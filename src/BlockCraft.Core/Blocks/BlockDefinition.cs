namespace BlockCraft.Core.Blocks;

/// <summary>Describes a block's visual, physical, mining, and drop properties.</summary>
public sealed record BlockDefinition
{
    /// <summary>Stable numeric block identifier. Zero is reserved for air.</summary>
    public required ushort Id { get; init; }
    /// <summary>Unique, stable internal name.</summary>
    public required string Name { get; init; }
    /// <summary>Texture keys for the top, side, and bottom faces.</summary>
    public Dictionary<string, string> Textures { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Mining time multiplier. A value of zero means unbreakable.</summary>
    public float Hardness { get; init; } = 1f;
    /// <summary>Tool category required for the intended drop.</summary>
    public string RequiredTool { get; init; } = "none";
    /// <summary>Item identifier dropped when this block is mined.</summary>
    public ushort DropItemId { get; init; }
    /// <summary>Whether the block can be seen through.</summary>
    public bool Transparent { get; init; }
    /// <summary>Whether the block blocks player movement.</summary>
    public bool Solid { get; init; } = true;
    /// <summary>Emitted block-light level, from zero through fifteen.</summary>
    public byte LightEmission { get; init; }
    /// <summary>Gets whether this block occludes faces belonging to adjacent blocks.</summary>
    public bool OccludesFaces => Solid && !Transparent;
}
