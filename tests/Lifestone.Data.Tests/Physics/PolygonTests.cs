using System.Numerics;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// <see cref="PhysPolygon"/> against retail <c>CPolygon</c> (11.4186 PORTAL/cgeometry/CPolygon.c): the plane, the inside
/// tests, the sphere tests with their epsilon edges, and the places where ACE differs.
/// </summary>
public sealed class PolygonTests
{
    /// <summary>A 2 m square floor at z = 1, counter-clockwise from above (normal +Z).</summary>
    private static readonly PhysPolygon Floor = new(7, [new(0, 0, 1), new(2, 0, 1), new(2, 2, 1), new(0, 2, 1)]);

    [Fact]
    public void Make_plane_matches_the_terrain_samplers_retail_plane_bit_for_bit_on_triangles()
    {
        var random = new Random(4186);
        for (var i = 0; i < 2000; i++)
        {
            Vector3 Corner() => new((float)(random.NextDouble() * 192), (float)(random.NextDouble() * 192), (float)(random.NextDouble() * 200));
            var v0 = Corner();
            var v1 = Corner();
            var v2 = Corner();
            var terrain = TerrainSampler.MakePlane(v0, v1, v2);
            var physics = PhysPolygon.MakePlane([v0, v1, v2]);
            Assert.Equal(terrain.Normal, physics.Normal);
            Assert.Equal(terrain.D, physics.D);
        }
    }

    [Fact]
    public void Make_plane_sums_the_fan_of_a_polygon()
    {
        Assert.Equal(new Vector3(0, 0, 1), Floor.Plane.Normal);
        Assert.Equal(-1.0f, Floor.Plane.D);

        // A vertical wall in the plane y = 0, wound so that it faces +Y.
        var wall = new PhysPolygon(0, [new(0, 0, 0), new(0, 0, 3), new(4, 0, 3), new(4, 0, 0)]);
        Assert.Equal(new Vector3(0, 1, 0), wall.Plane.Normal);
        Assert.Equal(0.0f, wall.Plane.D);
    }

    [Fact]
    public void Point_in_polygon_and_in_poly_2D()
    {
        Assert.True(Floor.PointInPolygon(new Vector3(1, 1, 1)));
        Assert.True(Floor.PointInPolygon(new Vector3(2, 1, 1)));     // on an edge
        Assert.False(Floor.PointInPolygon(new Vector3(2.01f, 1, 1)));

        // Retail walks the edges backwards (v0, v[n-1], ..., v1), clockwise for this counter-clockwise floor, so an inside
        // point is on the right of every edge: the Positive test passes it and the Negative test refuses it.
        Assert.True(Floor.PointInPoly2D(new Vector3(1, 1, 0), Sidedness.Positive));
        Assert.False(Floor.PointInPoly2D(new Vector3(3, 1, 0), Sidedness.Positive));
        Assert.False(Floor.PointInPoly2D(new Vector3(1, 1, 0), Sidedness.Negative));
    }

    [Fact]
    public void A_sphere_tangent_within_the_epsilon_does_not_hit_and_one_just_inside_does()
    {
        // The test shrinks the radius by the epsilon, then compares |distance| with it: equal counts as a hit. The floor
        // is at z = 0, so the distance is the centre's z exactly.
        var ground = new PhysPolygon(0, [new(0, 0, 0), new(2, 0, 0), new(2, 2, 0), new(0, 2, 0)]);
        var radius = 0.5f;
        var shrunk = radius - PhysicsGlobals.Epsilon;
        Assert.True(ground.PolygonHitsSphere(new PhysSphere(new Vector3(1, 1, shrunk), radius), out var contact));
        Assert.Equal(new Vector3(1, 1, 0), contact);
        Assert.True(ground.PolygonHitsSphereSlowButSure(new PhysSphere(new Vector3(1, 1, shrunk), radius), out _));
        Assert.False(ground.PolygonHitsSphere(new PhysSphere(new Vector3(1, 1, radius), radius), out _));
        Assert.False(ground.PolygonHitsSphereSlowButSure(new PhysSphere(new Vector3(1, 1, radius), radius), out _));
    }

    [Theory]
    [InlineData(-0.3f, 1.0f, true)]    // beside the west edge, within the projected radius
    [InlineData(-0.6f, 1.0f, false)]   // too far beside it
    [InlineData(-0.2f, -0.2f, true)]   // near the south-west corner
    [InlineData(-0.45f, -0.45f, false)] // diagonal from the corner, beyond the radius
    public void A_sphere_beside_the_polygon_hits_its_edges_and_corners(float x, float y, bool hits)
    {
        var sphere = new PhysSphere(new Vector3(x, y, 1.1f), 0.5f);
        Assert.Equal(hits, Floor.PolygonHitsSphereSlowButSure(sphere, out _));
        Assert.Equal(hits, Floor.HitsSphere(sphere));
    }

    [Theory]
    [InlineData(1.0f, -0.4f, 0f, 1f)]    // south of the floor: the edge (0,0)-(2,0), whose N x edge points north
    [InlineData(2.4f, 1.0f, -1f, 0f)]    // east
    [InlineData(1.0f, 2.4f, 0f, -1f)]    // north
    [InlineData(-0.4f, 1.0f, 1f, 0f)]    // west
    public void Find_crossed_edge_reports_the_edge_the_centre_is_outside_of(float x, float y, float nx, float ny)
    {
        Assert.True(Floor.FindCrossedEdge(new PhysSphere(new Vector3(x, y, 1.5f), 0.5f), Vector3.UnitZ, out var normal));
        Assert.Equal(new Vector3(nx, ny, 0), normal);
        Assert.False(Floor.FindCrossedEdge(new PhysSphere(new Vector3(1, 1, 1.5f), 0.5f), Vector3.UnitZ, out _));
    }

    [Fact]
    public void Check_walkable_keeps_testing_corners_after_an_edge_the_centre_is_outside_of()
    {
        // Centre south-west of the floor: outside the first edge retail walks (west, (0,2)->(0,0)) beyond its segment,
        // and within the radius of the corner (0,0) that a later edge reaches. Retail sets the result false and moves on
        // (CPolygon::check_walkable, 11.4186 0x00538E60); ACE returns false at once (Polygon.cs:165).
        var sphere = new PhysSphere(new Vector3(-0.1f, -0.3f, 1.4f), 0.5f);
        Assert.True(Floor.CheckWalkable(sphere, Vector3.UnitZ));

        // Half the radius (check_small_walkable) no longer reaches the corner.
        Assert.False(Floor.CheckSmallWalkable(sphere, Vector3.UnitZ));
    }

    [Fact]
    public void Check_walkable_accepts_a_polygon_facing_away_from_up()
    {
        // A ceiling (normal -Z): retail only refuses |dot(N, up)| below the epsilon; ACE also refuses a negative dot.
        var ceiling = new PhysPolygon(0, [new(0, 0, 3), new(0, 2, 3), new(2, 2, 3), new(2, 0, 3)]);
        Assert.Equal(new Vector3(0, 0, -1), ceiling.Plane.Normal);
        Assert.True(ceiling.CheckWalkable(new PhysSphere(new Vector3(1, 1, 2.5f), 0.5f), Vector3.UnitZ));

        var wall = new PhysPolygon(0, [new(0, 0, 0), new(0, 0, 3), new(4, 0, 3), new(4, 0, 0)]);
        Assert.False(wall.CheckWalkable(new PhysSphere(new Vector3(1, -0.5f, 1), 0.5f), Vector3.UnitZ));
    }

    [Fact]
    public void Pos_hits_sphere_needs_a_movement_against_the_front()
    {
        var sphere = new PhysSphere(new Vector3(1, 1, 1.2f), 0.5f);
        PhysPolygon? struck = null;
        Assert.True(Floor.PosHitsSphere(sphere, new Vector3(0, 0, -1), out _, ref struck));
        Assert.Same(Floor, struck);

        struck = null;
        Assert.False(Floor.PosHitsSphere(sphere, new Vector3(0, 0, 1), out _, ref struck));
        Assert.Same(Floor, struck);   // the hit is still reported
    }

    [Fact]
    public void A_ray_hits_a_single_sided_polygon_only_from_the_front()
    {
        Assert.True(Floor.PolygonHitsRay(Ray.FromOffset(new Vector3(1, 1, 5), new Vector3(0, 0, -10)), out var time));
        Assert.Equal(4.0, time, 6);
        Assert.False(Floor.PolygonHitsRay(Ray.FromOffset(new Vector3(1, 1, -5), new Vector3(0, 0, 10)), out _));
        Assert.False(Floor.PolygonHitsRay(Ray.FromOffset(new Vector3(3, 1, 5), new Vector3(0, 0, -10)), out _));

        var twoSided = new PhysPolygon(0, Floor.Vertices, sidesType: 1);
        Assert.True(twoSided.PolygonHitsRay(Ray.FromOffset(new Vector3(1, 1, -5), new Vector3(0, 0, 10)), out time));
        Assert.Equal(6.0, time, 6);
    }

    [Theory]
    [InlineData(false, 1.5f, 1.5f)]   // solid check off: to radius above the plane
    [InlineData(true, 1.5f, 1.5f)]    // centre in front, not solid: to radius in front
    [InlineData(true, 0.8f, 0.5f)]    // centre behind (z 0.8 < 1): to radius behind
    public void Adjust_to_placement_poly_pushes_both_spheres_to_the_radius(bool solidCheck, float z, float expectedZ)
    {
        var struck = new PhysSphere(new Vector3(1, 1, z), 0.5f);
        var other = new PhysSphere(new Vector3(1, 1, z + 1), 0.5f);
        Floor.AdjustToPlacementPoly(ref struck, ref other, 0.5f, centerSolid: false, solidCheck);
        Assert.Equal(expectedZ, struck.Center.Z, 5);
        Assert.Equal(expectedZ + 1, other.Center.Z, 5);
    }

    [Fact]
    public void Adjust_to_placement_poly_negates_twice_for_a_solid_centre_behind_the_plane()
    {
        // Retail negates the radius for a solid centre, then again for a centre at or behind the plane, so a solid centre
        // behind ends in front (CPolygon::adjust_to_placement_poly, 11.4186 0x00538CE0). ACE negates once and leaves it
        // behind (Polygon.cs:119-120).
        var struck = new PhysSphere(new Vector3(1, 1, 0.8f), 0.5f);
        var other = struck;
        Floor.AdjustToPlacementPoly(ref struck, ref other, 0.5f, centerSolid: true, solidCheck: true);
        Assert.Equal(1.5f, struck.Center.Z, 5);

        struck = new PhysSphere(new Vector3(1, 1, 1.3f), 0.5f);
        Floor.AdjustToPlacementPoly(ref struck, ref other, 0.5f, centerSolid: true, solidCheck: true);
        Assert.Equal(0.5f, struck.Center.Z, 5);
    }

    [Fact]
    public void Adjust_sphere_to_plane_backs_the_sphere_onto_the_plane_and_shortens_the_step()
    {
        var transition = new Transition();
        transition.SpherePath.WalkInterp = 1.0f;
        var valid = new PhysSphere(new Vector3(1, 1, 1.2f), 0.5f);
        Assert.True(Floor.AdjustSphereToPlane(transition.SpherePath, ref valid, new Vector3(0, 0, -1)));
        Assert.Equal(1.5f, valid.Center.Z, 5);
        Assert.Equal(0.7f, transition.SpherePath.WalkInterp, 5);

        // Moving along the plane: no adjustment.
        Assert.False(Floor.AdjustSphereToPlane(transition.SpherePath, ref valid, new Vector3(1, 0, 0)));
    }

    [Fact]
    public void Walkable_hits_sphere_needs_a_normal_above_the_walkable_allowance()
    {
        var transition = new Transition();
        var sphere = new PhysSphere(new Vector3(1, 1, 1.2f), 0.5f);
        transition.SpherePath.WalkableAllowance = PhysicsGlobals.FloorZ;
        Assert.True(Floor.WalkableHitsSphere(transition.SpherePath, sphere, Vector3.UnitZ));

        transition.SpherePath.WalkableAllowance = 1.0f;
        Assert.False(Floor.WalkableHitsSphere(transition.SpherePath, sphere, Vector3.UnitZ));
    }
}
