using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 7 AR: a ground's keep-out, any ground's data. Proved: the Burrow's south hall is out of the hunt under level 30: its nine slots, every tusk holder among them, leave the schedule and the tour, the other 33 stay; the pull's holders are the 5-pack's; the cells are not no-go, so a walk may cross them; a keep-out of a cell on no storey is refused.
/// </summary>
public sealed class GroundTourKeepOutTests
{
    private static HuntingGround Burrow => GroundCatalog.Get("tusker-burrow");

    private static GroundTour Tour(HuntingGround ground) => new(ground, static (a, b) => a == b ? 0 : 10, NullLogSink.Instance);

    [Fact]
    public void The_south_hall_is_data_and_holds_the_nine_slots_of_the_packs_and_every_holder()
    {
        var hunt = Burrow.Hunt!;
        var kept = Burrow.Respawn.Slots.Where(s => hunt.KeepOut.Contains(s.Cell)).ToList();

        Assert.Equal(9, kept.Count);
        Assert.Equal(5, kept.Count(s => s.Wcid == 22589));
        Assert.DoesNotContain(Burrow.Respawn.Slots.Except(kept), s => s.Wcid == 22589);
        Assert.Equal(30, hunt.UnderLevel);
        Assert.True(hunt.HoldsAt(16));
        Assert.False(hunt.HoldsAt(30));
        Assert.All(hunt.KeepOut.Cells, c => Assert.False(Burrow.IsNoGo(c))); // the way to the exit crosses them
    }

    [Fact]
    public void The_pull_is_the_data_choice_of_the_5_pack_holders_never_the_9_pack()
    {
        var pull = Assert.Single(Burrow.Hunt!.Pulls);

        Assert.Equal(22423u, pull.Item);
        Assert.Equal([0x57480106u, 0x57480113u], pull.Cells);
        Assert.All(pull.Cells, c => Assert.True(Burrow.Hunt.KeepOut.Contains(c)));
        Assert.DoesNotContain(0x5748010Du, pull.Cells); // slot 9's cell: its pack is nine
    }

    [Fact]
    public void The_tour_never_offers_a_kept_out_cell_and_gives_it_back()
    {
        var tour = Tour(Burrow);
        Assert.Equal(42, tour.Schedule.HuntedCount);

        tour.KeepOut(Burrow.Hunt!.KeepOut);

        Assert.Equal(33, tour.Schedule.HuntedCount);
        var from = Burrow.Arrival.Cell;
        for (var i = 0; i < 80; i++)
        {
            var next = tour.Next(from, i * 10.0)!.Value;
            Assert.False(Burrow.Hunt.KeepOut.Contains(next), $"offered 0x{next:X8}");
            tour.LegArrived(next, i * 10.0);
            from = next;
        }

        tour.KeepOut(CellSet.Empty);
        Assert.Equal(42, tour.Schedule.HuntedCount);
    }

    [Fact]
    public void A_ground_without_the_section_keeps_nothing_out() => Assert.Null(GroundCatalog.Get("tusker-cave").Hunt);
}
