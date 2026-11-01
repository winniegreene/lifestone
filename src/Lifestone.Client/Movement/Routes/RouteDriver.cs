using System.Globalization;
using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Routes;

/// <summary>A stop of a <see cref="RouteDriver"/> whose dwell is over (the scenarios check where-am-I here).</summary>
/// <param name="Lap">The lap, from 0.</param>
/// <param name="GoalIndex">The goal's index in <see cref="ScriptedRoute.Goals"/>.</param>
/// <param name="Position">Where the player stands.</param>
/// <param name="Time">When the dwell ended (the driver's clock, seconds).</param>
public readonly record struct RouteStop(int Lap, int GoalIndex, CellFrame Position, double Time);

/// <summary>
/// Walks a <see cref="ScriptedRoute"/> the way a player at the keyboard would, through
/// <see cref="IPlayerMovementIntents"/> only: it turns toward the goal until it is within 5 degrees, walks with the run
/// modifier, stops within the goal's tolerance and dwells, and fails loudly when it makes no progress for 3 s.
/// </summary>
/// <remarks>
/// <para><b>Steering.</b> Each frame the driver compares the player's heading (<see cref="RetailFrames.GetHeading"/>) with
/// the bearing to the goal, measured in the horizontal plane across landblocks. Beyond
/// <see cref="HeadingToleranceDegrees"/> it holds TurnRight or TurnLeft; beyond <see cref="TurnInPlaceDegrees"/> it lets
/// go of WalkForward and turns in place; otherwise it holds WalkForward. It holds the run key except within
/// <see cref="WalkWithinMetres"/> of a stop. Keys are pressed and released only when that changes, as a player's would
/// be, so every change of a movement key is one MoveToState (the run key, as retail's, sends none:
/// <see cref="IPlayerMovementIntents.SetHoldRun"/>).</para>
/// <para><b>Goals.</b> A goal is reached within its tolerance (horizontal distance). A goal with <see cref="RouteGoal.Stop"/>
/// releases every key and dwells <see cref="RouteGoal.DwellSeconds"/> (the scenarios use 1.5 s, so the 1.0 s cadence has
/// sent the rest position); <see cref="Stopped"/> is raised when the dwell ends. Other goals are passed through.</para>
/// <para><b>Failures.</b> <see cref="RouteStatus.Stuck"/> when neither the distance to the goal shrinks by 5 cm nor the
/// heading error by 2 degrees for <see cref="StuckSeconds"/>; <see cref="RouteStatus.Failed"/> when the player jumps more
/// than <see cref="TeleportMetres"/> in one frame (a teleport or a correction took it away). Both release every key and
/// name the goal, the cell and the position in <see cref="FailureReason"/>.</para>
/// <para><b>Callers:</b> the state track's scenarios (ST16) and the headless host, the wave-2 login check, and the
/// physics track's offline route tests. Main thread (or the headless tick) only; <c>now</c> is the host's monotonic
/// clock in seconds. Call <see cref="Tick"/> once per frame after the physics step. Allocation-free while running.</para>
/// </remarks>
public sealed class RouteDriver
{
    /// <summary>The heading error within which the driver stops turning (m2-plan PH9: 5 degrees).</summary>
    public const double HeadingToleranceDegrees = 5.0;

    /// <summary>The heading error beyond which the driver stops walking and turns in place.</summary>
    public const double TurnInPlaceDegrees = 45.0;

    /// <summary>How long without progress before the route is stuck (m2-plan PH9: 3 s).</summary>
    public const double StuckSeconds = 3.0;

    /// <summary>Within this distance of a stop the driver walks instead of running, so it does not overshoot.</summary>
    public const float WalkWithinMetres = 2.0f;

    /// <summary>A jump of the player's position in one frame longer than this fails the route (a teleport or a blip).</summary>
    public const float TeleportMetres = 10.0f;

    /// <summary>
    /// M5 8 npc: how long without progress before the driver looks for a solid server object in its way (a stall, well before <see cref="StuckSeconds"/> fails the route).
    /// </summary>
    public const double StallProbeSeconds = 1.0;

    /// <summary>M5 8 npc: the most detours (and goal-edge arrivals) the driver makes for one goal; after that the stall fails the route as before.</summary>
    public const int MaxDetoursPerGoal = 2;

    /// <summary>M5 8 npc: how far ahead of the walker a solid object is looked for on the leg to the goal.</summary>
    public const float LookAheadMetres = 4.0f;

    /// <summary>M5 8 npc: the room left between the walker's body and an object's cylinder at a detour point.</summary>
    public const float DetourClearanceMetres = 0.5f;

    /// <summary>M5 8 npc: a leg that comes this close (beyond the two radii) to an object counts as meeting it.</summary>
    public const float LegMarginMetres = 0.1f;

    /// <summary>M5 8 npc: how close the walker gets to a detour point.</summary>
    public const float DetourToleranceMetres = 0.4f;

    /// <summary>M5 8 npc: the arrival radius at the edge of an object's cylinder when the goal lies inside it.</summary>
    public const float EdgeToleranceMetres = 0.5f;

    private const float ProgressMetres = 0.05f;
    private const double ProgressDegrees = 2.0;
    private const double ObstacleLookEverySeconds = 0.2;

    private bool _started;
    private bool _dwelling;
    private double _dwellUntil;
    private bool _forward;
    private int _turn;
    private bool _runKey;
    private readonly bool? _runByDefault;
    private readonly IRunByDefaultSource? _runSource;
    private double _lastProgressTime;
    private float _bestDistance;
    private double _bestHeadingError;
    private CellFrame _lastPosition;
    private readonly List<RouteObstacle> _near = [];
    private CellFrame? _detour;
    private RouteObstacle _detourAround;
    private CellFrame? _goalEdge;
    private int _detoursForGoal;
    private bool _gaveUp;
    private double _nextObstacleLook;
    private bool _stallProbed;
    private string? _lastDetourAround;

    /// <summary>Creates a driver that walks <paramref name="route"/> with <paramref name="controller"/>.</summary>
    /// <param name="controller">The player to drive.</param>
    /// <param name="route">The route to walk.</param>
    /// <param name="runByDefault">
    /// The player's run-by-default option (<see cref="MovementOptions.RunByDefault"/>): with it on, the run key walks. Null (the
    /// default) reads it from the controller when that is an <see cref="IRunByDefaultSource"/> (the real
    /// <see cref="PlayerMovementController"/>), else the option is off: "run" then means run whatever the rig's default.
    /// </param>
    public RouteDriver(IPlayerMovementIntents controller, ScriptedRoute route, bool? runByDefault = null)
        : this(controller, route, runByDefault, null)
    {
    }

    /// <summary>
    /// Creates a driver whose run decision follows <paramref name="runSource"/> (the bound <c>MoveIntents.RunByDefault</c>),
    /// read every tick, so "run" means run whatever the rig's default is.
    /// </summary>
    /// <param name="controller">The player to drive.</param>
    /// <param name="route">The route to walk.</param>
    /// <param name="runSource">Where the run-by-default option is read (<c>session.Actions.Move</c>).</param>
    public RouteDriver(IPlayerMovementIntents controller, ScriptedRoute route, IRunByDefaultSource runSource)
        : this(controller, route, null, runSource ?? throw new ArgumentNullException(nameof(runSource)))
    {
    }

    private RouteDriver(IPlayerMovementIntents controller, ScriptedRoute route, bool? runByDefault, IRunByDefaultSource? source)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentOutOfRangeException.ThrowIfLessThan(route.Laps, 1);
        if (route.Goals.Count == 0)
        {
            throw new ArgumentException("a route needs at least one goal", nameof(route));
        }

        Controller = controller;
        Route = route;
        _runByDefault = runByDefault;
        _runSource = source;
    }

    /// <summary>Raised when the dwell at a stop ends (before the driver moves on).</summary>
    public event Action<RouteStop>? Stopped;

    /// <summary>
    /// M5 7 D153 (D141): raised when a key the driver holds was found let go by something else (a lost window focus drops every held key) and was pressed again; the
    /// argument names the keys, for the caller's log.
    /// </summary>
    public event Action<string>? KeysPressedAgain;

    /// <summary>
    /// M5 8 npc: raised once for each detour (or goal-edge arrival) the driver decides on, with the line for the caller's log: the object's name, wcid and distance, the side
    /// and the reason.
    /// </summary>
    public event Action<string>? Detoured;

    /// <summary>
    /// M5 8 npc: where the driver finds the solid server objects around the walker, or null (the default: it walks straight at its goals and fails when stuck, as before).
    /// </summary>
    public IRouteObstacles? Obstacles { get; set; }

    /// <summary>M5 8 npc: how many detours and goal-edge arrivals the driver has made in all.</summary>
    public int DetoursCount { get; private set; }

    /// <summary>M5 8 npc: the detour point the driver is walking to now, or null.</summary>
    public CellFrame? DetourPoint => _detour;

    /// <summary>M5 7 D153: how many times held keys were found let go and pressed again.</summary>
    public int KeysPressedAgainCount { get; private set; }

    /// <summary>The player being driven.</summary>
    public IPlayerMovementIntents Controller { get; }

    /// <summary>The route being walked.</summary>
    public ScriptedRoute Route { get; }

    /// <summary>
    /// The player's run-by-default option the driver presses the run key against: the bound source's, else the one given,
    /// else the controller's own, else off.
    /// </summary>
    public bool RunByDefault => _runSource?.RunByDefault ?? _runByDefault ?? (Controller as IRunByDefaultSource)?.RunByDefault ?? false;

    /// <summary>The status <see cref="Tick"/> last returned.</summary>
    public RouteStatus Status { get; private set; } = RouteStatus.Running;

    /// <summary>The lap being walked, from 0 (equal to <see cref="ScriptedRoute.Laps"/> once done).</summary>
    public int Lap { get; private set; }

    /// <summary>The goal being walked to, an index into <see cref="ScriptedRoute.Goals"/>.</summary>
    public int GoalIndex { get; private set; }

    /// <summary>Laps finished.</summary>
    public int CompletedLaps => Lap;

    /// <summary>Stops whose dwell has ended.</summary>
    public int StopsCompleted { get; private set; }

    /// <summary>True while the driver stands at a stop.</summary>
    public bool Dwelling => _dwelling;

    /// <summary>Why the route stopped, when <see cref="Tick"/> returned <see cref="RouteStatus.Stuck"/> or <see cref="RouteStatus.Failed"/>; else null.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>Presses and releases keys for this frame; call once per frame after the physics step.</summary>
    /// <param name="now">The host's monotonic clock in seconds.</param>
    public RouteStatus Tick(double now)
    {
        if (Status != RouteStatus.Running)
        {
            return Status;
        }

        var position = Controller.Position;
        if (!_started)
        {
            _started = true;
            _lastPosition = position;
            ResetProgress(now);
        }

        var moved = _lastPosition.GetOffset(position).Length();
        if (moved > TeleportMetres)
        {
            return Fail(
                RouteStatus.Failed,
                string.Create(CultureInfo.InvariantCulture, $"the player was moved {moved:F1} m in one frame (a teleport or a server correction)"),
                position);
        }

        _lastPosition = position;
        if (_dwelling)
        {
            if (now < _dwellUntil)
            {
                return Status;
            }

            _dwelling = false;
            StopsCompleted++;
            Stopped?.Invoke(new RouteStop(Lap, GoalIndex, position, now));
            return NextGoal(now);
        }

        var goal = Route.Goals[GoalIndex];
        var goalTarget = _goalEdge ?? goal.Target;
        var goalTolerance = _goalEdge is null ? goal.Tolerance : EdgeToleranceMetres;
        var offset = position.GetOffset(goalTarget);
        var distance = new Vector2(offset.X, offset.Y).Length();
        if (distance <= goalTolerance)
        {
            if (goal.Stop)
            {
                ReleaseAll();
                _dwelling = true;
                _dwellUntil = now + Math.Max(0.0, goal.DwellSeconds);
                return Status;
            }

            return NextGoal(now);
        }

        if (Obstacles is { } obstacles && _detour is null && LookForObstacle(obstacles, now, position, goal))
        {
            goalTarget = _goalEdge ?? goal.Target;
            offset = position.GetOffset(goalTarget);
            distance = new Vector2(offset.X, offset.Y).Length();
        }

        if (_detour is { } detour)
        {
            var toDetour = position.GetOffset(detour);
            var detourDistance = new Vector2(toDetour.X, toDetour.Y).Length();
            if (detourDistance <= DetourToleranceMetres)
            {
                _detour = null; // reached: the walk goes on to the goal, and the next stall (if any) is judged from here
                ResetProgress(now);
            }
            else
            {
                offset = toDetour;
                distance = detourDistance;
            }
        }

        var bearing = (Math.Atan2(offset.X, offset.Y) * (180.0 / Math.PI) + 360.0) % 360.0;
        var error = Normalize(bearing - RetailFrames.GetHeading(position.Frame));
        var magnitude = Math.Abs(error);
        if (distance < _bestDistance - ProgressMetres || magnitude < _bestHeadingError - ProgressDegrees)
        {
            _bestDistance = MathF.Min(_bestDistance, distance);
            _bestHeadingError = Math.Min(_bestHeadingError, magnitude);
            _lastProgressTime = now;
            _stallProbed = false;
        }
        else if (now - _lastProgressTime > StuckSeconds)
        {
            var toward = _detour is null
                ? string.Create(CultureInfo.InvariantCulture, $"goal {GoalIndex}")
                : string.Create(CultureInfo.InvariantCulture, $"the detour point beside {_detourAround.Name} on the way to goal {GoalIndex}");
            var detours = _detoursForGoal == 0 && !_gaveUp
                ? string.Empty
                : _detoursForGoal == 0
                    ? $", detour given up ({_lastDetourAround})"
                    : string.Create(CultureInfo.InvariantCulture, $", after {_detoursForGoal} detour{(_detoursForGoal == 1 ? string.Empty : "s")} ({_lastDetourAround})");
            return Fail(
                RouteStatus.Stuck,
                string.Create(CultureInfo.InvariantCulture, $"no progress toward {toward} for {StuckSeconds:F0} s ({distance:F2} m and {magnitude:F0} degrees off{detours})"),
                position);
        }

        PressLostKeysAgain();
        SetTurn(magnitude <= HeadingToleranceDegrees ? 0 : error > 0 ? 1 : -1);
        SetRun(!goal.Stop || distance > WalkWithinMetres);
        SetForward(magnitude < TurnInPlaceDegrees);
        return Status;
    }

    /// <summary>
    /// M5 8 npc (check, act, check, D141; Lifestone's own walker behaviour, retail has no automated walker): a solid server object (a static NPC, a chest, a stand; since
    /// M5 item 5 every server object with collision is solid in the player's world, D70 to D72) can stand on a route's leg or on its goal, and the straight-line walker then
    /// pushes at it until the stall fails the route. The Holtburg ring stalled at its goal 7 about once in ten runs with the Pumpkin Buffer next to the walker. CHECK: every
    /// 0.2 s, and at once when the walker has made no progress for <see cref="StallProbeSeconds"/>, ask <see cref="Obstacles"/> for the solid objects within
    /// <see cref="LookAheadMetres"/>; the leg to the goal meets an object whose cylinder (its radius plus the body's plus <see cref="LegMarginMetres"/>) it passes through, or
    /// the goal lies inside it, or (stalled, nothing on the leg) the walker touches one. ACT: for a leg, a detour point beside the object (its radius plus the body's plus
    /// <see cref="DetourClearanceMetres"/>, to the side of the leg), on whichever side the collision world says a body fits (both are probed, the shorter way wins, a side the
    /// probe refuses is never walked); for a goal inside the cylinder, the nearest free point on its edge, accepted as the goal's arrival within
    /// <see cref="EdgeToleranceMetres"/>. CHECK AFTER: the walk to the detour point and on to the goal is judged by the same progress rule as any other, so a detour that does not
    /// free the walk fails as stuck, named, with the detours listed; at most <see cref="MaxDetoursPerGoal"/> per goal, and none while one is being walked.
    /// </summary>
    /// <returns>True when the walk's target changed this frame (a detour point or a goal edge was set).</returns>
    private bool LookForObstacle(IRouteObstacles obstacles, double now, in CellFrame position, RouteGoal goal)
    {
        if (_gaveUp || _detoursForGoal >= MaxDetoursPerGoal)
        {
            return false;
        }

        var stalled = now - _lastProgressTime > StallProbeSeconds;
        if (stalled ? _stallProbed : now < _nextObstacleLook)
        {
            return false;
        }

        _nextObstacleLook = now + ObstacleLookEverySeconds;
        _stallProbed |= stalled;
        _near.Clear();
        obstacles.Near(position, LookAheadMetres, _near);
        if (_near.Count == 0)
        {
            return false;
        }

        var body = obstacles.BodyRadius;
        var target = _goalEdge ?? goal.Target;
        var leg = Horizontal(position.GetOffset(target));
        var length = leg.Length();

        // A goal inside an object's cylinder cannot be reached: the walk ends at the cylinder's edge.
        if (_goalEdge is null)
        {
            foreach (var o in _near)
            {
                var centre = Horizontal(position.GetOffset(o.Centre));
                if (o.Radius + body - (leg - centre).Length() > goal.Tolerance)
                {
                    return AcceptGoalEdge(obstacles, now, goal, o, centre, leg);
                }
            }
        }

        if (length < 1e-3f)
        {
            return false;
        }

        var ahead = leg / length;
        var found = false;
        var best = default(RouteObstacle);
        var bestAlong = float.MaxValue;
        foreach (var o in _near)
        {
            var centre = Horizontal(position.GetOffset(o.Centre));
            var along = Vector2.Dot(centre, ahead);
            var across = MathF.Abs((ahead.X * centre.Y) - (ahead.Y * centre.X));
            var reach = o.Radius + body;
            if (along > -0.2f && along - reach < length && across < reach + LegMarginMetres && along < bestAlong)
            {
                (found, best, bestAlong) = (true, o, along);
            }
        }

        var reason = "on the leg to the goal";
        if (!found && stalled)
        {
            var nearest = float.MaxValue;
            foreach (var o in _near)
            {
                var gap = Horizontal(position.GetOffset(o.Centre)).Length() - o.Radius - body;
                if (gap <= 0.5f && gap < nearest)
                {
                    (found, best, nearest) = (true, o, gap);
                }
            }

            reason = "touching it with no progress";
        }

        return found && StartDetour(obstacles, now, position, best, ahead, leg, reason);
    }

    private bool StartDetour(IRouteObstacles obstacles, double now, in CellFrame position, RouteObstacle around, Vector2 ahead, Vector2 leg, string reason)
    {
        var centre = Horizontal(position.GetOffset(around.Centre));
        var away = around.Radius + obstacles.BodyRadius + DetourClearanceMetres;
        var left = new Vector2(-ahead.Y, ahead.X);
        CellFrame? chosen = null;
        var chosenLeft = false;
        var chosenLength = float.MaxValue;
        var blocked = 0;
        foreach (var isLeft in new[] { true, false })
        {
            var side = isLeft ? left : -left;
            if (!obstacles.TryFindStand(around.Centre, side * away, out var standing))
            {
                blocked++;
                continue;
            }

            var point = centre + (side * away);
            var length = point.Length() + (leg - point).Length();
            if (length < chosenLength)
            {
                (chosen, chosenLeft, chosenLength) = (standing, isLeft, length);
            }
        }

        var distance = centre.Length();
        if (chosen is not { } stand)
        {
            _gaveUp = true; // both sides probed and refused: no more tries for this goal, the stall stands
            _lastDetourAround = $"no free side beside {around.Name}";
            Detoured?.Invoke(string.Create(
                CultureInfo.InvariantCulture,
                $"route '{Route.Name}' lap {Lap} goal {GoalIndex}: no detour around {RouteObstacleShapes.Describe(around)}, {distance:0.0} m away ({reason}): the collision world has no free place on either side ({blocked} probes refused); the walk goes on and a stall fails it"));
            return false;
        }

        _detour = stand;
        _detourAround = around;
        _detoursForGoal++;
        DetoursCount++;
        _lastDetourAround = $"around {around.Name} on the {(chosenLeft ? "left" : "right")}";
        ResetProgress(now);
        Detoured?.Invoke(string.Create(
            CultureInfo.InvariantCulture,
            $"route '{Route.Name}' lap {Lap} goal {GoalIndex}: detour {_detoursForGoal} of {MaxDetoursPerGoal} around {RouteObstacleShapes.Describe(around)}, {distance:0.0} m away ({reason}), by its {(chosenLeft ? "left" : "right")} side (free by probe), then on to the goal; reason: the walk to goal {GoalIndex} is blocked by a solid object"));
        return true;
    }

    private bool AcceptGoalEdge(IRouteObstacles obstacles, double now, RouteGoal goal, RouteObstacle covering, Vector2 centre, Vector2 leg)
    {
        _ = goal;
        var edge = covering.Radius + obstacles.BodyRadius + 0.1f;
        var toGoal = leg - centre;
        var toward = toGoal.Length() > 0.05f ? Vector2.Normalize(toGoal) : centre.Length() > 0.05f ? Vector2.Normalize(-centre) : new Vector2(0f, 1f);
        foreach (var degrees in new[] { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f })
        {
            var turn = degrees * (MathF.PI / 180f);
            var direction = new Vector2(
                (toward.X * MathF.Cos(turn)) - (toward.Y * MathF.Sin(turn)),
                (toward.X * MathF.Sin(turn)) + (toward.Y * MathF.Cos(turn)));
            if (!obstacles.TryFindStand(covering.Centre, direction * edge, out var standing))
            {
                continue;
            }

            _goalEdge = standing;
            _detoursForGoal++;
            DetoursCount++;
            _lastDetourAround = $"goal covered by {covering.Name}";
            var gap = (centre + (direction * edge) - leg).Length();
            ResetProgress(now);
            Detoured?.Invoke(string.Create(
                CultureInfo.InvariantCulture,
                $"route '{Route.Name}' lap {Lap} goal {GoalIndex}: the goal lies inside the cylinder of {RouteObstacleShapes.Describe(covering)}, {centre.Length():0.0} m away; arrival accepted at its edge, {gap:0.00} m from the goal, within {EdgeToleranceMetres:0.0} m (free by probe, {degrees:0} degrees round from the goal's side); reason: the goal cannot be reached"));
            return true;
        }

        _gaveUp = true;
        _lastDetourAround = $"no free edge point at {covering.Name}";
        Detoured?.Invoke(string.Create(
            CultureInfo.InvariantCulture,
            $"route '{Route.Name}' lap {Lap} goal {GoalIndex}: the goal lies inside the cylinder of {RouteObstacleShapes.Describe(covering)} and the collision world has no free place on its edge; the walk goes on and a stall fails it"));
        return false;
    }

    private static Vector2 Horizontal(Vector3 v) => new(v.X, v.Y);

    /// <summary>
    /// M5 7 D153 (D141, check, act, check): the driver remembers the keys it pressed, but something else can let them go: the gate's Holtburg walk of 2026-10-31 13:04 UTC
    /// stood still 2.95 m short of its first goal with no key held (a lost window focus, <c>CommandInterpreter::LoseKeyboardFocus</c>), while the driver still believed it
    /// held forward. Each frame the controller is asked; a key it no longer holds is pressed again (with the run key), counted and reported once each time.
    /// </summary>
    private void PressLostKeysAgain()
    {
        var forwardLost = _forward && Controller.IsHeld(MotionCommands.WalkForward) == false;
        var turnCommand = _turn > 0 ? MotionCommands.TurnRight : MotionCommands.TurnLeft;
        var turnLost = _turn != 0 && Controller.IsHeld(turnCommand) == false;
        if (!forwardLost && !turnLost)
        {
            return;
        }

        if (forwardLost)
        {
            Controller.HandleCommand(MotionCommands.WalkForward, start: true);
        }

        if (turnLost)
        {
            Controller.HandleCommand(turnCommand, start: true);
        }

        if (_runKey)
        {
            Controller.SetHoldRun(true);
        }

        KeysPressedAgainCount++;
        KeysPressedAgain?.Invoke(string.Create(
            CultureInfo.InvariantCulture,
            $"route '{Route.Name}' lap {Lap} goal {GoalIndex}: {(forwardLost ? turnLost ? "forward and turn" : "forward" : "turn")} found let go by something else, pressed again (#{KeysPressedAgainCount})"));
    }

    /// <summary>The angle in (-180, 180].</summary>
    private static double Normalize(double degrees)
    {
        degrees %= 360.0;
        if (degrees > 180.0)
        {
            degrees -= 360.0;
        }
        else if (degrees <= -180.0)
        {
            degrees += 360.0;
        }

        return degrees;
    }

    /// <summary>Moves on to the next goal (or lap); Done after the last.</summary>
    private RouteStatus NextGoal(double now)
    {
        GoalIndex++;
        if (GoalIndex >= Route.Goals.Count)
        {
            GoalIndex = 0;
            Lap++;
            if (Lap >= Route.Laps)
            {
                ReleaseAll();
                Status = RouteStatus.Done;
                return Status;
            }
        }

        _detour = null;
        _goalEdge = null;
        _detoursForGoal = 0;
        _gaveUp = false;
        _lastDetourAround = null;
        _nextObstacleLook = 0;
        ResetProgress(now);
        return Status;
    }

    private void ResetProgress(double now)
    {
        _stallProbed = false;
        _lastProgressTime = now;
        _bestDistance = float.MaxValue;
        _bestHeadingError = double.MaxValue;
    }

    private RouteStatus Fail(RouteStatus status, string why, in CellFrame position)
    {
        ReleaseAll();
        Status = status;
        FailureReason = string.Create(
            CultureInfo.InvariantCulture,
            $"route '{Route.Name}' lap {Lap} goal {GoalIndex}: {why}; player in cell 0x{position.CellId:X8} at ({position.Frame.Origin.X:F2}, {position.Frame.Origin.Y:F2}, {position.Frame.Origin.Z:F2})");
        return status;
    }

    private void SetTurn(int turn)
    {
        if (turn == _turn)
        {
            return;
        }

        if (_turn != 0)
        {
            Controller.HandleCommand(_turn > 0 ? MotionCommands.TurnRight : MotionCommands.TurnLeft, start: false);
        }

        _turn = turn;
        if (turn != 0)
        {
            Controller.HandleCommand(turn > 0 ? MotionCommands.TurnRight : MotionCommands.TurnLeft, start: true);
        }
    }

    private void SetForward(bool forward)
    {
        if (forward != _forward)
        {
            _forward = forward;
            Controller.HandleCommand(MotionCommands.WalkForward, forward);
        }
    }

    /// <summary>Runs (true) or walks (false): the run key is held when that is not what the run-by-default option already gives.</summary>
    private void SetRun(bool run) => SetRunKey(run != RunByDefault);

    private void SetRunKey(bool down)
    {
        if (down != _runKey)
        {
            _runKey = down;
            Controller.SetHoldRun(down);
        }
    }

    /// <summary>
    /// Stops the walk: every key is released and the route ends without a verdict (<see cref="Status"/> becomes
    /// <see cref="RouteStatus.Failed"/>, <see cref="FailureReason"/> says so), for a scenario that plans a new route from
    /// where the player stands with a new driver.
    /// </summary>
    public void Abandon()
    {
        ReleaseAll();
        if (Status == RouteStatus.Running)
        {
            Status = RouteStatus.Failed;
            FailureReason = "the route was abandoned for a new one";
        }
    }

    private void ReleaseAll()
    {
        SetForward(false);
        SetTurn(0);
        SetRunKey(false);
    }
}
