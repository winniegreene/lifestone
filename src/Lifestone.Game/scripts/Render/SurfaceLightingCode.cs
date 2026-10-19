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
/// <para><b>Creature mode</b> (<see cref="SurfaceLighting.CreatureMode"/>, <see cref="CreatureAmbient"/> and the other
/// <c>ac_cm_*</c> globals): <c>CreatureMode::Render</c> (11.4186 0x004529D0, 11.6096 0x00452A10) draws its objects with the
/// sun, the static lights and the dynamic lights all switched off, lit only by the scene's ambient
/// (<c>CreatureMode::CreatureMode</c>, 11.4186 0x00454390, 11.6096 0x00454430) and the distant light
/// <c>CreatureMode::AddLight</c> (0x004555B0, 0x00455650) added. D3D's fixed function adds the light's diffuse
/// (colour x intensity) scaled by max(0, N.L) to the ambient and the emissive, clamped per vertex, like the world's sun
/// (<c>SurfaceShaders</c>); the difference is only where the inputs come from: globals of their own, so the world's
/// time of day, indoor ambient and fog can never leak into a creature-mode view (global shader uniforms are shared by
/// every viewport, so a separate <c>World3D</c> does not isolate lighting; <c>--portal-space-checks</c> pins this). Fog
/// is never applied.</para>
/// <para><b>Moving objects</b> (<see cref="SurfaceLighting.DynamicObject"/>, <see cref="SurfaceLighting.DynamicCellObject"/>; M2 W2-pre(c)):
/// the player and the server's objects are MultiMesh instances, and what the statics bake into their vertices or their mesh
/// arrives per instance in <c>INSTANCE_CUSTOM</c>: x the cell's slot in <see cref="CellStates"/> (0 = none), y one plus the
/// row of the object's light set in <see cref="ObjectLights"/> (0 = none), z a luminosity override (0 keeps the Surface's),
/// w the translucency (0 = opaque; scales the alpha of blended surfaces. An opaque surface has no blend to scale, so while an instance's
/// translucency is strictly between 0 and 1 it is drawn with the blended twin of its mesh, <c>RenderResources.FadeTwin</c>, D45 and D63; the batches in
/// <c>PlayerView</c> and <c>ServerObjectsLayer</c> hand an instance over). All four are 0 for a plain instance. Outdoors the rule is
/// <see cref="SurfaceLighting.Object"/>'s (sun and ambient). In a cell it is <see cref="SurfaceLighting.CellObject"/>'s, except that
/// the object moves, so the up-to-eight lights of <c>Render::minimize_object_lighting</c> are summed per vertex in the shader
/// from the light set (two RGBA32F texels per light: position in Godot world space and reach; colour times intensity)
/// with the formula <c>CellObjectLighting.Light</c> uses on the CPU for statics.</para>
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

    /// <summary>
    /// Global sampler2D: the light sets of moving objects, one row per object, <see cref="ObjectLightRowWidth"/> RGBA32F texels
    /// wide: for light i, texel 2i = (position in Godot world space, reach in metres; 0 ends the list) and texel 2i + 1 =
    /// (colour times intensity, 0).
    /// </summary>
    public const string ObjectLights = "ac_object_lights";

    /// <summary>Width of the light-set table: two texels for each of the eight lights.</summary>
    public const int ObjectLightRowWidth = ObjectLightSets.TexelsPerLight * ObjectLightSets.MaxLights;

    /// <summary>True for the lighting rules of moving objects, whose per-instance values come from <c>INSTANCE_CUSTOM</c>.</summary>
    public static bool IsDynamic(SurfaceLighting lighting) => lighting is SurfaceLighting.DynamicObject or SurfaceLighting.DynamicCellObject;

    /// <summary>
    /// Global sampler2D: the dynamic lights of the frame, <see cref="DynamicLightTexels.Width"/> x 1 RGBA32F texels
    /// (<see cref="DynamicLightTexels"/>): for light i, texel 2i = (position in Godot world space, reach in metres; 0 ends the list)
    /// and texel 2i + 1 = (colour times intensity, 0). Written every frame by <c>DynamicLightTable</c>; read per vertex by the
    /// EnvCell and CellObject variants (<see cref="DynamicLightFunction"/>).
    /// </summary>
    public const string DynamicLights = "ac_dynamic_lights";

    /// <summary>
    /// The global declaration and the function of the cell glow: every dynamic light as a hardware point light on the cell's polygons
    /// (<c>Render::minimize_envcell_lighting</c> 0x0054CD80; <c>PrimD3DRender::config_hardware_light</c> 0x0059BD40: colour times
    /// intensity, range falloff times 1.5, attenuation 1/d), summed per vertex like <see cref="ObjectLightSum"/>:
    /// <c>min(1, sum of colour x intensity x max(0, N.L) / d)</c> over the lights within reach. A texture narrower than the table
    /// (the global before anything bound it) has no lights.
    /// </summary>
    public static string DynamicLightFunction
    {
        get
        {
            var sb = new StringBuilder();
            sb.AppendLine(FormattableString.Invariant($"global uniform sampler2D {DynamicLights};"));
            sb.AppendLine("vec3 ac_dynamic_light_sum(vec3 p, vec3 n) {");
            sb.AppendLine("\tvec3 sum = vec3(0.0);");
            sb.AppendLine(FormattableString.Invariant($"\tif (textureSize({DynamicLights}, 0).x < {DynamicLightTexels.Width}) {{"));
            sb.AppendLine("\t\treturn sum;");
            sb.AppendLine("\t}");
            sb.AppendLine(FormattableString.Invariant($"\tfor (int i = 0; i < {DynamicLightTexels.MaxLights}; i++) {{"));
            sb.AppendLine(FormattableString.Invariant($"\t\tvec4 at = texelFetch({DynamicLights}, ivec2(i * {DynamicLightTexels.TexelsPerLight}, 0), 0);"));
            sb.AppendLine("\t\tif (at.w <= 0.0) {");
            sb.AppendLine("\t\t\tbreak;");
            sb.AppendLine("\t\t}");
            sb.AppendLine("\t\tvec3 to = at.xyz - p;");
            sb.AppendLine("\t\tfloat d = length(to);");
            sb.AppendLine("\t\tif (d > at.w || d < 1e-6) {");
            sb.AppendLine("\t\t\tcontinue;");
            sb.AppendLine("\t\t}");
            sb.AppendLine("\t\tfloat c = dot(n, to / d);");
            sb.AppendLine("\t\tif (c > 0.0) {");
            sb.AppendLine(FormattableString.Invariant($"\t\t\tsum += texelFetch({DynamicLights}, ivec2(i * {DynamicLightTexels.TexelsPerLight} + 1, 0), 0).rgb * (c / d);"));
            sb.AppendLine("\t\t}");
            sb.AppendLine("\t}");
            sb.AppendLine("\treturn clamp(sum, 0.0, 1.0);");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }

    /// <summary>
    /// The <c>vertex()</c> lines that set <c>ac_dyn</c>, the dynamic lights' per-vertex sum at this vertex in Godot world space
    /// (a zero normal, CUSTOM0.x, gets no diffuse term, as in <see cref="ObjectLightSum"/>).
    /// </summary>
    public const string DynamicLightVertexTerm =
        "\t// The dynamic lights (the cell glow): every one a hardware point light on the cell's polygons, summed per vertex.\n" +
        "\tvec3 ac_dyn = vec3(0.0);\n" +
        "\tif (CUSTOM0.x < 0.5) {\n" +
        "\t\tac_dyn = ac_dynamic_light_sum((MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz, normalize(MODEL_NORMAL_MATRIX * NORMAL));\n" +
        "\t}\n";

    /// <summary>vec3 global: creature mode's ambient light (<c>CreatureMode::CreatureMode</c>: 0.3 white), raw colour units.</summary>
    public const string CreatureAmbient = "ac_cm_ambient";

    /// <summary>
    /// vec3 global: the unit vector <i>towards</i> creature mode's distant light, Godot world axes (the same sense as
    /// <c>ac_sun_dir</c>: N.L is positive for a surface facing the light). Set through <c>CreatureModeLighting</c>.
    /// </summary>
    public const string CreatureLightDirection = "ac_cm_light_dir";

    /// <summary>
    /// vec3 global: the distant light's diffuse colour times its intensity (<c>CreatureMode::AddLight</c>: white x 2.0 for
    /// the portal tunnel, so it saturates what it faces), raw colour units.
    /// </summary>
    public const string CreatureLightColor = "ac_cm_light_color";

    /// <summary>True for the lighting rules that draw EnvCells and what stands in them (they use the cell-state machinery).</summary>
    public static bool IsCellRule(SurfaceLighting lighting) =>
        lighting is SurfaceLighting.EnvCell or SurfaceLighting.CellObject or SurfaceLighting.DynamicCellObject;

    /// <summary>The global declarations the creature-mode variant needs (declared in <c>project.godot [shader_globals]</c>).</summary>
    public static string CreatureModeDeclarations =>
        "global uniform vec3 ac_cm_ambient;\n" +
        "global uniform vec3 ac_cm_light_dir;\n" +
        "global uniform vec3 ac_cm_light_color;\n";

    /// <summary>
    /// The <c>vertex()</c> line that sets <c>v_light</c> for creature mode: emissive + the creature scene's ambient + its
    /// distant light, clamped per vertex. <c>n_dot_l</c> is already computed against <see cref="CreatureLightDirection"/>
    /// (<see cref="SurfaceShaders.LightDirectionName"/>). No sun, no world ambient, no fog.
    /// </summary>
    public static string CreatureModeVertexLight =>
        "\t// Creature mode: no sun, no world ambient, no fog (CreatureMode::Render); emissive + its own ambient + its distant light.\n" +
        "\tv_light = clamp(vec3(luminosity) + ac_cm_ambient * diffuse + ac_cm_light_color * diffuse * n_dot_l, 0.0, 1.0);\n";

    /// <summary>The extra global declarations and helpers the cell variants need (globals declared in <c>project.godot [shader_globals]</c>).</summary>
    public static string ShaderDeclarations => Declarations(lighting: SurfaceLighting.CellObject);

    /// <summary>The global declarations and helpers of the cell variant <paramref name="lighting"/>.</summary>
    public static string Declarations(SurfaceLighting lighting)
    {
        var sb = new StringBuilder();
        sb.AppendLine("global uniform vec3 ac_indoor_ambient;");
        sb.AppendLine("global uniform bool ac_indoor_ambient_on;");
        sb.AppendLine("global uniform sampler2D ac_doorway_planes;");
        sb.AppendLine("global uniform sampler2D ac_cell_states;");
        if (lighting == SurfaceLighting.DynamicCellObject)
        {
            sb.AppendLine($"global uniform sampler2D {ObjectLights};"); // moving objects' light sets
        }
        else
        {
            sb.AppendLine(World.Objects.CellObjectLodShader.Declaration); // objects track (M1 wave 3)
            sb.Append(DynamicLightFunction); // M3 4.1: the cell glow (EnvCell and CellObject variants)
        }

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

    /// <summary>
    /// The <c>vertex()</c> lines of a moving object outdoors (<see cref="SurfaceLighting.DynamicObject"/>) that set <c>v_light</c>:
    /// <see cref="SurfaceLighting.Object"/>'s rule with the instance's luminosity override (<c>INSTANCE_CUSTOM.z</c>, 0 keeps the
    /// Surface's). <c>n_dot_l</c> is already computed.
    /// </summary>
    public static string DynamicObjectVertexLight =>
        "\t// Moving object outdoors: the sun and the world ambient; INSTANCE_CUSTOM.z overrides the Surface's luminosity (0 = keep).\n" +
        "\tfloat ac_lum = INSTANCE_CUSTOM.z > 0.0 ? INSTANCE_CUSTOM.z : luminosity;\n" +
        "\tv_light = clamp(vec3(ac_lum) + ac_ambient * diffuse + ac_sun_color * diffuse * n_dot_l, 0.0, 1.0);\n";

    /// <summary>
    /// The <c>vertex()</c> lines that sum the hardware point lights of a moving object's light set (INSTANCE_CUSTOM.y is one plus
    /// its row of <see cref="ObjectLights"/>, 0 = none) into <c>ac_lights</c>, per vertex in Godot world space:
    /// <c>min(1, sum of colour x intensity x max(0, N.L) / d)</c> over the lights within reach, as <c>CellObjectLighting.Light</c> does for
    /// statics (D3D9 point lights with attenuation (0, 1, 0)). A zero normal (CUSTOM0.x) gets no diffuse term.
    /// </summary>
    public static string ObjectLightSum
    {
        get
        {
            var sb = new StringBuilder();
            sb.AppendLine("\t// The object's light set (Render::minimize_object_lighting: at most 8 lights), summed per vertex.");
            sb.AppendLine("\tvec3 ac_lights = vec3(0.0);");
            sb.AppendLine("\tint ac_light_row = int(INSTANCE_CUSTOM.y + 0.5) - 1;");
            sb.AppendLine("\tif (ac_light_row >= 0 && CUSTOM0.x < 0.5) {");
            sb.AppendLine("\t\tvec3 ac_p = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;");
            sb.AppendLine("\t\tvec3 ac_n = normalize(MODEL_NORMAL_MATRIX * NORMAL);");
            sb.AppendLine(FormattableString.Invariant($"\t\tfor (int i = 0; i < {ObjectLightSets.MaxLights}; i++) {{"));
            sb.AppendLine(FormattableString.Invariant($"\t\t\tvec4 ac_at = texelFetch({ObjectLights}, ivec2(i * {ObjectLightSets.TexelsPerLight}, ac_light_row), 0);"));
            sb.AppendLine("\t\t\tif (ac_at.w <= 0.0) {");
            sb.AppendLine("\t\t\t\tbreak;");
            sb.AppendLine("\t\t\t}");
            sb.AppendLine("\t\t\tvec3 ac_to = ac_at.xyz - ac_p;");
            sb.AppendLine("\t\t\tfloat ac_d = length(ac_to);");
            sb.AppendLine("\t\t\tif (ac_d > ac_at.w || ac_d < 1e-6) {");
            sb.AppendLine("\t\t\t\tcontinue;");
            sb.AppendLine("\t\t\t}");
            sb.AppendLine("\t\t\tfloat ac_cos = dot(ac_n, ac_to / ac_d);");
            sb.AppendLine("\t\t\tif (ac_cos > 0.0) {");
            sb.AppendLine(FormattableString.Invariant($"\t\t\t\tac_lights += texelFetch({ObjectLights}, ivec2(i * {ObjectLightSets.TexelsPerLight} + 1, ac_light_row), 0).rgb * (ac_cos / ac_d);"));
            sb.AppendLine("\t\t\t}");
            sb.AppendLine("\t\t}");
            sb.AppendLine("\t\tac_lights = clamp(ac_lights, 0.0, 1.0);");
            sb.AppendLine("\t}");
            return sb.ToString();
        }
    }

    /// <summary>The <c>vertex()</c> lines that set <c>v_light</c>, the cell state and the position for <paramref name="lighting"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="SurfaceLighting.Object"/>, <see cref="SurfaceLighting.DynamicObject"/> and <see cref="SurfaceLighting.CreatureMode"/> (the caller emits their own rules) or an unknown value.</exception>
    public static string VertexLight(SurfaceLighting lighting)
    {
        var sb = new StringBuilder();
        if (lighting == SurfaceLighting.DynamicCellObject)
        {
            sb.AppendLine("\t// The cell's draw state: INSTANCE_CUSTOM.x = its slot in ac_cell_states (0 = none; docs/interiors.md §3.4).");
            sb.AppendLine("\tint ac_slot = int(INSTANCE_CUSTOM.x + 0.5);");
        }
        else
        {
            sb.AppendLine("\t// The cell's draw state: CUSTOM1.a = its slot in ac_cell_states (0 = none; docs/interiors.md §3.4).");
            sb.AppendLine("\tint ac_slot = int(CUSTOM1.a + 0.5);");
        }

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
                sb.Append(DynamicLightVertexTerm);
                sb.AppendLine("\t// EnvCell: emissive = burned-in static lights (CUSTOM1.rgb), no sun (PView::DrawCells, SetStaticLightingVertexColors); plus the dynamic lights.");
                sb.AppendLine("\tv_light = clamp(CUSTOM1.rgb + ac_dyn + world_ambient * diffuse, 0.0, 1.0);");
                break;
            case SurfaceLighting.CellObject:
                sb.AppendLine("\t// Object inside a cell: no sun (PView::DrawCells); CUSTOM1.rgb = its hardware static lights' clamped sum");
                sb.AppendLine("\t// (minimize_object_lighting, computed per part on the CPU; 0 where no light reaches).");
                sb.Append(DynamicLightVertexTerm);
                sb.AppendLine("\tv_light = clamp(vec3(luminosity) + world_ambient * diffuse + CUSTOM1.rgb + ac_dyn, 0.0, 1.0);");
                break;
            case SurfaceLighting.DynamicCellObject:
                sb.Append(ObjectLightSum);
                sb.AppendLine("\t// Moving object in a cell: no sun (PView::DrawCells); world ambient + its light set; INSTANCE_CUSTOM.z overrides the luminosity.");
                sb.AppendLine("\tfloat ac_lum = INSTANCE_CUSTOM.z > 0.0 ? INSTANCE_CUSTOM.z : luminosity;");
                sb.AppendLine("\tv_light = clamp(vec3(ac_lum) + world_ambient * diffuse + ac_lights, 0.0, 1.0);");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(lighting), lighting, "Only the cell lighting rules are generated here.");
        }

        return sb.ToString();
    }
}
