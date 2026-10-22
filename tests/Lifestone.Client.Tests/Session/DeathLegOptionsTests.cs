using Lifestone.Client.Options;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// What the game's death leg passes to <see cref="DeathScenario"/> from the command line (M4 3.1 d2, D20; <see cref="DeathLegOptions"/>, the one place <c>DeathLegRunner</c> reads them): the five new options of the death loop
/// (<c>--death-decoys</c>, <c>--death-rebuff</c>, <c>--death-fight</c>, <c>--death-fight-kills</c>, <c>--death-fight-seconds</c>) reach <see cref="DeathOptions"/>, the fight's options carry the kills and seconds of the death options (and nothing of the fight leg's own
/// <c>--fight-*</c>), the buff cycle's come from the <c>--buff-*</c> options, and a bad value in any of them is the leg's option error (it fails the leg at its first frame, as every other leg's does). The runner's wiring is pinned in <see cref="DeathLegRunnerTests"/>.
/// </summary>
public sealed class DeathLegOptionsTests
{
    private static DeathLegOptions Parse(params string[] args) => DeathLegOptions.FromArguments(CommandLineArgument.ParseAll(args));

    [Fact]
    public void Without_options_the_leg_runs_the_new_loop_with_the_decoys_the_rebuff_and_a_fight_of_four_kills_in_180_seconds()
    {
        var o = Parse();

        Assert.Null(o.Error);
        Assert.Empty(o.Errors);
        Assert.True(o.Death.Decoys);
        Assert.True(o.Death.Rebuff);
        Assert.True(o.Death.Fight);
        Assert.Equal(DeathOptions.DefaultFightKills, o.Fight.Kills);
        Assert.Equal(DeathOptions.DefaultFightSeconds, o.Fight.Seconds);
        Assert.Equal(4, o.Fight.Kills);
        Assert.Equal(180.0, o.Fight.Seconds);
        Assert.Equal(new BuffOptions().CastChance, o.Buff.CastChance);
        Assert.False(o.Fight.ArenaHere); // the live den, never the stand-in's arena
    }

    [Fact]
    public void The_five_new_options_reach_the_death_options_and_the_fights_kills_and_seconds_follow_the_death_options()
    {
        var o = Parse("--death-decoys=off", "--death-rebuff=off", "--death-fight=off", "--death-fight-kills=7", "--death-fight-seconds=45");

        Assert.Null(o.Error);
        Assert.False(o.Death.Decoys);
        Assert.False(o.Death.Rebuff);
        Assert.False(o.Death.Fight);
        Assert.Equal(7, o.Death.FightKills);
        Assert.Equal(45.0, o.Death.FightSeconds);
        Assert.Equal(7, o.Fight.Kills);
        Assert.Equal(45.0, o.Fight.Seconds);
    }

    [Fact]
    public void The_fight_legs_own_options_are_not_the_death_loops_and_the_buff_cycles_are_read()
    {
        // --fight-kills and --fight-seconds belong to the bsd-fight leg; the death loop's fight is sized by --death-fight-kills and --death-fight-seconds only.
        var o = Parse("--fight-kills=9", "--fight-seconds=33", "--buff-cast-chance=0.95", "--buff-mana-wait-s=60");

        Assert.Null(o.Error);
        Assert.Equal(DeathOptions.DefaultFightKills, o.Fight.Kills);
        Assert.Equal(DeathOptions.DefaultFightSeconds, o.Fight.Seconds);
        Assert.Equal(0.95, o.Buff.CastChance);
        Assert.Equal(60.0, o.Buff.ManaWaitSeconds);
    }

    [Theory]
    [InlineData("--death-decoys=maybe", "--death-decoys")]
    [InlineData("--death-rebuff=2", "--death-rebuff")]
    [InlineData("--death-fight=yes please", "--death-fight")]
    [InlineData("--death-fight-kills=0", "--death-fight-kills")]
    [InlineData("--death-fight-kills=1001", "--death-fight-kills")]
    [InlineData("--death-fight-kills=many", "--death-fight-kills")]
    [InlineData("--death-fight-seconds=0", "--death-fight-seconds")]
    [InlineData("--death-fight-seconds=-5", "--death-fight-seconds")]
    [InlineData("--death-fight-seconds=soon", "--death-fight-seconds")]
    [InlineData("--buff-cast-chance=0", "--buff-cast-chance")]
    [InlineData("--buff-transfer-tries=0", "--buff-transfer-tries")]
    public void A_bad_value_is_the_legs_option_error_naming_the_option_and_the_default_stays(string argument, string name)
    {
        var o = Parse(argument);

        Assert.NotNull(o.Error);
        Assert.StartsWith("bad death option: ", o.Error, StringComparison.Ordinal);
        Assert.Contains(name, o.Error, StringComparison.Ordinal);
        Assert.Single(o.Errors);
        Assert.Equal(4, o.Fight.Kills);
        Assert.Equal(180.0, o.Fight.Seconds);
        Assert.True(o.Death.Decoys);
        Assert.True(o.Death.Rebuff);
        Assert.True(o.Death.Fight);
    }

    [Fact]
    public void Every_problem_is_listed_in_the_order_death_options_then_buff_options()
    {
        var o = Parse("--buff-cast-chance=0", "--death-fight-kills=0");

        Assert.Equal(2, o.Errors.Count);
        Assert.StartsWith("--death-fight-kills:", o.Errors[0], StringComparison.Ordinal);
        Assert.StartsWith("--buff-cast-chance:", o.Errors[1], StringComparison.Ordinal);
        Assert.StartsWith("bad death option: --death-fight-kills:", o.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_options_are_registered_for_the_game_so_the_leg_never_warns_about_them()
    {
        var args = CommandLineArgument.ParseAll(["--login-check=bsd-death", "--death-decoys=off", "--death-rebuff=on", "--death-fight=on", "--death-fight-kills=2", "--death-fight-seconds=90", "--buff-cast-chance=0.9"]);

        Assert.Empty(OptionCatalog.Registry.Problems(args, OptionScenes.Main));
    }
}
