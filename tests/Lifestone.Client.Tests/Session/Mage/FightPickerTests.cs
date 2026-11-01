using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 7 F10 (D90): the fight's one live picker. The situations use F11's illustrative level-80 mage (War 361, Life 351, Creature 351, Mana Conversion 258, Melee Defense 100), the bolt F9's data gives (Flame Bolt VI, mean 146.5, 2.3 s gesture,
/// 0.4 s overhead, 10 m of flight at 15 m/s), and the den's tuskers as the ground profile has them (Slave 400 health, Magic Defense 200; Guard 260 and 225).
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
        TargetMagicDefense = 200,
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
        (s.TargetGuid, s.TargetName, s.TargetMaxHealth, s.TargetMagicDefense) = (GuardGuid, "Tusker Guard", 260, 225);
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

    private sealed class Recording : ILogSink
    {
        public List<string> Lines { get; } = [];

        public bool IsEnabled(LogSeverity severity) => true;

        public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields) => Lines.Add(message);
    }
}
