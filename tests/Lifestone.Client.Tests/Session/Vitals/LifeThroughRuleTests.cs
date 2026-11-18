using DatReaderWriter.Enums;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Xunit;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 item 14, <c>m6-life-through</c>: the numbers of Harm Other and the rule that chooses between it and Drain Health Other, on numbers alone (<see cref="HarmOtherRows"/>, <see cref="LifeThroughRule"/>). What these prove:
/// the rows are the world's (the same the picker's <see cref="FightDebuffs"/> holds); Harm Other IV on a Tusker Crimsonback at Life Magic 267 takes 16 to 33 and lands 99 times in 100; the drain goes while it takes at
/// least what Harm Other would and never when she is unhurt; with the dats, the SpellTable holds the line under the ids the fight casts, as a Boost at another with no projectile. The fight's side is
/// <c>FightScenarioTests.LifeThrough</c>.
/// </summary>
public sealed class LifeThroughRuleTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static SpellInfo Drain(uint id, uint power, uint mana) =>
        WorldActionRig.Spell(id, SpellType.Transfer, SpellFlags.Resistable) with { Name = "Drain Health Other", School = MagicSchool.LifeMagic, Power = power, BaseMana = mana, BaseRangeConstant = 20f, BaseRangeMod = 0f };

    private static readonly DrainLevel DrainOne = new(Drain(1237, 38, 10), DrainPicker.RowOf(1237)!.Value, 1.0);
    private static readonly DrainLevel DrainFour = new(Drain(1240, 188, 40), DrainPicker.RowOf(1240)!.Value, 1.0);
    private static readonly HarmLevel HarmFour = HarmOtherRows.RowOf(1174)!.Value;

    private static DrainSituation Hurt(uint missing, uint targetHealth) => new(170 - missing, 170, 400, 267, 267, targetHealth, 94, 2.5);

    [Fact]
    public void The_rows_are_the_worlds_one_a_level_and_the_same_the_picker_holds()
    {
        Assert.Equal(VitalsMeans.HarmOther, HarmOtherRows.Rows.Select(r => r.SpellId));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], HarmOtherRows.Rows.Select(r => r.Level));
        Assert.Equal([(-4, -2), (-7, -5), (-10, -9), (-16, -17), (-24, -23), (-33, -30), (-42, -37)], HarmOtherRows.Rows.Select(r => (r.Boost, r.BoostVariance))); // SELECT boost, boost_Variance FROM spell, the read-only world copy
        for (var level = 1; level <= 5; level++)
        {
            var effect = FightDebuffs.EffectOf(DebuffKind.HarmOther, level)!.Value;
            Assert.Equal((effect.Boost, effect.BoostVariance), (HarmOtherRows.Rows[level - 1].Boost, HarmOtherRows.Rows[level - 1].BoostVariance));
        }

        foreach (var spell in FightDebuffs.Spells.Where(s => s.Kind == DebuffKind.HarmOther))
        {
            var row = HarmOtherRows.RowOf(spell.Id)!.Value;
            Assert.Equal((spell.Level, spell.Boost, spell.BoostVariance), (row.Level, row.Boost, row.BoostVariance));
        }

        Assert.Null(HarmOtherRows.RowOf(8)); // Harm Self is another line
        Assert.Equal("Harm Other", VitalsMeans.NameOf(VitalsLine.HarmOther));
        Assert.Same(VitalsMeans.HarmOther, VitalsMeans.IdsOf(VitalsLine.HarmOther));
    }

    [Fact]
    public void Harm_Other_IV_on_a_Tusker_Crimsonback_at_Life_Magic_267_takes_16_to_33_and_lands_99_in_100()
    {
        var crimsonback = GroundCatalog.Get("tusker-cave").Targets.First(t => t.Wcid == 1627);
        Assert.Equal((110, 94), (crimsonback.MaxHealth, crimsonback.MagicDefence)); // weenie 1627: health 110; Magic Defense 94 as ACE computes it (the skill row's 88 plus (Focus 20 + Self 20) / 7)

        Assert.Equal((16, 33, 24.5), (HarmFour.MinHarm, HarmFour.MaxHarm, HarmFour.MeanHarm)); // a whole number from boost to boost + boost_Variance, both ends in; the Crimsonback's ResistHealthDrain is 1
        Assert.Equal(0.9945, DrainPicker.PassChance(267, 94), 4); // the resist roll, the 0.03 factor
        Assert.Equal(0.9954, DrainPicker.PassChance(267, 88), 4); // against the bare skill row
        Assert.Equal(0.9984, GearCasts.Chance(267, 175), 4); // the cast itself, the 0.07 factor, at the SpellTable's power 175
        var land = LifeThroughRule.LandChance(267, 175, 94);
        Assert.Equal(0.9929, land, 4);
        Assert.Equal(24.3, land * HarmFour.MeanHarm, 1); // expected a cast
        Assert.Equal((4, 7), ((int)Math.Ceiling(110.0 / HarmFour.MaxHarm), (int)Math.Ceiling(110.0 / HarmFour.MinHarm))); // landed casts for the 110: four at best, seven at worst
        Assert.Equal(4.5, 110 / HarmFour.MeanHarm, 1);
    }

    [Fact]
    public void A_drain_takes_a_share_of_what_the_target_has_left_scaled_to_what_she_is_missing()
    {
        Assert.Equal(28u, LifeThroughRule.DrainTakes(in DrainFour, Hurt(60, 110))); // a quarter of 110, rounded
        Assert.Equal(28u, LifeThroughRule.DrainTakes(in DrainOne, Hurt(60, 110))); // level I takes the same and gives her twice as much
        Assert.Equal(20u, LifeThroughRule.DrainTakes(in DrainFour, Hurt(60, 82))); // 20.5 rounds to the even 20, as ACE's Math.Round does
        Assert.Equal(4u, LifeThroughRule.DrainTakes(in DrainFour, Hurt(4, 110))); // she is missing 4: the transfer is scaled to it
        Assert.Equal(2u, LifeThroughRule.DrainTakes(in DrainOne, Hurt(4, 110))); // level I gives 2 for 1: 2 taken fill her 4
        Assert.Equal(0u, LifeThroughRule.DrainTakes(in DrainFour, Hurt(0, 110))); // unhurt: a drain takes nothing at all
        Assert.Equal(0u, LifeThroughRule.DrainTakes(in DrainFour, Hurt(60, 1))); // a quarter of 1 rounds to nothing: it cannot finish a creature
    }

    [Fact]
    public void The_drain_goes_while_it_takes_at_least_what_Harm_Other_would_then_Harm_Other_finishes()
    {
        var harmChance = LifeThroughRule.LandChance(267, 175, 94);
        var picked = new DrainChoice(0, 55.6, 0.9929, 10, 56, DrainWhy.Best);
        var none = new DrainChoice(-1, 0, 0, 0, 0, DrainWhy.NothingWorthACast);

        var full = LifeThroughRule.Choose(in picked, 28, HarmFour, harmChance, 110); // 27.8 against 24.3
        Assert.Equal((LifeThroughSpell.Drain, LifeThroughRule.DrainTakesMore), (full.Spell, full.Why));
        Assert.Equal((27.8, 24.3), (Math.Round(full.DrainTakes, 1), Math.Round(full.HarmTakes, 1)));

        var lower = LifeThroughRule.Choose(in picked, 20, HarmFour, harmChance, 82); // 19.9 against 24.3
        Assert.Equal((LifeThroughSpell.Harm, LifeThroughRule.HarmTakesMore), (lower.Spell, lower.Why));

        var tie = LifeThroughRule.Choose(new DrainChoice(0, 10, 0.5, 10, 20, DrainWhy.Best), 20, new HarmLevel(1, 7, -10, 0), 1.0, 110); // 10 against 10: the drain, which heals her as well
        Assert.Equal(LifeThroughSpell.Drain, tie.Spell);

        var last = LifeThroughRule.Choose(in none, 0, HarmFour, harmChance, 10); // Harm Other takes no more than is left
        Assert.Equal((LifeThroughSpell.Harm, LifeThroughRule.HarmAlone, 9.9), (last.Spell, last.Why, Math.Round(last.HarmTakes, 1)));

        var noHarm = LifeThroughRule.Choose(in picked, 28, null, 0, 110);
        Assert.Equal((LifeThroughSpell.Drain, LifeThroughRule.DrainAlone), (noHarm.Spell, noHarm.Why));

        var neither = LifeThroughRule.Choose(in none, 0, null, 0, 110);
        Assert.Equal((LifeThroughSpell.None, LifeThroughRule.Neither, 0.0, 0.0), (neither.Spell, neither.Why, neither.DrainTakes, neither.HarmTakes));
    }

    [RequiresDatsFact]
    public void The_SpellTable_holds_Harm_Other_under_the_ids_the_fight_casts_a_Boost_at_another_with_no_projectile()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        Assert.Equal(["Harm Other I", "Harm Other II", "Harm Other III", "Harm Other IV", "Harm Other V", "Harm Other VI", "Heart Rend"], VitalsMeans.HarmOther.Select(id => spells[id].Name));
        Assert.Equal(HarmPowers, VitalsMeans.HarmOther.Select(id => spells[id].Power));
        Assert.Equal(HarmMana, VitalsMeans.HarmOther.Select(id => spells[id].BaseMana));
        foreach (var id in VitalsMeans.HarmOther)
        {
            var info = spells[id];
            Assert.Equal((MagicSchool.LifeMagic, SpellType.Boost), (info.School, info.MetaSpellType)); // a Boost: ACE's HandleCastSpell_Boost, no projectile, so no line is asked
            Assert.Equal(SpellFlags.Resistable, info.Flags & (SpellFlags.Resistable | SpellFlags.SelfTargeted | SpellFlags.Beneficial)); // at another, harmful, and the target may resist
            Assert.True(FightScenario.CastRange(info, 267) >= 5, $"{info.Name} reaches {FightScenario.CastRange(info, 267)} m");
        }
    }

    private static readonly uint[] HarmPowers = [1, 75, 125, 175, 225, 275, 325];
    private static readonly uint[] HarmMana = [10, 20, 30, 40, 50, 60, 70];
}
