using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Ui;
using Lifestone.Data.World;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Movement.MovementTestKit;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// P5a lane S4, the intents binding: <see cref="MoveIntents"/> is the one path for scripted movement (Bind with the run-by-default option,
/// Unbind, the outcomes), the run key means run whatever the option is, <see cref="ControllerNavBody"/> reads the controller for the runner,
/// and a person's movement key reaches the runner as <see cref="MoveIntents.HumanKey"/> (R6).
/// </summary>
public sealed class IntentsBindingTests(ITestOutputHelper output)
{
    private static readonly ushort NonCombat = MotionCommandTable.ToWire(MotionCommands.NonCombat);

    private static RetailActivation Press => new(true, 1f, RetailToggleType.Impulse, 0, 0);

    private static RetailActivation Release => new(false, 0f, RetailToggleType.Impulse, 0, 0);

    [Fact]
    public void Bind_carries_the_run_by_default_option_and_Unbind_returns_every_intent_to_NotImplemented()
    {
        var h = new ActionHarness(offline: true);
        var spy = new IntentSpy();
        Assert.False(h.Api.Move.IsBound);
        Assert.False(h.Api.Move.RunByDefault);

        h.Api.Move.Bind(spy, true);
        Assert.True(h.Api.Move.IsBound);
        Assert.True(h.Api.Move.RunByDefault);
        h.Api.Move.Bind(spy, false); // the same controller: only the option changes
        Assert.True(h.Api.Move.IsBound);
        Assert.False(h.Api.Move.RunByDefault);
        Assert.Contains(h.Log.Entries, e => e.Message == "movement intents: controller bound");

        Assert.False(h.Api.Move.Unbind(new IntentSpy())); // another controller's binding is not ours to remove
        Assert.True(h.Api.Move.IsBound);
        Assert.True(h.Api.Move.Unbind(spy));
        Assert.False(h.Api.Move.IsBound);
        Assert.False(h.Api.Move.RunByDefault);
        var unbound = h.Api.Move.SetRun(true, ActionSource.Automation);
        Assert.Equal(ActionOutcomeKind.NotImplemented, unbound.Outcome!.Value.Kind);
        Assert.Contains(h.Log.Entries, e => e.Severity == LogSeverity.Error && e.Message.Contains("MoveIntent", StringComparison.Ordinal));
        Assert.Empty(spy.Calls);

        h.Api.Move.Bind(spy, true);
        h.Api.Move.Unbind();
        Assert.False(h.Api.Move.IsBound);
        h.Api.Move.Bind(null, true); // a null controller never leaves an option behind
        Assert.False(h.Api.Move.RunByDefault);
    }

    [Fact]
    public void A_bound_intent_keeps_its_outcomes_Local_DryRun_Refused_and_Cancelled_by_the_kill_switch()
    {
        var h = new ActionHarness(offline: true);
        var spy = new IntentSpy();
        h.Api.Move.Bind(spy, false);

        var local = h.Api.Move.SetRun(true, ActionSource.Automation);
        h.Tick(1);
        Assert.Equal(ActionOutcomeKind.Local, local.Outcome!.Value.Kind);
        Assert.Equal(["SetHoldRun True"], spy.Calls);

        spy.Calls.Clear();
        h.Api.DryRun = true;
        var dry = h.Api.Move.Command(MotionCommands.WalkForward, true, ActionSource.Automation);
        h.Tick(1);
        Assert.Equal(ActionOutcomeKind.DryRun, dry.Outcome!.Value.Kind);
        Assert.Empty(spy.Calls);
        h.Api.DryRun = false;

        var queued = h.Api.Move.Command(MotionCommands.WalkForward, true, ActionSource.Automation); // waits in its lane for the tick
        h.Api.KillSwitch = true;
        Assert.Equal(ActionOutcomeKind.Cancelled, queued.Outcome!.Value.Kind);
        var refused = h.Api.Move.SetRun(true, ActionSource.Automation);
        Assert.Equal(ActionOutcomeKind.Refused, refused.Outcome!.Value.Kind);
        h.Tick(1);
        Assert.Equal(["SetHoldRun False"], spy.Calls); // only the kill switch's release of the run hold reached the controller: the cancelled walk never did
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void SetRun_holds_the_key_only_when_the_wish_differs_from_the_run_by_default_option(bool runByDefault, bool run, bool keyDown)
    {
        var h = new ActionHarness(offline: true);
        var spy = new IntentSpy();
        h.Api.Move.Bind(spy, runByDefault);

        h.Api.Move.SetRun(run, ActionSource.Automation);
        h.Tick(1);

        Assert.Equal([$"SetHoldRun {keyDown}"], spy.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Through_Move_a_run_runs_and_a_walk_walks_on_the_real_controller_whatever_the_option(bool runByDefault)
    {
        foreach (var run in new[] { true, false })
        {
            var h = new ActionHarness(offline: true);
            var (controller, _, _) = FlatController(x: 20f, y: 60f, options: new MovementOptions { RunByDefault = runByDefault });
            h.Api.Move.Bind(controller, controller.Options.RunByDefault);
            Run(controller, 0.5);

            h.Api.Move.SetRun(run, ActionSource.Automation);
            h.Api.Move.Command(MotionCommands.WalkForward, true, ActionSource.Automation);
            h.Tick(1);
            Run(controller, 1.0);

            output.WriteLine($"run by default {runByDefault}, wish {(run ? "run" : "walk")}: key {controller.HoldRun}, forward {controller.Motion.Interpreted.ForwardCommand:X8}");
            Assert.Equal(run != runByDefault, controller.HoldRun);
            Assert.Equal(run ? MotionCommands.RunForward : MotionCommands.WalkForward, controller.Motion.Interpreted.ForwardCommand);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_driver_following_the_bound_option_runs_a_far_goal_and_walks_the_last_two_metres_before_a_stop(bool runByDefault)
    {
        var h = new ActionHarness(offline: true);
        var (controller, _, _) = FlatController(x: 20f, y: 60f, options: new MovementOptions { RunByDefault = runByDefault });
        h.Api.Move.Bind(controller, runByDefault);
        Run(controller, 0.5);
        var goal = At(Block, 60f, 60f, 0f);
        var route = new ScriptedRoute("east", [new RouteGoal(goal, 0.5f, Stop: true, DwellSeconds: 0)]);
        var driver = new RouteDriver(controller, route, h.Api.Move);
        Assert.Equal(runByDefault, driver.RunByDefault);

        uint farForward = 0;
        uint nearForward = 0;
        var now = controller.Now;
        for (var i = 0; i < 60 * 20; i++)
        {
            now += 1.0 / 60.0;
            controller.Tick(now);
            if (driver.Tick(now) != RouteStatus.Running)
            {
                break;
            }

            var distance = controller.Position.GetOffset(goal).Length();
            if (distance is > 10f and < 25f)
            {
                farForward = controller.Motion.Interpreted.ForwardCommand;
            }
            else if (distance is > 0.9f and < 1.8f)
            {
                nearForward = controller.Motion.Interpreted.ForwardCommand;
            }
        }

        output.WriteLine($"run by default {runByDefault}: far {farForward:X8}, within 2 m {nearForward:X8}, {driver.Status}");
        Assert.Equal(MotionCommands.RunForward, farForward);
        Assert.Equal(MotionCommands.WalkForward, nearForward);
    }

    [Fact]
    public void ControllerNavBody_reads_the_controller_and_ServerDriving_is_control_by_the_server_with_a_move_to_under_way()
    {
        Assert.Throws<ArgumentNullException>(() => new ControllerNavBody(null!));
        var (controller, _, _) = FlatController(x: 30f, y: 30f);
        INavBody body = new ControllerNavBody(controller);
        Run(controller, 0.5);
        Assert.Equal(controller.Position, body.Position);
        Assert.Equal(controller.Position.CellId, body.CellId);
        Assert.NotEqual(0u, body.CellId);
        Assert.True(body.OnGround);
        Assert.Equal(controller.Now, body.Now);
        Assert.False(body.ServerDriving); // under the server's control, but no MoveTo

        controller.HandleCommand(MotionCommands.WalkForward, true);
        var before = body.Position;
        var time = body.Now;
        Run(controller, 1.0);
        Assert.True(body.Now > time);
        Assert.NotEqual(before, body.Position);
        Assert.False(body.ServerDriving); // the key took control

        // ACE's MoveTo for the player: the host hands it to the body and gives control to the server.
        var physics = controller.Player;
        var playerBody = new PhysicsPlayerBody(physics);
        var target = At(Block, physics.Position.Frame.Origin.X, physics.Position.Frame.Origin.Y + 8f, 0f);
        var parameters = new MovementParameters { Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.MoveTowards, DistanceToObject = 0.6f };
        var moveTo = new WireMovement(WireMovementType.MoveToPosition, NonCombat, false, false, default, MoveTo: new WireMoveTo(0, target, parameters, 1f));
        Assert.Equal(MovementUnpackResult.Applied, playerBody.ApplyServerMovement(moveTo, [], autonomous: false, out _));
        controller.LoseControlToServer();
        Run(controller, 0.2);
        Assert.True(body.ServerDriving);

        Run(controller, 8.0);
        Assert.False(body.ServerDriving); // the MoveTo ended
    }

    [Fact]
    public void A_movement_key_pressed_by_a_person_raises_HumanKey_and_a_release_or_a_stranger_does_not()
    {
        var spy = new IntentSpy();
        var bindings = new MovementBindings(spy, () => 0.0);
        var keys = new List<string>();
        bindings.HumanKey += keys.Add;

        Assert.True(bindings.OnInputAction(UiInputRouter.MovementMap, MovementBindings.MoveForward, Press));
        Assert.True(bindings.OnInputAction(UiInputRouter.MovementMap, MovementBindings.MoveForward, Release));
        Assert.True(bindings.OnInputAction(UiInputRouter.MovementMap, MovementBindings.RunKey, Press));
        Assert.True(bindings.OnInputAction(UiInputRouter.MovementMap, MovementBindings.Jump, Press));
        Assert.True(bindings.OnInputAction(UiInputRouter.MovementMap, MovementBindings.FirstEmoteAction, Press));
        Assert.False(bindings.OnInputAction(UiInputRouter.MovementMap, 0x1234, Press)); // not ours
        Assert.False(bindings.OnInputAction(5, MovementBindings.MoveForward, Press)); // a camera map

        Assert.Equal(["MoveForward", "RunKey", "Jump", "Emote_0x10000094"], keys);
        Assert.Equal("0x1234", MovementBindings.NameOf(0x1234));
    }

    [Fact]
    public void ReportHumanKey_reaches_the_runner_through_the_binding_and_automation_never_raises_it()
    {
        var h = new ActionHarness(offline: true);
        var spy = new IntentSpy();
        h.Api.Move.Bind(spy, false);
        var keys = new List<string>();
        h.Api.Move.HumanKey += keys.Add;

        h.Api.Move.Command(MotionCommands.WalkForward, true, ActionSource.Automation);
        h.Api.Move.SetRun(true, ActionSource.Automation);
        h.Tick(1);
        Assert.Empty(keys); // the runner's own keys are not the human's

        h.Api.Move.ReportHumanKey("MoveForward");
        Assert.Equal(["MoveForward"], keys);
        Assert.Throws<ArgumentException>(() => h.Api.Move.ReportHumanKey(string.Empty));
    }

    /// <summary>Records the calls a movement controller gets.</summary>
    private sealed class IntentSpy : IPlayerMovementIntents
    {
        public List<string> Calls { get; } = [];

        public CellFrame Position => default;

        public bool OnGround => true;

        public void HandleCommand(uint command, bool start, float speed = 1f) => Calls.Add(FormattableString.Invariant($"HandleCommand 0x{command:X8} {start} {speed}"));

        public void SetHoldRun(bool on) => Calls.Add("SetHoldRun " + on);

        public void SetHoldSidestep(bool on) => Calls.Add("SetHoldSidestep " + on);

        public void ToggleAutoRun() => Calls.Add("ToggleAutoRun");

        public void CommenceJump(double now) => Calls.Add("CommenceJump");

        public void DoJump(double now) => Calls.Add("DoJump");
    }
}
