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

    /// <summary>
    /// M4 P5b (<c>TODO(m4-throwaway-teleport-frame)</c>): when this frame held a collection, the last collection's generation, pause, gen 0 size when it began and the bytes it promoted
    /// (<c>gc_last_generation</c>, <c>gc_last_pause_ms</c>, <c>gc_last_gen0_before_mib</c>, <c>gc_last_promoted_mib</c>), so a slow-frame line says how big the collection was; empty otherwise. It allocates (the
    /// runtime's info object), so it is read only when such a line is written.
    /// </summary>
    public (string Key, object? Value)[] LastCollectionFields()
    {
        if (Gen0 == 0)
        {
            return [];
        }

        var last = GC.GetGCMemoryInfo(GCKind.Any);
        return
        [
            ("gc_last_generation", last.Generation),
            ("gc_last_pause_ms", last.PauseDurations[0].TotalMilliseconds),
            ("gc_last_gen0_before_mib", last.GenerationInfo[0].SizeBeforeBytes / 1048576.0),
            ("gc_last_promoted_mib", last.PromotedBytes / 1048576.0),
        ];
    }
}
