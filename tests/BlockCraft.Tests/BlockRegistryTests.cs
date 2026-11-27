using BlockCraft.Core.Blocks;
using Xunit;

namespace BlockCraft.Tests;

public sealed class BlockRegistryTests
{
    private const string ValidJson = """
    [
      {"id":0,"name":"air","solid":false,"transparent":true},
      {"id":1,"name":"stone","solid":true,"transparent":false}
    ]
    """;

    [Fact]
    public void NonSolidBlocksCanBeSelectableAndTierGatedDropsAreEnforced()
    {
        var registry = BlockRegistry.LoadFromJson("""
        [
          {"id":0,"name":"air","solid":false,"transparent":true,"selectable":false},
          {"id":1,"name":"torch","solid":false,"transparent":true,"hardness":0.2,"dropItemId":1,"selectable":true},
          {"id":2,"name":"diamond_ore","requiredTool":"pickaxe","requiredToolTier":"iron"}
        ]
        """);

        Assert.False(registry[0].Selectable);
        Assert.True(registry[1].Selectable);
        Assert.True(registry[2].CanHarvestWith("pickaxe", "iron"));
        Assert.True(registry[2].CanHarvestWith("pickaxe", "diamond"));
        Assert.False(registry[2].CanHarvestWith("pickaxe", "stone"));
        Assert.False(registry[2].CanHarvestWith("axe", "netherite"));
    }

    [Fact]
    public void LoadsDefinitionsAndLooksUpIdsWithoutCaseSensitivity()
    {
        var registry = BlockRegistry.LoadFromJson(ValidJson);
        Assert.Equal(2, registry.Count);
        Assert.Equal((ushort)1, registry.GetId("STONE"));
        Assert.Equal("stone", registry[1].Name);
        Assert.False(registry[1].Transparent);
    }

    [Fact]
    public void RejectsDuplicateIds()
    {
        const string json = """
        [
          {"id":0,"name":"air","solid":false},
          {"id":0,"name":"stone","solid":true}
        ]
        """;
        Assert.Throws<InvalidDataException>(() => BlockRegistry.LoadFromJson(json));
    }

    [Fact]
    public void RequiresAirAtZero()
    {
        const string json = """[{"id":1,"name":"stone"}]""";
        Assert.Throws<InvalidDataException>(() => BlockRegistry.LoadFromJson(json));
    }
}
