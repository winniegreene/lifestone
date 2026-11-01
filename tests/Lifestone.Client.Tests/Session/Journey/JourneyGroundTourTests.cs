using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Grounds;
using Lifestone.Data.Tests;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M5 7 P7v (the item-end gate's death station, gate item-end-2: <c>died=0 hits_taken=0 health_min_pct=99.6 death_s=600</c>): the journey host gives every walk on a ground the ground's one tour (<see cref="JourneyGroundTours"/>), so the
/// death station's wait knows the den fight's kills. Before, each walk made a fresh tour, unseeded, and after a den fight that had emptied z 0 the death station's legs went round z 0's empty cells (0x01050383 first) for its whole
/// wait. Over the dats: the walk's metres are the den's room graph, as live.
/// </summary>
public sealed class JourneyGroundTourTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    /// <summary>The den's arrival, where the death station's first leg began in gate item-end-2.</summary>
    private const uint DenArrival = 0x01050375;

    /// <summary>The first leg of a fresh tour from the arrival at any clock: the nearest z 0 slot cell by the room graph (gate item-end-2's death leg 1).</summary>
    private const uint FreshFirstLeg = 0x01050383;

    [RequiresDatsFact]
    public void After_the_den_fight_killed_every_z_0_slot_the_death_stations_first_leg_on_the_shared_tour_is_a_standing_slot_on_z_minus_6()
    {
        var log = new RecordingLogSink();
        using var host = new HeadlessJourneyRunnerHost(fixture.Dats, log);
        var ground = DenGround.Profile;
        var z0 = ground.StoreyOf(DenArrival);
        Assert.Equal("z 0", ground.Storeys[z0].Name);

        // The den fight's walk: its tour is told the kill of every hunted z 0 slot at t=0 (P7o's two-storey hold kept the fight on z 0 until all of them were dead).
        using var fightWalk = host.FightWalk(null!, ground); // the context is not read
        var tour = Assert.IsType<GroundTour>(fightWalk.Tour);
        var killed = 0;
        for (var i = 0; i < tour.Schedule.Count; i++)
        {
            if (tour.Schedule.IsHunted(i) && tour.Schedule.StoreyOf(i) == z0 && tour.Schedule.IsStanding(i))
            {
                tour.Killed(tour.Schedule.Slot(i).Wcid, tour.Schedule.Slot(i).Cell, 0);
                killed++;
            }
        }

        Assert.True(killed > 0);
        Assert.Equal(0, tour.Schedule.StandingOn(z0));

        // The death station's walk, made after: the same tour, so its first leg at t=100 (z 0's slots due at 960 s at the latest) is a slot standing by the schedule, on z -6.
        using var deathWalk = host.FightWalk(null!, ground);
        Assert.Same(tour, deathWalk.Tour);
        var first = deathWalk.Tour!.Next(DenArrival, 100);
        Assert.NotNull(first);
        Assert.Equal(TourPurpose.Presumed, tour.Purpose);
        Assert.Equal("z -6", ground.Storeys[ground.StoreyOf(first.Value)].Name);
        Assert.Contains(Enumerable.Range(0, tour.Schedule.Count), i => tour.Schedule.IsHunted(i) && tour.Schedule.IsStanding(i) && tour.Schedule.Slot(i).Cell == first.Value);
        Assert.Contains(log.Lines, l => l.Contains("the ground's tour of", StringComparison.Ordinal) && l.Contains("is made for the journey", StringComparison.Ordinal));
        Assert.Contains(log.Lines, l => l.Contains("is kept for this walk", StringComparison.Ordinal) && l.Contains($"{killed} dead with their due times kept", StringComparison.Ordinal));

        // The regression baseline: a fresh tour (each walk's own, before P7v) knows no kill, and its first leg is the nearest z 0 slot cell, empty.
        var fresh = GroundTour.For(ground, host.GroundGraph(ground), NullLogSink.Instance);
        Assert.Equal(FreshFirstLeg, fresh.Next(DenArrival, 100));
        Assert.Equal(z0, ground.StoreyOf(FreshFirstLeg));
    }
}
