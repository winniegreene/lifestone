using Lifestone.Client.PortalSpace;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>
/// Step PS11: the session bridge maps the session's events to the SmartBox's teleport flags, sends LoginComplete through
/// the Action API, and forwards a logoff request to the machine.
/// </summary>
public sealed class PortalSessionBridgeTests
{
    private const uint Self = 0x50000001;
    private const uint Other = 0x80000123;

    [Fact]
    public void It_subscribes_to_six_events_becomes_the_LoginComplete_sink_and_lets_go_on_dispose()
    {
        var (session, rig, bridge) = Connect();

        Assert.Same(bridge, rig.Director.LoginComplete);
        Assert.Equal(1, session.Bus.Subscribers<ObjectCreated>());
        Assert.Equal(1, session.Bus.Subscribers<PlayerTeleportAnnounced>());
        Assert.Equal(1, session.Bus.Subscribers<ObjectMoved>());
        Assert.Equal(1, session.Bus.Subscribers<ObjectStateChanged>());
        Assert.Equal(1, session.Bus.Subscribers<SessionPhaseChanged>());
        Assert.Equal(1, session.Bus.Subscribers<LogOffRequested>());

        bridge.Dispose();
        bridge.Dispose();

        Assert.Null(rig.Director.LoginComplete);
        Assert.Equal(0, session.Bus.Subscribers<ObjectCreated>());
        Assert.Equal(0, session.Bus.Subscribers<LogOffRequested>());
        session.Bus.Publish(new ObjectCreated(Self, IsSelf: true, InWorld: true, Recreated: false));
        Assert.False(rig.Progress.HasPlayer);
        Assert.Throws<ObjectDisposedException>(bridge.SendLoginComplete);
    }

    [Fact]
    public void The_players_own_creation_starts_the_update_and_other_objects_do_nothing()
    {
        var (session, rig, _) = Connect();

        session.Bus.Publish(new ObjectCreated(Other, IsSelf: false, InWorld: true, Recreated: false));
        Assert.False(rig.Progress.HasPlayer);

        session.Bus.Publish(new ObjectCreated(Self, IsSelf: true, InWorld: true, Recreated: false));
        Assert.True(rig.Progress.InProgress);
        Assert.False(rig.Progress.IsLocal);

        // A forced re-create of the player (F7DB) starts it again, as HandleUpdateObject → HandleCreateObject does.
        rig.Progress.Tick(notBlockingForCells: true);
        Assert.False(rig.Progress.InProgress);
        session.Bus.Publish(new ObjectCreated(Self, IsSelf: true, InWorld: true, Recreated: true));
        Assert.True(rig.Progress.InProgress);
    }

    [Fact]
    public void An_F751_waits_for_the_self_teleport_position_and_no_other_verdict_ends_it()
    {
        var (session, rig, _) = Connect();
        session.Bus.Publish(new ObjectCreated(Self, IsSelf: true, InWorld: true, Recreated: false));
        rig.Progress.Tick(notBlockingForCells: true);

        session.Bus.Publish(new PlayerTeleportAnnounced(7));
        Assert.True(rig.Progress.WaitingForTeleport);
        Assert.True(rig.Progress.InProgress);

        session.Bus.Publish(Moved(PositionVerdict.ForceBlip));
        session.Bus.Publish(Moved(PositionVerdict.SelfEcho));
        session.Bus.Publish(Moved(PositionVerdict.Remote));
        Assert.True(rig.Progress.WaitingForTeleport);

        session.Bus.Publish(Moved(PositionVerdict.SelfTeleport));
        Assert.False(rig.Progress.WaitingForTeleport);
        Assert.True(rig.Progress.InProgress); // until the destination is ready
    }

    [Fact]
    public void A_self_state_without_hidden_ends_the_wait_and_other_states_do_not()
    {
        var (session, rig, _) = Connect();
        session.Bus.Publish(new ObjectCreated(Self, IsSelf: true, InWorld: true, Recreated: false));
        session.Bus.Publish(new PlayerTeleportAnnounced(1));

        session.Bus.Publish(new ObjectStateChanged(Other, IsSelf: false, PhysicsState.Hidden, PhysicsState.None));
        session.Bus.Publish(new ObjectStateChanged(Self, IsSelf: true, PhysicsState.None, PhysicsState.Hidden | PhysicsState.Gravity));
        Assert.True(rig.Progress.WaitingForTeleport);

        session.Bus.Publish(new ObjectStateChanged(Self, IsSelf: true, PhysicsState.Hidden, PhysicsState.Gravity));
        Assert.False(rig.Progress.WaitingForTeleport);
    }

    [Fact]
    public void A_logoff_request_is_forwarded_with_its_frame_time()
    {
        var (session, rig, _) = Connect();

        session.Bus.Publish(new LogOffRequested(12.5, PlayerKillerDelay: false));

        Assert.True(rig.Machine.LogOff.Requested);
        Assert.Equal(15.5, rig.Machine.LogOff.DueTime);
    }

    [Theory]
    [InlineData(SessionPhase.LoggingOff, SessionPhase.CharacterSelect, true)]
    [InlineData(SessionPhase.InWorld, SessionPhase.Failed, true)]
    [InlineData(SessionPhase.InWorld, SessionPhase.Closing, true)]
    [InlineData(SessionPhase.EnteringWorld, SessionPhase.CharacterSelect, true)]
    [InlineData(SessionPhase.InWorld, SessionPhase.LoggingOff, false)]
    [InlineData(SessionPhase.EnteringWorld, SessionPhase.InWorld, false)]
    [InlineData(SessionPhase.CharacterSelect, SessionPhase.EnteringWorld, false)]
    [InlineData(SessionPhase.DataCheck, SessionPhase.CharacterSelect, false)]
    public void Leaving_the_world_removes_the_player_and_resets_portal_space(SessionPhase from, SessionPhase to, bool resets)
    {
        var (session, rig, bridge) = Connect();
        session.Bus.Publish(new ObjectCreated(Self, IsSelf: true, InWorld: true, Recreated: false));
        rig.Step();
        Assert.Equal(TeleportAnimState.Tunnel, rig.Machine.State);

        session.Bus.Publish(new SessionPhaseChanged(from, to, null));

        Assert.Equal(resets, !rig.Progress.HasPlayer);
        Assert.Equal(resets, rig.Machine.State == TeleportAnimState.Off);
        Assert.Equal(resets ? 1 : 0, bridge.WorldExits);
    }

    [Fact]
    public void An_online_login_sends_one_LoginComplete_through_the_action_api_when_state_6_ends()
    {
        var (session, rig, bridge) = Connect();
        rig.RunFor(0.5);

        rig.BeforeNextStep = () => session.Bus.Publish(new ObjectCreated(Self, IsSelf: true, InWorld: true, Recreated: false));
        rig.RunUntil(f => (f.Cues & PortalCues.SendLoginComplete) != 0, 20.0);
        rig.RunFor(5.0);

        var sent = Assert.Single(session.FakeActions.LoginCompletes);
        Assert.Equal(ActionSource.Client, sent.Source);
        Assert.Equal(ActionKind.LoginComplete, bridge.LastLoginComplete!.Kind);
        Assert.Equal(1, rig.Director.LoginCompletesSent);
    }

    [Fact]
    public void An_online_teleport_runs_from_the_F751_to_its_own_LoginComplete()
    {
        var (session, rig, _) = Connect();
        rig.BeforeNextStep = () => session.Bus.Publish(new ObjectCreated(Self, IsSelf: true, InWorld: true, Recreated: false));
        rig.RunUntil(f => (f.Cues & PortalCues.SendLoginComplete) != 0, 20.0);
        rig.RunFor(3.0);

        rig.BeforeNextStep = () => session.Bus.Publish(new PlayerTeleportAnnounced(1));
        var begin = rig.Step();
        rig.RunFor(2.0);
        Assert.Equal(TeleportAnimState.InTunnel, rig.Last.State); // waiting for the position
        rig.BeforeNextStep = () => session.Bus.Publish(Moved(PositionVerdict.SelfTeleport));
        rig.RunUntil(f => (f.Cues & PortalCues.SendLoginComplete) != 0, 20.0);

        Assert.Equal(TeleportAnimState.Tunnel, begin.State);
        Assert.Equal(2, session.FakeActions.LoginCompletes.Count);
    }

    private static ObjectMoved Moved(PositionVerdict verdict) =>
        new(verdict == PositionVerdict.Remote ? Other : Self, default(CellFrame), verdict, verdict == PositionVerdict.SelfTeleport, true, null, null);

    private static (FakeSession Session, DirectorRig Rig, PortalSessionBridge Bridge) Connect()
    {
        var session = new FakeSession();
        var rig = new DirectorRig(withLoginComplete: false);
        var bridge = new PortalSessionBridge(session, rig.Director);
        return (session, rig, bridge);
    }
}
