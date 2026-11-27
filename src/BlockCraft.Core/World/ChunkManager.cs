using System.Collections.Concurrent;
using BlockCraft.Core.Persistence;

namespace BlockCraft.Core.World;

/// <summary>Asynchronous chunk request, cache, world-coordinate access, and persistence coordinator.</summary>
public sealed class ChunkManager
{
    private readonly TerrainGenerator _generator;
    private readonly WorldStorage _storage;
    private readonly ConcurrentDictionary<ChunkPos, Chunk> _chunks = new();
    private readonly ConcurrentDictionary<ChunkPos, Task<Chunk>> _pending = new();

    /// <summary>Creates a chunk manager for one seeded world slot.</summary>
    public ChunkManager(TerrainGenerator generator, WorldStorage storage)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    /// <summary>Currently loaded chunks.</summary>
    public IReadOnlyCollection<Chunk> LoadedChunks => _chunks.Values.ToArray();

    /// <summary>Requests generation or loading without blocking the caller.</summary>
    public Task<Chunk> RequestChunkAsync(ChunkPos position)
    {
        if (_chunks.TryGetValue(position, out var loaded)) return Task.FromResult(loaded);
        var task = _pending.GetOrAdd(position, pos => Task.Run(() =>
        {
            var chunk = _storage.LoadChunk(pos) ?? _generator.Generate(pos);
            return _chunks.GetOrAdd(pos, chunk);
        }));
        return AwaitAndRemoveAsync(position, task);
    }

    private async Task<Chunk> AwaitAndRemoveAsync(ChunkPos position, Task<Chunk> task)
    {
        try { return await task.ConfigureAwait(false); }
        finally
        {
            ((ICollection<KeyValuePair<ChunkPos, Task<Chunk>>>)_pending)
                .Remove(new KeyValuePair<ChunkPos, Task<Chunk>>(position, task));
        }
    }

    /// <summary>Returns whether a chunk is currently available.</summary>
    public bool TryGetChunk(ChunkPos position, out Chunk? chunk) => _chunks.TryGetValue(position, out chunk);

    /// <summary>Reads a block from world coordinates; unloaded and out-of-range blocks are air.</summary>
    public ushort GetBlock(int worldX, int y, int worldZ)
    {
        if ((uint)y >= Chunk.Height) return 0;
        var cx = FloorDiv(worldX, Chunk.Width); var cz = FloorDiv(worldZ, Chunk.Depth);
        if (!_chunks.TryGetValue(new ChunkPos(cx, cz), out var chunk)) return 0;
        return chunk.GetBlock(Mod(worldX, Chunk.Width), y, Mod(worldZ, Chunk.Depth));
    }

    /// <summary>Changes a loaded block and returns false if its chunk is not loaded.</summary>
    public bool SetBlock(int worldX, int y, int worldZ, ushort blockId)
    {
        if ((uint)y >= Chunk.Height) return false;
        var cx = FloorDiv(worldX, Chunk.Width); var cz = FloorDiv(worldZ, Chunk.Depth);
        if (!_chunks.TryGetValue(new ChunkPos(cx, cz), out var chunk)) return false;
        return chunk.SetBlock(Mod(worldX, Chunk.Width), y, Mod(worldZ, Chunk.Depth), blockId);
    }

    /// <summary>Saves all loaded chunks and waits for in-flight generation requests.</summary>
    public async Task SaveAllAsync(CancellationToken cancellationToken = default)
    {
        var pending = _pending.Values.ToArray();
        if (pending.Length > 0) await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
        foreach (var chunk in _chunks.Values)
            await _storage.SaveChunkAsync(chunk, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Unloads a chunk after persisting its current contents.</summary>
    public async Task UnloadAsync(ChunkPos position, CancellationToken cancellationToken = default)
    {
        if (!_chunks.TryGetValue(position, out var chunk)) return;
        await _storage.SaveChunkAsync(chunk, cancellationToken).ConfigureAwait(false);
        ((ICollection<KeyValuePair<ChunkPos, Chunk>>)_chunks)
            .Remove(new KeyValuePair<ChunkPos, Chunk>(position, chunk));
    }

    /// <summary>Saves and unloads chunks outside a square keep radius.</summary>
    public async Task<IReadOnlyList<ChunkPos>> UnloadOutsideAsync(
        ChunkPos center, int keepRadius, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keepRadius);
        var candidates = _chunks
            .Where(pair => Math.Abs((long)pair.Key.X - center.X) > keepRadius ||
                           Math.Abs((long)pair.Key.Z - center.Z) > keepRadius)
            .ToArray();
        if (candidates.Length == 0) return Array.Empty<ChunkPos>();

        await _storage.SaveChunksAsync(candidates.Select(pair => pair.Value), cancellationToken)
            .ConfigureAwait(false);

        var unloaded = new List<ChunkPos>(candidates.Length);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (((ICollection<KeyValuePair<ChunkPos, Chunk>>)_chunks).Remove(candidate))
                unloaded.Add(candidate.Key);
        }
        return unloaded;
    }

    /// <summary>Converts a world coordinate to its chunk coordinate using mathematical floor division.</summary>
    public static int FloorDiv(int value, int divisor) => (int)MathF.Floor(value / (float)divisor);
    /// <summary>Converts a world coordinate to its non-negative local coordinate.</summary>
    public static int Mod(int value, int divisor) => ((value % divisor) + divisor) % divisor;
}
