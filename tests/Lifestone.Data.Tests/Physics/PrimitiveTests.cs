using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.World;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// The small primitives against retail: <see cref="Ray"/> (<c>Ray::InitFromOffset</c>), <see cref="BBox"/>
/// (ENGINE/engine_shared_geometry/BBox.c), <see cref="PlaneMath"/> (<c>Plane</c>) and <see cref="PhysicsPositions"/>
/// (<c>Position</c>).
/// </summary>
public sealed class PrimitiveTests
{
    [Fact]
    public void A_ray_from_an_offset_is_unit_length_along_it()
    {
        var ray = Ray.FromOffset(new Vector3(1, 2, 3), new Vector3(0, 3, 4));
        Assert.Equal(new Vector3(1, 2, 3), ray.Point);
        Assert.Equal(5.0f, ray.Length);
        Assert.Equal(new Vector3(0, 0.6f, 0.8f), ray.Direction);
    }

    [Fact]
    public void A_ray_from_a_tiny_offset_has_no_length_and_points_north()
    {
        // Retail compares the offset with zero and sets dir to (0, 1, 0); ACE compares it with the start point and leaves
        // the point and direction unset (Ray.cs:26-36).
        var ray = Ray.FromOffset(new Vector3(5, 5, 5), new Vector3(0.0001f, 0, -0.0001f));
        Assert.Equal(new Vector3(5, 5, 5), ray.Point);
        Assert.Equal(0.0f, ray.Length);
        Assert.Equal(Vector3.UnitY, ray.Direction);
    }

    [Fact]
    public void The_box_centre_is_the_midpoint()
    {
        // ACE returns the half extent (BBox.cs:78); retail BBox::GetCenter returns (max + min) * 0.5.
        Assert.Equal(new Vector3(2, 3, 4), new BBox(new Vector3(1, 1, 1), new Vector3(3, 5, 7)).GetCenter());
    }

    [Fact]
    public void A_box_grows_by_points_and_by_other_boxes()
    {
        var box = BBox.ForAdjustment;
        box.AdjustBBox(new Vector3(1, -2, 3));
        box.AdjustBBox(new Vector3(-1, 2, 0));
        Assert.Equal(new BBox(new Vector3(-1, -2, 0), new Vector3(1, 2, 3)), box);

        var into = new BBox(Vector3.Zero, Vector3.One);
        box.BuildBoundingBox(ref into);
        Assert.Equal(new BBox(new Vector3(-1, -2, 0), new Vector3(1, 2, 3)), into);
    }

    [Fact]
    public void Local_to_local_bounds_all_eight_corners_of_a_rotated_box()
    {
        var box = new BBox(Vector3.Zero, new Vector3(1, 2, 3));
        var from = new CellFrame(0xA9B40019, new AcFrame(new Vector3(10, 20, 30), RotationTowardsX()));
        var to = new CellFrame(0xA9B40019, AcFrame.Identity);

        var result = BBox.LocalToLocal(box, from, to);

        Assert.Equal(BoundOf(Corners(box).Select(c => from.Frame.LocalToGlobal(c))), result, new BoxComparer(1e-5f));
    }

    [Fact]
    public void Convert_to_global_skips_the_maximum_corner_as_retail_does()
    {
        // A rotation that turns (1, 1, 1) onto +X makes the maximum corner the only one with the largest X. Retail
        // BBox::ConvertToGlobal (11.4186 0x005B23E0) transforms the other seven corners only, so its box misses it.
        var box = new BBox(Vector3.Zero, new Vector3(1, 2, 3));
        var frame = new AcFrame(new Vector3(5, 0, 0), RotationTowardsX());

        var result = box.ConvertToGlobal(frame);

        var all = BoundOf(Corners(box).Select(frame.LocalToGlobal));
        var withoutMax = BoundOf(Corners(box).Where(c => c != box.Max).Select(frame.LocalToGlobal));
        Assert.Equal(withoutMax, result, new BoxComparer(1e-5f));
        Assert.True(result.Max.X < all.Max.X - 0.1f, $"{result} should miss the maximum corner of {all}");
    }

    [Theory]
    [InlineData(0.5f, Sidedness.Positive)]
    [InlineData(0.0001f, Sidedness.InPlane)]
    [InlineData(-0.0001f, Sidedness.InPlane)]
    [InlineData(-0.5f, Sidedness.Negative)]
    public void Which_side_uses_the_epsilon_as_a_band(float z, Sidedness expected)
    {
        Assert.Equal(expected, new Plane(Vector3.UnitZ, 0).WhichSide(new Vector3(0, 0, z), PhysicsGlobals.Epsilon));
    }

    [Fact]
    public void A_box_is_on_one_side_of_a_plane_or_crossing_it()
    {
        var plane = new Plane(Vector3.UnitZ, -1);   // z = 1
        Assert.Equal(Sidedness.Positive, plane.IntersectBox(new BBox(new Vector3(0, 0, 2), new Vector3(1, 1, 3))));
        Assert.Equal(Sidedness.Negative, plane.IntersectBox(new BBox(new Vector3(0, 0, -3), new Vector3(1, 1, 0))));
        Assert.Equal(Sidedness.Crossing, plane.IntersectBox(new BBox(new Vector3(0, 0, 0), new Vector3(1, 1, 3))));

        // A corner within the epsilon of the plane makes the box crossing.
        Assert.Equal(Sidedness.Crossing, plane.IntersectBox(new BBox(new Vector3(0, 0, 0), new Vector3(1, 1, 1.0001f))));
        Assert.Equal(Sidedness.Crossing, plane.IntersectBox(new BBox(new Vector3(0, 0, 1), new Vector3(1, 1, 2))));
    }

    [Fact]
    public void Time_of_intersection_with_a_plane()
    {
        var plane = new Plane(Vector3.UnitZ, -1);
        Assert.True(plane.ComputeTimeOfIntersection(new Ray(new Vector3(0, 0, 5), -Vector3.UnitZ, 10), out var time));
        Assert.Equal(4.0, time, 6);

        // Behind the start: false, with the negative time.
        Assert.False(plane.ComputeTimeOfIntersection(new Ray(new Vector3(0, 0, 5), Vector3.UnitZ, 10), out time));
        Assert.Equal(-4.0, time, 6);

        // Parallel within the epsilon.
        Assert.False(plane.ComputeTimeOfIntersection(new Ray(new Vector3(0, 0, 5), Vector3.UnitX, 10), out _));
    }

    [Fact]
    public void Set_height_puts_a_point_on_the_plane()
    {
        var slope = PhysPolygon.MakePlane([new(0, 0, 0), new(4, 0, 2), new(4, 4, 2), new(0, 4, 0)]);
        var point = new Vector3(2, 1, 99);
        Assert.True(slope.SetHeight(ref point));
        Assert.Equal(1.0f, point.Z, 5);

        var wall = new Plane(Vector3.UnitX, 0);
        point = new Vector3(0, 1, 99);
        Assert.False(wall.SetHeight(ref point));
        Assert.Equal(99f, point.Z);
    }

    [Fact]
    public void A_plane_is_carried_into_another_landblocks_space()
    {
        // A floor at z = 1 in a frame turned 90 degrees about Z, one landblock east of the target position.
        var local = new Plane(Vector3.UnitZ, -1);
        var from = new CellFrame(0xAAB40001, new AcFrame(new Vector3(10, 10, 5), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)));
        var to = new CellFrame(0xA9B40001, AcFrame.Identity);

        var global = PlaneMath.LocalToGlobal(local, to, from);

        AssertVector(Vector3.UnitZ, global.Normal);
        Assert.Equal(-6.0f, global.D, 4);   // z = 5 + 1
        Assert.Equal(0.0f, global.DistanceTo(new Vector3(192 + 10, 10, 6)), 4);
    }

    [Fact]
    public void Positions_carry_points_and_offsets_across_landblocks()
    {
        var west = new CellFrame(0xA9B40038, new AcFrame(new Vector3(180, 100, 10), Quaternion.Identity));
        var east = new CellFrame(0xAAB40001, new AcFrame(new Vector3(4, 100, 12), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI)));

        Assert.Equal(new Vector3(16, 0, 2), west.GetOffset(east));
        Assert.Equal(new Vector3(-16, 0, -2), east.GetOffset(west));
        Assert.Equal(MathF.Sqrt(260), west.Distance(east), 5);

        // A point one metre in front of the east position (its frame faces south) seen from the west one.
        AssertVector(new Vector3(196, 99, 12), west.LocalToGlobal(east, Vector3.UnitY));
        AssertVector(new Vector3(16, -1, 2), west.LocalToLocal(east, Vector3.UnitY));
        AssertVector(Vector3.UnitY, east.LocalToLocal(east, Vector3.UnitY));
    }

    private static Quaternion RotationTowardsX()
    {
        var diagonal = Vector3.Normalize(Vector3.One);
        var axis = Vector3.Normalize(Vector3.Cross(diagonal, Vector3.UnitX));
        return Quaternion.CreateFromAxisAngle(axis, MathF.Acos(Vector3.Dot(diagonal, Vector3.UnitX)));
    }

    private static IEnumerable<Vector3> Corners(BBox box)
    {
        foreach (var x in new[] { box.Min.X, box.Max.X })
        {
            foreach (var y in new[] { box.Min.Y, box.Max.Y })
            {
                foreach (var z in new[] { box.Min.Z, box.Max.Z })
                {
                    yield return new Vector3(x, y, z);
                }
            }
        }
    }

    private static BBox BoundOf(IEnumerable<Vector3> points)
    {
        var box = BBox.ForAdjustment;
        foreach (var point in points)
        {
            box.AdjustBBox(point);
        }

        return box;
    }

    private static void AssertVector(Vector3 expected, Vector3 actual, float tolerance = 1e-5f)
    {
        Assert.True(Vector3.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");
    }

    private sealed class BoxComparer(float tolerance) : IEqualityComparer<BBox>
    {
        public bool Equals(BBox a, BBox b) => Vector3.Distance(a.Min, b.Min) <= tolerance && Vector3.Distance(a.Max, b.Max) <= tolerance;

        public int GetHashCode(BBox box) => 0;
    }
}
