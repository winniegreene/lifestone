using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session.Grounds;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 7 P7v (the item-end gate's death station): the wait among the tuskers walks by its walk's tour and tells it what the player sees, as the fight does. A slot's cell in the player's view for
/// <see cref="RespawnSchedule.SeenEmptySettleSeconds"/> with no creature of its own is seen empty and the next leg skips it; each leg's line names the tour's purpose. Before, the wait never looked, so a leg that did not reach its
/// cell (or a fresh tour after a den fight) was offered the same empty cell again.
/// </summary>
public sealed partial class DeathScenarioTests
{
    [Fact]
    public void P7v_an_empty_cell_in_view_for_two_seconds_of_the_wait_is_seen_empty_and_the_next_leg_skips_it()
    {
        var rig = new Rig(buffed: false);
        rig.Tuskers = false; // the cell is empty: no blow, so the quiet minute asks for the next leg
        var ground = DenGround.Profile;
        var empty = new GroundTour(ground, StandInMetres(ground), NullLogSink.Instance).Next(DenArrival, 0)!.Value; // the tour's first offer from the arrival: where leg 1 goes
        var tour = new GroundTour(ground, StandInMetres(ground), rig.Log, visible: c => c == DenArrival ? [empty] : null); // the empty cell is in the arrival's view
        var walk = new TourWalk(tour);
        var scenario = rig.Scenario(walk, DeathStages.Den | DeathStages.Death, new DeathOptions { Seconds = 100 });

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario)); // no tusker: the time limit
        Assert.Contains("did not kill the player", scenario.Failure, StringComparison.Ordinal);

        Assert.True(walk.Goals.Count >= 2, "legs " + walk.Goals.Count);
        Assert.Equal(empty, walk.Goals[0]); // looked at once before leg 1, not yet for the settle: the cell stands by the schedule
        var inEmpty = Enumerable.Range(0, tour.Schedule.Count).Where(i => tour.Schedule.IsHunted(i) && tour.Schedule.Slot(i).Cell == empty).ToList();
        Assert.NotEmpty(inEmpty);
        Assert.All(inEmpty, i => Assert.True(tour.Schedule.IsSeenEmpty(i), $"slot {i} seen empty"));
        Assert.Contains(Lines(rig), l => l.Contains($"in 0x{empty:X8}) is seen empty at", StringComparison.Ordinal));
        Assert.NotEqual(empty, walk.Goals[1]); // leg 2 skips it (before P7v the wait never looked, and leg 2 was this cell again)
        Assert.Equal(1, Lines(rig).Count(l => l.StartsWith("death: walking to the tuskers: purpose=presumed (", StringComparison.Ordinal) && l.Contains(string.Create(CultureInfo.InvariantCulture, $"tour leg 1 to 0x{empty:X8}"), StringComparison.Ordinal)));
        Assert.Contains(Lines(rig), l => l.StartsWith("death: no blow in 60 s; moving on: purpose=presumed (", StringComparison.Ordinal) && l.Contains("stands by the schedule and is not yet seen", StringComparison.Ordinal) && l.Contains("tour leg 2 to 0x", StringComparison.Ordinal));
        Assert.Contains(Lines(rig), l => l.StartsWith("death: the wait walks by the ground's tour: ", StringComparison.Ordinal));
    }

    [Fact]
    public void P7v_a_tour_left_with_a_fights_storeys_minimum_is_set_to_one_for_the_wait_and_logged()
    {
        var rig = new Rig(buffed: false);
        rig.Tuskers = false;
        var ground = DenGround.Profile;
        var tour = new GroundTour(ground, StandInMetres(ground), rig.Log) { StoreysMin = 2 }; // the den fight's --fight-storeys-min, on the journey's one tour
        var scenario = rig.Scenario(new TourWalk(tour), DeathStages.Den | DeathStages.Death, new DeathOptions { Seconds = 5 });

        rig.Run(scenario);

        Assert.Equal(1, tour.StoreysMin);
        Assert.Contains(Lines(rig), l => l == "death: the tour's --fight-storeys-min 2 (a fight's) is set to 1 for the wait: the wait wants tuskers, not storeys");
    }

    /// <summary>The walk stand-in for a profile (no dats): 100 m a storey plus half a metre a cell number (GroundTourPurposeTests' rule).</summary>
    private static Func<uint, uint, double> StandInMetres(HuntingGround ground) =>
        (a, b) => (100.0 * Math.Abs(ground.StoreyOf(a) - ground.StoreyOf(b))) + (Math.Abs((int)(a & 0xFFFF) - (int)(b & 0xFFFF)) * 0.5);

    /// <summary>A walk whose legs go to its tour's offers (from the cell the player stands in) and arrive after a few ticks without moving the player: the death wait's own view decides what the tour learns.</summary>
    private sealed class TourWalk(GroundTour tour) : IFightWalk
    {
        private int _ticks;
        private bool _walking;

        public List<uint> Goals { get; } = [];

        public string? Failure => null;

        public IGroundTour Tour => tour;

        public void Prepare(IGameSessionView session, double now)
        {
        }

        public bool BeginLeg(IGameSessionView session, double now, out string description)
        {
            var from = session.State.Self.Position?.CellId ?? 0;
            var goal = tour.Next(from, now) ?? from;
            Goals.Add(goal);
            description = string.Create(CultureInfo.InvariantCulture, $"tour leg {Goals.Count} to 0x{goal:X8}");
            (_walking, _ticks) = (true, 0);
            return true;
        }

        public FightWalkStatus Tick(IGameSessionView session, double now)
        {
            if (!_walking)
            {
                return FightWalkStatus.Idle;
            }

            if (++_ticks < 4)
            {
                return FightWalkStatus.Walking;
            }

            _walking = false;
            return FightWalkStatus.Arrived;
        }

        public void Abandon() => _walking = false;

        public void Dispose()
        {
        }
    }
}
