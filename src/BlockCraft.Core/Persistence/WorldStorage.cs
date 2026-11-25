using System.Collections.Concurrent;
using System.IO.Compression;
using BlockCraft.Core.World;

namespace BlockCraft.Core.Persistence;

/// <summary>Region-file world storage. Each region contains up to 32×32 compressed chunk records.</summary>
public sealed class WorldStorage
{
    private static readonly byte[] Magic = "BCRGN001"u8.ToArray();
    private const int RegionSize = 32;
    private readonly string _worldDirectory;
    private readonly ConcurrentDictionary<(int X, int Z), SemaphoreSlim> _regionLocks = new();

    /// <summary>Creates storage rooted at a world slot directory.</summary>
    public WorldStorage(string worldDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldDirectory);
        _worldDirectory = Path.GetFullPath(worldDirectory);
        Directory.CreateDirectory(Path.Combine(_worldDirectory, "regions"));
    }

    /// <summary>World slot directory.</summary>
    public string WorldDirectory => _worldDirectory;

    /// <summary>Loads a chunk from its region file, or returns null when the record is absent.</summary>
    public Chunk? LoadChunk(ChunkPos position)
    {
        var region = RegionOf(position);
        var path = RegionPath(region.X, region.Z);
        if (!File.Exists(path)) return null;
        var gate = _regionLocks.GetOrAdd(region, static _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {
            var records = ReadRegion(path);
            if (!records.TryGetValue(LocalKey(position), out var compressed)) return null;
            using var input = new MemoryStream(compressed, writable: false);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            brotli.CopyTo(output);
            var chunk = Chunk.Deserialize(output.ToArray());
            if (chunk.ChunkX != position.X || chunk.ChunkZ != position.Z)
                throw new InvalidDataException("Chunk region record coordinates do not match its index.");
            return chunk;
        }
        finally { gate.Release(); }
    }

    /// <summary>Atomically replaces one chunk record in its region file.</summary>
    public async Task SaveChunkAsync(Chunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var region = RegionOf(new ChunkPos(chunk.ChunkX, chunk.ChunkZ));
        var gate = _regionLocks.GetOrAdd(region, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = RegionPath(region.X, region.Z);
            var records = File.Exists(path) ? ReadRegion(path) : new Dictionary<int, byte[]>();
            using var input = new MemoryStream(chunk.Serialize());
            using var compressed = new MemoryStream();
            using (var brotli = new BrotliStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                await input.CopyToAsync(brotli, cancellationToken).ConfigureAwait(false);
            records[LocalKey(new ChunkPos(chunk.ChunkX, chunk.ChunkZ))] = compressed.ToArray();

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic); writer.Write(records.Count);
                foreach (var record in records.OrderBy(pair => pair.Key))
                {
                    writer.Write(record.Key); writer.Write(record.Value.Length); writer.Write(record.Value);
                }
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { gate.Release(); }
    }

    private static Dictionary<int, byte[]> ReadRegion(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        var magic = reader.ReadBytes(Magic.Length);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw new InvalidDataException($"Invalid region file header: {path}");
        var count = reader.ReadInt32();
        if (count is < 0 or > RegionSize * RegionSize) throw new InvalidDataException("Invalid region record count.");
        var records = new Dictionary<int, byte[]>(count);
        for (var i = 0; i < count; i++)
        {
            var key = reader.ReadInt32(); var length = reader.ReadInt32();
            if (key is < 0 or >= RegionSize * RegionSize || length is < 1 or > 4_000_000)
                throw new InvalidDataException("Invalid region record.");
            var data = reader.ReadBytes(length);
            if (data.Length != length || !records.TryAdd(key, data))
                throw new InvalidDataException("Truncated or duplicate chunk record in region.");
        }
        if (stream.Position != stream.Length) throw new InvalidDataException("Region file contains trailing data.");
        return records;
    }

    private string RegionPath(int x, int z) => Path.Combine(_worldDirectory, "regions", $"r.{x}.{z}.bcr");
    private static (int X, int Z) RegionOf(ChunkPos position) =>
        (ChunkManager.FloorDiv(position.X, RegionSize), ChunkManager.FloorDiv(position.Z, RegionSize));
    private static int LocalKey(ChunkPos position) =>
        ChunkManager.Mod(position.X, RegionSize) + RegionSize * ChunkManager.Mod(position.Z, RegionSize);
}
