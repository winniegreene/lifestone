using System.Collections.Concurrent;
using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Mage;

/// <summary>What <see cref="LogisticsCounters.Check"/> says the loop must do.</summary>
public enum LogisticsKind
{
    /// <summary>All is well: no stock line is under its lines and the load is under the burden line.</summary>
    None,

    /// <summary>A trip is due: a restockable line is under its <c>trip_under</c>, or the load passed the burden line. The loop finishes its fight step and goes.</summary>
    Trip,

    /// <summary>The loop must stop: a line is under its <c>stop_under</c> (for a carried line, which no trip restocks, the only trigger it has).</summary>
    Stop,

    /// <summary>The counts are not read yet: the pack's list is not known, or a held item's description has not arrived. Nothing is judged (a false stop at login would be worse); callers treat it as <see cref="None"/> and ask again.</summary>
    Unread,
}

/// <summary>Why a <see cref="LogisticsVerdict"/> is what it is.</summary>
public enum LogisticsCause
{
    /// <summary>Nothing is due.</summary>
    None,

    /// <summary>A restockable line is under its <c>trip_under</c> (a <see cref="LogisticsKind.Trip"/>).</summary>
    StockLow,

    /// <summary>A line is under its <c>stop_under</c> (a <see cref="LogisticsKind.Stop"/>).</summary>
    StockStop,

    /// <summary>The load passed the supplies' burden line (a <see cref="LogisticsKind.Trip"/>).</summary>
    Burden,

    /// <summary>M5 7 F29 (D96): the sell trip's schedule: the profile's <c>loot.trip_every_buff_cycles</c> buff cycles have run since the last trip (a <see cref="LogisticsKind.Trip"/>).</summary>
    Cadence,

    /// <summary>M5 7 F29 (D96): the main pack's free slots are under the profile's <c>loot.min_free_slots</c> (a <see cref="LogisticsKind.Trip"/>).</summary>
    Slots,

    /// <summary>The pack's list is not known yet, or is incomplete (<see cref="LogisticsKind.Unread"/>).</summary>
    PackUnread,

    /// <summary>A held item has no description yet, so its ware is unknown (<see cref="LogisticsKind.Unread"/>).</summary>
    ItemsUnsettled,

    /// <summary>The burden capacity is not known (no Strength), so the load cannot be judged (<see cref="LogisticsKind.Unread"/>).</summary>
    CapacityUnread,

    /// <summary>
    /// M6 item 1 c (D41): the spell plan's next level of a line is readable and a scrivener of the home town sells it (<see cref="ScrivenerLearnPlan"/>; a <see cref="LogisticsKind.Trip"/>, after every other trigger): the trip buys and reads it.
    /// </summary>
    Learn,

    /// <summary>M6 7 AT: the loot she carries is worth the ground's <c>loot.sale_trip_value</c> while a stock line or a scroll is wanted and the coin cannot pay it: a sale trip (a <see cref="LogisticsKind.Trip"/>, after every other trigger); the trip sells, and the buys the coin then pays are the same visit's.
    /// M6 14 T (the sprint of 2026-11-12: two sale trips in nine minutes, the first for 3,600 pyreals): only what the sale will offer is counted, and the trip is called only when the coin with the sale's payout pays a want.</summary>
    Sale,

    /// <summary>M6 7 AU: the Foci of Enchantment or of Artifice is due: the Burrow's gate conditions are met, the school is trained, the focus is not in the pack, a home scrivener sells it and the purse pays it (a <see cref="LogisticsKind.Trip"/>, before the learn trips).</summary>
    Foci,

    /// <summary>
    /// M6 14 pack room (<c>m6-pack-room</c>, amends <c>m6-full-pack-no-trip</c>; live 2026-11-14: the loot left 473 items over its load line in two hours and no trip was called): the loot's plans left an item over the load line since the
    /// last visit home (<see cref="PackRoomMemory.LoadLeftItems"/>): the pack is full of what she would sell, so a trip home sells it (a <see cref="LogisticsKind.Trip"/>, after the burden line). The visit clears it; the autopilot's minimum
    /// fight stint holds it after a trip.
    /// </summary>
    FullPack,

    /// <summary>
    /// M6 14 hour load (the owner, 2026-11-16: runs of about an hour, the pack at 150% when the trip leaves): the cycle's minutes of fighting on a comfortable ground are fought (<see cref="CycleLoad.HourDue"/>). The autopilot calls it after
    /// the counters' own triggers and the buff bot's (they win); the counters never return it (<see cref="LogisticsVerdict.HourUp"/>).
    /// </summary>
    Hour,
}

/// <summary>
/// One answer of <see cref="LogisticsCounters.Check"/>: what is due, the trigger named (the line, the count it holds, the threshold it is under; or the burden and its line). A value: building it allocates nothing; the
/// words (<see cref="Reason"/>, <see cref="Trigger"/>) are made when asked, for a log line on a change, never per frame.
/// </summary>
/// <param name="Kind">None, trip, stop or unread.</param>
/// <param name="Cause">The trigger.</param>
/// <param name="Line">The stock line behind a stock trigger, else null.</param>
/// <param name="Held">The count the line holds (a stock trigger), or the items not yet described (<see cref="LogisticsCause.ItemsUnsettled"/>), else 0.</param>
/// <param name="Threshold">The line's <c>trip_under</c> or <c>stop_under</c> it is under (a stock trigger), else 0.</param>
/// <param name="BurdenPercent">The load as a share of the capacity, rounded up (a burden trigger), else 0.</param>
/// <param name="LinePercent">The supplies' burden line (a burden trigger), else 0.</param>
/// <param name="Carried">True when the line is carried (no vendor of the ground sells it, so only its stop applies).</param>
public readonly record struct LogisticsVerdict(LogisticsKind Kind, LogisticsCause Cause, StockLine? Line, int Held, int Threshold, int BurdenPercent, int LinePercent, bool Carried = false)
{
    /// <summary>
    /// M6 safe load (the owner, 2026-11-20: never a trip home to sell before 40 minutes on the ground; other reasons are not held to it): true for a trip whose cure is a sale, the pack's weight or room or the loot's worth
    /// (<see cref="LogisticsCause.Burden"/>, <see cref="LogisticsCause.Cadence"/>, <see cref="LogisticsCause.Slots"/>, <see cref="LogisticsCause.Sale"/>, <see cref="LogisticsCause.FullPack"/>); the hour's own trip comes after its hour.
    /// </summary>
    public bool IsSale => Kind == LogisticsKind.Trip && Cause is LogisticsCause.Burden or LogisticsCause.Cadence or LogisticsCause.Slots or LogisticsCause.Sale or LogisticsCause.FullPack;

    /// <summary>M6 1 c: the plan's answer behind a <see cref="LogisticsCause.Learn"/> trip (the spell, the numbers), else null.</summary>
    public LearnNeed? Learn { get; init; }

    /// <summary>M6 7 AU: the focus behind a <see cref="LogisticsCause.Foci"/> trip, else null.</summary>
    public FocusNeed? Focus { get; init; }

    /// <summary>M6 14 T: the pyreals the sale behind a <see cref="LogisticsCause.Sale"/> trip is expected to pay (each offered item's Value at its best buyer's rate), else 0.</summary>
    public long SalePayout { get; init; }

    /// <summary>Nothing is due.</summary>
    public static LogisticsVerdict AllWell => new(LogisticsKind.None, LogisticsCause.None, null, 0, 0, 0, 0);

    /// <summary>M6 14 hour load: the hour's trip, with the minutes fought, the hour, the burden in percent of the capacity and the line at the top.</summary>
    /// <param name="minutesFought">The minutes since the cycle's first fight frame.</param>
    /// <param name="hourMinutes">The settings' minutes.</param>
    /// <param name="burdenPercent">The burden now, in percent of the capacity (rounded up).</param>
    /// <param name="topPercent">The line at the end of the ramp.</param>
    public static LogisticsVerdict HourUp(int minutesFought, int hourMinutes, int burdenPercent, int topPercent) => new(LogisticsKind.Trip, LogisticsCause.Hour, null, minutesFought, hourMinutes, burdenPercent, topPercent);

    /// <summary>True when a trip is due.</summary>
    public bool IsTrip => Kind == LogisticsKind.Trip;

    /// <summary>True when the loop must stop.</summary>
    public bool IsStop => Kind == LogisticsKind.Stop;

    /// <summary>
    /// The reason in words, naming the trigger: the line, its count and its threshold (<c>Prismatic Taper: 11 held, under its stop line 12</c>), or <c>burden 91% over the line 90%</c>. Allocates: ask on a change, not per frame.
    /// </summary>
    public string Reason => Cause switch
    {
        LogisticsCause.StockLow => string.Create(CultureInfo.InvariantCulture, $"{Line?.Name}: {Held} held, under its trip line {Threshold}"),
        LogisticsCause.StockStop => string.Create(CultureInfo.InvariantCulture, $"{Line?.Name}: {Held} held, under its stop line {Threshold}{(Carried ? " (carried: no vendor of the ground restocks it)" : string.Empty)}"),
        LogisticsCause.Burden => string.Create(CultureInfo.InvariantCulture, $"burden {BurdenPercent}% over the line {LinePercent}%"),
        LogisticsCause.Cadence => string.Create(CultureInfo.InvariantCulture, $"{Held} buff cycles since the last trip, the schedule is {Threshold}"),
        LogisticsCause.Slots => string.Create(CultureInfo.InvariantCulture, $"{Held} free slot(s), under the floor {Threshold}"),
        LogisticsCause.PackUnread => "the pack's list is not known yet",
        LogisticsCause.ItemsUnsettled => string.Create(CultureInfo.InvariantCulture, $"{Held} held items have no description yet"),
        LogisticsCause.CapacityUnread => "the burden capacity is not known (no Strength)",
        LogisticsCause.Learn => $"learn {Learn?.Spell?.Name}: {Learn?.Why}",
        LogisticsCause.Foci => $"foci {Focus?.Focus.Name}: {Focus?.Why}",
        LogisticsCause.Sale => string.Create(CultureInfo.InvariantCulture, $"the loot carried is worth {Held} Value, the sale line is {Threshold}, and the coin cannot pay what is wanted{(SalePayout > 0 ? $"; its sale (about {SalePayout} pyreals) does" : string.Empty)}"),
        LogisticsCause.FullPack => string.Create(CultureInfo.InvariantCulture, $"full pack: the loot left {Held} great-cantrip piece(s) or quest want(s) over its load line {LinePercent}% since the last visit (burden {BurdenPercent}% of the capacity): a trip sells what she carries (m6-pack-room)"),
        LogisticsCause.Hour => string.Create(CultureInfo.InvariantCulture, $"the hour: {Held} minute(s) fought on a comfortable ground, the cycle's target is {Threshold}; burden {BurdenPercent}% of the capacity under the hour's line {LinePercent}%: a trip sells what she carries (m6-hour-load)"),
        _ => "all well",
    };

    /// <summary>The trigger as the verdict's field reads it (<c>trigger=</c>): <c>stock:&lt;name&gt;</c>, <c>stop:&lt;name&gt;</c>, <c>burden</c>, <c>unread</c> or <c>none</c>. Allocates: ask on a change.</summary>
    public string Trigger => Cause switch
    {
        LogisticsCause.StockLow => "stock:" + Line?.Name,
        LogisticsCause.StockStop => "stop:" + Line?.Name,
        LogisticsCause.Burden => "burden",
        LogisticsCause.Cadence => "cadence",
        LogisticsCause.Slots => "slots",
        LogisticsCause.Learn => "learn:" + Learn?.Spell?.Name,
        LogisticsCause.Foci => "foci:" + Focus?.Focus.Name,
        LogisticsCause.Sale => "sale",
        LogisticsCause.FullPack => "full-pack",
        LogisticsCause.Hour => "hour",
        LogisticsCause.None => "none",
        _ => "unread",
    };
}

/// <summary>
/// M6 item 1 d (slice c's loose end): the learn triggers held after a learn trip that bought nothing (a short purse, a refused buy), so the counters do not call the same trip again at once. A hold on a spell lasts until the purse
/// (M6 16 E5a2: what the scrivener's counter can pay with, the coin and the faces of the trade notes held, <see cref="PurseView.CounterCanPayUnreadAsZero"/>; until then the coin alone) covers the scroll's price (only when the purse was the reason, M6 1 fix 1-R1) or the character's level rises past the level at the hold; then it is released (and forgotten). One per session (<see cref="Shared"/>): the logistics
/// station sets it, every counters' <see cref="LogisticsCounters.LearnDue"/> reads it. Game thread only (the tests give each counters its own).
/// </summary>
public sealed class LearnHolds
{
    private readonly ConcurrentDictionary<uint, (int Price, int Level, bool Purse)> _holds = new(); // Shared is read by parallel tests

    /// <summary>The session's holds.</summary>
    public static LearnHolds Shared { get; } = new();

    /// <summary>The holds standing (an outgrown one stays counted until it is next read).</summary>
    public int Count => _holds.Count;

    /// <summary>Holds the learn trigger of <paramref name="spellId"/> until the coin reaches <paramref name="price"/> or the level passes <paramref name="level"/>; true when the hold is new (the caller logs it, once).</summary>
    /// <param name="spellId">The spell the trip did not buy.</param>
    /// <param name="price">The scroll's price.</param>
    /// <param name="level">The character's level at the trip.</param>
    /// <param name="purse">M6 1 fix (1-R1): true when the purse was the reason (the coin short of the price): the hold then releases on coin as well as on the level; false for any other cause (the scrivener not reached, the scroll not listed, no scroll after the walk, a refused buy): the coin proves nothing, so only a level rise releases it.</param>
    public bool Hold(uint spellId, int price, int level, bool purse = true)
    {
        if (_holds.ContainsKey(spellId))
        {
            return false;
        }

        _holds[spellId] = (price, level, purse);
        return true;
    }

    /// <summary>True while <paramref name="spellId"/> is held for this coin and level; a hold the coin or the level has outgrown is released here. Allocation-free.</summary>
    /// <param name="spellId">The spell.</param>
    /// <param name="coins">The coin now.</param>
    /// <param name="level">The level now.</param>
    public bool Holds(uint spellId, int coins, int level)
    {
        if (!_holds.TryGetValue(spellId, out var hold))
        {
            return false;
        }

        if ((hold.Purse && coins >= hold.Price) || level > hold.Level)
        {
            _holds.TryRemove(spellId, out _);
            return false;
        }

        return true;
    }

    /// <summary>M6 14 T: true while <paramref name="spellId"/> would be held at this coin and level; nothing is released (a question about a purse she does not hold yet, the sale's). Allocation-free.</summary>
    /// <param name="spellId">The spell.</param>
    /// <param name="coins">The coin asked about.</param>
    /// <param name="level">The level now.</param>
    public bool Standing(uint spellId, long coins, int level) => _holds.TryGetValue(spellId, out var hold) && !((hold.Purse && coins >= hold.Price) || level > hold.Level);

    /// <summary>A number that changes when the set of standing holds does (for the counters' cache). Allocation-free.</summary>
    /// <param name="coins">The coin now.</param>
    /// <param name="level">The level now.</param>
    internal int Key(int coins, int level)
    {
        var key = 0;
        foreach (var (spell, hold) in _holds)
        {
            if ((!hold.Purse || coins < hold.Price) && level <= hold.Level)
            {
                key = HashCode.Combine(key, spell);
            }
        }

        return key;
    }
}

/// <summary>
/// The mage's logistics counters over a ground's <c>supplies</c> section (M5 item 3 m4, D48): "is a trip due, or must I stop?". Every number is the profile's, none is in code: for each stock line the count held (the
/// stacks of that ware in the main pack and the side packs, summed), a <b>trip</b> when a restockable line is under its <c>trip_under</c>, a <b>stop</b> when any line is under its <c>stop_under</c>, a trip when the load
/// passes the supplies' <c>burden_trip_percent</c> of the capacity. A carried line (<see cref="HuntingGround.IsCarried"/>: no vendor of the ground sells it, the den's Platinum and Pyreal Scarabs, D8) has no trip, only its
/// stop, which the logistics station names as the buy-trip's (M6 item 1 d, D12: <see cref="BuyTripStation"/>). A ground whose supplies hold no stock line (the outdoor grounds) judges nothing: <see cref="Active"/> is false and every check is <see cref="LogisticsKind.None"/>.
/// <para>M5 7 P7s (D148): a carried line named floor-only (<see cref="ComponentFloor.FloorOnly"/>: only the war options need it) is no stop: under its <c>stop_under</c> it is <see cref="AtFloor"/>, the spells that need it are dropped
/// and the run goes on; every other line keeps its trip and stop.</para>
/// </summary>
/// <remarks>
/// <para><b>Order.</b> A stop outranks a trip; among stops and among trips the first line of the profile's order is named; a stock trip outranks the burden trip, and the burden trip the full-pack trip (M6 14 pack room,
/// <see cref="LogisticsCause.FullPack"/>: the loot left an item over its load line since the last visit, <see cref="PackRoomMemory"/>). The first trigger is the verdict's reason.</para>
/// <para><b>The load</b> is read as the loot rule reads it (<see cref="LootContext.Of"/>: the burden is the <c>EncumbranceVal</c> quality, the capacity comes from the Strength with the registry applied and the carrying
/// augmentations, as the inventory page computes it). The load "passes" the line when it is strictly over it: <c>burden * 100 &gt; capacity * burden_trip_percent</c>.</para>
/// <para><b>Unread.</b> Before the pack's list and the held items' descriptions have arrived the counts are not the truth, and a false stop at login would be worse than none; <see cref="Check"/> then says
/// <see cref="LogisticsKind.Unread"/>, which a caller treats as "not yet".</para>
/// <para><b>Cost.</b> <see cref="Check"/> is called every frame by a fight or a loop and allocates nothing once constructed (the lines and the counts are arrays made once; <c>LogisticsCountersTests</c> pins it). The words of
/// a verdict are made on demand.</para>
/// </remarks>
public sealed class LogisticsCounters
{
    private readonly StockLine[] _lines;
    private readonly bool[] _carried;
    private readonly bool[] _floorOnly;
    private readonly bool[] _atFloor;
    private readonly bool[] _heldBack;
    private readonly bool[] _accountOnly;
    private bool _accountOff;
    private readonly bool[] _drinkLine;
    private bool _drinksOff;
    private readonly Func<uint, bool> _holdsFocus;
    private static readonly HashSet<uint> AllFociWcids = [.. GearKit.FociWcids];
    private long _noteFaces;
    private readonly int[] _held;
    private readonly int _burdenTripPercent;
    private readonly int _lootLoadLinePercent;
    private readonly int _tripEveryCycles;
    private readonly int _minFreeSlots;
    private int _cyclesAtTrip;
    private readonly HuntingGround _ground;
    private readonly Ladder? _ladder;
    private object? _learnBook;
    private int _learnKey;

    /// <summary>M6 item 14 (the den run): the travel lines an order adds to the learn plan, read first (<see cref="ScrivenerLearnPlan.DenRunLines"/>), or empty. The autopilot sets it before the counters are read.</summary>
    public IReadOnlyList<string> OrderLines { get; set; } = [];
    private LearnNeed? _learnDue;
    private readonly LearnHolds _learnHolds;
    private readonly ComponentNeeds _needs;
    private int _needsSeen;
    private readonly int _saleTripValue;
    private bool _learnCoinShort;
    private const uint FocusContainerType = 2;
    private readonly HashSet<uint> _fociHeld = [];
    private readonly IReadOnlySet<uint> _fociWcids = FociPlan.WcidsOf(ScrivenerCatalog.Default);
    private int _fociKey = int.MinValue;
    private FocusNeed? _fociDue;
    private string _fociNote = string.Empty;
    private int _saleKey = int.MinValue;
    private long _saleValue;
    private long _salePayout;
    private readonly List<LearnNeed> _learnQueue = new(8);
    private readonly long[] _heldBackCoin;
    private readonly LootBuyer[] _buyers;
    private readonly int _tripSpendPercent;
    private bool _saleWaits;

    /// <summary>Counters over <paramref name="ground"/>'s supplies; which lines are carried is read from its vendors once, here.</summary>
    /// <param name="ground">The ground (its supplies section and its vendors' wares).</param>
    /// <param name="floorOnly">M5 7 P7s (D148): the weenie class ids of the carried lines whose floor drops spells instead of stopping (<see cref="ComponentFloor.FloorOnly"/>); null: none (today's stop for every carried line). A wcid that is not a carried line is ignored.</param>
    /// <param name="learnHolds">M6 1 d: the learn triggers held after a trip that bought nothing; null: the session's (<see cref="LearnHolds.Shared"/>).</param>
    /// <param name="componentNeeds">M6 7 AT: the components the buff passes found the pack short of (<see cref="ComponentNeeds"/>); their stock lines are raised to the passes' want; null: the session's.</param>
    public LogisticsCounters(HuntingGround ground, IEnumerable<uint>? floorOnly = null, LearnHolds? learnHolds = null, ComponentNeeds? componentNeeds = null)
    {
        _learnHolds = learnHolds ?? LearnHolds.Shared;
        _needs = componentNeeds ?? ComponentNeeds.Shared;
        ArgumentNullException.ThrowIfNull(ground);
        var stock = ground.Supplies.Stock;
        var floor = new HashSet<uint>(floorOnly ?? []);
        _lines = new StockLine[stock.Count];
        _carried = new bool[stock.Count];
        _floorOnly = new bool[stock.Count];
        _atFloor = new bool[stock.Count];
        _heldBack = new bool[stock.Count];
        _accountOnly = new bool[stock.Count];
        _drinkLine = new bool[stock.Count];
        _heldBackCoin = new long[stock.Count];
        _held = new int[stock.Count];
        for (var i = 0; i < stock.Count; i++)
        {
            _lines[i] = stock[i];
            _carried[i] = ground.IsCarried(stock[i].Wcid);
            _floorOnly[i] = _carried[i] && floor.Contains(stock[i].Wcid);
            _accountOnly[i] = FociComponents.IsAccountOnly(stock[i].Wcid);
            _drinkLine[i] = PackBudgetRule.DrinkBurdens.ContainsKey(stock[i].Wcid);
        }

        _holdsFocus = _fociHeld.Contains; // made once: Check allocates nothing

        _burdenTripPercent = ground.Loot is { LoadLinePercent: > 0 } loot ? loot.LoadLinePercent : ground.Supplies.BurdenTripPercent; // M5 7 F29 (D96): the loot section's load line (85) is the trip line when it names one
        _lootLoadLinePercent = MageLootThresholds.LoadLineOf(ground); // M6 14 pack room: the line the fight's loot keeps under, for the full-pack trigger's words
        _tripEveryCycles = ground.Loot?.TripEveryBuffCycles ?? 0;
        _minFreeSlots = ground.Loot?.MinFreeSlots ?? 0;
        _ground = ground;
        _saleTripValue = ground.Loot?.SaleTripValue ?? 0;
        _buyers = [.. ground.LootBuyers];
        _tripSpendPercent = Math.Clamp(ground.Supplies.Purse.TripSpendPercent, 1, 100);
        var ladder = LadderLoader.WarMage;
        _ladder = ground.Scriveners.Count > 0 && ladder.Rungs.Any(r => ScrivenerLearnPlan.RungOf(ladder, ground.Key, r.Band.From) is not null) ? ladder : null; // M6 1 c: the learn trigger only where a rung names the ground and the home town has scriveners
    }

    /// <summary>
    /// M6 item 1 c (D41): the first line of the spell plan whose next level is due (<see cref="LearnNeedKind.Due"/>: the book lacks it, it is readable, the home town sells it), not refused this session (<see cref="SaleMemory.Shared"/>), or null.
    /// Recomputed only when the spellbook, the level, the enchantments or the magic skills and attributes changed, so a per-frame call allocates nothing while they stand. M6 item 1 d: a spell held after a learn trip that bought nothing
    /// (<see cref="LearnHolds"/>: a short purse, a refused buy) is not due until the coin covers its scroll or the level rises. M6 7 X: a recovery line is bought on a trip that happens and never calls one, and a protective scroll the ground's
    /// trip budget (<see cref="GroundPurse.TripSpendPercent"/>) cannot pay calls no trip (the rule of M6 7 V: a trip the purse cannot pay is no trip).
    /// </summary>
    /// <param name="state">The session's read model.</param>
    public LearnNeed? LearnDue(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_ladder is null || !state.Self.Spellbook.IsKnown || !state.Self.Qualities.IsKnown)
        {
            return null;
        }

        var q = state.Self.Qualities;
        var book = state.Self.Spellbook.Spells;
        var botCovers = BuffBotCover.For(state).Covering; // M6 item 14 S3: the lines the buff bot casts (the run's profile) are never due while its stop applies to the run
        var coin = LearnPay(state); // M6 16 E5a2: what a scrivener's counter can pay with (the coin and the note faces): a hold is released by a note's face too
        var key = HashCode.Combine(HashCode.Combine(q.Level, q.ActiveEnchantments.IsDefault ? 0 : q.ActiveEnchantments.Length, SkillKey(q), book.Count, _learnHolds.Key(coin, q.Level), coin, _noteFaces, CoverKey(botCovers)), OrderLines.Count);
        if (ReferenceEquals(book, _learnBook) && key == _learnKey)
        {
            return _learnDue is { Spell: { } s } due && !SaleMemory.Shared.LearnRefused.ContainsKey(s.SpellId) && !Held(s.SpellId, coin, q.Level) ? due : null;
        }

        (_learnBook, _learnKey, _learnDue) = (book, key, null);
        _learnCoinShort = false;
        _learnQueue.Clear();
        if (ScrivenerLearnPlan.RungOf(_ladder, _ground.Key, q.Level) is { } rung)
        {
            // M6 7 AT (live, 2026-11-06: five trips in 25 minutes, one scroll each): a learn trip goes when the purse pays the next two scrolls due (the first the trip would buy and the one after it in the plan's order; the one alone when it is the last) or holds the ground's learn_trip_coin, not for each scroll.
            var casters = CasterGain.Of(state, rung.Element is { } element && SelfEquipJudge.ElementBits.TryGetValue(element, out var bits) ? bits : 0); // M6 14 (m6-caster): the caster auras pay only on a caster she holds
            foreach (var need in ScrivenerLearnPlan.Needs(_ground, rung, _ladder.PlanCastChance, q, state.Self.Spellbook, ScrivenerCatalog.Default, _ladder, botCovers, OrderLines, casters))
            {
                if (need is { Kind: LearnNeedKind.Due, Spell: { } spell } && !SaleMemory.Shared.LearnRefused.ContainsKey(spell.SpellId))
                {
                    _learnQueue.Add(need);
                }
            }

            _learnDue = PickLearn(state, 0, out _learnCoinShort);
        }

        return _learnDue;
    }

    /// <summary>
    /// The scroll a learn trip would be called for at the purse she holds with <paramref name="extra"/> pyreals more (0: now; the sale's payout: after a sale), over the due scrolls of the last plan (<see cref="LearnDue"/>): the first not held
    /// (<see cref="LearnHolds"/>) that may call a trip, when the purse pays its batch (<see cref="LearnBatchPays"/>); else null, and <paramref name="coinShort"/> says a wanted scroll waits for coin. With <paramref name="extra"/> no hold is released
    /// (<see cref="LearnHolds.Standing"/>). Allocation-free.
    /// </summary>
    private LearnNeed? PickLearn(GameState state, long extra, out bool coinShort)
    {
        coinShort = false;
        var q = state.Self.Qualities;
        var purse = PurseOf(state) + extra;
        var coin = LearnPay(state); // M6 16 E5a2: a hold and the batch below read one purse: what a scrivener's counter can pay with
        for (var i = 0; i < _learnQueue.Count; i++)
        {
            var need = _learnQueue[i];
            var spell = need.Spell!;
            var held = extra == 0 ? Held(spell.SpellId, coin, q.Level) : _learnHolds.Standing(spell.SpellId, coin + extra, q.Level);
            if (held || !CallsATrip(need.Line.Kind, spell, purse))
            {
                coinShort = true; // wanted, and the coin cannot pay it (a sale trip may)
                continue;
            }

            if (LearnBatchPays(_learnQueue, i, purse))
            {
                return need;
            }

            coinShort = true; // M6 7 AT: the batch waits for its coin
            return null;
        }

        return null;
    }

    /// <summary>
    /// M6 14 T (no silent choices): the scrolls the plan wants and no trip is called for, in words: how many, the first by name with its price, and what releases it. Null when a learn trip is due or nothing is wanted. Allocates: the caller asks
    /// when <see cref="LearnWaitKey"/> changes, not per frame.
    /// </summary>
    /// <param name="state">The session's read model, read by the last <see cref="Check"/>.</param>
    public string? LearnWaitNote(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_learnDue is not null || _learnQueue.Count == 0 || _learnQueue[0].Spell is not { } first)
        {
            return null;
        }

        long total = 0;
        foreach (var need in _learnQueue)
        {
            total += need.Spell is { } s && _ground.PriceOf(s.ScrollWcid) is { } p ? p : 0;
        }

        return string.Create(CultureInfo.InvariantCulture, $"learn held back: {_learnQueue.Count} scroll(s) are wanted ({total} pyreals; the first {first.Name}, {_ground.PriceOf(first.ScrollWcid) ?? 0}) and the purse {PurseOf(state)} does not pay the next {LearnBatch} inside the trip's share ({_tripSpendPercent}%), nor holds the learn line {_ground.Supplies.Purse.LearnTripCoin}: no trip is called for them; they are bought on the next visit the coin pays, or when a sale's payout does");
    }

    /// <summary>M6 14 T: a number that changes when <see cref="LearnWaitNote"/>'s subject does (the scrolls waiting, the first of them). Allocation-free.</summary>
    public int LearnWaitKey => _learnDue is not null || _learnQueue.Count == 0 ? 0 : HashCode.Combine(_learnQueue.Count, _learnQueue[0].Spell?.SpellId ?? 0);

    /// <summary>The last foci decision in words (the plan's conditions, the purse or the burden that held it back): a log line for the caller, empty before the first check.</summary>
    public string FociNote => _fociNote;

    /// <summary>
    /// M6 7 AU: the focus a trip buys now (<see cref="FociPlan.Next"/>), or null: the plan names none, the purse (coin and note faces, <see cref="PurseOf"/>) does not pay its price, or its burden would put the load over the line
    /// (<see cref="GroundPurse"/> aside, the burden line the counters read). A short purse is no trip (the rule of M6 7 V): the fallback is the full formula's components at two passes' keep (<c>fallback=full-formula-components-two-passes</c>, M6 7 AT's
    /// <see cref="ComponentNeeds"/>), named in <see cref="FociNote"/>. Recomputed only when what it reads changed.
    /// </summary>
    /// <param name="state">The session's read model, read by the last <see cref="Check"/>.</param>
    public FocusNeed? FociDue(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_ladder is null || !state.Self.Spellbook.IsKnown || !state.Self.Qualities.IsKnown || !CharacterPolicy.For(state.Self.Name).StarterFoci) // M6 7 AU: the climber's plan (the starter foci are hers: StartupAudit.AssumedFoci); BSD Mage keeps its kit's foci
        {
            return null;
        }

        var q = state.Self.Qualities;
        var load = LootContext.Of(state);
        var botCovers = BuffBotCover.For(state).Covering; // M6 item 14 S3
        var key = HashCode.Combine(q.Level, state.Self.Spellbook.Spells.Count, _fociHeld.Count, PurseOf(state), SaleMemory.Shared.FociRefused.Count, load.Burden, SkillKey(q), CoverKey(botCovers));
        if (key == _fociKey)
        {
            return _fociDue;
        }

        _fociKey = key;
        _fociDue = null;
        var knows = RungGateRule.BookKnows(BuffProfiles.Climber, state.Self.Spellbook.Knows);
        var need = FociPlan.Next(_ground, _ladder, ScrivenerCatalog.Default, q, knows, w => _fociHeld.Contains(w) ? 1 : 0, SaleMemory.Shared.FociRefused, out var why, botCovers);
        if (need is null)
        {
            _fociNote = "foci: not due: " + why;
            return null;
        }

        var purse = PurseOf(state);
        if (purse < need.Price)
        {
            _fociNote = string.Create(CultureInfo.InvariantCulture, $"foci: {need.Focus.Name} waits: the purse {purse} (coin and note faces) does not pay {need.Price}; fallback=full-formula-components-two-passes (AT's stock rule keeps the account formula's components at two passes)");
            return null;
        }

        if (load.Capacity > 0 && (long)(load.Burden + need.Focus.Burden) * 100 > (long)load.Capacity * _burdenTripPercent)
        {
            _fociNote = string.Create(CultureInfo.InvariantCulture, $"foci: {need.Focus.Name} waits: the load {load.Burden} and the focus' {need.Focus.Burden} are over the line {_burdenTripPercent}% of the capacity {load.Capacity}; fallback=full-formula-components-two-passes");
            return null;
        }

        _fociNote = "foci: due: " + need.Why;
        _fociDue = need;
        return need;
    }

    /// <summary>
    /// M6 7 X: whether a due line may call a trip. A bolt or a debuff does (a short purse is the hold's, M6 1 d); a protective line, a regeneration line (M6 7 AN, the owner: they are bought, no longer only on a trip that happens), a sustain line, M6 14's attack line (Harm Other), and M6 7 AD's dance line (with M6 14's Revitalize Self), only while the ground's trip budget pays its scroll at the purse now, so a rung with a poor
    /// purse is not walked home and back once a level for a scroll the trip would not buy (no budget: yes; no price listed: yes, the buy's own check stands).
    /// </summary>
    private bool CallsATrip(ScrivenerLineKind kind, ScrivenerSpell spell, long purse)
    {
        var percent = _ground.Supplies.Purse.TripSpendPercent;
        if (percent < 100 && _ladder is not null && IsFirstScroll(spell))
        {
            return _ground.PriceOf(spell.ScrollWcid) is not { } first || purse >= first; // M6 7 AK: the first scrolls are paid from the whole purse
        }

        return !(ScrivenerLearnPlan.IsBuffLine(kind) || kind is ScrivenerLineKind.Dance or ScrivenerLineKind.Revitalize or ScrivenerLineKind.Sustain or ScrivenerLineKind.Attack) || percent >= 100 || _ground.PriceOf(spell.ScrollWcid) is not { } price || purse * percent / 100 >= price;
    }

    private bool IsFirstScroll(ScrivenerSpell spell)
    {
        var firsts = _ladder!.FirstScrolls;
        for (var i = 0; i < firsts.Count; i++)
        {
            if (firsts[i].Line == spell.Line && firsts[i].Level == spell.Level)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// M6 7 AT: whether the purse pays a learn trip's batch: the first due scroll the trip would buy and the one after it in the plan's order (<paramref name="queue"/> from <paramref name="first"/>, <see cref="LearnBatch"/> at most) inside the ground's trip budget, or the coin line (<see cref="GroundPurse.LearnTripCoin"/>) reached. A scroll
    /// with no price listed counts as free (the buy's own check stands, as <see cref="CallsATrip"/>).
    /// </summary>
    private bool LearnBatchPays(List<LearnNeed> queue, int first, long purse)
    {
        if (purse >= _ground.Supplies.Purse.LearnTripCoin)
        {
            return true;
        }

        var percent = _ground.Supplies.Purse.TripSpendPercent;
        var budget = percent >= 100 ? purse : purse * percent / 100;
        long cost = 0;
        for (var i = first; i < queue.Count && i < first + LearnBatch; i++)
        {
            cost += queue[i].Spell is { } spell && _ground.PriceOf(spell.ScrollWcid) is { } price ? price : 0;
        }

        return cost <= budget;
    }

    /// <summary>
    /// M6 16 E5d: true for the session's reading twin (<see cref="Quartermaster.TripDesk.Reading"/>: the panel's, the audit's and the fight's floor read): a learn hold is asked and never released by it (<see cref="LearnHolds.Standing"/>),
    /// so a read moves nothing a decision reads. False for the counters that decide.
    /// </summary>
    public bool ReadOnly { get; init; }

    /// <summary>A learn hold on <paramref name="spellId"/>: released when outgrown by the counters that decide, only asked by the reading twin. Allocation-free.</summary>
    private bool Held(uint spellId, int coin, int level) => ReadOnly ? _learnHolds.Standing(spellId, coin, level) : _learnHolds.Holds(spellId, coin, level);

    /// <summary>M6 7 AT: how many due scrolls a learn trip waits to pay for (the next two).</summary>
    public const int LearnBatch = 2;

    /// <summary>M6 7 X: holds the learn trigger of <paramref name="spellId"/> after a trip whose budget could not pay its scroll (<see cref="LearnHolds.Hold"/>); true when the hold is new.</summary>
    /// <param name="spellId">The spell.</param>
    /// <param name="coin">The coin that releases the hold.</param>
    /// <param name="level">The character's level at the trip.</param>
    public bool HoldLearn(uint spellId, int coin, int level) => _learnHolds.Hold(spellId, coin, level);

    private static readonly uint[] MagicSkills = [MageTemplate.WarMagic, MageTemplate.LifeMagic, MageTemplate.CreatureEnchantment, MageTemplate.ItemEnchantment, MageTemplate.ManaConversion]; // M6 14: the dance's round (ScrivenerLearnPlan.RevitalizeTarget) and the press's level read Mana Conversion

    /// <summary>The bot's cover as one number for a plan's key (M6 item 14): 0 with none, else the covering profile's identity, so a plan made under one profile is not kept under another; allocates nothing.</summary>
    private static int CoverKey(BuffBotProfile? covering) => covering is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(covering);

    /// <summary>The magic skills and the two magic attributes as one number (the plan's inputs besides the book and the level); allocates nothing.</summary>
    private static int SkillKey(PlayerQualities q)
    {
        var key = 0;
        foreach (var id in MagicSkills)
        {
            key = HashCode.Combine(key, q.TryGetSkill(id, out var skill) ? skill.GetHashCode() : 0);
        }

        // M6 14 T: the dance line's level reads the stamina's standing maximum: the Endurance and the vital's own ranks are the plan's inputs too (never the stamina she holds)
        return HashCode.Combine(key, q.TryGetAttribute(MageTemplate.Focus, out var focus) ? focus.GetHashCode() : 0, q.TryGetAttribute(MageTemplate.Self, out var self) ? self.GetHashCode() : 0,
            q.TryGetAttribute(MageTemplate.Endurance, out var endurance) ? endurance.GetHashCode() : 0, q.TryGetVital(MageTemplate.MaxStamina, out var stamina) ? HashCode.Combine(stamina.Start, stamina.Ranks) : 0);
    }

    /// <summary>M5 7 F29: a trip every this many buff cycles (the profile's <c>loot.trip_every_buff_cycles</c>); 0 is off.</summary>
    public int TripEveryBuffCycles => _tripEveryCycles;

    /// <summary>M5 7 F29: the free-slot floor under which a trip is due (the profile's <c>loot.min_free_slots</c>); 0 is off.</summary>
    public int MinFreeSlots => _minFreeSlots;

    /// <summary>M5 7 F29: the buff cycles counted when the last trip ended (the schedule counts from here).</summary>
    public int CyclesAtTrip => _cyclesAtTrip;

    /// <summary>M5 7 F29: notes that a trip ended with <paramref name="buffCycles"/> buff cycles counted, so the schedule counts the next ones from there.</summary>
    /// <param name="buffCycles">The buff cycles counted so far.</param>
    public void MarkTrip(int buffCycles) => _cyclesAtTrip = buffCycles;

    /// <summary>The main pack's free slots the last <see cref="Check"/> read (-1 before one reached the loads).</summary>
    public int FreeSlots { get; private set; } = -1;

    /// <summary>True when the ground has stock lines to judge; false for the empty section (every check is <see cref="LogisticsKind.None"/>).</summary>
    public bool Active => _lines.Length > 0;

    /// <summary>The number of stock lines.</summary>
    public int LineCount => _lines.Length;

    /// <summary>The supplies' burden line, in percent of the capacity.</summary>
    public int BurdenTripPercent => _burdenTripPercent;

    /// <summary>
    /// M6 14 pit-hold (live 2026-11-13, the Tusker Abode: from 14:05 UTC her burden stood at 99% of 17,250 and from 14:53 her main pack was full, and no trip was called): what a full pack calls on this ground, for the audit's
    /// <c>load</c> line, said as it is. The slot line fires under <see cref="MinFreeSlots"/> free slots (the profile's <c>loot.min_free_slots</c>; 0, absent, is no line); the burden line fires strictly over
    /// <see cref="BurdenTripPercent"/>, which the loot alone can pass only when it is under the loot's own line <paramref name="lootLoadLinePercent"/> (the loot keeps the burden at or under that line).
    /// <para>M6 14 pack room (<c>m6-pack-room</c>, the owner's direction of 2026-11-14, which <b>amends</b> the decision <c>m6-full-pack-no-trip</c>: a full pack called no trip where neither line could fire; live 2026-11-14 in the Tusker
    /// Barracks the loot left 473 items worth about 2.9 M over its line in two hours, and her supplies were not sized to her capacity): on every ground the full-pack line (<see cref="LogisticsCause.FullPack"/>) calls a trip once the
    /// loot leaves an item over its load line since the last visit, held by the autopilot's minimum fight stint after a trip (<c>TripStint</c>) and by the trip loop guard after two trips that sold and bought nothing; the visit's burden
    /// budget (<see cref="PackBudgetRule"/>) sizes the supplies so the loot has room.</para>
    /// </summary>
    /// <param name="lootLoadLinePercent">The load line the fight's loot keeps under (<see cref="MageLootThresholds.LoadLineOf"/>; 0: no limit).</param>
    public string FullPackRule(int lootLoadLinePercent)
    {
        var c = CultureInfo.InvariantCulture;
        var slotLine = _minFreeSlots > 0 ? string.Create(c, $"the slot line calls a trip under {_minFreeSlots} free slot(s)") : "no slot line (the profile names no loot.min_free_slots)";
        var burdenFires = lootLoadLinePercent == 0 || _burdenTripPercent < lootLoadLinePercent;
        var burdenLine = burdenFires
            ? string.Create(c, $"the burden line calls a trip over {_burdenTripPercent}%")
            : string.Create(c, $"the burden line {_burdenTripPercent}% is at or over the loot's line {lootLoadLinePercent}%, so the loot never passes it");
        var fullLine = lootLoadLinePercent == 0
            ? "the loot has no load line, so it leaves nothing for the load"
            : string.Create(c, $"the full-pack line calls a trip once the loot leaves a great-cantrip piece or a quest's want over its line {lootLoadLinePercent}% (m6-pack-room, amends m6-full-pack-no-trip; the minimum fight stint holds it after a trip)");
        return $"none: {slotLine}; {burdenLine}; {fullLine}";
    }

    /// <summary>The stock line <paramref name="index"/> (the profile's order).</summary>
    /// <param name="index">The line's index.</param>
    public StockLine LineAt(int index) => _lines[index];

    /// <summary>True when the stock line <paramref name="index"/> is carried (no trip, only its stop).</summary>
    /// <param name="index">The line's index.</param>
    public bool IsCarried(int index) => _carried[index];

    /// <summary>M5 7 P7s (D148): true when the stock line <paramref name="index"/> is floor-only (carried, needed by the war options alone): under its <c>stop_under</c> it drops those spells, not the run.</summary>
    /// <param name="index">The line's index.</param>
    public bool IsFloorOnly(int index) => _floorOnly[index];

    /// <summary>
    /// M6 16 E5d: states the floor-only lines again, in place (the session's one set of counters is every site's: <see cref="Quartermaster.TripDesk"/>); a wcid that is not a carried line is ignored, as at construction. The
    /// floors read (<see cref="AtFloor"/>) follow at the next counted <see cref="Check"/>.
    /// </summary>
    /// <param name="floorOnly">The weenie class ids of the floor-only lines.</param>
    public void RestateFloorOnly(IReadOnlyCollection<uint> floorOnly)
    {
        ArgumentNullException.ThrowIfNull(floorOnly);
        for (var i = 0; i < _lines.Length; i++)
        {
            _floorOnly[i] = _carried[i] && floorOnly.Contains(_lines[i].Wcid);
        }
    }

    /// <summary>
    /// M5 7 P7s (D148): true when the floor-only line <paramref name="index"/> was under its <c>stop_under</c> in the last <see cref="Check"/> that counted the pack (an unread check keeps the last answer): the spells that need it are not
    /// cast, and what is held stays as the reserve.
    /// </summary>
    /// <param name="index">The line's index.</param>
    public bool AtFloor(int index) => _atFloor[index];

    /// <summary>
    /// M6 14 pack rules (components): true when the stock line <paramref name="index"/> is the account formula's alone (<see cref="FociComponents.IsAccountOnly"/>: a coloured taper, herb, powder, potion or talisman, the profiles' keep-2 lines) and the last counted
    /// <see cref="Check"/> found the focus of every school she casts in the pack (<see cref="FociComponents.AllFociHeld"/>): no spell burns it then, so the line has no stop, no trip and no buy, and the sale sells what is held. A lost focus
    /// brings the line back (the trip buys the focus back).
    /// </summary>
    /// <param name="index">The line's index.</param>
    /// <remarks>M6 14 (the owner, 2026-11-14): also every drink's line while the stamina dance covers her mana (<see cref="PackRoomMemory.DrinksDitched"/>, read at each counted check, or set by <see cref="DrinksOff"/>).</remarks>
    public bool IsSuspended(int index) => (_accountOff && _accountOnly[index]) || (_drinksOff && _drinkLine[index]);

    /// <summary>M6 14: the station's reading that the stamina dance covers her mana, set at once (the next counted <see cref="Check"/> reads the session's memory again).</summary>
    /// <param name="off">True to suspend every drink's line.</param>
    public void DrinksOff(bool off) => _drinksOff = off;

    /// <summary>M6 14 pack rules: true when the last counted <see cref="Check"/> found the focus of every school she casts (the account formula's lines are suspended).</summary>
    public bool AllFociHeld => _accountOff;

    /// <summary>M5 7 P7s (D148): the floor-only lines at their floor as bits by line index (line 0 is bit 0; lines past 63 are not shown), for a cheap "has it changed" test. Allocation-free.</summary>
    public ulong FloorMask
    {
        get
        {
            var mask = 0UL;
            for (var i = 0; i < _atFloor.Length && i < 64; i++)
            {
                if (_atFloor[i])
                {
                    mask |= 1UL << i;
                }
            }

            return mask;
        }
    }

    /// <summary>
    /// M6 7 V: true when the stock line <paramref name="index"/> was under its <c>trip_under</c> in the last counted <see cref="Check"/> but its trip is held back: the purse (the coin and the face of the trade notes) cannot pay the vendor's price
    /// (the profile's) for the wares that would lift the line clear of its trip line (<see cref="ClearOf"/>, M6 14 T) inside one trip's share of it, so a trip would come back with the line still on its trip line and the fight goes on until
    /// the coin can pay it. A line under its <c>stop_under</c> is never held back.
    /// </summary>
    /// <param name="index">The line's index.</param>
    public bool IsHeldBack(int index) => _heldBack[index];

    /// <summary>M6 14 T: the pyreals the purse must hold before the held-back line <paramref name="index"/> calls its trip (the wares to <see cref="ClearOf"/> at the vendor's price, over the trip's share); 0 for a line not held back.</summary>
    /// <param name="index">The line's index.</param>
    public long HeldBackCoin(int index) => _heldBackCoin[index];

    /// <summary>
    /// M6 14 T (the sprint of 2026-11-12: ten lines sat on their trip lines after a visit, each one use from the next trip): the count a visit must leave a line at for it to be clear of its trip line: halfway from the line (the higher of
    /// <c>trip_under</c> and <c>stop_under</c>) to the keep, rounded up, so the next trip is at least half the keep's stretch away. A line whose keep is its line has no clear count above it (the line itself): the profile's numbers are the fix there.
    /// A trip is called for a line only when the purse pays the wares to this count, and a visit buys every line to it before any line is topped up to its keep.
    /// </summary>
    /// <param name="line">The stock line.</param>
    public static int ClearOf(StockLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var floor = Math.Max(line.TripUnder, line.StopUnder);
        return line.Keep <= floor ? floor : floor + ((line.Keep - floor + 1) / 2);
    }

    /// <summary>
    /// M6 14 pack room (<c>m6-pack-room</c>): the visit's burden budget cuts the keep of the stock line <paramref name="index"/> to <paramref name="keep"/> (<see cref="PackBudgetRule"/>), never under its trip and stop lines nor over the
    /// keep it has. For the buy list of the visit that holds these counters; the fight's counters keep the profile's keeps. True when the keep changed.
    /// </summary>
    /// <param name="index">The line's index.</param>
    /// <param name="keep">The cut keep.</param>
    public bool CutKeep(int index, int keep)
    {
        var line = _lines[index];
        var cut = Math.Max(Math.Max(line.TripUnder, line.StopUnder), Math.Min(line.Keep, keep));
        if (cut >= line.Keep)
        {
            return false; // never raised: a line whose keep is its line keeps it
        }

        _keepBeforeCut ??= new int[_lines.Length];
        if (_keepBeforeCut[index] == 0)
        {
            _keepBeforeCut[index] = line.Keep; // 0 is "not cut": a line that is cut had a keep over the cut, so over 0
        }

        _lines[index] = line with { Keep = cut };
        return true;
    }

    private int[]? _keepBeforeCut;

    /// <summary>
    /// M6 16 E5d: the visit's buy list is made: every keep a visit cut (<see cref="CutKeep"/>) is the keep it was. The session's counters are the fight's too (<see cref="Quartermaster.TripDesk"/>), and the fight's keep the
    /// profile's keeps (a trip is called for a line when the purse lifts it clear of its trip line, <see cref="ClearOf"/>, which reads the keep): until this slice the cut died with the town trip's own counters.
    /// </summary>
    public void UncutKeeps()
    {
        if (_keepBeforeCut is null)
        {
            return;
        }

        for (var i = 0; i < _lines.Length; i++)
        {
            if (_keepBeforeCut[i] > 0)
            {
                _lines[i] = _lines[i] with { Keep = Math.Max(_lines[i].Keep, _keepBeforeCut[i]) };
                _keepBeforeCut[i] = 0;
            }
        }
    }

    /// <summary>M6 14 T: the pyreals one trip's stock buys may spend of <paramref name="purse"/> (the ground's <c>purse.trip_spend_percent</c>; 100 is all of it).</summary>
    private long TripShare(long purse) => _tripSpendPercent >= 100 ? purse : purse * _tripSpendPercent / 100;

    /// <summary>The purse (coin and note faces) at which one trip's share pays <paramref name="cost"/>.</summary>
    private long PurseFor(long cost) => _tripSpendPercent >= 100 ? cost : ((cost * 100) + _tripSpendPercent - 1) / _tripSpendPercent;

    /// <summary>M6 7 V: the held-back lines as bits by line index (line 0 is bit 0; lines past 63 are not shown), for a cheap "has it changed" test. Allocation-free.</summary>
    public ulong HeldBackMask
    {
        get
        {
            var mask = 0UL;
            for (var i = 0; i < _heldBack.Length && i < 64; i++)
            {
                if (_heldBack[i])
                {
                    mask |= 1UL << i;
                }
            }

            return mask;
        }
    }

    /// <summary>
    /// The pyreals the trip trigger counts as the purse: the coin now (unread counted as 0) and the face of every trade note held at the last <see cref="Check"/>, the side packs' too (the resupply's <c>FloorPurse</c>). M6 16
    /// E5a1: both from the quartermaster's read (<see cref="PurseView.CoinUnreadAsZero"/>, <see cref="PurseView.NotesHeld"/>), the same sum as before. M6 16 E5a2: a buy changes a note inside a side pack too (<see cref="VendorCounter"/>),
    /// so a trip judged payable here on any held note is payable at a counter that takes a note (<see cref="PurseView.CounterCanPayUnreadAsZero"/>).
    /// </summary>
    private long PurseOf(GameState state) => PurseView.CoinUnreadAsZero(state) + _noteFaces;

    /// <summary>
    /// M6 16 E5a2: the pyreals a learn hold is judged on: what the counter of a scroll's seller can pay with (<see cref="PurseView.CounterCanPayUnreadAsZero"/>, with the notes of the last <see cref="Check"/>: <see cref="PurseOf"/>).
    /// A scroll's seller is a scrivener, and every scrivener's mask takes a trade note (<see cref="TownTripPurse.ScrivenersTakeNotes"/>), so the notes are counted; until this slice a hold read the coin alone, and stood
    /// beside notes the counter would have changed. Allocation-free.
    /// </summary>
    private int LearnPay(GameState state) => (int)Math.Min(int.MaxValue, TownTripPurse.ScrivenersTakeNotes ? PurseOf(state) : PurseView.CoinUnreadAsZero(state));

    /// <summary>The count of the stock line <paramref name="index"/> that the last <see cref="Check"/> read.</summary>
    /// <param name="index">The line's index.</param>
    public int HeldOf(int index) => _held[index];

    /// <summary>M5 7 audit: the index of the first carried stock line (no home vendor sells it) under its <c>stop_under</c> in the counts the last <see cref="Check"/> read, or -1 (meaningful after a Check that was not unread).</summary>
    public int FirstCarriedShort()
    {
        for (var i = 0; i < _lines.Length; i++)
        {
            if (_carried[i] && !_floorOnly[i] && _held[i] < _lines[i].StopUnder)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The load the last <see cref="Check"/> read (the <c>EncumbranceVal</c> quality).</summary>
    public int Burden { get; private set; }

    /// <summary>The capacity the last <see cref="Check"/> read.</summary>
    public int Capacity { get; private set; }

    /// <summary>
    /// Counts the stock and judges it. Allocation-free.
    /// </summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="buffCycles">M5 7 F29: the buff cycles the caller has counted so far (the schedule compares them with <see cref="CyclesAtTrip"/>); 0 for a caller that keeps no count, which then never sees the schedule.</param>
    /// <param name="skipSale">M6 safe load (the owner, 2026-11-20): true to pass over the triggers whose cure is a sale (<see cref="LogisticsVerdict.IsSale"/>), so a trip the sale's floor holds does not hide a later trigger that is not
    /// held to it (the foci, a scroll to learn).</param>
    public LogisticsVerdict Check(GameState state, int buffCycles = 0, bool skipSale = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_lines.Length == 0)
        {
            return LogisticsVerdict.AllWell;
        }

        var inventory = state.Self.Inventory;
        if (!inventory.IsKnown || !inventory.ListsComplete)
        {
            return Unread(LogisticsCause.PackUnread, 0);
        }

        ApplyNeeds();
        Array.Clear(_held);
        _noteFaces = PurseView.NotesHeld(state); // M6 16 E5a1: the quartermaster's read of every note held (M6 7 V: a note pays at its face), kept until the next check
        _fociHeld.Clear();
        var unsettled = CountPack(state, inventory.Pack);
        var sides = inventory.SidePacks;
        for (var i = 0; i < sides.Count; i++)
        {
            unsettled += CountPack(state, inventory.ItemsIn(sides[i].Guid));
            if (sides[i].ContainerType == FocusContainerType)
            {
                // M6 7 AX (live: "not held" a run after the buy, 25,000 paid twice): ACE sends a focus as ContainerType 2 (WorldObject.cs:453-462 @95ef426d), so the model lists it beside the side packs, not in the pack
                if (!state.Objects.TryGet(sides[i].Guid, out var held))
                {
                    unsettled++;
                }
                else if (_fociWcids.Contains(held.Weenie.WeenieClassId) || AllFociWcids.Contains(held.Weenie.WeenieClassId)) // M6 14 pack rules: every focus, the ones the catalog does not sell too
                {
                    _fociHeld.Add(held.Weenie.WeenieClassId);
                }
            }
        }

        if (unsettled > 0)
        {
            return Unread(LogisticsCause.ItemsUnsettled, unsettled);
        }

        _accountOff = FociComponents.AllFociHeld(_holdsFocus); // M6 14 pack rules: every focus held, the account formula's lines are suspended
        _drinksOff = PackRoomMemory.For(state).DrinksDitched is not null; // M6 14 (the owner): the stamina dance covers her mana: no drink line stops, trips or buys

        for (var i = 0; i < _lines.Length; i++)
        {
            _atFloor[i] = _floorOnly[i] && _held[i] < _lines[i].StopUnder; // M5 7 P7s (D148): a floor-only line under its stop drops its spells, not the run
        }

        for (var i = 0; i < _lines.Length; i++)
        {
            if (_held[i] < _lines[i].StopUnder && !_floorOnly[i] && !IsSuspended(i))
            {
                return new LogisticsVerdict(LogisticsKind.Stop, LogisticsCause.StockStop, _lines[i], _held[i], _lines[i].StopUnder, 0, 0, _carried[i]);
            }
        }

        Array.Clear(_heldBack);
        Array.Clear(_heldBackCoin);
        _saleWaits = false;
        var purse = -1L;
        var due = -1;
        for (var i = 0; i < _lines.Length; i++)
        {
            if (_carried[i] || _held[i] >= _lines[i].TripUnder || IsSuspended(i))
            {
                continue;
            }

            // M6 7 V (r1h: level 10, 14 coin, the Mana Potion trip called again and again): a trip the purse cannot pay for is no trip. M6 14 T: nor is one that would leave the line on its trip line: the wares to lift it clear (ClearOf) at the
            // vendor's price (the profile's), inside one trip's share of the purse; no price listed: the trip stands.
            if (_ground.PriceOf(_lines[i].Wcid) is { } price && price > 0)
            {
                purse = purse < 0 ? PurseOf(state) : purse;
                var cost = (long)price * (ClearOf(_lines[i]) - _held[i]);
                if (TripShare(purse) < cost)
                {
                    _heldBack[i] = true;
                    _heldBackCoin[i] = PurseFor(cost);
                    continue;
                }
            }

            if (due < 0)
            {
                due = i;
            }
        }

        if (due >= 0)
        {
            return new LogisticsVerdict(LogisticsKind.Trip, LogisticsCause.StockLow, _lines[due], _held[due], _lines[due].TripUnder, 0, 0);
        }

        FreeSlots = PackView.MainPackFree(state);
        if (!skipSale && _tripEveryCycles > 0 && buffCycles - _cyclesAtTrip >= _tripEveryCycles)
        {
            return new LogisticsVerdict(LogisticsKind.Trip, LogisticsCause.Cadence, null, buffCycles - _cyclesAtTrip, _tripEveryCycles, 0, 0);
        }

        if (!skipSale && _minFreeSlots > 0 && FreeSlots < _minFreeSlots)
        {
            return new LogisticsVerdict(LogisticsKind.Trip, LogisticsCause.Slots, null, FreeSlots, _minFreeSlots, 0, 0);
        }

        var load = LootContext.Of(state);
        Burden = load.Burden;
        Capacity = load.Capacity;
        if (load.Capacity <= 0)
        {
            return Unread(LogisticsCause.CapacityUnread, 0);
        }

        if (!skipSale && (long)load.Burden * 100 > (long)load.Capacity * _burdenTripPercent)
        {
            var percent = (int)((((long)load.Burden * 100) + load.Capacity - 1) / load.Capacity);
            return new LogisticsVerdict(LogisticsKind.Trip, LogisticsCause.Burden, null, 0, 0, percent, _burdenTripPercent);
        }

        if (!skipSale && PackRoomMemory.For(state).LoadLeftKeepers is > 0 and var left)
        {
            // M6 14 pack room (m6-pack-room, amends m6-full-pack-no-trip): the loot left a piece she would keep (a keep-aside cantrip piece or a wear upgrade) over its load line since the last visit: a trip sells the rest and makes
            // room for it. Sale loot left behind calls no trip (her loot wants about eight times her capacity an hour: a trip for each would cost a third of the hour). The visit clears the record.
            var full = (int)((((long)load.Burden * 100) + load.Capacity - 1) / load.Capacity);
            return new LogisticsVerdict(LogisticsKind.Trip, LogisticsCause.FullPack, null, left, 0, full, _lootLoadLinePercent);
        }

        if (FociDue(state) is { } focus)
        {
            return new LogisticsVerdict(LogisticsKind.Trip, LogisticsCause.Foci, null, 0, 0, 0, 0) { Focus = focus }; // M6 7 AU: the foci, right after what the Burrow's gate names
        }

        if (LearnDue(state) is { } learn)
        {
            return new LogisticsVerdict(LogisticsKind.Trip, LogisticsCause.Learn, null, 0, 0, 0, 0) { Learn = learn }; // M6 1 c (D41): the plan's next level, readable and sold at home
        }

        if (!skipSale && _saleTripValue > 0 && (_learnCoinShort || AnyHeldBack()) && CarriedSaleValue(state) is var carried && carried >= _saleTripValue)
        {
            // M6 7 AT: the sale trip, after every other trigger. M6 14 T: only when the coin with the sale's payout pays a want (a scroll's batch, or a held-back line lifted clear): a sale that does not raise enough is no trip, it waits.
            if (SalePays(state, _salePayout))
            {
                return new LogisticsVerdict(LogisticsKind.Trip, LogisticsCause.Sale, null, (int)Math.Min(carried, int.MaxValue), _saleTripValue, 0, 0) { SalePayout = _salePayout };
            }

            _saleWaits = true;
        }

        return LogisticsVerdict.AllWell;
    }

    /// <summary>M6 14 T: true when the purse with <paramref name="payout"/> more pays a want the coin alone cannot: the next scrolls' batch (<see cref="PickLearn"/>), or a held-back stock line lifted clear of its trip line. Allocation-free.</summary>
    private bool SalePays(GameState state, long payout)
    {
        if (_learnCoinShort && PickLearn(state, payout, out _) is not null)
        {
            return true;
        }

        var purse = PurseOf(state) + payout;
        for (var i = 0; i < _heldBack.Length; i++)
        {
            if (_heldBack[i] && purse >= _heldBackCoin[i])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>M6 14 T: true when the last counted <see cref="Check"/> found the loot carried at the sale line and a want the coin cannot pay, and called no sale trip because the sale's payout would not pay it either.</summary>
    public bool SaleWaits => _saleWaits;

    /// <summary>M6 14 T (no silent choices): the sale that waits, in words with its numbers; null when none does. Allocates: the caller asks when <see cref="SaleWaits"/> changes.</summary>
    /// <param name="state">The session's read model, read by the last <see cref="Check"/>.</param>
    public string? SaleWaitNote(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!_saleWaits)
        {
            return null;
        }

        var purse = PurseOf(state);
        var least = long.MaxValue;
        var what = string.Empty;
        for (var i = 0; i < _heldBack.Length; i++)
        {
            if (_heldBack[i] && _heldBackCoin[i] < least)
            {
                (least, what) = (_heldBackCoin[i], _lines[i].Name);
            }
        }

        var want = _learnCoinShort && _learnQueue.Count > 0 && _learnQueue[0].Spell is { } first
            ? string.Create(CultureInfo.InvariantCulture, $"the next scrolls (the first {first.Name}, {_ground.PriceOf(first.ScrollWcid) ?? 0})")
            : string.Create(CultureInfo.InvariantCulture, $"{what} (a purse of {least})");
        return string.Create(CultureInfo.InvariantCulture, $"sale held back: the loot the sale would offer is worth {_saleValue} Value (the sale line {_saleTripValue}) and would pay about {_salePayout}; with the purse {purse} that is {purse + _salePayout}, which does not pay {want}: no trip, the fight goes on until the loot or the coin does");
    }

    private bool AnyHeldBack()
    {
        for (var i = 0; i < _heldBack.Length; i++)
        {
            if (_heldBack[i])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// M6 7 AT: the Value of the loot the pack carries that a trip's sale will sell. M6 16 E5c2: the rule book's answer (<see cref="Quartermaster.SaleEstimate"/>: the items whose verdict is sell that a buyer of the ground takes),
    /// where the estimate had a list of its own; valued as before. It is worked out afresh only when what it reads changed, by the stamps a check can read without allocating: the count of the main pack, of the side packs and
    /// of what she holds in all, the coin, the worn count, the counts of the session's sale and pack memories (kept aside, dropped, rotation, review, shed stones), her spellbook's count and her level. A check that finds them
    /// unchanged reads the last answer and allocates nothing. The work itself allocates (one verdict an item held): what it costs is in the estimate's summary. Not in the stamps, by name: an appraisal of an item she already
    /// holds, and the loot and the use the pack budget measures; either moves the answer at the next change of a stamp.
    /// </summary>
    /// <param name="state">The session's read model.</param>
    public long CarriedSaleValue(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var inventory = state.Self.Inventory;
        var memory = SaleMemory.Shared;
        var packs = PackMemory.Shared;
        var key = HashCode.Combine(
            HashCode.Combine(inventory.Pack.Count, inventory.SidePacks.Count, PurseView.CoinUnreadAsZero(state), memory.KeptAside.Count, inventory.Equipped.Count),
            inventory.Count,
            memory.Dropped.Count,
            HashCode.Combine(packs.Rotation.Count, packs.WearReview.Count, packs.Shed.Count),
            state.Self.Spellbook.Count,
            state.Self.Qualities.Level);
        if (key == _saleKey)
        {
            return _saleValue;
        }

        _saleKey = key;
        (_saleValue, _salePayout) = Quartermaster.SaleEstimate.Of(state, _ground, _buyers);
        return _saleValue;
    }

    /// <summary>M6 14 T: the pyreals the sale of <see cref="CarriedSaleValue"/>'s items is expected to pay: each item's Value at the best rate among the ground's buyers of its type (<see cref="HuntingGround.LootBuyers"/>), rounded down an item.</summary>
    /// <param name="state">The session's read model.</param>
    public long CarriedSalePayout(GameState state)
    {
        CarriedSaleValue(state);
        return _salePayout;
    }

    /// <summary>M6 16 E5d: the last sale estimate the counters made (<see cref="CarriedSaleValue"/>: the Value and the payout), or null when they made none yet (the trigger estimates only on a ground with a sale line, with a want the coin cannot pay). Allocation-free.</summary>
    public (long Value, long Payout)? SaleEstimate => _saleKey == int.MinValue ? null : (_saleValue, _salePayout);

    /// <summary>
    /// M6 7 AT: raises the stock line of every component the buff passes found short (<see cref="ComponentNeeds"/>): its trip line to one pass of the lines that burn it (under that, a pass cannot cast them), and its keep to
    /// <see cref="ComponentNeeds.PassesKept"/> passes above the trip line, so the trip is called by name (<c>stock:&lt;component&gt;</c>) and buys that many; the profile's own lines stay the floor. M6 sprint (run 6, 2026-11-19 22:01
    /// and 23:30 UTC, Vitriol, and 2026-11-20 00:05, Turquoise Taper, while the Foci of Artifice was not held): the keep and the trip line were both two passes, so the first component a pass burned called a trip of its own (each one
    /// left the fight 13 to 15 minutes); now a pass or two burns from the keep before the trip line is reached. A need whose component no stock line carries is named by <see cref="UnstockedNeeds"/>.
    /// </summary>
    private void ApplyNeeds()
    {
        if (_needs.Version == _needsSeen)
        {
            return;
        }

        _needsSeen = _needs.Version;
        for (var i = 0; i < _lines.Length; i++)
        {
            var pass = _needs.PassOf(_lines[i].Wcid);
            if (pass <= 0 || _carried[i])
            {
                continue;
            }

            var trip = Math.Max(_lines[i].TripUnder, pass);
            var keep = Math.Max(_lines[i].Keep, trip + _needs.WantOf(_lines[i].Wcid));
            if (keep != _lines[i].Keep || trip != _lines[i].TripUnder)
            {
                _lines[i] = _lines[i] with { Keep = keep, TripUnder = trip };
            }
        }
    }

    /// <summary>M6 7 AT: the noted components that no stock line of the ground carries (a trip cannot pay that pass: <c>TODO(m6-component-unstocked)</c>, named by the caller), by weenie class id.</summary>
    public IEnumerable<uint> UnstockedNeeds() => _needs.Wcids.Where(w => !_lines.Any(l => l.Wcid == w));

    private static LogisticsVerdict Unread(LogisticsCause cause, int held) => new(LogisticsKind.Unread, cause, null, held, 0, 0, 0);

    /// <summary>Adds the stacks of the stock wares among <paramref name="entries"/> to the counts; returns how many entries have no description yet.</summary>
    private int CountPack(GameState state, IReadOnlyList<InventoryEntry> entries)
    {
        var unsettled = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            if (!state.Objects.TryGet(entries[i].Guid, out var item))
            {
                unsettled++;
                continue;
            }

            var wcid = item.Weenie.WeenieClassId;
            if (_fociWcids.Contains(wcid))
            {
                _fociHeld.Add(wcid);
            }

            for (var line = 0; line < _lines.Length; line++)
            {
                if (_lines[line].Wcid == wcid)
                {
                    _held[line] += StackOf(state, item);
                    break;
                }
            }
        }

        return unsettled;
    }

    /// <summary>The stack of a held item: the server's last SetStackSize, else the weenie description's, else 1.</summary>
    private static int StackOf(GameState state, WorldObject item)
    {
        if (state.Self.Inventory.TryGetStack(item.Guid, out var size, out _))
        {
            return (int)Math.Min(size, int.MaxValue);
        }

        return item.Weenie.StackSize is { } stack ? (int)Math.Min(stack, int.MaxValue) : 1;
    }
}
