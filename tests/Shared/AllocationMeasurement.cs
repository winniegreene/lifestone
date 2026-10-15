namespace Lifestone.Client.Tests;

/// <summary>
/// The xUnit collection for tests that measure the bytes their own thread allocates. It runs with parallelization
/// disabled (after the parallel collections, alone), because other test classes running at the same time trigger GCs
/// and runtime work that land inside the measured window (the flaky SkyDirector time-lapse test of the M1 audit:
/// 2,816-4,144 B in a random frame, only in full-suite runs).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AllocationMeasurementCollection
{
    public const string Name = "Allocation measurements (not parallel)";
}

/// <summary>
/// One measured window of the current thread's allocations, as the game's sky counter measures it: a window with a GC
/// inside is reported as such and not judged (SkyController's frames_with_gc_inside), because the runtime's work
/// around a collection can show up in the thread's count. Allocation-free itself (a struct; no closures).
/// </summary>
internal readonly struct AllocationWindow
{
    private readonly int _collections;
    private readonly long _bytes;

    private AllocationWindow(int collections, long bytes)
    {
        _collections = collections;
        _bytes = bytes;
    }

    /// <summary>Opens a window: the GC count first, then the byte count (closed in the opposite order).</summary>
    public static AllocationWindow Open() => new(GC.CollectionCount(0), GC.GetAllocatedBytesForCurrentThread());

    /// <summary>Bytes this thread allocated since <see cref="Open"/>, and whether a GC (of any generation) ran meanwhile.</summary>
    public (long Bytes, bool GcInside) Close()
    {
        var bytes = GC.GetAllocatedBytesForCurrentThread() - _bytes;
        return (bytes, GC.CollectionCount(0) != _collections);
    }
}
