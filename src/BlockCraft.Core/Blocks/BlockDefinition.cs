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
    /// <summary>Tool category that receives the intended mining-speed bonus.</summary>
    public string RequiredTool { get; init; } = "none";
    /// <summary>Minimum pickaxe tier required to receive this block's drop.</summary>
    public string RequiredToolTier { get; init; } = "none";
    /// <summary>Whether the player can target this block with a mining/place ray.</summary>
    public bool Selectable { get; init; } = true;
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

    /// <summary>Returns whether the supplied tool meets this block's minimum harvesting tier.</summary>
    public bool CanHarvestWith(string? toolType, string? toolTier)
    {
        if (string.IsNullOrWhiteSpace(RequiredToolTier) ||
            RequiredToolTier.Equals("none", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.Equals(toolType, "pickaxe", StringComparison.OrdinalIgnoreCase))
            return false;
        return TierRank(toolTier) >= TierRank(RequiredToolTier);
    }

    private static int TierRank(string? tier) => tier?.ToLowerInvariant() switch
    {
        "wood" or "gold" => 1,
        "stone" or "copper" => 2,
        "iron" => 3,
        "diamond" => 4,
        "netherite" => 5,
        _ => 0
    };
}
