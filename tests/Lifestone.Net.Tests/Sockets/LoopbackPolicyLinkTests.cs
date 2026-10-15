using System.Text.RegularExpressions;
using Lifestone.Client.Tests;

namespace Lifestone.Net.Tests.Sockets;

/// <summary>
/// The server mod compile-links <c>LoopbackPolicy.cs</c> and <c>LoopbackRequiredException.cs</c> into its own project
/// (m2-plan §1.4, SM1), which may use neither implicit usings nor a nullable context and has no other Lifestone.Net
/// type. These checks keep the two files linkable alone.
/// </summary>
public sealed partial class LoopbackPolicyLinkTests
{
    private static readonly string[] LinkedFiles = ["LoopbackPolicy.cs", "LoopbackRequiredException.cs"];

    /// <summary>A Lifestone.Net type outside Sockets, or a using of another Lifestone namespace.</summary>
    [GeneratedRegex(@"(?<!System\.)\b(?:Session|Dispatch|Taps|Types|Messages|Diagnostics)\s*\.\s*[A-Z]|\busing\s+Lifestone\b(?!\.Net\.Sockets\b)")]
    private static partial Regex OtherNetType();

    [Fact]
    public void The_linked_files_set_their_own_context_and_name_no_other_lifestone_type()
    {
        foreach (var name in LinkedFiles)
        {
            var text = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Net", "Sockets", name));
            Assert.StartsWith("#nullable enable\nusing System;\n", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);

            var code = string.Join('\n', text.ReplaceLineEndings("\n").Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            var offences = OtherNetType().Matches(code).Select(m => m.Value).ToArray();
            Assert.True(offences.Length == 0, $"{name} names {string.Join(", ", offences)}");
        }
    }
}
