using System.Globalization;
using System.Security.Cryptography;
using Lifestone.Client.Options;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests;
using Lifestone.Data.Dat;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// What the three hunt loopback tests share (M4 1.2 s4b): the headless runner entering Holtburg Hunter on the test account, running <see cref="HuntScenario"/> with the given options, and recording the whole session to
/// <c>cache/m4/1-2/live/&lt;UTC&gt;-&lt;name&gt;.lsrec</c> of the main checkout (as the attack capture test did; the recordings are derived data under the gitignored <c>cache/</c>). The ground heights come from the retail dats,
/// as the headless host's <c>--scenario=hunt</c> reads them. Not a test itself.
/// </summary>
internal static class HuntLiveRun
{
    /// <summary>The headless limit of a run, in seconds (the bind, the belt's regeneration, the swap, the hunt's 300 s, the wind-down and the log off).</summary>
    internal const double QuitAfterSeconds = 1500;

    /// <summary>What one run produced.</summary>
    internal sealed record Outcome(HuntScenario Scenario, HeadlessRunResult Result, IReadOnlyList<string> Lines, string Recording);

    /// <summary>
    /// Runs the hunt the command-line options name (<c>--hunt-kills</c>, <c>--hunt-seconds</c>, <c>--hunt-weapon</c>, <c>--hunt-ground</c>, <c>--loot*</c>, <c>--heal</c>, <c>--heal-below</c>, <c>--heal-until</c>, <c>--heal-chance</c>; M4 3.1 s4) as
    /// the headless host and the game's hunt leg read them: <see cref="HuntOptions.FromArguments"/>, a bad value failing the test with the option's own message. <paramref name="quitAfterSeconds"/> is the headless limit of the run.
    /// </summary>
    internal static Outcome Run(ITestOutputHelper output, string name, double quitAfterSeconds, params string[] arguments)
    {
        var options = HuntOptions.FromArguments(CommandLineArgument.ParseAll(arguments));
        Assert.True(options.Errors.Count == 0, "the hunt options are wrong: " + string.Join("; ", options.Errors));
        return Run(output, name, options, quitAfterSeconds);
    }

    /// <summary>
    /// Runs the hunt with <paramref name="options"/> against the live backend and prints the run's summary, the hunt's log lines and its verdict fields to <paramref name="output"/>. The ground is the options' own (M4 3.1 s4:
    /// <c>--hunt-ground</c> was ignored here, the belt always), and the recovery options travel inside <paramref name="options"/> to the scenario.
    /// </summary>
    internal static Outcome Run(ITestOutputHelper output, string name, HuntOptions options, double quitAfterSeconds = QuitAfterSeconds)
    {
        using var dats = DatSet.Open();
        var files = BackendProbe.MainCheckoutFiles;
        var log = new RecordingLogSink();
        var heights = new Dictionary<uint, float?>();
        var scenario = new HuntScenario(
            options,
            log,
            cell =>
            {
                if (!heights.TryGetValue(cell.CellId, out var height))
                {
                    heights[cell.CellId] = height = HuntGround.TerrainHeight(dats, cell);
                }

                return height;
            },
            options.GroundRow,
            follow: new HuntFollow(dats, log)); // the client's own body follows ACE's approach, as the headless host's hunt does (Program.cs; fix3-R1)
        var folder = Path.Combine(RepositoryFiles.MainCheckout, "cache", "m4", "1-2", "live");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + name + RecordingFormat.FileExtension);

        HeadlessRunResult result;
        using (var recording = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
        {
            var secrets = File.ReadAllBytes(files.SecretsFile);
            HeadlessRunner runner;
            try
            {
                runner = HeadlessRunner.Start(
                    new HeadlessRunOptions
                    {
                        Session = new SessionOptions { Account = BackendProbe.TestAccountKey, Server = BackendProbe.Probe().AceEndPoint! },
                        QuitAfterSeconds = quitAfterSeconds,
                    },
                    scenario,
                    secrets,
                    File.ReadAllBytes(files.PidsFile),
                    dats,
                    TimeProvider.System,
                    log,
                    recording);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secrets);
            }

            var context = new GameThreadSynchronizationContext();
            using (context.Install())
            using (runner)
            {
                result = runner.Run(context, CancellationToken.None);
            }
        }

        output.WriteLine(result.SummaryLine(Path.GetFileName(path)));
        var lines = log.Lines.Where(l => l.Contains("[hunt]", StringComparison.Ordinal) || l.Contains("[fight]", StringComparison.Ordinal) || l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)).ToList();
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        output.WriteLine("hunt " + scenario.VerdictFields());
        output.WriteLine("hunt detail " + scenario.DetailFields());
        return new Outcome(scenario, result, lines, path);
    }
}
