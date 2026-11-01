using System.Globalization;
using System.Text.RegularExpressions;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Net.Messages;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 item 7 F21 (D92, the owner): a drain is cast only when it would help more than Heal Self; otherwise Heal Self. Heal Self and each drain candidate are scored in health a second (<see cref="HealPick"/>): the chance it lands
/// times the health it gives plus a small credit for the damage, over the cast seconds plus the mana's time at the dance's rate; a tie goes to Heal Self. The numbers are asserted, not hoped for.
/// </summary>
public sealed class FightF21Tests
{
    private const double SecondsPerMana = FightRates.SeedSecondsPerMana; // the dance's seed
    private const double Weight = HealPick.DefaultDamageWeight;

    /// <summary>A drain level's candidate from the pinned rows and a target's health, for a mage missing <paramref name="missing"/>.</summary>
    private static HealCandidate Drain(uint id, string name, uint targetHealth, uint missing, double chance, double mana, double seconds)
    {
        var row = DrainPicker.RowOf(id)!.Value;
        BuffStatMods.Transfer(in row, targetHealth, missing, out var taken, out var given);
        return new HealCandidate(name, 0x800051D0, given, taken, chance, mana, seconds);
    }

    private static HealCandidate HealSelfVI(uint missing, double chance = 0.9, double mana = 45) =>
        new("Heal Self VI", 0, Math.Min(missing, MeasuredHeals.SelfSeed(6)), 0, chance, mana, MeasuredHeals.DrainSeed(6));

    [Fact]
    public void Heal_Self_at_level_VI_is_ACEs_55_to_120_mean_87_5_cut_to_the_missing_health_and_scored_in_health_a_second()
    {
        Assert.Equal(87.5, MeasuredHeals.SelfSeed(6)); // world SQL spell 1161: boost 55, variance 65; ACE HandleCastSpell_Boost, WorldObject_Magic.cs 481-491 @95ef426d
        var self = HealSelfVI(missing: 190);
        Assert.Equal(87.5, self.Heal);
        Assert.Equal(60, HealSelfVI(missing: 60).Heal);

        // chance 0.9 x 87.5 over (3.0 s + 45 mana x the dance's seconds a mana point)
        Assert.Equal(0.9 * 87.5 / (3.0 + (45 * SecondsPerMana)), HealPick.PerSecond(in self, SecondsPerMana, Weight), 9);

        // a drain adds a quarter of the damage it does to the target
        var drain = new HealCandidate("d", 1, 60, 30, 0.5, 10, 1.6);
        Assert.Equal(0.5 * (60 + (0.25 * 30)) / (1.6 + (10 * SecondsPerMana)), HealPick.PerSecond(in drain, SecondsPerMana, Weight), 9);
        Assert.Equal(0.5 * 60 / (1.6 + (10 * SecondsPerMana)), HealPick.PerSecond(in drain, SecondsPerMana, 0), 9);
    }

    [Fact]
    public void A_near_dead_target_loses_to_Heal_Self()
    {
        // a Slave with 4 health left, the mage missing 100: level I takes round(4 x 0.25) = 1 and its loss of -1 doubles it: 2
        var one = Drain(1237, "Drain Health Other I", 4, 100, 0.9, 20, 1.6);
        var six = Drain(1242, "Drain Health Other VI", 4, 100, 0.9, 45, 3.0);
        Assert.True(one.Heal <= 2 && six.Heal <= 2, $"I gives {one.Heal}, VI {six.Heal}");
        var self = HealSelfVI(100);

        var choice = HealPick.Choose(in self, true, new[] { one, six }, SecondsPerMana, Weight, out var selfRate, out var best, out var bestRate);

        Assert.Equal(HealPick.HealSelf, choice);
        Assert.True(bestRate < selfRate, $"the best drain {bestRate:0.###} h/s is under Heal Self's {selfRate:0.###}");
        Assert.True(best >= 0);
    }

    [Fact]
    public void A_full_Slave_with_the_mage_missing_60_picks_Drain_I_by_the_numbers()
    {
        // a 400-health Slave at full health, the mage missing 60: level I takes 60 (the cap) and gives min(60 x 2, 60) = 60 in one 1.6 s gesture; Heal Self VI's 87.5 is cut to 60 in 3.0 s
        var one = Drain(1237, "Drain Health Other I", 400, 60, 0.9, 20, MeasuredHeals.DrainSeed(1));
        var six = Drain(1242, "Drain Health Other VI", 400, 60, 0.9, 45, MeasuredHeals.DrainSeed(6));
        Assert.Equal(60, one.Heal);
        Assert.Equal(30, one.Damage); // ACE scales what the target loses with the cut: 60 taken doubled to 120, cut to 60, so 30 taken
        Assert.Equal(60, six.Heal);
        var self = HealSelfVI(60);

        var oneRate = HealPick.PerSecond(in one, SecondsPerMana, Weight);
        var sixRate = HealPick.PerSecond(in six, SecondsPerMana, Weight);
        var selfRate = HealPick.PerSecond(in self, SecondsPerMana, Weight);
        Assert.True(oneRate > selfRate, $"I {oneRate:0.###} h/s against Heal Self's {selfRate:0.###}");
        Assert.True(oneRate > sixRate, $"I {oneRate:0.###} h/s against VI's {sixRate:0.###}");

        var choice = HealPick.Choose(in self, true, new[] { one, six }, SecondsPerMana, Weight, out _, out var best, out _);
        Assert.Equal(0, choice);
        Assert.Equal(0, best);
    }

    [Fact]
    public void A_tie_goes_to_Heal_Self_and_a_drain_just_over_it_wins()
    {
        var self = new HealCandidate("Heal Self VI", 0, 60, 0, 0.9, 45, 3.0);
        var tie = new HealCandidate("Drain Health Other I", 1, 60, 0, 0.9, 45, 3.0); // the same chance, heal, mana and seconds, no damage credit
        Assert.Equal(HealPick.HealSelf, HealPick.Choose(in self, true, new[] { tie }, SecondsPerMana, Weight, out var selfRate, out _, out var bestRate));
        Assert.Equal(selfRate, bestRate);

        var over = tie with { Damage = 1 }; // a quarter of a point of credit
        Assert.Equal(0, HealPick.Choose(in self, true, new[] { over }, SecondsPerMana, Weight, out _, out _, out _));
        Assert.Equal(HealPick.HealSelf, HealPick.Choose(in self, true, new[] { over }, SecondsPerMana, 0, out _, out _, out _)); // the weight 0 makes it a tie again
    }

    [Fact]
    public void With_no_Heal_Self_the_best_drain_is_cast_and_with_neither_nothing()
    {
        var self = HealSelfVI(60);
        var drain = new HealCandidate("Drain Health Other I", 1, 1, 0, 0.9, 20, 1.6);
        Assert.Equal(0, HealPick.Choose(in self, false, new[] { drain }, SecondsPerMana, Weight, out var selfRate, out _, out _));
        Assert.Equal(0, selfRate);
        Assert.Equal(HealPick.Nothing, HealPick.Choose(in self, false, [], SecondsPerMana, Weight, out _, out _, out _));
        Assert.Equal(HealPick.HealSelf, HealPick.Choose(in self, true, [], SecondsPerMana, Weight, out _, out var none, out _));
        Assert.Equal(-1, none);
    }

    [Fact]
    public void The_pick_line_carries_both_scores_and_the_best_drain()
    {
        var self = HealSelfVI(100);
        HealCandidate[] drains = [new("Drain Health Other I", 0x800051D0, 2, 1, 0.9, 20, 1.6), new("Drain Health Other VI", 0x800051D0, 2, 1, 0.9, 45, 3.0)];
        var choice = HealPick.Choose(in self, true, drains, SecondsPerMana, Weight, out var selfRate, out var best, out var bestRate);
        var line = HealPick.Line(choice, in self, selfRate, drains, best, bestRate);
        var heal = selfRate.ToString("0.###", CultureInfo.InvariantCulture);
        var drain = bestRate.ToString("0.###", CultureInfo.InvariantCulture);
        Assert.Equal($"heal pick: Heal Self VI h/s={heal} vs Heal Self h/s={heal} (best drain Drain Health Other I on 0x800051D0 h/s={drain})", line);
        Assert.Equal("heal pick: Heal Self VI h/s=0 vs Heal Self h/s=0 (no drain offered)", HealPick.Line(HealPick.HealSelf, default(HealCandidate) with { Name = "Heal Self VI" }, 0, [], -1, 0));

        var wins = HealPick.Line(0, in self, selfRate, drains, 0, bestRate);
        Assert.StartsWith("heal pick: Drain Health Other I h/s=", wins, StringComparison.Ordinal);
    }

    [Fact]
    public void The_damage_weight_is_a_registered_option_default_a_quarter_and_a_bad_value_keeps_it()
    {
        Assert.Equal(0.25, new FightOptions().HealDamageWeight);
        Assert.Equal(0.5, FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-heal-damage-weight=0.5"])).HealDamageWeight);
        Assert.Equal(0, FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-heal-damage-weight=0"])).HealDamageWeight);
        Assert.Equal(0.25, FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-heal-damage-weight=2"])).HealDamageWeight);
        Assert.Contains(OptionCatalog.Fight().Options, x => x.Name == "--fight-heal-damage-weight");
    }

    [Fact]
    public void In_the_fight_a_near_dead_Slave_gets_Heal_Self_and_a_full_Slave_a_drain_exactly_when_the_logged_scores_say_so()
    {
        // the near-dead Slave: four points of a 400-health Slave
        var dead = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true) { HitsToKill = 400 };
        dead.Caster.HealthPerCast = 20;
        dead.Caster.ManaRegenPerSecond = 20;
        var weak = dead.AddTusker(40);
        dead.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        dead.Caster.SetHealth(max * 55 / 100);
        var h = dead.R.H;
        h.R.Reducer.OnUpdateHealth(h.R.Context, h.Header, new UpdateHealthEvent(weak, 0.01f));
        dead.R.Tick();
        var scenario = dead.Scenario();
        dead.Run(scenario, 30, 0.1, () => dead.Caster.Answered.Any(a => VitalsMeans.HealSelf.Contains(a.Spell) || DrainPicker.Ids.Contains(a.Spell)));
        Assert.Contains(dead.Caster.Answered, a => VitalsMeans.HealSelf.Contains(a.Spell));
        Assert.DoesNotContain(dead.Caster.Answered, a => DrainPicker.Ids.Contains(a.Spell));
        var deadLine = dead.Log.Lines.First(l => l.Contains("emergency: heal pick:", StringComparison.Ordinal));
        Assert.Contains("heal pick: Heal Self", deadLine, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"best drain Drain Health Other \w+ on 0x" + weak.ToString("X8", CultureInfo.InvariantCulture) + @" h/s=[\d.]+\)"), deadLine);

        // a full Slave, the mage missing 60: the cast follows the scores the line carries
        var full = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true) { HitsToKill = 400 };
        full.Caster.HealthPerCast = 20;
        full.Caster.ManaRegenPerSecond = 20;
        var slave = full.AddTusker(40);
        full.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var fullMax);
        full.Caster.SetHealth(fullMax - 60);
        var h2 = full.R.H;
        h2.R.Reducer.OnUpdateHealth(h2.R.Context, h2.Header, new UpdateHealthEvent(slave, 1f));
        full.R.Tick();
        var fullScenario = full.Scenario();
        full.Run(fullScenario, 30, 0.1, () => full.Caster.Answered.Any(a => VitalsMeans.HealSelf.Contains(a.Spell) || DrainPicker.Ids.Contains(a.Spell)));
        var fullLine = full.Log.Lines.FirstOrDefault(l => l.Contains("heal pick:", StringComparison.Ordinal));
        Assert.NotNull(fullLine);
        var m = Regex.Match(fullLine!, @"heal pick: .+? h/s=([\d.]+) vs Heal Self h/s=([\d.]+) ");
        Assert.True(m.Success, fullLine);
        var (chosen, self) = (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        var drainCast = full.Caster.Answered.Any(a => DrainPicker.Ids.Contains(a.Spell));
        Assert.Equal(chosen > self, drainCast); // a drain is cast exactly when the line says it scores more than Heal Self
    }
}
