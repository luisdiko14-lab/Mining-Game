using BlockCraft.Core.Blocks;

namespace BlockCraft.Core.World;

/// <summary>Creates deterministic biome terrain, caves, ores, water, and trees.</summary>
public sealed class TerrainGenerator
{
    private readonly ushort _air, _stone, _dirt, _grass, _sand, _snow, _water, _bedrock, _log, _leaves;
    private readonly ushort _coalOre, _ironOre, _goldOre, _diamondOre;
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
            var top = biome switch { BiomeKind.Desert => _sand, BiomeKind.Snow => _snow, _ => _grass };

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
                        block = _air;
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
        if (y < 24 && chance > 0.9945f) return _diamondOre;
        if (y < 48 && chance > 0.990f) return _goldOre;
        if (y < 104 && chance > 0.975f) return _ironOre;
        if (y < 160 && chance > 0.945f) return _coalOre;
        return _stone;
    }

    private void AddTrees(Chunk chunk, int seed)
    {
        for (var z = 2; z < Chunk.Depth - 2; z++)
        for (var x = 2; x < Chunk.Width - 2; x++)
        {
            var biome = chunk.Biomes[x, z];
            if (biome is not (BiomeKind.Forest or BiomeKind.Plains)) continue;
            var worldX = chunk.ChunkX * Chunk.Width + x; var worldZ = chunk.ChunkZ * Chunk.Depth + z;
            if (Noise.Hash(seed + 733, worldX, 0, worldZ) < (biome == BiomeKind.Forest ? 0.965f : 0.992f)) continue;
            var ground = FindSurface(chunk, x, z);
            if (ground < SeaLevel || chunk.GetBlock(x, ground, z) != _grass) continue;
            var trunkHeight = 4 + (int)(MathF.Abs(Noise.Hash(seed + 811, worldX, 0, worldZ)) * 3);
            for (var y = 1; y <= trunkHeight; y++) chunk.SetBlock(x, ground + y, z, _log);
            var crownY = ground + trunkHeight;
            for (var dy = -2; dy <= 2; dy++)
            for (var dz = -2; dz <= 2; dz++)
            for (var dx = -2; dx <= 2; dx++)
            {
                if (Math.Abs(dx) + Math.Abs(dz) + Math.Abs(dy) > 5) continue;
                var lx = x + dx; var lz = z + dz; var ly = crownY + dy;
                if ((uint)lx < Chunk.Width && (uint)lz < Chunk.Depth && (uint)ly < Chunk.Height &&
                    chunk.GetBlock(lx, ly, lz) == _air) chunk.SetBlock(lx, ly, lz, _leaves);
            }
        }
    }

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
        if (moisture > 0.08f) return BiomeKind.Forest;
        return BiomeKind.Plains;
    }
}
