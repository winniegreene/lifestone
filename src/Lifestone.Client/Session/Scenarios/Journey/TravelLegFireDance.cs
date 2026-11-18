using System.Globalization;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 fire dance and walk drains: the travel leg's side. M6 16 Q7: the dance under fire moved with the fight-back into the skirmish (<c>Skirmish.FireDance.cs</c>, unchanged); the leg keeps the publishing of the fight-back's target
/// for the vitals guard (<see cref="PublishTarget"/>, the skirmish's <see cref="ISkirmishHost.NewTarget"/>) and shows the dance's casts (<see cref="FireDanceCasts"/>).
/// </summary>
internal abstract partial class TravelLeg
{
    /// <summary>The casts the dance under fire made on this leg (report-only).</summary>
    protected int FireDanceCasts => Skirmish.FireDanceCasts;

    /// <summary>
    /// M6 14 walk drains (the owner, 2026-11-15: the target's health and kind are known data): the fight-back's new target published with its health as data (<see cref="KnownTargetHealth"/>: its facts' maximum health, from the ground profiles
    /// or the world database's table, less what she deals it), for the vitals guard's drain pick; the words for the target's line. A weenie with no facts is said by name and nothing is published (no drain is priced on a guess).
    /// M6 16 Q5: the health in the words is the session's one picture's (<see cref="PictureHealth"/>: what an earlier fight or leg dealt it stays dealt, and it mends at its own rate); the published tracking carries the facts,
    /// and the guard reads its health from the same picture.
    /// </summary>
    private string PublishTarget(uint guid, string name)
    {
        var wcid = S.State.Objects.TryGet(guid, out var o) ? o.Weenie.WeenieClassId : 0u;
        if (CreatureFacts.All.MaxHealthOf(wcid, out var source) is not { } max)
        {
            Context.Facts.FightBackTarget = null;
            return $"its health is not known: {source}; no drain is priced on it";
        }

        var defence = Kinds.Of(wcid, name).MagicDefence ?? 0; // M6 16 E1e: the bestiary's; a kind it holds no Magic Defense for is 0 as before, named once a kind a leg (LegKindRead)
        var known = new KnownTargetHealth(guid, wcid, name, max, defence, string.Create(CultureInfo.InvariantCulture, $"weenie {wcid}, {source}, Magic Defense {defence}, health drain resistance {DrainResist.Of(wcid):0.##}"), S.State.Chat.Total);
        Context.Facts.FightBackTarget = known;
        var health = PictureHealth.Points(ThreatBoard.For(S).Health, known, S.State, S.Clock.Now, out var read); // M6 16 Q5: the one picture
        return "its health as data: " + PictureHealth.Words(known, health, in read, S.State);
    }
}
