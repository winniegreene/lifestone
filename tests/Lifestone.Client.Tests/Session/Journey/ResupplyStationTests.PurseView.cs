using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Quartermaster;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 16, slices E5a1 and E5a2: the town trip's purse and the learn holds on the quartermaster's read (<see cref="PurseView"/>). The trip plans with everything held, a note inside a side pack too, and since E5a2 its
/// buy changes that note where it lies; a learn hold is released by what the scrivener's counter can pay with, the face of a note too (E5a1 pinned both the other way, as they stood).
/// </summary>
public sealed partial class ResupplyStationTests
{
    [Fact]
    public void M6_16_E5a2_the_trips_floor_purse_counts_a_note_inside_a_side_pack_and_the_counter_reads_it_too()
    {
        var home = new Home(Den, 5_000, notes: false);
        var state = home.Vendor.State;
        var pack = state.Self.Inventory.SidePacks[0].Guid;
        var away = home.Vendor.NextGuid();
        home.Vendor.Arrive(away, "Trade Note (100,000)", 2627, VendorStock.PromissoryNoteType, 100_000);
        home.Vendor.Put(away, pack);
        var run = home.Start();
        home.Stations.Run(run, frames: 1); // one step binds the run to the session; no server answers, so nothing it sent has moved an item

        Assert.Equal(InventoryPlaceKind.InSidePack, state.Self.Inventory.Find(away).Kind);
        Assert.Equal(105_000, run.FloorPurse()); // as before the move: the coin and every note held
        Assert.Equal(PurseViewTests.Before.ResupplyFloorPurse(state), run.FloorPurse());

        // M6 16 E5a2: the trip's buy (the vendor counter) reads the note inside the side pack too, so what the trip plans with is what its counter can pay with.
        Assert.Equal(100_000, PurseView.NotesCounterCanChange(state));
        var read = Assert.Single(PurseNotes.Read(state, home.Ground.Supplies.Purse.Notes).Notes);
        Assert.Equal((away, pack, 1), (read.Guid, read.Container, read.Stack));
        Assert.Equal(105_000, PurseView.Read(state).CounterCanPay);
    }

    [Fact]
    public void M6_16_E5a2_a_learn_hold_is_released_by_the_face_of_a_note_the_counter_can_change()
    {
        var home = OutskirtsHome(warRanks: 56, coins: 2_000, notes: false); // Flame Bolt II is due; its scroll is 250 pyreals
        var holds = new LearnHolds();
        var counters = new LogisticsCounters(home.Ground, learnHolds: holds);
        Assert.Equal("learn:Flame Bolt II", counters.Check(home.Vendor.State).Trigger);
        home.Vendor.Coins(100);
        home.Vendor.Tick();
        Assert.True(holds.Hold(FlameBoltII, 250, 6)); // the trip bought no scroll: the purse was short

        home.Own("Trade Note (10,000)", 2625, VendorStock.PromissoryNoteType, 10_000); // the purse (coin and notes) now pays the scroll many times over
        home.Vendor.Tick();

        Assert.Equal(100, PurseView.CoinUnreadAsZero(home.Vendor.State));
        Assert.Equal(10_100, PurseView.CounterCanPayUnreadAsZero(home.Vendor.State));
        Assert.Equal("learn:Flame Bolt II", counters.Check(home.Vendor.State).Trigger); // M6 16 E5a2: the hold reads what the scrivener's counter can pay with, 10,100, not the coin alone (100 is under 250): released (before the slice it stood)
        Assert.Equal(0, holds.Count);
    }

    [Fact]
    public void M6_16_E5a2_a_learn_hold_is_released_by_a_note_inside_a_side_pack_too()
    {
        var home = OutskirtsHome(warRanks: 56, coins: 2_000, notes: false);
        var holds = new LearnHolds();
        var counters = new LogisticsCounters(home.Ground, learnHolds: holds);
        Assert.Equal("learn:Flame Bolt II", counters.Check(home.Vendor.State).Trigger);
        home.Vendor.Coins(100);
        home.Vendor.Tick();
        Assert.True(holds.Hold(FlameBoltII, 250, 6));
        Assert.NotEqual(LogisticsCause.Learn, counters.Check(home.Vendor.State).Cause); // held: 100 does not pay 250

        var pack = home.Vendor.State.Self.Inventory.SidePacks[0].Guid;
        var away = home.Vendor.NextGuid();
        home.Vendor.Arrive(away, "Trade Note (10,000)", 2625, VendorStock.PromissoryNoteType, 10_000);
        home.Vendor.Put(away, pack);
        home.Vendor.Tick();
        Assert.Equal(InventoryPlaceKind.InSidePack, home.Vendor.State.Self.Inventory.Find(away).Kind);

        Assert.Equal("learn:Flame Bolt II", counters.Check(home.Vendor.State).Trigger); // the counter changes the note where it lies, so the hold is released by it
        Assert.Equal(0, holds.Count);
    }

    [Fact]
    public void M6_16_E5a2_a_trip_that_bought_no_scroll_beside_a_note_that_covers_it_is_not_held_for_the_purse()
    {
        // 100 coin and a 10,000 note: the scroll is 250. The trip is called (the batch reads the coin and the notes) and buys nothing (no shop answers). Before the slice the hold's cause was read from the coin alone:
        // "purse short", released when the coin reached 250. The counter would have changed the note, so the purse was not the reason: the hold waits for the level, and says what it counted.
        var home = OutskirtsHome(warRanks: 56, coins: 100, notes: false);
        home.Own("Trade Note (10,000)", 2625, VendorStock.PromissoryNoteType, 10_000);
        home.Vendor.Tick();
        var holds = new LearnHolds();
        var counters = new LogisticsCounters(home.Ground, learnHolds: holds);
        Assert.Equal("learn:Flame Bolt II", counters.Check(home.Vendor.State).Trigger);

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

        Assert.Equal(1, holds.Count);
        Assert.Single(home.Lines, l => l.Contains("logistics: learn hold: the trip bought no scroll of Flame Bolt II (81; its price 250, the coin 100, level 6; cause: ", StringComparison.Ordinal) && !l.Contains("cause: purse short", StringComparison.Ordinal) && l.Contains("waits until the level rises", StringComparison.Ordinal));
        Assert.Single(home.Lines, l => l.Contains("logistics: learn hold: the trade notes held (10000) count with the coin: the hold is judged on what the scrivener's counter can pay with, 10100", StringComparison.Ordinal));
        home.Vendor.Coins(250); // the coin reaching the price does not release it: the purse was not the reason
        home.Vendor.Tick();
        Assert.NotEqual(LogisticsCause.Learn, counters.Check(home.Vendor.State).Cause);
        Assert.Equal(1, holds.Count);
    }
}
