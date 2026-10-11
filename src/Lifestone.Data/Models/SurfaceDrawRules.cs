using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Lifestone.Data.Dat;
using Lifestone.Data.Geometry;

namespace Lifestone.Data.Models;

/// <summary>What a mesh is drawn as. Retail skips different sub-meshes in each case (see <see cref="SurfaceDrawRules"/>).</summary>
public enum MeshDrawContext
{
    /// <summary>
    /// An object drawn through its part array (<c>CPartArray::Draw</c> → <c>CPhysicsPart::Draw(part, 0)</c>):
    /// statics, scenery, creatures, items, and the static objects inside EnvCells. Every sub-mesh is drawn.
    /// </summary>
    Object,

    /// <summary>
    /// A landblock building (<c>LandBlockInfo</c> building, retail <c>CBuildingObj</c>) drawn by
    /// <c>RenderDeviceD3D::DrawBuilding</c>, which draws the building's first part with
    /// <c>RenderDeviceD3D::ObjBuildingOrBuildingPart = 1</c>. Untextured sub-meshes are skipped.
    /// </summary>
    Building,

    /// <summary>
    /// An EnvCell's CellStruct mesh (<c>RenderDeviceD3D::DrawEnvCell</c> →
    /// <c>D3DPolyRender::DrawMesh(num_surfaces, surfaces, mesh, isEnvCell = 1)</c>). Untextured sub-meshes are skipped.
    /// </summary>
    EnvCell,
}

/// <summary>
/// Retail draw-time rules that depend on a sub-mesh's Surface record rather than on its geometry.
/// </summary>
/// <remarks>
/// <para>
/// <c>D3DPolyRender::DrawMesh</c> walks the subsets of a constructed mesh and, while the global
/// <c>skipNoTexture</c> is set (initialised to 1 in <c>acclient_data.h</c> and never cleared), skips every subset whose
/// <c>CSurface::type &amp; 6</c> is zero (neither <c>Base1Image</c> nor <c>Base1ClipMap</c>: a solid-colour surface)
/// when <c>RenderDeviceD3D::ObjBuildingOrBuildingPart</c> or <c>isEnvCell</c> is set. <c>DrawBuilding</c> sets the
/// former around its <c>CPhysicsPart::Draw(part, 0)</c> call (its preceding <c>Draw(part, 1)</c> only builds the
/// building's portal views, <c>RenderDeviceD3D::DrawMeshInternal</c>); <c>DrawEnvCell</c> passes the latter. Any other
/// object draws its untextured surfaces as solid colours.
/// </para>
/// <para>
/// This is how doorway and window portal polygons vanish: they sit on untextured Translucent surfaces (e.g.
/// 0x080007E6, type <c>Base1Solid | Translucent</c>, used by EnvCell 0xA9B40100's NoPos portal polygons and by the
/// Holtburg building 0x01000C1E). Drawn with depth writes they would block the view through doorways, and the opaque
/// solid-colour faces some building models carry are ones retail never shows. <see cref="GfxObjMeshBuilder"/> still
/// emits these sub-meshes, so a mesh can be shared between contexts; consumers drop them with
/// <see cref="IsDrawn"/> or <see cref="DrawnSubMeshes"/>.
/// </para>
/// </remarks>
public static class SurfaceDrawRules
{
    /// <summary><c>CSurface::type</c> bits that mean "has a texture" (<c>Base1Image | Base1ClipMap</c>, 0x6).</summary>
    public const SurfaceType TexturedTypeMask = SurfaceType.Base1Image | SurfaceType.Base1ClipMap;

    /// <summary>True when the Surface has a texture (<c>type &amp; 6 != 0</c>).</summary>
    public static bool IsTextured(SurfaceType type) => (type & TexturedTypeMask) != 0;

    /// <summary>
    /// Whether retail draws a sub-mesh on a Surface of type <paramref name="type"/> in <paramref name="context"/>
    /// (<c>D3DPolyRender::DrawMesh</c>).
    /// </summary>
    public static bool IsDrawn(SurfaceType type, MeshDrawContext context) =>
        context == MeshDrawContext.Object || IsTextured(type);

    /// <summary>
    /// <see cref="IsDrawn"/> for every sub-mesh of <paramref name="mesh"/>, resolving each Surface id from the
    /// portal dat. A Surface that does not exist counts as not drawn.
    /// </summary>
    /// <returns>One flag per <see cref="MeshData.SubMeshes"/> entry.</returns>
    public static bool[] DrawnSubMeshes(DatSet dats, MeshData mesh, MeshDrawContext context)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(mesh);
        var drawn = new bool[mesh.SubMeshes.Count];
        for (var i = 0; i < drawn.Length; i++)
        {
            drawn[i] = dats.TryGetPortal<Surface>(mesh.SubMeshes[i].SurfaceId, out var surface) && IsDrawn(surface.Type, context);
        }

        return drawn;
    }
}
