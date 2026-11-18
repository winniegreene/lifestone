using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Quartermaster;

/// <summary>Where a held trade note stack lies.</summary>
public enum PursePlace
{
    /// <summary>In the main pack: a buy at a counter changes it (<see cref="VendorCounter"/>), before any of a side pack's of the same face.</summary>
    MainPack,

    /// <summary>Inside a side pack: a buy at a counter changes it where it lies (M6 16 E5a2; ACE sells an item from anywhere she holds it, <c>Player_Commerce.cs</c> 232-298 and <c>Player_Inventory.cs</c> 29-44 @95ef426d).</summary>
    SidePack,
}

/// <summary>One stack of a trade note she holds.</summary>
/// <param name="Guid">The stack's guid.</param>
/// <param name="Note">The note (its face is <see cref="PurseNote.Value"/>).</param>
/// <param name="Stack">The notes in the stack.</param>
/// <param name="Place">Where the stack lies.</param>
/// <param name="Container">The container that holds it: the player for the main pack, else the side pack.</param>
public readonly record struct PurseStack(uint Guid, PurseNote Note, int Stack, PursePlace Place, uint Container)
{
    /// <summary>The faces of the stack's notes.</summary>
    public long Worth => (long)Note.Value * Stack;
}

/// <summary>
/// The purse as read once (<see cref="PurseView.Read"/>): the coin, every trade note stack with its place, and the totals by name. An unread coin stays unread here: <see cref="Coin"/> is -1 and the two totals that hold the
/// coin are null. A caller that budgets with an unread coin counted as nothing says so through <see cref="CoinUnreadAsZero"/> or <see cref="EverythingHeldUnreadAsZero"/>.
/// </summary>
/// <param name="Coin">The coin held, or -1 unread (<see cref="StationItems.CoinsOf"/>).</param>
/// <param name="Notes">The note stacks: the main pack's first, in the order of the counter's read (<see cref="PurseNotes.Read"/>), then the side packs' in their slot order.</param>
public sealed record PurseReading(long Coin, IReadOnlyList<PurseStack> Notes)
{
    /// <summary>True when the coin is read.</summary>
    public bool CoinRead => Coin >= 0;

    /// <summary>The coin with an unread coin counted as 0: the explicit spelling of what the trip's budgets do.</summary>
    public long CoinUnreadAsZero => Math.Max(0, Coin);

    /// <summary>The faces of the notes in the main pack.</summary>
    public long NotesInMainPack => Faces(PursePlace.MainPack);

    /// <summary>The faces of the notes inside side packs.</summary>
    public long NotesInSidePacks => Faces(PursePlace.SidePack);

    /// <summary>The faces of every note held, whichever pack it lies in.</summary>
    public long NotesHeld => NotesInMainPack + NotesInSidePacks;

    /// <summary>The faces of the notes a buy at a counter can change now: every note held (M6 16 E5a2; until then the main pack's alone).</summary>
    public long NotesCounterCanChange => NotesHeld;

    /// <summary>What a buy at a counter whose mask takes a note can pay with now: the coin and every note held. Null while the coin is unread.</summary>
    public long? CounterCanPay => CoinRead ? Coin + NotesCounterCanChange : null;

    /// <summary>Everything held: the coin and every note; the same sum as <see cref="CounterCanPay"/> since M6 16 E5a2. Null while the coin is unread.</summary>
    public long? EverythingHeld => CoinRead ? Coin + NotesHeld : null;

    /// <summary>Everything held with an unread coin counted as 0 (<see cref="PurseView.EverythingHeldUnreadAsZero"/>).</summary>
    public long EverythingHeldUnreadAsZero => CoinUnreadAsZero + NotesHeld;

    private long Faces(PursePlace place)
    {
        long faces = 0;
        for (var i = 0; i < Notes.Count; i++)
        {
            if (Notes[i].Place == place)
            {
                faces += Notes[i].Worth;
            }
        }

        return faces;
    }
}

/// <summary>
/// M6 item 16, slices E5a1 and E5a2 (docs/automation-engine.md section 2.5; m6-plan.md D139): the quartermaster's one read of the purse. "How much money does she hold" was computed once a caller (the town trip's floor
/// purse, the start-up audit's, the trip trigger's, the town stock's, the coin-only reads of the learn holds), and the copies did not count the same things. E5a1 moved them here, each asking for the total it meant by
/// name; E5a2 gave the names one meaning, "what the counter can pay with": the coin and every trade note she holds, for the vendor counter changes a note inside a side pack where it lies (<see cref="VendorCounter"/>;
/// ACE sells an item from anywhere she holds it: <c>Player.VerifySellItems</c>, <c>Player_Commerce.cs</c> 232-298, over <c>GetAllPossessions</c>, <c>Player_Inventory.cs</c> 29-44 @95ef426d).
/// <list type="bullet">
/// <item><see cref="NotesCounterCanChange"/> and <see cref="NotesHeld"/>: the faces of every note, the side packs' too (one number under two names: what is held is what the counter can change).</item>
/// <item><see cref="CounterCanPayUnreadAsZero"/>: the coin and, at a counter that takes a note, every note, an unread coin counted as 0: what a judge of a buy at a named vendor counts (the learn holds, the lockpick step,
/// the buy-trip). A vendor whose mask is known to take no note (<see cref="NoteMasks"/>) is judged on the coin alone, and <see cref="CounterSays"/> says so.</item>
/// <item><see cref="EverythingHeldUnreadAsZero"/>: the coin and every note, an unread coin counted as 0: what the trip's budgets and the trip trigger count, whatever the vendor.</item>
/// <item><see cref="CoinUnreadAsZero"/>: the coin alone, unread counted as 0; <see cref="Coin"/> keeps unread as -1.</item>
/// </list>
/// The whole picture, with each stack's place, is <see cref="Read"/>, built on the counter's own read (<see cref="PurseNotes.Read"/>). The totals above walk the pack lists by index and allocate nothing, so the trip
/// trigger's check (every few seconds from the outer loop) may call them.
/// <para>What is a note: one of ACE's ten trade notes by weenie class (<see cref="PurseNotes.All"/>), in the main pack or inside a side pack. A note is never worn and is no container, so the equipped list and the side pack
/// entries themselves are not walked; the copies that walked them (<see cref="GearInventory.Collect"/>) found no note there. A held guid whose description has not arrived counts nothing, as in every copy.</para>
/// <para>What a slot can still refuse: one note of a stack is split off before it is sold (ACE sells the whole object it is handed), and the split needs a free slot (<see cref="VendorCounter"/>); a payout's coin stacks need
/// theirs. Neither is counted here: the counter refuses by name, and the purse is what she holds.</para>
/// <para>Not on this read: the villa's price (<c>VillaReads.Price</c>: pyreal stacks and every promissory item by item type and stack value), the house payment's pick (<c>GuardianWindowModel</c>, a panel) and the melee and
/// archer paths (<c>HuntScenario.Vendor</c>).</para>
/// </summary>
public static class PurseView
{
    /// <summary>The coin held, or -1 unread: the one coin read (<see cref="StationItems.CoinsOf"/>).</summary>
    /// <param name="state">The session's read model.</param>
    public static long Coin(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return StationItems.CoinsOf(state);
    }

    /// <summary>The coin held with an unread coin counted as 0, by name: a caller that compares the coin with a price before the coin is read gets "nothing held", as it did. Allocation-free.</summary>
    /// <param name="state">The session's read model.</param>
    public static int CoinUnreadAsZero(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return (int)Math.Max(0, StationItems.CoinsOf(state));
    }

    /// <summary>The faces of the trade notes a buy at a counter can change now: every note held (M6 16 E5a2: <see cref="NotesHeld"/>, the same notes <see cref="PurseNotes.Read"/> lists). Allocation-free.</summary>
    /// <param name="state">The session's read model.</param>
    public static long NotesCounterCanChange(GameState state) => NotesHeld(state);

    /// <summary>
    /// M6 16 E5a2: what a buy at a counter can pay with, an unread coin counted as 0: the coin and, unless the vendor is known to take no trade note, every note held. The one number a judge of a buy at a named vendor
    /// compares with a price (the learn holds, the lockpick step). Allocation-free.
    /// </summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="vendorTakesNotes">False only when the vendor's mask is known and lacks the note type (<see cref="NoteMasks.Takes(uint)"/>); true or null (not known): the notes are counted, and a counter that takes none stops the buy by name (<see cref="VendorCounter"/>).</param>
    public static long CounterCanPayUnreadAsZero(GameState state, bool? vendorTakesNotes = null) => CoinUnreadAsZero(state) + (vendorTakesNotes == false ? 0 : NotesHeld(state));

    /// <summary>M6 16 E5a2: what a judge says of the notes it counted, or left out, for a buy at a named vendor, for its log line; empty when no note is held.</summary>
    /// <param name="notes">The faces of the notes held.</param>
    /// <param name="vendor">The vendor's name.</param>
    /// <param name="mask">The vendor's mask when a ground profile lists one (<see cref="NoteMasks.Of"/>), else null.</param>
    public static string CounterSays(long notes, string vendor, uint? mask)
    {
        if (notes <= 0)
        {
            return string.Empty;
        }

        var c = System.Globalization.CultureInfo.InvariantCulture;
        return mask switch
        {
            { } known when (known & VendorStock.PromissoryNoteType) == 0 => string.Create(c, $"{vendor}'s mask 0x{known:X8} takes no trade note (by the ground profiles): the {notes} held in notes are not counted for this buy, the coin alone is"),
            { } known => string.Create(c, $"the trade notes held ({notes}) are counted with the coin: {vendor}'s mask 0x{known:X8} takes a trade note (by the ground profiles)"),
            _ => string.Create(c, $"the trade notes held ({notes}) are counted with the coin: no ground profile lists {vendor}'s mask, so a counter that takes no note stops the buy by name there"),
        };
    }

    /// <summary>The faces of every trade note held: the main pack's and those inside the side packs. Allocation-free.</summary>
    /// <param name="state">The session's read model.</param>
    public static long NotesHeld(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var inventory = state.Self.Inventory;
        var faces = Faces(state, inventory.Pack);
        var sides = inventory.SidePacks;
        for (var i = 0; i < sides.Count; i++)
        {
            faces += Faces(state, inventory.ItemsIn(sides[i].Guid));
        }

        return faces;
    }

    /// <summary>
    /// Everything held, an unread coin counted as 0: the coin (<see cref="CoinUnreadAsZero"/>) and every note (<see cref="NotesHeld"/>). The trip's budgets (the floor purse, the town stock's) and the start-up audit count this.
    /// Since M6 16 E5a2 it is what a counter that takes a note can pay with (<see cref="CounterCanPayUnreadAsZero"/>): a note inside a side pack is changed there too. Allocation-free.
    /// </summary>
    /// <param name="state">The session's read model.</param>
    public static long EverythingHeldUnreadAsZero(GameState state) => CoinUnreadAsZero(state) + NotesHeld(state);

    /// <summary>
    /// The whole purse, read once: the coin (unread stays -1) and every note stack with its place, which is the counter's read itself (<see cref="PurseNotes.Read"/>: the main pack's stacks, the notes of
    /// <paramref name="first"/> before the rest, then the side packs' in their slot order). Allocates the lists: for a trip, a counter or a report, not for a check that runs every frame.
    /// </summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="first">The notes read first among the main pack's (a ground's <see cref="GroundPurse.Notes"/>), or null.</param>
    public static PurseReading Read(GameState state, IReadOnlyList<PurseNote>? first = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var atCounter = PurseNotes.Read(state, first);
        var owner = state.Self.Inventory.Owner;
        var stacks = new List<PurseStack>(atCounter.Notes.Count);
        foreach (var held in atCounter.Notes)
        {
            stacks.Add(new PurseStack(held.Guid, held.Note, held.Stack, held.InSidePack ? PursePlace.SidePack : PursePlace.MainPack, held.InSidePack ? held.Container : owner));
        }

        return new PurseReading(atCounter.Coin, stacks);
    }

    /// <summary>The faces of the trade notes among <paramref name="entries"/> (one container's list), stacks counted; an entry with no description yet counts nothing.</summary>
    private static long Faces(GameState state, IReadOnlyList<InventoryEntry> entries)
    {
        long faces = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            if (state.Objects.TryGet(entries[i].Guid, out var item) && NoteOf(item.Weenie.WeenieClassId) is { } note)
            {
                faces += (long)GearInventory.StackOf(state, item) * note.Value;
            }
        }

        return faces;
    }

    /// <summary>The trade note of that weenie class (one of ACE's ten), or null.</summary>
    private static PurseNote? NoteOf(uint wcid)
    {
        var notes = PurseNotes.All;
        for (var n = 0; n < notes.Count; n++)
        {
            if (notes[n].Wcid == wcid)
            {
                return notes[n];
            }
        }

        return null;
    }
}
