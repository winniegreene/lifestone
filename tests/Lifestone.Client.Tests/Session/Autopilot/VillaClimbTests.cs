using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Housing;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 villa, the climb's wiring (<c>AutopilotScenario.Villa.cs</c>): the level up job's villa schedule at the town trip's boundary, over the climber's scripted world (<see cref="TrophyQuestTests.MakeClimber"/>): the house status asked
/// once, the steps run in the schedule's order (the lockpick at the first trip, then the stone fetch as a ladder errand of the quest station), the cycle line's <c>villa=</c>, and the switch.
/// </summary>
public sealed class VillaClimbTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 16, 18, 0, 0, TimeSpan.Zero);

    private static int _event = 4000;

    /// <summary>The server's HouseStatus "no house" (0x0226), as ACE answers a HouseQuery for an account that owns none.</summary>
    private static void NoHouse(ApWorld w) =>
        w.Trip.Vendor.H.R.Reducer.OnHouseStatus(w.Trip.Vendor.H.R.Context, new GameEventHeader(WorldActionRig.Player, (uint)++_event, GameEventTypes.HouseStatus), new HouseStatusEvent(HouseStatusEvent.NoHouse));

    private static AutopilotOptions Retail => new() { Job = AutopilotJob.LevelUp, TripAfterKills = 1, VillaAccountAge = true };

    private (ApWorld World, AutopilotScenario Autopilot, List<string> Steps) Climber(bool houseKnown, AutopilotOptions? options = null)
    {
        var steps = new List<string>();
        var timers = new QuestTimers("Winnie");
        var (w, autopilot) = TrophyQuestTests.MakeClimber(output, tusk: false, spec => Fetch(spec, steps, timers), timers, options: options);
        autopilot.TrophyQuests = [];
        autopilot.VillaNowForTest = Monday;
        var inner = w.Factory.Script;
        w.Factory.Script = spec => spec.Kind == StationKinds.Villa ? Step(spec, steps) : inner?.Invoke(spec);
        if (houseKnown)
        {
            NoHouse(w);
        }

        return (w, autopilot, steps);
    }

    /// <summary>A villa station that ends well with its line's fields.</summary>
    private static ScriptedRun Step(StationSpec spec, List<string> steps) => new(
        spec.Name,
        _ =>
        {
            steps.Add(spec.Arguments[VillaStations.StepArgument]);
            return ScenarioStatus.Succeeded;
        },
        () => "ok=yes reason=bought");

    /// <summary>The stone fetch: the quest station on the cave's file, ending with the stone (done=yes) and its pick-up's hour on the timers, as the real station records it.</summary>
    private static ScriptedRun? Fetch(StationSpec spec, List<string> steps, QuestTimers timers) =>
        spec.Arguments.TryGetValue(JourneyPlans.QuestArgument, out var key) && key.StartsWith("minor-", StringComparison.Ordinal)
            ? new ScriptedRun(
                spec.Name,
                _ =>
                {
                    steps.Add("fetch:" + key);
                    timers.Complete(QuestCatalog.Get(key), Monday, QuestTimers.HowItem);
                    return ScenarioStatus.Succeeded;
                },
                () => $"key={key} from=home steps=2/2 done=yes xp=+0 reward_line=0 timer=saved ok=yes fail=none")
            : null;

    [Fact]
    public void At_the_first_town_trip_the_lockpick_is_bought_and_then_a_stone_from_each_walkable_cave()
    {
        // The owner, 2026-11-16: a house needs no account age, so the purchase is open and the writs are wanted now: each cave its hour's timer allows, at one town trip.
        var (w, autopilot, steps) = Climber(houseKnown: true);
        w.Run(autopilot, a => a.VillaFetches >= 2 && a.State == AutopilotState.KnownState, 20000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        Assert.Equal([LockpickBuyStation.StationName, "fetch:minor-smoldering-stone", "fetch:minor-sparking-stone"], steps.Take(3)); // no lockpick and no writ held: the lockpick, then both caves
        var kinds = w.Factory.Made.Select(s => s.Kind).ToList();
        var villa = kinds.IndexOf(StationKinds.Villa);
        Assert.Equal(StationKinds.Logistics, kinds[villa - 1]); // at the town trip's boundary
        Assert.Equal([StationKinds.Quest, StationKinds.Quest], kinds.Skip(villa + 1).Take(2)); // the schedule asked again after each step
        Assert.Contains(w.Lines, l => l.StartsWith("villa: weigh villa-purchase: below-level:", StringComparison.Ordinal)); // open, but the rig's climber is level 17 (the villa asks 35)
        Assert.DoesNotContain(w.Lines, l => l.Contains("too-early", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("villa: weigh lockpick-buy: due:", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("villa: after the trip: step lockpick-buy (due): the lockpick-buy station", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("villa: ended step=lockpick-buy ok=yes reason=bought", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("villa: after the villa step lockpick-buy: step writ-fetch (open): the quest station on minor-smoldering-stone", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("villa: after the stone fetch minor-smoldering-stone: step writ-fetch (open): the quest station on minor-sparking-stone", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("villa: check after the fetch minor-sparking-stone: done=yes", StringComparison.Ordinal));
        Assert.Equal(2, autopilot.VillaFetches);
        Assert.Equal(1, autopilot.VillaSteps);
        Assert.Equal(0, autopilot.LadderErrands); // the fetches are the villa's, not ladder errands
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot cycle ", StringComparison.Ordinal) && l.Contains(" villa=on,gather,writs:0/7,", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_16_E5a2_with_a_short_coin_the_lockpick_step_starts_on_a_note_held_inside_a_side_pack()
    {
        // The wiring's read (AutopilotScenario.VillaRead): 100 coin, under the lockpick's 233, and a 10,000 note inside a side pack. Before the slice the step was weighed purse-short on the coin alone and never started.
        var (w, autopilot, steps) = Climber(houseKnown: true);
        var rig = w.Trip.Vendor;
        var pack = rig.NextGuid();
        rig.Arrive(pack, "Pack", 136, 0x200, 65);
        rig.Put(pack, containerType: 1);
        var note = rig.NextGuid();
        rig.Arrive(note, "Trade Note (10,000)", 2625, VendorStock.PromissoryNoteType, 10_000);
        rig.Put(note, pack);
        rig.Coins(100);
        rig.Tick();
        Assert.Equal(InventoryPlaceKind.InSidePack, rig.State.Self.Inventory.Find(note).Kind);
        var notes = PurseView.NotesCounterCanChange(rig.State); // the climber's own notes and the one inside the side pack
        Assert.True(notes >= 10_000);
        Assert.Equal(notes - 10_000, PurseView.Read(rig.State).NotesInMainPack);

        w.Run(autopilot, a => a.VillaSteps >= 1 && a.State == AutopilotState.KnownState, 20000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        Assert.Equal(LockpickBuyStation.StationName, steps.First());
        Assert.Contains(w.Lines, l => l.StartsWith($"villa: weigh lockpick-buy: due: no Crude Lockpick held; one at 233: the coin 100 is short by 133 and the trade notes held ({notes}) cover it, so a note is changed at the Merchant's counter; the trade notes held ({notes}) are counted with the coin: no ground profile lists the Merchant's mask", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("villa: weigh lockpick-buy: purse-short:", StringComparison.Ordinal));
    }

    [Fact]
    public void Under_the_retail_account_age_the_first_trip_fetches_one_stone_paced_to_the_opening()
    {
        // --autopilot-villa-account-age=ace (a server started with LIFESTONE_ACE_HOUSE_ACCOUNT_AGE=ace): the purchase waits for 2026-11-24 22:51:38 UTC and the stones are spread over the time left.
        var (w, autopilot, steps) = Climber(houseKnown: true, Retail);
        w.Run(autopilot, a => a.VillaFetches > 0 && a.State == AutopilotState.KnownState, 20000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        Assert.Equal([LockpickBuyStation.StationName, "fetch:minor-smoldering-stone"], steps.Take(2));
        Assert.Contains(w.Lines, l => l.StartsWith("villa: weigh villa-purchase: too-early:", StringComparison.Ordinal) && l.Contains("2026-11-24 22:51:38Z", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("villa: after the villa step lockpick-buy: step writ-fetch (first): the quest station on minor-smoldering-stone", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("villa: after the stone fetch minor-smoldering-stone: no step:", StringComparison.Ordinal)); // one fetch a town trip on the even pace
        Assert.Equal(1, autopilot.VillaFetches);
    }

    [Fact]
    public void With_the_house_status_unread_one_query_is_sent_and_no_step_runs()
    {
        var (w, autopilot, steps) = Climber(houseKnown: false);
        w.Run(autopilot, a => w.Factory.Made.Count(s => s.Kind == StationKinds.Logistics) >= 2 && a.State == AutopilotState.Fight, 20000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        Assert.Empty(steps);
        Assert.Single(w.Lines, l => l.StartsWith("villa: check before: the house status is unread: one HouseQuery", StringComparison.Ordinal));
        Assert.Single(w.Lines, l => l.Contains("the house status is unread: no villa step until the server answers the HouseQuery", StringComparison.Ordinal) && l.Contains("TODO(villa-status-unanswered)", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot cycle ", StringComparison.Ordinal) && l.Contains(" villa=on,unread,", StringComparison.Ordinal));
    }

    [Fact]
    public void The_switch_off_runs_no_villa_step_and_says_so_on_the_cycle_line()
    {
        var (w, autopilot, steps) = Climber(houseKnown: true, new AutopilotOptions { Job = AutopilotJob.LevelUp, TripAfterKills = 1, Villa = false });
        w.Run(autopilot, a => w.Factory.Made.Count(s => s.Kind == StationKinds.Logistics) >= 2 && a.State == AutopilotState.Fight, 20000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        Assert.Empty(steps);
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("villa:", StringComparison.Ordinal));
        Assert.All(w.Lines.Where(l => l.StartsWith("autopilot cycle ", StringComparison.Ordinal)), l => Assert.Contains(" villa=off every_spell=", l, StringComparison.Ordinal)); // M6 14: the every-spell rule's field closes the line
    }

    [Fact]
    public void The_option_is_on_by_default_and_off_by_its_word()
    {
        static AutopilotOptions Parse(params string[] args) => AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(args));

        Assert.True(new AutopilotOptions().Villa);
        Assert.True(Parse().Villa);
        Assert.False(Parse("--autopilot-villa=off").Villa);
        Assert.True(Parse("--autopilot-villa").Villa);
        Assert.Contains(" villa=off", Parse("--autopilot-villa=off").ToString(), StringComparison.Ordinal);
        Assert.False(Parse().VillaAccountAge);
        Assert.Equal(DateTimeOffset.MinValue, Parse().VillaOpensAt); // the owner, 2026-11-16: at once
        Assert.True(Parse("--autopilot-villa-account-age=ace").VillaAccountAge);
        Assert.Equal(new DateTimeOffset(2026, 11, 24, 22, 51, 38, TimeSpan.Zero), Parse("--autopilot-villa-account-age=ace").VillaOpensAt);
        Assert.Contains(" villa_account_age=ace", Parse("--autopilot-villa-account-age=ace").ToString(), StringComparison.Ordinal);
        Assert.False(Parse("--autopilot-villa-account-age=off").VillaAccountAge);
        Assert.NotEmpty(Parse("--autopilot-villa-account-age=maybe").Errors);
        Assert.True(OptionCatalog.Registry.IsKnown("--autopilot-villa-account-age"));
        Assert.DoesNotContain("villa", Parse().ToString(), StringComparison.Ordinal);
        Assert.NotEmpty(Parse("--autopilot-villa=maybe").Errors);
        Assert.True(OptionCatalog.Registry.IsKnown("--autopilot-villa"));
    }
}
