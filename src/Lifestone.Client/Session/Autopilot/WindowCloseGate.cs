using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>What the host does with a close request of the game window.</summary>
public enum CloseDecision
{
    /// <summary>Quit now (the game's own log off, as before).</summary>
    QuitNow,

    /// <summary>Wait: the autopilot was asked to stop (it recalls home first); <see cref="WindowCloseGate.Tick"/> says when to quit.</summary>
    Wait,
}

/// <summary>
/// The window close during an autopilot run (M5 7 F18): a retail player recalls to safety before logging out, and a character logged off on the hunting ground stays in the world for its logout and can be killed there. When the dial is
/// running, the first close request asks the autopilot to stop (its recall home, <see cref="AutopilotHostLogic.WindowCloseReason"/>) and the game waits; the game quits when the dial is done, or after <see cref="WaitSeconds"/> anyway.
/// A second close request is an immediate quit. With the dial in any other mode (off, done, interrupted, halted) a close quits at once. Engine-free; the host (<c>SessionHost</c>'s close notification) asks <see cref="Request"/> and the
/// autopilot host ticks it.
/// </summary>
public sealed class WindowCloseGate
{
    /// <summary>Lifestone's own bound, in seconds, on how long a window close waits for the autopilot's recall home before the game quits anyway (a recall home is a 30 s station, a reconnect can take longer; the person asked to leave).</summary>
    public const double WaitSeconds = 60;

    private readonly Func<AutopilotMode> _mode;
    private readonly Action<string> _stop;
    private readonly Action<string> _log;
    private bool _waiting;
    private double _waited;

    /// <summary>A gate over the dial.</summary>
    /// <param name="mode">The dial's mode now.</param>
    /// <param name="stop">Asks the dial to stop with a reason token (<see cref="AutopilotHostLogic.Stop"/>).</param>
    /// <param name="log">One INFO line.</param>
    public WindowCloseGate(Func<AutopilotMode> mode, Action<string> stop, Action<string> log)
    {
        _mode = mode ?? throw new ArgumentNullException(nameof(mode));
        _stop = stop ?? throw new ArgumentNullException(nameof(stop));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>True while a first close request waits for the autopilot.</summary>
    public bool Waiting => _waiting;

    /// <summary>A close request of the window.</summary>
    /// <returns>Whether the game quits now or waits.</returns>
    public CloseDecision Request()
    {
        if (_waiting)
        {
            _log("autopilot: a second close request: the game quits now");
            return CloseDecision.QuitNow;
        }

        var mode = _mode();
        if (mode is not (AutopilotMode.Running or AutopilotMode.Stopping))
        {
            return CloseDecision.QuitNow;
        }

        _waiting = true;
        _waited = 0;
        if (mode == AutopilotMode.Running)
        {
            _stop(AutopilotHostLogic.WindowCloseReason);
        }

        _log($"autopilot: the window was closed during a run: the autopilot recalls home first and the game quits when it is done ({WaitSeconds:0} s at most; a second close quits at once)");
        return CloseDecision.Wait;
    }

    /// <summary>Counts <paramref name="delta"/> seconds of the wait.</summary>
    /// <param name="delta">Seconds since the last tick.</param>
    /// <returns>True when the game quits now: the dial is no longer running or stopping, or the bound passed. Once only.</returns>
    public bool Tick(double delta)
    {
        if (!_waiting)
        {
            return false;
        }

        _waited += delta;
        var mode = _mode();
        if (mode is not (AutopilotMode.Running or AutopilotMode.Stopping))
        {
            _waiting = false;
            _log($"autopilot: the run ended ({mode.ToString().ToLowerInvariant()}) after the window was closed: the game quits");
            return true;
        }

        if (_waited >= WaitSeconds)
        {
            _waiting = false;
            _log($"autopilot: the run did not end within {WaitSeconds:0} s of the window being closed: the game quits anyway");
            return true;
        }

        return false;
    }
}
