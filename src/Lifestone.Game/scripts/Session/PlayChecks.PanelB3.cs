using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Magic;

namespace Lifestone.Game.Sessions;

/// <summary>
/// Slice b3 (Spellbook and components (page 13)), docs/milestones/m3-plan.md section 4.6 row "b3". The stand-in grants three kit components through the item tool (a Lead Scarab, a Hyssop and a Red Taper, 20 each), then page 13
/// is opened and judged against what the model holds: the spellbook tab lists the known spells that pass the book's filter word in retail's order, each as a cell showing that spell (the stand-in's book is the probe spellbook of M3 2.2 fix B, sent on either description path), the components tab lists the pack's components by category with the summed counts. The page is closed again.
/// </summary>
public sealed partial class PlayChecks
{
    private async partial Task PanelB3ChecksAsync(GamePlayScreen shell)
    {
        const string bookName = "the-spellbook-lists-the-known-spells-that-pass-the-filter-in-retails-order-as-the-book-has-them";
        const string componentsName = "the-components-tab-lists-the-packs-components-by-category-with-summed-counts";
        const string closeName = "the-spell-page-closes-and-draws-nothing-while-hidden";
        var session = _host.Session;
        var panels = shell.PanelControllers;
        var book = panels?.ControllersOf(GameSpellbookPage.Page).OfType<GameSpellbookPage>().FirstOrDefault();
        var components = panels?.ControllersOf(GameComponentsPage.Page).OfType<GameComponentsPage>().FirstOrDefault();
        if (session is null || book is null || components is null)
        {
            Check(bookName, false, $"session={session is not null} spellbook_controller={book is not null} components_controller={components is not null}");
            return;
        }

        // The kit: one component of three categories.
        var wcids = new[] { WcidOfComponent(1), WcidOfComponent(7), WcidOfComponent(63) };
        var handles = wcids.Select(wcid => session.Tools.SpawnItem(wcid, 20, ActionSource.Host)).ToArray();
        var granted = await WaitFor(() => handles.All(static h => h.IsCompleted), 20.0);
        var guids = handles.Select(static h => h.Result?.Guid ?? 0u).ToArray();
        var objects = session.State.Objects;
        var created = await WaitFor(() => guids.All(g => g != 0 && objects.Contains(g)), 10.0);

        var opened = await OpenPanelAsync(shell, GameSpellbookPage.Page, 6);
        await WaitFor(() => session.SpellTableLoaded.IsCompleted, 20.0);
        var settled = await WaitFor(() => !book.IsFilling, 15.0);
        var spellbook = session.State.Self.Spellbook;
        var shown = book.ShownSpells.ToList();
        var allKnown = shown.All(spellbook.Knows);

        // M3 2.2 fix B (2.2-R5): the stand-in's book is the probe spellbook (StandInPlayerStats.ProbeSpellbook: 40 real spells and one the table lacks, sent with the filter word that hides level 8). What must be listed is counted here from
        // the hand-written filter bits of each row and the word as sent, not from the page's rule or its SortedSpells; the order is judged by fixed ids (the lowest display orders, 45, 55 and 138, and the highest, 10898) and by the table's own
        // display order rising along the list.
        var word = StandInPlayerStats.ProbeSpellbookFilters;
        var visibleProbe = new HashSet<uint>();
        foreach (var (id, bits) in StandInPlayerStats.ProbeSpellbook)
        {
            if ((word & bits) == bits)
            {
                visibleProbe.Add(id);
            }
        }

        var listedProbe = shown.Count(visibleProbe.Contains);
        var hiddenListed = shown.Count(id => StandInPlayerStats.ProbeSpellbook.Any(r => r.Id == id && (word & r.FilterBits) != r.FilterBits));
        var unknownListed = shown.Contains(StandInPlayerStats.ProbeUnknownSpell);
        var extras = shown.Count - listedProbe; // spells other checks learned into the book (none expected: the learn checks use ids outside the probe book and come later)
        uint[] firstIds = [2092, 2120, 59];
        var firstOk = shown.Count >= firstIds.Length && shown.Take(firstIds.Length).SequenceEqual(firstIds);
        var lastOk = shown.Count > 0 && shown[^1] == 234;
        var risingOrder = true;
        for (var i = 1; i < shown.Count; i++)
        {
            risingOrder &= session.Spell(shown[i - 1]) is { } before && session.Spell(shown[i]) is { } after && before.DisplayOrder <= after.DisplayOrder;
        }

        Check(
            bookName,
            opened && created && settled && spellbook.IsKnown && session.SpellTableReady && shown.Count > 0 && shown.Count == visibleProbe.Count && listedProbe == visibleProbe.Count && extras == 0 && hiddenListed == 0 && !unknownListed
                && firstOk && lastOk && risingOrder && allKnown && book.Filters == word && spellbook.Filters == word && book.List.OccupiedCount == shown.Count,
            $"opened={opened} book_known={spellbook.IsKnown} table_ready={session.SpellTableReady} spells_in_book={spellbook.Count} filter_word=0x{spellbook.Filters:X} listed={shown.Count} expected={visibleProbe.Count} (independent count of the probe rows) listed_probe={listedProbe} hidden_but_listed={hiddenListed} unknown_listed={unknownListed} extras={extras} first=[{string.Join(",", shown.Take(3))}] last={(shown.Count > 0 ? shown[^1] : 0)} first_last_ok={firstOk && lastOk} display_order_rising={risingOrder} all_known={allKnown} cells={book.List.CellCount} filling={book.IsFilling}");

        // The components tab: the pack's components by category, counts summed over the stacks (derived here from the model, not from the page).
        var panel = shell.PanelHost?.ElementFor(GameComponentsPage.Page) as UiPanel;
        var switched = panel is not null && panel.OpenTab(0x100002AA);
        await Frames(6);
        var built = await WaitFor(() => !components.IsBuilding, 10.0);
        var expectedCounts = new SortedDictionary<uint, int>();
        var inventory = session.State.Self.Inventory;
        foreach (var entry in inventory.Pack)
        {
            if (objects.TryGet(entry.Guid, out var obj) && obj.IsSpellComponent())
            {
                expectedCounts[obj.WeenieClassOf()] = expectedCounts.GetValueOrDefault(obj.WeenieClassOf()) + Math.Max(obj.ItemFields().StackSize, 1);
            }
        }

        var rows = components.Rows.ToList();
        var shownCounts = rows.Where(static r => !r.IsTitle).ToDictionary(static r => r.Wcid, static r => int.TryParse(r.Count, out var n) ? n : -1);
        var titles = rows.Count(static r => r.IsTitle);
        var categories = rows.Where(static r => !r.IsTitle).Select(static r => r.Category).Distinct().Count();
        var countsMatch = shownCounts.Count == expectedCounts.Count && expectedCounts.All(kv => shownCounts.TryGetValue(kv.Key, out var n) && n == kv.Value);
        Check(
            componentsName,
            switched && built && granted && expectedCounts.Count >= 3 && countsMatch && titles == categories && categories >= 3 && wcids.All(shownCounts.ContainsKey),
            $"tab_switched={switched} built={built} kit_granted={granted} classes_in_pack={expectedCounts.Count} rows={shownCounts.Count} titles={titles} categories={categories} counts_match={countsMatch} counts=[{string.Join(",", shownCounts.Select(static kv => $"{kv.Key}:{kv.Value}"))}] (icons are the stand-in's placeholder 0x06000001)");

        // Closed: the page does nothing while hidden.
        await ClosePanelAsync(shell, GameSpellbookPage.Page);
        var plans = book.Plans;
        var redraws = components.Redraws;
        await Frames(6);
        Check(closeName, !shell.PanelHost!.IsOpen && book.Plans == plans && components.Redraws == redraws, $"open={shell.PanelHost.IsOpen} plans={plans}->{book.Plans} redraws={redraws}->{components.Redraws}");
    }

    private static uint WcidOfComponent(uint componentId) => ComponentWeenies.TryGetByComponent(componentId, out var c) ? c.Wcid : 0u;
}
