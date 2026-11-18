using System.Diagnostics;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.WorldWalk;

/// <summary>
/// The pilot (W0): a spread of landblocks of every kind, each scanned, walked east and north at top speed, walked east at low-level speed, and probed as the physics oracle (W3) will probe, so the plan's
/// costs can be replaced with measured ones.
/// </summary>
internal static class Pilot
{
    /// <summary>The share of the sample each kind takes, in hundredths.</summary>
    private static readonly (string Kind, int Share)[] Quotas = [("sea", 10), ("land", 25), ("coast", 15), ("Dungeon", 15), ("BuildingInteriors", 20), ("Mixed", 15)];

    /// <summary>A low-level character's Run skill, about 4.8 m/s (docs/session.md's low-level pace).</summary>
    private const int LowRunSkill = 15;

    /// <summary>Metres between the probes, and how far each hop goes, as the oracle's 4 m grid does. The grid is offset 2 m, off the 24 m lines placement refuses (TODO(placement-cell-edge)).</summary>
    private const float ProbeSpacing = 24f;
    private const float HopMetres = 4f;

    private const double TimeLimitBase = 60;
    private const double TimeLimitMetresPerSecond = 2.5;

    /// <summary>Picks the sample: every id shuffled by the seed, taken while its kind's quota lasts. The same seed picks the same landblocks.</summary>
    public static IReadOnlyList<LandblockCoord> Pick(WalkContext context, int seed, int sample)
    {
        var quota = Quotas.ToDictionary(q => q.Kind, q => Math.Max(1, sample * q.Share / 100));
        var withInfo = CellDiscovery(context);
        var random = new Random(seed);
        var ids = new List<LandblockCoord>(LandDefs.BlocksPerSide * LandDefs.BlocksPerSide);
        for (var x = 0; x < LandDefs.BlocksPerSide; x++)
        {
            for (var y = 0; y < LandDefs.BlocksPerSide; y++)
            {
                ids.Add(new LandblockCoord((byte)x, (byte)y));
            }
        }

        var picked = new List<LandblockCoord>(sample);
        foreach (var coord in ids.OrderBy(_ => random.Next()))
        {
            if (quota.Values.All(v => v == 0))
            {
                break;
            }

            string kind;
            if (withInfo.Contains(coord))
            {
                kind = Lifestone.Data.World.Cells.CellDiscovery.Summarize(context.Structures, coord).Kind.ToString();
                kind = kind == "NoCells" ? "land" : kind;
            }
            else
            {
                if (context.Dats.GetLandblockTerrain(coord) is not { } terrain)
                {
                    continue;
                }

                kind = LandblockCollisionBuilder.FromTerrain(terrain, context.Dats.TerrainRegion.LandHeightTable).WaterType switch
                {
                    WaterType.EntirelyWater => "sea",
                    WaterType.PartiallyWater => "coast",
                    _ => "land",
                };
            }

            if (quota.TryGetValue(kind, out var left) && left > 0)
            {
                quota[kind] = left - 1;
                picked.Add(coord);
            }
        }

        return picked;
    }

    /// <summary>Scans, walks and probes one landblock.</summary>
    public static (object Result, object Timing) Run(WalkContext context, OfflineWalkRig rig, LandblockCoord coord)
    {
        var id = Census.IdOf(coord);
        var (row, tile, collisionMs, tileMs) = Census.Scan(context, coord);
        var walks = new List<object>();
        var walkMs = new List<double>();
        var walkSim = 0.0;
        (int Stands, int StandsOk, int Hops, int HopsOk, double StandMs, double HopMs) probe = default;
        if (tile is not null && WalkContext.MiddleNode(tile) is { } start)
        {
            Walk(context, rig, tile, start, coord, 1, 0, "east", OfflinePlayerWeenie.PlaceholderRunSkill, walks, walkMs, ref walkSim);
            Walk(context, rig, tile, start, coord, 0, 1, "north", OfflinePlayerWeenie.PlaceholderRunSkill, walks, walkMs, ref walkSim);
            Walk(context, rig, tile, start, coord, 1, 0, "east-low", LowRunSkill, walks, walkMs, ref walkSim);
            probe = Probe(context, coord, tile, start);
        }

        var result = new { id, row, walks, probe = new { probe.Stands, probe.StandsOk, probe.Hops, probe.HopsOk } };
        var timing = new
        {
            id,
            collision_ms = Math.Round(collisionMs, 2),
            tile_ms = Math.Round(tileMs, 2),
            walk_ms = walkMs.Select(m => Math.Round(m, 1)).ToArray(),
            walk_sim_s = Math.Round(walkSim, 1),
            stand_us = probe.Stands == 0 ? 0 : Math.Round(probe.StandMs * 1000 / probe.Stands, 1),
            hop_us = probe.Hops == 0 ? 0 : Math.Round(probe.HopMs * 1000 / probe.Hops, 1),
        };
        return (result, timing);
    }

    private static void Walk(WalkContext context, OfflineWalkRig rig, NavTile tile, (int X, int Y) start, LandblockCoord coord, int dx, int dy, string name, int runSkill, List<object> walks, List<double> walkMs, ref double walkSim)
    {
        if (coord.Offset(dx, dy) is not { } next || !LandDefs.IsOnMap(next))
        {
            return;
        }

        var nextTile = context.Tile(next, out _);
        if (WalkContext.MiddleNode(nextTile) is not { } end)
        {
            walks.Add(new { to = name, verdict = "no-open-node-next-door" });
            return;
        }

        var from = NavMap.ToCellFrame(WalkContext.PointOf(tile, start.X, start.Y));
        var goal = WalkContext.PointOf(nextTile, end.X, end.Y);
        var line = Vector2.Distance(NavMap.FromCellFrame(from).Global, goal.Global);
        var walk = rig.Walk(new OfflineWalkRequest(Census.IdOf(coord) + "-" + name, from, NavGoal.ToPoint(goal), TimeLimitBase + (line / TimeLimitMetresPerSecond)) { RunSkill = runSkill });
        walkMs.Add(walk.WallMilliseconds);
        walkSim += walk.SimSeconds;
        walks.Add(new
        {
            to = name,
            verdict = walk.Clean ? "ok" : "failed",
            state = walk.State?.ToString(),
            reason = walk.Reason,
            rungs = walk.Rungs,
            refused = walk.ConfirmFailures,
            replans = walk.Replans,
            line_m = Math.Round(line, 1),
            route_m = Math.Round(walk.RouteMetres, 1),
            walked_m = Math.Round(walk.WalkedMetres, 1),
            left_m = Math.Round(walk.LeftMetres, 2),
            sim_s = Math.Round(walk.SimSeconds, 1),
            rungs_at = walk.RungsAt,
        });
    }

    /// <summary>A placement and eight hops on a coarse grid, the two probes the oracle is made of: what they cost is what W3 will cost.</summary>
    private static (int Stands, int StandsOk, int Hops, int HopsOk, double StandMs, double HopMs) Probe(WalkContext context, LandblockCoord coord, NavTile tile, (int X, int Y) start)
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
        if (!walker.Place(NavMap.ToCellFrame(WalkContext.PointOf(tile, start.X, start.Y))))
        {
            return default;
        }

        var (stands, standsOk, hops, hopsOk) = (0, 0, 0, 0);
        var standClock = new Stopwatch();
        var hopClock = new Stopwatch();
        for (var gy = 2f; gy < NavTile.Size; gy += ProbeSpacing)
        {
            for (var gx = 2f; gx < NavTile.Size; gx += ProbeSpacing)
            {
                var (x, y) = NavTile.NodeAt(new Vector2(gx, gy));
                var at = NavMap.ToCellFrame(new NavPoint(tile.LandblockId, new Vector3(gx, gy, tile.Z[NavTile.Index(x, y)])));
                stands++;
                standClock.Start();
                var stood = walker.Physics.SetPosition(at, SetPositionFlags.Placement) == SetPositionError.OK;
                standClock.Stop();
                if (!stood)
                {
                    continue;
                }

                standsOk++;
                var here = walker.Physics.Position;
                for (var d = 0; d < 8; d++)
                {
                    var angle = d * MathF.PI / 4f;
                    var offset = new Vector3(MathF.Cos(angle) * HopMetres, MathF.Sin(angle) * HopMetres, 0f);
                    var to = new CellFrame(here.CellId, new AcFrame(here.Origin + offset, here.Frame.Orientation));
                    hops++;
                    hopClock.Start();
                    hopsOk += walker.Physics.TryTransitionTo(to, carriedContact: true) ? 1 : 0;
                    hopClock.Stop();
                    _ = walker.Physics.SetPosition(here, SetPositionFlags.Placement);
                }
            }
        }

        return (stands, standsOk, hops, hopsOk, standClock.Elapsed.TotalMilliseconds, hopClock.Elapsed.TotalMilliseconds);
    }

    private static HashSet<LandblockCoord> CellDiscovery(WalkContext context) => [.. Lifestone.Data.World.Cells.CellDiscovery.LandblocksWithInfo(context.Dats)];
}
