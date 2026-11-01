using Lifestone.Client.Options;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The death loop's new-order options (M4 3.1 d1, D20): <c>--death-rebuff on|off</c> and <c>--death-fight on|off</c> (default on), <c>--death-fight-kills N</c> (1 to 1000, default 4) and <c>--death-fight-seconds S</c> (above 0, default 180), parsed by
/// <see cref="DeathOptions.FromArguments"/> like their siblings (a bad value is an error, which fails the headless run and the game's leg as the other death options do), registered for the main scene and the headless host, and the two new stage names.
/// </summary>
public sealed class DeathOrderOptionsTests
{
    private static DeathOptions Parse(params string[] args) => DeathOptions.FromArguments(CommandLineArgument.ParseAll(args));

    [Fact]
    public void The_defaults_are_the_rebuff_and_the_fight_on_with_four_kills_in_180_seconds_and_the_log_line_does_not_change()
    {
        var o = Parse();

        Assert.Empty(o.Errors);
        Assert.True(o.Rebuff);
        Assert.True(o.Fight);
        Assert.Equal(4, o.FightKills);
        Assert.Equal(180.0, o.FightSeconds);
        Assert.Equal(4, DeathOptions.DefaultFightKills);
        Assert.Equal(180.0, DeathOptions.DefaultFightSeconds);
        Assert.Equal("seconds=600", o.ToString());
    }

    [Theory]
    [InlineData("--death-rebuff=off", false)]
    [InlineData("--death-rebuff", true)]
    public void Death_rebuff_takes_on_and_off(string argument, bool expected)
    {
        var o = Parse(argument);

        Assert.Empty(o.Errors);
        Assert.Equal(expected, o.Rebuff);
        Assert.True(o.Fight);
    }

    [Theory]
    [InlineData("--death-fight=off", false)]
    public void Death_fight_takes_on_and_off(string argument, bool expected)
    {
        var o = Parse(argument);

        Assert.Empty(o.Errors);
        Assert.Equal(expected, o.Fight);
        Assert.True(o.Rebuff);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("1000", 1000)]
    public void Death_fight_kills_takes_one_to_a_thousand(string value, int expected)
    {
        var o = Parse("--death-fight-kills=" + value);

        Assert.Empty(o.Errors);
        Assert.Equal(expected, o.FightKills);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1001")]
    [InlineData("many")]
    [InlineData("2.5")]
    public void A_bad_kills_value_is_an_error_and_keeps_the_default(string value)
    {
        var o = Parse("--death-fight-kills=" + value);

        Assert.Single(o.Errors);
        Assert.StartsWith("--death-fight-kills:", o.Errors[0], StringComparison.Ordinal);
        Assert.Equal(DeathOptions.DefaultFightKills, o.FightKills);
    }

    [Theory]
    [InlineData("0.5", 0.5)]
    [InlineData("3600", 3600.0)]
    public void Death_fight_seconds_takes_a_positive_number(string value, double expected)
    {
        var o = Parse("--death-fight-seconds=" + value);

        Assert.Empty(o.Errors);
        Assert.Equal(expected, o.FightSeconds);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("soon")]
    public void A_bad_seconds_value_is_an_error_and_keeps_the_default(string value)
    {
        var o = Parse("--death-fight-seconds=" + value);

        Assert.Single(o.Errors);
        Assert.StartsWith("--death-fight-seconds:", o.Errors[0], StringComparison.Ordinal);
        Assert.Equal(DeathOptions.DefaultFightSeconds, o.FightSeconds);
    }

    [Fact]
    public void A_bad_on_off_value_is_an_error_and_keeps_the_default()
    {
        // The one on/off parser is shared by every death on/off option; each is wired by name here (the corpse fact folded in by the M4 polish fold; the retired decoy option is refused by name in DeathDecoyOptionsTests).
        var o = Parse("--death-rebuff=maybe", "--death-fight=perhaps", "--death-recover-corpse=maybe");

        Assert.Equal(3, o.Errors.Count);
        Assert.Contains("--death-rebuff: 'maybe' is not on or off", o.Errors);
        Assert.Contains("--death-fight: 'perhaps' is not on or off", o.Errors);
        Assert.Contains("--death-recover-corpse: 'maybe' is not on or off", o.Errors);
        Assert.True(o.Rebuff);
        Assert.True(o.Fight);
        Assert.True(o.RecoverCorpse);
    }

    [Fact]
    public void A_bad_value_does_not_hide_a_good_sibling_and_the_options_read_together()
    {
        var bad = Parse("--death-fight-kills=0", "--death-fight-seconds=90", "--death-seconds=120");
        Assert.Single(bad.Errors);
        Assert.Equal(90.0, bad.FightSeconds);
        Assert.Equal(120.0, bad.Seconds);

        var all = Parse("--death-rebuff=off", "--death-fight=off", "--death-fight-kills=7", "--death-fight-seconds=60");
        Assert.Empty(all.Errors);
        Assert.False(all.Rebuff);
        Assert.False(all.Fight);
        Assert.Equal(7, all.FightKills);
        Assert.Equal(60.0, all.FightSeconds);
        Assert.Equal("seconds=600 rebuff=off fight=off fight_kills=7 fight_seconds=60", all.ToString());
    }

    [Fact]
    public void The_four_options_are_registered_for_the_main_scene_and_the_headless_host_after_the_retired_decoys_entry()
    {
        var group = OptionCatalog.Death();

        Assert.Equal(OptionScenes.Main | OptionScenes.Headless, group.Scenes);
        var rebuff = Assert.Single(group.Options, o => o.Name == "--death-rebuff");
        var fight = Assert.Single(group.Options, o => o.Name == "--death-fight");
        Assert.Single(group.Options, o => o.Name == "--death-fight-kills");
        Assert.Single(group.Options, o => o.Name == "--death-fight-seconds");
        Assert.Equal("on|off", rebuff.Value);
        Assert.Equal("on|off", fight.Value);
        var names = group.Options.Select(o => o.Name).ToList();
        Assert.True(names.IndexOf("--death-decoys") < names.IndexOf("--death-rebuff"));
        Assert.Equal(["--death-rebuff", "--death-fight", "--death-fight-kills", "--death-fight-seconds"], names.Skip(names.IndexOf("--death-decoys") + 1));
    }

    [Fact]
    public void The_new_stages_keep_every_existing_value_and_are_in_all_and_in_the_stage_list()
    {
        Assert.Equal(256, (int)DeathStages.Rebuff);
        Assert.Equal(512, (int)DeathStages.Fight);
        Assert.Equal(1, (int)DeathStages.Gear);
        Assert.Equal(128, (int)DeathStages.Corpse);
        Assert.True(DeathStages.All.HasFlag(DeathStages.Rebuff));
        Assert.True(DeathStages.All.HasFlag(DeathStages.Fight));
        Assert.Equal(DeathStages.Death | DeathStages.Rebuff | DeathStages.Fight, DeathOptions.ParseStages("death,rebuff,fight", out var problem));
        Assert.Null(problem);
        Assert.Equal(DeathStages.Rebuff, DeathOptions.ParseStages("REBUFF", out _));
        Assert.Equal(DeathStages.All, DeathOptions.ParseStages("all", out _));
        DeathOptions.ParseStages("heal", out var bad);
        Assert.Contains("rebuff, fight", bad, StringComparison.Ordinal);
        Assert.Equal("seconds=600 stages=death+rebuff+fight", Parse("--death-stages=death,rebuff,fight").ToString());
    }
}
