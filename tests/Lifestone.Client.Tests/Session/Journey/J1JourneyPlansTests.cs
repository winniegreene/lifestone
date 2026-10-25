using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>The three journeys as data (M4 P5b J1, design 3.4 with the lead's changes): their orders, kinds, budgets, grounds and expected seconds.</summary>
public sealed class J1JourneyPlansTests
{
    private static StationNeeds OwnNeeds(JourneyPlan plan, string station) =>
        JourneyStations.Create(plan, [], null, "acct").Create(plan.Stations.Single(s => s.Name == station)).Needs;

    [Fact]
    public void A_station_has_one_source_of_its_needs_the_plan_overrides_only_the_mage_harm_heal_place_and_the_hunt_grounds()
    {
        // M4 P5b F2-2: the station's own Needs are what the journey asserts and travels to; a plan keeps only what a station cannot know.
        var overrides = JourneyPlans.All.SelectMany(p => p.Stations.Where(s => s.NeedsOverride is not null).Select(s => $"{p.Name}/{s.Name}")).ToList();
        Assert.Equal(["mage/harm-heal", "melee/hunt", "archer/hunt"], overrides);

        // The hunts' overrides are the factory's own defaults (the plan's ground): no override warning for them; the mage's harm-heal is the one real override (Holtburg, not Here).
        foreach (var plan in JourneyPlans.All)
        {
            foreach (var spec in plan.Stations.Where(s => s.NeedsOverride is not null))
            {
                var own = OwnNeeds(plan, spec.Name);
                Assert.Equal(plan.Name == "mage", own != spec.NeedsOverride);
            }
        }

        Assert.Equal(StationWhere.Here, OwnNeeds(JourneyPlans.Melee, "harm-heal").Where);
        Assert.Equal(StationWhere.Holtburg, JourneyPlans.Mage.Stations.Single(s => s.Name == "harm-heal").NeedsOverride!.Where);
        Assert.Equal(60, OwnNeeds(JourneyPlans.Mage, "buff").MinManaPercent);
        Assert.Equal(50, OwnNeeds(JourneyPlans.Archer, "buff").MinManaPercent);
        Assert.Equal(StationWhere.Holtburg, OwnNeeds(JourneyPlans.Archer, "shortcuts").Where);
    }

    [Fact]
    public void The_mage_plan_is_pinned_in_order_with_no_expiry_and_one_teleport()
    {
        var p = JourneyPlans.Mage;
        Assert.Equal(("mage", "BSD Mage", "test", StationWhere.Den, 1), (p.Name, p.Character, p.Account, p.Ground, p.TeleportBudget));
        Assert.Equal(
            ["build-verify", "gear", "buff", "cast-facts", "kit-refused", "harm-heal", "bind", "den-travel", "den-fight", "death", "recall-home"],
            p.Stations.Select(s => s.Name));
        Assert.DoesNotContain(p.Stations, s => s.Name.Contains("expiry", StringComparison.Ordinal));
        Assert.Equal(StationWhere.Den, OwnNeeds(p, "den-fight").Where);
        Assert.Equal(StationWhere.Here, OwnNeeds(p, "den-travel").Where);
        Assert.Equal(1100, p.ExpectedSeconds);
    }

    [Fact]
    public void The_melee_plan_is_pinned_with_one_bind_and_the_hunt_on_the_belt()
    {
        var p = JourneyPlans.Melee;
        Assert.Equal(("melee", "Holtburg Brawler", "test-melee", StationWhere.Belt, 1), (p.Name, p.Character, p.Account, p.Ground, p.TeleportBudget));
        Assert.Equal(["build-verify", "buff", "bind", "hunt", "harm-heal", "recall-home", "vendor", "vendor-facts", "chests"], p.Stations.Select(s => s.Name));
        Assert.Single(p.Stations, s => s.Kind == StationKinds.Bind);
        Assert.Equal(StationWhere.Belt, p.Stations.Single(s => s.Name == "hunt").NeedsOverride!.Where);
        Assert.Equal(StationWhere.Holtburg, OwnNeeds(p, "vendor-facts").Where);
        Assert.Equal(990, p.ExpectedSeconds);
    }

    [Fact]
    public void The_archer_plan_is_pinned_with_the_hunt_in_the_forest()
    {
        var p = JourneyPlans.Archer;
        Assert.Equal(("archer", "Holtburg Archer", "test-archer", StationWhere.Forest, 1), (p.Name, p.Character, p.Account, p.Ground, p.TeleportBudget));
        Assert.Equal(
            ["build-verify", "buff", "bind", "hunt", "appraise-items", "harm-heal", "recall-home", "appraise-vendor", "vendor", "shortcuts"],
            p.Stations.Select(s => s.Name));
        Assert.Equal(StationWhere.Forest, p.Stations.Single(s => s.Name == "hunt").NeedsOverride!.Where);
        Assert.Equal(1000, p.ExpectedSeconds);
    }

    [Fact]
    public void Every_kind_is_in_the_lanes_table_names_are_unique_and_ForName_finds_each_plan()
    {
        Assert.Equal(18, StationKinds.All.Count);
        foreach (var plan in JourneyPlans.All)
        {
            Assert.All(plan.Stations, s => Assert.Contains(s.Kind, StationKinds.All));
            Assert.All(plan.Stations, s => Assert.True(s.ExpectedSeconds > 0, s.Name));
            Assert.Equal(plan.Stations.Count, plan.Stations.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count());
            Assert.Same(plan, JourneyPlans.ForName(plan.Name.ToUpperInvariant()));
            Assert.Equal(StationKinds.BuildVerify, plan.Stations[0].Kind);
            Assert.Empty(plan.Stations[0].Arguments);
        }

        Assert.Null(JourneyPlans.ForName("hero"));
        Assert.Null(JourneyPlans.ForName(null));
    }
}
