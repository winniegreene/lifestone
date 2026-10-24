using Lifestone.Client.Options;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The corpse trip's options (M4 2.2 s1): <c>--death-recover-corpse on|off</c> (default on) and <c>--death-recover-min-health PCT</c> (default 60), parsed by <see cref="DeathOptions.FromArguments"/> (a bad value is an error, which fails the
/// headless run and the game's leg as the other death options do), registered for the main scene and the headless host, and the new <see cref="DeathStages.Corpse"/> stage.
/// </summary>
public sealed class DeathCorpseOptionsTests
{
    private static DeathOptions Parse(params string[] args) => DeathOptions.FromArguments(CommandLineArgument.ParseAll(args));

    [Fact]
    public void The_trip_is_on_with_a_sixty_percent_minimum_by_default()
    {
        var o = Parse();

        Assert.Empty(o.Errors);
        Assert.True(o.RecoverCorpse);
        Assert.Equal(60.0, o.RecoverMinHealthPercent);
        Assert.Equal(DeathOptions.DefaultRecoverMinHealthPercent, o.RecoverMinHealthPercent);
        Assert.Equal("seconds=600", o.ToString()); // M3's log line is unchanged by the defaults
    }

    [Theory]
    [InlineData("--death-recover-corpse=off", false)]
    public void Recover_corpse_takes_on_and_off(string argument, bool expected)
    {
        var o = Parse(argument);

        Assert.Empty(o.Errors);
        Assert.Equal(expected, o.RecoverCorpse);
    }

    [Theory]
    [InlineData("0", 0.0)]
    [InlineData("72.5", 72.5)]
    [InlineData("100", 100.0)]
    public void Min_health_takes_zero_to_a_hundred_percent(string value, double expected)
    {
        var o = Parse("--death-recover-min-health=" + value);

        Assert.Empty(o.Errors);
        Assert.Equal(expected, o.RecoverMinHealthPercent);
    }

    [Theory]
    [InlineData("-1", "--death-recover-min-health: '-1' is not 0 to 100 percent")]
    [InlineData("101", "--death-recover-min-health: '101' is not 0 to 100 percent")]
    [InlineData("high", "--death-recover-min-health: 'high' is not a number")]
    public void A_bad_min_health_is_an_error_and_keeps_the_default(string value, string error)
    {
        var o = Parse("--death-recover-min-health=" + value);

        Assert.Equal([error], o.Errors);
        Assert.Equal(DeathOptions.DefaultRecoverMinHealthPercent, o.RecoverMinHealthPercent);
    }

    [Fact]
    public void The_corpse_stage_is_between_the_death_and_the_regear_in_all_and_in_the_stage_list()
    {
        Assert.Equal(128, (int)DeathStages.Corpse);
        Assert.True(DeathStages.All.HasFlag(DeathStages.Corpse));
        Assert.Equal(DeathStages.Death | DeathStages.Corpse | DeathStages.Regear, DeathOptions.ParseStages("death,corpse,regear", out var problem));
        Assert.Null(problem);
        Assert.Equal(DeathStages.All, DeathOptions.ParseStages("all", out _));
        Assert.Equal(DeathStages.Gear | DeathStages.Bind | DeathStages.Den | DeathStages.Unbuff | DeathStages.Death | DeathStages.Regear | DeathStages.Vitae | DeathStages.Rebuff | DeathStages.Fight, DeathStages.All & ~DeathStages.Corpse);
        DeathOptions.ParseStages("trip", out var bad);
        Assert.Contains("corpse", bad, StringComparison.Ordinal);
    }

    [Fact]
    public void M4_2_2_fix2_R11_the_login_check_help_names_all_four_corpse_looted_values()
    {
        var loginCheck = Assert.Single(OptionCatalog.Play().Options, o => o.Name == "--login-check");
        Assert.Contains("corpse_looted=<yes|partial|no|skipped>", loginCheck.Help, StringComparison.Ordinal);
        Assert.DoesNotContain("<yes|no|skipped>", loginCheck.Help, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_options_are_registered_for_the_main_scene_and_the_headless_host()
    {
        var group = OptionCatalog.Death();

        Assert.Equal(OptionScenes.Main | OptionScenes.Headless, group.Scenes);
        var corpse = Assert.Single(group.Options, o => o.Name == "--death-recover-corpse");
        Assert.Equal("on|off", corpse.Value);
        Assert.Contains("corpse_looted=", corpse.Help, StringComparison.Ordinal);
        Assert.Contains("partial when some did", corpse.Help, StringComparison.Ordinal); // M4 2.2 fix R2: the help names the third value
        var health = Assert.Single(group.Options, o => o.Name == "--death-recover-min-health");
        Assert.Equal("PCT", health.Value);
        Assert.Contains("default 60", health.Help, StringComparison.Ordinal);
    }
}
