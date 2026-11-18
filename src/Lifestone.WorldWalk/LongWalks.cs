using System.Globalization;
using System.Numerics;
using System.Runtime.Versioning;
using System.Text.Json;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.WorldWalk;

/// <summary>
/// The long walks (W4's last part). The edge walks are a landblock long, so they never ask the runner to plan past what it can see or to replan as it goes. From a sample of the main landmass's landblocks,
/// each start walks to a goal in each distance band, where the world graph says the two ends share an area: a no-path there is the planner's, not the land's.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class LongWalks
{
    /// <summary>The nearer edge of each distance band, in metres; a goal lies up to half as far again. Not retail: the walk's own.</summary>
    private static readonly int[] Bands = [1000, 2000, 4000, 8000];

    /// <summary>How many goals are tried in a band before it is recorded as having none.</summary>
    private const int TriesPerBand = 24;

    /// <summary>The time limit, as the edge walks set it. Not retail: the walk's own.</summary>
    private const double TimeLimitBase = 120;

    private const double TimeLimitMetresPerSecond = 1.5;

    /// <summary>A landblock's side in metres.</summary>
    private const float BlockMetres = 192f;

    /// <summary>The landblocks of the main landmass, the world graph's landmass 1.</summary>
    public static HashSet<(int X, int Y)> MainLand(string graphRun)
    {
        var land = new HashSet<(int X, int Y)>();
        foreach (var line in File.ReadLines(Path.Combine(graphRun, "results.jsonl")))
        {
            using var row = JsonDocument.Parse(line);
            if (row.RootElement.TryGetProperty("landmass", out var mass) && mass.GetInt32() == 1)
            {
                var id = ushort.Parse(row.RootElement.GetProperty("id").GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                land.Add((id >> 8, id & 0xFF));
            }
        }

        return land;
    }

    /// <summary>A repeatable sample of the main landmass, in id order.</summary>
    public static IReadOnlyList<LandblockCoord> Pick(HashSet<(int X, int Y)> land, int seed, int sample)
    {
        var all = land.OrderBy(c => c.X).ThenBy(c => c.Y).ToArray();
        new Random(seed).Shuffle(all);
        return [.. all.Take(sample).OrderBy(c => c.X).ThenBy(c => c.Y).Select(c => new LandblockCoord((byte)c.X, (byte)c.Y))];
    }

    /// <summary>Walks one start's bands.</summary>
    public static object Run(WalkContext context, OfflineWalkRig rig, AreaIndex areas, HashSet<(int X, int Y)> land, LandblockCoord coord, int seed, NavMap map, WorldRoutes? routes = null)
    {
        var id = Census.IdOf(coord);
        var rng = new Random((seed * 65599) + (coord.X * 256) + coord.Y);
        var tile = context.Tile(coord, out _);
        var walks = new List<object>(Bands.Length);
        var clean = 0;
        foreach (var band in Bands)
        {
            if (Goal(context, areas, land, coord, tile, band, rng, map) is not { } picked)
            {
                walks.Add(new { band, state = "no-goal-in-band" });
                continue;
            }

            var (from, to, target) = picked;
            var line = Vector2.Distance(from.Global, to.Global);
            var walk = rig.Walk(new OfflineWalkRequest($"{id}-long-{band}", NavMap.ToCellFrame(from), NavGoal.ToPoint(to), TimeLimitBase + (line / TimeLimitMetresPerSecond)) { Routes = routes });
            clean += walk.Clean ? 1 : 0;

            // A walk the route check ended: without the check, the body says whether the check was right.
            string? withoutCheck = null;
            double? withoutCheckLeft = null;
            int? withoutCheckRungs = null;
            if (walk.State != NavRunState.Arrived && walk.Reason?.Contains("physics refused", StringComparison.Ordinal) == true)
            {
                var again = rig.Walk(new OfflineWalkRequest($"{id}-long-{band}-nocheck", NavMap.ToCellFrame(from), NavGoal.ToPoint(to), TimeLimitBase + (line / TimeLimitMetresPerSecond)) { Routes = routes, NoRouteCheck = true });
                withoutCheck = again.State?.ToString() ?? "over-time";
                withoutCheckLeft = Math.Round(again.LeftMetres, 1);
                withoutCheckRungs = again.Rungs;
            }

            walks.Add(new
            {
                band,
                without_check = withoutCheck,
                without_check_left_m = withoutCheckLeft,
                without_check_rungs = withoutCheckRungs,
                to = target,
                straight_m = Math.Round(line, 1),
                clean = walk.Clean,
                state = walk.State?.ToString(),
                reason = walk.Reason,
                rungs = walk.Rungs,
                refused = walk.ConfirmFailures,
                why = walk.RefusedWhy,
                replans = walk.Replans,
                route_m = Math.Round(walk.RouteMetres, 1),
                walked_m = Math.Round(walk.WalkedMetres, 1),
                left_m = Math.Round(walk.LeftMetres, 2),
                sim_s = Math.Round(walk.SimSeconds, 1),
                rungs_at = walk.RungsAt,
            });
        }

        return new { id, clean, walks };
    }

    private static (NavPoint From, NavPoint To, string Target)? Goal(WalkContext context, AreaIndex areas, HashSet<(int X, int Y)> land, LandblockCoord coord, NavTile tile, int band, Random rng, NavMap map)
    {
        for (var i = 0; i < TriesPerBand; i++)
        {
            var angle = rng.NextDouble() * Math.Tau;
            var metres = band * (1.0 + (rng.NextDouble() * 0.5));
            var dx = (int)Math.Round(Math.Cos(angle) * metres / BlockMetres);
            var dy = (int)Math.Round(Math.Sin(angle) * metres / BlockMetres);
            if (coord.Offset(dx, dy) is not { } far || !LandDefs.IsOnMap(far) || !land.Contains((far.X, far.Y)) || !areas.Has(far))
            {
                continue;
            }

            var farTile = context.Tile(far, out _);
            map.Attach(tile);
            map.Attach(farTile);
            var pair = areas.SharedNodes(map, coord, tile, far, farTile);
            map.Detach(tile.LandblockId);
            map.Detach(farTile.LandblockId);
            if (pair is { } ends)
            {
                return (WalkContext.PointOf(tile, ends.A.X, ends.A.Y), WalkContext.PointOf(farTile, ends.B.X, ends.B.Y), Census.IdOf(far));
            }
        }

        return null;
    }
}
