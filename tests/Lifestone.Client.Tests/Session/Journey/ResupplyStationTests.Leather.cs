using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 14 (the owner's order of 2026-11-12: leather in place of the plate): the supply trip's part of the leather swap (<c>ResupplyStation.Leather.cs</c>) over the scripted shops: the climber of the floor tests (the Pathwarden Robe and
/// the chest's gauntlets worn, the Training Wand in her hand), the blacksmith of the quest file's <c>leather</c> block at his place with his wares at the block's prices. A purse that does not pay: nothing of the set is bought, the
/// line is said, the boots are kept from the sale. A purse that pays: every missing piece is bought at his counter, the robe comes off and the set goes on, and no part of the body is bare.
/// </summary>
public sealed partial class ResupplyStationTests
{
    /// <summary>The blacksmith placed in the home, with his shop scripted from the set's wares.</summary>
    private static void PlaceSmith(Home home)
    {
        var smith = LeatherSet.Default!.Vendor;
        home.Vendor.PlaceAt(smith.Guid, smith.Name, smith.Cell, smith.Point, 3f, smith.Wcid);
        var shop = home.Shops.Add(smith.Guid, smith.BuysItemTypes);
        var row = 0x80009000u;
        foreach (var ware in smith.Sells)
        {
            shop.Rows.Add((-1, ++row, ware.Name, ware.Wcid, ClothingType, ware.Price));
        }
    }

    [Fact]
    public void M6_14_a_purse_that_does_not_pay_for_the_leather_set_buys_none_of_it_says_so_and_keeps_the_boots_from_the_sale()
    {
        var f = new FloorHome(coins: 3_000, notes: false);
        PlaceSmith(f.Home);
        f.OwnSpareWand();
        var boots = f.Home.Own("Leather Boots", 115, ClothingType, 1_100); // the boots she was created in, taken off for the robe and kept by the Pathwarden station
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        Assert.Equal("purse", run.Leather);
        Assert.Single(f.Home.Lines, l => l.Contains("leather: held back: the 6 missing piece(s) of 8", StringComparison.Ordinal));
        Assert.True(f.Home.Logged("the purse 3000 less this trip's other buys 0 leaves 3000, and the trip's share of it (60%) is 1800, short by 844: none is bought and the robe stays on"), string.Join("\n", f.Home.Lines.Where(l => l.Contains("leather", StringComparison.Ordinal))));
        Assert.DoesNotContain(run.BuyList, b => b.Vendor.Wcid == 712);
        Assert.True(f.Home.Logged("sale: 'Leather Boots'") && f.Home.Logged("is a piece of the leather set and the swap is still to come: kept, not offered"));
        Assert.True(f.Inventory.Contains(boots)); // never sold: Renald would pay 990 for them
        Assert.Equal(0, run.Sold);
        Assert.Equal(f.Robe, f.Inventory.EquippedAt(0x7F00)); // the robe stays on
        Assert.Null(run.Failure);
    }

    [Fact]
    public void M6_14_a_purse_that_pays_buys_every_missing_piece_at_the_blacksmith_and_the_set_goes_on_in_the_robes_place_as_one_swap()
    {
        var f = new FloorHome(coins: 10_000, notes: false);
        PlaceSmith(f.Home);
        f.OwnSpareWand();
        var boots = f.Home.Own("Leather Boots", 115, ClothingType, 1_100);
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        var lines = f.Home.Lines.Where(l => l.Contains("leather", StringComparison.Ordinal)).ToList();
        var all = string.Join("\n", lines);
        Assert.True(f.Home.Logged("leather: due: the 6 missing piece(s) of 8 (Metal Cap 92, Studded Leather Breastplate 1813, Studded Leather Girth 200, Studded Leather Pauldrons 138, Studded Leather Bracers 138, Studded Leather Leggings 263) cost 2644 at Sedor Wystan the Blacksmith"), all);
        Assert.Equal([46u, 42u, 63u, 89u, 38u, 84u], run.BuyList.Where(b => b.Vendor.Wcid == 712).Select(b => b.Line.Wcid)); // last in the list, at his counter
        Assert.Equal(2_644L, run.Spent);
        Assert.True(f.Home.Logged("gear floor: bought 1 x Metal Cap at Sedor Wystan the Blacksmith: check after: held 0 -> 1, paid 92"), all);
        Assert.True(f.Home.Logged("gear floor: buy 1 x Studded Leather Breastplate at Sedor Wystan the Blacksmith: check before: open 0x7A9B4026 yes, the profile's price 1813"), string.Join("\n", f.Home.Lines.Where(l => l.Contains("Breastplate", StringComparison.Ordinal))));

        // The swap: the robe off, the six pieces and the boots on; the chest's gauntlets were on already.
        Assert.Equal("worn", run.Leather);
        Assert.True(f.Home.Logged("leather: swap: check before: 'Pathwarden Robe'"), all);
        Assert.True(f.Home.Logged("leather: swap done: 7 piece(s) put on, the set of 8 is worn in place of 'Pathwarden Robe', no part of the body is bare (0x0)"), all);
        Assert.NotEqual(InventoryPlaceKind.Equipped, f.Inventory.Find(f.Robe).Kind);
        Assert.True(f.Inventory.Contains(f.Robe)); // in the pack, for its return to Thorolf: attuned, so no vendor is offered it
        Assert.Equal(InventoryPlaceKind.Equipped, f.Inventory.Find(boots).Kind);
        var worn = SelfEquipJudge.Worn(f.Home.Vendor.State);
        Assert.Equal(0u, GearFloor.BareParts(worn));
        Assert.NotNull(GearFloor.ChestPiece(worn));
        Assert.Equal(8, worn.Count(w => LeatherSet.Default!.Pieces.Any(p => (GearCoverage.OfWorn(w.Coverage, w.Location) & p.Coverage) != 0)));
        Assert.Null(run.Failure);
        Assert.True(lines.FindIndex(l => l.Contains("leather: due", StringComparison.Ordinal)) < lines.FindIndex(l => l.Contains("leather: swap done", StringComparison.Ordinal)));
    }

    [Fact]
    public void M6_14_a_piece_that_will_not_go_on_undoes_the_swap_and_the_robe_is_worn_again()
    {
        var f = new FloorHome(coins: 10_000, notes: false);
        PlaceSmith(f.Home);
        f.OwnSpareWand();
        var boots = f.Home.Own("Leather Boots", 115, ClothingType, 1_100);
        f.RefuseOnly.Add(boots); // ACE refuses the boots' wield: the feet would be bare
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.NotEqual(ScenarioStatus.Running, f.Run(run));

        var all = string.Join("\n", f.Home.Lines.Where(l => l.Contains("leather", StringComparison.Ordinal)));
        Assert.Equal("failed", run.Leather);
        Assert.True(f.Home.Logged("leather: swap: recovery=leather-wield-again: 'Leather Boots' is not worn after the Wield; one more Wield"), all);
        Assert.True(f.Home.Logged("leather: swap: recovery=leather-swap-undo: 'Leather Boots' would not go on"), all);
        Assert.True(f.Home.Logged("'Pathwarden Robe' back on at 0x7F00"), all);
        Assert.True(f.Home.Logged("worn again: the body is covered as before"), all);
        Assert.Equal(f.Robe, f.Inventory.EquippedAt(0x7F00)); // never left bare
        Assert.DoesNotContain(SelfEquipJudge.Worn(f.Home.Vendor.State), w => w.Name.StartsWith("Studded", StringComparison.Ordinal) || w.Name == "Metal Cap");
    }

    [Fact]
    public void M6_14_a_blacksmith_who_is_not_found_leaves_the_robe_on_and_the_next_trip_tries_the_buy_again()
    {
        var f = new FloorHome(coins: 10_000, notes: false); // no blacksmith stands in this home yet
        f.OwnSpareWand();
        f.Home.Own("Leather Boots", 115, ClothingType, 1_100);
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        Assert.Equal("failed", run.Leather);
        Assert.Equal(0L, run.Spent);
        Assert.True(f.Home.Logged("WARNING: leather: the swap waits (named: leather-pieces-missing: 6 of 8 piece(s) were not held after the trip's buys at Sedor Wystan the Blacksmith"));
        Assert.True(f.Home.Logged("the next trip tries the buy again"));
        Assert.Equal(f.Robe, f.Inventory.EquippedAt(0x7F00));

        // The next trip of the same run: nothing is remembered against him; he stands there now, the set is bought and worn.
        PlaceSmith(f.Home);
        var next = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));
        Assert.Equal(ScenarioStatus.Succeeded, f.Run(next));
        Assert.Equal("worn", next.Leather);
        Assert.Equal(2_644L, next.Spent);
        Assert.NotEqual(InventoryPlaceKind.Equipped, f.Inventory.Find(f.Robe).Kind);
    }

    private const string LaneFailure = "travel:run:blocked: run 'journey-vendor-22' to object 0x7A9B4026 within 6.0 m: plan 2 rung 0: no path: npc-in-lane:0x8000039D";

    [Fact]
    public void M6_14_a_body_in_the_lane_to_the_blacksmith_is_waited_out_once_and_the_second_walk_buys_the_set()
    {
        // Live 2026-11-12 01:02 UTC: the walk failed npc-in-lane and the set was put off for the run. Now: one more walk after a short wait, on the same trip.
        var f = new FloorHome(coins: 10_000, notes: false);
        PlaceSmith(f.Home);
        f.OwnSpareWand();
        f.Home.Own("Leather Boots", 115, ClothingType, 1_100);
        var walks = 0;
        f.Home.Travel.FailFor = name => name == "Sedor Wystan the Blacksmith" && ++walks == 1 ? LaneFailure : null;
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        Assert.Equal(2, walks); // the walk that failed and the one after the wait
        Assert.True(f.Home.Logged("a body stands in the lane (npc-in-lane), which clears by itself: waiting 5 s, then one more walk on this trip (recovery=leather-walk-again)"), string.Join("\n", f.Home.Lines.Where(l => l.Contains("Sedor", StringComparison.Ordinal))));
        Assert.Equal("worn", run.Leather);
        Assert.Equal(2_644L, run.Spent);
        Assert.True(f.Home.Logged("leather: swap done: 7 piece(s) put on"));
        Assert.Equal(0, run.VendorsSkipped);
    }

    [Theory]
    [InlineData(LaneFailure, 2)] // a body in the lane: one more walk after the wait, then the stop is skipped for this trip
    [InlineData("travel:run:blocked: no path: no-route to 0xA9B40100", 1)] // any other failure: one walk a trip, as every vendor
    public void M6_14_a_walk_that_keeps_failing_is_named_the_robe_stays_on_and_only_a_lane_failure_is_walked_twice(string failure, int expected)
    {
        var f = new FloorHome(coins: 10_000, notes: false);
        PlaceSmith(f.Home);
        f.OwnSpareWand();
        f.Home.Own("Leather Boots", 115, ClothingType, 1_100);
        var walks = 0;
        f.Home.Travel.FailFor = name =>
        {
            if (name != "Sedor Wystan the Blacksmith")
            {
                return null;
            }

            walks++;
            return failure;
        };
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        Assert.Equal(expected, walks);
        Assert.Equal(1, run.VendorsSkipped);
        Assert.Equal("failed", run.Leather);
        Assert.True(f.Home.Logged("WARNING: leather: the swap waits (named: leather-pieces-missing: 6 of 8 piece(s)") && f.Home.Logged("; the walk: " + failure), string.Join("\n", f.Home.Lines.Where(l => l.Contains("leather", StringComparison.Ordinal))));
        Assert.True(f.Home.Logged("the next trip tries the buy again"));
        Assert.Equal(f.Robe, f.Inventory.EquippedAt(0x7F00));
        Assert.Equal(0L, run.Spent);
    }

    [Fact]
    public void M6_14_the_live_case_a_looted_pair_worn_over_the_hands_is_left_on_and_the_six_in_the_pack_go_on_with_no_buy_and_no_refused_wield()
    {
        // Live 2026-11-12 02:10 to 02:17 UTC: the gear pass wore a looted pair of Long Leather Gauntlets in place of the Leather Gauntlets (which went to the pack), the swap then asked ACE to wield the Leather Gauntlets over
        // them, ACE refused ("You must remove your Long Leather Gauntlets to wield Leather Gauntlets") and the swap undid itself. She now holds the six bought pieces, the Leather Gauntlets and the Leather Boots in her pack,
        // the robe on and the looted pair on her hands.
        var f = new FloorHome(coins: 500, notes: false, gauntlets: false);
        PlaceSmith(f.Home);
        f.OwnSpareWand();
        var looted = f.Home.OwnPiece("Long Leather Gauntlets", 9_301, HandWear, Hands, value: 4_000);
        f.Home.Vendor.R.Server((p, s) => AceM3Writers.WieldObject(p, s, looted, (int)HandWear));
        var pair = f.Home.Own("Leather Gauntlets", 56, ClothingType, 1_100);
        f.RefuseOnly.Add(pair); // as ACE: the pair cannot go on over the worn one
        var boots = f.Home.Own("Leather Boots", 115, ClothingType, 1_100);
        var bought = new[] { ("Metal Cap", 46u), ("Studded Leather Breastplate", 42u), ("Studded Leather Girth", 63u), ("Studded Leather Pauldrons", 89u), ("Studded Leather Bracers", 38u), ("Studded Leather Leggings", 84u) }
            .Select(p => f.Home.Own(p.Item1, p.Item2, ClothingType, 100)).ToList();
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        var all = string.Join("\n", f.Home.Lines.Where(l => l.Contains("leather", StringComparison.Ordinal)));
        Assert.True(f.Home.Logged("leather: every one of the 8 parts is held or covered"), all); // nothing is bought again
        Assert.DoesNotContain(run.BuyList, b => b.Vendor.Wcid == 712);
        Assert.Equal(0L, run.Spent);
        Assert.Equal(0, run.Sold); // the six, the pair and the boots were kept from the sale while the swap was still to come
        Assert.True(f.Home.Logged("covered already, left as it is: 'Long Leather Gauntlets' over the 'Studded Leather Gauntlets' part 0x8000"), all);
        Assert.True(f.Home.Logged("leather: swap: 'Long Leather Gauntlets' over the 'Studded Leather Gauntlets' part 0x8000: worn already"), all);
        Assert.Equal(0, f.WieldsRefused); // no Wield was sent for the pair ACE would refuse
        Assert.Equal("worn", run.Leather);
        Assert.True(f.Home.Logged("leather: swap done: 7 piece(s) put on, the set of 8 is worn in place of 'Pathwarden Robe' (1 part(s) by a piece she wore already), no part of the body is bare (0x0)"), all);
        Assert.DoesNotContain(f.Home.Lines, l => l.Contains("leather-swap-undo", StringComparison.Ordinal));
        Assert.Equal(looted, f.Inventory.EquippedAt(HandWear)); // the better pair stays on her hands
        Assert.NotEqual(InventoryPlaceKind.Equipped, f.Inventory.Find(pair).Kind);
        Assert.NotEqual(InventoryPlaceKind.Equipped, f.Inventory.Find(f.Robe).Kind);
        Assert.All(bought.Append(boots), g => Assert.Equal(InventoryPlaceKind.Equipped, f.Inventory.Find(g).Kind));
        Assert.Equal(0u, GearFloor.BareParts(SelfEquipJudge.Worn(f.Home.Vendor.State)));
    }

    [Fact]
    public void M6_14_a_piece_of_the_set_she_wears_already_is_counted_on_by_name_and_no_wield_is_sent_for_it()
    {
        var f = new FloorHome(coins: 10_000, notes: false); // the chest's gauntlets worn: they fill the hands
        PlaceSmith(f.Home);
        f.OwnSpareWand();
        f.Home.Own("Leather Boots", 115, ClothingType, 1_100);
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        Assert.Equal("worn", run.Leather);
        Assert.True(f.Home.Logged("leather: swap: 'Pathwarden Gauntlets'") && f.Home.Logged("is worn already (coverage 0x8000): counted on, no Wield sent"));
        Assert.Equal(7, f.Home.Vendor.H.Net.Of(FakeSendKind.GetAndWieldItem).Count()); // the six and the boots; none for the pair on her hands
    }

    [Fact]
    public void M6_14_a_trip_with_no_gear_phase_plans_no_leather_and_says_nothing_of_it()
    {
        // The den's mage on a plain supply trip (no gear phase), a rich purse, a vendor robe: nothing of the set, not a line (the rule on who wears what is LeatherSetTests').
        var home = new Home("holtburg-outskirts", 100_000);
        PlaceSmith(home);
        var robe = home.Own("Faran Robe with Hood", 5851, ClothingType, 50);
        home.Vendor.R.Server((p, s) => AceM3Writers.WieldObject(p, s, robe, 0x7F01));
        var run = home.Start(new ResupplyOptions(true, () => []));
        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));
        Assert.Equal("n/a", run.Leather);
        Assert.DoesNotContain(home.Lines, l => l.Contains("leather", StringComparison.Ordinal));
        Assert.DoesNotContain(run.BuyList, b => b.Vendor.Wcid == 712);
    }
}
