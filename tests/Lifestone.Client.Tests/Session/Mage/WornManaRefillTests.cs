using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 14: the refill's rule (<see cref="WornManaRefill"/>) on the climber's worn set as the long run of 2026-11-11 read it (the Pathwarden Robe 1,000 at 0.0333 a second, a looted Vest 400 and Gauntlets 240 at 0.025, the Pathwarden
/// Trinket 6,000 at 0.049), her stones (the Mana Stone, efficiency 0.6; the Lesser, 0.25), her loot as the trips weighed it, and Archmage Cindrue's charges (the pinned world SQL: Tiny 100 for 473, Small 175 for 878, Moderate 350 for
/// 2,025, High 500 for 3,375).
/// </summary>
public sealed class WornManaRefillTests
{
    private const uint Robe = 0x8000039C;
    private const uint Vest = 0x80004657;
    private const uint Gauntlets = 0x80000AE5;
    private const uint Trinket = 0x80000398;
    private const double HalfHour = 1800;

    private static readonly ManaOffer[] Cindrue =
    [
        new(2304, "Archmage Cindrue", 2472, 270), new(2304, "Archmage Cindrue", 4612, 473), new(2304, "Archmage Cindrue", 4613, 878), new(2304, "Archmage Cindrue", 4614, 2025), new(2304, "Archmage Cindrue", 4615, 3375),
    ];

    private static WornMana RobeAt(int mana) => new(Robe, "Pathwarden Robe", mana, 1000, -1.0 / 30);

    private static List<WornMana> Worn(int robe, int vest = 218, int trinket = 4658) =>
        [RobeAt(robe), new(Vest, "Vest", vest, 400, -0.025), new(Gauntlets, "Long Leather Gauntlets", 240, 240, -0.025), new(Trinket, "Pathwarden Trinket", trinket, 6000, -0.049)];

    private static HeldStone ManaStone(uint guid, int? mana = null) => new(guid, "Mana Stone", mana, 0.6, 0.1, 1000);

    private static HeldStone Lesser(uint guid, int? mana = null) => new(guid, "Lesser Mana Stone", mana, 0.25, 0.05, 500);

    private static DrainFacts Loot(uint guid, string name, int mana, long payout) => new(new DrainTarget(guid, name, mana, mana, payout, payout));

    private static readonly DrainFacts Ring = Loot(0x21, "Ring", 1084, 2109);
    private static readonly DrainFacts Slippers = Loot(0x22, "Slippers", 856, 7124);
    private static readonly DrainFacts Staff = Loot(0x23, "Staff", 1750, 3619);

    [Fact]
    public void An_item_is_due_when_it_runs_out_before_the_next_visit_plus_the_margin_not_when_it_is_empty()
    {
        // 13:20 UTC, 2026-11-11: the Robe at 97/1000 had 48 min left, the trip after it came 12 min later and the one after that never refilled it: it ran out at 13:31.
        var due = WornManaRefill.Due(Worn(robe: 97), nextVisitSeconds: HalfHour, marginSeconds: HalfHour);

        var robe = Assert.Single(due);
        Assert.Equal((Robe, 903, false), (robe.Item.Guid, robe.Need, robe.Out));
        Assert.Equal(2910, robe.SecondsLeft, 0);
        Assert.Equal("'Pathwarden Robe' 97/1000: 49 min left at 0.0333/s, under the next visit in 30 min plus 30 min", robe.Why);

        Assert.Empty(WornManaRefill.Due(Worn(robe: 400), HalfHour, HalfHour)); // 3.3 h left: past the hour
        Assert.Empty(WornManaRefill.Due(Worn(robe: 121), HalfHour, HalfHour)); // 3,630 s: just past it
        Assert.Single(WornManaRefill.Due(Worn(robe: 119), HalfHour, HalfHour)); // 3,570 s: under it
        Assert.Empty(WornManaRefill.Due(Worn(robe: 97), HalfHour, HalfHour, new HashSet<uint> { Robe })); // another rule's item is never due here
    }

    [Fact]
    public void An_empty_item_is_due_as_out_and_a_full_one_never_however_small_its_tank()
    {
        // The Gauntlets' full tank lasts 2.7 h: under a long horizon it is still not due, because nothing can be added.
        var due = WornManaRefill.Due(Worn(robe: 0, vest: 100, trinket: 1000), nextVisitSeconds: 4 * 3600, marginSeconds: 4 * 3600);

        Assert.Equal([Robe, Vest, Trinket], due.Select(d => d.Item.Guid)); // the most urgent first: out, then 67 min, then 5.7 h
        Assert.True(due[0].Out);
        Assert.Equal("'Pathwarden Robe' 0/1000: out of mana, its spells are off", due[0].Why);
        Assert.DoesNotContain(due, d => d.Item.Guid == Gauntlets);
    }

    [Fact]
    public void The_margin_is_one_more_gap_and_at_least_half_an_hour()
    {
        Assert.Equal(1800, WornManaRefill.Margin(600));
        Assert.Equal(2760, WornManaRefill.Margin(WornManaRefill.DefaultVisitGapSeconds));
    }

    [Fact]
    public void A_stone_on_the_player_is_rationed_as_ACE_rations_it()
    {
        // ManaStone.cs:121-162: 650 over three items needing 903, 182 and 1,342: round one gives 216 each (the Vest capped at 182), round two splits the 36 left over the two still needing.
        var gains = WornManaRefill.PourOnPlayer(650, Worn(robe: 97), out var left);

        Assert.Equal((234, 182, 234), (gains[Robe], gains[Vest], gains[Trinket]));
        Assert.False(gains.ContainsKey(Gauntlets)); // full: ACE gives it nothing
        Assert.Equal(0, left);

        var all = WornManaRefill.PourOnPlayer(5000, Worn(robe: 97), out var over); // more than every need: the rest reaches no item
        Assert.Equal((903, 182, 1342, 5000 - 903 - 182 - 1342), (all[Robe], all[Vest], all[Trinket], over));
    }

    [Fact]
    public void A_stone_no_larger_than_the_due_items_need_goes_on_the_item_and_the_largest_such_stone_is_picked()
    {
        var worn = Worn(robe: 97);
        var due = WornManaRefill.Due(worn, HalfHour, HalfHour);

        var use = WornManaRefill.PickUse(due, worn, [ManaStone(0x11, 300), ManaStone(0x12, 650), ManaStone(0x13, 2000)], HalfHour, HalfHour);

        Assert.Equal((0x12u, Robe, 650, 0), (use.Stone.Guid, use.Target, use.PredictedTotal, use.Waste)); // on the player the Robe would get 234 of the 650
        Assert.Equal("'Mana Stone' 0x00000012 (650) on 'Pathwarden Robe' 0x8000039C, which needs 903: all of it lands there", use.Why);
    }

    [Fact]
    public void A_stone_larger_than_the_need_goes_on_the_player_when_the_ration_leaves_nothing_due_and_on_the_item_when_it_would_not()
    {
        // The Vest alone is due and needs 300; the stone holds 650: on the Vest 350 would be lost, on the player the ration fills it and the rest goes to the Robe and the Trinket.
        var worn = Worn(robe: 500, vest: 100);
        var due = WornManaRefill.Due(worn, HalfHour, 3600);
        Assert.Equal([Vest], due.Select(d => d.Item.Guid));

        var onPlayer = WornManaRefill.PickUse(due, worn, [ManaStone(0x12, 650)], HalfHour, 3600);
        Assert.Equal((0u, "the player", 0), (onPlayer.Target, onPlayer.TargetName, onPlayer.Waste));
        Assert.Equal(217, onPlayer.Predicted[Vest]); // 650 / 3 = 216 each, then the 2 left over one by one

        // Seven other worn pieces all take a share: the ration (2,000 / 8 = 250) would leave the due piece with 52 min, still due, so the stone goes on it and the rest is lost and named.
        var crowd = new List<WornMana> { new(1, "Amulet", 60, 1000, -0.1) };
        for (uint i = 2; i <= 8; i++)
        {
            crowd.Add(new WornMana(i, "Bracelet", 5000, 9000, -0.01));
        }

        var urgent = WornManaRefill.Due(crowd, HalfHour, HalfHour);
        var onItem = WornManaRefill.PickUse(urgent, crowd, [ManaStone(0x12, 2000)], HalfHour, HalfHour);
        Assert.Equal((1u, 940, 1060), (onItem.Target, onItem.PredictedTotal, onItem.Waste));
        Assert.Contains("on the player the even ration would leave 'Amulet' due; 1060 of the stone is lost", onItem.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_never_list_refuses_what_she_wears_keeps_or_needs_and_names_why()
    {
        var item = new DrainTarget(0x30, "Sceptre", 467, 467, 1290, 1161);

        Assert.Null(WornManaRefill.NeverDrain(new DrainFacts(item)));
        Assert.Equal("worn", WornManaRefill.NeverDrain(new DrainFacts(item, Worn: true)));
        Assert.Equal("a quest item", WornManaRefill.NeverDrain(new DrainFacts(item, QuestItem: true)));
        Assert.Equal("kept aside", WornManaRefill.NeverDrain(new DrainFacts(item, KeptAside: true)));
        Assert.Equal("a caster she keeps", WornManaRefill.NeverDrain(new DrainFacts(item, KeptCaster: true))); // the spare Sceptre the trips of 2026-11-11 weighed every visit
        Assert.Equal("a piece of her armour set", WornManaRefill.NeverDrain(new DrainFacts(item, ArmourSet: true)));
        Assert.Equal("attuned", WornManaRefill.NeverDrain(new DrainFacts(item, Attuned: true)));
        Assert.Equal("retained (ACE refuses the drain)", WornManaRefill.NeverDrain(new DrainFacts(item, Retained: true)));
        Assert.Equal("a supply ware", WornManaRefill.NeverDrain(new DrainFacts(item, Supply: true)));
        Assert.Equal("the pack rules keep it (value-zero-gear)", WornManaRefill.NeverDrain(new DrainFacts(item, Kept: "value-zero-gear"))); // M6 16 E5c2: any other row of the rule book that keeps it
        Assert.Equal("a supply ware", WornManaRefill.NeverDrain(new DrainFacts(item, Supply: true, Kept: "unknown"))); // the named facts come first
        Assert.Equal("not in the main pack", WornManaRefill.NeverDrain(new DrainFacts(item, MainPack: false)));
        Assert.Equal("no mana", WornManaRefill.NeverDrain(new DrainFacts(item with { Mana = 0 })));
        Assert.Equal("worn", WornManaRefill.NeverDrain(new DrainFacts(item, Worn: true, KeptCaster: true, MainPack: false))); // worn is said first
    }

    [Fact]
    public void The_capture_takes_the_loot_whose_captured_point_costs_least_much_mana_and_little_sale_value()
    {
        var decisions = new List<string>();
        var spare = new DrainFacts(new DrainTarget(0x30, "Sceptre", 467, 467, 1290, 1161), KeptCaster: true);

        var pick = WornManaRefill.PickDrain([Slippers, Ring, Staff, spare], [Lesser(0x10), ManaStone(0x12), ManaStone(0x13, 500)], null, decisions)!.Value;

        // The Mana Stone (0.6) beats the Lesser (0.25); the charged stone is not an empty one. Ring: 650 points for 2,109 + 100 (the stone's 10% of its 1,000) = 3.40 a point; Staff 3.54; Slippers 14.05.
        Assert.Equal((0x12u, 0x21u, 650), (pick.Stone.Guid, pick.Item.Guid, pick.Captured));
        Assert.Equal(3.40, pick.CostPerPoint, 2);
        Assert.Equal("'Mana Stone' 0x00000012 (efficiency 0.6) on 'Ring' 0x00000021: 1084 mana -> 650 in the stone for a payout of 2109 forgone and 100 for the stone's 10% destroy chance: 3.40 pyreals a point", pick.Why);
        Assert.Equal(4, decisions.Count);
        Assert.Contains("gear mana: refill: never drained: 'Sceptre' 0x00000030 (467 mana): a caster she keeps", decisions);
        Assert.Contains(decisions, d => d.StartsWith("gear mana: refill: weigh 'Mana Stone' 0x00000012 (efficiency 0.6) on 'Slippers'", StringComparison.Ordinal) && d.EndsWith("14.05 pyreals a point", StringComparison.Ordinal));

        Assert.Null(WornManaRefill.PickDrain([spare], [ManaStone(0x12)])); // only what the never-list refuses
        Assert.Null(WornManaRefill.PickDrain([Ring], [ManaStone(0x13, 500)])); // no empty stone
        Assert.Null(WornManaRefill.PickDrain([Ring], [new HeldStone(0x14, "Mana Stone", null)])); // an efficiency not known never drains
        Assert.Equal(0x23u, WornManaRefill.PickDrain([Ring, Staff], [ManaStone(0x12)], new HashSet<uint> { 0x21 })!.Value.Item.Guid); // an item a use failed on is not picked again
    }

    [Fact]
    public void Cindrues_cheapest_point_is_the_Tiny_Mana_Charge()
    {
        var cheapest = WornManaRefill.CheapestCharge(Cindrue)!.Value;

        Assert.Equal((4612u, 100, 473), (cheapest.Offer.Wcid, cheapest.Each, cheapest.Offer.Price));
        Assert.Equal(4.73, cheapest.PerPoint, 2); // Small 5.02, Moderate 5.79, High 6.75
        Assert.Null(WornManaRefill.CheapestCharge([new ManaOffer(2304, "Archmage Cindrue", 2472, 270)])); // a Wand is no charge
    }

    [Fact]
    public void With_a_charged_stone_held_the_due_item_is_refilled_from_it_first()
    {
        var step = WornManaRefill.Next(new ManaRefillInput(Worn(robe: 97), [ManaStone(0x12, 650), ManaStone(0x13)], [Ring], Cindrue, 50_000, HalfHour, HalfHour));

        Assert.Equal(ManaRefillAct.Use, step.Act);
        Assert.Equal((0x12u, Robe), (step.Use!.Value.Stone.Guid, step.Use.Value.Target));
        Assert.StartsWith("gear mana: refill: use: 'Mana Stone' 0x00000012 (650) on 'Pathwarden Robe' 0x8000039C, which needs 903: all of it lands there (due: 'Pathwarden Robe' 97/1000: 49 min left", step.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_charged_stone_a_capture_no_dearer_than_a_bought_point_comes_before_the_charges()
    {
        var decisions = new List<string>();
        var step = WornManaRefill.Next(new ManaRefillInput(Worn(robe: 97), [ManaStone(0x12), Lesser(0x10)], [Slippers, Ring], Cindrue, 50_000, HalfHour, HalfHour), decisions);

        Assert.Equal(ManaRefillAct.Capture, step.Act);
        Assert.Equal((0x12u, 0x21u), (step.Drain!.Value.Stone.Guid, step.Drain.Value.Item.Guid));
        Assert.Contains("3.40 pyreals a point, no dearer than a bought point (4.73) (due: 'Pathwarden Robe' 97/1000", step.Why, StringComparison.Ordinal);
        Assert.Equal(2, decisions.Count); // every item weighed
    }

    [Fact]
    public void A_capture_dearer_than_a_bought_point_gives_way_to_Cindrues_charges_and_the_count_covers_two_horizons()
    {
        // Only a Lesser stone (0.25): the Ring gives 271 points for 2,109 + 25 = 7.87 a point, against 4.73 bought.
        var step = WornManaRefill.Next(new ManaRefillInput(Worn(robe: 97), [Lesser(0x10)], [Ring], Cindrue, 50_000, HalfHour, HalfHour));

        Assert.Equal(ManaRefillAct.Buy, step.Act);
        var buy = step.Buy!.Value;
        Assert.Equal((4612u, 2304u, 2, 100, 143, 946L), (buy.Offer.Wcid, buy.Offer.VendorWcid, buy.Count, buy.Each, buy.Need, buy.Cost)); // two horizons of an hour burn 240; she holds 97
        Assert.Equal(
            "gear mana: refill: buy: 2 x wcid 4612 (100 mana each for 473: 4.73 pyreals a point) at Archmage Cindrue: the due items need 143 to last two horizons of 60 min; the cheapest capture would cost 7.87 a point (due: 'Pathwarden Robe' 97/1000: 49 min left at 0.0333/s, under the next visit in 30 min plus 30 min)",
            step.ToString());

        // Once the charges are on the buy list the rule plans no more: they come after the sale.
        var planned = WornManaRefill.Next(new ManaRefillInput(Worn(robe: 97), [Lesser(0x10)], [Ring], Cindrue, 50_000, HalfHour, HalfHour, BuyPlanned: true));
        Assert.Equal(ManaRefillAct.None, planned.Act);
        Assert.Contains("the charges on this trip's buy list are used once she holds them", planned.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_purse_short_of_one_charge_falls_back_to_the_dearer_capture_and_no_means_at_all_is_named()
    {
        var poor = WornManaRefill.Next(new ManaRefillInput(Worn(robe: 97), [Lesser(0x10)], [Ring], Cindrue, 400, HalfHour, HalfHour));
        Assert.Equal(ManaRefillAct.Capture, poor.Act);
        Assert.Contains("dearer than a bought point (4.73) but no charge can be bought (purse 400)", poor.Why, StringComparison.Ordinal);

        var nowhere = WornManaRefill.Next(new ManaRefillInput(Worn(robe: 97), [ManaStone(0x12)], [Ring], [], 50_000, HalfHour, HalfHour)); // no vendor sells a charge: the capture is the means
        Assert.Equal(ManaRefillAct.Capture, nowhere.Act);
        Assert.Contains("and no vendor here sells a charge", nowhere.Why, StringComparison.Ordinal);

        var none = WornManaRefill.Next(new ManaRefillInput(Worn(robe: 97), [ManaStone(0x12)], [], Cindrue, 400, HalfHour, HalfHour));
        Assert.Equal(ManaRefillAct.None, none.Act);
        Assert.Contains("1 item(s) due and NO MEANS (named: mana-refill-no-means): no charged stone, 1 empty stone(s) with no loot to drain, and the purse 400 is short of one charge at 473 (Archmage Cindrue)", none.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stone_or_an_item_a_use_did_nothing_with_is_not_picked_again_and_the_next_means_is()
    {
        var failed = new HashSet<uint> { 0x12 };

        // The charged stone failed: the capture with the other stone is next.
        var next = WornManaRefill.Next(new ManaRefillInput(Worn(robe: 97), [ManaStone(0x12, 650), ManaStone(0x13)], [Ring], Cindrue, 50_000, HalfHour, HalfHour, Failed: failed));
        Assert.Equal((ManaRefillAct.Capture, 0x13u), (next.Act, next.Drain!.Value.Stone.Guid));

        // The capture's item failed too: the charges are next.
        failed.Add(0x21);
        Assert.Equal(ManaRefillAct.Buy, WornManaRefill.Next(new ManaRefillInput(Worn(robe: 97), [ManaStone(0x12, 650), ManaStone(0x13)], [Ring], Cindrue, 50_000, HalfHour, HalfHour, Failed: failed)).Act);
    }

    [Fact]
    public void With_nothing_due_one_cheap_capture_keeps_a_charged_stone_for_the_field_and_no_loot_is_burned_beyond_it()
    {
        var reserve = WornManaRefill.Next(new ManaRefillInput(Worn(robe: 900), [ManaStone(0x12)], [Ring], Cindrue, 50_000, HalfHour, HalfHour));
        Assert.Equal(ManaRefillAct.Capture, reserve.Act);
        Assert.StartsWith("the field's reserve (0 charged of 1): ", reserve.Why, StringComparison.Ordinal);

        var held = WornManaRefill.Next(new ManaRefillInput(Worn(robe: 900), [ManaStone(0x12), ManaStone(0x13, 650)], [Ring], Cindrue, 50_000, HalfHour, HalfHour));
        Assert.Equal(ManaRefillAct.None, held.Act);
        Assert.Equal("gear mana: refill: nothing more: every worn item lasts past the next visit in 30 min plus 30 min; 1 charged stone(s) held", held.ToString());

        // A capture dearer than a bought point is no reserve: the Lesser stone's 7.87 against 4.73.
        Assert.Equal(ManaRefillAct.None, WornManaRefill.Next(new ManaRefillInput(Worn(robe: 900), [Lesser(0x10)], [Ring], Cindrue, 50_000, HalfHour, HalfHour)).Act);
    }

    [Fact]
    public void The_charges_bought_stop_at_the_purse_and_at_the_trips_cap()
    {
        var due = WornManaRefill.Due([new WornMana(Trinket, "Pathwarden Trinket", 0, 6000, -0.049)], HalfHour, HalfHour); // two horizons burn 353: four Tiny charges

        Assert.Equal(4, WornManaRefill.PickCharges(due, Cindrue, 50_000, 3600)!.Value.Count);
        Assert.Equal(2, WornManaRefill.PickCharges(due, Cindrue, 1000, 3600)!.Value.Count); // the purse pays two
        Assert.Null(WornManaRefill.PickCharges(due, Cindrue, 472, 3600));
        Assert.Null(WornManaRefill.PickCharges(due, [], 50_000, 3600));

        var many = WornManaRefill.Due([new WornMana(Trinket, "Pathwarden Trinket", 0, 6000, -0.5)], HalfHour, HalfHour); // 3,600 wanted in two hours: capped
        var capped = WornManaRefill.PickCharges(many, Cindrue, 50_000, 3600)!.Value;
        Assert.Equal(WornManaRefill.MaxChargesATrip, capped.Count);
        Assert.Contains("36 wanted, the purse 50000 and the cap 12 give 12", capped.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_memory_expects_the_next_visit_after_the_longest_of_her_last_gaps()
    {
        var memory = new WornManaMemory();
        Assert.Equal(WornManaRefill.DefaultVisitGapSeconds, memory.ExpectedGap); // 46 min before any gap is measured
        Assert.Equal(WornManaRefill.DefaultVisitGapSeconds, memory.SecondsToNextVisit(500));

        Assert.Equal(WornManaRefill.DefaultVisitGapSeconds, memory.NoteHomeVisit(131)); // 12:04 UTC, the first visit
        Assert.Equal(WornManaRefill.DefaultVisitGapSeconds, memory.NoteHomeVisit(160)); // a trip run again 29 s later is the same visit
        Assert.Equal(1274, memory.NoteHomeVisit(1434)); // 12:26
        Assert.Equal(1529, memory.NoteHomeVisit(2963)); // 12:51
        Assert.Equal(1755, memory.NoteHomeVisit(4718)); // 13:20: the longest of the three
        Assert.Equal(1755, memory.NoteHomeVisit(5400)); // 682 s: the longest of the last three stays 1,755
        Assert.Equal(1755 - 600, memory.SecondsToNextVisit(6000));
        Assert.Equal(WornManaMemory.FieldFloorSeconds, memory.SecondsToNextVisit(9000)); // overdue: an item with under ten minutes left is still the field's

        Assert.True(memory.HomeStepActive(5400 + 60));
        Assert.False(memory.HomeStepActive(5400 + WornManaMemory.HomeHoldSeconds));
        memory.TouchHome(5600);
        Assert.True(memory.HomeStepActive(5700));

        Assert.False(memory.RewearTried(Robe));
        memory.MarkRewear(Robe);
        Assert.True(memory.RewearTried(Robe));

        Assert.Equal(WornManaRefill.DefaultVisitGapSeconds, memory.NoteHomeVisit(10)); // the clock went back (a new session): the gaps are forgotten
        memory.Reset();
        Assert.False(memory.RewearTried(Robe));
    }
}
