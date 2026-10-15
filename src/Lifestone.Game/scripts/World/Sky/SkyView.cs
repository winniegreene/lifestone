using Godot;
using Lifestone.Client.Sky;

namespace Lifestone.Game.World.Sky;

/// <summary>
/// The view retail's before-pass sky is drawn in: an off-screen viewport with its own world holding the sky objects
/// (anchor at the origin), a camera that copies the main camera's orientation, field of view and position relative to
/// the anchor, retail's sky clip planes, a black clear, and a linear float buffer so blends happen on raw values. Its
/// texture is the main view's background (<see cref="SkyShaders.BackgroundCode"/>).
/// </summary>
/// <remarks>
/// Retail clears to black (<c>SceneTool::BeginScene</c>, acclient.c:122402) and draws the sky with <c>zfar</c> x 4
/// (<see cref="SkySurfaceVariant.FarClip"/>) and <c>Render::znear</c> = 0.1 (acclient.c:45526).
/// </remarks>
public partial class SkyView : SubViewport
{
    /// <summary>Retail <c>Render::znear</c> (acclient.c:45526).</summary>
    public const float NearClip = 0.1f;

    public Camera3D Camera { get; private set; } = null!;

    /// <summary>Parent of the before-pass sky object nodes (sky-local space: the anchor is the origin).</summary>
    public Node3D Objects { get; private set; } = null!;

    public static SkyView Create()
    {
        var view = new SkyView
        {
            Name = "SkyView",
            OwnWorld3D = true,
            UseHdr2D = true,
            TransparentBg = false,
            Msaa3D = Viewport.Msaa.Disabled,
            ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled,
            UseTaa = false,
            UseDebanding = false,
            RenderTargetUpdateMode = UpdateMode.Always,
            Size = new Vector2I(1280, 720),
        };

        var environment = new WorldEnvironment
        {
            Name = "SkyEnvironment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Colors.Black,
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
            },
        };
        view.AddChild(environment);
        view.Camera = new Camera3D { Name = "SkyCamera", Near = NearClip, Far = SkySurfaceVariant.FarClip, Current = true };
        view.AddChild(view.Camera);
        view.Objects = new Node3D { Name = "SkyObjects" };
        view.AddChild(view.Objects);
        return view;
    }

    /// <summary>
    /// Not drawing: the view's render target stops updating (<see cref="SubViewport.RenderTargetUpdateMode"/> disabled) and
    /// keeps its last image. Set while the 3D world is hidden (the portal tunnel is up, a full-screen screen covers it): the
    /// world arbiter (<see cref="Lifestone.Game.Render.WorldVisibility"/>) drives it, so the sky costs nothing then and
    /// draws a fresh frame again with the first frame the world shows (a viewport updates before the viewport that samples
    /// it). A SubViewport keeps rendering when the main viewport's <c>Disable3D</c> is set, which is why this is needed.
    /// </summary>
    public bool Paused
    {
        get => RenderTargetUpdateMode == UpdateMode.Disabled;
        set => RenderTargetUpdateMode = value ? UpdateMode.Disabled : UpdateMode.Always;
    }

    /// <summary>
    /// Copies the main camera (orientation, lens, size) with the eye at <paramref name="eyeFromAnchor"/>. The lens is the
    /// camera's projection mode with its near plane and either the field of view or, in frustum mode (the portal zoom,
    /// D31), the frustum's size and offset: the sky sees exactly what the world sees.
    /// </summary>
    public void Follow(Camera3D main, Vector3 eyeFromAnchor, Vector2I size)
    {
        if (size.X > 0 && size.Y > 0 && Size != size)
        {
            Size = size;
        }

        Camera.KeepAspect = main.KeepAspect;
        Camera.Projection = main.Projection;
        if (main.Projection == Camera3D.ProjectionType.Frustum)
        {
            // Frustum mode: Size is the frustum's height at the near plane, so the near plane travels with it; the offset is
            // in the same units. (Retail's near plane moves with the zoom for the sky pass too: Render::set_vdst.)
            Camera.Near = main.Near;
            Camera.Size = main.Size;
            Camera.FrustumOffset = main.FrustumOffset;
        }
        else
        {
            Camera.Fov = main.Fov;
            Camera.Near = NearClip; // retail's znear for the sky pass (acclient.c:45526)
        }

        Camera.Transform = new Transform3D(main.GlobalTransform.Basis, eyeFromAnchor);
    }
}
