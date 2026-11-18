using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Grounds;
using static Lifestone.Client.Tests.Session.Grounds.TwoStoreyGround;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 14 pit-hold (live 2026-11-13 14:53:51 to 15:04:39 UTC, the Tusker Abode, <c>cache/m6/14/live/abode5</c>): the climber stood on the pit's lip in its floorless cell 0x5C4502A9, no walk could start there, and the tour held
/// 438 times on "slot N ... is due ...; its cell is not offerable now". A slot she cannot walk to now is never waited for: the tour's last resort (D47) is the earliest due slot a walk reaches, and with none it holds naming the
/// slots it leaves. Over the synthetic <see cref="TwoStoreyGround"/> (its top storey alone), with walks that some cells have none of.
/// </summary>
public sealed class GroundTourPitHoldTests
{
    /// <summary>A cell of no storey of the test ground, as the Abode's pit cell is of the Abode.</summary>
    private const uint Pit = 0x0A0A02A9;

    /// <summary>The test ground's walks with <paramref name="cut"/> joined to no other cell (a slot past a jump she cannot make), and <see cref="Pit"/> joined to none (no walk starts in a floorless cell).</summary>
    private static Func<uint, uint, double> WalksWithout(params uint[] cut) => (a, b) =>
        a == b ? 0 : a == Pit || b == Pit || cut.Contains(a) || cut.Contains(b) ? double.PositiveInfinity : Metres(a, b);

    /// <summary>Every hunted slot of the top storey killed: T(1)'s first (the earliest due), then T(2)'s two.</summary>
    private static GroundTour AllDead(Func<uint, uint, double> metres, ILogSink log)
    {
        var tour = new GroundTour(Build(oneStorey: true), metres, log);
        tour.Killed(Slave, T(1), 0);
        tour.Killed(Slave, T(2), 1);
        tour.Killed(Slave, T(2), 2);
        Assert.Equal(0, tour.Schedule.StandingHunted);
        return tour;
    }

    [Fact]
    public void A_dead_slot_no_walk_reaches_is_not_waited_for_and_the_tour_walks_to_the_earliest_one_it_reaches()
    {
        var log = new RecordingLogSink();
        var tour = AllDead(WalksWithout(T(1)), log);
        var first = tour.Schedule.EarliestDueOn(-1);
        Assert.Equal(T(1), tour.Schedule.Slot(first).Cell); // the earliest due of all is past the cut

        Assert.Equal(T(2), tour.Next(Arrival, 10)); // before the fix: T(1), a walk that cannot be planned, then "its cell is not offerable now"
        Assert.Equal(TourPurpose.Due, tour.Purpose);
        Assert.Contains("in 0x0A0A0102", tour.PurposeNote, StringComparison.Ordinal);
        tour.LegArrived(T(2), 12);

        Assert.Equal(T(2), tour.Next(T(2), 13)); // there: the hold, for a slot she reached
        Assert.Equal(TourPurpose.Hold, tour.Purpose);
        Assert.Contains("in 0x0A0A0102", tour.PurposeNote, StringComparison.Ordinal);
        Assert.DoesNotContain(log.Lines, l => l.Contains("not offerable now", StringComparison.Ordinal));
    }

    [Fact]
    public void Asked_from_the_pit_s_cell_the_tour_holds_for_no_slot_and_names_the_ones_it_leaves()
    {
        // The live case: from the pit's cell every walk is infinite. The hold no longer names a due slot whose "cell is not offerable now"; it names the dead slots no walk reaches and waits for none of them.
        var log = new RecordingLogSink();
        var tour = AllDead(WalksWithout(), log);
        Assert.Equal(Pit, tour.Next(Pit, 10));
        Assert.Equal(TourPurpose.Hold, tour.Purpose);
        Assert.Contains("no dead hunted slot she can walk to now: 3 dead slot(s)", tour.PurposeNote, StringComparison.Ordinal);
        Assert.Contains("none is waited for", tour.PurposeNote, StringComparison.Ordinal);
        Assert.DoesNotContain("not offerable now", tour.PurposeNote, StringComparison.Ordinal);
        Assert.Single(log.Lines, l => l.Contains("tour: holding in 0x0A0A02A9 on 'no storey'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_cell_whose_leg_could_not_be_planned_rests_and_the_tour_walks_to_the_next_due_slot_instead()
    {
        // The walk tells the tour of a waypoint no plan reached (RunnerLegs, M6 14 pit-hold): that cell rests, and the earliest due slot the tour waits for is the next one it can walk to.
        var tour = AllDead(WalksWithout(), NullLogSink.Instance);
        Assert.Equal(T(1), tour.Next(Arrival, 10));
        tour.LegFailed(T(1), 10);
        Assert.Equal(T(2), tour.Next(Arrival, 10.5));
        Assert.Equal(TourPurpose.Due, tour.Purpose);
        tour.LegFailed(T(2), 10.5);
        Assert.Equal(Arrival, tour.Next(Arrival, 11));
        Assert.Equal(TourPurpose.Hold, tour.Purpose);
        Assert.Contains("none is waited for", tour.PurposeNote, StringComparison.Ordinal);
        Assert.Equal(T(1), tour.Next(Arrival, 10 + GroundTour.FailedCellRestSeconds + 1)); // the rest over, the earliest due slot is offered again
    }
}
