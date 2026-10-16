using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Lifestone.Client.Audio;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The sound judges and the wiring of M3 2.1 s3 (the buff leg's neighbours): the walking legs' <c>sounds_hooks&gt;=1</c> judge (<see cref="LoginCheckScenario.JudgeWalkingSounds"/>; D27 keeps the Academy arrival's
/// <c>sounds_server</c> a report), the smoke <c>record-audio</c> step's judge of the sounded counts in <c>sources=</c> (1.3 review R7: a recording of portal sounds alone fails; the PowerShell function is run for
/// real), <c>--login-check=buff</c>'s parse and its mid-cast frame routing, and the smoke step list (<c>login-buff</c> runs only under <c>-Backend require</c>, so <c>-Backend auto</c> skips it).
/// </summary>
public sealed class BuffLegJudgesTests
{
    private static string Smoke => File.ReadAllText(RepositoryFiles.PathOf("tools", "smoke.ps1"));

    // ---------------------------------------------------------------------------------------------- the walking legs' judge

    [Theory]
    [InlineData(1, 1)]
    [InlineData(37, 2)]
    [InlineData(1, 3)]
    public void Walking_legs_that_sounded_a_hook_hold(int hooks, int legs)
    {
        Assert.Null(LoginCheckScenario.JudgeWalkingSounds(hooks, legs));
    }

    [Fact]
    public void Walking_legs_with_no_hook_sound_fail_and_say_which_count()
    {
        var zero = LoginCheckScenario.JudgeWalkingSounds(0, 2);
        var absent = LoginCheckScenario.JudgeWalkingSounds(null, 2);

        Assert.NotNull(zero);
        Assert.Contains("sounds_hooks=0", zero, StringComparison.Ordinal);
        Assert.NotNull(absent);
        Assert.Contains("sounds_hooks=n/a", absent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(null)]
    public void A_check_with_no_walking_leg_judges_nothing_here(int? hooks)
    {
        // The enter-only check has no leg: its arrival's sounds_server stays a report (D27), and its hooks are not judged by this rule.
        Assert.Null(LoginCheckScenario.JudgeWalkingSounds(hooks, 0));
    }

    [Fact]
    public void The_runner_judges_each_walking_legs_own_tally_and_the_arrival_server_sound_stays_a_report()
    {
        var runner = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "LoginCheckRunner.cs"));

        var at = runner.IndexOf("private void JudgeWalkingSounds", StringComparison.Ordinal);
        Assert.True(at > 0, "LoginCheckRunner has no JudgeWalkingSounds");
        var body = runner[at..runner.IndexOf("/// <summary>What only the engine sees", at, StringComparison.Ordinal)];
        Assert.Contains("_soundLegs.Of(leg.Name).Hooks", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Server", body, StringComparison.Ordinal);
        Assert.Contains("JudgeWalkingSounds(problems);", runner, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------- record-audio's sounded counts

    [Fact]
    public void The_recording_judge_passes_a_run_that_sounded_server_and_hook_sounds()
    {
        var result = JudgeSources("server:1,hooks:199,footsteps:3,footsteps_sounded:0,ui:0,ambient:n/a,server_handed:1,hooks_handed:203");

        Assert.True(result.Ok, result.Detail);
        Assert.Equal(string.Empty, result.Detail);
    }

    [Fact]
    public void The_recording_judge_fails_a_recording_of_portal_sounds_alone_and_names_the_count()
    {
        var portalOnly = JudgeSources("server:0,hooks:0,footsteps:0,footsteps_sounded:0,ui:0,ambient:n/a,server_handed:0,hooks_handed:0");
        var noHooks = JudgeSources("server:2,hooks:0,footsteps:0,footsteps_sounded:0,ui:1,ambient:n/a,server_handed:2,hooks_handed:5");
        var noServer = JudgeSources("server:0,hooks:40,footsteps:3,footsteps_sounded:0,ui:0,ambient:n/a,server_handed:1,hooks_handed:40");

        Assert.False(portalOnly.Ok);
        Assert.Contains("server=0", portalOnly.Detail, StringComparison.Ordinal);
        Assert.Contains("hooks=0", portalOnly.Detail, StringComparison.Ordinal);
        Assert.False(noHooks.Ok);
        Assert.Equal("hooks=0 (>= 1 expected)", noHooks.Detail);
        Assert.False(noServer.Ok);
        Assert.Equal("server=0 (>= 1 expected)", noServer.Detail);
    }

    [Theory]
    [InlineData("server:n/a,hooks:12", "server=n/a")]
    [InlineData("hooks:12", "server is missing")]
    [InlineData("server:3,hooks:n/a,ui:0", "hooks=n/a")]
    public void The_recording_judge_fails_a_counter_the_run_does_not_have(string sources, string said)
    {
        var result = JudgeSources(sources);

        Assert.False(result.Ok);
        Assert.Contains(said, result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_recording_judge_reads_what_the_game_prints()
    {
        // The game's own describe line, with the server's and the hooks' sounded counts at 1 and 2 (footsteps, the UI and the ambient draws are not judged).
        var sources = new SoundSourceCounts(1, 2, 0, 0, null, 1, 2, 0).Describe();

        Assert.True(JudgeSources(sources).Ok);
        Assert.False(JudgeSources(new SoundSourceCounts(0, 2, 0, 0, null).Describe()).Ok);
    }

    [Fact]
    public void The_record_audio_step_judges_the_sounded_counts_and_the_step_detail_says_so()
    {
        var script = Smoke;
        var step = script[script.IndexOf("'record-audio' {", StringComparison.Ordinal)..];
        step = step[..step.IndexOf("'shell-frames' {", StringComparison.Ordinal)];

        Assert.Contains("Test-RecordedSources $levels.sources", step, StringComparison.Ordinal);
        Assert.Contains("-and $sourcesOk", step, StringComparison.Ordinal);
        Assert.Contains("FAIL: sounded counts:", step, StringComparison.Ordinal);
        Assert.DoesNotContain("(sources report-only)", step, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------- --login-check=buff

    [Fact]
    public void The_buff_kind_parses_and_is_not_part_of_all()
    {
        Assert.Equal(LoginCheckKind.Buff, PlayOptions.Parse(["--login-check=buff"]).LoginCheck);
        Assert.Equal(LoginCheckKind.All, PlayOptions.Parse(["--login-check"]).LoginCheck);
        Assert.Equal(LoginCheckKind.All, PlayOptions.Parse(["--login-check=all"]).LoginCheck);
        Assert.NotEqual(LoginCheckKind.Buff, PlayOptions.Parse(["--login-check=all"]).LoginCheck);

        // Hooked by one line ahead of the legs' construction, as the throwaway leg is; 'all' never reaches it.
        var runner = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "LoginCheckRunner.cs"));
        var hook = runner.IndexOf("BuffLegRunner.Install(", StringComparison.Ordinal);
        Assert.True(hook > 0, "LoginCheckRunner does not install the buff leg");
        Assert.True(hook < runner.IndexOf("var legs = new List<RoutePlay>();", StringComparison.Ordinal));
        Assert.Contains("kind == LoginCheckKind.Buff", runner[(hook - 120)..hook], StringComparison.Ordinal);
    }

    [Fact]
    public void The_mid_cast_frame_is_the_buff_legs_when_no_screenshot_delay_is_given()
    {
        var mid = PlayOptions.Parse(["--login-check=buff", "--play-screenshot=cache/m3/2-1/buff-cast.png"]);
        var late = PlayOptions.Parse(["--login-check=buff", "--play-screenshot=cache/m3/2-1/buff-cast.png", "--play-screenshot-after=12"]);
        var other = PlayOptions.Parse(["--login-check=enter", "--play-screenshot=cache/x.png"]);
        var order = PlayOptions.Parse(["--play-screenshot=cache/m3/2-1/buff-cast.png", "--login-check=buff"]);

        Assert.Equal("cache/m3/2-1/buff-cast.png", mid.BuffShotPath);
        Assert.Null(mid.ScreenshotPath); // play mode's own capture (a fixed wait, then quit) must not fire
        Assert.Equal("cache/m3/2-1/buff-cast.png", order.BuffShotPath);
        Assert.Null(late.BuffShotPath); // an explicit delay keeps play mode's own frame
        Assert.Equal("cache/m3/2-1/buff-cast.png", late.ScreenshotPath);
        Assert.Equal(12, late.ScreenshotAfterSeconds);
        Assert.Null(other.BuffShotPath);
        Assert.Equal("cache/x.png", other.ScreenshotPath);
    }

    // ---------------------------------------------------------------------------------------------- the smoke step list

    [Fact]
    public void The_smoke_step_list_has_login_buff_as_a_backend_step_and_skips_it_unless_the_backend_is_required()
    {
        var script = Smoke;
        var steps = Regex.Match(script, @"\$allSteps = @\(([^)]*)\)", RegexOptions.Singleline).Groups[1].Value;
        var backend = Regex.Match(script, @"\$backendSteps = @\(([^)]*)\)").Groups[1].Value;

        Assert.Contains("'login-buff'", steps, StringComparison.Ordinal);
        Assert.Contains("'login-buff'", backend, StringComparison.Ordinal);
        Assert.True(steps.IndexOf("'login-throwaway'", StringComparison.Ordinal) < steps.IndexOf("'login-buff'", StringComparison.Ordinal));
        Assert.DoesNotContain("'login-buff'", steps[..steps.IndexOf("'login-check'", StringComparison.Ordinal)], StringComparison.Ordinal);

        // The skip rule runs before the backend gate: under auto (slice-check's) and start the step is recorded as a SKIP, never run, never a failure.
        var skip = script.IndexOf("if ($step -eq 'login-buff' -and $Backend -ne 'require')", StringComparison.Ordinal);
        var gate = script.IndexOf("if ($backendSteps -contains $step -and -not (Test-BackendGate", StringComparison.Ordinal);
        Assert.True(skip > 0 && skip < gate, "the login-buff skip rule must come before the backend gate");
        var rule = script[skip..gate];
        Assert.Contains("-Skipped", rule, StringComparison.Ordinal);
        Assert.Contains("continue", rule, StringComparison.Ordinal);

        // The step: the buff leg's command, the leg's own time for a whole cycle, and a PASS that needs ok=true.
        var body = script[script.IndexOf("'login-buff' {", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("'login' {", StringComparison.Ordinal)];
        Assert.Contains("--account=test --login-check=buff", body, StringComparison.Ordinal);
        Assert.Contains("' 900 ", body, StringComparison.Ordinal);
        Assert.Contains("buff done ok=true", body, StringComparison.Ordinal);
        Assert.Contains("$verdict", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Slice_check_never_runs_the_buff_step()
    {
        var sliceCheck = File.ReadAllText(RepositoryFiles.PathOf("tools", "slice-check.ps1"));

        // slice-check only ever passes -Backend auto (its parameter accepts nothing else), and smoke skips login-buff under auto.
        Assert.Contains("[ValidateSet('auto')]", sliceCheck, StringComparison.Ordinal);
        Assert.DoesNotContain("login-buff", sliceCheck, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------- running the smoke script's judge

    /// <summary>Runs <c>Test-RecordedSources</c> (the function itself, cut out of tools/smoke.ps1) in PowerShell on <paramref name="sources"/>.</summary>
    private static (bool Ok, string Detail) JudgeSources(string sources)
    {
        var script = Smoke;
        var start = script.IndexOf("function Test-RecordedSources", StringComparison.Ordinal);
        Assert.True(start >= 0, "tools/smoke.ps1 has no Test-RecordedSources");
        var end = script.IndexOf("\n}\n", start, StringComparison.Ordinal);
        var function = script[start..(end + 3)];
        var command = function + "\n$r = Test-RecordedSources '" + sources + "'\nWrite-Output (\"OK=\" + $r.Ok)\nWrite-Output (\"DETAIL=\" + $r.Detail)\n";
        var run = RunPowerShell(command);
        var ok = Regex.Match(run, @"^OK=(True|False)\s*$", RegexOptions.Multiline);
        var detail = Regex.Match(run, @"^DETAIL=(.*?)\r?$", RegexOptions.Multiline);
        Assert.True(ok.Success && detail.Success, "the judge printed no verdict:\n" + run);
        return (ok.Groups[1].Value == "True", detail.Groups[1].Value);
    }

    private static string RunPowerShell(string command)
    {
        foreach (var shell in new[] { "pwsh", "powershell" })
        {
            var info = new ProcessStartInfo(shell)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
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

        throw new InvalidOperationException("neither pwsh nor powershell is available to run the smoke script's judge");
    }
}
