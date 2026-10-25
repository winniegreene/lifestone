using System.Numerics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// P5a lane S5, the runner on a scripted body (<see cref="S5KinematicBody"/> bound to an offline session's <see cref="MoveIntents"/>):
/// each state, the stuck ladder rung by rung on a body that does not move, the human's key, the server's MoveTo, the intents' outcomes,
/// and keys sent only when they change.
/// </summary>
public sealed class NavRunnerTests(ITestOutputHelper output)
{
    [Fact]
    public void A_straight_run_arrives_within_the_stop_tolerance_walking_the_last_metres_and_sends_keys_only_on_change()
    {
        var rig = new S5Rig(50f, 50f, heading: 0f);
        rig.Runner.Start(S5Rig.To(50f, 80f), rig.Body.Now);

        var state = rig.RunFor(30);

        output.WriteLine($"{state}: {rig.Runner.VerdictFields()}; keys {rig.Runner.KeyChanges}, max {rig.Runner.MaxKeysPerSecond}/s; {string.Join(", ", rig.Body.Calls)}");
        Assert.Equal(NavRunState.Arrived, state);
        Assert.Null(rig.Runner.FailureReason);
        Assert.True(Vector2.Distance(rig.Body.Xy, new Vector2(50f, 80f)) <= NavGoal.DefaultStopTolerance + 0.15f, $"stopped at {rig.Body.Xy}");
        Assert.Equal(["run-key on", "forward on", "run-key off", "forward off"], rig.Body.Calls);
        Assert.Equal(1, rig.Runner.Plans);
        Assert.Equal(0, rig.Runner.Rungs);
        Assert.InRange(rig.Runner.MaxKeysPerSecond, 1, 19);
        Assert.Contains("plans=1 replans=0 rungs=0 blocked=0 corrections=none where_max_m=none", rig.Runner.VerdictFields(), StringComparison.Ordinal);
        Assert.InRange(rig.Runner.RouteMetres, 29f, 31f);
    }

    [Fact]
    public void Beyond_60_degrees_the_body_turns_in_place_first_then_runs()
    {
        var rig = new S5Rig(50f, 50f, heading: 0f);
        rig.Runner.Start(S5Rig.To(50f, 20f), rig.Body.Now); // behind it

        rig.Frame();
        rig.Frame();
        Assert.Equal(["turn-right on", "run-key on"], rig.Body.Calls);
        Assert.False(rig.Body.Holds(MotionCommands.WalkForward));

        var state = rig.RunFor(30);
        Assert.Equal(NavRunState.Arrived, state);
        var forwardAt = rig.Body.Calls.IndexOf("forward on");
        Assert.True(forwardAt > 0);
        Assert.Equal(0, rig.Runner.Rungs); // a turn in place longer than the stuck time is not stuck: the clock starts when forward is pressed
        output.WriteLine(string.Join(", ", rig.Body.Calls));
    }

    [Fact]
    public void A_body_that_does_not_move_climbs_the_ladder_rung_by_rung_and_is_Blocked_after_three_plans()
    {
        var rig = new S5Rig(50f, 50f, heading: 0f);
        rig.Body.Frozen = true;
        rig.Runner.Start(S5Rig.To(50f, 90f), rig.Body.Now);

        var rungsSeen = new List<int>();
        while (!rig.Runner.IsDone && rig.Body.Now < 200)
        {
            var before = rig.Runner.Rungs;
            rig.Frame();
            if (rig.Runner.Rungs != before)
            {
                rungsSeen.Add(rig.Runner.Rungs);
            }
        }

        var calls = rig.Body.Calls;
        output.WriteLine($"{rig.Runner.State} at {rig.Body.Now:F1} s: {rig.Runner.FailureReason}");
        output.WriteLine(string.Join(", ", calls));
        Assert.Equal(NavRunState.Blocked, rig.Runner.State);
        Assert.Equal(12, rig.Runner.Rungs);
        Assert.Equal(Enumerable.Range(1, 12), rungsSeen);
        Assert.Equal(3, rig.Runner.Plans);
        Assert.Equal(2, rig.Runner.Replans);
        Assert.Equal(3, rig.Paths.PlanCalls);
        Assert.Equal(2, rig.Paths.ScarsSeen); // the third plan saw both scars
        Assert.Equal(2, rig.Runner.Scars.All.Count);
        Assert.Contains("stuck after 3 plans and 12 rungs; nearest no physics world to name the nearest static", rig.Runner.FailureReason, StringComparison.Ordinal);
        Assert.StartsWith("run 'run' to ", rig.Runner.FailureReason, StringComparison.Ordinal);
        Assert.Contains("player in cell 0x", rig.Runner.FailureReason, StringComparison.Ordinal);
        Assert.Contains("blocked=1", rig.Runner.VerdictFields(), StringComparison.Ordinal);

        // Rung by rung, in order, three times: back off, sidestep left then right, a charged jump (the fourth rung replans).
        var order = new[] { "back on", "back off", "side-left on", "side-left off", "side-right on", "side-right off", "charge-jump", "jump" };
        var at = 0;
        for (var cycle = 0; cycle < 3; cycle++)
        {
            foreach (var call in order)
            {
                var next = calls.IndexOf(call, at);
                Assert.True(next >= at, $"cycle {cycle}: '{call}' after call {at}");
                at = next + 1;
            }
        }

        Assert.False(rig.Body.Holds(MotionCommands.WalkForward)); // Blocked releases every key
        Assert.False(rig.Body.HoldRun);
    }

    [Theory]
    [InlineData("MoveForward", MotionCommands.WalkForward)]
    [InlineData("TurnLeft", MotionCommands.TurnLeft)] // the key the runner holds too: its own entry must go
    [InlineData("TurnRight", MotionCommands.TurnRight)] // the opposite key
    public void A_person_pressing_a_movement_key_interrupts_the_run_and_the_body_stops_when_the_person_lets_go(string key, uint command)
    {
        // P5a-R1: the controller's command lists are stacks (CommandList.Add never dedupes), so a runner entry left under the person's press
        // would resume as the head when the person lets go. The scripted body counts presses per command the same way.
        var rig = new S5Rig(50f, 50f, heading: 30f);
        rig.Runner.Start(S5Rig.To(50f, 90f), rig.Body.Now);
        rig.Frame();
        rig.Frame();
        Assert.Equal(1, rig.Body.Depth(MotionCommands.TurnLeft));
        Assert.Equal(1, rig.Body.Depth(MotionCommands.WalkForward));
        var before = rig.Body.Calls.Count;

        rig.Body.HandleCommand(command, true); // the person's press is applied first (MovementBindings.OnInputAction), then reported
        rig.H.Api.Move.ReportHumanKey(key);
        Assert.Equal(NavRunState.Interrupted, rig.Runner.State);
        rig.Frame();
        rig.Frame();

        var after = rig.Body.Calls.Skip(before).ToList();
        output.WriteLine(string.Join(", ", after) + "; " + rig.Runner.FailureReason);
        Assert.Contains("forward off", after);
        Assert.Contains("turn-left off", after);
        Assert.Contains("run-key off", after);
        Assert.Equal(1, rig.Body.Depth(command)); // the person's entry alone: the runner's own entry for the same key is gone
        Assert.Equal(1, rig.Body.TotalDepth); // only the person's press is left
        Assert.Contains("a person pressed " + key + " (the human's key wins)", rig.Runner.FailureReason, StringComparison.Ordinal);
        Assert.Equal(NavRunState.Interrupted, rig.Runner.Step(rig.Body.Now));

        rig.Body.HandleCommand(command, false); // the person lets go
        rig.Frame();
        Assert.Equal(0, rig.Body.TotalDepth);
        var rest = (rig.Body.Xy, rig.Body.Heading);
        for (var i = 0; i < 90; i++)
        {
            rig.Frame();
        }

        Assert.Equal(rest, (rig.Body.Xy, rig.Body.Heading)); // the body stands: nothing of the runner's is left held
    }

    [Fact]
    public void Dispose_releases_every_key_in_every_state_even_after_an_arrival_that_kept_its_keys()
    {
        var rig = new S5Rig(50f, 50f, options: new NavRunnerOptions { Stop = false });
        rig.Runner.Start(S5Rig.To(50f, 70f), rig.Body.Now);
        Assert.Equal(NavRunState.Arrived, rig.RunFor(30));
        rig.H.Tick(S5Rig.Dt);
        Assert.True(rig.Body.TotalDepth > 0); // a pass goal leaves the keys held

        rig.Runner.Dispose();
        rig.H.Tick(S5Rig.Dt);
        Assert.Equal(0, rig.Body.TotalDepth);

        var interrupted = new S5Rig(50f, 50f);
        interrupted.Runner.Start(S5Rig.To(50f, 90f), interrupted.Body.Now);
        interrupted.Frame();
        interrupted.Frame();
        interrupted.H.Api.Move.ReportHumanKey("MoveBackward");
        interrupted.Runner.Dispose();
        interrupted.H.Tick(S5Rig.Dt);
        Assert.Equal(0, interrupted.Body.TotalDepth);
    }

    [Fact]
    public void While_the_server_drives_the_player_the_runner_yields_and_presses_nothing_then_runs_on()
    {
        var rig = new S5Rig(50f, 50f);
        rig.Runner.Start(S5Rig.To(50f, 90f), rig.Body.Now);
        rig.RunFor(1);
        Assert.Equal(NavRunState.Running, rig.Runner.State);

        rig.Body.ServerDriving = true;
        Assert.Equal(NavRunState.Yielding, rig.Frame());
        rig.Frame();
        var released = rig.Body.Calls.Count;
        Assert.False(rig.Body.Holds(MotionCommands.WalkForward));
        for (var i = 0; i < 90; i++)
        {
            Assert.Equal(NavRunState.Yielding, rig.Frame());
        }

        Assert.Equal(released, rig.Body.Calls.Count); // nothing pressed while ACE's MoveTo runs

        rig.Body.ServerDriving = false;
        Assert.Equal(NavRunState.Arrived, rig.RunFor(30));
        Assert.Equal(0, rig.Runner.Rungs);
    }

    public static TheoryData<string> Outcomes => new() { "DryRun", "Refused", "Cancelled", "NotImplemented" };

    [Theory]
    [MemberData(nameof(Outcomes))]
    public void An_intent_not_taken_ends_the_run_Blocked_naming_the_outcome(string outcome)
    {
        var rig = new S5Rig(50f, 50f);
        switch (outcome)
        {
            case "DryRun":
                rig.H.Api.DryRun = true;
                break;
            case "Refused":
                rig.H.Api.KillSwitch = true;
                break;
            case "NotImplemented":
                rig.H.Api.Move.Unbind();
                break;
        }

        rig.Runner.Start(S5Rig.To(50f, 90f), rig.Body.Now);
        rig.Body.Advance(S5Rig.Dt);
        rig.Runner.Step(rig.Body.Now); // the keys are queued
        if (outcome == "Cancelled")
        {
            rig.H.Api.KillSwitch = true; // the queued keys are withdrawn
        }

        var state = rig.RunFor(2);

        output.WriteLine(rig.Runner.FailureReason);
        Assert.Equal(NavRunState.Blocked, state);
        Assert.Contains("ended " + outcome, rig.Runner.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Pause_releases_the_keys_and_keeps_the_route_and_Resume_replans_when_three_metres_off()
    {
        var rig = new S5Rig(50f, 50f);
        rig.Runner.Start(S5Rig.To(50f, 90f), rig.Body.Now);
        rig.RunFor(1);
        var route = rig.Runner.Path;

        rig.Runner.Pause();
        Assert.Equal(NavRunState.Waiting, rig.Runner.State);
        rig.Frame();
        Assert.False(rig.Body.Holds(MotionCommands.WalkForward));
        rig.Body.Xy += new Vector2(15f, 0f); // a station moved the player aside (over 10 m: not a failure while paused)
        for (var i = 0; i < 60; i++)
        {
            Assert.Equal(NavRunState.Waiting, rig.Frame());
        }

        Assert.Same(route, rig.Runner.Path);
        rig.Runner.Resume(rig.Body.Now);
        rig.Frame();
        Assert.Equal(1, rig.Runner.Replans);
        Assert.Equal(NavRunState.Arrived, rig.RunFor(30));
    }

    [Fact]
    public void Resume_after_the_player_was_moved_during_Pause_replans_and_does_not_fail()
    {
        // P5a-R3: a station pauses, recalls the player (more than 10 m), and resumes without a Step in between.
        var rig = new S5Rig(20f, 20f);
        rig.Runner.Start(S5Rig.To(20f, 100f), rig.Body.Now);
        rig.RunFor(1);
        rig.Runner.Pause();
        rig.Frame();
        rig.Body.Xy += new Vector2(60f, 0f);

        rig.Runner.Resume(rig.Body.Now);
        Assert.Equal(NavRunState.Running, rig.Frame());
        Assert.Equal(NavRunState.Running, rig.Runner.State);
        Assert.Equal(NavRunState.Arrived, rig.RunFor(30));
        Assert.Equal(1, rig.Runner.Replans);
    }

    [Fact]
    public void A_horizon_wait_that_never_ends_is_Blocked_naming_the_landblock()
    {
        // P5a-R2: the next tiles never attach.
        var rig = new S5Rig(50f, 50f);
        var map = S2TileKit.Map(S2TileKit.Flat(MovementTestKit.Block.LandBlockId & 0xFFFF0000u));
        rig.Paths.Map = map;
        rig.Paths.HorizonAt = new Vector2(50f, 70f);
        rig.Runner.Start(S5Rig.To(50f, 120f), rig.Body.Now);

        var state = rig.RunFor(NavRunner.HorizonWaitSeconds + 20);

        output.WriteLine(rig.Runner.FailureReason);
        Assert.Equal(NavRunState.Blocked, state);
        Assert.Contains("horizon:landblock 0x", rig.Runner.FailureReason, StringComparison.Ordinal);
        Assert.Contains("not attached within", rig.Runner.FailureReason, StringComparison.Ordinal);
        Assert.InRange(rig.Body.Now, NavRunner.HorizonWaitSeconds, NavRunner.HorizonWaitSeconds + 10);
        Assert.Equal(0, rig.Body.TotalDepth);
    }

    [Fact]
    public void A_jump_of_over_ten_metres_in_one_step_fails_the_run()
    {
        var rig = new S5Rig(50f, 50f);
        rig.Runner.Start(S5Rig.To(50f, 90f), rig.Body.Now);
        rig.RunFor(0.5);
        rig.Body.Xy += new Vector2(0f, 12f);

        Assert.Equal(NavRunState.Failed, rig.Frame());
        Assert.Contains("in one step (a teleport or a server correction)", rig.Runner.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void No_path_is_Blocked_with_the_planners_reason()
    {
        var rig = new S5Rig(50f, 50f);
        rig.Paths.FailWith = "no-path";
        rig.Runner.Start(S5Rig.To(50f, 90f), rig.Body.Now);

        Assert.Equal(NavRunState.Blocked, rig.Frame());
        Assert.Contains("no path: no-path", rig.Runner.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_object_goal_arrives_within_range_and_without_physics_its_sight_is_a_loud_todo()
    {
        var plain = new S5Rig(50f, 50f);
        plain.Runner.Start(NavGoal.ToObject(0x80000001, NavPoint.At(MovementTestKit.Block.LandBlockId, 50f, 90f), range: 10f, needSight: false), plain.Body.Now);
        Assert.Equal(NavRunState.Arrived, plain.RunFor(30));
        Assert.InRange(plain.Body.Xy.Y, 79.5f, 80.5f);

        var sight = new S5Rig(50f, 50f);
        sight.Runner.Start(NavGoal.ToObject(0x80000001, NavPoint.At(MovementTestKit.Block.LandBlockId, 50f, 90f), range: 10f, needSight: true), sight.Body.Now);
        Assert.Equal(NavRunState.ArrivedWithoutSight, sight.RunFor(30));
        Assert.Contains("TODO(nav-sight-no-physics)", sight.Runner.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pass_goal_arrives_within_its_pass_tolerance_and_keeps_the_keys()
    {
        var rig = new S5Rig(50f, 50f, options: new NavRunnerOptions { Stop = false });
        rig.Runner.Start(S5Rig.To(50f, 70f), rig.Body.Now);

        Assert.Equal(NavRunState.Arrived, rig.RunFor(30));
        Assert.True(rig.Body.Holds(MotionCommands.WalkForward));
        Assert.True(Vector2.Distance(rig.Body.Xy, new Vector2(50f, 70f)) <= NavGoal.DefaultPassTolerance);
        Assert.DoesNotContain("run-key off", rig.Body.Calls); // no walking near a pass goal
    }

    [Fact]
    public void A_horizon_route_stands_at_its_end_and_plans_to_the_real_goal_once_the_map_changes()
    {
        var rig = new S5Rig(50f, 50f);
        var map = S2TileKit.Map(S2TileKit.Flat(MovementTestKit.Block.LandBlockId & 0xFFFF0000u));
        rig.Paths.Map = map;
        rig.Paths.HorizonAt = new Vector2(50f, 70f);
        rig.Paths.HorizonUntilVersion = map.Version + 1;
        rig.Runner.Start(S5Rig.To(50f, 120f), rig.Body.Now);

        rig.RunFor(10);
        Assert.Equal(NavRunState.Running, rig.Runner.State);
        Assert.Equal(NavPathStatus.Horizon, rig.Runner.Path!.Status);
        Assert.InRange(rig.Body.Xy.Y, 68.5f, 70.5f);
        Assert.False(rig.Body.Holds(MotionCommands.WalkForward)); // standing at the horizon, no rung
        Assert.Equal(0, rig.Runner.Rungs);

        map.Attach(S2TileKit.Flat(S2TileKit.Block(0x40, 0x41)));
        Assert.Equal(NavRunState.Arrived, rig.RunFor(30));
        Assert.Equal(2, rig.Runner.Plans);
        Assert.Equal(1, rig.Runner.Replans);
        Assert.Equal(0, rig.Runner.Rungs);
    }

    [Fact]
    public void The_audit_counts_the_runners_plans_replans_and_rungs()
    {
        var rig = new S5Rig(50f, 50f);
        rig.Body.Frozen = true;
        using var audit = new MovementAudit(new RecordingMovementSink(), rig.H.R.Bus, () => rig.Body.Now, "s5-ladder");
        var runner = new NavRunner(rig.H.Api.Move, rig.Body, rig.Paths, audit: audit);
        runner.Start(S5Rig.To(50f, 90f), rig.Body.Now);
        while (!runner.IsDone && rig.Body.Now < 200)
        {
            rig.Body.Advance(S5Rig.Dt);
            runner.Step(rig.Body.Now);
            rig.H.Tick(S5Rig.Dt);
        }

        Assert.Equal(3, audit.Plans);
        Assert.Equal(2, audit.Replans);
        Assert.Equal(12, audit.LadderRungs);
        Assert.Equal((3, 2, 12), (audit.Summary.Plans, audit.Summary.Replans, audit.Summary.LadderRungs));
        Assert.Equal("movement route=s5-ladder laps=0 corrections=0 where_max_m=none result=FAIL", audit.SummaryLine()); // the verdict line unchanged
        Assert.Contains("corrections=0 where_max_m=none", runner.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_route_of_one_waypoint_steers_at_the_goal_and_arrives_without_indexing_past_the_list()
    {
        // m5-nav-one-waypoint: the start and the goal snapped to one lattice point leave one waypoint, and Steer used to index waypoints[1] every frame.
        var rig = new S5Rig(50f, 50f);
        var paths = new OneWaypointPaths(count: 1);
        using var runner = new NavRunner(rig.H.Api.Move, rig.Body, paths);
        runner.Start(S5Rig.To(50f, 53f), rig.Body.Now);

        var state = runner.State;
        while (!runner.IsDone && rig.Body.Now < 20)
        {
            rig.Body.Advance(S5Rig.Dt);
            state = runner.Step(rig.Body.Now);
            rig.H.Tick(S5Rig.Dt);
        }

        output.WriteLine($"{state}: {runner.VerdictFields()}; {string.Join(", ", rig.Body.Calls)}");
        Assert.Equal(NavRunState.Arrived, state);
        Assert.Null(runner.FailureReason);
        Assert.Equal(1, paths.PlanCalls);
        Assert.True(Vector2.Distance(rig.Body.Xy, new Vector2(50f, 53f)) <= NavGoal.DefaultStopTolerance + 0.15f, $"stopped at {rig.Body.Xy}");
        Assert.Equal(0, runner.Rungs);
    }

    [Fact]
    public void A_route_of_zero_waypoints_is_Blocked_naming_the_empty_path_and_does_not_throw()
    {
        var rig = new S5Rig(50f, 50f);
        using var runner = new NavRunner(rig.H.Api.Move, rig.Body, new OneWaypointPaths(count: 0));
        runner.Start(S5Rig.To(50f, 90f), rig.Body.Now);

        rig.Body.Advance(S5Rig.Dt);
        var state = runner.Step(rig.Body.Now);

        Assert.Equal(NavRunState.Blocked, state);
        Assert.Contains("no path: empty-path", runner.FailureReason, StringComparison.Ordinal);
        Assert.Contains("no waypoints", runner.FailureReason, StringComparison.Ordinal);
    }

    /// <summary>A path source that answers a Found path of <c>count</c> waypoints: 0 (empty), or 1 (the goal alone, the snapped start and goal).</summary>
    private sealed class OneWaypointPaths(int count) : INavPathSource
    {
        // A lattice, so Steer takes the straight run (the clause that indexed waypoints[next]); no planner, the source answers the path itself.
        public NavMap? Map { get; } = S2TileKit.Map(S2TileKit.Flat(MovementTestKit.Block.LandBlockId));

        public PathPlanner? Planner => null;

        public int PlanCalls { get; private set; }

        public NavPath Plan(in CellFrame from, NavGoal goal, NavScars scars)
        {
            PlanCalls++;
            IReadOnlyList<NavWaypoint> waypoints = count == 0 ? [] : [new NavWaypoint(NavMap.ToCellFrame(goal.Point), false)];
            return new NavPath(NavPathStatus.Found, waypoints, null, []);
        }
    }
}
