using System.Collections.Concurrent;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;

namespace Lifestone.Data.World.Cells;

/// <summary>
/// Thread-safe, bounded (least-recently-used) cache of EnvCell meshes keyed by (Environment, struct index, surface list).
/// </summary>
/// <remarks>
/// <para>
/// Many cells share a mesh: the 734,976 end-of-retail EnvCells use 3,145 distinct (Environment, struct) pairs, and the
/// surface list is the only other input. The key carries a hash of the surface list for fast lookup and the full list
/// for equality, so two lists with the same hash never share a mesh.
/// </para>
/// <para>
/// A mesh is built at most once per key while it is cached: concurrent requests for the same key wait on one
/// <see cref="Lazy{T}"/>. Building happens outside the cache lock, so different keys build in parallel. When the cache
/// holds more than <see cref="Capacity"/> meshes the least recently used are dropped (callers that still hold a mesh
/// keep it; the cache only forgets it).
/// </para>
/// </remarks>
public sealed class CellMeshCache
{
    private readonly object _gate = new();
    private readonly Dictionary<CellMeshKey, LinkedListNode<Entry>> _map = new();
    private readonly LinkedList<Entry> _lru = new();
    private readonly CellStructureLibrary _structures;
    private readonly SurfaceTypeLookup _surfaceTypes;
    private long _hits;
    private long _misses;

    /// <param name="structures">Decoded Environments (shared with other users of the same dats).</param>
    /// <param name="capacity">Maximum number of meshes kept; at least 1.</param>
    public CellMeshCache(CellStructureLibrary structures, int capacity = 4096)
    {
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _structures = structures;
        _surfaceTypes = new SurfaceTypeLookup(structures.Dats);
        Capacity = capacity;
    }

    public int Capacity { get; }

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

    public long Hits => Interlocked.Read(ref _hits);

    public long Misses => Interlocked.Read(ref _misses);

    /// <summary>The mesh of an EnvCell (cell-local; place it with <see cref="EnvCellData.Frame"/>).</summary>
    /// <exception cref="InvalidDataException">The cell's Environment, struct or a Surface is missing or malformed.</exception>
    public CellMesh Get(EnvCellData cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return Get(cell.EnvironmentId, cell.CellStructureIndex, cell.SurfaceIds);
    }

    /// <summary>The mesh of a struct drawn with a surface list.</summary>
    public CellMesh Get(uint environmentId, ushort structIndex, IReadOnlyList<uint> surfaceIds)
    {
        ArgumentNullException.ThrowIfNull(surfaceIds);
        var key = new CellMeshKey(environmentId, structIndex, surfaceIds.ToArray());
        Lazy<CellMesh> lazy;
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                _hits++;
                lazy = node.Value.Mesh;
            }
            else
            {
                _misses++;
                lazy = new Lazy<CellMesh>(() => Build(key), LazyThreadSafetyMode.ExecutionAndPublication);
                var added = _lru.AddFirst(new Entry(key, lazy));
                _map.Add(key, added);
                while (_map.Count > Capacity)
                {
                    var last = _lru.Last!;
                    _lru.RemoveLast();
                    _map.Remove(last.Value.Key);
                }
            }
        }

        try
        {
            return lazy.Value;
        }
        catch
        {
            // Do not keep a failed build cached; the next request retries (and fails loudly again).
            lock (_gate)
            {
                if (_map.TryGetValue(key, out var node) && ReferenceEquals(node.Value.Mesh, lazy))
                {
                    _lru.Remove(node);
                    _map.Remove(key);
                }
            }

            throw;
        }
    }

    private CellMesh Build(CellMeshKey key)
    {
        var structure = _structures.Get(key.EnvironmentId, key.StructIndex);
        return CellMeshBuilder.Build(structure, key.SurfaceIds, _surfaceTypes.TypeOf);
    }

    private sealed record Entry(CellMeshKey Key, Lazy<CellMesh> Mesh);
}

/// <summary>Cache key: Environment, struct index and the full surface list (hashed once).</summary>
public readonly struct CellMeshKey : IEquatable<CellMeshKey>
{
    private readonly uint[] _surfaceIds;

    public CellMeshKey(uint environmentId, ushort structIndex, uint[] surfaceIds)
    {
        ArgumentNullException.ThrowIfNull(surfaceIds);
        EnvironmentId = environmentId;
        StructIndex = structIndex;
        _surfaceIds = surfaceIds;
        var hash = new HashCode();
        hash.Add(environmentId);
        hash.Add(structIndex);
        foreach (var id in surfaceIds)
        {
            hash.Add(id);
        }

        SurfaceListHash = hash.ToHashCode();
    }

    public uint EnvironmentId { get; }

    public ushort StructIndex { get; }

    public IReadOnlyList<uint> SurfaceIds => _surfaceIds;

    /// <summary>Hash of (environment, struct, surface list).</summary>
    public int SurfaceListHash { get; }

    public bool Equals(CellMeshKey other) =>
        EnvironmentId == other.EnvironmentId
        && StructIndex == other.StructIndex
        && SurfaceListHash == other.SurfaceListHash
        && _surfaceIds.AsSpan().SequenceEqual(other._surfaceIds);

    public override bool Equals(object? obj) => obj is CellMeshKey other && Equals(other);

    public override int GetHashCode() => SurfaceListHash;

    public static bool operator ==(CellMeshKey left, CellMeshKey right) => left.Equals(right);

    public static bool operator !=(CellMeshKey left, CellMeshKey right) => !left.Equals(right);
}

/// <summary>Thread-safe Surface (0x08) type-bit lookup, read byte-exact from client_portal.dat and kept.</summary>
internal sealed class SurfaceTypeLookup(DatSet dats)
{
    private readonly ConcurrentDictionary<uint, uint> _types = new();

    /// <exception cref="InvalidDataException">The Surface does not exist or is malformed.</exception>
    public uint TypeOf(uint surfaceId) => _types.GetOrAdd(surfaceId, Load);

    private uint Load(uint surfaceId)
    {
        var bytes = CellRecordReader.ReadPortalBytes(dats, surfaceId)
            ?? throw new InvalidDataException($"Surface 0x{surfaceId:X8} not found in {DatSet.PortalFileName}.");
        return (uint)CellRecordReader.UnpackExact<Surface>(bytes, surfaceId).Type;
    }
}
