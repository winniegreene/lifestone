using Godot;
using Lifestone.Client.Rendering;
using Lifestone.Game.Core;

namespace Lifestone.Game.Render.Particles;

/// <summary>
/// The surface shaders of the render contract (<see cref="SurfaceShaders.GenerateCode"/>: retail fixed-function lighting,
/// fog, blend and alpha test) with one addition for instanced particles: a per-instance part material alpha read from
/// <c>INSTANCE_CUSTOM</c>.
/// </summary>
/// <remarks>
/// <para>A particle is a <c>CPhysicsPart</c> whose translucency changes every frame. Retail gives such a part a
/// <c>CMaterial</c> of alpha 1 − t, which replaces the surface's vertex alpha (docs/spell-viewer.md §3,
/// <see cref="Lifestone.Client.Effects.PartTranslucencyRules"/>). Instances carry <c>INSTANCE_CUSTOM.x</c> = 1 − t and
/// <c>INSTANCE_CUSTOM.y</c> = 1 when that material applies; otherwise the surface's <c>material_alpha</c> stays.
/// Everything else is the core's generated code, unchanged, so lighting and fog stay exactly the contract's.</para>
/// <para>The additions are spliced in at three anchors of the core code; if the core generator changes so that an anchor
/// is missing, <see cref="GenerateCode"/> throws (loud, at the first particle) rather than drawing something else.</para>
/// </remarks>
public static class ParticleShaders
{
    /// <summary>Core line after which the varyings are declared.</summary>
    public const string DeclarationAnchor = "varying vec3 v_light;";

    /// <summary>Core vertex() opening.</summary>
    public const string VertexAnchor = "void vertex() {";

    /// <summary>The core alpha computation that the part material replaces.</summary>
    public const string AlphaAnchor = "float a = texel.a * material_alpha;";

    /// <summary>Cell variants (<see cref="SurfaceLighting.CellObject"/>): the core line reading the cell's draw-state slot from the mesh.</summary>
    public const string CellSlotAnchor = "\tint ac_slot = int(CUSTOM1.a + 0.5);";

    /// <summary>Cell variants: the core cell-object light rule, whose CUSTOM1.rgb is a merged cell mesh's static-light sum
    /// and ac_dyn the dynamic lights' sum.</summary>
    public const string CellLightAnchor = "\tv_light = clamp(vec3(luminosity) + world_ambient * diffuse + CUSTOM1.rgb + ac_dyn, 0.0, 1.0);";

    private static readonly Dictionary<SurfaceShaderVariant, Shader> Cache = new();

    /// <summary>Godot shading language for a particle variant (pure; testable without the engine).</summary>
    /// <remarks>
    /// <para><b>Particles of EnvCell statics</b> (objects track, living statics; docs/objects.md §2.6) use the
    /// <see cref="SurfaceLighting.CellObject"/> variants, so they get the depth rules of the cell they stand in, exactly as
    /// the cell's geometry does (<see cref="SurfaceLightingCode"/>: drawn over the landscape while the cell is in the
    /// viewer's walk, clipped to the doorway cone and pulled in front of the doorway plane when seen from outdoors). Retail
    /// draws a cell's objects, their particle objects included, in the cell pass after the same depth reset
    /// (<c>PView::DrawCells</c> → <c>DrawObjCell</c>) and with the sun off (<c>Render::useSunlightSet(0)</c>). A particle
    /// mesh carries no cell slot, so the slot comes per instance from <c>INSTANCE_CUSTOM.z</c>
    /// (<see cref="Lifestone.Client.Effects.InstanceBuffer.ParticleCustom"/>), and no static-light sum: the lit colour is
    /// luminosity + world ambient + the dynamic lights (M3 4.1 r2). Retail lights a particle part like any
    /// other: a particle emitter's parts are <c>CPhysicsPart</c>s drawn through <c>CPhysicsPart::Draw</c> (0x0050E270) and
    /// <c>RenderDeviceD3D::DrawMeshInternal</c> (0x005A0470), which calls <c>Render::minimize_object_lighting</c> (0x0054E090) for every part
    /// drawn with the sun off, dynamic lights first (11.4186 acclient.c 314587 and 456960). The dynamic term is the cell variants' own
    /// (<see cref="SurfaceLightingCode.DynamicLightFunction"/>, per vertex, so a sprite takes the light at its corners). The static cell
    /// lights are not summed for a particle (the mesh carries no light sum): <c>TODO(particle-cell-static-lights)</c>; the flame and glow
    /// sprites of living statics are luminous, so they show the same either way.</para>
    /// </remarks>
    public static string GenerateCode(SurfaceShaderVariant variant)
    {
        var code = SurfaceShaders.GenerateCode(variant);
        code = Replace(code, DeclarationAnchor,
            DeclarationAnchor + "\n" +
            "varying float v_part_alpha;     // particle part material: 1 - translucency (INSTANCE_CUSTOM.x)\n" +
            "varying float v_part_material;  // 1 when the part material replaces the surface alpha (INSTANCE_CUSTOM.y)");
        code = Replace(code, VertexAnchor,
            VertexAnchor + "\n" +
            "\tv_part_alpha = INSTANCE_CUSTOM.x;\n" +
            "\tv_part_material = INSTANCE_CUSTOM.y;");
        code = Replace(code, AlphaAnchor,
            "float a = texel.a * (v_part_material > 0.5 ? v_part_alpha : material_alpha);   // part CMaterial alpha replaces the vertex alpha");
        if (variant.Lighting == SurfaceLighting.CellObject)
        {
            code = Replace(code, CellSlotAnchor,
                "\tint ac_slot = int(INSTANCE_CUSTOM.z + 0.5);   // particle of an EnvCell static: its cell's slot, per instance");
            code = Replace(code, CellLightAnchor,
                "\tv_light = clamp(vec3(luminosity) + world_ambient * diffuse + ac_dyn, 0.0, 1.0);   // particle in a cell: no sun, the dynamic lights, no static-light sum (TODO(particle-cell-static-lights))");
        }
        else if (variant.Lighting != SurfaceLighting.Object)
        {
            throw new ArgumentOutOfRangeException(nameof(variant), variant.Lighting, "Particles are drawn with the object or the cell-object lighting rule.");
        }

        return "// Lifestone particle variant (scripts/Render/Particles/ParticleShaders.cs) of:\n" + code;
    }

    /// <summary>The compiled shader of a particle variant (main thread; cached for the process).</summary>
    public static Shader Get(SurfaceShaderVariant variant)
    {
        if (!Cache.TryGetValue(variant, out var shader))
        {
            DynamicLightTable.Declare(); // M3 4.1 r2: the cell variants name the dynamic-light global
            shader = new Shader { Code = GenerateCode(variant), ResourceName = $"lifestone_particle_{variant.Name}" };
            Cache.Add(variant, shader);
            Log.Debug("particles", "particle shader created", ("variant", variant.Name));
        }

        return shader;
    }

    private static string Replace(string code, string anchor, string replacement)
    {
        var index = code.IndexOf(anchor, StringComparison.Ordinal);
        if (index < 0 || code.IndexOf(anchor, index + anchor.Length, StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException(
                $"TODO(render): the core surface shader no longer contains exactly one '{anchor}'; update ParticleShaders to the new SurfaceShaders.GenerateCode.");
        }

        return string.Concat(code.AsSpan(0, index), replacement, code.AsSpan(index + anchor.Length));
    }
}
