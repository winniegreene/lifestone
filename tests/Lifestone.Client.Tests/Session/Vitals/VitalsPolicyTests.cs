using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 14 (<c>m6-vitals-policy</c>): the climber's fills by the numbers. What these prove, on the live numbers of 2026-11-13 in the Tusker Abode at level 40 (<c>cache/m6/14/live/abode2</c>: War Magic and Life Magic 302, Mana Conversion 213,
/// health 156, stamina 221, mana 332, Flame Bolt V, 99 kills in 34.1 minutes, 818 attacks thrown at her, 2,274 damage, the biggest hit 10): the regeneration is ACE's and meets the live rest; the bolts a kill meet the live count; the 28
/// presses at mana 320 to 325 are not made, and the stamina drain she never cast is; a press waits until its whole give fits and her stamina is ready; the floors fill at once; a bolt that likely kills goes first; the health drain is the
/// one with the fewest seconds a point; a vital that regeneration covers is never filled; the pull's lines; the ladder's section.
/// </summary>
public sealed class VitalsPolicyTests
{
    private static readonly LadderVitalsPolicy Rules = LadderLoader.WarMage.VitalsPolicy;

    // Stamina to Mana Self I to V (spell rows 1676-1680; base mana and power the SpellTable's).
    private static readonly FillLevel[] Presses =
    [
        new(0, 1, 10, 1, Rules.SelfSeconds(1), BuffStatMods.StaminaToMana[0]),
        new(1, 2, 20, 50, Rules.SelfSeconds(2), BuffStatMods.StaminaToMana[1]),
        new(2, 3, 30, 100, Rules.SelfSeconds(3), BuffStatMods.StaminaToMana[2]),
        new(3, 4, 40, 150, Rules.SelfSeconds(4), BuffStatMods.StaminaToMana[3]),
        new(4, 5, 50, 200, Rules.SelfSeconds(5), BuffStatMods.StaminaToMana[4]),
    ];

    // Drain Health Other I to IV (spell rows 1237-1240: proportion 0.25, loss -1, -0.6, -0.33, 0, cap 60, 80, 100, 100).
    private static readonly FillLevel[] HealthDrains =
    [
        new(0, 1, 10, 38, Rules.OtherSeconds(1), new TransferRow(1237, 2, 2, 0.25f, -1f, 60)),
        new(1, 2, 20, 88, Rules.OtherSeconds(2), new TransferRow(1238, 2, 2, 0.25f, -0.6f, 80)),
        new(2, 3, 30, 138, Rules.OtherSeconds(3), new TransferRow(1239, 2, 2, 0.25f, -0.33f, 100)),
        new(3, 4, 40, 188, Rules.OtherSeconds(4), new TransferRow(1240, 2, 2, 0.25f, 0f, 100)),
    ];

    // Drain Stamina Other I (spell 1249: proportion 0.1, loss -1, cap 60).
    private static readonly FillLevel[] StaminaDrains = [new(0, 1, 10, 38, Rules.OtherSeconds(1), new TransferRow(1249, 4, 4, 0.1f, -1f, 60))];

    /// <summary>Level 40 in the Abode, as the live run read her; the caller sets the bars.</summary>
    private static VitalsPolicyFacts Abode(uint health = 156, uint stamina = 221, uint mana = 332, double target = 250, double burst = 0) => new()
    {
        Health = health,
        HealthMax = 156,
        Stamina = stamina,
        StaminaMax = 221,
        Mana = mana,
        ManaMax = 332,
        ManaConversion = 213,
        HealthRegen = VitalsPolicy.Regen(VitalsPolicy.HealthAtOne, VitalsPolicy.CombatStance, 2.15, 72, 94), // Hydra's Head, base Strength 72 and Endurance 94 (the shard)
        StaminaRegen = VitalsPolicy.Regen(VitalsPolicy.StaminaAtOne, VitalsPolicy.CombatStance, 2.15), // Tenaciousness
        ManaRegen = VitalsPolicy.Regen(VitalsPolicy.ManaAtOne, 1, 2.15), // Battlemage's Boon
        SecondsPerKill = 34.1 * 60 / 98, // 99 kills, 98 gaps
        AttacksPerSecond = 818 / 2046.0,
        DamagePerSecond = 2274 / 2046.0,
        BurstPerSecond = burst,
        BiggestHit = 10,
        BoltLevel = 5,
        BoltBaseMana = 25,
        BoltPower = 200,
        BoltSeconds = Rules.BoltSeconds(5),
        BoltMeanDamage = ((68 + 136) / 2.0) + (68 * (302 - 200) / 1000.0) + (VitalsPolicy.CritChance * 0.5 * 136), // SpellProjectile.cs:513-537: the roll, the skill bonus, the critical
        BoltSpread = 68,
        BoltLandChance = GearCasts.Chance(302, 200) * ManaConversionModel.SkillChance(302u, 171u),
        KillHealth = 250,
        KillStamina = 480,
        TargetHealth = target,
        FullestHealth = 250,
        FullestStamina = 480,
        EmergencyLine = 94,
        Presses = Presses,
        StaminaDrains = StaminaDrains,
        HealthDrains = HealthDrains,
    };

    [Fact]
    public void Her_regeneration_is_ACEs_rows_times_her_buffs_and_meets_the_live_rest()
    {
        // Creature_Vitals.cs:102-236: rate x attribute x stance x enchantment a 5 s heartbeat. Strength 72 + 2 x Endurance 94 = 260: the attribute factor 1.1.
        var lyingHealth = VitalsPolicy.Regen(VitalsPolicy.HealthAtOne, 3.0, 2.15, 72, 94);
        Assert.Equal(0.4257, lyingHealth, 4);
        Assert.InRange(54 / 121.0 / lyingHealth, 0.95, 1.06); // the live rest of 04:18:10-04:20:11 UTC: health 102 -> 156 in 121 s lying down
        Assert.Equal(0.43, VitalsPolicy.Regen(VitalsPolicy.ManaAtOne, 1, 2.15), 4); // no stance moves mana; the rest read at least 0.37 (it reached the top before the health did)
        Assert.Equal(0.645, VitalsPolicy.Regen(VitalsPolicy.StaminaAtOne, VitalsPolicy.CombatStance, 2.15), 4);
        Assert.Equal(VitalsPolicy.HealthAtOne, VitalsPolicy.Regen(VitalsPolicy.HealthAtOne, 1, 1, 10, 50), 6); // under 200 the attribute factor is 1
        Assert.Equal(2.1 * VitalsPolicy.HealthAtOne, VitalsPolicy.Regen(VitalsPolicy.HealthAtOne, 1, 1, 500, 500), 6); // capped at 2.1
    }

    [Fact]
    public void The_bolts_a_kill_meet_the_live_count_in_the_Abode()
    {
        var f = Abode();
        var bolts = VitalsPolicy.BoltsToKill(in f, 250);
        Assert.InRange(bolts, 2.6, 2.95); // live: 257 Flame Bolt V for 99 kills (2.6, a few drained); the renewal sum on ACE's uniform roll 2.8
        Assert.Equal(0, VitalsPolicy.BoltsToKill(in f, 0));
        Assert.Equal(1, VitalsPolicy.BoltsToKill(in f, 5)); // at least one bolt
    }

    [Fact]
    public void The_next_bolt_kills_by_the_share_of_its_roll_over_the_targets_health()
    {
        var f = Abode();
        Assert.True(VitalsPolicy.NextBoltKills(in f, 60) > 0.95); // under the roll's low end: the land chance
        Assert.Equal(0, VitalsPolicy.NextBoltKills(in f, 250));
        var mid = VitalsPolicy.NextBoltKills(in f, f.BoltMeanDamage);
        Assert.InRange(mid, 0.45, 0.5); // the mean of a uniform: half its roll, times the land chance
        Assert.Equal(0, VitalsPolicy.NextBoltKills(in f, -1)); // not read
    }

    [Fact]
    public void The_live_presses_at_mana_320_to_325_are_not_made_and_the_stamina_drain_is()
    {
        // Live 04:09-04:30 UTC: 28 presses at mana 320 to 325 of 332, stamina 84 to 102 of 221, one drain. The budget: a kill costs her more mana than she regenerates over it, and the attacks and the presses more stamina.
        foreach (var (mana, stamina) in new (uint, uint)[] { (320, 84), (325, 102), (322, 95) })
        {
            var plan = VitalsPolicy.Decide(Abode(stamina: stamina, mana: mana, health: 150), Rules);
            Assert.True(plan.Budget.ManaShort > 0, $"mana short {plan.Budget.ManaShort}");
            Assert.True(plan.Budget.StaminaShort > 0, $"stamina short {plan.Budget.StaminaShort}");
            Assert.NotEqual(VitalsMove.Press, plan.Choice.Move);
            Assert.Equal(VitalsMove.DrainStamina, plan.Choice.Move); // a Redeemer holds 480: Drain Stamina Other I gives its cap of 60, and 119 to 137 are missing
            Assert.Equal(60, plan.Choice.Give);
        }
    }

    [Fact]
    public void A_press_waits_until_its_whole_give_fits_and_then_the_best_priced_level_goes()
    {
        var full = VitalsPolicy.Decide(Abode(mana: 300), Rules);
        Assert.Equal(VitalsMove.Bolt, full.Choice.Move); // 32 missing: no level's whole give from a full bar fits
        var room = VitalsPolicy.Decide(Abode(mana: 183), Rules);
        Assert.Equal(VitalsMove.Press, room.Choice.Move);
        Assert.Equal(4, room.Choice.Index); // Stamina to Mana Self V: 110 stamina gives 148 (loss -0.35), the fewest seconds a net mana with the stamina at its price
        Assert.True(room.Choice.Give <= 332 - 183);
    }

    [Fact]
    public void With_the_ladders_line_off_the_cheapest_press_that_fits_now_goes_and_the_bars_stay_nearer_full()
    {
        var waits = VitalsPolicy.Decide(Abode(mana: 280), Rules);
        Assert.Equal(VitalsMove.Bolt, waits.Choice.Move); // 52 missing: the cheapest level (V, 148) does not fit yet
        var now = VitalsPolicy.Decide(Abode(mana: 280), Rules with { PressWaitsForBest = false });
        Assert.Equal(VitalsMove.Press, now.Choice.Move);
        Assert.Equal(0, now.Choice.Index); // Stamina to Mana Self I: its whole 45 fits the 52 missing
        Assert.True(Rules.PressWaitsForBest);
    }

    [Fact]
    public void A_press_waits_for_her_stamina_to_be_ready_and_the_stamina_drain_goes_first()
    {
        var low = VitalsPolicy.Decide(Abode(stamina: 120, mana: 150), Rules);
        Assert.True(120 < low.Lines.StaminaReady);
        Assert.Equal(VitalsMove.DrainStamina, low.Choice.Move);
        var ready = VitalsPolicy.Decide(Abode(stamina: 200, mana: 150), Rules);
        Assert.True(200 >= ready.Lines.StaminaReady);
        Assert.Equal(VitalsMove.Press, ready.Choice.Move);
    }

    [Fact]
    public void Under_the_mana_floor_a_press_goes_at_once_whether_or_not_it_fits()
    {
        var plan = VitalsPolicy.Decide(Abode(stamina: 60, mana: 40), Rules);
        Assert.Equal(25u + 40 + 10, plan.Lines.ManaFloor); // a bolt, the dearest drain and the cheapest press at their dearest rolls
        Assert.Equal(VitalsMove.Press, plan.Choice.Move);
        Assert.Equal("under the mana floor", plan.Choice.Why);
    }

    [Fact]
    public void Under_the_health_floor_the_cheapest_health_drain_goes_at_once()
    {
        var plan = VitalsPolicy.Decide(Abode(health: 30, burst: 6.7), Rules);
        Assert.Equal((uint)Math.Ceiling(10 + (6.7 * (Rules.BoltSeconds(5) + Rules.OtherSeconds(4)))), plan.Lines.HealthFloor); // the biggest hit and the recent burst over a bolt and a drain
        Assert.Equal(VitalsMove.DrainHealth, plan.Choice.Move);
        Assert.Equal(2, plan.Choice.Index); // Drain Health Other III: 62 from a fresh Redeemer gives her 82, the damage counted as bolt seconds saved
        Assert.Equal("under the health floor", plan.Choice.Why);
    }

    [Fact]
    public void The_health_drain_goes_when_its_whole_give_fits_and_waits_when_none_fits()
    {
        var fits = VitalsPolicy.Decide(Abode(health: 70), Rules); // 86 missing: III's 82 fits
        Assert.Equal(VitalsMove.DrainHealth, fits.Choice.Move);
        Assert.Equal(2, fits.Choice.Index);
        Assert.Equal(82, fits.Choice.Give);
        var waits = VitalsPolicy.Decide(Abode(health: 100), Rules); // 56 missing: I gives 60, IV 62, III 82: none fits
        Assert.NotEqual(VitalsMove.DrainHealth, waits.Choice.Move);
    }

    [Fact]
    public void A_bolt_that_likely_kills_goes_before_any_fill_but_not_before_a_floor()
    {
        var dying = VitalsPolicy.Decide(Abode(health: 70, stamina: 90, target: 60), Rules);
        Assert.Equal(VitalsMove.Bolt, dying.Choice.Move);
        Assert.Equal("the next bolt likely kills", dying.Choice.Why);
        var floor = VitalsPolicy.Decide(Abode(health: 30, target: 60, burst: 6.7), Rules);
        Assert.Equal(VitalsMove.DrainHealth, floor.Choice.Move);
    }

    [Fact]
    public void A_vital_her_regeneration_covers_is_never_filled_above_its_floor()
    {
        // Slow kills and cheap bolts: the mana she regenerates over a kill covers it, and so does the stamina.
        var f = Abode(mana: 150, stamina: 221) with { SecondsPerKill = 60, AttacksPerSecond = 0.1 };
        var plan = VitalsPolicy.Decide(in f, Rules);
        Assert.True(plan.Budget.ManaShort < 0);
        Assert.True(plan.Budget.StaminaShort < 0);
        Assert.Equal(VitalsMove.Bolt, plan.Choice.Move);
        Assert.Equal(0, plan.Prices.Stamina); // stamina comes back by itself: it costs no seconds
    }

    [Fact]
    public void The_pulls_lines_are_one_drain_of_full_over_the_emergency_and_the_floor_and_a_kills_mana_over_the_floor()
    {
        var plan = VitalsPolicy.Decide(Abode(), Rules);
        Assert.Equal(94u, plan.Lines.PullHealth); // the emergency's line (60% of 156) is over 156 less III's 82
        var noEmergency = VitalsPolicy.Decide(Abode() with { EmergencyLine = 0 }, Rules);
        Assert.Equal(156u - 82, noEmergency.Lines.PullHealth);
        Assert.Equal(plan.Lines.ManaFloor + (uint)Math.Ceiling(plan.Budget.ManaShort), plan.Lines.PullMana);
        Assert.InRange(plan.Lines.PullMana, 75u, 95u); // well under the potion line of 75% (249): the drinks stay the reserve
    }

    [Fact]
    public void The_budget_at_level_40_in_the_Abode_meets_the_live_mana()
    {
        // Live: 1,964 mana in over 99 kills (1,032 by presses, 52 by a potion, 880 regenerated): 19.8 a kill, of which about 2.5 the buff refreshes and 0.8 the presses' own costs.
        var budget = VitalsPolicy.Budget(Abode(), Rules);
        Assert.InRange(budget.ManaAKill, 14.5, 18.5); // the bolts (2.8 x 5.6 after Mana Conversion) and the health drains
        Assert.InRange(budget.ManaRegenAKill, 8.5, 9.5);
        Assert.InRange(budget.HealthAKill, 22, 25); // 2,274 damage over 99 kills
        Assert.True(budget.Measured);
        var seed = VitalsPolicy.Budget(Abode() with { SecondsPerKill = 0 }, Rules);
        Assert.False(seed.Measured);
        Assert.Equal(Rules.SeedOverheadSeconds + (seed.BoltsAKill * Rules.BoltSeconds(5)), seed.SecondsAKill, 6);
    }

    [Fact]
    public void Every_decision_names_every_number_it_read()
    {
        var f = Abode(stamina: 95, mana: 322, health: 150);
        var line = VitalsPolicy.Decide(in f, Rules).Describe(in f);
        foreach (var part in new[] { "vitals policy: drainstamina", "health 150/156", "stamina 95/221", "mana 322/332", "bolts", "short", "floors mana", "pull mana", "prices damage", "next bolt kills", "fullest health 250 stamina 480", "incoming", "regen a second", "Mana Conversion 213" })
        {
            Assert.Contains(part, line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_ladder_carries_the_policys_lines_and_an_absent_section_is_off()
    {
        Assert.True(Rules.On);
        Assert.Equal(0.5, Rules.BoltFirstChance);
        Assert.Equal(1.74, Rules.SelfSeconds(1));
        Assert.Equal(3.53, Rules.BoltSeconds(5));
        Assert.Equal(Rules.SelfSeconds(8), Rules.SelfSeconds(12)); // past the table: its last row
        Assert.Contains("m6-vitals-policy", LadderLoader.WarMage.Sources["vitals_policy"], StringComparison.Ordinal);
        Assert.False(LadderVitalsPolicy.Off.On);
        Assert.Throws<InvalidOperationException>(() => LadderVitalsPolicy.Off.SelfSeconds(1));
    }
}
