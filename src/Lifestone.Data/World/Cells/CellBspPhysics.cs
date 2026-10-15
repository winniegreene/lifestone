// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/BSP/BSPNode.cs (sphere_intersects_cell_bsp,
// box_intersects_cell_bsp) (AGPL-3.0)
// with changes: extension methods over the positive chain CellBsp keeps (the physics track's additive file in World/Cells;
// the cells track's CellBsp is not edited); distances in double, as retail's x87 code keeps them; box corners tested with
// retail's which_side band (ACE's GetSide adds the epsilon as a bias).

using System.Numerics;

namespace Lifestone.Data.World.Cells;

/// <summary>How a sphere lies against a cell's BSP (retail <c>BoundingType</c>: 0 outside, 1 partially, 2 entirely inside).</summary>
public enum CellBspContact
{
    /// <summary>Wholly behind one of the cell's planes.</summary>
    Outside = 0,

    /// <summary>Crossing at least one plane and behind none.</summary>
    Partially = 1,

    /// <summary>In front of every plane by more than the sphere's reach.</summary>
    Entirely = 2,
}

/// <summary>
/// The physics queries retail runs on a cell struct's cell BSP (its containment tree), over the positive chain
/// <see cref="CellBsp.Planes"/> keeps. Every end-of-retail cell BSP's positive chain ends at a node without a positive
/// child (all 3,168 cell structs), never at a leaf, so the chain is the whole test.
/// </summary>
public static class CellBspPhysics
{
    /// <summary>
    /// The extra reach <c>BSPNODE::sphere_intersects_cell_bsp</c> gives the sphere (the double 0.0099999998).
    /// </summary>
    public const double SphereReach = 0.0099999998;

    /// <summary>
    /// Retail <c>BSPNODE::sphere_intersects_cell_bsp</c> (11.4186 0x0053C260, 11.6096 0x0053CFC0), reached through
    /// <c>CCellStruct::sphere_intersects_cell</c> (0x00533900, 0x00534640): a sphere at <paramref name="center"/>
    /// (cell-local) with <paramref name="radius"/>, widened by <see cref="SphereReach"/>, is outside when it is wholly behind
    /// a plane of the chain, entirely inside when it is wholly in front of all of them, and partially inside otherwise.
    /// </summary>
    public static CellBspContact SphereIntersects(this CellBsp bsp, Vector3 center, float radius)
    {
        ArgumentNullException.ThrowIfNull(bsp);
        var reach = (double)(float)(radius + SphereReach);
        var crossing = false;
        var planes = bsp.Planes;
        for (var i = 0; i < planes.Count; i++)
        {
            var distance = Distance(planes[i], center);
            if (distance <= -reach)
            {
                return CellBspContact.Outside;
            }

            if (distance < reach)
            {
                crossing = true;
            }
        }

        return crossing ? CellBspContact.Partially : CellBspContact.Entirely;
    }

    /// <summary>
    /// Retail <c>BSPNODE::box_intersects_cell_bsp</c> (11.4186 0x0053C880, 11.6096 0x0053D5E0), reached through
    /// <c>CCellStruct::box_intersects_cell</c> (0x00533910, 0x00534650): false only when, for some plane of the chain,
    /// the box's minimum corner and its seven others are all behind it beyond <see cref="CellPlane.SideEpsilon"/>.
    /// </summary>
    /// <param name="bsp">The cell BSP.</param>
    /// <param name="min">The box's minimum corner, cell-local.</param>
    /// <param name="max">The box's maximum corner, cell-local.</param>
    public static bool BoxIntersects(this CellBsp bsp, Vector3 min, Vector3 max)
    {
        ArgumentNullException.ThrowIfNull(bsp);
        var planes = bsp.Planes;
        for (var i = 0; i < planes.Count; i++)
        {
            var plane = planes[i];
            if (Distance(plane, min) < -CellPlane.SideEpsilon
                && Behind(plane, new Vector3(max.X, max.Y, max.Z))
                && Behind(plane, new Vector3(min.X, min.Y, max.Z))
                && Behind(plane, new Vector3(min.X, max.Y, min.Z))
                && Behind(plane, new Vector3(max.X, min.Y, min.Z))
                && Behind(plane, new Vector3(max.X, min.Y, max.Z))
                && Behind(plane, new Vector3(min.X, max.Y, max.Z))
                && Behind(plane, new Vector3(max.X, max.Y, min.Z)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The plane distance as retail's x87 code sums it: y, z, x, then d.</summary>
    private static double Distance(in CellPlane plane, Vector3 point) =>
        ((double)plane.Normal.Y * point.Y) + ((double)plane.Normal.Z * point.Z) + ((double)plane.Normal.X * point.X) + plane.D;

    /// <summary>Retail <c>Plane::which_side</c> with the epsilon band answering "negative".</summary>
    private static bool Behind(in CellPlane plane, Vector3 point) => Distance(plane, point) < -CellPlane.SideEpsilon;
}
