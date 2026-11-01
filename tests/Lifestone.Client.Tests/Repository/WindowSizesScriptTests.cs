using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// M5 6 v1 (D9, D34; DoD 12): the sizes the shell frames and the play-checks run at. <c>-AllSizes</c> is 800x600, 1280x720 and 1920x1080 for both; the 2560x1440 scale-2 shell run is <c>-HiDpi</c>'s alone; the
/// play-checks step runs once per size through <c>tools\godot.ps1 -Resolution</c> and judges the window it was given; the item-end check (<c>slice-check -Smoke</c>) implies <c>-AllSizes</c>. They read
/// tools/smoke.ps1, tools/slice-check.ps1 and tools/live-gate.ps1 (text) and run the size lists and the per-size judge in PowerShell, cut out of the script; no Godot, no backend and no dats.
/// </summary>
public sealed class WindowSizesScriptTests
{
    private const string CameraLine = "INFO  [camera] the world camera draws with retail's field of view (Render.FieldOfView 90, SmartBox::RenderNormalMode) vertical_fov_degrees=53.642 window=\"({0}, {1})\" was=60";

    private static string Smoke => File.ReadAllText(RepositoryFiles.PathOf("tools", "smoke.ps1"));

    private static string SliceCheck => File.ReadAllText(RepositoryFiles.PathOf("tools", "slice-check.ps1"));

    private static string LiveGate => File.ReadAllText(RepositoryFiles.PathOf("tools", "live-gate.ps1"));

    private static string Line(string script, string startsWith)
    {
        var match = Regex.Match(script, "^" + Regex.Escape(startsWith) + @"[^\r\n]*", RegexOptions.Multiline);
        Assert.True(match.Success, "tools script has no line starting " + startsWith);
        return match.Value;
    }

    private static string Function(string script, string name)
    {
        var start = script.IndexOf("function " + name, StringComparison.Ordinal);
        Assert.True(start >= 0, "tools script has no function " + name);
        var end = Regex.Match(script[start..], @"\r?\n\}\r?\n");
        Assert.True(end.Success, "no end of function " + name);
        return script[start..(start + end.Index + end.Length)];
    }

    private static string SizeLists(bool allSizes, bool hiDpi)
    {
        var script = Smoke;
        var lines = string.Join("\n", Line(script, "$gateSizes ="), Line(script, "$shellFrameSizes ="), Line(script, "$playCheckSizes ="));
        var command = $"$AllSizes = [switch]${allSizes.ToString().ToLowerInvariant()}; $HiDpi = [switch]${hiDpi.ToString().ToLowerInvariant()}; {lines}; 'shell=' + ($shellFrameSizes -join ','); 'play=' + ($playCheckSizes -join ',')";
        return RunPowerShell(command).Replace("\r", string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, "800x600", "800x600")]
    [InlineData(true, false, "800x600,1280x720,1920x1080", "800x600,1280x720,1920x1080")]
    [InlineData(false, true, "800x600,2560x1440", "800x600")]
    [InlineData(true, true, "800x600,1280x720,1920x1080,2560x1440", "800x600,1280x720,1920x1080")]
    public void The_sizes_of_the_shell_frames_and_the_play_checks_follow_AllSizes_and_HiDpi(bool allSizes, bool hiDpi, string shell, string play)
    {
        var lists = SizeLists(allSizes, hiDpi);

        Assert.Contains("shell=" + shell + "\n", lists, StringComparison.Ordinal);
        Assert.Contains("play=" + play + "\n", lists, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_scripts_take_HiDpi_and_slice_check_passes_it_and_AllSizes_through_and_the_item_end_smoke_implies_AllSizes()
    {
        Assert.Contains("[switch] $HiDpi,", Smoke, StringComparison.Ordinal);
        Assert.Contains(".PARAMETER HiDpi", Smoke, StringComparison.Ordinal);
        Assert.Contains("[switch] $HiDpi,", SliceCheck, StringComparison.Ordinal);
        Assert.Contains("@(if ($AllSizes) { '-AllSizes' }) + @(if ($HiDpi) { '-HiDpi' })", SliceCheck, StringComparison.Ordinal);
        Assert.Contains("if ($Smoke -and -not $AllSizes) { $AllSizes = [switch]::new($true) }", SliceCheck, StringComparison.Ordinal);
        Assert.Contains("-HiDpi passes smoke's -HiDpi", SliceCheck, StringComparison.Ordinal);
        Assert.Contains("with -AllSizes from M5 item 6", LiveGate, StringComparison.Ordinal); // the gate's doc names the item-end smoke it asks for
    }

    [Fact]
    public void The_play_checks_step_runs_once_per_size_through_godot_resolution_and_the_frame_judge_stays_at_the_default_size()
    {
        var script = Smoke;
        var play = script[script.IndexOf("'play-checks' {", StringComparison.Ordinal)..];
        play = play[..play.IndexOf("'record-audio' {", StringComparison.Ordinal)];

        Assert.Contains("foreach ($size in $playCheckSizes)", play, StringComparison.Ordinal);
        Assert.Contains("Invoke-Godot $step '--play-checks' 420 \"-$size\" $size", play, StringComparison.Ordinal); // -Resolution WxH and a log suffix per size
        Assert.Contains("Invoke-Godot $step '--play-checks' 420 '' $defaultSize", play, StringComparison.Ordinal);   // the 800x600 run is unchanged
        Assert.Contains("Wait-SoloEngine $step $timer", play, StringComparison.Ordinal);

        // The frame judge (login-check, login) stays at 800x600 (M2 D57).
        foreach (var step in new[] { "'login-check' {", "'login-throwaway' {" })
        {
            var block = script[script.IndexOf(step, StringComparison.Ordinal)..];
            block = block[..Math.Min(block.Length, 700)];
            Assert.Contains("$defaultSize", block, StringComparison.Ordinal);
            Assert.DoesNotContain("$playCheckSizes", block, StringComparison.Ordinal);
        }

        // The shell frames (which already took a size) take the list of -AllSizes and -HiDpi.
        Assert.Contains("foreach ($size in $shellFrameSizes)", script, StringComparison.Ordinal);
        Assert.Contains("--play-checks --play-frames=$dir", script, StringComparison.Ordinal);
    }

    private static string Judge(string logText, string size, int code, bool large = false, string frameTimeChecks = "@()")
    {
        var script = Smoke;
        var work = Path.Combine(Path.GetTempPath(), "lifestone-sizes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var functions = string.Join("\n", Function(script, "Get-ErrorCount"), Function(script, "Get-PlayCheckFailures"), Function(script, "Get-FrameVerdict"), Function(script, "Get-PlayChecksSizeVerdict"));
            var text = logText.Replace("'", "''", StringComparison.Ordinal);
            var command = $"$OutDir = '{work.Replace("'", "''", StringComparison.Ordinal)}'; $SoloFrames = $false; $parallelPhase = $false; $frameTimeChecks = {frameTimeChecks}; {functions}; " +
                $"$run = @{{ Code = {code}; Log = 'C:\\x\\05-play-checks-{size}.log'; Text = '{text}'; Others = 0 }}; $v = Get-PlayChecksSizeVerdict $run '{size}' $false 0 ${large.ToString().ToLowerInvariant()}; " +
                $"'ok=' + $v.Ok; 'detail=' + $v.Detail; 'errors=' + $v.Errors; 'file=' + (Test-Path (Join-Path $OutDir 'play-checks\\{size}\\play-checks.txt')); 'result=' + (Get-Content -Raw (Join-Path $OutDir 'play-checks\\{size}\\play-checks.txt'))";
            return RunPowerShell(command).Replace("\r", string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, recursive: true);
            }
        }
    }

    [Fact]
    public void A_size_run_passes_when_the_checks_hold_in_the_window_asked_for_and_leaves_its_result_in_its_own_folder()
    {
        var log = string.Format(CameraLine, 1280, 720) + "\n[playcheck] PASS a\n[playcheck] PASS b\nall play checks hold max_frame_ms_after_arrival=21.3\n";
        var result = Judge(log, "1280x720", 0);

        Assert.Contains("ok=True", result, StringComparison.Ordinal);
        Assert.Contains("1280x720 pass=2 fail=0 max_frame_ms=21.3", result, StringComparison.Ordinal);
        Assert.Contains("file=True", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1264, 720)] // a decorated window clamped by the desktop's work area
    [InlineData(800, 600)]  // the size argument was lost
    public void A_size_run_fails_when_the_window_was_not_the_size_asked_for(int width, int height)
    {
        var log = string.Format(CameraLine, width, height) + "\n[playcheck] PASS a\nall play checks hold max_frame_ms_after_arrival=21.3\n";
        var result = Judge(log, "1280x720", 0);

        Assert.Contains("ok=False", result, StringComparison.Ordinal);
        Assert.Contains($"FAIL: the window was {width}x{height}, not the 1280x720 asked for", result, StringComparison.Ordinal);
    }

    [Fact]
    public void A_size_run_fails_without_the_cameras_line_and_when_a_check_failed()
    {
        var noLine = Judge("[playcheck] PASS a\nall play checks hold\n", "1920x1080", 0);
        var failed = Judge(string.Format(CameraLine, 1920, 1080) + "\n[playcheck] FAIL some-check detail=x\nPLAY CHECKS FAILED\n", "1920x1080", 1);

        Assert.Contains("ok=False", noLine, StringComparison.Ordinal);
        Assert.Contains("the window was not reported", noLine, StringComparison.Ordinal);
        Assert.Contains("ok=False", failed, StringComparison.Ordinal);
        Assert.Contains("1920x1080 pass=0 fail=1", failed, StringComparison.Ordinal);
    }

    [Fact]
    public void The_large_sizes_are_judged_from_item_7s_gate_except_their_frame_time_rows_and_the_default_size_judges_them_all_M5_D36()
    {
        // The real list of frame-time rows is read from the script (6-R7), not an empty one.
        var from = Smoke.IndexOf("$frameTimeChecks = @(", StringComparison.Ordinal);
        var list = Smoke[(from + "$frameTimeChecks = ".Length)..];
        list = list[..(list.IndexOf(')', StringComparison.Ordinal) + 1)].Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        Assert.Contains("'no-ui-step-after-enter-is-over-12-ms'", list, StringComparison.Ordinal);
        const string FrameRow = "[playcheck] FAIL no-ui-step-after-enter-is-over-12-ms\n";
        const string ErrorLine = "2026-10-25T00:00:00Z ERROR [playcheck] x\n";
        var good = string.Format(CameraLine, 1920, 1080) + "\n";

        // A 1920x1080 run whose window was wrong, with a failed check, a failed frame-time check and an ERROR line: JUDGED, so it fails, with the frame-time row counted apart.
        var bad = string.Format(CameraLine, 1264, 720) + "\n" + FrameRow + "[playcheck] FAIL some-check\n" + ErrorLine + "PLAY CHECKS FAILED\n";
        var failed = Judge(bad, "1920x1080", 1, large: true, frameTimeChecks: list);
        Assert.Contains("ok=False", failed, StringComparison.Ordinal);
        Assert.Contains("errors=1", failed, StringComparison.Ordinal); // not excused: a failed row besides the frame-time one
        Assert.Contains("frame_time_rows=1 (report-only above 800x600)", failed, StringComparison.Ordinal);
        Assert.Contains("FAIL: the window was 1264x720, not the 1920x1080 asked for", failed, StringComparison.Ordinal);
        Assert.Contains("FAIL: failed_rows=1 [some-check]", failed, StringComparison.Ordinal);
        Assert.Contains("judged=yes", failed, StringComparison.Ordinal);
        Assert.DoesNotContain("judged=no", failed, StringComparison.Ordinal);
        Assert.DoesNotContain("would_fail", failed, StringComparison.Ordinal);
        Assert.Contains("ok=False", failed[failed.IndexOf("result=", StringComparison.Ordinal)..], StringComparison.Ordinal);

        // A large-size failed row that is not a frame-time row fails the step on its own.
        var otherRow = Judge(good + "[playcheck] PASS a\n[playcheck] FAIL some-check\nPLAY CHECKS FAILED\n", "1920x1080", 1, large: true, frameTimeChecks: list);
        Assert.Contains("ok=False", otherRow, StringComparison.Ordinal);
        Assert.Contains("failed_rows=1 [some-check]", otherRow, StringComparison.Ordinal);

        // 7-R1: a run killed by the timeout (no "PLAY CHECKS FAILED" verdict line) after an early frame-time row is not a pass.
        var killed = Judge(good + "[playcheck] PASS a\n" + FrameRow + ErrorLine, "1920x1080", 1, large: true, frameTimeChecks: list);
        Assert.Contains("ok=False", killed, StringComparison.Ordinal);

        // A large-size run that only failed frame-time rows (and logged their ERROR line) passes: they are report-only above 800x600, counted in frame_time_rows, and their ERROR lines are theirs.
        var frameOnly = Judge(good + "[playcheck] PASS a\n" + FrameRow + ErrorLine + "PLAY CHECKS FAILED\n", "1920x1080", 1, large: true, frameTimeChecks: list);
        Assert.Contains("ok=True", frameOnly, StringComparison.Ordinal);
        Assert.Contains("errors=0", frameOnly, StringComparison.Ordinal);
        Assert.Contains("frame_time_rows=1 (report-only above 800x600)", frameOnly, StringComparison.Ordinal);
        Assert.Contains("judged=yes", frameOnly, StringComparison.Ordinal);
        var frameOnlyFile = frameOnly[frameOnly.IndexOf("result=", StringComparison.Ordinal)..];
        Assert.Contains("ok=True", frameOnlyFile, StringComparison.Ordinal);
        Assert.Contains("frame_time_rows=1 (report-only above 800x600)", frameOnlyFile, StringComparison.Ordinal);

        // A run that logged an ERROR line of its own besides the frame-time row's still fails on errors (godot_errors is judged by the row at every size).
        var extraError = Judge(good + FrameRow + ErrorLine + ErrorLine + "PLAY CHECKS FAILED\n", "1920x1080", 1, large: true, frameTimeChecks: list);
        Assert.Contains("errors=1", extraError, StringComparison.Ordinal);

        // A clean large-size run passes and says so; a run with no verdict at all (no rows, no "all play checks hold") fails.
        var clean = Judge(good + "[playcheck] PASS a\nall play checks hold max_frame_ms_after_arrival=30\n", "1920x1080", 0, large: true, frameTimeChecks: list);
        Assert.Contains("ok=True", clean, StringComparison.Ordinal);
        Assert.Contains("frame_time_rows=0 (report-only above 800x600)", clean, StringComparison.Ordinal);
        var silent = Judge(good, "1920x1080", 1, large: true, frameTimeChecks: list);
        Assert.Contains("ok=False", silent, StringComparison.Ordinal);

        // The same log judged the default size's way: every row counts, the frame-time row too.
        var judged = Judge(bad, "1920x1080", 1, large: false, frameTimeChecks: list);
        Assert.Contains("ok=False", judged, StringComparison.Ordinal);
        Assert.Contains("judged=yes", judged, StringComparison.Ordinal);
        var defaultFrame = Judge(string.Format(CameraLine, 800, 600) + "\n[playcheck] PASS a\n" + FrameRow + "PLAY CHECKS FAILED\n", "800x600", 1, large: false, frameTimeChecks: list);
        Assert.Contains("ok=False", defaultFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("frame_time_rows", defaultFrame, StringComparison.Ordinal);

        // The script judges exactly the gate sizes as large sizes and passes the flag only for them; the retail-ui frames do the same; nothing is report-only any more.
        var script = Smoke;
        Assert.Contains("$largeSizes = $gateSizes", script, StringComparison.Ordinal);
        Assert.Contains("Get-PlayChecksSizeVerdict $extra $size $false 0 ($largeSizes -contains $size)", script, StringComparison.Ordinal);
        Assert.Contains("Get-PlayChecksSizeVerdict $run $defaultSize ([bool]$shared) $recordingErrors", script, StringComparison.Ordinal); // 800x600: no flag, every row judged
        Assert.Contains("if ($largeSizes -contains $size) {", script, StringComparison.Ordinal);
        Assert.DoesNotContain("reportOnlySizes", script, StringComparison.Ordinal);
        Assert.DoesNotContain("judged=no", script, StringComparison.Ordinal);
        Assert.DoesNotContain("would_fail", script, StringComparison.Ordinal);
        Assert.Contains("JUDGED from M5 item 7's gate", script, StringComparison.Ordinal); // the help says so
        Assert.Contains("frame_time_rows=<n> (report-only above 800x600)", script, StringComparison.Ordinal);
        Assert.Contains("judged from M5 item 7's gate", LiveGate, StringComparison.Ordinal);
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
