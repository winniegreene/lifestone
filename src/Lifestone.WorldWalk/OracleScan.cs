using System.Runtime.Versioning;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.WorldWalk;

/// <summary>Runs the physics oracle (W3) over one landblock, in a new world, so no landblock depends on the one before. A hop into the east, north or north-east neighbour is judged on that neighbour's tile; the reverse hop is that neighbour's own scan.</summary>
[SupportedOSPlatform("windows")]
internal static class OracleScan
{
    public static object Run(WalkContext context, AreaIndex areas, LandblockCoord coord, NavMap map)
    {
        var id = Census.IdOf(coord);
        if (!areas.Has(coord))
        {
            return new { id, skipped = "no walkable ground" };
        }

        var attached = new List<uint>(4);
        foreach (var (dx, dy) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
        {
            if (coord.Offset(dx, dy) is { } near && LandDefs.IsOnMap(near) && areas.Has(near))
            {
                var tile = context.Tile(near, out _);
                map.Attach(tile);
                attached.Add(tile.LandblockId);
            }
        }

        try
        {
            var world = new PhysicsWorld();
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (coord.Offset(dx, dy) is { } near && LandDefs.IsOnMap(near))
                    {
                        world.Attach(context.Store.Get(near));
                    }
                }
            }

            var walker = OfflineWalker.Create(context.Dats, world, NullLogSink.Instance, geometry: context.Geometry, animations: context.Animations);
            world.UseTime(0.0);
            var blockId = WalkContext.BlockId(coord);
            if (!map.TryGetTile(blockId, out var own))
            {
                return new { id, skipped = "no tile" };
            }

            var tried = 0;
            var placed = WalkContext.MiddleNode(own) is { } middle && walker.Place(NavMap.ToCellFrame(WalkContext.PointOf(own, middle.X, middle.Y)));
            for (var y = 2; y < NavTile.Size && !placed; y += 4)
            {
                for (var x = 2; x < NavTile.Size && !placed; x += 4)
                {
                    if (own.IsWalkable(x, y))
                    {
                        tried++;
                        placed = walker.Place(NavMap.ToCellFrame(WalkContext.PointOf(own, x, y)));
                    }
                }
            }

            if (!placed)
            {
                return new { id, skipped = "the body could not be placed", tried };
            }

            var oracle = new PhysicsOracle(walker, map, areas.AreaOf, areas.NodesOf);
            var report = oracle.Scan(blockId);
            return new
            {
                id,
                report.Points,
                report.Stood,
                report.Hops,
                report.HopsLanded,
                report.Agreed,
                report.KnownDrops,
                report.Indoors,
                report.OtherLevel,
                report.OffMap,
                report.StatesDropped,
                counts = report.Counts.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value),
                samples = report.Samples.Select(s => new
                {
                    s.Kind,
                    from = Text(s.From),
                    to = Text(s.To),
                    flags = s.Flags.ToString(),
                    s.Detail,
                }).ToArray(),
            };
        }
        finally
        {
            foreach (var tile in attached)
            {
                map.Detach(tile);
            }
        }
    }

    private static string Text(NavPoint point) => FormattableString.Invariant($"{point.LandblockId >> 16:X4} {point.Local.X:F1},{point.Local.Y:F1},{point.Local.Z:F1}");
}
