using BlockCraft.Core.Items;
using Xunit;

namespace BlockCraft.Tests;

public sealed class InventoryTests
{
    private static ItemRegistry CreateItems() => ItemRegistry.LoadFromJson("""
    [
      {"id":1,"name":"stone","maxStack":64},
      {"id":2,"name":"sword","maxStack":1}
    ]
    """);

    [Fact]
    public void AddSplitsAcrossStacksAndFillsExistingStacksBeforeReportingOverflow()
    {
        var inventory = new Inventory(CreateItems(), 2);
        Assert.Equal(0, inventory.Add(1, 100));
        Assert.Equal(new ItemStack(1, 64), inventory[0]);
        Assert.Equal(new ItemStack(1, 36), inventory[1]);
        Assert.Equal(0, inventory.Add(1, 5));
        Assert.Equal(new ItemStack(1, 41), inventory[1]);
        Assert.Equal(27, inventory.Add(1, 50));
    }

    [Fact]
    public void FailedRemovalDoesNotMutateInventory()
    {
        var inventory = new Inventory(CreateItems(), 2);
        inventory.Add(1, 12);
        Assert.False(inventory.Remove(1, 13));
        Assert.Equal(new ItemStack(1, 12), inventory[0]);
    }

    [Fact]
    public void NonStackableItemsOccupySeparateSlots()
    {
        var inventory = new Inventory(CreateItems(), 2);
        Assert.Equal(0, inventory.Add(2, 2));
        Assert.Equal(new ItemStack(2, 1), inventory[0]);
        Assert.Equal(new ItemStack(2, 1), inventory[1]);
    }
}
