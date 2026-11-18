using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>What the physics and the planner's map disagreed about at one spot.</summary>
/// <param name="Kind">One of the <see cref="PhysicsOracle"/> kinds.</param>
/// <param name="From">Where the body stood.</param>
/// <param name="To">Where the hop ended, or the same point for a stand.</param>
/// <param name="Flags">The map's flags at <paramref name="To"/>.</param>
/// <param name="Detail">The areas involved, when the finding is about two.</param>
public sealed record OracleFinding(string Kind, NavPoint From, NavPoint To, NavFlags Flags, string Detail);

/// <summary>What one landblock's scan found. Repeats exactly for the same dats.</summary>
public sealed class OracleReport
{
    /// <summary>The most findings of one kind kept with their positions; the counts go on.</summary>
    public const int SamplesPerKind = 8;

    private readonly Dictionary<string, int> _kept = [];

    /// <summary>Grid points tried.</summary>
    public int Points { get; set; }

    /// <summary>Grid points the body stood on.</summary>
    public int Stood { get; set; }

    /// <summary>Hops tried.</summary>
    public int Hops { get; set; }

    /// <summary>Hops that ended where they aimed, on walkable ground.</summary>
    public int HopsLanded { get; set; }

    /// <summary>Landed hops where the map agrees: both ends lie in one area.</summary>
    public int Agreed { get; set; }

    /// <summary>Landed hops between two areas along a link the map holds: a one-way drop the planner knows.</summary>
    public int KnownDrops { get; set; }

    /// <summary>Hops that landed in a room; the room graph owns those (W5).</summary>
    public int Indoors { get; set; }

    /// <summary>Hops that landed well above or below the map's ground: a bridge, a roof; the tile holds one height.</summary>
    public int OtherLevel { get; set; }

    /// <summary>Hops into a landblock past the east or north neighbours, left to that landblock's own scan.</summary>
    public int OffMap { get; set; }

    /// <summary>States the scan had to leave out because a landblock reached <see cref="PhysicsOracle.MaxStates"/>.</summary>
    public int StatesDropped { get; set; }

    /// <summary>Findings by kind.</summary>
    public Dictionary<string, int> Counts { get; } = [];

    /// <summary>The first few findings of each kind.</summary>
    public List<OracleFinding> Samples { get; } = [];

    /// <summary>Counts a finding and keeps it if it is among the first of its kind.</summary>
    public void Add(OracleFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        Counts[finding.Kind] = Counts.GetValueOrDefault(finding.Kind) + 1;
        var kept = _kept.GetValueOrDefault(finding.Kind);
        if (kept < SamplesPerKind)
        {
            _kept[finding.Kind] = kept + 1;
            Samples.Add(finding);
        }
    }
}

/// <summary>
/// The physics oracle (world walk W3): an answer to "where can a body go" that does not use the planner's tiles. On a 4 m grid, offset 2 m off the 24 m lines placement refuses
/// (TODO(placement-cell-edge)), the body is placed and takes a real physics step in eight directions, retail's <c>CPhysicsObj::transition</c> (11.6096 0x005138C0). Only then is the map asked, and every
/// disagreement is named. The tiles decide nothing here: they are what is being checked.
/// </summary>
/// <remarks>
/// <para><b>A hop counts</b> only when it ends within <see cref="HopTolerance"/> of its aim on walkable ground: a body that slid along a wall or down a slope did not get there.</para>
/// <para><b>Levels.</b> A hop that lands far from the map's ground is a new state, so the scan climbs onto bridges and ledges from the ground, as a body would. The tile holds one height, so such a state is
/// counted, not compared.</para>
/// <para><b>Refinement.</b> A hop that says the map is wrongly closed is walked again in 1 m hops. A 4 m hop can clear a gap a walking body cannot, so only what survives counts.</para>
/// <para><b>Limit.</b> The physics is shared with the planner's route check and the walker, so a physics bug agrees with itself here. W7 compares it with ACE.</para>
/// </remarks>
/// <param name="walker">The walker, placed in a world with the landblock and its eight neighbours attached.</param>
/// <param name="map">The map, with the landblock's tile and those of its east, north and north-east neighbours attached.</param>
/// <param name="areaOf">The area of a landblock's region (0: none).</param>
/// <param name="areaNodes">An area's size in nodes.</param>
public sealed class PhysicsOracle(OfflineWalker walker, NavMap map, Func<uint, int, int> areaOf, Func<int, int> areaNodes)
{
    /// <summary>A body stood on open ground the map says has room, and the physics refused it: the map is wrongly open.</summary>
    public const string RefusedOnOpen = "refused-on-open";

    /// <summary>A body stood on open ground with room, and no step left it: the map is wrongly open, or the spot is a pit.</summary>
    public const string TrappedOnOpen = "trapped-on-open";

    /// <summary>A body walked onto ground the map blocks: the map is wrongly closed.</summary>
    public const string WalkedOntoBlocked = "walked-onto-blocked";

    /// <summary>A body walked between two areas of at least <see cref="MinAreaNodes"/> nodes that the map says do not join: the map is wrongly closed.</summary>
    public const string JoinedAreas = "joined-areas";

    /// <summary>The same with a smaller area: a sliver the map cut off, which matters far less.</summary>
    public const string JoinedSliver = "joined-sliver";

    /// <summary>One patch of blocked ground a body walks across touches two areas of at least <see cref="MinAreaNodes"/> nodes: the map has closed a real passage.</summary>
    public const string ClosedPassage = "closed-passage";

    /// <summary>A hop's room for error, metres. Not retail: the walk's own.</summary>
    public const float HopTolerance = 0.3f;

    /// <summary>Metres above or below the map's ground past which a body is on another level. Not retail: the walk's own.</summary>
    public const float LevelMetres = 1.5f;

    /// <summary>Nodes an area needs to count as more than a sliver. Not retail: the walk's own.</summary>
    public const int MinAreaNodes = 100;

    /// <summary>The most states one landblock may hold. Not retail: the walk's own, a bound on a landblock of stacked levels.</summary>
    public const int MaxStates = 20000;

    private const float Spacing = 4f;
    private const float Offset = 2f;
    private const float RefineStep = 1f;
    private const byte RoomNodes = 2;

    private readonly List<(uint Landblock, int Region)> _links = [];
    private readonly Dictionary<(int X, int Y), (int X, int Y)> _patchParent = [];
    private readonly Dictionary<(int X, int Y), Dictionary<int, (NavPoint Open, NavPoint Blocked)>> _patchAreas = [];

    /// <summary>Scans one landblock.</summary>
    /// <param name="landblockId">The landblock, 0xXXYY0000. Its tile must be attached.</param>
    /// <returns>What the scan found.</returns>
    public OracleReport Scan(uint landblockId)
    {
        var report = new OracleReport();
        if (!map.TryGetTile(landblockId, out var tile))
        {
            return report;
        }

        _patchParent.Clear();
        _patchAreas.Clear();
        var seen = new HashSet<(int X, int Y, int Level)>();
        var queue = new Queue<CellFrame>();
        for (var gy = Offset; gy < NavTile.Size; gy += Spacing)
        {
            for (var gx = Offset; gx < NavTile.Size; gx += Spacing)
            {
                var (x, y) = NavTile.NodeAt(new Vector2(gx, gy));
                var index = NavTile.Index(x, y);
                var point = new NavPoint(landblockId, new Vector3(gx, gy, tile.Z[index]));
                var at = NavMap.ToCellFrame(point);
                report.Points++;
                if (walker.Physics.SetPosition(at, SetPositionFlags.Placement) != SetPositionError.OK)
                {
                    if (tile.IsWalkable(x, y) && tile.Clearance[index] >= RoomNodes)
                    {
                        report.Add(new OracleFinding(RefusedOnOpen, point, point, tile.Flags[index], string.Empty));
                    }

                    continue;
                }

                report.Stood++;
                seen.Add(KeyOf(point.Global, tile.Z[index]));
                queue.Enqueue(at);
            }
        }

        while (queue.TryDequeue(out var state))
        {
            var landed = 0;
            for (var d = 0; d < 8; d++)
            {
                if (Hop(report, state, d, Spacing, tile, seen, queue))
                {
                    landed++;
                }
            }

            if (landed == 0)
            {
                NoteTrapped(report, state, tile);
            }
        }

        ReportPassages(report, tile);
        return report;
    }

    private static (int X, int Y) PatchKey(Vector2 global) => ((int)MathF.Floor(global.X / Spacing), (int)MathF.Floor(global.Y / Spacing));

    private (int X, int Y) PatchRoot((int X, int Y) key)
    {
        if (!_patchParent.TryGetValue(key, out var parent))
        {
            _patchParent[key] = key;
            return key;
        }

        while (parent != key)
        {
            key = parent;
            parent = _patchParent[key];
        }

        return key;
    }

    private void JoinPatches((int X, int Y) a, (int X, int Y) b) => _patchParent[PatchRoot(a)] = PatchRoot(b);

    private void TouchPatch(Vector2 blockedGlobal, int area, NavPoint open, NavPoint blocked)
    {
        var key = PatchKey(blockedGlobal);
        PatchRoot(key);
        if (!_patchAreas.TryGetValue(key, out var areas))
        {
            _patchAreas[key] = areas = [];
        }

        areas.TryAdd(area, (open, blocked));
    }

    /// <summary>Each patch of blocked ground with big areas on two sides is one finding, the two largest areas named. Each hop is judged from its own tile, so a patch across a border is seen in part.</summary>
    private void ReportPassages(OracleReport report, NavTile tile)
    {
        var patches = new Dictionary<(int X, int Y), Dictionary<int, (NavPoint Open, NavPoint Blocked)>>();
        foreach (var (key, areas) in _patchAreas)
        {
            var root = PatchRoot(key);
            if (!patches.TryGetValue(root, out var merged))
            {
                patches[root] = merged = [];
            }

            foreach (var (area, touch) in areas)
            {
                merged.TryAdd(area, touch);
            }
        }

        foreach (var (_, merged) in patches.OrderBy(p => p.Key))
        {
            var big = merged.Keys.Where(a => areaNodes(a) >= MinAreaNodes).OrderByDescending(areaNodes).ThenBy(a => a).Take(2).ToArray();
            if (big.Length == 2)
            {
                var (open, blocked) = merged[big[0]];
                var flags = blocked.LandblockId == tile.LandblockId ? FlagsAt(tile, blocked) : NavFlags.None;
                report.Add(new OracleFinding(ClosedPassage, open, merged[big[1]].Open, flags, $"{big[0]}>{big[1]}"));
            }
        }
    }

    private static NavFlags FlagsAt(NavTile tile, NavPoint point)
    {
        var (x, y) = NavTile.NodeAt(new Vector2(point.Local.X, point.Local.Y));
        return tile.Flags[NavTile.Index(x, y)];
    }

    /// <summary>True when the map holds a link from the region at the hop's start into <paramref name="areaTo"/>.</summary>
    private bool Linked(uint landblockId, int x, int y, int areaTo)
    {
        var region = map.RegionAt(landblockId, x, y);
        if (region < 0)
        {
            return false;
        }

        _links.Clear();
        map.LinksOf(landblockId, region, _links);
        foreach (var (block, into) in _links)
        {
            if (areaOf(block, into) == areaTo)
            {
                return true;
            }
        }

        return false;
    }

    private static (int X, int Y, int Level) KeyOf(Vector2 global, float z) =>
        ((int)MathF.Floor(global.X / Spacing), (int)MathF.Floor(global.Y / Spacing), (int)MathF.Round(z / LevelMetres));

    private void NoteTrapped(OracleReport report, in CellFrame state, NavTile tile)
    {
        var point = NavMap.FromCellFrame(state);
        if (point.LandblockId == tile.LandblockId)
        {
            var (x, y) = NavTile.NodeAt(new Vector2(point.Local.X, point.Local.Y));
            var index = NavTile.Index(x, y);
            if (tile.IsWalkable(x, y) && tile.Clearance[index] >= RoomNodes && MathF.Abs(point.Local.Z - tile.Z[index]) < LevelMetres)
            {
                report.Add(new OracleFinding(TrappedOnOpen, point, point, tile.Flags[index], string.Empty));
            }
        }
    }

    /// <summary>One step of the physics in one of the eight directions; true when it landed where it aimed.</summary>
    private bool Hop(OracleReport report, in CellFrame state, int direction, float metres, NavTile tile, HashSet<(int X, int Y, int Level)> seen, Queue<CellFrame> queue)
    {
        var end = Step(state, direction, metres);
        report.Hops++;
        if (end is not { } landed)
        {
            return false;
        }

        report.HopsLanded++;
        if (LandDefs.IsEnvCell(landed.CellId))
        {
            report.Indoors++;
            return true;
        }

        var from = NavMap.FromCellFrame(state);
        var to = NavMap.FromCellFrame(landed);
        if (!map.TryGetTile(to.LandblockId, out var target))
        {
            report.OffMap++;
            return true;
        }

        var (tx, ty) = NavTile.NodeAt(new Vector2(to.Local.X, to.Local.Y));
        var (fx, fy) = NavTile.NodeAt(new Vector2(from.Local.X, from.Local.Y));
        var toIndex = NavTile.Index(tx, ty);
        if (MathF.Abs(to.Local.Z - target.Z[toIndex]) >= LevelMetres)
        {
            report.OtherLevel++;
            if (to.LandblockId == tile.LandblockId && seen.Count < MaxStates && seen.Add(KeyOf(to.Global, to.Local.Z)))
            {
                queue.Enqueue(landed);
            }
            else if (seen.Count >= MaxStates)
            {
                report.StatesDropped++;
            }

            return true;
        }

        if (!map.TryGetTile(from.LandblockId, out var source) || MathF.Abs(from.Local.Z - source.Z[NavTile.Index(fx, fy)]) >= LevelMetres)
        {
            return true; // a step along an upper level says nothing about the map's ground
        }

        var areaFrom = AreaAt(from.LandblockId, fx, fy);
        var areaTo = AreaAt(to.LandblockId, tx, ty);
        if (areaFrom == 0)
        {
            // Not a finding of its own: it says which areas a patch of blocked ground joins (ReportPassages). Tried only when it adds to what the patch knows, as the refinement is dear.
            var fromKey = PatchKey(from.Global);
            if (areaTo == 0)
            {
                if (PatchRoot(fromKey) != PatchRoot(PatchKey(to.Global)) && Refined(state, direction, metres))
                {
                    JoinPatches(fromKey, PatchKey(to.Global));
                }
            }
            else if ((!_patchAreas.TryGetValue(fromKey, out var known) || !known.ContainsKey(areaTo)) && Refined(state, direction, metres))
            {
                TouchPatch(from.Global, areaTo, to, from);
            }

            return true;
        }

        if (areaFrom == areaTo)
        {
            report.Agreed++;
            return true;
        }

        if (areaTo != 0 && Linked(from.LandblockId, fx, fy, areaTo))
        {
            report.KnownDrops++;
            return true;
        }

        if (!Refined(state, direction, metres))
        {
            return true;
        }

        if (areaTo == 0)
        {
            report.Add(new OracleFinding(WalkedOntoBlocked, from, to, target.Flags[toIndex], string.Empty));
            TouchPatch(to.Global, areaFrom, from, to);
            return true;
        }

        var smaller = Math.Min(areaNodes(areaFrom), areaNodes(areaTo));
        report.Add(new OracleFinding(smaller >= MinAreaNodes ? JoinedAreas : JoinedSliver, from, to, target.Flags[toIndex], $"{areaFrom}>{areaTo}"));
        return true;
    }

    /// <summary>The same hop in 1 m steps: true when a walking body gets there too.</summary>
    private bool Refined(in CellFrame state, int direction, float metres)
    {
        var at = state;
        for (var walked = 0f; walked < metres; walked += RefineStep)
        {
            if (Step(at, direction, RefineStep) is not { } next || LandDefs.IsEnvCell(next.CellId))
            {
                return false;
            }

            at = next;
        }

        return true;
    }

    /// <summary>Places the body and steps it; the position it landed at, or null when it did not land where it aimed.</summary>
    private CellFrame? Step(in CellFrame from, int direction, float metres)
    {
        var heading = new Vector3(NavMap.Dx[direction], NavMap.Dy[direction], 0f);
        var move = Vector3.Normalize(heading) * metres;
        var aim = NavMap.FromCellFrame(from).Global + new Vector2(move.X, move.Y);
        var body = walker.Physics;
        if (body.SetPosition(from, SetPositionFlags.Placement) != SetPositionError.OK)
        {
            return null;
        }

        var start = body.Position;
        var to = new CellFrame(start.CellId, new AcFrame(start.Origin + move, start.Frame.Orientation));
        if (!body.TryTransitionTo(to, carriedContact: true) || !walker.OnGround)
        {
            return null;
        }

        var end = body.Position;
        return Vector2.Distance(NavMap.FromCellFrame(end).Global, aim) <= HopTolerance ? end : null;
    }

    private int AreaAt(uint landblockId, int x, int y)
    {
        var region = map.RegionAt(landblockId, x, y);
        return region < 0 ? 0 : areaOf(landblockId, region);
    }
}
