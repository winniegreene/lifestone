using System.Globalization;
using System.Numerics;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Journey;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M5 item 4 a1 (D57): the autopilot over the scripted ACE on both dungeon profiles (the den's Holtburg, the Tusker Temple's Fort Tethana). The world is the logistics station's scripted one (<see cref="LogisticsStationTests.Trip"/>: the
/// ground's home vendors scripted from their <c>sells</c>, the pack held from the ground's own stock lines and purse, a scripted experience server), the logistics trip is the journey's real station made by the real catalog
/// (<see cref="JourneyStations.Create(JourneyPlan, IReadOnlyList{string}, Lifestone.Data.Dat.DatSet?, string?)"/>), and the buff, the travels, the fight and the death are scripted stations that move the player as theirs do
/// (the fight counts a kill every two seconds and takes the external stop). Proved: every arrow (home, the ground, elsewhere, the death seen in any state), each stop rule (the minutes, the level, a request), the forced
/// trip, the counters' trip and stop, the scripted death and back, the cycle and done lines' fields in order; and the real fight's <see cref="FightScenario.RequestStop"/>, the catalog's open-ended fight and trip specs, the options and
/// their registration.
/// </summary>
public sealed class AutopilotTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_scripted_run_makes_a_cycle_by_the_scripted_death_and_one_by_the_forced_trip_then_stops_on_request(string key)
    {
        var w = new ApWorld(key, output);
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = 3, TestDeath = true });
        var status = w.Run(autopilot, a => a.Cycles == 2 && a.State == AutopilotState.Fight);
        Assert.Equal(ScenarioStatus.Running, status);
        autopilot.RequestStop("test");
        status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        // home: buff, travel, fight; the death and back; the ground with no enchantment up (the scripted buff casts none): buff, fight; the forced trip and back; buff, fight
        Assert.Equal(["bind", "buff", "den-travel", "den-fight", "death", "buff", "den-fight", "logistics", "buff", "den-fight", "recall-home"], Kinds(w)); // the start-up audit binds at home first (M5 7: the bind is not proven this session); the stop on the ground recalls home first (M5 7 F18)
        Assert.Equal(JourneyPlans.TripForced, w.Factory.Made.Single(s => s.Kind == StationKinds.Logistics).Arguments[JourneyPlans.TripArgument]);
        Assert.Equal([JourneyStations.DeathNoFightArgument, JourneyStations.UnattendedArgument], w.Factory.Made.Single(s => s.Kind == StationKinds.Death).Arguments.Keys.Order(StringComparer.Ordinal)); // M5 7 F25: unattended, its corpse-trip fights hold the magic mode
        var lines = autopilot.Lines;
        Assert.Equal(4, lines.Count); // two cycles, the stopped one, the done line
        Assert.StartsWith("autopilot cycle n=1 state=death ", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("autopilot cycle n=2 state=trip ", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("autopilot cycle n=3 state=stopped ", lines[2], StringComparison.Ordinal);
        Assert.Equal((1, 1, 0), (Int(lines[0], "kills"), Int(lines[0], "deaths"), Int(lines[0], "trips"))); // the scripted death after the first kill
        Assert.Equal((3, 0, 1), (Int(lines[1], "kills"), Int(lines[1], "deaths"), Int(lines[1], "trips"))); // the forced trip after three kills from the cycle's start
        Assert.Equal(3000, Int(lines[1], "xp"));
        Assert.True(Int(lines[1], "spent_xp") > 0, lines[1]); // the real station's experience calls reach the cycle line
        Assert.Equal(0, Int(lines[1], "bought")); // the pack holds every line of the ground's kit at its keep: the forced trip has nothing to buy (the counters' own trip below buys)
        Assert.Contains(w.Lines, l => l.Contains("trigger=forced", StringComparison.Ordinal));
        var done = lines[3];
        Assert.StartsWith("autopilot done ok=yes cycles=2 stop=request:test ", done, StringComparison.Ordinal);
        Assert.Equal(DoneNames, Names(done, "autopilot done "));
        Assert.Equal(CycleNames, Names(lines[0], "autopilot cycle "));
        Assert.Equal((1, 1, 0, 0), (Int(done, "deaths"), Int(done, "trips"), Int(done, "granted"), Int(done, "places")));
        Assert.Equal(Int(lines[0], "kills") + Int(lines[1], "kills") + Int(lines[2], "kills"), Int(done, "kills"));
        Assert.Equal((0, 0, 0), (Int(done, "interrupted"), Int(done, "resumed"), Int(done, "reconnects"))); // a2's and h2's fields read 0, never omitted
        Assert.Equal("2", Field(done, "storeys_visited"));
        Assert.Equal(0, w.Trip.ModVerbsSent); // nothing granted, nothing placed
        Assert.True(autopilot.Ok);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void Every_arrow_is_seen_by_the_state_hook_and_elsewhere_recalls_home_once(string key)
    {
        var w = new ApWorld(key, output);
        w.Trip.Vendor.StandAt(0x7D640013, new Vector3(12, 12, 0)); // neither the ground, its home, nor Holtburg
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 });
        autopilot.RequestStop("key");
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(["recall-home", "buff", "den-travel", "den-fight", "recall-home"], Kinds(w)); // the last: the recall home before the stop (M5 7 F18)
        Assert.Equal(
            [AutopilotState.KnownState, AutopilotState.Travel, AutopilotState.KnownState, AutopilotState.Buff, AutopilotState.Travel, AutopilotState.Fight, AutopilotState.Travel, AutopilotState.Stopped],
            arrows.Select(a => a.To));
        Assert.Contains(w.Lines, l => l.Contains("known-state place=elsewhere", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("known-state place=home", StringComparison.Ordinal));
        Assert.StartsWith("autopilot done ok=yes cycles=0 stop=request:key ", autopilot.Lines[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_recall_home_refused_with_0x049E_binds_once_at_the_grounds_home_and_the_recall_then_lands_M5_7_F14(string key)
    {
        var w = new ApWorld(key, output);
        w.Factory.RefuseRecallUntilBound = true;
        w.Trip.Vendor.StandAt(0x7D640013, new Vector3(12, 12, 0)); // neither the ground, its home, nor Holtburg
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 });
        autopilot.RequestStop("key");
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(["recall-home", "bind", "buff", "den-travel", "den-fight", "recall-home"], Kinds(w)); // the refused recall, the bind, then the arrows of a mage at home
        Assert.Equal(1, Kinds(w).Count(k => k == "bind"));
        Assert.True(w.Factory.Bound);
        Assert.Contains(w.Lines, l => l.Contains($"autopilot: no lifestone bind: binding at {w.Trip.Ground.Home.Town} first", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("autopilot recovery after", StringComparison.Ordinal)); // a bind, not a recovery
        Assert.Contains(arrows, a => a.To == AutopilotState.Travel && a.Why.Contains("no lifestone bind", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("known-state place=home", StringComparison.Ordinal));
        Assert.StartsWith("autopilot done ok=yes cycles=0 stop=request:key ", autopilot.Lines[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_second_0x049E_after_the_bind_stops_by_name_and_binds_no_second_time_M5_7_F14(string key)
    {
        var w = new ApWorld(key, output);
        w.Factory.RefuseRecallUntilBound = true;
        w.Factory.BindTiesNothing = true; // the bind "succeeds" and the mage still stands elsewhere
        w.Trip.Vendor.StandAt(0x7D640013, new Vector3(12, 12, 0));
        var autopilot = w.Autopilot(new AutopilotOptions());
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(AutopilotScenario.NoBindStop, autopilot.StopName);
        Assert.Equal(["recall-home", "bind", "recall-home"], Kinds(w));
        Assert.False(autopilot.Ok);
        Assert.StartsWith($"autopilot done ok=no cycles=0 stop={AutopilotScenario.NoBindStop} ", autopilot.Lines[^1], StringComparison.Ordinal);
        Assert.Single(w.Lines, l => l.Contains("autopilot: no lifestone bind: binding at", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_mage_that_is_bound_never_runs_the_bind_station_M5_7_F14(string key)
    {
        var w = new ApWorld(key, output);
        w.Trip.Vendor.StandAt(0x7D640013, new Vector3(12, 12, 0));
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 });
        autopilot.RequestStop("key");
        w.Run(autopilot);

        Assert.DoesNotContain("bind", Kinds(w));
        Assert.DoesNotContain(w.Lines, l => l.Contains("no lifestone bind", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void The_minutes_stop_the_fight_cleanly_on_the_session_clock(string key)
    {
        var w = new ApWorld(key, output, atHome: false);
        var start = w.Now;
        var autopilot = w.Autopilot(new AutopilotOptions { Minutes = 1 });
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.InRange(w.Now - start, 60, 62);
        var done = autopilot.Lines[^1];
        Assert.StartsWith("autopilot done ok=yes cycles=0 stop=minutes ", done, StringComparison.Ordinal);
        Assert.InRange(Int(done, "kills"), 28, 31);
        Assert.Contains(w.Lines, l => l.Contains("stop asked (stop-rule)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void The_level_read_from_the_state_stops_the_autopilot(string key)
    {
        var w = new ApWorld(key, output, atHome: false, onKill: (world, kills) =>
        {
            if (kills == 4)
            {
                world.Level(80);
            }
        });
        var autopilot = w.Autopilot(new AutopilotOptions { Level = 80 });
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        var done = autopilot.Lines[^1];
        Assert.StartsWith("autopilot done ok=yes cycles=0 stop=level ", done, StringComparison.Ordinal);
        Assert.Equal(4, Int(done, "kills"));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void Without_the_force_the_counters_trip_ends_a_cycle_and_back_in_the_ground_the_fight_goes_on(string key)
    {
        var ground = GroundCatalog.Get(key);
        var low = ground.Supplies.Stock.First(l => !ground.IsCarried(l.Wcid));
        var lowered = false;
        var w = new ApWorld(key, output, atHome: false, onKill: (world, _) => // M5 7: the pack is full at the start (a short line there is the start-up audit's trip); the fight uses the line up
        {
            if (!lowered)
            {
                lowered = true;
                world.SetHeld(low.Wcid, Math.Max(low.StopUnder, low.TripUnder - 1));
            }
        });
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.Cycles == 1 && a.State == AutopilotState.Fight);
        autopilot.RequestStop("test");
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(["buff", "den-fight", "logistics", "buff", "den-fight", "recall-home"], Kinds(w)); // a cold start in the ground with no enchantment up buffs there first
        Assert.Equal(JourneyStations.TripByCounters, w.Factory.Made[2].Arguments[JourneyPlans.TripArgument]);
        Assert.Contains(w.Lines, l => l.Contains("stop asked (logistics-trip)", StringComparison.Ordinal));
        Assert.StartsWith("autopilot cycle n=1 state=trip ", autopilot.Lines[0], StringComparison.Ordinal);
        Assert.Equal(1, Int(autopilot.Lines[0], "trips"));
        Assert.True(Int(autopilot.Lines[0], "bought") >= 1, autopilot.Lines[0]); // the low line bought back at the ground's own vendor
        Assert.Equal(AutopilotPlace.Home, AutopilotScenario.Classify(w.Trip.Vendor.H.State, ground)); // stopped at home: the recall home came before the stop (M5 7 F18)
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void The_counters_stop_ends_the_autopilot_by_name_and_no_trip_is_made(string key)
    {
        var ground = GroundCatalog.Get(key);
        var stop = ground.Supplies.Stock.First(l => l.StopUnder > 0);
        var lowered = false;
        var w = new ApWorld(key, output, atHome: false, onKill: (world, _) => // M5 7: the pack is full at the start (the start-up audit judges a start short); the fight uses the line up past its stop line
        {
            if (!lowered)
            {
                lowered = true;
                world.SetHeld(stop.Wcid, stop.StopUnder - 1);
            }
        });
        var autopilot = w.Autopilot(new AutopilotOptions());
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.StartsWith("logistics-stop:", autopilot.StopName, StringComparison.Ordinal);
        Assert.DoesNotContain(w.Factory.Made, s => s.Kind == StationKinds.Logistics);
        Assert.StartsWith("autopilot done ok=no cycles=0 stop=logistics-stop:", autopilot.Lines[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_death_the_autopilot_did_not_script_is_handled_by_the_death_station_after_the_respawn_and_the_fight_goes_on(string key)
    {
        var died = false;
        var w = new ApWorld(key, output, atHome: false, onKill: (world, kills) =>
        {
            if (kills == 2 && !died)
            {
                died = true;
                world.Trip.Vendor.H.R.Bus.Publish(new PlayerDied("You have been slain by a Tusker Guard!", 0, DeathNoticeSource.VictimNotification));
            }
        });
        var autopilot = w.Autopilot(new AutopilotOptions { Minutes = 10 });
        w.Run(autopilot, a => a.Cycles == 1 && a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 });
        autopilot.RequestStop("test");
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(autopilot.Ok);
        Assert.Equal("request:test", autopilot.StopName); // no stop=death-unscripted any more
        Assert.Equal(["buff", "den-fight", "death", "buff", "den-fight", "recall-home"], Kinds(w)); // the fight, the death station, then the known state sends the mage back to the fight
        var death = w.Factory.Made.Single(s => s.Kind == StationKinds.Death);
        Assert.Same(AutopilotScenario.UnscriptedDeathSpec, death);
        Assert.Equal([JourneyStations.DeathNoFightArgument, JourneyStations.UnattendedArgument], death.Arguments.Keys.Order(StringComparer.Ordinal)); // M5 7 F25: unattended, its corpse-trip fights hold the magic mode
        Assert.Equal(StationNeeds.None, death.NeedsOverride); // the mage stands at the lifestone: no place to be in
        Assert.Equal((1, true), (w.Factory.ObservedAtStart!.Value.VictimNotices, w.Factory.ObservedAtStart.Value.Running)); // the autopilot's own tally, running since its start, saw the death and was offered to the station
        var lines = autopilot.Lines;
        Assert.StartsWith("autopilot cycle n=1 state=death why=death-unscripted:1 ", lines[0], StringComparison.Ordinal);
        Assert.Equal((2, 1), (Int(lines[0], "kills"), Int(lines[0], "deaths"))); // the fight's kills before the death are counted
        Assert.StartsWith("autopilot cycle n=2 state=stopped why=stop-rule:1 ", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("autopilot done ok=yes cycles=1 stop=request:test ", lines[2], StringComparison.Ordinal);
        Assert.Contains(w.Lines, l => l.Contains("the death station is entered after the respawn", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("TODO(m5-autopilot-death-unscripted)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_buff_line_under_its_margin_stops_the_fight_runs_the_buff_station_and_goes_back_to_the_fight_the_cycle_line_names_the_trigger(string key)
    {
        var w = new ApWorld(key, output, atHome: false);
        ArmourBuffLedger.For(w.Trip.View.State).MarkSeeded(); // the armour memory is seeded (the driver waits for it): no line is active in the scripted world, so every wanted line is under its margin
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => w.Factory.Made.Count(s => s.Kind == StationKinds.Buff) >= 3);
        autopilot.RequestStop("test");
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(["buff", "den-fight", "buff", "den-fight", "buff"], Kinds(w).Take(5)); // the cold buff in the ground, the fight, the upkeep's buff, the fight again, the next upkeep
        Assert.Same(AutopilotScenario.UpkeepSpec, w.Factory.Made[2]); // the journey's buff station, where the mage stands; D126 (a): the upkeep's, with the re-buff margin, not the start pass's
        Assert.Same(AutopilotScenario.BuffSpec, w.Factory.Made[0]); // the cold buff in the ground is a start's pass
        Assert.Contains(arrows, a => a.To == AutopilotState.Upkeep && a.Why.StartsWith("buff-margin: ", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("stop asked (buff-margin)", StringComparison.Ordinal));
        var stopped = autopilot.Lines[0];
        Assert.StartsWith("autopilot cycle n=1 state=stopped why=buff-margin:", stopped, StringComparison.Ordinal);
        Assert.True(int.Parse(Field(stopped, "why").Split(':')[1], CultureInfo.InvariantCulture) >= 2);
        Assert.StartsWith("autopilot done ok=yes cycles=0 stop=request:test ", autopilot.Lines[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Temple)] // M5 7 F16 (D91): the den's profile has fight.retreat_health_percent 0, so its upkeep never stops the fight for health (FightF16Tests); the other grounds still do
    public void The_health_under_the_floor_stops_the_fight_the_keepers_heal_runs_in_place_and_the_fight_goes_on_named_in_the_cycle_line(string key)
    {
        byte sequence = 0;
        var hurt = false;
        void Health(ApWorld world, uint points) => world.Trip.Vendor.R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(++sequence, 2, points));
        var w = new ApWorld(key, output, atHome: false, onKill: (world, kills) =>
        {
            if (kills == 2 && !hurt)
            {
                hurt = true;
                Health(world, 20); // 20 of 100: under the floor (30% with the drain rule on, M5 7 F15)
            }
        });
        var r = w.Trip.Vendor.H.R;
        r.Reducer.OnPlayerDescription(
            r.Context,
            new GameEventHeader(WorldActionRig.Player, 90, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { Inventory = [.. w.Trip.View.State.Self.Inventory.Pack.Select(e => new ContentDesc(e.Guid, e.ContainerType))], Vitals = [new VitalDesc(1, 100, 0, 0, 100), new VitalDesc(3, 100, 0, 0, 100), new VitalDesc(5, 100, 0, 0, 100)] });
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        var restored = false;
        w.Run(
            autopilot,
            a => restored && a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 },
            perFrame: a =>
            {
                if (a.State == AutopilotState.Upkeep && !restored)
                {
                    restored = true;
                    Health(w, 100); // the keeper's heal (the scripted world has none): the pass ends with the health restored
                }
            });
        autopilot.RequestStop("test");
        var status = w.Run(autopilot);

        Assert.True(hurt && restored);
        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(["buff", "den-fight", "den-fight", "recall-home"], Kinds(w)); // the heal in place makes no station: the fight, the heal, the fight again
        Assert.Contains(arrows, a => a.To == AutopilotState.Upkeep && a.Why.StartsWith("health-floor: health 20 of 100 is under the 30% floor", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("stop asked (health-floor)", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("the keeper restored the health", StringComparison.Ordinal));
        Assert.StartsWith("autopilot cycle n=1 state=stopped why=health-floor:1,stop-rule:1 ", autopilot.Lines[0], StringComparison.Ordinal);
        Assert.Equal(AutopilotScenario.HealthFloorPercent, 50);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void The_fights_fizzles_reach_the_cycle_line_and_the_cycle_line_names_every_trigger_that_fired(string key)
    {
        var w = new ApWorld(key, output);
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = 4, TestDeath = true });
        w.Run(autopilot, a => a.Cycles == 2 && a.State == AutopilotState.Fight);
        autopilot.RequestStop("test");
        w.Run(autopilot);

        var lines = autopilot.Lines;
        Assert.StartsWith("autopilot cycle n=1 state=death why=test-death:1 ", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("autopilot cycle n=2 state=trip why=forced-trip:1 ", lines[1], StringComparison.Ordinal);
        Assert.Equal(CycleNames, Names(lines[0], "autopilot cycle "));
        Assert.Equal(Int(lines[0], "kills") / 2, Int(lines[0], "fizzles")); // the scripted fight's fizzles= (the cast policy's tally on the real line, M5 4 a3)
        Assert.Equal(Int(lines[1], "kills") / 2, Int(lines[1], "fizzles"));
        Assert.True(Int(lines[1], "fizzles") > 0);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_scenario_rebuilt_after_a_hand_back_continues_the_cycle_number_and_the_cycles_count_from_its_seed(string key)
    {
        var w = new ApWorld(key, output);
        var autopilot = w.Autopilot(new AutopilotOptions(), seed: new AutopilotSeed(4, 3));
        w.Run(autopilot, a => a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 });
        Assert.Equal((4, 3), (autopilot.Cycle, autopilot.Cycles));
        autopilot.RequestStop("key");
        w.Run(autopilot);

        Assert.StartsWith("autopilot cycle n=4 state=stopped ", autopilot.Lines[0], StringComparison.Ordinal);
        Assert.StartsWith("autopilot done ok=yes cycles=3 stop=request:key ", autopilot.Lines[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void The_known_state_classifies_the_profiles_places(string key)
    {
        var w = new ApWorld(key, output);
        var g = w.Ground;
        Assert.Equal(AutopilotPlace.Home, AutopilotScenario.Classify(w.Trip.Vendor.H.State, g));
        var arrival = g.ArrivalDestination().Location.Position;
        w.Trip.Vendor.StandAt(arrival.CellId, arrival.Local);
        Assert.Equal(AutopilotPlace.Ground, AutopilotScenario.Classify(w.Trip.Vendor.H.State, g));
        w.Trip.Vendor.StandAt(0x7D640013, new Vector3(12, 12, 0));
        Assert.Equal(AutopilotPlace.Elsewhere, AutopilotScenario.Classify(w.Trip.Vendor.H.State, g));
    }

    [Fact]
    public void The_real_fight_takes_the_external_stop_at_its_safe_point_and_names_it()
    {
        var r = new FightManaTests.Rig(mana: 426, stamina: 100) { Fight = new FightOptions { Kills = FightOptions.UnlimitedKills, Seconds = JourneyStations.OpenEndedFailSeconds }, Respawn = true, HitsToKill = 1 };
        r.Caster.ManaRegenPerSecond = 50;
        r.AddTusker();
        var fight = r.Scenario();
        r.Run(fight, 300, 0.25, () => fight.Tally.Kills >= 2);
        Assert.Null(fight.StopReason);
        fight.RequestStop(FightStopReason.ForcedTrip);
        fight.RequestStop(new FightStopReason("buff-margin")); // an open set: a3's reasons are names; the first stands
        var status = r.Run(fight, 60, 0.25);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(FightStopReason.ForcedTrip, fight.StopReason);
        Assert.EndsWith(" stop=forced-trip", fight.VerdictFields(), StringComparison.Ordinal);
        Assert.True(fight.Tally.Kills >= 2);
        Assert.Contains(r.Log.Lines, l => l.Contains("the fight was asked to stop (forced-trip)", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("the first reason stands (forced-trip)", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => fight.RequestStop(new FightStopReason(" ")));
    }

    [Fact]
    public void The_catalog_makes_the_open_ended_fight_the_trips_and_the_death_without_its_fight()
    {
        var open = JourneyStations.DenFightOptions(AutopilotScenario.FightSpec, new FightOptions(), CommandLineArgument.ParseAll(["--fight-kills=7", "--fight-minutes=5"]));
        Assert.True(open.UnlimitedKillCount);
        Assert.Equal((0.0, double.PositiveInfinity), (open.Minutes, open.FailSeconds));
        var plain = JourneyStations.DenFightOptions(JourneyPlans.Mage.Stations.Single(s => s.Kind == StationKinds.DenFight), new FightOptions(), []);
        Assert.Equal(3, plain.Kills); // the journey's own den fight keeps its cap

        var factory = JourneyStations.Create(JourneyPlans.Mage, [], null);
        Assert.False(((LogisticsStation)factory.Create(AutopilotScenario.TripSpec(forced: false))).Forced);
        Assert.True(((LogisticsStation)factory.Create(AutopilotScenario.TripSpec(forced: true))).Forced);
        Assert.True(((LogisticsStation)factory.Create(JourneyPlans.Mage.Stations.Single(s => s.Kind == StationKinds.Logistics))).Forced);
        Assert.Throws<ArgumentException>(() => factory.Create(new StationSpec("logistics", StationKinds.Logistics, null, 1, new Dictionary<string, string> { [JourneyPlans.TripArgument] = "sometimes" })));
        var death = factory.Create(AutopilotScenario.DeathSpec);
        Assert.Equal(("death", StationWhere.Den), (death.Name, death.Needs.Where));
        Assert.Equal(StationWhere.Here, AutopilotScenario.BuffSpec.NeedsOverride!.Where);
    }

    [Fact]
    public void The_autopilots_den_fight_and_the_trips_sale_take_the_loot_option_of_the_command_line_M5_7_F28()
    {
        var args = CommandLineArgument.ParseAll(["--autopilot-start", "--fight-loot=on"]);
        var fight = JourneyStations.DenFightOptions(AutopilotScenario.FightSpec, FightOptions.FromArguments(args), args);
        Assert.True(fight.Loot);
        Assert.False(JourneyStations.DenFightOptions(AutopilotScenario.FightSpec, FightOptions.FromArguments([]), []).Loot); // the default stays off for every other run

        var factory = JourneyStations.Create(JourneyPlans.Mage, ["--autopilot-start", "--fight-loot=on"], null);
        Assert.True(((LogisticsStation)factory.Create(AutopilotScenario.TripSpec(forced: false))).Resupply.Loot); // the trip sells what the fight kept
    }

    [Fact]
    public void The_options_parse_bound_and_are_registered_with_their_help()
    {
        var o = AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-minutes=30", "--autopilot-level=80", "--autopilot-trip-after-kills=5", "--autopilot-test-death"]));
        Assert.Empty(o.Errors);
        Assert.Equal((30.0, 80, 5, true), (o.Minutes, o.Level, o.TripAfterKills, o.TestDeath));
        Assert.Equal("minutes=30 level=80 trip_after_kills=5 test_death=on", o.ToString());
        var bad = AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-minutes=-1", "--autopilot-level=300", "--autopilot-trip-after-kills=x", "--autopilot-test-death=maybe"]));
        Assert.Equal(4, bad.Errors.Count);
        Assert.False(new AutopilotOptions().HasStopRule);
        Assert.Throws<ArgumentException>(() => new AutopilotScenario(bad, JourneyStations.Create(JourneyPlans.Mage, [], null), new LogisticsStationTests.Trip(Den).Context()));

        var help = OptionCatalog.HelpText();
        foreach (var name in new[] { "--autopilot-minutes", "--autopilot-level", "--autopilot-trip-after-kills", "--autopilot-test-death" })
        {
            Assert.Contains(name, help, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(Den, "request", "trip")]
    [InlineData(Den, "level", "trip")]
    [InlineData(Temple, "request", "trip")]
    [InlineData(Den, "request", "death")]
    [InlineData(Den, "level", "death")]
    [InlineData(Temple, "level", "death")]
    public void A_stop_rule_due_during_a_trip_or_a_death_lets_it_finish_first_and_then_stops(string key, string rule, string station)
    {
        // M5 4 F1, 4-R18: "a trip or a death in progress finishes first": the rule falls due while the station runs; the station ends (the trip counted, the death counted, the cycle line written) and only then the run stops
        var w = new ApWorld(key, output, atHome: false);
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = station == "trip" ? 1 : 0, TestDeath = station == "death", Level = rule == "level" ? 10 : 0 });
        var inStation = station == "trip" ? AutopilotState.Trip : AutopilotState.Death;
        w.Run(autopilot, a => a.State == inStation && a.CurrentRun is not null);
        Assert.Equal(inStation, autopilot.State);
        if (rule == "request")
        {
            autopilot.RequestStop("test");
        }
        else
        {
            w.Level(10);
        }

        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(autopilot.Ok);
        Assert.Equal(rule == "request" ? "request:test" : "level", autopilot.StopName);
        Assert.Equal(station == "trip" ? StationKinds.Logistics : StationKinds.Death, w.Factory.Made[^2].Kind); // nothing was started after the station that was in progress but the recall home before the stop (M5 7 F18)
        Assert.Equal(StationKinds.RecallHome, w.Factory.Made[^1].Kind);
        var lines = autopilot.Lines;
        Assert.Equal(3, lines.Count);
        Assert.StartsWith($"autopilot cycle n=1 state={station} ", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("autopilot cycle n=2 state=stopped ", lines[1], StringComparison.Ordinal);
        Assert.Equal(1, Int(lines[0], station == "trip" ? "trips" : "deaths"));
        Assert.Equal(1, Int(lines[2], station == "trip" ? "trips" : "deaths"));
        Assert.StartsWith("autopilot done ok=yes cycles=1 stop=", lines[2], StringComparison.Ordinal);
    }
}
