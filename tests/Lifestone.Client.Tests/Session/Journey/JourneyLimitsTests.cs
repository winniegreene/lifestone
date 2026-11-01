using Lifestone.Client.Options;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M5 1 fix B (review 1-R4): the journey's limits cover the den fight's own limit. The station limit, and the whole-run extra both hosts add, are raised by <c>--fight-minutes</c> and a long
/// <c>--fight-seconds</c> and by nothing else, so the default journeys' limits are the ones the gates were sized with.
/// </summary>
public sealed class JourneyLimitsTests
{
    private static readonly string[] Plain = [];

    private static StationSpec DenFightOf(JourneyPlan plan) => plan.Stations.First(s => s.Kind == StationKinds.DenFight);

    [Fact]
    public void Without_fight_options_every_station_limit_and_the_whole_run_extra_are_the_ones_the_gates_were_sized_with()
    {
        foreach (var plan in new[] { JourneyPlans.Mage, JourneyPlans.Melee, JourneyPlans.Archer, JourneyPlans.HeroMage })
        {
            var factory = JourneyStations.Create(plan, Plain, null);
            foreach (var spec in plan.Stations)
            {
                Assert.Equal((spec.ExpectedSeconds * 3) + 300, factory.StationLimitSeconds(spec));
                Assert.Equal(JourneyLimits.DefaultStationSeconds(spec), JourneyLimits.StationSeconds(spec, new FightOptions()));
            }

            Assert.Equal(JourneyLimits.HuntExtraSeconds(plan), JourneyLimits.WholeExtraSeconds(plan, new FightOptions())); // nothing but the archer hunt's own (k2)
        }

        Assert.Equal(945, JourneyLimits.StationSeconds(DenFightOf(JourneyPlans.Mage), new FightOptions())); // 215 x 3 + 300 since k2 (150 x 3 + 300 = 750 before the tour of two storeys)
    }

    [Theory]
    [InlineData("--fight-minutes=30", "--fight-kills=0", 2100)] // the clean 30-minute fight: 1,800 s plus the 300 s margin
    [InlineData("--fight-seconds=1200", "--fight-kills=3", 1500)] // a long failure limit alone
    [InlineData("--fight-minutes=1440", "--fight-kills=0", 86_700)] // the spec's day
    [InlineData("--fight-minutes=1", "--fight-kills=2", 945)] // under the default 300 s failure limit: nothing is raised
    [InlineData("--fight-seconds=300", "--fight-kills=2", 945)]
    public void A_raised_fight_limit_raises_the_den_fight_stations_limit_to_the_fight_limit_plus_the_margin(string first, string second, double expected)
    {
        var plan = JourneyPlans.Mage;
        var factory = JourneyStations.Create(plan, [first, second], null);
        var den = DenFightOf(plan);

        Assert.Equal(expected, factory.StationLimitSeconds(den));
    }

    [Fact]
    public void A_raised_fight_limit_raises_no_other_stations_limit()
    {
        var plan = JourneyPlans.Mage;
        var factory = JourneyStations.Create(plan, ["--fight-minutes=30", "--fight-kills=0"], null);

        foreach (var spec in plan.Stations.Where(s => s.Kind != StationKinds.DenFight))
        {
            Assert.Equal((spec.ExpectedSeconds * 3) + 300, factory.StationLimitSeconds(spec));
        }
    }

    [Fact]
    public void The_whole_run_extra_is_the_raise_over_the_default_fight_for_each_den_fight_and_nothing_for_a_plan_without_one()
    {
        var thirty = JourneyLimits.FightOf(["--fight-minutes=30", "--fight-kills=0"]);

        Assert.Equal(1800 - FightOptions.DefaultSeconds, JourneyLimits.WholeExtraSeconds(JourneyPlans.Mage, thirty));
        Assert.Equal(1800 - FightOptions.DefaultSeconds, JourneyLimits.WholeExtraSeconds(JourneyPlans.HeroMage, thirty));
        Assert.Equal(1200 - FightOptions.DefaultSeconds, JourneyLimits.WholeExtraSeconds(JourneyPlans.Mage, JourneyLimits.FightOf(["--fight-seconds=1200"])));
        Assert.Equal(0, JourneyLimits.WholeExtraSeconds(JourneyPlans.Mage, JourneyLimits.FightOf(["--fight-minutes=1"])));
        Assert.Equal(0, JourneyLimits.WholeExtraSeconds(JourneyPlans.Melee, thirty));
        Assert.Equal(JourneyLimits.HuntExtraSeconds(JourneyPlans.Archer), JourneyLimits.WholeExtraSeconds(JourneyPlans.Archer, thirty)); // a fight raise adds nothing to a plan with no den fight but the archer hunt's
    }

    [Fact]
    public void The_journey_ends_a_den_fight_over_its_default_limit_but_lets_one_the_options_raised_run_on()
    {
        var plan = new JourneyPlan("limits", "BSD Mage", "test", StationWhere.Belt, 1, [new StationSpec("den-fight", StationKinds.DenFight, StationNeeds.None, 30)]);

        // 2,000 steps at 0.25 s a frame: 500 s, over the default 390 s (30 x 3 + 300) and under a 30-minute fight's.
        var scripts = new Dictionary<string, J1StationScript> { ["den-fight"] = new(Steps: 2000) };

        var plain = new J1JourneyRig();
        var plainJourney = plain.Journey(plan, new LimitFactory(new J1Factory(scripts), JourneyLimits.FightOf(Plain)), plain.Context());
        Assert.Equal(ScenarioStatus.Failed, plain.Run(plainJourney));
        Assert.Contains("timeout:running_(over_390_s)", plainJourney.Lines[0], StringComparison.Ordinal);

        var raised = new J1JourneyRig();
        var raisedJourney = raised.Journey(plan, new LimitFactory(new J1Factory(scripts), JourneyLimits.FightOf(["--fight-minutes=30", "--fight-kills=0"])), raised.Context());
        Assert.Equal(ScenarioStatus.Succeeded, raised.Run(raisedJourney));
        Assert.DoesNotContain("timeout", raisedJourney.Lines[0], StringComparison.Ordinal);
    }

    /// <summary>A scripted factory that answers the station limit as the catalog does (from the command line's fight options).</summary>
    private sealed class LimitFactory(J1Factory inner, FightOptions fight) : IStationFactory
    {
        public IStation Create(StationSpec spec) => inner.Create(spec);

        public double StationLimitSeconds(StationSpec spec) => JourneyLimits.StationSeconds(spec, fight);
    }
}
