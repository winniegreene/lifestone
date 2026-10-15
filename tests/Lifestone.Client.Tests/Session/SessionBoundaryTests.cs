using System.Text.RegularExpressions;
using Lifestone.Client.Tests.Repository;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// DoD 13's boundary (step ST13; docs/session.md §1): nothing that plays the game talks to the network session. The
/// network layer's session types (the session, its factory, its sink, its tap, its dispatcher, the credentials) are named
/// only by the session layer (<c>src/Lifestone.Client/Session/</c>) and, in the game, by the session host's folder
/// (<c>scripts/Session/</c>); everything else holds <c>IGameSessionView</c>. The headless host reaches the session through
/// the hosting types only. Scanned in code with comments and literals blanked. ArchitectureTests already forbids naming
/// Lifestone.Net in Lifestone.Game, sockets and files in Lifestone.Client, and <c>AcSession.Create</c> outside
/// <c>SessionBootstrap.cs</c>; this adds the names a global using could hide.
/// </summary>
public sealed partial class SessionBoundaryTests
{
    private const string GameScripts = "src/Lifestone.Game/";
    private const string GameSessionFolder = "src/Lifestone.Game/scripts/Session/";
    private const string ClientFolder = "src/Lifestone.Client/";
    private const string SessionFolder = "src/Lifestone.Client/Session/";
    private const string HeadlessFolder = "src/Lifestone.Headless/";

    /// <summary>The network session's own types.</summary>
    [GeneratedRegex(@"\b(?:IAcSession|AcSession|IAcSessionSink|IRawMessageTap|InboundDispatcher|AcCredentials|SessionMachine|AcSessionOptions)\b")]
    private static partial Regex NetworkSessionTypes();

    /// <summary>The live session's creation and its internals.</summary>
    [GeneratedRegex(@"\bSessionBootstrap\b|\bGameStateReducer\b|\bActionApi\b|\bEventBus\b")]
    private static partial Regex SessionInternals();

    [Fact]
    public void Only_scripts_Session_touches_the_network_session()
    {
        var offences = Scan(CheckoutFiles.CSharpUnder(GameScripts).Where(p => !p.StartsWith(GameSessionFolder, StringComparison.Ordinal)), NetworkSessionTypes());
        offences.AddRange(Scan(CheckoutFiles.CSharpUnder(GameScripts).Where(p => !p.StartsWith(GameSessionFolder, StringComparison.Ordinal)), SessionInternals()));
        Assert.True(offences.Count == 0, "Lifestone.Game outside scripts/Session names the network session (DoD 13: use IGameSessionView):\n" + string.Join('\n', offences));
    }

    [Fact]
    public void The_client_outside_the_session_layer_never_touches_the_network_session()
    {
        var sources = CheckoutFiles.CSharpUnder(ClientFolder).Where(p => !p.StartsWith(SessionFolder, StringComparison.Ordinal)).ToList();
        Assert.True(sources.Count > 50, $"only {sources.Count} client sources outside Session/ were scanned; did the folder move?");
        var offences = Scan(sources, NetworkSessionTypes());
        Assert.True(offences.Count == 0, "Lifestone.Client outside Session/ names the network session (use IGameSessionView):\n" + string.Join('\n', offences));
    }

    [Fact]
    public void The_headless_host_reaches_the_session_through_the_hosting_types()
    {
        var sources = CheckoutFiles.CSharpUnder(HeadlessFolder).ToList();
        Assert.Contains(HeadlessFolder + "Program.cs", sources);
        var offences = Scan(sources, NetworkSessionTypes());
        Assert.True(offences.Count == 0, "the headless host names the network session (it goes through HeadlessRunner and SessionBootstrap):\n" + string.Join('\n', offences));
    }

    [Theory]
    [InlineData("IAcSession net = null;", true)]
    [InlineData("var s = AcSession.Create(o, sink);", true)]
    [InlineData("using var c = new AcCredentials(a, p);", true)]
    [InlineData("IGameSessionView view = host.Session;", false)]
    [InlineData("// IAcSession is the network session", false)]
    [InlineData("var text = \"InboundDispatcher\";", false)]
    public void The_rule_sees_code_not_comments_or_strings(string line, bool offends)
    {
        Assert.Equal(offends, NetworkSessionTypes().IsMatch(CSharpText.CodeOnly(line)));
    }

    private static List<string> Scan(IEnumerable<string> files, Regex rule)
    {
        var offences = new List<string>();
        foreach (var file in files)
        {
            var code = CheckoutFiles.ReadCSharpCode(file);
            foreach (Match m in rule.Matches(code))
            {
                offences.Add($"{file}:{CheckoutFiles.LineOf(code, m.Index)}: {m.Value}");
            }
        }

        return offences;
    }
}
