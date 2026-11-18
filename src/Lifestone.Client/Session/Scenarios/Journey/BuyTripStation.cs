using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>What <see cref="BuyTripRule.Decide"/> says about the buy-trip.</summary>
public enum BuyTripKind
{
    /// <summary>No carried line the plan casts is under its stop line: nothing to buy.</summary>
    NotDue,

    /// <summary>A carried line the plan casts is under its stop line and a catalog archmage in a known town sells it: the trip is due.</summary>
    Due,

    /// <summary>The trip cannot be judged or made (the plan's level unread, no catalog seller in a known town): a named stop.</summary>
    Stop,
}

/// <summary>
/// The buy-trip's answer (M6 item 1 d, D12): the kind, a token (kebab case, the verdict's <c>reason=</c>), the reason in words, and for a due trip the line, the count held, the count to buy (<c>keep - held</c>), the first spell level that burns
/// the line (the ground's components), the plan's level, the seller, his place, the town's arrival (the lifestone of the ground whose home is that town) and the catalog's price of one.
/// </summary>
/// <param name="Kind">Not due, due or stop.</param>
/// <param name="Token">The reason as a token.</param>
/// <param name="Why">The reason in words, with every number.</param>
/// <param name="Line">The carried stock line, or null.</param>
/// <param name="Held">The count held.</param>
/// <param name="Want">The count to buy (<see cref="StockLine.Keep"/> less <paramref name="Held"/>).</param>
/// <param name="FirstLevel">The first spell level whose components burn the line (the ground's <see cref="HuntingGround.Components"/>), or 0.</param>
/// <param name="PlanLevel">The plan's spell level (<see cref="SpellPlan.LevelAt(uint, double)"/> on the current War Magic at the ladder's chance, D4), or 0.</param>
/// <param name="Seller">The archmage, or null.</param>
/// <param name="Place">His place, or null.</param>
/// <param name="Arrival">The town's arrival: the home of the ground whose home town is his, or null.</param>
/// <param name="CatalogPrice">His price of one by the catalog (<see cref="ScarabSeller.PriceOf"/>), or 0.</param>
public sealed record BuyTripDecision(
    BuyTripKind Kind,
    string Token,
    string Why,
    StockLine? Line = null,
    int Held = 0,
    int Want = 0,
    int FirstLevel = 0,
    int PlanLevel = 0,
    ScarabSeller? Seller = null,
    ScrivenerPlace? Place = null,
    GroundHome? Arrival = null,
    int CatalogPrice = 0);

/// <summary>
/// M6 item 1 d (D12, D4, D40): the buy-trip's rule, pure. A carried stock line (<see cref="HuntingGround.IsCarried"/>: no vendor of the ground sells it) that is not floor-only (<see cref="ComponentFloor.FloorOnly"/>: the Diamond Scarab's ring is put
/// away at its floor, D12) and is under its <c>stop_under</c> is due for the trip when the plan casts it: the plan's level (the highest of the war line cast at the ladder's <see cref="Ladder.PlanCastChance"/> on the current War Magic, D4) is at
/// least the first spell level whose components burn the line (the ground's <see cref="HuntingGround.Components"/>: the Pyreal Scarab from VI, the Platinum Scarab from VII), never a constant. The archmage is the catalog's
/// (<see cref="ScrivenerCatalog.ScarabSellers"/>, D28): the first who lists the scarab and stands in a town some ground calls home (the town's arrival is that ground's lifestone: Zisaha of Fort Tethana, D40).
/// </summary>
public static class BuyTripRule
{
    /// <summary>The first spell level whose components burn <paramref name="wcid"/> by the ground's <see cref="HuntingGround.Components"/>, or null when none names it.</summary>
    /// <param name="ground">The ground.</param>
    /// <param name="wcid">The component's weenie class id.</param>
    public static int? FirstLevelBurning(HuntingGround ground, uint wcid)
    {
        ArgumentNullException.ThrowIfNull(ground);
        int? first = null;
        foreach (var c in ground.Components)
        {
            if (c.Wcid == wcid && (first is null || c.SpellLevel < first))
            {
                first = c.SpellLevel;
            }
        }

        return first;
    }

    /// <summary>The plan's spell level (D4): the highest level of the war line whose chance on the CURRENT War Magic (<see cref="GearCasts.TryCurrentSkill"/>, the buffs counted) is at least the ladder's chance; null with the reason when unread.</summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="ladder">The ladder (its <see cref="Ladder.PlanCastChance"/>).</param>
    /// <param name="why">The numbers, or why the level is unread.</param>
    public static int? PlanLevel(PlayerQualities q, Ladder ladder, out string why)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(ladder);
        if (!q.IsKnown)
        {
            why = "the qualities are not read yet";
            return null;
        }

        if (!GearCasts.TryCurrentSkill(q, MageTemplate.WarMagic, out var skill, out var noSkill))
        {
            why = "War Magic: " + noSkill;
            return null;
        }

        var level = SpellPlan.LevelAt(skill, ladder.PlanCastChance);
        why = string.Create(CultureInfo.InvariantCulture, $"War Magic {skill} casts level {Numeral(level)} at the ladder's chance {ladder.PlanCastChance:0.00}");
        return level;
    }

    /// <summary>The first catalog seller of <paramref name="wcid"/> whose town some ground calls home, with his place and that town's arrival; null with the towns that have no arrival.</summary>
    /// <param name="wcid">The scarab's weenie class id.</param>
    /// <param name="catalog">The catalog.</param>
    /// <param name="grounds">The grounds (their homes are the towns' arrivals).</param>
    /// <param name="why">Who was chosen, or why none was.</param>
    public static (ScarabSeller Seller, ScrivenerPlace Place, GroundHome Arrival)? SellerFor(uint wcid, ScrivenerCatalog catalog, IReadOnlyList<HuntingGround> grounds, out string why)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(grounds);
        var sellers = catalog.ScarabSellersOf(wcid);
        var unknown = new List<string>();
        foreach (var seller in sellers)
        {
            foreach (var place in seller.Places)
            {
                if (grounds.FirstOrDefault(g => string.Equals(g.Home.Town, place.Town, StringComparison.Ordinal)) is { } home)
                {
                    why = string.Create(CultureInfo.InvariantCulture, $"{seller.Name} ({seller.Wcid}) in {place.Town}, the home of the ground '{home.Key}' (the arrival: its lifestone 0x{home.Home.LifestoneCell:X8})");
                    return (seller, place, home.Home);
                }

                unknown.Add(place.Town);
            }
        }

        why = sellers.Count == 0
            ? string.Create(CultureInfo.InvariantCulture, $"no catalog seller lists the weenie {wcid}")
            : $"the catalog's sellers stand in {string.Join(", ", unknown.Distinct(StringComparer.Ordinal))}, none of them a ground's home town (no arrival known)";
        return null;
    }

    /// <summary>
    /// The trip's answer over the counters' last count (<see cref="LogisticsCounters.Check"/> must have read the pack): the first carried, not floor-only line under its stop line that the plan casts is due; a carried line under its stop
    /// line the plan does not cast yet (or the ground's components never name) is not due, named; with such a line, an unread plan level or no seller in a known town is a stop by name.
    /// </summary>
    /// <param name="ground">The ground (its components and stock).</param>
    /// <param name="counters">The counters, checked.</param>
    /// <param name="planLevel">The plan's level (<see cref="PlanLevel"/>), or null when unread.</param>
    /// <param name="planWhy">The plan level's reason.</param>
    /// <param name="catalog">The catalog.</param>
    /// <param name="grounds">The grounds.</param>
    public static BuyTripDecision Decide(HuntingGround ground, LogisticsCounters counters, int? planLevel, string planWhy, ScrivenerCatalog catalog, IReadOnlyList<HuntingGround> grounds)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(counters);
        BuyTripDecision? notDue = null;
        var c = CultureInfo.InvariantCulture;
        for (var i = 0; i < counters.LineCount; i++)
        {
            var line = counters.LineAt(i);
            var held = counters.HeldOf(i);
            if (!counters.IsCarried(i) || counters.IsFloorOnly(i) || held >= line.StopUnder)
            {
                continue;
            }

            var under = string.Create(c, $"{line.Name}: {held} held, under its stop line {line.StopUnder}");
            if (FirstLevelBurning(ground, line.Wcid) is not { } first)
            {
                notDue ??= new BuyTripDecision(BuyTripKind.NotDue, "unplanned:" + Word(line.Name), $"{under}; the components of '{ground.Key}' name no spell level that burns it, so the plan never makes it due", line, held);
                continue;
            }

            if (planLevel is not { } plan)
            {
                return new BuyTripDecision(BuyTripKind.Stop, "plan-unread", $"{under}; the plan's level is unread ({planWhy})", line, held, FirstLevel: first);
            }

            if (plan < first)
            {
                notDue ??= new BuyTripDecision(BuyTripKind.NotDue, "plan-below:" + Word(line.Name), string.Create(c, $"{under}; the plan casts level {Numeral(plan)} ({planWhy}) and the first level that burns it is {Numeral(first)}"), line, held, FirstLevel: first, PlanLevel: plan);
                continue;
            }

            if (SellerFor(line.Wcid, catalog, grounds, out var sellerWhy) is not { } s)
            {
                return new BuyTripDecision(BuyTripKind.Stop, "no-seller:" + Word(line.Name), $"{under}; {sellerWhy} (TODO(m6-buy-trip-seller))", line, held, FirstLevel: first, PlanLevel: plan);
            }

            var price = s.Seller.PriceOf(s.Seller.SaleOf(line.Wcid)!.Value);
            var want = Math.Max(0, line.Keep - held);
            return new BuyTripDecision(
                BuyTripKind.Due,
                "due:" + Word(line.Name),
                string.Create(c, $"{under}; the plan casts level {Numeral(plan)} ({planWhy}), which burns it from {Numeral(first)}; buy {want} to its keep {line.Keep} at {sellerWhy}, {price} pyreals each by the catalog"),
                line,
                held,
                want,
                first,
                plan,
                s.Seller,
                s.Place,
                s.Arrival,
                price);
        }

        return notDue ?? new BuyTripDecision(BuyTripKind.NotDue, "none", "no carried line is under its stop line");
    }

    /// <summary>The spell level as a numeral (I to VIII), or <c>none</c>.</summary>
    /// <param name="level">The level.</param>
    public static string Numeral(int level) => level switch
    {
        >= 1 and <= 6 => ScrivenerLearnPlan.Numeral(level),
        7 => "VII",
        8 => "VIII",
        _ => "none",
    };

    /// <summary>A name as one word of a line (spaces as underscores).</summary>
    /// <param name="name">The name.</param>
    public static string Word(string name) => (name ?? string.Empty).Replace(' ', '_');

    /// <summary>
    /// The note a logistics stop on a carried line carries (M6 item 1 d replaces <c>TODO(m5-platinum-stock)</c> with the station; the stop stays a named stop for a town the audit cannot reach or a trip already tried; <c>m6-buy-trip-autopilot</c> is closed, M6 4 t4b, D44): <c>buy-trip due: ...</c> naming the scarab, the count, the archmage and the town (and, for the climber away from that town, the
    /// Town Network hop the station takes, M6 4 t4a), else the reason it is not due.
    /// </summary>
    /// <param name="d">The decision.</param>
    /// <param name="noTeleportBudget">True when the journey has no teleport budget (<see cref="BuyTripStation.NoTeleportBudget"/>; the climber, D40).</param>
    public static string StopNote(BuyTripDecision d, bool noTeleportBudget)
    {
        ArgumentNullException.ThrowIfNull(d);
        return d.Kind == BuyTripKind.Due
            ? string.Create(CultureInfo.InvariantCulture, $"buy-trip due: {d.Line!.Name} x{d.Want} at {d.Seller!.Name} ({d.Seller.Wcid}) in {d.Place!.Town}; the station {StationKinds.BuyTrip} makes it (D12)")
                + (noTeleportBudget ? $"; with no teleport budget the way to {d.Place.Town} is the Town Network hop (travel={TownNetworkTrip.TravelWord}, D40)" : string.Empty)
                + "; the autopilot's audit queues the station when the seller's town is reachable (M6 4 t4b, D44), so this stop stands only when it was not or the trip was already tried"
            : $"buy-trip {(d.Kind == BuyTripKind.Stop ? "stopped" : "not due")}: {d.Why}";
    }
}

/// <summary>The session's memory of the buy-trip's loud stops (D40: the climber's Town Network stop is logged once a session for each town and scarab).</summary>
public sealed class BuyTripMemory
{
    private readonly HashSet<string> _logged = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>The session's memory.</summary>
    public static BuyTripMemory Shared { get; } = new();

    /// <summary>True the first time <paramref name="key"/> is asked this session.</summary>
    /// <param name="key">The stop's key (the town and the scarab).</param>
    public bool First(string key)
    {
        lock (_gate)
        {
            return _logged.Add(key);
        }
    }
}

/// <summary>
/// The buy-trip station (M6 item 1 d, D12, D40): a journey to the archmage who sells a carried-only scarab. It reads the counters over the journey's ground (<see cref="LogisticsCounters"/>, the floor-only lines as the logistics station reads
/// them), decides by <see cref="BuyTripRule.Decide"/> and, when the trip is due, makes it with check, act, check (M5 D141):
/// <list type="number">
/// <item><b>The way there.</b> A character with no teleport budget (the climber, D40) away from the town goes by the Town Network (M6 4 t4a, closes <c>TODO(m6-buy-trip-town-network)</c>, <c>travel=town-network</c>): the seller is chosen
/// among the catalog's sellers of the scarab by <see cref="TownNetworkTrip.Choose"/> (on the travel file, no walk past a lever-only door, the nearest to his town's drop: Zisaha of Fort Tethana, whose walk from the drop stays clear of the
/// fort's lever gate, TownNetworkTripDatTests), then the hops (<see cref="PortalHopLeg"/>, each its own line) and the walk from the drop to him; no seller admitted is the stop <c>buy-trip:town-network</c> naming
/// <c>TODO(door-lever)</c> or the refusals, logged once a session. A built character (D10) reaches the town by the journey's one budgeted teleport (<see cref="TravelPurpose.Ground"/> to the seller's place,
/// <c>travel=budgeted-teleport</c>); a mage already in the town walks (<c>travel=none</c>). Before it leaves, the purse must cover the count at the catalog's price: the coin, and (M6 16 E5b) the faces of the trade notes of the
/// main pack unless a ground profile's mask says the seller's counter takes none.</item>
/// <item><b>At the archmage:</b> before, he is in reach (the walk by <see cref="TravelPurpose.Vendor"/>, the live distance against his reach), his shop opens and lists the scarab, and the purse covers the count at his wire price (the coin, and the trade notes of the main
/// pack when his mask takes one: M6 16 E5b, the vendor counter's buy, <see cref="VendorCounter.Buy"/>, which changes a note at the counter when the coin is short); the act,
/// <see cref="IActionApi.Buy"/> for the count that fills the line to its keep; after, the pack's count rose by that count and the coin fell. A refusal or a short purse is a named stop, never retried.</item>
/// <item><b>The way back</b> by the seam (<see cref="TravelPurpose.Home"/>, Lifestone Recall to the ground's home) when the book holds it, else (a trip by the Town Network) the rung change's <c>direction=home</c> by portals, unless the
/// spec says <see cref="NoBackArgument"/> (the loopback's trip ends at the archmage).</item>
/// </list>
/// One line: <c>buy_trip: scarab= town= vendor= want= bought= price= purse_before= purse_after= travel= ok= reason=</c>.
/// </summary>
/// <param name="back">False to end the trip at the archmage (<see cref="NoBackArgument"/>).</param>
/// <param name="accountName">The account's login name, for the floor-only lines on the account's formula (as <see cref="LogisticsStation"/>'s), or null.</param>
/// <param name="memory">The loud stops' memory (null: <see cref="BuyTripMemory.Shared"/>).</param>
public sealed class BuyTripStation(bool back = true, string? accountName = null, BuyTripMemory? memory = null) : IStation
{
    /// <summary>The station's name.</summary>
    public const string StationName = "buy-trip";

    /// <summary>The spec argument that ends the trip at the archmage (no way back).</summary>
    public const string NoBackArgument = "no-back";

    /// <summary>The line's prefix.</summary>
    public const string LinePrefix = "buy_trip:";

    /// <summary>The seconds the pack and the purse have to show the purchase after the buy's UseDone.</summary>
    public const double AfterSeconds = 5;

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    /// <remarks>In peace (a shop needs it), with the mana for the recall back.</remarks>
    public StationNeeds Needs { get; } = new(Peace: true, MinManaPercent: 20);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Run(context, back, accountName, memory ?? BuyTripMemory.Shared);
    }

    /// <summary>
    /// M6 1 fix (1-R13, D40): true when the journey has no teleport budget (a cheat-free character, the climber, takes no mod teleport; the rule, not the character's name). Such a character away from the seller's town goes by the Town
    /// Network hop (M6 4 t4a).
    /// </summary>
    /// <param name="facts">The journey's facts.</param>
    public static bool NoTeleportBudget(JourneyFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts.TeleportBudget <= 0;
    }

    /// <summary>
    /// M6 1 fix (1-R13): the landblocks of a town, as the catalog knows it: every place the catalog lists in <paramref name="town"/> (the scriveners' and the scarab sellers': Fort Tethana's Zisaha stands in 0x2481, its scrivener in 0x2581) and
    /// the town's arrival landblock (the lifestone's, 0x2780): one town, three landblocks.
    /// </summary>
    /// <param name="town">The town's name.</param>
    /// <param name="arrival">The town's arrival (the lifestone of the ground whose home it is).</param>
    /// <param name="catalog">The scrivener catalog.</param>
    internal static HashSet<uint> TownLandblocks(string town, GroundHome arrival, ScrivenerCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(arrival);
        ArgumentNullException.ThrowIfNull(catalog);
        var blocks = new HashSet<uint> { arrival.LifestoneCell >> 16 };
        foreach (var place in catalog.Scriveners.SelectMany(s => s.Places).Concat(catalog.ScarabSellers.SelectMany(s => s.Places)))
        {
            if (string.Equals(place.Town, town, StringComparison.Ordinal))
            {
                blocks.Add(place.Cell >> 16);
            }
        }

        return blocks;
    }

    private sealed class Run(JourneyContext context, bool back, string? accountName, BuyTripMemory memory) : StationLeg(StationName, context)
    {
        private BuyTripDecision? _decision;
        private int _bought;
        private uint _price;
        private long _purseBefore = -1;
        private long _purseAfter = -1;
        private string _travel = "none";
        private string _reason = "none";
        private bool _lined;

        /// <inheritdoc />
        public override string VerdictFields() => Fields() + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

        /// <summary>The line's fields.</summary>
        private string Fields()
        {
            var d = _decision;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"scarab={(d?.Line is { } l ? BuyTripRule.Word(l.Name) : "none")} town={(d?.Place is { } p ? BuyTripRule.Word(p.Town) : "none")} vendor={d?.Seller?.Wcid ?? 0} want={d?.Want ?? 0} bought={_bought} price={(_price > 0 ? _price : (uint)(d?.CatalogPrice ?? 0))} purse_before={_purseBefore} purse_after={_purseAfter} travel={_travel} ok={(Failure is null ? "yes" : "no")} reason={_reason}");
        }

        private IReadOnlyDictionary<uint, IReadOnlyList<string>> FloorOnlyLines(HuntingGround ground)
        {
            if (string.IsNullOrEmpty(accountName))
            {
                return ComponentFloor.FloorOnly(ground, Context.Spells);
            }

            var held = GearInventory.CountAll(S.State, out _);
            return ComponentFloor.FloorOnly(ground, Context.Spells, accountName, school => AccountFormula.HoldsFocus(held, school));
        }

        protected override IEnumerable<ActionHandle?> Script()
        {
            var ground = Context.Dungeon;
            try
            {
                Stage = "count";
                var counters = TripDesk.For(S.State).On(ground, FloorOnlyLines(ground).Keys, null, Log); // M6 16 E5d: the session's one set of counters
                foreach (var h in Until(() => counters.Check(S.State).Kind != LogisticsKind.Unread, NpcSeconds)) { yield return h; }

                var verdict = counters.Check(S.State);
                if (verdict.Kind == LogisticsKind.Unread)
                {
                    Fail("buy-trip:counters-unread", $"the counters did not read within {NpcSeconds:0} s: {verdict.Reason}");
                    yield break;
                }

                var plan = BuyTripRule.PlanLevel(S.State.Self.Qualities, LadderLoader.WarMage, out var planWhy);
                var d = BuyTripRule.Decide(ground, counters, plan, planWhy, ScrivenerCatalog.Default, GroundCatalog.All);
                _decision = d;
                Log($"buy-trip: decide: {d.Kind} {d.Token}: {d.Why}");
                if (d.Kind == BuyTripKind.NotDue)
                {
                    _reason = "not-due:" + d.Token;
                    Log($"buy-trip: no trip: {d.Why}");
                    yield break;
                }

                if (d.Kind == BuyTripKind.Stop)
                {
                    Fail("buy-trip:" + d.Token, d.Why);
                    yield break;
                }

                var line = d.Line!;
                var seller = d.Seller!;
                var place = d.Place!;
                var arrival = d.Arrival!;
                _purseBefore = Coins();
                var inTown = InTown(place, arrival);
                Log(string.Create(CultureInfo.InvariantCulture, $"buy-trip: check before: {line.Name} {d.Held} held, want {d.Want}; the purse {_purseBefore} pyreals; the player {(inTown ? "is in" : "is away from")} {place.Town} ({Where()})"));
                TownTripPlan? hop = null;
                if (!inTown && NoTeleportBudget(Context.Facts))
                {
                    // M6 4 t4a (closes TODO(m6-buy-trip-town-network), D40): the climber goes by the Town Network hop to a catalog seller the rule admits.
                    _travel = TownNetworkTrip.TravelWord;
                    var candidates = ScrivenerCatalog.Default.ScarabSellersOf(line.Wcid).SelectMany(s => s.Places.Select(p => new TownTripCandidate(p.Town, s.Name, s.Wcid, p))).ToList();
                    var choice = TownNetworkTrip.Choose(candidates, TownInput(ground), Context.Routes, LeverDoors.Default);
                    Log($"buy-trip: by the Town Network (no teleport budget, D40): {choice.Why}");
                    if (choice.Chosen is not { } chosen)
                    {
                        if (memory.First(place.Town + "|" + line.Name))
                        {
                            Context.Log.Warn("station", $"[live {Name}] {choice.Stop}: no seller of the {line.Name} is admitted for a trip by the Town Network: {choice.Why}; logged once a session");
                        }

                        Fail("buy-trip:town-network", $"{choice.Stop}: no seller of the {line.Name} is admitted for a trip by the Town Network ({choice.Why})");
                        yield break;
                    }

                    hop = chosen;
                    if (chosen.Candidate.Wcid != seller.Wcid || chosen.Candidate.Vendor.Guid != place.Guid)
                    {
                        seller = ScrivenerCatalog.Default.ScarabSellers.First(s => s.Wcid == chosen.Candidate.Wcid);
                        place = chosen.Candidate.Vendor;
                        d = d with { Seller = seller, Place = place, CatalogPrice = seller.PriceOf(seller.SaleOf(line.Wcid)!.Value) };
                        _decision = d;
                        Log($"buy-trip: the seller is {seller.Name} ({seller.Wcid}) in {place.Town} (the trip's choice), {d.CatalogPrice} pyreals each by the catalog");
                    }
                }

                if (_purseBefore < 0)
                {
                    Fail("buy-trip:purse-unread", "the coin (CoinValue) is not read");
                    yield break;
                }

                var need = (long)d.CatalogPrice * d.Want;
                if (_purseBefore < need)
                {
                    // M6 16 E5b (closes TODO(m6-buy-trip-notes)): the buy is the vendor counter's, which changes a trade note she holds when the coin is short (M6 16 E5a2: a side pack's too), so the purse is the coin and the notes' faces, when the seller's counter takes a note.
                    var notes = PurseView.NotesCounterCanChange(S.State);
                    var mask = NoteMaskOf(seller.Wcid);
                    var uncovered = string.Create(CultureInfo.InvariantCulture, $"the purse {_purseBefore} does not cover {d.Want} {line.Name} at {seller.Name}'s {d.CatalogPrice} each ({need}) by the catalog; nothing is bought, never retried");
                    if (notes <= 0)
                    {
                        Fail("buy-trip:purse-short", uncovered + " (no trade note is held in the main pack to change)");
                        yield break;
                    }

                    if (_purseBefore + notes < need)
                    {
                        Fail("buy-trip:purse-short", uncovered + string.Create(CultureInfo.InvariantCulture, $" (the trade notes held, {notes}, do not cover the rest)"));
                        yield break;
                    }

                    if (mask is { } known && (known & VendorStock.PromissoryNoteType) == 0)
                    {
                        Fail("buy-trip:purse-short", uncovered + string.Create(CultureInfo.InvariantCulture, $" ({seller.Name}'s mask 0x{known:X8} takes no trade note, so the {notes} held in notes are not changed at that counter)"));
                        yield break;
                    }

                    Log(string.Create(CultureInfo.InvariantCulture, $"buy-trip: the coin {_purseBefore} is short of {need}; the trade notes held ({notes}) cover the rest: a note is changed at {seller.Name}'s counter before the buy")
                        + (mask is { } takes
                            ? string.Create(CultureInfo.InvariantCulture, $" (the mask 0x{takes:X8} takes a trade note, by the ground profiles)")
                            : " (TODO(m6-buy-trip-note-mask): no ground profile gives this seller's mask, so whether the counter takes a note is read from the wire there; a mask that takes none is the stop purse-short at the counter)"));
                }

                if (hop is { } trip)
                {
                    Stage = "travel";
                    foreach (var h in HopOut(trip, line.Name)) { yield return h; }

                    if (Failure is not null)
                    {
                        yield break;
                    }
                }
                else if (!inTown)
                {
                    Stage = "travel";
                    _travel = "budgeted-teleport";
                    var teleports = Context.Facts.Teleports;
                    // M6 1 L2 (live, 2026-11-02): the teleport lands at the seller's own place, not the town's lifestone: Fort Tethana's lifestone (0x2780) is two landblocks from Zisaha's room
                    // (0x2481). Since M6 4 W1 a leg from the lifestone plans to her room too (horizons until her landblock's tile attaches, GoalTileDatTests); the built character keeps the landing at her place.
                    Log($"walking: purpose=ground (buy-trip: the journey's one budgeted teleport to {seller.Name} in {place.Town}, 0x{place.Cell:X8} (her own place, M6 1 L2; a walk from the town's arrival 0x{arrival.LifestoneCell:X8} also plans since M6 4 W1), for the {line.Name}; D10, D40: the built character's way, the climber's is the Town Network hop)");
                    foreach (var h in TravelTo(new TravelRequest(Destination("buy-trip-town:" + BuyTripRule.Word(place.Town), place.Cell, place.Point), TravelPurpose.Ground), place.Town, optional: true)) { yield return h; }

                    if (LastTravelFailure is { } failed)
                    {
                        Fail("buy-trip:travel", $"the way to {place.Town}: {failed}");
                        yield break;
                    }

                    if (S.State.Self.Position is not { } at || at.CellId >> 16 != place.Cell >> 16)
                    {
                        Fail("buy-trip:travel:off-town", $"after the travel the player stands at {Where()}, not in {seller.Name}'s landblock 0x{place.Cell >> 16:X4} in {place.Town}");
                        yield break;
                    }

                    Log(string.Create(CultureInfo.InvariantCulture, $"buy-trip: check after the travel: in {place.Town} ({Where()}); the journey's teleports {teleports} -> {Context.Facts.Teleports} of {Context.Facts.TeleportBudget}"));
                }

                Stage = "vendor";
                uint guid = 0;
                string? unreached = null;
                foreach (var h in Meet(seller, place, g => guid = g, why => unreached = why)) { yield return h; }

                if (guid == 0)
                {
                    Fail("buy-trip:vendor-unreached", $"{seller.Name} 0x{place.Guid:X8} was not met in reach: {unreached ?? "not seen within " + NpcSeconds.ToString("0", CultureInfo.InvariantCulture) + " s"}");
                    yield break;
                }

                var open = S.Actions.OpenVendor(guid, ActionSource.Automation);
                yield return open;
                if (open.Outcome is not { Kind: ActionOutcomeKind.Confirmed } || S.State.Vendor is not { } opened || opened.Vendor != guid)
                {
                    Fail("buy-trip:vendor-closed", $"{seller.Name}'s shop did not open: {Describe(open)}");
                    yield break;
                }

                // M6 16 E5b: the buy is the vendor counter's (the checks before, a trade note changed when the coin is short, the send, the wait, the check after); the trip keeps its stops' names and its lines.
                var scratch = new List<uint>();
                var before = GearInventory.CountOf(S.State, scratch, line.Wcid);
                var counter = new VendorBuyResult();
                var host = new VendorCounterHost("buy-trip", Log, Until, () => PackView.MainPackFree(S.State))
                {
                    PriceLine = (wire, catalog) => string.Create(CultureInfo.InvariantCulture, $"buy-trip: {line.Name}: {seller.Name}'s wire price {wire} differs from the catalog's {catalog} (rate {seller.SellRate}): the wire wins"),
                    CheckBefore = judged =>
                    {
                        _price = judged.Price > 0 ? judged.Price : _price;
                        if (judged.Refusal is null)
                        {
                            Stage = "buy";
                            var held = Coins();
                            Log(string.Create(CultureInfo.InvariantCulture, $"buy-trip: check before the buy: {seller.Name} in reach and open, {line.Name} listed at {_price}, the cost {judged.Cost} {(held >= judged.Cost ? "within" : "over")} the coin {held}{(held >= judged.Cost ? string.Empty : " (a trade note is changed first)")}; the pack holds {before}"));
                        }
                    },
                };
                foreach (var h in VendorCounter.Buy(S, new VendorBuyRequest(guid, line.Wcid, d.Want) { VendorName = seller.Name, WareName = line.Name, CataloguePrice = d.CatalogPrice, ArrivalSeconds = AfterSeconds }, counter, host)) { yield return h; }

                switch (counter.Refusal)
                {
                    case null or VendorCounter.AfterCheck:
                        break;
                    case "not-sold":
                        Fail("buy-trip:not-sold", string.Create(CultureInfo.InvariantCulture, $"{line.Name} ({line.Wcid}) is not in {seller.Name}'s shop ({S.State.Vendor?.Entries.Length ?? 0} rows), against the catalog's row {seller.SaleOf(line.Wcid)?.CreateListId}"));
                        yield break;
                    case "stock-short":
                        Fail("buy-trip:stock-short", string.Create(CultureInfo.InvariantCulture, $"{seller.Name} holds {counter.Entry?.Stack ?? 0} {line.Name}, fewer than the {d.Want} wanted"));
                        yield break;
                    case "purse-short":
                        Fail("buy-trip:purse-short", string.Create(CultureInfo.InvariantCulture, $"the coin {counter.CoinBefore} does not cover {d.Want} {line.Name} at {seller.Name}'s wire price ({counter.Cost}); nothing is bought, never retried") + (PurseNotes.Read(S.State).Notes.Count > 0 ? " (the trade notes: " + counter.Why + ")" : string.Empty));
                        yield break;
                    case "pack-full":
                        Fail("buy-trip:pack-full", "the main pack has no free slot for the bought stack");
                        yield break;
                    case VendorCounter.Refused:
                        Fail("buy-trip:refused", $"{seller.Name} refused {d.Want} {line.Name}: {counter.Outcome}; never retried");
                        yield break;
                    default:
                        Fail("buy-trip:" + counter.Refusal, counter.Why);
                        yield break;
                }

                var coins = counter.CoinBefore; // the coin at the send, after any note was changed
                var after = GearInventory.CountOf(S.State, scratch, line.Wcid);
                _bought = Math.Max(0, after - before);
                _purseAfter = Coins();
                if (_bought != d.Want || _purseAfter >= coins)
                {
                    Fail("buy-trip:after-check", string.Create(CultureInfo.InvariantCulture, $"after the buy the pack holds {after} {line.Name} (was {before}, want +{d.Want}) and the coin is {_purseAfter} (was {coins})"));
                    yield break;
                }

                if (counter.NotesChanged > 0)
                {
                    Log(string.Create(CultureInfo.InvariantCulture, $"buy-trip: paid from the purse: {counter.NotesChanged} trade note(s) changed to coin at {seller.Name} before the buy (the coin {_purseBefore} was short of {counter.Cost})"));
                }

                Log(string.Create(CultureInfo.InvariantCulture, $"buy-trip: check after the buy: the pack holds {after} {line.Name} (+{_bought}), the coin {coins} -> {_purseAfter} (paid {coins - _purseAfter})"));
                _reason = "bought";
                Emit();

                if (!back || inTown)
                {
                    Log(back ? "buy-trip: the trip started in the town: no way back" : "buy-trip: the trip ends at the archmage (no-back)");
                    yield break;
                }

                Stage = "back";
                foreach (var h in WayBack(ground, hop)) { yield return h; }
            }
            finally
            {
                Emit();
            }
        }

        /// <summary>The trip rule's input: the body's travel place (else the home lifestone's), the level, Lifestone Recall in the book, and whether the ground's home is the travel file's start.</summary>
        private TownTripInput TownInput(HuntingGround ground) => new(
            (S.State.Self.Position is { } at ? TownNetworkTrip.PlaceKeyOf(at.CellId >> 16, Context.Routes) : null) ?? TownNetworkTrip.PlaceKeyOf(ground.Home.LifestoneCell >> 16, Context.Routes),
            S.State.Self.Qualities.Level,
            S.State.Self.Spellbook.Knows(Fighter.RecallHome.LifestoneRecallSpell),
            Context.Routes.StartLifestone.Cell >> 16 == ground.Home.LifestoneCell >> 16);

        /// <summary>
        /// M6 4 t4a: the hops of a trip by the Town Network, check, act, check: before, the body stands in the first portal's place (else the walk to the home lifestone's point, the travel file's start, and the check again); each hop a
        /// <see cref="PortalHopLeg"/> (its own <c>buy_trip_hop:</c> line); after, present in the town's place. A failure fails the station by name (<c>buy-trip:hop:&lt;name&gt;</c>) and the way back is tried (the body is away from home).
        /// </summary>
        private IEnumerable<ActionHandle?> HopOut(TownTripPlan trip, string what)
        {
            var ground = Context.Dungeon;
            if (trip.Hops.Count > 0 && trip.Hops[0].FromLandblocks.Count > 0 && (S.State.Self.Position is not { } at || !trip.Hops[0].FromLandblocks.Contains((ushort)(at.CellId >> 16))))
            {
                Log($"walking: purpose=town-trip (buy-trip: to {ground.Home.Town}'s lifestone point, in the place of {trip.Hops[0].Name}, the first portal to {trip.Candidate.Town})");
                foreach (var h in TravelTo(new TravelRequest(Destination("home-lifestone-point", ground.Home.LifestoneCell, ground.Home.LifestonePoint), TravelPurpose.Bind), "the trip's start", optional: true)) { yield return h; }

                if (LastTravelFailure is not null || S.State.Self.Position is not { } there || !trip.Hops[0].FromLandblocks.Contains((ushort)(there.CellId >> 16)))
                {
                    Fail("buy-trip:start", $"the walk to the trip's start ended at {Where()}, not in {trip.Hops[0].Name}'s place: {LastTravelFailure ?? "the check after"}");
                    yield break;
                }
            }

            Log($"buy-trip: by the Town Network to {trip.Candidate.Town} for the {what}: hops {trip.HopKeys}, the drop 0x{trip.DropCell:X8}, home by {trip.Home}");
            for (var n = 0; n < trip.Hops.Count; n++)
            {
                var portal = trip.Hops[n];
                var leg = new PortalHopLeg(Context, portal, $"the buy-trip to {trip.Candidate.Town}, hop {n + 1} of {trip.Hops.Count}");
                while (leg.Step(S, S.Clock.Now) == ScenarioStatus.Running)
                {
                    yield return null;
                }

                var seen = leg.Outcome;
                Log(string.Create(CultureInfo.InvariantCulture, $"buy_trip_hop: n={n + 1} edge={portal.Key} to={RungRoute.Hex(S.State.Self.Position?.CellId >> 16 ?? 0)} walk_m={seen.WalkMetres:0} taken={(seen.Taken ? "yes" : "no")} recoveries={seen.Recoveries} ok={(leg.Failure is null ? "yes" : "no")}"));
                if (leg.Failure is not null)
                {
                    Fail("buy-trip:hop:" + (leg.FailureName ?? portal.Key), $"the hop {portal.Key} to {trip.Candidate.Town} failed: {leg.Failure}");
                    foreach (var h in WayBack(ground, trip)) { yield return h; }

                    yield break;
                }
            }

            if (S.State.Self.Position is not { } here || !trip.Place.Landblocks.Contains((ushort)(here.CellId >> 16)))
            {
                Fail("buy-trip:travel:off-town", $"after the hops the player stands at {Where()}, not in {trip.Candidate.Town}'s place ({trip.Place.Key})");
                yield break;
            }

            Log($"buy-trip: check after the travel: in {trip.Candidate.Town} ({Where()}), {trip.Hops.Count} hops, no teleport spent ({Context.Facts.Teleports} of {Context.Facts.TeleportBudget})");
        }

        /// <summary>
        /// The way back: Lifestone Recall to the ground's home when the book holds it (the seam's <see cref="TravelPurpose.Home"/>), else after a trip by the Town Network the rung change's <c>direction=home</c> by portals; after, present in
        /// the home's landblock (the recall) or the travel file's start (the portals). A failure is the stop <c>buy-trip:way-back</c>.
        /// </summary>
        private IEnumerable<ActionHandle?> WayBack(HuntingGround ground, TownTripPlan? trip)
        {
            uint landblock;
            if (trip is null || trip.HomeByRecall || S.State.Self.Spellbook.Knows(Fighter.RecallHome.LifestoneRecallSpell))
            {
                landblock = ground.Home.LifestoneCell >> 16;
                Log($"walking: purpose=home (buy-trip: Lifestone Recall to {ground.Home.Town}, the way back)");
                foreach (var h in TravelTo(new TravelRequest(Destination("home-lifestone", ground.Home.LifestoneCell, ground.Home.LifestonePoint), TravelPurpose.Home), "home", optional: true)) { yield return h; }

                if (LastTravelFailure is { } home)
                {
                    FailBack($"the way back to {ground.Home.Town}: {home}");
                    yield break;
                }
            }
            else
            {
                landblock = Context.Routes.StartLifestone.Cell >> 16;
                Log($"walking: purpose=home (buy-trip: the book lacks Lifestone Recall; the rung change direction=home by portals ({trip.Home}) to {Context.Routes.StartPlace})");
                var run = new RungChangeStation(RungDirection.Home).Start(Context);
                while (run.Step(S, S.Clock.Now) == ScenarioStatus.Running)
                {
                    yield return null;
                }

                Log($"buy-trip: the way back by portals: {run.VerdictFields()}");
                if (!run.Ok)
                {
                    FailBack($"the way back by portals: {run.VerdictFields()}");
                    yield break;
                }
            }

            if (S.State.Self.Position is not { } at || at.CellId >> 16 != landblock)
            {
                FailBack(string.Create(CultureInfo.InvariantCulture, $"after the way back the player stands at {Where()}, not in the home landblock 0x{landblock:X4}"));
                yield break;
            }

            Log($"buy-trip: check after the way back: at {Where()}");
        }

        /// <summary>The way back failed: the stop <c>buy-trip:way-back</c>, or (after an earlier failure) a line beside it.</summary>
        private void FailBack(string why)
        {
            if (Failure is null)
            {
                Fail("buy-trip:way-back", why + (_bought > 0 ? " (the scarabs are bought)" : string.Empty));
            }
            else
            {
                Check(false, "buy-trip:way-back: " + why);
            }
        }

        /// <summary>Logs the line once (after the buy, or at the end).</summary>
        private void Emit()
        {
            if (_lined)
            {
                return;
            }

            _lined = true;
            Log(LinePrefix + " " + Fields());
        }

        /// <summary>True when the player stands in the seller's town (<see cref="TownLandblocks"/>), not in one landblock of it (M6 1 fix, 1-R13).</summary>
        private bool InTown(ScrivenerPlace place, GroundHome arrival) =>
            S.State.Self.Position is { } at && TownLandblocks(place.Town, arrival, ScrivenerCatalog.Default).Contains(at.CellId >> 16);

        private string Where() => S.State.Self.Position is { } p ? string.Create(CultureInfo.InvariantCulture, $"cell 0x{p.CellId:X8} ({p.Origin.X:0.#}, {p.Origin.Y:0.#})") : "an unknown place";

        /// <summary>
        /// Walks to the archmage (as the resupply's walk to a vendor): to his live place with an object goal and his use radius when the object table holds him, else to his catalog point stopping short of his body
        /// (<see cref="ShopLeg.UnseenVendorReachMetres"/>), then once more when he appears out of reach. Found only when the live distance is within his reach (<see cref="ShopLeg.ReachOf"/>).
        /// </summary>
        private IEnumerable<ActionHandle?> Meet(ScarabSeller seller, ScrivenerPlace place, Action<uint> found, Action<string> unreached)
        {
            for (var walk = 0; walk < 2; walk++)
            {
                TravelRequest request;
                if (S.State.Objects.TryGet(place.Guid, out var live) && live.Position is { } at)
                {
                    request = new TravelRequest(Destination(seller.Name, at.CellId, at.Origin), TravelPurpose.Vendor, place.Guid, ShopLeg.UseRadiusOf(live));
                }
                else if (walk == 0)
                {
                    request = new TravelRequest(Destination(seller.Name, place.Cell, place.Point), TravelPurpose.Vendor, Range: ShopLeg.UnseenVendorReachMetres);
                }
                else
                {
                    yield break;
                }

                Log($"walking: purpose=vendor (buy-trip: to {seller.Name} 0x{place.Guid:X8} in {place.Town})");
                foreach (var h in TravelTo(request, "the archmage " + seller.Name, optional: true)) { yield return h; }

                if (LastTravelFailure is { } why)
                {
                    unreached(why);
                    yield break;
                }

                foreach (var h in Until(() => S.State.Objects.TryGet(place.Guid, out var o) && o.Position is not null, NpcSeconds)) { yield return h; }

                if (!S.State.Objects.TryGet(place.Guid, out var met) || met.Position is not { } there || S.State.Self.Position is not { } me)
                {
                    yield break;
                }

                var distance = Vector2.Distance(ResupplyPlanner.WorldMetres(me.CellId, me.Origin), ResupplyPlanner.WorldMetres(there.CellId, there.Origin));
                if (distance <= ShopLeg.ReachOf(met))
                {
                    Log(string.Create(CultureInfo.InvariantCulture, $"buy-trip: {seller.Name} is {distance:0.0} m away, within his reach {ShopLeg.ReachOf(met):0.0} m"));
                    found(place.Guid);
                    yield break;
                }

                Log(string.Create(CultureInfo.InvariantCulture, $"buy-trip: {seller.Name} is {distance:0.0} m away after the walk, beyond his reach {ShopLeg.ReachOf(met):0.0} m"));
            }
        }

        /// <summary>
        /// M6 16 E5b: the ItemType mask a seller's counter buys (ACE's MerchandiseItemTypes, PropertyInt 74), when a ground profile lists the seller's weenie with one (the profiles' <c>buys_item_types</c>, from the pinned world SQL
        /// v0.9.296: Zisaha bint Jaidka the Mage 2314 has 0x000CB000, which holds PromissoryNote 0x40000), else null: not known before the trip.
        /// </summary>
        private static uint? NoteMaskOf(uint sellerWcid) => NoteMasks.Of(sellerWcid); // M6 16 E5a2: the quartermaster's one read of the profiles' masks, the same answer

        private void Fail(string token, string why)
        {
            _reason = token.StartsWith("buy-trip:", StringComparison.Ordinal) ? token["buy-trip:".Length..] : token;
            var text = token + ": " + why;
            Check(false, text);
            Failure ??= text;
        }
    }
}
