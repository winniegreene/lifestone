using System.Numerics;
using System.Runtime.CompilerServices;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// The attached <see cref="NavTile"/>s as one walk map (P5a, design 3.1, docs/wayfinding.md): tiles keyed by landblock,
/// a global node lattice across landblocks, the 8-neighbour step rules, the lattice line of sight, and the coarse graph
/// the <see cref="PathPlanner"/> plans its corridor on.
/// </summary>
/// <remarks>
/// <para><b>Addressing.</b> A <see cref="NavPoint"/> is a landblock plus local metres; the map works in global metres
/// (<see cref="NavPoint.Global"/>: <see cref="LandDefs.GetBlockOffset"/> from landblock 0x0000, plus the local x and y).
/// Global node (gx, gy) is the 1 m node whose south-west corner is global metre (gx, gy): landblock x = gx / 192, local
/// node x = gx % 192, and the same in y. So the node east of landblock 0x7FYY's node 191 is landblock 0x80YY's node 0,
/// with no carry between the id's bytes.</para>
/// <para><b>Steps.</b> A step goes to one of the 8 neighbours. It needs the target not blocked; a diagonal also needs
/// both orthogonal nodes it passes not blocked (no corner cutting); a rise over <see cref="StepUpMetres"/> or a drop over
/// <see cref="MaxDropMetres"/> blocks the step (a drop under it is allowed and costed by the planner).</para>
/// <para><b>The coarse graph.</b> Each tile's 24 m land cells (8 x 8) are flood-filled into regions over the steps that
/// pass both ways; a land cell may hold several regions (a wall or a cliff splits it). Regions link where a step leads
/// from one to another (directed: a drop links one way), inside the tile and across its edges to the attached
/// neighbours. Rebuilt per tile on <see cref="Attach"/>; <see cref="Detach"/> drops the tile and the links into it.</para>
/// <para>Not thread-safe: attach, detach and plan on one thread (the game thread; the tile is built on a worker).
/// Not retail: Lifestone's own wayfinder; every constant is Lifestone's.</para>
/// </remarks>
public sealed class NavMap
{
    /// <summary>The highest rise a step may climb, in metres (the body's step-up). Not retail: Lifestone's own. The one definition: <see cref="NavBuildOptions.StepUp"/> takes its default from here.</summary>
    public const float StepUpMetres = 0.6f;

    /// <summary>The deepest drop a step may take, in metres; deeper is blocked. Not retail: Lifestone's own conservatism.</summary>
    public const float MaxDropMetres = 2f;

    /// <summary>The lattice line of sight's sample spacing in metres. Not retail: Lifestone's own.</summary>
    public const float SightSampleMetres = 0.5f;

    /// <summary>A land cell's side in nodes (24 m, the retail land cell).</summary>
    public const int LandCellNodes = 24;

    /// <summary>Land cells per tile side.</summary>
    public const int LandCellsPerSide = NavTile.Size / LandCellNodes;

    internal const int NodesPerTile = NavTile.Size * NavTile.Size;
    internal const int CellsPerTile = LandCellsPerSide * LandCellsPerSide;
    internal const ushort NoRegion = ushort.MaxValue;

    // The 8 directions: E, N, W, S, NE, NW, SW, SE.
    internal static readonly int[] Dx = [1, 0, -1, 0, 1, -1, -1, 1];
    internal static readonly int[] Dy = [0, 1, 0, -1, 1, 1, -1, -1];
    private static readonly int[] DiagH = [-1, -1, -1, -1, 0, 2, 2, 0];
    private static readonly int[] DiagV = [-1, -1, -1, -1, 1, 1, 3, 3];

    private readonly short[] _slotOfBlock = new short[256 * 256];
    private readonly Stack<int> _freeSlots = new();
    private readonly List<uint> _attached = [];
    private int[] _floodStack = new int[NodesPerTile];
    private NavTile?[] _tiles = new NavTile?[4];
    private TileGraph?[] _graphs = new TileGraph?[4];
    private int[] _originX = new int[4];
    private int[] _originY = new int[4];
    private int[] _nbr = new int[4 * 9];
    private int[] _regionBase = new int[4];
    private int[] _regionSlot = [];
    private int _slotCount;

    /// <summary>An empty map.</summary>
    public NavMap()
    {
        Array.Fill(_slotOfBlock, (short)-1);
        Array.Fill(_nbr, -1);
    }

    /// <summary>The attached landblocks (0xXXYY0000).</summary>
    public IReadOnlyCollection<uint> AttachedLandblocks => _attached;

    /// <summary>Counts every attach and detach: a runner on a <see cref="NavStatus.Horizon"/> route replans when it changes.</summary>
    public int Version { get; private set; }

    /// <summary>The coarse graph's region count over every attached tile.</summary>
    public int RegionCount { get; private set; }

    internal int SlotCapacity => _tiles.Length;

    /// <summary>
    /// Attaches a tile (replacing one already attached for its landblock): floods its land cells into regions and links
    /// them inside the tile and to the attached neighbours. The tile is read, never written.
    /// </summary>
    public void Attach(NavTile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        if (tile.BlockX >= LandDefs.BlocksPerSide || tile.BlockY >= LandDefs.BlocksPerSide)
        {
            throw new ArgumentOutOfRangeException(nameof(tile), $"{tile} is off the map.");
        }

        Detach(tile.LandblockId);
        int slot = _freeSlots.Count > 0 ? _freeSlots.Pop() : _slotCount++;
        EnsureSlots(slot + 1);
        _tiles[slot] = tile;
        _originX[slot] = tile.BlockX * NavTile.Size;
        _originY[slot] = tile.BlockY * NavTile.Size;
        _slotOfBlock[(tile.BlockX << 8) | tile.BlockY] = (short)slot;
        _attached.Add(tile.LandblockId);
        RebuildNeighbours();
        _graphs[slot] = BuildGraph(slot);
        LinkAcrossEdges(slot);
        RebuildRegionBases();
        Version++;
    }

    /// <summary>Detaches a landblock's tile and the coarse links into it; false when it was not attached.</summary>
    public bool Detach(uint landblockId)
    {
        int bx = (int)(landblockId >> 24);
        int by = (int)((landblockId >> 16) & 0xFF);
        int slot = _slotOfBlock[(bx << 8) | by];
        if (slot < 0)
        {
            return false;
        }

        for (int k = 0; k < 9; k++)
        {
            int n = _nbr[(slot * 9) + k];
            if (n < 0 || n == slot)
            {
                continue;
            }

            foreach (var edges in _graphs[n]!.Edges)
            {
                edges.RemoveAll(e => (e >> 16) == slot);
            }
        }

        _slotOfBlock[(bx << 8) | by] = -1;
        _tiles[slot] = null;
        _graphs[slot] = null;
        _attached.Remove(landblockId & 0xFFFF0000u);
        _freeSlots.Push(slot);
        RebuildNeighbours();
        RebuildRegionBases();
        Version++;
        return true;
    }

    /// <summary>The attached tile of a landblock.</summary>
    public bool TryGetTile(uint landblockId, out NavTile tile)
    {
        int slot = SlotOfBlock((int)(landblockId >> 24), (int)((landblockId >> 16) & 0xFF));
        tile = slot >= 0 ? _tiles[slot]! : null!;
        return slot >= 0;
    }

    /// <summary>The global node (gx, gy) under a point (see the remarks).</summary>
    public static (int Gx, int Gy) GlobalNode(NavPoint point)
    {
        var g = point.Global;
        return ((int)MathF.Floor(g.X / NavTile.NodeMetres), (int)MathF.Floor(g.Y / NavTile.NodeMetres));
    }

    /// <summary>The node under a point, when its tile is attached.</summary>
    public bool TryGetNode(NavPoint point, out NavNode node)
    {
        if (!TryNode(point.Global, out int n))
        {
            node = default;
            return false;
        }

        int slot = n / NodesPerTile;
        int i = n - (slot * NodesPerTile);
        var tile = _tiles[slot]!;
        node = new NavNode(NodeGx(n), NodeGy(n), tile.LandblockId, tile.Z[i], tile.Flags[i], tile.Clearance[i]);
        return true;
    }

    /// <summary>
    /// The lattice line of sight from <paramref name="a"/> to <paramref name="b"/>: every <see cref="SightSampleMetres"/>
    /// sample on an attached, unblocked node with clearance at least <paramref name="minClearance"/>, each change of node
    /// a legal step (the step-up and drop rules, no corner cutting).
    /// </summary>
    public bool LineOfSight(NavPoint a, NavPoint b, int minClearance = 1) =>
        Sight(a.Global, b.Global, minClearance, NavFlags.None, [], [], 0);

    /// <summary>
    /// The point of a retail position: its landblock and its origin. An outdoor position is normalised into the landblock that holds it (an origin past the edge
    /// is the neighbour's ground). An indoor one (an EnvCell) is not: a room's origin is in its own landblock's frame whatever its value, and a dungeon's rooms
    /// lie outside 0-192 m (the Black Spawn Den's 0x01050377 at y -139), so the room graph and the sight probe, which read <see cref="NavPoint.Local"/> in the
    /// goal cell's frame, need it as it came. Normalising it put the den corpse's goal at 0x01040000 [110.02 52.93], 192 m from the corpse in the room's frame,
    /// and the plan ran 201 m into the rock (M4 4.2 F1, DoD 8). <see cref="NavPoint.Global"/> is the same either way.
    /// </summary>
    public static NavPoint FromCellFrame(in CellFrame frame) =>
        LandDefs.IsEnvCell(frame.CellId)
            ? new NavPoint(frame.CellId & 0xFFFF0000u, frame.Origin)
            : new NavPoint(frame.CellId & 0xFFFF0000u, frame.Origin).Normalized();

    /// <summary>
    /// The retail position of a point: the outdoor land cell under it (<see cref="LandDefs.LandCoordToCellId"/>), its
    /// local origin, no rotation. An indoor point maps to the land cell above it: the room graph (S3) owns indoor cells.
    /// </summary>
    public static CellFrame ToCellFrame(NavPoint point)
    {
        var p = point.Normalized();
        int lx = ((int)(p.LandblockId >> 24) * LandDefs.CellsPerBlock) + Math.Clamp((int)(p.Local.X / LandCellNodes), 0, LandCellsPerSide - 1);
        int ly = ((int)((p.LandblockId >> 16) & 0xFF) * LandDefs.CellsPerBlock) + Math.Clamp((int)(p.Local.Y / LandCellNodes), 0, LandCellsPerSide - 1);
        return new CellFrame(LandDefs.LandCoordToCellId(lx, ly), new AcFrame(p.Local, Quaternion.Identity));
    }

    /// <summary>The coarse regions of an attached tile, in the order <see cref="LinksOf"/> numbers them (the world walk's graph, W2). A landblock with no tile has none.</summary>
    /// <param name="landblockId">The landblock, 0xXXYY0000.</param>
    public IReadOnlyList<NavRegionRead> RegionsOf(uint landblockId)
    {
        int slot = SlotOfBlock((int)(landblockId >> 24), (int)((landblockId >> 16) & 0xFF));
        if (slot < 0)
        {
            return [];
        }

        var regions = _graphs[slot]!.Regions;
        var read = new NavRegionRead[regions.Count];
        for (int i = 0; i < read.Length; i++)
        {
            read[i] = new NavRegionRead(regions[i].Cell, regions[i].Count, regions[i].Centroid);
        }

        return read;
    }

    /// <summary>The regions a step leads to from one region, inside its tile and across its edges into attached tiles: a drop links one way only (the world walk's graph, W2).</summary>
    /// <param name="landblockId">The landblock, 0xXXYY0000.</param>
    /// <param name="region">The region's index in <see cref="RegionsOf"/>.</param>
    /// <param name="into">Receives each link's landblock and region.</param>
    public void LinksOf(uint landblockId, int region, List<(uint Landblock, int Region)> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        int slot = SlotOfBlock((int)(landblockId >> 24), (int)((landblockId >> 16) & 0xFF));
        if (slot < 0)
        {
            return;
        }

        foreach (int packed in _graphs[slot]!.Edges[region])
        {
            into.Add((_tiles[packed >> 16]!.LandblockId, packed & 0xFFFF));
        }
    }

    /// <summary>The region of a tile's node, numbered as in <see cref="RegionsOf"/>; -1 when the node is in none or the tile is not attached.</summary>
    /// <param name="landblockId">The landblock, 0xXXYY0000.</param>
    /// <param name="x">The node's x on its tile.</param>
    /// <param name="y">The node's y on its tile.</param>
    public int RegionAt(uint landblockId, int x, int y)
    {
        int slot = SlotOfBlock((int)(landblockId >> 24), (int)((landblockId >> 16) & 0xFF));
        if (slot < 0)
        {
            return -1;
        }

        ushort region = _graphs[slot]!.RegionOf[NavTile.Index(x, y)];
        return region == NoRegion ? -1 : region;
    }

    /// <summary>The coarse regions of one attached tile's land cell (tests and the audit).</summary>
    internal int RegionsInCell(uint landblockId, int cellX, int cellY)
    {
        int slot = SlotOfBlock((int)(landblockId >> 24), (int)((landblockId >> 16) & 0xFF));
        if (slot < 0)
        {
            return 0;
        }

        int cell = (cellY * LandCellsPerSide) + cellX;
        int n = 0;
        foreach (var r in _graphs[slot]!.Regions)
        {
            if (r.Cell == cell)
            {
                n++;
            }
        }

        return n;
    }

    // ---- internals the planner reads ----

    internal int SlotOfBlock(int bx, int by) =>
        (uint)bx < LandDefs.BlocksPerSide && (uint)by < LandDefs.BlocksPerSide ? _slotOfBlock[(bx << 8) | by] : -1;

    internal NavTile TileOfSlot(int slot) => _tiles[slot]!;

    internal int RegionCountOfSlot(int slot) => _graphs[slot]!.Regions.Count;

    internal bool TryNode(Vector2 global, out int node) =>
        TryNodeG((int)MathF.Floor(global.X), (int)MathF.Floor(global.Y), out node);

    internal bool TryNodeG(int gx, int gy, out int node)
    {
        node = -1;
        if (gx < 0 || gy < 0)
        {
            return false;
        }

        int slot = SlotOfBlock(gx / NavTile.Size, gy / NavTile.Size);
        if (slot < 0)
        {
            return false;
        }

        node = (slot * NodesPerTile) + ((gy % NavTile.Size) * NavTile.Size) + (gx % NavTile.Size);
        return true;
    }

    internal int NodeGx(int node)
    {
        int slot = node / NodesPerTile;
        return _originX[slot] + ((node - (slot * NodesPerTile)) % NavTile.Size);
    }

    internal int NodeGy(int node)
    {
        int slot = node / NodesPerTile;
        return _originY[slot] + ((node - (slot * NodesPerTile)) / NavTile.Size);
    }

    internal Vector2 NodeCentre(int node) => new(NodeGx(node) + 0.5f, NodeGy(node) + 0.5f);

    internal float Z(int node)
    {
        int slot = node / NodesPerTile;
        return _tiles[slot]!.Z[node - (slot * NodesPerTile)];
    }

    internal NavFlags Flags(int node)
    {
        int slot = node / NodesPerTile;
        return _tiles[slot]!.Flags[node - (slot * NodesPerTile)];
    }

    internal byte Clearance(int node)
    {
        int slot = node / NodesPerTile;
        return _tiles[slot]!.Clearance[node - (slot * NodesPerTile)];
    }

    internal bool Walkable(int node) => (Flags(node) & NavFlags.Blocked) == 0;

    /// <summary>The corridor index of a node's land cell: slot x 64 + cell.</summary>
    internal static int CellOf(int node)
    {
        int slot = node / NodesPerTile;
        int i = node - (slot * NodesPerTile);
        return (slot * CellsPerTile) + ((i / NavTile.Size / LandCellNodes) * LandCellsPerSide) + ((i % NavTile.Size) / LandCellNodes);
    }

    /// <summary>The neighbour of <paramref name="node"/> in direction <paramref name="dir"/>, or -1 when its tile is not attached.</summary>
    internal int Neighbour(int node, int dir)
    {
        int slot = node / NodesPerTile;
        int i = node - (slot * NodesPerTile);
        int x = (i % NavTile.Size) + Dx[dir];
        int y = (i / NavTile.Size) + Dy[dir];
        int sx = 1;
        int sy = 1;
        if (x < 0)
        {
            x += NavTile.Size;
            sx = 0;
        }
        else if (x >= NavTile.Size)
        {
            x -= NavTile.Size;
            sx = 2;
        }

        if (y < 0)
        {
            y += NavTile.Size;
            sy = 0;
        }
        else if (y >= NavTile.Size)
        {
            y -= NavTile.Size;
            sy = 2;
        }

        int ns = (sx == 1 && sy == 1) ? slot : _nbr[(slot * 9) + (sy * 3) + sx];
        return ns < 0 ? -1 : (ns * NodesPerTile) + (y * NavTile.Size) + x;
    }

    /// <summary>Whether a body may step from <paramref name="from"/> to its neighbour <paramref name="to"/> in direction <paramref name="dir"/>.</summary>
    internal bool CanStep(int from, int to, int dir)
    {
        if (!Walkable(to))
        {
            return false;
        }

        if (dir >= 4)
        {
            int h = Neighbour(from, DiagH[dir]);
            int v = Neighbour(from, DiagV[dir]);
            if (h < 0 || v < 0 || !Walkable(h) || !Walkable(v))
            {
                return false;
            }
        }

        return StepRule(Z(from), Z(to));
    }

    internal static bool StepRule(float zFrom, float zTo)
    {
        float rise = zTo - zFrom;
        return rise <= StepUpMetres && -rise <= MaxDropMetres;
    }

    internal static int Opposite(int dir) => dir < 4 ? (dir + 2) & 3 : 4 + ((dir - 2) & 3);

    /// <summary>
    /// The lattice line of sight with the planner's extras: no node flagged with any of <paramref name="avoid"/> (the costed
    /// water or slope the node path kept off), and the live scars (those flagged in <paramref name="scarIgnored"/> skipped:
    /// the scar the start stands in). M6 14 (m6-14-plaza2): the scars are judged by the line's exact distance from each disc's centre, as the runner judges a plan's line against its kept portals
    /// (<see cref="PortalKeepOff.FirstMeeting(Vector2, float, Vector2, float)"/>), not by the centres of the nodes it crosses, which let a pulled line dip up to half a node's diagonal into a disc (live ladder63, 2026-11-17 18:06:38 UTC).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal bool Sight(Vector2 a, Vector2 b, int minClearance, NavFlags avoid, NavScar[] scars, bool[] scarIgnored, int scarCount)
    {
        for (int k = 0; k < scarCount; k++)
        {
            float r2 = scars[k].Radius * scars[k].Radius;
            float near = SegmentDistance2(scars[k].Centre, a, b);
            if (!scarIgnored[k])
            {
                if (near <= r2)
                {
                    return false;
                }

                continue;
            }

            // M6 sprint (live 2026-11-19 01:51-01:52 UTC, run 3: 93 s against the Scrivener of Item Magic, replayed by ScrivenerDoorStallDatTests): the scar the start stands in was skipped whole, so a pulled line from inside it ran
            // out through the body on its far side (the replan from the stall, due south through her to the door's lane). A line that leaves from inside it may never come nearer its centre than where it leaves; one from
            // outside it is judged as by any scar.
            float df = Vector2.DistanceSquared(a, scars[k].Centre);
            if (df <= r2 ? near < df - 1e-4f : near <= r2)
            {
                return false;
            }
        }

        float len = Vector2.Distance(a, b);
        int n = Math.Max(1, (int)MathF.Ceiling(len / SightSampleMetres));
        int prev = -1;
        int prevGx = 0;
        int prevGy = 0;
        for (int s = 0; s <= n; s++)
        {
            var p = Vector2.Lerp(a, b, (float)s / n);
            int gx = (int)MathF.Floor(p.X);
            int gy = (int)MathF.Floor(p.Y);
            if (prev >= 0 && gx == prevGx && gy == prevGy)
            {
                continue;
            }

            if (!TryNodeG(gx, gy, out int node))
            {
                return false;
            }

            int slot = node / NodesPerTile;
            int i = node - (slot * NodesPerTile);
            var tile = _tiles[slot]!;
            var flags = tile.Flags[i];
            if ((flags & (NavFlags.Blocked | avoid)) != 0 || tile.Clearance[i] < minClearance)
            {
                return false;
            }

            if (prev >= 0)
            {
                if (!StepRule(Z(prev), tile.Z[i]))
                {
                    return false;
                }

                int dx = gx - prevGx;
                int dy = gy - prevGy;
                if (dx != 0 && dy != 0)
                {
                    if (!TryNodeG(prevGx + dx, prevGy, out int h) || !TryNodeG(prevGx, prevGy + dy, out int v) || !Walkable(h) || !Walkable(v))
                    {
                        return false;
                    }
                }
            }

            prev = node;
            prevGx = gx;
            prevGy = gy;
        }

        return true;
    }

    /// <summary>The squared distance from <paramref name="p"/> to the segment from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static float SegmentDistance2(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float len2 = ab.LengthSquared();
        float t = len2 <= 0f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.DistanceSquared(p, a + (ab * t));
    }

    // ---- the coarse graph ----

    internal int RegionOfNode(int node)
    {
        int slot = node / NodesPerTile;
        ushort r = _graphs[slot]!.RegionOf[node - (slot * NodesPerTile)];
        return r == NoRegion ? -1 : _regionBase[slot] + r;
    }

    internal NavRegion Region(int region)
    {
        int slot = _regionSlot[region];
        return _graphs[slot]!.Regions[region - _regionBase[slot]];
    }

    /// <summary>The corridor index (slot x 64 + cell) of a region's land cell.</summary>
    internal int RegionCellIndex(int region)
    {
        int slot = _regionSlot[region];
        return (slot * CellsPerTile) + _graphs[slot]!.Regions[region - _regionBase[slot]].Cell;
    }

    internal int SlotOfRegion(int region) => _regionSlot[region];

    internal List<int> RegionEdges(int region)
    {
        int slot = _regionSlot[region];
        return _graphs[slot]!.Edges[region - _regionBase[slot]];
    }

    /// <summary>A packed edge (slot &lt;&lt; 16 | local region) as a global region index.</summary>
    internal int EdgeTarget(int packed) => _regionBase[packed >> 16] + (packed & 0xFFFF);

    internal (int OriginX, int OriginY) SlotOrigin(int slot) => (_originX[slot], _originY[slot]);

    internal bool SlotAttached(int slot) => slot < _slotCount && _tiles[slot] is not null;

    internal int SlotHighWater => _slotCount;

    private void EnsureSlots(int count)
    {
        if (count <= _tiles.Length)
        {
            return;
        }

        int size = Math.Max(count, _tiles.Length * 2);
        Array.Resize(ref _tiles, size);
        Array.Resize(ref _graphs, size);
        Array.Resize(ref _originX, size);
        Array.Resize(ref _originY, size);
        Array.Resize(ref _regionBase, size);
        int old = _nbr.Length;
        Array.Resize(ref _nbr, size * 9);
        Array.Fill(_nbr, -1, old, _nbr.Length - old);
    }

    private void RebuildNeighbours()
    {
        for (int slot = 0; slot < _slotCount; slot++)
        {
            var tile = _tiles[slot];
            for (int k = 0; k < 9; k++)
            {
                _nbr[(slot * 9) + k] = tile is null
                    ? -1
                    : SlotOfBlock(tile.BlockX + (k % 3) - 1, tile.BlockY + (k / 3) - 1);
            }
        }
    }

    private void RebuildRegionBases()
    {
        int total = 0;
        for (int slot = 0; slot < _slotCount; slot++)
        {
            _regionBase[slot] = total;
            total += _graphs[slot]?.Regions.Count ?? 0;
        }

        RegionCount = total;
        _regionSlot = new int[total];
        for (int slot = 0; slot < _slotCount; slot++)
        {
            var g = _graphs[slot];
            if (g is not null)
            {
                Array.Fill(_regionSlot, slot, _regionBase[slot], g.Regions.Count);
            }
        }
    }

    private TileGraph BuildGraph(int slot)
    {
        var tile = _tiles[slot]!;
        var graph = new TileGraph();
        Array.Fill(graph.RegionOf, NoRegion);
        var flags = tile.Flags;
        var z = tile.Z;

        // Flood each land cell's walkable nodes into regions over the steps that pass both ways.
        for (int cy = 0; cy < LandCellsPerSide; cy++)
        {
            for (int cx = 0; cx < LandCellsPerSide; cx++)
            {
                int x0 = cx * LandCellNodes;
                int y0 = cy * LandCellNodes;
                for (int y = y0; y < y0 + LandCellNodes; y++)
                {
                    for (int x = x0; x < x0 + LandCellNodes; x++)
                    {
                        int seed = NavTile.Index(x, y);
                        if ((flags[seed] & NavFlags.Blocked) != 0 || graph.RegionOf[seed] != NoRegion)
                        {
                            continue;
                        }

                        if (graph.Regions.Count >= NoRegion)
                        {
                            throw new InvalidOperationException($"{tile}: more than {NoRegion - 1} coarse regions.");
                        }

                        ushort r = (ushort)graph.Regions.Count;
                        long sumX = 0;
                        long sumY = 0;
                        int count = 0;
                        int top = 0;
                        _floodStack[top++] = seed;
                        graph.RegionOf[seed] = r;
                        while (top > 0)
                        {
                            int i = _floodStack[--top];
                            int ix = i % NavTile.Size;
                            int iy = i / NavTile.Size;
                            sumX += ix;
                            sumY += iy;
                            count++;
                            for (int d = 0; d < 8; d++)
                            {
                                int nx = ix + Dx[d];
                                int ny = iy + Dy[d];
                                if (nx < x0 || nx >= x0 + LandCellNodes || ny < y0 || ny >= y0 + LandCellNodes)
                                {
                                    continue;
                                }

                                int j = NavTile.Index(nx, ny);
                                if (graph.RegionOf[j] != NoRegion || (flags[j] & NavFlags.Blocked) != 0)
                                {
                                    continue;
                                }

                                if (d >= 4 && ((flags[NavTile.Index(nx, iy)] & NavFlags.Blocked) != 0 || (flags[NavTile.Index(ix, ny)] & NavFlags.Blocked) != 0))
                                {
                                    continue;
                                }

                                if (!StepRule(z[i], z[j]) || !StepRule(z[j], z[i]))
                                {
                                    continue;
                                }

                                graph.RegionOf[j] = r;
                                _floodStack[top++] = j;
                            }
                        }

                        var centroid = new Vector2(
                            _originX[slot] + ((float)sumX / count) + 0.5f,
                            _originY[slot] + ((float)sumY / count) + 0.5f);
                        graph.Regions.Add(new NavRegion((cy * LandCellsPerSide) + cx, centroid, count));
                        graph.Edges.Add([]);
                    }
                }
            }
        }

        // Link regions inside the tile where a step leads from one to another.
        int baseNode = slot * NodesPerTile;
        for (int i = 0; i < NodesPerTile; i++)
        {
            ushort r = graph.RegionOf[i];
            if (r == NoRegion)
            {
                continue;
            }

            int x = i % NavTile.Size;
            int y = i / NavTile.Size;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + Dx[d];
                int ny = y + Dy[d];
                if (!NavTile.InBounds(nx, ny))
                {
                    continue;
                }

                int j = NavTile.Index(nx, ny);
                ushort rj = graph.RegionOf[j];
                if (rj == NoRegion || rj == r)
                {
                    continue;
                }

                if (CanStep(baseNode + i, baseNode + j, d))
                {
                    AddEdge(graph.Edges[r], (slot << 16) | rj);
                }
            }
        }

        return graph;
    }

    private void LinkAcrossEdges(int slot)
    {
        var graph = _graphs[slot]!;
        int baseNode = slot * NodesPerTile;
        for (int i = 0; i < NodesPerTile; i++)
        {
            int x = i % NavTile.Size;
            int y = i / NavTile.Size;
            if (x != 0 && y != 0 && x != NavTile.Size - 1 && y != NavTile.Size - 1)
            {
                // Only the perimeter steps out; skip the interior row by row.
                if (x == 1)
                {
                    i += NavTile.Size - 3;
                }

                continue;
            }

            ushort r = graph.RegionOf[i];
            if (r == NoRegion)
            {
                continue;
            }

            int node = baseNode + i;
            for (int d = 0; d < 8; d++)
            {
                int nb = Neighbour(node, d);
                if (nb < 0)
                {
                    continue;
                }

                int ns = nb / NodesPerTile;
                if (ns == slot)
                {
                    continue;
                }

                var other = _graphs[ns]!;
                ushort rn = other.RegionOf[nb - (ns * NodesPerTile)];
                if (rn == NoRegion)
                {
                    continue;
                }

                if (CanStep(node, nb, d))
                {
                    AddEdge(graph.Edges[r], (ns << 16) | rn);
                }

                if (CanStep(nb, node, Opposite(d)))
                {
                    AddEdge(other.Edges[rn], (slot << 16) | r);
                }
            }
        }
    }

    private static void AddEdge(List<int> edges, int packed)
    {
        if (!edges.Contains(packed))
        {
            edges.Add(packed);
        }
    }

    internal sealed class TileGraph
    {
        public ushort[] RegionOf { get; } = new ushort[NodesPerTile];

        public List<NavRegion> Regions { get; } = [];

        public List<List<int>> Edges { get; } = [];
    }
}

/// <summary>A coarse region as <see cref="NavMap.RegionsOf"/> reads it.</summary>
/// <param name="Cell">The land cell in its tile, cy x 8 + cx.</param>
/// <param name="Nodes">The node count.</param>
/// <param name="Centroid">The nodes' mean centre, global metres.</param>
public readonly record struct NavRegionRead(int Cell, int Nodes, Vector2 Centroid);

/// <summary>A coarse region: the walkable nodes of one land cell a body can walk between both ways.</summary>
/// <param name="Cell">The land cell in its tile, cy x 8 + cx.</param>
/// <param name="Centroid">The nodes' mean centre, global metres.</param>
/// <param name="Count">The node count.</param>
internal readonly record struct NavRegion(int Cell, Vector2 Centroid, int Count);

/// <summary>One node of the attached map (<see cref="NavMap.TryGetNode"/>).</summary>
/// <param name="Gx">The global node x (see <see cref="NavMap"/>'s remarks).</param>
/// <param name="Gy">The global node y.</param>
/// <param name="LandblockId">The node's landblock, 0xXXYY0000.</param>
/// <param name="Z">The ground's height at the node's centre.</param>
/// <param name="Flags">The node's flags.</param>
/// <param name="Clearance">The node's clearance in nodes.</param>
public readonly record struct NavNode(int Gx, int Gy, uint LandblockId, float Z, NavFlags Flags, byte Clearance)
{
    /// <summary>The node's centre as a point (its z the ground's).</summary>
    public NavPoint Centre => NavPoint.FromGlobal(new Vector2(Gx + 0.5f, Gy + 0.5f), Z);
}
