using Lifestone.Client.Session.Hosting;
using Lifestone.Headless;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// <c>--scenario=autopilot</c> in the headless host (M5 4 h1; a plain unit test in this project, as <see cref="J4HeadlessJourneyOptionsTests"/>): the scenario parses to BSD Mage's plan on the test account, the account and journey rules, the
/// <c>--autopilot-*</c> options (bound, parsed once, taken by this scenario only), and the lifted limit (the stop rules end the run: a ceiling from <c>--autopilot-minutes</c>, none without it, <c>--quit-after</c> wins).
/// </summary>
public sealed class AutopilotHeadlessOptionsTests
{
    [Fact]
    public void The_scenario_is_the_mages_on_the_test_account_and_reads_the_autopilot_options()
    {
        var o = HeadlessOptions.Parse(["--scenario=autopilot", "--autopilot-minutes=90", "--autopilot-level=80", "--autopilot-trip-after-kills=2", "--autopilot-test-death", "--fight-minutes=5"]);
        Assert.Empty(o.Errors);
        Assert.Equal(HeadlessScenario.Autopilot, o.Scenario);
        Assert.Equal("BSD Mage", o.Journey!.Character);
        Assert.Equal("test", o.Session.Account);
        Assert.Equal((90.0, 80, 2, true), (o.Autopilot.Minutes, o.Autopilot.Level, o.Autopilot.TripAfterKills, o.Autopilot.TestDeath));
        Assert.Contains("--fight-minutes=5", o.Arguments); // the stations read their options from the command line as given
        Assert.Empty(HeadlessOptions.Parse(["--scenario=autopilot", "--account=test", "--journey=mage"]).Errors);
    }

    [Fact]
    public void Another_account_another_plan_or_a_bad_autopilot_option_is_a_loud_error()
    {
        var account = Assert.Single(HeadlessOptions.Parse(["--scenario=autopilot", "--account=test-melee"]).Errors);
        Assert.StartsWith("--account:", account, StringComparison.Ordinal);
        Assert.DoesNotContain("test-melee", account, StringComparison.Ordinal);
        Assert.Contains("runs the mage plan only", Assert.Single(HeadlessOptions.Parse(["--scenario=autopilot", "--journey=archer"]).Errors), StringComparison.Ordinal);
        Assert.Contains(HeadlessOptions.Parse(["--scenario=autopilot", "--autopilot-minutes=-4"]).Errors, e => e.StartsWith("--autopilot-minutes:", StringComparison.Ordinal));
    }

    [Fact]
    public void The_autopilot_options_belong_to_the_autopilot_scenario_alone()
    {
        var error = Assert.Single(HeadlessOptions.Parse(["--scenario=journey", "--journey=mage", "--autopilot-minutes=5"]).Errors);
        Assert.Contains("only --scenario=autopilot takes them", error, StringComparison.Ordinal);
        Assert.Contains("only --scenario=autopilot takes them", Assert.Single(HeadlessOptions.Parse(["--autopilot-test-death"]).Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void The_hosts_limits_are_lifted_the_stop_rules_end_the_run()
    {
        var none = HeadlessOptions.Parse(["--scenario=autopilot"]).Autopilot;
        Assert.Equal(HeadlessRunOptions.Unlimited, Program.AutopilotQuitAfterSeconds(none));
        Assert.True(double.IsPositiveInfinity(HeadlessRunOptions.Unlimited));

        var timed = HeadlessOptions.Parse(["--scenario=autopilot", "--autopilot-minutes=10"]).Autopilot;
        Assert.Equal(Program.AutopilotLoginMarginSeconds + 600 + Program.AutopilotSafePointSeconds, Program.AutopilotQuitAfterSeconds(timed));

        Assert.Equal(25.0, HeadlessOptions.Parse(["--scenario=autopilot", "--quit-after=25"]).QuitAfter); // --quit-after still wins in RunSession (options.QuitAfter ?? the scenario's)
    }

    [Fact]
    public void Help_lists_the_scenario_and_its_options()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, Program.Run(["--help"], output, error));
        var help = output.ToString();
        Assert.Contains("wayfind|journey|autopilot", help, StringComparison.Ordinal);
        Assert.Contains("--autopilot-minutes", help, StringComparison.Ordinal);
        Assert.Contains("--autopilot-trip-after-kills", help, StringComparison.Ordinal);
    }
}
