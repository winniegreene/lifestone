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
        Assert.Equal(Program.DeathQuitAfterOverheadSeconds + 120.5, Program.DeathQuitAfterSeconds(o.Death.Seconds));
        Assert.Equal(1500.0, Program.DeathQuitAfterSeconds(DeathOptions.DefaultSeconds));
        Assert.Equal(900.0, Program.DeathQuitAfterOverheadSeconds);
    }

    [Theory]
    [InlineData("--death-seconds=0", "--death-seconds: '0' is not 1 to 3600 seconds")]
    [InlineData("--death-seconds=3601", "--death-seconds: '3601' is not 1 to 3600 seconds")]
    [InlineData("--death-seconds=-5", "--death-seconds: '-5' is not 1 to 3600 seconds")]
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
    public void Death_and_regear_runs_on_the_default_test_account_only()
    {
        var error = Assert.Single(HeadlessOptions.Parse(["--scenario=death-and-regear", "--account=somebody"]).Errors);
        Assert.Equal("--account: --scenario=death-and-regear runs on the default test account 'test' only; the value is not shown", error);
        Assert.Empty(HeadlessOptions.Parse(["--scenario=death-and-regear", "--account=test"]).Errors);
    }

    [Fact]
    public void The_death_option_is_a_headless_option_so_other_scenarios_take_it_without_complaint()
    {
        Assert.Empty(HeadlessOptions.Parse(["--death-seconds=60"]).Errors);
    }
}
