using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Bsp;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using static Lifestone.Data.Tests.Physics.PhysicsTestKit;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// The physics BSP queries (retail BSPNODE, BSPLEAF and BSPTREE, 11.4186 PORTAL/cgeometry), the cell BSP's sphere and box
/// tests, and the pool of ten transitions, where retail and ACE differ or the behaviour is easy to state.
/// </summary>
public sealed class BspQueryTests
{
    private static readonly PhysSphere Everything = new(Vector3.Zero, 100f);

    [Fact]
    public void A_placement_sphere_centred_on_a_splitting_plane_searches_the_positive_side_first()
    {
        // BSPNODE::sphere_intersects_solid_poly takes the negative side first only for a centre strictly behind the plane;
        // ACE takes a centre on the plane to the negative side. Across a plane the answer is the centre-solid flag, the
        // polygon hit only being reported (retail and ACE alike).
        var positive = Polygon(0, new(-1, -1, 0.1f), new(1, -1, 0.1f), new(1, 1, 0.1f), new(-1, 1, 0.1f));
        var negative = Polygon(1, new(-1, -1, -0.1f), new(1, -1, -0.1f), new(1, 1, -0.1f), new(-1, 1, -0.1f));
        var node = PhysicsBspNode.Create(
            new Plane(Vector3.UnitZ, 0f),
            Everything,
            PhysicsBspLeaf.Create(0, 0, Everything, [positive]),
            PhysicsBspLeaf.Create(1, 1, Everything, [negative]));

        var centerSolid = false;
        PhysPolygon? hit = null;
        Assert.False(node.SphereIntersectsSolidPoly(new PhysSphere(Vector3.Zero, 0.5f), 0.5f, ref centerSolid, ref hit, true));
        Assert.Same(positive, hit);
        Assert.False(centerSolid);

        // Strictly behind: the negative (solid) side first, which sets the centre-solid flag and reports its polygon.
        hit = null;
        Assert.True(node.SphereIntersectsSolidPoly(new PhysSphere(new Vector3(0, 0, -0.01f), 0.5f), 0.5f, ref centerSolid, ref hit, true));
        Assert.Same(negative, hit);
        Assert.True(centerSolid);
    }

    [Fact]
    public void A_point_on_a_splitting_plane_belongs_to_the_positive_side()
    {
        var node = PhysicsBspNode.Create(
            new Plane(Vector3.UnitZ, 0f),
            Everything,
            PhysicsBspLeaf.Create(0, 1, Everything, [Polygon(0, new(0, 0, 0), new(1, 0, 0), new(0, 1, 0))]),
            PhysicsBspLeaf.Create(1, 0, Everything, []));
        Assert.True(node.PointIntersectsSolid(Vector3.Zero));
        Assert.False(node.PointIntersectsSolid(new Vector3(0, 0, -1e-3f)));
    }

    [Fact]
    public void Placement_pushes_a_sphere_out_of_a_box_and_leaves_a_free_one_alone()
    {
        var shape = BoxShape(0x0100F001, new Vector3(2f, 2f, 2f));
        var at = new CellFrame(LandCellAt(Block, 10f, 10f), new AcFrame(new Vector3(10f, 10f, 0f), Quaternion.Identity));

        // A sphere sunk 0.2 m into the box's east face comes out along +X until it only touches it.
        var transition = PlacementAt(at, new Vector3(1.3f, 0f, 1f));
        Assert.Equal(TransitionState.Adjusted, shape.Physics!.Bsp.PlacementInsert(transition));
        var center = transition.SpherePath.GlobalSphere[0].Center;
        Assert.InRange(center.X - 10f, 1.5f - 1e-4f, 1.5f + 1e-4f);
        Assert.Equal(1f, center.Z, 4);

        transition = PlacementAt(at, new Vector3(3f, 0f, 1f));
        Assert.Equal(TransitionState.OK, shape.Physics.Bsp.PlacementInsert(transition));
    }

    [Fact]
    public void A_path_clipped_sphere_is_moved_back_to_its_first_contact_with_a_polygon()
    {
        // BSPTREE::adjust_to_plane: the contact time with the polygon, then halving until the free and blocked times are
        // within 0.02 of the move; the sphere ends at the free one.
        var wall = Polygon(0, new(1, -5, -5), new(1, -5, 5), new(1, 5, 5), new(1, 5, -5));
        Assert.Equal(-Vector3.UnitX, wall.Plane.Normal);
        var tree = PhysicsBspTree.FromRoot(PhysicsBspLeaf.Create(0, 1, Everything, [wall]));
        var sphere = new PhysSphere(new Vector3(1.2f, 0, 0), 0.5f);
        var contact = Vector3.Zero;
        Assert.True(tree.AdjustToPlane(ref sphere, Vector3.Zero, wall, ref contact));
        Assert.InRange(sphere.Center.X, 0.5f - (0.02f * 1.2f), 0.5f);
        Assert.False(wall.HitsSphere(sphere));
    }

    [Fact]
    public void The_cell_bsp_tells_outside_partly_and_wholly_inside_spheres_and_boxes_apart()
    {
        // The interior of the unit cube: planes facing inwards.
        var cube = CellBsp.FromPlanes(
        [
            new CellPlane(Vector3.UnitX, 0f), new CellPlane(-Vector3.UnitX, 1f),
            new CellPlane(Vector3.UnitY, 0f), new CellPlane(-Vector3.UnitY, 1f),
            new CellPlane(Vector3.UnitZ, 0f), new CellPlane(-Vector3.UnitZ, 1f),
        ]);
        Assert.Equal(CellBspContact.Entirely, cube.SphereIntersects(new Vector3(0.5f), 0.4f));
        Assert.Equal(CellBspContact.Partially, cube.SphereIntersects(new Vector3(0.5f), 0.495f));   // within the 0.01 reach
        Assert.Equal(CellBspContact.Partially, cube.SphereIntersects(new Vector3(1.2f, 0.5f, 0.5f), 0.3f));
        Assert.Equal(CellBspContact.Outside, cube.SphereIntersects(new Vector3(1.4f, 0.5f, 0.5f), 0.3f));

        Assert.True(cube.BoxIntersects(new Vector3(0.9f, 0.2f, 0.2f), new Vector3(1.5f, 0.4f, 0.4f)));
        Assert.False(cube.BoxIntersects(new Vector3(1.1f, 0.2f, 0.2f), new Vector3(1.5f, 0.4f, 0.4f)));
    }

    [Fact]
    public void The_pool_lends_ten_transitions_and_wants_them_back_in_reverse_order()
    {
        var pool = new TransitionPool(new PhysicsWorld(), PhysicsCompat.Retail);
        var lent = new Transition[TransitionPool.Capacity];
        for (var i = 0; i < lent.Length; i++)
        {
            lent[i] = pool.Rent();
        }

        Assert.Equal(TransitionPool.Capacity, pool.InUse);
        Assert.Throws<InvalidOperationException>(() => pool.Rent());
        Assert.Throws<InvalidOperationException>(() => pool.Return(lent[0]));
        for (var i = lent.Length - 1; i >= 0; i--)
        {
            pool.Return(lent[i]);
        }

        Assert.Equal(0, pool.InUse);
        Assert.Same(lent[0], pool.Rent());
    }

    /// <summary>An initial placement of one 0.5 m sphere at <paramref name="local"/> from <paramref name="at"/>, cached in its space.</summary>
    private static Transition PlacementAt(in CellFrame at, Vector3 local)
    {
        var transition = new Transition();
        transition.InitObject(new object(), ObjectInfoState.None, PhysicsState.None, 1f, 0f, 0f);
        transition.InitSphere([new PhysSphere(Vector3.Zero, 0.5f)], 1f);
        var position = at.AddOffset(local);
        transition.InitPath(null, null, position);
        transition.SpherePath.InsertType = InsertType.InitialPlacement;
        transition.SpherePath.SetCheckPos(position, null);
        transition.SpherePath.CacheLocalSpaceSphere(at, 1f);
        return transition;
    }
}
