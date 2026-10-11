namespace Lifestone.Client.Rendering;

/// <summary>
/// Which retail lighting rule a surface shader variant applies (an extension of <see cref="SurfaceShaderVariant"/> for
/// the interiors track; docs/interiors.md §4, docs/render-contract.md §2.1).
/// </summary>
/// <remarks>
/// <para>Retail decides lighting per draw, not per surface: the same Surface is lit differently on an outdoor object,
/// on an EnvCell and on an object standing in an EnvCell. Godot fixes the lighting code when it compiles the shader, so
/// the rule is part of the variant (and therefore of the material key).</para>
/// <list type="bullet">
/// <item><see cref="Object"/>: the render contract's rule for everything drawn by <c>LScape::draw</c> (sun on,
/// <c>SmartBox::RenderNormalMode</c> → <c>Render::useSunlightSet(1)</c>): <c>luminosity + ac_ambient + sun</c>.</item>
/// <item><see cref="EnvCell"/>: an EnvCell's own mesh (<c>PView::DrawCells</c> draws cells after
/// <c>Render::useSunlightSet(0)</c>; <c>RenderDeviceD3D::DrawEnvCell</c> → <c>D3DPolyRender::SetStaticLightingVertexColors</c>
/// burns the static lights into the vertex colour, and <c>D3DPolyRender::RenderMeshSubset</c> then takes the material
/// emissive from that vertex colour): <c>vertex colour + world ambient</c>, no sun, no Surface luminosity.</item>
/// <item><see cref="CellObject"/>: an object drawn inside a cell (<c>PView::DrawCells</c> → <c>DrawObjCellForDummies</c>,
/// still with the sun off): <c>luminosity + world ambient</c> plus up to eight hardware point lights
/// (<c>Render::minimize_object_lighting</c>), whose sum the interiors compute per part and pass as a vertex attribute
/// (docs/interiors.md §4.4).</item>
/// </list>
/// <para>"World ambient" is <c>ac_ambient</c>, except while the viewer stands in an EnvCell that is not seen_outside,
/// when retail sets it to 0.2 white (<c>CellManager::ChangePosition</c> → <c>SmartBox::SetWorldAmbientLight(0.2,
/// 0xFFFFFFFF)</c>); the shaders read that from the <c>ac_indoor_ambient</c> globals.</para>
/// </remarks>
public enum SurfaceLighting
{
    /// <summary>Outdoor objects, buildings, scenery (the render contract's default).</summary>
    Object,

    /// <summary>EnvCell geometry: burned-in static lights (vertex colour) + world ambient; no sun.</summary>
    EnvCell,

    /// <summary>Objects inside EnvCells: luminosity + world ambient + their static lights; no sun.</summary>
    CellObject,
}
