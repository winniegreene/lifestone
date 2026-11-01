using Lifestone.Client.Session.Grounds;

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
