using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Lifestone.Client.Tests;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod.Tests;

/// <summary>
/// Checks on the server mod's sources that need no ACE (m2-plan SM2): the mod builds only against ACE's own assemblies
/// and ships none of them, patches nothing, registers exactly one command the way the wire contract assumes, keeps
/// ACE's hot reload off, writes no files and opens nothing to the network, and keeps the log lines start-server.ps1
/// reads. Building the mod itself is server\build-mod.ps1's check (it needs a set-up ACE).
/// </summary>
public sealed partial class ModSourceTests
{
    private static readonly string ModRoot = RepositoryFiles.PathOf("server", "mod");
    private static readonly string ProjectFolder = Path.Combine(ModRoot, "Lifestone.ServerMod");

    /// <summary>The mod's source files (its project folder and Shared), without build output.</summary>
    private static IReadOnlyList<string> Files { get; } =
    [
        .. Directory.EnumerateFiles(ModRoot, "*", SearchOption.AllDirectories)
            .Where(p => !Path.GetRelativePath(ModRoot, p).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Order(StringComparer.Ordinal),
    ];

    private static IEnumerable<string> CSharp(string folder) =>
        Files.Where(p => p.EndsWith(".cs", StringComparison.Ordinal) && p.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    private static string Relative(string path) => Path.GetRelativePath(RepositoryFiles.Root, path).Replace('\\', '/');

    [GeneratedRegex(@"\[CommandHandler\s*\(")]
    private static partial Regex CommandHandlerAttribute();

    [GeneratedRegex(@"\bConsole\s*\.|\bFile\s*\.\s*(?:Write|Append|Create|Delete|Move|Copy|Replace|Open(?!Read\b))\w*|\bDirectory\s*\.\s*(?:Create|Delete|Move)\w*|\bEnvironment\s*\.\s*SetEnvironmentVariable\b|\bIPAddress\s*\.\s*(?:A[n]y|IPv6A[n]y)\b|\bIPv6A[n]y\b|(?<![\d.])0\.0\.0\.0(?![\d.])")]
    private static partial Regex ForbiddenCall();

    [Fact]
    public void The_scan_sees_the_mod()
    {
        string[] expected =
        [
            "server/mod/.editorconfig",
            "server/mod/Lifestone.ServerMod/Lifestone.ServerMod.csproj",
            "server/mod/Lifestone.ServerMod/Meta.json",
            "server/mod/Lifestone.ServerMod/Mod.cs",
            "server/mod/Lifestone.ServerMod/LifestoneCommand.cs",
            "server/mod/Lifestone.ServerMod/StartupWatch.cs",
            "server/mod/Lifestone.ServerMod/ArrivalGuard.cs",
            "server/mod/Lifestone.ServerMod/CorpseCoin.cs",
            "server/mod/Lifestone.ServerMod/HouseAccountAge.cs",
            "server/mod/Lifestone.ServerMod/BuffBot.cs",
            "server/mod/Lifestone.ServerMod/ToolCall.cs",
            "server/mod/Lifestone.ServerMod/AceFacts.cs",
            "server/mod/Lifestone.ServerMod/Verbs/StatusVerb.cs",
            "server/mod/Lifestone.ServerMod/Verbs/NetVerb.cs",
            "server/mod/Lifestone.ServerMod/Verbs/WhereVerb.cs",
            "server/mod/Lifestone.ServerMod/Verbs/PlaceVerb.cs",
            "server/mod/Lifestone.ServerMod/Verbs/XpVerb.cs",
            "server/mod/Lifestone.ServerMod/Verbs/PlacesVerb.cs",
            "server/mod/Lifestone.ServerMod/Verbs/QuestVerb.cs",
            "server/mod/Lifestone.ServerMod/Verbs/CharVerb.cs",
            "server/mod/Lifestone.ServerMod/Verbs/SpellVerb.cs",
            "server/mod/Lifestone.ServerMod/Verbs/ItemVerb.cs",
            "server/mod/Lifestone.ServerMod/Verbs/CorpsesVerb.cs",
            "server/mod/Lifestone.ServerMod/Core/CorpseRotRules.cs",
            "server/mod/Lifestone.ServerMod/Core/ArrivalKeepRules.cs",
            "server/mod/Lifestone.ServerMod/Core/CorpseCoinRules.cs",
            "server/mod/Lifestone.ServerMod/Core/HouseAccountAgeRules.cs",
            "server/mod/Lifestone.ServerMod/Core/BuffBotRules.cs",
            "server/mod/Lifestone.ServerMod/Core/ItemCreateRules.cs",
            "server/mod/Lifestone.ServerMod/Core/ToolRouting.cs",
            "server/mod/Lifestone.ServerMod/Core/CharPurgeRules.cs",
            "server/mod/Lifestone.ServerMod/Core/OneAnswer.cs",
            "server/mod/Lifestone.ServerMod/Core/PlacesBuilder.cs",
            "server/mod/Lifestone.ServerMod/Core/XpGrant.cs",
            "server/mod/Lifestone.ServerMod/Core/QuestChange.cs",
            "server/mod/Shared/ToolsWire.cs",
            "server/mod/Shared/PlaceRules.cs",
            "server/mod/Shared/BuffBotWire.cs",
        ];
        var found = Files.Select(Relative).ToArray();
        Assert.All(expected, e => Assert.Contains(e, found));
    }

    [Fact]
    public void Nothing_in_the_mod_patches_ACE()
    {
        // The mod uses ACE's public API only: no Harmony patching, and no Harmony assembly in its build or output.
        string[] words = ["HarmonyLib", "HarmonyPatch", "0Harmony"];
        var offences = Files
            .SelectMany(p => words.Where(w => File.ReadAllText(p).Contains(w, StringComparison.OrdinalIgnoreCase)).Select(w => $"{Relative(p)}: {w}"))
            .ToArray();
        Assert.True(offences.Length == 0, "Harmony in the mod:\n" + string.Join('\n', offences));
    }

    [Fact]
    public void Meta_json_is_PascalCase_with_hot_reload_off_and_the_projects_version()
    {
        // ACE reads Meta.json with ConfigManager.SerializerOptions, which match names case-sensitively (ModManager.cs:147,
        // ConfigManager.cs:76-82), and HotReload defaults to true (ModMetadata.cs:26), so every field is spelled out.
        using var meta = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectFolder, "Meta.json")));
        var root = meta.RootElement;
        Assert.Equal(
            ["Author", "Description", "Enabled", "HotReload", "Name", "Priority", "RegisterCommands", "Version"],
            root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Lifestone", root.GetProperty("Name").GetString());
        Assert.False(root.GetProperty("HotReload").GetBoolean());
        Assert.True(root.GetProperty("Enabled").GetBoolean());
        Assert.True(root.GetProperty("RegisterCommands").GetBoolean());
        Assert.Equal(0, root.GetProperty("Priority").GetInt32());
        Assert.Equal(Property(Project(), "Version"), root.GetProperty("Version").GetString());
    }

    [Fact]
    public void The_project_references_ACE_only_and_copies_none_of_it()
    {
        var project = Project();
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Empty(project.Descendants("ProjectReference"));
        Assert.Equal("false", Property(project, "NuGetAudit"));
        Assert.Equal("true", Property(project, "TreatWarningsAsErrors"));
        Assert.Equal("net10.0", Property(project, "TargetFramework"));
        Assert.Equal("AnyCPU", Property(project, "PlatformTarget"));
        Assert.Equal("Lifestone.ServerMod", Property(project, "AssemblyName"));

        var references = project.Descendants("Reference").ToArray();
        Assert.Equal(
            ["ACE.Common", "ACE.DatLoader", "ACE.Database", "ACE.Entity", "ACE.Server"],
            references.Select(r => (string?)r.Attribute("Include") ?? string.Empty).Order(StringComparer.Ordinal));
        foreach (var reference in references)
        {
            var include = (string)reference.Attribute("Include")!;
            var isPrivate = (string?)reference.Attribute("Private") ?? (string?)reference.Element("Private");
            Assert.True(isPrivate == "false", $"{include} must be Private=false: ACE provides it, and a copy beside the mod breaks type identity");
            var hint = (string?)reference.Attribute("HintPath") ?? (string?)reference.Element("HintPath") ?? string.Empty;
            Assert.Equal("$(_LsAceDir)" + include + ".dll", hint);
        }

        Assert.DoesNotContain(references, r => Regex.IsMatch((string?)r.Attribute("Include") ?? string.Empty, "Harmony|McMaster|log4net", RegexOptions.IgnoreCase));

        // The scripts move the intermediates out of the project folder, so its own bin\ and obj\ stay out of the compile.
        Assert.Equal(@"$(DefaultItemExcludes);bin\**;obj\**", Property(project, "DefaultItemExcludes"));
    }

    [Fact]
    public void The_project_compiles_the_shared_contract_and_links_the_loopback_gate_from_Lifestone_Net()
    {
        var includes = Project().Descendants("Compile").Select(c => (string?)c.Attribute("Include") ?? string.Empty).ToArray();
        Assert.Equal(
            [
                @"..\Shared\**\*.cs",
                @"..\..\..\src\Lifestone.Net\Sockets\LoopbackPolicy.cs",
                @"..\..\..\src\Lifestone.Net\Sockets\LoopbackRequiredException.cs",
            ],
            includes);
        Assert.True(File.Exists(RepositoryFiles.PathOf("src", "Lifestone.Net", "Sockets", "LoopbackPolicy.cs")));
        Assert.True(File.Exists(RepositoryFiles.PathOf("src", "Lifestone.Net", "Sockets", "LoopbackRequiredException.cs")));

        // One gate (m2-plan §1.4): no copy of it in the mod's own sources.
        var copies = Files.Where(p => p.EndsWith(".cs", StringComparison.Ordinal) && Regex.IsMatch(File.ReadAllText(p), @"\bclass\s+LoopbackPolicy\b")).ToArray();
        Assert.Empty(copies);
    }

    [Fact]
    public void The_shared_contract_uses_the_base_class_library_only()
    {
        var shared = Path.Combine(ModRoot, "Shared");
        var usings = CSharp(shared)
            .SelectMany(p => File.ReadLines(p).Where(l => l.StartsWith("using ", StringComparison.Ordinal)).Select(l => (File: Relative(p), Line: l)))
            .Where(u => !u.Line.StartsWith("using System", StringComparison.Ordinal))
            .Select(u => $"{u.File}: {u.Line}")
            .ToArray();
        Assert.True(usings.Length == 0, "non-BCL usings in server/mod/Shared (it is compiled into Lifestone.Client and the mod):\n" + string.Join('\n', usings));
        Assert.All(CSharp(shared), p => Assert.Contains("namespace Lifestone.ServerTools;", File.ReadAllText(p), StringComparison.Ordinal));
    }

    [Fact]
    public void Exactly_one_command_is_registered_the_way_the_wire_assumes()
    {
        var handlers = CSharp(ModRoot)
            .SelectMany(p => CommandHandlerAttribute().Matches(File.ReadAllText(p)).Select(m => (File: p, Text: File.ReadAllText(p)[m.Index..])))
            .ToArray();
        var handler = Assert.Single(handlers);
        Assert.Equal("server/mod/Lifestone.ServerMod/LifestoneCommand.cs", Relative(handler.File));

        // Player level, no flags (RequiresWorld and refused access get no reply at all, GameActionTalk.cs:83-95), the raw
        // text (ACE's parser deletes quotes) and no parameter count.
        Assert.StartsWith("[CommandHandler(\"lifestone\", AccessLevel.Player, CommandHandlerFlag.None, true, -1,", handler.Text, StringComparison.Ordinal);
        Assert.Equal("lifestone", ToolsWire.Command);
        Assert.Contains("public static void Handle(Session? session, params string[] parameters)", handler.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Replies_are_sent_on_the_session_with_the_contracts_chat_type()
    {
        // One F7E0 to the requesting session, not Player.SendMessage, whose squelch filter can drop it (Player.cs:1079-1085).
        var text = File.ReadAllText(Path.Combine(ProjectFolder, "Reply.cs"));
        Assert.Contains("session.Network.EnqueueSend(new GameMessageSystemChat(text, (ChatMessageType)ToolsWire.ReplyChatType));", text, StringComparison.Ordinal);
        Assert.DoesNotContain(CSharp(ModRoot), p => File.ReadAllText(p).Contains(".SendMessage(", StringComparison.Ordinal));
    }

    [Fact]
    public void The_mod_type_is_where_ACE_looks_for_it()
    {
        // ACE instantiates <assembly name>.Mod (ModContainer.cs:40), so the class is Lifestone.ServerMod.Mod.
        var text = File.ReadAllText(Path.Combine(ProjectFolder, "Mod.cs"));
        Assert.Contains("namespace Lifestone.ServerMod;", text, StringComparison.Ordinal);
        Assert.Contains("public sealed class Mod : IHarmonyMod", text, StringComparison.Ordinal);
        Assert.Equal("Lifestone.ServerMod", Property(Project(), "RootNamespace"));
    }

    [Fact]
    public void The_mod_writes_no_files_and_opens_nothing_to_the_network()
    {
        // D138 (the owner, 2026-11-14: the bot's credits survive a restart): one named writer, the buff bot's state file, written to a side file and moved over the old one, in ACE's runtime folder (the parent of
        // the mods folder, BuffBot.Start); its two calls are the whole exception.
        const string Ledger = "server/mod/Lifestone.ServerMod/Core/BuffBotLedger.cs";
        string[] stateFile = ["File.WriteAllText(side, ", "File.Move(side, path, "];
        var calls = Files
            .Where(p => !p.EndsWith(".editorconfig", StringComparison.Ordinal))
            .SelectMany(p => File.ReadAllLines(p).Select((line, i) => (p, i, line)))
            .SelectMany(x => ForbiddenCall().Matches(x.line).Select(m => (
                Where: $"{Relative(x.p)}:{x.i + 1}: {m.Value}",
                Allowed: Relative(x.p) == Ledger && stateFile.Any(s => x.line.TrimStart().StartsWith(s, StringComparison.Ordinal)))))
            .ToArray();
        var offences = calls.Where(c => !c.Allowed).Select(c => c.Where).ToArray();
        Assert.True(offences.Length == 0, "forbidden calls in the server mod:\n" + string.Join('\n', offences));
        Assert.Equal(stateFile.Length, calls.Count(c => c.Allowed));
    }

    [Theory]
    [InlineData("Console.WriteLine(x);", true)]
    [InlineData("File.WriteAllText(p, t);", true)]
    [InlineData("File.AppendAllText(p, t);", true)]
    [InlineData("File.Delete(p);", true)]
    [InlineData("Directory.CreateDirectory(p);", true)]
    [InlineData("Environment.SetEnvironmentVariable(\"A\", \"1\");", true)]
    [InlineData("using var s = File.OpenRead(p);", false)]
    [InlineData("using var s = new FileStream(p, FileMode.Open, FileAccess.Read);", false)]
    [InlineData("ModManager.Log(ModLog.ConsoleReply + text);", false)]
    [InlineData("var v = new Version(10, 0, 0, 0);", false)]
    public void The_forbidden_call_rule_sees_its_tokens(string line, bool offends)
    {
        Assert.Equal(offends, ForbiddenCall().IsMatch(line));
    }

    [Fact]
    public void The_forbidden_call_rule_sees_wildcard_addresses()
    {
        // Built at run time: LoopbackOnlyTests forbids spelling a wildcard address anywhere under tests/.
        var any = "IPAddress." + "Any";
        var v6Any = "IPv6" + "Any";
        var dotted = string.Join('.', 0, 0, 0, 0);
        Assert.Matches(ForbiddenCall(), "socket.Bind(new IPEndPoint(" + any + ", 9000));");
        Assert.Matches(ForbiddenCall(), "var wildcard = IPAddress." + v6Any + ";");
        Assert.Matches(ForbiddenCall(), "var host = \"" + dotted + "\";");
        Assert.DoesNotMatch(ForbiddenCall(), "var host = \"10." + dotted + "\";");
    }

    [Fact]
    public void The_log_lines_start_server_reads_are_unchanged()
    {
        // m2-plan §4.12: server\start-server.ps1 (SM6) and the smoke steps match these; change them only with their readers.
        var text = File.ReadAllText(Path.Combine(ProjectFolder, "ModInfo.cs"));
        string[] lines =
        [
            "\"Lifestone mod loaded: \"",
            "\"Lifestone mod refused to load: \"",
            "\"Lifestone: ACE's first-account Admin auto-promotion is off; new accounts get DefaultAccessLevel 0 (Player).\"",
            "\"Lifestone mod ready: \"",
            "\"Lifestone tool: \"",
            "\"Lifestone console: \"",
        ];
        Assert.All(lines, l => Assert.Contains(l, text, StringComparison.Ordinal));
    }

    [Fact]
    public void Culture_sensitive_calls_are_build_errors_under_server_mod()
    {
        var text = File.ReadAllText(Path.Combine(ModRoot, ".editorconfig"));
        Assert.All(
            ["CA1304", "CA1305", "CA1310", "CA1311"],
            rule => Assert.Contains($"dotnet_diagnostic.{rule}.severity = error", text, StringComparison.Ordinal));
        Assert.Contains("[*.cs]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_command_hands_every_request_to_ToolRouting()
    {
        // SM3: the guards and the dispatch are ToolRouting's (ToolRoutingTests run them); the command only wires ACE's
        // session and player in, and its IToolVerbs<Player> implementation must compile with every verb in it.
        var command = File.ReadAllText(Path.Combine(ProjectFolder, "LifestoneCommand.cs"));
        Assert.Single(Regex.Matches(command, @"\bToolRouting\.Route\("));
        Assert.Matches(@"class\s+\w+\([^)]*\)\s*:\s*IToolVerbs<Player>", command);
        Assert.DoesNotContain("TODO(servermod)", command, StringComparison.Ordinal);
        Assert.Contains("ToolRouting.Implemented", File.ReadAllText(Path.Combine(ProjectFolder, "Verbs", "StatusVerb.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_verbs_that_follow_ACE_s_admin_commands_carry_port_headers()
    {
        // The call sequences of @myloc, @telepoi, @teleloc, @grantxp and @qst, re-expressed (docs/third-party/server-mod.md).
        (string File, string Source)[] ports =
        [
            ("WhereVerb.cs", "DeveloperCommands.cs HandleMyLoc"),
            ("PlaceVerb.cs", "AdminCommands.cs HandleTeleportPoi,"),
            ("XpVerb.cs", "DeveloperCommands.cs HandleGrantXp"),
            ("QuestVerb.cs", "AdminCommands.cs Handleqst"),
            ("CharVerb.cs", "CharacterCommands.cs HandleCharacterForcedDelete"),
            ("SpellVerb.cs", "AdminCommands.cs HandleAddSpell"),
            ("ItemVerb.cs", "AdminCommands.cs HandleCI"),
        ];
        foreach (var (file, source) in ports)
        {
            var first = File.ReadLines(Path.Combine(ProjectFolder, "Verbs", file)).First();
            Assert.StartsWith("// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Command/Handlers/" + source, first, StringComparison.Ordinal);
        }

        var doc = File.ReadAllText(RepositoryFiles.PathOf("docs", "third-party", "server-mod.md"));
        Assert.All(ports, p => Assert.Contains(p.File, doc, StringComparison.Ordinal));
    }

    [Fact]
    public void Place_loc_gives_ACE_the_rotation_in_its_own_order()
    {
        // An ACE call shape no unit test reaches: ToolLocation is w, x, y, z (the wire order), but ACE's Position
        // constructor takes x, y, z, w (Position.cs:220).
        var text = File.ReadAllText(Path.Combine(ProjectFolder, "Verbs", "PlaceVerb.cs"));
        Assert.Single(Regex.Matches(text, @"new Position\((?<at>\w+)\.Cell, \k<at>\.X, \k<at>\.Y, \k<at>\.Z, \k<at>\.Qx, \k<at>\.Qy, \k<at>\.Qz, \k<at>\.Qw\)"));
        Assert.Single(Regex.Matches(text, @"new Position\(\w+\.Cell,"));
    }

    [Fact]
    public void Xp_grants_as_ACE_s_grantxp_between_its_two_queued_actions()
    {
        // ACE call shapes no unit test reaches (XpGrantTests run the count itself): GrantXP with @grantxp's XpType.Admin
        // and no fellowship or allegiance share, and queued between the read of the experience and the reply, because
        // GrantXP only queues ACE's update on the player's action queue (Player_Xp.cs:72; ActionQueue.cs RunActions).
        var text = File.ReadAllText(Path.Combine(ProjectFolder, "Verbs", "XpVerb.cs"));
        var grant = Assert.Single(Regex.Matches(text, @"\.GrantXP\(\s*\w+\.Amount,\s*XpType\.Admin,\s*ShareType\.None\s*\)"));
        Assert.Single(Regex.Matches(text, @"\.GrantXP\("));
        var queued = Regex.Matches(text, @"\.EnqueueAction\(").Select(m => m.Index).ToArray();
        Assert.Equal(2, queued.Length);
        Assert.True(queued[0] < grant.Index && grant.Index < queued[1], "xp must queue its read of the experience, then grant, then queue its reply");
    }

    [Fact]
    public void Quest_acts_on_the_callers_own_registry_through_ACE_s_QuestManager_and_writes_no_database()
    {
        // ACE call shapes no unit test reaches (QuestChangeTests run the decisions): the verb takes the requesting Player and
        // nobody else, stamps with QuestManager.Stamp (a quest script's call, which respects the quest's maximum solves) and
        // never with the admin command's forced count, erases one quest and never all of them, and reaches no creature,
        // fellowship or target. Saving the character is ACE's own (Update and Erase mark it changed): the mod's verbs and
        // decisions never touch the shard database.
        var text = string.Join(Environment.NewLine, File.ReadLines(Path.Combine(ProjectFolder, "Verbs", "QuestVerb.cs")).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        Assert.Contains("public static void Run(ToolCall call, Player player, QuestRequest request)", text, StringComparison.Ordinal);
        Assert.Contains("var quests = player.QuestManager;", text, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(text, @"\bquests\.Stamp\(request\.Name\)"));
        Assert.Single(Regex.Matches(text, @"\bquests\.Erase\(request\.Name\)"));
        Assert.Single(Regex.Matches(text, @"\.QuestManager\b"));
        string[] forbidden = ["EraseAll", "SetQuestCompletions", ".Update(", "Fellowship", "CurrentAppraisal", "LastAppraised", "GetTarget", "SetQuestBits", "Decrement", "Increment"];
        Assert.All(forbidden, word => Assert.DoesNotContain(word, text, StringComparison.Ordinal));

        var writers = CSharp(Path.Combine(ProjectFolder, "Verbs")).Concat(CSharp(Path.Combine(ProjectFolder, "Core")))
            .Where(p => !p.EndsWith("CharVerb.cs", StringComparison.Ordinal))
            .Where(p => Regex.IsMatch(File.ReadAllText(p), @"DatabaseManager\s*\.\s*Shard|SaveCharacter|\.ExecuteSql|\bSaveBiota"))
            .Select(Relative)
            .ToArray();
        Assert.True(writers.Length == 0, "only char purge (CharVerb.cs) may touch the shard database:\n" + string.Join('\n', writers));
    }

    [Fact]
    public void Char_purge_is_the_only_verb_that_touches_the_shard_database_and_purges_only_after_the_rules()
    {
        // ACE call shapes no unit test reaches (CharPurgeRulesTests run the rules): the order lookup (the serialized shard queue),
        // ACE's cache bookkeeping, then the purge; the rules are applied to the account's own characters (GetCharacters by the
        // caller's account id) and once more to the fresh row right before PurgeCharacter; nothing else writes the database.
        var code = string.Join(Environment.NewLine, File.ReadLines(Path.Combine(ProjectFolder, "Verbs", "CharVerb.cs")).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        Assert.Contains("DatabaseManager.Shard.GetCharacters(caller.AccountId, true,", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"PlayerManager\.ProcessDeletedPlayer\("));
        Assert.Single(Regex.Matches(code, @"ShardDatabaseOfflineTools\.PurgeCharacter\("));
        Assert.Single(Regex.Matches(code, @"WorldManager\.ActionQueue\.EnqueueAction\("));
        string[] forbidden = ["SaveCharacter", "SaveBiota", "RemoveBiota", "ExecuteSql", "PurgeCharactersInParallel", "PurgePlayer", "HandlePlayerDelete", "DeleteTime =", "IsDeleted =", "Restore"];
        Assert.All(forbidden, word => Assert.DoesNotContain(word, code, StringComparison.Ordinal));

        var lookup = code.IndexOf("GetCharacters(", StringComparison.Ordinal);
        var caches = code.IndexOf("ProcessDeletedPlayer(", StringComparison.Ordinal);
        var purge = code.IndexOf("PurgeCharacter(", StringComparison.Ordinal);
        Assert.True(0 <= lookup && lookup < caches && caches < purge, "char purge must read the queue, then update ACE's caches, then purge");
        var rules = Regex.Matches(code, @"CharPurgeRules\.Check\(").Select(m => m.Index).ToArray();
        Assert.Equal(2, rules.Length);
        Assert.True(rules[0] < caches && caches < rules[1] && rules[1] < purge, "the rules run on the account's list, and again on the fresh row just before the purge");
        Assert.Contains("CharPurgeRules.EarlyRefusal(", code, StringComparison.Ordinal);
        Assert.Contains("caller.Session.Characters.RemoveAll(c => c.Id == target.Guid);", code, StringComparison.Ordinal);

        // The graduated test characters are spelled once, in the rules.
        Assert.Contains("[\"Lifestone Smoke\", \"Lifestone Loopback\", \"BSD Mage\", \"Holtburg Hunter\", \"Holtburg Brawler\", \"Holtburg Archer\", \"Lifestone Tools\", \"Winnie\", \"Qwinnie\"]", File.ReadAllText(Path.Combine(ProjectFolder, "Core", "CharPurgeRules.cs")), StringComparison.Ordinal);
        var spelled = CSharp(ModRoot)
            .Where(p => !p.EndsWith("CharPurgeRules.cs", StringComparison.Ordinal))
            .Where(p => File.ReadAllLines(p).Any(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && l.Contains("Lifestone Smoke", StringComparison.Ordinal)))
            .Select(Relative)
            .ToArray();
        Assert.True(spelled.Length == 0, "the graduated names belong in CharPurgeRules only:\n" + string.Join('\n', spelled));
    }

    [Fact]
    public void Corpses_rot_makes_the_one_write_ACE_s_corpse_limit_makes_on_the_landblocks_queue_and_destroys_and_saves_nothing()
    {
        // ACE call shapes no unit test reaches (CorpseRotRulesTests run the selection): the search runs as one action on the caller's current
        // landblock's own queue (Landblock.EnqueueAction, as ACE's spam-limit block runs on it), reads that landblock's objects there, takes
        // the corpses the rules pick from Corpse objects only, and writes only TimeToRot, to Corpse.EmptyDecayTime, once per corpse. It never destroys
        // anything (ACE's decay does), never reaches the database, and never moves, opens or empties a corpse.
        var path = Path.Combine(ProjectFolder, "Verbs", "CorpsesVerb.cs");
        var code = string.Join(Environment.NewLine, File.ReadLines(path).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        Assert.Contains("public static void Rot(ToolCall call, Player player, CorpsesRotRequest request)", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"player\.CurrentLandblock\b"));
        Assert.Single(Regex.Matches(code, @"landblock\.EnqueueAction\(new ActionEventDelegate\("));
        Assert.Single(Regex.Matches(code, @"\.GetWorldObjectsForPhysicsHandling\(\)\.OfType<Corpse>\(\)\.ToList\(\)"));
        Assert.Contains("c.Level.HasValue, c.IsMonster, c.TimeToRot", code, StringComparison.Ordinal);
        Assert.Contains("CorpseRotRules.Plan(", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"\.TimeToRot\s*=[^=]"));
        Assert.Single(Regex.Matches(code, @"corpse\.TimeToRot = Corpse\.EmptyDecayTime;"));
        Assert.Equal(3, Regex.Matches(code, @"\bCorpse\.EmptyDecayTime\b").Count);
        string[] forbidden = ["Destroy", "Decay(", "RemoveWorldObject", "DatabaseManager", "SaveBiota", "SaveCharacter", "ExecuteSql", "TryRemoveFromInventory", "Open(", "Close(", "Location =", ".Inventory", "Teleport"];
        Assert.All(forbidden, word => Assert.DoesNotContain(word, code, StringComparison.Ordinal));

        // The header names the ACE source and the port is listed.
        Assert.StartsWith("// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Entity/Landblock.cs AddWorldObjectInternal", File.ReadLines(path).First(), StringComparison.Ordinal);
        Assert.Contains("CorpsesVerb.cs", File.ReadAllText(RepositoryFiles.PathOf("docs", "third-party", "server-mod.md")), StringComparison.Ordinal);

        // Nothing but char purge writes the shard database, and the new verb is not char purge (the quest test scans every verb for it too).
        Assert.DoesNotMatch(@"DatabaseManager\s*\.\s*Shard|SaveCharacter|\.ExecuteSql|\bSaveBiota", File.ReadAllText(path));
    }

    [Fact]
    public void Spell_learn_reads_the_table_and_the_spellbook_around_ACE_s_one_call_and_checks_it_took()
    {
        // ACE call shapes no unit test reaches (SpellOutcomes.Classify is tested): LearnSpellWithNetworking returns nothing, so the verb
        // classifies from the spell table and SpellIsKnown first, calls it once per learned spell on the requesting player only, and reads the
        // spellbook again so a spell ACE did not add is a loud failure.
        var code = string.Join(Environment.NewLine, File.ReadLines(Path.Combine(ProjectFolder, "Verbs", "SpellVerb.cs")).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        Assert.Contains("public static void Learn(ToolCall call, Player player, SpellLearnRequest request)", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"\bplayer\.LearnSpellWithNetworking\(spellId, false\)"));
        Assert.Single(Regex.Matches(code, @"\.LearnSpellWithNetworking\("));
        Assert.Contains("DatManager.PortalDat.SpellTable.Spells", code, StringComparison.Ordinal);
        Assert.Contains("SpellOutcomes.Classify(", code, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(code, @"player\.SpellIsKnown\(spellId\)").Count);
        Assert.True(code.IndexOf("SpellIsKnown(", StringComparison.Ordinal) < code.IndexOf("LearnSpellWithNetworking(", StringComparison.Ordinal));
        string[] forbidden = ["RemoveKnownSpell", "AddKnownSpell", "LearnSpellsInBulk", "Fellowship", "CurrentAppraisal", "GetTarget"];
        Assert.All(forbidden, word => Assert.DoesNotContain(word, code, StringComparison.Ordinal));
    }

    [Fact]
    public void Item_follows_the_HandleCI_path_into_the_callers_own_pack_with_no_audit_broadcast()
    {
        // ACE call shapes no unit test reaches (ItemCreateRulesTests run the decisions): the cached weenie, ACE's own type check,
        // the factory, SetStackSize only for an item with a MaxStackSize, then the callers own TryCreateInInventoryWithNetworking; a pack
        // with no room frees the object; nothing is placed in the world and nothing is broadcast to the audit channel.
        var code = string.Join(Environment.NewLine, File.ReadLines(Path.Combine(ProjectFolder, "Verbs", "ItemVerb.cs")).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        Assert.Contains("public static void Run(ToolCall call, Player player, ItemRequest request)", code, StringComparison.Ordinal);
        var steps = new[]
        {
            "DatabaseManager.World.GetCachedWeenie(request.Wcid)",
            "AdminCommands.VerifyCreateWeenieType(",
            "WorldObjectFactory.CreateNewWorldObject(weenie!)",
            "item.SetStackSize(",
            "item.PaletteTemplate = palette", // @ci's order (AdminCommands.cs:2559-2563): stack, palette, shade, then the pack
            "item.Shade = shade",
            "player.TryCreateInInventoryWithNetworking(item)",
            "item.Destroy()",
        }.Select(s => code.IndexOf(s, StringComparison.Ordinal)).ToArray();
        Assert.All(steps, i => Assert.True(i >= 0));
        Assert.Equal(steps.OrderBy(i => i), steps);
        Assert.Single(Regex.Matches(code, @"\.SetStackSize\("));
        Assert.Single(Regex.Matches(code, @"\.TryCreateInInventoryWithNetworking\("));
        Assert.Contains("item.MaxStackSize is { } max", code, StringComparison.Ordinal);
        string[] forbidden = ["BroadcastToAuditChannel", "EnterWorld", "Location", "InFrontOf", "LastSpawnPos", "SaveBiota", "DatabaseManager.Shard", "CreateObjectForCommand"];
        Assert.All(forbidden, word => Assert.DoesNotContain(word, code, StringComparison.Ordinal));
    }

    [Fact]
    public void Places_runs_off_the_world_thread_and_names_are_matched_ordinally()
    {
        Assert.Contains("Task.Run(", File.ReadAllText(Path.Combine(ProjectFolder, "Verbs", "PlacesVerb.cs")), StringComparison.Ordinal);
        var facts = File.ReadAllText(Path.Combine(ProjectFolder, "AceFacts.cs"));
        Assert.Contains("string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)", facts, StringComparison.Ordinal);

        // ACE's own lookup lower-cases in the thread's culture and caches misses as nulls (WorldDatabaseWithEntityCache.cs:687-702).
        Assert.DoesNotContain(CSharp(ModRoot), p => File.ReadAllText(p).Contains("GetCachedPointOfInterest", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_reply_goes_through_one_ToolCall()
    {
        // Exactly one reply and one 'Lifestone tool:' line per request: only ToolCall sends and logs them, through its
        // OneAnswer gate (OneAnswerTests run the gate).
        var senders = CSharp(ModRoot).Where(p => File.ReadAllText(p).Contains("Reply.Send(", StringComparison.Ordinal)).Select(Relative).ToArray();
        Assert.Equal(["server/mod/Lifestone.ServerMod/ToolCall.cs"], senders);
        var loggers = CSharp(ModRoot).Where(p => File.ReadAllText(p).Contains("ModLog.Tool +", StringComparison.Ordinal)).Select(Relative).ToArray();
        Assert.Equal(["server/mod/Lifestone.ServerMod/ToolCall.cs"], loggers);
    }

    private static XDocument Project() => XDocument.Load(Path.Combine(ProjectFolder, "Lifestone.ServerMod.csproj"));

    private static string? Property(XDocument project, string name) =>
        project.Descendants("PropertyGroup").Elements(name).Select(e => e.Value).FirstOrDefault();
}
