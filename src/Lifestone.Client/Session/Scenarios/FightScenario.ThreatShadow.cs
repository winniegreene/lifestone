using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Session.Threat;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 16 E2b (docs/automation-engine.md E2 and section 3, "shadow first"; m6-plan.md D139): the fight's part of the threat board's shadow. Report-only: the fight's three detectors of "who is near, who attacks her" decide
/// exactly as before; the session's <see cref="ThreatBoard"/> is asked each one's question beside it and a difference is one line when it appears, changes or ends (<see cref="ThreatDetectorShadow"/>).
/// <list type="bullet">
/// <item><c>fight-defence</c>, the self-defence list (<c>FightScenario.SelfDefence.cs</c>: the creatures a notice or a hostile spell line named in the fight stage, each kept by its guid until it is dead, gone or quiet for
/// <see cref="FightOptions.DefendForgetSeconds"/>; no reach). Asked once a step in the fight stage: "is one attacking her" against every attack heard within that window from a living creature at any distance, the killed
/// set aside.</item>
/// <item><c>fight-refresh</c>, the buff refresh's hostile read (<see cref="RefreshHostile"/>: a damage notice within <see cref="RefreshQuietSeconds"/> of the decision that saw it, or a living target within the refresh's
/// reach) against a hit within that window or a target within that reach.</item>
/// <item><c>fight-pull-rest</c>, the pull rest's read (<see cref="PullRestHostile"/>: the hurt target in range, or a creature of the ground within the rule's aggro reach with a clear line) against a creature of the ground
/// within that reach. The line test and the hurt target are no parameters of a board query: a creature a wall hides is a difference, and is named as one.</item>
/// </list>
/// At the fight's end (its disposal) one line carries the fight's asks: <c>threat-shadow: fight: its asks: threat_shadow_agree=&lt;n&gt; threat_shadow_differ=&lt;n&gt; (...)</c>. The fight's verdict line keeps its pinned
/// shape, so the counts have this line of their own. Each wrapper below is the detector's old name; the detector itself is the method beside it whose name ends in <c>Own</c>, unchanged.
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>The lane's word of the self-defence list.</summary>
    public const string DefenceDetector = "fight-defence";

    /// <summary>The lane's word of the buff refresh's hostile read.</summary>
    public const string RefreshDetector = "fight-refresh";

    /// <summary>The lane's word of the pull rest's read.</summary>
    public const string PullRestDetector = "fight-pull-rest";

    private ThreatDetectorShadow? _shadowDefence;
    private ThreatDetectorShadow? _shadowRefresh;
    private ThreatDetectorShadow? _shadowPullRest;
    private ThreatShadowShare? _shadowShare;
    private Func<uint, bool>? _shadowKilled;
    private uint[] _shadowGroundWcids = [];
    private bool _shadowTried;

    /// <summary>Makes the fight's three shadows, once. Shadows that cannot be made are named once and left out: the detectors decide without them, as before.</summary>
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
            _shadowKilled = guid => _killed.Contains(guid);
            var ground = new List<uint>(_targetWcids); // the pull rest's creatures (IsFightCreature): the targets and, on an outdoor ground, every creature its generators spawn
            if (_ground.Outdoor is { } outdoor)
            {
                foreach (var creature in outdoor.Creatures)
                {
                    if (!ground.Contains(creature.Wcid))
                    {
                        ground.Add(creature.Wcid);
                    }
                }
            }

            _shadowGroundWcids = [.. ground];
            _shadowDefence = new ThreatDetectorShadow(session, DefenceDetector, line, _log, LogCategory, hearsSince: true);
            _shadowRefresh = new ThreatDetectorShadow(session, RefreshDetector, line, _log, LogCategory, hearsSince: true);
            _shadowPullRest = new ThreatDetectorShadow(session, PullRestDetector, line, _log, LogCategory);
            var share = new ThreatShadowShare();
            share.Add(_shadowDefence.Lane);
            share.Add(_shadowRefresh.Lane);
            share.Add(_shadowPullRest.Lane);
            _shadowShare = share;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _log.Error(LogCategory, $"{ThreatShadowLane.Token}fight: TODO(threat-shadow-failed): the fight's shadows could not be made ({error.GetType().Name}: {error.Message}); its detectors decide without them, as ever");
        }

        return _shadowShare is not null;
    }

    /// <summary>
    /// Each step: the shadows are made at the first (and with them the session's board when no one made it before, so the board hears what the fight's own list hears); in the fight stage, the self-defence list beside
    /// the board (report-only). Allocates nothing while the two agree.
    /// </summary>
    private void ThreatShadowStep(IGameSessionView session, double clock)
    {
        if (!ShadowsReady(session) || Stage != "fight" || _options.DefendForgetSeconds <= 0)
        {
            return;
        }

        var query = new ThreatQuery(double.PositiveInfinity, _options.DefendForgetSeconds, ThreatHeard.Attacks, ThreatStanding.Attackers) { Skip = _shadowKilled };
        _shadowDefence!.Answered(clock, in query, AttackerNow(session, clock) is not null); // the list keeps one creature a notice, by its guid; the board every living creature of a name heard: only "is one attacking her" is compared
    }

    /// <summary>True when a hostile is in reach for the buff refresh (<see cref="RefreshHostileOwn"/> decides, as ever); the board is asked the same beside it (report-only).</summary>
    private bool RefreshHostile(IGameSessionView session, double clock, out string what)
    {
        var hostile = RefreshHostileOwn(session, clock, out what);
        if (ShadowsReady(session))
        {
            var query = ThreatQuery.InReach(_refreshReach, RefreshQuietSeconds, _targetWcids) with { Skip = _shadowKilled };
            _shadowRefresh!.Answered(clock, in query, hostile, said: hostile ? what : null);
        }

        return hostile;
    }

    /// <summary>True when a rest may not start or go on (<see cref="PullRestHostileOwn"/> decides, as ever); the board is asked for a creature of the ground within the rule's reach beside it (report-only).</summary>
    private bool PullRestHostile(IGameSessionView session, in CellFrame here, GroundPullRest rule, out string what, out bool hurtTarget)
    {
        var hostile = PullRestHostileOwn(session, in here, rule, out what, out hurtTarget);
        if (ShadowsReady(session))
        {
            var query = new ThreatQuery(rule.AggroMetres, 0, ThreatHeard.Hits, ThreatStanding.Targets) { Targets = _shadowGroundWcids, Here = here, Skip = _shadowKilled };
            _shadowPullRest!.Answered(session.Clock.Now, in query, hostile, said: hostile ? what : null);
        }

        return hostile;
    }

    /// <summary>The fight ends: its line with the asks of its three detectors, when it asked at all.</summary>
    private void EndThreatShadow()
    {
        if (_shadowShare?.Line("fight") is { } line)
        {
            _log.Info(LogCategory, line);
        }

        EndHealthShadow(); // M6 16 E2d: the fight's line of its health readers' asks, report-only
        EndSurvivalShadow(); // M6 16 E3.0: the fight's line of its asks of the survival layer, report-only
    }
}
