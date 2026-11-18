using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 AD (the dance needs no Mana Conversion: ACE only discounts the cost with it, <c>Creature_Magic.cs:41-44</c> @95ef426d; r3d, level 10: dead at 8 mana with 52 stamina unused): Stamina to Mana Self in the spell plan. What these prove: the line stands right
/// after the bolt, its level is the one that nets the most at the stamina's maximum and not the highest at the 95% rule, it is bought from the first trip, a stamina too low to net anything buys nothing, Revitalize Self stays out because it costs more mana than a press
/// gives back for its stamina, and BSD Mage's den names no rung, so his plan is unchanged. M6 14 (the owner, 2026-11-14: at her level the potions go; with Mana Conversion trained, Revitalize Self then Stamina to Mana Self restores her mana): the plan
/// weighs that round live (<see cref="ScrivenerLearnPlan.RevitalizeTarget"/>): at her level-68 shape it takes Revitalize Self at the level whose round nets the most, and at a level-10 shape it leaves the line off and says why.
/// </summary>
public sealed class DancePlanTests(ITestOutputHelper output)
{
    private const uint PressI = 1676;
    private const uint PressII = 1677;
    private const string Burrow = "tusker-lodge"; // M6 7 AC: the rung these tests read is the Lodge now (the owner: the Burrow is no rung)
    private const string Outskirts = "holtburg-outskirts";
    private const string Line = "Stamina to Mana Self";
    private const string Revitalize = "Revitalize Self";
    private const string Holding = "tusker-holding"; // M6 14: her rung at level 68 (ladder19, 2026-11-15)

    private static Spellbook Book(params uint[] spells)
    {
        var book = new Spellbook { IsKnown = true };
        foreach (var spell in spells)
        {
            book.Learn(spell);
        }

        return book;
    }

    private static PlayerQualities Mage(int skill, uint stamina, int level = 10)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Ints[PlayerQualities.LevelProperty] = level;
        q.Attributes[MageTemplate.Focus] = new AttributeDesc(MageTemplate.Focus, 0, 100, 0);
        q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 0, 100, 0);
        q.Skills[MageTemplate.WarMagic] = new SkillDesc(MageTemplate.WarMagic, (ushort)(skill - 50), 1, 2u, 0, 0u, 0, 0);
        q.Skills[MageTemplate.LifeMagic] = new SkillDesc(MageTemplate.LifeMagic, (ushort)(skill - 50), 1, 2u, 0, 0u, 0, 0);
        q.Vitals[MageTemplate.MaxStamina] = new VitalDesc(MageTemplate.MaxStamina, 0, stamina, 0, stamina);
        return q;
    }

    private static LearnNeed Need(string key, int skill, uint stamina, Spellbook book, int level = 10)
    {
        var ladder = LadderLoader.WarMage;
        var rung = ScrivenerLearnPlan.RungOf(ladder, key, level)!;
        return ScrivenerLearnPlan.Needs(GroundCatalog.Get(key), rung, ladder.PlanCastChance, Mage(skill, stamina, level), book, ScrivenerCatalog.Default).Single(n => n.Line.Name == Line);
    }

    /// <summary>
    /// M6 14: her live shape (ladder19, 2026-11-15 00:09 UTC, level 68): Life Magic 354 standing (ranks 304 over (Focus 100 + Self 100) / 4), Mana Conversion 264 (ranks 231 over (100 + 100) / 6 = 33), stamina maximum 287, mana maximum 391 (start 291 and
    /// Self 100); <paramref name="manaConversion"/> false leaves the skill untrained.
    /// </summary>
    private static PlayerQualities Her(bool manaConversion = true)
    {
        var q = Mage(354, 287, level: 68);
        q.Skills[MageTemplate.ManaConversion] = new SkillDesc(MageTemplate.ManaConversion, 231, 1, manaConversion ? 2u : 1u, 0, 0u, 0, 0);
        q.Vitals[MageTemplate.MaxMana] = new VitalDesc(MageTemplate.MaxMana, 0, 291, 0, 391);
        return q;
    }

    /// <summary>The plan's answer for Revitalize Self on <paramref name="key"/>, read as the trip and the counters read it (the ladder given: the standing skills).</summary>
    private static LearnNeed RevitalizeNeed(string key, PlayerQualities q, Spellbook book)
    {
        var ladder = LadderLoader.WarMage;
        var rung = ScrivenerLearnPlan.RungOf(ladder, key, q.Level)!;
        return ScrivenerLearnPlan.Needs(GroundCatalog.Get(key), rung, ladder.PlanCastChance, q, book, ScrivenerCatalog.Default, ladder).Single(n => n.Line.Name == Revitalize);
    }

    [Fact]
    public void The_line_stands_after_the_bolt_and_the_den_is_unchanged()
    {
        var rung = ScrivenerLearnPlan.RungOf(LadderLoader.WarMage, Burrow, 15)!;
        Assert.Equal(["Flame Bolt", Line, Revitalize, "Focus Self"], ScrivenerLearnPlan.LinesFor(rung, ScrivenerCatalog.Default).Take(4).Select(l => l.Name)); // M6 7 AK: the self buffs follow the dance; M6 14: Revitalize Self right after the press
        Assert.Equal(ScrivenerLineKind.Dance, ScrivenerCatalog.Default.Line(Line)!.Kind);
        Assert.Null(ScrivenerLearnPlan.RungOf(LadderLoader.WarMage, "black-spawn-den-aluvian", 80));
    }

    [Theory]
    [InlineData(118, 50u, 1)] // level 10: I nets 12.5, II 5
    [InlineData(143, 52u, 1)] // III castable at 95%: it nets -2, II 6, I 13
    [InlineData(118, 109u, 1)] // I 35 at its cap of 50 drained, II 34.5
    [InlineData(118, 111u, 2)] // II 35.5
    [InlineData(143, 200u, 2)] // II and III tie at 80: the lower
    [InlineData(143, 220u, 3)]
    public void The_level_is_the_one_that_nets_the_most_at_the_stamina_not_the_highest_at_the_rule(int skill, uint stamina, int level)
    {
        var need = Need(Burrow, skill, stamina, Book(81));
        Assert.Equal(level, need.Target);
        Assert.Equal(LearnNeedKind.Due, need.Kind);
    }

    [Fact]
    public void The_95_rule_still_caps_the_level()
    {
        Assert.Equal(1, Need(Burrow, 90, 300, Book(81)).Target); // power 50 needs 93: II is out of reach whatever the stamina
        Assert.Equal(2, Need(Burrow, 93, 300, Book(81)).Target);
    }

    [Fact]
    public void A_stamina_that_nets_nothing_buys_nothing()
    {
        var need = Need(Outskirts, 60, 22, Book(), level: 2); // 11 x 0.9 = 10 for 10
        Assert.Equal((LearnNeedKind.NoLevel, 0), (need.Kind, need.Target));
        Assert.Null(need.Spell);
    }

    [Fact]
    public void From_the_first_trip_I_is_due_at_fifty_pyreals_and_known_it_is_not_bought_again()
    {
        var need = Need(Outskirts, 60, 40, Book(), level: 6);
        Assert.Equal((LearnNeedKind.Due, PressI), (need.Kind, need.Spell!.SpellId));
        Assert.Equal(50, GroundCatalog.Get(Outskirts).PriceOf(need.Spell.ScrollWcid));
        Assert.Contains("nets the most", need.Why, StringComparison.Ordinal);
        Assert.Equal(LearnNeedKind.Known, Need(Outskirts, 60, 40, Book(PressI), level: 6).Kind);
        Assert.Equal(PressII, ScrivenerCatalog.Default.LineSpells(Line)[1].SpellId);
    }

    [Fact]
    public void A_stamina_not_read_buys_the_cheapest_press()
    {
        var spells = ScrivenerCatalog.Default.LineSpells(Line);
        var q = new PlayerQualities { IsKnown = true };
        Assert.Equal(1, ScrivenerLearnPlan.DanceTarget(spells, 3, q, out var note));
        Assert.Contains("not read", note, StringComparison.Ordinal);
        Assert.Equal(0, ScrivenerLearnPlan.DanceTarget(spells, 0, q, out _));
    }

    [Fact]
    public void Revitalize_Self_does_not_pay_a_stamina_point_costs_more_mana_than_a_press_gives_for_it()
    {
        // Mana a stamina point at the Revitalize's mean (ACE: boost + variance / 2; the SpellTable's base mana) against the mana a press gives for it (1 - loss). I: 15 mana for a mean of 25; II 30 for 32.5; III 40 for 45; IV 50 for 60.
        (uint Mana, int Level)[] rows = [(15, 0), (30, 1), (40, 2), (50, 3)];
        foreach (var (mana, level) in rows)
        {
            var boost = BuffStatMods.Revitalize[level];
            var manaForStamina = (1.0 - BuffStatMods.StaminaToMana[level].LossPercent) * BuffStatMods.StaminaToMana[level].Proportion / BuffStatMods.StaminaToMana[level].Proportion; // a drained point gives 1 - loss
            Assert.True(mana / (boost.Boost + (boost.Variance / 2.0)) > 0.5 * manaForStamina, $"level {level + 1}"); // and a press drains half the bar, so a Revitalized point pays half of that a press
        }

        Assert.Equal(ScrivenerLineKind.Revitalize, ScrivenerCatalog.Default.Line(Revitalize)!.Kind); // M6 14: listed, and the plan weighs the round live (RevitalizeTarget): at level 10 it stays off (below)
    }

    [Fact]
    public void M6_14_at_her_level_68_shape_Revitalize_Self_joins_the_plan_at_the_level_whose_round_nets_the_most()
    {
        var presses = ScrivenerCatalog.Default.LineSpells(Line);
        var need = RevitalizeNeed(Holding, Her(), Book([.. presses.Select(s => s.SpellId)])); // her book: Stamina to Mana Self I to VI, no Revitalize Self
        output.WriteLine(need.Why);
        Assert.Equal((LearnNeedKind.OffHome, 6, 1182u), (need.Kind, need.Target, need.Spell!.SpellId)); // VI: sold only at the Grand Master Scriveners' towns, not at the home Holtburg
        Assert.Contains("Stonehold", need.Towns);
        Assert.Contains("LifeMagic skill 354 (standing)", need.Why, StringComparison.Ordinal);
        Assert.Contains("a round on Mana Conversion 264 at stamina 287 (the standing maximum): Revitalize Self VI restores 120 stamina", need.Why, StringComparison.Ordinal);
        Assert.Contains("mana a round, the most", need.Why, StringComparison.Ordinal);

        // the rule's own numbers: VI's round nets more than V's, and more than nothing
        var spells = ScrivenerCatalog.Default.LineSpells(Revitalize);
        Assert.Equal(6, ScrivenerLearnPlan.RevitalizeTarget(spells, 6, presses, 6, Her(), out var note));
        Assert.Equal(5, ScrivenerLearnPlan.RevitalizeTarget(spells, 5, presses, 6, Her(), out _)); // capped at V by the chance: V nets too
        Assert.Contains("Stamina to Mana Self VI nets", note, StringComparison.Ordinal);

        // read: the plan holds it, and nothing more is bought
        Assert.Equal(LearnNeedKind.Known, RevitalizeNeed(Holding, Her(), Book([.. presses.Select(s => s.SpellId), 1182u])).Kind);
    }

    [Fact]
    public void M6_14_at_a_level_10_shape_Revitalize_Self_stays_off_and_the_line_says_why()
    {
        var need = Need(Burrow, 118, 50, Book(81)); // no Mana Conversion: every cast costs its whole base
        var off = ScrivenerLearnPlan.Needs(GroundCatalog.Get(Burrow), ScrivenerLearnPlan.RungOf(LadderLoader.WarMage, Burrow, 10)!, LadderLoader.WarMage.PlanCastChance, Mage(118, 50), Book(81), ScrivenerCatalog.Default).Single(n => n.Line.Name == Revitalize);
        output.WriteLine(off.Why);
        Assert.Equal(LearnNeedKind.Due, need.Kind); // the press is still bought
        Assert.Equal((LearnNeedKind.NoLevel, 0), (off.Kind, off.Target));
        Assert.Null(off.Spell);
        Assert.Contains("no round nets mana on Mana Conversion 0 at stamina 50", off.Why, StringComparison.Ordinal);
        Assert.Contains("Revitalize Self stays off", off.Why, StringComparison.Ordinal);

        // and with her stamina but no Mana Conversion the round loses too: the skill is what pays it
        Assert.Equal(0, ScrivenerLearnPlan.RevitalizeTarget(ScrivenerCatalog.Default.LineSpells(Revitalize), 6, ScrivenerCatalog.Default.LineSpells(Line), 6, Her(manaConversion: false), out var untrained));
        output.WriteLine(untrained);
    }

    [Theory]
    [InlineData(Outskirts, 1)]
    [InlineData(Burrow, 4)]
    public void The_low_grounds_stock_the_press_with_no_trip_and_no_stop(string key, int keep)
    {
        var supplies = GroundCatalog.Get(key).Supplies;
        foreach (var wcid in new uint[] { 778, 759 }) // Saffron, Quicksilver: only the press uses them
        {
            var line = supplies.StockOf(wcid)!;
            Assert.Equal((keep, 0, 0), (line.Keep, line.TripUnder, line.StopUnder));
        }
    }
}
