using Godot;
using Lifestone.Client.Rendering;
using Lifestone.Data.Geometry;
using Lifestone.Game.Core;
using Lifestone.Game.Player;
using Lifestone.Game.World;

namespace Lifestone.Game.Render;

/// <summary>
/// <c>--blend-checks</c>: pins on the running engine what the render-fidelity fix relies on (docs/render-contract.md
/// §2.3-2.4), in both blend spaces, then quits (0 = every check holds). Run it windowed:
/// <c>tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 180 -GameArgs:'--blend-checks'</c>.
/// </summary>
/// <remarks>
/// <para>A check suite (Main's check mode, one suite per run): Main builds only the environment, so no world streams and
/// no sky driver writes the globals; anything of the world, the sky or the HUD that is present is hidden anyway.
/// Known quads are drawn in front of the main camera with the real surface shaders (<see cref="SurfaceShaders.GenerateCode"/>) and known light (ambient 0,
/// sun 0, fog off, luminosity 1, so the light is exactly 1); the background is a known clear colour. The same scene is
/// read back in byte space (the default) and then in linear space (<c>--blend-space=linear</c>); each check states both
/// expectations. Retail's numbers are the byte-space ones: D3D9 blended the raw bytes in an 8-bit framebuffer.</para>
/// <list type="bullet">
/// <item>An opaque raw colour and the clear colour reach the screen unchanged in both spaces (no regression for opaque
/// surfaces, terrain or the fog background).</item>
/// <item>Black Mix at 0.5 over grey 0.6 and over the background; an additive glow over a dark wall and over grass: byte
/// arithmetic in byte space, linear-light arithmetic in linear space.</item>
/// <item>Known deviation, pinned: the float buffer does not saturate between blends (grey 0.6, AddOne white, black Mix
/// 0.7 gives 122 where retail's 8-bit buffer gives 77; docs/sky.md §8 pins the same in the sky view).</item>
/// <item>The detail pass (<see cref="DetailTextures"/>): an opaque detail-variant quad over a known base with a known
/// 1×1 detail texel multiplies by <c>detail.rgb x light + 1 - detail.a</c>, as retail's DESTCOLOR/INVSRCALPHA pass.</item>
/// <item>Built-in Godot materials (review fix): an unshaded <see cref="StandardMaterial3D"/> and a <see cref="Label3D"/>
/// (the spell viewer's words) given <see cref="ByteSpaceBlending.BuiltinColor"/> show the raw colour in both spaces
/// (docs/render-contract.md §2.3: Godot decodes their colours, so byte space hands them over sRGB-encoded).</item>
/// </list>
/// </remarks>
public partial class BlendChecks : Node
{
    private const int Tolerance = 2;
    private const float Distance = 10f;

    private static readonly (double R, double G, double B) Background = (0.3, 0.5, 0.7);

    private readonly List<Node> _created = new();
    private readonly List<Probe> _probes = new();
    private readonly List<string> _failures = new();
    private readonly List<Action> _builtinColours = new();
    private Camera3D _camera = null!;
    private WorldEnvironment? _environment;
    private int _frame;
    private int _checks;

    public override void _Ready()
    {
        ProcessPriority = 50;
    }

    public override void _Process(double delta)
    {
        _frame++;
        switch (_frame)
        {
            case 1:
                SetUp();
                ByteSpaceBlending.Select(BlendSpace.Byte);
                SetBackground();
                break;
            case 10:
                Verify(BlendSpace.Byte);
                ByteSpaceBlending.Select(BlendSpace.Linear);
                SetBackground();
                break;
            case 20:
                Verify(BlendSpace.Linear);
                foreach (var node in _created)
                {
                    node.QueueFree();
                }

                var ok = _failures.Count == 0;
                Log.Info("blendcheck", ok ? "all blend checks hold" : "blend checks FAILED", ("checks", _checks), ("failures", _failures.Count));
                GetTree().Quit(ok ? 0 : 1);
                break;
        }
    }

    private void SetUp()
    {
        var main = GetParent();
        if (main.GetNodeOrNull<Node>("SkyController") is { } sky)
        {
            sky.ProcessMode = ProcessModeEnum.Disabled;
            foreach (var child in sky.GetChildren())
            {
                if (child is Node3D renderer)
                {
                    renderer.Visible = false;
                }
            }
        }

        if (main.GetNodeOrNull<Node3D>("WorldRoot") is { } world)
        {
            world.Visible = false;
        }

        foreach (var child in main.GetChildren())
        {
            if (child is CanvasLayer layer)
            {
                layer.Visible = false;
            }
        }

        var player = main.GetNode<FlyCamera>("Player");
        player.InputEnabled = false;
        _camera = player.Camera;
        _environment = main.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");

        RenderingServer.GlobalShaderParameterSet(RenderGlobals.Ambient, Vector3.Zero);
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.SunColor, Vector3.Zero);
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.FogEnabled, false);

        // Row 1: opaque colour; black Mix 0.5 over grey 0.6; background only; black Mix 0.5 over the background.
        Add(-7f, 2.5f, "opaque raw (0.8, 0.4, 0.2)", Opaque(0.8f, 0.4f, 0.2f), Same((0.8, 0.4, 0.2)), Same((0.8, 0.4, 0.2)));
        Add(-3.5f, 2.5f, "black Mix 0.5 over grey 0.6", [Opaque(0.6f, 0.6f, 0.6f), Blended(SurfaceBlend.Mix, 0f, 0f, 0f, 0.5f, 1)],
            Same((0.3, 0.3, 0.3)), Linear(l => 0.5 * l, (0.6, 0.6, 0.6)));
        Add(0f, 2.5f, "the clear (fog) colour", [], Same(Background), Same(Background));
        Add(3.5f, 2.5f, "black Mix 0.5 over the clear colour", [Blended(SurfaceBlend.Mix, 0f, 0f, 0f, 0.5f, 1)],
            Same((0.15, 0.25, 0.35)), Linear(l => 0.5 * l, Background));

        // Row 2: an additive glow (retail ONE/ONE) over a dark wall and over grass; the saturation deviation.
        var glow = (R: 0.4, G: 0.2, B: 0.05);
        var wall = (R: 0.1, G: 0.1, B: 0.12);
        var grass = (R: 0.35, G: 0.45, B: 0.15);
        Add(-7f, -2.5f, "AddOne glow over a dark wall", [Opaque(wall), Blended(SurfaceBlend.AddOne, (float)glow.R, (float)glow.G, (float)glow.B, 1f, 1)],
            Same((wall.R + glow.R, wall.G + glow.G, wall.B + glow.B)), LinearSum(wall, glow));
        Add(-3.5f, -2.5f, "AddOne glow over grass", [Opaque(grass), Blended(SurfaceBlend.AddOne, (float)glow.R, (float)glow.G, (float)glow.B, 1f, 1)],
            Same((grass.R + glow.R, grass.G + glow.G, grass.B + glow.B)), LinearSum(grass, glow));
        Add(0f, -2.5f, "known deviation: no saturation between blends (retail 77)",
            [Opaque(0.6f, 0.6f, 0.6f), Blended(SurfaceBlend.AddOne, 1f, 1f, 1f, 1f, 1), Blended(SurfaceBlend.Mix, 0f, 0f, 0f, 0.7f, 2)],
            Same((0.48, 0.48, 0.48)), Linear(l => 0.3 * (l + 1.0), (0.6, 0.6, 0.6)));
        AddDetailCheck(3.5f, -2.5f);

        // Column 5 (review fix): built-in Godot materials, whose colours Godot sRGB-decodes (render-contract §2.3).
        AddBuiltinChecks(7f);
    }

    /// <summary>
    /// Built-in materials in the main view (docs/render-contract.md §2.3): their colours are sRGB-decoded by Godot, so
    /// <see cref="ByteSpaceBlending.BuiltinColor"/> must hand them over encoded in byte space. Raw (0.9, 0.75, 0.35) must
    /// reach the screen unchanged in both spaces: from an unshaded <see cref="StandardMaterial3D"/> quad (its albedo
    /// colour) and from a <see cref="Label3D"/> glyph (its modulate, a vertex colour: the spell viewer's words). Godot
    /// creates both materials itself, and the colour is set again after each <see cref="ByteSpaceBlending.Select"/>.
    /// </summary>
    private void AddBuiltinChecks(float x)
    {
        var raw = new Color(0.9f, 0.75f, 0.35f);
        var expected = (R: (double)raw.R, G: (double)raw.G, B: (double)raw.B);

        var standard = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        var quad = new MeshInstance3D { Mesh = Quad(x - 1f, x + 1f, 2.5f - 1f, 2.5f + 1f, -Distance), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        quad.SetSurfaceOverrideMaterial(0, standard);
        _camera.AddChild(quad);
        _created.Add(quad);
        _builtinColours.Add(() => standard.AlbedoColor = ByteSpaceBlending.BuiltinColor(raw));
        _probes.Add(new Probe("built-in StandardMaterial3D albedo (0.9, 0.75, 0.35)", x, 2.5f, expected, expected));

        // A tall "I": the brightest pixel of the probe row lies inside the stroke, where the glyph is opaque (its
        // anti-aliased edges blend with the clear colour and are darker).
        var label = new Label3D
        {
            Text = "I",
            FontSize = 256,
            PixelSize = 0.01f,
            OutlineSize = 0,
            Position = new Vector3(x, -2.5f, -Distance),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _camera.AddChild(label);
        _created.Add(label);
        _builtinColours.Add(() => label.Modulate = ByteSpaceBlending.BuiltinColor(raw));
        _probes.Add(new Probe("built-in Label3D modulate (0.9, 0.75, 0.35)", x, -2.5f, expected, expected, ScanHalfWidth: 40));
    }

    private void SetBackground()
    {
        foreach (var apply in _builtinColours)
        {
            apply();
        }

        if (_environment?.Environment is { } environment)
        {
            environment.BackgroundMode = Godot.Environment.BGMode.Color;
            environment.BackgroundColor = ByteSpaceBlending.BackgroundColor(new Color((float)Background.R, (float)Background.G, (float)Background.B));
        }
    }

    /// <summary>
    /// The detail pass (docs/render-contract.md §2.4): a textured opaque building-detail quad, base texel grey 0.5 at light
    /// 1, detail texel rgb 0.2 / alpha 0.1, must show 0.5 x (0.2 x 1 + 1 - 0.1) = 0.55 in both blend spaces (the pass is
    /// folded into the opaque draw before the output conversion).
    /// </summary>
    private void AddDetailCheck(float x, float y)
    {
        var variant = new SurfaceShaderVariant(SurfaceBlend.Opaque, false, true, SurfaceCull.None, TextureAddressMode.Wrap, true, false).WithDetail(SurfaceDetail.Building);
        var material = new ShaderMaterial { Shader = new Shader { Code = SurfaceShaders.GenerateCode(variant) } };
        material.SetShaderParameter(MaterialFactory.TextureParameter, Texel(0.5f, 0.5f, 0.5f, 1f));
        material.SetShaderParameter(MaterialFactory.DetailTextureParameter, Texel(0.2f, 0.2f, 0.2f, 0.1f));
        material.SetShaderParameter(MaterialFactory.DetailTilingParameter, 4f);
        material.SetShaderParameter("material_alpha", 1f);
        material.SetShaderParameter("luminosity", 1f);
        Add(x, y, "detail pass: grey 0.5 x (detail 0.2 + 1 - detail alpha 0.1)", material, (0.55, 0.55, 0.55), (0.55, 0.55, 0.55));
    }

    private static ImageTexture Texel(float r, float g, float b, float a)
    {
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.SetPixel(0, 0, new Color(r, g, b, a));
        return ImageTexture.CreateFromImage(image);
    }

    private void Verify(BlendSpace space)
    {
        var image = GetViewport().GetTexture().GetImage();
        foreach (var probe in _probes)
        {
            var screen = _camera.UnprojectPosition(_camera.GlobalTransform * new Vector3(probe.X, probe.Y, -Distance));
            var c = probe.ScanHalfWidth > 0 ? Brightest(image, (int)screen.X, (int)screen.Y, probe.ScanHalfWidth) : image.GetPixel((int)screen.X, (int)screen.Y);
            var expected = space == BlendSpace.Byte ? probe.Byte : probe.Linear;
            var e = (R: expected.R * 255.0, G: expected.G * 255.0, B: expected.B * 255.0);
            var ok = Math.Abs(c.R8 - e.R) <= Tolerance && Math.Abs(c.G8 - e.G) <= Tolerance && Math.Abs(c.B8 - e.B) <= Tolerance;
            _checks++;
            if (!ok)
            {
                _failures.Add($"{space}: {probe.Name}");
            }

            var log = ok ? (Action<string, string, (string, object?)[]>)Log.Info : Log.Error;
            log("blendcheck", ok ? "PASS" : "FAIL", [("space", space), ("check", probe.Name), ("pixel", $"({c.R8}, {c.G8}, {c.B8})"), ("expected", $"({e.R:0}, {e.G:0}, {e.B:0})")]);
        }
    }

    /// <summary>The brightest pixel (sum of channels) of row <paramref name="y"/> within <paramref name="halfWidth"/> of <paramref name="x"/>.</summary>
    private static Color Brightest(Image image, int x, int y, int halfWidth)
    {
        var best = image.GetPixel(x, y);
        for (var px = Math.Max(0, x - halfWidth); px <= Math.Min(image.GetWidth() - 1, x + halfWidth); px++)
        {
            var c = image.GetPixel(px, y);
            if (c.R + c.G + c.B > best.R + best.G + best.B)
            {
                best = c;
            }
        }

        return best;
    }

    private void Add(float x, float y, string name, ShaderMaterial material, (double R, double G, double B) byteSpace, (double R, double G, double B) linear) =>
        Add(x, y, name, [material], byteSpace, linear);

    /// <summary>Stacked quads at (x, y), drawn in list order (later ones nearer the camera and higher priority).</summary>
    private void Add(float x, float y, string name, ShaderMaterial[] layers, (double R, double G, double B) byteSpace, (double R, double G, double B) linear)
    {
        for (var i = 0; i < layers.Length; i++)
        {
            var instance = new MeshInstance3D { Mesh = Quad(x - 1f, x + 1f, y - 1f, y + 1f, -Distance + (0.01f * i)), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            instance.SetSurfaceOverrideMaterial(0, layers[i]);
            _camera.AddChild(instance);
            _created.Add(instance);
        }

        _probes.Add(new Probe(name, x, y, byteSpace, linear));
    }

    private static (double R, double G, double B) Same((double R, double G, double B) raw) => raw;

    /// <summary>Linear space: decode, apply <paramref name="f"/> in linear light, encode.</summary>
    private static (double R, double G, double B) Linear(Func<double, double> f, (double R, double G, double B) raw) =>
        (Encode(f(Decode(raw.R))), Encode(f(Decode(raw.G))), Encode(f(Decode(raw.B))));

    private static (double R, double G, double B) LinearSum((double R, double G, double B) a, (double R, double G, double B) b) =>
        (Encode(Decode(a.R) + Decode(b.R)), Encode(Decode(a.G) + Decode(b.G)), Encode(Decode(a.B) + Decode(b.B)));

    private static double Decode(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double Encode(double l)
    {
        var c = l <= 0.0031308 ? l * 12.92 : (1.055 * Math.Pow(l, 1.0 / 2.4)) - 0.055;
        return Math.Clamp(c, 0.0, 1.0);
    }

    private static ShaderMaterial Opaque((double R, double G, double B) c) => Opaque((float)c.R, (float)c.G, (float)c.B);

    private static ShaderMaterial Opaque(float r, float g, float b) =>
        Solid(new SurfaceShaderVariant(SurfaceBlend.Opaque, false, true, SurfaceCull.None, TextureAddressMode.Wrap, false, false), r, g, b, 1f, 0);

    private static ShaderMaterial Blended(SurfaceBlend blend, float r, float g, float b, float alpha, int priority) =>
        Solid(new SurfaceShaderVariant(blend, false, false, SurfaceCull.None, TextureAddressMode.Wrap, false, false), r, g, b, alpha, priority);

    /// <summary>A solid-colour surface material (light exactly 1: luminosity 1, the checks zero ambient and sun).</summary>
    private static ShaderMaterial Solid(SurfaceShaderVariant variant, float r, float g, float b, float alpha, int priority)
    {
        var material = new ShaderMaterial { Shader = new Shader { Code = SurfaceShaders.GenerateCode(variant) }, RenderPriority = priority };
        material.SetShaderParameter("solid_color", new Vector4(r, g, b, 1f));
        material.SetShaderParameter("material_alpha", alpha);
        material.SetShaderParameter("luminosity", 1f);
        return material;
    }

    /// <summary>A camera-space quad facing the camera.</summary>
    private static ArrayMesh Quad(float x0, float x1, float y0, float y1, float z)
    {
        var mesh = new ArrayMesh();
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[] { new(x0, y0, z), new(x1, y0, z), new(x1, y1, z), new(x0, y1, z) };
        arrays[(int)Mesh.ArrayType.Normal] = new Vector3[] { Vector3.Back, Vector3.Back, Vector3.Back, Vector3.Back };
        arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[] { Vector2.Zero, Vector2.Right, Vector2.One, Vector2.Down };
        arrays[(int)Mesh.ArrayType.Custom0] = new float[] { 0f, 0f, 0f, 0f };
        arrays[(int)Mesh.ArrayType.Index] = new int[] { 0, 2, 1, 0, 3, 2 };
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: MeshFactory.ZeroNormalFormat);
        mesh.CustomAabb = new Aabb(new Vector3(-100f, -100f, -100f), new Vector3(200f, 200f, 200f));
        return mesh;
    }

    /// <param name="ScanHalfWidth">0: the pixel at the probe; otherwise the brightest pixel of its row within this many pixels.</param>
    private sealed record Probe(string Name, float X, float Y, (double R, double G, double B) Byte, (double R, double G, double B) Linear, int ScanHalfWidth = 0);
}
