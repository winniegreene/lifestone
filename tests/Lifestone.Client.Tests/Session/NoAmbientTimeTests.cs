using System.Text.RegularExpressions;
using Lifestone.Client.Tests.Repository;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The session layer's clock rule (A0's R5; docs/session.md §3), scanned in the sources with comments and literals
/// blanked: nothing under src/Lifestone.Client/Session/ reads time except through the <see cref="TimeProvider"/> the
/// host gave the session (<c>SessionClock.cs</c> is the one file that reads it). So a test or a replay that moves a
/// manual provider moves every timeout, handle time and recording time, and replays are deterministic.
/// ArchitectureTests already forbids the wall clock and sleeps there; this adds the ambient timers: the system
/// provider, delays and timers built without a provider, and cancellation after a delay.
/// </summary>
public sealed partial class NoAmbientTimeTests
{
    private const string SessionFolder = "src/Lifestone.Client/Session/";

    /// <summary>Wall-clock reads and sleeps (as ArchitectureTests).</summary>
    [GeneratedRegex(@"\bDateTime(?:Offset)?\s*\.\s*(?:UtcNow|Now|Today)\b|\bEnvironment\s*\.\s*TickCount(?:64)?\b|\bStopwatch\b|\bThread\s*\.\s*Sleep\b")]
    private static partial Regex WallClock();

    /// <summary>The system provider: hosts pass it in; the session never picks it itself.</summary>
    [GeneratedRegex(@"\bTimeProvider\s*\.\s*System\b")]
    private static partial Regex SystemProvider();

    /// <summary>Calls whose timing needs a <see cref="TimeProvider"/> argument to follow the session's clock.</summary>
    [GeneratedRegex(@"\bTask\s*\.\s*Delay\s*\(|\bnew\s+(?:System\s*\.\s*Threading\s*\.\s*)?(?:Timer|PeriodicTimer|CancellationTokenSource)\s*\(")]
    private static partial Regex TimedCall();

    /// <summary>A delay that always runs on the system timer.</summary>
    [GeneratedRegex(@"\.\s*CancelAfter\s*\(")]
    private static partial Regex CancelAfter();

    [Fact]
    public void The_scan_sees_the_session_sources()
    {
        var files = SessionSources().ToArray();
        Assert.True(files.Length > 20, $"only {files.Length} session sources found; did the folder move?");
        Assert.Contains(SessionFolder + "Contracts/SessionClock.cs", CheckoutFiles.CSharpUnder(SessionFolder));
        Assert.DoesNotContain(files, f => f.EndsWith("/SessionClock.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void No_ambient_clock_in_Session_sources()
    {
        var offences = new List<string>();
        foreach (var file in SessionSources())
        {
            offences.AddRange(Offences(file, CheckoutFiles.ReadCSharpCode(file)));
        }

        Assert.True(
            offences.Count == 0,
            "ambient time in src/Lifestone.Client/Session (take the session's TimeProvider; SessionClock.cs is the one reader):\n" + string.Join('\n', offences));
    }

    [Theory]
    [InlineData("var t = DateTime.UtcNow;", true)]
    [InlineData("var t = DateTimeOffset.Now;", true)]
    [InlineData("var s = Stopwatch.GetTimestamp();", true)]
    [InlineData("Thread.Sleep(5);", true)]
    [InlineData("var p = TimeProvider.System;", true)]
    [InlineData("await Task.Delay(500);", true)]
    [InlineData("await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);", true)]
    [InlineData("await Task.Delay(TimeSpan.FromSeconds(1), _time);", false)]
    [InlineData("await Task.Delay(delay, Clock.Time, cancellationToken);", false)]
    [InlineData("using var t = new Timer(Tick, null, 0, 100);", true)]
    [InlineData("using var t = new PeriodicTimer(TimeSpan.FromSeconds(1));", true)]
    [InlineData("using var t = new PeriodicTimer(TimeSpan.FromSeconds(1), time);", false)]
    [InlineData("using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));", true)]
    [InlineData("using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5), _time);", false)]
    [InlineData("using var cts = new CancellationTokenSource();", false)]
    [InlineData("cts.CancelAfter(1000);", true)]
    [InlineData("var t = _time.GetTimestamp(); // not Stopwatch, never DateTime.UtcNow", false)]
    [InlineData("var s = \"Task.Delay(5)\";", false)]
    public void The_rules_see_code_not_comments_or_strings(string line, bool offends)
    {
        Assert.Equal(offends, Offences("sample.cs", CSharpText.CodeOnly(line)).Count > 0);
    }

    private static IEnumerable<string> SessionSources() =>
        CheckoutFiles.CSharpUnder(SessionFolder).Where(p => !p.EndsWith("/SessionClock.cs", StringComparison.Ordinal));

    /// <summary>Every rule broken in one file's code (comments and literals already blanked), as <c>path:line: token</c>.</summary>
    private static List<string> Offences(string file, string code)
    {
        var offences = new List<string>();
        foreach (var rule in new[] { WallClock(), SystemProvider(), CancelAfter() })
        {
            foreach (Match m in rule.Matches(code))
            {
                offences.Add($"{file}:{CheckoutFiles.LineOf(code, m.Index)}: {m.Value.Trim()}");
            }
        }

        foreach (Match m in TimedCall().Matches(code))
        {
            if (!NamesATimeProvider(Arguments(code, m.Index + m.Length)))
            {
                offences.Add($"{file}:{CheckoutFiles.LineOf(code, m.Index)}: {m.Value.Trim()} without the session's TimeProvider");
            }
        }

        return offences;
    }

    /// <summary>
    /// True when an argument after the first names a time provider: an identifier containing <c>time</c> or
    /// <c>clock</c> (the session passes <c>_time</c> or <c>Clock.Time</c>; a cancellation token or a state object does
    /// not qualify). A timer or source with no arguments at all runs no clock.
    /// </summary>
    private static bool NamesATimeProvider(IReadOnlyList<string> arguments) =>
        arguments.Count == 0
        || (arguments.Count > 1 && arguments.Skip(1).Any(a =>
            (a.Contains("time", StringComparison.OrdinalIgnoreCase) && !a.Contains("TimeSpan", StringComparison.Ordinal))
            || a.Contains("clock", StringComparison.OrdinalIgnoreCase)));

    /// <summary>The top-level arguments of the call whose argument list starts at <paramref name="start"/>.</summary>
    private static List<string> Arguments(string code, int start)
    {
        var arguments = new List<string>();
        var depth = 0;
        var from = start;
        for (var i = start; i < code.Length; i++)
        {
            switch (code[i])
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}' when depth > 0:
                    depth--;
                    break;
                case ')':
                    AddArgument(arguments, code[from..i]);
                    return arguments;
                case ',' when depth == 0:
                    AddArgument(arguments, code[from..i]);
                    from = i + 1;
                    break;
            }
        }

        return arguments;
    }

    private static void AddArgument(List<string> arguments, string text)
    {
        if (!string.IsNullOrWhiteSpace(text) || arguments.Count > 0)
        {
            arguments.Add(text.Trim());
        }
    }
}
