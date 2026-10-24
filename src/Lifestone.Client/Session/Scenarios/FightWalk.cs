using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
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

    /// <summary>
    /// True when a bolt flying from <paramref name="from"/> (the caster's feet) to <paramref name="to"/> (the target's feet) meets no wall (M3 4.2 P4: ACE and retail check no line, so a cast through rock explodes on the wall).
    /// The default is true (a walker without physics, the tests' scripted ones, keeps the nearest pick).
    /// </summary>
    /// <param name="from">The caster's position.</param>
    /// <param name="to">The target's position.</param>
    bool HasClearLine(in CellFrame from, in CellFrame to) => true;
}

/// <summary>
/// The Black Spawn Den's rooms for the fight walk: the floor's cells (<see cref="FloorOf"/>) as a <see cref="RoomGraph"/> whose lean is
/// <see cref="MaxLean"/>. The den's plazas are octagons whose doorways lean 11 degrees (the portal plane's normal has a vertical part of
/// 0.196), which the upright rule (normal Z under 0.1) refuses, so a graph with no lean finds no walk from the arrival to any spawn cell
/// but one. The den's own planner (<c>DenPlanner</c>) is gone: P5a moved its lean into <see cref="CellRoutePlanner"/> and its search into
/// <see cref="RoomGraph"/> (closes <c>m3-fight-planner-lean</c>).
/// </summary>
public static class DenRooms
{
    /// <summary>The largest vertical part of a doorway's normal accepted in the den (the plazas lean 0.196; floors and ceilings are 1).</summary>
    public const float MaxLean = 0.3f;

    /// <summary>The den's floor out of a loaded landblock: the cells <see cref="BlackSpawnDen.IsFloorCell"/> accepts.</summary>
    /// <param name="all">Every cell of landblock 0x0105.</param>
    public static LandblockCellSet FloorOf(LandblockCellSet all)
    {
        ArgumentNullException.ThrowIfNull(all);
        return LandblockCellSet.FromCells(all.Structures, all.Coord, all.Cells.Where(c => BlackSpawnDen.IsFloorCell(c.Id)));
    }

    /// <summary>The den's room graph over a loaded landblock: the floor's cells with <see cref="MaxLean"/>.</summary>
    /// <param name="all">Every cell of landblock 0x0105.</param>
    public static RoomGraph GraphOf(LandblockCellSet all) => new(FloorOf(all), MaxLean);
}

/// <summary>
/// The real <see cref="IFightWalk"/>: an <see cref="OfflineWalker"/> in the den's collision (landblock 0x0105 alone), placed where the server puts the player and given the server's
/// positions and moves as <see cref="RoutePlay"/> gives them to its walker (the same session wiring: the presence-gated movement sink, the player's own ObjectMoved and ObjectMotionChanged,
/// <see cref="SessionTargetWorld"/> for the TurnToObject ACE sends when it turns the caster), and a <see cref="RouteDriver"/> for each leg of the <see cref="RoomGraph"/>'s route.
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

    /// <summary>How long the walker stands at the end of a leg, in seconds (a short rest; the loop then casts).</summary>
    public const double StopDwellSeconds = 0.25;

    /// <summary>The longest a leg may take, in seconds on the session clock.</summary>
    public const double LegSeconds = 90;

    private const string LogCategory = "fight";

    private readonly DatSet _dats;
    private readonly PhysicsGeometryLibrary _geometry;
    private readonly PhysicsWorld _world;
    private readonly RoomGraph _planner;
    private readonly ILogSink _log;
    private readonly List<ObjectMoved> _moves = [];
    private readonly List<ObjectMotionChanged> _serverMotions = [];
    private WireMotionItem[] _actions = new WireMotionItem[16];
    private IDisposable? _movesSubscription;
    private IDisposable? _motionSubscription;
    private OfflineWalker? _walker;
    private MoveIntents? _move;
    private PresenceGatedMovementSink? _gate;
    private SessionTargetWorld? _targets;
    private RouteDriver? _driver;
    private bool _placed;
    private bool _disposed;
    private int _next;
    private int _legs;
    private double _legStart;

    private DenWalk(DatSet dats, PhysicsGeometryLibrary geometry, PhysicsWorld world, RoomGraph planner, ILogSink log)
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
    public RoomGraph Planner => _planner;

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
        return new DenWalk(dats, geometry, world, DenRooms.GraphOf(LandblockCellSet.Load(new CellStructureLibrary(dats), coord)), log);
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
        _move = session.Actions.Move; // scripted movement goes through IActionApi.Move (the human's keys, R2)
        _move.Bind(_walker.Controller, _walker.Controller.Options.RunByDefault);
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

            if (_planner.PlanScripted("den-leg-" + (_legs + 1), in from, cellId, StopWithinMetres, StopDwellSeconds) is not { } route)
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
    /// <remarks>The driver is dropped too: <see cref="RouteDriver.Abandon"/> marks a running route Failed and its <c>Tick</c> keeps answering that, so a walk that kept it would report an abandoned leg as a failure on its next tick.</remarks>
    public void Abandon()
    {
        _driver?.Abandon();
        _driver = null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Abandon();
        if (_move is not null && _walker is not null)
        {
            _move.Unbind(_walker.Controller);
        }

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
