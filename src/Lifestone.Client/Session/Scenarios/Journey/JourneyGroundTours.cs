using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M5 7 P7v (the item-end gate's death station): one tour per ground for the whole journey, which both journey hosts (the headless one and the Godot one) give every walk they make on that ground: the den fight's, the death
/// station's wait among the tuskers, and the corpse trip's fight. Before, each walk made its own tour, unseeded (the schedule's "every slot stands at the start"), so a death station after a den fight that had killed every tusker
/// of the arrival's storey walked from one of those empty cells to the next for its whole wait (gate item-end-2: <c>died=0</c>, nine legs over z 0, <c>creatures_max_walk=0</c>, while their 900 s respawn could not land in it).
/// One tour keeps the schedule's kills, their due times and the cells seen empty, so the next walk on the ground goes where the schedule has tuskers standing, by the tour's own choice.
/// </summary>
/// <remarks>Game thread only, as the tours are. Keyed by <see cref="HuntingGround.Key"/>; a tour is made the first time a walk on its ground asks and kept until the host is disposed (the autopilot makes a host per scenario).</remarks>
/// <param name="log">Where the tour's making and each reuse are logged.</param>
/// <param name="logCategory">The host's log category.</param>
public sealed class JourneyGroundTours(ILogSink log, string logCategory)
{
    private readonly Dictionary<string, IGroundTour> _tours = new(StringComparer.Ordinal);

    /// <summary>The tours made so far (one per ground).</summary>
    public int Count => _tours.Count;

    /// <summary>The ground's one tour for the journey: the one made before for <paramref name="ground"/>'s key, else <paramref name="make"/>'s, kept. Each answer is logged (made, or kept with what its schedule knows).</summary>
    /// <param name="ground">The ground the walk is on.</param>
    /// <param name="make">Makes the ground's tour the first time (<see cref="GroundTour.For"/> over the host's room graph).</param>
    public IGroundTour For(HuntingGround ground, Func<IGroundTour> make)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(make);
        if (_tours.TryGetValue(ground.Key, out var kept))
        {
            log.Info(logCategory, $"the ground's tour of {ground.Name} is kept for this walk (one tour per ground for the journey, M5 7 P7v): {Knows(kept)}");
            return kept;
        }

        var tour = make();
        _tours[ground.Key] = tour;
        log.Info(logCategory, $"the ground's tour of {ground.Name} is made for the journey (one tour per ground, M5 7 P7v: every later walk on it, the death station's and the corpse trip's included, keeps its schedule)");
        return tour;
    }

    /// <summary>What a kept tour's schedule knows, for the log: its standing, dead and seen-empty hunted slots and the earliest due.</summary>
    private static string Knows(IGroundTour tour)
    {
        if (tour is not GroundTour ground)
        {
            return "a tour with no schedule (the profile's order)";
        }

        var schedule = ground.Schedule;
        var standing = schedule.StandingHunted;
        var first = schedule.EarliestDueOn(-1);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{standing} of {schedule.HuntedCount} hunted slot(s) standing by its schedule, {schedule.HuntedCount - standing} dead with their due times kept{(first >= 0 ? $" (the earliest due at {schedule.DueAt(first):0} s, in 0x{schedule.Slot(first).Cell:X8})" : string.Empty)}, {schedule.SeenEmpty} seen empty in all");
    }
}
