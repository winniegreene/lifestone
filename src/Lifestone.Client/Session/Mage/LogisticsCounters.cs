using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;

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
    /// <summary>Nothing is due.</summary>
    public static LogisticsVerdict AllWell => new(LogisticsKind.None, LogisticsCause.None, null, 0, 0, 0, 0);

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
        LogisticsCause.None => "none",
        _ => "unread",
    };
}

/// <summary>
/// The mage's logistics counters over a ground's <c>supplies</c> section (M5 item 3 m4, D48): "is a trip due, or must I stop?". Every number is the profile's, none is in code: for each stock line the count held (the
/// stacks of that ware in the main pack and the side packs, summed), a <b>trip</b> when a restockable line is under its <c>trip_under</c>, a <b>stop</b> when any line is under its <c>stop_under</c>, a trip when the load
/// passes the supplies' <c>burden_trip_percent</c> of the capacity. A carried line (<see cref="HuntingGround.IsCarried"/>: no vendor of the ground sells it, the den's Platinum and Pyreal Scarabs, D8) has no trip, only its
/// stop (<c>m5-platinum-stock</c>). A ground whose supplies hold no stock line (the outdoor grounds) judges nothing: <see cref="Active"/> is false and every check is <see cref="LogisticsKind.None"/>.
/// <para>M5 7 P7s (D148): a carried line named floor-only (<see cref="ComponentFloor.FloorOnly"/>: only the war options need it) is no stop: under its <c>stop_under</c> it is <see cref="AtFloor"/>, the spells that need it are dropped
/// and the run goes on; every other line keeps its trip and stop.</para>
/// </summary>
/// <remarks>
/// <para><b>Order.</b> A stop outranks a trip; among stops and among trips the first line of the profile's order is named; a stock trip outranks the burden trip. The first trigger is the verdict's reason.</para>
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
    private readonly int[] _held;
    private readonly int _burdenTripPercent;
    private readonly int _tripEveryCycles;
    private readonly int _minFreeSlots;
    private int _cyclesAtTrip;

    /// <summary>Counters over <paramref name="ground"/>'s supplies; which lines are carried is read from its vendors once, here.</summary>
    /// <param name="ground">The ground (its supplies section and its vendors' wares).</param>
    /// <param name="floorOnly">M5 7 P7s (D148): the weenie class ids of the carried lines whose floor drops spells instead of stopping (<see cref="ComponentFloor.FloorOnly"/>); null: none (today's stop for every carried line). A wcid that is not a carried line is ignored.</param>
    public LogisticsCounters(HuntingGround ground, IEnumerable<uint>? floorOnly = null)
    {
        ArgumentNullException.ThrowIfNull(ground);
        var stock = ground.Supplies.Stock;
        var floor = new HashSet<uint>(floorOnly ?? []);
        _lines = new StockLine[stock.Count];
        _carried = new bool[stock.Count];
        _floorOnly = new bool[stock.Count];
        _atFloor = new bool[stock.Count];
        _held = new int[stock.Count];
        for (var i = 0; i < stock.Count; i++)
        {
            _lines[i] = stock[i];
            _carried[i] = ground.IsCarried(stock[i].Wcid);
            _floorOnly[i] = _carried[i] && floor.Contains(stock[i].Wcid);
        }

        _burdenTripPercent = ground.Loot is { LoadLinePercent: > 0 } loot ? loot.LoadLinePercent : ground.Supplies.BurdenTripPercent; // M5 7 F29 (D96): the loot section's load line (85) is the trip line when it names one
        _tripEveryCycles = ground.Loot?.TripEveryBuffCycles ?? 0;
        _minFreeSlots = ground.Loot?.MinFreeSlots ?? 0;
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
    /// M5 7 P7s (D148): true when the floor-only line <paramref name="index"/> was under its <c>stop_under</c> in the last <see cref="Check"/> that counted the pack (an unread check keeps the last answer): the spells that need it are not
    /// cast, and what is held stays as the reserve.
    /// </summary>
    /// <param name="index">The line's index.</param>
    public bool AtFloor(int index) => _atFloor[index];

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
    public LogisticsVerdict Check(GameState state, int buffCycles = 0)
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

        Array.Clear(_held);
        var unsettled = CountPack(state, inventory.Pack);
        var sides = inventory.SidePacks;
        for (var i = 0; i < sides.Count; i++)
        {
            unsettled += CountPack(state, inventory.ItemsIn(sides[i].Guid));
        }

        if (unsettled > 0)
        {
            return Unread(LogisticsCause.ItemsUnsettled, unsettled);
        }

        for (var i = 0; i < _lines.Length; i++)
        {
            _atFloor[i] = _floorOnly[i] && _held[i] < _lines[i].StopUnder; // M5 7 P7s (D148): a floor-only line under its stop drops its spells, not the run
        }

        for (var i = 0; i < _lines.Length; i++)
        {
            if (_held[i] < _lines[i].StopUnder && !_floorOnly[i])
            {
                return new LogisticsVerdict(LogisticsKind.Stop, LogisticsCause.StockStop, _lines[i], _held[i], _lines[i].StopUnder, 0, 0, _carried[i]);
            }
        }

        for (var i = 0; i < _lines.Length; i++)
        {
            if (!_carried[i] && _held[i] < _lines[i].TripUnder)
            {
                return new LogisticsVerdict(LogisticsKind.Trip, LogisticsCause.StockLow, _lines[i], _held[i], _lines[i].TripUnder, 0, 0);
            }
        }

        FreeSlots = MageLootRules.FreeSlots(state);
        if (_tripEveryCycles > 0 && buffCycles - _cyclesAtTrip >= _tripEveryCycles)
        {
            return new LogisticsVerdict(LogisticsKind.Trip, LogisticsCause.Cadence, null, buffCycles - _cyclesAtTrip, _tripEveryCycles, 0, 0);
        }

        if (_minFreeSlots > 0 && FreeSlots < _minFreeSlots)
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

        if ((long)load.Burden * 100 > (long)load.Capacity * _burdenTripPercent)
        {
            var percent = (int)((((long)load.Burden * 100) + load.Capacity - 1) / load.Capacity);
            return new LogisticsVerdict(LogisticsKind.Trip, LogisticsCause.Burden, null, 0, 0, percent, _burdenTripPercent);
        }

        return LogisticsVerdict.AllWell;
    }

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
