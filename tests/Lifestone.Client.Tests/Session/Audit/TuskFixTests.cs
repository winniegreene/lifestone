using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Tests.Session.Grounds;
using Xunit;

namespace Lifestone.Client.Tests.Session.Audit;

/// <summary>
/// M6 audit, the tusks (the owner's decision of 2026-11-19: every tusk when it is due, turned in on the hour): the timer counted from the pickup the server stamps, and the Barracks' Guard Tusk sought at its own carriers.
/// </summary>
public sealed class TuskFixTests
{
    [Fact]
    public void Every_tusk_quest_times_from_the_pickup_and_names_its_own_carrier()
    {
        var tusks = QuestCatalog.All.Where(q => q.Key.StartsWith("tusk-", StringComparison.Ordinal)).ToList();
        Assert.Equal(10, tusks.Count);
        foreach (var q in tusks)
        {
            Assert.Equal(QuestTimerFrom.Pickup, q.TimerFrom);
            var hunt = q.Steps.Single(s => s.Kind == QuestStepKind.HuntUntil);
            var carrier = Assert.Single(hunt.Carriers!);
            Assert.Equal(hunt.Target!.Wcid, carrier.Wcid); // the pinned world SQL: the target holds the tusk at destination 1 (Contain)
            Assert.Equal(1.0, carrier.Shade);
            Assert.Equal(hunt.Item, TrophyPickups.PickupItem(q));
        }

        Assert.All(QuestCatalog.All.Where(q => !q.Key.StartsWith("tusk-", StringComparison.Ordinal)), q => Assert.Equal(QuestTimerFrom.TurnIn, q.TimerFrom));
    }

    [Fact]
    public void The_timer_runs_from_the_moment_the_tusk_is_first_seen_held()
    {
        // run 4 of the sprint (cache/m6/sprint/run4/step3-levelup.log): the Female Tusker Tusk kept at 03:57:11 UTC, turned in at 04:35:54 UTC, recorded next due 2026-11-20T00:35:54Z
        var female = QuestCatalog.Get("tusk-female");
        var picked = DateTimeOffset.Parse("2026-11-19T03:57:11Z", System.Globalization.CultureInfo.InvariantCulture);
        var turnIn = DateTimeOffset.Parse("2026-11-19T04:35:54Z", System.Globalization.CultureInfo.InvariantCulture);
        var pickups = new TrophyPickups();

        Assert.True(pickups.Watch(22423, 1, picked));
        Assert.False(pickups.Watch(22423, 1, picked.AddMinutes(5))); // still held: the first moment stands
        var at = pickups.StartOf(female, turnIn, out var note);
        Assert.Equal(picked, at);
        Assert.Contains("the pickup: Female Tusker Tusk (22423) first seen in the pack at 2026-11-19T03:57:11Z, 38.7 min before the turn-in", note, StringComparison.Ordinal);
        var entry = new QuestTimers("test").Complete(female, at, QuestTimers.HowReply);
        Assert.Equal(DateTimeOffset.Parse("2026-11-19T23:57:11Z", System.Globalization.CultureInfo.InvariantCulture), entry.NextDue); // 72000 s after the pickup: 38 min 43 s sooner than the turn-in's

        Assert.False(pickups.Watch(22423, 0, turnIn)); // given away: forgotten
        Assert.Null(pickups.PickedAt(22423));
        Assert.Equal(turnIn, pickups.StartOf(female, turnIn, out var unseen)); // no moment seen this session: the turn-in's, never early
        Assert.Contains("was not seen this session", unseen, StringComparison.Ordinal);

        pickups.Watch(22423, 1, picked);
        Assert.Equal(turnIn, pickups.StartOf(QuestCatalog.Get("worcer-heirlooms"), turnIn, out var turnInNote)); // a quest timed from the turn-in is not moved
        Assert.Equal("the turn-in", turnInNote);
    }

    [Fact]
    public void A_held_tusks_turn_in_fights_nothing_so_the_room_never_sends_it_after_the_sale()
    {
        // run 4 (cache/m6/sprint/run4/step3-levelup.log 04:25:50 UTC): the held Female Tusk's turn-in read as a fight, a low stock line sent the sale first, and the turn-in left the Burrow as a trip of its own one second after
        // that sale trip walked her back (04:34:37 to 04:34:38). Held, its hunt is done at once: it gains nothing, so the room is not read and the turn-in rides the town run it was called on.
        var female = QuestCatalog.Get("tusk-female");
        var held = ErrandRoomRule.GainOf(female, null, w => w == 22423 ? 1 : 0, _ => null);
        Assert.Equal((0, false, false), (held.Items, held.Fights, held.Gains));

        var none = ErrandRoomRule.GainOf(female, null, _ => 0, _ => null); // not held: the hunt still fights (the start quest or a hunt errand)
        Assert.Equal((1, true, true), (none.Items, none.Fights, none.Gains));
    }

    [Fact]
    public void The_Barracks_fight_names_the_Guard_Tusks_own_carrier()
    {
        var carrier = Assert.Single(AutopilotQuestRule.Carriers("tusker-barracks", QuestCatalog.All, [22425u]));
        Assert.Equal((22593u, 22425u, 1.0, "Tusker Guard Tusk"), (carrier.Creature, carrier.Item, carrier.Shade, carrier.ItemName));
        Assert.Empty(AutopilotQuestRule.Carriers("tusker-barracks", QuestCatalog.All, []));
        Assert.Empty(AutopilotQuestRule.Carriers("tusker-barracks", QuestCatalog.All, [22423u])); // another ground's tusk
    }

    [Fact]
    public void A_tour_told_the_carriers_goes_to_their_slots_first_and_back_to_its_order_when_none_stands()
    {
        var log = new RecordingLogSink();
        var plain = new GroundTour(TwoStoreyGround.Build(), TwoStoreyGround.Metres, NullLogSink.Instance);
        Assert.Equal(TwoStoreyGround.T(1), plain.Next(TwoStoreyGround.Arrival, 0)); // its own order: the nearest Slave

        var tour = new GroundTour(TwoStoreyGround.Build(), TwoStoreyGround.Metres, log);
        tour.Prefer([TwoStoreyGround.Guard], "the test's item");
        Assert.Equal(TwoStoreyGround.L(1), tour.Next(TwoStoreyGround.Arrival, 0)); // the carrier's nearest standing slot, a storey down, before the Slaves on hers
        Assert.Equal(TourPurpose.Presumed, tour.Purpose);
        Assert.Contains("the wanted trophy's carrier: a standing slot of 11", tour.PurposeNote, StringComparison.Ordinal);

        tour.Killed(TwoStoreyGround.Guard, TwoStoreyGround.L(1), 1);
        tour.Killed(TwoStoreyGround.Guard, TwoStoreyGround.L(3), 2);
        tour.Killed(TwoStoreyGround.Guard, TwoStoreyGround.L(5), 3);
        Assert.Equal(TwoStoreyGround.T(1), tour.Next(TwoStoreyGround.Arrival, 4)); // no carrier stands: the tour's own order

        tour.Prefer([], string.Empty);
        Assert.DoesNotContain("carrier", tour.PurposeNote, StringComparison.Ordinal);
    }

    [Fact]
    public void On_the_Barracks_the_tour_goes_to_one_of_the_five_Guard_Tusk_carriers_first()
    {
        // the pinned world SQL (cache/m6/4/world.sqlite, landblock_instance rows of 0x5A48): 49 Tusker Guards of 1629 and 5 of 22593, the tusk's carrier, all children of the linkable generator 0x75A48033
        var barracks = GroundCatalog.TryGet("tusker-barracks")!;
        var tour = new GroundTour(barracks, static (a, b) => Math.Abs((double)(a & 0xFFFF) - (b & 0xFFFF)), NullLogSink.Instance);
        var slots = Enumerable.Range(0, tour.Schedule.Count).Select(tour.Schedule.Slot).ToList();
        var carriers = slots.Where(s => s.Wcid == 22593).Select(s => s.Cell).ToHashSet();
        Assert.Equal(new HashSet<uint> { 0x5A480101, 0x5A480108, 0x5A48011D, 0x5A480132, 0x5A480139 }, carriers);
        Assert.Equal(49, slots.Count(s => s.Wcid == 1629));

        tour.Prefer([22593u], "Tusker Guard Tusk");
        var first = tour.Next(barracks.Arrival.Cell, 0);
        Assert.NotNull(first);
        Assert.Contains(first!.Value, carriers);
    }
}
