using System.Diagnostics;
using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Movement.Wayfinding.S2TileKit;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// <see cref="PathPlanner"/> (P5a lane S2) on hand-built tiles: the octile cost on open ground, routes round boxes and
/// through gaps, no corner cutting, routes across landblock edges, the step-up and drop rules, the water and slope costs,
/// scars, the horizon goal, the named failures, the pulled route's clearance, the corridor's escalation and the pooled
/// replan. The 1 km timing is <c>Tier=Timing</c> (the lead runs it alone).
/// </summary>
public sealed class PathPlannerTests(ITestOutputHelper output)
{
    [Fact]
    public void A_straight_line_on_open_ground_costs_the_octile_distance()
    {
        var map = Map(Flat());
        var planner = new PathPlanner(map);
        var result = planner.Plan(P(10.5f, 20.5f), NavGoal.ToPoint(P(110.5f, 70.5f)));
        Assert.Equal(NavStatus.Found, result.Status);
        Assert.Null(result.Reason);
        Assert.Equal(NavWidening.Bounded, result.Widened);
        Assert.Equal(Octile(100, 50), result.Cost, 2);
        Assert.Equal(101, result.Route!.RawNodeCount);
        Assert.Equal(2, result.Route.Vertices.Count);
        Assert.Equal(new Vector2(100f, 50f).Length(), result.Route.ArcLength, 2);
        Assert.Equal(P(110.5f, 70.5f).Global, result.Route.End.Global);
        Assert.True(result.NodesExpanded > 0 && result.Milliseconds >= 0);
    }

    [Fact]
    public void A_route_goes_round_a_blocked_box()
    {
        var map = Map(Flat().Box(50, 10, 60, 90));
        var result = new PathPlanner(map).Plan(P(20.5f, 50.5f), NavGoal.ToPoint(P(90.5f, 50.5f)));
        Assert.Equal(NavStatus.Found, result.Status);
        Assert.True(result.Route!.ArcLength > 75f, $"arc {result.Route.ArcLength}");
        Assert.True(result.Route.Vertices.Count >= 3);
        Assert.All(SampleNodes(map, result.Route), n => Assert.True(n.Clearance >= 1, $"sample on {n}"));
    }

    [Fact]
    public void A_route_threads_the_gap_in_a_wall()
    {
        var map = Map(Flat().Box(95, 0, 96, 139).Box(95, 143, 96, 191));
        var result = new PathPlanner(map).Plan(P(20.5f, 30.5f), NavGoal.ToPoint(P(170.5f, 30.5f)));
        Assert.Equal(NavStatus.Found, result.Status);
        var crossing = Samples(result.Route!).Where(p => p.Local.X is >= 95f and < 97f).ToList();
        Assert.NotEmpty(crossing);
        Assert.All(crossing, p => Assert.InRange(p.Local.Y, 140f, 143f));
    }

    [Fact]
    public void A_diagonal_never_cuts_a_blocked_corner()
    {
        var map = Map(Flat().Box(10, 10, 10, 10).Box(11, 11, 11, 11));
        var result = new PathPlanner(map).Plan(P(10.5f, 11.5f), NavGoal.ToPoint(P(11.5f, 10.5f)));
        Assert.Equal(NavStatus.Found, result.Status);
        Assert.True(result.Cost > 4f, $"cost {result.Cost}: the direct diagonal cuts two blocked corners");
        Assert.True(result.Route!.ArcLength > 2f);
        Assert.All(SampleNodes(map, result.Route), n => Assert.True(n.Clearance >= 1));
    }

    [Fact]
    public void A_route_crosses_a_landblock_edge()
    {
        var map = Map(Flat(Block(0x10, 0x10)), Flat(Block(0x11, 0x10)));
        var goal = NavPoint.At(Block(0x11, 0x10), 90.5f, 50.5f);
        var result = new PathPlanner(map).Plan(P(100.5f, 50.5f, Block(0x10, 0x10)), NavGoal.ToPoint(goal));
        Assert.Equal(NavStatus.Found, result.Status);
        Assert.Equal(182f, result.Route!.ArcLength, 2);
        Assert.Equal(Block(0x11, 0x10), result.Route.End.LandblockId);
        Assert.Equal(new Vector3(90.5f, 50.5f, 0f), result.Route.End.Local);
    }

    [Fact]
    public void A_route_crosses_the_four_landblock_corner_at_the_byte_carry_without_cutting_it()
    {
        // 0x7F to 0x80 on both axes; two boxes close the corner's diagonal step (191,191)-(192,192).
        var map = Map(
            Flat(Block(0x7F, 0x7F)),
            Flat(Block(0x80, 0x7F)).Box(0, 180, 10, 191),
            Flat(Block(0x7F, 0x80)).Box(180, 0, 191, 10),
            Flat(Block(0x80, 0x80)));
        var from = P(150.5f, 150.5f, Block(0x7F, 0x7F));
        var to = P(40.5f, 40.5f, Block(0x80, 0x80));
        var result = new PathPlanner(map).Plan(from, NavGoal.ToPoint(to));
        Assert.Equal(NavStatus.Found, result.Status);
        Assert.Equal(Block(0x80, 0x80), result.Route!.End.LandblockId);
        Assert.True(result.Route.ArcLength > Vector2.Distance(from.Global, to.Global) + 1f);
        Assert.All(SampleNodes(map, result.Route), n => Assert.True(n.Clearance >= 1, $"sample on {n}"));
    }

    [Theory]
    [InlineData(0.7f, false)]
    [InlineData(0.5f, true)]
    public void A_rise_over_the_step_up_blocks(float rise, bool passes)
    {
        // The north of the tile is a plateau the whole width: the only way up is one step of the rise.
        var map = Map(Flat().Height(0, 100, 191, 191, rise));
        var result = new PathPlanner(map).Plan(P(50.5f, 50.5f), NavGoal.ToPoint(P(50.5f, 150.5f)));
        Assert.Equal(passes ? NavStatus.Found : NavStatus.NoPath, result.Status);
        if (!passes)
        {
            Assert.Equal("no-path", result.Reason);
        }
    }

    [Theory]
    [InlineData(2.5f, false)]
    [InlineData(1.5f, true)]
    public void A_drop_is_costed_up_to_two_metres_and_blocked_past_it(float drop, bool passes)
    {
        var map = Map(Flat().Height(0, 100, 191, 191, drop));
        var planner = new PathPlanner(map);
        var down = planner.Plan(P(50.5f, 150.5f), NavGoal.ToPoint(P(50.5f, 50.5f)));
        Assert.Equal(passes ? NavStatus.Found : NavStatus.NoPath, down.Status);
        if (passes)
        {
            Assert.Equal(100f + (PathPlanner.DropWeight * (drop - NavMap.StepUpMetres)), down.Cost, 2);
            Assert.Equal(NavStatus.NoPath, planner.Plan(P(50.5f, 50.5f), NavGoal.ToPoint(P(50.5f, 150.5f))).Status);
        }
    }

    [Theory]
    [InlineData(NavFlags.Water, 0)]
    [InlineData(NavFlags.Slope, 255)]
    public void Water_and_slope_costs_turn_the_route(NavFlags flags, int slopeCost)
    {
        var start = P(40.5f, 100.5f);
        var goal = NavGoal.ToPoint(P(140.5f, 100.5f));
        var dry = new PathPlanner(Map(Flat())).Plan(start, goal);
        Assert.Equal(100f, dry.Route!.ArcLength, 2);

        var map = Map(Flat().Mark(80, 0, 100, 119, flags, (byte)slopeCost));
        var wet = new PathPlanner(map).Plan(start, goal);
        Assert.Equal(NavStatus.Found, wet.Status);
        Assert.True(wet.Route!.ArcLength > 105f, $"arc {wet.Route.ArcLength}");
        Assert.True(wet.Route.Vertices.Max(v => v.Local.Y) >= 119f);
        Assert.All(SampleNodes(map, wet.Route), n => Assert.Equal(NavFlags.None, n.Flags & flags)); // the pull keeps off it too
    }

    [Theory]
    [InlineData(255, true)]
    [InlineData(254, false)]
    public void A_strip_at_the_top_of_the_slope_ramp_is_worth_a_short_detour(int slopeCost, bool detours)
    {
        // One node wide, open to the north past y = 119: crossing at the top of the ramp costs SteepEdgeWeight more than the
        // detour's roughly 8 m; a node one step lower on the ramp is crossed straight.
        var map = Map(Flat().Mark(90, 0, 90, 119, NavFlags.Slope, (byte)slopeCost));
        var plan = new PathPlanner(map).Plan(P(40.5f, 100.5f), NavGoal.ToPoint(P(140.5f, 100.5f)));
        Assert.Equal(NavStatus.Found, plan.Status);
        Assert.Equal(detours, plan.Route!.Vertices.Max(v => v.Local.Y) >= 119f);
    }

    [Fact]
    public void A_steep_edge_strip_turns_the_route_and_the_pull_keeps_off_it()
    {
        var map = Map(Flat().Mark(90, 0, 90, 119, NavFlags.SteepEdge));
        var plan = new PathPlanner(map).Plan(P(40.5f, 100.5f), NavGoal.ToPoint(P(140.5f, 100.5f)));
        Assert.Equal(NavStatus.Found, plan.Status);
        Assert.True(plan.Route!.Vertices.Max(v => v.Local.Y) >= 119f);
        Assert.All(SampleNodes(map, plan.Route), n => Assert.Equal(NavFlags.None, n.Flags & NavFlags.SteepEdge));
    }

    [Fact]
    public void Scars_force_a_detour_and_expire()
    {
        var clock = new ManualTimeProvider();
        var scars = new NavScars(clock);
        var map = Map(Flat());
        var planner = new PathPlanner(map);
        var start = P(20.5f, 100.5f);
        var goal = NavGoal.ToPoint(P(170.5f, 100.5f));
        var centre = P(95.5f, 100.5f);
        scars.Add(centre, 5f);
        Assert.True(scars.IsScarred(P(98f, 101f)));
        Assert.False(scars.IsScarred(P(101f, 101f)));

        var around = planner.Plan(start, goal, scars);
        Assert.Equal(NavStatus.Found, around.Status);
        Assert.True(around.Route!.Vertices.Count >= 3);
        Assert.All(Samples(around.Route), p => Assert.True(p.HorizontalDistance(centre) > 5f - 0.75f, $"{p} in the scar"));
        Assert.Single(map.AttachedLandblocks); // the tile is untouched: scars are the run's own overlay
        Assert.Equal(NavFlags.None, map.TryGetTile(Lb, out var tile) ? tile.Flags[NavTile.Index(95, 100)] : NavFlags.Blocked);

        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(0, scars.LiveCount);
        var straight = planner.Plan(start, goal, scars);
        Assert.Equal(2, straight.Route!.Vertices.Count);
        Assert.Equal(1, scars.Expire());
        Assert.Empty(scars.All);
    }

    [Fact]
    public void A_run_may_leave_the_scar_it_stands_in_but_never_enter_one()
    {
        var scars = new NavScars(new ManualTimeProvider());
        var start = P(50.5f, 50.5f);
        scars.Add(start, 3f, TimeSpan.FromSeconds(5));
        var planner = new PathPlanner(Map(Flat()));
        var result = planner.Plan(start, NavGoal.ToPoint(P(120.5f, 50.5f)), scars);
        Assert.Equal(NavStatus.Found, result.Status);

        var into = planner.Plan(P(120.5f, 50.5f), NavGoal.ToPoint(start), scars);
        Assert.Equal(NavStatus.GoalBlocked, into.Status);
        Assert.Equal("goal-scarred", into.Reason);
    }

    /// <summary>
    /// M6 14 (m6-14-yinar; live ladder61, 2026-11-17 14:14:16 UTC, the Yinar Portal's front): a point goal is judged against the scars by its own point, not by its node's centre. The front 3.00 m south of the portal stands outside the own portal's
    /// disc (2.84 m), in the node whose centre the disc covers (2.63 m from the portal): the plan reaches the goal and no sample of its line enters the disc. A goal inside the disc is still <c>goal-scarred</c>.
    /// </summary>
    /// <summary>
    /// M6 14 (m6-14-plaza2; live ladder63, 2026-11-17 18:06:38 UTC: a plan's leg passed within the Maythen Geroyu Villas Portal's clearance, and the runner's exact check refused it, <c>portal-keep-off</c>): the planner and the runner agree.
    /// Over a field of portal discs (centres off the node grid, the clearance and a shared lane's radius), every leg of every plan keeps each disc's exact radius, as <see cref="PortalKeepOff.FirstMeeting(IReadOnlyList{NavWaypoint})"/> judges it.
    /// </summary>
    [Fact]
    public void Every_leg_of_a_plan_keeps_each_kept_discs_exact_radius()
    {
        var rng = new Random(61);
        var discs = new List<(Vector2 Centre, float Radius)>();
        for (int i = 0; i < 40; i++)
        {
            var c = P(20f + (float)(rng.NextDouble() * 150.0), 20f + (float)(rng.NextDouble() * 150.0)).Global;
            discs.Add((c, i % 3 == 0 ? PortalKeepOff.LaneFloorMetres : PortalKeepOff.ClearanceMetres));
        }

        var scars = new NavScars(new ManualTimeProvider());
        scars.SetKeepOff(discs);
        var planner = new PathPlanner(Map(Flat()));
        var checkedPlans = 0;
        for (int i = 0; i < 120; i++)
        {
            var a = P(5f + (float)(rng.NextDouble() * 180.0), 5f + (float)(rng.NextDouble() * 180.0));
            var b = P(5f + (float)(rng.NextDouble() * 180.0), 5f + (float)(rng.NextDouble() * 180.0));
            if (discs.Any(d => Vector2.Distance(d.Centre, a.Global) <= d.Radius + 0.5f || Vector2.Distance(d.Centre, b.Global) <= d.Radius + 0.5f))
            {
                continue;
            }

            var result = planner.Plan(a, NavGoal.ToPoint(b), scars);
            if (result.Status != NavStatus.Found)
            {
                continue;
            }

            checkedPlans++;
            var v = result.Route!.Vertices;
            for (int k = 1; k < v.Count; k++)
            {
                foreach (var (centre, radius) in discs)
                {
                    var ab = v[k].Global - v[k - 1].Global;
                    var t = ab.LengthSquared() <= 1e-8f ? 0f : Math.Clamp(Vector2.Dot(centre - v[k - 1].Global, ab) / ab.LengthSquared(), 0f, 1f);
                    var metres = Vector2.Distance(centre, v[k - 1].Global + (ab * t));
                    Assert.True(metres >= radius, $"plan {i}: leg {k} passes {metres:0.000} m from a disc of {radius:0.000} m");
                }
            }
        }

        Assert.True(checkedPlans >= 60, $"only {checkedPlans} plans were checked");
    }

    [Fact]
    public void A_point_goal_beside_a_disc_is_judged_by_its_own_point_not_by_its_nodes_centre()
    {
        var portal = P(20.6096f, 53.1273f);
        var reach = 2.841f;
        var scars = new NavScars(new ManualTimeProvider());
        scars.SetKeepOff([(portal.Global, reach)]);
        var front = P(20.6377f, 50.1274f);
        Assert.True(front.HorizontalDistance(portal) > reach);
        Assert.True(P(20.5f, 50.5f).HorizontalDistance(portal) < reach); // the goal's node's centre is inside the disc

        var planner = new PathPlanner(Map(Flat()));
        var result = planner.Plan(P(30.5f, 10.5f), NavGoal.ToPoint(front), scars);
        Assert.Equal(NavStatus.Found, result.Status);
        Assert.Equal(front.Global, result.Route!.End.Global);
        Assert.All(Samples(result.Route, 0.05f), p => Assert.True(p.HorizontalDistance(portal) > reach - 1e-3f, $"{p} in the disc"));

        var inside = planner.Plan(P(30.5f, 10.5f), NavGoal.ToPoint(P(20.6f, 50.6f)), scars);
        Assert.Equal(NavStatus.GoalBlocked, inside.Status);
        Assert.Equal("goal-scarred", inside.Reason);
    }

    [Fact]
    public void A_far_goal_plans_to_a_horizon_goal_on_the_attached_edge_then_on_once_the_tiles_attach()
    {
        var map = Map(Flat());
        var planner = new PathPlanner(map);
        var start = P(50.5f, 100.5f);
        var far = NavGoal.ToPoint(NavPoint.At(0xABB40000u, 100f, 60f));
        var result = planner.Plan(start, far);
        Assert.Equal(NavStatus.Horizon, result.Status);
        var horizon = result.HorizonPoint!.Value;
        Assert.Equal(Lb, horizon.LandblockId);
        Assert.InRange(horizon.Local.X, 191f, 192f);
        Assert.InRange(horizon.Local.Y, 87.3f - 1.5f, 87.3f + 1.5f); // where the straight line leaves the tile
        Assert.Equal(horizon, result.Route!.End);

        int version = map.Version;
        map.Attach(Flat(0xAAB40000u).Clear());
        map.Attach(Flat(0xABB40000u).Clear());
        Assert.NotEqual(version, map.Version);
        var on = planner.Plan(result.Route.PointAt(result.Route.ArcLength - PathPlanner.HorizonReplanMetres), far);
        Assert.Equal(NavStatus.Found, on.Status);
        Assert.Equal(0xABB40000u, on.Route!.End.LandblockId);
    }

    [Fact]
    public void A_horizon_goal_is_an_open_edge_node_the_start_reaches()
    {
        // The east edge is walled from y 60 to 120 and the wall's west face is closed by a pen round the start.
        var map = Map(Flat().Box(150, 60, 191, 61).Box(150, 119, 191, 120).Box(150, 60, 151, 120));
        var result = new PathPlanner(map).Plan(P(170.5f, 90.5f), NavGoal.ToPoint(NavPoint.At(0xAAB40000u, 50f, 90f)));
        Assert.Equal(NavStatus.Horizon, result.Status);
        var h = result.HorizonPoint!.Value;
        Assert.InRange(h.Local.X, 191f, 192f);
        Assert.InRange(h.Local.Y, 62f, 119f);

        // A start outside the pen cannot reach those edge nodes: its horizon lies above or below the pen.
        var outside = new PathPlanner(map).Plan(P(100.5f, 90.5f), NavGoal.ToPoint(NavPoint.At(0xAAB40000u, 50f, 90f)));
        Assert.Equal(NavStatus.Horizon, outside.Status);
        Assert.True(outside.HorizonPoint!.Value.Local.Y is < 60f or > 120f, $"{outside.HorizonPoint}");
    }

    [Fact]
    public void Failures_are_named()
    {
        var map = Map(Flat().Box(50, 50, 69, 69).Box(100, 100, 120, 100).Box(100, 120, 120, 120).Box(100, 100, 100, 120).Box(120, 100, 120, 120));
        var planner = new PathPlanner(map);
        var open = P(20.5f, 20.5f);

        var a = planner.Plan(P(60.5f, 60.5f), NavGoal.ToPoint(open));
        Assert.Equal((NavStatus.StartBlocked, "start-blocked"), (a.Status, a.Reason));
        var edge = planner.Plan(P(50.2f, 60.5f), NavGoal.ToPoint(open)); // on the box's edge node: starts from the node beside it
        Assert.Equal((NavStatus.Found, true), (edge.Status, edge.StartSnapped));
        Assert.Equal(new Vector2(49.5f, 60.5f), edge.Route!.Start.Global - NavPoint.BlockOrigin(Lb));
        var b = planner.Plan(P(10f, 10f, 0xA9B50000u), NavGoal.ToPoint(open));
        Assert.Equal((NavStatus.StartBlocked, "start-not-attached"), (b.Status, b.Reason));
        var c = planner.Plan(open, NavGoal.ToPoint(P(60.5f, 60.5f)));
        Assert.Equal((NavStatus.GoalBlocked, "goal-blocked"), (c.Status, c.Reason));
        var d = planner.Plan(open, NavGoal.ToObject(0x80000001u, P(60f, 60f), 2f, needSight: true));
        Assert.Equal((NavStatus.GoalBlocked, "no-open-node-in-range"), (d.Status, d.Reason));
        var e = planner.Plan(open, NavGoal.ToPoint(P(110.5f, 110.5f)));
        Assert.Equal((NavStatus.NoPath, "no-path", NavWidening.Unbounded), (e.Status, e.Reason, e.Widened));
        Assert.Null(e.Route);

        var reach = planner.Plan(open, NavGoal.ToObject(0x80000001u, P(60f, 60f), 15f, needSight: true));
        Assert.Equal(NavStatus.Found, reach.Status);
        Assert.InRange(reach.Route!.End.HorizontalDistance(P(60f, 60f)), 0f, 15f);
        Assert.True(reach.Route.ArcLength < open.HorizontalDistance(P(60f, 60f)) - 10f);
    }

    [Fact]
    public void Pulled_routes_keep_clearance_at_every_half_metre_sample()
    {
        var rng = new Random(7);
        var tile = Flat();
        for (int k = 0; k < 60; k++)
        {
            int x = rng.Next(5, 180);
            int y = rng.Next(5, 180);
            tile.Box(x, y, x + rng.Next(1, 9), y + rng.Next(1, 9));
        }

        var map = Map(tile);
        var planner = new PathPlanner(map);
        int found = 0;
        for (int k = 0; k < 30; k++)
        {
            var from = P(rng.Next(0, 192) + 0.5f, rng.Next(0, 192) + 0.5f);
            var to = P(rng.Next(0, 192) + 0.5f, rng.Next(0, 192) + 0.5f);
            var result = planner.Plan(from, NavGoal.ToPoint(to));
            if (result.Status != NavStatus.Found)
            {
                continue;
            }

            found++;
            Assert.All(SampleNodes(map, result.Route!), n => Assert.True(n.Clearance >= PathPlanner.PullMinClearance, $"sample on {n}"));
            for (int v = 0; v + 1 < result.Route!.Vertices.Count; v++)
            {
                Assert.True(map.LineOfSight(result.Route.Vertices[v], result.Route.Vertices[v + 1]));
            }
        }

        Assert.True(found >= 15, $"only {found} of 30 random plans found a route");
    }

    [Fact]
    public void The_search_widens_from_the_corridor_then_goes_unbounded_and_says_so()
    {
        var map = Map(Flat());
        var planner = new PathPlanner(map);
        var start = P(12.5f, 100.5f);
        var goal = NavGoal.ToPoint(P(180.5f, 100.5f));
        Assert.Equal(NavWidening.Bounded, planner.Plan(start, goal).Widened);

        // A wall of scars across the corridor plus one land cell (y 72-143): the widened band (y 24-191) goes round.
        var scars = new NavScars(new ManualTimeProvider());
        for (int y = 60; y <= 156; y += 4)
        {
            scars.Add(NavPoint.At(Lb, 96f, y), 3f);
        }

        var widened = planner.Plan(start, goal, scars);
        Assert.Equal((NavStatus.Found, NavWidening.Widened), (widened.Status, widened.Widened));
        Assert.True(widened.CorridorRegions > 0);

        // Closing the widened band too leaves only the unbounded search, round the south of the tile.
        for (int y = 20; y <= 192; y += 4)
        {
            scars.Add(NavPoint.At(Lb, 96f, y), 3f);
        }

        var unbounded = planner.Plan(start, goal, scars);
        Assert.Equal((NavStatus.Found, NavWidening.Unbounded), (unbounded.Status, unbounded.Widened));
        Assert.True(unbounded.Route!.Vertices.Min(v => v.Local.Y) < 17f);
    }

    [Fact]
    [Trait("Tier", "Timing")] // m5-pathplanner-km-timing-flake: a warm-plan byte bound; the one-kilometre timing theory below was already Timing since M4
    public void A_pooled_replan_allocates_only_its_result()
    {
        var tile = Flat();
        var rng = new Random(3);
        for (int k = 0; k < 40; k++)
        {
            int x = rng.Next(20, 170);
            int y = rng.Next(5, 180);
            tile.Box(x, y, x + 3, y + 3);
        }

        var map = Map(tile, Flat(0xAAB40000u));
        var planner = new PathPlanner(map);
        var start = P(5.5f, 90.5f);
        var goal = NavGoal.ToPoint(NavPoint.At(0xAAB40000u, 150.5f, 100.5f));
        long cold = GC.GetAllocatedBytesForCurrentThread();
        var first = planner.Plan(start, goal);
        cold = GC.GetAllocatedBytesForCurrentThread() - cold;
        Assert.Equal(NavStatus.Found, first.Status);
        planner.Plan(start, goal);

        const int plans = 20;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < plans; k++)
        {
            planner.Plan(start, goal);
        }

        long perPlan = (GC.GetAllocatedBytesForCurrentThread() - before) / plans;
        output.WriteLine($"cold plan {cold} B (the pools), warm plan {perPlan} B, {first.Route!.Vertices.Count} vertices");
        Assert.True(cold > 1_000_000, $"cold {cold} B");
        Assert.True(perPlan < 4096, $"a warm replan allocated {perPlan} B");
    }

    [Theory]
    [Trait("Tier", "Timing")]
    [InlineData(120)] // a dense wood: about 4% of the ground blocked and 40% under the clearance cost
    [InlineData(20)] // open country with a few trees
    public void A_one_kilometre_plan_takes_low_single_digit_milliseconds(int boxesPerTile)
    {
        var rng = new Random(11);
        var tiles = new NavTile[6];
        for (int b = 0; b < tiles.Length; b++)
        {
            tiles[b] = Flat(Block(0x10 + b, 0x10));
            for (int k = 0; k < boxesPerTile; k++)
            {
                int x = rng.Next(0, 186);
                int y = rng.Next(0, 186);
                tiles[b].Box(x, y, x + rng.Next(1, 6), y + rng.Next(1, 6));
            }
        }

        var map = Map(tiles);
        var planner = new PathPlanner(map);
        var start = P(10.5f, 96.5f, Block(0x10, 0x10));
        var goal = NavGoal.ToPoint(P(90.5f, 96.5f, Block(0x15, 0x10)));
        Assert.True(map.TryGetNode(start, out var s) && s.Clearance > 0 && map.TryGetNode(goal.Point, out var g) && g.Clearance > 0);
        var times = new List<double>();
        NavPlanResult? last = null;
        for (int k = 0; k < 12; k++)
        {
            var sw = Stopwatch.StartNew();
            last = planner.Plan(start, goal);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }

        var warm = times.Skip(2).Order().ToList();
        double median = warm[warm.Count / 2];
#if DEBUG
        const string config = "Debug";
#else
        const string config = "Release";
#endif
        output.WriteLine(
            $"{config}, {boxesPerTile} boxes a tile: 1 km plan median {median:0.00} ms (min {warm[0]:0.00}, cold {times[0]:0.00}); " +
            $"status {last!.Status} {last.Widened}, arc {last.Route?.ArcLength:0} m, expanded {last.NodesExpanded}, touched {last.NodesTouched}, " +
            $"vertices {last.Route?.Vertices.Count}, pull {planner.LastPullMilliseconds:0.00} ms, max heap {planner.HeapHighWater}");
        Assert.Equal(NavStatus.Found, last.Status);
        Assert.True(median < 50, $"median {median:0.00} ms");
    }

    [Fact]
    public void A_route_answers_arc_length_queries()
    {
        var route = new NavRoute([new Vector2(1000f, 1000f), new Vector2(1010f, 1000f), new Vector2(1010f, 1010f)], [0f, 2f, 4f], 21);
        Assert.Equal(20f, route.ArcLength);
        Assert.Equal(21, route.RawNodeCount);
        Assert.Equal(new Vector2(1010f, 1005f), route.PointAt(15f).Global);
        Assert.Equal(3f, route.PointAt(15f).Z, 4);
        Assert.Equal(route.Start, route.PointAt(-1f));
        Assert.Equal(route.End, route.PointAt(99f));
        var off = NavPoint.FromGlobal(new Vector2(1012f, 1005f), 0f);
        Assert.Equal(15f, route.NearestArcLength(off), 4);
        Assert.Equal(2f, route.DistanceFrom(off), 4);
        Assert.Equal(10f, route.ArcLengthAt(1));
    }

    /// <summary>
    /// D134: the runner's forward-only progress. A U route (east 10 m, north 2 m, west 10 m) whose legs pass 2 m apart: a body at the second leg's end, swung
    /// wide towards the first leg, is nearest the first leg anywhere, but within the window ahead of its progress it stays on the third; and a route that passes
    /// over its own start a storey up (the first leg at z 0, the last at z 4) answers by the body's storey.
    /// </summary>
    [Fact]
    public void A_routes_window_keeps_progress_forward_and_on_the_bodys_storey()
    {
        var u = new NavRoute([new Vector2(1000f, 1000f), new Vector2(1010f, 1000f), new Vector2(1010f, 1002f), new Vector2(1000f, 1002f)], [0f, 0f, 0f, 0f], 4);
        var swung = NavPoint.FromGlobal(new Vector2(1004f, 1000.8f), 0f);
        Assert.Equal(4f, u.NearestArcLength(swung), 3); // anywhere: back on the first leg
        var (arc, distance) = u.NearestInWindow(swung, 13f, 0f, 8f, 1.5f);
        Assert.Equal(18f, arc, 3); // within the window: the third leg, 1.2 m off
        Assert.Equal(1.2f, distance, 3);

        var over = new NavRoute([new Vector2(1000f, 1000f), new Vector2(1010f, 1000f), new Vector2(1010f, 1000f), new Vector2(1000f, 1000.5f)], [0f, 0f, 4f, 4f], 4);
        var upstairs = NavPoint.FromGlobal(new Vector2(1005f, 1000.1f), 4f);
        Assert.Equal(5f, over.NearestArcLength(upstairs), 3); // flat: the leg below
        Assert.InRange(over.NearestInWindow(upstairs, 0f, 0f, over.ArcLength, 1.5f).Arc, 14.5f, 15.5f); // by storey: the leg it stands on
    }
}
