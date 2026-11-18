using System.Numerics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.WorldWalk;

/// <summary>
/// The outdoor walks (W4): each landblock's east and north edges, both ways, at top speed and at a low-level character's speed, between land the world graph says joins. Land the graph says does not join
/// is not walked here; it is on the record as <c>no-shared-area</c>, and the physics oracle (W3) judges whether the graph was right.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class EdgeWalks
{
    /// <summary>A low-level character's Run skill, about 4.8 m/s.</summary>
    private const int LowRunSkill = 15;

    private const double TimeLimitBase = 120;
    private const double TimeLimitMetresPerSecond = 1.5;

    /// <summary>The second try of a walk that ran out of time with the world routes on, in simulated seconds: the first limit comes from the straight line, and the way round a mountain can be kilometres.</summary>
    private const double LongerLimitSeconds = 1200;

    /// <summary>Walks one landblock's edges. A walk that was not clean keeps every detail; a clean one is counted.</summary>
    public static object Run(WalkContext context, OfflineWalkRig rig, AreaIndex areas, LandblockCoord coord, NavMap map, WorldRoutes? routes = null)
    {
        var id = Census.IdOf(coord);
        var edges = new List<object>();
        var (clean, walks) = (0, 0);
        if (areas.Has(coord))
        {
            var tile = context.Tile(coord, out _);
            foreach (var (dx, dy, name) in new[] { (1, 0, "east"), (0, 1, "north") })
            {
                if (coord.Offset(dx, dy) is not { } next || !LandDefs.IsOnMap(next) || !areas.Has(next))
                {
                    continue;
                }

                var nextTile = context.Tile(next, out _);
                map.Attach(tile);
                map.Attach(nextTile);
                var pair = areas.SharedNodes(map, coord, tile, next, nextTile);
                map.Detach(tile.LandblockId);
                map.Detach(nextTile.LandblockId);
                if (pair is not { } ends)
                {
                    edges.Add(new { to = name, verdict = "no-shared-area" });
                    continue;
                }

                var here = WalkContext.PointOf(tile, ends.A.X, ends.A.Y);
                var there = WalkContext.PointOf(nextTile, ends.B.X, ends.B.Y);
                var bad = new List<object>();
                foreach (var (from, to, way) in new[] { (here, there, "out"), (there, here, "back") })
                {
                    foreach (var (skill, pace) in new[] { (OfflinePlayerWeenie.PlaceholderRunSkill, "top"), (LowRunSkill, "low") })
                    {
                        var line = Vector2.Distance(from.Global, to.Global);
                        var walk = rig.Walk(new OfflineWalkRequest($"{id}-{name}-{way}-{pace}", NavMap.ToCellFrame(from), NavGoal.ToPoint(to), TimeLimitBase + (line / TimeLimitMetresPerSecond)) { RunSkill = skill, Routes = routes });
                        walks++;
                        if (walk.Clean)
                        {
                            clean++;
                            continue;
                        }

                        string? longerLimit = null;
                        bool? longerLimitClean = null;
                        double? longerLimitRouteMetres = null;
                        double? longerLimitLeftMetres = null;
                        double? longerLimitSeconds = null;
                        if (routes != null && walk.Reason == OfflineWalkRig.OverTime)
                        {
                            var again = rig.Walk(new OfflineWalkRequest($"{id}-{name}-{way}-{pace}-longer", NavMap.ToCellFrame(from), NavGoal.ToPoint(to), LongerLimitSeconds) { RunSkill = skill, Routes = routes });
                            longerLimit = again.Reason ?? again.State?.ToString();
                            longerLimitClean = again.Clean;
                            longerLimitRouteMetres = Math.Round(again.RouteMetres, 1);
                            longerLimitLeftMetres = Math.Round(again.LeftMetres, 2);
                            longerLimitSeconds = Math.Round(again.SimSeconds, 1);
                        }

                        bad.Add(new
                        {
                            way,
                            pace,
                            state = walk.State?.ToString(),
                            reason = walk.Reason,
                            rungs = walk.Rungs,
                            refused = walk.ConfirmFailures,
                            replans = walk.Replans,
                            route_m = Math.Round(walk.RouteMetres, 1),
                            walked_m = Math.Round(walk.WalkedMetres, 1),
                            left_m = Math.Round(walk.LeftMetres, 2),
                            sim_s = Math.Round(walk.SimSeconds, 1),
                            rungs_at = walk.RungsAt,
                            refused_why = walk.RefusedWhy,
                            longer_limit = longerLimit,
                            longer_limit_clean = longerLimitClean,
                            longer_limit_route_m = longerLimitRouteMetres,
                            longer_limit_left_m = longerLimitLeftMetres,
                            longer_limit_sim_s = longerLimitSeconds,
                        });
                    }
                }

                edges.Add(new { to = name, verdict = bad.Count == 0 ? "ok" : "failed", ends = new[] { ends.A.X, ends.A.Y, ends.B.X, ends.B.Y }, bad });
            }
        }

        return new { id, walks, clean, edges };
    }
}

/// <summary>Which area each coarse region belongs to, as the world graph left it in <c>regions.bin</c>.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class AreaIndex
{
    private readonly Dictionary<ushort, int[]> _blocks = [];
    private readonly int[] _sizes;

    public AreaIndex(string graphRun)
    {
        using var reader = new BinaryReader(File.OpenRead(Path.Combine(graphRun, WorldGraph.RegionsFile)));
        _sizes = new int[reader.ReadInt32() + 1];
        for (var i = 1; i < _sizes.Length; i++)
        {
            _sizes[i] = reader.ReadInt32();
        }

        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var block = reader.ReadUInt16();
            var areas = new int[reader.ReadInt32()];
            for (var r = 0; r < areas.Length; r++)
            {
                areas[r] = reader.ReadInt32();
            }

            _blocks[block] = areas;
        }
    }

    public bool Has(LandblockCoord coord) => _blocks.ContainsKey(Key(coord));

    /// <summary>The area of a landblock's region.</summary>
    public int AreaOf(uint landblockId, int region) => Area(LandblockCoord.FromId(landblockId), region);

    /// <summary>An area's size in nodes.</summary>
    public int NodesOf(int area) => _sizes[area];

    /// <summary>A node in each tile in the largest area the two share, nearest each tile's middle, with room round it and dry; null when they share none.</summary>
    public ((int X, int Y) A, (int X, int Y) B)? SharedNodes(NavMap map, LandblockCoord a, NavTile tileA, LandblockCoord b, NavTile tileB)
    {
        var sizesA = Sizes(map, a, tileA);
        var sizesB = Sizes(map, b, tileB);
        var shared = sizesA.Keys.Where(sizesB.ContainsKey).OrderByDescending(k => Math.Min(sizesA[k], sizesB[k])).ThenBy(k => k).Cast<int?>().FirstOrDefault();
        if (shared is not { } area)
        {
            return null;
        }

        return Nearest(map, a, tileA, area) is { } first && Nearest(map, b, tileB, area) is { } second ? (first, second) : null;
    }

    private static ushort Key(LandblockCoord coord) => (ushort)((coord.X * LandDefs.BlocksPerSide) + coord.Y);

    private int AreaAt(NavMap map, LandblockCoord coord, NavTile tile, int x, int y)
    {
        var region = map.RegionAt(tile.LandblockId, x, y);
        return region < 0 ? 0 : Area(coord, region);
    }

    /// <summary>
    /// A region's area, refusing loudly when the saved graph was built from other tiles than this build makes: a change to the tile builder that splits a
    /// region gives a landblock more regions than the graph knows (the border fix of 2026-11-11 did, and 43 starts threw an index fault).
    /// </summary>
    private int Area(LandblockCoord coord, int region)
    {
        var areas = _blocks[Key(coord)];
        if (region >= areas.Length)
        {
            throw new InvalidDataException($"stale graph: landblock {coord.X:X2}{coord.Y:X2} has region {region} but the graph knows {areas.Length}; rebuild the graph with this build");
        }

        return areas[region];
    }

    private Dictionary<int, int> Sizes(NavMap map, LandblockCoord coord, NavTile tile)
    {
        var sizes = new Dictionary<int, int>();
        for (var y = 0; y < NavTile.Size; y++)
        {
            for (var x = 0; x < NavTile.Size; x++)
            {
                if (AreaAt(map, coord, tile, x, y) is var area and > 0)
                {
                    sizes[area] = sizes.GetValueOrDefault(area) + 1;
                }
            }
        }

        return sizes;
    }

    private (int X, int Y)? Nearest(NavMap map, LandblockCoord coord, NavTile tile, int area)
    {
        const int Middle = NavTile.Size / 2;
        foreach (var minClearance in new byte[] { 2, 1 })
        {
            for (var r = 0; r <= Middle; r++)
            {
                for (var y = Middle - r; y <= Middle + r; y++)
                {
                    for (var x = Middle - r; x <= Middle + r; x++)
                    {
                        if (Math.Max(Math.Abs(x - Middle), Math.Abs(y - Middle)) != r || !NavTile.InBounds(x, y))
                        {
                            continue;
                        }

                        var i = NavTile.Index(x, y);
                        if (tile.IsWalkable(x, y) && tile.Clearance[i] >= minClearance && (tile.Flags[i] & NavFlags.Water) == 0 && AreaAt(map, coord, tile, x, y) == area)
                        {
                            return (x, y);
                        }
                    }
                }
            }
        }

        return null;
    }
}
