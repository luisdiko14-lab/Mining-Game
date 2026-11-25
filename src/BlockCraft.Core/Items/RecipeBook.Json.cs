using System.Text.Json;

namespace BlockCraft.Core.Items;

/// <summary>JSON data-pack loading for shaped crafting recipes.</summary>
public sealed partial class RecipeBook
{
    /// <summary>Loads shaped recipes from JSON, resolving item names through the supplied registry.</summary>
    public static RecipeBook LoadFromFile(string path, ItemRegistry items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(items);
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Recipe file root must be an array.");

        var recipes = new List<CraftingRecipe>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var pattern = element.GetProperty("pattern").EnumerateArray()
                .Select(row => row.GetString() ?? throw new InvalidDataException("Recipe pattern rows must be strings."))
                .ToArray();
            var ingredients = new Dictionary<char, ushort>();
            foreach (var property in element.GetProperty("ingredients").EnumerateObject())
            {
                if (property.Name.Length != 1)
                    throw new InvalidDataException($"Recipe ingredient key '{property.Name}' must be one character.");
                var itemName = property.Value.GetString()
                    ?? throw new InvalidDataException($"Recipe ingredient '{property.Name}' must be an item name.");
                ingredients.Add(property.Name[0], items.GetId(itemName));
            }

            var resultElement = element.GetProperty("result");
            var resultName = resultElement.GetProperty("item").GetString()
                ?? throw new InvalidDataException("Recipe result item must be a name.");
            var resultId = items.GetId(resultName);
            var resultCount = resultElement.GetProperty("count").GetInt32();
            if (resultCount < 1 || resultCount > items[resultId].MaxStack)
                throw new InvalidDataException($"Recipe result '{resultName}' has an invalid stack size.");
            recipes.Add(new CraftingRecipe(pattern, ingredients, new ItemStack(resultId, resultCount)));
        }
        return new RecipeBook(recipes);
    }
}
