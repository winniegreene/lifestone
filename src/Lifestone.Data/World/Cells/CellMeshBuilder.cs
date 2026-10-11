using System.Numerics;
using Lifestone.Data.Geometry;
using Lifestone.Data.Models;

namespace Lifestone.Data.World.Cells;

/// <summary>
/// Builds the drawn geometry of an EnvCell: its CellStruct's polygons with the cell's surfaces, as <see cref="MeshData"/>
/// in cell-local AC coordinates (apply <see cref="EnvCellData.Frame"/> to place it in the landblock).
/// </summary>
/// <remarks>
/// <para>Re-expresses retail <c>D3DPolyRender::ConstructMesh</c> (acclient.c:455780, called from
/// <c>CEnvCell::UnPack</c> with the CellStruct's drawing polygons) and the env-cell path of
/// <c>D3DPolyRender::DrawMesh</c> (acclient.c:455154). The ConstructMesh rules are not restated here: this class adapts
/// a <see cref="CellStructure"/> to <see cref="RetailConstructMesh"/>, the one implementation GfxObjs use as well
/// (<c>CellExhaustiveTests.Every_distinct_cell_mesh_is_identical_through_the_cell_and_gfxobj_paths</c> pins that both
/// adapters give the same mesh for every distinct cell mesh; <c>RetailConstructMeshTests</c> does so for shared synthetic
/// cases and the Holtburg cells). In short:</para>
/// <list type="number">
/// <item><b>Every</b> drawing polygon is put in the mesh, portal polygons included; nothing is filtered by stippling.</item>
/// <item>Sides: type 0 emits the positive side; type 1 emits it again with reversed winding, negated normals and the
/// same surface and UVs (<c>CPolygon::UnPack</c> copies pos_surface/pos_uv_indices into the negative side); type 2
/// emits a negative side with neg_surface and neg_uv_indices. Any other value fails loudly.</item>
/// <item>Triangles are fans <c>(v0, v[i+1], v[i+2])</c>; the reversed side uses <c>(v[i+2], v[i+1], v0)</c>.</item>
/// <item>Vertices are shared per (vertex id, UV index) within a side. A side stored without UV indices (e.g. NoPos)
/// uses UV index 0, i.e. the vertex's first UV when it has one (<c>D3DPolyRender::ConstructMesh</c> passes 0 when
/// the side has no UV list); a UV index outside the vertex's UV list gives (0, 0) (retail <c>copyVert</c>).</item>
/// <item>Sub-meshes come in retail's subset order: by first surface index, positive side first; a Surface listed at
/// several indices with the same addressing keeps one contiguous block of triangles per index.</item>
/// <item>When drawing an env cell retail skips every mesh subset whose Surface has neither Base1Image (0x2) nor
/// Base1ClipMap (0x4) (<c>skipNoTexture</c> is 1, acclient.c:45777, and only ever set to 1). Those triangles are left
/// out here. Over the 17,134 distinct (struct, surface list) meshes of the end-of-retail dats this drops 46,873
/// invisible NoPos portal faces and 346 solid-colour faces (which ACViewer, skipping only NoPos, would draw), and
/// keeps 1,218 textured portal polygons, which retail draws.</item>
/// </list>
/// <para>Cell polygons are authored like object polygons: counter-clockwise when seen from their positive side, which
/// faces into the room. Measured over those meshes: for 95.7% of the drawn front-face triangles a point 5 cm in front
/// of the triangle lies inside the cell BSP (the cell BSP is a convex approximation, so faces of pillars and alcoves
/// can probe outside it). Retail draws cells
/// with the same cull mode as objects (<c>RenderMeshSubset</c> sets CULLMODE_CW for every subset), so the winding is
/// kept as authored and no flip is applied.</para>
/// <para>Retail difference (not reachable for cells): for sides type 2 retail does not reverse the negative side's
/// index order (only its normals). MeshData's convention reverses it; no CellStruct polygon has sides type 2.</para>
/// </remarks>
public static class CellMeshBuilder
{
    /// <summary>ACE <c>SurfaceType.Base1Image</c>.</summary>
    public const uint SurfaceBase1Image = 0x2;

    /// <summary>ACE <c>SurfaceType.Base1ClipMap</c>.</summary>
    public const uint SurfaceBase1ClipMap = 0x4;

    /// <summary>True when retail draws a surface in an env-cell (or building) mesh: <c>type &amp; 6</c>.</summary>
    public static bool IsDrawnInCells(uint surfaceType) => (surfaceType & (SurfaceBase1Image | SurfaceBase1ClipMap)) != 0;

    /// <summary>Builds the mesh of <paramref name="structure"/> with an EnvCell's surface list.</summary>
    /// <param name="structure">The cell's struct.</param>
    /// <param name="surfaceIds">The EnvCell's resolved Surface (0x08) ids.</param>
    /// <param name="surfaceTypeOf">Returns a Surface's type bits (Surface.Type).</param>
    /// <exception cref="InvalidDataException">A polygon references a surface index outside the list.</exception>
    /// <exception cref="NotSupportedException">A polygon has a sides type other than 0, 1 or 2.</exception>
    public static CellMesh Build(CellStructure structure, IReadOnlyList<uint> surfaceIds, Func<uint, uint> surfaceTypeOf)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(surfaceIds);
        ArgumentNullException.ThrowIfNull(surfaceTypeOf);

        // The one ConstructMesh implementation (Models/RetailConstructMesh.cs), shared with GfxObjMeshBuilder; cells only
        // ever draw in the EnvCell context, so the untextured sides retail skips there (DrawMesh with isEnvCell) are
        // dropped at build time.
        var polygons = new ConstructMeshPolygon[structure.Polygons.Count];
        for (var i = 0; i < polygons.Length; i++)
        {
            var p = structure.Polygons[i];
            polygons[i] = new ConstructMeshPolygon(
                p.Id,
                p.VertexIndices,
                unchecked((ushort)p.PosSurface),
                unchecked((ushort)p.NegSurface),
                p.SidesType,
                p.Stippling,
                p.PosUvIndices,
                p.NegUvIndices);
        }

        var built = RetailConstructMesh.Build(
            structure.ToString(),
            surfaceIds,
            new StructureVertices(structure),
            polygons,
            surfaceId => IsDrawnInCells(surfaceTypeOf(surfaceId)));

        var infos = new CellSubMeshInfo[built.SubMeshInfo.Count];
        for (var i = 0; i < infos.Length; i++)
        {
            infos[i] = new CellSubMeshInfo((short)built.SubMeshInfo[i].FirstSurfaceIndex);
        }

        return new CellMesh(
            structure.EnvironmentId,
            structure.Index,
            surfaceIds.ToArray(),
            built.Mesh,
            infos,
            structure.HasDrawingBsp,
            structure.Polygons.Count,
            built.DrawnPolygonCount,
            built.FilteredPolygonCount,
            built.Stats.DegeneratePolygonCount);
    }

    /// <summary>A CellStruct's vertex array as <see cref="RetailConstructMesh"/> reads it.</summary>
    private sealed class StructureVertices(CellStructure structure) : IConstructMeshVertices
    {
        public int Count => structure.Positions.Count;

        public bool TryGet(ushort id, out Vector3 position, out Vector3 normal, out int uvCount)
        {
            if (id >= structure.Positions.Count)
            {
                position = normal = default;
                uvCount = 0;
                return false;
            }

            position = structure.Positions[id];
            normal = structure.Normals[id];
            uvCount = structure.Uvs[id].Count;
            return true;
        }

        public Vector2 Uv(ushort id, int index) => structure.Uvs[id][index];
    }
}

/// <summary>The drawn geometry of one (Environment, struct, surface list) combination.</summary>
/// <param name="EnvironmentId">Environment (0x0D) id.</param>
/// <param name="StructIndex">CellStruct index within it.</param>
/// <param name="SurfaceIds">The EnvCell surface list the mesh was built with.</param>
/// <param name="Mesh">Cell-local geometry, one sub-mesh per (Surface, side, texture addressing) as
/// <see cref="MeshData"/> specifies: usually one per (Surface, side), but a cell that lists a Surface twice with
/// different addressing gets two (339 of the 17,134 end-of-retail meshes). Key materials by (SurfaceId,
/// <see cref="SubMesh.Addressing"/>), never by the Surface id alone.</param>
/// <param name="SubMeshInfo">Parallel to <c>Mesh.SubMeshes</c>: the surface index each sub-mesh came from first.</param>
/// <param name="HasDrawingBsp">False when retail would not draw the cell at all (<c>PView::DrawCells</c>).</param>
/// <param name="PolygonCount">Drawing polygons in the struct.</param>
/// <param name="DrawnPolygonCount">Polygons with at least one side in the mesh.</param>
/// <param name="UntexturedPolygonCount">Polygons dropped because their surfaces are not textured (retail skipNoTexture).</param>
/// <param name="DegeneratePolygonCount">Polygons with fewer than three corners.</param>
public sealed record CellMesh(
    uint EnvironmentId,
    ushort StructIndex,
    IReadOnlyList<uint> SurfaceIds,
    MeshData Mesh,
    IReadOnlyList<CellSubMeshInfo> SubMeshInfo,
    bool HasDrawingBsp,
    int PolygonCount,
    int DrawnPolygonCount,
    int UntexturedPolygonCount,
    int DegeneratePolygonCount);

/// <summary>Per-sub-mesh data retail derives while building the mesh. Texture addressing (WRAP when some polygon of
/// the subset has a stippling bit set, else CLAMP; <c>D3DPolyRender::SetSurface</c>, acclient.c:454437, address mode 1
/// vs 3) now travels on the sub-mesh itself as <see cref="SubMesh.Addressing"/>.</summary>
/// <param name="SurfaceIndex">First index of the Surface in the cell's surface list.</param>
public readonly record struct CellSubMeshInfo(short SurfaceIndex);
