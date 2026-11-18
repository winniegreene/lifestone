using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 16 Q5 (docs/automation-engine.md section 6, <c>TODO(engine-health-flip)</c>; E2d had it in shadow): the vitals guard reads a creature's health from the session's one picture (<see cref="HealthPicture"/>), through
/// <see cref="PictureHealth.Points"/>: the pick of the creature to drain (<see cref="KnownFullest"/>), the attackers' health together in the losing race (<see cref="AttackersOnHer"/>) and a creature's health before the
/// bolt that killed it (<see cref="ReadKills"/>: the picture forgot it at its death and answers with its health just before). Its trackings (<see cref="KnownTargetHealth"/>: the fight-back's target, or its own) still say
/// which creatures are offered and carry their facts. The take of the guard's own drain is told to the picture. The picture is made at the guard's first sample, so it hears from then at the latest (it is the session's:
/// it may have heard long before).
/// </summary>
internal sealed partial class AutopilotVitalsGuard
{
    private HealthPicture? _healthPicture;

    /// <summary>The session's picture, made (and its feed begun) at the guard's first sample.</summary>
    private HealthPicture PictureOf(IGameSessionView session) => _healthPicture ??= ThreatBoard.For(session).Health;

    /// <summary>The health of <paramref name="known"/>'s creature now, from the one picture (<see cref="PictureHealth.Points"/>).</summary>
    private uint HealthOf(IGameSessionView session, KnownTargetHealth known, double clock, out HealthRead read) => PictureHealth.Points(PictureOf(session), known, session.State, clock, out read);

    /// <summary>The guard's own drain took <paramref name="take"/> from <paramref name="guid"/>: the picture is told.</summary>
    private void HealthPictureTook(uint guid, double take) => _healthPicture?.Took(guid, take);
}
