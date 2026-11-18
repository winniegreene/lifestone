using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 16, slice E5a1: the trip trigger's purse on the quartermaster's read (<see cref="PurseView"/>), its answer unchanged. The trigger counts the coin and every trade note held, a note inside a side pack too
/// (<see cref="PurseView.NotesHeld"/>); a buy at a counter changes a note of the main pack only (<see cref="PurseView.NotesCounterCanChange"/>). These pin the first by name on the state where the two differ.
/// </summary>
public sealed partial class LogisticsCountersTests
{
    private const uint Note100k = 2627;

    /// <summary>A side pack (ContainerType 1) put in the main pack of <paramref name="pack"/>, and one Trade Note (100,000) put inside it or, with <paramref name="inside"/> false, in the main pack.</summary>
    private static void AddNote(Pack pack, bool inside)
    {
        const uint side = 0x80009001, note = 0x80009002;
        var bag = ReducerHarness.Create(side, ReducerHarness.Stamps(), null, name: "Pack", itemType: 0x200);
        pack.R.H.R.Reducer.OnCreateObject(pack.R.H.R.Context, bag with { Weenie = bag.Weenie with { Header = WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player, WeenieClassId = 136 } }, isUpdate: false);
        pack.R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, side, WorldActionRig.Player, 0, 1));
        var held = ReducerHarness.Create(note, ReducerHarness.Stamps(), null, name: "Trade Note (100,000)", itemType: VendorStock.PromissoryNoteType);
        pack.R.H.R.Reducer.OnCreateObject(
            pack.R.H.R.Context,
            held with { Weenie = held.Weenie with { Header = WeenieHeaderFlags.Container | WeenieHeaderFlags.Value, ContainerId = WorldActionRig.Player, WeenieClassId = Note100k, Value = 100_000 } },
            isUpdate: false);
        pack.R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, note, inside ? side : WorldActionRig.Player, 0, 0));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void M6_16_E5a2_the_trip_trigger_counts_a_note_wherever_it_lies_and_the_counter_can_change_it_there(bool insideASidePack)
    {
        var ground = PotionTripGround();
        var broke = BurrowPack(potions: 0, coin: 14); // r1h: 14 coin and no potion: the trip the purse cannot pay is no trip
        Assert.Equal(LogisticsKind.None, new LogisticsCounters(ground).Check(broke.State).Kind);

        var noted = BurrowPack(potions: 0, coin: 14);
        AddNote(noted, insideASidePack);
        Assert.True(PurseThatLifts(ground, ground.Supplies.Stock.Single(l => l.Wcid == ManaPotion), held: 0) <= 100_014);

        var verdict = new LogisticsCounters(ground).Check(noted.State);

        // As before the move: the face of the note is in the trigger's purse whichever pack holds it, so the trip is called.
        Assert.Equal((LogisticsKind.Trip, LogisticsCause.StockLow, ManaPotion), (verdict.Kind, verdict.Cause, verdict.Line!.Wcid));
        Assert.Equal(100_000, PurseView.NotesHeld(noted.State));

        // M6 16 E5a2: one meaning. A buy at a counter changes the note whichever pack holds it, so what the trigger counts is what the counter can pay with.
        Assert.Equal(100_000, PurseView.NotesCounterCanChange(noted.State));
        Assert.Equal(100_014, PurseView.Read(noted.State).CounterCanPay);
        Assert.Equal(100_014, PurseView.CounterCanPayUnreadAsZero(noted.State));
        Assert.Equal(14, PurseView.CounterCanPayUnreadAsZero(noted.State, vendorTakesNotes: false)); // a vendor known to take no note: the coin alone
    }

    [Fact]
    public void M6_16_E5a1_the_trip_trigger_keeps_the_notes_of_its_last_check_and_counts_a_note_that_arrived_at_the_next()
    {
        var ground = PotionTripGround();
        var pack = BurrowPack(potions: 0, coin: 14);
        var counters = new LogisticsCounters(ground);
        Assert.Equal(LogisticsKind.None, counters.Check(pack.State).Kind);
        Assert.True(counters.IsHeldBack(IndexOf(counters, ManaPotion)));

        AddNote(pack, inside: true); // the note arrives between two checks

        Assert.Equal(LogisticsKind.Trip, counters.Check(pack.State).Kind); // the same counters: the next check reads the notes again
        Assert.False(counters.IsHeldBack(IndexOf(counters, ManaPotion)));
    }
}
