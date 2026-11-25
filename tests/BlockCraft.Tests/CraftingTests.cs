using BlockCraft.Core.Items;
using Xunit;

namespace BlockCraft.Tests;

public sealed class CraftingTests
{
    [Fact]
    public void TwoByTwoRecipeConsumesInputsAndReturnsProduct()
    {
        var recipe = new CraftingRecipe(
            ["PP", "PP"],
            new Dictionary<char, ushort> { ['P'] = 1 },
            new ItemStack(2, 1));
        var book = new RecipeBook([recipe]);
        var grid = new ItemStack[2, 2];
        for (var y = 0; y < 2; y++)
        for (var x = 0; x < 2; x++)
            grid[y, x] = new ItemStack(1, 1);

        Assert.True(book.TryCraft(grid, out var crafted));
        Assert.Equal(new ItemStack(2, 1), crafted);
        Assert.All(grid.Cast<ItemStack>(), stack => Assert.True(stack.IsEmpty));
    }

    [Fact]
    public void ThreeByThreeRecipeMatchesCenteredPattern()
    {
        var recipe = new CraftingRecipe(
            [" S ", " S ", " P "],
            new Dictionary<char, ushort> { ['S'] = 1, ['P'] = 2 },
            new ItemStack(3, 1));
        var book = new RecipeBook([recipe]);
        var grid = new ItemStack[3, 3];
        grid[0, 1] = new ItemStack(1, 1);
        grid[1, 1] = new ItemStack(1, 1);
        grid[2, 1] = new ItemStack(2, 1);

        Assert.True(book.TryCraft(grid, out var crafted));
        Assert.Equal(new ItemStack(3, 1), crafted);
        Assert.All(grid.Cast<ItemStack>(), stack => Assert.True(stack.IsEmpty));
    }
}
