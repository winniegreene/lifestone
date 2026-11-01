using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Navigation;
using Lifestone.Client.Physics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Movement;
using Lifestone.Client.Tests.Movement.Wayfinding;
using Lifestone.Client.Tests.Physics;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M5 7 P7l (D143, D141): the walk out of Archmage Cindrue's entry hall 0xA9B40117 to the Scrivener of Item Magic outdoors in landblock 0xA9B3, on the retail dats with both of the hall's outside doors standing shut in the
/// player's world (the server-object host), the walker, the journeys' own leg planner and the runner with a door feed (the leg as <c>JourneyRunLeg</c> runs it). Live trip check 3 (2026-10-30 06:13 UTC) planned the leg out
/// by the east door 0x7A9B400A (<c>cross_m=5.319 stop_m=4.019</c>) and the body stuck at (159.2, 133.4) pressed on the shut door 0.9 m short of the stop along the route, three plans and twelve rungs, the door never Used
/// (<c>door_steps=1 door_uses=0</c>). The cause: the room route zigzags inside the door's thin doorway cell 0xA9B40119 (up to the outer sill, back 0.45 m, up to it again), so the stop 1.3 m before the doorway along the route
/// lay 0.4 m from it in a straight line, inside the body's contact with the panel. The route confirmer passed the leg offline because it judges the fixed world only (the door is a server object, M5 5 F3). The stop now stands
/// the stand-off from the doorway in a straight line (<see cref="DoorCrossings.StepsOf"/>), and a body that stalls within use reach of the shut door ahead Uses it there (<see cref="NavRunnerOptions.DoorUseReachMetres"/>, D141).
/// </summary>
/// <remarks>
/// Test data, read-only from the pinned world database (ACE-World-Database-v0.9.296, <c>landblock_instance</c>): the hall's doors, both wcid 412 (Setup 0x020019FF, UseRadius 2), the front door 0x7A9B4009 at
/// (148.525, 131.611, 66) turned (w -0.924213, z 0.381878) and the east door 0x7A9B400A at (159.73, 133.93, 66) turned (w 0.381878, z 0.924213). The starts are the live ones: the stall at (159.2, 133.4, 66.005) with the
/// live heading, and live check 2's start after Monyra, (155.42, 134.31, 66.01).
/// </remarks>
[Trait("Tier", "Exhaustive")]
public sealed class CindrueHallExitDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const string Den = "black-spawn-den-aluvian";
    private const double Fps = 60.0;
    private const uint Hall = 0xA9B40117;
    private const uint EastDoorCell = 0xA9B40119;
    private const uint DoorSetupId = 0x020019FF;
    private const uint FrontDoor = 0x7A9B4009;
    private const uint EastDoor = 0x7A9B400A;
    private static readonly Vector3 FrontDoorAt = new(148.525f, 131.611f, 66f);
    private static readonly Quaternion FrontDoorTurn = new(0f, 0f, 0.381878f, -0.924213f);
    private static readonly Vector3 EastDoorAt = new(159.73f, 133.93f, 66f);
    private static readonly Quaternion EastDoorTurn = new(0f, 0f, 0.924213f, 0.381878f);
    private static readonly Vector3 LiveStall = new(159.2f, 133.4f, 66.005f);
    private static readonly Quaternion LiveHeading = new(0f, 0f, -0.3922f, 0.9199f);
    private static readonly Vector3 AfterMonyra = new(155.42f, 134.31f, 66.01f);

    private DatSet Dats => fixture.Dats;

    /// <summary>
    /// From the live stall the doorway is 0.7 m away in a straight line, inside the stand-off: the stop is the route's start (<c>stop_rule=start</c>), so the door step Uses the door before the body moves, the door opens
    /// and the body walks out through 0xA9B40119 to the Scrivener. Before P7l the stop lay 0.9 m on along the zigzag, the body pressed on the panel, and the run ended stuck after 3 plans and 12 rungs, as live.
    /// </summary>
    [RequiresDatsFact]
    public void From_the_live_stall_by_the_shut_east_door_the_leg_uses_it_and_walks_out_to_the_Scrivener()
    {
        var leg = Walk("hall-stall", LiveStall, LiveHeading, new NavRunnerOptions { Name = "hall-stall" });

        Assert.True(leg.Runner.State == NavRunState.Arrived, leg.Runner.FailureReason);
        Assert.Equal([EastDoor], leg.Doors.Used);
        Assert.Equal(1, leg.Runner.DoorsOpened);
        Assert.Contains(leg.Log, l => l.Contains("the route crosses a door", StringComparison.Ordinal) && l.Contains("door=0x7A9B400A", StringComparison.Ordinal) && l.Contains("stop_rule=start", StringComparison.Ordinal));
        Assert.Contains(leg.Log, l => l.Contains("stopped at a closed door: using it", StringComparison.Ordinal));
        AssertOutToTheScrivener(leg);
    }

    /// <summary>
    /// From live check 2's start after Monyra the route runs 5.6 m to the doorway; along the route the old stop (1.3 m before the crossing) lies within half a metre of the doorway in a straight line, in the panel's
    /// contact; the new stop stands 1.3 m from it in a straight line (<c>stop_rule=straight</c>). The body stops there, Uses the door without a stall, and walks out to the Scrivener.
    /// </summary>
    [RequiresDatsFact]
    public void From_the_start_after_Monyra_the_stop_stands_the_stand_off_from_the_doorway_in_a_straight_line_and_the_leg_walks_out_to_the_Scrivener()
    {
        var (source, goal, crossings) = Plan(new CellFrame(Hall, new AcFrame(AfterMonyra, Quaternion.Identity)));
        var path = source.Plan(new CellFrame(Hall, new AcFrame(AfterMonyra, Quaternion.Identity)), goal, new NavScars());
        Assert.Equal(NavPathStatus.Found, path.Status);
        var standOff = NavRunnerOptions.Default.DoorStandOffMetres;
        var step = Assert.Single(DoorCrossings.StepsOf(path, crossings, standOff), s => s.Guid == EastDoor);
        var route = path.ToRoute();
        var cross = route.PointAt(step.CrossArc).Global;
        var oldStop = route.PointAt(step.CrossArc - standOff).Global; // the stop along the route (before P7l)
        var newStop = route.PointAt(step.StopArc).Global;
        output.WriteLine($"east door: cross {step.CrossArc:F2} m, stop {step.StopArc:F2} m ({step.StopRule}, {step.StopStraightMetres:F2} m straight); the old stop {Vector2.Distance(oldStop, cross):F2} m from the doorway, the new {Vector2.Distance(newStop, cross):F2} m");
        Assert.Equal(EastDoorCell, step.Crossing.CellId);
        Assert.InRange(Vector2.Distance(oldStop, cross), 0f, 0.6f); // the cause: the old stop stood inside the body's contact with the shut panel (a walker stops 0.52 m short of it, plus its radius)
        Assert.Equal("straight", step.StopRule);
        Assert.Equal(standOff, Vector2.Distance(newStop, cross), 0.01f);
        Assert.True(step.StopArc < step.CrossArc - standOff, "the stop moved earlier along the route");

        var leg = Walk("hall-monyra", AfterMonyra, Quaternion.Identity, new NavRunnerOptions { Name = "hall-monyra" });

        Assert.True(leg.Runner.State == NavRunState.Arrived, leg.Runner.FailureReason);
        Assert.Equal([EastDoor], leg.Doors.Used);
        Assert.Equal(0, leg.Runner.StalledDoorUses);
        Assert.Contains(leg.Log, l => l.Contains("door=0x7A9B400A", StringComparison.Ordinal) && l.Contains("stop_rule=straight", StringComparison.Ordinal));
        AssertOutToTheScrivener(leg);
    }

    /// <summary>
    /// The owner's rule D141 (check, act, check) on its own: with the stand-off set inside the panel's contact (0.1 m) the stop cannot be reached, as the live stop could not; the body stalls against the shut door
    /// within its use reach, Uses it from there (logged <c>using it from here (D141)</c>), sees it open and walks on, with no rung or replan into the same shut door.
    /// </summary>
    [RequiresDatsFact]
    public void A_body_stalled_within_use_reach_of_the_shut_door_short_of_an_unreachable_stop_uses_it_from_there_D141()
    {
        var leg = Walk("hall-d141", AfterMonyra, Quaternion.Identity, new NavRunnerOptions { Name = "hall-d141", DoorStandOffMetres = 0.1f });

        Assert.True(leg.Runner.State == NavRunState.Arrived, leg.Runner.FailureReason);
        Assert.Equal(1, leg.Runner.StalledDoorUses);
        Assert.Equal([EastDoor], leg.Doors.Used);
        Assert.Equal(1, leg.Runner.DoorsOpened);
        Assert.Contains(leg.Log, l => l.Contains("stalled within use reach of a shut door short of its stop: using it from here (D141)", StringComparison.Ordinal) && l.Contains("door=0x7A9B400A", StringComparison.Ordinal));
        Assert.DoesNotContain(leg.Log, l => l.Contains("replanned", StringComparison.Ordinal));
        AssertOutToTheScrivener(leg);

        // Without the rule (no use reach) the same stall is live's: the ladder climbs against the shut door, replans into it and the run ends stuck, the door never Used.
        var without = Walk("hall-no-d141", AfterMonyra, Quaternion.Identity, new NavRunnerOptions { Name = "hall-no-d141", DoorStandOffMetres = 0.1f, DoorUseReachMetres = 0f });
        Assert.Equal(NavRunState.Blocked, without.Runner.State);
        Assert.Contains("stuck after 3 plans and 12 rungs", without.Runner.FailureReason, StringComparison.Ordinal);
        Assert.Empty(without.Doors.Used);
        Assert.Contains(without.Log, l => l.Contains("stalled before a shut door out of use reach: the ladder climbs (D141)", StringComparison.Ordinal));
    }

    /// <summary>The same stall with a door that never opens: one Use, the retry, then the run ends by the door's own name (<c>door-closed-timeout</c>), never a rung or a replan into it.</summary>
    [RequiresDatsFact]
    public void A_shut_door_used_from_a_stall_that_stays_shut_ends_the_run_by_its_name_without_a_replan_into_it()
    {
        var leg = Walk("hall-d141-shut", AfterMonyra, Quaternion.Identity, new NavRunnerOptions { Name = "hall-d141-shut", DoorStandOffMetres = 0.1f }, openDelay: double.PositiveInfinity);

        Assert.Equal(NavRunState.Blocked, leg.Runner.State);
        Assert.Contains("door-closed-timeout: door 0x7A9B400A did not open", leg.Runner.FailureReason, StringComparison.Ordinal);
        Assert.Equal(1, leg.Runner.StalledDoorUses);
        Assert.Equal(2, leg.Runner.DoorUses);
        Assert.DoesNotContain(leg.Log, l => l.Contains("replanned", StringComparison.Ordinal));
    }

    private void AssertOutToTheScrivener(Leg leg)
    {
        var ground = GroundCatalog.Get(Den);
        var scrivener = ground.Vendors.First(v => v.Name == "Scrivener of Item Magic");
        var stop = scrivener.StopPoint ?? scrivener.Point;
        Assert.Equal(Hall, leg.Visited[0]);
        Assert.Contains(EastDoorCell, leg.Visited); // out by the east door's doorway cell
        Assert.Equal(scrivener.StopCell >> 16, leg.At.CellId >> 16);
        Assert.True(LandDefs.IsOutdoorCell(leg.At.CellId), leg.At.ToString());
        Assert.InRange(Vector2.Distance(new Vector2(leg.At.Origin.X, leg.At.Origin.Y), new Vector2(stop.X, stop.Y)), 0f, NavGoal.DefaultStopTolerance + 0.5f);
    }

    private sealed record Leg(NavRunner Runner, HostDoors Doors, List<uint> Visited, CellFrame At, IReadOnlyList<string> Log);

    /// <summary>The leg's source and goal (the station's request for the Scrivener, <c>ResupplyRun.Meet</c>) from <paramref name="start"/>, with the doors' doorways on its rooms and tiles.</summary>
    private (INavPathSource Source, NavGoal Goal, IReadOnlyList<DoorCrossing> Crossings) Plan(CellFrame start)
    {
        var ground = GroundCatalog.Get(Den);
        var planner = new JourneyLegPlanner(Dats);
        var scrivener = ground.Vendors.First(v => v.Name == "Scrivener of Item Magic");
        var request = new TravelRequest(new TeleportDestination(scrivener.Name, [], new WorldLocation(scrivener.Name, new CellPosition(scrivener.StopCell, scrivener.StopPoint ?? scrivener.Point), HasHeight: true, HeadingDegrees: 0), "hall exit test"), TravelPurpose.Vendor);
        var goal = planner.GoalOf(new J1JourneyRig().View, request);
        var source = planner.SourceFor(start.CellId, JourneyLegPlanner.GoalCellOf(goal), Map());
        var radius = DoorCrossings.SetupExtents(Dats)(DoorSetupId)!.Value;
        var doors = new List<NavDoor> { new(FrontDoor, ServerObjectPhysicsTests.OutdoorAt(FrontDoorAt), radius, false, false), new(EastDoor, ServerObjectPhysicsTests.OutdoorAt(EastDoorAt), radius, false, false) };
        return (source, goal, DoorCrossings.Map(doors, source));
    }

    /// <summary>
    /// The leg over the whole chain: the Holtburg world with both hall doors shut in the server-object host, the walker placed at <paramref name="at"/> in the hall, the runner over the leg planner's source with a door feed
    /// whose Use turns the door Ethereal <paramref name="openDelay"/> s later (<see cref="HostDoors"/>, standing for <c>SessionDoorFeed</c> and ACE's <c>Door.Open</c>).
    /// </summary>
    private Leg Walk(string name, Vector3 at, Quaternion heading, NavRunnerOptions options, double openDelay = 0.6)
    {
        var world = ServerObjectPhysicsTests.HoltburgWorld(Dats);
        var host = new ServerObjectPhysicsHost(world, Dats, log: NullLogSink.Instance);
        host.Create(DoorDescription(FrontDoor), ServerObjectPhysicsTests.OutdoorAt(FrontDoorAt) with { Frame = new AcFrame(FrontDoorAt, FrontDoorTurn) });
        host.Create(DoorDescription(EastDoor), ServerObjectPhysicsTests.OutdoorAt(EastDoorAt) with { Frame = new AcFrame(EastDoorAt, EastDoorTurn) });
        Assert.True(host.Hosts(FrontDoor) && host.Hosts(EastDoor), "both hall doors stand shut in the world");
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230));
        var start = new CellFrame(Hall, new AcFrame(at, heading));
        var (source, goal, _) = Plan(start);
        var doors = new HostDoors(host, DoorCrossings.SetupExtents(Dats)(DoorSetupId)!.Value, [FrontDoor, EastDoor]) { OpenDelaySeconds = openDelay };
        var h = new ActionHarness(offline: true);
        var now = 0.0;
        var log = new RecordingLogSink();
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, name);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.True(walker.Place(start), $"placed at {start}");
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, confirmer, audit, options, log) { Doors = doors };
        runner.Start(goal, now);
        var visited = new List<uint> { walker.Position.CellId };
        while (!runner.IsDone && now < 150)
        {
            now += 1.0 / Fps;
            doors.Now = now;
            walker.Controller.Tick(now);
            host.Tick();
            walker.ReplaceAfterObjectsEntered(host.Stats);
            runner.Step(now);
            h.Tick(1.0 / Fps);
            if (walker.Position.CellId != visited[^1])
            {
                visited.Add(walker.Position.CellId);
            }
        }

        var lines = log.Entries.Where(e => e.Category == "wayfinding").Select(e => e.Line).ToList();
        foreach (var line in lines.Where(l => l.Contains("door", StringComparison.Ordinal) || l.Contains("stuck", StringComparison.Ordinal) || l.Contains("plan", StringComparison.Ordinal)))
        {
            output.WriteLine("  " + line);
        }

        output.WriteLine($"{name}: {runner.State} after {now:F1} s; {runner.VerdictFields()} stalled_door_uses={runner.StalledDoorUses} at 0x{walker.Position.CellId:X8} {walker.Position.Origin}; used {string.Join(" ", doors.Used.Select(g => $"0x{g:X8}"))}; cells {string.Join(" ", visited.Select(c => (c & 0xFFFF).ToString("X3")))}; {runner.FailureReason}");
        runner.Dispose();
        return new Leg(runner, doors, visited, walker.Position, lines);
    }

    private static ServerObjectDescription DoorDescription(uint guid) =>
        new(guid, DoorSetupId, 1f, (uint)PhysicsState.ReportCollisions, IsCreature: false, IsSelf: false, DescriptionFlags: 0x1000);

    /// <summary>The terrain tiles of Holtburg and the landblocks round it (the Scrivener's 0xA9B3 among them).</summary>
    private NavMap Map()
    {
        var builder = new LandblockCollisionBuilder(Dats);
        var map = new NavMap();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                map.Attach(NavTileBuilder.Build(builder.Build(LandblockCoord.Holtburg.Offset(dx, dy)!.Value)));
            }
        }

        return map;
    }
}
