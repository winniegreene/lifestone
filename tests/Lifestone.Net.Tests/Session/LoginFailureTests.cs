using System.Buffers.Binary;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.FakeAce;
using Lifestone.Net.Tests.Support;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// The ways a login ends without character select (m2-plan §3.3 N5): boots (wrong password, wrong client version),
/// dat mismatches (older or newer dats, DDD_BeginDDD), a ban, an unknown account, and a graceful server shutdown.
/// </summary>
public sealed class LoginFailureTests
{
    [Fact]
    public void A_wrong_password_is_booted_with_the_servers_reason()
    {
        using var rig = new SessionRig();
        rig.Login(password: "not-the-password");

        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(2)));
        var failure = rig.Session.Failure!;
        Assert.Equal(SessionFailureKind.Booted, failure.Kind);
        Assert.Equal(" because the password was not correct (fake)", rig.Sink.OfKind(SessionEventKind.Booted).Single().Text);
        Assert.EndsWith(" because the password was not correct (fake)", failure.Message, StringComparison.Ordinal);
        Assert.Single(rig.Sink.Boots);
        Assert.Empty(rig.Sink.OfKind(SessionEventKind.RetryScheduled));
        Assert.Equal(SessionEventKind.Failed, rig.Sink.Events[^1].Kind);
        Assert.False(rig.Machine.HoldsPassword);
        Assert.Null(rig.Machine.Connection);
    }

    [Fact]
    public void A_wrong_client_version_is_booted_before_any_connect_request()
    {
        using var rig = new SessionRig(configure: o => o with { ClientVersion = "1801" });
        rig.Login();

        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(2)));
        Assert.Equal(SessionFailureKind.Booted, rig.Session.Failure!.Kind);
        Assert.Equal(SessionPhase.Connecting, rig.Session.Failure.Phase);
        Assert.Contains(rig.Log.At(NetLogLevel.Warning), l => l.Contains("without a checksum", StringComparison.Ordinal));
        Assert.Empty(rig.Sink.OfKind(SessionEventKind.ConnectRequestReceived));
    }

    [Theory]
    [InlineData(2071, "older")]
    [InlineData(2073, "newer")]
    public void Dats_that_differ_from_the_servers_fail_the_data_check(int portal, string which)
    {
        using var rig = new SessionRig(configure: o => o with { Iterations = new DddIterations(portal, 982, 994, null) });
        rig.Login();

        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(2)));
        var failure = rig.Session.Failure!;
        Assert.Equal(SessionFailureKind.DataCheckMismatch, failure.Kind);
        Assert.Equal(SessionPhase.DataCheck, failure.Phase);
        Assert.Contains($"p{portal} c982 l994", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"dat files are {which} than the server's", failure.Message, StringComparison.Ordinal);
        Assert.Single(rig.Sink.OfKind(SessionEventKind.Booted));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DDD_BeginDDD_fails_the_data_check_whether_or_not_it_parses(bool malformed)
    {
        using var rig = new SessionRig();
        rig.Harness.Network.Drop = (from, to, datagram) => ClientOpcode(datagram) == MessageOpcodes.DddInterrogationResponse;
        rig.Sink.OnAnyMessage = opcode =>
        {
            if (opcode == MessageOpcodes.DddInterrogation)
            {
                var begin = AceWireWriters.DddBegin(1000, [new AceMissingIteration(0, 1, 2073, [0x0A000001], [])]);
                rig.ServerSession.EnqueueSend((malformed ? begin[..^2] : begin, FakeAceSession.DatabaseQueue));
            }
        };
        rig.Login();

        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(2)));
        Assert.Equal(SessionFailureKind.DataCheckMismatch, rig.Session.Failure!.Kind);
        Assert.Contains("DDD_BeginDDD", rig.Session.Failure.Message, StringComparison.Ordinal);
        Assert.Equal(malformed ? 1 : 0, rig.Sink.Malformed.Count);
    }

    [Fact]
    public void A_banned_account_fails_with_the_ban()
    {
        var script = SessionRig.DefaultScript();
        var harnessStart = Lifestone.Tests.Shared.ManualTimeProvider.DefaultStart;
        script.Accounts[SessionRig.Account].BanExpires = harnessStart.AddHours(2);
        script.Accounts[SessionRig.Account].BanReason = "testing";
        using var rig = new SessionRig(script);
        rig.Login();

        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(2)));
        Assert.Equal(SessionFailureKind.AccountBanned, rig.Session.Failure!.Kind);
        Assert.Contains("banned for another", rig.Session.Failure.Message, StringComparison.Ordinal);
        Assert.EndsWith(" - testing", rig.Session.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_account_without_auto_creation_fails_with_its_character_error_at_once()
    {
        using var rig = new SessionRig(new FakeAceScript { AllowAutoAccountCreation = false });
        rig.Login("nobody");

        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(2)));
        Assert.Equal((SessionFailureKind.CharacterError, 0x0Au), (rig.Session.Failure!.Kind, rig.Session.Failure.Code));
        Assert.Empty(rig.Sink.OfKind(SessionEventKind.RetryScheduled));
        Assert.Equal(1, rig.SocketsOpened);
    }

    [Fact]
    public void A_graceful_shutdown_fails_the_session_in_the_pump_that_received_it()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        int? errorPump = null, failedPump = null;
        rig.Sink.OnAnyMessage = opcode =>
        {
            if (opcode == MessageOpcodes.CharacterError)
            {
                errorPump = rig.PumpResults.Count;
            }
        };
        rig.Sink.OnAnyEvent = e =>
        {
            if (e.Kind == SessionEventKind.Failed)
            {
                failedPump = rig.PumpResults.Count;
            }
        };

        rig.Server.Shutdown();
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(1)));

        Assert.Equal((SessionFailureKind.CharacterError, 0x04u), (rig.Session.Failure!.Kind, rig.Session.Failure.Code));
        Assert.NotNull(errorPump);
        Assert.Equal(errorPump, failedPump);
    }

    [Fact]
    public void A_boot_in_the_world_fails_the_session()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.ServerSession.EnqueueSend((AceWireWriters.AccountBoot(" because a test said so"), FakeAceSession.UiQueue));

        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(1)));
        Assert.Equal((SessionFailureKind.Booted, SessionPhase.InWorld), (rig.Session.Failure!.Kind, rig.Session.Failure.Phase));
        Assert.Null(rig.Session.PlayerGuid);
    }

    [Fact]
    public void A_failed_session_pumps_nothing_and_refuses_new_work()
    {
        using var rig = new SessionRig();
        rig.Login(password: "not-the-password");
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(2)));

        Assert.Equal(default, rig.Session.Pump(TimeSpan.Zero));
        Assert.Throws<InvalidOperationException>(() => rig.Session.EnterWorld(SessionRig.HeroGuid));
        Assert.Throws<InvalidOperationException>(() => rig.Login());
        rig.Session.Quit();
        Assert.Equal(SessionPhase.Failed, rig.Session.Phase);
    }

    /// <summary>The opcode of the first message in a client data packet (no optional section), or 0.</summary>
    internal static uint ClientOpcode(byte[] datagram)
    {
        const uint OptionalSections = 0x0FFFFF00;
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(4));
        return (flags & 0x4) != 0 && (flags & OptionalSections) == 0 && datagram.Length >= 40
            ? BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(36))
            : 0;
    }

    /// <summary>The opcodes of the whole-message fragments in a server datagram.</summary>
    internal static IEnumerable<uint> ServerOpcodes(byte[] datagram, int port) =>
        ServerDatagram.Decode(datagram, port).Fragments.Where(f => f.Index == 0 && f.Data.Length >= 4).Select(f => BinaryPrimitives.ReadUInt32LittleEndian(f.Data));
}
