using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 8 K (the owner, 2026-11-09): the survival layer above every activity. Proved, pure (<see cref="SurvivalRule"/>): the order, the lines from the ladder, a hold runs to the back lines, a hostile near never rests, the leave's four conditions, the fight's inputs carried and not yet decided. Over the scripted ACE: no station is stepped past the layer
/// while her bars call for action, whatever its kind; the hold ends and the same station goes on from the same step; the leave ends by name and goes home; a walk lets go and goes on; the watchdog does not count a hold; an errand starts ready.
/// </summary>
public sealed class SurvivalLayerTests(ITestOutputHelper output)
{
    private const string Outskirts = "holtburg-outskirts";
    private const string Attacker = "Tusker Guard";
    private const uint HealthCurrent = 2;
    private const uint ManaCurrent = 6;
    private static readonly LadderSurvival Lines = new(50, 80, 25, 60, 15, 40, 50, 15, 80, 50, 0);

    private static SurvivalInput Fine() => new() { HealthPercent = 100, ManaPercent = 100, StaminaPercent = 100, HealPayable = true, MayWait = true, MayLeave = true, Attackers = [] };

    // ------------------------------------------------------------------------------------------------ the rule

    [Fact]
    public void Over_every_line_the_activity_goes_on()
    {
        var input = Fine();
        Assert.Equal(SurvivalVerdict.Go, SurvivalRule.Decide(in input, Lines).Verdict);
    }

    [Theory]
    [InlineData(49, 100, 100, SurvivalReason.Heal)]
    [InlineData(100, 100, 14, SurvivalReason.Stamina)]
    [InlineData(100, 24, 100, SurvivalReason.Mana)]
    [InlineData(49, 24, 14, SurvivalReason.Heal)] // health first
    [InlineData(100, 24, 14, SurvivalReason.Stamina)] // a press needs stamina
    public void Under_a_line_she_waits_for_that_bar_health_first(double health, double mana, double stamina, SurvivalReason reason)
    {
        var input = Fine() with { HealthPercent = health, ManaPercent = mana, StaminaPercent = stamina };
        var decided = SurvivalRule.Decide(in input, Lines);
        Assert.Equal((SurvivalVerdict.Wait, reason), (decided.Verdict, decided.Reason));
        Assert.Contains("%", decided.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hold_runs_on_to_the_back_line_and_a_new_wait_starts_only_under_the_wait_line()
    {
        var between = Fine() with { ManaPercent = 40 }; // over the wait line 25, under the back line 60
        Assert.Equal(SurvivalVerdict.Go, SurvivalRule.Decide(in between, Lines).Verdict);
        var held = between with { Held = true };
        Assert.Equal(SurvivalVerdict.Wait, SurvivalRule.Decide(in held, Lines).Verdict);
        var over = held with { ManaPercent = 60 };
        Assert.Equal(SurvivalVerdict.Go, SurvivalRule.Decide(in over, Lines).Verdict);
    }

    [Fact]
    public void A_hostile_near_is_never_rested_among_the_activity_fights_back()
    {
        var input = Fine() with { ManaPercent = 5, HostileNear = true };
        Assert.Equal(SurvivalVerdict.Go, SurvivalRule.Decide(in input, Lines).Verdict);
    }

    [Fact]
    public void A_state_the_layer_may_not_hold_goes_on()
    {
        var input = Fine() with { ManaPercent = 5, MayWait = false };
        Assert.Equal(SurvivalVerdict.Go, SurvivalRule.Decide(in input, Lines).Verdict);
    }

    [Fact]
    public void She_leaves_attacked_under_both_lines_with_nothing_payable()
    {
        var input = Fine() with { HealthPercent = 40, ManaPercent = 10, Attacked = true, HostileNear = true, HealPayable = false };
        var decided = SurvivalRule.Decide(in input, Lines);
        Assert.Equal((SurvivalVerdict.Leave, SurvivalReason.NoMana), (decided.Verdict, decided.Reason));
    }

    [Theory]
    [InlineData(false, false, 40, 10, true)] // not attacked
    [InlineData(true, true, 40, 10, true)] // a heal is payable
    [InlineData(true, false, 60, 10, true)] // health over its line
    [InlineData(true, false, 40, 20, true)] // mana over its line
    [InlineData(true, false, 40, 10, false)] // the state may not be left
    public void Each_condition_of_the_leave_is_needed(bool attacked, bool payable, double health, double mana, bool mayLeave)
    {
        var input = Fine() with { HealthPercent = health, ManaPercent = mana, Attacked = attacked, HostileNear = attacked, HealPayable = payable, MayLeave = mayLeave };
        Assert.NotEqual(SurvivalVerdict.Leave, SurvivalRule.Decide(in input, Lines).Verdict);
    }

    [Fact]
    public void The_guards_own_line_counts_as_the_health_line_of_the_leave()
    {
        // round 2 at 20:19:25 UTC: health 18 of 22, the line raised by the incoming rate, nothing payable
        var input = Fine() with { HealthPercent = 82, ManaPercent = 0, Attacked = true, HostileNear = true, HealPayable = false, GuardNothingPaid = true };
        Assert.Equal(SurvivalVerdict.Leave, SurvivalRule.Decide(in input, Lines).Verdict);
    }

    [Fact]
    public void The_fights_inputs_are_carried_and_the_fight_gets_the_layers_own_answer()
    {
        // M6 16 E3.0: the rule answers a fight (SurvivalRuleFightTests holds the rule); before, a fight got Go whatever its inputs (TODO(m6-survival-fight))
        var attackers = new[] { new SurvivalAttacker(0x80000739, "Drudge Skulker", 3.5, 0.4f, 120u), new SurvivalAttacker(0x8000073A, "Drudge Skulker", 9, null, null) };
        var input = Fine() with { InFight = true, HealthPercent = 5, ManaPercent = 1, Attacked = true, HostileNear = true, HealPayable = false, Attackers = attackers };
        var decided = SurvivalRule.Decide(in input, Lines);

        Assert.Equal((SurvivalVerdict.Leave, SurvivalReason.NoMana, SurvivalRule.DieLeaves), (decided.Verdict, decided.Reason, decided.Why)); // about to die, nothing payable, attacked, under both of the ladder's leave lines
        Assert.Equal(2, input.Attackers.Count);
        Assert.Equal(120u, input.Attackers[0].Stamina);
        Assert.Null(input.Attackers[1].HealthShare); // never read as full (D92)
        var drain = new SurvivalDecision(SurvivalVerdict.TakeMana, SurvivalReason.Mana, "the best sum is a drain", attackers[0].Guid);
        Assert.Equal(0x80000739u, drain.DrainTarget);
        Assert.Contains(SurvivalVerdict.Heal, Enum.GetValues<SurvivalVerdict>());
    }

    [Fact]
    public void Every_state_gets_the_wait_but_the_three_that_keep_their_own_bars()
    {
        var own = Enum.GetValues<AutopilotState>().Where(SurvivalRule.KeepsItsOwnBars).ToArray();
        Assert.Equal([AutopilotState.Buff, AutopilotState.Fight, AutopilotState.Upkeep], own.OrderBy(s => s.ToString()).ToArray());
    }

    [Fact]
    public void The_lines_are_the_ladders_and_a_wait_line_is_under_its_back_line()
    {
        var s = LadderLoader.WarMage.Survival;
        Assert.True(s.WaitHealth > 0 && s.WaitHealth < s.BackHealth);
        Assert.True(s.WaitMana > 0 && s.WaitMana < s.BackMana);
        Assert.True(s.WaitStamina > 0 && s.WaitStamina < s.BackStamina);
        Assert.True(s.LeaveMana <= s.WaitMana);
    }

    // ------------------------------------------------------------------------------------------------ over the scripted ACE

    /// <summary>A station that counts its steps and never ends (or ends when told); a kind the layer has never heard of is the same to it.</summary>
    private sealed class CountingRun(string name) : IStationRun, IStationHeld, IStationCommitted
    {
        public int Steps { get; private set; }

        public double Held { get; private set; }

        public bool Finish { get; set; }

        public string? Committed { get; set; }

        public string Name => name;

        public string Stage => "counting";

        public string? Failure => null;

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public bool Ok => Finish;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            Steps++;
            return Finish ? ScenarioStatus.Succeeded : ScenarioStatus.Running;
        }

        public string VerdictFields() => "sold=0 bought=0";

        public void NoteHeld(double seconds) => Held += seconds;
    }

    private sealed class Bars(ApWorld w)
    {
        private byte _sequence;

        public void Mana(uint points) => w.Trip.Vendor.R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(++_sequence, ManaCurrent, points));

        public void Health(uint points) => w.Trip.Vendor.R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(++_sequence, HealthCurrent, points));

        public void Hit() => w.Trip.Vendor.R.Server((p, seq) => AceM3Writers.DefenderNotification(p, seq, Attacker, 4, 0.1, 1, 4, false, 0));
    }

    /// <summary>The climber on the outskirts, vitals read (each maximum 100), a real keeper, the logistics station the one <paramref name="run"/> and named as the test says.</summary>
    private ApWorld Climber(CountingRun run, out Bars bars, out AutopilotScenario autopilot)
    {
        ApWorld? world = null;
        var w = new ApWorld(Outskirts, output, atHome: false) { KeeperMaker = c => new VitalsKeeper(world!.Trip.View, c.Spells, new VitalsOptions(), c.Log) };
        world = w;
        var r = w.Trip.Vendor.H.R;
        r.Reducer.OnPlayerDescription(
            r.Context,
            new GameEventHeader(WorldActionRig.Player, 90, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { Inventory = [.. w.Trip.View.State.Self.Inventory.Pack.Select(e => new ContentDesc(e.Guid, e.ContainerType))], Vitals = [new VitalDesc(1, 100, 0, 0, 100), new VitalDesc(3, 100, 0, 0, 100), new VitalDesc(5, 100, 0, 0, 100)] });
        w.Level(5);
        w.Trip.Vendor.State.Self.Spellbook.IsKnown = true;
        KnowSkills(w);
        w.Factory.Script = spec => spec.Kind switch
        {
            StationKinds.Logistics => run,
            StationKinds.RungChange => new ScriptedRun(spec.Name, _ => // the outskirts' way home is on foot: the leave takes it
            {
                w.Trip.Vendor.StandAt(w.Ground.Home.LifestoneCell, w.Ground.Home.LifestonePoint);
                return ScenarioStatus.Succeeded;
            }, () => $"ground={Outskirts} ok=yes fail=none"),
            _ => null,
        };
        bars = new Bars(w);
        autopilot = w.Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp, TripAfterKills = 1 });
        return w;
    }

    private static void ToTheStation(ApWorld w, AutopilotScenario autopilot, CountingRun run)
    {
        w.Run(autopilot, a => a.State == AutopilotState.Trip && run.Steps >= 2);
        Assert.Equal(AutopilotState.Trip, autopilot.State);
    }

    [Theory]
    [InlineData("logistics")]
    [InlineData("a-station-kind-added-later")] // the layer reads no station's name or kind
    public void No_station_is_stepped_past_the_layer_while_her_bars_call_for_action_and_it_resumes_from_the_same_step(string name)
    {
        var run = new CountingRun(name);
        var w = Climber(run, out var bars, out var autopilot);
        ToTheStation(w, autopilot, run);

        var before = run.Steps;
        bars.Mana(10); // under the ladder's wait line, no hostile on her
        var heldFrom = w.Now;
        w.Run(autopilot, _ => w.Now >= heldFrom + 20);

        Assert.InRange(run.Steps, before, before + 1); // at most the frame the update was still on its way
        var stopped = run.Steps;
        Assert.Contains(w.Lines, l => l.StartsWith("survival: trip: wait: mana: ", StringComparison.Ordinal) && l.Contains(" station is held", StringComparison.Ordinal));
        Assert.Equal(1, autopilot.SurvivalHolds);

        bars.Mana(100);
        w.Run(autopilot, _ => run.Steps > stopped + 2, 4000);

        Assert.True(run.Steps > stopped + 2); // the same run object goes on: not started again
        Assert.Equal(AutopilotState.Trip, autopilot.State);
        Assert.Single(w.Factory.Made, s => s.Kind == StationKinds.Logistics);
        Assert.Contains(w.Lines, l => l.StartsWith("survival: trip: go: ", StringComparison.Ordinal) && l.Contains("held ", StringComparison.Ordinal));
        Assert.True(run.Held >= 15); // told how long, so its own waits do not count it
    }

    [Fact]
    public void A_walk_in_flight_lets_go_when_a_hold_begins_and_goes_on_when_it_ends()
    {
        var run = new CountingRun("logistics");
        var w = Climber(run, out var bars, out var autopilot);
        ToTheStation(w, autopilot, run);

        bars.Mana(10);
        w.Run(autopilot, _ => w.Trip.Travel.WalkHolds == 1, 400);
        Assert.Equal((1, 0), (w.Trip.Travel.WalkHolds, w.Trip.Travel.WalkReleases));

        bars.Mana(100);
        w.Run(autopilot, _ => w.Trip.Travel.WalkReleases == 1, 4000);
        Assert.Equal((1, 1), (w.Trip.Travel.WalkHolds, w.Trip.Travel.WalkReleases));
    }

    private sealed class RecordingLeg : ITravelLeg
    {
        public int Pauses { get; private set; }

        public int Resumes { get; private set; }

        public TravelMeans Means => TravelMeans.Run;

        public string? Failure => null;

        public string Fields => string.Empty;

        public TravelStatus Step(IGameSessionView session, double now) => TravelStatus.Running;

        public void Pause() => Pauses++;

        public void Resume(double now) => Resumes++;

        public void Dispose()
        {
        }
    }

    private sealed class OneLeg(ITravelLeg leg) : IJourneyRunnerHost
    {
        public ITravelLeg Run(IGameSessionView session, TravelRequest request, double now) => leg;
    }

    [Fact]
    public void The_travel_seam_pauses_the_walk_in_flight_and_resumes_the_same_one()
    {
        var w = new ApWorld(Outskirts, output, atHome: false);
        var leg = new RecordingLeg();
        var travel = new JourneyTravel("Winnie", new JourneyFacts(), Lifestone.Client.Diagnostics.NullLogSink.Instance, new OneLeg(leg));
        var walk = travel.GoTo(w.Trip.View, new TravelRequest(JourneyGrounds.Holtburg, TravelPurpose.Vendor), w.Now);

        travel.HoldWalk();
        Assert.Equal((1, 0), (leg.Pauses, leg.Resumes));
        travel.ReleaseWalk(w.Now);
        Assert.Equal((1, 1), (leg.Pauses, leg.Resumes));
        Assert.Equal(TravelStatus.Running, walk.Step(w.Trip.View, w.Now));
        travel.ReleaseWalk(w.Now);
        Assert.Equal(1, leg.Resumes); // nothing held: nothing resumed
    }

    [Fact]
    public void A_portal_use_in_flight_stands_the_layer_down_by_name()
    {
        var run = new CountingRun("logistics");
        var w = Climber(run, out var bars, out var autopilot);
        ToTheStation(w, autopilot, run);

        run.Committed = "the hop outskirts:use";
        bars.Mana(10);
        var before = run.Steps;
        var from = w.Now;
        w.Run(autopilot, _ => w.Now >= from + 5);

        Assert.True(run.Steps > before + 2);
        Assert.Equal(0, autopilot.SurvivalHolds);
        Assert.Contains(w.Lines, l => l == "survival: trip: stand-down: the hop outskirts:use");
    }

    [Fact]
    public void A_hold_is_no_lack_of_progress_for_the_trips_watchdog()
    {
        var run = new CountingRun("logistics");
        var w = Climber(run, out var bars, out var autopilot);
        ToTheStation(w, autopilot, run);

        bars.Mana(10);
        var from = w.Now;
        w.Run(autopilot, _ => w.Now >= from + AutopilotWatchdogs.BaseSeconds(AutopilotState.Trip) + 60, 8000);

        Assert.True(w.Now >= from + AutopilotWatchdogs.BaseSeconds(AutopilotState.Trip));
        Assert.DoesNotContain(w.Lines, l => l.Contains("watchdog: trip over", StringComparison.Ordinal));
    }

    [Fact]
    public void A_wait_under_way_in_a_script_leg_has_as_long_left_after_a_hold()
    {
        var w = new ApWorld(Outskirts, output, atHome: false);
        var leg = new WaitingLeg();
        leg.Step(w.Trip.View, w.Now);
        w.Trip.Vendor.R.Tick(8);
        leg.NoteHeld(8); // eight seconds held: the ten-second wait has spent none of them
        Assert.Equal(ScenarioStatus.Running, leg.Step(w.Trip.View, w.Now));
        w.Trip.Vendor.R.Tick(11);
        leg.Step(w.Trip.View, w.Now);
        Assert.True(leg.Waited);
    }

    private sealed class WaitingLeg() : ScriptLeg("waiting", _ => { })
    {
        public bool Waited { get; private set; }

        protected override IEnumerable<ActionHandle?> Script()
        {
            foreach (var h in Until(() => false, 10))
            {
                yield return h;
            }

            Waited = true;
            for (; ; )
            {
                yield return null;
            }
        }
    }

    [Fact]
    public void Attacked_with_nothing_payable_the_trip_ends_by_name_and_she_goes_home()
    {
        var run = new CountingRun("logistics");
        var w = Climber(run, out var bars, out var autopilot);
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        ToTheStation(w, autopilot, run);

        bars.Health(40);
        bars.Mana(5);
        bars.Hit();
        w.Run(autopilot, a => a.State != AutopilotState.Trip, 400);

        Assert.Contains(w.Lines, l => l.StartsWith("survival: trip: leave: nomana: ", StringComparison.Ordinal) && l.Contains("failed:trip:logistics:retreat:no-mana", StringComparison.Ordinal));
        Assert.Equal(1, autopilot.SurvivalLeaves);
        Assert.Equal(1, autopilot.Retreats);
        Assert.Contains(arrows, a => a.From == AutopilotState.Trip && a.Why.StartsWith("retreat: ", StringComparison.Ordinal));
    }

    /// <summary>
    /// M6 16 E2b (the one world picture): the attackers the layer reads for its leave line (the creatures of the last attacker's name within the watch's reach) are counted where the session draws them. The count is in
    /// the line's words only: no verdict of the rule reads it.
    /// </summary>
    [Theory]
    [InlineData(false, "0 attacker(s) read")] // the report alone: the attacker was last reported 60 m off, past the watch's reach
    [InlineData(true, "1 attacker(s) read")] // its body is drawn 4 m from her
    public void The_leave_line_counts_an_attacker_where_it_is_drawn(bool drawn, string count)
    {
        const uint guard = 0x80003001;
        var run = new CountingRun("logistics");
        var w = Climber(run, out var bars, out var autopilot);
        ToTheStation(w, autopilot, run);
        var here = w.Trip.View.State.Self.Position!.Value;
        var r = w.Trip.Vendor.H.R;
        var basis = ReducerHarness.Create(guard, ReducerHarness.Stamps(), ReducerHarness.Position(here.CellId, here.Frame.Origin.X + 60, here.Frame.Origin.Y, here.Frame.Origin.Z), name: Attacker);
        r.Reducer.OnCreateObject(r.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, ObjectDescriptionFlags = WorldObjectTable.AttackableFlag } }, isUpdate: false);
        if (drawn)
        {
            w.Trip.View.Live = new Lifestone.Client.Tests.Session.Contracts.ScriptedBodies().StandEast(guard, here, 4).Over(w.Trip.View.Clock);
        }

        bars.Health(40);
        bars.Mana(5);
        bars.Hit();
        w.Run(autopilot, a => a.State != AutopilotState.Trip, 400);

        var leave = Assert.Single(w.Lines, l => l.StartsWith("survival: trip: leave: nomana: ", StringComparison.Ordinal));
        Assert.Contains(count, leave, StringComparison.Ordinal);
        Assert.Equal(1, autopilot.SurvivalLeaves); // the leave itself is the same either way
    }

    [Fact]
    public void A_ladder_errand_left_for_no_mana_is_failed_by_its_key_and_not_called_again()
    {
        var run = new CountingRun("quest");
        var (w, autopilot) = TrophyQuestTests.MakeClimber(output, tusk: false, _ => run, prepare: ReadVitals);
        autopilot.TrophyQuests = [LadderErrandForTest()];
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Trip && run.Steps >= 2, 20000);
        var bars = new Bars(w);

        bars.Health(40);
        bars.Mana(5);
        bars.Hit();
        w.Run(autopilot, a => a.State != AutopilotState.Trip, 3000, _ =>
        {
            AnswerWieldsAndModes(w);
            bars.Hit();
        });

        Assert.Contains(w.Lines, l => l.Contains("failed:quest:survival-test:retreat:no-mana", StringComparison.Ordinal));
        Assert.Equal(1, autopilot.SurvivalLeaves);
        Assert.Contains(arrows, a => a.Why.StartsWith("retreat: failed:quest:survival-test:retreat:no-mana", StringComparison.Ordinal));
    }

    /// <summary>Long round 3: she left Nen Ai's errand for no mana, the way home failed twice and the climb ended, with /lifestone never armed, because only a failed station armed it. A leave out of a ladder errand is a failed errand (D120).</summary>
    [Fact]
    public void A_ladder_errand_left_for_no_mana_whose_way_home_fails_takes_the_last_resort()
    {
        var run = new CountingRun("quest");
        var (w, autopilot) = TrophyQuestTests.MakeClimber(output, tusk: false, _ => run, prepare: ReadVitals);
        autopilot.TrophyQuests = [LadderErrandForTest()];
        var script = w.Factory.Script!;
        w.Factory.Script = spec => spec.Kind == StationKinds.RungChange ? null : script(spec); // the climber's script walks every rung change home: here it fails
        w.Factory.FailWith = spec => spec.Name is "recall-home" or "rung-change-home" ? spec.Name + ": scripted: the way home failed" : null;
        w.Run(autopilot, a => a.State == AutopilotState.Trip && run.Steps >= 2, 20000);
        var bars = new Bars(w);

        bars.Health(40);
        bars.Mana(5);
        bars.Hit();
        w.Run(autopilot, a => a.State == AutopilotState.Travel, 3000, _ => AnswerWieldsAndModes(w));
        Assert.Equal(AutopilotState.Travel, autopilot.State);
        bars.Health(100); // over the vitals guard's line, which would hold the way home for a heal this world never answers
        w.Run(autopilot, _ => w.Trip.Vendor.H.Net.Of(FakeSendKind.TeleToLifestone).Any(), 6000, _ => AnswerWieldsAndModes(w));

        Assert.Single(w.Trip.Vendor.H.Net.Of(FakeSendKind.TeleToLifestone));
        Assert.Contains(w.Lines, l => l.StartsWith("survival: ", StringComparison.Ordinal) && l.Contains("leave: lifestone-recall: the walk ", StringComparison.Ordinal) && l.Contains("the ladder errand survival-test", StringComparison.Ordinal));
        Assert.Null(autopilot.StopName);
    }

    private static Lifestone.Client.Session.Quests.Quest LadderErrandForTest()
    {
        var place = new Lifestone.Client.Session.Quests.QuestPlace(GroundCatalog.Get("tusker-burrow").Home.LifestoneCell, System.Numerics.Vector3.Zero, "the errand's start");
        return new Lifestone.Client.Session.Quests.Quest
        {
            Key = "survival-test",
            Name = "A test errand",
            Giver = null,
            Start = place,
            Steps = [TrophyQuestTests.Step(0, Lifestone.Client.Session.Quests.QuestStepKind.Go, "go to the giver") with { Place = place }],
            Done = new Lifestone.Client.Session.Quests.QuestDone(null, "Thank you.", null),
            TimerSeconds = null,
            Sources = ["the test's own record"],
            Ladder = new Lifestone.Client.Session.Quests.QuestLadder(10, 30, true),
        };
    }

    private static void ReadVitals(ApWorld w)
    {
        var r = w.Trip.Vendor.H.R;
        var book = w.Trip.Vendor.State.Self.Spellbook;
        r.Reducer.OnPlayerDescription(
            r.Context,
            new GameEventHeader(WorldActionRig.Player, 90, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { Inventory = [.. w.Trip.View.State.Self.Inventory.Pack.Select(e => new ContentDesc(e.Guid, e.ContainerType))], Vitals = [new VitalDesc(1, 100, 0, 0, 100), new VitalDesc(3, 100, 0, 0, 100), new VitalDesc(5, 100, 0, 0, 100)] });
        book.IsKnown = true;
        KnowSkills(w);
    }

    /// <summary>Life Magic read, so "nothing payable" is a read and not an unknown; Item Enchantment for the rung gates (a description replaces the qualities).</summary>
    private static void KnowSkills(ApWorld w)
    {
        var skills = w.Trip.Vendor.State.Self.Qualities.Skills;
        skills[MageTemplate.LifeMagic] = new SkillDesc(MageTemplate.LifeMagic, 21, 1, 2u, 0, 0u, 0, 0);
        skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
    }


    [Fact]
    public void A_cycles_ladder_line_carries_the_holds_and_the_leaves()
    {
        Assert.Equal(["corpses_lost", "survival_holds", "survival_leaves", "errands"], LadderLine.FieldNames.SkipWhile(f => f != "corpses_lost").Take(4));
        var text = new LadderLine("cycle", 1, 6, "outskirts", Outskirts, new AutopilotTally(), 0, null, null, SurvivalHolds: 3, SurvivalLeaves: 1).Text();
        Assert.Contains(" survival_holds=3 survival_leaves=1 errands=", text, StringComparison.Ordinal);
    }

    private (ApWorld World, AutopilotScenario Autopilot, CountingRun Run) ReadyClimber()
    {
        var run = new CountingRun("quest");
        var (w, _) = TrophyQuestTests.MakeClimber(output, tusk: false, _ => run, prepare: ReadVitals);
        var autopilot = new AutopilotScenario(new AutopilotOptions { Job = AutopilotJob.LevelUp, TripAfterKills = 1 }, w.Factory, KeeperContext(w));
        autopilot.BuffsForTest = new RungBuffRead(static _ => 8, static _ => 4);
        autopilot.TrophyQuests = [LadderErrandForTest()];
        return (w, autopilot, run);
    }

    [Fact]
    public void A_ladder_errand_with_no_enchantment_up_does_not_start_and_the_trip_goes_on()
    {
        var (w, autopilot, run) = ReadyClimber();
        w.Run(autopilot, _ => w.Factory.Made.Any(s => s.Kind == StationKinds.Logistics) && w.Lines.Any(l => l.Contains("wait: buff: no enchantment stands before the ladder errand survival-test", StringComparison.Ordinal)), 20000);

        Assert.Contains(w.Lines, l => l.Contains("wait: buff: no enchantment stands before the ladder errand survival-test", StringComparison.Ordinal));
        Assert.Equal(0, run.Steps);
        Assert.DoesNotContain(w.Factory.Made, s => s.Kind == StationKinds.Quest);
    }

    [Fact]
    public void A_ladder_errand_starts_with_the_needs_of_the_ladder()
    {
        var (w, autopilot, _) = ReadyClimber();
        w.Trip.Vendor.State.Self.Qualities.Enchantments = [new(666, 1, 666, 0, 666, 0, 1800.0, 0x50000001, 0f, 0f, 0.0, EnchantmentRules.Multiplicative, 0, 1f, 0, EnchantmentBucket.Vitae)];
        w.Run(autopilot, _ => w.Factory.Made.Any(s => s.Kind == StationKinds.Quest), 20000);

        var spec = w.Factory.Made.First(s => s.Kind == StationKinds.Quest);
        var lines = LadderLoader.WarMage.Survival;
        Assert.Equal(new StationNeeds(StationWhere.Here, Peace: true, MinHealthPercent: lines.StartHealth, MinStaminaPercent: lines.StartStamina, MinManaPercent: lines.StartMana, Buffed: true), spec.NeedsOverride);
    }

    private static JourneyContext KeeperContext(ApWorld w)
    {
        var c = w.Trip.Context();
        return new JourneyContext { Character = c.Character, Account = c.Account, Log = c.Log, Travel = c.Travel, Spells = c.Spells, Dungeon = c.Dungeon, Quests = c.Quests, Keeper = new VitalsKeeper(w.Trip.View, c.Spells, new VitalsOptions(), c.Log) };
    }
}
