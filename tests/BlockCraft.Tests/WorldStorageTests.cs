using BlockCraft.Core.World;
using BlockCraft.Core.Persistence;
using Xunit;

namespace BlockCraft.Tests;

public sealed class WorldStorageTests
{
    [Fact]
    public async Task SavesAndLoadsNegativeCoordinateChunkInRegionFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BlockCraftTests", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new WorldStorage(directory);
            var chunk = new Chunk(-1, -33);
            chunk.SetBlock(4, 72, 8, 15);
            await storage.SaveChunkAsync(chunk);
            var loaded = storage.LoadChunk(new ChunkPos(-1, -33));
            Assert.NotNull(loaded);
            Assert.Equal((ushort)15, loaded!.GetBlock(4, 72, 8));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
