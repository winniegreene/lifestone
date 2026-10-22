namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The loot window's frame measure (M4 2.2 s0; <c>loot_frame_max_ms</c>): the slowest frame, in milliseconds, from the kill's corpse being opened (the open sent) to its close being settled or the sequence abandoned
/// (<see cref="HuntScenario.CorpseWindow"/>), over every corpse of the leg. One call per frame, no allocation, no clock of its own (the engine's delta, which is the previous frame's whole time, is the frame time), after the pattern of
/// <see cref="AttackFrameSampler"/>. Report-only in 2.2 (m4-plan.md section 5, item 3, D11: judged from 3.1).
/// </summary>
/// <remarks>
/// <para><b>The flag is the previous call's</b> (as the attack sampler's): the delta is the previous frame's whole time, so it is judged against the window as the call before it left it. The frame that applied the send
/// does not count (its delta is the frame before the send); the frame that applied the close's settlement does (its delta is a frame of the window). A frame flagged <c>skip</c> (the one after a saved review frame: the
/// read-back stalls it) is left out. A window opened twice without a close between is one window; a close with no window open is ignored.</para>
/// <para>The window is the scenario's, not the bus's: nothing in the event bus says "an open was sent", so the runner reads <see cref="HuntScenario.CorpseWindow"/> after the scenario's step each frame and passes it to
/// <see cref="Track"/>.</para>
/// </remarks>
public sealed class LootFrameSampler
{
    /// <summary>A frame in the window over this many milliseconds prints <c>loot_frame_over_50=yes</c> (a note, never a failure in 2.2).</summary>
    public const double WarnMs = 50.0;

    private bool _open;
    private bool _wasOpen;
    private double _maxMs = double.NaN;
    private long _frames;
    private long _windows;

    /// <summary>The slowest frame inside a loot window, in milliseconds, or null when none was sampled.</summary>
    public double? LootFrameMaxMs => double.IsNaN(_maxMs) ? null : _maxMs;

    /// <summary>The frames counted inside a loot window.</summary>
    public long Frames => _frames;

    /// <summary>The windows that were opened and closed.</summary>
    public long Windows => _windows;

    /// <summary>True while a loot window is open.</summary>
    public bool WindowOpen => _open;

    /// <summary>A corpse's open was sent: the window opens unless it already is.</summary>
    public void Open() => _open = true;

    /// <summary>The corpse's close was settled, or the sequence was abandoned: the window closes; ignored when none is open.</summary>
    public void Close()
    {
        if (_open)
        {
            _open = false;
            _windows++;
        }
    }

    /// <summary>Opens or closes the window to match <paramref name="windowOpen"/> (the scenario's <see cref="HuntScenario.CorpseWindow"/>).</summary>
    /// <param name="windowOpen">Whether the scenario is between a corpse's open being sent and its close being settled or abandoned.</param>
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
