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

    [Fact]
    public void The_window_is_off_by_default_in_the_test_host()
    {
        Assert.False(GamePlayScreen.NoGcWindowEnabled);
    }
}
