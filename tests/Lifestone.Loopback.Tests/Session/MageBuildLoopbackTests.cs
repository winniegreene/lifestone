using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M3 1.1's mage build against the bundled ACE (<c>--scenario=mage-build</c>, spec 5.3, DoD 2): the headless host logs in on the test account, finds
/// "BSD Mage" or creates it, and builds it to the template (the mod's <c>xp</c> to level 126, the four skills trained, every target raised, the arena
/// profile's spells learned). The scenario runs twice in a row. The first run ends <c>mage=ready</c> (the mage was already built) or <c>mage=built</c>,
/// with a level of at least the target, <c>template=ok</c> and every spell known or learned; the second must end <c>mage=ready</c> with
/// <c>built_steps=0</c>: nothing sent but the spellbook check, which proves the scenario is idempotent and that a ready mage is only verified.
/// Skipped, with the probe's reason, unless the backend runs with the Lifestone server mod (with the <c>spell learn</c> verb) ready.
/// </summary>
/// <remarks>
/// This is the only test allowed to build BSD Mage (owner decision D3: the mage is levelled only by its build and the loops' kills, never reset or purged).
/// It enters that character and no other, creates nothing else and deletes nothing.
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test")]
public sealed class MageBuildLoopbackTests(ITestOutputHelper output)
{
    private static readonly Regex Built = new(
        @"^mage=(ready|built) level=(?<level>\d+) template=ok spells=(?<known>\d+)/(?<all>\d+) built_steps=(?<steps>\d+)$",
        RegexOptions.CultureInvariant);

    [LoopbackFact]
    public void The_mage_is_built_or_verified_and_a_second_run_changes_nothing()
    {
        using var dats = DatSet.Open();
        var first = RunOnce(dats, "first");
        var firstMatch = Built.Match(first.Verdict);
        Assert.True(firstMatch.Success, first.Verdict);
        Assert.True(int.Parse(firstMatch.Groups["level"].Value) >= MageTemplate.TargetLevel, first.Verdict);
        Assert.Equal(ArenaProfile.SpellsToLearn.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), firstMatch.Groups["all"].Value);
        Assert.Equal(firstMatch.Groups["all"].Value, firstMatch.Groups["known"].Value);

        var second = RunOnce(dats, "second");
        var secondMatch = Built.Match(second.Verdict);
        Assert.True(secondMatch.Success, second.Verdict);
        Assert.StartsWith("mage=ready ", second.Verdict, StringComparison.Ordinal);
        Assert.Equal("0", secondMatch.Groups["steps"].Value);
        Assert.Equal(firstMatch.Groups["all"].Value, secondMatch.Groups["known"].Value);
        Assert.All(second.Steps, s => Assert.StartsWith("learn spells ", s, StringComparison.Ordinal)); // only the spellbook check was sent
    }

    private (string Verdict, IReadOnlyList<string> Steps) RunOnce(DatSet dats, string label)
    {
        var files = BackendProbe.MainCheckoutFiles;
        var log = new RecordingLogSink();
        using var recording = new MemoryStream();
        var scenario = new MageBuildScenario(XpTableData.Load(dats), HeadlessCharacter.RetailSkillClasses(dats), log);

        HeadlessRunResult result;
        LiveCharacter.Release(output, BackendProbe.TestAccountKey);
        var secrets = File.ReadAllBytes(files.SecretsFile);
        HeadlessRunner runner;
        try
        {
            runner = HeadlessRunner.Start(
                new HeadlessRunOptions
                {
                    Session = new SessionOptions { Account = BackendProbe.TestAccountKey, Server = BackendProbe.Probe().AceEndPoint! },
                    QuitAfterSeconds = 600,
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

        output.WriteLine($"[{label}] {result.SummaryLine("memory")}");
        output.WriteLine($"[{label}] {scenario.VerdictFields()}");
        foreach (var step in scenario.SentSteps)
        {
            output.WriteLine($"[{label}] step: {step}");
        }

        foreach (var line in log.Lines.Where(l => l.Contains("mage", StringComparison.Ordinal) || l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        Assert.True(result.Ok, result.Failure);
        Assert.Equal(0, result.Errors);
        return (scenario.VerdictFields(), scenario.SentSteps);
    }
}
