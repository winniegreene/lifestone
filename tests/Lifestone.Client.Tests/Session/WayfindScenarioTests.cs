using System.Text.RegularExpressions;
using Lifestone.Client.Options;
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
/// The wayfind scenario (P5a S6, Exhaustive tier: it reads the dats) against <see cref="ScriptedAceSession"/> playing ACE for a walk: the character logs in at the Academy, is placed at
/// <c>--from</c> by one <c>place loc</c>, the streamer builds the collision and tiles round it on its workers, the runner walks across a landblock border to <c>--to</c> with the human's keys,
/// then back with <c>--wayfind-back</c>, the server echoing every position it receives. What this cannot show (the live run proves it): that ACE accepts the keys the runner presses over
/// 2 km, and the corridor's reach over that many landblocks (the dat test of the streamer shows the corridor on the belt route).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class WayfindScenarioTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly Regex Leg = new(
        @"^wayfind leg=(out|back) ok=true route_m=\d+\.\d run_s=\d+\.\d speed_mps=\d+\.\d\d plans=\d+ replans=\d+ rungs=0 blocked=0 stuck=0 corrections=0 where_start_m=\d+\.\d\d where_end_m=\d+\.\d\d where_max_m=\d+\.\d\d settle_s=\d+\.\d teleports=\d landblocks_peak=\d+ turns=\d+ key_changes_per_min=\d+$",
        RegexOptions.CultureInvariant);

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void The_character_is_placed_at_from_runs_across_a_landblock_border_to_the_cell_and_back_with_a_line_for_each_leg()
    {
        // Holtburg lifestone (cell 0xA9B40019) to the first cell of the landblock to the east: 120 m across the border.
        var options = WayfindOptions.FromArguments(CommandLineArgument.ParseAll(["--from=0xA9B40019@84,7.7,94", "--to=0xAAB40001", "--wayfind-back"]));
        Assert.Empty(options.Errors);
        using var run = new Run(Dats, options, configure: s => s.AcceptsMovement = true);
        var result = run.Finish();
        var scenario = (WayfindScenario)run.Runner.Scenario;
        output.WriteLine($"{result.SummaryLine("memory")} ({run.Frames} frames, {run.WallSeconds:F1} s wall)");
        foreach (var line in scenario.Lines)
        {
            output.WriteLine(line);
        }

        output.WriteLine(scenario.StreamerLine);
        output.WriteLine(scenario.DoneLine(result.Ok));

        Assert.True(result.Ok, result.Failure);
        Assert.Equal(2, scenario.Lines.Count);
        Assert.Matches(Leg, scenario.Lines[0]);
        Assert.Matches(Leg, scenario.Lines[1]);
        Assert.StartsWith("wayfind leg=out ", scenario.Lines[0], StringComparison.Ordinal);
        Assert.StartsWith("wayfind leg=back ", scenario.Lines[1], StringComparison.Ordinal);
        Assert.Contains(" teleports=1 ", scenario.Lines[0], StringComparison.Ordinal); // the one place
        Assert.Contains(" teleports=0 ", scenario.Lines[1], StringComparison.Ordinal);
        Assert.Equal("wayfind done ok=true legs=2", scenario.DoneLine(result.Ok));
        Assert.Equal(1, run.Server.Talked.Count(t => t.StartsWith("@lifestone place loc ", StringComparison.Ordinal)));
        Assert.Equal(0, result.Corrections);
        Assert.InRange(result.WhereMaxMetres!.Value, 0.0, 0.5);
        Assert.Equal(0, run.Server.IgnoredWhileTeleporting);
        Assert.True(run.Server.PositionMessages >= 20, $"{run.Server.PositionMessages} position messages: the keys were pressed and the body reported them");
        var streamer = scenario.Streamer!;
        Assert.InRange(streamer.PeakAttached, 9, 12);
        Assert.Equal(0, streamer.TileFailures);
        Assert.Contains("wayfind streamer landblocks_peak=", scenario.StreamerLine, StringComparison.Ordinal);
        Assert.Contains((MessageOpcodes.CharacterLogOff, 0u), run.Server.Sent);
    }

    [RequiresDatsFact]
    public void An_indoor_end_is_refused_before_anything_moves()
    {
        // The scripted player begins in the Academy: an indoor --to is refused at the first step, with no place sent.
        var indoor = WayfindOptions.FromArguments(CommandLineArgument.ParseAll(["--to=0xA9B4013F@10,10,94"]));
        Assert.Empty(indoor.Errors);
        using var run = new Run(Dats, indoor, configure: s => s.AcceptsMovement = true);
        var result = run.Finish();
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}");

        Assert.False(result.Ok);
        Assert.Contains("TODO(wayfind-indoor)", result.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Server.Talked, t => t.StartsWith("@lifestone place", StringComparison.Ordinal));
        Assert.Empty(((WayfindScenario)run.Runner.Scenario).Lines);
    }

    /// <summary>A headless run of the wayfind scenario over the scripted server, stepped at 60 Hz on a manual clock; real time passes while the streamer's workers build.</summary>
    private sealed class Run : IDisposable
    {
        private readonly System.Diagnostics.Stopwatch _wall = System.Diagnostics.Stopwatch.StartNew();

        public Run(DatSet dats, WayfindOptions options, Action<ScriptedAceSession> configure)
        {
            Time = new ManualTimeProvider();
            Runner = HeadlessRunner.StartWith(
                new HeadlessRunOptions { Session = new SessionOptions { Account = "test" }, QuitAfterSeconds = 3000 },
                new WayfindScenario(dats, Log, options, character: "Amy"),
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

        public double WallSeconds => _wall.Elapsed.TotalSeconds;

        public HeadlessRunResult Finish(double maxSeconds = 1500)
        {
            var scenario = (WayfindScenario)Runner.Scenario;
            for (var t = 0.0; t < maxSeconds && !Runner.IsDone; t += 1.0 / 60.0)
            {
                Time.Advance(TimeSpan.FromSeconds(1.0 / 60.0));
                Runner.Step(Runner.Session.Clock.Now);
                Frames++;
                if (scenario.Streamer is { Busy: true } || scenario.Stage.EndsWith("settling", StringComparison.Ordinal))
                {
                    Thread.Sleep(1); // the workers run in real time, the clock in simulated time
                }
            }

            Assert.True(Runner.IsDone, $"the run did not end within {maxSeconds} s (stage {Runner.Scenario.Stage})");
            return Runner.Result;
        }

        public void Dispose() => Runner.Dispose();
    }
}
