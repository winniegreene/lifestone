using System.Net;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Credentials;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Net.Session;
using Lifestone.Net.Sockets;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <see cref="SessionBootstrap.CreateLive"/>'s gates (ST2, ST5), in order: loopback, mod ready, the language dat, the
/// account. Each refusal logs one WARNING naming the runner and stops everything after it. With every gate passed the
/// live session is created idle (ST13): net's session exists, but no socket until the login. Nothing reaches the network,
/// and no sentinel password reaches a message or a log line.
/// </summary>
public sealed class SessionBootstrapGateTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [Fact]
    public void Arguments_are_checked_before_anything_else()
    {
        var log = new RecordingLogSink();
        Assert.Throws<ArgumentNullException>(() => SessionBootstrap.CreateLive(null!, [], [], null!, TimeProvider.System, log, null));
        Assert.Throws<ArgumentNullException>(() => SessionBootstrap.CreateLive(new SessionOptions(), [], [], null!, TimeProvider.System, log, null));
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void The_loopback_gate_runs_first()
    {
        // A refused endpoint with every later input broken: only the loopback refusal is seen.
        var gates = new Gates();
        var e = Run(gates, new SessionOptions { Server = new IPEndPoint(IPAddress.Parse("10.0.0.5"), 9000) }, secrets: [], pids: []);
        Assert.IsType<LoopbackRequiredException>(e);
        Assert.Equal(0, gates.IterationCalls);
        Assert.Equal(0, gates.Sockets.Calls);
        Assert.Contains("loopback gate", Assert.Single(gates.Log.Entries).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_mod_ready_gate_runs_second()
    {
        var gates = new Gates();
        var e = Run(gates, new SessionOptions(), secrets: [], pids: SessionTestData.PidsWithoutMod());
        var refused = Assert.IsType<BackendNotReadyException>(e);
        Assert.Contains("ace.modReady", refused.Reason, StringComparison.Ordinal);
        Assert.Contains("Admin", refused.Reason, StringComparison.Ordinal);
        Assert.Equal(0, gates.IterationCalls);
        var warning = Assert.Single(gates.Log.Entries);
        Assert.Equal(LogSeverity.Warning, warning.Severity);
        Assert.Contains(SessionBootstrap.Runner, warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_language_dat_gate_runs_third()
    {
        var gates = new Gates { IterationFailure = new InvalidOperationException("no language dat") };
        var e = Run(gates, new SessionOptions(), secrets: [], pids: SessionTestData.PidsReady());
        Assert.Same(gates.IterationFailure, e);
        Assert.Equal(1, gates.IterationCalls);
        var warning = Assert.Single(gates.Log.Entries);
        Assert.Equal(LogSeverity.Warning, warning.Severity);
        Assert.Equal("language dat: SessionBootstrap.CreateLive refused: no language dat", warning.Message);
        Assert.Contains(warning.Fields, f => f.Key == "runner" && Equals(f.Value, "SessionBootstrap.CreateLive"));
        Assert.Equal(0, gates.Sockets.Calls);
    }

    [Fact]
    public void The_account_gate_runs_last_and_names_no_value()
    {
        var gates = new Gates();
        var e = Run(gates, new SessionOptions { Account = "nobody" }, SessionTestData.SecretsV2(), SessionTestData.PidsReady());
        var refused = Assert.IsType<PlayAccountException>(e);
        Assert.Contains("play (lifestone)", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("nobody", refused.Message, StringComparison.Ordinal);
        Assert.Equal(1, gates.IterationCalls);
        var warning = Assert.Single(gates.Log.Entries);
        Assert.Equal(LogSeverity.Warning, warning.Severity);
        AssertNoSentinel(gates.Log, e);
    }

    [Fact]
    public void With_every_gate_passed_an_idle_live_session_is_created_without_a_socket()
    {
        var gates = new Gates();
        using var session = SessionBootstrap.CreateLiveWith(
            new SessionOptions { Account = "test" }, SessionTestData.SecretsV2(), SessionTestData.PidsReady(), gates.Iterations, new ManualTimeProvider(), gates.Log, null, gates.Sockets.Open);
        Assert.Equal(SessionPhase.Idle, session.State.Phase);
        Assert.False(session.State.IsOffline);
        Assert.NotNull(session.Network);
        Assert.Equal(0, gates.Sockets.Calls);
        Assert.Equal(1, gates.IterationCalls);

        Assert.Equal([LogSeverity.Info, LogSeverity.Info], gates.Log.Entries.Select(x => x.Severity));
        Assert.Contains(gates.Log.Entries[0].Fields, f => f is { Key: "account", Value: "lifestone-test" });
        Assert.StartsWith("live session created", gates.Log.Entries[1].Message, StringComparison.Ordinal);
        AssertNoSentinel(gates.Log, null);
    }

    [RequiresLocalDatsFact]
    public void The_public_entry_checks_the_dats_language_iteration()
    {
        var log = new RecordingLogSink();
        using (var session = SessionBootstrap.CreateLive(
            new SessionOptions(), SessionTestData.SecretsV2(), SessionTestData.PidsReady(), fixture.Dats, new ManualTimeProvider(), log, null))
        {
            Assert.Equal(SessionPhase.Idle, session.State.Phase);
        }

        var iterations = SessionBootstrap.LanguageIterations(fixture.Dats);
        Assert.Equal(fixture.Dats.Iterations.Local, iterations.Language);
        Assert.Equal(fixture.Dats.Iterations.Portal, iterations.Portal);
        Assert.Contains(log.Entries[0].Fields, f => f.Key == "dats" && (string?)f.Value == $"p{iterations.Portal}-c{iterations.Cell}-l{iterations.Language}");
        AssertNoSentinel(log, null);
    }

    [Fact]
    public void The_data_check_iterations_come_from_the_dats_and_need_the_language_dat()
    {
        Assert.Equal(new DddIterations(2072, 982, 994, 497), SessionBootstrap.ToDddIterations(new DatIterations(2072, 982, 994, 497)));
        Assert.Equal(new DddIterations(2072, 982, 994, null), SessionBootstrap.ToDddIterations(new DatIterations(2072, 982, 994, null)));
        var e = Assert.Throws<InvalidOperationException>(() => SessionBootstrap.ToDddIterations(new DatIterations(2072, 982, null, 497)));
        Assert.Contains(DatSet.LocalFileName, e.Message, StringComparison.Ordinal);
    }

    private static Exception? Run(Gates gates, SessionOptions options, byte[] secrets, byte[] pids) =>
        Record.Exception(() => SessionBootstrap.CreateLiveWith(options, secrets, pids, gates.Iterations, new ManualTimeProvider(), gates.Log, null, gates.Sockets.Open));

    private static void AssertNoSentinel(RecordingLogSink log, Exception? e)
    {
        foreach (var sentinel in SessionTestData.Sentinels)
        {
            Assert.False(log.AnyLineContains(sentinel));
            Assert.DoesNotContain(sentinel, e?.ToString() ?? string.Empty, StringComparison.Ordinal);
        }
    }

    private sealed class Gates
    {
        public RecordingLogSink Log { get; } = new();

        public SocketFactorySpy Sockets { get; } = new();

        public Exception? IterationFailure { get; init; }

        public int IterationCalls { get; private set; }

        public DddIterations Iterations()
        {
            IterationCalls++;
            return IterationFailure is null ? SessionTestData.EndOfRetail : throw IterationFailure;
        }
    }
}
