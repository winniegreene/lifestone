using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Lifestone.Data.Dat;
using Lifestone.Data.Geometry;

namespace Lifestone.Data.Models;

/// <summary>
/// Builds <see cref="GfxObjMesh"/> (engine-agnostic <see cref="MeshData"/>) from a GfxObj's drawing polygons,
/// following retail <c>D3DPolyRender::ConstructMesh</c> (acclient 11.4186, rendering core unchanged in 11.6096).
/// </summary>
/// <remarks>
/// <para>The rules themselves live in one place, <see cref="RetailConstructMesh"/>, which EnvCells
/// (<c>CellMeshBuilder</c>) use too; this class adapts DatReaderWriter's GfxObj records to it and adds the GfxObj
/// metadata (bounds, BSP spheres, degrade link).</para>
/// <para><b>What retail emits per drawing polygon</b> (in file order; physics polygons are never drawn):</para>
/// <list type="bullet">
/// <item><b>Positive side, always</b> — whatever the <c>stippling</c> bits say. Surface = <c>pos_surface</c>; UVs from
/// <c>pos_uv_indices</c>, or UV index 0 for every corner when the polygon has none (<c>stippling &amp; NoPos</c>,
/// <c>CPolygon::UnPack</c> then leaves the pointer null and ConstructMesh substitutes 0). Normals are the stored
/// <c>CSWVertex</c> normals, copied unnormalised (<c>copyVert</c>). Triangles are a fan
/// <c>(v0, v[i], v[i+1])</c>, counter-clockwise under the right-hand rule like the polygon plane
/// (<c>CPolygon::make_plane</c>).</item>
/// <item><b><c>sides_type == 1</c></b> (ACE/DatReaderWriter name it <c>CullMode.None</c>): a second winding of the
/// same polygon with the same surface and the same (positive) UVs, reversed (<c>indexBuf[2 - c]</c>) and with
/// negated normals (<c>normalMultiplier = -1</c>). <c>CPolygon::UnPack</c> also copies <c>pos_surface</c> into
/// <c>neg_surface</c> for these.</item>
/// <item><b><c>sides_type == 2</c></b> (<c>CullMode.Clockwise</c>): a negative side using <c>neg_surface</c> and
/// <c>neg_uv_indices</c> (UV index 0 when absent, i.e. <c>stippling &amp; NoNeg</c>), with negated normals.</item>
/// <item><c>sides_type == 0</c> is single-sided. Retail would draw any other value single-sided too, but none occurs
/// in the dats, so one fails loudly (<see cref="NotSupportedException"/>, see <see cref="RetailConstructMesh"/>).</item>
/// </list>
/// <para>
/// Retail draws GfxObj meshes with <c>CULLMODE_CW</c> (<c>D3DPolyRender::RenderMeshSubset</c>), so the positive side
/// is visible from the front only, and the <c>sides_type 1</c> reversed copy makes such polygons visible from both
/// sides. This builder emits those reversed copies as <see cref="MeshSide.Negative"/> sub-meshes, per the
/// <see cref="MeshData"/> contract, so consumers never disable back-face culling.
/// </para>
/// <para>
/// <b>Known retail quirk not reproduced (lead decision: keep the true back face):</b> for <c>sides_type == 2</c>,
/// retail's ConstructMesh writes the negative side's triangles <i>without</i> reversing them (only <c>sides_type 1</c>
/// gets a second "winding", <c>w ? 2 - c : c</c>); with <c>CULLMODE_CW</c> that face is coplanar with the positive
/// side, front-facing, and never visible from behind. This builder follows the <see cref="MeshData"/> contract instead
/// (reversed winding, a real back face), a documented deviation. It affects 11 polygons (14 triangles) in 6 GfxObjs
/// of the end-of-retail portal.dat (0x010008AA, 0x010016D3, 0x01002197, 0x01002B66, 0x0100326E, 0x010032C4);
/// <see cref="PolygonMeshStats.SeparateBackPolygonCount"/> flags them.
/// </para>
/// <para>
/// <b>Vertices</b> are split per (vertex id, UV index) within each sub-mesh, like retail's <c>vertmap</c>
/// (and ACViewer <c>VertexArrayExtensions.BuildUVLookup</c>). A UV index that is negative as a signed byte or
/// outside the vertex's UV list yields (0, 0), as in <c>copyVert</c> (<c>uv &gt;= 0 &amp;&amp; uv &lt; num_uvs</c>
/// on a <c>char</c>). UVs are passed through unmodified (AC's UV origin is the texture's top-left corner).
/// </para>
/// <para>
/// <b>Sub-meshes and surface indices.</b> Retail builds one D3DX attribute subset per surface <i>index</i>
/// (<c>s_MeshBatches[pos_surface]</c>) and gives each its own texture addressing: WRAP when
/// <c>isStippledOrAlphaedMask[index] &amp; 1</c>, else CLAMP (<see cref="TextureAddressMode"/>).
/// <see cref="MeshData"/> wants one sub-mesh per (Surface id, side, addressing), so when one Surface id sits at
/// several used surface indices with the same addressing, their triangles share a sub-mesh; indices whose addressing
/// differs get separate sub-meshes. Inside a sub-mesh the indices are not mixed: each keeps a contiguous block of
/// triangles (in file order), listed in <see cref="SubMeshDrawInfo.Ranges"/>. On end-of-retail data 84 sub-meshes in
/// 70 GfxObjs merge two or more surface indices, and 24 (Surface id, side) pairs are split by addressing (137
/// polygons that retail draws with CLAMP next to WRAP ones of the same Surface, e.g. 0x010000D1 surface 0x080001CF at
/// indices 0, 1, 2); <c>ModelsExhaustiveTests</c> pins the counts.
/// </para>
/// <para>
/// <b>EnvCells</b> go through the same retail function (<c>CEnvCell</c> passes its CellStruct's polygons to
/// <c>D3DPolyRender::ConstructMesh</c>, with a detail-texture UV factor of 3 and baked static lighting), so
/// <see cref="BuildPolygons"/> accepts any vertex array, polygon list and resolved surface list. Everything is
/// emitted; what retail then <b>skips at draw time</b> depends on the Surface and on what is being drawn:
/// <c>D3DPolyRender::DrawMesh</c> drops every subset whose Surface has no texture (<c>type &amp; 6 == 0</c>, e.g.
/// <c>Base1Solid</c>) when it draws an EnvCell (<c>isEnvCell</c>, from <c>RenderDeviceD3D::DrawEnvCell</c>) or a
/// building (<c>RenderDeviceD3D::DrawBuilding</c> sets <c>ObjBuildingOrBuildingPart</c>; the global
/// <c>skipNoTexture</c> is 1 and never cleared). That is how a CellStruct's or a building's portal polygons
/// (doorways and windows, NoPos polygons on untextured Translucent surfaces such as 0x080007E6 in EnvCell
/// 0xA9B40100 and building 0x01000C1E) disappear; they are <b>not</b> drawn transparent. Apply the rule with
/// <see cref="SurfaceDrawRules"/>. (ACViewer instead skips <c>NoPos</c> polygons in EnvCells,
/// <c>Render/InstanceBatch.cs</c>.)
/// </para>
/// </remarks>
public static class GfxObjMeshBuilder
{
    /// <summary>Retail <c>CullMode</c> value meaning "both sides, same surface" (ACE <c>CullMode.None</c>).</summary>
    public const int SidesTypeDoubleSided = RetailConstructMesh.SidesTypeDoubleSided;

    /// <summary>Retail <c>CullMode</c> value meaning "separate negative side" (ACE <c>CullMode.Clockwise</c>).</summary>
    public const int SidesTypeSeparateBack = RetailConstructMesh.SidesTypeSeparateBack;

    /// <summary>Reads GfxObj <paramref name="gfxObjId"/> through <paramref name="dats"/> and builds its mesh.</summary>
    /// <returns>The mesh, or null when the dat has no such GfxObj.</returns>
    public static GfxObjMesh? Load(DatSet dats, uint gfxObjId)
    {
        ArgumentNullException.ThrowIfNull(dats);
        return dats.TryGetPortal<GfxObj>(gfxObjId, out var gfxObj) ? Build(gfxObjId, gfxObj) : null;
    }

    /// <summary>Builds the mesh of an already-decoded GfxObj. Does not modify <paramref name="gfxObj"/>.</summary>
    /// <exception cref="InvalidDataException">A polygon references a missing vertex or surface index.</exception>
    public static GfxObjMesh Build(uint gfxObjId, GfxObj gfxObj)
    {
        ArgumentNullException.ThrowIfNull(gfxObj);

        var vertices = gfxObj.VertexArray?.Vertices ?? new Dictionary<ushort, SWVertex>();
        var polygons = gfxObj.Flags.HasFlag(GfxObjFlags.HasDrawing)
            ? gfxObj.Polygons
            : new Dictionary<ushort, Polygon>();
        var surfaceIds = gfxObj.Surfaces.Select(s => s.DataId).ToArray();
        var built = BuildPolygons(gfxObjId, surfaceIds, vertices, polygons);

        return new GfxObjMesh(
            gfxObjId,
            built.Mesh,
            built.SubMeshInfo,
            (GfxObjMeshFlags)(uint)gfxObj.Flags,
            gfxObj.SortCenter,
            VertexBounds(vertices),
            gfxObj.Flags.HasFlag(GfxObjFlags.HasDrawing) ? RootSphere(gfxObj.DrawingBSP?.Root) : null,
            gfxObj.Flags.HasFlag(GfxObjFlags.HasPhysics) ? RootSphere(gfxObj.PhysicsBSP?.Root) : null,
            gfxObj.Flags.HasFlag(GfxObjFlags.HasDIDDegrade) ? gfxObj.DIDDegrade : 0u,
            built.Stats,
            surfaceIds);
    }

    /// <summary>
    /// The retail <c>D3DPolyRender::ConstructMesh</c> rules (<see cref="RetailConstructMesh"/>, the one implementation
    /// shared with <c>CellMeshBuilder</c>) applied to DatReaderWriter's polygon list: a GfxObj's drawing polygons, or an
    /// EnvCell CellStruct's polygons with the EnvCell's surfaces.
    /// </summary>
    /// <param name="ownerId">Id of the owning record, for error messages only.</param>
    /// <param name="surfaceIds">Resolved Surface ids (<c>0x08xxxxxx</c>) indexed by the polygons' surface indices.</param>
    /// <param name="vertices">The vertex array, keyed by vertex id.</param>
    /// <param name="polygons">Polygons in file order.</param>
    /// <param name="keepSurface">Null (the default) to emit every side; see <see cref="RetailConstructMesh.Build"/>.</param>
    /// <exception cref="InvalidDataException">A polygon references a missing vertex or surface index, or has a
    /// malformed UV table.</exception>
    /// <exception cref="NotSupportedException">A polygon has a <c>sides_type</c> other than 0, 1 or 2 (none in the dats).</exception>
    public static PolygonMesh BuildPolygons(
        uint ownerId,
        IReadOnlyList<uint> surfaceIds,
        IReadOnlyDictionary<ushort, SWVertex> vertices,
        IEnumerable<KeyValuePair<ushort, Polygon>> polygons,
        Func<uint, bool>? keepSurface = null)
    {
        ArgumentNullException.ThrowIfNull(surfaceIds);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(polygons);

        var adapted = new List<ConstructMeshPolygon>();
        foreach (var (key, polygon) in polygons)
        {
            var vertexIds = new ushort[polygon.VertexIds.Count];
            for (var i = 0; i < vertexIds.Length; i++)
            {
                vertexIds[i] = unchecked((ushort)polygon.VertexIds[i]);
            }

            adapted.Add(new ConstructMeshPolygon(
                key,
                vertexIds,
                unchecked((ushort)polygon.PosSurface),
                unchecked((ushort)polygon.NegSurface),
                (int)polygon.SidesType,
                (byte)polygon.Stippling,
                polygon.PosUVIndices,
                polygon.NegUVIndices));
        }

        var built = RetailConstructMesh.Build($"0x{ownerId:X8}", surfaceIds, new SwVertexArray(vertices), adapted, keepSurface);
        return new PolygonMesh(built.Mesh, built.SubMeshInfo, built.Stats);
    }

    /// <summary>A DatReaderWriter vertex array as <see cref="RetailConstructMesh"/> reads it.</summary>
    private sealed class SwVertexArray(IReadOnlyDictionary<ushort, SWVertex> vertices) : IConstructMeshVertices
    {
        public int Count => vertices.Count;

        public bool TryGet(ushort id, out Vector3 position, out Vector3 normal, out int uvCount)
        {
            if (!vertices.TryGetValue(id, out var vertex))
            {
                position = normal = default;
                uvCount = 0;
                return false;
            }

            position = vertex.Origin;
            normal = vertex.Normal;
            uvCount = vertex.UVs.Count;
            return true;
        }

        public Vector2 Uv(ushort id, int index)
        {
            var uv = vertices[id].UVs[index];
            return new Vector2(uv.U, uv.V);
        }
    }

    /// <summary>Retail <c>CGfxObj::init_end</c>: bounds of every vertex in the vertex array.</summary>
    private static BoundingBox VertexBounds(IReadOnlyDictionary<ushort, SWVertex> vertices)
    {
        if (vertices.Count == 0)
        {
            return BoundingBox.Empty;
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var vertex in vertices.Values)
        {
            min = Vector3.Min(min, vertex.Origin);
            max = Vector3.Max(max, vertex.Origin);
        }

        return new BoundingBox(min, max);
    }

    /// <summary>
    /// <c>BSPTREE::GetSphere</c>: the root node's sphere. Every physics node stores one, leaves included
    /// (<c>BSPLEAF::UnPackLeaf</c> reads <c>solid</c> and <c>sphere</c> when <c>BSPNODE::pack_tree_type == 1</c>);
    /// a drawing-tree leaf stores none, so a drawing tree whose root is a leaf yields null. No end-of-retail GfxObj
    /// has a leaf root in either tree.
    /// </summary>
    private static ModelSphere? RootSphere(BSPNode? root) => root switch
    {
        PhysicsBSPNode p => ToSphere(p.BoundingSphere),
        DrawingBSPNode { Type: not BSPNodeType.Leaf } d => ToSphere(d.BoundingSphere),
        _ => null,
    };

    private static ModelSphere? ToSphere(Sphere? sphere) => sphere is null ? null : new ModelSphere(sphere.Origin, sphere.Radius);
}
