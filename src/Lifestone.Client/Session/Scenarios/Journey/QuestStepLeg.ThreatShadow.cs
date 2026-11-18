using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Threat;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 16 E2b (docs/automation-engine.md E2 and section 3, "shadow first"; m6-plan.md D139): the quest steps' part of the threat board's shadow. Report-only: the steps' own list of attackers (the names a defence notice or
/// a hostile spell line gave since the leg began watching, each forgotten after <see cref="ForgetSeconds"/>; a bite's kept to <see cref="CastRange"/>, a spell line's to <see cref="HostileReach.DefaultMetres"/>; the
/// creatures the shot rule dropped and the ones she runs past set aside) decides exactly as before (<see cref="LiveAttackerOwn"/>). The session's <see cref="ThreatBoard"/> is asked beside it for the living creatures
/// of a name heard attacking within <see cref="ForgetSeconds"/> that stand within <see cref="HostileReach.DefaultMetres"/>, the ones she runs past set aside, and a difference is one line when it appears, changes or
/// ends (<c>threat-shadow: quest-steps: ...</c>). The list's two reaches and the shot rule's drops are no parameters of a board query: a bite's attacker between the two reaches, or a dropped one, is a difference, and
/// is named as one. At the leg's end, when it asked at all, one line with its counts (<c>threat-shadow: quest-leg: its asks: threat_shadow_agree=&lt;n&gt; threat_shadow_differ=&lt;n&gt; (...)</c>).
/// </summary>
public abstract partial class QuestStepLeg
{
    /// <summary>The lane's word of the quest steps' list.</summary>
    public const string StepsDetector = "quest-steps";

    private ThreatDetectorShadow? _shadowSteps;
    private ThreatShadowShare? _shadowShare;
    private Func<uint, bool>? _shadowPassed;
    private bool _shadowTried;

    /// <summary>The leg begins watching: its shadow is made (once), and with it the session's board when no one made it before, so the board hears what the leg's own list hears. A shadow that cannot be made is named once and left out.</summary>
    private void BeginThreatShadow()
    {
        if (_shadowTried)
        {
            return;
        }

        _shadowTried = true;
        try
        {
            _shadowPassed = PassedBy;
            _shadowSteps = new ThreatDetectorShadow(S, StepsDetector, Log, Context.Log, "station", hearsSince: true);
            var share = new ThreatShadowShare();
            share.Add(_shadowSteps.Lane);
            _shadowShare = share;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _shadowSteps = null;
            Context.Log.Error("station", $"{ThreatShadowLane.Token}{StepsDetector}: TODO(threat-shadow-failed): the shadow could not be made ({error.GetType().Name}: {error.Message}); the steps' list decides without it, as ever");
        }
    }

    /// <summary>The steps' attacker now, or 0 (<see cref="LiveAttackerOwn"/> decides, as ever); the board is asked the same beside it (report-only). Allocates nothing of its own while the two agree.</summary>
    protected uint LiveAttacker()
    {
        var attacker = LiveAttackerOwn();
        if (_shadowSteps is { } shadow)
        {
            var query = ThreatQuery.OnHer(HostileReach.DefaultMetres, ForgetSeconds) with { Skip = _shadowPassed };
            shadow.Answered(S.Clock.Now, in query, attacker != 0, attacker);
        }

        return attacker;
    }

    /// <summary>The leg ends: its line with the list's asks, when it asked at all.</summary>
    private void EndThreatShadow()
    {
        if (_shadowShare?.Line("quest-leg") is { } line)
        {
            Log(line);
        }
    }
}
