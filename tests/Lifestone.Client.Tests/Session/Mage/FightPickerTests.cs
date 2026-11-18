using DatReaderWriter.Enums;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 7 F10 (D90): the fight's one live picker. The situations use F11's illustrative level-80 mage (War 361, Life 351, Creature 351, Mana Conversion 258, Melee Defense 100), the bolt F9's data gives (Flame Bolt VI, mean 146.5, 2.3 s gesture,
/// 0.4 s overhead, 10 m of flight at 15 m/s), and the den's tuskers as the ground profile has them (Slave 400 health, Magic Defense 219 (M6 3 a2: ACE's current value, was the init level 200); Guard 400 health and Magic Defense 244, was 260 and 225).
/// </summary>
public sealed class FightPickerTests(ITestOutputHelper output)
{
    private const uint SlaveGuid = 0x80001234, GuardGuid = 0x80005678;

    private static FightSnapshot Slave(uint health = 300, uint healthMax = 300, uint allowed = 0) => new()
    {
        Health = health,
        HealthMax = healthMax,
        Stamina = 300,
        StaminaMax = 300,
        Mana = 300,
        ManaMax = 300,
        WarSkill = 361,
        LifeSkill = 351,
        CreatureSkill = 351,
        ManaConversion = 258,
        MeleeDefense = 100,
        DebuffLevel = 6,
        AllowedDebuffs = allowed,
        DrainVI = true,
        TargetGuid = SlaveGuid,
        TargetName = "Tusker Slave",
        TargetHealthFraction = 1.0,
        TargetMaxHealth = 400,
        TargetMagicDefense = 219,
        Distance = 10,
        LineClear = true,
        BoltName = "Flame Bolt VI",
        BoltLevel = 6,
        BoltMeanDamage = 146.5,
        BoltCastSeconds = 2.3 + 0.4 + (10 / 15.0),
        BoltBaseMana = 30,
        BoltPower = 250,
        DebuffCastSeconds = 2.7,
    };

    private static FightSnapshot Guard()
    {
        var s = Slave();
        (s.TargetGuid, s.TargetName, s.TargetMaxHealth, s.TargetMagicDefense) = (GuardGuid, "Tusker Guard", 400, 244);
        return s;
    }

    /// <summary>A picker with the target hitting for 25 a second and the heal at 0.04 s a point (F11's illustrative rates); the dance at its 0.09 seed.</summary>
    private static FightPicker Picker(Recording log, double healRate = 0.04, double incoming = 25)
    {
        var p = new FightPicker(log, "fight");
        p.Rates.NoteIncoming(0, 0);
        p.Rates.NoteIncoming(1000, (long)(incoming * 1000));
        p.Rates.SeedHeal(healRate);
        return p;
    }

    [Fact]
    public void The_decide_line_shows_the_rings_measured_factor_only_while_the_ring_is_an_option()
    {
        var log = new Recording();
        var p = new FightPicker(log, "fight") { LogEveryDecision = true };
        p.Rates.NoteIncoming(0, 0);
        p.Rates.NoteIncoming(1000, 25_000);
        p.Rates.SeedHeal(0.04);
        var s = Slave();
        p.Decide(in s, clock: 10);
        Assert.DoesNotContain(log.Lines, l => l.Contains("ring factor", StringComparison.Ordinal));
        s.RingFactor = 0.6;
        p.Decide(in s, clock: 11);
        Assert.Contains(log.Lines, l => l.StartsWith("decide: ", StringComparison.Ordinal) && l.EndsWith(" | ring factor 0.60", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Slave_at_full_health_gets_the_fire_vulnerability_then_bolts()
    {
        var log = new Recording();
        var p = Picker(log);
        var s = Slave(allowed: FightPicker.Bit(DebuffKind.FireVulnerability));

        var first = p.Decide(in s, clock: 10);
        Assert.Equal(FightMoveKind.Debuff, first.Kind);
        Assert.Equal(DebuffKind.FireVulnerability, first.Debuff);
        Assert.Equal(6, first.Level);
        Assert.True(first.AllIn < first.BoltAllIn, $"{first.AllIn} vs {first.BoltAllIn}");
        output.WriteLine($"[measure] f10 slave: vulnerability all-in {first.AllIn:0.00} s vs bolt {first.BoltAllIn:0.00} s");

        p.NoteLanded(SlaveGuid, DebuffKind.FireVulnerability, 2.5, 10 + 210);
        var next = p.Decide(in s, clock: 13);
        Assert.Equal(FightMoveKind.Bolt, next.Kind);
        Assert.Equal(2.5, p.VulnerabilityOn(SlaveGuid, 13));
        Assert.Equal(1.0, p.VulnerabilityOn(SlaveGuid, 10 + 211)); // lapsed: the ledger keeps ACE's duration

        // One line when the pick changes, in the brief's shape.
        Assert.Contains(log.Lines, l => l.StartsWith("pick: Fire Vulnerability Other VI on Tusker Slave 0x80001234 | all-in ", StringComparison.Ordinal) && l.Contains(" s vs bolt ", StringComparison.Ordinal) && l.Contains("| hit ", StringComparison.Ordinal) && l.Contains("| mana ", StringComparison.Ordinal) && l.Contains("| why ", StringComparison.Ordinal));
        Assert.Contains(log.Lines, l => l.StartsWith("pick: Flame Bolt VI on Tusker Slave", StringComparison.Ordinal));
        var lines = log.Lines.Count;
        p.Decide(in s, clock: 16);
        Assert.Equal(lines, log.Lines.Count); // never a line per repeated cast
    }

    [Fact]
    public void A_Guard_bolts()
    {
        var log = new Recording();
        var p = Picker(log);
        var s = Guard(); // the den's Guard names no opener: the profile allows no debuff on it (F8, the owner)

        var pick = p.Decide(in s, clock: 1);
        Assert.Equal(FightMoveKind.Bolt, pick.Kind);
        Assert.Equal(pick.BoltAllIn, pick.AllIn);
        Assert.Equal("Flame Bolt VI", pick.Spell);
    }

    [Fact]
    public void A_hurt_mage_and_a_healthy_target_pick_the_drain_as_an_attack_when_it_beats_the_bolt()
    {
        var log = new Recording();
        var p = Picker(log);
        var hurt = Slave(health: 180); // 60%: over D88's floor (50%), so the picker decides
        var pick = p.Decide(in hurt, clock: 1);
        Assert.Equal(FightMoveKind.DrainHealth, pick.Kind);
        Assert.Equal(6, pick.Level);
        Assert.True(pick.AllIn < pick.BoltAllIn);
        output.WriteLine($"[measure] f10 drain attack at 180/300: all-in {pick.AllIn:0.00} s vs bolt {pick.BoltAllIn:0.00} s");

        // At full health ACE scales the drain to nothing: the bolt.
        var full = Slave();
        Assert.Equal(FightMoveKind.Bolt, p.Decide(in full, clock: 2).Kind);

        // Not offered (mana under the floor, out of range): never picked.
        var unpaid = Slave(health: 180);
        unpaid.DrainVI = false;
        Assert.Equal(FightMoveKind.Bolt, p.Decide(in unpaid, clock: 3).Kind);
    }

    [Fact]
    public void Weakness_is_never_picked_at_400_health_and_the_log_says_once_why()
    {
        var log = new Recording();
        var p = Picker(log);
        var s = Slave(allowed: FightPicker.Bit(DebuffKind.Weakness) | FightPicker.Bit(DebuffKind.Imperil));
        (s.TargetStrength, s.TargetEndurance, s.TargetQuickness, s.TargetCoordination, s.TargetFocus, s.TargetSelf) = (210, 300, 180, 200, 70, 60);
        (s.TargetAttackSkill, s.TargetSwingSeconds, s.TargetPowerupSeconds) = (287, 1.2, 2.5);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(FightMoveKind.Bolt, p.Decide(in s, clock: i).Kind);
        }

        Assert.Single(log.Lines, l => l.StartsWith("pick: Weakness Other VI does not win here", StringComparison.Ordinal));
        Assert.Single(log.Lines, l => l.StartsWith("pick: Imperil Other VI never wins: Imperil lowers armour level", StringComparison.Ordinal));

        // With no attributes from any source the attribute debuffs are skipped, named once.
        var log2 = new Recording();
        var p2 = Picker(log2);
        var bare = Slave(allowed: FightPicker.Bit(DebuffKind.Weakness));
        p2.Decide(in bare, clock: 1);
        p2.Decide(in bare, clock: 2);
        Assert.Single(log2.Lines, l => l.Contains("TODO(f10-target-attributes): Weakness", StringComparison.Ordinal));
    }

    [Fact]
    public void A_debuff_that_reads_the_attack_skill_is_skipped_by_name_while_the_attributes_alone_serve_the_others_M6_3_a2()
    {
        // The profile gives attributes (FightPicker.FillTargetStats) but no attack skill: Weakness is skipped by its own TODO, once; Bafflement and Frailty read the attributes alone and are not skipped.
        var log = new Recording();
        var p = Picker(log);
        var s = Slave(allowed: FightPicker.Bit(DebuffKind.Weakness) | FightPicker.Bit(DebuffKind.Bafflement) | FightPicker.Bit(DebuffKind.Frailty));
        (s.TargetStrength, s.TargetEndurance, s.TargetQuickness, s.TargetCoordination, s.TargetFocus, s.TargetSelf) = (210, 300, 180, 200, 70, 60);
        p.Decide(in s, clock: 1);
        p.Decide(in s, clock: 2);
        Assert.Single(log.Lines, l => l.Contains("TODO(m6-target-attack-skill): Weakness", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, l => l.Contains("TODO(f10-target-attributes)", StringComparison.Ordinal));
    }

    [Fact]
    public void Drain_Stamina_refills_the_stamina_when_it_beats_Revitalize_Self()
    {
        var log = new Recording();
        var p = Picker(log);
        var drain = FightDebuffs.Find(DebuffKind.DrainStaminaOther, 6)!.Value;
        var revitalizeMana = Lifestone.Client.Session.Casting.ManaConversionModel.ExpectedCost(70, 250, 258);
        var revitalizeGain = BuffStatMods.Revitalize.Single(r => r.Id == 1182).Boost; // Revitalize Self VI: 80

        var s = Slave();
        (s.Stamina, s.TargetStamina) = (100, 400); // the mage short of stamina, a tusker full of it
        Assert.True(p.StaminaDrainWins(in s, in drain, revitalizeGain, 1.0, revitalizeMana, out var drainRate, out var revitalizeRate), $"{drainRate} vs {revitalizeRate}");
        output.WriteLine($"[measure] f10 stamina: drain {drainRate:0.0} a second vs Revitalize Self {revitalizeRate:0.0}");

        // A target with little stamina left gives less than Revitalize Self: the order's Revitalize stands.
        s.TargetStamina = 60;
        Assert.False(p.StaminaDrainWins(in s, in drain, revitalizeGain, 1.0, revitalizeMana, out _, out _));

        // And it is never an attack (F11: -2.5 s): the decision never picks it.
        var attack = Slave();
        (attack.Stamina, attack.TargetStamina, attack.AllowedDebuffs) = (100, 400, FightPicker.Bit(DebuffKind.DrainStaminaOther));
        Assert.Equal(FightMoveKind.Bolt, p.Decide(in attack, clock: 1).Kind);
    }

    [Fact]
    public void The_measured_yield_scales_the_stamina_drains_score_and_the_gain_if_it_lands_is_reported_M5_7_F15_7_F4()
    {
        var p = Picker(new Recording());
        var drain = FightDebuffs.Find(DebuffKind.DrainStaminaOther, 6)!.Value;
        var s = Slave();
        (s.Stamina, s.TargetStamina) = (100, 400);
        p.StaminaDrainWinsMeasured(in s, in drain, 80, 1.0, 20, 1.0, out var full, out _, out var gain);
        p.StaminaDrainWinsMeasured(in s, in drain, 80, 1.0, 20, 0.25, out var quarter, out _, out var gain2);
        Assert.True(gain > 0);
        Assert.Equal(gain, gain2, 6);
        Assert.Equal(full * 0.25, quarter, 6);
    }

    [Fact]
    public void The_measured_rates_move_the_pick()
    {
        // A little hurt (40 missing) and nothing hitting: with the heal cheap, the bolt; once the measured heal is dear, the drain (its 40 points are worth more than its cast).
        var log = new Recording();
        var p = Picker(log, healRate: 0.005, incoming: 0);
        var s = Slave(health: 260);
        Assert.Equal(FightMoveKind.Bolt, p.Decide(in s, clock: 1).Kind);

        for (var i = 0; i < 20; i++)
        {
            p.Rates.NoteHeal(12, 60); // 0.2 s a health point measured
        }

        Assert.Equal(0.2, p.Rates.SecondsPerHealth, 3);
        Assert.Equal(FightMoveKind.DrainHealth, p.Decide(in s, clock: 2).Kind);

        // The dance's rate starts at its seed and follows the landed dances.
        Assert.Equal(FightRates.SeedSecondsPerMana, p.Rates.SecondsPerMana);
        p.Rates.NoteDance(9, 60);
        Assert.Equal(0.15, p.Rates.SecondsPerMana, 6);
        p.Rates.NoteDance(3, 60);
        Assert.Equal(0.15 + (FightRates.Weight * (0.05 - 0.15)), p.Rates.SecondsPerMana, 6);
        p.Rates.NoteDance(0, 60); // ignored
        p.Rates.NoteDance(5, -3); // ignored
        Assert.Equal(2, p.Rates.ManaSamples);
    }

    [Fact]
    public void A_war_option_that_kills_faster_is_picked_and_a_tie_keeps_the_bolt()
    {
        var log = new Recording();
        var p = Picker(log);
        var s = Slave();
        (s.WarName, s.WarLevel, s.WarSeconds, s.WarBoltSeconds, s.WarMana) = ("Cassius' Ring of Fire", 6, 9.4, 10.1, 10.9);
        var ring = p.Decide(in s, clock: 1);
        Assert.Equal(FightMoveKind.War, ring.Kind);
        Assert.Equal("Cassius' Ring of Fire", ring.Spell);
        Assert.Equal(ring.BoltAllIn * 9.4 / 10.1, ring.AllIn, 6);

        s.WarSeconds = 10.1;
        Assert.Equal(FightMoveKind.Bolt, p.Decide(in s, clock: 2).Kind);
    }

    [Fact]
    public void Missing_data_keeps_the_bolt_with_one_loud_TODO_and_picks_are_counted_for_the_verdict()
    {
        var log = new Recording();
        var p = Picker(log);
        var s = Slave(allowed: FightPicker.Bit(DebuffKind.FireVulnerability));
        s.TargetMaxHealth = 0;
        Assert.True(double.IsNaN(p.Decide(in s, clock: 1).AllIn));
        p.Decide(in s, clock: 2);
        Assert.Single(log.Lines, l => l.StartsWith("TODO(f10-picker-data)", StringComparison.Ordinal));

        Assert.Equal("picks=none", p.Field());
        p.Count("Flame Bolt VI");
        p.Count("Fire Vulnerability Other VI");
        p.Count("Flame Bolt VI");
        p.Count("Cassius' Ring of Fire");
        Assert.Equal("picks=Flame_Bolt_VI:2,Fire_Vulnerability_Other_VI:1,Cassius_Ring_of_Fire:1", p.Field());
    }

    [Fact]
    [Trait("Tier", "Timing")] // m6-alloc-flake-fightpicker: a zero-byte allocation count (failed once under the parallel suite, passes alone: tiered JIT and the other tests on the thread pool)
    public void A_decision_allocates_nothing()
    {
        var log = new Recording();
        var p = Picker(log);
        var s = Slave(health: 200, allowed: FightPicker.Bit(DebuffKind.FireVulnerability) | FightPicker.Bit(DebuffKind.Weakness));
        (s.WarName, s.WarSeconds, s.WarBoltSeconds) = ("Flame Streak VI", 14.8, 10.1);
        p.Decide(in s, clock: 1); // the notes and the first pick line are written once
        var clock = 2.0;
        AllocationAssert.Nothing(() =>
        {
            p.Decide(in s, clock: clock++); // one decision a window: a short window rarely holds another test's GC (the suite runs in parallel)
            p.Count("Flame Bolt VI");
        });
    }

    [Fact]
    public void Under_the_drain_floor_the_fight_casts_the_drain_and_the_verdict_counts_the_picks()
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true) { HitsToKill = 40 };
        r.Caster.HealthPerCast = 40;
        r.Caster.ManaRegenPerSecond = 20;
        r.AddTusker();
        r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        r.Caster.SetHealth(max * 30 / 100); // under D88's 50% floor

        var scenario = r.Scenario();
        r.Run(scenario, 40, 0.1, () => scenario.Drains >= 1 && r.BoltAnswers.Any());

        Assert.Contains(r.Caster.Answered.Select(a => a.Spell), DrainPicker.Ids.Contains);
        var verdict = scenario.VerdictFields();
        Assert.Matches(@" picks=[^ ]*Drain_Health_Other[^ ]*:\d+", verdict);
        Assert.Contains(" picks=", verdict, StringComparison.Ordinal);
        output.WriteLine(verdict);
    }

    // --- M6 1 f: the picker by band ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The rows with Fire Vulnerability Other I to V from a SpellTable of the dat's ids and powers.</summary>
    private static DebuffRows LowRows()
    {
        var table = new Dictionary<uint, SpellInfo>();
        foreach (var spell in ArenaProfile.ExtraNamed("Fire Vulnerability Other")!.Spells.Take(5))
        {
            table[spell.Id] = WorldActionRig.Spell(spell.Id, SpellType.Enchantment) with { Name = spell.Name, School = MagicSchool.LifeMagic, Power = spell.Power, BaseMana = 10, Duration = spell.DurationSeconds };
        }

        return DebuffRows.FromSpellTable(table, out _);
    }

    /// <summary>A level-20 band mage (War 160, Life 150) with Flame Bolt III against a 250-health creature of Magic Defense 60, the plan cap III.</summary>
    private static FightSnapshot Band(uint known)
    {
        var s = Slave(allowed: FightPicker.Bit(DebuffKind.FireVulnerability));
        (s.WarSkill, s.LifeSkill, s.CreatureSkill, s.ManaConversion, s.MeleeDefense) = (160, 150, 0, 60, 40);
        (s.DebuffLevel, s.BookRead, s.KnownDebuffs, s.DrainVI) = (3, true, known, false);
        (s.TargetName, s.TargetMaxHealth, s.TargetMagicDefense) = ("Drudge Skulker", 250, 60);
        (s.BoltName, s.BoltLevel, s.BoltMeanDamage, s.BoltCastSeconds, s.BoltBaseMana, s.BoltPower) = ("Flame Bolt III", 3, 46, 2.9, 15, 125);
        return s;
    }

    [Fact]
    public void A_band_mage_scores_and_picks_the_vulnerability_at_its_own_level_when_the_book_holds_it()
    {
        var log = new Recording();
        var p = Picker(log, healRate: 0.1, incoming: 5);
        p.Rows = LowRows();
        var s = Band(known: FightPicker.Bit(DebuffKind.FireVulnerability));
        var pick = p.Decide(in s, clock: 1);
        Assert.Equal((FightMoveKind.Debuff, DebuffKind.FireVulnerability, 3, "Fire Vulnerability Other III"), (pick.Kind, pick.Debuff, pick.Level, pick.Spell));
        Assert.DoesNotContain(log.Lines, l => l.Contains("f10-debuff-levels", StringComparison.Ordinal));
        Assert.Single(log.Lines, l => l.StartsWith("pick: Weakness Other III is skipped: the spellbook does not hold it", StringComparison.Ordinal) || l.StartsWith("pick: Weakness level III is skipped", StringComparison.Ordinal));
    }

    [Fact]
    public void A_vulnerability_the_book_lacks_is_skipped_by_name_once_and_the_bolt_goes_on()
    {
        var log = new Recording();
        var p = Picker(log, healRate: 0.1, incoming: 5);
        p.Rows = LowRows();
        var s = Band(known: 0);
        Assert.Equal(FightMoveKind.Bolt, p.Decide(in s, clock: 1).Kind);
        Assert.Equal(FightMoveKind.Bolt, p.Decide(in s, clock: 2).Kind);
        Assert.Single(log.Lines, l => l.StartsWith("pick: Fire Vulnerability Other III is skipped: the spellbook does not hold it or its school is not trained", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_data_is_named_once_per_kind_of_target_and_never_on_the_decide_line()
    {
        var log = new Recording();
        var p = new FightPicker(log, "fight") { LogEveryDecision = true };
        var s = Slave();
        s.TargetMaxHealth = 0;
        p.Decide(in s, clock: 1);
        p.Decide(in s, clock: 2);
        var g = Guard();
        g.TargetMaxHealth = 0;
        p.Decide(in g, clock: 3);
        p.Decide(in s, clock: 4);
        Assert.Equal(2, log.Lines.Count(l => l.StartsWith("TODO(f10-picker-data)", StringComparison.Ordinal)));
        Assert.Single(log.Lines, l => l.StartsWith("TODO(f10-picker-data): Tusker Guard: the target's maximum health", StringComparison.Ordinal));
        Assert.Equal(4, log.Lines.Count(l => l.StartsWith("decide:", StringComparison.Ordinal)));
        Assert.DoesNotContain(log.Lines, l => l.StartsWith("decide:", StringComparison.Ordinal) && l.Contains("TODO(", StringComparison.Ordinal));
    }

    [Fact]
    public void With_no_war_option_the_bolts_numbers_come_from_the_bolt_the_fight_casts_f10_picker_data()
    {
        var picker = new WarSpellPicker(new WarPickSettings());
        var s = new FightSnapshot();
        Assert.True(FightPicker.FillBolt(ref s, picker, 27, "Flame Bolt I", 5, 1, 70, 15));
        Assert.Equal(("Flame Bolt I", 1, 5u, 1u), (s.BoltName, s.BoltLevel, s.BoltBaseMana, s.BoltPower));
        Assert.InRange(s.BoltMeanDamage, 23.5 * 1.05, 30); // (16 + 31) / 2 = 23.5, the skill bonus, the crit share and the rating
        Assert.Equal(2.30 + 0.4 + 1.0, s.BoltCastSeconds, 6); // the gesture, the overhead and 15 m at 15 m/s

        var frost = new FightSnapshot();
        Assert.True(FightPicker.FillBolt(ref frost, picker, 71, "Frost Bolt III", 15, 125, 160, 5)); // another element's bolt has its row (M6 1 f)
        Assert.Equal(3, frost.BoltLevel);

        var unknown = new FightSnapshot();
        Assert.False(FightPicker.FillBolt(ref unknown, picker, 9999, "Unknown Bolt", 10, 50, 100, 5));
        Assert.Equal((0.0, "Unknown Bolt"), (unknown.BoltMeanDamage, unknown.BoltName)); // the damage stays unknown: the picker names it once
        Assert.True(unknown.BoltCastSeconds > 0);
    }

    /// <summary>r2b's numbers at level 10 on the picker's scale: Flame Bolt II's mean 44 at 87 %, 2.85 s; a Female Tusker of 95 health at 37 %.</summary>
    private static FightSnapshot Finisher(double fraction = 0.37, uint health = 24, bool unknown = false)
    {
        var s = Slave(health: health, healthMax: 49);
        (s.WarSkill, s.TargetMaxHealth, s.TargetMagicDefense) = (118, 95, 55); // the Magic Defense that reads the log's 87 %
        (s.BoltMeanDamage, s.BoltCastSeconds, s.TargetHealthFraction, s.TargetHealthUnknown) = (44, 2.85, fraction, unknown);
        return s;
    }

    private static IncomingRead Blows(params (double At, double Damage)[] blows)
    {
        var hits = new RecentHits();
        foreach (var (at, damage) in blows)
        {
            hits.Note(at, damage);
        }

        return hits.Read(blows[^1].At + 0.01);
    }

    private static readonly (double, double)[] R2bFirst = [(0.49, 7), (3.80, 18)];
    private static readonly (double, double)[] R2bSecond = [(3.80, 18), (5.27, 16)];

    [Fact]
    public void The_finishing_bolt_wins_at_r2bs_first_decision_the_mage_lives_through_the_cast_M6_7_Y()
    {
        var s = Finisher();
        var picker = new FightPicker(new Recording(), "t");
        var landing = FightDebuffs.Chance(s.WarSkill, s.TargetMagicDefense);
        var read = picker.Finishing(in s, Blows(R2bFirst), 7.04, 2.83, 0, 0);
        Assert.True(read.Wins);
        Assert.True(read.Survives);
        Assert.Equal((95 * 0.37, 44 * landing), (read.Remaining, read.Expected));
    }

    [Fact]
    public void The_finishing_bolt_wins_at_r2bs_second_decision_when_no_heal_can_save_her_M6_7_Y()
    {
        var s = Finisher(health: 8);
        var read = new FightPicker(new Recording(), "t").Finishing(in s, Blows(R2bSecond), 7.04, 2.83, 0, 0);
        Assert.True(read.Wins);
        Assert.False(read.Survives);
        Assert.False(read.HealSaves);
        Assert.Contains("no heal can save", read.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_heal_that_saves_her_beats_a_bolt_she_would_not_live_through_M6_7_Y()
    {
        var s = Finisher(health: 12);
        var read = new FightPicker(new Recording(), "t").Finishing(in s, Blows(R2bSecond), 40, 0.5, 0, 0); // a half-second heal of 40 a second, before the next blow is due
        Assert.False(read.Wins);
        Assert.True(read.HealSaves);
    }

    [Fact]
    public void Health_a_bolt_does_not_take_or_a_health_not_known_or_one_the_projectiles_in_flight_take_is_no_finish_M6_7_Y()
    {
        var picker = new FightPicker(new Recording(), "t");
        var read = Blows(R2bFirst);
        var full = Finisher(fraction: 0.9);
        Assert.False(picker.Finishing(in full, read, 7, 2.8, 0, 0).Wins);
        var unknown = Finisher(fraction: 0.1, unknown: true);
        Assert.False(picker.Finishing(in unknown, read, 7, 2.8, 0, 0).Wins);
        var low = Finisher();
        Assert.False(picker.Finishing(in low, read, 7, 2.8, 44, 0).Wins);
    }

    [Fact]
    public void Our_vulnerability_on_the_target_counts_toward_the_finish_M6_7_Y()
    {
        var s = Finisher(fraction: 0.6); // 57 health: over the plain 44 at its chance
        var picker = new FightPicker(new Recording(), "t");
        var read = Blows(R2bFirst);
        Assert.False(picker.Finishing(in s, read, 7, 2.8, 0, 0).Wins);
        picker.NoteLanded(s.TargetGuid, DebuffKind.FireVulnerability, 2.0, 100);
        Assert.True(picker.Finishing(in s, read, 7, 2.8, 0, 0).Wins);
    }

    private sealed class Recording : ILogSink
    {
        public List<string> Lines { get; } = [];

        public bool IsEnabled(LogSeverity severity) => true;

        public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields) => Lines.Add(message);
    }
}
