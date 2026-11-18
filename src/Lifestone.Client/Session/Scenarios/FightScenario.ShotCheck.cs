using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;

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

    /// <summary>True while the shot rule still leaves <paramref name="guid"/> alone from where she stands (<see cref="ShotLedger.IsDropped(uint, double, double)"/>: one that has come close is back).</summary>
    private bool ShotDropHolds(IGameSessionView session, uint guid, double clock) =>
        session.State.Self.Position is { } here && session.State.Objects.TryGet(guid, out var o) && StandsAt(session, o) is { } at
            ? Shots.IsDropped(guid, clock, at.ToGlobal().DistanceTo(here.ToGlobal()))
            : Shots.IsDropped(guid, clock);

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

        Shots.Drop(guid, name, clock, "the shot is blocked: " + shot.Why);
        _missesInARow = 0;
        _phase = Phase.Choose;
        return true;
    }

    /// <summary>A cast of the current target came to nothing: counted; true when it dropped the target and the pick goes on.</summary>
    private bool WastedDrops(IGameSessionView session, double clock, string why)
    {
        if (_target == 0 || !Shots.NoteWasted(_target, NameOfTarget(session, _target), clock, why))
        {
            return false;
        }

        _phase = Phase.Choose; // the misses in a row stand: they say this spot is bad, whichever target they were on
        return true;
    }
}
