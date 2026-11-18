using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Quartermaster;

/// <summary>
/// M6 item 16, slices E5a1 and E5a2: the quartermaster's one read of the purse (<see cref="PurseView"/>) over a pack fed through ACE's own writers and the real decoder. What these prove: an unread coin stays unread, and only
/// the named call counts it as 0; a note in the main pack and a note inside a side pack are both what a counter can change (E5a2: one meaning; E5a1 pinned the side pack's as only held); mixed faces and stacks sum by face;
/// a worn item, a side pack and an item of another class are no notes; the reading's stacks are the counter's own read; the allocation-free totals and the reading agree; a vendor known to take no note is judged on the
/// coin alone; and each copy E5a1 moved (<see cref="Before"/>, the code as it stood) gives the answer it gave, on states where the copies differed from each other.
/// </summary>
public sealed class PurseViewTests
{
    private const uint Player = WorldActionRig.Player;
    private const uint Note100 = 2621;
    private const uint Note500 = 2622;
    private const uint Note5k = 2624;
    private const uint Note10k = 2625;
    private const uint Note100k = 2627;
    private const uint Note250k = 20630;
    private const uint NoteType = VendorStock.PromissoryNoteType;
    private const uint OtherClass = 990_001; // a weenie class that is none of ACE's ten trade notes

    /// <summary>A described player whose packs are filled item by item; the coin is unread until <see cref="Coins"/> says it.</summary>
    internal sealed class PurseRig
    {
        private uint _guid = 0x80007000;
        private byte _seq;

        public PurseRig(int? coin = null)
        {
            R = new WorldActionRig();
            R.Describe([], []);
            if (coin is { } value)
            {
                Coins(value);
            }
        }

        public WorldActionRig R { get; }

        public GameState State => R.H.State;

        /// <summary>GameMessage 0x02CD CoinValue (PropertyInt 20).</summary>
        public void Coins(int value) => R.PrivateInt(PlayerQualities.CoinValueProperty, value);

        /// <summary>An item's F745 and its 0022 into <paramref name="container"/> (the main pack by default).</summary>
        public uint Hold(string name, uint wcid, uint itemType, uint value, ushort? stack = null, uint? container = null)
        {
            var guid = Arrive(name, wcid, itemType, value, stack);
            R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, guid, container ?? Player, 0, 0));
            return guid;
        }

        /// <summary>A trade note stack: the weenie's Value is the stack's (the face times the size), as ACE sends it.</summary>
        public uint Note(uint wcid, int size = 1, uint? container = null)
        {
            var note = PurseNotes.All.Single(n => n.Wcid == wcid);
            return Hold(note.Name, wcid, NoteType, (uint)(note.Value * size), size == 1 ? null : (ushort)size, container);
        }

        /// <summary>A side pack (ContainerType 1) in the main pack.</summary>
        public uint SidePack()
        {
            var guid = Arrive("Pack", 136, 0x200, 65, null);
            R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, guid, Player, 0, 1));
            return guid;
        }

        /// <summary>A worn piece (GameEvent 0x0023).</summary>
        public uint Wear(string name, uint wcid, uint itemType, uint value)
        {
            var guid = Arrive(name, wcid, itemType, value, null);
            R.Server((p, s) => AceM3Writers.WieldObject(p, s, guid, 0x200));
            return guid;
        }

        /// <summary>GameMessage 0x0197: the server's word on a stack's size and value.</summary>
        public void Stack(uint item, uint size, uint value) => R.Raw(AceM3Writers.SetStackSize(++_seq, item, size, value));

        private uint Arrive(string name, uint wcid, uint itemType, uint value, ushort? stack)
        {
            var guid = ++_guid;
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name, itemType: itemType);
            var header = WeenieHeaderFlags.Container | WeenieHeaderFlags.Value | (stack is null ? WeenieHeaderFlags.None : WeenieHeaderFlags.StackSize);
            R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { Header = header, WeenieClassId = wcid, ContainerId = Player, Value = value, StackSize = stack } }, isUpdate: false);
            return guid;
        }
    }

    /// <summary>
    /// The copies as they stood before the slice (328e2f15), re-expressed line for line, so the answer of each moved caller is pinned by its name: the town trip's floor purse, the start-up audit's, the town stock's, the trip
    /// trigger's note count, the counter-side note read of the lockpick buy and the buy-trip, and the two coin spellings.
    /// </summary>
    internal static class Before
    {
        /// <summary><c>ResupplyRun.FloorPurse()</c> (ResupplyStation.Gear.cs): the stations' coin, clamped at 0, and every held note by <see cref="GearInventory.CountOf"/>.</summary>
        public static long ResupplyFloorPurse(GameState state)
        {
            long notes = 0;
            var scratch = new List<uint>();
            foreach (var note in PurseNotes.All)
            {
                notes += (long)GearInventory.CountOf(state, scratch, note.Wcid) * note.Value;
            }

            return Math.Max(0, StationItems.CoinsOf(state)) + notes;
        }

        /// <summary><c>StartupAudit.FloorPurse(GameState)</c>: the same notes, the coin read from the property with unread as 0.</summary>
        public static long StartupAuditFloorPurse(GameState state)
        {
            long notes = 0;
            var scratch = new List<uint>();
            foreach (var note in PurseNotes.All)
            {
                notes += (long)GearInventory.CountOf(state, scratch, note.Wcid) * note.Value;
            }

            var coin = state.Self.Qualities.TryGetInt(PlayerQualities.CoinValueProperty, out var value) ? value : 0;
            return Math.Max(0, coin) + notes;
        }

        /// <summary><c>TownStock.PurseOf(GameState)</c>: every held guid (<see cref="GearInventory.Collect"/>: worn ones and the side packs themselves too) matched against the ten notes.</summary>
        public static long TownStockPurseOf(GameState state)
        {
            long faces = 0;
            var scratch = new List<uint>();
            GearInventory.Collect(state, scratch);
            var notes = PurseNotes.All;
            foreach (var guid in scratch)
            {
                if (!state.Objects.TryGet(guid, out var o))
                {
                    continue;
                }

                for (var n = 0; n < notes.Count; n++)
                {
                    if (notes[n].Wcid == o.Weenie.WeenieClassId)
                    {
                        var stack = state.Self.Inventory.TryGetStack(guid, out var size, out _) ? size : o.Weenie.StackSize ?? 1;
                        faces += (long)stack * notes[n].Value;
                        break;
                    }
                }
            }

            return Math.Max(0, state.Self.Qualities.CoinValue) + faces;
        }

        /// <summary><c>LogisticsCounters</c>' note faces, counted in its <c>Check</c> with the stock: the main pack and the inside of every side pack.</summary>
        public static long TripTriggerNoteFaces(GameState state)
        {
            var inventory = state.Self.Inventory;
            var faces = Count(state, inventory.Pack);
            foreach (var side in inventory.SidePacks)
            {
                faces += Count(state, inventory.ItemsIn(side.Guid));
            }

            return faces;

            static long Count(GameState state, IReadOnlyList<InventoryEntry> entries)
            {
                long faces = 0;
                foreach (var entry in entries)
                {
                    if (!state.Objects.TryGet(entry.Guid, out var item))
                    {
                        continue;
                    }

                    foreach (var note in PurseNotes.All)
                    {
                        if (note.Wcid == item.Weenie.WeenieClassId)
                        {
                            var stack = state.Self.Inventory.TryGetStack(item.Guid, out var size, out _) ? (int)Math.Min(size, int.MaxValue) : item.Weenie.StackSize is { } s ? (int)Math.Min(s, int.MaxValue) : 1;
                            faces += (long)stack * note.Value;
                            break;
                        }
                    }
                }

                return faces;
            }
        }

        /// <summary><c>LogisticsCounters.PurseOf</c> right after a check: the model's coin (unread 0), clamped, and the note faces.</summary>
        public static long TripTriggerPurse(GameState state) => Math.Max(0, state.Self.Qualities.CoinValue) + TripTriggerNoteFaces(state);

        /// <summary>The lockpick buy's and the buy-trip's notes (M6 16 E5b), as the counter's read listed them before E5a2: the faces of the main pack's notes alone (<c>StationItems.InPackOf</c> for each of the ten).</summary>
        public static long CounterNotesWorth(GameState state)
        {
            long faces = 0;
            foreach (var note in PurseNotes.All)
            {
                foreach (var guid in StationItems.InPackOf(state, note.Wcid))
                {
                    faces += (long)note.Value * (state.Objects.TryGet(guid, out var o) ? GearInventory.StackOf(state, o) : 1);
                }
            }

            return faces;
        }

        /// <summary>The coin spelled as the model's property (the learn holds, the sale key): unread is 0.</summary>
        public static int ModelCoin(GameState state) => state.Self.Qualities.CoinValue;

        /// <summary>The coin spelled as the villa schedule read it: the property, unread is -1.</summary>
        public static long PropertyCoin(GameState state) => state.Self.Qualities.TryGetInt(PlayerQualities.CoinValueProperty, out var coin) ? coin : -1;
    }

    /// <summary>The states the copies are compared on, by name; each is one where at least two of the old copies counted different things, or a plain case beside them.</summary>
    public static TheoryData<string> States => new()
    {
        "nothing held, the coin unread",
        "the coin alone",
        "the coin unread, a note inside a side pack",
        "5,000 coin, a 100,000 note inside a side pack",
        "notes in the main pack and inside a side pack, mixed faces and stacks",
        "a worn piece, a side pack and a promissory item of another class",
        "a stack whose size the server sent after the description",
    };

    internal static PurseRig Build(string name)
    {
        switch (name)
        {
            case "nothing held, the coin unread":
                return new PurseRig();
            case "the coin alone":
                return new PurseRig(52_094);
            case "the coin unread, a note inside a side pack":
            {
                var rig = new PurseRig();
                rig.Note(Note100k, container: rig.SidePack());
                return rig;
            }

            case "5,000 coin, a 100,000 note inside a side pack":
            {
                var rig = new PurseRig(5_000);
                rig.Note(Note100k, container: rig.SidePack());
                return rig;
            }

            case "notes in the main pack and inside a side pack, mixed faces and stacks":
            {
                var rig = new PurseRig(12_345);
                rig.Note(Note100, 5);
                rig.Note(Note250k, 2);
                rig.Note(Note5k);
                var first = rig.SidePack();
                var second = rig.SidePack();
                rig.Note(Note500, 7, first);
                rig.Note(Note10k, 3, second);
                rig.Note(Note10k, 1, second);
                rig.Hold("Prismatic Taper", 20631, 0x1000, 30, 100, first);
                return rig;
            }

            case "a worn piece, a side pack and a promissory item of another class":
            {
                var rig = new PurseRig(700);
                rig.Wear("Robe", OtherClass + 1, 0x4, 250_000);
                rig.Hold("Promissory item", OtherClass, NoteType, 100_000, 3, rig.SidePack());
                rig.Hold("Promissory item", OtherClass, NoteType, 100_000);
                rig.Hold("Pyreal", 273, 0x40, 700, 700);
                return rig;
            }

            case "a stack whose size the server sent after the description":
            {
                var rig = new PurseRig(1);
                var main = rig.Note(Note10k, 3);
                var side = rig.Note(Note5k, 2, rig.SidePack());
                rig.Stack(main, 9, 90_000); // six more merged in
                rig.Stack(side, 1, 5_000); // one split away
                return rig;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, "no such state");
        }
    }

    [Fact]
    public void An_unread_coin_stays_unread_and_only_the_named_call_counts_it_as_zero()
    {
        var rig = Build("the coin unread, a note inside a side pack");

        Assert.Equal(-1, PurseView.Coin(rig.State));
        Assert.Equal(0, PurseView.CoinUnreadAsZero(rig.State));
        var reading = PurseView.Read(rig.State);
        Assert.Equal(-1, reading.Coin);
        Assert.False(reading.CoinRead);
        Assert.Null(reading.CounterCanPay); // never "0 pyreals" by accident
        Assert.Null(reading.EverythingHeld);
        Assert.Equal(0, reading.CoinUnreadAsZero);
        Assert.Equal(100_000, reading.EverythingHeldUnreadAsZero);
        Assert.Equal(100_000, PurseView.EverythingHeldUnreadAsZero(rig.State));

        rig.Coins(0); // read, and empty: not the same as unread
        Assert.Equal(0, PurseView.Coin(rig.State));
        Assert.Equal(100_000, PurseView.Read(rig.State).CounterCanPay); // M6 16 E5a2: the note inside the side pack is what the counter changes
        Assert.Equal(100_000, PurseView.Read(rig.State).EverythingHeld);
    }

    [Fact]
    public void A_note_in_the_main_pack_and_a_note_inside_a_side_pack_are_both_what_the_counter_can_change()
    {
        var rig = new PurseRig(5_000);
        var main = rig.Note(Note10k, 3);
        var pack = rig.SidePack();
        var away = rig.Note(Note100k, container: pack);

        var reading = PurseView.Read(rig.State);

        Assert.Equal(
            [new PurseStack(main, PurseNotes.All.Single(n => n.Wcid == Note10k), 3, PursePlace.MainPack, Player), new PurseStack(away, PurseNotes.All.Single(n => n.Wcid == Note100k), 1, PursePlace.SidePack, pack)],
            reading.Notes);
        Assert.Equal((30_000L, 100_000L, 130_000L), (reading.NotesInMainPack, reading.NotesInSidePacks, reading.NotesHeld));
        Assert.Equal(130_000, reading.NotesCounterCanChange); // M6 16 E5a2: every note held (E5a1: 30,000, the main pack's)
        Assert.Equal(135_000, reading.CounterCanPay);
        Assert.Equal(135_000, reading.EverythingHeld);
        Assert.Equal(130_000, PurseView.NotesCounterCanChange(rig.State));
        Assert.Equal(130_000, PurseView.NotesHeld(rig.State));
        Assert.Equal(135_000, PurseView.EverythingHeldUnreadAsZero(rig.State));
        Assert.Equal(135_000, PurseView.CounterCanPayUnreadAsZero(rig.State));
        Assert.Equal(135_000, PurseView.CounterCanPayUnreadAsZero(rig.State, vendorTakesNotes: true));
        Assert.Equal(5_000, PurseView.CounterCanPayUnreadAsZero(rig.State, vendorTakesNotes: false)); // a vendor known to take no note: the coin alone
    }

    [Fact]
    public void Mixed_faces_and_stacks_sum_by_face_in_each_place()
    {
        var rig = Build("notes in the main pack and inside a side pack, mixed faces and stacks");

        var reading = PurseView.Read(rig.State);

        Assert.Equal((5 * 100) + (2 * 250_000) + 5_000, reading.NotesInMainPack);
        Assert.Equal((7 * 500) + (3 * 10_000) + 10_000, reading.NotesInSidePacks);
        Assert.Equal(6, reading.Notes.Count); // the taper is no note
        Assert.Equal(3, reading.Notes.Count(n => n.Place == PursePlace.MainPack));
        Assert.Equal(3, reading.Notes.Count(n => n.Place == PursePlace.SidePack));
        Assert.Equal(2, reading.Notes.Where(n => n.Place == PursePlace.SidePack).Select(n => n.Container).Distinct().Count());
        Assert.Equal(12_345 + 505_500 + 43_500, reading.EverythingHeld);
    }

    [Fact]
    public void A_worn_piece_a_side_pack_and_an_item_of_another_class_are_no_notes()
    {
        var rig = Build("a worn piece, a side pack and a promissory item of another class");

        var reading = PurseView.Read(rig.State);

        Assert.Empty(reading.Notes); // the robe is worn, the pack is a pack, the two items are promissory by type and none of ACE's ten trade notes, the pyreal stack is the coin's own item
        Assert.Equal(0, PurseView.NotesHeld(rig.State));
        Assert.Equal(700, PurseView.EverythingHeldUnreadAsZero(rig.State));
        Assert.Single(rig.State.Self.Inventory.Equipped);
        Assert.Single(rig.State.Self.Inventory.SidePacks);
    }

    [Fact]
    public void The_servers_last_word_on_a_stack_is_its_size()
    {
        var rig = Build("a stack whose size the server sent after the description");

        Assert.Equal(90_000, PurseView.Read(rig.State).NotesInMainPack);
        Assert.Equal(95_000, PurseView.NotesCounterCanChange(rig.State));
        Assert.Equal(95_000, PurseView.NotesHeld(rig.State));
        Assert.Equal([9, 1], PurseView.Read(rig.State).Notes.Select(n => n.Stack));
    }

    [Fact]
    public void The_readings_stacks_are_the_counters_own_read_in_its_order_the_main_packs_first()
    {
        var rig = Build("notes in the main pack and inside a side pack, mixed faces and stacks");
        var first = PurseNotes.All.Where(n => n.Wcid is Note5k or Note100).ToList(); // a ground's purse notes, read first

        var counter = PurseNotes.Read(rig.State, first);
        var reading = PurseView.Read(rig.State, first);

        Assert.Equal(counter.Coin, reading.Coin);
        Assert.Equal(counter.Notes.Select(h => (h.Guid, h.Note, h.Stack, h.InSidePack)), reading.Notes.Select(n => (n.Guid, n.Note, n.Stack, n.Place == PursePlace.SidePack)));
        Assert.Equal([Note5k, Note100, Note250k], reading.Notes.Take(3).Select(n => n.Note.Wcid));
        Assert.All(reading.Notes.Take(3), n => Assert.Equal((PursePlace.MainPack, Player), (n.Place, n.Container)));
        Assert.Equal(counter.NotesWorth, reading.NotesCounterCanChange);
        Assert.All(reading.Notes.Skip(3), n => Assert.Equal(PursePlace.SidePack, n.Place));
        Assert.Equal(counter.Notes.Skip(3).Select(h => h.Container), reading.Notes.Skip(3).Select(n => n.Container)); // the side pack that holds each: where the counter sells or splits it
    }

    [Theory]
    [MemberData(nameof(States))]
    public void The_allocation_free_totals_and_the_reading_agree(string name)
    {
        var state = Build(name).State;

        var reading = PurseView.Read(state);

        Assert.Equal(reading.Coin, PurseView.Coin(state));
        Assert.Equal(reading.CoinUnreadAsZero, PurseView.CoinUnreadAsZero(state));
        Assert.Equal(reading.NotesCounterCanChange, PurseView.NotesCounterCanChange(state));
        Assert.Equal(reading.NotesHeld, PurseView.NotesHeld(state));
        Assert.Equal(reading.EverythingHeldUnreadAsZero, PurseView.EverythingHeldUnreadAsZero(state));
        Assert.Equal(reading.Notes.Sum(n => n.Worth), reading.NotesHeld);
    }

    [Theory]
    [MemberData(nameof(States))]
    public void Each_moved_caller_is_answered_what_its_own_copy_answered(string name)
    {
        var state = Build(name).State;

        // The town trip's floor purse, the start-up audit's, the town stock's and the trip trigger's: everything held, an unread coin counted as 0.
        Assert.Equal(Before.ResupplyFloorPurse(state), PurseView.EverythingHeldUnreadAsZero(state));
        Assert.Equal(Before.StartupAuditFloorPurse(state), StartupAudit.FloorPurse(state));
        Assert.Equal(Before.TownStockPurseOf(state), TownStock.PurseOf(state));
        Assert.Equal(Before.TripTriggerNoteFaces(state), PurseView.NotesHeld(state));
        Assert.Equal(Before.TripTriggerPurse(state), PurseView.CoinUnreadAsZero(state) + PurseView.NotesHeld(state));

        // The lockpick buy's and the buy-trip's notes: what the counter can change. M6 16 E5a2 changed this one answer: it was the main pack's notes, and is every note held.
        Assert.Equal(Before.CounterNotesWorth(state), PurseView.Read(state).NotesInMainPack);
        Assert.Equal(PurseView.NotesHeld(state), PurseView.NotesCounterCanChange(state));
        Assert.Equal(PurseNotes.Read(state).NotesWorth, PurseView.NotesCounterCanChange(state));

        // The coin: the learn holds' and the sale key's spelling (unread 0), the villa schedule's (unread -1).
        Assert.Equal(Before.ModelCoin(state), PurseView.CoinUnreadAsZero(state));
        Assert.Equal(Before.PropertyCoin(state), PurseView.Coin(state));
    }

    [Fact]
    public void What_she_is_judged_to_have_is_what_the_counter_can_pay_on_5000_coin_and_a_100000_note_inside_a_side_pack()
    {
        var state = Build("5,000 coin, a 100,000 note inside a side pack").State;

        // What the trip's budgets and the trip trigger count: the note is held.
        Assert.Equal(105_000, PurseView.EverythingHeldUnreadAsZero(state));
        Assert.Equal(105_000, StartupAudit.FloorPurse(state));
        Assert.Equal(105_000, TownStock.PurseOf(state));

        // M6 16 E5a2 (E5a1 pinned this the other way, as The_copies_disagreed_and_still_do_by_name_...: 0, 5,000 and no note read): what a buy at a counter pays with is the same 105,000, for the counter changes the
        // note where it lies. A 50,000 buy is planned by the first and paid by the second (VendorCounterTests buys it through).
        Assert.Equal(100_000, PurseView.NotesCounterCanChange(state));
        Assert.Equal(105_000, PurseView.Read(state).CounterCanPay);
        Assert.Equal(105_000, PurseView.CounterCanPayUnreadAsZero(state));
        var read = Assert.Single(PurseNotes.Read(state).Notes);
        Assert.True(read.InSidePack);
        Assert.Equal(state.Self.Inventory.SidePacks[0].Guid, read.Container);
    }

    [Fact]
    public void A_judge_says_what_it_did_with_the_notes_for_a_buy_at_a_named_vendor()
    {
        Assert.Equal(string.Empty, PurseView.CounterSays(0, "the Merchant", null)); // no note held: nothing to say

        // Not known (no ground profile lists the mask): the notes are counted, and the counter stops by name.
        Assert.Equal("the trade notes held (10000) are counted with the coin: no ground profile lists the Merchant's mask, so a counter that takes no note stops the buy by name there", PurseView.CounterSays(10_000, "the Merchant", null));

        // Known and takes a note.
        Assert.Equal("the trade notes held (10000) are counted with the coin: Archmage Cindrue's mask 0x000CB000 takes a trade note (by the ground profiles)", PurseView.CounterSays(10_000, "Archmage Cindrue", 0x000CB000));

        // Known and takes none: the coin alone, said.
        Assert.Equal("a tailor's mask 0x00000004 takes no trade note (by the ground profiles): the 10000 held in notes are not counted for this buy, the coin alone is", PurseView.CounterSays(10_000, "a tailor", 0x4));
        Assert.Equal<(bool?, bool?, bool?)>((true, false, null), (NoteMasks.TakesNote(0x000CB000), NoteMasks.TakesNote(0x4), NoteMasks.TakesNote(null)));
    }

    [Fact]
    public void The_ground_profiles_list_no_vendor_whose_known_mask_lacks_the_note_type()
    {
        // The profiles' own data (the pinned world SQL v0.9.296 behind each buys_item_types): the rows whose mask lacks PromissoryNote 0x40000 are the two zero rows, the den profiles' Scrivener of Item Magic, and a zero
        // mask is "no loot buyer", not a known mask (NoteMasks): every scrivener's weenie takes a note (TownTripPurse.ScrivenersTakeNotes).
        Assert.Equal(
            [("black-spawn-den-aluvian", "Scrivener of Item Magic", 20214u, 0u), ("black-spawn-den-sho", "Scrivener of Item Magic", 20214u, 0u)],
            NoteMasks.Lacking);
        Assert.Null(NoteMasks.Of(20214)); // not known: the notes are counted, the counter stops by name
        Assert.Null(NoteMasks.Takes(20214));
        Assert.Null(NoteMasks.Of(1387)); // the Merchant of the lockpick: no profile lists him
        Assert.Equal((0x000CB000u, true), (NoteMasks.Of(2304), NoteMasks.Takes(2304))); // Archmage Cindrue
        Assert.Equal((0x480467A7u, true), (NoteMasks.Of(718), NoteMasks.Takes(718))); // Shopkeeper Renald the Elder
        Assert.Equal(TownTripPurse.ScrivenersTakeNotes, NoteMasks.Takes(20214) ?? true);
    }
}

/// <summary>M6 16 E5a1: the purse totals a per-frame path may call allocate nothing (the trip trigger's check runs from the outer loop).</summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class PurseViewAllocationTests
{
    [Fact]
    public void The_named_totals_allocate_nothing()
    {
        var state = PurseViewTests.Build("notes in the main pack and inside a side pack, mixed faces and stacks").State;
        long sum = 0;

        AllocationAssert.Nothing(() =>
        {
            sum += PurseView.Coin(state);
            sum += PurseView.CoinUnreadAsZero(state);
            sum += PurseView.NotesCounterCanChange(state);
            sum += PurseView.NotesHeld(state);
            sum += PurseView.EverythingHeldUnreadAsZero(state);
        });

        Assert.True(sum > 0);
    }
}
