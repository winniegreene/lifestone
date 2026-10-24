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
        Assert.Equal(block.Buildings.Sum(b => b.Portals.Count), stats.LanesBuilt + stats.PortalsImpassable + stats.PortalsNoGeometry + stats.PortalsOffTile);

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
