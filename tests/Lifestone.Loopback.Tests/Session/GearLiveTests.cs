using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M3 1.4's gear scenario against the bundled ACE (<c>--scenario=gear</c>, spec 5.2 and 5.3): the headless host logs in on the test account, enters BSD Mage (which must already exist: run the mage build first), has the server mod
/// grant the kit (the wand, the leather set and the profile's components as the account casts them), wields the wand and wears the five pieces through GameAction 0x001A, and logs off. It runs twice in a row.
/// <b>What it proves:</b> the first run ends <c>gear=wielded worn=5</c> with every kit line held (<c>kit=n/n</c>): ACE accepted the grants and each wield, and the read model, fed only by GameEvent 0x0023 and 0x0022 (ACE's 0x02DA and 0x02CE
/// zeros come before them), shows the wand at HeldCaster and the five pieces at their locations. The second run must end <c>gear=wielded worn=5</c> with <c>gear_steps=0</c>: nothing was granted and nothing wielded, which proves the scenario is
/// idempotent, that the top-up counts what the pack holds (a merged stack included: SpawnGear merges each top-up into the held stack), and that the worn pieces survive a log off and a new login. The two judged runs pass
/// <c>cast: false</c> (M3 1.4 fix B, review 1.4-R1: a cast burns components at random, so a second run's top-up would depend on the first run's luck), so <c>gear_steps=0</c> judges the gear alone and <c>cast=n/a</c>; the component top-up is its own
/// field (<c>topup=</c>, printed, not asserted). A third run with the cast on (M3 1.4 s2: the magic combat mode, one Creature Enchantment self buff, back to Non-combat) is report-only: its <c>cast=ok|fizzled|failed|n/a</c> is printed and
/// nothing about it is asserted (the first casts are judged by <c>CastLiveTests</c>); it adds no flake of its own, since a cast never fails the run (a cast that cannot be made is <c>cast=failed</c> with a warning).
/// Skipped, with the probe's reason, unless the backend runs with the Lifestone server mod ready.
/// </summary>
/// <remarks>
/// It enters BSD Mage and no other character, creates nothing and deletes nothing; the gear does not level the mage (owner decision D3). The account name the kit is built for is read from the secrets file at run time and is in no output.
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
public sealed class GearLiveTests(ITestOutputHelper output)
{
    private static readonly Regex Wielded = new(
        @"^gear=wielded worn=5 kit=(?<held>\d+)/(?<lines>\d+) gear_steps=(?<steps>\d+) topup=(?<topup>\d+) cast=(?<cast>ok|fizzled|failed|n/a)$",
        RegexOptions.CultureInvariant);

    [LoopbackFact]
    public void The_kit_is_granted_the_wand_wielded_and_the_armour_worn_and_a_second_run_sends_no_gear_step()
    {
        using var dats = DatSet.Open();
        var spells = SpellTableData.Load(dats).Spells;
        var first = RunOnce(dats, spells, "first", cast: false, out var firstMissing);
        var firstMatch = Wielded.Match(first);
        Assert.True(firstMatch.Success, first);
        Assert.True(firstMatch.Groups["lines"].Value == firstMatch.Groups["held"].Value, "every kit line is held; missing: " + MissingText(firstMissing) + " in " + first); // every kit line is held

        var second = RunOnce(dats, spells, "second", cast: false, out var secondMissing);
        var secondMatch = Wielded.Match(second);
        Assert.True(secondMatch.Success, second);
        Assert.Equal("0", secondMatch.Groups["steps"].Value); // the wand, the armour and the wields: none (the components are topup=, not asserted)
        Assert.Equal("n/a", secondMatch.Groups["cast"].Value);
        Assert.True(firstMatch.Groups["lines"].Value == secondMatch.Groups["held"].Value, "the second run holds every kit line; missing: " + MissingText(secondMissing) + " in " + second);

        // Report-only: the cast on (it burns components and mana; the top-up the next run needs is its own field).
        var third = RunOnce(dats, spells, "third (cast on, report-only)", cast: true, out _);
        Assert.True(Wielded.Match(third).Success, third);
    }

    private static string MissingText(IReadOnlyList<string> missing) => missing.Count == 0 ? "none" : string.Join("; ", missing);

    private string RunOnce(DatSet dats, IReadOnlyDictionary<uint, SpellInfo> spells, string label, bool cast, out IReadOnlyList<string> missing)
    {
        var files = BackendProbe.MainCheckoutFiles;
        var log = new RecordingLogSink();
        using var recording = new MemoryStream();
        var secrets = File.ReadAllBytes(files.SecretsFile);
        GearScenario scenario;
        HeadlessRunner runner;
        try
        {
            string account;
            using (var credentials = PlayAccounts.Parse(secrets, AccountChoice.FromOption(BackendProbe.TestAccountKey, AccountChoice.Default)))
            {
                account = credentials.Account;
            }

            scenario = new GearScenario(spells, account, log, cast: cast);
            runner = HeadlessRunner.Start(
                new HeadlessRunOptions
                {
                    Session = new SessionOptions { Account = BackendProbe.TestAccountKey, Server = BackendProbe.Probe().AceEndPoint! },
                    QuitAfterSeconds = 300,
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

        HeadlessRunResult result;
        var context = new GameThreadSynchronizationContext();
        using (context.Install())
        using (runner)
        {
            result = runner.Run(context, CancellationToken.None);
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{label}] {result.SummaryLine("memory")}"));
        output.WriteLine($"[{label}] {scenario.VerdictFields()}"); // includes cast=..., report-only
        output.WriteLine($"[{label}] cast={scenario.Cast} spell={scenario.CastSpell ?? "(none)"}");
        foreach (var wield in scenario.SentWields)
        {
            output.WriteLine($"[{label}] {wield}");
        }

        foreach (var line in log.Lines.Where(l => l.Contains("[gear]", StringComparison.Ordinal) || l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        Assert.True(result.Ok, result.Failure);
        Assert.Equal(0, result.Errors);
        missing = [.. scenario.MissingKit];
        return scenario.VerdictFields();
    }
}
