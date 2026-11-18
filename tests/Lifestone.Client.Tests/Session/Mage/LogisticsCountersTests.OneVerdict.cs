using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 16 E5d (docs/automation-engine.md section 2.5): the session's one set of logistics counters (<see cref="TripDesk"/>) against each of the constructions it replaced, over the states the counters' own tests judge and a
/// set of mixed ones. The seven sites built their counters in two shapes: with the floor-only lines (the outer loop, the fight's floors, the logistics station, the buy-trip) and with none (the town trip, the panel's two).
/// What these prove: the session's set, stated the floor-only lines, gives the first shape's verdict, counts, floors and held-back lines in every state; the second shape's answer is the same in every state but one, a
/// floor-only line under its stop line, which is the named difference; the reading twin gives the same answer and moves nothing of the set that decides; a visit's cut keeps end with its buy list; and none of the asks
/// allocates.
/// </summary>
public sealed partial class LogisticsCountersTests
{
    /// <summary>A state with the name the failure message shows.</summary>
    private sealed record Judged(string Name, GameState State);

    /// <summary>The counters' own test states and a set of mixed ones over <paramref name="ground"/>: each a pack and a purse.</summary>
    private static List<Judged> States(HuntingGround ground)
    {
        var stock = ground.Supplies.Stock;
        var capacity = new Pack(Stocked(ground)).Capacity;
        var over = (uint)((long)capacity * (LoadLine(ground) + 1) / 100);
        var states = new List<Judged>
        {
            new("all at keep, a light load", new Pack(Stocked(ground), burden: 100).State),
            new("the burden over its line", new Pack(Stocked(ground), burden: over).State),
            new("the pack unread", new Pack([], describe: false).State),
            new("an item not described", new Pack(Stocked(ground), burden: 100, extraUndescribed: 2).State),
            new("no burden sent", new Pack(Stocked(ground)).State),
            new("two free slots", new Pack(Filled(ground, 2), burden: 100).State),
        };
        foreach (var line in stock)
        {
            states.Add(new($"{line.Name} one under its trip line", new Pack(With(ground, line.Wcid, Math.Max(0, line.TripUnder - 1)), burden: 100).State));
            states.Add(new($"{line.Name} one under its stop line", new Pack(With(ground, line.Wcid, Math.Max(0, line.StopUnder - 1)), burden: 100).State));
            states.Add(new($"{line.Name} none held", new Pack(With(ground, line.Wcid, 0), burden: 100).State));
        }

        // mixed: every line low at once; every line low and the load over; a poor purse with a line low (a trip the purse cannot pay is no trip, D78); a poor purse and the load over
        List<(uint Wcid, uint Count)> allLow = [.. stock.Select(l => (l.Wcid, (uint)Math.Max(l.StopUnder, l.TripUnder - 1)))];
        states.Add(new("every line one under its trip line", new Pack(allLow, burden: 100).State));
        states.Add(new("every line low and the burden over", new Pack(allLow, burden: over).State));
        foreach (var coin in new[] { 0, 14, 500, 5_000 })
        {
            var poor = new Pack(allLow, burden: 100);
            poor.R.PrivateInt(PlayerQualities.CoinValueProperty, coin);
            states.Add(new($"every line low on a purse of {coin}", poor.State));
            var poorAndHeavy = new Pack(allLow, burden: over);
            poorAndHeavy.R.PrivateInt(PlayerQualities.CoinValueProperty, coin);
            states.Add(new($"every line low, the burden over, a purse of {coin}", poorAndHeavy.State));
        }

        return states;
    }

    private static void SameAnswer(string where, LogisticsCounters expected, LogisticsVerdict want, LogisticsCounters actual, LogisticsVerdict got)
    {
        Assert.True(want == got, $"{where}: {want.Kind} {want.Trigger} ({want.Reason}) expected, {got.Kind} {got.Trigger} ({got.Reason}) given");
        Assert.Equal(expected.FloorMask, actual.FloorMask);
        Assert.Equal(expected.HeldBackMask, actual.HeldBackMask);
        Assert.Equal(expected.FirstCarriedShort(), actual.FirstCarriedShort());
        for (var i = 0; i < expected.LineCount; i++)
        {
            Assert.Equal(expected.HeldOf(i), actual.HeldOf(i));
            Assert.Equal(expected.IsFloorOnly(i), actual.IsFloorOnly(i));
            Assert.Equal(expected.IsSuspended(i), actual.IsSuspended(i));
            Assert.Equal(expected.HeldBackCoin(i), actual.HeldBackCoin(i));
            Assert.Equal(expected.LineAt(i), actual.LineAt(i));
        }
    }

    [Theory]
    [MemberData(nameof(Dungeons))]
    public void M6_16_E5d_the_sessions_one_set_answers_as_each_construction_it_replaced_in_every_state_but_the_floor_only_lines(string key)
    {
        var ground = Ground(key);
        var floorOnly = ComponentFloor.FloorOnly(ground, FloorTable()).Keys.ToList();
        var differed = new List<string>();
        foreach (var judged in States(ground))
        {
            // the outer loop's, the fight's, the logistics station's and the buy-trip's construction: the floor-only lines stated
            var withFloors = new LogisticsCounters(ground, floorOnly);
            var want = withFloors.Check(judged.State);
            var desk = TripDesk.For(judged.State);
            var one = desk.On(ground, floorOnly);
            SameAnswer(judged.Name, withFloors, want, one, one.Check(judged.State));
            Assert.Same(one, desk.On(ground)); // every later asker is given the same set
            Assert.Same(one, desk.On(ground, floorOnly));

            // the reading twin: the same answer, and the set that decides is as its own check left it
            var held = Enumerable.Range(0, one.LineCount).Select(one.HeldOf).ToList();
            var twin = desk.Reading(ground);
            Assert.NotSame(one, twin);
            SameAnswer(judged.Name + " (the reading twin)", withFloors, want, twin, twin.Check(judged.State));
            Assert.Equal(held, Enumerable.Range(0, one.LineCount).Select(one.HeldOf));

            // the town trip's and the panel's construction: no floor-only line. The same verdict in every state but a floor-only line under its stop line.
            var plain = new LogisticsCounters(ground);
            var before = plain.Check(judged.State);
            if (before != want)
            {
                differed.Add(judged.Name);
                Assert.Equal(LogisticsCause.StockStop, before.Cause);
                Assert.Contains(before.Line!.Wcid, floorOnly); // the one named difference: the line is floor-only (D148)
                Assert.NotEqual(LogisticsKind.Stop, want.Kind);
            }
        }

        // the den's Diamond Scarab is floor-only; the fort's profile has no carried line a war option alone burns
        if (key == Den)
        {
            Assert.Contains("Diamond Scarab one under its stop line", differed);
            Assert.All(differed, name => Assert.StartsWith("Diamond Scarab ", name, StringComparison.Ordinal));
        }
        else
        {
            Assert.Empty(differed);
        }
    }

    [Fact]
    public void M6_16_E5d_a_floor_only_line_under_its_stop_line_reads_the_same_at_every_site_once_a_site_states_it()
    {
        // The named difference between the constructions. The den's Diamond Scarab under its stop line: the outer loop's counters (floor-only stated) said all well and put the ring away; the town trip's and the panel's own
        // counters (none stated) said stop:Diamond Scarab for the same pack and purse. D148 is the rule: no stop. The session's set is stated once and every site reads that.
        var ground = Ground(Den);
        var stop = ground.Supplies.StockOf(DiamondScarab)!.StopUnder;
        var state = new Pack(With(ground, DiamondScarab, stop - 1), burden: 100).State;
        Assert.Equal("stop:Diamond Scarab", new LogisticsCounters(ground).Check(state).Trigger); // the town trip's and the panel's own counters, before

        var desk = TripDesk.For(state);
        var job = desk.On(ground, ComponentFloor.FloorOnly(ground, FloorTable()).Keys); // a site that reads the floor-only lines states them
        var townTrip = desk.On(ground); // the town trip states none and reads what stands
        var panel = desk.Reading(ground); // so does the panel

        Assert.Same(job, townTrip);
        Assert.Equal(LogisticsKind.None, townTrip.Check(state).Kind);
        Assert.Equal(LogisticsKind.None, panel.Check(state).Kind);
        Assert.True(panel.AtFloor(IndexOf(panel, DiamondScarab)));
        Assert.Equal([DiamondScarab], desk.FloorOnlyOf(ground));
    }

    [Fact]
    public void M6_16_E5d_the_jobs_floor_only_lines_stand_and_a_statement_that_differs_is_named_never_applied_in_silence()
    {
        var ground = Ground(Den);
        var state = new Pack(Stocked(ground), burden: 100).State;
        var desk = TripDesk.For(state);
        var notes = new List<string>();

        // with no job: a station's statement applies, and a later one that differs is applied and said
        var counters = desk.On(ground, [DiamondScarab], note: notes.Add);
        Assert.Empty(notes);
        Assert.Same(counters, desk.On(ground, [], note: notes.Add));
        Assert.Equal("trip: the floor-only lines of black-spawn-den-aluvian are stated again: none (they stood at 7299)", Assert.Single(notes));
        Assert.False(counters.IsFloorOnly(IndexOf(counters, DiamondScarab)));

        // the job states its own: it takes the session's set (its own gives way) and its lines stand
        notes.Clear();
        var stated = TripDesk.Stated(ground, [DiamondScarab]);
        Assert.Same(counters, desk.Adopt(ground, stated, [DiamondScarab], notes.Add));
        Assert.Equal("trip: the floor-only lines of black-spawn-den-aluvian are the job's now: 7299 (they stood at none, as read before the job stated its own)", Assert.Single(notes));
        Assert.True(counters.IsFloorOnly(IndexOf(counters, DiamondScarab)));
        Assert.True(desk.Reading(ground).IsFloorOnly(IndexOf(counters, DiamondScarab)));

        // a station that reads them differently is told so, and the job's stand
        notes.Clear();
        Assert.Same(counters, desk.On(ground, [], note: notes.Add));
        Assert.Equal("trip: TODO(engine-trip-floor-statements): this site reads the floor-only lines of black-spawn-den-aluvian as none, the job stated 7299: the job's stand", Assert.Single(notes));
        Assert.True(counters.IsFloorOnly(IndexOf(counters, DiamondScarab)));

        // on a ground the session has not counted, the job's own counters become the session's
        var fort = Ground("tusker-temple-fort-tethana");
        var own = TripDesk.Stated(fort, []);
        Assert.Same(own, desk.Adopt(fort, own, []));
        Assert.Same(own, desk.On(fort));
        Assert.NotSame(own, counters); // a set a ground
    }

    [Fact]
    public void M6_16_E5d_a_visits_cut_keeps_end_with_its_buy_list_and_the_fight_reads_the_profiles_keeps_again()
    {
        var ground = Ground("black-spawn-den-aluvian");
        var state = new Pack(Stocked(ground), burden: 100).State;
        var counters = TripDesk.For(state).On(ground);
        counters.Check(state);
        var index = Enumerable.Range(0, counters.LineCount).First(i => counters.LineAt(i).Keep > Math.Max(counters.LineAt(i).TripUnder, counters.LineAt(i).StopUnder) + 1);
        var profile = counters.LineAt(index);
        var clear = LogisticsCounters.ClearOf(profile);

        Assert.True(counters.CutKeep(index, Math.Max(profile.TripUnder, profile.StopUnder) + 1)); // the town trip's burden budget
        Assert.True(counters.LineAt(index).Keep < profile.Keep);
        Assert.True(LogisticsCounters.ClearOf(counters.LineAt(index)) < clear); // what the trigger's purse test would price, were the cut left standing

        counters.UncutKeeps(); // the visit's buy list is made
        Assert.Equal(profile, counters.LineAt(index));
        Assert.Equal(clear, LogisticsCounters.ClearOf(counters.LineAt(index)));
        counters.UncutKeeps(); // nothing left to restore
        Assert.Equal(profile, counters.LineAt(index));
        Assert.False(counters.CutKeep(index, profile.Keep)); // a keep is never raised by a cut
    }

    [Fact]
    public void M6_16_E5d_the_desks_asks_and_the_reading_twins_check_allocate_nothing()
    {
        var ground = Ground("black-spawn-den-aluvian");
        var stock = ground.Supplies.Stock;
        var state = new Pack(With(ground, stock[0].Wcid, stock[0].TripUnder - 1)).State;
        var desk = TripDesk.For(state);
        desk.On(ground, ComponentFloor.FloorOnly(ground, FloorTable()).Keys).Check(state);
        desk.Reading(ground).Check(state); // warms the twin

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            TripDesk.For(state).On(ground).Check(state);
            TripDesk.For(state).Reading(ground).Check(state);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
