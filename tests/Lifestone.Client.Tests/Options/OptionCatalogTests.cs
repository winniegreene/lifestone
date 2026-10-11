using System.Text.RegularExpressions;
using Lifestone.Client.Options;

namespace Lifestone.Client.Tests.Options;

/// <summary>
/// The game's option registry against the parsers that read the options (Lifestone.Game's source, which tests cannot
/// reference because it needs the engine): every option a parser reads is registered with help, and every registered
/// option is read somewhere. This is what keeps "unknown argument" meaning truly unknown after features add options.
/// </summary>
public sealed partial class OptionCatalogTests
{
    /// <summary>Option literals on <c>case</c> lines (<c>case "--x":</c>, <c>case "--x" or "--y":</c>).</summary>
    [GeneratedRegex("\"(--[a-z0-9][a-z0-9-]*)\"")]
    private static partial Regex OptionLiteral();

    /// <summary>A literal that is exactly an option, with an optional fixed value (<c>"--scene=spellviewer"</c>).</summary>
    [GeneratedRegex("\"(--[a-z0-9][a-z0-9-]*)(=[a-z0-9-]*)?\"")]
    private static partial Regex WholeOptionLiteral();

    private static IEnumerable<(string File, string Text)> GameSources() =>
        Directory.EnumerateFiles(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts"), "*.cs", SearchOption.AllDirectories)
            .Select(f => (Path.GetRelativePath(RepositoryFiles.Root, f).Replace('\\', '/'), File.ReadAllText(f)));

    /// <summary>Option names the Game's parsers read: <c>case</c> labels anywhere, and option literals in *Options.cs / *Arguments.cs.</summary>
    private static Dictionary<string, string> ParsedOptions()
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (file, text) in GameSources())
        {
            var parserFile = file.EndsWith("Options.cs", StringComparison.Ordinal) || file.EndsWith("Arguments.cs", StringComparison.Ordinal);
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
            ["--blend-checks", "--engine-checks", "--interior-engine-checks", "--sky-checks"],
            registry.Names.Where(n => registry.Find(n)!.Role == OptionRole.CheckSuite).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_option_a_game_parser_reads_is_registered()
    {
        var registry = OptionCatalog.Registry;
        var parsed = ParsedOptions();
        Assert.True(parsed.Count > 60, $"the source scan found only {parsed.Count} options; did the parsers move?");
        var missing = parsed.Where(p => !registry.IsKnown(p.Key)).Select(p => $"{p.Key} (read in {p.Value})").Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            missing.Length == 0,
            "These options are parsed but not registered, so the game reports them as unknown and --help does not list them. " +
            "Add an OptionSpec with help to the feature's group in src/Lifestone.Client/Options/OptionCatalog.cs:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void Every_registered_option_is_read_by_some_parser()
    {
        var sources = string.Join("\n", GameSources().Select(s => s.Text));
        var unread = OptionCatalog.Registry.Names
            .Where(n => !sources.Contains($"\"{n}\"", StringComparison.Ordinal) && !sources.Contains($"\"{n}=", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(unread.Length == 0, "Registered in OptionCatalog.cs but read by no parser in src/Lifestone.Game (remove or rename them):\n  " + string.Join("\n  ", unread));
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
                     ("--interior-engine-checks", OptionScenes.Main), ("--engine-checks", OptionScenes.Main), ("--blend-checks", OptionScenes.Main),
                     ("--help", OptionScenes.Main),
                     ("--showcase", OptionScenes.SpellViewer), ("--screenshot=x.png", OptionScenes.SpellViewer),
                     ("--golden", OptionScenes.TerrainPreview), ("--screenshot=x.png", OptionScenes.TerrainPreview),
                 })
        {
            Assert.True(registry.EndsRun(CommandLineArgument.ParseAll([endsRun]), scene), $"{endsRun} in {scene}");
        }

        foreach (var interactive in new[] { "--start=arwic", "--walk", "--ui=open", "--interior-show-all", "--quit-after=60", "--scene=spellviewer" })
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
        Assert.Empty(registry.Problems(CommandLineArgument.ParseAll(["--seamless-normals", "--retail-normals", "--day-group=2", "--golden", "--dat-dir=x", "--max-mip=2"]), OptionScenes.TerrainPreview));
        Assert.Empty(registry.Problems(CommandLineArgument.ParseAll(["--seamless-normals", "--retail-normals", "--day-group=2", "--scene=spellviewer", "--blend-space=linear", "--detail=off"]), OptionScenes.Main));
        Assert.Empty(registry.Problems(CommandLineArgument.ParseAll(["--scene=spellviewer", "--showcase", "--time-of-day=0.3", "--retail-normals", "--blend-space=linear", "--detail=off"]), OptionScenes.SpellViewer));
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
}
