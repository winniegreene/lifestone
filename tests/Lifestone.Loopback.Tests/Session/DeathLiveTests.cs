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

/// <summary>What a live death-scenario run left behind (shared by <see cref="DeathLiveTests"/> and <see cref="IncomingDamageLiveTests"/>).</summary>
/// <param name="Result">The headless run's result.</param>
/// <param name="Scenario">The scenario (its tally and verdict).</param>
/// <param name="Lines">The scenario's, gear's, WARN and ERROR log lines.</param>
internal sealed record DeathLiveRun(HeadlessRunResult Result, DeathScenario Scenario, IReadOnlyList<string> Lines);

/// <summary>Runs <see cref="DeathScenario"/> headless against the bundled ACE (the lead's live tests; a helper of this file's two classes).</summary>
internal static class DeathLive
{
    /// <summary>The headless limit beyond <c>--death-seconds</c>: the gear, the bind, a suicide, the respawn, the regear and the log off (the headless host's <c>DeathQuitAfterOverheadSeconds</c>).</summary>
    internal const double OverheadSeconds = 900;

    internal static DeathLiveRun Run(ITestOutputHelper output, DeathOptions options, DeathStages stages)
    {
        using var dats = DatSet.Open();
        var spells = SpellTableData.Load(dats).Spells;
        var files = BackendProbe.MainCheckoutFiles;
        var log = new RecordingLogSink();
        var secrets = File.ReadAllBytes(files.SecretsFile);
        DeathScenario scenario;
        HeadlessRunner runner;
        try
        {
            string account;
            using (var credentials = PlayAccounts.Parse(secrets, AccountChoice.FromOption(BackendProbe.TestAccountKey, AccountChoice.Default)))
            {
                account = credentials.Account;
            }

            scenario = new DeathScenario(spells, account, options, DenWalk.Create(dats, log), log, stages: stages, approach: HoltburgApproach.Create(dats, log));
            runner = HeadlessRunner.Start(
                new HeadlessRunOptions
                {
                    Session = new SessionOptions { Account = BackendProbe.TestAccountKey, Server = BackendProbe.Probe().AceEndPoint! },
                    QuitAfterSeconds = OverheadSeconds + options.Seconds,
                },
                scenario,
                secrets,
                File.ReadAllBytes(files.PidsFile),
                dats,
                TimeProvider.System,
                log,
                recording: null);
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

        output.WriteLine(result.SummaryLine("memory"));
        output.WriteLine("death " + scenario.VerdictFields());
        var lines = log.Lines.Where(l => l.Contains("[death]", StringComparison.Ordinal) || l.Contains("[gear]", StringComparison.Ordinal) || l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)).ToList();
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        return new DeathLiveRun(result, scenario, lines);
    }
}

/// <summary>
/// M3 3.2 lane da, DoD 10: the death-and-regear scenario against the bundled ACE (<c>--scenario=death-and-regear</c>). WRITTEN AND COMPILED BY THE LANE, RUN BY THE LEAD (it needs the backend, the mod and BSD Mage built;
/// it changes the character: vitae, dropped items, a corpse in the den). On <c>BSD Mage</c> only (the test account) the headless runner gears the mage, puts its spell bars right, binds it at Holtburg's lifestone (D34),
/// sends it to the Black Spawn Den, suicides there when any enchantment but the vitae is active (D33), waits in non-combat until the tuskers kill it, respawns, re-gears and logs off. Neither this test nor the scenario
/// resets the mage.
/// </summary>
/// <remarks>
/// <para><b>What it proves</b> (each assertion names the ACE behaviour it relies on).
/// (a) <c>died == 1</c>: exactly one GameEvent 0x01AC VictimNotification, the tuskers' death (<c>Player_Death.cs</c> 34-90); a suicide sends none (299-345).
/// (b) <c>vitae &gt; 0</c> in the verdict, whose measures were frozen when the scenario concluded (spell 666 on the player after a death, <c>EnchantmentManager.cs</c> 391-443; the level is not asserted, D35). The vitae stage (D37) then pays it
/// back with the <c>xp</c> verb, so the tally reads 0 at the end: <c>vitae_paid &gt; 0</c> and <c>vitae_after=0</c> are asserted instead of the tally.
/// (c) <c>respawn=lifestone</c>: the death's teleport (F751 and the position) ended within 15 m of a <c>BF_LIFESTONE</c> object (<c>Player_Death.cs</c> 247-292: the sanctuary the bind set, <c>Lifestone.cs</c> 55-75).
/// (d) The death's own corpse of the player ("Corpse of &lt;name&gt;", F745, <c>Creature_Death.cs</c> 428-477) was created: <c>CorpsesSeen</c> counts a corpse only between a death signal and that death's arrival, so the den's older corpses
/// (re-created for the client at every arrival, <c>corpses_stale=</c>) cannot satisfy it (review R1).
/// (e) <c>gear_restored=yes</c>: after the death the gear scenario found the kit short and the wand and the five pieces are wielded again (the corpse is not looted, D8).
/// (f) <c>purged &gt;= 1</c> on a run that took the suicide (0x02C6 removed the buffs, <c>Player_Death.cs</c> 211-216); an unbuffed death's purge removes nothing and is not counted (<c>TODO(m3-purge-empty)</c>).
/// (g) The vitals after the arrival are about 75% (<c>Player_Death.cs</c> 262-282, between 60 and 90 here).
/// (h) The tuskers' blows landed: <c>hits_taken &gt;= 1</c>, <c>damage_taken &gt; 0</c> (D36).
/// </para>
/// <para>It prints <c>death_live</c> and the scenario's verdict fields (<c>ok= died= suicides= vitae= respawn= corpse= purged= protected= vitals_pct= gear_restored= worn= hits_taken= damage_taken= evades= health_min_pct= death_s= deaths= corpses_stale= protection= bars= unbuff= bound=</c>;
/// <c>protection=</c> is the report-only end of the lifestone protection from ACE's two Magic lines, <c>dispelled</c> by the first teleport after the respawn, review R2)
/// for the lead. Skipped, with the probe's reason, unless the backend runs with the Lifestone server mod ready.</para>
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
public sealed class DeathLiveTests(ITestOutputHelper output)
{
    [LoopbackFact]
    public void The_mage_dies_to_the_tuskers_respawns_at_Holtburgs_lifestone_with_vitae_and_is_regeared()
    {
        var run = DeathLive.Run(output, new DeathOptions(), DeathStages.All);
        var scenario = run.Scenario;
        var tally = scenario.Tally;
        output.WriteLine("death_live " + scenario.VerdictFields());

        Assert.True(run.Result.Ok, run.Result.Failure);
        Assert.Equal(0, run.Result.Errors);
        Assert.Null(scenario.Failure);
        Assert.Equal("done", scenario.Stage);
        Assert.True(scenario.Bound, "the lifestone's Use did not succeed (D34)");

        var outcome = scenario.Outcome();
        Assert.True(outcome.Ok, string.Join("; ", outcome.Problems));

        Assert.Equal(1, tally.VictimNotices); // (a)
        var fields = scenario.VerdictFields();
        var held = Regex.Match(fields, @" vitae=(\d+) ", RegexOptions.CultureInvariant);
        Assert.True(held.Success && int.Parse(held.Groups[1].Value, CultureInfo.InvariantCulture) > 0, "vitae in the verdict: " + fields); // (b), from the frozen measures
        var paid = Regex.Match(fields, @" vitae_paid=(\d+) vitae_after=(\d+)", RegexOptions.CultureInvariant);
        Assert.True(paid.Success && long.Parse(paid.Groups[1].Value, CultureInfo.InvariantCulture) > 0 && paid.Groups[2].Value == "0", "vitae_paid / vitae_after (D37): " + fields);
        Assert.Contains(" respawn=lifestone ", scenario.VerdictFields(), StringComparison.Ordinal); // (c)
        Assert.True(tally.CorpsesSeen >= 1 && tally.LastCorpseGuid != 0, $"corpses {tally.CorpsesSeen} (stale {tally.StaleCorpses}, last guid 0x{tally.LastCorpseGuid:X8}): the death's own corpse expected"); // (d)
        Assert.True(scenario.GearRestored); // (e)
        Assert.StartsWith("gear=wielded worn=5", scenario.Regear!.VerdictFields(), StringComparison.Ordinal);
        if (scenario.Suicides >= 1)
        {
            Assert.True(tally.Purges >= 1, "purged " + tally.Purges); // (f)
        }

        Assert.InRange(tally.VitalsAfterArrivalPercent, 60, 90); // (g)
        Assert.True(tally.HitsTaken >= 1 && tally.DamageTaken > 0, $"hits {tally.HitsTaken} damage {tally.DamageTaken}"); // (h)
    }
}
