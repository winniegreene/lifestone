namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The vendor window's frame measure (M4 3.2 s4a; <c>vendor_frame_max_ms</c>): the slowest frame, in milliseconds, from a vendor's open being sent to the visit's end (<see cref="HuntScenario.VendorWindow"/>), over both vendors of the
/// loop's end (Renald the Elder's sale, Thelnoth Cort's purchase). One call per frame, no allocation, no clock of its own (the engine's delta, which is the previous frame's whole time, is the frame time), after the pattern of
/// <see cref="LootFrameSampler"/>. Report-only in 3.2 (m4-plan.md section 5, item 3, D11: judged at 4.2).
/// </summary>
/// <remarks>
/// <para><b>The flag is the previous call's</b> (as the loot sampler's): the delta is the previous frame's whole time, so it is judged against the window as the call before it left it. The frame that applied the open's send does not
/// count (its delta is the frame before the send); the frame that applied the visit's end does (its delta is a frame of the window). A frame flagged <c>skip</c> (the one after a saved review frame: the read-back stalls it) is left out.
/// A window opened twice without an end between is one window; an end with no window open is ignored. The window ends when the hunt leaves the shop (the teleport away is the vendor's close: ACE has no close message, and the model closes the
/// vendor by distance), so the arrival frames of a teleport are not in it.</para>
/// <para>The window is the scenario's, not the bus's: the runner reads <see cref="HuntScenario.VendorWindow"/> after the scenario's step each frame and passes it to <see cref="Track"/>.</para>
/// </remarks>
public sealed class VendorFrameSampler
{
    /// <summary>A frame in the window over this many milliseconds is a note in the log (report-only in 3.2; the judge is 4.2's).</summary>
    public const double WarnMs = 50.0;

    private bool _open;
    private bool _wasOpen;
    private double _maxMs = double.NaN;
    private long _frames;
    private long _windows;

    /// <summary>The slowest frame inside a vendor window, in milliseconds, or null when none was sampled.</summary>
    public double? VendorFrameMaxMs => double.IsNaN(_maxMs) ? null : _maxMs;

    /// <summary>The frames counted inside a vendor window.</summary>
    public long Frames => _frames;

    /// <summary>The windows that were opened and closed (one per vendor visited).</summary>
    public long Windows => _windows;

    /// <summary>True while a vendor window is open.</summary>
    public bool WindowOpen => _open;

    /// <summary>A vendor's open was sent: the window opens unless it already is.</summary>
    public void Open() => _open = true;

    /// <summary>The visit ended (the hunt leaves the shop, or the sale was abandoned): the window closes; ignored when none is open.</summary>
    public void Close()
    {
        if (_open)
        {
            _open = false;
            _windows++;
        }
    }

    /// <summary>Opens or closes the window to match <paramref name="windowOpen"/> (the scenario's <see cref="HuntScenario.VendorWindow"/>).</summary>
    /// <param name="windowOpen">Whether the scenario is between a vendor's open being sent and the visit's end.</param>
    public void Track(bool windowOpen)
    {
        if (windowOpen)
        {
            Open();
        }
        else
        {
            Close();
        }
    }

    /// <summary>Takes one frame. The window's state now is the next call's judge (the remarks).</summary>
    /// <param name="deltaSeconds">The engine's delta, in seconds: the previous frame's whole time.</param>
    /// <param name="skip">True for a frame to leave out (the one after a saved review frame).</param>
    public void Step(double deltaSeconds, bool skip)
    {
        var inWindow = _wasOpen;
        _wasOpen = _open;
        if (skip || deltaSeconds <= 0 || double.IsNaN(deltaSeconds) || !inWindow)
        {
            return;
        }

        _frames++;
        var ms = deltaSeconds * 1000.0;
        if (double.IsNaN(_maxMs) || ms > _maxMs)
        {
            _maxMs = ms;
        }
    }
}
