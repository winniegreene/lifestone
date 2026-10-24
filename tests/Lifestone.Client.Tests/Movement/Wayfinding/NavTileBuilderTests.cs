using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// The tile builder on synthetic landblocks (P5a lane S1): ground and slope, water, static stamping and its named skips,
/// buildings and doorway lanes, the clearance chamfer, determinism. The dats half is <see cref="NavTileDatTests"/>.
/// </summary>
public sealed class NavTileBuilderTests(ITestOutputHelper output)
{
    private const float Inflation = 0.48f + 0.1f;

    private static int N(int x, int y) => NavTile.Index(x, y);

    /// <summary>Distance from (cx, cy) to the axis-aligned rectangle, the reference the stamp is judged against.</summary>
    private static float RectDistance(float cx, float cy, float minX, float maxX, float minY, float maxY)
    {
        var dx = MathF.Max(MathF.Max(minX - cx, cx - maxX), 0f);
        var dy = MathF.Max(MathF.Max(minY - cy, cy - maxY), 0f);
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    [Fact]
    public void The_builders_step_up_is_the_planners_one_definition()
    {
        // P5a-R13: NavBuildOptions.StepUp takes its default from NavMap.StepUpMetres; a tile and the planner's edge rule agree.
        Assert.Equal(NavMap.StepUpMetres, new NavBuildOptions().StepUp);
    }

    [Fact]
    public void A_flat_tile_is_all_open_at_the_clearance_cap_and_z_zero()
    {
        var tile = NavTileBuilder.Build(S1TileKit.Flat(), null, out var stats);
        output.WriteLine($"flat: {stats}");
        Assert.Equal(0x40400000u, tile.LandblockId);
        Assert.All(tile.Flags, f => Assert.Equal(NavFlags.None, f));
        Assert.All(tile.Z, z => Assert.Equal(0f, z, 1e-4f));
        Assert.All(tile.Clearance, c => Assert.Equal(NavTile.MaxClearance, c));
        Assert.All(tile.SlopeCost, c => Assert.Equal(0, c));
        Assert.All(tile.Owner, o => Assert.Equal(NavTile.NoOwner, o));
        Assert.Empty(tile.Obstacles);
        Assert.Empty(tile.DoorwayLanes);
        Assert.True(tile.IsWalkable(0, 0) && tile.IsWalkable(191, 191));
        Assert.Equal(stats.Milliseconds, tile.BuildMilliseconds);
        Assert.True(tile.BuildMilliseconds > 0 && tile.BuildMilliseconds < 500, $"{tile.BuildMilliseconds} ms");
        Assert.StartsWith("navtile ms=", stats.ToString());
        Assert.Equal(0, stats.NodesWithoutTerrain);
    }

    [Fact]
    public void A_box_static_stamps_its_footprint_inflated_by_the_body_and_the_margin_and_names_its_owner()
    {
        var box = S1TileKit.Box(S1TileKit.Block, 100f, 100f, 0f, new Vector3(4f, 2f, 2f));
        var tile = NavTileBuilder.Build(S1TileKit.FlatWith(S1TileKit.Block, box), null, out var stats);
        output.WriteLine($"box: {stats}");
        Assert.Equal(1, stats.StaticsStamped);
        var obstacle = Assert.Single(tile.Obstacles);
        Assert.Equal(box.ModelId, obstacle.Id);
        Assert.Equal("static", obstacle.Kind);
        Assert.Equal(100f, obstacle.Origin.X, 1e-3f);

        var blocked = 0;
        for (var y = 0; y < NavTile.Size; y++)
        {
            for (var x = 0; x < NavTile.Size; x++)
            {
                var expected = RectDistance(x + 0.5f, y + 0.5f, 98f, 102f, 99f, 101f) <= Inflation;
                Assert.Equal(expected, !tile.IsWalkable(x, y));
                if (expected)
                {
                    blocked++;
                    Assert.Equal(NavFlags.Blocked | NavFlags.Static, tile.Flags[N(x, y)]);
                    Assert.Equal(0, tile.Owner[N(x, y)]);
                    Assert.Equal(0, tile.Clearance[N(x, y)]);
                }
                else
                {
                    Assert.Equal(NavTile.NoOwner, tile.Owner[N(x, y)]);
                }
            }
        }

        // x 97..102 (6 columns: the rounded ends reach 0.58 m past 98 and 102), y 98..101 (4 rows): 24 nodes less the 4 corners.
        Assert.Equal(20, blocked);
    }

    [Fact]
    public void A_box_turned_forty_five_degrees_stamps_the_oriented_rectangle_not_its_world_box()
    {
        var box = S1TileKit.Box(S1TileKit.Block, 100f, 100f, 0f, new Vector3(6f, 2f, 2f), yaw: MathF.PI / 4f);
        var tile = NavTileBuilder.Build(S1TileKit.FlatWith(S1TileKit.Block, box));

        // Along the box's long axis (northeast) a node 3.5 m out is blocked; across it (southeast) a node 3.5 m out is free,
        // though both lie inside the box's axis-aligned bounds.
        Assert.False(tile.IsWalkable(102, 102));
        Assert.True(tile.IsWalkable(102, 97));
        var cos = MathF.Cos(MathF.PI / 4f);
        var sin = MathF.Sin(MathF.PI / 4f);
        for (var y = 90; y < 110; y++)
        {
            for (var x = 90; x < 110; x++)
            {
                var ox = x + 0.5f - 100f;
                var oy = y + 0.5f - 100f;
                var local = new Vector2((ox * cos) + (oy * sin), (-ox * sin) + (oy * cos));
                var expected = RectDistance(local.X, local.Y, -3f, 3f, -1f, 1f) <= Inflation;
                Assert.Equal(expected, !tile.IsWalkable(x, y));
            }
        }
    }

    [Fact]
    public void Clearance_around_a_box_counts_the_nodes_to_the_nearest_blocked_one_and_caps()
    {
        var box = S1TileKit.Box(S1TileKit.Block, 100f, 100f, 0f, new Vector3(4f, 2f, 2f));
        var tile = NavTileBuilder.Build(S1TileKit.FlatWith(S1TileKit.Block, box));

        // The blocked rows run to y = 101 (centre 101.5, within 0.58 of the box's edge at 101); then 1, 2, 3 nodes of room.
        Assert.Equal(0, tile.Clearance[N(100, 101)]);
        Assert.Equal(1, tile.Clearance[N(100, 102)]);
        Assert.Equal(2, tile.Clearance[N(100, 103)]);
        Assert.Equal(3, tile.Clearance[N(100, 104)]);
        Assert.Equal(1, tile.Clearance[N(103, 100)]);
        Assert.Equal(NavTile.MaxClearance, tile.Clearance[N(100, 140)]);
        Assert.Equal(NavTile.MaxClearance, tile.Clearance[N(100, 111)]);

        // The chamfer is within 1.5 nodes of the true distance everywhere near the box (a 5-7 chamfer overestimates a knight-move distance by up to 8 percent, plus the rounding).
        for (var y = 80; y < 120; y++)
        {
            for (var x = 80; x < 120; x++)
            {
                var truth = float.MaxValue;
                for (var by = 90; by < 110; by++)
                {
                    for (var bx = 90; bx < 110; bx++)
                    {
                        if (!tile.IsWalkable(bx, by))
                        {
                            truth = MathF.Min(truth, MathF.Sqrt(((x - bx) * (x - bx)) + ((y - by) * (y - by))));
                        }
                    }
                }

                var expected = tile.IsWalkable(x, y) ? Math.Min(truth, NavTile.MaxClearance) : 0f;
                Assert.True(MathF.Abs(tile.Clearance[N(x, y)] - expected) <= 1.5f, $"({x},{y}) clearance {tile.Clearance[N(x, y)]} against {expected:F2}");
            }
        }
    }

    [Fact]
    public void The_chamfer_weighs_axis_steps_five_and_diagonals_seven_and_treats_the_border_as_open()
    {
        var tile = new NavTile(0x40400000);
        tile.Block(100, 100, NavFlags.Static);
        NavTileBuilder.ComputeClearance(tile);
        Assert.Equal(0, tile.Clearance[N(100, 100)]);
        Assert.Equal(1, tile.Clearance[N(101, 100)]);
        Assert.Equal(1, tile.Clearance[N(101, 101)]); // 7 / 5 rounds to 1
        Assert.Equal(2, tile.Clearance[N(100, 98)]);
        Assert.Equal(3, tile.Clearance[N(102, 102)]); // 14 / 5 = 2.8
        Assert.Equal(3, tile.Clearance[N(103, 101)]); // two axis steps and a diagonal: 17 / 5
        Assert.Equal(5, tile.Clearance[N(105, 100)]);
        Assert.Equal(NavTile.MaxClearance, tile.Clearance[N(110, 100)]);
        Assert.Equal(NavTile.MaxClearance, tile.Clearance[N(150, 150)]);

        // A blocked corner node: its neighbours down the border read their distance, and the far border is not "blocked".
        var corner = new NavTile(0x40400000);
        corner.Block(0, 0);
        NavTileBuilder.ComputeClearance(corner);
        Assert.Equal(1, corner.Clearance[N(0, 1)]);
        Assert.Equal(7, corner.Clearance[N(5, 5)]);
        Assert.Equal(NavTile.MaxClearance, corner.Clearance[N(191, 191)]);
        Assert.Equal(NavTile.MaxClearance, corner.Clearance[N(191, 0)]);

        // Re-runnable: unblocking nothing and running again changes nothing; blocking another node and running again updates.
        var before = corner.Clearance.ToArray();
        NavTileBuilder.ComputeClearance(corner);
        Assert.Equal(before, corner.Clearance);
        corner.Block(191, 191);
        NavTileBuilder.ComputeClearance(corner);
        Assert.Equal(0, corner.Clearance[N(191, 191)]);
        Assert.Equal(1, corner.Clearance[N(190, 190)]);
    }

    [Fact]
    public void A_corrugated_landblock_reads_its_ground_z_from_the_terrain_plane_and_stays_open()
    {
        var tile = NavTileBuilder.Build(MovementTestKit.Corrugated(S1TileKit.Block));
        Assert.All(tile.Flags, f => Assert.Equal(NavFlags.None, f));

        // Vertex heights alternate 0 and 1 m across 24 m cells: node (0, 0) is 0.5 m along the first rise of 1 m per 24 m.
        Assert.Equal(0.5f / 24f, tile.Z[N(0, 0)], 1e-3f);
        Assert.Equal(1f - (0.5f / 24f), tile.Z[N(23, 5)], 1e-3f);
        Assert.Equal(1f - (0.5f / 24f), tile.Z[N(24, 5)], 1e-3f); // the next cell falls back from 1 m
    }

    [Theory]
    [InlineData(12, false, false)] // normal z 0.894: open ground
    [InlineData(18, true, false)] // normal z 0.800: a slope
    [InlineData(24, true, false)] // normal z 0.707: a steep slope
    [InlineData(30, false, true)] // normal z 0.625: under FloorZ, blocked
    public void A_ramp_sets_slope_and_blocked_by_the_terrain_normal(int metresPerVertex, bool slope, bool blocked)
    {
        var tile = NavTileBuilder.Build(S1TileKit.Ramp(metresPerVertex));
        var normalZ = 1f / MathF.Sqrt(1f + ((metresPerVertex / 24f) * (metresPerVertex / 24f)));
        Assert.Equal(slope, normalZ >= PhysicsGlobals.FloorZ && normalZ < 0.85f);
        for (var x = 0; x < NavTile.Size; x += 7)
        {
            for (var y = 0; y < NavTile.Size; y += 11)
            {
                var i = N(x, y);
                Assert.Equal(metresPerVertex * (x + 0.5f) / 24f, tile.Z[i], 1e-2f);
                Assert.Equal(blocked, (tile.Flags[i] & NavFlags.Blocked) != 0);
                Assert.Equal(slope, (tile.Flags[i] & NavFlags.Slope) != 0);
                Assert.Equal(blocked ? 0 : (int)NavTile.MaxClearance, tile.Clearance[i]);
                if (slope)
                {
                    var expected = 255f * (0.85f - normalZ) / (0.85f - PhysicsGlobals.FloorZ);
                    Assert.True(MathF.Abs(tile.SlopeCost[i] - expected) <= 1f, $"{tile.SlopeCost[i]} against {expected:F1}");
                }
                else
                {
                    Assert.Equal(blocked ? 255 : 0, tile.SlopeCost[i]);
                }
            }
        }
    }

    [Fact]
    public void Water_flags_the_cells_with_a_water_corner_and_never_blocks()
    {
        Assert.True(LandblockCollision.IsWater(S1TileKit.WaterTerrainType << 2));
        Assert.True(LandblockCollision.IsWater(20 << 2));
        Assert.False(LandblockCollision.IsWater(15 << 2));
        Assert.False(LandblockCollision.IsWater(21 << 2));
        Assert.False(LandblockCollision.IsWater(1 << 2));

        // Vertices (2..3, 4..5) are water: cell (2, 4) is entirely water, the eight cells round it partly.
        var tile = NavTileBuilder.Build(S1TileKit.Water((x, y) => x is 2 or 3 && y is 4 or 5));
        for (var cx = 0; cx < 8; cx++)
        {
            for (var cy = 0; cy < 8; cy++)
            {
                var wet = cx is >= 1 and <= 3 && cy is >= 3 and <= 5;
                foreach (var (x, y) in new[] { (cx * 24, cy * 24), ((cx * 24) + 23, (cy * 24) + 23), ((cx * 24) + 11, (cy * 24) + 5) })
                {
                    Assert.Equal(wet, (tile.Flags[N(x, y)] & NavFlags.Water) != 0);
                    Assert.True(tile.IsWalkable(x, y), "water is costed, not blocked");
                }
            }
        }
    }

    [Fact]
    public void A_cylinder_sphere_and_a_sphere_stamp_circles_when_there_is_no_physics_bsp()
    {
        var tile = NavTileBuilder.Build(S1TileKit.Assemble([
            S1TileKit.Cylinder(S1TileKit.Block, 50f, 50f, 0f, 1f, 3f),
            S1TileKit.Ball(S1TileKit.Block, 150f, 40f, 0f, 0.8f)]));
        Assert.Equal(2, tile.Obstacles.Count);
        for (var y = 0; y < NavTile.Size; y++)
        {
            for (var x = 0; x < NavTile.Size; x++)
            {
                var cyl = MathF.Sqrt(MathF.Pow(x + 0.5f - 50f, 2) + MathF.Pow(y + 0.5f - 50f, 2)) <= 1f + Inflation;
                var ball = MathF.Sqrt(MathF.Pow(x + 0.5f - 150f, 2) + MathF.Pow(y + 0.5f - 40f, 2)) <= 0.8f + Inflation;
                Assert.Equal(cyl || ball, !tile.IsWalkable(x, y));
            }
        }
    }

    [Fact]
    public void Statics_that_cannot_block_are_skipped_and_counted_never_silently()
    {
        var tile = NavTileBuilder.Build(
            S1TileKit.Assemble([
                S1TileKit.Box(S1TileKit.Block, 40f, 40f, -5f, new Vector3(4f, 4f, 2f)),    // wholly below the ground
                S1TileKit.Box(S1TileKit.Block, 60f, 40f, 0f, new Vector3(4f, 4f, 0.03f)),  // a decal
                S1TileKit.Box(S1TileKit.Block, 80f, 40f, 0f, new Vector3(0.2f, 0.2f, 0.3f)), // a pebble
                S1TileKit.Box(S1TileKit.Block, 100f, 40f, 3f, new Vector3(4f, 4f, 1f)),    // an arch overhead
                S1TileKit.IndoorBox(120f, 40f, 0f, new Vector3(4f, 4f, 2f)),                // a room's furniture
                S1TileKit.Box(S1TileKit.Block, 140f, 40f, 0f, new Vector3(2f, 2f, 2f))]),  // the one that counts
            null,
            out var stats);
        output.WriteLine(stats.ToString());
        Assert.Equal(2, stats.StaticsBelowGround);
        Assert.Equal(1, stats.StaticsTiny);
        Assert.Equal(1, stats.StaticsNoNodes);
        Assert.Equal(1, stats.StaticsIndoor);
        Assert.Equal(1, stats.StaticsStamped);
        var obstacle = Assert.Single(tile.Obstacles);
        Assert.Equal(140f, obstacle.Origin.X);
        Assert.True(tile.IsWalkable(40, 40) && tile.IsWalkable(60, 40) && tile.IsWalkable(80, 40) && tile.IsWalkable(100, 40) && tile.IsWalkable(120, 40));
        Assert.False(tile.IsWalkable(140, 40));
    }

    [Fact]
    public void A_neighbours_static_reaching_over_the_border_is_stamped_as_a_shadow_only_when_the_neighbour_is_given()
    {
        var east = new LandblockCoord((byte)(S1TileKit.Block.X + 1), S1TileKit.Block.Y);
        var reaching = S1TileKit.Box(S1TileKit.Block, 191.5f, 100f, 0f, new Vector3(6f, 6f, 2f)); // x 188.5 to 194.5
        var neighbour = S1TileKit.FlatWith(S1TileKit.Block, reaching);
        Assert.NotEmpty(neighbour.ForeignShadows);
        var mine = S1TileKit.FlatWith(east);

        var alone = NavTileBuilder.Build(mine);
        Assert.All(alone.Flags, f => Assert.Equal(NavFlags.None, f));

        var tile = NavTileBuilder.Build(mine, new NavBuildOptions { Neighbours = [neighbour, mine] }, out var stats);
        output.WriteLine(stats.ToString());
        Assert.Equal(1, stats.ShadowsStamped); // one object, however many cells it reached
        Assert.Equal(0, stats.StaticsStamped);
        var owner = tile.Owner[N(0, 100)];
        Assert.NotEqual(NavTile.NoOwner, owner);
        Assert.Equal("shadow", tile.Obstacles[owner].Kind);
        Assert.Equal(-0.5f, tile.Obstacles[owner].Origin.X, 1e-3f);
        Assert.False(tile.IsWalkable(2, 100)); // x to 2.5 m, plus 0.58 m reaches the centre at 2.5 and not 3.5
        Assert.True(tile.IsWalkable(3, 100));
        Assert.True(tile.IsWalkable(0, 90));
    }

    [Fact]
    public void A_building_blocks_its_bounds_as_a_building_and_the_door_gets_a_lane_from_the_sill_out()
    {
        var (building, cell) = S1TileKit.HouseWithSouthDoor(1.2f);
        var tile = NavTileBuilder.Build(S1TileKit.Assemble([], [building], [cell]), null, out var stats);
        output.WriteLine(stats.ToString());
        Assert.Equal(1, stats.BuildingsStamped);
        Assert.Equal(1, stats.LanesBuilt);
        var walls = tile.Owner[N(94, 96)];
        Assert.Equal("building", tile.Obstacles[walls].Kind);
        Assert.Equal(building.ModelId, tile.Obstacles[walls].Id);
        Assert.Equal(NavFlags.Blocked | NavFlags.Building, tile.Flags[N(94, 96)]);
        Assert.False(tile.IsWalkable(94, 90)); // the 0.58 m round the wall, beside the lane
        Assert.True(tile.IsWalkable(94, 88));

        var lane = Assert.Single(tile.DoorwayLanes);
        Assert.Equal(0, lane.BuildingIndex);
        Assert.Equal(cell.Id, lane.EntryCellId);
        Assert.Equal(96f, lane.Sill.X, 1e-3f);
        Assert.Equal(91f, lane.Sill.Y, 1e-3f);
        Assert.Equal(0f, lane.Outward.X, 1e-3f);
        Assert.Equal(-1f, lane.Outward.Y, 1e-3f);

        // 1.5 m wide (two columns of nodes) and 2.5 m long (four rows): the sill end first, the outer end last.
        Assert.Equal(8, lane.Nodes.Count);
        Assert.Equal(91, lane.Nodes[0] / NavTile.Size);
        Assert.Equal(88, lane.Nodes[^1] / NavTile.Size);
        Assert.All(lane.Nodes, i =>
        {
            Assert.Equal(NavFlags.DoorwayLane, tile.Flags[i]);
            Assert.Equal(NavTile.NoOwner, tile.Owner[i]);
            Assert.True(tile.Clearance[i] >= 1);
        });
        Assert.Equal(new[] { 95, 96 }, lane.Nodes.Select(i => i % NavTile.Size).Distinct().Order().ToArray());
        Assert.True(tile.IsWalkable(96, 91) && tile.IsWalkable(95, 90) && tile.IsWalkable(96, 88));
        Assert.False(tile.IsWalkable(97, 91)); // the wall beside the lane stays
        Assert.False(tile.IsWalkable(96, 92)); // and the building behind the sill
    }

    [Fact]
    public void A_buildings_footprint_is_its_shell_polygons_not_its_bounding_box()
    {
        var tile = NavTileBuilder.Build(S1TileKit.Assemble([], [S1TileKit.LShapedHouse()]), null, out var stats);
        output.WriteLine(stats.ToString());
        Assert.Equal(1, stats.BuildingsStamped);
        Assert.Equal(0, stats.BuildingsByBounds);
        Assert.False(tile.IsWalkable(98, 93)); // under the south arm
        Assert.False(tile.IsWalkable(93, 98)); // under the west arm
        Assert.True(tile.IsWalkable(98, 98));  // inside the bounding box, in the open corner of the L
        Assert.True(tile.IsWalkable(99, 99));
        Assert.Equal(NavFlags.Blocked | NavFlags.Building, tile.Flags[N(93, 98)]);
        Assert.Equal("building", tile.Obstacles[tile.Owner[N(93, 98)]].Kind);

        // The same building without collision polygons falls back to its bounding box, and says so.
        var (box, cell) = S1TileKit.HouseWithSouthDoor(1.2f);
        _ = NavTileBuilder.Build(S1TileKit.Assemble([], [box], [cell]), null, out var boxStats);
        Assert.Equal(1, boxStats.BuildingsByBounds);
    }

    [Fact]
    public void A_building_portal_a_standing_human_cannot_pass_gets_no_lane_and_is_counted()
    {
        var (building, cell) = S1TileKit.HouseWithSouthDoor(0.7f); // narrower than the body's 0.96 m
        var tile = NavTileBuilder.Build(S1TileKit.Assemble([], [building], [cell]), null, out var stats);
        output.WriteLine(stats.ToString());
        Assert.Equal(0, stats.LanesBuilt);
        Assert.Equal(1, stats.PortalsImpassable);
        Assert.Empty(tile.DoorwayLanes);
        Assert.False(tile.IsWalkable(96, 91));
        Assert.DoesNotContain(tile.Flags, f => (f & NavFlags.DoorwayLane) != 0);
    }

    [Fact]
    public void A_building_portal_whose_entry_cell_is_missing_is_counted_not_dropped_silently()
    {
        var (building, _) = S1TileKit.HouseWithSouthDoor(1.2f);
        var tile = NavTileBuilder.Build(S1TileKit.Assemble([], [building], []), null, out var stats);
        Assert.Equal(1, stats.PortalsNoGeometry);
        Assert.Empty(tile.DoorwayLanes);
    }

    [Fact]
    public void A_landblock_without_terrain_is_blocked_throughout_and_counted()
    {
        var tile = NavTileBuilder.Build(new LandblockCollision(S1TileKit.Block), null, out var stats);
        Assert.Equal(NavTile.Size * NavTile.Size, stats.NodesWithoutTerrain);
        Assert.All(tile.Flags, f => Assert.Equal(NavFlags.Blocked, f));
        Assert.All(tile.Clearance, c => Assert.Equal(0, c));
    }

    [Fact]
    public void Builds_are_pure_and_the_same_from_every_thread()
    {
        var block = S1TileKit.Assemble(
            [S1TileKit.Box(S1TileKit.Block, 100f, 100f, 0f, new Vector3(4f, 2f, 2f), yaw: 0.6f), S1TileKit.Cylinder(S1TileKit.Block, 30f, 30f, 0f, 0.5f, 2f)],
            [S1TileKit.HouseWithSouthDoor(1.2f).Building],
            [S1TileKit.HouseWithSouthDoor(1.2f).Cell]);
        var reference = NavTileBuilder.Build(block);
        var tiles = new NavTile[8];
        Parallel.For(0, tiles.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i => tiles[i] = NavTileBuilder.Build(block));
        foreach (var tile in tiles)
        {
            Assert.Equal(reference.Flags, tile.Flags);
            Assert.Equal(reference.Z, tile.Z);
            Assert.Equal(reference.Clearance, tile.Clearance);
            Assert.Equal(reference.Owner, tile.Owner);
            Assert.Equal(reference.Obstacles, tile.Obstacles);
            Assert.Equal(reference.DoorwayLanes.Count, tile.DoorwayLanes.Count);
        }
    }
}
