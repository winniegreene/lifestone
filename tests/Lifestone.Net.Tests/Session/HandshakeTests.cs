using System.Buffers.Binary;
using System.Net;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.FakeAce;
using Lifestone.Net.Types;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// The session's login on the fake server (m2-plan §3.3 N5): LoginRequest, ConnectRequest, the delayed and repeated
/// ConnectResponse, the data check, and character select once both F7EA and F658 are in.
/// </summary>
public sealed class HandshakeTests
{
    [Fact]
    public void A_login_goes_connecting_handshaking_data_check_character_select()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();

        Assert.Equal(
            new[] { SessionPhase.Connecting, SessionPhase.Handshaking, SessionPhase.DataCheck, SessionPhase.CharacterSelect },
            rig.Sink.Phases);
        Assert.Single(rig.Sink.OfKind(SessionEventKind.ConnectRequestReceived));
        Assert.True(rig.Sink.OfKind(SessionEventKind.ServerTime).Any(), "the first bundle's TimeSync raised no ServerTime event");
        Assert.Single(rig.Sink.OfKind(SessionEventKind.DataCheckStarted));
        Assert.Single(rig.Sink.OfKind(SessionEventKind.DataCheckComplete));

        var list = Assert.IsType<CharacterListMsg>(rig.Session.CharacterList);
        Assert.Equal(SessionRig.Account, list.Account);
        Assert.Equal("Hero", list.Characters.Single().Name);
        Assert.False(rig.Machine.HoldsPassword);
        Assert.Equal(1, rig.SocketsOpened);
        Assert.Contains(rig.Log.At(NetLogLevel.Info), l => l.StartsWith("reached character select: account tester, 1 character(s), DDD p2072 c982 l994 ok", StringComparison.Ordinal));
    }

    [Fact]
    public void The_data_check_answer_is_the_codecs_bytes_and_F7E5_comes_first()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();

        var expected = new PacketWriter();
        DddCodec.WriteInterrogationResponse(expected, new DddIterations(2072, 982, 994, null), serverAllowsHighRes: false);
        var answer = Assert.Single(rig.ServerSession.HandledMessages, m => BitConverter.ToUInt32(m) == MessageOpcodes.DddInterrogationResponse);
        Assert.Equal(expected.ToArray(), answer);

        // ACE sends one bundle per 5 ms, its groups in order: the database queue's F7E5 is fragment 0. The UI queue's
        // F658 and F7E1 go out a tick later, and the F7EA answering the session's prompt F7E6 can go out before them
        // (the database queue comes first), so F7EA may precede the list; character select needs both either way.
        Assert.Equal(new uint[] { 0, 1, 2, 3 }, rig.Sink.Dispatched.Select(d => d.Sequence));
        Assert.Equal(MessageOpcodes.DddInterrogation, rig.Sink.Dispatched[0].Opcode);
        Assert.Equal(
            new[] { MessageOpcodes.CharacterList, MessageOpcodes.ServerName, MessageOpcodes.DddEnd, MessageOpcodes.DddInterrogation }.Order(),
            rig.Sink.Opcodes.Order());
        var opcodes = rig.Sink.Opcodes.ToList();
        Assert.True(opcodes.IndexOf(MessageOpcodes.CharacterList) < opcodes.IndexOf(MessageOpcodes.ServerName));

        // The tap sees the F7E6 with the fragment sequence the transport gave it, on the database queue.
        var tapped = Assert.Single(rig.Tap.Messages, m => m.Direction == MessageDirection.Outbound);
        Assert.Equal((1u, MessageQueue.Database), (tapped.Sequence, tapped.Queue));
        Assert.Equal(expected.ToArray(), tapped.Body);
    }

    [Theory]
    [InlineData(150, 0)]
    [InlineData(400, 1)]
    [InlineData(2000, 6)]
    public void Connect_responses_before_the_servers_database_write_are_ignored_and_resent(int dbWriteMilliseconds, int ignored)
    {
        var script = new FakeAceScript { DbWriteDelay = TimeSpan.FromMilliseconds(dbWriteMilliseconds) };
        script.AddAccount(SessionRig.Account, SessionRig.Password);
        using var rig = new SessionRig(script);
        TimeSpan? connectRequestAt = null;
        var responses = new List<TimeSpan>();
        rig.Harness.Network.Drop = (from, to, datagram) =>
        {
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(4));
            if ((flags & (uint)PacketHeaderFlags.ConnectRequest) != 0)
            {
                connectRequestAt ??= rig.Harness.Time.Elapsed;
            }
            else if (to.Port == rig.Harness.ServerEndPoint.Port + 1)
            {
                responses.Add(rig.Harness.Time.Elapsed);
            }

            return false;
        };

        rig.ToCharacterSelect();

        Assert.Equal(ignored, rig.Server.Counters.ConnectResponsesIgnored);
        Assert.Equal(ignored + 1, responses.Count);
        Assert.True(responses[0] - connectRequestAt >= TimeSpan.FromMilliseconds(200), $"the first ConnectResponse went {responses[0] - connectRequestAt} after the ConnectRequest");
        for (var i = 1; i < responses.Count; i++)
        {
            Assert.InRange(responses[i] - responses[i - 1], TimeSpan.FromSeconds(0.333), TimeSpan.FromSeconds(0.345));
        }

        Assert.Equal(0, rig.Server.Counters.BadHandshakes);
    }

    [Fact]
    public void A_lost_login_request_is_retried_from_a_fresh_socket_and_the_server_sees_no_bad_handshake()
    {
        using var rig = new SessionRig();
        var dropped = false;
        var loginSources = new List<IPEndPoint>();
        rig.Harness.Network.Drop = (from, to, datagram) =>
        {
            if ((BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(4)) & (uint)PacketHeaderFlags.LoginRequest) == 0)
            {
                return false;
            }

            loginSources.Add(from);
            if (dropped)
            {
                return false;
            }

            dropped = true;
            return true;
        };

        rig.ToCharacterSelect();

        Assert.Equal(2, loginSources.Count);
        Assert.NotEqual(loginSources[0].Port, loginSources[1].Port);
        Assert.Equal(2, rig.SocketsOpened);
        var retry = Assert.Single(rig.Sink.OfKind(SessionEventKind.RetryScheduled));
        Assert.Equal((2, 0u, TimeSpan.FromSeconds(3)), (retry.Attempt, retry.Code, retry.Delay));
        Assert.Equal(0, rig.Server.Counters.BadHandshakes);
        Assert.Single(rig.Server.History);
    }

    /// <summary>
    /// A lost ConnectRequest leaves the server a session that already holds the account (ACE sets it before the state
    /// that waits for the ConnectResponse), so the retry meets account_login_boots_in_use: both get 0x01, the old one is
    /// booted, and the third attempt gets in. D18's 3 retries leave one spare.
    /// </summary>
    [Fact]
    public void A_lost_connect_request_costs_two_retries_because_the_abandoned_session_holds_the_account()
    {
        using var rig = new SessionRig();
        var dropped = false;
        rig.Harness.Network.Drop = (from, to, datagram) =>
        {
            if (dropped || (BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(4)) & (uint)PacketHeaderFlags.ConnectRequest) == 0)
            {
                return false;
            }

            dropped = true;
            return true;
        };

        rig.ToCharacterSelect();

        Assert.Equal(3, rig.Server.History.Count);
        Assert.Equal(3, rig.Server.History.Select(h => h.EndPointC2S).Distinct().Count());
        Assert.Equal(
            new[] { (2, 0u), (3, (uint)CharacterErrorCode.Logon) },
            rig.Sink.OfKind(SessionEventKind.RetryScheduled).Select(r => (r.Attempt, r.Code)));
        Assert.Equal(FakeAceTermination.AccountLoggedIn, rig.Server.History[0].TerminationReason);
        Assert.Equal(FakeAceTermination.AccountInUse, rig.Server.History[1].TerminationReason);
        Assert.Null(rig.Server.History[2].TerminationReason);
        Assert.Equal(0, rig.Server.Counters.BadHandshakes);
    }

    [Fact]
    public void A_handshake_the_server_never_takes_fails_after_ten_seconds()
    {
        using var rig = new SessionRig();
        rig.Harness.Network.Drop = (from, to, datagram) => to.Port == rig.Harness.ServerEndPoint.Port + 1;
        rig.Login();

        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(12)));
        Assert.Equal(SessionFailureKind.HandshakeTimeout, rig.Session.Failure!.Kind);
        Assert.Equal(SessionPhase.Handshaking, rig.Session.Failure.Phase);
        Assert.InRange(rig.Harness.Time.Elapsed, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10.1));
        Assert.False(rig.Machine.HoldsPassword);
    }

    [Fact]
    public void No_server_at_all_fails_after_four_lost_connect_requests()
    {
        using var rig = new SessionRig();
        rig.Harness.ServerAlive = false;
        rig.Login();

        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(40)));
        Assert.Equal(SessionFailureKind.ConnectTimeout, rig.Session.Failure!.Kind);
        Assert.Equal(4, rig.SocketsOpened);
        Assert.Equal(3, rig.Sink.OfKind(SessionEventKind.RetryScheduled).Count());

        // 5 s for each of four attempts and 3 s before each of the three retries.
        Assert.InRange(rig.Harness.Time.Elapsed, TimeSpan.FromSeconds(29), TimeSpan.FromSeconds(29.1));
    }

    [Fact]
    public void Without_the_lost_connect_request_retry_one_attempt_is_made()
    {
        using var rig = new SessionRig(configure: o => o with { Retry = LoginRetryPolicy.Default with { RetryLostConnectRequest = false } });
        rig.Harness.ServerAlive = false;
        rig.Login();

        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(10)));
        Assert.Equal(SessionFailureKind.ConnectTimeout, rig.Session.Failure!.Kind);
        Assert.Equal(1, rig.SocketsOpened);
    }

    [Fact]
    public void Login_refuses_a_password_that_cannot_go_into_a_login_request_before_opening_anything()
    {
        using var rig = new SessionRig();
        using var credentials = new AcCredentials(SessionRig.Account, "pässword");

        var thrown = Assert.Throws<ArgumentException>(() => rig.Session.Login(credentials));
        Assert.DoesNotContain("pässword", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(0, rig.SocketsOpened);
        Assert.Equal(SessionPhase.Idle, rig.Session.Phase);
    }

    [Fact]
    public void Login_is_refused_twice_and_the_phase_change_waits_for_the_pump()
    {
        using var rig = new SessionRig();
        rig.Login();

        Assert.Equal(SessionPhase.Connecting, rig.Session.Phase);
        Assert.Empty(rig.Sink.Events);
        Assert.Throws<InvalidOperationException>(() => rig.Login());

        rig.Harness.Step();
        Assert.Equal(SessionEventKind.PhaseChanged, rig.Sink.Events[0].Kind);
        Assert.Equal((SessionPhase.Idle, SessionPhase.Connecting), (rig.Sink.Events[0].PreviousPhase, rig.Sink.Events[0].Phase));
    }

    [Fact]
    public void The_link_counts_what_crossed_the_wire()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        rig.RunFor(TimeSpan.FromSeconds(5));

        var link = rig.Session.Link;
        Assert.True(link.PacketsSent >= 4, $"{link}");  // LoginRequest, ConnectResponse, F7E6, acks
        Assert.True(link.PacketsReceived >= 5, $"{link}");
        Assert.Equal(0, link.ChecksumFailures);
        Assert.Equal(0, link.GapSkips);
        Assert.Equal(0, rig.Server.Counters.C2SCrcErrors);
    }
}
