using Lifestone.Client.Options;

namespace Lifestone.Client.Tests.Options;

/// <summary>The shared tokenizer, the value parsers and the registry's answers (known, read in a scene, ends the run, help).</summary>
public sealed class OptionRegistryTests
{
    private static OptionRegistry Sample() => new(
    [
        new OptionGroup("Alpha", "everywhere",
        [
            new OptionSpec("--size", "N", "A size."),
            new OptionSpec("--shot", "PATH", "Saves and quits.", OptionRole.EndsRun),
            new OptionSpec("--help", null, "Help.", OptionRole.Help),
        ]),
        new OptionGroup("Beta", "one scene",
        [
            new OptionSpec("--check-a", null, "Check A.", OptionRole.CheckSuite),
            new OptionSpec("--check-b", null, "Check B.", OptionRole.CheckSuite),
            new OptionSpec("--out", "DIR", "Output.", OptionRole.EndsRun, ValueOptional: true),
            new OptionSpec("--shared", null, "Also the terrain preview's.", AlsoIn: OptionScenes.TerrainPreview),
        ], OptionScenes.Main),
        new OptionGroup("Gamma", "the spell viewer",
        [
            new OptionSpec("--show", null, "Shows and quits.", OptionRole.EndsRun),
        ], OptionScenes.SpellViewer),
    ]);

    [Theory]
    [InlineData("--a", "--a", "", false)]
    [InlineData("  --a=1  ", "--a", "1", true)]
    [InlineData("--a=", "--a", "", true)]
    [InlineData("--a=b=c", "--a", "b=c", true)]
    [InlineData("--path=\"C:/My Dir/x.png\"", "--path", "C:/My Dir/x.png", true)]
    public void Arguments_split_at_the_first_equals_sign(string raw, string name, string value, bool hasValue)
    {
        var argument = CommandLineArgument.Parse(raw);
        Assert.Equal(name, argument.Name);
        Assert.Equal(value, argument.Value);
        Assert.Equal(hasValue, argument.HasValue);
        Assert.Equal(raw.Trim(), argument.Raw);
    }

    [Fact]
    public void Empty_arguments_are_skipped()
    {
        var all = CommandLineArgument.ParseAll(["", "  ", "--x"]);
        Assert.Equal(["--x"], all.Select(a => a.Name));
        Assert.Equal(string.Empty, CommandLineArgument.Parse(null).Raw);
    }

    [Fact]
    public void Values_that_do_not_parse_are_errors_and_keep_the_fallback()
    {
        var errors = new List<string>();
        Assert.Equal(7, OptionValues.Int(CommandLineArgument.Parse("--n=7"), errors, 3));
        Assert.Equal(3, OptionValues.Int(CommandLineArgument.Parse("--n=seven"), errors, 3));
        Assert.Equal(0.25, OptionValues.Double(CommandLineArgument.Parse("--x=0.25"), errors, 1.0));
        Assert.Equal(1.0, OptionValues.Double(CommandLineArgument.Parse("--x=NaN"), errors, 1.0));
        Assert.Equal(2, errors.Count);
        Assert.Contains("--n: 'seven' is not an integer", errors);
    }

    [Theory]
    [InlineData("--v", true)]
    [InlineData("--v=on", true)]
    [InlineData("--v=1", true)]
    [InlineData("--v=off", false)]
    [InlineData("--v=false", false)]
    [InlineData("--v=no", false)]
    public void Switches_accept_on_and_off_words(string raw, bool expected)
    {
        var errors = new List<string>();
        Assert.Equal(expected, OptionValues.Switch(CommandLineArgument.Parse(raw), errors, !expected));
        Assert.Empty(errors);
    }

    [Fact]
    public void A_mistyped_choice_is_an_error_not_a_silent_default()
    {
        var errors = new List<string>();
        var choices = new[] { ("on", 1), ("off", 0), ("mailbox", 2) };
        Assert.Equal(2, OptionValues.Choice(CommandLineArgument.Parse("--vsync=mailbox"), errors, 1, choices));
        Assert.Empty(errors);
        Assert.Equal(1, OptionValues.Choice(CommandLineArgument.Parse("--vsync=of"), errors, 1, choices));
        Assert.Equal(["--vsync: 'of' is not one of on, off, mailbox"], errors);
        Assert.False(OptionValues.Switch(CommandLineArgument.Parse("--v=maybe"), errors, fallback: false));
        Assert.Equal("--v: 'maybe' is not on or off", errors[^1]);
    }

    [Fact]
    public void Unknown_means_registered_by_no_feature()
    {
        var registry = Sample();
        var args = CommandLineArgument.ParseAll(["--size=3", "--check-a", "--sise=3", "-x"]);
        Assert.Equal(
            ["unknown argument '--sise=3' (--help lists every option)", "unknown argument '-x' (--help lists every option)"],
            registry.Problems(args, OptionScenes.Main));
        Assert.True(registry.IsKnown("--out"));
        Assert.Equal("Beta", registry.FeatureOf("--out"));
        Assert.Null(registry.Find("--sise"));
        Assert.Equal(OptionScenes.None, registry.ScenesOf("--sise"));
    }

    [Fact]
    public void Options_of_another_scene_are_reported_and_ignored()
    {
        var registry = Sample();
        Assert.Equal(OptionScenes.All, registry.ScenesOf("--size"));
        Assert.Equal(OptionScenes.Main | OptionScenes.TerrainPreview, registry.ScenesOf("--shared"));
        Assert.True(registry.AppliesTo("--shared", OptionScenes.TerrainPreview));
        Assert.False(registry.AppliesTo("--out", OptionScenes.SpellViewer));
        Assert.False(registry.AppliesTo("--sise", OptionScenes.All));

        var args = CommandLineArgument.ParseAll(["--size=3", "--show", "--shared", "--out=x"]);
        Assert.Equal(
            ["'--show' is an option of the Gamma (the spell viewer), not read in this run's main scene; ignored"],
            registry.Problems(args, OptionScenes.Main));
        Assert.Equal(2, registry.Problems(args, OptionScenes.SpellViewer).Count);
        Assert.Equal(
            [
                "'--show' is an option of the Gamma (the spell viewer), not read in this run's terrain preview; ignored",
                "'--out=x' is an option of the Beta (one scene), not read in this run's terrain preview; ignored",
            ],
            registry.Problems(args, OptionScenes.TerrainPreview));
        Assert.Empty(registry.Problems(args, OptionScenes.All));
    }

    [Fact]
    public void Runs_end_by_themselves_when_an_option_of_their_scene_says_so()
    {
        var registry = Sample();
        Assert.False(registry.EndsRun(CommandLineArgument.ParseAll(["--size=3"]), OptionScenes.Main));
        Assert.True(registry.EndsRun(CommandLineArgument.ParseAll(["--size=3", "--out"]), OptionScenes.Main));
        Assert.True(registry.EndsRun(CommandLineArgument.ParseAll(["--check-b"]), OptionScenes.Main));
        Assert.True(registry.EndsRun(CommandLineArgument.ParseAll(["--help"]), OptionScenes.SpellViewer));
        Assert.False(registry.EndsRun(CommandLineArgument.ParseAll(["--unknown"]), OptionScenes.All));

        // Another scene's ending option does not make this scene's run automated (nothing here would end it).
        Assert.False(registry.EndsRun(CommandLineArgument.ParseAll(["--show"]), OptionScenes.Main));
        Assert.True(registry.EndsRun(CommandLineArgument.ParseAll(["--show"]), OptionScenes.SpellViewer));
        Assert.False(registry.EndsRun(CommandLineArgument.ParseAll(["--out"]), OptionScenes.TerrainPreview));
    }

    [Fact]
    public void A_name_is_registered_once_and_well_formed()
    {
        var twice = Assert.Throws<ArgumentException>(() => new OptionRegistry(
        [
            new OptionGroup("A", "x", [new OptionSpec("--same", null, "a")]),
            new OptionGroup("B", "y", [new OptionSpec("--same", "N", "b")]),
        ]));
        Assert.Contains("registered by both 'A' and 'B'", twice.Message, StringComparison.Ordinal);

        foreach (var bad in new[] { "-x", "--", "---x", "--Upper", "--a_b", "--a=b" })
        {
            Assert.Throws<ArgumentException>(() => new OptionRegistry([new OptionGroup("A", "x", [new OptionSpec(bad, null, "a")])]));
        }

        var nowhere = Assert.Throws<ArgumentException>(() => new OptionRegistry([new OptionGroup("A", "x", [new OptionSpec("--x", null, "a")], OptionScenes.None)]));
        Assert.Contains("read in no scene", nowhere.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_lists_every_group_and_option_with_its_usage()
    {
        var help = Sample().FormatHelp("Preamble line.");
        Assert.StartsWith("Preamble line.", help, StringComparison.Ordinal);
        Assert.Contains("Alpha (everywhere)", help, StringComparison.Ordinal);
        Assert.Contains("Beta (one scene)", help, StringComparison.Ordinal);
        Assert.Contains("  --size=N", help, StringComparison.Ordinal);
        Assert.Contains("  --out[=DIR]", help, StringComparison.Ordinal);
        Assert.Contains("  --help", help, StringComparison.Ordinal);
        Assert.Contains("Saves and quits. " + OptionRegistry.EndsRunMarker, help, StringComparison.Ordinal);
        Assert.Contains("Check A. " + OptionRegistry.EndsRunMarker, help, StringComparison.Ordinal);
        Assert.DoesNotContain("A size. " + OptionRegistry.EndsRunMarker, help, StringComparison.Ordinal);
    }
}
