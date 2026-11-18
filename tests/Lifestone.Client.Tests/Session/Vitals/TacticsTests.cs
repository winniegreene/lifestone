using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Xunit;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 14 tactics (live 2026-11-17 08:32:59 to 08:33:09 UTC, <c>cache/m6/14/live/ladder56/step3-levelup.log</c>, the Crater Caves mote hunt): the climber, level 80, 219 health, War Magic 378 and Life Magic about 390, against two
/// Cursed Wisps (weenie 7126: 225 health, Magic Defense 285). At 08:33:02.349 her Whirling Blade VI had left 0x800050F6 at 46 when 0x800051C0 harmed her and the self-defence rule took it at full health; at 08:33:08.306, at 73 of
/// 219, the vitals guard rated Heal Self VI 9.4 health a second and Drain Health Other I 20.8 and drained; at 08:33:09.868 the race read lost and she recalled. The numbers here are the log's, ACE's (the world database's spell rows,
/// fight-sim's cast times) and fight-sim's; <c>cache/m6/tactics/wisp-sim.md</c> has the runs.
/// </summary>
public sealed class TacticsTests
{
    private const uint Life = 392; // her Life Magic: fight-sim's saved sheet (the brief's "about 390"); the log's 20.762 for the drain needs its 96.1 % landing
    private const uint WispMagicDefence = 285; // fight-sim: Cursed Wisp, Magic Defense 285
    private const uint DrainOnePower = 38; // fight-sim cast: Drain Health Other I, power 38, base mana 10
    private const uint HealSelfSixPower = 250; // fight-sim cast: Heal Self VI, power 250, base mana 70

    /// <summary>Drain Health Other I (1237) as the world database has it: proportion 0.25, loss -1 (she gets twice the take), cap 60.</summary>
    private static readonly TransferRow DrainOne = new(1237, 2, 2, 0.25f, -1f, 60);

    private static double DrainChance => GearCasts.Chance(Life, DrainOnePower) * DrainPicker.PassChance(Life, WispMagicDefence);

    private static HealCandidate DrainOn(uint targetHealth, uint missing, double seconds)
    {
        BuffStatMods.Transfer(in DrainOne, targetHealth, missing, out var taken, out var given);
        return new HealCandidate("Drain Health Other I", 0x800051C0, given, taken, DrainChance, 10, seconds);
    }

    private static HealCandidate HealSelfSix(double seconds) => new("Heal Self VI", 0, MeasuredHeals.SelfSeed(6), 0, GearCasts.Chance(Life, HealSelfSixPower), 70, seconds);

    // ------------------------------------------------------------------------------------------------ the heal pick

    [Fact]
    public void The_08_33_08_drain_is_ACEs_transfer_48_for_24_off_a_94_health_wisp_and_its_landing_is_fight_sims_96_percent()
    {
        var drain = DrainOn(94, 219 - 73, MeasuredHeals.DrainSeed(1));
        Assert.Equal((48.0, 24.0), (drain.Heal, drain.Damage)); // the log: "gave 48 (the formula said 48): it took 24 from it"
        Assert.InRange(DrainChance, 0.960, 0.962); // fight-sim: her Life Magic lands on a Cursed Wisp 96.1 % of the time

        BuffStatMods.Transfer(in DrainOne, 225, 146, out var fullTaken, out var fullGiven);
        Assert.Equal((30u, 60u), (fullTaken, fullGiven)); // fight-sim's recovery table on a fresh wisp: +60 her health, -30 its
    }

    [Fact]
    public void The_old_score_charged_Heal_Self_VI_6_point_3_s_of_mana_time_and_rated_it_9_point_4_against_the_drains_20_point_8()
    {
        // The cause: HealPick.PerSecond charged each cast its ceiling mana at the seed 0.09 s a point (70 for Heal Self VI: 6.3 s; ACE takes 12.2 after her Mana Conversion and she held 379), gave the drain a quarter of its
        // take, and put Heal Self on the drain's level-VI seed of 3.0 s.
        var self = HealSelfSix(MeasuredHeals.DrainSeed(6));
        var drain = DrainOn(94, 146, MeasuredHeals.DrainSeed(1));
        Assert.Equal(9.408, HealPick.PerSecond(in self, FightRates.SeedSecondsPerMana, HealPick.DefaultDamageWeight), 2); // the log: "Heal Self h/s=9.408"
        Assert.Equal(20.762, HealPick.PerSecond(in drain, FightRates.SeedSecondsPerMana, HealPick.DefaultDamageWeight), 1); // the log: "Drain Health Other I h/s=20.762"
    }

    [Fact]
    public void The_pick_now_is_health_a_second_a_landed_cast_gives_and_at_08_33_08_ACE_still_says_the_drain_by_two()
    {
        // Heal Self VI at ACE's own cast (3.26 s): mean 87.5 a cast, 26.8 a second; Drain Health Other I off the 94-health wisp: 96 % of 48 in 1.6 s, 28.8 a second. The drain is the faster heal there; fight-sim agrees (29.8 at its 1.55 s).
        var self = HealSelfSix(MeasuredHeals.SelfSecondsSeedOf(6));
        Assert.Equal(3.26, self.CastSeconds);
        var drains = new[] { DrainOn(94, 146, MeasuredHeals.DrainSeed(1)) };
        var choice = HealPick.ChooseNow(in self, true, drains, out var selfRate, out var best, out var bestRate);
        Assert.Equal(0, choice);
        Assert.Equal(87.5 * self.Chance / 3.26, selfRate, 9);
        Assert.InRange(selfRate, 26.8, 26.9);
        Assert.Equal(DrainChance * 48 / 1.6, bestRate, 9);
        Assert.InRange(bestRate, 28.75, 28.9);
        Assert.Equal(0, best);
    }

    [Fact]
    public void One_drain_later_the_wisp_holds_70_and_Heal_Self_VI_is_the_pick()
    {
        // The drain took 24: the next one off a 70-health wisp takes 18 (ACE rounds 17.5 to even) and gives 36, 21.6 a second, under Heal Self VI's 26.8 (and its measured 3.17 s live makes it 27.6).
        var self = HealSelfSix(MeasuredHeals.SelfSecondsSeedOf(6));
        var drains = new[] { DrainOn(70, 219 - 85, MeasuredHeals.DrainSeed(1)) };
        Assert.Equal((36.0, 18.0), (drains[0].Heal, drains[0].Damage));
        Assert.Equal(HealPick.HealSelf, HealPick.ChooseNow(in self, true, drains, out var selfRate, out _, out var bestRate));
        Assert.True(selfRate > bestRate);

        var measured = HealSelfSix(3.17);
        Assert.Equal(HealPick.HealSelf, HealPick.ChooseNow(in measured, true, drains, out var measuredRate, out _, out _));
        Assert.InRange(measuredRate, 27.5, 27.7);
    }

    [Fact]
    public void The_mana_she_holds_and_the_drains_damage_no_longer_move_the_pick_and_a_tie_is_Heal_Self()
    {
        var self = HealSelfSix(3.0) with { Chance = 1.0 };
        var dear = self with { Mana = 700 };
        var drains = new[] { new HealCandidate("Drain Health Other I", 1, 87.5, 500, 1.0, 0, 3.0) }; // as much health in as long, and a big take: a tie
        Assert.Equal(HealPick.HealSelf, HealPick.ChooseNow(in dear, true, drains, out var selfRate, out _, out var bestRate));
        Assert.Equal(selfRate, bestRate);
        Assert.Equal(HealPick.Sustained(in self), selfRate);

        Assert.Equal(0, HealPick.ChooseNow(in self, false, drains, out var none, out _, out _)); // no Heal Self paid for: the drain
        Assert.Equal(0, none);
        Assert.Equal(HealPick.Nothing, HealPick.ChooseNow(in self, false, [], out _, out _, out _));
    }

    [Fact]
    public void Heal_Self_takes_ACEs_cast_seconds_for_its_level_until_its_own_are_measured_not_the_drains_seed()
    {
        Assert.Equal([1.48, 2.02, 2.49, 2.92, 3.32, 3.26, 3.6], MeasuredHeals.SelfSecondsSeed); // fight-sim cast on her, 2026-11-17 (VII not in her book: the drain's seed)
        Assert.Equal(3.26, MeasuredHeals.SelfSecondsSeedOf(6));
        Assert.Equal(1.48, MeasuredHeals.SelfSecondsSeedOf(0)); // clamped
        Assert.NotEqual(MeasuredHeals.DrainSeed(6), MeasuredHeals.SelfSecondsSeedOf(6));

        var heals = new MeasuredHeals();
        Assert.Equal(3.26, heals.Seconds("Heal Self VI", MeasuredHeals.SelfSecondsSeedOf(6)));
        heals.NoteSeconds("Heal Self VI", 3.17); // live 08:33:23.265: "landed after 1 tries (Heal Self VI, level 6, 3.17 s"
        Assert.Equal(3.17, heals.Seconds("Heal Self VI", MeasuredHeals.SelfSecondsSeedOf(6)));
    }

    // ------------------------------------------------------------------------------------------------ the per-kind rate both reads share

    [Fact]
    public void The_kinds_rate_is_the_races_own_read_whole_new_and_unread()
    {
        // 08:33:09.868: "'Cursed Wisp' 17/s (5 hit(s) for 170 over 10 s, measured over the whole 10 s: its 6 attack(s) since 11.2 s"
        Assert.True(KindIncoming.Whole(11.2, 6, out var isNew));
        Assert.False(isNew);
        Assert.Equal(17.0, KindIncoming.Rate(170, 11.2, 6, 99, true, out var blend), 9);
        Assert.Equal(0, blend);

        // ladder52 04:54:27.874: a golem's first 42 adds 4.2 a second to the golems' derived 9.9
        Assert.Equal(14.1, KindIncoming.Rate(42, 0, 1, 9.9, true, out blend), 9);
        Assert.Equal(14.1, blend, 9);

        // no derived rate read: its hits over its span, never under the new read's 4 s
        Assert.Equal(42 / KindIncoming.NewKindSeconds, KindIncoming.Rate(42, 0, 1, 0, false, out _), 9);
    }

    // ------------------------------------------------------------------------------------------------ the target switch

    [Fact]
    public void At_08_33_02_she_finishes_the_wisp_at_46_before_the_fresh_one_that_harmed_her()
    {
        // The kind at 08:33:02: Harm Other IV for 18 at 08:33:00.164, a drain for 0 at 08:32:58.647 and an Exhaustion at 08:33:01.831 (3 attacks over 3.7 s, new); the derived 6.7 a second of the 2 on her
        // (the guard's own line at 08:33:08.306): the kind reads 6.7, 3.35 a wisp. Her damage a second on either, the kill side's 34.8 (same kind, same bolt).
        var kind = KindIncoming.Rate(18, 3.7, 3, 6.7, true, out _);
        Assert.Equal(6.7, kind, 9);
        var current = new SwitchSide(0x800050F6, "Cursed Wisp", 46, 34.8, kind / 2, "kind");
        var attacker = new SwitchSide(0x800051C0, "Cursed Wisp", 225, 34.8, kind / 2, "kind");

        var answer = TargetSwitch.Decide(in current, in attacker);

        Assert.True(answer.Keep);
        Assert.Equal((46 / 34.8 * 6.7) + (225 / 34.8 * 3.35), answer.TakenKeeping, 9); // about 30.5
        Assert.Equal((225 / 34.8 * 6.7) + (46 / 34.8 * 3.35), answer.TakenSwitching, 9); // about 47.7
        Assert.StartsWith("target switch: keep 'Cursed Wisp' 0x800050F6 (46 left, her 34.8/s on it: dies in 1.3 s; 3.4/s on her: kind) over 'Cursed Wisp' 0x800051C0 (225 left", answer.Line, StringComparison.Ordinal);
        Assert.Contains("she loses 30.5 finishing 0x800050F6 first against 47.7 taking 0x800051C0 first", answer.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_caster_bolting_her_is_taken_over_a_fresh_melee_golem()
    {
        // fight-sim: a Nubilous Golem (275 health) dies to her Lightning Bolt VI in 7.7 s and takes 0.81 a second off her; the Cursed Wisp (225) dies in 7.1 s, and the live wisps' bolts measured 4 hits for 134 over 10 s.
        var golem = new SwitchSide(0x80000001, "Nubilous Golem", 275, 275 / 7.7, 0.81, "derived");
        var wisp = new SwitchSide(0x800051C0, "Cursed Wisp", 225, 225 / 7.1, 13.4, "measured");
        var answer = TargetSwitch.Decide(in golem, in wisp);
        Assert.False(answer.Keep);
        Assert.True(answer.TakenSwitching < answer.TakenKeeping);
        Assert.StartsWith("target switch: switch to 'Cursed Wisp' 0x800051C0", answer.Line, StringComparison.Ordinal);
        Assert.Contains("the attacker is the bigger threat for the seconds it takes to kill", answer.Line, StringComparison.Ordinal);

        // and the golem one bolt from death is still finished first only when its threat over its seconds says so: at 10 health (0.3 s) it is
        var dying = golem with { Health = 10 };
        Assert.True(TargetSwitch.Decide(in dying, in wisp).Keep);
    }

    [Fact]
    public void Unknown_kill_seconds_never_hide_behind_the_rule_and_a_tie_keeps()
    {
        var current = new SwitchSide(1, "A", 100, 30, 5, "x");
        var attacker = new SwitchSide(2, "B", 100, 30, 5, "x");
        Assert.True(TargetSwitch.Decide(in current, in attacker).Keep); // the same in every way: no turn

        var untouchable = current with { HerDamagePerSecond = 0 };
        var switched = TargetSwitch.Decide(in untouchable, in attacker);
        Assert.False(switched.Keep);
        Assert.Contains("her bolts do nothing to the current target", switched.Line, StringComparison.Ordinal);

        var kept = TargetSwitch.Decide(in current, attacker with { HerDamagePerSecond = 0 });
        Assert.True(kept.Keep);
        Assert.Contains("her bolts do nothing to the attacker", kept.Line, StringComparison.Ordinal);
    }
}
