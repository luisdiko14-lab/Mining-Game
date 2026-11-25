namespace BlockCraft.Core.World;

/// <summary>Deterministic day/night clock measured in in-game seconds.</summary>
public sealed class WorldClock
{
    /// <summary>Length of one full in-game day in real-time seconds.</summary>
    public const float DayLengthSeconds = 1200f;
    /// <summary>Creates a world clock at the supplied time-of-day fraction.</summary>
    public WorldClock(float timeOfDay = 0.25f) => TimeOfDay = Wrap(timeOfDay);
    /// <summary>Time fraction from zero (midnight) through one (next midnight).</summary>
    public float TimeOfDay { get; private set; }
    /// <summary>Advances the clock.</summary>
    public void Update(float elapsedSeconds) => TimeOfDay = Wrap(TimeOfDay + Math.Max(0, elapsedSeconds) / DayLengthSeconds);
    /// <summary>Approximate daylight amount, clamped to 0..1.</summary>
    public float Daylight => Math.Clamp((MathF.Sin((TimeOfDay - 0.25f) * MathF.Tau) + 0.18f) / 0.7f, 0.08f, 1f);
    private static float Wrap(float value) => value - MathF.Floor(value);
}
