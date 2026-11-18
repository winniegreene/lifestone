using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Animation;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 0 a3b (DoD 2): the Academy by play as a station. The plan read from <c>quests/academy.json</c> by step kind, the cell matching by low word, the start rule, the climber's plan, and the cheat-free journey's no-place rule (a known state that is not where the first
/// station starts stops by name and sends no place); on the dats, the Academy's rooms the walks need and the experience table the Academy's awards climb.
/// </summary>
public sealed class AcademyStationTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly Quest Academy = QuestCatalog.Get("academy");
    private static readonly Quest Skip = QuestCatalog.Get("academy-skip");

    [Fact]
    public void The_skip_quest_file_loads_strictly_with_its_cited_values_and_no_stamp_of_its_own_to_read_back()
    {
        Assert.Equal(("academy-skip", 4, "0x860201AD"), (Skip.Key, Skip.Steps.Count, "0x" + Skip.Start.Cell.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Null(Skip.Done.Stamp); // finalize_exit erases AcademeyExitTokenGiven
        Assert.Equal("Make sure you talk to Alcott", Skip.Done.Reply);
        Assert.Null(Skip.TimerSeconds);
        Assert.All(Skip.Steps, s => Assert.Contains("D37", s.Source, StringComparison.Ordinal));
        Assert.DoesNotContain(Skip.Steps, s => s.Todo is not null);
        var talk = Skip.Steps.Single(s => s.Kind == QuestStepKind.Talk);
        Assert.Equal((29324u, "AcademeyExitTokenGiven", 29335u), (talk.Target!.Wcid!.Value, talk.Expect.Stamp, talk.Expect.Item!.Wcid!.Value)); // the server's spelling
        Assert.Contains(Skip.Steps, s => s.Kind == QuestStepKind.Give && s.Item!.Wcid == 5084 && s.Expect.Stamp == "CallingStoneGiven");
        Assert.Contains(QuestCatalog.All, q => q.Key == "academy-skip");
        Assert.Contains(QuestCatalog.All, q => q.Key == "academy"); // the full route stays for item 4
    }

    [Fact]
    public void The_skip_plan_is_the_stone_the_three_pieces_Jonathans_talk_and_the_give_that_the_server_teleports()
    {
        var plan = AcademyPlan.Of(Skip);
        Assert.Equal(
            [
                AcademyAct.Give, AcademyAct.Go, AcademyAct.Talk, AcademyAct.GiveExit, // M6 0 lead, live round 8: the leather pickups left the skip (the walk to the cap was refused at the player's own raised floor, TODO(m6-probe-start-surface), item 4; the Pathwarden chest dresses the mage)
            ],
            plan.Select(p => p.Act));
        Assert.DoesNotContain(plan, p => p.Act == AcademyAct.PickUpFloor);
        var exit = plan[^1].Step;
        Assert.Equal((29324u, 29335u, 0xA9B40019u), (exit.Target!.Wcid!.Value, exit.Item!.Wcid!.Value, exit.Place!.Cell));
        Assert.Contains("Alcott", exit.Expect.Reply, StringComparison.Ordinal);
        Assert.NotEqual(exit.Place.Cell >> 16, Skip.Start.Cell >> 16); // the arrival is outside the Academy's landblock: the check is the teleport and the landblock 0xA9B4
        Assert.DoesNotContain(plan, p => p.Act is AcademyAct.UsePortal or AcademyAct.Exit or AcademyAct.KillAndLoot or AcademyAct.Unlock); // no portal walked, no fight
        Assert.Equal(["full", "skip"], new[] { AcademyPlan.RouteOf(Academy), AcademyPlan.RouteOf(Skip) });
        Assert.Equal(AcademyAct.Give, AcademyPlan.Of(Academy).Where(p => p.Act is AcademyAct.Give or AcademyAct.GiveExit).First().Act); // the full route's gives stay plain gives
        Assert.DoesNotContain(AcademyPlan.Of(Academy), p => p.Act == AcademyAct.GiveExit);
    }

    [Fact]
    public void The_skip_station_starts_at_the_same_cell_as_the_full_route_and_reads_its_own_quest()
    {
        var skipStation = new AcademyStation(AcademyStation.SkipKey);
        Assert.Equal(AcademyPlan.Unplayable(Skip, At(0x860201AD)), skipStation.Unplayable(At(0x860201AD)));
        Assert.Null(skipStation.Unplayable(At(0x860201AD)));
        Assert.Equal("cell-0xA9B40019", skipStation.Unplayable(At(0xA9B40019)));
        Assert.Equal("academy", skipStation.Name); // the verdict line is still station=academy, the live test's reading
        Assert.Throws<KeyNotFoundException>(() => new AcademyStation("academy-nope").Unplayable(At(0x860201AD)));
    }

    [Fact]
    public void The_station_plays_every_step_of_the_quest_file_in_order_by_its_kind()
    {
        var plan = AcademyPlan.Of(Academy);
        Assert.Equal(Academy.Steps.Count, plan.Count);
        Assert.Equal(
            [
                AcademyAct.Give, AcademyAct.Go, AcademyAct.PickUpFloor, AcademyAct.Go, AcademyAct.PickUpFloor, AcademyAct.PickUpFloor, AcademyAct.Go, AcademyAct.Talk, AcademyAct.Go, AcademyAct.Talk,
                AcademyAct.Go, AcademyAct.KillAndLoot, AcademyAct.Give, AcademyAct.Go, AcademyAct.UsePortal,
                AcademyAct.Go, AcademyAct.KillAndLoot, AcademyAct.Go, AcademyAct.Give, AcademyAct.Go, AcademyAct.Unlock, AcademyAct.Go, AcademyAct.Talk, // M6 0 F6: the courtyard (the Bellows, the key, the library door, the Senior Guard)
                AcademyAct.Go, AcademyAct.UsePortal, AcademyAct.Go, AcademyAct.Talk, AcademyAct.Go, AcademyAct.KillAndLoot, AcademyAct.Go, AcademyAct.Give,
                AcademyAct.Go, AcademyAct.Exit,
            ],
            plan.Select(p => p.Act));
        Assert.Equal([13240u, 13241u, 13239u], plan.Where(p => p.Act == AcademyAct.PickUpFloor).Select(p => p.Step.Item!.Wcid!.Value));
        Assert.Equal([(12698u, 12709u), (29333u, 12710u), (29331u, 29336u)], plan.Where(p => p.Act == AcademyAct.KillAndLoot).Select(p => (p.Step.Target!.Wcid!.Value, p.Step.Item!.Wcid!.Value)));
        Assert.Equal([(30991u, 5084u), (29320u, 12709u), (30996u, 12710u), (30994u, 29336u)], plan.Where(p => p.Act == AcademyAct.Give).Select(p => (p.Step.Target!.Wcid!.Value, p.Step.Item!.Wcid!.Value)));
    }

    [Fact]
    public void Each_kill_follows_a_go_to_its_creature_and_each_loot_is_given_after_it()
    {
        var plan = AcademyPlan.Of(Academy);
        for (var i = 0; i < plan.Count; i++)
        {
            if (plan[i].Act != AcademyAct.KillAndLoot)
            {
                continue;
            }

            Assert.Equal(AcademyAct.Go, plan[i - 1].Act);
            Assert.Equal(plan[i].Step.Target!.Wcid, plan[i - 1].Step.Target!.Wcid);
            Assert.NotNull(plan[i - 1].Step.Place!.Point);
            Assert.Contains(plan.Skip(i + 1), p => p.Act == AcademyAct.Give && p.Step.Item!.Wcid == plan[i].Step.Item!.Wcid);
        }

        // Every go has a point (the walk's goal), and the exit's place is Holtburg, outside the Academy's landblock.
        Assert.All(plan.Where(p => p.Act == AcademyAct.Go), p => Assert.NotNull(p.Step.Place!.Point));
        var exit = Assert.Single(plan, p => p.Act == AcademyAct.Exit);
        Assert.Equal((29338u, 0xA9B40019u), (exit.Step.Target!.Wcid!.Value, exit.Step.Place!.Cell));
        Assert.Equal(Academy.Done.Reply, exit.Step.Expect.Reply);
    }

    [Fact]
    public void A_cell_of_the_Academy_moves_to_the_instance_by_its_low_word_and_any_other_cell_is_kept()
    {
        Assert.Equal(0x7202023Au, AcademyPlan.Rebase(0x8602023A, Academy.Start.Cell, 0x7202));
        Assert.Equal(0x8602023Au, AcademyPlan.Rebase(0x8602023A, Academy.Start.Cell, 0x8602));
        Assert.Equal(0xA9B40019u, AcademyPlan.Rebase(0xA9B40019, Academy.Start.Cell, 0x7202));
    }

    [Fact]
    public void The_start_is_the_start_cell_by_its_low_word_in_any_instance_and_nowhere_else()
    {
        Assert.Null(AcademyPlan.Unplayable(Academy, At(0x860201AD)));
        Assert.Null(AcademyPlan.Unplayable(Academy, At(0x720201AD)));
        Assert.Equal("cell-0x860201B0", AcademyPlan.Unplayable(Academy, At(0x860201B0)));
        Assert.Equal("cell-0xA9B40019", AcademyPlan.Unplayable(Academy, At(0xA9B40019)));
        Assert.Equal("no-position", AcademyPlan.Unplayable(Academy, null));
        Assert.Equal("cell-0xA9B40019", new AcademyStation().Unplayable(At(0xA9B40019)));
    }

    [Fact]
    public void A_step_the_station_cannot_play_fails_by_name()
    {
        var go = Academy.Steps.First(s => s.Kind == QuestStepKind.Go);
        var noPoint = Academy with { Steps = [go with { Place = go.Place! with { Point = null } }] };
        var e = Assert.Throws<InvalidOperationException>(() => AcademyPlan.Of(noPoint));
        Assert.Contains("TODO(m6-academy-go-point)", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_climber_journey_is_the_Academy_by_play_then_the_Pathwarden_with_no_place_of_any_kind()
    {
        var plan = JourneyPlans.Climber;
        Assert.True(plan.CheatFree);
        Assert.Equal(0, plan.TeleportBudget);
        Assert.Equal([StationKinds.Academy, StationKinds.Pathwarden], plan.Stations.Select(s => s.Kind));
        Assert.Equal(AcademyStation.SkipKey, plan.Stations[0].Arguments[JourneyPlans.QuestArgument]); // M6 0 F7, D37: the climber takes the tutorial's own skip
        Assert.Equal(JourneyPlans.AcademySkipSeconds, plan.Stations[0].ExpectedSeconds);
        Assert.Contains(StationKinds.Academy, StationKinds.All);
        Assert.IsAssignableFrom<IKnownStart>(new AcademyStation());
        Assert.Equal(StationWhere.Here, new AcademyStation().Needs.Where);
        Assert.False(JourneyPlans.Mage.CheatFree); // the other plans keep their recovery places
    }

    [Theory]
    [InlineData(1.79, 1.5, AcademyReach.Use)] // round 4: the golem's corpse 1.79 m away, the walk refused at segment 0: the Open goes and the server's move-to walks the rest
    [InlineData(1.2, 1.5, AcademyReach.InRange)]
    [InlineData(1.5, 1.5, AcademyReach.InRange)]
    [InlineData(5.0, 1.5, AcademyReach.Use)] // the use reach is the use range plus 3.5 m
    [InlineData(5.1, 1.5, AcademyReach.Walk)]
    [InlineData(12.0, 1.5, AcademyReach.Walk)]
    public void An_object_within_the_use_reach_is_used_without_a_walk_and_a_farther_one_is_walked_to(double metres, double range, AcademyReach expected)
    {
        Assert.Equal(5.0, AcademyRun.UseReachMetres);
        Assert.Equal(expected, AcademyRun.ReachOf(metres, range));
    }

    [Fact]
    public void The_step_away_recovery_goes_back_only_to_a_walkable_point_of_the_same_landblock_at_least_a_metre_away()
    {
        var here = new System.Numerics.Vector3(55.35f, -19.49f, 0f);
        Assert.True(AcademyRun.StepAwayWorthIt(0x8602023Au, here, 0x86020273u, here + new System.Numerics.Vector3(3f, 0f, 0f)));
        Assert.False(AcademyRun.StepAwayWorthIt(0x8602023Au, here, 0x86020273u, here + new System.Numerics.Vector3(0.5f, 0f, 0f))); // the player stands on it
        Assert.False(AcademyRun.StepAwayWorthIt(0x8602023Au, here, 0xA9B40019u, here + new System.Numerics.Vector3(3f, 0f, 0f))); // another landblock: never a place, never a walk across
    }

    [Fact]
    public void The_courtyard_portal_lands_in_a_pocket_and_a_second_portal_leads_on_to_the_Sentry()
    {
        var plan = AcademyPlan.Of(Academy);
        var portals = plan.Where(p => p.Act == AcademyAct.UsePortal).Select(p => p.Step.Target!.Wcid!.Value).ToArray();
        Assert.Equal([31061u, 29334u], portals); // live round 5: 0x8602021E (the landing) is not joined to the Sentry's rooms; the Outer Courtyard portal 29334 is in the pocket
        var go = plan.Single(p => p.Act == AcademyAct.Go && p.Step.Target?.Wcid == 29334);
        Assert.Equal(0x86020331u, go.Step.Place!.Cell);
        Assert.Equal(plan.ToList().IndexOf(go) + 1, plan.ToList().FindIndex(p => p.Act == AcademyAct.UsePortal && p.Step.Target!.Wcid == 29334));
    }

    [Theory]
    [InlineData(1, 0x8602021Eu, 50f, -54f, true)] // round 5: the Central Courtyard portal at (70, -40) took the player to the Portal Linkspot (50, -54)
    [InlineData(1, 0x86020273u, 69.2f, -40.5f, false)] // a teleport, but the player stands beside the portal
    [InlineData(0, 0x86020273u, 69.2f, -40.5f, false)] // standing beside it
    [InlineData(1, 0xA9B40019u, 70f, -40f, true)] // the exit took the player to Holtburg
    [InlineData(0, 0x8602021Eu, 50f, -54f, false)] // M6 0 F6: far from the portal with no teleport is a walk that ended short, never a touch
    public void A_walk_was_taken_on_touch_only_when_a_teleport_came_and_the_player_is_far_from_its_portal(int teleports, uint cell, float x, float y, bool taken)
    {
        Assert.Equal(taken, AcademyRun.TouchTeleported(teleports, cell, new System.Numerics.Vector3(x, y, 0f), 0x86020273u, new System.Numerics.Vector3(70f, -40f, -0.06f)));
    }

    [Fact]
    public void Round_7s_shut_library_door_41_m_from_the_portal_is_a_walk_failure_naming_the_door_not_a_touch()
    {
        // Live round 7 (cache/m6/0/live/r7-test-keep.log): the walk to the Outer Courtyard portal (0x86020331, (90, -60, 11.94)) ended Blocked at the Academy Library door, the player in 0x8602024C at (64.16, -90.06), 41.4 m away, with no teleport.
        Assert.False(AcademyRun.TouchTeleported(0, 0x8602024Cu, new System.Numerics.Vector3(64.16f, -90.06f, 0.01f), 0x86020331u, new System.Numerics.Vector3(90f, -60f, 11.94f)));
        const string Failure = "travel:run:blocked: run 'journey-cell-7' to 0x86020000 [90.00 -60.00 11.94] in cell 0x86020331 plan 1 rung 0: door-closed-timeout: door 0x7860204D did not open within 5.0 s of 2 Use(s) (locked: unknown, TODO(door-locked-unknown)); player in cell 0x8602024C at (64.16, -90.06, 0.01)";
        Assert.Equal(0x7860204Du, AcademyRun.ShutDoorIn(Failure));
        Assert.Equal(0x7860204Du, AcademyRun.ShutDoorIn("door-closed-timeout: door 0x7860204D closed again before the body was through, twice"));
        Assert.Equal(0u, AcademyRun.ShutDoorIn("travel:run:indoor-start-unreachable:0x8602024C: no room route from the start reaches a door with a lane outside"));
        Assert.Equal(0u, AcademyRun.ShutDoorIn("door-closed-timeout: door 0x786"));
        Assert.Equal(0u, AcademyRun.ShutDoorIn(null));
    }

    [Fact]
    public void The_library_door_is_unlocked_with_the_Blacksmiths_key_before_the_walk_up_to_the_Senior_Guard_and_his_portal()
    {
        // M6 0 F6: the courtyard's way up to the Outer Courtyard portal is the Academy Library door 30998, locked (the pinned world release: LockCode keydooracademya); the Academy Blacksmith 30996 gives its key 30999 for the Bellows 12710 of a Thieving Thrungus 29333.
        var plan = AcademyPlan.Of(Academy).ToList();
        int At(Func<AcademyStepPlan, bool> what) => plan.FindIndex(p => what(p));
        var courtyard = At(p => p.Act == AcademyAct.UsePortal && p.Step.Target!.Wcid == 31061);
        var bellows = At(p => p.Act == AcademyAct.KillAndLoot && p.Step.Item!.Wcid == 12710);
        var give = At(p => p.Act == AcademyAct.Give && p.Step.Item!.Wcid == 12710);
        var unlock = At(p => p.Act == AcademyAct.Unlock);
        var guard = At(p => p.Act == AcademyAct.Talk && p.Step.Target!.Wcid == 29318);
        var outer = At(p => p.Act == AcademyAct.UsePortal && p.Step.Target!.Wcid == 29334);
        Assert.True(courtyard < bellows && bellows < give && give < unlock && unlock < guard && guard < outer, $"{courtyard} {bellows} {give} {unlock} {guard} {outer}");
        Assert.Equal((30996u, 30999u), (plan[give].Step.Target!.Wcid!.Value, plan[give].Step.Expect.Item!.Wcid!.Value)); // the key is checked in the pack
        Assert.Equal("BellowsNewbieTurnedIn", plan[give].Step.Expect.Stamp);
        Assert.Equal((30998u, 30999u), (plan[unlock].Step.Target!.Wcid!.Value, plan[unlock].Step.Item!.Wcid!.Value));
        Assert.Equal("The Academy Library has been unlocked.", plan[unlock].Step.Expect.Reply);
        Assert.Equal((AcademyAct.Go, 30998u, 0x8602024Cu), (plan[unlock - 1].Act, plan[unlock - 1].Step.Target!.Wcid!.Value, plan[unlock - 1].Step.Place!.Cell)); // the walk to the door is the unlock's go
        Assert.Equal((AcademyAct.Go, 0x8602032Cu), (plan[guard - 1].Act, plan[guard - 1].Step.Place!.Cell)); // the walk up to him is the talk's go
        Assert.StartsWith("A hive of Olthoi just breached the walls of the Outer Courtyard", plan[guard].Step.Expect.Reply, StringComparison.Ordinal);
        Assert.Equal(0x8602021Eu, plan[courtyard].Step.Place!.Cell); // the landings: the portals' instance links to their Portal Linkspots
        Assert.Equal(0x860202C3u, plan[outer].Step.Place!.Cell);
        Assert.Equal(AcademyAct.Unlock, AcademyPlan.Of(Academy)[unlock].Act);
    }

    [Fact]
    public void A_quest_a_station_saw_done_is_a_journey_fact_the_ladder_reads()
    {
        var facts = new JourneyFacts();
        Assert.False(facts.QuestDone("academy"));
        facts.MarkQuestDone("academy");
        facts.MarkQuestDone("academy");
        Assert.True(facts.QuestDone("academy"));
        Assert.False(facts.QuestDone("pathwarden"));
        Assert.Equal(["academy"], facts.QuestsDone);
    }

    [Fact]
    public void A_cheat_free_journey_away_from_its_ground_stops_by_name_and_sends_no_place()
    {
        var t = new J1JourneyRig(J1JourneyRig.BeltCell, peace: false);
        var journey = t.Journey(CheatFreePlan(new StationSpec("s1", StationKinds.HarmHeal, StationNeeds.None, 30)), new J1Factory(), t.Context());

        Assert.Equal(ScenarioStatus.Failed, t.Run(journey));

        Assert.Empty(t.PlacesTo);
        Assert.Equal(0xA5B0u, t.Landblock);
        Assert.Contains("stop=known-state-unplayable:holtburg:cell-0xA5B00032", journey.Failure, StringComparison.Ordinal);
        Assert.Equal("journey climber cheat_free=yes level=" + t.R.H.State.Self.Qualities.Level + " granted=0 place=0 recovery_place=0 stop=known-state-unplayable:holtburg:cell-0xA5B00032", journey.Lines[^1]);
        Assert.Contains(" places=0 recovery_place=0 ", journey.Lines[^2], StringComparison.Ordinal);
    }

    [Fact]
    public void A_cheat_free_journey_whose_first_station_is_the_Academy_stops_outside_its_start_and_sends_no_place()
    {
        var t = new J1JourneyRig(J1JourneyRig.HoltburgCell);
        var journey = t.Journey(CheatFreePlan(new StationSpec("academy", StationKinds.Academy, null, 30)), new AcademyFactory(), t.Context());

        Assert.Equal(ScenarioStatus.Failed, t.Run(journey));

        Assert.Empty(t.PlacesTo);
        Assert.EndsWith(" place=0 recovery_place=0 stop=known-state-unplayable:cell-0xA9B40019", journey.Lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_cheat_free_journey_on_its_ground_runs_its_stations_and_judges_place_and_recovery_place_from_the_counter()
    {
        var t = new J1JourneyRig(J1JourneyRig.HoltburgCell);
        var journey = t.Journey(CheatFreePlan(new StationSpec("s1", StationKinds.HarmHeal, StationNeeds.None, 30)), new J1Factory(), t.Context());

        Assert.Equal(ScenarioStatus.Succeeded, t.Run(journey));

        Assert.Empty(t.PlacesTo);
        Assert.StartsWith("journey done ok=true name=c1 stations=1/1 ", journey.Lines[^2], StringComparison.Ordinal);
        Assert.EndsWith(" granted=0 place=0 recovery_place=0 stop=none", journey.Lines[^1], StringComparison.Ordinal);
        Assert.Contains(t.Log.Entries, e => e.Message == "journey known-state peace=yes cheat_free=yes recovery_place=0");
    }

    [Fact]
    public void The_climber_line_reads_the_counter_verb_by_verb()
    {
        Assert.Equal("journey climber cheat_free=no level=5 granted=3 place=1 recovery_place=2 stop=none", JourneyScenario.ClimberLine(5, new ModVerbCounts(Item: 1, Xp: 2, Place: 1, RecoveryPlace: 2), null)); // 0-R1: the word is computed from the counts
        Assert.Equal("journey climber cheat_free=yes level=5 granted=0 place=0 recovery_place=0 stop=none", JourneyScenario.ClimberLine(5, new ModVerbCounts(), null));
        Assert.Equal("no", JourneyScenario.CheatFreeWord(new ModVerbCounts(RecoveryPlace: 1)));
    }

    // ------------------------------------------------------------------------------------------------ on the dats

    [RequiresDatsFact]
    public void The_start_rooms_reach_the_courtyard_portal_and_the_Sentrys_room_opens_only_outside_the_rooms()
    {
        var start = Academy.Start.Cell;
        var cells = LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), LandblockCoord.FromId(start));
        var planner = new CellRoutePlanner(cells);
        var plan = AcademyPlan.Of(Academy);
        var portal = plan.First(p => p.Act == AcademyAct.UsePortal).Step.Index;
        var before = plan.Where(p => p.Act == AcademyAct.Go && p.Step.Index < portal).Select(p => p.Step.Place!.Cell).Distinct().ToList();
        var lastPortal = plan.Last(p => p.Act == AcademyAct.UsePortal).Step.Index;
        var after = plan.Where(p => p.Act == AcademyAct.Go && p.Step.Index > lastPortal).Select(p => p.Step.Place!.Cell).Distinct().ToList();
        foreach (var cell in before)
        {
            var path = planner.FindPath(start, cell);
            output.WriteLine($"start 0x{start:X8} -> 0x{cell:X8}: {(path is null ? "no walkable way" : $"{path.Count - 1} doorway(s)")}");
            Assert.NotNull(path);
        }

        uint[] probe = [0x8602021E, 0x860202C2, 0x860202C3, 0x86020169, 0x86020148, 0x86020134, 0x86020100, 0x86020104, 0x8602014F, 0x86020331, 0x8602032C, 0x86020273, 0x860201AD];
        foreach (var x in probe)
        {
            output.WriteLine($"0x{x:X8}: " + string.Join(" ", probe.Select(y => planner.FindPath(x, y) is { } q ? (q.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")));
        }

        // After the Outer Courtyard portal: the Sentry's room (0x860202C2, with the Portal Linkspot's 0x860202C3) shares no walkable doorway with the hive's rooms nor the exit's wing; the landblock has no outdoor cells (M6 0 F6), so the way down is
        // the floor portals the room graph refuses (TODO(m6-academy-sentry-ramp), the test below).
        var sentry = after[0];
        Assert.Equal(0x860202C2u, sentry);
        Assert.NotNull(planner.FindPath(sentry, 0x860202C3));
        Assert.All(after.Where(c => c != sentry), c => Assert.Null(planner.FindPath(sentry, c)));
        Assert.Null(planner.FindPath(start, sentry)); // the Central Courtyard portal joins the two (docs/session.md section 25)
    }

    [RequiresDatsFact]
    public void The_courtyard_reaches_the_upper_floor_only_through_the_library_and_the_Sentrys_and_the_hives_rooms_join_no_others()
    {
        var cells = LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), LandblockCoord.FromId(Academy.Start.Cell));
        var planner = new CellRoutePlanner(cells);
        const uint Landing = 0x8602021E, Library = 0x8602024C, Portal = 0x86020331;

        // Live round 7's walk: the only room route from the courtyard's landing to the Outer Courtyard portal goes through the library (0x8602024C) and its door, then up the stairs (0x86020305 at z 6, 0x86020327 at z 12) past the Senior Guard (0x8602032C).
        var route = planner.FindPath(Landing, Portal);
        Assert.NotNull(route);
        output.WriteLine("landing -> portal: " + string.Join(" ", route!.Select(c => (c & 0xFFFF).ToString("X4", System.Globalization.CultureInfo.InvariantCulture))));
        Assert.Contains(Library, route);
        Assert.Contains(0x8602032Cu, route);
        var withoutLibrary = new CellRoutePlanner(LandblockCellSet.FromCells(cells.Structures, cells.Coord, cells.Cells.Where(c => c.Id != Library)));
        Assert.Null(withoutLibrary.FindPath(Landing, Portal)); // no other way: the door must open (the key), a replan has nowhere to go

        // The courtyard's walks between the two portals: the landing reaches every go's room before the library door, and the library's room reaches the rest.
        var plan = AcademyPlan.Of(Academy).ToList();
        var first = plan.FindIndex(p => p.Act == AcademyAct.UsePortal);
        var second = plan.FindLastIndex(p => p.Act == AcademyAct.UsePortal);
        foreach (var go in plan.Skip(first + 1).Take(second - first - 1).Where(p => p.Act == AcademyAct.Go))
        {
            Assert.NotNull(planner.FindPath(Landing, go.Step.Place!.Cell));
        }

        // No cell of the Academy's landblock opens outside (its "courtyards" are rooms): the indoor-outdoor planner cannot walk out and in. The Sentry's rooms (0x860202C2-C5) join the others only through floor portals (0x860202C4 -> 0x8602013B,
        // 0x860202C5 -> 0x8602013C, 6 m down), and the hive's deepest rooms (0x86020100-0107, z -12) only through 0x86020105 -> 0x8602010A (fails the fit probe) and -> 0x86020106 (not upright): TODO(m6-academy-sentry-ramp), TODO(m6-academy-hive-ramp).
        Assert.DoesNotContain(cells.Cells, c => c.Portals.Any(pp => pp.LeadsOutside));
        // M6 4 W4: 0x860202C4 and 0x860202C5 left the Sentry's group: they have no floor at all (the shafts down to 0x8602013B and 0x8602013C), so the sill rule
        // (CellRoutePlanner.HasFloorsAtSill) refuses the doorways into them; the ramps stay open (the floor crossings do not join the shafts' 6 m drops).
        Assert.Equal([0x860202C2u, 0x860202C3u], cells.Ids.Where(id => planner.FindPath(0x860202C2, id) is not null).Order());
        // M6 4 W4: the hive's ramp is a floor crossing now (0x86020105's floor meets 0x86020106's on the not-upright portal; IslandDungeonWalkDatTests confirms the
        // hive's 10 floor crossings by the physics), so the deepest rooms reach 144 cells (7 before: TODO(m6-academy-hive-ramp) falls out of the floor-crossing rule).
        Assert.Equal(144, cells.Ids.Count(id => planner.FindPath(0x86020100, id) is not null));
        Assert.NotNull(planner.FindPath(0x86020100, 0x86020169)); // M6 4 W4: null before the hive's ramp became a floor crossing
    }

    [RequiresDatsFact]
    public void The_experience_table_says_what_the_Academys_fixed_awards_reach()
    {
        var table = XpTableData.Load(fixture.Dats);
        for (var level = 1; level <= 6; level++)
        {
            output.WriteLine($"level {level}: {table.XpForLevel(level)} total experience");
        }

        // The Training Master's AwardXP is 1,000 (cache/m6/0/sel-sentry-master.tsv); the Sentry's level-proportional award has the amount 5,000 (its percent is TODO(m6-academy-sentry-percent)); the kills' experience is not in the SELECT.
        // ACE's level-proportional award is the span of the character's level times its percent, held between its minimum and maximum: at level 2 a whole span (1,777) lands on level 3's 2,777 exactly, and the 5,000 as the award reaches level 4; level 5 is 10,248.
        var span = table.XpForLevel(3) - table.XpForLevel(2);
        output.WriteLine($"1,000 reaches level {LevelOf(table, 1_000)}; 1,000 + one span ({span}) reaches level {LevelOf(table, 1_000 + span)}; 1,000 + 5,000 reaches level {LevelOf(table, 6_000)}; level 5 needs {table.XpForLevel(5)}");
        Assert.Equal(2, LevelOf(table, 1_000));
        Assert.Equal(3, LevelOf(table, 1_000 + span));
        Assert.Equal(4, LevelOf(table, 6_000));
        Assert.True(table.XpForLevel(5) > 6_000); // the Academy's fixed awards do not reach DoD 2's level 5 on this server

        // M6 0 F7 (D37): the skip's award is 11,000 (the W0 SELECT's amount of Jonathan's level-proportional award); what it reaches is the character's own property after the live run, this is only the table's reading of the amount.
        output.WriteLine($"11,000 reaches level {LevelOf(table, 11_000)}; level 5 needs {table.XpForLevel(5)}, level 6 needs {table.XpForLevel(6)}");
        Assert.True(LevelOf(table, 11_000) >= 5);
    }

    private static int LevelOf(XpTableData table, ulong xp)
    {
        var level = 1;
        while (table.XpForLevel(level + 1) <= xp)
        {
            level++;
        }

        return level;
    }

    private static CellFrame At(uint cell) => new(cell, AcFrame.Identity);

    private static JourneyPlan CheatFreePlan(StationSpec station) => new("c1", "BSD Mage", "test", StationWhere.Holtburg, 0, [station]) { CheatFree = true };

    /// <summary>A factory that makes the real Academy station (the known-state reads its start rule; it never starts here).</summary>
    private sealed class AcademyFactory : IStationFactory
    {
        public IStation Create(StationSpec spec) => new AcademyStation();
    }
}
