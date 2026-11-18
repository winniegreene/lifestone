using System.Numerics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// A portal of a room that leads outside and that a standing human fits through (<see cref="RoomGraph.ExitsToOutside"/>): the join
/// with a tile's <see cref="NavDoorwayLane"/> (<see cref="NavDoorwayLane.EntryCellId"/> is <see cref="CellId"/>).
/// </summary>
/// <param name="CellId">The EnvCell the portal is in (the cell behind the door).</param>
/// <param name="PolygonId">The portal's polygon id in the cell's structure.</param>
/// <param name="Sill">The portal's centre on its sill, landblock-local (the lane's <see cref="NavDoorwayLane.Sill"/> for the same door).</param>
/// <param name="Outward">The unit horizontal direction from the portal out of the building (the lane's <see cref="NavDoorwayLane.Outward"/>).</param>
public sealed record RoomExit(uint CellId, ushort PolygonId, Vector3 Sill, Vector2 Outward);

/// <summary>
/// The indoor half of the wayfinder (P5a, docs/wayfinding.md): one landblock's rooms as a graph whose nodes are cells and whose
/// vertices are the doorway pairs of <see cref="CellRoutePlanner"/> (<see cref="CellRoutePlanner.ApproachMetres"/> 0.7 m before a
/// portal, <see cref="CellRoutePlanner.PushIntoCellMetres"/> 0.5 m after it). One planner serves the Academy, the shops and the
/// Black Spawn Den: the den's lean (<c>maxLean</c> 0.3) is the only difference, and the den's own planner is gone (closes
/// <c>m3-fight-planner-lean</c>).
/// </summary>
/// <remarks>
/// <para><b>Plan.</b> <see cref="Plan(uint, Vector3, uint, Vector3)"/> is a shortest-walk search (Dijkstra) over the doorway crossings: from the start point to a
/// doorway's approach point, through its pair, on to the next, to the goal point; every crossing also costs
/// <see cref="CrossingMetres"/> so that, of equal walks, the one with fewer doorways wins (the breadth-first rule the den's planner
/// had). It returns a <see cref="CellRoute"/>: the cells and the waypoint pairs; the caller walks to the goal point after the last.</para>
/// <para><b>The join.</b> A portal that leads outside (<see cref="CellPortalData.LeadsOutside"/>) is an <see cref="RoomExit"/>; the
/// tile's doorway lane for the same door (<see cref="NavTileBuilder"/> cuts one per building portal a human fits) is found by its
/// entry cell and its sill (<see cref="TryJoin(NavTile, uint, out NavDoorwayLane)"/>). <see cref="IndoorOutdoorRoute"/> composes
/// the outdoor planner, the lane and this graph.</para>
/// <para><b>Creatures.</b> <see cref="Creatures"/>, a run's creature layer (nav-creatures-indoor, as <see cref="PathPlanner.Creatures"/> outdoors), raises
/// the cost of every straight walk of the search (start to approach, approach to push, push to the goal) by its exposure to the hostile creatures' discs
/// (<see cref="NavCreatures.ExposureAlong(Vector2, float, Vector2, float, uint)"/>: 1 more a metre at a disc's edge rising to 5 at the creature, overlapping discs
/// adding), so a walk through other doorways wins when its extra length is less than the exposure it saves, and a creature in the only way costs the
/// route but never fails it. A creature counts only within <see cref="NavCreatures.StoreyBandMetres"/> of the walk's height (a creature a storey above or below
/// costs nothing: M5 2 fix F2, review 2-R6). Without a layer (or an empty one) every plan is exactly as before.</para>
/// <para><b>Steps and floors</b> (M6 4 W4). A doorway is walked only where the floors either side meet its sill within the step-up
/// (<see cref="CellRoutePlanner.HasFloorsAtSill"/>: Holtburg 0x13F's portals to the threshold 0x140 sit at 95.0 over its floor at 94.0, so Renald's back room
/// 0xA9B40141 is refused at the plan), and a portal no doorway by the fit test is walked where the two cells' floors meet on it
/// (<see cref="CellRoutePlanner.FloorCrossingOf"/>: the island's tusker dungeons, the den's leaning plazas). A straight walk inside a room that would leave its
/// floor or run into a static object goes round through the floor polygons' centres and corners (<see cref="CellRoutePlanner.FloorDetour"/>): Tusker Cave's
/// ramp rises through a hole in 0x5B460114's floor, and 0x5B460185 has a statue on its diagonal. Since M6 4 W6b a floorless cell stands on the floor under its floor portal, a stair through a level portal is walked
/// (<see cref="CellRoutePlanner.StairOf"/>, its points between the approach and the push) and a pit is not. <c>TODO(room-steps)</c> keeps the rest: a step inside a room
/// (not at a doorway) is the runner's physics to judge. A wide opening with a static object on its centre has two crossings (<see cref="CellRoutePlanner.DoorwayPairs"/>, M6 7 G), and the search takes the side its walk lies on.
/// A ramp a static carries past its hole's own cells is an edge of its own (<see cref="CellRoutePlanner.RunsFrom"/>, M6 7 J: the Facility Hub's two), from a cell with no floor polygon to the cell at the ramp's foot and back.</para>
/// <para>Not retail: Lifestone's own wayfinder; <see cref="CrossingMetres"/> and <see cref="JoinToleranceMetres"/> are its own.</para>
/// </remarks>
public sealed class RoomGraph
{
    /// <summary>What a doorway crossing costs beyond its length, in metres: of two equally long walks the one through fewer doorways wins. Not retail: Lifestone's own.</summary>
    public const float CrossingMetres = 0.5f;

    /// <summary>How far under 1.8 m the fit test probes a lintel by default (the shops' doors are 1.8 m tall). Not retail: Lifestone's own.</summary>
    public const float DefaultHeadSlackMetres = 0.05f;

    /// <summary>How far a lane's sill may lie from an exit's sill (horizontally) for them to be the same door. Not retail: Lifestone's own.</summary>
    public const float JoinToleranceMetres = 0.75f;

    /// <summary>How many in-room detours (<see cref="CellRoutePlanner.FloorDetour"/>) the graph keeps before it forgets them all. Not retail: Lifestone's own.</summary>
    public const int MaxCachedDetours = 4096;

    /// <summary>How far inside an exit the indoor walk of <see cref="IndoorOutdoorRoute"/> starts, in metres. Not retail: Lifestone's own.</summary>
    public const float InsideStartMetres = 0.5f;

    /// <summary>
    /// The doorway lean of the buildings on the land (M6 5 w, <c>m6-alabree-entry-walk</c>): the largest vertical part of a building doorway's normal that the town walks
    /// (<see cref="IndoorOutdoorPathSource"/>) and the tile's doorway lanes (<see cref="NavBuildOptions.DoorwayLean"/>) accept, the Black Spawn Den's 0.3. The dungeon-entrance
    /// caves on the land lean their mouths: environment 0x0D0003F2 (the Cave of Alabree's 0xA7B30104 and 0xA7B30103, normal Z 0.24) and 0x0D0003F4 (Holtburg's 0xA9B4017F,
    /// 0xA9B40184 and 0xA9B40189, Shoushi's three, normal Z 0.22 to 0.23), so under the upright rule (0.1) no lane was cut and no exit listed, and a walk to the portal inside
    /// failed <c>no-doorway-lane-reaches-the-goal</c>. The fit test is judged on the flat normal (<see cref="CellRoutePlanner.Fits"/>), so a leaning mouth passes as the
    /// opening a human walks through. Not retail: Lifestone's own (retail walks the physics).
    /// </summary>
    public const float BuildingDoorwayLean = 0.3f;

    /// <summary>The crossing number of a <see cref="StaticRun"/> in an arrival's key: past a portal's (at most two a portal).</summary>
    private const int RunVia = 2;

    /// <summary>M6 14 pits: the crossing number of a jump (<see cref="CellRoutePlanner.JumpsFrom"/>) in an arrival's key: past a run's.</summary>
    private const int JumpVia = 3;

    /// <summary>The crossing number of a <see cref="PairedDoors"/> crossing in an arrival's key: past a jump's (the key holds a crossing number in three bits, <see cref="ViaKey"/>).</summary>
    private const int PairVia = 4;

    /// <summary>The hub walk: <see cref="ViaKey"/>'s crossing numbers of the two crossings <see cref="Beside"/> adds, one either side (5 and 6).</summary>
    private const int BesideVia = 5;

    /// <summary>
    /// M6 14 pits: what a jump costs beyond its length, in metres: the line-up, the charge of the jump bar (a second) and the landing, so a walk of about the same length wins over a jump. Not retail: Lifestone's own.
    /// </summary>
    public const float JumpMetres = 10f;

    /// <summary>How near in area two floors count as equally large for <see cref="RoomFrame"/>, as a share. Not retail: Lifestone's own.</summary>
    private const float EqualFloorShare = 0.01f;

    private readonly LandblockCellSet _cells;
    private readonly CellRoutePlanner _planner;
    private readonly Vector2 _blockOrigin;
    private IReadOnlyList<RoomExit>? _exits;
    private Dictionary<uint, (RoomExit From, RoomExit To)[]>? _paired;
    private PortalKeepOff? _keepOff;
    private IReadOnlyList<KeptPlate> _plateList = [];
    private float? _plateSpeed;
    private PlateKeepOff? _plates;
    private Dictionary<uint, List<DirectedJump>>? _plateJumps;
    private readonly HashSet<string> _refusedPlateJumps = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, Vector3 From, Vector3 To), (IReadOnlyList<CellFrame> Vias, bool Unwalked)> _detours = new();

    /// <summary>A graph over <paramref name="cells"/>: the doorways a standing human fits, those leaning up to <paramref name="maxLean"/> too.</summary>
    /// <param name="cells">The cells walked over (a landblock's, or a subset: a doorway into a cell outside it is not walked).</param>
    /// <param name="maxLean">The largest vertical part of a doorway's normal accepted beyond the upright rule (0: upright only; the den uses 0.3).</param>
    /// <param name="headSlack">How far under the human's 1.8 m the fit test probes the lintel (<see cref="CellRoutePlanner.HeadSlack"/>).</param>
    public RoomGraph(LandblockCellSet cells, float maxLean = 0f, float headSlack = DefaultHeadSlackMetres)
        : this(cells, maxLean, headSlack, memo: null)
    {
    }

    /// <summary>A graph as above whose planner answers from <paramref name="memo"/> (<see cref="CellRoutePlanner.Memo"/>: the work of earlier graphs of the same cells; M6 14 hub plan), or its own when null.</summary>
    /// <param name="cells">The cells walked over.</param>
    /// <param name="maxLean">The lean.</param>
    /// <param name="headSlack">The head slack.</param>
    /// <param name="memo">The memo of the same cells, lean and head slack, or null.</param>
    public RoomGraph(LandblockCellSet cells, float maxLean, float headSlack, CellRoutePlanner.Memo? memo)
    {
        ArgumentNullException.ThrowIfNull(cells);
        _cells = cells;
        _planner = new CellRoutePlanner(cells, maxLean, headSlack, memo);
        _memo = memo;
        _blockOrigin = NavPoint.BlockOrigin(LandblockId);
    }

    private readonly CellRoutePlanner.Memo? _memo;

    /// <summary>How many lists' detours the memo keeps (<see cref="CellRoutePlanner.Memo.KeptDetours"/>): the legs of a hub pass, a few at a time. Not retail: Lifestone's own.</summary>
    private const int MaxKeptLists = 8;

    /// <summary>
    /// M6 14 (m6-14-plaza): where the detours of the current refusals are kept: the memo's, by the kept list's content, when a list of portals is the only thing that refuses a step (<see cref="CellRoutePlanner.Memo.KeptDetours"/>); else this
    /// graph's own.
    /// </summary>
    private System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, Vector3 From, Vector3 To), (IReadOnlyList<CellFrame> Vias, bool Unwalked)> DetourCache
    {
        get
        {
            if (_keepOff is not { } keep || _memo is not { } memo || Plates is not null || Patches.HasBoxes)
            {
                return _detours;
            }

            if (memo.KeptDetours.Count > MaxKeptLists)
            {
                memo.KeptDetours.Clear();
            }

            return memo.KeptDetours.GetOrAdd(keep.ContentKey, _ => new());
        }
    }

    /// <summary>
    /// The creature layer the plans are costed with (nav-creatures-indoor), or null: a run's own layer (<see cref="NavRunner"/> sets it at each plan, so a
    /// graph a source shares between runs takes the planning run's layer, and null for a run without a feed). Null or empty: the plans are today's.
    /// </summary>
    public NavCreatures? Creatures { get; set; }

    /// <summary>M5 7 P7i: the short-lived costly floor patches (spots where walks stuck); empty: the plans are today's.</summary>
    public RoomPatches Patches { get; } = new();

    /// <summary>
    /// M6 7 AI: the portals the plans keep clear of (<see cref="PortalKeepOff"/>), or null: a run's own list, as <see cref="Creatures"/> (<see cref="NavRunner"/> sets it at each plan). Unlike a creature's disc or a patch, a portal costs no metres: no step of a plan
    /// meets one. A straight walk that would goes round (<see cref="CellRoutePlanner.FloorDetour"/>), a doorway crossing that would is not walked, and with no way left the plan is null and the runner names the portal. Null: the plans are today's.
    /// </summary>
    public PortalKeepOff? KeepOff
    {
        get => _keepOff;
        set
        {
            if (!ReferenceEquals(_keepOff, value))
            {
                var same = value is not null && value.SameAs(_keepOff); // M6 14 (m6-14-plaza): the same list made again (the warm-up's and the runner's): the detours stand
                _keepOff = value;
                if (!same)
                {
                    _detours.Clear(); // a detour is the list's own
                }
            }
        }
    }

    /// <summary>
    /// M6 14 holding plates: the pressure plates of the ground (the profile's <c>plates</c>, <c>GroundRooms</c>), walls of every plan over this graph (<see cref="Plates"/>); empty for every other graph. Unlike a patch, a plate costs no
    /// metres: no step of a plan meets its wall. A walk inside a room goes round it (<see cref="CellRoutePlanner.FloorDetour"/>), a doorway crossing that meets one is not walked, a standing jump passes over it
    /// (<see cref="PlateJumpsFrom"/>), and with no way left the plan is null.
    /// </summary>
    public IReadOnlyList<KeptPlate> PlateList
    {
        get => _plateList;
        set
        {
            _plateList = value ?? [];
            ResetPlates();
        }
    }

    /// <summary>
    /// M6 14 holding plates: the planning body's run speed, metres a second, from which the plates' walls take her stop (<see cref="PlateKeepOff.StopAt"/>): a run sets it at each plan from her live Run skill; null takes
    /// <see cref="PlateKeepOff.UnknownSpeedMetresPerSecond"/> (wider walls than any body's).
    /// </summary>
    public float? PlateSpeedMetresPerSecond
    {
        get => _plateSpeed;
        set
        {
            if (value != _plateSpeed)
            {
                _plateSpeed = value;
                ResetPlates();
            }
        }
    }

    /// <summary>M6 14 holding plates: the plates' walls at the planning body's speed, or null for a graph without plates.</summary>
    public PlateKeepOff? Plates => _plateList.Count == 0 ? null : _plates ??= new PlateKeepOff(_plateList, PlateKeepOff.StopAt(_plateSpeed ?? PlateKeepOff.UnknownSpeedMetresPerSecond));

    /// <summary>M6 14 holding plates: the standing jumps refused for the rest of the run (<see cref="RefusePlateJump"/>).</summary>
    public IReadOnlyCollection<string> RefusedPlateJumps => _refusedPlateJumps;

    /// <summary>M6 14 holding plates: takes the standing jump named <paramref name="name"/> out of every later plan over this graph (a plate's spells reached her on it); true when it was not refused before.</summary>
    /// <param name="name">The jump's crossing name (<see cref="PitJump.Name"/>).</param>
    public bool RefusePlateJump(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var added = _refusedPlateJumps.Add(name);
        _plateJumps = null;
        return added;
    }

    /// <summary>
    /// M6 14 holding plates: the standing jumps over the plates from <paramref name="cellId"/> (<see cref="PlateJumps.Derive"/>) for the planning body's flight (<see cref="JumpReachMetres"/>) and speed
    /// (<see cref="PlateSpeedMetresPerSecond"/>: the arc's height comes from both); empty without plates, without her flight or her speed (a body that cannot tell makes no jump over a plate), or for a jump refused
    /// (<see cref="RefusePlateJump"/>).
    /// </summary>
    /// <param name="cellId">The room jumped from.</param>
    public IReadOnlyList<DirectedJump> PlateJumpsFrom(uint cellId)
    {
        if (Plates is not { } walls || JumpReachMetres is not { } flight || _plateSpeed is not { } speed)
        {
            return [];
        }

        if (_plateJumps is null)
        {
            _plateJumps = [];
            foreach (var jump in PlateJumps.Derive(_planner, walls, flight, speed))
            {
                if (_refusedPlateJumps.Contains(jump.Crossing.Name))
                {
                    continue;
                }

                if (!_plateJumps.TryGetValue(jump.From.Cell, out var list))
                {
                    _plateJumps[jump.From.Cell] = list = [];
                }

                list.Add(jump);
            }
        }

        return _plateJumps.TryGetValue(cellId, out var from) ? from : [];
    }

    /// <summary>M6 14 holding plates: every standing jump of the graph now (<see cref="PlateJumpsFrom"/> of every room), for the log and the tests.</summary>
    public IReadOnlyList<DirectedJump> AllPlateJumps()
    {
        _ = PlateJumpsFrom(0);
        return _plateJumps is null ? [] : [.. _plateJumps.Values.SelectMany(j => j)];
    }

    private void ResetPlates()
    {
        _plates = null;
        _plateJumps = null;
        _detours.Clear(); // a detour is the walls' own
    }

    /// <summary>The cells planned over.</summary>
    public LandblockCellSet Cells => _cells;

    /// <summary>M6 14 pits: the pits a running jump crosses (<see cref="CellRoutePlanner.Jumps"/>): the ground's profile sets them (<c>GroundRooms</c>); empty for every other graph.</summary>
    public IReadOnlyList<PitJump> Jumps
    {
        get => _planner.Jumps;
        set => _planner.Jumps = value ?? [];
    }

    /// <summary>M6 14 pits: the planning body's flight (<see cref="CellRoutePlanner.JumpReachMetres"/>): a run sets it at each plan; null takes the profile's jumps as its data says.</summary>
    public float? JumpReachMetres
    {
        get => _planner.JumpReachMetres;
        set
        {
            if (value != _planner.JumpReachMetres)
            {
                _planner.JumpReachMetres = value;
                _plateJumps = null; // M6 14 holding plates: a standing jump lands her own flight past its take-off
            }
        }
    }

    /// <summary>The lean accepted (see the constructor).</summary>
    public float MaxLean => _planner.MaxLean;

    /// <summary>The generalised planner under the graph: its doorway test and waypoints.</summary>
    public CellRoutePlanner Planner => _planner;

    /// <summary>The landblock id (0xXXYY0000) of the cells.</summary>
    public uint LandblockId => ((uint)_cells.Coord.X << 24) | ((uint)_cells.Coord.Y << 16);

    /// <summary>True when a standing human can walk through <paramref name="portal"/> of <paramref name="cell"/> (an inside portal; see <see cref="CellRoutePlanner.Fits"/>).</summary>
    public bool IsPassable(EnvCellData cell, CellPortalData portal) => _planner.IsWalkable(cell, portal);

    /// <summary>M6 8 M: the height of the room's own floor under <paramref name="point"/> (a counter's top is not the room's floor), or null: <see cref="CellRoutePlanner.RoomFloorUnder"/>.</summary>
    /// <param name="cellId">The room.</param>
    /// <param name="point">The point, landblock-local.</param>
    public float? RoomFloorUnder(uint cellId, Vector3 point) => _planner.RoomFloorUnder(cellId, point);

    /// <summary>The cells of the walk with the fewest doorways from <paramref name="from"/> to <paramref name="to"/>, or null.</summary>
    public IReadOnlyList<uint>? FindPath(uint from, uint to) => _planner.FindPath(from, to);

    /// <summary>
    /// M6 14 pit-hold (live 2026-11-13, the Tusker Abode): the room a body standing at <paramref name="point"/> in <paramref name="cellId"/> walks from. A cell of the graph is its own room; a body on a profile pit's lip
    /// (<see cref="CellRoutePlanner.LipOf"/>: held by the edge slide at a brink, its centre past the doorway, so the physics puts it in the pit's floorless cell, which no storey holds and the graph does not) walks from the room
    /// whose brink it stands at, onto whose floor it can step back. Any other cell off the graph is returned as it is, and no plan starts there.
    /// </summary>
    /// <param name="cellId">The cell the body stands in.</param>
    /// <param name="point">Where it stands, landblock-local.</param>
    public uint StandingRoom(uint cellId, Vector3 point) =>
        _cells.GetCell(cellId) is null && _planner.LipOf(cellId, point) is { } lip && _cells.GetCell(lip.Side.Cell) is not null ? lip.Side.Cell : cellId;

    /// <summary>
    /// The shortest walk from <paramref name="from"/> in <paramref name="fromCell"/> to <paramref name="to"/> in <paramref name="toCell"/>
    /// (landblock-local points), or null when either cell is missing or no walkable doorways join them. The route's waypoints are the
    /// doorway pairs only (two per doorway, the first left out where the room is too thin); the goal point is the caller's last step.
    /// </summary>
    public CellRoute? Plan(uint fromCell, Vector3 from, uint toCell, Vector3 to) => Plan(fromCell, from, toCell, to, ignoreCreature: 0);

    /// <summary>
    /// <see cref="Plan(uint, Vector3, uint, Vector3)"/> with <see cref="Creatures"/>' discs costed, <paramref name="ignoreCreature"/> left out (a run's goal
    /// creature, which the walk goes to; 0 for none).
    /// </summary>
    /// <param name="fromCell">The start's cell.</param>
    /// <param name="from">The start, landblock-local.</param>
    /// <param name="toCell">The goal's cell.</param>
    /// <param name="to">The goal, landblock-local.</param>
    /// <param name="ignoreCreature">The creature left out of the cost, or 0.</param>
    public CellRoute? Plan(uint fromCell, Vector3 from, uint toCell, Vector3 to, uint ignoreCreature)
    {
        fromCell = StandingRoom(fromCell, from); // M6 14 pit-hold: a body on a pit's lip walks off it from the room whose brink it stands at
        if (_cells.GetCell(fromCell) is null || _cells.GetCell(toCell) is null)
        {
            return null;
        }

        if (fromCell == toCell && !Refused(from, to))
        {
            return new CellRoute([fromCell], []);
        }

        var layer = Creatures is { Count: > 0 } creatures ? creatures : null;
        var patches = Patches.Count > 0 ? Patches : null;
        var nodes = new List<Arrival> { new(fromCell, from, -1, 0f, null, null, IsGoal: false) };
        var best = new Dictionary<(uint Cell, int Via), float> { [(fromCell, -1)] = 0f };
        var queue = new PriorityQueue<int, float>();
        queue.Enqueue(0, 0f);
        var pairs = new Dictionary<(uint Cell, int Portal), IReadOnlyList<(CellFrame? Approach, CellFrame Push)>>();
        while (queue.TryDequeue(out var index, out _))
        {
            var node = nodes[index];
            if (node.IsGoal)
            {
                return Build(nodes, index);
            }

            if (node.Cost < best[(node.Cell, node.Via)] - 1e-4f)
            {
                continue; // a stale entry
            }

            if (node.Cell == toCell)
            {
                var vias = JudgedDetour(node.Cell, node.Point, to, out var unwalked);
                var last = node.Cost + LegCost(node.Point, vias, to, layer, patches, ignoreCreature) + (unwalked ? UnwalkedLegMetres : 0f);
                if (!float.IsInfinity(last))
                {
                    nodes.Add(new Arrival(node.Cell, to, index, last, null, null, IsGoal: true) { Vias = vias });
                    queue.Enqueue(nodes.Count - 1, nodes[^1].Cost);
                }
            }

            var cell = _cells.GetCell(node.Cell)!;
            for (var i = 0; i < cell.Portals.Count; i++)
            {
                var portal = cell.Portals[i];
                if (portal.LeadsOutside || _cells.GetCell(portal.OtherCellId) is null || !_planner.IsWalkable(cell, portal))
                {
                    continue;
                }

                if (!pairs.TryGetValue((cell.Id, i), out var crossings))
                {
                    crossings = _planner.DoorwayPairs(cell, portal);
                    if ((_keepOff is not null || Plates is not null) && crossings.All(p => Refused(p.Approach?.Origin ?? p.Push.Origin, p.Push.Origin)))
                    {
                        crossings = [.. crossings, .. Beside(cell, portal)];
                    }

                    pairs[(cell.Id, i)] = crossings;
                }

                for (var c = 0; c < crossings.Count; c++)
                {
                    var pair = crossings[c];
                    if (index == 0 && pair.Approach is { } early && PastApproach(node.Point, early.Origin, pair.Push.Origin))
                    {
                        pair = (null, pair.Push);
                    }

                    var cost = node.Cost + CrossingMetres;
                    var at = node.Point;
                    IReadOnlyList<CellFrame> legVias = [];
                    if (pair.Approach is { } approach)
                    {
                        legVias = JudgedDetour(node.Cell, at, approach.Origin, out var unwalked);
                        cost += LegCost(at, legVias, approach.Origin, layer, patches, ignoreCreature) + (unwalked ? UnwalkedLegMetres : 0f);
                        at = approach.Origin;
                    }

                    if (float.IsInfinity(cost) || Refused(at, pair.Push.Origin))
                    {
                        continue;
                    }

                    cost += Vector3.Distance(at, pair.Push.Origin);
                    if (layer is not null)
                    {
                        cost += Exposure(layer, at, pair.Push.Origin, ignoreCreature);
                    }

                    cost += patches?.Cost(at, pair.Push.Origin) ?? 0f;
                    var key = (portal.OtherCellId, Via: ViaKey(cell.Id, c < BesideVia ? c : c + BesideVia - 2, i));
                    if (best.TryGetValue(key, out var known) && known <= cost)
                    {
                        continue;
                    }

                    best[key] = cost;
                    nodes.Add(new Arrival(portal.OtherCellId, pair.Push.Origin, index, cost, pair.Approach, pair.Push, IsGoal: false, key.Via) { Vias = legVias, Climb = _planner.StairClimb(cell, portal) });
                    queue.Enqueue(nodes.Count - 1, cost);
                }
            }

            var paired = PairedFrom(node.Cell);
            for (var p = 0; p < paired.Length; p++)
            {
                var (out1, in2) = paired[p];
                var approach = new CellFrame(out1.CellId, new AcFrame(out1.Sill - new Vector3(out1.Outward * CellRoutePlanner.ApproachMetres, 0f), Quaternion.Identity));
                var push = new CellFrame(in2.CellId, new AcFrame(in2.Sill - new Vector3(in2.Outward * CellRoutePlanner.PushIntoCellMetres, 0f), Quaternion.Identity));
                var legVias = JudgedDetour(node.Cell, node.Point, approach.Origin, out var unwalked);
                var cost = node.Cost + CrossingMetres + LegCost(node.Point, legVias, approach.Origin, layer, patches, ignoreCreature) + (unwalked ? UnwalkedLegMetres : 0f);
                if (float.IsInfinity(cost) || Refused(approach.Origin, push.Origin))
                {
                    continue;
                }

                cost += Vector3.Distance(approach.Origin, push.Origin) + (layer is null ? 0f : Exposure(layer, approach.Origin, push.Origin, ignoreCreature)) + (patches?.Cost(approach.Origin, push.Origin) ?? 0f);
                var key = (in2.CellId, Via: ViaKey(cell.Id, PairVia, p));
                if (best.TryGetValue(key, out var known) && known <= cost)
                {
                    continue;
                }

                best[key] = cost;
                nodes.Add(new Arrival(in2.CellId, push.Origin, index, cost, approach, push, IsGoal: false, key.Via) { Vias = legVias });
                queue.Enqueue(nodes.Count - 1, cost);
            }

            var runs = _planner.RunsFrom(node.Cell);
            for (var r = 0; r < runs.Count; r++)
            {
                var run = runs[r];
                var legVias = JudgedDetour(node.Cell, node.Point, run.Start.Origin, out var unwalked);
                var cost = node.Cost + CrossingMetres + LegCost(node.Point, legVias, run.Start.Origin, layer, patches, ignoreCreature) + run.Length + (unwalked ? UnwalkedLegMetres : 0f);
                var key = (run.ToCell, Via: ViaKey(cell.Id, RunVia, r));
                if (float.IsInfinity(cost) || (best.TryGetValue(key, out var known) && known <= cost))
                {
                    continue;
                }

                best[key] = cost;
                nodes.Add(new Arrival(run.ToCell, run.End.Origin, index, cost, run.Start, run.End, IsGoal: false, key.Via) { Vias = legVias, Climb = run.Points });
                queue.Enqueue(nodes.Count - 1, cost);
            }

            var jumps = _planner.JumpsFrom(node.Cell);
            if (PlateJumpsFrom(node.Cell) is { Count: > 0 } plateJumps)
            {
                jumps = [.. jumps, .. plateJumps]; // M6 14 holding plates
            }

            for (var j = 0; j < jumps.Count; j++)
            {
                // M6 14 pits: walk to the run-up, run to the brink, fly to the landing (a jump's edge: the take-off is the waypoint the runner jumps at).
                var jump = jumps[j];
                var runUp = jump.RunUp;
                var legVias = Detour(node.Cell, node.Point, runUp.Origin);
                var cost = node.Cost + CrossingMetres + JumpMetres + LegCost(node.Point, legVias, runUp.Origin, layer, patches, ignoreCreature)
                    + Vector3.Distance(runUp.Origin, jump.TakeOff.Origin) + Vector3.Distance(jump.TakeOff.Origin, jump.Landing.Origin);
                var key = (jump.To.Cell, Via: ViaKey(cell.Id, JumpVia, j));
                if (float.IsInfinity(cost) || (best.TryGetValue(key, out var known) && known <= cost))
                {
                    continue;
                }

                best[key] = cost;
                var approach = jump.Crossing.Standing ? (CellFrame?)null : runUp; // M6 14 holding plates: a standing jump's run-up is its take-off, walked to once
                nodes.Add(new Arrival(jump.To.Cell, jump.Landing.Origin, index, cost, approach, jump.Landing, IsGoal: false, key.Via) { Vias = legVias, Climb = [jump.TakeOff], Jump = jump });
                queue.Enqueue(nodes.Count - 1, cost);
            }
        }

        return null;
    }

    /// <summary>
    /// The walk of <see cref="Plan(uint, Vector3, uint, Vector3)"/> as a scripted route (the Black Spawn Den's legs): a pass point before and after each doorway
    /// (<see cref="CellRoutePlanner.PushIntoCellMetres"/> tolerance), then a stop at the middle of <paramref name="toCell"/>
    /// (<see cref="RoomFrame"/>) within <paramref name="stopWithin"/>.
    /// </summary>
    /// <param name="name">The route's name.</param>
    /// <param name="from">Where the walk starts.</param>
    /// <param name="toCell">The last cell.</param>
    /// <param name="stopWithin">The arrival radius of the stop, in metres.</param>
    /// <param name="stopDwellSeconds">How long the walker stands at the end, in seconds.</param>
    /// <returns>The route, or null when no walk joins the cells (or the last cell has no floor).</returns>
    public ScriptedRoute? PlanScripted(string name, in CellFrame from, uint toCell, float stopWithin, double stopDwellSeconds)
    {
        if (RoomFrame(toCell) is not { } target || Plan(from.CellId, from.Origin, toCell, target.Origin) is not { } route)
        {
            return null;
        }

        var goals = new List<RouteGoal>(route.Waypoints.Count + 1);
        foreach (var waypoint in route.Waypoints)
        {
            goals.Add(new RouteGoal(waypoint, CellRoutePlanner.PushIntoCellMetres, Stop: false, DwellSeconds: 0));
        }

        goals.Add(new RouteGoal(target, stopWithin, Stop: true, DwellSeconds: stopDwellSeconds));
        return new ScriptedRoute(name, goals) { Start = from };
    }

    /// <summary>
    /// The largest floor polygon's centre of the cell, as a frame in it (or null for a missing cell or one without a floor). Of floors equally large, the first whose centre no static object stands on (<see cref="CellRoutePlanner.InStatic"/>):
    /// the Cave of Alabree's 0x0162017E has four floors of one size round a crossing, and the first one's centre lies inside the ring of a static's physics, which no walk reaches (M6 7 Q). A larger floor is never passed over: a walk stops short of a
    /// centre it cannot stand on (Thelnoth's cellar 0xA9B40111).
    /// </summary>
    /// <param name="cellId">The cell.</param>
    public CellFrame? RoomFrame(uint cellId)
    {
        if (_cells.GetCell(cellId) is not { } cell)
        {
            return null;
        }

        var floors = _cells.GetStructure(cell).Floors;
        if (floors.Count == 0)
        {
            return null;
        }

        var best = floors[0];
        var clear = !_planner.InStatic(cell, cell.Frame.LocalToParent(best.Centroid));
        foreach (var floor in floors)
        {
            var free = !_planner.InStatic(cell, cell.Frame.LocalToParent(floor.Centroid));
            if (floor.Area > best.Area * (1f + EqualFloorShare) || (floor.Area >= best.Area * (1f - EqualFloorShare) && free && !clear))
            {
                (best, clear) = (floor, free);
            }
        }

        return new CellFrame(cell.Id, new AcFrame(cell.Frame.LocalToParent(best.Centroid), Quaternion.Identity));
    }

    /// <summary>
    /// The portals that lead outside and that a standing human fits through (<see cref="CellRoutePlanner.Fits"/>), in cell-id order:
    /// the cell, the sill point and the outward direction. Computed once.
    /// </summary>
    public IReadOnlyList<RoomExit> ExitsToOutside => _exits ??= FindExits();

    /// <summary>
    /// Finds the doorway lane of <paramref name="tile"/> that is the same door as one of the exits of <paramref name="entryCellId"/>
    /// (the lane's <see cref="NavDoorwayLane.EntryCellId"/>, its sill within <see cref="JoinToleranceMetres"/> of the exit's, the same
    /// way out). False when the tile is another landblock's, the cell has no exit here, or no lane matches.
    /// </summary>
    public bool TryJoin(NavTile tile, uint entryCellId, out NavDoorwayLane lane) => TryJoin(tile, entryCellId, out lane, out _);

    /// <summary><see cref="TryJoin(NavTile, uint, out NavDoorwayLane)"/> that also names the exit.</summary>
    public bool TryJoin(NavTile tile, uint entryCellId, out NavDoorwayLane lane, out RoomExit exit)
    {
        ArgumentNullException.ThrowIfNull(tile);
        lane = null!;
        exit = null!;
        if (tile.LandblockId != LandblockId)
        {
            return false;
        }

        var bestDistance = float.MaxValue;
        foreach (var candidate in tile.DoorwayLanes)
        {
            if (candidate.EntryCellId == entryCellId && TryExit(candidate, out var found, out var distance) && distance < bestDistance)
            {
                bestDistance = distance;
                (lane, exit) = (candidate, found);
            }
        }

        return lane is not null;
    }

    /// <summary>
    /// The exit of this graph that is the same door as <paramref name="lane"/>: in the lane's entry cell, its sill within
    /// <see cref="JoinToleranceMetres"/> of the lane's, the same way out. False when there is none.
    /// </summary>
    public bool TryExit(NavDoorwayLane lane, out RoomExit exit) => TryExit(lane, out exit, out _);

    private bool TryExit(NavDoorwayLane lane, out RoomExit exit, out float distance)
    {
        ArgumentNullException.ThrowIfNull(lane);
        exit = null!;
        distance = float.MaxValue;
        foreach (var e in ExitsToOutside)
        {
            if (e.CellId != lane.EntryCellId)
            {
                continue;
            }

            var d = Vector2.Distance(new Vector2(lane.Sill.X, lane.Sill.Y), new Vector2(e.Sill.X, e.Sill.Y));
            if (d <= JoinToleranceMetres && Vector2.Dot(lane.Outward, e.Outward) > 0.9f && d < distance)
            {
                (exit, distance) = (e, d);
            }
        }

        return exit is not null;
    }

    /// <summary>
    /// The ways out that open straight into another building's way out (the world walk, W8: buildings set wall to wall, Viamont 0x31D6 among 102 landblocks): two exits of different cells
    /// whose sills lie within <see cref="JoinToleranceMetres"/> of each other, face opposite ways and stand within the step-up of each other. Each pair is listed both ways, the first exit the
    /// one walked out of. No ground lies between them, so no doorway lane joins them; <see cref="Plan(uint, Vector3, uint, Vector3)"/> walks one as a doorway, from 0.7 m inside the first
    /// sill to 0.5 m inside the second. Computed once. Not retail: Lifestone's own (retail walks the physics, which passes the outdoor cell between the two portals).
    /// </summary>
    public IEnumerable<(RoomExit From, RoomExit To)> PairedDoors => (_paired ??= FindPaired()).Values.SelectMany(p => p);

    /// <summary>True when <paramref name="exit"/> opens straight into another building's way out (<see cref="PairedDoors"/>).</summary>
    public bool IsPaired(RoomExit exit) => PairedFrom(exit.CellId).Any(p => p.From == exit);

    private (RoomExit From, RoomExit To)[] PairedFrom(uint cellId) => (_paired ??= FindPaired()).GetValueOrDefault(cellId, []);

    private Dictionary<uint, (RoomExit From, RoomExit To)[]> FindPaired()
    {
        var found = new Dictionary<uint, List<(RoomExit, RoomExit)>>();
        foreach (var a in ExitsToOutside)
        {
            foreach (var b in ExitsToOutside)
            {
                if (a.CellId != b.CellId
                    && Vector2.Distance(new Vector2(a.Sill.X, a.Sill.Y), new Vector2(b.Sill.X, b.Sill.Y)) <= JoinToleranceMetres
                    && Vector2.Dot(a.Outward, b.Outward) < -0.9f
                    && MathF.Abs(a.Sill.Z - b.Sill.Z) <= CellRoutePlanner.StepUpMetres)
                {
                    if (!found.TryGetValue(a.CellId, out var list))
                    {
                        found[a.CellId] = list = [];
                    }

                    list.Add((a, b));
                }
            }
        }

        return found.ToDictionary(p => p.Key, p => p.Value.ToArray());
    }

    private RoomExit[] FindExits()
    {
        var exits = new List<RoomExit>();
        foreach (var id in _cells.Ids)
        {
            var cell = _cells.GetCell(id)!;
            foreach (var portal in cell.Portals)
            {
                if (portal.LeadsOutside && _planner.Fits(cell, portal) && _planner.DoorwayOf(cell, portal) is { } doorway)
                {
                    exits.Add(new RoomExit(id, portal.PolygonId, new Vector3(doorway.Centre.X, doorway.Centre.Y, doorway.Sill), new Vector2(doorway.Outward.X, doorway.Outward.Y)));
                }
            }
        }

        return [.. exits];
    }

    /// <summary>The layer's extra cost of the straight walk between two landblock-local points: counting only the creatures within a storey band of the walk's height (<see cref="NavCreatures.StoreyBandMetres"/>, M5 2 fix F2).</summary>
    private float Exposure(NavCreatures layer, Vector3 a, Vector3 b, uint ignore) =>
        layer.ExposureAlong(_blockOrigin + new Vector2(a.X, a.Y), a.Z, _blockOrigin + new Vector2(b.X, b.Y), b.Z, ignore);

    /// <summary>
    /// The points a straight walk inside <paramref name="cellId"/> from <paramref name="from"/> to <paramref name="to"/> goes through to stay on the room's floor
    /// (<see cref="CellRoutePlanner.FloorDetour"/>, M6 4 W4: a hole a ramp rises through, an L of a corridor) and clear of the kept portals (M6 7 AI); empty for a straight walk. Computed once per pair.
    /// </summary>
    private IReadOnlyList<CellFrame> Detour(uint cellId, Vector3 from, Vector3 to)
    {
        return JudgedDetour(cellId, from, to, out _);
    }

    /// <summary>
    /// <see cref="Detour"/>, and whether it is a straight walk the pre-check's probe refuses with no way round inside the room (<see cref="CellRoutePlanner.JudgedFloorDetour"/>):
    /// such a leg costs <see cref="UnwalkedLegMetres"/> more, so the search takes a way through the neighbouring rooms where there is one.
    /// </summary>
    private IReadOnlyList<CellFrame> JudgedDetour(uint cellId, Vector3 from, Vector3 to, out bool unwalked)
    {
        var cache = DetourCache;
        if (cache.Count > MaxCachedDetours)
        {
            cache.Clear(); // a long run plans from ever new body points: the cache stays bounded
        }

        Func<Vector3, Vector3, bool>? meets = (_keepOff is not null || Plates is not null, Patches.HasBoxes) switch
        {
            (true, true) => (a, b) => Refused(a, b) || Patches.MeetsBox(a, b),
            (true, false) => Refused,
            (false, true) => Patches.MeetsBox, // M6 14 next grounds: a seeded avoid box is gone round inside a room, not only between doorways
            _ => null,
        };
        var found = cache.GetOrAdd((cellId, from, to), key =>
        {
            var vias = _planner.JudgedFloorDetour(_cells.GetCell(key.Cell)!, key.From, key.To, meets, out var refused);
            return ([.. vias.Select(p => new CellFrame(key.Cell, new AcFrame(p, Quaternion.Identity)))], refused);
        });
        unwalked = found.Unwalked;
        return found.Vias;
    }

    /// <summary>
    /// What a leg the pre-check's probe refuses, with no way round inside its room, costs on top of its length, in metres (<see cref="JudgedDetour"/>): a statue as wide as a niche
    /// stands between a body and the niche's doorway, and the way round lies through the hall beside it (the world walk's rooms, W8: 0x72000257 to 0x72000253 and its 39 copies). The leg is still taken when no
    /// other way is (the body slides round what the probe refuses more often than not). Not retail: Lifestone's own.
    /// </summary>
    public const float UnwalkedLegMetres = 25f;

    /// <summary>How far to the side of the line from a doorway's approach to its push a start may lie and still count as past the approach (<see cref="PastApproach"/>), in metres. Not retail: Lifestone's own.</summary>
    public const float ApproachPassedSideMetres = 0.35f;

    /// <summary>
    /// True when the start <paramref name="body"/> already lies past a doorway's <paramref name="approach"/> on the way to its <paramref name="push"/>, on their line within <see cref="ApproachPassedSideMetres"/> and within a
    /// step's height: the approach would turn the walk back. A body that came in through a doorway stands its push (<see cref="CellRoutePlanner.PushIntoCellMetres"/>) from it, nearer than the approach's
    /// <see cref="CellRoutePlanner.ApproachMetres"/>, so a walk back out went 0.2 m back first, where a thin room's wall stood (the world walk's rooms, W8: 0x00110128 to 0x00110111, 344 walks the pre-check refused
    /// at that first point, in 219 room layouts). Such a walk goes straight to the push.
    /// </summary>
    private static bool PastApproach(Vector3 body, Vector3 approach, Vector3 push)
    {
        var line = new Vector2(push.X - approach.X, push.Y - approach.Y);
        var off = new Vector2(body.X - approach.X, body.Y - approach.Y);
        var length = line.Length();
        if (length < 1e-3f || MathF.Abs(body.Z - approach.Z) > CellRoutePlanner.StepUpMetres)
        {
            return false;
        }

        var along = Vector2.Dot(off, line) / length;
        var side = MathF.Abs((off.X * line.Y) - (off.Y * line.X)) / length;
        return along > 0f && along < length && side <= ApproachPassedSideMetres;
    }

    /// <summary>True when the straight step between two landblock-local points comes within the clearance of a kept portal (<see cref="KeepOff"/>).</summary>
    private bool MeetsPortal(Vector3 a, Vector3 b) =>
        _keepOff is { } keep && keep.FirstMeeting(_blockOrigin + new Vector2(a.X, a.Y), a.Z, _blockOrigin + new Vector2(b.X, b.Y), b.Z) is not null;

    /// <summary>M6 14 holding plates: true when the straight step between two landblock-local points comes within a plate's wall (<see cref="Plates"/>).</summary>
    /// <param name="a">The step's start.</param>
    /// <param name="b">Its end.</param>
    public bool MeetsPlate(Vector3 a, Vector3 b) => Plates is { } walls && walls.FirstMeeting(a, b) is not null;

    /// <summary>A step no plan walks: within a kept portal's clearance or a plate's wall.</summary>
    private bool Refused(Vector3 a, Vector3 b) => MeetsPortal(a, b) || MeetsPlate(a, b);

    /// <summary>The cost of the walk from <paramref name="from"/> through <paramref name="vias"/> to <paramref name="to"/>: its length, the creature layer's exposure and the patches' cost of each straight step; infinite when a step meets a kept portal (<see cref="KeepOff"/>).</summary>
    private float LegCost(Vector3 from, IReadOnlyList<CellFrame> vias, Vector3 to, NavCreatures? layer, RoomPatches? patches, uint ignore)
    {
        var cost = 0f;
        var at = from;
        for (var i = 0; i <= vias.Count; i++)
        {
            var next = i < vias.Count ? vias[i].Origin : to;
            if (Refused(at, next))
            {
                return float.PositiveInfinity;
            }

            cost += Vector3.Distance(at, next);
            if (layer is not null)
            {
                cost += Exposure(layer, at, next, ignore);
            }

            cost += patches?.Cost(at, next) ?? 0f;
            at = next;
        }

        return cost;
    }

    /// <summary>
    /// The hub walk (2026-11-18): the crossings of a wide doorway beside a kept portal's disc or a plate's wall that covers its centre, the nearest to the centre either side along its <see cref="CellRoutePlanner.CrossingSpan"/> whose step through
    /// the opening the list does not refuse (<see cref="CellRoutePlanner.CrossingStepMetres"/> apart). The search crossed only through the centre, and with the centre refused it went the long way round: the Facility Hub's walk to the Tusker
    /// Quarters' front, whose portal's reach covers the middle of the 10 m opening into the front's room, went through the room beside it and back, a U of corners in 10 m. Empty when the doorway has no span or no crossing clears.
    /// </summary>
    private List<(CellFrame? Approach, CellFrame Push)> Beside(EnvCellData cell, CellPortalData portal)
    {
        var found = new List<(CellFrame? Approach, CellFrame Push)>();
        if (_planner.CrossingSpan(cell, portal) is not { } span)
        {
            return found;
        }

        foreach (var (limit, side) in (ReadOnlySpan<(float, float)>)[(span.High, 1f), (-span.Low, -1f)])
        {
            for (var offset = CellRoutePlanner.CrossingStepMetres; offset <= limit; offset += CellRoutePlanner.CrossingStepMetres)
            {
                var pair = _planner.PairAtOffset(cell, portal, side * offset);
                if (!Refused(pair.Approach?.Origin ?? pair.Push.Origin, pair.Push.Origin))
                {
                    found.Add(pair);
                    break;
                }
            }
        }

        return found;
    }

    private static CellRoute Build(List<Arrival> nodes, int goal)
    {
        var chain = new List<Arrival>();
        for (var at = nodes[goal].Parent; at >= 0; at = nodes[at].Parent)
        {
            chain.Add(nodes[at]);
        }

        chain.Reverse();
        var cells = new List<uint>(chain.Count);
        var waypoints = new List<CellFrame>();
        var jumps = new List<RouteJump>();
        foreach (var arrival in chain)
        {
            cells.Add(arrival.Cell);
            waypoints.AddRange(arrival.Vias);
            if (arrival.Approach is { } approach)
            {
                waypoints.Add(approach);
            }

            if (arrival.Jump is { } jump)
            {
                jumps.Add(new RouteJump(waypoints.Count, jump)); // the take-off is the climb's one point; the push (the landing) follows it
            }

            waypoints.AddRange(arrival.Climb);
            if (arrival.Push is { } push && (waypoints.Count == 0 || waypoints[^1] != push))
            {
                waypoints.Add(push); // a slab's stair hop repeats the push of the hop before it
            }
        }

        waypoints.AddRange(nodes[goal].Vias);
        return new CellRoute(cells, waypoints) { Jumps = jumps };
    }

    /// <summary>
    /// An arrival's key past a crossing out of <paramref name="cellId"/>: the cell's low 16 bits, the crossing's number (a portal's first or second crossing, 0 or 1; <see cref="RunVia"/>, <see cref="JumpVia"/>
    /// or <see cref="PairVia"/>) in three bits, and the crossing's index among its kind (under 1,024). Not retail: Lifestone's own.
    /// </summary>
    private static int ViaKey(uint cellId, int crossing, int index) => (int)((cellId & 0xFFFF) << 13) + (crossing << 10) + index;

    /// <summary>One way of being in a cell: the point reached, the cost so far, the arrival before and the doorway pair crossed to get here.</summary>
    private sealed record Arrival(uint Cell, Vector3 Point, int Parent, float Cost, CellFrame? Approach, CellFrame? Push, bool IsGoal, int Via = -1)
    {
        /// <summary>The points walked inside the cell before this arrival's approach (or, for the goal, before the goal): <see cref="Detour"/>.</summary>
        public IReadOnlyList<CellFrame> Vias { get; init; } = [];

        /// <summary>The points of a stair walked between the approach and the push (<see cref="CellRoutePlanner.StairClimb"/>; empty through a doorway).</summary>
        public IReadOnlyList<CellFrame> Climb { get; init; } = [];

        /// <summary>M6 14 pits: the jump this arrival crossed (its climb is the take-off, its approach the run-up, its push the landing), or null.</summary>
        public DirectedJump? Jump { get; init; }
    }
}
