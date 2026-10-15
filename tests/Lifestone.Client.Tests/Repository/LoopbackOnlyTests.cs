using System.Text.RegularExpressions;

namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// Loopback only (CLAUDE.md, "Network"; M2 DoD 1): nothing in the code or its tests names a wildcard address, so no
/// socket can be bound or pointed at every interface by accident. C# comments are ignored (they may explain the rule);
/// string literals count. Other text files under these folders (scenes, settings, JSON) are read whole. The built
/// assemblies' IL is checked too (<see cref="BuiltAssemblies"/>), which a <c>using static</c> cannot dodge.
/// </summary>
public sealed partial class LoopbackOnlyTests
{
    /// <summary>The folders scanned: the code and its tests, and the server mod (compiled into Lifestone.Client and ACE).</summary>
    private static readonly string[] Folders = ["src/", "tests/", "server/mod/"];

    /// <summary>
    /// Files allowed to spell a wildcard address, each with its reason: this file, which defines the rule. A test of the
    /// loopback policy should build its wildcard vectors (<c>new IPAddress(0)</c>) rather than join this list.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        ["tests/Lifestone.Client.Tests/Repository/LoopbackOnlyTests.cs"] = "defines the rule and its samples",
    };

    [GeneratedRegex(@"\bIPAddress\s*\.\s*(?:Any|IPv6Any)\b|\bIPv6Any\b|(?<![\d.])0\.0\.0\.0(?![\d.])")]
    private static partial Regex Wildcard();

    [Fact]
    public void Nothing_names_a_wildcard_address()
    {
        var files = Folders.SelectMany(CheckoutFiles.Under)
            .Where(p => !Allowed.ContainsKey(p) && !p.Split('/').Contains("vendor-eval"))
            .Where(p => CheckoutFiles.TryReadText(p) is not null)
            .ToArray();
        Assert.True(files.Length > 400, $"only {files.Length} files scanned; did the scan break?");

        var offences = new List<string>();
        foreach (var file in files)
        {
            var text = file.EndsWith(".cs", StringComparison.Ordinal) ? CheckoutFiles.ReadCSharpCodeAndStrings(file) : CheckoutFiles.ReadText(file);
            offences.AddRange(Wildcard().Matches(text).Select(m => $"{file}:{CheckoutFiles.LineOf(text, m.Index)}: {m.Value}"));
        }

        Assert.True(offences.Count == 0, "wildcard addresses (bind and connect to 127.0.0.1 only):\n" + string.Join('\n', offences));
    }

    [Fact]
    public void No_built_assembly_reads_a_wildcard_address()
    {
        // The metadata backstop: a 'using static System.Net.IPAddress;' and a bare 'Any' dodge the source scan, not the IL.
        var uses = BuiltAssemblies.All
            .SelectMany(a => a.MemberUses.Where(IsWildcardAddress).Select(u => $"{a.Name}: {u.Caller} reads {u.Type}.{u.Member}"))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(uses.Count == 0, "wildcard addresses in the built assemblies (bind and connect to 127.0.0.1 only):\n" + string.Join('\n', uses));
    }

    [Fact]
    public void The_metadata_scan_sees_a_wildcard_read()
    {
        var self = AssemblyMetadata.Read(typeof(LoopbackOnlyTests).Assembly.Location);
        Assert.Contains(self.MemberUses, u => u.Caller == typeof(LoopbackOnlyTests).FullName && IsWildcardAddress(u));
    }

    /// <summary>
    /// A wildcard read for <see cref="The_metadata_scan_sees_a_wildcard_read"/> to find in this assembly's IL. Never called;
    /// this file is the one allowed to spell the wildcard.
    /// </summary>
    internal static System.Net.IPAddress WildcardSample() => System.Net.IPAddress.IPv6Any;

    private static bool IsWildcardAddress(MemberUse u) =>
        u.Type == "System.Net.IPAddress" && u.Member is "Any" or "IPv6Any";

    [Fact]
    public void Every_allowed_file_still_exists()
    {
        var missing = Allowed.Keys.Where(p => !CheckoutFiles.All.Contains(p)).ToArray();
        Assert.True(missing.Length == 0, "Allowed entries for files that no longer exist; remove them: " + string.Join(", ", missing));
    }

    [Theory]
    [InlineData("socket.Bind(new IPEndPoint(IPAddress.Any, port));", true)]
    [InlineData("var a = IPAddress.IPv6Any;", true)]
    [InlineData("var e = IPEndPoint.Parse(\"0.0.0.0:9000\");", true)]
    [InlineData("var e = new IPEndPoint(IPAddress.Loopback, port); // never IPAddress.Any", false)]
    [InlineData("var v = new Version(10, 0, 0, 0);", false)]
    [InlineData("var s = \"10.0.0.5\";", false)]
    public void The_rule_sees_code_and_strings_but_not_comments(string line, bool offends)
    {
        Assert.Equal(offends, Wildcard().IsMatch(CSharpText.WithoutComments(line)));
    }
}
