using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// M4 1.1 s3, the testing loop's remainder (m4-plan.md section 4.1 row s3): smoke's summary.json, the locked restore, the test-count baseline file read by
/// slice-check, the live legs' frame fields and contended rule, the parallel runs' own user-data slots, and the Timing and KnownFailure traits in both
/// filters. Text tests over tools/*.ps1, README.md and tests/Shared (plus the pure functions run in PowerShell); no Godot, no backend, no dats.
/// </summary>
public sealed class ScriptLoopRemainderTests
{
    private const string Filter = "Category!=Loopback&Tier!=Soak&Tier!=Exhaustive&Tier!=Timing&Status!=KnownFailure";

    private static string Smoke => Normal(File.ReadAllText(RepositoryFiles.PathOf("tools", "smoke.ps1")));

    private static string SliceCheck => Normal(File.ReadAllText(RepositoryFiles.PathOf("tools", "slice-check.ps1")));

    private static string Normal(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    // ---------------------------------------------------------------------------------------------- 1: summary.json

    [Fact]
    public void Smoke_writes_summary_json_beside_summary_md_without_detail_text()
    {
        var script = Smoke;
        var start = script.IndexOf("Set-Content -LiteralPath (Join-Path $OutDir 'summary.md')", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var block = script[start..script.IndexOf("Write-Host ''", start, StringComparison.Ordinal)];

        Assert.Contains("(Join-Path $OutDir 'summary.json')", block, StringComparison.Ordinal);
        foreach (var field in new[] { "commit", "branch", "filter", "started_utc", "ended_utc", "verdict", "steps", "seconds", "frame_over_50", "frame_judge" })
        {
            Assert.Matches(@"\b" + field + @"\s*=|\['" + field + @"'\]", block);
        }

        Assert.Contains("rev-parse HEAD", block, StringComparison.Ordinal);
        Assert.Contains("rev-parse --abbrev-ref HEAD", block, StringComparison.Ordinal);
        Assert.Contains("filter     = $testFilter", block, StringComparison.Ordinal);
        Assert.Contains("[DateTime]::UtcNow", block, StringComparison.Ordinal);
        // The json never carries the free-text detail (it can hold a path or an account name) nor the repository path.
        var json = block[block.IndexOf("$json = [ordered]", StringComparison.Ordinal)..];
        Assert.DoesNotContain("Detail", json, StringComparison.Ordinal);
        Assert.DoesNotContain("$repoRoot", json, StringComparison.Ordinal);
        Assert.DoesNotContain("account", json, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------------- 2: locked restore

    [Fact]
    public void Smokes_build_step_restores_in_locked_mode_and_every_project_has_its_lock_file()
    {
        var script = Smoke;
        var build = script[script.IndexOf("            'build' {", StringComparison.Ordinal)..];
        build = build[..build.IndexOf("            'test' {", StringComparison.Ordinal)];

        Assert.Contains("'restore', (Join-Path $repoRoot 'Lifestone.slnx'), '--locked-mode', '--disable-build-servers'", build, StringComparison.Ordinal);
        Assert.Contains("'--no-restore'", build, StringComparison.Ordinal);
        Assert.True(build.IndexOf("'--locked-mode'", StringComparison.Ordinal) < build.IndexOf("'build', ", StringComparison.Ordinal), "the locked restore comes first");
        Assert.Contains("<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>", File.ReadAllText(RepositoryFiles.PathOf("Directory.Build.props")), StringComparison.Ordinal);
        var separator = Path.DirectorySeparatorChar;
        foreach (var root in new[] { "src", "tests" })
        {
            foreach (var project in Directory.GetFiles(RepositoryFiles.PathOf(root), "*.csproj", SearchOption.AllDirectories).Where(p => !p.Contains($"{separator}obj{separator}", StringComparison.Ordinal)))
            {
                Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(project)!, "packages.lock.json")), "no packages.lock.json beside " + Path.GetFileName(project));
            }
        }
    }

    // ---------------------------------------------------------------------------------------------- 3: the count baseline

    [Fact]
    public void The_baseline_file_holds_the_default_and_exhaustive_counts_and_never_below_the_seed()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.PathOf("tests", "Shared", "test-count-baseline.json")));
        var root = document.RootElement;

        // Seeded 7968 and 76 (M4 1.1 s3); the lead raises it at a merge, a slice never lowers it.
        Assert.True(root.GetProperty("default").GetInt32() >= 7968);
        Assert.True(root.GetProperty("exhaustive").GetInt32() >= 76);
    }

    [Fact]
    public void Slice_check_reads_the_baseline_as_the_default_floor_and_an_explicit_min_tests_wins()
    {
        var script = SliceCheck;

        Assert.Contains("$baselineFile = Join-Path $repoRoot 'tests\\Shared\\test-count-baseline.json'", script, StringComparison.Ordinal);
        Assert.Contains("$minTestsSource = if ($PSBoundParameters.ContainsKey('MinTests')) { '-MinTests' } else { 'baseline file' }", script, StringComparison.Ordinal);
        Assert.Contains("if ($minTestsSource -eq 'baseline file') { $MinTests = $baseline.Default }", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-TestRun 'test' $testFilter $MinTests", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-TestRun 'exhaustive' $exhaustiveFilter $exhaustiveMinimum", script, StringComparison.Ordinal);
        Assert.Contains("(the count must never decrease)", script, StringComparison.Ordinal);
        // A missing file is loud, never a zero floor.
        Assert.Contains("The test-count baseline file is missing", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_TestBaseline_reads_the_checked_in_file_and_fails_loudly_on_a_missing_one()
    {
        var function = Function(SliceCheck, "Read-TestBaseline");
        var file = RepositoryFiles.PathOf("tests", "Shared", "test-count-baseline.json");

        var ok = RunPowerShell("$ErrorActionPreference='Stop'; Set-StrictMode -Version Latest; $baselineFile = '" + file + "'\n" + function
            + "\n$b = Read-TestBaseline\nWrite-Output (\"DEFAULT=\" + $b.Default)\nWrite-Output (\"EXHAUSTIVE=\" + $b.Exhaustive)\n", expectSuccess: true);
        Assert.True(int.Parse(Regex.Match(ok, @"^DEFAULT=(\d+)", RegexOptions.Multiline).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) >= 7968);
        Assert.True(int.Parse(Regex.Match(ok, @"^EXHAUSTIVE=(\d+)", RegexOptions.Multiline).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) >= 76);

        var failed = RunPowerShell("$ErrorActionPreference='Stop'; $baselineFile = '" + file + ".missing'\n" + function + "\nRead-TestBaseline\n", expectSuccess: false);
        Assert.Contains("baseline file is missing", failed, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------- 4: frame_over_50 in the summary

    [Theory]
    [InlineData("yes", 0, "yes", "counted")]
    [InlineData("no", 0, "no", "counted")]
    [InlineData("yes", 1, "yes", "contended")]
    [InlineData("none", 2, "n/a", "contended")]
    public void A_legs_flag_and_judge_reach_the_summary_fields(string flag, int others, string over50, string judge)
    {
        var smoke = Smoke;
        var text = flag switch
        {
            "yes" => "[logincheck] bsd-fight done ok=true kills=1 cast_frame_max_ms=61.2 fight_frame_max_ms=33.0 frame_over_50=yes",
            "no" => "[logincheck] bsd-fight done ok=true kills=1 cast_frame_max_ms=21.4 fight_frame_max_ms=30.0",
            _ => "no verdict line here",
        };
        var script = Function(smoke, "Get-FrameFlags") + "\n" + Function(smoke, "Get-FrameFields") + "\n"
            + "$t = @'\n" + text + "\n'@\n$f = Get-FrameFlags $t 'bsd-fight'\n$r = Get-FrameFields $f @{ Others = " + others
            + " }\nWrite-Output (\"OVER=\" + $r.Over50)\nWrite-Output (\"JUDGE=\" + $r.Judge)\n";
        var run = RunPowerShell(script, expectSuccess: true);

        Assert.Equal(over50, Regex.Match(run, @"^OVER=(.*?)\r?$", RegexOptions.Multiline).Groups[1].Value);
        Assert.Equal(judge, Regex.Match(run, @"^JUDGE=(.*?)\r?$", RegexOptions.Multiline).Groups[1].Value);
    }

    [Fact]
    public void Every_judged_live_leg_passes_its_frame_fields_to_Add_Result_and_the_md_prints_them_report_only()
    {
        var script = Smoke;

        Assert.Equal(6, Regex.Matches(script, @"-FrameOver50 \$fields\.Over50 -FrameJudge \$fields\.Judge").Count); // login-check, login, buff, bsd, death, hunt
        Assert.Contains("frame_over_50 (report-only): ", script, StringComparison.Ordinal);
        Assert.Contains("[string] $FrameOver50 = '', [string] $FrameJudge = ''", script, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------- 5: the contended rule

    [Fact]
    public void The_live_legs_contended_rule_is_settled_and_its_todo_name_is_closed()
    {
        var script = Smoke;

        Assert.DoesNotContain("TODO(p5a-live-frame-contended)", script, StringComparison.Ordinal);
        Assert.Contains("Settled (M4 1.1 s3, closes p5a-live-frame-contended)", script, StringComparison.Ordinal);
        // -Backend require implies solo: the rule lives in Get-FrameRule, and the help says what a leg reports otherwise.
        Assert.Contains("$implied = $Backend -eq 'require'", script, StringComparison.Ordinal);
        Assert.Contains("frame judge counts only when no other engine process is up", script, StringComparison.Ordinal);
        Assert.Contains("reports frames=contended (the verdict stands, the frame judge is not counted)", script, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------- 6: the shader cache under -Parallel

    [Fact]
    public void Each_parallel_run_has_its_own_user_data_slot_and_so_its_own_shader_cache()
    {
        var smoke = Smoke;
        var godot = Normal(File.ReadAllText(RepositoryFiles.PathOf("tools", "godot.ps1")));

        // The batch hands every child a slot no running child holds (p1, p2, p3), the child forwards it, godot.ps1 moves APPDATA and LOCALAPPDATA there: Godot's shader cache lives under
        // %APPDATA%\Godot\app_userdata\<project>\shader_cache, so two parallel runs never share one (measured, handoff: a cache of its own in each slot).
        Assert.Contains("$slot = @('p1', 'p2', 'p3') | Where-Object { $used -notcontains $_ } | Select-Object -First 1", smoke, StringComparison.Ordinal);
        Assert.Contains("'-UserDataSlot', $Slot", smoke, StringComparison.Ordinal);
        Assert.Contains("if ($UserDataSlot) { $arguments += @('-UserDataSlot', $UserDataSlot) }", smoke, StringComparison.Ordinal);
        Assert.Contains("$dataRoot = if ($UserDataSlot) { Join-Path $repoRoot \"cache\\godot\\slot-$UserDataSlot\" } else { Join-Path $repoRoot 'cache\\godot' }", godot, StringComparison.Ordinal);
        Assert.Contains("$godotEnv['APPDATA'] = $roaming", godot, StringComparison.Ordinal);
        Assert.Contains("$godotEnv['LOCALAPPDATA'] = $local", godot, StringComparison.Ordinal);
        Assert.Contains("-IsolateUserData", smoke[smoke.IndexOf("function Invoke-Godot", StringComparison.Ordinal)..], StringComparison.Ordinal);
        // No parallel step is frame-judged, so a slot's cold cache never touches a judged run.
        var parallel = Regex.Match(smoke, @"\$parallelSteps = @\(([^)]*)\)").Groups[1].Value;
        Assert.NotEmpty(parallel);
        foreach (var judged in new[] { "play-checks", "record-audio", "login-check", "login", "login-buff", "login-bsd", "login-death", "login-hunt" })
        {
            Assert.DoesNotContain($"'{judged}'", parallel, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------------------------------- 8: the traits

    [Fact]
    public void Both_filters_leave_out_the_timing_tier_and_the_known_failure_status_and_the_docs_say_who_runs_them()
    {
        Assert.Contains("$testFilter = '" + Filter + "'", Smoke, StringComparison.Ordinal);
        Assert.Contains("$testFilter = '" + Filter + "'", SliceCheck, StringComparison.Ordinal);
        Assert.Contains("--filter \"" + Filter + "\"", SliceCheck, StringComparison.Ordinal); // the help text at the top
        Assert.Contains("--filter \"" + Filter + "\"", Smoke, StringComparison.Ordinal);

        var readme = Normal(File.ReadAllText(RepositoryFiles.PathOf("README.md")));
        Assert.Contains(Filter, readme, StringComparison.Ordinal);
        Assert.Contains("[Trait(\"Tier\",\"Timing\")]", readme, StringComparison.Ordinal);
        Assert.Contains("[Trait(\"Status\",\"KnownFailure\")]", readme, StringComparison.Ordinal);
        var shared = Normal(File.ReadAllText(RepositoryFiles.PathOf("tests", "Shared", "README.md")));
        Assert.Contains("`[Trait(\"Tier\", \"Timing\")]`", shared, StringComparison.Ordinal);
        Assert.Contains("the lead runs it alone", shared, StringComparison.Ordinal);
        Assert.Contains("`[Trait(\"Status\", \"KnownFailure\")]`", shared, StringComparison.Ordinal);
        Assert.Contains("TODO(...)", shared, StringComparison.Ordinal);
    }

    [Fact]
    public void The_cuts_keep_the_pins_the_live_legs_depend_on()
    {
        var sliceCheck = SliceCheck;

        // q5 section 3: slice-check never names the live legs as steps and keeps its single-value backend switch.
        Assert.DoesNotContain("'login-buff'", sliceCheck, StringComparison.Ordinal);
        Assert.DoesNotContain("'login-death'", sliceCheck, StringComparison.Ordinal);
        Assert.DoesNotContain("'login-bsd'", sliceCheck, StringComparison.Ordinal);
        Assert.DoesNotContain("login-hunt", sliceCheck, StringComparison.Ordinal); // M4 1.2 s5: as login-bsd, it runs only under smoke -Backend require
        Assert.Contains("[ValidateSet('auto')]", sliceCheck, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------- helpers

    private static string Function(string script, string name)
    {
        var start = script.IndexOf("function " + name, StringComparison.Ordinal);
        Assert.True(start >= 0, "tools script has no function " + name);
        var end = Regex.Match(script[start..], @"\r?\n\}\r?\n");
        Assert.True(end.Success, "no end of function " + name);
        return script[start..(start + end.Index + end.Length)];
    }

    private static string RunPowerShell(string command, bool expectSuccess)
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
                var errorTask = process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEnd();
                Assert.True(process.WaitForExit(60_000), "PowerShell did not finish");
                var error = errorTask.Result;
                Assert.True((process.ExitCode == 0) == expectSuccess, (expectSuccess ? "PowerShell failed: " : "PowerShell should have failed: ") + error + output);
                return output + error;
            }
            catch (Win32Exception)
            {
                // that shell is not installed: try the next
            }
        }

        throw new InvalidOperationException("neither pwsh nor powershell is installed");
    }
}
