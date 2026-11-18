using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 pit-hold, the full pack (live 2026-11-13, the Tusker Abode, <c>cache/m6/14/live/abode5</c>): after the 14:00 UTC trip she left with 90 free main-pack slots at 92% of 17,250; from 14:05 her burden stood at 99% (the loot's
/// line, 100%) and from 14:53 the main pack was full, and nothing called a trip. The Abode has no slot line (no <c>loot.min_free_slots</c>) and its burden line is <c>burden_trip_percent</c> 150, over the loot's 100%: neither can fire.
/// The decision then (<c>m6-full-pack-no-trip</c>) was that a full pack calls no trip there. M6 14 pack room (<c>m6-pack-room</c>, the owner's direction of 2026-11-14, which amends it; live in the Tusker Barracks the loot left 473
/// items over its line in two hours): the full-pack line calls a trip once the loot leaves an item over its load line, the visit clears it, and the audit names it. A ground with a slot line (the Holding's 5) keeps it.
/// </summary>
public sealed partial class LogisticsCountersTests
{
    [Fact]
    public void A_full_pack_at_the_loot_line_calls_no_trip_on_the_abode_until_the_loot_leaves_an_item_and_the_audit_says_why()
    {
        var abode = Ground("tusker-abode");
        var counters = new LogisticsCounters(abode, learnHolds: new LearnHolds(), componentNeeds: new ComponentNeeds());
        var capacity = new Pack(Stocked(abode)).Capacity;
        var atTheShelf = (uint)((long)capacity * 9947 / 10_000); // 17,158 of 17,250 at 14:57: 99.47%
        var verdict = counters.Check(new Pack(Filled(abode, freeSlots: 0), burden: atTheShelf).State);
        Assert.Equal(LogisticsKind.None, verdict.Kind); // the burden alone calls nothing: the loot has not left an item yet
        Assert.Equal(0, counters.FreeSlots);
        Assert.Equal((0, 150), (counters.MinFreeSlots, counters.BurdenTripPercent));

        var rule = counters.FullPackRule(MageLootThresholds.LoadLineOf(abode));
        Assert.Equal(
            "none: no slot line (the profile names no loot.min_free_slots); the burden line 150% is at or over the loot's line 100%, so the loot never passes it; the full-pack line calls a trip once the loot leaves a keep-aside piece (a great cantrip) over its line 100% (m6-pack-room, amends m6-full-pack-no-trip; the minimum fight stint holds it after a trip)",
            rule);
    }

    [Fact]
    public void The_holding_s_full_pack_calls_its_trip_at_its_slot_line_and_the_audit_names_it()
    {
        var holding = Ground("tusker-holding");
        var counters = new LogisticsCounters(holding, learnHolds: new LearnHolds(), componentNeeds: new ComponentNeeds());
        var verdict = counters.Check(new Pack(Filled(holding, freeSlots: 0), burden: 100).State);
        Assert.Equal((LogisticsKind.Trip, LogisticsCause.Slots), (verdict.Kind, verdict.Cause));

        var rule = counters.FullPackRule(MageLootThresholds.LoadLineOf(holding));
        Assert.Equal("none: the slot line calls a trip under 5 free slot(s); the burden line 85% is at or over the loot's line 85%, so the loot never passes it; the full-pack line calls a trip once the loot leaves a keep-aside piece (a great cantrip) over its line 85% (m6-pack-room, amends m6-full-pack-no-trip; the minimum fight stint holds it after a trip)", rule);
    }

    [Fact]
    public void M6_14_pack_room_a_trip_is_called_once_the_loot_leaves_an_item_for_the_load_and_not_again_after_the_visit()
    {
        // the Barracks of 2026-11-14 20:39 UTC: 17,848 of 17,850, the loot leaving Platemail Greaves and a Zircon over its line 100%
        var barracks = Ground("tusker-barracks");
        var counters = new LogisticsCounters(barracks, learnHolds: new LearnHolds(), componentNeeds: new ComponentNeeds());
        var capacity = new Pack(Stocked(barracks)).Capacity;
        var pack = new Pack(Stocked(barracks), burden: (uint)((long)capacity * 9_998 / 10_000));
        Assert.Equal(LogisticsKind.None, counters.Check(pack.State).Kind); // full, and the burden line 150% is over the loot's 100%

        var room = PackRoomMemory.For(pack.State);
        room.NoteLoot(leftItems: 0, leftValue: 0, leftBurden: 0, keptBurden: 35); // a corpse whose loot fit
        Assert.Equal(LogisticsKind.None, counters.Check(pack.State).Kind);

        room.NoteLoot(leftItems: 3, leftValue: 20_000, leftBurden: 1_400, keptBurden: 0); // sale loot left for the load: no trip (M6 14: only a keeper left calls one)
        Assert.Equal(0, room.LoadLeftKeepers);
        room.NoteLoot(leftItems: 2, leftValue: 13_502, leftBurden: 672, keptBurden: 0, leftKeepers: 1); // the loot starts leaving a piece she would keep for the load
        var verdict = counters.Check(pack.State);
        Assert.Equal((LogisticsKind.Trip, LogisticsCause.FullPack, 1, 100), (verdict.Kind, verdict.Cause, verdict.Held, verdict.LinePercent));
        Assert.Equal("full-pack", verdict.Trigger);
        Assert.Equal("full pack: the loot left 1 keep-aside piece(s) (a great cantrip) over its load line 100% since the last visit (burden 100% of the capacity): a trip sells what she carries (m6-pack-room)", verdict.Reason);
        Assert.Equal(LogisticsCause.FullPack, new LogisticsCounters(barracks, learnHolds: new LearnHolds(), componentNeeds: new ComponentNeeds()).Check(pack.State).Cause); // the station's own counters read the same session

        room.Visited(3_600, new Dictionary<uint, int>(), new Dictionary<uint, int>()); // the visit sold it
        Assert.Equal(LogisticsKind.None, counters.Check(pack.State).Kind); // not again at once: the next trip waits for the next item the loot leaves
    }

    /// <summary>M6 14 (the owner, 2026-11-14: the potions go once the stamina dance covers her mana): a drink's stock line is suspended while the session's reading says so: no stop, no trip, no buy with none held.</summary>
    [Fact]
    public void M6_14_a_drink_line_calls_no_trip_once_the_stamina_dance_covers_her_mana()
    {
        var barracks = Ground("tusker-barracks");
        var counters = new LogisticsCounters(barracks, learnHolds: new LearnHolds(), componentNeeds: new ComponentNeeds());
        var none = new Pack(With(barracks, FighterKit.ManaPotionWcid, 0), burden: 100);
        var verdict = counters.Check(none.State);
        Assert.Equal((LogisticsKind.Trip, LogisticsCause.StockLow, FighterKit.ManaPotionWcid), (verdict.Kind, verdict.Cause, verdict.Line?.Wcid ?? 0)); // the profile's line: under its trip line, a trip

        PackRoomMemory.For(none.State).DrinksDitched = "the stamina dance pays (the test)";
        Assert.NotEqual(LogisticsCause.StockLow, counters.Check(none.State).Cause); // suspended: the dance covers the mana
        var index = Enumerable.Range(0, counters.LineCount).Single(i => counters.LineAt(i).Wcid == FighterKit.ManaPotionWcid);
        Assert.True(counters.IsSuspended(index));
    }
}
