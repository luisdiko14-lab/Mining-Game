using BlockCraft.Core.Blocks;

namespace BlockCraft.Core.World;

/// <summary>Chunk-local sunlight and emitted-light volumes with flood propagation and corner occlusion.</summary>
public sealed class ChunkLighting
{
    private readonly byte[] _sunlight;
    private readonly byte[] _blockLight;
    private readonly Func<int, int, int, ushort> _readBlock;
    private readonly BlockRegistry _registry;

    private ChunkLighting(byte[] sunlight, byte[] blockLight, Func<int, int, int, ushort> readBlock, BlockRegistry registry)
    {
        _sunlight = sunlight;
        _blockLight = blockLight;
        _readBlock = readBlock;
        _registry = registry;
    }

    /// <summary>
    /// Builds light volumes for one chunk. Optional accessors expose neighboring chunks' blocks and
    /// existing emitted light so light can cross chunk boundaries without assuming unloaded space is solid.
    /// </summary>
    public static ChunkLighting Calculate(
        Chunk chunk,
        BlockRegistry registry,
        Func<int, int, int, ushort>? neighborBlock = null,
        Func<int, int, int, byte>? neighborLight = null)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(registry);

        ushort ReadBlock(int x, int y, int z)
        {
            if ((uint)x < Chunk.Width && (uint)y < Chunk.Height && (uint)z < Chunk.Depth)
                return chunk.GetBlock(x, y, z);
            return neighborBlock?.Invoke(x, y, z) ?? 0;
        }

        byte ReadNeighborLight(int x, int y, int z) =>
            (uint)x < Chunk.Width && (uint)y < Chunk.Height && (uint)z < Chunk.Depth
                ? (byte)0
                : neighborLight?.Invoke(x, y, z) ?? 0;

        var length = Chunk.Width * Chunk.Height * Chunk.Depth;
        var sunlight = new byte[length];
        var blockLight = new byte[length];
        var sunQueue = new Queue<int>();
        var blockQueue = new Queue<int>();

        for (var z = 0; z < Chunk.Depth; z++)
        for (var x = 0; x < Chunk.Width; x++)
        {
            byte level = 15;
            for (var y = Chunk.Height - 1; y >= 0; y--)
            {
                var definition = registry[ReadBlock(x, y, z)];
                if (definition.OccludesFaces)
                    level = 0;
                else if (definition.Transparent && definition.Id != 0 && level > 0)
                    level = (byte)Math.Max(0, level - 1);

                var index = Index(x, y, z);
                sunlight[index] = level;
                if (level > 1)
                    sunQueue.Enqueue(index);

                if (definition.LightEmission > 0)
                {
                    blockLight[index] = definition.LightEmission;
                    blockQueue.Enqueue(index);
                }
            }
        }

        SeedNeighborLight(blockLight, blockQueue, ReadBlock, ReadNeighborLight, registry);
        Propagate(sunlight, sunQueue, ReadBlock, registry);
        Propagate(blockLight, blockQueue, ReadBlock, registry);
        return new ChunkLighting(sunlight, blockLight, ReadBlock, registry);
    }

    /// <summary>Reads natural sunlight at a local block coordinate.</summary>
    public byte GetSunlight(int x, int y, int z) => Read(_sunlight, x, y, z);
    /// <summary>Reads emitted block light at a local block coordinate.</summary>
    public byte GetBlockLight(int x, int y, int z) => Read(_blockLight, x, y, z);
    /// <summary>Returns the stronger of sunlight and block light.</summary>
    public byte GetCombinedLight(int x, int y, int z) => Math.Max(GetSunlight(x, y, z), GetBlockLight(x, y, z));

    /// <summary>
    /// Samples the three neighboring voxels at a face corner. Returns zero for fully occluded
    /// corners and three for corners with no occluders. Axes are X=0, Y=1, Z=2.
    /// </summary>
    public byte GetAmbientOcclusion(
        int blockX, int blockY, int blockZ,
        int normalAxis, int normalSign,
        int uAxis, int vAxis, int uCorner, int vCorner)
    {
        if (normalAxis is < 0 or > 2 || uAxis is < 0 or > 2 || vAxis is < 0 or > 2 ||
            normalAxis == uAxis || normalAxis == vAxis || uAxis == vAxis ||
            normalSign is not (-1 or 1) || uCorner is not (-1 or 1) || vCorner is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(normalAxis), "Face axes and corner directions must be valid and distinct.");

        var adjacent = new[] { blockX, blockY, blockZ };
        adjacent[normalAxis] += normalSign;
        var sideU = (int[])adjacent.Clone();
        var sideV = (int[])adjacent.Clone();
        var corner = (int[])adjacent.Clone();
        sideU[uAxis] += uCorner;
        sideV[vAxis] += vCorner;
        corner[uAxis] += uCorner;
        corner[vAxis] += vCorner;

        var sideUBlocked = IsOccluding(sideU[0], sideU[1], sideU[2]);
        var sideVBlocked = IsOccluding(sideV[0], sideV[1], sideV[2]);
        if (sideUBlocked && sideVBlocked)
            return 0;

        var diagonalBlocked = IsOccluding(corner[0], corner[1], corner[2]);
        return (byte)(3 - Convert.ToInt32(sideUBlocked) - Convert.ToInt32(sideVBlocked) - Convert.ToInt32(diagonalBlocked));
    }

    private bool IsOccluding(int x, int y, int z)
    {
        var id = _readBlock(x, y, z);
        return id != 0 && _registry[id].OccludesFaces;
    }

    private static void SeedNeighborLight(
        byte[] light,
        Queue<int> queue,
        Func<int, int, int, ushort> readBlock,
        Func<int, int, int, byte> readNeighborLight,
        BlockRegistry registry)
    {
        for (var y = 0; y < Chunk.Height; y++)
        for (var z = 0; z < Chunk.Depth; z++)
        for (var x = 0; x < Chunk.Width; x++)
        {
            if (x is not (0 or Chunk.Width - 1) && z is not (0 or Chunk.Depth - 1))
                continue;
            if (registry[readBlock(x, y, z)].OccludesFaces)
                continue;

            foreach (var (nx, ny, nz) in Neighbors(x, y, z))
            {
                if ((uint)nx < Chunk.Width && (uint)nz < Chunk.Depth)
                    continue;
                if ((uint)ny >= Chunk.Height)
                    continue;
                var sourceLevel = Math.Max(registry[readBlock(nx, ny, nz)].LightEmission, readNeighborLight(nx, ny, nz));
                var candidate = (byte)Math.Max(0, sourceLevel - 1);
                var target = Index(x, y, z);
                if (candidate <= light[target])
                    continue;
                light[target] = candidate;
                queue.Enqueue(target);
            }
        }
    }

    private static void Propagate(byte[] light, Queue<int> queue, Func<int, int, int, ushort> readBlock, BlockRegistry registry)
    {
        while (queue.TryDequeue(out var index))
        {
            var (x, y, z) = Coordinates(index);
            var nextLevel = (byte)(light[index] - 1);
            if (nextLevel == 0)
                continue;

            foreach (var (nx, ny, nz) in Neighbors(x, y, z))
            {
                if ((uint)nx >= Chunk.Width || (uint)ny >= Chunk.Height || (uint)nz >= Chunk.Depth)
                    continue;
                var target = Index(nx, ny, nz);
                if (nextLevel <= light[target] || registry[readBlock(nx, ny, nz)].OccludesFaces)
                    continue;
                light[target] = nextLevel;
                queue.Enqueue(target);
            }
        }
    }

    private static IEnumerable<(int X, int Y, int Z)> Neighbors(int x, int y, int z)
    {
        yield return (x - 1, y, z);
        yield return (x + 1, y, z);
        yield return (x, y - 1, z);
        yield return (x, y + 1, z);
        yield return (x, y, z - 1);
        yield return (x, y, z + 1);
    }

    private static byte Read(byte[] source, int x, int y, int z) =>
        (uint)x < Chunk.Width && (uint)y < Chunk.Height && (uint)z < Chunk.Depth
            ? source[Index(x, y, z)]
            : (byte)0;

    private static int Index(int x, int y, int z) => x + Chunk.Width * (z + Chunk.Depth * y);

    private static (int X, int Y, int Z) Coordinates(int index)
    {
        var y = index / (Chunk.Width * Chunk.Depth);
        var remainder = index - y * Chunk.Width * Chunk.Depth;
        var z = remainder / Chunk.Width;
        return (remainder - z * Chunk.Width, y, z);
    }
}
