using System.Text.RegularExpressions;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The Holtburg laps scenario (step ST16) against <see cref="ScriptedAceSession"/> playing ACE for a walk: the character
/// logs in, is sent to the ring's start with <c>place loc</c>, walks three laps on the real Holtburg collision with its
/// own run skill, and every position it sends is echoed. The audit passes with no corrections and a where-am-I at each
/// lap; a correction, or a server that disagrees about where the player is, fails the scenario and says so.
/// </summary>
public sealed class HoltburgLapsScenarioTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly Regex Verdict = new(
        @"^movement route=holtburg-laps laps=3 corrections=0 where_max_m=\d+\.\d\d result=PASS$",
        RegexOptions.CultureInvariant);

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void Three_laps_pass_against_a_server_that_accepts_the_walk()
    {
        using var run = new Run(Dats, configure: s => s.AcceptsMovement = true);
        var result = run.Finish();
        var server = run.Server;
        var scenario = (HoltburgLapsScenario)run.Runner.Scenario;
        var play = scenario.Play;
        output.WriteLine($"{result.SummaryLine("memory")} ({run.Frames} frames)");
        output.WriteLine($"{play.SummaryLine}; {server.PositionMessages} position messages, {server.Echoes} echoes, {server.IgnoredWhileTeleporting} ignored while teleporting; unmatched {play.Unmatched}");

        Assert.True(result.Ok, result.Failure);
        Assert.Equal("done", result.Stage);
        Assert.Matches(Verdict, play.SummaryLine);
        var audit = play.Audit!;
        Assert.Equal(0, audit.Corrections);
        Assert.Equal(3, audit.Laps);
        Assert.True(audit.WhereChecks >= 4, $"the first check and one per lap, not {audit.WhereChecks}");
        Assert.InRange(audit.WhereMaxMetres!.Value, 0.0, 0.5);
        Assert.True(audit.Passed);
        Assert.Equal(0, result.Corrections);
        Assert.InRange(result.WhereMaxMetres!.Value, 0.0, 0.5);

        // It really walked: movement went out on retail's cadence, after the teleport, and the server echoed it.
        Assert.True(audit.MoveToStateSent >= 10, $"{audit.MoveToStateSent} MoveToState");
        Assert.True(audit.AutonomousPositionSent >= 8, $"{audit.AutonomousPositionSent} AutonomousPosition over three laps");
        Assert.Equal(0, server.IgnoredWhileTeleporting);
        Assert.True(audit.MaxAutonomousPositionIntervalSeconds <= 1.0 + 0.1, $"{audit.MaxAutonomousPositionIntervalSeconds:F3} s between positions");
        Assert.Equal(2, audit.Teleports); // the ring start and the return home
        Assert.Equal(0, play.Unmatched); // every position the server received was echoed

        // The scenario sent the player to the ring's start by location, once, and the character ran on its own skill.
        Assert.Equal(2, server.Talked.Count(t => t.StartsWith("@lifestone place loc ", StringComparison.Ordinal))); // to the ring's start, then home
        Assert.Contains(server.Talked, t => t.StartsWith("@lifestone where", StringComparison.Ordinal));
        Assert.True(play.Walker!.Weenie is QualitiesWeenie);
        Assert.True(play.RunRate > 1.0f, $"run rate {play.RunRate}"); // the qualities are cleared by the logoff, so the rate was taken when the walker was made
        Assert.Contains((MessageOpcodes.CharacterLogOff, 0u), server.Sent);
        Assert.Equal(1, server.Quits);

        // The character is left where it was found, and nothing the walker did afterwards moved it away again.
        Assert.Equal(ScriptedAceSession.AcademyCell, server.PlayerAt.Cell);
        Assert.Equal((61.5f, -21.5f), (server.PlayerAt.X, server.PlayerAt.Y));
        Assert.Equal(0, server.IgnoredWhileTeleporting);
    }

    [RequiresDatsFact]
    public void A_force_position_correction_fails_the_audit()
    {
        using var run = new Run(Dats, configure: s => (s.AcceptsMovement, s.CorrectAfterEchoes) = (true, 12));
        var result = run.Finish();
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}");

        Assert.False(result.Ok);
        Assert.Contains("the movement audit failed", result.Failure, StringComparison.Ordinal);
        Assert.Contains("corrections=1", result.Failure, StringComparison.Ordinal);
        Assert.Equal(1, result.Corrections);
        Assert.Contains("result=FAIL", ((HoltburgLapsScenario)run.Runner.Scenario).Play.SummaryLine, StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void A_jump_leg_after_the_laps_sends_a_standing_and_a_running_jump_and_the_server_agrees_where_the_player_is()
    {
        using var run = new Run(Dats, configure: s => s.AcceptsMovement = true, jumps: true);
        var result = run.Finish();
        var play = ((HoltburgLapsScenario)run.Runner.Scenario).Play;
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}; {play.SummaryLine}; jump leg {play.Jump?.Describe()}; unmatched {play.Unmatched}");

        Assert.True(result.Ok, result.Failure);
        Assert.NotNull(play.Jump);
        Assert.Null(play.Jump.Failure);
        Assert.Equal(2, play.Jump.Jumps);
        Assert.Equal(2, play.Jump.Landed);
        var audit = play.Audit!;
        Assert.Equal(2, audit.JumpSent); // a standing jump and a running jump went out as the Jump action
        Assert.Equal(0, audit.Corrections);
        Assert.True(audit.WhereChecks >= 5, $"the first check, one per lap and one after the jumps, not {audit.WhereChecks}");
        Assert.InRange(audit.WhereMaxMetres!.Value, 0.0, 0.5);
        Assert.True(play.Unmatched <= 2, $"unmatched {play.Unmatched}: at most the two Jump actions, which ACE answers with a vector update");
    }

    [RequiresDatsFact]
    public void A_server_that_puts_the_player_elsewhere_fails_the_audit()
    {
        using var run = new Run(Dats, configure: s => (s.AcceptsMovement, s.WalkWhereOffsetMetres) = (true, 3f));
        var result = run.Finish();
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}");

        Assert.False(result.Ok);
        Assert.Contains("the movement audit failed", result.Failure, StringComparison.Ordinal);
        Assert.Contains("result=FAIL", result.Failure, StringComparison.Ordinal);
        Assert.InRange(result.WhereMaxMetres!.Value, 2.9, 3.1);
    }

    /// <summary>A headless run of the Holtburg scenario over the scripted server, stepped at 60 Hz on a manual clock.</summary>
    private sealed class Run : IDisposable
    {
        public Run(DatSet dats, Action<ScriptedAceSession> configure, bool jumps = false)
        {
            Time = new ManualTimeProvider();
            Runner = HeadlessRunner.StartWith(
                new HeadlessRunOptions { Session = new SessionOptions { Account = "test" } },
                new HoltburgLapsScenario(dats, Log, character: "Amy", jumps: jumps),
                SessionTestData.SecretsV2(),
                SessionTestData.PidsReady(),
                () => SessionTestData.EndOfRetail,
                () => HeadlessTestWorld.Assets,
                _ => new HeadlessTestWorld(),
                Time,
                Log,
                new MemoryStream(),
                new SocketFactorySpy().Open,
                (_, sink, tap) =>
                {
                    Server = new ScriptedAceSession(sink, tap, Time);
                    configure(Server);
                    return Server;
                });
        }

        public ManualTimeProvider Time { get; }

        public RecordingLogSink Log { get; } = new();

        public HeadlessRunner Runner { get; }

        public ScriptedAceSession Server { get; private set; } = null!;

        public int Frames { get; private set; }

        public HeadlessRunResult Finish(double maxSeconds = 240)
        {
            for (var t = 0.0; t < maxSeconds && !Runner.IsDone; t += 1.0 / 60.0)
            {
                Time.Advance(TimeSpan.FromSeconds(1.0 / 60.0));
                Runner.Step(Runner.Session.Clock.Now);
                Frames++;
            }

            Assert.True(Runner.IsDone, $"the run did not end within {maxSeconds} s (stage {Runner.Scenario.Stage})");
            return Runner.Result;
        }

        public void Dispose() => Runner.Dispose();
    }
}
