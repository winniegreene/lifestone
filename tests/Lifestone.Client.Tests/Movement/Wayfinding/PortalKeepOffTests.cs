using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>M6 7 AI: the rule of <see cref="PortalKeepOff"/>, no dats: which steps meet a portal's disc, how the room graph and the runner read a list, and that a walk that cannot keep clear ends by name.</summary>
public sealed class PortalKeepOffTests
{
    private const float Clearance = 2.5f;
    private const float RoomFillingMetres = 6f;

    private static readonly Vector2 Gate = new(100f, 100f);

    private static readonly Vector3 RingStart = new(93f, 99f, 0f);
    private static readonly Vector3 RingGoal = new(103f, 109f, 0f);

    private static Vector2 Global(float x, float y) => new NavPoint(IndoorRoomKit.Block.LandBlockId, new Vector3(x, y, 0f)).Global;

    private static PortalKeepOff One(bool outdoor = false) => new([new KeptPortal("Gate", Gate, 0f, outdoor)], Clearance);

    /// <summary>A list of a room's portals, with a disc that fills a 10 m room: the ring's walks cannot go round it inside the room.</summary>
    private static PortalKeepOff Indoors(params (string Name, float X, float Y)[] portals) =>
        new(portals.Select(p => new KeptPortal(p.Name, Global(p.X, p.Y), 0f, Outdoor: false)), RoomFillingMetres);

    private static CellFrame At(uint cell, Vector3 point) => new(cell, new AcFrame(point, Quaternion.Identity));

    private static NavWaypoint Way(float x, float y, float z = 0f) => new(At(0x40400001u, new Vector3(x, y, z)), Doorway: false);

    /// <summary>The number the walks keep: the two spheres' reach from above and what a body strays.</summary>
    [Fact]
    public void The_clearance_is_the_touch_and_the_wobble()
    {
        Assert.Equal(1.837f, PortalKeepOff.TouchMetres, 3);
        Assert.Equal(PortalKeepOff.TouchMetres + PortalKeepOff.WobbleMetres, PortalKeepOff.ClearanceMetres, 3);
    }

    [Theory]
    [InlineData(90f, 100f, 110f, 100f, true)] // through the middle
    [InlineData(90f, 102f, 110f, 102f, true)] // inside the clearance, past the middle
    [InlineData(90f, 100f, 99f, 100f, true)] // ends in the disc
    [InlineData(90f, 103f, 110f, 103f, false)] // passes outside
    [InlineData(90f, 100f, 96f, 100f, false)] // stops short
    [InlineData(101f, 100f, 110f, 100f, false)] // starts in the disc and leaves it
    [InlineData(99f, 100f, 110f, 100f, true)] // starts in the disc and crosses it
    public void A_step_meets_a_disc_it_enters_or_crosses_and_not_one_it_leaves(float ax, float ay, float bx, float by, bool meets)
    {
        Assert.Equal(meets, One().FirstMeeting(new Vector2(ax, ay), 0f, new Vector2(bx, by), 0f) is not null);
    }

    [Theory]
    [InlineData(2.9f, true)]
    [InlineData(6f, false)]
    [InlineData(-6f, false)]
    public void A_portal_counts_at_its_own_storey(float z, bool meets)
    {
        Assert.Equal(meets, One().FirstMeeting(new Vector2(90f, 100f), z, new Vector2(110f, 100f), z) is not null);
    }

    [Fact]
    public void The_whole_line_names_the_portal_and_the_leg()
    {
        var keep = new PortalKeepOff([new KeptPortal("Gate", Global(100f, 100f), 0f, false)], Clearance);
        var met = keep.FirstMeeting([Way(90f, 90f), Way(90f, 100f), Way(110f, 100f)]);
        Assert.Equal(("Gate", 2), (met!.Value.Portal.Name, met.Value.Leg));
        Assert.Null(keep.FirstMeeting([Way(90f, 90f), Way(90f, 110f), Way(110f, 110f)]));
    }

    [Fact]
    public void The_nearest_reads_the_storey_and_the_near_list_drops_what_a_walk_cannot_meet()
    {
        var keep = new PortalKeepOff([new KeptPortal("Low", Gate, 0f, false), new KeptPortal("High", new Vector2(100f, 104f), 6f, true), new KeptPortal("Far", new Vector2(900f, 100f), 0f, true)], Clearance);
        Assert.Equal(("Low", 5f), keep.Nearest(new Vector2(105f, 100f), 0f) is { } low ? (low.Portal.Name, low.Metres) : default);
        Assert.Equal("High", keep.Nearest(new Vector2(100f, 105f), 6f)!.Value.Portal.Name);
        Assert.Null(keep.Nearest(new Vector2(100f, 105f), 40f));
        Assert.Equal(3f, new PortalKeepOff([new KeptPortal("Gate", Global(100f, 100f), 0f, false)], Clearance).NearestOnLine([Way(97f, 90f), Way(97f, 110f)])!.Value.Metres, 3);
        Assert.Equal(["Low", "High"], keep.Near(new Vector2(0f, 100f), new Vector2(200f, 100f), 192f).Portals.Select(p => p.Name));
        Assert.Equal(2, keep.OutdoorDiscs.Count);
        Assert.All(keep.OutdoorDiscs, d => Assert.Equal(Clearance, d.Radius));
    }

    /// <summary>The lattice reads a portal's disc as a scar, and a plan's bodies leave it standing.</summary>
    [Fact]
    public void The_scars_hold_the_portals_discs_past_the_bodies()
    {
        var scars = new NavScars();
        scars.SetKeepOff(One(outdoor: true).OutdoorDiscs);
        var buffer = new NavScar[1];
        Assert.Equal(1, scars.CopyLive(ref buffer));
        Assert.True(scars.IsScarred(Gate + Vector2.UnitX, scars.Now));
        scars.SetBodies([(new Vector2(0f, 0f), 1f)]);
        Assert.Equal(2, scars.CopyLive(ref buffer));
        scars.ClearBodies();
        Assert.True(scars.IsScarred(Gate, scars.Now));
        Assert.Equal(0, scars.LiveCount); // not a timed scar
        scars.Clear();
        Assert.False(scars.IsScarred(Gate, scars.Now));
    }

    /// <summary>A kept portal is no cost but a wall to the plan: the ring's shorter way runs through it, so the plan takes the other, the walk's own end stays reachable, and a null list is today's plan.</summary>
    [Fact]
    public void The_room_graph_goes_round_a_kept_portal_by_the_other_way()
    {
        var cells = IndoorRoomKit.Ring();
        var bare = new RoomGraph(cells).Plan(IndoorRoomKit.RingSouthWest, RingStart, IndoorRoomKit.RingNorthEast, RingGoal)!;
        Assert.Equal([IndoorRoomKit.RingSouthWest, IndoorRoomKit.RingNorthWest, IndoorRoomKit.RingNorthEast], bare.Cells);

        var graph = new RoomGraph(cells) { KeepOff = Indoors(("Neighbour", 96f, 106f)) };
        var round = graph.Plan(IndoorRoomKit.RingSouthWest, RingStart, IndoorRoomKit.RingNorthEast, RingGoal)!;
        Assert.Equal([IndoorRoomKit.RingSouthWest, IndoorRoomKit.RingSouthEast, IndoorRoomKit.RingNorthEast], round.Cells);

        graph.KeepOff = null;
        Assert.Equal(bare.Waypoints, graph.Plan(IndoorRoomKit.RingSouthWest, RingStart, IndoorRoomKit.RingNorthEast, RingGoal)!.Waypoints);
    }

    [Fact]
    public void A_goal_inside_a_kept_portal_s_disc_has_no_plan()
    {
        var graph = new RoomGraph(IndoorRoomKit.Ring()) { KeepOff = Indoors(("Neighbour", RingGoal.X, RingGoal.Y)) };
        Assert.Null(graph.Plan(IndoorRoomKit.RingSouthWest, RingStart, IndoorRoomKit.RingNorthEast, RingGoal));
    }

    /// <summary>A body that stands beside a portal (an arrival) walks away from it: the disc holds the start and the first step leaves it.</summary>
    [Fact]
    public void A_start_inside_a_disc_walks_out_of_it()
    {
        var graph = new RoomGraph(IndoorRoomKit.Ring()) { KeepOff = Indoors(("Beside", RingStart.X - 1f, RingStart.Y - 1f)) };
        Assert.NotNull(graph.Plan(IndoorRoomKit.RingSouthWest, RingStart, IndoorRoomKit.RingNorthEast, RingGoal));
    }

    /// <summary>Both ways round closed: the run ends Blocked and names the portal, never a walk through it. Without the list the same walk arrives.</summary>
    [Fact]
    public void A_walk_with_no_way_round_a_portal_fails_by_name()
    {
        var cells = IndoorRoomKit.Ring();
        var rig = new IndoorRig(cells, 0f, IndoorRoomKit.RingSouthWest, new Vector2(RingStart.X, RingStart.Y), heading: 0f);
        rig.Runner.KeepOff = Indoors(("North Neighbour", 96f, 106f), ("East Neighbour", 106f, 96f));
        rig.Runner.Start(NavGoal.ToPoint(new NavPoint(IndoorRoomKit.Block.LandBlockId, RingGoal), IndoorRoomKit.RingNorthEast), rig.Body.Now);
        rig.Frame();

        Assert.Equal(NavRunState.Blocked, rig.Runner.State);
        Assert.Contains("no path: " + NavRunner.PortalKeepOffFailure + ":North_Neighbour", rig.Runner.FailureReason, StringComparison.Ordinal);
        Assert.False(rig.Body.Holds(MotionCommands.WalkForward));
        Assert.NotNull(rig.Graph.KeepOff); // the plan made to name it gave the list back

        var free = new IndoorRig(cells, 0f, IndoorRoomKit.RingSouthWest, new Vector2(RingStart.X, RingStart.Y), heading: 0f);
        free.Runner.Start(NavGoal.ToPoint(new NavPoint(IndoorRoomKit.Block.LandBlockId, RingGoal), IndoorRoomKit.RingNorthEast), free.Body.Now);
        free.RunFor(20);
        Assert.Equal(NavRunState.Arrived, free.Runner.State);
        Assert.DoesNotContain("keep_off", free.Runner.VerdictFields(), StringComparison.Ordinal);
    }

    /// <summary>The run says how near the body came, on the line the gate's watcher reads.</summary>
    [Fact]
    public void The_run_reports_the_nearest_the_body_came()
    {
        var cells = IndoorRoomKit.Ring();
        var rig = new IndoorRig(cells, 0f, IndoorRoomKit.RingSouthWest, new Vector2(RingStart.X, RingStart.Y), heading: 0f);
        rig.Runner.KeepOff = Indoors(("Neighbour", 96f, 106f));
        rig.Runner.Start(NavGoal.ToPoint(new NavPoint(IndoorRoomKit.Block.LandBlockId, RingGoal), IndoorRoomKit.RingNorthEast), rig.Body.Now);
        rig.RunFor(20);

        Assert.Equal(NavRunState.Arrived, rig.Runner.State);
        Assert.Contains("keep_off_nearest_m=", rig.Runner.VerdictFields(), StringComparison.Ordinal);
        Assert.True(rig.Runner.KeepOffNearestMetres >= RoomFillingMetres - 1f, rig.Runner.VerdictFields());
    }

    /// <summary>A portal's list holds every other portal the travel data knows and never itself, whatever record names it: the walk's own end is not refused.</summary>
    [Fact]
    public void A_portal_s_list_leaves_itself_out()
    {
        var routes = TravelRoutes.Default;
        var lodge = RungPortal.Of(routes.Edge("hub-lodge"), routes);
        var names = lodge.KeepOffTheOthers().Portals.Select(p => p.Name).ToList();
        Assert.DoesNotContain(lodge.Name, names);
        Assert.Contains(routes.Edge("hub-burrow").Name, names);
        Assert.Contains(routes.Edge("facility-hub-to-eastham").Name, names);
        Assert.Equal(RungPortal.AllKnown.Count - 1, names.Count);

        // Each record is one object: a run target named by several routes is listed once.
        var global = RungPortal.AllKnown.Select(p => p.Global).ToList();
        Assert.Equal(global.Count, global.Distinct().Count());
    }
}
