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
/// M6 1 (D42's fresh climbs, 2026-11-02): the walks from Holtburg's lifestone to the town's scriveners, on the retail dats. Climb 2's learn trips could not reach the Apprentice Scriveners of Life and
/// Creature Magic, even as object goals, and reached the Apprentice Scrivener of Item and War Magic 20216 (Flame Bolt II) only once he had been seen: the walker's physics pre-check
/// (<see cref="RouteConfirmer"/>) refused the push into the rooms behind their counters (the Life apprentice's push stands on his counter, 117.08 over the floor's 116.0; the Creature
/// apprentice's doorway approach at (182.56, 83.96) has no floor for the foot), and 20216 stands on a balcony (119.0 in the outdoor cell 0xA9B3003C), so a walk to his own point runs into
/// the building's wall. The catalog's stop (<see cref="ScrivenerPlace.StopCell"/>) puts each walk on a clear floor within his reach: the front room 0xA9B30100 before the counters, the
/// street under the balcony. Here: the old walk to the balcony is refused as live (the counter rooms' old walks now confirm and are walked: M6 7 G and the world walk's W8), each stop is planned and
/// confirmed from the lifestone, and the walker walks it and ends within the scrivener's reach.
/// </summary>
/// <remarks>The scriveners' points are their landblock_instance rows (the pinned world SQL v0.9.296, read-only); the Holtburg scriveners' UseRadius is 3 m (<see cref="ShopLeg.UnseenVendorReachMetres"/>'s remarks).</remarks>
[Trait("Tier", "Exhaustive")]
public sealed class HoltburgScrivenerWalkDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const string Den = "black-spawn-den-aluvian";
    private const double Fps = 60.0;
    private const float SeenRange = 3f; // the live walks' "within 3.0 m": his UseRadius
    private const double Reach = 3.0 + 1.0; // ShopLeg.ReachOf at UseRadius 3: the station's "met" line
    private const double BodyClearMetres = 1.2; // two human cylinders (0.48 m each) and a margin: the stop is not against his body (D120)

    /// <summary>The scriveners with a stop, by guid: Creature I, Life I, War III (Journeyman), Item and War II (20216).</summary>
    private static readonly uint[] Stopped = [0x7A9B300A, 0x7A9B3009, 0x7A9B3019, 0x7A9B300D];

    /// <summary>
    /// The scriveners of the upper floor (0xA9B30107 and 0xA9B30108, 119.0: Life and Creature II and up): closed by M6 7 G (m6-holtburg-upper-floor). The building's own way up is its inner stair 0xA9B3010F between the ground
    /// floor's 0xA9B30103 and the upper floor's 0xA9B3010A (M6 4 W6b); the balcony doors' lanes are cut (M6 4 w6a: the tile cuts no lane for a door whose sill no floor steps down to the ground from along it); the last gap was the
    /// front room's doorway into the stair room, the pillar on that portal's centre, and the room graph now crosses beside it (<see cref="HoltburgWalkOutDatTests"/>).
    /// </summary>
    private static readonly uint[] UpperFloor = [0xA9B30107, 0xA9B30108];

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void The_old_walk_to_the_balcony_is_refused_by_the_pre_check_and_the_counter_rooms_confirm_and_are_walked_within_reach()
    {
        var rig = Rig();
        foreach (var vendor in Holtburg().Where(v => Stopped.Contains(v.Guid)))
        {
            var plan = rig.Plan(vendor.Cell, vendor.Point, ShopLeg.UnseenVendorReachMetres);
            var unseen = rig.Confirm(plan);
            var seen = rig.Confirm(rig.PlanObject(vendor));
            output.WriteLine($"{vendor.Name} 0x{vendor.Cell:X8}: unseen {unseen}, seen {seen}; {rig.Confirmer.Failure}");
            if (vendor.Cell is 0xA9B30103 or 0xA9B30102)
            {
                // The Creature apprentice's room (M6 7 G): climb 2's refusal was the pillar on the portal's centre; the room graph crosses beside it. The Life apprentice's room (the world walk's W8):
                // the placement on the planned line beside his counter is refused, and the body stepped there from where it last stood gets there; the walk ends in the front room within his reach.
                Assert.Equal(RouteConfirmStatus.Confirmed, unseen);
                var (state, at) = Walk(rig, plan.Goal, vendor.Name);
                var distance = Distance(at.CellId, at.Origin, vendor.Cell, vendor.Point);
                output.WriteLine($"  walked: {state} at 0x{at.CellId:X8} {at.Origin}, {distance:F2} m from him");
                Assert.Equal(NavRunState.Arrived, state);
                Assert.InRange(distance, BodyClearMetres, Reach);
                continue;
            }

            Assert.NotEqual(RouteConfirmStatus.Confirmed, unseen);
            if (vendor.Guid != 0x7A9B300D)
            {
                Assert.NotEqual(RouteConfirmStatus.Confirmed, seen); // 20216 as an object goal was the walk that worked (climb 2, trip 4)
            }
        }
    }

    [RequiresDatsFact]
    public void Each_stop_is_confirmed_from_the_lifestone_and_the_walk_to_it_ends_within_the_scriveners_reach()
    {
        var rig = Rig();
        var stopped = Holtburg().Where(v => Stopped.Contains(v.Guid)).ToList();
        Assert.Equal(Stopped.Length, stopped.Count);
        foreach (var vendor in stopped)
        {
            Assert.NotNull(vendor.StopPoint);
            var stop = vendor.StopPoint!.Value;
            var (path, goal) = rig.Plan(vendor.StopCell, stop, 0);
            Assert.Equal(NavPathStatus.Found, path.Status);
            Assert.Equal(RouteConfirmStatus.Confirmed, rig.Confirmer.ConfirmAll(path.Waypoints, goal.StopTolerance));

            var (state, at) = Walk(rig, goal, vendor.Name);
            var distance = Distance(at.CellId, at.Origin, vendor.Cell, vendor.Point);
            output.WriteLine($"{vendor.Name}: stop 0x{vendor.StopCell:X8} {stop}: {state} at 0x{at.CellId:X8} {at.Origin}, {distance:F2} m from him");
            Assert.Equal(NavRunState.Arrived, state);
            Assert.InRange(distance, BodyClearMetres, Reach);
        }
    }

    [RequiresDatsFact]
    public void The_upper_floor_scriveners_plan_in_by_the_front_door_and_up_the_stair_and_the_physics_confirms()
    {
        var rig = Rig();
        foreach (var vendor in Holtburg().Where(v => UpperFloor.Contains(v.Cell)))
        {
            Assert.Null(vendor.StopPoint);
            var plan = rig.PlanObject(vendor);
            var status = rig.Confirm(plan);
            output.WriteLine($"{vendor.Name} 0x{vendor.Cell:X8}: plan {plan.Path.Status} {plan.Path.Reason}; {status} {rig.Confirmer.Failure}");
            output.WriteLine("  cells " + string.Join(' ', plan.Path.CellsEntered.Select(c => c.ToString("X8"))) + "; waypoints " + string.Join(' ', plan.Path.Waypoints.Select(w => $"0x{w.At.CellId:X8}({w.At.Origin.X:0.00},{w.At.Origin.Y:0.00},{w.At.Origin.Z:0.00})")));

            // M6 4 W6a + W6b: the plan is whole: in by the front door 0xA9B30104, the front room, the stair room 0xA9B30103, the inner stair 0xA9B3010F, the upper floor, and never by a balcony door.
            Assert.Equal(NavPathStatus.Found, plan.Path.Status);
            Assert.Equal(vendor.Cell, plan.Path.Waypoints[^1].At.CellId);
            Assert.Equal([0xA9B30104u, 0xA9B30100u, 0xA9B30103u, 0xA9B3010Fu, 0xA9B3010Au], plan.Path.CellsEntered.Take(5));
            Assert.DoesNotContain(plan.Path.CellsEntered, c => c is 0xA9B30106 or 0xA9B30110);

            // The whole walk confirms (M6 7 G: the doorway into the stair room crosses beside the pillar).
            Assert.Equal(RouteConfirmStatus.Confirmed, status);
        }
    }

    /// <summary>
    /// M6 4 W2 (<c>m6-npc-in-door-lane</c>, climb 3 of M6 item 1): the walks to the Life and Creature apprentices stalled live 5.5 m before the front door 0x7A9B3005 against the Scrivener of
    /// Item Magic 0x7A9B301B, who stands in the way to the door's lane (her landblock_instance row: the outdoor cell 0xA9B3003C at (187.288, 86.7918, 116.005); her weenie 20214's Setup, DID 1,
    /// is the human 0x02000001; PhysicsState, int 93, 0x200418). Here her body stands in the walker's physics as the server object it is live (<see cref="ServerObjectPhysicsHost"/>). The plan
    /// without the bodies runs through her disc (the live cause); with her body on the plan (<see cref="NavRunner.BodyFeed"/>) the route goes round her, the physics confirms it, and the walker
    /// arrives at the Life apprentice's stop without touching her. Measured: without the bodies the walk sticks against her body 0.96 m from her centre (3 plans, 12
    /// rungs, Blocked, as live); with them it is one plan, round her (its nearest 1.05 m, her disc 1.16 m), arriving 1.97 m from the Life apprentice.
    /// </summary>
    [RequiresDatsFact]
    public void The_walk_to_the_front_room_goes_round_the_Scrivener_of_Item_Magic_before_the_door_and_arrives()
    {
        var rig = Rig();
        var radius = HostileCreatureFeed.SetupRadii(Dats)(OfflineWalker.HumanSetupId)!.Value;
        var her = new CellFrame(ItemMagicCell, new AcFrame(ItemMagicPoint, Quaternion.Identity));
        var body = new NavBody(ItemMagicGuid, "Scrivener of Item Magic", NavMap.FromCellFrame(her).Global, radius, ItemMagicPoint.Z, Outdoor: true);
        var feed = new FixedBodies(body);
        var life = Holtburg().Single(v => v.Guid == 0x7A9B3009);
        var (bare, goal) = rig.Plan(life.StopCell, life.StopPoint!.Value, 0);
        var start = NavMap.FromCellFrame(rig.Home).Global;
        var layer = new NpcBodies();
        layer.Refresh(feed, start, 500f, 0, goal.Point.Global, rig.Map);
        Assert.Single(layer.Seen); // on the street's level, not holding the goal
        Assert.Equal(ItemMagicGuid, layer.FirstMeeting(bare.Waypoints, start)?.Guid); // the cause: the plan without her body runs through it

        new ServerObjectPhysicsHost(rig.World, Dats).Create(new ServerObjectDescription(ItemMagicGuid, OfflineWalker.HumanSetupId, 1f, ItemMagicPhysicsState, IsCreature: true, IsSelf: false, DescriptionFlags: 0), her);
        var blind = Walk(rig, goal, life.Name + " (no bodies)", null, body.Centre);
        output.WriteLine($"{life.Name} without the bodies: {blind.State} at 0x{blind.At.CellId:X8} {blind.At.Origin}; nearest her centre {blind.Nearest:F2} m");
        // M6 sprint (2026-11-19): it sticks against her as live, two body radii from her centre; the replan then leaves the stuck spot's scar outward (the start's scar is no longer skipped by the pulled lines,
        // ScrivenerDoorStallDatTests), so it goes round her and arrives (before: 3 plans, 12 rungs, Blocked)
        Assert.True(blind.Nearest < (2 * NavCreatures.PlayerRadiusMetres) + 0.05f, $"the blind walk came {blind.Nearest:F2} m from her centre");
        Assert.Equal(NavRunState.Arrived, blind.State);
        var walk = Walk(rig, goal, life.Name, feed, body.Centre);
        output.WriteLine($"{life.Name} round her: {walk.State} at 0x{walk.At.CellId:X8} {walk.At.Origin}; nearest her centre {walk.Nearest:F2} m (disc {NpcBodies.DiscRadius(radius):F2} m); stamped {walk.Stamped}; plans {walk.Plans}");
        Assert.Equal(NavRunState.Arrived, walk.State);
        Assert.Equal(1, walk.Stamped);
        Assert.True(walk.Nearest > 2 * NavCreatures.PlayerRadiusMetres, $"the walker came {walk.Nearest:F2} m from her centre (two body radii: touching)");
        Assert.InRange(Distance(walk.At.CellId, walk.At.Origin, life.Cell, life.Point), BodyClearMetres, Reach);
    }

    /// <summary>
    /// M6 sprint (live 2026-11-19 01:51:54 to 01:52:29 UTC, run 3, and 00:05 to 00:07 UTC, run 2): the walk from Archmage Cindrue's hall to the Life apprentice reached the street with the door found only after the plan, and
    /// stood against the Scrivener of Item Magic at (186.883, 87.663) in 0xA9B3003C, 0.96 m from her centre, inside her disc. A disc holding the start was dropped, so each replan ran on into her: 3 plans, 12 rungs, Blocked
    /// after 93 s. From that very spot, with her body in the physics and on the plan, the walk now leaves round her and arrives at the Life apprentice's stop.
    /// </summary>
    [RequiresDatsFact]
    public void From_against_the_Scrivener_of_Item_Magic_the_walk_leaves_round_her_and_arrives()
    {
        var rig = Rig();
        var radius = HostileCreatureFeed.SetupRadii(Dats)(OfflineWalker.HumanSetupId)!.Value;
        var her = new CellFrame(ItemMagicCell, new AcFrame(ItemMagicPoint, Quaternion.Identity));
        var body = new NavBody(ItemMagicGuid, "Scrivener of Item Magic", NavMap.FromCellFrame(her).Global, radius, ItemMagicPoint.Z, Outdoor: true);
        var life = Holtburg().Single(v => v.Guid == 0x7A9B3009);
        var (_, goal) = rig.Plan(life.StopCell, life.StopPoint!.Value, 0);
        new ServerObjectPhysicsHost(rig.World, Dats).Create(new ServerObjectDescription(ItemMagicGuid, OfflineWalker.HumanSetupId, 1f, ItemMagicPhysicsState, IsCreature: true, IsSelf: false, DescriptionFlags: 0), her);
        var stuck = new CellFrame(0xA9B3003Cu, new AcFrame(new Vector3(186.883f, 87.663f, 116.005f), Quaternion.Identity)); // the live stall point (01:52:14 UTC)
        var layer = new NpcBodies();
        layer.Refresh(new FixedBodies(body), NavMap.FromCellFrame(stuck).Global, 500f, 0, goal.Point.Global, rig.Map);
        output.WriteLine($"from against her, on the dats' lattice: seen {layer.Seen.Count}, off level {layer.OffLevel} {string.Join(",", layer.OffLevelBodies.Select(o => $"{o.Body.Name} dz={o.Gap:0.00}"))}");
        Assert.Single(layer.Seen); // on the dats' lattice she is on the street's level: the live walk left one body out as off level on every plan (named in its log from now on)
        var walk = Walk(rig, goal, life.Name + " (from against her)", new FixedBodies(body), body.Centre, stuck);
        output.WriteLine($"{life.Name} from against her: {walk.State} at 0x{walk.At.CellId:X8} {walk.At.Origin}; nearest her centre {walk.Nearest:F2} m; plans {walk.Plans}");
        Assert.Equal(NavRunState.Arrived, walk.State);
        Assert.InRange(Distance(walk.At.CellId, walk.At.Origin, life.Cell, life.Point), BodyClearMetres, Reach);
    }

    /// <summary>
    /// M6 item 14 (live 2026-11-12 01:02 UTC, <c>m6-forge-in-smithy-lane</c>, closed): the walk to Sedor Wystan the Blacksmith (the leather set's vendor, 0xA9B40100) ended "no path: npc-in-lane" after 3 s. The body in the lane
    /// was no townsperson: it is the Salvaging Forge (ACE weenie 30461, a creature to the client; landblock_instance guid 0x7A9B4065 at (68.113, 131.614, 66.005) in 0xA9B40000; Setup 0x0200124A, DefaultScale 1.4), which stands
    /// for good 2.5 m west of the lane's last outdoor point before the smithy's door. The body feed sized it by its Setup's radius times its scale (2.07 x 1.4 = 2.90 m, a disc of 3.38 m with the walker's radius), and that disc
    /// covered the lane, which the walker's own physics passes 3.3 m from the forge's centre: no wait could clear it. The feed now sizes a body wider than a person by what its collision shape blocks
    /// (<see cref="NpcBodyFeed.BodyRadius"/>). Here, on the dats: at the old disc the run ends Blocked as live (the pin of the cause); at the feed's radius the walker reaches the blacksmith, with the forge both a body on the
    /// plan and an object in the physics.
    /// </summary>
    [RequiresDatsFact]
    public void The_walk_to_the_blacksmith_passes_the_Salvaging_Forge_now_that_its_body_is_its_collision_shape_not_its_models_bounds()
    {
        const uint forgeSetup = 0x0200124A;
        const float forgeScale = 1.4f;
        var rig = Rig();
        var smith = Lifestone.Client.Session.Mage.LeatherSet.Default!.Vendor;
        var lookup = HostileCreatureFeed.SetupRadii(Dats);
        var shapes = SetupShapes.Of(lookup);
        Assert.NotNull(shapes); // the hosts' lookup carries the shapes: no host passes a second one
        var bounds = lookup(forgeSetup)!.Value * forgeScale;
        var shape = shapes!.ShapeRadius(forgeSetup) * forgeScale;
        var radius = NpcBodyFeed.BodyRadius(bounds, shape);
        var human = lookup(OfflineWalker.HumanSetupId)!.Value;
        output.WriteLine($"Salvaging Forge: bounds {bounds:F2} m (the old disc {NpcBodies.DiscRadius(bounds):F2} m, live 3.38 m); collision shape {(shape is { } sh ? sh.ToString("F2") : "none")} m; the body now {radius:F2} m (disc {NpcBodies.DiscRadius(radius):F2} m)");
        output.WriteLine($"a person: bounds {human:F2} m, collision shape {(shapes.ShapeRadius(OfflineWalker.HumanSetupId) is { } hs ? hs.ToString("F2") : "none")} m; the body {NpcBodyFeed.BodyRadius(human, shapes.ShapeRadius(OfflineWalker.HumanSetupId)):F2} m, as before");
        Assert.Equal(human, NpcBodyFeed.BodyRadius(human, shapes.ShapeRadius(OfflineWalker.HumanSetupId))); // a townsperson is walked round exactly as before
        Assert.Equal(NpcBodyFeed.PersonRadiusMetres, human, 2);
        Assert.InRange(bounds, 2.85f, 2.95f);
        Assert.True(radius < bounds);

        var at = new CellFrame(0xA9B40016, new AcFrame(new Vector3(68.113f, 131.614f, 66.005f), Quaternion.Identity));
        var centre = NavMap.FromCellFrame(at).Global;
        var plan = rig.PlanObject(smith);
        Assert.Equal(NavPathStatus.Found, plan.Path.Status);
        var start = NavMap.FromCellFrame(rig.Home).Global;

        // The cause, pinned: at the model's bounds the plan runs through the disc and the run ends Blocked, as live.
        var old = new NavBody(0x7A9B4065, "Salvaging Forge", centre, bounds, 66.005f, Outdoor: true);
        var layer = new NpcBodies();
        layer.Refresh(new FixedBodies(old), start, 500f, smith.Guid, plan.Goal.Point.Global, rig.Map);
        Assert.Equal(old.Guid, layer.FirstMeeting(plan.Path.Waypoints, start)?.Guid);
        var blocked = Walk(rig, plan.Goal, smith.Name + " (the forge at its model's bounds)", new FixedBodies(old), centre);
        Assert.Equal(NavRunState.Blocked, blocked.State);

        // The fix: the forge as the feed counts it now, on the plan and in the physics (its Setup at its scale, the server object it is live): she reaches the blacksmith.
        new ServerObjectPhysicsHost(rig.World, Dats).Create(new ServerObjectDescription(old.Guid, forgeSetup, forgeScale, 0x600418, IsCreature: true, IsSelf: false, DescriptionFlags: 0), at);
        var forge = old with { Radius = radius };
        var walk = Walk(rig, plan.Goal, smith.Name + " (the forge at its collision shape)", new FixedBodies(forge), centre);
        output.WriteLine($"{smith.Name}: {walk.State} at 0x{walk.At.CellId:X8} {walk.At.Origin}; nearest the forge's centre {walk.Nearest:F2} m; plans {walk.Plans}; stamped {walk.Stamped}");
        Assert.Equal(NavRunState.Arrived, walk.State);
        Assert.Equal(smith.Cell, walk.At.CellId);
        Assert.True(walk.Nearest > NpcBodies.DiscRadius(radius), $"the walker came {walk.Nearest:F2} m from the forge's centre, inside its disc {NpcBodies.DiscRadius(radius):F2} m");
        Assert.InRange(Distance(walk.At.CellId, walk.At.Origin, smith.Cell, smith.Point), 0, Reach);
    }

    /// <summary>
    /// M6 item 14 (live 2026-11-12 01:03 to 01:05 UTC, <c>m6-stair-head-stuck</c>, open): the walk from the healer's room to the upper-floor Apprentice Scrivener of Life Magic 20218 (0xA9B30107 at (176.79, 82.19, 119.0))
    /// ended "stuck after 3 plans and 13 rungs". It is not the forge's cause: no body closed a lane (no npc-in-lane; bodies in rooms are not on the plan at all, <c>TODO(m6-npc-indoor-bodies)</c>). She walked 340 m, in by the
    /// front door and up the inner stair, and stuck in the stair-head room 0xA9B3010A at (179.18, 85.52, 119.96): on the plan's straight leg from the stair's head to the room's east door, 0.96 m over the floor. Here, on the
    /// dats with no live object, the same request plans by that leg and the walker arrives within the scrivener's reach: the dats hold nothing there, so what she stood on is live (an object or a body in the room that neither
    /// the dats nor the room graph's plan hold). Named, not fixed; this pins that the walk itself is whole.
    /// </summary>
    [RequiresDatsFact]
    public void The_walk_to_the_upper_floor_Life_apprentice_arrives_on_the_dats_so_the_live_stuck_at_the_stair_head_is_a_live_object()
    {
        var rig = Rig();
        var (path, goal) = rig.Plan(0xA9B30107, new Vector3(176.79f, 82.19f, 119.005f), ShopLeg.UnseenVendorReachMetres);
        Assert.Equal(NavPathStatus.Found, path.Status);
        output.WriteLine("the plan's last waypoints: " + string.Join(' ', path.Waypoints.TakeLast(8).Select(w => $"0x{w.At.CellId:X8}({w.At.Origin.X:0.00},{w.At.Origin.Y:0.00},{w.At.Origin.Z:0.00})")));
        Assert.Contains(path.Waypoints, w => w.At.CellId == 0xA9B3010A); // through the stair-head room, where she stuck live

        var (state, at) = Walk(rig, goal, "the upper-floor Life apprentice");
        output.WriteLine($"the upper-floor Life apprentice: {state} at 0x{at.CellId:X8} {at.Origin}");
        Assert.Equal(NavRunState.Arrived, state);
        Assert.Equal(0xA9B30107u, at.CellId);
        Assert.InRange(at.Origin.Z, 118.9f, 119.1f); // on the floor, not 0.96 m over it
        Assert.InRange(Distance(at.CellId, at.Origin, 0xA9B30107, new Vector3(176.79f, 82.19f, 119.005f)), 0, Reach);
    }

    /// <summary>
    /// M6 sprint (run 6, 2026-11-19 22:53 to 2026-11-20 00:08 UTC): the walk to the Apprentice Scrivener of Item and War Magic 20215 (0x7A9B3008 in his counter's cell 0xA9B30101 at (183.749, 79.945, 116.005), the Foci of
    /// Artifice's only seller, create-list row 77964), an object goal in his cell, ended "stuck after 3 plans and 12 rungs" five times: the body pressed on his body 1.0 m from his point, in the front room 0xA9B30100, which
    /// the runner does not count as his room. The Foci of Artifice waited two hours. Here, on the dats with his body in the walker's physics: the object-goal walk as live arrives in the front room within his reach (the pin
    /// inverted by M6 sprint m6-vendor-reach: an object goal arrives within its range when nothing walls her off from him, or on a contact with him, not only in his room; <see cref="StoneholdVendorReachDatTests"/>), and his
    /// stop in the front room is planned, confirmed and walked from the lifestone, ending within his reach and clear of his body.
    /// </summary>
    [RequiresDatsFact]
    public void The_foci_of_artifice_seller_is_reached_at_his_stop_in_the_front_room_with_his_body_in_the_physics()
    {
        const uint sellerGuid = 0x7A9B3008;
        var rig = Rig();
        var seller = Holtburg().Single(v => v.Guid == sellerGuid);
        Assert.Equal(0xA9B30101u, seller.Cell);
        Assert.NotNull(seller.StopPoint);
        new ServerObjectPhysicsHost(rig.World, Dats).Create(new ServerObjectDescription(sellerGuid, OfflineWalker.HumanSetupId, 1f, ItemMagicPhysicsState, IsCreature: true, IsSelf: false, DescriptionFlags: 0x200), new CellFrame(seller.Cell, new AcFrame(seller.Point, Quaternion.Identity)));
        var him = NavMap.FromCellFrame(new CellFrame(seller.Cell, new AcFrame(seller.Point, Quaternion.Identity))).Global;

        var old = Walk(rig, rig.PlanObject(seller).Goal, seller.Name + " (as live: the object goal in his cell)", null, him);
        var reached = Distance(old.At.CellId, old.At.Origin, seller.Cell, seller.Point);
        output.WriteLine($"as live: {old.State} at 0x{old.At.CellId:X8} {old.At.Origin}, {reached:F2} m from him; nearest his centre {old.Nearest:F2} m");
        Assert.Equal(NavRunState.Arrived, old.State); // before m6-vendor-reach: Blocked against his body, as live
        Assert.Equal(0xA9B30100u, old.At.CellId); // the front room: outside his cell, within his range, nothing between
        Assert.InRange(reached, BodyClearMetres, Reach);

        var stop = seller.StopPoint!.Value;
        var (path, goal) = rig.Plan(seller.StopCell, stop, 0);
        Assert.Equal(NavPathStatus.Found, path.Status);
        Assert.Equal(RouteConfirmStatus.Confirmed, rig.Confirmer.ConfirmAll(path.Waypoints, goal.StopTolerance));
        var walk = Walk(rig, goal, seller.Name + " (his stop)", null, him);
        var distance = Distance(walk.At.CellId, walk.At.Origin, seller.Cell, seller.Point);
        output.WriteLine($"{seller.Name}: stop 0x{seller.StopCell:X8} {stop}: {walk.State} at 0x{walk.At.CellId:X8} {walk.At.Origin}, {distance:F2} m from him; nearest {walk.Nearest:F2} m");
        Assert.Equal(NavRunState.Arrived, walk.State);
        Assert.Equal(0xA9B30100u, walk.At.CellId);
        Assert.InRange(distance, BodyClearMetres, Reach);
    }

    /// <summary>
    /// M6 sprint (run 6, 2026-11-20 03:16:39 to 03:16:47 UTC, the video's 5:38:25): Lifestone Recall lands on the lifestone's own point (81.3304, 11.7974, 94.005) (ACE WorldObject_Magic.cs 1087-1095 @95ef426d), and the walk
    /// to Archmage Cindrue left it to the north-east: two touches of the stone, two stuck rungs, then a touch of Alcott, who stands 3.0 m from its centre (landblock_instance 0x7A9B4073 at (83.3001, 14.0365, 94.005),
    /// the human Setup). The planner knew no lifestone: the body feed counted creatures only. Here, on the dats, with the stone and Alcott both in the walker's physics and both on the plan as the feed counts them now
    /// (<see cref="NpcBodyFeed.IsBody"/>, the stone's radius by <see cref="NpcBodyFeed.BodyRadius"/> over its Setup 0x020002EE): from the recall's arrival the walker reaches Cindrue's stop with both on the plan and never comes inside Alcott's disc.
    /// </summary>
    [RequiresDatsFact]
    public void From_the_recall_arrival_on_the_lifestone_the_walk_to_Cindrue_keeps_clear_of_Alcott_with_the_stone_on_the_plan()
    {
        const uint stoneGuid = 0x7A9B404F;
        const uint stoneSetup = 0x020002EE;
        const uint alcottGuid = 0x7A9B4073;
        var stoneAt = new Vector3(81.3304f, 11.7974f, 94.005f);
        var alcottAt = new Vector3(83.3001f, 14.0365f, 94.005f);
        var lookup = HostileCreatureFeed.SetupRadii(Dats);
        var shapes = SetupShapes.Of(lookup)!;
        var stoneRadius = NpcBodyFeed.BodyRadius(lookup(stoneSetup)!.Value, shapes.ShapeRadius(stoneSetup));
        var alcottRadius = NpcBodyFeed.BodyRadius(lookup(OfflineWalker.HumanSetupId)!.Value, shapes.ShapeRadius(OfflineWalker.HumanSetupId));
        output.WriteLine($"the lifestone: bounds {lookup(stoneSetup):F2} m, collision shape {shapes.ShapeRadius(stoneSetup)?.ToString("F2") ?? "none"} m, the body {stoneRadius:F2} m (disc {NpcBodies.DiscRadius(stoneRadius):F2} m); Alcott {alcottRadius:F2} m (disc {NpcBodies.DiscRadius(alcottRadius):F2} m)");

        var cindrue = GroundCatalog.Get(Den).Vendors.Single(v => v.Name == "Archmage Cindrue");
        var arrival = new CellFrame(0xA9B40019, new AcFrame(stoneAt, new Quaternion(0f, 0f, -0.543991f, 0.839091f)));
        var alcott = NavMap.FromCellFrame(new CellFrame(0xA9B40019, new AcFrame(alcottAt, Quaternion.Identity))).Global;
        var stone = NavMap.FromCellFrame(new CellFrame(0xA9B40019, new AcFrame(stoneAt, Quaternion.Identity))).Global;
        var alcottBody = new NavBody(alcottGuid, "Alcott", alcott, alcottRadius, alcottAt.Z, Outdoor: true);
        var stoneBody = new NavBody(stoneGuid, "Life Stone", stone, stoneRadius, stoneAt.Z, Outdoor: true);

        // Each walk on a fresh world: the stone and Alcott in the physics, the body placed on the stone's point by the client's sliding placement (live, the stone's create came 1.0 s after the
        // teleport, 03:16:30.478, 2.7 m from her). Offline the walk from that placement touches neither in either case, so this pins that the stone on the plan keeps the walk whole and clear of
        // Alcott; the live touches themselves are not reproduced here.
        (NavRunState State, CellFrame At, float Nearest, int Stamped, int Plans) Run(string name, params NavBody[] bodies)
        {
            var rig = Rig();
            var host = new ServerObjectPhysicsHost(rig.World, Dats);
            host.Create(new ServerObjectDescription(alcottGuid, OfflineWalker.HumanSetupId, 1f, ItemMagicPhysicsState, IsCreature: true, IsSelf: false, DescriptionFlags: 0), new CellFrame(0xA9B40019, new AcFrame(alcottAt, Quaternion.Identity)));
            var (path, goal) = rig.Plan(cindrue.StopCell, cindrue.StopPoint ?? cindrue.Point, 0);
            Assert.Equal(NavPathStatus.Found, path.Status);
            host.Create(new ServerObjectDescription(stoneGuid, stoneSetup, 1f, (uint)PhysicsState.ReportCollisions, IsCreature: false, IsSelf: false, DescriptionFlags: 0x4000), new CellFrame(0xA9B40019, new AcFrame(stoneAt, Quaternion.Identity)));
            return Walk(rig, goal, name, new FixedBodies(bodies), alcott, arrival);
        }

        var blind = Run("Cindrue from the recall (Alcott only, as live: the stone off the plan)", alcottBody);
        output.WriteLine($"as live: {blind.State} at 0x{blind.At.CellId:X8} {blind.At.Origin}; nearest Alcott's centre {blind.Nearest:F2} m; plans {blind.Plans}");
        var walk = Run("Cindrue from the recall (the stone and Alcott on the plan)", stoneBody, alcottBody);
        output.WriteLine($"with the stone: {walk.State} at 0x{walk.At.CellId:X8} {walk.At.Origin}; nearest Alcott's centre {walk.Nearest:F2} m (disc {NpcBodies.DiscRadius(alcottRadius):F2} m); stamped {walk.Stamped}; plans {walk.Plans}");
        Assert.Equal(NavRunState.Arrived, walk.State);
        Assert.True(walk.Stamped >= 2, $"the stone and Alcott are both on the plan: stamped {walk.Stamped}");
        Assert.True(walk.Nearest > NpcBodies.DiscRadius(alcottRadius) - NpcBodies.MeetSlackMetres, $"the walker came {walk.Nearest:F2} m from Alcott's centre, inside his disc");
    }

    private const uint ItemMagicGuid = 0x7A9B301B;
    private const uint ItemMagicCell = 0xA9B3003C;
    private const uint ItemMagicPhysicsState = 0x200418;
    private static readonly Vector3 ItemMagicPoint = new(187.288f, 86.7918f, 116.005f);

    private static IReadOnlyList<GroundVendor> Holtburg() => ScrivenerCatalog.Default.VendorsIn("Holtburg");

    /// <summary>The resupply's distance (<c>ResupplyRun.Meet</c>): the flat world metres and the height.</summary>
    private static double Distance(uint cellA, Vector3 a, uint cellB, Vector3 b)
    {
        var flat = Vector2.Distance(ResupplyPlanner.WorldMetres(cellA, a), ResupplyPlanner.WorldMetres(cellB, b));
        var dz = a.Z - b.Z;
        return Math.Sqrt((flat * flat) + (dz * dz));
    }

    private (NavRunState State, CellFrame At) Walk(WalkRig rig, NavGoal goal, string name)
    {
        var walk = Walk(rig, goal, name, null, Vector2.Zero);
        return (walk.State, walk.At);
    }

    /// <summary>The walk on the runner over the walker's physics, with <paramref name="bodies"/> as its NPC bodies; the nearest the walker came to <paramref name="watch"/> (global metres).</summary>
    private (NavRunState State, CellFrame At, float Nearest, int Stamped, int Plans) Walk(WalkRig rig, NavGoal goal, string name, INavBodyFeed? bodies, Vector2 watch, CellFrame? from = null)
    {
        var at = from ?? rig.Home;
        var h = new ActionHarness(offline: true);
        var now = 0.0;
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, name);
        var walker = OfflineWalker.Create(Dats, rig.World, NullLogSink.Instance, new OfflinePlayerWeenie(230), sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        rig.World.UseTime(0.0);
        Assert.True(walker.Place(at), $"placed at {at}");
        using var confirmer = new RouteConfirmer(rig.World, walker.Physics.Setup!);
        var source = rig.Planner.SourceFor(at.CellId, JourneyLegPlanner.GoalCellOf(goal), rig.Map);
        var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, confirmer, audit, new NavRunnerOptions { Name = name }) { BodyFeed = bodies };
        runner.Start(goal, now);
        var nearest = float.MaxValue;
        while (!runner.IsDone && now < 180.0)
        {
            now += 1.0 / Fps;
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / Fps);
            nearest = MathF.Min(nearest, Vector2.Distance(NavMap.FromCellFrame(walker.Position).Global, watch));
        }

        output.WriteLine($"{name}: {runner.State} after {now:F1} s; {runner.VerdictFields()}; {runner.FailureReason}");
        var state = runner.State;
        var stamped = runner.Npcs.Stamped.Count;
        var plans = runner.Plans;
        runner.Dispose();
        return (state, walker.Position, nearest, stamped, plans);
    }

    private WalkRig Rig()
    {
        var ground = GroundCatalog.Get(Den);
        var world = ServerObjectPhysicsTests.HoltburgWorld(Dats);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230));
        var builder = new LandblockCollisionBuilder(Dats);
        var map = new NavMap();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -2; dy <= 1; dy++)
            {
                map.Attach(NavTileBuilder.Build(builder.Build(LandblockCoord.Holtburg.Offset(dx, dy)!.Value)));
            }
        }

        var home = new CellFrame(ground.Home.LifestoneCell, new AcFrame(ground.Home.LifestonePoint, Quaternion.Identity));
        return new WalkRig(new JourneyLegPlanner(Dats), new J1JourneyRig(), map, home, world, new RouteConfirmer(world, walker.Physics.Setup!));
    }

    /// <summary>A body feed of fixed bodies (the NPCs a live session's object table would list).</summary>
    private sealed class FixedBodies(params NavBody[] bodies) : INavBodyFeed
    {
        public void Fill(Vector2 from, float maxMetres, List<NavBody> into)
        {
            into.Clear();
            into.AddRange(bodies.Where(b => Vector2.Distance(b.Centre, from) <= maxMetres));
        }
    }

    /// <summary>The journeys' leg planner from Holtburg's lifestone over the Holtburg collision, with the walker's confirmer on the human body (as <see cref="VendorStopConfirmDatTests"/>).</summary>
    private sealed record WalkRig(JourneyLegPlanner Planner, J1JourneyRig View, NavMap Map, CellFrame Home, PhysicsWorld World, RouteConfirmer Confirmer)
    {
        /// <summary>A point request from the lifestone (the resupply's <c>RoomRequest</c>): the cell and point, with <paramref name="range"/> as its stop when positive.</summary>
        public (NavPath Path, NavGoal Goal) Plan(uint cell, Vector3 point, double range)
        {
            var request = new TravelRequest(new TeleportDestination("scrivener", [], new WorldLocation("scrivener", new CellPosition(cell, point), HasHeight: true, HeadingDegrees: 0), "walk test"), TravelPurpose.Vendor, Range: range);
            var goal = Planner.GoalOf(View.View, request);
            return (Planner.SourceFor(Home.CellId, JourneyLegPlanner.GoalCellOf(goal), Map).Plan(Home, goal, new NavScars()), goal);
        }

        /// <summary>The seen request from the lifestone: an object goal in his own cell within his use radius.</summary>
        public (NavPath Path, NavGoal Goal) PlanObject(GroundVendor vendor)
        {
            var goal = NavGoal.ToObject(vendor.Guid, new NavPoint(vendor.Cell & 0xFFFF0000u, vendor.Point), SeenRange, needSight: false, LandDefs.IsOutdoorCell(vendor.Cell) ? null : vendor.Cell);
            return (Planner.SourceFor(Home.CellId, JourneyLegPlanner.GoalCellOf(goal), Map).Plan(Home, goal, new NavScars()), goal);
        }

        /// <summary>The pre-check of a planned walk as the runner makes it: up to the stop tolerance, or for an object goal up to its range.</summary>
        public RouteConfirmStatus Confirm((NavPath Path, NavGoal Goal) plan)
        {
            var (path, goal) = plan;
            if (path.Status != NavPathStatus.Found)
            {
                return RouteConfirmStatus.Failed;
            }

            var stop = goal.IsObject ? RouteConfirmer.ArcBeyondRange(path.Waypoints, goal.Point, goal.Range, goal.CellId) : goal.StopTolerance;
            return Confirmer.ConfirmAll(path.Waypoints, stop);
        }
    }
}
