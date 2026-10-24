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

/// <summary>What a live death-scenario run left behind (shared by <see cref="DeathLiveTests"/>).</summary>
/// <param name="Result">The headless run's result.</param>
/// <param name="Scenario">The scenario (its tally and verdict).</param>
/// <param name="Lines">The scenario's, gear's, WARN and ERROR log lines.</param>
internal sealed record DeathLiveRun(HeadlessRunResult Result, DeathScenario Scenario, IReadOnlyList<string> Lines);

/// <summary>Runs <see cref="DeathScenario"/> headless against the bundled ACE (the lead's live tests; a helper of this file's two classes).</summary>
internal static class DeathLive
{
    /// <summary>The headless limit beyond <c>--death-seconds</c> and <c>--death-fight-seconds</c>: the gear, the bind, a suicide, the respawn, the regear and the log off (the headless host's <c>DeathQuitAfterOverheadSeconds</c>, 900).</summary>
    internal const double OverheadSeconds = 900;

    /// <summary>
    /// M4 3.1 d2: the seconds the stages after the respawn may take besides the fight's own <c>--death-fight-seconds</c>, as the game's leg budgets them (<c>DeathLegRunner</c>: the decoy top-up 30, the rebuff 240, the teleport back to the den 30, the walk to the tuskers 60,
    /// the pre-trip heal 120, the corpse trip 90 and the return 60 = 630). The headless run's limit is <see cref="QuitAfterSecondsFor"/>.
    /// </summary>
    internal const double NewStagesSeconds = 630;

    /// <summary>M4 P4 g3fix: the most the fight stage waits for mana before the den (<see cref="DeathOptions.FightManaWaitSeconds"/>).</summary>
    internal const double FightManaWaitSeconds = 600;

    /// <summary>The headless run's limit for these options: the older overhead, the new stages, the fight's seconds and the wait among the tuskers (a ceiling: the real run takes about a third of it).</summary>
    /// <param name="options">The death loop's options.</param>
    internal static double QuitAfterSecondsFor(DeathOptions options) => OverheadSeconds + NewStagesSeconds + options.FightSeconds + options.Seconds + options.FightManaWaitSeconds;

    internal static DeathLiveRun Run(ITestOutputHelper output, DeathOptions options, DeathStages stages)
    {
        using var dats = DatSet.Open();
        var spells = SpellTableData.Load(dats).Spells;
        var files = BackendProbe.MainCheckoutFiles;
        var log = new RecordingLogSink();
        LiveCharacter.Release(output, BackendProbe.TestAccountKey);
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
                    QuitAfterSeconds = QuitAfterSecondsFor(options),
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
        var lines = log.Lines.Where(l => l.Contains("[death]", StringComparison.Ordinal) || l.Contains("[gear]", StringComparison.Ordinal) || l.Contains("[fight]", StringComparison.Ordinal) || l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)).ToList();
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        return new DeathLiveRun(result, scenario, lines);
    }
}

/// <summary>
/// M3 3.2 lane da, DoD 10, and M4 3.1 d2 (D20, D22): the death-and-regear scenario against the bundled ACE (<c>--scenario=death-and-regear</c>). WRITTEN AND COMPILED BY THE LANE, RUN BY THE LEAD (it needs the backend, the mod and BSD Mage built;
/// it changes the character: vitae, dropped items, a corpse in the den, the buffs and the kills of the fight). On <c>BSD Mage</c> only (the test account) the headless runner gears the mage with the decoys, puts its spell bars right, binds it at Holtburg's lifestone (D34),
/// sends it to the Black Spawn Den, suicides there when any enchantment but the vitae is active (D33; the decoys it drops are spawned again), waits in non-combat until the tuskers kill it, respawns, re-gears (the plain kit: the decoys are on the corpse), re-buffs at
/// Holtburg, goes back to the den and kills tuskers (the real Black Spawn Den's, D23: nothing is spawned), heals when the fight left the health under the trip's minimum, goes for its corpse and takes everything back, returns and logs off. Neither this test nor the
/// scenario resets the mage.
/// </summary>
/// <remarks>
/// <para><b>How long it takes.</b> The headless run's limit is <see cref="DeathLive.QuitAfterSecondsFor"/>: the older overhead 900 s + the new stages 630 s (the decoy top-up 30, the rebuff 240, the teleport back to the den 30, the walk to the tuskers 60, the pre-trip
/// heal 120, the corpse trip 90, the return 60) + <c>--death-fight-seconds</c> 180 + <c>--death-seconds</c> 600 = 2,310 s. That is a ceiling, the sum of the limits, as the game's leg budgets 2,015 s for the same stages; the loop really takes about <b>10 to 15 minutes</b>
/// (the suicide's countdown 15 s, the tuskers' kill 10 to 60 s, the full buff cycle about a minute, the fight 1 to 3 minutes, the trip about a minute). A run is one login and ends logged off; do not run it beside another live test on BSD Mage.</para>
/// <para><b>What it proves</b> (each assertion names the ACE behaviour it relies on).
/// (a) <c>died == 1</c>: exactly one GameEvent 0x01AC VictimNotification, the tuskers' death (<c>Player_Death.cs</c> 34-93); a suicide sends none (299-345). A death during the rebuff or the fight would fail the scenario, so the fight's blows never make a second one.
/// (b) <c>vitae &gt; 0</c> in the verdict, whose measures were frozen at the respawn's vitals (spell 666 on the player after a death, <c>EnchantmentManager.cs</c> 391-443; the level is not asserted, D35). The vitae stage (D37) then pays it
/// back with the <c>xp</c> verb, so the tally reads 0 at the end: <c>vitae_paid &gt; 0</c> and <c>vitae_after=0</c> are asserted instead of the tally.
/// (c) <c>respawn=lifestone</c>: the death's teleport (F751 and the position) ended within 15 m of a <c>BF_LIFESTONE</c> object (<c>Player_Death.cs</c> 247-292: the sanctuary the bind set, <c>Lifestone.cs</c> 55-75).
/// (d) The death's own corpse of the player ("Corpse of &lt;name&gt;", F745, <c>Creature_Death.cs</c> 428-496) was created: <c>CorpsesSeen</c> counts a corpse only between a death signal and that death's arrival, so the den's older corpses
/// (re-created for the client at every arrival, <c>corpses_stale=</c>) cannot satisfy it (review R1).
/// (e) <c>gear_restored=yes</c>: after the death the gear scenario found the kit short and the wand and the five pieces are wielded again (the re-gear now comes BEFORE the rebuff and the fight, with the plain kit).
/// (f) <c>purged &gt;= 1</c> on a run that took the suicide (0x02C6 removed the buffs, <c>Player_Death.cs</c> 211-216); an unbuffed death's purge removes nothing and is not counted (<c>TODO(m3-purge-empty)</c>).
/// (g) The vitals after the arrival are about 75% (<c>Player_Death.cs</c> 262-282, between 60 and 90 here), READ FROM THE VERDICT FIELDS, which d1 froze at the end of the respawn's vitals wait: the rebuff, the fight and the trip (stages after the freeze) cannot move them.
/// (h) The tuskers' blows landed: <c>hits_taken &gt;= 1</c>, <c>damage_taken &gt; 0</c> (D36), frozen the same way.</para>
/// <para><b>What the decoy loop proves</b> (M4 3.1 d2; each is a field of the verdict line, asserted on the scenario's own fields; a field that reads <c>n/a</c> fails the test NAMING it, so a skipped or aborted trip is reported, never hidden).
/// (i) <c>decoy_safe=yes</c>: the gear stage's check (<c>DecoyKit.Check</c> over the live inventory, ACE's rule in <c>DeathItem.cs</c> and <c>Player_Death.cs</c> 481-603) found the wand, the worn non-bonded armour and the components the loops cast with unable to drop at level 126: at least
/// as many items rank above the best needed one, in every corner of the +-10% variance, as a death can drop (level/20 + 2 = 8). It is the post-top-up check when the unbuff suicide ran.
/// (j) <c>gear_dropped=0</c>: no listed entry of the corpse's own contents is an item the loops need (<c>DeathDropModel.IsNeeded</c>): the death took decoys, not the gear. The scenario FAILS the run (not only this assertion) when such an item was listed and not taken back.
/// (k) The corpse held at least 6 items (level/20 at level 126: ACE drops level/20 + rand(0..2), 6 to 8; the scenario's <c>ListingEntries</c> less its <c>ListingCoins</c>: ACE puts half the pyreals on the corpse, a coin stack that is neither decoy nor gear, left out of <c>listed</c> and printed as <c>coins=</c>, review R20) and all but at most one are decoys: <c>decoys_dropped &gt;= listed - 1</c>. The one that may not be a decoy is the unneeded Mana Scarab
/// (15,000 a unit, a SpellComponents item no loop's level-VII casts use, ranked among the top drops). So <c>decoys_dropped</c> is 5 when 6 dropped and the scarab was among them: asserting a flat 6 would fail one run in three by design (see the handoff); the line prints both.
/// (l) <c>decoys_recovered == decoys_dropped</c>: every decoy the corpse listed is in the pack after the trip (the trip took it, one pick-up at a time, <c>CorpseLooter</c>).
/// (m) <c>rebuffed=yes</c>: the rebuff stage's buff cycle ended with every profile line up (the death purged them all, <c>Player_Death.cs</c> 211-216).
/// (n) <c>den_kills &gt;= 1</c>: the fight stage killed at least one real Tusker Slave in the den before the trip (the tuskers are dead when the corpse is reached; <c>--death-fight-kills</c> asks for 4).
/// Not asserted, printed: <c>pretrip_heals</c> (the fight can leave the health under the trip's minimum; Heal Self VII and regeneration run then), <c>corpse_looted</c>, <c>items_recovered</c>, <c>trip_s</c>, <c>trip_abort</c> and the decoy top-up grants.</para>
/// <para>It prints <c>death_live</c> and the scenario's verdict fields, <c>death_live_corpse</c> (the corpse the tally remembered: guid, cell, age) and <c>death_live_decoys</c> (every new field: <c>rebuffed= den_kills= decoys= decoy_safe= decoys_dropped= gear_dropped= decoys_recovered= pretrip_heals=</c>
/// with the listing's size, the trip's fields and the top-up grants), for the lead. <c>protection=</c> is the report-only end of the lifestone protection from ACE's two Magic lines, <c>dispelled</c> by the first teleport after the respawn, review R2.
/// Skipped, with the probe's reason, unless the backend runs with the Lifestone server mod ready.</para>
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test")]
public sealed class DeathLiveTests(ITestOutputHelper output)
{
    private static string FieldOf(string fields, string name)
    {
        var match = Regex.Match(fields, @"(?:^| )" + Regex.Escape(name) + @"=(\S+)", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : "(absent)";
    }

    /// <summary>A numeric field of the verdict, or a failure NAMING the field and the trip's state (a field that reads n/a means the loop did not get that far).</summary>
    private static int CountOf(string fields, string name)
    {
        var text = FieldOf(fields, name);
        Assert.True(int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value), $"{name}={text} (a number expected; n/a means it was not measured: corpse_looted={FieldOf(fields, "corpse_looted")} trip_abort={FieldOf(fields, "trip_abort")} items_recovered={FieldOf(fields, "items_recovered")}) in: {fields}");
        return value;
    }

    [LoopbackFact]
    public void The_mage_dies_to_the_tuskers_respawns_at_Holtburgs_lifestone_with_vitae_and_is_regeared_and_the_decoys_take_the_death_and_come_back()
    {
        // M4 P4 g3fix: g3 started the fight at 23 mana (the rebuff's 27 casts) and the 0.43 a second regeneration left it waiting for mana (kills=3 of 4, phase=Mana): the fight stage first waits, at Holtburg, up to 600 s for 70% of the maximum mana (empty to 70% of about 300 is 490 s).
        var run = DeathLive.Run(output, new DeathOptions { FightManaWaitSeconds = DeathLive.FightManaWaitSeconds }, DeathStages.All);
        var scenario = run.Scenario;
        var tally = scenario.Tally;
        output.WriteLine("death_live " + scenario.VerdictFields());
        var trip = Regex.Match(scenario.VerdictFields(), @" corpse_looted=(yes|partial|no|skipped) items_recovered=(\d+) trip_s=([\d.]+|n/a) trip_abort=(\S+)", RegexOptions.CultureInvariant);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"death_live_corpse {(trip.Success ? trip.Value.Trim() : "(the verdict line carries no corpse fields)")} remembered_guid=0x{tally.LastCorpseGuid:X8} remembered_cell=0x{tally.LastCorpsePosition?.CellId ?? 0:X8} remembered_at_s={(double.IsNaN(tally.LastCorpseAt) ? "n/a" : (scenario.Tally.LastCorpseAt).ToString("0.0", CultureInfo.InvariantCulture))} trip_elapsed_s={scenario.TripElapsedSeconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? "n/a"} (report-only)"));

        var fields = scenario.VerdictFields();
        // Review R20: the listing is classified as the scenario does (ClassifyListing): ACE puts half the pyreals on the corpse (Player_Death.cs), a coin stack (wcid 273) that is neither a decoy nor gear, so it is left out of `listed` and printed
        // apart. A mage that carries coins cannot fail (k) for them.
        var entries = scenario.ListingEntries ?? -1;
        var coins = scenario.ListingCoins ?? 0;
        var listed = entries < 0 ? -1 : entries - coins;
        output.WriteLine("death_live_decoys " + string.Join(' ', new[] { "rebuffed", "den_kills", "decoys", "decoy_safe", "decoys_dropped", "gear_dropped", "decoys_recovered", "pretrip_heals" }.Select(name => name + "=" + FieldOf(fields, name)))
            + string.Create(CultureInfo.InvariantCulture, $" listed={(listed < 0 ? "n/a" : listed.ToString(CultureInfo.InvariantCulture))} coins={coins} entries={(entries < 0 ? "n/a" : entries.ToString(CultureInfo.InvariantCulture))} corpse_looted={FieldOf(fields, "corpse_looted")} items_recovered={FieldOf(fields, "items_recovered")} trip_s={FieldOf(fields, "trip_s")} trip_abort={FieldOf(fields, "trip_abort")} decoy_top_up_grants={scenario.DecoyTopUpGrants} suicides={scenario.Suicides} (the lead reads these at the gate)"));

        Assert.True(run.Result.Ok, run.Result.Failure);
        Assert.Equal(0, run.Result.Errors);
        Assert.Null(scenario.Failure);
        Assert.Equal("done", scenario.Stage);
        Assert.True(scenario.Bound, "the lifestone's Use did not succeed (D34)");

        var outcome = scenario.Outcome();
        Assert.True(outcome.Ok, string.Join("; ", outcome.Problems));

        Assert.Equal(1, tally.VictimNotices); // (a)
        Assert.Equal("1", FieldOf(fields, "died")); // (a), frozen at the respawn: the fight and the trip cannot add a death
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

        var vitals = double.Parse(FieldOf(fields, "vitals_pct"), NumberStyles.Float, CultureInfo.InvariantCulture);
        Assert.InRange(vitals, 60, 90); // (g), the frozen figure
        Assert.True(CountOf(fields, "hits_taken") >= 1 && CountOf(fields, "damage_taken") > 0, $"hits {FieldOf(fields, "hits_taken")} damage {FieldOf(fields, "damage_taken")}"); // (h)
        Assert.True(tally.HealthMinPercent < 100.0, "health_min_pct " + tally.HealthMinPercent + " (the 0x02E9 health fall; the check IncomingDamageLiveTests made, dropped in M3 4.2 p5b C9: this test is DoD 9's loopback evidence)"); // (h2)

        // The decoy loop (M4 3.1 d2). A field that is n/a fails naming it and the trip's state: a skipped or aborted trip is reported, not hidden.
        Assert.Equal("yes", FieldOf(fields, "decoy_safe")); // (i)
        Assert.Equal(0, CountOf(fields, "gear_dropped")); // (j)
        var decoysDropped = CountOf(fields, "decoys_dropped");
        Assert.True(listed >= 6, $"the corpse's listing held {listed} entries besides {coins} pyreal stack(s) (level 126 drops at least level/20 = 6; -1 = the trip never read a listing: corpse_looted={FieldOf(fields, "corpse_looted")} trip_abort={FieldOf(fields, "trip_abort")})"); // (k)
        Assert.True(decoysDropped >= listed - 1, $"decoys_dropped={decoysDropped} of {listed} listed besides {coins} pyreal stack(s) (all but at most the unneeded Mana Scarab are decoys; the decoys are what a death takes: {fields})"); // (k)
        Assert.Equal(decoysDropped, CountOf(fields, "decoys_recovered")); // (l)
        Assert.Equal("yes", FieldOf(fields, "rebuffed")); // (m)
        Assert.True(CountOf(fields, "den_kills") >= 1, "den_kills " + FieldOf(fields, "den_kills") + " (the fight stage killed no tusker before the trip)"); // (n)
    }

    /// <summary>
    /// M3 4.2 p1: what it proves. The gear and bars stage twice, each a fresh login. The first run leaves tab I and tab II exactly the owner's lists (the removals and adds ACE took); the second run reads
    /// the bars from ACE's own PlayerDescription, so <c>bars=ok</c> there means ACE kept the first run's layout and the plan has nothing left to send. Both runs end with the tabs equal to the lists.
    /// </summary>
    [LoopbackFact]
    public void The_mage_spell_bars_tabs_I_and_II_are_the_owners_lists_and_a_second_run_sends_nothing()
    {
        var first = DeathLive.Run(output, new DeathOptions(), DeathStages.Gear);
        Assert.True(first.Result.Ok, first.Result.Failure);
        Assert.NotNull(first.Scenario.Bars);
        Assert.Equal(MageSpellBars.BuffTab.ToArray(), first.Scenario.Bars!.BuffTabAtEnd.ToArray());
        Assert.Equal(MageSpellBars.FightTab.ToArray(), first.Scenario.Bars.FightTabAtEnd.ToArray());

        var second = DeathLive.Run(output, new DeathOptions(), DeathStages.Gear);
        Assert.True(second.Result.Ok, second.Result.Failure);
        Assert.Equal("ok", second.Scenario.BarsNote);
        Assert.Equal(MageSpellBars.BuffTab.ToArray(), second.Scenario.Bars!.BuffTabAtEnd.ToArray());
        Assert.Equal(MageSpellBars.FightTab.ToArray(), second.Scenario.Bars.FightTabAtEnd.ToArray());
    }
}
