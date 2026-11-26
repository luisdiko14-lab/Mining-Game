using BlockCraft.Core.Blocks;

namespace BlockCraft.Core.World;

/// <summary>Creates deterministic biome terrain, caves, ores, water, and trees.</summary>
public sealed class TerrainGenerator
{
    private readonly ushort _air, _stone, _dirt, _grass, _sand, _snow, _water, _bedrock, _log, _leaves;
    private readonly ushort _coalOre, _ironOre, _goldOre, _diamondOre;
    private readonly ushort _granite, _diorite, _andesite, _deepslate, _tuff, _lava;
    private readonly ushort _copperOre, _redstoneOre, _lapisOre, _emeraldOre, _ancientDebris;
    private readonly ushort _ironOreDeepslate, _goldOreDeepslate, _diamondOreDeepslate;
    private readonly ushort _copperOreDeepslate, _redstoneOreDeepslate, _lapisOreDeepslate, _emeraldOreDeepslate;
    private readonly ushort _birchLog, _birchLeaves, _spruceLog, _spruceLeaves;
    private readonly ushort _jungleLog, _jungleLeaves, _acaciaLog, _acaciaLeaves;
    private readonly ushort _darkOakLog, _darkOakLeaves, _mangroveLog, _mangroveLeaves, _cherryLog, _cherryLeaves;
    /// <summary>Mean sea level used by oceans and surface water.</summary>
    public const int SeaLevel = 62;

    /// <summary>Creates a generator using the supplied validated block registry.</summary>
    public TerrainGenerator(long seed, BlockRegistry blocks)
    {
        Seed = seed;
        ArgumentNullException.ThrowIfNull(blocks);
        _air = blocks.GetId("air"); _stone = blocks.GetId("stone"); _dirt = blocks.GetId("dirt");
        _grass = blocks.GetId("grass_block"); _sand = blocks.GetId("sand"); _snow = blocks.GetId("snow_block");
        _water = blocks.GetId("water"); _bedrock = blocks.GetId("bedrock"); _log = blocks.GetId("oak_log");
        _leaves = blocks.GetId("oak_leaves"); _coalOre = blocks.GetId("coal_ore"); _ironOre = blocks.GetId("iron_ore");
        _goldOre = blocks.GetId("gold_ore"); _diamondOre = blocks.GetId("diamond_ore");
        _granite = Optional(blocks, "granite", _stone); _diorite = Optional(blocks, "diorite", _stone);
        _andesite = Optional(blocks, "andesite", _stone); _deepslate = Optional(blocks, "deepslate", _stone);
        _tuff = Optional(blocks, "tuff", _stone); _lava = Optional(blocks, "lava", _air);
        _copperOre = Optional(blocks, "copper_ore", _stone); _redstoneOre = Optional(blocks, "redstone_ore", _stone);
        _lapisOre = Optional(blocks, "lapis_ore", _stone); _emeraldOre = Optional(blocks, "emerald_ore", _stone);
        _ancientDebris = Optional(blocks, "ancient_debris", _stone);
        _ironOreDeepslate = Optional(blocks, "iron_ore_deepslate", _ironOre);
        _goldOreDeepslate = Optional(blocks, "gold_ore_deepslate", _goldOre);
        _diamondOreDeepslate = Optional(blocks, "diamond_ore_deepslate", _diamondOre);
        _copperOreDeepslate = Optional(blocks, "copper_ore_deepslate", _copperOre);
        _redstoneOreDeepslate = Optional(blocks, "redstone_ore_deepslate", _redstoneOre);
        _lapisOreDeepslate = Optional(blocks, "lapis_ore_deepslate", _lapisOre);
        _emeraldOreDeepslate = Optional(blocks, "emerald_ore_deepslate", _emeraldOre);
        _birchLog = Optional(blocks, "birch_log", _log); _birchLeaves = Optional(blocks, "birch_leaves", _leaves);
        _spruceLog = Optional(blocks, "spruce_log", _log); _spruceLeaves = Optional(blocks, "spruce_leaves", _leaves);
        _jungleLog = Optional(blocks, "jungle_log", _log); _jungleLeaves = Optional(blocks, "jungle_leaves", _leaves);
        _acaciaLog = Optional(blocks, "acacia_log", _log); _acaciaLeaves = Optional(blocks, "acacia_leaves", _leaves);
        _darkOakLog = Optional(blocks, "dark_oak_log", _log); _darkOakLeaves = Optional(blocks, "dark_oak_leaves", _leaves);
        _mangroveLog = Optional(blocks, "mangrove_log", _log); _mangroveLeaves = Optional(blocks, "mangrove_leaves", _leaves);
        _cherryLog = Optional(blocks, "cherry_log", _log); _cherryLeaves = Optional(blocks, "cherry_leaves", _leaves);
    }

    /// <summary>World seed. Equal seeds and chunk coordinates produce identical block data.</summary>
    public long Seed { get; }

    /// <summary>Generates one complete chunk, including its horizontal biome map.</summary>
    public Chunk Generate(ChunkPos position)
    {
        var chunk = new Chunk(position.X, position.Z);
        var seed = unchecked((int)(Seed ^ (Seed >> 32)));
        for (var z = 0; z < Chunk.Depth; z++)
        for (var x = 0; x < Chunk.Width; x++)
        {
            var worldX = position.X * Chunk.Width + x; var worldZ = position.Z * Chunk.Depth + z;
            var temperature = Noise.Fractal2D(seed + 17, worldX / 420f, worldZ / 420f, 3);
            var moisture = Noise.Fractal2D(seed + 37, worldX / 310f, worldZ / 310f, 3);
            var continental = Noise.Fractal2D(seed + 71, worldX / 180f, worldZ / 180f, 5);
            var biome = SelectBiome(temperature, moisture, continental);
            chunk.Biomes[x, z] = biome;
            var height = Math.Clamp((int)MathF.Round(66 + continental * 24 +
                Noise.Fractal2D(seed + 91, worldX / 36f, worldZ / 36f, 3) * 7), 8, Chunk.Height - 12);
            var top = biome switch { BiomeKind.Desert or BiomeKind.Ocean => _sand, BiomeKind.Snow => _snow, _ => _grass };

            for (var y = 0; y < Chunk.Height; y++)
            {
                ushort block = _air;
                if (y == 0) block = _bedrock;
                else if (y <= height)
                {
                    if (y == height) block = top;
                    else if (y >= height - 3 && biome == BiomeKind.Desert) block = _sand;
                    else if (y >= height - 3) block = _dirt;
                    else if (y > 5 && y < height - 4 &&
                             MathF.Abs(Noise.Sample3D(seed + 211, worldX * 0.055f, y * 0.065f, worldZ * 0.055f)) < 0.058f)
                        block = y < 16 && _lava != _air && Noise.Hash(seed + 1009, worldX, y, worldZ) > 0.45f ? _lava : _air;
                    else block = SelectOre(seed, worldX, y, worldZ);
                }
                else if (y <= SeaLevel && biome != BiomeKind.Snow) block = _water;
                chunk.SetBlock(x, y, z, block);
            }
        }
        AddTrees(chunk, seed);
        return chunk;
    }

    private ushort SelectOre(int seed, int x, int y, int z)
    {
        var chance = MathF.Abs(Noise.Hash(seed + 401, x / 3, y / 2, z / 3));
        var vein = MathF.Abs(Noise.Hash(seed + 521, x / 5, y / 4, z / 5));
        if (y < 12 && chance > 0.9991f) return _ancientDebris;
        if (y < 30 && chance > 0.995f) return _diamondOreDeepslate;
        if (y < 48 && chance > 0.992f) return _redstoneOreDeepslate;
        if (y < 48 && chance > 0.988f) return _goldOreDeepslate;
        if (y < 48 && chance > 0.982f) return _ironOreDeepslate;
        if (y < 48 && chance > 0.976f) return _copperOreDeepslate;
        if (y < 48 && chance > 0.970f) return _lapisOreDeepslate;
        if (y < 64 && chance > 0.9983f) return _emeraldOreDeepslate;
        if (y < 26 && chance > 0.991f) return _diamondOre;
        if (y < 52 && chance > 0.987f) return _redstoneOre;
        if (y < 76 && chance > 0.991f) return _goldOre;
        if (y < 96 && chance > 0.998f) return _emeraldOre;
        if (y < 96 && chance > 0.980f) return _copperOre;
        if (y < 112 && chance > 0.987f) return _lapisOre;
        if (y < 120 && chance > 0.975f) return _ironOre;
        if (y < 160 && chance > 0.945f) return _coalOre;
        if (y < 48) return _deepslate;
        if (y < 76 && vein > 0.965f) return _tuff;
        if (vein > 0.992f) return _granite;
        if (vein > 0.981f) return _diorite;
        if (vein > 0.968f) return _andesite;
        return _stone;
    }

    private void AddTrees(Chunk chunk, int seed)
    {
        for (var z = 2; z < Chunk.Depth - 2; z++)
        for (var x = 2; x < Chunk.Width - 2; x++)
        {
            var biome = chunk.Biomes[x, z];
            if (biome is not (BiomeKind.Forest or BiomeKind.Plains or BiomeKind.Jungle or BiomeKind.Savanna or BiomeKind.Snow)) continue;
            var worldX = chunk.ChunkX * Chunk.Width + x; var worldZ = chunk.ChunkZ * Chunk.Depth + z;
            var density = biome switch { BiomeKind.Forest => 0.965f, BiomeKind.Jungle => 0.925f, BiomeKind.Savanna => 0.982f, BiomeKind.Snow => 0.982f, _ => 0.992f };
            if (Noise.Hash(seed + 733, worldX, 0, worldZ) < density) continue;
            var ground = FindSurface(chunk, x, z);
            var groundBlock = chunk.GetBlock(x, ground, z);
            if (ground < SeaLevel || (groundBlock != _grass && groundBlock != _snow)) continue;
            var species = MathF.Abs(Noise.Hash(seed + 887, worldX, 0, worldZ));
            var (treeLog, treeLeaves) = biome switch
            {
                BiomeKind.Jungle => (_jungleLog, _jungleLeaves), BiomeKind.Savanna => (_acaciaLog, _acaciaLeaves),
                BiomeKind.Snow => (_spruceLog, _spruceLeaves),
                BiomeKind.Forest when species < 0.16f => (_birchLog, _birchLeaves),
                BiomeKind.Forest when species < 0.31f => (_darkOakLog, _darkOakLeaves),
                BiomeKind.Forest when species < 0.42f => (_mangroveLog, _mangroveLeaves),
                BiomeKind.Forest when species < 0.55f => (_cherryLog, _cherryLeaves),
                BiomeKind.Plains when species < 0.22f => (_birchLog, _birchLeaves), _ => (_log, _leaves)
            };
            var trunkHeight = (biome == BiomeKind.Jungle ? 6 : 4) + (int)(MathF.Abs(Noise.Hash(seed + 811, worldX, 0, worldZ)) * 3);
            for (var y = 1; y <= trunkHeight; y++) chunk.SetBlock(x, ground + y, z, treeLog);
            var crownY = ground + trunkHeight;
            for (var dy = -2; dy <= 2; dy++)
            for (var dz = -2; dz <= 2; dz++)
            for (var dx = -2; dx <= 2; dx++)
            {
                if (Math.Abs(dx) + Math.Abs(dz) + Math.Abs(dy) > 5) continue;
                var lx = x + dx; var lz = z + dz; var ly = crownY + dy;
                if ((uint)lx < Chunk.Width && (uint)lz < Chunk.Depth && (uint)ly < Chunk.Height &&
                    chunk.GetBlock(lx, ly, lz) == _air) chunk.SetBlock(lx, ly, lz, treeLeaves);
            }
        }
    }

    private static ushort Optional(BlockRegistry blocks, string name, ushort fallback) => blocks.TryGetId(name, out var id) ? id : fallback;

    private static int FindSurface(Chunk chunk, int x, int z)
    {
        for (var y = Chunk.Height - 2; y > 0; y--)
            if (chunk.GetBlock(x, y, z) != 0) return y;
        return 0;
    }

    private static BiomeKind SelectBiome(float temperature, float moisture, float continental)
    {
        if (continental < -0.48f) return BiomeKind.Ocean;
        if (temperature < -0.34f) return BiomeKind.Snow;
        if (temperature > 0.36f && moisture < 0.05f) return BiomeKind.Desert;
        if (temperature > 0.25f && moisture > 0.28f) return BiomeKind.Jungle;
        if (temperature > 0.25f && moisture < -0.18f) return BiomeKind.Savanna;
        if (moisture > 0.08f) return BiomeKind.Forest;
        return BiomeKind.Plains;
    }
}
