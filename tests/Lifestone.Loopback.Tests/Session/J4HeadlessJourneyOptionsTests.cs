using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Headless;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// <c>--scenario=journey --journey=mage|melee|archer</c> (M4 P5b J4; a plain unit test in this project, as <see cref="HeadlessFighterOptionsTests"/>): the plan, the account rule
/// (each journey on its own test account; without <c>--account</c> the journey's), the refusals, and the default limit (the plan's sum plus the login margin, <c>--quit-after</c> wins).
/// </summary>
public sealed class J4HeadlessJourneyOptionsTests
{
    [Theory]
    [InlineData("mage", "test", "BSD Mage")]
    [InlineData("melee", "test-melee", "Holtburg Brawler")]
    [InlineData("archer", "test-archer", "Holtburg Archer")]
    public void Each_journey_runs_on_its_own_account_given_or_not(string journey, string account, string character)
    {
        var bare = HeadlessOptions.Parse(["--scenario=journey", "--journey=" + journey]);
        Assert.Empty(bare.Errors);
        Assert.Equal(HeadlessScenario.Journey, bare.Scenario);
        Assert.Equal(journey, bare.Journey!.Name);
        Assert.Equal(character, bare.Journey.Character);
        Assert.Equal(account, bare.Session.Account);

        var given = HeadlessOptions.Parse(["--scenario=journey", "--journey=" + journey, "--account=" + account, "--hunt-kills=2", "--vitals-infight-potion=off"]);
        Assert.Empty(given.Errors);
        Assert.Same(bare.Journey, given.Journey);
        Assert.Contains("--hunt-kills=2", given.Arguments); // the stations read their scenario options from the command line as given
    }

    [Theory]
    [InlineData("mage", "test-melee")]
    [InlineData("melee", "test")]
    [InlineData("archer", "test-melee")]
    [InlineData("melee", "somebody")]
    public void A_journey_on_another_account_is_a_loud_error_naming_no_account_value(string journey, string account)
    {
        var error = Assert.Single(HeadlessOptions.Parse(["--scenario=journey", "--journey=" + journey, "--account=" + account]).Errors);
        Assert.StartsWith("--account:", error, StringComparison.Ordinal);
        Assert.DoesNotContain("somebody", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_or_unknown_journey_or_a_journey_on_another_scenario_is_an_error()
    {
        Assert.StartsWith("--journey: --scenario=journey needs", Assert.Single(HeadlessOptions.Parse(["--scenario=journey"]).Errors), StringComparison.Ordinal);
        Assert.Contains("is not one of mage, melee, archer", Assert.Single(HeadlessOptions.Parse(["--scenario=journey", "--journey=hero"]).Errors), StringComparison.Ordinal);
        Assert.StartsWith("--journey: only --scenario=journey", Assert.Single(HeadlessOptions.Parse(["--scenario=gear", "--journey=mage"]).Errors), StringComparison.Ordinal);
        Assert.Null(HeadlessOptions.Parse(["--scenario=gear", "--journey=mage"]).Journey);
    }

    [Fact]
    public void The_default_limit_is_the_plans_sum_plus_the_login_margin()
    {
        foreach (var plan in JourneyPlans.All)
        {
            Assert.Equal(plan.ExpectedSeconds + Program.JourneyLoginMarginSeconds, Program.JourneyQuitAfterSeconds(plan));
        }

        Assert.Equal(1_290, Program.JourneyQuitAfterSeconds(JourneyPlans.Melee));
        Assert.Equal(400, HeadlessOptions.Parse(["--scenario=journey", "--journey=melee", "--quit-after=400"]).QuitAfter);
    }
}
