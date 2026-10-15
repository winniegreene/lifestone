using System.Net;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Recording;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Session;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The live <see cref="GameSession"/> (step ST13; docs/session.md §2 and §4): created idle by
/// <see cref="SessionBootstrap"/> after its gates, ticked in the documented order, reconnected after a failure with its
/// subscribers kept, and disposed with the network session quit, the recording completed and the credentials zeroed. The
/// network session is <see cref="ScriptedAceSession"/>.
/// </summary>
public sealed class GameSessionLiveTests
{
    [Fact]
    public void A_live_session_starts_idle_with_no_socket_and_no_login()
    {
        using var h = new LiveHarness();
        Assert.Equal(SessionPhase.Idle, h.State.Phase);
        Assert.False(h.State.IsOffline);
        Assert.Equal(0, h.Sockets.Calls);
        Assert.Empty(h.Server.Credentials);
        Assert.Contains(h.Log.Entries, e => e.Message.StartsWith("live session created", StringComparison.Ordinal));
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 9000), h.NetOptions!.Server);
        Assert.Equal(SessionTestData.EndOfRetail, h.NetOptions.Iterations);
        Assert.Same(h.Time, h.NetOptions.Time);
    }

    [Fact]
    public void Tick_order_is_pump_then_actions_then_flush()
    {
        using var h = new LiveHarness();
        var header = h.Recording!.Length; // the recorder writes its start and header at once
        Assert.True(header > RecordingFormat.FileHeaderSize);
        var login = h.Actions.Login();
        var listSeenWhileLoginPending = false;
        var phaseAtCompletion = SessionPhase.Idle;
        h.Session.Events.Subscribe((in CharacterListChanged _) => listSeenWhileLoginPending = !login.IsCompleted);
        login.Completed += _ => phaseAtCompletion = h.State.Phase;

        // Tick 1: the Action API sends the login (the server's answers are queued for the next pump).
        h.Run();
        Assert.False(login.IsCompleted);
        Assert.Single(h.Server.Credentials);
        Assert.Equal(0, h.Session.LastPump.Messages);
        Assert.Equal(header, h.Recording.Length);

        // Tick 2: the pump delivers the login (F7E5, F7E2, F7E1, F658) and the reducer publishes; the handle completes
        // after, in the same tick.
        h.Run();
        Assert.Equal(4, h.Session.LastPump.Messages);
        Assert.True(listSeenWhileLoginPending);
        Assert.True(login.IsCompleted);
        Assert.Equal(SessionPhase.CharacterSelect, phaseAtCompletion);

        // The recorder flushes at the end of a tick at most once a second.
        Assert.Equal(header, h.Recording.Length);
        h.Run(1.0);
        Assert.True(h.Recording.Length > header);
    }

    [Fact]
    public void The_network_session_gets_lifestones_options_or_retails_with_retail_net()
    {
        var log = new RecordingLogSink();
        var server = new IPEndPoint(IPAddress.Loopback, 9000);
        var lifestone = SessionBootstrap.NetworkOptions(new SessionOptions(), server, SessionTestData.EndOfRetail, TimeProvider.System, log, null);
        Assert.True(lifestone.TailProbe);
        Assert.Same(SessionTimeouts.Default, lifestone.Timeouts);
        Assert.Same(LoginRetryPolicy.Default, lifestone.Retry);
        Assert.Equal(InboundOrder.FragmentSequence, lifestone.Order);

        var retail = SessionBootstrap.NetworkOptions(new SessionOptions { RetailNet = true }, server, SessionTestData.EndOfRetail, TimeProvider.System, log, null);
        Assert.False(retail.TailProbe);
        Assert.Same(SessionTimeouts.Retail, retail.Timeouts);
        Assert.Same(LoginRetryPolicy.Retail, retail.Retry);
        Assert.Equal(InboundOrder.Arrival, retail.Order);
    }

    [Fact]
    public void The_network_sessions_log_goes_to_the_net_category()
    {
        var log = new RecordingLogSink(LogSeverity.Info);
        var adapter = new NetLogAdapter(log);
        Assert.False(adapter.IsEnabled(NetLogLevel.Debug));
        Assert.True(adapter.IsEnabled(NetLogLevel.Warning));
        adapter.Log(NetLogLevel.Debug, "dropped");
        adapter.Log(NetLogLevel.Warning, "a filtered datagram");
        var entry = Assert.Single(log.Entries);
        Assert.Equal(("net", LogSeverity.Warning, "a filtered datagram"), (entry.Category, entry.Severity, entry.Message));
    }

    [Fact]
    public void A_whole_session_logs_in_enters_materialises_and_logs_off()
    {
        using var h = new LiveHarness();
        h.Present();
        Assert.Equal(ScriptedAceSession.Amy, h.State.Self.Guid);
        Assert.Equal(4, h.State.Objects.Count);
        Assert.True(h.State.Self.Qualities.IsKnown);

        var logOff = h.Actions.LogOff(ActionSource.Host);
        Assert.True(h.RunUntil(() => logOff.IsCompleted));
        Assert.Equal(ActionOutcomeKind.Confirmed, logOff.Outcome!.Value.Kind);
        Assert.Equal(SessionPhase.CharacterSelect, h.State.Phase);
        Assert.Equal(0, h.State.Objects.Count);
        Assert.Contains(h.Server.Sent, s => s.Opcode == 0xF653);
    }

    [Fact]
    public void Reconnect_resets_state_keeps_subscribers()
    {
        using var h = new LiveHarness();
        var phases = new List<(SessionPhase From, SessionPhase To)>();
        h.Session.Events.Subscribe((in SessionPhaseChanged e) => phases.Add((e.Previous, e.Phase)));
        h.Present();
        var first = h.Server;
        first.FailNow();
        h.Run();
        Assert.Equal(SessionPhase.Failed, h.State.Phase);

        var pending = h.Actions.Talk("anyone there?", ActionSource.Ui);
        phases.Clear();
        h.Session.Reconnect();

        Assert.Equal(1, h.Session.Reconnects);
        Assert.True(first.Disposed);
        Assert.Equal(2, h.Servers.Count);
        Assert.Equal(SessionPhase.Idle, h.State.Phase);
        Assert.Null(h.State.Failure);
        Assert.Equal(0, h.State.Objects.Count);
        Assert.Equal(0u, h.State.Self.Guid);
        Assert.Empty(h.State.Characters.Entries);
        Assert.Equal(ActionOutcomeKind.Cancelled, pending.Outcome!.Value.Kind);
        Assert.Contains((SessionPhase.Failed, SessionPhase.Idle), phases);

        // The same subscriber sees the next login; the session still holds the credentials (no secret was needed).
        var login = h.Actions.Login();
        Assert.True(h.RunUntil(() => login.IsCompleted));
        Assert.Equal(ActionOutcomeKind.Confirmed, login.Outcome!.Value.Kind);
        Assert.Contains((SessionPhase.DataCheck, SessionPhase.CharacterSelect), phases);
        Assert.Same(h.Servers[0].Credentials[0], h.Servers[1].Credentials[0]);
    }

    [Fact]
    public void Dispose_quits_completes_the_recording_and_zeroes_the_credentials()
    {
        var h = new LiveHarness();
        h.Present();
        var server = h.Server;
        var credentials = server.Credentials[0];
        var expected = GameStateDigest.Compute(h.State);

        h.Session.Dispose();
        h.Session.Dispose();

        Assert.Equal(1, server.Quits);
        Assert.True(server.Disposed);
        Assert.True(credentials.IsDisposed);
        Assert.All(PasswordHygieneTests.PasswordChars(credentials), c => Assert.Equal('\0', c));
        Assert.Contains(server.Sent, s => s.Opcode == 0xF653);

        using var reader = SessionRecordingReader.Open(new MemoryStream(h.RecordingBytes()));
        Assert.Equal("test", reader.Header.Runner);
        Assert.Equal("harness", reader.Header.Scenario);
        Assert.NotNull(reader.Header.Iterations);
        foreach (var _ in reader.ReadAll())
        {
        }

        Assert.Equal(RecordingEnd.Trailer, reader.End);
        Assert.True(reader.Trailer!.IsIntact);
        Assert.Equal(expected, reader.Trailer.StateDigest);
        Assert.Throws<ObjectDisposedException>(() => h.Session.Tick(1));
    }

    [Fact]
    public async Task Waiting_between_frames_takes_the_whole_wait_on_the_session_clock()
    {
        using var h = new LiveHarness(record: false);
        var wait = h.Session.WaitForInboundAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None).AsTask();
        Assert.False(wait.IsCompleted);
        h.Time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.False(await wait.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void A_failing_network_factory_leaves_the_credentials_to_the_bootstrap_which_zeroes_them()
    {
        var log = new RecordingLogSink();
        AcCredentials? held = null;
        var e = Record.Exception(() => SessionBootstrap.CreateLiveWith(
            new SessionOptions { Account = "test" },
            SessionTestData.SecretsV2(),
            SessionTestData.PidsReady(),
            () => SessionTestData.EndOfRetail,
            new Lifestone.Tests.Shared.ManualTimeProvider(),
            log,
            recording: null,
            socketFactory: null,
            recordingHeader: null,
            networkFactory: (_, _, _) => throw new ArgumentException("the port is unusable"),
            credentialsObserver: c => held = c));
        Assert.IsType<ArgumentException>(e);
        Assert.NotNull(held);
        Assert.True(held.IsDisposed);
        var password = PasswordHygieneTests.PasswordChars(held);
        Assert.Equal(SessionTestData.TestSentinel.Length, password.Length);
        Assert.All(password, c => Assert.Equal('\0', c));
        Assert.All(SessionTestData.Sentinels, s => Assert.False(log.AnyLineContains(s)));
    }
}
