using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Mage;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// <see cref="FightDebuffs"/> (M5 item 7 F11, D90): the value of the debuffs and drains in all-in seconds. The target is the den's Tusker Slave as the world SQL has it (wcid 1628: Strength 210, Endurance 300, Quickness 180,
/// Coordination 200, Focus 70, Self 60, Light Weapons 150 + 137 = 287, Magic Defense 200 + 19 = 219, health 400, stamina 300, PowerupTime 2.5); the mage's numbers are illustrative level-80 values (War 361, Life 351, Creature 351, Mana
/// Conversion 258, the dance 0.09 s a mana point, the heal 0.04 s a health point), and the swing length and damage rate are stand-ins the fight measures live.
/// </summary>
public sealed class FightDebuffsTests(ITestOutputHelper output)
{
    private static DebuffTarget Slave(double health = 400, double incoming = 25) => new()
    {
        RemainingHealth = health,
        MaxHealth = 400,
        Stamina = 300,
        Strength = 210,
        Endurance = 300,
        Quickness = 180,
        Coordination = 200,
        Focus = 70,
        Self = 60,
        MagicDefense = 219,
        AttackSkill = 287,
        IncomingDamagePerSecond = incoming,
        SwingSeconds = 1.2,
        PowerupSeconds = 2.5,
    };

    private static DebuffCaster Mage(double manaConversion = 258, double secondsPerMana = 0.09) => new()
    {
        CreatureSkill = 351,
        LifeSkill = 351,
        WarSkill = 361,
        ManaConversion = manaConversion,
        MeleeDefense = 100,
        MissingHealth = 200,
        MissingStamina = 100,
        SecondsPerMana = secondsPerMana,
        SecondsPerHealth = 0.04,
        SecondsPerStamina = 0.05,
        DebuffCastSeconds = 2.7,
        WarMeanDamage = 146,
        WarCastSeconds = 2.9,
        WarBaseMana = 30,
        WarPower = 250,
    };

    private static DebuffSpell Spell(DebuffKind kind, int level = 6) => FightDebuffs.Find(kind, level)!.Value;

    [Fact]
    public void The_table_has_a_level_VI_and_a_level_VII_of_every_kind_with_the_ids_once()
    {
        var kinds = Enum.GetValues<DebuffKind>();
        Assert.Equal(kinds.Length * 2, FightDebuffs.Spells.Count);
        Assert.Equal(FightDebuffs.Spells.Count, FightDebuffs.Spells.Select(s => s.Id).Distinct().Count());
        foreach (var kind in kinds)
        {
            Assert.Equal(1, FightDebuffs.Spells.Count(s => s.Kind == kind && s.Level == 6));
            Assert.Equal(1, FightDebuffs.Spells.Count(s => s.Kind == kind && s.Level == 7));
        }

        Assert.Null(FightDebuffs.Find(DebuffKind.Weakness, 5));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public void Imperil_has_no_value_against_war_damage_and_says_why(int level)
    {
        var value = FightDebuffs.Evaluate(Spell(DebuffKind.Imperil, level), Slave(), Mage());
        Assert.False(value.HasValue);
        Assert.False(value.Pays);
        Assert.Contains("SpellProjectile.cs:526-557", value.Why, StringComparison.Ordinal);
        Assert.Equal(0, value.SavedSeconds);
        Assert.True(value.NetSeconds < 0);
        Assert.Same(value.Why, FightDebuffs.Evaluate(Spell(DebuffKind.Imperil, level), Slave(900), Mage()).Why); // one constant string, so the log can say it once
    }

    [Theory]
    [InlineData(DebuffKind.Vulnerability)]
    [InlineData(DebuffKind.Defenselessness)]
    [InlineData(DebuffKind.Exhaustion)]
    public void The_defences_against_our_melee_and_the_stamina_regeneration_have_no_value_in_this_fight(DebuffKind kind)
    {
        var value = FightDebuffs.Evaluate(Spell(kind), Slave(), Mage());
        Assert.False(value.HasValue);
        Assert.NotEmpty(value.Why);
    }

    [Fact]
    public void A_strength_debuff_on_a_hard_hitting_tusker_saves_heal_seconds_in_proportion_to_its_damage()
    {
        var weakness = Spell(DebuffKind.Weakness);
        var none = FightDebuffs.Evaluate(weakness, Slave(incoming: 0), Mage());
        var soft = FightDebuffs.Evaluate(weakness, Slave(incoming: 25), Mage());
        var hard = FightDebuffs.Evaluate(weakness, Slave(incoming: 100), Mage());
        output.WriteLine($"Weakness VI on a Slave: saved {none.SavedSeconds:F2} / {soft.SavedSeconds:F2} / {hard.SavedSeconds:F2} s at 0 / 25 / 100 damage a second, cost {soft.CostSeconds:F2} / {hard.CostSeconds:F2}");
        Assert.True(none.HasValue);
        Assert.Equal(0, none.SavedSeconds, 9);
        Assert.True(soft.SavedSeconds > 0);
        Assert.Equal(4.0, hard.SavedSeconds / soft.SavedSeconds, 6); // linear in the damage taken
        Assert.InRange(soft.Landing, 0.97, 0.999);

        // 210 -> 175 Strength: the attribute modifier 2.705 -> 2.32, and the attack skill 287 -> 275 against a Melee Defense of 100 (a hit either way).
        var attribute = FightDebuffs.AttributeMod(175) / FightDebuffs.AttributeMod(210);
        Assert.Equal(2.705, FightDebuffs.AttributeMod(210), 3);
        Assert.Equal(0.8576, attribute, 3);
        var factor = attribute * FightDebuffs.Chance(275, 100) / FightDebuffs.Chance(287, 100);
        var mage = Mage();
        var before = FightDebuffs.FightSeconds(mage, Slave(), 400, FightDebuffs.Chance(mage.WarSkill, 219), 1.0, 1.0);
        var after = FightDebuffs.FightSeconds(mage, Slave(), 400, FightDebuffs.Chance(mage.WarSkill, 219), 1.0, factor);
        Assert.Equal(soft.Landing * (before - after), soft.SavedSeconds, 2);
    }

    [Fact]
    public void A_strength_debuff_pays_only_in_a_long_fight_because_its_cast_is_paid_under_the_same_blows()
    {
        var weakness = Spell(DebuffKind.Weakness);
        var short400 = FightDebuffs.Evaluate(weakness, Slave(400, 50), Mage());
        var long4000 = FightDebuffs.Evaluate(weakness, Slave(4000, 50), Mage());
        output.WriteLine($"Weakness VI at 50 a second: net {short400.NetSeconds:F2} s on 400 health, {long4000.NetSeconds:F2} s on 4000");
        Assert.False(short400.Pays);
        Assert.True(long4000.Pays);
    }

    [Fact]
    public void Slowness_is_scored_only_when_the_swing_length_is_known()
    {
        var slowness = Spell(DebuffKind.Slowness);
        var unknown = FightDebuffs.Evaluate(slowness, Slave() with { SwingSeconds = 0 }, Mage());
        Assert.False(unknown.HasValue);
        Assert.StartsWith("TODO(f11-swing-seconds)", unknown.Why, StringComparison.Ordinal);

        // Quickness 180 -> animation speed 1.5; 145 -> 1.277: the swing 0.8 s -> 0.94 s, an attack every 2.05 s -> 2.19 s.
        Assert.Equal(1.5, FightDebuffs.AnimSpeed(180), 3);
        Assert.Equal(1.277, FightDebuffs.AnimSpeed(145), 3);
        Assert.Equal(2.0, FightDebuffs.AnimSpeed(250), 6); // the ceiling
        var known = FightDebuffs.Evaluate(slowness, Slave(), Mage());
        Assert.True(known.SavedSeconds > 0);
    }

    [Fact]
    public void Magic_Yield_and_the_Focus_and_Self_debuffs_raise_our_landing_chance_by_a_few_hundredths_and_do_not_pay()
    {
        var mage = Mage();
        foreach (var kind in new[] { DebuffKind.MagicYield, DebuffKind.Bafflement, DebuffKind.Feeblemind })
        {
            var value = FightDebuffs.Evaluate(Spell(kind), Slave(), mage);
            output.WriteLine($"{kind} VI: saved {value.SavedSeconds:F3} s for {value.CostSeconds:F2} s");
            Assert.True(value.HasValue);
            Assert.True(value.SavedSeconds > 0);
            Assert.False(value.Pays);
        }

        // Magic Defense 219 -> 184 with Magic Yield; Focus 70 -> 35 gives (35 + 60) / 7 = 14 against 19, so 214.
        var yield = FightDebuffs.Chance(361, 184) - FightDebuffs.Chance(361, 219);
        Assert.InRange(yield, 0.005, 0.02);
    }

    [Fact]
    public void Fire_Vulnerability_value_grows_with_the_targets_remaining_health()
    {
        var vulnerability = Spell(DebuffKind.FireVulnerability);
        var saved = new[] { 100.0, 200.0, 400.0, 800.0, 1600.0 }
            .Select(h => FightDebuffs.Evaluate(vulnerability, Slave(h), Mage()).SavedSeconds)
            .ToArray();
        output.WriteLine("Fire Vulnerability VI saved seconds at 100 / 200 / 400 / 800 / 1600 health: " + string.Join(" / ", saved.Select(s => s.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))));
        for (var i = 1; i < saved.Length; i++)
        {
            Assert.True(saved[i] > saved[i - 1], $"step {i}");
        }

        // Against a target a bolt kills anyway it loses by its whole cost; against a long fight it pays.
        Assert.False(FightDebuffs.Evaluate(vulnerability, Slave(60), Mage()).Pays);
        Assert.True(FightDebuffs.Evaluate(vulnerability, Slave(1600), Mage()).Pays);
        Assert.Equal(2.5, vulnerability.Magnitude);
        Assert.Equal(2.85, Spell(DebuffKind.FireVulnerability, 7).Magnitude);
    }

    [Fact]
    public void Fire_Vulnerability_already_on_the_target_is_not_cast_again_and_a_stronger_one_is_worth_the_ratio()
    {
        var six = Spell(DebuffKind.FireVulnerability);
        var seven = Spell(DebuffKind.FireVulnerability, 7);
        var on = Slave() with { FireVulnerabilityMultiplier = 2.5 };
        var again = FightDebuffs.Evaluate(six, on, Mage());
        Assert.False(again.HasValue);
        Assert.Contains("already", again.Why, StringComparison.Ordinal);
        var upgrade = FightDebuffs.Evaluate(seven, on, Mage());
        Assert.True(upgrade.HasValue);
        Assert.True(upgrade.SavedSeconds < FightDebuffs.Evaluate(seven, Slave(), Mage()).SavedSeconds);
    }

    [Fact]
    public void The_cost_side_follows_Mana_Conversion_and_the_dances_rate()
    {
        var vulnerability = Spell(DebuffKind.FireVulnerability);
        var target = Slave();
        var high = FightDebuffs.Evaluate(vulnerability, target, Mage(manaConversion: 311));
        var mid = FightDebuffs.Evaluate(vulnerability, target, Mage(manaConversion: 258));
        var low = FightDebuffs.Evaluate(vulnerability, target, Mage(manaConversion: 220));
        output.WriteLine($"Fire Vulnerability VI mana at Mana Conversion 311 / 258 / 220: {high.ManaCost:F2} / {mid.ManaCost:F2} / {low.ManaCost:F2}");
        Assert.Equal(ManaConversionModel.ExpectedCost(60, 275, 258), mid.ManaCost, 9);
        Assert.True(high.ManaCost < mid.ManaCost && mid.ManaCost < low.ManaCost);
        Assert.True(high.CostSeconds < mid.CostSeconds && mid.CostSeconds < low.CostSeconds);

        // The dance's rate scales only the mana part of the cost.
        var slow = FightDebuffs.Evaluate(vulnerability, target, Mage(secondsPerMana: 0.18));
        var fast = FightDebuffs.Evaluate(vulnerability, target, Mage(secondsPerMana: 0.09));
        Assert.Equal(fast.ManaCost * 0.09, slow.CostSeconds - fast.CostSeconds, 9);

        // Untrained Mana Conversion pays the base.
        Assert.Equal(60, FightDebuffs.Evaluate(vulnerability, target, Mage(manaConversion: 0)).ManaCost);
    }

    [Fact]
    public void Frailty_takes_only_the_healths_excess_over_the_new_maximum_and_does_not_pay()
    {
        var frailty = Spell(DebuffKind.Frailty);
        var full = FightDebuffs.Evaluate(frailty, Slave(400), Mage());
        var hurt = FightDebuffs.Evaluate(frailty, Slave(300), Mage());
        Assert.True(full.HasValue);
        Assert.False(full.Pays);
        Assert.Equal(0, hurt.SavedSeconds, 9); // 300 is under the new maximum 400 - 17.5: nothing to clamp
    }

    [Fact]
    public void Harm_Other_and_Drain_Health_Other_are_attacks_with_a_damage_a_heal_and_a_landing_chance()
    {
        var harm = FightDebuffs.Evaluate(Spell(DebuffKind.HarmOther), Slave(), Mage());
        Assert.True(harm.HasValue);
        Assert.Equal(harm.Landing * 48, harm.ExpectedDamage, 6); // an integer from -63 to -33, mean 48
        Assert.Equal(harm.Landing * 60.5, FightDebuffs.Evaluate(Spell(DebuffKind.HarmOther, 7), Slave(), Mage()).ExpectedDamage, 6);

        var drain = FightDebuffs.Evaluate(Spell(DebuffKind.DrainHealthOther), Slave(), Mage());
        Assert.True(drain.HasValue);
        Assert.Equal(drain.Landing * 150, drain.ExpectedDamage, 6); // 0.4 x 400 = 160, capped at 150
        Assert.Equal(drain.Landing * 112, drain.ExpectedHeal, 6); // 150 x (1 - 0.25) = 112.5, rounded to even
        Assert.True(drain.SavedSeconds > harm.SavedSeconds);
        output.WriteLine($"Harm VI: {harm.ExpectedDamage:F1} damage, net {harm.NetSeconds:F2} s; Drain Health VI: {drain.ExpectedDamage:F1} damage, {drain.ExpectedHeal:F1} heal, net {drain.NetSeconds:F2} s");
    }

    [Fact]
    public void A_drain_is_scaled_to_the_health_the_mage_can_still_take_and_does_nothing_at_full_health()
    {
        var spell = Spell(DebuffKind.DrainHealthOther);
        var roomy = FightDebuffs.Evaluate(spell, Slave(), Mage());
        var cramped = FightDebuffs.Evaluate(spell, Slave(), Mage() with { MissingHealth = 56 });
        Assert.Equal(roomy.Landing * 56, cramped.ExpectedHeal, 6);
        Assert.Equal(roomy.Landing * 75, cramped.ExpectedDamage, 6); // 150 x 56 / 112
        var full = FightDebuffs.Evaluate(spell, Slave(), Mage() with { MissingHealth = 0 });
        Assert.False(full.HasValue);
        Assert.Contains("WorldObject_Magic.cs:735-746", full.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Drain_Stamina_Other_gives_the_mage_stamina_at_the_stamina_rate()
    {
        var value = FightDebuffs.Evaluate(Spell(DebuffKind.DrainStaminaOther), Slave(), Mage());
        Assert.Equal(value.Landing * 90, value.ExpectedStamina, 6); // 300 x 0.4 = 120 drained, 120 x 0.75 = 90 given, under the 100 the mage can still take
        Assert.Equal(value.Landing * 90 * 0.05, value.SavedSeconds, 6);
        Assert.Equal(0, value.ExpectedDamage);
    }

    [Fact]
    public void A_baseline_without_damage_is_a_loud_todo_not_a_guess()
    {
        var value = FightDebuffs.Evaluate(Spell(DebuffKind.FireVulnerability), Slave(), Mage() with { WarMeanDamage = 0 });
        Assert.False(value.HasValue);
        Assert.StartsWith("TODO(f11-baseline)", value.Why, StringComparison.Ordinal);
    }
    [Fact]
    public void Every_spell_is_valued_against_the_Slave_and_the_table_is_printed_for_the_doc_notes()
    {
        foreach (var spell in FightDebuffs.Spells)
        {
            var v = FightDebuffs.Evaluate(spell, Slave(), Mage());
            output.WriteLine($"{spell.Name,-36} L{spell.Level} id {spell.Id,4} land {v.Landing:P1} saved {v.SavedSeconds,6:F2} s cost {v.CostSeconds,5:F2} s (mana {v.ManaCost:F1}) net {v.NetSeconds,6:F2} s dmg {v.ExpectedDamage:F1} heal {v.ExpectedHeal:F1} sta {v.ExpectedStamina:F1} {(v.HasValue ? string.Empty : "NO VALUE: " + v.Why)}");
            Assert.InRange(v.Landing, 0.9, 1.0);
        }
    }
}
