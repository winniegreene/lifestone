using System.Text.RegularExpressions;

namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// Provenance of third-party code in the repository (CLAUDE.md, "Data and sources"; docs/milestones/m2-plan.md §2.3
/// L11):
/// <list type="bullet">
/// <item>every port header follows the M1 grammar and cites an approved pin with its licence;</item>
/// <item>a port of MIT code keeps the source's copyright line;</item>
/// <item>docs/third-party lists every file that holds a port, with its sources;</item>
/// <item>src/Lifestone.Net/THIRD-PARTY-NOTICES.md lists the OpenAC ports in Lifestone.Net.</item>
/// </list>
/// The listing checks are pure functions over paths and texts (<see cref="ListingProblems"/>,
/// <see cref="NoticesProblems"/>), tested on samples here as well as run on the checkout.
/// </summary>
public sealed partial class ProvenanceTests
{
    /// <summary>The folders whose sources may carry ported code.</summary>
    private static readonly string[] ScannedFolders = ["src/", "tests/", "tools/", "server/"];

    /// <summary>The notices file the net track keeps for the OpenAC code in Lifestone.Net (m2-plan §3.3 net).</summary>
    private const string NetNotices = "src/Lifestone.Net/THIRD-PARTY-NOTICES.md";

    /// <summary>
    /// The files with port headers at the end of M1, and how many headers each holds: 19 in 17 files, counting the
    /// "Partly ported" file headers and the two member headers in SpellFormulaRules.cs. Every one must still be found, so
    /// a regression in the scan or the grammar cannot quietly take a file out of the checks below. The list only grows;
    /// a port that moves or is renamed moves its entry.
    /// </summary>
    private static readonly Dictionary<string, int> M1PortedFiles = new(StringComparer.Ordinal)
    {
        ["src/Lifestone.Data/Animation/MotionCommands.cs"] = 1,
        ["src/Lifestone.Data/Magic/CastGesturePlanner.cs"] = 1,
        ["src/Lifestone.Data/Magic/SpellCastSimulation.cs"] = 1,
        ["src/Lifestone.Data/Magic/SpellComponentTableData.cs"] = 1,
        ["src/Lifestone.Data/Magic/SpellFormulaRules.cs"] = 3,
        ["src/Lifestone.Data/Magic/SpellInfo.cs"] = 1,
        ["src/Lifestone.Data/Magic/SpellVisualResolver.cs"] = 1,
        ["src/Lifestone.Data/Terrain/Blending/TerrainTexMerge.cs"] = 1,
        ["src/Lifestone.Data/World/Scenery/SceneryGenerator.cs"] = 1,
        ["src/Lifestone.Data/World/Scenery/SceneryRandom.cs"] = 1,
        ["src/Lifestone.Data/World/Scenery/SceneryRoad.cs"] = 1,
        ["src/Lifestone.Data/World/Scenery/TerrainSampler.cs"] = 1,
        ["tests/Lifestone.Data.Tests/Animation/AceMotionOracle.cs"] = 1,
        ["tests/Lifestone.Data.Tests/Effects/AceParticleOracle.cs"] = 1,
        ["tests/Lifestone.Data.Tests/Scenery/AceSceneryOracle.cs"] = 1,
        ["tests/Lifestone.Data.Tests/Terrain/Oracle/AcViewerTexMergeOracle.cs"] = 1,
        ["tests/Lifestone.Data.Tests/Terrain/Oracle/OpenAcVertexNormalsOracle.cs"] = 1,
    };

    /// <summary>
    /// Ports whose headers are correct but that no docs/third-party page names yet, with the reason. Empty since M2 wave
    /// 0 named the four M1 scenery files in scenery.md; an entry is a temporary, reviewed exception, and the stale-entry
    /// check fails once the doc names the file.
    /// </summary>
    private static readonly Dictionary<string, string> UnlistedM1Ports = new(StringComparer.Ordinal);

    /// <summary>
    /// Port notices that are not yet in the header grammar, by file and a phrase of the notice line. Empty since M2 wave
    /// 0 rewrote the last M1 notice (SpellTableTests' ComputeHash oracle) as a header; the stale-entry check fails once a
    /// listed notice is fixed.
    /// </summary>
    private static readonly (string Path, string Phrase, string Reason)[] M1PortNotices = [];

    private const string SampleNetPort = "src/Lifestone.Net/Transport/PacketHeader.cs";

    private const string SampleOpenAcHeader =
        "// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Net/PacketHeader.cs (MIT;\n// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: none.\nnamespace Sample;\n";

    private const string SampleNotices =
        "# Third-party notices\n\nPacketHeader.cs: eriknihlen/OpenAC at 1ee5a08.\n\nCopyright (c) 2026 Erik Nihlén and OpenAC contributors\n\nPermission is hereby granted, free of charge, to any person obtaining a copy\n";

    private static readonly Lazy<IReadOnlyList<PortHeader>> Headers = new(() =>
        [.. SourceFiles().SelectMany(p => PortHeader.Find(p, CheckoutFiles.ReadText(p)))]);

    /// <summary>
    /// A comment that says code is ported: "ported", "porting", "port of" or "port from". A pin cited for reading only
    /// (a reference, such as the 11.6096 symbol table of owner decision D4) belongs in a comment without these words.
    /// </summary>
    [GeneratedRegex(@"(?i)\b(?:ported|porting)\b|\bports?\s+(?:of|from)\b")]
    private static partial Regex PortClaim();

    private static IEnumerable<string> SourceFiles() =>
        CheckoutFiles.All.Where(p => ScannedFolders.Any(f => p.StartsWith(f, StringComparison.Ordinal)) && PortHeader.LineCommentFor(p) is not null);

    [Fact]
    public void The_scan_finds_every_M1_port_header()
    {
        var found = Headers.Value.GroupBy(h => h.Path, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var missing = M1PortedFiles
            .Where(e => found.GetValueOrDefault(e.Key) < e.Value)
            .Select(e => $"{e.Key}: {found.GetValueOrDefault(e.Key)} of {e.Value} headers found")
            .ToArray();
        Assert.True(missing.Length == 0, "M1 port headers the scan no longer finds (did the grammar, the scan or the file change?):\n" + string.Join('\n', missing));
    }

    [Fact]
    public void Every_port_header_follows_the_grammar_and_cites_an_approved_pin_with_its_licence()
    {
        var headers = Headers.Value;
        var floor = M1PortedFiles.Values.Sum();
        Assert.True(headers.Count >= floor, $"the scan found only {headers.Count} port headers; M1 had {floor}. Did the grammar or the scan change?");
        var problems = headers.SelectMany(h => h.Problems().Select(p => $"{h.Path}:{h.Line}: {p}")).ToArray();
        Assert.True(problems.Length == 0, "port headers that break the M1 grammar (MotionCommands.cs:1-3):\n" + string.Join('\n', problems));
    }

    [Fact]
    public void Every_pin_a_port_comment_cites_is_approved()
    {
        var problems = new List<string>();
        var cited = 0;
        foreach (var path in SourceFiles())
        {
            foreach (var (line, repository, sha) in PinsInPortComments(path, CheckoutFiles.ReadText(path)))
            {
                cited++;
                if (PortHeader.PinProblem(repository, sha) is { } problem)
                {
                    problems.Add($"{path}:{line}: {problem}");
                }
            }
        }

        Assert.True(cited >= M1PortedFiles.Values.Sum(), $"only {cited} pins are cited in port comments; did the scan break?");
        Assert.True(
            problems.Count == 0,
            "pins cited in comments that say code is ported, but not approved for porting (a pin cited for reading only belongs in a comment that makes no port claim):\n"
            + string.Join('\n', problems));
    }

    [Fact]
    public void Every_comment_that_says_ported_under_a_licence_is_a_header()
    {
        var headers = Headers.Value.ToLookup(h => h.Path, StringComparer.Ordinal);
        var problems = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in SourceFiles())
        {
            var comment = PortHeader.LineCommentFor(path)!;
            var lines = CheckoutFiles.Lines(CheckoutFiles.ReadText(path));
            for (var i = 0; i < lines.Length; i++)
            {
                var line = i + 1;
                if (!PortHeader.TrySplitComment(lines[i], comment, out _, out var content)
                    || !PortHeader.PortNotice().IsMatch(content)
                    || headers[path].Any(h => h.Line <= line && line <= h.EndLine))
                {
                    continue;
                }

                var known = M1PortNotices.FirstOrDefault(n => n.Path == path && content.Contains(n.Phrase, StringComparison.Ordinal));
                if (known.Path is not null)
                {
                    used.Add(known.Path + "|" + known.Phrase);
                    continue;
                }

                problems.Add($"{path}:{line}");
            }
        }

        Assert.True(problems.Count == 0, "comments that say code is ported under a licence but are not port headers (write 'Ported from owner/repo@sha ...'):\n" + string.Join('\n', problems));
        var stale = M1PortNotices.Where(n => !used.Contains(n.Path + "|" + n.Phrase)).Select(n => n.Path).ToArray();
        Assert.True(stale.Length == 0, "M1PortNotices entries that no longer match a notice; remove them: " + string.Join(", ", stale));
    }

    [Fact]
    public void Every_ported_file_is_listed_in_docs_third_party_with_its_sources()
    {
        var docs = CheckoutFiles.Under("docs/third-party/")
            .Where(p => p.EndsWith(".md", StringComparison.Ordinal))
            .Select(p => (Path: p, Text: CheckoutFiles.ReadText(p)))
            .ToArray();
        Assert.NotEmpty(docs);

        var (problems, stale) = ListingProblems(Headers.Value, docs, UnlistedM1Ports.Keys);
        Assert.True(problems.Count == 0, "ported files missing from docs/third-party:\n" + string.Join('\n', problems));
        Assert.True(stale.Count == 0, "UnlistedM1Ports entries that docs/third-party now names; remove them: " + string.Join(", ", stale));
    }

    [Fact]
    public void OpenAC_ports_in_Lifestone_Net_are_listed_in_its_third_party_notices()
    {
        // Nothing to check until the net track brings OpenAC code into src/Lifestone.Net.
        var notices = CheckoutFiles.All.Contains(NetNotices) ? CheckoutFiles.ReadText(NetNotices) : null;
        var problems = NoticesProblems(Headers.Value, notices).ToArray();
        Assert.True(problems.Length == 0, $"{NetNotices}:\n" + string.Join('\n', problems));
    }

    [Fact]
    public void The_full_approved_pins_are_the_ones_the_repository_records()
    {
        var records = CheckoutFiles.All
            .Where(p => p.StartsWith("docs/", StringComparison.Ordinal) || p == "server/versions.json")
            .Where(p => p.EndsWith(".md", StringComparison.Ordinal) || p.EndsWith(".json", StringComparison.Ordinal))
            .Select(CheckoutFiles.ReadText)
            .ToArray();
        foreach (var source in PortHeader.Approved)
        {
            foreach (var pin in source.Pins)
            {
                Assert.True(pin.Full is null || pin.Full.StartsWith(pin.Prefix, StringComparison.Ordinal), $"{source.Repository}: {pin.Full} does not start with {pin.Prefix}");
                Assert.True(pin.Full is null || records.Any(t => t.Contains(pin.Full, StringComparison.Ordinal)), $"{source.Repository}@{pin.Prefix}: no doc records the full SHA ({pin.Where})");
            }
        }
    }

    [Fact]
    public void The_reference_header_is_well_formed()
    {
        var text = CheckoutFiles.ReadText("src/Lifestone.Data/Animation/MotionCommands.cs");
        var header = Assert.Single(PortHeader.Find("src/Lifestone.Data/Animation/MotionCommands.cs", text));
        Assert.Equal((1, 3), (header.Line, header.EndLine));
        Assert.Empty(header.Problems());
    }

    [Theory]
    [InlineData("// Ported from ACEmulator/ACE@95ef426d Source/A.cs (AGPL-3.0) [with changes: none].")]
    [InlineData("// Partly ported from ACEmulator/ACE@95ef426d412459ff619a12cc18f82bcf72c1c2a8 Source/A.cs (AGPL-3.0)\n// [with changes: one member].")]
    [InlineData("// Ported (API shape) from eriknihlen/OpenAC@1ee5a08 src/B.cs (MIT;\n// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: names only.")]
    [InlineData("// Ported from ACEmulator/ACViewer@c5c54fb A/T.cs (GPL-3.0)\n// and ACEmulator/ACE@7715c052 Source/L.cs (AGPL-3.0) with changes: merged.")]
    [InlineData("// Ported from Chorizite/DatReaderWriter@a9345f2 Lib/X.cs (MIT; Copyright 2024 ACClientLib) with changes: none.")]
    public void A_well_formed_header_has_no_problems(string header)
    {
        var found = Assert.Single(PortHeader.Find("src/Sample.cs", header + "\nnamespace Sample;\n"));
        Assert.Empty(found.Problems());
    }

    [Theory]
    [InlineData("// Ported from ACE Source/A.cs (AGPL-3.0) with changes: none.", "must open with")]
    [InlineData("// ported from ACEmulator/ACE@95ef426d Source/A.cs (AGPL-3.0) with changes: none.", "must open with")]
    [InlineData("// Ported from ACEmulator/ACE@95ef426d Source/A.cs (AGPL-3.0).", "with changes")]
    [InlineData("// Ported from ACEmulator/ACE@95ef426d Source/A.cs with changes: none.", "without its licence")]
    [InlineData("// Ported from ACEmulator/ACE@95ef426d Source/A.cs (MIT) with changes: none.", "its licence is AGPL-3.0")]
    [InlineData("// Ported from ACEmulator/ACE@95ef42 Source/A.cs (AGPL-3.0) with changes: none.", "7 to 40 lowercase hex")]
    [InlineData("// Ported from ACEmulator/ACE@95EF426D Source/A.cs (AGPL-3.0) with changes: none.", "7 to 40 lowercase hex")]
    [InlineData("// Ported from ACEmulator/ACE@0123456789 Source/A.cs (AGPL-3.0) with changes: none.", "not an approved pin")]
    [InlineData("// Ported from ACEmulator/ACE@95ef426d4124ffff Source/A.cs (AGPL-3.0) with changes: none.", "not an approved pin")]
    [InlineData("// Ported from someone/Fork@95ef426d Source/A.cs (AGPL-3.0) with changes: none.", "not an approved source")]
    [InlineData("// Ported from eriknihlen/OpenAC@1ee5a08 src/B.cs (MIT) with changes: none.", "must keep its copyright line")]
    [InlineData("// Ported from eriknihlen/OpenAC@1ee5a08 src/B.cs (MIT; Copyright (c) 2025 Someone) with changes: none.", "must keep its copyright line")]
    [InlineData("// Ported from ACEmulator/ACViewer@c5c54fb A/T.cs (GPL-3.0) and ACEmulator/ACE@95ef426d B.cs with changes: none.", "ACEmulator/ACE is cited without its licence")]
    [InlineData("using System;\n// Ported from ACEmulator/ACE@95ef426d Source/A.cs (AGPL-3.0) with changes: none.", "must start on line 1")]
    public void A_malformed_header_is_reported(string source, string expected)
    {
        var found = Assert.Single(PortHeader.Find("src/Sample.cs", source + "\nnamespace Sample;\n"));
        Assert.Contains(found.Problems(), p => p.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void A_member_header_ends_with_its_remarks()
    {
        var source = "/// <remarks>\n/// Ported from ACEmulator/ACE@95ef426d Source/S.cs <c>Scale</c> (AGPL-3.0)\n/// <c>Scale</c> [with changes: none].\n/// </remarks>\n/// <param name=\"x\">X.</param>\n";
        var header = Assert.Single(PortHeader.Find("src/Sample.cs", source));
        Assert.Equal(("///", 2, 3), (header.CommentPrefix, header.Line, header.EndLine));
        Assert.Empty(header.Problems());
    }

    [Theory]
    [InlineData("/// ACE <c>X</c>, ported verbatim (AGPL-3.0, @95ef426d).", true)]
    [InlineData("// MIT code, ported as an oracle", true)]
    [InlineData("/// Uses the effects track's port of the retail function.", false)]
    [InlineData("/// The ACE-ported oracle.", false)]
    public void A_port_notice_outside_the_grammar_is_recognised(string line, bool notice)
    {
        Assert.True(PortHeader.TrySplitComment(line, "//", out _, out var content));
        Assert.Equal(notice, PortHeader.PortNotice().IsMatch(content));
    }

    [Theory]
    [InlineData("// Ported from ACEmulator/ACE@95ef426d Source/A.cs (AGPL-3.0) with changes: none.", "1 ACEmulator/ACE@95ef426d")]
    [InlineData("// The oracle is a port of\n// someone/Fork@0123abc Source/A.cs.", "2 someone/Fork@0123abc")]
    [InlineData("/// <summary>A port from someone/Fork@0123abc.</summary>\n/// <remarks>And ACEmulator/ACE@7715c052.</remarks>", "1 someone/Fork@0123abc|2 ACEmulator/ACE@7715c052")]
    [InlineData("// Addresses read from someone/Reference@0123abc (reference only, owner decision D4).", "")]
    [InlineData("// The session binds port 9000.\n// See someone/Reference@0123abc.", "")]
    [InlineData("// Porting notes follow.\n\n// someone/Reference@0123abc is a separate comment.", "")]
    [InlineData("var url = \"someone/Fork@0123abc\"; // ported", "")]
    public void Only_pins_in_comments_that_claim_a_port_are_checked(string source, string expected)
    {
        var pins = PinsInPortComments("src/Sample.cs", source).Select(p => $"{p.Line} {p.Repository}@{p.Sha}");
        Assert.Equal(expected, string.Join('|', pins));
    }

    [Theory]
    [InlineData(null, "does not exist")]
    [InlineData("PacketHeader.cs\nPermission is hereby granted, free of charge", "copyright line")]
    [InlineData("PacketHeader.cs\nCopyright (c) 2026 Erik Nihlén and OpenAC contributors", "permission notice")]
    [InlineData("Copyright (c) 2026 Erik Nihlén and OpenAC contributors\nPermission is hereby granted, free of charge", "does not name")]
    [InlineData("OtherPacketHeader.cs\nCopyright (c) 2026 Erik Nihlén and OpenAC contributors\nPermission is hereby granted", "does not name")]
    public void A_net_port_missing_from_the_notices_is_reported(string? notices, string expected)
    {
        var headers = PortHeader.Find(SampleNetPort, SampleOpenAcHeader);
        Assert.Contains(NoticesProblems(headers, notices), p => p.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void The_notices_check_passes_a_complete_file_and_ignores_other_ports()
    {
        Assert.Empty(NoticesProblems(PortHeader.Find(SampleNetPort, SampleOpenAcHeader), SampleNotices));
        Assert.Empty(NoticesProblems(PortHeader.Find("src/Lifestone.Data/Terrain/Sample.cs", SampleOpenAcHeader), null));
        var acePort = "// Ported from ACEmulator/ACE@7715c052 Source/P.cs (AGPL-3.0) with changes: none.\nnamespace Sample;\n";
        Assert.Empty(NoticesProblems(PortHeader.Find(SampleNetPort, acePort), null));
        Assert.Empty(NoticesProblems([], null));
    }

    [Theory]
    [InlineData("Nothing about it.", "no docs/third-party/*.md names PacketHeader.cs")]
    [InlineData("PacketHeader.cs is a port of the packet header.", "but not its source eriknihlen/OpenAC at 1ee5a08")]
    [InlineData("PacketHeader.cs follows eriknihlen/OpenAC at 0123abc.", "but not its source eriknihlen/OpenAC at 1ee5a08")]
    [InlineData("OldPacketHeader.cs follows eriknihlen/OpenAC at 1ee5a08.", "no docs/third-party/*.md names PacketHeader.cs")]
    [InlineData("PacketHeader.cs follows eriknihlen/OpenAC at 1ee5a08.", "")]
    public void A_ported_file_missing_from_docs_third_party_is_reported(string doc, string expected)
    {
        var (problems, stale) = ListingProblems(PortHeader.Find(SampleNetPort, SampleOpenAcHeader), [("docs/third-party/net.md", doc)], []);
        if (expected.Length == 0)
        {
            Assert.Empty(problems);
        }
        else
        {
            Assert.Contains(expected, Assert.Single(problems), StringComparison.Ordinal);
        }

        Assert.Empty(stale);
    }

    [Fact]
    public void An_unlisted_port_is_skipped_until_a_doc_names_it()
    {
        var headers = PortHeader.Find(SampleNetPort, SampleOpenAcHeader);
        var (problems, stale) = ListingProblems(headers, [("docs/third-party/net.md", "Nothing about it.")], [SampleNetPort]);
        Assert.Empty(problems);
        Assert.Empty(stale);

        (problems, stale) = ListingProblems(headers, [("docs/third-party/net.md", "PacketHeader.cs")], [SampleNetPort]);
        Assert.Empty(problems);
        Assert.Equal(SampleNetPort, Assert.Single(stale));
    }

    /// <summary>
    /// The pins cited in a file's comments that say code is ported, as (line, repository, SHA). A comment is a run of
    /// comment lines with the same marker, so a claim on one line covers a pin on the next.
    /// </summary>
    internal static IEnumerable<(int Line, string Repository, string Sha)> PinsInPortComments(string path, string text)
    {
        var comment = PortHeader.LineCommentFor(path);
        if (comment is null)
        {
            yield break;
        }

        var lines = CheckoutFiles.Lines(text);
        var i = 0;
        while (i < lines.Length)
        {
            if (!PortHeader.TrySplitComment(lines[i], comment, out var prefix, out _))
            {
                i++;
                continue;
            }

            var block = new List<(int Line, string Content)>();
            while (i < lines.Length && PortHeader.TrySplitComment(lines[i], comment, out var next, out var content) && next == prefix)
            {
                block.Add((i + 1, content));
                i++;
            }

            if (!PortClaim().IsMatch(string.Join(' ', block.Select(b => b.Content))))
            {
                continue;
            }

            foreach (var (line, content) in block)
            {
                foreach (Match m in PortHeader.PinCitation().Matches(content))
                {
                    yield return (line, m.Groups["repo"].Value, m.Groups["sha"].Value);
                }
            }
        }
    }

    /// <summary>
    /// What docs/third-party is missing for the ported files: a page must name each file and, for each source its headers
    /// cite, the repository and the approved pin. Files in <paramref name="unlisted"/> are skipped, and returned as stale
    /// once a page names them.
    /// </summary>
    internal static (List<string> Problems, List<string> Stale) ListingProblems(
        IEnumerable<PortHeader> headers, IReadOnlyList<(string Path, string Text)> docs, IReadOnlyCollection<string> unlisted)
    {
        var problems = new List<string>();
        var stale = new List<string>();
        foreach (var file in headers.GroupBy(h => h.Path, StringComparer.Ordinal))
        {
            var naming = docs.Where(d => NamesFile(d.Text, Path.GetFileName(file.Key))).ToArray();
            if (unlisted.Contains(file.Key))
            {
                if (naming.Length > 0)
                {
                    stale.Add(file.Key);
                }

                continue;
            }

            if (naming.Length == 0)
            {
                problems.Add($"{file.Key}: no docs/third-party/*.md names {Path.GetFileName(file.Key)}");
                continue;
            }

            foreach (var (repository, sha) in file.SelectMany(h => h.Pins()).Distinct())
            {
                if (PortHeader.PinProblem(repository, sha) is not null)
                {
                    continue; // reported by the grammar test
                }

                var pin = PortHeader.Approved.Single(a => a.Repository == repository).Pins.First(p => sha.StartsWith(p.Prefix, StringComparison.Ordinal));
                if (!naming.Any(d => d.Text.Contains(repository, StringComparison.Ordinal) && d.Text.Contains(pin.Prefix, StringComparison.Ordinal)))
                {
                    problems.Add($"{file.Key}: {string.Join(", ", naming.Select(d => d.Path))} name the file but not its source {repository} at {pin.Prefix}");
                }
            }
        }

        return (problems, stale);
    }

    /// <summary>
    /// What is wrong with the Lifestone.Net notices (<paramref name="notices"/>, null when the file does not exist) for
    /// the OpenAC ports among <paramref name="headers"/>: the file must name each of them and reproduce OpenAC's copyright
    /// line and the MIT permission notice. Empty when no file under src/Lifestone.Net cites OpenAC.
    /// </summary>
    internal static IEnumerable<string> NoticesProblems(IEnumerable<PortHeader> headers, string? notices)
    {
        var openAc = PortHeader.Approved.Single(a => a.Repository == "eriknihlen/OpenAC");
        var ports = headers
            .Where(h => h.Path.StartsWith("src/Lifestone.Net/", StringComparison.Ordinal) && h.Pins().Any(p => p.Repository == openAc.Repository))
            .Select(h => h.Path)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ports.Length == 0)
        {
            yield break;
        }

        if (notices is null)
        {
            yield return $"{ports.Length} files under src/Lifestone.Net port OpenAC code, but {NetNotices} does not exist";
            yield break;
        }

        if (!notices.Contains(openAc.CopyrightLine!, StringComparison.Ordinal))
        {
            yield return $"it must reproduce OpenAC's copyright line '{openAc.CopyrightLine}'";
        }

        if (!notices.Contains("Permission is hereby granted", StringComparison.Ordinal))
        {
            yield return "it must reproduce the MIT permission notice";
        }

        foreach (var port in ports.Where(p => !NamesFile(notices, Path.GetFileName(p))))
        {
            yield return $"it does not name {port}, which holds OpenAC code";
        }
    }

    private static bool NamesFile(string text, string fileName) =>
        Regex.IsMatch(text, @"(?<![A-Za-z0-9_])" + Regex.Escape(fileName) + "(?![A-Za-z0-9_])");
}
