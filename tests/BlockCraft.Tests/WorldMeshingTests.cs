using BlockCraft.Core.Blocks;
using BlockCraft.Core.World;
using Xunit;

namespace BlockCraft.Tests;

public sealed class WorldMeshingTests
{
    private static BlockRegistry CreateBlocks() => BlockRegistry.LoadFromJson("""
    [
      {"id":0,"name":"air","solid":false,"transparent":true},
      {"id":1,"name":"stone","solid":true,"transparent":false},
      {"id":2,"name":"water","solid":false,"transparent":true}
    ]
    """);

    [Fact]
    public void SingleVoxelProducesSixExposedQuads()
    {
        var registry = CreateBlocks();
        var quads = GreedyMesher.Build((x, y, z) => x == 0 && y == 0 && z == 0 ? (ushort)1 : (ushort)0, registry);
        Assert.Equal(6, quads.Count);
        Assert.All(quads, quad => Assert.Equal((ushort)1, quad.BlockId));
    }

    [Fact]
    public void AdjacentVoxelsMergeCoplanarFaces()
    {
        var registry = CreateBlocks();
        var quads = GreedyMesher.Build((x, y, z) => y == 0 && z == 0 && x is 0 or 1 ? (ushort)1 : (ushort)0, registry);
        Assert.Equal(6, quads.Count);
    }

    [Fact]
    public void NonSolidTransparentVoxelsStillProduceVisibleFaces()
    {
        var registry = CreateBlocks();
        var quads = GreedyMesher.Build((x, y, z) => x == 0 && y == 0 && z == 0 ? (ushort)2 : (ushort)0, registry);
        Assert.Equal(6, quads.Count);
        Assert.All(quads, quad => Assert.Equal((ushort)2, quad.BlockId));
    }
}
