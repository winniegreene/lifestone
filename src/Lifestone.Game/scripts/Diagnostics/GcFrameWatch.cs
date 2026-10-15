namespace Lifestone.Game.Diagnostics;

/// <summary>
/// The .NET garbage collector's work between two frames: the total pause time and the collections of each generation, as differences of running totals (no
/// allocation per read). A slow frame whose process time and render set-up are both small can be a GC pause; this names it (DoD 15, no frame over 50 ms).
/// </summary>
public sealed class GcFrameWatch
{
    private TimeSpan _pause = GC.GetTotalPauseDuration();
    private int _gen0 = GC.CollectionCount(0);
    private int _gen1 = GC.CollectionCount(1);
    private int _gen2 = GC.CollectionCount(2);

    /// <summary>The collector's work since the previous <see cref="Step"/> (or since construction).</summary>
    public GcFrame Last { get; private set; }

    /// <summary>Reads the totals now, stores their difference to the previous read as <see cref="Last"/>.</summary>
    public GcFrame Step()
    {
        var pause = GC.GetTotalPauseDuration();
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        Last = new GcFrame((pause - _pause).TotalMilliseconds, gen0 - _gen0, gen1 - _gen1, gen2 - _gen2);
        _pause = pause;
        _gen0 = gen0;
        _gen1 = gen1;
        _gen2 = gen2;
        return Last;
    }
}

/// <summary>One frame's garbage collection: the pause in milliseconds and the collections started per generation (a gen 2 collection also counts in gen 0 and gen 1).</summary>
public readonly record struct GcFrame(double PauseMilliseconds, int Gen0, int Gen1, int Gen2)
{
    /// <summary>The log fields <c>gc_pause_ms</c>, <c>gc_gen0</c>, <c>gc_gen1</c> and <c>gc_gen2</c>.</summary>
    public (string Key, object? Value)[] Fields() =>
    [
        ("gc_pause_ms", PauseMilliseconds),
        ("gc_gen0", Gen0),
        ("gc_gen1", Gen1),
        ("gc_gen2", Gen2),
    ];
}
