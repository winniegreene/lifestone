using System.Text.RegularExpressions;

namespace Lifestone.Client.Tests.Repository;

/// <summary>A third-party repository that Lifestone may port code from, pinned to the commits the owner approved.</summary>
/// <param name="Repository">The <c>owner/repo</c> name a header cites.</param>
/// <param name="Licence">The SPDX identifier the header must name for it.</param>
/// <param name="CopyrightLine">For an MIT source, the copyright line of its LICENSE, which a port must keep; else null.</param>
/// <param name="Pins">The approved commits.</param>
internal sealed record ApprovedSource(string Repository, string Licence, string? CopyrightLine, IReadOnlyList<ApprovedPin> Pins);

/// <summary>An approved commit: a cited SHA must start with <paramref name="Prefix"/> and be a prefix of <paramref name="Full"/>.</summary>
/// <param name="Prefix">The short SHA the plan pins (docs/milestones/m2-plan.md §1.3 and §2.3).</param>
/// <param name="Full">The full SHA where the repository records it, else null.</param>
/// <param name="Where">Where the pin is recorded.</param>
internal sealed record ApprovedPin(string Prefix, string? Full, string Where);

/// <summary>
/// A port header: the comment that names the third-party source of code ported into Lifestone, in the M1 grammar of
/// <c>src/Lifestone.Data/Animation/MotionCommands.cs:1-3</c>:
/// <c>// [Partly ported|Ported[ (qualifier)]] from owner/repo@sha source-paths (licence[; copyright line]) [with changes: ...]</c>,
/// where every further <c>owner/repo@sha</c> it cites is followed by its own licence. A file header (<c>//</c>) starts
/// on line 1; a member's header is a <c>///</c> remarks paragraph.
/// </summary>
/// <param name="Path">The repository-relative file.</param>
/// <param name="Line">The 1-based line the header starts on.</param>
/// <param name="CommentPrefix"><c>//</c>, <c>///</c> or <c>#</c>.</param>
/// <param name="EndLine">The 1-based last line of the header.</param>
/// <param name="Statement">The header's text with the comment markers removed and its lines joined by spaces.</param>
internal sealed partial record PortHeader(string Path, int Line, string CommentPrefix, int EndLine, string Statement)
{
    /// <summary>
    /// The sources Lifestone ports from. Adding a repository or a pin needs the owner's approval (CLAUDE.md, "Third-party
    /// code"); the full SHAs are the ones server/versions.json, docs/protocol.md and docs/third-party/terrain.md record.
    /// </summary>
    public static readonly IReadOnlyList<ApprovedSource> Approved =
    [
        new("ACEmulator/ACE", "AGPL-3.0", null,
        [
            new("95ef426d", "95ef426d412459ff619a12cc18f82bcf72c1c2a8", "v1.78.4816, the bundled server (server/versions.json)"),
            new("7715c052", "7715c052c2038a96e57c477789cafd4d870e0d4d", "master, the protocol ground truth (docs/protocol.md)"),
        ]),
        new("eriknihlen/OpenAC", "MIT", "Copyright (c) 2026 Erik Nihlén and OpenAC contributors",
        [
            new("1ee5a08", "1ee5a08581a7f4b0b22effdf911171a76cf4391c", "v0.1.24 (docs/protocol.md, docs/third-party/terrain.md)"),
            new("f22b048", "f22b04851c5fcc3f38acfe50fb704ca38130c081", "the signed head of M4 D7 (docs/research/foundation-openac.md, docs/third-party/net.md)"),
        ]),
        new("ACEmulator/ACViewer", "GPL-3.0", null,
        [
            new("c5c54fb", "c5c54fb3318cd956a330a920c105a0109aaeb3fd", "docs/third-party/terrain.md"),
        ]),
        new("Chorizite/DatReaderWriter", "MIT", "Copyright 2024 ACClientLib",
        [
            new("a9345f2", null, "the commit NuGet 2.1.9 was built from (m2-plan §1.3)"),
        ]),
    ];

    /// <summary>The comment lines of a file in a language the scan reads, by extension; null for other files.</summary>
    public static string? LineCommentFor(string path) => System.IO.Path.GetExtension(path) switch
    {
        ".cs" or ".gdshader" or ".gdshaderinc" or ".glsl" or ".js" => "//",
        ".ps1" or ".psm1" or ".py" => "#",
        _ => null,
    };

    /// <summary>A comment line that starts a header (or a malformed attempt at one): "Ported" or "Partly ported" first.</summary>
    [GeneratedRegex(@"^(?:partly\s+)?ported\b", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderStart();

    /// <summary>The grammar of the header's opening words.</summary>
    [GeneratedRegex(@"^(?:Partly ported|Ported)(?: \([^()]+\))? from (?<repo>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)@(?<sha>[0-9A-Za-z]+)(?=[\s(:,;]|$)")]
    private static partial Regex Opening();

    /// <summary>An <c>owner/repo@sha</c> pin in a header (any SHA-like word, so a malformed one is reported).</summary>
    [GeneratedRegex(@"(?<![\w/.-])(?<repo>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)@(?<sha>[0-9A-Za-z]+)")]
    private static partial Regex Pin();

    /// <summary>A pin cited anywhere in a comment: <c>owner/repo@</c> followed by 7 to 40 lowercase hex digits.</summary>
    [GeneratedRegex(@"(?<![\w/.-])(?<repo>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)@(?<sha>[0-9a-f]{7,40})(?![0-9A-Za-z])")]
    public static partial Regex PinCitation();

    /// <summary>A licence parenthesis, with the copyright line an MIT port keeps: <c>(MIT; Copyright (c) 2026 Name)</c>.</summary>
    [GeneratedRegex(@"\((?<licence>AGPL-3\.0|GPL-3\.0|LGPL-[23]\.[01]|MIT|Apache-2\.0|BSD-[23]-Clause|MPL-2\.0)(?:;\s*(?<copyright>Copyright[^()]*(?:\([cC]\)[^()]*)?))?\)")]
    private static partial Regex Licence();

    /// <summary>An XML doc line that closes the remarks or opens another block: the end of a member's header.</summary>
    [GeneratedRegex(@"^</|^<(?:para|summary|remarks|returns|param|typeparam|example|list|item|exception|seealso)\b")]
    private static partial Regex DocTag();

    /// <summary>A comment line that says "ported" and names a licence: a port notice outside the grammar.</summary>
    [GeneratedRegex(@"(?i)\bported\b.*\b(?:A?GPL|LGPL|MIT|Apache|BSD|MPL)\b|\b(?:A?GPL|LGPL|MIT|Apache|BSD|MPL)\b.*\bported\b")]
    public static partial Regex PortNotice();

    /// <summary>The headers of one file, found by their first words (so a malformed header is found too).</summary>
    public static IReadOnlyList<PortHeader> Find(string path, string text)
    {
        var comment = LineCommentFor(path);
        if (comment is null)
        {
            return [];
        }

        var lines = CheckoutFiles.Lines(text);
        var headers = new List<PortHeader>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!TrySplitComment(lines[i], comment, out var prefix, out var content) || !HeaderStart().IsMatch(content))
            {
                continue;
            }

            var parts = new List<string> { content };
            var end = i;
            while (end + 1 < lines.Length
                && TrySplitComment(lines[end + 1], comment, out var nextPrefix, out var next)
                && nextPrefix == prefix
                && next.Length > 0
                && !(prefix == "///" && DocTag().IsMatch(next)))
            {
                parts.Add(next);
                end++;
            }

            headers.Add(new PortHeader(path, i + 1, prefix, end + 1, string.Join(' ', parts)));
            i = end;
        }

        return headers;
    }

    /// <summary>Splits a comment line into its marker (<c>//</c>, <c>///</c> or <c>#</c>) and its trimmed text.</summary>
    public static bool TrySplitComment(string line, string comment, out string prefix, out string content)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith(comment, StringComparison.Ordinal))
        {
            prefix = content = "";
            return false;
        }

        prefix = comment == "//" && trimmed.StartsWith("///", StringComparison.Ordinal) && !trimmed.StartsWith("////", StringComparison.Ordinal)
            ? "///"
            : comment;
        content = trimmed[prefix.Length..].Trim();
        return true;
    }

    /// <summary>The <c>owner/repo@sha</c> pins the header cites, in order.</summary>
    public IEnumerable<(string Repository, string Sha)> Pins() =>
        Pin().Matches(Statement).Select(m => (m.Groups["repo"].Value, m.Groups["sha"].Value));

    /// <summary>What is wrong with the header's grammar, pins and licences; empty when it is well-formed.</summary>
    public IEnumerable<string> Problems()
    {
        if (CommentPrefix == "//" && Line != 1)
        {
            yield return "a file's port header must start on line 1";
        }

        if (!Opening().IsMatch(Statement))
        {
            yield return "the header must open with 'Ported from owner/repo@sha' (or 'Partly ported from', 'Ported (qualifier) from')";
        }

        if (!Statement.Contains("with changes", StringComparison.Ordinal))
        {
            yield return "the header must say 'with changes' and list them (or state that there are none)";
        }

        var pins = Pin().Matches(Statement);
        if (pins.Count == 0)
        {
            yield break;
        }

        for (var p = 0; p < pins.Count; p++)
        {
            var repository = pins[p].Groups["repo"].Value;
            var sha = pins[p].Groups["sha"].Value;
            if (PinProblem(repository, sha) is { } pinProblem)
            {
                yield return pinProblem;
                continue;
            }

            var source = Approved.Single(a => a.Repository == repository);
            var segmentStart = pins[p].Index + pins[p].Length;
            var segmentEnd = p + 1 < pins.Count ? pins[p + 1].Index : Statement.Length;
            var licence = Licence().Match(Statement[segmentStart..segmentEnd]);
            if (!licence.Success)
            {
                yield return $"{repository} is cited without its licence in parentheses, e.g. '({source.Licence})'";
                continue;
            }

            if (licence.Groups["licence"].Value != source.Licence)
            {
                yield return $"{repository} is cited as {licence.Groups["licence"].Value}; its licence is {source.Licence}";
            }

            var copyright = licence.Groups["copyright"].Success ? licence.Groups["copyright"].Value.Trim() : null;
            if (source.CopyrightLine is not null && copyright != source.CopyrightLine)
            {
                yield return $"an MIT port of {repository} must keep its copyright line: '({source.Licence}; {source.CopyrightLine})'";
            }
        }
    }

    /// <summary>Why a cited pin is not approved, or null when it is.</summary>
    public static string? PinProblem(string repository, string sha)
    {
        var source = Approved.FirstOrDefault(a => a.Repository == repository);
        if (source is null)
        {
            return $"{repository} is not an approved source (PortHeader.Approved; a new source needs the owner's approval)";
        }

        if (sha.Length is < 7 or > 40 || !sha.All(char.IsAsciiHexDigitLower))
        {
            return $"the {repository} pin must be 7 to 40 lowercase hex digits";
        }

        return source.Pins.Any(p => sha.StartsWith(p.Prefix, StringComparison.Ordinal) && (p.Full is null || p.Full.StartsWith(sha, StringComparison.Ordinal)))
            ? null
            : $"{repository}@{sha} is not an approved pin (approved: {string.Join(", ", source.Pins.Select(p => p.Prefix))})";
    }
}
