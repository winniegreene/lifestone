using System.Numerics;

namespace Lifestone.Data.World.Cells;

/// <summary>A plane <c>dot(Normal, p) + D = 0</c>; positive distances are on the side the normal points to.</summary>
public readonly record struct CellPlane(Vector3 Normal, float D)
{
    /// <summary>
    /// Retail's sidedness epsilon <c>F_EPSILON</c> (0.00019999999 in acclient.c), used by <c>PView::InitCell</c>,
    /// <c>PView::ConstructView</c> and <c>BSPNODE::point_inside_cell_bsp</c>: a point within it is "on" the plane.
    /// </summary>
    public const float SideEpsilon = 0.0002f;

    public float Distance(Vector3 point) => Vector3.Dot(Normal, point) + D;

    /// <summary>Which side of the plane a point lies on, with retail's epsilon.</summary>
    public PlaneSide SideOf(Vector3 point)
    {
        var d = Distance(point);
        return d > SideEpsilon ? PlaneSide.Positive : d < -SideEpsilon ? PlaneSide.Negative : PlaneSide.OnPlane;
    }

    /// <summary>
    /// The plane of a polygon as retail computes it (<c>CPolygon::make_plane</c>): the normal is the normalised sum of
    /// the fan cross products <c>(v[i] - v0) x (v[i+1] - v0)</c> (counter-clockwise front, right-handed), and D is the
    /// negated mean of <c>dot(normal, v)</c> over all vertices.
    /// </summary>
    /// <exception cref="InvalidDataException">The polygon has fewer than three corners or zero area.</exception>
    public static CellPlane FromPolygon(ReadOnlySpan<Vector3> vertices) =>
        TryFromPolygon(vertices, out var plane)
            ? plane
            : throw new InvalidDataException($"Degenerate polygon ({vertices.Length} corners, zero area): no plane.");

    /// <summary><see cref="FromPolygon"/> that returns false for degenerate polygons instead of throwing.</summary>
    public static bool TryFromPolygon(ReadOnlySpan<Vector3> vertices, out CellPlane plane)
    {
        plane = default;
        if (vertices.Length < 3)
        {
            return false;
        }

        var v0 = vertices[0];
        var sum = Vector3.Zero;
        for (var i = 1; i + 1 < vertices.Length; i++)
        {
            sum += Vector3.Cross(vertices[i] - v0, vertices[i + 1] - v0);
        }

        var length = sum.Length();
        if (length <= 0f || !float.IsFinite(length))
        {
            return false;
        }

        var normal = sum / length;
        var dot = 0f;
        foreach (var v in vertices)
        {
            dot += Vector3.Dot(normal, v);
        }

        plane = new CellPlane(normal, -dot / vertices.Length);
        return true;
    }

    /// <summary>The plane mapped into the parent space of <paramref name="frame"/>.</summary>
    public CellPlane ToParent(Placement frame)
    {
        var n = frame.RotateToParent(Normal);
        // A point p on the local plane maps to frame.LocalToParent(p); D' = -dot(n', p').
        var pointOnPlane = Normal * -D;
        return new CellPlane(n, -Vector3.Dot(n, frame.LocalToParent(pointOnPlane)));
    }
}

/// <summary>Retail <c>Sidedness</c> as used by the portal code: 0 positive, 1 negative, 2 on the plane.</summary>
public enum PlaneSide
{
    Positive = 0,
    Negative = 1,
    OnPlane = 2,
}
