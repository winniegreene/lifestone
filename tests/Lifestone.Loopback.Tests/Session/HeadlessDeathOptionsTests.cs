using Lifestone.Client.Session.Scenarios;
using Lifestone.Headless;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// The headless host's death-and-regear options (M3 3.2 lane da): <c>--scenario=death-and-regear</c> with <c>--death-seconds</c>, the default test account rule and the scenario's time limit. Plain unit tests (no backend); they
/// live here because this project is the one that references the headless host.
/// </summary>
public sealed class HeadlessDeathOptionsTests
{
    [Fact]
    public void Death_and_regear_is_a_scenario_with_ten_minutes_among_the_tuskers_by_default()
    {
        var o = HeadlessOptions.Parse(["--scenario=death-and-regear"]);
        Assert.Empty(o.Errors);
        Assert.Equal(HeadlessScenario.DeathAndRegear, o.Scenario);
        Assert.Equal(DeathOptions.DefaultSeconds, o.Death.Seconds);
        Assert.Equal(600.0, o.Death.Seconds);
        Assert.False(o.Death.StopAfterHits); // not on the command line
    }

    [Fact]
    public void The_death_seconds_option_parses_and_the_time_limit_covers_the_gear_the_bind_a_suicide_the_wait_and_the_log_off()
    {
        var o = HeadlessOptions.Parse(["--scenario=death-and-regear", "--death-seconds=120.5"]);
        Assert.Empty(o.Errors);
        Assert.Equal(120.5, o.Death.Seconds);
        Assert.Equal(Program.DeathQuitAfterOverheadSeconds + Program.DeathQuitAfterNewStagesSeconds + DeathOptions.DefaultFightSeconds + 120.5, Program.DeathQuitAfterSeconds(o.Death.Seconds, o.Death.FightSeconds));
        Assert.Equal(2310.0, Program.DeathQuitAfterSeconds(DeathOptions.DefaultSeconds)); // 3.1 fix R19: the new loop's 900 + 630 + 180 + 600, not the old 1500
        Assert.Equal(900.0, Program.DeathQuitAfterOverheadSeconds);
        Assert.Equal(630.0, Program.DeathQuitAfterNewStagesSeconds);
    }

    [Fact]
    public void The_death_fight_seconds_option_moves_the_headless_limit_and_the_formula_is_the_loopback_tests_3_1_fix_R19()
    {
        var o = HeadlessOptions.Parse(["--scenario=death-and-regear", "--death-fight-seconds=300", "--death-seconds=100"]);
        Assert.Empty(o.Errors);
        Assert.Equal(300.0, o.Death.FightSeconds);
        Assert.Equal(900.0 + 630.0 + 300.0 + 100.0, Program.DeathQuitAfterSeconds(o.Death.Seconds, o.Death.FightSeconds));

        // The loopback tests' own limit and the host's default are one formula (the game leg's 2,015 s plus the log off, the margin and the old overhead's slack).
        foreach (var args in new[] { Array.Empty<string>(), ["--death-seconds=120.5"], ["--death-fight-seconds=60"], ["--death-seconds=900", "--death-fight-seconds=240"] })
        {
            var parsed = HeadlessOptions.Parse(["--scenario=death-and-regear", .. args]);
            Assert.Empty(parsed.Errors);
            Assert.Equal(DeathLive.QuitAfterSecondsFor(parsed.Death), Program.DeathQuitAfterSeconds(parsed.Death.Seconds, parsed.Death.FightSeconds));
        }
    }

    [Theory]
    [InlineData("--death-seconds=0", "--death-seconds: '0' is not 1 to 3600 seconds")]
    [InlineData("--death-seconds=3601", "--death-seconds: '3601' is not 1 to 3600 seconds")]
    public void A_bad_death_option_is_reported_and_keeps_its_default(string argument, string expected)
    {
        var o = HeadlessOptions.Parse(["--scenario=death-and-regear", argument]);
        Assert.Equal(expected, Assert.Single(o.Errors));
        Assert.Equal(600.0, o.Death.Seconds);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("3600")]
    public void The_ends_of_the_range_are_accepted(string value)
    {
        Assert.Empty(HeadlessOptions.Parse(["--scenario=death-and-regear", "--death-seconds=" + value]).Errors);
    }

    [Fact]
    public void The_death_option_is_a_headless_option_so_other_scenarios_take_it_without_complaint()
    {
        Assert.Empty(HeadlessOptions.Parse(["--death-seconds=60"]).Errors);
    }
}
