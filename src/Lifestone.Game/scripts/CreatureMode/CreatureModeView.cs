using Godot;
using Lifestone.Client.Rendering;
using Lifestone.Game.Core;
using Lifestone.Game.Render;

namespace Lifestone.Game.CreatureMode;

/// <summary>
/// Retail's creature mode as an off-screen view (<c>CreatureMode</c>, docs/portal-space.md §5): its own world holding only the
/// objects put in <see cref="Objects"/>, one camera, one distant light and an ambient, none of the world's sun, ambient, fog or
/// sky. The portal tunnel is the first user (<c>PortalTunnelView</c>); the character-creation figure is the second (G3).
/// Modelled on <see cref="Lifestone.Game.World.Sky.SkyView"/>.
/// </summary>
/// <remarks>
/// <para><b>Colour.</b> The view owns an environment with <see cref="ByteSpaceBlending.Attach(Godot.Environment)"/>: in byte
/// space its shaders write raw retail values and the tonemap pass's decode LUT lets exactly those bytes reach the view's texture,
/// as the main view's do. The texture is therefore the finished picture (drawn as it is, no further conversion), unlike the
/// sky view's, which the main view's background shader converts.</para>
/// <para><b>Light.</b> Materials built with <see cref="SurfaceLighting.CreatureMode"/> read the <c>ac_cm_*</c> globals
/// (<see cref="CreatureModeLighting"/>), not the world's: they are written whenever the view is made <see cref="Active"/> or its
/// lights change. Global shader uniforms are shared, so only one creature-mode view should be active at a time (portal space
/// and character creation never overlap).</para>
/// <para><b>Cost.</b> An inactive view does not render (<see cref="SubViewport.RenderTargetUpdateMode"/> disabled); a hidden
/// tunnel costs nothing. <see cref="RenderOnce"/> draws one frame while inactive: the start-up warm render that compiles the
/// view's pipelines and uploads its textures before the first transition needs them.</para>
/// <para>Main thread only; the view's contents are posed by its user, in this view's Godot axes (AC to Godot only through
/// <see cref="AcToGodot"/>).</para>
/// </remarks>
public partial class CreatureModeView : SubViewport
{
    /// <summary>The camera's far plane in metres (the scenes are a few metres across).</summary>
    public const float FarClip = 1000f;

    private Vector3 _ambient = Vector3.One * 0.3f;
    private Vector3 _towardLight = Vector3.Up;
    private Vector3 _lightColor = Vector3.One;
    private bool _active;
    private long _onceRequestedAt = -1;

    /// <summary>The view's camera.</summary>
    public Camera3D Camera { get; private set; } = null!;

    /// <summary>Parent of the view's objects (the creature-mode scene's origin).</summary>
    public Node3D Objects { get; private set; } = null!;

    /// <summary>The view's environment (black background; the byte-space decode when that blend space is on).</summary>
    public Godot.Environment Environment { get; private set; } = null!;

    /// <summary>How many times <see cref="RenderOnce"/> has asked for a frame.</summary>
    public int WarmRenders { get; private set; }

    /// <summary>
    /// Creates the view at <paramref name="size"/> with its own world, a black clear, no anti-aliasing beyond
    /// <paramref name="msaa"/>, and the byte-space environment. Inactive.
    /// </summary>
    public static CreatureModeView Create(string name, Vector2I size, Viewport.Msaa msaa = Viewport.Msaa.Disabled)
    {
        var view = new CreatureModeView
        {
            Name = name,
            OwnWorld3D = true,
            TransparentBg = false,
            Msaa3D = msaa,
            ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled,
            UseTaa = false,
            UseDebanding = false,
            RenderTargetUpdateMode = UpdateMode.Disabled,
            Size = size,
        };

        // Retail's creature mode draws only its own objects: black behind them, no ambient from the environment (the scene's
        // ambient is the shader global), no fog, and a linear identity tonemap (render-contract.md §2.3).
        view.Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = ByteSpaceBlending.BackgroundColor(Colors.Black),
            AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            TonemapExposure = 1f,
            GlowEnabled = false,
            FogEnabled = false,
            VolumetricFogEnabled = false,
            SsaoEnabled = false,
            SsrEnabled = false,
            SdfgiEnabled = false,
            AdjustmentEnabled = false,
        };
        ByteSpaceBlending.Attach(view.Environment);
        view.AddChild(new WorldEnvironment { Name = "CreatureModeEnvironment", Environment = view.Environment });
        view.Camera = new Camera3D { Name = "CreatureModeCamera", Near = 0.1f, Far = FarClip, Current = true };
        view.AddChild(view.Camera);
        view.Objects = new Node3D { Name = "CreatureModeObjects" };
        view.AddChild(view.Objects);
        return view;
    }

    /// <summary>
    /// True while the view draws every frame. Turning it on writes the view's lights into the shader globals first, so the first
    /// frame is lit by them.
    /// </summary>
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value)
            {
                return;
            }

            _active = value;
            if (value)
            {
                WriteLights();
            }

            RenderTargetUpdateMode = value ? UpdateMode.Always : UpdateMode.Disabled;
        }
    }

    /// <summary>
    /// Sets the scene's light: the ambient, the direction <i>towards</i> the distant light (Godot axes) and the light's colour
    /// times its intensity (<see cref="CreatureModeLighting.Apply"/>). Written at once when the view is active, else when it
    /// next becomes so.
    /// </summary>
    public void SetLights(Vector3 ambient, Vector3 towardLight, Vector3 lightColor)
    {
        _ambient = ambient;
        _towardLight = towardLight;
        _lightColor = lightColor;
        if (_active)
        {
            WriteLights();
        }
    }

    /// <summary>The portal-space scene's lights: ambient 0.3 and the distant light of intensity 2 (<see cref="CreatureModeLighting.ApplyPortalScene"/>).</summary>
    public void SetPortalSceneLights()
    {
        var ambient = Lifestone.Client.PortalSpace.PortalSpaceScene.AmbientColor;
        SetLights(
            new Vector3(ambient.X, ambient.Y, ambient.Z),
            CreatureModeLighting.PortalSceneDirection,
            Vector3.One * Lifestone.Client.PortalSpace.PortalSpaceScene.LightIntensity);
    }

    /// <summary>The camera's lens (<see cref="CameraLenses.Apply"/>).</summary>
    public void SetLens(in CameraLens lens) => CameraLenses.Apply(Camera, lens);

    /// <summary>
    /// Draws the view once while it is inactive, with its lights: the start-up warm render. A no-op for an active view (it is
    /// drawing already). The engine returns the view to disabled after that frame.
    /// </summary>
    public void RenderOnce()
    {
        if (_active)
        {
            return;
        }

        WriteLights();
        RenderTargetUpdateMode = UpdateMode.Once;
        _onceRequestedAt = (long)Engine.GetFramesDrawn();
        WarmRenders++;
    }

    /// <summary>
    /// True until the engine has drawn a frame after the last <see cref="RenderOnce"/> (the server draws the once-viewport in that
    /// frame; the node's own <see cref="SubViewport.RenderTargetUpdateMode"/> keeps reading "once" afterwards, so the engine's frame
    /// counter is the witness).
    /// </summary>
    public bool RenderPending => !_active && _onceRequestedAt >= 0 && (long)Engine.GetFramesDrawn() <= _onceRequestedAt;

    private void WriteLights() => CreatureModeLighting.Apply(_ambient, _towardLight, _lightColor);
}
