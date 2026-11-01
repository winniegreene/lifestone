using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// <see cref="LogisticsCounters"/> over the supplies of both dungeon profiles (M5 item 3 m4, D46, D48): each stock line's trip and stop, a carried line's stop alone (the den's scarabs; Tusker Temple's are sold by its mage and
/// have a trip), the burden line, none when all is well, the unread states, a ground without stock, and no allocation per check. Every threshold is read from the profile, none is written here: the tests move a line to
/// one under its line and read the verdict.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed partial class LogisticsCountersTests
{
    private const uint Strength = 1;
    private const uint FirstItem = 0x80004000;

    public static TheoryData<string> Dungeons => new() { "black-spawn-den-aluvian", "tusker-temple-fort-tethana" };

    /// <summary>A character holding stacks of wares in the main pack (their descriptions created, their stacks sent) with a Strength of 100.</summary>
    private sealed class Pack
    {
        private readonly List<(uint Guid, uint Wcid, uint Count)> _held = [];
        private byte _seq;

        public Pack(IEnumerable<(uint Wcid, uint Count)> stacks, uint burden = 0, bool describe = true, uint extraUndescribed = 0)
        {
            R = new WorldActionRig();
            var guid = FirstItem;
            var pack = new List<ContentDesc>();
            foreach (var (wcid, count) in stacks)
            {
                if (count == 0)
                {
                    continue;
                }

                _held.Add((guid, wcid, count));
                var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: "Ware " + wcid);
                R.H.R.Reducer.OnCreateObject(
                    R.H.R.Context,
                    basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player, WeenieClassId = wcid, ItemType = 0x80 } },
                    isUpdate: false);
                pack.Add(new ContentDesc(guid, 0));
                guid++;
            }

            for (var i = 0; i < extraUndescribed; i++)
            {
                pack.Add(new ContentDesc(guid++, 0)); // listed by the description, its CreateObject not arrived
            }

            if (describe)
            {
                R.H.Describe(new PlayerDescriptionMsg
                {
                    Attributes = [new AttributeDesc(Strength, 0, 100, 0)],
                    Inventory = [.. pack],
                });
            }

            foreach (var (g, _, count) in _held)
            {
                R.Raw(AceM3Writers.SetStackSize(++_seq, g, count, count * 10));
            }

            if (burden > 0)
            {
                R.PrivateInt(PlayerInventory.EncumbranceProperty, (int)burden);
            }
        }

        public WorldActionRig R { get; }

        public GameState State => R.H.State;

        public int Capacity => LootContext.Of(State).Capacity;
    }

    private static HuntingGround Ground(string key) => GroundCatalog.Get(key);

    /// <summary>The burden line the counters use: the loot section's load line (M5 7 F29, D96) when the profile names one, else the supplies' <c>burden_trip_percent</c>.</summary>
    private static int LoadLine(HuntingGround ground) => ground.Loot is { LoadLinePercent: > 0 } loot ? loot.LoadLinePercent : ground.Supplies.BurdenTripPercent;

    private static List<(uint Wcid, uint Count)> Filled(HuntingGround ground, int freeSlots)
    {
        var stacks = Stocked(ground);
        var extras = 102 - freeSlots - stacks.Count; // the main pack holds 102 items; each extra is one more slot taken
        for (var i = 0; i < extras; i++)
        {
            stacks.Add((900_000u + (uint)i, 1u));
        }

        return stacks;
    }

    /// <summary>The stacks that leave every line at its keep count.</summary>
    private static List<(uint Wcid, uint Count)> Stocked(HuntingGround ground) => [.. ground.Supplies.Stock.Select(l => (l.Wcid, (uint)l.Keep))];

    /// <summary>The stocked stacks with the line of <paramref name="wcid"/> at <paramref name="count"/>.</summary>
    private static List<(uint Wcid, uint Count)> With(HuntingGround ground, uint wcid, int count) =>
        [.. ground.Supplies.Stock.Select(l => (l.Wcid, (uint)(l.Wcid == wcid ? count : l.Keep)))];

    [Theory]
    [MemberData(nameof(Dungeons))]
    public void Everything_at_its_keep_count_and_a_light_load_is_none(string key)
    {
        var ground = Ground(key);
        var counters = new LogisticsCounters(ground);
        var pack = new Pack(Stocked(ground), burden: 100);

        var verdict = counters.Check(pack.State);

        Assert.True(counters.Active);
        Assert.Equal(LogisticsKind.None, verdict.Kind);
        Assert.Equal(LogisticsCause.None, verdict.Cause);
        Assert.Equal("all well", verdict.Reason);
        Assert.Equal("none", verdict.Trigger);
        for (var i = 0; i < counters.LineCount; i++)
        {
            Assert.Equal(counters.LineAt(i).Keep, counters.HeldOf(i));
        }
    }

    [Theory]
    [MemberData(nameof(Dungeons))]
    public void A_restockable_line_one_under_its_trip_line_is_a_trip_named_by_its_line_its_count_and_its_threshold(string key)
    {
        var ground = Ground(key);
        var counters = new LogisticsCounters(ground);
        var checkedLines = 0;
        for (var i = 0; i < counters.LineCount; i++)
        {
            var line = counters.LineAt(i);
            if (counters.IsCarried(i) || line.TripUnder <= line.StopUnder || line.TripUnder == 0)
            {
                continue; // a carried line has no trip; a line whose trip line is its stop line is a stop (below)
            }

            checkedLines++;
            var held = line.TripUnder - 1;
            var verdict = counters.Check(new Pack(With(ground, line.Wcid, held)).State);

            Assert.Equal(LogisticsKind.Trip, verdict.Kind);
            Assert.Equal(LogisticsCause.StockLow, verdict.Cause);
            Assert.Equal(line, verdict.Line);
            Assert.Equal((held, line.TripUnder), (verdict.Held, verdict.Threshold));
            Assert.Equal($"{line.Name}: {held} held, under its trip line {line.TripUnder}", verdict.Reason);
            Assert.Equal("stock:" + line.Name, verdict.Trigger);

            // At the line itself it is not under it.
            Assert.Equal(LogisticsKind.None, counters.Check(new Pack(With(ground, line.Wcid, line.TripUnder)).State).Kind);
        }

        Assert.True(checkedLines >= 2, "each dungeon restocks at least its tapers and its potions");
    }

    [Theory]
    [MemberData(nameof(Dungeons))]
    public void A_line_one_under_its_stop_line_is_a_stop_that_outranks_a_trip(string key)
    {
        var ground = Ground(key);
        var counters = new LogisticsCounters(ground);
        var checkedLines = 0;
        for (var i = 0; i < counters.LineCount; i++)
        {
            var line = counters.LineAt(i);
            if (line.StopUnder == 0)
            {
                continue; // a line with no stop line never stops (the Mana Potions)
            }

            checkedLines++;
            var held = line.StopUnder - 1;
            var verdict = counters.Check(new Pack(With(ground, line.Wcid, held)).State);

            Assert.Equal(LogisticsKind.Stop, verdict.Kind);
            Assert.Equal(LogisticsCause.StockStop, verdict.Cause);
            Assert.Equal(line, verdict.Line);
            Assert.Equal((held, line.StopUnder), (verdict.Held, verdict.Threshold));
            Assert.Equal(counters.IsCarried(i), verdict.Carried);
            Assert.StartsWith($"{line.Name}: {held} held, under its stop line {line.StopUnder}", verdict.Reason, StringComparison.Ordinal);
            Assert.Equal("stop:" + line.Name, verdict.Trigger);
            Assert.False(verdict.IsTrip);
            Assert.True(verdict.IsStop);
        }

        Assert.True(checkedLines >= 3, "tapers and both scarabs have stop lines");
    }

    [Theory]
    [MemberData(nameof(Dungeons))]
    public void A_carried_line_has_a_stop_and_no_trip_while_a_sold_one_has_a_trip_at_the_same_count(string key)
    {
        var ground = Ground(key);
        var counters = new LogisticsCounters(ground);
        var carried = 0;
        var sold = 0;
        for (var i = 0; i < counters.LineCount; i++)
        {
            var line = counters.LineAt(i);
            if (line.StopUnder == 0)
            {
                continue;
            }

            // The stop line's own count is not under it; under the trip line it is a trip for a line a vendor sells and nothing for a carried one.
            var verdict = counters.Check(new Pack(With(ground, line.Wcid, line.StopUnder)).State);
            var tripWanted = !counters.IsCarried(i) && line.TripUnder > line.StopUnder;
            Assert.Equal(tripWanted ? LogisticsKind.Trip : LogisticsKind.None, verdict.Kind);
            if (counters.IsCarried(i))
            {
                carried++;
                Assert.Null(ground.VendorSelling(line.Wcid));
                Assert.Equal(LogisticsKind.None, counters.Check(new Pack(With(ground, line.Wcid, line.TripUnder)).State).Kind);
            }
            else
            {
                sold++;
            }
        }

        Assert.True(carried + sold >= 3);
        if (key == "black-spawn-den-aluvian")
        {
            Assert.Equal(4, carried); // M5 7 D148: Cindrue sells the Silver Scarab (688, 338), so it is a sold line now; Holtburg sells none of these scarabs (D8): the Pyreal, the Platinum, (M5 7 F10, the ring's) the Diamond and (M5 7 F25, the buff levels IV and V) the Silver and the Gold are the carried lines
        }
        else
        {
            Assert.Equal(0, carried); // Zisaha sells both
        }
    }

    [Theory]
    [MemberData(nameof(Dungeons))]
    public void The_load_passing_the_supplies_burden_line_is_a_trip_and_the_line_itself_is_not(string key)
    {
        var ground = Ground(key);
        var counters = new LogisticsCounters(ground);
        var capacity = new Pack(Stocked(ground)).Capacity;
        Assert.True(capacity > 0);
        var line = LoadLine(ground);
        var atLine = (uint)((long)capacity * line / 100);
        Assert.Equal(0, (long)capacity * line % 100); // the test's pack: a Strength of 100 gives a whole-number line

        Assert.Equal(LogisticsKind.None, counters.Check(new Pack(Stocked(ground), burden: atLine).State).Kind);

        var over = counters.Check(new Pack(Stocked(ground), burden: atLine + (uint)(capacity / 100)).State); // one percent over
        Assert.Equal(LogisticsKind.Trip, over.Kind);
        Assert.Equal(LogisticsCause.Burden, over.Cause);
        Assert.Equal((line + 1, line), (over.BurdenPercent, over.LinePercent));
        Assert.Equal($"burden {line + 1}% over the line {line}%", over.Reason);
        Assert.Equal("burden", over.Trigger);
        Assert.Equal((int)(atLine + (uint)(capacity / 100)), counters.Burden);
        Assert.Equal(capacity, counters.Capacity);

        // Just over the line, by one unit of burden, the share rounds up so the reason never reads "90% over 90%".
        var justOver = counters.Check(new Pack(Stocked(ground), burden: atLine + 1).State);
        Assert.Equal(LogisticsKind.Trip, justOver.Kind);
        Assert.Equal(line + 1, justOver.BurdenPercent);
    }

    [Fact]
    public void A_stop_outranks_a_stock_trip_and_a_stock_trip_outranks_the_burden_trip_and_the_first_line_is_named()
    {
        var ground = Ground("black-spawn-den-aluvian");
        var counters = new LogisticsCounters(ground);
        var stock = ground.Supplies.Stock;
        var taper = stock[0];
        var potion = stock.First(l => ground.VendorSelling(l.Wcid) is not null && l.Wcid != taper.Wcid);
        var scarab = stock.First(l => ground.IsCarried(l.Wcid) && l.StopUnder > 0);
        var capacity = new Pack(Stocked(ground)).Capacity;
        var overLoad = (uint)((long)capacity * (LoadLine(ground) + 1) / 100);

        // A potion trip and a burden trip: the stock line is named.
        var tripAndBurden = counters.Check(new Pack(With(ground, potion.Wcid, potion.TripUnder - 1), burden: overLoad).State);
        Assert.Equal(LogisticsCause.StockLow, tripAndBurden.Cause);
        Assert.Equal(potion, tripAndBurden.Line);

        // A scarab stop beside a taper trip and a burden trip: the stop is named.
        var counts = Stocked(ground);
        counts[stock.ToList().IndexOf(taper)] = (taper.Wcid, (uint)(taper.TripUnder - 1));
        counts[stock.ToList().IndexOf(scarab)] = (scarab.Wcid, (uint)(scarab.StopUnder - 1));
        var all = counters.Check(new Pack(counts, burden: overLoad).State);
        Assert.Equal(LogisticsKind.Stop, all.Kind);
        Assert.Equal(scarab, all.Line);
    }

    [Fact]
    public void Several_stacks_of_one_ware_are_summed_and_a_stack_size_the_server_sent_beats_the_description()
    {
        var ground = Ground("black-spawn-den-aluvian");
        var counters = new LogisticsCounters(ground);
        var taper = ground.Supplies.Stock.First(l => l.TripUnder > 1);
        var half = (uint)(taper.TripUnder / 2);
        var stocks = ground.Supplies.Stock.Where(l => l.Wcid != taper.Wcid).Select(l => (l.Wcid, (uint)l.Keep)).ToList();

        // Two stacks that together sit one under the trip line.
        stocks.Add((taper.Wcid, half));
        stocks.Add((taper.Wcid, (uint)taper.TripUnder - 1 - half));
        var verdict = counters.Check(new Pack(stocks).State);
        Assert.Equal(LogisticsKind.Trip, verdict.Kind);
        Assert.Equal(taper.TripUnder - 1, verdict.Held);

        // The same two stacks plus one more unit: not under.
        stocks[^1] = (taper.Wcid, (uint)taper.TripUnder - half);
        Assert.Equal(LogisticsKind.None, counters.Check(new Pack(stocks).State).Kind);
    }

    [Fact]
    public void The_counts_are_unread_until_the_pack_and_its_items_are_known_and_nothing_is_judged_then()
    {
        var ground = Ground("black-spawn-den-aluvian");
        var counters = new LogisticsCounters(ground);

        var undescribed = new Pack([], describe: false);
        var before = counters.Check(undescribed.State);
        Assert.Equal(LogisticsKind.Unread, before.Kind);
        Assert.Equal(LogisticsCause.PackUnread, before.Cause);
        Assert.False(before.IsStop);
        Assert.Equal("unread", before.Trigger);

        var settling = counters.Check(new Pack(Stocked(ground), extraUndescribed: 2).State);
        Assert.Equal(LogisticsKind.Unread, settling.Kind);
        Assert.Equal(LogisticsCause.ItemsUnsettled, settling.Cause);
        Assert.Equal(2, settling.Held);
        Assert.Equal("2 held items have no description yet", settling.Reason);

        // Described and empty: every line is at 0, which is under the stop lines (a real stop, not a guess).
        var empty = counters.Check(new Pack([]).State);
        Assert.Equal(LogisticsKind.Stop, empty.Kind);
    }

    [Fact]
    public void A_ground_without_stock_lines_judges_nothing()
    {
        var outdoor = GroundCatalog.All.First(g => g.Supplies.Stock.Count == 0);
        var counters = new LogisticsCounters(outdoor);

        Assert.False(counters.Active);
        Assert.Equal(0, counters.LineCount);
        Assert.Equal(LogisticsKind.None, counters.Check(new Pack([], burden: 1_000_000).State).Kind); // a heavy load calls no trip where nothing can be bought
        Assert.Equal(LogisticsKind.None, counters.Check(new Pack([], describe: false).State).Kind);
    }

    [Fact]
    public void A_check_allocates_nothing_in_any_verdict()
    {
        var ground = Ground("black-spawn-den-aluvian");
        var counters = new LogisticsCounters(ground);
        var stock = ground.Supplies.Stock;
        var capacity = new Pack(Stocked(ground)).Capacity;
        var states = new GameState[]
        {
            new Pack(Stocked(ground), burden: 100).State, // none
            new Pack(With(ground, stock[0].Wcid, stock[0].TripUnder - 1)).State, // a trip
            new Pack(With(ground, stock[0].Wcid, stock[0].StopUnder - 1)).State, // a stop
            new Pack(Stocked(ground), burden: (uint)((long)capacity * (LoadLine(ground) + 1) / 100)).State, // the burden
            new Pack([], describe: false).State, // unread
        };
        var kinds = new[] { LogisticsKind.None, LogisticsKind.Trip, LogisticsKind.Stop, LogisticsKind.Trip, LogisticsKind.Unread };
        for (var s = 0; s < states.Length; s++)
        {
            Assert.Equal(kinds[s], counters.Check(states[s]).Kind); // warms every path
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            for (var s = 0; s < states.Length; s++)
            {
                counters.Check(states[s]);
            }
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void The_den_profile_says_a_trip_every_two_buff_cycles_an_early_one_under_five_free_slots_and_the_load_line_at_85_percent_M5_7_F29()
    {
        var den = Ground("black-spawn-den-aluvian");
        Assert.Equal(2, new LogisticsCounters(den).TripEveryBuffCycles);
        Assert.Equal(5, new LogisticsCounters(den).MinFreeSlots);
        Assert.Equal(85, new LogisticsCounters(den).BurdenTripPercent); // the loot section's line, not the supplies' 90
        Assert.Equal(90, den.Supplies.BurdenTripPercent);
        var belt = new LogisticsCounters(Ground("tusker-temple-fort-tethana"));
        Assert.Equal((0, 0), (belt.TripEveryBuffCycles, belt.MinFreeSlots)); // a ground without the values keeps today's rules
    }

    [Fact]
    public void The_trip_comes_after_the_second_buff_cycle_since_the_last_trip_not_before_and_the_schedule_restarts_at_a_trip_M5_7_F29()
    {
        var den = Ground("black-spawn-den-aluvian");
        var counters = new LogisticsCounters(den);
        var pack = new Pack(Stocked(den), burden: 100);

        Assert.Equal(LogisticsKind.None, counters.Check(pack.State, buffCycles: 0).Kind);
        Assert.Equal(LogisticsKind.None, counters.Check(pack.State, buffCycles: 1).Kind); // one cycle: not yet
        var due = counters.Check(pack.State, buffCycles: 2);
        Assert.Equal((LogisticsKind.Trip, LogisticsCause.Cadence), (due.Kind, due.Cause));
        Assert.Equal("cadence", due.Trigger);
        Assert.Equal("2 buff cycles since the last trip, the schedule is 2", due.Reason);

        counters.MarkTrip(2); // the trip ended with two cycles counted
        Assert.Equal(LogisticsKind.None, counters.Check(pack.State, buffCycles: 3).Kind);
        Assert.Equal(LogisticsCause.Cadence, counters.Check(pack.State, buffCycles: 4).Cause);
        Assert.Equal(LogisticsKind.None, counters.Check(pack.State).Kind); // a caller that keeps no count never sees the schedule
    }

    [Fact]
    public void Four_free_slots_are_an_early_trip_and_five_are_not_M5_7_F29()
    {
        var den = Ground("black-spawn-den-aluvian");
        var counters = new LogisticsCounters(den);

        Assert.Equal(LogisticsKind.None, counters.Check(new Pack(Filled(den, 5), burden: 100).State).Kind);
        Assert.Equal(5, counters.FreeSlots);

        var verdict = counters.Check(new Pack(Filled(den, 4), burden: 100).State);
        Assert.Equal((LogisticsKind.Trip, LogisticsCause.Slots), (verdict.Kind, verdict.Cause));
        Assert.Equal((4, 5), (verdict.Held, verdict.Threshold));
        Assert.Equal("4 free slot(s), under the floor 5", verdict.Reason);
        Assert.Equal("slots", verdict.Trigger);
    }

    [Fact]
    public void Over_85_percent_of_the_capacity_is_an_early_trip_and_85_is_not_M5_7_F29()
    {
        var den = Ground("black-spawn-den-aluvian");
        var counters = new LogisticsCounters(den);
        var capacity = new Pack(Stocked(den)).Capacity;
        var atLine = (uint)((long)capacity * 85 / 100);

        Assert.Equal(LogisticsKind.None, counters.Check(new Pack(Stocked(den), burden: atLine).State).Kind);
        var over = counters.Check(new Pack(Stocked(den), burden: atLine + (uint)(capacity / 100)).State);
        Assert.Equal((LogisticsKind.Trip, LogisticsCause.Burden, 85), (over.Kind, over.Cause, over.LinePercent));
    }
}
