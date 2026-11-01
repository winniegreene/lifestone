using System.Numerics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// P5a lane S5's scripted body: a kinematic player on flat ground in <see cref="MovementTestKit.Block"/> that is both the controller
/// <see cref="MoveIntents"/> drives (it records every call and holds the keys) and the <see cref="INavBody"/> the runner reads. With
/// <see cref="Frozen"/> it never moves (the ladder rung by rung); else forward runs 4 m/s times <see cref="RunRate"/> (walks 3.12), back 1.5,
/// sidestep 1.25, a turn key 90 degrees a second (the synthetic motion table's rates). The run rate defaults to the fighters' live one
/// (<see cref="FighterRunRate"/>, 9.9 m/s: P5a live round 1 measured 9.6; the earlier 4 m/s was a convenience, not a real rate). It holds the
/// keys as a STACK per command (retail's CommandList: an Add never dedupes, a Remove takes one entry), so a leaked press shows.
/// </summary>
internal sealed class S5KinematicBody : IPlayerMovementIntents, INavBody
{
    /// <summary>The fighters' buffed run rate (P5a research, live round 1: 9.6 m/s measured).</summary>
    public const float FighterRunRate = 2.47f;

    /// <summary>The BSD Mage's run rate (P5a research: Run 144 ranks, about 2.15).</summary>
    public const float MageRunRate = 2.15f;

    /// <summary>The run speed at run rate 1.</summary>
    public const float BaseRunSpeed = 4f;
    public const float WalkSpeed = 3.12f;
    public const float BackSpeed = 1.5f;
    public const float SideSpeed = 1.25f;
    public const float TurnDegreesPerSecond = 90f;

    /// <summary>The turn rate of a running player, in degrees a second (about 200: <see cref="NavRunner.CrossTrackMetres"/>'s note on the physics step).</summary>
    public const float RunningTurnDegreesPerSecond = 200f;

    /// <summary>The rate the held turn key turns the body, in degrees a second (<see cref="TurnDegreesPerSecond"/> unless a test sets the running player's).</summary>
    public float TurnRate { get; set; } = TurnDegreesPerSecond;

    private readonly Dictionary<uint, int> _held = [];

    public S5KinematicBody(float x, float y, float heading = 0f)
    {
        Xy = new Vector2(x, y);
        Heading = heading;
    }

    public List<string> Calls { get; } = [];

    public Vector2 Xy { get; set; }

    public float Z { get; set; }

    /// <summary>Degrees clockwise from north (retail's heading).</summary>
    public float Heading { get; set; }

    public bool Frozen { get; set; }

    /// <summary>The run rate (<see cref="FighterRunRate"/> by default).</summary>
    public float RunRate { get; set; } = FighterRunRate;

    /// <summary>The run speed in metres a second.</summary>
    public float RunSpeed => BaseRunSpeed * RunRate;

    public bool HoldRun { get; private set; }

    public bool RunByDefault { get; set; }

    public bool OnGround { get; set; } = true;

    public bool ServerDriving { get; set; }

    public double Now { get; set; }

    public CellFrame Position => new(
        CellIds.LandCellAt(MovementTestKit.Block, Xy.X, Xy.Y)!.Value,
        RetailFrames.SetHeading(new AcFrame(new Vector3(Xy.X, Xy.Y, Z), Quaternion.Identity), Heading));

    public uint CellId => Position.CellId;

    public bool Holds(uint command) => Depth(command) > 0;

    /// <summary>The entries of <paramref name="command"/> on the stack (presses minus releases, never below 0).</summary>
    public int Depth(uint command) => _held.GetValueOrDefault(command);

    /// <summary>Every entry on the stacks, summed.</summary>
    public int TotalDepth => _held.Values.Sum();

    public void HandleCommand(uint command, bool start, float speed = 1f)
    {
        Calls.Add(FormattableString.Invariant($"{Name(command)} {(start ? "on" : "off")}"));
        var depth = Depth(command);
        _held[command] = start ? depth + 1 : Math.Max(0, depth - 1);
    }

    public void SetHoldRun(bool on)
    {
        HoldRun = on;
        Calls.Add("run-key " + (on ? "on" : "off"));
    }

    public void SetHoldSidestep(bool on) => Calls.Add("sidestep-key " + (on ? "on" : "off"));

    public void ToggleAutoRun() => Calls.Add("autorun");

    public void CommenceJump(double now) => Calls.Add("charge-jump");

    public void DoJump(double now) => Calls.Add("jump");

    /// <summary>Moves the body by its held keys for <paramref name="dt"/> seconds.</summary>
    public void Advance(double dt)
    {
        Now += dt;
        if (Frozen || ServerDriving)
        {
            return;
        }

        var t = (float)dt;
        if (Holds(MotionCommands.TurnRight))
        {
            Heading += TurnRate * t;
        }

        if (Holds(MotionCommands.TurnLeft))
        {
            Heading -= TurnRate * t;
        }

        Heading = ((Heading % 360f) + 360f) % 360f;
        var rad = Heading * MathF.PI / 180f;
        var ahead = new Vector2(MathF.Sin(rad), MathF.Cos(rad));
        var right = new Vector2(ahead.Y, -ahead.X);
        var running = HoldRun != RunByDefault;
        if (Holds(MotionCommands.WalkForward))
        {
            Xy += ahead * (running ? RunSpeed : WalkSpeed) * t;
        }

        if (Holds(MotionCommands.WalkBackwards))
        {
            Xy -= ahead * BackSpeed * t;
        }

        if (Holds(MotionCommands.SideStepRight))
        {
            Xy += right * SideSpeed * t;
        }

        if (Holds(MotionCommands.SideStepLeft))
        {
            Xy -= right * SideSpeed * t;
        }
    }

    private static string Name(uint command) => command switch
    {
        MotionCommands.WalkForward => "forward",
        MotionCommands.WalkBackwards => "back",
        MotionCommands.TurnRight => "turn-right",
        MotionCommands.TurnLeft => "turn-left",
        MotionCommands.SideStepRight => "side-right",
        MotionCommands.SideStepLeft => "side-left",
        _ => FormattableString.Invariant($"0x{command:X8}"),
    };
}

/// <summary>A path source that plans a straight line from the body to the goal (or fails, or a horizon), counting its plans and the scars it was given.</summary>
internal sealed class S5LinePaths : INavPathSource
{
    public NavMap? Map { get; set; }

    public PathPlanner? Planner => null;

    public int PlanCalls { get; private set; }

    public string? FailWith { get; set; }

    /// <summary>When set, the plan ends here as a horizon route (until <see cref="HorizonUntilVersion"/> is reached by the map's version).</summary>
    public Vector2? HorizonAt { get; set; }

    public int HorizonUntilVersion { get; set; } = int.MaxValue;

    public int ScarsSeen { get; private set; }

    public NavPath Plan(in CellFrame from, NavGoal goal, NavScars scars)
    {
        PlanCalls++;
        ScarsSeen = scars.All.Count;
        if (FailWith is not null)
        {
            return NavPath.Fail(FailWith);
        }

        var start = new NavWaypoint(new CellFrame(from.CellId, new AcFrame(from.Origin, Quaternion.Identity)), false);
        if (HorizonAt is { } h && (Map?.Version ?? 0) < HorizonUntilVersion)
        {
            var edge = NavPoint.At(goal.Point.LandblockId, h.X, h.Y);
            return new NavPath(NavPathStatus.Horizon, [start, new NavWaypoint(NavMap.ToCellFrame(edge), false)], null, []);
        }

        return new NavPath(NavPathStatus.Found, [start, new NavWaypoint(NavMap.ToCellFrame(goal.Point), false)], null, []);
    }
}

/// <summary>The scripted-body rig: an offline session's Action API with the body bound to <c>Move</c>, a runner, and the frame loop.</summary>
internal sealed class S5Rig
{
    public const double Dt = 1.0 / 30.0;

    public S5Rig(float x = 50f, float y = 50f, float heading = 0f, NavRunnerOptions? options = null, Action<ActionHarness>? before = null)
    {
        H = new ActionHarness(offline: true);
        Body = new S5KinematicBody(x, y, heading);
        before?.Invoke(H);
        H.Api.Move.Bind(Body, false);
        Runner = new NavRunner(H.Api.Move, Body, Paths, options: options);
    }

    public ActionHarness H { get; }

    public S5KinematicBody Body { get; }

    public S5LinePaths Paths { get; } = new();

    public NavRunner Runner { get; }

    public static NavGoal To(float x, float y) => NavGoal.ToPoint(NavPoint.At(MovementTestKit.Block.LandBlockId, x, y));

    /// <summary>One frame: the body moves by its keys, the runner steps, the session applies the queued intents.</summary>
    public NavRunState Frame()
    {
        Body.Advance(Dt);
        var state = Runner.Step(Body.Now);
        H.Tick(Dt);
        return state;
    }

    /// <summary>Frames until the run ends or <paramref name="seconds"/> pass.</summary>
    public NavRunState RunFor(double seconds)
    {
        var end = Body.Now + seconds;
        var state = Runner.State;
        while (Body.Now < end && !Runner.IsDone)
        {
            state = Frame();
        }

        return state;
    }
}
