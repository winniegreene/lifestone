using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>Where a <see cref="NavRunner"/> stands.</summary>
public enum NavRunState
{
    /// <summary>Not started.</summary>
    Idle,

    /// <summary>Planning, confirming or running the route (the stuck ladder included).</summary>
    Running,

    /// <summary>Paused by its caller (<see cref="NavRunner.Pause"/>): keys released, the route kept.</summary>
    Waiting,

    /// <summary>The server drives the player (ACE's MoveTo, <see cref="INavBody.ServerDriving"/>): keys released, nothing pressed until it ends.</summary>
    Yielding,

    /// <summary>The goal reached (a point within its tolerance; an object within range and in sight, or no sight asked).</summary>
    Arrived,

    /// <summary>An object goal within range whose sight line every try found blocked.</summary>
    ArrivedWithoutSight,

    /// <summary>The run cannot go on: no path, the ladder's last plan, a route the physics refused, or an intent not taken (<see cref="NavRunner.FailureReason"/>).</summary>
    Blocked,

    /// <summary>A person pressed a movement key (R6: the human's key wins).</summary>
    Interrupted,

    /// <summary>The player was moved more than <see cref="NavRunner.TeleportMetres"/> in one step (a teleport or a server correction).</summary>
    Failed,
}

/// <summary>The runner's tuning; the defaults are the design's (P5a 3.2). Not retail: Lifestone's own.</summary>
public sealed record NavRunnerOptions
{
    /// <summary>The defaults.</summary>
    public static NavRunnerOptions Default { get; } = new();

    /// <summary>The confirmation budget per <see cref="NavRunner.Step"/> in milliseconds: unbounded headless, about 2 ms in the Godot host.</summary>
    public double ConfirmBudgetMilliseconds { get; init; } = double.PositiveInfinity;

    /// <summary>True to stop at a point goal (within its stop tolerance, keys released); false to pass it (within its pass tolerance, keys kept).</summary>
    public bool Stop { get; init; } = true;

    /// <summary>The name the runner's log lines and failure reasons carry.</summary>
    public string Name { get; init; } = "run";
}

/// <summary>
/// The wayfinding runner (P5a design 3.2, docs/wayfinding.md): runs a planned path to a goal the way a player at the keyboard would,
/// acting only through <see cref="IActionApi.Move"/> (<see cref="MoveIntents"/>: R2) and reading only <see cref="INavBody"/>. A stepped
/// API: call <see cref="Step"/> once per frame after the physics step; nothing blocks.
/// </summary>
/// <remarks>
/// <para><b>Steering.</b> Pure pursuit: the lead point lies max(<see cref="LeadMinMetres"/>, <see cref="LeadSpeedFactor"/> x speed) of arc
/// length ahead of the body's nearest point on the route, never past the next doorway vertex, and, outdoors, clamped to the farthest route
/// vertex the lattice sees (<see cref="NavMap.LineOfSight"/>). Forward is held up to <see cref="TurnInPlaceDegrees"/> of heading error, else
/// the body turns in place first; it walks within <see cref="WalkNearStopMetres"/> of a stop and <see cref="WalkNearDoorwayMetres"/> of a
/// doorway vertex (the thin-doorway rule, <c>physics-thin-doorway</c>) and runs elsewhere (<see cref="MoveIntents.SetRun"/>). A key is
/// sent only when it changes: every movement key change is one MoveToState (Lifestone's own budget is 20 a second, <see cref="MaxKeysPerSecond"/>; ACE @95ef426d has no such limit on MoveToState, it only caps the position broadcasts they cause to one a second, <c>Player_Tick.cs</c> 394-403 <c>MoveToState_UpdatePosition_Threshold</c>).</para>
/// <para><b>Plans.</b> Off the route by <see cref="OffRouteMetres"/>: a replan. A horizon route (<see cref="NavPathStatus.Horizon"/>):
/// once <see cref="NavMap.Version"/> has changed and the body is within <see cref="HorizonReplanMetres"/> of its end, a plan to the real
/// goal (handoff-s2's contract); at its end with no new tiles the body stands. With a <see cref="RouteConfirmer"/>, every path is confirmed
/// by the physics before it is run; a refused sample scars its node and asks for a replan.</para>
/// <para><b>Stuck and the ladder.</b> Progress is arc length along the route. Under <see cref="StuckProgressMetres"/> in
/// <see cref="StuckSeconds"/> with forward held and on the ground is one rung: (1) back off <see cref="BackOffMetres"/> and re-aim;
/// (2) sidestep left <see cref="SidestepLeftMetres"/>, then right <see cref="SidestepRightMetres"/>; (3) a charged jump forward (the
/// JumpCharge path: <see cref="MoveIntents.ChargeJump"/>, then <see cref="MoveIntents.Jump"/> after <see cref="JumpCharge.FillSeconds"/>);
/// (4) scar the stuck node and a metre round it and replan; the fourth rung of the <see cref="MaxStuckPlans"/>th plan is
/// <see cref="NavRunState.Blocked"/>, naming the nearest static. Progress of <see cref="LadderResetMetres"/> past where the ladder began
/// ends it.</para>
/// <para><b>Ends.</b> An intent completing DryRun, Refused, Cancelled (the kill switch) or NotImplemented ends the run Blocked naming the
/// outcome. A person's movement key (<see cref="MoveIntents.HumanKey"/>) ends it Interrupted. A jump of over <see cref="TeleportMetres"/>
/// in one step is Failed, as <see cref="RouteDriver"/>'s.</para>
/// <para>Not retail: every constant here is Lifestone's own. Game thread only.</para>
/// </remarks>
public sealed class NavRunner : IDisposable
{
    /// <summary>The shortest lead distance, in metres. Not retail: Lifestone's own.</summary>
    public const float LeadMinMetres = 2f;

    /// <summary>The lead distance per metre a second of speed. Not retail: Lifestone's own.</summary>
    public const float LeadSpeedFactor = 0.6f;

    /// <summary>The heading error within which no turn key is held, in degrees (<see cref="RouteDriver"/>'s 5). Not retail: Lifestone's own.</summary>
    public const double HeadingToleranceDegrees = 5.0;

    /// <summary>
    /// A turn key once held is let go only within this heading error (or when the error changes side), in degrees: the hysteresis that
    /// keeps the turn key from flickering round <see cref="HeadingToleranceDegrees"/> (each flick is a MoveToState). Not retail: Lifestone's own.
    /// </summary>
    public const double TurnReleaseDegrees = 1.5;

    /// <summary>
    /// The least time between two changes of the turn key, in seconds (unless the error is past <see cref="TurnInPlaceDegrees"/>): round a
    /// corner the lead point moves on as fast as the body turns, and a key pressed and let go every frame would be 30 MoveToStates a
    /// second against Lifestone's own budget of 20 (ACE @95ef426d sets no limit: <c>Player_Tick.cs</c> 394-403 caps only the position broadcasts). Not retail: Lifestone's own.
    /// </summary>
    public const double TurnKeyMinSeconds = 0.2;

    /// <summary>The heading error beyond which forward is let go and the body turns in place, in degrees. Not retail: Lifestone's own.</summary>
    public const double TurnInPlaceDegrees = 60.0;

    /// <summary>An indoor point goal is reached only within this height of it too (a room above or below is not the room), in metres. Not retail: Lifestone's own.</summary>
    public const float ArrivalHeightMetres = 1.5f;

    /// <summary>Within this of a stop the runner walks. Not retail: Lifestone's own.</summary>
    public const float WalkNearStopMetres = 2f;

    /// <summary>Within this of a doorway vertex the runner walks (the thin-doorway rule). Not retail: Lifestone's own.</summary>
    public const float WalkNearDoorwayMetres = 3f;

    /// <summary>Off the route by more than this is a replan (<see cref="PathPlanner.OffRouteMetres"/>).</summary>
    public const float OffRouteMetres = PathPlanner.OffRouteMetres;

    /// <summary>Within this of a horizon route's end, a plan to the real goal once the map changed (<see cref="PathPlanner.HorizonReplanMetres"/>).</summary>
    public const float HorizonReplanMetres = PathPlanner.HorizonReplanMetres;

    /// <summary>The least arc-length progress that counts, in metres. Not retail: Lifestone's own.</summary>
    public const float StuckProgressMetres = 0.25f;

    /// <summary>
    /// How long the body may stand at the end of a horizon route waiting for the next tiles to attach, in seconds; past it the run ends Blocked
    /// (<c>why=horizon:</c> naming the landblocks). Tiles build in 15-40 ms on a worker, so this is generous. Not retail: Lifestone's own.
    /// </summary>
    public const double HorizonWaitSeconds = 30.0;

    /// <summary>Without progress for this long with forward held (the clock starts when forward is pressed, so a turn in place before it is not stuck) on the ground is a rung. Not retail: Lifestone's own.</summary>
    public const double StuckSeconds = 1.5;

    /// <summary>Without progress for this long whatever the keys is a rung too (a body that turns and turns). Not retail: Lifestone's own.</summary>
    public const double NoProgressSeconds = 6.0;

    /// <summary>Rung 1's back-off, in metres. Not retail: Lifestone's own.</summary>
    public const float BackOffMetres = 1f;

    /// <summary>Rung 2's sidestep left, in metres. Not retail: Lifestone's own.</summary>
    public const float SidestepLeftMetres = 1f;

    /// <summary>Rung 2's sidestep right (from the left one's end), in metres. Not retail: Lifestone's own.</summary>
    public const float SidestepRightMetres = 2f;

    /// <summary>The longest a rung's move is held when the body does not get there, in seconds. Not retail: Lifestone's own.</summary>
    public const double RungMoveSeconds = 3.0;

    /// <summary>The scar round the stuck node, in metres (the node and a metre round it). Not retail: Lifestone's own.</summary>
    public const float StuckScarMetres = 1.5f;

    /// <summary>Plans in one stuck episode before its fourth rung is Blocked. Not retail: Lifestone's own.</summary>
    public const int MaxStuckPlans = 3;

    /// <summary>Progress past where the ladder began that ends it, in metres. Not retail: Lifestone's own.</summary>
    public const float LadderResetMetres = 2f;

    /// <summary>Refused confirmations before the run is Blocked. Not retail: Lifestone's own.</summary>
    public const int MaxConfirmFailures = 3;

    /// <summary>A jump of the body in one step longer than this is Failed (<see cref="RouteDriver.TeleportMetres"/>).</summary>
    public const float TeleportMetres = RouteDriver.TeleportMetres;

    /// <summary>The speed estimate's smoothing per step. Not retail: Lifestone's own.</summary>
    private const float SpeedSmoothing = 0.2f;

    private const string LogCategory = "wayfinding";

    private readonly MoveIntents _move;
    private readonly INavBody _body;
    private readonly INavPathSource _paths;
    private readonly RouteConfirmer? _confirmer;
    private readonly MovementAudit? _audit;
    private readonly ILogSink _log;
    private readonly List<ActionHandle> _inFlight = [];
    private readonly Queue<double> _keyTimes = new();

    private NavGoal? _goal;
    private NavPath? _path;
    private NavRoute? _route;
    private int _mapVersion;
    private bool _confirming;
    private int _confirmFailures;
    private double _startTime;
    private double _endTime = double.NaN;
    private double _lastTime;
    private Vector2 _lastGlobal;
    private float _lastZ;
    private bool _started;
    private float _speed;

    private float _bestProgress;
    private double _lastProgressTime;
    private bool _ladderActive;
    private int _ladderLevel;
    private int _stuckPlans;
    private float _ladderOrigin;
    private Rung _rung;
    private double _rungStart;
    private Vector2 _rungFrom;
    private bool _rungLeftGround;

    private bool _forward;
    private double _forwardSince;
    private bool _back;
    private int _turn;
    private double _turnChangedAt = double.NegativeInfinity;
    private int _side;
    private bool _runKey;
    private bool _awaitingTiles;
    private double _awaitingSince;

    /// <summary>A runner moving the player bound to <paramref name="move"/>, reading <paramref name="body"/>, planning with <paramref name="paths"/>.</summary>
    /// <param name="move">The session's <see cref="IActionApi.Move"/> (bound to the player's controller).</param>
    /// <param name="body">The player's body (<see cref="ControllerNavBody"/>).</param>
    /// <param name="paths">Where paths come from.</param>
    /// <param name="confirmer">The physics confirmation, or null to run unconfirmed paths (the scripted-body tests).</param>
    /// <param name="audit">The walk's audit, or null.</param>
    /// <param name="options">The tuning, or null for the defaults.</param>
    /// <param name="log">The log, or null.</param>
    public NavRunner(MoveIntents move, INavBody body, INavPathSource paths, RouteConfirmer? confirmer = null, MovementAudit? audit = null, NavRunnerOptions? options = null, ILogSink? log = null)
    {
        ArgumentNullException.ThrowIfNull(move);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(paths);
        _move = move;
        _body = body;
        _paths = paths;
        _confirmer = confirmer;
        _audit = audit;
        Options = options ?? NavRunnerOptions.Default;
        _log = log ?? NullLogSink.Instance;
        _move.HumanKey += OnHumanKey;
    }

    private enum Rung
    {
        None,
        BackOff,
        SidestepLeft,
        SidestepRight,
        JumpCharge,
        JumpAir,
    }

    /// <summary>The tuning.</summary>
    public NavRunnerOptions Options { get; }

    /// <summary>Where the run stands.</summary>
    public NavRunState State { get; private set; } = NavRunState.Idle;

    /// <summary>Why the run ended Blocked, Interrupted, Failed or ArrivedWithoutSight (shaped like <see cref="RouteDriver.FailureReason"/>); else null.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>The goal.</summary>
    public NavGoal? Goal => _goal;

    /// <summary>The path being run (null before the first plan).</summary>
    public NavPath? Path => _path;

    /// <summary>The scars of this run (the planner's keep-out overlay; 30 s each).</summary>
    public NavScars Scars { get; } = new();

    /// <summary>Plans made (the first and every replan).</summary>
    public int Plans { get; private set; }

    /// <summary>Replans made.</summary>
    public int Replans { get; private set; }

    /// <summary>Rungs of the ladder climbed.</summary>
    public int Rungs { get; private set; }

    /// <summary>Times the run was stuck (each is a rung).</summary>
    public int Stuck => Rungs;

    /// <summary>Metres the body moved horizontally while the run was live.</summary>
    public float RouteMetres { get; private set; }

    /// <summary>Seconds from the start to the end (or to the last step).</summary>
    public double RunSeconds => (double.IsNaN(_endTime) ? _lastTime : _endTime) - _startTime;

    /// <summary>Movement key changes sent (each a MoveToState for a command; the run key sends none).</summary>
    public int KeyChanges { get; private set; }

    /// <summary>The most key changes in any one second.</summary>
    public int MaxKeysPerSecond { get; private set; }

    /// <summary>Paths the confirmer refused.</summary>
    public int ConfirmFailures => _confirmFailures;

    /// <summary>The last refused confirmation, or null.</summary>
    public RouteConfirmFailure? LastConfirmFailure { get; private set; }

    /// <summary>True once the run has ended (any state but Idle, Running, Waiting and Yielding).</summary>
    public bool IsDone => State is not (NavRunState.Idle or NavRunState.Running or NavRunState.Waiting or NavRunState.Yielding);

    /// <summary>Starts a run to <paramref name="goal"/> at <paramref name="now"/>; the first <see cref="Step"/> plans.</summary>
    public void Start(NavGoal goal, double now)
    {
        ArgumentNullException.ThrowIfNull(goal);
        _goal = goal;
        _path = null;
        _route = null;
        _confirming = false;
        _confirmFailures = 0;
        _startTime = now;
        _lastTime = now;
        _endTime = double.NaN;
        _started = false;
        _ladderActive = false;
        _ladderLevel = 0;
        _rung = Rung.None;
        FailureReason = null;
        State = NavRunState.Running;
        _log.Info(LogCategory, "run started", new LogField("name", Options.Name), new LogField("goal", Describe(goal)));
    }

    /// <summary>Pauses the run (a station's wait): every key released, the route kept.</summary>
    public void Pause()
    {
        if (State is NavRunState.Running or NavRunState.Yielding)
        {
            ReleaseAll();
            _rung = Rung.None;
            State = NavRunState.Waiting;
        }
    }

    /// <summary>Resumes a paused run; the next step replans when the body is off the route by <see cref="OffRouteMetres"/>.</summary>
    public void Resume(double now)
    {
        if (State == NavRunState.Waiting)
        {
            State = NavRunState.Running;
            _lastProgressTime = now;
            _started = false; // re-anchor: a station may have moved the player (a recall) without a Step in between; the off-route check replans
            _awaitingTiles = false;
        }
    }

    /// <summary>One frame: reads the body, sends the keys that change. Call after the physics step.</summary>
    /// <param name="now">The body's clock in seconds (<see cref="INavBody.Now"/>, or the host's monotonic clock).</param>
    public NavRunState Step(double now)
    {
        if (State is NavRunState.Idle || IsDone)
        {
            return State;
        }

        var position = _body.Position;
        var here = NavMap.FromCellFrame(position);
        var global = here.Global;
        if (!_started)
        {
            _started = true;
            _lastGlobal = global;
            _lastZ = here.Z;
            _lastTime = now;
        }

        var moved = Vector2.Distance(global, _lastGlobal);
        var jump = MathF.Sqrt((moved * moved) + ((here.Z - _lastZ) * (here.Z - _lastZ)));
        if (jump > TeleportMetres && State != NavRunState.Waiting) // a paused run's station may move the player (a recall); Resume replans
        {
            return End(NavRunState.Failed, string.Create(CultureInfo.InvariantCulture, $"the player was moved {jump:F1} m in one step (a teleport or a server correction)"), now);
        }

        var dt = now - _lastTime;
        if (dt > 0)
        {
            _speed += SpeedSmoothing * ((float)(moved / dt) - _speed);
        }

        if (State is NavRunState.Running or NavRunState.Yielding)
        {
            RouteMetres += moved;
        }

        _lastGlobal = global;
        _lastZ = here.Z;
        _lastTime = now;

        if (CheckIntents(now) is { } ended)
        {
            return ended;
        }

        if (State == NavRunState.Waiting)
        {
            return State;
        }

        if (_body.ServerDriving)
        {
            if (State != NavRunState.Yielding)
            {
                ReleaseAll();
                _rung = Rung.None;
                State = NavRunState.Yielding;
                _log.Info(LogCategory, "yielding to the server's MoveTo", new LogField("name", Options.Name));
            }

            return State;
        }

        if (State == NavRunState.Yielding)
        {
            State = NavRunState.Running;
            _lastProgressTime = now;
        }

        RunStep(now, position, here);
        return CheckIntents(now) ?? State;
    }

    /// <summary>
    /// The runner's verdict fields (report-only in their first gate): <c>route_m= run_s= speed_mps= plans= replans= rungs= blocked=
    /// corrections= where_max_m=</c>; corrections and where_max_m from the audit, <c>none</c> without one (or without a where check).
    /// </summary>
    public string VerdictFields()
    {
        var seconds = RunSeconds;
        var summary = _audit?.Summary;
        var corrections = summary is null ? "none" : summary.Corrections.ToString(CultureInfo.InvariantCulture);
        var where = summary?.WhereMaxMetres is { } w ? w.ToString("0.00", CultureInfo.InvariantCulture) : "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"route_m={RouteMetres:0.0} run_s={seconds:0.0} speed_mps={(seconds > 0 ? RouteMetres / seconds : 0):0.00} plans={Plans} replans={Replans} rungs={Rungs} blocked={(State == NavRunState.Blocked ? 1 : 0)} corrections={corrections} where_max_m={where}");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _move.HumanKey -= OnHumanKey;
        ReleaseAll(); // every state: an Interrupted or Arrived (no Stop) run still holds what it pressed, and the controller's lists are stacks
    }

    private static string Describe(NavGoal goal) => goal.IsObject
        ? string.Create(CultureInfo.InvariantCulture, $"object 0x{goal.Guid:X8} within {goal.Range:F1} m of {goal.Point}{(goal.NeedSight ? " in sight" : string.Empty)}")
        : string.Create(CultureInfo.InvariantCulture, $"{goal.Point}{(goal.CellId is { } c ? $" in cell 0x{c:X8}" : string.Empty)}");

    private static double Normalize(double degrees)
    {
        degrees %= 360.0;
        return degrees > 180.0 ? degrees - 360.0 : degrees <= -180.0 ? degrees + 360.0 : degrees;
    }

    private void RunStep(double now, in CellFrame position, NavPoint here)
    {
        var goal = _goal!;

        // Arrival comes first: anywhere along the route.
        if (CheckArrival(position, here, now))
        {
            return;
        }

        if (_rung != Rung.None)
        {
            StepRung(now, here.Global);
            return;
        }

        if (_route is null && !Plan(now, position, replan: false))
        {
            return;
        }

        if (_confirming && !StepConfirm(now, position))
        {
            return;
        }

        var route = _route!;
        if (route.DistanceFrom(here) > OffRouteMetres)
        {
            if (Plan(now, position, replan: true) && (!_confirming || StepConfirm(now, position)))
            {
                route = _route!;
            }
            else
            {
                return;
            }
        }

        var s = route.NearestArcLength(here);
        var remaining = route.ArcLength - s;
        if (_path!.Status == NavPathStatus.Horizon && _paths.Map is { } map)
        {
            if (map.Version != _mapVersion && remaining <= HorizonReplanMetres)
            {
                _awaitingTiles = false;
                if (!Plan(now, position, replan: true) || (_confirming && !StepConfirm(now, position)))
                {
                    return;
                }

                route = _route!;
                s = route.NearestArcLength(here);
                remaining = route.ArcLength - s;
            }
            else if (remaining < goal.StopTolerance + 0.5f)
            {
                if (!_awaitingTiles)
                {
                    _awaitingTiles = true;
                    _awaitingSince = now;
                    _log.Info(LogCategory, "at the horizon: waiting for the next tiles", new LogField("name", Options.Name));
                }

                if (now - _awaitingSince >= HorizonWaitSeconds)
                {
                    var end = route.End;
                    End(
                        NavRunState.Blocked,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"horizon:landblock 0x{end.LandblockId:X8} (goal 0x{goal.Point.LandblockId:X8}) not attached within {HorizonWaitSeconds:F0} s ({map.AttachedLandblocks.Count} attached)"),
                        now);
                    return;
                }

                ReleaseAll();
                _lastProgressTime = now;
                return;
            }
        }

        var progress = remaining > WalkNearStopMetres ? s : route.ArcLength - here.HorizontalDistance(route.End);
        if (progress > _bestProgress + StuckProgressMetres)
        {
            _bestProgress = progress;
            _lastProgressTime = now;
            if (_ladderActive && progress > _ladderOrigin + LadderResetMetres)
            {
                _ladderActive = false;
                _ladderLevel = 0;
            }
        }
        else if ((_forward && _body.OnGround && now - Math.Max(_lastProgressTime, _forwardSince) >= StuckSeconds) || now - _lastProgressTime >= NoProgressSeconds)
        {
            ClimbRung(now, position, here, progress);
            return;
        }

        Steer(route, here, position, s);
    }

    private bool CheckArrival(in CellFrame position, NavPoint here, double now)
    {
        var goal = _goal!;
        var distance = here.HorizontalDistance(goal.Point);
        if (goal.IsObject)
        {
            if (distance > goal.Range)
            {
                return false;
            }

            ReleaseAll();
            if (!goal.NeedSight)
            {
                End(NavRunState.Arrived, null, now);
                return true;
            }

            if (_confirmer is null)
            {
                End(NavRunState.ArrivedWithoutSight, "TODO(nav-sight-no-physics): no physics world to test the sight line (no RouteConfirmer given)", now);
                return true;
            }

            var target = goal.CellId is { } cell
                ? new CellFrame(cell, new AcFrame(goal.Point.Local, Quaternion.Identity))
                : NavMap.ToCellFrame(goal.Point);
            if (_confirmer.InSight(position, target, out var tries))
            {
                End(NavRunState.Arrived, null, now);
            }
            else
            {
                End(NavRunState.ArrivedWithoutSight, string.Create(CultureInfo.InvariantCulture, $"within {distance:F1} m of 0x{goal.Guid:X8} but every sight line ({tries}) is blocked"), now);
            }

            return true;
        }

        var tolerance = Options.Stop ? goal.StopTolerance : goal.PassTolerance;
        if (distance > tolerance || (goal.CellId is { } indoor && !LandDefs.IsOutdoorCell(indoor) && MathF.Abs(here.Z - goal.Point.Z) > ArrivalHeightMetres))
        {
            return false;
        }

        if (Options.Stop)
        {
            ReleaseAll();
        }

        End(NavRunState.Arrived, null, now);
        return true;
    }

    private bool Plan(double now, in CellFrame position, bool replan)
    {
        var path = _paths.Plan(position, _goal!, Scars);
        Plans++;
        if (replan)
        {
            Replans++;
        }

        _audit?.RecordPlan(replan);
        _mapVersion = _paths.Map?.Version ?? 0;
        if (path.Status == NavPathStatus.Failed || path.Waypoints.Count == 0)
        {
            End(NavRunState.Blocked, "no path: " + (path.Reason ?? "empty"), now);
            return false;
        }

        _path = path;
        _route = path.ToRoute();
        _bestProgress = float.MinValue;
        _lastProgressTime = now;
        _awaitingTiles = false;
        if (_ladderActive)
        {
            _ladderOrigin = 0f;
        }

        _log.Info(
            LogCategory,
            replan ? "replanned" : "planned",
            new LogField("name", Options.Name),
            new LogField("status", path.Status.ToString()),
            new LogField("metres", _route.ArcLength),
            new LogField("waypoints", path.Waypoints.Count));
        if (_confirmer is not null)
        {
            var goal = _goal!;
            _confirmer.Begin(path.Waypoints, path.Status == NavPathStatus.Horizon || goal.IsObject ? 0f : Options.Stop ? goal.StopTolerance : goal.PassTolerance);
            _confirming = true;
        }

        return true;
    }

    /// <summary>Confirms on within the budget; true once the path is confirmed (and the run may steer this step).</summary>
    private bool StepConfirm(double now, in CellFrame position)
    {
        var confirmer = _confirmer!;
        var status = confirmer.Continue(Options.ConfirmBudgetMilliseconds);
        if (status == RouteConfirmStatus.Pending)
        {
            ReleaseAll(); // the body stands while the rest of the path is confirmed (the Godot host's slices)
            _lastProgressTime = now;
            return false;
        }

        _confirming = false;
        if (status == RouteConfirmStatus.Confirmed)
        {
            _lastProgressTime = now;
            return true;
        }

        var failure = confirmer.Failure!.Value;
        LastConfirmFailure = failure;
        _confirmFailures++;
        _log.Warn(LogCategory, "the physics refused the path", new LogField("name", Options.Name), new LogField("failure", failure.ToString()));
        if (AvoidCellOf(failure) is { } cell && _paths is INavCellAvoider avoider && avoider.Avoid(cell))
        {
            // Indoors the scar is the cell: the physics refused the way into it, the room graph goes round it.
            _log.Warn(LogCategory, "TODO(room-steps): the room graph leaves out a cell the physics refused (the graph knows no steps)", new LogField("name", Options.Name), new LogField("cell", cell));
        }
        else if (_paths.Map is not null && LandDefs.IsOutdoorCell(failure.At.CellId))
        {
            Scars.Add(NavMap.FromCellFrame(failure.At), StuckScarMetres);
        }

        if (_confirmFailures >= MaxConfirmFailures)
        {
            End(NavRunState.Blocked, "the physics refused the path " + _confirmFailures + " times; last: " + failure, now);
            return false;
        }

        return Plan(now, position, replan: true) && StepConfirm(now, position);
    }

    /// <summary>
    /// The indoor cell a refused sample was on its way into: the first waypoint's cell after the failed segment's start that differs from
    /// it (an EnvCell, not the goal's); null when there is none (the failure was in the goal's own cell, or outdoors).
    /// </summary>
    private uint? AvoidCellOf(RouteConfirmFailure failure)
    {
        var waypoints = _path!.Waypoints;
        if (failure.Segment >= waypoints.Count)
        {
            return null;
        }

        var from = waypoints[failure.Segment].At.CellId;
        for (var i = failure.Segment + 1; i < waypoints.Count; i++)
        {
            var cell = waypoints[i].At.CellId;
            if (cell != from)
            {
                return !LandDefs.IsOutdoorCell(cell) && cell != _goal!.CellId ? cell : null;
            }
        }

        return null;
    }

    private void Steer(NavRoute route, NavPoint here, in CellFrame position, float s)
    {
        var goal = _goal!;
        var lead = MathF.Max(LeadMinMetres, LeadSpeedFactor * _speed);
        var target = MathF.Min(s + lead, route.ArcLength);
        var waypoints = _path!.Waypoints;
        var next = 1;
        while (next < waypoints.Count - 1 && route.ArcLengthAt(next) <= s + 0.01f)
        {
            next++;
        }

        // Never past the next doorway vertex.
        for (var i = next; i < waypoints.Count; i++)
        {
            if (route.ArcLengthAt(i) > target)
            {
                break;
            }

            if (waypoints[i].Doorway)
            {
                target = route.ArcLengthAt(i);
                break;
            }
        }

        // Outdoors, the farthest vertex the lattice sees.
        var aim = route.PointAt(target);
        if (_paths.Map is { } map && LandDefs.IsOutdoorCell(position.CellId) && !map.LineOfSight(here, aim))
        {
            var seen = next;
            for (var i = next; i < waypoints.Count && route.ArcLengthAt(i) <= target; i++)
            {
                if (!waypoints[i].Indoor && map.LineOfSight(here, route.Vertices[i]))
                {
                    seen = i;
                }
            }

            aim = route.Vertices[Math.Min(seen, waypoints.Count - 1)];
        }

        var offset = aim.Global - here.Global;
        var bearing = (Math.Atan2(offset.X, offset.Y) * (180.0 / Math.PI) + 360.0) % 360.0;
        var error = Normalize(bearing - RetailFrames.GetHeading(position.Frame));
        var magnitude = Math.Abs(error);

        var nearDoorway = false;
        for (var i = Math.Max(0, next - 1); i < waypoints.Count; i++)
        {
            if (waypoints[i].Doorway && here.HorizontalDistance(route.Vertices[i]) <= WalkNearDoorwayMetres)
            {
                nearDoorway = true;
                break;
            }

            if (route.ArcLengthAt(i) > s + WalkNearDoorwayMetres)
            {
                break;
            }
        }

        var nearStop = Options.Stop && here.HorizontalDistance(goal.Point) <= WalkNearStopMetres;
        var side = error > 0 ? 1 : -1;
        var turn = _turn != 0 && side == _turn && magnitude > TurnReleaseDegrees ? _turn : magnitude <= HeadingToleranceDegrees ? 0 : side;
        if (turn != _turn && (_lastTime - _turnChangedAt >= TurnKeyMinSeconds || magnitude > TurnInPlaceDegrees))
        {
            _turnChangedAt = _lastTime;
            SetTurn(turn);
        }

        SetBack(false);
        SetSide(0);
        SetRun(!nearStop && !nearDoorway);
        SetForward(magnitude <= TurnInPlaceDegrees);
    }

    private void ClimbRung(double now, in CellFrame position, NavPoint here, float progress)
    {
        if (!_ladderActive)
        {
            _ladderActive = true;
            _ladderLevel = 0;
            _stuckPlans = 1;
            _ladderOrigin = progress;
        }

        _ladderLevel++;
        Rungs++;
        _audit?.RecordRung();
        _log.Info(LogCategory, "stuck: a rung", new LogField("name", Options.Name), new LogField("rung", _ladderLevel), new LogField("plan", _stuckPlans), new LogField("at", position.ToString()));
        _rungStart = now;
        _rungFrom = here.Global;
        switch (_ladderLevel)
        {
            case 1:
                _rung = Rung.BackOff;
                SetForward(false);
                SetTurn(0);
                SetBack(true);
                break;
            case 2:
                _rung = Rung.SidestepLeft;
                SetForward(false);
                SetTurn(0);
                SetSide(-1);
                break;
            case 3:
                _rung = Rung.JumpCharge;
                _rungLeftGround = false;
                SetTurn(0);
                SetForward(true);
                Send(_move.ChargeJump(ActionSource.Automation));
                break;
            default:
                if (_stuckPlans >= MaxStuckPlans)
                {
                    var nearest = _confirmer?.NearestStatic(position) ?? "no physics world to name the nearest static";
                    End(NavRunState.Blocked, string.Create(CultureInfo.InvariantCulture, $"stuck after {_stuckPlans} plans and {Rungs} rungs; nearest {nearest}"), now);
                    return;
                }

                // The stuck node: the route's node a metre past the furthest progress (where the body pushes), and a metre round it.
                var route = _route!;
                var stuck = route.PointAt(MathF.Min(route.ArcLength, MathF.Max(0f, route.NearestArcLength(here)) + 1f));
                if (_paths.Map is { } map && map.TryGetNode(stuck, out var node))
                {
                    stuck = node.Centre;
                }

                Scars.Add(stuck, StuckScarMetres);
                _stuckPlans++;
                _ladderLevel = 0;
                if (Plan(now, position, replan: true))
                {
                    _ladderOrigin = 0f;
                }

                break;
        }
    }

    private void StepRung(double now, Vector2 global)
    {
        var gone = Vector2.Distance(global, _rungFrom);
        var timedOut = now - _rungStart >= RungMoveSeconds;
        switch (_rung)
        {
            case Rung.BackOff:
                if (gone >= BackOffMetres || timedOut)
                {
                    SetBack(false);
                    EndRung(now);
                }

                break;
            case Rung.SidestepLeft:
                if (gone >= SidestepLeftMetres || timedOut)
                {
                    _rung = Rung.SidestepRight;
                    _rungStart = now;
                    _rungFrom = global;
                    SetSide(1);
                }

                break;
            case Rung.SidestepRight:
                if (gone >= SidestepRightMetres || timedOut)
                {
                    SetSide(0);
                    EndRung(now);
                }

                break;
            case Rung.JumpCharge:
                if (now - _rungStart >= JumpCharge.FillSeconds)
                {
                    Send(_move.Jump(ActionSource.Automation));
                    _rung = Rung.JumpAir;
                    _rungStart = now;
                }

                break;
            case Rung.JumpAir:
                _rungLeftGround |= !_body.OnGround;
                if ((_rungLeftGround && _body.OnGround) || timedOut)
                {
                    EndRung(now);
                }

                break;
        }
    }

    private void EndRung(double now)
    {
        _rung = Rung.None;
        _lastProgressTime = now;
        _bestProgress = _route is { } route ? route.NearestArcLength(NavMap.FromCellFrame(_body.Position)) : _bestProgress;
    }

    private NavRunState? CheckIntents(double now)
    {
        for (var i = _inFlight.Count - 1; i >= 0; i--)
        {
            var handle = _inFlight[i];
            if (!handle.IsCompleted)
            {
                continue;
            }

            _inFlight.RemoveAt(i);
            if (handle.Outcome is { } outcome && outcome.Kind is ActionOutcomeKind.DryRun or ActionOutcomeKind.Refused or ActionOutcomeKind.Cancelled or ActionOutcomeKind.NotImplemented
                && !IsDone)
            {
                _inFlight.Clear();
                return End(NavRunState.Blocked, string.Create(CultureInfo.InvariantCulture, $"the movement intent '{handle.Summary}' ended {outcome.Kind}: {outcome.Detail}"), now);
            }
        }

        return null;
    }

    private void OnHumanKey(string key)
    {
        if (State is NavRunState.Idle || IsDone)
        {
            return;
        }

        // The human's key was applied before it was reported (MovementBindings.OnInputAction). The controller's command lists are stacks (CommandList.Add
        // never dedupes, Remove takes the newest entry), so the runner releases EVERY key it holds, the pressed one included: its own entry for that
        // key would otherwise outlive the person's press and resume as the head when the person lets go.
        ReleaseAll();

        _rung = Rung.None;
        State = NavRunState.Interrupted;
        FailureReason = Reason("a person pressed " + key + " (the human's key wins)");
        _endTime = _lastTime;
        _log.Info(LogCategory, "interrupted by the human's key", new LogField("name", Options.Name), new LogField("key", key));
    }

    private NavRunState End(NavRunState state, string? why, double now)
    {
        if (state is not NavRunState.Arrived || Options.Stop || _goal?.IsObject == true)
        {
            ReleaseAll();
        }

        _rung = Rung.None;
        State = state;
        _endTime = now;
        FailureReason = why is null ? null : Reason(why);
        _log.Write(
            state is NavRunState.Blocked or NavRunState.Failed ? LogSeverity.Warning : LogSeverity.Info,
            LogCategory,
            "run ended " + state + " " + VerdictFields(),
            [new LogField("name", Options.Name), new LogField("why", FailureReason ?? string.Empty), new LogField("keys", KeyChanges), new LogField("keys_per_s_max", MaxKeysPerSecond)]);
        return state;
    }

    private string Reason(string why)
    {
        var p = _body.Position;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"run '{Options.Name}' to {(_goal is null ? "nothing" : Describe(_goal))} plan {Plans} rung {Rungs}: {why}; player in cell 0x{p.CellId:X8} at ({p.Origin.X:F2}, {p.Origin.Y:F2}, {p.Origin.Z:F2})");
    }

    private void Send(ActionHandle handle)
    {
        KeyChanges++;
        _keyTimes.Enqueue(_lastTime);
        while (_keyTimes.Count > 0 && _lastTime - _keyTimes.Peek() >= 1.0)
        {
            _keyTimes.Dequeue();
        }

        MaxKeysPerSecond = Math.Max(MaxKeysPerSecond, _keyTimes.Count);
        if (!handle.IsCompleted || handle.Outcome?.Kind is not ActionOutcomeKind.Local)
        {
            _inFlight.Add(handle);
        }
    }

    private void SetForward(bool on)
    {
        if (on != _forward)
        {
            _forward = on;
            _forwardSince = _lastTime;
            Send(_move.Command(MotionCommands.WalkForward, on, ActionSource.Automation));
        }
    }

    private void SetBack(bool on)
    {
        if (on != _back)
        {
            _back = on;
            Send(_move.Command(MotionCommands.WalkBackwards, on, ActionSource.Automation));
        }
    }

    private void SetTurn(int turn)
    {
        if (turn == _turn)
        {
            return;
        }

        if (_turn != 0)
        {
            Send(_move.Command(_turn > 0 ? MotionCommands.TurnRight : MotionCommands.TurnLeft, false, ActionSource.Automation));
        }

        _turn = turn;
        if (turn != 0)
        {
            Send(_move.Command(turn > 0 ? MotionCommands.TurnRight : MotionCommands.TurnLeft, true, ActionSource.Automation));
        }
    }

    private void SetSide(int side)
    {
        if (side == _side)
        {
            return;
        }

        if (_side != 0)
        {
            Send(_move.Command(_side > 0 ? MotionCommands.SideStepRight : MotionCommands.SideStepLeft, false, ActionSource.Automation));
        }

        _side = side;
        if (side != 0)
        {
            Send(_move.Command(side > 0 ? MotionCommands.SideStepRight : MotionCommands.SideStepLeft, true, ActionSource.Automation));
        }
    }

    /// <summary>Runs (true) or walks (false) whatever the run-by-default option is (<see cref="MoveIntents.SetRun"/>).</summary>
    private void SetRun(bool run)
    {
        var down = run != _move.RunByDefault;
        if (down != _runKey)
        {
            _runKey = down;
            Send(_move.SetRun(run, ActionSource.Automation));
        }
    }

    private void SetRunKey(bool down)
    {
        if (down != _runKey)
        {
            _runKey = down;
            Send(_move.HoldRun(down, ActionSource.Automation));
        }
    }

    private void ReleaseAll()
    {
        SetForward(false);
        SetBack(false);
        SetTurn(0);
        SetSide(0);
        SetRunKey(false);
    }
}
