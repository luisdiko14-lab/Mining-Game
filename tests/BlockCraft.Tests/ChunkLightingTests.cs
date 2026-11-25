using BlockCraft.Core.Blocks;
using BlockCraft.Core.World;
using Xunit;

namespace BlockCraft.Tests;

public sealed class ChunkLightingTests
{
    private static BlockRegistry CreateBlocks() => BlockRegistry.LoadFromJson("""
    [
      {"id":0,"name":"air","solid":false,"transparent":true},
      {"id":1,"name":"stone","solid":true,"transparent":false},
      {"id":2,"name":"torch","solid":false,"transparent":true,"lightEmission":14},
      {"id":3,"name":"water","solid":false,"transparent":true}
    ]
    """);

    [Fact]
    public void DirectSunlightStopsAtOpaqueBlocks()
    {
        var chunk = new Chunk(0, 0);
        chunk.SetBlock(5, 100, 5, 1);
        var lighting = ChunkLighting.Calculate(chunk, CreateBlocks());

        Assert.Equal((byte)15, lighting.GetSunlight(5, 101, 5));
        Assert.Equal((byte)0, lighting.GetSunlight(5, 100, 5));
        Assert.InRange(lighting.GetSunlight(5, 99, 5), (byte)1, (byte)14);
    }

    [Fact]
    public void TorchLightPropagatesAndDoesNotPassThroughOpaqueBlocks()
    {
        var chunk = new Chunk(0, 0);
        chunk.SetBlock(8, 100, 8, 2);
        for (var y = 0; y < Chunk.Height; y++)
        for (var z = 0; z < Chunk.Depth; z++)
            chunk.SetBlock(9, y, z, 1);
        var lighting = ChunkLighting.Calculate(chunk, CreateBlocks());

        Assert.Equal((byte)14, lighting.GetBlockLight(8, 100, 8));
        Assert.Equal((byte)13, lighting.GetBlockLight(7, 100, 8));
        Assert.Equal((byte)0, lighting.GetBlockLight(9, 100, 8));
        Assert.Equal((byte)0, lighting.GetBlockLight(10, 100, 8));
    }

    [Fact]
    public void LightCanEnterFromLoadedNeighborChunk()
    {
        var chunk = new Chunk(0, 0);
        var lighting = ChunkLighting.Calculate(
            chunk,
            CreateBlocks(),
            neighborBlock: (x, y, z) => x == -1 && y == 80 && z == 4 ? (ushort)2 : (ushort)0);

        Assert.Equal((byte)13, lighting.GetBlockLight(0, 80, 4));
    }

    [Fact]
    public void AmbientOcclusionDarkensCornersBehindTwoSideBlocks()
    {
        var chunk = new Chunk(0, 0);
        chunk.SetBlock(6, 81, 5, 1);
        chunk.SetBlock(5, 81, 6, 1);
        var lighting = ChunkLighting.Calculate(chunk, CreateBlocks());

        Assert.Equal((byte)0, lighting.GetAmbientOcclusion(5, 80, 5, 1, 1, 0, 2, 1, 1));
    }
}
