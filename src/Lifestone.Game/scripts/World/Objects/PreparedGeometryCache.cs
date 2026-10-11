using Lifestone.Client.Objects;
using Lifestone.Game.Render;

namespace Lifestone.Game.World.Objects;

/// <summary>An object mesh's converted geometry (Godot arrays), shared by every landblock that draws it.</summary>
/// <param name="Mesh">The arrays, or null when retail draws nothing for this mesh (<paramref name="Problem"/> says why).</param>
/// <param name="Radius">Distance from the GfxObj's origin to the farthest corner of its bounds (unscaled).</param>
/// <param name="Problem">Why nothing is drawn, or null.</param>
public sealed record PreparedGeometry(PreparedMesh? Mesh, float Radius, string? Problem);

/// <summary>
/// Thread-safe, bounded, least-recently-used cache of <see cref="PreparedGeometry"/> by mesh key, so the landblocks
/// around the viewer (which mostly draw the same trees, rocks and buildings) convert each GfxObj once instead of once
/// per landblock: fewer allocations on the workers and far fewer long-lived arrays for the garbage collector to copy.
/// </summary>
/// <remarks>Bounded by entries and by vertices; evicted geometry stays valid for holders (it is immutable). Builds run
/// outside the lock; two threads may build the same key at once, and the first stored wins.</remarks>
public sealed class PreparedGeometryCache
{
    public const int DefaultMaxEntries = 2048;
    public const long DefaultMaxVertices = 1_500_000;

    private readonly object _gate = new();
    private readonly Dictionary<ObjectMeshKey, LinkedListNode<(ObjectMeshKey Key, PreparedGeometry Value)>> _map = new();
    private readonly LinkedList<(ObjectMeshKey Key, PreparedGeometry Value)> _lru = new();
    private long _vertices;

    public PreparedGeometryCache(int maxEntries = DefaultMaxEntries, long maxVertices = DefaultMaxVertices)
    {
        MaxEntries = maxEntries;
        MaxVertices = maxVertices;
    }

    public int MaxEntries { get; }

    public long MaxVertices { get; }

    public long Hits { get; private set; }

    public long Misses { get; private set; }

    public long Evictions { get; private set; }

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

    /// <summary>The cached geometry of <paramref name="key"/>, building it with <paramref name="build"/> on a miss.</summary>
    public PreparedGeometry GetOrAdd(ObjectMeshKey key, Func<ObjectMeshKey, PreparedGeometry> build)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                Hits++;
                return node.Value.Value;
            }

            Misses++;
        }

        var value = build(key);
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var raced))
            {
                return raced.Value.Value;
            }

            _map.Add(key, _lru.AddFirst((key, value)));
            _vertices += value.Mesh?.VertexCount ?? 0;
            while (_lru.Last is { } last && last.Value.Key != key && (_map.Count > MaxEntries || _vertices > MaxVertices))
            {
                _lru.RemoveLast();
                _map.Remove(last.Value.Key);
                _vertices -= last.Value.Value.Mesh?.VertexCount ?? 0;
                Evictions++;
            }

            return value;
        }
    }
}
