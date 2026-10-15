using Godot;
using Lifestone.Client.Audio;
using Lifestone.Client.Interiors;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.Rendering;
using Lifestone.Client.Streaming;
using Lifestone.Data.Audio;
using Lifestone.Data.Effects;
using Lifestone.Data.Geometry;
using Lifestone.Data.Terrain;
using Lifestone.Game.Audio;
using Lifestone.Game.Core;
using Lifestone.Game.CreatureMode;
using Lifestone.Game.Render;
using Lifestone.Game.SpellViewer;
using Lifestone.Game.World.Sky;
using AcVector2 = System.Numerics.Vector2;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.PortalSpace;

/// <summary>
/// <c>--portal-space-checks</c>: verifies, in the running engine, the shared pieces the portal view stands on, then quits
/// with 0 when every check holds and 1 otherwise (M2 W2-pre(a); the portal track's wave-2 stage adds the rest of its list,
/// m2-plan G1 W5). Run it off-screen:
/// <c>tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 120 -GameArgs:'--portal-space-checks'</c>.
/// Needs no dats and no world (Main's check mode installs nothing live).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><b>Camera roll</b> (<see cref="AcToGodot.CameraRotation(double, double, double)"/>): a roll turns the camera about
/// its view axis right-handedly in AC's frame and leaves the view direction alone.</item>
/// <item><b>The widest lens</b> (<see cref="ViewFrustum.FromLens"/>): the zoom's 179.885° lens still makes a valid
/// pyramid (D31).</item>
/// <item><b>The world arbiter</b> (<see cref="WorldVisibility"/>): reference counts drive the main viewport's
/// <c>Disable3D</c>, and a <c>SubViewport</c> keeps rendering while it is set (the tunnel's view, the sky's).</item>
/// <item><b>Creature-mode lighting</b> (<see cref="SurfaceLighting.CreatureMode"/>, the <c>ac_cm_*</c> globals), on real
/// surface shaders and known quads: <c>clamp(ambient + colour x intensity x max(0, N.L))</c> with N.L against the direction
/// <i>towards</i> the light; per-channel colours; the portal scene's own values (ambient 0.3, intensity 2). Under world
/// globals set to hostile values (a red sun, a blue ambient, a fog that swallows everything) the creature-mode quads do not
/// change, while two world-lit control quads do, so the independence is not vacuous.</item>
/// <item><b>The lens</b> (<see cref="CameraLenses"/>, <see cref="CameraLens"/>): for f = √3, 0.4, 0.01 and 0.001, centred and
/// off-centre, a <c>Camera3D</c> set to the portal zoom's frustum projects the corners and edge midpoints of its view where
/// <see cref="CameraLens.Sides"/> says (read back from the camera's own projection matrix), and the interiors' frustum
/// (<see cref="ViewFrustum.FromLens"/>) contains the rays just inside the view's edges and not those just outside.</item>
/// <item><b>The zoom renders</b>: a floor drawn through the frustum at each of those focal distances covers exactly the rows
/// below the horizon (D31: no cap, near plane down to 0.00025 m). Godot logs a harmless engine message while f is below
/// about 0.01 (<c>Condition "!res" is true</c>, <c>Projection::get_endpoints</c>), which is not a Lifestone ERROR line;
/// docs/portal-space.md §6.</item>
/// <item><b>The sky view</b> (<see cref="SkyView.Follow"/>): copies the projection mode, near plane, size and offset of a
/// frustum camera, the field of view of a perspective one, and pauses.</item>
/// <item><b>The tunnel's surfaces</b> (<see cref="PortalTunnelView"/>, needs the dats): a readback of the lit view counts the wall's
/// blue-grey and the purple shapes at six spin frames, and fails when a surface is missing; the look itself is not judged.</item>
/// <item>A <b>known deviation, pinned</b>: <c>Camera3D.ProjectRayNormal</c> ignores a frustum offset, so picking under the
/// SmartBox frame (D40) unprojects from the projection matrix's elements.</item>
/// </list>
/// </remarks>
public partial class PortalSpaceChecks : Node
{
    private const int Tolerance = 3;
    private const float TangentTolerance = 2e-4f;
    private static readonly Vector2I ViewportSize = new(128, 32);

    private readonly List<(string Name, Outcome Outcome, string Detail)> _results = new();

    private enum Outcome
    {
        Pass,
        Fail,
        Skipped,
    }

    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        var exitCode = 1;
        try
        {
            CheckCameraRoll();
            CheckWidestLensFrustum();
            if (DisplayServer.GetName() == "headless")
            {
                Record("render checks", Outcome.Skipped, "headless: no renderer; run off-screen with the Dummy audio driver, not --headless");
            }
            else
            {
                await CheckWorldVisibilityAsync();
                await CheckCreatureModeLightingAsync();
                await CheckLensesAsync();
                await CheckZoomRendersAsync();
                await CheckSkyViewAsync();
                await CheckWorldLensAsync();
                await CheckByteSpaceCompositionAsync();
                CheckNoticeLine();
                CheckOfflineSequence();
                CheckUiAssetChain();
                await CheckTunnelSurfacesAsync();
                CheckPortalStreams();
                await CheckAudioBusesAsync();
                await CheckTunnelHumsAsync();
                await CheckPresenterRulesAsync();
                await CheckDeferredJumpAsync();
            }

            exitCode = _results.Any(r => r.Outcome == Outcome.Fail) ? 1 : 0;
        }
        catch (Exception ex)
        {
            Log.Exception("portalcheck", "portal-space checks crashed", ex);
        }

        Log.Info(
            "portalcheck",
            exitCode == 0 ? "all portal-space checks hold" : "PORTAL-SPACE CHECKS FAILED",
            ("pass", _results.Count(r => r.Outcome == Outcome.Pass)),
            ("fail", _results.Count(r => r.Outcome == Outcome.Fail)),
            ("skipped", _results.Count(r => r.Outcome == Outcome.Skipped)));
        Log.FlushToGodot();
        GetTree().Quit(exitCode);
    }

    private void Record(string name, Outcome outcome, string detail)
    {
        _results.Add((name, outcome, detail));
        var verdict = outcome.ToString().ToUpperInvariant();
        if (outcome == Outcome.Fail)
        {
            Log.Error("portalcheck", $"{verdict} {name}", ("detail", detail));
        }
        else
        {
            Log.Info("portalcheck", $"{verdict} {name}", ("detail", detail));
        }
    }

    private void Check(string name, bool ok, string detail) => Record(name, ok ? Outcome.Pass : Outcome.Fail, detail);

    // ---------------------------------------------------------------------------------------------------------------
    // Camera roll

    private void CheckCameraRoll()
    {
        static (AcVector3 Forward, AcVector3 Up, AcVector3 Right) Axes(Quaternion rotation)
        {
            var basis = new Basis(rotation);
            return (AcToGodot.ToAc(-basis.Z), AcToGodot.ToAc(basis.Y), AcToGodot.ToAc(basis.X));
        }

        static bool Same(AcVector3 a, AcVector3 b) => (a - b).Length() < 1e-5f;

        var flat = Axes(AcToGodot.CameraRotation(0, 0, 0));
        var rolled = Axes(AcToGodot.CameraRotation(0, 0, 90));
        var heading = Axes(AcToGodot.CameraRotation(90, 30, 45));
        var expectedForward = new AcVector3(MathF.Cos(30f * MathF.PI / 180f), 0f, MathF.Sin(30f * MathF.PI / 180f)); // heading 90 (east), pitch 30 up
        var twoArguments = AcToGodot.CameraRotation(37, -12);
        var threeArguments = AcToGodot.CameraRotation(37, -12, 0);
        var ok = Same(flat.Forward, AcVector3.UnitY) && Same(flat.Up, AcVector3.UnitZ) && Same(flat.Right, AcVector3.UnitX)
            && Same(rolled.Forward, AcVector3.UnitY) && Same(rolled.Up, AcVector3.UnitX) && Same(rolled.Right, -AcVector3.UnitZ)
            && Same(heading.Forward, expectedForward)
            && twoArguments.IsEqualApprox(threeArguments);
        Check(
            "camera roll turns about the view axis, right-handed in AC's frame",
            ok,
            $"roll 90: forward {rolled.Forward} (north), up {rolled.Up} (east), right {rolled.Right} (down); heading 90 pitch 30 roll 45: forward {heading.Forward}");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The widest lens

    private void CheckWidestLensFrustum()
    {
        var widest = new PortalLens(0.001f, true);
        var lens = CameraLens.FromPortal(widest);
        var aspect = 16f / 9f;
        string detail;
        bool ok;
        try
        {
            var frustum = ViewFrustum.FromLens(AcVector3.UnitY, AcVector3.UnitZ, lens, aspect, marginDegrees: 0f);
            AcVector3 At(double degrees, double around) => new(
                (float)(Math.Sin(degrees * Math.PI / 180.0) * Math.Cos(around)),
                (float)Math.Cos(degrees * Math.PI / 180.0),
                (float)(Math.Sin(degrees * Math.PI / 180.0) * Math.Sin(around)));
            var up89 = At(89.0, Math.PI / 2);
            var up90_5 = At(90.5, Math.PI / 2);
            var left89 = At(89.0, Math.PI);
            var behind = -AcVector3.UnitY;
            ok = frustum.ContainsDirection(AcVector3.UnitY) && frustum.ContainsDirection(up89) && frustum.ContainsDirection(left89)
                && !frustum.ContainsDirection(up90_5) && !frustum.ContainsDirection(behind);
            detail = $"f=0.001: vertical {lens.EffectiveVerticalFovDegrees:0.###} deg; forward, 89 deg up and 89 deg left inside; 90.5 deg up and behind outside";
        }
        catch (ArgumentException ex)
        {
            ok = false;
            detail = ex.Message;
        }

        Check("the widest portal lens (179.885 deg) makes a valid view pyramid", ok, detail);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The world arbiter

    private async Task CheckWorldVisibilityAsync()
    {
        var arbiter = GameServices.Instance.WorldVisibility;
        var root = GetViewport();
        var states = new List<string>();
        void Note(string step) => states.Add($"{step}: visible={arbiter.Visible} disable3d={root.Disable3D} requests={arbiter.Requests}");

        var changes = 0;
        void OnChanged(bool visible) => changes++;
        arbiter.Changed += OnChanged;
        Note("start");
        arbiter.Hide("check-a");
        arbiter.Hide("check-b");
        Note("two hides");
        var hiddenBoth = !arbiter.Visible && root.Disable3D && arbiter.Requests == 2;
        arbiter.Unhide("check-a");
        Note("one release");
        var hiddenOne = !arbiter.Visible && root.Disable3D && arbiter.Requests == 1;

        // A SubViewport keeps rendering while the main viewport's 3D is disabled: a known clear colour reads back.
        var viewport = new SubViewport
        {
            Size = ViewportSize,
            OwnWorld3D = true,
            TransparentBg = false,
            Msaa3D = Viewport.Msaa.Disabled,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);
        viewport.AddChild(new Camera3D { Current = true, Environment = LinearEnvironment(new Color(0f, 0.5f, 0f)) });
        await FramesAsync(4);
        var pixel = Bytes(viewport.GetTexture().GetImage().GetPixel(ViewportSize.X / 2, ViewportSize.Y / 2));
        viewport.QueueFree();

        arbiter.Unhide("check-b");
        Note("all released");
        var shown = arbiter.Visible && !root.Disable3D && arbiter.Requests == 0;
        arbiter.Follow("check-c", worldVisible: false);
        arbiter.Follow("check-c", worldVisible: false);
        var followHeld = arbiter.Held("check-c") == 1 && !arbiter.Visible && root.Disable3D;
        arbiter.Follow("check-c", worldVisible: true);
        var followReleased = arbiter.Held("check-c") == 0 && arbiter.Visible && !root.Disable3D;
        arbiter.Changed -= OnChanged;
        Note("end");

        Check(
            "world visibility: hide requests are reference-counted and Disable3D follows them",
            hiddenBoth && hiddenOne && shown && followHeld && followReleased && changes == 4,
            $"{string.Join("; ", states)}; Follow holds exactly one request: {followHeld}, releases it: {followReleased}; changes seen: {changes} (expected 4)");
        Check(
            "a SubViewport keeps rendering while the main viewport's 3D is disabled",
            Near(pixel, 0, 128, 0),
            $"read back {pixel} from a SubViewport cleared to (0, 128, 0) while the arbiter held a request");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Creature-mode lighting

    private async Task CheckCreatureModeLightingAsync()
    {
        var shaders = new SurfaceShaders();
        var plain = new SurfaceShaderVariant(SurfaceBlend.Opaque, false, true, SurfaceCull.None, TextureAddressMode.Clamp, Textured: false, Fog: false);
        var creature = plain.WithLighting(SurfaceLighting.CreatureMode);
        var worldFogged = plain with { Fog = true };
        var towardCamera = AcToGodot.Direction(new AcVector3(0f, -1f, 0f)); // AC south: towards the test camera (Godot +Z)
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.MaxMipLevel, 3f);

        // Probe 1: ambient 0.2, a light of colour 0.5 towards the camera. Four normals; the world globals are hostile.
        SetHostileWorld(sunColor: new Vector3(0.9f, 0.9f, 0.9f), ambient: new Vector3(0.9f, 0.9f, 0.9f), fogColor: new Vector3(0f, 0f, 1f));
        CreatureModeLighting.Apply(new Vector3(0.2f, 0.2f, 0.2f), towardCamera, new Vector3(0.5f, 0.5f, 0.5f));
        var image = await RenderQuadsAsync(
            shaders,
            [
                (-3f, creature, new AcVector3(0, -1, 0)),
                (-1f, creature, new AcVector3(1, 0, 0)),
                (1f, creature, new AcVector3(0.8660254f, -0.5f, 0)),
                (3f, creature, new AcVector3(0, 1, 0)),
            ]);
        Pixel(image, 16, "creature mode: facing the light is ambient + colour x intensity (0.2 + 0.5)", 179, 179, 179);
        Pixel(image, 48, "creature mode: a surface edge-on to the light gets the ambient alone (N.L = 0)", 51, 51, 51);
        Pixel(image, 80, "creature mode: N.L = 0.5 gets ambient + half the light", 115, 115, 115);
        Pixel(image, 112, "creature mode: facing away gets the ambient alone, never negative light", 51, 51, 51);

        // Probe 2: per-channel ambient and colour; and the two world-lit controls under the same hostile world.
        SetHostileWorld(sunColor: new Vector3(1f, 0f, 0f), ambient: new Vector3(0f, 0f, 0.5f), fogColor: new Vector3(0f, 0.8f, 0f));
        CreatureModeLighting.Apply(new Vector3(0f, 0f, 0.3f), towardCamera, new Vector3(1f, 0.5f, 0f));
        image = await RenderQuadsAsync(
            shaders,
            [
                (-3f, creature, new AcVector3(0, -1, 0)),
                (-1f, creature, new AcVector3(0, 1, 0)),
                (1f, plain, new AcVector3(0, -1, 0)),
                (3f, worldFogged, new AcVector3(0, -1, 0)),
            ]);
        Pixel(image, 16, "creature mode: per-channel light (1, 0.5, 0) over ambient (0, 0, 0.3) facing the light", 255, 128, 77);
        Pixel(image, 48, "creature mode: per-channel ambient alone when facing away", 0, 0, 77);
        Pixel(image, 80, "control: the world-lit quad shows the hostile red sun over the blue ambient (the world globals are live)", 255, 0, 128);
        Pixel(image, 112, "control: the fogged world quad shows the hostile fog colour (fog is live for the world, never for creature mode)", 0, 204, 0);

        // Probe 3: the portal scene's own values: ambient 0.3, white light of intensity 2 along its direction.
        SetHostileWorld(sunColor: new Vector3(0f, 1f, 0f), ambient: new Vector3(1f, 0f, 0f), fogColor: new Vector3(1f, 0f, 1f));
        CreatureModeLighting.ApplyPortalScene();
        var unit = PortalSpaceScene.LightDirectionUnit;
        var edgeOn = AcVector3.Normalize(AcVector3.Cross(unit, AcVector3.UnitZ)); // perpendicular to the light
        image = await RenderQuadsAsync(
            shaders,
            [
                (-3f, creature, unit),
                (-1f, creature, -unit),
                (1f, creature, edgeOn),
            ]);
        var direction = CreatureModeLighting.PortalSceneDirection;
        Pixel(image, 16, "portal scene: facing the light saturates (ambient 0.3 + white x intensity 2)", 255, 255, 255);
        Pixel(image, 48, "portal scene: facing away shows the ambient 0.3 alone (77)", 77, 77, 77);
        Pixel(image, 80, "portal scene: edge-on to the light shows the ambient 0.3 alone (77)", 77, 77, 77);
        Check("portal scene light direction is the AC vector (0.3, -1.9, 0.65) normalised, in Godot axes", (direction - new Vector3(0.147754f, 0.320126f, 0.935773f)).Length() < 1e-4f, $"{direction}");
    }

    /// <summary>The world's lighting globals set to values that would change any quad that read them.</summary>
    private static void SetHostileWorld(Vector3 sunColor, Vector3 ambient, Vector3 fogColor)
    {
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.SunDirection, new Vector3(0f, 0f, 1f));
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.SunColor, sunColor);
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.Ambient, ambient);
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.FogColor, fogColor);
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.FogStart, 0f);
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.FogEnd, 0.001f);
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.FogEnabled, true);
        RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.IndoorAmbient, new Vector3(1f, 1f, 0f));
        RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.IndoorAmbientOn, true);
    }

    /// <summary>
    /// Quads (1.6 m, in the AC x/z plane, vertex normals as given, AC south side towards the camera) at the given x centres
    /// with the given variants, rendered on a 128 x 32 SubViewport; returns the image.
    /// </summary>
    private async Task<Image> RenderQuadsAsync(SurfaceShaders shaders, (float X, SurfaceShaderVariant Variant, AcVector3 Normal)[] quads)
    {
        var viewport = new SubViewport
        {
            Size = ViewportSize,
            OwnWorld3D = true,
            TransparentBg = false,
            Msaa3D = Viewport.Msaa.Disabled,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);
        var camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 2f, // y in [-1, 1]; x in [-4, 4] at 4:1
            Near = 0.1f,
            Far = 40f,
            Environment = LinearEnvironment(new Color(0f, 0f, 0f)),
        };
        viewport.AddChild(camera);
        camera.Position = new Vector3(0, 0, 5);
        camera.Current = true;
        foreach (var (x, variant, normal) in quads)
        {
            var mesh = MeshFactory.Build(MeshFactory.Prepare(Quad(x, normal)));
            var material = new ShaderMaterial { Shader = shaders.Get(variant) };
            material.SetShaderParameter("solid_color", Colors.White);
            mesh.SurfaceSetMaterial(0, material);
            viewport.AddChild(new MeshInstance3D { Mesh = mesh });
        }

        await FramesAsync(4);
        var image = viewport.GetTexture().GetImage();
        viewport.QueueFree();
        return image;
    }

    /// <summary>
    /// A 1.6 m square quad in the AC x/z plane centred on x = <paramref name="centreX"/>, facing AC south (towards the test
    /// camera on Godot +Z), with the given vertex normal. The normal is an attribute: how a quad is lit does not depend on
    /// which way its triangles face.
    /// </summary>
    private static MeshData Quad(float centreX, AcVector3 normal)
    {
        AcVector3[] positions =
        [
            new(centreX - 0.8f, 0, -0.8f),
            new(centreX + 0.8f, 0, -0.8f),
            new(centreX + 0.8f, 0, 0.8f),
            new(centreX - 0.8f, 0, 0.8f),
        ];
        var normals = Enumerable.Repeat(normal, 4).ToArray();
        var uvs = new[] { new AcVector2(0, 1), new AcVector2(1, 1), new AcVector2(1, 0), new AcVector2(0, 0) };
        return new MeshData([new SubMesh(0x08000000, MeshSide.Positive, TextureAddressMode.Clamp, positions, normals, uvs, [0, 1, 2, 0, 2, 3])]);
    }

    private void Pixel(Image image, int x, string name, int r, int g, int b)
    {
        var got = Bytes(image.GetPixel(x, ViewportSize.Y / 2));
        Check(name, Near(got, r, g, b), $"got {got}, expected ({r}, {g}, {b}) +-{Tolerance}");
    }

    private static (int R, int G, int B) Bytes(Color c) => (c.R8, c.G8, c.B8);

    private static bool Near((int R, int G, int B) got, int r, int g, int b) =>
        Math.Abs(got.R - r) <= Tolerance && Math.Abs(got.G - g) <= Tolerance && Math.Abs(got.B - b) <= Tolerance;

    private static Godot.Environment LinearEnvironment(Color background) => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Color,
        BackgroundColor = background,
        TonemapMode = Godot.Environment.ToneMapper.Linear,
        TonemapExposure = 1f,
        AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
        ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
    };

    private async Task FramesAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The lens

    private static readonly float[] Focals = [1.7320508f, 0.4f, 0.01f, 0.001f];

    private async Task CheckLensesAsync()
    {
        var viewport = new SubViewport
        {
            Size = ViewportSize,
            OwnWorld3D = true,
            Msaa3D = Viewport.Msaa.Disabled,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(viewport);
        var camera = new Camera3D { Near = 0.1f, Far = 6000f, Current = true };
        viewport.AddChild(camera);
        await FramesAsync(2);

        var aspect = (float)ViewportSize.X / ViewportSize.Y;
        foreach (var focal in Focals)
        {
            foreach (var offset in new[] { (X: 0f, Y: 0f), (X: 0.2f, Y: -0.1f) })
            {
                var portal = new PortalLens(focal, true);
                var lens = CameraLens.FromPortal(portal, offset.X * portal.FrustumHeightAtNear, offset.Y * portal.FrustumHeightAtNear);
                CameraLenses.Apply(camera, lens);
                var read = CameraLenses.Of(camera);
                var sides = lens.Sides(aspect);

                // The sides the camera's own projection matrix gives (an off-centre frustum is the matrix's third column); the
                // matrix is read element by element: Godot's Projection.Inverse fails on these tiny elements.
                var matrix = camera.GetCameraProjection();
                var got = (Left: TangentAt(matrix, -1f, 0f).X, Right: TangentAt(matrix, 1f, 0f).X, Bottom: TangentAt(matrix, 0f, -1f).Y, Top: TangentAt(matrix, 0f, 1f).Y);
                var ok = Close(got.Left, sides.Left) && Close(got.Right, sides.Right) && Close(got.Bottom, sides.Bottom) && Close(got.Top, sides.Top) && read == lens;

                // The interiors' frustum: rays 2 px inside the edges are in, 2 px outside are out.
                var frustum = ViewFrustum.FromLens(AcVector3.UnitY, AcVector3.UnitZ, lens, aspect, marginDegrees: 0f);
                var inside = 1f - (4f / ViewportSize.X);
                var outsideX = 1f + (4f / ViewportSize.X);
                var insideY = 1f - (4f / ViewportSize.Y);
                var outsideY = 1f + (4f / ViewportSize.Y);
                bool In(float x, float y) => frustum.ContainsDirection(AcToGodot.ToAc(DirectionAt(matrix, x, y)));
                var frustumOk = In(inside, 0f) && In(-inside, 0f) && In(0f, insideY) && In(0f, -insideY)
                    && !In(outsideX, 0f) && !In(-outsideX, 0f) && !In(0f, outsideY) && !In(0f, -outsideY);

                Check(
                    $"lens f={focal:0.###}{(offset.X != 0f ? " off-centre" : string.Empty)}: Camera3D frustum projects the sides where CameraLens says, and the interiors' pyramid matches",
                    ok && frustumOk,
                    $"tangents L/R/B/T got {got.Left:0.####}/{got.Right:0.####}/{got.Bottom:0.####}/{got.Top:0.####} expected {sides.Left:0.####}/{sides.Right:0.####}/{sides.Bottom:0.####}/{sides.Top:0.####}; " +
                    $"vertical {portal.VerticalFovDegrees:0.###} deg, near {portal.Near}, size {portal.FrustumHeightAtNear:0.####}; lens read back equal: {read == lens}; frustum edges ok: {frustumOk}");
            }
        }

        // Known deviation, pinned (for picking, D40): Camera3D.ProjectRayNormal assumes a centred frustum, so under an offset it
        // is not the ray through the pixel; picking must unproject from the matrix elements as above.
        var shiftedLens = CameraLens.Frustum(0.2f, 0.1f, 0.05f, -0.03f);
        CameraLenses.Apply(camera, shiftedLens);
        var shiftedMatrix = camera.GetCameraProjection();
        var pixel = new Vector2(0f, 0f); // the view's top-left corner
        var exact = DirectionAt(shiftedMatrix, -1f, 1f);
        var engine = camera.ProjectRayNormal(pixel);
        Check(
            "known deviation: Camera3D.ProjectRayNormal ignores a frustum offset (picking unprojects from the matrix elements)",
            exact.AngleTo(engine) > 0.01f,
            $"top-left corner under offset (0.05, -0.03): the ray through the pixel is {exact}, ProjectRayNormal gives {engine} ({exact.AngleTo(engine) * 180f / MathF.PI:0.##} deg apart)");

        // The game lens: a perspective camera reads back as the perspective lens, and the interiors build the same pyramid.
        camera.Fov = 60f;
        camera.SetPerspective(60f, 0.1f, 6000f);
        var perspective = CameraLenses.Of(camera);
        var fromCamera = ViewFrustum.FromCamera(AcVector3.UnitY, AcVector3.UnitZ, 60f, aspect);
        var fromLens = ViewFrustum.FromLens(AcVector3.UnitY, AcVector3.UnitZ, perspective, aspect);
        Check(
            "the game lens: a 60 deg perspective camera reads back as a perspective lens and gives the M1 pyramid unchanged",
            !perspective.IsFrustum && perspective.VerticalFovDegrees == 60f && fromCamera == fromLens,
            $"{perspective}");

        viewport.QueueFree();
    }

    /// <summary>How far from zero a tangent read back from a projection matrix may be and still be on the axis: the matrix's float rounding grows with the side widths (thousands at f = 0.001).</summary>
    private static float AxisTolerance(float sideWidth) => 1e-4f + (1e-6f * sideWidth);

    private static bool Relative(float a, float b) => MathF.Abs(a - b) <= 1e-4f * MathF.Max(MathF.Abs(a), MathF.Abs(b));

    private static bool Close(float got, float expected) => MathF.Abs(got - expected) <= TangentTolerance * MathF.Max(1f, MathF.Abs(expected));

    /// <summary>
    /// The tangents (x/-z, y/-z) of the eye-space ray through the normalised-device point (<paramref name="ndcX"/>,
    /// <paramref name="ndcY"/>), read from a frustum projection matrix: its first two diagonal elements are 2n/(r-l) and
    /// 2n/(t-b), its third column the offset terms (r+l)/(r-l) and (t+b)/(t-b).
    /// </summary>
    private static Vector2 TangentAt(Projection matrix, float ndcX, float ndcY) =>
        new((ndcX + matrix.Z.X) / matrix.X.X, (ndcY + matrix.Z.Y) / matrix.Y.Y);

    /// <summary>The eye-space direction of the same ray (the eye is the origin; the camera looks along -Z).</summary>
    private static Vector3 DirectionAt(Projection matrix, float ndcX, float ndcY)
    {
        var tangent = TangentAt(matrix, ndcX, ndcY);
        return new Vector3(tangent.X, tangent.Y, -1f).Normalized();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The zoom renders

    /// <summary>
    /// A floor plane one metre below the eye, drawn through a frustum camera at each focal distance of the zoom: every row
    /// below the view's centre shows the floor (a ray with a downward tangent meets it), every row above shows the clear
    /// colour, whatever the field of view, centred or shifted (docs/portal-space.md §6). Pins that Godot's culling and depth
    /// handle the near plane (f/4, down to 0.00025 m) and the 179.885 deg frustum; engine errors from a degenerate
    /// frustum would appear in the log beside the verdicts.
    /// </summary>
    private async Task CheckZoomRendersAsync()
    {
        var shaders = new SurfaceShaders();
        var variant = new SurfaceShaderVariant(SurfaceBlend.Opaque, false, true, SurfaceCull.None, TextureAddressMode.Clamp, Textured: false, Fog: false);
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.SunColor, Vector3.Zero);
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.Ambient, new Vector3(0.6f, 0.6f, 0.6f));
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.FogEnabled, false);
        foreach (var focal in Focals)
        {
            foreach (var shifted in new[] { false, true })
            {
                var viewport = new SubViewport
                {
                    Size = ViewportSize,
                    OwnWorld3D = true,
                    TransparentBg = false,
                    Msaa3D = Viewport.Msaa.Disabled,
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                };
                AddChild(viewport);
                var camera = new Camera3D { Near = 0.1f, Far = 6000f, Environment = LinearEnvironment(new Color(0f, 0f, 0.8f)) };
                viewport.AddChild(camera);
                var portal = new PortalLens(focal, true);
                var vertical = shifted ? 0.15f * portal.FrustumHeightAtNear : 0f; // the frustum's centre moves up: more of the view is above the horizon
                CameraLenses.Apply(camera, CameraLens.FromPortal(portal, 0f, vertical));
                camera.Current = true;

                // The floor: AC z = -1, 400 m square around the eye, normal up, lit by the ambient alone (0.6 -> 153).
                var floor = new MeshData([new SubMesh(
                    0x08000000,
                    MeshSide.Positive,
                    TextureAddressMode.Clamp,
                    [new(-200, -200, -1), new(200, -200, -1), new(200, 200, -1), new(-200, 200, -1)],
                    Enumerable.Repeat(AcVector3.UnitZ, 4).ToArray(),
                    [new AcVector2(0, 1), new AcVector2(1, 1), new AcVector2(1, 0), new AcVector2(0, 0)],
                    [0, 1, 2, 0, 2, 3])]);
                var mesh = MeshFactory.Build(MeshFactory.Prepare(floor));
                var material = new ShaderMaterial { Shader = shaders.Get(variant) };
                material.SetShaderParameter("solid_color", Colors.White);
                mesh.SurfaceSetMaterial(0, material);
                viewport.AddChild(new MeshInstance3D { Mesh = mesh, CustomAabb = new Aabb(new Vector3(-300, -2, -300), new Vector3(600, 4, 600)) });
                await FramesAsync(4);
                var image = viewport.GetTexture().GetImage();
                viewport.QueueFree();

                // Where the horizon row falls (ndc y of the tangent 0): with a shift of +0.15 height the centre is above it.
                var horizonRow = shifted ? (int)MathF.Round(ViewportSize.Y * (0.5f + 0.15f)) : ViewportSize.Y / 2;
                var above = Bytes(image.GetPixel(ViewportSize.X / 2, Math.Max(0, horizonRow - 4)));
                var below = Bytes(image.GetPixel(ViewportSize.X / 2, Math.Min(ViewportSize.Y - 1, horizonRow + 4)));
                var corner = Bytes(image.GetPixel(2, ViewportSize.Y - 2));
                Check(
                    $"the zoom renders at f={focal:0.###}{(shifted ? " off-centre" : string.Empty)}: floor below the horizon, clear colour above",
                    Near(above, 0, 0, 204) && Near(below, 153, 153, 153) && Near(corner, 153, 153, 153),
                    $"above the horizon {above} (expected (0, 0, 204)), below {below} and in the bottom corner {corner} (expected (153, 153, 153)); near {portal.Near} m, vertical {portal.VerticalFovDegrees:0.###} deg");
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The sky view

    private async Task CheckSkyViewAsync()
    {
        var host = new SubViewport
        {
            Size = ViewportSize,
            OwnWorld3D = true,
            Msaa3D = Viewport.Msaa.Disabled,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(host);
        var main = new Camera3D { Near = 0.1f, Far = 6000f, Current = true };
        host.AddChild(main);
        var sky = SkyView.Create();
        AddChild(sky);
        await FramesAsync(2);

        var portal = new PortalLens(0.001f, true);
        CameraLenses.Apply(main, CameraLens.FromPortal(portal, 0.05f, -0.02f));
        sky.Follow(main, new Vector3(1f, 2f, 3f), ViewportSize);
        await FramesAsync(2);
        var a = main.GetCameraProjection();
        var b = sky.Camera.GetCameraProjection();
        var frustum = sky.Camera.Projection == Camera3D.ProjectionType.Frustum
            && sky.Camera.Near == main.Near
            && sky.Camera.Size == main.Size
            && sky.Camera.FrustumOffset == main.FrustumOffset
            && sky.Size == ViewportSize
            && sky.Camera.Position.IsEqualApprox(new Vector3(1f, 2f, 3f))
            && Relative(a.X.X, b.X.X) && Relative(a.Y.Y, b.Y.Y) && Relative(a.Z.X, b.Z.X) && Relative(a.Z.Y, b.Z.Y);
        var detailFrustum = $"frustum: projection {sky.Camera.Projection}, near {sky.Camera.Near} (main {main.Near}), size {sky.Camera.Size} (main {main.Size}), offset {sky.Camera.FrustumOffset} (main {main.FrustumOffset}); " +
            $"matrix xx/yy/zx/zy {b.X.X:0.#####}/{b.Y.Y:0.#####}/{b.Z.X:0.#####}/{b.Z.Y:0.#####} (main {a.X.X:0.#####}/{a.Y.Y:0.#####}/{a.Z.X:0.#####}/{a.Z.Y:0.#####})";

        main.SetPerspective(75f, 0.1f, 6000f);
        sky.Follow(main, Vector3.Zero, ViewportSize);
        var perspective = sky.Camera.Projection == Camera3D.ProjectionType.Perspective && Mathf.IsEqualApprox(sky.Camera.Fov, 75f) && sky.Camera.Near == SkyView.NearClip;

        sky.Paused = true;
        var paused = sky.Paused && sky.RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled;
        sky.Paused = false;
        var resumed = !sky.Paused && sky.RenderTargetUpdateMode == SubViewport.UpdateMode.Always;
        Check(
            "the sky view follows the camera's lens: frustum size, offset and near, or the field of view and the sky's own near; and pauses",
            frustum && perspective && paused && resumed,
            $"{detailFrustum}; perspective follows fov and keeps near {SkyView.NearClip}: {perspective}; paused: {paused}; resumed: {resumed}");

        sky.QueueFree();
        host.QueueFree();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // G1 W4 and W5: the portal view over the real assets

    /// <summary>True when the dats are open; otherwise records <paramref name="name"/> as skipped.</summary>
    private bool NeedDats(string name, out GameServices services)
    {
        services = GameServices.Instance;
        if (services.Dats is not null)
        {
            return true;
        }

        Record(name, Outcome.Skipped, "the retail dats are not available");
        return false;
    }

    // --- the world lens ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <see cref="WorldLens"/> on a real camera: for f = f_game, 0.4, 0.01 and 0.001 the zoom's frustum (retail's size, near f/4 below
    /// 0.4) is exactly <see cref="CameraLens.FromPortal(in PortalLens, float, float)"/> with a zero offset for the whole window, its
    /// sides are where <see cref="CameraLens.Sides"/> says (read from the camera's own matrix), and an off-centre
    /// <c>GameViewRect</c> hook puts the view's axis at the asked fractions of the window; releasing the zoom gives the rest
    /// perspective back, and the rest focal distance is the camera's own f_game.
    /// </summary>
    private async Task CheckWorldLensAsync()
    {
        var host = new SubViewport
        {
            Size = ViewportSize,
            OwnWorld3D = true,
            Msaa3D = Viewport.Msaa.Disabled,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(host);
        var camera = new Camera3D { Fov = 60f, Near = 0.1f, Far = 6000f, Current = true };
        host.AddChild(camera);
        await FramesAsync(2);

        var lens = new WorldLens(camera);
        var aspect = (float)ViewportSize.X / ViewportSize.Y;
        Check(
            "world lens: the rest lens is the camera's own perspective and f_game is its focal distance (sqrt 3 at 60 deg)",
            lens.Rest == CameraLens.Perspective(60f, 0.1f) && MathF.Abs(lens.RestFocal - 1.7320508f) < 1e-5f && !lens.Overridden,
            $"rest {lens.Rest}, f_game {lens.RestFocal}");

        const float x = 0.25f;
        const float y = 0.7f;
        foreach (var focal in Focals)
        {
            var portal = new PortalLens(focal, true);

            // Whole window: no offset at all.
            lens.ViewCentre = static () => (0.5f, 0.5f);
            lens.Follow(portal);
            var expected = CameraLens.FromPortal(portal);
            var read = lens.Current;
            var centredMatrix = camera.GetCameraProjection();
            var sides = expected.Sides(aspect);
            var centredOk = read == expected && read.OffsetX == 0f && read.OffsetY == 0f && lens.Overridden
                && Close(TangentAt(centredMatrix, -1f, 0f).X, sides.Left) && Close(TangentAt(centredMatrix, 1f, 0f).X, sides.Right)
                && Close(TangentAt(centredMatrix, 0f, -1f).Y, sides.Bottom) && Close(TangentAt(centredMatrix, 0f, 1f).Y, sides.Top)
                && Close(TangentAt(centredMatrix, 0f, 0f).X, 0f) && Close(TangentAt(centredMatrix, 0f, 0f).Y, 0f);

            // An off-centre GameViewRect: the ray through the frame's centre pixel is the view axis.
            lens.ViewCentre = static () => (x, y);
            lens.Follow(portal);
            var shiftedMatrix = camera.GetCameraProjection();
            var (offsetX, offsetY) = CameraLens.OffsetForCentre(portal.FrustumHeightAtNear, aspect, x, y);
            var shifted = CameraLens.FromPortal(portal, offsetX, offsetY);
            var shiftedSides = shifted.Sides(aspect);
            var axis = TangentAt(shiftedMatrix, (2f * x) - 1f, 1f - (2f * y));
            var shiftedOk = lens.Current == shifted
                && Close(TangentAt(shiftedMatrix, -1f, 0f).X, shiftedSides.Left) && Close(TangentAt(shiftedMatrix, 1f, 0f).X, shiftedSides.Right)
                && Close(TangentAt(shiftedMatrix, 0f, -1f).Y, shiftedSides.Bottom) && Close(TangentAt(shiftedMatrix, 0f, 1f).Y, shiftedSides.Top)
                && MathF.Abs(axis.X) <= AxisTolerance(shiftedSides.Right - shiftedSides.Left) && MathF.Abs(axis.Y) <= AxisTolerance(shiftedSides.Top - shiftedSides.Bottom);

            Check(
                $"world lens f={focal:0.###}: frustum {portal.FrustumHeightAtNear:0.####} at near {portal.Near:0.#####}; zero offset for the whole window; an off-centre GameViewRect puts the axis at its centre",
                centredOk && shiftedOk,
                $"whole window: lens read back equal {read == expected}, offset ({read.OffsetX}, {read.OffsetY}); off-centre ({x}, {y}): offset ({offsetX:0.####}, {offsetY:0.####}), " +
                $"axis tangent ({axis.X:0.#####}, {axis.Y:0.#####}) at the frame centre, sides L/R/B/T {shiftedSides.Left:0.####}/{shiftedSides.Right:0.####}/{shiftedSides.Bottom:0.####}/{shiftedSides.Top:0.####}");
        }

        // Letting go gives the rest perspective back (and only once).
        lens.Follow(new PortalLens(PortalZoom.FocalFromVerticalFov(60.0 * Math.PI / 180.0), false));
        var restored = lens.Current == CameraLens.Perspective(60f, 0.1f) && !lens.Overridden && lens.Overrides == 1;
        Check("world lens: when the zoom lets go the camera has its rest perspective again", restored, $"{lens.Current}, overridden {lens.Overridden}, takeovers {lens.Overrides}");

        // A different rest field of view (--fov): f_game follows it.
        camera.Fov = 90f;
        var wide = new WorldLens(camera);
        Check("world lens: f_game follows --fov (90 deg gives 1)", MathF.Abs(wide.RestFocal - 1f) < 1e-5f, $"{wide.RestFocal}");

        host.QueueFree();
    }

    // --- the composition of the view's picture -------------------------------------------------------------------------

    /// <summary>
    /// Byte-space composition readback: a <see cref="CreatureModeView"/> over the byte-space environment composes its picture the way
    /// the world does (raw bytes, blends in byte space: a 0.5 black "mix" over grey 0.6 is 0.3, not the linear-light 0.43), and the
    /// window shows that picture as it is (the tunnel's <c>TextureRect</c> adds no gamma or tonemap).
    /// </summary>
    private async Task CheckByteSpaceCompositionAsync()
    {
        var previous = ByteSpaceBlending.Current;
        ByteSpaceBlending.Select(BlendSpace.Byte);
        var layer = new CanvasLayer { Name = "ByteSpaceCheckLayer", Layer = 100 };
        AddChild(layer);
        CreatureModeView? view = null;
        try
        {
            view = CreatureModeView.Create("ByteSpaceCheckView", ViewportSize);
            AddChild(view);
            view.Camera.Projection = Camera3D.ProjectionType.Orthogonal;
            view.Camera.Size = 2f; // y in [-1, 1]; x in [-4, 4] at 4:1
            view.Camera.Near = 0.1f;
            view.Camera.Far = 40f;
            view.Camera.Position = new Vector3(0, 0, 5);
            view.SetLights(Vector3.One, Vector3.Back, Vector3.Zero); // ambient 1, no light: a surface shows its own colour
            view.Active = true;

            var shaders = new SurfaceShaders();
            var opaque = new SurfaceShaderVariant(SurfaceBlend.Opaque, false, true, SurfaceCull.None, TextureAddressMode.Clamp, false, false).WithLighting(SurfaceLighting.CreatureMode);
            var mix = new SurfaceShaderVariant(SurfaceBlend.Mix, false, false, SurfaceCull.None, TextureAddressMode.Clamp, false, false).WithLighting(SurfaceLighting.CreatureMode);
            var normal = new AcVector3(0, -1, 0);
            AddQuad(view, shaders, opaque, -3f, 0f, new Color(0.6f, 0.6f, 0.6f), 1f, normal); // grey 0.6
            AddQuad(view, shaders, opaque, -1f, 0f, new Color(0.6f, 0.6f, 0.6f), 1f, normal); // grey 0.6 ...
            AddQuad(view, shaders, mix, -1f, 0.5f, new Color(0f, 0f, 0f), 0.5f, normal); // ... under a black mix of 0.5
            AddQuad(view, shaders, mix, 1f, 0.5f, new Color(1f, 1f, 1f), 0.5f, normal); // a white mix of 0.5 over the clear colour (black)

            var screen = new TextureRect
            {
                Name = "ByteSpaceCheckScreen",
                Texture = view.GetTexture(),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Position = Vector2.Zero,
                Size = ViewportSize,
            };
            layer.AddChild(screen);
            await FramesAsync(5);

            using var picture = view.GetTexture().GetImage();
            using var window = GetViewport().GetTexture().GetImage();
            var probes = new (int X, string Name, int Expected)[]
            {
                (16, "opaque grey 0.6 is byte 153", 153),
                (48, "a 0.5 black mix over grey 0.6 is byte 77 (0.3 in byte space)", 77),
                (80, "a 0.5 white mix over the black clear colour is byte 128 (0.5 in byte space)", 128),
            };
            foreach (var (px, name, expected) in probes)
            {
                var got = picture.GetPixel(px, ViewportSize.Y / 2);
                var shown = window.GetPixel(px, ViewportSize.Y / 2);
                Check(
                    $"byte-space composition: {name}; the window shows the view's picture as it is",
                    Near(Bytes(got), expected, expected, expected) && Math.Abs(shown.R8 - got.R8) <= 1 && Math.Abs(shown.G8 - got.G8) <= 1 && Math.Abs(shown.B8 - got.B8) <= 1,
                    $"view {Bytes(got)}, window {Bytes(shown)}, expected {expected} +-{Tolerance}");
            }
        }
        finally
        {
            ByteSpaceBlending.Select(previous);
            layer.QueueFree();
            view?.QueueFree();
        }
    }

    private static void AddQuad(CreatureModeView view, SurfaceShaders shaders, SurfaceShaderVariant variant, float x, float z, Color colour, float alpha, AcVector3 normal)
    {
        var mesh = MeshFactory.Build(MeshFactory.Prepare(Quad(x, normal)));
        var material = new ShaderMaterial { Shader = shaders.Get(variant) };
        material.SetShaderParameter("solid_color", new Vector4(colour.R, colour.G, colour.B, 1f)); // a Vector4, not a Color: the engine would decode a Color as sRGB
        material.SetShaderParameter("material_alpha", alpha);
        mesh.SurfaceSetMaterial(0, material);
        view.Objects.AddChild(new MeshInstance3D { Mesh = mesh, Position = new Vector3(0, 0, z) });
    }

    // --- the UIASSET chain ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The UIASSET chain in the engine's own decode: the enum map resolves the portal-space records to the tunnel Setup 0x02000306, its
    /// spin 0x030005AC and the UI sound table 0x2000004B; the Setup's two parts build creature-mode meshes; the spin has 120 frames and
    /// starts at frame 1 and stays in 1 to 119; the table has the two whooshes (0x6A, 0x6B) and the hum's wave exists.
    /// </summary>
    private void CheckUiAssetChain()
    {
        const string name = "UIASSET chain: portal-space background, animation and sound table resolve and the tunnel builds in the engine";
        if (!NeedDats(name, out var services))
        {
            return;
        }

        var dats = services.Dats!;
        var assets = PortalSpaceAssets.Resolve(dats.EnumMaps);
        var idsOk = assets == new PortalSpaceAssets(0x02000306u, 0x030005ACu, 0x2000004Bu);
        var assembly = services.Setups.Get(assets.TunnelSetupId);
        var partsOk = assembly is { Parts.Count: 2 } && assembly.Parts.All(p => p.IsGfxObj);
        var meshesOk = false;
        if (partsOk)
        {
            using var meshes = new ActorMeshLibrary(services, SurfaceLighting.CreatureMode);
            meshesOk = assembly!.Parts.All(p => meshes.Get(p.GfxObjId) is { } mesh && mesh.GetSurfaceCount() > 0);
        }

        var tunnel = TunnelObject.Create(dats, assets, 1, createdAt: 0.0);
        tunnel.Restart();
        var startFrame = tunnel.CurrentFrame;
        int min = int.MaxValue, max = int.MinValue;
        for (var i = 1; i <= 240; i++) // 4 s: more than one loop
        {
            tunnel.AdvanceTo(i / 60.0);
            min = Math.Min(min, tunnel.CurrentFrame);
            max = Math.Max(max, tunnel.CurrentFrame);
        }

        var spinOk = tunnel.Spin.FrameCount == 120 && startFrame == 1 && min >= 1 && max == 119;
        var source = new DatAudioSource(dats);
        var table = source.GetSoundTable(assets.UiSoundTableId);
        var soundsOk = table is not null && (table.Lookup(PortalSpaceDirector.EnterPortalSoundType)?.Count ?? 0) > 0
            && (table.Lookup(PortalSpaceDirector.ExitPortalSoundType)?.Count ?? 0) > 0 && source.GetWave(TunnelHumWave) is not null;
        Check(
            name,
            idsOk && partsOk && meshesOk && spinOk && soundsOk,
            $"ids {assets} (expected 0x02000306/0x030005AC/0x2000004B: {idsOk}); setup parts {assembly?.Parts.Count} gfx-only {partsOk}; creature-mode meshes {meshesOk}; " +
            $"spin frames {tunnel.Spin.FrameCount}, starts at {startFrame}, range {min}-{max} over 4 s: {spinOk}; sound table with both whooshes and the hum wave: {soundsOk}");
    }

    // --- the tunnel's surfaces ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A readback of the real tunnel view (the UIASSET torus, lit as in the portal scene) counts the pixels of the two hues the tunnel is
    /// made of: the torus wall's blue-grey (a large share of the picture) and the purple of the small shapes in it (a few hundred pixels),
    /// at six spin frames. A surface that went missing (a texture that did not resolve, a part with no mesh, a material that draws
    /// nothing) leaves one of them out and fails here. It judges neither the look nor the exact colours (the owner accepted the look), only
    /// that the wall always covers a real part of the picture and that the purple shows in at least one of the frames (the shapes move in
    /// and out of view with the spin).
    /// </summary>
    private async Task CheckTunnelSurfacesAsync()
    {
        const string name = "tunnel surfaces: a readback of the lit tunnel view holds the wall's blue-grey in every sampled spin frame and the purple in at least one";
        if (!NeedDats(name, out var services))
        {
            return;
        }

        var dats = services.Dats!;
        var assets = PortalSpaceAssets.Resolve(dats.EnumMaps);
        var tunnel = TunnelObject.Create(dats, assets, 1, createdAt: 0.0);
        tunnel.Restart();
        var view = PortalTunnelView.Create(services, assets.TunnelSetupId, tunnel);
        AddChild(view);
        var samples = new List<string>();
        var leastWall = double.MaxValue;
        var mostPurple = 0;
        var controlOk = false;
        try
        {
            view.SetShown(true);
            view.PlaceCamera(0.0);
            foreach (var seconds in new[] { 0.0, 0.5, 1.0, 1.5, 2.0, 2.5 })
            {
                tunnel.AdvanceTo(seconds);
                view.Pose(tunnel);
                await FramesAsync(4);
                using var image = view.View.GetTexture().GetImage();
                var (wall, purple, total) = CountTunnelHues(image);
                leastWall = Math.Min(leastWall, wall / (double)total);
                mostPurple = Math.Max(mostPurple, purple);
                samples.Add($"frame {tunnel.CurrentFrame}: wall {wall / (double)total:P1}, purple {purple}");
            }

            // The control: with every part hidden the same readback fails both rules, so a view that draws nothing cannot pass (a missing
            // surface is what the check exists to catch; the clear colour and the engine's own pixels hold neither hue).
            foreach (var part in view.Parts)
            {
                part.Visible = false;
            }

            await FramesAsync(4);
            using var empty = view.View.GetTexture().GetImage();
            var (emptyWall, emptyPurple, emptyTotal) = CountTunnelHues(empty);
            controlOk = emptyWall / (double)emptyTotal < MinTunnelWallShare && emptyPurple < MinTunnelPurpleSamples;
            samples.Add($"control, every part hidden: wall {emptyWall / (double)emptyTotal:P1}, purple {emptyPurple}");
        }
        finally
        {
            view.SetShown(false);
            view.QueueFree();
        }

        Check(
            name,
            leastWall >= MinTunnelWallShare && mostPurple >= MinTunnelPurpleSamples && controlOk,
            $"the least wall share {leastWall:P1} (at least {MinTunnelWallShare:P0}), the most purple samples {mostPurple} (at least {MinTunnelPurpleSamples}, every second pixel in each direction); {string.Join("; ", samples)}");
    }

    /// <summary>The least share of the view's pixels the wall's blue-grey must cover in every sampled frame (the readback measures about 19 to 59 %).</summary>
    private const double MinTunnelWallShare = 0.08;

    /// <summary>The fewest purple samples that must show in the best sampled frame (the best of the six sampled frames measures about 700; the shapes are small and move).</summary>
    private const int MinTunnelPurpleSamples = 16;

    /// <summary>
    /// Samples every second pixel of every second row; counts the wall's blue-grey (hue 180 to 255 degrees, value over 0.1, saturation over
    /// 0.08) and the purple (hue 255 to 330, value over 0.3, saturation over 0.3), and the samples taken.
    /// </summary>
    private static (int Wall, int Purple, int Total) CountTunnelHues(Image image)
    {
        int wall = 0, purple = 0, total = 0;
        for (var y = 0; y < image.GetHeight(); y += 2)
        {
            for (var x = 0; x < image.GetWidth(); x += 2)
            {
                total++;
                var c = image.GetPixel(x, y);
                var hue = c.H * 360f;
                if (hue is >= 180f and < 255f && c.V > 0.1f && c.S > 0.08f)
                {
                    wall++;
                }
                else if (hue is >= 255f and < 330f && c.V > 0.3f && c.S > 0.3f)
                {
                    purple++;
                }
            }
        }

        return (wall, purple, total);
    }

    // --- the sound streams ---------------------------------------------------------------------------------------------------

    private const uint TunnelHumWave = 0x0A000316;

    /// <summary>
    /// The portal waves (the two whooshes and the hum) become 11,025 Hz mono 16-bit <see cref="AudioStreamWav"/> streams whose length
    /// is within 1 ms of their sample count, in the engine's own stream builder, centred and panned (a pan makes 16-bit stereo, same
    /// length).
    /// </summary>
    private void CheckPortalStreams()
    {
        const string name = "streams: the portal waves are 11,025 Hz mono 16-bit with their exact length";
        if (!NeedDats(name, out var services))
        {
            return;
        }

        var source = new DatAudioSource(services.Dats!);
        var assets = PortalSpaceAssets.Resolve(services.Dats!.EnumMaps);
        var table = source.GetSoundTable(assets.UiSoundTableId);
        var waves = new List<uint>();
        foreach (var type in new[] { PortalSpaceDirector.EnterPortalSoundType, PortalSpaceDirector.ExitPortalSoundType })
        {
            waves.AddRange(table?.Lookup(type)?.Select(e => e.WaveId) ?? []);
        }

        waves.Add(TunnelHumWave);
        var failures = new List<string>();
        var report = new List<string>();
        foreach (var id in waves.Distinct())
        {
            if (source.GetWave(id) is not { } wave)
            {
                failures.Add($"0x{id:X8} missing");
                continue;
            }

            var expectedSeconds = (wave.Samples.Length / 2) / 11025.0;
            if (WaveStreams.Build(wave, 0, out var problem) is not AudioStreamWav stream)
            {
                failures.Add($"0x{id:X8} no stream ({problem})");
                continue;
            }

            var panned = WaveStreams.Build(wave, -9, out _) as AudioStreamWav;
            var formatOk = wave.Format is { Channels: 1, SamplesPerSecond: 11025, BitsPerSample: 16 }
                && stream.MixRate == 11025 && !stream.Stereo && stream.Format == AudioStreamWav.FormatEnum.Format16Bits;
            var lengthOk = Math.Abs(stream.GetLength() - expectedSeconds) <= 0.001;
            var pannedOk = panned is { Stereo: true, MixRate: 11025 } && Math.Abs(panned.GetLength() - expectedSeconds) <= 0.001;
            report.Add($"0x{id:X8} {stream.MixRate} Hz {(stream.Stereo ? "stereo" : "mono")} {stream.Format} {stream.GetLength():0.0000} s (samples {expectedSeconds:0.0000} s)");
            if (!formatOk || !lengthOk || !pannedOk)
            {
                failures.Add($"0x{id:X8} format {formatOk} length {lengthOk} panned {pannedOk}");
            }
        }

        Check(name, failures.Count == 0 && waves.Count >= 3, failures.Count == 0 ? string.Join("; ", report) : string.Join("; ", failures));
    }

    // --- buses, mute and the hum ---------------------------------------------------------------------------------------

    /// <summary>Forwards every call to the audio service and counts the plays of one wave (the hum).</summary>
    private sealed class CountingOutput(AudioService service, uint wave) : IAudioOutput
    {
        public int Plays { get; private set; }

        public void Play(in VoicePlay play)
        {
            if (play.WaveId == wave)
            {
                Plays++;
            }

            service.Play(in play);
        }

        public void Stop(int voice) => service.Stop(voice);

        public bool IsPlaying(int voice) => service.IsPlaying(voice);
    }

    /// <summary>
    /// The audio service's run-time buses (UI and Effects, both sending to Master), the master level and mute from
    /// <see cref="AudioSettings"/>, the automated-run silence of <see cref="AudioOptions"/>, and that a voice plays on the bus it was given.
    /// </summary>
    private async Task CheckAudioBusesAsync()
    {
        const string name = "audio: UI and Effects buses send to Master; volume and mute set Master; automated runs are silent unless --volume";
        if (!NeedDats(name, out var services))
        {
            return;
        }

        var audio = AudioService.Create(services, new AudioSettings(0.5f, false), 1);
        AddChild(audio);
        var guard = AudioService.ShutdownGuard.For(audio);
        AddChild(guard);
        try
        {
            var busesOk = AudioServer.GetBusName(audio.UiBus) == AudioService.UiBusName && AudioServer.GetBusName(audio.EffectsBus) == AudioService.EffectsBusName
                && AudioServer.GetBusSend(audio.UiBus) == "Master" && AudioServer.GetBusSend(audio.EffectsBus) == "Master"
                && audio.UiBus != audio.EffectsBus && audio.UiBus != audio.MasterBus;
            var half = AudioServer.GetBusVolumeDb(audio.MasterBus);
            var levelOk = MathF.Abs(half - (20f * MathF.Log10(0.5f))) < 0.01f && !AudioServer.IsBusMute(audio.MasterBus);
            audio.Apply(AudioSettings.Mute);
            var muteOk = AudioServer.IsBusMute(audio.MasterBus) && AudioServer.GetBusVolumeDb(audio.MasterBus) <= AudioSettings.SilentDecibels + 0.01f;
            audio.Apply(new AudioSettings(0f, false));
            var zeroOk = AudioServer.GetBusVolumeDb(audio.MasterBus) <= AudioSettings.SilentDecibels + 0.01f;
            audio.Apply(AudioSettings.Default);
            var fullOk = MathF.Abs(AudioServer.GetBusVolumeDb(audio.MasterBus)) < 0.01f && !AudioServer.IsBusMute(audio.MasterBus);

            var automated = AudioOptions.Parse([]).Resolve(automated: true);
            var live = AudioOptions.Parse([]).Resolve(automated: false);
            var explicitVolume = AudioOptions.Parse(["--volume=0.5"]).Resolve(automated: true);
            var muted = AudioOptions.Parse(["--mute", "--volume=0.5"]).Resolve(automated: true);
            var optionsOk = automated.Gain == 0f && live.MasterVolume == 1f && !live.Muted && MathF.Abs(explicitVolume.Gain - 0.5f) < 1e-6f && muted.Muted && muted.Gain == 0f;

            // A voice plays on the bus it was given.
            var played = audio.Played;
            audio.Play(new VoicePlay(0, TunnelHumWave, 0, 0, AudioBus.Ui));
            audio.Play(new VoicePlay(1, TunnelHumWave, 0, 0, AudioBus.Effects));
            for (var i = 0; i < 200 && audio.Played < played + 2; i++)
            {
                await FramesAsync(1);
            }

            var routedOk = audio.Played == played + 2 && audio.PlayerOf(0).Bus == AudioService.UiBusName && audio.PlayerOf(1).Bus == AudioService.EffectsBusName
                && audio.PlayerOf(0).Playing && audio.PlayerOf(1).Playing;
            Check(
                name,
                busesOk && levelOk && muteOk && zeroOk && fullOk && optionsOk && routedOk,
                $"buses {busesOk} (UI {audio.UiBus}, Effects {audio.EffectsBus}, Master {audio.MasterBus}); 0.5 volume {half:0.00} dB: {levelOk}; mute {muteOk}; volume 0 {zeroOk}; volume 1 {fullOk}; " +
                $"automated gain {automated.Gain}, live {live.MasterVolume}, explicit {explicitVolume.Gain}, mute wins {muted.Muted}: {optionsOk}; voices routed {routedOk} (played {audio.Played - played} of 2, buses {audio.PlayerOf(0).Bus}/{audio.PlayerOf(1).Bus})");
        }
        finally
        {
            audio.StopAllAndWait();
            guard.QueueFree();
            audio.QueueFree();
        }
    }

    /// <summary>
    /// The tunnel's hum in the engine: the real <see cref="TunnelObject"/> and the director with a retail sound player over the audio
    /// service. While the tunnel is held (state 3), three loops of the spin (2.975 s each) start the hum wave once each, at the start of each
    /// loop, and the next loop starts a fourth.
    /// </summary>
    private async Task CheckTunnelHumsAsync()
    {
        const string name = "tunnel hum: three loops of the spin start the hum three times, once per loop";
        if (!NeedDats(name, out var services))
        {
            return;
        }

        var dats = services.Dats!;
        var audio = AudioService.Create(services, AudioSettings.Mute, 1);
        AddChild(audio);
        var guard = AudioService.ShutdownGuard.For(audio);
        AddChild(guard);
        try
        {
            var output = new CountingOutput(audio, TunnelHumWave);
            var sound = new RetailSoundPlayer(output, audio.Source, new RetailRandom(1), new MsvcCrtRandom(1), AudioLevels.Default, GodotLogSink.Instance);
            var assets = PortalSpaceAssets.Resolve(dats.EnumMaps);
            var tunnel = TunnelObject.Create(dats, assets, 1, createdAt: 0.0);
            var machine = new PortalSpaceMachine(tunnel, new MsvcCrtRandom(1), () => 1.7320508f);
            var progress = new TeleportProgress();
            var director = new PortalSpaceDirector(machine, progress, assets, ReadinessPolicy.Default, sound, null, GodotLogSink.Instance);
            progress.BeginLocal();
            progress.Hold(true); // state 3 until the end of the check
            var world = new AllAttachedWorld();
            var counts = new List<int>();
            var now = 0.0;
            foreach (var until in new[] { 1.5, 4.5, 7.5, 10.5 }) // the middle of the first, second, third and fourth loops (each hum is at a loop's start, 2.975 s apart)
            {
                while (now < until)
                {
                    now += 1.0 / 60.0;
                    director.Step(now, 0xA9B40019u, world);
                }

                counts.Add(output.Plays);
            }

            // The hums were real plays: the service built their streams and started voices (on the Dummy driver off-screen).
            for (var i = 0; i < 200 && audio.Played < audio.Starts; i++)
            {
                await FramesAsync(1);
            }

            var spinning = machine.State == TeleportAnimState.InTunnel;
            Check(
                name,
                counts.SequenceEqual([1, 2, 3, 4]) && spinning && audio.Played == audio.Starts && audio.Silenced == 0,
                $"hum plays after loops 1 to 4: {string.Join(", ", counts)} (expected 1, 2, 3, 4); machine state {machine.State}; voices started {audio.Starts}, streams played {audio.Played}, silenced {audio.Silenced}");
        }
        finally
        {
            audio.StopAllAndWait();
            guard.QueueFree();
            audio.QueueFree();
        }
    }

    // --- the offline sequence ------------------------------------------------------------------------------------------------

    /// <summary>Every piece attached: a world that is ready at once.</summary>
    private sealed class AllAttachedWorld : IWorldAttachments
    {
        public AttachState Terrain(LandblockCoord landblock) => AttachState.Attached;

        public AttachState Objects(LandblockCoord landblock) => AttachState.Attached;

        public AttachState Collision(LandblockCoord landblock) => AttachState.Disabled;

        public AttachState Cell(uint envCellId) => AttachState.Attached;

        public IReadOnlyList<uint>? VisibleCells(uint envCellId) => [];

        public bool SeenOutside(uint envCellId) => false;
    }

    private sealed class LoginCompleteSpy : ILoginCompleteSink
    {
        public int Sent { get; private set; }

        public void SendLoginComplete() => Sent++;
    }

    /// <summary>
    /// The offline transition end to end without an engine view: a jump through <see cref="ExploreTransition"/> runs states 2, 3, 4, 5, 6 and
    /// back to 0 in at least 5 s of fixed 1/60 s steps, posts the retail notice (text type 26) to the placeholder line, hides the world only
    /// while the tunnel covers it, and sends no LoginComplete (it is suppressed and counted). What the presenter does with each frame (the
    /// notice line's visibility, the arbiter, the lenses) is pinned by <see cref="CheckPresenterRulesAsync"/>.
    /// </summary>
    private void CheckOfflineSequence()
    {
        const string name = "offline sequence: a scripted 2 to 0 run sends no LoginComplete and posts the portal notice";
        var progress = new TeleportProgress();
        var tunnel = new TimedTunnel(createdAt: 0.0);
        var machine = new PortalSpaceMachine(tunnel, new MsvcCrtRandom(1), () => 1.7320508f);
        var notice = PortalNoticeLine.Create();
        AddChild(notice);
        var spy = new LoginCompleteSpy();
        var director = new PortalSpaceDirector(machine, progress, new PortalSpaceAssets(0x02000306u, 0x030005ACu, 0x2000004Bu), ReadinessPolicy.Default, null, notice, GodotLogSink.Instance)
        {
            LoginComplete = spy,
        };
        var transition = new ExploreTransition(progress);
        var played = transition.Jump(static () => { });
        var world = new AllAttachedWorld();
        var states = new List<TeleportAnimState>();
        var worldHiddenOutsideTunnel = false;
        var now = 0.0;
        while (now < 30.0)
        {
            now += 1.0 / 60.0;
            var frame = director.Step(now, 0xA9B40019u, world);
            if (states.Count == 0 || states[^1] != frame.State)
            {
                states.Add(frame.State);
            }

            worldHiddenOutsideTunnel |= !frame.WorldVisible && !frame.TunnelVisible;
            if (frame.State == TeleportAnimState.Off && states.Count > 1)
            {
                break;
            }
        }

        var expectedStates = new[] { TeleportAnimState.Tunnel, TeleportAnimState.InTunnel, TeleportAnimState.Holding, TeleportAnimState.LeavingTunnel, TeleportAnimState.Arriving, TeleportAnimState.Off };
        Check(
            name,
            played && states.SequenceEqual(expectedStates) && now >= 5.0 && now <= 9.0
                && spy.Sent == 0 && director.LoginCompletesSent == 0 && director.LoginCompletesSuppressed == 1 && transition.Transitions == 1
                && notice.Posted >= 1 && notice.Text == PortalSpaceMachine.PortalNotice && notice.LastTextType == PortalSpaceMachine.PortalNoticeTextType
                && !worldHiddenOutsideTunnel,
            $"jump played {played}; states {string.Join(" -> ", states)}; {now:0.00} s; LoginComplete sent {director.LoginCompletesSent} (spy {spy.Sent}), suppressed {director.LoginCompletesSuppressed}; " +
            $"notice \"{notice.Text}\" type {notice.LastTextType} posted {notice.Posted}x; world hidden outside the tunnel: {worldHiddenOutsideTunnel}");
        notice.QueueFree();
    }

    /// <summary>
    /// The hook's deferred camera move (<see cref="OfflinePortalTransition"/>, docs/portal-space.md §9): a jump that starts portal space has
    /// not run when the hook returns and runs at the start of the next frame once the presenter has armed it (not before), a second hook
    /// call before that makes the first run at once and in order, and a jump with portal space off runs at once.
    /// </summary>
    private async Task CheckDeferredJumpAsync()
    {
        const string name = "offline hook: the camera move runs once, on the frame after portal space starts, in order";
        var progress = new TeleportProgress();
        var explore = new ExploreTransition(progress);
        var transition = new OfflinePortalTransition(explore);
        var order = new List<string>();
        var played = transition.Teleport(() => order.Add("first"));
        var waiting = order.Count == 0 && transition.Pending && progress.InProgress && progress.IsLocal;
        await FramesAsync(1); // a frame passes, but the presenter has not armed the move: it still waits
        var unarmed = order.Count == 0 && transition.Pending;
        transition.Arm();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); // the start of the next frame: the armed move runs first
        var ran = order.SequenceEqual(["first"]) && !transition.Pending;
        transition.Teleport(() => order.Add("second"));
        transition.Teleport(() => order.Add("third")); // the second has not run: it runs now, before the third is queued
        var inOrder = order.SequenceEqual(["first", "second"]) && transition.Pending;
        transition.Arm();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var all = order.SequenceEqual(["first", "second", "third"]) && !transition.Pending && explore.Transitions == 3;
        transition.Enabled = false;
        var bare = false;
        var playedOff = transition.Teleport(() => bare = true);
        Check(
            name,
            played && waiting && unarmed && ran && inOrder && all && !playedOff && bare && !transition.Pending,
            $"started {played}, waiting at once {waiting}, still waiting unarmed {unarmed}, ran on the armed frame {ran}, a second call runs the first at once {inOrder}, all three in order {all} ({string.Join(", ", order)}); off: played {playedOff}, ran at once {bare}");
    }

    /// <summary>
    /// The real <see cref="PortalSpacePresenter"/> on a test camera, a <see cref="WorldVisibility"/> over a <c>SubViewport</c>, the
    /// real tunnel view and the placeholder notice line, stepped through one fixed-clock jump (an offline 2 to 0 sequence). In every frame:
    /// the arbiter holds the world hidden exactly in states 2 to 5 (so <c>Disable3D</c> says so), the tunnel is shown exactly then and its
    /// camera has the frame's lens, the notice line shows exactly while the tunnel does, the world camera has the zoom's frustum while the
    /// world is drawn and the zoom is on, and at state 0 the world camera has its rest perspective back; the busy state is told once on and
    /// once off. This pins what the presenter does with a frame, which the director-level check above cannot.
    /// </summary>
    private async Task CheckPresenterRulesAsync()
    {
        const string name = "presenter: Disable3D, the tunnel, the notice line and the lenses follow each frame of a scripted jump";
        if (!NeedDats(name, out var services))
        {
            return;
        }

        var dats = services.Dats!;
        var host = new SubViewport
        {
            Size = ViewportSize,
            OwnWorld3D = true,
            Msaa3D = Viewport.Msaa.Disabled,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(host);
        var camera = new Camera3D { Fov = 60f, Near = 0.1f, Far = 6000f, Current = true };
        host.AddChild(camera);
        await FramesAsync(2);

        var visibility = new WorldVisibility(host);
        var assets = PortalSpaceAssets.Resolve(dats.EnumMaps);
        var tunnel = TunnelObject.Create(dats, assets, 1, createdAt: 0.0);
        var lens = new WorldLens(camera);
        var rest = lens.Rest;
        var machine = new PortalSpaceMachine(tunnel, new MsvcCrtRandom(1), () => lens.RestFocal);
        var progress = new TeleportProgress();
        var notice = PortalNoticeLine.Create();
        AddChild(notice);
        var director = new PortalSpaceDirector(machine, progress, assets, ReadinessPolicy.Default, null, notice, GodotLogSink.Instance);
        var view = PortalTunnelView.Create(services, assets.TunnelSetupId, tunnel);
        AddChild(view);
        var busy = new List<bool>();
        var steps = 0;
        var presenter = PortalSpacePresenter.Create(director, tunnel, view, lens, visibility, new AllAttachedWorld(), () => 0xA9B40019u, notice, 0.0, 1.0 / 60.0, busy.Add, () => steps++);

        var started = new ExploreTransition(progress).Begin();
        var states = new List<TeleportAnimState>();
        var problems = new List<string>();
        var perState = new Dictionary<TeleportAnimState, string>();
        for (var i = 0; i < 30 * 60; i++)
        {
            presenter._Process(1.0 / 60.0);
            var frame = presenter.LastFrame;
            if (states.Count == 0 || states[^1] != frame.State)
            {
                states.Add(frame.State);
            }

            var covered = frame.State is TeleportAnimState.Tunnel or TeleportAnimState.InTunnel or TeleportAnimState.Holding or TeleportAnimState.LeavingTunnel;
            var zoomOnWorld = frame.WorldVisible && frame.Lens.Overridden;
            void Expect(bool ok, string what)
            {
                if (!ok && problems.Count < 8)
                {
                    problems.Add($"{frame.State} t={frame.StateElapsed:0.00}: {what}");
                }
            }

            Expect(frame.TunnelVisible == covered && frame.WorldVisible == !covered, $"the machine says tunnel {frame.TunnelVisible} world {frame.WorldVisible}");
            Expect(visibility.Visible == frame.WorldVisible && host.Disable3D == !frame.WorldVisible, $"arbiter visible {visibility.Visible}, Disable3D {host.Disable3D}");
            Expect(view.Shown == frame.TunnelVisible, $"tunnel shown {view.Shown}");
            Expect(notice.Shown == frame.TunnelVisible, $"notice shown {notice.Shown}");
            if (frame.TunnelVisible)
            {
                Expect(CameraLenses.Of(view.View.Camera) == CameraLens.FromPortal(frame.Lens), $"tunnel camera lens {CameraLenses.Of(view.View.Camera)}");
            }

            if (zoomOnWorld)
            {
                Expect(lens.Overridden && lens.Current == CameraLens.FromPortal(frame.Lens), $"world camera lens {lens.Current} while the zoom is on the world");
            }
            else if (frame.WorldVisible)
            {
                Expect(!lens.Overridden && lens.Current == rest, $"world camera lens {lens.Current}, not the rest perspective {rest}");
            }

            perState[frame.State] = $"{frame.State}: Disable3D {host.Disable3D}, tunnel {view.Shown}, notice {notice.Shown}, world lens zoom {lens.Overridden}";
            if (frame.State == TeleportAnimState.Off && states.Count > 1)
            {
                break;
            }
        }

        var expectedStates = new[] { TeleportAnimState.Tunnel, TeleportAnimState.InTunnel, TeleportAnimState.Holding, TeleportAnimState.LeavingTunnel, TeleportAnimState.Arriving, TeleportAnimState.Off };
        var ended = presenter.LastFrame.State == TeleportAnimState.Off && visibility.Visible && !host.Disable3D && !view.Shown && !notice.Shown
            && !lens.Overridden && lens.Current == rest;
        Check(
            name,
            started && states.SequenceEqual(expectedStates) && problems.Count == 0 && ended && busy.SequenceEqual([true, false]) && steps == presenter.Frames,
            $"jump started {started}; states {string.Join(" -> ", states)}; end state ok {ended} (world drawn, tunnel and notice hidden, rest lens {lens.Current}); busy told {string.Join(", ", busy)} (expected true, false); " +
            $"after-step calls {steps} of {presenter.Frames}; {string.Join("; ", perState.Values)}" + (problems.Count == 0 ? string.Empty : "; PROBLEMS: " + string.Join(" | ", problems)));

        view.SetShown(false);
        view.QueueFree();
        notice.QueueFree();
        host.QueueFree();
    }

    /// <summary>
    /// The placeholder status line (D32): shows the retail text only while a sequence runs, says it is a placeholder, and does not stack
    /// identical posts.
    /// </summary>
    private void CheckNoticeLine()
    {
        var line = PortalNoticeLine.Create();
        AddChild(line);
        var hiddenBefore = !line.Shown;
        line.PostNotice(PortalSpaceMachine.PortalNotice, PortalSpaceMachine.PortalNoticeTextType);
        var hiddenWhileIdle = !line.Shown; // posted, but no sequence is running yet
        line.SetActive(true);
        var shown = line.Shown && line.Text == "In Portal Space - Please Wait...";
        line.PostNotice(PortalSpaceMachine.PortalNotice, PortalSpaceMachine.PortalNoticeTextType); // a new roll segment re-posts it
        var sameText = line.Text == "In Portal Space - Please Wait..." && line.Posted == 2;
        var labelled = line.FindChild("PlaceholderLabel", true, false) is Label { Text: var text } && text.Contains("placeholder", StringComparison.Ordinal);
        line.SetActive(false);
        var hiddenAfter = !line.Shown;
        Check(
            "notice line: the placeholder shows the retail text during a sequence, is labelled a placeholder and hides afterwards",
            hiddenBefore && hiddenWhileIdle && shown && sameText && labelled && hiddenAfter,
            $"hidden before {hiddenBefore}, idle {hiddenWhileIdle}; shown {shown}; re-post keeps one line {sameText}; labelled {labelled}; hidden after {hiddenAfter}");
        line.QueueFree();
    }
}
