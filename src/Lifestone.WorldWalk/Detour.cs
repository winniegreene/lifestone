using System.Numerics;
using System.Runtime.Versioning;
using System.Text.Json;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.WorldWalk;

/// <summary>
/// What a walk's "no path" needed (W4's largest cause). The world graph says the two ends join; the planner, seeing only the tiles round the body, said no. This widens the attached window one ring of
/// landblocks at a time and plans again, so each answer says how far out the true way runs. It reads no physics: it measures the planner's window, and the walks judge the routes.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Detour
{
    /// <summary>The widest window tried, in landblocks round the start. Not retail: the measurement's own.</summary>
    public const int MaxRing = 12;

    /// <summary>The edges of a walks run with a no-path walk, by landblock.</summary>
    public static Dictionary<string, List<(string To, string Way, int[] Ends)>> ReadEdges(string walksRun)
    {
        var found = new Dictionary<string, List<(string To, string Way, int[] Ends)>>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(walksRun, "results.jsonl")))
        {
            using var row = JsonDocument.Parse(line);
            if (!row.RootElement.TryGetProperty("edges", out var edges))
            {
                continue;
            }

            foreach (var edge in edges.EnumerateArray())
            {
                if (!edge.TryGetProperty("bad", out var bad))
                {
                    continue;
                }

                var ways = bad.EnumerateArray().Where(IsNoPath).Select(b => b.GetProperty("way").GetString()!).Distinct(StringComparer.Ordinal).ToArray();
                if (ways.Length == 0)
                {
                    continue;
                }

                var id = row.RootElement.GetProperty("id").GetString()!;
                if (!found.TryGetValue(id, out var list))
                {
                    found[id] = list = [];
                }

                int[] ends = [.. edge.GetProperty("ends").EnumerateArray().Select(e => e.GetInt32())];
                list.AddRange(ways.Select(way => (edge.GetProperty("to").GetString()!, way, ends)));
            }
        }

        return found;
    }

    /// <summary>Measures one landblock's no-path edges.</summary>
    public static object Run(WalkContext context, LandblockCoord coord, IReadOnlyList<(string To, string Way, int[] Ends)> edges, NavMap map)
    {
        var id = Census.IdOf(coord);
        var answers = new List<object>(edges.Count);
        foreach (var (to, way, ends) in edges)
        {
            var next = to == "east" ? coord.Offset(1, 0) : coord.Offset(0, 1);
            var tile = context.Tile(coord, out _);
            var nextTile = context.Tile(next!.Value, out _);
            var here = WalkContext.PointOf(tile, ends[0], ends[1]);
            var there = WalkContext.PointOf(nextTile, ends[2], ends[3]);
            var (from, goal) = way == "out" ? (here, there) : (there, here);
            var (centre, other) = way == "out" ? (coord, next.Value) : (next.Value, coord);
            answers.Add(Measure(context, map, centre, other, from, goal, to, way, Vector2.Distance(here.Global, there.Global)));
        }

        return new { id, edges = answers };
    }

    private static object Measure(WalkContext context, NavMap map, LandblockCoord coord, LandblockCoord next, NavPoint from, NavPoint goal, string to, string way, float straight)
    {
        var attached = new List<uint>();
        var seen = new HashSet<(int, int)>();
        try
        {
            void Take(LandblockCoord c)
            {
                if (seen.Add((c.X, c.Y)))
                {
                    var tile = context.Tile(c, out _);
                    map.Attach(tile);
                    attached.Add(tile.LandblockId);
                }
            }

            Take(coord);
            Take(next);
            var planner = new PathPlanner(map);
            for (var ring = 0; ring <= MaxRing; ring++)
            {
                for (var dx = -ring; dx <= ring; dx++)
                {
                    for (var dy = -ring; dy <= ring; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == ring && coord.Offset(dx, dy) is { } near && LandDefs.IsOnMap(near))
                        {
                            Take(near);
                        }
                    }
                }

                var plan = planner.Plan(from, NavGoal.ToPoint(goal));
                if (plan.Status == NavStatus.Found && plan.Route is { } route)
                {
                    return new { to, way, verdict = "found", ring, straight_m = Math.Round(straight, 1), route_m = Math.Round(route.ArcLength, 1) };
                }
            }

            return new { to, way, verdict = "beyond", ring = MaxRing, straight_m = Math.Round(straight, 1), route_m = 0.0 };
        }
        finally
        {
            foreach (var tile in attached)
            {
                map.Detach(tile);
            }
        }
    }

    private static bool IsNoPath(JsonElement bad) =>
        bad.GetProperty("reason").ValueKind == JsonValueKind.String && bad.GetProperty("reason").GetString()!.Contains("no path: no-path", StringComparison.Ordinal);
}
