using Lifestone.Client.Options;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The death loop's decoy option (M4 3.1 d0, D20): <c>--death-decoys on|off</c> (default on), parsed by <see cref="DeathOptions.FromArguments"/> like its siblings (a bad value is an error, which fails the headless run and the game's leg as the other death options do) and registered
/// for the main scene and the headless host. <see cref="DeathOptions.Decoys"/> is what the death scenario's kit builder reads (<c>GearKit.ForDeathLoop</c>).
/// </summary>
public sealed class DeathDecoyOptionsTests
{
    private static DeathOptions Parse(params string[] args) => DeathOptions.FromArguments(CommandLineArgument.ParseAll(args));

    [Fact]
    public void The_decoys_are_on_by_default_and_the_log_line_is_unchanged_by_the_default()
    {
        var o = Parse();

        Assert.Empty(o.Errors);
        Assert.True(o.Decoys);
        Assert.True(new DeathOptions().Decoys);
        Assert.Equal("seconds=600", o.ToString());
    }

    [Theory]
    [InlineData("--death-decoys=off", false)]
    [InlineData("--death-decoys=no", false)]
    [InlineData("--death-decoys=on", true)]
    [InlineData("--death-decoys=yes", true)]
    [InlineData("--death-decoys", true)]
    public void Death_decoys_takes_on_and_off(string argument, bool expected)
    {
        var o = Parse(argument);

        Assert.Empty(o.Errors);
        Assert.Equal(expected, o.Decoys);
    }

    [Fact]
    public void A_bad_value_is_an_error_and_keeps_the_default()
    {
        var o = Parse("--death-decoys=maybe");

        Assert.Equal(["--death-decoys: 'maybe' is not on or off"], o.Errors);
        Assert.True(o.Decoys);
    }

    [Fact]
    public void The_option_reads_together_with_the_others_and_shows_in_the_log_line_only_when_off()
    {
        var o = Parse("--death-seconds=120", "--death-decoys=off", "--death-recover-corpse=off", "--death-recover-min-health=75");

        Assert.Empty(o.Errors);
        Assert.False(o.Decoys);
        Assert.Equal(120.0, o.Seconds);
        Assert.False(o.RecoverCorpse);
        Assert.Equal(75.0, o.RecoverMinHealthPercent);
        Assert.Equal("seconds=120 recover_corpse=off decoys=off recover_min_health=75", o.ToString());
        Assert.Equal("seconds=600 decoys=off", Parse("--death-decoys=off").ToString());
        Assert.Equal("seconds=600", Parse("--death-decoys=on").ToString());
    }

    [Fact]
    public void A_bad_decoys_value_does_not_hide_a_good_value_of_a_sibling()
    {
        var o = Parse("--death-decoys=maybe", "--death-seconds=90");

        Assert.Equal(["--death-decoys: 'maybe' is not on or off"], o.Errors);
        Assert.Equal(90.0, o.Seconds);
    }

    [Fact]
    public void The_option_is_registered_for_the_main_scene_and_the_headless_host_with_a_help_that_names_the_decoys_and_the_check()
    {
        var group = OptionCatalog.Death();

        Assert.Equal(OptionScenes.Main | OptionScenes.Headless, group.Scenes);
        var decoys = Assert.Single(group.Options, o => o.Name == "--death-decoys");
        Assert.Equal("on|off", decoys.Value);
        Assert.Contains("default on", decoys.Help, StringComparison.Ordinal);
        Assert.Contains("452 burden", decoys.Help, StringComparison.Ordinal);
        Assert.Contains("decoy check", decoys.Help, StringComparison.Ordinal);
        Assert.Contains("off is M3's kit exactly", decoys.Help, StringComparison.Ordinal);
        // The siblings are still there, in front of it.
        Assert.Equal(["--death-seconds", "--death-stages", "--death-recover-corpse", "--death-recover-min-health", "--death-decoys", "--death-rebuff", "--death-fight", "--death-fight-kills", "--death-fight-seconds"], group.Options.Select(o => o.Name));
    }

    [Fact]
    public void Every_stage_and_the_other_options_are_untouched_by_the_new_option()
    {
        Assert.Equal(DeathStages.All, DeathOptions.ParseStages("all", out var problem));
        Assert.Null(problem);
        Assert.Equal(600.0, new DeathOptions().Seconds);
        Assert.True(new DeathOptions().RecoverCorpse);
    }
}
