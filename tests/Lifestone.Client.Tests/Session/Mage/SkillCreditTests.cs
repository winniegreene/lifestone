using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 1 b: the climber's skill credits (<see cref="SkillCreditPlan"/>). What these prove: the order is the creation's unpaid trained lines with the costs the creation read (Creature Enchantment 8, Item Enchantment 8, Mana
/// Conversion 6; M6 D43's order), then Alchemy at its trained cost (6 in the dats) only when a job needs it (M6 14 D136, amending D132: the credits bank until then), never Melee Defense (D5) and never a specialisation; the plan trains the order's first untrained skill when the credits cover it and otherwise waits for the difference, keeping the order; a
/// skill already trained is skipped; and, with the retail dats, the XpTable's credits a level and the plan at levels 1, 5, 10, 11, 12, 19, 20 and 32 (Winnie at level 10 with 13 credits trains Creature Enchantment, Item Enchantment at 16,
/// Mana Conversion at 32: levels 11 to 19 grant a credit only at the even ones, so the map's "11 and 19" do not hold). The dat tests skip without the dats.
/// </summary>
public sealed class SkillCreditTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Creature = MageTemplate.CreatureEnchantment, Conversion = MageTemplate.ManaConversion, Item = MageTemplate.ItemEnchantment;

    private static readonly Dictionary<uint, int> TrainedCost = new()
    {
        [MageTemplate.WarMagic] = 14, [MageTemplate.Alchemy] = 6, [MageTemplate.LifeMagic] = 12, [Creature] = 8, [Item] = 8, [Conversion] = 6, [MageTemplate.MeleeDefense] = 10,
    };

    private static readonly Dictionary<uint, int> SpecializedCost = new()
    {
        [MageTemplate.WarMagic] = 28, [MageTemplate.LifeMagic] = 20, [MageTemplate.Alchemy] = 12, [Creature] = 16, [Item] = 16, [Conversion] = 12, [MageTemplate.MeleeDefense] = 20,
    };

    /// <summary>The Aluvian costs (as <c>FreshClimberTests</c>' stand-in): 52 credits pay War Magic and Life Magic specialised and leave 4.</summary>
    private static IReadOnlyList<CreditLine> Order(int credits = 52) => SkillCreditPlan.OrderOf(MageTemplate.PlanClimberCreation(credits, id => TrainedCost[id], id => SpecializedCost[id]));

    [Fact]
    public void The_order_is_the_creations_unpaid_trained_lines_with_their_costs_then_Alchemy_only_on_a_need_never_Melee_Defense_or_a_specialisation()
    {
        // M6 14 D136 (the owner, 2026-11-13, amending D132): with no job needing it Alchemy is not in the order and the credits bank; a need adds it last at its trained cost; the creation still trains none.
        Assert.Equal([new CreditLine(Creature, 8), new CreditLine(Item, 8), new CreditLine(Conversion, 6)], Order()); // D43: Item Enchantment before Mana Conversion
        Assert.Equal([new CreditLine(Creature, 8), new CreditLine(Item, 8), new CreditLine(Conversion, 6), new CreditLine(MageTemplate.Alchemy, 6)], Needed());
        Assert.Equal(Order(), SkillCreditPlan.OrderOf(MageTemplate.PlanClimberCreation(52, id => TrainedCost[id], id => SpecializedCost[id]), _ => false)); // a need for another skill adds nothing
        Assert.DoesNotContain(MageTemplate.Alchemy, MageTemplate.ClimberCreationOrder); // D34 at creation stands
        Assert.DoesNotContain(MageTemplate.MeleeDefense, MageTemplate.ClimberCreationOrder); // D5
        Assert.Equal([MageTemplate.MeleeDefense], SkillCreditPlan.Never); // M6 14: Alchemy left Never
        Assert.Equal([MageTemplate.Alchemy], MageTemplate.ClimberCreditsOnNeed);
        Assert.DoesNotContain(MageTemplate.Alchemy, MageTemplate.ClimberSpendSkills); // trained on a need, never raised with experience in M6 14

        // Credits that cannot specialise Life Magic: its specialisation is unpaid, its trained class paid; the order holds no specialisation and no skill the creation paid.
        var narrow = SkillCreditPlan.OrderOf(MageTemplate.PlanClimberCreation(40, id => TrainedCost[id], id => SpecializedCost[id]));
        Assert.DoesNotContain(narrow, l => l.Skill == MageTemplate.LifeMagic);
        Assert.Equal([Creature, Item, Conversion], narrow.Select(l => l.Skill));

        // A creation whose unpaid lines name Alchemy or Melee Defense (a template change) never puts Melee Defense in the order, and Alchemy only last (its Last line) on a need; a plan made by hand with no Last has none.
        var odd = new MageTemplate.ClimberCreationPlan(4, [], [
            new(MageTemplate.Alchemy, HeadlessCharacter.TrainedSkill, 6, 4),
            new(MageTemplate.MeleeDefense, HeadlessCharacter.TrainedSkill, 10, 4),
            new(Creature, HeadlessCharacter.TrainedSkill, 8, 4),
        ]);
        Assert.Equal([new CreditLine(Creature, 8)], SkillCreditPlan.OrderOf(odd));
        Assert.Equal([new CreditLine(Creature, 8)], SkillCreditPlan.OrderOf(odd with { Last = [new(MageTemplate.Alchemy, 6)] }));
        Assert.Equal([new CreditLine(Creature, 8), new CreditLine(MageTemplate.Alchemy, 6)], SkillCreditPlan.OrderOf(odd with { Last = [new(MageTemplate.Alchemy, 6)] }, id => id == MageTemplate.Alchemy));

        // Next keeps the order it is given and never trains Melee Defense.
        var step = SkillCreditPlan.Next([new(MageTemplate.MeleeDefense, 10), new(Creature, 8), new(MageTemplate.Alchemy, 6)], 99, _ => false);
        Assert.Equal((CreditStepKind.Train, (uint?)Creature), (step.Kind, step.Skill));
        var last = SkillCreditPlan.Next(Needed(), 6, id => id is Creature or Item or Conversion);
        Assert.Equal((CreditStepKind.Train, (uint?)MageTemplate.Alchemy, 6), (last.Kind, last.Skill, last.Cost));
        var banked = SkillCreditPlan.Next(Order(), 6, id => id is Creature or Item or Conversion); // no need: the 6 bank
        Assert.Equal((CreditStepKind.Done, (uint?)null), (banked.Kind, banked.Skill));
    }

    [Fact]
    public void The_plan_trains_the_first_untrained_skill_the_credits_cover_and_otherwise_waits_keeping_the_order()
    {
        var order = Order();
        var wait = SkillCreditPlan.Next(order, 6, _ => false); // 6 credits would pay Mana Conversion, but Creature Enchantment comes first (and Item Enchantment, D43, before Mana Conversion)
        Assert.Equal((CreditStepKind.Wait, (uint?)Creature, 8, 2), (wait.Kind, wait.Skill, wait.Cost, wait.Short));
        Assert.Equal("wait for 2 credits: Creature Enchantment costs 8, the character has 6", wait.ToString());

        var train = SkillCreditPlan.Next(order, 8, _ => false);
        Assert.Equal((CreditStepKind.Train, (uint?)Creature, 8, 0), (train.Kind, train.Skill, train.Cost, train.Short));
        Assert.Equal("train Creature Enchantment for 8 of 8 credits", train.ToString());
    }

    [Fact]
    public void A_skill_already_trained_is_skipped_and_with_all_trained_the_credits_bank_until_a_need()
    {
        var order = Order();
        var next = SkillCreditPlan.Next(order, 8, id => id == Creature); // Creature trained (by the plan or by hand): Item Enchantment is next (D43)
        Assert.Equal((CreditStepKind.Train, (uint?)Item), (next.Kind, next.Skill));
        var item = SkillCreditPlan.Next(order, 3, id => id is Creature or Item);
        Assert.Equal((CreditStepKind.Wait, (uint?)Conversion, 3), (item.Kind, item.Skill, item.Short));
        var bank = SkillCreditPlan.Next(order, 3, id => id is Creature or Item or Conversion); // M6 14 D136: no job needs Alchemy, so the credits bank
        Assert.Equal((CreditStepKind.Done, (uint?)null), (bank.Kind, bank.Skill));
        var alchemy = SkillCreditPlan.Next(Needed(), 3, id => id is Creature or Item or Conversion); // a need: Alchemy waits for its 6
        Assert.Equal((CreditStepKind.Wait, (uint?)MageTemplate.Alchemy, 3), (alchemy.Kind, alchemy.Skill, alchemy.Short));
        var done = SkillCreditPlan.Next(order, 3, _ => true);
        Assert.Equal((CreditStepKind.Done, (uint?)null), (done.Kind, done.Skill));
        Assert.Equal("every skill of the order is trained (3 credits stay unspent)", done.ToString());
    }

    [Fact]
    public void Over_a_table_of_one_credit_a_level_the_schedule_trains_each_skill_as_soon_as_the_credits_cover_it()
    {
        var table = ScriptedMage.SyntheticTable(); // one credit a level from level 2
        Assert.Equal(1, SkillCreditPlan.GrantedAt(table, 2));
        Assert.Equal(4, SkillCreditPlan.EarnedThrough(table, 4, 1)); // level 1's entry is never granted
        Assert.Equal(13, SkillCreditPlan.EarnedThrough(table, 4, 10));
        Assert.Equal([(Creature, 5, 0), (Item, 13, 0), (Conversion, 19, 0)], SkillCreditPlan.Schedule(table, Order(), 4, 30)); // M6 14 D136: no need, the credits bank after Mana Conversion
        Assert.Equal([(Creature, 5, 0), (Item, 13, 0), (Conversion, 19, 0), (MageTemplate.Alchemy, 25, 0)], SkillCreditPlan.Schedule(table, Needed(), 4, 30)); // a need: Alchemy's 6 last
        Assert.Throws<ArgumentOutOfRangeException>(() => SkillCreditPlan.GrantedAt(table, 1));
    }

    [RequiresDatsFact]
    public void With_the_dats_Winnie_at_10_with_13_credits_trains_Creature_then_Item_Enchantment_at_16_and_Mana_Conversion_at_32()
    {
        var table = XpTableData.Load(fixture.Dats);
        var creation = ClimberCreateScenario.PlanFromDats(fixture.Dats, buffBot: false); // the order of before item 14 (the bot off); the bot's order is BotBuildTests'
        var order = SkillCreditPlan.OrderOf(creation);
        output.WriteLine($"creation: {creation.Credits} credits, {creation.Spent} spent, {creation.Left} left; order: {string.Join(", ", order.Select(l => $"{MageTemplate.SkillName(l.Skill)} {l.Cost}"))}");
        output.WriteLine("credits granted at levels 2-50: " + string.Join(" ", Enumerable.Range(2, 49).Select(l => $"{l}:{SkillCreditPlan.GrantedAt(table, l)}")));
        Assert.Equal([new CreditLine(Creature, 8), new CreditLine(Item, 8), new CreditLine(Conversion, 6)], order); // the template's order (D43) and the SkillTable's costs (8, 8, 6); M6 14 D136: Alchemy only on a need
        Assert.Equal(4, creation.Left);

        // The XpTable's credits: one at each level 2 to 10 (item 0's climb read 10 at level 7, 11 at 8, 12 at 9 and 13 at 10, untrained), then one at each even level to 20, then at 23, 26, 29, 32, 35, 40, 45, 50.
        // The map's "Mana Conversion at 11, Item at 19" (the old order) assumed one a level throughout: levels 11, 13, 15, 17 and 19 grant none.
        int[] granting = [.. Enumerable.Range(2, 9), 12, 14, 16, 18, 20, 23, 26, 29, 32, 35, 40, 45, 50];
        Assert.All(Enumerable.Range(2, 49), l => Assert.Equal(granting.Contains(l) ? 1 : 0, SkillCreditPlan.GrantedAt(table, l)));
        Assert.Equal(4, SkillCreditPlan.EarnedThrough(table, creation.Left, 1));
        Assert.Equal(8, SkillCreditPlan.EarnedThrough(table, creation.Left, 5));
        Assert.Equal(13, SkillCreditPlan.EarnedThrough(table, creation.Left, 10));

        bool None(uint _) => false;
        bool CreatureOnly(uint id) => id == Creature;
        bool CreatureAndItem(uint id) => id is Creature or Item;
        bool All(uint _) => true;
        int Since(int from, int to) => (int)table.SkillCreditsThroughLevel(to, from);

        // Level 1: the creation's 4 wait for Creature Enchantment's 8. Level 5 (a climber that trains at each level-up from level 1): 8 pay it.
        Assert.Equal((CreditStepKind.Wait, (uint?)Creature, 4), Kind(SkillCreditPlan.Next(order, 4, None)));
        Assert.Equal((CreditStepKind.Train, (uint?)Creature, 0), Kind(SkillCreditPlan.Next(order, 8, None)));

        // Level 10 as item 0 left Winnie (13 credits, nothing trained): Creature Enchantment (13 -> 5), then Item Enchantment waits for 3 (D43: Item before Mana Conversion).
        Assert.Equal((CreditStepKind.Train, (uint?)Creature, 0), Kind(SkillCreditPlan.Next(order, 13, None)));
        Assert.Equal((CreditStepKind.Wait, (uint?)Item, 3), Kind(SkillCreditPlan.Next(order, 13 - 8, CreatureOnly)));

        // Level 11 grants none: still waiting for 3; level 12 and 14 wait for 2 and 1; level 15 grants none; level 16: 8 pay Item Enchantment (8 -> 0), the travel spells' school at 16.
        Assert.Equal((CreditStepKind.Wait, (uint?)Item, 3), Kind(SkillCreditPlan.Next(order, 5 + Since(10, 11), CreatureOnly)));
        Assert.Equal((CreditStepKind.Wait, (uint?)Item, 2), Kind(SkillCreditPlan.Next(order, 5 + Since(10, 12), CreatureOnly)));
        Assert.Equal((CreditStepKind.Wait, (uint?)Item, 1), Kind(SkillCreditPlan.Next(order, 5 + Since(10, 15), CreatureOnly)));
        Assert.Equal((CreditStepKind.Train, (uint?)Item, 0), Kind(SkillCreditPlan.Next(order, 5 + Since(10, 16), CreatureOnly)));

        // Then Mana Conversion (6): level 18 and 20 and 23, 26, 29 grant one each; 6 are there at level 32 (waiting for 1 at 31 and 29's 5); then all are trained.
        Assert.Equal((CreditStepKind.Wait, (uint?)Conversion, 5), Kind(SkillCreditPlan.Next(order, Since(16, 18), CreatureAndItem)));
        Assert.Equal((CreditStepKind.Wait, (uint?)Conversion, 1), Kind(SkillCreditPlan.Next(order, Since(16, 31), CreatureAndItem)));
        Assert.Equal((CreditStepKind.Train, (uint?)Conversion, 0), Kind(SkillCreditPlan.Next(order, Since(16, 32), CreatureAndItem)));
        Assert.Equal((CreditStepKind.Done, (uint?)null, 0), Kind(SkillCreditPlan.Next(order, Since(32, 35), All)));

        // The schedule from level 1, training at each level-up: Creature Enchantment at 5, Item Enchantment at 16, Mana Conversion at 32 (the traditional mage's "all three from level 32", MageTemplate.MinLevel's remark).
        Assert.Equal([(Creature, 5, 0), (Item, 16, 0), (Conversion, 32, 0)], SkillCreditPlan.Schedule(table, order, creation.Left, 40));
    }

    [RequiresDatsFact]
    public void M6_14_with_the_dats_the_credits_bank_after_the_order_and_a_need_trains_Alchemy_at_the_skill_tables_trained_cost()
    {
        var table = XpTableData.Load(fixture.Dats);
        var creation = ClimberCreateScenario.PlanFromDats(fixture.Dats, buffBot: true); // the climber's order (the buff bot on, the default)
        var banked = SkillCreditPlan.OrderOf(creation); // D136: no job needs Alchemy
        Assert.Equal([new CreditLine(Item, 8), new CreditLine(Conversion, 6), new CreditLine(Creature, 8)], banked);
        Assert.Equal((CreditStepKind.Done, (uint?)null, 0), Kind(SkillCreditPlan.Next(banked, 2 + (int)table.SkillCreditsThroughLevel(60, 40), id => id is Item or Conversion or Creature))); // level 60's 6 credits bank
        Assert.Equal([(Item, 5, 0), (Conversion, 12, 0), (Creature, 32, 0)], SkillCreditPlan.Schedule(table, banked, creation.Left, 126));

        var order = SkillCreditPlan.OrderOf(creation, id => id == MageTemplate.Alchemy); // a job needs it (the Atlan wand's Alchemy check, TODO(m6-credit-need))
        var schedule = SkillCreditPlan.Schedule(table, order, creation.Left, 126);
        output.WriteLine($"order: {string.Join(", ", order.Select(l => $"{MageTemplate.SkillName(l.Skill)} {l.Cost}"))}; schedule: {string.Join(", ", schedule.Select(s => $"{MageTemplate.SkillName(s.Skill)} at {s.Level} ({s.CreditsLeft} left)"))}");
        output.WriteLine("credits granted at levels 30-126: " + string.Join(" ", Enumerable.Range(30, 97).Where(l => SkillCreditPlan.GrantedAt(table, l) > 0).Select(l => $"{l}:{SkillCreditPlan.GrantedAt(table, l)}")));

        // The SkillTable's trained cost for the Aluvian heritage is 6, read from the dats as the other lines' costs are; the creation still trains none (D34 stands there).
        Assert.Equal([new CreditLine(Item, 8), new CreditLine(Conversion, 6), new CreditLine(Creature, 8), new CreditLine(MageTemplate.Alchemy, 6)], order);
        Assert.Null(creation.ClassOf(MageTemplate.Alchemy));

        // With the need from level 32: after Creature Enchantment nothing is left; the XpTable grants one credit at 35, 40, 45, 50, 55 and 60: the 2 she holds at level 40 (the live log of 2026-11-12) wait for 4 more, and Alchemy is trained at level 60.
        Assert.Equal((MageTemplate.Alchemy, 60, 0), schedule[^1]);
        Assert.Equal((CreditStepKind.Wait, (uint?)MageTemplate.Alchemy, 4), Kind(SkillCreditPlan.Next(order, 2, id => id is Item or Conversion or Creature)));
        Assert.Equal("wait for 4 credits: Alchemy costs 6, the character has 2", SkillCreditPlan.Next(order, 2, id => id is Item or Conversion or Creature).ToString());
        Assert.Equal((CreditStepKind.Train, (uint?)MageTemplate.Alchemy, 0), Kind(SkillCreditPlan.Next(order, 2 + (int)table.SkillCreditsThroughLevel(60, 40), id => id is Item or Conversion or Creature)));
    }

    private static (CreditStepKind Kind, uint? Skill, int Short) Kind(CreditStep step) => (step.Kind, step.Skill, step.Short);

    /// <summary>The order when a job needs Alchemy (D136).</summary>
    private static IReadOnlyList<CreditLine> Needed(int credits = 52) => SkillCreditPlan.OrderOf(MageTemplate.PlanClimberCreation(credits, id => TrainedCost[id], id => SpecializedCost[id]), id => id == MageTemplate.Alchemy);
}
