using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
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
public sealed record ResupplyOptions(bool Loot = false, Func<IReadOnlyCollection<uint>>? Kept = null, IReadOnlyList<KitLine>? Kit = null, SaleMemory? Memory = null, bool Gear = false);

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
    public static IReadOnlyList<ResupplyBuy> BuyList(HuntingGround ground, LogisticsCounters counters)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(counters);
        var list = new List<ResupplyBuy>();
        for (var i = 0; i < counters.LineCount; i++)
        {
            var line = counters.LineAt(i);
            if (counters.IsCarried(i) || ground.VendorSelling(line.Wcid) is not { } vendor)
            {
                continue;
            }

            var held = counters.HeldOf(i);
            if (held < line.Keep)
            {
                list.Add(new ResupplyBuy(line, vendor, held, line.Keep - held));
            }
        }

        return list;
    }

    /// <summary>
    /// The buy list of kit lines (M5 3 F5, D53; D46: the vendor is the profile's <see cref="HuntingGround.VendorSelling"/>, no vendor or ware constant): one ware of each line a home vendor sells, in the order given. A line no vendor sells is added to
    /// <paramref name="unsold"/> and is not on the list: the caller names it and fails, never grants.
    /// </summary>
    /// <param name="ground">The fight's ground.</param>
    /// <param name="lines">The kit lines to buy.</param>
    /// <param name="unsold">Receives the lines no vendor of the profile sells.</param>
    public static IReadOnlyList<ResupplyBuy> KitBuyList(HuntingGround ground, IReadOnlyList<KitLine> lines, List<KitLine> unsold)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(unsold);
        var list = new List<ResupplyBuy>();
        foreach (var line in lines)
        {
            if (line.Slot == KitSlot.Pack)
            {
                // M5 7 G3: the side packs are granted by the build; a kit trip does not buy them again (a trip that buys packs is item 8's, TODO(m5-side-packs-use)).
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
    /// <remarks>At the home (checked by the run against the ground's lifestone landblock: <see cref="StationWhere"/> has no value for a ground's home, <c>TODO(m5-station-where-home)</c>), in peace.</remarks>
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
        $"bought={Bought} spent={Spent} notes_sold={NotesSold} sold={Sold} earned={Earned} vendors={Vendors} refused={_refusals.Count} refusals={(_refusals.Count == 0 ? "none" : string.Join('+', _refusals))} short={Short} not_found={NotFound} loot={(_options.Loot ? "on" : "off")} problems={Problems.Count} granted={Granted} notes_bought={NotesBought} dropped={DroppedItems} vendors_skipped={VendorsSkipped}")
        + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

    /// <inheritdoc />
    protected override IEnumerable<ActionHandle?> Script()
    {
        var ground = Context.Dungeon;
        var counters = new LogisticsCounters(ground);
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
        }

        if (S.State.Self.Position is not { } here || here.CellId >> 16 != ground.Home.LifestoneCell >> 16)
        {
            Fail(string.Create(CultureInfo.InvariantCulture, $"not at the home: the player stands in 0x{S.State.Self.Position?.CellId ?? 0:X8}, the home lifestone of {ground.Home.Town} is in 0x{ground.Home.LifestoneCell:X8}"));
            yield break;
        }

        if (_options.Kit is { } kitLines)
        {
            var unsold = new List<KitLine>();
            _buyList = ResupplyPlanner.KitBuyList(ground, kitLines, unsold);
            if (unsold.Count > 0)
            {
                Fail(Text($"no vendor of {ground.Home.Town} sells the kit line {string.Join(", ", unsold.Select(k => $"{k.Name} (weenie {k.Wcid})"))}: the profile lists none, and nothing is granted (D51)"));
                yield break;
            }

            Log(Text($"resupply: a kit trip (D53): {kitLines.Count} line(s), the stock is not counted and nothing is sold"));
        }
        else
        {
            _buyList = ResupplyPlanner.BuyList(ground, counters);
            if (ground.Learn.Count > 0)
            {
                _buyList = [.. _buyList, .. PlanLearns(ground)]; // M5 7 P7j (D143): a scroll the spellbook needs joins the walk at its vendor
            }

            for (var i = 0; i < counters.LineCount; i++)
            {
                var line = counters.LineAt(i);
                if (counters.IsCarried(i))
                {
                    Log(Text($"resupply: {line.Name} is carried (held {counters.HeldOf(i)}, stop under {line.StopUnder}): no home vendor sells it, never bought here; its stop is the counters'"));
                }
            }
        }

        foreach (var b in _buyList)
        {
            Log(Text($"resupply: {b.Line.Name} held {b.Held} keep {b.Line.Keep}: buy {b.Count} at {b.Vendor.Name}"));
        }

        if (_options.Kit is null && _options.Loot && ground.Loot?.KeepAside is { } keepAside)
        {
            foreach (var h in AppraiseForKeepAside(new KeepAsideRule(keepAside))) { yield return h; }
        }

        if (_options.Gear && _options.Kit is null)
        {
            foreach (var h in GearPhase(ground)) { yield return h; } // M5 7 D121, D122, D125: before the sale, so the main pack holds the loot to sell alone
        }

        var kept = _options.Kit is null ? SaleItems(ground) : [];
        var sellers = kept.Count == 0 ? [] : ResupplyPlanner.LootBuyers(ground, [.. kept.Select(k => k.Object.ItemType)]);
        if (_options.Loot && kept.Count > 0)
        {
            Log(sellers.Count == 0
                ? Text($"sale: no vendor of {ground.Home.Town} buys any of the {kept.Count} pack items: nothing is sold")
                : Text($"sale: {kept.Count} pack items for the loot buyers {string.Join(", ", sellers.Select(b => $"{b.Vendor.Name} ({b.Count}, mask 0x{b.Vendor.BuysItemTypes:X8})"))}"));
        }

        _visits = ResupplyPlanner.Order(_buyList, sellers, ResupplyPlanner.WorldMetres(here.CellId, here.Origin));
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

        // M5 7 G1 (edge audit 1, 12b): a pack nearly full of coin stacks cannot take a payout (ACE refuses the whole sale), so the coin is turned into notes first.
        if (_options.Kit is null && _options.Loot && kept.Count > 0 && MageLootRules.FreeSlots(S.State) < PreNoteFreeSlots)
        {
            foreach (var h in ConvertCoin(ground)) { yield return h; }
        }

        foreach (var visit in _visits)
        {
            foreach (var h in VisitVendor(ground, visit, kept)) { yield return h; }
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

        if (_options.Gear && _options.Kit is null)
        {
            foreach (var h in SortPacks(ground, "after the buys")) { yield return h; } // M5 7 D121: the bought components and kit to their packs (ACE creates a bought ware in the main pack, Player_Commerce.cs 59-102)
            Log("resupply: gear: " + GearFields());
        }

        if (ground.Learn.Count > 0 && _options.Kit is null)
        {
            Log("resupply: learn: " + LearnFields);
        }

        Log("resupply: done: " + VerdictFields());
    }

    /// <summary>Free slots under which the coin is turned into notes before the sale walk.</summary>
    private const int PreNoteFreeSlots = 3;

    /// <summary>One vendor of the walk: met, opened, the loot sold in batches, the lines bought.</summary>
    private IEnumerable<ActionHandle?> VisitVendor(HuntingGround ground, ResupplyVisit visit, List<(uint Guid, WorldObject Object)> kept)
    {
        Stage = "vendor:" + visit.Vendor.Name;
        Log(visit.WalkLine);
        uint guid = 0;
        foreach (var h in Meet(visit.Vendor, g => guid = g)) { yield return h; }

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
            foreach (var h in BuyLine(ground, visit.Vendor, guid, buy)) { yield return h; }
        }
    }

    /// <summary>
    /// M5 7 G1 (edge audit item 1): the trade notes the coin above the purse's coin pays for: the largest note of the purse a profile vendor sells (the den's: Archmage Cindrue's 250,000), as many as the coin and the free slots allow. ACE
    /// sells a promissory note at 1.15 times its face (<c>Vendor.cs</c> 577-585 @95ef426d) and never merges coin stacks, so without this a day's sales fill the pack with 25,000-pyreal stacks.
    /// </summary>
    private int NotesDue(HuntingGround ground)
    {
        // M5 7 D136: the bought notes come as one stack (up to 250) and join the held stack, so one free slot is enough (the buy's own cap is NoteRoom)
        if (_options.Kit is not null || MageLootRules.FreeSlots(S.State) <= 0)
        {
            return 0;
        }

        return PurseNotes.Greedy(Coins() - ground.Supplies.Purse.Coin, NoteOffers(ground)).Picks.Sum(p => p.Count);
    }

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
        var floor = ground.Supplies.Purse.Coin;
        var minNote = ground.Supplies.Purse.MinNote;
        var head = Text($"purse: coin {coin}, floor {floor}, above {Math.Max(0, coin - floor)}, notes of {minNote} and up (a smaller note saves no pack slot and costs 15%): ");
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

        if (MageLootRules.FreeSlots(S.State) <= 0)
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
        Log(Text($"walking: purpose=vendor:{vendor.Name} (coin to notes: {Coins()} pyreals, keep {ground.Supplies.Purse.Coin}, {due} notes)"));
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
        var floor = ground.Supplies.Purse.Coin;
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

            if (MageLootRules.FreeSlots(S.State) <= 0)
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
    private int NoteRoom(VendorStockEntry entry) => (int)Math.Min(int.MaxValue, (long)Math.Max(0, MageLootRules.FreeSlots(S.State)) * entry.MaxStackSize);

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

    /// <summary>
    /// M5 7 G1 (edge audit items 2 and 3): looting on, every main-pack item that is not the kit: not a stock ware, a kit item, one of the four foci, a trade note, money or a container, of a Value of at least 1, not worn or wielded and not
    /// one already dropped. The loot rule's kept list no longer decides, so loot kept before a death, a reconnect or a restart is sold too.
    /// </summary>
    private List<(uint Guid, WorldObject Object)> SaleItems(HuntingGround ground)
    {
        var items = new List<(uint, WorldObject)>();
        if (!_options.Loot)
        {
            Log("sale: looting off (D38): no walk to the loot buyer, sold=0");
            return items;
        }

        var supplies = ground.Supplies;
        var keepAside = ground.Loot?.KeepAside is { } setting ? new KeepAsideRule(setting) : null;
        foreach (var (guid, o) in PackItems())
        {
            var wcid = o.Weenie.WeenieClassId;
            if (IsKeptAside(guid, o, keepAside, out var why))
            {
                Log($"sale: '{o.Name}' 0x{guid:X8} is kept aside ({why}), worth a look: not offered");
                continue;
            }

            if ((o.ItemType & PackRules.ManaStoneType) != 0 && !(PackMemory.Shared.Shed.Contains(guid) && S.TryGetAppraisal(guid, out var stone) && stone.ItemMana is not > 0))
            {
                continue; // M5 7 D139 (d): only an empty stone beyond the keep is sold (ShedStones; Archmage Cindrue's mask buys mana stones); a charged or kept one never
            }

            if (PackMemory.Shared.Rotation.Contains(guid))
            {
                continue; // M5 7 D139 (k): a caster the swap took off is rotation gear wherever it landed (the main pack when the kit pack was full)
            }

            if (_memory.Dropped.Contains(guid) || supplies.StockOf(wcid) is not null || supplies.KitOf(wcid) is not null || supplies.Purse.Notes.Any(n => n.Wcid == wcid) || GearKit.MageFociWcids.Contains(wcid)
                || (o.ItemType & (SellRules.ItemTypeMoney | VendorStock.PromissoryNoteType | ContainerType)) != 0 || (o.Weenie.Value ?? 0) < 1 || (o.Weenie.WielderId is { } wielder && wielder != 0))
            {
                continue;
            }

            items.Add((guid, o));
        }

        return items;
    }

    private const uint ContainerType = 0x200;

    /// <summary>
    /// M5 7 F30 (the owner): true when the sale must not offer the item: the loot step noted its guid (<see cref="SaleMemory.KeptAside"/>), or the keep-aside rule matches the appraisal the session holds of it (so a restart, which empties the ledger,
    /// still protects what an appraisal of the item says is worth a look). <paramref name="why"/> is the bonus or the cantrips, or says the guid was noted.
    /// </summary>
    internal bool IsKeptAside(uint guid, WorldObject item, KeepAsideRule? rule, out string why)
    {
        why = string.Empty;
        if (rule is not null && S.TryGetAppraisal(guid, out var appraisal) && rule.Match(item.ItemType, appraisal, SpellNameOf) is { } matched)
        {
            why = matched;
            _memory.KeptAside.Add(guid);
            return true;
        }

        if (_memory.KeptAside.Contains(guid))
        {
            why = "noted by the loot step";
            return true;
        }

        return false;
    }

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

            foreach (var h in TravelTo(request, Text($"vendor:{vendor.Name}"), optional: true)) { yield return h; }

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
        vendor.StopCell != vendor.Cell
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

    /// <summary>Sells the kept items the loot buyer's mask takes; an item IsSellable marks false (from the wire when an appraisal carries it, else ACE's class list) is left and named, as is one ACE kept.</summary>
    private IEnumerable<ActionHandle?> SellLoot(HuntingGround ground, GroundVendor vendor, uint guid, List<(uint Guid, WorldObject Object)> kept)
    {
        if (S.State.Vendor is not { } stock)
        {
            yield break;
        }

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

            var wire = S.TryGetAppraisal(item, out var appraisal) ? appraisal.Bool(IsSellableBool) : null;
            if (wire == false)
            {
                Refuse("unsellable", Text($"{o.Name} is marked unsellable on the wire (IsSellable=0): left in the pack"));
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
        var free = MageLootRules.FreeSlots(S.State);
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
            free = MageLootRules.FreeSlots(S.State);
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
            Log(Text($"sale: {waiting} of {offer.Count} items wait: their payout needs more coin stacks than the {MageLootRules.FreeSlots(S.State)} free slots hold (ACE refuses the whole sale then, Player_Commerce.cs 176-191); {batch.Count} go in this batch ({batchPayout} pyreals)"));
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
        if (S.State.Vendor is not { } stock || stock.Vendor != guid)
        {
            Refuse("open", Text($"{name}: {vendor.Name}'s stock is no longer open"));
            yield break;
        }

        var entry = stock.Entries.FirstOrDefault(e => e.WeenieClassId == buy.Line.Wcid);
        if (entry is null)
        {
            Refuse("stock", Text($"{name} ({buy.Line.Wcid}) is not in {vendor.Name}'s 0062 ({stock.Entries.Length} rows)"));
            yield break;
        }

        if (PriceNote(name, vendor.Name, stock.PriceToBuy(entry, 1), ground.PriceOf(buy.Line.Wcid)) is { } note)
        {
            Log(note);
        }

        var count = buy.Count;
        if (!entry.IsUnlimited)
        {
            if (entry.Stack <= 0)
            {
                Refuse("stock", Text($"{name}: {vendor.Name} is out of stock (his 0062 lists 0)"));
                yield break;
            }

            if (count > entry.Stack)
            {
                Log(Text($"resupply: {name}: {vendor.Name} holds {entry.Stack}, fewer than the {count} to buy: buys {entry.Stack}"));
                count = entry.Stack;
            }
        }

        if (MageLootRules.FreeSlots(S.State) == 0)
        {
            Refuse("pack", Text($"{name}: the main pack is full (0 free slots): nothing is bought"));
            yield break;
        }

        var cost = (long)stock.PriceToBuy(entry, (uint)count);
        while (cost > Coins() && stock.BuysType(VendorStock.PromissoryNoteType) && NextNote(ground, cost - Coins()) is { } held)
        {
            var changed = false;
            foreach (var h in SellNote(vendor, guid, held, ok => changed = ok)) { yield return h; }

            if (!changed || S.State.Vendor is not { } refreshed || refreshed.Vendor != guid)
            {
                break;
            }

            stock = refreshed;
        }

        var coins = Coins();
        if (cost > coins)
        {
            var affordable = Affordable(stock, entry, count, coins);
            Short++;
            Log(Text($"purse: short by {cost - coins} pyreals for {name} (the {count} cost {cost}, the coin is {coins} and no note is left): buys {affordable}"));
            count = affordable;
            if (count == 0)
            {
                yield break;
            }
        }

        var before = Coins();
        var heldBefore = GearInventory.CountOf(S.State, new List<uint>(), buy.Line.Wcid);
        var handle = S.Actions.Buy(guid, [(entry.Guid, (uint)count)], ActionSource.Automation);
        yield return handle;
        if (handle.Outcome is not { Kind: ActionOutcomeKind.Confirmed })
        {
            Refuse(Kind(handle.Outcome?.Detail, "other"), Text($"{name} x{count} at {vendor.Name}: {Describe(handle)}"));
            yield break;
        }

        var paid = handle.Result?.Actual ?? Math.Max(0, before - Coins());
        Bought += count;
        Spent += paid;
        Log(Text($"resupply: bought {count} {name} at {vendor.Name} for {paid} pyreals"));
        foreach (var h in MergeBought(buy.Line.Wcid, heldBefore + count, name)) { yield return h; }

        if (ground.Learn.Any(l => l.ScrollWcid == buy.Line.Wcid))
        {
            ScrollsBought += count;
            Log(Text($"learn: bought the scroll {name} (weenie {buy.Line.Wcid}) at {vendor.Name} for {paid} pyreals; scrolls of it in the pack {InPack(buy.Line.Wcid).Count}: read after the walk"));
        }
        else if ((entry.ItemType & WritableType) != 0)
        {
            Log(Text($"TODO(m6-scroll-read): {name} is a scroll (ItemType 0x{entry.ItemType:X}) no learn line of the ground names; it is not read"));
        }
    }

    /// <summary>The stack merges the server confirmed after a purchase (M5 7).</summary>
    public int StackMerges { get; private set; }

    /// <summary>The post-purchase merges that were refused or got no answer (M5 7).</summary>
    public int MergesRefused { get; private set; }

    /// <summary>
    /// M5 7: ACE never merges a bought stack into a held one (<c>Player_Commerce.cs</c> 59-102 <c>FinalizeBuyTransaction</c> creates each ware as a new object), so every purchase of a stackable (spell components, potions) would take new
    /// pack slots. Waits up to 3 s for the bought items to be in the model (<paramref name="expectedTotal"/> held in all), then merges the smallest held stack of the weenie into the fullest other one with room (<see cref="StackMerge"/>,
    /// GameAction 0x0054) until none is left to merge; a refused merge is logged, counted and that stack is left. A ware that does not stack does nothing. M5 7 D136: the trade notes and the coin pass <paramref name="mainPackTarget"/>, so
    /// they join only a stack in the main pack (D121).
    /// </summary>
    private IEnumerable<ActionHandle?> MergeBought(uint wcid, int expectedTotal, string name, bool mainPackTarget = false)
    {
        var scratch = new List<uint>();
        foreach (var h in Until(() => GearInventory.CountOf(S.State, scratch, wcid) >= expectedTotal, 3)) { yield return h; }

        var skip = new HashSet<uint>();
        for (var round = 0; round < 12 && StackMerge.TryPlanFor(S.State, wcid, scratch, skip, out var from, out var to, out var amount, mainPackTarget); round++)
        {
            Log(Text($"resupply: merging a {(expectedTotal > 0 ? "bought" : "held")} {name} stack 0x{from:X8} into 0x{to:X8}: x{amount}"));
            var merge = S.Actions.MergeStack(from, to, amount, ActionSource.Automation);
            yield return merge;
            if (merge.Outcome is { Kind: ActionOutcomeKind.Confirmed })
            {
                StackMerges++;
                continue;
            }

            MergesRefused++;
            skip.Add(from);
            Log(Text($"resupply: the merge of {name} 0x{from:X8} into 0x{to:X8} ended {Describe(merge)}; counted, the stack stays, going on"));
        }
    }

    /// <summary>The held trade note to change for <paramref name="shortfall"/> pyreals: the smallest whose face covers it, else the largest; null when the pack holds none of the purse's notes.</summary>
    private (uint Guid, PurseNote Note)? NextNote(HuntingGround ground, long shortfall)
    {
        var held = new List<(uint Guid, PurseNote Note)>();
        foreach (var note in ground.Supplies.Purse.Notes)
        {
            held.AddRange(InPack(note.Wcid).Select(g => (g, note)));
        }

        if (held.Count == 0)
        {
            return null;
        }

        var covering = held.Where(h => h.Note.Value >= shortfall).OrderBy(h => h.Note.Value).ToList();
        return covering.Count > 0 ? covering[0] : held.MaxBy(h => h.Note.Value);
    }

    /// <summary>
    /// Changes one trade note to coin at the open vendor (D49: a vendor pays a promissory note at 1.0). ACE's <c>Vendor.GetBuyCost</c> (<c>Vendor.cs</c> 591-599 @95ef426d) pays <c>max(1, floor(rate x Value + 0.1))</c> with the rate the
    /// vendor's BuyPrice (1 when absent) and exactly 1.0 for a promissory note (593-595), so a note's face value is its payout whatever the vendor's own rate.
    /// <para>M5 7 D136: ACE sells the whole object it is handed, whatever the amount (<c>Player_Commerce.cs</c> 232-298 <c>VerifySellItems</c> only checks the amount against the stack; 199-205 removes the item; <c>Vendor.cs</c> 586-609
    /// pays the stack's whole Value), so a note from a stack of several is first split off into the main pack with GameAction 0x0055 (<see cref="IActionApi.SplitToContainer"/>; ACE
    /// <c>Player_Inventory.cs</c> 2243 <c>HandleActionStackableSplitToContainer</c>) and that one note is sold; the rest of the stack stays.</para>
    /// </summary>
    private IEnumerable<ActionHandle?> SellNote(GroundVendor vendor, uint guid, (uint Guid, PurseNote Note) held, Action<bool> done)
    {
        var one = held.Guid;
        var size = S.State.Objects.TryGet(held.Guid, out var stack) ? GearInventory.StackOf(S.State, stack) : 1;
        if (size > 1)
        {
            if (MageLootRules.FreeSlots(S.State) <= 0)
            {
                Refuse("pack", Text($"the trade note {held.Note.Name}: one note of the stack 0x{held.Guid:X8} (x{size}) cannot be split off: the main pack has no free slot"));
                done(false);
                yield break;
            }

            var others = InPack(held.Note.Wcid);
            var split = S.Actions.SplitToContainer(held.Guid, 0, 0, 1, ActionSource.Automation);
            yield return split;
            foreach (var h in Until(() => InPack(held.Note.Wcid).Except(others).Any(), 2)) { yield return h; } // ACE's 0022 confirms the split; the new stack's F745 follows it (the tracker's note)

            one = InPack(held.Note.Wcid).Except(others).FirstOrDefault();
            if (split.Outcome is not { Kind: ActionOutcomeKind.Confirmed } || one == 0)
            {
                Refuse(Kind(split.Outcome?.Detail, "other"), Text($"the trade note {held.Note.Name}: the split of one from 0x{held.Guid:X8} (x{size}) {Describe(split)}"));
                done(false);
                yield break;
            }

            Log(Text($"purse: split one {held.Note.Name} 0x{one:X8} from its stack 0x{held.Guid:X8} (x{size}) to change it"));
        }

        var before = Coins();
        var sell = S.Actions.Sell(guid, [one], ActionSource.Automation);
        yield return sell;
        foreach (var h in Until(() => Coins() > before, 2)) { yield return h; }

        if (sell.Outcome is not { Kind: ActionOutcomeKind.Confirmed } || S.State.Self.Inventory.Contains(one))
        {
            Refuse(Kind(sell.Outcome?.Detail, "other"), Text($"the trade note {held.Note.Name} at {vendor.Name}: {Describe(sell)}"));
            done(false);
            yield break;
        }

        NotesSold++;
        Log(Text($"purse: changed {held.Note.Name} to coin at {vendor.Name}: paid {sell.Result?.Actual ?? (Coins() - before)} pyreals (the coin {before} -> {Coins()})"));
        done(true);
    }

    /// <summary>The most of the row the coin pays for (the price is chunked by stack, so it is searched, not divided).</summary>
    private static int Affordable(VendorStock stock, VendorStockEntry entry, int count, long coins)
    {
        int lo = 0, hi = count;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (stock.PriceToBuy(entry, (uint)mid) <= coins)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

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
