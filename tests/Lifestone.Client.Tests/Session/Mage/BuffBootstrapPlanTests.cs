using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 AK (the owner's decisions of 2026-11-06): outside until 16, buff everything and bootstrap, 0.75 is the bar during buffing, Armor Self IV and Bludgeoning Protection Self IV first, a mage keeps pushing for stronger buffs. Proved here on Winnie's numbers (Focus 131, Self 130, Creature 115,
/// Life 140, Item 65 once trained): the bootstrap's skill at each step against ACE's sums; the plan's lines, the first scrolls and the saving line; the experience push paid from the magic share, the health floor untouched; the armor factor; the credit of level 16 and the plan after it.
/// </summary>
public sealed class BuffBootstrapPlanTests
{
    private const string Outskirts = "holtburg-outskirts";

    private static Ladder L => LadderLoader.WarMage;

    private static ScrivenerCatalog C => ScrivenerCatalog.Default;

    private static uint Id(string line, int level) => C.LineSpells(line).Single(s => s.Level == level).SpellId;

    private static Spellbook Book(params (string Line, int Level)[] spells)
    {
        var book = new Spellbook { IsKnown = true };
        foreach (var (line, level) in spells)
        {
            book.Learn(Id(line, level));
        }

        return book;
    }

    /// <summary>Winnie at <paramref name="level"/>; Item trained at 65 when <paramref name="itemTrained"/>. Skills are trained here with their whole margin in ranks (the rule reads base less formula, so it is the same number).</summary>
    private static PlayerQualities Winnie(bool itemTrained = false, int level = 15, int creature = 115, int item = 65)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Ints[PlayerQualities.LevelProperty] = level;
        q.Attributes[MageTemplate.Focus] = new AttributeDesc(MageTemplate.Focus, 31, 100, 0);
        q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 30, 100, 0);
        q.Skills[MageTemplate.CreatureEnchantment] = new SkillDesc(MageTemplate.CreatureEnchantment, (ushort)(creature - 65), 1, 2u, 0, 0u, 0, 0);
        q.Skills[MageTemplate.LifeMagic] = new SkillDesc(MageTemplate.LifeMagic, 75, 1, 2u, 0, 0u, 0, 0);
        q.Skills[MageTemplate.WarMagic] = new SkillDesc(MageTemplate.WarMagic, 66, 1, 2u, 0, 0u, 0, 0);
        q.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, (ushort)(item - 65), 1, itemTrained ? 2u : 1u, 0, 0u, 0, 0);
        return q;
    }

    private static IReadOnlyList<LearnNeed> Needs(PlayerQualities q, Spellbook book, int level = 15)
    {
        var ground = GroundCatalog.Get(Outskirts);
        return ScrivenerLearnPlan.Needs(ground, ScrivenerLearnPlan.RungOf(L, Outskirts, level)!, L.PlanCastChance, q, book, C, L);
    }

    [Fact]
    public void The_bars_are_two_values_and_0_75_is_sixteen_over_the_power()
    {
        Assert.Equal(0.95, L.PlanCastChance); // combat, unchanged
        Assert.Equal(0.75, L.PlanBuffChance);
        Assert.True(SpellPlan.Chance(166, 150) >= 0.75 && SpellPlan.Chance(165, 150) < 0.75);
        Assert.True(SpellPlan.Chance(193, 150) >= 0.95 && SpellPlan.Chance(192, 150) < 0.95); // 43 over, not 42
    }

    [Fact]
    public void M6_14_own_buffs_her_standing_walk_stands_on_the_V_and_VI_her_book_holds()
    {
        // The every-spell rule buys V and VI of her own lines (the catalog of the plan sells them to IV): a book with Focus, Willpower and Creature Enchantment Mastery Self VI alone (ArenaProfile 1426, 1450, 562) stands on them.
        var q = Winnie(itemTrained: true, creature: 280); // VI (power 250) at 0.75 from 266
        var book = new Spellbook { IsKnown = true };
        foreach (var id in new uint[] { 1426, 1450, 562 })
        {
            book.Learn(id);
        }

        var reach = ScrivenerLearnPlan.Standing(q, book, C, L)!;

        Assert.Equal((6, 6, 6), (reach.Focus, reach.Willpower, reach.CreatureMastery));
        Assert.Equal(280u + 18u + 35u, reach.Creature); // (131 + 35 + 130 + 35) / 4 = 83 over the raw 65, and the mastery's 35 (BuffStatMods: VI adds 35)
        Assert.Equal(0, BuffReach.Walk(q, L.PlanBuffChance, (_, level) => level > BuffReach.BookTop)!.Focus); // nothing above VI is read
    }

    [Fact]
    public void The_bootstrap_climbs_as_the_owner_ordered_and_reads_each_step_on_the_skill_the_last_one_left()
    {
        var reach = BuffReach.Walk(Winnie(itemTrained: true), L.PlanBuffChance, (line, level) => level <= 3)!;

        Assert.Equal(
            [("Focus Self", 2, 115u, 119u), ("Willpower Self", 3, 119u, 124u), ("Creature Enchantment Mastery Self", 3, 124u, 144u), ("Focus Self", 3, 144u, 145u)],
            reach.Steps.Select(s => (s.Line, s.Level, s.Before, s.After)));
        Assert.Equal((3, 3, 3), (reach.Focus, reach.Willpower, reach.CreatureMastery));
        Assert.Equal(145u, reach.Creature); // the coordinator's 145 stands
        Assert.Equal(170u, reach.Life); // IV at 0.80
        Assert.Equal(95u, reach.Item); // II at 0.96, III at 0.46 (the skill 116 needs base 86)
        Assert.True(SpellPlan.Chance(reach.Life, 150) >= 0.75);
        Assert.True(SpellPlan.Chance(reach.Item, 100) < 0.75);
    }

    [Fact]
    public void Level_IV_of_the_bootstrap_lifts_every_school_by_seven_and_a_half_more()
    {
        var iii = BuffReach.Walk(Winnie(creature: 136), L.PlanBuffChance, (line, level) => level <= 3)!;
        var iv = BuffReach.Walk(Winnie(creature: 136), L.PlanBuffChance, (line, level) => level <= 4)!;
        Assert.Equal(166u, iii.Creature);
        Assert.True(iv.Creature >= 173);
        Assert.Equal((4, 4, 4), (iv.Focus, iv.Willpower, iv.CreatureMastery));
    }

    [Fact]
    public void The_first_scrolls_stand_right_after_the_bolt_and_every_other_due_line_saves_for_them_by_name()
    {
        var needs = Needs(Winnie(), Book());

        var first = needs.Where(n => n.Kind == LearnNeedKind.Due && n.Line.Kind is not (ScrivenerLineKind.Bolt or ScrivenerLineKind.Dance)).Select(n => n.Spell!.Name);
        Assert.Equal(["Focus Self II", "Willpower Self II", "Life Magic Mastery Self III", "Armor Self IV", "Bludgeoning Protection Self IV"], first);
        var saved = needs.Where(n => n.Kind == LearnNeedKind.Saving).ToList();
        Assert.Contains(saved, n => n.Spell!.Name == "Creature Enchantment Mastery Self III");
        Assert.Contains("saving for", saved[0].Why, StringComparison.Ordinal);
        Assert.Contains("the owner's first scrolls", saved[0].Why, StringComparison.Ordinal);
        Assert.DoesNotContain(needs, n => n.Kind == LearnNeedKind.Due && n.Spell is { Level: >= 3 } s && !L.FirstScrolls.Any(g => g.Line == s.Line && g.Level == s.Level));
    }

    [Fact]
    public void With_the_first_scrolls_read_the_bootstrap_buys_both_its_steps_and_a_mastery_waits_for_its_skill()
    {
        var book = Book(("Focus Self", 2), ("Willpower Self", 2), ("Life Magic Mastery Self", 3), ("Armor Self", 4), ("Bludgeoning Protection Self", 4));

        var needs = Needs(Winnie(), book);

        var due = needs.Where(n => n.Kind == LearnNeedKind.Due).Select(n => n.Spell!.Name).ToList();
        Assert.Contains("Focus Self III", due);
        Assert.Contains("Willpower Self III", due);
        Assert.Contains("Creature Enchantment Mastery Self III", due);
        Assert.Contains("War Magic Mastery Self III", due);
        Assert.Equal(LearnNeedKind.Untrained, needs.Single(n => n.Line.Name == "Item Enchantment Mastery Self").Kind); // needs_skill 32
        Assert.Equal(LearnNeedKind.Untrained, needs.Single(n => n.Line.Name == "Impenetrability").Kind);
    }

    [Fact]
    public void Across_level_16_the_credit_trains_Item_and_the_plan_then_learns_the_robes_lines_at_the_bar()
    {
        var order = new List<CreditLine> { new(MageTemplate.ItemEnchantment, 8) };
        Assert.Equal(CreditStepKind.Wait, SkillCreditPlan.Next(order, 7, _ => false).Kind); // level 15: 7 credits
        Assert.Equal(CreditStepKind.Train, SkillCreditPlan.Next(order, 8, _ => false).Kind); // level 16's credit

        var book = Book(("Focus Self", 2), ("Willpower Self", 2), ("Life Magic Mastery Self", 3), ("Armor Self", 4), ("Bludgeoning Protection Self", 4));
        var needs = Needs(Winnie(itemTrained: true, level: 16), book, 16);

        Assert.Equal("Bludgeon Bane II", needs.Single(n => n.Line.Name == "Bludgeon Bane").Spell!.Name); // Item 95 by the bootstrap: II at 0.97, III at 0.46
        Assert.Equal("Impenetrability II", needs.Single(n => n.Line.Name == "Impenetrability").Spell!.Name);
        Assert.Equal(LearnNeedKind.Due, needs.Single(n => n.Line.Name == "Item Enchantment Mastery Self").Kind);
    }

    [Fact]
    public void The_push_asks_Item_to_86_then_Creature_to_136_with_the_buffs_at_III_and_nothing_with_no_buffs()
    {
        var levels = new[] { "Focus Self", "Willpower Self", "Creature Enchantment Mastery Self", "Item Enchantment Mastery Self" };
        var pushes = BuffReach.Push(Winnie(itemTrained: true, level: 16), L.PlanBuffChance, (line, level) => level <= 3 && levels.Contains(line));

        Assert.Equal([(MageTemplate.ItemEnchantment, 21), (MageTemplate.CreatureEnchantment, 71)], pushes.Select(p => (p.Skill, p.Ranks)));
        Assert.Contains("base 86 brings level III", pushes[0].Why, StringComparison.Ordinal);
        Assert.Contains("base 136 brings level IV", pushes[1].Why, StringComparison.Ordinal);
        Assert.Empty(BuffReach.Push(Winnie(itemTrained: true), L.PlanBuffChance, (_, _) => false)); // no buff held: nothing to count on
    }

    [Fact]
    public void The_climbers_pass_casts_the_bootstrap_first_retries_a_fizzle_by_the_lines_rule_and_reports_each_step_and_the_pass()
    {
        var r = new WorldActionRig();
        var known = new uint[] { 1422, 1423, 1447, 559, 607, 1310, 1021 }; // Focus II and III, Willpower III, Creature Mastery III, Life Mastery III, Armor Self IV, Protection IV
        var caster = new ScriptedCaster(r, 400, 52, known: known);
        caster.FizzleNext(1423, 1);
        var cycle = new BuffCycle(r.Api, r.H.State, caster.Table, new BuffOptions(), r.H.Log, profile: BuffProfiles.Climber);
        Assert.True(cycle.Start(r.H.R.Clock.Now));
        var end = r.H.R.Clock.Now + 300;
        var status = BuffStatus.Running;
        while (status == BuffStatus.Running && r.H.R.Clock.Now < end)
        {
            r.Tick(0.25);
            caster.Pump();
            r.Tick();
            status = cycle.Step(r.H.R.Clock.Now);
        }

        Assert.Equal(BuffStatus.Done, status);
        var landed = caster.Landed.Distinct().ToList();
        Assert.Equal([1423u, 1447u, 559u, 607u, 1310u, 1021u], landed); // the owner's order: the bootstrap, the school, last the armor lines; the fizzled Focus III is cast again by the line's rule
        Assert.Contains(caster.Answered, a => a.Spell == 1423 && a.Fizzled);
        Assert.Contains(r.H.Log.Lines, l => l.Contains("buff: bootstrap: step Focus Self: level", StringComparison.Ordinal));
        Assert.Contains(r.H.Log.Lines, l => l.Contains("buff: bootstrap: end: CreatureEnchantment=", StringComparison.Ordinal));
        Assert.Contains(r.H.Log.Lines, l => l.Contains("buff: pass: casts=", StringComparison.Ordinal) && l.Contains("armor_buffs=4:4:0:0 damage_factor=unread armor_pieces=0 armor_unread=5 bare=unread", StringComparison.Ordinal)); // M6 item 14: the factor is read on what she wears; the rig's five worn pieces are not appraised, so the line says unread and prints no factor on a guess
    }

    [Fact]
    public void The_armor_factor_is_ACEs_sum_on_the_robe()
    {
        Assert.Equal(0.32, ArmorReport.Factor(2, 2, 0, 0), 2); // today: Armor Self II, Protection II
        Assert.Equal(0.175, ArmorReport.Factor(4, 4, 0, 0), 3); // 45% less
        Assert.Equal(0.130, ArmorReport.Factor(4, 4, 2, 2), 3); // 59% less
        Assert.Equal(0.107, ArmorReport.Factor(4, 4, 3, 3), 3); // 67% less
        Assert.Equal("armor_buffs=4:4:3:3 damage_factor=0.107", ArmorReport.Line(4, 4, 3, 3));
    }

    private static Spellbook FirstScrollsBook() =>
        Book(("Focus Self", 2), ("Willpower Self", 2), ("Life Magic Mastery Self", 3), ("Armor Self", 4), ("Bludgeoning Protection Self", 4));

    [Fact]
    public void M6_7_AS_Lifestone_Recall_then_Tie_are_due_right_after_the_first_scrolls_and_before_the_drains()
    {
        var needs = Needs(Winnie(itemTrained: true, level: 16, item: 150), FirstScrollsBook(), 16);

        var travel = needs.Where(n => n.Line.Kind == ScrivenerLineKind.Travel).ToList();
        Assert.Equal(["Lifestone Recall", "Lifestone Tie"], travel.Select(n => n.Line.Name));
        Assert.All(travel, n => Assert.Equal(LearnNeedKind.Due, n.Kind)); // sold by the Journeyman Scrivener of Item Magic in Holtburg, 0.75 on the live Item skill
        Assert.Equal((1635u, 2644u), (travel[0].Spell!.SpellId, travel[1].Spell!.SpellId));
        var names = needs.Select(n => n.Line.Name).ToList();
        Assert.True(names.IndexOf("Lifestone Recall") < names.IndexOf("Drain Health Other"));
        Assert.True(names.IndexOf("Bludgeoning Protection Self") < names.IndexOf("Lifestone Recall"));
    }

    [Fact]
    public void M6_7_AS_The_travel_lines_save_for_the_first_scrolls_and_no_portal_line_is_planned()
    {
        var needs = Needs(Winnie(itemTrained: true, level: 16, item: 150), Book(), 16);

        Assert.All(needs.Where(n => n.Line.Kind == ScrivenerLineKind.Travel), n => Assert.Equal(LearnNeedKind.Saving, n.Kind));
        Assert.DoesNotContain(needs, n => n.Line.Name.Contains("Portal", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_7_AS_An_Item_skill_under_the_bar_buys_nothing_and_says_why_and_a_held_spell_is_known()
    {
        var held = Needs(Winnie(itemTrained: true, level: 16, item: 150), Book(("Focus Self", 2), ("Willpower Self", 2), ("Life Magic Mastery Self", 3), ("Armor Self", 4), ("Bludgeoning Protection Self", 4), ("Lifestone Recall", 0), ("Lifestone Tie", 0)), 16);
        Assert.All(held.Where(n => n.Line.Kind == ScrivenerLineKind.Travel), n => Assert.Equal(LearnNeedKind.Known, n.Kind));

        var low = Needs(Winnie(itemTrained: true, level: 16, item: 70), FirstScrollsBook(), 16).Where(n => n.Line.Kind == ScrivenerLineKind.Travel).ToList();
        Assert.Equal(2, low.Count);
        Assert.All(low, n => Assert.NotEqual(LearnNeedKind.Due, n.Kind)); // under the 0.75 bar: not bought, the plan says why, the way home stays on foot
        Assert.All(low, n => Assert.Contains("Item", n.Why, StringComparison.Ordinal));

        var untrained = Needs(Winnie(itemTrained: false, level: 15), FirstScrollsBook(), 15).Where(n => n.Line.Kind == ScrivenerLineKind.Travel);
        Assert.All(untrained, n => Assert.Equal(LearnNeedKind.Untrained, n.Kind));
    }
}
