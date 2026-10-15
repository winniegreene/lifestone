using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.FakeAce;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// Leaving the world (m2-plan §3.3 N6): logoff back to character select after ACE's 6 s, and quit (F653, then a
/// cleartext Disconnect that ACE terminates on at once), so an immediate new login finds the account free.
/// </summary>
public sealed class LogoffQuitTests
{
    [Fact]
    public void Logoff_returns_to_character_select_six_seconds_later_with_a_new_list()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        var lists = rig.Sink.CharacterLists.Count;

        rig.Session.LogOff();
        Assert.Equal(SessionPhase.LoggingOff, rig.Session.Phase);
        Assert.Equal(SessionRig.HeroGuid, BitConverter.ToUInt32(rig.Tap.Outbound.Last(), 4));

        rig.RunFor(TimeSpan.FromSeconds(5.9));
        Assert.Equal(SessionPhase.LoggingOff, rig.Session.Phase);
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.CharacterSelect, TimeSpan.FromSeconds(0.5)));

        Assert.Single(rig.Sink.OfKind(SessionEventKind.LoggedOff));
        Assert.Equal(lists + 1, rig.Sink.CharacterLists.Count);
        Assert.Null(rig.Session.PlayerGuid);
        Assert.Equal(new[] { MessageOpcodes.CharacterLogOff, MessageOpcodes.CharacterList }, rig.Sink.Opcodes.Where(o => o is MessageOpcodes.CharacterLogOff or MessageOpcodes.CharacterList).TakeLast(2));
        Assert.Equal(FakeAceSessionState.AuthConnected, rig.ServerSession.State);

        // Back in character select, the same character can enter again.
        rig.Session.EnterWorld(SessionRig.HeroGuid);
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.InWorld, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void A_logoff_the_server_never_finishes_fails_after_thirty_seconds()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.Harness.Network.Drop = (from, to, datagram) =>
            from.Port is 9000 or 9001 && LoginFailureTests.ServerOpcodes(datagram, from.Port).Contains(MessageOpcodes.CharacterLogOff);

        rig.Session.LogOff();
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(31)));
        Assert.Equal((SessionFailureKind.LogoffTimeout, SessionPhase.LoggingOff), (rig.Session.Failure!.Kind, rig.Session.Failure.Phase));
        Assert.Contains("never came", rig.Session.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LogOff_needs_the_world()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        Assert.Throws<InvalidOperationException>(() => rig.Session.LogOff());
    }

    [Fact]
    public void Quit_in_the_world_sends_F653_then_a_disconnect_and_the_server_drops_the_session()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        var server = rig.ServerSession;

        rig.Session.Quit();
        Assert.Equal(SessionPhase.Closed, rig.Session.Phase);
        Assert.Null(rig.Machine.Connection);
        rig.Harness.Step();

        Assert.Equal(MessageOpcodes.CharacterLogOff, BitConverter.ToUInt32(server.HandledMessages[^1]));
        Assert.Equal(FakeAceTermination.PacketHeaderDisconnect, server.TerminationReason);
        Assert.Equal(new[] { SessionPhase.Closing, SessionPhase.Closed }, rig.Sink.Phases.TakeLast(2));
        Assert.Equal(default, rig.Session.Pump(TimeSpan.Zero));
    }

    [Fact]
    public void Quit_in_character_select_sends_the_disconnect_alone()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        var server = rig.ServerSession;

        rig.Session.Quit();
        rig.Harness.Step();

        Assert.DoesNotContain(MessageOpcodes.CharacterLogOff, rig.Tap.OutboundOpcodes);
        Assert.Equal(FakeAceTermination.PacketHeaderDisconnect, server.TerminationReason);
    }

    [Fact]
    public void Quit_while_idle_or_twice_is_harmless()
    {
        using var rig = new SessionRig();
        rig.Session.Quit();
        Assert.Equal(SessionPhase.Closed, rig.Session.Phase);
        rig.Session.Quit();
        Assert.Equal(0, rig.SocketsOpened);
        Assert.Empty(rig.Harness.Network.Sent);
    }

    [Fact]
    public void A_new_login_two_seconds_after_quit_finds_the_account_free()
    {
        using var first = new SessionRig();
        first.ToWorld();
        first.Session.Quit();
        first.RunFor(TimeSpan.FromSeconds(2.1));

        // A second client on the same server (the first rig's harness keeps the server ticking).
        using var second = SessionOn(first);
        second.ToCharacterSelect();

        Assert.Empty(second.Sink.OfKind(SessionEventKind.RetryScheduled));
        Assert.Empty(second.Sink.CharacterErrors);
    }

    [Fact]
    public void A_new_login_at_once_after_quit_meets_the_terminating_session_and_gets_in_on_the_retry()
    {
        // ACE keeps a terminating session in its map for 2 s, and account_login_boots_in_use answers a login that finds
        // it with 0x01 (AuthenticationHandler); the retry policy's next attempt, 3 s later, finds the account free.
        using var first = new SessionRig();
        first.ToWorld();
        first.Session.Quit();

        using var second = SessionOn(first);
        second.ToCharacterSelect();

        var retry = Assert.Single(second.Sink.OfKind(SessionEventKind.RetryScheduled));
        Assert.Equal((uint)CharacterErrorCode.Logon, retry.Code);
    }

    [Fact]
    public void Disposing_a_connected_session_disconnects_it()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        var server = rig.ServerSession;

        rig.Pumping = false;
        rig.Session.Dispose();
        rig.Harness.Step();

        Assert.Equal(FakeAceTermination.PacketHeaderDisconnect, server.TerminationReason);
        Assert.Equal(SessionPhase.Closed, rig.Session.Phase);
    }

    /// <summary>A second session on <paramref name="rig"/>'s server and clock, pumped by the same harness.</summary>
    private static SecondSession SessionOn(SessionRig rig) => new(rig);

    private sealed class SecondSession : IDisposable
    {
        private readonly SessionRig _rig;

        public SecondSession(SessionRig rig)
        {
            _rig = rig;
            Session = AcSession.Create(rig.Options with { Log = null }, Sink);
            rig.Harness.AddParticipant(() => Session.Pump(TimeSpan.MaxValue));
        }

        public IAcSession Session { get; }

        public RecordingSink Sink { get; } = new();

        public void ToCharacterSelect()
        {
            using var credentials = new AcCredentials(SessionRig.Account, SessionRig.Password);
            Session.Login(credentials);
            Assert.True(_rig.RunUntil(() => Session.Phase == SessionPhase.CharacterSelect, TimeSpan.FromSeconds(10)), $"stuck in {Session.Phase} ({Session.Failure})");
        }

        public void Dispose() => Session.Dispose();
    }
}
