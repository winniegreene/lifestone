using System.Text.RegularExpressions;
using Lifestone.Client.Options;

namespace Lifestone.Client.Tests.Options;

/// <summary>
/// The option registry against the parsers that read the options, scanned as source: Lifestone.Game's (which tests
/// cannot reference because it needs the engine), the option parsers of Lifestone.Client (its *Options.cs and
/// *Arguments.cs files outside Options/) and Lifestone.Headless. Every option a parser reads is registered with help,
/// and every registered option is read somewhere. This is what keeps "unknown argument" meaning truly unknown after
/// features add options.
/// </summary>
public sealed partial class OptionCatalogTests
{
    /// <summary>Option literals on <c>case</c> lines (<c>case "--x":</c>, <c>case "--x" or "--y":</c>).</summary>
    [GeneratedRegex("\"(--[a-z0-9][a-z0-9-]*)\"")]
    private static partial Regex OptionLiteral();

    /// <summary>A literal that is exactly an option, with an optional fixed value (<c>"--scene=spellviewer"</c>).</summary>
    [GeneratedRegex("\"(--[a-z0-9][a-z0-9-]*)(=[a-z0-9-]*)?\"")]
    private static partial Regex WholeOptionLiteral();

    /// <summary>
    /// The source files that may parse options: every script of Lifestone.Game, Lifestone.Client's parser files
    /// (*Options.cs, *Arguments.cs) outside src/Lifestone.Client/Options/ - which holds every option literal of the
    /// registry, so scanning it would make <see cref="Every_registered_option_is_read_by_some_parser"/> pass by itself - and
    /// every file of Lifestone.Headless. Build output (bin/, obj/) is skipped.
    /// </summary>
    private static IEnumerable<(string File, string Text)> ParserSources()
    {
        var game = SourceFiles("src", "Lifestone.Game", "scripts");
        var client = SourceFiles("src", "Lifestone.Client")
            .Where(f => !f.StartsWith("src/Lifestone.Client/Options/", StringComparison.Ordinal))
            .Where(IsParserFile);
        var headless = SourceFiles("src", "Lifestone.Headless");
        return game.Concat(client).Concat(headless).Select(f => (f, File.ReadAllText(RepositoryFiles.PathOf(f))));
    }

    /// <summary>The .cs files under a source folder, as repository-relative paths with forward slashes, without bin/ and obj/.</summary>
    private static IEnumerable<string> SourceFiles(params string[] folder) =>
        Directory.EnumerateFiles(RepositoryFiles.PathOf(folder), "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(RepositoryFiles.Root, f).Replace('\\', '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    private static bool IsParserFile(string file) =>
        file.EndsWith("Options.cs", StringComparison.Ordinal) || file.EndsWith("Arguments.cs", StringComparison.Ordinal);

    /// <summary>Option names the parsers read: <c>case</c> labels anywhere, and option literals in *Options.cs / *Arguments.cs.</summary>
    private static Dictionary<string, string> ParsedOptions()
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (file, text) in ParserSources())
        {
            var parserFile = IsParserFile(file);
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("case \"", StringComparison.Ordinal))
                {
                    foreach (Match m in OptionLiteral().Matches(trimmed))
                    {
                        parsed.TryAdd(m.Groups[1].Value, file);
                    }
                }
                else if (parserFile && !trimmed.StartsWith("///", StringComparison.Ordinal) && !trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    foreach (Match m in WholeOptionLiteral().Matches(trimmed))
                    {
                        parsed.TryAdd(m.Groups[1].Value, file);
                    }
                }
            }
        }

        return parsed;
    }

    [Fact]
    public void The_catalog_builds_and_every_option_has_help()
    {
        var registry = OptionCatalog.Registry;
        Assert.NotEmpty(registry.Groups);
        foreach (var group in registry.Groups)
        {
            Assert.False(string.IsNullOrWhiteSpace(group.Scope), group.Feature);
            Assert.All(group.Options, o => Assert.False(string.IsNullOrWhiteSpace(o.Help), o.Name));
        }

        Assert.True(registry.Find("--help")?.Role == OptionRole.Help);
        Assert.Equal(
            ["--blend-checks", "--engine-checks", "--interior-engine-checks", "--login-check", "--offline-player-checks", "--play-checks", "--portal-space-checks", "--retail-ui-checks", "--sky-checks"],
            registry.Names.Where(n => registry.Find(n)!.Role == OptionRole.CheckSuite).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_scan_reads_the_parsers_of_every_project_that_parses_options()
    {
        var files = ParserSources().Select(s => s.File).ToArray();
        Assert.Contains("src/Lifestone.Game/scripts/Core/GameOptions.cs", files);
        Assert.Contains("src/Lifestone.Headless/Program.cs", files);
        Assert.DoesNotContain(files, f => f.StartsWith("src/Lifestone.Client/Options/", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.Contains("/obj/", StringComparison.Ordinal) || f.Contains("/bin/", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_option_a_parser_reads_is_registered()
    {
        var registry = OptionCatalog.Registry;
        var parsed = ParsedOptions();
        Assert.True(parsed.Count > 60, $"the source scan found only {parsed.Count} options; did the parsers move?");
        var missing = parsed.Where(p => !registry.IsKnown(p.Key)).Select(p => $"{p.Key} (read in {p.Value})").Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            missing.Length == 0,
            "These options are parsed but not registered, so the game reports them as unknown and --help does not list them. " +
            "Add an OptionSpec with help to the feature's group in src/Lifestone.Client/Options/OptionCatalog*.cs:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void Every_registered_option_is_read_by_some_parser()
    {
        var sources = string.Join("\n", ParserSources().Select(s => s.Text));
        var unread = OptionCatalog.Registry.Names
            .Where(n => !sources.Contains($"\"{n}\"", StringComparison.Ordinal) && !sources.Contains($"\"{n}=", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(unread.Length == 0, "Registered in OptionCatalog*.cs but read by no parser in Lifestone.Game, Lifestone.Client or Lifestone.Headless (remove or rename them):\n  " + string.Join("\n  ", unread));
    }

    [Fact]
    public void Automated_runs_come_from_the_registry_for_the_run_s_scene()
    {
        var registry = OptionCatalog.Registry;
        foreach (var (endsRun, scene) in new[]
                 {
                     ("--screenshot=x.png", OptionScenes.Main), ("--fly-path=holtburg:arwic:200", OptionScenes.Main),
                     ("--golden-viewpoints", OptionScenes.Main), ("--interior-tour", OptionScenes.Main), ("--sky-checks", OptionScenes.Main),
                     ("--teleport-tour", OptionScenes.Main), ("--teleport-tour=2", OptionScenes.Main),
                     ("--interior-engine-checks", OptionScenes.Main), ("--engine-checks", OptionScenes.Main), ("--blend-checks", OptionScenes.Main), ("--portal-space-checks", OptionScenes.Main),
                     ("--help", OptionScenes.Main),
                     ("--showcase", OptionScenes.SpellViewer), ("--screenshot=x.png", OptionScenes.SpellViewer),
                     ("--golden", OptionScenes.TerrainPreview), ("--screenshot=x.png", OptionScenes.TerrainPreview),
                 })
        {
            Assert.True(registry.EndsRun(CommandLineArgument.ParseAll([endsRun]), scene), $"{endsRun} in {scene}");
        }

        foreach (var interactive in new[] { "--start=arwic", "--walk", "--clean-hud", "--ui=open", "--interior-show-all", "--quit-after=60", "--scene=spellviewer" })
        {
            Assert.False(registry.EndsRun(CommandLineArgument.ParseAll([interactive]), OptionScenes.Main), interactive);
        }

        // Another scene's harness given to the main scene is reported, and the run stays interactive (review finding:
        // --showcase or the terrain preview's --golden used to put the main scene in automated mode with nothing to end it).
        foreach (var foreign in new[] { "--showcase", "--golden", "--golden=out" })
        {
            var args = CommandLineArgument.ParseAll([foreign]);
            Assert.False(registry.EndsRun(args, OptionScenes.Main), foreign);
            Assert.Contains("not read in this run's main scene", Assert.Single(registry.Problems(args, OptionScenes.Main)), StringComparison.Ordinal);
        }

        Assert.False(registry.EndsRun(CommandLineArgument.ParseAll(["--fly-path=holtburg:arwic:200"]), OptionScenes.SpellViewer));
    }

    [Fact]
    public void Options_two_scenes_read_are_read_in_both()
    {
        var registry = OptionCatalog.Registry;
        Assert.Empty(registry.Problems(CommandLineArgument.ParseAll(["--seamless-normals", "--retail-normals", "--day-group=2", "--golden", "--dat-dir=x", "--max-mip=2", "--fov=70"]), OptionScenes.TerrainPreview));
        Assert.Empty(registry.Problems(CommandLineArgument.ParseAll(["--seamless-normals", "--retail-normals", "--day-group=2", "--scene=spellviewer", "--blend-space=linear", "--detail=off", "--fov=75"]), OptionScenes.Main));
        Assert.Empty(registry.Problems(CommandLineArgument.ParseAll(["--scene=spellviewer", "--showcase", "--time-of-day=0.3", "--retail-normals", "--blend-space=linear", "--detail=off", "--fov=50"]), OptionScenes.SpellViewer));
        Assert.Single(registry.Problems(CommandLineArgument.ParseAll(["--blend-space=linear"]), OptionScenes.TerrainPreview));
        Assert.Equal(OptionScenes.All, registry.ScenesOf("--screenshot"));
    }

    [Theory]
    [InlineData(new string[0], new string[0], OptionScenes.Main)]
    [InlineData(new[] { "--path", "src/Lifestone.Game" }, new[] { "--start=arwic" }, OptionScenes.Main)]
    [InlineData(new string[0], new[] { "--scene=spellviewer" }, OptionScenes.SpellViewer)]
    [InlineData(new string[0], new[] { "--scene=other" }, OptionScenes.Main)]
    [InlineData(new[] { "res://scenes/TerrainPreview.tscn" }, new[] { "--golden" }, OptionScenes.TerrainPreview)]
    [InlineData(new[] { "res://scenes/TerrainPreview.tscn" }, new[] { "--scene=spellviewer" }, OptionScenes.TerrainPreview)]
    [InlineData(new[] { "--scene", "res://scenes/SpellViewer.tscn" }, new string[0], OptionScenes.SpellViewer)]
    [InlineData(new[] { "C:\\Lifestone\\scenes\\main.TSCN" }, new[] { "--scene=spellviewer" }, OptionScenes.SpellViewer)]
    [InlineData(new[] { "res://scenes/Other.tscn" }, new[] { "--golden" }, OptionScenes.All)]
    [InlineData(new[] { "uid://b4x2" }, new string[0], OptionScenes.All)]
    public void The_run_s_scene_comes_from_the_scene_file_and_the_scene_switch(string[] engineArgs, string[] userArgs, OptionScenes expected) =>
        Assert.Equal(expected, OptionCatalog.SceneOf(engineArgs, CommandLineArgument.ParseAll(userArgs)));

    [Fact]
    public void Help_text_lists_every_registered_option()
    {
        var help = OptionCatalog.HelpText();
        Assert.StartsWith("Lifestone command-line options.", help, StringComparison.Ordinal);
        Assert.All(OptionCatalog.Registry.Names, n => Assert.Contains("  " + n, help, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_m2_track_has_its_option_group_registered()
    {
        // Registered in wave 0, so the tracks only ever edit their own partial file (OptionCatalog.<Group>.cs).
        var features = OptionCatalog.Registry.Groups.Select(g => g.Feature).ToArray();
        Assert.Subset(features.ToHashSet(StringComparer.Ordinal), new HashSet<string>(["Session", "Headless host", "Portal space", "Audio", "Retail UI", "Avatars and player", "Play mode"], StringComparer.Ordinal));
        Assert.Equal(features.Length, features.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Groups_without_options_are_absent_from_help()
    {
        var help = OptionCatalog.HelpText();
        foreach (var group in OptionCatalog.Registry.Groups)
        {
            var header = $"{group.Feature} ({group.Scope})";
            if (group.Options.Count == 0)
            {
                Assert.DoesNotContain(header, help, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains(header, help, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void No_option_takes_a_secret()
    {
        // Passwords come from server/runtime/secrets.json as bytes, never from a command line: tools\godot.ps1 echoes the
        // whole command line, and command lines end up in logs and process listings.
        string[] forbidden = ["pass", "secret", "token", "key"];
        var offenders = OptionCatalog.Registry.Names
            .Where(n => forbidden.Any(f => n.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(offenders.Length == 0, "Options whose name suggests a secret (no option may take one):\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void An_unknown_argument_s_value_is_never_echoed()
    {
        var registry = OptionCatalog.Registry;
        var problems = registry.Problems(CommandLineArgument.ParseAll(["--pasword=hunter2", "--golden=x", "--acount", "hunter2"]), OptionScenes.Main);
        Assert.Equal(
            [
                "unknown argument '--pasword=(value hidden)' (--help lists every option)",
                "'--golden=(value hidden)' is an option of the Terrain preview (scene res://scenes/TerrainPreview.tscn, as tools\\terrain-golden.ps1 runs it), not read in this run's main scene; ignored",
                "unknown argument '--acount' (--help lists every option)",
                "unknown argument #4, which is not an --option (hidden; --help lists every option)",
            ],
            problems);
        Assert.All(problems, p => Assert.DoesNotContain("hunter2", p, StringComparison.Ordinal));
        Assert.All(problems, p => Assert.DoesNotContain("=x", p, StringComparison.Ordinal));
    }

    [Fact]
    public void The_headless_host_reads_core_s_shared_options_and_reports_the_game_s()
    {
        var registry = OptionCatalog.Registry;
        Assert.Empty(registry.Problems(CommandLineArgument.ParseAll(["--help", "--dat-dir=x", "--log-level=debug", "--quit-after=60", "--workers=2"]), OptionScenes.Headless));

        var problem = Assert.Single(registry.Problems(CommandLineArgument.ParseAll(["--dat-dir=x", "--vsync=off"]), OptionScenes.Headless));
        Assert.Equal("'--vsync=(value hidden)' is an option of the Core (every scene), not read in this run's headless host; ignored", problem);
        Assert.True(registry.EndsRun(CommandLineArgument.ParseAll(["--help"]), OptionScenes.Headless));
        Assert.False(registry.EndsRun(CommandLineArgument.ParseAll(["--screenshot=x.png"]), OptionScenes.Headless));

        // OptionScenes.All keeps its meaning: the three Godot scenes, not the headless host.
        Assert.Equal(OptionScenes.None, OptionScenes.All & OptionScenes.Headless);
        Assert.Equal(OptionScenes.All, registry.ScenesOf("--screenshot"));
        Assert.Equal(OptionScenes.All | OptionScenes.Headless, registry.ScenesOf("--dat-dir"));
    }

    [Fact]
    public void The_headless_help_lists_only_what_a_headless_run_reads()
    {
        var help = OptionCatalog.Registry.FormatHelp("Headless.", OptionScenes.Headless);
        Assert.StartsWith("Headless.", help, StringComparison.Ordinal);
        foreach (var name in new[] { "--help", "--dat-dir", "--log-level", "--quit-after", "--workers" })
        {
            Assert.Contains("  " + name, help, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("--vsync", help, StringComparison.Ordinal);

        var listed = help.Split('\n').Where(l => l.StartsWith("  --", StringComparison.Ordinal)).Select(l => l.Trim().Split(' ', '=', '[')[0]);
        Assert.Equal(
            OptionCatalog.Registry.Names.Where(n => OptionCatalog.Registry.AppliesTo(n, OptionScenes.Headless)).Order(StringComparer.Ordinal),
            listed.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_video_capture_options_belong_to_the_main_scene_only()
    {
        var registry = OptionCatalog.Registry;
        Assert.Equal(OptionScenes.Main, registry.ScenesOf("--clean-hud"));
        Assert.Equal(OptionScenes.Main, registry.ScenesOf("--record-audio"));
        Assert.Equal(OptionScenes.Main, registry.ScenesOf("--video-pace"));
        Assert.NotEmpty(registry.Problems(CommandLineArgument.ParseAll(["--record-audio=cache/video/a.wav"]), OptionScenes.SpellViewer));
        Assert.Empty(registry.Problems(CommandLineArgument.ParseAll(["--clean-hud", "--record-audio=cache/video/a.wav", "--video-pace=1.5"]), OptionScenes.Main));
    }
}
