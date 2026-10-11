using System.Numerics;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Interiors;

/// <summary>Geometric queries on one EnvCell, in landblock-local AC coordinates.</summary>
public static class CellQueries
{
    private const float Epsilon = 1e-4f;

    /// <summary>
    /// True when a vertical line through <paramref name="landblockPoint"/> meets the cell's drawing polygons both below
    /// and above the point: the point is under the room's ceiling and over its floor.
    /// </summary>
    /// <remarks>
    /// Not a retail test. Retail's <c>CEnvCell::point_in_cell</c> (the cell BSP's positive chain,
    /// <see cref="LandblockCellSet.Contains"/>) describes a convex region that can reach outside a non-convex room, which
    /// does not matter to retail because walls stop the player. Lifestone's free camera passes through walls, so before it
    /// counts a camera found by a global point search (not by crossing a portal) as inside a cell, it also asks for this
    /// (docs/interiors.md §3.2).
    /// </remarks>
    public static bool IsEnclosed(LandblockCellSet cells, EnvCellData cell, Vector3 landblockPoint)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(cell);
        var structure = cells.GetStructure(cell);
        var origin = cell.Frame.ParentToLocal(landblockPoint);
        var up = Vector3.Transform(Vector3.UnitZ, Quaternion.Conjugate(cell.Frame.Orientation));
        var below = false;
        var above = false;
        Span<Vector3> corners = stackalloc Vector3[64];
        foreach (var polygon in structure.Polygons)
        {
            var n = polygon.VertexIndices.Length;
            if (n < 3 || n > corners.Length)
            {
                continue;
            }

            for (var i = 0; i < n; i++)
            {
                corners[i] = structure.Positions[polygon.VertexIndices[i]];
            }

            if (RayHitsPolygon(origin, up, corners[..n], out var t))
            {
                if (t > 0f)
                {
                    above = true;
                }
                else
                {
                    below = true;
                }

                if (above && below)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The highest walkable floor of the cell (its physics floor polygons, <see cref="CellStructure.Floors"/>) under the
    /// point whose height is at most <paramref name="maxZ"/>, as a landblock-space Z; null when there is none.
    /// </summary>
    public static float? FloorBelow(LandblockCellSet cells, EnvCellData cell, Vector3 landblockPoint, float maxZ)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(cell);
        var structure = cells.GetStructure(cell);
        var origin = cell.Frame.ParentToLocal(landblockPoint);
        var up = Vector3.Transform(Vector3.UnitZ, Quaternion.Conjugate(cell.Frame.Orientation));
        float? best = null;
        foreach (var floor in structure.Floors)
        {
            if (!RayHitsPolygon(origin, up, floor.Vertices, out var t))
            {
                continue;
            }

            var z = landblockPoint.Z + t;
            if (z <= maxZ + Epsilon && (best is null || z > best.Value))
            {
                best = z;
            }
        }

        return best;
    }

    /// <summary>
    /// The line <c>origin + t·direction</c> (any t) against a convex polygon: true with the parameter of the hit when the
    /// line crosses the polygon's plane inside the polygon (edges included).
    /// </summary>
    public static bool RayHitsPolygon(Vector3 origin, Vector3 direction, ReadOnlySpan<Vector3> polygon, out float t)
    {
        t = 0f;
        if (!CellPlane.TryFromPolygon(polygon, out var plane))
        {
            return false;
        }

        var denominator = Vector3.Dot(plane.Normal, direction);
        if (MathF.Abs(denominator) < 1e-6f)
        {
            return false;
        }

        t = -plane.Distance(origin) / denominator;
        var hit = origin + (direction * t);
        return InsideConvex(polygon, plane.Normal, hit);
    }

    /// <summary>
    /// True when the segment from <paramref name="from"/> to <paramref name="to"/> passes through the convex polygon
    /// (crosses its plane at a point inside it).
    /// </summary>
    public static bool SegmentCrossesPolygon(Vector3 from, Vector3 to, ReadOnlySpan<Vector3> polygon)
    {
        if (!CellPlane.TryFromPolygon(polygon, out var plane))
        {
            return false;
        }

        var a = plane.Distance(from);
        var b = plane.Distance(to);
        if ((a > 0f && b > 0f) || (a < 0f && b < 0f) || a == b)
        {
            return false;
        }

        var hit = from + ((to - from) * (a / (a - b)));
        return InsideConvex(polygon, plane.Normal, hit);
    }

    private static bool InsideConvex(ReadOnlySpan<Vector3> polygon, Vector3 normal, Vector3 point)
    {
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            if (Vector3.Dot(Vector3.Cross(b - a, point - a), normal) < -Epsilon * Vector3.Distance(a, b))
            {
                return false;
            }
        }

        return true;
    }
}
