using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 X (spells are the armor; r2b: level 10, 49 health, only Armor Self I, dead to a Female Tusker in 5 s): the protective lines in the spell plan. What these prove: what a line takes off a hit is ACE's <c>CalcArmorMod</c> times the
/// protection's multiplier; the plan learns Armor Self and the protection line of the damage the ground's targets carry (data), by the same 95% rule on the current skill, after the bolt, before the debuffs, in the order of value per coin, and
/// the recovery line last; a line the skill cannot cast at 0.95 waits; the outskirts name no damage type, so they learn Armor Self alone; BSD Mage's den names no rung, so his plan is unchanged.
/// </summary>
public sealed class ProtectivePlanTests
{
    private const uint ArmorI = 24;
    private const uint ArmorII = 1308;
    private const uint ArmorIII = 1309;
    private const uint BludgeonII = 1019;
    private const uint BludgeonIII = 1020;
    private const uint ManaRenewalII = 213;
    /// <summary>M6 7 AC: the ground of the protection plan is the Lodge at level 15 (the Burrow rung is dropped and the outskirts name no damage type); the Lodge's Male Tuskers deal bludgeon as the Burrow's Female Tuskers did.</summary>
    private const string Burrow = "tusker-lodge";

    private static Spellbook Book(params uint[] spells)
    {
        var book = new Spellbook { IsKnown = true };
        foreach (var spell in spells)
        {
            book.Learn(spell);
        }

        return book;
    }

    /// <summary>A climber of <paramref name="level"/>, Focus and Self 100, War Magic and Life Magic trained at <paramref name="skill"/> ((100 + 100) / 4 + ranks).</summary>
    private static PlayerQualities Mage(int skill, int level = 15)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Ints[PlayerQualities.LevelProperty] = level;
        q.Attributes[MageTemplate.Focus] = new AttributeDesc(MageTemplate.Focus, 0, 100, 0);
        q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 0, 100, 0);
        q.Skills[MageTemplate.WarMagic] = new SkillDesc(MageTemplate.WarMagic, (ushort)(skill - 50), 1, 2u, 0, 0u, 0, 0);
        q.Skills[MageTemplate.LifeMagic] = new SkillDesc(MageTemplate.LifeMagic, (ushort)(skill - 50), 1, 2u, 0, 0u, 0, 0);
        return q;
    }

    private static IReadOnlyList<LearnNeed> Needs(string key, int skill, Spellbook book, int level = 15)
    {
        var ground = GroundCatalog.Get(key);
        var ladder = LadderLoader.WarMage;
        var rung = ScrivenerLearnPlan.RungOf(ladder, key, level)!;
        return ScrivenerLearnPlan.Needs(ground, rung, ladder.PlanCastChance, Mage(skill, level), book, ScrivenerCatalog.Default);
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(20, 0.7692)]
    [InlineData(50, 0.5714)]
    [InlineData(75, 0.4706)]
    [InlineData(100, 0.4)]
    [InlineData(150, 0.3077)]
    [InlineData(200, 0.25)]
    public void Armor_is_ACEs_CalcArmorMod(double armorLevel, double factor) => Assert.Equal(factor, ProtectiveValue.ArmorFactor(armorLevel), 4); // SkillFormula.cs:29-37 @95ef426d

    /// <summary>
    /// The table of the doc notes (D28: the hit is <c>ClimberHealthRule</c>'s mean unarmoured hit, the ladder's <c>health.hitter</c>): a Female Tusker's 34, a Male Tusker's 36. Spell ids and multipliers are ACE's spell table (<see cref="BuffStatMods"/>):
    /// Armor Self I to VI 24, 1308-1312; Bludgeoning Protection Self I to VI 1018-1023.
    /// </summary>
    [Theory]
    [InlineData(34, 24u, 0u, 26.2)] // r2b: Female, Armor Self I
    [InlineData(34, 1308u, 1019u, 15.5)] // level 10: II and II
    [InlineData(34, 1309u, 1020u, 10.7)] // level 15-17: III and III
    [InlineData(34, 1310u, 1021u, 7.8)]
    [InlineData(36, 24u, 0u, 27.7)] // Male
    [InlineData(36, 1308u, 1019u, 16.5)]
    [InlineData(36, 1309u, 1020u, 11.4)]
    public void A_tuskers_hit_falls_by_the_two_lines_together(double hit, uint armor, uint protection, double after) =>
        Assert.Equal(after, ProtectiveValue.Hit(hit, armor, protection), 1);

    [Fact]
    public void The_hits_in_the_table_are_the_ladders_hitters()
    {
        var lodge = LadderLoader.WarMage.Rungs.Single(r => r.Key == "lodge").Health!.Hitter;
        Assert.Equal(34, LadderLoader.WarMage.Rungs.Single(r => r.Key == "burrow").Health!.Hitter.Hit); // M6 7 AR: the Female Tusker, the rung is back
        Assert.Equal(("Male Tusker", 36), (lodge.Name, lodge.Hit));
    }

    [Fact]
    public void The_ground_targets_damage_types_name_the_protection_line_and_an_avoided_creature_does_not_count()
    {
        Assert.Equal(["bludgeon"], ScrivenerLearnPlan.IncomingElements(GroundCatalog.Get(Burrow)));
        Assert.Equal(["bludgeon"], ScrivenerLearnPlan.IncomingElements(GroundCatalog.Get("tusker-temple-20-40"))); // the Shrine Statue's 0x0048 is role avoid
        Assert.Empty(ScrivenerLearnPlan.UncoveredElements(GroundCatalog.Get(Burrow), ScrivenerCatalog.Default));
        Assert.Empty(ScrivenerLearnPlan.IncomingElements(GroundCatalog.Get("holtburg-outskirts"))); // TODO(m6-outskirts-damage-types): the outdoor creatures carry no damage_types
    }

    [Fact]
    public void The_lines_stand_bolt_then_armor_and_protection_then_debuffs_then_the_recovery_line()
    {
        var rung = ScrivenerLearnPlan.RungOf(LadderLoader.WarMage, Burrow, 15)!;
        var names = ScrivenerLearnPlan.LinesFor(rung, ScrivenerCatalog.Default).Where(l => l.Kind is not (ScrivenerLineKind.SelfBuff or ScrivenerLineKind.ItemArmor)).Select(l => l.Name); // M6 7 AK's lines stand after the dance and after the protections: BuffBootstrapPlanTests
        Assert.Equal(["Flame Bolt", "Stamina to Mana Self", "Revitalize Self", "Armor Self", "Bludgeoning Protection Self", "Lifestone Recall", "Lifestone Tie", "Drain Health Other", "Drain Stamina Other", "Heal Self", "Harm Other", "Mana Renewal Self", "Regeneration Self", "Rejuvenation Self", "Spirit Drinker", "Hermetic Link", "Defender", "Fire Vulnerability Other", "Imperil Other", "Vulnerability Other", "Magic Yield Other"], names); // M6 7 AD: the press after the bolt; M6 14: Revitalize Self right after the press; M6 14 (m6-life-through): Harm Other with the drains she fights with; M6 14 (m6-caster): the caster auras before the debuffs

        var outskirts = ScrivenerLearnPlan.RungOf(LadderLoader.WarMage, "holtburg-outskirts", 6)!;
        Assert.DoesNotContain("Bludgeoning Protection Self", ScrivenerLearnPlan.LinesFor(outskirts, ScrivenerCatalog.Default).Select(l => l.Name));
        Assert.Null(ScrivenerLearnPlan.RungOf(LadderLoader.WarMage, "black-spawn-den-aluvian", 80)); // BSD Mage's den: no rung names it, so his plan is the ground's own list, unchanged
    }

    [Fact]
    public void At_level_15_Life_118_buys_Armor_II_and_the_protection_II_in_the_order_of_value_and_III_waits_for_the_95_rule()
    {
        var needs = Needs(Burrow, 118, Book(81, ArmorI));
        var armor = needs.Single(n => n.Line.Name == "Armor Self");
        var protection = needs.Single(n => n.Line.Name == "Bludgeoning Protection Self");
        Assert.Equal((LearnNeedKind.Due, ArmorII), (armor.Kind, armor.Spell!.SpellId));
        Assert.Equal((LearnNeedKind.Due, BludgeonII), (protection.Kind, protection.Spell!.SpellId));
        Assert.Equal(["Flame Bolt", "Stamina to Mana Self", "Armor Self", "Bludgeoning Protection Self", "Lifestone Recall"], needs.Where(n => n.Line.Kind is not (ScrivenerLineKind.SelfBuff or ScrivenerLineKind.ItemArmor or ScrivenerLineKind.Sustain or ScrivenerLineKind.Recovery or ScrivenerLineKind.Revitalize)).Take(5).Select(n => n.Line.Name)); // M6 14: Revitalize Self stands after the press, off here (no Mana Conversion: DancePlanTests) // Armor II from I: log(0.769 / 0.571) = 0.297; the protection II from none: log(1 / 0.8) = 0.223; 250 each

        Assert.Equal(ArmorII, Needs(Burrow, 142, Book(81, ArmorI)).Single(n => n.Line.Name == "Armor Self").Spell!.SpellId); // 42 over power 100: 0.9498, III waits
        Assert.Equal(ArmorIII, Needs(Burrow, 143, Book(81, ArmorII)).Single(n => n.Line.Name == "Armor Self").Spell!.SpellId);
        Assert.Equal(LearnNeedKind.Known, Needs(Burrow, 142, Book(81, ArmorII)).Single(n => n.Line.Name == "Armor Self").Kind);
    }

    [Fact]
    public void The_value_per_coin_is_the_order_when_the_prices_differ()
    {
        // Armor II held, its III at 1,000 (log(0.571 / 0.471) = 0.194) against the protection from nothing at III, also 1,000 (log(1 / 0.67) = 0.400): the first step of a line is worth more.
        var needs = Needs(Burrow, 143, Book(ArmorII));
        Assert.Equal(["Bludgeoning Protection Self", "Armor Self"], needs.Where(n => n.Line.Kind is ScrivenerLineKind.Armor or ScrivenerLineKind.Protection).Select(n => n.Line.Name));
        var line = ScrivenerCatalog.Default.LineSpells("Armor Self");
        Assert.Equal(0.1942, ProtectiveValue.Gain(line, line[2], Book(ArmorII)), 4);
        Assert.Equal(0.2624, ProtectiveValue.Gain(line, line[0], Book()), 4);
        Assert.Equal(0, ProtectiveValue.Gain(line, line[1], Book(ArmorII)));
        Assert.Equal(0, ProtectiveValue.Gain(line, line[1], Book(ArmorIII)));
    }

    [Fact]
    public void An_untrained_school_learns_no_protective_line_and_the_recovery_line_is_planned_like_any_other()
    {
        var q = Mage(118);
        q.Skills.Remove(MageTemplate.LifeMagic);
        var ground = GroundCatalog.Get(Burrow);
        var rung = ScrivenerLearnPlan.RungOf(LadderLoader.WarMage, Burrow, 15)!;
        var needs = ScrivenerLearnPlan.Needs(ground, rung, 0.95, q, Book(81), ScrivenerCatalog.Default);
        Assert.Equal(LearnNeedKind.Untrained, needs.Single(n => n.Line.Name == "Armor Self").Kind);

        var renewal = Needs(Burrow, 118, Book(81, ArmorII, BludgeonII)).Single(n => n.Line.Name == "Mana Renewal Self");
        Assert.Equal((LearnNeedKind.Due, ManaRenewalII), (renewal.Kind, renewal.Spell!.SpellId));
        Assert.Equal(BludgeonIII, ScrivenerCatalog.Default.LineSpells("Bludgeoning Protection Self")[2].SpellId);
    }

    [Fact]
    public void Every_protective_scroll_of_the_plan_is_sold_on_foot_in_Holtburg_to_level_IV()
    {
        var holtburg = GroundCatalog.Get(Burrow);
        foreach (var name in new[] { "Armor Self", "Bludgeoning Protection Self", "Mana Renewal Self" })
        {
            var line = ScrivenerCatalog.Default.LineSpells(name);
            Assert.Equal(6, line.Count);
            foreach (var spell in line.Take(4))
            {
                Assert.Equal(spell.ScrollValue * 50, holtburg.PriceOf(spell.ScrollWcid)); // ACE's GetSellCost at SellPrice 50
            }
        }
    }
}
