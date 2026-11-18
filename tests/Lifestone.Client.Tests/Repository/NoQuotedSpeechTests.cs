using System.Text.RegularExpressions;

namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// The repository is public (CLAUDE.md, "The repository is public"): tracked files carry no quotes from conversations.
/// A decision of the owner is written as a dated ruling in plain words (the owner's ruling, 2026-11-16: use Life Magic on a
/// target bolts cannot reach), never as the words themselves. This scans the source, tools, tests, docs and server files
/// for a quoted sentence that follows "the owner" on the same line and fails on any match, naming file and line.
/// M6 16 Q13.
/// </summary>
public sealed class NoQuotedSpeechTests
{
    private static readonly string[] Folders = ["src/", "tools/", "tests/", "docs/", "server/"];

    /// <summary>The words "the owner", then within 40 characters a quoted run of 4 to 120 characters, on one line.</summary>
    private static readonly Regex Quoted = new(@"the owner[^""\r\n]{0,40}""[^""\r\n]{4,120}""", RegexOptions.CultureInvariant);

    /// <summary>This file holds the pattern as text, so it is not scanned.</summary>
    private const string Self = "tests/Lifestone.Client.Tests/Repository/NoQuotedSpeechTests.cs";

    /// <summary>
    /// The only lines the pattern may find: code whose string is a source label a test asserts, or game text, never a
    /// person's words. Each is a file and the exact text of the matched span; keep it short.
    /// </summary>
    private static readonly (string File, string Text)[] Allowed =
    [
        ("tests/Lifestone.Client.Tests/Session/Grounds/LadderTests.cs", "the owner, 2026-11-13\", ladder.RungFor(40).Sources[\""),
        ("tests/Lifestone.Client.Tests/Session/Grounds/LadderTests.cs", "the owner's decision of 2026-11-08\", ladder.Sources[\""),
    ];

    /// <summary>The matches at or past this branch's tip: file, line and the matched text, for the fix.</summary>
    private static List<string> Matches()
    {
        var found = new List<string>();
        foreach (var path in CheckoutFiles.All)
        {
            if (path == Self || !Folders.Any(f => path.StartsWith(f, StringComparison.Ordinal)))
            {
                continue;
            }

            var text = CheckoutFiles.TryReadText(path);
            if (text is null)
            {
                continue;
            }

            var lines = CheckoutFiles.Lines(text);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in Quoted.Matches(lines[i]))
                {
                    if (!Allowed.Any(a => a.File == path && a.Text == m.Value))
                    {
                        found.Add($"{path}:{i + 1}: {m.Value}");
                    }
                }
            }
        }

        return found;
    }

    [Fact]
    public void No_tracked_file_quotes_the_owner()
    {
        var found = Matches();
        Assert.True(found.Count == 0,
            "A tracked file quotes the owner (CLAUDE.md, The repository is public). Write the decision as a dated ruling in your own words instead:\n"
            + string.Join('\n', found));
    }
}
