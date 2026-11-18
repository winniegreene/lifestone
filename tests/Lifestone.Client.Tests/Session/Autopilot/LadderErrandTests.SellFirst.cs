using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 14 sell first (the owner's ruling, 2026-11-17: she must sell before she goes on this quest; live ladder58 12:23 UTC: the Atlan wand's start quest left home at burden 21,674 of 21,750 with 0 free main-pack slots). Proved, pure
/// (<see cref="ErrandRoomRule"/>): the gain of the Atlan wand's own steps (16 motes at the peak, the combines after it), each line of the decision named. Over the scripted ACE: a start quest with a full pack runs the sale trip
/// (<c>trigger=errand-room</c>) first and then the quest, with a check before and after; with room it goes straight; a ladder errand from home with a full pack does the same, from elsewhere and at the fight's trip. Not proved: the live
/// server's sale (the lead's run).
/// </summary>
public sealed partial class LadderErrandTests
{
    private const uint Mote = 6353;

    /// <summary>A ladder errand whose steps gain items: a walk, a hunt-until of 16 motes, then 8 combines (the Atlan wand's shape).</summary>
    private static Quest HuntErrand() => Errand() with
    {
        Steps =
        [
            TrophyQuestTests.Step(0, QuestStepKind.Go, "go to the giver") with { Place = new QuestPlace(Holtburg, Vector3.Zero, "the errand's start") },
            TrophyQuestTests.Step(1, QuestStepKind.HuntUntil, "16 motes") with { Item = new QuestThing(Mote, "Pyreal Mote"), Count = 16, Ground = "crater-caves" },
            TrophyQuestTests.Step(2, QuestStepKind.Combine, "16 motes to 8 slivers") with { Item = new QuestThing(Mote, "Pyreal Mote"), Count = 8, Expect = new QuestExpect(null, null, new QuestThing(6355, "Pyreal Sliver")) },
        ],
    };

    /// <summary>Every scrivener's line in her book, so the start audit makes no learn trip (a trip with no kill after it is the check after's sale).</summary>
    private static void NoLearnTrip(ApWorld w)
    {
        foreach (var spell in ScrivenerCatalog.Default.Spells)
        {
            w.Trip.Vendor.State.Self.Spellbook.Learn(spell.SpellId);
        }
    }

    private static StationSpec[] RoomTrips(ApWorld w) =>
        [.. w.Factory.Made.Where(s => s.Kind == StationKinds.Logistics && s.Arguments.TryGetValue(JourneyPlans.TripArgument, out var t) && t == JourneyPlans.TripErrandRoom)];

    [Fact]
    public void The_atlan_wands_own_steps_gain_its_combined_chain_at_the_peak_and_each_room_line_is_named()
    {
        // M6 14 combine3: the hunt combines as it goes (motes to slivers to nuggets to bars), so its room is the chain's end, 2 bars, and the most objects on the way, 5 (one unpaired at each link and the new mote), not 16 loose motes
        var atlan = QuestCatalog.All.First(q => q.Key == "atlan-wand");
        var none = ErrandRoomRule.GainOf(atlan, null, _ => 0, _ => null);
        Assert.Equal((5, 5, 0, true), (none.Items, none.Slots, none.Burden, none.Fights)); // the no-roll combines after the hunt add nothing: the peak is the hunt's
        Assert.Contains("Pyreal Bar (6329)", none.Unknown, StringComparison.Ordinal); // the end state's item, of which she holds no copy

        // the world DB's burdens (weenie_properties_int type 5): mote 1, sliver 10, nugget 50, bar 100; each stacks to 1
        (int, int)? Copy(uint w) => w switch { Mote => (1, 1), 6355 => (10, 1), 6354 => (50, 1), 6329 => (100, 1), _ => null };
        var some = ErrandRoomRule.GainOf(atlan, null, w => w == Mote ? 10 : 0, Copy);
        Assert.Equal((1, 1, 190, null), (some.Items, some.Slots, some.Burden, some.Unknown)); // 10 motes held become 2 bars: the slots fall, the burden rises (-10 + 200); one more object at the peak (the 11th mote before its combine)

        var talk = ErrandRoomRule.GainOf(atlan, "0-3", _ => 0, _ => null); // the walk and the portal alone
        Assert.False(talk.Gains);

        // ladder58 12:23 UTC: burden 21,674 of 21,750, 0 free slots
        var full = ErrandRoomRule.Decide(none, 21674, 21750, 100, 0, null);
        Assert.True(full.SaleFirst);
        Assert.Contains("0 free main-pack slot(s), under the 5 the errand needs", full.Why, StringComparison.Ordinal);
        Assert.True(ErrandRoomRule.Decide(none, 21750, 21750, 100, 40, null) is { SaleFirst: true } at && at.Why.Contains("is at or over the load line 100% (21750)", StringComparison.Ordinal));
        Assert.True(ErrandRoomRule.Decide(some, 21740, 21750, 100, 40, null) is { SaleFirst: true } cross && cross.Why.Contains("burden 21740 with the errand's 190 would cross the load line 100%", StringComparison.Ordinal));
        Assert.True(ErrandRoomRule.Decide(none, 1000, 21750, 100, 40, "Prismatic Taper: 3 held, under its trip line 50") is { SaleFirst: true } stock && stock.Why.Contains("a stock line is under its trip line: Prismatic Taper", StringComparison.Ordinal));
        var room = ErrandRoomRule.Decide(none, 1000, 21750, 100, 40, null);
        Assert.False(room.SaleFirst);
        Assert.EndsWith("room for it: the errand goes straight", room.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_start_quest_with_a_full_pack_runs_the_sale_trip_first_then_the_quest()
    {
        var quest = HuntErrand();
        var timers = new QuestTimers("Winnie");
        var (w, autopilot) = TrophyQuestTests.MakeClimber(output, tusk: false, spec => Completes(spec, quest, timers), timers, options: new AutopilotOptions { Job = AutopilotJob.LevelUp, StartQuest = Key });
        autopilot.TrophyQuests = [quest];
        autopilot.FreeSlotsForTest = 0;
        NoLearnTrip(w);
        w.Run(autopilot, a => a.LadderErrands > 0 && a.State == AutopilotState.Fight, 20000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        var made = w.Factory.Made.ToList();
        var at = made.FindIndex(s => s.Kind == StationKinds.Quest);
        Assert.True(at > 0, string.Join(",", Kinds(w)));
        Assert.Same(AutopilotScenario.ErrandRoomTripSpec, made[at - 1]); // the sale right before the quest
        Assert.Single(RoomTrips(w));
        Assert.DoesNotContain("den-fight", Kinds(w).Take(at));
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: key={Key}: errand room: check before: ", StringComparison.Ordinal) && l.Contains("0 free main-pack slot(s), under the 16 the errand needs", StringComparison.Ordinal) && l.Contains("trigger=errand-room", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: key={Key}: errand room: check after the sale trip (no kill since it ended): ", StringComparison.Ordinal) && l.Contains("m6-errand-room-after-sale", StringComparison.Ordinal)); // still full in the script: named, no second trip
        Assert.Single(Quests(w));
        Assert.Equal(1, autopilot.LadderErrands);
    }

    [Fact]
    public void A_start_quest_with_room_goes_straight()
    {
        var quest = HuntErrand();
        var timers = new QuestTimers("Winnie");
        var (w, autopilot) = TrophyQuestTests.MakeClimber(output, tusk: false, spec => Completes(spec, quest, timers), timers, options: new AutopilotOptions { Job = AutopilotJob.LevelUp, StartQuest = Key });
        autopilot.TrophyQuests = [quest];
        autopilot.FreeSlotsForTest = 40;
        autopilot.BurdenForTest = 30;
        NoLearnTrip(w);
        w.Run(autopilot, a => a.LadderErrands > 0 && a.State == AutopilotState.Fight, 20000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        Assert.Empty(RoomTrips(w));
        Assert.Single(Quests(w));
        var room = Assert.Single(w.Lines, l => l.StartsWith($"autopilot quest: key={Key}: errand room: check", StringComparison.Ordinal));
        Assert.EndsWith("room for it: the errand goes straight", room, StringComparison.Ordinal);
        Assert.Contains("40 free main-pack slot(s)", room, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ladder_errand_from_home_with_a_full_pack_sells_first_then_runs()
    {
        var quest = HuntErrand();
        ApWorld? world = null;
        QuestTimers? timers = null;
        var (w, autopilot, t) = Climber(quest, (spec, _) => Completes(spec, quest, timers!, () => world!.Trip.Vendor.StandAt(world.Ground.Home.LifestoneCell, world.Ground.Home.LifestonePoint)));
        (world, timers) = (w, t);
        autopilot.FreeSlotsForTest = 0;
        w.Trip.Vendor.StandAt(Shoushi, new Vector3(100, 100, 0)); // a travel place, neither the ground nor home: the errand from home is asked on the way home
        w.Run(autopilot, a => a.LadderErrands > 0 && a.State != AutopilotState.Trip, 20000);

        var made = w.Factory.Made.ToList();
        var at = made.FindIndex(s => s.Kind == StationKinds.Quest);
        Assert.True(at > 0, string.Join(",", Kinds(w)));
        Assert.Same(AutopilotScenario.ErrandRoomTripSpec, made[at - 1]);
        Assert.Single(Quests(w));
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: key={Key}: errand room: check before: ", StringComparison.Ordinal) && l.Contains("under the 16 the errand needs", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: key={Key}: errand room: act: the logistics station first (trigger=errand-room), then the errand from home", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains($"after the sale trip (errand-room): ", StringComparison.Ordinal) && l.Contains($"the ladder errand {Key} is due", StringComparison.Ordinal));
        Assert.Equal(1, autopilot.LadderErrands);
    }

    [Fact]
    public void A_ladder_errand_at_the_fights_trip_with_a_full_pack_waits_for_that_trips_sale()
    {
        var quest = HuntErrand();
        QuestTimers? timers = null;
        var (w, autopilot, t) = Climber(quest, (spec, _) => Completes(spec, quest, timers!));
        timers = t;
        autopilot.FreeSlotsForTest = 0;
        NoLearnTrip(w);
        w.Run(autopilot, a => a.LadderErrands > 0 && a.State == AutopilotState.KnownState, 20000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        var kinds = Kinds(w);
        var at = Array.IndexOf(kinds, "quest");
        Assert.Equal("logistics", kinds[at - 1]); // the trip the fight stopped for sells first, then the errand
        Assert.Equal("den-fight", kinds[at - 2]);
        Assert.Empty(RoomTrips(w)); // that trip is the sale: no second one
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: key={Key}: errand room: act: the trip the fight stopped for sells first; the errand from where it ends", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: key={Key}: errand room: check after the sale trip (no kill since it ended): ", StringComparison.Ordinal));
        Assert.Single(Quests(w));
        Assert.Equal(1, autopilot.LadderErrands);
    }
}
