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
    private readonly ushort _echoOre, _amethystBlock, _calcite, _glowcap, _fossilBlock, _torch, _oakPlanks;
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
        _echoOre = Optional(blocks, "echo_ore", _stone);
        _amethystBlock = Optional(blocks, "amethyst_block", _air);
        _calcite = Optional(blocks, "calcite", _air);
        _glowcap = Optional(blocks, "glowcap", _air);
        _fossilBlock = Optional(blocks, "fossil_block", _air);
        _torch = Optional(blocks, "torch", _air);
        _oakPlanks = Optional(blocks, "oak_planks", _dirt);
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
        AddGeodes(chunk, seed);
        AddAbandonedMineshaft(chunk, seed);
        AddCaveFeatures(chunk, seed);
        return chunk;
    }

    private ushort SelectOre(int seed, int x, int y, int z)
    {
        var chance = MathF.Abs(Noise.Hash(seed + 401, x / 3, y / 2, z / 3));
        var vein = MathF.Abs(Noise.Hash(seed + 521, x / 5, y / 4, z / 5));
        if (y < 12 && chance > 0.9991f) return _ancientDebris;
        if (y < 42 && chance > 0.9976f && _echoOre != _stone) return _echoOre;
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

    /// <summary>
    /// Adds rare hollow geodes with tuff exteriors, calcite shells, and exposed amethyst.
    /// Their centers stay inside chunk borders, so generation never depends on neighbor load order.
    /// </summary>
    private void AddGeodes(Chunk chunk, int seed)
    {
        if (_amethystBlock == _air || _calcite == _air)
            return;
        if (Noise.Hash(seed + 2027, chunk.ChunkX, 0, chunk.ChunkZ) < 0.72f)
            return;

        var centerX = 5 + (int)(MathF.Abs(Noise.Hash(seed + 2039, chunk.ChunkX, 0, chunk.ChunkZ)) * 6f);
        var centerZ = 5 + (int)(MathF.Abs(Noise.Hash(seed + 2053, chunk.ChunkX, 0, chunk.ChunkZ)) * 6f);
        var centerY = 24 + (int)(MathF.Abs(Noise.Hash(seed + 2063, chunk.ChunkX, 0, chunk.ChunkZ)) * 42f);
        var radius = 3.7f + MathF.Abs(Noise.Hash(seed + 2081, chunk.ChunkX, 0, chunk.ChunkZ)) * 1.4f;
        var outerSquared = radius * radius;

        for (var y = Math.Max(8, centerY - 5); y <= Math.Min(SeaLevel + 8, centerY + 5); y++)
        for (var z = Math.Max(1, centerZ - 5); z <= Math.Min(Chunk.Depth - 2, centerZ + 5); z++)
        for (var x = Math.Max(1, centerX - 5); x <= Math.Min(Chunk.Width - 2, centerX + 5); x++)
        {
            var dx = x - centerX;
            var dy = (y - centerY) * 1.08f;
            var dz = z - centerZ;
            var distance = dx * dx + dy * dy + dz * dz;
            if (distance > outerSquared)
                continue;

            var current = chunk.GetBlock(x, y, z);
            if (current == _air || current == _water || current == _lava)
                continue;

            var radialDistance = MathF.Sqrt(distance);
            var shell = radialDistance >= radius - 0.7f ? _tuff
                : radialDistance >= radius - 1.55f ? _calcite
                : radialDistance >= radius - 2.35f ? _amethystBlock
                : _air;
            chunk.SetBlock(x, y, z, shell);
        }

        // Interior crystal points make the hollow cavity visible from more than one angle.
        for (var crystal = 0; crystal < 4; crystal++)
        {
            var x = Math.Clamp(centerX + (int)(Noise.Hash(seed + 2111 + crystal, chunk.ChunkX, 0, chunk.ChunkZ) * 2.2f), 2, Chunk.Width - 3);
            var z = Math.Clamp(centerZ + (int)(Noise.Hash(seed + 2131 + crystal, chunk.ChunkX, 0, chunk.ChunkZ) * 2.2f), 2, Chunk.Depth - 3);
            var y = Math.Clamp(centerY + (int)(Noise.Hash(seed + 2153 + crystal, chunk.ChunkX, 0, chunk.ChunkZ) * 1.7f), 10, Chunk.Height - 10);
            if (chunk.GetBlock(x, y, z) == _air)
                chunk.SetBlock(x, y, z, _amethystBlock);
        }
    }

    /// <summary>Adds occasional timber-supported abandoned mining corridors.</summary>
    private void AddAbandonedMineshaft(Chunk chunk, int seed)
    {
        if (Noise.Hash(seed + 2203, chunk.ChunkX, 0, chunk.ChunkZ) < 0.88f)
            return;

        var centerZ = 4 + (int)(MathF.Abs(Noise.Hash(seed + 2213, chunk.ChunkX, 0, chunk.ChunkZ)) * 8f);
        var floorY = 24 + (int)(MathF.Abs(Noise.Hash(seed + 2221, chunk.ChunkX, 0, chunk.ChunkZ)) * 24f);
        for (var x = 1; x < Chunk.Width - 1; x++)
        {
            for (var z = Math.Max(1, centerZ - 2); z <= Math.Min(Chunk.Depth - 2, centerZ + 2); z++)
            for (var y = floorY; y <= floorY + 3; y++)
            {
                var existing = chunk.GetBlock(x, y, z);
                if (existing != _bedrock && existing != _lava)
                    chunk.SetBlock(x, y, z, _air);
            }

            if (x == 2 || x == 6 || x == 10 || x == 14)
            {
                chunk.SetBlock(x, floorY - 1, centerZ, _oakPlanks);
                for (var y = floorY; y <= floorY + 3; y++)
                {
                    chunk.SetBlock(x, y, centerZ - 2, _log);
                    chunk.SetBlock(x, y, centerZ + 2, _log);
                }
                for (var z = centerZ - 2; z <= centerZ + 2; z++)
                    chunk.SetBlock(x, floorY + 3, z, _log);
            }
            else if (x % 2 == 0)
            {
                chunk.SetBlock(x, floorY - 1, centerZ, _oakPlanks);
            }

            if ((x == 4 || x == 12) && _torch != _air)
                chunk.SetBlock(x, floorY + 2, centerZ - 1, _torch);
            if (x == 12 && _fossilBlock != _air)
                chunk.SetBlock(x, floorY + 1, centerZ + 1, _fossilBlock);
        }
    }

    /// <summary>Seeds bioluminescent glowcaps onto cave ceilings in dark underground pockets.</summary>
    private void AddCaveFeatures(Chunk chunk, int seed)
    {
        if (_glowcap == _air)
            return;
        for (var z = 1; z < Chunk.Depth - 1; z++)
        for (var x = 1; x < Chunk.Width - 1; x++)
        {
            var worldX = chunk.ChunkX * Chunk.Width + x;
            var worldZ = chunk.ChunkZ * Chunk.Depth + z;
            for (var y = 12; y < 78; y++)
            {
                if (chunk.GetBlock(x, y, z) != _air)
                    continue;
                var ceiling = chunk.GetBlock(x, y + 1, z);
                if (ceiling == _air || ceiling == _water || ceiling == _lava)
                    continue;
                if (Noise.Hash(seed + 2281, worldX, y, worldZ) > 0.9975f)
                    chunk.SetBlock(x, y, z, _glowcap);
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
