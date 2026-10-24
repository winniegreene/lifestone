using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.World;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The movement channel and the automation movement intents (step ST11): sends carry the player's current sequences and
/// go out at once; sending while not present, or with nothing to send to, is counted and logged once; a send allocates
/// nothing; intents reach the bound controller with the tick's frame time, and fail loudly without one; the kill switch
/// releases what automation held.
/// </summary>
public sealed class MovementChannelTests
{
    private static readonly CellFrame At = new(0x860201AD, new AcFrame(new Vector3(12.3f, -28.5f, 0.005f), Quaternion.Identity));

    [Fact]
    public void Sends_carry_the_player_s_current_stamps()
    {
        var h = new ActionHarness();
        h.Present();

        // A server correction (force position 4) and a server-controlled motion (server control 5) move the stamps.
        h.R.Reducer.OnUpdatePosition(h.R.Context, ReducerHarness.Move(ActionHarness.Amy, 1, 3, 1, 4));
        h.R.Reducer.OnUpdateMotion(h.R.Context, new Lifestone.Net.Messages.UpdateMotionMsg(ActionHarness.Amy, 1, 2, 5, false, ReducerHarness.Motion()));
        var expected = new ObjSeqs(1, 5, 1, 4);
        Assert.Equal(expected, h.State.Self.Sequences);

        var raw = new RawMotionState { ForwardCommand = MotionCommands.WalkForward, CurrentHoldKey = HoldKey.Run, ForwardSpeed = 1.5f };
        raw.Actions.Add(new MotionActionNode(0x13000087, 1f, 5, true));
        h.Api.Movement.MoveToState(raw, At, contact: true, standingLongJump: false);
        h.Api.Movement.AutonomousPosition(At, contact: true);
        h.Api.Movement.Jump(0.5f, new Vector3(0, 1, 4), At);

        Assert.Equal([FakeSendKind.MoveToState, FakeSendKind.AutonomousPosition, FakeSendKind.Jump], h.Net.Kinds().Where(k => k is FakeSendKind.MoveToState or FakeSendKind.AutonomousPosition or FakeSendKind.Jump));
        Assert.All(h.Net.Sends.Where(s => s.Sequences is not null), s => Assert.Equal(expected, s.Sequences));
        var move = h.Net.Of(FakeSendKind.MoveToState)[0];
        Assert.Equal(new AcPosition(0x860201AD, At.Origin, Quaternion.Identity), move.Position);
        var wire = move.Motion!.Value;
        Assert.Equal((2u, MotionCommands.WalkForward, 1.5f, 0x8000003Du), (wire.CurrentHoldKey, wire.ForwardCommand, wire.ForwardSpeed, wire.CurrentStyle));
        Assert.Equal(new RawMotionAction(0x13000087, 5, true, 1f), Assert.Single(wire.Actions));
        Assert.Equal(0.5f, h.Net.Of(FakeSendKind.Jump)[0].Extent);
        Assert.Equal((1, 1, 1, 0), (h.Api.Channel.MoveToStateSent, h.Api.Channel.AutonomousPositionSent, h.Api.Channel.JumpSent, h.Api.Channel.SentWhileNotPresent));
    }

    [Fact]
    public void Sending_while_not_present_is_counted_and_logged_once_per_arrival()
    {
        var h = new ActionHarness();
        h.InWorld();
        for (var i = 0; i < 3; i++)
        {
            h.Api.Movement.AutonomousPosition(At, true);
        }

        Assert.Equal(3, h.Net.Of(FakeSendKind.AutonomousPosition).Count);
        Assert.Equal(3, h.Api.Channel.SentWhileNotPresent);
        Assert.Single(h.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.Contains("not present", StringComparison.Ordinal));

        // Present: no warning; the next arrival (a teleport) warns once more.
        h.R.Reducer.ApplyLocal(new Lifestone.Client.Session.Recording.RecordingNotice(Lifestone.Client.Session.State.SessionNotices.LoginCompleteSent, 0, null));
        h.Unhide();
        h.Api.Movement.AutonomousPosition(At, true);
        h.R.Reducer.OnPlayerTeleport(h.R.Context, new Lifestone.Net.Messages.PlayerTeleportMsg(2));
        h.Api.Movement.AutonomousPosition(At, true);
        h.Api.Movement.AutonomousPosition(At, true);
        Assert.Equal(5, h.Api.Channel.SentWhileNotPresent);
        Assert.Equal(2, h.Log.Entries.Count(e => e.Severity == LogSeverity.Warning && e.Message.Contains("not present", StringComparison.Ordinal)));
    }

    [Fact]
    public void Nothing_to_send_to_is_counted_and_logged_once()
    {
        var h = new ActionHarness();
        h.AtCharacterSelect();
        h.Api.Movement.AutonomousPosition(At, true);
        h.Api.Movement.MoveToState(new RawMotionState(), At, true, false);
        Assert.Empty(h.Net.Sends);
        Assert.Equal(2, h.Api.Channel.Unsent);
        var warning = Assert.Single(h.Log.Entries, e => e.Severity == LogSeverity.Warning);
        Assert.Contains("movement messages are not sent: not in the world", warning.Message, StringComparison.Ordinal);

        // The network session refusing a send counts the same way.
        var w = new ActionHarness();
        w.Present();
        w.Net.Throw = new InvalidOperationException("not in world");
        w.Api.Movement.Jump(1f, Vector3.UnitZ, At);
        Assert.Equal(1, w.Api.Channel.Unsent);
    }

    [Fact]
    public void Intents_without_a_controller_fail_loudly()
    {
        var h = new ActionHarness();
        h.Present();
        Assert.False(h.Api.Move.IsBound);
        var handles = new[]
        {
            h.Api.Move.Command(MotionCommands.WalkForward, true, ActionSource.Automation),
            h.Api.Move.HoldRun(true, ActionSource.Automation),
            h.Api.Move.HoldSidestep(true, ActionSource.Automation),
            h.Api.Move.ToggleAutoRun(ActionSource.Automation),
            h.Api.Move.ChargeJump(ActionSource.Automation),
            h.Api.Move.Jump(ActionSource.Automation),
        };
        Assert.All(handles, a => Assert.Equal(ActionOutcomeKind.NotImplemented, a.Outcome!.Value.Kind));
        Assert.Equal(6, h.Log.Entries.Count(e => e.Severity == LogSeverity.Error && e.Message.Contains("MoveIntent", StringComparison.Ordinal)));
        Assert.Equal(ActionOutcomeKind.Refused, h.Api.Move.Command(0, true, ActionSource.Automation).Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, h.Api.Move.Command(MotionCommands.WalkForward, true, ActionSource.Automation, float.NaN).Outcome!.Value.Kind);
    }

    [Fact]
    public void Intents_reach_the_bound_controller_in_the_tick_with_its_frame_time()
    {
        var h = new ActionHarness(offline: true);
        var controller = new ControllerSpy();
        h.Api.Move.Bind(controller, false);
        var walk = h.Api.Move.Command(MotionCommands.WalkForward, true, ActionSource.Automation, 0.5f);
        var charge = h.Api.Move.ChargeJump(ActionSource.Automation);
        Assert.Empty(controller.Calls);

        // An offline session's controller takes intents too: they need no server.
        h.Tick(2);
        Assert.Equal(["HandleCommand 0x45000005 True 0.5", "CommenceJump 2"], controller.Calls);
        Assert.Equal(ActionOutcomeKind.Local, walk.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Local, charge.Outcome!.Value.Kind);
        Assert.Null(walk.SentAt);
        Assert.Equal("MoveIntent Command 0x45000005 start speed=0.5", walk.Summary);

        // They are rate-limited like any action: 20 now, the rest later, none dropped.
        controller.Calls.Clear();
        for (var i = 0; i < 25; i++)
        {
            h.Api.Move.HoldRun(i % 2 == 0, ActionSource.Automation);
        }

        h.Tick(1);
        Assert.Equal((int)ActionPolicy.MovePerSecond, controller.Calls.Count);
        h.Tick(1);
        Assert.Equal(25, controller.Calls.Count);
    }

    [Fact]
    public void The_kill_switch_releases_what_automation_held()
    {
        var h = new ActionHarness(offline: true);
        var controller = new ControllerSpy();
        h.Api.Move.Bind(controller, false);
        h.Api.Move.Command(MotionCommands.WalkForward, true, ActionSource.Automation);
        h.Api.Move.Command(MotionCommands.TurnRight, true, ActionSource.Automation);
        h.Api.Move.Command(MotionCommands.TurnRight, false, ActionSource.Automation);
        h.Api.Move.HoldRun(true, ActionSource.Automation);
        h.Api.Move.ToggleAutoRun(ActionSource.Automation);
        h.Api.Move.Command(MotionCommands.SideStepLeft, true, ActionSource.Ui);
        h.Tick();
        controller.Calls.Clear();

        h.Api.KillSwitch = true;
        Assert.Equal(["HandleCommand 0x45000005 False 1", "SetHoldRun False", "ToggleAutoRun"], controller.Calls);
        Assert.Equal(ActionOutcomeKind.Refused, h.Api.Move.Jump(ActionSource.Automation).Outcome!.Value.Kind);
        Assert.Equal(ActionStatus.Queued, h.Api.Move.Jump(ActionSource.Ui).Status);
    }

    [Fact]
    public void The_wire_form_keeps_retail_defaults_and_hold_keys()
    {
        var wire = MovementChannel.ToWire(new RawMotionState());
        Assert.Equal(RawMotionStateWire.Default with { Actions = wire.Actions }, wire);
        Assert.True(wire.Actions.IsEmpty);
        Assert.Equal((uint)HoldKey.None, wire.CurrentHoldKey);
    }

    /// <summary>Records the calls a movement controller gets.</summary>
    private sealed class ControllerSpy : IPlayerMovementIntents
    {
        public List<string> Calls { get; } = [];

        public CellFrame Position => At;

        public bool OnGround => true;

        public void HandleCommand(uint command, bool start, float speed = 1f) =>
            Calls.Add(FormattableString.Invariant($"HandleCommand 0x{command:X8} {start} {speed}"));

        public void SetHoldRun(bool on) => Calls.Add("SetHoldRun " + on);

        public void SetHoldSidestep(bool on) => Calls.Add("SetHoldSidestep " + on);

        public void ToggleAutoRun() => Calls.Add("ToggleAutoRun");

        public void CommenceJump(double now) => Calls.Add(FormattableString.Invariant($"CommenceJump {now}"));

        public void DoJump(double now) => Calls.Add(FormattableString.Invariant($"DoJump {now}"));
    }
}
