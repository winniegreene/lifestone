using Godot;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.Rendering;
using Lifestone.Game.Render;

namespace Lifestone.Game.PortalSpace;

/// <summary>
/// The portal zoom on the world camera (docs/portal-space.md §6): while the zoom owns the projection
/// (<see cref="PortalLens.Overridden"/>) the camera is an off-centre frustum of retail's size at retail's near plane (D31:
/// <c>Near = f/4</c> below f = 0.4, no 179-degree cap); when the zoom lets go the camera gets back the perspective lens it had.
/// </summary>
/// <remarks>
/// <para><b>One lens, every reader.</b> The lens is set on the camera itself, so the sky view (<c>SkyView.Follow</c> copies the
/// projection mode, near, size and offset), the interiors' frustum and view key (<see cref="CameraLenses.Of"/>) and picking all
/// see what the world sees. The presenter runs at priority 15, before the sky controller (20); the interiors controller
/// (priority 5) reads the lens a frame late, which is invisible (a lens changes smoothly) except for the very first frame of the
/// arrival's expansion.</para>
/// <para><b>The frame's centre (D40).</b> The zoom collapses and expands about the centre of the SmartBox frame
/// (<c>GameViewRect</c>). The in-game shell's frame fills the screen for now (owner decision), so <see cref="ViewCentre"/>
/// is (0.5, 0.5) and the frustum offset is exactly zero; <see cref="ViewCentre"/> is the hook the retail UI's viewport element will
/// set (<c>CameraLens.OffsetForCentre</c> moves the frustum's centre to any other fraction of the window). No offset is applied today.</para>
/// <para><b>The rest lens</b> is whatever perspective the camera had when the zoom took over (<c>--fov</c>, 60 degrees by default, near
/// 0.1); <see cref="RestFocal"/> is its focal distance, which the machine takes as f_game.</para>
/// </remarks>
public sealed class WorldLens
{
    private const float DefaultRestFovDegrees = 60f;
    private const float DefaultRestNear = 0.1f;

    private readonly Camera3D _camera;
    private CameraLens _rest;

    /// <summary>Creates the lens controller for the world camera; the camera's current perspective is the rest lens.</summary>
    public WorldLens(Camera3D camera)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _rest = Capture();
    }

    /// <summary>
    /// The frame's centre as fractions of the window (0 to 1 from the left, from the top): the <c>GameViewRect</c> hook (D40).
    /// The default, the middle of the window, gives a frustum offset of exactly zero.
    /// </summary>
    public Func<(float X, float Y)> ViewCentre { get; set; } = static () => (0.5f, 0.5f);

    /// <summary>True while the zoom owns the camera's projection.</summary>
    public bool Overridden { get; private set; }

    /// <summary>Times the zoom has taken the projection over.</summary>
    public int Overrides { get; private set; }

    /// <summary>The perspective lens the camera returns to.</summary>
    public CameraLens Rest => _rest;

    /// <summary>The camera's rest focal distance, f_game = 1 / tan(fov / 2) (√3 for the default 60 degrees).</summary>
    public float RestFocal => PortalZoom.FocalFromVerticalFov(_rest.VerticalFovDegrees * (Math.PI / 180.0));

    /// <summary>The lens the camera is drawn with now.</summary>
    public CameraLens Current => CameraLenses.Of(_camera);

    /// <summary>
    /// Applies the frame's lens to the world camera: the zoom's frustum while it is overridden, else the rest perspective.
    /// Cheap when nothing changes (a restore happens once).
    /// </summary>
    /// <param name="lens">The frame's <see cref="PortalFrame.Lens"/>.</param>
    public void Follow(in PortalLens lens)
    {
        if (!lens.Overridden)
        {
            Release();
            return;
        }

        if (!Overridden)
        {
            _rest = Capture();
            Overridden = true;
            Overrides++;
        }

        var (centreX, centreY) = ViewCentre();
        var size = _camera.GetViewport().GetVisibleRect().Size;
        var aspect = size.Y > 0f ? size.X / size.Y : 16f / 9f;
        var (offsetX, offsetY) = CameraLens.OffsetForCentre(lens.FrustumHeightAtNear, aspect, centreX, centreY);
        CameraLenses.Apply(_camera, CameraLens.FromPortal(lens, offsetX, offsetY));
    }

    /// <summary>Gives the camera back its rest perspective, if the zoom had taken it.</summary>
    public void Release()
    {
        if (!Overridden)
        {
            return;
        }

        Overridden = false;
        CameraLenses.Apply(_camera, _rest);
    }

    /// <summary>The camera's perspective now, or the default one when it is not in perspective mode (a frustum left by someone else).</summary>
    private CameraLens Capture() =>
        _camera.Projection == Camera3D.ProjectionType.Perspective
            ? CameraLens.Perspective(_camera.Fov, _camera.Near)
            : CameraLens.Perspective(DefaultRestFovDegrees, DefaultRestNear);
}
