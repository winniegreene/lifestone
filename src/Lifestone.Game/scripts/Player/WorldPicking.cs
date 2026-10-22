using Godot;
using Lifestone.Client.Avatars;
using Lifestone.Client.Play;
using Lifestone.Client.Rendering;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;
using Lifestone.Game.Core;
using Lifestone.Game.Render;
using Lifestone.Game.World;
using Lifestone.Game.World.ServerObjects;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.Player;

/// <summary>
/// Picking and selection projection over the world camera (M2 G5 W7, docs/avatars.md §18): the game side of <see cref="RayPicking"/> and
/// <see cref="ISelectionProjector"/>. It reads the camera's lens through <see cref="CameraLenses.Of"/> (the lens the world is drawn with, the D40 offset
/// included, not a second projection) and answers both ways: <see cref="TryPick"/> for a click (retail's per-part drawing sphere and polygon test over
/// the parts the avatar set drew last frame) and <see cref="TryProject"/> for the selected object's rectangle (retail's selection sphere).
/// </summary>
/// <remarks>
/// <para>Main thread. The view size is the retail UI's logical size, because the mouse is reported in logical UI pixels and the selection rectangle is
/// drawn in them; the world fills the window, so the aspect is the window's (<c>GameViewRect</c>, D40).</para>
/// <para><b>What can be picked:</b> every avatar the layer drew in the last frame, except the player (a hidden or undrawn object, an object in a cell not in
/// view and a wielded or parented one are not drawn on their own), and of those the parts that were drawn (a GfxObj, translucency below 1) at the pose
/// and level of detail they were drawn with (<see cref="AvatarFrame"/>). The ray is moved into AC axes (render space is the AC axes permuted, relative to
/// the render origin), where the part frames and the GfxObj meshes already are. The test runs at the click against the last drawn frame; retail tests in
/// the next draw (<c>SmartBox::find_object</c> marks the position, <c>Render::update_viewpoint</c> tests while drawing): one frame of pose, a deliberate deviation.
/// <b>Selection box:</b> the Setup's selection sphere, else a sphere over its height and radius, as retail's indicator uses it.</para>
/// </remarks>
public sealed class WorldPicking : ISelectionProjector
{
    private readonly Camera3D _camera;
    private readonly WorldRoot _world;
    private readonly Func<AvatarSet?> _avatars;
    private readonly Func<ServerObjectsLayer?> _layer;
    private readonly Func<Vector2> _viewSize;
    private readonly Func<uint, PickShape?> _shapes;
    private PickPart[] _parts = new PickPart[256];

    /// <summary>True for <c>--retail-picking</c>: retail's first polygon of a part and its local-unit hit times (<see cref="RayPicking.Pick"/>); default false.</summary>
    public bool RetailRules { get; set; } = PlayOptions.Parse(OS.GetCmdlineUserArgs()).RetailPicking;

    /// <summary>Makes the picker for <paramref name="camera"/>; the sources are read at every call (play mode renews the avatar set at every log off).</summary>
    /// <param name="camera">The world camera.</param>
    /// <param name="world">The world (its render origin).</param>
    /// <param name="avatars">The session's avatar set, or null.</param>
    /// <param name="layer">The server objects layer, or null.</param>
    /// <param name="viewSize">The view's size in logical UI pixels.</param>
    /// <param name="shapes">A GfxObj's drawing sphere and triangles (null when it has none).</param>
    public WorldPicking(Camera3D camera, WorldRoot world, Func<AvatarSet?> avatars, Func<ServerObjectsLayer?> layer, Func<Vector2> viewSize, Func<uint, PickShape?> shapes)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(avatars);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(viewSize);
        ArgumentNullException.ThrowIfNull(shapes);
        _shapes = shapes;
        _camera = camera;
        _world = world;
        _avatars = avatars;
        _layer = layer;
        _viewSize = viewSize;
    }

    /// <summary>The camera as picking sees it now; false when the view has no size.</summary>
    public bool TryView(out PickView view)
    {
        var size = _viewSize();
        if (!(size.X > 0f) || !(size.Y > 0f))
        {
            view = default;
            return false;
        }

        var basis = _camera.GlobalTransform.Basis;
        view = new PickView(
            CameraLenses.Of(_camera),
            size.X,
            size.Y,
            ToNumerics(_camera.GlobalPosition),
            ToNumerics(basis.X),
            ToNumerics(basis.Y),
            -ToNumerics(basis.Z));
        return true;
    }

    /// <summary>The nearest drawn object (not the player) under the window point (<paramref name="x"/>, <paramref name="y"/>) in logical UI pixels.</summary>
    public bool TryPick(float x, float y, out uint guid)
    {
        guid = 0;
        if (_avatars() is not { } avatars || !TryView(out var view))
        {
            return false;
        }

        var count = CollectParts(avatars, _layer());
        var ray = view.Ray(x, y);
        var acRay = new PickRay(ToAc(ray.Origin), ToAc(ray.Direction));
        if (RayPicking.Pick(acRay, _parts.AsSpan(0, count), _shapes, avatars.PlayerGuid, retailRules: RetailRules) is { } hit)
        {
            guid = hit.Guid;
            return true;
        }

        return false;
    }

    /// <summary>Every drawn part of every pickable object, placed as it was drawn, in AC axes relative to the render origin.</summary>
    private int CollectParts(AvatarSet avatars, ServerObjectsLayer? layer)
    {
        var frame = avatars.Frame;
        var drawn = frame.Parts;
        var translucency = frame.PartTranslucency;
        var count = 0;
        foreach (var draw in frame.Avatars)
        {
            if (draw.IsPlayer || !draw.Visible || !avatars.TryGet(draw.Guid, out var avatar) || !IsPickable(avatar, layer))
            {
                continue;
            }

            var anchor = AcCoordinates.LandblockOrigin(LandblockCoord.FromId(draw.AnchorCellId));
            for (var i = 0; i < draw.PartCount; i++)
            {
                var row = draw.PartStart + i;
                var part = drawn[row];
                if (part.GfxObjId == 0 || translucency[row] >= 1f)
                {
                    continue;
                }

                if (count == _parts.Length)
                {
                    Array.Resize(ref _parts, count * 2);
                }

                _parts[count++] = new PickPart(draw.Guid, part.PartIndex, part.GfxObjId, ToAc(_world.ToRender(anchor + part.Frame.Origin)), part.Frame.Orientation, part.Scale);
            }
        }

        return count;
    }

    /// <inheritdoc/>
    public bool TryProject(uint guid, out ScreenRect rect, out bool onScreen) => TryProjectCore(guid, false, out rect, out onScreen);

    /// <summary>As <see cref="TryProject"/>, but the player's own avatar can be projected too (the review hunt frames judge whether the player hides the target; nothing else asks).</summary>
    /// <param name="guid">The object.</param>
    /// <param name="rect">Its selection box.</param>
    /// <param name="onScreen">False when behind the camera or outside the view.</param>
    public bool TryProjectIncludingPlayer(uint guid, out ScreenRect rect, out bool onScreen) => TryProjectCore(guid, true, out rect, out onScreen);

    private bool TryProjectCore(uint guid, bool allowPlayer, out ScreenRect rect, out bool onScreen)
    {
        rect = default;
        onScreen = false;
        if (_avatars() is not { } avatars || !avatars.TryGet(guid, out var avatar) || (avatar.IsPlayer && !allowPlayer) || !IsPickable(avatar, _layer()) || !TryBounds(avatar, out var centre, out var radius) || !TryView(out var view))
        {
            return false;
        }

        view.TryProjectSphere(centre, radius, out rect, out onScreen);
        return true;
    }

    private static bool IsPickable(Avatar avatar, ServerObjectsLayer? layer) => Refusal(avatar, layer) is null;

    // M4 3.1 p (m4-hunt-frame-camera): the layer never draws the player (ServerObjectsLayer._Process skips IsPlayer), so the layer test is not asked for it; see SelectionProjectionRules.
    private static string? Refusal(Avatar avatar, ServerObjectsLayer? layer) =>
        SelectionProjectionRules.Refusal(avatar.IsPlayer, avatar.Body.IsDrawn, avatar.Body.Parent is not null, layer is null || layer.IsDrawn(avatar.Guid));

    /// <summary>
    /// Why the selection box of <paramref name="guid"/> cannot be projected now, as a phrase naming the input (M4 3.1 p, <c>m4-hunt-frame-camera</c>: the hunt logs it once when the player's box is missing), or null when it can.
    /// </summary>
    /// <param name="guid">The object.</param>
    /// <param name="allowPlayer">True when the player's own avatar may be projected (<see cref="TryProjectIncludingPlayer"/>).</param>
    public string? WhyNotProjected(uint guid, bool allowPlayer)
    {
        if (_avatars() is not { } avatars)
        {
            return "the session has no avatar set";
        }

        if (!avatars.TryGet(guid, out var avatar))
        {
            return $"the avatar set has no avatar for 0x{guid:X8} (its player is 0x{avatars.PlayerGuid:X8})";
        }

        if (avatar.IsPlayer && !allowPlayer)
        {
            return "the player's own avatar is not selectable";
        }

        if (Refusal(avatar, _layer()) is { } refusal)
        {
            return refusal;
        }

        if (!TryBounds(avatar, out _, out _))
        {
            return "the selection sphere has no radius";
        }

        return TryView(out _) ? null : "the view has no size";
    }

    /// <summary>The object selection sphere in render space.</summary>
    private bool TryBounds(Avatar avatar, out AcVector3 centre, out float radius)
    {
        var body = avatar.Body;
        var selection = body.Setup.SelectionSphere;
        var scale = body.Scale;
        AcVector3 local;
        if (selection.Radius > 0f)
        {
            local = selection.Center * scale;
            radius = selection.Radius * scale;
        }
        else
        {
            // No selection sphere in the Setup: a sphere over the cylinder (retail falls back to a small sphere above the origin: CPhysicsObj::GetSelectionSphere).
            radius = MathF.Max(body.Radius, body.Height * 0.5f);
            local = new AcVector3(0f, 0f, body.Height * 0.5f);
        }

        var frame = body.AnchorFrame;
        var anchor = AcCoordinates.LandblockOrigin(LandblockCoord.FromId(body.AnchorCellId));
        var at = frame.Origin + AcVector3.Transform(local, frame.Orientation);
        centre = ToNumerics(_world.ToRender(anchor + at));
        return radius > 0f;
    }

    private static AcVector3 ToAc(Vector3 render) => AcToGodot.ToAc(render);

    private static AcVector3 ToAc(AcVector3 render) => AcToGodot.ToAc(new Vector3(render.X, render.Y, render.Z));

    private static AcVector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);
}
