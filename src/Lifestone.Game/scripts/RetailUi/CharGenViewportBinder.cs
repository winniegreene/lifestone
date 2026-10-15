using Godot;
using Lifestone.Client.Avatars;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.Rendering;
using Lifestone.Game.Core;
using Lifestone.Game.CreatureMode;
using Lifestone.Game.Render;
using Lifestone.Game.SpellViewer;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.RetailUi;

/// <summary>
/// Character creation's 3D character in a viewport element's hole (docs/retail-ui.md §20): the avatars track's <see cref="CreaturePreview"/> of a
/// <see cref="Cg3DViewModel"/> drawn into the portal track's <see cref="CreatureModeView"/>, whose texture the <see cref="RetailViewportHost"/> shows in the
/// hole. The view's own world holds one node per part of the character (and of the heritage's backdrop), a camera at the model's position, and creature
/// mode's light: one distant light of intensity 2 and the 0.3 ambient (<see cref="Cg3DViewModel.LightDirection"/>).
/// </summary>
/// <remarks>
/// <para><b>What it draws.</b> Each frame the model's version is compared with the one last drawn; when it changed (a choice, the camera, the heading, a
/// step of the pose) every part is posed again from <see cref="CreaturePreview.Draws"/> through <see cref="AcToGodot"/>, and a part whose GfxObj or whose
/// dress (<see cref="Lifestone.Data.Appearance.PartAppearance.VariantKey"/>: its texture swaps and its modified palette) changed gets the mesh of that
/// variant from an <see cref="ActorMeshLibrary"/> built for creature-mode lighting; the variants no part uses any more are released.</para>
/// <para><b>Cost.</b> The view renders only while its hole shows (<see cref="CreatureModeView.Active"/>), at the hole's size in window pixels. Two binders
/// exist (the appearance page's and the summary page's); only the visible page's hole is bound as visible, so one view draws at a time, as creature mode's
/// shared light globals need.</para>
/// <para><b>The light's sign.</b> The CharGen light vector is (0.3, 1.9, 0.65); portal space's is (0.3, -1.9, 0.65) and is read as pointing towards the
/// light (<c>TODO(portal)</c> in <see cref="CreatureModeLighting"/>). Read the same way, the CharGen light would stand behind the character, whose front
/// faces the camera at -Y; retail's lights are D3D lights, whose direction is the way the light travels, so the CharGen vector is taken to travel along
/// +Y, lighting the figure's front (<c>TODO(retailui-chargen-light)</c>: unverified against retail; one constant, <see cref="LightTravelsAlongVector"/>).</para>
/// </remarks>
public sealed partial class CharGenViewportBinder : Node, IUiViewportSource
{
    /// <summary>True when the light vector is the direction the light travels (D3D's reading), so the direction towards the light is its negation.</summary>
    public const bool LightTravelsAlongVector = true;

    /// <summary>The near plane of the view's camera, in metres.</summary>
    public const float Near = 0.1f;

    private readonly GameServices _services;
    private readonly Cg3DViewModel _model;
    private readonly string _label;
    private CreatureModeView _view = null!;
    private ActorMeshLibrary _meshes = null!;
    private MeshInstance3D[] _parts = [];
    private string?[] _partKeys = [];
    private uint _partsSetup;
    private MeshInstance3D[] _backdropParts = [];
    private uint _backdropSetup;
    private readonly HashSet<string> _keep = [];
    private int _lastVersion = -1;
    private bool _visible;
    private int _redraws;
    private bool _lightLogged;

    /// <summary>A binder for <paramref name="model"/>; <paramref name="label"/> names it in logs (the page it serves).</summary>
    public CharGenViewportBinder(GameServices services, Cg3DViewModel model, string label)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(model);
        _services = services;
        _model = model;
        _label = label;
        Name = $"CharGenViewport_{label}";
    }

    /// <summary>The off-screen view.</summary>
    public CreatureModeView View => _view;

    /// <summary>How many times the parts were posed again (checks).</summary>
    public int Redraws => _redraws;

    /// <summary>The part nodes of the character (checks).</summary>
    public IReadOnlyList<MeshInstance3D> Parts => _parts;

    /// <summary>The part nodes of the backdrop (checks).</summary>
    public IReadOnlyList<MeshInstance3D> BackdropParts => _backdropParts;

    /// <summary>True while the hole shows.</summary>
    public bool Visible => _visible;

    /// <inheritdoc/>
    public Texture2D? Texture => _view?.GetTexture();

    /// <inheritdoc/>
    public override void _Ready()
    {
        _view = CreatureModeView.Create($"CharGenView_{_label}", new Vector2I(2, 2), _services.GetViewport().Msaa3D);
        AddChild(_view);
        _meshes = new ActorMeshLibrary(_services, SurfaceLighting.CreatureMode);
        var towardLight = CreatureModeLighting.FromAc(LightTravelsAlongVector ? -Cg3DViewModel.LightDirection : Cg3DViewModel.LightDirection);
        _view.SetLights(new Vector3(Cg3DViewModel.Ambient.X, Cg3DViewModel.Ambient.Y, Cg3DViewModel.Ambient.Z), towardLight, Vector3.One * CreaturePreview.LightIntensity);
        if (!_lightLogged)
        {
            _lightLogged = true;
            Log.Warn("retailui", "TODO(retailui-chargen-light): the CharGen light vector (0.3, 1.9, 0.65) is read as the direction the light travels (D3D), so it lights the figure's front; unverified against retail", ("toward_light", towardLight));
        }

        SetLens();
    }

    /// <inheritdoc/>
    public void Bound(Rect2I windowRect, bool visible)
    {
        _visible = visible && windowRect.Size.X > 0 && windowRect.Size.Y > 0;
        if (_view is null)
        {
            return;
        }

        if (windowRect.Size.X > 0 && windowRect.Size.Y > 0)
        {
            var size = new Vector2I(Math.Max(windowRect.Size.X, 2), Math.Max(windowRect.Size.Y, 2));
            if (_view.Size != size)
            {
                _view.Size = size;
            }
        }

        _view.Active = _visible;
        _lastVersion = -1;
    }

    /// <summary>The camera's lens: the game's field of view (<c>--fov</c>, 60 by default) as the view's vertical angle.</summary>
    private void SetLens() =>
        _view.SetLens(CameraLens.Perspective((float)(_services.Options.Fov ?? GameOptions.DefaultFov), Near));

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (!_visible || _view is null || _model.Version == _lastVersion)
        {
            return;
        }

        _lastVersion = _model.Version;
        Pose();
    }

    /// <summary>Poses the camera, the character and the backdrop from the model.</summary>
    private void Pose()
    {
        _redraws++;
        var camera = _view.Camera;
        camera.Position = AcToGodot.Position(_model.CameraPosition);
        var direction = _model.CameraDirection;
        camera.Basis = new Basis(AcToGodot.CameraRotation(direction.Z, direction.X, direction.Y));
        var keep = _keep;
        keep.Clear();
        if (_model.Preview is { } preview)
        {
            if (_partsSetup != preview.Setup.Id || _parts.Length != preview.Draws.Length)
            {
                ReplaceParts(preview.Draws.Length, preview.Setup.Id);
            }

            var draws = preview.Draws;
            for (var i = 0; i < draws.Length; i++)
            {
                var draw = draws[i];
                var node = _parts[i];
                if (draw.GfxObjId == 0)
                {
                    node.Visible = false;
                    continue;
                }

                var appearance = preview.Parts[i];
                var key = _meshes.VariantKey(draw.GfxObjId, appearance);
                keep.Add(key);
                if (_partKeys[i] != key)
                {
                    _partKeys[i] = key;
                    node.Mesh = _meshes.Get(draw.GfxObjId, appearance);
                }

                node.Transform = AcToGodot.Transform(draw.Frame.Origin, draw.Frame.Orientation, draw.Scale);
                node.Visible = node.Mesh is not null;
            }
        }

        PoseBackdrop(keep);
        _meshes.ReleaseVariantsExcept(keep);
    }

    private void PoseBackdrop(HashSet<string> keep)
    {
        var draws = _model.BackdropDraws;
        if (_backdropSetup != _model.BackdropSetupId || _backdropParts.Length != draws.Length)
        {
            foreach (var node in _backdropParts)
            {
                node.QueueFree();
            }

            _backdropSetup = _model.BackdropSetupId;
            _backdropParts = new MeshInstance3D[draws.Length];
            for (var i = 0; i < draws.Length; i++)
            {
                _backdropParts[i] = new MeshInstance3D
                {
                    Name = $"Backdrop{i:D2}_0x{draws[i].GfxObjId:X8}",
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                };
                _view.Objects.AddChild(_backdropParts[i]);
            }
        }

        for (var i = 0; i < draws.Length; i++)
        {
            var draw = draws[i];
            var node = _backdropParts[i];
            node.Mesh ??= _meshes.Get(draw.GfxObjId);
            node.Transform = AcToGodot.Transform(draw.Frame.Origin, draw.Frame.Orientation, draw.Scale);
            node.Visible = node.Mesh is not null;
        }
    }

    /// <summary>The character is a different object (a new body, or another heritage): its part nodes are made again.</summary>
    private void ReplaceParts(int count, uint setupId)
    {
        foreach (var node in _parts)
        {
            node.QueueFree();
        }

        _partsSetup = setupId;
        _parts = new MeshInstance3D[count];
        _partKeys = new string?[count];
        for (var i = 0; i < count; i++)
        {
            _parts[i] = new MeshInstance3D
            {
                Name = $"Part{i:D2}",
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                Visible = false,
            };
            _view.Objects.AddChild(_parts[i]);
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        _meshes?.Dispose();
    }
}
