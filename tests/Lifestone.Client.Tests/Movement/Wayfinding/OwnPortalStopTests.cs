using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// M6 14 (portal overshoot): the runner's stop and the walk's own portal, no dats. The stop (<see cref="NavRunner.StopMetres"/>) and the walk zone before it (<see cref="NavRunner.WalkZoneMetres"/>) are derived from the goal's tolerance, her live
/// speed and the frame; a walk to a portal's front keeps that portal off by its touch plus the stop (<see cref="KeptPortal.Own"/>), so it never passes through the portal on the way (live 2026-11-13 19:20 UTC: 0.72 m from the Tusker Barracks' centre)
/// and never stops in its touch.
/// </summary>
public sealed class OwnPortalStopTests
{
    private static readonly Vector3 RingStart = new(93f, 99f, 0f);

    private static Vector2 Global(float x, float y) => new NavPoint(IndoorRoomKit.Block.LandBlockId, new Vector3(x, y, 0f)).Global;

    private static CellFrame At(uint cell, Vector3 point) => new(cell, new AcFrame(point, Quaternion.Identity));

    private static NavWaypoint Way(float x, float y) => new(At(0x40400001u, new Vector3(x, y, 0f)), Doorway: false);

    private static float Reach => PortalKeepOff.OwnReach(NavRunner.StopMetres(NavGoal.DefaultStopTolerance));

    /// <summary>
    /// The stop is the tolerance, one physics step at the walk and the brake's coast; the walk zone is 2 m until her speed and the frame need more (the buffed climber's 7.9 m/s and the live 141 ms slow frame: 2.4 m). The hop's 3 m front stands
    /// outside its own portal's reach (the touch and the stop), so it stays reachable.
    /// </summary>
    [Fact]
    public void The_stop_and_the_walk_zone_come_from_the_tolerance_her_speed_and_the_frame()
    {
        var stop = NavRunner.StopMetres(0.5f);
        Assert.Equal(0.5f + (MotionInterpreter.WalkAnimSpeed * (float)PhysicsGlobals.MinQuantum) + MathF.Max(NavRunner.CornerStopMetres, NavRunner.CornerBrakeSeconds * MotionInterpreter.WalkAnimSpeed), stop, 4);
        Assert.Equal(1.004f, stop, 3);
        Assert.True(NavRunner.StopMetres(1f) > stop); // a wider tolerance stops wider
        Assert.True(PortalKeepOff.OwnReach(stop) < 3f, $"the reach {PortalKeepOff.OwnReach(stop):0.00} m against the hop's 3 m front");

        Assert.Equal(NavRunner.WalkNearStopMetres, NavRunner.WalkZoneMetres(0.5f, 4.1f, 1.0 / 60.0)); // the old climber at 60 fps: the 2 m zone
        Assert.Equal(0.5f + (7.86f * (0.141f + NavRunner.CornerBrakeSeconds)), NavRunner.WalkZoneMetres(0.5f, 7.86f, 0.141), 3);
        Assert.True(NavRunner.WalkZoneMetres(0.5f, 7.86f, 0.141) > NavRunner.WalkNearStopMetres);
        Assert.True(NavRunner.WalkZoneMetres(0.5f, 9f, 0.141) > NavRunner.WalkZoneMetres(0.5f, 7.86f, 0.141)); // faster, wider
    }

    /// <summary>
    /// The own portal is kept off by its reach, not the others' clearance: a step past it at the live plan's 0.72 m meets it, a step that ends at the front 3 m short of it head-on does not (the others' 3.14 m would refuse that end), and the
    /// near list keeps it whatever the line. The nearest-portal reports leave it out (the goal stands beside it on purpose); its own line is read on its own.
    /// </summary>
    [Fact]
    public void The_own_portal_is_kept_off_by_its_touch_and_her_stop()
    {
        var barracks = new KeptPortal("Barracks", new Vector2(80f, -40f), 0f, Outdoor: true, Own: true);
        var lodge = new KeptPortal("Lodge", new Vector2(70f, -40f), 0f, Outdoor: true);
        var keep = new PortalKeepOff([barracks, lodge]).WithOwnClearance(Reach);
        Assert.Equal(Reach, keep.OwnClearance, 4);
        Assert.Equal(barracks, keep.Own);
        Assert.Equal(Reach, keep.ClearanceOf(barracks), 4);
        Assert.Equal(PortalKeepOff.ClearanceMetres, keep.ClearanceOf(lodge), 4);

        Assert.Equal("Barracks", keep.FirstMeeting(new Vector2(80f, -35.5f), 0f, new Vector2(78.82f, -42.76f), 0f)?.Name); // the live plan's last step
        Assert.Null(keep.FirstMeeting(new Vector2(78.82f, -50f), 0f, new Vector2(78.82f, -42.76f), 0f)); // head-on from the front's own side, ending 3 m short
        Assert.NotNull(new PortalKeepOff([barracks with { Own = false }]).FirstMeeting(new Vector2(78.82f, -50f), 0f, new Vector2(78.82f, -42.76f), 0f));

        Assert.Equal("Lodge", keep.Nearest(new Vector2(80f, -42f), 0f)!.Value.Portal.Name);
        var line = new PortalKeepOff([barracks with { Global = Global(100f, 100f) }]);
        Assert.Null(line.NearestOnLine([Way(97f, 90f), Way(97f, 110f)]));
        Assert.Equal(3f, line.NearestOnLine([Way(97f, 90f), Way(97f, 110f)], line.Own!.Value)!.Value, 3);
        Assert.Contains(keep.Near(new Vector2(900f, 900f), new Vector2(950f, 900f), 10f).Portals, p => p.Own);
        Assert.Equal([Reach, PortalKeepOff.ClearanceMetres], keep.OutdoorDiscs.Select(d => d.Radius));
        Assert.Same(keep, keep.WithOwnClearance(Reach));

        var routes = TravelRoutes.Default;
        var portal = RungPortal.Of(routes.Edge("hub-barracks"), routes);
        var front = portal.KeepOffForFront();
        Assert.Equal(portal.Name, front.Own!.Value.Name);
        Assert.Equal(portal.KeepOffTheOthers().Portals.Count + 1, front.Portals.Count);
        Assert.Null(portal.KeepOffTheOthers().Own);
    }

    /// <summary>A goal inside its own portal's reach is never walked into: the run ends Blocked by name before a key is pressed, and says the goal's distance.</summary>
    [Fact]
    public void A_goal_inside_its_own_portal_s_reach_ends_the_run_by_name()
    {
        var rig = new IndoorRig(IndoorRoomKit.Ring(), 0f, IndoorRoomKit.RingSouthWest, new Vector2(RingStart.X, RingStart.Y), heading: 0f);
        rig.Runner.KeepOff = new PortalKeepOff([new KeptPortal("Own Portal", Global(106f, 106f), 0f, Outdoor: false, Own: true)]);
        rig.Runner.Start(NavGoal.ToPoint(new NavPoint(IndoorRoomKit.Block.LandBlockId, new Vector3(108f, 106f, 0f)), IndoorRoomKit.RingNorthEast), rig.Body.Now);

        Assert.Equal(NavRunState.Blocked, rig.Runner.State);
        Assert.Contains("no path: " + NavRunner.PortalKeepOffFailure + ":Own_Portal: the goal stands 2.00 m from the walk's own portal, inside its touch and her stop", rig.Runner.FailureReason, StringComparison.Ordinal);
        Assert.False(rig.Body.Holds(MotionCommands.WalkForward));
    }

    /// <summary>
    /// The live Barracks shape in the ring: the goal stands 3 m past a portal on the far side from the shorter way in. Without the portal on its list the plan runs through its touch (as live); with it as the walk's own the plan takes the way
    /// that keeps the reach (the other doorway of the room), the body arrives at the goal and never comes within the touch. A portal with no way round at all ends the run by name (the runner's keep-off rule, <c>PortalKeepOffTests</c>).
    /// </summary>
    [Fact]
    public void A_walk_to_a_point_past_its_own_portal_goes_round_it_and_stops_outside_its_touch()
    {
        var own = new KeptPortal("Own Portal", Global(104.5f, 106f), 0f, Outdoor: false, Own: true);
        var goal = new Vector3(107.5f, 106f, 0f);
        var bare = new IndoorRig(IndoorRoomKit.Ring(), 0f, IndoorRoomKit.RingSouthWest, new Vector2(RingStart.X, RingStart.Y), heading: 0f);
        bare.Runner.Start(NavGoal.ToPoint(new NavPoint(IndoorRoomKit.Block.LandBlockId, goal), IndoorRoomKit.RingNorthEast), bare.Body.Now);
        bare.Frame();
        var bareLine = new PortalKeepOff([own]).NearestOnLine(bare.Runner.Path!.Waypoints, own)!.Value;
        Assert.True(bareLine < PortalKeepOff.TouchMetres, $"the plan with no list passes {bareLine:0.00} m from it");

        var rig = new IndoorRig(IndoorRoomKit.Ring(), 0f, IndoorRoomKit.RingSouthWest, new Vector2(RingStart.X, RingStart.Y), heading: 0f);
        rig.Runner.KeepOff = new PortalKeepOff([own]);
        rig.Runner.Start(NavGoal.ToPoint(new NavPoint(IndoorRoomKit.Block.LandBlockId, goal), IndoorRoomKit.RingNorthEast), rig.Body.Now);
        rig.RunFor(30);
        var line = rig.Runner.State + " " + rig.Runner.FailureReason + " " + rig.Runner.VerdictFields();

        Assert.True(rig.Runner.State == NavRunState.Arrived, line);
        var kept = new PortalKeepOff([own]).NearestOnLine(rig.Runner.Path!.Waypoints, own)!.Value;
        Assert.True(kept >= Reach - 0.01f, $"the plan keeps {kept:0.00} m of the {Reach:0.00} m reach; {line}");
        Assert.True(rig.Runner.OwnNearestMetres >= PortalKeepOff.TouchMetres, line);
        Assert.Contains("own_nearest_m=", rig.Runner.VerdictFields(), StringComparison.Ordinal);
    }
}
