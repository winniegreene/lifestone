using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Quartermaster;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 16 E5d (docs/automation-engine.md section 2.5): the session's trip desk (<see cref="TripDesk"/>) at the home of the learn tests, against a scripted ACE: the reading twin releases no learn hold; the verdict a trip
/// carries, counted again at home by the town trip and named where it differs.
/// </summary>
public sealed partial class ResupplyStationTests
{
    [Fact]
    public void M6_16_E5d_a_read_through_the_twin_releases_no_learn_hold_and_the_set_that_decides_does()
    {
        // 9,000 coin and a purse hold on Flame Bolt II at its price of 250: the purse has outgrown the hold. A check releases an outgrown hold for good; until this slice the panel's read and the audit's went through
        // the outer loop's own counters, so a read released it. The twin asks and releases nothing.
        var home = OutskirtsHome(warRanks: 56, coins: 9000, notes: false);
        var state = home.Vendor.State;
        var holds = new LearnHolds();
        var desk = TripDesk.For(state);
        var deciding = desk.On(home.Ground, holds: holds);
        Assert.True(holds.Hold(FlameBoltII, 250, 6));

        var twin = desk.Reading(home.Ground);
        Assert.True(twin.ReadOnly);
        Assert.Equal("learn:Flame Bolt II", twin.Check(state).Trigger); // the hold no longer stands at this purse: the learn is due
        Assert.Equal(1, holds.Count); // and the read released nothing
        Assert.Equal("learn:Flame Bolt II", twin.Check(state).Trigger);
        Assert.Equal(1, holds.Count);

        Assert.False(deciding.ReadOnly);
        Assert.Equal("learn:Flame Bolt II", deciding.Check(state).Trigger); // the same answer from the set that decides
        Assert.Equal(0, holds.Count); // which releases the outgrown hold, as before
    }

    [Fact]
    public void M6_16_E5d_the_logistics_station_reads_the_order_lines_the_trigger_reads()
    {
        // The named change. A job adds a bolt line to the learn plan (a quest's hunt needs it: the outer loop sets the order lines on the counters its trigger checks). Before, the station built counters of its own
        // without them: for a trip the trigger called for the job's scroll it counted the rung's own next scroll (Flame Bolt II), and when the trip bought nothing it held that one, so the trigger called the job's line
        // again at once. Now the station reads the session's counters: the same learn, and the hold on the scroll the trip was called for.
        var home = OutskirtsHome(warRanks: 56, coins: 9000, notes: false);
        var state = home.Vendor.State;
        var holds = new LearnHolds();
        string TriggerWith(IReadOnlyList<string> lines) => new LogisticsCounters(home.Ground, learnHolds: new LearnHolds()) { OrderLines = lines }.Check(state).Trigger;
        Assert.Equal("learn:Flame Bolt II", TriggerWith([])); // the rung's own plan: what the station's own counters read before
        var job = ScrivenerCatalog.Default.Lines.First(l => l.Kind == ScrivenerLineKind.Bolt && TriggerWith([l.Name]) is var t && t.StartsWith("learn:" + l.Name, StringComparison.Ordinal) && t != "learn:Flame Bolt II");

        var desk = TripDesk.For(state);
        var counters = desk.On(home.Ground, holds: holds);
        counters.OrderLines = [job.Name]; // as the outer loop states it
        var called = counters.Check(state);
        var spell = called.Learn!.Spell!;
        Assert.Equal(job.Name, spell.Line);
        Assert.NotEqual(FlameBoltII, spell.SpellId);

        var context = new JourneyContext
        {
            Character = "BSD Mage",
            Account = "test",
            Log = home.Vendor.H.Log,
            Travel = home.Travel,
            Spells = new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>(),
            Dungeon = home.Ground,
        };
        var run = new LogisticsStation(resupply: new ResupplyOptions(Memory: new SaleMemory()), learnHolds: holds).Start(context);
        home.Stations.Run(run, 600, pump: () => { }); // no shop answers: no scroll is bought

        Assert.Single(home.Lines, l => l.Contains($"logistics: the counters say Trip: learn {spell.Name}: ", StringComparison.Ordinal)); // the learn the trigger called the trip for
        Assert.DoesNotContain(home.Lines, l => l.Contains("logistics: the counters say Trip: learn Flame Bolt II", StringComparison.Ordinal));
        Assert.Single(home.Lines, l => l.Contains($"logistics: learn hold: the trip bought no scroll of {spell.Name} (", StringComparison.Ordinal)); // and its trigger is the one held
        Assert.True(holds.Standing(spell.SpellId, 9000, 6));
        Assert.False(holds.Standing(FlameBoltII, 9000, 6));
        Assert.NotEqual(called.Trigger, counters.Check(state).Trigger); // so the same trip is not called again at once
        Assert.Equal([job.Name], counters.OrderLines); // the job's lines stand on the session's counters through the station's run
    }
}
