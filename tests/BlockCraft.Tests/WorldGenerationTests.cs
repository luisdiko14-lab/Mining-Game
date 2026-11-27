using BlockCraft.Core.Blocks;
using BlockCraft.Core.World;
using Xunit;

namespace BlockCraft.Tests;

public sealed class WorldGenerationTests
{
    private static BlockRegistry CreateBlocks() => BlockRegistry.LoadFromJson("""
    [
      {"id":0,"name":"air","solid":false,"transparent":true},
      {"id":1,"name":"stone"},
      {"id":2,"name":"dirt"},
      {"id":3,"name":"grass_block"},
      {"id":4,"name":"sand"},
      {"id":5,"name":"oak_log"},
      {"id":6,"name":"oak_leaves"},
      {"id":7,"name":"snow_block"},
      {"id":8,"name":"water","solid":false,"transparent":true},
      {"id":9,"name":"bedrock"},
      {"id":10,"name":"coal_ore"},
      {"id":11,"name":"iron_ore"},
      {"id":12,"name":"gold_ore"},
      {"id":13,"name":"diamond_ore"},
      {"id":14,"name":"amethyst_block"},
      {"id":15,"name":"calcite"},
      {"id":16,"name":"tuff"},
      {"id":17,"name":"oak_planks"},
      {"id":18,"name":"torch","lightEmission":14},
      {"id":19,"name":"glowcap","solid":false,"transparent":true,"lightEmission":9},
      {"id":20,"name":"fossil_block"},
      {"id":21,"name":"echo_ore"}
    ]
    """);

    [Fact]
    public void NoiseIsDeterministic()
    {
        Assert.Equal(Noise.Sample3D(42, 12.25f, 7.5f, -8.1f), Noise.Sample3D(42, 12.25f, 7.5f, -8.1f));
        Assert.NotEqual(Noise.Sample2D(42, 12, 9), Noise.Sample2D(43, 12, 9));
    }

    [Fact]
    public void GeneratedChunkHasBedrockAndTerrain()
    {
        var chunk = new TerrainGenerator(123456, CreateBlocks()).Generate(new ChunkPos(-1, 2));
        Assert.Equal(9, chunk.GetBlock(0, 0, 0));
        Assert.Equal(Chunk.Width, chunk.Biomes.GetLength(0));
        Assert.Equal(Chunk.Depth, chunk.Biomes.GetLength(1));
        Assert.Contains(chunk.CopyBlocks(), id => id == 1 || id == 2 || id == 3 || id == 4);
    }

    [Fact]
    public void WorldGenerationCanProduceCrystalGeodesAndAbandonedMineshafts()
    {
        var generator = new TerrainGenerator(8317, CreateBlocks());
        var foundGeode = false;
        var foundMineshaft = false;
        var foundGlowcap = false;

        for (var z = -4; z <= 4; z++)
        for (var x = -4; x <= 4; x++)
        {
            var chunk = generator.Generate(new ChunkPos(x, z));
            var blocks = chunk.CopyBlocks();
            foundGeode |= blocks.Contains((ushort)14);
            foundMineshaft |= blocks.Contains((ushort)17);
            foundGlowcap |= blocks.Contains((ushort)19);
        }

        Assert.True(foundGeode, "The deterministic sample should contain at least one amethyst geode.");
        Assert.True(foundMineshaft, "The deterministic sample should contain at least one abandoned mineshaft.");
        Assert.True(foundGlowcap, "Cave ceilings should sometimes grow bioluminescent glowcaps.");
    }

    [Fact]
    public void SameSeedAndCoordinatesProduceIdenticalChunkBytes()
    {
        var registry = CreateBlocks();
        var first = new TerrainGenerator(77, registry).Generate(new ChunkPos(3, -4));
        var second = new TerrainGenerator(77, registry).Generate(new ChunkPos(3, -4));
        Assert.Equal(first.Serialize(), second.Serialize());
    }
}
