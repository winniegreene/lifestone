using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// M5 item 7 s3: tools/soak.ps1 (the soak's runner) and tools/soak-report.ps1 (its report). The runner is tried only with <c>-Plan</c> or
/// with a refusal (a fake main checkout through its <c>-MainCheckout</c> seam, so it never reads or starts the real backend, never
/// runs Godot and creates no run folder); the report runs over a small synthetic log and CSV that the test writes (no retail data, no
/// backend, no game).
/// </summary>
public sealed class SoakRunnerTests
{
    private static string Script(params string[] parts) => File.ReadAllText(RepositoryFiles.PathOf(parts)).Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void Soak_scripts_parse_and_have_their_parameters()
    {
        foreach (var (script, parameters) in new[]
        {
            ("soak.ps1", new[] { "Until", "Minutes", "Resolution", "FrameSeconds", "Rehearsal", "Plan", "Stop", "RunsRoot", "MainCheckout", "Resume", "SetAsideOnly", "AutopilotFolder", "ControlDrill", "TripAfterKills" }),
            ("soak-watch.ps1", new[] { "IntervalSeconds", "OutFile", "RunFolder", "GameLog", "Since", "ExpectGame", "GodotProcessPattern" }),
            ("soak-report.ps1", new[] { "RunFolder" }),
        })
        {
            var path = RepositoryFiles.PathOf("tools", script);
            var result = Pwsh(
                "-Command",
                "$tokens = $null; $errors = $null; "
                + $"$ast = [System.Management.Automation.Language.Parser]::ParseFile('{path.Replace("'", "''", StringComparison.Ordinal)}', [ref]$tokens, [ref]$errors); "
                + "if ($errors.Count -gt 0) { $errors | ForEach-Object { $_.Message }; exit 1 }; "
                + "($ast.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath }) -join ','");
            Assert.Equal(0, result.ExitCode);
            foreach (var parameter in parameters)
            {
                Assert.Contains(parameter, result.Output.Trim().Split(','));
            }
        }
    }

    [Fact]
    public void Soak_plan_prints_the_run_the_game_arguments_and_both_roots_and_starts_nothing()
    {
        using var main = new TempFolder();
        main.Write(Path.Combine("cache", "backend-owner.txt"), "the lead\n"); // the lock exists; no backend runtime does (reported, not fatal under -Plan)
        var before = RunFolders();

        var result = RunSoak("-Minutes", "300", "-Plan", "-MainCheckout", main.Path);

        Assert.Equal(0, result.ExitCode);
        var output = result.Output;
        var stamp = Regex.Match(output, @"--soak-frame-dir=cache/soak/(?<stamp>\d{8}T\d{6}Z)/frames").Groups["stamp"].Value;
        Assert.NotEmpty(stamp);
        Assert.Contains(
            $"--autopilot-start --autopilot-minutes=300 --fight-loot=on --max-fps=60 --soak-line-s=60 --log-copies=file --log-max-mb=16 --log-keep=0 --log-vitals-s=10 --log-objects-m=60 --clean-hud --mute --soak-frame-s=1800 --soak-frame-dir=cache/soak/{stamp}/frames --autopilot-stop-file=cache/soak/{stamp}/stop",
            output,
            StringComparison.Ordinal);
        Assert.Contains("-Offscreen -IsolateUserData -KeepServer -Character \"BSD Mage\" -Resolution 1280x720 -TimeoutSeconds 19800", output, StringComparison.Ordinal); // 300 * 60 + 1800
        Assert.DoesNotContain("--record-session", output, StringComparison.Ordinal);
        Assert.Contains("[soak] main checkout (backend, lock, server folders): " + main.Path, output, StringComparison.Ordinal);
        Assert.Contains("[soak] this checkout (run folder, game, user data): " + RepositoryFiles.Root, output, StringComparison.Ordinal);
        Assert.Contains("tools\\soak-watch.ps1 -IntervalSeconds 300 -OutFile", output, StringComparison.Ordinal);
        Assert.Contains("-ServerLogsDirectory " + Path.Combine(main.Path, "server", "runtime", "logs"), output, StringComparison.Ordinal);
        Assert.Contains("-DatabaseDirectory " + Path.Combine(main.Path, "server", "runtime", "mariadb-data"), output, StringComparison.Ordinal);
        Assert.Contains("-GodotLogsDirectory " + Path.Combine(RepositoryFiles.Root, "cache", "godot", "roaming", "Godot", "app_userdata", "Lifestone", "logs"), output, StringComparison.Ordinal);
        Assert.Contains("-Plan: nothing was started and nothing was created", output, StringComparison.Ordinal);
        Assert.Equal(before, RunFolders()); // nothing was created
    }

    [Fact]
    public void M5_7_F18_soak_stop_writes_the_stop_file_in_the_newest_run_folder_without_a_report_and_refuses_when_none_runs()
    {
        using var runs = new TempFolder("runs");
        var none = RunSoak("-Stop", "-RunsRoot", runs.Path);
        Assert.Equal(1, none.ExitCode);
        Assert.Contains("no soak is running", none.Output, StringComparison.Ordinal);

        runs.Write(System.IO.Path.Combine("20260101T000000Z", "report.txt"), "done\n"); // ended
        runs.Write(System.IO.Path.Combine("20260102T000000Z", "watch.csv"), "x\n"); // running
        runs.Write(System.IO.Path.Combine("20260103T000000Z", "report.txt"), "done\n"); // the newest, ended: not it

        var result = RunSoak("-Stop", "-RunsRoot", runs.Path);

        Assert.Equal(0, result.ExitCode);
        var stop = System.IO.Path.Combine(runs.Path, "20260102T000000Z", "stop");
        Assert.True(File.Exists(stop));
        Assert.Contains("[soak] -Stop: wrote " + stop, result.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(System.IO.Path.Combine(runs.Path, "20260103T000000Z", "stop")));
        Assert.False(File.Exists(System.IO.Path.Combine(runs.Path, "20260101T000000Z", "stop")));
    }

    [Fact]
    public void Soak_plan_takes_the_length_the_resolution_the_frame_interval_and_the_rehearsal_name()
    {
        using var main = new TempFolder();
        main.Write(Path.Combine("cache", "backend-owner.txt"), "the lead\n");

        var result = RunSoak("-Minutes", "45", "-Resolution", "800x600", "-FrameSeconds", "600", "-Rehearsal", "-Plan", "-MainCheckout", main.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"--soak-frame-dir=cache/soak/\d{8}T\d{6}Z-rehearsal/frames", result.Output);
        Assert.Contains("--autopilot-minutes=45 ", result.Output, StringComparison.Ordinal);
        Assert.Contains("--soak-frame-s=600 ", result.Output, StringComparison.Ordinal);
        Assert.Contains("-Resolution 800x600 -TimeoutSeconds 4500", result.Output, StringComparison.Ordinal); // 45 * 60 + 1800
        Assert.Contains("rehearsal=yes", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void M5_7_D145_control_drill_passes_the_drill_folder_and_forces_a_trip_unless_one_is_given()
    {
        using var main = new TempFolder();
        main.Write(Path.Combine("cache", "backend-owner.txt"), "the lead\n");

        var plain = RunSoak("-Minutes", "45", "-Plan", "-MainCheckout", main.Path);
        Assert.DoesNotContain("--autopilot-control-drill", plain.Output, StringComparison.Ordinal);

        var drill = RunSoak("-Minutes", "45", "-ControlDrill", "-Plan", "-MainCheckout", main.Path);
        Assert.Equal(0, drill.ExitCode);
        Assert.Matches(@"--autopilot-control-drill=cache/soak/\d{8}T\d{6}Z/control-drill", drill.Output);
        Assert.Contains("--autopilot-trip-after-kills=6", drill.Output, StringComparison.Ordinal);
        Assert.Contains("-ControlDrill: no -TripAfterKills given", drill.Output, StringComparison.Ordinal);

        var given = RunSoak("-Minutes", "45", "-ControlDrill", "-TripAfterKills", "3", "-Plan", "-MainCheckout", main.Path);
        Assert.Contains("--autopilot-trip-after-kills=3", given.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("--autopilot-trip-after-kills=6", given.Output, StringComparison.Ordinal);
        Assert.Contains("--autopilot-control-drill=", given.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Soak_refuses_without_the_leads_lock_and_plan_names_the_missing_lock()
    {
        using var main = new TempFolder(); // no cache\backend-owner.txt
        var before = RunFolders();

        var refused = RunSoak("-Minutes", "30", "-MainCheckout", main.Path);
        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("[soak] REFUSED: cache\\backend-owner.txt does not exist in the main checkout", refused.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("starting the game", refused.Output, StringComparison.Ordinal);

        var planned = RunSoak("-Minutes", "30", "-Plan", "-MainCheckout", main.Path);
        Assert.Equal(0, planned.ExitCode); // -Plan always exits 0 and says what would refuse
        Assert.Contains("precondition not met: cache\\backend-owner.txt does not exist", planned.Output, StringComparison.Ordinal);
        Assert.Equal(before, RunFolders());
    }

    [Fact]
    public void Soak_refuses_with_the_lock_but_no_backend()
    {
        using var main = new TempFolder();
        main.Write(Path.Combine("cache", "backend-owner.txt"), "the lead\n"); // the lock, but the fake main checkout has no server\runtime

        var result = RunSoak("-Minutes", "30", "-MainCheckout", main.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("REFUSED", result.Output, StringComparison.Ordinal);
        Assert.Contains("no server\\runtime", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { "-Minutes", "10", "-Until", "23:59" }, "not both")]
    [InlineData(new string[0], "give -Until")]
    public void Soak_refuses_an_unclear_length(string[] arguments, string expected)
    {
        using var main = new TempFolder();
        var result = RunSoak([.. arguments, "-MainCheckout", main.Path]);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Soak_runner_holds_the_display_and_ends_the_watcher_in_a_finally_and_touches_no_backend_or_secret()
    {
        var script = Script("tools", "soak.ps1");

        Assert.Contains("Start-GateDisplayHold", script, StringComparison.Ordinal);
        Assert.Contains("Stop-GateDisplayHold -Process $hold", script, StringComparison.Ordinal);
        Assert.Contains("Get-ForeignGodot", script, StringComparison.Ordinal);
        Assert.Contains("Get-LsBackendState", script, StringComparison.Ordinal);
        Assert.Contains("$watcher.Kill($true)", script, StringComparison.Ordinal);
        Assert.Contains("'-DurationSeconds', \"$($timeoutSeconds + 600)\"", script, StringComparison.Ordinal); // 7-R3: the watcher ends itself when this script is killed hard
        Assert.Matches(@"(?s)finally \{.*Stop-GateDisplayHold.*\$reportScript", script);
        Assert.Contains("'-KeepServer'", script, StringComparison.Ordinal); // play.ps1 never stops the lead's backend
        Assert.DoesNotContain("start-server", script, StringComparison.Ordinal);
        Assert.DoesNotContain("stop-server", script, StringComparison.Ordinal);
        Assert.DoesNotContain("setup-server", script, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.json", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-Password", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--record-session", script, StringComparison.Ordinal); // recording off
        Assert.DoesNotContain("Set-ItemProperty", script, StringComparison.Ordinal); // no registry, power or other setting
        Assert.DoesNotContain("powercfg", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Soak_report_gives_the_expected_fields_over_a_synthetic_log_and_csv()
    {
        using var run = new TempFolder("20261028T160000Z");
        run.Write("lifestone.log", SyntheticLog());
        run.Write("watch.csv", "time_utc,godot_ws_mb,ace_ws_mb,mariadb_ws_mb,db_mb,free_gb\n2026-10-28T16:00:00Z,2000.5,n/a,300,10,50\n2026-10-28T16:05:00Z,2100.5,800,310,11,49.5\n");

        var result = Pwsh("-File", RepositoryFiles.PathOf("tools", "soak-report.ps1"), "-RunFolder", run.Path);

        Assert.Equal(0, result.ExitCode);
        var report = File.ReadAllText(Path.Combine(run.Path, "report.txt"));
        Assert.Equal(report.Replace("\r\n", "\n", StringComparison.Ordinal), result.Output.Replace("\r\n", "\n", StringComparison.Ordinal)); // report.txt is the console lines
        Assert.Contains("rehearsal=no minutes=358 soak_lines=180 ", report, StringComparison.Ordinal);
        Assert.Contains("done_ok=yes stop=minutes cycles=2 cycle_lines=2 accounted=yes", report, StringComparison.Ordinal);
        Assert.Contains("ws_slope_mb_per_h=60 ", report, StringComparison.Ordinal); // ws_mb rises 2 MB every 2 minutes
        Assert.Contains("objects_max=56 in_cell_max=3 light_rows_max=40 light_rows_ok=yes", report, StringComparison.Ordinal);
        Assert.Contains("godot_errors=0 p99_first_ms=11 p99_last_ms=11", report, StringComparison.Ordinal);
        Assert.Contains("reconnects=1", report, StringComparison.Ordinal);
        Assert.Contains("reconnect n=1 at=16:30:00Z phase=loggedoff state=fight cycle=2", report, StringComparison.Ordinal);
        Assert.Contains("kills=12 deaths=1 interrupted=1 resumed=1", report, StringComparison.Ordinal);
        Assert.Contains("watch_rows=2 godot_ws_mb_max=2100.5 ace_ws_mb_max=800 mariadb_ws_mb_max=310 db_mb_max=11 free_gb_min=49.5", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Soak_report_flags_unaccounted_cycles_too_many_light_rows_and_a_rolled_log()
    {
        using var run = new TempFolder("20261028T160000Z-rehearsal");
        var older = new StringBuilder();
        older.Append("2026-10-28T16:00:00.000Z INFO  [soak] ws_mb=1000 objects=10 in_cell=1 light_rows=300 frame_p99_ms=9 godot_errors=0 hp_pct=60 sp_pct=100 mp_pct=10 hit_max=9\n");
        run.Write("lifestone.1.log", older.ToString());
        var newer = new StringBuilder();
        newer.Append("2026-10-28T16:01:00.000Z INFO  [soak] ws_mb=1001 objects=11 in_cell=1 light_rows=256 frame_p99_ms=9 godot_errors=3 kills=4 deaths=0 cycles=1 hp_pct=80 sp_pct=90 mp_pct=50 hit_max=17 stance_changes=5 loot_kept=42 loot_left=16 free_slots=38 burden_pct=61.5 trips=3 turn_reversals=300 turn_flips=60 progress_backs=4 swings=9\n");
        newer.Append("2026-10-28T16:02:00.000Z INFO  [autopilot] autopilot cycle n=1 state=ok why=x kills=4 s=60\n");
        newer.Append("2026-10-28T16:03:00.000Z INFO  [autopilot] autopilot cycle n=3 state=ok why=x kills=4 s=60\n"); // n=2 is missing
        newer.Append("2026-10-28T16:04:00.000Z INFO  [autopilot] autopilot done ok=no cycles=3 stop=death kills=8 deaths=1 interrupted=0 resumed=0 reconnects=0 s=240\n");
        run.Write("lifestone.log", newer.ToString());

        var result = Pwsh("-File", RepositoryFiles.PathOf("tools", "soak-report.ps1"), "-RunFolder", run.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("rehearsal=yes", result.Output, StringComparison.Ordinal);
        Assert.Contains("soak_lines=2", result.Output, StringComparison.Ordinal); // both files read, the older first
        Assert.Contains("done_ok=no stop=death cycles=3 cycle_lines=2 accounted=no", result.Output, StringComparison.Ordinal);
        Assert.Contains("light_rows_max=300 light_rows_ok=no", result.Output, StringComparison.Ordinal);
        Assert.Contains("godot_errors=3", result.Output, StringComparison.Ordinal); // the last line's value
        Assert.Contains("reconnects=0 causes=none", result.Output, StringComparison.Ordinal);
        Assert.Contains("ws_slope_mb_per_h=n/a", result.Output, StringComparison.Ordinal); // under an hour of data: no slope, not a guess
        Assert.Contains("watch_rows=n/a", result.Output, StringComparison.Ordinal); // no watch.csv
        Assert.Contains("hp_pct_avg=70 hp_pct_min=60 sp_pct_avg=95 sp_pct_min=90 mp_pct_avg=30 mp_pct_min=10 hit_max=17", result.Output, StringComparison.Ordinal); // M5 7 F16: read by name
        Assert.Contains("[soak-report] stance_changes=5", result.Output, StringComparison.Ordinal); // M5 7 F25: read by name, the last count
        Assert.Contains("[soak-report] loot_kept=42 loot_left=16 free_slots=38 burden_pct=61.5 trips=3", result.Output, StringComparison.Ordinal); // M5 7 F29 (D96): read by name, the last line's values
        Assert.Contains("[soak-report] turn_reversals=300 turn_reversals_per_h=", result.Output, StringComparison.Ordinal); // M5 7 D134: read by name, the last count and per hour
        Assert.Contains(" turn_flips=60 turn_flips_per_h=", result.Output, StringComparison.Ordinal);
        Assert.Contains(" progress_backs=4 progress_backs_per_h=", result.Output, StringComparison.Ordinal);
        Assert.Contains(" swings=9 swings_per_h=", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Soak_watcher_is_given_the_play_timeout_plus_600_seconds_and_a_past_until_is_refused_not_rolled_to_tomorrow()
    {
        using var main = new TempFolder();
        main.Write(Path.Combine("cache", "backend-owner.txt"), "the lead\n");
        var planned = RunSoak("-Minutes", "45", "-Plan", "-MainCheckout", main.Path);
        Assert.Contains("-DurationSeconds 5100", planned.Output, StringComparison.Ordinal); // 45 * 60 + 1800 + 600

        var past = DateTime.UtcNow.AddMinutes(-2).ToString("HH:mm", CultureInfo.InvariantCulture);
        if (DateTime.UtcNow.TimeOfDay < TimeSpan.FromMinutes(3))
        {
            return; // just after midnight UTC a time two minutes ago is yesterday's: nothing to prove
        }

        var refused = RunSoak("-Until", past, "-MainCheckout", main.Path);
        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("has already passed", refused.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("starting the game", refused.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Soak_report_reads_the_older_log_first_says_rolled_and_after_two_rolls_says_the_first_hour_is_partial()
    {
        // One roll: the older file holds the start of the run, so the first hour is whole and the first-hour fields read it.
        using var one = new TempFolder("20261028T160000Z");
        var older = new StringBuilder();
        for (var i = 0; i < 60; i++)
        {
            older.Append(CultureInfo.InvariantCulture, $"2026-10-28T{16 + (i / 60):00}:{i % 60:00}:00.000Z INFO  [soak] ws_mb={1000 + i} objects=10 in_cell=1 light_rows=40 frame_p99_ms=9 godot_errors=0\n");
        }

        one.Write("lifestone.1.log", older.ToString());
        one.Write("lifestone.log", "2026-10-28T17:00:00.000Z INFO  [log] log rolled rolls=1 previous=lifestone.1.log max_mb=256\n2026-10-28T17:30:00.000Z INFO  [soak] ws_mb=1100 objects=10 in_cell=1 light_rows=40 frame_p99_ms=20 godot_errors=0\n");
        var oneRoll = Pwsh("-File", RepositoryFiles.PathOf("tools", "soak-report.ps1"), "-RunFolder", one.Path);
        Assert.Contains("rolled=1 first_hour=whole", oneRoll.Output, StringComparison.Ordinal);
        Assert.Contains("p99_first_ms=9 ", oneRoll.Output, StringComparison.Ordinal);

        // Two rolls: the oldest lines are gone; the first-hour fields are n/a, not a later window's.
        using var two = new TempFolder("20261028T160000Z");
        two.Write("lifestone.1.log", "2026-10-28T19:00:00.000Z INFO  [log] log rolled rolls=1 previous=lifestone.1.log max_mb=256\n2026-10-28T19:01:00.000Z INFO  [soak] ws_mb=1000 objects=10 in_cell=1 light_rows=40 frame_p99_ms=30 godot_errors=0\n");
        two.Write("lifestone.log", "2026-10-28T20:00:00.000Z INFO  [log] log rolled rolls=2 previous=lifestone.1.log max_mb=256\n2026-10-28T20:01:00.000Z INFO  [soak] ws_mb=1001 objects=10 in_cell=1 light_rows=40 frame_p99_ms=30 godot_errors=0\n2026-10-28T20:02:00.000Z INFO  [autopilot] autopilot done ok=yes cycles=1 stop=minutes kills=1 deaths=0 interrupted=0 resumed=0 reconnects=0 s=1\n");
        var twoRolls = Pwsh("-File", RepositoryFiles.PathOf("tools", "soak-report.ps1"), "-RunFolder", two.Path);
        Assert.Contains("rolled=2 first_hour=partial", twoRolls.Output, StringComparison.Ordinal);
        Assert.Contains("p99_first_ms=n/a", twoRolls.Output, StringComparison.Ordinal);
        Assert.Contains("accounted=n/a", twoRolls.Output, StringComparison.Ordinal);
        Assert.Contains("ws_slope_mb_per_h=n/a", twoRolls.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Soak_report_reads_every_rolled_file_oldest_first_and_says_the_first_hour_is_whole_when_none_was_deleted_M5_7_F17()
    {
        // --log-keep=0: three rolls, three files (the highest number is the oldest); the first hour is in lifestone.3.log.
        using var run = new TempFolder("20261028T160000Z");
        run.Write("lifestone.3.log", "2026-10-28T16:01:00.000Z INFO  [soak] ws_mb=1000 objects=10 in_cell=1 light_rows=40 frame_p99_ms=9 godot_errors=0\n");
        run.Write("lifestone.2.log", "2026-10-28T17:00:00.000Z INFO  [log] log rolled rolls=1 previous=lifestone.1.log max_mb=16\n2026-10-28T17:01:00.000Z INFO  [soak] ws_mb=1001 objects=10 in_cell=1 light_rows=40 frame_p99_ms=9 godot_errors=0\n");
        run.Write("lifestone.1.log", "2026-10-28T18:00:00.000Z INFO  [log] log rolled rolls=2 previous=lifestone.1.log max_mb=16\n2026-10-28T18:01:00.000Z INFO  [soak] ws_mb=1002 objects=10 in_cell=1 light_rows=40 frame_p99_ms=9 godot_errors=0\n");
        run.Write("lifestone.log", "2026-10-28T19:00:00.000Z INFO  [log] log rolled rolls=3 previous=lifestone.1.log max_mb=16\n2026-10-28T19:01:00.000Z INFO  [soak] ws_mb=1003 objects=10 in_cell=1 light_rows=40 frame_p99_ms=9 godot_errors=0\n");

        var result = Pwsh("-File", RepositoryFiles.PathOf("tools", "soak-report.ps1"), "-RunFolder", run.Path);

        Assert.Contains("soak_lines=4 rolled=3 first_hour=whole", result.Output, StringComparison.Ordinal);
        Assert.Contains("minutes=180", result.Output, StringComparison.Ordinal); // 16:01 to 19:01: the oldest file was read first
    }

    [Fact]
    public void Soak_report_reads_n_a_when_there_is_no_log()
    {
        using var run = new TempFolder("20261028T160000Z");

        var result = Pwsh("-File", RepositoryFiles.PathOf("tools", "soak-report.ps1"), "-RunFolder", run.Path);

        Assert.Equal(0, result.ExitCode);
        foreach (var field in new[] { "minutes=n/a", "soak_lines=n/a", "log_mb=n/a", "done_ok=n/a", "cycles=n/a", "accounted=n/a", "ws_slope_mb_per_h=n/a", "objects_max=n/a", "in_cell_max=n/a", "light_rows_max=n/a", "light_rows_ok=n/a", "godot_errors=n/a", "p99_first_ms=n/a", "p99_last_ms=n/a", "reconnects=n/a", "kills=n/a", "deaths=n/a", "interrupted=n/a", "resumed=n/a", "hp_pct_avg=n/a", "mp_pct_min=n/a", "hit_max=n/a" })
        {
            Assert.Contains(field, result.Output, StringComparison.Ordinal);
        }

        Assert.Contains("no lifestone.log in the run folder", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Soak_report_only_reads_the_run_folder_and_writes_its_report()
    {
        var script = Script("tools", "soak-report.ps1");

        foreach (var verb in new[] { "Start-Process", "Stop-Process", "Kill(", "Remove-Item", "Invoke-Expression", "Copy-Item", "Set-ItemProperty", "Invoke-WebRequest" })
        {
            Assert.DoesNotContain(verb, script, StringComparison.Ordinal);
        }

        Assert.Single(Regex.Matches(script, @"WriteAllText\("));
        foreach (var name in new[] { "ws_mb", "objects", "in_cell", "light_rows", "frame_p99_ms", "godot_errors" })
        {
            Assert.Contains(name, script, StringComparison.Ordinal);
        }
    }

    private static string SyntheticLog()
    {
        var t0 = new DateTime(2026, 10, 28, 16, 0, 0, DateTimeKind.Utc);
        var log = new StringBuilder();
        string Stamp(DateTime t) => t.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        log.Append(Stamp(t0)).Append(" INFO  [soak] soak line on every_s=60\n");
        for (var i = 1; i <= 180; i++)
        {
            log.Append(Stamp(t0.AddMinutes(i * 2))).Append(CultureInfo.InvariantCulture, $" INFO  [soak] ws_mb={1000 + (i * 2)}.5 objects={50 + (i % 7)} in_cell=3 light_rows=40 frame_p99_ms={10 + (i % 3)} godot_errors=0 kills=n/a cycles=n/a\n");
        }

        log.Append("2026-10-28T16:10:00.000Z INFO  [autopilot] autopilot cycle n=1 state=ok why=x kills=5 xp=1 deaths=0 s=600\n");
        log.Append("2026-10-28T16:30:00.000Z WARN  [autopilot] reconnect: the session left the world (phase loggedoff) in the fight state, cycle 2; try 1 of 3 in 5 s\n");
        log.Append("2026-10-28T16:40:00.000Z INFO  [autopilot] autopilot host: interrupted by key; released 1 held; interrupted=1 resumed=0\n");
        log.Append("2026-10-28T16:50:00.000Z INFO  [autopilot] autopilot cycle n=2 state=ok why=x kills=6 xp=1 deaths=1 s=600\n");
        log.Append("2026-10-28T22:00:00.000Z INFO  [autopilot] autopilot cycle n=3 state=stopped why=x kills=1 s=60\n");
        log.Append("2026-10-28T22:00:00.000Z INFO  [autopilot] autopilot done ok=yes cycles=2 stop=minutes kills=12 xp=2 deaths=1 interrupted=1 resumed=1 reconnects=1 s=21000\n");
        return log.ToString();
    }

    [Fact]
    public void M5_7_G2_soak_sets_the_autopilot_checkpoint_aside_by_rename_unless_resume_and_plan_only_says_what_it_would_move()
    {
        using var folder = new TempFolder("autopilot");
        folder.Write("bsd-mage.json", "{\"minutes\":600}\n");
        var file = System.IO.Path.Combine(folder.Path, "bsd-mage.json");

        var kept = RunSoak("-SetAsideOnly", "-Resume", "-AutopilotFolder", folder.Path);
        Assert.Equal(0, kept.ExitCode);
        Assert.True(File.Exists(file));
        Assert.Contains("-Resume: the autopilot checkpoint is kept", kept.Output, StringComparison.Ordinal);

        using var main = new TempFolder();
        main.Write(Path.Combine("cache", "backend-owner.txt"), "the lead\n");
        var planned = RunSoak("-Minutes", "30", "-Plan", "-MainCheckout", main.Path, "-AutopilotFolder", folder.Path);
        Assert.Equal(0, planned.ExitCode);
        Assert.True(File.Exists(file)); // -Plan moves nothing
        Assert.Contains("would set " + file + " aside as", planned.Output, StringComparison.Ordinal);

        var moved = RunSoak("-SetAsideOnly", "-AutopilotFolder", folder.Path);
        Assert.Equal(0, moved.ExitCode);
        Assert.False(File.Exists(file));
        var aside = Assert.Single(Directory.GetFiles(folder.Path, "bsd-mage.*.aside"));
        Assert.Equal("{\"minutes\":600}\n", File.ReadAllText(aside)); // renamed, never deleted, never changed
        Assert.Contains("checkpoint set aside: " + file + " -> " + aside, moved.Output, StringComparison.Ordinal);

        var none = RunSoak("-SetAsideOnly", "-AutopilotFolder", folder.Path);
        Assert.Contains("none to set aside", none.Output, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(folder.Path, "bsd-mage.*.aside"));
    }

    [Fact]
    public void M5_7_G2_soak_starts_the_watcher_with_the_run_folder_and_prints_the_alarm_file_at_the_end()
    {
        var script = Script("tools", "soak.ps1");
        Assert.Contains("'-RunFolder', $runFolder", script, StringComparison.Ordinal);
        Assert.Contains("Join-Path $runFolder 'ALARM.txt'", script, StringComparison.Ordinal);
        Assert.Contains("[soak] ALARM.txt", script, StringComparison.Ordinal);
        Assert.Matches(@"(?s)New-Item -ItemType Directory -Force -Path \$runFolder.*Invoke-CheckpointAside.*\$hold = \$null", script); // the checkpoint is set aside before the game starts
    }

    [Fact]
    public void M5_7_G2_the_alarm_pass_writes_one_line_per_alarm_and_prints_it_and_a_healthy_run_writes_none()
    {
        using var folder = new TempFolder("run");
        var log = System.IO.Path.Combine(folder.Path, "lifestone.log");
        var start = new DateTime(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc);
        var text = new StringBuilder();
        for (var i = 0; i <= 31; i++)
        {
            var deaths = i >= 30 ? 1 : 0;
            var trips = i / 8; // 0 to 3 across the 30 minutes
            text.Append(CultureInfo.InvariantCulture, $"{start.AddMinutes(i):yyyy-MM-dd'T'HH:mm:ss.fff'Z'} INFO  [soak] ws_mb=900 kills=5 deaths={deaths} cycles=1 reconnects={(i >= 31 ? 1 : 0)} free_slots=2 trips={trips}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"{start.AddMinutes(20):yyyy-MM-dd'T'HH:mm:ss.fff'Z'} INFO  [autopilot] autopilot host: interrupted by MoveForward; released 0 held; interrupted=1 resumed=0\n");
        File.WriteAllText(log, text.ToString());

        var result = RunWatch(folder, log, expectGame: true);

        Assert.Equal(0, result.ExitCode);
        var alarms = File.ReadAllLines(System.IO.Path.Combine(folder.Path, "ALARM.txt"));
        foreach (var expected in new[] { "the game process ended", "the autopilot was interrupted", "kills stalled for 15 minutes", "a death: deaths=1", "trips in 30 minutes", "free_slots=2, under 3", "a reconnect" })
        {
            var line = Assert.Single(alarms, l => l.Contains(expected, StringComparison.Ordinal));
            Assert.Contains("[soak-watch] ALARM " + line.Split(" ALARM ", 2)[1], result.Output, StringComparison.Ordinal);
        }

        Assert.All(alarms, l => Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ ALARM ", l)); // the UTC time first

        using var healthy = new TempFolder("healthy");
        var okLog = System.IO.Path.Combine(healthy.Path, "lifestone.log");
        var ok = new StringBuilder();
        for (var i = 0; i <= 31; i++)
        {
            ok.Append(CultureInfo.InvariantCulture, $"{start.AddMinutes(i):yyyy-MM-dd'T'HH:mm:ss.fff'Z'} INFO  [soak] ws_mb=900 kills={i * 2} deaths=0 cycles=1 reconnects=0 free_slots=40 trips={i / 20}\n");
        }

        File.WriteAllText(okLog, ok.ToString());
        var quiet = RunWatch(healthy, okLog, expectGame: false);
        Assert.Equal(0, quiet.ExitCode);
        Assert.False(File.Exists(System.IO.Path.Combine(healthy.Path, "ALARM.txt")));
        Assert.DoesNotContain("ALARM", quiet.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void M5_7_G2_the_alarm_pass_watches_godots_working_set_over_3_GB_and_reads_the_log_without_changing_it()
    {
        var script = Script("tools", "soak-watch.ps1");
        Assert.Contains("$AlarmGodotWsMb = 3072", script, StringComparison.Ordinal);
        Assert.Contains("$AlarmStallMinutes = 15", script, StringComparison.Ordinal);
        Assert.Contains("$AlarmTripWindowMinutes = 30", script, StringComparison.Ordinal);
        Assert.Contains("$AlarmTripCount = 3", script, StringComparison.Ordinal);
        Assert.Contains("$AlarmFreeSlots = 3", script, StringComparison.Ordinal);
        Assert.Contains("[IO.FileShare]::ReadWrite", script, StringComparison.Ordinal); // the game keeps writing its log
        Assert.DoesNotContain("Remove-Item", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Stop-Process", script, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output) RunWatch(TempFolder run, string log, bool expectGame)
    {
        var arguments = new List<string>
        {
            "-File", RepositoryFiles.PathOf("tools", "soak-watch.ps1"), "-Once", "-OutFile", System.IO.Path.Combine(run.Path, "watch.csv"), "-RunFolder", run.Path, "-GameLog", log,
            "-GodotLogsDirectory", run.Path, "-ServerLogsDirectory", System.IO.Path.Combine(run.Path, "none-a"), "-DatabaseDirectory", System.IO.Path.Combine(run.Path, "none-b"),
            "-Since", "2000-01-01T00:00:00Z", "-GodotProcessPattern", "NoSuchGameProcessLifestoneTest*",
        };
        if (expectGame)
        {
            arguments.Add("-ExpectGame");
        }

        return Pwsh([.. arguments]);
    }

    private static string[] RunFolders()
    {
        var soak = RepositoryFiles.PathOf("cache", "soak");
        return Directory.Exists(soak) ? [.. Directory.GetDirectories(soak).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)] : [];
    }

    private static (int ExitCode, string Output) RunSoak(params string[] arguments) =>
        Pwsh([.. new[] { "-File", RepositoryFiles.PathOf("tools", "soak.ps1") }, .. arguments]);

    private static (int ExitCode, string Output) Pwsh(params string[] arguments)
    {
        foreach (var shell in new[] { "pwsh", "powershell" })
        {
            var info = new ProcessStartInfo(shell) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            try
            {
                using var process = Process.Start(info)!;
                var errorTask = process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEnd();
                Assert.True(process.WaitForExit(120_000), "PowerShell did not finish");
                return (process.ExitCode, output + errorTask.Result);
            }
            catch (Win32Exception)
            {
                // that shell is not installed: try the next
            }
        }

        throw new InvalidOperationException("neither pwsh nor powershell is installed");
    }

    /// <summary>A temporary folder (optionally with a given leaf name) that is removed on dispose.</summary>
    private sealed class TempFolder : IDisposable
    {
        private readonly string _parent = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lifestone-soak-runner-" + Guid.NewGuid().ToString("N"));

        public TempFolder(string leaf = "main")
        {
            Path = System.IO.Path.Combine(_parent, leaf);
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Write(string relative, string text)
        {
            var file = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }

        public void Dispose()
        {
            if (Directory.Exists(_parent))
            {
                Directory.Delete(_parent, recursive: true);
            }
        }
    }
}
