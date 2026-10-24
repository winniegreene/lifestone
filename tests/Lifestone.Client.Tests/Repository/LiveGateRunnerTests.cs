using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// M4 P4 gate (D31): the parallel live gate's runner and its script-side parts, without a backend, a game or a login. tools/live-gate.ps1 is run with -Plan (it prints the steps, their lanes and their command
/// lines and runs nothing); tools/smoke.ps1's step table, readiness probe, frame rule and audio check, and tools/lib/Play.psm1's -Audio refusal are read as text and their pure functions run in PowerShell
/// (cut out of the script, as SliceCheckAndSmokeTests does).
/// </summary>
public sealed class LiveGateRunnerTests
{
    private static string Smoke => File.ReadAllText(RepositoryFiles.PathOf("tools", "smoke.ps1"));

    private static string Runner => File.ReadAllText(RepositoryFiles.PathOf("tools", "live-gate.ps1"));

    private static readonly Dictionary<string, string[]> PlanCache = [];

    // ------------------------------------------------------------------------------------------ the runner's plan (its dry run)

    [Fact]
    public void The_parallel_plan_has_three_account_lanes_then_a_solo_phase_in_the_briefs_order()
    {
        var plan = Plan("-Loopback", "full");

        Assert.Contains(plan, l => l.StartsWith("[gate-plan] mode=parallel loopback=full classes=all soak=False", StringComparison.Ordinal));
        Assert.Contains("[gate-plan] phase PARALLEL (3 lanes at once (at most three Godot), each lane in order)", plan);
        Assert.Contains("[gate-plan] phase SOLO (one at a time, nothing else running)", plan);
        Assert.Contains(plan, l => l.Contains("backend: server\\start-server.ps1 from the main checkout", StringComparison.Ordinal) && l.Contains("stop-server.ps1 in a finally", StringComparison.Ordinal));
        Assert.Contains(plan, l => l.StartsWith("[gate-plan] done steps=", StringComparison.Ordinal) && l.EndsWith("(nothing was run)", StringComparison.Ordinal));

        // The parallel phase: lanes in the order test, test-melee, test-archer; each lane's loopback first, then its smoke steps.
        var parallel = Steps(plan, "PARALLEL");
        Assert.Equal(["test:loopback", "test:smoke", "test-melee:loopback", "test-archer:loopback", "test-archer:smoke"], parallel.Select(s => s.Lane + ":" + s.Name).ToArray());
        Assert.Contains("--filter \"Category=Loopback&Tier!=Soak&Account=test&Lane!=solo\"", parallel[0].Command, StringComparison.Ordinal);
        Assert.Contains("--filter \"Category=Loopback&Tier!=Soak&Account=test-melee&Lane!=solo\"", parallel[2].Command, StringComparison.Ordinal);
        Assert.Contains("--filter \"Category=Loopback&Tier!=Soak&Account=test-archer&Lane!=solo\"", parallel[3].Command, StringComparison.Ordinal);
        Assert.All(parallel.Where(s => s.Name == "loopback"), s => Assert.Contains("dotnet test tests\\Lifestone.Loopback.Tests --disable-build-servers --no-build", s.Command, StringComparison.Ordinal));
        Assert.Contains("-Only login-buff,login-death -Backend require -NoBuild", parallel[1].Command, StringComparison.Ordinal);
        Assert.Contains("-Only login-hunt-missile -Backend require -NoBuild", parallel[4].Command, StringComparison.Ordinal);

        // Frame-time fields are reported in the parallel phase (-ContendedFrames), and each lane's Godot runs have a user-data slot of their own.
        var smokes = parallel.Where(s => s.Name == "smoke").ToList();
        Assert.All(smokes, s => Assert.Contains("-ContendedFrames -UserDataSlot g", s.Command, StringComparison.Ordinal));
        Assert.Equal(2, smokes.Select(s => Regex.Match(s.Command, @"-UserDataSlot (g\d)").Groups[1].Value).Distinct().Count());

        // The solo phase: the frame judge three runs, one melee hunt, the movement-precision loopback tests, the throwaway's frames three runs, the leak scan; no -ContendedFrames anywhere.
        var solo = Steps(plan, "SOLO");
        Assert.Equal(["frame-judge-1", "frame-judge-2", "frame-judge-3", "melee-hunt", "login-bsd", "movement-loopback", "throwaway-frames-1", "throwaway-frames-2", "throwaway-frames-3", "leak-scan"], solo.Select(s => s.Name).ToArray());
        Assert.All(solo.Take(3), s => Assert.Contains("-Only login-check,login -Backend require", s.Command, StringComparison.Ordinal));
        Assert.Contains("-Only login-hunt -Backend require", solo[3].Command, StringComparison.Ordinal);
        Assert.Contains("-Only login-bsd -Backend require", solo[4].Command, StringComparison.Ordinal); // rfix-c: the den fight's walk is solo
        Assert.Contains("--filter \"Category=Loopback&Tier!=Soak&Lane=solo\"", solo[5].Command, StringComparison.Ordinal);
        Assert.All(solo.Skip(6).Take(3), s => Assert.Contains("-Only login-throwaway -Backend require", s.Command, StringComparison.Ordinal));
        Assert.All(solo, s => Assert.DoesNotContain("-ContendedFrames", s.Command, StringComparison.Ordinal));
    }

    [Fact]
    public void The_plan_never_repeats_the_offline_steps_or_a_second_hunt_or_a_full_smoke()
    {
        var all = string.Join("\n", Plan("-Loopback", "full"));

        // The smoke steps are named with -Only, so no offline step (G-A's) is in any command; the full smoke is not in the parallel mode.
        Assert.DoesNotContain("-Parallel", all, StringComparison.Ordinal);
        Assert.DoesNotContain("-SoloFrames", all, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(all, @"-Only login-hunt -Backend"));
        Assert.Single(Regex.Matches(all, @"-Only login-hunt-missile -Backend"));
        Assert.Equal(3, Regex.Matches(all, @"-Only login-check,login ").Count); // the frame judge's three runs, no fourth in a full smoke
        Assert.Single(Regex.Matches(all, @"phase SETUP")); // one build before the lanes: three --no-build processes must not race a build
    }

    [Fact]
    public void Serial_is_the_old_order_exactly()
    {
        var plan = Plan("-Serial", "-Loopback", "full");
        var steps = Steps(plan, "SERIAL");

        Assert.Equal(["loopback", "frame-judge-1", "frame-judge-2", "frame-judge-3", "full-smoke"], steps.Select(s => s.Name).ToArray());
        Assert.Contains("dotnet test tests\\Lifestone.Loopback.Tests --disable-build-servers --filter \"Category=Loopback&Tier!=Soak\"", steps[0].Command, StringComparison.Ordinal);
        Assert.DoesNotContain("--no-build", steps[0].Command, StringComparison.Ordinal);
        Assert.All(steps.Skip(1).Take(3), s => Assert.Contains("-Only login-check,login -Backend require", s.Command, StringComparison.Ordinal));
        Assert.Contains("smoke.ps1 -Backend require -SoloFrames -FullScan -ScanDir ", steps[4].Command, StringComparison.Ordinal);
        Assert.Contains("-Parallel 2 ", steps[4].Command, StringComparison.Ordinal);
        Assert.DoesNotContain(plan, l => l.Contains("phase PARALLEL", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, l => l.Contains("phase SETUP", StringComparison.Ordinal));

        // -SkipOffline leaves the Godot-only steps and the test step out of the full smoke (the build stays); the old order without it has no -Skip.
        Assert.DoesNotContain("-Skip", steps[4].Command, StringComparison.Ordinal);
        var skipped = Steps(Plan("-Serial", "-SkipOffline", "-Loopback", "full"), "SERIAL")[4].Command;
        Assert.Contains("-Skip test,cli-help,cli-bad-argument,", skipped, StringComparison.Ordinal);
        Assert.DoesNotContain("login-", skipped[skipped.IndexOf("-Skip", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    [Fact]
    public void Soak_adds_the_soak_tier_once_alone_at_the_end()
    {
        foreach (var args in new[] { new[] { "-Soak", "-Loopback", "full" }, ["-Serial", "-Soak", "-Loopback", "full"] })
        {
            var plan = Plan(args);
            var soak = plan.Where(l => l.Contains("Tier=Soak", StringComparison.Ordinal)).ToList();

            Assert.Single(soak);
            Assert.Contains("--filter \"Category=Loopback&Tier=Soak\"", soak[0], StringComparison.Ordinal);
            Assert.Contains("SOAK lane=soak", soak[0], StringComparison.Ordinal);
            var last = Array.FindLastIndex(plan, l => l.StartsWith("[gate-plan] SOLO", StringComparison.Ordinal) || l.StartsWith("[gate-plan] SERIAL", StringComparison.Ordinal));
            Assert.True(Array.IndexOf(plan, soak[0]) > last, "the soak tier comes after every other step");
        }

        Assert.DoesNotContain(Plan("-Loopback", "full"), l => l.Contains("Tier=Soak", StringComparison.Ordinal));
    }

    [Fact]
    public void Loopback_core_runs_the_fixed_list_plus_the_classes_of_the_areas_the_diff_touches()
    {
        // A diff with no loopback area: the fixed list only, each class in the lane of its account (the solo class in the solo phase).
        var plain = Plan("-Loopback", "core", "-ChangedPaths", "docs/readme.md");
        var parallel = Steps(plain, "PARALLEL");
        Assert.Contains(plain, l => l.Contains("classes=HeadlessLoopbackTests,HoltburgLapsLoopbackTests,InventoryLiveTests,LoopbackGateTests,MeleeLiveTests,MissileLiveTests ", StringComparison.Ordinal));
        Assert.Contains("(FullyQualifiedName~.HeadlessLoopbackTests.|FullyQualifiedName~.InventoryLiveTests.|FullyQualifiedName~.LoopbackGateTests.)", parallel[0].Command, StringComparison.Ordinal);
        Assert.Contains("Account=test-melee&Lane!=solo&(FullyQualifiedName~.MeleeLiveTests.)", parallel.Single(s => s.Lane == "test-melee").Command, StringComparison.Ordinal);
        Assert.Contains("Account=test-archer&Lane!=solo&(FullyQualifiedName~.MissileLiveTests.)", parallel.First(s => s.Lane == "test-archer").Command, StringComparison.Ordinal);
        Assert.Contains("Lane=solo&(FullyQualifiedName~.HoltburgLapsLoopbackTests.)", Steps(plain, "SOLO").Single(s => s.Name == "movement-loopback").Command, StringComparison.Ordinal);

        // The fighter folder adds the fighter area's classes (the Brawler's and the Archer's lanes grow); the mage area's classes stay out.
        var fighter = string.Join("\n", Plan("-Loopback", "core", "-ChangedPaths", "src/Lifestone.Client/Session/Fighter/HuntGround.cs"));
        Assert.Contains("loopback: area fighter touched by src/Lifestone.Client/Session/Fighter/HuntGround.cs", fighter, StringComparison.Ordinal);
        Assert.Contains("FullyQualifiedName~.HuntLiveTests.", fighter, StringComparison.Ordinal);
        Assert.Contains("FullyQualifiedName~.FighterArcherBuildLoopbackTests.", fighter, StringComparison.Ordinal);
        Assert.DoesNotContain("BuffLoopLiveTests", fighter, StringComparison.Ordinal);

        // A changed loopback test file selects the classes it declares.
        var file = string.Join("\n", Plan("-Loopback", "core", "-ChangedPaths", "tests/Lifestone.Loopback.Tests/Session/VendorLiveTests.cs"));
        Assert.Contains("class VendorLiveTests changed", file, StringComparison.Ordinal);

        // A shared file runs everything and says so (the fail-safe, as StepPaths.psd1's Everything).
        var shared = Plan("-Loopback", "core", "-ChangedPaths", "src/Lifestone.Client/Session/GameSession.cs");
        Assert.Contains(shared, l => l.Contains("a shared file changed", StringComparison.Ordinal) && l.Contains("every loopback class runs", StringComparison.Ordinal));
        Assert.Contains(shared, l => l.Contains("loopback=core classes=all", StringComparison.Ordinal));
        Assert.DoesNotContain(Steps(shared, "PARALLEL"), s => s.Command.Contains("FullyQualifiedName", StringComparison.Ordinal));
    }

    [Fact]
    public void Loopback_areas_run_those_areas_only_and_an_unknown_value_is_refused_loudly()
    {
        var mage = Plan("-Loopback", "mage");
        Assert.Contains(mage, l => l.Contains("loopback=areas classes=BuffLoopLiveTests,CastLiveTests,DeathLiveTests,FightLiveTests,GearLiveTests,HealLiveTests,MageBuildLoopbackTests,RebuffLiveTests ", StringComparison.Ordinal));
        var lanes = Steps(mage, "PARALLEL").Select(s => s.Lane + ":" + s.Name).ToArray();
        Assert.Contains("test:loopback", lanes);
        Assert.DoesNotContain("test-melee:loopback", lanes); // no mage class lives on the Brawler's account: the lane has no loopback step
        Assert.DoesNotContain("test-archer:loopback", lanes);

        var bad = RunRunner(["-Plan", "-Loopback", "bogus"], expectSuccess: false);
        Assert.Contains("unknown -Loopback value 'bogus'", bad, StringComparison.Ordinal);
        Assert.Contains("actions, fighter, mage, movement, net, servermod", bad, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatIf_prints_the_same_plan_and_runs_nothing()
    {
        var plan = RunRunner(["-WhatIf", "-Loopback", "full"], expectSuccess: true).Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("[gate-plan]", StringComparison.Ordinal)).ToArray();

        Assert.Equal(Plan("-Loopback", "full"), plan);
    }

    [Fact]
    public void The_three_run_rule_fails_two_flagged_runs_or_any_other_failure_and_passes_one_flagged_run()
    {
        var function = Function(Runner, "Get-GroupVerdict");
        string Run(string runs) => RunPowerShell(function + $"$v = Get-GroupVerdict @({runs})\nWrite-Output (\"OK=\" + $v.Ok)\nWrite-Output (\"DETAIL=\" + $v.Detail)");
        const string Clean = "@{ Ok = $true; FrameOnly = $false; Over75 = $false }";
        const string Frame = "@{ Ok = $false; FrameOnly = $true; Over75 = $false }";
        const string Other = "@{ Ok = $false; FrameOnly = $false; Over75 = $false }";
        const string Over75 = "@{ Ok = $false; FrameOnly = $true; Over75 = $true }";

        Assert.Contains("OK=True", Run($"{Clean}, {Clean}, {Clean}"), StringComparison.Ordinal);
        var one = Run($"{Clean}, {Frame}, {Clean}");
        Assert.Contains("OK=True", one, StringComparison.Ordinal);
        Assert.Contains("one run over 50 ms: flagged", one, StringComparison.Ordinal);
        var two = Run($"{Frame}, {Clean}, {Frame}");
        Assert.Contains("OK=False", two, StringComparison.Ordinal);
        Assert.Contains("two of three fail the gate", two, StringComparison.Ordinal);
        Assert.Contains("OK=False", Run($"{Clean}, {Other}, {Clean}"), StringComparison.Ordinal); // a failure that is not a frame window fails at once
        Assert.Contains("over 75 ms", Run($"{Clean}, {Over75}, {Clean}"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_row_is_frame_only_when_every_error_line_is_a_frame_line()
    {
        var function = $"Import-Module '{GateModule}' -Force\n";
        string Run(string detail, string log) => RunPowerShell(function + $"$log = @'\n{log}\n'@\nWrite-Output (\"R=\" + (Test-FrameOnlyText -Detail '{detail}' -LogText $log))");

        Assert.Contains("R=True", Run("", "2026-10-23T01:02:03Z ERROR [logincheck] frame over 50 ms between Enter and walking: 61 ms"), StringComparison.Ordinal);
        Assert.Contains("R=True", Run("", "Z ERROR [logincheck] the hunt leg failed why=attack_frame_max_ms=80.1 (at most 75 expected)"), StringComparison.Ordinal);
        Assert.Contains("R=False", Run("", "Z ERROR [logincheck] frame over 50 ms between Enter and walking\nZ ERROR [net] session failed in InWorld: Silence"), StringComparison.Ordinal);
        Assert.Contains("R=False", Run("", "no error lines at all"), StringComparison.Ordinal);
        Assert.Contains("R=True", Run("teleport figures over 50 ms (frame-only failure)", ""), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------ the loopback classes and GateAreas.psd1

    [Fact]
    public void Every_class_GateAreas_names_is_a_loopback_class_and_every_area_can_be_planned()
    {
        // The runner throws on a name that is not a loopback class (read from the test sources), so planning every area proves the file in step with the tests.
        foreach (var area in new[] { "net", "movement", "actions", "fighter", "mage", "servermod" })
        {
            var plan = RunRunner(["-Plan", "-Loopback", area], expectSuccess: true);
            Assert.Contains("loopback=areas classes=", plan, StringComparison.Ordinal);
        }

        var areas = File.ReadAllText(RepositoryFiles.PathOf("tools", "lib", "GateAreas.psd1"));
        Assert.Contains("Core = @('LoopbackGateTests', 'HeadlessLoopbackTests', 'InventoryLiveTests', 'MeleeLiveTests', 'MissileLiveTests', 'HoltburgLapsLoopbackTests')", areas, StringComparison.Ordinal);
        var map = File.ReadAllText(RepositoryFiles.PathOf("tools", "lib", "StepPaths.psd1"));
        Assert.Contains("'tools/live-gate.ps1'", map, StringComparison.Ordinal); // a change to the runner needs no Godot step in slice-check
    }

    // ------------------------------------------------------------------------------------------ smoke: the step table and the accounts

    [Fact]
    public void Smoke_has_the_missile_hunt_in_every_list_and_each_backend_step_on_its_characters_account()
    {
        var script = Smoke;
        var steps = Regex.Match(script, @"\$allSteps = @\(([^)]*)\)", RegexOptions.Singleline).Groups[1].Value;
        var backend = Regex.Match(script, @"\$backendSteps = @\(([^)]*)\)").Groups[1].Value;
        var judged = Regex.Match(script, @"\$frameJudgedSteps = @\(([^)]*)\)").Groups[1].Value;
        var audio = Regex.Match(script, @"\$audioSteps = @\(([^)]*)\)").Groups[1].Value;

        foreach (var list in new[] { steps, backend, judged, audio })
        {
            Assert.True(list.IndexOf("'login-hunt'", StringComparison.Ordinal) < list.IndexOf("'login-hunt-missile'", StringComparison.Ordinal), "login-hunt-missile follows login-hunt");
        }

        // The account of each backend step: from its character; the fighter templates' accounts for the hunts.
        var table = script[script.IndexOf("$stepAccounts = [ordered]@{", StringComparison.Ordinal)..];
        table = table[..table.IndexOf("\n}\n", StringComparison.Ordinal)];
        var accounts = Regex.Matches(table, @"^\s+'([a-z-]+)'\s+= '([a-z-]+)'", RegexOptions.Multiline).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        Assert.Equal(["login-check", "login", "login-throwaway", "login-buff", "login-bsd", "login-death", "login-hunt", "login-hunt-missile"], accounts.Keys.ToArray());
        Assert.Equal("test-melee", accounts["login-hunt"]);
        Assert.Equal("test-archer", accounts["login-hunt-missile"]);
        Assert.All(accounts.Where(a => !a.Key.StartsWith("login-hunt", StringComparison.Ordinal)), a => Assert.Equal("test", a.Value));
        Assert.Equal(Regex.Matches(backend, "'[a-z-]+'").Select(m => m.Value.Trim('\'')).ToArray(), accounts.Keys.ToArray()); // every backend step has an account, and only those

        // No backend step carries the literal --account=test any more: the account comes from the table (play.ps1's login step is the test account by its -Offscreen default).
        var body = script[script.IndexOf("$allSteps = @(", StringComparison.Ordinal)..];
        Assert.DoesNotContain("--account=test ", body.Replace("--account=test-", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("--hunt-weapon=$($hunt.Weapon)", script, StringComparison.Ordinal);
        Assert.Contains("'login-hunt-missile' = @{ Weapon = 'missile'; Kills = 2 }", script, StringComparison.Ordinal); // proved live: kills=2 seeks=1
        Assert.Contains("--play-screenshot=$frameStem", script, StringComparison.Ordinal); // the hunt's review frames, the arrow frame among them
        Assert.Contains("if ($step -eq 'login-hunt-missile' -and $Backend -ne 'require')", script, StringComparison.Ordinal); // runs only at the lead's gate

        var map = File.ReadAllText(RepositoryFiles.PathOf("tools", "lib", "StepPaths.psd1"));
        Assert.Contains("'login-hunt-missile' = @(", map, StringComparison.Ordinal); // slice-check's advisory line names it when a Session folder changes
    }

    [Fact]
    public void The_readiness_probe_checks_the_key_of_each_account_the_wanted_steps_need()
    {
        var script = Smoke;
        var probe = Function(script, "Test-BackendReady");

        Assert.Contains("param([string[]] $Accounts = @('test'))", probe, StringComparison.Ordinal);
        Assert.Contains("foreach ($account in $Accounts)", Function(script, "Test-SecretsAccounts"), StringComparison.Ordinal);
        Assert.Contains("Test-SecretsAccounts -Path $secretsPath -Accounts $Accounts", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetProperty($account", Function(script, "Test-SecretsAccounts"), StringComparison.Ordinal);
        Assert.DoesNotContain("GetString()", probe, StringComparison.Ordinal); // by key name only: no value is read
        Assert.Contains("Test-BackendReady -Accounts (Get-StepAccounts @($step))", script, StringComparison.Ordinal);
        Assert.Contains("Test-BackendReady -Accounts (Get-StepAccounts $wantedBackendSteps)", script, StringComparison.Ordinal);

        var run = RunPowerShell("$stepAccounts = [ordered]@{ 'login-check' = 'test'; 'login-hunt' = 'test-melee'; 'login-hunt-missile' = 'test-archer' }\n" + Function(script, "Get-StepAccounts")
            + "Write-Output ('A=' + ((Get-StepAccounts @('login-hunt-missile', 'login-check', 'login-hunt', 'cli-help')) -join ','))\nWrite-Output ('B=' + ((Get-StepAccounts @('cli-help')) -join ','))\nWrite-Output ('C=' + ((Get-StepAccounts @('login-check')) -join ','))");
        Assert.Contains("A=test,test-melee,test-archer", run, StringComparison.Ordinal); // the table's order, each account once
        Assert.Contains("B=", run, StringComparison.Ordinal);
        Assert.Contains("C=test", run, StringComparison.Ordinal);
    }

    [Fact]
    public void Lanes_and_contended_frames_make_a_backend_gate_report_frame_times_where_require_alone_judges_them()
    {
        var rule = Function(Smoke, "Get-FrameRule");
        string Run(string args) => RunPowerShell(rule + $"$r = Get-FrameRule {args}\nWrite-Output ('SOLO=' + $r.Solo + ' FULL=' + $r.Full)\nWrite-Output ('TEXT=' + $r.Text)");

        Assert.Contains("SOLO=True FULL=True", Run("-Backend require -Solo $false -Full $false"), StringComparison.Ordinal); // the live gate as before: solo, full scan
        var contended = Run("-Backend require -Solo $false -Full $false -Contended $true");
        Assert.Contains("SOLO=False FULL=True", contended, StringComparison.Ordinal); // the lanes: not solo, still the full scan
        Assert.Contains("frames: contended", contended, StringComparison.Ordinal);
        Assert.Contains("SOLO=False FULL=False", Run("-Backend auto -Solo $false -Full $false"), StringComparison.Ordinal);

        var both = RunPowerShellFails(rule + "Get-FrameRule -Backend require -Solo $true -Full $false -Contended $true");
        Assert.Contains("contradict each other", both, StringComparison.Ordinal);

        var script = Smoke;
        Assert.Contains("[switch] $ContendedFrames", script, StringComparison.Ordinal);
        Assert.Contains("[switch] $Lanes", script, StringComparison.Ordinal);
        Assert.Contains("-Contended ([bool]($ContendedFrames -or $Lanes))", script, StringComparison.Ordinal);
        // -Lanes: one child per account, started together, each with -ContendedFrames and a slot of its own; a lane's rows replay in the table's order; one account runs here, in turn.
        var batch = Function(script, "Invoke-LaneBatch");
        Assert.Contains("if ($groups.Count -lt 2) { return @{} }", batch, StringComparison.Ordinal);
        Assert.Contains("'-ContendedFrames'", batch, StringComparison.Ordinal);
        Assert.Contains("$slots = @('l1', 'l2', 'l3')", batch, StringComparison.Ordinal);
        Assert.Contains("if ($Lanes -and $backendSteps -contains $step)", script, StringComparison.Ordinal);
        var groups = RunPowerShell("$stepAccounts = [ordered]@{ 'login-check' = 'test'; 'login-buff' = 'test'; 'login-hunt' = 'test-melee'; 'login-hunt-missile' = 'test-archer' }\n" + Function(script, "Get-LaneGroups")
            + "$g = Get-LaneGroups @('login-hunt-missile', 'login-buff', 'login-check', 'login-hunt')\nforeach ($k in $g.Keys) { Write-Output ('G=' + $k + ':' + ($g[$k] -join '+')) }");
        Assert.Equal(["G=test:login-check+login-buff", "G=test-melee:login-hunt", "G=test-archer:login-hunt-missile"], groups.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("G=", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public void The_throwaway_leg_reports_its_frame_figures_as_contended_in_a_lane_and_judges_them_solo()
    {
        var body = Smoke[Smoke.IndexOf("'login-throwaway' {", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("'login-buff' {", StringComparison.Ordinal)];

        Assert.Contains("$contended = $legOk -and -not $framesOk -and ($run.Others -gt 0 -or $parallelPhase) -and -not $SoloFrames", body, StringComparison.Ordinal);
        Assert.Contains("($framesOk -or $contended)", body, StringComparison.Ordinal);
        Assert.Contains("frames=contended", body, StringComparison.Ordinal);
        Assert.Contains("gt 50.0", body, StringComparison.Ordinal); // the 50 ms figures are still the ones judged in a solo run
        Assert.Contains("frame-only failure", body, StringComparison.Ordinal); // the runner's three-run rule reads this marker
    }

    // ------------------------------------------------------------------------------------------ the audio check

    [Fact]
    public void A_godot_run_without_a_muted_audio_line_fails_its_step_with_a_named_reason()
    {
        var function = Function(Smoke, "Get-AudioMuteVerdict");
        string Run(string log) => RunPowerShell(function + $"$log = @'\n{log}\n'@\n$v = Get-AudioMuteVerdict -Text $log\nWrite-Output ('OK=' + $v.Ok)\nWrite-Output ('REASON=' + $v.Reason)");

        Assert.Contains("OK=True", Run("2026-10-22T04:53:36.322Z INFO  [audio] audio service installed voices=16 master_db=-80 muted=True device=Default ms=8.46"), StringComparison.Ordinal);
        Assert.Contains("OK=True", Run("INFO [audio] audio service installed (no portal view in this run) voices=16 master_db=-80 muted=true device=Dummy ms=1"), StringComparison.Ordinal);
        var loud = Run("INFO [audio] audio service installed voices=16 master_db=0 muted=False device=Default ms=8");
        Assert.Contains("OK=False", loud, StringComparison.Ordinal);
        Assert.Contains("muted=false", loud, StringComparison.Ordinal);
        var none = Run("INFO [portal] portal view built\nsome other line");
        Assert.Contains("OK=False", none, StringComparison.Ordinal);
        Assert.Contains("has no \"audio service installed ... muted=true\" line", none, StringComparison.Ordinal);
        Assert.Contains("OK=False", Run("audio service installed muted=true\naudio service installed muted=false"), StringComparison.Ordinal); // any unmuted install fails the run

        // Wiring: Invoke-Godot judges every run of a step in $audioSteps, the login step (play.ps1) too, and Add-Result turns the recorded reason into the step's FAIL.
        var script = Smoke;
        Assert.Contains("Set-AudioVerdict $Step (Get-Content -LiteralPath $log -Raw)", Function(script, "Invoke-Godot"), StringComparison.Ordinal);
        Assert.Contains("Set-AudioVerdict $step $text", script, StringComparison.Ordinal);
        Assert.Contains("$script:audioFailures.ContainsKey($Step)", Function(script, "Add-Result"), StringComparison.Ordinal);
        Assert.Contains("FAIL: audio check: $($script:audioFailures[$Step])", Function(script, "Add-Result"), StringComparison.Ordinal);
        var run = RunPowerShell("$script:audioFailures = @{}\n$audioSteps = @('login-hunt')\n" + Function(script, "Get-AudioMuteVerdict") + Function(script, "Set-AudioVerdict")
            + "Set-AudioVerdict 'login-hunt' 'no audio line' | Out-Null\nSet-AudioVerdict 'cli-help' 'no audio line' | Out-Null\nWrite-Output ('N=' + $script:audioFailures.Count + ' HUNT=' + $script:audioFailures.ContainsKey('login-hunt'))");
        Assert.Contains("N=1 HUNT=True", run, StringComparison.Ordinal); // a step outside $audioSteps (--help, the engine checks) is not asked for the line
    }

    [Fact]
    public void The_record_audio_step_is_held_to_the_muted_rule_and_keeps_its_own_muted_judge()
    {
        // The one run that needs sound records the Master bus BEFORE the bus's level (so the recording holds the full mix) and is muted all the same: no exemption is needed, both judges apply and agree.
        var script = Smoke;
        var audio = Regex.Match(script, @"\$audioSteps = @\(([^)]*)\)").Groups[1].Value;
        var step = script[script.IndexOf("'record-audio' {", StringComparison.Ordinal)..];
        step = step[..step.IndexOf("'shell-frames' {", StringComparison.Ordinal)];

        Assert.Contains("'record-audio'", audio, StringComparison.Ordinal);
        Assert.Contains("$muted = $levels.muted -eq ' muted=true'", step, StringComparison.Ordinal);
        Assert.Contains("FAIL: the run was not muted (M2 D8)", step, StringComparison.Ordinal);
        Assert.Contains("the record-audio step is held to it too", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Play_refuses_audio_with_offscreen_loudly_and_allows_it_for_a_run_you_play_yourself()
    {
        var module = RepositoryFiles.PathOf("tools", "lib", "Play.psm1");
        var run = RunPowerShell($"Import-Module '{module}' -Force -DisableNameChecking\n$a = Get-PlayGameOptions -Offscreen -Audio\n$b = Get-PlayGameOptions -Audio\n$c = Get-PlayGameOptions -Offscreen\n"
            + "Write-Output ('A=' + $a.Problems.Count + ' ' + ($a.Problems -join '|'))\nWrite-Output ('B=' + $b.Problems.Count)\nWrite-Output ('C=' + $c.Problems.Count)");

        Assert.Contains("A=1 -Audio cannot be combined with -Offscreen: an automated run is silent (M2 D8", run, StringComparison.Ordinal);
        Assert.Contains("B=0", run, StringComparison.Ordinal);
        Assert.Contains("C=0", run, StringComparison.Ordinal);
        var play = File.ReadAllText(RepositoryFiles.PathOf("tools", "play.ps1"));
        Assert.Equal(2, Regex.Matches(play, @"Get-PlayGameOptions [^\r\n]*-Audio:\$Audio").Count); // both calls pass the switch, so play.ps1 refuses before it looks for the backend
    }

    // ------------------------------------------------------------------------------------------ rfix-a: the review's fixes, executed (P4-R1 to R15)

    private const string StubPrelude = """
        Import-Module '{MOD}' -Force
        $d = Join-Path ([IO.Path]::GetTempPath()) ('gate-rfix-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Force -Path $d | Out-Null
        function New-Stub([string] $Name, [string] $Body, [string] $Kind = 'test', [hashtable] $Env = @{}, [string] $Exe = 'pwsh') {
            $f = Join-Path $d "$Name.ps1"
            Set-Content -LiteralPath $f -Value $Body -Encoding utf8
            [pscustomobject]@{ Phase = 'PARALLEL'; Lane = 'stub'; Name = $Name; Kind = $Kind; Exe = $Exe; Arguments = @('-NoProfile', '-File', $f); OutDir = ''; Group = ''; Run = 0; TimeoutSeconds = 60; Env = $Env }
        }

        """;

    private static string Stub(string body) => StubPrelude.Replace("{MOD}", GateModule, StringComparison.Ordinal) + body;

    [Fact]
    public void A_lane_of_two_steps_runs_both_through_the_real_Start_Job_path_R1()
    {
        var run = RunPowerShell(Stub("""
            $steps = @((New-Stub 'one' "Write-Output 'Failed: 0, Passed: 3, Skipped: 0'"), (New-Stub 'two' "Write-Output 'Failed: 0, Passed: 5, Skipped: 0'"))
            $job = Start-GateLaneJob -Lane 'stub' -Steps $steps -Root $d -LogDir $d -GateLog (Join-Path $d 'gate.log')
            $rows = @(Receive-GateLaneJob -Job $job -Steps $steps)
            Write-Output ('N=' + $rows.Count)
            foreach ($r in $rows) { Write-Output ('ROW=' + $r.Lane + '|' + $r.Step + '|' + $r.Ok + '|' + $r.Detail) }
            Write-Output ('LOG=' + (Test-Path (Join-Path $d 'stub-one.log')) + (Test-Path (Join-Path $d 'stub-two.log')))
            """));

        Assert.Contains("N=2", run, StringComparison.Ordinal);
        Assert.Contains("ROW=stub|one|True|passed=3 failed=0 skipped=0 exit=0", run, StringComparison.Ordinal);
        Assert.Contains("ROW=stub|two|True|passed=5 failed=0 skipped=0 exit=0", run, StringComparison.Ordinal);
        Assert.Contains("LOG=TrueTrue", run, StringComparison.Ordinal);
        Assert.DoesNotContain("(, $laneSteps)", Runner, StringComparison.Ordinal);
    }

    [Fact]
    public void A_lane_that_dies_or_a_step_that_throws_writes_FAIL_rows_and_never_goes_missing_R5()
    {
        var run = RunPowerShell(Stub("""
            $steps = @((New-Stub 'a' "Write-Output 'Failed: 0, Passed: 1, Skipped: 0'"), (New-Stub 'b' "x" 'test' @{} 'no-such-exe-for-the-gate-test'), (New-Stub 'c' "Write-Output 'Failed: 0, Passed: 2, Skipped: 0'"))
            $rows = @(Invoke-GateSteps -LaneSteps $steps -Root $d -LogDir $d -GateLog (Join-Path $d 'gate.log'))
            Write-Output ('THROW=' + (($rows | ForEach-Object { $_.Step + ':' + $_.Ok }) -join ','))
            Write-Output ('WHY=' + ($rows | Where-Object Step -eq 'b').Detail)
            $dead = Start-Job -ScriptBlock { throw 'boom' }
            $got = @(Receive-GateLaneJob -Job $dead -Steps $steps)
            Write-Output ('DEAD=' + (($got | ForEach-Object { $_.Step + ':' + $_.Ok }) -join ','))
            Write-Output ('DEADWHY=' + $got[0].Detail)
            """));

        Assert.Contains("THROW=a:True,b:False,c:True", run, StringComparison.Ordinal); // the third step still ran
        Assert.Contains("WHY=the step threw ", run, StringComparison.Ordinal);
        Assert.Contains("DEAD=a:False,b:False,c:False", run, StringComparison.Ordinal);
        Assert.Contains("DEADWHY=no result: the lane job Failed", run, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dotnet_test_step_names_its_failed_tests_and_its_log_in_the_FAIL_row_and_a_step_env_is_set_and_restored_R10()
    {
        var run = RunPowerShell(Stub("""
            $fail = "Write-Output '  Failed A.B.One [1 s]'; Write-Output '  Failed A.B.Two [1 s]'; Write-Output '  Failed A.B.Three [1 s]'; Write-Output '  Failed A.B.Four [1 s]'; Write-Output '  Failed A.B.Five [1 s]'; Write-Output '  Failed A.B.Six [1 s]'; Write-Output 'Failed: 6, Passed: 2, Skipped: 0'; exit 1"
            $env1 = '$n = if ($env:ZZ_GATE_STUB) { $env:ZZ_GATE_STUB.Length } else { 0 }; Write-Output ("Failed: 0, Passed: $n, Skipped: 0")'
            $steps = @((New-Stub 'bad' $fail), (New-Stub 'env' $env1 'test' @{ ZZ_GATE_STUB = 'per-test' }))
            $rows = @(Invoke-GateSteps -LaneSteps $steps -Root $d -LogDir $d -GateLog (Join-Path $d 'gate.log'))
            Write-Output ('BAD=' + $rows[0].Ok + '|' + $rows[0].Detail)
            Write-Output ('ENV=' + $rows[1].Detail)
            Write-Output ('AFTER=[' + $env:ZZ_GATE_STUB + ']')
            """));

        Assert.Contains("BAD=False|passed=2 failed=6 skipped=0 exit=1 failed tests: A.B.One, A.B.Two, A.B.Three, A.B.Four, A.B.Five (+1 more) log=stub-bad.log", run, StringComparison.Ordinal);
        Assert.Contains("ENV=passed=8 ", run, StringComparison.Ordinal); // the child saw the variable
        Assert.Contains("AFTER=[]", run, StringComparison.Ordinal); // and this process did not keep it

        // Every dotnet test step also keeps a trx beside its log, and the loopback steps are detailed (the [live-session] lines reach the log, R14).
        var module = File.ReadAllText(GateModule);
        Assert.Contains("'--logger', (\"trx;LogFileName={0}-{1}.trx\" -f $s.Lane, $s.Name), '--results-directory', $LogDir", module, StringComparison.Ordinal);
        var plan = string.Join("\n", Plan("-Loopback", "full"));
        Assert.DoesNotContain("verbosity=normal", plan, StringComparison.Ordinal);
        Assert.Contains("--logger console;verbosity=detailed", plan, StringComparison.Ordinal);
    }

    [Fact]
    public void Serial_runs_per_test_sessions_and_says_so_in_the_plan_R10()
    {
        var plan = Plan("-Serial", "-Loopback", "full");
        Assert.Contains(plan, l => l.Contains("session=per-test", StringComparison.Ordinal));
        Assert.All(Steps(plan, "SERIAL"), s => Assert.Contains("(env LIFESTONE_LOOPBACK_SESSION=per-test)", s.Command, StringComparison.Ordinal));
        Assert.Contains(Plan("-Loopback", "full"), l => l.Contains("session=shared", StringComparison.Ordinal));
        Assert.DoesNotContain("LIFESTONE_LOOPBACK_SESSION", string.Join("\n", Steps(Plan("-Loopback", "full"), "PARALLEL").Select(s => s.Command)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_smoke_exit_a_secret_or_a_missing_summary_fails_the_step_even_when_rows_are_clean_R3()
    {
        var run = RunPowerShell($"Import-Module '{GateModule}' -Force\n" + """
            $pass = [pscustomobject]@{ Step = 'leak-scan'; Outcome = 'PASS'; Detail = ''; Seconds = 3; Log = '' }
            $frame = [pscustomobject]@{ Step = 'login'; Outcome = 'FAIL'; Detail = 'x (frame-only failure)'; Seconds = 3; Log = '' }
            function Show($tag, $rows) { Write-Output ($tag + '=' + (($rows | ForEach-Object { $_.Name + ':' + $_.Ok + ':' + $_.FrameOnly }) -join ',')) }
            $clean = [pscustomobject]@{ secrets = 0; scanned = 120; verdict = 'ALL PASS' }
            Show 'CLEAN' (Get-SmokeStepRows -StepName 'leak-scan' -Code 0 -SmokeRows @($pass) -Summary $clean)
            Write-Output ('CLEANDETAIL=' + (Get-SmokeStepRows -StepName 'leak-scan' -Code 0 -SmokeRows @($pass) -Summary $clean)[0].Detail)
            Show 'EXIT' (Get-SmokeStepRows -StepName 'leak-scan' -Code 1 -SmokeRows @($pass) -Summary ([pscustomobject]@{ secrets = 0; scanned = 5; verdict = 'SECRET SCAN FAILED' }))
            Show 'SECRET' (Get-SmokeStepRows -StepName 'leak-scan' -Code 1 -SmokeRows @($pass) -Summary ([pscustomobject]@{ secrets = 2; scanned = 5; verdict = 'SECRET SCAN FAILED' }))
            Show 'NOSUM' (Get-SmokeStepRows -StepName 'leak-scan' -Code 0 -SmokeRows @($pass) -Summary $null)
            Show 'FRAME' (Get-SmokeStepRows -StepName 'frame-judge-1' -Code 1 -SmokeRows @($frame) -Summary $clean)
            Show 'NOROWS' (Get-SmokeStepRows -StepName 'x' -Code 1 -SmokeRows @() -Summary $null)
            """);

        Assert.Contains("CLEAN=leak-scan:True:False", run, StringComparison.Ordinal);
        Assert.Contains("CLEANDETAIL=PASS secrets=0 scanned=120", run, StringComparison.Ordinal);
        Assert.Contains("EXIT=leak-scan:True:False,leak-scan:exit:False:False", run, StringComparison.Ordinal);
        Assert.Contains("SECRET=leak-scan:True:False,leak-scan:secret-scan:False:False", run, StringComparison.Ordinal);
        Assert.Contains("NOSUM=leak-scan:True:False,leak-scan:summary:False:False", run, StringComparison.Ordinal);
        Assert.Contains("FRAME=frame-judge-1:login:False:True", run, StringComparison.Ordinal); // a frame-only failure stays one flagged run for the three-run rule: no extra exit row
        Assert.Contains("NOROWS=x:False:False", run, StringComparison.Ordinal);
    }

    [Fact]
    public void The_leak_scan_reads_the_whole_gate_folder_and_the_log_dir_R4()
    {
        var plan = Steps(Plan("-Loopback", "full"), "SOLO").Single(s => s.Name == "leak-scan").Command;
        Assert.Contains("-ScanDir <worktree>\\cache\\gate\\gate,<main>\\cache\\gate\\gate", plan, StringComparison.Ordinal);
        Assert.Contains("-ScanDir ", Steps(Plan("-Serial", "-Loopback", "full"), "SERIAL").Single(s => s.Name == "full-smoke").Command, StringComparison.Ordinal);
        Assert.Contains("foreach ($extra in $ScanDir)", Smoke, StringComparison.Ordinal);
        Assert.Contains("scanned    = $leaks.Scanned", Smoke, StringComparison.Ordinal);
    }

    [Fact]
    public void The_secrets_probe_runs_against_a_keys_only_file_for_present_and_missing_keys_R2()
    {
        var script = Smoke;
        var run = RunPowerShell(Function(script, "Get-ExceptionKind") + Function(script, "Test-SecretsAccounts") + """
            $d = Join-Path ([IO.Path]::GetTempPath()) ('secrets-rfix-' + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Force -Path $d | Out-Null
            $f = Join-Path $d 'secrets.json'
            Set-Content -LiteralPath $f -Value '{"schema":2,"accounts":{"test":{"k":"dummy"},"test-melee":{"k":"dummy"}}}' -Encoding utf8
            Write-Output ('PRESENT=[' + (Test-SecretsAccounts -Path $f -Accounts @('test', 'test-melee')) + ']')
            Write-Output ('SINGLE=[' + (Test-SecretsAccounts -Path $f) + ']')
            $m = Test-SecretsAccounts -Path $f -Accounts ([string[]]@('test', 'test-archer'))
            Write-Output ('MISSING=' + $m.Substring(0, 40))
            $g = Join-Path $d 'b.json'
            Set-Content -LiteralPath $g -Value '{"schema":1}' -Encoding utf8
            Write-Output ('NOACCOUNTS=' + (Test-SecretsAccounts -Path $g -Accounts @('test')).Substring(0, 30))
            Set-Content -LiteralPath $f -Value '{ not json' -Encoding utf8
            Write-Output ('BROKEN=' + (Test-SecretsAccounts -Path $f -Accounts @('test')))
            Remove-Item -Recurse -Force $d
            """);

        Assert.Contains("PRESENT=[]", run, StringComparison.Ordinal);
        Assert.Contains("SINGLE=[]", run, StringComparison.Ordinal);
        Assert.Contains("MISSING=secrets.json has no accounts.test-archer", run, StringComparison.Ordinal);
        Assert.Contains("NOACCOUNTS=secrets.json has no accounts.t", run, StringComparison.Ordinal);
        Assert.Contains("BROKEN=secrets.json could not be parsed (", run, StringComparison.Ordinal);
        Assert.DoesNotContain("dummy", run, StringComparison.Ordinal); // no value is read out
    }

    [Fact]
    public void In_the_parallel_phase_a_frame_only_failure_is_reported_whatever_else_is_running_R6()
    {
        var run = RunPowerShell(Function(Smoke, "Get-LiveFrameVerdict") + """
            $t = '2026-10-23T01:02:03Z ERROR [logincheck] frame over 50 ms between Enter and walking: 61 ms'
            Write-Output ('PARALLEL_NOBODY=' + (Get-LiveFrameVerdict $t 0 $false $true).Contended)
            Write-Output ('SOLO_PHASE_NOBODY=' + (Get-LiveFrameVerdict $t 0 $false $false).Contended)
            Write-Output ('SOLO_PHASE_OTHERS=' + (Get-LiveFrameVerdict $t 2 $false $false).Contended)
            Write-Output ('SOLOFRAMES=' + (Get-LiveFrameVerdict $t 0 $true $true).Contended)
            Write-Output ('OTHER_ERROR=' + (Get-LiveFrameVerdict ($t + "`n2026-10-23T01:02:04Z ERROR [net] session failed") 0 $false $true).Contended)
            """);

        Assert.Contains("PARALLEL_NOBODY=True", run, StringComparison.Ordinal);
        Assert.Contains("SOLO_PHASE_NOBODY=False", run, StringComparison.Ordinal); // the solo phase judges
        Assert.Contains("SOLO_PHASE_OTHERS=True", run, StringComparison.Ordinal); // unchanged outside the gate: another Godot run still counts
        Assert.Contains("SOLOFRAMES=False", run, StringComparison.Ordinal);
        Assert.Contains("OTHER_ERROR=False", run, StringComparison.Ordinal); // only a frame-only failure is reported
        Assert.Contains("$parallelPhase = [bool]($ContendedFrames -or $Lanes)", Smoke, StringComparison.Ordinal);
        Assert.Contains("-Reported $parallelPhase", Smoke, StringComparison.Ordinal);
    }

    [Fact]
    public void The_gate_calls_the_tested_frame_only_function_and_login_throwaway_leaves_the_test_lane_R8_R9()
    {
        Assert.DoesNotContain("function Test-FrameOnlyText", Runner, StringComparison.Ordinal);
        var module = File.ReadAllText(GateModule);
        Assert.Contains("Test-FrameOnlyText -Detail ([string]$r.Detail) -LogText $logText", module, StringComparison.Ordinal);
        Assert.DoesNotContain(Steps(Plan("-Loopback", "full"), "PARALLEL"), s => s.Lane == "test" && s.Command.Contains("login-throwaway", StringComparison.Ordinal));
        Assert.Equal(3, Steps(Plan("-Loopback", "full"), "SOLO").Count(s => s.Command.Contains("-Only login-throwaway ", StringComparison.Ordinal)));
    }

    [Fact]
    public void Every_non_soak_loopback_class_is_in_core_or_an_area_R12()
    {
        var areas = File.ReadAllText(RepositoryFiles.PathOf("tools", "lib", "GateAreas.psd1"));
        var named = Regex.Matches(areas, @"'(\w+)'").Select(m => m.Groups[1].Value).ToHashSet();
        var pattern = new Regex(@"(?<attrs>(?:[ \t]*(?:\[[^\]\r\n]+\]|//[^\r\n]*)[ \t]*\r?\n)+)[ \t]*(?:public|internal)\s+(?:sealed\s+|partial\s+|abstract\s+|static\s+)*class\s+(?<name>\w+)");
        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(RepositoryFiles.PathOf("tests", "Lifestone.Loopback.Tests"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal) || file.EndsWith("LoopbackConventionTests.cs", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match m in pattern.Matches(File.ReadAllText(file)))
            {
                var attrs = m.Groups["attrs"].Value;
                if (attrs.Contains("Collection(LoopbackCollection.Name)", StringComparison.Ordinal) && !attrs.Contains("\"Soak\"", StringComparison.Ordinal) && !named.Contains(m.Groups["name"].Value))
                {
                    missing.Add(m.Groups["name"].Value);
                }
            }
        }

        Assert.True(missing.Count == 0, "loopback classes in no gate area and not in Core (a change to their code would skip them under -Loopback core): " + string.Join(", ", missing));
        Assert.Contains("'ModXpLiveTests'", areas, StringComparison.Ordinal);
    }

    [Fact]
    public void An_offscreen_play_run_is_held_to_the_muted_rule_with_its_own_output_R15()
    {
        var module = RepositoryFiles.PathOf("tools", "lib", "Play.psm1");
        var run = RunPowerShell($"Import-Module '{module}' -Force -DisableNameChecking\n" + """
            function V($t) { $v = Get-PlayAudioVerdict $t; Write-Output ($v.Ok.ToString() + '|' + $v.Reason) }
            Write-Output ('A=' + (V 'INFO [audio] audio service installed voices=16 master_db=-80 muted=True device=Default ms=8'))
            Write-Output ('B=' + (V 'INFO [audio] audio service installed voices=16 master_db=0 muted=False device=Default ms=8'))
            Write-Output ('C=' + (V 'a capture run that never installed audio'))
            """);

        Assert.Contains("A=True|", run, StringComparison.Ordinal);
        Assert.Contains("B=False|the game logged \"audio service installed ... muted=false\"", run, StringComparison.Ordinal);
        Assert.Contains("C=False|the game output has no", run, StringComparison.Ordinal);
        var play = File.ReadAllText(RepositoryFiles.PathOf("tools", "play.ps1"));
        Assert.Contains("Invoke-PlayChild $godotScript $godotParameters -Capture:$Offscreen", play, StringComparison.Ordinal);
        Assert.Contains("Get-PlayAudioVerdict $script:capturedOutput.ToString()", play, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------ rfix-c: the g2 faults, executed

    [Fact]
    public void Test_counts_are_read_from_the_trx_and_from_both_console_summaries_rfix_c_1()
    {
        // The detailed logger's summary lines, as test-loopback.log of g2 ends (28 tests, 27 passed, 1 skipped) and test-archer's (3 passed, 1 failed).
        var run = RunPowerShell(Stub("""
            $detailed = "Test Run Successful.`nTotal tests: 28`n     Passed: 27`n    Skipped: 1`n Total time: 22.6258 Minutes`nResults File: x.trx"
            $failing = "Total tests: 4`n     Passed: 3`n     Failed: 1`n Total time: 3.1983 Minutes"
            $normal = "Failed:     0, Passed:     9, Skipped:     0, Total:     9, Duration: 6 m"
            foreach ($t in @($detailed, $failing, $normal, 'nothing here')) { $c = Get-TestCounts -Text $t; Write-Output ("C=$($c.Source)|$($c.Passed)|$($c.Failed)|$($c.Skipped)") }
            $trx = Join-Path $d 'x.trx'
            Set-Content -LiteralPath $trx -Value '<TestRun><ResultSummary outcome="Completed"><Counters total="28" executed="27" passed="27" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" /></ResultSummary></TestRun>'
            $c = Get-TestCounts -Text 'no summary in the console' -TrxPath $trx; Write-Output ("T=$($c.Source)|$($c.Passed)|$($c.Failed)|$($c.Skipped)")
            # Through the step runner: a stub dotnet-test step that prints the detailed summary is judged PASS with the counts, not "filter matched nothing".
            $step = New-Stub 'det' "Write-Output 'Total tests: 28'; Write-Output '     Passed: 27'; Write-Output '    Skipped: 1'"
            $rows = @(Invoke-GateSteps -LaneSteps @($step) -Root $d -LogDir $d -GateLog (Join-Path $d 'gate.log'))
            Write-Output ('ROW=' + $rows[0].Ok + '|' + $rows[0].Detail)
            """));

        Assert.Contains("C=console|27|0|1", run, StringComparison.Ordinal);
        Assert.Contains("C=console|3|1|0", run, StringComparison.Ordinal);
        Assert.Contains("C=console|9|0|0", run, StringComparison.Ordinal);
        Assert.Contains("C=none|0|0|0", run, StringComparison.Ordinal);
        Assert.Contains("T=trx|27|0|1", run, StringComparison.Ordinal);
        Assert.Contains("ROW=True|passed=27 failed=0 skipped=1 exit=0", run, StringComparison.Ordinal);
        Assert.DoesNotContain("filter matched nothing", run, StringComparison.Ordinal);
    }

    [Fact]
    public void A_smoke_child_that_runs_git_ends_inside_a_lane_job_and_a_step_past_its_limit_is_a_named_FAIL_rfix_c_2()
    {
        // g2: smoke wrote summary.md and then never exited in a Start-Job lane: its git calls (summary.json) waited on the job's inherited stdin. The stub runs git exactly so, through the real lane job.
        var run = RunPowerShell(Stub("""
            $gitStep = New-Stub 'git' "& git --version | Out-Null; Write-Output 'Failed: 0, Passed: 1, Skipped: 0'"
            $gitStep.TimeoutSeconds = 45
            $slow = New-Stub 'slow' "Start-Sleep -Seconds 120"
            $slow.TimeoutSeconds = 3
            $steps = @($gitStep, $slow)
            $job = Start-GateLaneJob -Lane 'stub' -Steps $steps -Root $d -LogDir $d -GateLog (Join-Path $d 'gate.log')
            $rows = @(Receive-GateLaneJob -Job $job -Steps $steps)
            foreach ($r in $rows) { Write-Output ('ROW=' + $r.Step + '|' + $r.Ok + '|' + $r.Detail + '|' + [math]::Round($r.Seconds)) }
            """));

        var git = Regex.Match(run, @"ROW=git\|(?<ok>\w+)\|(?<detail>[^|]*)\|(?<s>\d+)");
        Assert.True(git.Success, run);
        Assert.Equal("True", git.Groups["ok"].Value);
        Assert.True(int.Parse(git.Groups["s"].Value, System.Globalization.CultureInfo.InvariantCulture) < 40, "the git step took as long as a hang: " + run);
        Assert.Contains("ROW=slow|False|TIMED OUT: step 'stub slow' ran past its timeout of 3 s and was killed (exit 124)", run, StringComparison.Ordinal);
        Assert.Contains("-RedirectStandardInput $stdin", File.ReadAllText(GateModule), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_gate_step_has_a_named_timeout_and_none_is_four_hours_rfix_c_2()
    {
        foreach (var plan in new[] { Plan("-Loopback", "full"), Plan("-Serial", "-Loopback", "full"), Plan("-Loopback", "full", "-Soak") })
        {
            var steps = plan.Where(l => Regex.IsMatch(l, @"^\[gate-plan\] [A-Z]+ lane=")).ToList();
            Assert.NotEmpty(steps);
            Assert.All(steps, l =>
            {
                var m = Regex.Match(l, @"\(timeout (\d+) s\)$");
                Assert.True(m.Success, "no timeout on: " + l);
                var seconds = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                Assert.InRange(seconds, 120, 10_000);
            });
        }

        Assert.DoesNotContain("14400", string.Join("\n", Plan("-Loopback", "full")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_fighter_lanes_budget_the_buff_leg_P5a_Fgate()
    {
        // Round 2 of the P5a gate: the Archer's lane was killed at 600 s (253 s done + Appraise about 60 s + the buff leg about 700 s needs about 1,015 s) and the Brawler's lane needed about 1,250 s against a limit of 1,040 s.
        var plan = Plan("-Loopback", "full");
        int Limit(string lane) => int.Parse(Regex.Match(plan.Single(l => l.StartsWith("[gate-plan] PARALLEL lane=" + lane + " ", StringComparison.Ordinal) && l.Contains(" loopback:", StringComparison.Ordinal)), @"\(timeout (\d+) s\)$").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(1600, Limit("test-melee"));
        Assert.Equal(1500, Limit("test-archer"));
        Assert.Contains(plan, l => l.Contains("release: after a step is killed by its timeout the next step on the same account waits up to 80 s", StringComparison.Ordinal));
    }

    [Fact]
    public void A_step_killed_by_its_timeout_makes_the_next_step_on_that_account_wait_for_ACE_to_release_the_character_and_other_accounts_do_not_P5a_Fgate()
    {
        var run = RunPowerShell(Stub("""
            $gl = Join-Path $d 'gate.log'
            function Mk($name, $body, $account, $secs) { $x = New-Stub $name $body; $x | Add-Member Account $account; $x | Add-Member ReleaseSeconds 6; $x.TimeoutSeconds = $secs; $x }
            $slow = Mk 'slow' 'Start-Sleep -Seconds 120' 'acct-a' 2
            $same = Mk 'same' "Write-Output 'Failed: 0, Passed: 1, Skipped: 0'" 'acct-a' 30
            $other = Mk 'other' "Write-Output 'Failed: 0, Passed: 1, Skipped: 0'" 'acct-b' 30
            $none = Mk 'none' "Write-Output 'Failed: 0, Passed: 1, Skipped: 0'" 'none' 30
            # The kill; another account and an account-less step then run at once; the same account waits out the rest of the 6 s.
            $w = [Diagnostics.Stopwatch]::StartNew()
            $r1 = @(Invoke-GateSteps -LaneSteps @($slow) -Root $d -LogDir $d -GateLog $gl)
            $afterKill = $w.Elapsed.TotalSeconds
            $r2 = @(Invoke-GateSteps -LaneSteps @($other, $none) -Root $d -LogDir $d -GateLog $gl)
            $afterOther = $w.Elapsed.TotalSeconds
            $r3 = @(Invoke-GateSteps -LaneSteps @($same) -Root $d -LogDir $d -GateLog $gl)
            $afterSame = $w.Elapsed.TotalSeconds
            Write-Output ('T=' + [math]::Round($afterKill, 1) + '|' + [math]::Round($afterOther - $afterKill, 1) + '|' + [math]::Round($afterSame - $afterOther, 1))
            Write-Output ('OK=' + $r1[0].Ok + '|' + $r2[0].Ok + '|' + $r2[1].Ok + '|' + $r3[0].Ok)
            Write-Output ('LOG=' + ((Get-Content -LiteralPath $gl) -join ' // '))
            """));

        var t = Regex.Match(run, @"T=(?<kill>[\d.]+)\|(?<other>[\d.]+)\|(?<same>[\d.]+)");
        Assert.True(t.Success, run);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Assert.True(double.Parse(t.Groups["other"].Value, inv) < 4.5, "another account must not wait: " + run);
        Assert.True(double.Parse(t.Groups["same"].Value, inv) >= 1.0, "the same account must wait out the release (6 s from the kill, minus what the other steps already used): " + run);
        Assert.Contains("OK=False|True|True|True", run, StringComparison.Ordinal); // the killed step is a FAIL; the waited step runs and passes
        Assert.Contains("slow KILLED by its timeout: the next step on account 'acct-a' waits up to 6 s", run, StringComparison.Ordinal);
        Assert.Contains("same WAIT ", run, StringComparison.Ordinal);
        Assert.DoesNotContain("other WAIT", run, StringComparison.Ordinal);
    }

    [Fact]
    public void The_den_fight_runs_in_the_solo_phase_after_the_melee_hunt_and_the_plan_says_so_rfix_c_5()
    {
        var plan = Plan("-Loopback", "full");
        var lane = Steps(plan, "PARALLEL");
        Assert.DoesNotContain("login-bsd", string.Join("\n", lane.Select(s => s.Command)), StringComparison.Ordinal);
        var solo = Steps(plan, "SOLO").Select(s => s.Name).ToList();
        Assert.Equal(solo.IndexOf("melee-hunt") + 1, solo.IndexOf("login-bsd"));
        Assert.Contains(plan, l => l.Contains("login-bsd runs in the SOLO phase", StringComparison.Ordinal));
    }

    [Fact]
    public void The_two_real_frame_failures_of_g2_are_frame_only_flagged_runs_and_a_real_failure_is_not_rfix_c_3()
    {
        var run = RunPowerShell(Stub("""
            $check = "ERROR: 2026-10-23T09:28:07.052Z ERROR [logincheck] the login check failed stage=done why=`"DoD 15: 1 frame(s) over 50 ms between Enter and walking, the longest 68.5 ms (each is logged above with what the frame held)`""
            $throw = "ERROR: 2026-10-23T09:43:51.523Z ERROR [logincheck] the throwaway check failed stage=done why=`"a frame was over 50 ms`" recovered=False"
            $over75 = "ERROR: 2026-10-23T09:43:51.523Z ERROR [logincheck] the login check failed stage=done why=`"DoD 15: 1 frame(s) over 50 ms between Enter and walking, the longest 81.0 ms (each is logged above)`""
            $other = "ERROR: 2026-10-23T09:43:51.523Z ERROR [logincheck] the login check failed stage=done why=`"the character list was empty`""
            $mixed = $check + "`n" + $other
            $rowOf = { param($step, $detail, $log) $r = [pscustomobject]@{ Step = $step; Outcome = 'FAIL'; Detail = $detail; Seconds = 1; Log = 'x.log' }
                $rd = { param($row) $log }.GetNewClosure()
                $summary = [pscustomobject]@{ secrets = 0; scanned = 1; verdict = 'x' }
                @(Get-SmokeStepRows -StepName $step -Code 1 -SmokeRows @($r) -Summary $summary -ReadLog $rd -LogPath 'l')[0] }
            $a = & $rowOf 'login-check' 'done ok=false enter_to_walk_max_ms=68.5 holtburg_min_fps=238.4; FAIL: 1 ERROR line(s) logged' $check
            $b = & $rowOf 'login-throwaway' 'throwaway done ok=false throwaway=purged teleport_frame_max_ms=59.4 slow_frames=1; FAIL: 1 ERROR line(s) logged' $throw
            $c = & $rowOf 'login-check' 'done ok=false enter_to_walk_max_ms=81.0; FAIL: 1 ERROR line(s) logged' $over75
            $e = & $rowOf 'login-check' 'done ok=false; FAIL: 1 ERROR line(s) logged' $other
            $f = & $rowOf 'login-check' 'done ok=false; FAIL: 2 ERROR line(s) logged' $mixed
            foreach ($x in @($a, $b, $c, $e, $f)) { Write-Output ('R=' + $x.Name + '|frameOnly=' + $x.FrameOnly + '|over75=' + $x.Over75) }
            """));

        var rows = Regex.Matches(run, @"R=(?<n>[\w-]+)\|frameOnly=(?<f>\w+)\|over75=(?<o>\w+)").Select(m => (m.Groups["f"].Value, m.Groups["o"].Value)).ToList();
        Assert.Equal([("True", "False"), ("True", "False"), ("True", "True"), ("False", "False"), ("False", "False")], rows);

        // The section 5 rule on the real g2 runs: one flagged run of three passes; two flagged runs of three fail by the frame rule (not "a reason other than a frame window").
        var group = Function(Runner, "Get-GroupVerdict");
        string Verdict(string runs) => RunPowerShell(group + $"$v = Get-GroupVerdict @({runs})\nWrite-Output (\"DETAIL=\" + $v.Detail)");
        const string Clean = "@{ Ok = $true; FrameOnly = $false; Over75 = $false }";
        const string Frame = "@{ Ok = $false; FrameOnly = $true; Over75 = $false }";
        Assert.Contains("one run over 50 ms: flagged", Verdict($"{Frame}, {Clean}, {Clean}"), StringComparison.Ordinal);
        Assert.Contains("2 of 3 runs over 50 ms (two of three fail the gate)", Verdict($"{Frame}, {Frame}, {Clean}"), StringComparison.Ordinal);
    }

    [Fact]
    public void No_script_assigns_a_variable_named_like_one_of_its_own_typed_parameters_rfix_c_4()
    {
        // g2: play.ps1 stored the audio verdict (a hashtable) in $audio, which is its [switch] $Audio parameter (PowerShell names are case-insensitive): "Cannot convert ... Hashtable to SwitchParameter" threw
        // after the game exited 0, the catch set exit 2, and smoke's login row read FAIL Exit=2 with ok=true in the verdict. The detector finds that shape in every tools script.
        const string Detector = """
            function Find-Shadow([string] $Path) {
                $tokens = $null; $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
                if (-not $ast.ParamBlock) { return }
                $typed = @{}
                foreach ($p in $ast.ParamBlock.Parameters) { if ($p.StaticType.IsValueType) { $typed[$p.Name.VariablePath.UserPath.ToLowerInvariant()] = $p.StaticType.Name } }
                $assigns = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and $n.Left -is [System.Management.Automation.Language.VariableExpressionAst] }, $true)
                foreach ($a in $assigns) {
                    $inner = $false
                    for ($up = $a.Parent; $up; $up = $up.Parent) { if ($up -is [System.Management.Automation.Language.FunctionDefinitionAst] -or $up -is [System.Management.Automation.Language.ScriptBlockExpressionAst]) { $inner = $true; break } }
                    if ($inner) { continue }
                    # Only a result that cannot be the parameter's own kind is the fault: a command's output (Get-PlayAudioVerdict ...) or a hashtable or array literal. A cast, a constant or another variable
                    # (godot.ps1: $Run = [switch]$true; hero-video.ps1: $Fps = $listFps) is a plain assignment of the same kind.
                    $rhs = $a.Right
                    $isCall = $rhs -is [System.Management.Automation.Language.PipelineAst]
                    $isLiteral = $rhs -is [System.Management.Automation.Language.CommandExpressionAst] -and ($rhs.Expression -is [System.Management.Automation.Language.HashtableAst] -or $rhs.Expression -is [System.Management.Automation.Language.ArrayLiteralAst] -or $rhs.Expression -is [System.Management.Automation.Language.ArrayExpressionAst])
                    if (-not ($isCall -or $isLiteral)) { continue }
                    $name = $a.Left.VariablePath.UserPath.ToLowerInvariant()
                    if ($typed.ContainsKey($name)) { "$([IO.Path]::GetFileName($Path)):$($a.Extent.StartLineNumber) assigns `$$name, a value-typed parameter ($($typed[$name]))" }
                }
            }
            """;
        var sample = Path.Combine(Path.GetTempPath(), "shadow-" + Guid.NewGuid().ToString("N") + ".ps1");
        try
        {
            File.WriteAllText(sample, "param([switch] $Audio, $Free)\n$audio = @{ Ok = $true }\n$free = 1\nfunction F { $audio = 2 }\n");
            var found = RunPowerShell(Detector + $"Find-Shadow '{sample}'");
            Assert.Contains(":2 assigns $audio, a value-typed parameter (SwitchParameter)", found, StringComparison.Ordinal);
            Assert.DoesNotContain("$free", found, StringComparison.Ordinal);
            Assert.DoesNotContain(":4 ", found, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(sample);
        }

        var scripts = Directory.GetFiles(RepositoryFiles.PathOf("tools"), "*.ps1", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "research" + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToList();
        var all = RunPowerShell(Detector + string.Join("\n", scripts.Select(f => $"Find-Shadow '{f}'")));
        Assert.Equal(string.Empty, all.Trim());
        Assert.DoesNotContain("$audio = Get-PlayAudioVerdict", File.ReadAllText(RepositoryFiles.PathOf("tools", "play.ps1")), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------ helpers

    private sealed record PlanStep(string Phase, string Lane, string Name, string Command);

    private static string[] Plan(params string[] arguments)
    {
        var key = string.Join(' ', arguments);
        if (!PlanCache.TryGetValue(key, out var lines))
        {
            // -Loopback core reads the diff with git unless -ChangedPaths names it: the tests always name it.
            var extra = arguments.Contains("core") && !arguments.Contains("-ChangedPaths") ? new[] { "-ChangedPaths", "docs/none.md" } : [];
            var text = RunRunner(["-Plan", .. arguments, .. extra], expectSuccess: true);
            lines = [.. text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("[gate-plan]", StringComparison.Ordinal))];
            PlanCache[key] = lines;
        }

        return lines;
    }

    private static List<PlanStep> Steps(string[] plan, string phase)
    {
        var steps = new List<PlanStep>();
        foreach (var line in plan)
        {
            var match = Regex.Match(line, @"^\[gate-plan\] (?<phase>[A-Z]+) lane=(?<lane>\S+) step=\d+ (?<name>\S+): (?<cmd>.*)$");
            if (match.Success && match.Groups["phase"].Value == phase)
            {
                steps.Add(new PlanStep(phase, match.Groups["lane"].Value, match.Groups["name"].Value, match.Groups["cmd"].Value));
            }
        }

        Assert.NotEmpty(steps);
        return steps;
    }

    private static string RunRunner(string[] arguments, bool expectSuccess)
    {
        var script = RepositoryFiles.PathOf("tools", "live-gate.ps1");
        var info = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        string[] all = ["-NoProfile", "-NonInteractive", "-File", script, "-Worktree", RepositoryFiles.PathOf(), .. arguments];
        foreach (var a in all)
        {
            info.ArgumentList.Add(a);
        }

        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(120_000), "live-gate.ps1 did not finish");
        Assert.True((process.ExitCode == 0) == expectSuccess, "live-gate.ps1 exit code " + process.ExitCode + ": " + error.Result + output);
        return output + error.Result;
    }

    private static string GateModule => RepositoryFiles.PathOf("tools", "lib", "GateJudge.psm1");

    private static string Function(string script, string name)
    {
        var start = script.IndexOf("function " + name, StringComparison.Ordinal);
        Assert.True(start >= 0, "script has no function " + name);
        var end = Regex.Match(script[start..], @"\r?\n\}\r?\n");
        Assert.True(end.Success, "no end of function " + name);
        return script[start..(start + end.Index + end.Length)] + "\n";
    }

    private static string RunPowerShell(string command) => RunShell(command, expectSuccess: true);

    private static string RunPowerShellFails(string command) => RunShell(command, expectSuccess: false);

    private static string RunShell(string command, bool expectSuccess)
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
                Assert.True((process.ExitCode == 0) == expectSuccess, "PowerShell exit code " + process.ExitCode + ": " + error.Result);
                return output + error.Result;
            }
            catch (Win32Exception)
            {
                // that shell is not installed: try the next
            }
        }

        throw new InvalidOperationException("neither pwsh nor powershell is installed");
    }
}
