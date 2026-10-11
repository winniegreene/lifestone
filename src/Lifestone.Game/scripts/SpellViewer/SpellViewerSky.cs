using Godot;
using Lifestone.Client.Navigation;
using Lifestone.Client.Sky;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;
using Lifestone.Game.Core;
using Lifestone.Game.Render;
using Lifestone.Game.World.Sky;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.SpellViewer;

/// <summary>
/// The spell viewer's sky: the Region's sky objects (dome, clouds, sun) drawn by the sky track's
/// <see cref="SkyRenderer"/> at the viewer's fixed time of day, as the main scene draws them, instead of a flat fog-colour
/// background. Particle glows then blend over the sky colours they meet in retail (audit finding spellviewer-no-sky).
/// </summary>
/// <remarks>
/// <para><b>Time.</b> The stage's lighting comes from <c>RenderGlobals.ApplyDefaults</c>: day group 0 (Sunny) at
/// <c>--time-of-day</c>. The sky shows the same moment: the first day from Morningthaw 1, 10 P.Y. whose retail day group
/// is 0, at that fraction (<see cref="GoldenViewpoints.GameTimeFor"/>, the clock the automated runs of the main scene
/// use). The clock stands still; the client clock that scrolls cloud textures is the stage time, so fixed-step captures
/// are reproducible.</para>
/// <para><b>Placement.</b> The sky objects are centred on the camera (the sky track's <see cref="SkyAnchor.Camera"/>):
/// the M1 default by the lead's fidelity decision (retail's landblock-corner anchor makes the sky jump at landblock
/// edges, a retail artefact; docs/sky.md §4.2), and the orbit camera never leaves the stage.</para>
/// <para><b>Below the horizon.</b> In the main scene the landscape, fogged towards the fog colour with distance, covers
/// everything below the horizon, as in retail; the sky dome's lower part is never seen. The stage's terrain ends about
/// 480 m out, so between its edge and the horizon the dome showed through: a pale band with cloud streaks and hard
/// edges whose colour hardly changed with the time of day (review finding: pixel (450, 270) was (140, 148, 128) at
/// night under a (20, 15, 24) sky). A lower hemisphere centred on the camera (<see cref="BelowHorizonRadius"/>, its rim
/// exactly at eye height) is drawn unshaded in the time of day's fog colour (the colour fully fogged landscape takes and
/// the viewer's background before the sky; a built-in material, so the colour goes through
/// <see cref="ByteSpaceBlending.BuiltinColor"/>, docs/render-contract.md §2.3), so that strip reads as distant, fully
/// fogged ground. Stage geometry, nearer, draws over it.</para>
/// <para>Runs after the camera has moved (process priority 100), so the sky view copies this frame's camera.</para>
/// </remarks>
public partial class SpellViewerSky : Node
{
    /// <summary>Radius of the fog-coloured lower hemisphere (m); inside the orbit camera's far plane (3000 m).</summary>
    public const float BelowHorizonRadius = 2500f;

    private SkyDirector _director = null!;
    private SkyRenderer _renderer = null!;
    private Camera3D _camera = null!;
    private Func<double> _clientClock = null!;
    private Func<GlobalPosition, Vector3> _toRender = null!;
    private GlobalPosition _stageOrigin;
    private double _gameTime;
    private MeshInstance3D _belowHorizon = null!;

    /// <summary>
    /// Builds the sky for the stage in landblock <paramref name="stage"/> (its south-west corner is the Godot origin) and
    /// makes it the background of <paramref name="environment"/>, with everything below the horizon in the colour of
    /// <paramref name="fog"/> (see the remarks). Returns null (logged) when the sky cannot be built; the viewer then
    /// keeps the fog-colour background.
    /// </summary>
    public static SpellViewerSky? Create(
        Node parent,
        GameServices services,
        Godot.Environment environment,
        Camera3D camera,
        LandblockCoord stage,
        double dayFraction,
        Func<double> clientClock,
        Lifestone.Data.World.Sky.SkyFog fog)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var sky = new SpellViewerSky { Name = "Sky", ProcessPriority = 100 };
            sky.Build(services, camera, stage, dayFraction, clientClock);
            parent.AddChild(sky);
            parent.AddChild(sky._renderer);
            sky._belowHorizon = BelowHorizon(fog);
            parent.AddChild(sky._belowHorizon);
            environment.BackgroundMode = Godot.Environment.BGMode.Sky;
            environment.Sky = sky._renderer.Background;
            sky.Apply();
            Log.Info("spellviewer", "sky", ("game_time", sky._gameTime), ("day_group", sky._director.Last?.DayGroup.Name), ("models", sky._renderer.Models.Ready),
                ("ms", timer.Elapsed.TotalMilliseconds));
            return sky;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or KeyNotFoundException)
        {
            Log.Error("spellviewer", "sky not built; the background stays the fog colour", ("error", ex.Message));
            return null;
        }
    }

    public override void _Process(double delta) => Apply();

    private void Build(GameServices services, Camera3D camera, LandblockCoord stage, double dayFraction, Func<double> clientClock)
    {
        _camera = camera;
        _clientClock = clientClock;
        _stageOrigin = AcCoordinates.LandblockOrigin(stage);
        var origin = _stageOrigin;
        _toRender = g => AcToGodot.Position(new AcVector3((float)(g.X - origin.X), (float)(g.Y - origin.Y), (float)(g.Z - origin.Z)));
        var derethSky = services.Sky;
        _gameTime = GoldenViewpoints.GameTimeFor(new GoldenTime(dayFraction, GoldenViewpoints.Sunny), derethSky);
        _director = new SkyDirector(derethSky) { PartResolver = id => PartsOf(services, id) };
        var dayGroup = derethSky.Sky.DayGroups[_director.DayGroupAt(_gameTime)];

        // As SkyController: compile the sky variants this day group can need, then build its models now (no pop-in).
        var shaders = new SkyShaders();
        var uses = new List<(Lifestone.Data.Textures.SurfaceMaterial, Lifestone.Data.Geometry.TextureAddressMode, SkyPass)>();
        foreach (var (id, pass) in SkyDirector.ModelUses(derethSky.Sky))
        {
            foreach (var part in PartsOf(services, id))
            {
                if (services.GfxObjMeshes.Get(part) is not { } mesh)
                {
                    continue;
                }

                foreach (var sub in mesh.Mesh.SubMeshes)
                {
                    if (services.Textures.GetSurface(sub.SurfaceId) is { } surface)
                    {
                        uses.Add((surface, sub.Addressing, pass));
                    }
                }
            }
        }

        shaders.WarmUp(SkySurfaceVariant.Reachable(uses));
        var models = new SkyModels(services);
        models.LoadAllNow(SkyDirector.ModelIds(dayGroup));
        _renderer = new SkyRenderer();
        _renderer.Initialize(models, shaders, services.Render.Materials);
    }

    /// <summary>The fog-coloured lower hemisphere: rim at the node's height, pole <see cref="BelowHorizonRadius"/> below.</summary>
    private static MeshInstance3D BelowHorizon(Lifestone.Data.World.Sky.SkyFog fog)
    {
        const int rings = 8;
        const int segments = 48;
        static Vector3 At(int ring, int segment)
        {
            var elevation = -0.5 * Math.PI * ring / rings;
            var azimuth = 2.0 * Math.PI * segment / segments;
            return new Vector3(
                (float)(Math.Cos(elevation) * Math.Cos(azimuth)),
                (float)Math.Sin(elevation),
                (float)(Math.Cos(elevation) * Math.Sin(azimuth))) * BelowHorizonRadius;
        }

        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (var ring = 0; ring < rings; ring++)
        {
            for (var segment = 0; segment < segments; segment++)
            {
                tool.AddVertex(At(ring, segment));
                tool.AddVertex(At(ring + 1, segment));
                tool.AddVertex(At(ring + 1, segment + 1));
                tool.AddVertex(At(ring, segment));
                tool.AddVertex(At(ring + 1, segment + 1));
                tool.AddVertex(At(ring, segment + 1));
            }
        }

        var material = new StandardMaterial3D
        {
            ResourceName = "lifestone_spellviewer_below_horizon",
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            // Render-fidelity rule (render-contract §2.3): Godot decodes a built-in material's colour, so hand it over
            // through BuiltinColor (sRGB-encoded in byte space) to show the fog colour's raw bytes, as the background does.
            AlbedoColor = ByteSpaceBlending.BuiltinColor(new Color(fog.Color.R / 255f, fog.Color.G / 255f, fog.Color.B / 255f)),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DisableFog = true,
            DisableReceiveShadows = true,
        };
        return new MeshInstance3D
        {
            Name = "BelowHorizon",
            Mesh = tool.Commit(),
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private void Apply()
    {
        _belowHorizon.GlobalPosition = _camera.GlobalPosition;
        _director.Update(_gameTime, _clientClock(), discontinuity: false);
        var camera = AcToGodot.ToAc(_camera.GlobalPosition);
        var viewer = new GlobalPosition(_stageOrigin.X + camera.X, _stageOrigin.Y + camera.Y, _stageOrigin.Z + camera.Z);
        _renderer.Apply(_director, _toRender, viewer, viewer, _camera, (Vector2I)_camera.GetViewport().GetVisibleRect().Size);
    }

    private static IEnumerable<uint> PartsOf(GameServices services, uint id) =>
        (id & 0xFF000000u) == 0x01000000u
            ? [id]
            : services.Setups.Get(id)?.Parts.Where(p => p.IsGfxObj).Select(p => p.GfxObjId) ?? [];
}
