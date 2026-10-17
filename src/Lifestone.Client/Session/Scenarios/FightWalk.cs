using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>Where a leg of the fight loop's walk stands (<see cref="IFightWalk.Tick"/>).</summary>
public enum FightWalkStatus
{
    /// <summary>No leg is being walked.</summary>
    Idle,

    /// <summary>A leg is being walked.</summary>
    Walking,

    /// <summary>The leg's last waypoint was reached (reported once; the walk is then idle).</summary>
    Arrived,

    /// <summary>The leg failed (<see cref="IFightWalk.Failure"/> says why).</summary>
    Failed,
}

/// <summary>
/// The walk of the fight loop (M3 3.1 lane fa): a body that follows the server's view of the player and walks it along the den's floor, one leg at a time, to the next waypoint. The loop
/// (<see cref="FightScenario"/>) only asks for legs; <see cref="DenWalk"/> is the real one (physics and the dats), the tests give a scripted one. Game thread only.
/// </summary>
public interface IFightWalk : IDisposable
{
    /// <summary>The reason a leg failed, or null.</summary>
    string? Failure { get; }

    /// <summary>Makes the body and starts following the server's positions of the player; call before the teleport to the den, once (a second call does nothing).</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The frame time.</param>
    void Prepare(IGameSessionView session, double now);

    /// <summary>Plans and starts the leg to the next waypoint of the floor from where the player stands.</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The frame time.</param>
    /// <param name="description">What the leg is (the waypoint's cell and the doorways), for the log.</param>
    /// <returns>False with <see cref="Failure"/> set when no leg can be planned.</returns>
    bool BeginLeg(IGameSessionView session, double now, out string description);

    /// <summary>One frame: applies the server's positions of the player, ticks the body and the leg. Call every frame after <see cref="Prepare"/>.</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The frame time.</param>
    FightWalkStatus Tick(IGameSessionView session, double now);

    /// <summary>Drops the leg and releases every key.</summary>
    void Abandon();
}

/// <summary>
/// Routes over the den's floor: a breadth-first search over the floor's cells through the doorways a standing human fits, with the waypoints of <see cref="CellRoutePlanner"/>
/// (<see cref="CellRoutePlanner.Waypoint"/>). Not <see cref="CellRoutePlanner.Plan"/>: the den's plazas are octagons whose doorways lean 11 degrees (the portal plane's normal has a
/// vertical part of 0.196), which <see cref="CellRoutePlanner.IsWalkable"/> refuses (it wants upright portals, normal Z under 0.1), so the planner finds no walk from the arrival to any
/// spawn cell but one. <see cref="Passable"/> accepts the planner's doorways and the leaning ones (normal Z under <see cref="MaxLean"/>) by the planner's own fit test.
/// <c>TODO(m3-fight-planner-lean)</c>: the planner could take the lean as a parameter; the lane that owns it can then drop this class's search.
/// </summary>
public sealed class DenPlanner
{
    /// <summary>The largest vertical part of a doorway's normal accepted here (the den's plazas lean 0.196; floors and ceilings are 1).</summary>
    public const float MaxLean = 0.3f;

    private readonly LandblockCellSet _cells;
    private readonly CellRoutePlanner _planner;

    /// <summary>A planner over <paramref name="cells"/> (the floor's cells alone: a doorway into a cell outside it is not walked).</summary>
    /// <param name="cells">The floor's cells.</param>
    public DenPlanner(LandblockCellSet cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        _cells = cells;
        _planner = new CellRoutePlanner(cells);
    }

    /// <summary>The cells planned over.</summary>
    public LandblockCellSet Cells => _cells;

    /// <summary>The den's floor out of a loaded landblock: the cells <see cref="BlackSpawnDen.IsFloorCell"/> accepts.</summary>
    /// <param name="all">Every cell of landblock 0x0105.</param>
    public static LandblockCellSet FloorOf(LandblockCellSet all)
    {
        ArgumentNullException.ThrowIfNull(all);
        return LandblockCellSet.FromCells(all.Structures, all.Coord, all.Cells.Where(c => BlackSpawnDen.IsFloorCell(c.Id)));
    }

    /// <summary>True when a standing human can walk through <paramref name="portal"/> of <paramref name="cell"/> (see the class remarks).</summary>
    /// <param name="cell">The cell the portal is in.</param>
    /// <param name="portal">The portal.</param>
    public bool Passable(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (_planner.IsWalkable(cell, portal))
        {
            return true;
        }

        if (portal.LeadsOutside || _cells.GetStructure(cell).GetPortal(portal.PolygonId) is not { } polygon)
        {
            return false;
        }

        var normal = cell.Frame.RotateToParent(polygon.Plane.Normal);
        if (MathF.Abs(normal.Z) > MaxLean || MathF.Abs(normal.Z) <= 0.1f)
        {
            return false; // a floor or a ceiling, or an upright doorway the planner already judged
        }

        var corners = new Vector3[polygon.Vertices.Length];
        var centre = Vector3.Zero;
        var sill = float.MaxValue;
        for (var i = 0; i < corners.Length; i++)
        {
            corners[i] = cell.Frame.LocalToParent(polygon.Vertices[i]);
            centre += corners[i];
            sill = MathF.Min(sill, corners[i].Z);
        }

        centre /= corners.Length;
        var flat = new Vector3(normal.X, normal.Y, 0f);
        if (flat.LengthSquared() < 1e-6f)
        {
            return false;
        }

        var along = Vector3.Normalize(new Vector3(-flat.Y, flat.X, 0f));
        var floor = new Vector3(centre.X, centre.Y, sill);
        return Inside(corners, along, floor + new Vector3(0, 0, 0.2f))
            && Inside(corners, along, floor + new Vector3(0, 0, 1.8f))
            && Inside(corners, along, floor + new Vector3(0, 0, 1f) + (along * CellRoutePlanner.HumanHalfWidth))
            && Inside(corners, along, floor + new Vector3(0, 0, 1f) - (along * CellRoutePlanner.HumanHalfWidth));
    }

    /// <summary>The cells of the shortest walk (fewest doorways) from <paramref name="from"/> to <paramref name="to"/>, or null.</summary>
    /// <param name="from">The first cell.</param>
    /// <param name="to">The last cell.</param>
    public IReadOnlyList<uint>? FindPath(uint from, uint to)
    {
        if (_cells.GetCell(from) is null || _cells.GetCell(to) is null)
        {
            return null;
        }

        var previous = new Dictionary<uint, uint> { [from] = 0 };
        var queue = new Queue<uint>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (id == to)
            {
                var path = new List<uint>();
                for (var at = to; at != 0; at = previous[at])
                {
                    path.Add(at);
                    if (at == from)
                    {
                        break;
                    }
                }

                path.Reverse();
                return path;
            }

            var cell = _cells.GetCell(id)!;
            foreach (var portal in cell.Portals)
            {
                if (portal.LeadsOutside || previous.ContainsKey(portal.OtherCellId) || _cells.GetCell(portal.OtherCellId) is null || !Passable(cell, portal))
                {
                    continue;
                }

                previous[portal.OtherCellId] = id;
                queue.Enqueue(portal.OtherCellId);
            }
        }

        return null;
    }

    /// <summary>The walk from <paramref name="from"/> to the middle of cell <paramref name="toCell"/>: a pass point before and after each doorway, then a stop within <paramref name="stopWithin"/>.</summary>
    /// <param name="name">The route's name.</param>
    /// <param name="from">Where the walk starts.</param>
    /// <param name="toCell">The last cell.</param>
    /// <param name="stopWithin">The arrival radius of the stop, in metres.</param>
    /// <returns>The route, or null when no walk joins the cells.</returns>
    public ScriptedRoute? Plan(string name, in CellFrame from, uint toCell, float stopWithin)
    {
        if (FindPath(from.CellId, toCell) is not { } path || RoomFrame(toCell) is not { } target)
        {
            return null;
        }

        var goals = new List<RouteGoal>(path.Count * 2);
        for (var i = 0; i + 1 < path.Count; i++)
        {
            var cell = _cells.GetCell(path[i])!;
            CellPortalData? door = null;
            foreach (var portal in cell.Portals)
            {
                if (portal.OtherCellId == path[i + 1] && Passable(cell, portal))
                {
                    door = portal;
                    break;
                }
            }

            if (door is not { } p)
            {
                return null;
            }

            if (Inside(cell, p, -CellRoutePlanner.ApproachMetres) is { } approach)
            {
                goals.Add(new RouteGoal(approach, CellRoutePlanner.PushIntoCellMetres, Stop: false, DwellSeconds: 0));
            }

            goals.Add(new RouteGoal(Inside(cell, p, CellRoutePlanner.PushIntoCellMetres) ?? _planner.Waypoint(cell, p, 0f), CellRoutePlanner.PushIntoCellMetres, Stop: false, DwellSeconds: 0));
        }

        goals.Add(new RouteGoal(target, stopWithin, Stop: true, DwellSeconds: StopDwellSeconds));
        return new ScriptedRoute(name, goals) { Start = from };
    }

    /// <summary>The largest floor polygon's centre of the cell, as a frame in it (or null for a cell without a floor).</summary>
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
        foreach (var f in floors)
        {
            if (f.Area > best.Area)
            {
                best = f;
            }
        }

        return new CellFrame(cell.Id, new AcFrame(cell.Frame.LocalToParent(best.Centroid), Quaternion.Identity));
    }

    /// <summary>How long the walker stands at the end of a leg, in seconds (a short rest; the loop then casts).</summary>
    public const double StopDwellSeconds = 0.25;

    /// <summary><see cref="CellRoutePlanner.Waypoint"/> at <paramref name="metres"/> or half of it, whichever lies inside the cell it is meant for (the planner's own rule, <c>InsideWaypoint</c>, which is private).</summary>
    private CellFrame? Inside(EnvCellData cell, CellPortalData portal, float metres)
    {
        var expected = metres >= 0f ? _cells.GetCell(portal.OtherCellId) : cell;
        if (expected is null)
        {
            return null;
        }

        foreach (var distance in (ReadOnlySpan<float>)[metres, metres * 0.5f])
        {
            var point = _planner.Waypoint(cell, portal, distance);
            if (_cells.Contains(expected, point.Frame.Origin + new Vector3(0f, 0f, 1f)))
            {
                return point;
            }
        }

        return null;
    }

    /// <summary>A crossing test of the point against the polygon in the plane of the horizontal axis <paramref name="along"/> and Z.</summary>
    private static bool Inside(Vector3[] corners, Vector3 along, Vector3 point)
    {
        var inside = false;
        var px = Vector3.Dot(point, along);
        var py = point.Z;
        for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
        {
            var xi = Vector3.Dot(corners[i], along);
            var yi = corners[i].Z;
            var xj = Vector3.Dot(corners[j], along);
            var yj = corners[j].Z;
            if ((yi > py) != (yj > py) && px < ((xj - xi) * (py - yi) / (yj - yi)) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}

/// <summary>
/// The real <see cref="IFightWalk"/>: an <see cref="OfflineWalker"/> in the den's collision (landblock 0x0105 alone), placed where the server puts the player and given the server's
/// positions and moves as <see cref="RoutePlay"/> gives them to its walker (the same session wiring: the presence-gated movement sink, the player's own ObjectMoved and ObjectMotionChanged,
/// <see cref="SessionTargetWorld"/> for the TurnToObject ACE sends when it turns the caster), and a <see cref="RouteDriver"/> for each leg of <see cref="DenPlanner"/>'s route.
/// </summary>
/// <remarks>
/// <para>The waypoints are the floor's spawn cells in <see cref="Tour"/> order, nearest the arrival first; a leg goes to the next one in the tour that is not the cell the player stands in and
/// has a walk from it. Nothing here asserts that the server agrees with the walker: the loop's fight has its own checks (a cast at a target in range is the server's word on where the player is).</para>
/// <para><c>TODO(m3-fight-walk-audit)</c>: the walk is not audited against ACE as <see cref="RoutePlay"/>'s is (no where-am-I at the stops, no corrections count).</para>
/// </remarks>
public sealed class DenWalk : IFightWalk
{
    /// <summary>The spawn cells' low words in the order the loop visits them (nearest the arrival first, then around the floor).</summary>
    public static readonly IReadOnlyList<ushort> Tour = [0x0383, 0x0377, 0x036F, 0x033F, 0x0338, 0x0320, 0x0303, 0x0310, 0x030D, 0x0325, 0x02E2, 0x02E1];

    /// <summary>The arrival radius of a leg's last stop, in metres.</summary>
    public const float StopWithinMetres = 1.0f;

    /// <summary>The longest a leg may take, in seconds on the session clock.</summary>
    public const double LegSeconds = 90;

    private const string LogCategory = "fight";

    private readonly DatSet _dats;
    private readonly PhysicsGeometryLibrary _geometry;
    private readonly PhysicsWorld _world;
    private readonly DenPlanner _planner;
    private readonly ILogSink _log;
    private readonly List<ObjectMoved> _moves = [];
    private readonly List<ObjectMotionChanged> _serverMotions = [];
    private WireMotionItem[] _actions = new WireMotionItem[16];
    private IDisposable? _movesSubscription;
    private IDisposable? _motionSubscription;
    private OfflineWalker? _walker;
    private PresenceGatedMovementSink? _gate;
    private SessionTargetWorld? _targets;
    private RouteDriver? _driver;
    private bool _placed;
    private bool _disposed;
    private int _next;
    private int _legs;
    private double _legStart;

    private DenWalk(DatSet dats, PhysicsGeometryLibrary geometry, PhysicsWorld world, DenPlanner planner, ILogSink log)
    {
        (_dats, _geometry, _world, _planner, _log) = (dats, geometry, world, planner, log);
    }

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <summary>The legs begun so far.</summary>
    public int Legs => _legs;

    /// <summary>The walker, or null before <see cref="Prepare"/>.</summary>
    public OfflineWalker? Walker => _walker;

    /// <summary>The floor planner.</summary>
    public DenPlanner Planner => _planner;

    /// <summary>Builds the den's collision and floor graph from the dats (a few seconds).</summary>
    /// <param name="dats">The dats.</param>
    /// <param name="log">Where the walk logs.</param>
    /// <exception cref="InvalidDataException">The den's cells do not load.</exception>
    public static DenWalk Create(DatSet dats, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(log);
        var geometry = new PhysicsGeometryLibrary(dats);
        var coord = new LandblockCoord(1, 5);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, geometry).Build(coord));
        var floor = DenPlanner.FloorOf(LandblockCellSet.Load(new CellStructureLibrary(dats), coord));
        return new DenWalk(dats, geometry, world, new DenPlanner(floor), log);
    }

    /// <inheritdoc/>
    public void Prepare(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_walker is not null)
        {
            return;
        }

        var self = session.State.Self;
        if (!self.Qualities.IsKnown)
        {
            Failure = "preparing the walk: the player's qualities have not arrived (no PlayerDescription), so its run and jump skills are unknown";
            return;
        }

        var weenie = QualitiesWeenie.FromSkillTable(self.Qualities, SkillTableData.Load(_dats), _log);
        _gate = new PresenceGatedMovementSink(session.Actions.Movement, () => session.State.Self.Travel == SelfTravel.Present);
        _world.SetTime(now);
        _walker = OfflineWalker.Create(_dats, _world, _log, weenie, _geometry, id: self.Guid, sink: _gate);
        _targets = new SessionTargetWorld(session.State, _dats, _geometry, () => _world.CurrentTime, _walker.Physics);
        _walker.Physics.TargetWorld = _targets;
        _movesSubscription = session.Events.Subscribe<ObjectMoved>((in ObjectMoved e) =>
        {
            if (e.IsSelf)
            {
                _moves.Add(e);
            }
        });
        _motionSubscription = session.Events.Subscribe<ObjectMotionChanged>((in ObjectMotionChanged e) =>
        {
            if (e.IsSelf && !e.Autonomous)
            {
                _serverMotions.Add(e);
            }
        });
        _log.Info(LogCategory, "the den walker is made; it is placed by the server's teleport", new LogField("run_rate", weenie.TryGetRunRate(out var rate) ? rate : null));
    }

    /// <inheritdoc/>
    public bool BeginLeg(IGameSessionView session, double now, out string description)
    {
        description = string.Empty;
        if (_walker is null || !_placed)
        {
            Failure = "the den walker is not placed: no teleport of the player reached it";
            return false;
        }

        var from = _walker.Position;
        for (var tries = 0; tries < Tour.Count; tries++)
        {
            var cellId = (BlackSpawnDen.Landblock << 16) | Tour[_next++ % Tour.Count];
            if (cellId == from.CellId)
            {
                continue;
            }

            if (_planner.Plan("den-leg-" + (_legs + 1), in from, cellId, StopWithinMetres) is not { } route)
            {
                _log.Warn(LogCategory, "no walk joins the player's cell and a waypoint", new LogField("from", $"0x{from.CellId:X8}"), new LogField("to", $"0x{cellId:X8}"));
                continue;
            }

            _driver?.Abandon();
            _driver = new RouteDriver(_walker.Controller, route);
            _legs++;
            _legStart = session.Clock.Now;
            description = $"leg {_legs} from 0x{from.CellId:X8} to 0x{cellId:X8} ({route.Goals.Count} goals)";
            return true;
        }

        Failure = $"no walk joins the player's cell 0x{from.CellId:X8} and any waypoint of the floor (the player stands off the floor's cells or the floor is cut)";
        return false;
    }

    /// <inheritdoc/>
    public FightWalkStatus Tick(IGameSessionView session, double now)
    {
        if (_walker is null)
        {
            return FightWalkStatus.Idle;
        }

        DrainMoves(now);
        ApplyServerMotions();
        if (_placed)
        {
            _walker.Controller.Tick(now);
            var cell = _walker.Position.CellId;
            if (BlackSpawnDen.LeftFloor(cell))
            {
                // The legs are planned over the floor's cells only, but the physics collides against the whole landblock: a slide or an overshoot near a doorway can leave the floor (the exit portal's doorway 0x035C is next to the arrival). Fail at that moment, not later and indirectly (3.1-R4, D12).
                Failure = $"the walker left the den's floor: it is in cell 0x{cell:X8}, which is in landblock 0x0105 but not a floor cell (TODO(m3-fight-left-floor) if this is a planner or physics gap)";
                _driver?.Abandon();
                _driver = null;
                return FightWalkStatus.Failed;
            }
        }

        if (_driver is not { } driver)
        {
            return FightWalkStatus.Idle;
        }

        var status = driver.Tick(now);
        switch (status)
        {
            case RouteStatus.Done:
                _driver = null;
                return FightWalkStatus.Arrived;
            case RouteStatus.Stuck:
            case RouteStatus.Failed:
                Failure = driver.FailureReason ?? "the leg failed without saying why";
                driver.Abandon();
                _driver = null;
                return FightWalkStatus.Failed;
        }

        if (session.Clock.Now - _legStart > LegSeconds)
        {
            Failure = $"the leg did not finish within {LegSeconds:0.#} s (goal {driver.GoalIndex})";
            driver.Abandon();
            _driver = null;
            return FightWalkStatus.Failed;
        }

        return FightWalkStatus.Walking;
    }

    /// <inheritdoc/>
    public void Abandon() => _driver?.Abandon();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _driver?.Abandon();
        _movesSubscription?.Dispose();
        _motionSubscription?.Dispose();
    }

    /// <summary>Hands the player's position updates to the walker, in order, as <see cref="RoutePlay"/> does.</summary>
    private void DrainMoves(double now)
    {
        if (_moves.Count == 0)
        {
            return;
        }

        foreach (var move in _moves)
        {
            var coord = LandblockCoord.FromId(move.Position.CellId);
            if (!_world.IsAttached(coord))
            {
                continue; // the login's own arrival outside the den
            }

            if (!_placed)
            {
                if (move.Verdict != PositionVerdict.SelfTeleport)
                {
                    continue;
                }

                if (!_walker!.Place(move.Position))
                {
                    _log.Error(LogCategory, "no valid position for the walker where the server put the player", new LogField("cell", $"0x{move.Position.CellId:X8}"));
                    continue;
                }

                _placed = true;
            }

            _walker!.Controller.Apply(move, now);
        }

        _moves.Clear();
    }

    /// <summary>Gives the walker the server's MoveTo and TurnTo of the player (ACE turns the caster to a targeted spell's target that way); the rest of the server's motion is left alone.</summary>
    private void ApplyServerMotions()
    {
        if (_serverMotions.Count == 0)
        {
            return;
        }

        foreach (var e in _serverMotions)
        {
            var type = (byte)e.Motion.MovementType;
            if (type is < 6 or > 9 || !_placed || _walker!.Body is not IPlayerMovementBody body)
            {
                continue;
            }

            var count = AvatarWire.CopyActions(e.Motion, _actions);
            if (count > _actions.Length)
            {
                _actions = new WireMotionItem[count];
                AvatarWire.CopyActions(e.Motion, _actions);
            }

            body.ApplyServerMovement(AvatarWire.ToMovement(e.Motion), _actions.AsSpan(0, count), e.Autonomous, out _);
        }

        _serverMotions.Clear();
    }
}
