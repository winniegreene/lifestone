using Godot;
using Lifestone.Client.Interiors;
using Lifestone.Client.Rendering;
using Lifestone.Data.Geometry;
using Lifestone.Game.Core;
using Lifestone.Game.Render;
using Lifestone.Game.World.ServerObjects;
using AcVector2 = System.Numerics.Vector2;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.World.Interiors;

/// <summary>
/// <c>--interior-engine-checks</c>: renders quads with the cell lighting variants into an off-screen viewport and
/// checks the engine behaviours the interiors rely on, then quits (0 = all hold). Run windowed (off-screen), like
/// core's <c>--engine-checks</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A mesh without CUSTOM1 reads 0 there (no light, slot 0 = no state).</item>
/// <item>CUSTOM1.rgb (RGBA float) reaches the shader unchanged, for objects and for cells.</item>
/// <item>The indoor ambient switch (<c>ac_indoor_ambient_on</c>) replaces <c>ac_ambient</c>.</item>
/// <item>The cell-state table works through the slot in CUSTOM1.a: a front cell quad that is farther away is drawn over
/// an object quad (reversed-Z depth), and a cell clipped to a doorway row is discarded outside the cone and drawn over
/// the object inside it (depth pulled to the doorway's plane).</item>
/// <item>The cell variants use no instance uniforms: with more cell-material instances than the global shader-uniform
/// buffer has instance blocks, an instance-uniform probe still gets its block; then, as a negative control, filling
/// the buffer with instance-uniform instances makes the next probe lose its block (Godot prints its "Too many
/// instances using shader instance variables" errors for those, as expected).</item>
/// <item>The moving objects' variants (M2 W2-pre(c): <see cref="SurfaceLighting.DynamicObject"/> and
/// <see cref="SurfaceLighting.DynamicCellObject"/>, MultiMesh instances through <see cref="DynamicObjectBatch"/>) read
/// <c>INSTANCE_CUSTOM</c>: z overrides the luminosity, w scales the alpha of a blended surface (an instance with 0 is untouched),
/// x is the cell's slot (a clipped slot discards outside its cone), and y is one plus a row of <c>ac_object_lights</c> whose
/// lights are summed per vertex exactly as <see cref="CellObjectLighting.Light"/> sums them on the CPU for statics; a light out
/// of reach adds nothing.</item>
/// </list>
/// </remarks>
public partial class InteriorEngineChecks : Node
{
    private const int Tolerance = 2;

    /// <summary>Godot reserves this many vec4 slots of the global buffer per instance with instance uniforms (MAX_INSTANCE_UNIFORM_INDICES).</summary>
    private const int SlotsPerInstanceBlock = 16;

    private const int FrontSlot = 5;
    private const int ClippedSlot = 6;
    private const int BandSlot = 7;
    private const string ProbeParameter = "probe_color";

    // 576 x 64 pixels over x in [-9, 9] (orthographic, 2 m high): pixel x = (x + 9) * 32.
    private static readonly Vector2I ViewportSize = new(576, 64);
    private readonly List<string> _failures = new();

    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        // Render-fidelity fix: these checks read their own off-screen viewport, whose environment has no byte-space decode, so
        // they pin the lighting rules in linear output space (the blend spaces themselves: --blend-checks).
        ByteSpaceBlending.Select(BlendSpace.Linear);
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.SunDirection, new Vector3(0, 0, 1));
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.SunColor, new Vector3(0.5f, 0.5f, 0.5f));
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.Ambient, new Vector3(0.2f, 0.2f, 0.2f));
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.FogEnabled, false);
        RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.IndoorAmbient, new Vector3(0.1f, 0.1f, 0.1f));
        RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.IndoorAmbientOn, false);

        // Doorway rows 0 and 1: their plane at Godot z = 1.5 (between the camera and the object quads), their cones
        // x <= -0.2 and x <= 2.0.
        var planes = Image.CreateEmpty(1 + DoorwayRows.MaxConePlanes, 4, false, Image.Format.Rgbaf);
        planes.Fill(new Color(0f, 0f, 0f, 1f));
        planes.SetPixel(0, 0, new Color(0f, 0f, 1f, -1.5f));
        planes.SetPixel(1, 0, new Color(-1f, 0f, 0f, -0.2f));
        planes.SetPixel(0, 1, new Color(0f, 0f, 1f, -1.5f));
        planes.SetPixel(1, 1, new Color(-1f, 0f, 0f, 2.0f));
        RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.DoorwayPlanes, ImageTexture.CreateFromImage(planes));
        var states = Image.CreateEmpty(SurfaceLightingCode.CellStateTableSize, SurfaceLightingCode.CellStateTableSize, false, Image.Format.Rgba8);
        states.Fill(new Color(0f, 0f, 0f, 0f));
        SetState(states, FrontSlot, CellDrawState.Create(front: true, -1, -1));
        SetState(states, ClippedSlot, CellDrawState.Create(front: false, 0, -1));
        SetState(states, BandSlot, CellDrawState.Create(front: false, 1, -1));
        RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.CellStates, ImageTexture.CreateFromImage(states));

        var viewport = new SubViewport
        {
            Size = ViewportSize,
            OwnWorld3D = true,
            TransparentBg = false,
            Msaa3D = Viewport.Msaa.Disabled,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);
        viewport.AddChild(new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 2f, // y in [-1, 1]; x in [-9, 9]
            Near = 0.1f,
            Far = 20f,
            Position = new Vector3(0, 0, 5),
            Current = true,
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0f, 0.5f, 0f),
                TonemapMode = Godot.Environment.ToneMapper.Linear,
                TonemapExposure = 1f,
                AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            },
        });

        var shaders = new SurfaceShaders();
        foreach (var lighting in new[] { SurfaceLighting.EnvCell, SurfaceLighting.CellObject, SurfaceLighting.DynamicObject, SurfaceLighting.DynamicCellObject })
        {
            var code = SurfaceShaders.GenerateCode(Variant(lighting));
            Check(!code.Contains("instance uniform", StringComparison.Ordinal), $"the {lighting} variant declares no instance uniform", "none", code.Contains("instance uniform", StringComparison.Ordinal) ? "declared" : "none");
        }

        var cellObject = Solid(shaders, SurfaceLighting.CellObject);
        var envCell = Solid(shaders, SurfaceLighting.EnvCell);
        var outdoor = Solid(shaders, SurfaceLighting.Object);

        // A (x = -8): object in a cell, plain mesh (no CUSTOM1): ambient only.
        Add(viewport, MeshFactory.Build(MeshFactory.Prepare(Quad(-8f, 0f)), _ => cellObject));

        // B (x = -6): object in a cell with CUSTOM1 = 0.3: ambient + 0.3.
        Add(viewport, LitQuad(-6f, 0f, 0.3f, 0, cellObject));

        // C (x = -4): cell with CUSTOM1 = 102/255 (a burned colour): 0.4 + ambient.
        Add(viewport, LitQuad(-4f, 0f, 102 / 255f, 0, envCell));

        // D (x = -2): an outdoor object quad in front (z = +1) and a cell quad behind it (z = -1) whose slot says front.
        Add(viewport, MeshFactory.Build(MeshFactory.Prepare(Quad(-2f, 1f)), _ => outdoor));
        Add(viewport, LitQuad(-2f, -1f, 1f, FrontSlot, envCell));

        // E (x = 0): the same pair, the cell's slot clipped to doorway row 0 (cone x <= -0.2, plane z = 1.5).
        Add(viewport, MeshFactory.Build(MeshFactory.Prepare(Quad(0f, 1f)), _ => outdoor));
        Add(viewport, LitQuad(0f, -1f, 1f, ClippedSlot, envCell));

        // F (x = 2): a cell quad alone clipped to row 1 (cone x <= 2.0): kept just inside and within the seam band just
        // outside (own depth), discarded beyond it. Pins the band's sign (Godot's projection has P[1][1] < 0).
        Add(viewport, LitQuad(2f, -1f, 1f, BandSlot, envCell));

        await Frames(4);
        var image = viewport.GetTexture().GetImage();
        Expect(image, 32, "object in a cell without CUSTOM1: ambient only (0.2)", 51);
        Expect(image, 96, "object in a cell with CUSTOM1 0.3: ambient + 0.3", 128);
        Expect(image, 160, "cell with CUSTOM1 102/255: 0.4 + ambient", 153);
        Expect(image, 224, "front cell quad (slot state) behind an object quad is drawn over it (reversed-Z): white", 255);
        Expect(image, 269, "doorway-clipped cell inside the cone: depth pulled to the doorway plane, drawn over the object: white", 255);
        Expect(image, 307, "doorway-clipped cell outside the cone: discarded, the object shows (0.2 + 0.5 sun)", 178);
        Expect(image, 347, "doorway-clipped cell 0.15 m inside its cone edge: drawn (white)", 255);
        Expect(image, 355, "doorway-clipped cell 0.1 m outside its cone edge: in the seam band, drawn (white)", 255);
        ExpectColor(image, 374, "doorway-clipped cell 0.7 m outside its cone edge: beyond the band, discarded (background)", new Color8(0, 128, 0));

        // The indoor ambient switch: 0.1 instead of 0.2.
        RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.IndoorAmbientOn, true);
        await Frames(4);
        image = viewport.GetTexture().GetImage();
        Expect(image, 32, "indoor ambient on: object without CUSTOM1 shows ac_indoor_ambient (0.1)", 26);
        Expect(image, 160, "indoor ambient on: cell shows CUSTOM1 + 0.1", 128);
        RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.IndoorAmbientOn, false);

        await CheckDynamicObjects(viewport, shaders);
        await CheckInstanceBudget(viewport, envCell);

        viewport.QueueFree();
        Log.Info("interiors", _failures.Count == 0 ? "engine checks: all hold" : "engine checks FAILED", ("failures", _failures.Count));
        Log.FlushFile();
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    /// <summary>
    /// The moving objects' variants through MultiMesh instances: the luminosity override, the translucency, the cell slot and the light
    /// set (see the class remarks). The quads of the earlier checks are cleared first; the batches are freed at the end.
    /// </summary>
    private async Task CheckDynamicObjects(SubViewport viewport, SurfaceShaders shaders)
    {
        foreach (var node in viewport.GetChildren().OfType<MeshInstance3D>().ToArray())
        {
            node.QueueFree();
        }

        await Frames(2);
        var table = new ObjectLightTable(4);
        var near = table.Allocate();
        var far = table.Allocate();

        // The CPU's answer for statics (CellObjectLighting.Light) at the quad of x = 4, with the light 1 m in front of its centre (Godot z = +1,
        // AC y = -1): per vertex, clamped, the same for the four corners by symmetry.
        var nearLight = new CellLight(0, 0, AcVector3.Zero, new AcVector3(4f, -1f, 0f), new AcVector3(1f, 0.5f, 0.25f), 0.4f, 2f);
        var quad = Quad(4f, 0f).SubMeshes[0];
        var sums = new AcVector3[quad.Positions.Length];
        CellObjectLighting.Light(quad.Positions, quad.Normals, [nearLight], sums);
        var lit = new Color8(
            (int)Math.Round((0.2 + sums[0].X) * 255.0),
            (int)Math.Round((0.2 + sums[0].Y) * 255.0),
            (int)Math.Round((0.2 + sums[0].Z) * 255.0));
        Span<ObjectLight> set = stackalloc ObjectLight[ObjectLightSets.MaxLights];
        var count = ObjectLightSets.Select(new AcVector3(4f, 0f, 0f), 1f, [nearLight], set);
        Check(count == 1, "ObjectLightSets.Select keeps the light that reaches the sphere", "1", count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var godot = AcToGodot.Position(set[0].Position);
        set[0] = set[0] with { Position = new AcVector3(godot.X, godot.Y, godot.Z) };
        table.Write(near, set, count);
        Span<ObjectLight> farSet = [set[0] with { Reach = 0.5f }];
        table.Write(far, farSet, 1);
        // No Flush here on purpose: the table uploads itself before the next draw, as it does in the game (M2 5b b0: the game never called Flush, so
        // the rows never reached the GPU while this check, which flushed by hand, passed). The quads below read the rows only if that upload happens.

        var plain = DynamicVariant(SurfaceLighting.DynamicObject);
        var blended = DynamicVariant(SurfaceLighting.DynamicObject, SurfaceBlend.Mix);
        var inCell = DynamicVariant(SurfaceLighting.DynamicCellObject);
        var batches = new List<DynamicObjectBatch>
        {
            DynamicQuad(viewport, shaders, plain, -8f, default),
            DynamicQuad(viewport, shaders, plain, -6f, new Vector4(0f, 0f, 0.25f, 0f)),
            DynamicQuad(viewport, shaders, blended, -4f, new Vector4(0f, 0f, 0f, 1f)),
            DynamicQuad(viewport, shaders, blended, -2f, default),
            DynamicQuad(viewport, shaders, blended, 0f, new Vector4(0f, 0f, 0f, 0.5f)),
            DynamicQuad(viewport, shaders, inCell, 2f, default),
            DynamicQuad(viewport, shaders, inCell, 4f, new Vector4(0f, near + 1f, 0f, 0f)),
            DynamicQuad(viewport, shaders, inCell, 6f, new Vector4(0f, far + 1f, 0f, 0f)),
            DynamicQuad(viewport, shaders, inCell, 8f, new Vector4(ClippedSlot, 0f, 0f, 0f)),
        };
        await Frames(4);
        Check(table.Uploads >= 1, "the light table uploads its rows itself before a draw (no caller flushes it)", ">= 1", table.Uploads.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var image = viewport.GetTexture().GetImage();
        Expect(image, 32, "DynamicObject, plain instance: sun and ambient as an outdoor object (0.2 + 0.5)", 178);
        Expect(image, 96, "DynamicObject, INSTANCE_CUSTOM.z = 0.25 overrides the luminosity: 0.25 + 0.2 + 0.5", 242);
        ExpectColor(image, 160, "DynamicObject blended, translucency 1: nothing drawn (background)", new Color8(0, 128, 0));
        Expect(image, 224, "DynamicObject blended, translucency 0: as the plain instance", 178);
        var half = image.GetPixel(288, ViewportSize.Y / 2);
        Check(half.R8 > 20 && half.R8 < 170, "DynamicObject blended, translucency 0.5: between the object and the background", "20 < R < 170", $"({half.R8}, {half.G8}, {half.B8})");
        Expect(image, 352, "DynamicCellObject, plain instance: no sun, the world ambient (0.2)", 51);
        ExpectColor(image, 416, "DynamicCellObject with a light set: ambient + the lights summed per vertex, as CellObjectLighting.Light does on the CPU", lit);
        Expect(image, 480, "DynamicCellObject whose light set holds a light out of reach: ambient only (0.2)", 51);
        ExpectColor(image, 544, "DynamicCellObject with a clipped slot (INSTANCE_CUSTOM.x), outside its doorway cone: discarded (background)", new Color8(0, 128, 0));

        foreach (var batch in batches)
        {
            batch.Dispose();
        }

        table.Dispose();
        await Frames(2);
    }

    private static DynamicObjectBatch DynamicQuad(SubViewport viewport, SurfaceShaders shaders, SurfaceShaderVariant variant, float centreX, Vector4 custom)
    {
        var material = new ShaderMaterial { Shader = shaders.Get(variant) };
        material.SetShaderParameter("solid_color", Colors.White);
        var mesh = MeshFactory.Build(MeshFactory.Prepare(Quad(centreX, 0f)), _ => material);
        var batch = new DynamicObjectBatch(viewport, $"Dynamic_{centreX}", 1u) { Mesh = mesh };
        batch.Begin(1);
        batch.Add(Transform3D.Identity, custom, mesh.GetAabb());
        batch.End();
        return batch;
    }

    private static SurfaceShaderVariant DynamicVariant(SurfaceLighting lighting, SurfaceBlend blend = SurfaceBlend.Opaque) =>
        new(blend, false, blend == SurfaceBlend.Opaque, SurfaceCull.None, TextureAddressMode.Clamp, Textured: false, Fog: false, lighting);

    /// <summary>
    /// The cell variants take no blocks of the global shader-uniform buffer (finding: the old instance uniforms ran out
    /// of blocks in dense areas). F: more cell instances than there are blocks, then an instance-uniform probe still
    /// works. G (negative control): as many probe-shader instances as there are blocks, then the next probe fails.
    /// </summary>
    private async Task CheckInstanceBudget(SubViewport viewport, ShaderMaterial envCell)
    {
        var bufferSize = (int)ProjectSettings.GetSetting("rendering/limits/global_shader_variables/buffer_size", 65536);
        var blocks = bufferSize / SlotsPerInstanceBlock;
        var probeShader = new Shader
        {
            Code = "shader_type spatial;\nrender_mode unshaded, cull_disabled;\ninstance uniform vec4 probe_color = vec4(0.0, 0.0, 0.0, 1.0);\n" +
                "void fragment() {\n\tALBEDO = probe_color.rgb;\n}\n",
        };
        var probeMaterial = new ShaderMaterial { Shader = probeShader };
        var offscreen = LitQuad(100f, 0f, 1f, 0, envCell);
        for (var i = 0; i < blocks + 64; i++)
        {
            viewport.AddChild(new MeshInstance3D { Mesh = offscreen });
        }

        // The RenderingServer updates dirty instances newest first: the probe must come a frame after the fillers.
        await Frames(2);
        var probe = new MeshInstance3D { Mesh = MeshFactory.Build(MeshFactory.Prepare(Quad(4f, 0f))), MaterialOverride = probeMaterial };
        viewport.AddChild(probe);
        probe.SetInstanceShaderParameter(ProbeParameter, Colors.White);
        await Frames(4);
        Expect(viewport.GetTexture().GetImage(), 416, $"after {blocks + 64} cell-variant instances (the buffer has {blocks} instance blocks), an instance-uniform probe still gets one: white", 255);

        Log.Info("interiors", "engine check negative control: Godot reports 'Too many instances using shader instance variables' for the next instances, and 'instance_buffer_pos' when they are freed at exit; that is expected", ("instances", blocks + 8));
        var probeMesh = MeshFactory.Build(MeshFactory.Prepare(Quad(100f, 0f)));
        for (var i = 0; i < blocks + 8; i++)
        {
            viewport.AddChild(new MeshInstance3D { Mesh = probeMesh, MaterialOverride = probeMaterial });
        }

        await Frames(2);
        var late = new MeshInstance3D { Mesh = MeshFactory.Build(MeshFactory.Prepare(Quad(6f, 0f))), MaterialOverride = probeMaterial };
        viewport.AddChild(late);
        late.SetInstanceShaderParameter(ProbeParameter, Colors.White);
        await Frames(4);
        var c = viewport.GetTexture().GetImage().GetPixel(480, ViewportSize.Y / 2);
        Check(c.R8 < 255 - Tolerance, $"negative control: after {blocks + 8} more instance-uniform instances the next probe gets no block (not white)", "not 255", $"({c.R8}, {c.G8}, {c.B8})");
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }

    private void Expect(Image image, int x, string name, int grey)
    {
        var c = image.GetPixel(x, ViewportSize.Y / 2);
        var ok = Math.Abs(c.R8 - grey) <= Tolerance && Math.Abs(c.G8 - grey) <= Tolerance && Math.Abs(c.B8 - grey) <= Tolerance;
        Check(ok, name, grey.ToString(System.Globalization.CultureInfo.InvariantCulture), $"({c.R8}, {c.G8}, {c.B8})");
    }

    private void ExpectColor(Image image, int x, string name, Color8 expected)
    {
        var c = image.GetPixel(x, ViewportSize.Y / 2);
        var ok = Math.Abs(c.R8 - expected.R) <= Tolerance && Math.Abs(c.G8 - expected.G) <= Tolerance && Math.Abs(c.B8 - expected.B) <= Tolerance;
        Check(ok, name, $"({expected.R}, {expected.G}, {expected.B})", $"({c.R8}, {c.G8}, {c.B8})");
    }

    private readonly record struct Color8(int R, int G, int B);

    private void Check(bool ok, string name, string expected, string got)
    {
        if (!ok)
        {
            _failures.Add(name);
        }

        Log.Info("interiors", ok ? "engine check holds" : "ENGINE CHECK FAILS", ("check", name), ("got", got), ("expected", expected));
    }

    private static void SetState(Image states, int slot, CellDrawState state)
    {
        var (r, g, b, a) = state.ToBytes();
        states.SetPixel(slot % SurfaceLightingCode.CellStateTableSize, slot / SurfaceLightingCode.CellStateTableSize, Color.Color8(r, g, b, a));
    }

    private static SurfaceShaderVariant Variant(SurfaceLighting lighting) =>
        new(SurfaceBlend.Opaque, false, true, SurfaceCull.None, TextureAddressMode.Clamp, Textured: false, Fog: false, lighting);

    private static ShaderMaterial Solid(SurfaceShaders shaders, SurfaceLighting lighting)
    {
        var material = new ShaderMaterial { Shader = shaders.Get(Variant(lighting)) };
        material.SetShaderParameter("solid_color", Colors.White);
        return material;
    }

    /// <summary>A quad with CUSTOM1 = (light, light, light, slot).</summary>
    private static ArrayMesh LitQuad(float centreX, float towardsCamera, float light, int slot, Material material)
    {
        var prepared = MeshFactory.Prepare(Quad(centreX, towardsCamera));
        var floats = new float[prepared.SubMeshes[0].Positions.Length * 4];
        for (var v = 0; v < floats.Length; v += 4)
        {
            floats[v] = floats[v + 1] = floats[v + 2] = light;
            floats[v + 3] = slot;
        }

        return InteriorMeshes.Build(prepared, [floats], _ => material);
    }

    private static MeshInstance3D Add(SubViewport viewport, ArrayMesh mesh)
    {
        var instance = new MeshInstance3D { Mesh = mesh };
        viewport.AddChild(instance);
        return instance;
    }

    /// <summary>A 1.6 m quad in the AC x/z plane at AC y = -<paramref name="towardsCamera"/>, centred on x, facing the camera (AC south).</summary>
    private static MeshData Quad(float centreX, float towardsCamera)
    {
        var y = -towardsCamera;
        var positions = new[]
        {
            new AcVector3(centreX - 0.8f, y, -0.8f),
            new AcVector3(centreX + 0.8f, y, -0.8f),
            new AcVector3(centreX + 0.8f, y, 0.8f),
            new AcVector3(centreX - 0.8f, y, 0.8f),
        };
        var normals = Enumerable.Repeat(new AcVector3(0, -1, 0), 4).ToArray();
        var uvs = new[] { new AcVector2(0, 1), new AcVector2(1, 1), new AcVector2(1, 0), new AcVector2(0, 0) };
        return new MeshData([new SubMesh(0x08000000, MeshSide.Positive, TextureAddressMode.Clamp, positions, normals, uvs, [0, 1, 2, 0, 2, 3])]);
    }
}
