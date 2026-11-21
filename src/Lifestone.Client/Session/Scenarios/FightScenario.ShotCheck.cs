using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 8 L: the climb's half of the rule both fights share (<see cref="ShotLedger"/>). The pick's line test (<see cref="LineIsClear"/>) stays as the cheap sieve over many creatures; this is the one check of the one shot about to be paid for, on the server's own launch test.
/// The case that escaped: <see cref="WallHit"/> needs a bolt the tracker took for ours, and a target right under a ledge makes a bolt whose flat heading points back at the player, so the tracker reads "no projectile" and the wall rule never ran (and <see cref="WallStay"/> is outdoors only).
/// </summary>
public sealed partial class FightScenario
{
    private ShotLedger? _shotLedger;

    private ShotLedger Shots => _shotLedger ??= new ShotLedger(line => _log.Info(LogCategory, line));

    /// <summary>The casts the shot rule counted and held back, for the tests.</summary>
    internal ShotLedger ShotTally => Shots;

    private string NameOfTarget(IGameSessionView session, uint guid) => session.State.Objects.TryGet(guid, out var o) && !string.IsNullOrEmpty(o.Name) ? o.Name : "unknown";

    /// <summary>
    /// M6 fight targeting (run 9): the least move, in metres, of her or the target that makes a new place for a shot drop (<see cref="ShotLedger.IsDropped(uint, double, in CellFrame, in CellFrame, double)"/>). It is the fight's
    /// own measure of two places that really differ, <see cref="LivePositionGapMetres"/> (above the sticky follow's 0.3 m gap and an interpolation's lag): no new number.
    /// </summary>
    public const double ShotDropMovedMetres = LivePositionGapMetres;

    /// <summary>
    /// True while the shot rule still leaves <paramref name="guid"/> alone from where she stands: the drop belongs to the place it was measured at (<see cref="ShotLedger.IsDropped(uint, double, in CellFrame, in CellFrame, double)"/>:
    /// she or it has moved, or one measured farther than 3 m has come close, and it is back). Reads her position and the target where the fight takes it to stand (<see cref="StandsAt"/>).
    /// </summary>
    private bool ShotDropHolds(IGameSessionView session, uint guid, double clock) =>
        session.State.Self.Position is { } here && session.State.Objects.TryGet(guid, out var o) && StandsAt(session, o) is { } at
            ? Shots.IsDropped(guid, clock, in here, in at, ShotDropMovedMetres)
            : Shots.IsDropped(guid, clock);

    /// <summary><see cref="ShotDropHolds"/> on positions the caller has read already.</summary>
    private bool ShotDropHoldsAt(uint guid, double clock, in CellFrame here, in CellFrame at) => Shots.IsDropped(guid, clock, in here, in at, ShotDropMovedMetres);

    /// <summary>Where she and <paramref name="guid"/> stand now, for a drop's place; null when either position is not known.</summary>
    private static ShotPlace? PlaceOf(IGameSessionView session, uint guid) =>
        session.State.Self.Position is { } here && session.State.Objects.TryGet(guid, out var o) && StandsAt(session, o) is { } at ? new ShotPlace(here, at) : null;

    /// <summary>M6 fight targeting (run 9): the drop's real reason in words, for the life-through lines: what measured it.</summary>
    private string DropReasonOf(uint guid) =>
        Shots.DropKindOf(guid, out var kind) && kind == ShotDropKind.Blocked
            ? "the shot rule dropped it: the shot check before the cast found the bolt's path blocked from where she stood, and no bolt was sent"
            : "the shot rule dropped it: its bolts came to nothing";

    /// <summary>The check before the bolt: true when the shot is blocked and the frame is taken (outdoors a step toward a better line; else the target is dropped and the pick goes on). Nothing is cast and nothing is paid.</summary>
    private bool ShotRefused(IGameSessionView session, double clock, uint guid, uint spellId)
    {
        if (_options.IgnoreLine)
        {
            return false;
        }

        var name = NameOfTarget(session, guid);
        var shot = Shots.Before(session.Actions.Shots, session, spellId, guid, name);
        if (shot.Answer != ShotAnswer.Blocked)
        {
            return false;
        }

        if (WallStay(session, clock))
        {
            return true;
        }

        Shots.Drop(guid, name, clock, "the shot is blocked: " + shot.Why, ShotDropKind.Blocked, PlaceOf(session, guid));
        _missesInARow = 0;
        _phase = Phase.Choose;
        return true;
    }

    /// <summary>A cast of the current target came to nothing: counted; true when it dropped the target and the pick goes on.</summary>
    private bool WastedDrops(IGameSessionView session, double clock, string why)
    {
        if (_target == 0 || !Shots.NoteWasted(_target, NameOfTarget(session, _target), clock, why, PlaceOf(session, _target)))
        {
            return false;
        }

        _phase = Phase.Choose; // the misses in a row stand: they say this spot is bad, whichever target they were on
        return true;
    }
}
