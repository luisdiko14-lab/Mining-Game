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
    private readonly ConcurrentQueue<(ChunkPos Position, int Revision, VertexPositionColorTexture[] Vertices)> _readyMeshes = new();
    private readonly Dictionary<ChunkPos, (int Revision, VertexPositionColorTexture[] Vertices)> _meshes = [];
    private readonly WorldClock _clock = new();
    private GameSettings _settings;
    private BlockRegistry _blocks = null!;
    private ItemRegistry _items = null!;
    private Inventory? _inventory;
    private ChunkManager? _world;
    private PlayerController? _player;
    private SpriteBatch _spriteBatch = null!;
    private Texture2D _pixel = null!;
    private BitmapFont _font = null!;
    private BasicEffect _effect = null!;
    private SoundEffect? _blockSound;
    private Texture2D? _blockAtlas;
    private readonly Dictionary<string, Rectangle> _atlasTiles = new(StringComparer.OrdinalIgnoreCase);
    private int _atlasWidth;
    private int _atlasHeight;
    private SoundEffectInstance? _ambientSound;
    private MouseState _previousMouse;
    private KeyboardState _previousKeyboard;
    private GameScreen _screen = GameScreen.MainMenu;
    private int _selectedHotbar;
    private int _lastLoadedChunkCount = -1;
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
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
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
            _screen = _screen switch
            {
                GameScreen.Playing => GameScreen.Paused,
                GameScreen.Paused or GameScreen.Inventory => GameScreen.Playing,
                _ => GameScreen.MainMenu
            };
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
            if (keyboard.IsKeyDown(Keys.C) && !_previousKeyboard.IsKeyDown(Keys.C))
                CraftPlanks();
        }
        else if (_screen == GameScreen.Playing && _world is not null && _player is not null && _inventory is not null)
        {
            UpdatePlaying(gameTime, keyboard, mouse, rightPressed);
        }

        _clock.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
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
        _player = new PlayerController(_blocks, new NVector3(8.5f, surface + 1.05f, 8.5f));
        _inventory = new Inventory(_items, 36);
        _inventory.Add(_items.GetId("stone"), 64);
        _inventory.Add(_items.GetId("dirt"), 64);
        _inventory.Add(_items.GetId("oak_planks"), 64);
        _inventory.Add(_items.GetId("oak_log"), 16);
        _inventory.Add(_items.GetId("glass"), 32);
        _inventory.Add(_items.GetId("torch"), 24);
        var playerSavePath = Path.Combine(_worldDirectory, "player.json");
        if (File.Exists(playerSavePath))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<PlayerSaveData>(File.ReadAllText(playerSavePath));
                if (saved is not null && saved.Inventory is not null)
                {
                    _player.Teleport(new NVector3(saved.X, saved.Y, saved.Z));
                    _player.Yaw = saved.Yaw;
                    _player.Pitch = saved.Pitch;
                    for (var index = 0; index < _inventory.Count; index++)
                        _inventory.Set(index, index < saved.Inventory.Length ? saved.Inventory[index] : ItemStack.Empty);
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or KeyNotFoundException)
            {
                Console.Error.WriteLine($"Player save could not be loaded: {exception.Message}");
            }
        }
        _selectedHotbar = 0;
        _screen = GameScreen.Playing;
        IsMouseVisible = false;
        _meshes.Clear();
        _lastLoadedChunkCount = -1;
        CenterMouse();
        RequestNearbyChunks();
    }

    private void UpdatePlaying(GameTime gameTime, KeyboardState keyboard, MouseState mouse, bool rightPressed)
    {
        var elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var centerX = GraphicsDevice.Viewport.Width / 2;
        var centerY = GraphicsDevice.Viewport.Height / 2;
        _player!.Yaw += (mouse.X - centerX) * 0.0024f;
        _player.Pitch = Math.Clamp(_player.Pitch - (mouse.Y - centerY) * 0.0024f, -1.52f, 1.52f);
        CenterMouse();

        var horizontal = (keyboard.IsKeyDown(Keys.D) ? 1f : 0f) - (keyboard.IsKeyDown(Keys.A) ? 1f : 0f);
        var vertical = (keyboard.IsKeyDown(Keys.W) ? 1f : 0f) - (keyboard.IsKeyDown(Keys.S) ? 1f : 0f);
        _player.Update(_world!, new NVector2(horizontal, vertical), elapsed,
            keyboard.IsKeyDown(Keys.Space), keyboard.IsKeyDown(Keys.LeftShift), keyboard.IsKeyDown(Keys.LeftControl));

        if (keyboard.IsKeyDown(Keys.F5) && !_previousKeyboard.IsKeyDown(Keys.F5))
            _player.IsFlying = !_player.IsFlying;
        if (keyboard.IsKeyDown(Keys.F3) && !_previousKeyboard.IsKeyDown(Keys.F3))
            _showDebug = !_showDebug;
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

        if (mouse.LeftButton == ButtonState.Pressed && hit is not null)
            MineBlock(hit.Value, elapsed);
        else
            ResetBreakProgress();

        if (rightPressed && hit is not null)
            PlaceBlock(hit.Value);
        RequestNearbyChunks();
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
        if (!stack.IsEmpty)
        {
            var tool = _items[stack.ItemId];
            if (tool.ToolType.Equals(definition.RequiredTool, StringComparison.OrdinalIgnoreCase))
                speed = Math.Max(1f, tool.MiningSpeed);
        }
        _breakProgress += elapsed * speed / Math.Max(0.15f, definition.Hardness * 0.62f);
        if (_breakProgress < 1f) return;
        if (_world.SetBlock(position.X, position.Y, position.Z, 0))
        {
            if (definition.DropItemId != 0)
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
        if (_world!.GetBlock(place.X, place.Y, place.Z) != 0) return;
        if (_world.SetBlock(place.X, place.Y, place.Z, item.PlacesBlockId))
        {
            _inventory.Set(_selectedHotbar, stack.Count == 1 ? ItemStack.Empty : stack with { Count = stack.Count - 1 });
            _blockSound?.Play(_settings.MasterVolume * 0.55f, -0.15f, 0);
        }
    }

    private void CraftPlanks()
    {
        if (_inventory is null) return;
        var logs = _items.GetId("oak_log");
        if (_inventory.Remove(logs, 1))
            _inventory.Add(_items.GetId("oak_planks"), 4);
    }

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
        var radius = Math.Min(_settings.RenderDistance, 6);
        for (var z = -radius; z <= radius; z++)
        for (var x = -radius; x <= radius; x++)
            _ = _world.RequestChunkAsync(new ChunkPos(chunkX + x, chunkZ + z));
    }

    private void UpdateChunkMeshes()
    {
        if (_world is null) return;
        var chunks = _world.LoadedChunks;
        if (chunks.Count != _lastLoadedChunkCount)
        {
            _lastLoadedChunkCount = chunks.Count;
            foreach (var key in _meshes.Keys.ToArray())
                _meshes[key] = (-1, _meshes[key].Vertices);
        }

        foreach (var chunk in chunks)
        {
            var position = new ChunkPos(chunk.ChunkX, chunk.ChunkZ);
            if (_meshes.TryGetValue(position, out var existing) && existing.Revision == chunk.Revision)
                continue;
            if (!_meshJobs.TryAdd(position, 0)) continue;
            _ = Task.Run(() =>
            {
                try
                {
                    var quads = GreedyMesher.Build(
                        (x, y, z) => _world.GetBlock(chunk.ChunkX * Chunk.Width + x, y, chunk.ChunkZ * Chunk.Depth + z),
                        _blocks);
                    var vertices = new List<VertexPositionColorTexture>(quads.Count * 6);
                    foreach (var quad in quads)
                    {
                        var color = BlockColor(quad.BlockId);
                        var shade = quad.Normal.Y > 0 ? 1f : quad.Normal.Y < 0 ? 0.52f : 0.78f;
                        color = new Color((byte)(color.R * shade), (byte)(color.G * shade), (byte)(color.B * shade), color.A);
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
                        vertices.Add(new VertexPositionColorTexture(p0, color, uv0));
                        vertices.Add(new VertexPositionColorTexture(p1, color, uv1));
                        vertices.Add(new VertexPositionColorTexture(p2, color, uv2));
                        vertices.Add(new VertexPositionColorTexture(p0, color, uv0));
                        vertices.Add(new VertexPositionColorTexture(p2, color, uv2));
                        vertices.Add(new VertexPositionColorTexture(p3, color, uv3));
                    }
                    _readyMeshes.Enqueue((position, chunk.Revision, vertices.ToArray()));
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
            _meshes[mesh.Position] = (mesh.Revision, mesh.Vertices);
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
        if (_screen is GameScreen.Playing or GameScreen.Paused or GameScreen.Inventory)
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
        else if (_screen == GameScreen.Inventory && _inventory is not null)
        {
            DrawPanel(new Rectangle(width / 2 - 260, height / 2 - 210, 520, 420), new Color(10, 16, 28, 238));
            _font.Draw(_spriteBatch, "INVENTORY", new Vector2(width / 2 - 80, height / 2 - 185), Color.White, 2.5f);
            DrawInventorySlots(width, height);
            _font.Draw(_spriteBatch, "C: CRAFT 4 PLANKS FROM 1 LOG", new Vector2(width / 2 - 170, height / 2 + 170), new Color(177, 194, 218), 1.3f);
            _font.Draw(_spriteBatch, "PRESS E OR ESC TO RETURN", new Vector2(width / 2 - 125, height / 2 + 190), new Color(137, 155, 179), 1.1f);
        }
        _spriteBatch.End();
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
        if (_player is not null && _inventory is not null)
        {
            try
            {
                var state = new PlayerSaveData(_player.Position.X, _player.Position.Y, _player.Position.Z,
                    _player.Yaw, _player.Pitch, _inventory.Snapshot().ToArray());
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
        _blockSound?.Dispose();
        _font?.Dispose();
        _blockAtlas?.Dispose();
        _pixel?.Dispose();
        _spriteBatch?.Dispose();
        _effect?.Dispose();
    }

    private sealed record PlayerSaveData(float X, float Y, float Z, float Yaw, float Pitch, ItemStack[] Inventory);
    private enum GameScreen { MainMenu, Playing, Paused, Inventory }
}
