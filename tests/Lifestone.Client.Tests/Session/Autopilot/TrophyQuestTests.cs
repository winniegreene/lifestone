using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 item 5 Q5 (D58): the level up job's trophy errand. Proved, pure (<see cref="AutopilotQuestRule"/>): the fight's want set is the due trophies of the rung's ground in the level's band not yet held (never another ground's, never one on its
/// timer); the call runs the quest station <c>from=ground</c> at level 10 on the Burrow without Lifestone Recall (the travel file's way home from the Burrow starts with its exit into the island's place, where Brighteyes stands) and
/// <c>from=home</c> at 16 with it; the refusals are named (<c>level-out-of-band</c>, <c>not-due</c>, <c>not-held</c>, <c>no-route</c>). Over the scripted ACE: the level up job's fight carries <c>want=</c>; with a due tusk held the fight's
/// trip runs the quest station first and the trip after it (the turn-in counted on the <c>[ladder]</c> line's <c>turn_ins=</c>); a failed turn-in stops by its own name through the station failure's recovery and is not called again on the way
/// home; a cut errand is not run again and the trip goes on. The quest records are the test's own (the tusk files are Q6a's); the real station's walk and give are the loopback's (TrophyLiveTests).
/// </summary>
public sealed class TrophyQuestTests(ITestOutputHelper output)
{
    private const string Burrow = "tusker-burrow";
    private const string Lodge = "tusker-lodge"; // M6 7 AC: the Burrow rung is dropped; the scripted climbs below run on the Lodge (the ladder rung of level 16), where tusk-male is due
    internal const uint Tusk = 22423; // Female Tusker Tusk (facts.md section 2)
    private const uint Carrier = 22589; // Female Tusker (the tusk's carrier)
    private const uint Brighteyes = 22642;
    private const uint Gem = 22729; // Aphus Lassel Portal Gem
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 14, 0, 0, TimeSpan.Zero);
    private static readonly HuntingGround BurrowGround = GroundCatalog.Get(Burrow);

    /// <summary>A trophy quest in the shape of Q6a's tusk files: hunt-until the tusk on the Burrow, go to Brighteyes (cell 0xF682011C, facts.md section 1), give it; the 20 h timer.</summary>
    internal static Quest TuskFemale(QuestLevel? level = null, uint turnInCell = 0xF682011C, string ground = Burrow) => new()
    {
        Key = "tusk-female",
        Name = "Female Tusker Tusk",
        Giver = new QuestThing(Brighteyes, "Brighteyes, the Tailor"),
        Start = new QuestPlace(turnInCell, new Vector3(175.994f, 30.9674f, 63.605f), "Brighteyes's room"),
        Steps =
        [
            Step(0, QuestStepKind.HuntUntil, "hunt the Female Tusker for its tusk") with { Target = new QuestThing(Carrier, "Female Tusker"), Item = new QuestThing(Tusk, "Female Tusker Tusk"), Count = 1, Ground = ground },
            Step(1, QuestStepKind.Go, "go to Brighteyes") with { Place = new QuestPlace(turnInCell, new Vector3(175.994f, 30.9674f, 63.605f), "Brighteyes's room"), Target = new QuestThing(Brighteyes, "Brighteyes, the Tailor") },
            Step(2, QuestStepKind.Give, "give her the tusk") with { Target = new QuestThing(Brighteyes, "Brighteyes, the Tailor"), Item = new QuestThing(Tusk, "Female Tusker Tusk"), Expect = new QuestExpect("Excellent work.", null, new QuestThing(Gem, "Aphus Lassel Portal Gem")) },
        ],
        Done = new QuestDone(null, "Excellent work.", new QuestThing(Gem, "Aphus Lassel Portal Gem")),
        TimerSeconds = 72000,
        Sources = ["the test's own record (Q6a writes the file)"],
        Level = level,
    };

    internal static QuestStep Step(int index, QuestStepKind kind, string what) => new()
    {
        Index = index,
        Kind = kind,
        What = what,
        Place = null,
        Target = null,
        Item = null,
        Expect = new QuestExpect(null, null, null),
        Source = "test",
        Todo = null,
    };

    private static uint OnBurrow => BurrowGround.ArrivalDestination().Location.Position.CellId;

    private static TrophyQuestInput Input(int held = 1, int level = 10, bool recall = false, uint? cell = null, QuestTimers? timers = null, string? ground = Burrow, Quest? quest = null) =>
        new(ground, [quest ?? TuskFemale()], timers ?? new QuestTimers("Winnie"), Now, w => w == Tusk ? held : 0, level, recall, cell ?? OnBurrow, GroundCatalog.All, TravelRoutes.Default);

    // ------------------------------------------------------------------------------------------------ the rule

    [Fact]
    public void The_want_set_is_the_due_trophies_of_the_rungs_ground_not_yet_held()
    {
        Assert.Equal([Tusk], AutopilotQuestRule.Want(Input(held: 0)));
        Assert.Empty(AutopilotQuestRule.Want(Input(held: 1))); // held: the pick-up is gated once a kind per 20 h, a second is never wanted
        Assert.Empty(AutopilotQuestRule.Want(Input(held: 0, ground: "tusker-lodge"))); // another ground's rung
        Assert.Empty(AutopilotQuestRule.Want(Input(held: 0, ground: null))); // no ground rung (the den job, a quest rung)

        var timers = new QuestTimers("Winnie");
        timers.Complete(TuskFemale(), Now.AddHours(-1), QuestTimers.HowReply);
        Assert.Empty(AutopilotQuestRule.Want(Input(held: 0, timers: timers))); // on its 20 h timer
        Assert.Equal([Tusk], AutopilotQuestRule.Want(Input(held: 0, timers: timers) with { Now = Now.AddHours(20) })); // due again

        Assert.Empty(AutopilotQuestRule.Want(Input(held: 0, level: 40, quest: TuskFemale(new QuestLevel(10, 30))))); // out of the quest's band
    }

    [Fact]
    public void At_ten_without_lifestone_recall_the_turn_in_is_from_the_ground_and_at_sixteen_it_is_a_trip_from_home()
    {
        var ten = Assert.Single(AutopilotQuestRule.Calls(Input(level: 10)));
        Assert.True(ten.Runs);
        Assert.Equal(JourneyPlans.FromGround, ten.From);
        Assert.Contains("starts with its exit into island", ten.Why, StringComparison.Ordinal); // the travel file's way home from the Burrow: exit 22664 to 0xF982, the island's place, which holds the Refuge
        Assert.StartsWith("autopilot quest: key=tusk-female due=yes held=1 from=ground why=", ten.Line, StringComparison.Ordinal);

        var sixteen = Assert.Single(AutopilotQuestRule.Calls(Input(level: 16, recall: true)));
        Assert.Equal(JourneyPlans.FromHome, sixteen.From);
        Assert.Contains("Lifestone Recall is in the book", sixteen.Why, StringComparison.Ordinal);

        var home = Assert.Single(AutopilotQuestRule.Calls(Input(cell: BurrowGround.Home.LifestoneCell))); // standing in Holtburg below the recall: a trip of its own
        Assert.Equal(JourneyPlans.FromHome, home.From);
        Assert.Contains("holtburg", home.Why, StringComparison.Ordinal);
        Assert.Same(ten.Quest, AutopilotQuestRule.First([sixteen with { From = null, Refusal = "x" }, ten])!.Quest);
    }

    [Fact]
    public void The_refusals_are_named_with_their_inputs()
    {
        static TrophyQuestCall One(TrophyQuestInput input) => Assert.Single(AutopilotQuestRule.Calls(input));

        var timers = new QuestTimers("Winnie");
        timers.Complete(TuskFemale(), Now.AddHours(-2), QuestTimers.HowReply);
        var notDue = One(Input(timers: timers));
        Assert.Equal(("not-due", false), (notDue.Refusal, notDue.Due));
        Assert.Contains("next due 2026-11-05T08:00:00Z", notDue.Why, StringComparison.Ordinal);
        Assert.EndsWith("from=none why=not-due: " + notDue.Why, notDue.Line, StringComparison.Ordinal);

        var notHeld = One(Input(held: 0));
        Assert.Equal("not-held", notHeld.Refusal);
        Assert.Contains("0 of 1 Female Tusker Tusk (22423)", notHeld.Why, StringComparison.Ordinal);

        var band = One(Input(level: 40, quest: TuskFemale(new QuestLevel(10, 30))));
        Assert.Equal("level-out-of-band", band.Refusal);
        Assert.Contains("level 40 is out of the quest's band 10-30", band.Why, StringComparison.Ordinal);

        var nowhere = One(Input(quest: TuskFemale(turnInCell: 0x12340100)));
        Assert.Equal("no-route", nowhere.Refusal);
        Assert.Contains("in no travel place", nowhere.Why, StringComparison.Ordinal);

        var lost = One(Input(cell: 0x12340100)); // the body on no ground and in no travel place
        Assert.Equal("no-route", lost.Refusal);
        Assert.False(lost.Runs);
        Assert.Null(AutopilotQuestRule.First([notDue, notHeld, band, nowhere, lost]));
    }

    [Fact]
    public void Over_the_shipped_quest_files_each_profiled_rung_ground_has_its_one_tusk_and_the_Burrow_turns_in_from_the_ground()
    {
        // Q6a's files (D57): one tusk a profiled island ground; the rule reads them by their hunt-until ground, never by a constant (D26).
        foreach (var rung in LadderLoader.WarMage.Rungs.Where(r => r.GroundKey is { } g && g.StartsWith("tusker-", StringComparison.Ordinal) && !g.StartsWith("tusker-temple", StringComparison.Ordinal)))
        {
            if (rung.GroundKey == "tusker-retreat")
            {
                // M6 retreat: the Tusker Retreat holds no tusk carrier (tusker-retreat.json sources.targets); its rung's tusk is its alternate's, the Burrow's
                Assert.Empty(AutopilotQuestRule.Trophies(rung.GroundKey, QuestCatalog.All));
                Assert.Single(AutopilotQuestRule.Trophies(Assert.Single(rung.Alternates).GroundKey, QuestCatalog.All));
                continue;
            }

            Assert.Single(AutopilotQuestRule.Trophies(rung.GroundKey, QuestCatalog.All));
        }

        var burrow = Input(level: 12) with { Quests = QuestCatalog.All };
        var call = Assert.Single(AutopilotQuestRule.Calls(burrow));
        Assert.Equal(("tusk-female", JourneyPlans.FromGround), (call.Quest.Key, call.From));
        Assert.Equal([Tusk], AutopilotQuestRule.Want(burrow with { Held = _ => 0 }));
    }

    /// <summary>
    /// M6 7 AR (the owner's correction of 2026-11-06): the Burrow is a rung again, so tusk-female is due on it; the Lodge's tusk-male stays the Lodge's: its carrier is 22597 at the Lodge, the fight wants 22427 there, and at level 15 (no Lifestone Recall until 16)
    /// the turn-in is the way home's own (from=ground) when the Lodge's way home starts with the exit into the island's place, else a trip of its own from Holtburg: either arm is a named call, never a refusal.
    /// </summary>
    [Fact]
    public void M6_7_AR_the_Burrow_and_the_Lodge_are_the_ladders_tusk_grounds()
    {
        // M6 retreat (the owner, 2026-11-19): the 10-14 rung hunts the Tusker Retreat and the Burrow is its live alternate, the ground of the Female Tusk's trophy job (RungOwner's job reads a rung's alternates)
        var rungGrounds = LadderLoader.WarMage.Rungs.Select(r => r.GroundKey).Concat(LadderLoader.WarMage.Rungs.SelectMany(r => r.Alternates.Where(a => !a.Pending).Select(a => (string?)a.GroundKey))).ToList();
        Assert.Contains(Burrow, rungGrounds);
        Assert.Contains(rungGrounds.Where(g => g is not null).SelectMany(g => AutopilotQuestRule.Trophies(g, QuestCatalog.All)), t => t.Quest.Key == "tusk-female");

        var lodge = GroundCatalog.Get(Lodge);
        var input = new TrophyQuestInput(Lodge, QuestCatalog.All, new QuestTimers("Winnie"), Now, w => w == 22427 ? 1 : 0, 15, false, lodge.ArrivalDestination().Location.Position.CellId, GroundCatalog.All, TravelRoutes.Default);
        Assert.Equal([22427u], AutopilotQuestRule.Want(input with { Held = _ => 0 }));
        var call = Assert.Single(AutopilotQuestRule.Calls(input));
        Assert.Equal("tusk-male", call.Quest.Key);
        Assert.True(call.Runs, call.Why);
        Assert.Equal(JourneyPlans.FromGround, call.From); // the way home from the Lodge below Lifestone Recall starts with its exit into the island: the turn-in sits between
        Assert.Equal(JourneyPlans.FromHome, Assert.Single(AutopilotQuestRule.Calls(input with { Level = 16, KnowsLifestoneRecall = true })).From);
    }

    // ------------------------------------------------------------------------------------------------ the autopilot over the scripted ACE

    /// <summary>A level-15 climber on the Lodge (M6 7 AC; the Burrow rung is dropped) in the level up job, two casters held (one wielded), the tusk in the pack when <paramref name="tusk"/>; the trip forced after one kill so the fight stops for it.</summary>
    private (ApWorld World, AutopilotScenario Autopilot) Climber(bool tusk, Func<StationSpec, IStationRun?>? quest = null) => MakeClimber(output, tusk, quest);

    /// <summary>The scripted climber (<see cref="Climber"/>), for <see cref="LadderErrandTests"/> too; <paramref name="timers"/> are the character's when given.</summary>
    internal static (ApWorld World, AutopilotScenario Autopilot) MakeClimber(ITestOutputHelper output, bool tusk, Func<StationSpec, IStationRun?>? quest = null, QuestTimers? timers = null, Action<ApWorld>? prepare = null, AutopilotOptions? options = null)
    {
        var w = new ApWorld(Lodge, output, atHome: false) { Quests = timers };
        prepare?.Invoke(w); // M6 8 K: before the wield: a player description replaces the qualities
        w.Level(17); // M6 7 AR: the Lodge from 17
        var wielded = w.Trip.Own("Wand", 31336, 0x8000, 100);
        w.Trip.Vendor.R.Server((p, s) => Lifestone.Tests.Shared.Net.AceM3Writers.WieldObject(p, s, wielded, 0x01000000));
        w.Trip.Vendor.R.Tick();
        w.Trip.Own("Spare Wand", 31336, 0x8000, 100);
        if (tusk)
        {
            w.Trip.Own("Female Tusker Tusk", Tusk, 1, 0);
        }

        var arrival = w.Ground.ArrivalDestination().Location.Position;
        w.Factory.Script = spec => spec.Kind switch
        {
            StationKinds.Quest => quest?.Invoke(spec),
            StationKinds.Logistics => new ScriptedRun(spec.Name, _ => ScenarioStatus.Succeeded, () => "sold=0 bought=0"),
            StationKinds.RungChange => new ScriptedRun(spec.Name, _ => // out: the Burrow's arrival; home: Holtburg's lifestone (AutopilotRungTravelTests' script)
            {
                var home = spec.Arguments[RungChangeStation.DirectionArgument] == "home";
                w.Trip.Vendor.StandAt(home ? w.Ground.Home.LifestoneCell : arrival.CellId, home ? w.Ground.Home.LifestonePoint : arrival.Local);
                return ScenarioStatus.Succeeded;
            }, () => $"ground={Lodge} ok=yes fail=none"),
            _ => null,
        };
        var self = w.Trip.Vendor.State.Self;
        self.Qualities.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0); // M6 7 AW: the gates are read on the way back: this climber may go
        self.Spellbook.IsKnown = true;
        foreach (var line in LadderLoader.WarMage.Gates.SelectMany(g => g.Lines))
        {
            self.Spellbook.Learn(RungGateRule.LineOf(BuffProfiles.Climber, line.Line).Spells[line.Level - 1].Id);
        }

        var autopilot = w.Autopilot(options ?? new AutopilotOptions { Job = AutopilotJob.LevelUp, TripAfterKills = 1 });
        autopilot.BuffsForTest = new RungBuffRead(static _ => 8, static _ => 4);
        autopilot.TrophyQuests = [TuskFemale(ground: Lodge)];
        return (w, autopilot);
    }

    internal static IStationRun TurnIn(StationSpec spec, ApWorld w) => new ScriptedRun(
        spec.Name,
        _ =>
        {
            var state = w.Trip.Vendor.State;
            var tusk = state.Self.Inventory.Pack.First(e => state.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == Tusk);
            w.Trip.Vendor.Remove(tusk.Guid); // given
            return ScenarioStatus.Succeeded;
        },
        () => $"key={spec.Arguments[JourneyPlans.QuestArgument]} from={spec.Arguments[JourneyPlans.FromArgument]} steps=3/3 done=yes xp=+100000 reward_line=100000 timer=saved ok=yes fail=none");

    [Fact]
    public void The_level_up_fight_wants_the_due_tusk_of_its_rung()
    {
        var (w, autopilot) = Climber(tusk: false);
        w.Run(autopilot, a => a.State == AutopilotState.Fight, 6000);

        var fight = w.Factory.Made.Last(s => s.Kind == StationKinds.DenFight);
        Assert.Equal(Tusk.ToString(CultureInfo.InvariantCulture), fight.Arguments[JourneyStations.WantArgument]);
        Assert.Contains(w.Lines, l => l.Contains("ladder: the fight's want: 22423 (want=22423: the due trophy quests of tusker-lodge, D58)", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_7_AY_a_dropped_back_climber_holds_to_the_first_slots_and_the_tusk_stays_wanted()
    {
        var (w, autopilot) = Climber(tusk: false);
        autopilot.PackHoldNow = new PackHold(Lodge, 2, true, 100_000); // far above any base mana: the hold stands
        w.Run(autopilot, a => a.State == AutopilotState.Fight, 6000);

        var fight = w.Factory.Made.Last(s => s.Kind == StationKinds.DenFight);
        Assert.Equal("yes", fight.Arguments[JourneyStations.PackHoldArgument]);
        Assert.Equal(Tusk.ToString(CultureInfo.InvariantCulture), fight.Arguments[JourneyStations.WantArgument]); // the tusk goes ahead: the rung and its wants are untouched
        Assert.Contains(w.Lines, l => l.Contains("ladder: the fight holds to the first slots of tusker-lodge (pack-hold=yes", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_7_AY_grown_mana_lifts_the_hold_before_the_fight_and_another_grounds_hold_binds_nothing()
    {
        var (w, autopilot) = Climber(tusk: false);
        autopilot.PackHoldNow = new PackHold("tusker-burrow", 2, true, 100_000);
        w.Run(autopilot, a => a.State == AutopilotState.Fight, 6000);
        Assert.DoesNotContain(JourneyStations.PackHoldArgument, w.Factory.Made.Last(s => s.Kind == StationKinds.DenFight).Arguments.Keys);

        var (w2, lifted) = Climber(tusk: false);
        w2.Trip.Vendor.State.Self.Qualities.Vitals[MageTemplate.MaxMana] = new VitalDesc(MageTemplate.MaxMana, 500, 0, 0, 500);
        w2.Trip.Vendor.State.Self.Qualities.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 100, 100, 0);
        lifted.PackHoldNow = new PackHold(Lodge, 2, true, 100);
        w2.Run(lifted, a => a.State == AutopilotState.Fight, 6000);
        Assert.DoesNotContain(JourneyStations.PackHoldArgument, w2.Factory.Made.Last(s => s.Kind == StationKinds.DenFight).Arguments.Keys);
        Assert.False(lifted.PackHoldNow.Held);
        Assert.Contains(w2.Lines, l => l.Contains("pack: drop-back lifted on tusker-lodge", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_7_AY_the_drop_back_is_written_to_the_checkpoint_and_a_restart_reads_it_back()
    {
        var (w, autopilot) = Climber(tusk: false);
        var store = new MemoryCheckpointStore();
        var hold = new PackHold(Lodge, 2, true, 100_000);
        var first = w.Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp, TripAfterKills = 1, RunTag = "stretch-1" }, store);
        first.BuffsForTest = autopilot.BuffsForTest;
        first.TrophyQuests = autopilot.TrophyQuests;
        first.PackHoldNow = hold;
        w.Run(first, a => a.State == AutopilotState.Fight, 6000);
        Assert.Contains("\"packHold\"", store.Json, StringComparison.Ordinal);

        var restart = w.Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp, TripAfterKills = 1, RunTag = "stretch-2" }, store);
        restart.BuffsForTest = autopilot.BuffsForTest;
        restart.TrophyQuests = autopilot.TrophyQuests;
        w.Run(restart, a => a.State == AutopilotState.Fight, 6000);

        Assert.Equal(hold, restart.PackHoldNow);
        Assert.Contains(w.Lines, l => l.Contains("pack: pack hold: carried from the checkpoint: ground=tusker-lodge deaths=2 held=yes", StringComparison.Ordinal));
        Assert.Equal("yes", w.Factory.Made.Last(s => s.Kind == StationKinds.DenFight).Arguments[JourneyStations.PackHoldArgument]);
    }

    [Fact]
    public void A_held_due_tusk_is_turned_in_from_the_ground_before_the_trip_and_counted_on_the_ladder_line()
    {
        ApWorld? world = null;
        var (w, autopilot) = Climber(tusk: true, spec => TurnIn(spec, world!));
        world = w;
        w.Run(autopilot, a => a.TurnIns > 0 && a.State == AutopilotState.KnownState, 20000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        var kinds = Kinds(w);
        var quest = w.Factory.Made.Single(s => s.Kind == StationKinds.Quest);
        Assert.Equal(("tusk-female", JourneyPlans.FromGround), (quest.Arguments[JourneyPlans.QuestArgument], quest.Arguments[JourneyPlans.FromArgument]));
        Assert.Equal("logistics", kinds[Array.IndexOf(kinds, "quest") + 1]); // the trip the fight stopped for runs after the errand
        Assert.Equal("den-fight", kinds[Array.IndexOf(kinds, "quest") - 1]);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot quest: key=tusk-female due=yes held=1 from=ground why=", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot quest: ended key=tusk-female ok=yes done=yes xp=+100000 timer=saved turn_ins=1", StringComparison.Ordinal));
        Assert.Equal(1, autopilot.TurnIns);
        var done = autopilot.Lines.Last(l => l.StartsWith(LadderLine.Prefix, StringComparison.Ordinal));
        Assert.Contains(" turn_ins=1 health=", done, StringComparison.Ordinal); // M6 7 R: health= follows it
        Assert.Contains(" quests_done=0 ", done, StringComparison.Ordinal); // M6 8 E (m6-ladder-quests-done): a trophy turn-in is not a quest rung's quest, so it counts in turn_ins and never in quests_done: the two fields mean different things
    }

    [Fact]
    public void A_failed_turn_in_stops_by_its_own_name_through_the_recovery_and_is_not_called_again_on_the_way_home()
    {
        var (w, autopilot) = Climber(tusk: true, spec => new ScriptedRun(spec.Name, _ => ScenarioStatus.Failed, () => "key=tusk-female from=ground steps=1/3 done=no xp=+0 reward_line=0 timer=none ok=no fail=go:exit-hop") { Failure = "go:exit-hop: the hop failed" });
        w.Run(autopilot, a => a.State == AutopilotState.Fight && Kinds(w).Count(k => k == "logistics") >= 2, 20000); // the audit's learn trip, then the trip the fight stopped for
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        Assert.Single(Kinds(w), "quest"); // never a loop: the recovery's way home, the next trip and the stop's recall do not call it again
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot quest: key=tusk-female: its turn-in failed this run (failed:quest:tusk-female:go:exit-hop): not called again", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot recovery after quest:tusk-female:go:exit-hop: the quest station tusk-female failed", StringComparison.Ordinal));
        Assert.Contains("logistics", Kinds(w)); // the trip the fight stopped for is still made (ResumeTrip) after the recovery
        Assert.Equal(0, autopilot.TurnIns);
    }

    [Fact]
    public void An_errand_cut_by_a_death_is_not_run_again_the_trip_goes_on_and_the_next_way_home_calls_it_again()
    {
        ApWorld? world = null;
        var made = 0;
        var (w, autopilot) = Climber(tusk: true, spec => ++made == 1
            ? new ScriptedRun(spec.Name, step =>
            {
                if (step == 0)
                {
                    world!.Trip.Vendor.H.R.Bus.Publish(new PlayerDied("You have been slain on the island!", 0, DeathNoticeSource.VictimNotification)); // a death the autopilot did not script, mid-errand
                }

                return null;
            }, () => "key=tusk-female from=ground steps=0/3 done=no ok=no fail=none")
            : TurnIn(spec, world!));
        world = w;
        w.Run(autopilot, a => a.TurnIns > 0 && a.State == AutopilotState.KnownState, 30000);

        var kinds = Kinds(w);
        Assert.Equal(2, kinds.Count(k => k == "quest"));
        var cut = Array.IndexOf(kinds, "quest");
        Assert.Equal("death", kinds[cut + 1]); // the death station first
        Assert.Equal("logistics", kinds[cut + 2]); // then the trip the fight stopped for (ResumeTrip), without the errand
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot quest: the errand tusk-female was cut: it is not run again this trip", StringComparison.Ordinal));
        Assert.Equal(1, autopilot.TurnIns); // the next trip's way home turned it in
    }

    [Fact]
    public void No_tusk_held_runs_the_trip_alone_with_the_refusal_logged()
    {
        var (w, autopilot) = Climber(tusk: false);
        w.Run(autopilot, a => Kinds(w).Count(k => k == "logistics") >= 2 && a.State == AutopilotState.Fight, 20000); // the audit's learn trip, then the fight's

        Assert.DoesNotContain("quest", Kinds(w));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot quest: key=tusk-female due=yes held=0 from=none why=not-held: ", StringComparison.Ordinal));
    }
}
