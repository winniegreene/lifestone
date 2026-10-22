using System.Text.RegularExpressions;
using Lifestone.Client.Tests;

namespace Lifestone.Net.Tests.Hygiene;

/// <summary>
/// The net track's provenance (m2-plan §3.3 N13). Every source the track owns whose first line is a port header
/// ("// Ported from owner/repo@sha ..." or "// Partly ported from ...") cites a pin the track ports from: OpenAC
/// @1ee5a08 or ACE @95ef426d, and DatReaderWriter @a9345f2 for the motion command table alone. docs/third-party/net.md
/// names each such file together with its source and pin. And src/Lifestone.Net/THIRD-PARTY-NOTICES.md lists no file
/// that is gone or holds no OpenAC code.
/// </summary>
/// <remarks>
/// The repository-wide <c>ProvenanceTests</c> in Lifestone.Client.Tests check the header grammar everywhere, that some
/// docs/third-party page names every port, and that the notices name every OpenAC port in Lifestone.Net. These are the
/// net track's own, stricter rules: its pins, its page, and no stale notice.
/// </remarks>
public sealed partial class NetProvenanceTests
{
    private const string OpenAc = "eriknihlen/OpenAC@1ee5a08";

    /// <summary>The second OpenAC pin (M4 D7): the signed head the M4 wire is ported from (1.2 s0 on: the combat, container, vendor and appraisal codecs); the ports above stay at <see cref="OpenAc"/>.</summary>
    private const string OpenAcM4 = "eriknihlen/OpenAC@f22b048";
    private const string Ace = "ACEmulator/ACE@95ef426d";
    private const string DatReaderWriter = "Chorizite/DatReaderWriter@a9345f2";

    private const string NetDoc = "docs/third-party/net.md";
    private const string Notices = "src/Lifestone.Net/THIRD-PARTY-NOTICES.md";

    /// <summary>The track's source folders (or single files) and the pins a port in each may cite.</summary>
    private static readonly (string Path, string[] Pins)[] Scopes =
    [
        ("src/Lifestone.Net", [OpenAc, OpenAcM4, Ace]),
        ("tests/Lifestone.Net.Tests", [OpenAc, OpenAcM4, Ace]),
        ("tests/Shared/Net", [OpenAc, OpenAcM4, Ace]),
        ("tests/Lifestone.Loopback.Tests/Net", [OpenAc, OpenAcM4, Ace]),
        ("src/Lifestone.Data/Animation/MotionCommandTable.cs", [DatReaderWriter]),
    ];

    /// <summary>The first line of a port header: the claim, then the source as owner/repo@sha.</summary>
    [GeneratedRegex(@"^// (?:Partly p|P)orted from (?<source>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-f]{7,40})\s")]
    private static partial Regex HeaderLine();

    /// <summary>A notices table row: its first cell, a path below src/Lifestone.Net in backticks.</summary>
    [GeneratedRegex(@"^\|\s*`(?<path>[^`]+\.cs)`\s*\|", RegexOptions.Multiline)]
    private static partial Regex NoticesRow();

    private static readonly Lazy<IReadOnlyList<Port>> Ports = new(FindPorts);

    [Fact]
    public void The_scan_finds_the_track_s_ports()
    {
        // 53 product files and the motion command table, and 24 test files, when N13 landed (60 product files after M4 3.2 s0): a broken scan must not pass.
        var ports = Ports.Value;
        Assert.True(ports.Count(p => p.Path.StartsWith("src/", StringComparison.Ordinal)) >= 60, $"only {ports.Count} port headers found");
        Assert.True(ports.Count(p => p.Path.StartsWith("tests/", StringComparison.Ordinal)) >= 24, $"only {ports.Count} port headers found");
        Assert.Contains(ports, p => p.Path == "src/Lifestone.Data/Animation/MotionCommandTable.cs");
        Assert.Contains(ports, p => p.Path == "tests/Lifestone.Net.Tests/FakeAce/AceCryptoSystemOracle.cs" && p.Source.StartsWith(Ace, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_port_cites_a_pin_the_track_ports_from()
    {
        var problems = Ports.Value
            .Where(p => !p.AllowedPins.Any(pin => p.Source.StartsWith(pin, StringComparison.Ordinal)))
            .Select(p => $"{p.Path}: cites {p.Source}; allowed here: {string.Join(", ", p.AllowedPins)}")
            .ToArray();
        Assert.True(problems.Length == 0, "net-track ports with a pin the track does not port from:\n" + string.Join('\n', problems));
    }

    [Fact]
    public void Net_md_names_every_port_with_its_source_and_pin()
    {
        var doc = File.ReadAllText(RepositoryFiles.PathOf(NetDoc.Split('/')));
        var problems = new List<string>();
        foreach (var port in Ports.Value)
        {
            var name = Path.GetFileName(port.Path);
            if (!NamesFile(doc, name))
            {
                problems.Add($"{port.Path}: {NetDoc} does not name {name}");
            }

            var at = port.Source.IndexOf('@', StringComparison.Ordinal);
            var repository = port.Source[..at];
            var prefix = port.Source[(at + 1)..Math.Min(port.Source.Length, at + 1 + 7)];
            if (!doc.Contains(repository, StringComparison.Ordinal) || !doc.Contains(prefix, StringComparison.Ordinal))
            {
                problems.Add($"{port.Path}: {NetDoc} does not name its source {repository} at {prefix}");
            }
        }

        Assert.True(problems.Count == 0, "ports the net track's page does not list:\n" + string.Join('\n', problems));
    }

    [Fact]
    public void Every_file_the_notices_list_exists_and_holds_OpenAC_code()
    {
        var notices = File.ReadAllText(RepositoryFiles.PathOf(Notices.Split('/')));
        var rows = NoticesRow().Matches(notices).Select(m => m.Groups["path"].Value).ToArray();
        Assert.True(rows.Length >= 50, $"only {rows.Length} rows found in {Notices}; did its table change?");

        var openAcPorts = Ports.Value
            .Where(p => p.Path.StartsWith("src/Lifestone.Net/", StringComparison.Ordinal) && (p.Source.StartsWith(OpenAc, StringComparison.Ordinal) || p.Source.StartsWith(OpenAcM4, StringComparison.Ordinal)))
            .Select(p => p.Path["src/Lifestone.Net/".Length..])
            .ToHashSet(StringComparer.Ordinal);
        var stale = rows.Where(r => !openAcPorts.Contains(r)).ToArray();
        Assert.True(stale.Length == 0, $"{Notices} lists files that are gone or hold no OpenAC code:\n" + string.Join('\n', stale));
    }

    [Theory]
    [InlineData("// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Transport/SequenceMath.cs (MIT;", "eriknihlen/OpenAC@1ee5a08")]
    [InlineData("// Ported from eriknihlen/OpenAC@f22b048 src/AcDream.Core.Net/Messages/AttackTargetRequest.cs (MIT;", "eriknihlen/OpenAC@f22b048")]
    [InlineData("// Partly ported from ACEmulator/ACE@95ef426d Source/ACE.Common/Cryptography/CryptoSystem.cs (AGPL-3.0)", "ACEmulator/ACE@95ef426d")]
    [InlineData("// The tail probe (N8): ported from nowhere", null)]
    [InlineData("/// Ported from eriknihlen/OpenAC@1ee5a08 src/x.cs (MIT;", null)]
    public void The_header_pattern_reads_the_source_of_a_first_line(string line, string? source)
    {
        var match = HeaderLine().Match(line);
        Assert.Equal(source, match.Success ? match.Groups["source"].Value : null);
    }

    private static IReadOnlyList<Port> FindPorts()
    {
        var ports = new List<Port>();
        foreach (var (scope, pins) in Scopes)
        {
            var full = RepositoryFiles.PathOf(scope.Split('/'));
            IEnumerable<string> files = File.Exists(full)
                ? [full]
                : Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories)
                    .Where(p => !IsBuildOutput(Path.GetRelativePath(full, p)));
            foreach (var file in files)
            {
                using var reader = new StreamReader(file);
                var first = reader.ReadLine() ?? string.Empty;
                var match = HeaderLine().Match(first.TrimStart('﻿'));
                if (match.Success)
                {
                    var path = Path.GetRelativePath(RepositoryFiles.Root, file).Replace('\\', '/');
                    ports.Add(new Port(path, match.Groups["source"].Value, pins));
                }
            }
        }

        return ports;
    }

    private static bool IsBuildOutput(string relative)
    {
        var first = relative.Replace('\\', '/').Split('/')[0];
        return first is "bin" or "obj";
    }

    private static bool NamesFile(string text, string fileName) =>
        Regex.IsMatch(text, @"(?<![A-Za-z0-9_])" + Regex.Escape(fileName) + "(?![A-Za-z0-9_])");

    /// <summary>A file with a port header on its first line, the source it cites, and the pins its scope allows.</summary>
    private sealed record Port(string Path, string Source, string[] AllowedPins);
}
