namespace BlockCraft.Core.Player;

/// <summary>
/// Persistent lifetime statistics and milestone achievements for one local world.
/// Progress is deterministic and safe to serialize between game sessions.
/// </summary>
public sealed class AchievementTracker
{
    private readonly HashSet<string> _visitedChunks = new(StringComparer.Ordinal);

    public int BlocksMined { get; private set; }
    public int OreBlocksMined { get; private set; }
    public int DiamondOresMined { get; private set; }
    public int AmethystBlocksMined { get; private set; }
    public int EchoOresMined { get; private set; }
    public int FossilBlocksMined { get; private set; }
    public int BlocksPlaced { get; private set; }
    public int ChunksVisited => _visitedChunks.Count;
    public int DeepestY { get; private set; } = 255;

    public void RecordMine(string blockName, int y)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        BlocksMined = SaturatingIncrement(BlocksMined);
        var name = blockName.ToLowerInvariant();
        if (name.Contains("ore", StringComparison.Ordinal))
            OreBlocksMined = SaturatingIncrement(OreBlocksMined);
        if (name.Contains("diamond_ore", StringComparison.Ordinal))
            DiamondOresMined = SaturatingIncrement(DiamondOresMined);
        if (name == "amethyst_block")
            AmethystBlocksMined = SaturatingIncrement(AmethystBlocksMined);
        if (name == "echo_ore")
            EchoOresMined = SaturatingIncrement(EchoOresMined);
        if (name == "fossil_block")
            FossilBlocksMined = SaturatingIncrement(FossilBlocksMined);
        RecordDepth(y);
    }

    public void RecordPlace() => BlocksPlaced = SaturatingIncrement(BlocksPlaced);

    public void RecordChunkVisit(int chunkX, int chunkZ) =>
        _visitedChunks.Add($"{chunkX.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{chunkZ.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

    public void RecordDepth(int y)
    {
        if (y is >= 0 and < 256)
            DeepestY = Math.Min(DeepestY, y);
    }

    public IReadOnlyList<AchievementStatus> GetAchievements()
    {
        var values = new[]
        {
            new AchievementStatus("first_dig", "First Steps", "Mine your very first block.", BlocksMined, 1),
            new AchievementStatus("stone_worker", "Stone Worker", "Mine 50 blocks.", BlocksMined, 50),
            new AchievementStatus("ore_collector", "Ore Collector", "Mine 10 ore blocks.", OreBlocksMined, 10),
            new AchievementStatus("diamond_finder", "Diamond Finder", "Discover and mine a diamond ore.", DiamondOresMined, 1),
            new AchievementStatus("crystal_hunter", "Crystal Hunter", "Mine 5 amethyst blocks from geodes.", AmethystBlocksMined, 5),
            new AchievementStatus("deep_delver", "Deep Delver", "Reach the deep-cave layers below Y=32.", DeepestY <= 32 ? 1 : 0, 1),
            new AchievementStatus("world_roamer", "World Roamer", "Visit 25 different chunks.", ChunksVisited, 25),
            new AchievementStatus("master_builder", "Master Builder", "Place 100 blocks.", BlocksPlaced, 100),
            new AchievementStatus("echo_resonance", "Echo Resonance", "Mine 5 rare echo ores.", EchoOresMined, 5),
            new AchievementStatus("fossil_record", "Fossil Record", "Uncover an underground fossil.", FossilBlocksMined, 1)
        };
        return Array.AsReadOnly(values);
    }

    public AchievementSnapshot CreateSnapshot() => new(
        BlocksMined, OreBlocksMined, DiamondOresMined, AmethystBlocksMined,
        EchoOresMined, FossilBlocksMined, BlocksPlaced, DeepestY,
        _visitedChunks.OrderBy(value => value, StringComparer.Ordinal).ToArray());

    public void Restore(AchievementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.BlocksMined < 0 || snapshot.OreBlocksMined < 0 ||
            snapshot.DiamondOresMined < 0 || snapshot.AmethystBlocksMined < 0 ||
            snapshot.EchoOresMined < 0 || snapshot.FossilBlocksMined < 0 ||
            snapshot.BlocksPlaced < 0 || snapshot.DeepestY is < 0 or > 255 ||
            snapshot.VisitedChunks is null || snapshot.VisitedChunks.Length > 250_000)
            throw new InvalidDataException("Achievement save data is invalid.");

        var restored = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in snapshot.VisitedChunks)
        {
            var parts = value.Split(':');
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var x) ||
                !int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var z))
                throw new InvalidDataException("Achievement save contains an invalid explored-chunk coordinate.");
            restored.Add($"{x.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{z.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        BlocksMined = snapshot.BlocksMined;
        OreBlocksMined = snapshot.OreBlocksMined;
        DiamondOresMined = snapshot.DiamondOresMined;
        AmethystBlocksMined = snapshot.AmethystBlocksMined;
        EchoOresMined = snapshot.EchoOresMined;
        FossilBlocksMined = snapshot.FossilBlocksMined;
        BlocksPlaced = snapshot.BlocksPlaced;
        DeepestY = snapshot.DeepestY;
        _visitedChunks.Clear();
        _visitedChunks.UnionWith(restored);
    }

    private static int SaturatingIncrement(int value) => value == int.MaxValue ? value : value + 1;
}

public sealed record AchievementStatus(string Id, string Title, string Description, int Progress, int Goal)
{
    public bool IsUnlocked => Progress >= Goal;
    public float Completion => Math.Clamp(Progress / (float)Goal, 0f, 1f);
}

public sealed record AchievementSnapshot(
    int BlocksMined, int OreBlocksMined, int DiamondOresMined,
    int AmethystBlocksMined, int EchoOresMined, int FossilBlocksMined,
    int BlocksPlaced, int DeepestY, string[] VisitedChunks);
