using System.Text.RegularExpressions;
using Lifestone.Client.Options;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// What the fight leg (<c>--login-check=bsd-fight</c>, M3 3.1 fd) runs without Godot or a server: the frame sampler (<c>cast_frame_max_ms</c>, <c>fight_frame_max_ms</c>, <c>bsd_min_fps</c>), the review frames' plan (the bolt, the
/// hit and the kill line), the verdict wiring from the sampler and the tally, the routing of <c>--play-screenshot</c> and <c>--retail-sphereless-missile</c>, and the smoke step <c>login-bsd</c> (a backend step that runs only
/// under <c>-Backend require</c>) with the time it gives the leg. The runner's stage machine itself is not tested offline (no stand-in serves BSD Mage; the game's player is the walker): the lead's live leg is its proof, and the
/// whole <see cref="FightScenario"/> is run by the play-check <c>fight loop</c> (<c>PlayChecks.Fight.cs</c>).
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class FightLegRunnerTests
{
    private const double Frame = 1.0 / 60.0;

    private static string Smoke => File.ReadAllText(RepositoryFiles.PathOf("tools", "smoke.ps1"));

    private static string Runner => File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "FightLegRunner.cs"));

    // ---------------------------------------------------------------------------------------------- the sampler

    [Fact]
    public void The_cast_frame_is_the_slowest_frame_while_the_cast_window_is_open()
    {
        var s = new FightFrameSampler();
        s.Step(0.050, castWindowOpen: false, projectileWindowOpen: false, targetEngaged: false, fighting: true, inDen: true, skip: false); // slow, but no window
        s.Step(0.020, true, false, false, true, true, false);
        s.Step(0.032, true, false, false, true, true, false);
        s.Step(0.018, true, false, false, true, true, false);

        Assert.Equal(32.0, s.CastFrameMaxMs!.Value, 6);
        Assert.Equal(4, s.Frames);
    }

    [Fact]
    public void The_fight_frame_needs_the_fight_stage_and_a_cast_window_a_projectile_or_a_target()
    {
        var s = new FightFrameSampler();
        s.Step(0.010, true, false, false, fighting: false, inDen: true, skip: false); // the top-up's cast window opened this frame: the delta is the frame before it
        s.Step(0.090, true, false, false, fighting: false, inDen: true, skip: false); // the top-up's cast: a cast frame, not a fight frame
        s.Step(0.080, false, false, false, fighting: true, inDen: true, skip: false); // the fight stage began this frame; the delta is the top-up's last frame (its cast window was open): a cast frame, not a fight frame
        s.Step(0.020, false, false, false, true, true, false); // the fight stage, nothing engaged
        Assert.Null(s.FightFrameMaxMs);

        s.Step(0.011, false, true, false, true, true, false); // a projectile alive from this frame: the delta is the frame before it
        s.Step(0.011, false, false, true, true, true, false); // the projectile's frame; a target engaged from this frame
        s.Step(0.013, true, false, false, true, true, false); // the target's frame; a cast window from this frame
        s.Step(0.012, false, false, false, true, true, false); // the cast window's frame

        Assert.Equal(13.0, s.FightFrameMaxMs!.Value, 6);
        Assert.Equal(90.0, s.CastFrameMaxMs!.Value, 6);
    }

    [Fact]
    public void A_delta_is_judged_by_the_flags_of_the_call_before_it_so_the_frame_that_opened_a_window_counts_and_the_frame_before_it_does_not()
    {
        // The engine's delta is the previous frame's whole time, and the flags are read after this frame's events were applied (the runners step after play mode):
        // the flags that held while the delta's frame ran are the previous call's. A window that opens and closes within two frames is the case that shows it.
        var s = new FightFrameSampler();
        s.Step(0.010, false, false, false, fighting: true, inDen: true, skip: false); // the frame before the first sample: nothing open
        s.Step(0.012, false, false, false, true, true, false); // the frame before the F747: nothing open
        s.Step(0.040, false, true, false, true, true, false); // the call that saw the projectile's F747: its delta is the frame before the window opened, not a fight frame
        s.Step(0.030, false, false, false, true, true, false); // the bolt exploded this frame: the delta is the F747 frame's own time, inside the projectile window

        Assert.Equal(30.0, s.FightFrameMaxMs!.Value, 6);
        Assert.Null(s.CastFrameMaxMs);

        s.Step(0.015, true, false, false, true, true, false); // the call that saw the cast window open: its delta is the frame before it, not a cast frame
        s.Step(0.022, false, false, false, true, true, false); // the window closed this frame: the delta is the open frame's time

        Assert.Equal(22.0, s.CastFrameMaxMs!.Value, 6);
        Assert.Equal(30.0, s.FightFrameMaxMs!.Value, 6);
    }

    [Fact]
    public void A_skipped_frame_is_left_out_of_every_field()
    {
        var s = new FightFrameSampler();
        s.Step(0.200, true, true, true, true, true, skip: true); // the frame after a saved review frame (its flags still stand for the next delta)
        s.Step(0.014, true, true, true, true, true, false);

        Assert.Equal(14.0, s.CastFrameMaxMs!.Value, 6);
        Assert.Equal(14.0, s.FightFrameMaxMs!.Value, 6);
        Assert.Equal(1, s.Frames);
    }

    [Fact]
    public void The_lowest_one_second_window_in_the_den_is_the_min_fps()
    {
        var s = new FightFrameSampler();
        s.Step(1.0 / 64.0, false, false, false, false, true, false); // the call that saw the player in the den: its delta is the frame before, outside any window
        for (var i = 0; i < 64; i++)
        {
            s.Step(1.0 / 64.0, false, false, false, false, true, false); // a second at 64 fps (powers of two: the sums are exact)
        }

        for (var i = 0; i < 32; i++)
        {
            s.Step(1.0 / 32.0, false, false, false, false, true, false); // a second at 32 fps
        }

        for (var i = 0; i < 65; i++)
        {
            s.Step(1.0 / 64.0, false, false, false, false, true, false); // a second at 64 fps and one frame of a window that never completes
        }

        Assert.Equal(3, s.Windows);
        Assert.Equal(32.0, s.BsdMinFps!.Value, 6);
    }

    [Fact]
    public void Frames_outside_the_den_make_no_window_and_leaving_it_drops_the_window_in_progress()
    {
        var s = new FightFrameSampler();
        for (var i = 0; i < 100; i++)
        {
            s.Step(Frame, false, false, false, false, inDen: false, skip: false);
        }

        Assert.Null(s.BsdMinFps);
        Assert.Equal(0, s.DenFrames);

        for (var i = 0; i < 30; i++)
        {
            s.Step(0.030, false, false, false, false, true, false); // half a slow window ...
        }

        s.Step(Frame, false, false, false, false, inDen: false, skip: false); // the call that saw the player leave: its delta is the last den frame, still in the window ...
        for (var i = 0; i < 65; i++)
        {
            s.Step(1.0 / 64.0, false, false, false, false, true, false); // ... which the next call drops (its delta is the frame outside); then a second at 64 fps
        }

        Assert.Equal(1, s.Windows);
        Assert.Equal(64.0, s.BsdMinFps!.Value, 6);
    }

    [Fact]
    public void A_skipped_frame_spoils_the_window_it_fell_in()
    {
        var s = new FightFrameSampler();
        for (var i = 0; i < 30; i++)
        {
            s.Step(Frame, false, false, false, false, true, false);
        }

        s.Step(0.25, false, false, false, false, true, skip: true);
        for (var i = 0; i < 59; i++)
        {
            s.Step(Frame, false, false, false, false, true, false);
        }

        Assert.Equal(0, s.Windows); // 59 frames after the stall are less than a second
        Assert.Null(s.BsdMinFps);
    }

    [Fact]
    public void A_sampler_with_no_frames_reports_nothing_and_the_verdict_prints_n_a()
    {
        var s = new FightFrameSampler();
        var m = new FightLegMeasures(1, 10, 2, 0, 5, 0, 0, 0, null, s.CastFrameMaxMs, s.FightFrameMaxMs, s.BsdMinFps, 12.0);

        var outcome = FightLegVerdict.Compose(null, m);

        Assert.True(outcome.Ok);
        Assert.Contains("projectiles=n/a cast_frame_max_ms=n/a fight_frame_max_ms=n/a bsd_min_fps=n/a fight_s=12", outcome.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sampler_allocates_nothing_per_frame()
    {
        var s = new FightFrameSampler();
        for (var i = 0; i < 500; i++)
        {
            s.Step(Frame, i % 3 == 0, i % 5 == 0, i % 7 == 0, true, true, i % 97 == 0); // warm the code
        }

        var window = AllocationWindow.Open();
        for (var i = 0; i < 5_000; i++)
        {
            s.Step(Frame + ((i % 11) * 0.0001), i % 3 == 0, i % 5 == 0, i % 7 == 0, i % 2 == 0, i % 13 != 0, i % 97 == 0);
        }

        var (bytes, gc) = window.Close();
        Assert.True(gc || bytes == 0, $"the sampler allocated {bytes} B over 5000 frames");
    }

    // ---------------------------------------------------------------------------------------------- the review frames' plan

    [Fact]
    public void The_plan_names_the_three_frames_and_their_files()
    {
        Assert.Equal(["bolt", "hit", "kill"], FightShotPlan.Names);
        Assert.Equal("cache/m3/3-1/fight-bolt.png", FightShotPlan.ShotPath("cache/m3/3-1/fight.png", 0).Replace('\\', '/'));
        Assert.Equal("cache/m3/3-1/fight-hit.png", FightShotPlan.ShotPath("cache/m3/3-1/fight.png", 1).Replace('\\', '/'));
        Assert.Equal("fight-kill.png", FightShotPlan.ShotPath("fight", 2));
        Assert.Equal([0.15, 0.1, 0.5], FightShotPlan.DelaysSeconds);
    }

    [Fact]
    public void The_bolt_frame_waits_for_a_projectile_created_after_the_first_damage_line()
    {
        var p = new FightShotPlan();
        p.Begin(projectilesCreated: 4, projectileExplodes: 7, damageLines: 2, kills: 0);
        var t = 100.0;
        Assert.Equal(-1, p.Step(t, 4, 7, 2, 0)); // nothing rose
        Assert.False(p.Owed);

        // The first casts (created, no damage line yet: they hit a wall) trigger nothing.
        Assert.Equal(-1, p.Step(t += Frame, 5, 7, 2, 0));
        Assert.Equal(-1, p.Step(t += Frame, 6, 7, 2, 0));
        Assert.False(p.Owed);

        // The first damage line: the mark. A projectile created after it is the bolt.
        Assert.Equal(-1, p.Step(t += Frame, 6, 7, 3, 0));
        Assert.False(p.Owed);
        var triggered = t + Frame;
        Assert.Equal(-1, p.Step(triggered, 7, 7, 3, 0)); // created: its trigger, nothing is due yet
        Assert.True(p.Owed);
        var due = -1;
        var frames = 0;
        while (due < 0 && frames++ < 100)
        {
            due = p.Step(t += Frame, 7, 7, 3, 0);
        }

        Assert.Equal(0, due);
        Assert.True(t >= triggered + 0.15, "due before its delay: " + (t - triggered));
        Assert.True(frames >= FightShotPlan.HiddenFramesBeforeShot);
        p.MarkTaken(0);
        Assert.False(p.Owed);
        Assert.Equal(1, p.Taken);
        Assert.Equal(-1, p.Step(t += Frame, 7, 7, 3, 0)); // a trigger fires once
    }

    [Fact]
    public void An_explode_on_a_wall_gives_no_hit_frame_and_an_explode_with_a_damage_line_does_in_either_order()
    {
        var p = new FightShotPlan();
        p.Begin(0, 0, 0, 0);
        var t = 10.0;
        p.Step(t, 1, 1, 0, 0); // a bolt exploded on a wall: no damage line follows
        for (var i = 0; i < 180; i++) // three seconds later
        {
            Assert.Equal(-1, p.Step(t += Frame, 1, 1, 0, 0));
        }

        Assert.False(p.Owed);
        Assert.Contains("hit=trigger never came", p.Note(), StringComparison.Ordinal);

        // The damage line comes 0.3 s after the next Explode: the Explode is the trigger.
        var explodedAt = t += Frame;
        p.Step(explodedAt, 2, 2, 0, 0);
        for (var i = 0; i < 18; i++)
        {
            p.Step(t += Frame, 2, 2, 0, 0);
        }

        p.Step(t += Frame, 2, 2, 1, 0);
        Assert.True(p.Owed);
        var due = -1;
        for (var i = 0; i < 100 && due != 1; i++)
        {
            due = p.Step(t += Frame, 2, 2, 1, 0);
        }

        Assert.Equal(1, due);
        Assert.True(t >= explodedAt + 0.1);

        // A trigger fires once: a later pair asks for no second hit frame.
        p.MarkTaken(1);
        p.Step(t += Frame, 3, 3, 2, 0);
        Assert.Equal(1, p.Taken);
    }

    [Fact]
    public void A_damage_line_then_an_explode_within_a_second_is_a_hit_and_one_later_is_not()
    {
        var late = new FightShotPlan();
        late.Begin(0, 0, 0, 0);
        late.Step(1.0, 0, 0, 1, 0); // a damage line
        late.Step(1.0 + FightShotPlan.PairSeconds + 0.5, 0, 1, 1, 0); // an Explode too late to be its hit
        Assert.False(late.Owed);

        var p = new FightShotPlan();
        p.Begin(0, 0, 0, 0);
        p.Step(1.0, 0, 0, 1, 0);
        p.Step(1.5, 0, 1, 1, 0); // the Explode 0.5 s after the damage line
        Assert.True(p.Owed);
    }

    [Fact]
    public void A_hit_and_a_kill_in_one_frame_give_one_frame_at_a_time()
    {
        var p = new FightShotPlan();
        p.Begin(0, 0, 0, 0);
        var t = 10.0;
        p.Step(t, 0, 1, 1, 1); // the hit and the kill triggers at once (the bolt waits for a projectile created after the damage line)
        p.Step(t += Frame, 1, 1, 1, 1); // the bolt's trigger
        var order = new List<int>();
        for (var i = 0; i < 100 && order.Count < 3; i++)
        {
            var due = p.Step(t += Frame, 1, 1, 1, 1);
            if (due >= 0)
            {
                order.Add(due);
                p.MarkTaken(due);
            }
        }

        Assert.Equal([1, 0, 2], order); // the hit (0.1 s) before the bolt (0.15 s) before the kill line (0.5 s)
        Assert.False(p.Owed);
        Assert.Equal(3, p.Taken);
    }

    [Fact]
    public void A_frame_not_taken_within_the_hold_is_given_up_and_a_trigger_that_never_came_is_named()
    {
        var p = new FightShotPlan();
        p.Begin(0, 0, 0, 0);
        var t = 0.0;
        p.Step(t, 0, 0, 1, 0); // the first damage line
        p.Step(t += Frame, 1, 0, 1, 0); // the bolt created after it
        for (var i = 0; i < 400 && p.Owed; i++)
        {
            t += Frame * 4; // the caller never saves the frame
            _ = p.Step(t, 1, 0, 1, 0);
        }

        Assert.False(p.Owed);
        Assert.Equal(0, p.Taken);
        var note = p.Note();
        Assert.Contains("bolt=not taken (not taken within the hold after its trigger)", note, StringComparison.Ordinal);
        Assert.Contains("hit=trigger never came", note, StringComparison.Ordinal);
        Assert.Contains("kill=trigger never came", note, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_save_is_named_and_a_plan_not_begun_triggers_nothing()
    {
        var idle = new FightShotPlan();
        Assert.Equal(-1, idle.Step(1.0, 5, 5, 5, 5));
        Assert.False(idle.Owed);

        var p = new FightShotPlan();
        p.Begin(0, 0, 0, 0);
        p.Step(0, 0, 0, 0, 1);
        p.MarkFailed(2, "the frame could not be saved");
        Assert.False(p.Owed);
        Assert.Contains("kill=not taken (the frame could not be saved)", p.Note(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_plan_allocates_nothing_per_frame()
    {
        var p = new FightShotPlan();
        p.Begin(0, 0, 0, 0);
        var t = 0.0;
        for (var i = 0; i < 100; i++)
        {
            p.Step(t += Frame, 0, 0, 0, 0);
        }

        var window = AllocationWindow.Open();
        for (var i = 0; i < 5_000; i++)
        {
            p.Step(t += Frame, 0, 0, 0, 0);
        }

        var (bytes, gc) = window.Close();
        Assert.True(gc || bytes == 0, $"the plan allocated {bytes} B over 5000 frames");
    }

    // ---------------------------------------------------------------------------------------------- the verdict wiring

    [Fact]
    public void The_verdict_judges_the_tallys_kills_and_damage_and_reports_the_samplers_fields()
    {
        var s = new FightFrameSampler();
        s.Step(Frame, true, true, true, true, true, false); // the windows opened this frame
        s.Step(0.0334, true, true, true, true, true, false); // their first frame
        for (var i = 0; i < 60; i++)
        {
            s.Step(Frame, false, false, false, true, true, false);
        }

        var good = FightLegVerdict.Compose(null, new FightLegMeasures(1, 322, 6, 3, 30_646, 0, 0, 0, 6, s.CastFrameMaxMs, s.FightFrameMaxMs, s.BsdMinFps, 34.2));
        Assert.True(good.Ok);
        Assert.Matches(new Regex(@"cast_frame_max_ms=33\.4 fight_frame_max_ms=33\.4 bsd_min_fps=(5\d|60)\.\d fight_s=34$"), good.Line);

        var noDamage = FightLegVerdict.Compose(null, new FightLegMeasures(1, 0, 1, 0, 30_646, 0, 0, 0, 1, null, null, null, 3.0));
        Assert.False(noDamage.Ok);
        Assert.Contains(noDamage.Problems, p => p.StartsWith("damage_dealt=0", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------------- the options

    [Fact]
    public void The_fight_legs_review_frame_path_is_routed_like_the_buff_legs_and_only_without_a_delay()
    {
        var fight = PlayOptions.Parse(["--login-check=bsd-fight", "--play-screenshot=cache/m3/3-1/fight.png"]);
        var late = PlayOptions.Parse(["--login-check=bsd-fight", "--play-screenshot=cache/m3/3-1/fight.png", "--play-screenshot-after=9"]);
        var enter = PlayOptions.Parse(["--login-check=enter", "--play-screenshot=cache/x.png"]);

        Assert.Equal("cache/m3/3-1/fight.png", fight.BuffShotPath);
        Assert.Null(fight.ScreenshotPath); // play mode's own capture (a fixed wait, then quit) must not fire
        Assert.Null(late.BuffShotPath);
        Assert.Equal("cache/m3/3-1/fight.png", late.ScreenshotPath);
        Assert.Null(enter.BuffShotPath);
    }

    [Fact]
    public void The_retail_sphereless_missile_flag_is_registered_and_parsed()
    {
        Assert.False(PlayOptions.Parse([]).RetailSphereless);
        Assert.True(PlayOptions.Parse(["--retail-sphereless-missile"]).RetailSphereless);
        Assert.True(OptionCatalog.Registry.IsKnown("--retail-sphereless-missile"));
        Assert.Contains("RetailSpherelessMissile = _services.Options.Play.RetailSphereless", File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "PlayMode.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_arena_here_option_is_in_the_record_and_not_on_the_command_line()
    {
        var parsed = FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-kills=2", "--fight-seconds=45"]));

        Assert.False(parsed.ArenaHere);
        Assert.Equal(FightOptions.DefaultArenaSkill, parsed.ArenaSkill);
        Assert.Equal("kills=2 seconds=45", parsed.ToString());
        Assert.Equal("kills=1 seconds=300 arena=here skill=300", new FightOptions { ArenaHere = true }.ToString());
        Assert.DoesNotContain(OptionCatalog.Registry.Names, n => n.Contains("arena", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------------- the smoke step

    [Fact]
    public void The_smoke_step_login_bsd_is_a_backend_step_that_runs_only_when_the_backend_is_required()
    {
        var script = Smoke;
        var steps = Regex.Match(script, @"\$allSteps = @\(([^)]*)\)", RegexOptions.Singleline).Groups[1].Value;
        var backend = Regex.Match(script, @"\$backendSteps = @\(([^)]*)\)").Groups[1].Value;

        Assert.Contains("'login-bsd'", steps, StringComparison.Ordinal);
        Assert.Contains("'login-bsd'", backend, StringComparison.Ordinal);
        Assert.True(steps.IndexOf("'login-buff'", StringComparison.Ordinal) < steps.IndexOf("'login-bsd'", StringComparison.Ordinal));

        var skip = script.IndexOf("if ($step -eq 'login-bsd' -and $Backend -ne 'require')", StringComparison.Ordinal);
        var gate = script.IndexOf("if ($backendSteps -contains $step -and -not (Test-BackendGate", StringComparison.Ordinal);
        Assert.True(skip > 0 && skip < gate, "the login-bsd skip rule must come before the backend gate");
        var rule = script[skip..gate];
        Assert.Contains("-Skipped", rule, StringComparison.Ordinal);
        Assert.Contains("continue", rule, StringComparison.Ordinal);

        var body = script[script.IndexOf("'login-bsd' {", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("'login' {", StringComparison.Ordinal)];
        Assert.Contains("--account=test --login-check=bsd-fight", body, StringComparison.Ordinal);
        Assert.Contains("bsd-fight done ok=true", body, StringComparison.Ordinal);
        Assert.Contains("$verdict", body, StringComparison.Ordinal);
        Assert.Contains("login-bsd ", script[..script.IndexOf("$allSteps", StringComparison.Ordinal)], StringComparison.Ordinal); // its help line
    }

    [Fact]
    public void Slice_check_never_runs_the_fight_step()
    {
        var sliceCheck = File.ReadAllText(RepositoryFiles.PathOf("tools", "slice-check.ps1"));

        Assert.Contains("[ValidateSet('auto')]", sliceCheck, StringComparison.Ordinal);
        Assert.DoesNotContain("login-bsd", sliceCheck, StringComparison.Ordinal);
    }

    [Fact]
    public void The_smoke_gives_the_leg_its_whole_time_and_the_time_to_log_off_after_a_failure()
    {
        // The runner's limit is the sum of its stage limits plus --fight-seconds' default; the Godot run must outlast it plus the recovery.
        var runner = Runner;
        double Constant(string name) => double.Parse(Regex.Match(runner, @"public const double " + name + @" = (\d+(\.\d+)?);").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var whole = Constant("LoginSeconds") + Constant("GearSeconds") + Constant("TopUpSeconds") + Constant("PlaceSeconds") + FightOptions.DefaultSeconds;
        var recovery = Constant("RecoverySeconds");
        var timeout = double.Parse(Regex.Match(Smoke, @"--login-check=bsd-fight' (\d+) ").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(750.0, whole);
        Assert.Contains("WholeCheckSeconds = LoginSeconds + GearSeconds + TopUpSeconds + PlaceSeconds + FightOptions.DefaultSeconds", runner, StringComparison.Ordinal);
        Assert.True(timeout >= whole + recovery + 300, $"the smoke gives the leg {timeout} s for {whole} s plus {recovery} s of recovery and the start-up");
    }

    [Fact]
    public void The_runner_is_hooked_the_play_path_is_warmed_and_the_frame_code_allocates_nothing()
    {
        var runner = Runner;
        var hook = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "LoginCheckRunner.cs"));

        Assert.Contains("kind == LoginCheckKind.BsdFight", hook, StringComparison.Ordinal);
        Assert.Contains("FightLegRunner.Install(main, services, host, mode, ui)", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO(m3-fight-leg)", runner, StringComparison.Ordinal);
        Assert.Contains("RuntimeHelpers.PrepareMethod", runner, StringComparison.Ordinal); // the fight's methods are compiled on the loading worker
        Assert.Contains("mode.ScenarioHost = walkHost", runner, StringComparison.Ordinal);
        Assert.Contains("FightStages.All", runner, StringComparison.Ordinal);

        // SampleFrame and StepShot run every frame: no LINQ, no string building and no lambda in them.
        foreach (var method in new[] { "private void SampleFrame(", "private void StepShot(" })
        {
            var start = runner.IndexOf(method, StringComparison.Ordinal);
            Assert.True(start > 0, method);
            var body = runner[start..runner.IndexOf("\n    }\n", start, StringComparison.Ordinal)];
            Assert.DoesNotContain(".Select(", body, StringComparison.Ordinal);
            Assert.DoesNotContain(".Where(", body, StringComparison.Ordinal);
            Assert.DoesNotContain("=>", body, StringComparison.Ordinal);
        }
    }
}
