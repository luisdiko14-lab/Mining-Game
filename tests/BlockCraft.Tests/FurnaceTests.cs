using BlockCraft.Core.Items;
using Xunit;

namespace BlockCraft.Tests;

public sealed class FurnaceTests
{
    private static ItemRegistry CreateItems() => ItemRegistry.LoadFromJson("""
    [
      {"id":1,"name":"raw_iron","maxStack":64},
      {"id":2,"name":"iron_ingot","maxStack":64},
      {"id":3,"name":"coal","maxStack":64,"burnTimeSeconds":80},
      {"id":4,"name":"stone","maxStack":64}
    ]
    """);
    private static SmeltingRecipeRegistry CreateRecipes() => new([new SmeltingRecipe(1, new ItemStack(2, 1), 10f)]);

    [Fact]
    public void LoadsRecipesByItemNameFromJson()
    {
        var recipes = SmeltingRecipeRegistry.LoadFromJson(
            """[{"input":"raw_iron","output":"iron_ingot","count":1,"durationSeconds":10}]""", CreateItems());
        Assert.Equal(1, recipes.Count);
        Assert.True(recipes.TryGet(1, out var recipe));
        Assert.Equal(new ItemStack(2, 1), recipe.Result);
    }

    [Fact]
    public void SmeltsInputUsingFuelAndProducesOutput()
    {
        var furnace = new FurnaceState(CreateItems(), CreateRecipes());
        Assert.True(furnace.InsertInput(new ItemStack(1, 2)).IsEmpty);
        Assert.True(furnace.InsertFuel(new ItemStack(3, 1)).IsEmpty);
        furnace.Update(9.5f);
        Assert.True(furnace.Output.IsEmpty);
        Assert.Equal(0.5f, furnace.SmeltProgressSeconds, 3);
        furnace.Update(0.5f);
        Assert.Equal(new ItemStack(2, 1), furnace.Output);
        Assert.Equal(new ItemStack(1, 1), furnace.Input);
        Assert.Equal(70f, furnace.BurnRemainingSeconds, 3);
        Assert.True(furnace.Fuel.IsEmpty);
    }

    [Fact]
    public void NonFuelItemsCannotEnterFuelSlot()
    {
        var furnace = new FurnaceState(CreateItems(), CreateRecipes());
        Assert.Equal(new ItemStack(4, 10), furnace.InsertFuel(new ItemStack(4, 10)));
        Assert.True(furnace.Fuel.IsEmpty);
    }

    [Fact]
    public void OutputCanBeCollectedIntoInventory()
    {
        var items = CreateItems();
        var furnace = new FurnaceState(items, CreateRecipes());
        furnace.InsertInput(new ItemStack(1, 1));
        furnace.InsertFuel(new ItemStack(3, 1));
        furnace.Update(10f);
        var inventory = new Inventory(items, 1);
        Assert.True(furnace.TakeOutput(inventory));
        Assert.Equal(new ItemStack(2, 1), inventory[0]);
        Assert.True(furnace.Output.IsEmpty);
    }

    [Fact]
    public void FullOutputSlotPreventsSmelting()
    {
        var furnace = new FurnaceState(CreateItems(), CreateRecipes());
        furnace.Restore(new FurnaceSnapshot(new ItemStack(1, 1), new ItemStack(3, 1),
            new ItemStack(2, 64), 0f, 0f, 0f));
        furnace.Update(10f);
        Assert.Equal(new ItemStack(1, 1), furnace.Input);
        Assert.Equal(new ItemStack(2, 64), furnace.Output);
        Assert.Equal(0f, furnace.BurnRemainingSeconds);
    }
}
