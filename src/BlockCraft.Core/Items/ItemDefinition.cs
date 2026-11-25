using System.Text.Json;

namespace BlockCraft.Core.Items;

/// <summary>Data-driven definition of an inventory item.</summary>
public sealed record ItemDefinition
{
    /// <summary>Stable numeric item id.</summary>
    public required ushort Id { get; init; }
    /// <summary>Unique internal item name.</summary>
    public required string Name { get; init; }
    /// <summary>Maximum number of items in one stack.</summary>
    public int MaxStack { get; init; } = 64;
    /// <summary>Tool category such as pickaxe, axe, shovel, or sword.</summary>
    public string ToolType { get; init; } = "none";
    /// <summary>Tool tier such as wood, stone, iron, gold, or diamond.</summary>
    public string ToolTier { get; init; } = "none";
    /// <summary>Mining speed multiplier for matching blocks.</summary>
    public float MiningSpeed { get; init; } = 1f;
    /// <summary>Food points restored when consumed.</summary>
    public int FoodValue { get; init; }
    /// <summary>Block id placed by this item, or zero for non-block items.</summary>
    public ushort PlacesBlockId { get; init; }
    /// <summary>Fuel energy in seconds; zero means the item cannot be burned.</summary>
    public float BurnTimeSeconds { get; init; }
}

/// <summary>Validated lookup for item definitions.</summary>
public sealed class ItemRegistry
{
    private readonly Dictionary<ushort, ItemDefinition> _byId = [];
    private readonly Dictionary<string, ItemDefinition> _byName = new(StringComparer.OrdinalIgnoreCase);

    private ItemRegistry(IEnumerable<ItemDefinition> items)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Name) || item.MaxStack is < 1 or > 999 || item.MiningSpeed < 0 || !float.IsFinite(item.MiningSpeed) || item.BurnTimeSeconds < 0 || !float.IsFinite(item.BurnTimeSeconds))
                throw new InvalidDataException($"Item {item.Id} has invalid definition data.");
            if (!_byId.TryAdd(item.Id, item) || !_byName.TryAdd(item.Name, item))
                throw new InvalidDataException($"Duplicate item id or name: {item.Name}.");
        }
    }

    /// <summary>Number of registered items.</summary>
    public int Count => _byId.Count;
    /// <summary>Gets an item by id.</summary>
    public ItemDefinition this[ushort id] => _byId.TryGetValue(id, out var value)
        ? value : throw new KeyNotFoundException($"Unknown item id {id}.");
    /// <summary>Gets an item id by internal name.</summary>
    public ushort GetId(string name) => _byName.TryGetValue(name, out var value)
        ? value.Id : throw new KeyNotFoundException($"Unknown item '{name}'.");

    /// <summary>Loads item definitions from JSON file.</summary>
    public static ItemRegistry LoadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        var items = JsonSerializer.Deserialize<List<ItemDefinition>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Item registry JSON was empty.");
        return new ItemRegistry(items);
    }

    /// <summary>Loads item definitions from a JSON string.</summary>
    public static ItemRegistry LoadFromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var items = JsonSerializer.Deserialize<List<ItemDefinition>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Item registry JSON was empty.");
        return new ItemRegistry(items);
    }
}
