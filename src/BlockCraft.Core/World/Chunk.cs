namespace BlockCraft.Core.World;

/// <summary>One 16×16×256 voxel region using compact unsigned-short block ids.</summary>
public sealed class Chunk
{
    /// <summary>Chunk width along X.</summary>
    public const int Width = 16;
    /// <summary>Chunk depth along Z.</summary>
    public const int Depth = 16;
    /// <summary>Chunk height along Y.</summary>
    public const int Height = 256;
    private readonly ushort[] _blocks = new ushort[Width * Depth * Height];

    /// <summary>Creates an empty chunk at the supplied chunk coordinates.</summary>
    public Chunk(int chunkX, int chunkZ) { ChunkX = chunkX; ChunkZ = chunkZ; }
    /// <summary>Chunk coordinate on the X axis.</summary>
    public int ChunkX { get; }
    /// <summary>Chunk coordinate on the Z axis.</summary>
    public int ChunkZ { get; }
    /// <summary>Increments whenever a block changes.</summary>
    public int Revision { get; private set; }
    /// <summary>Biome for each horizontal column.</summary>
    public BiomeKind[,] Biomes { get; } = new BiomeKind[Width, Depth];

    /// <summary>Reads a block. Coordinates outside this chunk or vertical range return air.</summary>
    public ushort GetBlock(int x, int y, int z)
    {
        if ((uint)x >= Width || (uint)y >= Height || (uint)z >= Depth) return 0;
        return _blocks[Index(x, y, z)];
    }

    /// <summary>Sets a block and reports whether the stored value changed.</summary>
    public bool SetBlock(int x, int y, int z, ushort blockId)
    {
        if ((uint)x >= Width || (uint)y >= Height || (uint)z >= Depth)
            throw new ArgumentOutOfRangeException(nameof(x), "Local block coordinates must be inside the chunk.");
        var index = Index(x, y, z);
        if (_blocks[index] == blockId) return false;
        _blocks[index] = blockId;
        Revision++;
        return true;
    }

    /// <summary>Returns a defensive copy of all block ids for persistence.</summary>
    public ushort[] CopyBlocks() => (ushort[])_blocks.Clone();

    /// <summary>Serializes block ids in a stable, versioned binary representation.</summary>
    public byte[] Serialize()
    {
        using var memory = new MemoryStream(16 + _blocks.Length * sizeof(ushort));
        using var writer = new BinaryWriter(memory);
        writer.Write(1);
        writer.Write(ChunkX);
        writer.Write(ChunkZ);
        writer.Write(_blocks.Length);
        foreach (var block in _blocks) writer.Write(block);
        return memory.ToArray();
    }

    /// <summary>Restores a chunk from data produced by <see cref="Serialize"/>.</summary>
    public static Chunk Deserialize(ReadOnlySpan<byte> data)
    {
        using var memory = new MemoryStream(data.ToArray(), writable: false);
        using var reader = new BinaryReader(memory);
        if (reader.ReadInt32() != 1) throw new InvalidDataException("Unsupported chunk data version.");
        var chunk = new Chunk(reader.ReadInt32(), reader.ReadInt32());
        var count = reader.ReadInt32();
        if (count != chunk._blocks.Length) throw new InvalidDataException($"Invalid chunk block count {count}.");
        for (var i = 0; i < count; i++) chunk._blocks[i] = reader.ReadUInt16();
        if (memory.Position != memory.Length) throw new InvalidDataException("Chunk data contains unexpected trailing bytes.");
        return chunk;
    }

    private static int Index(int x, int y, int z) => x + Width * (z + Depth * y);
}

/// <summary>Terrain regions used by generation and the debug overlay.</summary>
public enum BiomeKind : byte { Plains, Forest, Desert, Snow, Ocean }
/// <summary>Integer chunk coordinate pair.</summary>
public readonly record struct ChunkPos(int X, int Z);
/// <summary>Integer world block coordinate triplet.</summary>
public readonly record struct BlockPos(int X, int Y, int Z);
