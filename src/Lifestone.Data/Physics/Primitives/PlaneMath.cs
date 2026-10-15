// Partly ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Extensions/PlaneExtensions.cs (LocalToGlobal,
// GetSide, compute_time_of_intersection, intersect_box, set_height, snap_to_plane) (AGPL-3.0)
// with changes: which_side takes retail's epsilon as a threshold (ACE adds it to the distance as a bias); intersect_box
// tests the corners in retail's order without allocating a list; set_height in double with a float result, as the
// scenery track's TerrainSampler; snap_to_plane as retail's x87 code writes it; is_equal waits for the step that uses
// it (PH7).

using System.Numerics;
using Lifestone.Data.World;

namespace Lifestone.Data.Physics.Primitives;

/// <summary>Which side of a plane something lies on (retail <c>Sidedness</c>: 0 positive, 1 negative, 2 in the plane, 3 crossing).</summary>
public enum Sidedness
{
    /// <summary>On the side the normal points to.</summary>
    Positive = 0,

    /// <summary>Behind the plane.</summary>
    Negative = 1,

    /// <summary>Within the epsilon of the plane.</summary>
    InPlane = 2,

    /// <summary>A box with corners on both sides (or in the plane).</summary>
    Crossing = 3,
}

/// <summary>Retail <c>Plane</c> members over <see cref="Plane"/> (<c>dot(Normal, p) + D = 0</c>).</summary>
public static class PlaneMath
{
    /// <summary>The signed distance of <paramref name="point"/> from the plane (positive on the normal's side).</summary>
    public static float DistanceTo(this in Plane plane, Vector3 point) => Vector3.Dot(plane.Normal, point) + plane.D;

    /// <summary>
    /// Retail <c>Plane::Plane(normal, point)</c> (11.4186 0x00536A20, 11.6096 0x00537760): the plane through
    /// <paramref name="point"/> with <paramref name="normal"/>.
    /// </summary>
    public static Plane Through(Vector3 normal, Vector3 point) => new(normal, -Vector3.Dot(normal, point));

    /// <summary>
    /// Retail <c>Plane::which_side(point, epsilon)</c> (11.4186 0x00444720, 11.6096 0x00444880):
    /// <see cref="Sidedness.Positive"/> beyond <paramref name="epsilon"/>, <see cref="Sidedness.Negative"/> below
    /// <c>-epsilon</c>, else <see cref="Sidedness.InPlane"/>. ACE's <c>GetSide</c> adds the epsilon to the distance as
    /// a bias instead.
    /// </summary>
    public static Sidedness WhichSide(this in Plane plane, Vector3 point, float epsilon)
    {
        var distance = plane.DistanceTo(point);
        return distance > epsilon ? Sidedness.Positive : distance < -epsilon ? Sidedness.Negative : Sidedness.InPlane;
    }

    /// <summary>
    /// Retail <c>Plane::compute_time_of_intersection</c> (11.4186 0x00538320, 11.6096 0x00539060): the ray parameter where it meets the plane. False when the ray is parallel within
    /// <see cref="PhysicsGlobals.Epsilon"/> or the plane is behind its start (negative time).
    /// </summary>
    public static bool ComputeTimeOfIntersection(this in Plane plane, in Ray ray, out double time)
    {
        time = 0.0;
        double angle = Vector3.Dot(ray.Direction, plane.Normal);
        if (Math.Abs(angle) < PhysicsGlobals.Epsilon)
        {
            return false;
        }

        time = (Vector3.Dot(ray.Point, plane.Normal) + plane.D) * (-1.0 / angle);
        return time >= 0.0;
    }

    /// <summary>
    /// Retail <c>Plane::intersect_box</c> (11.4186 0x005AA170, 11.6096 0x005AB220): the box's side of the plane, or
    /// <see cref="Sidedness.Crossing"/> when a corner is on the other side or within <see cref="PhysicsGlobals.Epsilon"/>
    /// of it. The minimum corner decides the side; the other seven are tested in retail's order.
    /// </summary>
    public static Sidedness IntersectBox(this in Plane plane, in BBox box)
    {
        var first = plane.DistanceTo(box.Min);
        Sidedness side;
        if (first <= PhysicsGlobals.Epsilon)
        {
            if (first >= -PhysicsGlobals.Epsilon)
            {
                return Sidedness.Crossing;
            }

            side = Sidedness.Negative;
        }
        else
        {
            side = Sidedness.Positive;
        }

        var min = box.Min;
        var max = box.Max;
        return plane.WhichSide(new Vector3(max.X, max.Y, max.Z), PhysicsGlobals.Epsilon) == side
            && plane.WhichSide(new Vector3(max.X, min.Y, min.Z), PhysicsGlobals.Epsilon) == side
            && plane.WhichSide(new Vector3(min.X, max.Y, min.Z), PhysicsGlobals.Epsilon) == side
            && plane.WhichSide(new Vector3(min.X, min.Y, max.Z), PhysicsGlobals.Epsilon) == side
            && plane.WhichSide(new Vector3(max.X, max.Y, min.Z), PhysicsGlobals.Epsilon) == side
            && plane.WhichSide(new Vector3(max.X, min.Y, max.Z), PhysicsGlobals.Epsilon) == side
            && plane.WhichSide(new Vector3(min.X, max.Y, max.Z), PhysicsGlobals.Epsilon) == side
                ? side
                : Sidedness.Crossing;
    }

    /// <summary>
    /// Retail <c>Plane::set_height</c> (11.4186 0x0052F050, 11.6096 0x0052FD90): sets <paramref name="point"/>'s Z to the plane's height at
    /// its X and Y, <c>-(y * N.y + x * N.x + d) / N.z</c> in double rounded to float (as
    /// <see cref="Lifestone.Data.World.Scenery.TerrainSampler.TrySetHeight"/>); false, leaving Z alone, when |N.z| is at most
    /// <see cref="PhysicsGlobals.Epsilon"/>.
    /// </summary>
    public static bool SetHeight(this in Plane plane, ref Vector3 point)
    {
        var n = plane.Normal;
        if (Math.Abs(n.Z) <= PhysicsGlobals.Epsilon)
        {
            return false;
        }

        point.Z = (float)-(((((double)point.Y * n.Y) + ((double)point.X * n.X)) + plane.D) / n.Z);
        return true;
    }

    /// <summary>
    /// Retail <c>Plane::snap_to_plane</c> (11.4186 0x00509C50, 11.6096 0x0050A720): gives <paramref name="offset"/> the
    /// height that keeps it in the plane's direction (<c>-(y N.y + x N.x) / N.z</c>, computed as retail's x87 code writes
    /// it, with the plane distance added and taken away again); unchanged when |N.z| is at most
    /// <see cref="PhysicsGlobals.Epsilon"/>.
    /// </summary>
    public static void SnapToPlane(this in Plane plane, ref Vector3 offset)
    {
        var n = plane.Normal;
        if (Math.Abs(n.Z) <= PhysicsGlobals.Epsilon)
        {
            return;
        }

        var inverse = 1.0 / n.Z;
        offset.Z = (float)((-(((double)offset.Y * n.Y) + ((double)offset.X * n.X) + (n.Z * 0.0) + plane.D) * inverse) - (inverse * -plane.D));
    }

    /// <summary>
    /// Retail <c>Plane::localtoglobal(to, from, local)</c> (11.4186 0x005AA0A0, 11.6096 0x005AB150): a plane given in <paramref name="from"/>'s
    /// frame, expressed in <paramref name="to"/>'s landblock space. The normal is rotated by <paramref name="from"/>'s
    /// orientation; the point <c>-d * N</c> is carried across with <see cref="PhysicsPositions.LocalToGlobal"/>.
    /// </summary>
    public static Plane LocalToGlobal(in Plane local, in CellFrame to, in CellFrame from)
    {
        var normal = from.Frame.LocalToGlobalVec(local.Normal);
        var point = to.LocalToGlobal(from, local.Normal * -local.D);
        return new Plane(normal, -Vector3.Dot(normal, point));
    }
}
