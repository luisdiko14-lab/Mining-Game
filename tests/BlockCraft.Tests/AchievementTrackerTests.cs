using BlockCraft.Core.Player;
using Xunit;

namespace BlockCraft.Tests;

public sealed class AchievementTrackerTests
{
    [Fact]
    public void TracksMiningBuildingExplorationAndUnlocksAchievements()
    {
        var tracker = new AchievementTracker();
        tracker.RecordMine("stone", 64);
        for (var i = 0; i < 49; i++) tracker.RecordMine("stone", 63);
        for (var i = 0; i < 10; i++) tracker.RecordMine("iron_ore", 24);
        tracker.RecordMine("diamond_ore", 18);
        for (var i = 0; i < 5; i++) tracker.RecordMine("amethyst_block", 30);
        for (var i = 0; i < 100; i++) tracker.RecordPlace();
        for (var i = 0; i < 25; i++) tracker.RecordChunkVisit(i, -i);
        tracker.RecordDepth(31);

        var unlocked = tracker.GetAchievements().Where(value => value.IsUnlocked).Select(value => value.Id).ToHashSet();
        Assert.Contains("first_dig", unlocked);
        Assert.Contains("stone_worker", unlocked);
        Assert.Contains("ore_collector", unlocked);
        Assert.Contains("diamond_finder", unlocked);
        Assert.Contains("crystal_hunter", unlocked);
        Assert.Contains("deep_delver", unlocked);
        Assert.Contains("world_roamer", unlocked);
        Assert.Contains("master_builder", unlocked);
    }

    [Fact]
    public void SnapshotRoundTripsAndDeduplicatesExploredChunks()
    {
        var first = new AchievementTracker();
        first.RecordMine("echo_ore", 12);
        first.RecordMine("fossil_block", 20);
        first.RecordPlace();
        first.RecordChunkVisit(-3, 7);
        first.RecordChunkVisit(-3, 7);
        first.RecordChunkVisit(2, -8);
        first.RecordDepth(9);

        var second = new AchievementTracker();
        second.Restore(first.CreateSnapshot());

        Assert.Equal(first.CreateSnapshot(), second.CreateSnapshot());
        Assert.Equal(2, second.ChunksVisited);
        Assert.Contains(second.GetAchievements(), achievement => achievement.Id == "echo_resonance" && !achievement.IsUnlocked);
    }

    [Fact]
    public void RejectsCorruptExplorationCoordinates()
    {
        var tracker = new AchievementTracker();
        var snapshot = new AchievementSnapshot(0, 0, 0, 0, 0, 0, 0, 255, ["not-a-coordinate"]);
        Assert.Throws<InvalidDataException>(() => tracker.Restore(snapshot));
    }
}
