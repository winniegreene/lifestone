using System.ComponentModel;
using System.Diagnostics;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The smoke script's 50 ms flags (M3 4.2 x3, cluster-k7 gap 3): <c>Get-FrameFlags</c> (the function itself, cut out of tools/smoke.ps1 and run in PowerShell) reads a leg's verdict line and gives the
/// summary the <c>frame_over_50=</c> flag and every window's <c>*_frame_max_ms</c>, and tells whether the step reads <c>pass (frame over 50 ms)</c>. The fixtures are lines the legs' own composers print.
/// </summary>
public sealed class SmokeFrameFlagsTests
{
    private static string Smoke => File.ReadAllText(RepositoryFiles.PathOf("tools", "smoke.ps1"));

    [Fact]
    public void A_fight_line_over_50_ms_is_flagged_with_both_window_maxima()
    {
        var (over, suffix) = Flags("bsd-fight", "[logincheck] bsd-fight done ok=true kills=1 damage_dealt=40 spells_cast=3 misses=0 xp_gained=10 damage_taken=0 hits_taken=0 evades=0 projectiles=3 cast_frame_max_ms=61.2 fight_frame_max_ms=33.0 bsd_min_fps=30.1 fight_s=44 frame_over_50=yes");

        Assert.True(over);
        Assert.Equal("; frame_over_50=yes cast_frame_max_ms=61.2 fight_frame_max_ms=33.0", suffix);
    }

    [Fact]
    public void A_line_under_50_ms_says_no_and_still_prints_the_maxima()
    {
        var (over, suffix) = Flags("bsd-fight", "[logincheck] bsd-fight done ok=true kills=1 cast_frame_max_ms=21.4 fight_frame_max_ms=n/a bsd_min_fps=60.0 fight_s=44");

        Assert.False(over);
        Assert.Equal("; frame_over_50=no cast_frame_max_ms=21.4 fight_frame_max_ms=n/a", suffix);
    }

    [Fact]
    public void The_buff_and_death_lines_are_read_by_their_own_prefix()
    {
        var buffLine = "[logincheck] buff done ok=true gear=wielded cast_scripts=12 cast_frame_max_ms=55.5 sounds_server=3 surpassed=0 cast_scripts_expected=30 frame_over_50=yes";
        var deathLine = "[logincheck] bsd-death done ok=true died=1 death_frame_max_ms=40.0 fight_frame_max_ms=22.5 deaths=1";

        Assert.Equal((true, "; frame_over_50=yes cast_frame_max_ms=55.5"), Flags("buff", buffLine));
        Assert.Equal((false, "; frame_over_50=no death_frame_max_ms=40.0 fight_frame_max_ms=22.5"), Flags("bsd-death", deathLine));
        Assert.Equal((false, string.Empty), Flags("bsd-fight", buffLine)); // another leg's line is not this step's
    }

    [Fact]
    public void The_summary_wiring_marks_the_passthe_composers_print_what_it_reads()
    {
        var script = Smoke;

        // Each of the three steps passes the flag as the pass note, so the summary reads PASS (frame over 50 ms) and the detail carries the maxima.
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(script, @"\(\$verdict \+ \$flags\.Suffix \+ \$live\.Note\).*elseif \(\$flags\.Over50\) \{ 'frame over 50 ms' \}").Count);
    }

    private static (bool Over50, string Suffix) Flags(string prefix, string text)
    {
        var script = Smoke;
        var start = script.IndexOf("function Get-FrameFlags", StringComparison.Ordinal);
        Assert.True(start >= 0, "tools/smoke.ps1 has no Get-FrameFlags");
        var end = script.IndexOf("\n}\n", start, StringComparison.Ordinal);
        var function = script[start..(end + 3)];
        var command = function + "\n$r = Get-FrameFlags '" + text.Replace("'", "''", StringComparison.Ordinal) + "' '" + prefix + "'\nWrite-Output (\"OVER=\" + $r.Over50)\nWrite-Output (\"SUFFIX=\" + $r.Suffix)\n";
        var run = RunPowerShell(command);
        var over = System.Text.RegularExpressions.Regex.Match(run, @"^OVER=(True|False)\s*$", System.Text.RegularExpressions.RegexOptions.Multiline);
        var suffix = System.Text.RegularExpressions.Regex.Match(run, @"^SUFFIX=(.*?)\r?$", System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.True(over.Success && suffix.Success, "Get-FrameFlags printed nothing readable:\n" + run);
        return (over.Groups[1].Value == "True", suffix.Groups[1].Value);
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
