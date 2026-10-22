using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The attack window's frame measure (M4 1.3 s2; <c>attack_frame_max_ms</c>): the slowest frame, in milliseconds, from an attack's send (<see cref="AttackRequested"/>, whoever sent it) to the 0x01A7 with a non-zero
/// code that ends its run (<see cref="AttackSequenceState.Done"/>), over every run of the leg. One call per frame, no allocation, no clock of its own (the engine's delta, which is the previous frame's whole time,
/// is the frame time), as <see cref="FightFrameSampler"/> does for the cast window. Report-only in 1.3 (m4-plan.md section 5, item 3, D11: judged from 2.1).
/// </summary>
/// <remarks>
/// <para><b>The flag is the previous call's</b> (as the fight sampler's, package 2 review finding 11): the delta is the previous frame's whole time, so it is judged against the window as the call before it left it.
/// The frame that applied the send does not count (its delta is the frame before the send); the frame that applied the Done does (its delta is a frame of the run). A frame flagged <c>skip</c> (the one after a saved
/// review frame: the read-back stalls it) is left out. One send gives a whole auto-repeated run, so a second send inside a run does not open a second window; a Done with no window open is ignored.</para>
/// </remarks>
public sealed class AttackFrameSampler
{
    private bool _open;
    private bool _wasOpen;
    private double _maxMs = double.NaN;
    private long _frames;
    private long _windows;
    private long _runs;

    /// <summary>The slowest frame inside an attack window, in milliseconds, or null when none was sampled.</summary>
    public double? AttackFrameMaxMs => double.IsNaN(_maxMs) ? null : _maxMs;

    /// <summary>The frames counted inside an attack window.</summary>
    public long Frames => _frames;

    /// <summary>The runs (windows) that were opened and closed by a Done.</summary>
    public long Windows => _windows;

    /// <summary>The sends that opened a window (a send inside an open window is not counted).</summary>
    public long Runs => _runs;

    /// <summary>True while an attack window is open.</summary>
    public bool WindowOpen => _open;

    /// <summary>An attack was sent (<see cref="AttackRequested"/>): the window opens unless it already is.</summary>
    public void Open()
    {
        if (!_open)
        {
            _open = true;
            _runs++;
        }
    }

    /// <summary>The run ended (a 0x01A7 with a non-zero code): the window closes; ignored when none is open.</summary>
    public void Close()
    {
        if (_open)
        {
            _open = false;
            _windows++;
        }
    }

    /// <summary>Opens and closes the window from the session's own events (<see cref="AttackRequested"/> and <see cref="AttackChanged"/> reaching <see cref="AttackSequenceState.Done"/>). Dispose it to stop.</summary>
    /// <param name="events">The session's event bus (<see cref="IGameSessionView.Events"/>).</param>
    public IDisposable Attach(IEventBus events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var requested = events.Subscribe<AttackRequested>(OnRequested);
        var changed = events.Subscribe<AttackChanged>(OnChanged);
        return new Subscriptions(requested, changed);
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

    private void OnRequested(in AttackRequested e) => Open();

    private void OnChanged(in AttackChanged e)
    {
        if (e.State == AttackSequenceState.Done)
        {
            Close();
        }
    }

    private sealed class Subscriptions(IDisposable first, IDisposable second) : IDisposable
    {
        public void Dispose()
        {
            first.Dispose();
            second.Dispose();
        }
    }
}
