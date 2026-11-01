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
    [InlineData("--fight-kills=0", "--fight-kills: 0 (no kill limit) needs --fight-minutes above 0: the fight would end only at --fight-seconds, as a failure")] // M5 1 F1: 0 is no kill limit, with --fight-minutes
    [InlineData("--fight-kills=-1", "--fight-kills: '-1' is not 0 (no kill limit) or a positive whole number")] // M5 1 F1: the 1,000 cap is lifted
    [InlineData("--fight-seconds=0", "--fight-seconds: '0' is not a positive number of seconds")]
    public void A_bad_fight_option_is_reported_and_keeps_its_default(string argument, string expected)
    {
        var o = HeadlessOptions.Parse(["--scenario=fight", argument]);
        Assert.Equal(expected, Assert.Single(o.Errors));
        Assert.Equal((1, 300.0), (o.Fight.Kills, o.Fight.Seconds));
    }

    [Fact]
    public void Fight_minutes_raise_the_fight_scenarios_limit_and_the_default_keeps_it()
    {
        // M5 1 F1: --fight-minutes 30 with no kill limit: the fight's failure limit is 1,800 s, so the scenario's limit grows by 1,500 s over the default's.
        var o = HeadlessOptions.Parse(["--scenario=fight", "--fight-kills=0", "--fight-minutes=30"]);
        Assert.Empty(o.Errors);
        Assert.Equal(Program.FightQuitAfterSeconds + 1500, Program.FightQuitAfterSecondsFor(o.Fight));
        Assert.Equal(Program.FightQuitAfterSeconds, Program.FightQuitAfterSecondsFor(new FightOptions()));
    }

    [Theory]
    [InlineData("fight")]
    [InlineData("buff")]
    [InlineData("login")]
    public void A_bad_cast_mana_value_is_reported_once_though_the_buff_and_the_fight_options_both_parse_it(string scenario)
    {
        // M5 1 fix B (G3): BuffOptions, FightOptions (and the keeper's options) each parse --cast-mana-*; the host lists a problem once.
        var o = HeadlessOptions.Parse(["--scenario=" + scenario, "--cast-mana-tries=many", "--cast-mana-try-chance=2"]);
        var tries = o.Errors.Where(e => e.Contains("cast-mana-tries", StringComparison.Ordinal)).ToList();
        var chance = o.Errors.Where(e => e.Contains("cast-mana-try-chance", StringComparison.Ordinal)).ToList();
        Assert.Single(tries);
        Assert.Single(chance);
        Assert.Equal(o.Errors.Count, o.Errors.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_fight_options_are_headless_options_so_other_scenarios_take_them_without_complaint()
    {
        // Registered for the headless host and the game alike (OptionCatalog.Fight): no unknown-option problem for the login scenario either.
        Assert.Empty(HeadlessOptions.Parse(["--fight-kills=2", "--fight-seconds=60"]).Errors);
    }
}
