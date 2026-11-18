using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.World;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 7 AJ (<c>m6-body-apart</c>; docs/handoff-m6-7-aj.md): the body and the server part ways. Gate A's climb, 2026-11-06: a hop to the Refuge landed in a landblock five minutes idle; ACE's unload took the player off it (the race
/// <c>ToolTracker</c>'s remarks name for a login: an empty block unloads <c>UnloadInterval</c> after its last <c>SetActive</c>, and only the player's own heartbeat sets it, @95ef426d Entity/Landblock.cs:532-549, 1130-1147,
/// WorldObjects/Player_Tick.cs:112-114). A player on no landblock gets no physics tick and no heartbeat: every position the client sends is taken and never answered, vitals and gear mana stop, the five-minute save stops. The client's
/// session keeps the last position, the constraint holds the body to it at 50 m (20 m in a room), and the journey read that as a wall, then waited on a settle gate that could never open until <c>stop=watchdog:travel</c>. The constraint adds up
/// the distance moved in any direction, so at its end no key moves the body, a person's included (the replay: IslandDungeonWalkDatTests, the Lodge ramp). Stretch 2's stick on that ramp had the mod's <c>not-in-world</c> live. Nothing the client
/// sends puts the player back; only a log-off and a new login does.
/// <list type="number">
/// <item><b>Check</b> (<see cref="BodyApartWatch"/>, one read a second in the states the watchdogs cover, alive): past the named distance with no new position from the server for its hold: one line, then one <c>where</c>.</item>
/// <item><b>The mod says <c>not-in-world</c></b>: the one re-login at once (<see cref="Orphaned"/>'s machine: the station cut as a drop cuts it, one log-off, the wait, the reconnect's tries, the probe after).</item>
/// <item><b>The server has the player in the world, the mod does not answer, or the tools are not ready</b>: named, and the run goes on; <see cref="BodyApartChecks"/> findings in a row with no closing of the gap between them make the same
/// re-login, since a position that never moves leaves the run dead whatever the reason.</item>
/// <item><b>Check after</b>: the known state's probe, then one line when the body and the server first agree.</item>
/// <item><b>Gives up</b>: a gap found within <see cref="BodyApartForgetSeconds"/> of a re-login stops the run <see cref="BodyApartStop"/> (or <see cref="OrphanedLoginStop"/> on the orphan's own rule), with no recall before it; so does a
/// gap with no way to log in again.</item>
/// </list>
/// A body that stands still shows no gap: an orphan who only casts is read at her next walk (TODO(m6-orphan-standing)).
/// </summary>
public sealed partial class AutopilotScenario
{
    /// <summary>The stop when the gap comes back after the re-login, or there is no way to log in again.</summary>
    public const string BodyApartStop = "failed:body-server-apart";

    /// <summary>The TODO name the lines carry.</summary>
    public const string BodyApartTodo = "m6-body-apart";

    /// <summary>Findings in a row before a re-login when the server does not say the player is out of the world (the first may be a passing gap; a <c>where</c> is a read, a re-login is not).</summary>
    public const int BodyApartChecks = 2;

    /// <summary>Seconds after a re-login in which a new gap means the re-login did not cure it (Lifestone's own: the landblock's five minutes, so a second unload race counts as a new event only after a block could unload again).</summary>
    public const double BodyApartForgetSeconds = 300;

    private const double BodyApartReadSeconds = 1;

    private readonly BodyApartWatch _bodyApart = new();
    private ToolHandle<WhereResult>? _apartWhere;
    private int _apartChecks;
    private double _apartNextRead;
    private double _apartReloginAt = double.NegativeInfinity;
    private bool _apartAwaitsCheckAfter;

    /// <summary>The re-logins this rule made this run (the seam tests read it).</summary>
    public int BodyApartRelogins { get; private set; }

    /// <summary>The frame's check: null to go on (nothing found, or a <c>where</c> out while the station runs on), else the stop or the re-login's status.</summary>
    private ScenarioStatus? BodyApartStep(IGameSessionView session)
    {
        if (_apartWhere is { } where)
        {
            return where.Outcome is { } outcome ? BodyApartAnswered(session, where, outcome) : null;
        }

        if (_deathSeen || _phase is not (RunPhase.Known or RunPhase.Vitals or RunPhase.Peace or RunPhase.Running))
        {
            _bodyApart.Reset();
            return null;
        }

        if (_clock < _apartNextRead)
        {
            return null;
        }

        _apartNextRead = _clock + BodyApartReadSeconds;
        var self = session.State.Self;
        var present = self.Travel == SelfTravel.Present;
        var body = present ? (_context.Runners as IJourneyWalks)?.BodyPosition(_context) : null;
        var found = _bodyApart.Step(present ? self.Position : null, body, _clock);
        if (!_bodyApart.Apart && !double.IsNaN(_bodyApart.LastMetres))
        {
            if (_apartChecks > 0)
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot: body apart: the gap closed ({_bodyApart.LastMetres:0.0} m): the server is answering; no re-login ({BodyApartTodo})"));
                _apartChecks = 0;
            }

            if (_apartAwaitsCheckAfter)
            {
                _apartAwaitsCheckAfter = false;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot: body apart: check after the re-login: the body and the server agree ({_bodyApart.LastMetres:0.0} m) ({BodyApartTodo})"));
            }
        }

        return found is { } apart ? BodyApartFound(session, apart) : null;
    }

    private ScenarioStatus? BodyApartFound(IGameSessionView session, in BodyApart apart)
    {
        _apartChecks++;
        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot: body apart: the body stands {apart.Metres:0.0} m from where the server has the player (limit {apart.Limit:0} m) with no new position from the server for {apart.HeldSeconds:0} s; body {Spot(apart.Body)}, server {Spot(apart.Server)}; finding {_apartChecks} of {BodyApartChecks}, the {StateName(_state)} state, cycle {_cycle} ({BodyApartTodo})"));
        if (_clock - _apartReloginAt < BodyApartForgetSeconds)
        {
            return BodyApartGiveUp(string.Create(CultureInfo.InvariantCulture, $"the body and the server are {apart.Metres:0.0} m apart again {_clock - _apartReloginAt:0} s after the re-login, within the {BodyApartForgetSeconds:0} s that count as the same trouble ({BodyApartTodo})"));
        }

        var tools = session.Tools.Availability;
        if (tools != ToolAvailability.Ready)
        {
            return BodyApartCounted(session, $"the player tools are {tools.ToString().ToLowerInvariant()}, so the server cannot be asked");
        }

        _apartWhere = session.Tools.WhereAmI(ActionSource.Automation);
        _log.Info(LogCategory, $"autopilot: body apart: check: one `where` (a read: no mod verb is counted); the station runs on while it is out ({BodyApartTodo})");
        return null;
    }

    private ScenarioStatus? BodyApartAnswered(IGameSessionView session, ToolHandle<WhereResult> where, ActionOutcome outcome)
    {
        _apartWhere = null;
        if (outcome.Kind == ActionOutcomeKind.Confirmed && where.Result is { } found)
        {
            return BodyApartCounted(session, string.Create(CultureInfo.InvariantCulture, $"the server has the player in the world at {Spot(found.Position)}{(found.Teleporting ? ", teleporting" : string.Empty)}"));
        }

        if (outcome.Kind == ActionOutcomeKind.ServerError && outcome.Detail?.StartsWith(ToolErrors.NotInWorld, StringComparison.Ordinal) == true)
        {
            _log.Warn(LogCategory, $"autopilot: body apart: the mod says {ToolErrors.NotInWorld}: the server has the player on no landblock, so nothing the client sends is taken; the re-login, the station cut ({BodyApartTodo})");
            FoldInFlight(); // as a drop: a fight's kills count, a cut trip or death is kept, no key stays held
            if (_host is not null)
            {
                NoteBodyApartRelogin();
            }

            _orphanAfter = "body apart";
            return Orphaned(session, outcome.Detail);
        }

        return BodyApartCounted(session, $"the `where` was not confirmed ({outcome.Kind}: {outcome.Detail ?? "no detail"})");
    }

    /// <summary>A finding with no <c>not-in-world</c>: named; the run goes on until the second in a row, then the one re-login.</summary>
    private ScenarioStatus? BodyApartCounted(IGameSessionView session, string read)
    {
        if (_apartChecks < BodyApartChecks)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot: body apart: {read}; the run goes on and the watch reads the gap for {BodyApartWatch.HoldSeconds:0} s more ({BodyApartTodo})"));
            return null;
        }

        if (_host is null)
        {
            return BodyApartGiveUp($"{read}; the gap stayed and the host gave the autopilot no way to log in again (IAutopilotSession) ({BodyApartTodo})");
        }

        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot: body apart: {read}; the gap stayed through {_apartChecks} findings: one log-off, a wait of {OrphanReloginWaitSeconds:0} s, then one re-login puts the body where the server has it, cycle {_cycle} ({BodyApartTodo})"));
        FoldInFlight();
        NoteBodyApartRelogin();
        (_reloginName, _reloginTodo) = ("body apart", BodyApartTodo);
        return StartRelogin(session);
    }

    private void NoteBodyApartRelogin()
    {
        BodyApartRelogins++;
        _apartReloginAt = _clock;
        _apartChecks = 0;
        _apartAwaitsCheckAfter = true;
        _bodyApart.Reset();
    }

    private ScenarioStatus BodyApartGiveUp(string why)
    {
        _log.Warn(LogCategory, "autopilot: body apart: gave up: " + why);
        FoldInFlight();
        _finalDone = true; // no recall before this stop: the server is not taking the player's moves or casts
        return Stop(BodyApartStop, ok: false, why);
    }

    private static string Spot(in CellFrame at) => string.Create(CultureInfo.InvariantCulture, $"0x{at.CellId:X8} ({at.Origin.X:0.0}, {at.Origin.Y:0.0}, {at.Origin.Z:0.0})");
}
