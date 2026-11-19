using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// What the resupply station is told by its caller (M5 item 3 m3): whether looting is on (D38: off by default, so no walk to the loot buyer and <c>sold=0</c>) and, when it is, the guids the loot rule kept, which are the only items it
/// offers. With looting on and no kept list the station sells nothing and says so (<c>TODO(m5-resupply-kept-list)</c>): it never guesses the loot from the pack, where the mage's foci and kit also lie.
/// </summary>
/// <param name="Loot">True when the loot step ran (the fight's <c>--fight-loot</c>, <see cref="FightOptions.Loot"/>).</param>
/// <param name="Kept">The guids the loot rule kept, read when the station starts, or null when the caller has no list.</param>
/// <param name="Kit">
/// M5 3 F5 (D53, the death handler's rearm): the kit lines to buy, one each, at the home vendor whose <c>sells</c> lists them (<see cref="ResupplyPlanner.KitBuyList"/>), or null (the stock trip). A kit trip counts and sells nothing: it reads no stock,
/// offers no loot and buys only these lines, out of the purse as the stock trip does (a note changed when the coin is short).
/// </param>
/// <param name="Memory">M5 7 G1: what the trips remember of the items a vendor refused (see <see cref="SaleMemory"/>), or null (a fresh memory for the one trip: nothing reaches a second refusal).</param>
/// <param name="Gear">M5 7 D121, D122, D125: the gear phase runs (the packs sorted to their jobs, the footwear, the self-equip, the worn items' mana): <see cref="ResupplyRun.GearPhase"/>. Off for a kit trip.</param>
/// <param name="Replace">M6 7 AH (the death handler's replace step): the trip buys only the gear floor's missing pieces (a robe, gauntlets, one caster: <see cref="DeathReplace.Plan"/>), wears them (<see cref="ResupplyRun.EquipPass"/>) and nothing else: no count, no sale, no kit, no mana charge.</param>
/// <param name="AccountName">The login name the server hashes (the host's, never the secrets key), for a scroll's components on the account's full formula (<see cref="ScrollSupply"/>); null: a school without its focus cannot be named (a loud TODO; the scroll is still bought, D116). Hashed, never logged.</param>
/// <param name="TownShop">M6 14 T: the buys of a stop in another town, all at one vendor (<see cref="TownStockOrder"/>): the run then walks to that vendor, buys the lines and ends; the rung change runs it between the two hops of the stop. Null: the home walk.</param>
public sealed record ResupplyOptions(bool Loot = false, Func<IReadOnlyCollection<uint>>? Kept = null, IReadOnlyList<KitLine>? Kit = null, SaleMemory? Memory = null, bool Gear = false, bool Replace = false, string? AccountName = null, IReadOnlyList<TownStockOrder>? TownShop = null);

/// <summary>
/// M5 7 G1 (edge audit item 2): the items a vendor refused, trip after trip. A sale offers every main-pack item that is not the kit, so an item no vendor will take would be offered for ever; one refused on two trips is dropped at the
/// home (logged once) and never offered again.
/// </summary>
public sealed class SaleMemory
{
    /// <summary>How many trips refused an item (by guid).</summary>
    public Dictionary<uint, int> Refused { get; } = [];

    /// <summary>The items dropped (or whose drop was tried): never offered again.</summary>
    public HashSet<uint> Dropped { get; } = [];

    /// <summary>M5 7 F30 (the owner): the items the loot rule kept aside as worth a look (<see cref="KeepAsideRule"/>): never offered to a vendor, whatever an appraisal says.</summary>
    public HashSet<uint> KeptAside { get; } = [];

    /// <summary>M6 7 AU: the foci a vendor refused or that did not arrive in the pack after a buy: the plan does not try them again this session (no loop).</summary>
    public HashSet<uint> FociRefused { get; } = [];

    /// <summary>M6 7 AX: the wares bought whose stack had not shown when <see cref="ResupplyRun.BoughtArrivalSeconds"/> ran out, with the total expected held (by weenie class id). The next plan holds such a line out once (<c>TODO(m6-bought-unseen)</c>), so a late update is never bought twice.</summary>
    public Dictionary<uint, int> BoughtUnseen { get; } = [];

    /// <summary>The owner's rule of 2026-11-09 (D116): the components bought scrolls need and a trip could not supply, by weenie class id: the next trip buys them first, ahead of new scrolls, until the pack holds the count (<see cref="ResupplyRun"/>). Session memory; after a restart the buff passes name a missing component again (<see cref="ComponentNeeds"/>).</summary>
    public Dictionary<uint, ComponentShoppingLine> ComponentShopping { get; } = [];

    /// <summary>
    /// M6 sprint (runs 1 to 5: "The Health Draughts is unsellable." and the like, 19 times in run 5): ACE's IsSellable verdict by weenie class, learned from appraisals (the bool rides an appraisal, an AssessmentProperty, and its
    /// absence on a successful one means sellable, <c>WorldObject_Properties.cs</c> 1460-1463 @95ef426d). A class is appraised once a session; the sale reads the verdict for every item of it.
    /// </summary>
    public Dictionary<uint, bool> SellableClass { get; } = [];

    /// <summary>The memory of the run's journeys: every fight station builds its own factory, so the memory outlives them (item guids are unique within a session).</summary>
    public static SaleMemory Shared { get; } = new();

    /// <summary>The refusals that make a drop.</summary>
    public const int DropAfter = 2;

    /// <summary>M5 7 P7j (D143): the spells of the learn list whose buy or read was refused this session, with the reason: no trip tries them again (no loop).</summary>
    public Dictionary<uint, LearnReason> LearnRefused { get; } = [];
}

/// <summary>One line of the buy list: the stock line, the count the mage holds and the count to buy (<c>keep - held</c>), at the home vendor whose <c>sells</c> lists it.</summary>
/// <param name="Line">The stock line.</param>
/// <param name="Vendor">The home vendor who sells it (<see cref="HuntingGround.VendorSelling"/>).</param>
/// <param name="Held">The count held when the list was made.</param>
/// <param name="Count">The count to buy.</param>
public sealed record ResupplyBuy(StockLine Line, GroundVendor Vendor, int Held, int Count)
{
    /// <summary>The owner's rule of 2026-11-08, its gate taken out on 2026-11-09 (D116): on a scroll's buy, the components its spell needs (<see cref="ScrollSupply"/>): the buy list buys what the pack lacks from what the scroll leaves of the purse. Null: not a scroll under the rule.</summary>
    public IReadOnlyList<ScrollComponent>? Components { get; init; }

    /// <summary>The coin of the components the buy list planned for this scroll (0 when the pack holds them or the purse cannot pay them).</summary>
    public long ComponentCoin { get; init; }

    /// <summary>The components the buy list planned for this scroll, by name and count (<c>Silver Scarab x2</c>), or empty.</summary>
    public string ComponentNote { get; init; } = string.Empty;

    /// <summary>
    /// The buy of one kit line (M5 3 F5): a kit line has no keep or trip count, so its stock line is a stand-in of one (<c>Keep = TripUnder = StopUnder = 1</c>, never read by the buy), held 0, count 1.
    /// </summary>
    /// <param name="kit">The kit line.</param>
    /// <param name="vendor">The home vendor who sells it.</param>
    public static ResupplyBuy ForKit(KitLine kit, GroundVendor vendor)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(vendor);
        return new ResupplyBuy(new StockLine(kit.Wcid, kit.Name, 1, 1, 1), vendor, 0, 1);
    }

    /// <summary>
    /// M6 2 b: the buy of one gear floor line (<see cref="GearFloor"/>: a caster, a robe, a mana charge), beside the kit and never in it: a stand-in stock line of the line's count (<c>Keep</c> = the count, never read by the buy), held 0.
    /// </summary>
    /// <param name="line">The floor line.</param>
    public static ResupplyBuy ForFloor(GearFloorLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new ResupplyBuy(new StockLine(line.Wcid, line.Name, line.Count, 1, 1), line.Vendor, 0, line.Count);
    }
}

/// <summary>One stop of the trip: the vendor, the lines bought there and whether the loot is sold there first (he is the loot buyer and looting is on).</summary>
/// <param name="Vendor">The profile's vendor.</param>
/// <param name="Buys">The buy list's lines at this vendor, in the profile's order.</param>
/// <param name="Sell">True when the loot is sold here.</param>
/// <param name="SellCount">The kept items to offer here.</param>
public sealed record ResupplyVisit(GroundVendor Vendor, IReadOnlyList<ResupplyBuy> Buys, bool Sell, int SellCount)
{
    /// <summary>The walk's purpose line (D47): <c>walking: purpose=vendor:&lt;name&gt; (&lt;n&gt; lines to buy)</c>, with the items to sell when he is the loot buyer.</summary>
    public string WalkLine => string.Create(
        CultureInfo.InvariantCulture,
        $"walking: purpose=vendor:{Vendor.Name} ({Buys.Count} lines to buy{(Sell ? $", {SellCount} items to sell" : string.Empty)})");
}

/// <summary>
/// The resupply's plan from the profile alone (M5 item 3 m3, D46, D48): the buy list over the ground's stock lines, the loot buyer, and the vendors in the order of the shortest walk. No vendor, price or count lives here: every number is the
/// profile's or the counters'.
/// </summary>
public static class ResupplyPlanner
{
    /// <summary>
    /// The buy list: every stock line under its keep that a home vendor sells, the count to buy <c>keep - held</c>, in the profile's order. A carried line (no home vendor sells it, D8) is never on it: its stop is the counters'.
    /// </summary>
    /// <param name="ground">The fight's ground.</param>
    /// <param name="counters">The counters after a <see cref="LogisticsCounters.Check"/> that read the pack.</param>
    public static IReadOnlyList<ResupplyBuy> BuyList(HuntingGround ground, LogisticsCounters counters) => BuyList(ground, counters, -1);

    /// <summary>
    /// M6 7 X (spells are the armor): the buy list with the trip's scrolls inside the budget (<see cref="BuyList(HuntingGround, LogisticsCounters, long, uint?)"/>). With a scroll to buy the order of need is: every line to its stop line (the run
    /// cannot fight under it); the scrolls in the plan's order, each while the budget pays it (one it cannot pay goes to <paramref name="waiting"/>, the next is still tried); one of each component with no stop line, from the whole
    /// purse (a scroll read with no component to cast it is coin thrown away, and these wares cost 7 to 34 pyreals); then every line to its trip line, clear of it (M6 14 T, the cheapest lift first) and to its keep. The list is the stock lines in the profile's order, then the scrolls kept. With
    /// no scroll, or no budget, it is the list without scrolls.
    /// </summary>
    /// <param name="ground">The fight's ground.</param>
    /// <param name="counters">The counters after a <see cref="LogisticsCounters.Check"/> that read the pack.</param>
    /// <param name="purse">The pyreals the trip may count, or negative for no budget.</param>
    /// <param name="scrolls">The trip's scroll buys, in the plan's order (one each).</param>
    /// <param name="waiting">Takes the scrolls the budget cannot pay this trip.</param>
    /// <param name="shopping">The standing shopping list's lines (D116: components an earlier trip could not buy), bought ahead of new scrolls, from the whole purse; null or empty when there is none.</param>
    /// <param name="called">M6 16 Q10: the weenie class id of the stock line the carried trip order names, or null. Its lift clear of its trip line comes before the cheapest-lift pass (after the stop lines), inside the same trip share; null is today's list.</param>
    public static IReadOnlyList<ResupplyBuy> BuyList(HuntingGround ground, LogisticsCounters counters, long purse, IReadOnlyList<ResupplyBuy> scrolls, List<ResupplyBuy> waiting, IReadOnlyList<ResupplyBuy>? shopping = null, uint? called = null)
    {
        ArgumentNullException.ThrowIfNull(scrolls);
        ArgumentNullException.ThrowIfNull(waiting);
        shopping ??= [];
        var supplied = scrolls.Any(s => s.Components is not null); // the owner's rule: a scroll's components are planned beside it, budget or not
        return shopping.Count == 0 && (scrolls.Count == 0 || (!supplied && (purse < 0 || ground.Supplies.Purse.TripSpendPercent >= 100)))
            ? [.. BuyList(ground, counters, purse, called), .. scrolls]
            : Filled(ground, counters, purse < 0 ? int.MaxValue : purse, scrolls, waiting, shopping, called);
    }

    /// <summary>
    /// M6 7 V (r1h: one trip at level 10 spent 9,907 of the climber's coin on 198 items and the next found 14): the buy list within a budget. <paramref name="purse"/> is the pyreals the trip may count (the coin and the face of the notes), or a
    /// negative number for no budget (today's list for every caller that gives none). The budget is the ground's <c>purse.trip_spend_percent</c> of it, spent in the order of need: first every line to its trip line (the lines with a stop
    /// line, the components, before the others, the potion), then the top-up to the keeps in the same order; a line the budget cannot pay in full is bought in part, at the profile's price. The list stays in the profile's order.
    /// <para>M6 14 T (the sprint of 2026-11-12: a visit with 14,658 in the purse bought 27 Mana Potions and left the Silver Scarab on its trip line, one use from the next trip): between the two, every line is lifted clear of its trip line
    /// (<see cref="LogisticsCounters.ClearOf"/>), the cheapest lift first, so a short budget leaves as few lines as it can sitting on their trip lines; only then is any line topped up to its keep.</para>
    /// </summary>
    /// <param name="ground">The fight's ground.</param>
    /// <param name="counters">The counters after a <see cref="LogisticsCounters.Check"/> that read the pack.</param>
    /// <param name="purse">The pyreals the trip may count, or negative for no budget.</param>
    /// <param name="called">M6 16 Q10: the weenie class id of the stock line the carried trip order names, or null. Its lift clear of its trip line comes before the cheapest-lift pass (after the stop lines), inside the same trip share; null is today's list.</param>
    public static IReadOnlyList<ResupplyBuy> BuyList(HuntingGround ground, LogisticsCounters counters, long purse, uint? called = null)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(counters);
        var rows = RowsOf(ground, counters);
        var budget = purse < 0 || ground.Supplies.Purse.TripSpendPercent >= 100 ? long.MaxValue : purse * ground.Supplies.Purse.TripSpendPercent / 100; // 100: no budget (BSD Mage, as before)
        var left = budget == long.MaxValue ? long.MaxValue : purse; // M6 sprint: the whole purse, for the casting components' last pass
        var order = Enumerable.Range(0, rows.Count).OrderBy(r => counters.LineAt(rows[r].Index).StopUnder > 0 ? 0 : 1).ToList(); // stable: the profile's order within a class
        for (var pass = 0; pass < 4; pass++)
        {
            foreach (var r in pass == 1 ? ClearOrder(ground, counters, rows, order, called) : order)
            {
                var (index, vendor, bought) = rows[r];
                var line = counters.LineAt(index);
                var have = counters.HeldOf(index) + bought;
                var target = pass switch { 0 => Math.Min(line.Keep, Math.Max(line.TripUnder, line.StopUnder)), 1 => ClearTarget(line), 2 => line.Keep, _ => CastingComponentKeep(line) }; // M6 sprint: pass 3, from the whole purse
                var want = target - have;
                if (want <= 0)
                {
                    continue;
                }

                if (ground.PriceOf(line.Wcid) is { } price && price > 0 && budget != long.MaxValue)
                {
                    want = (int)Math.Min(want, (pass == 3 ? left : Math.Min(budget, left)) / price);
                    budget = Math.Max(0, budget - ((long)want * price));
                    left -= (long)want * price;
                }

                rows[r] = (index, vendor, bought + want);
            }
        }

        return ListOf(rows, counters);
    }

    /// <summary>
    /// M6 sprint (the owner's decision of 2026-11-19): the count a casting component's line is topped up to from the rest of the purse, once the trip's share is spent: its keep when it has a stop line (one short of it and she
    /// cannot cast, so the fight stops for a trip) and the item is a spell component; 0 otherwise (a line with no stop line has its one of each from the whole purse already). Live, run 5's first trip
    /// (cache/m6/sprint/run5/step3-levelup.log 14:24:12 UTC, purse 10,009, the share 60%): nine scrolls took the share first, so 12 herb, powder and talisman lines were bought only to their stop line 2 of keep 4 and the Red and
    /// Indigo Tapers to 1 of 2, one burn from a trip each; their keeps would have cost 324 pyreals more, and she left with 2,051.
    /// </summary>
    private static int CastingComponentKeep(StockLine line) => line.StopUnder > 0 && ComponentWeenies.TryGetByWcid(line.Wcid, out _) ? line.Keep : 0;

    /// <summary>M6 14 T: the count the clear pass lifts a line to: clear of its trip line (<see cref="LogisticsCounters.ClearOf"/>), never over its keep.</summary>
    private static int ClearTarget(StockLine line) => Math.Min(line.Keep, LogisticsCounters.ClearOf(line));

    /// <summary>M6 14 T: the rows in the clear pass's order: the cheapest lift to the clear count first (the vendor's price times what is still missing), ties in <paramref name="order"/>; a short budget then clears the most lines.</summary>
    private static List<int> ClearOrder(HuntingGround ground, LogisticsCounters counters, List<(int Index, GroundVendor Vendor, int Count)> rows, List<int> order, uint? called = null)
    {
        long Cost(int r)
        {
            var line = counters.LineAt(rows[r].Index);
            return (long)(ground.PriceOf(line.Wcid) ?? 0) * Math.Max(0, ClearTarget(line) - counters.HeldOf(rows[r].Index) - rows[r].Count);
        }

        return [.. order.OrderBy(r => called is { } wcid && counters.LineAt(rows[r].Index).Wcid == wcid ? 0 : 1).ThenBy(Cost)]; // stable; M6 16 Q10: the line the trip was called for is lifted first, then the cheapest lift
    }

    /// <summary>The stock lines a home vendor sells, under their keeps, nothing bought yet.</summary>
    private static List<(int Index, GroundVendor Vendor, int Count)> RowsOf(HuntingGround ground, LogisticsCounters counters)
    {
        var rows = new List<(int Index, GroundVendor Vendor, int Count)>();
        for (var i = 0; i < counters.LineCount; i++)
        {
            var line = counters.LineAt(i);
            if (counters.IsCarried(i) || counters.IsSuspended(i) || ground.VendorSelling(line.Wcid) is not { } vendor)
            {
                continue; // M6 14 pack rules: an account formula's line is not bought while every focus is held (FociComponents)
            }

            if (counters.HeldOf(i) < line.Keep)
            {
                rows.Add((i, vendor, 0));
            }
        }

        return rows;
    }

    private static List<ResupplyBuy> ListOf(List<(int Index, GroundVendor Vendor, int Count)> rows, LogisticsCounters counters)
    {
        var list = new List<ResupplyBuy>();
        foreach (var (index, vendor, count) in rows)
        {
            if (count > 0)
            {
                list.Add(new ResupplyBuy(counters.LineAt(index), vendor, counters.HeldOf(index), count));
            }
        }

        return list;
    }

    /// <summary>M6 7 AK: the scroll weenies of the ladder's first scrolls.</summary>
    private static readonly Lazy<HashSet<uint>> FirstScrollWcids = new(() =>
        [.. LadderLoader.WarMage.FirstScrolls.SelectMany(g => ScrivenerCatalog.Default.LineSpells(g.Line).Where(s => s.Level == g.Level)).Select(s => s.ScrollWcid)]);

    /// <summary>M6 7 AU: the foci's weenies (paid from the whole purse, as the first scrolls).</summary>
    internal static readonly Lazy<HashSet<uint>> FociWcids = new(() => [.. ScrivenerCatalog.Default.Foci.Select(f => f.Wcid)]);

    /// <summary>The list with scrolls under a budget: the order of <see cref="BuyList(HuntingGround, LogisticsCounters, long, IReadOnlyList{ResupplyBuy}, List{ResupplyBuy}, IReadOnlyList{ResupplyBuy}, uint?)"/>. D116: the stop lines, then the standing shopping list, then each scroll by its own price alone, then the components of the scrolls kept, from what is left of the whole purse and in part if that is all it pays.</summary>
    private static List<ResupplyBuy> Filled(HuntingGround ground, LogisticsCounters counters, long purse, IReadOnlyList<ResupplyBuy> scrolls, List<ResupplyBuy> waiting, IReadOnlyList<ResupplyBuy> shopping, uint? called = null)
    {
        var rows = RowsOf(ground, counters);
        var budget = purse * ground.Supplies.Purse.TripSpendPercent / 100;
        var left = purse;
        var order = Enumerable.Range(0, rows.Count).OrderBy(r => counters.LineAt(rows[r].Index).StopUnder > 0 ? 0 : 1).ToList(); // stable: the profile's order within a class
        var extras = new List<ResupplyBuy>();

        void Fill(Func<StockLine, int> targetOf, bool wholePurse, bool cheapestFirst = false)
        {
            foreach (var r in cheapestFirst ? ClearOrder(ground, counters, rows, order, called) : order)
            {
                var (index, vendor, bought) = rows[r];
                var line = counters.LineAt(index);
                var want = targetOf(line) - counters.HeldOf(index) - bought;
                if (want <= 0)
                {
                    continue;
                }

                if (ground.PriceOf(line.Wcid) is { } price && price > 0)
                {
                    want = (int)Math.Min(want, (wholePurse ? left : Math.Min(budget, left)) / price);
                    left -= (long)want * price;
                    budget = Math.Max(0, budget - ((long)want * price));
                }

                rows[r] = (index, vendor, bought + want);
            }
        }

        // One buy of a component: on its stock line's row (a new row when the line stands at its keep), or a line of the trip's alone.
        void Place(uint wcid, string name, int want, int held, GroundVendor vendor, int gap)
        {
            var stock = StockIndexOf(counters, wcid);
            var row = stock < 0 ? -1 : rows.FindIndex(r => r.Index == stock);
            if (row >= 0)
            {
                rows[row] = (stock, rows[row].Vendor, rows[row].Count + gap);
            }
            else if (stock >= 0)
            {
                rows.Add((stock, vendor, gap));
            }
            else
            {
                var extra = extras.FindIndex(b => b.Line.Wcid == wcid);
                if (extra >= 0)
                {
                    extras[extra] = extras[extra] with { Count = extras[extra].Count + gap };
                }
                else
                {
                    extras.Add(new ResupplyBuy(new StockLine(wcid, name, want, 1, 1), vendor, held, gap));
                }
            }
        }

        // As many as the purse pays; the coin left and the budget reduced.
        int Afford(int gap, int price)
        {
            var buy = price > 0 ? (int)Math.Min(gap, Math.Max(0, left) / price) : gap;
            left -= (long)buy * price;
            budget = Math.Max(0, budget - ((long)buy * price));
            return buy;
        }

        Fill(line => line.StopUnder, wholePurse: false);
        foreach (var line in shopping)
        {
            var stock = StockIndexOf(counters, line.Line.Wcid);
            var row = stock < 0 ? -1 : rows.FindIndex(r => r.Index == stock);
            var buy = Afford(Math.Max(0, line.Count - (row >= 0 ? rows[row].Count : 0)), ground.PriceOf(line.Line.Wcid) ?? 0);
            if (buy > 0)
            {
                Place(line.Line.Wcid, line.Line.Name, line.Line.Keep, line.Held, line.Vendor, buy);
            }
        }

        var kept = new List<ResupplyBuy>();
        foreach (var scroll in scrolls)
        {
            var price = scroll.Vendor.Sells.FirstOrDefault(w => w.Wcid == scroll.Line.Wcid)?.Price ?? 0;
            var first = FirstScrollWcids.Value.Contains(scroll.Line.Wcid) || FociWcids.Value.Contains(scroll.Line.Wcid); // M6 7 AK: the owner's first scrolls are paid from the whole purse; M6 7 AU: so are the foci
            if (price > (first ? left : Math.Min(budget, left)))
            {
                waiting.Add(scroll); // D116: a scroll waits only for its own price
                continue;
            }

            budget = Math.Max(0, budget - price);
            left -= price;
            kept.Add(scroll);
        }

        var wanted = new Dictionary<uint, int>();
        for (var i = 0; i < kept.Count; i++)
        {
            var scroll = kept[i];
            if (scroll.Components is null)
            {
                continue;
            }

            long coin = 0;
            var note = new List<string>();
            foreach (var (c, gap) in ComponentGaps(scroll, rows, extras, wanted, counters))
            {
                var buy = Afford(gap, c.Price);
                if (buy > 0)
                {
                    Place(c.Wcid, c.Name, c.Want, c.Held, c.Vendor!, buy);
                    coin += (long)buy * c.Price;
                    note.Add(string.Create(CultureInfo.InvariantCulture, $"{c.Name} x{buy}"));
                }
            }

            foreach (var c in scroll.Components)
            {
                wanted[c.Wcid] = wanted.GetValueOrDefault(c.Wcid) + c.Want;
            }

            kept[i] = scroll with { ComponentCoin = coin, ComponentNote = string.Join(", ", note) };
        }

        Fill(line => line.StopUnder == 0 && ComponentWeenies.TryGetByWcid(line.Wcid, out _) ? Math.Min(1, line.Keep) : 0, wholePurse: true);
        Fill(line => Math.Min(line.Keep, Math.Max(line.TripUnder, line.StopUnder)), wholePurse: false);
        Fill(ClearTarget, wholePurse: false, cheapestFirst: true); // M6 14 T: every line clear of its trip line, the cheapest lift first, before any is topped up to its keep
        Fill(line => line.Keep, wholePurse: false);
        Fill(CastingComponentKeep, wholePurse: true); // M6 sprint: a casting component's line to its keep from the rest of the purse (CastingComponentKeep)
        return [.. ListOf(rows, counters), .. extras, .. kept];
    }

    /// <summary>The index of the stock line of <paramref name="wcid"/>, or -1.</summary>
    private static int StockIndexOf(LogisticsCounters counters, uint wcid)
    {
        for (var i = 0; i < counters.LineCount; i++)
        {
            if (counters.LineAt(i).Wcid == wcid)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>What <paramref name="scroll"/>'s components still lack, with the scrolls kept before it and what the list buys already counted: the pack's count (the stock line's or the plan's) and the rows' purchases against the two passes each spell wants. A component no vendor of the trip sells is not a gap: the walk cannot buy it, and the shopping list names it after the walk (D116).</summary>
    private static List<(ScrollComponent Component, int Gap)> ComponentGaps(ResupplyBuy scroll, List<(int Index, GroundVendor Vendor, int Count)> rows, List<ResupplyBuy> extras, Dictionary<uint, int> wanted, LogisticsCounters counters)
    {
        var gaps = new List<(ScrollComponent, int)>();
        foreach (var c in scroll.Components ?? [])
        {
            var stock = StockIndexOf(counters, c.Wcid);
            var row = stock < 0 ? -1 : rows.FindIndex(r => r.Index == stock);
            var have = stock >= 0 ? counters.HeldOf(stock) + (row >= 0 ? rows[row].Count : 0) : c.Held + (extras.FirstOrDefault(b => b.Line.Wcid == c.Wcid)?.Count ?? 0);
            var gap = wanted.GetValueOrDefault(c.Wcid) + c.Want - have;
            if (gap > 0 && c.Vendor is not null)
            {
                gaps.Add((c, gap));
            }
        }

        return gaps;
    }

    /// <summary>
    /// The buy list of kit lines (M5 3 F5, D53; D46: the vendor is the profile's <see cref="HuntingGround.VendorSelling"/>, no vendor or ware constant): one ware of each line a home vendor sells, in the order given. A line no vendor sells is added to
    /// <paramref name="unsold"/> and is not on the list: the caller names it and fails, never grants.
    /// </summary>
    /// <param name="ground">The fight's ground.</param>
    /// <param name="lines">The kit lines to buy.</param>
    /// <param name="unsold">Receives the lines no vendor of the profile sells.</param>
    /// <param name="held">
    /// M6 8 D (<c>m5-side-packs-use</c> closed): how many of a weenie the player holds, or null when the caller does not count (a pack line is then left to the build, which grants the side packs). With it, a pack line is bought for what is short of its count, as the container
    /// slots allow (<paramref name="containerRoom"/>): a purchase with a named reason, riding on the kit trip that happens (it calls no trip of its own).
    /// </param>
    /// <param name="containerRoom">The container slots free (<see cref="PackLayout.ContainerRoom"/>); read only with <paramref name="held"/>.</param>
    public static IReadOnlyList<ResupplyBuy> KitBuyList(HuntingGround ground, IReadOnlyList<KitLine> lines, List<KitLine> unsold, Func<uint, int>? held = null, int containerRoom = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(unsold);
        var list = new List<ResupplyBuy>();
        foreach (var line in lines)
        {
            if (line.Slot == KitSlot.Pack)
            {
                if (held is null)
                {
                    continue; // the caller does not count the packs: the build grants them (M5 7 G3)
                }

                var have = held(line.Wcid);
                var need = Math.Min(line.Count - have, containerRoom);
                if (need <= 0)
                {
                    continue; // held already, or no container slot to put one in
                }

                if (ground.VendorSelling(line.Wcid) is { } packVendor)
                {
                    list.Add(new ResupplyBuy(new StockLine(line.Wcid, line.Name, line.Count, 1, 1), packVendor, have, need));
                    containerRoom -= need;
                }
                else
                {
                    unsold.Add(line);
                }

                continue;
            }

            if (ground.VendorSelling(line.Wcid) is { } vendor)
            {
                list.Add(ResupplyBuy.ForKit(line, vendor));
            }
            else
            {
                unsold.Add(line);
            }
        }

        return list;
    }

    /// <summary>
    /// M6 8 D (<c>m5-side-packs-use</c> and <c>m5-pack-short</c>): the side packs a trip buys when the layout is short of them (<see cref="PackLayout.PacksWanted"/>), or null with <paramref name="why"/> naming the reason it buys none. The ware is the Pack
    /// (<see cref="FighterKit.SidePackWcid"/>, ItemType Container, 24 slots) at the home vendor whose <c>sells</c> lists it, at the profile's price; the count is what the layout lacks, what the container slots allow and what the purse pays (the pack
    /// is the last line of the list, so the purse left is what the other lines leave). No vendor, price or count of a ground lives here.
    /// </summary>
    /// <param name="ground">The fight's ground.</param>
    /// <param name="held">The side packs held (<see cref="PackLayout.SidePacks"/>, the foci not counted).</param>
    /// <param name="containerRoom">The container slots free once the foci the same list buys are counted.</param>
    /// <param name="purseLeft">The pyreals the rest of the list leaves.</param>
    /// <param name="why">The decision, for the log: the reason for the buy, or for none.</param>
    public static ResupplyBuy? PackBuy(HuntingGround ground, int held, int containerRoom, long purseLeft, out string why)
    {
        ArgumentNullException.ThrowIfNull(ground);
        var lacks = PackLayout.PacksWanted - held;
        if (lacks <= 0)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"packs: {held} side packs held, the layout wants {PackLayout.PacksWanted}: none to buy");
            return null;
        }

        if (ground.VendorSelling(FighterKit.SidePackWcid) is not { } vendor || ground.PriceOf(FighterKit.SidePackWcid) is not { } price)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"packs: {held} side packs held, the layout wants {PackLayout.PacksWanted}, and no vendor of {ground.Home.Town} sells a pack: the last jobs go to the main pack (TODO(m5-pack-short))");
            return null;
        }

        var count = Math.Min(lacks, containerRoom);
        if (count <= 0)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"packs: {held} side packs held, the layout wants {PackLayout.PacksWanted}, and no container slot is free (the foci and packs fill them): none bought (TODO(m5-pack-short))");
            return null;
        }

        var pays = price > 0 ? (int)Math.Min(count, Math.Max(0, purseLeft) / price) : count;
        if (pays <= 0)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"packs: {held} side packs held, the layout wants {PackLayout.PacksWanted}: the purse left, {Math.Max(0, purseLeft)} pyreals, does not pay one at {price}: none bought (TODO(m5-pack-short))");
            return null;
        }

        var name = vendor.Sells.First(w => w.Wcid == FighterKit.SidePackWcid).Name;
        why = string.Create(CultureInfo.InvariantCulture, $"packs: {held} side packs held, the layout wants {PackLayout.PacksWanted}: buys {pays} {name} at {vendor.Name} for {pays * (long)price} pyreals (price {price} each, {containerRoom} container slots free, purse left {purseLeft})");
        return new ResupplyBuy(new StockLine(FighterKit.SidePackWcid, name, PackLayout.PacksWanted, 1, 1), vendor, held, pays);
    }

    /// <summary>
    /// The loot buyer: the profile's vendor whose buy mask (<see cref="GroundVendor.BuysItemTypes"/>) takes the most of <paramref name="itemTypes"/> (ties: the profile's order), or null when none takes any (Renald for the den).
    /// </summary>
    /// <param name="ground">The fight's ground.</param>
    /// <param name="itemTypes">The kept items' ItemTypes.</param>
    public static GroundVendor? LootBuyer(HuntingGround ground, IReadOnlyList<uint> itemTypes)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(itemTypes);
        GroundVendor? best = null;
        var bestCount = 0;
        foreach (var vendor in ground.Vendors)
        {
            var count = itemTypes.Count(vendor.Buys);
            if (count > bestCount)
            {
                (best, bestCount) = (vendor, count);
            }
        }

        return best;
    }

    /// <summary>
    /// The loot buyers (M5 7 F28, D95): the vendors that between them buy the kept items, each with the number of items it takes. Greedy by the most items first (ties: the profile's order), each item sold at the first vendor chosen that buys
    /// its type; an item no vendor buys is in no count. The den's kept loot goes to Renald (armour, clothing, essences, scrolls), the Jeweler (jewellery, gems) and Archmage Cindrue (wands, mana stones, components), not to one vendor alone.
    /// </summary>
    /// <param name="ground">The fight's ground.</param>
    /// <param name="itemTypes">The kept items' ItemTypes.</param>
    public static IReadOnlyList<(GroundVendor Vendor, int Count)> LootBuyers(HuntingGround ground, IReadOnlyList<uint> itemTypes)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(itemTypes);
        var buyers = new List<(GroundVendor, int)>();
        var remaining = new List<uint>(itemTypes);
        while (remaining.Count > 0)
        {
            GroundVendor? best = null;
            var bestCount = 0;
            foreach (var vendor in ground.Vendors)
            {
                var count = remaining.Count(vendor.Buys);
                if (count > bestCount)
                {
                    (best, bestCount) = (vendor, count);
                }
            }

            if (best is null)
            {
                break;
            }

            buyers.Add((best, bestCount));
            remaining.RemoveAll(best.Buys);
        }

        return buyers;
    }

    /// <summary>
    /// The trip's stops: one per vendor of the buy list, and the loot buyer when <paramref name="sellAt"/> names him, ordered by the shortest walk from <paramref name="start"/> (the nearest stop next, each from the last; the
    /// vendors' points in world metres, <see cref="WorldMetres"/>).
    /// </summary>
    /// <param name="buys">The buy list.</param>
    /// <param name="sellAt">The loot buyer, or null when nothing is sold.</param>
    /// <param name="sellCount">The items to offer him.</param>
    /// <param name="start">Where the walk starts, in world metres.</param>
    public static IReadOnlyList<ResupplyVisit> Order(IReadOnlyList<ResupplyBuy> buys, GroundVendor? sellAt, int sellCount, Vector2 start) =>
        Order(buys, sellAt is null ? [] : [(sellAt, sellCount)], start);

    /// <summary>
    /// The trip's stops with several loot buyers (M5 7 F28, <see cref="LootBuyers"/>): one per vendor of the buy list and one per loot buyer, a vendor that is both visited once, ordered by the shortest walk from <paramref name="start"/>.
    /// </summary>
    /// <param name="buys">The buy list.</param>
    /// <param name="sellers">The loot buyers with the number of items each takes (empty when nothing is sold).</param>
    /// <param name="start">Where the walk starts, in world metres.</param>
    public static IReadOnlyList<ResupplyVisit> Order(IReadOnlyList<ResupplyBuy> buys, IReadOnlyList<(GroundVendor Vendor, int Count)> sellers, Vector2 start)
    {
        ArgumentNullException.ThrowIfNull(buys);
        ArgumentNullException.ThrowIfNull(sellers);
        var open = new List<ResupplyVisit>();
        foreach (var group in buys.GroupBy(b => b.Vendor.Guid))
        {
            var vendor = group.First().Vendor;
            var sells = sellers.Any(x => x.Vendor.Guid == vendor.Guid);
            open.Add(new ResupplyVisit(vendor, [.. group], sells, sells ? sellers.First(x => x.Vendor.Guid == vendor.Guid).Count : 0));
        }

        foreach (var (buyer, count) in sellers)
        {
            if (!open.Any(v => v.Vendor.Guid == buyer.Guid))
            {
                open.Add(new ResupplyVisit(buyer, [], true, count));
            }
        }

        var ordered = new List<ResupplyVisit>(open.Count);
        var here = start;
        while (open.Count > 0)
        {
            var next = open.MinBy(v => Vector2.Distance(here, WorldMetres(v.Vendor.Cell, v.Vendor.Point)))!;
            ordered.Add(next);
            open.Remove(next);
            here = WorldMetres(next.Vendor.Cell, next.Vendor.Point);
        }

        return ordered;
    }

    /// <summary>
    /// M5 7 P7l (D143): the walk's stops with those that stand outdoors first when the walk starts outdoors (after the recall the mage stands at the home lifestone). Live trip check 3 (2026-10-30 06:13 UTC) walked the
    /// shortest walk's order, Renald, Cindrue, Monyra, then the Scrivener of Item Magic outdoors, and the last leg had to leave Archmage Cindrue's entry hall by a shut door; it stuck there and the scroll was never bought.
    /// From the lifestone the Scrivener costs no more walking first (about 160 m, then 135 m to Renald, against Monyra to the Scrivener's 250 m last), and no learn then depends on leaving a building. The stops in
    /// buildings keep the shortest walk's order among themselves (the loot buyers' sale order is unchanged); a walk that starts indoors, or has no stop outdoors or no stop indoors, keeps the shortest walk's order.
    /// </summary>
    /// <param name="visits">The stops in the shortest walk's order (<see cref="Order(IReadOnlyList{ResupplyBuy}, IReadOnlyList{ValueTuple{GroundVendor, int}}, Vector2)"/>).</param>
    /// <param name="startCell">The cell the walk starts in.</param>
    /// <param name="why">The decision and its reason, for the station's log.</param>
    public static IReadOnlyList<ResupplyVisit> OutdoorStopsFirst(IReadOnlyList<ResupplyVisit> visits, uint startCell, out string why)
    {
        ArgumentNullException.ThrowIfNull(visits);
        var outdoor = visits.Where(v => LandDefs.IsOutdoorCell(v.Vendor.StopCell)).ToList();
        var start = string.Create(CultureInfo.InvariantCulture, $"0x{startCell:X8}");
        if (!LandDefs.IsOutdoorCell(startCell))
        {
            why = $"the walk starts indoors ({start}): the shortest walk's order";
            return visits;
        }

        if (outdoor.Count == 0 || outdoor.Count == visits.Count)
        {
            why = $"the walk starts outdoors ({start}) and {(outdoor.Count == 0 ? "no stop is" : "every stop is")} outdoors: the shortest walk's order";
            return visits;
        }

        var ordered = outdoor.Concat(visits.Where(v => !LandDefs.IsOutdoorCell(v.Vendor.StopCell))).ToList();
        why = ordered.SequenceEqual(visits)
            ? $"the walk starts outdoors ({start}) and the stops outdoors ({string.Join(", ", outdoor.Select(v => v.Vendor.Name))}) already come first: the shortest walk's order"
            : $"the walk starts outdoors ({start}): the stops outdoors ({string.Join(", ", outdoor.Select(v => v.Vendor.Name))}) first, so no buy or learn there waits on leaving a building; the stops in buildings keep the shortest walk's order";
        return ordered;
    }

    /// <summary>
    /// M6 1 L3 (live climb, 2026-11-02), the fallback since M6 4 W1: true when a walk from <paramref name="fromCell"/> to <paramref name="toCell"/> joins two rooms in two landblocks (Holtburg's
    /// healer 0xA9B40111 to the Apprentice Scrivener of Life Magic 0xA9B30102). Such a walk now plans as one composed walk (<c>JourneyLegPlanner.SourceFor</c> gives both landblocks' rooms: out of the
    /// start's room, outdoors, in to the goal's); when that walk fails, the station walks out to the home lifestone (a room to an outdoor cell), then in again, when the lifestone's landblock is within one of
    /// the goal's. Fort Tethana's lifestone (0x2780, three landblocks from Zisaha's 0x2481) is too far to be that way out; the walk from it plans since M6 4 W1 (a horizon toward the room's land point
    /// until the room's tile attaches), so the composed walk's failure is named as it is.
    /// </summary>
    /// <param name="fromCell">The player's cell.</param>
    /// <param name="toCell">The walk's goal cell.</param>
    /// <param name="lifestoneCell">The home lifestone's cell (the way out).</param>
    public static bool NeedsWayOut(uint fromCell, uint toCell, uint lifestoneCell) =>
        LandDefs.IsEnvCell(fromCell) && LandDefs.IsEnvCell(toCell) && fromCell >> 16 != toCell >> 16
        && Math.Abs((int)((lifestoneCell >> 24) & 0xFF) - (int)((toCell >> 24) & 0xFF)) <= 1 && Math.Abs((int)((lifestoneCell >> 16) & 0xFF) - (int)((toCell >> 16) & 0xFF)) <= 1;

    /// <summary>A landblock-local point as world metres on the map (the landblock's x and y bytes times its 192 m, plus the point), for the walk's order and the live-against-profile distance.</summary>
    /// <param name="cell">The full cell id.</param>
    /// <param name="point">The landblock-local point.</param>
    public static Vector2 WorldMetres(uint cell, Vector3 point) =>
        new((float)((((cell >> 24) & 0xFF) * LandDefs.BlockLength) + point.X), (float)((((cell >> 16) & 0xFF) * LandDefs.BlockLength) + point.Y));
}

/// <summary>
/// The resupply station (M5 item 3 m3; DoD 6, D38, D47, D48, D49): from the ground's home lifestone, in peace, the mage walks to each home vendor that sells a stock line it holds under its keep, in the order of the shortest walk,
/// sells the loot at the loot buyer when looting is on, changes a trade note to coin when the coin is short of the next buy (D49, paid at 1.0) and buys back to the keep counts out of the purse, never a grant. Every walk goes through the
/// travel seam as <see cref="TravelPurpose.Vendor"/> with its purpose logged; no place. The vendor is met by the profile's guid in the object table, and his live cell and point are logged beside the profile's
/// (<c>m6-ground-live-facts</c>). The ground is the fight's (<see cref="JourneyContext.Dungeon"/>, D46). The mage's plan does not run it yet (the logistics station of slice c2 calls it).
/// </summary>
/// <param name="options">Looting and the kept list.</param>
public sealed class ResupplyStation(ResupplyOptions? options = null) : IStation
{
    /// <inheritdoc />
    public string Name => "resupply";

    /// <inheritdoc />
    /// <remarks>At the home (checked by the run against the ground's lifestone landblock, by design: <see cref="StationWhere"/> names the fixed Holtburg and the den, never a ground's own home, which is a profile fact (D26); closes <c>m5-station-where-home</c>, M6 6 MG), in peace.</remarks>
    public StationNeeds Needs { get; } = new(Peace: true);

    /// <summary>The options the station was built with.</summary>
    public ResupplyOptions Options { get; } = options ?? new ResupplyOptions();

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context) => new ResupplyRun(context, Options);
}

/// <summary>The resupply station's run (see <see cref="ResupplyStation"/>).</summary>
public sealed partial class ResupplyRun : ShopLeg
{
    /// <summary>
    /// ACE's PropertyBool IsSellable (69; ACE.Entity/Enum/Properties/PropertyBool.cs:95 @95ef426d), read from an appraisal's bools when one carries it. Absent means sellable (<c>WorldObject_Properties.cs</c> 1460-1463); the sale check is
    /// <c>Player_Commerce.cs</c> 268-273: an item type outside the vendor's mask, a false IsSellable or a Retained item is refused with "The {item} is unsellable." (271), and a Value under 1 with "has no value and cannot be sold" (275-280).
    /// </summary>
    public const uint IsSellableBool = 69;

    /// <summary>The ItemType of a scroll (Writable, 0x2000): a bought scroll the ground's learn list names is read after the walk (M5 7 P7j, <see cref="ReadScrolls"/>); any other stays a <c>TODO(m6-scroll-read)</c>.</summary>
    public const uint WritableType = 0x2000;

    /// <summary>Where ACE walks an out-of-range player to the thing it uses (<c>HandleActionUseItem</c> <c>Player_Use.cs</c> 205 runs <c>CreateMoveToChain</c>, which sends <c>MoveToObject</c>, <c>Player_Move.cs</c> 92, @95ef426d).</summary>
    private const string AceMoveTo = "Player_Use.cs:205 CreateMoveToChain, Player_Move.cs:92 MoveToObject @95ef426d";

    /// <summary>The pyreals one coin stack holds at most (ACE's Pyreal MaxStackSize, GearKit's catalogue): a payout needs one free slot per 25,000 (Player_Commerce.cs 176-191, 194, 212-219 @95ef426d).</summary>
    public const int CoinStackMax = 25_000;

    /// <summary>The extra rounds the walk makes to sell what the free slots held back, each after the coin was turned into notes.</summary>
    public const int MaxSaleRounds = 4;

    private readonly ResupplyOptions _options;
    private readonly SaleMemory _memory;
    private readonly List<string> _refusals = [];
    private IReadOnlyList<ResupplyBuy> _buyList = [];
    private IReadOnlyList<ResupplyVisit> _visits = [];
    private readonly HashSet<uint> _refusedThisTrip = [];
    private bool _slotLimited;
    private readonly HashSet<ResupplyBuy> _floorBuys = [];

    internal ResupplyRun(JourneyContext context, ResupplyOptions options)
        : base("resupply", context)
    {
        _options = options;
        _memory = options.Memory ?? new SaleMemory();
    }

    /// <summary>The buy list the run made (empty before the count).</summary>
    public IReadOnlyList<ResupplyBuy> BuyList => _buyList;

    /// <summary>True once the run walked off the lifestone before its first walk (<c>m5-logistics-step-off</c>, 3-R19).</summary>
    public bool SteppedOff { get; private set; }

    /// <summary>The trip's stops in the walk's order.</summary>
    public IReadOnlyList<ResupplyVisit> Visits => _visits;

    /// <summary>The items bought.</summary>
    public long Bought { get; private set; }

    /// <summary>The pyreals paid.</summary>
    public long Spent { get; private set; }

    /// <summary>The trade notes changed to coin.</summary>
    public int NotesSold { get; private set; }

    /// <summary>M5 7 G1: the trade notes bought with the coin above the purse's coin.</summary>
    public int NotesBought { get; private set; }

    /// <summary>M5 7 G1: the items dropped after two trips' refusals.</summary>
    public int DroppedItems { get; private set; }

    /// <summary>The loot items that left the pack for the loot buyer.</summary>
    public int Sold { get; private set; }

    /// <summary>The pyreals the loot sale paid.</summary>
    public long Earned { get; private set; }

    /// <summary>The vendors met and opened.</summary>
    public int Vendors { get; private set; }

    /// <summary>The vendors not met in the object table within the station's time.</summary>
    public int NotFound { get; private set; }

    /// <summary>The vendors whose walk the physics refused (the stop is skipped and the trip goes on: TODO(m5-vendor-unreachable)).</summary>
    public int VendorsSkipped { get; private set; }

    private string? _unreachable;

    /// <summary>The lines the purse could not pay in full.</summary>
    public int Short { get; private set; }

    /// <summary>ACE's refusals and the station's own (coin, pack, stock, unsellable, open, other), one per refusal.</summary>
    public IReadOnlyList<string> Refusals => _refusals;

    /// <inheritdoc />
    /// <remarks>Report-only (a new judge's first gate).</remarks>
    public override string VerdictFields() => string.Create(
        CultureInfo.InvariantCulture,
        $"bought={Bought} spent={Spent} notes_sold={NotesSold} sold={Sold} earned={Earned} vendors={Vendors} refused={_refusals.Count} refusals={(_refusals.Count == 0 ? "none" : string.Join('+', _refusals))} short={Short} not_found={NotFound} loot={(_options.Loot ? "on" : "off")} problems={Problems.Count} granted={Granted} notes_bought={NotesBought} dropped={DroppedItems} vendors_skipped={VendorsSkipped} junk_dropped={JunkDropped} components_offered={ComponentsOffered}")
        + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

    /// <inheritdoc />
    protected override IEnumerable<ActionHandle?> Script()
    {
        var ground = Context.Dungeon;
        if (_options.Replace)
        {
            foreach (var h in ReplaceScript(ground)) { yield return h; } // M6 7 AH: the death's replace step, the floor's missing pieces only

            yield break;
        }

        if (_options.TownShop is { } townOrders)
        {
            foreach (var h in TownShopScript(ground, townOrders)) { yield return h; } // M6 14 T: the buys of a stop in another town, on the way out

            yield break;
        }

        SaleDesk.For(S.State).StateIfNone(Context.Character, Context.Spells, Context.Facts); // M6 16 E5c2: a trip run with no job states who she is and the SpellTable for the rule book's verdict outside its own sale
        var counters = TripDesk.For(S.State).On(ground); // M6 16 E5d: the session's one set of counters: the town trip states no floor-only line and reads what stands
        if (_options.Kit is null)
        {
            Stage = "count";
            foreach (var h in Until(() => counters.Check(S.State).Kind != LogisticsKind.Unread, NpcSeconds)) { yield return h; }

            var verdict = counters.Check(S.State);
            if (verdict.Kind == LogisticsKind.Unread)
            {
                Fail($"stock unread: {verdict.Cause} after {NpcSeconds:0} s, so there is no buy list");
                yield break;
            }

            if (TripDesk.For(S.State).InHand is { } order)
            {
                // M6 16 E5d (check, act, check): the trip carries the verdict the trigger called it on. The count above is the check at home: what the visit acts on. Where it differs from what the trip was called for
                // (the pack and the purse changed on the way home, the experience was spent) each difference is said here, never recomputed in silence.
                Log(order.AtHome(counters, verdict, S.State));
            }

            foreach (var wcid in counters.UnstockedNeeds())
            {
                if (ground.TownStockOf(wcid) is { } town)
                {
                    // M6 item 14 (m6-goldscarab): no gap: the ware is the town stock's, bought on the way out; the home walk has nothing to buy
                    Log(Text($"resupply: the buff passes found the pack short of {ComponentNeeds.Shared.NameOf(wcid)} {wcid}: no stock line of the home walk carries it; the town stock buys it at {town.Vendor.Name} in {town.Town} on the way out (to {town.Keep} when under {town.TripUnder})"));
                    continue;
                }

                Log(Text($"resupply: TODO(m6-component-unstocked): the buff passes found the pack short of {ComponentNeeds.Shared.NameOf(wcid)} {wcid}, and no stock line of {ground.Key} carries it: this trip cannot buy it"));
            }

            MeasurePackUse(ground); // M6 14 pack room (m6-pack-room): what she used and the loot since the last visit, read before anything is sold or bought
        }

        // M6 1 L2 (live, 2026-11-02): the home is the town, not the lifestone's landblock alone: a start in a home vendor's landblock is at home too (Fort Tethana's lifestone, 0x2780,
        // is two landblocks from its shops in 0x2581 and 0x2481; the scrivener loopback starts by the Grand Master, D40). The walk from the lifestone to their rooms plans since M6 4 W1 (a horizon toward the room's land point until its tile attaches).
        if (S.State.Self.Position is not { } here || (here.CellId >> 16 != ground.Home.LifestoneCell >> 16 && !ground.Vendors.Any(v => v.Cell >> 16 == here.CellId >> 16)))
        {
            Fail(string.Create(CultureInfo.InvariantCulture, $"not at the home: the player stands in 0x{S.State.Self.Position?.CellId ?? 0:X8}, neither the landblock of the home lifestone of {ground.Home.Town} (0x{ground.Home.LifestoneCell:X8}) nor a home vendor's"));
            yield break;
        }

        if (_options.Kit is { } kitLines)
        {
            var unsold = new List<KitLine>();
            _buyList = ResupplyPlanner.KitBuyList(ground, kitLines, unsold, wcid => GearInventory.CountOf(S.State, [], wcid), PackLayout.ContainerRoom(S.State)); // M6 8 D: a pack line short of its count is bought, named below
            foreach (var pack in _buyList.Where(b => b.Line.Wcid == FighterKit.SidePackWcid))
            {
                Log(Text($"packs: a kit line asks for {pack.Line.Keep} {pack.Line.Name} and {pack.Held} are held: buys {pack.Count} at {pack.Vendor.Name}"));
            }
            if (unsold.Count > 0)
            {
                Fail(Text($"no vendor of {ground.Home.Town} sells the kit line {string.Join(", ", unsold.Select(k => $"{k.Name} (weenie {k.Wcid})"))}: the profile lists none, and nothing is granted (D51)"));
                yield break;
            }

            Log(Text($"resupply: a kit trip (D53): {kitLines.Count} line(s), the stock is not counted and nothing is sold"));
        }
        if (_options.Kit is not null)
        {
            LogBuyList();
        }

        if (_options.Kit is null && _options.Loot && KeepAsideRule.For(ground, Context.Character) is { } keepAside) // M6 14 (m6-lore-cantrips): the profile's block, else the climber's default
        {
            foreach (var h in AppraiseForKeepAside(keepAside)) { yield return h; }
        }

        if (_options.Gear && _options.Kit is null)
        {
            foreach (var h in GearPhase(ground)) { yield return h; } // M5 7 D121, D122, D125: before the sale, so the main pack holds the loot to sell alone
        }

        if (_options.Gear && _options.Kit is null && !PlanFloor(ground))
        {
            yield break; // M6 2 b: the gear floor rides on the gear phase (a loot trip, slice e turns the climber's loot on); the loud no-caster stop: no caster is held and none can be bought
        }

        var kept = _options.Kit is null ? SaleItems(ground) : [];
        if (_options.Kit is null)
        {
            PlanPackBudget(ground, counters, kept); // M6 14 pack room (m6-pack-room): the burden budget from her live capacity, before the sale walk
            foreach (var h in SurplusDrinks(kept)) { yield return h; } // the drinks over the cut keeps join the sale (amends m6-sale-keeps-drinks)
        }

        var sellers = kept.Count == 0 ? [] : ResupplyPlanner.LootBuyers(ground, [.. kept.Select(k => k.Object.ItemType)]);
        if (_options.Loot && kept.Count > 0)
        {
            Log(sellers.Count == 0
                ? Text($"sale: no vendor of {ground.Home.Town} buys any of the {kept.Count} pack items: nothing is sold")
                : Text($"sale: {kept.Count} pack items for the loot buyers {string.Join(", ", sellers.Select(b => $"{b.Vendor.Name} ({b.Count}, mask 0x{b.Vendor.BuysItemTypes:X8})"))}"));
        }

        // D123 (the owner: sell first, then buy): with loot to sell, the stock is planned after the sale, from the coin the sale brought in (long4's trip 3 planned on 729 coin, sold 20,646, bought 321 and walked again 14 s later for the
        // ten scrolls it had put off). The sale stops are walked first; the buy list then rests on the purse read after the last sale.
        var sellFirst = _options.Kit is null && sellers.Count > 0;
        if (_options.Kit is null && !sellFirst)
        {
            PlanStock(ground, counters);
            PlanLeather(ground); // M6 item 14: the leather set, last: only what the purse pays over the stock and the scrolls
            LogBuyList();
        }
        else if (sellFirst)
        {
            Log(Text($"resupply: sell first (D123): the stock is planned after the sale, from the purse it leaves (now {FloorPurse()}); the floor's {_buyList.Count} line(s) wait for it too"));
        }

        _visits = ResupplyPlanner.Order(sellFirst ? [] : _buyList, sellers, ResupplyPlanner.WorldMetres(here.CellId, here.Origin));
        if (_visits.Count > 1)
        {
            _visits = ResupplyPlanner.OutdoorStopsFirst(_visits, here.CellId, out var orderWhy);
            Log("resupply: the walk's order: " + orderWhy); // M5 7 P7l (D143)
        }

        if (_visits.Count == 0 && NotesDue(ground) == 0 && _toRead.Count == 0)
        {
            if (_options.Kit is null)
            {
                Log(PurseDecision(ground)); // M5 7 D136 follow-up: the purse's decision every trip, the reason when it buys none
            }

            Log("resupply: nothing to buy or sell: no walk");
            if (_options.Kit is null)
            {
                PackVisited(ground); // M6 14 pack room: a visit, even one with nothing to do: the measure starts again and the loot record is cleared
            }

            if (_options.Gear && _options.Kit is null)
            {
                // M6 2 f (2-R2): a loot trip with nothing to buy or sell still wears what it holds and gives back what it can (the gear phase's pieces the judge held back, a piece unread before)
                foreach (var h in EquipPass(ground)) { yield return h; }
                foreach (var h in ReturnPass(ground)) { yield return h; }
            }

            NoCasterStop("the trip, nothing to buy");
            Stage = "done";
            yield break;
        }

        if (_visits.Count > 0)
        {
            Log("resupply: the walk " + string.Join(" -> ", _visits.Select(v => v.Vendor.Name)));
        }

        // m5-logistics-step-off (3-R19): after Lifestone Recall the player stands inside the lifestone's cylinder, which the client does not collide with (TODO(d25-server-object-host)), and the first walk would start from there. As the recall-home
        // station does before it ends, the player first walks to the ground's known lifestone point (the home profile's measure) when a lifestone object is within its stand-off.
        if (S.State.Self.Position is { } standing && S.State.Objects.NearestWithFlags(WorldObjectTable.LifestoneFlag, in standing, RecallHomeStation.OnLifestoneMetres, out var lifestone))
        {
            Stage = "step-off";
            SteppedOff = true;
            Log(Text($"resupply: stands in lifestone {lifestone:X8} (within {RecallHomeStation.OnLifestoneMetres} m): stepping off it to the lifestone point before the first walk (m5-logistics-step-off)"));
            foreach (var h in TravelTo(new TravelRequest(Destination("home-lifestone-point", ground.Home.LifestoneCell, ground.Home.LifestonePoint), TravelPurpose.Bind), "off the lifestone")) { yield return h; }
        }

        // M5 7 G1 (edge audit 1, 12b): a pack nearly full of coin stacks cannot take a payout (ACE refuses the whole sale), so the coin is turned into notes first. M6 14 pack rules: before every sale, the coin over the float (CoinFloat)
        if (_options.Kit is null && _options.Loot && kept.Count > 0 && NotesDue(ground) > 0)
        {
            foreach (var h in ConvertCoin(ground)) { yield return h; }
        }

        foreach (var visit in _visits)
        {
            foreach (var h in VisitVendor(ground, visit, kept)) { yield return h; }
        }

        if (sellFirst)
        {
            // D123, check, act, check: the purse read after the last sale (check), the stock planned from it once (act), the plan's lines logged with the coin they rest on (check). The buy stops start where the sale left her;
            // a buy at the vendor she stands by is the nearest stop, so it costs no walk.
            Log(Text($"resupply: sold first (D123): sold {Sold} item(s) for {Earned}; the purse now {FloorPurse()}: the stock is planned from it"));
            PlanStock(ground, counters);
            PlanLeather(ground); // M6 item 14: the leather set, last: only what the purse pays over the stock and the scrolls
            LogBuyList();
            var at = S.State.Self.Position ?? here;
            var buys = ResupplyPlanner.Order(_buyList, [], ResupplyPlanner.WorldMetres(at.CellId, at.Origin));
            if (buys.Count > 1)
            {
                buys = ResupplyPlanner.OutdoorStopsFirst(buys, at.CellId, out var buyOrderWhy);
                Log("resupply: the buy walk's order: " + buyOrderWhy);
            }

            Log(buys.Count == 0 ? "resupply: sold first (D123): nothing to buy after the sale" : "resupply: the buy walk " + string.Join(" -> ", buys.Select(v => v.Vendor.Name)));
            _visits = [.. _visits, .. buys];
            foreach (var visit in buys)
            {
                foreach (var h in VisitVendor(ground, visit, kept)) { yield return h; }
            }
        }

        if (_options.Kit is null)
        {
            foreach (var h in BuyScrollKits(ground)) { yield return h; } // D116: the components after the scroll are a required step of this trip, check, act, check
        }

        if (_options.Kit is null && _toRead.Count > 0)
        {
            foreach (var h in ReadScrolls()) { yield return h; } // M5 7 P7j (D143): the scrolls bought on the walk are read, each proved by the spellbook
        }

        if (_options.Kit is null)
        {
            foreach (var h in ConvertCoin(ground)) { yield return h; }
        }

        // The sale a vendor's payout could not fit (free slots, 12b) goes on in rounds: the notes made room, so the items that waited are offered again.
        for (var round = 1; round <= MaxSaleRounds && _slotLimited && _options.Kit is null; round++)
        {
            _slotLimited = false;
            var again = SaleItems(ground);
            if (again.Count == 0 || S.State.Self.Position is not { } now)
            {
                break;
            }

            var buyers = ResupplyPlanner.LootBuyers(ground, [.. again.Select(k => k.Object.ItemType)]);
            var soldBefore = Sold;
            Log(Text($"sale: round {round + 1}: {again.Count} items waited for a free slot"));
            foreach (var visit in ResupplyPlanner.Order([], buyers, ResupplyPlanner.WorldMetres(now.CellId, now.Origin)))
            {
                foreach (var h in VisitVendor(ground, visit, again)) { yield return h; }
            }

            foreach (var h in ConvertCoin(ground)) { yield return h; }

            if (Sold == soldBefore)
            {
                Log(Text($"sale: round {round + 1} sold nothing: the rest waits for the next trip"));
                break;
            }
        }

        foreach (var h in DropJunk(ground)) { yield return h; } // M6 14 pack rules: the value-0 junk and the tool no Holtburg buyer takes, dropped at the home with check, act, check

        if (_options.Kit is null && (_options.Gear || _floorBuys.Count > 0))
        {
            foreach (var h in EquipPass(ground)) { yield return h; } // M6 2 b: the floor's caster and robe and every better piece held, worn with check, act, check (M5 D141)
            foreach (var h in ReturnPass(ground)) { yield return h; } // M6 2 d: an attuned piece the pass did not wear, outgrown or blocked, given back to its NPC (GearReturn), with check, act, check
        }

        if (_options.Gear && _options.Kit is null)
        {
            foreach (var h in SortPacks(ground, "after the buys")) { yield return h; } // M5 7 D121: the bought components and kit to their packs (ACE creates a bought ware in the main pack, Player_Commerce.cs 59-102)
            Log("resupply: gear: " + GearFields());
        }

        if (_options.Kit is null)
        {
            NoCasterStop("the trip");
        }

        if (LearnsOn(ground) && _options.Kit is null)
        {
            Log("resupply: learn: " + LearnFields);
        }

        if (_options.Kit is null)
        {
            PackVisited(ground); // M6 14 pack room (m6-pack-room): the next measure's departure; the full-pack line waits for the next item the loot leaves
        }

        Log("resupply: done: " + VerdictFields());
    }

    /// <summary>One vendor of the walk: met, opened, the loot sold in batches, the lines bought.</summary>
    /// <summary>
    /// The stock's buy list from the purse held now (D123: after the sale when there is one): the scrolls the plan wants, the stock lines within the ground's trip budget, a side pack, the scroll's components; the gear floor's lines,
    /// planned before, stay first. A scroll the budget cannot pay waits (<see cref="WaitLearn"/>). Called once a trip.
    /// </summary>
    private void PlanStock(HuntingGround ground, LogisticsCounters counters)
    {
        var floor = _buyList.Where(_floorBuys.Contains).ToList();
        CutPackKeeps(counters); // M6 14 pack room (m6-pack-room): the stock is bought to the budget's keeps
        var scrolls = LearnsOn(ground) ? PlanLearns(ground) : []; // M6 1 c: the ground's list or the plan's lines at the home town's scriveners; M5 7 P7j (D143): a scroll the spellbook needs joins the walk at its vendor
        var waiting = new List<ResupplyBuy>();
        var stockList = ResupplyPlanner.BuyList(ground, counters, FloorPurse(), scrolls, waiting, ShoppingBuys(ground), TripDesk.For(S.State).InHand?.Verdict.Line?.Wcid); // M6 16 Q10 (engine-trip-order-acts): the line the trip was called for is lifted clear first; // M6 7 V: within the ground's trip budget; M6 7 X: the scrolls inside it, in the plan's order
        counters.UncutKeeps(); // M6 16 E5d: the cut keeps were this visit's buy list's alone: the session's counters are the fight's too, which keeps the profile's
        _buyList = [.. floor, .. HoldUnseen([.. stockList, .. PackBuys(ground, stockList)])]; // M6 7 AX: a buy that has not shown is not bought twice; M6 8 D: a side pack rides on this trip when the layout is short
        foreach (var scroll in waiting)
        {
            WaitLearn(ground, scroll, counters);
        }

        AfterScrollPlan(ground); // the owner's rule: components with every scroll (D115, its gate taken out by D116)

        for (var i = 0; i < counters.LineCount; i++)
        {
            var line = counters.LineAt(i);
            if (counters.IsCarried(i))
            {
                Log(Text($"resupply: {line.Name} is carried (held {counters.HeldOf(i)}, stop under {line.StopUnder}): no home vendor sells it, never bought here; its stop is the counters'"));
            }
        }
    }

    private void LogBuyList()
    {
        foreach (var b in _buyList)
        {
            Log(Text($"resupply: {b.Line.Name} held {b.Held} keep {b.Line.Keep}: buy {b.Count} at {b.Vendor.Name}"));
        }
    }

    private IEnumerable<ActionHandle?> VisitVendor(HuntingGround ground, ResupplyVisit visit, List<(uint Guid, WorldObject Object)> kept)
    {
        Stage = "vendor:" + visit.Vendor.Name;
        Log(visit.WalkLine);
        uint guid = 0;
        foreach (var h in Meet(visit.Vendor, g => guid = g)) { yield return h; }

        if (guid == 0 && _unreachable is { } first && LeatherWalkAgain(visit, first))
        {
            // M6 item 14 (live 2026-11-12 01:02 UTC): a body in the lane to the leather set's vendor: one more walk after a short wait, on this trip; named either way.
            Log(Text($"vendor: {visit.Vendor.Name} could not be reached ({first}): a body stands in the lane (npc-in-lane), which clears by itself: waiting {LaneWaitSeconds:0} s, then one more walk on this trip (recovery=leather-walk-again)"));
            foreach (var h in Until(() => false, LaneWaitSeconds)) { yield return h; }

            foreach (var h in Meet(visit.Vendor, g => guid = g)) { yield return h; }

            if (guid == 0 && _unreachable is { } second)
            {
                _ = LeatherWalkAgain(visit, second); // the second failure is the one named
            }
        }

        if (guid == 0 && _unreachable is { } why)
        {
            VendorsSkipped++;
            Log(Text($"vendor: {visit.Vendor.Name} could not be reached ({why}); skipped: {(visit.Sell ? kept.Count : 0)} items wait to be sold, {visit.Buys.Count} lines are not bought here this trip (TODO(m5-vendor-unreachable))"));
            yield break;
        }

        if (guid == 0)
        {
            NotFound++;
            Log(Text($"vendor: {visit.Vendor.Name} 0x{visit.Vendor.Guid:X8} not found in reach within {NpcSeconds:0} s of the walk: {visit.Buys.Count} lines not bought{(visit.Sell ? ", nothing sold" : string.Empty)}; on to the next vendor"));
            yield break;
        }

        var open = S.Actions.OpenVendor(guid, ActionSource.Automation);
        yield return open;
        if (open.Outcome is not { Kind: ActionOutcomeKind.Confirmed } || S.State.Vendor is not { } opened || opened.Vendor != guid)
        {
            Refuse("open", Text($"{visit.Vendor.Name} did not open: {Describe(open)}"));
            yield break;
        }

        Vendors++;
        if (visit.Sell)
        {
            foreach (var h in SellLoot(ground, visit.Vendor, guid, kept)) { yield return h; }
        }

        foreach (var buy in visit.Buys)
        {
            foreach (var h in _floorBuys.Contains(buy) ? FloorBuy(ground, visit.Vendor, guid, buy) : BuyLine(ground, visit.Vendor, guid, buy)) { yield return h; }
        }
    }

    /// <summary>
    /// M5 7 G1 (edge audit item 1): the trade notes the coin above the purse's coin pays for: the largest note of the purse a profile vendor sells (the den's: Archmage Cindrue's 250,000), as many as the coin and the free slots allow. ACE
    /// sells a promissory note at 1.15 times its face (<c>Vendor.cs</c> 577-585 @95ef426d) and never merges coin stacks, so without this a day's sales fill the pack with 25,000-pyreal stacks.
    /// </summary>
    private int NotesDue(HuntingGround ground)
    {
        // M5 7 D136: the bought notes come as one stack (up to 250) and join the held stack, so one free slot is enough (the buy's own cap is NoteRoom)
        if (_options.Kit is not null || PackView.MainPackFree(S.State) <= 0)
        {
            return 0;
        }

        var coin = Coins();
        var picks = PurseNotes.Greedy(coin - PurseFloor(ground), NoteOffers(ground)).Picks;
        return Context.Facts.CoinToNotes || NotesSaveASlot(coin, picks) ? picks.Sum(p => p.Count) : 0; // M6 14 pack rules: a change that frees no slot costs 15% for nothing
    }

    /// <summary>
    /// The coin the purse keeps as coin: the ground's (<c>purse.coin</c>), or 0 while an order turns the coin into notes (<see cref="JourneyFacts.CoinToNotes"/>, M6 item 14: the den run's trip; what is left is under the smallest
    /// purse note's price).
    /// </summary>
    /// <remarks>M6 14 pack rules: the ground's coin is the float (<see cref="CoinFloat"/>): the trip's own spend rounded up to whole coin stacks, no longer the profile's <c>purse.coin</c> (a restock from nothing, which kept three stacks when a trip spends about one).</remarks>
    private long PurseFloor(HuntingGround ground) => Context.Facts.CoinToNotes ? 0 : FloatOf(ground, out _);

    private static PurseNote? NoteToBuy(HuntingGround ground) =>
        ground.Supplies.Purse.Notes.Where(n => ground.VendorSelling(n.Wcid) is not null).OrderByDescending(n => n.Value).FirstOrDefault();

    /// <summary>
    /// M5 7 D136 follow-up: the notes asked of the vendor of the purse's largest note: all ten (<see cref="PurseNotes.All"/>; Archmage Cindrue lists them all: her CreateList, ACE-World-Database v0.9.296 weenie_properties_create_list
    /// rows 21060-21069), at ACE's note price (1.15 times the face, <c>Vendor.cs</c> 577-585), of a face at least the purse's <see cref="GroundPurse.MinNote"/> (25,000 for the den: a smaller note saves no pack slot and costs 15%;
    /// ACE has no 25,000 note, so the 50,000 to the 250,000). None when no profile vendor sells a purse note. At the vendor her 0062 decides: a note she does not list is skipped, logged.
    /// </summary>
    private static List<PurseNotes.Offer> NoteOffers(HuntingGround ground) =>
        NoteToBuy(ground) is null ? [] : [.. PurseNotes.All.Where(n => n.Value >= ground.Supplies.Purse.MinNote).Select(n => new PurseNotes.Offer(n, PurseNotes.PriceOf(n.Value)))];

    /// <summary>M5 7 D136 follow-up: the purse's decision this trip, in one line: the coin held, the floor, and the notes the coin above it buys by denomination with their price, or why none.</summary>
    private string PurseDecision(HuntingGround ground)
    {
        var coin = Coins();
        var floor = PurseFloor(ground);
        var floatWhy = "the order turns the coin into notes";
        if (!Context.Facts.CoinToNotes)
        {
            _ = FloatOf(ground, out floatWhy);
        }

        var minNote = ground.Supplies.Purse.MinNote;
        var head = Text($"purse: coin {coin}, floor {floor} ({floatWhy}), above {Math.Max(0, coin - floor)}, notes of {minNote} and up (a smaller note saves no pack slot and costs 15%): ");
        if (NoteToBuy(ground) is null)
        {
            return head + Text($"no notes: no vendor of {ground.Home.Town} sells a note of the purse");
        }

        var offers = NoteOffers(ground);
        if (offers.Count == 0)
        {
            return head + Text($"no notes: no trade note is of {minNote} or more");
        }

        var (picks, left) = PurseNotes.Greedy(coin - floor, offers);
        var smallest = offers.MinBy(o => o.Price);
        if (picks.Count == 0)
        {
            return head + Text($"no notes: the coin above the floor is under the smallest such note's price ({smallest.Note.Name} at {smallest.Price}); it stays coin");
        }

        if (!Context.Facts.CoinToNotes && !NotesSaveASlot(coin, picks))
        {
            return head + Text($"no notes: {string.Join(", ", picks.Select(p => Text($"{p.Count} x {p.Note.Name}")))} would leave the coin in as many slots or more, a note stack counted ({CoinFloat.Stacks(coin)} coin stacks now): a note costs 15% and frees no slot; it stays coin");
        }

        if (PackView.MainPackFree(S.State) <= 0)
        {
            return head + "no notes: the main pack has no free slot for a note stack";
        }

        return head + "buy " + string.Join(", ", picks.Select(p => Text($"{p.Count} x {p.Note.Name} at {p.Price}"))) + Text($"; {left} stays coin (under {smallest.Price}, the {smallest.Note.Name}'s price)");
    }

    private IEnumerable<ActionHandle?> ConvertCoin(HuntingGround ground)
    {
        Log(PurseDecision(ground));
        var due = NotesDue(ground);
        if (due == 0 || NoteToBuy(ground) is not { } largest || ground.VendorSelling(largest.Wcid) is not { } vendor)
        {
            yield break;
        }

        Stage = "notes:" + vendor.Name;
        Log(Text($"walking: purpose=vendor:{vendor.Name} (coin to notes: {Coins()} pyreals, keep {PurseFloor(ground)}, {due} notes)"));
        uint guid = 0;
        foreach (var h in Meet(vendor, g => guid = g)) { yield return h; }

        if (guid == 0 && _unreachable is { } why)
        {
            VendorsSkipped++;
            Log(Text($"vendor: {vendor.Name} could not be reached ({why}); skipped: the coin stays coin until the next trip (TODO(m5-vendor-unreachable))"));
            yield break;
        }

        if (guid == 0)
        {
            Log(Text($"purse: {vendor.Name} not found: the coin stays coin until the next trip"));
            yield break;
        }

        var open = S.Actions.OpenVendor(guid, ActionSource.Automation);
        yield return open;
        if (open.Outcome is not { Kind: ActionOutcomeKind.Confirmed } || S.State.Vendor is not { } stock || stock.Vendor != guid)
        {
            Refuse("open", Text($"{vendor.Name} did not open for the notes: {Describe(open)}"));
            yield break;
        }

        // M5 7 D136 follow-up: greedy over the live 0062, largest note first, each denomination bought as one row and merged into its own stack before the next, the coin re-read after each buy
        var floor = PurseFloor(ground);
        var scratch = new List<uint>();
        foreach (var offer in NoteOffers(ground).OrderByDescending(o => o.Note.Value))
        {
            var note = offer.Note;
            if (S.State.Vendor is not { } live || live.Vendor != guid)
            {
                Refuse("open", Text($"{vendor.Name}'s stock is no longer open for the notes"));
                break;
            }

            var entry = live.Entries.FirstOrDefault(e => e.WeenieClassId == note.Wcid);
            if (entry is null)
            {
                Log(Text($"purse: {note.Name} ({note.Wcid}) is not in {vendor.Name}'s 0062 ({live.Entries.Length} rows): skipped"));
                continue;
            }

            var wirePrice = (long)live.PriceToBuy(entry, 1);
            var above = Coins() - floor;
            if (wirePrice <= 0 || above < wirePrice)
            {
                continue;
            }

            if (PackView.MainPackFree(S.State) <= 0)
            {
                Log(Text($"purse: the main pack has no free slot for the {note.Name} stack: {above} above the floor stays coin"));
                break;
            }

            var count = (int)Math.Min(above / wirePrice, NoteRoom(entry));
            var before = Coins();
            var heldBefore = GearInventory.CountOf(S.State, scratch, note.Wcid);
            var buy = S.Actions.Buy(guid, [(entry.Guid, (uint)count)], ActionSource.Automation);
            yield return buy;
            if (buy.Outcome is not { Kind: ActionOutcomeKind.Confirmed })
            {
                Refuse(Kind(buy.Outcome?.Detail, "other"), Text($"{count} x {note.Name} at {vendor.Name}: {Describe(buy)}"));
                break;
            }

            NotesBought += count;
            Log(Text($"purse: bought {count} x {note.Name} at {vendor.Name} for {Math.Max(0, before - Coins())} pyreals ({wirePrice} each; the coin {before} -> {Coins()}, the floor {floor})"));
            foreach (var h in MergeBought(note.Wcid, heldBefore + count, note.Name, mainPackTarget: true)) { yield return h; }

            LogNoteStacks(note);
        }

        foreach (var h in MergeCoin()) { yield return h; }

        Log(Text($"purse: done at {vendor.Name}: the coin {Coins()}, {Math.Max(0, Coins() - floor)} above the floor {floor}"));
    }

    /// <summary>
    /// M5 7 D136: how many notes one buy may take. ACE makes one stack of up to the weenie's MaxStackSize per bought row (<c>Vendor.cs</c> 392-424 <c>ItemProfileToWorldObjects</c>) and asks one free slot a stack
    /// (<c>ItemsToReceive.cs</c> 78-110), and every trade note's MaxStackSize is 250 (ACE-World-Database v0.9.296, wcids 2621-2627 and 20628-20630, PropertyInt 11), so each free main-pack slot holds that many. A row whose 0062 names
    /// no MaxStackSize counts one a slot, as before the notes stacked.
    /// </summary>
    private int NoteRoom(VendorStockEntry entry) => (int)Math.Min(int.MaxValue, (long)Math.Max(0, PackView.MainPackFree(S.State)) * entry.MaxStackSize);

    /// <summary>M5 7 D136: one line of the notes held after a buy: each stack in the main pack with its size, so a live run shows the notes in one stack (<c>TODO(m5-note-merge)</c> closed).</summary>
    private void LogNoteStacks(PurseNote note)
    {
        var stacks = new List<string>();
        foreach (var g in HeldOf(note.Wcid).Order())
        {
            var where = S.State.Self.Inventory.Find(g).Kind == InventoryPlaceKind.MainPack ? "main pack" : "elsewhere";
            stacks.Add(Text($"0x{g:X8} x{(S.State.Objects.TryGet(g, out var o) ? GearInventory.StackOf(S.State, o) : 0)} ({where})"));
        }

        Log(Text($"purse: {note.Name} held in {stacks.Count} stack(s): {(stacks.Count == 0 ? "none" : string.Join(", ", stacks))}"));
    }

    /// <summary>
    /// M5 7 D136: the pyreal stacks joined into the main pack's fullest stack with room. ACE never joins coin either: a sale's payout comes as new stacks of up to 25,000 (<c>Player_Commerce.cs</c> 194, 211-218, 302ff
    /// <c>CreatePayoutCoinStacks</c>), and the coin weenie (273) stacks to 25,000 (ACE-World-Database v0.9.296, PropertyInt 11); the player joins them with the same GameAction 0x0054 the corpse looter already sends for looted coin.
    /// </summary>
    private IEnumerable<ActionHandle?> MergeCoin()
    {
        foreach (var h in MergeBought(SellRules.PyrealWcid, 0, "Pyreal", mainPackTarget: true)) { yield return h; }
    }

    /// <summary>The seconds a Give has to take the piece and bring the reward (the Pathwarden station's, <see cref="PathwardenRun.AfterSeconds"/>).</summary>
    private const double ReturnSeconds = PathwardenRun.AfterSeconds;

    /// <summary>M6 5 Q2 (2-R9): metres beyond <see cref="PathwardenRun.UseRange"/> the giver may stand before the return pass walks to him (no value change).</summary>
    private const double ReturnWalkSlack = 1;

    /// <summary>M6 5 Q2 (2-R9): metres beyond <see cref="PathwardenRun.UseRange"/> the giver may stand after the walk, and at each Give's check before, for the Give to be tried (no value change).</summary>
    private const double ReturnGiveSlack = 3;

    /// <summary>M6 5 Q2 (2-R9): the most metres a placed giver may stand from the quest file's point and still be taken for him (<see cref="ReturnNpc"/>; no value change).</summary>
    private const double ReturnNpcMatchRadius = 40;

    /// <summary>The attuned pieces given back this trip.</summary>
    public int Returned { get; private set; }

    /// <summary>The attuned pieces kept this trip, each named with its reason (<c>return: kept &lt;name&gt; (&lt;reason&gt;)</c>).</summary>
    public IReadOnlyList<string> ReturnKept => _returnKept;

    private readonly List<string> _returnKept = [];

    /// <summary>
    /// M6 2 d: the return pass after the equip pass (<see cref="GearReturn"/>): each quest file's <c>returns</c> table is planned over the packs' attuned pieces the pass left (outgrown, or blocked by coverage). With pieces to give: check (the NPC's landblock is
    /// the trip's, he is in the object table within reach, by the station's approach), act (Give), check (the piece gone and the reward arrived, <see cref="GearReturn.Failure"/>); a piece that fails a check is kept and named, with no retry. One summary line:
    /// <c>return pass: given=&lt;n&gt; kept=&lt;n&gt;</c>.
    /// </summary>
    internal IEnumerable<ActionHandle?> ReturnPass(HuntingGround ground)
    {
        foreach (var table in GearReturnTable.All)
        {
            var skills = new QualitiesSkills(S.State.Self.Qualities);
            var context = Judge(ground).Context(skills);
            var held = new List<GearReturnHeld>();
            foreach (var candidate in HeldCandidates())
            {
                if (S.State.Self.Inventory.Contains(candidate.Guid) && S.State.Self.Inventory.Find(candidate.Guid).Kind != InventoryPlaceKind.Equipped && S.State.Objects.TryGet(candidate.Guid, out var o))
                {
                    held.Add(new GearReturnHeld(candidate, o.Weenie.WeenieClassId));
                }
            }

            var plan = ReturnUnlitGuard(GearReturn.Plan(table, held, SelfEquipJudge.Worn(S.State), context)); // M6 16 E5e6: a piece a worn piece beats only with its magic off is kept and named, and no Give of this visit takes it
            if (plan.Returns.Count == 0 && plan.Kept.Count == 0)
            {
                continue;
            }

            Stage = "return";
            foreach (var item in plan.Returns)
            {
                Log(Text($"return: {item.Name} 0x{item.Guid:X8} goes back to {table.GiverName} ({table.GiverWcid}): {item.Why}; his Give emote {item.Row.Emote} gives {item.Row.RewardName} ({item.Row.RewardWcid})"));
            }

            var npc = 0u;
            if (plan.Returns.Count > 0)
            {
                string? unreachable = null;
                if (table.Place is not { Point: { } at } place)
                {
                    unreachable = $"the quest names no place for {table.GiverName}";
                }
                else if (S.State.Self.Position is not { } here || (here.CellId >> 16) != (place.Cell >> 16))
                {
                    unreachable = $"he stands in landblock 0x{place.Cell >> 16:X4}, not where the trip is";
                }
                else
                {
                    npc = ReturnNpc(table.GiverWcid, place.Cell, at);
                    if (npc == 0 || ReturnDistance(npc) > PathwardenRun.UseRange + ReturnWalkSlack)
                    {
                        foreach (var h in TravelTo(new TravelRequest(Destination(place.Name, place.Cell, at), TravelPurpose.Cell, npc, PathwardenRun.UseRange), table.GiverName, optional: true)) { yield return h; }

                        if (LastTravelFailure is { } failure)
                        {
                            unreachable = "the walk failed: " + failure;
                        }
                        else
                        {
                            foreach (var h in Until(() => ReturnNpc(table.GiverWcid, place.Cell, at) != 0, NpcSeconds)) { yield return h; }

                            npc = ReturnNpc(table.GiverWcid, place.Cell, at);
                            unreachable = npc == 0 ? "he is not in the object table after the walk" : ReturnDistance(npc) > PathwardenRun.UseRange + ReturnGiveSlack ? Text($"he is {ReturnDistance(npc):0.0} m away after the walk") : null;
                        }
                    }
                }

                if (unreachable is not null)
                {
                    plan = plan.Unreachable(table.GiverName, unreachable);
                    Log(Text($"return: {table.GiverName} not in reach ({unreachable}): the pieces are kept"));
                }
            }

            var given = 0;
            var kept = plan.Kept.Select(k => k.ToString()).ToList();
            foreach (var item in plan.Returns)
            {
                // Check before: the piece is still held in a pack and the NPC is still in the table in reach.
                if (!S.State.Self.Inventory.Contains(item.Guid) || S.State.Self.Inventory.Find(item.Guid).Kind == InventoryPlaceKind.Equipped || !S.State.Objects.TryGet(npc, out _) || ReturnDistance(npc) > PathwardenRun.UseRange + ReturnGiveSlack)
                {
                    kept.Add(new GearReturnKept(item.Guid, item.Name, "check before failed: the piece is not held in a pack or the NPC is out of reach").ToString());
                    continue;
                }

                var scratch = new List<uint>();
                var before = GearInventory.CountOf(S.State, scratch, item.Row.RewardWcid);
                var give = S.Actions.Give(npc, item.Guid, 1, ActionSource.Automation);
                yield return give;
                foreach (var h in Until(() => !S.State.Self.Inventory.Contains(item.Guid) && GearInventory.CountOf(S.State, scratch, item.Row.RewardWcid) > before, ReturnSeconds)) { yield return h; }

                var after = GearInventory.CountOf(S.State, scratch, item.Row.RewardWcid);
                var verdict = GearReturn.Failure(item, S.State.Self.Inventory.Contains(item.Guid), before, after);
                Log(Text($"return: Give {item.Name} 0x{item.Guid:X8} to {table.GiverName}: {Describe(give)}; check after: {(verdict is null ? Text($"gone, and {item.Row.RewardName} arrived (held {before} -> {after})") : verdict)}"));
                if (verdict is null)
                {
                    given++;
                    Returned++;
                }
                else
                {
                    kept.Add(new GearReturnKept(item.Guid, item.Name, verdict).ToString());
                }
            }

            foreach (var line in kept)
            {
                _returnKept.Add(line);
                Log(line);
            }

            Log(Text($"return pass: given={given} kept={kept.Count}"));
        }
    }

    private uint ReturnNpc(uint wcid, uint cell, Vector3 at)
    {
        uint best = 0;
        var bestDistance = double.MaxValue;
        foreach (var o in S.State.Objects.All)
        {
            if (o.Weenie.WeenieClassId != wcid || o.Position is not { } p || p.CellId >> 16 != cell >> 16 || (o.ContainerId is { } c && c != 0))
            {
                continue;
            }

            var d = Vector3.Distance(p.Origin, at);
            if (d < bestDistance)
            {
                (best, bestDistance) = (o.Guid, d);
            }
        }

        return bestDistance <= ReturnNpcMatchRadius ? best : 0;
    }

    private double ReturnDistance(uint guid) =>
        S.State.Self.Position is { } here && S.State.Objects.TryGet(guid, out var o) && o.Position is { } p && p.CellId >> 16 == here.CellId >> 16 ? Vector3.Distance(p.Origin, here.Origin) : double.MaxValue;

    private bool _saleKeptLogged;
    private int _drinksKept;

    /// <summary>
    /// M6 14 (the Cave's run of 2026-11-12, 09:07 UTC): a restarted run begins on the ladder's first ground, whose stock lists neither tincture, and its sale sold the 12 Health Tinctures and 30 Mana Tinctures bought for the
    /// Cave to the shopkeeper. True for a drink the vitals rules drink (<see cref="HealthDrinks.All"/>, <see cref="ManaDrinks.All"/>, the Stamina Potion): no character sells one, whatever ground the sale is
    /// planned for.
    /// </summary>
    internal static bool DrinkInUse(uint wcid) => HealthDrinks.IndexOf(wcid) >= 0 || ManaDrinks.IndexOf(wcid) >= 0 || wcid == FighterKit.StaminaPotionWcid;

    /// <summary>
    /// M6 2 d: why the sale must not offer a pack item, or null. A worn piece; a Value of 0 (ACE: "has no value and cannot be sold", <c>Player_Commerce.cs</c>:275-280); an attuned piece (PropertyInt 114 above 0: ACE does buy it, <c>Vendor.cs</c>:626-627 only stops the vendor reselling it; Lifestone keeps it back so GearReturn can give it to its NPC for the reward); a retained one (PropertyBool 91, or the Retained description bit); a caster the gear floor keeps (<see cref="FloorCasters"/>: the floor wants two, the spare is never sold).
    /// </summary>
    private string? NotSellable(uint guid, WorldObject o, HashSet<uint> floorCasters)
    {
        if (o.Weenie.WielderId is { } wielder && wielder != 0)
        {
            return "worn";
        }

        if ((o.Weenie.Value ?? 0) < 1)
        {
            return "value 0";
        }

        var appraised = S.TryGetAppraisal(guid, out var appraisal) ? appraisal : null;
        if (GearScore.Attuned(appraised))
        {
            return "attuned";
        }

        if (appraised?.Bool(RetainedBool) == true || (o.DescriptionFlags & VendorStock.RetainedFlag) != 0)
        {
            return "retained";
        }

        return floorCasters.Contains(guid) ? "floor caster" : null;
    }

    /// <summary>
    /// M6 2 d: the pack casters the sale keeps for the gear floor. The floor wants <see cref="GearFloor.CastersWanted"/> casters held (wielded, in the main pack, in the side packs); the main pack's casters beyond what the rest of the packs and the
    /// hands hold are kept best first by <see cref="GearScore"/>, an unread one always (an unappraised caster is not sold on a guess). The rest are loot.
    /// </summary>
    private HashSet<uint> FloorCasters(HuntingGround ground, KeepAsideRule? keepAside)
    {
        // M6 14 (the owner's ruling, 2026-11-16: drop the loot casters too): the casters her gear rules need (CasterKeep: the wielded, the best for each element she casts, the floor's spare, the charged spare, a useful great cantrip,
        // the spend's target, an unread one); every verdict logged. The floor's two-casters count is one of the rules, no longer the whole.
        _ = ground;
        _ = keepAside;
        return [.. GearKeepsNow().Verdicts.Where(v => v.KeptFor is not null).Select(v => v.Guid)];
    }

    private (Func<uint, string?> Casters, Func<uint, bool> Stones, List<CasterKeepVerdict> Verdicts)? _gearKeeps;

    /// <summary>M6 14: the gear rules' keeps over what she carries (<see cref="CantripRoom.GearKeeps"/>), worked out once a sale (or a drain's list), each caster's verdict logged.</summary>
    private (Func<uint, string?> Casters, Func<uint, bool> Stones, List<CasterKeepVerdict> Verdicts) GearKeepsNow()
    {
        if (_gearKeeps is { } made)
        {
            return made;
        }

        var keeps = CantripRoom.GearKeeps(S.State, Context.Dungeon, Context.Spells, Wardrobe.For(S.State).KeepCastChance); // M6 16 E5e1: the job's chance, as the field's (the level up job: the ladder's plan_cast_chance)
        foreach (var v in keeps.Verdicts)
        {
            Log("gear: caster keep: " + v.Line);
        }

        _gearKeeps = keeps;
        return keeps;
    }

    private const uint ContainerType = 0x200;

    /// <summary>M6 5 q4 (D57), F1 (review 5-R13): the weenie class ids of every quest's hunt-until item and of the items a quest file names as expected or done items (<see cref="QuestCatalog.All"/>, read once), and (M6 villa) the Writ of Refuge and the Crude Lockpick: the sale never offers them. Not every Bonded item: a bonded item that no quest file names is the sale's to judge (ACE refuses to sell it).</summary>
    internal static IReadOnlySet<uint> QuestItemWcids => QuestItems.Value;

    private static readonly Lazy<IReadOnlySet<uint>> QuestItems = new(() => QuestCatalog.All.SelectMany(QuestItemsOf).Concat([Housing.VillaFacts.WritWcid, Housing.VillaFacts.LockpickWcid]).ToHashSet()); // M6 villa: the villa's price and rent items (the writs and the Crude Lockpick) are kept as a quest's are; the minor stones are the stone files' done items

    /// <summary>The wcids of one quest file the sale keeps: its hunt-until items, the expected items of its steps and its done item.</summary>
    internal static IEnumerable<uint> QuestItemsOf(Quest quest) =>
        quest.Steps.Where(st => st.Kind == QuestStepKind.HuntUntil && st.Item?.Wcid is not null).Select(st => st.Item!.Wcid!.Value)
            .Concat(quest.Steps.Select(st => st.Expect.Item?.Wcid).OfType<uint>())
            .Concat(quest.Done.Item?.Wcid is { } done ? [done] : []);

    /// <summary>
    /// M6 5 q4 (D57): why the sale (and the drop of a refused item) must never touch the item, or null: its weenie is a quest's hunt-until item (<paramref name="questItems"/>; restart-proof, read from the quest files, not the loot step's note),
    /// (M6 5 F1, review 5-R13: no longer "any Bonded item": the hunt-until items, the expected items and the done items of the quest files are the whole rule).
    /// </summary>
    /// <param name="item">The held item.</param>
    /// <param name="appraisal">The session's appraisal of it, or null.</param>
    /// <param name="questItems">The quests' hunt-until wcids.</param>
    internal static string? QuestKeepReason(WorldObject item, Appraisal? appraisal, IReadOnlySet<uint> questItems) =>
        questItems.Contains(item.Weenie.WeenieClassId) ? "quest item: a hunt-until, expected or done item of a quest file" : null;

    private Func<WorldObject, string?>? _scrollKeep;

    /// <summary>M6 6 MG: the keep reason <see cref="ScrollKeepRule.Hook"/> gives <paramref name="item"/> over the live spellbook and skills for the ground's rung, or null (not a scroll, the book unread, or the rule does not keep it).</summary>
    internal string? ScrollKeepReason(WorldObject item) =>
        (_scrollKeep ??= ScrollKeepRule.Hook(LadderLoader.WarMage, Context.Dungeon.Key, Context.Spells, () => S.State.Self.Spellbook, () => S.State.Self.Qualities, botCovers: () => BuffBotCover.For(S.State).Covering))(item)
        ?? EverySpellKeepReason(item); // M6 14 (the every-spell rule): its bought, refused and wanted scrolls are kept too

    private string? SpellNameOf(uint id) => Context.Spells.TryGetValue(id, out var info) ? info.Name : null;

    /// <summary>The most pack items asked for an appraisal before one sale (a restart empties the appraisals; a pack holds 102).</summary>
    private const int KeepAsideAppraisalCap = 40;

    /// <summary>
    /// M5 7 F30: before the sale, asks the server about each pack item the keep-aside rule could match (a caster, a wearable or jewellery piece) that no appraisal of is held, so the rule can read its bonus and spells; the sale then leaves the ones that match.
    /// The worn pieces are not asked about (they are not for sale).
    /// </summary>
    private IEnumerable<ActionHandle?> AppraiseForKeepAside(KeepAsideRule rule)
    {
        var asked = 0;
        foreach (var (guid, o) in PackItems())
        {
            if (asked >= KeepAsideAppraisalCap || !KeepAsideRule.NeedsAppraisal(o.ItemType) || (o.Weenie.WielderId is { } wielder && wielder != 0) || (o.Weenie.Value ?? 0) < 1 || _memory.KeptAside.Contains(guid) || S.TryGetAppraisal(guid, out _))
            {
                continue;
            }

            asked++;
            var identify = S.Actions.Identify(guid, ActionSource.Host);
            yield return identify;
        }

        if (asked > 0)
        {
            Log($"sale: asked about {asked} caster/wearable pack item(s) for the keep-aside rule ({rule.Setting.MinWarBonusPercent:0.##}% war bonus, {rule.SpellNames.Count} cantrip names)");
        }
    }

    /// <summary>
    /// Walks to the vendor and sets his guid when he is in the object table and may be opened. A vendor whose profile stop cell is his own (Thelnoth): known before the walk, the stop is at his use radius from where he stands (an object
    /// goal in his live cell); unknown, the walk goes to his own point and stops <see cref="ShopLeg.UnseenVendorReachMetres"/> short of it (his body stands there, D120) and, when he then appears out of reach (<see cref="ShopLeg.ReachOf"/>), once more to him. A vendor whose stop cell is a room in front of him
    /// (<see cref="GroundVendor.StopCell"/> is not his <see cref="GroundVendor.Cell"/>: Archmage Cindrue, Renald) and who is not live in some other cell: every walk is a point request to the stop cell and <see cref="GroundVendor.StopPoint"/>
    /// (else his point), never an object goal, because an object goal that names an indoor cell arrives only in that room and within his range (<c>NavRunner.CheckArrival</c>), which a front room is not. There is no second walk: the room
    /// graph does not join the rooms behind that room (<c>TODO(room-steps)</c>), so he is taken as met when the walk ended and the object table holds him, and the open (the use) lets ACE's move-to walk the last metres.
    /// </summary>
    private IEnumerable<ActionHandle?> Meet(GroundVendor vendor, Action<uint> found)
    {
        _unreachable = null;
        var logged = false;
        var front = vendor.StopCell != vendor.Cell;
        for (var walk = 0; walk < 2; walk++)
        {
            TravelRequest request;
            var toRoom = false;
            if (S.State.Objects.TryGet(vendor.Guid, out var live) && live.Position is { } at)
            {
                logged = logged || LogLive(vendor, at);
                if (front && at.CellId == vendor.Cell)
                {
                    request = RoomRequest(vendor);
                    toRoom = true;
                }
                else
                {
                    var cell = at.CellId == vendor.Cell ? vendor.StopCell : at.CellId;
                    request = new TravelRequest(Destination(vendor.Name, cell, at.Origin), TravelPurpose.Vendor, vendor.Guid, UseRadiusOf(live));
                }
            }
            else if (walk == 0)
            {
                request = RoomRequest(vendor);
                toRoom = front;
            }
            else
            {
                yield break;
            }

            var walkFrom = S.State.Self.Position;
            foreach (var h in TravelTo(request, Text($"vendor:{vendor.Name}"), optional: true)) { yield return h; }

            // M6 1 L3's way out, the fallback since M6 4 W1: a walk between rooms of two landblocks plans as one composed walk (out, outdoors, in); when it fails, out to the lifestone, then in again.
            if (LastTravelFailure is { } composed && walkFrom is { } from && ResupplyPlanner.NeedsWayOut(from.CellId, request.Destination.Location.Position.CellId, Context.Dungeon.Home.LifestoneCell))
            {
                var home = Context.Dungeon.Home;
                Log(Text($"walking: purpose=vendor:{vendor.Name} (the way out: the walk from the room 0x{from.CellId:X8} to his room 0x{request.Destination.Location.Position.CellId:X8} in another landblock, out of the room, outdoors and in (M6 4 W1), failed: {composed}; out to {home.Town}'s lifestone 0x{home.LifestoneCell:X8} first, then in)"));
                foreach (var h in TravelTo(new TravelRequest(Destination("way-out:" + vendor.Name, home.LifestoneCell, home.LifestonePoint), TravelPurpose.Vendor, Range: ShopLeg.UnseenVendorReachMetres), Text($"vendor:{vendor.Name} (the way out)"), optional: true)) { yield return h; }

                if (LastTravelFailure is { } outWhy)
                {
                    _unreachable = $"the way out: {outWhy} (after the composed walk: {composed})"; // named like any unreached stop
                    yield break;
                }

                foreach (var h in TravelTo(request, Text($"vendor:{vendor.Name}"), optional: true)) { yield return h; }
            }

            if (LastTravelFailure is { } why)
            {
                _unreachable = why; // the caller names it and goes on to the next stop (TODO(m5-vendor-unreachable))
                yield break;
            }

            foreach (var h in Until(() => S.State.Objects.TryGet(vendor.Guid, out var o) && o.Position is not null, NpcSeconds)) { yield return h; }

            if (!S.State.Objects.TryGet(vendor.Guid, out var met) || met.Position is not { } there || S.State.Self.Position is not { } me)
            {
                yield break;
            }

            logged = logged || LogLive(vendor, there);
            var distance = Distance(me.CellId, me.Origin, there.CellId, there.Origin);
            if (distance <= ReachOf(met))
            {
                found(vendor.Guid);
                yield break;
            }

            if (toRoom)
            {
                Log(Text($"vendor: {vendor.Name} is {distance:0.0} m from his front room 0x{vendor.StopCell:X8}, beyond his reach {ReachOf(met):0.0} m: the room graph stops here (TODO(room-steps)); the open asks ACE's move-to for the rest ({AceMoveTo})"));
                found(vendor.Guid);
                yield break;
            }

            Log(Text($"vendor: {vendor.Name} is {distance:0.0} m away after the walk, beyond his reach {ReachOf(met):0.0} m"));
        }
    }

    /// <summary>
    /// The point request to the vendor's stop: his stop cell and the profile's stop point (a room in front of him: no object, no range, the body stands on the point), else his own point with
    /// <see cref="ShopLeg.UnseenVendorReachMetres"/> as the range, so the walk stops short of the body that stands on that point (M5 item 7, D120: a 0.5 m stop on his own point jammed against him in Thelnoth's cellar).
    /// </summary>
    private static TravelRequest RoomRequest(GroundVendor vendor) =>
        vendor.StopCell != vendor.Cell || vendor.StopPoint is not null // M6 1: a stop point in his own cell (20216's street under his balcony) is a point request too
            ? new TravelRequest(Destination(vendor.Name, vendor.StopCell, vendor.StopPoint ?? vendor.Point), TravelPurpose.Vendor)
            : new TravelRequest(Destination(vendor.Name, vendor.StopCell, vendor.Point), TravelPurpose.Vendor, Range: ShopLeg.UnseenVendorReachMetres);

    private bool LogLive(GroundVendor vendor, CellFrame live)
    {
        var off = Distance(live.CellId, live.Origin, vendor.Cell, vendor.Point);
        Log(Text($"vendor: {vendor.Name} live 0x{live.CellId:X8} ({live.Origin.X:0.0}, {live.Origin.Y:0.0}, {live.Origin.Z:0.0}), profile 0x{vendor.Cell:X8} ({vendor.Point.X:0.0}, {vendor.Point.Y:0.0}, {vendor.Point.Z:0.0}), off by {off:0.0} m"));
        return true;
    }

    private static double Distance(uint cellA, Vector3 a, uint cellB, Vector3 b)
    {
        var flat = Vector2.Distance(ResupplyPlanner.WorldMetres(cellA, a), ResupplyPlanner.WorldMetres(cellB, b));
        var dz = a.Z - b.Z;
        return Math.Sqrt((flat * flat) + (dz * dz));
    }

    /// <summary>
    /// M6 sprint (runs 1 to 5; owner ruling 2026-11-19: no error a retail player would not see): ACE refuses an item whose IsSellable is false with "The X is unsellable." (<c>Player_Commerce.cs</c> 268-272 @95ef426d); Health Draught 2457, Potion of
    /// Healing 377, Mana Potion 379 and Mana Draught 2460 carry IsSellable 0 (world weenie_properties_bool type 69). Before the offer, each item the vendor's mask takes whose class has no verdict yet (<see cref="SaleMemory.SellableClass"/>)
    /// and that no successful appraisal covers is appraised, one per class, as a player looks before selling; the offer then leaves the ones marked unsellable.
    /// </summary>
    private IEnumerable<ActionHandle?> AppraiseForSale(VendorStock stock, List<(uint Guid, WorldObject Object)> kept)
    {
        var asked = new HashSet<uint>();
        foreach (var (item, o) in kept)
        {
            var wcid = o.Weenie.WeenieClassId;
            if (!S.State.Self.Inventory.Contains(item) || !stock.BuysType(o.ItemType) || SellableOnWire(item, o) is not null || !asked.Add(wcid))
            {
                continue;
            }

            yield return S.Actions.Identify(item, ActionSource.Host);
        }

        if (asked.Count > 0)
        {
            Log(Text($"sale: appraised {asked.Count} offered item(s), one of each class with no IsSellable verdict yet (PropertyBool {IsSellableBool}): the offer leaves the ones marked unsellable (m6-sale-appraise)"));
        }
    }

    /// <summary>The item's IsSellable as ACE holds it: a successful appraisal of it (the bool, or sellable when the bool is absent), else the verdict its class taught (<see cref="SaleMemory.SellableClass"/>), else null (unknown).</summary>
    private bool? SellableOnWire(uint item, WorldObject o)
    {
        var wcid = o.Weenie.WeenieClassId;
        if (S.TryGetAppraisal(item, out var appraisal) && appraisal.Success)
        {
            var sellable = appraisal.Bool(IsSellableBool) ?? true;
            _memory.SellableClass[wcid] = sellable;
            return sellable;
        }

        return _memory.SellableClass.TryGetValue(wcid, out var learned) ? learned : null;
    }

    /// <summary>Sells the kept items the loot buyer's mask takes; an item IsSellable marks false (from the wire when an appraisal carries it, else ACE's class list) is left and named, as is one ACE kept.</summary>
    private IEnumerable<ActionHandle?> SellLoot(HuntingGround ground, GroundVendor vendor, uint guid, List<(uint Guid, WorldObject Object)> kept)
    {
        if (S.State.Vendor is not { } stock)
        {
            yield break;
        }

        foreach (var h in AppraiseForSale(stock, kept)) { yield return h; }

        var offer = new List<uint>();
        var noWire = 0;
        var dropping = new List<(uint Guid, string Name)>();
        foreach (var (item, o) in kept)
        {
            if (!S.State.Self.Inventory.Contains(item))
            {
                continue;
            }

            if (!stock.BuysType(o.ItemType))
            {
                if (ground.Vendors.Any(v => v.Guid != vendor.Guid && v.Buys(o.ItemType)))
                {
                    continue; // another loot buyer of the trip takes it (M5 7 F28)
                }

                Log(Text($"sale: {o.Name} (ItemType 0x{o.ItemType:X}) is outside {vendor.Name}'s mask: kept"));
                continue;
            }

            var wire = SellableOnWire(item, o);
            if (wire == false)
            {
                Refuse("unsellable", Text($"{o.Name} is marked unsellable on the wire (IsSellable=0, by its appraisal or its class's): left in the pack, not offered (m6-sale-appraise)"));
                NoteRefusal(item, o.Name, dropping);
                continue;
            }

            noWire += wire is null ? 1 : 0;
            if (stock.Refusal(o) != SaleRefusal.None)
            {
                Refuse("unsellable", Text($"{o.Name}: {stock.DescribeRefusal(o)}: left in the pack"));
                NoteRefusal(item, o.Name, dropping);
                continue;
            }

            offer.Add(item);
        }

        if (noWire > 0)
        {
            Log(Text($"TODO(m4-vendor-issellable-wire): IsSellable (PropertyBool {IsSellableBool}) is not on the wire for {noWire} of the offered items (no appraisal carries it): ACE's class list decided them"));
        }

        // M5 7 G1 (12b): ACE counts the payout's coin stacks (25,000 each) against the free slots before it takes the items and refuses the whole sale when they do not fit (Player_Commerce.cs 176-191 @95ef426d), so the sale goes in batches
        // whose payout fits the free slots less one (kept for a note). What does not fit waits for the notes bought after the walk (the next round) or the next trip.
        // M5 7 D99 (soak round 11, 2026-10-29): a full pack sold nothing, trip after trip (no free slot for the payout's coin, and the note's slot held back from the last one). With no free slot the
        // offer's cheapest item is dropped at the home to open one (never a kept-aside item: they are not offered); with one free slot it takes the payout (the note waits). A batch of two or
        // more items then frees more slots than its coin takes, so the next round sells more.
        var free = PackView.MainPackFree(S.State);
        if (free == 0 && offer.Count > 1)
        {
            var cheapest = offer.MinBy(g => S.State.Objects.TryGet(g, out var t) ? stock.PayoutFor(t) : 0);
            var cheapName = S.State.Objects.TryGet(cheapest, out var co) ? co.Name : $"0x{cheapest:X8}";
            var cheapPay = co is null ? 0 : stock.PayoutFor(co);
            var drop = S.Actions.Drop(cheapest, ActionSource.Automation);
            yield return drop;
            foreach (var h in Until(() => !S.State.Self.Inventory.Contains(cheapest), 2)) { yield return h; }
            offer.Remove(cheapest);
            _memory.Dropped.Add(cheapest);
            DroppedItems++;
            free = PackView.MainPackFree(S.State);
            Log(Text($"sale: the pack has no free slot for the payout's coin, so {cheapName} (0x{cheapest:X8}, payout {cheapPay}), the offer's cheapest, was dropped at the home to open one: {Describe(drop)}; free slots now {free}"));
        }

        var room = (free >= 2 ? free - 1 : free) * (long)CoinStackMax;
        var batch = new List<uint>();
        long batchPayout = 0;
        var waiting = 0;
        foreach (var item in offer)
        {
            var pay = S.State.Objects.TryGet(item, out var thing) ? stock.PayoutFor(thing) : 0;
            if (batchPayout + pay <= room)
            {
                batch.Add(item);
                batchPayout += pay;
            }
            else
            {
                waiting++;
            }
        }

        if (waiting > 0)
        {
            _slotLimited = true;
            Log(Text($"sale: {waiting} of {offer.Count} items wait: their payout needs more coin stacks than the {PackView.MainPackFree(S.State)} free slots hold (ACE refuses the whole sale then, Player_Commerce.cs 176-191); {batch.Count} go in this batch ({batchPayout} pyreals)"));
        }

        offer = batch;
        if (offer.Count == 0)
        {
            Log(Text($"sale: nothing {vendor.Name} takes among the pack items{(waiting > 0 ? " that fits the free slots" : string.Empty)}"));
            foreach (var h in DropRefused(dropping)) { yield return h; }

            yield break;
        }

        var coinsBefore = Coins();
        var sell = S.Actions.Sell(guid, offer, ActionSource.Automation);
        yield return sell;
        foreach (var h in Until(() => offer.All(g => !S.State.Self.Inventory.Contains(g)), 2)) { yield return h; }

        var left = offer.Count(g => !S.State.Self.Inventory.Contains(g));
        Sold += left;
        var earned = sell.Result?.Actual ?? Math.Max(0, Coins() - coinsBefore);
        Earned += earned;
        Log(Text($"sale: {left} of {offer.Count} items sold to {vendor.Name} for {earned} pyreals: {Describe(sell)}"));
        foreach (var g in offer.Where(g => S.State.Self.Inventory.Contains(g)))
        {
            var name = S.State.Objects.TryGet(g, out var o) ? o.Name : $"0x{g:X8}";
            var kind = Kind(sell.Outcome?.Detail, "unsellable");
            Refuse(kind, Text($"{name} stayed in the pack: {vendor.Name} did not take it ({Describe(sell)})"));
            if (kind == "unsellable")
            {
                NoteRefusal(g, name, dropping);
            }
        }

        foreach (var h in DropRefused(dropping)) { yield return h; }

        foreach (var h in MergeCoin()) { yield return h; } // M5 7 D136: the payout's new coin stacks join the main pack's
    }

    /// <summary>
    /// M5 7 G1 (edge audit item 2): counts one trip's refusal of an item (once a trip, whatever the vendors); the second trip's refusal puts it on the drop list, logged once and never offered again.
    /// </summary>
    private void NoteRefusal(uint guid, string name, List<(uint Guid, string Name)> dropping)
    {
        if (S.State.Objects.TryGet(guid, out var held) && IsUsableConsumable(held.Weenie.WeenieClassId))
        {
            if (_refusedThisTrip.Add(guid))
            {
                Log(Text($"sale: {name} (0x{guid:X8}) is a potion she drinks (the emergency rule or the mana rule): kept, never dropped for an unsellable refusal"));
            }

            return;
        }

        if (!_refusedThisTrip.Add(guid))
        {
            return;
        }

        var n = _memory.Refused.GetValueOrDefault(guid) + 1;
        _memory.Refused[guid] = n;
        if (n >= SaleMemory.DropAfter && _memory.Dropped.Add(guid))
        {
            dropping.Add((guid, name));
        }
    }

    /// <summary>M6 7 AU (the owner): the potions the mage drinks (Health Draught, Mana Draught, the Potions of Healing, Stamina and Mana): an unsellable one is kept for its use, only an item with no use is dropped after two refusals.</summary>
    /// <param name="wcid">The weenie class id.</param>
    internal static bool IsUsableConsumable(uint wcid) => wcid is 378 || Vitals.ManaDrinks.IndexOf(wcid) >= 0 || Vitals.HealthDrinks.IndexOf(wcid) >= 0; // M6 14 T: every health drink (the Health Draught 2457 and the Potion of Healing 377 among them)

    private IEnumerable<ActionHandle?> DropRefused(List<(uint Guid, string Name)> dropping)
    {
        foreach (var (guid, name) in dropping)
        {
            if (!S.State.Self.Inventory.Contains(guid))
            {
                continue;
            }

            var drop = S.Actions.Drop(guid, ActionSource.Automation);
            yield return drop;
            DroppedItems++;
            Log(Text($"sale: {name} (0x{guid:X8}) was refused by a vendor on {SaleMemory.DropAfter} trips: dropped at the home, never offered again: {Describe(drop)}"));
        }

        dropping.Clear();
    }

    /// <summary>
    /// Buys one line of the list: the wire's price checked against the profile's, a note changed when the coin is short, what the purse pays, and ACE's answer named. The vendor's price is <c>Vendor.GetSellCost</c> (<c>Vendor.cs</c> 577-585
    /// @95ef426d): <c>max(1, ceil(rate x Value - 0.1))</c> with the rate the vendor's SellPrice (1.0 when absent) and 1.15 for a promissory note; the wire's 0062 price is that, per stack. The whole buy is refused when the pyreals fall
    /// short (<c>BuyItems_ValidateTransaction</c> 546-552: no text), or the pack cannot take the ware (493-500).
    /// </summary>
    private IEnumerable<ActionHandle?> BuyLine(HuntingGround ground, GroundVendor vendor, uint guid, ResupplyBuy buy)
    {
        var name = buy.Line.Name;
        var isPack = buy.Line.Wcid == FighterKit.SidePackWcid; // M6 8 D: a side pack takes a container slot, not a main pack slot
        var sideBefore = PackLayout.SidePacks(S.State).Count;

        // M6 16 E5b: the buy itself is the vendor counter's, the one buy of every caller (the checks before, a note changed when the coin is short, the send, the wait for the ware, the merge, the check after); the trip's own lines and counters are said through its host (TownTripCounter), and what is the trip's alone stays below
        var counter = new VendorBuyResult();
        var request = new VendorBuyRequest(guid, buy.Line.Wcid, buy.Count) { VendorName = vendor.Name, WareName = name, PartialAllowed = true, MergeAfter = true, ContainerSlot = isPack, CoinMustFall = false, ArrivalSeconds = BoughtArrivalSeconds, NotesFirst = ground.Supplies.Purse.Notes };
        foreach (var h in VendorCounter.Buy(S, request, counter, new TownTripCounter(this, ground, vendor, buy))) { yield return h; }

        switch (counter.Refusal)
        {
            case "vendor-closed":
                Refuse("open", Text($"{name}: {vendor.Name}'s stock is no longer open"));
                yield break;
            case "not-sold":
                Refuse("stock", Text($"{name} ({buy.Line.Wcid}) is not in {vendor.Name}'s 0062 ({S.State.Vendor?.Entries.Length ?? 0} rows)"));
                yield break;
            case "stock-short":
                Refuse("stock", Text($"{name}: {vendor.Name} is out of stock (his 0062 lists 0)"));
                yield break;
            case "pack-full":
                Refuse("pack", isPack ? Text($"{name}: {PackLayout.ContainerRoom(S.State)} container slots free for the {counter.Count} to buy: nothing is bought") : Text($"{name}: the main pack is full (0 free slots): nothing is bought"));
                yield break;
            case "purse-short":
                yield break; // the purse line named it (TownTripCounter.PurseShort): the coin pays for none
            case VendorCounter.Refused:
                Refuse(Kind(counter.Detail, "other"), Text($"{name} x{counter.Count} at {vendor.Name}: {counter.Outcome}"));
                if (ResupplyPlanner.FociWcids.Value.Contains(buy.Line.Wcid) && _memory.FociRefused.Add(buy.Line.Wcid))
                {
                    Log(Text($"foci: {name} was refused at {vendor.Name}: not tried again this session (TODO(m6-foci-refused))"));
                }

                yield break;
        }

        var entry = counter.Entry!;
        var count = counter.Count;
        var paid = counter.Paid;
        var heldBefore = counter.HeldBefore;

        // M6 7 AX: the check after reads every place a ware can be held (GearInventory.Collect: the main pack, the side packs and what is in them, the worn): a focus comes as ContainerType 2 (WorldObject.cs:453-462 @95ef426d), beside the side packs, so a read of type-0 entries never finds it
        var heldAfter = counter.HeldAfter;
        var unseen = counter.Unseen;
        if (unseen)
        {
            _memory.BoughtUnseen[buy.Line.Wcid] = heldBefore + count;
            Log(Text($"resupply: TODO(m6-bought-unseen): {name}: {count} paid for at {vendor.Name}, held {heldBefore} -> {heldAfter} after {BoughtArrivalSeconds:0} s: the next plan holds the line out once"));
        }

        if (ResupplyPlanner.FociWcids.Value.Contains(buy.Line.Wcid))
        {
            Log(Text($"foci: check after: {name} held {heldBefore} -> {heldAfter}, paid {paid}; the next pass's lines read the foci formula (AccountFormula.HoldsFocus)"));
            if (unseen && _memory.FociRefused.Add(buy.Line.Wcid))
            {
                Refuse("stock", Text($"{name}: bought but not in the pack after {BoughtArrivalSeconds:0} s: not tried again this session (TODO(m6-foci-refused))"));
            }
        }
        else if (isPack)
        {
            // M6 8 D: the check after reads the side slots: the pack must be one of the player's side packs (ACE puts a container in a side slot, Container.cs 416 @95ef426d), where the layout and the sort find it.
            var sideAfter = PackLayout.SidePacks(S.State).Count;
            Log(Text($"packs: check after: side packs {sideBefore} -> {sideAfter} ({count} {name} bought at {vendor.Name} for {paid} pyreals); the sort after the buys puts them to their jobs"));
            if (!unseen && sideAfter < sideBefore + count)
            {
                Log(Text($"packs: TODO(m6-pack-unplaced): {name} is held but not in a side slot ({sideBefore} -> {sideAfter}, {count} bought); the layout counts the side packs again on the next trip, nothing is moved here"));
            }
        }
        else if (IsLearnScroll(ground, buy.Line.Wcid)) // M6 1 c: the ground's list or the plan's
        {
            ScrollsBought += count;
            Log(Text($"learn: bought the scroll {name} (weenie {buy.Line.Wcid}) at {vendor.Name} for {paid} pyreals; scrolls of it in the pack {InPack(buy.Line.Wcid).Count}: read after the walk"));
        }
        else if ((entry.ItemType & WritableType) != 0)
        {
            Log(Text($"TODO(m6-scroll-read): {name} is a scroll (ItemType 0x{entry.ItemType:X}) no learn line of the ground names; it is not read"));
        }
    }

    /// <summary>M6 7 AX: how long a paid ware may take to reach the model before the check after names it unseen (was 3 s). ACE's answer and the ware's F745 and 0022 are separate sends.</summary>
    internal const double BoughtArrivalSeconds = 10;

    /// <summary>
    /// M6 8 D: the side packs this trip buys (<see cref="ResupplyPlanner.PackBuy"/>), after the list's other lines: the layout's short is read from the state, the container slots free are counted less the foci the same list buys (a focus takes a slot too), and
    /// the purse left is the whole purse less the list's cost. Logged on one line whether it buys or not. It rides on this trip and calls none of its own.
    /// </summary>
    private List<ResupplyBuy> PackBuys(HuntingGround ground, IReadOnlyList<ResupplyBuy> list)
    {
        var pendingFoci = list.Where(b => ResupplyPlanner.FociWcids.Value.Contains(b.Line.Wcid)).Sum(b => b.Count);
        var listCost = list.Sum(b => (long)b.Count * (b.Vendor.Sells.FirstOrDefault(w => w.Wcid == b.Line.Wcid)?.Price ?? 0));
        var held = PackLayout.SidePacks(S.State).Count;
        var buy = ResupplyPlanner.PackBuy(ground, held, Math.Max(0, PackLayout.ContainerRoom(S.State) - pendingFoci), FloorPurse() - listCost, out var why);
        Log("resupply: " + why);
        return buy is null ? [] : [buy];
    }

    /// <summary>M6 7 AX: takes out of the plan a line whose last buy had not shown (<see cref="SaleMemory.BoughtUnseen"/>), once: a ware that has shown since is cleared, one still short is held for this plan by name. The next plan reads the counts alone.</summary>
    private List<ResupplyBuy> HoldUnseen(List<ResupplyBuy> plan)
    {
        foreach (var (wcid, expected) in _memory.BoughtUnseen.ToList())
        {
            _memory.BoughtUnseen.Remove(wcid);
            var held = GearInventory.CountOf(S.State, [], wcid);
            var name = plan.FirstOrDefault(b => b.Line.Wcid == wcid)?.Line.Name ?? wcid.ToString(CultureInfo.InvariantCulture);
            if (held >= expected)
            {
                Log(Text($"resupply: {name}: the late stack has shown (held {held}, expected {expected})"));
                continue;
            }

            if (plan.RemoveAll(b => b.Line.Wcid == wcid) > 0)
            {
                Log(Text($"resupply: {name}: held out of this plan: its last buy has not shown (held {held}, expected {expected}), TODO(m6-bought-unseen)"));
            }
        }

        return plan;
    }

    /// <summary>The stack merges the server confirmed after a purchase (M5 7).</summary>
    public int StackMerges { get; private set; }

    /// <summary>The post-purchase merges that were refused or got no answer (M5 7).</summary>
    public int MergesRefused { get; private set; }

    /// <summary>
    /// M5 7: ACE never merges a bought stack into a held one (<c>Player_Commerce.cs</c> 59-102 <c>FinalizeBuyTransaction</c> creates each ware as a new object), so every purchase of a stackable (spell components, potions) would take new
    /// pack slots. Waits up to <see cref="BoughtArrivalSeconds"/> for the bought items to be in the model (<paramref name="expectedTotal"/> held in all), then merges the smallest held stack of the weenie into the fullest other one with room (<see cref="StackMerge"/>,
    /// GameAction 0x0054) until none is left to merge; a refused merge is logged, counted and that stack is left. A ware that does not stack does nothing. M5 7 D136: the trade notes and the coin pass <paramref name="mainPackTarget"/>, so
    /// they join only a stack in the main pack (D121).
    /// </summary>
    private IEnumerable<ActionHandle?> MergeBought(uint wcid, int expectedTotal, string name, bool mainPackTarget = false)
    {
        foreach (var h in VendorCounter.Merge(S, new TownTripCounter(this, null, null, null), wcid, expectedTotal, name, BoughtArrivalSeconds, mainPackTarget)) { yield return h; } // M6 16 E5b: the one merge after a buy, the vendor counter's; the trip's lines and counters through its host
    }

    /// <summary>The trade note stacks a buy can change: the main pack's (<see cref="StationLeg.InPack"/>), every one of ACE's ten faces.</summary>
    private List<(uint Guid, PurseNote Note)> HeldNotes(HuntingGround ground) =>
        [.. PurseNotes.Read(S.State, ground.Supplies.Purse.Notes).Notes.Select(h => (h.Guid, h.Note))]; // M6 16 E5b: the one read of the purse's notes, the vendor counter's too (M6 7 AU: every one of ACE's ten faces, not only the profile's)

    /// <summary>M6 16 E5b: the main pack's free slots, for the vendor counter's host (<see cref="TownTripCounter"/>).</summary>
    private int MainRoom() => PackView.MainPackFree(S.State);

    /// <summary>
    /// The kind of an ACE refusal from its detail: <c>coin</c> (the tracker's "too few pyreals": ACE sends no text, Vendor.cs 546-552 @95ef426d), <c>unsellable</c> (ACE's 02EB "The X is unsellable.", Player_Commerce.cs:268-271 @95ef426d), <c>pack</c>
    /// or <c>stock</c> when the text names the pack, the burden or the stock, else <paramref name="fallback"/>. ACE's own buy-side lines are <c>Vendor.cs</c> 496 "You are too encumbered to buy that!", 498 "You do not have enough pack space
    /// to buy that!" and 500 "You do not have enough container slots to buy that!" (@95ef426d), which the words below match (<c>TODO(m5-resupply-refusal-text)</c> closed for them; ACE tells no line for a row it does not list, 456-468, so
    /// <c>stock</c> is the station's own kind, and the whole text is still logged).
    /// </summary>
    internal static string Kind(string? detail, string fallback)
    {
        var d = detail ?? string.Empty;
        return d.Contains("too few pyreals", StringComparison.Ordinal) ? "coin"
            : d.Contains("unsellable", StringComparison.Ordinal) ? "unsellable"
            : d.Contains("pack", StringComparison.OrdinalIgnoreCase) || d.Contains("container slots", StringComparison.OrdinalIgnoreCase) || d.Contains("encumb", StringComparison.OrdinalIgnoreCase) || d.Contains("burden", StringComparison.OrdinalIgnoreCase) ? "pack"
            : d.Contains("stock", StringComparison.OrdinalIgnoreCase) ? "stock"
            : fallback;
    }

    private void Refuse(string kind, string what)
    {
        _refusals.Add(kind);
        Log($"refused: {kind}: {what}");
    }

    private void Fail(string why)
    {
        Check(false, why);
        Failure = why;
    }

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
