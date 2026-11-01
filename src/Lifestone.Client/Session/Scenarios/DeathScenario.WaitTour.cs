using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M5 7 P7v (the item-end gate's death station, D141 check-act-check): the wait among the tuskers walks by the walk's tour, and tells it what the player sees.
/// <list type="bullet">
/// <item><b>The tour's choice.</b> Each leg of the wait is the tour's next offer (<see cref="IGroundTour.Next"/>: a seen tusker, a slot standing by the schedule, a slot due), its purpose and note logged with the leg. In a journey the
/// walk's tour is the ground's one tour (<see cref="Journey.JourneyGroundTours"/>), so it knows the den fight's kills and their due times, and the legs go where tuskers stand, not round the cells the fight emptied (gate item-end-2:
/// nine legs over z 0 with <c>creatures_max_walk=0</c>, <c>died=0</c>). There is no second chooser here.</item>
/// <item><b>The view.</b> Every frame of <c>death-wait</c> the player's cell and the object table go to the tour (<see cref="IGroundTour.Look"/>, as the fight does): a slot's cell in view for
/// <see cref="RespawnSchedule.SeenEmptySettleSeconds"/> with no creature of its own is seen empty, and the next leg skips it; a living tusker the table holds is walked toward.</item>
/// <item><b>The storeys.</b> The wait wants tuskers, not storeys: a <see cref="GroundTour"/> left with a fight's <c>--fight-storeys-min</c> is set back to 1 for the wait (logged), so the tour's leave-for-the-storeys does not pass
/// over a tusker on the player's own storey. The next fight sets its own when it is made.</item>
/// </list>
/// </summary>
public sealed partial class DeathScenario
{
    /// <summary>Before the wait's first leg: the storeys rule set back (logged), what the tour's schedule knows logged, and one look so the first leg can walk toward a tusker in view.</summary>
    private void BeginWaitTour(IGameSessionView session, double clock)
    {
        if (_walk?.Tour is not { } tour)
        {
            _log.Info(LogCategory, "death: the walk has no tour: the wait's legs are the walk's own (no schedule, no view)");
            return;
        }

        if (tour is GroundTour ground)
        {
            if (ground.StoreysMin != 1)
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"death: the tour's --fight-storeys-min {ground.StoreysMin} (a fight's) is set to 1 for the wait: the wait wants tuskers, not storeys"));
                ground.StoreysMin = 1;
            }

            var schedule = ground.Schedule;
            _log.Info(
                LogCategory,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"death: the wait walks by the ground's tour: {schedule.StandingHunted} of {schedule.HuntedCount} hunted slot(s) standing by its schedule, {schedule.SeenEmpty} seen empty in all; each leg is the tour's choice, its purpose logged"));
        }

        LookAround(session, clock);
    }

    /// <summary>One frame of the wait: the player's cell and the object table go to the walk's tour (F5's look). Allocates nothing per frame (the tour's view of a cell is made the first time it is seen).</summary>
    private void LookAround(IGameSessionView session, double clock)
    {
        if (_walk?.Tour is { } tour && session.State.Self.Position is { } at)
        {
            tour.Look(at.CellId, session.State.Objects, clock);
        }
    }

    /// <summary>The leg's purpose as the tour names it, for the leg's log line (<c>purpose=presumed (slot 40 ... stands by the schedule and is not yet seen); </c>), or empty for a walk whose tour names none.</summary>
    private string LegPurpose() =>
        _walk?.Tour is { Purpose: not TourPurpose.None } tour ? $"purpose={tour.Purpose.ToString().ToLowerInvariant()} ({tour.PurposeNote}); " : string.Empty;
}
