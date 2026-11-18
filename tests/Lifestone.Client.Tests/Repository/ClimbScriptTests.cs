using System.ComponentModel;
using System.Diagnostics;

namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// M6 item 0 F5 (review 0-R3): tools/climb.ps1, the lead's driver of the climb (the creation, the climber journey, the level up job), and its pure parts in tools/lib/Climb.psm1. Tried with -PlanOnly (prints the three command lines, starts nothing,
/// reads no backend) and by calling the module on synthetic log lines; no backend, no game, no login.
/// </summary>
public sealed class ClimbScriptTests
{
    private static readonly string Module = RepositoryFiles.PathOf("tools", "lib", "Climb.psm1");

    [Fact]
    public void Plan_only_prints_the_three_command_lines_and_starts_nothing()
    {
        var plan = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly", "-Minutes", "90", "-TargetLevel", "12");

        Assert.Equal(0, plan.ExitCode);
        Assert.Contains("--scenario=climber-create --account=test --fresh-character", plan.Output, StringComparison.Ordinal);
        Assert.Contains("--scenario=journey --journey=climber --account=test", plan.Output, StringComparison.Ordinal);
        Assert.Contains("-Account test -Character Winnie -Offscreen -IsolateUserData -KeepServer -NoBuild -TimeoutSeconds 6300", plan.Output, StringComparison.Ordinal);
        Assert.Contains("\"-GameArgs:--autopilot-job=level-up --autopilot-start --autopilot-minutes=90 --autopilot-level=12 --max-fps=60 --mute --fight-loot=on\"", plan.Output, StringComparison.Ordinal);
        Assert.Contains("-PlanOnly: nothing was started", plan.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("REFUSED", plan.Output, StringComparison.Ordinal);

        var skipped = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly", "-SkipCreate");
        Assert.Equal(0, skipped.ExitCode);
        Assert.Equal(2, skipped.Output.Split('\n').Count(l => l.Contains("(skipped)", StringComparison.Ordinal)));
    }

    [Fact]
    public void Plan_only_without_record_shows_no_recording_and_no_sync_marker()
    {
        var plan = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly");

        Assert.Equal(0, plan.ExitCode);
        Assert.Contains("record: off", plan.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("--video-sync-marker", plan.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("ffmpeg", plan.Output, StringComparison.Ordinal);

        var strayDir = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly", "-RecordDir", "x");
        Assert.NotEqual(0, strayDir.ExitCode);
        Assert.Contains("read only with -Record", strayDir.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_only_with_record_names_the_directory_the_marker_and_the_ffmpeg_shape()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lifestone-climb-record-plan");
        var plan = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly", "-Record", "-RecordDir", dir, "-RecordSegmentSeconds", "600");

        Assert.Equal(0, plan.ExitCode);
        Assert.Contains("record: on", plan.Output, StringComparison.Ordinal);
        Assert.Contains(dir, plan.Output, StringComparison.Ordinal);
        Assert.Contains("segment_s=600", plan.Output, StringComparison.Ordinal);
        Assert.Contains("--max-fps=60 --mute --fight-loot=on --video-sync-marker --clean-hud --video-chat --video-timer=10 --video-cantrip-look\"", plan.Output, StringComparison.Ordinal); // M6 hero video: the screen of the cut (no diagnostic text, the taller chat, the clock to the target level, the cantrip looks)
        Assert.Contains("gfxcapture=hwnd=<hwnd>", plan.Output, StringComparison.Ordinal);
        Assert.Contains("-segment_time 600", plan.Output, StringComparison.Ordinal);
        Assert.Contains("rec-%03d.ts", plan.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(dir), "a plan creates no folder");
    }

    [Fact]
    public void M6_2_e_the_climber_loots_by_default_and_no_loot_leaves_it_off()
    {
        var on = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly");
        Assert.Equal(0, on.ExitCode);
        Assert.Contains("--max-fps=60 --mute --fight-loot=on\"", on.Output, StringComparison.Ordinal);
        Assert.Contains("loot=on", on.Output, StringComparison.Ordinal);

        var off = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly", "-NoLoot");
        Assert.Equal(0, off.ExitCode);
        Assert.DoesNotContain("--fight-loot", off.Output, StringComparison.Ordinal);
        Assert.Contains("loot=off (-NoLoot)", off.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_14_the_sprint_passes_the_quests_switch_and_no_buff_bot_reaches_the_creation_and_the_job()
    {
        var plain = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly");
        Assert.Equal(0, plain.ExitCode);
        Assert.DoesNotContain("--autopilot-ladder-quests", plain.Output, StringComparison.Ordinal); // the options' defaults: quests on, the bot on (its skill order at the creation)
        Assert.DoesNotContain("--autopilot-buff-bot", plain.Output, StringComparison.Ordinal);
        Assert.Contains("quests=on buff_bot=on", plain.Output, StringComparison.Ordinal);

        var sprint = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly", "-NoQuests", "-TargetLevel", "20");
        Assert.Equal(0, sprint.ExitCode);
        Assert.Contains("--autopilot-level=20 --max-fps=60 --mute --fight-loot=on --autopilot-ladder-quests=off\"", sprint.Output, StringComparison.Ordinal);
        Assert.Contains("--scenario=climber-create --account=test --fresh-character", sprint.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("--autopilot-buff-bot", sprint.Output, StringComparison.Ordinal); // the creation is in the bot's order by the option's default
        Assert.Contains("quests=off (-NoQuests, the sprint) buff_bot=on", sprint.Output, StringComparison.Ordinal);

        var alone = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly", "-NoBuffBot");
        Assert.Equal(0, alone.ExitCode);
        Assert.Contains("--scenario=climber-create --account=test --fresh-character --autopilot-buff-bot=off", alone.Output, StringComparison.Ordinal);
        Assert.Contains("--fight-loot=on --autopilot-buff-bot=off\"", alone.Output, StringComparison.Ordinal);
        Assert.Contains("quests=on buff_bot=off (-NoBuffBot)", alone.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_14_the_den_run_switch_passes_the_order_to_the_level_up_job_and_is_off_by_default()
    {
        var plain = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly");
        Assert.Equal(0, plain.ExitCode);
        Assert.DoesNotContain("--autopilot-den-run", plain.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("den_run=", plain.Output, StringComparison.Ordinal);

        var order = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly", "-SkipCreate", "-DenRun", "-TargetLevel", "40");
        Assert.Equal(0, order.ExitCode);
        Assert.Contains("--autopilot-level=40 --max-fps=60 --mute --fight-loot=on --autopilot-den-run\"", order.Output, StringComparison.Ordinal);
        Assert.Contains("buff_bot=on den_run=on (-DenRun: the order; the level rule is not read), steps 1-2 skipped (-SkipCreate)", order.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("--autopilot-den-run-rehearse", order.Output, StringComparison.Ordinal);

        // the rehearsal: the order with its road cut short of the Desert March Portal
        var rehearse = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly", "-SkipCreate", "-DenRunRehearse");
        Assert.Equal(0, rehearse.ExitCode);
        Assert.Contains(" --autopilot-den-run-rehearse\"", rehearse.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_14_the_quest_selection_passes_the_list_to_the_level_up_job_and_is_refused_beside_the_sprint()
    {
        var chosen = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly", "-SkipCreate", "-TargetLevel", "60", "-Quests", "tusks,tusker-guard-kill-task");
        Assert.Equal(0, chosen.ExitCode);
        Assert.Contains("--autopilot-level=60 --max-fps=60 --mute --fight-loot=on --autopilot-ladder-quests=tusks,tusker-guard-kill-task\"", chosen.Output, StringComparison.Ordinal);
        Assert.Contains(" quests=tusks,tusker-guard-kill-task (-Quests, the selection) ", chosen.Output, StringComparison.Ordinal);
        Assert.Equal(1, chosen.Output.Split('\n').Count(l => l.Contains("--autopilot-ladder-quests", StringComparison.Ordinal))); // the level up job's line only

        var both = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly", "-NoQuests", "-Quests", "tusks");
        Assert.Equal(1, both.ExitCode);
        Assert.Contains("[climb] REFUSED: -Quests is a selection and -NoQuests the sprint: give one of them", both.Output, StringComparison.Ordinal);

        var badList = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly", "-Quests", "Tusks, Guards");
        Assert.NotEqual(0, badList.ExitCode);
        Assert.Contains("-Quests: 'Tusks, Guards' is not a list of quest keys", badList.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_14_the_trial_switch_passes_the_ground_and_the_minutes_to_the_level_up_job_and_is_off_by_default()
    {
        var plain = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly");
        Assert.Equal(0, plain.ExitCode);
        Assert.DoesNotContain("--autopilot-trial", plain.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("[climb] trial:", plain.Output, StringComparison.Ordinal);

        var trial = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly", "-SkipCreate", "-NoQuests", "-TargetLevel", "60", "-TrialGround", "tusker-quarters", "-TrialMinutes", "45");
        Assert.Equal(0, trial.ExitCode);
        Assert.Contains("--autopilot-level=60 --max-fps=60 --mute --fight-loot=on --autopilot-ladder-quests=off --autopilot-trial-ground=tusker-quarters --autopilot-trial-minutes=45\"", trial.Output, StringComparison.Ordinal);
        Assert.Contains("[climb] trial: ground=tusker-quarters minutes=45 ", trial.Output, StringComparison.Ordinal);
        Assert.Equal(1, trial.Output.Split('\n').Count(l => l.Contains("--autopilot-trial-ground", StringComparison.Ordinal))); // the level up job's line only: not the creation's, not the journey's

        var byDefault = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly", "-SkipCreate", "-TrialGround", "tusker-quarters");
        Assert.Equal(0, byDefault.ExitCode);
        Assert.Contains(" --autopilot-trial-ground=tusker-quarters --autopilot-trial-minutes=30\"", byDefault.Output, StringComparison.Ordinal);

        // the den run is a task of its own: refused beside the trial, by name (the game would fail the run on the pair)
        var withDen = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly", "-SkipCreate", "-DenRun", "-TrialGround", "tusker-quarters");
        Assert.Equal(1, withDen.ExitCode);
        Assert.Contains("[climb] REFUSED: -TrialGround is the level up job's own loop", withDen.Output, StringComparison.Ordinal);

        var alone = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly", "-TrialMinutes", "45");
        Assert.Equal(1, alone.ExitCode);
        Assert.Contains("[climb] REFUSED: -TrialMinutes is read only with -TrialGround", alone.Output, StringComparison.Ordinal);

        var badKey = Pwsh("-File", RepositoryFiles.PathOf("tools", "climb.ps1"), "-PlanOnly", "-TrialGround", "Tusker Quarters");
        Assert.NotEqual(0, badKey.ExitCode);
        Assert.Contains("-TrialGround: 'Tusker Quarters' is not a ground key", badKey.Output, StringComparison.Ordinal);

        var catalog = string.Concat(Directory.GetFiles(RepositoryFiles.PathOf("src", "Lifestone.Client", "Options"), "OptionCatalog*.cs").Select(File.ReadAllText));
        foreach (var option in new[] { "--autopilot-trial-ground", "--autopilot-trial-minutes" })
        {
            Assert.Contains($"new(\"{option}\"", catalog, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void M6_2_e_the_verdict_adds_casters_from_the_level_up_logs_last_cycle_line_and_only_then()
    {
        var result = Pwsh(
            "-Command",
            "Import-Module '" + Module + "' -Force; "
            + "$text = \"autopilot cycle n=1 state=trip why=x kills=1 casters=1 s=5`n[ladder] at=done n=2 level=7 rung=holtburg-outskirts kills=9`nautopilot cycle n=2 state=stopped why=x kills=1 casters=2 s=5`nautopilot done ok=yes cycles=1 stop=level kills=9 deaths=0 granted=0 places=0\"; "
            + "$l = Get-ClimbLevelUpVerdict -ExitCode 0 -Text $text; Write-Output ('CASTERS=' + $l.Casters); "
            + "Write-Output (Format-ClimbVerdict -Create $null -Journey $null -LevelUp $l -Minutes 5); "
            + "$q = Get-ClimbLevelUpVerdict -ExitCode 0 -Text \"autopilot cycle n=2 state=stopped why=x casters=? s=5`n[ladder] at=done n=2 level=7 rung=r kills=9`nautopilot done ok=yes stop=level kills=9\"; "
            + "Write-Output ('UNREAD=' + ($null -eq $q.Casters)); Write-Output (Format-ClimbVerdict -Create $null -Journey $null -LevelUp $q -Minutes 5)");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("CASTERS=2", result.Output, StringComparison.Ordinal); // the LAST cycle line
        Assert.Contains("minutes=5 casters=2 skipped=create,journey", result.Output, StringComparison.Ordinal);
        Assert.Contains("UNREAD=True", result.Output, StringComparison.Ordinal); // '?' is unread, never 0
        Assert.Contains("minutes=5 skipped=create,journey", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_7_R_the_verdict_reports_the_health_at_each_level_the_log_reached()
    {
        var result = Pwsh(
            "-Command",
            "Import-Module '" + Module + "' -Force; "
            + "$text = \"[ladder] at=cycle n=1 level=6 rung=outskirts kills=0 turn_ins=0 health=17`nladder: level-up spend: level 7: trained=none spent_xp=9 raises=1 credits=2 next=n/a health=24 (mid-run)`nladder: level-up spend: level 7: trained=none spent_xp=9 raises=1 credits=2 next=n/a health=26 (the cycle's start)`nladder: level-up spend: level 10: trained=none spent_xp=9 raises=1 credits=2 next=n/a health=? (mid-run)`nautopilot done ok=yes cycles=1 stop=level kills=9 deaths=0 granted=0 places=0`n[ladder] at=done n=2 level=10 rung=burrow kills=9 turn_ins=0 health=41 reason=x\"; "
            + "$l = Get-ClimbLevelUpVerdict -ExitCode 0 -Text $text; Write-Output (Format-ClimbVerdict -Create $null -Journey $null -LevelUp $l -Minutes 5); "
            + "$q = Get-ClimbLevelUpVerdict -ExitCode 0 -Text \"[ladder] at=done n=2 level=7 rung=r kills=9 health=?`nautopilot done ok=yes stop=level kills=9\"; "
            + "Write-Output ('NONE=[' + $q.HealthByLevel + ']'); Write-Output (Format-ClimbVerdict -Create $null -Journey $null -LevelUp $q -Minutes 5)");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("minutes=5 health_by_level=6:17,7:26,10:41 skipped=create,journey", result.Output, StringComparison.Ordinal); // the last read of a level wins; '?' is unread, in level order
        Assert.Contains("NONE=[]", result.Output, StringComparison.Ordinal);
        Assert.Contains("minutes=5 skipped=create,journey", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_options_the_third_step_passes_are_registered()
    {
        var catalog = string.Concat(Directory.GetFiles(RepositoryFiles.PathOf("src", "Lifestone.Client", "Options"), "OptionCatalog*.cs").Select(File.ReadAllText));
        foreach (var option in new[] { "--autopilot-job", "--autopilot-start", "--autopilot-minutes", "--autopilot-level", "--max-fps", "--mute", "--autopilot-ladder-quests", "--autopilot-buff-bot" })
        {
            Assert.Contains($"new(\"{option}\"", catalog, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Step_three_is_refused_unless_the_two_earlier_lines_read_zero_and_the_sum_follows_the_logs()
    {
        var result = Pwsh(
            "-Command",
            $"Import-Module '{Module}' -Force; "
            + "$c = Get-ClimbCreateVerdict -ExitCode 0 -Text \"x`n2026-11-01T00:00:00Z INFO [climber] verdict fresh=recreated purged=1 granted=0 template=ok level=1`nheadless done scenario=climber-create ok=1 corrections=0\"; "
            + "$j = Get-ClimbJourneyVerdict -ExitCode 0 -Text \"journey done ok=true name=climber stations=2/2`njourney climber cheat_free=yes level=4 granted=0 place=0 recovery_place=0 stop=none\"; "
            + "Write-Output ('GATE=' + (Test-ClimbMayLevelUp -Create $c -Journey $j).Allowed); "
            + "$bad = Get-ClimbJourneyVerdict -ExitCode 0 -Text \"journey done ok=true name=climber`njourney climber cheat_free=no level=4 granted=1 place=0 recovery_place=2 stop=none\"; "
            + "$g = Test-ClimbMayLevelUp -Create $c -Journey $bad; Write-Output ('BAD=' + $g.Allowed + ' ' + $g.Why); "
            + "Write-Output ('UNREAD=' + (Test-ClimbMayLevelUp -Create $c -Journey (Get-ClimbJourneyVerdict -ExitCode 0 -Text \"journey done ok=true`njourney climber cheat_free=yes level=4\")).Allowed); "
            + "$l = Get-ClimbLevelUpVerdict -ExitCode 0 -Text \"[ladder] at=cycle n=1 level=5 rung=holtburg-outskirts kills=3`n[ladder] at=done n=2 level=7 rung=holtburg-outskirts kills=9`nautopilot done ok=yes cycles=2 stop=level kills=9 xp=1 deaths=1 trips=0 granted=0 places=0\"; "
            + "Write-Output (Format-ClimbVerdict -Create $c -Journey $j -LevelUp $l -Minutes 61.26); "
            + "Write-Output (Format-ClimbVerdict -Create $null -Journey $null -LevelUp $l -Minutes 5); "
            + "Write-Output ('RC=' + (Get-ClimbLevelUpVerdict -ExitCode 0 -Text \"autopilot travel: check after the rung change: present in a`nx`nautopilot travel: check after the rung change: present in b\").RungChanges)");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("GATE=True", result.Output, StringComparison.Ordinal);
        Assert.Contains("BAD=False step 2 read granted=1, not 0; step 2 read recovery_place=2, not 0; step 2 read cheat_free=no, not yes", result.Output, StringComparison.Ordinal);
        Assert.Contains("UNREAD=False", result.Output, StringComparison.Ordinal);
        Assert.Contains("[climb] verdict level_start=4 level_end=7 rung=holtburg-outskirts kills=9 deaths=1 granted=0 place=0 recovery_place=0 minutes=61.3", result.Output, StringComparison.Ordinal);
        Assert.Contains("[climb] verdict level_start=n/a level_end=7 rung=holtburg-outskirts kills=9 deaths=1 granted=0 place=0 recovery_place=0 minutes=5 skipped=create,journey", result.Output, StringComparison.Ordinal);
        Assert.Contains("skipped=create,journey rung_changes=0", result.Output, StringComparison.Ordinal); // M6 7 AC: counted from the log, report-only; no change in this log
        Assert.Contains("RC=2", result.Output, StringComparison.Ordinal); // two arrivals after a rung change
    }

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
}
