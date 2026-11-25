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
      {"id":13,"name":"diamond_ore"}
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
    public void SameSeedAndCoordinatesProduceIdenticalChunkBytes()
    {
        var registry = CreateBlocks();
        var first = new TerrainGenerator(77, registry).Generate(new ChunkPos(3, -4));
        var second = new TerrainGenerator(77, registry).Generate(new ChunkPos(3, -4));
        Assert.Equal(first.Serialize(), second.Serialize());
    }
}
