using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The mage's loot rule (M5 item 3 m2, <see cref="MageLootRules"/>): which items of a corpse the mage keeps, ranked by value per burden and bounded by the load line and the main pack's free slots; pyreals always; a missing Value
/// left as unknown unless an appraisal gives it; the player's own corpse refused; and the plan as the rule the shared <see cref="CorpseLooter"/> asks.
/// </summary>
public sealed class MageLootRulesTests
{
    private const uint Misc = 0x80;
    private const uint Money = 0x40;
    private const int Capacity = 1500;

    private static MageLootItem Item(uint guid, long? value, long burden, uint type = Misc, string name = "Thing") => new(guid, name, type, value, burden);

    private static MageLootPlan Plan(MageLootThresholds? thresholds, MageLootItem[] items, int burden = 0, int capacity = Capacity, int freeSlots = 20, string corpse = "Corpse of Tusker Slave", string player = "Amy") =>
        new MageLootRules(thresholds).Plan(corpse, player, items, burden, capacity, freeSlots);

    [Fact]
    public void The_rest_rank_by_value_per_burden_and_pyreals_come_first()
    {
        // 40 per burden, 10 per burden, 3 per burden, and a stack of pyreals listed last.
        var items = new[] { Item(2, 100, 10), Item(3, 300, 100), Item(4, 400, 10), Item(5, 250, 0, Money, "Pyreal") };

        var plan = Plan(null, items);

        Assert.Equal([5u, 4u, 2u, 3u], plan.Kept);
        Assert.Equal(1050L, plan.KeptValue);
        Assert.Equal(120L, plan.KeptBurden);
        Assert.Equal(MageLootVerdict.Pyreals, plan.VerdictOf(5));
        Assert.Equal(MageLootVerdict.Keep, plan.VerdictOf(4));
        Assert.Equal(1, plan.Count(MageLootVerdict.Pyreals));
        Assert.Equal(3, plan.Count(MageLootVerdict.Keep));
    }

    [Fact]
    public void A_weightless_item_with_a_value_ranks_first_and_a_tie_goes_to_the_greater_value_then_the_corpses_order()
    {
        var items = new[] { Item(2, 50, 5), Item(3, 100, 10), Item(4, 7, 0), Item(5, 100, 10) };

        var plan = Plan(null, items);

        Assert.Equal([4u, 3u, 5u, 2u], plan.Kept); // 4 weightless; 3 and 5 tie at 10 and 100 (3 first in the corpse); 2 is 10 per burden but worth only 50
    }

    [Fact]
    public void An_item_under_the_value_per_burden_floor_is_left()
    {
        var thresholds = new MageLootThresholds { MinValuePerBurden = 5 };
        var items = new[] { Item(2, 40, 10), Item(3, 51, 10), Item(4, 0, 0) };

        var plan = Plan(thresholds, items);

        Assert.Equal([3u], plan.Kept); // 40 over 10 is 4: under the floor; a Value of 0 is worth nothing
        Assert.Equal(MageLootVerdict.ValuePerBurden, plan.VerdictOf(2));
        Assert.Equal(MageLootVerdict.ValuePerBurden, plan.VerdictOf(4));
        Assert.Equal(2, plan.Count(MageLootVerdict.ValuePerBurden));
    }

    [Fact]
    public void The_load_line_bounds_what_is_kept_and_a_lighter_item_further_down_may_still_fit()
    {
        // The burden is 1400 of 1500: 100 more fit. Ranked: 2 (burden 60), 3 (burden 60: over), 4 (burden 30: fits).
        var items = new[] { Item(2, 6000, 60), Item(3, 3000, 60), Item(4, 600, 30) };

        var plan = Plan(null, items, burden: 1400);

        Assert.Equal([2u, 4u], plan.Kept);
        Assert.Equal(MageLootVerdict.Load, plan.VerdictOf(3));
        Assert.Equal(1, plan.Count(MageLootVerdict.Load));
    }

    [Fact]
    public void A_lower_load_line_bounds_it_sooner_and_no_capacity_with_a_line_leaves_everything_but_pyreals()
    {
        var items = new[] { Item(2, 1000, 100), Item(3, 900, 100), Item(4, 5, 0, Money) };

        var half = Plan(new MageLootThresholds { LoadLinePercent = 50 }, items, burden: 350, capacity: 1000);
        Assert.Equal([4u, 2u], half.Kept); // 350 + 100 is 45% of 1000: under the 50% line; 450 + 100 is 55%: over it
        Assert.Equal(MageLootVerdict.Load, half.VerdictOf(3));

        var unknown = Plan(null, items, capacity: 0);
        Assert.Equal([4u], unknown.Kept);
        Assert.Equal(2, unknown.Count(MageLootVerdict.Load));

        var unlimited = Plan(new MageLootThresholds { LoadLinePercent = 0 }, items, burden: 5000);
        Assert.Equal([2u, 3u, 4u], unlimited.Kept.Order().ToArray()); // 0 is no load line
    }

    [Fact]
    public void The_free_slots_bound_what_is_kept_and_the_slots_kept_free_come_off_them()
    {
        var items = new[] { Item(2, 900, 10), Item(3, 800, 10), Item(4, 700, 10) };

        var two = Plan(null, items, freeSlots: 2);
        Assert.Equal([2u, 3u], two.Kept);
        Assert.Equal(MageLootVerdict.Slots, two.VerdictOf(4));

        var keptFree = Plan(new MageLootThresholds { SlotsKeptFree = 1 }, items, freeSlots: 2);
        Assert.Equal([2u], keptFree.Kept);
        Assert.Equal(2, keptFree.Count(MageLootVerdict.Slots));

        var none = Plan(null, items, freeSlots: 0);
        Assert.Empty(none.Kept);
        Assert.Equal(3, none.Count(MageLootVerdict.Slots));
    }

    [Fact]
    public void Pyreals_are_taken_over_the_floor_the_load_line_and_a_full_pack_and_use_a_slot()
    {
        var thresholds = new MageLootThresholds { MinValuePerBurden = 1000 };
        var items = new[] { Item(2, 3, 5, Money, "Pyreal"), Item(3, 900, 10) };

        var plan = Plan(thresholds, items, burden: 1500, freeSlots: 1);

        Assert.Equal([2u], plan.Kept); // the pyreals took the one slot, the item is under the floor anyway
        Assert.Equal(MageLootVerdict.Pyreals, plan.VerdictOf(2));

        var fullPack = Plan(null, items, burden: 1500, freeSlots: 0);
        Assert.Equal([2u], fullPack.Kept); // the pack is full and the item is over the line: the pyreals are still asked for
    }

    [Fact]
    public void A_missing_Value_is_left_as_unknown_until_an_appraisal_gives_it()
    {
        var items = new[] { Item(2, null, 10), Item(3, 100, 10) };

        var plan = Plan(null, items);

        Assert.Equal([3u], plan.Kept);
        Assert.Equal(MageLootVerdict.ValueUnknown, plan.VerdictOf(2));
        Assert.Equal(1, plan.Count(MageLootVerdict.ValueUnknown));
        Assert.Equal("TODO(m5-loot-value-unknown)", MageLootRules.ValueUnknownTodo);

        // The Value read: the description's, else an appraisal's (PropertyInt 19), else none.
        var rig = new WorldActionRig();
        rig.Server((p, s) => AceVendorWriters.IdentifyResponse(p, s, 0x80000211, new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 450, [AppraisalIds.Burden] = 90 } }));
        Assert.True(rig.H.State.Appraisals.TryGet(0x80000211, out var appraisal));
        var described = LootRulesTests.Item(Misc, 25, burden: 5);
        var undescribed = LootRulesTests.Item(Misc, null);

        Assert.Equal(25L, MageLootRules.ValueOf(described, appraisal));
        Assert.Equal(450L, MageLootRules.ValueOf(undescribed, appraisal));
        Assert.Null(MageLootRules.ValueOf(undescribed, null));
        var viaAppraisal = MageLootRules.ItemOf(undescribed, appraisal, held: false);
        Assert.Equal(450L, viaAppraisal.Value);
        Assert.Equal(90L, viaAppraisal.Burden);
        var plan2 = Plan(null, [viaAppraisal]);
        Assert.Equal([viaAppraisal.Guid], plan2.Kept);
    }

    [Fact]
    public void The_players_own_corpse_is_refused_whatever_it_holds()
    {
        var items = new[] { Item(2, 9000, 10), Item(3, 5, 0, Money) };

        var own = Plan(null, items, corpse: "Corpse of Amy");
        var theirs = Plan(null, items, corpse: "Corpse of Tusker Slave");

        Assert.True(own.OwnCorpse);
        Assert.Empty(own.Kept);
        Assert.Equal(2, own.Count(MageLootVerdict.OwnCorpse));
        Assert.False(theirs.OwnCorpse);
        Assert.Equal(2, theirs.Kept.Count);
        Assert.True(MageLootRules.IsOwnCorpse("corpse of amy", "Amy"));
        Assert.False(MageLootRules.IsOwnCorpse("Corpse of Amy Jr", "Amy"));
        Assert.False(MageLootRules.IsOwnCorpse("Corpse of Amy", null));
        Assert.Equal("Corpse of Tusker Slave", MageLootRules.CorpseNameOf("Tusker Slave"));
    }

    [Fact]
    public void The_plan_is_the_rule_the_shared_looter_asks_item_by_item()
    {
        var plan = Plan(null, [Item(0x80000211, 100, 10), Item(0x80000212, 1, 100), Item(0x80000213, null, 1), Item(0x80000214, 100, 10)], freeSlots: 1);
        var context = new LootContext(Burden: 0, Capacity: Capacity, Inventory: null);

        Assert.Equal(LootDecision.Take, plan.Decide(LootRulesTests.Item(Misc, 100, 10, guid: 0x80000211), in context));
        Assert.Equal(LootDecision.Value, plan.Decide(LootRulesTests.Item(Misc, 1, 100, guid: 0x80000212), in context)); // under the floor
        Assert.Equal(LootDecision.Value, plan.Decide(LootRulesTests.Item(Misc, null, 1, guid: 0x80000213), in context)); // no Value
        Assert.Equal(LootDecision.Burden, plan.Decide(LootRulesTests.Item(Misc, 100, 10, guid: 0x80000214), in context)); // no slot left for it
        Assert.Equal(LootDecision.NotResolved, plan.Decide(LootRulesTests.Item(Misc, 100, 10, guid: 0x80000299), in context)); // arrived after the plan
        Assert.Equal(LootDecision.NotResolved, plan.Decide(null, in context));
        Assert.Contains("keeps 1 of 4", plan.Describe(), StringComparison.Ordinal);
        Assert.Contains("keep 1 of 4", plan.Summary(), StringComparison.Ordinal);
    }

    [Fact]
    public void Thresholds_out_of_range_are_refused_and_the_free_slots_start_from_the_packs_capacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MageLootRules(new MageLootThresholds { MinValuePerBurden = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MageLootRules(new MageLootThresholds { LoadLinePercent = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MageLootRules(new MageLootThresholds { SlotsKeptFree = -1 }));
        Assert.Equal(1.0, new MageLootThresholds().MinValuePerBurden);
        Assert.Equal(100, new MageLootThresholds().LoadLinePercent);

        var rig = new WorldActionRig();
        Assert.Equal(GearKit.PackCapacity, Lifestone.Client.Session.Quartermaster.PackView.MainPackFree(rig.H.State));
    }

    private const uint Jewelry = 0x8;
    private const uint Melee = 0x1;
    private const uint Gem = 0x800;

    private static MageLootThresholds Den() => new FightOptions { Loot = true }.EffectiveLootThresholds; // the default ground is the den: its profile's loot section

    [Fact]
    public void The_dens_profile_says_a_payout_of_4000_sixty_Value_per_burden_unit_and_no_item_over_150_burden_M5_7_F29()
    {
        var thresholds = Den();
        Assert.Equal(60.0, thresholds.MinValuePerBurden);
        Assert.Equal(4000, thresholds.MinPayout);
        Assert.Equal(150, thresholds.MaxItemBurden);
        Assert.Equal(85, thresholds.LoadLinePercent); // the loot section's load line
        Assert.Equal(6, thresholds.Buyers.Count); // M6 14 grocer: Fispur Ansel the Grocer is the sixth (the scrivener's mask is 0: no buyer)
        Assert.Equal(0.9, thresholds.BestRate(0x400000)); // the Empty Flask's type, CraftCookingBase: the Grocer's 0.9
        Assert.Equal(0.9, thresholds.BestRate(Jewelry));
        Assert.Equal(5040, thresholds.PayoutOf(Jewelry, 5600)); // ACE: floor(0.9 x 5600 + 0.1)
        var belt = new FightOptions { Loot = true, Ground = "low-b-belt" }.EffectiveLootThresholds;
        Assert.Equal(1.0, belt.MinValuePerBurden); // a ground without the section keeps today's floor
        Assert.Equal((0L, 0), (belt.MinPayout, belt.MaxItemBurden));
    }

    [Fact]
    public void An_item_is_kept_only_when_both_its_payout_and_its_value_per_burden_reach_the_floors_M5_7_F29()
    {
        var thresholds = Den() with { MaxItemBurden = 0 }; // the table without the weight cap, which has its own test
        var items = new[]
        {
            Item(2, 5600, 70, Jewelry, "Opal Ring"), // payout 5,040 (0.9), v/b 80: kept
            Item(3, 3400, 17, Jewelry, "Pearl"), // payout 3,060, v/b 200: under the payout floor
            Item(4, 10000, 333, Jewelry, "Gold Chain"), // payout 9,000, v/b 30: under the value-per-burden floor
            Item(5, 250, 0, Money, "Pyreal"), // coins always
        };

        var plan = Plan(thresholds, items);

        Assert.Equal(MageLootVerdict.Keep, plan.VerdictOf(2));
        Assert.Equal(MageLootVerdict.Payout, plan.VerdictOf(3));
        Assert.Equal(MageLootVerdict.ValuePerBurden, plan.VerdictOf(4));
        Assert.Equal(MageLootVerdict.Pyreals, plan.VerdictOf(5));
        Assert.Equal([5u, 2u], plan.Kept);
        Assert.Equal(LootDecision.Value, plan.Decide(LootRulesTests.Item(Jewelry, 3400, 17, guid: 3), new LootContext(Burden: 0, Capacity: Capacity, Inventory: null)));
        Assert.Contains("payout_floor=1", plan.Summary(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_item_over_150_burden_is_left_as_too_heavy_whatever_its_value_and_the_log_says_so_apart_from_the_floor_M5_7_F29()
    {
        var thresholds = Den();
        var items = new[]
        {
            Item(2, 60000, 1200, 0x2, "Covenant Breastplate"), // v/b 50, payout 54,000: heavy and valuable
            Item(3, 900000, 151, 0x2, "Jewelled Mail"), // valuable, one over the cap
            Item(4, 6000, 150, 0x2, "Plate Helm"), // exactly 150: not over (v/b 40: the floor takes it)
            Item(5, 1000, 50, Jewelry, "Tin Ring"), // light but under the floors
            Item(6, 8000, 100, 0x2, "Mail Gloves"), // v/b 80, payout 7,200
        };

        var plan = Plan(thresholds, items);

        Assert.Equal(MageLootVerdict.TooHeavy, plan.VerdictOf(2));
        Assert.Equal(MageLootVerdict.TooHeavy, plan.VerdictOf(3));
        Assert.Equal(MageLootVerdict.ValuePerBurden, plan.VerdictOf(4));
        Assert.Equal(MageLootVerdict.ValuePerBurden, plan.VerdictOf(5));
        Assert.Equal(MageLootVerdict.Keep, plan.VerdictOf(6));
        var lines = plan.DecisionLines().ToArray();
        Assert.Contains(", too heavy)", lines[0], StringComparison.Ordinal);
        Assert.Contains(", too heavy)", lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain("too heavy", lines[2], StringComparison.Ordinal); // under the floor is its own reason
        Assert.DoesNotContain("too heavy", lines[3], StringComparison.Ordinal);
        Assert.Equal(LootDecision.Burden, plan.Decide(LootRulesTests.Item(0x2, 60000, 1200, guid: 2), new LootContext(Burden: 0, Capacity: Capacity, Inventory: null)));
        Assert.Contains("too_heavy=2", plan.Summary(), StringComparison.Ordinal);
    }

    [Fact]
    public void Another_load_line_set_on_the_options_wins_and_without_the_values_the_floor_is_F28s_M5_7_F29()
    {
        var own = new FightOptions { Loot = true, LootThresholds = new MageLootThresholds { LoadLinePercent = 70, MinPayout = 100, MaxItemBurden = 10 } }.EffectiveLootThresholds;
        Assert.Equal((70, 100L, 10), (own.LoadLinePercent, own.MinPayout, own.MaxItemBurden));

        var plain = Plan(new MageLootThresholds { MinValuePerBurden = 10 }, [Item(2, 1000, 760, Melee, "Steel Sword"), Item(3, 100000, 500, 0x2, "Heavy Mail")]);
        Assert.Equal(MageLootVerdict.ValuePerBurden, plain.VerdictOf(2)); // no cap and no payout floor: only F28's floor
        Assert.Equal(MageLootVerdict.Keep, plain.VerdictOf(3));
    }

    [Fact]
    public void Another_floor_set_on_the_options_wins_over_the_profile_s_and_the_sellable_types_come_from_the_grounds_vendors_M5_7_F28()
    {
        var own = new FightOptions { Loot = true, LootThresholds = new MageLootThresholds { MinValuePerBurden = 3 } }.EffectiveLootThresholds;
        Assert.Equal(3.0, own.MinValuePerBurden);

        var den = new FightOptions { Loot = true }.EffectiveLootThresholds;
        Assert.NotEqual(0u, den.SellableItemTypes);
        Assert.NotEqual(0u, den.SellableItemTypes & Jewelry); // the Jeweler buys jewellery
        Assert.NotEqual(0u, den.SellableItemTypes & Gem); // and gems
        Assert.Equal(0u, new FightOptions { Loot = true, Ground = "low-b-belt" }.EffectiveLootThresholds.SellableItemTypes); // no profile section: nothing restricted
    }

    [Fact]
    public void An_item_of_a_type_no_vendor_buys_is_left_even_over_the_floor_and_is_logged_so_M5_7_F28()
    {
        var thresholds = new MageLootThresholds { MinValuePerBurden = 10, SellableItemTypes = 0x1u | 0x2u | 0x4u }; // melee, armour, clothing: no jeweller
        var items = new[] { Item(2, 1000, 50, Jewelry, "Opal Ring"), Item(3, 5000, 100, 0x2, "Plate Helm"), Item(4, 100, 50, Jewelry, "Tin Ring"), Item(5, 20, 0, Money, "Pyreal") };

        var plan = Plan(thresholds, items);

        Assert.Equal(MageLootVerdict.NotSold, plan.VerdictOf(2));
        Assert.Equal(MageLootVerdict.ValuePerBurden, plan.VerdictOf(4)); // under the floor first
        Assert.Equal([5u, 3u], plan.Kept); // pyreals are never asked about their type
        Assert.Equal(LootDecision.Type, plan.Decide(LootRulesTests.Item(Jewelry, 1000, 50, guid: 2), new LootContext(Burden: 0, Capacity: Capacity, Inventory: null)));
        Assert.Contains("not_sold=1", plan.Summary(), StringComparison.Ordinal);
        Assert.Equal(2, plan.KeptCount);
        Assert.Equal(2, plan.LeftCount);
    }

    [Fact]
    public void Every_item_has_a_decision_line_with_its_value_burden_ratio_and_floor_M5_7_F28()
    {
        var thresholds = new MageLootThresholds { MinValuePerBurden = 10, SellableItemTypes = 0x1u | 0x2u };
        var items = new[] { Item(2, 1000, 50, Jewelry, "Opal Ring"), Item(3, 900, 760, Melee, "Steel Sword"), Item(4, 5000, 100, 0x2, "Plate Helm"), Item(5, 250, 0, Money, "Pyreal"), Item(6, null, 5, Melee, "Odd Thing") };

        var lines = Plan(thresholds, items).DecisionLines().ToArray();

        Assert.Equal(
            [
                "loot: left Opal Ring value 1000 burden 50 v/b 20 (floor 10, type not sold here)",
                "loot: left Steel Sword value 900 burden 760 v/b 1.18 (floor 10)",
                "loot: kept Plate Helm value 5000 burden 100 v/b 50 (floor 10)",
                "loot: kept Pyreal value 250 burden 0 v/b - (floor 10)",
                "loot: left Odd Thing value unknown burden 5 v/b 0 (floor 10, value unknown)",
            ],
            lines);
    }

    [Fact]
    public void The_decision_line_gains_the_payout_and_the_two_thresholds_when_the_profile_names_them_M5_7_F29()
    {
        var lines = Plan(Den(), [Item(2, 5600, 70, Jewelry, "Opal Ring"), Item(3, 3400, 17, Jewelry, "Pearl"), Item(4, 60000, 1200, 0x2, "Covenant Breastplate"), Item(5, 250, 0, Money, "Pyreal")]).DecisionLines().ToArray();

        Assert.Equal(
            [
                "loot: kept Opal Ring value 5600 burden 70 v/b 80 payout 5040 (floor 60, min payout 4000, max burden 150)",
                "loot: left Pearl value 3400 burden 17 v/b 200 payout 3060 (floor 60, min payout 4000, max burden 150, under the payout floor)",
                "loot: left Covenant Breastplate value 60000 burden 1200 v/b 50 payout 54000 (floor 60, min payout 4000, max burden 150, too heavy)",
                "loot: kept Pyreal value 250 burden 0 v/b - payout 250 (floor 60, min payout 4000, max burden 150)",
            ],
            lines);
    }
}
