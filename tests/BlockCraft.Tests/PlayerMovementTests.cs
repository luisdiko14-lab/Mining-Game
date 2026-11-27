using BlockCraft.Core.Blocks;
using BlockCraft.Core.Persistence;
using BlockCraft.Core.Player;
using BlockCraft.Core.World;
using System.Numerics;
using Xunit;

namespace BlockCraft.Tests;

public sealed class PlayerMovementTests
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
    public async Task WalkingOffALedgeClearsGroundedStateAndPreventsAirJump()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BlockCraftTests", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new WorldStorage(directory);
            var blocks = CreateBlocks();
            var floor = new Chunk(0, 0);
            floor.SetBlock(0, 0, 0, blocks.GetId("stone"));
            await storage.SaveChunkAsync(floor);
            var world = new ChunkManager(new TerrainGenerator(7, blocks), storage);
            await world.RequestChunkAsync(new ChunkPos(0, 0));

            var player = new PlayerController(blocks, new Vector3(0.5f, 1.05f, 0.5f));
            player.Update(world, Vector2.Zero, 0.05f, jump: false, sprint: false, sneak: false);
            Assert.True(player.IsGrounded);

            for (var i = 0; i < 8; i++)
                player.Update(world, new Vector2(1f, 0f), 0.05f, jump: false, sprint: false, sneak: false);

            Assert.False(player.IsGrounded);
            var airborneHeight = player.Position.Y;
            player.Update(world, Vector2.Zero, 0.05f, jump: true, sprint: false, sneak: false);
            Assert.True(player.Position.Y < airborneHeight + 0.2f);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
