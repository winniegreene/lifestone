using Lifestone.Client.Session.Scenarios;
using Lifestone.Headless;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// The headless host's fight scenario options (M3 3.1 lane fa): <c>--scenario=fight</c> with <c>--fight-kills</c> and <c>--fight-seconds</c> and the buff options it shares, the default test account rule, and the
/// limit of the fight scenario. Plain unit tests (no backend); they live here because this project is the one that references the headless host.
/// </summary>
public sealed class HeadlessFightOptionsTests
{
    [Fact]
    public void Fight_is_a_scenario_with_one_kill_and_five_minutes_by_default()
    {
        var o = HeadlessOptions.Parse(["--scenario=fight"]);
        Assert.Empty(o.Errors);
        Assert.Equal(HeadlessScenario.Fight, o.Scenario);
        Assert.Equal((FightOptions.DefaultKills, FightOptions.DefaultSeconds), (o.Fight.Kills, o.Fight.Seconds));
        Assert.Equal((1, 300.0), (o.Fight.Kills, o.Fight.Seconds));
        Assert.True(Program.FightQuitAfterSeconds > Program.BuffQuitAfterSeconds, "a fight scenario may run a whole buff cycle first");
    }

    [Fact]
    public void The_fight_options_parse_and_reach_the_scenario_with_the_buff_options()
    {
        var o = HeadlessOptions.Parse(["--scenario=fight", "--fight-kills=3", "--fight-seconds=120.5", "--buff-cast-chance=0.95"]);
        Assert.Empty(o.Errors);
        Assert.Equal((3, 120.5), (o.Fight.Kills, o.Fight.Seconds));
        Assert.Equal(0.95, o.Buff.CastChance);
    }

    [Theory]
    [InlineData("--fight-kills=0", "--fight-kills: '0' is not 1 to 1000")]
    [InlineData("--fight-kills=1001", "--fight-kills: '1001' is not 1 to 1000")]
    [InlineData("--fight-seconds=0", "--fight-seconds: '0' is not a positive number of seconds")]
    [InlineData("--fight-seconds=-5", "--fight-seconds: '-5' is not a positive number of seconds")]
    public void A_bad_fight_option_is_reported_and_keeps_its_default(string argument, string expected)
    {
        var o = HeadlessOptions.Parse(["--scenario=fight", argument]);
        Assert.Equal(expected, Assert.Single(o.Errors));
        Assert.Equal((1, 300.0), (o.Fight.Kills, o.Fight.Seconds));
    }

    [Fact]
    public void Fight_runs_on_the_default_test_account_only()
    {
        var error = Assert.Single(HeadlessOptions.Parse(["--scenario=fight", "--account=somebody"]).Errors);
        Assert.Equal("--account: --scenario=fight runs on the default test account 'test' only; the value is not shown", error);
        Assert.Empty(HeadlessOptions.Parse(["--scenario=fight", "--account=test"]).Errors);
    }

    [Fact]
    public void The_fight_options_are_headless_options_so_other_scenarios_take_them_without_complaint()
    {
        // Registered for the headless host and the game alike (OptionCatalog.Fight): no unknown-option problem for the login scenario either.
        Assert.Empty(HeadlessOptions.Parse(["--fight-kills=2", "--fight-seconds=60"]).Errors);
    }
}
