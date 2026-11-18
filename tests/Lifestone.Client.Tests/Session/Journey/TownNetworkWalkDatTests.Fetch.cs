using System.Globalization;
using System.Numerics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
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
/// M6 8 B2a (D118), on the retail dats: a low-level fetch quest walked from its own file, end to end (the Four Stones are pinned off the ladder below, by name; M6 8 B2c puts Nen Ai and the Stolen Goods on it). The walk starts at the quest's start (Holtburg's lifestone point); a <c>go</c> to another place is the Town Network trip the station plans (<see cref="TownNetworkTrip.Judge"/> for a level-5 mage without Lifestone Recall; its hops are the edge theory's), a <c>go</c> in the same place is planned and
/// confirmed by the physics to the step's point, and a <c>use</c> with an arrival is the seek to the portal within its 2 m and then the portal's arrival. One line a leg (<c>quest-walk name= leg= metres= pass=</c>). The ways out are the files' own last legs; the doors on the way are unlocked in the world (the facts' doors rows) and crossed by the runner's Use, as the Facility Hub's are.
/// </summary>
public sealed partial class TownNetworkWalkDatTests
{
    /// <summary>The level the trips are judged at: the lowest of the three bands, where a portal's cap cannot refuse and Lifestone Recall is not in the book.</summary>
    private const int FetchLevel = 5;

    /// <summary>A quest file on the ladder, walked whole.</summary>
    /// <param name="key">The quest file's key.</param>
    [RequiresDatsTheory]
    [InlineData("worcer-heirlooms")]
    [InlineData("nen-ai-pet-drudge")]
    [InlineData("lubziklan-stolen-goods")]
    public void A_fetch_quest_walks_end_to_end_from_its_own_file(string key)
    {
        var quest = QuestCatalog.Get(key);
        var start = new CellFrame(quest.Start.Cell, new AcFrame(quest.Start.Point!.Value, Quaternion.Identity));
        using var rig = Rig(start);
        var at = start;
        var failures = new List<string>();
        foreach (var step in quest.Steps)
        {
            if (step.Kind == QuestStepKind.Go)
            {
                var place = step.Place!;
                if (!SameTravelPlace(at.CellId, place.Cell))
                {
                    var plan = FetchTrip(at.CellId, place);
                    output.WriteLine($"quest-walk name={key} leg={step.Index} trip={plan.HopKeys} drop=0x{plan.DropCell:X8}");
                    at = new CellFrame(plan.DropCell, new AcFrame(plan.DropOrigin, Quaternion.Identity));
                }

                var cell = PortalCell(place.Cell, place.Point!.Value);
                at = rig.RealRoom(at);
                var walk = step.Way is { } way ? AuthoredWalk(rig, at, way, cell, place.Point.Value) : Walk(rig, at, rig.PointGoal(cell, place.Point.Value));
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"quest-walk name={key} leg={step.Index} ({step.What}) metres={walk.Metres:0.0} pass={(walk.Ok ? "yes" : "no")} {walk.Why}"));
                if (!walk.Ok)
                {
                    failures.Add($"step {step.Index} ({step.What}): {walk.Why}");
                    at = new CellFrame(cell, new AcFrame(place.Point.Value, Quaternion.Identity));
                }
                else
                {
                    at = rig.RealRoom(walk.End);
                }
            }
            else if (step.Kind == QuestStepKind.Use)
            {
                var edge = Routes.Edges.Single(e => e.Wcid == step.Target!.Wcid && e.Cell >> 16 == at.CellId >> 16);
                var seek = Walk(rig, at, rig.ObjectGoal(edge.Guid, PortalCell(edge.Cell, edge.Origin), edge.Origin, DenTravelStation.PortalRangeMetres));
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"quest-walk name={key} leg={step.Index} ({step.What}) seek metres={seek.Metres:0.0} pass={(seek.Ok ? "yes" : "no")} {seek.Why}"));
                if (!seek.Ok)
                {
                    failures.Add($"step {step.Index} ({step.What}): the seek: {seek.Why}");
                }

                var arrival = step.Place!;
                Assert.Equal((edge.DestCell, edge.DestOrigin), (arrival.Cell, arrival.Point!.Value));
                at = rig.RealRoom(new CellFrame(arrival.Cell, new AcFrame(arrival.Point.Value, Quaternion.Identity)));
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.Equal(quest.Start.Cell >> 16, at.CellId >> 16); // she ends at Holtburg
    }

    /// <summary>
    /// <c>TODO(m6-fetch-hive-drop)</c> and the Hive's portal, pinned: the Four Stones are off the ladder because the file does not walk. From the arrival Compassion plans and the three stones below it are in rooms the plan cannot join (the Hive's levels are one-way drops the room graph refuses as pit edges); the chest does reach the bottom Surface portal. From the street, the Hive portal's building is a stair round a floorless shaft
    /// (0xD8560104, the stair 0xD8560103 of 12 floor polygons, the shaft 0xD8560102, then 0xD8560101 and the portal's cell), so a go step plans nothing; a go step has no authored way (the hop's <c>walk_in</c> does). A pin fails when the walk reaches, so a fix removes it and puts the quest back.
    /// </summary>
    [RequiresDatsFact]
    public void The_four_stones_are_pinned_off_the_ladder_by_the_hives_drops_and_its_portal_stair()
    {
        Assert.Null(QuestCatalog.Get("four-stones-of-jojii").Ladder);
        var arrival = new CellFrame(0x015302E8, new AcFrame(new Vector3(45.7f, -43f, -18f), Quaternion.Identity));
        using var rig = Rig(arrival);
        var compassion = Walk(rig, arrival, rig.PointGoal(0x01530330, new Vector3(1.72869f, -28.0476f, 0.082f)));
        Assert.True(compassion.Ok, compassion.Why);
        foreach (var (name, cell, at) in new[] { ("humility", 0x015301ABu, new Vector3(4.01376f, -29.4326f, -29.918f)), ("discipline", 0x01530171u, new Vector3(12.7544f, -93.1133f, -41.918f)), ("the chest", 0x01530109u, new Vector3(28.022f, -133.253f, -65.961f)) })
        {
            var walk = Walk(rig, arrival, rig.PointGoal(cell, at));
            output.WriteLine($"quest-walk name=hive-{name} pass={(walk.Ok ? "yes" : "no")} {walk.Why}");
            Assert.False(walk.Ok, $"{name} reached from the arrival, so TODO(m6-fetch-hive-drop) is stale");
            Assert.Contains("indoor-start-unreachable", walk.Why, StringComparison.Ordinal);
        }

        var below = new CellFrame(0x01530109, new AcFrame(new Vector3(28.022f, -133.253f, -65.961f), Quaternion.Identity));
        var exit = Walk(rig, below, rig.PointGoal(0x0153010C, new Vector3(30.13f, -153.715f, -66f)));
        Assert.True(exit.Ok, exit.Why);
        AssertStairPinned(new CellFrame(0xD8560010, new AcFrame(new Vector3(35.6f, 189.2f, 30f), Quaternion.Identity)), 0xD8560100, new Vector3(36.382f, 161.18f, 24.805f), "the Hive portal");
    }

    /// <summary>
    /// M6 8 B2c: the walks the plan does not make, as a body walks them from the file's own points. The Stone Cathedral portal's house: a straight stair round the floorless shaft 0xD8540100, in the door hall's own room name (a flight), then the portal's room. Nen Ai's tower: two flights up round the shaft 0xD8530102, and down again. Replaces <c>Nen_ais_pet_drudge_is_pinned_off_the_ladder_by_the_cathedral_ramp_and_the_tower_stairs</c>: those walks now reach.
    /// </summary>
    [RequiresDatsFact]
    public void A_body_walks_the_cathedral_portals_stair_and_nen_ais_tower_flight_by_flight()
    {
        var q = QuestCatalog.Get("nen-ai-pet-drudge");
        WalkFlights(q, q.Steps[0].Place!, 0, 1, "the Cathedral portal's stair");
        var up = q.Steps.First(s => s.Way is not null && s.Index > 1).Index; // step 1, the Cathedral stair, also carries a way (M6 8 M)
        WalkFlights(q, q.Steps[up - 2].Place!, up - 2, up - 1, "the ramp below Nen Ai's landing", rungsAllowed: true); // an ordinary planned walk that stops on a steep ramp: the runner's own recovery may fire, the arrival is what counts
        foreach (var step in q.Steps.Where(s => s.Way is not null))
        {
            var from = q.Steps.Take(step.Index).Last(s2 => s2.Place is not null).Place!;
            var start = new CellFrame(from.Cell, new AcFrame(from.Point!.Value, Quaternion.Identity));
            using var rig = new FacilityHubWalkDatTests.Rig(Dats, start);
            var frames = step.Way!.Select(p => new CellFrame(p.Cell, new AcFrame(p.Point!.Value, Quaternion.Identity))).ToList();
            var end = rig.Run(rig.PointGoal(frames[^1].CellId, frames[^1].Origin), step.What, way: new AuthoredWaySource(frames));
            output.WriteLine(end.Line);
            Assert.True(end.Arrived, end.Line);
            Assert.Equal(0, end.Rungs);
        }
    }

    /// <summary>M6 8 (the long round): from the foot of the stair, in the portal's room where the live body stood, a body reaches the point short of the Stone Cathedral portal. The portal's own front lies past it, and the walk there touched the portal.</summary>
    [RequiresDatsFact]
    public void A_body_walks_from_the_cathedral_stairs_foot_to_short_of_the_portal()
    {
        var q = QuestCatalog.Get("nen-ai-pet-drudge");
        var use = q.Steps.First(s => s.Kind == QuestStepKind.Use && s.Target?.Wcid == 4955);
        var go = q.Steps[use.Index - 1].Place!;
        var foot = q.Steps[use.Index - 2].Place!;
        var start = new CellFrame(0xD8540102, new AcFrame(foot.Point!.Value, Quaternion.Identity));
        using var rig = new FacilityHubWalkDatTests.Rig(Dats, start);
        var end = rig.Run(rig.PointGoal(go.Cell, go.Point!.Value), "short of the portal");
        output.WriteLine(end.Line);
        Assert.True(end.Arrived, end.Line);
    }

    /// <summary>M6 8 B2c: the Sea Temple's door and ramp, the authored way of the Stolen Goods' go step: a body from the door's front round the aisle and up the ramp, then the seek to the portal within 2 m.</summary>
    [RequiresDatsFact]
    public void A_body_walks_the_sea_temples_way_in_to_its_portal()
    {
        var q = QuestCatalog.Get("lubziklan-stolen-goods");
        var door = q.Steps[0].Place!;
        var step = q.Steps[1];
        var start = new CellFrame(door.Cell, new AcFrame(door.Point!.Value, Quaternion.Identity));
        using var rig = new FacilityHubWalkDatTests.Rig(Dats, start);
        var frames = step.Way!.Select(p => new CellFrame(p.Cell, new AcFrame(p.Point!.Value, Quaternion.Identity))).ToList();
        var end = rig.Run(rig.PointGoal(frames[^1].CellId, frames[^1].Origin), "sea-temple", way: new AuthoredWaySource(frames));
        output.WriteLine(end.Line);
        Assert.True(end.Arrived, end.Line);
        var edge = Routes.Edge("yaraq-to-sea-temple");
        var seek = rig.Run(rig.SeekGoal(RungPortal.Of(edge, Routes)), "seek");
        output.WriteLine(seek.Line);
        Assert.True(seek.Arrived, seek.Line);
    }

    private void WalkFlights(Quest q, QuestPlace first, int from, int to, string name, bool rungsAllowed = false)
    {
        var start = new CellFrame(first.Cell, new AcFrame(first.Point!.Value, Quaternion.Identity));
        using var rig = new FacilityHubWalkDatTests.Rig(Dats, start);
        for (var i = from + 1; i <= to; i++)
        {
            var place = q.Steps[i].Place!;
            var end = rig.Run(rig.PointGoal(place.Cell, place.Point!.Value), $"{name}: {q.Steps[i].What}");
            output.WriteLine(end.Line);
            Assert.True(end.Arrived, end.Line);
            Assert.True(rungsAllowed || end.Rungs == 0, end.Line);
        }
    }

    /// <summary>A go step's authored way: the plan the source hands the runner, confirmed by the physics (the runner's own check), from where the body stands; one straight plan, as <see cref="AuthoredWaySource"/> makes it.</summary>
    private static WalkResult AuthoredWalk(StreamRig rig, CellFrame at, IReadOnlyList<QuestPlace> way, uint cell, Vector3 point)
    {
        var goal = rig.PointGoal(cell, point);
        var frames = way.Select(p => new CellFrame(p.Cell, new AcFrame(p.Point!.Value, Quaternion.Identity))).ToList();
        var path = new AuthoredWaySource(frames).Plan(at, goal, new NavScars());
        if (rig.Confirm(path, goal) != RouteConfirmStatus.Confirmed)
        {
            return new WalkResult(false, 0, at, $"the authored way was refused: {rig.Confirmer.Failure}");
        }

        return new WalkResult(true, Length(path), path.Waypoints[^1].At, null);
    }

    private void AssertStairPinned(CellFrame from, uint cell, Vector3 at, string name)
    {
        using var rig = Rig(from);
        var walk = Walk(rig, from, rig.PointGoal(cell, at));
        output.WriteLine($"quest-walk name=stair-{name} pass={(walk.Ok ? "yes" : "no")} {walk.Why}");
        Assert.False(walk.Ok, $"{name} reached from {from}, so TODO(m6-fetch-tower-stairs) is stale");
        Assert.Contains("no-doorway-lane-reaches-the-goal", walk.Why, StringComparison.Ordinal);
    }

    private static bool SameTravelPlace(uint a, uint b)
    {
        var (from, to) = (TownNetworkTrip.PlaceKeyOf(a >> 16, Routes), TownNetworkTrip.PlaceKeyOf(b >> 16, Routes));
        return a >> 16 == b >> 16 || (from is not null && from == to);
    }

    /// <summary>The trip the station plans for a go to another place.</summary>
    private static TownTripPlan FetchTrip(uint from, QuestPlace place)
    {
        var town = Routes.PlaceOf(RungRoute.Hex(place.Cell >> 16))!.Name;
        var candidate = new TownTripCandidate(town, place.Name, 0, new ScrivenerPlace(town, 0, place.Cell, place.Cell, place.Point!.Value));
        var input = new TownTripInput(TownNetworkTrip.PlaceKeyOf(from >> 16, Routes), FetchLevel, KnowsLifestoneRecall: false, HomeIsTravelStart: true);
        var verdict = TownNetworkTrip.Judge(candidate, input, Routes, LeverDoors.Default);
        Assert.True(verdict.Plan is not null, verdict.Why);
        return verdict.Plan!;
    }
}
