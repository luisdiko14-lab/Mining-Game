using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlockCraft.Core.Blocks;

/// <summary>Validated, immutable lookup table for data-driven block definitions.</summary>
public sealed class BlockRegistry
{
    private readonly Dictionary<ushort, BlockDefinition> _byId = [];
    private readonly Dictionary<string, BlockDefinition> _byName = new(StringComparer.OrdinalIgnoreCase);

    private BlockRegistry(IEnumerable<BlockDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.Name))
                throw new InvalidDataException($"Block {definition.Id} has no name.");
            if (definition.Hardness < 0 || float.IsNaN(definition.Hardness))
                throw new InvalidDataException($"Block {definition.Name} has invalid hardness.");
            if (definition.LightEmission > 15)
                throw new InvalidDataException($"Block {definition.Name} emits more than 15 light.");
            if (!_byId.TryAdd(definition.Id, definition))
                throw new InvalidDataException($"Duplicate block id: {definition.Id}.");
            if (!_byName.TryAdd(definition.Name, definition))
                throw new InvalidDataException($"Duplicate block name: {definition.Name}.");
        }
        if (!_byId.TryGetValue(0, out var air) ||
            !string.Equals(air.Name, "air", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Block id 0 must be named 'air'.");
    }

    /// <summary>Number of registered block definitions.</summary>
    public int Count => _byId.Count;
    /// <summary>Gets a definition by numeric id, throwing when it is unknown.</summary>
    public BlockDefinition this[ushort id] => _byId.TryGetValue(id, out var value)
        ? value : throw new KeyNotFoundException($"Unknown block id {id}.");
    /// <summary>Gets an id by its stable internal name, throwing when it is unknown.</summary>
    public ushort GetId(string name) => _byName.TryGetValue(name, out var value)
        ? value.Id : throw new KeyNotFoundException($"Unknown block '{name}'.");
    /// <summary>Returns whether a numeric id is registered.</summary>
    public bool Contains(ushort id) => _byId.ContainsKey(id);

    /// <summary>Loads and validates a registry from a JSON file.</summary>
    public static BlockRegistry LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    /// <summary>Loads and validates a registry from a readable stream.</summary>
    public static BlockRegistry Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var values = JsonSerializer.Deserialize<List<BlockDefinition>>(stream, JsonOptions())
            ?? throw new InvalidDataException("Block registry JSON was empty.");
        return new BlockRegistry(values);
    }

    /// <summary>Loads and validates a registry from a JSON string.</summary>
    public static BlockRegistry LoadFromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var values = JsonSerializer.Deserialize<List<BlockDefinition>>(json, JsonOptions())
            ?? throw new InvalidDataException("Block registry JSON was empty.");
        return new BlockRegistry(values);
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
