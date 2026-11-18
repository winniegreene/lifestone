using System.Globalization;
using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Data.Animation;
using Lifestone.Data.Tests;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 8 M, on the retail dats: the way home from inside a building. Live (round 2, level 8), the climber stood in the Cathedral portal's room 0xD8540102, whose only way out is a stair round a floorless shaft; the walk to the front of Shoushi's portal failed <c>indoor-start-unreachable</c> and ended the climb. Each place an errand's go or use can leave her,
/// the first walk of the way home plans (out of the building by the travel file's <c>ways_out</c> when the room graph cannot) and the physics confirms it; a body walks the Cathedral house's own.
/// </summary>
public sealed partial class TownNetworkWalkDatTests
{
    /// <summary>Every place of an errand on the ladder: the first walk of its way home.</summary>
    /// <param name="key">The quest file's key.</param>
    [RequiresDatsTheory]
    [InlineData("worcer-heirlooms")]
    [InlineData("nen-ai-pet-drudge")]
    [InlineData("lubziklan-stolen-goods")]
    public void From_every_place_of_an_errand_the_first_walk_home_plans_out_of_the_building(string key)
    {
        var failures = new List<string>();
        foreach (var step in QuestCatalog.Get(key).Steps.Where(s => s.Kind is QuestStepKind.Go or QuestStepKind.Use && s.Place?.Point is not null))
        {
            var place = TownNetworkTrip.PlaceKeyOf(step.Place!.Cell >> 16, Routes);
            if (place is null or "holtburg")
            {
                continue; // at home: no way home to walk
            }

            var way = Routes.WayHomeFrom(place, FetchLevel);
            Assert.True(way.WayHome is not null, $"{step.What}: {way.Stop}");
            var edge = way.WayHome!.Steps[0].Edge;
            var start = new CellFrame(PortalCell(step.Place.Cell, step.Place.Point!.Value), new AcFrame(step.Place.Point!.Value, Quaternion.Identity));
            using var rig = Rig(start);
            var from = rig.RealRoom(start);
            var walk = FirstWalkHome(rig, edge, from);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"way-home name={key} leg={step.Index} ({step.What}) from=0x{from.CellId:X8} edge={edge.Key} metres={walk.Metres:0.0} pass={(walk.Ok ? "yes" : "no")} {walk.Why}"));
            if (NoWalkHomeYet.Contains((key, step.Index)))
            {
                Assert.False(walk.Ok, $"step {step.Index} walks home now: TODO(m6-cathedral-pile-way-home) is stale, remove the pin");
                Assert.Contains("indoor-start-unreachable", walk.Why, StringComparison.Ordinal); // says where (its cell) and why
                continue;
            }

            if (!walk.Ok)
            {
                failures.Add($"step {step.Index} ({step.What}) from 0x{from.CellId:X8}: {walk.Why}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static WalkResult FirstWalkHome(StreamRig rig, TravelEdge edge, CellFrame from)
    {
        if (edge.WalkIn is { Count: > 0 } walkIn)
        {
            return Walk(rig, from, rig.PointGoal(walkIn[0].Cell, walkIn[0].Origin));
        }

        var portal = RungPortal.Of(edge, Routes);
        var portalCell = PortalCell(edge.Cell, edge.Origin);
        var (frontCell, front, frontBasis) = TravelLeg.FirstFront(portal, portalCell, from);
        if (frontBasis == TravelLeg.FrontBasisFacing && TravelLeg.FacingPastPortal(portal, portalCell, from))
        {
            // The hop's own order (M6 4 f5): the facing side lies past the portal's plane, so the approach side is the first walk, and it has no recovery. Long round 3 failed here at the Cathedral's Surface portal while this sweep walked the facing side.
            (frontCell, front, _) = TravelLeg.FrontOf(portal with { Facing = null }, portalCell, from);
            (frontCell, front) = rig.FrontRoom(frontCell, front, out _);
            return Walk(rig, from, rig.PointGoal(frontCell, front));
        }

        (frontCell, front) = rig.FrontRoom(frontCell, front, out var inNoRoom);
        var facing = inNoRoom ? null : Walk(rig, from, rig.PointGoal(frontCell, front));
        if (facing is { Ok: true })
        {
            return facing;
        }

        // The hop's one recovery, front-wall: the point on the approach side.
        var (approachCell, approach, _) = TravelLeg.FrontOf(portal with { Facing = null }, portalCell, from);
        (approachCell, approach) = rig.FrontRoom(approachCell, approach, out _);
        return Walk(rig, from, rig.PointGoal(approachCell, approach));
    }

    /// <summary>
    /// The places an errand can leave her with no walk home planned, each named; a pin fails when the walk reaches, so a fix removes it. Empty: the Greenmire Cider's rooms (<c>nen-ai-pet-drudge</c> step 5,
    /// <c>TODO(m6-cathedral-pile-way-home)</c>), whose planned walk to the Surface portal ran into the pile in the ramp room, now walk home since the merge of the world walk's W8 room fixes
    /// (walk/w0; which of them closed it is not singled out).
    /// </summary>
    private static readonly HashSet<(string Quest, int Step)> NoWalkHomeYet = [];

    /// <summary>The Cathedral portal's room, where the climb ended live: a body walks up the stair and out of the door to the street (the Cathedral's Surface portal's drop), by the file's way out and the ordinary plan after it, with the runner and the physics. The rest of the walk home is the plan from the street, which the sweep above confirms.</summary>
    [RequiresDatsFact]
    public void A_body_walks_home_from_the_cathedral_portals_room_up_the_stair_and_out_of_the_door()
    {
        var foot = new CellFrame(0xD8540102, new AcFrame(new Vector3(85.08f, 17.38f, 26.33f), Quaternion.Identity)); // where the live body stood
        using var rig = new FacilityHubWalkDatTests.Rig(Dats, foot);
        var end = rig.Run(rig.PointGoal(0xD8540019, new Vector3(84.8f, 2.1f, 31.8f)), "home-from-the-cathedral-house");
        output.WriteLine(end.Line);
        Assert.True(end.Arrived, end.Line);
        Assert.Equal(0xD8540019u, rig.Position.CellId);
    }

    /// <summary>Long round 3, level 14: she retreated from the Greenmire Cheese's room, and the walk home aimed at the approach side of the Cathedral's Surface portal, which the physics refused twice and which ended the climb. The travel file's walk in goes to the errand's own stand: a body reaches it, and the plan keeps off the portal, which takes a body on touch.</summary>
    [RequiresDatsFact]
    public void From_the_greenmire_cheese_the_walk_home_reaches_the_surface_portals_stand_and_keeps_off_the_portal()
    {
        var edge = Routes.WayHomeFrom("stone-cathedral", FetchLevel).WayHome!.Steps[0].Edge;
        Assert.True(edge.WalkIn is { Count: > 0 }, "the Cathedral's Surface portal has no walk in");
        var stand = edge.WalkIn![0];
        var live = new CellFrame(0x01540178, new AcFrame(new Vector3(19.4f, -56.6f, -5.964f), Quaternion.Identity)); // where the live body stood
        using var rig = Rig(live);
        var from = rig.RealRoom(live);
        var goal = rig.PointGoal(stand.Cell, stand.Origin);
        var source = rig.Planner.SourceFor(from.CellId, JourneyLegPlanner.GoalCellOf(goal), rig.Map);
        var plan = source.Plan(from, goal, new NavScars());
        Assert.Equal(NavPathStatus.Found, plan.Status);

        var portal = new Vector2(edge.Origin.X, edge.Origin.Y);
        var nearest = double.MaxValue;
        var previous = new Vector2(from.Origin.X, from.Origin.Y);
        foreach (var point in plan.Waypoints.Select(w => new Vector2(w.At.Origin.X, w.At.Origin.Y)))
        {
            for (var t = 0f; t <= 1f; t += 0.05f)
            {
                nearest = Math.Min(nearest, Vector2.Distance(Vector2.Lerp(previous, point, t), portal));
            }

            previous = point;
        }

        var walk = Walk(rig, from, goal);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"cheese-to-stand metres={walk.Metres:0.0} pass={(walk.Ok ? "yes" : "no")} nearest_portal_m={nearest:0.00} {walk.Why}"));
        Assert.True(walk.Ok, walk.Why);
        Assert.True(nearest >= 2.0, string.Create(CultureInfo.InvariantCulture, $"the plan passes {nearest:0.00} m from the portal"));
    }

    /// <summary>The way out serves the rooms it names, and a room the file does not name keeps today's failure, which now says the file has no way out of it.</summary>
    [RequiresDatsFact]
    public void A_room_with_no_named_way_out_says_so_and_a_named_room_plans_its_way_first()
    {
        var portalRoom = new CellFrame(0xD8540102, new AcFrame(new Vector3(84.9f, 25f, 26.05f), Quaternion.Identity));
        using var rig = Rig(portalRoom);
        var outside = rig.PointGoal(0xD8540019, new Vector3(84.8f, 2.1f, 31.8f));
        var named = (IndoorOutdoorPathSource)rig.Planner.SourceFor(portalRoom.CellId, JourneyLegPlanner.GoalCellOf(outside), rig.Map);
        var plan = named.Plan(portalRoom, outside, new NavScars());
        Assert.Equal(NavPathStatus.Found, plan.Status);
        Assert.Contains(plan.Waypoints, w => w.At.CellId == 0xD8540103); // up in the door hall before the door

        var without = new IndoorOutdoorPathSource(new PathPlanner(rig.Map), rig.Planner.CellsOf(portalRoom.CellId));
        var failed = without.Plan(portalRoom, outside, new NavScars());
        Assert.Equal(NavPathStatus.Failed, failed.Status);
        Assert.StartsWith("indoor-start-unreachable:0xD8540102: ", failed.Reason, StringComparison.Ordinal);
        Assert.Contains("TODO(m6-room-way-out)", failed.Reason, StringComparison.Ordinal);
    }

    /// <summary>The live cause, M6 8 M: the door hall's own floor is no counter. A plan named in one room whose next point is 5 m lower reads as raised without the room's floor; with it, she stands on the floor, and the leg plans its flight from the hall. Renald's counter stays raised: the room's floor under it is 0.94 m down.</summary>
    [RequiresDatsFact]
    public void A_flight_named_in_one_room_is_no_raised_start_and_a_counter_still_is()
    {
        var q = QuestCatalog.Get("nen-ai-pet-drudge");
        var hall = q.Steps[0].Place!;
        var foot = q.Steps[1].Place!;
        var start = new CellFrame(hall.Cell, new AcFrame(hall.Point!.Value - new Vector3(0f, 0f, 0.04f), Quaternion.Identity)); // the live body's height, 31.26
        using var rig = Rig(start);
        var goal = rig.PointGoal(foot.Cell, foot.Point!.Value);
        var source = (IndoorOutdoorPathSource)rig.Planner.SourceFor(start.CellId, JourneyLegPlanner.GoalCellOf(goal), rig.Map);
        var plan = source.Plan(start, goal, new NavScars());
        Assert.NotNull(RaisedStart.FloorBelow(plan, start, out _)); // the plan alone says raised: the live misread
        var floor = source.Rooms.RoomFloorUnder(start.CellId, start.Origin + new Vector3(0f, 0f, RaisedStart.FloorToleranceMetres));
        Assert.NotNull(floor);
        Assert.Null(RaisedStart.FloorBelow(plan, start, out _, floor));

        var counter = new CellFrame(0xA9B4013F, new AcFrame(new Vector3(134.76f, 15.40f, 94.94f), Quaternion.Identity));
        rig.Streamer.EnsureNow(counter.CellId);
        var rooms = new IndoorOutdoorPathSource(new PathPlanner(rig.Map), rig.Planner.CellsOf(counter.CellId)).Rooms;
        var underCounter = rooms.RoomFloorUnder(counter.CellId, counter.Origin + new Vector3(0f, 0f, RaisedStart.FloorToleranceMetres));
        Assert.NotNull(underCounter);
        Assert.True(counter.Origin.Z - underCounter > RaisedStart.RaisedMetres, $"the room's floor under the counter is {underCounter}");
    }

    /// <summary>Every go of an errand, from where the walk before it really ended: none reads as raised on its room's floor (the step-off is for a counter), so no go starts by being walked off its plan.</summary>
    /// <param name="key">The quest file's key.</param>
    [RequiresDatsTheory]
    [InlineData("worcer-heirlooms")]
    [InlineData("nen-ai-pet-drudge")]
    [InlineData("lubziklan-stolen-goods")]
    public void No_go_of_an_errand_reads_as_a_raised_start(string key)
    {
        var quest = QuestCatalog.Get(key);
        var failures = new List<string>();
        CellFrame? at = null;
        foreach (var step in quest.Steps.Where(s => s.Kind is QuestStepKind.Go or QuestStepKind.Use && s.Place?.Point is not null))
        {
            var cell = PortalCell(step.Place!.Cell, step.Place.Point!.Value);
            var point = new CellFrame(cell, new AcFrame(step.Place.Point!.Value, Quaternion.Identity));
            if (step.Kind == QuestStepKind.Go && step.Way is null && at is { } from && from.CellId >> 16 == cell >> 16 && LandDefs.IsEnvCell(from.CellId))
            {
                using var rig = Rig(from);
                var real = rig.RealRoom(from);
                var goal = rig.PointGoal(cell, step.Place.Point!.Value);
                if (rig.Planner.SourceFor(real.CellId, JourneyLegPlanner.GoalCellOf(goal), rig.Map) is IndoorOutdoorPathSource source)
                {
                    var plan = source.Plan(real, goal, new NavScars());
                    var floor = source.Rooms.RoomFloorUnder(real.CellId, real.Origin + new Vector3(0f, 0f, RaisedStart.FloorToleranceMetres));
                    if (RaisedStart.FloorBelow(plan, real, out _, floor) is { } z)
                    {
                        failures.Add(string.Create(CultureInfo.InvariantCulture, $"step {step.Index} ({step.What}) from 0x{real.CellId:X8}: reads raised over z {z:0.00} (the room's floor {floor?.ToString("0.00", CultureInfo.InvariantCulture) ?? "none"})"));
                    }
                }
            }

            at = point;
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>
    /// A go from the real end of a pick-up, Worcer's Candlestick: ACE's walk to the item left the live body at (134.01, -27.76), 0.4 m from the file's point, where the plan's first segment clipped something the physics
    /// refused, and the named recovery's step back (the last walkable point the live run recorded) walked: the go was answered by step-away, as it was live. Since the merge of the world walk's W8 room fixes
    /// (walk/w0) the plan from the real end walks too; the step back still walks, so the recovery stays a way on.
    /// </summary>
    [RequiresDatsFact]
    public void A_go_from_where_a_pick_up_really_left_her_walks_and_so_does_the_step_away()
    {
        var bowl = new Vector3(92.1639f, -23.1106f, -11.9752f);
        var real = new CellFrame(0x016301A3, new AcFrame(new Vector3(134.015f, -27.762f, 0.005f), Quaternion.Identity));
        using var rig = Rig(real);
        var fromReal = Walk(rig, real, rig.PointGoal(0x01630158, bowl));
        output.WriteLine($"from the real end: {fromReal.Ok} {fromReal.Why}");
        Assert.True(fromReal.Ok, fromReal.Why);
        var back = new CellFrame(0x016301A3, new AcFrame(new Vector3(132.93f, -29.14f, 0.005f), Quaternion.Identity));
        var walked = Walk(rig, back, rig.PointGoal(0x01630158, bowl));
        Assert.True(walked.Ok, walked.Why);
    }
}
