using System.Text;
using Lifestone.Client.Interiors;
using Lifestone.Client.Rendering;

namespace Lifestone.Game.Render;

/// <summary>
/// Shader code for the non-default <see cref="SurfaceLighting"/> rules (the interiors track's extension of the surface
/// shaders; docs/interiors.md §3.4 and §4, docs/render-contract.md §2.1). <see cref="SurfaceShaders.GenerateCode"/> calls
/// it for every variant whose lighting is not <see cref="SurfaceLighting.Object"/>.
/// </summary>
/// <remarks>
/// <para><b>World ambient.</b> Retail keeps one world ambient (<c>Render::world_lights.ambient_color</c>) for the whole
/// frame, chosen by the viewer's cell (<c>CellManager::ChangePosition</c>): the sky's object ambient when the viewer is
/// outdoors or in a seen_outside EnvCell (that is <c>ac_ambient</c>), and 0.2 white when the viewer is in an EnvCell
/// that is not seen_outside (<c>SmartBox::SetWorldAmbientLight(0.2, 0xFFFFFFFF)</c>). The interiors controller sets
/// <see cref="IndoorAmbientOn"/> in the second case. Nothing outdoors is drawn then (retail does not call
/// <c>LScape::draw</c> from such a cell), so only the cell variants need the switch.</para>
/// <para><b>EnvCell.</b> <c>RenderDeviceD3D::DrawEnvCell</c> burns the static lights into the mesh's vertex colours
/// (<c>D3DPolyRender::SetStaticLightingVertexColors</c>), and because the cell mesh was built with
/// <c>burn_in_static_lights</c> (<c>CEnvCell::UnPack</c> → <c>ConstructMesh(..., 3.0, 1, ...)</c>),
/// <c>D3DPolyRender::RenderMeshSubset</c> takes the D3D emissive colour from the vertex (<c>FromVertex</c>) and
/// material ambient/diffuse = <c>diffuseVal</c> (1.0). The sun is off for cells (<c>PView::DrawCells</c> →
/// <c>Render::useSunlightSet(0)</c>), so the lit colour is <c>clamp(vertex colour + world ambient)</c>; the
/// Surface's luminosity does not reach cells. The burned colour arrives in <c>CUSTOM1.rgb</c> (bytes / 255).</para>
/// <para><b>CellObject.</b> Objects inside cells are drawn in the same pass with the sun off, lit by up to 8 hardware
/// point lights (<c>Render::minimize_object_lighting</c>): <c>clamp(luminosity + world ambient + lights)</c>. The
/// objects never move, so the light sum is computed per part on the CPU (<c>CellObjectLighting</c>) and arrives in
/// <c>CUSTOM1.rgb</c> (0 where no light reaches; docs/interiors.md §4.4).</para>
/// <para><b>Per-cell state without instance uniforms.</b> <c>CUSTOM1.a</c> is the cell's slot in the global
/// <see cref="CellStates"/> table (<see cref="CellDrawState"/>, one RGBA8 texel per slot; a mesh without CUSTOM1 reads
/// slot 0 = no state). Every cell is one mesh holding its objects, so the slot is part of its vertex data. Godot's
/// instance uniforms are not used: they reserve a block of the global shader-uniform buffer for every instance whose
/// material declares one, hidden or not (about 4,000 instances with the default buffer size, shared with every other
/// track), and a dense dungeon has more cells than that.</para>
/// </remarks>
public static class SurfaceLightingCode
{
    /// <summary>vec3 global: the world ambient retail uses while the viewer is in a cell that is not seen_outside (0.2 white).</summary>
    public const string IndoorAmbient = "ac_indoor_ambient";

    /// <summary>bool global: the viewer is in an EnvCell that is not seen_outside (cells use <see cref="IndoorAmbient"/>).</summary>
    public const string IndoorAmbientOn = "ac_indoor_ambient_on";

    /// <summary>
    /// Global sampler2D: one row per building doorway looked into this frame, RGBA32F texels (n.x, n.y, n.z, d) of planes
    /// in Godot world space: column 0 the doorway's plane, columns 1 to <see cref="DoorwayRows.MaxConePlanes"/> the cone
    /// from the eye through it (positive inside; unused columns (0, 0, 0, 1)).
    /// </summary>
    public const string DoorwayPlanes = "ac_doorway_planes";

    /// <summary>
    /// Global sampler2D: the cell-state table, one RGBA8 texel per slot (row-major, <see cref="CellDrawState"/>):
    /// R = 255 draws the cell in front of the landscape, G and B = doorway rows + 1 (0 = none).
    /// </summary>
    public const string CellStates = "ac_cell_states";

    /// <summary>Width and height of the cell-state table: 16,384 slots (the densest landblock has 4,213 cells).</summary>
    public const int CellStateTableSize = 128;

    /// <summary>
    /// How far in front of a doorway's plane (as a fraction of the reversed-Z depth) the cells seen through it are drawn:
    /// their own depth order is kept inside that sliver (docs/interiors.md §3.4).
    /// </summary>
    public const float DoorwayDepthSliver = 0.001f;

    /// <summary>
    /// Width (pixels) of the band outside a doorway's cone where a cell fragment is kept at its own depth instead of
    /// being discarded: the building shell's opening is rasterized separately, so the exact cone and the shell's hole
    /// can disagree by a pixel along the doorway's edges (docs/interiors.md §3.4).
    /// </summary>
    public const float DoorwaySeamPixels = 1.5f;

    /// <summary>The extra global declarations and helpers the cell variants need (globals declared in <c>project.godot [shader_globals]</c>).</summary>
    public static string ShaderDeclarations
    {
        get
        {
            var sb = new StringBuilder();
            sb.AppendLine("global uniform vec3 ac_indoor_ambient;");
            sb.AppendLine("global uniform bool ac_indoor_ambient_on;");
            sb.AppendLine("global uniform sampler2D ac_doorway_planes;");
            sb.AppendLine("global uniform sampler2D ac_cell_states;");
            sb.AppendLine(World.Objects.CellObjectLodShader.Declaration); // objects track (M1 wave 3)
            sb.AppendLine("varying flat ivec2 v_ac_doorways; // doorway rows clipping this cell (-1 = none; a valid row is always in .x)");
            sb.AppendLine();
            sb.AppendLine("// Smallest signed distance (world units, positive inside) of p to the cone planes of doorway row `row`.");
            sb.AppendLine("float ac_cone_distance(int row, vec3 p) {");
            sb.AppendLine("\tfloat m = 1e30;");
            sb.AppendLine($"\tfor (int i = 1; i <= {DoorwayRows.MaxConePlanes}; i++) {{");
            sb.AppendLine("\t\tvec4 pl = texelFetch(ac_doorway_planes, ivec2(i, row), 0);");
            sb.AppendLine("\t\tm = min(m, dot(pl.xyz, p) + pl.w);");
            sb.AppendLine("\t}");
            sb.AppendLine("\treturn m;");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }

    /// <summary>
    /// The first lines of <c>fragment()</c> for cell variants: a cell seen from outdoors through a doorway is clipped to
    /// the doorway's cone (retail clips cells to their portal views) and drawn just in front of the doorway's plane
    /// (retail resets depth inside the doorway polygon before drawing the interior,
    /// <c>D3DPolyRender::DrawPortalPolyInternal(polygon, zClear = 1)</c> from <c>PView::ConstructView(CBldPortal*)</c>),
    /// so terrain inside the building cannot cover the floor; things in front of the doorway still cover the interior.
    /// Within <see cref="DoorwaySeamPixels"/> outside the cone a fragment keeps its own depth: where the building shell
    /// covers it, the shell wins; where the shell's rasterized opening is a pixel wider than the cone, the cell fills the
    /// seam instead of the terrain or sky beyond. Writes <c>DEPTH</c> on every path.
    /// </summary>
    public static string FragmentPrologue()
    {
        var sb = new StringBuilder();
        sb.AppendLine("\t// Seen through a building doorway: clip to its cone, reset depth behind it (PView::ConstructView(CBldPortal*)).");
        sb.AppendLine("\tfloat ac_depth = FRAGCOORD.z;");
        sb.AppendLine("\tif (v_ac_doorways.x >= 0) {");
        sb.AppendLine("\t\tvec3 ac_world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;");
        sb.AppendLine("\t\tvec3 ac_eye = INV_VIEW_MATRIX[3].xyz;");
        sb.AppendLine("\t\tvec3 ac_ray = ac_world - ac_eye;");
        sb.AppendLine("\t\t// The seam band in world units at this distance: pixels x the angle of one pixel (2 / (|P[1][1]| x height);");
        sb.AppendLine("\t\t// Godot's RenderingDevice projection flips Y, so P[1][1] is negative).");
        sb.AppendLine(FormattableString.Invariant($"\t\tfloat ac_band = -{DoorwaySeamPixels:0.0###} * 2.0 / (abs(PROJECTION_MATRIX[1][1]) * VIEWPORT_SIZE.y) * length(ac_ray);"));
        sb.AppendLine("\t\tint ac_row = -1;");
        sb.AppendLine("\t\tbool ac_inside = false;");
        sb.AppendLine("\t\tfloat ac_d = ac_cone_distance(v_ac_doorways.x, ac_world);");
        sb.AppendLine("\t\tif (ac_d >= ac_band) {");
        sb.AppendLine("\t\t\tac_row = v_ac_doorways.x;");
        sb.AppendLine("\t\t\tac_inside = ac_d >= 0.0;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t\tif (!ac_inside && v_ac_doorways.y >= 0) {");
        sb.AppendLine("\t\t\tac_d = ac_cone_distance(v_ac_doorways.y, ac_world);");
        sb.AppendLine("\t\t\tif (ac_d >= 0.0 || (ac_row < 0 && ac_d >= ac_band)) {");
        sb.AppendLine("\t\t\t\tac_row = v_ac_doorways.y;");
        sb.AppendLine("\t\t\t\tac_inside = ac_d >= 0.0;");
        sb.AppendLine("\t\t\t}");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t\tif (ac_row < 0) {");
        sb.AppendLine("\t\t\tdiscard;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t\tif (ac_inside) {");
        sb.AppendLine("\t\t\tvec4 ac_plane = texelFetch(ac_doorway_planes, ivec2(0, ac_row), 0);");
        sb.AppendLine("\t\t\tfloat ac_den = dot(ac_plane.xyz, ac_ray);");
        sb.AppendLine("\t\t\tif (abs(ac_den) > 1e-6) {");
        sb.AppendLine("\t\t\t\tfloat t = -(dot(ac_plane.xyz, ac_eye) + ac_plane.w) / ac_den;");
        sb.AppendLine("\t\t\t\tif (t > 0.0 && t < 1.0) {");
        sb.AppendLine("\t\t\t\t\tvec4 ac_clip = PROJECTION_MATRIX * (VIEW_MATRIX * vec4(ac_eye + ac_ray * t, 1.0));");
        sb.AppendLine(FormattableString.Invariant($"\t\t\t\t\tac_depth = max(ac_depth, ac_clip.z / ac_clip.w + {DoorwayDepthSliver:0.0######} * ac_depth);"));
        sb.AppendLine("\t\t\t\t}");
        sb.AppendLine("\t\t\t}");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");
        sb.AppendLine("\tDEPTH = ac_depth;");
        return sb.ToString();

    }

    /// <summary>The <c>vertex()</c> lines that set <c>v_light</c>, the cell state and the position for <paramref name="lighting"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="SurfaceLighting.Object"/> (the caller emits the default rule) or an unknown value.</exception>
    public static string VertexLight(SurfaceLighting lighting)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\t// The cell's draw state: CUSTOM1.a = its slot in ac_cell_states (0 = none; docs/interiors.md §3.4).");
        sb.AppendLine("\tint ac_slot = int(CUSTOM1.a + 0.5);");
        sb.AppendLine("\tvec4 ac_state = vec4(0.0);");
        sb.AppendLine("\tif (ac_slot > 0) {");
        sb.AppendLine("\t\tint ac_width = textureSize(ac_cell_states, 0).x;");
        sb.AppendLine("\t\tac_state = texelFetch(ac_cell_states, ivec2(ac_slot % ac_width, ac_slot / ac_width), 0);");
        sb.AppendLine("\t}");
        sb.AppendLine("\tv_ac_doorways = ivec2(int(ac_state.g * 255.0 + 0.5) - 1, int(ac_state.b * 255.0 + 0.5) - 1);");
        if (lighting == SurfaceLighting.CellObject)
        {
            sb.Append(World.Objects.CellObjectLodShader.VertexPrologue); // objects track (M1 wave 3): statics' LOD and draw frames
        }

        sb.AppendLine("\t// Cells of the viewer's walk draw over the landscape (PView::DrawCells clears Z first): reversed-Z depth into [0.5, 1].");
        sb.AppendLine("\tvec4 clip = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));");
        sb.AppendLine("\tif (ac_state.r > 0.5) {");
        sb.AppendLine("\t\tclip.z = 0.5 * (clip.z + clip.w);");
        sb.AppendLine("\t}");
        if (lighting == SurfaceLighting.CellObject)
        {
            sb.Append(World.Objects.CellObjectLodShader.ClipEpilogue);
        }

        sb.AppendLine("\tPOSITION = clip;");
        sb.AppendLine("\t// World ambient: CellManager::ChangePosition (0.2 white in a cell that is not seen_outside, else the sky's).");
        sb.AppendLine("\tvec3 world_ambient = ac_indoor_ambient_on ? ac_indoor_ambient : ac_ambient;");
        switch (lighting)
        {
            case SurfaceLighting.EnvCell:
                sb.AppendLine("\t// EnvCell: emissive = burned-in static lights (CUSTOM1.rgb), no sun (PView::DrawCells, SetStaticLightingVertexColors).");
                sb.AppendLine("\tv_light = clamp(CUSTOM1.rgb + world_ambient * diffuse, 0.0, 1.0);");
                break;
            case SurfaceLighting.CellObject:
                sb.AppendLine("\t// Object inside a cell: no sun (PView::DrawCells); CUSTOM1.rgb = its hardware static lights' clamped sum");
                sb.AppendLine("\t// (minimize_object_lighting, computed per part on the CPU; 0 where no light reaches).");
                sb.AppendLine("\tv_light = clamp(vec3(luminosity) + world_ambient * diffuse + CUSTOM1.rgb, 0.0, 1.0);");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(lighting), lighting, "Only the cell lighting rules are generated here.");
        }

        return sb.ToString();
    }
}
