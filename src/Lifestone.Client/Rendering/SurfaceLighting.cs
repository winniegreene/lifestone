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
/// <item><see cref="CreatureMode"/>: an object drawn by creature mode (<c>CreatureMode::Render</c>, the portal tunnel,
/// the character-creation figure, the paperdoll): in its own viewport with its own ambient and one distant light, the
/// sun and every other light off (<c>luminosity + creature ambient + light colour x intensity x max(0, N.L)</c>) and no
/// fog. Its inputs are the <c>ac_cm_*</c> globals (<see cref="Lifestone.Client.Rendering.SurfaceLighting.CreatureMode"/>),
/// so the world's sun, ambient and fog cannot reach it.</item>
/// <item><see cref="DynamicObject"/> and <see cref="DynamicCellObject"/>: the same two rules for objects that move (the player,
/// server objects), drawn as instances of a MultiMesh. The statics' per-part values cannot be baked into their vertices, so
/// each instance carries them in <c>INSTANCE_CUSTOM</c> (docs/render-contract.md §2.1, <c>SurfaceLightingCode</c>): x the
/// cell's slot in the cell-state table, y the row of its light set in the <c>ac_object_lights</c> table, z a luminosity
/// override (0 keeps the Surface's) and w the object's translucency.</item>
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

    /// <summary>
    /// Creature mode's objects: luminosity + the creature scene's ambient + its one distant light; no sun, no world
    /// ambient, no fog. Variants carrying it are made with <see cref="SurfaceShaderVariant.WithLighting"/>.
    /// </summary>
    CreatureMode,

    /// <summary>
    /// A moving object outdoors (the sun and the world ambient, as <see cref="Object"/>), per instance: the luminosity
    /// override <c>INSTANCE_CUSTOM.z</c> and the translucency <c>INSTANCE_CUSTOM.w</c> (applied to blended surfaces).
    /// </summary>
    DynamicObject,

    /// <summary>
    /// A moving object in an EnvCell (no sun, as <see cref="CellObject"/>): world ambient plus the up-to-eight point lights of
    /// its light set (<c>INSTANCE_CUSTOM.y</c>), summed per vertex because the object moves; the cell's doorway clipping and
    /// front depth through the cell slot <c>INSTANCE_CUSTOM.x</c>; luminosity and translucency as <see cref="DynamicObject"/>.
    /// </summary>
    DynamicCellObject,
}
