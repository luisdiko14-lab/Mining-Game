namespace BlockCraft.Core.Items;

/// <summary>One shaped crafting recipe using character-to-item mappings.</summary>
public sealed record CraftingRecipe(string[] Pattern, Dictionary<char, ushort> Ingredients, ItemStack Result)
{
    /// <summary>Width of the recipe pattern.</summary>
    public int Width => Pattern.Length == 0 ? 0 : Pattern.Max(row => row.Length);
    /// <summary>Height of the recipe pattern.</summary>
    public int Height => Pattern.Length;
    /// <summary>Number of each ingredient item required by this pattern.</summary>
    public IReadOnlyDictionary<ushort, int> RequiredItems()
    {
        var result = new Dictionary<ushort, int>();
        foreach (var symbol in Pattern.SelectMany(row => row).Where(character => character != ' '))
        {
            if (!Ingredients.TryGetValue(symbol, out var itemId))
                throw new InvalidDataException($"Recipe pattern uses undefined ingredient '{symbol}'.");
            result[itemId] = result.GetValueOrDefault(itemId) + 1;
        }
        return result;
    }
}

/// <summary>Matches shaped recipes in 2×2 or 3×3 crafting grids.</summary>
public sealed partial class RecipeBook
{
    private readonly List<CraftingRecipe> _recipes;
    /// <summary>Loaded recipes in stable data-file order.</summary>
    public IReadOnlyList<CraftingRecipe> Recipes => _recipes.AsReadOnly();
    /// <summary>Number of available recipes.</summary>
    public int Count => _recipes.Count;
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

    /// <summary>Quick-crafts a recipe from inventory ingredients without a manual grid layout.</summary>
    /// <remarks>Missing ingredients or a full inventory leave all slots unchanged.</remarks>
    public bool TryCraftFromInventory(Inventory inventory, int recipeIndex)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if ((uint)recipeIndex >= (uint)_recipes.Count) return false;
        var recipe = _recipes[recipeIndex];
        var required = recipe.RequiredItems();
        if (required.Any(pair => !inventory.Contains(pair.Key, pair.Value))) return false;
        var before = inventory.Snapshot();
        foreach (var (itemId, count) in required)
        {
            if (inventory.Remove(itemId, count)) continue;
            RestoreInventory(inventory, before);
            return false;
        }
        if (inventory.Add(recipe.Result.ItemId, recipe.Result.Count) == 0) return true;
        RestoreInventory(inventory, before);
        return false;
    }

    private static void RestoreInventory(Inventory inventory, IReadOnlyList<ItemStack> snapshot)
    {
        for (var index = 0; index < snapshot.Count; index++) inventory.Set(index, snapshot[index]);
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
