using BlockCraft.Core.Blocks;
using BlockCraft.Core.Persistence;
using BlockCraft.Core.World;
using Xunit;

namespace BlockCraft.Tests;

public sealed class ChunkStreamingTests
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
      {"id":8,"name":"water","solid":false,"transparent":true,"selectable":false},
      {"id":9,"name":"bedrock"},
      {"id":10,"name":"coal_ore"},
      {"id":11,"name":"iron_ore"},
      {"id":12,"name":"gold_ore"},
      {"id":13,"name":"diamond_ore"}
    ]
    """);

    [Fact]
    public async Task FarChunksAreUnloadedAndCanBeLoadedAgainFromDisk()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BlockCraftTests", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new WorldStorage(directory);
            var blocks = CreateBlocks();
            var manager = new ChunkManager(new TerrainGenerator(1234, blocks), storage);
            await manager.RequestChunkAsync(new ChunkPos(0, 0));
            await manager.RequestChunkAsync(new ChunkPos(4, -3));
            Assert.True(manager.SetBlock(64, 80, -48, blocks.GetId("stone")));

            var unloaded = await manager.UnloadOutsideAsync(new ChunkPos(0, 0), keepRadius: 0);

            Assert.Contains(new ChunkPos(4, -3), unloaded);
            Assert.False(manager.TryGetChunk(new ChunkPos(4, -3), out _));
            Assert.Equal((ushort)0, manager.GetBlock(64, 80, -48));

            await manager.RequestChunkAsync(new ChunkPos(4, -3));
            Assert.Equal(blocks.GetId("stone"), manager.GetBlock(64, 80, -48));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}