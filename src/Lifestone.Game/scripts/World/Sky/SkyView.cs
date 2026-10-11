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

    /// <summary>Copies the main camera (orientation, lens, size) with the eye at <paramref name="eyeFromAnchor"/>.</summary>
    public void Follow(Camera3D main, Vector3 eyeFromAnchor, Vector2I size)
    {
        if (size.X > 0 && size.Y > 0 && Size != size)
        {
            Size = size;
        }

        Camera.Fov = main.Fov;
        Camera.KeepAspect = main.KeepAspect;
        Camera.Projection = main.Projection;
        Camera.Transform = new Transform3D(main.GlobalTransform.Basis, eyeFromAnchor);
    }
}
