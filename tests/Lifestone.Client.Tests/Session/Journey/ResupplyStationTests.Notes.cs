using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M5 7 D136 point 3: trade notes stack as in retail. Every note weenie stacks to 250 and the coin to 25,000 (ACE-World-Database v0.9.296, PropertyInt 11); ACE never joins a bought or paid stack to a held one (<c>Player_Commerce.cs</c>
/// 59-102 and 194-218, <c>Container.cs</c> 499-575), so the trip joins them with GameAction 0x0054 into a stack in the main pack (D121), and changes one note of a stack by splitting it off first (0x0055), for ACE sells the whole object.
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const uint Note250k = 20630;
    private const uint Note10k = 2625;
    private const ushort NoteMax = 250;

    [Fact]
    public void M5_7_D136_the_bought_trade_notes_and_the_coin_join_their_held_stacks_in_the_main_pack()
    {
        var home = new Home(Den, 615_000, notes: false); // 575,000 above the floor: two 250,000 notes and nothing else (the follow-up's greedy)
        home.ShopOf(Note250k).MaxStacks[Note250k] = NoteMax;
        var held = home.Own("Trade Note (250,000)", Note250k, VendorStock.PromissoryNoteType, 750_000, stack: 3, maxStack: NoteMax);
        var coinA = home.Own("Pyreal", 273, 0x40, 3_000, stack: 3_000, maxStack: 25_000);
        var coinB = home.Own("Pyreal", 273, 0x40, 5_000, stack: 5_000, maxStack: 25_000);
        var run = home.Start(new ResupplyOptions(true));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        Assert.Equal(2, run.NotesBought); // (615,000 - 40,000) / 287,500, in one buy: ACE makes one stack of them (Vendor.cs 392-424)
        var inventory = home.Vendor.State.Self.Inventory;
        var notes = inventory.Pack.Where(e => home.Vendor.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == Note250k).Select(e => e.Guid).ToList();
        Assert.Equal([held], notes); // the bought stack merged away into the held one
        Assert.True(inventory.TryGetStack(held, out var size, out _));
        Assert.Equal(5u, size);
        Assert.Equal(InventoryPlaceKind.MainPack, inventory.Find(held).Kind);
        Assert.True(home.Logged("resupply: merging a bought Trade Note (250,000) stack"));
        Assert.True(home.Logged($"purse: Trade Note (250,000) held in 1 stack(s): 0x{held:X8} x5 (main pack)"));
        Assert.False(home.Logged("TODO(m5-note-merge)"));

        // the coin: the smaller stack joins the larger (the looter's rule), with the same verb
        Assert.False(inventory.Contains(coinA));
        Assert.True(inventory.TryGetStack(coinB, out var coins, out _));
        Assert.Equal(8_000u, coins);
        Assert.True(home.Logged("resupply: merging a held Pyreal stack"));
        Assert.Equal(2, home.Shops.Merges);
        Assert.Equal(0, run.MergesRefused);
    }

    [Fact]
    public void M5_7_D136_a_note_stack_in_a_side_pack_is_never_a_merge_target_and_comes_home_to_the_main_packs_stack()
    {
        var home = new Home(Den, 615_000, notes: false); // 575,000 above the floor: two 250,000 notes and nothing else (the follow-up's greedy)
        home.ShopOf(Note250k).MaxStacks[Note250k] = NoteMax;
        var pack = home.Vendor.NextGuid();
        home.Vendor.Arrive(pack, "Pack", 136, 0x200, 65);
        home.Vendor.R.Raw(AceM3Writers.PutObjInContainer(VendorRig.Player, 9_002, pack, VendorRig.Player, 0, 1)); // a side pack (ContainerType 1)
        var away = home.Vendor.NextGuid();
        home.Vendor.Arrive(away, "Trade Note (250,000)", Note250k, VendorStock.PromissoryNoteType, 750_000, 3, NoteMax);
        home.Vendor.Put(away, pack);
        var run = home.Start(new ResupplyOptions(true));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        var inventory = home.Vendor.State.Self.Inventory;
        Assert.False(inventory.Contains(away)); // the side pack's stack was the source, never the target
        var bought = inventory.Pack.Single(e => home.Vendor.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == Note250k).Guid;
        Assert.Equal(InventoryPlaceKind.MainPack, inventory.Find(bought).Kind);
        Assert.True(inventory.TryGetStack(bought, out var size, out _));
        Assert.Equal(5u, size);
        Assert.True(home.Logged($"purse: Trade Note (250,000) held in 1 stack(s): 0x{bought:X8} x5 (main pack)"));
    }

    [Fact]
    public void M5_7_D136_a_note_changed_from_a_stack_is_split_off_first_and_only_that_one_is_sold()
    {
        var home = new Home(Den, 100, held: l => l.Name == "Prismatic Taper" ? l.Keep - 10 : l.Keep, notes: false);
        var stack = home.Own("Trade Note (10,000)", Note10k, VendorStock.PromissoryNoteType, 30_000, stack: 3, maxStack: NoteMax);
        var run = home.Start();

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        var price = home.Ground.PriceOf(Line(home.Ground, "Prismatic Taper").Wcid)!.Value;
        Assert.Equal(1, run.NotesSold);
        Assert.Equal(1, home.Shops.Splits);
        Assert.True(home.Logged($"purse: split one Trade Note (10,000)"));
        Assert.True(home.Logged($"from its stack 0x{stack:X8} (x3) to change it"));
        Assert.True(home.Vendor.State.Self.Inventory.TryGetStack(stack, out var left, out _));
        Assert.Equal(2u, left); // the rest of the stack stays
        Assert.Equal(100 + 10_000 - (10 * price), home.Vendor.State.Self.Qualities.CoinValue); // one note's face, not the stack's (D49)
        Assert.Equal(10, run.Bought);
    }

    [Fact]
    public void M5_7_D136_the_note_prices_are_ACEs_and_the_greedy_plan_of_soak_round_13_leaves_under_the_smallest_price()
    {
        long[] prices = [.. PurseNotes.All.Select(n => PurseNotes.PriceOf(n.Value))];
        Assert.Equal([287_500L, 230_000, 172_500, 115_000, 57_500, 11_500, 5_750, 1_150, 575, 115], prices); // max(1, ceil(1.15 x Value - 0.1)), Vendor.cs 577-585

        // the planner over every note: round 13's 110,021 above the floor would go down to 81 in five denominations
        var (all, allLeft) = PurseNotes.Greedy(150_021 - 40_000, PurseNotes.All.Select(n => new PurseNotes.Offer(n, PurseNotes.PriceOf(n.Value))));
        Assert.Equal(
            [("Trade Note (50,000)", 1), ("Trade Note (10,000)", 4), ("Trade Note (5,000)", 1), ("Trade Note (500)", 1), ("Trade Note (100)", 1)],
            all.Select(p => (p.Note.Name, p.Count)));
        Assert.Equal(81, allLeft);

        // the den's min_note (25,000, one coin stack: a smaller note saves no slot and costs 15%): one 50,000 and 52,521 stays coin
        var minNote = GroundCatalog.Get(Den).Supplies.Purse.MinNote;
        Assert.Equal(25_000, minNote);
        var (picks, left) = PurseNotes.Greedy(150_021 - 40_000, PurseNotes.All.Where(n => n.Value >= minNote).Select(n => new PurseNotes.Offer(n, PurseNotes.PriceOf(n.Value))));
        Assert.Equal([("Trade Note (50,000)", 1)], picks.Select(p => (p.Note.Name, p.Count)));
        Assert.Equal(52_521, left);
    }

    [Fact]
    public void M5_7_D136_soak_round_13_s_trip_buys_the_notes_of_one_coin_stack_and_up_merges_each_into_its_stack_and_logs_the_decision()
    {
        var home = new Home(Den, 135_021, notes: false); // M6 14 pack rules: the float is one coin stack (25,000) where the purse's coin was 40,000, so the same 110,021 lies above it
        var tenK = home.Own("Trade Note (10,000)", Note10k, VendorStock.PromissoryNoteType, 10_000, stack: 1, maxStack: NoteMax);
        var fiftyK = home.Own("Trade Note (50,000)", 2626, VendorStock.PromissoryNoteType, 250_000, stack: 5, maxStack: NoteMax);
        home.Own("Trade Note (250,000)", Note250k, VendorStock.PromissoryNoteType, 250_000, stack: 1, maxStack: NoteMax);
        var run = home.Start(new ResupplyOptions(true));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        Assert.True(home.Logged("purse: coin 135021, floor 25000 (float 25000: the trip's own spend 0 "));
        Assert.True(home.Logged("), above 110021, notes of 25000 and up (a smaller note saves no pack slot and costs 15%): buy 1 x Trade Note (50,000) at 57500; 52521 stays coin (under 57500, the Trade Note (50,000)'s price)"));
        Assert.Equal(1, run.NotesBought);
        Assert.Equal(77_521, home.Vendor.State.Self.Qualities.CoinValue); // four coin stacks, from six
        var inventory = home.Vendor.State.Self.Inventory;
        Assert.True(inventory.TryGetStack(fiftyK, out var fifties, out _));
        Assert.Equal(6u, fifties); // the bought one joined the held five
        Assert.True(inventory.Contains(tenK)); // no 10,000 bought: the held one stays as it was
        foreach (var note in PurseNotes.All)
        {
            var stacks = inventory.Pack.Count(e => home.Vendor.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == note.Wcid);
            Assert.True(stacks <= 1, $"{note.Name}: {stacks} stacks"); // one stack a denomination
        }

        Assert.True(home.Logged("purse: bought 1 x Trade Note (50,000) at Archmage Cindrue for 57500 pyreals (57500 each"));
        Assert.False(home.Logged("x Trade Note (10,000) at Archmage Cindrue"));
        Assert.True(home.Logged("purse: done at Archmage Cindrue: the coin 77521, 52521 above the floor 25000"));
        Assert.Equal(0, run.MergesRefused);
    }

    /// <summary>Soak round 13's three coin merges, the stacks read back from the log's sizes: each case's full 25,000 stack is never touched and the merges end with one partial stack.</summary>
    [Theory]
    [InlineData("19:59:41", new uint[] { 0x80003548, 0x8000349C, 0x800005BD, 0x800005BE }, new[] { 5_000, 14_534, 6_249, 25_000 }, "80003548>8000349C x5000; 800005BD>8000349C x5466")]
    [InlineData("20:00:20", new uint[] { 0x800005BD, 0x8000075E, 0x800005BE }, new[] { 783, 5_883, 25_000 }, "800005BD>8000075E x783")]
    [InlineData("20:00:23", new uint[] { 0x8000075E, 0x8000033B, 0x800005BE, 0x80000479 }, new[] { 1_061, 9_426, 6_666, 25_000 }, "8000075E>8000033B x1061; 800005BE>8000033B x6666")]
    public void M5_7_D136_the_coin_merge_fills_partials_smallest_into_largest_never_from_a_full_stack_and_ends_with_no_ping_pong(string at, uint[] guids, int[] sizes, string expected)
    {
        var rig = new VendorRig();
        for (var i = 0; i < guids.Length; i++)
        {
            rig.Arrive(guids[i], "Pyreal", 273, 0x40, (uint)sizes[i], (ushort)sizes[i], 25_000);
            rig.Put(guids[i]);
        }

        rig.Tick();
        var merges = new List<string>();
        var scratch = new List<uint>();
        var size = guids.Select((g, i) => (g, sizes[i])).ToDictionary(p => p.g, p => p.Item2);
        while (Lifestone.Client.Session.Fighter.StackMerge.TryPlanFor(rig.State, 273, scratch, [], out var from, out var to, out var amount, mainPackTarget: true))
        {
            Assert.True(merges.Count < guids.Length, $"{at}: more merges than stacks: {string.Join("; ", merges)}");
            Assert.True(size[from] < 25_000 && size[to] < 25_000, $"{at}: a full stack in the merge {from:X8} > {to:X8}");
            merges.Add($"{from:X8}>{to:X8} x{amount}");
            size[from] -= amount;
            size[to] += amount;
            if (size[from] == 0)
            {
                rig.Remove(from);
            }
            else
            {
                rig.Stack(from, (uint)size[from], (uint)size[from]);
            }

            rig.Stack(to, (uint)size[to], (uint)size[to]);
            rig.Tick();
        }

        Assert.Equal(expected, string.Join("; ", merges));
        Assert.True(size.Values.Count(s => s is > 0 and < 25_000) <= 1, $"{at}: more than one partial stack left");
        Assert.Equal(sizes.Sum(), size.Values.Sum());
    }

    private const uint Outskirts10k = 2625; // Trade Note (10,000)

    /// <summary>
    /// M6 item 14 (the den run's trip, the owner's order of 2026-11-13: turn the coin into notes before the run): while the order's fact stands the purse's floor is 0, so the same 40,000 that stays coin on an ordinary trip buys
    /// three notes of 10,000 at 11,500 each and 5,500 is left as coin, under the smallest note's price.
    /// </summary>
    [Fact]
    public void M6_14_an_order_that_turns_the_coin_into_notes_buys_notes_down_to_a_floor_of_nothing()
    {
        var home = new Home("holtburg-outskirts", 40_000);
        home.ShopOf(Outskirts10k).MaxStacks[Outskirts10k] = NoteMax;
        var run = home.Start(new ResupplyOptions(true), facts: f => f.CoinToNotes = true);

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        Assert.Equal(3, run.NotesBought);
        Assert.True(home.Logged("purse: coin 40000, floor 0 (the order turns the coin into notes), above 40000, notes of 10000 and up"));
        Assert.True(home.Logged("purse: bought 3 x Trade Note (10,000)"));
    }

    [Fact]
    public void M6_7_AU_the_outskirts_climber_keeps_her_coin_a_note_costs_15_percent_and_saves_no_slot_she_needs()
    {
        var home = new Home("holtburg-outskirts", 40_000); // M6 14 pack rules: 15,000 above the float of one coin stack (it was 10,000 above the 30,000 floor, M6 8 I): one 10,000 note would leave 28,500 in two stacks, as 40,000 takes now, so it stays coin
        home.ShopOf(Outskirts10k).MaxStacks[Outskirts10k] = NoteMax;
        var run = home.Start(new ResupplyOptions(true));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        Assert.Equal(0, run.NotesBought);
        Assert.Equal(10_000, home.Ground.Supplies.Purse.MinNote);
        Assert.True(home.Logged("purse: coin 40000, floor 25000 (float 25000: the trip's own spend 0 "));
        Assert.True(home.Logged("), above 15000, notes of 10000 and up (a smaller note saves no pack slot and costs 15%): no notes: 1 x Trade Note (10,000) would leave the coin in as many slots or more, a note stack counted (2 coin stacks now): a note costs 15% and frees no slot; it stays coin"));
        Assert.False(home.Logged("purse: bought"));
    }

    [Fact]
    public void M6_7_AU_a_trade_note_the_profile_does_not_list_is_changed_to_coin_before_the_buy()
    {
        // 2026-11-06 18:31: "short by 2920 ... no note is left" with a Trade Note (100,000) held: NextNote read only the faces the profile's purse lists (the 10,000). A scrivener's mask holds PromissoryNote (0x40000) and ACE does not check MerchandiseMaxValue on a sale.
        var home = new Home("holtburg-outskirts", 100, held: l => l.Name == "Prismatic Taper" ? l.Keep - 10 : l.Keep, notes: false);
        home.Own("Trade Note (100,000)", 2627, VendorStock.PromissoryNoteType, 100_000);
        var run = home.Start();

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        Assert.Equal(1, run.NotesSold);
        Assert.Equal(0, run.Short);
        Assert.True(home.Logged("purse: changed Trade Note (100,000) to coin"));
        Assert.False(home.Logged("no note is left"));
    }

    [Fact]
    public void M6_2_d_the_creation_stack_stays_coin_and_the_floor_is_what_one_full_trip_spends()
    {
        var home = new Home("holtburg-outskirts", 10_000); // what the climber starts with (starterGear.json): under the float of one coin stack (M6 14 pack rules; the 30,000 floor of M6 8 I before), so nothing above it
        var run = home.Start(new ResupplyOptions(true));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        Assert.Equal(0, run.NotesBought);
        Assert.True(home.Logged("purse: coin 10000, floor 25000 (float 25000: the trip's own spend 0 "));
        Assert.True(home.Logged("), above 0, notes of 10000 and up"));
        Assert.True(home.Logged("no notes: the coin above the floor is under the smallest such note's price (Trade Note (10,000) at 11500); it stays coin"));

        // the floor is a full restock from nothing (every stock line at its keep, the vendors' prices) plus the floor's Wand and Faran Robe, rounded up to the next hundred
        var ground = home.Ground;
        var restock = ground.Supplies.Stock.Sum(l => (long)l.Keep * ground.PriceOf(l.Wcid)!.Value);
        var trip = restock + ground.PriceOf(2472)!.Value + ground.PriceOf(5851)!.Value;
        Assert.Equal(16_091, restock); // M6 14 (m6-life-through): 21 for Mandrake (14) and Hemlock Talisman (7) at keep 1; M6 14 T: 264 for two more of each of the twelve full-formula lines (keep 4); M6 8 (the owner, 2026-11-11): 11,270 for forty-nine more Mana Potions at 230, a top-up bought last inside the trip budget; M6 7 AR: 1,014 for two more Copper, Silver, and one more Red and Indigo; M6 7 AN: 21 for Alder Talisman and Damiana; M6 7 AK: 849 for the 21 new component lines at keep 1, bought last, inside the trip's budget (m6-buff-components-early); M6 7 lead: four more Iron Scarabs (keep 6) at 68; M6 7 X: 89 for the four protective lines at keep 1; M6 7 AD: 21 for Saffron and Quicksilver at keep 1
        Assert.Equal(16_429, trip); // M6 8: with the fifty Mana Potions; M6 14 T: and the twelve lines at keep 4; M6 14 (m6-life-through): and Harm Other's two components
        Assert.Equal(30_000, ground.Supplies.Purse.Coin); // M6 8 I: the floor is the purse's coin, 30,000 as the Burrow and the Lodge; it covers a full trip (16,429) nearly twice
        Assert.InRange(ground.Supplies.Purse.Coin - trip, 0, 30_000);

        // the coin a death can take is under half the floor plus the smallest note's price (8,000); the default 25,000 would leave 30,000 exposed
        var exposed = (ground.Supplies.Purse.Coin + (long)Math.Ceiling((1.15f * ground.Supplies.Purse.MinNote) - 0.1)) / 2;
        Assert.Equal(10_000, ground.Supplies.Purse.MinNote); // M6 8 I: ACE takes half of loose coin and no trade note (Player_Death.cs:675-685), so the coin above the floor is converted from the 10,000 note up; exposed is half of the floor and one note's price
        Assert.InRange(exposed, 1, 150_000);
    }

    /// <summary>M6 8 I: the coin a climber of the first bands carried when the purse rule was read live (round 1 of 2026-11-09, level 11, the outskirts: 89,190 pyreals, and the purse said "no notes").</summary>
    private const long CoinHeldAtLevel11 = 89_190;

    /// <summary>The coin a climber of a band holds: the live figure at level 11 scaled by the band's first level over 11 (an estimate that grows with the level; TODO(m6-band-coin): read the real figure from a gate's log).</summary>
    private static long CoinHeldAtTheBand(int bandFrom) => CoinHeldAtLevel11 * Math.Max(11, bandFrom) / 11;

    [Fact]
    public void M6_8_I_the_purse_rule_binds_at_every_rung_of_the_ladder_at_the_coin_a_climber_holds()
    {
        var failures = new List<string>();
        foreach (var rung in LadderLoader.WarMage.Rungs)
        {
            foreach (var key in rung.GroundKey is null ? [] : new[] { rung.GroundKey }.Concat(rung.Alternates.Where(a => !a.Pending).Select(a => a.GroundKey)).Distinct())
            {
                var ground = GroundCatalog.Get(key);
                var purse = ground.Supplies.Purse;
                var sold = purse.Notes.Where(n => ground.VendorSelling(n.Wcid) is not null).ToList();
                var offers = sold.Count == 0 ? [] : PurseNotes.All.Where(n => n.Value >= purse.MinNote).Select(n => new PurseNotes.Offer(n, PurseNotes.PriceOf(n.Value))).ToList();
                var picks = PurseNotes.Greedy(CoinHeldAtTheBand(rung.Band.From) - purse.Coin, offers).Picks;
                if (picks.Count == 0)
                {
                    failures.Add($"{rung.Key} ({key}): coin {CoinHeldAtTheBand(rung.Band.From)} buys no note: floor {purse.Coin}, min_note {purse.MinNote}, a home vendor sells a purse note: {sold.Count > 0}, the smallest offer {(offers.Count == 0 ? "none" : offers.Min(o => o.Price).ToString(System.Globalization.CultureInfo.InvariantCulture))}");
                }
            }
        }

        Assert.True(failures.Count == 0, "the purse rule cannot bind at: " + string.Join("; ", failures));
    }
}
