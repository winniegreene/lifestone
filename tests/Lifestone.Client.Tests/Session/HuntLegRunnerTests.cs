using System.Text.RegularExpressions;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// What the hunt leg (<c>--login-check=hunt</c>, M4 1.2 s4b) holds without Godot or a server: the option catalogue's help, the hook in the login-check runner, the runner's limits and its per-frame code. The stage machine
/// itself is not tested offline (the game's player and the live ACE are its proof: the lead's live leg); the hunt scenario it runs is tested by <c>HuntScenarioTests</c> on the stand-in.
/// </summary>
public sealed class HuntLegRunnerTests
{
    private static string Runner => File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "HuntLegRunner.cs"));

    [Fact]
    public void The_runner_is_hooked_as_a_kind_of_its_own_and_not_in_the_all_path()
    {
        var hook = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "LoginCheckRunner.cs"));

        Assert.Contains("kind == LoginCheckKind.Hunt", hook, StringComparison.Ordinal);
        Assert.Contains("HuntLegRunner.Install(main, services, host, mode, ui)", hook, StringComparison.Ordinal);
        Assert.True(hook.IndexOf("HuntLegRunner.Install", StringComparison.Ordinal) < hook.IndexOf("AcademyExitScenario(", StringComparison.Ordinal), "the hunt hook must return before the movement legs are built");
    }

    [Fact]
    public void The_hunt_options_are_registered()
    {
        foreach (var name in new[] { "--hunt-kills", "--hunt-seconds", "--hunt-weapon" })
        {
            Assert.True(OptionCatalog.Registry.IsKnown(name), name);
        }
    }

    [Fact]
    public void The_runner_passes_the_hunt_options_through_to_the_scenario_and_runs_it_without_ending_the_session()
    {
        var runner = Runner;

        Assert.Contains("HuntOptions.FromArguments(CommandLineArgument.ParseAll(OS.GetCmdlineUserArgs()))", runner, StringComparison.Ordinal);
        Assert.Contains("new HuntScenario(_options,", runner, StringComparison.Ordinal);
        Assert.Contains("endSession: false", runner, StringComparison.Ordinal); // the leg logs off itself
        Assert.Contains("HuntLegVerdict.Compose(", runner, StringComparison.Ordinal);
        Assert.Contains("HuntScenario.NameCheck(c.Name, _template)", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void The_whole_check_limit_is_the_sum_of_its_stage_limits_with_the_default_hunt_seconds()
    {
        var runner = Runner;
        double Constant(string name) => double.Parse(Regex.Match(runner, @"public const double " + name + @" = (\d+(\.\d+)?);").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        var hunting = Constant("LoginSeconds") + Constant("PrepareSeconds") + HuntOptions.DefaultSeconds + Constant("WindDownSeconds");

        Assert.Equal(780.0, hunting); // the hunt with no shop visits
        // 3.2-R15: the sale (up to four batches) and the purchase add the visits' bound, 420 s: the default check (--sell on, --buy-kits 2) is 1200 s.
        Assert.Equal(420.0, HuntScenario.VendorTripsSeconds);
        Assert.Equal(1200.0, hunting + HuntScenario.VendorTripsSeconds);
        Assert.Contains("WholeCheckSeconds = LoginSeconds + PrepareSeconds + HuntOptions.DefaultSeconds + WindDownSeconds + HuntScenario.VendorTripsSeconds", runner, StringComparison.Ordinal);
        Assert.Contains("_wholeSeconds = WholeCheckSecondsFor(options.Seconds, options.Sell || options.BuyKits > 0)", runner, StringComparison.Ordinal);
        Assert.Contains("(vendorTrips ? HuntScenario.VendorTripsSeconds : 0.0)", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void The_review_frame_code_holds_the_scenario_hides_the_hud_and_logs_what_the_brief_names()
    {
        var runner = Runner;

        Assert.Contains("HuntShotPlan.ShotPath(_shotPath, _plan.Slot)", runner, StringComparison.Ordinal);
        Assert.Contains("PlayMode.PlayShotDeveloperHudNames", runner, StringComparison.Ordinal);
        Assert.Contains("if (_plan.Owed || _uiPlan.Owed)", runner, StringComparison.Ordinal);
        foreach (var field in new[] { "(\"frame\", \"hunt-\" + _plan.Name)", "(\"trigger\"", "(\"target\"", "(\"target_m\"", "(\"weapon_wcid\"", "(\"developer_hud\"" })
        {
            Assert.Contains(field, runner, StringComparison.Ordinal);
        }

        // StepShot runs every frame of the hunt: no LINQ and no lambda in it.
        var start = runner.IndexOf("private void StepShot(", StringComparison.Ordinal);
        Assert.True(start > 0);
        var body = runner[start..runner.IndexOf("\n    }\n", start, StringComparison.Ordinal)];
        Assert.DoesNotContain(".Select(", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Where(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("=>", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_melee_leg_takes_the_three_retail_ui_frames_with_the_camera_framed_and_logs_both_meter_positions()
    {
        var runner = Runner;

        // M4 1.3 s2: c1-combat, c1-powerbar and meter, with the camera framed for them (m4-hunt-frame-camera) and put back at the end.
        Assert.Contains("HuntUiShotPlan.ShotPath(_shotPath, slot)", runner, StringComparison.Ordinal);
        Assert.Contains("StepUiShots(session, scenario);", runner, StringComparison.Ordinal);
        Assert.Contains("GameCombatPanel.WindowId", runner, StringComparison.Ordinal);
        Assert.Contains("GameCombatPanel.MeterId", runner, StringComparison.Ordinal);
        Assert.Contains("HuntUiShotPlan.CameraFramed(pivotDistance", runner, StringComparison.Ordinal);
        Assert.Contains("rig.Chase.Model.Scale = HuntUiShotPlan.FramedScale;", runner, StringComparison.Ordinal);
        Assert.Contains("rig.Chase.Model.Scale = _cameraScaleBefore;", runner, StringComparison.Ordinal);
        Assert.Contains("RestoreCamera();", runner, StringComparison.Ordinal);
        foreach (var field in new[] { "(\"camera_scale\"", "(\"camera_pivot_m\"", "(\"camera_blocked\"", "(\"target_m\"", "(\"health_meter_at_selection\"", "(\"health_meter_now\"", "(\"combat_meter\"", "(\"run_power\"" })
        {
            Assert.Contains(field, runner, StringComparison.Ordinal);
        }

        // The sampler for attack_frame_max_ms is stepped every frame, attached to the session's events and its worst frame goes into the verdict.
        Assert.Contains("_attackFrames.Step(delta, _skipNextFrame);", runner, StringComparison.Ordinal);
        Assert.Contains("_attackFrames.Attach(session.Events)", runner, StringComparison.Ordinal);
        Assert.Contains("_attackFrames.AttackFrameMaxMs", runner, StringComparison.Ordinal);
        Assert.Contains("_options.Weapon,", runner, StringComparison.Ordinal);

        // StepUiShots runs every frame of the review hunt: no LINQ in it, and only the hunt-leg-local helpers.
        var start = runner.IndexOf("private void StepUiShots(", StringComparison.Ordinal);
        Assert.True(start > 0);
        var body = runner[start..runner.IndexOf("\n    }\n", start, StringComparison.Ordinal)];
        Assert.DoesNotContain(".Any(", body, StringComparison.Ordinal); // (the selection's own .Select(target) is the click on the target)
        Assert.DoesNotContain(".Where(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_corpse_frame_waits_for_the_drawn_container_window_inside_the_hold_and_logs_the_window_and_the_corpses_objdesc()
    {
        var runner = Runner;
        var start = runner.IndexOf("private void StepUiShots(", StringComparison.Ordinal);
        var body = runner[start..runner.IndexOf("\n    }\n", start, StringComparison.Ordinal)];

        // M4 2.2 s4 (DoD 13): the slot stays owed (never dropped) until the window is shown with every entry listed and every icon drawn; the hold's end gives it up by name.
        Assert.Contains("GameContainerPanel.WindowId", body, StringComparison.Ordinal);
        var wait = body.IndexOf("ContainerWindowDraw.Drawn", StringComparison.Ordinal);
        Assert.True(wait > 0, "the corpse frame must wait for the drawn window");
        Assert.True(wait < body.IndexOf("var pose = _mode.Rig?.Chase.Pose;", StringComparison.Ordinal), "the wait comes before the projection rule");
        Assert.Contains("_uiPlan.IsGivenUp(HuntUiShotPlan.Corpse)", body, StringComparison.Ordinal);

        // The frame's line carries the window's title and cell count and the corpse's ObjDesc counts (q6-corpse-objdesc-live).
        Assert.Contains("CorpseText(session, scenario, container)", body, StringComparison.Ordinal);
        Assert.Contains("window '{window?.Title", runner, StringComparison.Ordinal);
        Assert.Contains("cells={window?.ItemRow?.CellCount", runner, StringComparison.Ordinal);
        Assert.Contains("HuntUiShotPlan.ObjDescFacts(corpse!.Created.Model", runner, StringComparison.Ordinal);
        Assert.Contains("with {view.Entries.Count} item(s)", runner, StringComparison.Ordinal); // the earlier fields stay
    }

    [Fact]
    public void The_hunt_leg_is_smoke_s_login_hunt_step()
    {
        var smoke = File.ReadAllText(RepositoryFiles.PathOf("tools", "smoke.ps1"));

        Assert.Contains("--login-check=hunt", smoke, StringComparison.Ordinal); // M4 1.2 s5: the login-hunt step (-Backend require only)
    }

    [Fact]
    public void M4_3_1_s4_the_runner_starts_lane_g_s_combat_mode_watch_with_the_hunt_and_prints_its_count_report_only()
    {
        var runner = Runner.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("private readonly CombatModeWatch _modeWatch = new();", runner, StringComparison.Ordinal); // beside the other samplers
        var begin = runner.IndexOf("private void BeginScenario(", StringComparison.Ordinal);
        var start = runner.IndexOf("_modeWatch.Start(session);", begin, StringComparison.Ordinal);
        Assert.True(begin > 0 && start > begin && start < runner.IndexOf("Go(Phase.Hunt);", begin, StringComparison.Ordinal), "the watch starts with the leg, before the hunt phase");
        Assert.Contains("_modeWatch.Dispose();", runner[runner.IndexOf("private void DisposeSubscriptions()", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains("_options.Ground,\n            _modeWatch.Violations,", runner, StringComparison.Ordinal); // the verdict line prints it after ground= (3.2 s4a: the sale's fields follow it in the measures)
        Assert.Contains("_vendorFrames.VendorFrameMaxMs,", runner, StringComparison.Ordinal); // the measures' last argument now (M4 3.2 s4a)
        Assert.Contains("_vendorFrames.Track(scenario.VendorWindow);", runner, StringComparison.Ordinal);
        Assert.Contains("_vendorFrames.Step(delta, _skipNextFrame);", runner, StringComparison.Ordinal);
        Assert.Contains("(\"mode_mismatch_first\", _modeWatch.FirstViolation ?? \"none\")", runner, StringComparison.Ordinal); // the first disagreement in the hunt detail line
        Assert.DoesNotContain("_modeWatch.Violations > 0", runner, StringComparison.Ordinal); // report-only: nothing in the runner judges it
    }

    [Fact]
    public void M4_2_2_fix2_R15_the_held_scenario_still_runs_the_hunts_guard_and_the_abandon_ends_the_corpse_frames_hold()
    {
        // The runner holds the scenario while a review frame is owed; the guard alone runs then (the death trip's GuardWhileHeld, as 2.2 fix R5), and an abandoned corpse sequence releases the corpse slot.
        var runner = Runner.Replace("\r\n", "\n", StringComparison.Ordinal);
        var held = runner.IndexOf("if (_plan.Owed || _uiPlan.Owed)", StringComparison.Ordinal);
        var step = runner.IndexOf("scenario.Step(session, PortalSpaceSetup.Now())", held, StringComparison.Ordinal);
        Assert.True(held > 0 && step > held);
        var hold = runner[held..step];
        Assert.Contains("scenario.GuardWhileHeld(session)", hold, StringComparison.Ordinal);
        Assert.Contains("_uiPlan.ReleaseCorpse(", hold, StringComparison.Ordinal);
        Assert.DoesNotContain("scenario.Step(", hold, StringComparison.Ordinal); // never a step of the hunt while held
    }
}
