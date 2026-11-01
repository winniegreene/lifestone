namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// M5 7 D134 (S5): the process-wide totals of the runners' steering counters, for the soak line's tail (<c>turn_reversals= turn_flips= progress_backs= swings=</c>,
/// report-only). Every <see cref="NavRunner"/> adds to them as it counts, so a walk cut off by a fight (which logs no end line) still counts. Interlocked
/// counters: reading and adding allocate nothing.
/// </summary>
public static class SteeringTally
{
    private static long _turnReversals;
    private static long _turnFlips;
    private static long _progressBacks;
    private static long _swings;

    /// <summary>Turn presses of the other side within <see cref="NavRunner.ReversalCountSeconds"/> of a release, over the process.</summary>
    public static long TurnReversals => Interlocked.Read(ref _turnReversals);

    /// <summary>Turn presses of the other side within <see cref="NavRunner.FlipCountSeconds"/> of a release (the aim jumping), over the process.</summary>
    public static long TurnFlips => Interlocked.Read(ref _turnFlips);

    /// <summary>Steps whose next route vertex stepped back on the same route, over the process.</summary>
    public static long ProgressBacks => Interlocked.Read(ref _progressBacks);

    /// <summary>Swings (an in-place press, then two reversals within <see cref="NavRunner.SwingWindowSeconds"/>), over the process; each is logged.</summary>
    public static long Swings => Interlocked.Read(ref _swings);

    internal static void AddTurnReversal() => Interlocked.Increment(ref _turnReversals);

    internal static void AddTurnFlip() => Interlocked.Increment(ref _turnFlips);

    internal static void AddProgressBack() => Interlocked.Increment(ref _progressBacks);

    internal static void AddSwing() => Interlocked.Increment(ref _swings);
}
