namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M5 7 F29 (D96, report-only): the items the loot rule's plans kept and left over the process, for the soak line's <c>loot_kept=</c> and <c>loot_left=</c> (pyreals count as kept, as the verdict's <c>loot_plan_kept</c> does). A process-wide
/// total like the walkers' steering tally: the plans are made by short-lived fight scenarios, the line is read once an interval. Allocation-free.
/// </summary>
public static class LootTally
{
    private static long _kept;
    private static long _left;
    private static long _opened;
    private static long _leftAggro;
    private static long _leftDecayed;
    private static long _busyWaits;
    private static long _leftBusy;
    private static long _leftUnreached;

    /// <summary>The items kept over the process.</summary>
    public static long Kept => Interlocked.Read(ref _kept);

    /// <summary>The items left over the process.</summary>
    public static long Left => Interlocked.Read(ref _left);

    /// <summary>The corpses opened over the process (the soak line's <c>corpses_opened=</c>).</summary>
    public static long Opened => Interlocked.Read(ref _opened);

    /// <summary>The times a corpse was put off by the aggro rule over the process (the soak line's <c>loot_left_aggro=</c>; one corpse can count more than once).</summary>
    public static long LeftAggro => Interlocked.Read(ref _leftAggro);

    /// <summary>The corpses given up to decay (rotted or gone) over the process (the soak line's <c>loot_left_decayed=</c>).</summary>
    public static long LeftDecayed => Interlocked.Read(ref _leftDecayed);

    /// <summary>M5 7 D123: the opens answered "You're too busy!" (0x001D) and sent again after the busy wait, over the process (the soak line's <c>loot_busy_waits=</c>).</summary>
    public static long BusyWaits => Interlocked.Read(ref _busyWaits);

    /// <summary>M5 7 D123: the corpses left with every open answered busy, over the process (the soak line's <c>loot_left_busy=</c>).</summary>
    public static long LeftBusy => Interlocked.Read(ref _leftBusy);

    /// <summary>M5 7 D123: the corpses left beyond the loot radius, out of sight, or not reached by ACE's walk, over the process (the soak line's <c>loot_left_unreached=</c>).</summary>
    public static long LeftUnreached => Interlocked.Read(ref _leftUnreached);

    /// <summary>Counts one busy wait.</summary>
    public static void AddBusyWait() => Interlocked.Increment(ref _busyWaits);

    /// <summary>Counts one corpse left busy.</summary>
    public static void AddLeftBusy() => Interlocked.Increment(ref _leftBusy);

    /// <summary>Counts one corpse left unreached.</summary>
    public static void AddLeftUnreached() => Interlocked.Increment(ref _leftUnreached);

    /// <summary>Counts one corpse opened.</summary>
    public static void AddOpened() => Interlocked.Increment(ref _opened);

    /// <summary>Counts one put-off by the aggro rule.</summary>
    public static void AddLeftAggro() => Interlocked.Increment(ref _leftAggro);

    /// <summary>Counts one corpse given up to decay.</summary>
    public static void AddLeftDecayed() => Interlocked.Increment(ref _leftDecayed);

    /// <summary>Adds one corpse's plan.</summary>
    /// <param name="kept">The items kept.</param>
    /// <param name="left">The items left.</param>
    public static void Add(int kept, int left)
    {
        Interlocked.Add(ref _kept, kept);
        Interlocked.Add(ref _left, left);
    }
}
