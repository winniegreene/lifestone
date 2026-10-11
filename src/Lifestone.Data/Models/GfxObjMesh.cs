using System.Numerics;
using Lifestone.Data.Geometry;

namespace Lifestone.Data.Models;

/// <summary>
/// Renderable geometry of one GfxObj (<c>0x01xxxxxx</c>), built by <see cref="GfxObjMeshBuilder"/> from its drawing
/// polygons, plus the per-object data retail keeps next to the mesh (sort centre, bounds, spheres, LOD link).
/// </summary>
/// <remarks>
/// <para>
/// Positions are in the GfxObj's own local AC frame, unscaled. A Setup part applies its
/// <see cref="PartPose"/> (including <c>DefaultScale × objectScale</c>) on top; see <see cref="ModelAssembly"/>.
/// </para>
/// <para>
/// <b>Surfaces of LOD levels.</b> <see cref="SubMesh.SurfaceId"/> is resolved through <i>this</i> GfxObj's own surface
/// list. Retail draws every LOD level of a part with the part's surface array, which is the <b>level-0</b> GfxObj's
/// list (<c>CPhysicsPart::SetGfxObjArray</c>: <c>surfaces = new_gfxobj[0]-&gt;m_rgSurfaces</c>; <c>CPhysicsPart::Draw</c>
/// → <c>Render::SetSurfaceArray(part-&gt;surfaces)</c> → <c>D3DPolyRender::DrawMesh(CGfxObj*)</c>, which pairs
/// <c>obj-&gt;num_surfaces</c> with <c>Render::curr_surfaces</c>), and later ObjDesc texture/palette changes edit that
/// array. A consumer drawing a level above 0 must therefore look surfaces up by
/// <see cref="SurfaceIndexRange.SurfaceIndex"/> in the level-0 list. On end-of-retail data every level's list equals
/// level 0's, so the ids here are already the ones retail uses. See <see cref="LodChain"/>.
/// </para>
/// </remarks>
public sealed class GfxObjMesh
{
    internal GfxObjMesh(
        uint id,
        MeshData mesh,
        IReadOnlyList<SubMeshDrawInfo> subMeshInfo,
        GfxObjMeshFlags flags,
        Vector3 sortCenter,
        BoundingBox vertexBounds,
        ModelSphere? drawingSphere,
        ModelSphere? physicsSphere,
        uint degradeId,
        PolygonMeshStats stats)
    {
        Id = id;
        Mesh = mesh;
        SubMeshInfo = subMeshInfo;
        Flags = flags;
        SortCenter = sortCenter;
        VertexBounds = vertexBounds;
        DrawingSphere = drawingSphere;
        PhysicsSphere = physicsSphere;
        DegradeId = degradeId;
        Stats = stats;
    }

    /// <summary>The GfxObj file id (<c>0x01xxxxxx</c>).</summary>
    public uint Id { get; }

    /// <summary>Triangle lists, one <see cref="SubMesh"/> per (resolved Surface id, side, texture addressing).</summary>
    public MeshData Mesh { get; }

    /// <summary>Retail draw metadata for each sub-mesh, parallel to <see cref="MeshData.SubMeshes"/>.</summary>
    public IReadOnlyList<SubMeshDrawInfo> SubMeshInfo { get; }

    /// <summary>The GfxObj's flags (which optional sections it has).</summary>
    public GfxObjMeshFlags Flags { get; }

    /// <summary>
    /// <c>CGfxObj::sort_center</c>, in local space. Retail measures LOD distance from the viewer to this point
    /// (scaled by the part scale) in <c>CPhysicsPart::UpdateViewerDistance</c>.
    /// </summary>
    public Vector3 SortCenter { get; }

    /// <summary>
    /// <c>CGfxObj::gfx_bound_box</c>: bounds of <b>every</b> vertex in the vertex array, including vertices no drawing
    /// polygon uses (retail <c>CGfxObj::init_end</c>). <see cref="MeshData.Bounds"/> covers only emitted vertices.
    /// </summary>
    public BoundingBox VertexBounds { get; }

    /// <summary>
    /// Root sphere of the drawing BSP (<c>CGfxObj::drawing_sphere</c> = <c>BSPTREE::GetSphere</c>), used by retail for
    /// picking (<c>Render::GfxObjUnderSelectionRay</c>). Null when the GfxObj has no drawing BSP or its root is a leaf:
    /// a drawing-tree leaf stores no sphere (<c>BSPLEAF::UnPackLeaf</c> reads one only for physics trees). No
    /// end-of-retail GfxObj has a leaf root.
    /// </summary>
    public ModelSphere? DrawingSphere { get; }

    /// <summary>
    /// Root sphere of the physics BSP (<c>CGfxObj::physics_sphere</c>), or null when the GfxObj has no physics BSP.
    /// Every physics node stores a sphere, a leaf root included (<c>BSPLEAF::UnPackLeaf</c> with
    /// <c>pack_tree_type == 1</c>). <c>CSetup::makeSimpleSetup</c> uses it as a bare GfxObj's sorting sphere (falling
    /// back to <see cref="DrawingSphere"/>).
    /// </summary>
    public ModelSphere? PhysicsSphere { get; }

    /// <summary>
    /// <c>DIDDegrade</c>: the GfxObjDegradeInfo (<c>0x11xxxxxx</c>) giving this GfxObj's LOD chain, or 0. See
    /// <see cref="LodChain"/>: when set, retail draws the chain's entries, never this GfxObj id directly.
    /// </summary>
    public uint DegradeId { get; }

    /// <summary>Polygon/vertex statistics gathered while building (diagnostics and tests).</summary>
    public PolygonMeshStats Stats { get; }
}

/// <summary>
/// Output of <see cref="GfxObjMeshBuilder.BuildPolygons"/>: the sub-meshes, their retail draw metadata (parallel to
/// <see cref="MeshData.SubMeshes"/>) and build statistics.
/// </summary>
public sealed record PolygonMesh(MeshData Mesh, IReadOnlyList<SubMeshDrawInfo> SubMeshInfo, PolygonMeshStats Stats);

/// <summary>Which optional sections a GfxObj carries (ACE <c>GfxObjFlags</c>; retail <c>CGfxObj::Serialize</c>).</summary>
[Flags]
public enum GfxObjMeshFlags : uint
{
    None = 0,

    /// <summary>Physics polygons and physics BSP present.</summary>
    HasPhysics = 0x1,

    /// <summary>Drawing polygons and drawing BSP present.</summary>
    HasDrawing = 0x2,

    /// <summary>Bit 0x4, unused by retail <c>CGfxObj::Serialize</c>.</summary>
    Unknown4 = 0x4,

    /// <summary>A trailing <c>DIDDegrade</c> (GfxObjDegradeInfo id) is present.</summary>
    HasDegrade = 0x8,
}

/// <summary>
/// Retail draw metadata of one sub-mesh.
/// </summary>
/// <param name="FirstSurfaceIndex">
/// Lowest index into the owner's surface list that maps to this sub-mesh's Surface id and addressing. Retail builds
/// one D3DX attribute subset per surface <b>index</b> and draws subsets in index order
/// (<c>D3DPolyRender::DrawMesh</c>); sub-meshes are ordered by this value, positive side first.
/// </param>
/// <param name="PolygonCount">Number of source polygons that contributed triangles to this sub-mesh.</param>
/// <param name="Ranges">
/// The retail subsets this sub-mesh is made of: one contiguous block of <see cref="SubMesh.Indices"/> per contributing
/// surface index, in ascending index order, together covering every index. A single range unless the owner lists the
/// same Surface id at several surface indices with the same retail addressing (<see cref="SubMesh.Addressing"/>, which
/// every range of a sub-mesh shares).
/// </param>
public sealed record SubMeshDrawInfo(int FirstSurfaceIndex, int PolygonCount, IReadOnlyList<SurfaceIndexRange> Ranges);

/// <summary>
/// The triangles of a sub-mesh that come from one surface index: retail's D3DX attribute subset for that index (its
/// <see cref="MeshSide.Positive"/> or <see cref="MeshSide.Negative"/> share).
/// </summary>
/// <param name="SurfaceIndex">Index into the owner's surface list.</param>
/// <param name="FirstIndex">Offset of the block in <see cref="SubMesh.Indices"/> (a multiple of 3).</param>
/// <param name="IndexCount">Number of indices in the block (a multiple of 3; triangles keep file order).</param>
public readonly record struct SurfaceIndexRange(int SurfaceIndex, int FirstIndex, int IndexCount);

/// <summary>Counts gathered by <see cref="GfxObjMeshBuilder"/>.</summary>
/// <param name="SourceVertexCount">Vertices in the source vertex array (used or not).</param>
/// <param name="DrawingPolygonCount">Polygons processed (for a GfxObj, its drawing polygons; physics polygons are never drawn).</param>
/// <param name="NoPosPolygonCount">Polygons without positive UV indices (<c>stippling &amp; NoPos</c>); drawn with UV index 0.</param>
/// <param name="DoubleSidedPolygonCount"><c>sides_type 1</c> polygons (reversed copy emitted).</param>
/// <param name="SeparateBackPolygonCount"><c>sides_type 2</c> polygons (see the retail quirk on <see cref="GfxObjMeshBuilder"/>).</param>
/// <param name="DegeneratePolygonCount">Polygons with fewer than 3 vertices (nothing drawn; none on end-of-retail data).</param>
/// <param name="ZeroNormalVertexCount">Distinct drawn vertices whose stored normal is exactly zero (kept as zero).</param>
/// <param name="OutOfRangeUvCount">
/// Stored UV indices outside the vertex's UV list (or negative as a signed byte), per polygon side; drawn as (0, 0).
/// </param>
public readonly record struct PolygonMeshStats(
    int SourceVertexCount,
    int DrawingPolygonCount,
    int NoPosPolygonCount,
    int DoubleSidedPolygonCount,
    int SeparateBackPolygonCount,
    int DegeneratePolygonCount,
    int ZeroNormalVertexCount,
    int OutOfRangeUvCount);
