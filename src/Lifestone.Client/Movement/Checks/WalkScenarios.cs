using System.Globalization;
using System.Numerics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Checks;

/// <summary>
/// A walk along a <see cref="ScriptedRoute"/> by <see cref="RouteDriver"/> (turn toward the goal and run forward, as a player at the keyboard
/// would), judged by the status the route ends in, by whether the player really ran (<see cref="RunningMetresPerSecond"/>, the Run hold key in the
/// MoveToStates) and, for each walk, its own rules (<see cref="Expect"/>).
/// </summary>
/// <remarks>
/// <b>Running is asserted, not assumed.</b> The run key inverts the run-by-default option, so a driver that holds it to run walks a player that runs by
/// default; the driver is told the option (<see cref="RouteDriver"/>'s <c>runByDefault</c>), and every walk fails when the player never ran: no stretch of
/// <see cref="RunWindowSeconds"/> on the ground faster than a walk, or the forward MoveToStates not carrying the Run hold key.
/// </remarks>
public class RouteWalk : WalkScenario
{
    /// <summary>
    /// The ground speed that tells a run from a walk (metres a second): above the human walk (<c>MotionInterpreter.WalkAnimSpeed</c>, 3.12) and below the
    /// stand-in's run (4 times its run rate).
    /// </summary>
    public const float RunningMetresPerSecond = 4.0f;

    /// <summary>The stretch of time over which a speed is measured (a shorter one would take a stumble for a run).</summary>
    public const double RunWindowSeconds = 0.5;

    private RouteDriver? _driver;

    /// <summary>A walk of <paramref name="route"/> that should end <paramref name="expected"/> (Done for a walk; Stuck for one that runs into a wall).</summary>
    public RouteWalk(string name, ScriptedRoute route, RouteStatus expected, double timeoutSeconds = 120.0)
        : base(name, timeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(route);
        Route = route;
        Expected = expected;
    }

    /// <summary>The route walked.</summary>
    public ScriptedRoute Route { get; }

    /// <summary>The status the route should end in.</summary>
    public RouteStatus Expected { get; }

    /// <summary>First person for this walk (the camera key), else the chase camera.</summary>
    public bool InFirstPerson { get; init; }

    /// <summary>True when the walk must show the player running (the default); a walk that does not run on purpose sets it false.</summary>
    public bool RequireRun { get; init; } = true;

    /// <summary>This walk's own rules: adds facts and problems after the common ones.</summary>
    public Action<RouteWalk, IWalkHost, List<string>, List<string>>? Expect { get; init; }

    /// <summary>The stops reached (the dwell at a stop ended).</summary>
    public List<RouteStop> Stops { get; } = [];

    /// <summary>The status the driver ended in.</summary>
    public RouteStatus Status => _driver?.Status ?? RouteStatus.Running;

    /// <summary>Why the driver ended Stuck or Failed.</summary>
    public string? FailureReason => _driver?.FailureReason;

    /// <inheritdoc/>
    public override CellFrame? StartFrame => Route.Start;

    /// <inheritdoc/>
    public override bool FirstPerson => InFirstPerson;

    /// <summary>The cells the player was in, in order, each once in a row.</summary>
    public List<uint> VisitedCells()
    {
        var cells = new List<uint>();
        foreach (var sample in Samples)
        {
            if (cells.Count == 0 || cells[^1] != sample.Position.CellId)
            {
                cells.Add(sample.Position.CellId);
            }
        }

        return cells;
    }

    /// <summary>The player's height range over the walk (<see cref="WalkScenario.HeightRange"/>).</summary>
    public (float Min, float Max, float First, float Last) Heights() => HeightRange();

    /// <summary>Frames the player was off the ground.</summary>
    public int FramesOffGround() => Samples.Count(s => !s.OnGround);

    /// <summary>
    /// The MoveToStates of this walk with a forward command of WalkForward, how many of them carry the Run hold key, and whether the first of them does (the
    /// press that starts the walk).
    /// </summary>
    public (int Forward, int Running, bool FirstRuns) ForwardMoveToStates(IWalkHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var forward = 0;
        var running = 0;
        var firstRuns = false;
        var messages = host.Trace.Messages;
        for (var i = FirstMessage; i < messages.Count; i++)
        {
            var message = messages[i];
            if (message.Kind == SentKind.MoveToState && message.ForwardCommand == MotionCommands.WalkForward)
            {
                firstRuns |= forward == 0 && message.HoldKey == HoldKey.Run;
                forward++;
                running += message.HoldKey == HoldKey.Run ? 1 : 0;
            }
        }

        return (forward, running, firstRuns);
    }

    /// <inheritdoc/>
    protected override void OnStart(IWalkHost host, double now)
    {
        _driver = new RouteDriver(Intents, Route, host.Controller.Options.RunByDefault);
        _driver.Stopped += Stops.Add;
    }

    /// <summary>Called each frame before the driver steers (the running jump presses its keys here).</summary>
    protected virtual void BeforeDriver(IWalkHost host, double now)
    {
    }

    /// <inheritdoc/>
    protected sealed override bool OnTick(IWalkHost host, double now)
    {
        BeforeDriver(host, now);
        return _driver!.Tick(now) != RouteStatus.Running;
    }

    /// <inheritdoc/>
    protected override void Evaluate(IWalkHost host, List<string> facts, List<string> problems)
    {
        var driver = _driver!;
        var heights = HeightRange();
        facts.Add(string.Create(CultureInfo.InvariantCulture, $"route '{Route.Name}' {driver.Status}, {driver.StopsCompleted} stop(s), height {heights.Min:F2}..{heights.Max:F2}, {FramesOffGround()} frames off the ground"));
        if (driver.Status != Expected)
        {
            problems.Add($"the route ended {driver.Status} but should end {Expected}" + (driver.FailureReason is { } why ? $" ({why})" : string.Empty) + $" [{host.Controller.DescribeMotion()}]");
        }

        RunRules(host, facts, problems);
        Expect?.Invoke(this, host, facts, problems);
    }

    private void RunRules(IWalkHost host, List<string> facts, List<string> problems)
    {
        var speed = PeakGroundSpeed(RunWindowSeconds);
        var (forward, running, firstRuns) = ForwardMoveToStates(host);
        facts.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"fastest on the ground {speed:F1} m/s over {RunWindowSeconds:F1} s (a walk is {MotionInterpreter.WalkAnimSpeed:F1}), {running} of {forward} forward MoveToStates carry the Run hold key"));
        if (!RequireRun)
        {
            return;
        }

        if (speed < RunningMetresPerSecond)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"the player never ran: the fastest it moved on the ground over {RunWindowSeconds:F1} s was {speed:F1} m/s (a run is over {RunningMetresPerSecond:F1}, a walk {MotionInterpreter.WalkAnimSpeed:F1})"));
        }

        // The steps within WalkWithinMetres of a stop walk, and a driver that steers close to its goal sends many of them, so a walk's share is a third at least.
        if (forward == 0 || !firstRuns || running * 3 < forward)
        {
            problems.Add($"only {running} of {forward} forward MoveToStates carry the Run hold key{(firstRuns ? string.Empty : ", and not the one that starts the walk")} (the driver should be running)");
        }
    }
}

/// <summary>
/// A jump's flight, measured frame by frame: whether and when the player left the ground, the apex, the landing, and the lowest the player was
/// above the ground under it (a fall through the floor shows as a negative number).
/// </summary>
public sealed class JumpTrack
{
    /// <summary>The player left the ground.</summary>
    public bool Left { get; private set; }

    /// <summary>The player came down again.</summary>
    public bool Landed { get; private set; }

    /// <summary>When the player left the ground.</summary>
    public double LeftAt { get; private set; }

    /// <summary>When the player landed.</summary>
    public double LandedAt { get; private set; }

    /// <summary>The height where the jump started.</summary>
    public float StartZ { get; private set; }

    /// <summary>The highest the player rose above <see cref="StartZ"/>.</summary>
    public float Apex { get; private set; }

    /// <summary>Where the jump started.</summary>
    public CellFrame TakeOff { get; private set; }

    /// <summary>Where the player landed.</summary>
    public CellFrame Landing { get; private set; }

    /// <summary>The lowest the player's origin was above the ground the physics found under it (+infinity when never measured).</summary>
    public float MinAboveGround { get; private set; } = float.PositiveInfinity;

    /// <summary>Starts watching a jump from the player's current place (the jump key went down).</summary>
    public void Begin(IWalkHost host) => Release(host);

    /// <summary>
    /// The jump key came up (<c>DoJump</c>): the take-off is where the player is now, so a running jump's carry does not count the run that charged it.
    /// </summary>
    public void Release(IWalkHost host)
    {
        TakeOff = host.Controller.Position;
        StartZ = TakeOff.Origin.Z;
    }

    /// <summary>Looks at the player this frame.</summary>
    public void Update(IWalkHost host, double now)
    {
        var controller = host.Controller;
        var position = controller.Position;
        if (!Left)
        {
            if (!controller.OnGround)
            {
                Left = true;
                LeftAt = now;
            }
        }
        else if (!Landed && controller.OnGround)
        {
            Landed = true;
            LandedAt = now;
            Landing = position;
        }

        Apex = MathF.Max(Apex, position.Origin.Z - StartZ);
        if (host.World.TryGroundZ(position, controller.Player.Radius, out var ground, out _))
        {
            MinAboveGround = MathF.Min(MinAboveGround, position.Origin.Z - ground);
        }
    }

    /// <summary>Adds the flight's facts, and what is wrong with it, for a jump released at <paramref name="jumpedAt"/>.</summary>
    public void Judge(double jumpedAt, List<string> facts, List<string> problems, float minApex)
    {
        facts.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"jump: left the ground {(Left ? LeftAt - jumpedAt : double.NaN):F2} s after DoJump, apex {Apex:F2} m, {(Landed ? $"landed after {LandedAt - LeftAt:F2} s, {Landing.GetOffset(TakeOff).Length():F2} m from take-off, {Landing.Origin.Z - StartZ:+0.00;-0.00;0.00} m in height" : "did not land")}, lowest above the ground {MinAboveGround:F2} m"));
        if (!Left)
        {
            problems.Add("the player never left the ground");
        }
        else if (LeftAt - jumpedAt > 0.6)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"the player left the ground {LeftAt - jumpedAt:F2} s after the jump (should be at once)"));
        }

        if (Apex < minApex)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"the jump rose only {Apex:F2} m (at least {minApex:F2} m expected)"));
        }

        if (!Landed)
        {
            problems.Add("the player did not land");
        }

        if (MinAboveGround < -0.05f)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"the player sank {-MinAboveGround:F2} m into the ground (a fall through the floor)"));
        }
    }
}

/// <summary>A standing jump: the player stands still, charges the jump bar, releases it, flies and lands where it took off.</summary>
public sealed class StandingJump : WalkScenario
{
    /// <summary>How long the jump key is held (the jump bar charges).</summary>
    public const double ChargeSeconds = 0.4;

    /// <summary>How long the scenario waits after landing (the position messages after a landing).</summary>
    public const double RestSeconds = 1.5;

    private readonly CellFrame _at;
    private readonly JumpTrack _track = new();
    private double _chargeStart;
    private double _jumpedAt = double.NaN;

    /// <summary>A standing jump at <paramref name="at"/>.</summary>
    public StandingJump(string name, CellFrame at)
        : base(name, 20.0)
    {
        _at = at;
    }

    /// <inheritdoc/>
    public override CellFrame? StartFrame => _at;

    /// <inheritdoc/>
    protected override void OnStart(IWalkHost host, double now)
    {
        _track.Begin(host);
        _chargeStart = now;
        Intents.CommenceJump(now);
    }

    /// <inheritdoc/>
    protected override bool OnTick(IWalkHost host, double now)
    {
        if (double.IsNaN(_jumpedAt))
        {
            if (now - _chargeStart >= ChargeSeconds)
            {
                _jumpedAt = now;
                _track.Release(host);
                Intents.DoJump(now);
            }

            return false;
        }

        _track.Update(host, now);
        return _track.Landed && now - _track.LandedAt >= RestSeconds;
    }

    /// <inheritdoc/>
    protected override void Evaluate(IWalkHost host, List<string> facts, List<string> problems)
    {
        _track.Judge(_jumpedAt, facts, problems, minApex: 0.2f);
        if (_track.Landed)
        {
            var drift = _track.Landing.GetOffset(_track.TakeOff).Length();
            if (drift > 0.75f)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"a standing jump landed {drift:F2} m from where it took off"));
            }

            if (MathF.Abs(_track.Landing.Origin.Z - _track.StartZ) > 0.15f)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"the player landed {_track.Landing.Origin.Z - _track.StartZ:+0.00;-0.00} m from the take-off height on flat ground"));
            }
        }

        var jumps = host.Trace.Count(SentKind.Jump, FirstMessage);
        if (jumps != 1)
        {
            problems.Add($"{jumps} Jump message(s) were sent for one jump");
        }

        if (host.Trace.Count(SentKind.MoveToState, FirstMessage) < 1)
        {
            problems.Add("charging the jump sent no MoveToState");
        }
    }
}

/// <summary>A running jump: a route walked at a run, with a jump charged and released part way along it; the player lands and the walk goes on.</summary>
public sealed class RunningJump : RouteWalk
{
    /// <summary>How long into the run the jump key goes down (the run has reached its speed).</summary>
    public const double JumpAfterSeconds = 1.0;

    /// <summary>How long the jump key is held.</summary>
    public const double ChargeSeconds = 0.3;

    /// <summary>The least a running jump carries the player, from where the jump key came up (a walking jump carries it about half as far).</summary>
    public const float MinCarryMetres = 3.0f;

    private readonly JumpTrack _track = new();
    private double _chargeStart = double.NaN;
    private double _jumpedAt = double.NaN;

    /// <summary>A walk of <paramref name="route"/> that jumps part way.</summary>
    public RunningJump(string name, ScriptedRoute route)
        : base(name, route, RouteStatus.Done, 60.0)
    {
        Expect = Rules;
    }

    /// <inheritdoc/>
    protected override void BeforeDriver(IWalkHost host, double now)
    {
        if (double.IsNaN(_chargeStart))
        {
            if (now - StartTime >= JumpAfterSeconds)
            {
                _track.Begin(host);
                _chargeStart = now;
                Intents.CommenceJump(now);
            }

            return;
        }

        if (double.IsNaN(_jumpedAt) && now - _chargeStart >= ChargeSeconds)
        {
            _jumpedAt = now;
            _track.Release(host);
            Intents.DoJump(now);
        }

        if (!double.IsNaN(_jumpedAt))
        {
            _track.Update(host, now);
        }
    }

    private void Rules(RouteWalk walk, IWalkHost host, List<string> facts, List<string> problems)
    {
        if (double.IsNaN(_jumpedAt))
        {
            problems.Add("the walk ended before the jump was released");
            return;
        }

        _track.Judge(_jumpedAt, facts, problems, minApex: 0.2f);
        if (_track.Landed)
        {
            var carry = _track.Landing.GetOffset(_track.TakeOff).Length();
            facts.Add(string.Create(CultureInfo.InvariantCulture, $"carried {carry:F1} m from the moment the jump key came up"));
            if (carry < MinCarryMetres)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"a running jump landed only {carry:F2} m from where the jump key came up (a run carries it at least {MinCarryMetres:F1} m)"));
            }
        }

        // The Jump message carries the velocity the run gave the leap: a walking jump's forward speed is the walk's.
        foreach (var message in host.Trace.Messages.Skip(FirstMessage))
        {
            if (message.Kind == SentKind.Jump)
            {
                var forward = new Vector2(message.Velocity.X, message.Velocity.Y).Length();
                facts.Add(string.Create(CultureInfo.InvariantCulture, $"the Jump message carries {forward:F1} m/s forward and {message.Velocity.Z:F1} up"));
                if (forward < RouteWalk.RunningMetresPerSecond)
                {
                    problems.Add(string.Create(CultureInfo.InvariantCulture, $"the Jump message carries only {forward:F1} m/s forward (a running jump carries over {RouteWalk.RunningMetresPerSecond:F1}; a walk's is {MotionInterpreter.WalkAnimSpeed:F1})"));
                }
            }
        }

        var jumps = host.Trace.Count(SentKind.Jump, FirstMessage);
        if (jumps != 1)
        {
            problems.Add($"{jumps} Jump message(s) were sent for one jump");
        }
    }
}
