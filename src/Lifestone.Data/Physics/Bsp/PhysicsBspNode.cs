// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/BSP/BSPNode.cs and BSPLeaf.cs (the node structure, its
// construction from the dat and the physics queries) (AGPL-3.0)
// with changes: built from DatReaderWriter's PhysicsBSPNode instead of ACE.DatLoader; leaf polygons resolved by array
// position, as retail BSPLEAF::UnPackLeaf does (ACE looks them up by stored id); immutable; spheres passed by reference
// (structs); retail fixes (docs/third-party/physics.md): sphere_intersects_solid_poly sends a sphere centred on the
// splitting plane to the positive child first and point_intersects_solid a point on the plane to the positive child
// (ACE: the negative child for both); both children are called without null checks (the census rule, docs/physics.md §5).

using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.Transitions;
using DatPhysicsBspNode = DatReaderWriter.Types.PhysicsBSPNode;

namespace Lifestone.Data.Physics.Bsp;

/// <summary>
/// An internal node of a physics BSP (retail <c>BSPNODE</c> unpacked with <c>pack_tree_type</c> 1): a splitting plane, a
/// bounding sphere and the children its type says it has.
/// </summary>
/// <remarks>
/// <para><b>Children.</b> Retail <c>BSPNODE::UnPack</c> (11.4186 0x0053C540, 11.6096 0x0053D2A0) reads a positive
/// child for BPnn, BPIn, BPIN and BPnN, a negative child for BpIN, BpnN, BPIN and BPnN, and none for BpIn; a child it
/// does not read stays null, and its physics queries call both children without checking. The census over every
/// end-of-retail physics BSP (docs/physics.md §5) finds only BPnN internal nodes and leaves, so a physics node always
/// has both children. The decoder enforces that: any other internal type is refused loudly, never guessed at, and
/// <see cref="PosNode"/> and <see cref="NegNode"/> are never null on an internal node.</para>
/// <para>Immutable; shared by every object and cell that uses the same GfxObj or cell struct.</para>
/// </remarks>
public class PhysicsBspNode
{
    private protected PhysicsBspNode(BSPNodeType type, Plane splittingPlane, PhysSphere boundingSphere, PhysicsBspNode? posNode, PhysicsBspNode? negNode)
    {
        Type = type;
        SplittingPlane = splittingPlane;
        BoundingSphere = boundingSphere;
        PosNode = posNode;
        NegNode = negNode;
    }

    /// <summary>The node's type tag (<c>BPIN</c>, <c>BPnN</c>, ..., or <c>LEAF</c> for a <see cref="PhysicsBspLeaf"/>).</summary>
    public BSPNodeType Type { get; }

    /// <summary>The splitting plane; default for a leaf.</summary>
    public Plane SplittingPlane { get; }

    /// <summary>The sphere bounding the node's contents.</summary>
    public PhysSphere BoundingSphere { get; }

    /// <summary>The child on the plane's positive side; null only on a leaf.</summary>
    public PhysicsBspNode? PosNode { get; }

    /// <summary>The child on the plane's negative side; null only on a leaf.</summary>
    public PhysicsBspNode? NegNode { get; }

    /// <summary>True for a <see cref="PhysicsBspLeaf"/>.</summary>
    public bool IsLeaf => Type == BSPNodeType.Leaf;

    /// <summary>True when the type declares a positive child (BPnn, BPIn, BPIN, BPnN).</summary>
    public static bool HasPosChild(BSPNodeType type) =>
        type is BSPNodeType.BPnn or BSPNodeType.BPIn or BSPNodeType.BPIN or BSPNodeType.BPnN;

    /// <summary>True when the type declares a negative child (BpIN, BpnN, BPIN, BPnN).</summary>
    public static bool HasNegChild(BSPNodeType type) =>
        type is BSPNodeType.BpIN or BSPNodeType.BpnN or BSPNodeType.BPIN or BSPNodeType.BPnN;

    /// <inheritdoc/>
    public override string ToString() => $"{TypeName(Type)} {SplittingPlane}";

    /// <summary>The four-letter tag of a node type, as stored (e.g. <c>BPIN</c>).</summary>
    public static string TypeName(BSPNodeType type)
    {
        var value = (uint)type;
        return string.Create(4, value, static (span, v) =>
        {
            span[0] = (char)((v >> 24) & 0xFF);
            span[1] = (char)((v >> 16) & 0xFF);
            span[2] = (char)((v >> 8) & 0xFF);
            span[3] = (char)(v & 0xFF);
        });
    }

    /// <summary>
    /// Retail <c>BSPNODE::sphere_intersects_poly</c> (11.4186 0x0053CA30, 11.6096 0x0053D790): whether the sphere, moving
    /// by <paramref name="movement"/>, hits the front of a polygon in this subtree (<see cref="PhysPolygon.PosHitsSphere"/>).
    /// A polygon touched from behind is still reported in <paramref name="polygon"/>.
    /// </summary>
    internal virtual bool SphereIntersectsPoly(in PhysSphere checkPos, Vector3 movement, ref PhysPolygon? polygon, ref Vector3 contactPoint)
    {
        if (!BoundingSphere.Intersects(checkPos))
        {
            return false;
        }

        var distance = SplittingPlane.DistanceTo(checkPos.Center);
        var reach = checkPos.Radius - PhysicsGlobals.Epsilon;
        if (distance >= reach)
        {
            return PosNode!.SphereIntersectsPoly(checkPos, movement, ref polygon, ref contactPoint);
        }

        if (distance <= -reach)
        {
            return NegNode!.SphereIntersectsPoly(checkPos, movement, ref polygon, ref contactPoint);
        }

        return PosNode!.SphereIntersectsPoly(checkPos, movement, ref polygon, ref contactPoint)
            || NegNode!.SphereIntersectsPoly(checkPos, movement, ref polygon, ref contactPoint);
    }

    /// <summary>
    /// Retail <c>BSPNODE::sphere_intersects_solid</c> (11.4186 0x0053CAF0, 11.6096 0x0053D850): whether the sphere touches a
    /// polygon of this subtree or, with <paramref name="centerCheck"/>, has its centre in a solid leaf. Across the plane,
    /// only the child on the centre's side keeps the centre check.
    /// </summary>
    internal virtual bool SphereIntersectsSolid(in PhysSphere checkPos, bool centerCheck)
    {
        if (!BoundingSphere.Intersects(checkPos))
        {
            return false;
        }

        var distance = SplittingPlane.DistanceTo(checkPos.Center);
        var reach = checkPos.Radius - PhysicsGlobals.Epsilon;
        if (distance >= reach)
        {
            return PosNode!.SphereIntersectsSolid(checkPos, centerCheck);
        }

        if (distance <= -reach)
        {
            return NegNode!.SphereIntersectsSolid(checkPos, centerCheck);
        }

        return distance < 0.0f
            ? PosNode!.SphereIntersectsSolid(checkPos, false) || NegNode!.SphereIntersectsSolid(checkPos, centerCheck)
            : PosNode!.SphereIntersectsSolid(checkPos, centerCheck) || NegNode!.SphereIntersectsSolid(checkPos, false);
    }

    /// <summary>
    /// Retail <c>BSPNODE::hits_walkable</c> (11.4186 0x0053CBD0, 11.6096 0x0053D930): whether a walkable polygon of this
    /// subtree (its normal above the path's allowance along <paramref name="up"/>) is touched by the sphere and lies under
    /// its half-radius footprint.
    /// </summary>
    internal virtual bool HitsWalkable(SpherePath path, in PhysSphere validPos, Vector3 up)
    {
        if (!BoundingSphere.Intersects(validPos))
        {
            return false;
        }

        var distance = SplittingPlane.DistanceTo(validPos.Center);
        var reach = validPos.Radius - PhysicsGlobals.Epsilon;
        if (distance >= reach)
        {
            return PosNode!.HitsWalkable(path, validPos, up);
        }

        if (distance <= -reach)
        {
            return NegNode!.HitsWalkable(path, validPos, up);
        }

        return PosNode!.HitsWalkable(path, validPos, up) || NegNode!.HitsWalkable(path, validPos, up);
    }

    /// <summary>
    /// Retail <c>BSPNODE::find_walkable</c> (11.4186 0x0053CC80, 11.6096 0x0053D9E0): moves <paramref name="validPos"/> back
    /// along <paramref name="movement"/> onto every walkable polygon of this subtree it touches
    /// (<see cref="PhysPolygon.AdjustSphereToPlane"/>), recording the last one in <paramref name="polygon"/>.
    /// </summary>
    internal virtual void FindWalkable(SpherePath path, ref PhysSphere validPos, ref PhysPolygon? polygon, Vector3 movement, Vector3 up, ref bool changed)
    {
        if (!BoundingSphere.Intersects(validPos))
        {
            return;
        }

        var distance = SplittingPlane.DistanceTo(validPos.Center);
        var reach = validPos.Radius - PhysicsGlobals.Epsilon;
        if (distance >= reach)
        {
            PosNode!.FindWalkable(path, ref validPos, ref polygon, movement, up, ref changed);
            return;
        }

        if (distance <= -reach)
        {
            NegNode!.FindWalkable(path, ref validPos, ref polygon, movement, up, ref changed);
            return;
        }

        PosNode!.FindWalkable(path, ref validPos, ref polygon, movement, up, ref changed);
        NegNode!.FindWalkable(path, ref validPos, ref polygon, movement, up, ref changed);
    }

    /// <summary>
    /// Retail <c>BSPNODE::sphere_intersects_solid_poly</c> (11.4186 0x0053CD50, 11.6096 0x0053DAB0): the placement test. A
    /// sphere of <paramref name="radius"/> touching a polygon of this subtree reports it in <paramref name="hitPoly"/>; with
    /// <paramref name="centerCheck"/>, a centre in a solid leaf sets <paramref name="centerSolid"/>. Across the plane the
    /// centre's side is searched first, the negative side only for a centre strictly behind the plane (ACE also takes a
    /// centre on the plane to the negative side).
    /// </summary>
    internal virtual bool SphereIntersectsSolidPoly(in PhysSphere checkPos, float radius, ref bool centerSolid, ref PhysPolygon? hitPoly, bool centerCheck)
    {
        if (!BoundingSphere.Intersects(checkPos))
        {
            return false;
        }

        var distance = SplittingPlane.DistanceTo(checkPos.Center);
        var reach = radius - PhysicsGlobals.Epsilon;
        if (distance >= reach)
        {
            return PosNode!.SphereIntersectsSolidPoly(checkPos, radius, ref centerSolid, ref hitPoly, centerCheck);
        }

        if (distance <= -reach)
        {
            return NegNode!.SphereIntersectsSolidPoly(checkPos, radius, ref centerSolid, ref hitPoly, centerCheck);
        }

        if (distance < 0.0f)
        {
            NegNode!.SphereIntersectsSolidPoly(checkPos, radius, ref centerSolid, ref hitPoly, centerCheck);
            if (hitPoly is null)
            {
                return PosNode!.SphereIntersectsSolidPoly(checkPos, radius, ref centerSolid, ref hitPoly, false);
            }
        }
        else
        {
            PosNode!.SphereIntersectsSolidPoly(checkPos, radius, ref centerSolid, ref hitPoly, centerCheck);
            if (hitPoly is null)
            {
                return NegNode!.SphereIntersectsSolidPoly(checkPos, radius, ref centerSolid, ref hitPoly, false);
            }
        }

        return centerSolid;
    }

    /// <summary>
    /// Retail <c>BSPNODE::point_intersects_solid</c> (11.4186 0x0053C310, 11.6096 0x0053D070): whether the point lies in a
    /// leaf with polygons. A point strictly behind the plane goes to the negative child, any other to the positive one
    /// (ACE sends a point on the plane to the negative child).
    /// </summary>
    internal virtual bool PointIntersectsSolid(Vector3 point) =>
        SplittingPlane.DistanceTo(point) < 0.0f ? NegNode!.PointIntersectsSolid(point) : PosNode!.PointIntersectsSolid(point);

    /// <summary>An internal BPnN node from parts (synthetic trees in tests; real ones come from the dat).</summary>
    internal static PhysicsBspNode Create(Plane splittingPlane, PhysSphere boundingSphere, PhysicsBspNode posNode, PhysicsBspNode negNode) =>
        new(BSPNodeType.BPnN, splittingPlane, boundingSphere, posNode, negNode);

    /// <summary>
    /// Builds a node and its subtree from DatReaderWriter's decode, resolving leaf polygon indices as positions in
    /// <paramref name="polygons"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">A PORT node, an internal node other than BPnN (a node with a missing child,
    /// which no end-of-retail physics BSP has), or a leaf polygon index out of range.</exception>
    internal static PhysicsBspNode FromDat(DatPhysicsBspNode node, IReadOnlyList<PhysPolygon> polygons, string owner)
    {
        var sphere = new PhysSphere(node.BoundingSphere.Origin, node.BoundingSphere.Radius);
        if (node.Type == BSPNodeType.Leaf)
        {
            var leafPolygons = new PhysPolygon[node.Polygons.Count];
            for (var i = 0; i < leafPolygons.Length; i++)
            {
                // BSPLEAF::UnPackLeaf indexes pack_poly with the stored u16 as a signed short.
                var index = (short)node.Polygons[i];
                if (index < 0 || index >= polygons.Count)
                {
                    throw new InvalidDataException($"{owner}: physics BSP leaf {node.LeafIndex} names polygon {index} of {polygons.Count}.");
                }

                leafPolygons[i] = polygons[index];
            }

            return new PhysicsBspLeaf(node.LeafIndex, node.Solid, sphere, leafPolygons);
        }

        if (node.Type != BSPNodeType.BPnN || node.PosNode is null || node.NegNode is null)
        {
            // Retail's physics queries would call a missing child (BSPNODE::sphere_intersects_poly and its siblings do not
            // check), and no end-of-retail physics BSP has one, so there is no retail behaviour to follow: refuse the tree.
            throw new InvalidDataException(
                $"TODO(physics): {owner}: physics BSP node {TypeName(node.Type)} (0x{(uint)node.Type:X8}) is not BPnN with two "
                + "children; the end-of-retail census has none, and retail would dereference the missing child (docs/physics.md §5).");
        }

        return new PhysicsBspNode(node.Type, node.SplittingPlane, sphere, FromDat(node.PosNode, polygons, owner), FromDat(node.NegNode, polygons, owner));
    }
}

/// <summary>
/// A leaf of a physics BSP (retail <c>BSPLEAF</c>, unpacked by <c>BSPLEAF::UnPackLeaf</c>, 11.4186 0x0053D4A0, 11.6096 0x0053E200): its
/// index, its solid flag, its bounding sphere and the polygons in it.
/// </summary>
public sealed class PhysicsBspLeaf : PhysicsBspNode
{
    private readonly PhysPolygon[] _polygons;

    internal PhysicsBspLeaf(int leafIndex, int solid, PhysSphere boundingSphere, PhysPolygon[] polygons)
        : base(BSPNodeType.Leaf, default, boundingSphere, null, null)
    {
        LeafIndex = leafIndex;
        Solid = solid;
        _polygons = polygons;
    }

    /// <summary>The leaf's index in its tree.</summary>
    public int LeafIndex { get; }

    /// <summary>Retail <c>BSPLEAF::solid</c>: non-zero when the leaf's space is inside the solid.</summary>
    public int Solid { get; }

    /// <summary>The polygons in the leaf (retail <c>in_polys</c>).</summary>
    public IReadOnlyList<PhysPolygon> Polygons => _polygons;

    /// <summary>A leaf from parts (synthetic trees in tests; real ones come from the dat).</summary>
    internal static PhysicsBspLeaf Create(int leafIndex, int solid, PhysSphere boundingSphere, PhysPolygon[] polygons) =>
        new(leafIndex, solid, boundingSphere, polygons);

    /// <summary>
    /// Retail <c>BSPLEAF::sphere_intersects_poly</c> (11.4186 0x0053D580, 11.6096 0x0053E2E0): the first polygon of the
    /// leaf the sphere hits while moving against its front.
    /// </summary>
    internal override bool SphereIntersectsPoly(in PhysSphere checkPos, Vector3 movement, ref PhysPolygon? polygon, ref Vector3 contactPoint)
    {
        if (_polygons.Length == 0 || !BoundingSphere.Intersects(checkPos))
        {
            return false;
        }

        foreach (var candidate in _polygons)
        {
            if (candidate.PosHitsSphere(checkPos, movement, out contactPoint, ref polygon))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Retail <c>BSPLEAF::sphere_intersects_solid</c> (11.4186 0x0053D5F0, 11.6096 0x0053E350): true at once for a solid
    /// leaf under the centre check, otherwise whether the sphere touches one of its polygons. An empty leaf never
    /// intersects.
    /// </summary>
    internal override bool SphereIntersectsSolid(in PhysSphere checkPos, bool centerCheck)
    {
        if (_polygons.Length == 0)
        {
            return false;
        }

        if (centerCheck && Solid != 0)
        {
            return true;
        }

        if (!BoundingSphere.Intersects(checkPos))
        {
            return false;
        }

        foreach (var candidate in _polygons)
        {
            if (candidate.HitsSphere(checkPos))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Retail <c>BSPLEAF::hits_walkable</c> (11.4186 0x0053D670, 11.6096 0x0053E3D0): a polygon that is walkable, touched
    /// and under the sphere's half-radius footprint (<see cref="PhysPolygon.CheckSmallWalkable"/>).
    /// </summary>
    internal override bool HitsWalkable(SpherePath path, in PhysSphere validPos, Vector3 up)
    {
        if (_polygons.Length == 0 || !BoundingSphere.Intersects(validPos))
        {
            return false;
        }

        foreach (var candidate in _polygons)
        {
            if (candidate.WalkableHitsSphere(path, validPos, up) && candidate.CheckSmallWalkable(validPos, up))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Retail <c>BSPLEAF::find_walkable</c> (11.4186 0x0053D6F0, 11.6096 0x0053E450): every walkable polygon the sphere
    /// touches moves it back onto its plane; the last one that moved it is recorded.
    /// </summary>
    internal override void FindWalkable(SpherePath path, ref PhysSphere validPos, ref PhysPolygon? polygon, Vector3 movement, Vector3 up, ref bool changed)
    {
        if (_polygons.Length == 0 || !BoundingSphere.Intersects(validPos))
        {
            return;
        }

        foreach (var candidate in _polygons)
        {
            if (candidate.WalkableHitsSphere(path, validPos, up) && candidate.AdjustSphereToPlane(path, ref validPos, movement))
            {
                changed = true;
                polygon = candidate;
            }
        }
    }

    /// <summary>
    /// Retail <c>BSPLEAF::sphere_intersects_solid_poly</c> (11.4186 0x0053D780, 11.6096 0x0053E4E0): a solid leaf under the
    /// centre check sets <paramref name="centerSolid"/>; the first polygon the sphere touches is reported. The result is
    /// true for a hit, else the centre-solid flag.
    /// </summary>
    internal override bool SphereIntersectsSolidPoly(in PhysSphere checkPos, float radius, ref bool centerSolid, ref PhysPolygon? hitPoly, bool centerCheck)
    {
        if (_polygons.Length == 0)
        {
            return false;
        }

        if (centerCheck && Solid != 0)
        {
            centerSolid = true;
        }

        if (!BoundingSphere.Intersects(checkPos))
        {
            return centerSolid;
        }

        foreach (var candidate in _polygons)
        {
            if (candidate.HitsSphere(checkPos))
            {
                hitPoly = candidate;
                return true;
            }
        }

        return centerSolid;
    }

    /// <summary>Retail <c>BSPLEAF::point_intersects_solid</c> (11.4186 0x0053D3F0, 11.6096 0x0053E150): a leaf with polygons.</summary>
    internal override bool PointIntersectsSolid(Vector3 point) => _polygons.Length != 0;

    /// <inheritdoc/>
    public override string ToString() => $"LEAF {LeafIndex} solid={Solid} polygons={_polygons.Length}";
}
