using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Autopilot;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Net.Messages;
using Xunit;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 14 retreat attackers (live 2026-11-14 10:46 to 11:01 UTC, <c>cache/m6/14/live/holdtrial1</c>: the climber, level 62 and 197 health, died on the Tusker Holding's approach to two Infected Assailers, acid, while the run leg's retreat line
/// was the Holding's Rampager in bludgeon under the bot's protection, 10%, and her mana was spent under Lifestone Recall's 50): the line from the attackers present, by their own facts and damage type and her live resistance to it; the
/// general fallback for an attacker with no facts; the measured floor; the way out's reserve under the vitals guard; the approach check that holds the Holding and a trial by name; the corpse trip that refuses an approach she cannot
/// survive. Her numbers: the armour line the leg logged at 10:48:57 (six pieces under Impenetrability VI and the banes VI: 0.045), the bot's protections (bludgeon 0.315, every other type 0.35), the Infected Assailer's facts as the
/// Holding's approach records them.
/// </summary>
public sealed class RetreatAttackersTests
{
    private const uint HealthMax = 197; // the climber at level 62

    private const double LiveArmour = 0.045; // the leg's armour line at 10:48:57 UTC: armour 0.045 on 6 piece(s), body armour 225, Impenetrability 200, bane 1.5

    private const double LiveBludgeonResistance = 0.315; // the same line's resistance under the bot's Boon of the Mace Turner

    private static readonly LadderHitter Rampager = new("Rampager", 10810, 160, 0.75, 230);

    private static CreatureFact Fact(uint wcid) => CreatureFacts.All.Find(wcid) ?? throw new InvalidOperationException($"no profile records weenie {wcid}");

    /// <summary>Her factor against the worst bit of <paramref name="types"/>: her live armour times the bot's protection against the bit (acid's as <paramref name="acid"/>).</summary>
    private static (double Factor, uint Type) Her(uint types, double acid)
    {
        var (best, bit) = (-1.0, 0u);
        for (var b = 1u; b <= 0x40; b <<= 1)
        {
            if (((types == 0 ? 0x7Fu : types) & b) != 0 && LiveArmour * (b == 0x4 ? LiveBludgeonResistance : b == 0x20 ? acid : 0.35) is var f && f > best)
            {
                (best, bit) = (f, b);
            }
        }

        return (best, bit);
    }

    private const uint ResistFloat = 0x2000000 | EnchantmentRules.Multiplicative | 0x1000 | FightLines.FloatKind;
    private const uint BodyArmour = 0x2000000 | EnchantmentRules.Additive | 0x2000 | FightLines.BodyArmourKind;

    private static EnchantmentDesc Ench(ushort spell, ushort category, uint power, uint type, uint key, float value) =>
        new(spell, 1, category, 0, power, 0, 3600, 0x50000001, 0f, 0f, 0.0, type, key, value, 0, (type & EnchantmentRules.Additive) != 0 ? EnchantmentBucket.Additive : EnchantmentBucket.Multiplicative);

    private static GameState Climber(params EnchantmentDesc[] registry)
    {
        var state = new GameState();
        state.Self.Qualities.IsKnown = true;
        state.Self.Qualities.Vitals[MageTemplate.MaxHealth] = new VitalDesc(MageTemplate.MaxHealth, HealthMax, 0, 0, HealthMax);
        state.Self.Qualities.Enchantments = [.. registry];
        return state;
    }

    [Fact]
    public void The_live_acid_case_lifts_the_line_far_over_ten_percent_and_she_leaves_before_the_health_is_under_it()
    {
        // the line the leg read live: the destination's Rampager, bludgeon, through her armour and the bot's protection
        var place = FightLines.Derive(new FightLineInput("tusker-holding", HealthMax, Rampager, 1, LiveArmour, LiveBludgeonResistance, 0, 0, null));
        Assert.Equal(15, place.Percent); // M6 14 the losing race: over the way out's 4.45 s (the live leg read 10% over 4 s)

        // the attackers present at 10:57:54 by their own facts: two Infected Assailers, acid, with no acid protection read (resistance 1)
        var assailer = Fact(27800);
        Assert.Equal((0x20u, 439, 4, "tusker-holding:approach"), (assailer.DamageTypes, assailer.Hitter.Hit, assailer.Pack, assailer.Where));
        AttackerRead[] present = [new(0x800008F7, 27800, "Infected Assailer", assailer, 0x20, LiveArmour, 1.0), new(0x800008F6, 27800, "Infected Assailer", assailer, 0x20, LiveArmour, 1.0)];
        var model = FightLines.DeriveAttackers(HealthMax, present, 0, 0);
        Assert.Equal(75, model.Percent); // M6 14 the losing race: 2 x the mean hit with its criticals (0.9 x 439 + 0.1 x 1404 = 535.5) x 0.045 every 2.75 s for the way out's 4.45 s is 78, and a critical of 702 x 2 x 0.045 = 63.2: 141.2 of 197 (72%)
        Assert.Contains("'Infected Assailer' 0x800008F7 (tusker-holding:approach: hit 439, damage 0x20 x armour 0.045 x resistance 1 = 0.045; an attack every 2.75 s, 0.36 hits/s (TODO(f11-swing-seconds): 'Infected Assailer' (weenie 27800) is not derived (no timing read for this line)", model.Why(), StringComparison.Ordinal); // M6 14 attack timing: no timing given, the named fallback

        // the hits measured from 10:59:05 to 10:59:16: 169 in 11 s, the largest 40
        var measured = FightLines.DeriveAttackers(HealthMax, present, 169.0 / 11, 40);
        Assert.Equal(75, measured.Percent);

        // the health at the leg's checks in fight-back 6 (after each bolt and the guard's heals): she leaves at the first under the line
        uint[] checks = [182, 183, 193, 178, 182, 123, 86, 15];
        Assert.Equal(123u, checks.First(h => h * 100.0 < measured.Percent * HealthMax)); // at 123 of 197, with 108 left over the hits while she recalls
        Assert.Equal(15u, checks.First(h => h * 100.0 < place.Percent * HealthMax)); // the old line's: 15, two seconds before the death (29.6 at 15%)

        // under the bot's Caustic Boon (acid x0.35, which the live registry held), the measured hits alone carry the line: she leaves at 86
        AttackerRead[] protectedRead = [present[0] with { Resistance = 0.35 }, present[1] with { Resistance = 0.35 }];
        var underBoon = FightLines.DeriveAttackers(HealthMax, protectedRead, 169.0 / 11, 40);
        Assert.Equal(60, underBoon.Percent); // 15.4 a second for 4.45 s and the largest 40: 108.4 of 197 (55.01%)
        Assert.Equal(86u, checks.First(h => h * 100.0 < underBoon.Percent * HealthMax));
    }

    [Fact]
    public void An_attacker_with_no_facts_gives_the_general_fallback_by_name_and_the_live_reader_reads_each_type()
    {
        Assert.Equal((int)AutopilotScenario.HealthFloorPercent, FightLines.GeneralFallbackPercent); // the old general default the line used before it was derived
        var unknown = FightLines.DeriveAttackers(HealthMax, [new(0x80001944, 999_999, "Nobody's Creature", null, 0, 1, 1)], 0, 0);
        Assert.Equal((50, 5, true, 1), (unknown.Percent, unknown.LossPercent, unknown.Fallback, unknown.Unknown));
        Assert.Contains("'Nobody's Creature' 0x80001944 (weenie 999999: in no ground profile and not in the world database's creature table, TODO(m6-creature-attack-unknown): the general fallback 50%)", unknown.Why(), StringComparison.Ordinal);
        Assert.Contains("lifted from 5% to the general fallback 50% (rule 2)", unknown.Why(), StringComparison.Ordinal);
        Assert.Equal(50, FightLines.DeriveAttackers(HealthMax, [], 0, 0).Percent); // no attacker read: the fallback

        // the live reader: each attacker by its weenie class across every profile, its own damage type, her live resistance to it
        var state = Climber(Ench(2101, 901, 300, BodyArmour, 0, 225f), Ench(2152, 900, 300, ResistFloat, FightLines.ResistBludgeonKey, 0.35f)); // Armor Self VII and Bludgeoning Protection VII only
        var line = new LiveFightLine().ReadAttackers(state, [new(1, 999_999, "Nobody's Creature", 3), new(2, 27800, "Infected Assailer", 3), new(3, 10810, "Rampager", 4)], 0, 0, 0);
        Assert.Null(line.Attackers[0].Fact); // M6 14 the losing race: only a weenie the world database does not hold is unknown (a Female Tusker, 236, resolves from its table)
        Assert.StartsWith("the world database's creature table", CreatureFacts.All.Find(236)!.Where, StringComparison.Ordinal);
        Assert.Equal((0x20u, 1.0), (line.Attackers[1].DamageType, line.Attackers[1].Resistance)); // acid: the bludgeon protection does not touch it
        Assert.Equal((0x4u, 0.35), (line.Attackers[2].DamageType, Math.Round(line.Attackers[2].Resistance, 6)));
        Assert.Equal(FightLines.MaxPercent, line.Percent); // the body armour alone (no piece read) against unprotected acid: the top, she does not stay
        Assert.Equal(1, line.Unknown);
    }

    [Fact]
    public void The_measured_hits_are_the_lines_floor()
    {
        Assert.Equal(60, FightLines.MeasuredPercent(HealthMax, 169.0 / 11, 40)); // 15.4 a second for the way out's 4.45 s and the largest 40: 108.4 of 197
        Assert.Equal(FightLines.MinPercent, FightLines.MeasuredPercent(HealthMax, 0, 0));
        Assert.Equal(FightLines.MaxPercent, FightLines.MeasuredPercent(0, 1, 1)); // an unread health is the top

        // a weak attacker's model under the measured hits: the measure carries the line
        var reedshark = new AttackerRead(1, 22746, "Reedshark Ravager", Fact(22746), 0x2, LiveArmour, 0.35);
        Assert.Equal(5, FightLines.DeriveAttackers(HealthMax, [reedshark], 0, 0).Percent); // 119 x 0.0158 every 2.75 s for 4 s and a critical of 6: 8.7 of 197
        var hit = FightLines.DeriveAttackers(HealthMax, [reedshark], 169.0 / 11, 40);
        Assert.Equal(FightLines.MeasuredPercent(HealthMax, 169.0 / 11, 40), hit.Percent);
        Assert.Equal(40, hit.Critical); // the largest hit seen over the model's critical
    }

    [Fact]
    public void The_vitals_guard_spends_no_heal_under_the_recall_a_fight_back_keeps_in_reserve()
    {
        var rig = new FightManaTests.Rig(mana: 200, stamina: 200, knowsDrain: true);
        rig.Caster.ManaRegenPerSecond = 0;
        AutopilotF30Tests.Settle(rig);
        rig.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        rig.Caster.SetHealth(max * 30 / 100);
        rig.R.Tick();
        var ground = rig.Fight.GroundProfile;
        using var watch = new HostileWatch(rig.View, ground, 20);
        var reserve = ManaWayOut.LineOf(195, 70); // a recall priced so that no heal over it is payable at mana 200
        var guard = new AutopilotVitalsGuard(rig.Caster.Table, ground, new AutopilotOptions(), 0.9, watch, rig.Log) { Reserve = () => reserve };
        var until = rig.Now + 30;
        while (rig.Now < until && !guard.NothingPaid)
        {
            rig.R.Tick(0.1);
            rig.Caster.Pump();
            rig.R.Tick();
            guard.Step(rig.View, rig.Now, "travel");
        }

        Assert.DoesNotContain(rig.Caster.Answered, a => VitalsMeans.HealSelf.Contains(a.Spell));
        Assert.True(guard.NothingPaid, string.Join(" | ", rig.Log.Lines.Where(l => l.Contains("guard", StringComparison.Ordinal)).Take(8)));
        Assert.False(guard.Holding); // the hold is released: the leg's next check leaves by the recall it can still pay
        Assert.True(rig.Log.Lines.Count(l => l.Contains("guard: travel: health ", StringComparison.Ordinal) && l.Contains("under Lifestone Recall's 195 (the fight-back's reserve, line 265): refused (drain-refused)", StringComparison.Ordinal)) == 1, string.Join(" | ", rig.Log.Lines.Where(l => l.Contains("WARN  [autopilot]", StringComparison.Ordinal))));

        guard.Reserve = () => null; // the fight-back is over: the heal goes as before
        var end = rig.Now + 30;
        while (rig.Now < end && !rig.Caster.Answered.Any(a => VitalsMeans.HealSelf.Contains(a.Spell)))
        {
            rig.R.Tick(0.1);
            rig.Caster.Pump();
            rig.R.Tick();
            guard.Step(rig.View, rig.Now, "travel");
        }

        Assert.Contains(rig.Caster.Answered, a => VitalsMeans.HealSelf.Contains(a.Spell));
    }

    [Fact]
    public void The_Holdings_approach_is_recorded_as_facts_and_judged_at_her_state()
    {
        var holding = GroundCatalog.Get("tusker-holding");
        var approach = holding.Approach!;
        Assert.Equal(11, approach.Landblocks.Count);
        Assert.Same(approach, holding.ApproachAt(0xEC7D0019)); // where she died
        Assert.Same(approach, holding.ApproachAt(holding.Entry!.FrontCell));
        Assert.Contains((27800u, 4), approach.Hostiles.Select(h => (h.Wcid, h.Pack)));
        Assert.Contains((22506u, 0x43u), approach.Hostiles.Select(h => (h.Wcid, h.DamageTypes))); // the Iron Spined Chittick: its parts' slash and pierce, its spines' electric
        Assert.All(approach.Hostiles, h => Assert.NotNull(h.Hitter));
        Assert.All(GroundCatalog.Get("tusker-barracks").Approach!.Hostiles, h => Assert.NotNull(h.Hitter)); // the Barracks' three carry theirs

        // her state with no acid protection (the case the brief names): the worst camp, four Infected Assailers, takes 90% of her while she recalls: held
        var held = ApproachCheck.Judge(holding.Key, approach, HealthMax, recallReady: true, t => Her(t, acid: 1.0));
        Assert.False(held.Survivable);
        Assert.Equal((27800u, 0x20u), (held.Worst!.Value.Hostile.Wcid, held.Worst.Value.DamageType));
        Assert.Equal(191.0, held.Worst.Value.Loss, 1); // 4 x 439 x 0.045 every 2.75 s for the way out's 4.45 s is 127.9, and a critical of 63.2
        Assert.StartsWith("tusker-holding's approach is not survivable at her state: the worst camp is 4 x 'Infected Assailer' (weenie 27800, level 115, damage 0x20)", held.Why, StringComparison.Ordinal);
        Assert.StartsWith("approach: ", held.Held, StringComparison.Ordinal);

        // under the bot's Caustic Boon (acid x0.35, as the live registry held it): 62 of 197 (32%), the line 35: survivable
        var passed = ApproachCheck.Judge(holding.Key, approach, HealthMax, recallReady: true, t => Her(t, acid: 0.35));
        Assert.True(passed.Survivable, passed.Why);
        Assert.Equal(27800u, passed.Worst!.Value.Hostile.Wcid);
        Assert.Equal(66.9, passed.Worst.Value.Loss, 1); // 4 x 439 x 0.01575 every 2.75 s for 4.45 s is 44.8, and a critical of 22.1

        // unknown never sends her: an unread health, a book without Lifestone Recall
        Assert.False(ApproachCheck.Judge(holding.Key, approach, 0, true, t => Her(t, 0.35)).Survivable);
        Assert.Contains("her book holds no Lifestone Recall", ApproachCheck.Judge(holding.Key, approach, HealthMax, false, t => Her(t, 0.35)).Why, StringComparison.Ordinal);

        // the Barracks' approach at the same state: survivable (its camps are tuskers, bludgeon)
        Assert.True(ApproachCheck.Judge("tusker-barracks", GroundCatalog.Get("tusker-barracks").Approach!, HealthMax, true, t => Her(t, 1.0)).Survivable);
    }

    [Fact]
    public void An_approach_she_cannot_survive_holds_the_trial_and_the_rung_by_name_and_she_hunts_her_own_ground()
    {
        var holding = GroundCatalog.Get("tusker-holding");
        var unsurvivable = ApproachCheck.Judge(holding.Key, holding.Approach!, HealthMax, true, t => Her(t, acid: 1.0));
        var survivable = ApproachCheck.Judge(holding.Key, holding.Approach!, HealthMax, true, t => Her(t, acid: 0.35));
        Func<string, ApproachVerdict?> Judge(ApproachVerdict verdict) => key => key == holding.Key ? verdict : null;
        var ladder = LadderLoader.WarMage;

        // the trial switch at 62, the way out to the Holding: refused by the approach, by name; the ladder's own rung is chosen
        var trial = new LadderTrial(holding.Key);
        var tried = RungOwner.Decide(ladder, Ask(62, holding.Key, leaving: true) with { Trial = trial, Approach = Judge(unsurvivable) });
        Assert.False(tried.Trial!.Steers);
        Assert.StartsWith("approach: tusker-holding's approach is not survivable at her state", tried.Trial.Refused, StringComparison.Ordinal);
        Assert.Same(unsurvivable, tried.ApproachHeld);
        Assert.NotEqual(holding.Key, tried.Ground?.Key ?? tried.Decision.Rung?.GroundKey);
        trial.Note(tried.Trial, 62, "the way out", NullLogSink.Instance);
        Assert.Equal(LadderTrialPhase.Refused, trial.Phase);

        var open = RungOwner.Decide(ladder, Ask(62, holding.Key, leaving: true) with { Trial = new LadderTrial(holding.Key), Approach = Judge(survivable) });
        Assert.True(open.Trial!.Steers);
        Assert.Null(open.ApproachHeld);

        // the ladder's own way out at 65, the Holding's band: held by name, one rung down, her own ground
        var own = RungOwner.Decide(ladder, Ask(65, "tusker-quarters", leaving: true) with { Approach = Judge(unsurvivable) });
        Assert.Equal(("approach-held:holding", "quarters"), (own.Reason, own.Decision.Rung?.Key));
        Assert.Contains("the rung holding is held and she hunts her own ground instead", own.Why, StringComparison.Ordinal);
        Assert.Equal("holding", RungOwner.Decide(ladder, Ask(65, "tusker-quarters", leaving: true) with { Approach = Judge(survivable) }).Decision.Rung?.Key);
        Assert.Equal("holding", RungOwner.Decide(ladder, Ask(65, "tusker-quarters", leaving: false) with { Approach = Judge(unsurvivable) }).Decision.Rung?.Key); // not a way out: not read
    }

    [Fact]
    public void The_corpse_trip_refuses_a_corpse_in_an_approach_she_cannot_survive_and_names_the_rot()
    {
        const uint deathCell = 0xEC7D0019; // where she died, on the Holding's approach and on no rung's ground
        Assert.Null(RungOwner.GroundOf(LadderLoader.WarMage, deathCell));
        var place = ApproachCheck.ApproachOf(GroundCatalog.All, deathCell);
        Assert.Equal("tusker-holding", place?.Key);

        var verdict = ApproachCheck.Judge(place!.Key, place.Approach!, HealthMax, true, t => Her(t, acid: 1.0));
        var answer = CorpseApproachRule.Decide(verdict, 62, 30);
        Assert.False(answer.Walk);
        Assert.Equal(CorpseApproachRule.Unsurvivable, answer.Reason);
        Assert.Contains("it rots 18600 s after the death (max(3600 s, level 62 x 300 s), ACE Corpse.cs:110), 18570 s from now", answer.Why, StringComparison.Ordinal);
        Assert.EndsWith("so the trip gives up now", answer.Why, StringComparison.Ordinal);
        Assert.Equal("approach", DeathLegVerdict.TripAbortApproach);

        Assert.Equal(3600, CorpseApproachRule.RotSeconds(10)); // the hour's floor
        Assert.True(CorpseApproachRule.Decide(ApproachCheck.Judge(place.Key, place.Approach!, HealthMax, true, t => Her(t, 0.35)), 62, 30).Walk);
        Assert.Equal("no-approach", CorpseApproachRule.Decide(null, 62, 30).Reason);
    }

    // ------------------------------------------------------------------------------------------------ the walk's line on the Crater Pathway (live ladder51, 2026-11-17)

    /// <summary>
    /// The walk's watch over 03:47:47.961 to 03:48:30.997 UTC (<c>cache/m6/14/live/ladder51/step3-levelup.log</c>), every Magic and combat line of the window, as seconds after 03:47:00: the Jibrit and Kilif Zefirs' swings (0 or 1 a hit,
    /// Shock Wave I and II for 12 and 15), their resisted spells, and the Dark Wisps' enchantments on her from 03:48:21.594 ("Dark Wisp cast Exhaustion Other II on you") and the first resist the old watch heard at 03:48:30.993.
    /// </summary>
    private static readonly string[] At0348 =
    [
        "47.961|21|Jibrit Zefir scratches your foot for 0 points of slashing damage!", "49.211|21|Critical hit! Jibrit Zefir scratches your head for 1 point of slashing damage!", "51.278|21|Jibrit Zefir scratches your lower leg for 1 point of slashing damage!",
        "51.279|21|You evaded Jibrit Zefir!", "51.795|7|You resist the spell cast by Kilif Zefir", "52.778|21|Jibrit Zefir scratches your chest for 0 points of slashing damage!", "54.144|7|You resist the spell cast by Kilif Zefir",
        "54.378|7|Jibrit Zefir scorches you for 22 points with Flame Bolt I.", "55.044|7|You resist the spell cast by Kilif Zefir", "55.328|7|You resist the spell cast by Kilif Zefir", "55.711|21|Jibrit Zefir scratches your upper arm for 0 points of slashing damage!",
        "58.111|21|Kilif Zefir scratches your chest for 1 point of slashing damage!", "58.778|7|You resist the spell cast by Jibrit Zefir", "59.578|21|Kilif Zefir scratches your lower arm for 0 points of slashing damage!",
        "59.644|21|Jibrit Zefir scratches your hand for 1 point of slashing damage!", "61.344|21|Jibrit Zefir scratches your lower arm for 0 points of slashing damage!", "61.494|21|You evaded Kilif Zefir!",
        "64.377|7|You resist the spell cast by Jibrit Zefir", "65.328|21|Jibrit Zefir scratches your chest for 1 point of slashing damage!", "66.378|21|Jibrit Zefir scratches your upper arm for 1 point of slashing damage!",
        "69.444|7|You resist the spell cast by Jibrit Zefir", "69.561|7|You lose 0 points of stamina due to Kilif Zefir casting Drain Stamina Other II on you", "70.711|21|Jibrit Zefir scratches your upper leg for 1 point of slashing damage!",
        "72.911|7|Kilif Zefir cast Frailty Other II on you", "73.011|21|Jibrit Zefir scratches your lower leg for 0 points of slashing damage!", "76.460|7|Jibrit Zefir grazes you for 12 points with Shock Wave I.",
        "80.260|21|Kilif Zefir scratches your foot for 1 point of slashing damage!", "81.293|21|Kilif Zefir scratches your chest for 0 points of slashing damage!", "81.594|7|Dark Wisp cast Exhaustion Other II on you",
        "81.927|21|Kilif Zefir scratches your chest for 0 points of slashing damage!", "83.394|21|Kilif Zefir scratches your abdomen for 1 point of slashing damage!", "84.093|7|You resist the spell cast by Kilif Zefir",
        "84.760|7|Dark Wisp cast Mana Depletion Other II on you", "86.460|7|You resist the spell cast by Kilif Zefir", "86.710|21|Kilif Zefir scratches your head for 1 point of slashing damage!",
        "86.777|21|Jibrit Zefir scratches your chest for 0 points of slashing damage!", "87.327|21|Kilif Zefir scratches your chest for 0 points of slashing damage!", "87.743|21|Kilif Zefir scratches your chest for 0 points of slashing damage!",
        "87.843|7|Dark Wisp cast Fester Other II on you", "90.510|7|Kilif Zefir grazes you for 15 points with Shock Wave II.", "90.993|7|You resist the spell cast by Dark Wisp",
    ];

    [Fact]
    public void The_0348_walk_line_prices_the_measured_kinds_by_their_hits_and_she_stays_at_142_of_191()
    {
        const uint MaxHealth = 191; // the climber on ladder51
        var rig = new FightManaTests.Rig(mana: 300, stamina: 200);
        using var watch = new AttackWatch(rig.View, _ => { });
        watch.Start();
        var zero = rig.Now - 47.0;
        foreach (var e in At0348)
        {
            var f = e.Split('|');
            var at = double.Parse(f[0], System.Globalization.CultureInfo.InvariantCulture);
            rig.R.Tick(Math.Max(0, zero + at - rig.Now));
            if (f[1] == "7")
            {
                rig.R.H.ServerMessage(f[2], (int)FightTally.MagicChatType);
            }
            else if (f[2].StartsWith("You evaded ", StringComparison.Ordinal))
            {
                rig.R.H.R.Bus.Publish(new CombatEvent(CombatEventKind.DefenseEvaded, f[2]["You evaded ".Length..^1], 0, 0, 0, false, 0, false, 0, f[2]));
            }
            else
            {
                var text = f[2].StartsWith("Critical hit! ", StringComparison.Ordinal) ? f[2]["Critical hit! ".Length..] : f[2];
                var who = text[..text.LastIndexOf(' ', text.IndexOf(" your ", StringComparison.Ordinal) - 1)];
                var points = uint.Parse(text[(text.IndexOf(" for ", StringComparison.Ordinal) + 5)..text.IndexOf(" point", StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture);
                rig.R.H.R.Bus.Publish(new CombatEvent(CombatEventKind.DefenseHit, who, 4, points, 0.05, false, 4, false, 0, f[2]));
            }
        }

        rig.R.Tick(Math.Max(0, zero + 90.997 - rig.Now));
        var now = rig.Now;

        // the kinds as the watch measured them: the Wisps heard 9.4 s (their enchantments from 03:48:21.594), the Kilifs a whole attack long, the Jibrit's attack new at 03:48:26.777 after a quiet of 10.3 s
        var wisp = watch.Measure("Dark Wisp", now)!.Value;
        Assert.Equal((9.403, 4, 0.0), (Math.Round(wisp.Since, 3), wisp.Attacks, wisp.Damage));
        var kilif = watch.Measure("Kilif Zefir", now)!.Value;
        Assert.True(kilif.Heard >= 1, $"{kilif}");
        Assert.Equal((17.0, 15.0), (kilif.Damage, kilif.Biggest)); // 1, 1 and 15 in the last 10 s, the rest 0 points
        var jibrit = watch.Measure("Jibrit Zefir", now)!.Value;
        Assert.Equal((4.22, 1, 0.0), (Math.Round(jibrit.Since, 2), jibrit.Attacks, jibrit.Damage));

        // the five present, priced as the live line read them: each one's timing and spells from the line (the dats' attack loop: a Wisp every 2.99 s, its spells 6.6/s and its largest 78 unprotected; a Kilif every 1.8 s at 0.45 hits/s and
        // its spells 2.41/s; a Jibrit every 1.93 s at 0.4 hits/s and 2.13/s), her factors (a Wisp's cold through armour 0.234 at resistance 1, the Zefirs' pierce through 0.125 at 0.85)
        var wispTiming = new AttackTiming(1988, "Dark Wisp", 2.99, 0, 1, [], "the live line");
        var kilifTiming = new AttackTiming(2610, "Kilif Zefir", 1.8, 0.45 * 1.8, 1, [], "the live line");
        var jibritTiming = new AttackTiming(2608, "Jibrit Zefir", 1.93, 0.4 * 1.93, 1, [], "the live line");
        AttackerRead Wisp(uint guid) => new(guid, 1988, "Dark Wisp", Fact(1988), 0x8, 0.234, 1) { Timing = wispTiming, SpellPerSecond = 6.6, LargestSpellHit = 78 };
        AttackerRead[] old =
        [
            Wisp(0x800008FC), new(0x800008F8, 2610, "Kilif Zefir", Fact(2610), 0x2, 0.125, 0.85) { Timing = kilifTiming, SpellPerSecond = 2.41 }, Wisp(0x800008FA), Wisp(0x800008BF),
            new(0x800008FE, 2608, "Jibrit Zefir", Fact(2608), 0x2, 0.125, 0.85) { Timing = jibritTiming, SpellPerSecond = 2.13 },
        ];
        var live = FightLines.DeriveAttackers(MaxHealth, old, 3.14, 15);
        Assert.Equal(95, live.Percent); // the live read: every attacker at the table's rate, 24.9 a second for 4.45 s and a critical of 78
        Assert.True(142 * 100.0 < live.Percent * MaxHealth); // she retreated

        var measured = old.Select(a => a with { Measure = watch.Measure(a.Name, now), KindPresent = old.Count(o => o.Name == a.Name) }).ToArray();
        var line = FightLines.DeriveAttackers(MaxHealth, measured, 3.14, 15);
        var why = line.Why();
        Assert.Contains("'Kilif Zefir' 0x800008F8", why, StringComparison.Ordinal);
        Assert.Contains("measured over the whole 10 s: its kind's ", why, StringComparison.Ordinal);
        Assert.Contains("17 in hits, largest 15: priced 1.7/s, not the table's", why, StringComparison.Ordinal); // the Kilif: its hits, not 2.41 + its swings
        Assert.Contains("heard 9.4 s of the 10: its kind's 4 attack(s), 0 in hits shared by the 3 of it present, the table's rate for the other 0.6 s: priced 0.39/s, largest 78", why, StringComparison.Ordinal); // each Wisp
        Assert.InRange(line.ModelPerSecond, 4.0, 4.5); // 1.7 + 3 x 0.39 + the Jibrit's new attack, 0.58 of its table's 2.3
        Assert.Equal(78, line.Critical); // one Wisp's largest spell, as the Wisps are not yet measured over the whole window: the way out's margin
        Assert.Equal(55, line.Percent); // 4.2 a second for 4.45 s and the 78: 96.7 of 191 (50.6%), up to the step
        Assert.False(142 * 100.0 < line.Percent * MaxHealth, why); // 142 of 191 (74%) is over the line: she fights on

        // with every kind heard over the whole window, the line is the measured hits' alone: the way out's 4.45 s at the measured 3.14 and the largest hit of 15
        var whole = measured.Select(a => a with { Measure = a.Measure!.Value with { Since = 12 } }).ToArray();
        var settled = FightLines.DeriveAttackers(MaxHealth, whole, 3.14, 15);
        Assert.Equal((15.0, 3.14), (settled.Critical, settled.MeasuredPerSecond));
        Assert.True(settled.ModelPerSecond <= 3.14, settled.Why()); // the hits of the kinds present, 1.7: never over the measure
        Assert.Equal(20, settled.Percent); // 3.14 x 4.45 + 15 = 29 of 191 (15.2%), up to the step
    }

    [Fact]
    public void An_enchantment_cast_on_her_is_timeline_evidence_not_an_attacker_by_itself()
    {
        Assert.True(FightTally.TryCastOnYou("Dark Wisp cast Exhaustion Other II on you", out var caster));
        Assert.Equal("Dark Wisp", caster);
        Assert.True(FightTally.TryCastOnYou("Dark Wisp cast Mana Depletion Other II on you, refreshing Mana Depletion Other II", out _));
        Assert.False(FightTally.TryCastOnYou("You cast Heal Self VI on yourself", out _));
        Assert.False(FightTally.TryCastOnYou("Dark Wisp cast Fester Other II on your Wand", out _));
        Assert.True(FightTally.TryHealthLoss("You lose 7 points of health due to Dark Wisp casting Drain Health Other II on you", out var source, out var amount));
        Assert.Equal(("Dark Wisp", 7L), (source, amount));

        var rig = new FightManaTests.Rig(mana: 300, stamina: 200);
        using var watch = new AttackWatch(rig.View, _ => { });
        watch.Start();
        rig.R.H.ServerMessage("Sho cast Strength Self on you", (int)FightTally.MagicChatType); // a buff from another
        Assert.DoesNotContain("Sho", watch.HeardWithin(10)); // not heard attacking: no attacker by it
        rig.R.H.ServerMessage("Dark Wisp casts Harm Other II and drains 11 points of your health.", (int)FightTally.MagicChatType);
        Assert.Equal((11.0, 11.0), (watch.Measure("Dark Wisp", rig.Now)!.Value.Damage, watch.Measured(rig.Now).BiggestHit)); // health a drain took is a hit
        Assert.Contains("Dark Wisp", watch.HeardWithin(10));
    }

    private static RungAsk Ask(int level, string run, bool leaving)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
        return new RungAsk(level, null, q, static (_, _) => true, new RungBuffRead(static _ => 8, static _ => 4)) { RunGround = run, Leaving = leaving };
    }
}
