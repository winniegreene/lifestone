using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 1 e (D4, the VII rule): which found scrolls are kept to be read. What these prove, on the outskirts rung (fire, the war line from the profile) with a scripted book and skills: a VII Flame Bolt (power 300, which ACE
/// <c>CanReadScroll</c> lets anyone read) is kept at any War Magic; a VI (power 250) is kept only when the current War Magic is within 50 of it; a spell the book holds, a level under the book's highest, a scroll of another school's line
/// or of another element's line, a Writable with no spell id and an item that is no scroll are not kept; an unread book keeps nothing; a VII debuff of the plan's lines (Imperil Other VII) is kept, and the hook is the delegate the
/// loot thresholds take (a reason for a kept scroll, null for the rest, the book and skills read at each call).
/// </summary>
public sealed class ScrollKeepRuleTests
{
    private const uint FlameBoltI = 27;
    private const uint FlameBoltII = 81;
    private const uint FlameBoltIII = 82;
    private const uint FlameBoltIV = 83;
    private const uint FlameBoltV = 84;
    private const uint FlameBoltVI = 85;
    private const uint FlameBoltVII = 2128;
    private const uint ImperilOtherVII = 2074;
    private const uint WritableType = 0x2000;
    private const string Outskirts = "holtburg-outskirts";

    private static readonly LadderRung Rung = ScrivenerLearnPlan.RungOf(LadderLoader.WarMage, Outskirts, 6)!;

    private static Spellbook Book(bool known, params uint[] spells)
    {
        var book = new Spellbook { IsKnown = known };
        foreach (var spell in spells)
        {
            book.Learn(spell);
        }

        return book;
    }

    /// <summary>A character of <paramref name="level"/> with Focus and Self 100 and War Magic trained with <paramref name="warRanks"/> ranks (current skill 50 + ranks); Life Magic untrained.</summary>
    private static PlayerQualities Mage(int warRanks, int level = 6)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Ints[PlayerQualities.LevelProperty] = level;
        q.Attributes[MageTemplate.Focus] = new AttributeDesc(MageTemplate.Focus, 0, 100, 0);
        q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 0, 100, 0);
        q.Skills[MageTemplate.WarMagic] = new SkillDesc(MageTemplate.WarMagic, (ushort)warRanks, 1, 2u, 0, 0u, 0, 0);
        return q;
    }

    internal static WorldObject Scroll(uint guid, string name, uint? spellId, uint itemType = WritableType)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name, itemType: itemType);
        return new WorldObject(basis with { Weenie = basis.Weenie with { SpellId = (ushort?)spellId, WeenieClassId = 1000 + guid } }, isSelf: false, createdAt: 0);
    }

    private static ScrollKeepVerdict Check(WorldObject item, Spellbook book, PlayerQualities q) => ScrollKeepRule.Evaluate(item, ScrollKeepRule.PlanLines(Rung, null, ScrivenerCatalog.Default), book, q, LadderLoader.WarMage.PlanCastChance);

    [Theory]
    [InlineData(0)]
    [InlineData(56)]
    [InlineData(400)]
    public void A_VII_Flame_Bolt_is_kept_at_any_War_Magic_because_ACE_lets_a_power_of_300_be_read_by_anyone(int warRanks)
    {
        var verdict = Check(Scroll(1, "Scroll of Ilservian's Flame", FlameBoltVII), Book(true, FlameBoltI), Mage(warRanks));

        Assert.Equal(ScrollKeepKind.Keep, verdict.Kind);
        Assert.Contains("level 7 of the plan's Flame Bolt line, a VII, kept always (D4)", verdict.Reason, StringComparison.Ordinal);
        Assert.Contains("power 300 needs no skill", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_VI_is_kept_only_when_it_is_the_plans_next_level_on_the_current_skill_and_ACE_lets_the_character_read_it()
    {
        var item = Scroll(2, "Scroll of Flame Bolt VI", FlameBoltVI); // power 250 needs 200 (Player.CanReadScroll, Player_Skills.cs:458-471 @95ef426d)
        var book = Book(true, FlameBoltI);
        var powers = ScrollKeepRule.PlanLines(Rung, null, ScrivenerCatalog.Default)[0].Levels.Where(l => l.Level <= 6).Select(l => l.Power).ToList();
        var ranks = Enumerable.Range(0, 800).First(r => ScrivenerLearnPlan.TargetLevel((uint)(50 + r), LadderLoader.WarMage.PlanCastChance, powers, floor: 2) == 6); // the first War Magic whose plan target is VI

        var below = Check(item, book, Mage(ranks - 1)); // the target is V there: a VI is a level above the plan's next
        Assert.Equal(ScrollKeepKind.NotNext, below.Kind);
        Assert.False(below.Keep);

        var next = Check(item, book, Mage(ranks));
        Assert.True(next.Keep);
        Assert.Contains("level 6 of the plan's Flame Bolt line", next.Reason, StringComparison.Ordinal);
        Assert.Contains("the plan's next level on WarMagic skill", next.Reason, StringComparison.Ordinal);

        Assert.Equal(ScrollKeepKind.NotNext, Check(item, book, new PlayerQualities { IsKnown = true }).Kind); // no War Magic at all: the plan has no next level
    }

    [Fact]
    public void Only_the_plans_next_level_is_kept_a_looted_III_or_IV_over_a_next_of_II_is_left_to_the_loot_rules_and_a_VII_is_always_kept_M6_1_fix_1_R3()
    {
        var q = Mage(56); // War Magic 106: the plan's target is II (II at 0.98, III at 0.60: D42's numbers)
        var book = Book(true, FlameBoltI);

        var ii = Check(Scroll(20, "Scroll of Flame Bolt II", FlameBoltII), book, q);
        Assert.Equal(ScrollKeepKind.Keep, ii.Kind); // the plan's next level, readable
        Assert.Contains("the plan's next level", ii.Reason, StringComparison.Ordinal);

        var iii = Check(Scroll(21, "Scroll of Flame Bolt III", FlameBoltIII), book, q);
        Assert.Equal(ScrollKeepKind.NotNext, iii.Kind); // would be read and then hide II from the buy: the loot rules judge it (sold as loot)
        Assert.Contains("the plan's next level on WarMagic skill 106 at chance 0.95 is II", iii.Reason, StringComparison.Ordinal);
        Assert.Equal(ScrollKeepKind.NotNext, Check(Scroll(22, "Scroll of Flame Bolt IV", FlameBoltIV), book, q).Kind);

        Assert.Equal(ScrollKeepKind.Keep, Check(Scroll(23, "Scroll of Ilservian's Flame", FlameBoltVII), book, q).Kind); // D4: a VII always
        Assert.Equal(ScrollKeepKind.Keep, Check(Scroll(24, "Scroll of Ilservian's Flame", FlameBoltVII), Book(true, FlameBoltI, FlameBoltIII), q).Kind); // even when a III is already read
    }

    [Fact]
    public void A_looted_III_read_by_hand_never_hides_the_plans_II_from_the_buy_M6_1_fix_1_R3()
    {
        var home = GroundCatalog.TryGet(Outskirts)!;
        var q = Mage(56);
        var rung = Rung;
        var needs = ScrivenerLearnPlan.Needs(home, rung, LadderLoader.WarMage.PlanCastChance, q, Book(true, FlameBoltI, FlameBoltIII), ScrivenerCatalog.Default);
        var bolt = needs.Single(n => n.Line.Name == "Flame Bolt");
        Assert.Equal((LearnNeedKind.Due, 2), (bolt.Kind, bolt.Target)); // the book holds III, not II: II is still bought (the cap then casts the highest level held at or under it)

        var withII = ScrivenerLearnPlan.Needs(home, rung, LadderLoader.WarMage.PlanCastChance, q, Book(true, FlameBoltI, FlameBoltII), ScrivenerCatalog.Default).Single(n => n.Line.Name == "Flame Bolt");
        Assert.Equal(LearnNeedKind.Known, withII.Kind);
    }

    [Fact]
    public void A_known_spell_or_a_level_under_the_books_highest_is_not_kept()
    {
        var q = Mage(0);

        var known = Check(Scroll(4, "Scroll of Ilservian's Flame", FlameBoltVII), Book(true, FlameBoltI, FlameBoltVII), q);
        Assert.Equal(ScrollKeepKind.NotAbove, known.Kind);
        Assert.Contains("the book holds it already", known.Reason, StringComparison.Ordinal);

        var lower = Check(Scroll(5, "Scroll of Flame Bolt V", FlameBoltV), Book(true, FlameBoltI, FlameBoltVI), q); // V under the book's VI, though V itself is unknown: not the plan's next level (M6 1 fix, 1-R3)
        Assert.Equal(ScrollKeepKind.NotNext, lower.Kind);
        Assert.False(lower.Keep);
    }

    [Fact]
    public void A_scroll_of_another_school_or_another_element_or_no_line_of_the_plan_is_not_kept()
    {
        var q = Mage(0);
        var book = Book(true, FlameBoltI);
        var frostBoltVI = ScrivenerCatalog.Default.LineSpells("Frost Bolt").Last().SpellId; // the cold line is not this rung's element
        var strengthSelfVII = ArenaProfile.LineNamed("Strength Self")!.Spells.First(s => s.Power == 300).Id; // Creature Enchantment, no line the plan learns

        Assert.Equal(ScrollKeepKind.NotPlanned, Check(Scroll(6, "Scroll of Frost Bolt VI", frostBoltVI), book, q).Kind);
        Assert.Equal(ScrollKeepKind.NotPlanned, Check(Scroll(7, "Scroll of Strength Self VII", strengthSelfVII), book, q).Kind);
        Assert.Equal(ScrollKeepKind.NotPlanned, Check(Scroll(8, "Scroll of nothing", 65_000), book, q).Kind);
    }

    [Fact]
    public void A_non_scroll_a_scroll_with_no_spell_and_an_unread_book_keep_nothing()
    {
        var q = Mage(0);
        var book = Book(true, FlameBoltI);

        Assert.Equal(ScrollKeepKind.NotScroll, Check(Scroll(9, "Flame Bolt VII wand", FlameBoltVII, itemType: 0x8000), book, q).Kind); // a caster that names the spell is no scroll
        Assert.Equal(ScrollKeepKind.NoSpell, Check(Scroll(10, "A blank scroll", null), book, q).Kind);
        Assert.Equal(ScrollKeepKind.SpellbookUnread, Check(Scroll(11, "Scroll of Ilservian's Flame", FlameBoltVII), Book(false), q).Kind);
    }

    [Fact]
    public void A_VII_debuff_of_the_plans_lines_is_kept_like_the_bolt_line()
    {
        var verdict = Check(Scroll(12, "Scroll of Gossamer Flesh", ImperilOtherVII), Book(true, FlameBoltI), Mage(0));

        Assert.Equal(ScrollKeepKind.Keep, verdict.Kind);
        Assert.Contains("level 7 of the plan's Imperil Other line, a VII", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hook_gives_the_reason_for_a_kept_scroll_and_null_for_the_rest_reading_the_book_and_skills_at_each_call()
    {
        var book = Book(true, FlameBoltI);
        var q = Mage(0);
        var hook = ScrollKeepRule.Hook(LadderLoader.WarMage, Outskirts, null, () => book, () => q);
        var vii = Scroll(13, "Scroll of Ilservian's Flame", FlameBoltVII);

        Assert.Contains("kept to be read", hook(vii), StringComparison.Ordinal);
        Assert.Null(hook(Scroll(14, "Scroll of Flame Bolt VI", FlameBoltVI))); // War Magic 50: not the plan's next level
        Assert.Null(hook(Scroll(15, "Flame Bolt VII wand", FlameBoltVII, itemType: 0x8000)));
        q = Mage(56);
        Assert.NotNull(hook(Scroll(16, "Scroll of Flame Bolt II", FlameBoltII))); // the skills of the time: War Magic 106, the plan's next is II
        Assert.Null(hook(Scroll(18, "Scroll of Flame Bolt III", FlameBoltIII)));
        book = Book(true, FlameBoltI, FlameBoltVII);
        Assert.Null(hook(vii)); // the book of the time

        Assert.Null(ScrollKeepRule.Hook(LadderLoader.WarMage, "no-such-ground", null, () => book, () => q)(Scroll(17, "Scroll of Ilservian's Flame", FlameBoltVII))); // no rung plays it
    }
}
