using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The fight loop's walk in the game (M3 3.1 fd; <c>TODO(m3-fight-no-walker)</c> closed for the game): an <see cref="IFightWalk"/> over the game's own player. Headless, <see cref="GroundWalk"/> makes a walker of its own
/// in a physics world of its own; in the game the walker belongs to the player controller (it ticks, streams collision, is placed by every teleport and follows the server's moves and MoveTos), so this class only
/// plans the legs on the ground's walkable cells (<see cref="RoomGraph"/> with the ground's lean, <see cref="GroundRooms"/>, built on the leg's loading worker) and drives that controller with a <see cref="NavRunner"/> made as the Godot
/// journey host makes its runner (the player's body, the session's keys, a <see cref="RouteConfirmer"/> on the player's collision, the hostile-creature layer with the dats' own radii), <see cref="RunnerLegs"/>. The movement goes out through the host's sink (the presence gate over the session's movement channel); no audit sits in between (<c>TODO(m3-fight-walk-audit)</c>, as <see cref="GroundWalk"/>).
/// </summary>
/// <remarks>The leg order is the walk's <see cref="IGroundTour"/> (the ground's tour in its order unless the host gave another): the next cell after the one the player stands in that has a walk from it, with the stop distance, dwell and leg limit of the profile's walk section. A failed leg is abandoned and the walk goes on, three in a row ending it (<c>m5-fight-walk-recover</c>, <see cref="RunnerLegs"/>). Engine-agnostic, it moved from the Godot project to the client in M4 P5b J5b so the Godot journey host gives it to the fight and the death (<see cref="Journey.IJourneyWalks.FightWalk"/>) and the tests can drive it over a body that follows the server.</remarks>
public sealed class GameFightWalk : IFightWalk
{
    private const string LogCategory = "fight";

    private readonly RoutePlayHost _host;
    private readonly RoomGraph _planner;
    private readonly HuntingGround _ground;
    private readonly ILogSink _log;
    private readonly Func<uint, float?>? _setupRadius;
    private readonly Func<uint, float?>? _setupExtent;
    private readonly RunnerLegs _legs;
    private NavRunner? _runner;
    private RouteConfirmer? _confirmer;
    private PlateSpellWitness? _plateWitness; // M6 14 holding plates: the check after a jump over plates
    private bool _prepared;
    private bool _disposed;

    /// <summary>A walk over the game's player.</summary>
    /// <param name="host">The player's walker, through the scenario host play mode was given.</param>
    /// <param name="planner">The ground's room graph (built on a worker, <see cref="GroundRooms"/>).</param>
    /// <param name="ground">The ground the walk runs in (its tour, walk section and no-go cells).</param>
    /// <param name="log">Where the walk logs.</param>
    /// <param name="setupRadius">A creature's Setup radius by id (<see cref="HostileCreatureFeed.SetupRadii"/>), so the creature layer's discs use each creature's own radius; null: every disc assumes 1 m, named once (<c>TODO(nav-creature-radius-host)</c>).</param>
    /// <param name="tour">Where each leg's next waypoint comes from; null: the ground's tour (<see cref="GroundTour.For"/>: a <see cref="GroundTour"/> for a dungeon with slots).</param>
    /// <param name="setupExtent">A door Setup's extent by its id (<see cref="DoorCrossings.SetupExtents"/>) for the walk's door feed; null: every door assumes <see cref="DoorCrossings.UnknownRadiusMetres"/>, named once.</param>
    public GameFightWalk(RoutePlayHost host, RoomGraph planner, HuntingGround ground, ILogSink log, Func<uint, float?>? setupRadius = null, IGroundTour? tour = null, Func<uint, float?>? setupExtent = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(log);
        (_host, _planner, _ground, _log, _setupRadius, _setupExtent) = (host, planner, ground, log, setupRadius, setupExtent);
        _legs = new RunnerLegs(ground, tour ?? GroundTour.For(ground, planner, log), planner, log);
    }

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <summary>The legs begun so far, the recovery legs included.</summary>
    public int Legs => _legs.Legs;

    /// <inheritdoc/>
    public IGroundTour Tour => _legs.Tour;

    /// <summary>The failed legs the walk abandoned and went on from (<c>m5-fight-walk-recover</c>).</summary>
    public int RecoveredLegs => _legs.Recovered;

    /// <inheritdoc/>
    int? IFightWalk.LegsRecovered => _legs.Recovered;

    /// <summary>M6 item 0 F2 (a5's note c): the walking seconds of every leg, arrived or cancelled (<c>m6-walk-cancel-seconds</c>), so the play host's fight verdict reads <c>walk_s=</c>.</summary>
    public double? WalkSecondsTotal => _legs.WalkSecondsTotal;

    /// <summary>The runner of the leg walked last, or null before the first leg.</summary>
    public NavRunner? Runner => _runner;

    /// <inheritdoc/>
    public void Prepare(IGameSessionView session, double now)
    {
        if (_prepared)
        {
            return;
        }

        _prepared = true;
        _log.Info(LogCategory, "the game's player is the ground walker (the body the screen draws); the play places it wherever the server has the player, a teleport or none");
    }

    /// <inheritdoc/>
    public bool BeginLeg(IGameSessionView session, double now, out string description)
    {
        description = string.Empty;
        if (Ready(session, out var from) is { } notReady)
        {
            Failure = notReady;
            return false;
        }

        if (_legs.Begin(_runner!, in from, now, session.Clock.Now, out description))
        {
            return true;
        }

        Failure = _legs.Failure;
        return false;
    }

    /// <inheritdoc/>
    public int LegStuckRungs => _legs.StuckRungs;

    /// <inheritdoc/>
    public uint LegGoalCell => _legs.GoalCell;

    /// <inheritdoc/>
    public bool JumpInFlight => _legs.JumpInFlight;

    /// <inheritdoc/>
    public bool BeginLegTo(IGameSessionView session, double now, in CellFrame goal, out string description)
    {
        if (Ready(session, out var from) is { } notReady)
        {
            description = notReady;
            return false;
        }

        return _legs.BeginTo(_runner!, in from, in goal, now, session.Clock.Now, out description);
    }

    /// <inheritdoc/>
    public bool AvoidSpot(in CellFrame at, double until, out string description) => _legs.AvoidSpot(in at, until, out description);

    /// <summary>The player's body is placed off the no-go cells and the runner is made (at the first leg): null, with where the player stands; else why not.</summary>
    private string? Ready(IGameSessionView session, out CellFrame from)
    {
        from = default;
        if (_host.Walker is not { } walker || walker.Physics.PlacementPending || walker.Physics.Cell is null)
        {
            return "the game's player body is not placed where the server put the player";
        }

        from = walker.Position;
        if (_ground.IsNoGo(from.CellId))
        {
            return GroundWalk.NoGoReason(_ground, from.CellId);
        }

        if (_runner is null)
        {
            // The runner is made as the Godot journey host makes its own: the player's body, the session's keys, the room graph as the path source, a confirmer on the player's collision (sliced to the frame's budget) and the creature layer.
            if (walker.Physics.Setup is not { } setup)
            {
                return "the game's player body has no collision Setup";
            }

            _confirmer = new RouteConfirmer(walker.World, setup, _log);
            _runner = new NavRunner(
                session.Actions.Move,
                new ControllerNavBody(walker.Controller),
                new RoomPathSource(_planner),
                _confirmer,
                null,
                new NavRunnerOptions { Name = "ground-walk", ConfirmBudgetMilliseconds = ConfirmBudgetMilliseconds, PredictiveSteering = true, PredictiveOutdoors = true },
                _log,
                new HostileCreatureFeed(session.State.Objects, session.State.Self.Guid, _log, _setupRadius))
            {
                Doors = new SessionDoorFeed(session, _log, _setupExtent), // M5 5 d2: a closed door on a leg is opened (purpose=door), not walked into
                BodyFeed = new NpcBodyFeed(session.State.Objects, session.State.Self.Guid, _log, _setupRadius), // the NPCs' bodies on the plan (M6 4 W2; the fight walks since M6 4 w6a)
            };
            if (_ground.Plates is { } plates)
            {
                _plateWitness = new PlateSpellWitness(session, plates, _log);
                _runner.JumpWitness = _plateWitness;
            }
        }

        return null;
    }

    /// <summary>The milliseconds a frame may spend confirming a route (the Godot journey host's figure).</summary>
    public const double ConfirmBudgetMilliseconds = 2.0;

    /// <inheritdoc/>
    /// <remarks>
    /// ACE aims a bolt from the caster's 2/3 height to the target's 2/3 height (<c>ProjHeight</c>, WorldObject_Magic.cs 1526); the sweep is the camera's viewer sphere (<c>PhysicsWorld.SweepViewer</c>, retail <c>SmartBox::update_viewer</c>).
    /// <c>TODO(m3-fight-los-radius)</c>: the bolt's own sphere is not known, 0.3 m is the viewer's. <c>TODO(m3-fight-los-target-height)</c>: the target's height is not read, 1.8 m stands in.
    /// <c>TODO(m3-fight-los-approximation)</c>: moving objects and ethereal flags are not part of the sweep.
    /// </remarks>
    public bool HasClearLine(in CellFrame from, in CellFrame to) => _host.Walker is not { } walker || ClearLine(walker.World, in from, in to);

    /// <summary>
    /// F3: the clear-line sweep both fight walks share (<see cref="GameFightWalk"/> and the headless <see cref="GroundWalk"/>): true when no wall of <paramref name="world"/> stands between a bolt's start and end heights (the remarks of <see cref="HasClearLine"/>).
    /// </summary>
    /// <param name="world">The physics world holding the ground's collision.</param>
    /// <param name="from">The caster's position (feet).</param>
    /// <param name="to">The target's position (feet).</param>
    public static bool ClearLine(PhysicsWorld world, in CellFrame from, in CellFrame to)
    {
        ArgumentNullException.ThrowIfNull(world);
        var lift = new System.Numerics.Vector3(0f, 0f, StandInBodyHeight * ProjHeight);
        var a = new CellFrame(from.CellId, from.Frame with { Origin = from.Frame.Origin + lift });
        var b = new CellFrame(to.CellId, to.Frame with { Origin = to.Frame.Origin + lift });
        return !world.SweepViewer(in a, in b, LineRadius).Blocked;
    }

    /// <inheritdoc/>
    public LineTrace CellsOnLine(in CellFrame from, in CellFrame to, List<uint> cells)
    {
        if (_host.Walker is not { } walker)
        {
            ArgumentNullException.ThrowIfNull(cells);
            cells.Clear();
            return LineTrace.NotTraced;
        }

        return TraceCells(walker.World, in from, in to, cells);
    }

    /// <inheritdoc/>
    public IReadOnlyList<CellFrame> FloorSpots => _floorSpots ??= RoomSpots(_planner);

    private IReadOnlyList<CellFrame>? _floorSpots;

    /// <summary>
    /// M5 7 P7s (D148, <c>m5-8-loot-no-go-corpse</c>): the cells a straight line from <paramref name="from"/> to <paramref name="to"/> passes through, in order, as a sphere of <see cref="TraceRadius"/> at <see cref="TraceLift"/>
    /// above the feet swept in hops of at most <see cref="TraceHopMetres"/> (<see cref="PhysicsWorld.SweepViewer"/>, whose transition follows the cells through their portals). The line ACE's MoveTo walks: a Use of an object out of
    /// reach turns the player to it and runs straight at it (<c>Player_Use.cs:184-206</c>, <c>Player_Move.cs:37-96</c> @95ef426d), with no path round a cell. <see cref="LineTrace.Stopped"/> when a hop was stopped (the cells past it are
    /// not known) or the two ends lie in different landblocks (<c>TODO(ground-multi-landblock)</c>).
    /// </summary>
    /// <param name="world">The physics world holding the ground's collision.</param>
    /// <param name="from">The start (feet).</param>
    /// <param name="to">The end (feet).</param>
    /// <param name="cells">Filled with the cells crossed, the start's first, each once in a row.</param>
    public static LineTrace TraceCells(PhysicsWorld world, in CellFrame from, in CellFrame to, List<uint> cells)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(cells);
        cells.Clear();
        cells.Add(from.CellId);
        if ((from.CellId >> 16) != (to.CellId >> 16))
        {
            return LineTrace.Stopped;
        }

        var lift = new System.Numerics.Vector3(0f, 0f, TraceLift);
        var start = from.Frame.Origin + lift;
        var end = to.Frame.Origin + lift;
        var length = System.Numerics.Vector3.Distance(start, end);
        var hops = Math.Max(1, (int)Math.Ceiling(length / TraceHopMetres));
        var at = new CellFrame(from.CellId, from.Frame with { Origin = start });
        for (var i = 1; i <= hops; i++)
        {
            var next = new CellFrame(at.CellId, from.Frame with { Origin = System.Numerics.Vector3.Lerp(start, end, (float)i / hops) });
            var swept = world.SweepViewer(in at, in next, TraceRadius);
            if (swept.Blocked)
            {
                return LineTrace.Stopped;
            }

            at = swept.Position;
            if (cells[^1] != at.CellId)
            {
                cells.Add(at.CellId);
            }
        }

        return LineTrace.Traced;
    }

    /// <summary>M5 7 P7s (D148, <c>m5-8-no-go-attacker</c>): the middle of every room of <paramref name="planner"/> (its walkable cells; <see cref="RoomGraph.RoomFrame"/>), the spots a short step may go to. Built once per walk.</summary>
    /// <param name="planner">The ground's room graph.</param>
    public static IReadOnlyList<CellFrame> RoomSpots(RoomGraph planner)
    {
        ArgumentNullException.ThrowIfNull(planner);
        var spots = new List<CellFrame>();
        foreach (var cell in planner.Cells.Cells)
        {
            if (planner.RoomFrame(cell.Id) is { } spot)
            {
                spots.Add(spot);
            }
        }

        return spots;
    }

    /// <summary>The height above the feet the approach is traced at: a body's middle, under every doorway's lintel (Lifestone's own).</summary>
    private const float TraceLift = 1.0f;

    /// <summary>The sphere the approach is traced with: thin, so a door's frame does not stop it where a body would turn through (Lifestone's own).</summary>
    private const float TraceRadius = 0.1f;

    /// <summary>The longest hop of the trace (Lifestone's own): a cell crossed is read at the end of each hop.</summary>
    private const float TraceHopMetres = 0.5f;

    /// <summary>The body height a bolt's start and end heights are taken from (<c>TODO(m3-fight-los-target-height)</c>).</summary>
    private const float StandInBodyHeight = 1.8f;

    /// <summary>ACE's <c>ProjHeight</c>, the share of a body's height a bolt leaves from and aims at.</summary>
    private const float ProjHeight = 2f / 3f;

    /// <summary>The sphere swept along the line (<c>TODO(m3-fight-los-radius)</c>).</summary>
    private const float LineRadius = 0.3f;

    /// <inheritdoc/>
    public FightWalkStatus Tick(IGameSessionView session, double now)
    {
        if (_host.Walker is not { } walker)
        {
            return _legs.Active ? FightWalkStatus.Walking : FightWalkStatus.Idle;
        }

        // 2-R9: the no-go rule is checked every frame the walk ticks, as GroundWalk does, not only while a leg is active (a slide or an overshoot while casting between legs enters a no-go cell too).
        if (_ground.IsNoGo(walker.Position.CellId))
        {
            Failure = GroundWalk.NoGoReason(_ground, walker.Position.CellId);
            _legs.Abandon();
            return FightWalkStatus.Failed;
        }

        if (!_legs.Active)
        {
            return FightWalkStatus.Idle;
        }

        var status = _legs.Step(walker.Position, now, session.Clock.Now);
        if (status == FightWalkStatus.Failed)
        {
            Failure = _legs.Failure;
        }

        return status;
    }

    /// <inheritdoc/>
    /// <remarks>The leg ends inactive: its <see cref="RunnerLegs.Step"/> answers Idle until the next <see cref="BeginLeg"/>, so an abandoned leg is not reported as a failure on the next tick.</remarks>
    public void Abandon() => _legs.Abandon();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Abandon();
        _runner?.Dispose();
        _confirmer?.Dispose();
        _plateWitness?.Dispose();
    }
}
