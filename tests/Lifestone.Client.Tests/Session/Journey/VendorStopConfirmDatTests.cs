using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Navigation;
using Lifestone.Client.Physics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Movement;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.World.Cells;
using Lifestone.Tests.Shared;
using Lifestone.Client.Tests.Physics;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// The walker's physics pre-check over the resupply trip's first walk to each vendor, on the retail dats (M5 item 7's soak, 2026-10-29 03:13 UTC: the trip recalled to Holtburg, planned
/// the walk to Shopkeeper Renald the Elder's stop, and <see cref="RouteConfirmer"/> refused the plan three times in a row at a sample inside his front room 0xA9B4013F, so the mage
/// never moved; m5-plan.md section 9 row <c>m5-renald-shop-floor</c>, D120). <see cref="ResupplyRouteDatTests"/> proves a route exists; this proves the route stands. The leg planned
/// from the home lifestone to the vendor's stop (the request <c>ResupplyRun.Meet</c> sends) is confirmed as the runner confirms it before it presses a key: a ground probe over the fixed
/// world every metre and a placement of the body every 2 m, up to the goal's stop tolerance. The stop point and the ring of that tolerance round it (the body stops anywhere within it)
/// must find the stop cell's floor through the cell's statics, which the route test's floor-polygon check cannot see: Renald's old stop (135.6, 15.8) stood on a floor polygon of his
/// front room but among three barrels (Setup 0x020000A5, a cylinder of radius 0.27 m: two on the floor at (134.83, 16.05) and (135.75, 16.05), one stacked on them) in the corner
/// by his counter (0x02000272, two spheres of 0.405 m with their west face at x 135.14), where the human's foot sphere (radius 0.48 m) fits nowhere. The physics is right: the corner is
/// full. The fix is the profile's <c>stop_point</c>: the clear floor by the door in front of the counter, where the walks of M4 P5b and M5 item 0 ended and sold.
/// </summary>
/// <remarks>
/// Test data, read-only from the pinned world database (ACE-World-Database-v0.9.296), as <see cref="F6RenaldMoveToDatTests"/>: Renald the Elder 0x7A9B4021 at (139.23, 18.33, 94.01) in
/// cell 0xA9B40141 with UseRadius 5; the shop's front door 0x7A9B4015 (Setup 0x020019FF at (132.56, 17.115, 94), turned 180 degrees); Monyra the Jeweler 0x7A9B4020 at
/// (157.19, 135.30, 66.12) in 0xA9B40117 (Archmage Cindrue's entry hall) and Ecutha the Tailor 0x7A9B4025 at (56.52, 132.0, 66.12) in 0xA9B4012A, the two Holtburg vendors no
/// profile names, checked so no other shop hides the same fault: both own points confirm (Ecutha's lobby holds the pillar of Holtburg's scriveners' hall on a portal's centre: the room graph crosses beside it since M6 7 G, <see cref="HoltburgWalkOutDatTests"/>).
/// </remarks>
[Trait("Tier", "Exhaustive")]
public sealed class VendorStopConfirmDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const string Den = "black-spawn-den-aluvian";
    private const uint RenaldFrontRoomCell = 0xA9B4013F;
    private const uint Renald = 0x7A9B4021;
    private const uint FrontDoor = 0x7A9B4015;
    private const uint HumanSetupId = 0x02000001;
    private const uint BarrelSetupId = 0x020000A5;
    private const float BarrelRadius = 0.27f;
    private const float RenaldUseRadius = 5f;
    private static readonly Vector3 RenaldAt = new(139.23f, 18.33f, 94.01f);
    private static readonly Vector3 OldRenaldStop = new(135.6f, 15.8f, 94f);
    private static readonly Vector3 GateArrival = new(133.21f, 16.12f, 94.01f); // where the item-end gate's walks to Renald ended (journey-melee, 2026-10-31 00:54 UTC)
    private const uint Thelnoth = 0x7A9B4027;
    private const uint ThelnothCellar = 0xA9B40111;
    private static readonly Vector3 ThelnothAt = new(30.0196f, 159.683f, 66.005f);
    private static readonly Vector3 CellarEntry = new(32.87f, 156.43f, 66.01f); // the route's first waypoint inside the cellar, 4.3 m from him
    private const double Fps = 60.0;

    /// <summary>The Holtburg vendors outside the den's profile: name, cell and point from their landblock_instance rows.</summary>
    private static readonly (string Name, uint Cell, Vector3 Point)[] OtherHoltburgVendors =
    [
        ("Monyra the Jeweler", 0xA9B40117, new Vector3(157.19f, 135.298f, 66.12f)),
        ("Ecutha the Tailor", 0xA9B4012A, new Vector3(56.52f, 132f, 66.12f)),
        ("Sedor Wystan the Blacksmith", 0xA9B40100, new Vector3(87.48f, 131.04f, 66.005f)), // M6 item 14: the leather set's vendor (pathwarden.json's leather block; landblock_instance guid 0x7A9B4026)
    ];

    /// <summary>The vendors whose own point the pre-check refuses, with a piece of the first refused sample's text; a fixed one fails here until its row goes.</summary>
    private static readonly Dictionary<string, string> Gaps = [];

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void Each_den_vendors_leg_from_the_lifestone_passes_the_pre_check_and_its_stop_ring_stands_on_the_floor_through_the_statics()
    {
        var ground = GroundCatalog.Get(Den);
        var rig = Rig(ground);
        foreach (var vendor in ground.Vendors)
        {
            var stop = vendor.StopPoint ?? vendor.Point;
            var (path, goal) = rig.Plan(vendor.Name, vendor.StopCell, stop);
            Assert.Equal(NavPathStatus.Found, path.Status);
            var status = rig.Confirmer.ConfirmAll(path.Waypoints, goal.StopTolerance);
            output.WriteLine($"{vendor.Name}: stop 0x{vendor.StopCell:X8} {stop}: {status} {rig.Confirmer.ConfirmedMetres:F1} m, {rig.Confirmer.GroundSamples} probes, {rig.Confirmer.Placements} placements; {rig.Confirmer.Failure}");
            if (Gaps.TryGetValue(vendor.Name, out var gap))
            {
                Assert.NotEqual(RouteConfirmStatus.Confirmed, status);
                Assert.Contains(gap, rig.Confirmer.Failure?.ToString() ?? string.Empty, StringComparison.Ordinal);
                continue;
            }

            Assert.Equal(RouteConfirmStatus.Confirmed, status);

            // M5 7 P7p: Renald's stand lies inside his use radius, in the corner between the barrels and the box by his counter, so the far half of its ring stands on the box (95.0) or in the barrels: the body comes
            // only from the door and stops on the near half, which The_walk_to_Renalds_stand_... walks on the dats. Every other stop point keeps the whole ring.
            AssertRingStands(rig.World, rig.Foot, vendor.StopCell, stop, goal.StopTolerance, vendor.Name, wholeRing: vendor.StopPoint is not null && vendor.Guid != Renald);
        }
    }

    /// <summary>
    /// M5 7 P7k (D143): the leg live check 2 could not plan (2026-10-30 05:03 UTC, <c>indoor-start-unreachable:0xA9B40117</c>), from Archmage Cindrue's entry hall after Monyra to the
    /// Scrivener of Item Magic outdoors in landblock 0xA9B3, now planned out through the hall's door (<see cref="ResupplyRouteDatTests"/>), passes the walker's pre-check and its stop ring stands.
    /// </summary>
    [RequiresDatsFact]
    public void The_leg_from_Cindrues_entry_hall_to_the_Scrivener_passes_the_pre_check()
    {
        var ground = GroundCatalog.Get(Den);
        var rig = Rig(ground);
        var scrivener = ground.Vendors.First(v => v.Name == "Scrivener of Item Magic");
        var stop = scrivener.StopPoint ?? scrivener.Point;
        var hall = new CellFrame(0xA9B40117, new AcFrame(new Vector3(155.42f, 134.31f, 66.01f), Quaternion.Identity));
        var (path, goal) = rig.Plan(hall, scrivener.Name, scrivener.StopCell, stop);
        Assert.Equal(NavPathStatus.Found, path.Status);
        var status = rig.Confirmer.ConfirmAll(path.Waypoints, goal.StopTolerance);
        output.WriteLine($"hall -> {scrivener.Name}: {status} {rig.Confirmer.ConfirmedMetres:F1} m, {rig.Confirmer.GroundSamples} probes, {rig.Confirmer.Placements} placements; {rig.Confirmer.Failure}");
        Assert.Equal(RouteConfirmStatus.Confirmed, status);
        AssertRingStands(rig.World, rig.Foot, scrivener.StopCell, stop, goal.StopTolerance, scrivener.Name, wholeRing: scrivener.StopPoint is not null);
    }

    [RequiresDatsFact]
    public void Renalds_old_stop_point_stood_among_the_barrels_by_his_counter_and_the_pre_check_refused_the_leg_to_it_as_the_soak_did()
    {
        var ground = GroundCatalog.Get(Den);
        var rig = Rig(ground);

        // The point itself: a floor polygon under it (the route test's check), no room for the foot sphere on it, a barrel's axis within the sphere's and the barrel's radii.
        var at = new CellFrame(RenaldFrontRoomCell, new AcFrame(OldRenaldStop, Quaternion.Identity));
        Assert.False(rig.World.TryFixedGroundZ(at, rig.Foot, out _, out _), "no walkable ground within the probe's window at the old stop");
        var barrels = rig.World.GetLandblock(LandblockCoord.Holtburg)!.StaticObjects.Where(s => s.ModelId == BarrelSetupId && s.Position.CellId == RenaldFrontRoomCell).ToList();
        Assert.Equal(3, barrels.Count);
        var nearest = barrels.Min(b => Vector2.Distance(new Vector2(b.Position.Origin.X, b.Position.Origin.Y), new Vector2(OldRenaldStop.X, OldRenaldStop.Y)));
        output.WriteLine($"the nearest barrel's axis is {nearest:F2} m from the old stop (the foot sphere {rig.Foot.Radius} m, the barrel {BarrelRadius} m)");
        Assert.True(nearest < rig.Foot.Radius + BarrelRadius, "the old stop is inside a barrel's reach");

        // The leg: planned (the route test passed it), refused by the ground probe inside the front room, past the door at x 132.55.
        var (path, goal) = rig.Plan("Renald (old stop)", RenaldFrontRoomCell, OldRenaldStop);
        Assert.Equal(NavPathStatus.Found, path.Status);
        var status = rig.Confirmer.ConfirmAll(path.Waypoints, goal.StopTolerance);
        output.WriteLine($"old stop: {status}; {rig.Confirmer.Failure}");
        Assert.Equal(RouteConfirmStatus.Failed, status);
        var failure = rig.Confirmer.Failure!.Value;
        Assert.Equal("ground", failure.Probe);
        Assert.Equal(RenaldFrontRoomCell, failure.At.CellId);
        Assert.True(failure.At.Origin.X > 133.5f, $"refused inside the shop, not at its door: {failure}");
        Assert.Contains("no walkable ground within", failure.Why, StringComparison.Ordinal);
    }

    /// <summary>
    /// M5 7 P7p, the cause of the item-end gate's two fighter journeys (2026-10-31 00:53 UTC, journey-melee's vendor and vendor-facts stations, journey-archer's vendor): the hunt's and the vendor-facts station's walk to Renald
    /// was an object goal within <see cref="HuntScenario.RenaldUseMetres"/> (6.5 m centre to centre) and ended at (133.21, 16.12) in his front room, about 5.05 m from him cylinder to cylinder, just outside ACE's 5 m use radius;
    /// ACE answered the open with its walk-to-use MoveToObject, whose straight line from there runs into the barrels stacked by his counter: <c>TODO(m5-moveto-stuck) held_s=15 moved_m=0.09 contact=True</c>, and ACE's chain timed
    /// out ("UseDone 0 but no 0062"). Here the same MoveTo from that spot, with his body hosted, does not arrive and the controller's stuck rule ends it.
    /// </summary>
    [RequiresDatsFact]
    public void From_where_the_item_end_gates_walk_ended_ACEs_MoveTo_to_Renald_runs_into_the_barrels_and_ends_stuck()
    {
        var scene = RenaldScene(GateArrival, heading: 70f, holdMetres: 0.15f); // the live body pressed against the barrels; the placement slides it out of their contact, about 0.1 m south
        var start = scene.CylinderTo(Renald);
        output.WriteLine($"from the gate's arrival ({GateArrival.X}, {GateArrival.Y}): {start:F3} m cylinder to cylinder");
        Assert.True(start > RenaldUseRadius, $"ACE sends the MoveTo: {start:F3} m, over the 5 m use radius");
        Assert.InRange(start, 5.0f, 5.2f); // the gate's line: distance_m=5.06 wanted_m=5.00

        scene.MoveToObject(Renald, RenaldUseRadius);
        var seconds = scene.RunWhileMovingTo(30.0);
        var end = scene.Walker.Position.Frame.Origin;
        output.WriteLine($"{scene.Walker.Controller.LastServerMoveToEnd} in {seconds:F2} s at ({end.X:F2}, {end.Y:F2}, {end.Z:F2}), cylinder {scene.CylinderTo(Renald):F3} m");
        Assert.Equal("stuck", scene.Walker.Controller.LastServerMoveToEnd);
        Assert.Equal(1, scene.Walker.Controller.ServerMoveToStuckEnds);
        Assert.True(scene.CylinderTo(Renald) > RenaldUseRadius, "the barrels hold the body outside his use radius");
        Assert.True(Vector2.Distance(new Vector2(end.X, end.Y), new Vector2(GateArrival.X, GateArrival.Y)) < 0.5f, "the body barely moved, as the gate's moved_m=0.09");
    }

    /// <summary>
    /// The fix (M5 7 P7p): every walk to Renald is a point goal to his stand (<see cref="ShopLeg.RenaldStand"/>, the den profile's <c>stop_point</c>; the request the hunt, the vendor-facts station and the resupply station send: no
    /// object, no range, so the leg planner's default stop tolerance). The stand itself lies inside his use radius, and the walker's runner, on the dats with his body and the open door hosted, arrives there from the street before the door,
    /// from the street north of it and from the gate's stuck spot, every time on the front room's floor inside his use radius (and inside the 4.8 m ACE allows in the magic mode, <c>Player_Move.cs</c> 60-61 @95ef426d): the open
    /// needs no MoveTo of ACE's.
    /// </summary>
    [RequiresDatsFact]
    public void The_walk_to_Renalds_stand_arrives_inside_his_use_radius_and_the_open_needs_no_MoveTo()
    {
        var ground = GroundCatalog.Get(Den);
        var (cell, stand) = ShopLeg.RenaldStandOf(ground);
        Assert.Equal(ShopLeg.RenaldStand, (cell, stand)); // the one source: the default ground's profile
        Assert.Equal(RenaldFrontRoomCell, cell);

        // The stand point itself: on the floor through the statics, inside his use radius with no walk.
        var scene = RenaldScene(stand with { Z = 94.01f });
        var atStand = scene.CylinderTo(Renald);
        output.WriteLine($"the stand ({stand.X}, {stand.Y}): {atStand:F3} m cylinder to cylinder");
        Assert.True(atStand <= RenaldUseRadius - 0.3f, $"inside his use radius with a margin: {atStand:F3} m");
        var rig = Rig(ground);
        Assert.True(rig.World.TryFixedGroundZ(new CellFrame(cell, new AcFrame(stand, Quaternion.Identity)), rig.Foot, out var floor, out _));
        Assert.InRange(floor, 93.9f, 94.1f);

        // The walk: the stations' request, the leg planner's goal, the runner on the human body.
        var request = new TravelRequest(new TeleportDestination("Renald's stand", [], new WorldLocation("Renald's stand", new CellPosition(cell, stand), HasHeight: true, HeadingDegrees: 0), "stand test"), TravelPurpose.Vendor);
        var goal = new JourneyLegPlanner(Dats).GoalOf(new J1JourneyRig().View, request);
        Assert.Null(goal.Guid);
        Assert.Equal(NavGoal.DefaultStopTolerance, goal.StopTolerance);
        var starts = new (string Name, CellFrame From)[]
        {
            ("the street before the door", new CellFrame(0xA9B40029, new AcFrame(new Vector3(128f, 17.1f, 94.01f), Quaternion.Identity))),
            ("the street north of the door", new CellFrame(0xA9B40029, new AcFrame(new Vector3(130f, 22f, 94.01f), Quaternion.Identity))),
            ("the gate's stuck spot", new CellFrame(RenaldFrontRoomCell, new AcFrame(GateArrival, Quaternion.Identity))),
        };
        foreach (var (name, from) in starts)
        {
            var (runner, at, cylinder) = FrontRoomWalk(from, goal, "renald-stand");
            output.WriteLine($"from {name}: {runner.State}; at {at}; {cylinder:F3} m cylinder to cylinder");
            Assert.Equal(NavRunState.Arrived, runner.State);
            Assert.Equal(RenaldFrontRoomCell, at.CellId);
            Assert.InRange(at.Origin.Z, 93.95f, 94.1f); // on the floor, not up the box by the counter
            Assert.True(cylinder <= RenaldUseRadius - 0.2f, $"from {name}: {cylinder:F3} m, inside his use radius (and the magic mode's 4.8 m)");
        }
    }

    /// <summary>
    /// The second case (the soak's round 10, 2026-10-29 03:25 UTC): the walk to Thelnoth Cort the Healer, not yet in the object table when the leg was planned, was a point goal at his own point with the 0.5 m stop
    /// tolerance; the plan passed the pre-check and the body walked through his door, then jammed about a metre from the point, 19 rungs over 3 plans, and the run ended stuck. Since M5 item 5 (D70) his body stands in
    /// the player's physics world, where the pre-check does not look (F3: the fixed world only, by design), so the point under him can never be reached. Here his body is hosted at his point and the body starts inside the cellar.
    /// </summary>
    [RequiresDatsFact]
    public void A_point_goal_at_Thelnoths_own_point_jams_against_his_body_and_the_run_ends_stuck_as_round_10_did()
    {
        var (runner, at, cylinder) = CellarWalk(NavGoal.ToPoint(new NavPoint(0xA9B40000, ThelnothAt), ThelnothCellar), 120.0, "thelnoth-own-point");
        Assert.Equal(NavRunState.Blocked, runner.State);
        Assert.Contains("stuck after", runner.FailureReason, StringComparison.Ordinal);
        Assert.InRange(Vector2.Distance(new Vector2(at.Origin.X, at.Origin.Y), new Vector2(ThelnothAt.X, ThelnothAt.Y)), 0.9f, 1.6f); // his body holds the player off the point
        Assert.True(cylinder < 1.0f, $"against him: {cylinder:F2} m cylinder to cylinder");
    }

    /// <summary>The fix (D120): the station's point request to an unseen same-cell vendor carries <see cref="ShopLeg.UnseenVendorReachMetres"/>, so the leg planner's goal stops 1.6 m short of his point, within his reach.</summary>
    [RequiresDatsFact]
    public void The_stations_request_to_an_unseen_same_cell_vendor_stops_short_of_his_body_and_arrives_within_his_reach()
    {
        var ground = GroundCatalog.Get(Den);
        var thelnoth = ground.Vendors.First(v => v.Guid == Thelnoth);
        Assert.Equal(thelnoth.Cell, thelnoth.StopCell);
        var request = new TravelRequest(new TeleportDestination(thelnoth.Name, [], new WorldLocation(thelnoth.Name, new CellPosition(thelnoth.StopCell, thelnoth.Point), HasHeight: true, HeadingDegrees: 0), "stop confirm test"), TravelPurpose.Vendor, Range: ShopLeg.UnseenVendorReachMetres);
        var goal = new JourneyLegPlanner(Dats).GoalOf(new J1JourneyRig().View, request);
        Assert.False(goal.IsObject);
        Assert.Equal((float)ShopLeg.UnseenVendorReachMetres, goal.StopTolerance);

        var (runner, at, cylinder) = CellarWalk(goal, 30.0, "thelnoth-stand-off");
        var distance = Vector2.Distance(new Vector2(at.Origin.X, at.Origin.Y), new Vector2(ThelnothAt.X, ThelnothAt.Y));
        Assert.Equal(NavRunState.Arrived, runner.State);
        Assert.Equal(ThelnothCellar, at.CellId);
        Assert.InRange(distance, 0.9f, (float)ShopLeg.UnseenVendorReachMetres + 0.01f);
        Assert.True(distance <= 3.0 + 1.0, "within his reach (UseRadius 3 in the world database, plus the close rule's margin)");
        Assert.True(cylinder <= 3.0, $"within ACE's use radius, cylinder to cylinder: {cylinder:F2} m");
        Assert.Equal(0, runner.Rungs);
    }

    /// <summary>
    /// M6 14 grocer (the owner, 2026-11-13: sell the empty flasks): the walk to Fispur Ansel the Grocer (714) as the station sends it from Holtburg's lifestone, the home of every Holtburg profile that lists him. His stop cell is his
    /// own, so the request is his own point with <see cref="ShopLeg.UnseenVendorReachMetres"/> as the range (<c>ResupplyRun.RoomRequest</c>): the plan enters his shop 0xA9B4016A through the room 0xA9B4016E, ends on his floor within
    /// that range of his point, and the walker's pre-check confirms it. What this does not show: the shop's doors (0x7A9B401E and 0x7A9B401F, world objects the dats do not hold) and his body, which a live round walks
    /// (<c>TODO(m6-grocer-stop-point)</c>).
    /// </summary>
    [RequiresDatsFact]
    public void The_stations_walk_from_the_lifestone_to_the_Grocer_enters_his_shop_and_passes_the_pre_check()
    {
        var ground = GroundCatalog.Get("holtburg-outskirts");
        var grocer = ground.VendorOf(714)!;
        Assert.Equal((grocer.Cell, (Vector3?)null), (grocer.StopCell, grocer.StopPoint));
        var rig = Rig(ground);
        var request = new TravelRequest(new TeleportDestination(grocer.Name, [], new WorldLocation(grocer.Name, new CellPosition(grocer.StopCell, grocer.Point), HasHeight: true, HeadingDegrees: 0), "grocer test"), TravelPurpose.Vendor, Range: ShopLeg.UnseenVendorReachMetres);
        var goal = rig.Planner.GoalOf(rig.View.View, request);
        Assert.False(goal.IsObject);
        Assert.Equal((float)ShopLeg.UnseenVendorReachMetres, goal.StopTolerance);

        var path = rig.Planner.SourceFor(rig.Home.CellId, JourneyLegPlanner.GoalCellOf(goal), rig.Map).Plan(rig.Home, goal, new NavScars());
        var end = path.Waypoints.Count > 0 ? path.Waypoints[^1].At : rig.Home;
        var reach = Vector2.Distance(new Vector2(end.Origin.X, end.Origin.Y), new Vector2(grocer.Point.X, grocer.Point.Y));
        output.WriteLine($"lifestone -> {grocer.Name}: {path.Status} {path.Reason}, {path.Waypoints.Count} waypoints, cells entered {string.Join(" ", path.CellsEntered.Select(c => c.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)))}; ends 0x{end.CellId:X8} {end.Origin}, {reach:F2} m from his point");
        Assert.Equal(NavPathStatus.Found, path.Status);
        Assert.Equal(rig.Home.CellId, path.Waypoints[0].At.CellId);
        Assert.Equal([0xA9B4016Eu, 0xA9B4016Au], path.CellsEntered);
        Assert.Equal(0xA9B4016Au, end.CellId);
        Assert.InRange(reach, 0f, (float)ShopLeg.UnseenVendorReachMetres + 0.01f);
        Assert.InRange(MathF.Abs(end.Origin.Z - grocer.Point.Z), 0f, NavRunner.ArrivalHeightMetres);

        var status = rig.Confirmer.ConfirmAll(path.Waypoints, goal.StopTolerance);
        output.WriteLine($"pre-check: {status} {rig.Confirmer.ConfirmedMetres:F1} m, {rig.Confirmer.GroundSamples} probes, {rig.Confirmer.Placements} placements; {rig.Confirmer.Failure}");
        Assert.Equal(RouteConfirmStatus.Confirmed, status);
        AssertRingStands(rig.World, rig.Foot, grocer.StopCell, grocer.Point, goal.StopTolerance, grocer.Name, wholeRing: false);
    }

    [RequiresDatsFact]
    public void The_other_Holtburg_vendors_own_points_pass_the_same_check_or_are_named_gaps()
    {
        var ground = GroundCatalog.Get(Den);
        var rig = Rig(ground);
        foreach (var (name, cell, point) in OtherHoltburgVendors)
        {
            // The station's first request for a vendor whose stop cell is his own: his cell and point, no object, no range (ResupplyRun.Meet).
            var (path, goal) = rig.Plan(name, cell, point);
            var status = path.Status == NavPathStatus.Found ? rig.Confirmer.ConfirmAll(path.Waypoints, goal.StopTolerance) : RouteConfirmStatus.Idle;
            output.WriteLine($"{name}: 0x{cell:X8} {point}: plan {path.Status} {path.Reason}; confirm {status} {rig.Confirmer.ConfirmedMetres:F1} m; {rig.Confirmer.Failure}");
            if (Gaps.TryGetValue(name, out var gap))
            {
                Assert.NotEqual(RouteConfirmStatus.Confirmed, status);
                Assert.Contains(gap, rig.Confirmer.Failure?.ToString() ?? path.Reason ?? string.Empty, StringComparison.Ordinal);
                continue;
            }

            Assert.Equal(NavPathStatus.Found, path.Status);
            Assert.Equal(RouteConfirmStatus.Confirmed, status);
            AssertRingStands(rig.World, rig.Foot, cell, point, goal.StopTolerance, name, wholeRing: false);
        }
    }


    /// <summary>
    /// The stop point finds the cell's floor through the statics at its own height (within 0.2 m under the point, which an NPC's row puts up to 0.12 m over the floor), and so do the eight points
    /// of the stop tolerance round it when <paramref name="wholeRing"/>: a profile <c>stop_point</c> in a room in front of the vendor, where the body must stand wherever within the tolerance it stops.
    /// A vendor's own point (his stop cell is his own) is where he stands, often against his counter or a wall, and the body stops on its approach side, so the ring is printed and counted but not required.
    /// </summary>
    private void AssertRingStands(PhysicsWorld world, PhysSphere foot, uint cell, Vector3 stop, float tolerance, string name, bool wholeRing)
    {
        var standing = 0;
        foreach (var point in Ring(stop, tolerance))
        {
            var at = new CellFrame(cell, new AcFrame(point, Quaternion.Identity));
            var found = world.TryFixedGroundZ(at, foot, out var z, out _);
            output.WriteLine($"   ring ({point.X:F2}, {point.Y:F2}): {(found ? $"floor at {z:F2}" : "no floor")}");
            if (wholeRing || point == stop)
            {
                Assert.True(found, $"{name}: no walkable floor through the statics at ({point.X:F2}, {point.Y:F2}) of 0x{cell:X8}, {Vector2.Distance(new Vector2(point.X, point.Y), new Vector2(stop.X, stop.Y)):F2} m from the stop");
                Assert.InRange(z, stop.Z - 0.2f, stop.Z + 0.1f);
            }

            standing += found ? 1 : 0;
        }

        output.WriteLine($"   {standing} of 9 points of the stop ring stand");
    }

    /// <summary>
    /// A headless walk inside Thelnoth's cellar with his body hosted at his point (Setup 0x02000001, a creature, as the session hosts an NPC): the body placed at the cellar's entry, the runner on the human body with the
    /// confirmer and the landblock's room graph, stepped at 60 Hz with the host until the run ends or <paramref name="seconds"/> pass. Answers the runner, where the body stands and its cylinder distance to him.
    /// </summary>
    private (NavRunner Runner, CellFrame At, float Cylinder) CellarWalk(NavGoal goal, double seconds, string name)
    {
        var world = ServerObjectPhysicsTests.HoltburgWorld(Dats);
        var host = new ServerObjectPhysicsHost(world, Dats, log: NullLogSink.Instance);
        host.Create(new ServerObjectDescription(Thelnoth, HumanSetupId, 1f, (uint)PhysicsState.ReportCollisions, IsCreature: true, IsSelf: false, DescriptionFlags: 0), new CellFrame(ThelnothCellar, new AcFrame(ThelnothAt, Quaternion.Identity)));
        Assert.True(host.Hosts(Thelnoth));
        Assert.True(host.TryGetObject(Thelnoth, out var npc));
        var map = new NavMap();
        map.Attach(NavTileBuilder.Build(new LandblockCollisionBuilder(Dats).Build(LandblockCoord.Holtburg)));
        var cells = LandblockCellSet.Load(new CellStructureLibrary(Dats), LandblockCoord.Holtburg);
        var h = new ActionHarness(offline: true);
        var now = 0.0;
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, name);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230), sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.True(walker.Place(new CellFrame(ThelnothCellar, RetailFrames.SetHeading(new AcFrame(CellarEntry, Quaternion.Identity), 320f))), "placed at the cellar's entry");
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new IndoorOutdoorPathSource(new PathPlanner(map), cells), confirmer, audit, new NavRunnerOptions { Name = name });
        runner.Start(goal, now);
        while (!runner.IsDone && now < seconds)
        {
            now += 1.0 / Fps;
            walker.Controller.Tick(now);
            host.Tick();
            runner.Step(now);
            h.Tick(1.0 / Fps);
        }

        var cylinder = PositionFrames.CylinderDistance(walker.Physics.Radius, walker.Physics.Height, walker.Position, npc.Radius, npc.Height, npc.Position);
        output.WriteLine($"{name}: {runner.State} after {now:F1} s; {runner.VerdictFields()}; at {walker.Position}, {Vector2.Distance(new Vector2(walker.Position.Origin.X, walker.Position.Origin.Y), new Vector2(ThelnothAt.X, ThelnothAt.Y)):F2} m from his point, {cylinder:F2} m cylinder to cylinder; {runner.FailureReason}");
        runner.Dispose();
        return (runner, walker.Position, cylinder);
    }

    /// <summary>
    /// A headless walk to Renald's front room with his body hosted at his point and the shop's front door open (as <see cref="RenaldScene"/> stages them): the body placed at <paramref name="from"/>, the runner on the human body with the
    /// confirmer and the landblock's room graph, stepped at 60 Hz until the run ends (60 s at most), then one more second for the body to settle. Answers the runner, where the body stands and its cylinder distance to him.
    /// </summary>
    private (NavRunner Runner, CellFrame At, float Cylinder) FrontRoomWalk(CellFrame from, NavGoal goal, string name)
    {
        var world = ServerObjectPhysicsTests.HoltburgWorld(Dats);
        var host = new ServerObjectPhysicsHost(world, Dats, log: NullLogSink.Instance);
        host.Create(new ServerObjectDescription(FrontDoor, 0x020019FF, 1f, (uint)(PhysicsState.ReportCollisions | PhysicsState.Ethereal), IsCreature: false, IsSelf: false, DescriptionFlags: 0x1000), new CellFrame(0xA9B40029, new AcFrame(new Vector3(132.56f, 17.115f, 94f), new Quaternion(0f, 0f, -1f, 0f))));
        host.Create(new ServerObjectDescription(Renald, HumanSetupId, 1f, (uint)PhysicsState.ReportCollisions, IsCreature: true, IsSelf: false, DescriptionFlags: 0), new CellFrame(RenaldFrontRoomCell, new AcFrame(RenaldAt, Quaternion.Identity)));
        Assert.True(host.TryGetObject(Renald, out var npc));
        var map = new NavMap();
        map.Attach(NavTileBuilder.Build(new LandblockCollisionBuilder(Dats).Build(LandblockCoord.Holtburg)));
        var cells = LandblockCellSet.Load(new CellStructureLibrary(Dats), LandblockCoord.Holtburg);
        var h = new ActionHarness(offline: true);
        var now = 0.0;
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, name);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230), sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.True(walker.Place(from), $"placed at {from}");
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new IndoorOutdoorPathSource(new PathPlanner(map), cells), confirmer, audit, new NavRunnerOptions { Name = name });
        runner.Start(goal, now);
        var settle = -1.0;
        while ((!runner.IsDone || now < settle) && now < 60.0)
        {
            now += 1.0 / Fps;
            walker.Controller.Tick(now);
            host.Tick();
            runner.Step(now);
            h.Tick(1.0 / Fps);
            if (runner.IsDone && settle < 0)
            {
                settle = now + 1.0;
            }
        }

        var cylinder = PositionFrames.CylinderDistance(walker.Physics.Radius, walker.Physics.Height, walker.Position, npc.Radius, npc.Height, npc.Position);
        output.WriteLine($"{name}: {runner.State} after {now:F1} s; {runner.VerdictFields()}; {runner.FailureReason}");
        runner.Dispose();
        return (runner, walker.Position, cylinder);
    }

    private static IEnumerable<Vector3> Ring(Vector3 centre, float radius)
    {
        yield return centre;
        for (var k = 0; k < 8; k++)
        {
            var a = k * MathF.PI / 4f;
            yield return centre + new Vector3(radius * MathF.Cos(a), radius * MathF.Sin(a), 0f);
        }
    }

    private LegRig Rig(HuntingGround ground)
    {
        var world = ServerObjectPhysicsTests.HoltburgWorld(Dats);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230));
        return new LegRig(new JourneyLegPlanner(Dats), new J1JourneyRig(), Map(ground), new CellFrame(ground.Home.LifestoneCell, new AcFrame(ground.Home.LifestonePoint, Quaternion.Identity)), world, new RouteConfirmer(world, walker.Physics.Setup!), walker.Physics.Setup!.Spheres[0]);
    }

    /// <summary>The body in Renald's front room with the door open and Renald behind his counter, as <see cref="F6RenaldMoveToDatTests"/> stages him.</summary>
    private Scene RenaldScene(Vector3 at, float heading = 90f, float holdMetres = 0.05f)
    {
        var world = ServerObjectPhysicsTests.HoltburgWorld(Dats);
        var host = new ServerObjectPhysicsHost(world, Dats, log: NullLogSink.Instance);
        host.Create(new ServerObjectDescription(FrontDoor, 0x020019FF, 1f, (uint)(PhysicsState.ReportCollisions | PhysicsState.Ethereal), IsCreature: false, IsSelf: false, DescriptionFlags: 0x1000), new CellFrame(0xA9B40029, new AcFrame(new Vector3(132.56f, 17.115f, 94f), new Quaternion(0f, 0f, -1f, 0f))));
        host.Create(new ServerObjectDescription(Renald, HumanSetupId, 1f, (uint)PhysicsState.ReportCollisions, IsCreature: true, IsSelf: false, DescriptionFlags: 0), new CellFrame(RenaldFrontRoomCell, new AcFrame(RenaldAt, Quaternion.Identity)));
        Assert.True(host.Hosts(Renald));
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230));
        world.UseTime(0.0);
        Assert.True(walker.Place(new CellFrame(RenaldFrontRoomCell, RetailFrames.SetHeading(new AcFrame(at, Quaternion.Identity), heading))), $"placed at {at}");
        Assert.True(Vector2.Distance(new Vector2(walker.Position.Frame.Origin.X, walker.Position.Frame.Origin.Y), new Vector2(at.X, at.Y)) < holdMetres, $"the placement held the body at {at}: {walker.Position}");
        var targets = new Targets { [walker.Physics.Id] = walker.Physics };
        Assert.True(host.TryGetObject(Renald, out var npc));
        targets[Renald] = npc;
        npc.TargetWorld = targets;
        walker.Physics.TargetWorld = targets;
        return new Scene(world, host, walker);
    }

    /// <summary>The terrain tiles of the landblocks between the home lifestone and the vendors, with a landblock of margin (as <see cref="ResupplyRouteDatTests"/>).</summary>
    private NavMap Map(HuntingGround ground)
    {
        var blocks = ground.Vendors.Select(v => v.StopCell).Append(ground.Home.LifestoneCell).Select(LandblockCoord.FromId).ToList();
        var (x0, x1) = (blocks.Min(c => c.X) - 1, blocks.Max(c => c.X) + 1);
        var (y0, y1) = (blocks.Min(c => c.Y) - 1, blocks.Max(c => c.Y) + 1);
        var builder = new LandblockCollisionBuilder(Dats);
        var map = new NavMap();
        for (var x = x0; x <= x1; x++)
        {
            for (var y = y0; y <= y1; y++)
            {
                map.Attach(NavTileBuilder.Build(builder.Build(new LandblockCoord((byte)x, (byte)y))));
            }
        }

        return map;
    }

    /// <summary>The journeys' leg planner from the home lifestone over the Holtburg collision, with the walker's confirmer on the human body.</summary>
    private sealed record LegRig(JourneyLegPlanner Planner, J1JourneyRig View, NavMap Map, CellFrame Home, PhysicsWorld World, RouteConfirmer Confirmer, PhysSphere Foot)
    {

        /// <summary>The point request the resupply station sends for a stop (<c>ResupplyRun.Meet</c>: the stop cell and point, no object, no range), planned from the lifestone.</summary>
        public (NavPath Path, NavGoal Goal) Plan(string name, uint stopCell, Vector3 stop) => Plan(Home, name, stopCell, stop);

        /// <summary>The same request planned from <paramref name="from"/> (a later leg of the walk).</summary>
        public (NavPath Path, NavGoal Goal) Plan(CellFrame from, string name, uint stopCell, Vector3 stop)
        {
            var request = new TravelRequest(new TeleportDestination(name, [], new WorldLocation(name, new CellPosition(stopCell, stop), HasHeight: true, HeadingDegrees: 0), "stop confirm test"), TravelPurpose.Vendor);
            var goal = Planner.GoalOf(View.View, request);
            var path = Planner.SourceFor(from.CellId, JourneyLegPlanner.GoalCellOf(goal), Map).Plan(from, goal, new NavScars());
            return (path, goal);
        }
    }

    private sealed class Targets : Dictionary<uint, ITargetable>, ITargetWorld
    {
        public ITargetable? FindObject(uint objectId) => TryGetValue(objectId, out var t) ? t : null;
    }
}
