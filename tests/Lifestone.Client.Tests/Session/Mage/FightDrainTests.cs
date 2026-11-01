using DatReaderWriter.Enums;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 item 7 F7, the owner's D88: when the fight's health runs low it drains its target (Drain Health Other, the level the picker judges best at that moment) until the safe line, then bolts again; with no target in reach it casts Heal Self.
/// The picker (DrainPicker) is engine-free and tested on numbers; the rule is tested over the scripted caster. Nothing here needs a server; the level-floor test needs the dats' formulas.
/// </summary>
public sealed class FightDrainTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint PyrealScarab = 690;
    private const uint PlatinumScarab = 8897;
    private const uint PrismaticTaper = 20631;

    private static SpellInfo Info(uint id)
    {
        var spell = ScriptedCaster.DrainLine.Spells.First(s => s.Id == id);
        return WorldActionRig.Spell(id, SpellType.Transfer, SpellFlags.Resistable | SpellFlags.Reversed) with
        {
            Name = spell.Name,
            School = MagicSchool.LifeMagic,
            Power = spell.Power,
            BaseMana = spell.Position <= 6 ? 10u * (uint)spell.Position : 60u,
            BaseRangeConstant = 33f,
            BaseRangeMod = 0f,
        };
    }

    private static DrainLevel[] Levels() => [.. DrainPicker.Ids.Select(id => new DrainLevel(Info(id), DrainPicker.RowOf(id)!.Value, 1.0))];

    private static uint[] Floors(DrainLevel[] levels) => [.. levels.Select(l => GearCasts.ManaCeiling(l.Info, 0))];

    private static DrainSituation Mage(uint health, uint healthMax = 300, uint mana = 500, uint life = 400, uint targetHealth = 400, uint defence = 200) =>
        new(health, healthMax, mana, life, life, targetHealth, defence, 10);

    private static bool IsBolt(uint id) => FightManaTests.Rig.IsBolt(id);

    [Fact]
    public void The_transfer_rows_are_the_pinned_worlds_and_level_VI_heals_more_than_VII_on_a_big_target()
    {
        Assert.Equal(DrainPicker.Ids.Count, DrainPicker.Rows.Count);
        Assert.Equal(DrainPicker.Ids, DrainPicker.Rows.Select(r => r.Id));
        Assert.Equal(DrainPicker.Ids, ScriptedCaster.DrainLine.Spells.Take(7).Select(s => s.Id));
        var levels = Levels();
        var s = Mage(health: 100, targetHealth: 400);
        Assert.Equal(112u, DrainPicker.HealOf(in levels[5], in s)); // VI: 0.4 of 400 cut to 150, a quarter lost
        Assert.Equal(90u, DrainPicker.HealOf(in levels[6], in s)); // VII: 0.5 of 400 cut to 180, half lost
        Assert.Equal(120u, DrainPicker.HealOf(in levels[4], in s)); // V: 0.3 of 400 = 120, nothing lost
    }

    [Fact]
    public void A_full_health_target_and_a_mage_under_half_pick_the_level_with_the_most_expected_heal_and_pay_its_mana()
    {
        var levels = Levels();
        var s = Mage(health: 100, targetHealth: 400);
        var pick = DrainPicker.Pick(levels, Floors(levels), in s);

        Assert.True(pick.Index >= 3, "a high level, not the first ones");
        var best = Enumerable.Range(0, levels.Length).Max(i => DrainPicker.HealOf(in levels[i], in s));
        Assert.True(pick.HealIfLanded >= best * 0.9, $"the pick heals {pick.HealIfLanded}, the best {best}");
        Assert.InRange(pick.Chance, 0.9, 1.0);
        Assert.Equal(GearCasts.ManaCeiling(levels[pick.Index].Info, 0), pick.Mana);
        Assert.True(pick.Why is DrainWhy.Best or DrainWhy.CheaperNearBest);
    }

    [Fact]
    public void A_nearly_dead_target_picks_a_lower_level_or_a_bolt()
    {
        var levels = Levels();
        var low = DrainPicker.Pick(levels, Floors(levels), Mage(health: 100, targetHealth: 24));
        Assert.Equal(0, low.Index); // level I: a quarter of 24 taken and given back twice over, for the least mana

        var dying = DrainPicker.Pick(levels, Floors(levels), Mage(health: 100, targetHealth: 3));
        Assert.Equal(-1, dying.Index); // 3 points: nothing worth a cast, the bolt kills it
        Assert.Equal(DrainWhy.NothingWorthACast, dying.Why);
    }

    [Fact]
    public void The_pick_moves_with_the_Life_Magic_read_at_that_moment()
    {
        var levels = Levels();
        var strong = DrainPicker.Pick(levels, Floors(levels), Mage(health: 100, life: 450));
        var weak = DrainPicker.Pick(levels, Floors(levels), Mage(health: 100, life: 225));
        Assert.True(strong.Chance > weak.Chance, $"{strong.Chance} against {weak.Chance}");
        Assert.NotEqual(strong.Index, weak.Index); // the weaker skill cannot land the top levels (power 238 to 325 against 225): the pick drops
        Assert.True(weak.Index < strong.Index);
    }

    [Fact]
    public void The_pick_follows_the_mana_the_range_and_the_levels_offered()
    {
        var levels = Levels();
        Assert.Equal(DrainWhy.ManaShort, DrainPicker.Pick(levels, Floors(levels), Mage(health: 100, mana: 5)).Why);
        Assert.Equal(DrainWhy.OutOfRange, DrainPicker.Pick(levels, Floors(levels), Mage(health: 100) with { Distance = 40 }).Why);
        Assert.Equal(DrainWhy.NoLevels, DrainPicker.Pick([], [], Mage(health: 100)).Why);
        Assert.Equal(DrainWhy.NothingMissing, DrainPicker.Pick(levels, Floors(levels), Mage(health: 300)).Why);
        var cheap = DrainPicker.Pick(levels, Floors(levels), Mage(health: 100, mana: 35)); // only I to III are paid
        Assert.InRange(cheap.Index, 0, 2);
    }

    [RequiresDatsFact]
    public void The_level_floor_is_kept_VI_with_Pyreal_Scarabs_at_80_and_VII_with_Platinum_at_126()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var drain = new FightDrain(spells, NullLogSink.Instance, 50, 75);
        var refill = new FightRefill(spells, NullLogSink.Instance, 0);

        drain.Fill(null!, null, new Dictionary<uint, int> { [PyrealScarab] = 10, [PrismaticTaper] = 100 }, 0.9, refill);
        Assert.Equal([1242u], drain.Levels.ToArray().Select(l => l.Info.Id)); // the level 80 pack: VI only

        drain.Fill(null!, null, new Dictionary<uint, int> { [PyrealScarab] = 10, [PlatinumScarab] = 10, [PrismaticTaper] = 100 }, 0.9, refill);
        Assert.Equal([1242u, 2328u], drain.Levels.ToArray().Select(l => l.Info.Id)); // the level 126 pack holds both: the picker chooses

        drain.Fill(null!, null, new Dictionary<uint, int> { [PlatinumScarab] = 10, [PrismaticTaper] = 100 }, 0.9, refill);
        Assert.Equal([2328u], drain.Levels.ToArray().Select(l => l.Info.Id));

        drain.Fill(null!, null, new Dictionary<uint, int> { [PyrealScarab] = 10, [PrismaticTaper] = 0 }, 0.9, refill);
        Assert.Equal(0, drain.Count); // the tapers of the formula are short: no level
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void The_dats_spell_rows_equal_the_profile_ids_powers_and_costs_of_the_line()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var id in DrainPicker.Ids)
        {
            Assert.Equal(SpellType.Transfer, spells[id].MetaSpellType);
            Assert.Equal(MagicSchool.LifeMagic, spells[id].School);
        }

        Assert.Equal([10u, 20u, 30u, 40u, 50u, 60u, 60u], DrainPicker.Ids.Select(id => spells[id].BaseMana));
        Assert.Equal([38u, 88u, 138u, 188u, 238u, 288u, 325u], DrainPicker.Ids.Select(id => spells[id].Power));
    }

    [Fact]
    public void Low_health_with_a_target_in_line_drains_until_the_safe_line_then_bolts_again()
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true, fight: new FightOptions { EmergencyFloorPercent = 0 }) { HitsToKill = 40 }; // M5 7 F16: the old drain rule alone (the emergency has its own tests)
        r.Caster.HealthPerCast = 40;
        r.Caster.ManaRegenPerSecond = 20;
        var tusker = r.AddTusker();
        r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        r.Caster.SetHealth(max * 30 / 100); // under the 50% line

        var scenario = r.Scenario();
        r.Run(scenario, 40, 0.1, () => scenario.Drains >= 2 && r.BoltAnswers.Any());

        var spells = r.Caster.Answered.Select(a => a.Spell).ToList();
        var drains = spells.Count(DrainPicker.Ids.Contains);
        Assert.True(drains >= 2, "the drain is pressed again while the health is under the safe line");
        var firstBolt = spells.FindIndex(id => IsBolt(id));
        Assert.True(firstBolt > 0 && spells.Take(firstBolt).All(id => DrainPicker.Ids.Contains(id) || !IsBolt(id)), "drains first, then the bolt");
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("did not land", StringComparison.Ordinal));
        r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _);
        Assert.True(health * 100 >= max * 75, $"health {health} of {max} is at the safe line when the bolts begin");
        Assert.Equal(drains, scenario.Drains);
        Assert.Equal(max * 30 / 100, scenario.HealthMin);
        Assert.All(r.Log.Lines.Where(l => l.Contains("health: drain:", StringComparison.Ordinal)), l => Assert.Contains($"on 0x{tusker:X8} at health", l, StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("health: drain pick: ", StringComparison.Ordinal) && l.Contains("expected heal", StringComparison.Ordinal) && l.Contains("chance", StringComparison.Ordinal) && l.Contains("mana", StringComparison.Ordinal));
        Assert.Contains($" drains={drains} heals=0 health_min={max * 30 / 100}", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void Low_health_with_no_target_in_reach_casts_Heal_Self_and_no_drain()
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true, fight: new FightOptions { EmergencyFloorPercent = 0 }); // M5 7 F16: the old drain rule alone
        r.Caster.HealthPerCast = 40;
        r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        r.Caster.SetHealth(max * 30 / 100);

        var scenario = r.Scenario();
        r.Run(scenario, 30, 0.1, () => r.Caster.Answered.Count(a => VitalsMeans.HealSelf.Contains(a.Spell)) >= 2);

        var heals = r.Caster.Answered.Where(a => VitalsMeans.HealSelf.Contains(a.Spell)).Select(a => a.Spell).ToList();
        Assert.True(heals.Count >= 2, "Heal Self is pressed again while the health is under the safe line");
        Assert.All(heals, id => Assert.Equal(1161u, id)); // the highest level whose cast chance reaches 0.9 at the scripted Life Magic (VII, power 300, does not)
        Assert.DoesNotContain(r.Caster.Answered, a => DrainPicker.Ids.Contains(a.Spell));
        Assert.Equal(0, scenario.Drains);
        Assert.True(scenario.Heals >= 1);
        Assert.Contains(r.Log.Lines, l => l.Contains("health: heal: ", StringComparison.Ordinal) && l.Contains("no target in reach", StringComparison.Ordinal));
    }

    [Fact]
    public void With_the_health_over_the_line_or_the_rule_off_no_drain_is_cast()
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true);
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 30, 0.1);
        Assert.Equal(0, scenario.Drains + scenario.Heals);
        Assert.DoesNotContain(r.Caster.Answered, a => DrainPicker.Ids.Contains(a.Spell) || VitalsMeans.HealSelf.Contains(a.Spell));

        var off = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true) { Fight = new FightOptions { DrainUnderPercent = 0 } };
        off.AddTusker();
        off.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        off.Caster.SetHealth(max / 10);
        var offScenario = off.Scenario();
        off.Run(offScenario, 30, 0.1);
        Assert.DoesNotContain(off.Caster.Answered, a => DrainPicker.Ids.Contains(a.Spell));
    }

    [Fact]
    public void The_drain_options_are_registered_parsed_and_the_until_line_never_sits_under_the_under_line()
    {
        Assert.Equal(50, new FightOptions().DrainUnderPercent);
        Assert.Equal(75, new FightOptions().DrainUntilPercent);
        var o = FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-drain-under=40", "--fight-drain-until=60"]));
        Assert.Equal((40, 60), (o.DrainUnderPercent, o.DrainUntilPercent));
        Assert.Equal(0, FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-drain-under=0"])).DrainUnderPercent);
        Assert.Equal(80, FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-drain-under=80"])).DrainUntilPercent); // pulled up with it
        Assert.Equal(50, FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-drain-under=101"])).DrainUnderPercent);
        Assert.Contains(OptionCatalog.Fight().Options, x => x.Name == "--fight-drain-under");
        Assert.Contains(OptionCatalog.Fight().Options, x => x.Name == "--fight-drain-until");
    }
}
