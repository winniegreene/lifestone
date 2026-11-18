using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// World walk W0 on the real dats (Exhaustive tier, skips without them): the rig's walks repeat exactly, and the rig is not blind. A checker that cannot fail proves nothing, so each planted fault here is a
/// wrong map the walk must report: a wall the tile has lost, and a goal sealed off. Both tamper with the walk's own copy of the tile; nothing else sees it.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class OfflineWalkRigDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly LandblockCoord South = new(0xA9, 0xB3);
    private static readonly LandblockCoord North = LandblockCoord.Holtburg;

    private DatSet Dats => fixture.Dats;

    private OfflineWalkRig NewRig()
    {
        var geometry = new PhysicsGeometryLibrary(Dats);
        return new OfflineWalkRig(Dats, geometry, new DatAnimationSource(Dats), new LandblockCollisionBuilder(Dats, geometry).Build, NullLogSink.Instance);
    }

    private NavTile TileOf(LandblockCoord coord) => NavTileBuilder.Build(new LandblockCollisionBuilder(Dats).Build(coord));

    private static NavPoint Point(NavTile tile, int x, int y)
    {
        var centre = NavTile.NodeCentre(x, y);
        return new NavPoint(tile.LandblockId, new Vector3(centre.X, centre.Y, tile.Z[NavTile.Index(x, y)]));
    }

    private static (int X, int Y) MiddleOf(NavTile tile)
    {
        for (var r = 0; r < 96; r++)
        {
            for (var y = 96 - r; y <= 96 + r; y++)
            {
                for (var x = 96 - r; x <= 96 + r; x++)
                {
                    if (NavTile.InBounds(x, y) && tile.IsWalkable(x, y) && tile.Clearance[NavTile.Index(x, y)] >= 2)
                    {
                        return (x, y);
                    }
                }
            }
        }

        throw new InvalidOperationException($"{tile} has no open node");
    }

    private OfflineWalkRequest SouthToNorth(Action<NavMap>? tamper = null)
    {
        var from = TileOf(South);
        var to = TileOf(North);
        var (fx, fy) = MiddleOf(from);
        var (tx, ty) = MiddleOf(to);
        return new OfflineWalkRequest("rig", NavMap.ToCellFrame(Point(from, fx, fy)), NavGoal.ToPoint(Point(to, tx, ty)), 200) { TamperAfterAttach = tamper };
    }

    [RequiresDatsFact]
    public void The_same_walk_repeats_exactly()
    {
        var first = NewRig().Walk(SouthToNorth());
        var second = NewRig().Walk(SouthToNorth());
        output.WriteLine($"{first.State} {first.WalkedMetres:F1} m in {first.SimSeconds:F1} s, {first.WallMilliseconds:F0} ms");
        Assert.True(first.Clean, first.Reason);
        Assert.Equal(first with { WallMilliseconds = 0, RungsAt = [], RefusedWhy = [] }, second with { WallMilliseconds = 0, RungsAt = [], RefusedWhy = [] });
        Assert.Equal(first.RungsAt, second.RungsAt);
        Assert.Equal(first.RefusedWhy, second.RefusedWhy);
    }

    /// <summary>The first planted fault: Holtburg's tile forgets its buildings, so the plan runs straight through a wall. The physics must refuse what the map allowed; a clean arrival would mean the rig cannot see a falsely open map.</summary>
    [RequiresDatsFact]
    public void A_wall_the_tile_has_lost_is_reported()
    {
        var tile = TileOf(North);
        var (y, west, east) = AcrossABuilding(tile);
        var start = NavMap.ToCellFrame(Point(tile, west, y));
        var goal = NavGoal.ToPoint(Point(tile, east, y));

        var honest = NewRig().Walk(new OfflineWalkRequest("honest", start, goal, 200));
        var planted = NewRig().Walk(new OfflineWalkRequest("planted", start, goal, 200)
        {
            TamperAfterAttach = map =>
            {
                Assert.True(map.TryGetTile(tile.LandblockId, out var held));
                for (var i = 0; i < held.Flags.Length; i++)
                {
                    if ((held.Flags[i] & NavFlags.Building) != 0)
                    {
                        held.Flags[i] &= ~(NavFlags.Blocked | NavFlags.Building);
                    }
                }

                NavTileBuilder.ComputeClearance(held);
                map.Attach(held);
            },
        });

        output.WriteLine($"row {y}, x {west} to {east}");
        output.WriteLine($"honest: {honest.State} route {honest.RouteMetres:F1} m, refused {honest.ConfirmFailures}, rungs {honest.Rungs}, {honest.Reason}");
        output.WriteLine($"planted: {planted.State} route {planted.RouteMetres:F1} m, refused {planted.ConfirmFailures}, rungs {planted.Rungs}, {planted.Reason}");
        Assert.True(honest.Clean, honest.Reason);
        Assert.False(planted.Clean, "the walk through a building reported clean: the rig cannot see a falsely open map");
    }

    /// <summary>The second planted fault: the goal is walled in on the map alone, with open ground behind the wall. The walk must end by name, never as an arrival.</summary>
    [RequiresDatsFact]
    public void A_goal_the_tile_has_sealed_off_is_reported()
    {
        const int Ring = 6;
        var to = TileOf(North);
        var (gx, gy) = MiddleOf(to);
        var planted = NewRig().Walk(SouthToNorth(map =>
        {
            Assert.True(map.TryGetTile(to.LandblockId, out var held));
            for (var y = gy - Ring; y <= gy + Ring; y++)
            {
                for (var x = gx - Ring; x <= gx + Ring; x++)
                {
                    if (Math.Max(Math.Abs(x - gx), Math.Abs(y - gy)) == Ring)
                    {
                        held.Block(x, y);
                    }
                }
            }

            NavTileBuilder.ComputeClearance(held);
            map.Attach(held);
        }));

        output.WriteLine($"planted: {planted.State}, {planted.Reason}");
        Assert.False(planted.Clean);
        Assert.Equal(NavRunState.Blocked, planted.State);
        Assert.Contains("no-path", planted.Reason, StringComparison.Ordinal);
    }

    /// <summary>The oracle's planted fault: open ground the map has wrongly blocked. The tiles decide nothing in the oracle, so the body walks onto the band and the scan must say so.</summary>
    [RequiresDatsFact]
    public void The_oracle_reports_open_ground_the_map_has_wrongly_blocked()
    {
        var clean = OracleOf(South, _ => { });
        output.WriteLine($"clean: {Line(clean)}");
        var planted = OracleOf(South, tile =>
        {
            for (var y = 90; y < 102; y++)
            {
                for (var x = 0; x < NavTile.Size; x++)
                {
                    tile.Block(x, y);
                }
            }
        });
        output.WriteLine($"planted: {Line(planted)}");
        var before = clean.Counts.GetValueOrDefault(PhysicsOracle.WalkedOntoBlocked);
        var after = planted.Counts.GetValueOrDefault(PhysicsOracle.WalkedOntoBlocked);
        Assert.True(after > before + 50, $"the blocked band added {after - before} findings: the oracle cannot see a falsely closed map");
    }

    /// <summary>The oracle's other planted fault: a building's footprint opened on the map. The physics refuses to stand a body inside the walls, whatever the map says.</summary>
    [RequiresDatsFact]
    public void The_oracle_reports_ground_the_map_has_wrongly_opened()
    {
        var clean = OracleOf(North, _ => { });
        output.WriteLine($"clean: {Line(clean)}");
        var planted = OracleOf(North, tile =>
        {
            for (var i = 0; i < tile.Flags.Length; i++)
            {
                if ((tile.Flags[i] & NavFlags.Building) != 0)
                {
                    tile.Flags[i] &= ~(NavFlags.Blocked | NavFlags.Building);
                }
            }
        });
        output.WriteLine($"planted: {Line(planted)}");
        var before = clean.Counts.GetValueOrDefault(PhysicsOracle.RefusedOnOpen);
        var after = planted.Counts.GetValueOrDefault(PhysicsOracle.RefusedOnOpen);
        Assert.True(after > before, $"opening the buildings added {after - before} findings: the oracle cannot see a falsely open map");
    }

    /// <summary>The oracle's third planted fault: a blocked band cuts the tile in two, and the two sides are separate areas. The body crosses, so the map has closed a passage; a band with one area on both sides is not one.</summary>
    [RequiresDatsFact]
    public void The_oracle_reports_a_passage_the_map_has_closed()
    {
        static void Band(NavTile tile)
        {
            for (var y = 90; y < 102; y++)
            {
                for (var x = 0; x < NavTile.Size; x++)
                {
                    tile.Block(x, y);
                }
            }
        }

        var oneArea = OracleOf(South, Band);
        var twoAreas = OracleOf(South, Band, map => (id, region) => map.RegionsOf(id)[region].Centroid.Y % NavTile.Size < NavTile.Size / 2 ? 1 : 2);
        output.WriteLine($"one area: {Line(oneArea)}");
        output.WriteLine($"two areas: {Line(twoAreas)}");
        Assert.Equal(0, oneArea.Counts.GetValueOrDefault(PhysicsOracle.ClosedPassage));
        Assert.True(twoAreas.Counts.GetValueOrDefault(PhysicsOracle.ClosedPassage) > 0, "the band between two areas went unreported: the oracle cannot see a closed passage");
    }

    /// <summary>A hop along a link the map holds is the planner's own way, not a finding: with every region its own area, the links between them answer for the hops.</summary>
    [RequiresDatsFact]
    public void The_oracle_counts_a_hop_along_a_known_link_as_no_finding()
    {
        var split = OracleOf(South, _ => { }, _ => (_, region) => region + 1);
        output.WriteLine($"split: {Line(split)}, known drops {split.KnownDrops}");
        Assert.True(split.KnownDrops > 100, $"only {split.KnownDrops} hops went by a link");
        Assert.Equal(0, split.Counts.GetValueOrDefault(PhysicsOracle.JoinedAreas));
    }

    private static string Line(OracleReport r) =>
        $"stood {r.Stood}/{r.Points}, hops {r.HopsLanded}/{r.Hops}, agreed {r.Agreed}, " + string.Join(", ", r.Counts.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}"));

    /// <summary>One landblock's scan with its tile tampered with; every region is its own number so only the two map-against-physics kinds above are read.</summary>
    private OracleReport OracleOf(LandblockCoord coord, Action<NavTile> tamper, Func<NavMap, Func<uint, int, int>>? areas = null)
    {
        var geometry = new PhysicsGeometryLibrary(Dats);
        var builder = new LandblockCollisionBuilder(Dats, geometry);
        var world = new PhysicsWorld();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (coord.Offset(dx, dy) is { } near)
                {
                    world.Attach(builder.Build(near));
                }
            }
        }

        var tile = NavTileBuilder.Build(world.GetLandblock(coord)!);
        var (x, y) = MiddleOf(tile);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, geometry: geometry);
        world.UseTime(0.0);
        Assert.True(walker.Place(NavMap.ToCellFrame(Point(tile, x, y))));
        tamper(tile);
        NavTileBuilder.ComputeClearance(tile);
        var map = new NavMap();
        map.Attach(tile);
        return new PhysicsOracle(walker, map, areas?.Invoke(map) ?? ((_, _) => 1), _ => 1000).Scan(tile.LandblockId);
    }

    /// <summary>A row of the tile with a building on it wider than a few metres and open ground on both sides, so the walk round it and the walk through it differ.</summary>
    private static (int Y, int West, int East) AcrossABuilding(NavTile tile)
    {
        const int MinWidth = 6;
        const int Margin = 4;
        for (var y = 8; y < NavTile.Size - 8; y++)
        {
            var run = 0;
            for (var x = Margin; x < NavTile.Size - Margin; x++)
            {
                if ((tile.Flags[NavTile.Index(x, y)] & NavFlags.Building) != 0)
                {
                    run++;
                    continue;
                }

                if (run >= MinWidth && Open(tile, x + Margin - 1, y) && Open(tile, x - run - Margin, y))
                {
                    return (y, x - run - Margin, x + Margin - 1);
                }

                run = 0;
            }
        }

        throw new InvalidOperationException($"{tile} has no building with open ground on both sides");
    }

    private static bool Open(NavTile tile, int x, int y) => NavTile.InBounds(x, y) && tile.IsWalkable(x, y) && tile.Clearance[NavTile.Index(x, y)] >= 2;
}
