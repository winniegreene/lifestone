using System.Text.RegularExpressions;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Sockets;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The headless host's loop and its login scenario (step ST15) against <see cref="ScriptedAceSession"/>: the session
/// ticks, portal space plays the arrival with a timed tunnel and sends LoginComplete through the session bridge, and the
/// scenario logs in, enters, checks the mod's status and the server's view of the player, logs off and quits. The gates
/// refuse a non-loopback server (S8) and a backend without the mod before any session exists; a run that cannot start
/// gets a fixed failure phrase. The verdict line is m2-plan §4.14's.
/// </summary>
public sealed class HeadlessRunnerTests
{
    [Fact]
    public void Login_scenario_against_the_scripted_server_completes()
    {
        using var run = new ScriptedRun();
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("done", result.Stage);
        Assert.Equal(1, result.LoginCompletes);
        Assert.Equal(0, result.Errors);
        Assert.Equal(
            [
                SessionPhase.Connecting, SessionPhase.Handshaking, SessionPhase.DataCheck, SessionPhase.CharacterSelect,
                SessionPhase.EnteringWorld, SessionPhase.InWorld, SessionPhase.LoggingOff, SessionPhase.CharacterSelect,
            ],
            result.Phases);
        var server = run.Servers.Single();
        Assert.Contains((MessageOpcodes.GameAction, GameActionTypes.LoginComplete), server.Sent);
        Assert.Contains((MessageOpcodes.CharacterLogOff, 0u), server.Sent);
        Assert.Equal(1, server.Quits);
        var scenario = (LoginScenario)run.Runner.Scenario;
        Assert.Equal(ScriptedAceSession.Amy, scenario.EnteredGuid);
        Assert.Equal(PlayerTools.PlayerAccess, scenario.Status!.Access);

        // The server's view of the player agrees with the client's; nothing corrected it; no movement was sent.
        Assert.Contains(server.Talked, t => t.StartsWith("@lifestone where", StringComparison.Ordinal));
        Assert.Equal(ScriptedAceSession.AcademyCell, scenario.Where!.Position.CellId);
        Assert.Equal(0.0, result.WhereMaxMetres!.Value, 6);
        Assert.Equal(0, result.Corrections);
        Assert.Equal(0, result.Unmatched);

        // Portal space really played: the tunnel came and went before LoginComplete.
        Assert.True(result.Seconds > 4, $"the run took {result.Seconds:0.0} s; portal space's arrival takes at least 4 s");
        Assert.Equal(TeleportAnimState.Off, run.Runner.Director.Machine.State);
    }

    [Fact]
    public void The_run_records_and_its_recording_replays_to_the_trailer_digest()
    {
        byte[] recording;
        using (var run = new ScriptedRun())
        {
            Assert.True(run.Finish().Ok);
            run.Runner.Dispose();
            recording = run.Recording.ToArray();
        }

        var replay = new SessionReplayer().Replay(new MemoryStream(recording));
        Assert.Equal("headless", replay.Header.Runner);
        Assert.Equal(LoginScenario.ScenarioName, replay.Header.Scenario);
        Assert.Equal(RecordingEnd.Trailer, replay.End);
        Assert.True(replay.Trailer!.IsIntact);
        Assert.True(replay.DigestMatches);
        Assert.Equal(0, replay.Malformed);
    }

    [Fact]
    public void A_replays_digest_evidence_names_the_trailer_and_both_digests_so_a_failed_check_carries_its_cause_P4_sfix2()
    {
        byte[] recording;
        using (var run = new ScriptedRun())
        {
            Assert.True(run.Finish().Ok);
            run.Runner.Dispose();
            recording = run.Recording.ToArray();
        }

        var replay = new SessionReplayer().Replay(new MemoryStream(recording));
        var evidence = replay.DigestEvidence();
        var digest = GameStateDigest.ToHex(replay.StateDigest);
        Assert.Contains("trailer=present", evidence, StringComparison.Ordinal);
        Assert.Contains("recorded_digest=" + digest, evidence, StringComparison.Ordinal);
        Assert.Contains("replayed_digest=" + digest, evidence, StringComparison.Ordinal);
        Assert.Contains("end=Trailer", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void The_summary_line_is_the_frozen_verdict_line_and_names_no_secret_account_or_character()
    {
        using var run = new ScriptedRun();
        var result = run.Finish();
        var line = result.SummaryLine("cache/sessions/x.lsrec");
        Assert.Equal("headless done scenario=login ok=1 corrections=0 where_max_m=0.000 unmatched=0 recording=cache/sessions/x.lsrec", line);
        Assert.Matches(VerdictLine, line);
        foreach (var forbidden in SessionTestData.Sentinels.Append("lifestone-test").Append("Amy"))
        {
            Assert.DoesNotContain(forbidden, line, StringComparison.Ordinal);
        }

        Assert.False(run.Log.AnyLineContains(SessionTestData.TestSentinel));
    }

    [Fact]
    public void A_failed_run_keeps_the_verdict_keys_and_leaves_its_failure_to_the_log()
    {
        using var run = new ScriptedRun(new LoginScenario(character: "Nobody"));
        var result = run.Finish();
        var line = result.SummaryLine(null);
        Assert.Equal("headless done scenario=login ok=0 corrections=0 where_max_m=none unmatched=0 recording=none", line);
        Assert.Matches(VerdictLine, line);
        Assert.Contains(run.Log.Entries, e => e.Severity == LogSeverity.Error && e.Message.Contains("no character of that name", StringComparison.Ordinal));
    }

    [Fact]
    public void Server_corrections_of_the_player_are_counted()
    {
        using var run = new ScriptedRun(configure: s => s.ForcePositionsOnEnter = 2);
        var result = run.Finish();
        Assert.True(result.Ok, result.Failure);
        Assert.Equal(2, result.Corrections);
        Assert.Equal(2, run.Runner.Session.Corrections);
        Assert.StartsWith("headless done scenario=login ok=1 corrections=2 ", result.SummaryLine(null), StringComparison.Ordinal);
    }

    [Fact]
    public void A_server_position_that_disagrees_fails_the_where_step()
    {
        using var run = new ScriptedRun(configure: s => s.WhereOffsetMetres = 0.75f);
        var result = run.Finish();
        Assert.False(result.Ok);
        Assert.Equal("where", result.Stage);
        Assert.Contains("does not agree within 0.5 m", result.Failure, StringComparison.Ordinal);
        Assert.Equal(0.75, result.WhereMaxMetres!.Value, 3);
        Assert.Contains(" where_max_m=0.750 ", result.SummaryLine(null), StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_that_cannot_start_gets_a_fixed_failure_naming_no_account()
    {
        var log = new RecordingLogSink();
        var scenario = new LoginScenario();
        var e = Record.Exception(() => HeadlessRunner.StartWith(
            new HeadlessRunOptions { Session = new SessionOptions { Account = "test" } },
            scenario,
            SessionTestData.SecretsV2WithoutTest(),
            SessionTestData.PidsReady(),
            () => SessionTestData.EndOfRetail,
            () => HeadlessTestWorld.Assets,
            _ => new HeadlessTestWorld(),
            new ManualTimeProvider(),
            log,
            recording: null,
            new SocketFactorySpy().Open,
            networkFactory: null));
        Assert.IsType<PlayAccountException>(e);
        Assert.Contains("Alice-Alt", e.Message, StringComparison.Ordinal); // the message names usernames: the log only

        var result = HeadlessRunResult.CouldNotStart(scenario, e);
        Assert.Equal("the account gate refused (see the log)", result.Failure);
        Assert.Equal(1, result.ExitCode);
        var line = result.SummaryLine(null);
        Assert.Equal("headless done scenario=login ok=0 corrections=0 where_max_m=none unmatched=0 recording=none", line);
        foreach (var forbidden in SessionTestData.Sentinels.Concat(["lifestone", "Alice-Alt", "play", "test"]))
        {
            Assert.DoesNotContain(forbidden, line + " " + result.Failure, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>dod-1-2-12-13-1: the headless runner is always an automated run, so it signs in only to the test account.</summary>
    [Theory]
    [InlineData("play")]
    [InlineData("alice")]
    [InlineData("TEST")]
    [InlineData("test ")]
    public void Runner_refuses_any_account_but_the_test_account_before_anything_else(string account)
    {
        var log = new RecordingLogSink();
        var sockets = new SocketFactorySpy();
        var iterations = 0;
        var e = Record.Exception(() => HeadlessRunner.StartWith(
            new HeadlessRunOptions { Session = new SessionOptions { Account = account } },
            new LoginScenario(),
            SessionTestData.SecretsV2(),
            SessionTestData.PidsReady(),
            () =>
            {
                iterations++;
                return SessionTestData.EndOfRetail;
            },
            () => HeadlessTestWorld.Assets,
            _ => new HeadlessTestWorld(),
            new ManualTimeProvider(),
            log,
            recording: null,
            sockets.Open,
            networkFactory: null));
        Assert.IsType<PlayAccountException>(e);
        Assert.Contains("uses only the test account", e.Message, StringComparison.Ordinal);
        Assert.Equal(0, iterations);
        Assert.Equal(0, sockets.Calls);
        var warning = Assert.Single(log.Entries);
        Assert.Equal(LogSeverity.Warning, warning.Severity);
        Assert.Contains(HeadlessRunner.RunnerName, warning.Message, StringComparison.Ordinal);
        Assert.Equal("the account gate refused (see the log)", HeadlessRunResult.StartFailure(e));
        foreach (var text in new[] { e.Message, warning.Message }.Append(HeadlessRunResult.CouldNotStart(new LoginScenario(), e).SummaryLine(null)))
        {
            // The account name is never repeated, and nor is any secret.
            Assert.DoesNotContain("Alice-Alt", text, StringComparison.Ordinal);
            Assert.DoesNotContain("nobody", text, StringComparison.Ordinal);
            foreach (var sentinel in SessionTestData.Sentinels)
            {
                Assert.DoesNotContain(sentinel, text, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>The default (no account) and the explicit test account both go on to the gates behind the rule.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("test")]
    public void Runner_accepts_the_test_account_or_none(string? account)
    {
        var log = new RecordingLogSink();
        var e = Record.Exception(() => HeadlessRunner.StartWith(
            new HeadlessRunOptions { Session = new SessionOptions { Account = account } },
            new LoginScenario(),
            SessionTestData.SecretsV2(),
            SessionTestData.PidsWithoutMod(),
            () => SessionTestData.EndOfRetail,
            () => HeadlessTestWorld.Assets,
            _ => new HeadlessTestWorld(),
            new ManualTimeProvider(),
            log,
            recording: null,
            new SocketFactorySpy().Open,
            networkFactory: null));
        Assert.IsType<BackendNotReadyException>(e); // past the account rule, stopped by the next gate
    }

    [Fact]
    public void Each_start_refusal_has_its_own_fixed_phrase()
    {
        Assert.Equal("the loopback gate refused the server (see the log)", HeadlessRunResult.StartFailure(new LoopbackRequiredException(null, "refused")));
        Assert.Equal("the backend is not ready (see the log)", HeadlessRunResult.StartFailure(new BackendNotReadyException("no mod")));
        Assert.Equal("the dats could not be read (see the log)", HeadlessRunResult.StartFailure(new InvalidDataException("bad")));
        Assert.Equal("a backend file could not be read (see the log)", HeadlessRunResult.StartFailure(new IOException("locked")));
        Assert.Equal("a setting was refused (see the log)", HeadlessRunResult.StartFailure(new ArgumentException("bad")));
        Assert.Equal("the session could not be created (see the log)", HeadlessRunResult.StartFailure(new InvalidOperationException("no language dat")));
    }

    [Fact]
    public void Create_character_creates_it_then_enters_it()
    {
        using var run = new ScriptedRun(new LoginScenario(createCharacter: "Lifestone Smoke", skillClasses: HeadlessCharacterTests.StandInClasses()));
        var result = run.Finish();
        Assert.True(result.Ok, result.Failure);
        var server = run.Servers.Single();
        Assert.Contains((MessageOpcodes.CharacterCreate, 0u), server.Sent);
        var created = server.Characters.Single(c => c.Name == "Lifestone Smoke");
        Assert.Equal(created.Guid, ((LoginScenario)run.Runner.Scenario).EnteredGuid);
    }

    [Fact]
    public void P4_rfixb_R11_a_run_that_creates_a_character_on_test_melee_or_test_archer_is_refused_before_the_backend()
    {
        foreach (var account in new[] { "test-melee", "test-archer" })
        {
            var options = new HeadlessRunOptions { Session = new SessionOptions { Account = account } };
            var e = Assert.Throws<PlayAccountException>(() => new ScriptedRun(new LoginScenario(createCharacter: "Lifestone Throwaway", skillClasses: HeadlessCharacterTests.StandInClasses()), options: options));
            Assert.DoesNotContain(account, e.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Lifestone Throwaway", e.Message, StringComparison.Ordinal);
            Assert.Throws<PlayAccountException>(() => new ScriptedRun(new LoginCheckScenario(HeadlessCharacterTests.StandInClasses(), "Lifestone Smoke"), options: options));
        }
    }

    [Fact]
    public void P5b_G0_a_run_on_test_tools_may_create_no_other_character_and_the_main_account_may_not_create_the_throwaway()
    {
        var tools = new HeadlessRunOptions { Session = new SessionOptions { Account = "test-tools" } };
        var e = Assert.Throws<PlayAccountException>(() => new ScriptedRun(new LoginScenario(createCharacter: "Lifestone Smoke", skillClasses: HeadlessCharacterTests.StandInClasses()), options: tools));
        Assert.DoesNotContain("test-tools", e.Message, StringComparison.Ordinal);
        Assert.Contains("tools test account", e.Message, StringComparison.Ordinal);

        var main = new HeadlessRunOptions { Session = new SessionOptions { Account = "test" } };
        Assert.Throws<PlayAccountException>(() => new ScriptedRun(new LoginScenario(createCharacter: "Lifestone Throwaway", skillClasses: HeadlessCharacterTests.StandInClasses()), options: main));
    }

    [Fact]
    public void A_named_character_is_entered_and_an_unknown_one_fails_the_run()
    {
        using (var run = new ScriptedRun(new LoginScenario(character: "cy")))
        {
            Assert.True(run.Finish().Ok);
            Assert.Equal(0x50000003u, ((LoginScenario)run.Runner.Scenario).EnteredGuid);
        }

        using (var run = new ScriptedRun(new LoginScenario(character: "Nobody")))
        {
            var result = run.Finish();
            Assert.False(result.Ok);
            Assert.Equal(1, result.ExitCode);
            Assert.Equal("choosing", result.Stage);
            Assert.Contains("no character of that name", result.Failure, StringComparison.Ordinal);
            Assert.DoesNotContain("Nobody", result.Failure, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_access_level_above_Player_fails_the_status_step()
    {
        using var run = new ScriptedRun(configure: s => s.Access = "Admin");
        var result = run.Finish();
        Assert.False(result.Ok);
        Assert.Equal("status", result.Stage);
        Assert.Contains("access level is Admin", result.Failure, StringComparison.Ordinal);
        Assert.True(run.Runner.Session.Actions.KillSwitch);
        Assert.True(result.Errors >= 2);
    }

    [Fact]
    public void A_missing_server_mod_fails_the_status_step()
    {
        using var run = new ScriptedRun(configure: s => s.ModLoaded = false);
        var result = run.Finish();
        Assert.False(result.Ok);
        Assert.Equal("status", result.Stage);
        Assert.Contains("Refused", result.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void The_run_fails_when_its_time_limit_passes()
    {
        using var run = new ScriptedRun(
            configure: s => s.LogOffDelaySeconds = 1000,
            options: new HeadlessRunOptions { Session = new SessionOptions { Account = "test" }, QuitAfterSeconds = 15 });
        var result = run.Finish();
        Assert.False(result.Ok);
        Assert.Contains("did not finish within 15 s (stage logging-off", result.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Runner_refuses_without_mod_ready()
    {
        var log = new RecordingLogSink();
        var sockets = new SocketFactorySpy();
        var iterations = 0;
        var e = Record.Exception(() => HeadlessRunner.StartWith(
            new HeadlessRunOptions { Session = new SessionOptions { Account = "test" } },
            new LoginScenario(),
            SessionTestData.SecretsV2(),
            SessionTestData.PidsWithoutMod(),
            () =>
            {
                iterations++;
                return SessionTestData.EndOfRetail;
            },
            () => HeadlessTestWorld.Assets,
            _ => new HeadlessTestWorld(),
            new ManualTimeProvider(),
            log,
            recording: null,
            sockets.Open,
            networkFactory: null));
        Assert.IsType<BackendNotReadyException>(e);
        Assert.Equal(0, iterations);
        Assert.Equal(0, sockets.Calls);
        var warning = Assert.Single(log.Entries);
        Assert.Equal(LogSeverity.Warning, warning.Severity);
        Assert.Contains(HeadlessRunner.RunnerName, warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_frame_that_throws_ends_the_run_loudly()
    {
        using var run = new ScriptedRun(new ThrowingScenario());
        var result = run.Finish();
        Assert.False(result.Ok);
        Assert.Contains("InvalidOperationException: a scenario bug", result.Failure, StringComparison.Ordinal);
        Assert.Contains(run.Log.Entries, x => x.Severity == LogSeverity.Error && x.Message.Contains("a scenario bug", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_loops_until_the_scenario_ends_and_honours_cancellation()
    {
        using var run = new ScriptedRun(new ThrowingScenario(throwAt: int.MaxValue));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var result = run.Runner.Run(null, cancel.Token);
        Assert.True(result.Done);
        Assert.Equal("cancelled", result.Failure);
    }

    /// <summary>m2-plan §4.14's headless verdict line: its keys, in order, and nothing else.</summary>
    private static readonly Regex VerdictLine = new(
        @"^headless done scenario=[a-z-]+ ok=[01] corrections=\d+ where_max_m=(\d+\.\d{3}|none) unmatched=\d+ recording=\S+$",
        RegexOptions.CultureInvariant);

    /// <summary>A headless run over the scripted server, stepped on a manual clock.</summary>
    private sealed class ScriptedRun : IDisposable
    {
        public ScriptedRun(IScenario? scenario = null, Action<ScriptedAceSession>? configure = null, HeadlessRunOptions? options = null)
        {
            Runner = HeadlessRunner.StartWith(
                options ?? new HeadlessRunOptions { Session = new SessionOptions { Account = "test" } },
                scenario ?? new LoginScenario(),
                SessionTestData.SecretsV2(),
                SessionTestData.PidsReady(),
                () => SessionTestData.EndOfRetail,
                () => HeadlessTestWorld.Assets,
                _ => World,
                Time,
                Log,
                Recording,
                Sockets.Open,
                (_, sink, tap) =>
                {
                    var server = new ScriptedAceSession(sink, tap, Time);
                    configure?.Invoke(server);
                    Servers.Add(server);
                    return server;
                });
        }

        public ManualTimeProvider Time { get; } = new();

        public RecordingLogSink Log { get; } = new();

        public MemoryStream Recording { get; } = new();

        public SocketFactorySpy Sockets { get; } = new();

        public HeadlessTestWorld World { get; } = new();

        public List<ScriptedAceSession> Servers { get; } = [];

        public HeadlessRunner Runner { get; }

        public HeadlessRunResult Finish(double maxSeconds = 300)
        {
            for (var t = 0.0; t < maxSeconds && !Runner.IsDone; t += 0.05)
            {
                Time.Advance(TimeSpan.FromMilliseconds(50));
                Runner.Step(Runner.Session.Clock.Now);
            }

            Assert.True(Runner.IsDone, $"the run did not end within {maxSeconds} s (stage {Runner.Scenario.Stage})");
            Assert.Equal(0, Sockets.Calls);
            return Runner.Result;
        }

        public void Dispose() => Runner.Dispose();
    }

    /// <summary>A scenario that throws on its <c>throwAt</c>-th step.</summary>
    private sealed class ThrowingScenario(int throwAt = 3) : IScenario
    {
        private int _steps;

        public string Name => "throwing";

        public string Stage => "step-" + _steps;

        public string? Failure => null;

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public ScenarioStatus Step(IGameSessionView session, double now) =>
            ++_steps >= throwAt ? throw new InvalidOperationException("a scenario bug") : ScenarioStatus.Running;
    }
}
