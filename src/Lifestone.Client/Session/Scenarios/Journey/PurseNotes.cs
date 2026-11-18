using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M5 7 D136 follow-up (soak round 13): the coin above the purse's floor into trade notes, greedily, the largest note first. Archmage Cindrue sells every note from Trade Note (100) to (250,000) (her CreateList, ACE-World-Database
/// v0.9.296, weenie_properties_create_list rows 21060-21069) at 1.15 times the face (<c>Vendor.cs</c> 577-585); the trip offers only the notes of the purse's <c>min_note</c> and up (25,000 for the den: a smaller note saves no
/// pack slot and costs 15%), so the coin left after the plan is under the smallest offered note's price.
/// </summary>
internal static class PurseNotes
{
    /// <summary>
    /// The ten trade notes (ACE-World-Database v0.9.296: the weenies' names, PropertyString 1, and faces, Value PropertyInt 19; every one ItemType 0x40000 and MaxStackSize 250). The vendor that sells the purse's largest note is
    /// asked for all of them; its 0062 decides which it has (Archmage Cindrue, wcid 2304, lists all ten: weenie_properties_create_list rows 21060-21069, destination 4). Count is 0: none is granted.
    /// </summary>
    internal static IReadOnlyList<PurseNote> All { get; } =
    [
        new(20630, "Trade Note (250,000)", 250_000, 0),
        new(20629, "Trade Note (200,000)", 200_000, 0),
        new(20628, "Trade Note (150,000)", 150_000, 0),
        new(2627, "Trade Note (100,000)", 100_000, 0),
        new(2626, "Trade Note (50,000)", 50_000, 0),
        new(2625, "Trade Note (10,000)", 10_000, 0),
        new(2624, "Trade Note (5,000)", 5_000, 0),
        new(2623, "Trade Note (1,000)", 1_000, 0),
        new(2622, "Trade Note (500)", 500, 0),
        new(2621, "Trade Note (100)", 100, 0),
    ];

    /// <summary>ACE's price of a promissory note at any vendor: <c>max(1, ceil(1.15 x Value - 0.1))</c> in its float arithmetic (<c>Vendor.GetSellCost</c>, <c>Vendor.cs</c> 577-585): 287,500 for the 250,000, 115 for the 100.</summary>
    internal static long PriceOf(int face) => Math.Max(1L, (long)Math.Ceiling(((float)1.15 * face) - 0.1));

    /// <summary>A stack of one trade note she holds: its guid, the note, how many the stack holds and the side pack it lies in.</summary>
    /// <param name="Guid">The stack's guid.</param>
    /// <param name="Note">The note (its face is <see cref="PurseNote.Value"/>).</param>
    /// <param name="Stack">The notes in the stack.</param>
    /// <param name="Container">M6 16 E5a2: the side pack that holds the stack, or 0 for the main pack.</param>
    internal readonly record struct HeldNote(uint Guid, PurseNote Note, int Stack, uint Container = 0)
    {
        /// <summary>The faces of the stack's notes.</summary>
        internal long Worth => (long)Note.Value * Stack;

        /// <summary>True when the stack lies inside a side pack.</summary>
        internal bool InSidePack => Container != 0;
    }

    /// <summary>
    /// The purse as a buy at a counter sees it: the coin, and the note stacks a buy can change, which since M6 16 E5a2 is every stack she holds (the main pack's first, then the side packs'). The same purse with the totals by
    /// name is the quartermaster's <see cref="Lifestone.Client.Session.Quartermaster.PurseView"/> (M6 16 E5a1), which is built on this read.
    /// </summary>
    /// <param name="Coin">The coin held (-1 unread).</param>
    /// <param name="Notes">The note stacks: the main pack's, then those inside the side packs.</param>
    internal sealed record Purse(long Coin, IReadOnlyList<HeldNote> Notes)
    {
        /// <summary>The faces of every note held.</summary>
        internal long NotesWorth => Notes.Sum(n => n.Worth);
    }

    /// <summary>
    /// M6 16 E5b: the one read of the purse for a buy at a counter (the town trip's, <c>ResupplyRun.HeldNotes</c>, and the vendor counter's): the coin (<see cref="StationItems.CoinsOf"/>) and the trade note stacks she holds,
    /// every one of ACE's ten faces (<see cref="All"/>; M6 7 AU: the 100,000 note sat uncashed beside "no note is left" while only the profile's faces were read: a vendor pays any note at its face, <c>Vendor.cs</c> 591-599
    /// @95ef426d), the notes of <paramref name="first"/> (a ground's purse) before the rest.
    /// <para>M6 16 E5a2: the stacks inside the side packs follow the main pack's (ContainerType 0), in the side packs' slot order. ACE sells an item from anywhere she holds it: <c>Player.VerifySellItems</c>
    /// (<c>Player_Commerce.cs</c> 232-298 @95ef426d) looks the guid up in <c>GetAllPossessions()</c> (234, 242), which is the main pack, the contents of every side pack and the worn items (<c>Player_Inventory.cs</c> 29-44),
    /// and the removal searches the side packs (<c>Container.TryRemoveFromInventory</c>, <c>Container.cs</c> 644-684). So a note inside a side pack is changed where it lies; the main pack's are still read, and so taken, first
    /// among notes of one face.</para>
    /// </summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="first">The notes read first (a ground's <see cref="GroundPurse.Notes"/>), or null.</param>
    internal static Purse Read(GameState state, IReadOnlyList<PurseNote>? first = null)
    {
        var held = new List<HeldNote>();
        var order = (first ?? []).Concat(All).DistinctBy(n => n.Wcid).ToList();
        foreach (var note in order)
        {
            foreach (var guid in StationItems.InPackOf(state, note.Wcid))
            {
                held.Add(new HeldNote(guid, note, state.Objects.TryGet(guid, out var o) ? GearInventory.StackOf(state, o) : 1));
            }
        }

        var inventory = state.Self.Inventory;
        var sides = inventory.SidePacks;
        for (var i = 0; i < sides.Count; i++)
        {
            var inside = inventory.ItemsIn(sides[i].Guid);
            for (var j = 0; j < inside.Count; j++)
            {
                if (state.Objects.TryGet(inside[j].Guid, out var item) && order.FirstOrDefault(n => n.Wcid == item.Weenie.WeenieClassId) is { } note)
                {
                    held.Add(new HeldNote(inside[j].Guid, note, GearInventory.StackOf(state, item), sides[i].Guid));
                }
            }
        }

        return new Purse(StationItems.CoinsOf(state), held);
    }

    /// <summary>A note the vendor sells, at its price in pyreals.</summary>
    internal readonly record struct Offer(PurseNote Note, long Price);

    /// <summary>How many of a note the plan buys, at its price.</summary>
    internal readonly record struct Pick(PurseNote Note, long Price, int Count);

    /// <summary>
    /// The greedy plan for <paramref name="above"/> pyreals over the floor: by face value from the largest, as many of each as the coin left pays for. <c>Left</c> is the coin the plan does not spend (under the smallest
    /// offered price when anything is offered). Nothing is bought for 0 or less.
    /// </summary>
    internal static (List<Pick> Picks, long Left) Greedy(long above, IEnumerable<Offer> offers)
    {
        var picks = new List<Pick>();
        var left = Math.Max(0, above);
        foreach (var offer in offers.Where(o => o.Price > 0).OrderByDescending(o => o.Note.Value).ThenBy(o => o.Price))
        {
            var count = left / offer.Price;
            if (count <= 0)
            {
                continue;
            }

            picks.Add(new Pick(offer.Note, offer.Price, (int)Math.Min(count, int.MaxValue)));
            left -= count * offer.Price;
        }

        return (picks, left);
    }
}
