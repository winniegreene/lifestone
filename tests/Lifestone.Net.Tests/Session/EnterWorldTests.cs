using System.Buffers.Binary;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.FakeAce;
using Lifestone.Net.Tests.Support;
using Lifestone.Net.Types;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// Entering the world (m2-plan §3.3 N6; docs/protocol.md §4.3): F7C8, F7DF, F657 with the list's account string,
/// PlayerCreate; the 0x0D retry; LoginComplete only when asked for, and a warning when it is sent twice.
/// </summary>
public sealed class EnterWorldTests
{
    [Fact]
    public void Enter_world_sends_the_request_then_the_character_with_the_lists_account_on_server_ready()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();

        var ticket = rig.Session.EnterWorld(SessionRig.HeroGuid);
        Assert.Equal(SessionPhase.EnteringWorld, rig.Session.Phase);
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.InWorld, TimeSpan.FromSeconds(2)));

        Assert.Equal(new[] { MessageOpcodes.DddInterrogationResponse, MessageOpcodes.CharacterEnterWorldRequest, MessageOpcodes.CharacterEnterWorld }, rig.Tap.OutboundOpcodes);
        var request = rig.Tap.Messages.Single(m => m.Direction == MessageDirection.Outbound && m.Sequence == ticket.MessageSequence);
        Assert.Equal((MessageOpcodes.CharacterEnterWorldRequest, MessageQueue.Login), (BitConverter.ToUInt32(request.Body), request.Queue));
        var (guid, account, unread) = AceClientReaders.ReadCharacterEnterWorld(rig.Tap.Outbound.Last());
        Assert.Equal((SessionRig.HeroGuid, SessionRig.Account, 0), (guid, account, unread));

        Assert.Equal(SessionRig.HeroGuid, rig.Session.PlayerGuid);
        Assert.Equal(new[] { SessionPhase.EnteringWorld, SessionPhase.InWorld }, rig.Sink.Phases.TakeLast(2));
        Assert.Equal(FakeAceSessionState.WorldConnected, rig.ServerSession.State);
    }

    [Fact]
    public void The_player_arrives_hidden_after_PlayerCreate()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        var opcodes = rig.Sink.Opcodes.ToList();
        var playerCreate = opcodes.IndexOf(MessageOpcodes.PlayerCreate);
        Assert.True(playerCreate >= 0);
        Assert.Equal(MessageOpcodes.ObjectCreate, opcodes[playerCreate + 1]);

        var self = rig.Tap.Messages.First(m => m.Direction == MessageDirection.Inbound && BitConverter.ToUInt32(m.Body) == MessageOpcodes.ObjectCreate).Body;
        Assert.Equal(SessionRig.HeroGuid, BinaryPrimitives.ReadUInt32LittleEndian(self.AsSpan(4)));
        var physicsState = (PhysicsState)BinaryPrimitives.ReadUInt32LittleEndian(self.AsSpan(4 + 4 + 4 + 4));
        Assert.True(physicsState.HasFlag(PhysicsState.Hidden));
    }

    [Fact]
    public void An_auto_created_accounts_lower_cased_name_is_echoed_verbatim()
    {
        using var rig = new SessionRig(new FakeAceScript());
        rig.ToCharacterSelect("NewPlayer", "fresh-password-1");
        Assert.Equal("newplayer", rig.Session.CharacterList!.Account);

        rig.Session.CreateCharacter(CharacterOpsTests.Request("Freshling"));
        Assert.True(rig.RunUntil(() => rig.Sink.CharGenResponses.Count == 1, TimeSpan.FromSeconds(2)));
        rig.Session.EnterWorld(rig.Sink.CharGenResponses[0].Guid);
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.InWorld, TimeSpan.FromSeconds(2)));

        Assert.Equal("newplayer", AceClientReaders.ReadCharacterCreate(rig.Tap.Outbound.Single(b => BitConverter.ToUInt32(b) == MessageOpcodes.CharacterCreate)).Account);
        Assert.Equal("newplayer", AceClientReaders.ReadCharacterEnterWorld(rig.Tap.Outbound.Last()).Account);
    }

    [Fact]
    public void LoginComplete_unhides_the_player_and_a_second_one_logs_a_warning()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        Assert.True(rig.ServerSession.Teleporting);

        var ticket = rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromMilliseconds(100));
        Assert.False(rig.ServerSession.Teleporting);
        Assert.Contains(MessageOpcodes.SetState, rig.Sink.Opcodes);
        var sent = rig.Tap.Messages.Single(m => m.Sequence == ticket.MessageSequence && m.Direction == MessageDirection.Outbound);
        Assert.Equal(MessageQueue.Weenie, sent.Queue);
        var (sequence, actionType, payload) = AceClientReaders.ReadGameAction(sent.Body);
        payload.Dispose();
        Assert.Equal((1u, GameActionTypes.LoginComplete), (sequence, actionType));
        Assert.Empty(rig.Log.At(NetLogLevel.Warning));

        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromMilliseconds(100));
        Assert.Single(rig.Log.At(NetLogLevel.Warning), l => l.Contains("LoginComplete sent again", StringComparison.Ordinal));
        Assert.Single(rig.Sink.OfKind(SessionEventKind.Warning), e => e.Warning == SessionWarning.LoginCompleteRepeated);
        Assert.Equal(2, rig.HandledOpcodes().Count(o => o == MessageOpcodes.GameAction));
    }

    [Fact]
    public void The_action_sequence_restarts_from_one_at_each_world_entry()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.Session.SendLoginComplete();
        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromMilliseconds(50));
        rig.Session.LogOff();
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.CharacterSelect, TimeSpan.FromSeconds(7)));
        rig.Session.EnterWorld(SessionRig.HeroGuid);
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.InWorld, TimeSpan.FromSeconds(2)));
        rig.Session.SendLoginComplete();

        var sequences = rig.Tap.Outbound.Where(b => BitConverter.ToUInt32(b) == MessageOpcodes.GameAction).Select(b => BitConverter.ToUInt32(b, 4));
        Assert.Equal(new uint[] { 1, 2, 1 }, sequences);
    }

    [Fact]
    public void LoginComplete_is_never_sent_by_the_session_itself()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.RunFor(TimeSpan.FromSeconds(10));

        Assert.DoesNotContain(MessageOpcodes.GameAction, rig.Tap.OutboundOpcodes);
        Assert.True(rig.ServerSession.Teleporting);
        Assert.Throws<InvalidOperationException>(() => rig.Session.EnterWorld(SessionRig.HeroGuid));
    }

    [Fact]
    public void LoginComplete_needs_the_world()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        Assert.Throws<InvalidOperationException>(() => rig.Session.SendLoginComplete());
    }

    [Fact]
    public void A_character_still_in_the_world_is_retried_and_entered()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        rig.Script.EnterWorldErrors.Enqueue((uint)CharacterErrorCode.EnterGameCharacterInWorld);

        rig.Session.EnterWorld(SessionRig.HeroGuid);
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.InWorld, TimeSpan.FromSeconds(5)));

        var retry = Assert.Single(rig.Sink.OfKind(SessionEventKind.RetryScheduled));
        Assert.Equal((2, 0x0Du, SessionPhase.EnteringWorld), (retry.Attempt, retry.Code, retry.Phase));
        Assert.Equal(2, rig.Tap.OutboundOpcodes.Count(o => o == MessageOpcodes.CharacterEnterWorldRequest));
        Assert.Equal(2, rig.Tap.OutboundOpcodes.Count(o => o == MessageOpcodes.CharacterEnterWorld));
        Assert.Empty(rig.Sink.OfKind(SessionEventKind.CharacterErrorReceived));
        Assert.Equal(1, rig.SocketsOpened);
    }

    [Fact]
    public void Another_enter_world_error_returns_to_character_select()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        rig.Script.EnterWorldErrors.Enqueue((uint)CharacterErrorCode.EnterGameCharacterLocked);

        rig.Session.EnterWorld(SessionRig.HeroGuid);
        Assert.True(rig.RunUntil(() => rig.Sink.CharacterErrors.Count == 1, TimeSpan.FromSeconds(2)));

        Assert.Equal(SessionPhase.CharacterSelect, rig.Session.Phase);
        var error = Assert.Single(rig.Sink.OfKind(SessionEventKind.CharacterErrorReceived));
        Assert.Equal((0x17u, SessionPhase.CharacterSelect), (error.Code, error.Phase));
        Assert.Null(rig.Session.PlayerGuid);

        // Character select works on: the second try enters.
        rig.Session.EnterWorld(SessionRig.HeroGuid);
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.InWorld, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void An_enter_world_with_no_server_ready_fails_after_thirty_seconds()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        rig.Harness.Network.Drop = (from, to, datagram) =>
            from.Port is 9000 or 9001 && LoginFailureTests.ServerOpcodes(datagram, from.Port).Contains(MessageOpcodes.CharacterEnterWorldServerReady);
        var start = rig.Harness.Time.Elapsed;

        rig.Session.EnterWorld(SessionRig.HeroGuid);
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(35)));

        Assert.Equal((SessionFailureKind.EnterWorldTimeout, SessionPhase.EnteringWorld), (rig.Session.Failure!.Kind, rig.Session.Failure.Phase));
        Assert.InRange(rig.Harness.Time.Elapsed - start, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30.1));
    }
}
