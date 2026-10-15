using Lifestone.Net.Dispatch;

namespace Lifestone.Client.Session.StandIn;

/// <summary>
/// The timed steps of an offline session's stand-in data (<see cref="StandInSessions"/>): each step feeds the reducer at
/// a session-clock time, inside <c>GameSession.Tick</c> on the game thread, so stand-in progress (a data check that takes
/// two seconds) reaches the screens exactly as a server's messages would.
/// </summary>
/// <remarks>Game thread only.</remarks>
internal sealed class StandInSchedule
{
    private readonly List<(double At, long Order, Action<IAcSessionSink> Step)> _steps = [];
    private long _order;

    /// <summary>The steps not run yet.</summary>
    internal int Pending => _steps.Count;

    /// <summary>Schedules <paramref name="step"/> for session-clock time <paramref name="at"/>; steps due together run in the order scheduled.</summary>
    internal void At(double at, Action<IAcSessionSink> step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var entry = (at, _order++, step);
        var index = _steps.Count;
        while (index > 0 && _steps[index - 1].At > at)
        {
            index--;
        }

        _steps.Insert(index, entry);
    }

    /// <summary>Runs every step due at <paramref name="now"/>, oldest first (a step may schedule more); returns how many ran.</summary>
    internal int RunDue(double now, IAcSessionSink sink)
    {
        var ran = 0;
        while (_steps.Count > 0 && _steps[0].At <= now)
        {
            var step = _steps[0].Step;
            _steps.RemoveAt(0);
            step(sink);
            ran++;
        }

        return ran;
    }

    /// <summary>The visit that waits to be released (<see cref="StandInVisit.HoldEntry"/>), with the seconds from the release to the entry; null when none is held.</summary>
    internal Action<double>? HeldEntry { get; set; }

    /// <summary>Forgets every step (a reconnect runs the stand-in again).</summary>
    internal void Clear()
    {
        _steps.Clear();
        HeldEntry = null;
    }
}
