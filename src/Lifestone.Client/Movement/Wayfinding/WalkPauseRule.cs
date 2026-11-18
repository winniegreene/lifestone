using System.Text.RegularExpressions;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// What a process's command line says about pausing the world walk. A script counts when the line runs it, not when it only names it: a PowerShell command that reads or searches a gate script would otherwise
/// pause the walk for nothing (2026-11-06: 57 pauses for live gates in one run, each of seconds). A line that looks like a run counts even if it is only quoted text, as a needless pause costs seconds and a
/// missed gate can fail that gate.
/// </summary>
public static class WalkPauseRule
{
    /// <summary>The named reason to pause, or null.</summary>
    public static string? Reason(string commandLine, bool ignoreClimb)
    {
        if (Runs(commandLine, "live-gate.ps1"))
        {
            return "live-gate";
        }

        if (Runs(commandLine, "smoke.ps1"))
        {
            return "smoke";
        }

        if (Runs(commandLine, "soak.ps1"))
        {
            return "soak";
        }

        if (Runs(commandLine, "slice-check.ps1") && commandLine.Contains("-Gate", StringComparison.OrdinalIgnoreCase))
        {
            return "slice-check-gate";
        }

        if (commandLine.Contains("Category=Loopback", StringComparison.OrdinalIgnoreCase) || commandLine.Contains("Lifestone.Loopback.Tests", StringComparison.OrdinalIgnoreCase))
        {
            return "loopback-tests";
        }

        return !ignoreClimb && Runs(commandLine, "climb.ps1") ? "climb" : null;
    }

    /// <summary>The script is the file of -File, the shell's first argument, the start of -Command, or follows an operator that starts a command.</summary>
    private static bool Runs(string line, string script)
    {
        const string Path = """(?:"[^"]*|'[^']*|[^\s"']*)""";
        var name = Regex.Escape(script);
        return Regex.IsMatch(
            line,
            $"""-File\s+{Path}{name}|(?:-Command|-c)\s+["']?[^\s"']*{name}|[&;|(]\s*{Path}{name}|^"?[^"]*?(?:pwsh|powershell)(?:\.exe)?"?\s+(?:-No\w+\s+)*{Path}{name}""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
