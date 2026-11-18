using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Journey;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 14 approach cost (the owner, 2026-11-14, watching the climber cross the Tusker Island to the Holding: she was fighting things with high magic resistance, so she should recall and go back to the previous ground; live
/// <c>cache/m6/14/live/holdtrial2</c>, 12:54 to 13:03 UTC). Her numbers then, from the leg's lines: level 62, War Magic 352 buffed, Mana Conversion 254, 387 mana, Flame Bolt VI (power 250, base mana 30), the way out's reserve 120
/// (Lifestone Recall 50 + Heal Self VI 70), her run at 6.25 m/s (the Holtburg run leg's speed_mps), her mana regenerating about 0.6 a second between fight-backs (335 before a bolt at 12:55:02, 354 at 12:55:47). The route
/// by-facility-hub-barracks runs 1,506.5 m from the Barracks drop to the entry.
/// </summary>
public sealed class ApproachCostTests(ITestOutputHelper output)
{
    private static HuntingGround Holding => GroundCatalog.Get("tusker-holding");

    private static HuntingGround Barracks => GroundCatalog.Get("tusker-barracks");

    private const double HoldingRun = 1506.5; // travel-routes.json, by-facility-hub-barracks

    private const double BarracksRun = 19.6; // travel-routes.json, the Barracks' by-facility-hub

    /// <summary>Her live input at level 62 (the leg's lines; the rates are the ladder's quarters and holding rungs over their targets' xp).</summary>
    private static CrossingInput Her(uint war = 352)
    {
        var ladder = LadderLoader.WarMage;
        Assert.True(WarSpellData.TryGet(85, out var row)); // Flame Bolt VI
        var quarters = ladder.Rungs.Single(r => r.Key == "quarters");
        var holding = ladder.Rungs.Single(r => r.Key == "holding");
        var previous = ApproachCost.RungRate(quarters, GroundCatalog.Get("tusker-quarters"), out var previousWhy);
        var rung = ApproachCost.RungRate(holding, Holding, out var rungWhy);
        return new CrossingInput(war, 254, 387, 0.6, 120, 6.25, new CrossingBolt("Flame Bolt VI", "fire", row, 250, 30, ladder.VitalsPolicy.BoltSeconds(6)), BuffOptions.OneBuffCycleSeconds, previous, rung, previousWhy + "; " + rungWhy);
    }

    /// <summary>A survivable approach verdict of <paramref name="ground"/> (her live state's own read is the retreat attackers' tests'): the crossing's verdicts are judged on it.</summary>
    private static ApproachVerdict Survivable(HuntingGround ground) => new(true, ground.Key, ground.Key + "'s approach is survivable", null, []);

    /// <summary>
    /// The live case: the Holding's approach at level 62 is held before she leaves. The crossing's facts read as recorded (98 generators over 11 landblocks; the Reaper's Magic Defense 362 and fire resistance 0.38); the kill model
    /// gives the Reaper the live fight-back's bolts (21 there, 11 of them resisted), the Assailers theirs (6 and 8); the crossing would take her mana under the reserve before the portal, so the rung at 65 is held
    /// <c>approach-costly:holding</c> and she hunts the Quarters, the trial at 62 is refused by name, and a corpse there is not walked to. What would change it is named.
    /// </summary>
    [Fact]
    public void The_live_crossing_at_level_62_is_held_before_she_leaves_and_the_rung_the_trial_and_the_corpse_trip_read_it()
    {
        var approach = Holding.Approach!;
        Assert.Equal((98, 1.6112), (approach.Encounters, approach.PerRoll));
        var reaper = approach.Hostiles.Single(h => h.Wcid == 22747);
        Assert.Equal((362, 0.38, 24.0, 125_000L), (reaper.MagicDefence, reaper.Resist!["fire"], reaper.AwarenessMetres, reaper.Xp));
        Assert.All(approach.Hostiles, h => Assert.True(h.HasCrossingFacts, h.Name));
        Assert.Equal(80_000, Holding.Targets.First().Xp);

        var cost = ApproachCost.Judge(Holding.Key, approach, HoldingRun, Her())!;
        output.WriteLine(cost.Why);
        var kinds = cost.Kinds.ToDictionary(k => k.Wcid);
        Assert.InRange(kinds[22747].Casts, 19, 25); // the Reaper: live 21 bolts, 11 resisted
        Assert.InRange(kinds[22747].Land, 0.40, 0.45); // her 352 against its 362
        Assert.InRange(kinds[22053].Casts, 6, 8.5); // the Assailer: live 6 and 8
        Assert.InRange(kinds[10810].Casts, 3, 4.5); // the Rampager: live 3 each
        Assert.True(cost.Costly);
        Assert.Equal("mana", cost.Reason);
        Assert.True(cost.AtPortal < 120, cost.Why);
        Assert.StartsWith("the crossing would force a recall before the portal: 1507 m of tusker-holding's approach (98 generators over 11 landblocks)", cost.Why, StringComparison.Ordinal);

        var change = ApproachCost.WhatChanges(Holding.Key, approach, HoldingRun, Her());
        output.WriteLine(change);
        Assert.StartsWith("what would change it: her War Magic none up to 752 (now 352; her bolt Flame Bolt VI at the top)", change, StringComparison.Ordinal); // her pack pays no Ilservian's Flame: VI at every War Magic
        Assert.Contains("; a held caster x", change, StringComparison.Ordinal);
        Assert.Contains("; another route a run of ", change, StringComparison.Ordinal);

        // the ladder's way out at 65, the trial switch at 62 and the corpse trip read one verdict
        var verdict = Survivable(Holding) with { Cost = cost with { Change = change } };
        Assert.Equal((true, ApproachCost.CostlyPrefix), (verdict.Holds, verdict.Kind));
        var ladder = LadderLoader.WarMage;
        var own = RungOwner.Decide(ladder, Ask(65, "tusker-quarters") with { Approach = key => key == Holding.Key ? verdict : null });
        Assert.Equal(("approach-costly:holding", "quarters"), (own.Reason, own.Decision.Rung?.Key));
        Assert.Contains("approach costly (mana): the crossing would force a recall before the portal", own.Why, StringComparison.Ordinal);
        var tried = RungOwner.Decide(ladder, Ask(62, Holding.Key) with { Trial = new LadderTrial(Holding.Key), Approach = key => key == Holding.Key ? verdict : null });
        Assert.False(tried.Trial!.Steers);
        Assert.StartsWith("approach costly (mana): ", tried.Trial.Refused, StringComparison.Ordinal);
        var corpse = CorpseApproachRule.Decide(verdict, 62, 30);
        Assert.Equal((false, ApproachCost.CostlyPrefix), (corpse.Walk, corpse.Reason));
    }

    /// <summary>
    /// A cheap approach: the Barracks' Guards, the 19.6 m run from the hub's drop. At the same state the crossing is paid (well under one creature expected, a Tusker Guard 3.5 bolts), so the Barracks is not held, and a trial or a way out
    /// to it goes on.
    /// </summary>
    [Fact]
    public void A_cheap_approach_the_Barracks_Guards_is_not_held()
    {
        var cost = ApproachCost.Judge(Barracks.Key, Barracks.Approach!, BarracksRun, Her() with { RatePrevious = null, RateRung = null, RatesWhy = "the Barracks is no rung's ground" })!;
        output.WriteLine(cost.Why);
        Assert.False(cost.Costly);
        Assert.Equal(string.Empty, cost.Reason);
        Assert.True(cost.Creatures < 1, cost.Why);
        Assert.InRange(cost.Kinds.Single(k => k.Wcid == 1629).Casts, 3, 4.5);
        Assert.StartsWith("the crossing is paid: ", cost.Why, StringComparison.Ordinal);
        Assert.Contains("time not judged (the Barracks is no rung's ground)", cost.Why, StringComparison.Ordinal);
        var verdict = Survivable(Barracks) with { Cost = cost };
        Assert.Equal((false, "approach-ok"), (verdict.Holds, verdict.Kind));
        Assert.True(CorpseApproachRule.Decide(verdict, 62, 30).Walk);

        // the Holding at the same state over the Barracks' short run would pass too: the run is what costs
        Assert.False(ApproachCost.Judge(Holding.Key, Holding.Approach!, BarracksRun, Her())!.Costly);
    }

    /// <summary>
    /// The measured crossing (rule 2): a fight-back at 5 casts a kill with the mana falling. Before the seed kills nothing is judged; at three kills the run's rate (3 kills in 300 m, 75 mana a kill) projects the 1,200 m left to 12 kills and
    /// 900 mana against 81 over the reserve: she recalls and the crossing is given up for the run, which the next way out reads as <c>approach-abandoned:holding</c>. A run whose mana holds (regeneration covers the bolts) goes on.
    /// </summary>
    /// <summary>
    /// M6 14 crossing prior (live 2026-11-14 18:50, ladder13): the way out priced the Armory's 345 m at 6.4 creatures; on the run three kills came in the first 167 m and two more at the same spot, so the measured rate alone (2.99 a 100 m)
    /// projected 5.3 kills and 131 mana for the 176 m left against 109 over the reserve and gave the crossing up. Blended with the approach's facts the same run projects 3.9 kills and 97 mana: it goes on. A run that keeps meeting
    /// three times the facts' rate is still given up.
    /// </summary>
    [Fact]
    public void A_crossing_measure_blends_the_approachs_facts_so_two_kills_at_one_spot_do_not_give_up_a_priced_crossing()
    {
        const double prior = 6.4 / 345;
        var alone = ApproachCost.Measured(new CrossingMeasure(5, 14, 353, 229, 120, 343, 176, 3));
        output.WriteLine(alone.Why);
        Assert.True(alone.Abandon);
        Assert.Equal(131, Math.Round(alone.Needed));

        var blended = ApproachCost.Measured(new CrossingMeasure(5, 14, 353, 229, 120, 343, 176, 3, prior));
        output.WriteLine(blended.Why);
        Assert.False(blended.Abandon);
        Assert.InRange(blended.RemainingKills, 3.8, 4.0);
        Assert.InRange(blended.Needed, 94, 99);
        Assert.Contains("kills a 100 m (the approach's facts' 1.86 over its 343 m with the 2.99 measured over 167 m)", blended.Why, StringComparison.Ordinal);
        Assert.StartsWith("the crossing pays so far", blended.Why, StringComparison.Ordinal);

        // three times the facts' rate, kept up: 9 kills in the first 167 m at the same mana a kill: given up
        var dense = ApproachCost.Measured(new CrossingMeasure(9, 25, 353, 130, 120, 343, 176, 3, prior));
        output.WriteLine(dense.Why);
        Assert.True(dense.Abandon);

        // the approach's facts: the Armory's, read from its profile
        var armory = GroundCatalog.Get("tusker-armory");
        var perMetre = ApproachCost.CreaturesPerMetre(armory.Approach!);
        Assert.NotNull(perMetre);
        Assert.InRange(perMetre!.Value * 345, 3, 12);
    }

    /// <summary>
    /// M6 14 crossing stamina (live 2026-11-14 19:12, ladder14b): the Armory's crossing was given up 15 mana short (131 needed against 116 over the reserve) while she held about 180 stamina; one Stamina to Mana press at home right after
    /// the recall lifted her from 238 to 377. A press from the stamina held counts as mana she has, and says so.
    /// </summary>
    [Fact]
    public void A_crossing_measure_counts_a_stamina_to_mana_press_from_the_stamina_she_holds()
    {
        const double prior = 6.4 / 345;
        var bare = ApproachCost.Measured(new CrossingMeasure(3, 11, 367, 236, 120, 343, 166, 3, prior));
        output.WriteLine(bare.Why);
        Assert.True(bare.Abandon);

        var pressed = ApproachCost.Measured(new CrossingMeasure(3, 11, 367, 236, 120, 343, 166, 3, prior, StaminaMana: 139));
        output.WriteLine(pressed.Why);
        Assert.False(pressed.Abandon);
        Assert.Equal(255, Math.Round(pressed.Above));
        Assert.Contains("(139 of it a Stamina to Mana press from the stamina she holds)", pressed.Why, StringComparison.Ordinal);

        // a press is counted once: a run that needs more than the mana and one press is still given up
        Assert.True(ApproachCost.Measured(new CrossingMeasure(3, 11, 367, 150, 120, 343, 300, 3, prior, StaminaMana: 60)).Abandon);
    }

    /// <summary>M6 14 (live 2026-11-14 19:13, ladder14b, <c>failed:move-on-loop</c>): a crossing a run leg gave up stays given up in the session's holds, which the host's restarts share; a price never releases it.</summary>
    [Fact]
    public void A_given_up_crossing_stays_given_up_across_the_hosts_restarts()
    {
        var holds = new ApproachHolds();
        Assert.Null(holds.AbandonedOf("tusker-armory"));
        holds.NoteAbandoned("tusker-armory", "she will not reach the portal above the reserve: 3 kills");
        holds.NoteAbandoned("tusker-armory", "a later measure");
        Assert.Equal("she will not reach the portal above the reserve: 3 kills", holds.AbandonedOf("tusker-armory"));
        Assert.False(holds.Release("tusker-armory")); // the price's release is for priced holds; the measured crossing is kept apart
        Assert.NotNull(holds.AbandonedOf("tusker-armory"));
        var copy = new AutopilotOptions { ApproachHolds = holds } with { };
        Assert.Same(holds, copy.ApproachHolds); // a run's options are copies: the restarts read the same holds
    }

    /// <summary>M6 14 (live 2026-11-14 19:43, the Armory's first visit a death): --autopilot-hold-grounds names grounds held for the session; each is a crossing given up by name; a key that is not a ground fails the run.</summary>
    [Fact]
    public void The_hold_grounds_option_gives_each_named_ground_up_for_the_session_and_refuses_an_unknown_key()
    {
        var o = AutopilotOptions.FromArguments(Lifestone.Client.Options.CommandLineArgument.ParseAll(["--autopilot-hold-grounds=tusker-armory,tusker-holding"]));
        Assert.Empty(o.Errors);
        Assert.Contains("held for the session by --autopilot-hold-grounds", o.ApproachHolds.AbandonedOf("tusker-armory"), StringComparison.Ordinal);
        Assert.NotNull(o.ApproachHolds.AbandonedOf("tusker-holding"));
        Assert.Null(o.ApproachHolds.AbandonedOf("tusker-quarters"));
        Assert.NotEmpty(AutopilotOptions.FromArguments(Lifestone.Client.Options.CommandLineArgument.ParseAll(["--autopilot-hold-grounds=no-such-ground"])).Errors);
        Assert.NotEmpty(AutopilotOptions.FromArguments(Lifestone.Client.Options.CommandLineArgument.ParseAll(["--autopilot-hold-grounds="])).Errors);
    }

    [Fact]
    public void A_measured_crossing_at_five_casts_a_kill_with_mana_falling_recalls_and_holds_for_the_run()
    {
        var early = ApproachCost.Measured(new CrossingMeasure(2, 10, 426, 276, 120, 1500, 1350, 3));
        Assert.Equal((false, false), (early.Judged, early.Abandon));
        Assert.StartsWith("not judged yet: 2 of the 3 seed kills", early.Why, StringComparison.Ordinal);

        var measured = ApproachCost.Measured(new CrossingMeasure(3, 15, 426, 201, 120, 1500, 1200, 3));
        output.WriteLine(measured.Why);
        Assert.True(measured.Judged);
        Assert.True(measured.Abandon);
        Assert.Equal((5.0, 75.0, 12.0, 900.0, 81.0), (measured.CastsPerKill, measured.ManaPerKill, measured.RemainingKills, measured.Needed, measured.Above));
        Assert.StartsWith("she will not reach the portal above the reserve: 3 kills in 15 casts (5 a kill), mana 426 -> 201 (75 a kill", measured.Why, StringComparison.Ordinal);

        var holding = ApproachCost.Measured(new CrossingMeasure(3, 15, 426, 420, 120, 1500, 1200, 3));
        Assert.False(holding.Abandon);
        Assert.StartsWith("the crossing pays so far", holding.Why, StringComparison.Ordinal);

        // given up for the run: the way out's verdict holds by name, one rung down; the corpse trip reads the same
        var facts = new JourneyFacts();
        facts.AbandonApproach(Holding.Key, measured.Why);
        facts.AbandonApproach(Holding.Key, "a later reason"); // the first stands
        Assert.Equal(measured.Why, facts.ApproachAbandonedOf(Holding.Key));
        Assert.Null(facts.ApproachAbandonedOf(Barracks.Key));
        var verdict = Survivable(Holding) with { Abandoned = facts.ApproachAbandonedOf(Holding.Key) };
        var own = RungOwner.Decide(LadderLoader.WarMage, Ask(65, "tusker-quarters") with { Approach = key => key == Holding.Key ? verdict : null });
        Assert.Equal(("approach-abandoned:holding", "quarters"), (own.Reason, own.Decision.Rung?.Key));
        Assert.Contains("approach abandoned: she will not reach the portal above the reserve", own.Why, StringComparison.Ordinal);
        Assert.Equal((false, ApproachCost.AbandonedPrefix), (CorpseApproachRule.Decide(verdict, 65, 30).Walk, CorpseApproachRule.Decide(verdict, 65, 30).Reason));
    }

    /// <summary>
    /// What would change the verdict for her now, each checked by judging at it: a rung's planned rate is its kills-an-hour floor times its targets' xp (the Quarters 80 x 30,000, the Holding 86 x 80,000); the held caster's damage the scan
    /// names passes and a step under it does not; the run the scan names passes and 10 m more does not. The time verdict: a crossing whose mana is paid (a regeneration that covers it) but whose seconds (a slow run in a short cycle) exceed what the rung's
    /// rate allows is held for its time. An approach with no crossing facts is not judged.
    /// </summary>
    [Fact]
    public void What_would_change_the_verdict_is_judged_and_a_slow_paid_crossing_is_held_for_its_time()
    {
        var input = Her();
        Assert.Equal((2_400_000.0, 6_880_000.0), (input.RatePrevious!.Value, input.RateRung!.Value));
        var approach = Holding.Approach!;
        var change = ApproachCost.WhatChanges(Holding.Key, approach, HoldingRun, input);
        output.WriteLine(change);
        var caster = Number(change, "a held caster x", " on her bolts");
        Assert.False(ApproachCost.Judge(Holding.Key, approach, HoldingRun, input with { CasterDamage = caster })!.Costly);
        Assert.True(ApproachCost.Judge(Holding.Key, approach, HoldingRun, input with { CasterDamage = caster - 0.05 })!.Costly);
        var metres = Number(change, "another route a run of ", " m or less");
        Assert.InRange(metres, BarracksRun, HoldingRun);
        Assert.False(ApproachCost.Judge(Holding.Key, approach, metres, input)!.Costly);
        Assert.True(ApproachCost.Judge(Holding.Key, approach, metres + 10, input)!.Costly);

        var slow = ApproachCost.Judge(Holding.Key, approach, HoldingRun, input with { ManaRegen = 50, RunMetresPerSecond = 2, CycleSeconds = 600 })!;
        output.WriteLine(slow.Why);
        Assert.Equal((true, "time"), (slow.Costly, slow.Reason));
        Assert.True(slow.Seconds > slow.AllowedSeconds);
        Assert.StartsWith("the crossing costs more time than the rung's share of a cycle: ", slow.Why, StringComparison.Ordinal);
        Assert.False(ApproachCost.Judge(Holding.Key, approach, HoldingRun, input with { ManaRegen = 50 })!.Costly); // her own run: paid in time too
        Assert.Null(ApproachCost.Judge(Holding.Key, approach with { Encounters = null }, HoldingRun, input)); // no crossing facts: not judged
    }

    // ------------------------------------------------------------------------------------------------ M6 14 run past (D137): the crossing priced as a run past

    /// <summary>
    /// Her now as the brief reads her (level 64, about 199 health, War Magic 352 buffed, the Fire Sceptre at x1.08 on fire, the pick's line of 2026-11-14; 388 mana, 0.43 a second, the reserve 120: the way out's line of 15:28 UTC),
    /// her run at 7.7 m/s, the hits on her at her live factor (her armour 0.043 times her resistance about 0.35, the run leg's lines of 2026-11-14: 0.015) and the spells at her resistance (0.35, the bot's protection lines), her heal
    /// Heal Self VI (base 70, power 250) after her Mana Conversion over its mean 87.5 at the ladder's seconds; the creatures' paces by <paramref name="anim"/>.
    /// </summary>
    private static CrossingInput HerNow(Func<uint, double?> anim)
    {
        var ladder = LadderLoader.WarMage;
        var heal = ManaConversionModel.ExpectedCost(70, 250, 254);
        return Her() with
        {
            ManaStart = 388,
            ManaRegen = 0.43,
            RunMetresPerSecond = 7.7,
            CasterDamage = 1.08,
            PaceOf = h => CreaturePace.Of(h, anim),
            DamageFactor = _ => 0.015,
            SpellFactor = _ => 0.35,
            HealthMax = 199,
            HealthRegen = VitalsPolicy.Regen(VitalsPolicy.HealthAtOne, VitalsPolicy.CombatStance, 1),
            HealManaPerPoint = heal / MeasuredHeals.SelfHealSeed[5],
            HealSecondsPerPoint = ladder.VitalsPolicy.SelfSeconds(6) / MeasuredHeals.SelfHealSeed[5],
            HealWhy = "Heal Self VI",
        };
    }

    /// <summary>
    /// D137: the Holding's crossing priced as a run past, the motions' runs stand-ins (a tusker's 5, the Reaper's 2, a chittick's 1.5 m/s at rate 1; not the dats'): every tusker and the Reapers run faster than her 7.7 m/s and are fights
    /// she cannot avoid, their hits priced and healed; the chitticks, slower, are passed and their spines paid in health for the seconds in their range. The crossing still forces a recall before the portal, dearer than the fights alone
    /// (the heals). With no pace read every creature is a fight, as before. The Barracks' 19.6 m stay paid.
    /// </summary>
    [Fact]
    public void A_crossing_priced_as_a_run_past_fights_only_what_keeps_pace_and_heals_the_hits()
    {
        double? Stand(uint table) => table switch { 0x0900000Cu => 5.0, 0x0900001Au => 2.0, 0x09000079u => 1.5, _ => null };
        var input = HerNow(Stand);
        var cost = ApproachCost.Judge(Holding.Key, Holding.Approach!, HoldingRun, input)!;
        output.WriteLine(cost.Why);
        var kinds = cost.Kinds.ToDictionary(k => k.Wcid);
        Assert.Equal("fight", kinds[22053].Way); // an Assailer: 5 x 2.58 x 1.5, about 19 m/s
        Assert.Equal("fight", kinds[22747].Way); // the Reaper: 2 x 2.72 x 2.3, about 12.5 m/s
        Assert.Equal(("passing", "passing"), (kinds[22506].Way, kinds[4244].Way)); // the chitticks throw from 40.8 m and run under her
        Assert.True(kinds[22506].Speed < 7.7 * (1 - RunPast.PaceMarginShare), cost.Why);
        Assert.True(kinds[22506].Damage > 0 && kinds[22053].Damage > 0, cost.Why);
        Assert.InRange(cost.Fights, 1, cost.Creatures - 0.1);
        Assert.True(cost.HealMana > 0 && cost.Damage > 0, cost.Why);
        Assert.Equal((true, "mana"), (cost.Costly, cost.Reason));
        Assert.Contains("run past at 7.7 m/s: ", cost.Why, StringComparison.Ordinal);
        Assert.Contains(" damage taken, ", cost.Why, StringComparison.Ordinal);

        var fought = ApproachCost.Judge(Holding.Key, Holding.Approach!, HoldingRun, input with { PaceOf = null })!;
        Assert.Equal(fought.Creatures, fought.Fights, 6); // no pace read: every creature met is a fight
        Assert.True(fought.Casts > cost.Casts, fought.Why); // the passed chitticks cost no bolts

        var barracks = ApproachCost.Judge(Barracks.Key, Barracks.Approach!, BarracksRun, input with { RatePrevious = null, RateRung = null, RatesWhy = "the Barracks is no rung's ground" })!;
        Assert.False(barracks.Costly, barracks.Why);
    }

    /// <summary>
    /// D137, the verdicts for her now on the dats' runs (printed): the Holding's approach from the Barracks drop (1,507 m), the Barracks' own (19.6 m), and the 449 m from Stonehold's drop to the Phyntos Menace's portal through the High
    /// Direlands (its encounter rows and its mix generator's creatures read from the world database, below).
    /// </summary>
    [RequiresDatsFact]
    public void The_verdicts_for_her_now_priced_as_a_run_past_on_the_dats_runs()
    {
        using var dats = Lifestone.Data.Dat.DatSet.Open();
        var paces = CreaturePaces.FromDats(new Lifestone.Data.Animation.DatAnimationSource(dats));
        var input = HerNow(paces.AnimOf);
        var holding = ApproachCost.Judge(Holding.Key, Holding.Approach!, HoldingRun, input)!;
        output.WriteLine("HOLDING: " + holding.Why);
        output.WriteLine("  " + string.Join("; ", holding.Kinds.Select(k => $"{k.Name} {k.Way} {k.Speed:0.0} m/s x{k.Count:0.##} casts {k.Casts:0.#} damage {k.Damage:0}")));
        var barracks = ApproachCost.Judge(Barracks.Key, Barracks.Approach!, BarracksRun, input with { RatePrevious = null, RateRung = null, RatesWhy = "the Barracks is no rung's ground" })!;
        output.WriteLine("BARRACKS: " + barracks.Why);
        var stonehold = ApproachCost.Judge("phyntos-menace", Stonehold, StoneholdRun, input with { RatePrevious = null, RateRung = null, RatesWhy = "the Phyntos Menace is no rung's ground yet" })!;
        output.WriteLine("STONEHOLD: " + stonehold.Why);
        output.WriteLine("  " + string.Join("; ", stonehold.Kinds.Select(k => $"{k.Name} {k.Way} {k.Speed:0.0} m/s x{k.Count:0.###} casts {k.Casts:0.#} damage {k.Damage:0}")));
        Assert.True(holding.Costly, holding.Why); // the tuskers outrun her: the fights stay, and their hits are healed on top
        Assert.False(barracks.Costly, barracks.Why);
        Assert.All(holding.Kinds.Where(k => k.Wcid is 22053 or 27800 or 10810 or 11540), k => Assert.Equal("fight", k.Way));
        Assert.Contains(stonehold.Kinds, k => k.Way is "outrun" or "passing"); // the golems run under her
        Assert.True(double.IsFinite(stonehold.AtPortal) && double.IsFinite(stonehold.Mana), stonehold.Why); // a creature she cannot kill is a recall, not an endless fight
        Assert.Contains(stonehold.Kinds, k => k.Way == "unwinnable"); // the Magma Golem Exarch (fire 0) and the Crystal Lord (Magic Defense 900)
    }

    private const double StoneholdRun = 449; // the survey: network-to-stonehold's drop 0x64D5000B (30, 50) to the portal 4925 at 0x66D40102 (74.5, 108), straight

    /// <summary>A resistance table by element (slash, pierce, bludgeon, fire, cold, acid, electric).</summary>
    private static Dictionary<string, double> R(double slash, double pierce, double bludgeon, double fire, double cold, double acid, double electric) =>
        new(StringComparer.Ordinal) { ["slash"] = slash, ["pierce"] = pierce, ["bludgeon"] = bludgeon, ["fire"] = fire, ["cold"] = cold, ["acid"] = acid, ["electric"] = electric };

    /// <summary>
    /// The approach from Stonehold's drop to the Phyntos Menace's portal, read from the ACE world database (read-only, 2026-11-14): table encounter over 0x64D5, 0x65D5, 0x65D4 and 0x66D4 holds 6 rows, all the High Direlands Mountain
    /// Generator 1970 (0x65D5 1, 0x65D4 3, 0x66D4 2); its weenie_properties_generator rows' probability steps times each outcome's init_Create, a camp generator's creatures through its InitGeneratedObjects (type 82) and its rows' shares
    /// (Diamond and Gold Golem camps 2 each, the Banderling Thrasher camp 5, the Antagonists camp 4, the Destroyer Grievver camp 2, the Plate Armoredillo camp 2, the Lugian Tiatus camp 3, the Olthoi Warrior camp 3, the monouga camps 4, the
    /// Worn Weapons camp 4 Tumerok Troopers), 1.449 a roll listed, 0.125 unlisted (the plants, the scavenger hunts' camps, the seeds). Each creature's facts as the profiles' (level 25, xp 146, health init_Level + Endurance / 2, Magic
    /// Defense init_Level + (Focus + Self) / 7, Run init_Level + Quickness, resistances 64 to 70, awareness 31, scale 39, motion table d_i_d 2, the hardest body part); a caster's war spell its strongest damaging one (base_Intensity +
    /// variance, its share the sum of its damaging spells' spell book chances over 2), its range ACE's cap (Player.MaxRadarRange_Outdoors, 75 m: the dats' range rows are not read here).
    /// </summary>
    private static GroundApproach Stonehold => new([0x64D5, 0x65D5, 0x65D4, 0x66D4], StoneholdHostiles, "the ACE world database read-only, 2026-11-14 (see the summary)") { Encounters = 6, PerRoll = 1.5736, CostSource = "the same" };

    private static GroundApproachHostile[] StoneholdHostiles =>
    [
        new(24497, "Gotrok Tiatus") { Level = 100, MaxHealth = 450, DamageTypes = 0x4, DVal = 40, DVar = 0.3, Strength = 340, Pack = 3, MagicDefence = 336, Resist = R(0.66, 0.66, 0.66, 0.25, 0.42, 0.9, 1.0), AwarenessMetres = 23, PerRoll = 0.1453, Xp = 80000, RunSkill = 255, MotionTable = 0x09000006, Scale = 1 },
        new(7081, "Plate Armoredillo") { Level = 100, MaxHealth = 390, DamageTypes = 0x1, DVal = 80, DVar = 0.75, Strength = 240, Pack = 2, MagicDefence = 290, Resist = R(0.5, 1.0, 0.5, 0.5, 0.95, 0.7, 0.95), AwarenessMetres = 22, PerRoll = 0.1139, Xp = 80000, RunSkill = 255, MotionTable = 0x0900001C, Scale = 1.6 },
        new(7982, "Destroyer Grievver") { Level = 115, MaxHealth = 465, DamageTypes = 0x42, DVal = 80, DVar = 0.5, Strength = 220, Pack = 2, MagicDefence = 304, Resist = R(1.0, 0.9, 0.67, 0.25, 0.25, 0.25, 0.25), AwarenessMetres = 15, PerRoll = 0.1011, Xp = 125000, RunSkill = 220, MotionTable = 0x0900009A, Scale = 1.4, Ranged = new GroundRanged(136, 0.5, 0x20, 55, 75) { Share = 0.060, Spell = true } },
        new(7086, "Banderling Thrasher") { Level = 100, MaxHealth = 437, DamageTypes = 0x4, DVal = 65, DVar = 0.75, Strength = 210, Pack = 4, MagicDefence = 311, Resist = R(0.76, 0.65, 0.5, 1.08, 0.76, 0.65, 1.32), AwarenessMetres = 22, PerRoll = 0.0957, Xp = 80000, RunSkill = 220, MotionTable = 0x09000007, Scale = 1.3 },
        new(24277, "Banderling Slayer") { Level = 100, MaxHealth = 412, DamageTypes = 0x4, DVal = 15, DVar = 0.75, Strength = 200, Pack = 4, MagicDefence = 259, Resist = R(0.76, 0.65, 0.5, 0.75, 1.01, 0.95, 0.75), AwarenessMetres = 22, PerRoll = 0.0884, Xp = 80000, RunSkill = 220, MotionTable = 0x09000007, Scale = 1.3, Ranged = new GroundRanged(136, 0.5, 0x10, 55, 75) { Share = 0.030, Spell = true } },
        new(7096, "Gold Golem") { Level = 100, MaxHealth = 555, DamageTypes = 0x4, DVal = 90, DVar = 0.75, Strength = 250, Pack = 2, MagicDefence = 248, Resist = R(0.33, 0.33, 1.0, 1.0, 1.0, 1.0, 1.0), AwarenessMetres = 17, PerRoll = 0.0884, Xp = 80000, RunSkill = 160, MotionTable = 0x09000081, Scale = 1, Ranged = new GroundRanged(136, 0.5, 0x4, 55, 75) { Share = 0.228, Spell = true } },
        new(4216, "Diamond Golem") { Level = 100, MaxHealth = 555, DamageTypes = 0x4, DVal = 90, DVar = 0.75, Strength = 250, Pack = 2, MagicDefence = 255, Resist = R(0.33, 0.67, 1.0, 0.5, 0.5, 0.5, 0.5), AwarenessMetres = 17, PerRoll = 0.0884, Xp = 80000, RunSkill = 160, MotionTable = 0x09000081, Scale = 1, Ranged = new GroundRanged(136, 0.5, 0x4, 55, 75) { Share = 0.080, Spell = true } },
        new(7346, "Banderling Enforcer") { Level = 100, MaxHealth = 412, DamageTypes = 0x4, DVal = 65, DVar = 0.75, Strength = 200, Pack = 4, MagicDefence = 254, Resist = R(0.76, 0.65, 0.5, 1.08, 0.76, 0.65, 1.32), AwarenessMetres = 22, PerRoll = 0.0810, Xp = 80000, RunSkill = 220, MotionTable = 0x09000007, Scale = 1.3, Ranged = new GroundRanged(136, 0.5, 0x8, 55, 75) { Share = 0.053, Spell = true } },
        new(24275, "Banderling Antagonist") { Level = 100, MaxHealth = 412, DamageTypes = 0x4, DVal = 15, DVar = 0.75, Strength = 200, Pack = 4, MagicDefence = 254, Resist = R(0.76, 0.65, 0.5, 1.0, 0.76, 0.65, 1.0), AwarenessMetres = 22, PerRoll = 0.0766, Xp = 80000, RunSkill = 220, MotionTable = 0x09000007, Scale = 1.3, Ranged = new GroundRanged(136, 0.5, 0x8, 55, 75) { Share = 0.030, Spell = true } },
        new(23482, "Olthoi Warrior") { Level = 115, MaxHealth = 740, DamageTypes = 0x26, DVal = 100, DVar = 0.5, Strength = 330, Pack = 3, MagicDefence = 339, Resist = R(0.75, 1.0, 1.0, 0.75, 0.75, 0.42, 0.25), AwarenessMetres = 30, PerRoll = 0.0737, Xp = 125000, RunSkill = 240, MotionTable = 0x09000002, Scale = 1.3 },
        new(24291, "Merciless Monouga") { Level = 100, MaxHealth = 475, DamageTypes = 0x4, DVal = 70, DVar = 0.75, Strength = 320, Pack = 4, MagicDefence = 308, Resist = R(0.6, 0.9, 0.45, 0.85, 0.85, 0.75, 0.55), AwarenessMetres = 21, PerRoll = 0.0516, Xp = 80000, RunSkill = 260, MotionTable = 0x09000027, Scale = 1.2 },
        new(24958, "Olthoi Lancer") { Level = 100, MaxHealth = 615, DamageTypes = 0x22, DVal = 125, DVar = 0.5, Strength = 120, Pack = 3, MagicDefence = 306, Resist = R(0.7, 1.0, 0.75, 0.55, 0.6, 0.25, 0.45), AwarenessMetres = 28, PerRoll = 0.0442, Xp = 80000, RunSkill = 490, MotionTable = 0x0900012B, Scale = 0.8 },
        new(24290, "Insidious Monouga") { Level = 100, MaxHealth = 475, DamageTypes = 0x4, DVal = 80, DVar = 0.75, Strength = 325, Pack = 4, MagicDefence = 319, Resist = R(0.55, 0.8, 0.45, 0.85, 0.65, 0.85, 0.65), AwarenessMetres = 21, PerRoll = 0.0397, Xp = 80000, RunSkill = 260, MotionTable = 0x09000027, Scale = 1 },
        new(8138, "Extas Raider") { Level = 100, MaxHealth = 390, DamageTypes = 0x4, DVal = 40, DVar = 0.3, Strength = 290, Pack = 1, MagicDefence = 324, Resist = R(0.66, 0.66, 0.66, 0.25, 0.42, 0.9, 1.0), AwarenessMetres = 23, PerRoll = 0.0393, Xp = 80000, RunSkill = 205, MotionTable = 0x09000006, Scale = 1 },
        new(23566, "Tumerok Trooper") { Level = 115, MaxHealth = 400, DamageTypes = 0x4, DVal = 70, DVar = 0.75, Strength = 300, Pack = 4, MagicDefence = 324, Resist = R(1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0), AwarenessMetres = 15, PerRoll = 0.0360, Xp = 125000, RunSkill = 350, MotionTable = 0x0900000A, Scale = 1.2 },
        new(23616, "Crystal Shard") { Level = 100, MaxHealth = 215, DamageTypes = 0x4, DVal = 50, DVar = 0.75, Strength = 190, Pack = 1, MagicDefence = 292, Resist = R(1.0, 1.0, 1.0, 0.9, 0.65, 0.3, 1.0), AwarenessMetres = 12, PerRoll = 0.0295, Xp = 80000, RunSkill = 310, MotionTable = 0x090000A3, Scale = 1.5 },
        new(15267, "Acidic Diamond Golem") { Level = 100, MaxHealth = 555, DamageTypes = 0x4, DVal = 90, DVar = 0.75, Strength = 250, Pack = 1, MagicDefence = 283, Resist = R(0.33, 0.67, 1.0, 0.5, 0.5, 0.5, 0.5), AwarenessMetres = 17, PerRoll = 0.0295, Xp = 80000, RunSkill = 160, MotionTable = 0x09000081, Scale = 1, Ranged = new GroundRanged(136, 0.5, 0x4, 55, 75) { Share = 0.080, Spell = true } },
        new(14520, "Pyreal Golem") { Level = 100, MaxHealth = 555, DamageTypes = 0x4, DVal = 100, DVar = 0.75, Strength = 250, Pack = 1, MagicDefence = 273, Resist = R(0.1, 0.1, 0.6, 0.25, 0.25, 0.55, 0.1), AwarenessMetres = 22, PerRoll = 0.0295, Xp = 80000, RunSkill = 160, MotionTable = 0x09000081, Scale = 1, Ranged = new GroundRanged(136, 0.5, 0x40, 55, 75) { Share = 0.080, Spell = true } },
        new(28553, "Rufous Grievver") { Level = 80, MaxHealth = 240, DamageTypes = 0x42, DVal = 60, DVar = 0.5, Strength = 190, Pack = 1, MagicDefence = 255, Resist = R(1.0, 0.8, 0.7, 0.5, 0.5, 0.5, 0.5), AwarenessMetres = 10, PerRoll = 0.0295, Xp = 30000, RunSkill = 190, MotionTable = 0x0900009A, Scale = 1.2, Ranged = new GroundRanged(105, 0.5048, 0x20, 55, 75) { Share = 0.046, Spell = true } },
        new(21550, "Stasis Wisp") { Level = 100, MaxHealth = 225, DamageTypes = 0x18, DVal = 50, DVar = 0.5, Strength = 120, Pack = 1, MagicDefence = 275, Resist = R(1.0, 0.9, 0.9, 0.3, 0.0, 0.3, 0.9), AwarenessMetres = 30, PerRoll = 0.0295, Xp = 80000, RunSkill = 350, MotionTable = 0x0900008F, Scale = 1.3, Ranged = new GroundRanged(136, 0.5, 0x8, 55, 75) { Share = 0.150, Spell = true } },
        new(24292, "Pugnacious Monouga") { Level = 100, MaxHealth = 400, DamageTypes = 0x4, DVal = 60, DVar = 0.75, Strength = 250, Pack = 3, MagicDefence = 276, Resist = R(0.6, 0.9, 0.45, 0.45, 1.0, 0.75, 0.55), AwarenessMetres = 21, PerRoll = 0.0261, Xp = 80000, RunSkill = 240, MotionTable = 0x09000027, Scale = 1.4 },
        new(24287, "Bloodthirsty Monouga") { Level = 100, MaxHealth = 420, DamageTypes = 0x4, DVal = 60, DVar = 0.75, Strength = 300, Pack = 3, MagicDefence = 281, Resist = R(0.6, 0.9, 0.45, 0.45, 1.0, 0.75, 0.55), AwarenessMetres = 21, PerRoll = 0.0261, Xp = 80000, RunSkill = 250, MotionTable = 0x09000027, Scale = 1.3 },
        new(20190, "Gelid") { Level = 115, MaxHealth = 780, DamageTypes = 0x8, DVal = 100, DVar = 0.75, Strength = 150, Pack = 1, MagicDefence = 320, Resist = R(0.45, 0.45, 0.45, 0.65, 0.0, 0.3, 0.3), AwarenessMetres = 20, PerRoll = 0.0196, Xp = 125000, RunSkill = 260, MotionTable = 0x0900008F, Scale = 1.5, Ranged = new GroundRanged(168, 0.5, 0x8, 55, 75) { Share = 0.142, Spell = true } },
        new(20189, "Brumal") { Level = 100, MaxHealth = 575, DamageTypes = 0x8, DVal = 60, DVar = 0.75, Strength = 130, Pack = 1, MagicDefence = 287, Resist = R(0.45, 0.45, 0.45, 0.65, 0.0, 0.3, 0.3), AwarenessMetres = 20, PerRoll = 0.0196, Xp = 80000, RunSkill = 250, MotionTable = 0x0900008F, Scale = 1.3, Ranged = new GroundRanged(136, 0.5, 0x8, 55, 75) { Share = 0.142, Spell = true } },
        new(36443, "Harvest Reaper") { Level = 160, MaxHealth = 3300, DamageTypes = 0x4, DVal = 95, DVar = 0.75, Strength = 232, Pack = 1, MagicDefence = 401, Resist = R(1.7, 0.25, 1.58, 0.9, 0.3, 0.42, 0.4), AwarenessMetres = 16, PerRoll = 0.0168, Xp = 1000000, RunSkill = 315, MotionTable = 0x090001A8, Scale = 1.3 },
        new(24134, "Reedshark Slasher") { Level = 100, MaxHealth = 375, DamageTypes = 0x2, DVal = 110, DVar = 0.75, Strength = 155, Pack = 1, MagicDefence = 325, Resist = R(1.0, 0.75, 0.6, 0.38, 1.0, 0.75, 0.75), AwarenessMetres = 24, PerRoll = 0.0147, Xp = 80000, RunSkill = 265, MotionTable = 0x0900001A, Scale = 2.3 },
        new(36918, "Three Eyed Snowman") { Level = 135, MaxHealth = 20000, DamageTypes = 0x4, DVal = 10, DVar = 0.5, Strength = 370, Pack = 1, MagicDefence = 214, Resist = R(0.64, 0.66, 0.67, 0.92, 0.0, 0.65, 0.66), AwarenessMetres = 5, PerRoll = 0.0056, Xp = 250000, RunSkill = 540, MotionTable = 0x09000090, Scale = 0.8 },
        new(27566, "Magma Golem Exarch") { Level = 135, MaxHealth = 5000, DamageTypes = 0x4, DVal = 75, DVar = 0.75, Strength = 280, Pack = 1, MagicDefence = 276, Resist = R(0.5, 0.5, 0.5, 0.0, 1.0, 0.5, 0.5), AwarenessMetres = 17, PerRoll = 0.0049, Xp = 250000, RunSkill = 190, MotionTable = 0x09000081, Scale = 1.75, Ranged = new GroundRanged(136, 0.5, 0x4, 55, 75) { Share = 0.148, Spell = true } },
        new(14875, "Hyem") { Level = 100, MaxHealth = 575, DamageTypes = 0x8, DVal = 30, DVar = 0.75, Strength = 130, Pack = 1, MagicDefence = 268, Resist = R(0.45, 0.45, 0.45, 0.65, 0.0, 0.3, 0.3), AwarenessMetres = 20, PerRoll = 0.0029, Xp = 80000, RunSkill = 250, MotionTable = 0x0900008F, Scale = 1.4, Ranged = new GroundRanged(136, 0.5, 0x8, 55, 75) { Share = 0.008, Spell = true } },
        new(11991, "Diamond Lord") { Level = 100, MaxHealth = 645, DamageTypes = 0x4, DVal = 90, DVar = 0.75, Strength = 400, Pack = 1, MagicDefence = 323, Resist = R(0.13, 0.37, 0.5, 0.25, 0.25, 0.25, 0.25), AwarenessMetres = 17, PerRoll = 0.0010, Xp = 80000, RunSkill = 300, MotionTable = 0x09000081, Scale = 1, Ranged = new GroundRanged(189, 0.3915, 0x4, 55, 75) { Share = 0.180, Spell = true } },
        new(14801, "Crystal Lord") { Level = 220, MaxHealth = 15000, DamageTypes = 0x4, DVal = 1600, DVar = 0.5, Strength = 400, Pack = 1, MagicDefence = 900, Resist = R(0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1), AwarenessMetres = 10, PerRoll = 0.0010, Xp = 1400000, RunSkill = 210, MotionTable = 0x09000081, Scale = 1.6, Ranged = new GroundRanged(168, 0.5, 0x20, 55, 75) { Share = 0.140, Spell = true } },
    ];

    private static double Number(string text, string before, string after)
    {
        var from = text.IndexOf(before, StringComparison.Ordinal) + before.Length;
        return double.Parse(text[from..text.IndexOf(after, from, StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------------------------------------ M6 14 walk dance: the price and the measure count the stamina dance

    /// <summary>The motions' runs stand-ins of the run past test (a tusker's 5, the Reaper's 2, a chittick's 1.5 m/s at rate 1; not the dats').</summary>
    private static double? Stand(uint table) => table switch { 0x0900000Cu => 5.0, 0x0900001Au => 2.0, 0x09000079u => 1.5, _ => null };

    /// <summary>
    /// Her at level 68 as the way out read her at 01:55:46 UTC (live <c>cache/m6/14/live/ladder22</c>): War Magic 361 buffed, Mana Conversion 264, 393 mana at 0.43 a second, the reserve 120, her run at 7.9 m/s, Ilservian's Flame (power 300,
    /// the cast's ceiling 35) in the Fire Baton at x1.13, 204 health and Heal Self VI; the hits at the run past test's factor; the paces by the stand-ins.
    /// </summary>
    private static CrossingInput HerAt68()
    {
        var ladder = LadderLoader.WarMage;
        Assert.True(WarSpellData.TryGet(2128, out var row)); // Ilservian's Flame
        var heal = ManaConversionModel.ExpectedCost(70, 250, 264);
        return HerNow(Stand) with
        {
            War = 361,
            ManaConversion = 264,
            ManaStart = 393,
            ManaRegen = 0.43,
            RunMetresPerSecond = 7.9,
            CasterDamage = 1.13,
            Bolt = new CrossingBolt("Ilservian's Flame", "fire", row, 300, 35, ladder.VitalsPolicy.BoltSeconds(7)),
            HealthMax = 204,
            HealManaPerPoint = heal / MeasuredHeals.SelfHealSeed[5],
        };
    }

    /// <summary>
    /// M6 14 walk dance (rule 1; the owner, 2026-11-15: at her level the stamina dance replaces the potions; live 01:55:46 UTC: the Holding's 1,507 m approach called approach-costly (mana), -1,124 at the portal, its 994 s well inside the
    /// 2,869 s the rung's rate allows): at her level-68 shape the crossing alone is costly for mana; with the dance read on her state (<see cref="ApproachCrossing.DanceInput"/>: Revitalize Self VI then the best-net press, about 63 mana a
    /// round in two casts at the ladder's self seconds) the shortfall at the reserve is made up in rounds of the dance, their seconds added to the crossing's, and the crossing is paid, inside the time the rung's rate allows.
    /// </summary>
    [Fact]
    public void With_the_stamina_dance_the_Holdings_price_at_her_level_68_shape_is_paid_with_its_seconds()
    {
        var ladder = LadderLoader.WarMage;
        var input = HerAt68();
        var before = ApproachCost.Judge(Holding.Key, Holding.Approach!, HoldingRun, input)!;
        output.WriteLine("ALONE: " + before.Why);
        Assert.Equal((true, "mana"), (before.Costly, before.Reason));
        Assert.True(before.AtPortal < 0, before.Why); // far under the reserve, as live (-1,124)
        Assert.Equal(0, before.DanceRounds);

        var table = StaminaDanceTests.Table();
        var danced = ApproachCrossing.DanceInput(StaminaDanceTests.Her(table), table, input, ladder);
        output.WriteLine("DANCE: " + danced.DanceWhy);
        Assert.True(danced.DanceCovers, danced.DanceWhy);
        Assert.InRange(danced.DanceNet, 55, 70); // the live reading: +63 a round
        Assert.Equal(ladder.VitalsPolicy.SelfSeconds(6) * 2, danced.DanceSeconds, 6); // Revitalize Self VI and Stamina to Mana Self VI
        Assert.StartsWith("Revitalize Self VI (120 stamina for ", danced.DanceWhy, StringComparison.Ordinal);

        var paid = ApproachCost.Judge(Holding.Key, Holding.Approach!, HoldingRun, danced)!;
        output.WriteLine("WITH THE DANCE: " + paid.Why);
        var rounds = (int)Math.Ceiling((input.Reserve - before.AtPortal) / danced.DanceNet);
        Assert.Equal(rounds, paid.DanceRounds);
        Assert.Equal(rounds * danced.DanceSeconds, paid.DanceSeconds, 6);
        Assert.Equal(before.Seconds + paid.DanceSeconds, paid.Seconds, 6);
        Assert.Equal((false, string.Empty), (paid.Costly, paid.Reason));
        Assert.True(paid.AtPortal >= input.Reserve, paid.Why);
        Assert.True(paid.Seconds < paid.AllowedSeconds, paid.Why); // paid in time too: the dance's seconds are inside what the rung's rate allows
        Assert.StartsWith("the crossing is paid: 1507 m of tusker-holding's approach", paid.Why, StringComparison.Ordinal);
        Assert.Contains(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"; with {rounds} rounds of the stamina dance ({paid.DanceSeconds:0} s): "), paid.Why, StringComparison.Ordinal);

        // the time still holds it when the dance's seconds take the crossing past what the rung's rate allows (a slow round)
        var slow = ApproachCost.Judge(Holding.Key, Holding.Approach!, HoldingRun, danced with { DanceSeconds = 200 })!;
        output.WriteLine("SLOW DANCE: " + slow.Why);
        Assert.Equal((true, "time"), (slow.Costly, slow.Reason));
        Assert.Equal(paid.DanceRounds, slow.DanceRounds);
    }

    /// <summary>M6 14 walk dance: with the dance not covering her (Mana Conversion untrained, or no Revitalize Self in her book) the Holding's price is the crossing's alone, and the line says why the dance does not cover her.</summary>
    [Fact]
    public void Without_the_dance_covering_her_the_Holdings_price_is_unchanged()
    {
        var ladder = LadderLoader.WarMage;
        var input = HerAt68();
        var before = ApproachCost.Judge(Holding.Key, Holding.Approach!, HoldingRun, input)!;
        var table = StaminaDanceTests.Table();
        foreach (var (state, why) in new[] { (StaminaDanceTests.Her(table, manaConversion: false), "Mana Conversion is not trained"), (StaminaDanceTests.Her(table, revitalize: []), "no Revitalize Self level the book holds and the pack pays") })
        {
            var none = ApproachCrossing.DanceInput(state, table, input, ladder);
            Assert.False(none.DanceCovers);
            Assert.Equal(0, none.DanceNet);
            var cost = ApproachCost.Judge(Holding.Key, Holding.Approach!, HoldingRun, none)!;
            output.WriteLine(cost.Why);
            Assert.Equal((true, "mana", before.AtPortal, before.Seconds, 0), (cost.Costly, cost.Reason, cost.AtPortal, cost.Seconds, cost.DanceRounds));
            Assert.Contains("; the stamina dance does not cover her: " + why, cost.Why, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// M6 14 walk dance (rule 2; live 2026-11-15 01:54:50 UTC, ladder22: on the run to the Tusker Armory's entry front, 4 kills in 12 casts, mana 328 to 200 over 61 m gained, 283 m to the front: "7.2 kills, 232 mana, against 136 over the
    /// reserve 120 (56 of it a Stamina to Mana press)", and she recalled). With the dance not read the same numbers give the crossing up as before; with the dance covering her (its round read on her level-68 shape) the crossing is not given
    /// up: a dance is due at the next lull, and the crossing is measured again after; M6 14 top-off (the owner, 2026-11-15): it tops her off, its line her maximum 393 (the need, 352 with the reserve, is said). The dance's own mana is taken
    /// out of the fall a kill.
    /// </summary>
    [Fact]
    public void The_Armorys_live_measure_is_not_given_up_while_the_dance_covers_her_and_a_dance_is_due()
    {
        var prior = ApproachCost.CreaturesPerMetre(GroundCatalog.Get("tusker-armory").Approach!)!.Value;
        var live = new CrossingMeasure(4, 12, 328, 200, 120, 343, 283, 3, prior, StaminaMana: 56);
        var bare = ApproachCost.Measured(in live);
        output.WriteLine("LIVE: " + bare.Why);
        Assert.True(bare.Abandon);
        Assert.InRange(bare.Needed, 225, 240); // live 232
        Assert.Equal(136, Math.Round(bare.Above));
        Assert.False(bare.DanceDue);

        var table = StaminaDanceTests.Table();
        var round = StaminaDance.Round(StaminaDanceTests.Her(table), table, out var roundWhy)!.Value;
        var covered = live with { DanceNet = round.Net, ManaMax = 393 };
        var danced = ApproachCost.Measured(in covered);
        output.WriteLine("DANCE: " + danced.Why);
        Assert.False(danced.Abandon, danced.Why);
        Assert.True(danced.DanceDue);
        Assert.Equal(393u, danced.DanceTo); // M6 14 top-off: her maximum, not the reserve and the need
        Assert.StartsWith("she will not reach the portal above the reserve on the mana she holds, but the stamina dance covers her: the stamina dance at the next lull tops her off: mana to her maximum 393, stamina near full (the rest needs ", danced.Why, StringComparison.Ordinal);
        Assert.Equal(DanceTo(live with { DanceNet = round.Net }), (uint)Math.Ceiling(120 + bare.Needed)); // her maximum not read: the reserve and the need, as before
        Assert.Contains("then the crossing is measured again", danced.Why, StringComparison.Ordinal);
        Assert.Equal(bare.Needed, danced.Needed, 6);

        // after a dance of 150 the fall a kill is the fights' alone, the same 32
        var after = ApproachCost.Measured(covered with { ManaNow = 350, DancedMana = 150 });
        Assert.Equal(bare.ManaPerKill, after.ManaPerKill, 6);
        Assert.False(after.Abandon);
        Assert.Contains(", the dances' 150 taken out)", after.Why, StringComparison.Ordinal);

        // a need over her maximum: the line is capped there, and at it no dance is due now (she dances after the next fight)
        var capped = ApproachCost.Measured(covered with { ManaNow = 393, DancedMana = 193, MetresNow = 2000 });
        Assert.Equal((false, false, 393u), (capped.Abandon, capped.DanceDue, capped.DanceTo));
        Assert.Contains("she holds her maximum, so she dances at the next lull after the next fight", capped.Why, StringComparison.Ordinal);
        output.WriteLine(roundWhy);
    }

    private static uint DanceTo(in CrossingMeasure m) => ApproachCost.Measured(in m).DanceTo;

    /// <summary>M6 14 walk dance (rule 2): the Armory's live measure is given up when the dance cannot keep pace (its stop on the run, named) or does not cover her (why, named); the numbers are the live ones either way.</summary>
    [Fact]
    public void The_Armorys_live_measure_is_given_up_when_the_dance_cannot_keep_pace_or_does_not_cover_her_and_says_which()
    {
        var prior = ApproachCost.CreaturesPerMetre(GroundCatalog.Get("tusker-armory").Approach!)!.Value;
        var live = new CrossingMeasure(4, 12, 328, 200, 120, 343, 283, 3, prior, StaminaMana: 56);
        var bare = ApproachCost.Measured(in live);
        var table = StaminaDanceTests.Table();
        var round = StaminaDance.Round(StaminaDanceTests.Her(table), table, out _)!.Value;
        var stalled = ApproachCost.Measured(live with { DanceNet = round.Net, ManaMax = 393, DanceStalled = "walk-dance:attacked: attacked again after 3 fight-backs in the dance: the attackers never leave the lull" });
        output.WriteLine(stalled.Why);
        Assert.Equal((true, false, 0u), (stalled.Abandon, stalled.DanceDue, stalled.DanceTo));
        Assert.Equal(bare.Why + "; the stamina dance cannot keep pace: walk-dance:attacked: attacked again after 3 fight-backs in the dance: the attackers never leave the lull", stalled.Why);

        Assert.Null(StaminaDance.Round(StaminaDanceTests.Her(table, manaConversion: false), table, out var untrained));
        var none = ApproachCost.Measured(live with { DanceWhy = untrained });
        output.WriteLine(none.Why);
        Assert.Equal((true, false), (none.Abandon, none.DanceDue));
        Assert.Equal(bare.Why + "; the stamina dance does not cover her: " + untrained, none.Why);
        Assert.Equal((bare.Needed, bare.Above), (none.Needed, none.Above));
    }

    private static RungAsk Ask(int level, string run)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
        return new RungAsk(level, null, q, static (_, _) => true, new RungBuffRead(static _ => 8, static _ => 4)) { RunGround = run, Leaving = true };
    }
}
