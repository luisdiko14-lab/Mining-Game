using BlockCraft.Core.Player;
using Xunit;

namespace BlockCraft.Tests;

public sealed class SurvivalStatsTests
{
    [Fact]
    public void FallDamageUsesThreeBlockSafeDistance()
    {
        var stats = new SurvivalStats();
        stats.ApplyFallDamage(3f);
        Assert.Equal(20f, stats.Health);
        stats.ApplyFallDamage(5f);
        Assert.Equal(16f, stats.Health);
    }

    [Fact]
    public void ExhaustionConsumesSaturationBeforeHunger()
    {
        var stats = new SurvivalStats();
        stats.AddExhaustion(4f);
        Assert.Equal(4f, stats.Saturation);
        Assert.Equal(20f, stats.Hunger);
        stats.AddExhaustion(20f);
        Assert.Equal(0f, stats.Saturation);
        Assert.Equal(19f, stats.Hunger);
    }

    [Fact]
    public void FoodRestoresHungerAndSaturation()
    {
        var stats = new SurvivalStats();
        stats.AddExhaustion(24f);
        Assert.Equal(19f, stats.Hunger);
        Assert.Equal(0f, stats.Saturation);
        Assert.True(stats.ConsumeFood(5, 2f));
        Assert.Equal(20f, stats.Hunger);
        Assert.Equal(2f, stats.Saturation);
    }

    [Fact]
    public void StarvationCannotKillPlayer()
    {
        var stats = new SurvivalStats();
        stats.AddExhaustion(100f);
        stats.Damage(19f);
        stats.Update(16f, moving: false, sprinting: false);
        Assert.True(stats.IsAlive);
        Assert.Equal(1f, stats.Health);
    }

    [Fact]
    public void DamageCannotTakeHealthBelowZero()
    {
        var stats = new SurvivalStats();
        stats.Damage(500);
        stats.Damage(2);
        Assert.Equal(0f, stats.Health);
        Assert.False(stats.IsAlive);
    }
}
