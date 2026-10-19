using System.Text;
using Godot;
using Lifestone.Client.Rendering;
using Lifestone.Data.Geometry;
using Lifestone.Game.Core;

namespace Lifestone.Game.Render;

/// <summary>
/// Generates and caches Lifestone's surface shaders: retail-style fixed-function lighting and fog instead of Godot's
/// PBR (docs/render-contract.md). One <see cref="Shader"/> per <see cref="SurfaceShaderVariant"/>, created on the
/// main thread.
/// </summary>
/// <remarks>
/// <para><b>Lighting</b> (D3D9 fixed function as retail sets it up for mesh subsets; <c>D3DPolyRender::RenderMeshSubset</c>,
/// acclient.c:454633; <c>copyVert</c>, acclient.c:454109; <c>PrimD3DRender::UpdateLightsInternal</c>):</para>
/// <list type="bullet">
/// <item>Per vertex (Gouraud), clamped to [0, 1]: <c>emissive + ambient + sun_color * diffuse * max(0, N·L)</c>.</item>
/// <item><c>emissive</c> = the Surface's luminosity when it is &gt; 0 (RenderMeshSubset writes it into the D3D material),
/// else the part material's (0 unless the sky track sets one).</item>
/// <item>Material ambient and diffuse are white: <c>copyVert</c> writes vertex colour 0xFFFFFF with alpha
/// (1 - translucency) x 255 and the colour sources stay FromVertex. <c>Surface.Diffuse</c> only reaches
/// <c>Render::diffuse</c>, which no D3D path reads, so it is not used. The <c>diffuse</c> parameter (default 1) is the
/// part material's <c>CMaterial</c> diffuse for callers that need it.</item>
/// <item>A zero-length normal (16,043 stored normals) gives ambient + emissive only, as D3D does (N·L = 0). Godot cannot
/// store a zero normal (its octahedral encoding turns it into (0, 0, -1), AC north), so <see cref="MeshFactory"/> marks
/// those vertices in CUSTOM0.x and the shader tests that flag, not the normal's length.</item>
/// </list>
/// <para><b>Fog</b>: retail linear range fog per vertex (<c>RenderDeviceD3D::SetDefaultD3DStates</c>: FOGVERTEXMODE
/// LINEAR, RANGEFOGENABLE): <c>f = clamp((end - d) / (end - start))</c> with d the eye-to-vertex distance,
/// interpolated; <c>colour = mix(fog_color, colour, f)</c>. Disabled for Additive surfaces.</para>
/// <para><b>Colour space</b>: retail does every step on raw texture bytes. Textures are sampled without
/// <c>source_color</c> and all arithmetic happens on raw values. The result goes out through <c>ac_output</c>
/// (<see cref="RenderGlobals.OutputFunctionCode"/>): unchanged in byte space, where blends are retail's byte arithmetic
/// and <see cref="ByteSpaceBlending"/> converts the finished frame once (the default); converted with
/// <c>srgb_to_linear</c> in linear space (<c>--blend-space=linear</c>), where Godot blends in linear light. Either way
/// the Linear tonemapper at exposure 1 puts an opaque surface's retail bytes on screen.</para>
/// <para><b>Mipmaps</b>: Godot's mip chain is full-length; retail creates at most 4 levels
/// (<c>ImgTex::CreateD3DTexture</c>, docs/textures.md §7). The shader clamps the sampled level to
/// <c>ac_max_mip_level</c> (3 by default) with <c>textureQueryLod</c>/<c>textureLod</c>.</para>
/// </remarks>
public sealed class SurfaceShaders
{
    private readonly Dictionary<SurfaceShaderVariant, Shader> _shaders = new();
    private readonly Dictionary<SurfaceShaderVariant, ShaderMaterial> _warm = new();

    /// <summary>Number of compiled variants.</summary>
    public int Count => _shaders.Count;

    /// <summary>The shader for <paramref name="variant"/>, generated on first use. Main thread.</summary>
    public Shader Get(SurfaceShaderVariant variant)
    {
        if (!_shaders.TryGetValue(variant, out var shader))
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            DynamicLightTable.Declare(); // M3 4.1 r2: the cell variants name the dynamic-light global; it exists before any shader is made
            shader = new Shader { Code = GenerateCode(variant), ResourceName = $"lifestone_surface_{variant.Name}" };
            _shaders.Add(variant, shader);
            Log.Debug("render", "surface shader created", ("variant", variant.Name), ("ms", System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds));
        }

        return shader;
    }

    /// <summary>
    /// Main thread, at start-up: compiles every given variant now. Godot compiles a spatial shader when the first
    /// material using it is set up, which costs 7-11 ms per variant on the main thread (measured); done inside an attach
    /// step that blows the frame budget, so the world's variants are compiled once before streaming starts. One material
    /// per variant is kept so the compiled shader stays referenced.
    /// </summary>
    /// <returns>Variants compiled by this call.</returns>
    public int WarmUp(IEnumerable<SurfaceShaderVariant> variants)
    {
        ArgumentNullException.ThrowIfNull(variants);
        var compiled = 0;
        foreach (var variant in variants)
        {
            if (_warm.ContainsKey(variant))
            {
                continue;
            }

            // The same calls MaterialFactory makes for a real surface.
            var material = new ShaderMaterial { Shader = Get(variant), ResourceName = $"lifestone_warmup_{variant.Name}" };
            material.SetShaderParameter("material_alpha", 1f);
            material.SetShaderParameter("luminosity", 0f);
            material.SetShaderParameter("alpha_ref", 200f);
            _warm.Add(variant, material);
            compiled++;
        }

        return compiled;
    }

    /// <summary>
    /// The global the vertex stage takes N.L against: <c>ac_sun_dir</c> (towards the sun), or creature mode's own
    /// <see cref="SurfaceLightingCode.CreatureLightDirection"/> (towards its distant light).
    /// </summary>
    public static string LightDirectionName(SurfaceLighting lighting) =>
        lighting == SurfaceLighting.CreatureMode ? SurfaceLightingCode.CreatureLightDirection : RenderGlobals.SunDirection;

    /// <summary>The Godot shading-language source of a variant (pure; testable without the engine).</summary>
    public static string GenerateCode(SurfaceShaderVariant v)
    {
        if (v.Lighting == SurfaceLighting.CreatureMode && (v.Fog || v.Detail != SurfaceDetail.None))
        {
            throw new InvalidOperationException(
                $"TODO(render): variant {v.Name} asks for fog or a detail pass creature mode never draws; build it with SurfaceShaderVariant.WithLighting(CreatureMode).");
        }

        var modes = new List<string> { "unshaded", "fog_disabled", "specular_disabled", "shadows_disabled" };
        modes.Add(v.Cull == SurfaceCull.Back ? "cull_back" : "cull_disabled");
        switch (v.Blend)
        {
            case SurfaceBlend.Opaque:
                modes.Add("depth_draw_opaque");
                break;
            case SurfaceBlend.Mix:
                modes.Add("blend_mix");
                break;
            case SurfaceBlend.Premultiplied:
                modes.Add("blend_premul_alpha");
                break;
            default:
                modes.Add("blend_add");
                break;
        }

        if (v.Blend != SurfaceBlend.Opaque)
        {
            modes.Add(v.DepthWrite ? "depth_draw_always" : "depth_draw_never");
        }

        var sb = new StringBuilder();
        sb.AppendLine("// Generated by Lifestone.Game.Render.SurfaceShaders. Contract: docs/render-contract.md.");
        sb.AppendLine($"// Variant: {v.Name}");
        sb.AppendLine("shader_type spatial;");
        sb.AppendLine($"render_mode {string.Join(", ", modes)};");
        sb.AppendLine();
        sb.AppendLine(RenderGlobals.ShaderDeclarations);
        if (v.Lighting == SurfaceLighting.CreatureMode)
        {
            sb.AppendLine(SurfaceLightingCode.CreatureModeDeclarations); // M2 portal view: creature mode's own light and ambient
        }
        else if (v.Lighting == SurfaceLighting.DynamicCellObject)
        {
            sb.AppendLine(SurfaceLightingCode.Declarations(v.Lighting)); // M2 moving objects in cells: the cell machinery and the light sets
        }
        else if (v.Lighting is not (SurfaceLighting.Object or SurfaceLighting.DynamicObject))
        {
            sb.AppendLine(SurfaceLightingCode.ShaderDeclarations); // interiors extension (docs/interiors.md §4)
        }

        if (v.Textured)
        {
            var repeat = v.Addressing == TextureAddressMode.Wrap ? "repeat_enable" : "repeat_disable";
            sb.AppendLine($"uniform sampler2D {MaterialFactory.TextureParameter} : filter_linear_mipmap, {repeat};");
        }
        else
        {
            sb.AppendLine("uniform vec4 solid_color = vec4(1.0);   // retail's 1x1 solid texture: RGB, alpha (1 - t) x 255");
        }

        if (v.Detail != SurfaceDetail.None)
        {
            if (!v.CanCarryDetail)
            {
                throw new InvalidOperationException($"TODO(render): variant {v.Name} asks for a detail pass retail never draws on it.");
            }

            // Render-fidelity fix (docs/render-contract.md §2.4): retail's detail surface, WRAP and linear filtering
            // (ACRender::SetDetailSurfaceInternal); detail UV = tiling x UV (D3DPolyRender::SetDetailTiling).
            sb.AppendLine($"uniform sampler2D {MaterialFactory.DetailTextureParameter} : filter_linear_mipmap, repeat_enable;");
            sb.AppendLine($"uniform float {MaterialFactory.DetailTilingParameter} = 4.0;");
        }

        sb.AppendLine("uniform float material_alpha = 1.0;     // vertex alpha: (1 - translucency) x 255 truncated, / 255");
        sb.AppendLine("uniform float luminosity = 0.0;         // D3D material emissive (grey)");
        sb.AppendLine("uniform float diffuse = 1.0;            // material diffuse/ambient (white for mesh subsets)");
        sb.AppendLine("uniform float alpha_ref = 200.0;        // D3DRS_ALPHAREF, GREATEREQUAL");
        sb.AppendLine();
        sb.AppendLine("varying vec3 v_light;");
        var instanceAlpha = SurfaceLightingCode.IsDynamic(v.Lighting) && v.Blend != SurfaceBlend.Opaque;
        if (instanceAlpha)
        {
            sb.AppendLine("varying float v_instance_alpha;   // 1 - the instance's translucency (INSTANCE_CUSTOM.w) on blended surfaces");
        }

        if (v.Fog)
        {
            sb.AppendLine("varying float v_fog;");
        }

        sb.AppendLine();
        sb.AppendLine(RenderGlobals.OutputFunctionCode);
        sb.AppendLine("void vertex() {");
        sb.AppendLine("\tfloat n_dot_l = 0.0;");
        sb.AppendLine("\t// CUSTOM0.x = 1 marks a zero AC normal (MeshFactory); NORMAL cannot, it is octahedral-encoded (always unit).");
        sb.AppendLine("\tif (CUSTOM0.x < 0.5) {");
        sb.AppendLine($"\t\tn_dot_l = max(dot(normalize(MODEL_NORMAL_MATRIX * NORMAL), {LightDirectionName(v.Lighting)}), 0.0);");
        sb.AppendLine("\t}");
        if (v.Lighting == SurfaceLighting.Object)
        {
            sb.AppendLine("\tv_light = clamp(vec3(luminosity) + ac_ambient * diffuse + ac_sun_color * diffuse * n_dot_l, 0.0, 1.0);");
        }
        else if (v.Lighting == SurfaceLighting.DynamicObject)
        {
            sb.Append(SurfaceLightingCode.DynamicObjectVertexLight); // M2 moving objects outdoors
        }
        else if (v.Lighting == SurfaceLighting.CreatureMode)
        {
            sb.Append(SurfaceLightingCode.CreatureModeVertexLight);
        }
        else
        {
            sb.Append(SurfaceLightingCode.VertexLight(v.Lighting)); // interiors extension (docs/interiors.md §4)
        }

        if (instanceAlpha)
        {
            sb.AppendLine("\tv_instance_alpha = 1.0 - clamp(INSTANCE_CUSTOM.w, 0.0, 1.0);");
        }

        if (v.Fog)
        {
            sb.AppendLine("\tfloat d = length((MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz);");
            sb.AppendLine("\tv_fog = ac_fog_enabled ? clamp((ac_fog_end - d) / max(ac_fog_end - ac_fog_start, 1e-4), 0.0, 1.0) : 1.0;");
        }

        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("void fragment() {");
        if (SurfaceLightingCode.IsCellRule(v.Lighting))
        {
            sb.Append(SurfaceLightingCode.FragmentPrologue()); // interiors extension (docs/interiors.md §3.4)
        }

        if (v.Textured)
        {
            sb.AppendLine("\tfloat lod = clamp(textureQueryLod(surface_texture, UV).y, 0.0, ac_max_mip_level);");
            sb.AppendLine("\tvec4 texel = textureLod(surface_texture, UV, lod);");
        }
        else
        {
            sb.AppendLine("\tvec4 texel = solid_color;");
        }

        sb.AppendLine(instanceAlpha
            ? "\tfloat a = texel.a * material_alpha * v_instance_alpha;   // TEXOP_MODULATE of texture and vertex alpha; the object's translucency"
            : "\tfloat a = texel.a * material_alpha;   // TEXOP_MODULATE of texture and vertex alpha");
        if (v.AlphaTest)
        {
            // D3D compares the 8-bit alpha; round to the nearest byte before comparing.
            sb.AppendLine("\tif (floor(a * 255.0 + 0.5) < alpha_ref) {");
            sb.AppendLine("\t\tdiscard;");
            sb.AppendLine("\t}");
        }

        sb.AppendLine("\tvec3 c = texel.rgb * v_light;          // TEXOP_MODULATE of texture and lit vertex colour");
        if (v.Fog)
        {
            sb.AppendLine("\tc = mix(ac_fog_color, c, v_fog);");
        }

        if (v.Detail != SurfaceDetail.None)
        {
            // Retail draws the opaque subset again over itself (RenderMeshSubset, single-pass detailing off): the detail
            // texel MODULATEd with the same lit vertex colour and fogged like the base, blended DESTCOLOR/INVSRCALPHA, so
            // the framebuffer becomes c x (detail x light + 1 - detail alpha), saturated by the 8-bit buffer. On an
            // opaque base that is exact in one pass.
            sb.AppendLine($"\tvec2 duv = UV * {MaterialFactory.DetailTilingParameter};");
            sb.AppendLine($"\tfloat dlod = clamp(textureQueryLod({MaterialFactory.DetailTextureParameter}, duv).y, 0.0, ac_max_mip_level);");
            sb.AppendLine($"\tvec4 detail = textureLod({MaterialFactory.DetailTextureParameter}, duv, dlod);");
            sb.AppendLine("\tvec3 dc = detail.rgb * v_light;       // detail pass: TEXOP_MODULATE with the lit vertex colour");
            if (v.Fog)
            {
                sb.AppendLine("\tdc = mix(ac_fog_color, dc, v_fog);    // the detail pass is fogged like the base");
            }

            sb.AppendLine("\tc = min(c * (dc + vec3(1.0 - detail.a * material_alpha)), vec3(1.0)); // DESTCOLOR/INVSRCALPHA");
        }

        sb.AppendLine("\tALBEDO = ac_output(c);                // raw bytes in byte space, else linear (render-contract §2.3)");
        switch (v.Blend)
        {
            case SurfaceBlend.Mix:
            case SurfaceBlend.Premultiplied:
            case SurfaceBlend.AddSourceAlpha:
                sb.AppendLine("\tALPHA = a;");
                break;
            case SurfaceBlend.AddOne:
                sb.AppendLine("\tALPHA = 1.0;");
                break;
            case SurfaceBlend.AddInverseSourceAlpha:
                sb.AppendLine("\tALPHA = 1.0 - a;");
                break;
        }

        sb.AppendLine("}");
        return sb.ToString();
    }
}
