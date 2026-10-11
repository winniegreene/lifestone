using Lifestone.Data.Dat;

namespace Lifestone.Data.Models;

/// <summary>
/// Thread-safe, bounded, least-recently-used cache of <see cref="GfxObjMesh"/> keyed by GfxObj id, shared by the
/// scenery, static-object and EnvCell-static loaders.
/// </summary>
/// <remarks>
/// <para>
/// Each id is built at most once while it stays cached: concurrent requests for the same id wait for one build
/// (<see cref="Lazy{T}"/> with <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>); requests for different ids
/// build in parallel. Dat reads still serialise on <see cref="DatSet.SyncRoot"/>; mesh building runs outside it.
/// </para>
/// <para>
/// The cache is bounded by entry count and by the total number of mesh vertices it holds; least-recently-used
/// entries are evicted first. A missing GfxObj is cached as null. A build that throws is not cached.
/// Meshes are immutable and safe to share; an evicted mesh stays valid for holders of a reference.
/// </para>
/// </remarks>
public sealed class GfxObjMeshCache
{
    /// <summary>Default entry bound (end-of-retail portal.dat has 15,318 GfxObjs).</summary>
    public const int DefaultMaxEntries = 4096;

    /// <summary>Default vertex bound (about 4 M vertices ≈ 128 MB of position/normal/UV arrays).</summary>
    public const long DefaultMaxVertices = 4_000_000;

    private readonly Func<uint, GfxObjMesh?> _build;
    private readonly object _gate = new();
    private readonly Dictionary<uint, LinkedListNode<Entry>> _map = [];
    private readonly LinkedList<Entry> _lru = new();
    private long _cachedVertices;
    private long _hits;
    private long _misses;
    private long _evictions;

    /// <summary>Creates a cache that builds meshes from <paramref name="dats"/>.</summary>
    public GfxObjMeshCache(DatSet dats, int maxEntries = DefaultMaxEntries, long maxVertices = DefaultMaxVertices)
        : this(id => GfxObjMeshBuilder.Load(dats, id), maxEntries, maxVertices)
    {
        ArgumentNullException.ThrowIfNull(dats);
    }

    /// <summary>Creates a cache around an arbitrary builder (tests, or a future derived-data cache).</summary>
    public GfxObjMeshCache(Func<uint, GfxObjMesh?> build, int maxEntries = DefaultMaxEntries, long maxVertices = DefaultMaxVertices)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxVertices, 1);
        _build = build;
        MaxEntries = maxEntries;
        MaxVertices = maxVertices;
    }

    public int MaxEntries { get; }

    public long MaxVertices { get; }

    /// <summary>Entries currently cached (including in-flight builds and cached misses).</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    /// <summary>Total vertices of the meshes currently cached.</summary>
    public long CachedVertexCount
    {
        get
        {
            lock (_gate)
            {
                return _cachedVertices;
            }
        }
    }

    public long Hits => Interlocked.Read(ref _hits);

    public long Misses => Interlocked.Read(ref _misses);

    public long Evictions => Interlocked.Read(ref _evictions);

    /// <summary>Returns the mesh of GfxObj <paramref name="gfxObjId"/>, building it on first use; null if it does not exist.</summary>
    public GfxObjMesh? Get(uint gfxObjId)
    {
        Entry entry;
        lock (_gate)
        {
            if (_map.TryGetValue(gfxObjId, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                entry = node.Value;
                Interlocked.Increment(ref _hits);
            }
            else
            {
                entry = new Entry(gfxObjId, _build);
                _map.Add(gfxObjId, _lru.AddFirst(entry));
                Interlocked.Increment(ref _misses);
            }
        }

        GfxObjMesh? mesh;
        try
        {
            mesh = entry.Value.Value;
        }
        catch
        {
            lock (_gate)
            {
                if (_map.TryGetValue(gfxObjId, out var node) && ReferenceEquals(node.Value, entry))
                {
                    _map.Remove(gfxObjId);
                    _lru.Remove(node);
                }
            }

            throw;
        }

        lock (_gate)
        {
            if (!entry.Accounted && _map.TryGetValue(gfxObjId, out var node) && ReferenceEquals(node.Value, entry))
            {
                entry.Accounted = true;
                entry.Vertices = mesh?.Mesh.SubMeshes.Sum(s => (long)s.Positions.Length) ?? 0;
                _cachedVertices += entry.Vertices;
                Trim(entry);
            }
        }

        return mesh;
    }

    /// <summary>Drops every cached mesh.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _lru.Clear();
            _cachedVertices = 0;
        }
    }

    private void Trim(Entry keep)
    {
        var node = _lru.Last;
        while (node is not null && (_map.Count > MaxEntries || _cachedVertices > MaxVertices))
        {
            var previous = node.Previous;
            if (!ReferenceEquals(node.Value, keep))
            {
                _lru.Remove(node);
                _map.Remove(node.Value.Id);
                if (node.Value.Accounted)
                {
                    _cachedVertices -= node.Value.Vertices;
                }

                Interlocked.Increment(ref _evictions);
            }

            node = previous;
        }
    }

    private sealed class Entry(uint id, Func<uint, GfxObjMesh?> build)
    {
        public uint Id { get; } = id;

        public Lazy<GfxObjMesh?> Value { get; } = new(() => build(id), LazyThreadSafetyMode.ExecutionAndPublication);

        public bool Accounted { get; set; }

        public long Vertices { get; set; }
    }
}
