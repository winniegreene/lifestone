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

    private const float ProgressMetres = 0.05f;
    private const double ProgressDegrees = 2.0;

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
        var offset = position.GetOffset(goal.Target);
        var distance = new Vector2(offset.X, offset.Y).Length();
        if (distance <= goal.Tolerance)
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

        var bearing = (Math.Atan2(offset.X, offset.Y) * (180.0 / Math.PI) + 360.0) % 360.0;
        var error = Normalize(bearing - RetailFrames.GetHeading(position.Frame));
        var magnitude = Math.Abs(error);
        if (distance < _bestDistance - ProgressMetres || magnitude < _bestHeadingError - ProgressDegrees)
        {
            _bestDistance = MathF.Min(_bestDistance, distance);
            _bestHeadingError = Math.Min(_bestHeadingError, magnitude);
            _lastProgressTime = now;
        }
        else if (now - _lastProgressTime > StuckSeconds)
        {
            return Fail(
                RouteStatus.Stuck,
                string.Create(CultureInfo.InvariantCulture, $"no progress toward goal {GoalIndex} for {StuckSeconds:F0} s ({distance:F2} m and {magnitude:F0} degrees off)"),
                position);
        }

        SetTurn(magnitude <= HeadingToleranceDegrees ? 0 : error > 0 ? 1 : -1);
        SetRun(!goal.Stop || distance > WalkWithinMetres);
        SetForward(magnitude < TurnInPlaceDegrees);
        return Status;
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

        ResetProgress(now);
        return Status;
    }

    private void ResetProgress(double now)
    {
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
