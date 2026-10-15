using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Lifestone.Client.Tests;

namespace Lifestone.Net.Tests.Hygiene;

/// <summary>
/// Lifestone.Net's source rules (m2-plan §3.3 N2): no console output, no environment reads (OpenAC's <c>ACDREAM_*</c>
/// switches stay out), no wall clock, <c>Stopwatch</c> or sleeping (everything runs on a <see cref="TimeProvider"/>),
/// no wildcard address, no file or process access, and no process-wide encoding provider (CP1252 comes from
/// <c>CodePagesEncodingProvider.Instance</c> directly). Checked twice: in the sources with comments blanked, and in the
/// built assembly's member references, which an alias or a <c>using static</c> cannot dodge.
/// </summary>
/// <remarks>
/// The repository-wide scans in Lifestone.Client.Tests (ArchitectureTests, LoopbackOnlyTests) cover some of these rules
/// for every project; this is the net track's own, complete list. The forbidden tokens that the repository may not
/// spell even in a test (the wildcard addresses) are assembled at run time.
/// </remarks>
public sealed class SourceRulesTests
{
    private const string NetFolder = "src/Lifestone.Net";

    private static readonly string AnyAddress = "IPAddress" + @"\s*\.\s*(?:Any|IPv6" + "Any)";

    private static readonly string WildcardLiteral = string.Join(@"\.", Enumerable.Repeat("0", 4));

    /// <summary>Each rule's name and its pattern over code with comments blanked (string literals kept).</summary>
    private static readonly (string Rule, Regex Pattern)[] Rules =
    [
        ("console output", new(@"\bConsole\s*\.")),
        ("environment variables", new(@"\bEnvironment\s*\.\s*(?:GetEnvironmentVariables?|ExpandEnvironmentVariables|SetEnvironmentVariable)\b|ACDREAM_")),
        ("the wall clock", new(@"\bDateTime(?:Offset)?\s*\.\s*(?:UtcNow|Now|Today)\b|\bEnvironment\s*\.\s*TickCount(?:64)?\b")),
        ("Stopwatch", new(@"\bStopwatch\b")),
        ("sleeping", new(@"\bThread\s*\.\s*Sleep\b")),
        ("a wildcard address", new(@"\b" + AnyAddress + @"\b|(?<![\d.])" + WildcardLiteral + @"(?![\d.])")),
        ("file access", new(@"(?<![\w.])(?:System\.IO\.)?(?:File|Directory)\s*\.\s*[A-Z]|\b(?:FileStream|FileInfo|DirectoryInfo)\b")),
        ("process access", new(@"(?<![\w.])(?:System\.Diagnostics\.)?Process\s*\.\s*[A-Z]|\bProcessStartInfo\b")),
        ("a process-wide encoding provider", new(@"\bEncoding\s*\.\s*RegisterProvider\b")),
    ];

    public static TheoryData<string, bool> Samples => new()
    {
        { "Console.WriteLine(x);", true },
        { "var v = Environment.GetEnvironmentVariable(\"X\");", true },
        { "if (flags.Contains(\"ACDREAM_NET\")) {}", true },
        { "var t = DateTime.UtcNow;", true },
        { "var t = DateTimeOffset.Now;", true },
        { "var s = Stopwatch.GetTimestamp();", true },
        { "Thread.Sleep(5);", true },
        { "var e = new IPEndPoint(" + "IPAddress" + ".Any, 0);", true },
        { "var e = IPEndPoint.Parse(\"" + new System.Net.IPAddress(0) + ":9000\");", true },
        { "File.ReadAllBytes(p);", true },
        { "Process.Start(\"x\");", true },
        { "Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);", true },
        { "// Console.WriteLine is not used; nor is Stopwatch", false },
        { "var a = Admission.Process(null);", false },
        { "var t = time.GetTimestamp();", false },
        { "var e = new IPEndPoint(IPAddress.Loopback, 0);", false },
        { "var v = new Version(10, 0, 0, 0);", false },
        { "var p = Profile.Name;", false },
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void The_rules_see_code_and_strings_but_not_comments(string line, bool offends)
    {
        var code = BlankComments(line);
        Assert.Equal(offends, Rules.Any(r => r.Pattern.IsMatch(code)));
    }

    [Fact]
    public void No_source_in_lifestone_net_breaks_a_rule()
    {
        var root = RepositoryFiles.PathOf(NetFolder.Split('/'));
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsBuildOutput(Path.GetRelativePath(root, p)))
            .ToArray();
        Assert.True(files.Length > 50, $"only {files.Length} sources under {NetFolder}; did the scan break?");

        var offences = new List<string>();
        foreach (var file in files)
        {
            var code = BlankComments(File.ReadAllText(file));
            foreach (var (rule, pattern) in Rules)
            {
                foreach (Match m in pattern.Matches(code))
                {
                    var line = code.AsSpan(0, m.Index).Count('\n') + 1;
                    offences.Add($"{NetFolder}/{Path.GetRelativePath(root, file).Replace('\\', '/')}:{line}: {rule} ({m.Value})");
                }
            }
        }

        Assert.True(offences.Count == 0, "Lifestone.Net source rules (m2-plan §3.3 N2):\n" + string.Join('\n', offences));
    }

    [Fact]
    public void The_built_assembly_references_no_forbidden_member()
    {
        var forbidden = ForbiddenMemberReferences(typeof(NetAssembly).Assembly.Location).ToArray();
        Assert.True(forbidden.Length == 0, "Lifestone.Net.dll references:\n" + string.Join('\n', forbidden));
    }

    [Fact]
    public void The_metadata_scan_sees_a_forbidden_reference()
    {
        // This test assembly calls Stopwatch and Console (below); the scan must see them, or it proves nothing.
        var found = ForbiddenMemberReferences(typeof(SourceRulesTests).Assembly.Location).ToArray();
        Assert.Contains(found, f => f.StartsWith("System.Diagnostics.Stopwatch", StringComparison.Ordinal));
        Assert.Contains(found, f => f.StartsWith("System.Console", StringComparison.Ordinal));
    }

    /// <summary>Never called: the forbidden references <see cref="The_metadata_scan_sees_a_forbidden_reference"/> looks for.</summary>
    internal static void ForbiddenSample()
    {
        _ = System.Diagnostics.Stopwatch.GetTimestamp();
        Console.Out.Flush();
    }

    /// <summary>The member references of an assembly that break a rule, as <c>Type.Member</c>.</summary>
    private static IEnumerable<string> ForbiddenMemberReferences(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            var typeName = $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}";
            var memberName = metadata.GetString(member.Name);
            if (IsForbidden(typeName, memberName))
            {
                yield return $"{typeName}.{memberName}";
            }
        }
    }

    private static bool IsForbidden(string type, string member) => (type, member) switch
    {
        ("System.Console", _) => true,
        ("System.Diagnostics.Stopwatch", _) => true,
        ("System.Diagnostics.Process", _) => true,
        ("System.IO.File", _) or ("System.IO.Directory", _) or ("System.IO.FileStream", _) => true,
        ("System.Environment", "GetEnvironmentVariable" or "GetEnvironmentVariables" or "ExpandEnvironmentVariables"
            or "SetEnvironmentVariable" or "get_TickCount" or "get_TickCount64") => true,
        ("System.DateTime", "get_Now" or "get_UtcNow" or "get_Today") => true,
        ("System.DateTimeOffset", "get_Now" or "get_UtcNow") => true,
        ("System.Threading.Thread", "Sleep") => true,
        ("System.Text.Encoding", "RegisterProvider") => true,
        ("System.Net.IPAddress", _) when member == "Any" || member == "IPv6" + "Any" => true,
        _ => false,
    };

    private static bool IsBuildOutput(string relative)
    {
        var first = relative.Replace('\\', '/').Split('/')[0];
        return first is "bin" or "obj";
    }

    /// <summary>
    /// The source with its comments replaced by spaces (line breaks kept): line and block comments, outside string and
    /// character literals. A small lexer for this project's sources: regular, verbatim and interpolated strings; raw
    /// string literals are not used in Lifestone.Net.
    /// </summary>
    private static string BlankComments(string source)
    {
        var output = source.ToCharArray();
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n')
                {
                    output[i++] = ' ';
                }
            }
            else if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 2;
                for (; i < end; i++)
                {
                    if (source[i] != '\n')
                    {
                        output[i] = ' ';
                    }
                }
            }
            else if (c == '\'')
            {
                i = SkipQuoted(source, i + 1, '\'', verbatim: false);
            }
            else if (c == '"')
            {
                var verbatim = i > 0 && (source[i - 1] == '@' || (source[i - 1] == '$' && i > 1 && source[i - 2] == '@'));
                i = SkipQuoted(source, i + 1, '"', verbatim);
            }
            else
            {
                i++;
            }
        }

        return new string(output);
    }

    private static int SkipQuoted(string source, int i, char quote, bool verbatim)
    {
        while (i < source.Length)
        {
            if (!verbatim && source[i] == '\\')
            {
                i += 2;
            }
            else if (source[i] == quote)
            {
                if (verbatim && i + 1 < source.Length && source[i + 1] == quote)
                {
                    i += 2;
                }
                else
                {
                    return i + 1;
                }
            }
            else if (source[i] == '\n' && !verbatim)
            {
                return i + 1; // an unterminated literal: stop at the line end
            }
            else
            {
                i++;
            }
        }

        return i;
    }
}
