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

    private static string HeroVideo => File.ReadAllText(RepositoryFiles.PathOf("tools", "hero-video.ps1"));

    private const string DefaultFilter = "Category!=Loopback&Tier!=Soak&Tier!=Exhaustive&Tier!=Timing&Tier!=Tool&Status!=KnownFailure";

    // ------------------------------------------------------------------------------------------ C1: the default filter and the Exhaustive stage

    [Fact]
    public void Both_scripts_carry_the_same_default_filter_without_the_exhaustive_tier()
    {
        Assert.Contains("$testFilter = '" + DefaultFilter + "'", Smoke, StringComparison.Ordinal);
        Assert.Contains("$testFilter = '" + DefaultFilter + "'", SliceCheck, StringComparison.Ordinal);
        Assert.Contains("--filter \"" + DefaultFilter + "\"", SliceCheck, StringComparison.Ordinal); // the help text at the top
        Assert.Contains("--filter \"" + DefaultFilter + "\"", Smoke, StringComparison.Ordinal);

        // P1 fix (P-R6): the filter a developer or agent copies from the README, and the traits it leaves out, are the scripts' own.
        var readme = File.ReadAllText(RepositoryFiles.PathOf("README.md")).Replace("\r\n", "\n");
        Assert.Contains(DefaultFilter, readme, StringComparison.Ordinal);
        Assert.Contains("[Trait(\"Tier\",\"Timing\")]", readme, StringComparison.Ordinal);
        Assert.Contains("[Trait(\"Status\",\"KnownFailure\")]", readme, StringComparison.Ordinal);
        var shared = File.ReadAllText(RepositoryFiles.PathOf("tests", "Shared", "README.md")).Replace("\r\n", "\n");
        Assert.Contains(DefaultFilter, shared, StringComparison.Ordinal);
        Assert.Contains("`[Trait(\"Tier\", \"Timing\")]`", shared, StringComparison.Ordinal);
        Assert.Contains("the lead runs it alone", shared, StringComparison.Ordinal);
        Assert.Contains("TODO(...)", shared, StringComparison.Ordinal);
        Assert.Contains("`[Trait(\"Status\", \"KnownFailure\")]`", shared, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ground_draft_command_has_a_tier_of_its_own_that_both_default_filters_leave_out_and_the_timing_run_never_picks_up_M5_item_2_k1()
    {
        // The draft command (GroundDraftRunTests) writes files under the cache folder and is run by the lead on purpose: Tier=Tool. It was Tier=Timing, which the lead's quiet-machine timing run (--filter "Tier=Timing") picked up.
        Assert.Contains("&Tier!=Tool&", DefaultFilter, StringComparison.Ordinal);
        Assert.Contains("Tier=Tool (a lead-run command written as a test", SliceCheck, StringComparison.Ordinal); // slice-check's header text names the tier
        Assert.Contains("Tier!=Tool", Smoke, StringComparison.Ordinal);

        var draft = typeof(Lifestone.Client.Tests.Session.Grounds.GroundDraftRunTests);
        var method = Assert.Single(draft.GetMethods(), m => m.Name == "Draft_the_profile_of_the_named_dungeon_into_cache");
        static IEnumerable<(string Name, string Value)> Traits(System.Reflection.MemberInfo member) => member.GetCustomAttributesData()
            .Where(a => a.AttributeType.Name == "TraitAttribute" && a.ConstructorArguments.Count == 2)
            .Select(a => ((string)a.ConstructorArguments[0].Value!, (string)a.ConstructorArguments[1].Value!));
        var traits = Traits(method).Concat(Traits(draft)).ToList();
        Assert.Contains(("Tier", "Tool"), traits);
        Assert.DoesNotContain(traits, t => t.Name == "Tier" && t.Value != "Tool"); // not Timing (the timing run), not Exhaustive (slice-check -Exhaustive), not Soak
    }

    [Fact]
    public void Slice_check_has_an_exhaustive_stage_that_the_smoke_switch_implies()
    {
        var script = SliceCheck;

        Assert.Contains("$exhaustiveFilter = 'Tier=Exhaustive'", script, StringComparison.Ordinal);
        Assert.Contains("[switch] $Exhaustive", script, StringComparison.Ordinal);
        Assert.Matches(@"\$allStages = @\('build', 'test', 'exhaustive', 'steps', 'smoke'\)", script);
        Assert.Contains("if ($Exhaustive -or $Smoke) { \"exhaustive\" }", script, StringComparison.Ordinal);
        Assert.Contains("'exhaustive' { Invoke-TestRun 'exhaustive' $exhaustiveFilter $exhaustiveMinimum | Out-Null }", script, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------ D46: G-A before a live gate

    [Fact]
    public void Gate_is_the_build_and_both_test_tiers_with_no_godot_step_and_refuses_the_smoke_D46()
    {
        var script = SliceCheck;

        Assert.Contains("[switch] $Gate", script, StringComparison.Ordinal);
        Assert.Contains("$Stages = @('build', 'test', 'exhaustive')", script, StringComparison.Ordinal);
        Assert.Contains("if ($Gate -and $buildOk) { Write-OfflineAdvisory }", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-Smoke", Function(script, "Write-OfflineAdvisory"), StringComparison.Ordinal); // it names the steps, never runs them

        // The refusals are thrown before anything runs (no build, no dotnet, no Godot).
        var path = RepositoryFiles.PathOf("tools", "slice-check.ps1");
        foreach (var (extra, named) in new[] { ("-Smoke", "-Smoke"), ("-Steps play-checks", "-Steps"), ("-Changed", "-Changed"), ("-Stages build", "-Stages") })
        {
            var run = RunPowerShell($"try {{ & '{path}' -Gate {extra} }} catch {{ Write-Output ('REFUSED=' + $_.Exception.Message) }}");
            Assert.Contains("REFUSED=-Gate is G-A before a live gate", run, StringComparison.Ordinal);
            Assert.Contains("takes no " + named, run, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Gate_names_the_offline_steps_the_diff_selects_and_none_for_a_docs_and_tests_diff_D46()
    {
        var tools = RepositoryFiles.PathOf("tools");
        var functions = Function(SliceCheck, "Resolve-ChangedSteps") + "\n" + Function(SliceCheck, "Write-OfflineAdvisory").Replace("$PSScriptRoot", "'" + tools + "'", StringComparison.Ordinal) + "\n"
            + "function Write-Stage([string] $stage, [string] $outcome, [string] $detail, [double] $seconds) { Write-Output (\"LINE=$stage $outcome $detail\") }\n$Base = 'master'\n";

        string Advisory(params string[] files) => RunPowerShell(functions + "function Get-ChangedFiles { @(" + string.Join(",", files.Select(f => "'" + f + "'")) + ") }\nWrite-OfflineAdvisory");

        var session = Advisory("src/Lifestone.Client/Session/Scenarios/MageBuildScenario.cs", "docs/milestones/m4-plan.md");
        Assert.Contains("LINE=offline SKIP 3 step(s) the diff against master selects: play-checks,record-audio,shell-frames; G-A runs no Godot step", session, StringComparison.Ordinal);
        Assert.Contains("LINE=offline SKIP the diff against master selects no backend-free step", Advisory("docs/milestones/m4-plan.md", "tests/Lifestone.Client.Tests/Repository/SliceCheckAndSmokeTests.cs", "tools/slice-check.ps1", "server/mod/Lifestone.ServerMod/Core/CharPurgeRules.cs"), StringComparison.Ordinal);
        Assert.Contains("so every step", Advisory("tools/an-unmapped-tool.ps1"), StringComparison.Ordinal); // the fail-safe: a path no rule matches selects every step, and says so
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

    [Fact]
    public void A_first_runs_godot_errors_are_never_erased_by_a_clean_rerun_0_r6()
    {
        // M5 0 fix 0-R6: godot_errors is a fact (D19), not a flake. A failed step whose first row carries godot_errors above 0 is left out of the rerun
        // list and out of the rerun branch, so its FAIL and its count stand whatever a rerun would say; a failure with no Godot errors still reruns.
        var script = SliceCheck;
        var stage = script[script.IndexOf("function Invoke-StepsStage", StringComparison.Ordinal)..];
        stage = stage[..stage.IndexOf("function Invoke-SmokeStage", StringComparison.Ordinal)];
        Assert.Contains("-not (Test-GodotErrors $rows[$_])", stage, StringComparison.Ordinal);
        Assert.Contains("if ($outcome -eq 'FAIL' -and $Rerun -and -not (Test-GodotErrors $row))", stage, StringComparison.Ordinal);

        var run = RunPowerShell(Function(script, "Test-GodotErrors") + """

            Write-Output ('A=' + (Test-GodotErrors @{ Result = 'FAIL'; GodotErrors = '3' }))
            Write-Output ('B=' + (Test-GodotErrors @{ Result = 'FAIL'; GodotErrors = '0' }))
            Write-Output ('C=' + (Test-GodotErrors @{ Result = 'FAIL'; GodotErrors = 'n/a' }))
            Write-Output ('D=' + (Test-GodotErrors @{ Result = 'FAIL' }))
            Write-Output ('E=' + (Test-GodotErrors $null))
            """);

        Assert.Contains("A=True", run, StringComparison.Ordinal);
        Assert.Contains("B=False", run, StringComparison.Ordinal);
        Assert.Contains("C=False", run, StringComparison.Ordinal);
        Assert.Contains("D=False", run, StringComparison.Ordinal);
        Assert.Contains("E=False", run, StringComparison.Ordinal);
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
        Assert.DoesNotContain("login-hunt", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_journey_steps_are_judged_backend_steps_that_skip_unless_the_backend_is_required_and_the_retired_live_steps_are_gone()
    {
        // M4 P5b G1: journey-mage / journey-melee / journey-archer replace login-buff, login-bsd, login-death, login-hunt and login-hunt-missile.
        var script = Smoke;
        var steps = Regex.Match(script, @"\$allSteps = @\(([^)]*)\)", RegexOptions.Singleline).Groups[1].Value;
        var backend = Regex.Match(script, @"\$backendSteps = @\(([^)]*)\)").Groups[1].Value;
        var judged = Regex.Match(script, @"\$frameJudgedSteps = @\(([^)]*)\)").Groups[1].Value;
        var audio = Regex.Match(script, @"\$audioSteps = @\(([^)]*)\)").Groups[1].Value;

        foreach (var list in new[] { steps, backend, audio })
        {
            Assert.Contains("'login-throwaway', 'journey-mage', 'journey-melee', 'journey-archer'", list, StringComparison.Ordinal);
        }

        Assert.Contains("'journey-mage', 'journey-melee', 'journey-archer'", judged, StringComparison.Ordinal);
        foreach (var retired in new[] { "login-buff", "login-bsd", "login-death", "login-hunt", "login-hunt-missile" })
        {
            foreach (var list in new[] { steps, backend, judged, audio })
            {
                Assert.DoesNotContain($"'{retired}'", list, StringComparison.Ordinal);
            }

            Assert.DoesNotContain($"if ($step -eq '{retired}'", script, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("$huntSteps", script, StringComparison.Ordinal);

        // The skip under -Backend auto (and start) comes before the backend gate and records a SKIP.
        var skip = script.IndexOf("if ($journeySteps.ContainsKey($step) -and $Backend -ne 'require')", StringComparison.Ordinal);
        var gate = script.IndexOf("if ($backendSteps -contains $step -and -not (Test-BackendGate", StringComparison.Ordinal);
        Assert.True(skip > 0 && skip < gate, "the journey skip rule must come before the backend gate");
        Assert.Contains("-Skipped", script[skip..gate], StringComparison.Ordinal);
        Assert.Contains("continue", script[skip..gate], StringComparison.Ordinal);

        // The table: the three journeys and a Godot timeout each (its relation to the runner's own limit: The_smoke_journeys_godot_timeouts_are_above_the_runners_own_limits, M5 2 fix F2, 2-R5).
        Assert.Equal(["archer", "mage", "melee"], SmokeJourneySeconds(script).Keys.Order(StringComparer.Ordinal).ToArray());

        // The body: the account from the table, the journey option, the frames into <OutDir>/frames, the journey's own verdict, the frame fields report-only.
        var body = script[script.IndexOf("{ $journeySteps.ContainsKey($_) } {", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("'login' {", StringComparison.Ordinal)];
        Assert.Contains("--account=$($stepAccounts[$step]) --login-check=journey --journey=$($journey.Journey) --play-screenshot=$frameStem", body, StringComparison.Ordinal);
        Assert.Contains("$journey.Seconds '' $defaultSize", body, StringComparison.Ordinal);
        Assert.Contains("(Join-Path $OutDir 'frames')", body, StringComparison.Ordinal);
        Assert.Contains("Get-JourneyVerdict $run.Text $windowsContended", body, StringComparison.Ordinal); // M4 4.2 J2: the window frames are judged unless contended
        Assert.Contains("$windowsContended = ($run.Others -gt 0 -or $parallelPhase) -and -not $SoloFrames", body, StringComparison.Ordinal);
        Assert.Contains("windows (judged over 75 ms unless contended)", body, StringComparison.Ordinal);
        Assert.Contains("Resolve-LiveFrames ($run.Code -eq 0 -and $judged.Ok) $run", body, StringComparison.Ordinal); // -ContendedFrames reports the frame fields as login-hunt-missile did
        Assert.Contains("-FrameOver50 $over50 -FrameJudge $judge", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// M5 2 fix F2 (review 2-R5): the relation, not a number. k2 raised the archer's whole-run limit by its 1,200 s hunt (<see cref="Lifestone.Client.Session.Scenarios.Journey.JourneyLimits.HuntExtraSeconds"/>) to 2,780 s while smoke
    /// still killed Godot at 2,000 s, so a hunt that found nothing ended as a smoke timeout instead of the hunt's own named reason. Each journey's Godot timeout in smoke.ps1 is at least the runner's whole limit
    /// (<c>JourneyLegRunner.WholeSecondsFor</c>, read from the host's source so its login, margin and factor are not copied here; the plan's seconds and the hunt's extra come from the plans and <c>JourneyLimits</c>) plus
    /// <see cref="LeastMarginSeconds"/>, and live-gate.ps1's copy of the table (which sizes the gate's lane budget) is the same table.
    /// </summary>
    [Fact]
    public void The_smoke_journeys_godot_timeouts_are_above_the_runners_own_limits()
    {
        var smoke = SmokeJourneySeconds(Smoke);
        var gateTable = Regex.Match(File.ReadAllText(RepositoryFiles.PathOf("tools", "live-gate.ps1")), @"\$script:SmokeJourneyTimeouts = @\{ mage = (?<mage>\d+); melee = (?<melee>\d+); archer = (?<archer>\d+) \}");
        Assert.True(gateTable.Success, "live-gate.ps1 has no SmokeJourneyTimeouts table for the three journeys");

        var plans = new (string Name, Lifestone.Client.Session.Scenarios.Journey.JourneyPlan Plan)[]
        {
            ("mage", Lifestone.Client.Session.Scenarios.Journey.JourneyPlans.Mage),
            ("melee", Lifestone.Client.Session.Scenarios.Journey.JourneyPlans.Melee),
            ("archer", Lifestone.Client.Session.Scenarios.Journey.JourneyPlans.Archer),
        };
        foreach (var (name, plan) in plans)
        {
            var runner = RunnerWholeSeconds(plan);
            Assert.True(smoke.ContainsKey(name), "smoke.ps1 has no journey step for " + name);
            Assert.True(smoke[name] >= runner + LeastMarginSeconds, $"smoke gives the {name} journey {smoke[name]} s but the runner's own limit is {runner} s (+ {LeastMarginSeconds} s of margin): smoke would kill Godot before the runner's verdict");
            Assert.Equal(smoke[name], double.Parse(gateTable.Groups[name].Value, System.Globalization.CultureInfo.InvariantCulture)); // the gate's lane budget is sized from the same numbers
        }
    }

    /// <summary>The least the other two journeys keep over the runner's limit when k2 left them (the melee's 55 s): Godot's start before the runner's clock and the verdict's flush after its limit.</summary>
    private const double LeastMarginSeconds = 50;

    /// <summary>smoke.ps1's journey table: the journey name and the Godot timeout of each.</summary>
    private static Dictionary<string, double> SmokeJourneySeconds(string script) =>
        Regex.Matches(script, @"'journey-\w+'\s*=\s*@\{ Journey = '(?<j>\w+)'; Seconds = (?<s>\d+) \}")
            .ToDictionary(m => m.Groups["j"].Value, m => double.Parse(m.Groups["s"].Value, System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The host's whole-run limit for <paramref name="plan"/> with the default fight options: <c>JourneyLegRunner.WholeSecondsFor</c>, its constants and factor read from the source (the Godot project is not referenced here).</summary>
    private static double RunnerWholeSeconds(Lifestone.Client.Session.Scenarios.Journey.JourneyPlan plan)
    {
        var source = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "JourneyLegRunner.cs"));
        double Constant(string name)
        {
            var match = Regex.Match(source, @"const double " + name + @" = (?<v>\d+(\.\d+)?);");
            Assert.True(match.Success, "JourneyLegRunner has no const double " + name);
            return double.Parse(match.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        var formula = Regex.Match(source, @"WholeSecondsFor\(JourneyPlan plan, FightOptions\? fight = null\) => LoginSeconds \+ \((?<k>\d+(\.\d+)?) \* plan\.ExpectedSeconds\) \+ MarginSeconds \+ JourneyLimits\.WholeExtraSeconds\(plan, fight \?\? new FightOptions\(\)\);");
        Assert.True(formula.Success, "JourneyLegRunner.WholeSecondsFor changed shape: update this test's reading of it, then the smoke table");
        return Constant("LoginSeconds") + (double.Parse(formula.Groups["k"].Value, System.Globalization.CultureInfo.InvariantCulture) * plan.ExpectedSeconds) + Constant("MarginSeconds")
            + Lifestone.Client.Session.Scenarios.Journey.JourneyLimits.WholeExtraSeconds(plan, new Lifestone.Client.Session.Scenarios.FightOptions());
    }

    [Fact]
    public void The_journey_verdict_reads_the_done_line_every_station_line_and_the_teleport_budget()
    {
        var script = Smoke;
        string[] lines0 =
        [
            "[logincheck] journey station=buff ok=true s=60.0",
            "[logincheck] journey station=den-fight ok=true s=60.0 kills=9 mana_wait_max_s=1.5 storeys_visited=2 basement_entries=0",
            "[logincheck] journey station=vendor ok=false s=0.0 skipped=precondition:no_vendor",
            MageLogistics,
            MageDeath,
            "[logincheck] journey frames station=buff frame_max_ms=31.5 frames=900",
            "[logincheck] journey done ok=true name=mage stations=5/6 failed=none teleports=1 recalls=2 budget=3 route_m=900.0 run_s=1000.0 recovery_places=0 granted=0 places=0",
        ];
        var run = RunPowerShell(Function(script, "Get-JourneyVerdict")
            + "$ok = @(" + string.Join(",", lines0.Select(l => "'" + l + "'")) + ") -join \"`n\"\n"
            + "$a = Get-JourneyVerdict $ok\nWrite-Output ('A=' + $a.Ok + ' ' + $a.Frames + ' ' + $a.FrameMax)\n"
            + "$b = Get-JourneyVerdict ($ok -replace 'teleports=1', 'teleports=4')\nWrite-Output ('B=' + $b.Ok)\n"
            + "$c = Get-JourneyVerdict ($ok -replace 'station=buff ok=true', 'station=buff ok=false' -replace 'failed=none', 'failed=buff')\nWrite-Output ('C=' + $c.Ok)\n"
            + "$d = Get-JourneyVerdict 'nothing'\nWrite-Output ('D=' + $d.Ok + ' ' + $d.Detail)\n"
            + "$e = Get-JourneyVerdict ($ok -replace 'done ok=true', 'done ok=false')\nWrite-Output ('E=' + $e.Ok)\n");

        var lines = run.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 1 && l[1] == '=').ToArray();
        Assert.Equal(["A=True buff:31.5 31.5", "B=False", "C=False", "D=False no journey done line", "E=False"], lines);
    }

    // M5 item 4 c1 (D55): the mage's done line carries places=0 (judged on the mage only); a helper line without it gets it, so the older cases keep testing the field they name.
    private static string JourneyDone(string name, string fields, int budget = 1) =>
        $"[logincheck] journey done ok=true name={name} stations=9/9 failed=none budget={budget} route_m=500.0 run_s=900.0 rungs=1 blocked=0 sightless=2 vitals_passes=3 {fields}"
        + (name == "mage" && !(" " + fields).Contains(" places=", StringComparison.Ordinal) ? " places=0" : string.Empty);

    // The mage's logistics and death station lines as the gate wants them (DoD 6: bought>=1 spent_xp>0, sold report-only; DoD 7: deaths=1 corpse_recovered=yes vitae_grants=0 rebuffed=yes; D55: granted=0 places=0).
    private const string MageLogistics = "[logincheck] journey station=logistics ok=true s=141.0 trigger=forced granted=0 places=0 loot=off sold=0 bought=3 spent=1200 notes_sold=0 spent_xp=5000 raises=2 vitals_passes=1";

    private const string MageDeath = "[logincheck] journey station=death ok=true s=118.0 bars=ok deaths=1 corpse_recovered=yes route=portal items_back=9 vitae_grants=0 rebuffed=yes rebuffed_pretrip=no relines=3 trip_abort=none";

    // The mage's den-fight station line (M5 item 2 k1: mana_wait_max_s<=10 is judged on it; M5 item 3 c2b: storeys_visited>=2 and basement_entries=0 too, the rest of the tour and the loot fields ride on it) in front of its done line.
    private const string Tour = "storeys_visited=2 basement_entries=0 returns=1 slots_standing=33 legs_recovered=2 kills_unpaired=1 slots_expired=3";

    private const string LootOff = "loot=off looted=0 kept=0 loot_value=0";

    /// <summary>What the report-only den-fight bracket shows for <see cref="Tour"/> and <see cref="LootOff"/>: the two judged counts have left it.</summary>
    private const string ShownTour = "returns=1 slots_standing=33 legs_recovered=2 kills_unpaired=1 slots_expired=3 loot=off looted=0 kept=0 loot_value=0";

    private const string AllTourNa = "returns=n/a slots_standing=n/a legs_recovered=n/a kills_unpaired=n/a slots_expired=n/a loot=n/a looted=n/a kept=n/a loot_value=n/a";

    private const string LogisticsNa = "trigger=n/a granted=n/a places=n/a loot=n/a sold=n/a bought=n/a spent=n/a notes_sold=n/a spent_xp=n/a raises=n/a";

    private const string DeathNa = "deaths=n/a corpse_recovered=n/a route=n/a items_back=n/a vitae_grants=n/a rebuffed=n/a rebuffed_pretrip=n/a relines=n/a trip_abort=n/a";

    // A mage's done line comes with the mage's logistics and death stations (the judged DoD 6 and 7 lines) unless the case asks for none (mageStations: false); the other journeys never get them.
    private static string WithFight(string done, string fields = "mana_wait_max_s=1.5 mana_wait_s=4.0 " + Tour + " " + LootOff, bool mageStations = true) =>
        "[logincheck] journey station=den-fight ok=true s=49.4 kills=9 target_kills=9 " + fields + "\n"
        + (mageStations && done.Contains(" name=mage ", StringComparison.Ordinal) ? MageLogistics + "\n" + MageDeath + "\n" : string.Empty) + done;

    // Runs Get-JourneyVerdict over each (name, text, contended) and prints "<name>=<Ok>|<WindowOver50>|<WindowsSeen>|<Windows>|<Detail>|<TourFields>|<LogisticsFields>|<DeathFields>|<Places>|<PhysicsFields>|<DoorFields>|<RecallFields>".
    private static Dictionary<string, string> JudgeJourneys(params (string Name, string Text, bool Contended)[] cases)
    {
        // In batches of six: one command line has a limit (about 32,000 characters on Windows), and the mage's cases carry three station lines each.
        var result = new Dictionary<string, string>();
        foreach (var batch in cases.Chunk(6))
        {
            var script = Function(Smoke, "Get-JourneyVerdict");
            foreach (var (name, text, contended) in batch)
            {
                var literal = "@(" + string.Join(",", text.Split('\n').Select(l => "'" + l + "'")) + ") -join \"`n\"";
                script += $"$v = Get-JourneyVerdict ({literal}) ${(contended ? "true" : "false")}\n"
                    + $"Write-Output ('{name}=' + $v.Ok + '|' + $v.WindowOver50 + '|' + $v.WindowsSeen + '|' + $v.Windows + '|' + $v.Detail + '|' + $v.TourFields + '|' + $v.LogisticsFields + '|' + $v.DeathFields + '|' + $v.Places + '|' + $v.PhysicsFields + '|' + $v.DoorFields + '|' + $v.RecallFields)\n";
            }

            var output = RunPowerShell(script);
            foreach (var line in output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Contains('=', StringComparison.Ordinal) && l.Contains('|', StringComparison.Ordinal)))
            {
                result[line[..line.IndexOf('=', StringComparison.Ordinal)]] = line[(line.IndexOf('=', StringComparison.Ordinal) + 1)..];
            }
        }

        return result;
    }

    [Fact]
    public void The_journey_verdict_judges_the_done_line_fields_the_owner_promoted_M4_4_2_J2()
    {
        // teleports<=1 on every journey, recalls>=2 on the mage, recovery_places=0 on every journey, granted=0 on the mage and the archer; the melee's granted and the other fields stay report-only.
        var got = JudgeJourneys(
            ("magepass", WithFight(JourneyDone("mage", "teleports=1 recalls=3 recovery_places=0 granted=0")), false),
            ("magerecalls", WithFight(JourneyDone("mage", "teleports=1 recalls=1 recovery_places=0 granted=0")), false),
            ("magerecallsgone", WithFight(JourneyDone("mage", "teleports=1 recovery_places=0 granted=0")), false),
            ("mageteleports", WithFight(JourneyDone("mage", "teleports=2 recalls=3 recovery_places=0 granted=0", budget: 2)), false),
            ("mageplaces", WithFight(JourneyDone("mage", "teleports=1 recalls=3 recovery_places=1 granted=0")), false),
            ("magegranted", WithFight(JourneyDone("mage", "teleports=1 recalls=3 recovery_places=0 granted=2")), false),
            ("archerpass", JourneyDone("archer", "teleports=1 recalls=1 recovery_places=0 granted=0"), false),
            ("archergranted", JourneyDone("archer", "teleports=1 recalls=1 recovery_places=0 granted=1"), false),
            ("archernorecalls", JourneyDone("archer", "teleports=0 recalls=0 recovery_places=0 granted=0"), false),
            ("meleegranted", JourneyDone("melee", "teleports=1 recalls=1 recovery_places=0 granted=1"), false),
            ("meleeplaces", JourneyDone("melee", "teleports=1 recalls=1 recovery_places=2 granted=1"), false),
            ("meleeteleports", JourneyDone("melee", "teleports=2 recalls=1 recovery_places=0 granted=1", budget: 3), false),
            ("overbudget", JourneyDone("archer", "teleports=3 recalls=1 recovery_places=0 granted=0", budget: 2), false));

        Assert.StartsWith("True|", got["magepass"], StringComparison.Ordinal);
        Assert.StartsWith("False|", got["magerecalls"], StringComparison.Ordinal);
        Assert.Contains("; recalls 1 under 2 (mage)", got["magerecalls"], StringComparison.Ordinal);
        Assert.Contains("; recalls n/a under 2 (mage)", got["magerecallsgone"], StringComparison.Ordinal);
        Assert.StartsWith("False|", got["mageteleports"], StringComparison.Ordinal);
        Assert.Contains("; teleports 2 over 1", got["mageteleports"], StringComparison.Ordinal);
        Assert.Contains("; recovery_places 1 not 0", got["mageplaces"], StringComparison.Ordinal);
        Assert.Contains("; granted 2 not 0 (mage)", got["magegranted"], StringComparison.Ordinal);
        Assert.StartsWith("True|", got["archerpass"], StringComparison.Ordinal);
        Assert.Contains("; granted 1 not 0 (archer)", got["archergranted"], StringComparison.Ordinal);
        Assert.StartsWith("True|", got["archernorecalls"], StringComparison.Ordinal); // recalls is judged on the mage only
        Assert.StartsWith("True|", got["meleegranted"], StringComparison.Ordinal); // the melee's granted stays report-only (vendor-facts grants a sale item by design)
        Assert.Contains("; recovery_places 2 not 0", got["meleeplaces"], StringComparison.Ordinal);
        Assert.Contains("; teleports 2 over 1", got["meleeteleports"], StringComparison.Ordinal);
        Assert.Contains("; teleports 3 over budget 2", got["overbudget"], StringComparison.Ordinal);
        Assert.DoesNotContain("; teleports 3 over 1", got["overbudget"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_journey_verdict_judges_the_mage_dens_mana_wait_at_10_seconds_from_M5_item_2_k1()
    {
        // m5-plan.md section 5 item 3: mana_wait_max_s<=10 judged from item 2's gate, on the mage journey's den-fight station line. Over 10, the field missing or n/a, and no den-fight line at all fail it by name.
        var done = JourneyDone("mage", "teleports=1 recalls=3 recovery_places=0 granted=0");
        var got = JudgeJourneys(
            ("pass", WithFight(done, "mana_wait_max_s=3.4 " + Tour), false),
            ("zero", WithFight(done, "mana_wait_max_s=0 " + Tour), false),
            ("exactly10", WithFight(done, "mana_wait_max_s=10 " + Tour), false),
            ("exactly10point0", WithFight(done, "mana_wait_max_s=10.0 " + Tour), false),
            ("over", WithFight(done, "mana_wait_max_s=10.1 " + Tour), false),
            ("farover", WithFight(done, "mana_wait_max_s=87.5 mana_wait_s=140.2 " + Tour), false),
            ("overcontended", WithFight(done, "mana_wait_max_s=10.1 " + Tour), true),
            ("missing", WithFight(done, "mana_wait_s=4.0 " + Tour), false),
            ("na", WithFight(done, "mana_wait_max_s=n/a " + Tour), false),
            ("stationless", done, false),
            ("skipped", "[logincheck] journey station=den-fight ok=false s=0.0 skipped=precondition:no_target\n" + done, false),
            ("lookalike", WithFight(done, "x_mana_wait_max_s=1.0 " + Tour), false));

        const string Named = "; mana_wait_max_s n/a on the den-fight station, not 10 or less (mage)";
        Assert.StartsWith("True|", got["pass"], StringComparison.Ordinal);
        Assert.DoesNotContain("; mana_wait_max_s", got["pass"], StringComparison.Ordinal); // no reason in the detail of a pass
        Assert.StartsWith("True|", got["zero"], StringComparison.Ordinal);
        Assert.StartsWith("True|", got["exactly10"], StringComparison.Ordinal); // the rule is "10 or less"
        Assert.StartsWith("True|", got["exactly10point0"], StringComparison.Ordinal);
        Assert.StartsWith("False|", got["over"], StringComparison.Ordinal);
        Assert.Contains("; mana_wait_max_s 10.1 over 10 (mage den-fight)", got["over"], StringComparison.Ordinal);
        Assert.Contains("; mana_wait_max_s 87.5 over 10 (mage den-fight)", got["farover"], StringComparison.Ordinal);
        Assert.StartsWith("False|", got["overcontended"], StringComparison.Ordinal); // a mana wait is not a frame time: contention does not excuse it
        Assert.Contains(Named, got["missing"], StringComparison.Ordinal);
        Assert.Contains(Named, got["na"], StringComparison.Ordinal);
        Assert.Contains(Named, got["stationless"], StringComparison.Ordinal);
        Assert.Contains(Named, got["skipped"], StringComparison.Ordinal);
        Assert.Contains(Named, got["lookalike"], StringComparison.Ordinal); // only the field itself counts, not a longer name ending in it
        Assert.All(new[] { "missing", "na", "stationless", "skipped", "lookalike" }, k => Assert.StartsWith("False|", got[k], StringComparison.Ordinal));
    }

    [Fact]
    public void The_journey_verdict_judges_the_mana_wait_on_the_mage_only_and_keeps_the_tour_fields_report_only_M5_item_2_k1()
    {
        var mage = JourneyDone("mage", "teleports=1 recalls=3 recovery_places=0 granted=0");
        var melee = JourneyDone("melee", "teleports=1 recalls=1 recovery_places=0 granted=1");
        var archer = JourneyDone("archer", "teleports=1 recalls=1 recovery_places=0 granted=0");
        var got = JudgeJourneys(
            // the melee's and the archer's den-fight (or none) are not judged on the mana wait
            ("meleeslow", WithFight(melee, "mana_wait_max_s=55.0 " + Tour), false),
            ("meleebare", WithFight(melee, "kills=3"), false),
            ("archernone", archer, false),
            // the tour's fields that stay report-only (c2b: storeys_visited and basement_entries left them for the judge): any value passes and is shown
            ("tourbad", WithFight(mage, "mana_wait_max_s=2.0 storeys_visited=2 basement_entries=0 returns=0 slots_standing=0 legs_recovered=0 kills_unpaired=0 slots_expired=0 loot=on looted=0 kept=0 loot_value=0"), false),
            ("tourna", WithFight(mage, "mana_wait_max_s=2.0 storeys_visited=2 basement_entries=0 returns=n/a slots_standing=n/a legs_recovered=n/a kills_unpaired=n/a slots_expired=n/a"), false),
            ("tourabsent", WithFight(mage, "mana_wait_max_s=2.0 storeys_visited=2 basement_entries=0"), false),
            ("tourshown", WithFight(mage), false),
            ("tourmelee", WithFight(melee, "mana_wait_max_s=55.0 " + Tour + " " + LootOff), false),
            ("tourfailed", WithFight(mage, "mana_wait_max_s=40.0 " + Tour + " " + LootOff), false),
            ("tournone", melee, false));

        Assert.StartsWith("True|", got["meleeslow"], StringComparison.Ordinal);
        Assert.StartsWith("True|", got["meleebare"], StringComparison.Ordinal);
        Assert.StartsWith("True|", got["archernone"], StringComparison.Ordinal);
        Assert.StartsWith("True|", got["tourbad"], StringComparison.Ordinal); // no returns, no slots standing and no legs recovered fail nothing in this gate
        Assert.Equal("returns=0 slots_standing=0 legs_recovered=0 kills_unpaired=0 slots_expired=0 loot=on looted=0 kept=0 loot_value=0", Field(got["tourbad"], 5));
        Assert.StartsWith("True|", got["tourna"], StringComparison.Ordinal);
        Assert.Equal("returns=n/a slots_standing=n/a legs_recovered=n/a kills_unpaired=n/a slots_expired=n/a loot=n/a looted=n/a kept=n/a loot_value=n/a", Field(got["tourna"], 5));
        Assert.Equal(AllTourNa, Field(got["tourabsent"], 5));
        Assert.Equal(ShownTour, Field(got["tourshown"], 5));
        Assert.Equal(ShownTour, Field(got["tourmelee"], 5)); // shown for any journey that runs the station
        Assert.StartsWith("False|", got["tourfailed"], StringComparison.Ordinal); // the mana wait fails it, the tour fields are still shown
        Assert.Equal(ShownTour, Field(got["tourfailed"], 5));
        Assert.Equal(AllTourNa, Field(got["tournone"], 5)); // no den-fight station: n/a, not a failure for the melee
    }

    [Theory]
    [InlineData("melee", " door_opened=yes", " door_opened=yes", true)]       // the stations in the other order do not matter
    [InlineData("melee", " door_opened=yes", " stale_door_opened=yes", false)] // the key is read whole, never as the tail of another field
    [InlineData("archer", " door_opened=no", " door_opened=n/a", true)]        // only the melee journey is judged
    public void The_melee_door_judge_reads_the_key_whole_in_either_station_order_and_judges_no_other_journey_M5_7_s4(string journey, string vendorDoor, string factsDoor, bool ok)
    {
        var done = JourneyDone(journey, "teleports=1 recalls=1 recovery_places=0 granted=" + (journey == "melee" ? "1" : "0"));
        var got = JudgeJourneys(("order", Facts(factsDoor) + Vendor(vendorDoor) + done, false));

        Assert.StartsWith(ok ? "True|" : "False|", got["order"], StringComparison.Ordinal);
        if (!ok)
        {
            Assert.Contains("door_opened n/a on the vendor-facts station, not yes (melee)", got["order"], StringComparison.Ordinal);
        }
    }

    private const string PhysicsShown ="creature_jumps=0 creature_flips=2 transit_ms_max=1.25 place_ms_max=0.40 transits_max=7";

    private const string PhysicsNa = "creature_jumps=n/a creature_flips=n/a transit_ms_max=n/a place_ms_max=n/a transits_max=n/a";

    private const string RecallLine = "[logincheck] journey station=recall-home ok=true s=6.0 recalls=1 recall_s=4.0 recall_fizzles=0 stepped_off=no\n";

    private static string Vendor(string door) => "[logincheck] journey station=vendor ok=true s=20.0 buy=yes problems=0 granted=0 vendor_frame_max_ms=20.5" + door + "\n";

    private static string Facts(string door) => "[logincheck] journey station=vendor-facts ok=true s=9.0 buy=no problems=0 granted=0 vendor_frame_max_ms=n/a" + door + "\n";

    [Fact]
    public void The_journey_verdict_shows_the_physics_and_stepped_off_fields_report_only_and_judges_door_opened_on_the_melee_stations_that_ran_M5_5_c1_M5_7_s4()
    {
        var mage = JourneyDone("mage", "teleports=1 recalls=3 recovery_places=0 granted=0");
        var melee = JourneyDone("melee", "teleports=1 recalls=1 recovery_places=0 granted=1");
        var got = JudgeJourneys(
            ("magephys", RecallLine + WithFight(mage, "mana_wait_max_s=2.0 " + Tour + " " + LootOff + " " + PhysicsShown), false),
            ("magephysloud", WithFight(mage, "mana_wait_max_s=2.0 " + Tour + " " + LootOff + " creature_jumps=4000 creature_flips=99 transit_ms_max=950.00 place_ms_max=800.00 transits_max=512"), false),
            ("magephysna", WithFight(mage, "mana_wait_max_s=2.0 " + Tour + " " + LootOff + " " + PhysicsNa), false),
            ("magephysabsent", WithFight(mage), false),
            ("meleedoors", Vendor(" door_opened=yes") + Facts(" door_opened=no") + melee, false),
            ("meleedoorsnot", Vendor(" door_opened=no") + Facts(" door_opened=yes") + melee, false),
            ("meleedoorsna", Vendor(" door_opened=n/a") + Facts(" door_opened=n/a") + melee, false),
            ("meleedoorsabsent", Vendor(string.Empty) + Facts(string.Empty) + melee, false),
            ("meleenostations", melee, false),
            ("meleedoorsyes", Vendor(" door_opened=yes") + Facts(" door_opened=yes") + melee, false),
            ("meleevendoronly", Vendor(" door_opened=yes") + melee, false),
            ("meleefactsskipped", Vendor(" door_opened=yes") + "[logincheck] journey station=vendor-facts ok=false skipped=no-vendor door_opened=n/a\n" + melee, false),
            ("mageignored", Vendor(" door_opened=no") + WithFight(mage), false));

        const string DoorsNa = "vendor:door_opened=n/a vendor-facts:door_opened=n/a";
        Assert.StartsWith("True|", got["magephys"], StringComparison.Ordinal);
        Assert.Equal(PhysicsShown, Field(got["magephys"], 9));
        Assert.Equal("stepped_off=no", Field(got["magephys"], 11));
        Assert.Equal(DoorsNa, Field(got["magephys"], 10)); // the mage runs no vendor
        Assert.StartsWith("True|", got["magephysloud"], StringComparison.Ordinal); // report-only: any value passes
        Assert.Equal("creature_jumps=4000 creature_flips=99 transit_ms_max=950.00 place_ms_max=800.00 transits_max=512", Field(got["magephysloud"], 9));
        Assert.Equal("stepped_off=n/a", Field(got["magephysloud"], 11));
        Assert.StartsWith("True|", got["magephysna"], StringComparison.Ordinal); // the headless host prints n/a
        Assert.Equal(PhysicsNa, Field(got["magephysna"], 9));
        Assert.StartsWith("True|", got["magephysabsent"], StringComparison.Ordinal); // a log from before c1
        Assert.Equal(PhysicsNa, Field(got["magephysabsent"], 9));
        // door_opened=yes is JUDGED from M5 item 7's gate, on the melee journey's vendor and vendor-facts stations that ran (m5-plan.md section 5 item 3); the fields are still shown beside the other report-only ones.
        Assert.StartsWith("False|", got["meleedoors"], StringComparison.Ordinal);
        Assert.Contains("door_opened no not yes (melee vendor-facts station)", got["meleedoors"], StringComparison.Ordinal);
        Assert.DoesNotContain("(melee vendor station)", got["meleedoors"], StringComparison.Ordinal);
        Assert.Equal("vendor:door_opened=yes vendor-facts:door_opened=no", Field(got["meleedoors"], 10));
        Assert.StartsWith("False|", got["meleedoorsnot"], StringComparison.Ordinal);
        Assert.Contains("door_opened no not yes (melee vendor station)", got["meleedoorsnot"], StringComparison.Ordinal);
        Assert.Equal("vendor:door_opened=no vendor-facts:door_opened=yes", Field(got["meleedoorsnot"], 10));
        Assert.StartsWith("False|", got["meleedoorsna"], StringComparison.Ordinal); // a station that ran and printed n/a is not yes
        Assert.Contains("door_opened n/a not yes (melee vendor station)", got["meleedoorsna"], StringComparison.Ordinal);
        Assert.Equal(DoorsNa, Field(got["meleedoorsna"], 10));
        Assert.StartsWith("False|", got["meleedoorsabsent"], StringComparison.Ordinal); // a station that ran without the field fails, named
        Assert.Contains("door_opened n/a on the vendor station, not yes (melee)", got["meleedoorsabsent"], StringComparison.Ordinal);
        Assert.Contains("door_opened n/a on the vendor-facts station, not yes (melee)", got["meleedoorsabsent"], StringComparison.Ordinal);
        Assert.Equal(DoorsNa, Field(got["meleedoorsabsent"], 10));
        Assert.StartsWith("True|", got["meleedoorsyes"], StringComparison.Ordinal);
        Assert.Equal("vendor:door_opened=yes vendor-facts:door_opened=yes", Field(got["meleedoorsyes"], 10));
        Assert.StartsWith("True|", got["meleevendoronly"], StringComparison.Ordinal); // a station that did not run (no line) is n/a, not a failure
        Assert.Equal("vendor:door_opened=yes vendor-facts:door_opened=n/a", Field(got["meleevendoronly"], 10));
        Assert.StartsWith("True|", got["meleefactsskipped"], StringComparison.Ordinal); // a station skipped by a precondition did not run
        Assert.StartsWith("True|", got["mageignored"], StringComparison.Ordinal); // only the melee journey is judged
        Assert.StartsWith("True|", got["meleenostations"], StringComparison.Ordinal);
        Assert.Equal(DoorsNa, Field(got["meleenostations"], 10));
        Assert.DoesNotContain("door_opened", got["meleenostations"].Split('|')[4], StringComparison.Ordinal);
        // the step's summary line carries them
        Assert.Contains("report-only den-fight physics: $($judged.PhysicsFields); report-only door: $($judged.DoorFields); report-only recall-home: $($judged.RecallFields)", Smoke, StringComparison.Ordinal);
    }

    /// <summary>The <paramref name="index"/>th '|'-separated part of a <see cref="JudgeJourneys"/> row (0 Ok, 1 WindowOver50, 2 WindowsSeen, 3 Windows, 4 Detail, 5 TourFields, 6 LogisticsFields, 7 DeathFields, 8 Places, 9 PhysicsFields, 10 DoorFields, 11 RecallFields).</summary>
    private static string Field(string row, int index) => row.Split('|')[index];

    [Fact]
    public void The_journey_verdict_judges_the_mage_dens_storeys_and_basement_entries_from_M5_item_3_c2b()
    {
        // m5-plan.md section 5 item 3 and D44: storeys_visited>=2 basement_entries=0 judged from item 3's gate, on the mage journey's den-fight station line beside mana_wait_max_s<=10.
        var mage = JourneyDone("mage", "teleports=1 recalls=3 recovery_places=0 granted=0");
        var melee = JourneyDone("melee", "teleports=1 recalls=1 recovery_places=0 granted=1");
        var archer = JourneyDone("archer", "teleports=1 recalls=1 recovery_places=0 granted=0");
        const string Wait = "mana_wait_max_s=2.0 ";
        const string Rest = " returns=1 slots_standing=33 legs_recovered=2 kills_unpaired=1 slots_expired=3";
        var got = JudgeJourneys(
            ("pass", WithFight(mage, Wait + "storeys_visited=2 basement_entries=0" + Rest), false),
            ("six", WithFight(mage, Wait + "storeys_visited=6 basement_entries=0" + Rest), false),
            ("one", WithFight(mage, Wait + "storeys_visited=1 basement_entries=0" + Rest), false),
            ("zero", WithFight(mage, Wait + "storeys_visited=0 basement_entries=0" + Rest), false),
            ("entered", WithFight(mage, Wait + "storeys_visited=3 basement_entries=2" + Rest), false),
            ("both", WithFight(mage, Wait + "storeys_visited=1 basement_entries=4" + Rest), false),
            ("contended", WithFight(mage, Wait + "storeys_visited=1 basement_entries=4" + Rest), true),
            ("storeysna", WithFight(mage, Wait + "storeys_visited=n/a basement_entries=0" + Rest), false),
            ("storeysmissing", WithFight(mage, Wait + "basement_entries=0" + Rest), false),
            ("basementna", WithFight(mage, Wait + "storeys_visited=2 basement_entries=n/a" + Rest), false),
            ("basementmissing", WithFight(mage, Wait + "storeys_visited=2" + Rest), false),
            ("lookalike", WithFight(mage, Wait + "x_storeys_visited=3 x_basement_entries=0" + Rest), false),
            ("stationless", mage, false),
            ("skipped", "[logincheck] journey station=den-fight ok=false s=0.0 skipped=precondition:no_target\n" + mage, false),
            ("twostations", "[logincheck] journey station=den-fight ok=true s=40.0 mana_wait_max_s=1.0 storeys_visited=2 basement_entries=0\n" + WithFight(mage, Wait + "storeys_visited=1 basement_entries=1" + Rest), false),
            ("meleeone", WithFight(melee, "mana_wait_max_s=2.0 storeys_visited=1 basement_entries=4" + Rest), false),
            ("archerone", WithFight(archer, "mana_wait_max_s=2.0 storeys_visited=1 basement_entries=4" + Rest), false));

        const string UnderTwo = "storeys_visited n/a on the den-fight station, not 2 or more (mage)";
        const string NoEntries = "basement_entries n/a on the den-fight station, not 0 (mage)";
        Assert.StartsWith("True|", got["pass"], StringComparison.Ordinal);
        Assert.DoesNotContain("; storeys_visited", got["pass"], StringComparison.Ordinal); // no reason in the detail of a pass
        Assert.DoesNotContain("; basement_entries", got["pass"], StringComparison.Ordinal);
        Assert.StartsWith("True|", got["six"], StringComparison.Ordinal); // the rule is "2 or more"
        Assert.StartsWith("False|", got["one"], StringComparison.Ordinal);
        Assert.Contains("; storeys_visited 1 under 2 (mage den-fight)", got["one"], StringComparison.Ordinal);
        Assert.DoesNotContain("; basement_entries", got["one"], StringComparison.Ordinal);
        Assert.Contains("; storeys_visited 0 under 2 (mage den-fight)", got["zero"], StringComparison.Ordinal);
        Assert.StartsWith("False|", got["entered"], StringComparison.Ordinal);
        Assert.Contains("; basement_entries 2 over 0 (mage den-fight)", got["entered"], StringComparison.Ordinal);
        Assert.DoesNotContain("; storeys_visited", got["entered"], StringComparison.Ordinal);
        Assert.Contains("; storeys_visited 1 under 2 (mage den-fight)", got["both"], StringComparison.Ordinal);
        Assert.Contains("; basement_entries 4 over 0 (mage den-fight)", got["both"], StringComparison.Ordinal);
        Assert.StartsWith("False|", got["contended"], StringComparison.Ordinal); // a count is not a frame time: contention does not excuse it
        Assert.Contains("; storeys_visited 1 under 2 (mage den-fight)", got["contended"], StringComparison.Ordinal);
        Assert.Contains(UnderTwo, got["storeysna"], StringComparison.Ordinal);
        Assert.DoesNotContain(NoEntries, got["storeysna"], StringComparison.Ordinal);
        Assert.Contains(UnderTwo, got["storeysmissing"], StringComparison.Ordinal);
        Assert.Contains(NoEntries, got["basementna"], StringComparison.Ordinal);
        Assert.DoesNotContain(UnderTwo, got["basementna"], StringComparison.Ordinal);
        Assert.Contains(NoEntries, got["basementmissing"], StringComparison.Ordinal);
        Assert.Contains(UnderTwo, got["lookalike"], StringComparison.Ordinal); // only the field itself counts, not a longer name ending in it
        Assert.Contains(NoEntries, got["lookalike"], StringComparison.Ordinal);
        Assert.Contains(UnderTwo, got["stationless"], StringComparison.Ordinal);
        Assert.Contains(NoEntries, got["stationless"], StringComparison.Ordinal);
        Assert.Contains(UnderTwo, got["skipped"], StringComparison.Ordinal);
        Assert.Contains("; storeys_visited 1 under 2 (mage den-fight)", got["twostations"], StringComparison.Ordinal); // the worst den-fight line counts
        Assert.Contains("; basement_entries 1 over 0 (mage den-fight)", got["twostations"], StringComparison.Ordinal);
        Assert.All(new[] { "one", "zero", "entered", "both", "contended", "storeysna", "storeysmissing", "basementna", "basementmissing", "lookalike", "stationless", "skipped", "twostations" }, k => Assert.StartsWith("False|", got[k], StringComparison.Ordinal));
        Assert.StartsWith("True|", got["meleeone"], StringComparison.Ordinal); // only the mage journey is judged
        Assert.StartsWith("True|", got["archerone"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_journey_verdict_carries_the_logistics_death_and_places_fields_report_only_with_n_a_when_absent_M5_item_3_c2b()
    {
        const string Logistics = MageLogistics + "\n";
        const string Death = MageDeath + "\n";
        var withPlaces = JourneyDone("mage", "teleports=1 recalls=3 recovery_places=0 granted=0 places=0");
        var got = JudgeJourneys(
            ("all", Logistics + Death + WithFight(withPlaces, mageStations: false), false),
            ("bare", WithFight(JourneyDone("mage", "teleports=1 recalls=3 recovery_places=0 granted=0").Replace(" places=0", string.Empty, StringComparison.Ordinal), mageStations: false), false),
            ("failing", Logistics + Death + WithFight(JourneyDone("mage", "teleports=1 recalls=3 recovery_places=0 granted=0 places=2"), "mana_wait_max_s=40.0", mageStations: false), false),
            ("lookalike", "[logincheck] journey station=logistics ok=true x_bought=7\n" + WithFight(JourneyDone("mage", "teleports=1 recalls=3 recovery_places=0 granted=0 xplaces=1").Replace(" places=0", string.Empty, StringComparison.Ordinal), mageStations: false), false),
            ("melee", Logistics + JourneyDone("melee", "teleports=1 recalls=1 recovery_places=0 granted=1 places=5"), false));

        const string LogisticsShown = "trigger=forced granted=0 places=0 loot=off sold=0 bought=3 spent=1200 notes_sold=0 spent_xp=5000 raises=2";
        const string DeathShown = "deaths=1 corpse_recovered=yes route=portal items_back=9 vitae_grants=0 rebuffed=yes rebuffed_pretrip=no relines=3 trip_abort=none";
        Assert.StartsWith("True|", got["all"], StringComparison.Ordinal);
        Assert.Equal(LogisticsShown, Field(got["all"], 6));
        Assert.Equal(DeathShown, Field(got["all"], 7));
        Assert.Equal("0", Field(got["all"], 8));
        Assert.StartsWith("False|", got["bare"], StringComparison.Ordinal); // M5 4 c1: the fields stay n/a in the report-only brackets, and the mage's judges now name them (the D55, DoD 6 and DoD 7 tests below)
        Assert.Equal(LogisticsNa, Field(got["bare"], 6));
        Assert.Equal(DeathNa, Field(got["bare"], 7));
        Assert.Equal("n/a", Field(got["bare"], 8));
        Assert.StartsWith("False|", got["failing"], StringComparison.Ordinal); // M5 4 c1: places=2 on the mage's done line is judged now (D55), besides the mana wait
        Assert.Equal("2", Field(got["failing"], 8));
        Assert.Contains("; places 2 not 0 (mage done line)", got["failing"], StringComparison.Ordinal);
        Assert.Equal(LogisticsNa, Field(got["lookalike"], 6)); // a longer name ending in a field's is not the field
        Assert.Equal("n/a", Field(got["lookalike"], 8));
        Assert.StartsWith("True|", got["melee"], StringComparison.Ordinal);
        Assert.Equal("5", Field(got["melee"], 8)); // every journey's done line
    }

    [Fact]
    public void The_journey_step_shows_the_den_fight_tour_fields_in_its_summary_detail_M5_item_2_k1()
    {
        var script = Smoke;
        var body = script[script.IndexOf("{ $journeySteps.ContainsKey($_) } {", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("'login' {", StringComparison.Ordinal)];
        Assert.Contains("report-only den-fight: $($judged.TourFields)", body, StringComparison.Ordinal);
        // M5 item 3 c2b: item 3's fields reach the summary report-only beside it, n/a when a line lacks them.
        Assert.Contains("report-only logistics: $($judged.LogisticsFields)", body, StringComparison.Ordinal);
        Assert.Contains("report-only death: $($judged.DeathFields)", body, StringComparison.Ordinal);
        Assert.Contains("report-only done: places=$($judged.Places)", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// M5 item 3 c2b: the mage's Godot step and the gate's copy of it cover the logistics station (about 150 s expected: Lifestone Recall, the walk to two vendors and the buys, the experience, Primary Portal Recall) that the plan gains,
    /// and the death station's longer order. smoke.ps1's table is a number, so the test follows the plan: the plan's whole-run limit (with the station: the plan's own once it has one, else plus 1.5 times its 150 s expected) and the 50 s
    /// margin must be under the step's seconds, and the gate's expected seconds for the mage's journey must have grown by the same 150 s over item 2's 600.
    /// </summary>
    [Fact]
    public void The_mage_journey_step_limit_and_expected_time_cover_the_logistics_station_M5_item_3_c2b()
    {
        var mage = Lifestone.Client.Session.Scenarios.Journey.JourneyPlans.Mage;
        var hasStation = mage.Stations.Any(s => s.Name == "logistics");
        var runner = RunnerWholeSeconds(mage) + (hasStation ? 0 : 1.5 * LogisticsExpectedSeconds);
        var smoke = SmokeJourneySeconds(Smoke)["mage"];
        Assert.True(smoke >= runner + LeastMarginSeconds, $"smoke gives the mage journey {smoke} s but the runner's limit with the logistics station is {runner} s (+ {LeastMarginSeconds} s of margin)");
        Assert.True(smoke >= 2333 + LeastMarginSeconds, "the mage's step is under item 2's runner limit (2,108 s) plus the logistics station's 225 s"); // 60 + 1.5 x (1165 + 150) + 300, a floor that does not read the plan
        var gate = File.ReadAllText(RepositoryFiles.PathOf("tools", "live-gate.ps1"));
        var expected = Regex.Match(gate, @"'journey-mage'\s*=\s*(?<s>\d+)\s");
        Assert.True(expected.Success, "live-gate.ps1 has no expected seconds for the mage's journey");
        Assert.True(double.Parse(expected.Groups["s"].Value, System.Globalization.CultureInfo.InvariantCulture) >= 600 + LogisticsExpectedSeconds, "the gate's expected seconds for the mage journey do not cover the logistics station");
    }

    /// <summary>The logistics station's expected seconds (the c2a plan row; the brief's "about 150 s").</summary>
    private const double LogisticsExpectedSeconds = 150;

    [Fact]
    public void The_journey_verdict_judges_the_window_frames_over_75_ms_and_flags_over_50_unless_contended_M4_4_2_J2()
    {
        var done = JourneyDone("melee", "teleports=1 recalls=1 recovery_places=0 granted=0");
        string Log(string hunt, string vendor, string facts) =>
            "[logincheck] journey station=hunt ok=true s=9.0 " + hunt + "\n[logincheck] journey station=vendor ok=true s=3.0 " + vendor + " door_opened=yes" // the melee's door_opened=yes is judged from M5 item 7's gate
            + "\n[logincheck] journey station=vendor-facts ok=true s=3.0 " + facts + " door_opened=yes\n" + done;
        var quiet = Log("attack_frame_max_ms=12.3 loot_frame_max_ms=9.0", "vendor_frame_max_ms=20.0", "vendor_frame_max_ms=21.5");
        var flagged = Log("attack_frame_max_ms=62.0 loot_frame_max_ms=9.0", "vendor_frame_max_ms=20.0", "vendor_frame_max_ms=21.5");
        var over = Log("attack_frame_max_ms=75.1 loot_frame_max_ms=9.0", "vendor_frame_max_ms=20.0", "vendor_frame_max_ms=21.5");
        var exactly75 = Log("attack_frame_max_ms=75.0 loot_frame_max_ms=9.0", "vendor_frame_max_ms=20.0", "vendor_frame_max_ms=21.5");
        var lootOver = Log("attack_frame_max_ms=12.0 loot_frame_max_ms=90.4", "vendor_frame_max_ms=20.0", "vendor_frame_max_ms=21.5");
        var factsOver = Log("attack_frame_max_ms=12.0 loot_frame_max_ms=9.0", "vendor_frame_max_ms=20.0", "vendor_frame_max_ms=88.8");
        var na = Log("attack_frame_max_ms=n/a loot_frame_max_ms=n/a", "vendor_frame_max_ms=n/a", "vendor_frame_max_ms=n/a");
        var absent = Log("kills=3", "sold=1", "sold=1"); // a log from before J1
        var got = JudgeJourneys(
            ("quiet", quiet, false), ("flagged", flagged, false), ("over", over, false), ("exactly75", exactly75, false), ("lootover", lootOver, false),
            ("factsover", factsOver, false), ("overcontended", over, true), ("flaggedcontended", flagged, true), ("na", na, false), ("absent", absent, false));

        Assert.StartsWith("True|False|True|hunt:attack_frame_max_ms=12.3 hunt:loot_frame_max_ms=9.0 vendor:vendor_frame_max_ms=20.0 vendor-facts:vendor_frame_max_ms=21.5|", got["quiet"], StringComparison.Ordinal);
        Assert.StartsWith("True|True|True|", got["flagged"], StringComparison.Ordinal); // over 50 flags, never fails
        Assert.StartsWith("False|True|True|", got["over"], StringComparison.Ordinal);
        Assert.Contains("; window frame over 75 ms: hunt attack_frame_max_ms=75.1", got["over"], StringComparison.Ordinal);
        Assert.StartsWith("True|True|", got["exactly75"], StringComparison.Ordinal); // the rule is "over 75"
        Assert.Contains("; window frame over 75 ms: hunt loot_frame_max_ms=90.4", got["lootover"], StringComparison.Ordinal);
        Assert.Contains("; window frame over 75 ms: vendor-facts vendor_frame_max_ms=88.8", got["factsover"], StringComparison.Ordinal);
        Assert.StartsWith("True|True|True|", got["overcontended"], StringComparison.Ordinal); // contended: reported, never judged
        Assert.Contains("; window frames=contended: hunt attack_frame_max_ms=75.1 over 75 ms (reported, not judged)", got["overcontended"], StringComparison.Ordinal);
        Assert.DoesNotContain("window frame over 75 ms", got["overcontended"], StringComparison.Ordinal);
        Assert.StartsWith("True|True|True|", got["flaggedcontended"], StringComparison.Ordinal);
        Assert.DoesNotContain("contended", got["flaggedcontended"], StringComparison.Ordinal);
        Assert.StartsWith("True|False|False|hunt:attack_frame_max_ms=n/a hunt:loot_frame_max_ms=n/a vendor:vendor_frame_max_ms=n/a vendor-facts:vendor_frame_max_ms=n/a|", got["na"], StringComparison.Ordinal); // the headless host
        Assert.StartsWith("True|False|False||", got["absent"], StringComparison.Ordinal); // reported as absent, fails nothing
    }

    [Fact]
    public void The_summary_shows_na_with_no_judge_suffix_for_a_leg_that_prints_no_flag()
    {
        // M4 1.2 s5 (1.1 review R14): login-check and login print no frame_over_50 flag, so their summary entry is "n/a", not "n/a (counted)".
        var script = Smoke;
        Assert.Contains("if ($_.FrameOver50 -eq 'n/a') { \"$($_.Step)=n/a\" } else { \"$($_.Step)=$($_.FrameOver50) ($($_.FrameJudge))\" }", script, StringComparison.Ordinal);
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
        Assert.DoesNotContain("login-hunt", map, StringComparison.Ordinal);
        Assert.Contains("'journey-mage' = @(", map, StringComparison.Ordinal); // slice-check's advisory line names the journeys when a Session folder changes
        Assert.Contains("'journey-melee' = @(", map, StringComparison.Ordinal);
        Assert.Contains("'journey-archer' = @(", map, StringComparison.Ordinal);
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
        // M5 6 v1: the play-checks judges moved into Get-PlayChecksSizeVerdict, which the row calls for each size (WindowSizesScriptTests).
        var sizeVerdict = Function(script, "Get-PlayChecksSizeVerdict");
        Assert.Contains("Get-PlayChecksSizeVerdict $run $defaultSize", play, StringComparison.Ordinal);
        Assert.Contains("all play checks hold", sizeVerdict, StringComparison.Ordinal);
        Assert.Contains("max_frame_ms_after_arrival", sizeVerdict, StringComparison.Ordinal);
        Assert.Contains("Test-RecordedSources $levels.sources", record, StringComparison.Ordinal);
        Assert.Contains("-and $sourcesOk", record, StringComparison.Ordinal);
        Assert.Contains("-and $fileOk", record, StringComparison.Ordinal);

        // The recording's ERRORs fail the record-audio row, the play-checks row does not count them.
        Assert.Contains("Get-RecordingErrorCount", play, StringComparison.Ordinal);
        Assert.Contains("Get-RecordingErrorCount", record, StringComparison.Ordinal);

        // The frames run is never joined with a judged run.
        Assert.DoesNotContain("--record-audio", script[script.IndexOf("'shell-frames' {", StringComparison.Ordinal)..script.IndexOf("'login-check' {", StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    [Fact]
    public void The_shell_frames_step_keeps_its_frames_and_judges_and_the_retired_skip_option_is_gone()
    {
        var script = Smoke;
        var frames = script[script.IndexOf("'shell-frames' {", StringComparison.Ordinal)..script.IndexOf("'login-check' {", StringComparison.Ordinal)];

        Assert.Contains("--play-checks --play-frames=$dir", frames, StringComparison.Ordinal);
        Assert.Contains("$expectedFrames = 11", frames, StringComparison.Ordinal);
        Assert.Contains("shell frames done size=", frames, StringComparison.Ordinal);
        Assert.Contains("$files -eq $expectedFrames", frames, StringComparison.Ordinal);

        // M4 4.1 fix2b: --play-skip-scenarios did nothing since t1 (the stand-in's fight and death checks are retired), so it is gone from the smoke, the catalog and the parser: an old command line is refused loudly.
        Assert.DoesNotContain("--play-skip-scenarios", script.Replace("--play-skip-scenarios is gone", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.NotEmpty(OptionCatalog.Registry.Problems(CommandLineArgument.ParseAll(["--play-checks", "--play-skip-scenarios", "--play-frames=cache/x"]), OptionScenes.Main));
        Assert.Empty(OptionCatalog.Registry.Problems(CommandLineArgument.ParseAll(["--play-checks", "--play-frames=cache/x"]), OptionScenes.Main));
        var checks = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "PlayChecks.cs"));
        Assert.DoesNotContain("FightChecksAsync", checks, StringComparison.Ordinal);
        Assert.DoesNotContain("DeathChecksAsync", checks, StringComparison.Ordinal);
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
    public void Solo_frames_waits_a_bounded_time_for_a_foreign_godot_then_refuses_by_name_m5_gate_foreign_godot()
    {
        var wait = Function(Smoke, "Wait-SoloEngine");

        Assert.DoesNotContain("-ge 600", wait, StringComparison.Ordinal); // no longer the 10-minute wait that outlived the gate's own step limit
        Assert.Contains("if (-not $SoloFrames) { return $true }", wait, StringComparison.Ordinal);
        Assert.Contains("Wait-GateForeignGodot -Seconds $ForeignGodotSeconds", wait, StringComparison.Ordinal);
        Assert.Contains("Get-ForeignGodot -OwnRoot $PID", wait, StringComparison.Ordinal);
        Assert.Contains("refused to start: $($wait.Detail) (-SoloFrames)", wait, StringComparison.Ordinal); // the detail starts with foreign-godot: (GateHost.psm1)
        Assert.Contains("[ValidateRange(0, 3600)]\n    [int] $ForeignGodotSeconds = 120,", Smoke, StringComparison.Ordinal);
        Assert.Contains("Import-Module (Join-Path $PSScriptRoot 'lib\\GateHost.psm1') -Force", Smoke, StringComparison.Ordinal);
        Assert.Contains("$SoloFrames -and $frameJudgedSteps -contains $step", Smoke, StringComparison.Ordinal);
        Assert.Contains("'-SoloFrames', '-FullScan'", SliceCheck, StringComparison.Ordinal);
        Assert.Contains("foreign-godot: another Godot run was up for $ForeignGodotSeconds s", Smoke, StringComparison.Ordinal); // the shared play-checks refusal names it too
    }

    [Fact]
    public void The_live_gate_implies_solo_frames_and_a_full_scan_and_the_first_line_names_the_frame_rule()
    {
        var script = Function(Smoke, "Get-FrameRule") + "\n";
        string Rule(string backend, bool solo, bool full) => RunPowerShell(script + $"$r = Get-FrameRule -Backend '{backend}' -Solo ${solo} -Full ${full}; Write-Output (\"SOLO=\" + $r.Solo + \" FULL=\" + $r.Full + \" \" + $r.Text)");

        var live = Rule("require", false, false);
        Assert.Contains("SOLO=True FULL=True", live, StringComparison.Ordinal);
        Assert.Contains("SOLO=False FULL=False", Rule("auto", false, false), StringComparison.Ordinal);
        Assert.Contains("SOLO=True FULL=False", Rule("auto", true, false), StringComparison.Ordinal);
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

    [Fact]
    public void The_hero_video_composer_trims_a_clip_shot_to_its_frames_at_the_lists_fps_and_size_M4_hero_video()
    {
        // The M4 hero video's shots are live screen captures (takes): a clip shot names its take and the second it starts at, and the composer encodes exactly the shot's
        // frames into the same near-lossless segment a recorded shot gets, so the edit (captions, crossfades, cards, the 720p and the poster) is unchanged.
        var script = Function(HeroVideo, "Get-ClipArguments") + "\n";
        var run = RunPowerShell(script + "(Get-ClipArguments -Source 'cache/take.mkv' -From 12.25 -Frames 105 -Fps 30 -Width 1920 -Height 1080 -Segment 'cache/seg.mkv') -join '|'");
        Assert.Equal("-ss|12.25|-i|cache/take.mkv|-frames:v|105|-an|-vf|fps=30,scale=1920:1080:flags=lanczos,format=rgb24|-c:v|libx264rgb|-preset|faster|-crf|8|cache/seg.mkv", run.Trim());
        var refused = RunPowerShell(script + "foreach ($from in -1, 3) { try { $null = Get-ClipArguments -Source a -From $from -Frames 0 -Fps 30 -Width 2 -Height 2 -Segment b; 'kept' } catch { 'refused' } }");
        Assert.Equal(["refused", "refused"], refused.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); // a negative start; no frames

        // A list with no end card (the owner: no text on screen) ends on the last shot's one-second fade to black.
        var fade = RunPowerShell("function Format-Number([double] $value) { return $value.ToString('0.###', [Globalization.CultureInfo]::InvariantCulture) }\n" + Function(HeroVideo, "Get-FilmFadeOut") + "\nGet-FilmFadeOut 6.2\nGet-FilmFadeOut 0.5");
        Assert.Equal(["fade=t=out:st=5.2:d=1", "fade=t=out:st=0:d=0.5"], fade.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Assert.Contains("if ($i -eq $shots.Count - 1 -and -not $endCard) { $filters += Get-FilmFadeOut $duration }", HeroVideo, StringComparison.Ordinal);
        Assert.Contains("if ($endCard) {", HeroVideo, StringComparison.Ordinal);

        // The record loop: a clip records nothing, and its source must lie under cache\ (takes are renders of the user's data).
        Assert.Contains("if ((Get-Prop $shot 'scene' 'world') -eq 'clip')", HeroVideo, StringComparison.Ordinal);
        Assert.Contains("if (-not (Test-Inside $source @('cache')))", HeroVideo, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------ M5 item 0 c5: Godot's own errors counted by every check (D19), and the gate's frame cap

    private static string GodotCount(string console)
    {
        var script = string.Join("\n", new[] { "Get-GodotErrorLines", "Get-GodotErrorCount", "Format-GodotErrorLine" }.Select(name => Function(Smoke, name))) + "\n$repoRoot = 'C:\\repo'\n";
        var run = RunPowerShell(script + $"$console = @'\n{console}\n'@\nWrite-Output ('COUNT=' + (Get-GodotErrorCount $console))\nforeach ($l in (Get-GodotErrorLines $console)) {{ Write-Output ('LINE=' + $l) }}");
        return run.Replace("\r\n", "\n");
    }

    [Fact]
    public void One_godot_error_with_a_csharp_backtrace_counts_once_and_a_script_error_and_a_user_error_count_once_each_c5()
    {
        var run = GodotCount("""
            2026-10-25T10:00:00.000Z INFO [core] frame pacing vsync=Enabled
            ERROR: System.NullReferenceException: Object reference not set to an instance of an object.
               at Lifestone.Game.Foo.Bar() in res://scripts/Foo.cs:line 12
               at: void Godot.Foo.Process (res://scripts/Foo.cs:208)
               C# backtrace (most recent call first):
                   [0] void Godot.GD.PushError(string) (/root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/GD.cs:366)
                   [1] void Lifestone.Game.Foo.Bar() (res://scripts/Foo.cs:12)
            SCRIPT ERROR: Parse Error: Unexpected token
               at: GDScript::reload (res://x.gd:3)
            USER ERROR: a user error
               at: push_error (core/variant/core.cpp:1)
            WARNING: this is a warning and does not count
            """);

        Assert.Contains("COUNT=3", run, StringComparison.Ordinal); // not 12: the "at:" and "C# backtrace" lines belong to their error
        Assert.Contains("LINE=ERROR: System.NullReferenceException:", run, StringComparison.Ordinal);
        Assert.Contains("LINE=SCRIPT ERROR: Parse Error: Unexpected token", run, StringComparison.Ordinal);
        Assert.Contains("LINE=USER ERROR: a user error", run, StringComparison.Ordinal);
        Assert.Contains("COUNT=0", GodotCount("2026-10-25T10:00:00.000Z INFO [core] nothing wrong\nWARNING: only a warning"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_lifestone_error_entry_is_not_a_godot_error_even_when_log_to_godot_echoes_it_as_an_error_line_c5()
    {
        // Log.ToGodot prints a Lifestone ERROR through GD.PushError: Godot prints "ERROR: <utc>Z ERROR [category] ..." and an "at:" line. That entry is the "Errors logged" column's (Get-ErrorCount), once.
        var run = GodotCount("""
            2026-10-25T10:00:00.000Z ERROR [logincheck] the login check failed stage=done why="x"
            ERROR: 2026-10-25T10:00:00.000Z ERROR [logincheck] the login check failed stage=done why="x"
               at: void Lifestone.Game.Core.Log.ToGodot(Lifestone.Game.Core.LogLevel, string) (res://scripts/Core/Log.cs:208)
               C# backtrace (most recent call first):
                   [0] void Godot.GD.PushError(string) (/root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/GD.cs:366)
            ERROR: Parent node is busy setting up children, `add_child()` failed.
               at: add_child (scene/main/node.cpp:1721)
            """);

        Assert.Contains("COUNT=1", run, StringComparison.Ordinal);
        Assert.Contains("LINE=ERROR: Parent node is busy", run, StringComparison.Ordinal);
        Assert.DoesNotContain("LINE=ERROR: 2026", run, StringComparison.Ordinal);

        // Lifestone's own count is as it was: the two entries that carry "Z ERROR [".
        var own = RunPowerShell(Function(Smoke, "Get-ErrorCount") + "\n$t = \"2026-10-25T10:00:00.000Z ERROR [a] x`nERROR: 2026-10-25T10:00:00.000Z ERROR [a] x`nERROR: Parent node is busy\"\nWrite-Output ('OWN=' + (Get-ErrorCount $t))");
        Assert.Contains("OWN=2", own, StringComparison.Ordinal);
    }

    [Fact]
    public void The_first_godot_error_line_in_a_row_keeps_no_path_outside_the_repository_c5()
    {
        var run = RunPowerShell(Function(Smoke, "Format-GodotErrorLine") + "\n$repoRoot = 'C:\\work\\checkout'\n"
            + "Write-Output ('A=' + (Format-GodotErrorLine 'ERROR: cannot open D:\\elsewhere\\x.dat for C:\\work\\checkout\\cache\\y.log'))\n"
            + "Write-Output ('B=' + (Format-GodotErrorLine ('ERROR: ' + ('x' * 400))).Length)");

        Assert.Contains("A=ERROR: cannot open <path> for <repo>\\cache\\y.log", run, StringComparison.Ordinal);
        Assert.Contains("B=160", run, StringComparison.Ordinal);
    }

    [Fact]
    public void A_godot_step_with_a_godot_error_fails_and_its_row_and_the_summary_carry_the_field_c5()
    {
        var script = string.Join("\n", new[] { "Get-GodotErrorLines", "Format-GodotErrorLine", "Get-DeclaredEngineErrors", "Split-ExpectedEngineErrors", "Register-GodotErrors", "Add-Result" }.Select(name => Function(Smoke, name))) + """

            $repoRoot = 'C:\repo'
            $script:results = [System.Collections.Generic.List[object]]::new()
            $script:audioFailures = @{}
            $script:godotErrors = @{}
            Register-GodotErrors 'engine-checks' "[engine] all passed`nERROR: Condition x is true.`n   at: foo (a.cpp:1)" | Out-Null
            Register-GodotErrors 'sky-checks' "[sky] all passed" | Out-Null
            Add-Result 'engine-checks' $true 0 1.0 'passed' 'a.log' 0
            Add-Result 'sky-checks' $true 0 1.0 'passed' 'b.log' 0
            Add-Result 'build' $true 0 1.0 'built' 'c.log'
            Add-Result 'replayed' $true 0 1.0 'from a child' 'd.log' -1 '' -GodotErrors '0'
            foreach ($r in $script:results) { Write-Output ('ROW=' + $r.Step + '|' + $r.Outcome + '|' + $r.GodotErrors + '|' + $r.Detail) }
            """;
        var run = RunPowerShell(script).Replace("\r\n", "\n");

        Assert.Contains("ROW=engine-checks|FAIL|1|passed; FAIL: godot_errors=1, first: ERROR: Condition x is true.", run, StringComparison.Ordinal);
        Assert.Contains("ROW=sky-checks|PASS|0|passed", run, StringComparison.Ordinal);
        Assert.Contains("ROW=build|PASS|n/a|built", run, StringComparison.Ordinal); // a step with no Godot run reads n/a
        Assert.Contains("ROW=replayed|PASS|0|from a child", run, StringComparison.Ordinal);
        Assert.Contains("passed godot_errors=0", run, StringComparison.Ordinal); // the console line carries the field

        // The table, the summary line, summary.json and the child rows carry it too.
        var smoke = Smoke;
        Assert.Contains("| Step | Result | Exit | Seconds | Errors logged | godot_errors | Detail | Log |", smoke, StringComparison.Ordinal);
        Assert.Contains("$($r.Errors) | $($r.GodotErrors) | $($r.Detail", smoke, StringComparison.Ordinal);
        Assert.Contains("$godotLine = \"godot_errors=$godotTotal (judged 0 on each of", smoke, StringComparison.Ordinal);
        Assert.Contains("godot_errors = $(if (\"$($_.GodotErrors)\" -match '^\\d+$') { [int]$_.GodotErrors } else { 'n/a' })", smoke, StringComparison.Ordinal);
        Assert.Contains("godot_errors = [int]$godotTotal", smoke, StringComparison.Ordinal);
        Assert.Contains("[string] $FrameJudge = '', [string] $GodotErrors = ''", smoke, StringComparison.Ordinal);
        Assert.Contains("$note -GodotErrors $godot -ExpectedEngineErrors", smoke, StringComparison.Ordinal); // a replayed child row keeps its field

        // Every path that runs Godot counts its console: Invoke-Godot (all the steps through it), terrain-golden and login (their own runs).
        Assert.Contains("Register-GodotErrors $Step $text | Out-Null", Function(smoke, "Invoke-Godot"), StringComparison.Ordinal);
        Assert.Contains("Register-GodotErrors $step $text | Out-Null", smoke[smoke.IndexOf("'terrain-golden' {", StringComparison.Ordinal)..smoke.IndexOf("'engine-checks' {", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("Register-GodotErrors $step $text | Out-Null", smoke[smoke.IndexOf("'login' {", StringComparison.Ordinal)..smoke.IndexOf("'offline-player' {", StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    [Fact]
    public void Smoke_max_fps_is_appended_to_every_godot_steps_game_arguments_only_when_given_c5()
    {
        var smoke = Smoke;
        Assert.Matches(@"\[ValidateRange\(0, 1000\)\]\s+\[int\] \$MaxFps = 0,", smoke);
        Assert.Contains("$arguments += \"-GameArgs:$GameArgs$(Get-MaxFpsArgs)\"", Function(smoke, "Invoke-Godot"), StringComparison.Ordinal);
        Assert.Contains("\"-GameArgs:--login-check=enter$(Get-MaxFpsArgs)\"", smoke, StringComparison.Ordinal); // the login step starts the game through tools\play.ps1
        Assert.Contains("@(if ($MaxFps -gt 0) { '-MaxFps'; \"$MaxFps\" })", smoke, StringComparison.Ordinal); // the -Parallel and -Lanes children get it too

        var function = Function(smoke, "Get-MaxFpsArgs");
        Assert.Equal("[ --max-fps=60]", RunPowerShell(function + "\n$MaxFps = 60\nWrite-Output ('[' + (Get-MaxFpsArgs) + ']')").Trim());
        Assert.Equal("[]", RunPowerShell(function + "\n$MaxFps = 0\nWrite-Output ('[' + (Get-MaxFpsArgs) + ']')").Trim()); // outside the gate a run is as it was

        // The option exists under that spelling.
        Assert.Contains("new(\"--max-fps\", \"N\"", File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Client", "Options", "OptionCatalog.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void Slice_check_reports_and_judges_the_godot_errors_of_smokes_rows_and_summary_c5()
    {
        var read = Function(SliceCheck, "Read-SmokeRow");
        string Row(string line) => RunPowerShell(read + $"\n$run = @{{ Lines = @('{line}') }}\n$r = Read-SmokeRow $run 'play-checks'\nWrite-Output ('R=' + $r.Result + '|' + $r.GodotErrors + '|' + $r.Detail)").Trim();

        Assert.Equal("R=PASS|0|pass=80 fail=0", Row("[smoke] play-checks             PASS  (68.3 s) pass=80 fail=0 godot_errors=0"));
        Assert.Equal("R=FAIL|2|passed; FAIL: godot_errors=2, first: ERROR: x", Row("[smoke] play-checks             FAIL  (68.3 s) passed; FAIL: godot_errors=2, first: ERROR: x godot_errors=2"));
        Assert.Equal("R=PASS|n/a|built", Row("[smoke] play-checks             PASS  (68.3 s) built")); // a step with no Godot run has no field

        var script = SliceCheck;
        Assert.Contains("\"$detail godot_errors=$godot$($godotRow.Expected)\".Trim()", script, StringComparison.Ordinal); // the steps stage's line names the field
        Assert.Contains("$verdict += \" godot_errors=$godotTotal\"", script, StringComparison.Ordinal); // the smoke stage names the total
        Assert.Contains("$pass = $run.Code -eq 0 -and $godotTotal -notmatch '^[1-9]'", script, StringComparison.Ordinal); // and fails on any
        Assert.Contains("^(?:SCRIPT |USER )?ERROR:", Function(script, "Get-StepFailureLines"), StringComparison.Ordinal); // a failing step shows Godot's first lines
    }

    // ------------------------------------------------------------------------------------------ M5 item 0 fix: declared engine errors

    private static string DeclaredRow(string console)
    {
        var script = string.Join("\n", new[] { "Get-GodotErrorLines", "Format-GodotErrorLine", "Get-DeclaredEngineErrors", "Split-ExpectedEngineErrors", "Register-GodotErrors", "Add-Result" }.Select(name => Function(Smoke, name))) + """

            $repoRoot = 'C:\repo'
            $script:results = [System.Collections.Generic.List[object]]::new()
            $script:audioFailures = @{}
            $script:godotErrors = @{}
            """ + $"\n$console = @'\n{console}\n'@\n" + """
            Register-GodotErrors 'interior-engine-checks' $console | Out-Null
            Add-Result 'interior-engine-checks' $true 0 1.0 'holds=37 fails=0' 'a.log' 0
            $r = $script:results[0]
            Write-Output ('ROW=' + $r.Outcome + '|' + $r.GodotErrors + '|' + $r.ExpectedEngineErrors + '|' + $r.Detail)
            """;
        return RunPowerShell(script).Replace("\r\n", "\n");
    }

    private const string DeclaredBegin = """
        2026-10-25T22:04:58.811Z INFO  [interiorcheck] expected engine errors begin text="Too many instances using shader instance variables" count=2
        2026-10-25T22:04:58.811Z INFO  [interiorcheck] expected engine errors begin text=global_shader_uniforms.instance_buffer_pos.has(p_instance) count=2
        """;

    private const string DeclaredEnd = "2026-10-25T22:04:58.934Z INFO  [interiorcheck] expected engine errors end";

    private const string TooMany = "ERROR: Too many instances using shader instance variables. Increase buffer size in Project Settings.\n   at: instance_allocate (servers/rendering/renderer_rd/storage_rd/material_storage.cpp:1)";

    private const string FreedTwice = "ERROR: Condition \"global_shader_uniforms.instance_buffer_pos.has(p_instance)\" is true. Returning: -1\n   at: x (y.cpp:2)";

    [Fact]
    public void Declared_engine_errors_seen_equal_to_declared_pass_with_godot_errors_zero_and_the_expected_count_in_the_row()
    {
        // The log holds stdout and stderr as two blocks (the markers come first, the ERROR lines after them), so the lines are matched by text, not by position.
        var run = DeclaredRow($"{DeclaredBegin}\n{DeclaredEnd}\n{TooMany}\n{TooMany}\n{FreedTwice}\n{FreedTwice}");

        Assert.Contains("ROW=PASS|0|4/4|holds=37 fails=0", run, StringComparison.Ordinal);
    }

    [Fact]
    public void Declared_engine_errors_seen_unequal_to_declared_fail_the_step_naming_both_counts()
    {
        var fewer = DeclaredRow($"{DeclaredBegin}\n{DeclaredEnd}\n{TooMany}\n{TooMany}\n{FreedTwice}");
        Assert.Contains("ROW=FAIL|0|3/4|", fewer, StringComparison.Ordinal);
        Assert.Contains("FAIL: expected_engine_errors=3/4 (declared 2 but saw 1 of 'global_shader_uniforms.instance_buffer_pos.has(p_instance)')", fewer, StringComparison.Ordinal);

        var more = DeclaredRow($"{DeclaredBegin}\n{DeclaredEnd}\n{TooMany}\n{TooMany}\n{TooMany}\n{FreedTwice}\n{FreedTwice}");
        Assert.Contains("ROW=FAIL|0|5/4|", more, StringComparison.Ordinal);
        Assert.Contains("declared 2 but saw 3 of 'Too many instances using shader instance variables'", more, StringComparison.Ordinal);

        // A declaration that was never closed (the control did not finish) fails too.
        var open = DeclaredRow($"{DeclaredBegin}\n{TooMany}\n{TooMany}\n{FreedTwice}\n{FreedTwice}");
        Assert.Contains("ROW=FAIL|0|4/4|", open, StringComparison.Ordinal);
        Assert.Contains("the declaration is not closed (no end marker)", open, StringComparison.Ordinal);
    }

    [Fact]
    public void An_undeclared_godot_error_fails_the_step_anywhere_in_it_and_with_no_declaration_every_error_counts()
    {
        var other = DeclaredRow($"{DeclaredBegin}\n{DeclaredEnd}\n{TooMany}\n{TooMany}\n{FreedTwice}\n{FreedTwice}\nERROR: Parent node is busy setting up children.\n   at: add_child (scene/main/node.cpp:1)");
        Assert.Contains("ROW=FAIL|1|4/4|", other, StringComparison.Ordinal);
        Assert.Contains("FAIL: godot_errors=1, first: ERROR: Parent node is busy setting up children.", other, StringComparison.Ordinal);

        // The same errors with no declaration are all unexpected.
        var undeclared = DeclaredRow($"{TooMany}\n{TooMany}");
        Assert.Contains("ROW=FAIL|2||", undeclared, StringComparison.Ordinal);
        Assert.Contains("FAIL: godot_errors=2, first: ERROR: Too many instances", undeclared, StringComparison.Ordinal);

        // The console line and the summary carry the count seen against the declared one.
        Assert.Contains("godot_errors=0 expected_engine_errors=4/4", DeclaredRow($"{DeclaredBegin}\n{DeclaredEnd}\n{TooMany}\n{TooMany}\n{FreedTwice}\n{FreedTwice}"), StringComparison.Ordinal);
        Assert.Contains("expected_engine_errors = $(if ($_.ExpectedEngineErrors)", Smoke, StringComparison.Ordinal);
    }

    [Fact]
    public void The_interior_check_declares_its_negative_controls_errors_before_provoking_them_and_closes_the_declaration()
    {
        var source = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "World", "Interiors", "InteriorEngineChecks.cs"));
        var begin = source.IndexOf("\"expected engine errors begin\"", StringComparison.Ordinal);
        var provoke = source.IndexOf("for (var i = 0; i < blocks + 8; i++)", StringComparison.Ordinal);
        var end = source.IndexOf("\"expected engine errors end\"", StringComparison.Ordinal);

        Assert.True(begin > 0 && begin < provoke && provoke < end, "begin, then the negative control's instances, then end");
        Assert.Contains("(\"text\", \"Too many instances using shader instance variables\")", source, StringComparison.Ordinal);
        Assert.Contains("(\"count\", overflow)", source, StringComparison.Ordinal);
        Assert.Contains("var overflow = blocks + 8 + 2 - instancesWithBlocks;", source, StringComparison.Ordinal); // computed in the check from the buffer's blocks
    }

    [Fact]
    public void Soak_watch_selects_no_step_and_is_not_an_unmatched_tools_path()
    {
        var map = File.ReadAllText(RepositoryFiles.PathOf("tools", "lib", "StepPaths.psd1"));
        Assert.Contains("'tools/soak-watch.ps1'", map[map.IndexOf("NoGodot = @(", StringComparison.Ordinal)..map.IndexOf("Live = @{", StringComparison.Ordinal)], StringComparison.Ordinal);

        var path = RepositoryFiles.PathOf("tools", "lib", "StepPaths.psd1");
        var run = RunPowerShell(Function(SliceCheck, "Resolve-ChangedSteps") + $"\n$map = Import-PowerShellDataFile '{path}'\n$r = Resolve-ChangedSteps @('tools/soak-watch.ps1') $map\n"
            + "Write-Output ('STEPS=' + $r.Steps.Count + ' UNMATCHED=' + $r.Unmatched.Count + ' EVERYTHING=' + $r.Everything)");
        Assert.Contains("STEPS=0 UNMATCHED=0 EVERYTHING=False", run, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------ M5 item 4 c1: the mage's judged fields (D55, D38, D56) and the autopilot step

    private const string MageFight = "[logincheck] journey station=den-fight ok=true s=49.4 kills=9 target_kills=9 mana_wait_max_s=1.5 " + Tour + " " + LootOff;

    /// <summary>A mage journey's text: the den-fight, the logistics and death station lines given (null = none) and the done line.</summary>
    private static string MageText(string? logistics = MageLogistics, string? death = MageDeath, string? done = null) =>
        MageFight + "\n" + (logistics is null ? string.Empty : logistics + "\n") + (death is null ? string.Empty : death + "\n") + (done ?? JourneyDone("mage", "teleports=1 recalls=3 recovery_places=0 granted=0 places=0"));

    [Fact]
    public void The_journey_verdict_judges_places_bought_spent_xp_and_the_death_line_on_the_mage_only_D55_D38_D56_M5_item_4_c1()
    {
        var got = JudgeJourneys(
            ("pass", MageText(), false),
            ("appended", MageText(MageLogistics + " why=none newfield=9 granted_total=4", MageDeath + " newfield=7 why=x"), false), // fields appended after the known ones (item 5 appends to existing lines): by key, a longer name is not the field
            ("soldseven", MageText(MageLogistics.Replace("sold=0", "sold=7", StringComparison.Ordinal)), false), // sold= stays report-only while looting is off (D38)
            ("soldgone", MageText(MageLogistics.Replace(" sold=0", string.Empty, StringComparison.Ordinal)), false),
            ("pretripyes", MageText(death: MageDeath.Replace("rebuffed_pretrip=no", "rebuffed_pretrip=yes", StringComparison.Ordinal)), false), // report-only (D56)
            ("doneplaces", MageText(done: JourneyDone("mage", "teleports=1 recalls=3 recovery_places=0 granted=0 places=2")), false),
            ("donemissing", MageText(done: JourneyDone("mage", "teleports=1 recalls=3 recovery_places=0 granted=0 places=0").Replace(" places=0", string.Empty, StringComparison.Ordinal)), false),
            ("stationgranted", MageText(MageLogistics.Replace("granted=0", "granted=3", StringComparison.Ordinal)), false),
            ("stationplaces", MageText(MageLogistics.Replace("places=0", "places=1", StringComparison.Ordinal)), false),
            ("otherstation", MageText() .Replace("[logincheck] journey station=logistics", "[logincheck] journey station=heal ok=true problems=0 granted=2\n[logincheck] journey station=logistics", StringComparison.Ordinal), false),
            ("recoveryonly", MageText().Replace("[logincheck] journey station=logistics", "[logincheck] journey station=heal ok=true recovery_places=3\n[logincheck] journey station=logistics", StringComparison.Ordinal), false),
            ("bought0", MageText(MageLogistics.Replace("bought=3", "bought=0", StringComparison.Ordinal)), false),
            ("spentxp0", MageText(MageLogistics.Replace("spent_xp=5000", "spent_xp=0", StringComparison.Ordinal)), false),
            ("spentxp0pool", MageText(MageLogistics.Replace("spent_xp=5000", "spent_xp=0", StringComparison.Ordinal) + " spend_why=pool-covers-nothing"), false), // M6 7 C: the pool covered no planned next rank, not a failure
            ("spentxp0refused", MageText(MageLogistics.Replace("spent_xp=5000", "spent_xp=0", StringComparison.Ordinal) + " spend_why=refused"), false), // something to cover, nothing spent: still fails
            ("spentxp5000pool", MageText(MageLogistics + " spend_why=pool-covers-nothing"), false),
            ("boughtgone", MageText(MageLogistics.Replace(" bought=3", string.Empty, StringComparison.Ordinal)), false),
            ("spentxpgone", MageText(MageLogistics.Replace(" spent_xp=5000", string.Empty, StringComparison.Ordinal)), false),
            ("skippedtrip", MageText("[logincheck] journey station=logistics ok=false s=0.0 skipped=no-trip-due"), false),
            ("nologistics", MageText(null), false),
            ("deaths2", MageText(death: MageDeath.Replace("deaths=1", "deaths=2", StringComparison.Ordinal)), false),
            ("nocorpse", MageText(death: MageDeath.Replace("corpse_recovered=yes", "corpse_recovered=no", StringComparison.Ordinal)), false),
            ("vitae", MageText(death: MageDeath.Replace("vitae_grants=0", "vitae_grants=1", StringComparison.Ordinal)), false),
            ("notrebuffed", MageText(death: MageDeath.Replace("rebuffed=yes", "rebuffed=no", StringComparison.Ordinal)), false),
            ("pretriponly", MageText(death: MageDeath.Replace(" rebuffed=yes", string.Empty, StringComparison.Ordinal)), false), // rebuffed_pretrip=no is not rebuffed=
            ("nodeath", MageText(death: null), false));

        Assert.StartsWith("True|", got["pass"], StringComparison.Ordinal);
        Assert.DoesNotContain("(mage", got["pass"][got["pass"].IndexOf('|', StringComparison.Ordinal)..], StringComparison.Ordinal); // no reason in the detail of a pass
        Assert.StartsWith("True|", got["appended"], StringComparison.Ordinal);
        Assert.StartsWith("True|", got["soldseven"], StringComparison.Ordinal);
        Assert.Contains("sold=7", Field(got["soldseven"], 6), StringComparison.Ordinal);
        Assert.StartsWith("True|", got["soldgone"], StringComparison.Ordinal);
        Assert.StartsWith("True|", got["pretripyes"], StringComparison.Ordinal);
        Assert.Contains("rebuffed_pretrip=yes", Field(got["pretripyes"], 7), StringComparison.Ordinal); // shown, not judged
        Assert.StartsWith("True|", got["recoveryonly"], StringComparison.Ordinal); // recovery_places= is another field

        Assert.Contains("; places 2 not 0 (mage done line)", got["doneplaces"], StringComparison.Ordinal);
        Assert.Contains("; places n/a on the done line, not 0 (mage)", got["donemissing"], StringComparison.Ordinal);
        Assert.Contains("; granted 3 not 0 (mage logistics station)", got["stationgranted"], StringComparison.Ordinal);
        Assert.Contains("; places 1 not 0 (mage logistics station)", got["stationplaces"], StringComparison.Ordinal);
        Assert.Contains("; granted 2 not 0 (mage heal station)", got["otherstation"], StringComparison.Ordinal);
        Assert.Contains("; bought 0 under 1 (mage logistics station)", got["bought0"], StringComparison.Ordinal);
        Assert.Contains("; spent_xp 0 under 1 (mage logistics station)", got["spentxp0"], StringComparison.Ordinal);
        Assert.StartsWith("True|", got["spentxp0pool"], StringComparison.Ordinal);
        Assert.StartsWith("True|", got["spentxp5000pool"], StringComparison.Ordinal);
        Assert.Contains("; spent_xp 0 under 1 (mage logistics station)", got["spentxp0refused"], StringComparison.Ordinal);
        Assert.Contains("; bought n/a on the logistics station, not 1 or more (mage)", got["boughtgone"], StringComparison.Ordinal);
        Assert.Contains("; spent_xp n/a on the logistics station, not 1 or more (mage)", got["spentxpgone"], StringComparison.Ordinal);
        Assert.Contains("; bought n/a on the logistics station, not 1 or more (mage)", got["skippedtrip"], StringComparison.Ordinal);
        Assert.Contains("; bought n/a, no logistics station line (mage), not 1 or more", got["nologistics"], StringComparison.Ordinal);
        Assert.Contains("; spent_xp n/a, no logistics station line (mage), not 1 or more", got["nologistics"], StringComparison.Ordinal);
        Assert.Contains("; deaths 2 not 1 (mage death station)", got["deaths2"], StringComparison.Ordinal);
        Assert.Contains("; corpse_recovered no not yes (mage death station)", got["nocorpse"], StringComparison.Ordinal);
        Assert.Contains("; vitae_grants 1 not 0 (mage death station)", got["vitae"], StringComparison.Ordinal);
        Assert.Contains("; rebuffed no not yes (mage death station)", got["notrebuffed"], StringComparison.Ordinal);
        Assert.Contains("; rebuffed n/a on the death station, not yes (mage)", got["pretriponly"], StringComparison.Ordinal);
        Assert.Contains("; deaths n/a, no death station line (mage), not 1", got["nodeath"], StringComparison.Ordinal);
        Assert.Contains("; rebuffed n/a, no death station line (mage), not yes", got["nodeath"], StringComparison.Ordinal);
        Assert.All(new[] { "doneplaces", "donemissing", "stationgranted", "stationplaces", "otherstation", "bought0", "spentxp0", "boughtgone", "spentxpgone", "skippedtrip", "nologistics", "deaths2", "nocorpse", "vitae", "notrebuffed", "pretriponly", "nodeath" },
            k => Assert.StartsWith("False|", got[k], StringComparison.Ordinal));
        Assert.DoesNotContain("; bought", got["deaths2"], StringComparison.Ordinal); // a miss names only its own field
        Assert.DoesNotContain("; deaths", got["bought0"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_fighters_journeys_read_the_same_fields_report_only_D55_M5_item_4_c1()
    {
        const string Logistics = "[logincheck] journey station=logistics ok=true trigger=forced granted=5 places=3 loot=off sold=0 bought=0 spent=0 notes_sold=0 spent_xp=0 raises=0";
        const string Death = "[logincheck] journey station=death ok=true deaths=0 corpse_recovered=no route=none items_back=0 vitae_grants=2 rebuffed=no rebuffed_pretrip=no relines=0 trip_abort=none";
        var melee = Logistics + "\n" + Death + "\n" + JourneyDone("melee", "teleports=1 recalls=1 recovery_places=0 granted=4 places=5");
        var archer = Logistics + "\n" + Death + "\n" + JourneyDone("archer", "teleports=1 recalls=1 recovery_places=0 granted=0 places=4");
        var archerGranted = Logistics + "\n" + Death + "\n" + JourneyDone("archer", "teleports=1 recalls=1 recovery_places=0 granted=1 places=4");
        var got = JudgeJourneys(("melee", melee, false), ("archer", archer, false), ("archergranted", archerGranted, false), ("meleebare", JourneyDone("melee", "teleports=1 recalls=1 recovery_places=0 granted=1"), false));

        Assert.StartsWith("True|", got["melee"], StringComparison.Ordinal); // granted=5 places=3 bought=0 deaths=0 rebuffed=no and a done line with places=5 fail nothing for the melee
        Assert.StartsWith("True|", got["archer"], StringComparison.Ordinal);
        Assert.Contains("granted=5 places=3 loot=off sold=0 bought=0 spent=0 notes_sold=0 spent_xp=0", Field(got["melee"], 6), StringComparison.Ordinal); // shown, report-only
        Assert.Contains("deaths=0 corpse_recovered=no route=none items_back=0 vitae_grants=2 rebuffed=no rebuffed_pretrip=no", Field(got["melee"], 7), StringComparison.Ordinal);
        Assert.Equal("5", Field(got["melee"], 8));
        Assert.Equal("4", Field(got["archer"], 8));
        Assert.DoesNotContain("(mage", got["melee"], StringComparison.Ordinal);
        Assert.StartsWith("False|", got["archergranted"], StringComparison.Ordinal); // M4 4.2 J2's judge on the archer's done line is not loosened
        Assert.Contains("; granted 1 not 0 (archer)", got["archergranted"], StringComparison.Ordinal);
        Assert.StartsWith("True|", got["meleebare"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_autopilot_step_is_one_smoke_step_on_the_mage_account_with_the_dials_arguments_M5_item_4_c1()
    {
        var script = Smoke;
        var steps = Regex.Match(script, @"\$allSteps = @\(([^)]*)\)", RegexOptions.Singleline).Groups[1].Value;
        var backend = Regex.Match(script, @"\$backendSteps = @\(([^)]*)\)").Groups[1].Value;
        var audio = Regex.Match(script, @"\$audioSteps = @\(([^)]*)\)").Groups[1].Value;
        var judged = Regex.Match(script, @"\$frameJudgedSteps = @\(([^)]*)\)").Groups[1].Value;
        foreach (var list in new[] { steps, backend, audio })
        {
            Assert.True(list.IndexOf("'journey-archer'", StringComparison.Ordinal) < list.IndexOf("'autopilot-mage'", StringComparison.Ordinal), "autopilot-mage follows the journeys");
        }

        Assert.DoesNotContain("autopilot-mage", judged, StringComparison.Ordinal); // its frames are report-only (contended): not a frame-judged step
        Assert.Matches(@"'autopilot-mage'\s+= 'test'", script);
        Assert.Contains("Import-Module (Join-Path $PSScriptRoot 'lib\\GateJudge.psm1') -Force -Function Get-AutopilotVerdict", script, StringComparison.Ordinal);

        // The step runs only with -Backend require, before the backend gate, as a SKIP.
        var skip = script.IndexOf("if ($autopilotSteps.ContainsKey($step) -and $Backend -ne 'require')", StringComparison.Ordinal);
        var gate = script.IndexOf("if ($backendSteps -contains $step -and -not (Test-BackendGate", StringComparison.Ordinal);
        Assert.True(skip > 0 && skip < gate, "the autopilot skip rule must come before the backend gate");
        Assert.Contains("-Skipped", script[skip..gate], StringComparison.Ordinal);

        // The body: the dial's arguments from the table, every number in one place, the frames into <OutDir>/frames, the autopilot's own verdict, the report-only fields and frames.
        var body = script[script.IndexOf("{ $autopilotSteps.ContainsKey($_) } {", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("{ $journeySteps.ContainsKey($_) } {", StringComparison.Ordinal)];
        Assert.Contains(
            "$autopilotArgs = \"--account=$($stepAccounts[$step]) --login-check=autopilot --autopilot-minutes=$($auto.Minutes) --autopilot-trip-after-kills=$($auto.TripAfterKills) --autopilot-test-death=on --autopilot-test-interrupt-at=$($auto.InterruptAt) --autopilot-test-handback-after=$($auto.HandbackAfter) --play-screenshot=$frameStem\"",
            body, StringComparison.Ordinal);
        Assert.Contains("Invoke-Godot $step $autopilotArgs $auto.Seconds '' $defaultSize", body, StringComparison.Ordinal);
        Assert.Contains("(Join-Path $OutDir 'frames')", body, StringComparison.Ordinal);
        Assert.Contains("Get-AutopilotVerdict $run.Text", body, StringComparison.Ordinal);
        Assert.Contains("Resolve-LiveFrames ($run.Code -eq 0 -and $judged.Ok) $run", body, StringComparison.Ordinal);
        Assert.Contains("report-only autopilot: $($judged.ReportOnly); report-only frames: $($judged.Frames)", body, StringComparison.Ordinal);
        Assert.Contains("-FrameOver50 'n/a'", body, StringComparison.Ordinal);
        Assert.DoesNotContain("--max-fps", body, StringComparison.Ordinal); // the cap, when the gate sets one, is smoke's -MaxFps (Invoke-Godot appends it), as for the journeys
        Assert.DoesNotContain("$journeySteps", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_step_paths_name_the_gate_host_and_the_autopilots_files_and_the_autopilot_step_is_a_live_advisory_M5_item_4_c1()
    {
        var map = StepPaths;
        var noGodot = map[map.IndexOf("NoGodot = @(", StringComparison.Ordinal)..map.IndexOf("Live = @{", StringComparison.Ordinal)];
        Assert.Contains("'tools/lib/GateHost.psm1'", noGodot, StringComparison.Ordinal); // c2's module: a gate-side file no Godot step can notice
        Assert.Contains("'src/Lifestone.Client/Session/Autopilot/'", noGodot, StringComparison.Ordinal); // covered by the tests
        Assert.Contains("'autopilot-mage' = @(", map, StringComparison.Ordinal); // slice-check's advisory names the lane when a Session folder changes

        var path = RepositoryFiles.PathOf("tools", "lib", "StepPaths.psd1");
        var run = RunPowerShell(Function(SliceCheck, "Resolve-ChangedSteps") + $"\n$map = Import-PowerShellDataFile '{path}'\n"
            + "foreach ($f in @('tools/lib/GateHost.psm1', 'src/Lifestone.Client/Session/Autopilot/AutopilotScenario.cs', 'src/Lifestone.Game/scripts/Session/AutopilotHost.cs', 'src/Lifestone.Game/scripts/Session/AutopilotLegRunner.cs')) {\n"
            + "  $r = Resolve-ChangedSteps @($f) $map\n  Write-Output ('R=' + $f + '|' + (($r.Steps | Sort-Object) -join ',') + '|unmatched=' + $r.Unmatched.Count + '|everything=' + $r.Everything + '|live=' + ($r.Live -join ','))\n}");

        Assert.Contains("R=tools/lib/GateHost.psm1||unmatched=0|everything=False|live=", run, StringComparison.Ordinal); // no step, not an unmatched tools path
        Assert.Contains("R=src/Lifestone.Client/Session/Autopilot/AutopilotScenario.cs|", run, StringComparison.Ordinal);
        Assert.Contains("unmatched=0", run.Split("R=src/Lifestone.Client/Session/Autopilot/AutopilotScenario.cs|")[1].Split('\n')[0], StringComparison.Ordinal);
        Assert.Contains("autopilot-mage", run.Split("R=src/Lifestone.Client/Session/Autopilot/AutopilotScenario.cs|")[1].Split('\n')[0], StringComparison.Ordinal); // the live advisory
        foreach (var file in new[] { "AutopilotHost.cs", "AutopilotLegRunner.cs" })
        {
            var line = run.Split("R=src/Lifestone.Game/scripts/Session/" + file + "|")[1].Split('\n')[0];
            Assert.Contains("cli-help", line, StringComparison.Ordinal); // the Main scene's --autopilot-* options
            Assert.Contains("cli-bad-argument", line, StringComparison.Ordinal);
            Assert.Contains("play-checks", line, StringComparison.Ordinal); // the dial's rows
        }

        // R12 (every non-soak loopback class is in an area): AutopilotLiveTests lost its Tier=Soak tag and is named in the mage area, with the autopilot's folders.
        var areas = File.ReadAllText(RepositoryFiles.PathOf("tools", "lib", "GateAreas.psd1"));
        var mage = areas[areas.IndexOf("'mage'      = @{", StringComparison.Ordinal)..areas.IndexOf("'servermod'", StringComparison.Ordinal)];
        Assert.Contains("'AutopilotLiveTests'", mage, StringComparison.Ordinal);
        Assert.Contains("'src/Lifestone.Client/Session/Autopilot/'", mage, StringComparison.Ordinal);
        var live = File.ReadAllText(RepositoryFiles.PathOf("tests", "Lifestone.Loopback.Tests", "Session", "AutopilotLiveTests.cs"));
        Assert.DoesNotContain("[Trait(\"Tier\", \"Soak\")]", live, StringComparison.Ordinal); // the soak phase (Category=Loopback&Tier=Soak) would run it: it is the lead's own run
        Assert.Contains("[Trait(\"Lane\", \"solo\")]", live, StringComparison.Ordinal); // and the account lanes still leave it out
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

    /// <summary>
    /// M5 4 live gate round 1: smoke imports GateHost after GateJudge, because GateJudge imports GateHost with -Force inside itself and that
    /// removes a global GateHost imported before it, so the solo frame judge's Wait-GateForeignGodot was unknown and wrote no rows.
    /// </summary>
    [Fact]
    public void Smoke_imports_gate_host_after_gate_judge_so_the_foreign_godot_wait_is_defined()
    {
        var smoke = Smoke;
        var judge = smoke.IndexOf(@"Import-Module (Join-Path $PSScriptRoot 'lib\GateJudge.psm1')", StringComparison.Ordinal);
        var host = smoke.IndexOf(@"Import-Module (Join-Path $PSScriptRoot 'lib\GateHost.psm1')", StringComparison.Ordinal);
        Assert.True(judge >= 0 && host >= 0, "smoke.ps1 imports both modules");
        Assert.True(host > judge, "GateHost must be imported after GateJudge");
    }
}
