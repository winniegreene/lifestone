using System.Numerics;
using System.Runtime.InteropServices;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// The big picture a long walk needs and the attached tiles cannot give: how all the land joins up, landblock by landblock. A <b>component</b> is ground of one landblock a body can walk round both ways inside that landblock; a link
/// is a step from one component into another, one way where a drop is. The world walk's <c>graph</c> verb builds the file from the planner's own coarse graph (docs/world-walk.md), so it is derived from the dats and lives in <c>cache/</c>.
/// Immutable once loaded and safe to share between threads; a search over it is a <see cref="WorldRouteSearch"/>. Not retail: Lifestone's own.
/// </summary>
public sealed class WorldRoutes
{
    /// <summary>The file's first word; a different one is a different format and refuses to load by name.</summary>
    public const int Magic = 0x3152574C;

    private readonly int[] _blockIndex;
    private readonly int[] _regionBase;
    private readonly int[] _componentBase;
    private readonly ushort[] _regionComponent;
    private readonly byte[] _regionCell;
    private readonly ushort[] _componentBlock;
    private readonly int[] _componentNodes;
    private readonly float[] _componentX;
    private readonly float[] _componentY;
    private readonly int[] _linkOffset;
    private readonly int[] _links;

    private WorldRoutes(ushort[] blockIds, int[] regionBase, int[] componentBase, ushort[] regionComponent, byte[] regionCell, ushort[] componentBlock, int[] componentNodes, float[] componentX, float[] componentY, int[] linkOffset, int[] links)
    {
        _blockIndex = new int[ushort.MaxValue + 1];
        Array.Fill(_blockIndex, -1);
        for (var i = 0; i < blockIds.Length; i++)
        {
            _blockIndex[blockIds[i]] = i;
        }

        (_regionBase, _componentBase, _regionComponent, _regionCell, _componentBlock, _componentNodes, _componentX, _componentY, _linkOffset, _links) =
            (regionBase, componentBase, regionComponent, regionCell, componentBlock, componentNodes, componentX, componentY, linkOffset, links);
    }

    /// <summary>How many components the world has.</summary>
    public int Components => _componentBlock.Length;

    /// <summary>Reads the file; the host opens it (Lifestone.Client does no file I/O).</summary>
    /// <param name="file">The bytes the world walk's <c>graph</c> verb wrote, read from the start.</param>
    /// <exception cref="InvalidDataException">The bytes are not this format.</exception>
    public static WorldRoutes Load(Stream file)
    {
        using var reader = new BinaryReader(file, System.Text.Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != Magic)
        {
            throw new InvalidDataException("not a world routes file of this format");
        }

        var blocks = reader.ReadInt32();
        var regions = reader.ReadInt32();
        var components = reader.ReadInt32();
        var links = reader.ReadInt32();
        return new WorldRoutes(
            Read<ushort>(file, blocks),
            Read<int>(file, blocks + 1),
            Read<int>(file, blocks + 1),
            Read<ushort>(file, regions),
            Read<byte>(file, regions),
            Read<ushort>(file, components),
            Read<int>(file, components),
            Read<float>(file, components),
            Read<float>(file, components),
            Read<int>(file, components + 1),
            Read<int>(file, links));
    }

    /// <summary>Writes the file. Each array is as the fields above name it.</summary>
    public static void Write(Stream file, ReadOnlySpan<ushort> blockIds, ReadOnlySpan<int> regionBase, ReadOnlySpan<int> componentBase, ReadOnlySpan<ushort> regionComponent, ReadOnlySpan<byte> regionCell, ReadOnlySpan<ushort> componentBlock, ReadOnlySpan<int> componentNodes, ReadOnlySpan<float> componentX, ReadOnlySpan<float> componentY, ReadOnlySpan<int> linkOffset, ReadOnlySpan<int> links)
    {
        using var writer = new BinaryWriter(file, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(blockIds.Length);
        writer.Write(regionComponent.Length);
        writer.Write(componentBlock.Length);
        writer.Write(links.Length);
        writer.Flush();
        file.Write(MemoryMarshal.AsBytes(blockIds));
        file.Write(MemoryMarshal.AsBytes(regionBase));
        file.Write(MemoryMarshal.AsBytes(componentBase));
        file.Write(MemoryMarshal.AsBytes(regionComponent));
        file.Write(regionCell);
        file.Write(MemoryMarshal.AsBytes(componentBlock));
        file.Write(MemoryMarshal.AsBytes(componentNodes));
        file.Write(MemoryMarshal.AsBytes(componentX));
        file.Write(MemoryMarshal.AsBytes(componentY));
        file.Write(MemoryMarshal.AsBytes(linkOffset));
        file.Write(MemoryMarshal.AsBytes(links));
    }

    private static T[] Read<T>(Stream file, int count)
        where T : unmanaged
    {
        var values = new T[count];
        file.ReadExactly(MemoryMarshal.AsBytes(values.AsSpan()));
        return values;
    }

    /// <summary>The number of coarse regions the file holds for a landblock (0 for none).</summary>
    public int RegionCount(uint landblockId) => _blockIndex[landblockId >> 16] is var b and >= 0 ? _regionBase[b + 1] - _regionBase[b] : 0;

    /// <summary>The component of a landblock's region, numbered as <see cref="NavMap.RegionsOf"/> numbers them; -1 for none.</summary>
    public int ComponentOfRegion(uint landblockId, int region)
    {
        var b = _blockIndex[landblockId >> 16];
        return b < 0 || region < 0 || region >= _regionBase[b + 1] - _regionBase[b] ? -1 : _componentBase[b] + _regionComponent[_regionBase[b] + region];
    }

    /// <summary>The largest component with ground in a land cell of a landblock; -1 for none.</summary>
    /// <param name="landblockId">The landblock.</param>
    /// <param name="cell">The land cell, as <see cref="NavRegionRead.Cell"/> numbers them.</param>
    public int ComponentAtCell(uint landblockId, int cell)
    {
        var b = _blockIndex[landblockId >> 16];
        if (b < 0)
        {
            return -1;
        }

        var best = -1;
        for (var r = _regionBase[b]; r < _regionBase[b + 1]; r++)
        {
            if (_regionCell[r] == cell)
            {
                var component = _componentBase[b] + _regionComponent[r];
                if (best < 0 || _componentNodes[component] > _componentNodes[best])
                {
                    best = component;
                }
            }
        }

        return best;
    }

    /// <summary>The landblock of a component, 0xXXYY0000.</summary>
    public uint LandblockOf(int component) => (uint)_componentBlock[component] << 16;

    /// <summary>The middle of a component's ground, global metres.</summary>
    public Vector2 CentreOf(int component) => new(_componentX[component], _componentY[component]);

    internal ReadOnlySpan<int> LinksOf(int component) => _links.AsSpan(_linkOffset[component], _linkOffset[component + 1] - _linkOffset[component]);
}

/// <summary>One thread's search over <see cref="WorldRoutes"/>: a shortest route from component to component. Its working arrays are kept between searches. Not thread-safe.</summary>
public sealed class WorldRouteSearch(WorldRoutes routes)
{
    private readonly PriorityQueue<int, float> _open = new();
    private int[]? _seen;
    private int[]? _parent;
    private float[]? _cost;
    private int _generation;

    /// <summary>The world routes searched.</summary>
    public WorldRoutes Routes { get; } = routes ?? throw new ArgumentNullException(nameof(routes));

    /// <summary>The components from <paramref name="from"/> to <paramref name="to"/>, both included, or null when the land does not join them on foot.</summary>
    public IReadOnlyList<int>? Route(int from, int to)
    {
        var size = Routes.Components;
        var seen = _seen ??= new int[size];
        var parent = _parent ??= new int[size];
        var cost = _cost ??= new float[size];
        var generation = ++_generation;
        var goal = Routes.CentreOf(to);
        _open.Clear();
        seen[from] = generation;
        parent[from] = -1;
        cost[from] = 0f;
        _open.Enqueue(from, Vector2.Distance(Routes.CentreOf(from), goal));
        while (_open.TryDequeue(out var at, out var priority))
        {
            if (at == to)
            {
                var route = new List<int>();
                for (var c = to; c >= 0; c = parent[c])
                {
                    route.Add(c);
                }

                route.Reverse();
                return route;
            }

            var here = Routes.CentreOf(at);
            if (priority > cost[at] + Vector2.Distance(here, goal) + 0.001f)
            {
                continue;
            }

            foreach (var next in Routes.LinksOf(at))
            {
                var there = Routes.CentreOf(next);
                var g = cost[at] + Vector2.Distance(here, there);
                if (seen[next] != generation || g < cost[next])
                {
                    seen[next] = generation;
                    parent[next] = at;
                    cost[next] = g;
                    _open.Enqueue(next, g + Vector2.Distance(there, goal));
                }
            }
        }

        return null;
    }
}
