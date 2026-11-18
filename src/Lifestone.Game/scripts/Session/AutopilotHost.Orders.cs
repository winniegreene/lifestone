using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Autopilot.Commands;
using Lifestone.Client.Session.Autopilot.Steer;
using Lifestone.Client.Session.Contracts;
using Lifestone.Game.Core;

namespace Lifestone.Game.Sessions;

/// <summary>
/// The command registry's host side (the owner's ruling of 2026-11-17: finer-grained commands for her, one job each): one <see cref="CommandRunner"/> over the dial, fed by the chat's orders (<c>/trip</c>, <c>/rebuff</c>,
/// <c>/quest &lt;key&gt;</c>, <c>/ladder</c>, <c>/help</c>, ...) and by the steer desk's command orders (<c>--autopilot-steer-orders=on</c>), ticked after the dial each frame. Its lines go to her chat window. A person's /stop goes
/// through it (the queue is emptied, then the stop). It claims the end of a task run an order started (the den run), so the game does not quit for it.
/// </summary>
public sealed partial class AutopilotHost
{
    /// <summary>The command runner (the checks read it).</summary>
    internal CommandRunner? Orders => _orders;

    private void SetupOrders(AutopilotHostLogic logic)
    {
        var dial = new DialCommandTarget(logic, () => _host?.Session, person => person ? Auto() : PressByOrder(), StopHereCore);
        _orders = new CommandRunner(CommandRegistry.Default, dial, GodotLogSink.Instance);
        _orders.Line += line => ShowChat(_ui, line);
        logic.AfterRun ??= _orders.ClaimEnd;
    }

    /// <summary>The chat's order: null when the name is no command (the line goes its usual way); else the runner takes it and its lines reach the chat through the runner.</summary>
    private IReadOnlyList<string>? ChatOrder(string name, string line)
    {
        if (_orders is not { } orders || !orders.Knows(name))
        {
            return null;
        }

        orders.Submit(line, "chat", person: true);
        return [];
    }

    /// <summary>An order's start (the ladder after the den run, a desk's /ladder from off or done): an unattended run stays unattended (the desk's press), a person's run is pressed as their /auto would be, with no person counted.</summary>
    private string PressByOrder()
    {
        var mode = _logic.Mode;
        _pressRefusal = null;
        if (_unattendedRun)
        {
            Press(OrderDesk.StartedBy);
        }
        else
        {
            Press(startedBy: null, reason: AutopilotHostLogic.ResumeReason);
        }

        return _logic.Mode == AutopilotMode.Running ? $"the autopilot runs ({mode.ToString().ToLowerInvariant()} before)"
            : _pressPending ? "the autopilot is reading its parts; it starts when they are read"
            : "the autopilot did not start (" + (_pressRefusal ?? _logic.Status.LastDecision) + ")";
    }
}
