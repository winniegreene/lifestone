using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Journey;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Quartermaster;

/// <summary>
/// M6 item 16, slice E5b: the vendor counter's one buy (<see cref="VendorCounter.Buy"/>) on the scripted vendor the town trip's tests use (<see cref="VendorRig"/>, <see cref="ScriptedShop"/>: ACE's own writers and the real decoder, the
/// shop at the sell rate 1.0 so a row's Value is its price, a trade note paid at its face). What these prove: each refusal token of the check before sends nothing; the server's refusal is <c>refused</c> with its words; a trade note is
/// changed when the coin is short, the smallest that covers the rest, one split off its stack first (m6-plan.md D99, the town trip's rule); a note that cannot be changed ends the notes and the buy is <c>purse-short</c> at the counter;
/// a partial buy takes what the shop holds and what the coin pays; the check after reads the ware and the payment; a bought stack is merged when asked; the wire price wins over the catalogue's and is named.
/// <para>M6 16 E5a2: a note inside a side pack is changed too: a single note is sold where it lies (ACE's sale takes a guid from anywhere she holds it, <c>Player_Commerce.cs</c> 232-298 over <c>GetAllPossessions</c>,
/// <c>Player_Inventory.cs</c> 29-44 @95ef426d); one note of a stack is split off first, into the main pack when it has a free slot, else into a side pack that has one (the split needs its slot in the container the
/// action names, <c>Player_Inventory.cs</c> 2256 and 2445), and with no slot anywhere the split is refused by name and nothing is sent; the main pack's note is taken before a side pack's of the same face; and the
/// 5,000 coin with a 100,000 note in a side pack, which the trip planned with and the counter could not pay from, buys a 50,000 line through.</para>
/// </summary>
public sealed class VendorCounterTests
{
    private const uint Ware = 7001;
    private const uint WareRow = 0x80006001;
    private const uint MiscType = VendorRig.MiscType;
    private const uint Note10k = 2625;
    private const uint Note100k = 2627;
    private const uint TakesNotes = MiscType | VendorStock.PromissoryNoteType;

    /// <summary>One counter: the player beside Thelnoth, his shop scripted with one ware at 100 pyreals, the buy driven to its end.</summary>
    private sealed class Counter
    {
        private uint _item = 0x80007000;

        public Counter(int coins, uint mask = TakesNotes, int stock = -1)
        {
            Rig = new VendorRig(coins);
            Shops = new ScriptedShop(Rig);
            Shop = Shops.Add(VendorRig.Thelnoth, mask);
            Shop.Rows.Add((stock, WareRow, "Ware", Ware, MiscType, 100));
            View = new J3aView(Rig.R);
        }

        public VendorRig Rig { get; }

        public ScriptedShop Shops { get; }

        public ScriptedShop.Shop Shop { get; }

        public J3aView View { get; }

        public List<string> Lines { get; } = [];

        /// <summary>The main pack's free slots the host reports; null: the real read.</summary>
        public int? Room { get; set; }

        public List<CheckedBuy.Verdict> Judged { get; } = [];

        public long Coin => Rig.State.Self.Qualities.CoinValue;

        public uint Own(string name, uint wcid, uint itemType, uint value, ushort? stack = null, ushort? maxStack = null)
        {
            var guid = ++_item;
            Rig.Arrive(guid, name, wcid, itemType, value, stack, maxStack);
            Rig.Put(guid);
            Rig.Tick();
            return guid;
        }

        /// <summary>A side pack (ContainerType 1) in the main pack; 24 items by the layout's default (no ItemsCapacity is sent).</summary>
        public uint SidePack()
        {
            var pack = Rig.NextGuid();
            Rig.Arrive(pack, "Pack", 136, 0x200, 65);
            Rig.Put(pack, containerType: 1);
            Rig.Tick();
            return pack;
        }

        /// <summary>An item inside a side pack.</summary>
        public uint OwnIn(uint pack, string name, uint wcid, uint itemType, uint value, ushort? stack = null, ushort? maxStack = null)
        {
            var guid = ++_item;
            Rig.Arrive(guid, name, wcid, itemType, value, stack, maxStack);
            Rig.Put(guid, pack);
            Rig.Tick();
            return guid;
        }

        public Counter Open()
        {
            var open = Rig.Api.OpenVendor(VendorRig.Thelnoth, ActionSource.Automation);
            for (var i = 0; i < 40 && !open.IsCompleted; i++)
            {
                Step();
            }

            Assert.Equal(ActionOutcomeKind.Confirmed, open.Outcome!.Value.Kind);
            return this;
        }

        public VendorBuyRequest Request(int count, uint wcid = Ware) => new(VendorRig.Thelnoth, wcid, count) { VendorName = "Thelnoth", ArrivalSeconds = 3 };

        public VendorBuyResult Buy(VendorBuyRequest request)
        {
            var result = new VendorBuyResult();
            var host = new VendorCounterHost("t", Lines.Add, VendorCounterHost.ClockUntil(View), () => Room ?? Lifestone.Client.Session.Quartermaster.PackView.MainPackFree(Rig.State)) { CheckBefore = Judged.Add };
            using var script = VendorCounter.Buy(View, request, result, host).GetEnumerator();
            ActionHandle? wait = null;
            for (var i = 0; i < 400; i++)
            {
                if (wait is not { IsCompleted: false })
                {
                    if (!script.MoveNext())
                    {
                        return result;
                    }

                    wait = script.Current;
                }

                Step();
            }

            Assert.Fail("the buy did not end in 400 frames: " + string.Join(" | ", Lines));
            return result;
        }

        public bool Logged(string part) => Lines.Any(l => l.Contains(part, StringComparison.Ordinal));

        public int Held(uint wcid) => GearInventory.CountOf(Rig.State, [], wcid);

        private void Step()
        {
            Rig.Tick(0.25);
            Shops.Pump();
            Rig.Tick();
        }
    }

    [Fact]
    public void A_buy_within_the_coin_is_checked_sent_and_checked_after()
    {
        var c = new Counter(1_000).Open();

        var result = c.Buy(c.Request(3));

        Assert.True(result.Ok, result.Why);
        Assert.Equal((3, 300L, 0, 1_000L, 700L, 0, 3, false), (result.Bought, result.Paid, result.NotesChanged, result.CoinBefore, result.CoinAfter, result.HeldBefore, result.HeldAfter, result.Unseen));
        Assert.Equal((100u, 300L, 3), (result.Price, result.Cost, result.Count));
        Assert.Equal((1, 0, 700L, 3), (c.Shops.Buys, c.Shops.Sells, c.Coin, c.Held(Ware)));
        var judged = Assert.Single(c.Judged); // the caller's check-before line is asked for once, before the send
        Assert.Null(judged.Refusal);
        Assert.Contains("Ware listed at 100, 3 cost 300 within the coin 1000", judged.Why, StringComparison.Ordinal);
        Assert.Empty(c.Lines); // nothing to say: no note, no price that differs, no merge
    }

    [Fact]
    public void A_shop_that_is_not_open_or_does_not_list_the_ware_is_refused_by_name_and_nothing_is_sent()
    {
        var closed = new Counter(1_000);
        var notOpen = closed.Buy(closed.Request(1));
        Assert.Equal(("vendor-closed", 0, 0), (notOpen.Refusal, notOpen.Bought, closed.Shops.Buys));
        Assert.Contains("is not open (no shop)", notOpen.Why, StringComparison.Ordinal);

        var open = new Counter(1_000).Open();
        var notSold = open.Buy(open.Request(1, wcid: 9_999));
        Assert.Equal(("not-sold", 0, 0), (notSold.Refusal, notSold.Bought, open.Shops.Buys));
        Assert.Null(notSold.Entry);
        Assert.False(notOpen.Ok || notSold.Ok);
    }

    [Fact]
    public void A_shop_that_holds_fewer_is_stock_short_unless_the_buy_is_partial()
    {
        var c = new Counter(1_000, stock: 3).Open();

        var whole = c.Buy(c.Request(5));
        Assert.Equal(("stock-short", 0, 0), (whole.Refusal, whole.Bought, c.Shops.Buys));
        Assert.Contains("the shop holds 3 Ware, fewer than 5", whole.Why, StringComparison.Ordinal);

        var partial = c.Buy(c.Request(5) with { PartialAllowed = true });
        Assert.True(partial.Ok, partial.Why);
        Assert.Equal((3, 3, 300L, 1), (partial.Bought, partial.Count, partial.Paid, c.Shops.Buys));

        var empty = new Counter(1_000, stock: 0).Open();
        var none = empty.Buy(empty.Request(5) with { PartialAllowed = true });
        Assert.Equal(("stock-short", 0), (none.Refusal, empty.Shops.Buys)); // a partial buy of nothing is still a refusal
    }

    [Fact]
    public void A_purse_that_does_not_cover_the_price_is_purse_short_with_its_reason_and_nothing_is_sent()
    {
        var coinOnly = new Counter(50).Open();
        var bare = coinOnly.Buy(coinOnly.Request(1));
        Assert.Equal(("purse-short", "the coin 50 does not cover 1 Ware at 100 each (100)"), (bare.Refusal, bare.Why)); // the words the quest step and the lockpick step stop with

        var thin = new Counter(50).Open();
        thin.Own("Trade Note (100)", 2621, VendorStock.PromissoryNoteType, 100);
        var notEnough = thin.Buy(thin.Request(3));
        Assert.Equal(("purse-short", "the coin 50 and the trade notes held (100) do not cover 3 Ware at 100 each (300)"), (notEnough.Refusal, notEnough.Why));

        var noNotesHere = new Counter(50, mask: MiscType).Open();
        noNotesHere.Own("Trade Note (10,000)", Note10k, VendorStock.PromissoryNoteType, 10_000);
        var masked = noNotesHere.Buy(noNotesHere.Request(1));
        Assert.Equal("purse-short", masked.Refusal);
        Assert.Contains("the shop's mask takes no trade note (the 10000 held in notes are not changed here)", masked.Why, StringComparison.Ordinal);

        var kept = new Counter(50).Open();
        kept.Own("Trade Note (10,000)", Note10k, VendorStock.PromissoryNoteType, 10_000);
        var coinOnlyBuy = kept.Buy(kept.Request(1) with { ChangeNotes = false });
        Assert.Equal(("purse-short", "the coin 50 does not cover 1 Ware at 100 each (100)"), (coinOnlyBuy.Refusal, coinOnlyBuy.Why)); // a caller that changes no note keeps the coin-only rule

        Assert.Equal((0, 0, 0, 0), (coinOnly.Shops.Buys + coinOnly.Shops.Sells, thin.Shops.Buys + thin.Shops.Sells, noNotesHere.Shops.Buys + noNotesHere.Shops.Sells, kept.Shops.Buys + kept.Shops.Sells));
    }

    [Fact]
    public void A_full_pack_is_pack_full_and_a_side_pack_is_judged_by_the_container_slots()
    {
        var c = new Counter(1_000).Open();
        c.Room = 0;
        var full = c.Buy(c.Request(1));
        Assert.Equal(("pack-full", "the main pack has no free slot", 0), (full.Refusal, full.Why, c.Shops.Buys));

        var packs = new Counter(1_000).Open();
        for (var i = 0; i < GearKit.ContainerCapacity; i++)
        {
            var pack = packs.Rig.NextGuid();
            packs.Rig.Arrive(pack, "Pack", 136, 0x200, 65);
            packs.Rig.R.Raw(AceM3Writers.PutObjInContainer(VendorRig.Player, 8_000 + (uint)i, pack, VendorRig.Player, 0, 1)); // a side pack (ContainerType 1)
        }

        packs.Rig.Tick();
        packs.Room = 0; // the main pack is not what a side pack takes
        var noSlot = packs.Buy(packs.Request(1) with { ContainerSlot = true });
        Assert.Equal(("pack-full", "0 container slots are free for the 1 Ware", 0), (noSlot.Refusal, noSlot.Why, packs.Shops.Buys));
    }

    [Fact]
    public void An_unread_coin_is_purse_unread_and_a_partial_buy_leaves_the_purse_to_the_counter()
    {
        var c = new Counter(1_000).Open();
        var stock = c.Rig.State.Vendor!;

        var unread = CheckedBuy.Judge(stock, VendorRig.Thelnoth, Ware, 1, coins: -1, freeSlots: 5);
        Assert.Equal(("purse-unread", "the coin (CoinValue) is not read", 100u), (unread.Refusal, unread.Why, unread.Price));

        var partial = CheckedBuy.Judge(stock, VendorRig.Thelnoth, Ware, 4, coins: 50, freeSlots: 5, partial: true);
        Assert.Equal((null, 4, 400L), (partial.Refusal, partial.Count, partial.Cost)); // the purse is not judged before the notes are changed
    }

    [Fact]
    public void A_buy_the_server_refuses_is_the_token_refused_with_the_servers_words_and_is_not_sent_again()
    {
        var c = new Counter(1_000).Open();
        c.Shop.BuyRefusal = "You can't buy that right now.";

        var result = c.Buy(c.Request(2));

        Assert.Equal((VendorCounter.Refused, 0, 0L, 2, 1), (result.Refusal, result.Bought, result.Paid, result.Count, c.Shops.Buys));
        Assert.Contains("You can't buy that right now.", result.Detail, StringComparison.Ordinal);
        Assert.Equal(result.Outcome, result.Why);
        Assert.Equal((1_000L, 0), (c.Coin, c.Held(Ware)));
    }

    [Fact]
    public void A_trade_note_is_changed_when_the_coin_is_short_and_the_buy_is_paid_from_it()
    {
        var c = new Counter(50).Open();
        var note = c.Own("Trade Note (10,000)", Note10k, VendorStock.PromissoryNoteType, 10_000);

        var result = c.Buy(c.Request(3));

        Assert.True(result.Ok, result.Why);
        Assert.Equal((3, 300L, 1, 10_050L, 9_750L), (result.Bought, result.Paid, result.NotesChanged, result.CoinBefore, result.CoinAfter)); // the note paid at its face (Vendor.cs 591-599 @95ef426d), then the 300
        Assert.Equal((1, 1, 0), (c.Shops.Sells, c.Shops.Buys, c.Shops.Splits)); // a single note is sold as it is
        Assert.False(c.Rig.State.Self.Inventory.Contains(note));
        Assert.Equal((9_750L, 3), (c.Coin, c.Held(Ware)));
        Assert.Contains("the coin 50 is short by 250 and the trade notes held (10000) cover it, so a note is changed at the counter", Assert.Single(c.Judged).Why, StringComparison.Ordinal);
        Assert.True(c.Logged("t: purse: the coin was short: changed Trade Note (10,000) to coin at Thelnoth: paid 10000 pyreals (the coin 50 -> 10050)"));
        Assert.Contains("1 trade note(s) changed first", result.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_note_changed_is_the_smallest_that_covers_the_rest_and_one_is_split_off_its_stack_first_D99()
    {
        var c = new Counter(50).Open();
        var big = c.Own("Trade Note (100,000)", Note100k, VendorStock.PromissoryNoteType, 100_000);
        var stack = c.Own("Trade Note (10,000)", Note10k, VendorStock.PromissoryNoteType, 30_000, stack: 3, maxStack: 250);

        var result = c.Buy(c.Request(3));

        Assert.True(result.Ok, result.Why);
        Assert.Equal((1, 1, 1), (result.NotesChanged, c.Shops.Splits, c.Shops.Sells));
        Assert.True(c.Rig.State.Self.Inventory.Contains(big)); // the 100,000 is not the one: a 10,000 covers the 250
        Assert.True(c.Rig.State.Self.Inventory.TryGetStack(stack, out var left, out _));
        Assert.Equal(2u, left); // ACE sells the whole object it is handed (Player_Commerce.cs 232-298 @95ef426d), so one note was split off and the rest stays
        Assert.Equal(50 + 10_000 - 300, c.Coin);
        Assert.True(c.Logged("t: purse: split one Trade Note (10,000)"));
        Assert.True(c.Logged($"from its stack 0x{stack:X8} (x3) to change it"));
    }

    [Fact]
    public void A_note_that_cannot_be_changed_ends_the_notes_and_the_buy_is_purse_short_at_the_counter()
    {
        var c = new Counter(50).Open();
        var note = c.Own("Trade Note (10,000)", Note10k, VendorStock.PromissoryNoteType, 10_000);
        c.Shop.Unsellable.Add(Note10k);

        var result = c.Buy(c.Request(3));

        Assert.Equal(("purse-short", 0, 0, 0), (result.Refusal, result.Bought, result.NotesChanged, c.Shops.Buys));
        Assert.Equal("the coin 50 does not cover 3 Ware at 100 each (300): a held note was not changed (its refusal is named above)", result.Why);
        Assert.Equal(1, c.Shops.Sells); // one try, never again
        Assert.True(c.Rig.State.Self.Inventory.Contains(note));
        Assert.True(c.Logged("t: purse: refused: unsellable: the trade note Trade Note (10,000) at Thelnoth:"));
        Assert.Equal(50, c.Coin);
    }

    [Fact]
    public void M6_16_E5a2_5000_coin_and_a_100000_note_inside_a_side_pack_buys_a_50000_line_the_note_sold_where_it_lies()
    {
        var c = new Counter(5_000).Open();
        var pack = c.SidePack();
        var note = c.OwnIn(pack, "Trade Note (100,000)", Note100k, VendorStock.PromissoryNoteType, 100_000);
        Assert.Equal(InventoryPlaceKind.InSidePack, c.Rig.State.Self.Inventory.Find(note).Kind);
        Assert.Equal(105_000, PurseView.CounterCanPayUnreadAsZero(c.Rig.State)); // what every judge counts

        var result = c.Buy(c.Request(500)); // 500 at 100: the 50,000 line the trip planned with 105,000 and, before the slice, met with 5,000 at the counter

        Assert.True(result.Ok, result.Why);
        Assert.Equal((500, 50_000L, 1, 105_000L, 55_000L), (result.Bought, result.Paid, result.NotesChanged, result.CoinBefore, result.CoinAfter));
        Assert.Equal((1, 1, 0), (c.Shops.Sells, c.Shops.Buys, c.Shops.Splits)); // a single note is sold as it is, from the side pack
        Assert.False(c.Rig.State.Self.Inventory.Contains(note));
        Assert.Equal((55_000L, 500), (c.Coin, c.Held(Ware)));
        Assert.Contains("the coin 5000 is short by 45000 and the trade notes held (100000) cover it, so a note is changed at the counter", Assert.Single(c.Judged).Why, StringComparison.Ordinal);
        Assert.True(c.Logged($"t: purse: Trade Note (100,000) 0x{note:X8} (x1) lies in the side pack 0x{pack:X8}: a single note, sold where it lies"));
        Assert.True(c.Logged("t: purse: the coin was short: changed Trade Note (100,000) to coin at Thelnoth: paid 100000 pyreals (the coin 5000 -> 105000)")); // the line of a changed note, word for word
    }

    [Fact]
    public void M6_16_E5a2_one_note_of_a_stack_inside_a_side_pack_is_split_off_into_the_main_pack_and_sold()
    {
        var c = new Counter(50).Open();
        var pack = c.SidePack();
        var stack = c.OwnIn(pack, "Trade Note (10,000)", Note10k, VendorStock.PromissoryNoteType, 30_000, stack: 3, maxStack: 250);

        var result = c.Buy(c.Request(3));

        Assert.True(result.Ok, result.Why);
        Assert.Equal((1, 1, 1), (result.NotesChanged, c.Shops.Splits, c.Shops.Sells));
        Assert.True(c.Rig.State.Self.Inventory.TryGetStack(stack, out var left, out _));
        Assert.Equal(2u, left); // ACE sells the whole object it is handed, so one note was split off and the rest stays
        Assert.Equal((InventoryPlaceKind.InSidePack, pack), (c.Rig.State.Self.Inventory.Find(stack).Kind, c.Rig.State.Self.Inventory.Find(stack).Container)); // where it lay
        Assert.Equal(50 + 10_000 - 300, c.Coin);
        Assert.Equal(20_000, PurseView.NotesCounterCanChange(c.Rig.State));
        Assert.True(c.Logged($"t: purse: Trade Note (10,000) 0x{stack:X8} (x3) lies in the side pack 0x{pack:X8}: one note is split off into the main pack"));
        Assert.True(c.Logged("t: purse: split one Trade Note (10,000)"));
        Assert.True(c.Logged($"from its stack 0x{stack:X8} (x3) to change it")); // the split's line, word for word
    }

    [Fact]
    public void M6_16_E5a2_with_the_main_pack_full_one_note_is_split_off_inside_a_side_pack_and_with_no_slot_anywhere_the_split_is_refused_by_name()
    {
        // The main pack has no free slot and the ware is a side pack (it takes a container slot, so the check before lets the buy through): the split goes into the stack's own side pack, which has room.
        var c = new Counter(50).Open();
        c.Shop.ContainerTypes[Ware] = 1;
        var pack = c.SidePack();
        var stack = c.OwnIn(pack, "Trade Note (10,000)", Note10k, VendorStock.PromissoryNoteType, 30_000, stack: 3, maxStack: 250);
        c.Room = 0;

        var result = c.Buy(c.Request(1) with { ContainerSlot = true });

        Assert.True(result.Ok, result.Why);
        Assert.Equal((1, 1, 1, 1), (result.NotesChanged, c.Shops.Splits, c.Shops.Sells, c.Shops.Buys));
        Assert.True(c.Rig.State.Self.Inventory.TryGetStack(stack, out var left, out _));
        Assert.Equal(2u, left);
        Assert.Equal(50 + 10_000 - 100, c.Coin);
        Assert.True(c.Logged($"t: purse: Trade Note (10,000) 0x{stack:X8} (x3) lies in the side pack 0x{pack:X8}: the main pack has no free slot, so one note is split off into the side pack 0x{pack:X8}"));
        Assert.True(c.Logged($"from its stack 0x{stack:X8} (x3) to change it"));

        // No slot anywhere: the main pack full, the one side pack full. The split is refused by name, nothing is sent, and the buy is purse-short at the counter.
        var none = new Counter(50).Open();
        none.Shop.ContainerTypes[Ware] = 1;
        var full = none.SidePack();
        var held = none.OwnIn(full, "Trade Note (10,000)", Note10k, VendorStock.PromissoryNoteType, 30_000, stack: 3, maxStack: 250);
        for (var i = 1; i < PackLayout.SidePackCapacity; i++)
        {
            none.OwnIn(full, "Pebble", 9_000 + (uint)i, MiscType, 1);
        }

        Assert.Equal(0, PackLayout.SidePacks(none.Rig.State).Single().Free);
        none.Room = 0;

        var refused = none.Buy(none.Request(1) with { ContainerSlot = true });

        Assert.Equal(("purse-short", 0, 0), (refused.Refusal, refused.Bought, refused.NotesChanged));
        Assert.Equal((0, 0, 0), (none.Shops.Splits, none.Shops.Sells, none.Shops.Buys));
        Assert.Equal("the coin 50 does not cover 1 Ware at 100 each (100): a held note was not changed (its refusal is named above)", refused.Why);
        Assert.True(none.Logged($"t: purse: refused: pack: the trade note Trade Note (10,000): one note of the stack 0x{held:X8} (x3) in the side pack 0x{full:X8} cannot be split off: neither the main pack nor a side pack has a free slot"));
        Assert.True(none.Rig.State.Self.Inventory.TryGetStack(held, out var kept, out _) || none.Rig.State.Self.Inventory.Contains(held));
        Assert.Equal(50, none.Coin);
    }

    [Fact]
    public void M6_16_E5a2_a_main_pack_note_is_taken_before_a_side_packs_of_the_same_face_and_the_smallest_that_covers_still_wins_D99()
    {
        var c = new Counter(50).Open();
        var pack = c.SidePack();
        var away = c.OwnIn(pack, "Trade Note (10,000)", Note10k, VendorStock.PromissoryNoteType, 10_000);
        var near = c.Own("Trade Note (10,000)", Note10k, VendorStock.PromissoryNoteType, 10_000);

        Assert.True(c.Buy(c.Request(3)).Ok);

        Assert.False(c.Rig.State.Self.Inventory.Contains(near)); // the main pack's first
        Assert.True(c.Rig.State.Self.Inventory.Contains(away));

        // D99's choice is unchanged by where a note lies: a 10,000 inside a side pack covers the 250 and is taken before the 100,000 in the main pack.
        var d = new Counter(50).Open();
        var side = d.SidePack();
        var small = d.OwnIn(side, "Trade Note (10,000)", Note10k, VendorStock.PromissoryNoteType, 10_000);
        var big = d.Own("Trade Note (100,000)", Note100k, VendorStock.PromissoryNoteType, 100_000);

        Assert.True(d.Buy(d.Request(3)).Ok);

        Assert.False(d.Rig.State.Self.Inventory.Contains(small));
        Assert.True(d.Rig.State.Self.Inventory.Contains(big));
        Assert.Equal(50 + 10_000 - 300, d.Coin);
    }

    [Fact]
    public void A_partial_buy_takes_what_the_coin_pays_and_none_when_it_pays_for_none()
    {
        var c = new Counter(250).Open();

        var some = c.Buy(c.Request(5) with { PartialAllowed = true });

        Assert.True(some.Ok, some.Why);
        Assert.Equal((2, 2, 200L, 50L), (some.Bought, some.Count, some.Paid, c.Coin));
        Assert.True(c.Logged("t: purse: short by 250 pyreals for Ware (the 5 cost 500, the coin is 250 and no note is left): buys 2"));

        var none = c.Buy(c.Request(5) with { PartialAllowed = true });

        Assert.Equal(("purse-short", 0, 0, 1), (none.Refusal, none.Bought, none.Count, c.Shops.Buys));
        Assert.True(c.Logged("t: purse: short by 450 pyreals for Ware (the 5 cost 500, the coin is 50 and no note is left): buys 0"));
    }

    [Fact]
    public void A_ware_that_does_not_show_after_the_servers_yes_is_the_token_after_check()
    {
        var c = new Counter(1_000).Open();
        c.Shop.ArriveAfterPumps = int.MaxValue; // the buy is answered and paid; the ware's F745 and 0022 never come

        var result = c.Buy(c.Request(2));

        Assert.Equal((VendorCounter.AfterCheck, true, 2, 200L, 0, 0), (result.Refusal, result.Unseen, result.Bought, result.Paid, result.HeldBefore, result.HeldAfter));
        Assert.Equal((1_000L, 800L), (result.CoinBefore, result.CoinAfter));
        Assert.Contains("after the buy she holds 0 Ware (was 0, want +2) and the coin is 800 (was 1000", result.Why, StringComparison.Ordinal);
        Assert.False(result.Ok);
        Assert.Equal(1, c.Shops.Buys); // never sent again
    }

    [Fact]
    public void A_bought_stack_is_merged_into_the_held_one_when_the_caller_asks()
    {
        var c = new Counter(1_000).Open();
        c.Shop.MaxStacks[Ware] = 100;
        var held = c.Own("Ware", Ware, MiscType, 1_000, stack: 10, maxStack: 100);

        var result = c.Buy(c.Request(5) with { MergeAfter = true });

        Assert.True(result.Ok, result.Why);
        Assert.Equal((10, 15, 1), (result.HeldBefore, result.HeldAfter, c.Shops.Merges));
        var stacks = c.Rig.State.Self.Inventory.Pack.Where(e => c.Rig.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == Ware).Select(e => e.Guid).ToList();
        Assert.Equal([held], stacks); // ACE never joins a bought stack to a held one (Player_Commerce.cs 59-102 @95ef426d): the counter did
        Assert.True(c.Logged("t: merging a bought Ware stack"));

        var apart = new Counter(1_000).Open();
        apart.Shop.MaxStacks[Ware] = 100;
        apart.Own("Ware", Ware, MiscType, 1_000, stack: 10, maxStack: 100);
        Assert.True(apart.Buy(apart.Request(5)).Ok);
        Assert.Equal(0, apart.Shops.Merges); // not asked: the stacks stay apart
    }

    [Fact]
    public void A_wire_price_that_differs_from_the_catalogues_is_named_and_the_wire_wins()
    {
        var c = new Counter(1_000).Open();

        var result = c.Buy(c.Request(1) with { CataloguePrice = 90 });

        Assert.True(result.Ok, result.Why);
        Assert.Equal((true, 100u, 100L, 900L), (result.PriceDiffers, result.Price, result.Paid, c.Coin));
        Assert.True(c.Logged("t: the wire price 100 differs from the catalogue's 90: the wire wins"));

        var same = new Counter(1_000).Open();
        Assert.False(same.Buy(same.Request(1) with { CataloguePrice = 100 }).PriceDiffers);
        Assert.Empty(same.Lines);
    }
}
