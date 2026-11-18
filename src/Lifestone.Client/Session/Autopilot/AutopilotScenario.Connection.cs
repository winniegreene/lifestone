using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 6 MH (map-menu.md 5 and 6.7; closes <c>m5-8-orphaned-login</c>): the orphaned login. ACE can log a character in to no landblock while its old session is still held (facts.md section 6:
/// @95ef426d Network/Handlers/AuthenticationHandler.cs:157-196; the old session is held 2 s, Network/SessionTerminationDetails.cs:12, before DropSession logs its player off, Network/Session.cs:300-322). The client then sees an
/// empty world: nothing on its side names it. Only the server mod can: a player-scope request is answered <c>not-in-world</c>.
/// <list type="bullet">
/// <item><b>The probe</b> (check): after each login (the autopilot's start) and each reconnect's success, the known state sends one <see cref="PlayerTools.WhereAmI"/> (<see cref="ActionSource.Automation"/>; a read: <see cref="ModVerbCounter"/> counts
/// no <c>where</c>) and waits for it before it classifies or audits anything. It asks only a mod that answered this session (the tools <see cref="ToolAvailability.Ready"/>: the reconnect's login confirms the mod's status first); otherwise one line names why it is not asked. The tool's own tracker meets the mod's <c>not-in-world</c> with its bounded wait and one resend (<c>ToolTracker</c>, <c>ActionPolicy.ToolWorldWaitSeconds</c>), so
/// an orphan that heals by itself within that wait reads as in the world; one that does not ends the request as a server error whose detail starts with <see cref="ToolErrors.NotInWorld"/>: that, and only that, is an orphan.</item>
/// <item><b>The one re-login</b> (act): one log-off (<see cref="IActionApi.LogOff"/>), a wait of <see cref="OrphanReloginWaitSeconds"/> for the old session to drop and its character to log out, then the reconnect's own first try
/// (<see cref="IAutopilotSession.Reconnect"/>, stepped by the reconnect's machine: a failed try goes on to the reconnect's later tries and waits). Each line says <c>autopilot: orphaned login: ... (m5-8-orphaned-login)</c>.</item>
/// <item><b>The check after</b>: the reconnect's success arms the probe again. A probe that finds the player in the world ends the episode (a later orphan, after a later drop, gets its own re-login); a second orphan in a row stops the run
/// <see cref="OrphanedLoginStop"/> with no recall before it (the server has no landblock for the player: a cast would only time out).</item>
/// </list>
/// The race itself (ACE booting the old session while the new one logs in) cannot be staged offline: the seam tests (AutopilotOrphanTests) answer the <c>where</c> with the mod's <c>not-in-world</c>, and a live round would need two
/// clients on one account.
/// </summary>
public sealed partial class AutopilotScenario
{
    /// <summary>The stop when the player is orphaned again after the one re-login.</summary>
    public const string OrphanedLoginStop = "failed:known-state:orphaned-login";

    /// <summary>The TODO name the orphaned login's lines carry (m5-plan.md section 9; closed by M6 6 MH as these lines).</summary>
    public const string OrphanedLoginTodo = "m5-8-orphaned-login";

    /// <summary>
    /// Seconds between the orphan's log-off and its re-login: the booted old session is held 2 s (@95ef426d Network/SessionTerminationDetails.cs:12) before DropSession logs its character off (Network/Session.cs:300-322), and a
    /// logout finishes no sooner than 6 s after it began (Network/Session.cs:149-153): about 8-10 s (facts.md section 6, LM13's VERIFIED.md nuance 1). The upper end, so the re-login meets no old session.
    /// </summary>
    public const double OrphanReloginWaitSeconds = 10;

    private enum OrphanStage
    {
        None,
        LoggingOff,
        Waiting,
    }

    private bool _orphanProbed; // false: the probe is due (the start, and each reconnect's success arms it again)
    private string _orphanAfter = "login";
    private ToolHandle<WhereResult>? _orphanWhere;
    private int _orphanRelogins; // the re-logins since the last probe that found the player in the world (at most one)
    private OrphanStage _orphanStage;
    private ActionHandle? _orphanLogOff;
    private double _orphanWaitUntil;
    private string _reloginName = "orphaned login"; // what the re-login's lines call themselves: the orphan's, or M6 7 AJ's body apart
    private string _reloginTodo = OrphanedLoginTodo;

    /// <summary>The orphan re-logins this run (the seam tests read it).</summary>
    public int OrphanRelogins { get; private set; }

    /// <summary>The reconnect's success: the probe is due again in the known state (the check after the act).</summary>
    private void ArmOrphanProbe(string after = "reconnect")
    {
        _orphanProbed = false;
        _orphanAfter = after;
        _orphanWhere = null; // M6 6 FXM2 (6-M6): a `where` of the session that dropped answers for nobody; the probe asks its own
        _apartWhere = null; // M6 7 AJ: the same for the body-apart check's
    }

    /// <summary>
    /// The known state's probe: null when it is done (or not due) and the known state goes on; <see cref="ScenarioStatus.Running"/> while the <c>where</c> is out; the orphan's re-login or its stop otherwise.
    /// </summary>
    private ScenarioStatus? OrphanProbe(IGameSessionView session)
    {
        if (_orphanProbed)
        {
            return null;
        }

        if (_orphanWhere is null)
        {
            var tools = session.Tools.Availability;
            if (tools != ToolAvailability.Ready)
            {
                // Only a mod that has answered this session is asked: the reconnect's login confirms the mod's status before its success (LoginScenario's status step), and the session probes it on each world entry. A mod that never
                // answered (missing, not loopback, an offline session, or a status still in flight) is named here instead of holding the known state on a request nothing will answer.
                _orphanProbed = true;
                var why = tools == ToolAvailability.Unknown ? "the mod has not answered this session yet" : PlayerTools.Refusal(tools);
                _log.Info(LogCategory, $"autopilot: orphan probe after the {_orphanAfter}: not asked: the player tools are {tools.ToString().ToLowerInvariant()} ({why}), so an orphaned login cannot be read here; the known state goes on ({OrphanedLoginTodo})");
                return null;
            }

            _orphanWhere = session.Tools.WhereAmI(ActionSource.Automation);
            _log.Info(LogCategory, $"autopilot: orphan probe after the {_orphanAfter}: one `where` (a read: no mod verb is counted); the known state waits for it ({OrphanedLoginTodo})");
            return ScenarioStatus.Running;
        }

        if (_orphanWhere.Outcome is not { } outcome)
        {
            return ScenarioStatus.Running; // the tool's own limits end it (ActionPolicy.ToolSeconds, with the not-in-world wait and its one resend inside)
        }

        var where = _orphanWhere;
        _orphanWhere = null;
        _orphanProbed = true;
        if (outcome.Kind == ActionOutcomeKind.Confirmed && where.Result is { } found)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot: orphan probe after the {_orphanAfter}: in the world (the server has the player in cell 0x{found.Position.CellId:X8}{(found.Teleporting ? ", teleporting" : string.Empty)}); the known state goes on"));
            _orphanRelogins = 0;
            return null;
        }

        if (outcome.Kind == ActionOutcomeKind.ServerError && outcome.Detail?.StartsWith(ToolErrors.NotInWorld, StringComparison.Ordinal) == true)
        {
            return Orphaned(session, outcome.Detail);
        }

        _log.Warn(LogCategory, $"autopilot: orphan probe after the {_orphanAfter}: the `where` was not confirmed ({outcome.Kind}: {outcome.Detail ?? "no detail"}): not the mod's {ToolErrors.NotInWorld}, so no orphan is read; the known state goes on ({OrphanedLoginTodo})");
        return null;
    }

    /// <summary>The mod said <c>not-in-world</c> after its own wait: the one re-login (log off, wait, the reconnect's try), or the stop on a second orphan in a row.</summary>
    private ScenarioStatus Orphaned(IGameSessionView session, string detail)
    {
        if (_orphanRelogins >= 1 || _host is null)
        {
            var why = _host is null
                ? $"the server has no landblock for the player ({detail}) and the host gave the autopilot no way to log in again (IAutopilotSession) ({OrphanedLoginTodo})"
                : $"the server again has no landblock for the player after the one re-login ({detail}) ({OrphanedLoginTodo})";
            _log.Warn(LogCategory, "autopilot: orphaned login: " + why);
            _finalDone = true; // no recall before this stop: the server has the player in no landblock, so a cast would only time out
            return Stop(OrphanedLoginStop, ok: false, why);
        }

        _orphanRelogins++;
        OrphanRelogins++;
        (_reloginName, _reloginTodo) = ("orphaned login", OrphanedLoginTodo);
        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot: orphaned login: the server has no landblock for the player after the {_orphanAfter} ({detail}); one log-off, a wait of {OrphanReloginWaitSeconds:0} s, then one re-login, cycle {_cycle} ({OrphanedLoginTodo})"));
        return StartRelogin(session);
    }

    /// <summary>The act both rules share: one log-off, then <see cref="OrphanReloginStep"/> steps the wait and the reconnect's first try.</summary>
    private ScenarioStatus StartRelogin(IGameSessionView session)
    {
        _orphanWhere = null;
        _apartWhere = null;
        _reconnectFrom = _state;
        _reconnectTry = 1;
        _reconnectLoggedIn = false;
        _reconnectLeftWorld = false; // M6 14 body: she is still in the world until the log-off takes her out; only an entry after that is a re-login
        _phase = RunPhase.Reconnecting; // the log-off takes the session out of the world: the reconnect's machine owns the frames from here
        Stage = "reconnecting";
        _orphanStage = OrphanStage.LoggingOff;
        _orphanLogOff = session.Actions.LogOff(ActionSource.Automation);
        return ScenarioStatus.Running;
    }

    /// <summary>The re-login's log-off and its wait, inside the reconnect's machine; then the reconnect's first try, which <see cref="ReconnectStep"/> steps on.</summary>
    private ScenarioStatus OrphanReloginStep(IGameSessionView session)
    {
        if (_orphanStage == OrphanStage.LoggingOff)
        {
            if (_orphanLogOff is { IsCompleted: false })
            {
                return ScenarioStatus.Running; // the log-off's own limit ends it (ActionPolicy.LogOffSeconds)
            }

            var ended = _orphanLogOff?.Outcome;
            _orphanLogOff = null;
            _orphanStage = OrphanStage.Waiting;
            _orphanWaitUntil = _clock + OrphanReloginWaitSeconds;
            var confirmed = ended?.Kind == ActionOutcomeKind.Confirmed;
            var line = string.Create(CultureInfo.InvariantCulture, $"autopilot: {_reloginName}: the log-off ended {ended?.Kind.ToString() ?? "unknown"}{(ended?.Detail is { } d ? " (" + d + ")" : string.Empty)} (session phase {session.State.Phase.ToString().ToLowerInvariant()}){(confirmed ? string.Empty : "; the reconnect drops the old link anyway")}; the re-login in {OrphanReloginWaitSeconds:0} s ({_reloginTodo})");
            if (confirmed)
            {
                _log.Info(LogCategory, line);
            }
            else
            {
                _log.Warn(LogCategory, line);
            }

            return ScenarioStatus.Running;
        }

        if (_clock < _orphanWaitUntil)
        {
            return ScenarioStatus.Running;
        }

        _orphanStage = OrphanStage.None;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot: {_reloginName}: the re-login: reconnect try {_reconnectTry} of {_options.ReconnectWaits.Count} after the log-off and {OrphanReloginWaitSeconds:0} s (session phase {session.State.Phase.ToString().ToLowerInvariant()}) ({_reloginTodo})"));
        try
        {
            _host!.Reconnect();
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException or IOException)
        {
            return TryFailed("Reconnect threw: " + e.Message);
        }

        _reconnectLoggedIn = true;
        _reconnectLoginStart = _clock;
        return ScenarioStatus.Running;
    }
}
