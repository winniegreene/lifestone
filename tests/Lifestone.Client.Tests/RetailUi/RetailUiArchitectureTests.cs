using System.Text.RegularExpressions;
using Lifestone.Client.Tests.Repository;

namespace Lifestone.Client.Tests.RetailUi;

/// <summary>
/// The retail UI's boundaries (m2-plan.md R16), checked in the sources (comments and literals blanked) and in the built
/// Lifestone.Client's metadata for every type under <c>Lifestone.Client.RetailUi</c>:
/// <list type="bullet">
/// <item>no Godot (the host adapts; the runtime is engine-agnostic);</item>
/// <item>no file, process or stream I/O (hosts supply the dats through DatSet, and every byte else);</item>
/// <item>no Lifestone.Net, and of the session layer only the state contract (<c>Lifestone.Client.Session.Contracts</c>:
/// <c>IGameSessionView</c>, <c>IActionApi</c> and the events and records they expose), never the session itself, its
/// bootstrap, options or credentials.</item>
/// </list>
/// </summary>
public sealed partial class RetailUiArchitectureTests
{
    private const string RetailUiFolder = "src/Lifestone.Client/RetailUi/";
    private const string RetailUiNamespace = "Lifestone.Client.RetailUi.";

    [GeneratedRegex(@"(?<![\w.])(?:global::)?Godot\s*[.;]")]
    private static partial Regex GodotUse();

    /// <summary>File, process and stream access, and the System.IO namespace itself.</summary>
    [GeneratedRegex(@"\bSystem\s*\.\s*IO\b|(?<![\w.])(?:File|Directory|Path)\s*\.\s*[A-Z]|\b(?:FileStream|FileInfo|DirectoryInfo|DriveInfo|FileSystemWatcher|RandomAccess|StreamReader|StreamWriter|BinaryReader|BinaryWriter)\b|(?<![\w.])Process\s*\.\s*[A-Z]|\bProcessStartInfo\b")]
    private static partial Regex IoUse();

    [GeneratedRegex(@"\bLifestone\s*\.\s*Net\b")]
    private static partial Regex NetUse();

    /// <summary>The session layer other than its contract.</summary>
    [GeneratedRegex(@"\bLifestone\s*\.\s*Client\s*\.\s*Session\b(?!\s*\.\s*Contracts\b)")]
    private static partial Regex SessionUse();

    /// <summary>The live session, its bootstrap, options and host, and anything that holds credentials.</summary>
    [GeneratedRegex(@"\b(?:GameSession|SessionBootstrap|SessionOptions|SessionHost|AcSession|LoginCredentials|Secrets\w*|\w*Password\w*|\w*Credential\w*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SessionOrCredentialUse();

    private static IReadOnlyList<string> Sources => [.. CheckoutFiles.CSharpUnder(RetailUiFolder)];

    [Fact]
    public void The_scan_sees_the_retail_ui()
    {
        Assert.True(Sources.Count > 40, "too few RetailUi sources; did the scan break?");
        Assert.Contains(RetailUiFolder + "Input/UiInputRouter.cs", Sources);
        Assert.Contains(RetailUiFolder + "Flow/GmUiFlow.cs", Sources);
        Assert.Contains(BuiltAssemblies.Named("Lifestone.Client").MemberUses, u => u.Caller.StartsWith(RetailUiNamespace, StringComparison.Ordinal));
    }

    [Fact]
    public void The_retail_ui_uses_no_Godot()
    {
        AssertNone(Offences(GodotUse()), "Godot in the retail UI");
        AssertNone(Uses(t => t.StartsWith("Godot", StringComparison.Ordinal)), "Godot types used by the retail UI");
    }

    [Fact]
    public void The_retail_ui_does_no_io()
    {
        AssertNone(Offences(IoUse()), "file, process or stream I/O in the retail UI");
        // InvalidDataException lives in System.IO but is only the loud failure for bad data, used across the codebase.
        AssertNone(
            Uses(t => (t.StartsWith("System.IO.", StringComparison.Ordinal) && t != "System.IO.InvalidDataException") || t.StartsWith("System.Diagnostics.Process", StringComparison.Ordinal)),
            "System.IO or process types used by the retail UI");
    }

    [Fact]
    public void The_retail_ui_reaches_the_session_only_through_the_state_contract()
    {
        AssertNone(Offences(NetUse()), "Lifestone.Net named in the retail UI");
        AssertNone(Offences(SessionUse()), "the session layer beyond its contract named in the retail UI");
        AssertNone(Offences(SessionOrCredentialUse()), "the live session, its bootstrap or options, or credentials named in the retail UI");
        AssertNone(
            Uses(t => t.StartsWith("Lifestone.Net.", StringComparison.Ordinal)
                || (t.StartsWith("Lifestone.Client.Session.", StringComparison.Ordinal) && !t.StartsWith("Lifestone.Client.Session.Contracts.", StringComparison.Ordinal))
                || t.StartsWith("System.Net.", StringComparison.Ordinal)),
            "network or session types beyond the contract used by the retail UI");
    }

    [Theory]
    [InlineData("io", "var b = File.ReadAllBytes(p);", true)]
    [InlineData("io", "using System.IO;", true)]
    [InlineData("io", "var r = new StreamReader(s);", true)]
    [InlineData("io", "var f = Profile.File;", false)]
    [InlineData("net", "using Lifestone.Net.Messages;", true)]
    [InlineData("net", "var n = Lifestone.Network;", false)]
    [InlineData("session", "using Lifestone.Client.Session;", true)]
    [InlineData("session", "using Lifestone.Client.Session.Contracts;", false)]
    [InlineData("session", "var s = Lifestone.Client.Session.GameSession.Offline();", true)]
    [InlineData("secrets", "var p = account.Password;", true)]
    [InlineData("secrets", "var s = SessionBootstrap.CreateLive();", true)]
    [InlineData("secrets", "var v = view.Actions;", false)]
    public void The_rules_see_what_they_forbid(string rule, string line, bool offends)
    {
        var regex = rule switch
        {
            "io" => IoUse(),
            "net" => NetUse(),
            "session" => SessionUse(),
            _ => SessionOrCredentialUse(),
        };

        Assert.Equal(offends, regex.IsMatch(CSharpText.CodeOnly(line)));
    }

    private static List<string> Offences(Regex rule)
    {
        var offences = new List<string>();
        foreach (var file in Sources)
        {
            var text = CheckoutFiles.ReadCSharpCode(file);
            foreach (Match m in rule.Matches(text))
            {
                offences.Add($"{file}:{CheckoutFiles.LineOf(text, m.Index)}: {m.Value.Trim()}");
            }
        }

        return offences;
    }

    private static List<string> Uses(Func<string, bool> forbidden) =>
        [.. BuiltAssemblies.Named("Lifestone.Client").MemberUses
            .Where(u => u.Caller.StartsWith(RetailUiNamespace, StringComparison.Ordinal) && forbidden(u.Type))
            .Select(u => $"{u.Caller} uses {u.Type}.{u.Member}")
            .Distinct(StringComparer.Ordinal)];

    private static void AssertNone(List<string> offences, string rule) =>
        Assert.True(offences.Count == 0, rule + ":\n" + string.Join('\n', offences));
}
