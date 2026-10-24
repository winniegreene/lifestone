using Godot;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.Rendering;
using Lifestone.Data.Animation;
using Lifestone.Game.Core;
using Lifestone.Game.CreatureMode;
using Lifestone.Game.Render;
using Lifestone.Game.SpellViewer;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.PortalSpace;

/// <summary>
/// The portal tunnel on screen (docs/portal-space.md §5, §11.1): the <see cref="CreatureModeView"/> holding the two halves of the
/// UIASSET torus, drawn over the whole window, behind the interface. It poses the parts from
/// <see cref="TunnelObject.PartFrames"/>, puts the camera at <see cref="PortalSpaceScene.CameraPosition"/> rolled about AC +Y,
/// and takes the portal zoom's lens; the presenter (<c>PortalSpacePresenter</c>) and the <c>--portal-space</c> hold decide what
/// shows and when.
/// </summary>
/// <remarks>
/// <para><b>Where it draws.</b> A canvas layer below the default one (<see cref="TunnelLayer"/> −1: the F3 overlay and the
/// position HUD stay on top of it, and so does every interface layer) with one full-window <see cref="TextureRect"/> of the
/// view's texture; the view is the window's size and follows its resizing. The in-game shell's SmartBox fills the screen (owner
/// decision), so the tunnel does too; the SmartBox viewport element 0x10000436 will take the texture over when the retail UI
/// owns the layout (<c>GameViewRect</c>, D40): with the in-game shell up, the viewport draws it
/// (<see cref="PortalTunnelViewportSource"/>, <see cref="DrawnByViewport"/>) and this layer's picture is hidden.</para>
/// <para><b>Cost.</b> Hidden, the view does not render and the layer is not drawn. While shown the tunnel takes
/// <see cref="FrameBudgetShare.TunnelShareMilliseconds"/> from the main-thread queue's budget.</para>
/// <para><b>Materials.</b> The parts are meshes of the shared render path built for <c>SurfaceLighting.CreatureMode</c>
/// (<see cref="ActorMeshLibrary"/>): lit by the scene's ambient 0.3 and one distant light, no fog, no sun.</para>
/// </remarks>
public partial class PortalTunnelView : CanvasLayer
{
    /// <summary>The canvas layer: behind the default layer's contents and every interface layer.</summary>
    public const int TunnelLayer = -1;

    private const int MaxWarmFrames = 10;

    private int _warmFrames;
    private MeshInstance3D[] _parts = [];
    private System.Numerics.Vector3[] _scales = [];
    private ActorMeshLibrary? _meshes;
    private TextureRect _screen = null!;
    private bool _shown;
    private bool _drawnByViewport;
    private bool _rollTodoLogged;
    private double _roll;

    /// <summary>The off-screen view.</summary>
    public CreatureModeView View { get; private set; } = null!;

    /// <summary>Whether the tunnel is on screen.</summary>
    public bool Shown => _shown;

    /// <summary>
    /// True while the retail UI draws the tunnel in the SmartBox viewport's hole (<see cref="PortalTunnelViewportSource"/>): this layer's own full-window
    /// picture then stays hidden, so the tunnel is drawn once.
    /// </summary>
    public bool DrawnByViewport
    {
        get => _drawnByViewport;
        set
        {
            _drawnByViewport = value;
            if (_screen is not null)
            {
                _screen.Visible = !value;
            }
        }
    }

    /// <summary>The roll currently applied to the camera, in degrees.</summary>
    public double RollDegrees => _roll;

    /// <summary>The part nodes (checks).</summary>
    public IReadOnlyList<MeshInstance3D> Parts => _parts;

    /// <summary>
    /// Main thread: builds the view and the part nodes for the tunnel Setup, with creature-mode materials, and draws one
    /// frame while hidden (the warm render) so the first transition does not pay for compiling its pipelines.
    /// </summary>
    /// <param name="services">The shared services.</param>
    /// <param name="setupId">The tunnel Setup (<see cref="PortalSpaceAssets.TunnelSetupId"/>).</param>
    /// <param name="tunnel">The tunnel object, posed once so the first frame shows its first pose.</param>
    public static PortalTunnelView Create(GameServices services, uint setupId, TunnelObject tunnel, double? restFovDegrees = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tunnel);
        var assembly = services.Setups.Get(setupId) ?? throw new InvalidDataException($"The portal-space tunnel Setup 0x{setupId:X8} does not exist.");
        if (assembly.Parts.Count != tunnel.PartCount)
        {
            throw new InvalidDataException($"The tunnel object has {tunnel.PartCount} part(s) but Setup 0x{setupId:X8} has {assembly.Parts.Count}.");
        }

        var node = new PortalTunnelView { Name = "PortalTunnel", Layer = TunnelLayer, Visible = false };
        var window = WindowSize();
        node.View = CreatureModeView.Create("PortalTunnelView", window, services.GetViewport().Msaa3D);
        node.AddChild(node.View);
        node.View.SetPortalSceneLights();
        node._screen = new TextureRect
        {
            Name = "TunnelScreen",
            Texture = node.View.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        node._screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        node.AddChild(node._screen);

        node._meshes = new ActorMeshLibrary(services, SurfaceLighting.CreatureMode);
        node._parts = new MeshInstance3D[assembly.Parts.Count];
        node._scales = new System.Numerics.Vector3[assembly.Parts.Count];
        for (var i = 0; i < node._parts.Length; i++)
        {
            var part = assembly.Parts[i];
            node._scales[i] = part.DefaultScale;
            node._parts[i] = new MeshInstance3D
            {
                Name = $"Part{i:D2}_0x{part.GfxObjId:X8}",
                Mesh = part.IsGfxObj ? node._meshes.Get(part.GfxObjId) : null,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            node.View.Objects.AddChild(node._parts[i]);
        }

        node.Pose(tunnel);
        node.PlaceCamera(0.0);
        node.View.SetLens(CameraLens.FromPortal(new PortalLens(PortalZoom.FocalFromVerticalFov((restFovDegrees ?? services.Options.Fov ?? GameOptions.DefaultFov) * Math.PI / 180.0), Overridden: true)));
        node.View.RenderOnce();
        return node;
    }

    /// <summary>The window's size in pixels (at least 2 x 2).</summary>
    private static Vector2I WindowSize()
    {
        var size = DisplayServer.WindowGetSize();
        return new Vector2I(Math.Max(size.X, 2), Math.Max(size.Y, 2));
    }

    public override void _Ready() => GetTree().Root.SizeChanged += OnWindowResized;

    public override void _Process(double delta)
    {
        // Watches the start-up warm render until the engine has drawn it, then stops processing. At debug level it also reads the
        // texture back once and says how much of it is lit (a one-off GPU readback, so never at the default level).
        _warmFrames++;
        if (View.RenderPending && _warmFrames <= MaxWarmFrames)
        {
            return;
        }

        if (Log.MinimumLevel <= LogLevel.Debug)
        {
            var image = View.GetTexture().GetImage();
            var lit = 0;
            for (var y = 0; y < image.GetHeight(); y += 8)
            {
                for (var x = 0; x < image.GetWidth(); x += 8)
                {
                    lit += image.GetPixel(x, y).Luminance > 0.01f ? 1 : 0;
                }
            }

            Log.Debug("portal", "portal tunnel warm render drew", ("size", image.GetSize()), ("lit_samples", lit), ("of", (image.GetWidth() / 8) * (image.GetHeight() / 8)));
        }

        Log.Info("portal", "portal tunnel warm render", ("frames_waited", _warmFrames), ("drawn", !View.RenderPending));
        SetProcess(false);
    }

    public override void _ExitTree()
    {
        if (IsInsideTree() && GetTree()?.Root is { } root)
        {
            root.SizeChanged -= OnWindowResized;
        }

        if (_shown)
        {
            FrameBudgetShare.Release(FrameBudgetShare.TunnelShareName);
        }

        _meshes?.Dispose();
        _meshes = null;
    }

    private void OnWindowResized()
    {
        var size = WindowSize();
        if (View.Size != size)
        {
            View.Size = size;
        }
    }

    /// <summary>Shows or hides the tunnel: the layer, the view's drawing and the queue-budget share.</summary>
    public void SetShown(bool shown)
    {
        if (_shown == shown)
        {
            return;
        }

        _shown = shown;
        Visible = shown;
        View.Active = shown;
        if (shown)
        {
            if (!_rollTodoLogged)
            {
                _rollTodoLogged = true;
                Log.Warn("portal", "TODO(portal): the tunnel roll's sign is unverified against retail (AcToGodot.CameraRotation); it only shows in a frame captured at a fixed seed");
            }

            FrameBudgetShare.Take(FrameBudgetShare.TunnelShareName, FrameBudgetShare.TunnelShareMilliseconds);
        }
        else
        {
            FrameBudgetShare.Release(FrameBudgetShare.TunnelShareName);
        }
    }

    /// <summary>Poses the part nodes from the tunnel object's frames (AC to Godot through <see cref="AcToGodot"/>).</summary>
    public void Pose(TunnelObject tunnel)
    {
        ArgumentNullException.ThrowIfNull(tunnel);
        var frames = tunnel.PartFrames;
        for (var i = 0; i < _parts.Length; i++)
        {
            _parts[i].Transform = AcToGodot.Transform(frames[i].Origin, frames[i].Orientation, _scales[i]);
        }
    }

    /// <summary>
    /// Puts the camera at (0.24, −2.7, 0.88) looking along AC +Y, rolled by <paramref name="rollDegrees"/> about that axis
    /// (<c>CreatureMode::SetCameraPosition</c> 11.4186 0x00452360, 11.6096 0x004523A0, and <c>SetCameraDirection_Degrees</c> 0x00453760 / 0x004537A0; the roll's sign is a
    /// <c>TODO(portal)</c> in <see cref="AcToGodot.CameraRotation(double, double, double)"/>).
    /// </summary>
    public void PlaceCamera(double rollDegrees)
    {
        _roll = rollDegrees;
        var camera = View.Camera;
        camera.Position = AcToGodot.Position(PortalSpaceScene.CameraPosition);
        camera.Basis = new Basis(AcToGodot.CameraRotation(0.0, 0.0, rollDegrees));
    }

    /// <summary>The portal zoom's lens for the tunnel camera (a frustum centred on the whole window, D31/D40).</summary>
    public void SetLens(in PortalLens lens) => View.SetLens(CameraLens.FromPortal(lens));
}
