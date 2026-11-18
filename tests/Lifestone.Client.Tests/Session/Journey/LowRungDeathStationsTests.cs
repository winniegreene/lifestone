using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 7 P (live round 1c of <c>LowRungDeathLiveTests</c>: <c>journey station den-fight: skipped=precondition:where</c>, <c>stations=2/5</c>): the case's station list, <see cref="LowRungStations.Outskirts"/> (the list the live case runs), over the journey's scripted host.
/// The den-fight needs <see cref="StationWhere.Den"/>, which the journey meets only through the den-travel station (an outdoor ground: a walk from Holtburg to its first cell), so the fight needs a den-travel before it, and again after the death (the respawn is Holtburg's lifestone).
/// </summary>
public sealed class LowRungDeathStationsTests
{
    private const string Outskirts = "holtburg-outskirts";

    /// <summary>The stations the case's list makes, with the real stations' needs and scripted runs: den-travel puts the player at the ground's arrival (the walk itself is proved by M6 7 H and the F3 tests), the death puts it at the lifestone with the drop rule's token.</summary>
    private sealed class Factory(J1JourneyRig rig, HuntingGround ground, List<string> started, List<string> arrivals, string deathFields) : IStationFactory
    {
        public IStation Create(StationSpec spec)
        {
            var plan = new JourneyPlan("low-rung-death", "Lifestone Throwaway", "test-tools", StationWhere.Here, 0, []) { CheatFree = true };
            var real = JourneyStations.Create(plan, [], null, null, ground).Create(spec);
            var script = spec.Kind switch
            {
                StationKinds.DenTravel => new J1StationScript(Fields: "den=walk", OnEnd: _ =>
                {
                    var to = ground.ArrivalDestination().Location.Position;
                    rig.TeleportNow(to.CellId, to.Local);
                    arrivals.Add(spec.Name);
                }),
                StationKinds.Death => new J1StationScript(Fields: deathFields, OnEnd: _ => rig.TeleportNow(J1JourneyRig.HoltburgCell)),
                _ => new J1StationScript(Fields: spec.Kind == StationKinds.DenFight ? "kills=1" : string.Empty),
            };
            // The buffed need reads an active enchantment, which the scripted buff station casts none of (the live run proved buffs=1/1): it is the one need not carried over; the where, health and mana needs are the real station's.
            return new J1Station(spec.Name, (spec.NeedsOverride ?? real.Needs) with { Buffed = false }, script, started);
        }
    }

    [Fact]
    public void M6_7_P_the_outskirts_case_walks_to_the_ground_before_each_fight_dies_and_climbs_again()
    {
        var ground = GroundCatalog.Get(Outskirts);
        Assert.Equal(GroundKind.Outdoor, ground.Kind);
        var stations = LowRungStations.Outskirts(ground);
        Assert.Equal(["bind", "buff", "den-travel", "den-fight", "death", "den-travel", "den-fight"], stations.Select(s => s.Name));

        // The drop rule at the case's level (6, 7 after the refill): nothing drops below level 11, the retained line, the caster kept.
        var drop = DeathDropRule.Decide(new DeathDropInput(7, RetainedLine: true, LostLine: null, LinesRead: true, WornBefore: 1, WornMissing: 0, CasterKept: true));
        Assert.True(drop.NoneNeeded);
        var deathFields = $"deaths=1 route=walk corpse_recovered={(drop.NoneNeeded ? DeathCorpse.NoneNeeded : "yes")} vitae_grants=0 rebuffed=yes rearm=none";

        var rig = new J1JourneyRig(); // Holtburg, in peace
        var started = new List<string>();
        var arrivals = new List<string>();
        var plan = new JourneyPlan("low-rung-death", "Lifestone Throwaway", "test-tools", StationWhere.Here, 0, stations) { CheatFree = true };
        var context = rig.Context("Lifestone Throwaway", budget: 0);
        context = context.WithDungeon(ground);
        var journey = rig.Journey(plan, new Factory(rig, ground, started, arrivals, deathFields), context);

        var status = rig.Run(journey);
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(Environment.NewLine, rig.Log.Lines.Where(l => l.Contains("journey", StringComparison.Ordinal))));

        Assert.Equal(["bind", "buff", "den-travel", "den-fight", "death", "den-travel", "den-fight"], started);
        Assert.Equal(2, arrivals.Count);
        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("precondition:where", StringComparison.Ordinal));
        Assert.All(journey.Verdict.Stations, r => Assert.Null(r.Skipped));
        var death = Assert.Single(journey.Verdict.Stations, r => r.Name == "death");
        Assert.Contains("corpse_recovered=none-needed", death.Fields, StringComparison.Ordinal);
        var fights = journey.Verdict.Stations.Where(r => r.Name == "den-fight").ToArray();
        Assert.Equal(2, fights.Length);
        Assert.Contains("kills=1", fights[^1].Fields, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_7_P_a_list_without_the_den_travel_skips_the_fight_on_where_as_live_round_1c_did()
    {
        var ground = GroundCatalog.Get(Outskirts);
        var stations = LowRungStations.Outskirts(ground).Where(s => s.Kind != StationKinds.DenTravel).ToArray();
        var rig = new J1JourneyRig();
        var plan = new JourneyPlan("low-rung-death", "Lifestone Throwaway", "test-tools", StationWhere.Here, 0, stations) { CheatFree = true };
        var journey = rig.Journey(plan, new Factory(rig, ground, [], [], "deaths=1"), rig.Context("Lifestone Throwaway", budget: 0).WithDungeon(ground));

        rig.Run(journey);

        Assert.Contains(journey.Verdict.Stations, r => r.Name == "den-fight" && r.Skipped == "precondition:where");
    }
}
