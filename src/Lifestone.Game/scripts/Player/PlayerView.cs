using Godot;
using Lifestone.Client.Avatars;
using Lifestone.Client.Interiors;
using Lifestone.Client.Play;
using Lifestone.Client.Rendering;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;
using Lifestone.Game.Core;
using Lifestone.Game.SpellViewer;
using Lifestone.Game.World;
using Lifestone.Game.World.Interiors;
using Lifestone.Game.World.ServerObjects;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.Player;

/// <summary>
/// Draws the player's character in the world from the avatars track's output (<see cref="AvatarFrame"/>): one
/// <see cref="DynamicObjectBatch"/> per part, each holding one instance (the part's transform and the four floats of
/// <c>INSTANCE_CUSTOM</c> the dynamic lighting rules read), with the part's mesh as the character's ObjDesc dresses it
/// (<see cref="ActorMeshLibrary"/> variants keyed by <see cref="Lifestone.Data.Appearance.PartAppearance.VariantKey"/>, as character
/// creation's figure does).
/// </summary>
/// <remarks>
/// <para><b>Lighting.</b> Outdoors the parts use <see cref="SurfaceLighting.DynamicObject"/> (the sun and the world ambient, as every
/// object outdoors); in an EnvCell, <see cref="SurfaceLighting.DynamicCellObject"/>: no sun, the world ambient and the up to eight
/// lights of the cell that reach the character (<see cref="ObjectLightSets"/>, D46), summed per vertex in the shader because the
/// character moves. The two rules are two mesh libraries (a material carries its lighting rule), built together by
/// <see cref="Prepare"/> so the shaders compile at start-up, not on the frame the character walks through a door.</para>
/// <para><b>Why MultiMesh batches and not mesh nodes.</b> The per-instance values (the cell's slot, the light-set row, the
/// luminosity override, the translucency) are <c>INSTANCE_CUSTOM</c>, which only a MultiMesh instance has; Godot's per-node
/// instance uniforms would reserve a block of the shared shader-uniform buffer per node (SurfaceLightingCode remarks). A single
/// character does not need the batching itself (one instance per mesh variant), but it is the path the server's objects will take
/// with many instances per batch, so the shaders and the batch are the real ones now.</para>
/// <para><b>Visibility.</b> The character is drawn when the avatars say it is visible (<see cref="AvatarDraw.Visible"/>: in a cell,
/// not hidden, not the first-person body), and in a cell only while the interiors show that cell this frame, as retail draws an
/// object of an EnvCell only with the cell. The batches are on the interiors' cells layer in a cell, so a camera that has hidden the
/// landscape still sees the character; outdoors they are on the default layer.</para>
/// <para><b>Translucency (D45, D63; closes <c>TODO(g5-translucency)</c> for the materialise fade).</b> <c>INSTANCE_CUSTOM.w</c> scales the alpha of blended parts. An opaque part
/// has no blend to scale, so each part has a second batch (<c>PartFadeNN</c>) holding the blended twin of its mesh (<see cref="ActorMeshLibrary.FadeOf"/>: surfaces drawn
/// SRCALPHA/INVSRCALPHA, no alpha test, no depth write, as retail draws a part with a translucency material); the part is an instance of that batch while its translucency is
/// strictly between 0 and 1 and of the opaque one otherwise, in exactly one of the two in every frame. Offline the character is never translucent.</para>
/// <para>Main thread only.</para>
/// </remarks>
public sealed partial class PlayerView : Node3D
{
    private readonly GameServices _services;
    private readonly WorldRoot _world;
    private readonly InteriorController? _interiors;
    private readonly ObjectLightTable _lightTable;
    private readonly PlayerCellLights _cellLights;
    private readonly List<DynamicObjectBatch> _batches = [];
    private readonly List<DynamicObjectBatch> _fadeBatches = [];

    /// <summary>Blended twins built per frame after the character's meshes (a part's mesh costs about a quarter of a millisecond; the arrival's frame builds the meshes themselves).</summary>
    private const int FadeBuildsPerFrame = 4;
    private readonly HashSet<string> _keep = [];
    private ActorMeshLibrary? _outdoor;
    private ActorMeshLibrary? _indoor;
    private uint[] _gfx = [];
    private int _appearanceVersion = -1;
    private bool _meshesForCell;
    private bool _haveMeshes;
    private int _lightRow = -1;
    private bool _translucencyLogged;
    private PlayerAvatar? _lastPlayer;
    private float _lastHeight;
    private float _lastRadius;
    private DrawInterpolation.Offset _lastOffset = DrawInterpolation.Offset.None;

    public PlayerView(GameServices services, WorldRoot world, InteriorController? interiors, ObjectLightTable lightTable, PlayerCellLights cellLights)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(lightTable);
        ArgumentNullException.ThrowIfNull(cellLights);
        _services = services;
        _world = world;
        _interiors = interiors;
        _lightTable = lightTable;
        _cellLights = cellLights;
        Name = "PlayerView";
    }

    /// <summary>Parts drawn this frame (diagnostics).</summary>
    public int DrawnParts { get; private set; }

    /// <summary>
    /// Parts of this frame drawn through the blended twin batch (<c>PartFadeNN</c>: a part whose translucency is strictly between 0 and 1). The fade checks require it above 0 on fading
    /// frames: <c>AvatarDraw.Translucency</c> alone says what was asked, not that the twin was drawn (S4).
    /// </summary>
    public int TwinParts { get; private set; }

    /// <summary>Parts of this frame that wanted the blended twin and were drawn solid in the opaque batch because the mesh had none (<c>TODO(g5-translucency-twin)</c>); 0 when the fade is drawn.</summary>
    public int SolidFadingParts { get; private set; }

    /// <summary>True once any frame drew a fading part solid for want of its twin (<c>TODO(g5-translucency-twin)</c> was logged); the fade checks fail on it.</summary>
    public bool TwinMissing => _translucencyLogged;

    /// <summary>The batches (one per part), diagnostics and checks.</summary>
    public IReadOnlyList<DynamicObjectBatch> Batches => _batches;

    /// <summary>The character is drawn in its cell rather than outdoors (diagnostics).</summary>
    public bool InCell => _haveMeshes && _meshesForCell;

    /// <summary>The player's row in the light table (-1 when it has none), diagnostics.</summary>
    public int LightRow => _lightRow;

    /// <summary>Lights in the player's light set this frame (diagnostics).</summary>
    public int LightCount { get; private set; }

    /// <summary>Distinct mesh variants held by the two libraries (diagnostics).</summary>
    public int MeshCount => (_outdoor?.Count ?? 0) + (_indoor?.Count ?? 0);

    /// <summary>
    /// Builds both libraries' meshes for the character's parts now, so their shader variants compile before the first frame. Call once
    /// with a frame in which the character is drawn.
    /// </summary>
    public void Prepare(AvatarFrame frame, PlayerAvatar player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!TryFindPlayer(frame, out var draw))
        {
            Log.Warn("play", "the player has no draw to prepare; its meshes are built on the first frame it is drawn");
            return;
        }

        var parts = frame.Parts.Slice(draw.PartStart, draw.PartCount);
        var other = !draw.InEnvCell;
        BuildMeshes(parts, player, other, assign: false);
        BuildMeshes(parts, player, draw.InEnvCell, assign: true);
        _appearanceVersion = draw.AppearanceVersion;
        _meshesForCell = draw.InEnvCell;
        _haveMeshes = true;
    }

    /// <summary>Draws the character of <paramref name="frame"/> (the avatars' output of this frame).</summary>
    /// <param name="frame">The avatars' draw output.</param>
    /// <param name="player">The player's avatar.</param>
    /// <param name="height">The character's height in metres (for the light sphere).</param>
    /// <param name="radius">The character's radius in metres.</param>
    /// <param name="offset">D26: the move from the physics step's body frame to the interpolated one it is drawn at (none for retail's stepping).</param>
    public void Update(AvatarFrame frame, PlayerAvatar player, float height, float radius, in DrawInterpolation.Offset offset)
    {
        (_meshesForCell ? _indoor : _outdoor)?.PumpFades(FadeBuildsPerFrame); // the blended twins of the character's parts, a few a frame after the meshes
        _lastPlayer = player;
        _lastHeight = height;
        _lastRadius = radius;
        _lastOffset = offset;
        if (!TryFindPlayer(frame, out var draw) || !draw.Visible || draw.Translucency >= 1f)
        {
            HideAll(); // fully translucent (translucency 1) is invisible, as ServerObjectsLayer treats it (wave 3, dod-6-7-2; the fade itself is drawn by the twin batches below)
            return;
        }

        var inCell = draw.InEnvCell;
        if (inCell && (_interiors is null || !_interiors.IsCellShown(draw.CellId)))
        {
            HideAll();
            return;
        }

        var parts = frame.Parts.Slice(draw.PartStart, draw.PartCount);
        if (!_haveMeshes || draw.AppearanceVersion != _appearanceVersion || inCell != _meshesForCell || !SameParts(parts))
        {
            BuildMeshes(parts, player, inCell, assign: true);
            _appearanceVersion = draw.AppearanceVersion;
            _meshesForCell = inCell;
            _haveMeshes = true;
        }

        var slot = inCell ? _interiors!.SlotOf(draw.CellId) : 0;
        var lightRow = inCell ? WriteLights(draw, height, radius) : ReleaseLights();
        var layers = inCell ? InteriorController.CellsLayerMask : 1u;
        var translucency = frame.PartTranslucency.Slice(draw.PartStart, draw.PartCount);
        var anchor = AcCoordinates.LandblockOrigin(LandblockCoord.FromId(draw.AnchorCellId));
        var drawn = 0;
        var twinParts = 0;
        var solidFading = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var batch = _batches[i];
            var fadeBatch = _fadeBatches[i];
            batch.Layers = layers;
            fadeBatch.Layers = layers;
            batch.Begin(1);
            fadeBatch.Begin(1);
            var part = parts[i];
            if (part.GfxObjId != 0 && translucency[i] < 1f && batch.Mesh is { } mesh)
            {
                var drawFrame = offset.Apply(part.Frame, draw.Frame.Origin);
                var transform = AcToGodot.Transform(AcVector3.Zero, drawFrame.Orientation, part.Scale);
                transform.Origin = _world.ToRender(anchor + drawFrame.Origin);

                // The materialise fade (D45, D63): while 0 < translucency < 1 the part is the instance of the mesh's blended twin (surfaces drawn SRCALPHA/INVSRCALPHA without a
                // depth write, as retail draws a part with a translucency material), at 0 the opaque mesh's. Both batches are begun and ended in this frame, so the part is in exactly
                // one of them in every frame: no gap frame and no double draw at the hand-over.
                var target = batch;
                if (Lifestone.Client.Effects.PartTranslucencyRules.HasPartMaterial(translucency[i]))
                {
                    fadeBatch.Mesh = (inCell ? _indoor : _outdoor)?.FadeOf(mesh);
                    if (fadeBatch.Mesh is not null)
                    {
                        target = fadeBatch;
                        twinParts++;
                    }
                    else
                    {
                        solidFading++;
                    }

                    if (fadeBatch.Mesh is null && !_translucencyLogged)
                    {
                        _translucencyLogged = true;
                        Log.Warn("play", "TODO(g5-translucency-twin): a translucent character part has no blended twin mesh, so it is drawn solid", ("part", part.PartIndex), ("translucency", translucency[i]));
                    }
                }

                target.Add(transform, new Vector4(slot, lightRow + 1, 0f, translucency[i]), mesh.GetAabb());
                drawn++;
            }

            batch.End();
            fadeBatch.End();
        }

        DrawnParts = drawn;
        TwinParts = twinParts;
        SolidFadingParts = solidFading;
    }

    /// <summary>Draws the last frame again (the floating origin moved: render transforms come from exact positions).</summary>
    public void Redraw(AvatarFrame frame)
    {
        if (_lastPlayer is { } player)
        {
            Update(frame, player, _lastHeight, _lastRadius, _lastOffset);
        }
    }

    /// <summary>Stops drawing the character (it is not in the world, or its cell is not shown).</summary>
    public void HideAll()
    {
        foreach (var batch in _batches)
        {
            batch.Hide();
        }

        foreach (var batch in _fadeBatches)
        {
            batch.Hide();
        }

        DrawnParts = 0;
        TwinParts = 0;
        SolidFadingParts = 0;
        LightCount = 0;
    }

    private static bool TryFindPlayer(AvatarFrame frame, out AvatarDraw draw)
    {
        foreach (var candidate in frame.Avatars)
        {
            if (candidate.IsPlayer)
            {
                draw = candidate;
                return true;
            }
        }

        draw = default;
        return false;
    }

    private bool SameParts(ReadOnlySpan<Lifestone.Client.Effects.PartDraw> parts)
    {
        if (parts.Length != _gfx.Length)
        {
            return false;
        }

        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].GfxObjId != _gfx[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Makes the meshes of the parts for one lighting rule (the character as its ObjDesc dresses it), and, with
    /// <paramref name="assign"/>, gives them to the batches and drops the variants of that library the character no longer wears.
    /// </summary>
    private void BuildMeshes(ReadOnlySpan<Lifestone.Client.Effects.PartDraw> parts, PlayerAvatar player, bool inCell, bool assign)
    {
        var library = inCell
            ? _indoor ??= new ActorMeshLibrary(_services, SurfaceLighting.DynamicCellObject, fadeTwins: true)
            : _outdoor ??= new ActorMeshLibrary(_services, SurfaceLighting.DynamicObject, fadeTwins: true);
        var appearances = player.Body.Appearance.Parts;
        while (_batches.Count < parts.Length)
        {
            _batches.Add(new DynamicObjectBatch(this, $"Part{_batches.Count:D2}", 1u));
            _fadeBatches.Add(new DynamicObjectBatch(this, $"PartFade{_fadeBatches.Count:D2}", 1u));
        }

        if (assign)
        {
            _gfx = new uint[parts.Length];
        }

        _keep.Clear();
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            ArrayMesh? mesh = null;
            if (part.GfxObjId != 0)
            {
                var appearance = appearances[part.PartIndex];
                _keep.Add(library.VariantKey(part.GfxObjId, appearance));
                mesh = library.Get(part.GfxObjId, appearance);
            }

            if (assign)
            {
                _gfx[i] = part.GfxObjId;
                _batches[i].Mesh = mesh;
            }
        }

        if (assign)
        {
            library.ReleaseVariantsExcept(_keep);
        }
    }

    /// <summary>
    /// Selects the cell lights that reach the character, converts them to Godot world space and writes the character's row of the
    /// light table. Returns the row (-1 when the table has none left).
    /// </summary>
    private int WriteLights(AvatarDraw draw, float height, float radius)
    {
        if (_lightRow < 0)
        {
            _lightRow = _lightTable.Allocate();
            if (_lightRow < 0)
            {
                return -1;
            }
        }

        var cellBlock = AcCoordinates.LandblockOrigin(LandblockCoord.FromId(draw.CellId));
        var anchorBlock = AcCoordinates.LandblockOrigin(LandblockCoord.FromId(draw.AnchorCellId));
        var anchorToCell = new AcVector3((float)(anchorBlock.X - cellBlock.X), (float)(anchorBlock.Y - cellBlock.Y), 0f);
        var center = draw.Frame.Origin + anchorToCell + new AcVector3(0f, 0f, height * 0.5f);
        Span<ObjectLight> set = stackalloc ObjectLight[ObjectLightSets.MaxLights];
        var count = ObjectLightSets.Select(center, Math.Max(height * 0.5f, radius), _cellLights.Get(draw.CellId), set);
        for (var i = 0; i < count; i++)
        {
            var world = _world.ToRender(cellBlock + set[i].Position);
            set[i] = set[i] with { Position = new AcVector3(world.X, world.Y, world.Z) };
        }

        _lightTable.Write(_lightRow, set, count);
        LightCount = count;
        return _lightRow;
    }

    private int ReleaseLights()
    {
        if (_lightRow >= 0)
        {
            _lightTable.Release(_lightRow);
            _lightRow = -1;
        }

        LightCount = 0;
        return -1;
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        ReleaseLights();
        foreach (var batch in _batches)
        {
            batch.Dispose();
        }

        foreach (var batch in _fadeBatches)
        {
            batch.Dispose();
        }

        _batches.Clear();
        _fadeBatches.Clear();
        _outdoor?.Dispose();
        _indoor?.Dispose();
    }
}
