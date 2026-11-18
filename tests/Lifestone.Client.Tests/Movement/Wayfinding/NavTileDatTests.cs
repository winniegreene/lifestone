using System.Diagnostics;
using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World.Data;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// The tile builder on Holtburg's real landblock 0xA9B4 (P5a lane S1, Exhaustive tier: it reads the dats and skips without
/// them, the pattern of <c>OfflineRouteTests</c>): the build time, the lifestone's node, and a doorway lane for Renald's
/// shop door and Thelnoth's cellar that the flood fill from the lifestone reaches.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class NavTileDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>A room of Renald's shop (the vendor approach P5a closes); the lane that leads to it is a door of its building.</summary>
    private const uint RenaldShopCell = 0xA9B40141;

    /// <summary>A room of Thelnoth's cellar building.</summary>
    private const uint ThelnothCellarCell = 0xA9B40111;

    private DatSet Dats => fixture.Dats;

    private LandblockCollision HoltburgBlock() => new LandblockCollisionBuilder(Dats).Build(LandblockCoord.Holtburg);

    /// <summary>
    /// The world walk (W8): a hut 0x0100110A (0x1747, its door 0x1747010F facing north at y 135.4) covers 6 m of porch before its door, its floor 0.4 m over the land. The straight 2.5 m strip ended
    /// among the shell's blocked nodes, so no plan reached the door, though a body walks straight out. The strip goes on over the shell to the land; with that switched off it does not.
    /// </summary>
    [RequiresDatsFact]
    public void A_huts_door_lane_goes_on_over_its_porch_to_the_land()
    {
        var block = new LandblockCollisionBuilder(Dats).Build(new LandblockCoord(0x17, 0x47));
        Assert.True(ReachesLand(NavTileBuilder.Build(block, null, out var stats)));
        Assert.True(stats.LanesOverShell > 0);
        Assert.False(ReachesLand(NavTileBuilder.Build(block, new NavBuildOptions { LaneOverShellMetres = 0f }, out _)));

        static bool ReachesLand(NavTile tile)
        {
            var lane = Assert.Single(tile.DoorwayLanes, l => l.EntryCellId == 0x1747010F);
            var nodes = lane.Nodes.ToHashSet();
            return nodes.Any(i =>
            {
                var (x, y) = (i % NavTile.Size, i / NavTile.Size);
                return new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) }.Any(n => NavTile.InBounds(n.Item1, n.Item2) && !nodes.Contains(NavTile.Index(n.Item1, n.Item2)) && tile.IsWalkable(n.Item1, n.Item2));
            });
        }
    }

    /// <summary>
    /// M6 7 A (D62, the Brighteyes walk): the Refuge's six hut doors (sills 63.60 over the ground at 58.00, on a covered walk whose stairs, five statics 0x02000300, leave at right angles) each
    /// get a lane down those stairs, from a walkable outer end on the ground to the sill's height, no two points over a metre apart and the middle of the flight kept (0.27 m either side of y 36: the 1.5 m flight less a body radius); Holtburg's two
    /// landblocks gain none, so their balconies (0xA9B30106, 0xA9B30110) and five raised doors stay refused.
    /// </summary>
    [RequiresDatsFact]
    public void The_Refuges_six_raised_doors_walk_their_turned_stairs_and_Holtburgs_balconies_stay_refused()
    {
        var builder = new LandblockCollisionBuilder(Dats);
        var refuge = NavTileBuilder.Build(builder.Build(new LandblockCoord(0xF6, 0x82)), null, out var stats);
        output.WriteLine(stats.ToString());
        Assert.Equal(6, stats.LanesBuilt);
        Assert.Equal(6, stats.LanesByStairs);
        Assert.Equal(0, stats.PortalsAboveGround);
        foreach (var lane in refuge.DoorwayLanes)
        {
            var climb = lane.Climb!;
            output.WriteLine($"0x{lane.EntryCellId:X8}: {string.Join(" ", climb)}");
            var (x, y) = NavTile.NodeAt(new Vector2(climb[0].X, climb[0].Y));
            Assert.True(refuge.IsWalkable(x, y), $"0x{lane.EntryCellId:X8}: the outer end {climb[0]}");
            Assert.InRange(climb[0].Z, 57.9f, 58.1f);
            Assert.InRange(climb[^1].Z, 63.5f, 63.7f);
            Assert.Equal(63.6f, lane.Sill.Z, 0.01f);
            for (var i = 1; i < climb.Count; i++)
            {
                Assert.True(Vector2.Distance(new Vector2(climb[i].X, climb[i].Y), new Vector2(climb[i - 1].X, climb[i - 1].Y)) <= 1.01f, $"0x{lane.EntryCellId:X8}: {climb[i - 1]} to {climb[i]}");
            }
        }

        var brighteyes = refuge.DoorwayLanes.Single(l => l.EntryCellId == 0xF682011B);
        Assert.All(brighteyes.Climb!.Where(p => p.X is > 171f and < 176f), p => Assert.InRange(p.Y, 35.7f, 36.3f));

        // The world walk (W8, building exits): a door whose steps run straight along its lane but meet the ground past the 2.5 m strip is walked as raised too, down its floors: the
        // strip had ended on the deck among the deck's blocked nodes, its points at the ground's height under the body. Holtburg's 0xA9B40179 is one (77 doors over the dats).
        var town = NavTileBuilder.Build(HoltburgBlock(), null, out var holtburg);
        var balconies = NavTileBuilder.Build(builder.Build(new LandblockCoord(0xA9, 0xB3)), null, out var street);
        Assert.Equal(1, holtburg.LanesByStairs);
        Assert.Equal(1, holtburg.LanesRaisedLine);
        var deck = town.DoorwayLanes.Single(l => l.EntryCellId == 0xA9B40179);
        output.WriteLine($"0xA9B40179: sill {deck.Sill}: {string.Join(" ", deck.Climb!)}");
        var (dx, dy) = NavTile.NodeAt(new Vector2(deck.Climb![0].X, deck.Climb[0].Y));
        Assert.True(town.IsWalkable(dx, dy), $"the outer end {deck.Climb[0]}");
        Assert.InRange(deck.Sill.Z - deck.Climb[0].Z, 1.5f, 2.5f);
        Assert.InRange(deck.Climb[^1].Z, deck.Sill.Z - 0.6f, deck.Sill.Z + 0.1f);
        Assert.Equal(5, holtburg.PortalsAboveGround);
        Assert.Equal(0, street.LanesByStairs);
        Assert.Equal(2, street.PortalsAboveGround);
        Assert.DoesNotContain(balconies.DoorwayLanes, l => l.EntryCellId is 0xA9B30106 or 0xA9B30110);
    }

    /// <summary>
    /// The world walk (W8, building exits): a door 2.5 m wide and 1.79 m tall, its sill 0.21 m over the ground (0xE74E01A7, one of a design found across the dats), is a way out to the room
    /// graph, whose fit test probes the head 5 cm under 1.8 m, and the body walks it both ways; the lane cutter probed at the full 1.8 m and cut no lane, so every walk through it ended
    /// no-path. The cutter now probes as the graph does (<see cref="NavBuildOptions.HeadSlack"/>), and the lane joins the graph's exit.
    /// </summary>
    [RequiresDatsFact]
    public void A_door_a_centimetre_under_the_humans_height_gets_its_lane_as_the_room_graph_lists_it()
    {
        const uint door = 0xE74E01A7;
        var coord = new LandblockCoord(0xE7, 0x4E);
        var tile = NavTileBuilder.Build(new LandblockCollisionBuilder(Dats).Build(coord));
        var graph = new RoomGraph(Lifestone.Data.World.Cells.LandblockCellSet.Load(new Lifestone.Data.World.Cells.CellStructureLibrary(Dats), coord), RoomGraph.BuildingDoorwayLean);
        Assert.Contains(graph.ExitsToOutside, e => e.CellId == door);
        Assert.True(graph.TryJoin(tile, door, out var lane), "the door's lane joins the graph's exit");
        Assert.InRange(lane.Sill.Z - (NavTileBuilder.TerrainZ(new LandblockCollisionBuilder(Dats).Build(coord), new Vector2(lane.Sill.X, lane.Sill.Y) + lane.Outward) ?? float.NaN), 0.1f, 0.3f);
        Assert.False(graph.TryJoin(NavTileBuilder.Build(new LandblockCollisionBuilder(Dats).Build(coord), NavBuildOptions.Default with { HeadSlack = 0f }), door, out _), "probed at the full 1.8 m the door gets no lane");
    }

    [RequiresDatsFact]
    public void Holtburgs_landblock_builds_a_tile_under_100_ms_with_both_shops_doorway_lanes()
    {
        var block = HoltburgBlock();
        var first = NavTileBuilder.Build(block, null, out var firstStats); // includes the JIT of the builder
        var times = new List<double>();
        NavTile tile = first;
        NavBuildStats stats = firstStats;
        for (var i = 0; i < 5; i++)
        {
            var watch = Stopwatch.StartNew();
            tile = NavTileBuilder.Build(block, null, out stats);
            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        output.WriteLine($"{block}");
        output.WriteLine($"first build {firstStats.Milliseconds:F1} ms; five more (sorted) {string.Join(" ", times.Select(t => t.ToString("F1")))} ms; median {times[2]:F1} ms");
        output.WriteLine(stats.ToString());
        Assert.Equal(0xA9B40000u, tile.LandblockId);
        Assert.True(times[2] < 100.0, $"median build {times[2]:F1} ms");
        Assert.True(firstStats.Milliseconds < 400.0, $"first build {firstStats.Milliseconds:F1} ms");
        Assert.True(stats.StaticsStamped > 0 && stats.BuildingsStamped > 0, stats.ToString());
        Assert.Equal(0, stats.NodesWithoutTerrain);
        Assert.Equal(0, stats.ObstacleOverflow);
        Assert.Equal(block.Buildings.Sum(b => b.Portals.Count), stats.LanesBuilt + stats.PortalsImpassable + stats.PortalsAboveGround + stats.PortalsNoGeometry + stats.PortalsOffTile);

        // M6 4 w6a: five doors stand over the ground with no floor stepping down to it along their lanes (sills 1 to 3.5 m over the street: 0x012D, 0x0137, 0x015E, 0x016F, 0x0178),
        // so 23 lanes became 18; the shops' doors below keep theirs.
        Assert.Equal(5, stats.PortalsAboveGround);
        // M6 5 w: the buildings' doorway lean (RoomGraph.BuildingDoorwayLean, 0.3) adds the door of 0xA9B40170 (sill (155.4, 16.0, 94.0), normal Z 0.10, just over the upright rule's
        // 0.1), so 19; the dungeon-entrance caves' leaning mouths here (0x017F, 0x0184, 0x0189) pass the fit test but cut no lane on this build (not diagnosed: not on a walk).
        // The world walk (W8, building exits): the lane cutter probes the head as the room graph does (NavBuildOptions.HeadSlack), which adds the door of 0xA9B40177 (sill (62.3, 156.5, 66.2)),
        // a few millimetres under 1.8 m and walked both ways by the body, so 20.
        Assert.Equal(20, stats.LanesBuilt);

        // The lifestone's node: the town's arrival point from ACE's points of interest, beside the lifestone.
        var poi = AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg");
        Assert.Equal(LandblockCoord.Holtburg, LandblockCoord.FromId(poi.CellId));
        var (lx, ly) = NavTile.NodeAt(new Vector2(poi.Origin.X, poi.Origin.Y));
        output.WriteLine($"lifestone node ({lx},{ly}) z {tile.Z[NavTile.Index(lx, ly)]:F2} (point z {poi.Origin.Z:F2}) clearance {tile.Clearance[NavTile.Index(lx, ly)]}");
        Assert.True(tile.IsWalkable(lx, ly));
        Assert.True(MathF.Abs(tile.Z[NavTile.Index(lx, ly)] - poi.Origin.Z) < 1.0f, $"node z {tile.Z[NavTile.Index(lx, ly)]:F2} against {poi.Origin.Z:F2}");

        var reached = FloodFrom(tile, lx, ly);
        output.WriteLine($"flood fill from the lifestone reaches {reached.Count(r => r)} of {tile.Flags.Count(f => (f & NavFlags.Blocked) == 0)} walkable nodes");
        foreach (var target in new[] { RenaldShopCell, ThelnothCellarCell })
        {
            // The target is a room of a building; its door is the lane whose entry cell reaches it through the rooms' doorways.
            var lanes = tile.DoorwayLanes.Where(l => RoomsFrom(block, l.EntryCellId).Contains(target)).ToList();
            Assert.True(lanes.Count > 0, $"no doorway lane leads to 0x{target:X8}; lanes: {string.Join(", ", tile.DoorwayLanes.Select(l => $"0x{l.EntryCellId:X8}"))}");
            foreach (var lane in lanes)
            {
                var outer = lane.Nodes[^1];
                output.WriteLine($"lane to 0x{target:X8} via entry 0x{lane.EntryCellId:X8}: building {lane.BuildingIndex}, sill {lane.Sill}, outward {lane.Outward}, {lane.Nodes.Count} nodes, outer end ({outer % NavTile.Size},{outer / NavTile.Size}) clearance {tile.Clearance[outer]}, connected {reached[outer]}");
                Assert.True(lane.Nodes.Count >= 4);
                Assert.All(lane.Nodes, n => Assert.NotEqual(NavFlags.None, tile.Flags[n] & NavFlags.DoorwayLane));
                Assert.True((tile.Flags[outer] & NavFlags.Blocked) == 0, "the lane's outer end is walkable");
                var origin = block.Buildings[lane.BuildingIndex].Position.Frame.Origin;
                Assert.True(Vector2.Distance(new Vector2(origin.X, origin.Y), NavTile.NodeCentre(outer % NavTile.Size, outer / NavTile.Size)) > 1.0f);
            }

            Assert.Contains(lanes, l => reached[l.Nodes[^1]]);
        }
    }

    /// <summary>Every EnvCell reachable from <paramref name="entry"/> through the rooms' inside doorways (the building's rooms).</summary>
    private static HashSet<uint> RoomsFrom(LandblockCollision block, uint entry)
    {
        var seen = new HashSet<uint> { entry };
        var queue = new Queue<uint>();
        queue.Enqueue(entry);
        while (queue.Count > 0)
        {
            if (!block.EnvCells.TryGetValue(queue.Dequeue(), out var cell))
            {
                continue;
            }

            foreach (var portal in cell.Portals)
            {
                if (!portal.LeadsOutside && seen.Add(portal.OtherCellId))
                {
                    queue.Enqueue(portal.OtherCellId);
                }
            }
        }

        return seen;
    }

    /// <summary>The walkable nodes connected to (x, y) by 8-neighbour steps that do not cut a blocked corner.</summary>
    private static bool[] FloodFrom(NavTile tile, int x, int y)
    {
        var seen = new bool[NavTile.Size * NavTile.Size];
        var queue = new Queue<int>();
        seen[NavTile.Index(x, y)] = true;
        queue.Enqueue(NavTile.Index(x, y));
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            int cx = i % NavTile.Size, cy = i / NavTile.Size;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if ((dx == 0 && dy == 0) || !NavTile.InBounds(nx, ny) || seen[NavTile.Index(nx, ny)] || !tile.IsWalkable(nx, ny))
                    {
                        continue;
                    }

                    if (dx != 0 && dy != 0 && (!tile.IsWalkable(cx + dx, cy) || !tile.IsWalkable(cx, cy + dy)))
                    {
                        continue;
                    }

                    seen[NavTile.Index(nx, ny)] = true;
                    queue.Enqueue(NavTile.Index(nx, ny));
                }
            }
        }

        return seen;
    }
}
