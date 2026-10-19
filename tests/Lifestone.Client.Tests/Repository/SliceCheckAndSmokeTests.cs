using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Lifestone.Client.Options;
using Lifestone.Client.Play;

namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// The testing loop's script-side cuts (M3 4.2 p5a, decisions D41 and D42): the default test filter and the Exhaustive stage, the per-test timings, one smoke process for slice-check's steps,
/// the step map, the bounded secret scan, the shared play-checks and record-audio run, the shell-frames run without the scenarios, and the frame-time checks judged against the machine. They read
/// tools/slice-check.ps1, tools/smoke.ps1 and tools/lib (text) and run their pure functions in PowerShell (cut out of the script, as BuffLegJudgesTests does); no Godot, no backend and no dats.
/// </summary>
public sealed class SliceCheckAndSmokeTests
{
    private static string Smoke => File.ReadAllText(RepositoryFiles.PathOf("tools", "smoke.ps1"));

    private static string SliceCheck => File.ReadAllText(RepositoryFiles.PathOf("tools", "slice-check.ps1"));

    private static string StepPaths => File.ReadAllText(RepositoryFiles.PathOf("tools", "lib", "StepPaths.psd1"));

    private const string DefaultFilter = "Category!=Loopback&Tier!=Soak&Tier!=Exhaustive";

    // ------------------------------------------------------------------------------------------ C1: the default filter and the Exhaustive stage

    [Fact]
    public void Both_scripts_carry_the_same_default_filter_without_the_exhaustive_tier()
    {
        Assert.Contains("$testFilter = '" + DefaultFilter + "'", Smoke, StringComparison.Ordinal);
        Assert.Contains("$testFilter = '" + DefaultFilter + "'", SliceCheck, StringComparison.Ordinal);
    }

    [Fact]
    public void Slice_check_has_an_exhaustive_stage_that_the_smoke_switch_implies()
    {
        var script = SliceCheck;

        Assert.Contains("$exhaustiveFilter = 'Tier=Exhaustive'", script, StringComparison.Ordinal);
        Assert.Contains("[switch] $Exhaustive", script, StringComparison.Ordinal);
        Assert.Matches(@"\$allStages = @\('build', 'test', 'exhaustive', 'steps', 'smoke'\)", script);
        Assert.Contains("if ($Exhaustive -or $Smoke) { \"exhaustive\" }", script, StringComparison.Ordinal);
        Assert.Contains("'exhaustive' { Invoke-TestRun 'exhaustive' $exhaustiveFilter 0 | Out-Null }", script, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------ C6: per-test timings

    [Fact]
    public void Both_default_test_runs_write_a_trx_into_their_own_folder()
    {
        var smoke = Smoke;
        var test = smoke[smoke.IndexOf("'test' {", StringComparison.Ordinal)..];
        test = test[..test.IndexOf("'cli-help' {", StringComparison.Ordinal)];

        Assert.Contains("'--logger', 'trx;LogFilePrefix=test', '--results-directory', $trxDir", test, StringComparison.Ordinal);
        Assert.Contains("test-times.txt", test, StringComparison.Ordinal);
        Assert.Contains("'--logger', 'trx;LogFilePrefix=test', '--results-directory', $trx", SliceCheck, StringComparison.Ordinal);
        Assert.Contains("at_s={4:0}", SliceCheck, StringComparison.Ordinal); // the stage clock on every stage line
    }

    [Fact]
    public void The_trx_reader_lists_the_slowest_tests_first_and_names_them_short()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lifestone-trx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "test_a.trx"), """
                <?xml version="1.0" encoding="utf-8"?>
                <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>
                <UnitTestResult testName="Lifestone.Data.Tests.Textures.SlowTests.The_slow_one(x: 1)" duration="00:01:06.5000000" outcome="Passed" />
                <UnitTestResult testName="Lifestone.Data.Tests.FastTests.The_fast_one" duration="00:00:00.0100000" outcome="Passed" />
                <UnitTestResult testName="Lifestone.Net.Tests.MidTests.The_mid_one" duration="00:00:03.2000000" outcome="Failed" />
                </Results></TestRun>
                """);
            var module = RepositoryFiles.PathOf("tools", "lib", "TestTimes.psm1");
            var run = RunPowerShell($"Import-Module '{module}' -Force; $t = @(Get-TrxTimes '{folder}'); Write-Output ('COUNT=' + $t.Count); Format-SlowestLines $t 2 | ForEach-Object {{ Write-Output ('LINE=' + $_.Trim()) }}; Write-Output ('SUMMARY=' + (Format-SlowestSummary $t 20 2))");

            Assert.Contains("COUNT=3", run, StringComparison.Ordinal);
            Assert.Contains("LINE=66.50  SlowTests.The_slow_one", run, StringComparison.Ordinal);
            Assert.Contains("LINE=3.20  MidTests.The_mid_one", run, StringComparison.Ordinal);
            Assert.Contains("SUMMARY=top 20 slowest (s): SlowTests.The_slow_one 66.5, MidTests.The_mid_one 3.2; top 20 sum 69.7 s of 3 tests", run, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ------------------------------------------------------------------------------------------ C7: one smoke process, the step map

    [Fact]
    public void Slice_check_makes_one_smoke_call_for_all_its_steps_and_one_for_the_rerun()
    {
        var script = SliceCheck;
        var stage = script[script.IndexOf("function Invoke-StepsStage", StringComparison.Ordinal)..];
        stage = stage[..stage.IndexOf("function Invoke-SmokeStage", StringComparison.Ordinal)];

        Assert.Single(Regex.Matches(stage, @"Invoke-Smoke @\('-Only', \(\$stepList -join ','\)\) 'steps'"));
        Assert.Single(Regex.Matches(stage, @"Invoke-Smoke @\('-Only', \(\$failed -join ','\)\) 'steps-rerun'"));
        Assert.DoesNotContain("foreach ($step in $Steps)", stage, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(script, @"= Invoke-Smoke ").Count); // steps, the rerun, the whole smoke: nothing per step
    }

    [Fact]
    public void The_smoke_stage_is_the_runs_only_test_and_passes_the_gate_flags()
    {
        var script = SliceCheck;

        Assert.Contains("Invoke-Smoke @('-SoloFrames', '-FullScan') 'smoke'", script, StringComparison.Ordinal);
        Assert.Contains("'test' { if ($Stages -notcontains 'smoke') { Invoke-TestRun 'test' $testFilter $MinTests | Out-Null } }", script, StringComparison.Ordinal);
        Assert.Contains("Read-SmokeRow $run 'test'", script, StringComparison.Ordinal); // the test line comes from smoke's test row, judged by -MinTests
        Assert.DoesNotContain("login-buff", script, StringComparison.Ordinal); // BuffLegJudgesTests: slice-check never runs the live legs
        Assert.DoesNotContain("login-death", script, StringComparison.Ordinal);
        Assert.DoesNotContain("login-bsd", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_step_in_the_map_is_a_smoke_step_and_every_backend_free_smoke_step_is_in_the_map()
    {
        var smoke = Smoke;
        var allSteps = Quoted(Regex.Match(smoke, @"\$allSteps = @\(([^)]*)\)", RegexOptions.Singleline).Groups[1].Value);
        var backend = Quoted(Regex.Match(smoke, @"\$backendSteps = @\(([^)]*)\)").Groups[1].Value);
        var map = StepPaths;
        var keys = Regex.Matches(map[map.IndexOf("Steps = @{", StringComparison.Ordinal)..map.IndexOf("Everything = @(", StringComparison.Ordinal)], @"^\s+'([a-z-]+)'\s+= @\(", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToList();

        Assert.All(keys, key => Assert.Contains(key, allSteps));
        Assert.Equal(keys.Count, keys.Distinct().Count());
        var needGodot = allSteps.Where(s => s is not ("build" or "test" or "stray-output") && !backend.Contains(s)).ToList();
        Assert.Equal(needGodot.Order(StringComparer.Ordinal), keys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("login-buff", map, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_source_folder_is_mapped_listed_as_cross_cutting_or_listed_as_needing_no_godot_step()
    {
        var map = StepPaths;
        var prefixes = Regex.Matches(map, @"'((?:src|tools)/[^']*)'").Select(m => m.Groups[1].Value).Distinct().ToList();
        var roots = new[] { ("src", "Lifestone.Game", "scripts"), ("src", "Lifestone.Client", ""), ("src", "Lifestone.Data", ""), ("src", "Lifestone.Net", ""), ("src", "Lifestone.Headless", "") };
        var missing = new List<string>();
        foreach (var (top, project, sub) in roots)
        {
            var folder = sub.Length == 0 ? RepositoryFiles.PathOf(top, project) : RepositoryFiles.PathOf(top, project, sub);
            var relative = sub.Length == 0 ? $"{top}/{project}/" : $"{top}/{project}/{sub}/";
            foreach (var directory in Directory.GetDirectories(folder).Select(Path.GetFileName).Where(n => n is not ("bin" or "obj")))
            {
                var path = relative + directory + "/";
                if (!prefixes.Any(p => path.StartsWith(p, StringComparison.Ordinal) || p.StartsWith(path, StringComparison.Ordinal)) && !prefixes.Contains($"src/{project}/"))
                {
                    missing.Add(path);
                }
            }
        }

        Assert.True(missing.Count == 0, "source folders no rule of tools/lib/StepPaths.psd1 names (add each to a step, Everything or NoGodot): " + string.Join(", ", missing));
    }

    // ------------------------------------------------------------------------------------------ C3 and C4: the shared play-checks run, shell-frames without the scenarios

    [Fact]
    public void The_play_checks_and_record_audio_steps_share_one_godot_run_and_keep_their_judges()
    {
        var script = Smoke;
        var shared = Function(script, "Get-PlayChecksRun");
        var play = script[script.IndexOf("'play-checks' {", StringComparison.Ordinal)..];
        play = play[..play.IndexOf("'record-audio' {", StringComparison.Ordinal)];
        var record = script[script.IndexOf("'record-audio' {", StringComparison.Ordinal)..];
        record = record[..record.IndexOf("'shell-frames' {", StringComparison.Ordinal)];

        // One shared call; the only other `--record-audio=` run is record-audio alone (play-checks not wanted).
        Assert.Contains("Invoke-Godot $Step \"--play-checks --record-audio=$rel\" 420 '' $defaultSize", shared, StringComparison.Ordinal);
        Assert.Contains("Get-PlayChecksRun $step $timer", play, StringComparison.Ordinal);
        Assert.Contains("Get-PlayChecksRun $step $timer", record, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(script, @"--play-checks --record-audio=\$rel").Count);

        // Each row keeps its judges.
        Assert.Contains("all play checks hold", play, StringComparison.Ordinal);
        Assert.Contains("max_frame_ms_after_arrival", play, StringComparison.Ordinal);
        Assert.Contains("Test-RecordedSources $levels.sources", record, StringComparison.Ordinal);
        Assert.Contains("-and $sourcesOk", record, StringComparison.Ordinal);
        Assert.Contains("-and $fileOk", record, StringComparison.Ordinal);

        // The failure names the half: the recording's ERRORs fail the record-audio row, the play-checks row does not count them.
        Assert.Contains("the recording half logged", play, StringComparison.Ordinal);
        Assert.Contains("the play checks half did not all hold", record, StringComparison.Ordinal);
        Assert.Contains("Get-RecordingErrorCount", play, StringComparison.Ordinal);
        Assert.Contains("Get-RecordingErrorCount", record, StringComparison.Ordinal);
        Assert.Contains("shared run", record, StringComparison.Ordinal);

        // The frames run is never joined with a judged run.
        Assert.DoesNotContain("--record-audio", script[script.IndexOf("'shell-frames' {", StringComparison.Ordinal)..script.IndexOf("'login-check' {", StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    [Fact]
    public void The_shell_frames_step_skips_the_two_scenarios_and_keeps_its_frames_and_judges()
    {
        var script = Smoke;
        var frames = script[script.IndexOf("'shell-frames' {", StringComparison.Ordinal)..script.IndexOf("'login-check' {", StringComparison.Ordinal)];

        Assert.Contains("--play-checks --play-skip-scenarios --play-frames=$dir", frames, StringComparison.Ordinal);
        Assert.Contains("$expectedFrames = 10", frames, StringComparison.Ordinal);
        Assert.Contains("shell frames done size=", frames, StringComparison.Ordinal);
        Assert.Contains("$files -eq $expectedFrames", frames, StringComparison.Ordinal);

        // The game side: the option is registered, parses, and the play checks gate their two scenario hooks on it.
        Assert.Equal(OptionScenes.Main, OptionCatalog.Registry.ScenesOf("--play-skip-scenarios"));
        Assert.True(PlayOptions.Parse(["--play-skip-scenarios"]).SkipScenarios);
        Assert.False(PlayOptions.Parse(["--play-checks"]).SkipScenarios);
        Assert.Empty(OptionCatalog.Registry.Problems(CommandLineArgument.ParseAll(["--play-checks", "--play-skip-scenarios", "--play-frames=cache/x"]), OptionScenes.Main));
        var checks = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "PlayChecks.cs"));
        Assert.Contains("if (!skipScenarios)\n        {\n            await FightChecksAsync();", checks.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("if (!skipScenarios)\n        {\n            await DeathChecksAsync();", checks.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------ D42: frame-time checks judged against the machine

    private static readonly string[] FrameChecks =
    [
        "no-frame-over-50-ms-from-the-arrival-to-the-teleport",
        "enter-right-after-character-select",
        "no-ui-step-after-enter-is-over-12-ms",
    ];

    [Fact]
    public void The_frame_time_check_list_is_the_play_checks_that_compare_a_frame_with_the_limit()
    {
        var script = Smoke;
        var listed = Quoted(script[script.IndexOf("$frameTimeChecks = @(", StringComparison.Ordinal)..script.IndexOf("# The steps -Parallel runs", StringComparison.Ordinal)]);
        var source = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "PlayChecks.cs"));

        Assert.Equal(5, listed.Count);
        Assert.All(listed, name => Assert.Contains("\"" + name + "\"", source, StringComparison.Ordinal));
        // Every check that names a frame or an ms limit is listed (the fade check is judged on the window as well).
        var named = Regex.Matches(source, "\"((?:no-frame-over-50-ms|no-ui-step|an-unhidden-object)[a-z0-9-]*)\"").Select(m => m.Groups[1].Value).Distinct();
        Assert.All(named, name => Assert.Contains(name, listed));
    }

    [Fact]
    public void A_frame_only_failure_with_another_godot_run_up_is_contended_and_with_none_it_fails()
    {
        var log = Failed("no-frame-over-50-ms-from-the-arrival-to-the-teleport") + "\n" + Failed("enter-right-after-character-select");

        var withOthers = Verdict(log, others: 1, solo: false);
        Assert.Equal(("True", "no-frame-over-50-ms-from-the-arrival-to-the-teleport,enter-right-after-character-select", "0"), (withOthers.Contended, withOthers.Frame, withOthers.Errors));
        Assert.Contains("frames=contended: no-frame-over-50-ms-from-the-arrival-to-the-teleport, enter-right-after-character-select", withOthers.Note, StringComparison.Ordinal);

        var alone = Verdict(log, others: 0, solo: false);
        Assert.Equal("False", alone.Contended);
        Assert.Equal("2", alone.Errors); // the two ERROR lines stay: it fails as today
        Assert.Contains("frame-time check(s) failed", alone.Note, StringComparison.Ordinal);

        Assert.Equal("False", Verdict(log, others: 2, solo: true).Contended); // -SoloFrames: any frame check fails
    }

    [Fact]
    public void A_failure_that_is_not_a_frame_check_is_never_contended()
    {
        var mixed = Failed("no-frame-over-50-ms-from-the-arrival-to-the-teleport") + "\n" + Failed("the-player-is-created-by-the-server");
        var verdict = Verdict(mixed, others: 3, solo: false);

        Assert.Equal("False", verdict.Contended);
        Assert.Equal("the-player-is-created-by-the-server", verdict.Other);

        // An ERROR line that is no check's is a failure of its own.
        var stray = Failed("no-ui-step-after-enter-is-over-12-ms") + "\n2026-10-19T12:00:00Z ERROR [session] something else broke";
        Assert.Equal("False", Verdict(stray, others: 1, solo: false).Contended);
    }

    [Fact]
    public void The_live_steps_frame_windows_are_contended_only_when_every_error_is_a_frame_window()
    {
        const string leg = "2026-10-19T12:00:00Z ERROR [logincheck] the bsd-fight leg failed phase=fight why=cast_frame_max_ms=81.0 (at most 75 expected); fight_frame_max_ms=77.5 (at most 75 expected)";
        const string enter = "2026-10-19T12:00:00Z ERROR [logincheck] frame over 50 ms between Enter and walking (TODO(login-burst-frame): the parts below are the frame's own)";
        const string other = "2026-10-19T12:00:00Z ERROR [logincheck] the bsd-fight leg failed phase=fight why=kills=0 (>= 1 expected); cast_frame_max_ms=81.0 (at most 75 expected)";

        Assert.Equal("True", LiveVerdict(leg, 1, false));
        Assert.Equal("True", LiveVerdict(enter, 2, false));
        Assert.Equal("False", LiveVerdict(leg, 0, false)); // no other run: fails as today
        Assert.Equal("False", LiveVerdict(leg, 1, true)); // -SoloFrames
        Assert.Equal("False", LiveVerdict(other, 1, false)); // a leg that failed for another reason too
        Assert.Equal("False", LiveVerdict("no error lines at all", 1, false));
    }

    [Fact]
    public void Solo_frames_waits_ten_minutes_for_a_quiet_machine_then_refuses()
    {
        var wait = Function(Smoke, "Wait-SoloEngine");

        Assert.Contains("-ge 600", wait, StringComparison.Ordinal);
        Assert.Contains("refused to start", wait, StringComparison.Ordinal);
        Assert.Contains("minute $($minute + 1) of 10", wait, StringComparison.Ordinal);
        Assert.Contains("if (-not $SoloFrames) { return $true }", wait, StringComparison.Ordinal);
        Assert.Contains("$SoloFrames -and $frameJudgedSteps -contains $step", Smoke, StringComparison.Ordinal);
        Assert.Contains("'-SoloFrames', '-FullScan'", SliceCheck, StringComparison.Ordinal);
    }

    [Fact]
    public void The_live_gate_implies_solo_frames_and_a_full_scan_and_the_first_line_names_the_frame_rule()
    {
        var script = Function(Smoke, "Get-FrameRule") + "\n";
        string Rule(string backend, bool solo, bool full) => RunPowerShell(script + $"$r = Get-FrameRule -Backend '{backend}' -Solo ${solo} -Full ${full}; Write-Output (\"SOLO=\" + $r.Solo + \" FULL=\" + $r.Full + \" \" + $r.Text)");

        var live = Rule("require", false, false);
        Assert.Contains("SOLO=True FULL=True", live, StringComparison.Ordinal);
        Assert.Contains("-SoloFrames implied by -Backend require", live, StringComparison.Ordinal);
        Assert.Contains("SOLO=False FULL=False frames: contended-allowed", Rule("auto", false, false), StringComparison.Ordinal);
        Assert.Contains("SOLO=True FULL=False frames: solo (-SoloFrames): a frame-time failure fails", Rule("auto", true, false), StringComparison.Ordinal);
        Assert.Contains("$($frameRule.Text); repository", Smoke, StringComparison.Ordinal); // the first line of the run
        Assert.Contains("$SoloFrames = [switch]$frameRule.Solo", Smoke, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------ C8: the bounded secret scan, the parallel batch

    [Fact]
    public void The_scan_is_full_without_a_stamp_when_forced_when_old_and_when_the_secrets_changed()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lifestone-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var secrets = Path.Combine(folder, "secrets.json");
            var stamp = Path.Combine(folder, "stamp.json");
            File.WriteAllText(secrets, "{ \"accounts\": { \"test\": { \"password\": \"SENTINEL-not-a-real-password\" } } }");
            var script = Function(Smoke, "Get-ScanPlan") + "\n" + Function(Smoke, "Write-ScanStamp") + "\n";
            string Plan(bool force, DateTime now) => RunPowerShell(script + $"$p = Get-ScanPlan -StampPath '{stamp}' -SecretsPath '{secrets}' -Force ${force} -Now ([datetime]'{now:o}'); Write-Output (\"FULL=\" + $p.Full + \" REASON=\" + $p.Reason)");

            Assert.Contains("FULL=True REASON=full scan (no stamp)", Plan(false, DateTime.UtcNow), StringComparison.Ordinal);

            RunPowerShell(script + $"Write-ScanStamp -StampPath '{stamp}' -SecretsPath '{secrets}' -Started ([datetime]::UtcNow)");
            var stampText = File.ReadAllText(stamp);
            Assert.DoesNotContain("SENTINEL", stampText, StringComparison.Ordinal); // timestamps and file facts only
            Assert.Contains("scanStartedTicks", stampText, StringComparison.Ordinal);

            Assert.Contains("FULL=False REASON=incremental since", Plan(false, DateTime.UtcNow), StringComparison.Ordinal);
            Assert.Contains("FULL=True REASON=full scan (-FullScan)", Plan(true, DateTime.UtcNow), StringComparison.Ordinal);
            Assert.Contains("FULL=True REASON=full scan (the last scan is over 7 days old)", Plan(false, DateTime.UtcNow.AddDays(8)), StringComparison.Ordinal);

            File.WriteAllText(secrets, "{ \"accounts\": { \"test\": { \"password\": \"another-sentinel\" } } }");
            Assert.Contains("FULL=True REASON=full scan (secrets.json changed since the last scan)", Plan(false, DateTime.UtcNow), StringComparison.Ordinal);

            File.WriteAllText(stamp, "not json");
            Assert.Contains("FULL=True REASON=full scan (the stamp could not be read)", Plan(false, DateTime.UtcNow), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_scan_reads_this_runs_folder_whole_and_bounds_only_the_folders_it_does_not_own()
    {
        var scan = Function(Smoke, "Measure-SecretLeaks");

        Assert.Contains("-not $plan.Full -and $file.LastWriteTimeUtc -lt $plan.Since", scan, StringComparison.Ordinal);
        Assert.Contains("& $addFiles $repoRoot @(Get-ChildItem -LiteralPath $OutDir -Recurse -File -Force -ErrorAction SilentlyContinue -ErrorVariable +listErrors)\n", scan.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(scan, @"\) \$true").Count); // godot logs, sessions, ACE logs
        Assert.Contains("Write-ScanStamp", scan, StringComparison.Ordinal);
        Assert.Contains("$result.Hits -eq 0 -and $result.Unreadable.Count -eq 0", scan, StringComparison.Ordinal); // only a clean scan moves the stamp
        Assert.Contains("[switch] $FullScan", Smoke, StringComparison.Ordinal);
    }

    [Fact]
    public void The_parallel_batch_starts_one_child_always_and_more_only_below_three_godot_runs()
    {
        var script = Function(Smoke, "Test-BatchSlot");
        string Slot(int requested, int running, int engines) => RunPowerShell(script + $"Write-Output (Test-BatchSlot {requested} {running} {engines})").Trim();

        Assert.Equal("True", Slot(2, 0, 5)); // the first child always starts, as a sequential run would
        Assert.Equal("True", Slot(2, 1, 2));
        Assert.Equal("False", Slot(2, 1, 3)); // three Godot processes on the machine already (other agents' too)
        Assert.Equal("False", Slot(2, 2, 2)); // the degree is reached
        Assert.Equal("True", Slot(3, 2, 2));
    }

    [Fact]
    public void The_parallel_steps_are_backend_free_never_frame_judged_and_smoke_steps()
    {
        var smoke = Smoke;
        var parallel = Quoted(Regex.Match(smoke, @"\$parallelSteps = @\(([^)]*)\)").Groups[1].Value);
        var allSteps = Quoted(Regex.Match(smoke, @"\$allSteps = @\(([^)]*)\)", RegexOptions.Singleline).Groups[1].Value);
        var backend = Quoted(Regex.Match(smoke, @"\$backendSteps = @\(([^)]*)\)").Groups[1].Value);
        var judged = Quoted(Regex.Match(smoke, @"\$frameJudgedSteps = @\(([^)]*)\)").Groups[1].Value);

        Assert.Equal(11, parallel.Count);
        Assert.All(parallel, step => Assert.Contains(step, allSteps));
        Assert.All(parallel, step => Assert.DoesNotContain(step, backend));
        Assert.All(parallel, step => Assert.DoesNotContain(step, judged));
        Assert.DoesNotContain("portal-space'", string.Join("'", parallel) + "'", StringComparison.Ordinal); // portal-space fails on a frame over 50 ms: it stays alone
        Assert.DoesNotContain("fly-path", parallel);
        Assert.Contains("[ValidateRange(1, 3)]\n    [int] $Parallel = 1,", Smoke.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void The_godot_script_keeps_the_default_user_data_and_takes_a_slot_for_the_parallel_runs()
    {
        var godot = File.ReadAllText(RepositoryFiles.PathOf("tools", "godot.ps1"));

        Assert.Contains("[string] $UserDataSlot = ''", godot, StringComparison.Ordinal);
        Assert.Contains("$dataRoot = if ($UserDataSlot) { Join-Path $repoRoot \"cache\\godot\\slot-$UserDataSlot\" } else { Join-Path $repoRoot 'cache\\godot' }", godot, StringComparison.Ordinal);
        Assert.Contains("[switch] $IsolateUserData", godot, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------ helpers

    private static string Failed(string check) => $"2026-10-19T12:00:00Z ERROR [playcheck] FAIL {check} detail=x";

    private static (string Contended, string Frame, string Other, string Errors, string Note) Verdict(string log, int others, bool solo)
    {
        var script = Function(Smoke, "Get-ErrorCount") + "\n" + Function(Smoke, "Get-PlayCheckFailures") + "\n" + Function(Smoke, "Get-FrameVerdict") + "\n";
        var checks = string.Join(",", Quoted(Smoke[Smoke.IndexOf("$frameTimeChecks = @(", StringComparison.Ordinal)..Smoke.IndexOf("# The steps -Parallel runs", StringComparison.Ordinal)]).Select(c => $"'{c}'"));
        var command = script + $"$log = @'\n{log}\n'@\n$v = Get-FrameVerdict -Text $log -Others {others} -Solo ${solo} -FrameChecks @({checks})\n"
            + "Write-Output (\"CONTENDED=\" + $v.Contended)\nWrite-Output (\"FRAME=\" + ($v.Frame -join ','))\nWrite-Output (\"OTHER=\" + ($v.Other -join ','))\nWrite-Output (\"ERRORS=\" + $v.Errors)\nWrite-Output (\"NOTE=\" + $v.Note)";
        var run = RunPowerShell(command);
        string Field(string name) => Regex.Match(run, "^" + name + "=(.*?)\\r?$", RegexOptions.Multiline).Groups[1].Value;
        return (Field("CONTENDED"), Field("FRAME"), Field("OTHER"), Field("ERRORS"), Field("NOTE"));
    }

    private static string LiveVerdict(string log, int others, bool solo)
    {
        var script = Function(Smoke, "Get-LiveFrameVerdict") + "\n";
        var run = RunPowerShell(script + $"$log = @'\n{log}\n'@\n$v = Get-LiveFrameVerdict -Text $log -Others {others} -Solo ${solo}\nWrite-Output (\"CONTENDED=\" + $v.Contended)");
        return Regex.Match(run, @"^CONTENDED=(True|False)", RegexOptions.Multiline).Groups[1].Value;
    }

    private static List<string> Quoted(string text) => Regex.Matches(text, "'([a-z0-9-]+)'").Select(m => m.Groups[1].Value).ToList();

    /// <summary>The function <paramref name="name"/>, cut out of the script by its header and the first line that is only a closing brace.</summary>
    private static string Function(string script, string name)
    {
        var start = script.IndexOf("function " + name, StringComparison.Ordinal);
        Assert.True(start >= 0, "tools script has no function " + name);
        var end = Regex.Match(script[start..], @"\r?\n\}\r?\n");
        Assert.True(end.Success, "no end of function " + name);
        return script[start..(start + end.Index + end.Length)];
    }

    private static string RunPowerShell(string command)
    {
        foreach (var shell in new[] { "pwsh", "powershell" })
        {
            var info = new ProcessStartInfo(shell) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add(command);
            try
            {
                using var process = Process.Start(info)!;
                var error = process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEnd();
                Assert.True(process.WaitForExit(60_000), "PowerShell did not finish");
                Assert.True(process.ExitCode == 0, "PowerShell failed: " + error.Result);
                return output;
            }
            catch (Win32Exception)
            {
                // that shell is not installed: try the next
            }
        }

        throw new InvalidOperationException("neither pwsh nor powershell is installed");
    }
}
