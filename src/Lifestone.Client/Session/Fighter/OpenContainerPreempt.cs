using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Fighter;

/// <summary>
/// M6 14 recall unblock (m6-recall-preempt; live 2026-11-14 17:27:19 to 17:32:21 UTC, the climber's death in the Tusker Quarters): a way out (Lifestone Recall for an emergency, a retreat, an urgent or ordinary trip home) and an
/// emergency drink or heal go before the loot. An open ground container (a corpse's view) is closed first with <see cref="IActionApi.CloseContainer"/> and the server's answer checked (its 0052: the view no longer shows it), so the
/// Use lane its open held is free at once (<c>OpenContainerTracker.HoldsLane</c> ends with the view) and the way out never waits it out.
/// </summary>
/// <remarks>
/// Check, act, check: before, the read model's view names the open container; act, the close goes; after, its outcome and the view are read again and named. A close the server did not answer within
/// <see cref="ActionPolicy.CloseContainerSeconds"/> (the tracker's timeout), or a view still open after it, is named and the caller goes on anyway (ACE's own answer judges what it sends next). The same container is not closed
/// again for <see cref="RetrySeconds"/>: one close a container, never a loop.
/// </remarks>
internal sealed class OpenContainerPreempt(IActionApi actions, GameState state, ILogSink log, string category, string who)
{
    /// <summary>The seconds after a close's answer during which the same container is not closed again (Lifestone's own: twice the close's own timeout).</summary>
    internal const double RetrySeconds = 2 * ActionPolicy.CloseContainerSeconds;

    private ActionHandle? _close;
    private uint _closing;
    private uint _lastClosed;
    private double _lastClosedAt = double.NegativeInfinity;

    /// <summary>The closes this preempt sent.</summary>
    public int Closes { get; private set; }

    /// <summary>The closes whose check after found the view closed.</summary>
    public int Closed { get; private set; }

    /// <summary>
    /// One frame. True while the close is out (the caller sends nothing yet); false when no ground container is open, or the close was answered and checked (or given up by name): the caller goes on.
    /// </summary>
    /// <param name="what">What waits for the close, for the log (e.g. "Lifestone Recall", "the Health Tonic").</param>
    /// <param name="now">The session clock.</param>
    public bool Step(string what, double now)
    {
        if (_close is { } close)
        {
            if (!close.IsCompleted)
            {
                return true; // bounded by the close's own timeout (ActionPolicy.CloseContainerSeconds)
            }

            var still = state.Containers.ViewedGuid == _closing;
            var outcome = close.Outcome is { } o ? o.Kind + (o.Detail is null ? string.Empty : ": " + o.Detail) : "no outcome";
            if (still)
            {
                log.Warn(category, string.Create(CultureInfo.InvariantCulture, $"{who}: preempt: check after: the close #{close.Id} of 0x{_closing:X8} ended {outcome}, and the view still shows it: {what} goes anyway (ACE's own answer judges it; m6-recall-preempt)"));
            }
            else
            {
                Closed++;
                log.Info(category, string.Create(CultureInfo.InvariantCulture, $"{who}: preempt: check after: the close #{close.Id} of 0x{_closing:X8} ended {outcome}; the view is closed, the Use lane its open held is free: {what} goes now (m6-recall-preempt)"));
            }

            (_lastClosed, _lastClosedAt) = (_closing, now);
            (_close, _closing) = (null, 0);
            return false;
        }

        var open = state.Containers.ViewedGuid;
        if (open == 0 || (open == _lastClosed && now - _lastClosedAt < RetrySeconds))
        {
            return false;
        }

        log.Info(category, $"{who}: preempt: check before: the ground container 0x{open:X8} is open (busy: {actions.BusyReason ?? "nothing"}): it is closed first, then {what} goes; a way out goes before the loot (m6-recall-preempt)");
        _close = actions.CloseContainer(open, ActionSource.Automation);
        _closing = open;
        Closes++;
        if (_close.IsCompleted)
        {
            return Step(what, now); // refused or answered at once: checked now
        }

        return true;
    }

    /// <summary>Forgets a close in hand (a new recall, a new emergency): the next <see cref="Step"/> reads the view afresh.</summary>
    public void Reset() => (_close, _closing, _lastClosed, _lastClosedAt) = (null, 0, 0, double.NegativeInfinity);
}
