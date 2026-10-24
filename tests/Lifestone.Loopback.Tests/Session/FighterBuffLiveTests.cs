using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M4 P5a C4's fighters' buff leg against the bundled ACE (<c>--scenario=buff --account=test-melee|test-archer</c>): the Holtburg Brawler and the Holtburg Archer buff themselves through the caster swap. WRITTEN AND COMPILED BY
/// THE SLICE, RUN BY THE LEAD (a slice never runs a loopback test; it takes a full pass of twelve level IV buffs at a fighter's mana regeneration: about 700 s of session time with the Stamina to Mana Self transfers and the
/// regeneration waits, and the headless limit is 3000 s). What each fact PROVES: the character found on its own account (built by the fighter build; this test never builds) swaps its weapon for the caster (the weapon and the
/// arrows to the pack, The Healer's Heart wielded, the magic combat mode), casts the ordered pass of its twelve lines at the highest level the 0.9 chance rule allows with the transfer line when the pool is short, runs the forced re-buff
/// pass (one line recast while still active), swaps back and logs off: the verdict reads <c>buffs=12/12</c> (every line of the profile active on the read model), <c>rebuffed=yes</c> and <c>swapped=yes</c>: those three are judged, and the scenario ended <c>done</c>. Report-only in this first gate (P5a-R9):
/// <c>misses=</c>, <c>forced=</c>, <c>transfers=</c>, <c>fizzles=</c>, <c>mana_min=</c>, <c>swap_s=</c> and the swap's end state (<see cref="SwapStatus.Done"/>: the weapon wielded again, the player in peace, the caster out of the hand) are printed for the lead to read. The mage's twin is <see cref="BuffLoopLiveTests"/>.
/// <para>The lead runs these: backend up with the server mod, the fighters built to level 55 with the caster in the pack (the fighter build's rerun does that), accounts <c>test-melee</c> and <c>test-archer</c> in secrets.json (key names only).</para>
/// </summary>
/// <remarks>
/// These are among the only tests allowed to log in as Holtburg Brawler and Holtburg Archer (levelled only by their build and the loops' kills, never reset or purged). Each enters its own character and no other.
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-melee")]
[LoopbackCharacter("Holtburg Brawler")]
public sealed class FighterBrawlerBuffLiveTests(ITestOutputHelper output)
{
    /// <summary>Holtburg Brawler on <c>test-melee</c>: swap in once, the pass, the forced pass, swap out; the Mace back in hand.</summary>
    [LoopbackFact]
    public void The_brawler_buffs_itself_through_the_caster_swap_and_ends_with_its_kit_weapon_in_hand() => new FighterBuffRun(output).Verify("melee");
}

/// <summary>Holtburg Archer on <c>test-archer</c>; see <see cref="FighterBrawlerBuffLiveTests"/>.</summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-archer")]
[LoopbackCharacter("Holtburg Archer")]
public sealed class FighterArcherBuffLiveTests(ITestOutputHelper output)
{
    /// <summary>Holtburg Archer on <c>test-archer</c>: swap in once, the pass, the forced pass, swap out; the Longbow and the arrows back in hand.</summary>
    [LoopbackFact]
    public void The_archer_buffs_itself_through_the_caster_swap_and_ends_with_its_kit_bow_and_arrows_in_hand() => new FighterBuffRun(output).Verify("archer");
}

/// <summary>The run both classes share: one headless session on the fighter's account and the buff scenario that logs in, buffs, logs off and quits (the headless host's own shape).</summary>
internal sealed class FighterBuffRun(ITestOutputHelper output)
{
    /// <summary>The headless limit of <c>--scenario=buff</c> (Program.BuffQuitAfterSeconds), in session seconds.</summary>
    private const double QuitAfterSeconds = 3000;

    /// <summary>
    /// What the fact judges (M4 P5a fix F-abil, P5a-R9: the new fields are report-only in their first gate): <c>buffs=n/n rebuffed=yes swapped=yes</c> and nothing else. The other fields of the verdict (<c>forced=</c>, <c>misses=</c>, <c>transfers=</c>,
    /// <c>fizzles=</c>, <c>mana_min=</c>, <c>swap_s=</c>, ...) and the swap's end state are printed, not asserted; the lead reads them and a later gate may judge them.
    /// </summary>
    private static readonly Regex Verdict = new(
        @"^gear=n/a worn=n/a buffs=(?<active>\d+)/(?<wanted>\d+) .* rebuffed=yes .*swapped=yes( |$)",
        RegexOptions.CultureInvariant);

    internal void Verify(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        var files = BackendProbe.MainCheckoutFiles;
        using var dats = DatSet.Open();
        var log = new RecordingLogSink();
        using var recording = new MemoryStream();
        var scenario = new BuffScenario(template, SpellTableData.Load(dats).Spells, new BuffOptions(), log, endSession: true);

        HeadlessRunResult result;
        LiveCharacter.Release(output, template.AccountKey);
        var secrets = File.ReadAllBytes(files.SecretsFile);
        HeadlessRunner runner;
        try
        {
            runner = HeadlessRunner.Start(
                new HeadlessRunOptions
                {
                    Session = new SessionOptions { Account = template.AccountKey, Server = BackendProbe.Probe().AceEndPoint! },
                    QuitAfterSeconds = QuitAfterSeconds,
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

        output.WriteLine($"{template.Key} {result.SummaryLine("memory")}");
        output.WriteLine($"{template.Key} buff {scenario.VerdictFields()}");
        foreach (var line in log.Lines.Where(l => l.Contains("[buff]", StringComparison.Ordinal) || l.Contains("[swap]", StringComparison.Ordinal) || l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        Assert.True(result.Ok, result.Failure);
        Assert.Equal(0, result.Errors);
        Assert.Null(scenario.Failure);
        Assert.Equal("done", scenario.Stage);

        // ok=true buffs=n/n rebuffed=yes swapped=yes: every judged field reads what it names.
        var match = Verdict.Match(scenario.VerdictFields());
        Assert.True(match.Success, scenario.VerdictFields());
        Assert.Equal(match.Groups["wanted"].Value, match.Groups["active"].Value);
        Assert.Equal(FighterProfile.For(template)!.Lines.Count(l => l.IsBuff).ToString(System.Globalization.CultureInfo.InvariantCulture), match.Groups["wanted"].Value);

        // Report-only (P5a-R9): the weapon back in hand and the player in peace (the swap's Out half reported done, the caster out of the hand). swapped=yes above is the judged part.
        Assert.NotNull(scenario.Swap);
        output.WriteLine($"{template.Key} swap end state (report-only): status={scenario.Swap.Status} swapped={scenario.Swap.Swapped} caster_wielded={scenario.Swap.CasterWielded}");
    }
}
