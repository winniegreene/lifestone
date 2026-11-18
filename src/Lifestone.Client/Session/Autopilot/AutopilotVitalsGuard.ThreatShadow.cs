using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Threat;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 16 E2b (docs/automation-engine.md E2 and section 3, "shadow first"; m6-plan.md D139): the vitals guard's part of the threat board's shadow. Report-only: the guard's count of the attackers on her
/// (<c>AttackersOnHer</c>: the living creatures of a name the outer loop's watch heard hit her within <see cref="HostileWatch.AttackQuietSeconds"/>, not named dead by her kill's line, within
/// <see cref="HostileReach.DefaultMetres"/> where the session draws them, and the travel fight-back's target whether heard or not) feeds the losing race exactly as before. The session's <see cref="ThreatBoard"/> is
/// asked beside it for the living creatures that may be attacked of a name that hit her within that window, within that reach, the killed set aside, counted; a difference is one line when it appears, changes or ends
/// (<c>threat-shadow: vitals-guard: ...</c>). The fight-back's target not yet heard, and a creature of the name that may not be attacked, are differences, and are named as such. The guard is the outer loop's: its asks
/// are in the cycle line's two fields (<c>AutopilotScenario.ThreatShadow.cs</c>).
/// </summary>
/// <remarks>
/// Not shadowed here, by design: the guard's incoming rate (<c>IncomingNow</c>: a rate from the watch's hits, no question of who is near) and its pick of the fullest creature to drain (<c>KnownFullest</c>: the
/// design's "one fullest-creature pick", a slice of its own).
/// </remarks>
internal sealed partial class AutopilotVitalsGuard
{
    /// <summary>The lane's word of the guard's attackers on her.</summary>
    internal const string AttackersDetector = "vitals-guard";

    private ThreatDetectorShadow? _shadowAttackers;
    private Func<uint, bool>? _shadowKilled;
    private bool _shadowTried;

    /// <summary>The guard counted the attackers on her: the board is asked the same and the pair compared. Allocates nothing while the two agree.</summary>
    private void ShadowAttackers(IGameSessionView session, double clock, int attackers)
    {
        if (!_shadowTried)
        {
            _shadowTried = true;
            try
            {
                _shadowKilled = _watch.IsKilled;
                _shadowAttackers = new ThreatDetectorShadow(session, AttackersDetector, text => _log.Info(AutopilotScenario.LogCategory, text), _log, AutopilotScenario.LogCategory, hearsSince: true);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                _shadowAttackers = null;
                _log.Error(AutopilotScenario.LogCategory, $"{ThreatShadowLane.Token}{AttackersDetector}: TODO(threat-shadow-failed): the shadow could not be made ({error.GetType().Name}: {error.Message}); the guard counts without it, as ever");
            }
        }

        if (_shadowAttackers is { } shadow)
        {
            var query = new ThreatQuery(HostileReach.DefaultMetres, HostileWatch.AttackQuietSeconds, ThreatHeard.Hits, ThreatStanding.Attackers) { Skip = _shadowKilled };
            shadow.Answered(clock, in query, attackers > 0, count: attackers);
        }
    }
}
