using System.Globalization;
using System.Runtime;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>
/// M4 1.1 s4 and its fix (<c>TODO(m3-ui-step-gc-margin)</c>): one no-GC window (<see cref="GC.TryStartNoGCRegion(long, bool)"/>) over the staged shell build. Only the instance whose start returned true
/// owns the process's region and ends it (a static owner), so a screen that was refused, or whose region ended early, never ends another screen's. The budget is per server-GC heap, so the request is sized
/// from the heap count (<see cref="RequestBytes"/>).
/// <para>M4 P5b (<c>TODO(m4-throwaway-teleport-frame)</c>): <see cref="GC.EndNoGCRegion"/> leaves gen 0's allocation budget at the region's size, so after a held window the next collection waited
/// for up to <see cref="CapBytes"/> of allocation and then promoted every survivor since the build: one 57 to 95 ms pause at the next arrival (the throwaway's Holtburg teleport, each journey's first).
/// The owner that ends a held region therefore leaves one gen 0 collection pending, which <see cref="RunPendingClose"/> runs at the start of the next retail UI step, before that step's clock starts
/// (the window keeps collections out of the UI steps, so its own closing collection is not run inside one): gen 0 then holds little more than the window's allocations, and the collection gives it its normal
/// budget back (<see cref="ClosingCollectionMilliseconds"/>).</para>
/// </summary>
internal sealed class NoGcWindow
{
    /// <summary>What the build allocated on the game thread in the live probe (14,436 KiB), rounded up to 16 MiB.</summary>
    public const long MeasuredBuildBytes = 16L * 1024 * 1024;

    /// <summary>Render, audio and net threads allocate against the same heaps during the multi-frame window, and the figure above counts the game thread only: twice the measured bytes a heap.</summary>
    public const long Margin = 2;

    /// <summary>The smallest request: the 240 MiB that held in the live probe (24 MiB did not).</summary>
    public const long FloorBytes = 240L * 1024 * 1024;

    /// <summary>
    /// The largest request: the region commits its memory up front, so the cap bounds that commit at 512 MiB (a small share of the machines this game runs on). It holds the formula up to 16 heaps
    /// (<see cref="MeasuredBuildBytes"/> times <see cref="Margin"/> times 16); with more heaps the window says it is undersized.
    /// </summary>
    public const long CapBytes = 512L * 1024 * 1024;

    private static NoGcWindow? s_owner;
    private static NoGcWindow? s_pendingClose;

    private bool _started;

    /// <summary>"off", "refused", "held" or "ended early".</summary>
    public string Report { get; private set; } = "off";

    /// <summary>The milliseconds the start cost.</summary>
    public double StartMilliseconds { get; private set; }

    /// <summary>The pause of the gen 0 collection that closed a held window, in milliseconds; NaN until it runs, and when none is due (the window was off, refused or ended early: a collection had already reset the budget).</summary>
    public double ClosingCollectionMilliseconds { get; private set; } = double.NaN;

    /// <summary>True from the end of a held window until <see cref="RunPendingClose"/> has run its closing collection.</summary>
    public bool ClosePending => ReferenceEquals(Volatile.Read(ref s_pendingClose), this);

    /// <summary>The GC heaps the request was sized for.</summary>
    public int Heaps { get; private set; }

    /// <summary>Where the heap count came from ("config GCHeapCount=n", "processors" or "workstation").</summary>
    public string HeapSource { get; private set; } = "n/a";

    /// <summary>The bytes asked for.</summary>
    public long RequestedBytes { get; private set; }

    /// <summary>True when the formula's need (per heap, times the heaps) is more than <see cref="CapBytes"/>: by the per-heap estimate the window may end early on this many heaps (an estimate, not a measurement: the held or ended-early state is).</summary>
    public bool Undersized { get; private set; }

    /// <summary>True while this instance owns the open region.</summary>
    public bool IsOpen => _started && ReferenceEquals(Volatile.Read(ref s_owner), this);

    /// <summary>The GC's heap count: the configured one when the runtime reports it, else the logical processors under server GC (the most the heaps can be), else 1.</summary>
    public static int ReadHeapCount(out string source)
    {
        if (!GCSettings.IsServerGC)
        {
            source = "workstation";
            return 1;
        }

        try
        {
            foreach (var pair in GC.GetConfigurationVariables())
            {
                if (pair.Key.Contains("HeapCount", StringComparison.OrdinalIgnoreCase)
                    && Convert.ToInt64(pair.Value, CultureInfo.InvariantCulture) is > 0 and var configured and <= 1024)
                {
                    source = $"config {pair.Key}={configured}";
                    return (int)configured;
                }
            }
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            // an unreadable value falls through to the processors
        }

        source = "processors";
        return Math.Max(1, Environment.ProcessorCount);
    }

    /// <summary>The request for <paramref name="heaps"/> heaps: the need per heap times the heaps, at least <see cref="FloorBytes"/> and at most <see cref="CapBytes"/>; <paramref name="undersized"/> when the cap cut it.</summary>
    public static long RequestBytes(int heaps, out bool undersized)
    {
        var need = MeasuredBuildBytes * Margin * Math.Max(1, heaps);
        undersized = need > CapBytes;
        return Math.Clamp(need, FloorBytes, CapBytes);
    }

    /// <summary>Starts the window; "refused" when the runtime will not (another region is open, or the size is not allowed).</summary>
    public void Begin()
    {
        Heaps = ReadHeapCount(out var source);
        HeapSource = source;
        RequestedBytes = RequestBytes(Heaps, out var undersized);
        Undersized = undersized;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var opened = false;
        try
        {
            // The runtime ends a region that is already open when a second start is asked for (and then throws), so another region is looked for first: it is refused without a call.
            opened = GCSettings.LatencyMode != GCLatencyMode.NoGCRegion && GC.TryStartNoGCRegion(RequestedBytes, true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException)
        {
            opened = false;
        }

        StartMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (opened)
        {
            _started = true;
            Volatile.Write(ref s_owner, this); // a stale owner (whose region ended early) is replaced by the region that really is open
        }

        Report = opened ? "held" : "refused";
    }

    /// <summary>Ends the region if this instance owns it; says "ended early" when the runtime had already ended it (an induced or over-budget collection, or another window's start).</summary>
    public void End()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        if (!ReferenceEquals(Interlocked.CompareExchange(ref s_owner, null, this), this))
        {
            Report = "ended early"; // our region ended and another window's began: that one is not ours to end
            return;
        }

        try
        {
            if (GCSettings.LatencyMode == GCLatencyMode.NoGCRegion)
            {
                GC.EndNoGCRegion();
                Volatile.Write(ref s_pendingClose, this); // the closing collection runs at the next UI step's start (RunPendingClose)
            }
            else
            {
                Report = "ended early";
            }
        }
        catch (InvalidOperationException)
        {
            Report = "ended early";
        }
    }

    /// <summary>
    /// Runs the gen 0 collection a held window's end left pending, so the next collection comes at gen 0's normal budget rather than the region's (see the class remarks); the retail UI layer calls it at
    /// the start of each step, before the step's clock. Nothing is pending most frames (one field read). A window that is open again is left alone (a collection would end it); its own end closes it.
    /// </summary>
    public static void RunPendingClose()
    {
        if (Volatile.Read(ref s_pendingClose) is not { } window || GCSettings.LatencyMode == GCLatencyMode.NoGCRegion)
        {
            return;
        }

        Volatile.Write(ref s_pendingClose, null);
        var pause = GC.GetTotalPauseDuration();
        GC.Collect(0, GCCollectionMode.Forced, blocking: true);
        window.ClosingCollectionMilliseconds = (GC.GetTotalPauseDuration() - pause).TotalMilliseconds;
    }
}
