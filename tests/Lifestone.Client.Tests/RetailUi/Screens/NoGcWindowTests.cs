using System.Runtime;
using System.Runtime.CompilerServices;
using Lifestone.Client.RetailUi.Screens;

namespace Lifestone.Client.Tests.RetailUi.Screens;

/// <summary>The test host's seam (M4 1.1 fix, R3): the staged shell's no-GC window is off in every test unless a test of <see cref="NoGcWindow"/> turns it on, so staged screens in parallel classes never start process-wide regions.</summary>
internal static class NoGcWindowTestHost
{
#pragma warning disable CA2255 // the test assembly's own seam, not a library's
    [ModuleInitializer]
    internal static void TurnTheWindowOff() => GamePlayScreen.NoGcWindowEnabled = false;
#pragma warning restore CA2255
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NoGcWindowCollection
{
    public const string Name = "No-GC window (not parallel)";
}

/// <summary>
/// M4 1.1 fix (R1, R3): the no-GC window's exit paths and sizing. The class runs alone (a region is process-wide, and other classes call <c>GC.Collect()</c>).
/// </summary>
[Collection(NoGcWindowCollection.Name)]
public sealed class NoGcWindowTests
{
    [Fact]
    public void A_held_window_is_ended_by_the_owner()
    {
        var window = new NoGcWindow();
        window.Begin();
        try
        {
            Assert.Equal("held", window.Report);
            Assert.True(window.IsOpen);
            Assert.Equal(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode);
        }
        finally
        {
            window.End();
        }

        Assert.Equal("held", window.Report);
        Assert.False(window.IsOpen);
        Assert.NotEqual(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode);
        Assert.True(window.ClosePending);
        NoGcWindow.RunPendingClose();
        Assert.False(window.ClosePending);
    }

    [Fact]
    public void A_collection_inside_the_window_gives_ended_early_and_no_throw()
    {
        var window = new NoGcWindow();
        window.Begin();
        Assert.Equal("held", window.Report);
        GC.Collect();
        var exception = Record.Exception(window.End);
        Assert.Null(exception);
        Assert.Equal("ended early", window.Report);
        Assert.NotEqual(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode);
    }

    [Fact]
    public void A_window_is_refused_while_another_region_is_open_and_never_ends_it()
    {
        Assert.True(GC.TryStartNoGCRegion(16L * 1024 * 1024));
        try
        {
            var window = new NoGcWindow();
            window.Begin();
            Assert.Equal("refused", window.Report);
            Assert.False(window.IsOpen);
            window.End();
            Assert.Equal("refused", window.Report);
            Assert.Equal(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode); // the other region is still open
        }
        finally
        {
            if (GCSettings.LatencyMode == GCLatencyMode.NoGCRegion)
            {
                GC.EndNoGCRegion();
            }
        }
    }

    [Fact]
    public void A_window_whose_region_ended_early_does_not_end_the_next_windows_region()
    {
        var a = new NoGcWindow();
        a.Begin();
        GC.Collect(); // a's region ends
        var b = new NoGcWindow();
        b.Begin();
        try
        {
            Assert.Equal("held", b.Report);
            a.End();
            Assert.Equal("ended early", a.Report);
            Assert.Equal(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode); // b's region is untouched
            Assert.True(b.IsOpen);
        }
        finally
        {
            b.End();
        }

        Assert.Equal("held", b.Report);
    }

    [Fact]
    public void The_request_follows_the_heap_count_between_the_floor_and_the_cap_and_says_when_it_cannot_hold()
    {
        Assert.Equal(NoGcWindow.FloorBytes, NoGcWindow.RequestBytes(1, out var undersized));
        Assert.False(undersized);
        Assert.Equal(32L * 1024 * 1024 * 12, NoGcWindow.RequestBytes(12, out undersized));
        Assert.False(undersized);
        Assert.Equal(NoGcWindow.CapBytes, NoGcWindow.RequestBytes(16, out undersized));
        Assert.False(undersized);
        Assert.Equal(NoGcWindow.CapBytes, NoGcWindow.RequestBytes(24, out undersized));
        Assert.True(undersized);
        var heaps = NoGcWindow.ReadHeapCount(out var source);
        Assert.True(heaps >= 1);
        Assert.False(string.IsNullOrEmpty(source));
    }

    /// <summary>What one frame's worth of short-lived small objects is: arrays under the large-object threshold, so they land in gen 0.</summary>
    private static void AllocateShortLived(long bytes)
    {
        for (long done = 0; done < bytes; done += 8 * 1024)
        {
            Volatile.Write(ref s_lastChunk, new byte[8 * 1024]); // escapes, so the JIT cannot put it on the stack
        }

        Volatile.Write(ref s_lastChunk, null);
    }

    private static byte[]? s_lastChunk;

    // M4 P5b (TODO(m4-throwaway-teleport-frame)): why the window closes with a collection. The runtime keeps a held region's gen 0 budget after EndNoGCRegion, so the next collection waits for that
    // much allocation; in the game it came at the next arrival and promoted everything since the build (57 to 95 ms live). If this fact stops holding, the closing collection can go.
    [Fact]
    public void After_EndNoGCRegion_alone_the_runtime_keeps_the_regions_gen0_budget()
    {
        Assert.True(GC.TryStartNoGCRegion(NoGcWindow.FloorBytes, true));
        AllocateShortLived(4L * 1024 * 1024);
        Assert.Equal(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode);
        GC.EndNoGCRegion();
        var count = GC.CollectionCount(0);
        AllocateShortLived(128L * 1024 * 1024);
        Assert.Equal(count, GC.CollectionCount(0));
    }

    [Fact]
    public void A_held_windows_end_leaves_one_gen0_collection_pending_that_gives_gen0_its_budget_back()
    {
        NoGcWindow.RunPendingClose(); // nothing left over from another test
        var window = new NoGcWindow();
        window.Begin();
        Assert.Equal("held", window.Report);
        AllocateShortLived(4L * 1024 * 1024);
        window.End();
        Assert.Equal("held", window.Report);
        Assert.True(window.ClosePending); // not run inside the UI step that ended the window
        Assert.True(double.IsNaN(window.ClosingCollectionMilliseconds));
        Assert.True(double.IsNaN(window.ClosingCollectionCpuMilliseconds)); // M5 7 D151

        var before = GC.CollectionCount(0);
        NoGcWindow.RunPendingClose(); // the next UI step's start
        Assert.True(GC.CollectionCount(0) > before, "the pending close ran no collection");
        Assert.False(window.ClosePending);
        Assert.True(window.ClosingCollectionMilliseconds >= 0.0, $"closing collection {window.ClosingCollectionMilliseconds} ms");
        Assert.True(window.ClosingCollectionCpuMilliseconds >= 0.0, $"closing collection cpu {window.ClosingCollectionCpuMilliseconds} ms"); // M5 7 D151: how many threads collected

        // The same 128 MiB that the region's budget let through without a collection (the test above) now runs at least one.
        var count = GC.CollectionCount(0);
        AllocateShortLived(128L * 1024 * 1024);
        Assert.True(GC.CollectionCount(0) > count, "gen 0 still had the region's budget after the window's end");
    }

    [Fact]
    public void A_window_that_ended_early_or_was_refused_runs_no_closing_collection()
    {
        NoGcWindow.RunPendingClose();
        var early = new NoGcWindow();
        early.Begin();
        GC.Collect(); // the region ends, and this collection has already reset the budget
        early.End();
        Assert.Equal("ended early", early.Report);
        Assert.False(early.ClosePending);
        NoGcWindow.RunPendingClose();
        Assert.True(double.IsNaN(early.ClosingCollectionMilliseconds));

        Assert.True(GC.TryStartNoGCRegion(16L * 1024 * 1024));
        try
        {
            var refused = new NoGcWindow();
            refused.Begin();
            refused.End();
            Assert.Equal("refused", refused.Report);
            Assert.False(refused.ClosePending);
            Assert.True(double.IsNaN(refused.ClosingCollectionMilliseconds));
        }
        finally
        {
            if (GCSettings.LatencyMode == GCLatencyMode.NoGCRegion)
            {
                GC.EndNoGCRegion();
            }
        }
    }

    [Fact]
    public void A_pending_close_leaves_a_window_that_is_open_again_alone_and_the_later_end_closes_it()
    {
        NoGcWindow.RunPendingClose();
        var a = new NoGcWindow();
        a.Begin();
        a.End();
        Assert.True(a.ClosePending);
        var b = new NoGcWindow();
        b.Begin();
        try
        {
            Assert.Equal("held", b.Report);
            NoGcWindow.RunPendingClose(); // a collection now would end b's region
            Assert.True(b.IsOpen);
            Assert.Equal(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode);
        }
        finally
        {
            b.End();
        }

        Assert.False(a.ClosePending); // b's end took the pending close over
        Assert.True(b.ClosePending);
        NoGcWindow.RunPendingClose();
        Assert.True(b.ClosingCollectionMilliseconds >= 0.0);
        Assert.True(double.IsNaN(a.ClosingCollectionMilliseconds));
    }

    [Fact]
    public void The_window_is_off_by_default_in_the_test_host()
    {
        Assert.False(GamePlayScreen.NoGcWindowEnabled);
    }
}
