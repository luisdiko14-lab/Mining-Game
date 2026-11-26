using System.Text.Json;
using BlockCraft.Core.Blocks;
using BlockCraft.Core.Items;
using Xunit;

namespace BlockCraft.Tests;

public sealed class ContentPackTests
{
    [Fact]
    public void ExpandedContentPackHasValidCrossReferencesAndRecipes()
    {
        var data = FindDataDirectory();
        var blocksPath = Path.Combine(data, "blocks.json");
        var itemsPath = Path.Combine(data, "items.json");
        var blocks = BlockRegistry.LoadFromFile(blocksPath);
        var items = ItemRegistry.LoadFromFile(itemsPath);
        var recipes = RecipeBook.LoadFromFile(Path.Combine(data, "recipes.json"), items);
        var smelting = SmeltingRecipeRegistry.LoadFromFile(Path.Combine(data, "smelting.json"), items);
        Assert.True(blocks.Count >= 100); Assert.True(items.Count >= 150);
        Assert.True(recipes.Count >= 25); Assert.True(smelting.Count >= 8);
        using var blockDocument = JsonDocument.Parse(File.ReadAllText(blocksPath));
        foreach (var block in blockDocument.RootElement.EnumerateArray())
        {
            var drop = block.GetProperty("dropItemId").GetUInt16();
            if (drop != 0) _ = items[drop];
        }
        using var itemDocument = JsonDocument.Parse(File.ReadAllText(itemsPath));
        foreach (var item in itemDocument.RootElement.EnumerateArray())
            if (item.TryGetProperty("placesBlockId", out var place) && place.GetUInt16() != 0)
                Assert.True(blocks.Contains(place.GetUInt16()));
    }
    [Fact]
    public void QuickCraftConsumesIngredientsAndCreatesResult()
    {
        var data = FindDataDirectory(); var items = ItemRegistry.LoadFromFile(Path.Combine(data, "items.json"));
        var recipes = RecipeBook.LoadFromFile(Path.Combine(data, "recipes.json"), items);
        var inventory = new Inventory(items, 36); inventory.Add(items.GetId("oak_log"), 1);
        Assert.True(recipes.TryCraftFromInventory(inventory, 0));
        Assert.False(inventory.Contains(items.GetId("oak_log"), 1));
        Assert.True(inventory.Contains(items.GetId("oak_planks"), 4));
    }
    [Fact]
    public void FailedQuickCraftLeavesInventoryUnchanged()
    {
        var data = FindDataDirectory(); var items = ItemRegistry.LoadFromFile(Path.Combine(data, "items.json"));
        var recipes = RecipeBook.LoadFromFile(Path.Combine(data, "recipes.json"), items);
        var inventory = new Inventory(items, 1); inventory.Add(items.GetId("oak_log"), 64);
        var before = inventory.Snapshot();
        Assert.False(recipes.TryCraftFromInventory(inventory, 0));
        Assert.Equal(before, inventory.Snapshot());
    }
    private static string FindDataDirectory()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "assets", "data");
                if (File.Exists(Path.Combine(candidate, "blocks.json"))) return candidate;
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException("Could not locate assets/data for content-pack tests.");
    }
}