using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 4 t4a on the retail dats: the walks of the trips by the Town Network, from each chosen town's Network drop (the travel file's edge destination) to the vendor the trip meets, as the stations walk them: the vendor's catalog point with
/// <see cref="ShopLeg.UnseenVendorReachMetres"/> as the stop (the resupply's <c>RoomRequest</c>, the buy-trip's point request). The tiles come from the headless streamer (the drop's 3 x 3 first, then the focus moved to each horizon's end);
/// the path source is the journeys' own (<see cref="JourneyLegPlanner.SourceFor(uint, uint, NavMap?)"/>); every plan is confirmed by the walker's physics (<see cref="RouteConfirmer"/>), as <c>GoalTileDatTests</c> does.
/// </summary>
/// <remarks>
/// The rows: the drops are <c>travel-routes.json</c>'s (network-to-fort-tethana 0x2681001D, network-to-dryreach 0xDA75002B, network-to-stonehold 0x64D5000B, network-to-neydisa 0x95D60033: weenie_properties_position of 43001, 42813, 42811,
/// 42828); the vendors are <c>Grounds/scriveners.json</c>'s (landblock_instance rows); the lever gate is <c>lever-doors.json</c>'s (landblock_instance 0x72581042 at (173.712, 83.9432) in 0x2581). All the pinned world SQL v0.9.296's.
/// </remarks>
[Trait("Tier", "Exhaustive")]
public sealed class TownNetworkTripDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const int MaxHops = 8;
    private const uint ZisahaGuid = 0x72481001;
    private const uint PhilomonGuid = 0x795D6007;
    private const uint WarMasterDryreach = 0x7DA75035;
    private const uint LifeMasterDryreach = 0x7DA7502F;
    private const uint CreatureMasterDryreach = 0x7DA75030;
    private const uint WarGrandMasterStonehold = 0x764D503C;
    private const uint LifeGrandMasterStonehold = 0x764D503B;
    private const uint CreatureGrandMasterStonehold = 0x764D503E;
    private const uint ItemMasterDryreach = 0x7DA75036;
    private const uint ItemGrandMasterStonehold = 0x764D503D;

    /// <summary>The nearest a walk to Zisaha may pass the gate door (metres): the walk goes round the fort's walls, never through the gate passage.</summary>
    private const float GateClearMetres = 20f;

    /// <summary>A walk behind the gate passes within this many metres of the gate door's origin (it goes through the gate passage).</summary>
    private const float GateCrossMetres = 4f;

    private DatSet Dats => fixture.Dats;

    private static TravelRoutes Routes => TravelRoutes.Default;

    /// <summary>
    /// Fort Tethana (the buy-trip's town for the Pyreal and Platinum Scarabs): from the Network drop 0x2681001D, on the plateau outside the walls, the walk to Zisaha's room 0x24810102 (two landblocks west) is one horizon and then the whole
    /// route, every plan confirmed, and no plan passes within 20 m of the lever gate (its nearest point is logged): her trip crosses no lever-only door. Every instance <c>lever-doors.json</c> lists behind the gate (the three Grand
    /// Masters, the town's own Portal to Town Network: every point 3 and 5 m round it): since W6a the planner plans through the gate (in the fixed world the door is open), so each walk behind it must pass the gate
    /// door's cell, and it is the rule, not the planner, that refuses those towns: <see cref="TownNetworkTrip.Choose"/> refuses a vendor behind the gate and (without the recall) the way back's first portal behind it, by <c>lever-doors.json</c>.
    /// </summary>
    [RequiresDatsFact]
    public void Fort_Tethanas_drop_reaches_Zisaha_clear_of_the_lever_gate_and_every_walk_behind_the_gate_crosses_it_and_the_town_rule_refuses_it()
    {
        var door = LeverDoors.Default.Doors.Single(d => d.Town == "Fort Tethana");
        var gate = Global(door.DoorCell, door.DoorOrigin);
        var start = Drop("network-to-fort-tethana");
        Assert.Equal(0x2681001Du, start.CellId);
        var zisaha = ScrivenerCatalog.Default.ScarabSellers.SelectMany(s => s.Places).Single(p => p.Guid == ZisahaGuid);
        Assert.Null(LeverDoors.Default.Before(ZisahaGuid));

        var (rig, plans) = Walk(start, zisaha.Cell, zisaha.Point, ShopLeg.UnseenVendorReachMetres);
        using (rig)
        {
            var last = plans[^1];
            Assert.Equal(NavPathStatus.Found, last.Status);
            Assert.Equal([0x24810101u, 0x24810100u, 0x24810102u], last.CellsEntered);
            Assert.Equal(RouteConfirmStatus.Confirmed, rig.Confirm(last, rig.Goal(zisaha.Cell, zisaha.Point, ShopLeg.UnseenVendorReachMetres)));
            var nearest = plans.SelectMany(p => p.Waypoints).Where(w => !w.Indoor).Min(w => Vector2.Distance(gate, Global(w.At.CellId, w.At.Origin)));
            output.WriteLine($"Zisaha: {plans.Count} plans; the nearest waypoint to the gate door 0x{door.DoorGuid:X8} is {nearest:0.0} m");
            Assert.True(nearest > GateClearMetres, $"a plan passes {nearest:0.0} m from the gate");
        }

        foreach (var guid in door.Behind)
        {
            var reached = 0;
            var points = ScrivenerCatalog.Default.Scriveners.SelectMany(s => s.Places).Where(p => p.Guid == guid).Select(p => (p.Cell, p.Point)).ToList();
            if (points.Count == 0)
            {
                var portal = Routes.Edges.Single(e => e.Guid == guid);
                points = [.. new[] { 3f, 5f }.SelectMany(r => Enumerable.Range(0, 8).Select(a => Ring(portal.Cell, portal.Origin, r, a)))];
            }

            foreach (var (cell, point) in points)
            {
                var (r, behind) = Walk(start, cell, point, ShopLeg.UnseenVendorReachMetres);
                r.Dispose();
                output.WriteLine($"behind the gate: 0x{guid:X8} at 0x{cell:X8} ({point.X:0.0}, {point.Y:0.0}): {behind[^1].Status} {behind[^1].Reason}; cells {string.Join(' ', behind.Select(Cells))}");
                if (behind[^1].Status == NavPathStatus.Failed)
                {
                    Assert.Equal("goal-blocked", behind[^1].Reason); // a ring point on blocked ground (a wall, a rock): the goal's, not the gate's
                    continue;
                }

                reached++;
                var line = behind.SelectMany(p => p.Waypoints).Select(w => Global(w.At.CellId, w.At.Origin)).ToList();
                var passes = line.Zip(line.Skip(1), (a, b) => DistanceToSegment(gate, a, b)).Min();
                output.WriteLine($"  passes the gate door at {passes:0.0} m");
                Assert.True(passes < GateCrossMetres, $"the walk passes {passes:0.0} m from the gate door"); // W6a: planned through the open gate: the walk crosses the gate
            }

            Assert.True(reached > 0, $"no walk to 0x{guid:X8} was planned");
        }

        // The rule: the vendors behind the gate are refused, and so is the Network portal behind it as the way back without the recall.
        var routes = Routes;
        var tethana = ScrivenerCatalog.Default.Scriveners.Where(s => s.Places.Any(p => door.Behind.Contains(p.Guid)))
            .SelectMany(s => s.Places.Where(p => door.Behind.Contains(p.Guid)).Select(p => new TownTripCandidate(p.Town, s.Name, s.Wcid, p))).ToList();
        Assert.NotEmpty(tethana);
        var withRecall = TownNetworkTrip.Choose(tethana, new TownTripInput("holtburg", 45, KnowsLifestoneRecall: true, HomeIsTravelStart: true), routes, LeverDoors.Default);
        Assert.Null(withRecall.Chosen);
        Assert.Equal("TODO(door-lever)", withRecall.Stop);
        Assert.All(withRecall.Verdicts, v => Assert.Equal("lever-door:" + door.Key, v.Refusal));
        var zisahaCandidate = ScrivenerCatalog.Default.ScarabSellers.SelectMany(s => s.Places.Select(p => new TownTripCandidate(p.Town, s.Name, s.Wcid, p))).Single(c => c.Vendor.Guid == ZisahaGuid);
        var noRecall = TownNetworkTrip.Choose([zisahaCandidate], new TownTripInput("holtburg", 45, KnowsLifestoneRecall: false, HomeIsTravelStart: true), routes, LeverDoors.Default);
        Assert.Equal("lever-door:" + door.Key, noRecall.Verdicts.Single().Refusal);
    }

    /// <summary>
    /// Dryreach (the learn's town for a level V: the first of the plan's towns by name the Network admits): from the drop 0xDA75002B the walks to the Master Scriveners of War, of Life and of Creature Magic in 0xDA75016D are whole and confirmed.
    /// The walk to the Master of Creature Magic was refused at its last push at his counter (the Holtburg case) until the room graph judged the counter, an object with physics BSP parts, by the body's own probe inside its disc
    /// (the world walk's W8, <c>CellRoutePlanner.InStatic</c>); his catalog stop 2 m before him at (159.94, 81.17) confirms too.
    /// </summary>
    [RequiresDatsFact]
    public void Dryreachs_drop_reaches_the_War_and_Life_Masters_and_names_the_Creature_Masters_stop()
    {
        var start = Drop("network-to-dryreach");
        foreach (var guid in new[] { WarMasterDryreach, LifeMasterDryreach })
        {
            var place = Scrivener(guid);
            Assert.Equal("Dryreach", place.Town);
            AssertWhole(start, place.Cell, place.Point, ShopLeg.UnseenVendorReachMetres, guid);
        }

        var creature = Scrivener(CreatureMasterDryreach);
        AssertWhole(start, creature.Cell, creature.Point, ShopLeg.UnseenVendorReachMetres, CreatureMasterDryreach);
        AssertWhole(start, creature.Cell, new Vector3(159.94f, 81.17f, creature.Point.Z), 0, CreatureMasterDryreach);
    }

    /// <summary>
    /// Stonehold (the learn's town for a level VI: Fort Tethana's Grand Masters are behind its lever gate, so the next Grand Master town on the Network): from the drop 0x64D5000B the walk to the Grand Master of War Magic in 0x64D50134 is
    /// whole and confirmed. The walks to the Grand Masters of Life and Creature Magic in 0x64D50136 were refused at the step from 0x64D50133 into their room, the pillar on the portal's centre; since M6 7 G the room graph crosses beside it and they confirm, as do the
    /// stops in 0x64D50133, 4.0 m and 2.0 m from them.
    /// </summary>
    [RequiresDatsFact]
    public void Stoneholds_drop_reaches_the_Grand_Masters_of_War_Life_and_Creature_and_the_stops_before_their_rooms()
    {
        var start = Drop("network-to-stonehold");
        var war = Scrivener(WarGrandMasterStonehold);
        Assert.Equal("Stonehold", war.Town);
        AssertWhole(start, war.Cell, war.Point, ShopLeg.UnseenVendorReachMetres, WarGrandMasterStonehold);
        foreach (var (guid, stop) in new[] { (LifeGrandMasterStonehold, new Vector3(107.5f, 61.0f, 78.005f)), (CreatureGrandMasterStonehold, new Vector3(109.25f, 59.0f, 78.005f)) })
        {
            var place = Scrivener(guid);
            AssertWhole(start, place.Cell, place.Point, ShopLeg.UnseenVendorReachMetres, guid);
            AssertWhole(start, 0x64D50133, stop, 0, guid);
        }
    }

    /// <summary>
    /// M6 14 (<c>m6-banes</c>): the Master Scrivener of Item Magic at Dryreach (the learn's town for a level V armour line: the first of the plan's towns by name the Network admits) and the Grand Master of Item Magic at Stonehold (level VI),
    /// the catalog's new places (landblock_instance 0x7DA75036 in 0xDA75016D, 0x764D503D in 0x64D50135): each walk from the town's Network drop to the scrivener's own point, as the learn trip walks it, is whole and confirmed
    /// (Dryreach by 0xDA750171 into his room; Stonehold by 0x64D50137 and 0x64D50133 into 0x64D50135), so neither needs a stop in the catalog. The eight other new places are not walked here: <c>TODO(m6-item-master-stops)</c>.
    /// </summary>
    [RequiresDatsFact]
    public void M6_14_the_drops_reach_the_Item_Master_at_Dryreach_and_the_Item_Grand_Master_at_Stonehold()
    {
        foreach (var (guid, town, drop) in new[] { (ItemMasterDryreach, "Dryreach", "network-to-dryreach"), (ItemGrandMasterStonehold, "Stonehold", "network-to-stonehold") })
        {
            var place = Scrivener(guid);
            Assert.Equal(town, place.Town);
            Assert.Null(place.StopCell);
            AssertWhole(Drop(drop), place.Cell, place.Point, ShopLeg.UnseenVendorReachMetres, guid);
        }
    }

    /// <summary>
    /// M6 4 R2: the catalog's stops for Dryreach's Master of Creature Magic and Stonehold's Grand Masters of Life and of Creature Magic (<c>TODO(m6-town-scrivener-stops)</c> closed for them, the Creature Master's at (109.25, 59.0) from M6 7 E after (105.0, 57.0)'s ring east point found a floor at 78.82; Fort Tethana's Grand Master of Creature Magic, <c>m6-tethana-grand-masters</c>'s stop) are the points the walks above confirm, and each stop and
    /// the eight points of its tolerance round it find the stop cell's floor through the statics at the stop's height (<c>VendorStopConfirmDatTests.AssertRingStands</c>'s check, the whole ring), from the drop (Fort Tethana: from the lifestone, in
    /// <c>GoalTileDatTests</c>).
    /// </summary>
    [RequiresDatsFact]
    public void The_town_scriveners_catalog_stops_are_the_confirmed_points_and_their_rings_stand_on_the_floor()
    {
        var rows = new (uint Guid, string Drop, uint Cell, Vector3 Stop)[]
        {
            (CreatureMasterDryreach, "network-to-dryreach", 0xDA75016D, new Vector3(159.94f, 81.17f, 18.005f)),
            (LifeGrandMasterStonehold, "network-to-stonehold", 0x64D50133, new Vector3(107.5f, 61.0f, 78.005f)),
            (CreatureGrandMasterStonehold, "network-to-stonehold", 0x64D50133, new Vector3(109.25f, 59.0f, 78.005f)),
        };
        foreach (var (guid, drop, cell, stop) in rows)
        {
            var place = Scrivener(guid);
            Assert.Equal(cell, place.StopCell);
            Assert.NotNull(place.StopPoint);
            Assert.True(Vector3.Distance(place.StopPoint!.Value, stop) < 0.05f, $"0x{guid:X8}: catalog stop {place.StopPoint}");
            var (rig, plans) = Walk(Drop(drop), place.StopCell!.Value, place.StopPoint.Value, 0);
            using (rig)
            {
                var goal = rig.Goal(place.StopCell!.Value, place.StopPoint.Value, 0);
                Assert.Equal(NavPathStatus.Found, plans[^1].Status);
                Assert.Equal(RouteConfirmStatus.Confirmed, rig.Confirm(plans[^1], goal));
                Assert.Equal(cell, plans[^1].Waypoints[^1].At.CellId);
                for (var k = -1; k < 8; k++)
                {
                    var a = k * MathF.PI / 4f;
                    var point = k < 0 ? stop : stop + new Vector3(goal.StopTolerance * MathF.Cos(a), goal.StopTolerance * MathF.Sin(a), 0f);
                    var found = rig.World.TryFixedGroundZ(new CellFrame(cell, new AcFrame(point, Quaternion.Identity)), rig.Foot, out var z, out _);
                    output.WriteLine($"0x{guid:X8} ring ({point.X:F2}, {point.Y:F2}): {(found ? $"floor at {z:F2}" : "no floor")}");
                    Assert.True(found, $"0x{guid:X8}: no walkable floor at ({point.X:F2}, {point.Y:F2}) of 0x{cell:X8}");
                    Assert.InRange(z, stop.Z - 0.2f, stop.Z + 0.1f);
                }
            }

            // The way out (M6 7 E): from the stop back to the drop, as the way in is walked: whole and confirmed.
            var dropFrame = Drop(drop);
            AssertWhole(new CellFrame(cell, new AcFrame(stop, Quaternion.Identity)), dropFrame.CellId, dropFrame.Frame.Origin, 0, guid);
        }
    }

    /// <summary>
    /// M6 7 E (the old pin inverted): Stonehold's Grand Master of Creature Magic has a catalog stop now, at (109.25, 59.0), and not at (105.0, 57.0): that candidate's walk confirms but its ring's east point, (105.5, 57.0), finds a floor at 78.82
    /// through the statics (the stop's 78.005 less than 0.2 m under or 0.1 over), a step or an object, so the body could stop on it at a height the walk did not plan.
    /// </summary>
    [RequiresDatsFact]
    public void Stoneholds_Creature_Grand_Master_has_a_catalog_stop_and_it_is_not_the_old_candidate_whose_ring_fails()
    {
        var place = Scrivener(CreatureGrandMasterStonehold);
        Assert.Equal(0x64D50133u, place.StopCell);
        Assert.NotNull(place.StopPoint);
        Assert.True(Vector3.Distance(place.StopPoint!.Value, new Vector3(109.25f, 59.0f, 78.005f)) < 0.05f);
        var (rig, _) = Walk(Drop("network-to-stonehold"), 0x64D50133, new Vector3(105.0f, 57.0f, 78.005f), 0);
        using (rig)
        {
            var found = rig.World.TryFixedGroundZ(new CellFrame(0x64D50133, new AcFrame(new Vector3(105.5f, 57.0f, 78.005f), Quaternion.Identity)), rig.Foot, out var z, out _);
            Assert.True(found);
            Assert.True(z > 78.105f, $"floor at {z:F2}");
        }
    }

    /// <summary>
    /// Neydisa: from the drop 0x95D60033 the walk to Archmage Philomon's room 0x95D6012B finds no path (<c>TODO(m6-neydisa-walk)</c>). It was the buy-trip's town when the book lacks Lifestone Recall (Zisaha's way back by portals is behind
    /// Fort Tethana's gate); since M6 item 14 (m6-goldscarab) that trip is Archmage Sammam al-Ashiz's at Uziz, the catalog's seller before Philomon and a travel place now (<c>BuyTripTests</c>; his walk: <c>TownNetworkWalkDatTests</c>, the town
    /// stock cases). The same gap is why the town stock's scarabs are not bought here, 40.5 m from the drop.
    /// </summary>
    [RequiresDatsFact]
    public void Neydisas_drop_finds_no_path_to_Archmage_Philomon_yet()
    {
        var philomon = ScrivenerCatalog.Default.ScarabSellers.SelectMany(s => s.Places).Single(p => p.Guid == PhilomonGuid);
        var (rig, plans) = Walk(Drop("network-to-neydisa"), philomon.Cell, philomon.Point, ShopLeg.UnseenVendorReachMetres);
        rig.Dispose();
        output.WriteLine($"Philomon: {plans[^1].Status} {plans[^1].Reason}");
        Assert.Equal(NavPathStatus.Failed, plans[^1].Status); // fixed: the gap goes and this line with it
    }

    private void AssertWhole(CellFrame start, uint cell, Vector3 point, double range, uint guid)
    {
        var (rig, plans) = Walk(start, cell, point, range);
        using (rig)
        {
            var last = plans[^1];
            var confirm = rig.Confirm(last, rig.Goal(cell, point, range));
            output.WriteLine($"0x{guid:X8} to 0x{cell:X8} ({point.X:0.00}, {point.Y:0.00}): {last.Status} {last.Reason}; cells {Cells(last)}; confirm {confirm} {rig.Confirmer.Failure}");
            Assert.Equal(NavPathStatus.Found, last.Status);
            Assert.Equal(cell, last.Waypoints[^1].At.CellId);
            Assert.Equal(RouteConfirmStatus.Confirmed, confirm);
        }
    }

    private static ScrivenerPlace Scrivener(uint guid) => ScrivenerCatalog.Default.Scriveners.SelectMany(s => s.Places).Single(p => p.Guid == guid);

    private static CellFrame Drop(string edge)
    {
        var e = Routes.Edge(edge);
        return new CellFrame(e.DestCell, new AcFrame(e.DestOrigin, Quaternion.Identity));
    }

    private static (uint Cell, Vector3 At) Ring(uint cell, Vector3 origin, float r, int a)
    {
        var t = a * MathF.PI / 4;
        var at = origin + new Vector3(MathF.Cos(t) * r, MathF.Sin(t) * r, 0);
        return (CellIds.LandCellAt(LandblockCoord.FromId(cell), at.X, at.Y) ?? cell, at);
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var t = ab.LengthSquared() < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
        return Vector2.Distance(p, a + (ab * t));
    }

    private static Vector2 Global(uint cell, Vector3 at) => new(((cell >> 24) * 192f) + at.X, (((cell >> 16) & 0xFF) * 192f) + at.Y);

    /// <summary>The station's walk from <paramref name="start"/>: a plan, and while it is a horizon (confirmed) the streamer's focus moved to its end and the plan again. Returns the rig (the caller disposes it) and every plan.</summary>
    private (StreamRig Rig, List<NavPath> Plans) Walk(CellFrame start, uint cell, Vector3 point, double range)
    {
        var rig = Rig(start);
        var goal = rig.Goal(cell, point, range);
        var source = rig.Planner.SourceFor(start.CellId, JourneyLegPlanner.GoalCellOf(goal), rig.Map);
        var plans = new List<NavPath> { source.Plan(start, goal, new NavScars()) };
        while (plans[^1].Status == NavPathStatus.Horizon && plans.Count <= MaxHops)
        {
            Assert.Equal(RouteConfirmStatus.Confirmed, rig.Confirm(plans[^1], goal));
            var at = plans[^1].Waypoints[^1].At;
            rig.Streamer.EnsureNow(at.CellId);
            plans.Add(source.Plan(at, goal, new NavScars()));
        }

        return (rig, plans);
    }

    private static string Cells(NavPath path) => string.Join(' ', path.CellsEntered.Select(c => c.ToString("X8", CultureInfo.InvariantCulture)));

    private StreamRig Rig(CellFrame start)
    {
        var world = new PhysicsWorld();
        var map = new NavMap();
        var streamer = new HeadlessStreamer(Dats, null, world, map, NullLogSink.Instance);
        streamer.EnsureNow(start.CellId);
        world.UseTime(0.0);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230));
        return new StreamRig(new JourneyLegPlanner(Dats), new J1JourneyRig(), map, streamer, new RouteConfirmer(world, walker.Physics.Setup!), world, walker.Physics.Setup!.Spheres[0]);
    }

    /// <summary>The journeys' leg planner over the headless streamer's tiles and collision, with the walker's confirmer on the human body (as <c>GoalTileDatTests</c>'s).</summary>
    private sealed class StreamRig(JourneyLegPlanner planner, J1JourneyRig view, NavMap map, HeadlessStreamer streamer, RouteConfirmer confirmer, PhysicsWorld world, PhysSphere foot) : IDisposable
    {
        public PhysicsWorld World { get; } = world;

        public PhysSphere Foot { get; } = foot;

        public JourneyLegPlanner Planner { get; } = planner;

        public NavMap Map { get; } = map;

        public HeadlessStreamer Streamer { get; } = streamer;

        public RouteConfirmer Confirmer { get; } = confirmer;

        /// <summary>A point request (the stations' vendor walk): the cell and point, with <paramref name="range"/> as its stop when positive.</summary>
        public NavGoal Goal(uint cell, Vector3 point, double range)
        {
            var request = new TravelRequest(new TeleportDestination("vendor", [], new WorldLocation("vendor", new CellPosition(cell, point), HasHeight: true, HeadingDegrees: 0), "walk test"), TravelPurpose.Vendor, Range: range);
            return Planner.GoalOf(view.View, request);
        }

        /// <summary>The runner's pre-check of a plan: a horizon to its end, a whole path up to the goal's stop tolerance.</summary>
        public RouteConfirmStatus Confirm(NavPath path, NavGoal goal) =>
            path.Status == NavPathStatus.Failed ? RouteConfirmStatus.Failed
            : Confirmer.ConfirmAll(path.Waypoints, path.Status == NavPathStatus.Horizon ? 0f : goal.StopTolerance);

        /// <inheritdoc/>
        public void Dispose()
        {
            Confirmer.Dispose();
            Streamer.Dispose();
        }
    }
}
