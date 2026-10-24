using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The long-lived headless host (M4 P4, D31) against <see cref="ScriptedAceSession"/>: one login, several scenarios in turn
/// (each with its own limit and result), a failed scenario leaves the session usable, Dispose logs off, and a scenario that ends
/// the session is reported as such. The frame loop idles by advancing a manual clock, so the tests run in milliseconds.
/// </summary>
public sealed class HeadlessSessionTests
{
    [Fact]
    public void Two_scenarios_run_in_one_login()
    {
        using var rig = new Rig();
        Assert.True(rig.Host.EntryResult.Ok, rig.Host.EntryResult.Failure);
        Assert.True(rig.Host.IsInWorld);

        var first = rig.Host.Run(new Probe("first", 40), 60);
        var second = rig.Host.Run(new Probe("second", 40), 60);

        Assert.True(first.Ok, first.Failure);
        Assert.True(second.Ok, second.Failure);
        Assert.Equal("first", first.Scenario);
        Assert.Equal("second", second.Scenario);
        Assert.Equal(3, rig.Host.Runs);
        var server = rig.Servers.Single(); // one network session: one login
        Assert.Single(server.Sent, s => s.Opcode == MessageOpcodes.CharacterEnterWorld);
        Assert.DoesNotContain(server.Sent, s => s.Opcode == MessageOpcodes.CharacterLogOff);

        // Each run reports itself: its own seconds and counters, not the session's.
        Assert.InRange(first.Seconds, 1.5, 4);
        Assert.InRange(second.Seconds, 1.5, 4);
        Assert.Equal(0, second.LoginCompletes);
        Assert.Equal(0, second.Errors);
        Assert.Empty(second.Phases);
        Assert.StartsWith("headless done scenario=second ok=1 ", second.SummaryLine(null), StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_scenario_leaves_the_session_usable()
    {
        using var rig = new Rig();
        var failed = rig.Host.Run(new Probe("bad", 10, fail: true), 60);
        Assert.False(failed.Ok);
        Assert.Equal("bad: it failed", failed.Failure);
        Assert.True(failed.Errors >= 1);
        Assert.True(rig.Host.IsInWorld);

        var after = rig.Host.Run(new Probe("after", 10), 60);
        Assert.True(after.Ok, after.Failure);
        Assert.Equal(0, after.Errors); // the earlier run's error is not this run's
        Assert.Single(rig.Servers);
    }

    [Fact]
    public void A_run_has_its_own_time_limit_and_the_session_survives_it()
    {
        using var rig = new Rig();
        var slow = rig.Host.Run(new Probe("slow", int.MaxValue), 3);
        Assert.False(slow.Ok);
        Assert.Contains("did not finish within 3 s", slow.Failure, StringComparison.Ordinal);
        Assert.True(rig.Host.IsInWorld);
        Assert.True(rig.Host.Run(new Probe("fine", 5), 3).Ok);
    }

    [Fact]
    public void Dispose_logs_the_character_off_and_quits_the_session()
    {
        var rig = new Rig();
        Assert.True(rig.Host.IsInWorld);
        rig.Host.Dispose();

        var server = rig.Servers.Single();
        Assert.Single(server.Sent, s => s.Opcode == MessageOpcodes.CharacterLogOff);
        Assert.Equal(1, server.Quits);
        Assert.Throws<ObjectDisposedException>(() => rig.Host.Run(new Probe("late", 1), 5));
        rig.Host.Dispose(); // a second Dispose does nothing
        Assert.Equal(1, server.Quits);
    }

    [Fact]
    public void A_keep_alive_frame_pumps_the_idle_session_between_two_runs_and_never_while_it_is_out_of_the_world_P4_sfix()
    {
        using var rig = new Rig();
        Assert.True(rig.Host.Run(new Probe("first", 5), 60).Ok);
        var before = rig.Host.OnGameThread(s => s.FrameTime);

        rig.Time.Advance(TimeSpan.FromSeconds(30)); // the idle gap between two tests: nothing pumps the session
        rig.Host.KeepAlive();

        Assert.Equal(1, rig.Host.KeepAlives);
        Assert.True(rig.Host.OnGameThread(s => s.FrameTime) >= before + 30, "the keep-alive frame ticked the session at the current time");
        Assert.True(rig.Host.IsInWorld);
        Assert.True(rig.Host.Run(new Probe("second", 5), 60).Ok);

        Assert.True(rig.Host.Run(new Probe("ends", 5, logOff: true), 60).Ok);
        Assert.False(rig.Host.IsInWorld);
        rig.Host.KeepAlive(); // out of the world: nothing to keep alive, and no throw
        Assert.Equal(1, rig.Host.KeepAlives);
    }

    [Fact]
    public void P4_rfixb_R13_a_failing_keep_alive_is_logged_once_by_exception_name()
    {
        var time = new ManualTimeProvider();
        var log = new RecordingLogSink();
        var sockets = new SocketFactorySpy();
        var fail = false;
        using var host = HeadlessSession.StartWith(
            new HeadlessRunOptions { Session = new SessionOptions { Account = "test" } },
            new LoginScenario { StayInWorld = true },
            SessionTestData.SecretsV2(),
            SessionTestData.PidsReady(),
            () => SessionTestData.EndOfRetail,
            () => HeadlessTestWorld.Assets,
            _ => new HeadlessTestWorld(),
            time,
            log,
            recording: null,
            sockets.Open,
            (_, sink, tap) => new ScriptedAceSession(sink, tap, time),
            idle: _ =>
            {
                if (fail)
                {
                    throw new TimeoutException("the frame broke");
                }

                time.Advance(TimeSpan.FromMilliseconds(50));
            });
        Assert.Null(host.KeepAliveFailure);
        host.KeepAliveGuarded();
        Assert.Null(host.KeepAliveFailure);

        fail = true;
        host.KeepAliveGuarded();
        host.KeepAliveGuarded(); // the second failure is not logged again
        Assert.Equal(nameof(TimeoutException), host.KeepAliveFailure);
        var warns = log.Lines.Where(l => l.Contains("keep-alive frame failed", StringComparison.Ordinal)).ToList();
        Assert.Single(warns);
        Assert.Contains(nameof(TimeoutException), warns[0], StringComparison.Ordinal);
        Assert.DoesNotContain("the frame broke", warns[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_scenario_that_ends_the_session_is_reported_and_dispose_does_not_log_off_twice()
    {
        var rig = new Rig();
        var ending = rig.Host.Run(new Probe("ends", 5, logOff: true), 60);
        Assert.True(ending.Ok, ending.Failure);
        Assert.False(rig.Host.IsInWorld);
        Assert.Equal(SessionPhase.CharacterSelect, rig.Host.Phase);

        var e = Assert.Throws<InvalidOperationException>(() => rig.Host.Run(new Probe("next", 1), 5));
        Assert.Contains("not in the world", e.Message, StringComparison.Ordinal);

        rig.Host.Dispose();
        var server = rig.Servers.Single();
        Assert.Single(server.Sent, s => s.Opcode == MessageOpcodes.CharacterLogOff);
        Assert.Equal(1, server.Quits); // the disposal of the network session, once
    }

    [Fact]
    public async Task The_game_thread_is_one_thread_whichever_thread_calls()
    {
        using var rig = new Rig();
        var seen = new List<int>();
        var a = rig.Host.OnGameThread(s => { seen.Add(Environment.CurrentManagedThreadId); return s.State.Phase; });
        var b = await Task.Run(() => rig.Host.OnGameThread(s => { seen.Add(Environment.CurrentManagedThreadId); return s.State.Phase; }));
        Assert.Equal(SessionPhase.InWorld, a);
        Assert.Equal(SessionPhase.InWorld, b);
        Assert.Equal(2, seen.Count);
        Assert.Equal(seen[0], seen[1]);
        Assert.NotEqual(Environment.CurrentManagedThreadId, seen[0]);
    }

    [Fact]
    public void A_refused_start_throws_and_leaves_no_thread_behind()
    {
        var e = Record.Exception(() => HeadlessSession.StartWith(
            new HeadlessRunOptions { Session = new SessionOptions { Account = "test" } },
            new LoginScenario { StayInWorld = true },
            SessionTestData.SecretsV2(),
            SessionTestData.PidsWithoutMod(),
            () => SessionTestData.EndOfRetail,
            () => HeadlessTestWorld.Assets,
            _ => new HeadlessTestWorld(),
            new ManualTimeProvider(),
            new RecordingLogSink(),
            recording: null,
            new SocketFactorySpy().Open,
            networkFactory: null,
            idle: null));
        Assert.IsType<BackendNotReadyException>(e);
    }

    /// <summary>A scenario that succeeds (or fails, or logs off) after a number of frames.</summary>
    private sealed class Probe(string name, int frames, bool fail = false, bool logOff = false) : IScenario
    {
        private int _frames;
        private ActionHandle? _off;

        public string Name => name;

        public string Stage => "frame-" + _frames;

        public string? Failure { get; private set; }

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            if (++_frames < frames)
            {
                return ScenarioStatus.Running;
            }

            if (fail)
            {
                Failure = name + ": it failed";
                return ScenarioStatus.Failed;
            }

            if (logOff)
            {
                _off ??= session.Actions.LogOff(ActionSource.Host);
                return _off.Outcome is null ? ScenarioStatus.Running : ScenarioStatus.Succeeded;
            }

            return ScenarioStatus.Succeeded;
        }
    }

    private sealed class Rig : IDisposable
    {
        public Rig()
        {
            Host = HeadlessSession.StartWith(
                new HeadlessRunOptions { Session = new SessionOptions { Account = "test" } },
                new LoginScenario { StayInWorld = true },
                SessionTestData.SecretsV2(),
                SessionTestData.PidsReady(),
                () => SessionTestData.EndOfRetail,
                () => HeadlessTestWorld.Assets,
                _ => new HeadlessTestWorld(),
                Time,
                Log,
                recording: null,
                Sockets.Open,
                (_, sink, tap) =>
                {
                    var server = new ScriptedAceSession(sink, tap, Time);
                    Servers.Add(server);
                    return server;
                },
                idle: _ => Time.Advance(TimeSpan.FromMilliseconds(50)));
        }

        public ManualTimeProvider Time { get; } = new();

        public RecordingLogSink Log { get; } = new();

        public SocketFactorySpy Sockets { get; } = new();

        public List<ScriptedAceSession> Servers { get; } = [];

        public HeadlessSession Host { get; }

        public void Dispose() => Host.Dispose();
    }
}
