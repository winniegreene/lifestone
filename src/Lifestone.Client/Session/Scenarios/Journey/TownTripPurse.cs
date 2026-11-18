using System.Globalization;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 item 14 (m6-purse; live 2026-11-12: a level 39 mage held about 870,000 pyreals in trade notes and 40,869 in coin, and the learn trip by the Town Network said the coin did not cover four level VI scrolls at 200,000, trip
/// after trip): the check before of a learn trip to one town, on the whole purse and the pack, pure. The buy at the counter already changes a note when the coin is short (<c>ResupplyRun.BuyLine</c>; since M6 16 E5b the vendor counter's buy, <c>VendorCounter.Buy</c> and its <c>ChangeNote</c>: a vendor pays a
/// promissory note at its face, ACE <c>Vendor.cs</c> 591-599 @95ef426d), and every scrivener's MerchandiseItemTypes holds PromissoryNote 0x40000 (<see cref="ScrivenersTakeNotes"/>), so the purse is the coin and the faces of the
/// notes that buy would offer (<c>PurseNotes.Read</c>: every note she holds; M6 16 E5a2: a side pack's too, changed where it lies, until then the main pack's alone).
/// <para><b>The rule.</b> The scrolls are taken cheapest first while the purse pays them; the rest wait (named). A town none of whose scrolls the purse pays is refused <c>purse-short</c>, and the caller tries the plan's next town.
/// The pack: a scroll bought needs a free slot of the main pack (<c>BuyLine</c> refuses with none), and so does one note split off a stack (the counter's <c>ChangeNote</c>; ACE's <c>Player_Inventory.cs</c> 2243-2438
/// <c>HandleActionStackableSplitToContainer</c> makes the split a new object in the target container, 2424-2436 and 2445, and takes any container of hers as the target, 2256; a split into a side pack would save the note's slot but
/// not the scroll's, so the main pack's slot is the one rule). ACE then counts the payout's coin stacks, 25,000 each, against the free slots of all her packs before it takes the note and refuses the sale when they do not fit
/// (<c>Player_Commerce.cs</c> 176-191, <c>ItemsToReceive</c>; <c>Container.GetFreeInventorySlots</c>, Container.cs 214-225, counts the side packs): the first note the buys would change is checked against that count. Either is
/// <c>pack-full</c>, named before the trip leaves. Later notes of the same trip are checked at the counter by the buy itself (the slots move with each scroll and each payout).</para>
/// </summary>
internal static class TownTripPurse
{
    /// <summary>
    /// Every scrivener buys trade notes: MerchandiseItemTypes (PropertyInt 74) is 0x42080 for each weenie named a Scrivener and 0x42000 for Old Wylfed's kind (654), all with PromissoryNote 0x40000: SELECT s.object_Id, HEX(i.value) FROM
    /// weenie_properties_string s JOIN weenie_properties_int i ON i.object_Id = s.object_Id AND i.type = 74 WHERE s.type = 1 AND s.value LIKE '%Scrivener%' (52 rows, a SELECT on the world copy, 2026-11-12). The catalog's vendor view
    /// carries mask 0 (nothing is sold to a scrivener as loot), so the check before reads this; the counter reads the wire's mask (<c>VendorStock.BuysType</c>) and names a vendor that takes none.
    /// </summary>
    internal const bool ScrivenersTakeNotes = true;

    /// <summary>A stack of one trade note she holds.</summary>
    /// <param name="Name">The note's name.</param>
    /// <param name="Face">One note's face value.</param>
    /// <param name="Stack">The notes in the stack.</param>
    internal readonly record struct Note(string Name, int Face, int Stack);

    /// <summary>A scroll of the trip at the catalog's price.</summary>
    /// <param name="Name">The spell's name.</param>
    /// <param name="Price">The scroll's price.</param>
    internal readonly record struct Line(string Name, long Price);

    /// <summary>The check's answer for one town.</summary>
    /// <param name="Goes">True when the trip leaves for this town.</param>
    /// <param name="Reason"><c>none</c>, <c>purse-short</c> or <c>pack-full</c>.</param>
    /// <param name="Buys">The lines the purse pays, cheapest first (indexes into the lines given).</param>
    /// <param name="Waits">The lines that wait for the purse.</param>
    /// <param name="NotesValue">The faces of the notes held.</param>
    /// <param name="Cost">The price of every line.</param>
    /// <param name="Pays">The price of the lines bought.</param>
    /// <param name="NotesChanged">The notes the buys would change at the counter.</param>
    /// <param name="Why">The sentence for the log: the coin, the notes, the cost, and what follows.</param>
    internal sealed record Verdict(bool Goes, string Reason, IReadOnlyList<int> Buys, IReadOnlyList<int> Waits, long NotesValue, long Cost, long Pays, int NotesChanged, string Why);

    /// <summary>The held note to change for <paramref name="shortfall"/> pyreals: the smallest face that covers it, else the largest; -1 with none held. The one rule of the counter (<c>VendorCounter.NextNote</c>) and of this check.</summary>
    /// <param name="faces">The faces of the notes held, one a stack.</param>
    /// <param name="shortfall">The pyreals the coin is short of.</param>
    internal static int Pick(IReadOnlyList<int> faces, long shortfall)
    {
        var covering = -1;
        var largest = -1;
        for (var i = 0; i < faces.Count; i++)
        {
            if (faces[i] >= shortfall && (covering < 0 || faces[i] < faces[covering]))
            {
                covering = i;
            }

            if (largest < 0 || faces[i] > faces[largest])
            {
                largest = i;
            }
        }

        return covering >= 0 ? covering : largest;
    }

    /// <summary>The check before for one town (see the class).</summary>
    /// <param name="coin">The coin held.</param>
    /// <param name="notes">The note stacks she holds (the counter's read, <c>PurseNotes.Read</c>: the main pack's, then the side packs').</param>
    /// <param name="vendorTakesNotes">True when the town's vendor buys trade notes (<see cref="ScrivenersTakeNotes"/>).</param>
    /// <param name="freeMain">The main pack's free slots.</param>
    /// <param name="freeAll">The free slots of the main pack and the side packs.</param>
    /// <param name="lines">The town's scrolls, in the plan's order.</param>
    internal static Verdict Judge(long coin, IReadOnlyList<Note> notes, bool vendorTakesNotes, int freeMain, int freeAll, IReadOnlyList<Line> lines)
    {
        var c = CultureInfo.InvariantCulture;
        coin = Math.Max(0, coin);
        var stacks = vendorTakesNotes ? notes.Where(n => n.Stack > 0 && n.Face > 0).ToList() : [];
        var held = stacks.Sum(n => n.Stack);
        var notesValue = stacks.Sum(n => (long)n.Face * n.Stack);
        var purse = coin + notesValue;
        var cost = lines.Sum(l => l.Price);
        var purseText = vendorTakesNotes
            ? string.Create(c, $"the coin {coin} and the notes {notesValue} ({held} held)")
            : string.Create(c, $"the coin {coin} (the vendor takes no trade note: the notes are not counted)");

        var buys = new List<int>();
        var waits = new List<int>();
        long pays = 0;
        foreach (var i in Enumerable.Range(0, lines.Count).OrderBy(i => lines[i].Price))
        {
            if (pays + lines[i].Price <= purse)
            {
                buys.Add(i);
                pays += lines[i].Price;
            }
            else
            {
                waits.Add(i);
            }
        }

        if (buys.Count == 0)
        {
            var cheapest = lines.Count == 0 ? default : lines.MinBy(l => l.Price);
            return new(false, "purse-short", buys, waits, notesValue, cost, 0, 0, string.Create(c, $"{purseText} do not cover the cheapest of the scrolls ({cheapest.Name} at {cheapest.Price}; {cost} in all): no trip to this town; nothing is remembered as refused"));
        }

        if (freeMain <= 0)
        {
            return new(false, "pack-full", buys, waits, notesValue, cost, pays, 0, string.Create(c, $"{purseText} cover {pays} of the scrolls {cost}, but the main pack has no free slot: a scroll bought needs one, and so does a note split off its stack: no trip until the pack has room; nothing is remembered as refused"));
        }

        // The buys as the counter makes them, one line at a time: the coin first, then a note for what is short (Pick), the note's face added and the price taken.
        var faces = stacks.Select(n => n.Face).ToList();
        var left = stacks.Select(n => n.Stack).ToList();
        var have = coin;
        var changed = 0;
        (Note Note, int Slots)? first = null;
        foreach (var i in buys)
        {
            while (lines[i].Price > have && Pick(faces, lines[i].Price - have) is >= 0 and var k)
            {
                first ??= (stacks[k] with { Stack = left[k] }, PayoutStacks(faces[k]) + (left[k] > 1 ? 1 : 0));
                changed++;
                have += faces[k];
                if (--left[k] == 0)
                {
                    stacks.RemoveAt(k);
                    faces.RemoveAt(k);
                    left.RemoveAt(k);
                }
            }

            have -= lines[i].Price;
        }

        var bought = buys.Count == lines.Count
            ? string.Create(c, $"cover the scrolls {cost}")
            : string.Create(c, $"cover {pays} of the scrolls {cost}: buys {string.Join(", ", buys.Select(i => lines[i].Name))} (cheapest first); {string.Join(", ", waits.Select(i => lines[i].Name))} wait(s) for the purse");
        if (first is not { } note)
        {
            return new(true, "none", buys, waits, notesValue, cost, pays, 0, string.Create(c, $"{purseText} {bought}; the coin pays: no note is changed"));
        }

        var split = note.Note.Stack > 1 ? string.Create(c, $" and 1 for the split from its stack of {note.Note.Stack}") : string.Empty;
        var need = string.Create(c, $"{note.Note.Name} needs {note.Slots} free slot(s) ({PayoutStacks(note.Note.Face)} coin stack(s) of {ResupplyRun.CoinStackMax} for its payout{split})");
        if (note.Slots > freeAll)
        {
            return new(false, "pack-full", buys, waits, notesValue, cost, pays, changed, string.Create(c, $"{purseText} {bought}, but the first note to change, {need}, and {freeAll} are free in her packs ({freeMain} in the main pack): ACE would refuse the sale: no trip until the pack has room; nothing is remembered as refused"));
        }

        return new(true, "none", buys, waits, notesValue, cost, pays, changed, string.Create(c, $"{purseText} {bought}; {changed} note(s) are changed at the counter, the first {need}; {freeAll} are free in her packs ({freeMain} in the main pack)"));
    }

    /// <summary>The coin stacks ACE makes of a payout: 25,000 a stack (<c>Player_Commerce.cs</c> 176-194 @95ef426d).</summary>
    private static int PayoutStacks(int face) => (int)((face + (long)ResupplyRun.CoinStackMax - 1) / ResupplyRun.CoinStackMax);
}
