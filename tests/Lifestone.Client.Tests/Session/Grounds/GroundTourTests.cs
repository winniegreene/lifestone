using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Grounds;
using static Lifestone.Client.Tests.Session.Grounds.TwoStoreyGround;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// The ground's tour (M5 item 2 g6, D25, D40, D46): the profile's storeys in order, down and back up; the nearest cell with a standing hunted slot; the timed return to the arrival's storey when its earliest slot comes due
/// within the walk back plus the margin, and not before; <c>--fight-storeys-min 2</c> leaving a storey after one kill; a ground of one storey. Over the synthetic <see cref="TwoStoreyGround"/> (two generators of different
/// delays and ticks) and the den's profile (its five storeys; the walk there is a stand-in, 100 m a storey plus the cell numbers' difference, since these tests read no dats).
/// </summary>
public sealed class GroundTourTests
{
    private static GroundTour Tour(HuntingGround? ground = null, ILogSink? log = null) => new(ground ?? Build(), Metres, log ?? NullLogSink.Instance);

    [Fact]
    public void The_next_waypoint_is_the_nearest_cell_with_a_standing_hunted_slot()
    {
        var tour = Tour();

        Assert.Equal(T(1), tour.Next(Arrival, 0)); // 10 m; T(2) is 20 m and T(3) holds only an avoided slot
        Assert.Equal(T(2), tour.Next(T(3), 1)); // from x 30: T(2) at 10 m beats T(1) at 20 m; T(3) is never offered (the Ghost's)
        tour.Killed(Slave, T(2), 2);
        Assert.Equal(T(2), tour.Next(T(1), 3)); // the second Slave of T(2) still stands
        tour.Killed(Slave, T(2), 4);
        Assert.Equal(T(1), tour.Next(T(2), 5)); // T(2)'s two are dead: T(1)'s Slave, on the same storey, before the low storey
    }

    [Fact]
    public void The_tour_goes_down_the_storeys_in_order_and_back_up()
    {
        var log = new RecordingLogSink();
        var tour = Tour(log: log);
        tour.StoreysMin = 2; // so the top still has standing Slaves when the low storey is cleared

        Assert.Equal(T(1), tour.Next(Arrival, 0));
        tour.Killed(Slave, T(1), 10);
        Assert.Equal(L(1), tour.Next(T(1), 11)); // down to the low storey, its nearest standing cell (10 + 20 + 10 m)
        Assert.Contains(log.Lines, l => l.Contains("leaving storey 'top' for 'low'", StringComparison.Ordinal));

        Assert.Equal(L(3), tour.Next(L(1), 20));
        tour.Killed(Guard, L(1), 21);
        tour.Killed(Guard, L(3), 22);
        Assert.Equal(L(5), tour.Next(L(3), 23));
        tour.Killed(Guard, L(5), 24);
        Assert.Equal(T(2), tour.Next(L(5), 25)); // the bottom is cleared: back up to the top, to its standing Slaves
        Assert.Equal(0, tour.Returns); // the order's turn, not a timed return
        Assert.Equal(2, tour.StoreysVisited);
    }

    [Fact]
    public void A_timed_return_turns_back_when_the_arrival_storeys_first_slot_is_due_within_the_walk_back_and_the_margin_and_not_before()
    {
        var log = new RecordingLogSink();
        var tour = Tour(log: log);
        tour.Killed(Slave, T(1), 1); // due at 1 + 600 + 60 = 661 (Top's phase unknown)
        tour.Killed(Slave, T(2), 2); // due 662
        tour.Killed(Slave, T(2), 3); // due 663
        Assert.Equal(L(1), tour.Next(T(2), 4));

        // From L(5) (x 50) the walk back to T(1) is 50 + 20 + 10 = 80 m, 7.7 s at 10.4 m/s; plus the 60 s margin: the return is due at 661 - 67.7 = 593.3.
        var walkBack = Metres(L(5), T(1)) / GroundTour.DefaultRunMetresPerSecond;
        var turn = 661 - walkBack - GroundTour.ReturnMarginSeconds;
        Assert.Equal(L(3), tour.Next(L(5), turn - 1)); // not before: the low storey's standing Guards
        Assert.False(tour.Returning);
        Assert.Equal(0, tour.Returns);

        Assert.Equal(T(1), tour.Next(L(5), turn + 0.1)); // the first of those slots' cells
        Assert.True(tour.Returning);
        Assert.Equal(1, tour.Returns);
        Assert.Contains(log.Lines, l => l.Contains("the timed return 1: slot 0 in 0x0A0A0101", StringComparison.Ordinal) && l.Contains("(phase unknown)", StringComparison.Ordinal));

        Assert.Equal(T(1), tour.Next(L(3), turn + 5)); // still returning, one return counted
        Assert.Equal(1, tour.Returns);
        Assert.Equal(T(1), tour.Next(T(1), turn + 30)); // back on the arrival's storey: it waits, here, where the first slot is due within the window (2-R3: a hold)
        Assert.False(tour.Returning);
    }

    [Fact]
    public void On_the_arrival_storey_a_slot_due_in_the_cell_the_player_stands_in_is_a_hold_not_a_leave_and_returns_count_only_returns_from_another_storey()
    {
        var log = new RecordingLogSink();
        var tour = Tour(log: log);
        tour.Killed(Slave, T(1), 1); // due at 661 (Top's phase unknown)
        tour.Killed(Slave, T(2), 2); // 662
        tour.Killed(Slave, T(2), 3); // 663: nothing hunted stands on the top

        // At 620 the slot in the very cell the player stands in is due in 41 s, inside the 60 s margin: the tour holds there. It used to leave for the low storey and be turned straight back.
        Assert.Equal(T(1), tour.Next(T(1), 620));
        Assert.Contains(log.Lines, l => l.Contains("tour: holding in 0x0A0A0101", StringComparison.Ordinal));
        Assert.Equal((0, false), (tour.Returns, tour.Returning));
        Assert.Equal(1, tour.StoreysVisited);

        // Due far off (at 100 it is 561 s away): leaving is right. The return, once it is time, counts from the other storey.
        Assert.Equal(L(1), tour.Next(T(1), 100));
        var walkBack = Metres(L(5), T(1)) / GroundTour.DefaultRunMetresPerSecond;
        var turn = 661 - walkBack - GroundTour.ReturnMarginSeconds;
        Assert.Equal(T(1), tour.Next(L(5), turn + 0.1));
        Assert.Equal((1, true), (tour.Returns, tour.Returning));

        // Back on the arrival's storey and holding again: the return count stays 1.
        Assert.Equal(T(1), tour.Next(T(1), 640));
        Assert.Equal((1, false), (tour.Returns, tour.Returning));
    }

    [Fact]
    public void The_tour_tells_the_pick_to_leave_a_storey_after_one_kill_until_the_minimum_is_visited_and_only_with_a_storey_to_go_to()
    {
        var tour = Tour();
        tour.StoreysMin = 2;
        Assert.False(tour.WantsToLeave(Arrival, 0, out _)); // no kill yet
        tour.Killed(Slave, T(1), 5);
        Assert.True(tour.WantsToLeave(T(1), 6, out var next)); // one kill here, one storey of two visited
        Assert.Equal(1, next); // the low storey, the tour's next
        Assert.False(tour.WantsToLeave(L(1), 6, out _)); // the player is not on the storey the tour last saw him on

        tour.Next(T(1), 7);
        tour.Killed(Guard, L(1), 31); // a kill below: two storeys visited
        Assert.False(tour.WantsToLeave(L(1), 32, out _));

        var one = Tour(Build(oneStorey: true)); // one storey: nowhere to go
        one.StoreysMin = 2;
        one.Killed(Slave, T(1), 5);
        Assert.False(one.WantsToLeave(T(1), 6, out _));
    }

    [Fact]
    public void The_walk_back_uses_the_measured_run_speed()
    {
        var tour = Tour();
        Assert.Equal(GroundTour.DefaultRunMetresPerSecond, tour.RunMetresPerSecond);
        Assert.Equal(L(3), tour.Next(L(5), 0)); // 20 m
        tour.LegArrived(L(3), 4); // 20 m in 4 s: 5 m/s
        Assert.Equal(5, tour.RunMetresPerSecond, 6);
        tour.LegArrived(L(1), 5); // not the waypoint offered: no sample
        Assert.Equal(5, tour.RunMetresPerSecond, 6);
        Assert.Equal(L(1), tour.Next(L(3), 10));
        tour.LegArrived(L(1), 12); // 20 m in 2 s: 40 m over 6 s in all
        Assert.Equal(40.0 / 6, tour.RunMetresPerSecond, 6);
    }

    [Fact]
    public void Fight_storeys_min_two_leaves_a_storey_after_one_kill_until_two_storeys_are_visited()
    {
        var one = Tour();
        Assert.Equal(T(1), one.Next(Arrival, 0));
        one.Killed(Slave, T(1), 5);
        Assert.Equal(T(2), one.Next(T(1), 6)); // the default (1): the top's other Slaves first

        var two = Tour();
        two.StoreysMin = 2;
        Assert.Equal(T(1), two.Next(Arrival, 0));
        two.Killed(Slave, T(1), 5);
        Assert.Equal(L(1), two.Next(T(1), 6)); // one kill on the top: down, though two Slaves stand there
        Assert.Equal(L(3), two.Next(L(1), 30)); // on the low storey: two storeys visited
        two.Killed(Guard, L(1), 31);
        Assert.Equal(L(3), two.Next(L(1), 32)); // a kill here no longer leaves it
        Assert.Equal(2, two.StoreysVisited);
    }

    [Fact]
    public void A_ground_of_one_storey_stays_on_it_and_never_returns()
    {
        var tour = Tour(Build(oneStorey: true));

        Assert.Equal(T(1), tour.Next(Arrival, 0));
        tour.Killed(Slave, T(1), 1);
        tour.Killed(Slave, T(2), 2);
        tour.Killed(Slave, T(2), 3);
        Assert.Equal(T(1), tour.Next(T(2), 4)); // nothing stands: the earliest due slot's cell (the tour waits there)
        Assert.Equal(T(1), tour.Next(T(1), 650)); // T(1)'s slot is dead and due in 11 s, in the cell the player stands in: it holds (2-R3)
        Assert.Equal(0, tour.Returns);
        Assert.Equal(1, tour.StoreysVisited);
    }

    [Fact]
    public void A_waypoint_with_no_walk_is_not_offered_again_in_the_same_leg_and_a_failed_one_rests()
    {
        var tour = Tour();
        Assert.Equal(T(1), tour.Next(Arrival, 0));
        Assert.Equal(T(2), tour.Next(Arrival, 0)); // the same leg asks again: the next nearest
        tour.LegFailed(T(1), 1);
        Assert.Equal(T(2), tour.Next(Arrival, 2)); // T(1) rests for 120 s
        Assert.Equal(T(1), tour.Next(Arrival, 2 + GroundTour.FailedCellRestSeconds));
        Assert.True(tour.Waypoints >= 5);
    }

    [Fact]
    public void The_dens_tour_walks_its_storeys_down_in_the_profiles_order()
    {
        var den = GroundCatalog.Get("black-spawn-den-aluvian");
        double DenMetres(uint a, uint b) => (100.0 * Math.Abs(den.StoreyOf(a) - den.StoreyOf(b))) + (Math.Abs((int)(a & 0xFFFF) - (int)(b & 0xFFFF)) * 0.5);
        var tour = new GroundTour(den, DenMetres, NullLogSink.Instance);
        Assert.Equal(0, tour.ArrivalStorey);

        var from = den.Arrival.Cell;
        var storeys = new List<string>();
        for (var step = 0; step < 200 && tour.Schedule.StandingHunted > 0; step++)
        {
            if (tour.Next(from, step) is not { } cell)
            {
                break;
            }

            var name = den.Storeys[den.StoreyOf(cell)].Name;
            if (storeys.Count == 0 || storeys[^1] != name)
            {
                storeys.Add(name);
            }

            for (var i = 0; i < tour.Schedule.Count; i++)
            {
                var slot = tour.Schedule.Slot(i);
                if (slot.Cell == cell && tour.Schedule.IsHunted(i) && tour.Schedule.IsStanding(i))
                {
                    tour.Killed(slot.Wcid, slot.Cell, step); // everything in the cell dies within a second
                }
            }

            from = cell;
        }

        Assert.Equal(["z 0", "z -6", "z -12", "z -18", "z -24"], storeys); // z -30 holds no hunted slot (g2: one walkable cell, no slot)
        Assert.Equal(0, tour.Schedule.StandingHunted);
        Assert.Equal(0, tour.Returns); // all within 200 s: no slave is due
    }

    [Fact]
    public void The_tour_takes_any_dungeon_profile_the_dens_three_generators_and_tusker_temples_one()
    {
        Assert.Equal(78, new GroundTour(GroundCatalog.Get("black-spawn-den-aluvian"), Metres, NullLogSink.Instance).Schedule.HuntedCount);
        var temple = new GroundTour(GroundCatalog.Get("tusker-temple-fort-tethana"), Metres, NullLogSink.Instance);
        Assert.Equal(4, GroundCatalog.Get("tusker-temple-fort-tethana").Storeys.Count);
        Assert.True(temple.Schedule.HuntedCount > 0);
    }
}
