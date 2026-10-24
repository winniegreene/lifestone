using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// Lane S2's hand-built <see cref="NavTile"/>s for the planner's tests (P5a): flat tiles, blocked boxes, water, slopes and
/// heights, with a brute-force clearance (a breadth-first Chebyshev distance to the nearest blocked node, capped, the
/// tile's outside counted open) in place of the tile builder's chamfer.
/// </summary>
internal static class S2TileKit
{
    /// <summary>Landblock 0xA9B4 (Holtburg's), the tests' default.</summary>
    public const uint Lb = 0xA9B40000u;

    /// <summary>The landblock (x, y) as 0xXXYY0000.</summary>
    public static uint Block(int x, int y) => ((uint)x << 24) | ((uint)y << 16);

    /// <summary>An open flat tile at height <paramref name="z"/>.</summary>
    public static NavTile Flat(uint landblockId = Lb, float z = 0f)
    {
        var tile = new NavTile(landblockId);
        Array.Fill(tile.Z, z);
        return tile;
    }

    /// <summary>Blocks the nodes x0..x1, y0..y1 (inclusive) as a static.</summary>
    public static NavTile Box(this NavTile tile, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                tile.Block(x, y, NavFlags.Static, 0);
            }
        }

        return tile;
    }

    /// <summary>Flags the nodes x0..x1, y0..y1 with <paramref name="flags"/> (water, slope with <paramref name="slopeCost"/>).</summary>
    public static NavTile Mark(this NavTile tile, int x0, int y0, int x1, int y1, NavFlags flags, byte slopeCost = 0)
    {
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int i = NavTile.Index(x, y);
                tile.Flags[i] |= flags;
                tile.SlopeCost[i] = slopeCost;
            }
        }

        return tile;
    }

    /// <summary>Sets the ground height of the nodes x0..x1, y0..y1.</summary>
    public static NavTile Height(this NavTile tile, int x0, int y0, int x1, int y1, float z)
    {
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                tile.Z[NavTile.Index(x, y)] = z;
            }
        }

        return tile;
    }

    /// <summary>Recomputes every node's clearance by brute force (Chebyshev distance to the nearest blocked node, capped).</summary>
    public static NavTile Clear(this NavTile tile)
    {
        var queue = new Queue<int>();
        for (int i = 0; i < tile.Clearance.Length; i++)
        {
            if ((tile.Flags[i] & NavFlags.Blocked) != 0)
            {
                tile.Clearance[i] = 0;
                queue.Enqueue(i);
            }
            else
            {
                tile.Clearance[i] = NavTile.MaxClearance;
            }
        }

        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % NavTile.Size;
            int y = i / NavTile.Size;
            int next = tile.Clearance[i] + 1;
            if (next >= NavTile.MaxClearance)
            {
                continue;
            }

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!NavTile.InBounds(x + dx, y + dy))
                    {
                        continue;
                    }

                    int j = NavTile.Index(x + dx, y + dy);
                    if (tile.Clearance[j] > next)
                    {
                        tile.Clearance[j] = (byte)next;
                        queue.Enqueue(j);
                    }
                }
            }
        }

        return tile;
    }

    /// <summary>A map of <paramref name="tiles"/> (each cleared first).</summary>
    public static NavMap Map(params NavTile[] tiles)
    {
        var map = new NavMap();
        foreach (var t in tiles)
        {
            map.Attach(t.Clear());
        }

        return map;
    }

    /// <summary>A point in <paramref name="landblockId"/> at local (x, y).</summary>
    public static NavPoint P(float x, float y, uint landblockId = Lb) => NavPoint.At(landblockId, x, y);

    /// <summary>The octile distance between two node centres.</summary>
    public static float Octile(int dx, int dy)
    {
        dx = Math.Abs(dx);
        dy = Math.Abs(dy);
        return Math.Max(dx, dy) - Math.Min(dx, dy) + (MathF.Sqrt(2f) * Math.Min(dx, dy));
    }

    /// <summary>The route's points every <paramref name="step"/> metres of arc length, the end included.</summary>
    public static IEnumerable<NavPoint> Samples(NavRoute route, float step = 0.5f)
    {
        for (float s = 0f; s < route.ArcLength; s += step)
        {
            yield return route.PointAt(s);
        }

        yield return route.End;
    }

    /// <summary>The node under each 0.5 m sample of the route (asserts every sample is on an attached tile).</summary>
    public static IEnumerable<NavNode> SampleNodes(NavMap map, NavRoute route)
    {
        foreach (var p in Samples(route))
        {
            Xunit.Assert.True(map.TryGetNode(p, out var node), $"sample {p} off the attached tiles");
            yield return node;
        }
    }

    /// <summary>The global position of a point.</summary>
    public static Vector2 G(NavPoint p) => p.Global;
}
