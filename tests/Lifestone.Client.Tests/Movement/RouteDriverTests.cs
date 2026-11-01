using System.Numerics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.World;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Movement.MovementTestKit;
using Kind = Lifestone.Client.Tests.Movement.RecordingMovementSink.Kind;

namespace Lifestone.Client.Tests.Movement;

/// <summary>
/// <see cref="RouteDriver"/> (m2-plan PH9) on synthetic terrain: it steers the controller through a route's goals like a
/// player at the keys, stops and dwells, counts laps, and fails loudly when stuck or moved away.
/// </summary>
public sealed class RouteDriverTests(ITestOutputHelper output)
{
    [Fact]
    public void The_driver_checks_its_route()
    {
        var player = new FakeIntents();
        var goal = new RouteGoal(At(Block, 10f, 10f, 0f), 0.5f, Stop: true, DwellSeconds: 1.5);
        Assert.Throws<ArgumentNullException>(() => new RouteDriver(null!, new ScriptedRoute("r", [goal])));
        Assert.Throws<ArgumentNullException>(() => new RouteDriver(player, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RouteDriver(player, new ScriptedRoute("r", [goal], Laps: 0)));
        Assert.Throws<ArgumentException>(() => new RouteDriver(player, new ScriptedRoute("r", [])));
        var driver = new RouteDriver(player, new ScriptedRoute("r", [goal], Laps: 3));
        Assert.Null(driver.FailureReason);
        Assert.Equal(RouteStatus.Running, driver.Status);
    }

    [Fact]
    public void Two_laps_of_a_square_stop_at_the_corner_dwell_and_finish()
    {
        var (controller, sink, _) = FlatController(x: 60f, y: 60f, weenie: new TestPlayerWeenie { RunRate = 1.5f }); // RouteDriver, not the runner: this lap timing was tuned at run rate 1.5
        Run(controller, 0.5);
        var route = new ScriptedRoute(
            "square",
            [
                Goal(72f, 60f),
                Goal(72f, 72f),
                Goal(60f, 72f),
                new RouteGoal(At(Block, 60f, 60f, 0f), 0.5f, Stop: true, DwellSeconds: 1.5),
            ],
            Laps: 2);
        var driver = new RouteDriver(controller, route);
        var stops = new List<RouteStop>();
        driver.Stopped += stops.Add;
        var status = Drive(controller, driver, 60.0);

        output.WriteLine($"{status} at {controller.Now:F2} s; {sink.Count(Kind.MoveToState)} movements, {sink.Count(Kind.AutonomousPosition)} positions");
        Assert.Equal(RouteStatus.Done, status);
        Assert.Null(driver.FailureReason);
        Assert.Equal(2, driver.CompletedLaps);
        Assert.Equal(2, driver.StopsCompleted);
        Assert.Equal(2, stops.Count);
        Assert.All(stops, s => Assert.True(s.Position.GetOffset(At(Block, 60f, 60f, 0f)).Length() <= 0.6f, $"stopped at {s.Position}"));
        Assert.Equal(1, stops[1].Lap);
        Assert.True(stops[1].Time - stops[0].Time > 4.0);

        // Every key released at the end; the player stands still.
        Assert.Equal(Data.Animation.MotionCommands.Ready, controller.Motion.Raw.ForwardCommand);
        Assert.Equal(0u, controller.Motion.Raw.TurnCommand);
        Assert.Equal(Data.Motion.HoldKey.None, controller.Motion.Raw.CurrentHoldKey);
        Assert.InRange(controller.Stats.MaxGroundMovingGapSeconds, 0.9, 1.0 + (1.0 / 60.0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_driver_runs_whatever_the_run_by_default_option_is(bool runByDefault)
    {
        // The run key inverts the run-by-default option (CommandInterpreter::SetHoldRun): the driver holds it to run when the option is off and leaves it up when on.
        var (controller, _, _) = FlatController(x: 20f, y: 60f, options: new MovementOptions { RunByDefault = runByDefault });
        Run(controller, 0.5);
        var route = new ScriptedRoute("far-east", [new RouteGoal(At(Block, 170f, 60f, 0f), 0.5f, Stop: true, DwellSeconds: 0)]);
        var driver = new RouteDriver(controller, route, runByDefault);
        var now = controller.Now;
        var from = controller.Position;
        var started = now;
        while (now - started < 3.0)
        {
            now += 1.0 / 60.0;
            controller.Tick(now);
            Assert.Equal(RouteStatus.Running, driver.Tick(now));
        }

        var speed = controller.Position.GetOffset(from).Length() / (now - started);
        output.WriteLine($"run by default {runByDefault}: {speed:F1} m/s, forward {controller.Motion.Interpreted.ForwardCommand:X8}");
        Assert.Equal(MotionCommands.RunForward, controller.Motion.Interpreted.ForwardCommand);
        Assert.Equal(Data.Motion.HoldKey.Run, controller.Motion.Raw.CurrentHoldKey);
        Assert.True(speed > 4.0, $"a driver that runs covers more than a walk's 3.1 m/s, not {speed:F1}");
    }

    [Fact]
    public void A_driver_told_the_wrong_option_walks_a_player_who_runs_by_default_and_one_told_nothing_follows_the_controller()
    {
        // The hazard P5a S4 settled: a driver told false against a rig that runs by default holds the run key and so walks (the XOR). Told nothing, it reads the controller's own option and runs.
        var walkedForward = new Dictionary<string, uint>();
        foreach (var (name, make) in new (string, Func<PlayerMovementController, ScriptedRoute, RouteDriver>)[]
        {
            ("told false", (c, r) => new RouteDriver(c, r, false)),
            ("told nothing", (c, r) => new RouteDriver(c, r)),
        })
        {
            var (controller, _, _) = FlatController(x: 20f, y: 60f, options: new MovementOptions { RunByDefault = true });
            Run(controller, 0.5);
            var route = new ScriptedRoute("far-east", [new RouteGoal(At(Block, 170f, 60f, 0f), 0.5f, Stop: true, DwellSeconds: 0)]);
            var driver = make(controller, route);
            var now = controller.Now;
            var started = now;
            while (now - started < 3.0)
            {
                now += 1.0 / 60.0;
                controller.Tick(now);
                driver.Tick(now);
            }

            walkedForward[name] = controller.Motion.Interpreted.ForwardCommand;
        }

        Assert.Equal(MotionCommands.WalkForward, walkedForward["told false"]);
        Assert.Equal(MotionCommands.RunForward, walkedForward["told nothing"]);
    }

    [Fact]
    public void A_goal_to_the_east_is_reached_by_turning_right()
    {
        var (controller, _, _) = FlatController(x: 60f, y: 60f);
        var turns = new List<uint>();
        var intents = new RecordingIntents(controller, turns);
        var driver = new RouteDriver(intents, new ScriptedRoute("east", [new RouteGoal(At(Block, 70f, 60f, 0f), 0.5f, Stop: true, DwellSeconds: 0)]));
        Assert.Equal(RouteStatus.Done, Drive(controller, driver, 10.0));
        Assert.Equal(MotionCommands.TurnRight, turns[0]);
        Assert.InRange(RetailFrames.GetHeading(controller.Position.Frame), 80f, 100f);
    }

    [Fact]
    public void No_progress_for_three_seconds_is_stuck_and_names_the_place()
    {
        var player = new FakeIntents { Position = At(Block, 10f, 10f, 0f) };
        var driver = new RouteDriver(player, new ScriptedRoute("blocked", [new RouteGoal(At(Block, 10f, 30f, 0f), 0.5f, true, 1.5)]));
        var now = 0.0;
        RouteStatus status;
        do
        {
            now += 1.0 / 60.0;
            status = driver.Tick(now);
        }
        while (status == RouteStatus.Running && now < 10.0);

        Assert.Equal(RouteStatus.Stuck, status);
        Assert.InRange(now, 3.0, 3.1);
        output.WriteLine(driver.FailureReason);
        Assert.Contains("route 'blocked' lap 0 goal 0", driver.FailureReason, StringComparison.Ordinal);
        Assert.Contains("cell 0x40400001", driver.FailureReason, StringComparison.Ordinal);
        Assert.Contains("(10.00, 10.00, 0.00)", driver.FailureReason, StringComparison.Ordinal);
        Assert.False(player.Forward);
        Assert.Equal(RouteStatus.Stuck, driver.Tick(now + 1.0));
    }

    [Fact]
    public void A_jump_of_the_player_fails_the_route()
    {
        var player = new FakeIntents { Position = At(Block, 10f, 10f, 0f) };
        var driver = new RouteDriver(player, new ScriptedRoute("moved", [new RouteGoal(At(Block, 10f, 30f, 0f), 0.5f, true, 1.5)]));
        Assert.Equal(RouteStatus.Running, driver.Tick(0.1));
        player.Position = At(Block, 40f, 40f, 0f);
        Assert.Equal(RouteStatus.Failed, driver.Tick(0.2));
        Assert.Contains("teleport", driver.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void M5_7_D153_keys_let_go_by_a_lost_focus_are_pressed_again_and_the_walk_goes_on()
    {
        // The gate's Holtburg walk of 2026-10-31 13:04 UTC: 0.7 s into the walk every held key was let go (a lost window focus, CommandInterpreter::LoseKeyboardFocus) while the driver
        // still believed it held forward, so it stood still 2.95 m short of goal 0 and failed as stuck. Check, act, check (D141): the driver asks each frame and presses again.
        var (controller, _, _) = FlatController(x: 20f, y: 60f);
        Run(controller, 0.5);
        var route = new ScriptedRoute("far-east", [new RouteGoal(At(Block, 60f, 60f, 0f), 0.5f, Stop: true, DwellSeconds: 0)]);
        var driver = new RouteDriver(controller, route);
        var notices = new List<string>();
        driver.KeysPressedAgain += notices.Add;
        Assert.Equal(RouteStatus.Running, Drive(controller, driver, 0.7));
        Assert.True(controller.IsHeld(MotionCommands.WalkForward));

        controller.LoseKeyboardFocus(); // what the window's focus-out does: every held key let go behind the driver's back
        Assert.False(controller.IsHeld(MotionCommands.WalkForward));
        Assert.Equal(MotionCommands.Ready, controller.Motion.Raw.ForwardCommand);

        var status = Drive(controller, driver, 30.0);
        output.WriteLine($"{status} at {controller.Now:F2} s; {string.Join(" | ", notices)}; {driver.FailureReason}");
        Assert.Equal(RouteStatus.Done, status);
        Assert.Equal(1, driver.KeysPressedAgainCount);
        Assert.Contains("route 'far-east' lap 0 goal 0: forward found let go by something else, pressed again (#1)", Assert.Single(notices), StringComparison.Ordinal);
        Assert.True(controller.Position.GetOffset(At(Block, 60f, 60f, 0f)).Length() <= 0.6f, $"ended at {controller.Position}");
        Assert.Null(controller.IsHeld(MotionCommands.Ready)); // no list for Ready: the controller cannot say
    }

    private static RouteGoal Goal(float x, float y) => new(At(Block, x, y, 0f), 1.0f, Stop: false, DwellSeconds: 0);

    /// <summary>Ticks the controller and then the driver each frame until the route ends or <paramref name="limit"/> seconds pass.</summary>
    private static RouteStatus Drive(PlayerMovementController controller, RouteDriver driver, double limit)
    {
        var now = controller.Now;
        var end = now + limit;
        var status = RouteStatus.Running;
        while (status == RouteStatus.Running && now < end)
        {
            now += 1.0 / 60.0;
            controller.Tick(now);
            status = driver.Tick(now);
        }

        return status;
    }

    /// <summary>Forwards to a controller and records the turn keys pressed.</summary>
    private sealed class RecordingIntents(IPlayerMovementIntents inner, List<uint> turns) : IPlayerMovementIntents
    {
        public CellFrame Position => inner.Position;

        public bool OnGround => inner.OnGround;

        public void HandleCommand(uint command, bool start, float speed = 1f)
        {
            if (start && command is MotionCommands.TurnRight or MotionCommands.TurnLeft)
            {
                turns.Add(command);
            }

            inner.HandleCommand(command, start, speed);
        }

        public void SetHoldRun(bool on) => inner.SetHoldRun(on);

        public void SetHoldSidestep(bool on) => inner.SetHoldSidestep(on);

        public void ToggleAutoRun() => inner.ToggleAutoRun();

        public void CommenceJump(double now) => inner.CommenceJump(now);

        public void DoJump(double now) => inner.DoJump(now);
    }

    /// <summary>A player that goes nowhere, facing north.</summary>
    private sealed class FakeIntents : IPlayerMovementIntents
    {
        public CellFrame Position { get; set; } = new(0x40400001, new AcFrame(Vector3.Zero, Quaternion.Identity));

        public bool OnGround => true;

        public bool Forward { get; private set; }

        public void HandleCommand(uint command, bool start, float speed = 1f)
        {
            if (command == MotionCommands.WalkForward)
            {
                Forward = start;
            }
        }

        public void SetHoldRun(bool on)
        {
        }

        public void SetHoldSidestep(bool on)
        {
        }

        public void ToggleAutoRun()
        {
        }

        public void CommenceJump(double now)
        {
        }

        public void DoJump(double now)
        {
        }
    }
}
