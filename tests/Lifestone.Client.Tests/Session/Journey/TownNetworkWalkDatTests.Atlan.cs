using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Data.Animation;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 8 B2b, on the retail dats: the walks of the Atlan wand's chain, the stone and Burun that slice B1's tests do not cover, from the files themselves: a body up and back down Silencia's tower by the file's own flights, and each go leg of the chains planned and confirmed (the Town Network trips as the fetch test plans them; the portals are taken as made; a go that names a road the travel file has no place for is a named TODO and is left out here).
/// </summary>
public sealed partial class TownNetworkWalkDatTests
{
    private const uint TowerFloor = 0x90D00100;

    /// <summary>The file's flights are the points B1's body walks (A_body_climbs_the_tower_to_silencias_roof_and_stands_within_reach_of_her_and_her_golem); the way down is the recall (the roof is an outdoor cell with no plan back into the tower).</summary>
    [Fact]
    public void The_atlan_wand_files_flights_are_the_ones_the_body_climbs()
    {
        var flights = QuestCatalog.Get("atlan-wand").Steps.Where(s => s.Kind == QuestStepKind.Go && s.Place!.Cell == TowerFloor).Select(s => s.Place!.Point!.Value);
        Assert.Equal(Flights.Select(f => new Vector3(f.X, f.Y, f.Z)), flights);
    }

    /// <summary>Each go leg a chain file walks, planned and confirmed from the leg before it. Left out and logged: the tower's flights (the body above), and a go to a place the travel file does not have (the file's named TODO).</summary>
    /// <param name="key">The quest file's key.</param>
    [RequiresDatsTheory]
    [InlineData("atlan-wand")]
    [InlineData("minor-smoldering-stone")]
    [InlineData("burun-history")]
    public void The_go_legs_of_a_chain_file_plan_and_confirm(string key)
    {
        var quest = QuestCatalog.Get(key);
        var start = new CellFrame(quest.Start.Cell, new AcFrame(quest.Start.Point!.Value, Quaternion.Identity));
        using var rig = Rig(start);
        var at = start;
        var failures = new List<string>();
        foreach (var step in quest.Steps)
        {
            switch (step.Kind)
            {
                case QuestStepKind.HuntUntil:
                    var arrival = GroundCatalog.Get(step.Ground!).Arrival;
                    at = new CellFrame(arrival.Cell, new AcFrame(arrival.Origin, Quaternion.Identity)); // the fight ends somewhere in the ground's rooms: the arrival stands for it
                    break;
                case QuestStepKind.Use or QuestStepKind.Recall:
                    at = new CellFrame(StandingCell(step.Place!.Cell), new AcFrame(step.Place.Point!.Value, Quaternion.Identity));
                    break;
                case QuestStepKind.Go:
                    var place = step.Place!;
                    var point = place.Point!.Value;
                    if (place.Cell == TowerFloor)
                    {
                        at = new CellFrame(place.Cell, new AcFrame(point, Quaternion.Identity));
                        break;
                    }

                    if (!SameTravelPlace(at.CellId, place.Cell))
                    {
                        if (Routes.PlaceOf(RungRoute.Hex(place.Cell >> 16)) is null)
                        {
                            output.WriteLine($"quest-walk name={key} leg={step.Index} skipped: {step.Todo}");
                            Assert.NotNull(step.Todo);
                            at = new CellFrame(PortalCell(place.Cell, point), new AcFrame(point, Quaternion.Identity));
                            break;
                        }

                        var plan = ChainTrip(at.CellId, place, quest.Ladder!.FromLevel);
                        output.WriteLine($"quest-walk name={key} leg={step.Index} trip={plan.HopKeys} drop=0x{plan.DropCell:X8}");
                        at = plan.Hops[^1].WalkOut is { Count: > 0 } walkOut // the hop walks out of the arrival building itself (B1)
                            ? new CellFrame(PortalCell(place.Cell, walkOut[^1].Origin), new AcFrame(walkOut[^1].Origin, Quaternion.Identity))
                            : new CellFrame(plan.DropCell, new AcFrame(plan.DropOrigin, Quaternion.Identity));
                    }

                    var walk = Walk(rig, at, rig.PointGoal(PortalCell(place.Cell, point), point));
                    output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"quest-walk name={key} leg={step.Index} ({step.What}) metres={walk.Metres:0.0} pass={(walk.Ok ? "yes" : "no")} {walk.Why}"));
                    if (walk.Ok)
                    {
                        at = walk.End;
                    }
                    else
                    {
                        failures.Add($"step {step.Index} ({step.What}): {walk.Why}");
                        at = new CellFrame(PortalCell(place.Cell, point), new AcFrame(point, Quaternion.Identity));
                    }

                    break;
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>The trip the station plans for a go at the band's first level, Lifestone Recall in the book (the Yinar place has no way home but the recall, B1).</summary>
    private static TownTripPlan ChainTrip(uint from, QuestPlace place, int level)
    {
        var town = Routes.PlaceOf(RungRoute.Hex(place.Cell >> 16))!.Name;
        var candidate = new TownTripCandidate(town, place.Name, 0, new ScrivenerPlace(town, 0, place.Cell, place.Cell, place.Point!.Value));
        var input = new TownTripInput(TownNetworkTrip.PlaceKeyOf(from >> 16, Routes), level, KnowsLifestoneRecall: true, HomeIsTravelStart: true);
        var verdict = TownNetworkTrip.Judge(candidate, input, Routes, LeverDoors.Default);
        Assert.True(verdict.Plan is not null, verdict.Why);
        return verdict.Plan!;
    }

    /// <summary>The cell a body stands in at a portal's arrival: the Crater Caves' stored Destination cell is stale (M6 8 B1, handoff: 0x019A02CA, the point stands in 0x019A02D6).</summary>
    private static uint StandingCell(uint arrival) => arrival == 0x019A02CA ? 0x019A02D6 : arrival;
}
