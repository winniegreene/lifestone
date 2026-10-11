using Godot;
using Lifestone.Client.Rendering;
using Lifestone.Client.Sky;
using Lifestone.Data.Geometry;
using Lifestone.Game.Core;
using Lifestone.Game.Player;
using Lifestone.Game.Render;

namespace Lifestone.Game.World.Sky;

/// <summary>
/// <c>--sky-checks</c>: verifies on the running engine the three things the sky's look depends on, then quits
/// (0 = all hold). Run it windowed (tools\godot.ps1 -Run -Offscreen), like the core's <c>--engine-checks</c>.
/// </summary>
/// <remarks>
/// <para>The world and the real sky objects are hidden; known quads are drawn with known light (ambient 0, sun 0, fog off,
/// luminosity 1 so the light is exactly 1):</para>
/// <list type="number">
/// <item><b>Byte-space blending in the sky view.</b> An opaque sky quad of raw colour (0.8, 0.4, 0.2) everywhere, then a
/// black Mix quad at alpha 0.7 over the left half. Retail blends bytes: the left half must show 0.3 x (204, 102, 51) =
/// (61, 31, 15); Godot's linear blending would show about (119, 85, 59).</item>
/// <item><b>Sky order.</b> The Mix quad has the higher <c>render_priority</c> and must be drawn after the opaque one.</item>
/// <item><b>The world covers the sky; the after pass covers the world.</b> An opaque world surface (the core's surface
/// shader) over the bottom-right quarter must replace the sky there, and an additive after-pass quad (raw 0.1) over the
/// bottom half, placed behind the world quad, must still add over both (depth test off).</item>
/// <item><b>Colour parameters.</b> Godot converts a <c>Color</c> given to a plain <c>vec4</c> shader parameter from sRGB to
/// linear, so raw retail colours (solid-colour surfaces) must be passed as <c>Vector4</c>. A grey 0.6 passed as a Color
/// must show as srgb_to_linear(0.6) = 81; if Godot ever stops converting, this check fails and the workaround can go.</item>
/// <item><b>Known deviation: no saturation between sky blends.</b> Retail's 8-bit framebuffer clamps after every
/// blend; the sky view's float buffer does not. An opaque grey 0.6, then an AddOne white 1.0 over it, then a black Mix at
/// 0.7: retail shows 0.3 x min(1.6, 1) = 77, Lifestone 0.3 x 1.6 = 122 (docs/sky.md §8). The check pins the deviation
/// (it expects 122); if it ever fails, the buffer saturates and the deviation note can go.</item>
/// </list>
/// </remarks>
public partial class SkyChecks : Node
{
    private const int Tolerance = 2;

    private SkyController _sky = null!;
    private WorldRoot _world = null!;
    private FlyCamera _camera = null!;
    private Func<string, bool>? _save;
    private CanvasLayer? _hud;
    private int _frame;
    private readonly List<Node> _created = new();

    public void Initialize(SkyController sky, WorldRoot world, FlyCamera camera, CanvasLayer? hud, Func<string, bool>? saveScreenshot)
    {
        _sky = sky;
        _world = world;
        _camera = camera;
        _hud = hud;
        _save = saveScreenshot;
        ProcessPriority = 40;
    }

    public override void _Process(double delta)
    {
        if (_sky.Renderer is not { } renderer)
        {
            Log.Error("skycheck", "sky objects are off (--sky=off); nothing to check");
            GetTree().Quit(1);
            return;
        }

        _frame++;
        if (_frame == 1)
        {
            SetUp(renderer);
            return;
        }

        if (_frame < 8)
        {
            return;
        }

        var ok = Verify();
        foreach (var node in _created)
        {
            node.QueueFree();
        }

        GetTree().Quit(ok ? 0 : 1);
    }

    private void SetUp(SkyRenderer renderer)
    {
        // Freeze the sky driver and hide everything that is not under test.
        _sky.ProcessMode = ProcessModeEnum.Disabled;
        _world.Visible = false;
        renderer.View.Objects.Visible = false;
        foreach (var child in renderer.GetChildren())
        {
            if (child is Node3D node3D)
            {
                node3D.Visible = false;
            }
        }

        if (_hud is not null)
        {
            _hud.Visible = false;
        }

        _camera.InputEnabled = false;
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.Ambient, Vector3.Zero);
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.SunColor, Vector3.Zero);
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.FogEnabled, false);

        var shaders = new SkyShaders();
        var skyCamera = renderer.View.Camera;
        Add(skyCamera, Quad(-15, 15, -10, 10, -10), SkyMaterial(shaders, SurfaceBlend.Opaque, SkyPass.Before, new Color(0.8f, 0.4f, 0.2f), 1f, -128));
        Add(skyCamera, Quad(-15, 0, -10, 10, -10), SkyMaterial(shaders, SurfaceBlend.Mix, SkyPass.Before, new Color(0f, 0f, 0f), 0.7f, -100));

        var main = _camera.Camera;
        var world = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = SurfaceShaders.GenerateCode(new SurfaceShaderVariant(SurfaceBlend.Opaque, false, true, SurfaceCull.Back, TextureAddressMode.Wrap, false, false)),
            },
        };
        world.SetShaderParameter("solid_color", new Vector4(0.2f, 0.6f, 0.2f, 1f));
        world.SetShaderParameter("material_alpha", 1f);
        world.SetShaderParameter("luminosity", 1f);
        Add(main, Quad(0, 15, -10, 0, -10), world);

        // Engine behaviour the render path must respect: a Color given to a plain vec4 parameter arrives linearised.
        var colorParameter = (ShaderMaterial)world.Duplicate();
        colorParameter.SetShaderParameter("solid_color", new Color(0.6f, 0.6f, 0.6f));
        Add(main, Quad(-1, 1, 3, 5, -10), colorParameter);
        Add(main, Quad(-30, 30, -20, 0, -20), SkyMaterial(shaders, SurfaceBlend.AddOne, SkyPass.After, new Color(0.1f, 0.1f, 0.1f), 1f, -60));

        // Known deviation (no saturation between blends): grey 0.6, AddOne white over it, black Mix 0.7 over that, in a
        // corner of the top-right quarter that no other check samples.
        foreach (var (blend, grey, alpha, priority) in new[] { (SurfaceBlend.Opaque, 0.6f, 1f, -95), (SurfaceBlend.AddOne, 1f, 1f, -94), (SurfaceBlend.Mix, 0f, 0.7f, -93) })
        {
            Add(skyCamera, Quad(SaturationX0, SaturationX1, SaturationY0, SaturationY1, -10), SkyMaterial(shaders, blend, SkyPass.Before, new Color(grey, grey, grey), alpha, priority));
        }
    }

    private const float SaturationX0 = 7f;
    private const float SaturationX1 = 9f;
    private const float SaturationY0 = 3f;
    private const float SaturationY1 = 5f;

    private bool Verify()
    {
        var image = GetViewport().GetTexture().GetImage();
        var w = image.GetWidth();
        var h = image.GetHeight();
        var main = _camera.Camera; // the sky camera copies its lens, so a camera-space point lands on the same pixel
        var centre = main.UnprojectPosition(main.GlobalTransform * new Vector3((SaturationX0 + SaturationX1) / 2f, (SaturationY0 + SaturationY1) / 2f, -10f));
        var saturation = (X: (int)centre.X, Y: (int)centre.Y);
        var sky = Bytes(0.8, 0.4, 0.2);
        var dimmed = (sky.R * 0.3, sky.G * 0.3, sky.B * 0.3);
        var world = Bytes(0.2, 0.6, 0.2);

        // Render-fidelity fix: the main view blends in byte space by default (Render/ByteSpaceBlending.cs), so the after
        // pass adds bytes there; with --blend-space=linear it adds in linear light as before.
        Func<(double R, double G, double B), (double R, double G, double B), (double R, double G, double B)> mainViewAdd =
            ByteSpaceBlending.Current == BlendSpace.Byte ? ByteAdd : LinearAdd;
        var checks = new (string Name, int X, int Y, (double R, double G, double B) Expected)[]
        {
            ("sky view opaque quad, raw bytes (top right)", w * 3 / 4, h / 4, sky),
            ("byte-space Mix 0.7 black over it, in priority order (top left)", w / 4, h / 4, dimmed),
            ($"world over the sky, after-pass add over the world in {ByteSpaceBlending.Current} space (bottom right)", w * 3 / 4, h * 3 / 4, mainViewAdd(world, Bytes(0.1, 0.1, 0.1))),
            ($"after-pass add over the sky background in {ByteSpaceBlending.Current} space (bottom left)", w / 4, h * 3 / 4, mainViewAdd(dimmed, Bytes(0.1, 0.1, 0.1))),
            ("a Color set on a vec4 parameter arrives as srgb_to_linear (pass raw colours as Vector4)", w / 2, (int)(h * 0.153), (Decode(153) * 255.0, Decode(153) * 255.0, Decode(153) * 255.0)),
            ("known deviation: the float sky buffer does not saturate between blends (retail shows 77)", saturation.X, saturation.Y, Bytes(0.48, 0.48, 0.48)),
        };

        var allOk = true;
        foreach (var (name, x, y, expected) in checks)
        {
            var c = image.GetPixel(x, y);
            var actual = (R: c.R8, G: c.G8, B: c.B8);
            var ok = Math.Abs(actual.R - expected.R) <= Tolerance && Math.Abs(actual.G - expected.G) <= Tolerance && Math.Abs(actual.B - expected.B) <= Tolerance;
            allOk &= ok;
            var log = ok ? (Action<string, string, (string, object?)[]>)Log.Info : Log.Error;
            log("skycheck", ok ? "PASS" : "FAIL", [("check", name), ("pixel", $"({actual.R}, {actual.G}, {actual.B})"), ("expected", $"({expected.R:0}, {expected.G:0}, {expected.B:0})")]);
        }

        if (_save is not null)
        {
            _save(System.IO.Path.Combine(System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..")), "docs", "screenshots", "m1", "sky-checks.png"));
        }

        Log.Info("skycheck", allOk ? "all sky checks hold" : "sky checks FAILED", ("checks", checks.Length));
        return allOk;
    }

    /// <summary>Raw value (0..1) as the byte retail would show.</summary>
    private static (double R, double G, double B) Bytes(double r, double g, double b) => (r * 255.0, g * 255.0, b * 255.0);

    /// <summary>The main view in byte space (the default): bytes add, saturating at the end.</summary>
    private static (double R, double G, double B) ByteAdd((double R, double G, double B) under, (double R, double G, double B) add) =>
        (Math.Min(under.R + add.R, 255.0), Math.Min(under.G + add.G, 255.0), Math.Min(under.B + add.B, 255.0));

    /// <summary>The main view in linear space (<c>--blend-space=linear</c>): decode both, add, encode.</summary>
    private static (double R, double G, double B) LinearAdd((double R, double G, double B) under, (double R, double G, double B) add) =>
        (Encode(Decode(under.R) + Decode(add.R)), Encode(Decode(under.G) + Decode(add.G)), Encode(Decode(under.B) + Decode(add.B)));

    private static double Decode(double b)
    {
        var c = b / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double Encode(double l)
    {
        var c = l <= 0.0031308 ? l * 12.92 : (1.055 * Math.Pow(l, 1.0 / 2.4)) - 0.055;
        return Math.Clamp(c, 0.0, 1.0) * 255.0;
    }

    private static ShaderMaterial SkyMaterial(SkyShaders shaders, SurfaceBlend blend, SkyPass pass, Color color, float alpha, int priority)
    {
        var material = new ShaderMaterial { Shader = shaders.Get(new SkySurfaceVariant(blend, false, TextureAddressMode.Wrap, false, pass)) };
        material.SetShaderParameter("solid_color", new Vector4(color.R, color.G, color.B, color.A));
        material.SetShaderParameter("vertex_alpha", alpha);
        material.SetShaderParameter("emissive", 1f);
        material.RenderPriority = priority;
        return material;
    }

    /// <summary>A camera-space quad facing the camera, both windings (one is culled).</summary>
    private static ArrayMesh Quad(float x0, float x1, float y0, float y1, float z)
    {
        var mesh = new ArrayMesh();
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[] { new(x0, y0, z), new(x1, y0, z), new(x1, y1, z), new(x0, y1, z) };
        arrays[(int)Mesh.ArrayType.Normal] = new Vector3[] { Vector3.Back, Vector3.Back, Vector3.Back, Vector3.Back };
        arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[] { Vector2.Zero, Vector2.Right, Vector2.One, Vector2.Down };
        arrays[(int)Mesh.ArrayType.Custom0] = new float[] { 0f, 0f, 0f, 0f };
        arrays[(int)Mesh.ArrayType.Index] = new int[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: MeshFactory.ZeroNormalFormat);
        mesh.CustomAabb = SkyModels.CullBounds;
        return mesh;
    }

    private void Add(Node3D parent, ArrayMesh mesh, Material material)
    {
        var instance = new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        instance.SetSurfaceOverrideMaterial(0, material);
        parent.AddChild(instance);
        _created.Add(instance);
    }
}
