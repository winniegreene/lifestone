using System.Numerics;

namespace Lifestone.Client.Play;

/// <summary>One drawn part of one drawn object, placed as it is drawn.</summary>
/// <param name="Guid">The object.</param>
/// <param name="PartIndex">The part's index in the object's Setup.</param>
/// <param name="GfxObjId">The GfxObj drawn for the part (the level of detail in use); the shapes source resolves it.</param>
/// <param name="Origin">Where the part's frame is, in the ray's space.</param>
/// <param name="Orientation">The part frame's rotation from the GfxObj's local axes to the ray's space.</param>
/// <param name="Scale">The part's scale (<c>gfxobj_scale</c>), per local axis.</param>
public readonly record struct PickPart(uint Guid, int PartIndex, uint GfxObjId, Vector3 Origin, Quaternion Orientation, Vector3 Scale);

/// <summary>A pick's result.</summary>
/// <param name="Guid">The object hit.</param>
/// <param name="PartIndex">The part of it the ray hit.</param>
/// <param name="Distance">The distance along the ray (in the ray's units) to the hit.</param>
/// <param name="Polygon">The hit is a polygon's (it beat every sphere hit); otherwise it is the nearest part sphere's.</param>
public readonly record struct PickHit(uint Guid, int PartIndex, float Distance, bool Polygon);

/// <summary>
/// Picking as retail does it (M2 G4 g5a, docs/avatars.md §18): the click ray is tested against every drawn part of every drawn object. A part
/// is hit when the ray enters its GfxObj drawing sphere; for a click the part's polygons are then tested, and a polygon hit beats every sphere
/// hit. Within each class the nearest wins.
/// </summary>
/// <remarks>
/// <para>Re-expressed from <c>Render::GfxObjUnderSelectionRay</c> (0x0054D350), with <c>CSphere::sphere_intersects_ray</c> (0x005384E0),
/// <c>CPolygon::polygon_hits_ray</c> (0x0053A320) and the result readers <c>Render::GetMouseSelectionObjectID</c> / <c>GetMouseSelectionPartIndex</c>
/// (0x0054D560 / 0x0054D590: a polygon hit if there is one, else the sphere hit); all addresses 11.6096. The per-part rule: the sphere test runs
/// first; a part whose sphere is entered beyond an already found polygon hit is skipped; the nearest sphere hit so far is kept; and for a click
/// the polygons are tested and the nearest polygon hit so far is kept.</para>
/// <para><b>Where this differs from retail, on purpose.</b> The eye inside a part's sphere is a miss, as in retail (the origin test of
/// <c>sphere_intersects_ray</c>). A sphere wholly behind the eye is also a miss (retail would report it with a negative time, but the view cone
/// culls such a part before the test). A part's polygons are searched for the nearest hit (retail keeps the first polygon in the part's order
/// that the ray hits). Distances are in the ray's own units, so parts of different scale compare directly. The ray is 10000 long, retail's
/// value: a hit further than that is ignored. Both of those two change which object a click picks, so they are glitch fixes with the flag the
/// fidelity rule asks for: <c>--retail-picking</c> (<c>retailRules</c> here) restores retail's first polygon of a part and its comparison of
/// hit times in each part's own scaled local-ray units (docs/avatars.md §18).</para>
/// <para>The caller leaves out what cannot be picked: hidden and undrawn objects, a part with translucency 1. The player is left out here
/// (<c>excludeGuid</c>). No allocation: the ray is moved into each part's frame on the stack.</para>
/// </remarks>
public static class RayPicking
{
    /// <summary>Retail's ray length: <c>Render::GfxObjUnderSelectionRay</c> builds its local ray with length 10000.</summary>
    public const float RayLength = 10000f;

    private const float Epsilon = 0.0002f; // retail's tolerance in sphere_intersects_ray and compute_time_of_intersection.

    /// <summary>The object under the ray, or null.</summary>
    /// <param name="ray">The click ray (unit direction), in the same space as the parts.</param>
    /// <param name="parts">The drawn parts, in draw order.</param>
    /// <param name="shapes">The shape of a GfxObj id, or null when it has none (that part cannot be hit).</param>
    /// <param name="excludeGuid">An object that is never picked (the player, 0 for none).</param>
    /// <param name="polygons">True for a click (the polygons are tested), false for a hover search (retail's "not polygon-accurate" flag: spheres only).</param>
    /// <param name="retailRules">
    /// True for retail's two rules that can pick the wrong object (<c>--retail-picking</c>): a part's first hit polygon in its own order (not its nearest),
    /// and hit times in the part's own local units, where the ray's direction is unit length in the part's scaled frame (not world units). Then
    /// <see cref="PickHit.Distance"/> is in those local units. Default false: the nearest polygon, world units.
    /// </param>
    public static PickHit? Pick(in PickRay ray, ReadOnlySpan<PickPart> parts, Func<uint, PickShape?> shapes, uint excludeGuid = 0, bool polygons = true, bool retailRules = false)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        PickHit? sphereBest = null;
        PickHit? polygonBest = null;
        foreach (var part in parts)
        {
            if (part.Guid == 0 || part.Guid == excludeGuid || part.GfxObjId == 0 || shapes(part.GfxObjId) is not { } shape)
            {
                continue;
            }

            // The ray in the part's local frame: the same parameter t is the distance along the world ray, so no direction is renormalised.
            var inverse = Quaternion.Conjugate(part.Orientation);
            var inverseScale = new Vector3(1f / part.Scale.X, 1f / part.Scale.Y, 1f / part.Scale.Z);
            var origin = Vector3.Transform(ray.Origin - part.Origin, inverse) * inverseScale;
            var direction = Vector3.Transform(ray.Direction, inverse) * inverseScale;

            if (!SphereEntry(origin, direction, shape.Centre, shape.Radius, out var sphereTime) || sphereTime > RayLength)
            {
                continue;
            }

            // Retail compares the times of its per-part local ray, whose direction is unit length in the part's scaled frame: t (world units) times the local direction's length.
            var unit = retailRules ? direction.Length() : 1f;
            sphereTime *= unit;

            if (polygonBest is { } found && sphereTime > found.Distance)
            {
                continue;
            }

            if (sphereBest is null || sphereTime < sphereBest.Value.Distance)
            {
                sphereBest = new PickHit(part.Guid, part.PartIndex, sphereTime, false);
            }

            if (polygons && FindTriangle(origin, direction, shape, retailRules, out var polygonTime) && polygonTime <= RayLength && (polygonBest is null || polygonTime * unit < polygonBest.Value.Distance))
            {
                polygonBest = new PickHit(part.Guid, part.PartIndex, polygonTime * unit, true);
            }
        }

        return polygonBest ?? sphereBest;
    }

    /// <summary>
    /// Where a ray (any direction length) first enters a sphere ahead of its origin. False when the origin is inside or on the sphere, the sphere is
    /// behind it, or the ray misses (<c>CSphere::sphere_intersects_ray</c>). <paramref name="time"/> is in units of the direction.
    /// </summary>
    public static bool SphereEntry(Vector3 origin, Vector3 direction, Vector3 centre, float radius, out float time)
    {
        time = 0f;
        var toOrigin = origin - centre;
        var a = Vector3.Dot(direction, direction);
        var halfB = Vector3.Dot(toOrigin, direction);
        var c = Vector3.Dot(toOrigin, toOrigin) - (radius * radius);
        if (c <= 0f || a < Epsilon || halfB >= 0f)
        {
            return false;
        }

        var discriminant = (halfB * halfB) - (a * c);
        if (discriminant < 0f)
        {
            return false;
        }

        time = (-halfB - MathF.Sqrt(discriminant)) / a;
        return true;
    }

    /// <summary>
    /// The nearest front-facing triangle of the shape the ray hits (<c>CPolygon::polygon_hits_ray</c>: the ray must run against the face's normal), or with
    /// <paramref name="first"/> the first one in the shape's order (retail's loop stops at the first polygon that reports a hit).
    /// </summary>
    private static bool FindTriangle(Vector3 origin, Vector3 direction, PickShape shape, bool first, out float nearest)
    {
        nearest = float.MaxValue;
        var found = false;
        var subMeshes = shape.SubMeshes;
        for (var s = 0; s < subMeshes.Count; s++)
        {
            var positions = subMeshes[s].Positions;
            var indices = subMeshes[s].Indices;
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                if (TriangleTime(origin, direction, positions[indices[i]], positions[indices[i + 1]], positions[indices[i + 2]], out var time) && time < nearest)
                {
                    nearest = time;
                    found = true;
                    if (first)
                    {
                        return true;
                    }
                }
            }
        }

        return found;
    }

    /// <summary>Moller-Trumbore with the back faces culled: the ray must run against the counter-clockwise face's normal.</summary>
    private static bool TriangleTime(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float time)
    {
        time = 0f;
        var edge1 = b - a;
        var edge2 = c - a;
        var p = Vector3.Cross(direction, edge2);
        var determinant = Vector3.Dot(edge1, p); // = -(direction . normal): positive when the ray runs against the normal
        if (determinant <= Epsilon * Epsilon)
        {
            return false;
        }

        var toOrigin = origin - a;
        var u = Vector3.Dot(toOrigin, p);
        if (u < 0f || u > determinant)
        {
            return false;
        }

        var q = Vector3.Cross(toOrigin, edge1);
        var v = Vector3.Dot(direction, q);
        if (v < 0f || u + v > determinant)
        {
            return false;
        }

        var t = Vector3.Dot(edge2, q) / determinant;
        if (t < 0f)
        {
            return false;
        }

        time = t;
        return true;
    }
}
