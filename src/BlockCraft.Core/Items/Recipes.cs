namespace BlockCraft.Core.Items;

/// <summary>One shaped crafting recipe using character-to-item mappings.</summary>
public sealed record CraftingRecipe(string[] Pattern, Dictionary<char, ushort> Ingredients, ItemStack Result)
{
    /// <summary>Width of the recipe pattern.</summary>
    public int Width => Pattern.Length == 0 ? 0 : Pattern.Max(row => row.Length);
    /// <summary>Height of the recipe pattern.</summary>
    public int Height => Pattern.Length;
}

/// <summary>Matches shaped recipes in 2×2 or 3×3 crafting grids.</summary>
public sealed partial class RecipeBook
{
    private readonly List<CraftingRecipe> _recipes;
    /// <summary>Creates a recipe book from validated recipe definitions.</summary>
    public RecipeBook(IEnumerable<CraftingRecipe> recipes)
    {
        _recipes = recipes?.ToList() ?? throw new ArgumentNullException(nameof(recipes));
        if (_recipes.Any(recipe => recipe.Pattern.Length is < 1 or > 3 || recipe.Width is < 1 or > 3 ||
                                    recipe.Pattern.Any(row => row.Length != recipe.Width)))
            throw new InvalidDataException("Crafting recipes must be rectangular and fit a 3×3 grid.");
    }

    /// <summary>Attempts to craft one recipe and consumes matching items from the grid on success.</summary>
    public bool TryCraft(ItemStack[,] grid, out ItemStack crafted)
    {
        ArgumentNullException.ThrowIfNull(grid);
        crafted = ItemStack.Empty;
        var height = grid.GetLength(0);
        var width = grid.GetLength(1);
        if (height is < 2 or > 3 || width is < 2 or > 3)
            throw new ArgumentOutOfRangeException(nameof(grid), "Crafting grid must be 2×2 or 3×3.");

        foreach (var recipe in _recipes)
        {
            if (recipe.Width > width || recipe.Height > height) continue;
            for (var offsetY = 0; offsetY <= height - recipe.Height; offsetY++)
            for (var offsetX = 0; offsetX <= width - recipe.Width; offsetX++)
            {
                if (!Matches(grid, recipe, offsetX, offsetY, width, height)) continue;
                foreach (var (character, itemId) in recipe.Ingredients)
                {
                    for (var y = 0; y < recipe.Height; y++)
                    for (var x = 0; x < recipe.Width; x++)
                    {
                        if (recipe.Pattern[y][x] != character) continue;
                        var row = offsetY + y; var column = offsetX + x;
                        var current = grid[row, column];
                        grid[row, column] = current.Count == 1 ? ItemStack.Empty : current with { Count = current.Count - 1 };
                    }
                }
                crafted = recipe.Result;
                return true;
            }
        }
        return false;
    }

    private static bool Matches(ItemStack[,] grid, CraftingRecipe recipe, int offsetX, int offsetY, int width, int height)
    {
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var px = x - offsetX; var py = y - offsetY;
            var expected = px >= 0 && py >= 0 && py < recipe.Height && px < recipe.Width
                ? recipe.Pattern[py][px] : ' ';
            var actual = grid[y, x];
            if (expected == ' ')
            {
                if (!actual.IsEmpty) return false;
            }
            else if (!recipe.Ingredients.TryGetValue(expected, out var itemId) || actual.ItemId != itemId || actual.Count < 1)
                return false;
        }
        return true;
    }
}
