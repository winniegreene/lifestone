using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.World;
using Lifestone.Data.World;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 8 N (D120, the owner's ruling: yes to the last-resort recall): <c>/lifestone</c> (<see cref="IActionApi.RecallLifestone"/>) as the survival layer's last leave option, never something quest code calls. It is armed by a failed ladder errand
/// (<see cref="LastResortArm"/>, from <see cref="LadderErrandFailed"/>) and taken only if the recovery's way home then fails (<see cref="LastResortOnTravelFailed"/>): the rung change's named failure or the Lifestone Recall station's. Other failures keep their behaviour.
/// At most once per failed errand. The owner's widening after long round 3 (<see cref="LastResortInsteadOfStop"/>): on the ladder it also answers any stop that says no way home worked, once the run's own retries are spent. Check, act, check: <see cref="LastResortRule.Check"/> before (alive, in the world, placed, not busy, a sanctuary known or the refusal line as the check), the recall sent with nothing else moving her (the phase steps no station, no walk, no
/// fight-back), and after: the tracker's teleport, then the body placed away from where it started (<see cref="LastResortRule.Arrived"/>). A refusal or no arrival stops by name, <c>failed:leave:lifestone-recall:&lt;reason&gt;</c>, with no retry.
/// </summary>
public sealed partial class AutopilotScenario
{
    private string? _lastResortKey; // the failed ladder errand whose way home is still to fail (armed), or null
    private bool _lastResortForLeave; // M6 8 long round 3: the arm came from the survival layer's leave out of the errand, whose way home is a retreat's
    private uint? _lastResortLanded; // the landblock the last one landed on: one from there would only land there again
    private readonly HashSet<string> _lastResortTaken = [];
    private ActionHandle? _lastResortHandle;
    private CellFrame? _lastResortFrom;
    private string _lastResortWalk = string.Empty;
    private string _lastResortFor = string.Empty;
    private double _lastResortAt;
    private double _lastResortConfirmedAt = double.NaN;
    private uint _lastResortMana;
    private int _lifestoneRecallsCycle;
    private int _lifestoneRecallsRun;

    /// <summary>M6 8 N: the Lifestone Recalls the last resort took and that arrived this run (the <c>[ladder]</c> line's <c>lifestone_recalls=</c>).</summary>
    public int LifestoneRecalls => _lifestoneRecallsRun;

    /// <summary>A ladder errand failed: its recovery's way home may end in the last resort, once. A key that already used it is not armed again.</summary>
    private void LastResortArm(string key)
    {
        _lastResortKey = _lastResortTaken.Contains(key) ? null : key;
        if (_lastResortKey is null)
        {
            _log.Info(LogCategory, $"survival: leave: lifestone-recall: not armed for the errand {key}: it was taken once already this run");
        }
    }

    /// <summary>
    /// A Travel station failed. Null: not the last resort's case (no failed errand armed, not a way home, or a retreat's way home), the failure takes its usual road. Otherwise the last resort begins, or the run stops by name.
    /// </summary>
    private ScenarioStatus? LastResortOnTravelFailed(string? why)
    {
        if (_lastResortKey is not { } key || (_retreating && !_lastResortForLeave) || _finalStop is not null || !(ReferenceEquals(_spec, RungHomeSpec) || ReferenceEquals(_spec, RecallHomeSpec)) || _session is not { } session)
        {
            return null;
        }

        _lastResortKey = null;
        _lastResortForLeave = false;
        _lastResortTaken.Add(key);
        _lastResortWalk = _spec!.Name;
        _lastResortFor = "the ladder errand " + key;
        _log.Warn(LogCategory, $"survival: {StateName(_state)}: leave: lifestone-recall: the walk {_lastResortWalk} failed after the ladder errand {key} ({(why is null ? "no failure text" : JourneyVerdict.Token(why))}): no way home works, so /lifestone is the last resort (once for this errand)");
        return BeginLastResort(session);
    }

    /// <summary>
    /// The owner's widening of D120, after long round 3: on the ladder, a stop that says no way home worked (<see cref="LastResortRule.CoversStop"/>) falls to <c>/lifestone</c> and the climb goes on; the run's own retries have already been spent. Null: not this rule's case, the stop stands.
    /// She must be in the world, placed and alive (else the stop's own name says more than a refusal would). A second one from the landblock the last one landed on would only land there again, so the stop stands.
    /// Each use names a route that failed, so it is logged loud: TODO(m6-lifestone-covered-a-failure).
    /// </summary>
    private ScenarioStatus? LastResortInsteadOfStop(string stop, string why)
    {
        if (!SurvivalOn() || _finalDone || !LastResortRule.CoversStop(stop) || _deathSeen || _deathPending || _session is not { } session || session.State.Phase != SessionPhase.InWorld || session.State.Self.Travel != SelfTravel.Present || session.State.Self.Position is not { } at)
        {
            return null;
        }

        if (at.CellId >> 16 == _lastResortLanded)
        {
            _log.Error(LogCategory, $"survival: {StateName(_state)}: leave: lifestone-recall: not taken: she stands in landblock 0x{at.CellId >> 16:X4}, where the last one landed, and a second would land her there again; the stop stands: {stop}");
            return null;
        }

        _lastResortWalk = stop;
        _lastResortFor = "no ladder errand";
        _log.Warn(LogCategory, $"survival: {StateName(_state)}: leave: lifestone-recall: the run would stop by {stop} ({JourneyVerdict.Token(why)}): no way home works, so /lifestone is the last resort and the climb goes on; TODO(m6-lifestone-covered-a-failure): the failure it covers is a bug to fix");
        return BeginLastResort(session);
    }

    /// <summary>
    /// M6 8 (live 2026-11-11 10:31 UTC: the walk out of the Burrow with no usable recall, a death at health 4): the two cases a walk she dies on never reaches, because it never fails by name. Before the walk
    /// home begins from a dungeon's kept-out hall (<see cref="LastResortRule.NoSafeWalkOut"/>), and on any way home when the guard finds her under its line with nothing payable.
    /// Null: not this rule's case (off the ladder, dead, not placed, or standing where the last one landed).
    /// </summary>
    private ScenarioStatus? LastResortNoWayHome(IGameSessionView session, string walk, string why)
    {
        if (!SurvivalOn() || _finalDone || _deathSeen || _deathPending || _phase == RunPhase.LastResort || session.State.Phase != SessionPhase.InWorld || session.State.Self.Travel != SelfTravel.Present || session.State.Self.Position is not { } at
            || at.CellId >> 16 == _lastResortLanded)
        {
            return null;
        }

        _lastResortWalk = walk;
        _lastResortFor = "no ladder errand";
        _log.Warn(LogCategory, $"survival: {StateName(_state)}: leave: lifestone-recall: check before: {why}: no way home works, so /lifestone is the last resort (m6-no-safe-way-home)");
        return BeginLastResort(session);
    }

    /// <summary>True when the walk home from where she stands is no way home (<see cref="LastResortRule.NoSafeWalkOut"/>): the body's own ground is read, not the run's (a rung move leaves her on the last one).</summary>
    private bool WalkOutIsDeadly(IGameSessionView session, out string why)
    {
        why = string.Empty;
        if (session.State.Self.Position is not { } at)
        {
            return false;
        }

        var body = _ground.InLandblocks(at.CellId) ? _ground : RungRoute.BodyGroundOf(at.CellId, _ground, _context.Grounds);
        var level = session.State.Self.Qualities.Level;
        if (body?.Hunt is not { } hunt || !LastResortRule.NoSafeWalkOut(body.Kind == Grounds.GroundKind.Dungeon, level, hunt.KeepOut.Count, hunt.UnderLevel, wayIsTheWalk: true))
        {
            return false;
        }

        why = string.Create(CultureInfo.InvariantCulture, $"the way home is the walk (no usable Lifestone Recall) out of {body.Key}, whose hunt keeps {hunt.KeepOut.Count} cell(s) out under level {hunt.UnderLevel}, and she is level {level}");
        return true;
    }

    private ScenarioStatus BeginLastResort(IGameSessionView session)
    {
        FoldInFlight();
        DisposeRun();
        _fix = null;
        _retry = null;
        (_lastFailure, _failureRepeats) = (null, 0); // the failure is answered: a later one, even from here, gets its own retries
        _phase = RunPhase.LastResort;
        _phaseStart = _clock;
        _lastResortHandle = null;
        _lastResortFrom = session.State.Self.Position;
        _lastResortAt = _clock;
        _lastResortConfirmedAt = double.NaN;
        return ScenarioStatus.Running;
    }

    private bool? SanctuaryKnown() => _context.Facts.Bound ? true : null;

    private ScenarioStatus LastResortStep(IGameSessionView session)
    {
        if (_deathSeen)
        {
            _lastResortHandle = null;
            _log.Warn(LogCategory, $"survival: {StateName(_state)}: leave: lifestone-recall: cut by a death: the death comes first");
            return UnscriptedDeath();
        }

        if (_lastResortHandle is null)
        {
            return LastResortSend(session);
        }

        if (_lastResortHandle.Outcome is not { } outcome)
        {
            return ScenarioStatus.Running; // the tracker's own bound ends it (ACE's delay plus its margin): nothing moves her meanwhile
        }

        if (outcome.Kind != ActionOutcomeKind.Confirmed)
        {
            return LastResortStop(LastResortReason(outcome), $"the recall ended {outcome.Kind}: {outcome.Detail ?? "no detail"}");
        }

        if (double.IsNaN(_lastResortConfirmedAt))
        {
            _lastResortConfirmedAt = _clock;
        }

        var self = session.State.Self;
        if (self.Position is { } now && _lastResortFrom is { } from)
        {
            var metres = from.ToGlobal().DistanceTo(now.ToGlobal());
            if (LastResortRule.Arrived(self.Travel == SelfTravel.Present, now.CellId == from.CellId, metres))
            {
                _lifestoneRecallsCycle++;
                _lifestoneRecallsRun++;
                _lastResortLanded = now.CellId >> 16;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"survival: {StateName(_state)}: leave: lifestone-recall: check after: arrived ({Cell(session)}), {metres:0} m from where the walk {_lastResortWalk} failed, after {_lastResortFor}; lifestone_recalls={_lifestoneRecallsRun}; the known state decides next"));
                _lastResortHandle = null;
                return EnterKnown($"recalled to the lifestone by the last resort after the failed walk {_lastResortWalk} ({_lastResortFor})");
            }
        }

        if (_clock - _lastResortConfirmedAt > LastResortRule.SettleSeconds)
        {
            return LastResortStop("not-arrived", $"the teleport was announced but she is not placed away from where she started within {LastResortRule.SettleSeconds:0} s (travel {self.Travel}, {Cell(session)})");
        }

        return ScenarioStatus.Running;
    }

    private ScenarioStatus LastResortSend(IGameSessionView session)
    {
        var self = session.State.Self;
        var q = self.Qualities;
        var busy = session.Actions.BusyReason; // the actions' own busy rules (a lane in flight or held), as RecallHome's busy wait asks
        var alive = q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _) && health > 0;
        var input = new LastResortInput
        {
            Alive = alive,
            InWorld = session.State.Phase == SessionPhase.InWorld,
            Placed = self.Travel == SelfTravel.Present && self.Position is not null,
            Busy = busy is not null,
            SanctuaryKnown = SanctuaryKnown(),
        };
        var decided = LastResortRule.Check(in input);
        if (decided.Verdict == LastResortVerdict.Wait)
        {
            if (_clock - _lastResortAt <= LastResortRule.BusySeconds)
            {
                return ScenarioStatus.Running;
            }

            return LastResortStop(decided.Reason!, $"{decided.Why} ({busy}): still so after {LastResortRule.BusySeconds:0} s");
        }

        if (decided.Verdict == LastResortVerdict.Refuse)
        {
            return LastResortStop(decided.Reason!, decided.Why);
        }

        _lastResortFrom = self.Position;
        _lastResortMana = q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _) ? mana : 0;
        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"survival: {StateName(_state)}: leave: lifestone-recall: check before: {decided.Why}; alive, in the world, placed ({Cell(session)}); act: /lifestone sent, she stands still until it completes; ACE halves her mana up front ({_lastResortMana} now, about {_lastResortMana / 2} after, Player_Location.cs:159); walk failed: {_lastResortWalk}, after {_lastResortFor}"));
        _lastResortHandle = session.Actions.RecallLifestone(ActionSource.Automation);
        return ScenarioStatus.Running;
    }

    /// <summary>The refusal's reason token from the tracker's outcome: ACE's WeenieError name or the sanctuary line, else the outcome's kind.</summary>
    internal static string LastResortReason(ActionOutcome outcome)
    {
        var detail = outcome.Detail ?? string.Empty;
        if (outcome.Kind == ActionOutcomeKind.TimedOut)
        {
            return "timed-out";
        }

        if (detail.Contains("sanctuary", StringComparison.Ordinal))
        {
            return "no-sanctuary";
        }

        const string Marker = "WeenieError ";
        var at = detail.IndexOf(Marker, StringComparison.Ordinal);
        if (at >= 0)
        {
            var name = detail[(at + Marker.Length)..].Split(' ', 2)[0];
            return JourneyVerdict.Token(name);
        }

        return JourneyVerdict.Token(outcome.Kind.ToString());
    }

    private ScenarioStatus LastResortStop(string reason, string why)
    {
        _lastResortHandle = null;
        var stop = LastResortRule.Stop(reason);
        _log.Error(LogCategory, $"survival: {StateName(_state)}: leave: lifestone-recall: check failed: {why}: the run stops by name {stop} (no retry; walk failed: {_lastResortWalk}, after {_lastResortFor})");
        return Stop(stop, ok: false, $"the last resort /lifestone after the failed walk {_lastResortWalk} ({_lastResortFor}): {why}");
    }
}
