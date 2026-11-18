using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session;
using Lifestone.Client.Tests.Session.Grounds;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// M5 item 5, d1: doors on the routes, on the real dats (Exhaustive tier). A door is a server object (D25; since M5 item 5 h1 and h2 the server-object physics host keeps a physics object for it in the body's world, so it stops the walker), and the wayfinder reads it from the object table:
/// <see cref="DoorCrossings"/> maps it to the doorway it stands in, the planned route marks a door step, and the runner stops before a closed door, Uses it, waits for its Ethereal state and walks on
/// (<c>purpose=door</c>); a door that stays shut fails the leg <c>door-closed-timeout</c>; an open one is walked through without a Use; a closed door between the body and an object goal blocks its sight.
/// The server is a scripted feed (<see cref="ScriptedDoors"/>): it opens the door a set time after the Use, as ACE's <c>Door.Open</c> sets Ethereal at once (<c>Door.cs:111-130</c> @95ef426d).
/// Test data, read-only from the pinned world database (ACE-World-Database-v0.9.296): Holtburg's landblock_instance 0x7A9B4015 and 0x7A9B4016 (weenie 412, door-aluvian-house, Setup 0x020019FF,
/// UseRadius 2 m, ResetInterval 300 s) and the den's 0x70105004 and 0x70105005 (weenie 1295, doordungeonlockedimpregnable). The den has no other door and Tusker Temple (0x5C4A) has none: the
/// den's room-to-room doorway is proved with a stand-in door that is a placeholder (no ACE door stands there).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class DoorLegTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const double Fps = 60.0;
    private const uint HoltburgSetup = 0x020019FF;
    private const uint RenaldFrontRoomCell = 0xA9B4013F;
    private const uint FrontDoor = 0x7A9B4015; // Renald's shop door, into cell 0xA9B40150
    private const uint SideDoor = 0x7A9B4016; // another door of the same building, into cell 0xA9B40145
    private static readonly Vector3 RenaldAt = new(139.23f, 18.33f, 94.01f); // object 0x7A9B4021
    private static readonly Vector3 FrontDoorAt = new(132.56f, 17.115f, 94f);
    private static readonly Vector3 SideDoorAt = new(136.29f, 5.155f, 94f);

    private DatSet Dats => fixture.Dats;

    private static CellFrame LifestoneStart() => new(0xA9B40019, new AcFrame(new Vector3(81.33f, 11.80f, 94.01f), Quaternion.Identity));

    private static NavGoal Renald() => NavGoal.ToObject(0x7A9B4021, new NavPoint(0xA9B40000, RenaldAt), 6.5f, needSight: false, RenaldFrontRoomCell);

    private float Extent() => DoorCrossings.SetupExtents(Dats)(HoltburgSetup)!.Value;

    private NavDoor Door(uint guid, Vector3 at, bool open = false, bool? locked = null, uint cell = 0xA9B40000) =>
        new(guid, new CellFrame(cell, new AcFrame(at, Quaternion.Identity)), Extent(), open, locked);

    [RequiresDatsFact]
    public void A_door_setups_extent_is_its_selection_sphere_not_its_tiny_physics_radius()
    {
        var extent = Extent();
        output.WriteLine($"door extent {extent} (physics radius {HostileCreatureFeed.SetupRadii(Dats)(HoltburgSetup)})");
        Assert.InRange(extent, 0.8f, 1.0f);
        Assert.True(HostileCreatureFeed.SetupRadii(Dats)(HoltburgSetup) < 0.2f);
        Assert.Null(DoorCrossings.SetupExtents(Dats)(0x02FFFFFF));
    }

    [RequiresDatsFact]
    public void Renalds_shop_doors_are_mapped_to_the_doorways_they_stand_in_and_a_door_in_the_open_is_mapped_to_none()
    {
        var (_, map, cells) = Holtburg();
        var rooms = new RoomGraph(cells);
        var street = Door(0x7A9B4FFF, new Vector3(100f, 100f, 66f)); // no doorway within its extent

        var crossings = DoorCrossings.Map([Door(FrontDoor, FrontDoorAt), Door(SideDoor, SideDoorAt), street], rooms, map);

        var front = Assert.Single(crossings, c => c.Door.Guid == FrontDoor);
        Assert.Equal(0xA9B40150u, front.CellId);
        Assert.NotNull(front.Lane);
        Assert.True(front.Distance < 0.1f, front.Distance.ToString());
        Assert.Equal(1f, front.Outward.Y, 0.01f); // out of the building to the north
        Assert.InRange(front.Sill.X, 132.5f, 132.6f);
        var side = Assert.Single(crossings, c => c.Door.Guid == SideDoor);
        Assert.Equal(0xA9B40145u, side.CellId);
        Assert.NotNull(side.Lane);
        Assert.DoesNotContain(crossings, c => c.Door.Guid == street.Guid);

        // Without the tile the room graph's own doorway answers (an exit to the outside), without a lane.
        var noTile = Assert.Single(DoorCrossings.Map([Door(FrontDoor, FrontDoorAt)], rooms, null));
        Assert.Equal(front.CellId, noTile.CellId);
        Assert.Null(noTile.Lane);
    }

    [RequiresDatsFact]
    public void The_route_to_Renalds_shop_marks_one_door_step_at_the_front_door_and_stops_a_metre_before_it()
    {
        var (_, map, cells) = Holtburg();
        var source = new IndoorOutdoorPathSource(new PathPlanner(map), cells);
        var path = source.Plan(LifestoneStart(), Renald(), new NavScars());
        Assert.Equal(NavPathStatus.Found, path.Status);
        var doors = new List<NavDoor> { Door(FrontDoor, FrontDoorAt), Door(SideDoor, SideDoorAt) };

        var steps = DoorCrossings.StepsOf(path, DoorCrossings.Map(doors, source.Rooms, map), 1f);

        var step = Assert.Single(steps);
        Assert.Equal(FrontDoor, step.Guid);
        Assert.Equal(step.CrossArc - 1f, step.StopArc, 0.001f);
        var route = path.ToRoute();
        Assert.InRange(step.CrossArc, 30f, route.ArcLength - 1f);
        var at = route.PointAt(step.CrossArc);
        output.WriteLine($"door step {step.CrossArc:F1} of {route.ArcLength:F1} m at {at}");
        Assert.True(Vector2.Distance(at.Global, new NavPoint(0xA9B40000, new Vector3(132.55f, 17.1f, 94f)).Global) < 0.5f);
    }

    [RequiresDatsFact]
    public void The_runner_stops_before_the_closed_door_uses_it_waits_for_it_to_open_and_walks_on_through()
    {
        var doors = new ScriptedDoors { OpenDelaySeconds = 0.6 }.With(Door(FrontDoor, FrontDoorAt)).With(Door(SideDoor, SideDoorAt));

        var leg = Run("door-open", doors, Renald(), LifestoneStart(), 120);

        Assert.True(leg.Runner.State == NavRunState.Arrived, leg.Runner.FailureReason);
        Assert.Equal(1, leg.Runner.DoorUses);
        Assert.Equal(1, leg.Runner.DoorsOpened);
        Assert.Equal(0, leg.Runner.DoorsFoundOpen);
        Assert.Equal([FrontDoor], doors.Used); // never the side door
        var used = Assert.Single(doors.UsedAt);
        Assert.True(LandDefs.IsOutdoorCell(used.CellId), $"the Use was sent outdoors (0x{used.CellId:X8}), before the doorway");
        var toSill = Vector2.Distance(new Vector2(used.Origin.X, used.Origin.Y), new Vector2(132.55f, 17.1f));
        Assert.InRange(toSill, 0.9f, 1.8f); // stopped short of the walker's contact (0.52 m from the panel) and inside ACE's 2 m use radius for this door
        Assert.True(doors.OpenedAt - doors.UsedAtSeconds[0] >= 0.59, "the door opened after the server's delay");
        Assert.Equal(RenaldFrontRoomCell, leg.At.CellId);
        Assert.Equal(0xA9B40150u, leg.Visited[leg.Visited.IndexOf(RenaldFrontRoomCell) - 1]);
        Assert.Contains(leg.Log.Lines, l => l.Contains("stopped at a closed door: using it", StringComparison.Ordinal) && l.Contains("purpose=door", StringComparison.Ordinal));
        Assert.Contains(leg.Log.Lines, l => l.Contains("the door opened: walking on", StringComparison.Ordinal));
        Assert.Contains("door_uses=1 doors_opened=1", leg.Runner.VerdictFields(), StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void A_door_that_does_not_open_fails_the_leg_door_closed_timeout_outside_the_doorway_after_a_second_use()
    {
        var doors = new ScriptedDoors { NeverOpens = true }.With(Door(FrontDoor, FrontDoorAt));

        var leg = Run("door-shut", doors, Renald(), LifestoneStart(), 120);

        Assert.Equal(NavRunState.Blocked, leg.Runner.State);
        Assert.Contains("door-closed-timeout", leg.Runner.FailureReason, StringComparison.Ordinal);
        Assert.Contains("did not open within 5.0 s of 2 Use(s)", leg.Runner.FailureReason, StringComparison.Ordinal);
        Assert.Equal(2, doors.Used.Count);
        Assert.DoesNotContain(leg.Visited, c => c == 0xA9B40150u); // never through the doorway
        Assert.True(LandDefs.IsOutdoorCell(leg.At.CellId));
        Assert.True(leg.Seconds >= 5.0);
    }

    [RequiresDatsFact]
    public void An_open_door_is_walked_through_without_a_use()
    {
        var doors = new ScriptedDoors().With(Door(FrontDoor, FrontDoorAt, open: true));

        var leg = Run("door-already-open", doors, Renald(), LifestoneStart(), 120);

        Assert.True(leg.Runner.State == NavRunState.Arrived, leg.Runner.FailureReason);
        Assert.Empty(doors.Used);
        Assert.Equal(0, leg.Runner.DoorUses);
        Assert.Equal(1, leg.Runner.DoorsFoundOpen);
        Assert.Single(leg.Runner.DoorSteps);
        Assert.Equal(RenaldFrontRoomCell, leg.At.CellId);
    }

    [RequiresDatsFact]
    public void A_door_known_locked_blocks_the_route_outright_named_and_nothing_is_used()
    {
        var doors = new ScriptedDoors().With(Door(FrontDoor, FrontDoorAt, locked: true));

        var leg = Run("door-locked", doors, Renald(), LifestoneStart(), 20);

        Assert.Equal(NavRunState.Blocked, leg.Runner.State);
        Assert.Contains("door-locked", leg.Runner.FailureReason, StringComparison.Ordinal);
        Assert.Empty(doors.Used);
        Assert.True(Vector3.Distance(leg.At.Origin, LifestoneStart().Origin) < 0.5f, "the body never moved");
    }

    [RequiresDatsFact]
    public void An_unknown_door_is_appraised_once_before_the_walk_and_one_that_reads_not_locked_opens_as_before()
    {
        var doors = new ScriptedDoors { OpenDelaySeconds = 0.6, AppraisalSays = false }.With(Door(FrontDoor, FrontDoorAt));

        var leg = Run("door-appraised-open", doors, Renald(), LifestoneStart(), 120);

        Assert.True(leg.Runner.State == NavRunState.Arrived, leg.Runner.FailureReason);
        Assert.Equal([FrontDoor], doors.LockReads); // once, at the plan
        Assert.Equal(1, leg.Runner.DoorUses);
        Assert.Contains(leg.Log.Lines, l => l.Contains("asking the server (appraisal)", StringComparison.Ordinal));
    }

    [RequiresDatsFact]
    public void A_door_appraised_locked_ends_the_walk_by_name_before_the_body_reaches_it()
    {
        var doors = new ScriptedDoors { AppraisalSays = true }.With(Door(FrontDoor, FrontDoorAt) with { Name = "Front Door" });

        var leg = Run("door-appraised-locked", doors, Renald(), LifestoneStart(), 60);

        Assert.Equal(NavRunState.Blocked, leg.Runner.State);
        Assert.Contains("door-locked", leg.Runner.FailureReason, StringComparison.Ordinal);
        Assert.Contains("0x7A9B4015 (Front Door, cell 0xA9B40150)", leg.Runner.FailureReason, StringComparison.Ordinal);
        Assert.Contains("read from appraised", leg.Runner.FailureReason, StringComparison.Ordinal);
        Assert.Empty(doors.Used);
        Assert.Single(leg.Log.Lines, l => l.Contains("the closed door is locked: the walk stops", StringComparison.Ordinal) && l.Contains("locked_from=appraised", StringComparison.Ordinal));
        Assert.True(LandDefs.IsOutdoorCell(leg.At.CellId));
    }

    [RequiresDatsFact]
    public void A_refused_use_ends_the_walk_at_the_first_use_with_no_second_use_and_no_open_timeout()
    {
        var doors = new ScriptedDoors { RefusesUse = true }.With(Door(FrontDoor, FrontDoorAt));

        var leg = Run("door-refused", doors, Renald(), LifestoneStart(), 60);

        Assert.Equal(NavRunState.Blocked, leg.Runner.State);
        Assert.Contains("door-locked", leg.Runner.FailureReason, StringComparison.Ordinal);
        Assert.Contains("read from refused-use", leg.Runner.FailureReason, StringComparison.Ordinal);
        Assert.DoesNotContain("door-closed-timeout", leg.Runner.FailureReason, StringComparison.Ordinal);
        Assert.Single(doors.Used);
    }

    [RequiresDatsFact]
    public void A_door_unknown_both_ways_keeps_todays_behaviour_two_uses_then_the_open_timeout_saying_unknown()
    {
        var doors = new ScriptedDoors { NeverOpens = true }.With(Door(FrontDoor, FrontDoorAt)); // the appraisal never answers, the Use says nothing

        var leg = Run("door-unknown", doors, Renald(), LifestoneStart(), 120);

        Assert.Equal([FrontDoor], doors.LockReads);
        Assert.Equal(NavRunState.Blocked, leg.Runner.State);
        Assert.Contains("door-closed-timeout", leg.Runner.FailureReason, StringComparison.Ordinal);
        Assert.Contains("locked: unknown", leg.Runner.FailureReason, StringComparison.Ordinal);
        Assert.Equal(2, doors.Used.Count);
    }

    [RequiresDatsFact]
    public void A_door_found_open_is_never_appraised()
    {
        var doors = new ScriptedDoors().With(Door(FrontDoor, FrontDoorAt, open: true));

        _ = Run("door-open-noread", doors, Renald(), LifestoneStart(), 120);

        Assert.Empty(doors.LockReads);
    }

    [RequiresDatsFact]
    public void A_closed_door_between_the_body_and_an_object_goal_blocks_its_sight_and_an_open_one_does_not()
    {
        // The body stands in the street at the front door's lane, the object just inside the doorway (a bare object: no room is named, so the range alone arrives).
        var street = NavMap.ToCellFrame(new NavPoint(0xA9B40000, new Vector3(132.55f, 21.5f, 94.01f)));
        var inside = NavGoal.ToObject(0x7A9B4021, new NavPoint(0xA9B40000, new Vector3(132.55f, 14.1f, 94.01f)), 8f, needSight: true);

        var shut = Run("sight-closed", new ScriptedDoors().With(Door(FrontDoor, FrontDoorAt)), inside, street, 5);
        var open = Run("sight-open", new ScriptedDoors().With(Door(FrontDoor, FrontDoorAt, open: true)), inside, street, 5);

        Assert.Equal(NavRunState.ArrivedWithoutSight, shut.Runner.State);
        Assert.Contains("closed door 0x7A9B4015 stands between", shut.Runner.FailureReason, StringComparison.Ordinal);
        Assert.Equal(NavRunState.Arrived, open.Runner.State);
    }

    [RequiresDatsFact]
    public void The_dens_only_doors_stand_in_a_no_go_cell_and_map_to_no_doorway_so_no_route_of_the_den_meets_them()
    {
        var all = LandblockCellSet.Load(new CellStructureLibrary(Dats), new LandblockCoord(1, 5));
        var graph = new RoomGraph(all, DenGround.Profile.Walk.DoorwayLean);
        var vault = new CellFrame(0x01050104, new AcFrame(new Vector3(52f, -50f, -29.995f), Quaternion.Identity));
        var vaultB = new CellFrame(0x01050104, new AcFrame(new Vector3(48f, -50f, -29.995f), Quaternion.Identity));

        var crossings = DoorCrossings.Map([new NavDoor(0x70105004, vault, 0.9f, false, null), new NavDoor(0x70105005, vaultB, 0.9f, false, null)], graph, null);

        Assert.Empty(crossings); // 2 m off the middle of cell 0x104, whose four doorways are 5 m and 1.7 m from them
        Assert.True(DenGround.Profile.IsNoGo(0x01050104), "and the profile keeps the cell out of every route");
    }

    [RequiresDatsFact]
    public void In_the_dens_room_graph_a_stand_in_door_in_a_room_to_room_doorway_is_opened_on_the_way_through_at_the_profiles_lean()
    {
        // PLACEHOLDER: no ACE door stands in the den's walkable doorways (its only doors are the vault's, above); this stand-in is placed at the middle of the arrival cell's first doorway
        // to a walkable neighbour so that the room-to-room crossing and the profile's doorway lean (0.3) take the same machinery as Holtburg's outside door.
        var coord = new LandblockCoord(1, 5);
        var all = LandblockCellSet.Load(new CellStructureLibrary(Dats), coord);
        var graph = GroundRooms.GraphOf(all, DenGround.Profile);
        var arrival = all.GetCell(DenGround.Arrival.CellId)!;
        var portal = arrival.Portals.First(p => !p.LeadsOutside && graph.Cells.GetCell(p.OtherCellId) is not null && graph.Planner.Fits(arrival, p));
        var doorway = graph.Planner.DoorwayOf(arrival, portal)!.Value;
        var standIn = new NavDoor(0x7FFF0001, new CellFrame(arrival.Id, new AcFrame(new Vector3(doorway.Centre.X, doorway.Centre.Y, doorway.Sill), Quaternion.Identity)), 0.9f, false, null);
        var goalCell = portal.OtherCellId;
        var goal = NavGoal.ToPoint(new NavPoint(0x01050000u, graph.RoomFrame(goalCell)!.Value.Origin), goalCell);
        var doors = new ScriptedDoors { OpenDelaySeconds = 0.5 }.With(standIn);

        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(Dats).Build(coord));
        var h = new ActionHarness(offline: true);
        var now = 0.0;
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, "den-door");
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230), sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(DenGround.Arrival, sliding: true));
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new RoomPathSource(graph), confirmer, audit, new NavRunnerOptions { Name = "den-door" }) { Doors = doors };
        doors.Where = () => walker.Position;
        runner.Start(goal, now);
        while (!runner.IsDone && now < 90)
        {
            now += 1.0 / Fps;
            doors.Now = now;
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / Fps);
        }

        output.WriteLine($"den-door: {runner.State} {runner.VerdictFields()} at 0x{walker.Position.CellId:X8}; {runner.FailureReason}");
        Assert.True(runner.State == NavRunState.Arrived, runner.FailureReason);
        Assert.Equal(goalCell, walker.Position.CellId);
        Assert.Equal([standIn.Guid], doors.Used);
        Assert.Equal(arrival.Id, doors.UsedAt[0].CellId); // stopped in the arrival cell, before the doorway
        Assert.Equal(1, runner.DoorsOpened);
    }

    private (PhysicsWorld World, NavMap Map, LandblockCellSet Cells) Holtburg()
    {
        var builder = new LandblockCollisionBuilder(Dats);
        var world = new PhysicsWorld();
        var map = new NavMap();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var block = builder.Build(LandblockCoord.Holtburg.Offset(dx, dy)!.Value);
                world.Attach(block);
                if (dx == 0 && dy == 0)
                {
                    map.Attach(NavTileBuilder.Build(block));
                }
            }
        }

        return (world, map, LandblockCellSet.Load(new CellStructureLibrary(Dats), LandblockCoord.Holtburg));
    }

    private sealed record Leg(NavRunner Runner, List<uint> Visited, CellFrame At, RecordingLogSink Log, double Seconds);

    /// <summary>The leg's order (JourneyRunLeg): the indoor-outdoor source over Holtburg, a runner with the scripted door feed, the explore-mode walker under the physics.</summary>
    private Leg Run(string name, ScriptedDoors doors, NavGoal goal, CellFrame start, double seconds)
    {
        var (world, map, cells) = Holtburg();
        var h = new ActionHarness(offline: true);
        var now = 0.0;
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, name);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230), sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.True(walker.Place(start), $"placed at {start}");
        doors.Where = () => walker.Position;
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var log = new RecordingLogSink();
        var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new IndoorOutdoorPathSource(new PathPlanner(map), cells), confirmer, audit, new NavRunnerOptions { Name = name }, log) { Doors = doors };
        runner.Start(goal, now);
        var visited = new List<uint> { walker.Position.CellId };
        while (!runner.IsDone && now < seconds)
        {
            now += 1.0 / Fps;
            doors.Now = now;
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / Fps);
            if (walker.Position.CellId != visited[^1])
            {
                visited.Add(walker.Position.CellId);
            }
        }

        output.WriteLine($"{name}: {runner.State} {runner.VerdictFields()} at 0x{walker.Position.CellId:X8} {walker.Position.Origin}; cells {string.Join(" ", visited.Select(c => (c & 0xFFFF).ToString("X3")))}; {runner.FailureReason}");
        runner.Dispose();
        return new Leg(runner, visited, walker.Position, log, now);
    }
}

/// <summary>
/// A scripted door feed standing for ACE (<see cref="INavDoorFeed"/>): the Use opens the door <see cref="OpenDelaySeconds"/> later (Ethereal at once on the server, a round trip on the wire), or never
/// (<see cref="NeverOpens"/>); <see cref="CloseWhen"/> closes an open door again. The crossings are the real ones unless a test gives its own (<see cref="FixedCrossings"/>).
/// </summary>
internal sealed class ScriptedDoors : INavDoorFeed
{
    private readonly Dictionary<uint, State> _doors = [];

    /// <summary>The scripted clock, in seconds; the test sets it every frame.</summary>
    public double Now { get; set; }

    /// <summary>Seconds from a Use to the door being open.</summary>
    public double OpenDelaySeconds { get; set; } = 0.5;

    /// <summary>True for a door the Use does not open.</summary>
    public bool NeverOpens { get; set; }

    /// <summary>Closes an open door again (its guid, the clock) when it returns true, at most <see cref="MaxCloses"/> times.</summary>
    public Func<uint, double, bool>? CloseWhen { get; set; }

    /// <summary>How many times <see cref="CloseWhen"/> may close a door.</summary>
    public int MaxCloses { get; set; } = int.MaxValue;

    /// <summary>Where the body is (for <see cref="UsedAt"/>).</summary>
    public Func<CellFrame>? Where { get; set; }

    /// <summary>The crossings to answer instead of the real mapping (the unit tests have no tile and no rooms).</summary>
    public Func<IReadOnlyList<NavDoor>, IReadOnlyList<DoorCrossing>>? FixedCrossings { get; set; }

    /// <summary>An outcome every Use ends with at once (a Refused or DryRun the kill switch gives), or null for a Use that sends and says nothing.</summary>
    public ActionOutcomeKind? UseOutcome { get; set; }

    /// <summary>What <see cref="INavDoorFeed.Version"/> answers; a test that adds a door mid-run bumps it (or leaves it, for a door the runner must find at a stuck rung).</summary>
    public int Version { get; set; }

    /// <summary>What the server's appraisal of a door says (M6 8 C): true locked, false not locked, null never answers (an unknown lock).</summary>
    public bool? AppraisalSays { get; set; }

    /// <summary>True for a door that refuses the Use as a locked one does (sound 0x94): the door is then locked, read from <c>refused-use</c>, and never opens.</summary>
    public bool RefusesUse { get; set; }

    /// <summary>The doors whose lock the runner asked for, in order.</summary>
    public List<uint> LockReads { get; } = [];

    /// <summary>The doors Used, in order.</summary>
    public List<uint> Used { get; } = [];

    /// <summary>Where the body stood at each Use.</summary>
    public List<CellFrame> UsedAt { get; } = [];

    /// <summary>The clock at each Use.</summary>
    public List<double> UsedAtSeconds { get; } = [];

    /// <summary>The clock when a door last opened.</summary>
    public double OpenedAt { get; private set; }

    /// <summary>Adds a door.</summary>
    public ScriptedDoors With(NavDoor door)
    {
        _doors[door.Guid] = new State(door);
        return this;
    }

    /// <inheritdoc/>
    public void Fill(List<NavDoor> into)
    {
        into.Clear();
        foreach (var guid in _doors.Keys)
        {
            into.Add(Read(guid));
        }
    }

    /// <inheritdoc/>
    public bool TryGet(uint guid, out NavDoor door)
    {
        var known = _doors.ContainsKey(guid);
        door = known ? Read(guid) : default;
        return known;
    }

    /// <inheritdoc/>
    public ActionHandle? Use(uint guid)
    {
        Used.Add(guid);
        UsedAtSeconds.Add(Now);
        if (Where is { } where)
        {
            UsedAt.Add(where());
        }

        var state = _doors[guid];
        if (RefusesUse)
        {
            state.Door = state.Door with { Locked = true, LockedFrom = "refused-use" };
        }
        else if (!state.Door.Open && !NeverOpens && double.IsNaN(state.OpensAt))
        {
            state.OpensAt = Now + OpenDelaySeconds;
        }

        if (UseOutcome is { } kind)
        {
            var handle = new ActionHandle(Used.Count, ActionKind.Use, ActionSource.Test, "Use", Now);
            handle.TryComplete(new ActionOutcome(kind, 0, "scripted"), Now, (_, _) => { });
            return handle;
        }

        return null;
    }

    /// <inheritdoc/>
    public void ReadLock(uint guid)
    {
        LockReads.Add(guid);
        if (AppraisalSays is { } says)
        {
            _doors[guid].Door = _doors[guid].Door with { Locked = says, LockedFrom = "appraised" };
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<DoorCrossing> Crossings(IReadOnlyList<NavDoor> doors, INavPathSource paths) =>
        FixedCrossings is { } fixedCrossings ? fixedCrossings(doors) : DoorCrossings.Map(doors, paths);

    private NavDoor Read(uint guid)
    {
        var state = _doors[guid];
        if (!state.Door.Open && !double.IsNaN(state.OpensAt) && Now >= state.OpensAt)
        {
            state.Door = state.Door with { Open = true };
            state.OpensAt = double.NaN;
            OpenedAt = Now;
        }
        else if (state.Door.Open && state.Closes < MaxCloses && CloseWhen is { } closeWhen && closeWhen(guid, Now))
        {
            state.Door = state.Door with { Open = false };
            state.Closes++;
        }

        return state.Door;
    }

    private sealed class State(NavDoor door)
    {
        public NavDoor Door { get; set; } = door;

        public double OpensAt { get; set; } = double.NaN;

        public int Closes { get; set; }
    }
}

/// <summary>
/// M5 item 5, d1: the session's door feed (<see cref="SessionDoorFeed"/>) and the station's door watch (<see cref="DoorWatch"/>) over a scripted object table: a door is an object with the Door description bit (ACE
/// <c>Door.cs:39</c> @95ef426d) and a position; it is open while its physics state has Ethereal; the Use goes out by the automation source; the watch says <c>yes</c> only when a Use and an opening both happened.
/// </summary>
public sealed class DoorFeedTests
{
    private const uint Door = 0x7A9B4015;

    private static WorldObject Make(uint guid, uint flags, PhysicsState state, bool placed = true, uint? setup = null, float? scale = null)
    {
        var weenie = new WeenieDesc { Header = WeenieHeaderFlags.None, Name = "door", WeenieClassId = 412, IconId = 0x06000001, ItemType = 0, ObjectDescriptionFlags = flags };
        var physics = new PhysicsDesc { Flags = PhysicsDescFlags.None, State = state, Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1), SetupId = setup, Scale = scale };
        var o = new WorldObject(new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isSelf: false, createdAt: 0) { State = state };
        if (placed)
        {
            o.Position = new CellFrame(0xA9B40000, new AcFrame(new Vector3(132.56f, 17.115f, 94f), Quaternion.Identity));
        }

        return o;
    }

    private sealed class View(ActionHarness h) : IGameSessionView
    {
        public GameState State => h.State;

        public IEventBus Events => h.R.Bus;

        public IActionApi Actions => h.Api;

        public PlayerTools Tools => null!;

        public SessionClock Clock => h.R.Clock;

        public ActionLog ActionLog => h.ActionLog;

        public LinkStats Link => default;
    }

    [Fact]
    public void The_feed_lists_the_placed_objects_with_the_door_bit_and_reads_open_from_ethereal_and_locked_from_the_servers_word()
    {
        var h = new ActionHarness(offline: true);
        h.State.Objects.Set(Make(Door, DoorCrossings.DoorFlag, PhysicsState.None, setup: 0x02000A01, scale: 2f));
        h.State.Objects.Set(Make(0x7A9B4016, DoorCrossings.DoorFlag, PhysicsState.None, placed: false)); // no position yet
        h.State.Objects.Set(Make(0x7A9B4017, 0x10, PhysicsState.None)); // a creature, not a door
        var feed = new SessionDoorFeed(new View(h), setupExtent: id => id == 0x02000A01 ? 0.9f : null);
        var doors = new List<NavDoor>();

        feed.Fill(doors);

        var door = Assert.Single(doors);
        Assert.Equal(Door, door.Guid);
        Assert.False(door.Open);
        Assert.Null(door.Locked);
        Assert.Equal(1.8f, door.Radius, 0.001f); // the selection sphere's radius times the scale
        Assert.True(feed.TryGet(Door, out _));
        Assert.False(feed.TryGet(0x7A9B4017, out _));

        h.State.Objects.TryGet(Door, out var o);
        o!.State = PhysicsState.Ethereal | PhysicsState.Gravity;
        o.Locked = true;
        Assert.True(feed.TryGet(Door, out var now));
        Assert.True(now.Open);
        Assert.True(now.Locked);
    }

    [Fact]
    public void The_feeds_version_moves_when_a_door_joins_or_leaves_the_table_and_not_for_other_objects_or_a_state_change()
    {
        var h = new ActionHarness(offline: true);
        var feed = new SessionDoorFeed(new View(h));
        var start = feed.Version;
        Assert.Equal(start, feed.Version);

        h.State.Objects.Set(Make(0x7A9B4017, 0x10, PhysicsState.None)); // a creature
        Assert.Equal(start, feed.Version);

        h.State.Objects.Set(Make(Door, DoorCrossings.DoorFlag, PhysicsState.None));
        var withDoor = feed.Version;
        Assert.NotEqual(start, withDoor);
        Assert.Equal(withDoor, feed.Version);

        h.State.Objects.TryGet(Door, out var o);
        o!.State = PhysicsState.Ethereal; // opening is not a new door
        Assert.Equal(withDoor, feed.Version);

        h.State.Objects.Set(Make(0x7A9B4016, DoorCrossings.DoorFlag, PhysicsState.None, placed: false)); // no position yet: not a door the feed knows
        Assert.Equal(withDoor, feed.Version);
    }

    [Fact]
    public void A_door_whose_extent_cannot_be_read_assumes_the_fallback_and_logs_it_once()
    {
        var h = new ActionHarness(offline: true);
        h.State.Objects.Set(Make(Door, DoorCrossings.DoorFlag, PhysicsState.None));
        h.State.Objects.Set(Make(0x7A9B4016, DoorCrossings.DoorFlag, PhysicsState.None));
        var log = new RecordingLogSink();
        var feed = new SessionDoorFeed(new View(h), log);
        var doors = new List<NavDoor>();

        feed.Fill(doors);
        feed.Fill(doors);

        Assert.All(doors, d => Assert.Equal(DoorCrossings.UnknownRadiusMetres, d.Radius));
        Assert.Single(log.Lines, l => l.Contains("unread door extent", StringComparison.Ordinal));
    }

    [Fact]
    public void The_feeds_use_goes_out_as_an_automation_use_of_the_door()
    {
        var h = new ActionHarness(offline: true);
        var feed = new SessionDoorFeed(new View(h));

        var handle = feed.Use(Door);

        Assert.NotNull(handle);
        Assert.Equal(ActionKind.Use, handle!.Kind);
        Assert.Equal(ActionSource.Automation, handle.Source);
    }

    [Fact]
    public void The_locked_sound_of_a_used_door_marks_it_locked_read_from_the_refused_use_and_another_objects_sound_does_not()
    {
        var h = new ActionHarness(offline: true);
        h.State.Objects.Set(Make(Door, DoorCrossings.DoorFlag, PhysicsState.None));
        h.State.Objects.Set(Make(0x7A9B4017, 0x10, PhysicsState.None));
        var feed = new SessionDoorFeed(new View(h));

        _ = feed.Use(Door);
        h.R.Bus.Publish(new ObjectSoundPlayed(0x7A9B4017, 0x94, 1f)); // not a door
        h.R.Bus.Publish(new ObjectSoundPlayed(Door, 0x12, 1f)); // another sound
        Assert.True(feed.TryGet(Door, out var before));
        Assert.Null(before.Locked);

        h.R.Bus.Publish(new ObjectSoundPlayed(Door, 0x94, 1f));

        Assert.True(feed.TryGet(Door, out var door));
        Assert.True(door.Locked);
        Assert.Equal("refused-use", door.LockedFrom);
    }

    [Fact]
    public void The_watch_says_yes_only_for_a_door_the_walk_itself_used_and_that_then_opened()
    {
        var h = new ActionHarness(offline: true);
        h.State.Objects.Set(Make(Door, DoorCrossings.DoorFlag, PhysicsState.None));
        h.State.Objects.Set(Make(0x7A9B4017, 0x10, PhysicsState.None));
        var view = new View(h);
        var feed = new SessionDoorFeed(view);
        using var watch = new DoorWatch(view);
        Assert.Equal("no", watch.Field);

        h.R.Bus.Publish(new ObjectStateChanged(0x7A9B4017, false, PhysicsState.None, PhysicsState.Ethereal)); // a creature going ethereal is no door
        Assert.Equal(0, watch.Opened);
        h.R.Bus.Publish(new ObjectStateChanged(Door, false, PhysicsState.None, PhysicsState.Ethereal));
        Assert.Equal("no", watch.Field); // opened, but the walk never Used it: someone else's door (or ACE's)
        Assert.Equal(0, watch.Opened);

        _ = feed.Use(Door); // the walk's own Use (the runner goes through the feed)
        h.Tick();
        Assert.Equal(1, watch.WalkUses);
        Assert.Equal(1, watch.Opened);
        Assert.Equal("yes", watch.Field);

        h.R.Bus.Publish(new ObjectStateChanged(Door, false, PhysicsState.Ethereal, PhysicsState.None)); // closing again is not an opening
        h.R.Bus.Publish(new ObjectStateChanged(Door, false, PhysicsState.None, PhysicsState.Ethereal)); // and the same door reopening counts once
        Assert.Equal(1, watch.Opened);
    }

    [Fact]
    public void Another_door_opening_while_the_vendor_is_used_reads_no_because_neither_was_the_walks_door()
    {
        var h = new ActionHarness(offline: true);
        h.State.Objects.Set(Make(Door, DoorCrossings.DoorFlag, PhysicsState.None)); // the door the walk Uses
        h.State.Objects.Set(Make(0x7A9B4018, DoorCrossings.DoorFlag, PhysicsState.None)); // another lane's, or ACE's
        var view = new View(h);
        var feed = new SessionDoorFeed(view);
        using var watch = new DoorWatch(view);

        h.R.Bus.Publish(new ObjectStateChanged(0x7A9B4018, false, PhysicsState.None, PhysicsState.Ethereal));
        _ = view.Actions.Use(0x7A9B4017, ActionSource.Automation); // the vendor's own Use: not through the door feed
        h.Tick();
        Assert.Equal("no", watch.Field);

        _ = feed.Use(Door);
        Assert.Equal("no", watch.Field); // the walk Used a door, but it has not opened, and the other door never counts
        h.R.Bus.Publish(new ObjectStateChanged(Door, false, PhysicsState.None, PhysicsState.Ethereal));
        Assert.Equal("yes", watch.Field);
        Assert.Equal(1, watch.Opened);
    }

    [Fact]
    public void A_use_before_the_watch_began_does_not_count_and_a_watch_of_another_session_is_its_own()
    {
        var h = new ActionHarness(offline: true);
        h.State.Objects.Set(Make(Door, DoorCrossings.DoorFlag, PhysicsState.None));
        var view = new View(h);
        _ = new SessionDoorFeed(view).Use(Door); // an earlier trip's Use
        using var watch = new DoorWatch(view);
        h.R.Bus.Publish(new ObjectStateChanged(Door, false, PhysicsState.None, PhysicsState.Ethereal));
        Assert.Equal("no", watch.Field);

        var other = new ActionHarness(offline: true);
        _ = new SessionDoorFeed(new View(other)).Use(Door); // another session's walk Uses the same guid
        Assert.Equal("no", watch.Field);
        Assert.Equal(0, watch.WalkUses);
    }

    /// <summary>The bus of <see cref="ActionHarness"/> with the live subscriptions counted.</summary>
    private sealed class CountingBus(IEventBus inner) : IEventBus
    {
        public int Live { get; private set; }

        public IDisposable Subscribe<T>(BusHandler<T> handler)
            where T : struct, IGameEvent
        {
            Live++;
            return new Gone(this, inner.Subscribe(handler));
        }

        public IDisposable SubscribeRaw(RawMessageHandler handler) => inner.SubscribeRaw(handler);

        public long Count<T>()
            where T : struct, IGameEvent => inner.Count<T>();

        private sealed class Gone(CountingBus owner, IDisposable subscription) : IDisposable
        {
            private bool _done;

            public void Dispose()
            {
                if (!_done)
                {
                    _done = true;
                    owner.Live--;
                }

                subscription.Dispose();
            }
        }
    }

    private sealed class CountedView(ActionHarness h, CountingBus bus) : IGameSessionView
    {
        public GameState State => h.State;

        public IEventBus Events => bus;

        public IActionApi Actions => h.Api;

        public PlayerTools Tools => null!;

        public SessionClock Clock => h.R.Clock;

        public ActionLog ActionLog => h.ActionLog;

        public LinkStats Link => default;
    }

    [Fact]
    public void Two_vendor_trips_leave_one_door_watch_subscribed_and_the_hunts_door_field_reads_the_walks_door()
    {
        var h = new ActionHarness(offline: true);
        h.State.Objects.Set(Make(Door, DoorCrossings.DoorFlag, PhysicsState.None));
        var bus = new CountingBus(h.R.Bus);
        var view = new CountedView(h, bus);
        var hunt = new HuntScenario(new HuntOptions(), new RecordingLogSink(), _ => 40f);
        Assert.Equal("no", hunt.DoorOpenedField); // no trip yet

        hunt.BeginDoorWatch(view);
        hunt.BeginDoorWatch(view); // the autopilot's next trip
        hunt.BeginDoorWatch(view);

        Assert.Equal(1, bus.Live);
        h.R.Bus.Publish(new ObjectStateChanged(Door, false, PhysicsState.None, PhysicsState.Ethereal));
        Assert.Equal("no", hunt.DoorOpenedField); // ACE opened it; the walk did not Use it
        _ = new SessionDoorFeed(view).Use(Door);
        Assert.Equal("yes", hunt.DoorOpenedField);
    }
}
