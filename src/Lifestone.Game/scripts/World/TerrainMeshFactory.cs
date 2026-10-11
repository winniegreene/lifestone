using Godot;
using Lifestone.Data.Terrain;
using Lifestone.Game.Core;
using Lifestone.Game.Render;

namespace Lifestone.Game.World;

/// <summary>A landblock's placeholder terrain converted to Godot arrays on a worker.</summary>
public sealed class PreparedTerrainMesh(Vector3[] positions, Vector3[] normals, Color[] colors, int[] indices, Aabb bounds)
{
    public Vector3[] Positions { get; } = positions;

    public Vector3[] Normals { get; } = normals;

    public Color[] Colors { get; } = colors;

    public int[] Indices { get; } = indices;

    public Aabb Bounds { get; } = bounds;
}

/// <summary>
/// PLACEHOLDER terrain (until the wave-2 terrain track lands the TexMerge shader): engine-agnostic
/// <see cref="TerrainMesh"/> to Godot meshes, vertex-coloured by the Region's terrain-type map colours, lit with the
/// retail terrain formula from the global uniforms and fogged like everything else.
/// </summary>
public static class TerrainMeshFactory
{
    private static readonly Color RoadColor = new(0.55f, 0.47f, 0.36f);

    /// <summary>
    /// The placeholder terrain shader: <c>CLandBlockStruct::calc_lighting</c>'s
    /// <c>min(1, sun_color x max(0, n · sun) + ambient_color x ambient_level)</c> per vertex (docs/terrain-shader.md §9)
    /// times the vertex colour, then retail linear fog, with the raw-to-linear conversion at the end
    /// (docs/render-contract.md). The real terrain shader replaces the vertex colour with the TexMerge blend.
    /// <see cref="TerrainMeshBuilder"/> normals are never zero (a sum shorter than 2e-4 becomes +Z, retail's fallback), so
    /// unlike the surface shaders this one needs no zero-normal flag; Godot could not store a zero normal anyway (its
    /// octahedral encoding makes every normal unit length, docs/render-contract.md §2.1).
    /// </summary>
    public const string ShaderCode =
        "// Lifestone placeholder terrain (vertex colours). Contract: docs/render-contract.md.\n" +
        "shader_type spatial;\n" +
        "render_mode unshaded, fog_disabled, cull_back, depth_draw_opaque, specular_disabled, shadows_disabled;\n" +
        RenderGlobals.ShaderDeclarations +
        "varying vec3 v_light;\n" +
        "varying float v_fog;\n" +
        RenderGlobals.OutputFunctionCode + // render-fidelity fix: the main view's blend space (render-contract §2.3)
        "void vertex() {\n" +
        "\t// TerrainMeshBuilder never emits a zero normal (it falls back to straight up, as retail does).\n" +
        "\tfloat n_dot_l = max(dot(normalize(MODEL_NORMAL_MATRIX * NORMAL), ac_sun_dir), 0.0);\n" +
        "\tv_light = min(ac_sun_color * n_dot_l + ac_land_ambient, vec3(1.0));\n" +
        "\tfloat d = length((MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz);\n" +
        "\tv_fog = ac_fog_enabled ? clamp((ac_fog_end - d) / max(ac_fog_end - ac_fog_start, 1e-4), 0.0, 1.0) : 1.0;\n" +
        "}\n" +
        "void fragment() {\n" +
        "\tvec3 c = COLOR.rgb * v_light;\n" +
        "\tc = mix(ac_fog_color, c, v_fog);\n" +
        "\tALBEDO = ac_output(c);\n" +
        "}\n";

    /// <summary>Worker: converts the mesh (landblock-local) to Godot axes and winding, with placeholder colours.</summary>
    public static PreparedTerrainMesh Prepare(TerrainMesh mesh, TerrainRegion region)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(region);
        var colors = new Color[mesh.VertexCount];
        for (var i = 0; i < colors.Length; i++)
        {
            var baseColor = ColorFromArgb(region.ColorOf(mesh.TerrainTypes[i]));
            colors[i] = mesh.Roads[i] != 0 ? baseColor.Lerp(RoadColor, 0.75f) : baseColor;
        }

        var min = new System.Numerics.Vector3(float.MaxValue);
        var max = new System.Numerics.Vector3(float.MinValue);
        foreach (var p in mesh.Positions)
        {
            min = System.Numerics.Vector3.Min(min, p);
            max = System.Numerics.Vector3.Max(max, p);
        }

        return new PreparedTerrainMesh(
            AcToGodot.Positions(mesh.Positions),
            AcToGodot.Directions(mesh.Normals),
            colors,
            AcToGodot.TriangleIndices(mesh.Indices),
            AcToGodot.Bounds(min, max));
    }

    /// <summary>Main thread: the ArrayMesh.</summary>
    public static ArrayMesh Build(PreparedTerrainMesh prepared, Material material)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = prepared.Positions;
        arrays[(int)Mesh.ArrayType.Normal] = prepared.Normals;
        arrays[(int)Mesh.ArrayType.Color] = prepared.Colors;
        arrays[(int)Mesh.ArrayType.Index] = prepared.Indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, material);
        mesh.CustomAabb = prepared.Bounds;
        return mesh;
    }

    /// <summary>The shared placeholder material (main thread).</summary>
    public static ShaderMaterial CreateMaterial() => new()
    {
        ResourceName = "lifestone_placeholder_terrain",
        Shader = new Shader { Code = ShaderCode, ResourceName = "lifestone_placeholder_terrain" },
    };

    private static Color ColorFromArgb(uint argb) => new(
        ((argb >> 16) & 0xFF) / 255f,
        ((argb >> 8) & 0xFF) / 255f,
        (argb & 0xFF) / 255f);
}
