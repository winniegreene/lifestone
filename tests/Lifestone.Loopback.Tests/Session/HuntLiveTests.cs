using Lifestone.Client.Session.Fighter;
using System.Text.RegularExpressions;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M4 1.2 s4b, the hunt loop's first form (decision D10: one kill on the Low B belt and the return to Holtburg) against the bundled ACE. WRITTEN AND COMPILED BY THE SLICE, RUN BY THE LEAD (it needs the backend with
/// the Lifestone server mod ready and Holtburg Hunter built by <c>--scenario=fighter-build</c>). On the test account the headless runner enters Holtburg Hunter and runs the whole <see cref="HuntScenario"/> with its
/// default options (the Mace, one kill), recording the session to <c>cache/m4/1-2/live/</c>.
/// </summary>
/// <remarks>
/// <para><b>What it proves.</b> The loop end to end in one session: the character is the fighter (<c>fighter=ok</c>), the bind at Holtburg's lifestone was Confirmed (<c>bound</c>), the belt was reached (at least one
/// cell stood in), one kill was made, the player is back in Holtburg's landblock and the log off came (the headless result is Ok with no ERROR line). It prints the full verdict line
/// <c>hunt fighter= kills= swings= shots= hits= misses= damage_dealt= damage_taken= hits_taken= evades= xp_gained= returned=</c> in the shape the game's <c>--login-check=hunt</c> leg prints it, and checks that shape. The
/// attack itself is judged in <see cref="MeleeLiveTests"/> and <see cref="MissileLiveTests"/>; the potion and the regeneration wait run only when health is low. The kill levels Holtburg Hunter (owner decision D1). M4 2.2 s4: the kill's corpse was opened (<c>corpses_opened&gt;=1</c>, judged in 2.1), and the loot's <c>looted= loot_skipped= loot_frame_max_ms=</c> are printed with the verdict and asserted on nothing (report-only until 3.1).</para>
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-melee")]
[LoopbackCharacter("Holtburg Brawler")]
[LiveSharedSession]
public sealed class HuntLiveTests(ITestOutputHelper output)
{
    internal static readonly Regex Verdict = new(@"^fighter=ok kills=\d+ swings=\d+ shots=\d+ hits=\d+ misses=\d+ blocked=\d+ seeks=\d+ damage_dealt=-?\d+ damage_taken=-?\d+ hits_taken=\d+ evades=\d+ xp_gained=-?\d+ returned=(yes|no)$", RegexOptions.CultureInvariant);

    [LoopbackFact]
    public void The_fighter_binds_hunts_one_creature_on_the_belt_returns_to_Holtburg_and_logs_off()
    {
        var run = HuntLiveRun.Run(output, "hunt", new HuntOptions(), character: FighterTemplate.Melee.Name);
        var scenario = run.Scenario;

        Assert.True(run.Result.Ok, run.Result.Failure);
        Assert.Equal(0, run.Result.Errors);
        Assert.Null(scenario.Failure);
        Assert.Equal("done", scenario.Stage);
        Assert.Equal("ok", scenario.Fighter);
        Assert.True(scenario.Bound, "the bind at the lifestone was not confirmed (TODO(m4-hunt-bind))");
        Assert.True(scenario.CellsVisited >= 1, "no belt cell was stood in");
        Assert.True(scenario.Tally.Kills >= 1, "kills " + scenario.Tally.Kills);
        Assert.True(scenario.Returned, "returned=no (the run did not end in Holtburg's landblock)");
        Assert.Matches(Verdict, scenario.VerdictFields());
        Assert.True(scenario.CorpsesOpened >= 1, "corpses_opened " + scenario.CorpsesOpened + " (the kill's corpse was not opened; 2.1's gate judges it)");
        output.WriteLine("hunt_live " + scenario.VerdictFields() + " " + scenario.DetailFields());

        // M4 2.2 s4: the loot of the kill's corpse is printed beside the verdict and asserted on nothing (report-only in 2.2, judged from 3.1). loot_frame_max_ms is the game leg's field (the slowest frame from the corpse's
        // open to its close); this headless run draws no frames, so it prints n/a here and the lead reads the number from smoke's login-hunt line.
        output.WriteLine($"hunt_live_loot looted={scenario.Looted} loot_skipped={scenario.LootSkipped} loot_frame_max_ms=n/a corpses_opened={scenario.CorpsesOpened} corpse_items={scenario.CorpseItems}");
    }

    /// <summary>The headless limit of the loop test, in seconds: the bind and the belt's regeneration, 300 s of hunting that may spend most of it waiting to recover, three corpse sequences, the wind-down and the log off.</summary>
    private const double LoopQuitAfterSeconds = 2400;

    /// <summary>
    /// M4 3.1 s4, the hunt as a loop (the live half of DoD 9 and 10): Holtburg Hunter on the Low B belt, <c>--hunt-kills 3 --hunt-seconds 300</c>, the loot and the recovery rule at their defaults (<c>--heal</c> on, below 75% up to
    /// 90%, the kit while its chance is at least 0.7, else a potion). WRITTEN AND COMPILED BY THE SLICE, RUN BY THE LEAD: it needs the backend with the Lifestone server mod ready and Holtburg Hunter built, with a kit and potions
    /// in the pack (the lead tops them up). About 8 to 20 minutes.
    /// <para><b>What it proves.</b> It prints <c>hunt_loop kills= looted= loot_skipped= heals= kit_uses= potions= returned=</c> and asserts <c>returned=yes</c>, the verdict composer's <c>ok</c> (the judges of the game's leg as
    /// <see cref="HuntLegVerdict"/> holds them since s3: kills, damage dealt, swings, <c>looted &gt;= 1</c> over two or more kills) and <c>kills &gt;= 2</c>. A leg that killed fewer because the hunter retreated (health under 30%) or
    /// ran out of time is REPORTED, not hidden: the failure text names the kills, the scenario's failure, its stage and the health it ended at. The headless run draws no frames, so the frame judges of the leg
    /// (<c>attack_frame_max_ms</c>, <c>loot_frame_max_ms</c>) read n/a here and the lead reads them from smoke's <c>login-hunt</c> line.</para>
    /// </summary>
    [LoopbackFact]
    public void The_fighter_hunts_three_kills_on_the_belt_loots_heals_between_them_and_returns_to_Holtburg()
    {
        var run = HuntLiveRun.RunAs(FighterTemplate.Melee.Name, output, "hunt-loop", LoopQuitAfterSeconds, "--hunt-ground=belt", "--hunt-kills=3", "--hunt-seconds=300");
        var scenario = run.Scenario;
        var tally = scenario.Tally;
        var line = $"hunt_loop kills={tally.Kills} looted={scenario.Looted} loot_skipped={scenario.LootSkipped} heals={scenario.Heals} kit_uses={scenario.KitUses} potions={scenario.PotionsUsed} returned={(scenario.Returned ? "yes" : "no")}";
        output.WriteLine(line);
        output.WriteLine("hunt_loop detail " + scenario.DetailFields());

        // The verdict of the game's leg, composed from the scenario's own numbers (the headless run has no frames to sample).
        // M4 P5a fix r4: the measures come from the scenario in one place (round 4 left one_blow_kills at 0 here while the scenario counted 3).
        var verdict = HuntLegVerdict.Compose(scenario.Failure, HuntLegVerdict.FromScenario(scenario, HuntWeapon.Melee, lootEnabled: true, HuntGroundChoice.Belt));
        output.WriteLine("hunt_loop verdict " + verdict.Line);
        foreach (var note in verdict.Notes ?? [])
        {
            output.WriteLine("hunt_loop note " + note);
        }

        var context = $" | {line} | failure={scenario.Failure ?? "none"} stage={scenario.Stage} | {scenario.DetailFields()}";
        Assert.True(run.Result.Ok, run.Result.Failure + context);
        Assert.Equal(0, run.Result.Errors);
        Assert.True(scenario.Returned, "returned=no (the run did not end in Holtburg's landblock)" + context);
        Assert.True(verdict.Ok, "the leg's judges did not hold: " + string.Join("; ", verdict.Problems) + context);
        Assert.True(tally.Kills >= 2, $"kills {tally.Kills} of 3: fewer than two kills (REPORTED, NOT HIDDEN: the hunter retreated under 30% health or ran out of its 300 s; the lead reads the line)" + context);
    }
}
