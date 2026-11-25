namespace BlockCraft.Core.World;

/// <summary>Deterministic smooth value noise helpers for reproducible terrain generation.</summary>
public static class Noise
{
    /// <summary>Samples smooth two-dimensional value noise in approximately the range -1 to 1.</summary>
    public static float Sample2D(int seed, float x, float z)
    {
        var x0 = (int)MathF.Floor(x); var z0 = (int)MathF.Floor(z);
        var tx = Fade(x - x0); var tz = Fade(z - z0);
        var a = Lerp(Hash(seed, x0, 0, z0), Hash(seed, x0 + 1, 0, z0), tx);
        var b = Lerp(Hash(seed, x0, 0, z0 + 1), Hash(seed, x0 + 1, 0, z0 + 1), tx);
        return Lerp(a, b, tz);
    }

    /// <summary>Samples smooth three-dimensional value noise in approximately the range -1 to 1.</summary>
    public static float Sample3D(int seed, float x, float y, float z)
    {
        var x0 = (int)MathF.Floor(x); var y0 = (int)MathF.Floor(y); var z0 = (int)MathF.Floor(z);
        var tx = Fade(x - x0); var ty = Fade(y - y0); var tz = Fade(z - z0);
        var c00 = Lerp(Hash(seed, x0, y0, z0), Hash(seed, x0 + 1, y0, z0), tx);
        var c10 = Lerp(Hash(seed, x0, y0 + 1, z0), Hash(seed, x0 + 1, y0 + 1, z0), tx);
        var c01 = Lerp(Hash(seed, x0, y0, z0 + 1), Hash(seed, x0 + 1, y0, z0 + 1), tx);
        var c11 = Lerp(Hash(seed, x0, y0 + 1, z0 + 1), Hash(seed, x0 + 1, y0 + 1, z0 + 1), tx);
        return Lerp(Lerp(c00, c10, ty), Lerp(c01, c11, ty), tz);
    }

    /// <summary>Samples normalized fractal Brownian motion over two-dimensional noise.</summary>
    public static float Fractal2D(int seed, float x, float z, int octaves = 4, float lacunarity = 2f, float persistence = 0.5f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(octaves);
        var sum = 0f; var amplitude = 1f; var normalizer = 0f; var frequency = 1f;
        for (var octave = 0; octave < octaves; octave++)
        {
            sum += Sample2D(seed + octave * 1013, x * frequency, z * frequency) * amplitude;
            normalizer += amplitude; amplitude *= persistence; frequency *= lacunarity;
        }
        return sum / normalizer;
    }

    /// <summary>Maps integer lattice coordinates and a seed to a stable value in -1..1.</summary>
    public static float Hash(int seed, int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)seed;
            h ^= (uint)x * 0x8DA6B343u; h = RotateLeft(h, 13);
            h ^= (uint)y * 0xD8163841u; h = RotateLeft(h, 11);
            h ^= (uint)z * 0xCB1AB31Fu; h ^= h >> 16;
            h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return (h / (float)uint.MaxValue) * 2f - 1f;
        }
    }

    private static uint RotateLeft(uint value, int count) => (value << count) | (value >> (32 - count));
    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
    private static float Lerp(float a, float b, float amount) => a + (b - a) * amount;
}
