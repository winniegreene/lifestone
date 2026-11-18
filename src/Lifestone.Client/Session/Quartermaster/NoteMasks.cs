using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Quartermaster;

/// <summary>
/// M6 item 16, slice E5a2 (docs/automation-engine.md section 2.5; m6-plan.md D139): what is known, before a trip, of whether a vendor's counter takes a trade note. A vendor buys an item only when its ItemType is in his
/// mask (ACE's MerchandiseItemTypes, PropertyInt 74: <c>Player.VerifySellItems</c>, <c>Player_Commerce.cs</c> 236 and 268 @95ef426d), so a note is changed to coin only at a counter whose mask holds PromissoryNote
/// 0x40000 (<see cref="VendorStock.PromissoryNoteType"/>). The counter itself reads the wire's mask (<see cref="VendorStock.BuysType"/>) and stops by name; a judge that counts the notes before the trip asks here.
/// <para><b>What is known.</b> A ground profile's vendor row lists the mask (<c>buys_item_types</c>, from the pinned world SQL v0.9.296). A row with mask 0 is the loader's "no loot buyer" and says nothing of notes
/// (the den profiles list the Scrivener of Item Magic 20214 so, and every scrivener's weenie takes a note: <c>TownTripPurse.ScrivenersTakeNotes</c>), so 0 is "not known", as a vendor no profile lists is (a town stock's
/// seller, the Merchant of the lockpick). Not known counts the notes: the counter stops by name (<see cref="VendorCounter"/>).</para>
/// <para><b>What the profiles hold (2026-11-17).</b> The 19 profiles list 101 vendor rows of 10 weenies; every non-zero mask among them holds the note type, so no vendor is judged on the coin alone today
/// (<see cref="Lacking"/> lists the rows whose mask lacks it: the two zero rows). The rule stands for the first profile that lists one.</para>
/// </summary>
public static class NoteMasks
{
    private static readonly Lazy<Dictionary<uint, uint>> Listed = new(Build);

    /// <summary>The mask a ground profile lists for the vendor's weenie class (the first non-zero one, in the catalog's order), or null: not known before the trip. Allocation-free once built.</summary>
    /// <param name="vendorWcid">The vendor's weenie class.</param>
    public static uint? Of(uint vendorWcid) => Listed.Value.TryGetValue(vendorWcid, out var mask) ? mask : null;

    /// <summary>True when the vendor's known mask takes a trade note, false when it is known and takes none, null when no profile lists it. Allocation-free once built.</summary>
    /// <param name="vendorWcid">The vendor's weenie class.</param>
    public static bool? Takes(uint vendorWcid) => TakesNote(Of(vendorWcid));

    /// <summary>True when the mask takes a trade note, false when it takes none, null for no mask (not known).</summary>
    /// <param name="mask">The vendor's mask, or null.</param>
    public static bool? TakesNote(uint? mask) => mask is { } known ? (known & VendorStock.PromissoryNoteType) != 0 : null;

    /// <summary>The vendor rows of the ground profiles whose mask lacks the note type, a zero mask included, in the catalog's order: the profile's key, the vendor's name and weenie class, the mask. Allocates: for a report or a test.</summary>
    public static IReadOnlyList<(string Ground, string Name, uint Wcid, uint Mask)> Lacking =>
        [.. GroundCatalog.All.SelectMany(g => g.Vendors.Where(v => (v.BuysItemTypes & VendorStock.PromissoryNoteType) == 0).Select(v => (g.Key, v.Name, v.Wcid, v.BuysItemTypes)))];

    private static Dictionary<uint, uint> Build()
    {
        var masks = new Dictionary<uint, uint>();
        foreach (var ground in GroundCatalog.All)
        {
            foreach (var vendor in ground.Vendors)
            {
                if (vendor.BuysItemTypes != 0)
                {
                    masks.TryAdd(vendor.Wcid, vendor.BuysItemTypes);
                }
            }
        }

        return masks;
    }
}
