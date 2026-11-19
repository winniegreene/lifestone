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
using Lifestone.Data.World.Cells;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 sprint (live 2026-11-19 01:50:56 to 01:52:29 UTC, run 3, and 00:05:32 to 00:07:06 UTC, run 2, <c>m6-scrivener-door-stall</c>): the walk from Archmage Cindrue's hall 0xA9B40117 to the Apprentice Scrivener of Life
/// Magic's stop (0xA9B30100 at (182.70, 85.40, 116.01)) left by the hall's open front door, found the scriveners' shut front door 0x7A9B3005 only after the plan, and stood against the Scrivener of Item Magic at (186.88,
/// 87.66) in 0xA9B3003C for 93 s: 3 plans, 12 rungs, Blocked. This replays the leg on the retail dats with the doors as the server objects they are (the hall's east door and the scriveners' four doors shut, the front
/// door open, as live) and the townspeople the session sees as bodies, in the physics and on the plan.
/// </summary>
/// <remarks>
/// Test data, read-only from the pinned world database (ACE-World-Database-v0.9.296, <c>landblock_instance</c>, <c>weenie_properties_d_i_d</c> type 1): the doors, wcid 412 (Setup 0x020019FF); the Scrivener of Item Magic
/// 0x7A9B301B (Setup 0x02000001) at (187.288, 86.7918, 116.005) in 0xA9B3003C; the Journeyman Scrivener of Item Magic 0x7A9B301A (Setup 0x0200004E) at (183.232, 76.6786, 116.005) in 0xA9B3003C; the Apprentice Scrivener of
/// Item and War Magic 0x7A9B300D (Setup 0x0200004E) at (184.004, 79.7872, 119.005), upstairs; the Royal Guard 0x7A9B46B0 (Setup 0x02000001) at (176.73, 106.375, 65.2775) in 0xA9B4003D. The start and heading are the live
/// leg's first line (01:50:57.143 UTC).
/// </remarks>
[Trait("Tier", "Exhaustive")]
public sealed class ScrivenerDoorStallDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const double Fps = 60.0;
    private const uint Hall = 0xA9B40117;
    private const uint DoorSetupId = 0x020019FF;
    private const uint HumanSetup = 0x02000001;
    private const uint ApprenticeSetup = 0x0200004E;
    private const uint CreaturePhysicsState = 0x200418;
    private static readonly Vector3 LiveStart = new(152.229f, 132.571f, 66.005f);
    private static readonly Quaternion LiveHeading = new(0f, 0f, -0.9568f, -0.2906f);
    private static readonly Vector3 LifeStop = new(182.70f, 85.40f, 116.01f);

    private static readonly LandblockCoord Holtburg = LandblockCoord.Holtburg;
    private static readonly LandblockCoord South = LandblockCoord.Holtburg.Offset(0, -1)!.Value;

    private static readonly (uint Guid, LandblockCoord Block, Vector3 At, Quaternion Turn, bool Open)[] Doors =
    [
        (0x7A9B4009, Holtburg, new(148.525f, 131.611f, 66f), new(0f, 0f, 0.381878f, -0.924213f), true), // the hall's front door: open live ("the door is open: walking through")
        (0x7A9B400A, Holtburg, new(159.73f, 133.93f, 66f), new(0f, 0f, 0.924213f, 0.381878f), false),
        (0x7A9B3004, South, new(180.2f, 77.59f, 116.01f), new(0f, 0f, 0f, 1f), false),
        (0x7A9B3005, South, new(184.695f, 82.545f, 115.995f), new(0f, 0f, 0.707107f, 0.707107f), false),
        (0x7A9B3006, South, new(183.245f, 83.895f, 118.995f), new(0f, 0f, 0f, 1f), false),
        (0x7A9B3007, South, new(182.21f, 80.19f, 119f), new(0f, 0f, 0.707107f, 0.707107f), false),
    ];

    private static readonly (uint Guid, string Name, uint Setup, LandblockCoord Block, Vector3 At)[] People =
    [
        (0x7A9B301B, "Scrivener of Item Magic", HumanSetup, South, new(187.288f, 86.7918f, 116.005f)),
        (0x7A9B301A, "Journeyman Scrivener of Item Magic", ApprenticeSetup, South, new(183.232f, 76.6786f, 116.005f)),
        (0x7A9B300D, "Apprentice Scrivener of Item and War Magic", ApprenticeSetup, South, new(184.004f, 79.7872f, 119.005f)),
        (0x7A9B46B0, "Royal Guard", HumanSetup, Holtburg, new(176.73f, 106.375f, 65.2775f)),
    ];

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void The_live_walk_from_Cindrues_hall_to_the_Life_apprentice_reaches_his_stop()
    {
        var (state, at, log, reason) = Walk("scrivener-door", withBodies: true, seenWithin: null);
        Assert.True(state == NavRunState.Arrived, reason);
    }

    /// <summary>As live: planned from inside the hall, where the client knows only what the hall shows, so the street's doors and people arrive as she nears them (within <paramref name="metres"/>).</summary>
    [RequiresDatsTheory]
    [InlineData(60f)]
    [InlineData(30f)]
    [InlineData(15f)]
    public void Planned_before_the_street_is_known_the_walk_still_reaches_the_Life_apprentice(float metres)
    {
        var (state, at, log, reason) = Walk("scrivener-door-" + metres, withBodies: true, seenWithin: metres);
        Assert.True(state == NavRunState.Arrived, reason);
        Assert.Equal(0xA9B30100u, at.CellId);
        // M6 sprint (m6-npc-ahead; run 5, 14:25:55 to 14:26:09 UTC live: four rungs against her, then the replan why=stuck): the first plan did not know her; the walk replans round her as she comes into sight on the
        // route ahead, before the body meets her: no rung, no stuck replan
        Assert.Equal(0, log.Count(l => l.Contains("stuck: a rung", StringComparison.Ordinal)));
        Assert.Equal(0, log.Count(l => l.Contains("replanned", StringComparison.Ordinal) && l.Contains("why=stuck", StringComparison.Ordinal)));
        var ahead = Assert.Single(log, l => l.Contains("(m6-npc-ahead)", StringComparison.Ordinal));
        Assert.Contains("npc=\"0x7A9B301B:Scrivener of Item Magic\"", ahead, StringComparison.Ordinal);
        Assert.Single(log, l => l.Contains("replanned", StringComparison.Ordinal) && l.Contains("why=npc", StringComparison.Ordinal));
    }

    /// <summary>
    /// The cause, on the planner alone: from the live stall, inside the Scrivener of Item Magic's disc, the replan with her body stamped was the plan without it (due south through her to the door's lane, 8 waypoints): the
    /// planner skipped the disc the start stands in for a step that ended outside it and for every pulled line. Now no leg of the plan comes nearer her centre than the start does.
    /// </summary>
    [RequiresDatsFact]
    public void From_the_stall_the_replan_with_her_body_never_comes_nearer_her_than_the_start()
    {
        var planner = new JourneyLegPlanner(Dats);
        var stall = new CellFrame(0xA9B3003C, new AcFrame(new Vector3(186.881f, 87.663f, 116.005f), new Quaternion(0f, 0f, -0.9955f, 0.0952f)));
        var request = new TravelRequest(new TeleportDestination("Life", [], new WorldLocation("Life", new CellPosition(0xA9B30100, LifeStop), HasHeight: true, HeadingDegrees: 0), "probe"), TravelPurpose.Vendor);
        var goal = planner.GoalOf(new J1JourneyRig().View, request);
        var source = planner.SourceFor(stall.CellId, JourneyLegPlanner.GoalCellOf(goal), Map());
        var radii = HostileCreatureFeed.SetupRadii(Dats);
        var bodies = new NpcBodies();
        var list = People.Select(p => new NavBody(p.Guid, p.Name, NavMap.FromCellFrame(At(p.Block, p.At)).Global, radii(p.Setup) ?? NpcBodyFeed.PersonRadiusMetres, p.At.Z, Outdoor: true)).ToList();
        bodies.Refresh(new Fixed(list), NavMap.FromCellFrame(stall).Global, 300f, 0, goal.Point.Global, null);
        bodies.SelectNear(NavMap.FromCellFrame(stall).Global, goal.Point.Global);
        var scars = new NavScars();
        bodies.Stamp(scars);
        var path = source.Plan(stall, goal, scars);
        Assert.Equal(NavPathStatus.Found, path.Status);
        var her = list.Single(b => b.Guid == 0x7A9B301B).Centre;
        var start = NavMap.FromCellFrame(stall).Global;
        var nearest = Enumerable.Range(0, path.Waypoints.Count).Select(i => NpcBodies.SegmentDistance(her, i == 0 ? start : NavMap.FromCellFrame(path.Waypoints[i - 1].At).Global, NavMap.FromCellFrame(path.Waypoints[i].At).Global)).Min();
        foreach (var w in path.Waypoints)
        {
            output.WriteLine($"  0x{w.At.CellId:X8} ({w.At.Origin.X:F2}, {w.At.Origin.Y:F2}, {w.At.Origin.Z:F2}) doorway={w.Doorway}");
        }

        output.WriteLine($"the start {Vector2.Distance(start, her):F2} m from her centre; the plan's nearest {nearest:F2} m");
        Assert.True(nearest >= Vector2.Distance(start, her) - 0.01f, $"the plan comes {nearest:F2} m from her centre, nearer than the start ({Vector2.Distance(start, her):F2} m)");
    }

    private static CellFrame At(LandblockCoord block, Vector3 point, Quaternion? turn = null) =>
        new(CellIds.LandCellAt(block, point.X, point.Y)!.Value, new AcFrame(point, turn ?? Quaternion.Identity));

    private (NavRunState State, CellFrame At, IReadOnlyList<string> Log, string? Reason) Walk(string name, bool withBodies, float? seenWithin)
    {
        var world = ServerObjectPhysicsTests.HoltburgWorld(Dats);
        var host = new ServerObjectPhysicsHost(world, Dats, log: NullLogSink.Instance);
        var radii = HostileCreatureFeed.SetupRadii(Dats);
        var bodies = new List<NavBody>();
        var pending = new List<(Vector2 Where, Action Arrive)>();
        foreach (var d in Doors)
        {
            var state = (uint)(d.Open ? PhysicsState.ReportCollisions | PhysicsState.Ethereal : PhysicsState.ReportCollisions);
            var frame = At(d.Block, d.At, d.Turn);
            void Arrive() => host.Create(new ServerObjectDescription(d.Guid, DoorSetupId, 1f, state, IsCreature: false, IsSelf: false, DescriptionFlags: 0x1000), frame);
            if (seenWithin is null || d.Block == Holtburg)
            {
                Arrive(); // the hall's own doors are seen from inside it
            }
            else
            {
                pending.Add((NavMap.FromCellFrame(frame).Global, Arrive));
            }
        }

        if (withBodies)
        {
            foreach (var p in People)
            {
                var frame = At(p.Block, p.At);
                void Arrive()
                {
                    host.Create(new ServerObjectDescription(p.Guid, p.Setup, 1f, CreaturePhysicsState, IsCreature: true, IsSelf: false, DescriptionFlags: 0), frame);
                    bodies.Add(new NavBody(p.Guid, p.Name, NavMap.FromCellFrame(frame).Global, radii(p.Setup) ?? NpcBodyFeed.PersonRadiusMetres, p.At.Z, Outdoor: true));
                }

                if (seenWithin is null)
                {
                    Arrive();
                }
                else
                {
                    pending.Add((NavMap.FromCellFrame(frame).Global, Arrive));
                }
            }
        }

        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230));
        var start = new CellFrame(Hall, new AcFrame(LiveStart, LiveHeading));
        var planner = new JourneyLegPlanner(Dats);
        var request = new TravelRequest(new TeleportDestination("Apprentice Scrivener of Life Magic", [], new WorldLocation("Apprentice Scrivener of Life Magic", new CellPosition(0xA9B30100, LifeStop), HasHeight: true, HeadingDegrees: 0), "door stall replay"), TravelPurpose.Vendor);
        var goal = planner.GoalOf(new J1JourneyRig().View, request);
        var source = planner.SourceFor(start.CellId, JourneyLegPlanner.GoalCellOf(goal), Map());
        var doors = new HostDoors(host, DoorCrossings.SetupExtents(Dats)(DoorSetupId)!.Value, [.. Doors.Select(d => d.Guid)]);
        var h = new ActionHarness(offline: true);
        var now = 0.0;
        var log = new RecordingLogSink();
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, name);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.True(walker.Place(start), $"placed at {start}");
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, confirmer, audit, new NavRunnerOptions { Name = name }, log) { Doors = doors, BodyFeed = new Fixed(bodies) };
        runner.Start(goal, now);
        while (!runner.IsDone && now < 180)
        {
            now += 1.0 / Fps;
            doors.Now = now;
            if (pending.Count > 0 && LandDefs.IsOutdoorCell(walker.Position.CellId))
            {
                var here = NavMap.FromCellFrame(walker.Position).Global;
                foreach (var p in pending.Where(p => Vector2.Distance(p.Where, here) <= seenWithin).ToList())
                {
                    p.Arrive(); // the object enters the client's world as she nears it
                    pending.Remove(p);
                }
            }

            walker.Controller.Tick(now);
            host.Tick();
            walker.ReplaceAfterObjectsEntered(host.Stats);
            runner.Step(now);
            h.Tick(1.0 / Fps);
        }

        var lines = log.Entries.Where(e => e.Category == "wayfinding").Select(e => e.Line).ToList();
        foreach (var line in lines.Where(l => !l.Contains("touched", StringComparison.Ordinal)))
        {
            output.WriteLine("  " + line);
        }

        output.WriteLine($"{name}: {runner.State} after {now:F1} s; {runner.VerdictFields()} at 0x{walker.Position.CellId:X8} {walker.Position.Origin}; used {string.Join(" ", doors.Used.Select(g => $"0x{g:X8}"))}; {runner.FailureReason}");
        var result = (runner.State, walker.Position, (IReadOnlyList<string>)lines, runner.FailureReason);
        runner.Dispose();
        return result;
    }

    /// <summary>The terrain tiles of Holtburg and the landblocks round it.</summary>
    private NavMap Map()
    {
        var builder = new LandblockCollisionBuilder(Dats);
        var map = new NavMap();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -2; dy <= 1; dy++)
            {
                map.Attach(NavTileBuilder.Build(builder.Build(Holtburg.Offset(dx, dy)!.Value)));
            }
        }

        return map;
    }

    private sealed class Fixed(List<NavBody> bodies) : INavBodyFeed
    {
        public void Fill(Vector2 from, float maxMetres, List<NavBody> into)
        {
            into.Clear();
            into.AddRange(bodies.Where(b => Vector2.Distance(b.Centre, from) <= maxMetres));
        }
    }
}
