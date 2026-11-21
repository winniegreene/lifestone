using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Navigation;
using Lifestone.Client.Physics;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Tests.Movement;
using Lifestone.Client.Tests.Session;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 sprint (<c>m6-vendor-reach</c>; live, run 10, 2026-11-21 07:17:25 to 07:18:33 UTC, cache/m6/sprint/run10/step3-levelup.log; the same leg in run 6 from 2026-11-20 10:18:04 UTC and run 9 from 19:53:47 UTC): the resupply's walk
/// to the Grand Master Scrivener of Item Magic in Stonehold (object 0x764D503D within 3.0 m, his room 0x64D50135) ran into his body for 12 stuck rungs about 1 m from him, standing in 0x64D50133, and ended Blocked, so the
/// learn trip went home without its two scrolls. The arrival test asked for her body in his room, which his body kept her out of. On the retail dats, with his body in the walker's physics as the server object it is live
/// (<see cref="ServerObjectPhysicsHost"/>) and on the run's body feed: from her stuck point the run arrives at once, within the station's reach and ACE's use radius; a walk whose range his body never lets her meet arrives
/// on the contact with him, not stuck; and the whole leg from the Network drop still plans and walks, and arrives within his reach.
/// </summary>
/// <remarks>
/// His row: landblock_instance 0x764D503D, weenie 20212 at (103.795, 60.703, 78.005) in 0x64D50135 (<c>Grounds/scriveners.json</c>). His weenie (read-only SELECTs on ace_world, the pinned world SQL v0.9.296):
/// weenie_properties_d_i_d type 1 (Setup) 33554510 = 0x0200004E; weenie_properties_int type 93 (PhysicsState) 2098200 = 0x200418; weenie_properties_float type 54 (UseRadius) 3; no ObjectScale (type 39), so 1.
/// The drop is <c>travel-routes.json</c>'s network-to-stonehold 0x64D5000B. ACE's use at the station's open: <c>Player.CreateMoveToChain</c> (Player_Move.cs 37-86 @95ef426d) asks <c>Landblock.WithinUseRadius</c>
/// (Landblock.cs 1004-1014), which is <c>WorldObject.IsWithinUseRadiusOf</c> (WorldObject_Use.cs 47-61): the cylinder distance (<c>Position.CylinderDistance</c>, Position.cs 100-114: centre distance less both
/// <c>PhysicsObj.GetRadius</c>, the Setup's radius times the scale, PartArray.cs 204-207) at most the UseRadius, less 0.2 m in magic combat mode (Player_Move.cs 61-62); within it ACE turns her and uses at once.
/// </remarks>
[Trait("Tier", "Exhaustive")]
public sealed class StoneholdVendorReachDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const double Fps = 60.0;
    private const uint ItemGrandMaster = 0x764D503D;
    private const uint HisRoom = 0x64D50135;
    private const uint HisSetup = 0x0200004E;
    private const uint HisPhysicsState = 0x200418;
    private const float HisUseRadius = 3f;

    /// <summary>The magic combat mode's cut of a use radius in ACE's walk-to-use (Player_Move.cs 61-62 @95ef426d).</summary>
    private const float MagicUseCut = 0.2f;

    private static readonly Vector3 HisPoint = new(103.795f, 60.703f, 78.005f);

    /// <summary>Her stuck point, the first of the twelve (07:17:47.769 UTC): 0x64D50133 [104.749 60.982 78.005], facing him (w=-0.6485 z=-0.7612).</summary>
    private static readonly CellFrame StuckPoint = new(0x64D50133, new AcFrame(new Vector3(104.749f, 60.982f, 78.005f), new Quaternion(0f, 0f, -0.7612f, -0.6485f)));

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void From_her_stuck_point_against_the_Item_Grand_Master_the_walk_arrives_at_once_within_his_reach_and_ACEs_use_radius()
    {
        using var rig = Rig(StuckPoint.CellId);
        var goal = Goal(HisUseRadius);

        // The cause: she stood within the range the goal asks for, in another cell than the goal's room (the old test asked for the room).
        var start = NavMap.FromCellFrame(StuckPoint);
        Assert.NotEqual(HisRoom, StuckPoint.CellId);
        Assert.True(start.HorizontalDistance(goal.Point) < goal.Range, $"{start.HorizontalDistance(goal.Point):F2} m");

        var walk = Walk(rig, goal, StuckPoint, "reach-from-stuck");
        Assert.Equal(NavRunState.Arrived, walk.State);
        Assert.Equal(0, walk.Rungs);
        Assert.True(walk.Seconds < 0.1, $"arrived after {walk.Seconds:F2} s");
        Assert.True(walk.RouteMetres < 0.05f, $"walked {walk.RouteMetres:F2} m");
        Assert.StartsWith("clear-line: ", walk.Reason, StringComparison.Ordinal); // out of his room, nothing between: the new rule, not the old one
        Assert.Contains("live place", walk.Reason, StringComparison.Ordinal);
        AssertInReach(rig, walk.At);
    }

    [RequiresDatsFact]
    public void A_walk_whose_range_his_body_never_lets_her_meet_arrives_on_the_contact_with_him_not_stuck()
    {
        using var rig = Rig(StuckPoint.CellId);

        // 0.5 m of his centre: two bodies (the human cylinders, 0.48 m each) keep her farther out, so the range alone is never met; she presses into him, as live.
        var walk = Walk(rig, Goal(0.5f), StuckPoint, "reach-by-contact");
        Assert.Equal(NavRunState.Arrived, walk.State);
        Assert.Equal(0, walk.Rungs);
        Assert.StartsWith("contact: ", walk.Reason, StringComparison.Ordinal);
        Assert.True(walk.Seconds < 5.0, $"arrived after {walk.Seconds:F2} s");
        AssertInReach(rig, walk.At);
    }

    [RequiresDatsFact]
    public void From_the_Network_drop_the_walk_to_him_still_plans_walks_and_arrives_within_his_reach()
    {
        var edge = TravelRoutes.Default.Edge("network-to-stonehold");
        var drop = new CellFrame(edge.DestCell, new AcFrame(edge.DestOrigin, Quaternion.Identity));
        Assert.Equal(0x64D5000Bu, drop.CellId);
        using var rig = Rig(drop.CellId);
        var goal = Goal(HisUseRadius);
        Assert.True(NavMap.FromCellFrame(drop).HorizontalDistance(goal.Point) > 50f); // far: no arrival before a plan and a walk

        var walk = Walk(rig, goal, drop, "reach-from-drop");
        Assert.Equal(NavRunState.Arrived, walk.State);
        Assert.True(walk.Plans >= 1);
        Assert.True(walk.RouteMetres > 50f, $"walked {walk.RouteMetres:F1} m");
        Assert.NotNull(walk.Reason);
        AssertInReach(rig, walk.At);
    }

    /// <summary>The check after the walk: the resupply station's (<c>ResupplyStation.Meet</c>: the centre distance at most <see cref="ShopLeg.ReachOf"/>) and ACE's (the cylinder distance at most the use radius, magic mode).</summary>
    private void AssertInReach(StoneholdRig rig, CellFrame at)
    {
        var flat = Vector2.Distance(new Vector2(at.Origin.X, at.Origin.Y), new Vector2(HisPoint.X, HisPoint.Y)); // one landblock: its local metres
        var dz = at.Origin.Z - HisPoint.Z;
        var centre = Math.Sqrt((flat * flat) + (dz * dz));
        var reach = HisUseRadius + 1.0; // ShopLeg.ReachOf at UseRadius 3: the use radius plus GameStateReducer.VendorCloseMarginMetres (1.0)
        var cylinder = flat - (rig.HerRadius + rig.HisRadius); // Position.CylinderDistance on one floor: the centre distance less both radii
        output.WriteLine($"ended at 0x{at.CellId:X8} {at.Origin}: {centre:F2} m from his centre (the station's reach {reach:F1} m), cylinder distance {cylinder:F2} m (ACE's use {HisUseRadius - MagicUseCut:F1} m in magic mode)");
        Assert.True(centre <= reach, $"{centre:F2} m from him");
        Assert.True(cylinder <= HisUseRadius - MagicUseCut, $"cylinder distance {cylinder:F2} m");
    }

    /// <summary>The goal <c>JourneyLegPlanner.GoalOf</c> makes of the station's request when he is in view: his live point and cell, the request's range, no sight.</summary>
    private static NavGoal Goal(float range) => NavGoal.ToObject(ItemGrandMaster, new NavPoint(HisRoom & 0xFFFF0000u, HisPoint), range, needSight: false, HisRoom);

    private (NavRunState State, CellFrame At, int Rungs, int Plans, float RouteMetres, double Seconds, string? Reason) Walk(StoneholdRig rig, NavGoal goal, CellFrame from, string name)
    {
        var h = new ActionHarness(offline: true);
        var now = 0.0;
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, name);
        var walker = OfflineWalker.Create(Dats, rig.World, NullLogSink.Instance, new OfflinePlayerWeenie(230), sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        rig.World.UseTime(0.0);
        Assert.True(walker.Place(from), $"placed at {from}");
        using var confirmer = new RouteConfirmer(rig.World, walker.Physics.Setup!);
        var source = rig.Planner.SourceFor(from.CellId, JourneyLegPlanner.GoalCellOf(goal), rig.Map);
        var log = new RecordingLogSink(LogSeverity.Info);
        var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, confirmer, audit, new NavRunnerOptions { Name = name }, log) { BodyFeed = rig.Bodies };
        runner.Start(goal, now);
        while (!runner.IsDone && now < 120.0)
        {
            now += 1.0 / Fps;
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / Fps);
        }

        output.WriteLine($"{name}: {runner.State} after {now:F2} s; {runner.VerdictFields()}; reason {runner.ArrivalReason}; {runner.FailureReason}");
        foreach (var line in log.Lines.Where(l => l.Contains("arrived:", StringComparison.Ordinal) || l.Contains("stuck", StringComparison.Ordinal) || l.Contains("wall stands between", StringComparison.Ordinal)))
        {
            output.WriteLine("  " + line);
        }

        var result = (runner.State, walker.Position, runner.Rungs, runner.Plans, runner.RouteMetres, now, runner.ArrivalReason);
        runner.Dispose();
        return result;
    }

    /// <summary>Stonehold over the headless streamer (the drop's 3 x 3), the journeys' leg planner, and his body: in the physics as the server object it is live, and on the body feed with his live place.</summary>
    private StoneholdRig Rig(uint focus)
    {
        var world = new PhysicsWorld();
        var map = new NavMap();
        var streamer = new HeadlessStreamer(Dats, null, world, map, NullLogSink.Instance);
        streamer.EnsureNow(focus);
        world.UseTime(0.0);
        var radii = HostileCreatureFeed.SetupRadii(Dats);
        var his = radii(HisSetup) ?? throw new InvalidOperationException($"no radius for his Setup 0x{HisSetup:X8}");
        var her = radii(OfflineWalker.HumanSetupId) ?? throw new InvalidOperationException("no radius for the human Setup");
        var him = new CellFrame(HisRoom, new AcFrame(HisPoint, Quaternion.Identity));
        new ServerObjectPhysicsHost(world, Dats).Create(new ServerObjectDescription(ItemGrandMaster, HisSetup, 1f, HisPhysicsState, IsCreature: true, IsSelf: false, DescriptionFlags: 0x200), him); // 0x200: a vendor (HostileCreatureFeed's vendor bit)
        var body = new NavBody(ItemGrandMaster, "Grand Master Scrivener of Item Magic", NavMap.FromCellFrame(him).Global, NpcBodyFeed.BodyRadius(his, SetupShapes.Of(radii)?.ShapeRadius(HisSetup)), HisPoint.Z, Outdoor: false);
        output.WriteLine($"his Setup radius {his:F2} m, hers {her:F2} m; his body on the feed {body.Radius:F2} m");
        return new StoneholdRig(new JourneyLegPlanner(Dats), map, streamer, world, new PlacedBodies(him, body), her, his);
    }

    private sealed class StoneholdRig(JourneyLegPlanner planner, NavMap map, HeadlessStreamer streamer, PhysicsWorld world, INavBodyFeed bodies, float herRadius, float hisRadius) : IDisposable
    {
        public JourneyLegPlanner Planner { get; } = planner;

        public NavMap Map { get; } = map;

        public PhysicsWorld World { get; } = world;

        public INavBodyFeed Bodies { get; } = bodies;

        public float HerRadius { get; } = herRadius;

        public float HisRadius { get; } = hisRadius;

        public void Dispose() => streamer.Dispose();
    }

    /// <summary>A body feed with his body and his live place (what a live session's object table holds for him).</summary>
    private sealed class PlacedBodies(CellFrame at, NavBody body) : INavBodyFeed
    {
        public void Fill(Vector2 from, float maxMetres, List<NavBody> into)
        {
            into.Clear();
            if (Vector2.Distance(body.Centre, from) <= maxMetres)
            {
                into.Add(body);
            }
        }

        public bool TryPlace(uint guid, out CellFrame place)
        {
            place = at;
            return guid == body.Guid;
        }
    }
}
