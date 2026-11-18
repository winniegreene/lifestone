using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Tests.Movement;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 7 AI, on the retail dats: the walk from the Facility Hub's drop to the front of each of the eight hub portals stays clear of the other portals. Gate A's climb, level 15, took the Tusker Burrow portal on touch on its way to the Tusker Lodge's: the plan ran 0.74 m past it, and from level 15 every
/// portal of the ring takes a body that touches it (<see cref="PortalKeepOff"/>). The fronts are the hop's own (<see cref="TravelLeg.FirstFront"/>, <see cref="TravelLeg.FacingPastPortal"/>, <see cref="TravelLeg.FrontRoomIn"/>).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class HubPortalKeepOffDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Drop = 0x8A020212;
    private const double Fps = 60.0;
    private const double WalkSeconds = 120.0;

    /// <summary>The margin a body keeps outside the largest touch at its nearest (measured: 1.13 m, <see cref="PortalKeepOff.WobbleMetres"/>).</summary>
    private const float MarginMetres = 1f;

    /// <summary>The Setups of the portals of the travel data (the pinned world SQL, weenie_properties_d_i_d type 1; <c>TravelRoutesSqlTests</c> checks every weenie against this list).</summary>
    internal static readonly uint[] PortalSetups = [0x020001B3, 0x020005D2, 0x020005D3, 0x020005D5, 0x020005D6];

    private static TravelRoutes Routes => TravelRoutes.Default;

    private DatSet Dats => fixture.Dats;

    private static CellFrame DropFrame => new(Drop, new AcFrame(Routes.Edge("network-to-facility-hub").DestOrigin, Quaternion.Identity));

    /// <summary>The eight portals of the hub's ring.</summary>
    public static TheoryData<string> Ring() => new(Routes.Edges.Where(e => e.From == "facility-hub" && e.Key.StartsWith("hub-", StringComparison.Ordinal)).Select(e => e.Key));

    /// <summary>Each portal of the ring at a slow, a middling and a fast Run skill.</summary>
    public static TheoryData<string, int> RingAndSkills()
    {
        var data = new TheoryData<string, int>();
        foreach (var e in Routes.Edges.Where(e => e.From == "facility-hub" && e.Key.StartsWith("hub-", StringComparison.Ordinal)))
        {
            foreach (var skill in new[] { 60, 230, 450 })
            {
                data.Add(e.Key, skill);
            }
        }

        return data;
    }

    /// <summary>The numbers <see cref="PortalKeepOff"/> is worked from, read from the dats: a portal's collision is one sphere, its radius within <see cref="PortalKeepOff.PortalSphereMetres"/>; the body's two spheres are <see cref="PortalKeepOff.BodyRadiusMetres"/>.</summary>
    [RequiresDatsFact]
    public void The_touch_is_the_two_spheres_of_the_dats()
    {
        foreach (var id in PortalSetups)
        {
            Assert.True(Dats.TryGetPortal<DatReaderWriter.DBObjs.Setup>(id, out var setup));
            var model = ModelAssembly.FromSetup(id, setup);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"0x{id:X8}: spheres {string.Join(", ", model.Spheres.Select(s => $"r {s.Radius} at {s.Center}"))}; cylinders {model.CylSpheres.Count}"));
            Assert.Empty(model.CylSpheres);
            var sphere = Assert.Single(model.Spheres);
            Assert.InRange(sphere.Radius, 0.1f, PortalKeepOff.PortalSphereMetres);
            Assert.Equal(0f, new Vector2(sphere.Center.X, sphere.Center.Y).Length(), 3);
        }

        Assert.True(Dats.TryGetPortal<DatReaderWriter.DBObjs.Setup>(OfflineWalker.HumanSetupId, out var human));
        var body = ModelAssembly.FromSetup(OfflineWalker.HumanSetupId, human);
        Assert.All(body.Spheres, s => Assert.Equal(PortalKeepOff.BodyRadiusMetres, s.Radius, 3));
    }

    /// <summary>The hop's front point from <paramref name="from"/>: the same choices as <see cref="TravelLeg.HopThrough"/> in the same order.</summary>
    private static (uint Cell, Vector3 At) Front(JourneyLegPlanner planner, RungPortal portal, in CellFrame from)
    {
        var (cell, front, _, _) = TravelLeg.HopFront(portal, portal.Cell, from, portal.KeepOffForFront(), (c, a) => LandDefs.IsOutdoorCell(c) || TravelLeg.FrontRoomIn(planner.CellsOf(c), c, a) is { Moved: 0f }); // the hop's own choice (M6 14 m6-14-plaza: a front in a neighbour's clearance is turned round the portal)

        return TravelLeg.FrontRoomIn(planner.CellsOf(cell), cell, front) is { } room ? (room.Cell, room.At) : (cell, front);
    }

    private (JourneyLegPlanner Planner, NavMap Map, HeadlessStreamer Streamer) Hub(in CellFrame from, PhysicsWorld world)
    {
        var map = new NavMap();
        var streamer = new HeadlessStreamer(Dats, null, world, map, NullLogSink.Instance);
        streamer.EnsureNow(from.CellId);
        return (new JourneyLegPlanner(Dats), map, streamer);
    }

    private static NavPath PlanFront(JourneyLegPlanner planner, NavMap map, in CellFrame from, RungPortal portal, PortalKeepOff? keep)
    {
        var (cell, at) = Front(planner, portal, from);
        var goal = NavGoal.ToPoint(new NavPoint(cell & 0xFFFF0000u, at), cell);
        var source = planner.SourceFor(from.CellId, JourneyLegPlanner.GoalCellOf(goal), map);
        ((INavRoomSource)source).Rooms.KeepOff = keep;
        return source.Plan(from, goal, new NavScars());
    }

    /// <summary>Each route's whole line stays the clearance away from every other portal the travel data knows: the seven of the ring and the hub's way out to Eastham.</summary>
    /// <param name="key">The hub edge.</param>
    [RequiresDatsTheory]
    [MemberData(nameof(Ring))]
    public void The_plan_to_a_hub_portal_s_front_keeps_the_clearance_from_the_others(string key)
    {
        var portal = RungPortal.Of(Routes.Edge(key), Routes);
        var keep = portal.KeepOffTheOthers();
        var from = DropFrame;
        var (planner, map, streamer) = Hub(from, new PhysicsWorld());
        using (streamer)
        {
            var path = PlanFront(planner, map, from, portal, keep);
            Assert.Equal(NavPathStatus.Found, path.Status);
            var met = keep.FirstMeeting(path.Waypoints);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{key}: {path.Waypoints.Count} waypoints; {(met is { } m ? $"meets {m.Portal.Name} on leg {m.Leg}" : "clear")}"));
            Assert.True(met is null, $"{key}: the plan comes within {keep.Clearance:0.00} m of {met?.Portal.Name}");
            var end = path.Waypoints[^1].At;
            Assert.Equal(Front(planner, portal, from).At, end.Origin);
        }
    }

    /// <summary>
    /// A body, not a line: run at a slow, a middling and a fast Run skill from the drop to the front and on to the portal's 2 m, as the hop does, the walk arrives and stands a metre outside the largest touch at its nearest; the margin is printed: what <see cref="PortalKeepOff.WobbleMetres"/> is measured from.
    /// </summary>
    /// <param name="key">The hub edge.</param>
    /// <param name="runSkill">The body's Run skill.</param>
    [RequiresDatsTheory]
    [MemberData(nameof(RingAndSkills))]
    public void The_body_stays_outside_the_touch_of_the_other_portals(string key, int runSkill)
    {
        var portal = RungPortal.Of(Routes.Edge(key), Routes);
        var keep = portal.KeepOffTheOthers();
        using var rig = new FacilityHubWalkDatTests.Rig(Dats, DropFrame, runSkill);
        var (cell, at) = Front(rig.Planner, portal, rig.Position);
        var end = rig.Run(NavGoal.ToPoint(new NavPoint(cell & 0xFFFF0000u, at), cell), key, keep);
        output.WriteLine(end.Line);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{key} run skill {runSkill}: nearest {end.Nearest:0.00} m; inside the clearance by {keep.Clearance - end.Nearest:0.00} m; margin to the touch {end.Nearest - PortalKeepOff.TouchMetres:0.00} m"));
        Assert.True(end.Arrived, end.Line);
        Assert.True(end.Nearest >= PortalKeepOff.TouchMetres + MarginMetres, $"{key}: the body came {end.Nearest:0.00} m from a portal, the touch {PortalKeepOff.TouchMetres:0.00} m and the margin {MarginMetres:0.00} m");
        var seek = rig.Run(rig.SeekGoal(portal), key + " seek", keep);
        output.WriteLine(seek.Line);
        Assert.True(seek.Arrived, seek.Line);
        Assert.True(seek.Nearest >= PortalKeepOff.TouchMetres + MarginMetres, seek.Line);
    }

    /// <summary>
    /// M6 14 (portal overshoot, live 2026-11-13 19:20:58 UTC), on the dats: the walk from the hub's drop to the Tusker Barracks' approach-side front (78.8, -42.8). The rooms come in from the north, the portal's facing side, so without its own portal on the
    /// list the plan's last step passes 0.72 m from the portal's centre, inside its touch (pinned: the live walk, which the server's sweep of her reports took through the portal). With it (<see cref="RungPortal.KeepOffForFront"/>, the reach
    /// the runner sets: the touch and her stop) the plan keeps the reach, and the real body, run at a slow, a middling and a fast Run skill, arrives at the front without coming within the touch.
    /// </summary>
    /// <param name="runSkill">The body's Run skill.</param>
    [RequiresDatsTheory]
    [InlineData(60)]
    [InlineData(230)]
    [InlineData(450)]
    public void The_walk_to_the_Barracks_front_keeps_off_its_own_portal(int runSkill)
    {
        var portal = RungPortal.Of(Routes.Edge("hub-barracks"), Routes);
        var own = portal.KeepOffForFront();
        var reach = PortalKeepOff.OwnReach(NavRunner.StopMetres(NavGoal.DefaultStopTolerance));
        var self = own.Own!.Value;
        using var rig = new FacilityHubWalkDatTests.Rig(Dats, DropFrame, runSkill);
        var (cell, at) = Front(rig.Planner, portal, rig.Position);
        Assert.True(Vector2.Distance(new NavPoint(cell & 0xFFFF0000u, at).Global, self.Global) >= reach, "the hop's front stands outside its own portal's reach");

        var bare = PlanFront(rig.Planner, rig.Map, rig.Position, portal, portal.KeepOffTheOthers());
        var bareLine = own.NearestOnLine(bare.Waypoints, self);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"without its own portal: the plan passes {bareLine:0.00} m from it (touch {PortalKeepOff.TouchMetres:0.00} m)"));
        Assert.True(bareLine < PortalKeepOff.TouchMetres, "the live plan ran through its own portal's touch");

        var kept = PlanFront(rig.Planner, rig.Map, rig.Position, portal, own.WithOwnClearance(reach));
        Assert.Equal(NavPathStatus.Found, kept.Status);
        var keptLine = own.NearestOnLine(kept.Waypoints, self);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"with it: {kept.Waypoints.Count} waypoints, nearest {keptLine:0.00} m (reach {reach:0.00} m)"));
        Assert.True(keptLine >= reach - 0.01f, $"the plan keeps the reach: {keptLine:0.00} of {reach:0.00} m");

        var end = rig.Run(NavGoal.ToPoint(new NavPoint(cell & 0xFFFF0000u, at), cell), "hub-barracks front", own);
        output.WriteLine(end.Line);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"run skill {runSkill}: the body came {end.OwnNearest:0.00} m from its own portal (touch {PortalKeepOff.TouchMetres:0.00} m)"));
        Assert.True(end.Arrived, end.Line);
        Assert.True(end.OwnNearest >= PortalKeepOff.TouchMetres, end.Line);
        Assert.True(end.Nearest >= PortalKeepOff.TouchMetres + MarginMetres, end.Line);
    }

    /// <summary>The list costs no walk the travel data has: from every arrival in the Town Network and the hub, the walk to the front of every portal there is planned clear of the others, and stops in the room graph's own reach (no plan refused).</summary>
    /// <param name="landblock">The landblock.</param>
    [RequiresDatsTheory]
    [InlineData(0x0007)]
    [InlineData(0x8A02)]
    public void Every_walk_between_the_arrivals_and_the_portals_of_a_hub_keeps_clear(int landblock)
    {
        var edges = Routes.Edges.Where(e => e.Cell >> 16 == (uint)landblock).ToList();
        var arrivals = Routes.Edges.Where(e => e.DestCell >> 16 == (uint)landblock).Select(e => new CellFrame(e.DestCell, new AcFrame(e.DestOrigin, Quaternion.Identity))).Distinct().ToList();
        Assert.NotEmpty(arrivals);
        var refused = new List<string>();
        var planner = new JourneyLegPlanner(Dats);

        // One graph for all the walks, the leg's own source's lean (a leg builds its own: 55 walks over the Network would take minutes).
        var graph = new RoomGraph(planner.CellsOf(arrivals[0].CellId), RoomGraph.BuildingDoorwayLean);
        var source = new RoomPathSource(graph);
        foreach (var from in arrivals)
        {
            foreach (var e in edges)
            {
                var portal = RungPortal.Of(e, Routes);
                var keep = portal.KeepOffTheOthers();
                var (cell, at) = Front(planner, portal, from);
                graph.KeepOff = keep;
                var path = source.Plan(from, NavGoal.ToPoint(new NavPoint(cell & 0xFFFF0000u, at), cell), new NavScars());
                var line = keep.NearestOnLine(path.Waypoints);
                var text = string.Create(CultureInfo.InvariantCulture, $"0x{from.CellId:X8} to {e.Key}: {path.Status} {path.Reason} n={path.Waypoints.Count} nearest {line?.Metres:0.00} {line?.Portal.Name}");
                output.WriteLine(text);
                if (path.Status != NavPathStatus.Found || keep.FirstMeeting(path.Waypoints) is not null)
                {
                    refused.Add(text);
                }
            }
        }

        Assert.True(refused.Count == 0, string.Join("\n", refused));
    }

    /// <summary>The same plans with no list, today's before this slice: the walk to the Lodge's front runs through the Burrow portal's touch, as the live walk did, and the walks that miss the clearance are printed. Pinned, so the test above cannot pass for want of a brush.</summary>
    [RequiresDatsFact]
    public void Without_the_list_the_walk_to_the_Lodge_runs_through_the_Burrow_portal_s_touch()
    {
        var from = DropFrame;
        var (planner, map, streamer) = Hub(from, new PhysicsWorld());
        using (streamer)
        {
            var missed = new List<string>();
            foreach (var edge in Routes.Edges.Where(e => e.From == "facility-hub" && e.Key.StartsWith("hub-", StringComparison.Ordinal)))
            {
                var key = edge.Key;
                var ringPortal = RungPortal.Of(edge, Routes);
                var keep = ringPortal.KeepOffTheOthers();
                var plan = PlanFront(planner, map, from, ringPortal, keep: null);
                var met = keep.FirstMeeting(plan.Waypoints);
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{key} with no list: {(met is { } m ? $"meets {m.Portal.Name} on leg {m.Leg}" : "clear")}; nearest {keep.NearestOnLine(plan.Waypoints)?.Metres:0.00} m"));
                if (met is not null)
                {
                    missed.Add(key);
                }
            }

            Assert.Contains("hub-lodge", missed);
            var portal = RungPortal.Of(Routes.Edge("hub-lodge"), Routes);
            var path = PlanFront(planner, map, from, portal, keep: null);
            Assert.Equal(NavPathStatus.Found, path.Status);
            var touched = new PortalKeepOff(portal.KeepOffTheOthers().Portals, PortalKeepOff.TouchMetres).FirstMeeting(path.Waypoints);
            Assert.NotNull(touched);
            Assert.Equal(Routes.Edge("hub-burrow").Name, touched.Value.Portal.Name);
        }
    }
}
