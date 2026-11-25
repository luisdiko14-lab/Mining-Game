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
        finally { _pending.TryRemove(position, out _); }
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
        if (_chunks.TryGetValue(position, out var chunk))
        {
            await _storage.SaveChunkAsync(chunk, cancellationToken).ConfigureAwait(false);
            _chunks.TryRemove(position, out _);
        }
    }

    /// <summary>Converts a world coordinate to its chunk coordinate using mathematical floor division.</summary>
    public static int FloorDiv(int value, int divisor) => (int)MathF.Floor(value / (float)divisor);
    /// <summary>Converts a world coordinate to its non-negative local coordinate.</summary>
    public static int Mod(int value, int divisor) => ((value % divisor) + divisor) % divisor;
}
