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
        var home = new Home(Den, 150_021, notes: false);
        var tenK = home.Own("Trade Note (10,000)", Note10k, VendorStock.PromissoryNoteType, 10_000, stack: 1, maxStack: NoteMax);
        var fiftyK = home.Own("Trade Note (50,000)", 2626, VendorStock.PromissoryNoteType, 250_000, stack: 5, maxStack: NoteMax);
        home.Own("Trade Note (250,000)", Note250k, VendorStock.PromissoryNoteType, 250_000, stack: 1, maxStack: NoteMax);
        var run = home.Start(new ResupplyOptions(true));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        Assert.True(home.Logged("purse: coin 150021, floor 40000, above 110021, notes of 25000 and up (a smaller note saves no pack slot and costs 15%): buy 1 x Trade Note (50,000) at 57500; 52521 stays coin (under 57500, the Trade Note (50,000)'s price)"));
        Assert.Equal(1, run.NotesBought);
        Assert.Equal(92_521, home.Vendor.State.Self.Qualities.CoinValue); // four coin stacks
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
        Assert.True(home.Logged("purse: done at Archmage Cindrue: the coin 92521, 52521 above the floor 40000"));
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
}
