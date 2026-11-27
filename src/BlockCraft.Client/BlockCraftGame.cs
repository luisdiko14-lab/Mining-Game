using System.Collections.Concurrent;
using System.Text.Json;
using BlockCraft.Core.Blocks;
using BlockCraft.Core.Items;
using BlockCraft.Core.Persistence;
using BlockCraft.Core.Player;
using BlockCraft.Core.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using XVector3 = Microsoft.Xna.Framework.Vector3;

namespace BlockCraft.Client;

internal sealed class BlockCraftGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly string _dataDirectory;
    private readonly string _settingsPath;
    private readonly string _worldDirectory;
    private readonly ConcurrentDictionary<ChunkPos, byte> _meshJobs = new();
    private readonly ConcurrentQueue<(ChunkPos Position, Chunk Chunk, int Revision, int InvalidationVersion, VertexPositionColorTexture[] Vertices)> _readyMeshes = new();
    private readonly Dictionary<ChunkPos, (int Revision, VertexPositionColorTexture[] Vertices)> _meshes = [];
    private readonly WorldClock _clock = new();
    private GameSettings _settings;
    private BlockRegistry _blocks = null!;
    private ItemRegistry _items = null!;
    private RecipeBook _recipes = null!;
    private Inventory? _inventory;
    private int _selectedRecipe;
    private SurvivalStats _survival = new();
    private AchievementTracker _achievements = new();
    private float _fallDistance;
    private ChunkManager? _world;
    private PlayerController? _player;
    private SpriteBatch _spriteBatch = null!;
    private Texture2D _pixel = null!;
    private Texture2D? _touchCircle;
    private BitmapFont _font = null!;
    private readonly VirtualJoystick _moveJoystick = new(NVector2.Zero, 80f);
    private bool _touchAvailable;
    private int? _touchLookPointer;
    private int? _touchJumpPointer;
    private int? _touchMinePointer;
    private int? _touchPlacePointer;
    private Vector2 _previousTouchLookPosition;
    private bool _touchJumpPressed;
    private bool _touchMinePressed;
    private bool _touchPlaceRequested;
    private BasicEffect _effect = null!;
    private SoundEffect? _blockSound;
    private SoundEffect? _musicEffect;
    private SoundEffectInstance? _musicInstance;
    private string? _currentMusic;
    private float _musicRefreshTimer;
    private float _rareDiscoveryMusicSeconds;
    private Texture2D? _blockAtlas;
    private readonly Dictionary<string, Rectangle> _atlasTiles = new(StringComparer.OrdinalIgnoreCase);
    private int _atlasWidth;
    private int _atlasHeight;
    private SoundEffectInstance? _ambientSound;
    private MouseState _previousMouse;
    private KeyboardState _previousKeyboard;
    private GameScreen _screen = GameScreen.MainMenu;
    private int _selectedHotbar;
    private NVector3 _spawnPosition;
    private readonly HashSet<ChunkPos> _knownLoadedChunks = [];
    private readonly Dictionary<ChunkPos, int> _meshInvalidationVersions = [];
    private Task? _chunkUnloadTask;
    private ChunkPos? _lastChunkUnloadCenter;
    private int _lastBreakX = int.MinValue;
    private int _lastBreakY = int.MinValue;
    private int _lastBreakZ = int.MinValue;
    private float _breakProgress;
    private bool _showDebug;
    private bool _isFullscreen;

    public BlockCraftGame()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(local, "BlockCraft");
        _dataDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "data");
        _settingsPath = Path.Combine(root, "settings.json");
        _worldDirectory = Path.Combine(root, "Worlds", "World-1");
        _settings = GameSettings.Load(_settingsPath);
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = _settings.Width,
            PreferredBackBufferHeight = _settings.Height,
            SynchronizeWithVerticalRetrace = _settings.VSync,
            IsFullScreen = _settings.Fullscreen
        };
        _isFullscreen = _settings.Fullscreen;
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        IsFixedTimeStep = false;
        Window.Title = "BlockCraft";
        Window.AllowUserResizing = true;
        Exiting += HandleExiting;
    }

    protected override void Initialize()
    {
        _blocks = BlockRegistry.LoadFromFile(Path.Combine(_dataDirectory, "blocks.json"));
        _items = ItemRegistry.LoadFromFile(Path.Combine(_dataDirectory, "items.json"));
        _recipes = RecipeBook.LoadFromFile(Path.Combine(_dataDirectory, "recipes.json"), _items);
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _touchCircle = CreateTouchCircle(GraphicsDevice, 128);
        try
        {
            _touchAvailable = TouchPanel.GetCapabilities().IsConnected;
        }
        catch (NotSupportedException)
        {
            _touchAvailable = false;
        }
        _font = new BitmapFont(GraphicsDevice);
        _effect = new BasicEffect(GraphicsDevice)
        {
            VertexColorEnabled = true,
            LightingEnabled = false,
            TextureEnabled = false,
            FogEnabled = false
        };
        LoadBlockAtlas();
        _effect.Texture = _blockAtlas;
        _effect.TextureEnabled = _blockAtlas is not null;
        LoadAudio();
    }

    private static Texture2D CreateTouchCircle(GraphicsDevice graphicsDevice, int size)
    {
        var texture = new Texture2D(graphicsDevice, size, size);
        var pixels = new Color[size * size];
        var center = (size - 1) * 0.5f;
        var radius = size * 0.5f - 2f;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
            var alpha = (byte)Math.Clamp((radius + 1f - distance) * 255f, 0f, 255f);
            pixels[y * size + x] = new Color(255, 255, 255, (int)alpha);
        }
        texture.SetData(pixels);
        return texture;
    }

    private void LoadBlockAtlas()
    {
        var atlasPath = Path.Combine(AppContext.BaseDirectory, "assets", "generated", "block_atlas.png");
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "assets", "generated", "atlas_manifest.json");
        if (!File.Exists(atlasPath) || !File.Exists(manifestPath)) return;
        using (var stream = File.OpenRead(atlasPath))
            _blockAtlas = Texture2D.FromStream(GraphicsDevice, stream);
        _atlasWidth = _blockAtlas.Width;
        _atlasHeight = _blockAtlas.Height;
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var tiles = document.RootElement.GetProperty("tiles");
        foreach (var property in tiles.EnumerateObject())
        {
            var tileX = property.Value.GetProperty("x").GetInt32();
            var tileY = property.Value.GetProperty("y").GetInt32();
            _atlasTiles[property.Name] = new Rectangle(tileX * 16, tileY * 16, 16, 16);
        }
    }

    private void UpdateMusic(float elapsedSeconds)
    {
        _rareDiscoveryMusicSeconds = Math.Max(0f, _rareDiscoveryMusicSeconds - Math.Max(0f, elapsedSeconds));
        _musicRefreshTimer -= Math.Max(0f, elapsedSeconds);
        if (_musicRefreshTimer > 0f)
            return;
        _musicRefreshTimer = 3f;

        var track = SelectMusicTrack();
        if (string.Equals(track, _currentMusic, StringComparison.OrdinalIgnoreCase))
            return;

        var path = Path.Combine(AppContext.BaseDirectory, "assets", "generated", "music", track + ".wav");
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Music track is missing: {path}");
            _musicRefreshTimer = 15f;
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var nextEffect = SoundEffect.FromStream(stream);
            var nextInstance = nextEffect.CreateInstance();
            nextInstance.IsLooped = true;
            nextInstance.Volume = Math.Clamp(_settings.MasterVolume * 0.34f, 0f, 1f);
            nextInstance.Play();

            _musicInstance?.Stop();
            _musicInstance?.Dispose();
            _musicEffect?.Dispose();
            _musicEffect = nextEffect;
            _musicInstance = nextInstance;
            _currentMusic = track;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"Could not load music '{track}': {exception.Message}");
            _musicRefreshTimer = 15f;
        }
    }

    private string SelectMusicTrack()
    {
        if (_screen == GameScreen.MainMenu || _player is null || _world is null)
            return "title_theme";
        if (_rareDiscoveryMusicSeconds > 0f)
            return "rare_discovery";

        var position = _player.Position;
        var y = (int)MathF.Floor(position.Y);
        var x = (int)MathF.Floor(position.X);
        var z = (int)MathF.Floor(position.Z);
        if (_world.GetBlock(x, y, z) == _blocks.GetId("water"))
            return "underwater_world";
        if (y <= 25 || _achievements.EchoOresMined >= 5)
            return "echo_chamber";
        if (y <= 39)
            return _achievements.AmethystBlocksMined > 0 ? "crystal_grotto" : "cave_depths";
        if (y < 55)
            return _achievements.FossilBlocksMined > 0 ? "abandoned_mineshaft" : "cave_depths";
        if (_clock.Daylight < 0.20f)
            return "surface_night";
        if (y >= 100)
            return "mountain_pass";

        var chunkX = ChunkManager.FloorDiv(x, Chunk.Width);
        var chunkZ = ChunkManager.FloorDiv(z, Chunk.Depth);
        if (!_world.TryGetChunk(new ChunkPos(chunkX, chunkZ), out var chunk) || chunk is null)
            return "plains_day";
        var biome = chunk.Biomes[ChunkManager.Mod(x, Chunk.Width), ChunkManager.Mod(z, Chunk.Depth)];
        return biome switch
        {
            BiomeKind.Forest => "forest_canopy",
            BiomeKind.Desert => "desert_wind",
            BiomeKind.Snow => "snowfall",
            BiomeKind.Ocean => "ocean_current",
            BiomeKind.Jungle => "jungle_mist",
            BiomeKind.Savanna => "savanna_wind",
            BiomeKind.Swamp => "swamp_lights",
            BiomeKind.CherryGrove => "cherry_grove",
            _ => "plains_day"
        };
    }

    private void LoadAudio()
    {
        var audioDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "generated");
        var blockPath = Path.Combine(audioDirectory, "block_break.wav");
        var ambientPath = Path.Combine(audioDirectory, "ambient_loop.wav");
        if (File.Exists(blockPath))
        {
            using var stream = File.OpenRead(blockPath);
            _blockSound = SoundEffect.FromStream(stream);
        }
        if (File.Exists(ambientPath))
        {
            using var stream = File.OpenRead(ambientPath);
            var sound = SoundEffect.FromStream(stream);
            _ambientSound = sound.CreateInstance();
            _ambientSound.IsLooped = true;
            _ambientSound.Volume = _settings.MasterVolume * 0.22f;
            _ambientSound.Play();
        }
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();
        var mouse = Mouse.GetState();
        var leftPressed = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        var rightPressed = mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released;

        if (keyboard.IsKeyDown(Keys.Escape) && !_previousKeyboard.IsKeyDown(Keys.Escape))
        {
            var wasGameOver = _screen == GameScreen.GameOver;
            _screen = _screen switch
            {
                GameScreen.Playing => GameScreen.Paused,
                GameScreen.Paused or GameScreen.Inventory or GameScreen.GameOver or GameScreen.Achievements => GameScreen.Playing,
                _ => GameScreen.MainMenu
            };
            if (wasGameOver) RespawnPlayer();
            IsMouseVisible = _screen != GameScreen.Playing;
        }

        if (_screen == GameScreen.MainMenu)
        {
            if (leftPressed && Button("NEW WORLD").Contains(mouse.Position))
                StartWorld();
            else if (leftPressed && Button("EXIT").Contains(mouse.Position))
                Exit();
        }
        else if (_screen == GameScreen.Paused)
        {
            if (leftPressed && Button("RESUME").Contains(mouse.Position))
            {
                _screen = GameScreen.Playing;
                IsMouseVisible = false;
                CenterMouse();
            }
            else if (leftPressed && Button("SAVE WORLD").Contains(mouse.Position))
                SaveWorld();
            else if (leftPressed && Button("MAIN MENU").Contains(mouse.Position))
            {
                SaveWorld();
                _screen = GameScreen.MainMenu;
                IsMouseVisible = true;
            }
            if (keyboard.IsKeyDown(Keys.PageUp) && !_previousKeyboard.IsKeyDown(Keys.PageUp))
                _settings.RenderDistance = Math.Clamp(_settings.RenderDistance + 1, 2, 12);
            if (keyboard.IsKeyDown(Keys.PageDown) && !_previousKeyboard.IsKeyDown(Keys.PageDown))
                _settings.RenderDistance = Math.Clamp(_settings.RenderDistance - 1, 2, 12);
            if (keyboard.IsKeyDown(Keys.OemPlus) && !_previousKeyboard.IsKeyDown(Keys.OemPlus))
                _settings.FieldOfView = Math.Clamp(_settings.FieldOfView + 5, 55, 110);
            if (keyboard.IsKeyDown(Keys.OemMinus) && !_previousKeyboard.IsKeyDown(Keys.OemMinus))
                _settings.FieldOfView = Math.Clamp(_settings.FieldOfView - 5, 55, 110);
            if (keyboard.IsKeyDown(Keys.F11) && !_previousKeyboard.IsKeyDown(Keys.F11))
                ToggleFullscreen();
            if (keyboard.IsKeyDown(Keys.V) && !_previousKeyboard.IsKeyDown(Keys.V))
                _settings.VSync = !_settings.VSync;
        }
        else if (_screen == GameScreen.Inventory)
        {
            if (keyboard.IsKeyDown(Keys.C) && !_previousKeyboard.IsKeyDown(Keys.C)) CraftSelectedRecipe();
            if (keyboard.IsKeyDown(Keys.Down) && !_previousKeyboard.IsKeyDown(Keys.Down))
                _selectedRecipe = (_selectedRecipe + 1) % Math.Max(1, _recipes.Count);
            if (keyboard.IsKeyDown(Keys.Up) && !_previousKeyboard.IsKeyDown(Keys.Up))
                _selectedRecipe = (_selectedRecipe - 1 + Math.Max(1, _recipes.Count)) % Math.Max(1, _recipes.Count);
            if (keyboard.IsKeyDown(Keys.E) && !_previousKeyboard.IsKeyDown(Keys.E))
            { _screen = GameScreen.Playing; IsMouseVisible = false; CenterMouse(); }
        }
        else if (_screen == GameScreen.Playing && _world is not null && _player is not null && _inventory is not null)
        {
            UpdatePlaying(gameTime, keyboard, mouse, rightPressed);
        }

        var frameSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _clock.Update(frameSeconds);
        UpdateMusic(frameSeconds);
        PumpMeshResults();
        UpdateChunkMeshes();
        _previousKeyboard = keyboard;
        _previousMouse = mouse;
        base.Update(gameTime);
    }

    private void StartWorld()
    {
        Directory.CreateDirectory(_worldDirectory);
        var seedPath = Path.Combine(_worldDirectory, "seed.txt");
        long seed;
        if (File.Exists(seedPath) && long.TryParse(File.ReadAllText(seedPath), out var savedSeed))
            seed = savedSeed;
        else
        {
            seed = Random.Shared.NextInt64(long.MinValue, long.MaxValue);
            File.WriteAllText(seedPath, seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var storage = new WorldStorage(_worldDirectory);
        var generator = new TerrainGenerator(seed, _blocks);
        _world = new ChunkManager(generator, storage);
        var spawnChunk = _world.RequestChunkAsync(new ChunkPos(0, 0)).GetAwaiter().GetResult();
        var surface = 64;
        for (var y = Chunk.Height - 2; y > 1; y--)
        {
            if (_blocks[spawnChunk.GetBlock(8, y, 8)].Solid)
            {
                surface = y;
                break;
            }
        }
        _spawnPosition = new NVector3(8.5f, surface + 1.05f, 8.5f);
        _player = new PlayerController(_blocks, _spawnPosition);
        _survival = new SurvivalStats();
        _fallDistance = 0f;
        _inventory = new Inventory(_items, 36);
        _inventory.Add(_items.GetId("stone"), 64);
        _inventory.Add(_items.GetId("dirt"), 64);
        _inventory.Add(_items.GetId("oak_planks"), 64);
        _inventory.Add(_items.GetId("oak_log"), 16);
        _inventory.Add(_items.GetId("glass"), 32);
        _inventory.Add(_items.GetId("torch"), 24);
        _inventory.Add(_items.GetId("wooden_pickaxe"), 1);
        _inventory.Add(_items.GetId("wooden_axe"), 1);
        _inventory.Add(_items.GetId("wooden_shovel"), 1);
        _inventory.Add(_items.GetId("bread"), 8);
        _selectedRecipe = 0;
        var playerSavePath = Path.Combine(_worldDirectory, "player.json");
        if (File.Exists(playerSavePath))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<PlayerSaveData>(File.ReadAllText(playerSavePath));
                if (saved is not null && IsValidPlayerSave(saved))
                {
                    var restoredInventory = new Inventory(_items, 36);
                    for (var index = 0; index < restoredInventory.Count; index++)
                        restoredInventory.Set(index, index < saved.Inventory.Length ? saved.Inventory[index] : ItemStack.Empty);

                    var restoredSurvival = new SurvivalStats();
                    if (saved.Health > 0f)
                    {
                        restoredSurvival.Restore(saved.Health, saved.Hunger, saved.Saturation);
                        _player.Teleport(new NVector3(saved.X, saved.Y, saved.Z));
                        _player.Yaw = MathF.IEEERemainder(saved.Yaw, MathF.Tau);
                        _player.Pitch = Math.Clamp(saved.Pitch, -1.52f, 1.52f);
                    }
                    else
                    {
                        _player.Teleport(_spawnPosition);
                        _player.Yaw = 0f;
                        _player.Pitch = 0f;
                    }

                    _inventory = restoredInventory;
                    _survival = restoredSurvival;
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or KeyNotFoundException or InvalidDataException)
            {
                Console.Error.WriteLine($"Player save could not be loaded: {exception.Message}");
            }
        }
        var achievementsPath = Path.Combine(_worldDirectory, "achievements.json");
        _achievements = new AchievementTracker();
        if (File.Exists(achievementsPath))
        {
            try
            {
                var progress = JsonSerializer.Deserialize<AchievementSnapshot>(File.ReadAllText(achievementsPath));
                if (progress is not null) _achievements.Restore(progress);
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or ArgumentException)
            {
                Console.Error.WriteLine($"Achievement progress could not be loaded: {exception.Message}");
            }
        }

        var playerChunkX = ChunkManager.FloorDiv((int)MathF.Floor(_player.Position.X), Chunk.Width);
        var playerChunkZ = ChunkManager.FloorDiv((int)MathF.Floor(_player.Position.Z), Chunk.Depth);
        for (var z = -1; z <= 1; z++)
        for (var x = -1; x <= 1; x++)
            _world.RequestChunkAsync(new ChunkPos(playerChunkX + x, playerChunkZ + z)).GetAwaiter().GetResult();

        _selectedHotbar = 0;
        _screen = GameScreen.Playing;
        IsMouseVisible = false;
        _meshes.Clear();
        _knownLoadedChunks.Clear();
        _meshInvalidationVersions.Clear();
        _lastChunkUnloadCenter = null;
        _chunkUnloadTask = null;
        while (_readyMeshes.TryDequeue(out _)) { }
        CenterMouse();
        RequestNearbyChunks();
    }

    private void RespawnPlayer()
    {
        if (_player is null) return;
        _player.Teleport(_spawnPosition);
        _player.Yaw = 0f;
        _player.Pitch = 0f;
        _survival = new SurvivalStats();
        _fallDistance = 0f;
        ResetBreakProgress();
        _touchJumpPressed = false;
        _touchMinePressed = false;
        _touchPlaceRequested = false;
        _screen = GameScreen.Playing;
        IsMouseVisible = false;
        CenterMouse();
    }

    private static bool IsValidPlayerSave(PlayerSaveData saved)
    {
        return saved.Inventory is not null && saved.Inventory.Length <= 36 &&
               float.IsFinite(saved.X) && float.IsFinite(saved.Y) && float.IsFinite(saved.Z) &&
               float.IsFinite(saved.Yaw) && float.IsFinite(saved.Pitch) &&
               float.IsFinite(saved.Health) && float.IsFinite(saved.Hunger) && float.IsFinite(saved.Saturation) &&
               saved.Health >= 0f && saved.Hunger >= 0f && saved.Saturation >= 0f &&
               MathF.Abs(saved.X) < 10_000_000f && MathF.Abs(saved.Z) < 10_000_000f &&
               saved.Y > 0f && saved.Y < Chunk.Height - 1;
    }

    private void UpdatePlaying(GameTime gameTime, KeyboardState keyboard, MouseState mouse, bool rightPressed)
    {
        var elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var touchLookDelta = UpdateTouchControls();
        var centerX = GraphicsDevice.Viewport.Width / 2;
        var centerY = GraphicsDevice.Viewport.Height / 2;
        if (_touchAvailable)
        {
            _player!.Yaw += touchLookDelta.X * 0.0024f;
            _player.Pitch = Math.Clamp(_player.Pitch - touchLookDelta.Y * 0.0024f, -1.52f, 1.52f);
        }
        else
        {
            _player!.Yaw += (mouse.X - centerX) * 0.0024f;
            _player.Pitch = Math.Clamp(_player.Pitch - (mouse.Y - centerY) * 0.0024f, -1.52f, 1.52f);
            CenterMouse();
        }

        var horizontal = (keyboard.IsKeyDown(Keys.D) ? 1f : 0f) - (keyboard.IsKeyDown(Keys.A) ? 1f : 0f);
        var vertical = (keyboard.IsKeyDown(Keys.W) ? 1f : 0f) - (keyboard.IsKeyDown(Keys.S) ? 1f : 0f);
        if (_touchAvailable)
        {
            horizontal = Math.Clamp(horizontal + _moveJoystick.Axis.X, -1f, 1f);
            vertical = Math.Clamp(vertical + _moveJoystick.Axis.Y, -1f, 1f);
        }
        var wasGrounded = _player.IsGrounded;
        var previousY = _player.Position.Y;
        var sprinting = keyboard.IsKeyDown(Keys.LeftShift);
        _player.Update(_world!, new NVector2(horizontal, vertical), elapsed,
            keyboard.IsKeyDown(Keys.Space) || _touchJumpPressed, sprinting, keyboard.IsKeyDown(Keys.LeftControl));
        if (!_player.IsFlying && !wasGrounded && _player.Position.Y < previousY)
            _fallDistance += previousY - _player.Position.Y;
        if (!_player.IsFlying && !wasGrounded && _player.IsGrounded)
        {
            _survival.ApplyFallDamage(_fallDistance);
            _fallDistance = 0f;
        }
        _survival.Update(elapsed, horizontal != 0f || vertical != 0f, sprinting);
        var visitedChunkX = ChunkManager.FloorDiv((int)MathF.Floor(_player.Position.X), Chunk.Width);
        var visitedChunkZ = ChunkManager.FloorDiv((int)MathF.Floor(_player.Position.Z), Chunk.Depth);
        _achievements.RecordChunkVisit(visitedChunkX, visitedChunkZ);
        _achievements.RecordDepth((int)MathF.Floor(_player.Position.Y));
        if (!_survival.IsAlive)
        {
            _screen = GameScreen.GameOver;
            IsMouseVisible = true;
            SaveWorld();
            return;
        }

        if (keyboard.IsKeyDown(Keys.F) && !_previousKeyboard.IsKeyDown(Keys.F))
            EatSelectedFood();

        if (keyboard.IsKeyDown(Keys.F5) && !_previousKeyboard.IsKeyDown(Keys.F5))
            _player.IsFlying = !_player.IsFlying;
        if (keyboard.IsKeyDown(Keys.F3) && !_previousKeyboard.IsKeyDown(Keys.F3))
            _showDebug = !_showDebug;
        if (keyboard.IsKeyDown(Keys.J) && !_previousKeyboard.IsKeyDown(Keys.J))
        {
            _screen = GameScreen.Achievements;
            IsMouseVisible = true;
        }
        if (keyboard.IsKeyDown(Keys.F2) && !_previousKeyboard.IsKeyDown(Keys.F2))
            SaveScreenshot();
        if (keyboard.IsKeyDown(Keys.E) && !_previousKeyboard.IsKeyDown(Keys.E))
        {
            _screen = GameScreen.Inventory;
            IsMouseVisible = true;
        }
        if (keyboard.IsKeyDown(Keys.F11) && !_previousKeyboard.IsKeyDown(Keys.F11))
            ToggleFullscreen();

        for (var slot = 0; slot < 9; slot++)
            if (keyboard.IsKeyDown((Keys)((int)Keys.D1 + slot)) && !_previousKeyboard.IsKeyDown((Keys)((int)Keys.D1 + slot)))
                _selectedHotbar = slot;
        if (mouse.ScrollWheelValue != _previousMouse.ScrollWheelValue)
            _selectedHotbar = (9 + _selectedHotbar + Math.Sign(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue)) % 9;

        var origin = _player.Position + new NVector3(0, 1.62f, 0);
        var direction = new NVector3(
            -MathF.Sin(_player.Yaw) * MathF.Cos(_player.Pitch),
            MathF.Sin(_player.Pitch),
            -MathF.Cos(_player.Yaw) * MathF.Cos(_player.Pitch));
        var hit = BlockRaycaster.Cast(_world!, _blocks, origin, direction, 6f);

        if ((mouse.LeftButton == ButtonState.Pressed || _touchMinePressed) && hit is not null)
            MineBlock(hit.Value, elapsed);
        else
            ResetBreakProgress();

        if ((rightPressed || _touchPlaceRequested) && hit is not null)
            PlaceBlock(hit.Value);
        RequestNearbyChunks();
    }

    private NVector2 UpdateTouchControls()
    {
        _touchPlaceRequested = false;
        if (!_touchAvailable)
            return NVector2.Zero;

        var width = GraphicsDevice.Viewport.Width;
        var height = GraphicsDevice.Viewport.Height;
        var layout = GetTouchLayout(width, height);
        _moveJoystick.Configure(ToNumerics(layout.JoystickCenter), layout.JoystickRadius);

        var activePointers = new HashSet<int>();
        var lookDelta = NVector2.Zero;
        foreach (var touch in TouchPanel.GetState())
        {
            if (touch.State is TouchLocationState.Pressed or TouchLocationState.Moved)
                activePointers.Add(touch.Id);

            if (touch.State == TouchLocationState.Pressed)
            {
                if (_moveJoystick.TryBegin(touch.Id, ToNumerics(touch.Position)))
                    continue;

                if (IsInside(touch.Position, layout.JumpCenter, layout.ButtonRadius))
                {
                    _touchJumpPointer = touch.Id;
                    _touchJumpPressed = true;
                }
                else if (IsInside(touch.Position, layout.MineCenter, layout.ButtonRadius))
                {
                    _touchMinePointer = touch.Id;
                    _touchMinePressed = true;
                }
                else if (IsInside(touch.Position, layout.PlaceCenter, layout.ButtonRadius))
                {
                    _touchPlacePointer = touch.Id;
                    _touchPlaceRequested = true;
                }
                else if (touch.Position.X > width * 0.42f && touch.Position.Y < height - layout.JoystickRadius * 3.1f)
                {
                    _touchLookPointer = touch.Id;
                    _previousTouchLookPosition = touch.Position;
                }
            }
            else if (touch.State == TouchLocationState.Moved)
            {
                if (_moveJoystick.UpdatePointer(touch.Id, ToNumerics(touch.Position)))
                    continue;

                if (_touchLookPointer == touch.Id)
                {
                    var delta = touch.Position - _previousTouchLookPosition;
                    lookDelta += new NVector2(delta.X, delta.Y);
                    _previousTouchLookPosition = touch.Position;
                }
            }
            else if (touch.State == TouchLocationState.Released)
            {
                _moveJoystick.Release(touch.Id);
                if (_touchLookPointer == touch.Id) _touchLookPointer = null;
                if (_touchJumpPointer == touch.Id)
                {
                    _touchJumpPointer = null;
                    _touchJumpPressed = false;
                }
                if (_touchMinePointer == touch.Id)
                {
                    _touchMinePointer = null;
                    _touchMinePressed = false;
                }
                if (_touchPlacePointer == touch.Id) _touchPlacePointer = null;
            }
        }

        if (_moveJoystick.PointerId is int joystickId && !activePointers.Contains(joystickId))
            _moveJoystick.Reset();
        if (_touchLookPointer is int lookId && !activePointers.Contains(lookId))
            _touchLookPointer = null;
        if (_touchJumpPointer is int jumpId && !activePointers.Contains(jumpId))
        {
            _touchJumpPointer = null;
            _touchJumpPressed = false;
        }
        if (_touchMinePointer is int mineId && !activePointers.Contains(mineId))
        {
            _touchMinePointer = null;
            _touchMinePressed = false;
        }
        if (_touchPlacePointer is int placeId && !activePointers.Contains(placeId))
            _touchPlacePointer = null;

        return lookDelta;
    }

    private static bool IsInside(Vector2 point, Vector2 center, float radius) =>
        Vector2.DistanceSquared(point, center) <= radius * radius;

    private static NVector2 ToNumerics(Vector2 point) => new(point.X, point.Y);

    private static TouchLayout GetTouchLayout(int width, int height)
    {
        var radius = Math.Clamp(Math.Min(width, height) * 0.105f, 54f, 90f);
        var buttonRadius = radius * 0.66f;
        return new TouchLayout(
            new Vector2(radius + 22f, height - radius - 22f),
            new Vector2(width - radius * 1.05f, height - radius - 22f),
            new Vector2(width - radius * 2.9f, height - radius - 22f),
            new Vector2(width - radius * 1.95f, height - radius * 2.65f),
            radius,
            buttonRadius);
    }

    private void DrawTouchControls(int width, int height)
    {
        if (!_touchAvailable || _touchCircle is null)
            return;
        var layout = GetTouchLayout(width, height);
        DrawTouchCircle(layout.JoystickCenter, layout.JoystickRadius,
            _moveJoystick.IsActive ? new Color(45, 130, 210, 95) : new Color(16, 28, 46, 115));
        DrawTouchCircle(ToXna2(_moveJoystick.KnobPosition), layout.JoystickRadius * 0.43f,
            _moveJoystick.IsActive ? new Color(205, 230, 255, 210) : new Color(174, 199, 227, 145));
        DrawTouchButton("JUMP", layout.JumpCenter, layout.ButtonRadius, _touchJumpPressed,
            new Color(83, 151, 218, 160));
        DrawTouchButton("MINE", layout.MineCenter, layout.ButtonRadius, _touchMinePressed,
            new Color(222, 111, 88, 160));
        DrawTouchButton("PLACE", layout.PlaceCenter, layout.ButtonRadius, _touchPlacePointer.HasValue,
            new Color(91, 178, 133, 160));
    }

    private void DrawTouchCircle(Vector2 center, float radius, Color color)
    {
        if (_touchCircle is null) return;
        var bounds = new Rectangle((int)(center.X - radius), (int)(center.Y - radius),
            (int)(radius * 2), (int)(radius * 2));
        _spriteBatch.Draw(_touchCircle, bounds, color);
    }

    private void DrawTouchButton(string label, Vector2 center, float radius, bool pressed, Color color)
    {
        DrawTouchCircle(center, radius, pressed ? Color.White : color);
        var scale = 1.05f;
        _font.Draw(_spriteBatch, label,
            new Vector2(center.X - label.Length * 3f * scale, center.Y - 4f * scale),
            pressed ? new Color(15, 28, 44) : Color.White, scale);
    }

    private static Vector2 ToXna2(NVector2 vector) => new(vector.X, vector.Y);

    private void EatSelectedFood()
    {
        if (_inventory is null)
            return;
        var stack = _inventory[_selectedHotbar];
        if (stack.IsEmpty)
            return;
        var item = _items[stack.ItemId];
        if (item.FoodValue <= 0 || !_survival.ConsumeFood(item.FoodValue))
            return;
        _inventory.Set(_selectedHotbar, stack.Count == 1 ? ItemStack.Empty : stack with { Count = stack.Count - 1 });
    }

    private void MineBlock(RayHit hit, float elapsed)
    {
        var position = hit.Block;
        var id = _world!.GetBlock(position.X, position.Y, position.Z);
        if (id == 0) return;
        var definition = _blocks[id];
        if (definition.Hardness <= 0) return;
        if (position.X != _lastBreakX || position.Y != _lastBreakY || position.Z != _lastBreakZ)
        {
            _lastBreakX = position.X; _lastBreakY = position.Y; _lastBreakZ = position.Z;
            _breakProgress = 0;
        }
        var speed = 1f;
        var stack = _inventory![_selectedHotbar];
        ItemDefinition? selectedTool = null;
        if (!stack.IsEmpty)
        {
            selectedTool = _items[stack.ItemId];
            if (selectedTool.ToolType.Equals(definition.RequiredTool, StringComparison.OrdinalIgnoreCase))
                speed = Math.Max(1f, selectedTool.MiningSpeed);
        }
        var canHarvest = definition.CanHarvestWith(
            selectedTool?.ToolType ?? "none", selectedTool?.ToolTier ?? "none");
        _breakProgress += elapsed * speed / Math.Max(0.15f, definition.Hardness * 0.62f);
        if (_breakProgress < 1f) return;
        if (_world.SetBlock(position.X, position.Y, position.Z, 0))
        {
            InvalidateBoundaryNeighborMeshes(position);
            _achievements.RecordMine(definition.Name, position.Y);
            if (definition.Name is "diamond_ore" or "diamond_ore_deepslate" or "amethyst_block" or "echo_ore" or "fossil_block")
                _rareDiscoveryMusicSeconds = 22f;
            if (definition.DropItemId != 0 && canHarvest)
                _inventory.Add(definition.DropItemId, 1);
            _blockSound?.Play(_settings.MasterVolume, 0, 0);
        }
        ResetBreakProgress();
    }

    private void PlaceBlock(RayHit hit)
    {
        var stack = _inventory![_selectedHotbar];
        if (stack.IsEmpty) return;
        var item = _items[stack.ItemId];
        if (item.PlacesBlockId == 0) return;
        var place = hit.Previous;
        var feet = _player!.Position;
        if (place.Y >= feet.Y && place.Y <= feet.Y + 1.8f &&
            MathF.Abs(place.X + 0.5f - feet.X) < 0.8f && MathF.Abs(place.Z + 0.5f - feet.Z) < 0.8f)
            return;
        var existingBlock = _world!.GetBlock(place.X, place.Y, place.Z);
        if (existingBlock != 0 && _blocks[existingBlock].Solid) return;
        if (_world.SetBlock(place.X, place.Y, place.Z, item.PlacesBlockId))
        {
            InvalidateBoundaryNeighborMeshes(place);
            _inventory.Set(_selectedHotbar, stack.Count == 1 ? ItemStack.Empty : stack with { Count = stack.Count - 1 });
            _achievements.RecordPlace();
            _blockSound?.Play(_settings.MasterVolume * 0.55f, -0.15f, 0);
        }
    }

    private void CraftSelectedRecipe()
    {
        if (_inventory is null || _recipes.Count == 0) return;
        _recipes.TryCraftFromInventory(_inventory, _selectedRecipe);
    }

    private string RecipeIngredientSummary(CraftingRecipe recipe) => string.Join(" + ",
        recipe.RequiredItems().Select(pair => $"{pair.Value} {_items[pair.Key].Name.Replace('_', ' ').ToUpperInvariant()}"));

    private void ResetBreakProgress()
    {
        _breakProgress = 0;
        _lastBreakX = int.MinValue;
        _lastBreakY = int.MinValue;
        _lastBreakZ = int.MinValue;
    }

    private void RequestNearbyChunks()
    {
        if (_world is null || _player is null) return;
        var chunkX = ChunkManager.FloorDiv((int)MathF.Floor(_player.Position.X), Chunk.Width);
        var chunkZ = ChunkManager.FloorDiv((int)MathF.Floor(_player.Position.Z), Chunk.Depth);
        var center = new ChunkPos(chunkX, chunkZ);
        var radius = Math.Min(_settings.RenderDistance, 6);
        for (var z = -radius; z <= radius; z++)
        for (var x = -radius; x <= radius; x++)
            _ = _world.RequestChunkAsync(new ChunkPos(chunkX + x, chunkZ + z));

        if ((_chunkUnloadTask is null || _chunkUnloadTask.IsCompleted) && _lastChunkUnloadCenter != center)
        {
            _lastChunkUnloadCenter = center;
            _chunkUnloadTask = UnloadFarChunksAsync(_world, center, radius + 1);
        }
    }

    private async Task UnloadFarChunksAsync(ChunkManager world, ChunkPos center, int keepRadius)
    {
        try
        {
            await world.UnloadOutsideAsync(center, keepRadius).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Far chunk cleanup failed: {exception}");
            _lastChunkUnloadCenter = null;
        }
    }

    private void UpdateChunkMeshes()
    {
        if (_world is null) return;
        var chunks = _world.LoadedChunks;
        var currentPositions = chunks.Select(chunk => new ChunkPos(chunk.ChunkX, chunk.ChunkZ)).ToHashSet();
        foreach (var added in currentPositions.Except(_knownLoadedChunks))
            InvalidateNeighborMeshes(added);
        foreach (var removed in _knownLoadedChunks.Except(currentPositions))
        {
            _meshes.Remove(removed);
            _meshInvalidationVersions.Remove(removed);
            InvalidateNeighborMeshes(removed);
        }
        foreach (var stale in _meshes.Keys.Where(position => !currentPositions.Contains(position)).ToArray())
            _meshes.Remove(stale);
        _knownLoadedChunks.Clear();
        _knownLoadedChunks.UnionWith(currentPositions);

        var world = _world;
        var daylight = _clock.Daylight;
        foreach (var chunk in chunks)
        {
            var position = new ChunkPos(chunk.ChunkX, chunk.ChunkZ);
            if (_meshes.TryGetValue(position, out var existing) && existing.Revision == chunk.Revision)
                continue;
            if (!_meshJobs.TryAdd(position, 0)) continue;
            var sourceChunk = chunk;
            var sourceRevision = sourceChunk.Revision;
            var sourceInvalidationVersion = _meshInvalidationVersions.GetValueOrDefault(position);
            _ = Task.Run(() =>
            {
                try
                {
                    var quads = GreedyMesher.Build(
                        (x, y, z) => world.GetBlock(chunk.ChunkX * Chunk.Width + x, y, chunk.ChunkZ * Chunk.Depth + z),
                        _blocks);
                    var lighting = ChunkLighting.Calculate(sourceChunk, _blocks,
                        (x, y, z) => world.GetBlock(chunk.ChunkX * Chunk.Width + x, y, chunk.ChunkZ * Chunk.Depth + z));
                    var vertices = new List<VertexPositionColorTexture>(quads.Count * 6);
                    foreach (var quad in quads)
                    {
                        var baseColor = BlockColor(quad.BlockId);
                        var center = (quad.P0 + quad.P1 + quad.P2 + quad.P3) * 0.25f;
                        var normalAxis = quad.Normal.X != 0 ? 0 : quad.Normal.Y != 0 ? 1 : 2;
                        var normalSign = (int)MathF.Sign(normalAxis == 0 ? quad.Normal.X : normalAxis == 1 ? quad.Normal.Y : quad.Normal.Z);
                        var blockX = (int)MathF.Floor(center.X - quad.Normal.X * 0.5f);
                        var blockY = (int)MathF.Floor(center.Y - quad.Normal.Y * 0.5f);
                        var blockZ = (int)MathF.Floor(center.Z - quad.Normal.Z * 0.5f);
                        var lightX = (int)MathF.Floor(center.X + quad.Normal.X * 0.1f);
                        var lightY = (int)MathF.Floor(center.Y + quad.Normal.Y * 0.1f);
                        var lightZ = (int)MathF.Floor(center.Z + quad.Normal.Z * 0.1f);
                        var sun = lighting.GetSunlight(lightX, lightY, lightZ) / 15f * daylight;
                        var emitted = lighting.GetBlockLight(lightX, lightY, lightZ) / 15f;
                        var light = Math.Clamp(Math.Max(sun, emitted), 0.10f, 1f);
                        var faceShade = normalAxis == 1 ? (normalSign > 0 ? 1f : 0.58f) : 0.82f;
                        var uAxis = (normalAxis + 1) % 3;
                        var vAxis = (normalAxis + 2) % 3;
                        var ao00 = lighting.GetAmbientOcclusion(blockX, blockY, blockZ, normalAxis, normalSign, uAxis, vAxis, -1, -1);
                        var ao10 = lighting.GetAmbientOcclusion(blockX, blockY, blockZ, normalAxis, normalSign, uAxis, vAxis, 1, -1);
                        var ao11 = lighting.GetAmbientOcclusion(blockX, blockY, blockZ, normalAxis, normalSign, uAxis, vAxis, 1, 1);
                        var ao01 = lighting.GetAmbientOcclusion(blockX, blockY, blockZ, normalAxis, normalSign, uAxis, vAxis, -1, 1);
                        var color00 = ShadeColor(baseColor, light, faceShade, ao00);
                        var color10 = ShadeColor(baseColor, light, faceShade, ao10);
                        var color11 = ShadeColor(baseColor, light, faceShade, ao11);
                        var color01 = ShadeColor(baseColor, light, faceShade, ao01);
                        var p0 = ToXna(quad.P0); var p1 = ToXna(quad.P1);
                        var p2 = ToXna(quad.P2); var p3 = ToXna(quad.P3);
                        var texture = GetTextureRectangle(quad.BlockId, quad.Normal);
                        var u0 = texture.Width == 0 ? 0f : (texture.X + 0.5f) / _atlasWidth;
                        var u1 = texture.Width == 0 ? 1f : (texture.Right - 0.5f) / _atlasWidth;
                        var v0 = texture.Height == 0 ? 0f : (texture.Y + 0.5f) / _atlasHeight;
                        var v1 = texture.Height == 0 ? 1f : (texture.Bottom - 0.5f) / _atlasHeight;
                        var uv0 = new Vector2(u0, v1);
                        var uv1 = new Vector2(u1, v1);
                        var uv2 = new Vector2(u1, v0);
                        var uv3 = new Vector2(u0, v0);
                        vertices.Add(new VertexPositionColorTexture(p0, color00, uv0));
                        vertices.Add(new VertexPositionColorTexture(p1, color10, uv1));
                        vertices.Add(new VertexPositionColorTexture(p2, color11, uv2));
                        vertices.Add(new VertexPositionColorTexture(p0, color00, uv0));
                        vertices.Add(new VertexPositionColorTexture(p2, color11, uv2));
                        vertices.Add(new VertexPositionColorTexture(p3, color01, uv3));
                    }
                    _readyMeshes.Enqueue((position, sourceChunk, sourceRevision, sourceInvalidationVersion, vertices.ToArray()));
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"Chunk mesh failed at {position}: {exception}");
                }
                finally
                {
                    _meshJobs.TryRemove(position, out _);
                }
            });
        }
    }

    private void PumpMeshResults()
    {
        while (_readyMeshes.TryDequeue(out var mesh))
        {
            if (_world is not null &&
                _world.TryGetChunk(mesh.Position, out var current) &&
                current is not null &&
                ReferenceEquals(current, mesh.Chunk) &&
                current.Revision == mesh.Revision &&
                _meshInvalidationVersions.GetValueOrDefault(mesh.Position) == mesh.InvalidationVersion)
                _meshes[mesh.Position] = (mesh.Revision, mesh.Vertices);
        }
    }

    private void InvalidateNeighborMeshes(ChunkPos position)
    {
        InvalidateChunkMesh(new ChunkPos(position.X - 1, position.Z));
        InvalidateChunkMesh(new ChunkPos(position.X + 1, position.Z));
        InvalidateChunkMesh(new ChunkPos(position.X, position.Z - 1));
        InvalidateChunkMesh(new ChunkPos(position.X, position.Z + 1));
    }

    private void InvalidateChunkMesh(ChunkPos position)
    {
        _meshInvalidationVersions[position] = _meshInvalidationVersions.GetValueOrDefault(position) + 1;
        if (_meshes.TryGetValue(position, out var mesh))
            _meshes[position] = (-1, mesh.Vertices);
    }

    private void InvalidateBoundaryNeighborMeshes(BlockPos block)
    {
        var chunkX = ChunkManager.FloorDiv(block.X, Chunk.Width);
        var chunkZ = ChunkManager.FloorDiv(block.Z, Chunk.Depth);
        var localX = ChunkManager.Mod(block.X, Chunk.Width);
        var localZ = ChunkManager.Mod(block.Z, Chunk.Depth);
        if (localX == 0) InvalidateChunkMesh(new ChunkPos(chunkX - 1, chunkZ));
        if (localX == Chunk.Width - 1) InvalidateChunkMesh(new ChunkPos(chunkX + 1, chunkZ));
        if (localZ == 0) InvalidateChunkMesh(new ChunkPos(chunkX, chunkZ - 1));
        if (localZ == Chunk.Depth - 1) InvalidateChunkMesh(new ChunkPos(chunkX, chunkZ + 1));
    }

    private static Color ShadeColor(Color color, float light, float faceShade, byte ambientOcclusion)
    {
        var aoFactor = 0.45f + 0.55f * ambientOcclusion / 3f;
        var factor = Math.Clamp(light * faceShade * aoFactor, 0f, 1f);
        return new Color((byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor), color.A);
    }

    private Color BlockColor(ushort id) => _blocks[id].Name switch
    {
        "stone" or "deepslate" => new Color(112, 116, 126),
        "dirt" => new Color(126, 82, 52),
        "grass_block" => new Color(88, 164, 76),
        "sand" or "sandstone" => new Color(216, 194, 126),
        "oak_log" or "birch_log" or "spruce_log" => new Color(133, 91, 52),
        "oak_leaves" or "birch_leaves" or "spruce_leaves" => new Color(58, 132, 65, 230),
        "water" or "ice" => new Color(68, 135, 224, 155),
        "lava" => new Color(255, 104, 28, 255),
        "coal_ore" => new Color(66, 67, 72),
        "iron_ore" => new Color(188, 130, 99),
        "gold_ore" => new Color(218, 181, 54),
        "diamond_ore" => new Color(67, 210, 204),
        "glass" => new Color(173, 218, 228, 110),
        "snow_block" => new Color(230, 237, 245),
        "bedrock" => new Color(57, 59, 66),
        "oak_planks" => new Color(174, 127, 76),
        "glowstone" or "torch" => new Color(255, 213, 116),
        "cobblestone" => new Color(105, 106, 108),
        _ => new Color(155, 155, 155)
    };

    private Rectangle GetTextureRectangle(ushort blockId, NVector3 normal)
    {
        var definition = _blocks[blockId];
        var key = normal.Y > 0
            ? definition.Textures.GetValueOrDefault("top")
            : normal.Y < 0
                ? definition.Textures.GetValueOrDefault("bottom")
                : definition.Textures.GetValueOrDefault("side");
        key ??= definition.Textures.GetValueOrDefault("all");
        return key is not null && _atlasTiles.TryGetValue(key, out var rectangle) ? rectangle : Rectangle.Empty;
    }

    private void DrawWorld()
    {
        if (_player is null) return;
        var camera = _player.Position + new NVector3(0, 1.62f, 0);
        var look = new NVector3(-MathF.Sin(_player.Yaw) * MathF.Cos(_player.Pitch),
            MathF.Sin(_player.Pitch), -MathF.Cos(_player.Yaw) * MathF.Cos(_player.Pitch));
        var view = Matrix.CreateLookAt(ToXna(camera), ToXna(camera + look), XVector3.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(_settings.FieldOfView),
            GraphicsDevice.Viewport.AspectRatio, 0.05f, 600f);
        var frustum = new BoundingFrustum(view * projection);
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        GraphicsDevice.BlendState = BlendState.AlphaBlend;
        GraphicsDevice.SamplerStates[0] = SamplerState.PointClamp;
        _effect.View = view;
        _effect.Projection = projection;
        _effect.DiffuseColor = new XVector3(_clock.Daylight, _clock.Daylight, _clock.Daylight);
        foreach (var (position, mesh) in _meshes)
        {
            if (mesh.Vertices.Length == 0) continue;
            var x = position.X * Chunk.Width;
            var z = position.Z * Chunk.Depth;
            var bounds = new BoundingBox(new XVector3(x, 0, z), new XVector3(x + 16, 256, z + 16));
            if (frustum.Contains(bounds) == ContainmentType.Disjoint) continue;
            _effect.World = Matrix.CreateTranslation(x, 0, z);
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, mesh.Vertices, 0, mesh.Vertices.Length / 3);
            }
        }
    }

    protected override void Draw(GameTime gameTime)
    {
        var sky = Color.Lerp(new Color(7, 12, 24), new Color(85, 154, 218), _clock.Daylight);
        GraphicsDevice.Clear(sky);
        if (_screen is GameScreen.Playing or GameScreen.Paused or GameScreen.Inventory or GameScreen.GameOver)
            DrawWorld();
        DrawInterface();
        base.Draw(gameTime);
    }

    private void DrawInterface()
    {
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        var width = GraphicsDevice.Viewport.Width;
        var height = GraphicsDevice.Viewport.Height;

        if (_screen == GameScreen.MainMenu)
        {
            DrawPanel(new Rectangle(width / 2 - 235, height / 2 - 180, 470, 360), new Color(11, 17, 30, 235));
            _font.Draw(_spriteBatch, "BLOCKCRAFT", new Vector2(width / 2 - 160, height / 2 - 125), Color.White, 4);
            _font.Draw(_spriteBatch, "A VOXEL WORLD TO MAKE YOURS", new Vector2(width / 2 - 155, height / 2 - 58), new Color(145, 183, 224), 1.4f);
            DrawButton("NEW WORLD", Button("NEW WORLD"), width, height);
            DrawButton("EXIT", Button("EXIT"), width, height);
            _font.Draw(_spriteBatch, "ORIGINAL PROCEDURAL WORLD", new Vector2(width / 2 - 118, height / 2 + 135), new Color(127, 144, 168), 1.2f);
        }
        else if (_screen == GameScreen.Paused)
        {
            DrawPanel(new Rectangle(width / 2 - 210, height / 2 - 190, 420, 385), new Color(10, 15, 27, 230));
            _font.Draw(_spriteBatch, "PAUSED", new Vector2(width / 2 - 84, height / 2 - 150), Color.White, 3);
            DrawButton("RESUME", Button("RESUME"), width, height);
            DrawButton("SAVE WORLD", Button("SAVE WORLD"), width, height);
            DrawButton("MAIN MENU", Button("MAIN MENU"), width, height);
            _font.Draw(_spriteBatch, $"RENDER DISTANCE: {_settings.RenderDistance}  [PGUP / PGDN]", new Vector2(width / 2 - 180, height / 2 + 105), new Color(176, 191, 211), 1.1f);
            _font.Draw(_spriteBatch, $"FOV: {_settings.FieldOfView:0}  [+ / -]   VSYNC: {(_settings.VSync ? "ON" : "OFF")}", new Vector2(width / 2 - 180, height / 2 + 126), new Color(176, 191, 211), 1.1f);
        }
        else if (_screen == GameScreen.Playing)
        {
            DrawHotbar(width, height);
            DrawSurvivalMeters(height);
            DrawTouchControls(width, height);
            _font.Draw(_spriteBatch, "J: JOURNAL", new Vector2(width - 126, 18), new Color(220, 235, 197), 1.0f);
            DrawRect(new Rectangle(width / 2 - 1, height / 2 - 9, 2, 18), Color.White);
            DrawRect(new Rectangle(width / 2 - 9, height / 2 - 1, 18, 2), Color.White);
            if (_showDebug && _player is not null && _world is not null)
            {
                DrawPanel(new Rectangle(12, 12, 350, 100), new Color(0, 0, 0, 160));
                var chunkX = ChunkManager.FloorDiv((int)_player.Position.X, Chunk.Width);
                var chunkZ = ChunkManager.FloorDiv((int)_player.Position.Z, Chunk.Depth);
                _font.Draw(_spriteBatch, $"XYZ: {_player.Position.X:0.0} {_player.Position.Y:0.0} {_player.Position.Z:0.0}", new Vector2(22, 22), Color.White, 1.2f);
                _font.Draw(_spriteBatch, $"CHUNK: {chunkX} {chunkZ}  LOADED: {_world.LoadedChunks.Count}", new Vector2(22, 44), Color.White, 1.2f);
                _font.Draw(_spriteBatch, $"DAYLIGHT: {_clock.Daylight:0.00}  FLY: {_player.IsFlying}", new Vector2(22, 66), Color.White, 1.2f);
            }
            if (_breakProgress > 0)
            {
                DrawRect(new Rectangle(width / 2 - 50, height / 2 + 22, 100, 5), new Color(15, 18, 22, 210));
                DrawRect(new Rectangle(width / 2 - 50, height / 2 + 22, (int)(100 * Math.Clamp(_breakProgress, 0, 1)), 5), new Color(244, 194, 72));
            }
        }
        else if (_screen == GameScreen.GameOver)
        {
            DrawPanel(new Rectangle(width / 2 - 220, height / 2 - 100, 440, 200), new Color(10, 12, 20, 235));
            _font.Draw(_spriteBatch, "YOU DIED", new Vector2(width / 2 - 88, height / 2 - 45), new Color(238, 111, 102), 3);
            _font.Draw(_spriteBatch, "WORLD SAVED  -  PRESS ESC", new Vector2(width / 2 - 125, height / 2 + 25), Color.White, 1.5f);
        }
        else if (_screen == GameScreen.Achievements)
        {
            DrawAchievementJournal(width, height);
        }
        else if (_screen == GameScreen.Inventory && _inventory is not null)
        {
            DrawPanel(new Rectangle(width / 2 - 260, height / 2 - 210, 520, 420), new Color(10, 16, 28, 238));
            _font.Draw(_spriteBatch, "INVENTORY", new Vector2(width / 2 - 80, height / 2 - 185), Color.White, 2.5f);
            DrawInventorySlots(width, height);
            if (_recipes.Count > 0)
            {
                var recipe = _recipes.Recipes[_selectedRecipe];
                var resultName = _items[recipe.Result.ItemId].Name.Replace('_', ' ').ToUpperInvariant();
                _font.Draw(_spriteBatch, $"RECIPE {_selectedRecipe + 1}/{_recipes.Count}: {resultName} X{recipe.Result.Count}",
                    new Vector2(width / 2 - 225, height / 2 + 119), new Color(246, 207, 119), 1.05f);
                var needs = RecipeIngredientSummary(recipe);
                _font.Draw(_spriteBatch, needs.Length > 54 ? needs[..54] : needs,
                    new Vector2(width / 2 - 225, height / 2 + 140), new Color(177, 194, 218), 1.0f);
            }
            _font.Draw(_spriteBatch, "UP/DOWN: SELECT RECIPE   C: QUICK CRAFT", new Vector2(width / 2 - 178, height / 2 + 164), new Color(177, 194, 218), 1.0f);
            _font.Draw(_spriteBatch, "E OR ESC: RETURN", new Vector2(width / 2 - 78, height / 2 + 186), new Color(137, 155, 179), 1.0f);
        }
        _spriteBatch.End();
    }

    private void DrawAchievementJournal(int width, int height)
    {
        var panelWidth = Math.Min(760, width - 28);
        var panelHeight = Math.Min(520, height - 28);
        var panel = new Rectangle((width - panelWidth) / 2, (height - panelHeight) / 2, panelWidth, panelHeight);
        DrawPanel(panel, new Color(9, 17, 27, 245));
        _font.Draw(_spriteBatch, "WORLD JOURNAL", new Vector2(panel.X + 28, panel.Y + 22), new Color(231, 245, 220), 2.5f);
        _font.Draw(_spriteBatch, "EXPLORATION MILESTONES", new Vector2(panel.X + 30, panel.Y + 59), new Color(133, 206, 112), 1.05f);
        var achievements = _achievements.GetAchievements();
        var rowHeight = Math.Max(34, (panel.Height - 125) / achievements.Count);
        for (var index = 0; index < achievements.Count; index++)
        {
            var achievement = achievements[index];
            var y = panel.Y + 92 + index * rowHeight;
            var unlocked = achievement.IsUnlocked;
            _font.Draw(_spriteBatch, unlocked ? "★" : "·", new Vector2(panel.X + 26, y + 1),
                unlocked ? new Color(255, 211, 98) : new Color(130, 145, 155), 1.5f);
            _font.Draw(_spriteBatch, achievement.Title.ToUpperInvariant(), new Vector2(panel.X + 54, y),
                unlocked ? new Color(218, 244, 186) : Color.White, 1.05f);
            _font.Draw(_spriteBatch, achievement.Description, new Vector2(panel.X + 54, y + 14),
                new Color(156, 176, 188), 0.8f);
            var progress = $"{Math.Min(achievement.Progress, achievement.Goal)}/{achievement.Goal}";
            _font.Draw(_spriteBatch, unlocked ? "UNLOCKED" : progress,
                new Vector2(panel.Right - 116, y + 3), unlocked ? new Color(139, 229, 116) : new Color(203, 215, 219), 0.85f);
            var bar = new Rectangle(panel.Right - 116, y + 19, 88, 4);
            DrawRect(bar, new Color(38, 52, 61));
            DrawRect(new Rectangle(bar.X, bar.Y, (int)(bar.Width * achievement.Completion), bar.Height),
                unlocked ? new Color(115, 218, 86) : new Color(82, 155, 190));
        }
        _font.Draw(_spriteBatch, "ESC: RETURN TO YOUR WORLD", new Vector2(panel.X + 28, panel.Bottom - 25),
            new Color(158, 182, 196), 0.95f);
    }

    private void DrawHotbar(int width, int height)
    {
        if (_inventory is null) return;
        const int slotSize = 48;
        var startX = width / 2 - (slotSize * 9) / 2;
        var y = height - slotSize - 18;
        for (var i = 0; i < 9; i++)
        {
            var rect = new Rectangle(startX + i * slotSize, y, slotSize - 3, slotSize - 3);
            DrawRect(rect, i == _selectedHotbar ? new Color(235, 242, 255, 230) : new Color(9, 14, 24, 205));
            DrawRect(new Rectangle(rect.X + 3, rect.Y + 3, rect.Width - 6, rect.Height - 6),
                i == _selectedHotbar ? new Color(50, 64, 85, 245) : new Color(45, 56, 74, 230));
            var stack = _inventory[i];
            if (!stack.IsEmpty)
            {
                var definition = _items[stack.ItemId];
                var color = definition.PlacesBlockId == 0 ? new Color(226, 205, 150) : BlockColor(definition.PlacesBlockId);
                DrawRect(new Rectangle(rect.X + 13, rect.Y + 10, 19, 19), color);
                _font.Draw(_spriteBatch, stack.Count.ToString(), new Vector2(rect.X + 25, rect.Y + 31), Color.White, 1);
            }
            _font.Draw(_spriteBatch, (i + 1).ToString(), new Vector2(rect.X + 4, rect.Y + 3), Color.White, 1);
        }
    }

    private void DrawSurvivalMeters(int height)
    {
        const int barWidth = 132;
        DrawPanel(new Rectangle(12, height - 82, 162, 68), new Color(7, 11, 20, 190));
        _font.Draw(_spriteBatch, "HEALTH", new Vector2(20, height - 75), new Color(244, 133, 128), 1);
        DrawRect(new Rectangle(20, height - 58, barWidth, 8), new Color(51, 29, 37));
        DrawRect(new Rectangle(20, height - 58, (int)(barWidth * (_survival.Health / SurvivalStats.MaximumHealth)), 8), new Color(224, 79, 83));
        _font.Draw(_spriteBatch, $"{_survival.Health:0}/{SurvivalStats.MaximumHealth:0}", new Vector2(20, height - 48), Color.White, 1);
        _font.Draw(_spriteBatch, "HUNGER", new Vector2(20, height - 33), new Color(232, 188, 116), 1);
        DrawRect(new Rectangle(20, height - 19, barWidth, 5), new Color(51, 43, 31));
        DrawRect(new Rectangle(20, height - 19, (int)(barWidth * (_survival.Hunger / SurvivalStats.MaximumHunger)), 5), new Color(220, 159, 65));
    }

    private void DrawInventorySlots(int width, int height)
    {
        if (_inventory is null) return;
        const int slot = 44;
        var startX = width / 2 - 4 * slot - 6;
        var startY = height / 2 - 115;
        for (var index = 0; index < _inventory.Count; index++)
        {
            var column = index % 8;
            var row = index / 8;
            var rect = new Rectangle(startX + column * slot, startY + row * slot, slot - 4, slot - 4);
            DrawRect(rect, new Color(49, 60, 78, 245));
            var stack = _inventory[index];
            if (!stack.IsEmpty)
            {
                var item = _items[stack.ItemId];
                var color = item.PlacesBlockId == 0 ? new Color(226, 205, 150) : BlockColor(item.PlacesBlockId);
                DrawRect(new Rectangle(rect.X + 12, rect.Y + 9, 17, 17), color);
                _font.Draw(_spriteBatch, stack.Count.ToString(), new Vector2(rect.X + 24, rect.Y + 25), Color.White, 1);
            }
        }
    }

    private void DrawButton(string label, Rectangle rectangle, int width, int height)
    {
        var hovered = rectangle.Contains(Mouse.GetState().Position);
        DrawRect(rectangle, hovered ? new Color(57, 112, 176) : new Color(30, 53, 81));
        DrawRect(new Rectangle(rectangle.X + 2, rectangle.Y + 2, rectangle.Width - 4, rectangle.Height - 4),
            hovered ? new Color(73, 138, 209) : new Color(39, 70, 105));
        _font.Draw(_spriteBatch, label, new Vector2(rectangle.Center.X - label.Length * 6, rectangle.Center.Y - 8), Color.White, 2);
    }

    private Rectangle Button(string name)
    {
        var width = GraphicsDevice.Viewport.Width;
        var height = GraphicsDevice.Viewport.Height;
        return name switch
        {
            "NEW WORLD" => new Rectangle(width / 2 - 145, height / 2 - 12, 290, 55),
            "EXIT" => new Rectangle(width / 2 - 145, height / 2 + 58, 290, 55),
            "RESUME" => new Rectangle(width / 2 - 145, height / 2 - 85, 290, 50),
            "SAVE WORLD" => new Rectangle(width / 2 - 145, height / 2 - 20, 290, 50),
            "MAIN MENU" => new Rectangle(width / 2 - 145, height / 2 + 45, 290, 50),
            _ => Rectangle.Empty
        };
    }

    private void DrawPanel(Rectangle rectangle, Color color)
    {
        DrawRect(rectangle, color);
        DrawRect(new Rectangle(rectangle.X, rectangle.Y, rectangle.Width, 2), new Color(94, 139, 190, 210));
    }

    private void DrawRect(Rectangle rectangle, Color color) => _spriteBatch.Draw(_pixel, rectangle, color);

    private void SaveWorld()
    {
        if (_world is null) return;
        try { _world.SaveAllAsync().GetAwaiter().GetResult(); }
        catch (Exception exception) { Console.Error.WriteLine($"World save failed: {exception}"); }
        try { _settings.Save(_settingsPath); }
        catch (IOException exception) { Console.Error.WriteLine($"Settings save failed: {exception.Message}"); }
        try
        {
            File.WriteAllText(Path.Combine(_worldDirectory, "achievements.json"),
                JsonSerializer.Serialize(_achievements.CreateSnapshot(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Achievement save failed: {exception.Message}");
        }
        if (_player is not null && _inventory is not null)
        {
            try
            {
                var state = new PlayerSaveData(_player.Position.X, _player.Position.Y, _player.Position.Z,
                    _player.Yaw, _player.Pitch, _inventory.Snapshot().ToArray(),
                    _survival.Health, _survival.Hunger, _survival.Saturation);
                File.WriteAllText(Path.Combine(_worldDirectory, "player.json"),
                    JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                Console.Error.WriteLine($"Player save failed: {exception.Message}");
            }
        }
    }

    private void SaveScreenshot()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "BlockCraft");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"Screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        using var stream = File.Create(path);
        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var height = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var data = new Color[width * height];
        GraphicsDevice.GetBackBufferData(data);
        using var image = new Texture2D(GraphicsDevice, width, height);
        image.SetData(data);
        image.SaveAsPng(stream, width, height);
    }

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        _graphics.IsFullScreen = _isFullscreen;
        _settings.Fullscreen = _isFullscreen;
        _graphics.ApplyChanges();
        CenterMouse();
    }

    private void CenterMouse() => Mouse.SetPosition(GraphicsDevice.Viewport.Width / 2, GraphicsDevice.Viewport.Height / 2);

    private static XVector3 ToXna(NVector3 vector) => new(vector.X, vector.Y, vector.Z);

    private void HandleExiting(object? sender, EventArgs args)
    {
        SaveWorld();
        _ambientSound?.Stop();
        _ambientSound?.Dispose();
        _musicInstance?.Stop();
        _musicInstance?.Dispose();
        _musicEffect?.Dispose();
        _blockSound?.Dispose();
        _font?.Dispose();
        _touchCircle?.Dispose();
        _blockAtlas?.Dispose();
        _pixel?.Dispose();
        _spriteBatch?.Dispose();
        _effect?.Dispose();
    }

    private readonly record struct TouchLayout(
        Vector2 JoystickCenter, Vector2 JumpCenter, Vector2 MineCenter, Vector2 PlaceCenter,
        float JoystickRadius, float ButtonRadius);
    private sealed record PlayerSaveData(
        float X, float Y, float Z, float Yaw, float Pitch, ItemStack[] Inventory,
        float Health = 20f, float Hunger = 20f, float Saturation = 5f);
    private enum GameScreen { MainMenu, Playing, Paused, Inventory, Achievements, GameOver }
}
