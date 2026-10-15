using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The login check with movement legs (<c>--login-check=academy-exit|holtburg-laps|all</c>, G6 W12) against <see cref="ScriptedAceSession"/> playing ACE for a
/// walk and a portal: the legs are the <see cref="RoutePlay"/>s of the Holtburg laps and the Academy exit, played in order between the settle and the log off,
/// and the verdict fields the game prints (<see cref="LoginCheckScenario.Corrections"/>, <see cref="LoginCheckScenario.WhereMaxMetres"/>,
/// <see cref="LoginCheckScenario.LegTeleports"/>, <see cref="LoginCheckScenario.ExitRefusedByQuest"/>) come from them. The legs here make their own walkers; the
/// hosted path (the game's player) needs the engine and is covered by the live <c>--login-check</c> runs.
/// </summary>
public sealed class LoginCheckLegsTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly uint[] SkillClasses = [.. Enumerable.Repeat(2u, 55)];

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void The_laps_and_the_exit_legs_play_in_order_and_their_results_are_the_verdict()
    {
        using var run = new Run(Dats, configure: s => (s.AcceptsMovement, s.ExitPortalAt, s.PortalQuest) = (true, AcademyExitScenarioTests.PortalPosition(Dats), "SentryTaskComplete"), exitLeg: true);
        var result = run.Finish();
        var scenario = run.Scenario;
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}");
        foreach (var leg in scenario.Legs)
        {
            output.WriteLine($"{leg.Name}: {leg.SummaryLine}");
        }

        Assert.True(result.Ok, result.Failure);
        Assert.True(scenario.Ok);
        Assert.Equal("done", scenario.Stage);
        Assert.Equal(2, scenario.Legs.Count);
        Assert.All(scenario.Legs, leg => Assert.Equal("done", leg.Stage));
        Assert.Equal(0, scenario.Corrections);
        Assert.InRange(scenario.WhereMaxMetres!.Value, 0.0, 0.5);
        Assert.Equal(scenario.Legs.Max(l => l.WhereMaxMetres), scenario.WhereMaxMetres); // the largest of the legs' own
        Assert.Equal(0, scenario.Teleports); // before the settle: the arrival only
        Assert.Equal(5, scenario.LegTeleports); // each leg: the route's start and home; the exit's own portal teleport
        Assert.Equal(5, scenario.LegTeleportEpisodes); // one portal-space visit each
        Assert.True(scenario.ExitTeleported); // the exit is taken, not refused: the quest was stamped for the Use, the portal's emote set erased it, and the way home found it as it began
        Assert.Equal(new[] { "has SentryTaskComplete", "stamp SentryTaskComplete", "has SentryTaskComplete" }, run.Server.QuestRequests.ToArray());
        Assert.Empty(run.Server.Quests);
        Assert.Equal("put-back", scenario.ExitQuestVerdict); // the verdict line's quest=
        Assert.True(scenario.LogOffOk);
        Assert.Equal(1, run.Server.Quits);
        Assert.Equal(1, run.Server.PortalTeleports);
        Assert.Equal(0, run.Server.PortalRefusals);
        Assert.Equal(0, run.Server.IgnoredWhileTeleporting);
    }

    [RequiresDatsFact]
    public void A_teleport_announced_while_the_previous_one_is_in_flight_shares_its_portal_space_visit()
    {
        // The live ACE announces the portal's teleport twice in a row when the Use's MoveTo and the contact with the portal both activate it: one visit, one LoginComplete.
        using var run = new Run(Dats, configure: s => (s.AcceptsMovement, s.ExitPortalAt, s.PortalQuest, s.PortalTeleportsTwice) = (true, AcademyExitScenarioTests.PortalPosition(Dats), "SentryTaskComplete", true), exitLeg: true);
        var result = run.Finish();
        var scenario = run.Scenario;
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}");

        Assert.True(result.Ok, result.Failure);
        Assert.Equal(6, scenario.LegTeleports); // the route starts, the portal twice, the homes
        Assert.Equal(5, scenario.LegTeleportEpisodes); // the two announcements of the portal are one visit
        Assert.True(scenario.ExitTeleported);
        Assert.Equal(0, scenario.Corrections);
    }

    [RequiresDatsFact]
    public void A_check_whose_exit_leg_took_the_portals_refusal_is_not_a_pass()
    {
        // The quest taken away (the refusal path): the leg passes by its own rule, but the login check wants the teleport.
        using var run = new Run(Dats, configure: s => (s.AcceptsMovement, s.ExitPortalAt, s.PortalQuest) = (true, AcademyExitScenarioTests.PortalPosition(Dats), "SentryTaskComplete"), exitLeg: true, exitQuest: ExitQuestMode.Erase);
        var result = run.Finish();
        var scenario = run.Scenario;
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}");

        Assert.Equal("done", scenario.Stage); // it ran to the end and logged off ...
        Assert.True(scenario.LogOffOk);
        Assert.All(scenario.Legs, leg => Assert.Equal("done", leg.Stage));
        Assert.Equal(1, run.Server.PortalRefusals);
        Assert.False(scenario.ExitTeleported);
        Assert.Equal("unchanged", scenario.ExitQuestVerdict); // the character held nothing and the refusal path erased nothing
        Assert.False(scenario.Ok); // ... and is not a pass
    }

    [RequiresDatsFact]
    public void A_leg_that_fails_keeps_the_stage_at_playing_for_the_runners_recovery_and_names_the_leg()
    {
        // The server disagrees about where the walk is: the Holtburg leg's audit fails, and the check stops there.
        using var run = new Run(Dats, configure: s => (s.AcceptsMovement, s.WalkWhereOffsetMetres) = (true, 3f), exitLeg: false);
        var result = run.Finish();
        var scenario = run.Scenario;
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}");

        Assert.False(result.Ok);
        Assert.False(scenario.Ok);
        Assert.Equal("playing", scenario.Stage); // the game's runner recovers the character from a failure in this stage
        Assert.Contains("playing holtburg-laps", scenario.Failure, StringComparison.Ordinal);
        Assert.False(scenario.LogOffOk);
    }

    /// <summary>A headless run of the login check over the scripted server, stepped at 60 Hz on a manual clock.</summary>
    private sealed class Run : IDisposable
    {
        public Run(DatSet dats, Action<ScriptedAceSession> configure, bool exitLeg, ExitQuestMode exitQuest = ExitQuestMode.Stamp)
        {
            var legs = new List<RoutePlay> { new HoltburgLapsScenario(dats, Log, laps: 1).Play };
            if (exitLeg)
            {
                legs.Add(new AcademyExitScenario(dats, Log, portalName: "Exit Portal", exitQuest: exitQuest).Play);
            }

            Scenario = new LoginCheckScenario(SkillClasses, "Amy", legs: legs);
            Runner = HeadlessRunner.StartWith(
                new HeadlessRunOptions { Session = new SessionOptions { Account = "test" }, QuitAfterSeconds = 900 },
                Scenario,
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
            Runner.Session.Actions.Login(ActionSource.Host); // what the game's session host does at start-up
        }

        public ManualTimeProvider Time { get; } = new();

        public RecordingLogSink Log { get; } = new();

        public LoginCheckScenario Scenario { get; }

        public HeadlessRunner Runner { get; }

        public ScriptedAceSession Server { get; private set; } = null!;

        public HeadlessRunResult Finish(double maxSeconds = 800)
        {
            for (var t = 0.0; t < maxSeconds && !Runner.IsDone; t += 1.0 / 60.0)
            {
                Time.Advance(TimeSpan.FromSeconds(1.0 / 60.0));
                Runner.Step(Runner.Session.Clock.Now);
            }

            Assert.True(Runner.IsDone, $"the run did not end within {maxSeconds} s (stage {Scenario.Stage})");
            return Runner.Result;
        }

        public void Dispose()
        {
            Runner.Dispose();
            Scenario.Dispose();
        }
    }
}
