namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>The showpiece events that start a <c>--journey-shots</c> burst (M4 P5b J7, the hero video, owner decision D32).</summary>
public static class ShowpieceEvents
{
    /// <summary>The Archer looses a Greater Frost Arrow (the hunt station's shot count rises).</summary>
    public const string FrostArrow = "frost-arrow";

    /// <summary>The Brawler swings the Mace (the hunt station's swing count rises); the name is the owner's, the swing is the moment the hit is on screen.</summary>
    public const string MaceHit = "mace-hit";

    /// <summary>The mage casts Primary Portal Recall (the den-travel station: the cast window opens).</summary>
    public const string PortalRecall = "portal-recall";

    /// <summary>The recall lands inside the den (the den-travel station: the player is present in the den's landblock).</summary>
    public const string PortalArrival = "portal-arrival";

    /// <summary>The mage casts Cassius' Ring of Fire among the tuskers (the fight station's cast count rises while the fight's spell is 1785).</summary>
    public const string RingOfFire = "ring-of-fire";

    /// <summary>Every event, in the hero plans' order.</summary>
    public static IReadOnlyList<string> All { get; } = [FrostArrow, MaceHit, PortalRecall, PortalArrival, RingOfFire];

    /// <summary>
    /// A run on foot is under way in any station (the wayfinder has run <see cref="RunShotMetres"/> of it): not a hero showpiece but the wayfinder's clip of how the
    /// character runs (the owner's note of 2026-10-24, <c>nav-natural-steering</c>, <c>nav-creature-avoid</c>); a burst per run, at most one per event gap.
    /// </summary>
    public const string Run = "run";

    /// <summary>The metres a run has gone before its <see cref="Run"/> burst starts (past the turn on the spot at its start). Not retail: Lifestone's own.</summary>
    public const float RunShotMetres = 4f;
}

/// <summary>What the watch reads each frame: counters and flags the host takes from the session and the avatars.</summary>
/// <param name="Station">The running station's name (null between stations).</param>
/// <param name="Swings">The attack sequence's swings.</param>
/// <param name="Shots">The attack sequence's shots.</param>
/// <param name="FightCasts">The fight station's cast count (0 outside it).</param>
/// <param name="FightSpell">The spell the fight casts (0 outside it).</param>
/// <param name="CastWindow">True while a cast window is open on the player (the avatars' cast window).</param>
/// <param name="InDen">True when the player is present in the den's landblock.</param>
/// <param name="Runs">The runs on foot that have gone <see cref="ShowpieceEvents.RunShotMetres"/> so far (the host counts each runner once).</param>
public readonly record struct ShowpieceInput(string? Station, int Swings, int Shots, int FightCasts, uint FightSpell, bool CastWindow, bool InDen, int Runs = 0);

/// <summary>
/// Turns the frame-by-frame counters into the showpiece events (M4 P5b J7): <see cref="ShowpieceEvents.FrostArrow"/> and <see cref="ShowpieceEvents.MaceHit"/> in the hunt station, <see cref="ShowpieceEvents.PortalRecall"/> and
/// <see cref="ShowpieceEvents.PortalArrival"/> in the den-travel station, <see cref="ShowpieceEvents.RingOfFire"/> in the den-fight station, <see cref="ShowpieceEvents.Run"/> in any station. Engine-free, so the events are tested over scripted counters. A station's counters are the bases a later rise is
/// measured from (they restart when the station changes), and an event fires once per rise.
/// </summary>
public sealed class ShowpieceWatch
{
    /// <summary>The spell Cassius' Ring of Fire (the fight's spell when the ring is the showpiece).</summary>
    public const uint RingOfFireSpell = 1785;

    private string? _station;
    private int _swings;
    private int _shots;
    private int _casts;
    private bool _castWindow;
    private bool _inDen;
    private int _runs;
    private readonly List<string> _events = [];

    /// <summary>Reads one frame and returns the events it raised (valid until the next call; usually empty).</summary>
    /// <param name="input">The frame's counters.</param>
    public IReadOnlyList<string> Observe(in ShowpieceInput input)
    {
        _events.Clear();
        if (!string.Equals(_station, input.Station, StringComparison.Ordinal))
        {
            _station = input.Station;
            (_swings, _shots, _casts, _castWindow, _inDen, _runs) = (input.Swings, input.Shots, input.FightCasts, input.CastWindow, input.InDen, input.Runs);
            return _events;
        }

        if (input.Station is not null && input.Runs > _runs)
        {
            _events.Add(ShowpieceEvents.Run);
        }

        switch (input.Station)
        {
            case JourneyStationNames.Hunt:
                if (input.Shots > _shots)
                {
                    _events.Add(ShowpieceEvents.FrostArrow);
                }

                if (input.Swings > _swings)
                {
                    _events.Add(ShowpieceEvents.MaceHit);
                }

                break;
            case StationKinds.DenTravel:
                if (input.CastWindow && !_castWindow)
                {
                    _events.Add(ShowpieceEvents.PortalRecall);
                }

                if (input.InDen && !_inDen)
                {
                    _events.Add(ShowpieceEvents.PortalArrival);
                }

                break;
            case JourneyStationNames.DenFight:
                if (input.FightSpell == RingOfFireSpell && input.FightCasts > _casts)
                {
                    _events.Add(ShowpieceEvents.RingOfFire);
                }

                break;
        }

        (_swings, _shots, _casts, _castWindow, _inDen, _runs) = (input.Swings, input.Shots, input.FightCasts, input.CastWindow, input.InDen, input.Runs);
        return _events;
    }
}
