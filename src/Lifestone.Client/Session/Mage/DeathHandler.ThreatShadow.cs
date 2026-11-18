using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 16 E2b (docs/automation-engine.md E2 and section 3, "shadow first"; m6-plan.md D139): the death handler's part of the threat board's shadow. Report-only: what stops a corpse trip is decided exactly as before.
/// <list type="bullet">
/// <item><c>death-path</c>, the path read (<see cref="HostileNearPath"/>: a living target of the ground within <see cref="DeathOptions.TripAggroMetres"/> of the path ahead toward the corpse) against a living target
/// of the ground within that reach of her. The path is no parameter of a board query: a creature near the path ahead and farther than the reach from her is a difference, and is named as one; the creature the read
/// names is the nearest to the path, the board's the nearest to her, so the two are compared on whether one answers.</item>
/// </list>
/// M6 16 Q2 (docs/automation-engine.md E2, <c>TODO(engine-threat-flip)</c>): the corpse leg's read on a travel leg (<c>LegRunPast</c>: the creatures of a name heard attacking her within <see cref="HuntScenario.DefendHeardSeconds"/>,
/// alive within <see cref="HostileReach.DefaultMetres"/> of her server position) asks the board itself (<see cref="LegPresent"/>), so its lane (<c>death-corpse-leg</c>) is gone. The one change of answer: an attack the board
/// heard before the leg's watch began is known for its window. The leg's watch keeps its own hearing for the run past's reads.
/// When the handler is disposed, and it asked at all, one line with its counts (<c>threat-shadow: death: its asks: threat_shadow_agree=&lt;n&gt; threat_shadow_differ=&lt;n&gt; (...)</c>).
/// </summary>
public sealed partial class DeathHandler
{
    /// <summary>The lane's word of the path read.</summary>
    public const string PathDetector = "death-path";

    private ThreatDetectorShadow? _shadowPath;
    private readonly List<ThreatStander> _legStanders = []; // M6 16 Q2: the board's creatures of the corpse leg's question, reused every frame
    private ThreatShadowShare? _shadowShare;
    private IReadOnlyList<uint> _shadowTargets = [];
    private bool _shadowTried;

    private bool ShadowsReady(IGameSessionView session)
    {
        if (_shadowTried)
        {
            return _shadowShare is not null;
        }

        _shadowTried = true;
        try
        {
            Action<string> line = text => _log.Info(LogCategory, text);
            _shadowTargets = FightTargets.Wcids(_ground);
            _shadowPath = new ThreatDetectorShadow(session, PathDetector, line, _log, LogCategory);
            var share = new ThreatShadowShare();
            share.Add(_shadowPath.Lane);
            _shadowShare = share;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _log.Error(LogCategory, $"{ThreatShadowLane.Token}death: TODO(threat-shadow-failed): the handler's shadows could not be made ({error.GetType().Name}: {error.Message}); its reads decide without them, as ever");
        }

        return _shadowShare is not null;
    }

    /// <summary>True when a living target of the ground stands near the path ahead (<see cref="HostileNearPathOwn"/> decides, as ever); the board is asked for one within the same reach of her beside it (report-only).</summary>
    private bool HostileNearPath(IGameSessionView session, out uint hostile, out double reach)
    {
        var near = HostileNearPathOwn(session, out hostile, out reach);
        if (ShadowsReady(session))
        {
            var query = new ThreatQuery(_options.TripAggroMetres, 0, ThreatHeard.Hits, ThreatStanding.Targets) { Targets = _shadowTargets };
            _shadowPath!.Answered(session.Clock.Now, in query, near, hostile, near ? reach : double.NaN, sameCreature: false);
        }

        return near;
    }

    /// <summary>
    /// The corpse leg's attackers present into <c>_legPresent</c> (M6 16 Q2): the board's creatures for the leg's own question (<see cref="ThreatQuery.OnHer"/>: <see cref="HostileReach.DefaultMetres"/>,
    /// <see cref="HuntScenario.DefendHeardSeconds"/>, every attack heard, from her server position), in the object table's order, as the old read through the leg's watch gave them. Allocation-free once the lists have their capacity.
    /// </summary>
    private void LegPresent(IGameSessionView session)
    {
        var query = ThreatQuery.OnHer(HostileReach.DefaultMetres, HuntScenario.DefendHeardSeconds);
        ThreatBoard.For(session).Gather(session, in query, session.Clock.Now, _legStanders);
        _legPresent.Clear();
        for (var i = 0; i < _legStanders.Count; i++)
        {
            var p = _legStanders[i];
            _legPresent.Add(new PresentAttacker(p.Guid, p.Wcid, p.Name, p.Metres));
        }
    }

    /// <summary>The handler ends: its line with the asks of its path read, when it asked at all.</summary>
    private void EndThreatShadow()
    {
        if (_shadowShare?.Line("death") is { } line)
        {
            _log.Info(LogCategory, line);
        }
    }
}
