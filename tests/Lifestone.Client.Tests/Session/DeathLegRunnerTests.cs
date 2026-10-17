using System.Text.RegularExpressions;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// What the death leg (<c>--login-check=bsd-death</c>, M3 3.2 dd) runs without Godot or a server: the review frames' plan (the hit, the Dead motion, the corpse and the lifestone), the death frame sampler (<c>death_frame_max_ms</c>
/// and the order of the corpse and the teleport) over ACE's messages through the real reducer, the routing of <c>--play-screenshot</c>, the limits and the smoke step <c>login-death</c> (a backend step that runs only under
/// <c>-Backend require</c>) with the time it gives the leg. The runner's stage machine is not tested offline (no stand-in serves BSD Mage and the game's player is the walker): the lead's live leg is its proof; the whole
/// <see cref="DeathScenario"/> runs in the play-check <c>death loop</c> (<c>PlayChecks.Death.cs</c>).
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class DeathLegRunnerTests
{
    private const uint Player = ReducerHarness.PlayerGuid;
    private const uint Den = 0x01050375;
    private const uint Holtburg = 0xA9B40019;

    private static string Smoke => File.ReadAllText(RepositoryFiles.PathOf("tools", "smoke.ps1"));

    private static string Runner => File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "DeathLegRunner.cs"));

    // ---------------------------------------------------------------------------------------------- the plan

    private static int Step(DeathShotPlan plan, double now, int hits = 0, bool underFull = false, int dead = 0, int corpses = 0, bool before = true, int arrivals = 0, bool portal = false) =>
        plan.Step(now, hits, underFull, dead, corpses, before, arrivals, portal);

    [Fact]
    public void The_hit_frame_needs_a_blow_with_the_health_under_full_and_is_due_after_its_delay_and_two_hidden_frames()
    {
        var plan = new DeathShotPlan();
        plan.Begin(0, 0, 0, 0);

        Assert.Equal(-1, Step(plan, 1.0, hits: 1, underFull: false)); // a blow that left the health at 100% is not the frame
        Assert.False(plan.IsOwed(1.0));
        Assert.Equal(-1, Step(plan, 2.0, hits: 1, underFull: true)); // the trigger frame: 0.2 s to wait
        Assert.True(plan.IsOwed(2.0));
        Assert.Equal(-1, Step(plan, 2.1, hits: 1, underFull: true));
        Assert.Equal(0, Step(plan, 2.25, hits: 1, underFull: true)); // the delay has passed and two frames were hidden
        plan.MarkTaken(0);

        Assert.False(plan.IsOwed(2.25));
        Assert.Equal(-1, Step(plan, 3.0, hits: 2, underFull: true)); // each slot is triggered once
        Assert.StartsWith("hit=taken ", plan.Note(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_dead_frame_is_the_dead_motion_plus_one_and_a_half_seconds()
    {
        var plan = new DeathShotPlan();
        plan.Begin(0, 0, 0, 0);

        Assert.Equal(-1, Step(plan, 10.0, dead: 1));
        Assert.Equal(-1, Step(plan, 11.0, dead: 1));
        Assert.Equal(-1, Step(plan, 11.4, dead: 1));
        Assert.Equal(1, Step(plan, 11.5, dead: 1));
        plan.MarkTaken(1);

        Assert.Contains(" dead=taken ", plan.Note(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_corpse_frame_is_taken_when_the_world_is_still_up_and_given_up_by_name_when_portal_space_is_up()
    {
        var up = new DeathShotPlan();
        up.Begin(0, 0, 0, 0);
        Assert.Equal(-1, Step(up, 5.0, corpses: 1, before: true));
        Assert.Equal(-1, Step(up, 5.1, corpses: 1, before: true));
        Assert.Equal(2, Step(up, 5.35, corpses: 1, before: true));
        up.MarkTaken(2);
        Assert.Contains("corpse=taken", up.Note(), StringComparison.Ordinal);

        var tunnel = new DeathShotPlan();
        tunnel.Begin(0, 0, 0, 0);
        Assert.Equal(-1, Step(tunnel, 5.0, corpses: 1, before: true, portal: true));
        Assert.Equal(-1, Step(tunnel, 5.1, corpses: 1, before: true, portal: true));
        Assert.Equal(-1, Step(tunnel, 5.35, corpses: 1, before: true, portal: true)); // due, but the tunnel is up: not a frame of the corpse
        Assert.Contains("corpse=not taken (portal space was already up 0.3 s after the corpse's F745)", tunnel.Note(), StringComparison.Ordinal);

        var after = new DeathShotPlan();
        after.Begin(0, 0, 0, 0);
        Assert.Equal(-1, Step(after, 5.0, corpses: 1, before: false)); // the corpse came after the F751
        Assert.Contains("the corpse's F745 came after the death's F751", after.Note(), StringComparison.Ordinal);
        Assert.False(after.IsOwed(5.0));
    }

    [Fact]
    public void The_lifestone_frame_is_the_arrival_plus_three_and_a_half_seconds_past_the_hold_and_waits_for_the_world()
    {
        var plan = new DeathShotPlan();
        plan.Begin(0, 0, 0, 0);

        Assert.Equal(-1, Step(plan, 20.0, arrivals: 1, portal: true));
        Assert.True(plan.IsOwed(22.9));
        Assert.False(plan.IsOwed(23.1)); // the scenario is held for 3 s of the 3.5, not longer
        Assert.Equal(-1, Step(plan, 22.0, arrivals: 1, portal: true));
        Assert.Equal(-1, Step(plan, 23.6, arrivals: 1, portal: true)); // due, the tunnel is still up: wait
        Assert.Equal(3, Step(plan, 24.0, arrivals: 1, portal: false));
        plan.MarkTaken(3);

        Assert.EndsWith("lifestone=taken", plan.Note(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_slot_whose_trigger_never_came_is_named_and_a_slot_the_tunnel_outlasts_is_given_up()
    {
        var plan = new DeathShotPlan();
        plan.Begin(0, 0, 0, 0);
        Step(plan, 1.0, arrivals: 1, portal: true);
        Step(plan, 2.0, arrivals: 1, portal: true);
        Step(plan, 20.0, arrivals: 1, portal: true); // 19 s of tunnel: past the give-up time

        Assert.Equal("hit=trigger never came dead=trigger never came corpse=trigger never came lifestone=not taken (portal space was up until the give-up time)", plan.Note());
    }

    [Fact]
    public void At_most_one_frame_is_due_a_frame_and_the_plan_allocates_nothing_per_frame()
    {
        var plan = new DeathShotPlan();
        plan.Begin(0, 0, 0, 0);
        Step(plan, 1.0, hits: 1, underFull: true, dead: 1);
        Step(plan, 1.01, hits: 1, underFull: true, dead: 1);
        Assert.Equal(0, Step(plan, 1.5, hits: 1, underFull: true, dead: 1)); // the hit first; the dead frame is not due yet anyway
        plan.MarkTaken(0);
        Assert.Equal(1, Step(plan, 2.6, hits: 1, underFull: true, dead: 1));
        plan.MarkTaken(1);

        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                Step(plan, 3.0 + (i * 0.016), hits: 3, underFull: true, dead: 2, corpses: 1, before: true, arrivals: 1, portal: false);
            }
        });
    }

    [Fact]
    public void The_frame_paths_are_the_stem_and_the_slot()
    {
        Assert.Equal(Path.Combine("cache", "m3", "3-2", "death-hit.png"), DeathShotPlan.ShotPath("cache/m3/3-2/death.png", 0));
        Assert.Equal("death-lifestone.png", DeathShotPlan.ShotPath("death.png", 3));
        Assert.Equal(["hit", "dead", "corpse", "lifestone"], DeathShotPlan.Names);
        Assert.Equal([0.2, 1.5, 0.3, 3.5], DeathShotPlan.DelaysSeconds);
    }

    // ---------------------------------------------------------------------------------------------- the death frame sampler

    private readonly ReducerHarness _h = new();
    private readonly DeathFrameSampler _sampler = new();
    private ushort _movement = 1;
    private ushort _position = 1;
    private ushort _teleport = 1;
    private ushort _state = 1;

    private void Started()
    {
        _h.EnterWorld();
        _sampler.Start(new View(_h));
    }

    private void DeadMotion() =>
        _h.Reducer.OnUpdateMotion(_h.Context, new UpdateMotionMsg(Player, 1, ++_movement, 1, false, new ServerMotionState { MovementType = MovementType.Interpreted, Stance = 0x003D, Forward = WorldObjectTable.DeadMotionLowWord }));

    private void Corpse(uint guid, string name)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(Den, 12, 30));
        _h.Reducer.OnCreateObject(_h.Context, basis with { Weenie = basis.Weenie with { Name = name, ObjectDescriptionFlags = WorldObjectTable.CorpseFlag } }, isUpdate: false);
    }

    private void Teleport() => _h.Reducer.OnPlayerTeleport(_h.Context, new PlayerTeleportMsg(++_teleport));

    private void Arrive() => _h.Reducer.OnUpdatePosition(_h.Context, ReducerHarness.Move(Player, instance: 1, position: ++_position, teleport: _teleport, forcePosition: 1, cell: Holtburg, x: 80));

    /// <summary>ACE's SetState that clears Hidden once the client's LoginComplete arrives after the teleport (<c>Player.OnTeleportComplete</c>): the player is present, portal space is over.</summary>
    private void Present() => _h.Reducer.OnSetState(_h.Context, new SetStateMsg(Player, PhysicsState.Gravity | PhysicsState.EdgeSlide, 1, ++_state));

    [Fact]
    public void The_window_runs_from_the_dead_motion_through_portal_space_to_the_first_frame_after_the_player_is_present_and_keeps_the_worst_frame()
    {
        Started();
        Assert.False(_sampler.Step(0.090, skip: false)); // before the death: not a death frame
        DeadMotion();
        Assert.False(_sampler.Step(0.100, skip: false)); // the call that saw the motion: its delta is the frame before it
        Assert.True(_sampler.Step(0.020, skip: false)); // the Dead motion's own frame
        Corpse(0x80000500, "Corpse of Tester");
        Teleport();
        Arrive(); // ACE sends the F751 and the destination position in one call (Player_Location.cs 686-696): one pump
        Assert.True(_sampler.Step(0.040, skip: false)); // the call that saw the F751 and the position: its delta is the frame before them (the Dead animation), which counts
        Assert.True(_sampler.Step(0.045, skip: false)); // portal space: the tunnel's frames count
        Assert.True(_sampler.Step(0.070, skip: false)); // the landblock load, still in the tunnel
        Assert.False(_sampler.Step(0.030, skip: false)); // the tunnel lasts as long as it lasts: a fourth frame of it counts too
        Present(); // the SetState that clears Hidden: the end of portal space
        Assert.False(_sampler.Step(0.035, skip: false)); // the call that saw the player present: its delta is the last frame of portal space, which counts
        Assert.True(_sampler.Step(0.080, skip: false)); // the frame the player materialised in: the arrival burst, the worst frame
        Assert.False(_sampler.Step(0.025, skip: false)); // the first frame after the arrival: the last one counted
        Assert.False(_sampler.Step(0.200, skip: false)); // the window is closed

        Assert.Equal(80.0, _sampler.DeathFrameMaxMs!.Value, 6);
        Assert.Equal(8, _sampler.Frames);
        Assert.Equal(1, _sampler.DeadMotions);
        Assert.Equal(1, _sampler.Portals);
        Assert.Equal(1, _sampler.Arrivals);
        Assert.False(_sampler.WindowOpen);
    }

    [Fact]
    public void A_skipped_frame_is_left_out_and_a_second_death_opens_the_window_again()
    {
        Started();
        DeadMotion();
        _sampler.Step(0.016, skip: false);
        _sampler.Step(0.500, skip: true); // the frame after a saved review frame
        Teleport();
        Arrive();
        Present();
        _sampler.Step(0.016, skip: false);
        _sampler.Step(0.016, skip: false);
        _sampler.Step(0.016, skip: false);
        Assert.Equal(16.0, _sampler.DeathFrameMaxMs!.Value, 6);

        DeadMotion(); // the tuskers' death after the suicide's
        _sampler.Step(0.016, skip: false);
        Assert.True(_sampler.Step(0.045, skip: false));
        Assert.Equal(45.0, _sampler.DeathFrameMaxMs!.Value, 6);
        Assert.Equal(2, _sampler.Windows);
        Assert.Equal(2, _sampler.DeadMotions);
    }

    [Fact]
    public void The_corpse_is_before_portal_space_only_when_it_comes_between_the_dead_motion_and_the_teleport()
    {
        Started();
        DeadMotion();
        Corpse(0x80000501, "Corpse of Tester");
        Assert.True(_sampler.CorpseBeforePortal); // ACE's order: the corpse, then the teleport (Player_Death.cs 247-292)
        Teleport();
        Corpse(0x80000502, "Corpse of Tester"); // after the F751, before the arrival: still this death's
        Assert.False(_sampler.CorpseBeforePortal);
        Arrive();
        Corpse(0x80000503, "Corpse of Somebody Else"); // another name is not the player's
        Assert.Equal(2, _sampler.Corpses);
        Assert.Equal(0, _sampler.StaleCorpses);
    }

    [Fact]
    public void A_corpse_of_the_player_outside_a_death_window_is_stale_and_raises_neither_the_count_nor_the_order_flag()
    {
        Started();
        Corpse(0x80000511, "Corpse of Tester"); // an old corpse streams in with no death pending
        Assert.Equal(0, _sampler.Corpses);
        Assert.Equal(1, _sampler.StaleCorpses);
        Assert.False(_sampler.CorpseBeforePortal);
        DeadMotion();
        Corpse(0x80000512, "Corpse of Tester"); // this death's own corpse
        Assert.Equal(1, _sampler.Corpses);
        Assert.True(_sampler.CorpseBeforePortal);
        Teleport();
        Arrive();
        Corpse(0x80000513, "Corpse of Tester"); // other old ones, at the arrival
        Corpse(0x80000514, "Corpse of Tester");
        Assert.Equal(1, _sampler.Corpses);
        Assert.Equal(3, _sampler.StaleCorpses);
        Assert.True(_sampler.CorpseBeforePortal); // the flag still says how this death's corpse came
    }

    [Fact]
    public void The_sampler_allocates_nothing_per_frame()
    {
        Started();
        DeadMotion();
        _sampler.Step(0.016, skip: false);
        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                _sampler.Step(0.016 + (i * 0.0001), skip: i % 50 == 49);
            }
        });
    }

    // ---------------------------------------------------------------------------------------------- the end of the verdict (live fix)

    [Fact]
    public void The_runner_takes_the_respawn_from_the_scenarios_outcome_and_never_re_measures()
    {
        var runner = Runner;

        Assert.Contains("scenario.Outcome(_deathSampler.DeathFrameMaxMs, _fightSampler.FightFrameMaxMs)", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("DeathLegVerdict.Measure(", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("ClassifyRespawn(", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void A_runner_failure_fails_the_scenarios_outcome_and_keeps_its_respawn_reading()
    {
        var good = DeathLegVerdict.Compose(null, new DeathLegMeasures(1, 0, 1, 40, DeathRespawn.Lifestone, 8, 0, 0, 75.6, true, 5, 4, 87, 1, 0.0, 20, 16.7, 10.1));
        Assert.True(good.Ok);
        Assert.Same(good, DeathLegFinish.WithRunnerFailure(good, null));
        Assert.Same(good, DeathLegFinish.WithRunnerFailure(good, string.Empty));

        var failed = DeathLegFinish.WithRunnerFailure(good, "the leg took more than 1205 s");

        Assert.False(failed.Ok);
        Assert.Contains(" ok=false ", failed.Line, StringComparison.Ordinal);
        Assert.Contains(" respawn=lifestone ", failed.Line, StringComparison.Ordinal);
        Assert.Equal("the death loop failed: the leg took more than 1205 s", failed.Problems[0]);

        var again = DeathLegFinish.WithRunnerFailure(failed, "the leg took more than 1205 s");
        Assert.Same(failed, again); // the scenario's own failure is not added twice
    }

    // ---------------------------------------------------------------------------------------------- the options

    [Fact]
    public void The_death_legs_review_frame_path_is_routed_like_the_fight_legs_and_only_without_a_delay()
    {
        var death = PlayOptions.Parse(["--login-check=bsd-death", "--play-screenshot=cache/m3/3-2/death.png"]);
        var late = PlayOptions.Parse(["--login-check=bsd-death", "--play-screenshot=cache/m3/3-2/death.png", "--play-screenshot-after=9"]);

        Assert.Equal(LoginCheckKind.BsdDeath, death.LoginCheck);
        Assert.Equal("cache/m3/3-2/death.png", death.BuffShotPath);
        Assert.Null(death.ScreenshotPath); // play mode's own capture (a fixed wait, then quit) must not fire
        Assert.Null(late.BuffShotPath);
        Assert.Equal("cache/m3/3-2/death.png", late.ScreenshotPath);
    }

    // ---------------------------------------------------------------------------------------------- the limits and the smoke step

    private static double Constant(string source, string name) => double.Parse(Regex.Match(source, @"public const double " + name + @" = (\d+(\.\d+)?);").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void The_whole_check_limit_covers_every_stage_and_the_smoke_gives_the_leg_its_time_plus_the_recovery_and_start_up()
    {
        var runner = Runner;
        var stages = new[] { "LoginSeconds", "GearSeconds", "BarsSeconds", "BindSeconds", "SuicideSeconds", "PlaceSeconds", "RespawnSeconds", "RegearSeconds" };
        var fixedStages = stages.Sum(name => Constant(runner, name));
        var whole = fixedStages + DeathOptions.DefaultSeconds;
        var recovery = Constant(runner, "RecoverySeconds");
        var timeout = double.Parse(Regex.Match(Smoke, @"--login-check=bsd-death' (\d+) ").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(605.0, fixedStages);
        Assert.Equal(1205.0, whole);
        Assert.Contains("WholeCheckSeconds = LoginSeconds + GearSeconds + BarsSeconds + BindSeconds + SuicideSeconds + PlaceSeconds + DeathOptions.DefaultSeconds + RespawnSeconds + RegearSeconds", runner, StringComparison.Ordinal);
        Assert.Contains("WholeCheckSecondsFor(double deathSeconds) => LoginSeconds + GearSeconds + BarsSeconds + BindSeconds + SuicideSeconds + PlaceSeconds + deathSeconds + RespawnSeconds + RegearSeconds", runner, StringComparison.Ordinal);
        Assert.True(timeout >= whole + 300, $"the smoke gives the leg {timeout} s for {whole} s plus the start-up");
        Assert.True(timeout >= whole + recovery + 300, $"the smoke gives the leg {timeout} s for {whole} s, {recovery} s of recovery and the start-up");
        Assert.True(Constant(runner, "BarsSeconds") >= DeathScenario.BarsSeconds);
        Assert.True(Constant(runner, "SuicideSeconds") >= DeathScenario.ArrivalSeconds + DeathScenario.VitalsSeconds + 15);
        Assert.True(Constant(runner, "RespawnSeconds") >= DeathScenario.ArrivalSeconds + DeathScenario.VitalsSeconds);
    }

    [Fact]
    public void The_smoke_step_login_death_is_a_backend_step_that_runs_only_when_the_backend_is_required_after_login_bsd()
    {
        var script = Smoke;
        var steps = Regex.Match(script, @"\$allSteps = @\(([^)]*)\)", RegexOptions.Singleline).Groups[1].Value;
        var backend = Regex.Match(script, @"\$backendSteps = @\(([^)]*)\)").Groups[1].Value;

        Assert.Contains("'login-death'", steps, StringComparison.Ordinal);
        Assert.Contains("'login-death'", backend, StringComparison.Ordinal);
        Assert.True(steps.IndexOf("'login-bsd'", StringComparison.Ordinal) < steps.IndexOf("'login-death'", StringComparison.Ordinal));
        Assert.True(backend.IndexOf("'login-bsd'", StringComparison.Ordinal) < backend.IndexOf("'login-death'", StringComparison.Ordinal));

        var skip = script.IndexOf("if ($step -eq 'login-death' -and $Backend -ne 'require')", StringComparison.Ordinal);
        var gate = script.IndexOf("if ($backendSteps -contains $step -and -not (Test-BackendGate", StringComparison.Ordinal);
        Assert.True(skip > 0 && skip < gate, "the login-death skip rule must come before the backend gate");
        var rule = script[skip..gate];
        Assert.Contains("-Skipped", rule, StringComparison.Ordinal);
        Assert.Contains("continue", rule, StringComparison.Ordinal);

        var body = script[script.IndexOf("'login-death' {", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("'login' {", StringComparison.Ordinal)];
        Assert.Contains("--account=test --login-check=bsd-death", body, StringComparison.Ordinal);
        Assert.Contains("bsd-death done ok=true", body, StringComparison.Ordinal);
        Assert.Contains("$verdict", body, StringComparison.Ordinal);
        Assert.Contains("login-death ", script[..script.IndexOf("$allSteps", StringComparison.Ordinal)], StringComparison.Ordinal); // its help line
    }

    [Fact]
    public void Slice_check_never_runs_the_death_step()
    {
        var sliceCheck = File.ReadAllText(RepositoryFiles.PathOf("tools", "slice-check.ps1"));

        Assert.Contains("[ValidateSet('auto')]", sliceCheck, StringComparison.Ordinal);
        Assert.DoesNotContain("login-death", sliceCheck, StringComparison.Ordinal);
    }

    [Fact]
    public void The_runner_is_hooked_the_play_path_is_warmed_and_the_frame_code_allocates_nothing()
    {
        var runner = Runner;
        var hook = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "LoginCheckRunner.cs"));

        Assert.Contains("kind == LoginCheckKind.BsdDeath", hook, StringComparison.Ordinal);
        Assert.Contains("DeathLegRunner.Install(main, services, host, mode, ui)", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO(m3-death-leg)", runner, StringComparison.Ordinal);
        Assert.Contains("RuntimeHelpers.PrepareMethod", runner, StringComparison.Ordinal); // the death loop's methods are compiled on the loading worker
        Assert.Contains("mode.ScenarioHost = walkHost", runner, StringComparison.Ordinal);
        Assert.Contains("DeathStages.All", runner, StringComparison.Ordinal);
        Assert.Contains("new GameApproachWalk(", runner, StringComparison.Ordinal); // the bind walks to the lifestone's use radius (ACE waits for the client to follow its MoveToObject)
        Assert.Contains("approach)", runner, StringComparison.Ordinal);
        Assert.Contains("SessionJitWarmUp", runner, StringComparison.Ordinal); // the death messages' handlers are warmed there, said in the remarks

        // SampleFrame and StepShot run every frame: no LINQ, no string building and no lambda in them.
        foreach (var method in new[] { "private void SampleFrame(", "private void StepShot(", "private void NoteStage(" })
        {
            var start = runner.IndexOf(method, StringComparison.Ordinal);
            Assert.True(start > 0, method);
            var body = runner[start..runner.IndexOf("\n    }\n", start, StringComparison.Ordinal)];
            Assert.DoesNotContain(".Select(", body, StringComparison.Ordinal);
            Assert.DoesNotContain(".Where(", body, StringComparison.Ordinal);
            Assert.DoesNotContain("=>", body, StringComparison.Ordinal);
        }
    }

    private sealed class View(ReducerHarness h) : IGameSessionView
    {
        public GameState State => h.State;

        public IEventBus Events => h.Bus;

        public IActionApi Actions => null!;

        public PlayerTools Tools => null!;

        public SessionClock Clock => h.Clock;

        public ActionLog ActionLog { get; } = new();

        public LinkStats Link => default;
    }
}
