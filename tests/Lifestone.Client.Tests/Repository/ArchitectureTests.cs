using System.Text.RegularExpressions;

namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// The project boundaries (docs/game-core.md §1; docs/milestones/m2-plan.md §1.6 item 5 and §2.3 L11), checked in the
/// sources (with comments and literals blanked, so a comment can neither trip nor dodge a rule) and, where a source scan
/// could be dodged by a <c>using static</c> or an alias, in the built assemblies' metadata (<see cref="BuiltAssemblies"/>):
/// <list type="bullet">
/// <item>only Lifestone.Game uses Godot;</item>
/// <item>only Lifestone.Net uses sockets, and nothing in src/ uses HTTP or the engine's networking;</item>
/// <item>Lifestone.Client does no file or process I/O (hosts supply bytes and streams);</item>
/// <item>Lifestone.Net and the Client's session layer read no wall clock and never sleep (System.TimeProvider instead);</item>
/// <item>only DatSet touches DatReaderWriter's DatCollection, which always opens client_highres.dat (an error when it is
/// missing) and reads the portal before high-res;</item>
/// <item>Lifestone.Game never names Lifestone.Net, and only its SessionHost creates sessions, the offline stand-in included (DoD 13).</item>
/// </list>
/// </summary>
public sealed partial class ArchitectureTests
{
    private const string GameFolder = "src/Lifestone.Game/";
    private const string NetFolder = "src/Lifestone.Net/";
    private const string ClientFolder = "src/Lifestone.Client/";
    private const string ClientSessionFolder = "src/Lifestone.Client/Session/";
    private const string DatSetFile = "src/Lifestone.Data/Dat/DatSet.cs";
    private const string SessionHostFile = "src/Lifestone.Game/scripts/Session/SessionHost.cs";
    private const string SessionBootstrapFile = "src/Lifestone.Client/Session/SessionBootstrap.cs";

    /// <summary>A use of the Godot API from C#: <c>using Godot;</c>, <c>Godot.Vector3</c>, <c>global::Godot.GD</c>.</summary>
    [GeneratedRegex(@"(?<![\w.])(?:global::)?Godot\s*[.;]")]
    private static partial Regex GodotUse();

    /// <summary>A Godot project SDK, package or project reference in an MSBuild file.</summary>
    [GeneratedRegex(@"Godot|Lifestone\.Game\.csproj")]
    private static partial Regex GodotInProject();

    [GeneratedRegex(@"\bSystem\.Net\.Sockets\b|\b(?:UdpClient|TcpClient|TcpListener|NetworkStream)\b|\bnew\s+Socket\s*\(")]
    private static partial Regex SocketUse();

    /// <summary>HTTP and WebSocket clients, and the engine's networking classes.</summary>
    [GeneratedRegex(@"\bSystem\.Net\.(?:Http|WebSockets)\b|\b(?:HttpClient|ClientWebSocket|HttpRequest|PacketPeerUdp|UdpServer|TcpServer|StreamPeerTcp|WebSocketPeer|ENetConnection|ENetMultiplayerPeer|WebRtcPeerConnection)\b")]
    private static partial Regex OtherNetworkingUse();

    /// <summary>File-system and process access: <c>File.</c>, <c>Directory.</c>, <c>FileStream</c>, <c>Process.</c> and their kin.</summary>
    [GeneratedRegex(@"(?<![\w.])(?:global::)?(?:System\.IO\.)?(?:File|Directory)\s*\.\s*[A-Z]|\b(?:FileStream|FileInfo|DirectoryInfo|DriveInfo|FileSystemWatcher|RandomAccess)\b|(?<![\w.])(?:global::)?(?:System\.Diagnostics\.)?Process\s*\.\s*[A-Z]|\bProcessStartInfo\b|\bnew\s+Process\s*\(")]
    private static partial Regex FileOrProcessUse();

    /// <summary>Wall-clock reads and sleeps.</summary>
    [GeneratedRegex(@"\bDateTime(?:Offset)?\s*\.\s*(?:UtcNow|Now|Today)\b|\bEnvironment\s*\.\s*TickCount(?:64)?\b|\bStopwatch\b|\bThread\s*\.\s*Sleep\b")]
    private static partial Regex ClockUse();

    [GeneratedRegex(@"\bDatCollection\b")]
    private static partial Regex DatCollectionUse();

    /// <summary>A line that names QualifiedDataId and resolves one (<c>((QualifiedDataId&lt;T&gt;)x).Get(dats)</c>).</summary>
    [GeneratedRegex(@"\bQualifiedDataId\b.*\.\s*(?:Try)?Get\s*[(<]")]
    private static partial Regex QualifiedDataIdGet();

    [GeneratedRegex(@"\bLifestone\.Net\b")]
    private static partial Regex LifestoneNetToken();

    /// <summary>
    /// Creating a session: <c>GameSession.Create*</c>, <c>SessionBootstrap.CreateLive</c> (m2-plan §2.3 L11) and the stand-in
    /// <c>GameSession.Offline</c>. The plan had the retail screens' host create the stand-in outside the SessionHost (m2-plan
    /// §5.3 U3); G6 W9 moved that creation into <c>SessionHost.CreateStandIn</c>, so one file in Lifestone.Game makes every
    /// session the game runs (the live one and the offline stand-in of <c>--retail-ui=standin</c> runs).
    /// </summary>
    [GeneratedRegex(@"\bGameSession\s*\.\s*(?:Create\w*|Offline)\b|\bSessionBootstrap\s*\.\s*CreateLive\b")]
    private static partial Regex SessionCreation();

    [GeneratedRegex(@"\bAcSession\s*\.\s*Create\b")]
    private static partial Regex AcSessionCreate();

    /// <summary>A Compile item that links source from outside the project (<c>..\..\server\mod\Shared\**\*.cs</c>).</summary>
    [GeneratedRegex(@"<Compile\s+Include=""(?<glob>[^""]+)""")]
    private static partial Regex LinkedCompile();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlComment();

    private static IReadOnlyList<AssemblyMetadata> Built => BuiltAssemblies.All;

    private static IEnumerable<string> SourcesOutside(string folder) =>
        CheckoutFiles.CSharpUnder("src/").Concat(CheckoutFiles.CSharpUnder("tests/")).Concat(CheckoutFiles.CSharpUnder("server/mod/"))
            .Where(p => !p.StartsWith(folder, StringComparison.Ordinal));

    /// <summary>
    /// Lifestone.Client's sources: its folder plus every folder its project compile-links (the server mod's shared wire
    /// code, M2 L1), which builds into the same assembly and so obeys the same rules.
    /// </summary>
    private static IReadOnlyList<string> ClientSources()
    {
        var project = CheckoutFiles.ReadText(ClientFolder + "Lifestone.Client.csproj");
        var folders = new List<string> { ClientFolder };
        foreach (Match m in LinkedCompile().Matches(XmlComment().Replace(project, "")))
        {
            var glob = m.Groups["glob"].Value.Replace('\\', '/');
            var wildcard = glob.IndexOfAny(['*', '?']);
            var folder = wildcard < 0 ? glob[..(glob.LastIndexOf('/') + 1)] : glob[..wildcard];
            folders.Add(Resolve(ClientFolder, folder));
        }

        return [.. folders.SelectMany(CheckoutFiles.CSharpUnder).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Resolves a project-relative folder (<c>../../server/mod/Shared/</c>) against the project's folder.</summary>
    private static string Resolve(string projectFolder, string relative)
    {
        var parts = projectFolder.TrimEnd('/').Split('/').ToList();
        foreach (var part in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "..")
            {
                parts.RemoveAt(parts.Count - 1);
            }
            else if (part != ".")
            {
                parts.Add(part);
            }
        }

        return string.Join('/', parts) + "/";
    }

    /// <summary>Every match of a rule in a set of files, as <c>path:line: token</c>.</summary>
    private static List<string> Offences(IEnumerable<string> files, Regex rule, Func<string, string> view)
    {
        var offences = new List<string>();
        foreach (var file in files)
        {
            var text = view(file);
            foreach (Match m in rule.Matches(text))
            {
                offences.Add($"{file}:{CheckoutFiles.LineOf(text, m.Index)}: {m.Value.Trim()}");
            }
        }

        return offences;
    }

    private static void AssertNone(List<string> offences, string rule) =>
        Assert.True(offences.Count == 0, rule + ":\n" + string.Join('\n', offences));

    [Fact]
    public void The_scans_see_the_projects()
    {
        Assert.True(ClientSources().Count > 50, "too few Lifestone.Client sources; did the scan break?");
        Assert.True(CheckoutFiles.CSharpUnder(GameFolder).Count() > 50, "too few Lifestone.Game sources; did the scan break?");
        Assert.True(CheckoutFiles.CSharpUnder("src/Lifestone.Data/").Count() > 50, "too few Lifestone.Data sources; did the scan break?");
        Assert.Contains(DatSetFile, CheckoutFiles.All);
        Assert.Contains(Built, a => a.Name == "Lifestone.Client");
        Assert.Contains(Built, a => a.Name == "Lifestone.Data");
        if (CheckoutFiles.All.Contains(BuiltAssemblies.NetProject))
        {
            Assert.True(
                Built.Any(a => a.Name == "Lifestone.Net"),
                $"{BuiltAssemblies.NetProject} exists but Lifestone.Net.dll is not next to the tests, so its metadata checks would be skipped");
        }
    }

    [Fact]
    public void Only_Lifestone_Game_uses_Godot()
    {
        var projects = CheckoutFiles.All
            .Where(p => p.EndsWith(".csproj", StringComparison.Ordinal) || p.EndsWith(".props", StringComparison.Ordinal) || p.EndsWith(".targets", StringComparison.Ordinal))
            .Where(p => !p.StartsWith(GameFolder, StringComparison.Ordinal));
        AssertNone(Offences(projects, GodotInProject(), p => XmlComment().Replace(CheckoutFiles.ReadText(p), m => Blank(m.Value))), "Godot in a project file outside Lifestone.Game");
        AssertNone(Offences(SourcesOutside(GameFolder), GodotUse(), CheckoutFiles.ReadCSharpCode), "Godot API use outside Lifestone.Game");
        var references = Built.SelectMany(a => a.AssemblyReferences.Where(r => r.StartsWith("Godot", StringComparison.Ordinal)).Select(r => $"{a.Name} references {r}")).ToList();
        AssertNone(references, "Godot assemblies referenced outside Lifestone.Game");
    }

    [Fact]
    public void Only_Lifestone_Net_uses_sockets()
    {
        var sources = CheckoutFiles.CSharpUnder("src/").Concat(ClientSources()).Distinct(StringComparer.Ordinal)
            .Where(p => !p.StartsWith(NetFolder, StringComparison.Ordinal));
        AssertNone(Offences(sources, SocketUse(), CheckoutFiles.ReadCSharpCode), "sockets outside src/Lifestone.Net");
        var references = Built.Where(a => a.Name != "Lifestone.Net")
            .SelectMany(a => a.AssemblyReferences.Where(r => r == "System.Net.Sockets").Select(r => $"{a.Name} references {r}")).ToList();
        AssertNone(references, "the sockets assembly referenced outside Lifestone.Net");
    }

    [Fact]
    public void Nothing_in_src_uses_http_or_the_engines_networking()
    {
        var sources = CheckoutFiles.CSharpUnder("src/").Concat(ClientSources()).Distinct(StringComparer.Ordinal);
        AssertNone(Offences(sources, OtherNetworkingUse(), CheckoutFiles.ReadCSharpCode), "HTTP, WebSocket or engine networking in src/ (the only network peer is ACE on loopback, through Lifestone.Net)");
        var references = Built
            .SelectMany(a => a.AssemblyReferences.Where(r => r is "System.Net.Http" or "System.Net.WebSockets" or "System.Net.WebSockets.Client").Select(r => $"{a.Name} references {r}")).ToList();
        AssertNone(references, "HTTP or WebSocket assemblies referenced");
    }

    [Fact]
    public void Lifestone_Client_does_no_file_or_process_io()
    {
        AssertNone(Offences(ClientSources(), FileOrProcessUse(), CheckoutFiles.ReadCSharpCode), "file or process I/O in Lifestone.Client (hosts supply bytes and Streams)");
        string[] forbidden =
        [
            "System.IO.File", "System.IO.Directory", "System.IO.FileStream", "System.IO.FileInfo", "System.IO.DirectoryInfo",
            "System.IO.DriveInfo", "System.IO.FileSystemInfo", "System.IO.FileSystemWatcher", "System.IO.RandomAccess",
            "System.Diagnostics.Process", "System.Diagnostics.ProcessStartInfo",
        ];
        var references = BuiltAssemblies.Named("Lifestone.Client").TypeReferences.Where(t => forbidden.Contains(t)).Select(t => $"Lifestone.Client references {t}").ToList();
        AssertNone(references, "file or process types referenced by Lifestone.Client");
    }

    [Fact]
    public void The_network_and_session_layers_read_no_wall_clock_and_never_sleep()
    {
        var sources = CheckoutFiles.CSharpUnder(NetFolder).Concat(CheckoutFiles.CSharpUnder(ClientSessionFolder))
            .Where(p => Path.GetFileName(p) != "SessionClock.cs");
        AssertNone(Offences(sources, ClockUse(), CheckoutFiles.ReadCSharpCode), "wall-clock reads or sleeps in src/Lifestone.Net or src/Lifestone.Client/Session (use System.TimeProvider; SessionClock.cs is the one exception)");

        var uses = Built
            .Where(a => a.Name is "Lifestone.Net" or "Lifestone.Client")
            .SelectMany(a => a.MemberUses.Where(u => a.Name == "Lifestone.Net" || u.Caller.StartsWith("Lifestone.Client.Session.", StringComparison.Ordinal)))
            .Where(u => !u.Caller.EndsWith(".SessionClock", StringComparison.Ordinal) && IsClockMember(u))
            .Select(u => $"{u.Caller} uses {u.Type}.{u.Member}")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        AssertNone(uses, "wall-clock reads or sleeps in the built network and session code");
    }

    private static bool IsClockMember(MemberUse u) => (u.Type, u.Member) switch
    {
        ("System.DateTime", "get_Now" or "get_UtcNow" or "get_Today") => true,
        ("System.DateTimeOffset", "get_Now" or "get_UtcNow") => true,
        ("System.Environment", "get_TickCount" or "get_TickCount64") => true,
        ("System.Diagnostics.Stopwatch", _) => true,
        ("System.Threading.Thread", "Sleep") => true,
        _ => false,
    };

    [Fact]
    public void Only_DatSet_uses_DatCollection_or_resolves_a_QualifiedDataId()
    {
        var sources = CheckoutFiles.CSharpUnder("src/").Concat(CheckoutFiles.CSharpUnder("tests/")).Where(p => p != DatSetFile).ToArray();
        AssertNone(Offences(sources, DatCollectionUse(), CheckoutFiles.ReadCSharpCode), "DatCollection outside DatSet.cs (it always opens client_highres.dat, an error when that is missing, and reads the portal before high-res)");
        AssertNone(Offences(sources, QualifiedDataIdGet(), CheckoutFiles.ReadCSharpCode), "QualifiedDataId resolved outside DatSet.cs (it needs a DatCollection)");

        var uses = Built
            .SelectMany(a => a.MemberUses)
            .Where(u => IsDatCollection(u.Type) || (u.Type.EndsWith(".QualifiedDataId`1", StringComparison.Ordinal) && u.Member is "Get" or "TryGet"))
            .Where(u => u.Caller != "Lifestone.Data.Dat.DatSet")
            .Select(u => $"{u.Caller} uses {u.Type}.{u.Member}")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        uses.AddRange(Built.Where(a => a.Name != "Lifestone.Data" && a.TypeReferences.Any(IsDatCollection)).Select(a => $"{a.Name} references DatCollection"));
        AssertNone(uses, "DatCollection or QualifiedDataId resolution in the built assemblies outside DatSet");
    }

    private static bool IsDatCollection(string type) => type == "DatCollection" || type.EndsWith(".DatCollection", StringComparison.Ordinal);

    [Fact]
    public void Lifestone_Game_never_names_Lifestone_Net()
    {
        // Any mention counts, comments included (DoD 13): the Game reaches the session layer only through Lifestone.Client.
        // packages.lock.json is generated by restore and lists the transitive project graph, so it is not a mention.
        var files = CheckoutFiles.Under(GameFolder)
            .Where(p => !p.EndsWith("/packages.lock.json", StringComparison.Ordinal) && CheckoutFiles.TryReadText(p) is not null);
        AssertNone(Offences(files, LifestoneNetToken(), CheckoutFiles.ReadText), "Lifestone.Net named in Lifestone.Game");
    }

    [Fact]
    public void Only_the_SessionHost_creates_sessions_in_Lifestone_Game()
    {
        var sources = CheckoutFiles.CSharpUnder(GameFolder).Where(p => p != SessionHostFile);
        AssertNone(Offences(sources, SessionCreation(), CheckoutFiles.ReadCSharpCode), $"sessions created outside {SessionHostFile} (DoD 13)");
    }

    [Fact]
    public void Only_the_SessionBootstrap_opens_a_network_session()
    {
        // The session facade's contract (m2-plan §4.7): SessionBootstrap.CreateLive is the only product caller of
        // AcSession.Create; tests (net's and the loopback suites) may call it directly.
        var sources = CheckoutFiles.CSharpUnder("src/").Concat(ClientSources()).Distinct(StringComparer.Ordinal)
            .Where(p => !p.StartsWith(NetFolder, StringComparison.Ordinal) && p != SessionBootstrapFile);
        AssertNone(Offences(sources, AcSessionCreate(), CheckoutFiles.ReadCSharpCode), $"AcSession.Create outside {SessionBootstrapFile}");
    }

    [Theory]
    [InlineData("var b = File.ReadAllBytes(p);", true)]
    [InlineData("using var s = new FileStream(p, FileMode.Open);", true)]
    [InlineData("System.IO.Directory.CreateDirectory(p);", true)]
    [InlineData("Process.Start(\"git\");", true)]
    [InlineData("// File.ReadAllBytes is the host's job", false)]
    [InlineData("var s = \"File.ReadAllBytes\";", false)]
    [InlineData("var f = options.File.Name;", false)]
    [InlineData("var p = Path.Combine(a, b);", false)]
    [InlineData("var x = ProfileFile.Name;", false)]
    public void The_io_rule_sees_code_not_comments_or_strings(string line, bool offends)
    {
        Assert.Equal(offends, FileOrProcessUse().IsMatch(CSharpText.CodeOnly(line)));
    }

    [Theory]
    [InlineData("var t = DateTime.UtcNow;", true)]
    [InlineData("var t = DateTimeOffset.Now;", true)]
    [InlineData("var t = Environment.TickCount64;", true)]
    [InlineData("var s = Stopwatch.StartNew();", true)]
    [InlineData("Thread.Sleep(10);", true)]
    [InlineData("var t = time.GetUtcNow();", false)]
    [InlineData("var t = time.GetTimestamp(); // not Stopwatch.GetTimestamp", false)]
    public void The_clock_rule_sees_wall_clock_reads(string line, bool offends)
    {
        Assert.Equal(offends, ClockUse().IsMatch(CSharpText.CodeOnly(line)));
    }

    [Theory]
    [InlineData("godot", "using Godot;", true)]
    [InlineData("godot", "var v = new Godot.Vector3();", true)]
    [InlineData("godot", "sink = new GodotLogSink(); // Godot.GD.Print", false)]
    [InlineData("sockets", "using System.Net.Sockets;", true)]
    [InlineData("sockets", "var u = new UdpClient(0);", true)]
    [InlineData("sockets", "IDatagramSocket socket = factory(endpoint);", false)]
    [InlineData("networking", "var h = new HttpRequest();", true)]
    [InlineData("networking", "var p = new PacketPeerUdp();", true)]
    [InlineData("dats", "using var c = new DatCollection(options);", true)]
    [InlineData("dats", "var s = ((QualifiedDataId<Surface>)id).Get(dats);", true)]
    [InlineData("dats", "gfxObj.Surfaces.Add((QualifiedDataId<Surface>)s);", false)]
    [InlineData("sessions", "var s = GameSession.Create(options, transport, time, log);", true)]
    [InlineData("sessions", "var s = GameSession.CreateReplay(tape, time, log);", true)]
    [InlineData("sessions", "var s = SessionBootstrap.CreateLive(o, secrets, pids, dats, time, log, null);", true)]
    [InlineData("sessions", "var s = GameSession.Offline(standIn, time, log);", true)]
    [InlineData("sessions", "var s = MyGameSession.Offline(standIn);", false)]
    [InlineData("sessions", "// the stand-in GameSession.Offline is made by SessionHost", false)]
    [InlineData("sessions", "var view = SessionHost.Current;", false)]
    [InlineData("net-token", "using Lifestone.Net.Sockets;", true)]
    [InlineData("net-token", "// reached through Lifestone.Client, never Lifestone.Net", true)]
    [InlineData("net-token", "var p = \"res://Lifestone.Net\";", true)]
    [InlineData("net-token", "using Lifestone.Network;", false)]
    [InlineData("net-token", "var x = MyLifestone.NetValue;", false)]
    [InlineData("ac-session", "var s = AcSession.Create(endpoint, factory, time, log);", true)]
    [InlineData("ac-session", "var s = AcSession . Create(e, f, t, l);", true)]
    [InlineData("ac-session", "var s = AcSession.CreateReplay(tape);", false)]
    [InlineData("ac-session", "var s = MyAcSession.Create(e);", false)]
    [InlineData("ac-session", "// SessionBootstrap wraps AcSession.Create", false)]
    public void The_rules_see_their_tokens(string rule, string line, bool offends)
    {
        var regex = rule switch
        {
            "godot" => GodotUse(),
            "sockets" => SocketUse(),
            "networking" => OtherNetworkingUse(),
            "dats" => new Regex(DatCollectionUse() + "|" + QualifiedDataIdGet()),
            "sessions" => SessionCreation(),
            "net-token" => LifestoneNetToken(),
            "ac-session" => AcSessionCreate(),
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, null),
        };

        // As in the scans: the Lifestone.Net rule reads whole files, comments and strings included; the others read code.
        Assert.Equal(offends, regex.IsMatch(rule == "net-token" ? line : CSharpText.CodeOnly(line)));
    }

    [Fact]
    public void The_metadata_reader_sees_member_uses()
    {
        // This assembly's own IL: the test above calls CSharpText.CodeOnly, and CheckoutFiles calls Process.Start.
        var self = AssemblyMetadata.Read(typeof(ArchitectureTests).Assembly.Location);
        Assert.Contains(self.MemberUses, u => u.Type == "System.Diagnostics.Process" && u.Member == "Start");
        Assert.Contains("System.Diagnostics.ProcessStartInfo", self.TypeReferences);
        Assert.Contains("Lifestone.Client", self.AssemblyReferences);
        Assert.Contains(BuiltAssemblies.Named("Lifestone.Data").MemberUses, u => u.Caller == "Lifestone.Data.Dat.DatSet");
    }

    [Fact]
    public void The_client_project_links_are_resolved_against_the_project_folder()
    {
        Assert.Equal("server/mod/Shared/", Resolve(ClientFolder, "../../server/mod/Shared/"));
        Assert.Equal("src/Lifestone.Client/Linked/", Resolve(ClientFolder, "./Linked/"));
    }

    private static string Blank(string text) => new([.. text.Select(c => c is '\n' or '\r' ? c : ' ')]);
}
