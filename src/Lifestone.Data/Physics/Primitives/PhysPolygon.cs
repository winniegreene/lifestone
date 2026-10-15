// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Polygon.cs (AGPL-3.0)
// with changes: immutable, with the vertex positions copied in; checked function by function against the 11.4186
// decompile. make_plane follows retail's x87 rounding (the scenery track's TerrainSampler.MakePlane, generalised to the
// triangle fan). Retail fixes (docs/third-party/physics.md, "ACE deviations"): check_walkable accepts a polygon facing
// away from the up vector and keeps testing the remaining corners after an edge the centre is outside of (ACE stops);
// adjust_to_placement_poly negates the radius once for a solid centre and once more for a centre behind the plane (ACE
// negates once); polygon_hits_sphere_slow_but_sure tests the plane distance before it accepts an empty polygon.

using System.Numerics;
using Lifestone.Data.Physics.Transitions;

namespace Lifestone.Data.Physics.Primitives;

/// <summary>
/// A collision polygon (retail <c>CPolygon</c>): its corners, its plane and how it is sided, with the sphere tests the
/// physics BSPs run on it.
/// </summary>
/// <remarks>
/// <para>Retail functions (11.4186 PORTAL/cgeometry/CPolygon.c; re-expressed, nothing copied) are cited on each member
/// with their 11.4186 and 11.6096 addresses. Edges run from the previous corner to the current one, starting with
/// (last, first), as retail walks them. Arithmetic as <see cref="PhysSphere"/>, except <see cref="MakePlane"/>.</para>
/// <para>Immutable and thread-safe after construction.</para>
/// </remarks>
public sealed class PhysPolygon
{
    private readonly Vector3[] _vertices;

    /// <summary>A polygon with the given corners; its plane is computed as retail's <c>make_plane</c> does.</summary>
    /// <param name="polyId">The polygon's id (its position in its owner's polygon array).</param>
    /// <param name="vertices">The corners in stored order (copied).</param>
    /// <param name="sidesType">Retail <c>sides_type</c>: 0 single-sided, 1 both sides, 2 separate negative side.</param>
    /// <param name="stippling">Retail <c>stippling</c>.</param>
    /// <param name="posSurface">The positive side's surface index.</param>
    /// <param name="negSurface">The negative side's surface index.</param>
    public PhysPolygon(int polyId, ReadOnlySpan<Vector3> vertices, int sidesType = 0, byte stippling = 0, short posSurface = -1, short negSurface = -1)
    {
        PolyId = polyId;
        _vertices = vertices.ToArray();
        SidesType = sidesType;
        Stippling = stippling;
        PosSurface = posSurface;
        NegSurface = negSurface;
        Plane = MakePlane(_vertices);
    }

    /// <summary>The polygon's id (its position in its owner's polygon array).</summary>
    public int PolyId { get; }

    /// <summary>The corners in stored order.</summary>
    public ReadOnlySpan<Vector3> Vertices => _vertices;

    /// <summary>The number of corners (retail <c>num_pts</c>).</summary>
    public int NumPoints => _vertices.Length;

    /// <summary>The plane (retail <c>make_plane</c>, run on every polygon as it is unpacked); not finite for fewer than three corners.</summary>
    public Plane Plane { get; }

    /// <summary>Retail <c>sides_type</c>: 0 single-sided, 1 both sides, 2 separate negative side.</summary>
    public int SidesType { get; }

    /// <summary>Retail <c>stippling</c>.</summary>
    public byte Stippling { get; }

    /// <summary>The positive side's surface index.</summary>
    public short PosSurface { get; }

    /// <summary>The negative side's surface index.</summary>
    public short NegSurface { get; }

    /// <summary>
    /// Retail <c>CPolygon::make_plane</c> (11.4186 0x005383D0, 11.6096 0x00539110): the normal is the normalised sum of
    /// the triangle fan's cross products <c>(v[i] - v0) x (v[i+1] - v0)</c>, and d is the negated mean of
    /// <c>dot(N, v)</c> over the corners.
    /// </summary>
    /// <remarks>
    /// The x87 code keeps some intermediates in double registers and stores others to 4-byte slots; this rounds at the
    /// same places, as the scenery track established for triangles from the 11.6096 machine code
    /// (<see cref="Lifestone.Data.World.Scenery.TerrainSampler"/>'s MakePlane, which this equals bit for bit on a triangle): per fan step the
    /// x and y of <c>v[i] - v0</c> and the x and y of the cross product are floats, the z of the cross product joins a
    /// float running sum while x and y sum in doubles; the reciprocal length scales x as a double and y, z as a float.
    /// A degenerate polygon (zero area, or fewer than three corners) gets a non-finite plane, as in retail, which divides by
    /// the zero length.
    /// </remarks>
    public static Plane MakePlane(ReadOnlySpan<Vector3> vertices)
    {
        double sumX = 0.0;
        double sumY = 0.0;
        var sumZ = 0.0f;
        for (var i = 1; i + 1 < vertices.Length; i++)
        {
            var v0 = vertices[0];
            var a = vertices[i];
            var b = vertices[i + 1];
            double ax = (float)(a.X - (double)v0.X);
            double ay = (float)(a.Y - (double)v0.Y);
            var az = a.Z - (double)v0.Z;
            var bx = b.X - (double)v0.X;
            var by = b.Y - (double)v0.Y;
            var bz = b.Z - (double)v0.Z;
            sumX += (float)((ay * bz) - (az * by));
            sumY += (float)((az * bx) - (bz * ax));
            sumZ = (float)(((ax * by) - (ay * bx)) + sumZ);
        }

        var reciprocal = 1.0 / Math.Sqrt(((double)sumZ * sumZ) + (sumY * sumY) + (sumX * sumX));
        double reciprocalAsFloat = (float)reciprocal;
        var normal = new Vector3((float)(reciprocal * sumX), (float)(reciprocalAsFloat * sumY), (float)(reciprocalAsFloat * sumZ));
        var total = 0.0;
        foreach (var v in vertices)
        {
            total += ((v.Z * (double)normal.Z) + (v.Y * (double)normal.Y)) + (v.X * (double)normal.X);
        }

        return new Plane(normal, (float)-(total / vertices.Length));
    }

    /// <summary>
    /// Retail <c>CPolygon::point_in_poly2D</c> (11.4186 0x00538060, 11.6096 0x00538DA0): whether
    /// <paramref name="point"/>'s X and Y lie inside the polygon's projection, walking the edges backwards from the
    /// first corner. For <see cref="Sidedness.Positive"/> no edge may have the point on its left; for any other side, on
    /// its right.
    /// </summary>
    public bool PointInPoly2D(Vector3 point, Sidedness side)
    {
        if (_vertices.Length == 0)
        {
            return true;
        }

        var previous = _vertices[0];
        for (var i = _vertices.Length - 1; i >= 0; i--)
        {
            var vertex = _vertices[i];
            var dy = previous.Y - vertex.Y;
            var dx = vertex.X - previous.X;
            var cross = (-(dy * vertex.X) - (dx * vertex.Y)) + (dx * point.Y) + (dy * point.X);
            if (side != Sidedness.Positive ? cross < 0.0f : cross > 0.0f)
            {
                return false;
            }

            previous = vertex;
        }

        return true;
    }

    /// <summary>
    /// Retail <c>CPolygon::point_in_polygon</c> (11.4186 0x00538D90, 11.6096 0x00539AD0): whether a point in the plane is
    /// inside every edge (on the inner side of <c>N x edge</c>).
    /// </summary>
    public bool PointInPolygon(Vector3 point)
    {
        if (_vertices.Length == 0)
        {
            return true;
        }

        var normal = Plane.Normal;
        var last = _vertices[^1];
        foreach (var vertex in _vertices)
        {
            if (Vector3.Dot(point - last, Vector3.Cross(normal, vertex - last)) < 0.0f)
            {
                return false;
            }

            last = vertex;
        }

        return true;
    }

    /// <summary>
    /// Retail <c>CPolygon::polygon_hits_ray</c> (11.4186 0x005395E0, 11.6096 0x0053A320): whether the ray meets the
    /// polygon, with its parameter. A single-sided polygon (sides type 0) is only hit from its front.
    /// </summary>
    public bool PolygonHitsRay(in Ray ray, out double time)
    {
        time = 0.0;
        if (SidesType == 0 && Vector3.Dot(ray.Direction, Plane.Normal) > 0.0f)
        {
            return false;
        }

        var plane = Plane;
        return plane.ComputeTimeOfIntersection(ray, out time)
            && PointInPolygon(ray.Point + (ray.Direction * (float)time));
    }

    /// <summary>
    /// Retail <c>CPolygon::polygon_hits_sphere</c> (11.4186 0x005387C0, 11.6096 0x00539500): whether the sphere (shrunk
    /// by <see cref="PhysicsGlobals.Epsilon"/>) touches the polygon, with the sphere centre's projection on the plane.
    /// The quick version: one pass over the edges.
    /// </summary>
    public bool PolygonHitsSphere(in PhysSphere sphere, out Vector3 contactPoint)
    {
        contactPoint = default;
        var plane = Plane;
        var distance = plane.DistanceTo(sphere.Center);
        var radius = sphere.Radius - PhysicsGlobals.Epsilon;
        if (radius < MathF.Abs(distance))
        {
            return false;
        }

        var radiusSquared = (radius * radius) - (distance * distance);
        contactPoint = sphere.Center - (plane.Normal * distance);
        var result = true;
        if (_vertices.Length == 0)
        {
            return result;
        }

        var last = _vertices[^1];
        foreach (var vertex in _vertices)
        {
            var edge = vertex - last;
            var displacement = contactPoint - last;
            var cross = Vector3.Cross(plane.Normal, edge);
            var side = Vector3.Dot(displacement, cross);
            if (side < 0.0f)
            {
                // Outside this edge: a hit only when the edge itself is within the projected radius. The decompile
                // loses the comparison's operands; this is ACE's reading, the edge's distance against the radius.
                if (cross.LengthSquared() * radiusSquared < side * side)
                {
                    return false;
                }

                var along = Vector3.Dot(displacement, edge);
                if (along >= 0.0f && along <= edge.LengthSquared())
                {
                    return true;
                }

                result = false;
            }

            if (displacement.LengthSquared() <= radiusSquared)
            {
                return true;
            }

            last = vertex;
        }

        return result;
    }

    /// <summary>
    /// Retail <c>CPolygon::polygon_hits_sphere_slow_but_sure</c> (11.4186 0x00538A10, 11.6096 0x00539750; ACE's
    /// <c>polygon_hits_sphere_precise</c>): as <see cref="PolygonHitsSphere"/>, but a centre inside every edge is a hit
    /// at once, and only otherwise are the edges and corners tested.
    /// </summary>
    public bool PolygonHitsSphereSlowButSure(in PhysSphere sphere, out Vector3 contactPoint)
    {
        contactPoint = default;
        var plane = Plane;
        var distance = plane.DistanceTo(sphere.Center);
        var radius = sphere.Radius - PhysicsGlobals.Epsilon;
        if (!(radius >= MathF.Abs(distance)))
        {
            return false;
        }

        var radiusSquared = (radius * radius) - (distance * distance);
        contactPoint = sphere.Center - (plane.Normal * distance);
        if (PointInPolygon(contactPoint))
        {
            return true;
        }

        var last = _vertices[^1];
        foreach (var vertex in _vertices)
        {
            var edge = vertex - last;
            var displacement = contactPoint - last;
            var cross = Vector3.Cross(plane.Normal, edge);
            var side = Vector3.Dot(displacement, cross);
            if (side < 0.0f)
            {
                if (cross.LengthSquared() * radiusSquared < side * side)
                {
                    return false;
                }

                var along = Vector3.Dot(displacement, edge);
                if (along >= 0.0f && along <= edge.LengthSquared())
                {
                    return true;
                }
            }

            if (displacement.LengthSquared() <= radiusSquared)
            {
                return true;
            }

            last = vertex;
        }

        return false;
    }

    /// <summary>Retail <c>CPolygon::hits_sphere</c> (11.4186 0x00539540, 11.6096 0x0053A280): <see cref="PolygonHitsSphereSlowButSure"/>.</summary>
    public bool HitsSphere(in PhysSphere sphere) => PolygonHitsSphereSlowButSure(sphere, out _);

    /// <summary>
    /// Retail <c>CPolygon::pos_hits_sphere</c> (11.4186 0x005394F0, 11.6096 0x0053A230): whether the sphere hits the
    /// polygon while moving against its front (<c>dot(N, movement) &lt; 0</c>). A hit is reported in
    /// <paramref name="struckPolygon"/> even when the movement is not against the front.
    /// </summary>
    public bool PosHitsSphere(in PhysSphere sphere, Vector3 movement, out Vector3 contactPoint, ref PhysPolygon? struckPolygon)
    {
        var hit = PolygonHitsSphereSlowButSure(sphere, out contactPoint);
        if (hit)
        {
            struckPolygon = this;
        }

        return Vector3.Dot(movement, Plane.Normal) < 0.0f && hit;
    }

    /// <summary>
    /// Retail <c>CPolygon::check_walkable</c> (11.4186 0x00538E60, 11.6096 0x00539BA0): whether a sphere standing on the
    /// polygon's plane (its centre projected along <paramref name="up"/>) is over the polygon, within its radius of it.
    /// </summary>
    public bool CheckWalkable(in PhysSphere sphere, Vector3 up) => CheckWalkable(sphere, up, sphere.Radius * sphere.Radius);

    /// <summary>
    /// Retail <c>CPolygon::check_small_walkable</c> (11.4186 0x005390B0, 11.6096 0x00539DF0): <see cref="CheckWalkable(in PhysSphere, Vector3)"/>
    /// with half the radius.
    /// </summary>
    public bool CheckSmallWalkable(in PhysSphere sphere, Vector3 up) => CheckWalkable(sphere, up, sphere.Radius * sphere.Radius * 0.25f);

    /// <summary>
    /// Retail <c>CPolygon::find_crossed_edge</c> (11.4186 0x00539300, 11.6096 0x0053A040): the first edge (in retail's
    /// order) that the sphere's centre, projected onto the plane along <paramref name="up"/>, is outside of, as the unit
    /// vector <c>N x edge</c> (which points into a counter-clockwise polygon; the caller orients it).
    /// </summary>
    public bool FindCrossedEdge(in PhysSphere sphere, Vector3 up, out Vector3 normal)
    {
        normal = default;
        var plane = Plane;
        var angleUp = Vector3.Dot(plane.Normal, up);
        if (MathF.Abs(angleUp) < PhysicsGlobals.Epsilon)
        {
            return false;
        }

        var center = sphere.Center - (up * (plane.DistanceTo(sphere.Center) / angleUp));
        if (_vertices.Length == 0)
        {
            return false;
        }

        var last = _vertices[^1];
        foreach (var vertex in _vertices)
        {
            var cross = Vector3.Cross(plane.Normal, vertex - last);
            if (Vector3.Dot(center - last, cross) < 0.0f)
            {
                normal = cross * (1.0f / cross.Length());
                return true;
            }

            last = vertex;
        }

        return false;
    }

    /// <summary>
    /// Retail <c>CPolygon::adjust_sphere_to_poly</c> (11.4186 0x00538170, 11.6096 0x00538EB0): the fraction of
    /// <paramref name="movement"/> (from <paramref name="currPos"/>) at which the sphere touches the plane, on the side it
    /// starts on: 1 when it already touches at the start, 0 when the movement is parallel.
    /// </summary>
    /// <remarks>
    /// The decompile keeps only the flags (less or equal) of the test that picks the sign of the contact distance, not
    /// its operands. It is read as "the start distance is not positive", so the contact is at <c>+radius</c> for a sphere
    /// starting in front of the plane and <c>-radius</c> behind it, the only reading under which
    /// <c>BSPTREE::adjust_to_plane</c> clips a moving sphere to its first contact. (Reading it as "the movement points along
    /// the normal" gives the same time for every sphere that approaches the plane, the only case
    /// <c>adjust_to_plane</c> meets, since it follows a front-face hit.) ACE's reading, negative when the move is no
    /// longer than the radius, sends the camera's viewer sphere, which moves exactly one radius per step
    /// (<c>CTransition::calc_num_steps</c>), through every wall it meets. TODO(physics): confirm the operands from the
    /// 11.6096 machine code at 0x00538EB0 (docs/physics.md §4).
    /// </remarks>
    public double AdjustSphereToPoly(in PhysSphere checkPos, Vector3 currPos, Vector3 movement)
    {
        var plane = Plane;
        var startDistance = plane.DistanceTo(currPos);
        if (!(checkPos.Radius <= MathF.Abs(startDistance)))
        {
            return 1.0;
        }

        double toward = Vector3.Dot(plane.Normal, movement);
        if (Math.Abs(toward) < PhysicsGlobals.Epsilon)
        {
            return 0.0;
        }

        var contact = checkPos.Radius;
        if (startDistance <= 0.0f)
        {
            contact = -contact;
        }

        return (contact - startDistance) / toward;
    }

    /// <summary>
    /// Retail <c>CPolygon::adjust_to_placement_poly</c> (11.4186 0x00538CE0, 11.6096 0x00539A20): pushes
    /// <paramref name="struckSphere"/> (and <paramref name="otherSphere"/> by the same amount) out along the normal to
    /// <paramref name="radius"/> from the plane. With <paramref name="solidCheck"/> the distance is negated for a solid
    /// centre and again for a centre at or behind the plane.
    /// </summary>
    public void AdjustToPlacementPoly(ref PhysSphere struckSphere, ref PhysSphere otherSphere, float radius, bool centerSolid, bool solidCheck)
    {
        var plane = Plane;
        var distance = plane.DistanceTo(struckSphere.Center);
        float shift;
        if (solidCheck)
        {
            var target = centerSolid ? -radius : radius;
            if (distance <= 0.0f)
            {
                target = -target;
            }

            shift = target - distance;
        }
        else
        {
            shift = radius - distance;
        }

        var adjusted = plane.Normal * shift;
        struckSphere.Center += adjusted;
        otherSphere.Center += adjusted;
    }

    /// <summary>
    /// Retail <c>CPolygon::adjust_sphere_to_plane</c> (11.4186 0x00538210, 11.6096 0x00538F50): moves
    /// <paramref name="validPos"/> back along <paramref name="movement"/> until it rests on the plane, shortening the
    /// path's walk interpolation; false when the movement is parallel or the result would fall outside the step.
    /// </summary>
    internal bool AdjustSphereToPlane(SpherePath path, ref PhysSphere validPos, Vector3 movement)
    {
        var plane = Plane;
        var startDistance = plane.DistanceTo(validPos.Center);
        var toward = Vector3.Dot(plane.Normal, movement);
        float distance;
        if (toward <= PhysicsGlobals.Epsilon)
        {
            if (toward >= -PhysicsGlobals.Epsilon)
            {
                return false;
            }

            distance = startDistance - validPos.Radius;
        }
        else
        {
            distance = -validPos.Radius - startDistance;
        }

        var fraction = distance / toward;
        var interp = (1.0f - fraction) * path.WalkInterp;
        if (interp >= path.WalkInterp || interp < -0.5f)
        {
            return false;
        }

        validPos.Center -= movement * fraction;
        path.WalkInterp = interp;
        return true;
    }

    /// <summary>
    /// Retail <c>CPolygon::walkable_hits_sphere</c> (11.4186 0x00539560, 11.6096 0x0053A2A0): whether a walkable polygon
    /// (its normal's dot with <paramref name="up"/> above the path's walkable allowance) is hit by the sphere. Retail
    /// also runs the quick test and repeats both when they disagree, for a debugger; the result is the slow test's.
    /// </summary>
    internal bool WalkableHitsSphere(SpherePath path, in PhysSphere sphere, Vector3 up) =>
        Vector3.Dot(Plane.Normal, up) > path.WalkableAllowance && PolygonHitsSphereSlowButSure(sphere, out _);

    /// <inheritdoc/>
    public override string ToString() => $"Polygon {PolyId} ({NumPoints} corners) {Plane}";

    private bool CheckWalkable(in PhysSphere sphere, Vector3 up, float insideSquared)
    {
        var plane = Plane;
        var angleUp = Vector3.Dot(plane.Normal, up);
        if (MathF.Abs(angleUp) < PhysicsGlobals.Epsilon)
        {
            return false;
        }

        var center = sphere.Center - (up * (plane.DistanceTo(sphere.Center) / angleUp));
        var result = true;
        if (_vertices.Length == 0)
        {
            return result;
        }

        var last = _vertices[^1];
        foreach (var vertex in _vertices)
        {
            var edge = vertex - last;
            var displacement = center - last;
            var cross = Vector3.Cross(plane.Normal, edge);
            var side = Vector3.Dot(displacement, cross);
            if (side < 0.0f)
            {
                if (cross.LengthSquared() * insideSquared < side * side)
                {
                    return false;
                }

                var along = Vector3.Dot(displacement, edge);
                if (along >= 0.0f && along <= edge.LengthSquared())
                {
                    return true;
                }

                result = false;
            }

            if (displacement.LengthSquared() <= insideSquared)
            {
                return true;
            }

            last = vertex;
        }

        return result;
    }
}
