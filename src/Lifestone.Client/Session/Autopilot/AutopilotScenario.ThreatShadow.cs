using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 16 E2a (docs/automation-engine.md E2 and section 3, "shadow first"; m6-plan.md D139): the outer loop's part of the threat board's shadow. Report-only: nothing here is read by any decision, and no option turns it off.
/// <para>M6 16 E2b: the vitals guard's attackers on her (<see cref="AutopilotVitalsGuard"/>, its own file) stand beside the board, lane <c>vitals-guard</c>; its asks are in the cycle line's two fields.</para>
/// <para>M6 16 Q2 (docs/automation-engine.md E2, <c>TODO(engine-threat-flip)</c>): the watch's own question (<see cref="HostileWatch.InReach"/>) and the retreat line's attackers (<see cref="RetreatPresent"/>) are the board's now,
/// so their lanes (<c>outer-loop</c>, <c>retreat</c>) are gone.</para>
/// </summary>
/// <remarks>
/// The cycle line closes with the cycle's two counts, <c>threat_shadow_agree=</c> and <c>threat_shadow_differ=</c>: the asks of the lanes still standing in which the detector and the board agreed, and those in which they
/// differed. A lane is the session's, so its counts outlive this scenario (it is rebuilt at every rung move); the cycle's share is read against a mark.
/// </remarks>
public sealed partial class AutopilotScenario
{
    private ThreatShadowShare? _threatShare;
    private readonly List<ThreatStander> _fightStanders = []; // M6 16 Q2: the board's creatures of the retreat line's question, reused every read
    private bool _threatShadowTried;

    /// <summary>
    /// Stands the outer loop's shadows, once a scenario, and marks their lanes' counts for this scenario's first cycle. A shadow that cannot be made is named once and left out: the reads decide without it, as before.
    /// </summary>
    private void ShadowHostiles()
    {
        if (_threatShadowTried || _hostiles is null)
        {
            return;
        }

        _threatShadowTried = true;
        try
        {
            var share = new ThreatShadowShare();
            _threatShare = share;
            if (_session is { } session)
            {
                // M6 16 E2b: the guard's read, the outer loop's own: its lane is marked at this scenario's start
                share.Add(ThreatBoard.For(session).Lane(AutopilotVitalsGuard.AttackersDetector));
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _log.Error(LogCategory, $"{ThreatShadowLane.Token}{AutopilotVitalsGuard.AttackersDetector}: TODO(threat-shadow-failed): the shadow could not be made ({error.GetType().Name}: {error.Message}); the outer loop's reads decide without it, as ever");
        }
    }

    /// <summary>The cycle line's two fields, and the mark moved to the next cycle: <c>threat_shadow_agree=&lt;n&gt; threat_shadow_differ=&lt;n&gt;</c>.</summary>
    private string ThreatShadowFields()
    {
        if (_threatShare is not { } share)
        {
            return ThreatShadowLane.FieldsOf(0, 0);
        }

        var fields = share.Fields();
        share.Remark();
        return fields;
    }

    /// <summary>
    /// M6 16 Q2: the retreat line's attackers present into <c>_fightPresent</c>: the board's creatures for its own question (the creatures of a name that hit her within <see cref="HostileWatch.AttackQuietSeconds"/>, alive and
    /// attackable within <see cref="Scenarios.HuntScenario.DefendReachMetres"/> of her server position), in the object table's order, as the old read through the outer loop's watch gave them. The one change of answer: a hit the
    /// board heard before the watch was built is known for its quiet. Allocation-free once the lists have their capacity.
    /// </summary>
    private void RetreatPresent(IGameSessionView session)
    {
        var query = new ThreatQuery(Scenarios.HuntScenario.DefendReachMetres, HostileWatch.AttackQuietSeconds, ThreatHeard.Hits, ThreatStanding.Attackers);
        ThreatBoard.For(session).Gather(session, in query, _clock, _fightStanders);
        _fightPresent.Clear();
        for (var i = 0; i < _fightStanders.Count; i++)
        {
            var p = _fightStanders[i];
            _fightPresent.Add(new PresentAttacker(p.Guid, p.Wcid, p.Name, p.Metres));
        }
    }
}
