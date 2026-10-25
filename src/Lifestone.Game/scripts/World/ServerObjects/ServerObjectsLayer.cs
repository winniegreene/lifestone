using System.Diagnostics;
using Godot;
using Lifestone.Client.Avatars;
using Lifestone.Client.Effects;
using Lifestone.Client.Interiors;
using Lifestone.Client.Play;
using Lifestone.Client.Rendering;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;
using Lifestone.Game.Core;
using Lifestone.Game.Player;
using Lifestone.Game.World.Interiors;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.World.ServerObjects;

/// <summary>
/// W6: draws every object the server creates (the avatar set's output, <see cref="AvatarFrame"/>), other than the player's own character (which
/// <c>PlayerView</c> draws, interpolated with the physics steps), as the ObjDesc dresses it: a part is a mesh variant (its GfxObj with the texture swaps and the
/// palette of its dress, <see cref="ServerObjectMeshes"/>) and every part that shares a variant and a visual layer is an instance of one
/// <see cref="DynamicObjectBatch"/> (a MultiMesh, <c>INSTANCE_CUSTOM</c> carrying the cell slot, the light-set row and the translucency, so objects are lit like every
/// object that moves: <see cref="SurfaceLighting.DynamicObject"/> outdoors, <see cref="SurfaceLighting.DynamicCellObject"/> in a cell with the cell's lights, D46).
/// Process priority <see cref="Priority"/>: after the world, so it draws the frame the player's controller advanced.
/// </summary>
/// <remarks>
/// <para><b>Who advances the set.</b> Not this node: the player's controller advances the avatar set every frame (the player's body first, then the other objects,
/// docs/avatars.md section 12) and this layer draws the frame that produced. Play mode switches the layer on with the player (<see cref="Active"/>) and off when
/// the player leaves.</para>
/// <para><b>An object is shown only when fully attached.</b> Its meshes are prepared on the workers and attached by the main-thread queue within its frame budget
/// (<see cref="ServerObjectMeshes"/>); until every part of an object has answered the object is not drawn at all (<see cref="Waiting"/> counts them), so a person never
/// appears as a few limbs. Once shown, an object whose dress changes keeps drawing the part meshes it had until the new ones arrive.</para>
/// <para><b>MultiMesh or nodes, and why.</b> The per-instance values (<c>INSTANCE_CUSTOM</c>: slot, light row, translucency) are only a MultiMesh instance's; Godot's
/// per-node instance uniforms would each take a block of the shared shader-uniform buffer (<c>SurfaceLightingCode</c> remarks). The Academy's counts alone (a few dozen
/// objects of up to 17 parts) would do per node, but the shaders and the batch are the real ones, and Holtburg's crowd, with the same dress on many bodies, shares
/// batches. A batch is uploaded only on a frame in which one of its instances changed (a hash of its instances is compared), so idle people cost no upload.</para>
/// <para><b>The materialise fade (D45, D63; closes <c>TODO(g5-translucency)</c> for the fade).</b> A part whose translucency is strictly between 0 and 1 is an instance of its mesh's
/// blended twin (<see cref="ServerObjectMeshes.FadeOf"/>: every opaque or alpha-tested surface blended without a depth write, <c>PartTranslucencyRules.WithPartMaterial</c>), which
/// honours the per-instance alpha; at 0 it goes back to the opaque mesh's batch. The twin is built with the mesh and its pipelines are warmed at start-up
/// (<see cref="ServerObjectsWarmUp"/>), so a fade compiles nothing. A fading instance changes every frame, so its batch uploads every frame of the 0.75 s.</para>
/// <para><b>Placeholders and gaps, logged:</b> particles are <see cref="ServerObjectParticles"/>'s. Main thread only.</para>
/// </remarks>
public sealed partial class ServerObjectsLayer : Node3D
{
    /// <summary>The process priority: after the world (10), before the portal presenter (15).</summary>
    public const int Priority = 12;

    private const long SweepEveryFrames = 120;
    private const long SlotIdleFrames = 90;
    private const long StateIdleFrames = 3;

    private readonly GameServices _services;
    private readonly WorldRoot _world;
    private readonly InteriorController? _interiors;
    private readonly ObjectLightTable _lights;
    private readonly ServerObjectCellLights _cellLights;
    private readonly ServerObjectMeshes _outdoor;
    private readonly ServerObjectMeshes _indoor;
    private ServerObjectParticles? _particles;
    private readonly Dictionary<uint, AvatarState> _states = new();
    private readonly Dictionary<SlotKey, Slot> _slots = new();
    private readonly List<uint> _stale = [];
    private readonly List<SlotKey> _staleSlots = [];
    private long _frameNo;
    private long _originEpoch;
    private bool _translucencyLogged;
    private int _drawn;
    private int _drawnInCell;
    private int _waiting;
    private int _drawnParts;
    private int _twinParts;
    private int _solidFadingParts;
    private bool _wasActive;
    private ProjectileWarmUp? _projectileWarm;

    public ServerObjectsLayer(GameServices services, WorldRoot world, InteriorController? interiors, ObjectLightTable lights)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(lights);
        _services = services;
        _world = world;
        _interiors = interiors;
        _lights = lights;
        _cellLights = new ServerObjectCellLights(services);
        _outdoor = new ServerObjectMeshes(services, SurfaceLighting.DynamicObject);
        _indoor = new ServerObjectMeshes(services, SurfaceLighting.DynamicCellObject);
        Name = "ServerObjectsLayer";
        ProcessPriority = Priority;
        _projectileWarm = new ProjectileWarmUp(services, _outdoor, _indoor); // M3 3.1 fc: the war spells' bolts are prepared on workers from the first frame (Poll hands the worker's plan to the meshes)
        _projectileWarm.Start();
        world.OriginShifted += (_, _, _) => _originEpoch++;
    }

    /// <summary>The avatar set drawn, or null. Replacing it forgets what was shown (a new session's world).</summary>
    public AvatarSet? Avatars
    {
        get => _avatars;
        set
        {
            if (!ReferenceEquals(_avatars, value))
            {
                _avatars = value;
                if (value is not null && _interiors is not null)
                {
                    value.CellFollower ??= new ProjectileCellFollower(_interiors.Registry); // a flying bolt changes cell as it crosses cell boundaries (M3 4.2 x2: closes TODO(m3-bolt-cell-tracking))
                }

                ForgetStates();
            }
        }
    }

    private AvatarSet? _avatars;

    /// <summary>
    /// The player's controller, whose interpolated draw frame the player's held items follow (M3 4.1 fix A, R6; closes TODO(g5-held-item-interpolation)). The player is drawn by <c>PlayerView</c> at the
    /// interpolated frame (D26), the other objects at the avatar set's; a wand in the hand uses the set's frame for the parent's parts, so without this it lags the body by up to a physics step.
    /// </summary>
    public PlayerController? PlayerSource { get; set; }

    private uint _playerGuid;
    private AcVector3 _playerBodyOrigin;
    private uint _playerAnchorCell;
    private DrawInterpolation.Offset _playerOffset = DrawInterpolation.Offset.None;

    /// <summary>True while the layer draws (the player is in the world and its controller advances the set).</summary>
    public bool Active { get; set; }

    /// <summary>Objects drawn this frame (diagnostics and checks).</summary>
    public int DrawnObjects => _drawn;

    /// <summary>Objects drawn in interior cells this frame: the avatars that hold a light-table row (M5 0 c1, the login check's <c>drawn_in_cell=</c>; the soak line's <c>in_cell</c> is a different count, every avatar whose body is in a cell, drawn or not).</summary>
    public int DrawnInCell => _drawnInCell;

    /// <summary>The object light table's rows in use (<see cref="ObjectLightTable.InUse"/>; the login check's <c>light_rows=</c>): a row is given back by the sweep once its avatar is no longer drawn, so the count falls with the avatar set (a culled or deleted object).</summary>
    public int LightRowsInUse => _lights.InUse;

    /// <summary>The particles of the objects (diagnostics and checks), or null before the first active frame.</summary>
    public ServerObjectParticles? Particles => _particles;

    /// <summary>Objects not drawn yet because a part's mesh is still being prepared.</summary>
    public int Waiting => _waiting;

    /// <summary>Most objects that waited at once.</summary>
    public int MaxWaiting { get; private set; }

    /// <summary>Parts drawn this frame.</summary>
    public int DrawnParts => _drawnParts;

    /// <summary>Parts drawn this frame through a blended twin mesh (a part whose translucency is strictly between 0 and 1); the fade checks require it above 0 on fading frames (S4).</summary>
    public int TwinParts => _twinParts;

    /// <summary>Parts of this frame that wanted a blended twin and were drawn solid because the mesh had none (<c>TODO(g5-translucency-twin)</c>); 0 when the fade is drawn.</summary>
    public int SolidFadingParts => _solidFadingParts;

    /// <summary>True once any frame drew a fading part solid for want of its twin (<c>TODO(g5-translucency-twin)</c> was logged); the fade checks fail on it.</summary>
    public bool TwinMissing => _translucencyLogged;

    /// <summary>The projectile warm-up (M3 3.1 lane fc), or null once it finished and was released.</summary>
    public ProjectileWarmUp? ProjectileWarm => _projectileWarm;

    /// <summary>Projectiles shown over the run (every part mesh attached), counted once each.</summary>
    public long ProjectilesShown { get; private set; }

    /// <summary>The most frames any projectile waited for its part meshes before it was first shown (0: it drew on the frame its create was applied).</summary>
    public int MaxProjectileWaitFrames { get; private set; }

    /// <summary>Frames the last projectile shown waited before its first draw.</summary>
    public int LastProjectileWaitFrames { get; private set; }

    /// <summary>Batches that exist (one per mesh variant and layer in use).</summary>
    public int BatchCount => _slots.Count;

    /// <summary>Batch uploads in the last frame.</summary>
    public int UploadsLastFrame { get; private set; }

    /// <summary>Batch uploads over the run.</summary>
    public long Uploads { get; private set; }

    /// <summary>Objects that have been shown (every part attached) over the run.</summary>
    public long Shown { get; private set; }

    /// <summary>The last frame's cost in this node, in milliseconds.</summary>
    public double LastMilliseconds { get; private set; }

    /// <summary>The largest frame cost in this node so far, in milliseconds.</summary>
    public double MaxMilliseconds { get; private set; }

    /// <summary>The meshes (diagnostics): outdoor and indoor lighting rules.</summary>
    public (ServerObjectMeshes Outdoor, ServerObjectMeshes Indoor) Meshes => (_outdoor, _indoor);

    /// <summary>True when the object <paramref name="guid"/> was drawn in the last frame.</summary>
    public bool IsDrawn(uint guid) => _states.TryGetValue(guid, out var state) && state.DrawnFrame == _frameNo;

    /// <summary>The objects drawn in the last frame, by guid (checks).</summary>
    public IEnumerable<uint> DrawnGuids => _states.Where(s => s.Value.DrawnFrame == _frameNo).Select(s => s.Key);

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        var started = Stopwatch.GetTimestamp();
        _frameNo++;
        _outdoor.BeginFrame();
        _indoor.BeginFrame();
        _projectileWarm?.Poll();
        UploadsLastFrame = 0;
        _drawn = 0;
        _drawnInCell = 0;
        _waiting = 0;
        _drawnParts = 0;
        _twinParts = 0;
        _solidFadingParts = 0;
        if (!Active || _avatars is not { } avatars)
        {
            if (_wasActive)
            {
                HideAll();
                _wasActive = false;
            }

            return;
        }

        _wasActive = true;
        foreach (var slot in _slots.Values)
        {
            slot.Instances.Clear();
            slot.Hash = 14695981039346656037UL;
        }

        var frame = avatars.Frame;
        _playerGuid = 0;
        _playerOffset = DrawInterpolation.Offset.None;
        foreach (var draw in frame.Avatars)
        {
            if (draw.IsPlayer)
            {
                // The held items (children of the player) are moved with the body by the same offset the player's own parts get.
                _playerGuid = draw.Guid;
                _playerBodyOrigin = draw.Frame.Origin;
                _playerAnchorCell = draw.AnchorCellId;
                if (PlayerSource is { } source && IsInstanceValid(source))
                {
                    _playerOffset = source.DrawOffset;
                }

                break;
            }
        }

        foreach (var draw in frame.Avatars)
        {
            if (!draw.IsPlayer)
            {
                DrawObject(avatars, frame, draw);
            }
        }

        Upload();
        if (GetViewport().GetCamera3D() is { } camera)
        {
            _particles ??= new ServerObjectParticles(this, _services, _world, _interiors);
            _particles.Update(avatars, camera.GlobalPosition);
        }

        MaxWaiting = Math.Max(MaxWaiting, _waiting);
        if (_frameNo % SweepEveryFrames == 0)
        {
            Sweep();
        }

        LastMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        MaxMilliseconds = Math.Max(MaxMilliseconds, LastMilliseconds);
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        ForgetStates();
        foreach (var slot in _slots.Values)
        {
            slot.Batch.Dispose();
        }

        _slots.Clear();
        _particles?.Dispose();
        _outdoor.Dispose();
        _indoor.Dispose();
    }

    private void DrawObject(AvatarSet avatars, AvatarFrame frame, in AvatarDraw draw)
    {
        if (!avatars.TryGet(draw.Guid, out var avatar))
        {
            return;
        }

        var state = StateOf(draw.Guid, draw.PartCount);
        state.SeenFrame = _frameNo;
        if (!draw.Visible || draw.Translucency >= 1f)
        {
            if (avatar.Body.IsHidden && (!draw.InEnvCell || (_interiors is not null && _interiors.IsCellShown(draw.CellId))))
            {
                Prefetch(draw, frame, avatar); // the server unhides an object it created hidden with a fade (D63): its meshes are ready by then, so the fade is not spent waiting for them
            }

            ReleaseLights(state);
            return;
        }

        var inCell = draw.InEnvCell;
        if (inCell && (_interiors is null || !_interiors.IsCellShown(draw.CellId)))
        {
            ReleaseLights(state); // an object in a cell that is not shown holds no row of the 256-row light table (dod-6-7-7: the row leaked until the object left)
            return;
        }

        var meshes = inCell ? _indoor : _outdoor;
        var parts = frame.Parts.Slice(draw.PartStart, draw.PartCount);
        var appearances = avatar.Body.Appearance.Parts;
        var complete = true;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.GfxObjId == 0)
            {
                state.Current[i] = null;
                continue;
            }

            if (meshes.TryGet(part.GfxObjId, appearances[part.PartIndex], out var mesh))
            {
                state.Current[i] = mesh;
                if (mesh is not null)
                {
                    state.Last[i] = mesh;
                }
            }
            else
            {
                state.Current[i] = null;
                complete = false;
            }
        }

        if (!state.Shown)
        {
            if (!complete)
            {
                _waiting++;
                if (avatar.Body.IsProjectile)
                {
                    state.WaitFrames++;
                }

                return;
            }

            state.Shown = true;
            Shown++;
            if (avatar.Body.IsProjectile)
            {
                ProjectilesShown++;
                LastProjectileWaitFrames = state.WaitFrames;
                MaxProjectileWaitFrames = Math.Max(MaxProjectileWaitFrames, state.WaitFrames);
                if (ProjectilesShown == 1)
                {
                    // Once, on the transition (package 2 review, finding 12): a line per bolt built a params array, boxed fields and a formatted guid in the frame cast_frame_max_ms and fight_frame_max_ms measure.
                    // The bolts after the first are counted only (ProjectilesShown, LastProjectileWaitFrames, MaxProjectileWaitFrames: the fight leg and the play-checks report them).
                    Log.Info("serverobjects", "first projectile shown", ("guid", $"0x{draw.Guid:X8}"), ("waited_frames", state.WaitFrames), ("warm", _projectileWarm is { Done: true }), ("parts", parts.Length));
                }
            }
        }

        var slot = inCell ? _interiors!.SlotOf(draw.CellId) : 0;
        var lightRow = inCell ? UpdateLights(state, draw, avatar.Body.Height, avatar.Body.Radius) : ReleaseLights(state);
        var layers = inCell ? InteriorController.CellsLayerMask : 1u;
        var translucency = frame.PartTranslucency.Slice(draw.PartStart, draw.PartCount);
        var anchor = AcCoordinates.LandblockOrigin(LandblockCoord.FromId(draw.AnchorCellId));
        var heldByPlayer = _playerGuid != 0 && !_playerOffset.IsNone && avatar.Body.Parent is { } holder && holder.Guid == _playerGuid && draw.AnchorCellId == _playerAnchorCell;
        var any = false;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.GfxObjId == 0 || translucency[i] >= 1f)
            {
                continue;
            }

            var mesh = state.Current[i];
            if (mesh is null && state.Last[i] is { } last && GodotObject.IsInstanceValid(last))
            {
                mesh = last;
            }

            if (mesh is null)
            {
                continue;
            }

            var partFrame = part.Frame;
            if (heldByPlayer)
            {
                partFrame = _playerOffset.Apply(partFrame, _playerBodyOrigin);
            }

            var transform = AcToGodot.Transform(AcVector3.Zero, partFrame.Orientation, part.Scale);
            transform.Origin = _world.ToRender(anchor + partFrame.Origin);
            // The materialise fade (D45, D63): while 0 < translucency < 1 the part is an instance of its mesh's blended twin (the surfaces drawn SRCALPHA/INVSRCALPHA without a depth
            // write, as retail draws a part with a translucency material), at 0 it is an instance of the opaque mesh again. Every slot is refilled each frame and uploaded in
            // Upload below, so the hand-over is within one frame: the part is in exactly one of the two batches in every frame (no gap, no double draw).
            var drawMesh = mesh;
            if (PartTranslucencyRules.HasPartMaterial(translucency[i]))
            {
                if (FadeOf(mesh) is { } fade)
                {
                    drawMesh = fade;
                    _twinParts++;
                }
                else
                {
                    _solidFadingParts++;
                }

                if (drawMesh == mesh && !_translucencyLogged)
                {
                    _translucencyLogged = true;
                    Log.Warn("serverobjects", "TODO(g5-translucency-twin): a translucent object part has no blended twin mesh (it was built without one), so it is drawn solid", ("guid", $"0x{draw.Guid:X8}"), ("translucency", translucency[i]));
                }
            }

            SlotFor(drawMesh, layers).Add(transform, new Vector4(slot, lightRow + 1, 0f, translucency[i]), mesh.GetAabb());
            _drawnParts++;
            any = true;
        }

        // A projectile counts as shown (AvatarStats.ProjectilesShown, 3.1-R6: not "drawn") when it is shown, whether or not a part has a mesh to put in a batch: the war spells' bolts have a Setup part whose GfxObj degrades to nothing (the Flame Bolt's 0x010001EC
        // has no level that draws), and what the player sees of them is the particles of their scripts, which ServerObjectParticles hands to the particle renderer for any shown avatar with emitters.
        if (any || avatar.Body.IsProjectile)
        {
            _drawn++;
            _drawnInCell += inCell ? 1 : 0;
            state.DrawnFrame = _frameNo;
            avatars.NoteDrawn(avatar); // a projectile's first shown frame counts (AvatarStats.ProjectilesShown)
        }
    }

    /// <summary>Asks for the meshes of a hidden object's parts (the workers prepare them in the background); draws nothing.</summary>
    private void Prefetch(in AvatarDraw draw, AvatarFrame frame, Avatar avatar)
    {
        var meshes = draw.InEnvCell ? _indoor : _outdoor;
        var parts = frame.Parts.Slice(draw.PartStart, draw.PartCount);
        var appearances = avatar.Body.Appearance.Parts;
        for (var i = 0; i < parts.Length; i++)
        {
            // A hidden object's frame carries no GfxObj (its pose is not drawn: GfxObjId 0), so the part's own level-0 GfxObj stands in: the one the first fade frame asks for at close range. Prefetching only the
            // frame's ids prefetched nothing, and the fade spent its first frames waiting for the meshes (m4-1-2 fade-R1: the twin was drawn two to three frames late).
            var appearance = appearances[parts[i].PartIndex];
            var gfxObjId = parts[i].GfxObjId != 0 ? parts[i].GfxObjId : appearance.Chain.NearestGfxObjId;
            if (gfxObjId != 0)
            {
                meshes.TryGet(gfxObjId, appearance, out _);
            }
        }
    }

    /// <summary>The blended twin of a part mesh, from the set that built it (the outdoor or the indoor one).</summary>
    private ArrayMesh? FadeOf(ArrayMesh mesh) => _outdoor.FadeOf(mesh) ?? _indoor.FadeOf(mesh);

    private Slot SlotFor(ArrayMesh mesh, uint layers)
    {
        var key = new SlotKey(mesh.GetInstanceId(), layers);
        if (!_slots.TryGetValue(key, out var slot))
        {
            slot = new Slot(new DynamicObjectBatch(this, $"Batch{_slots.Count:D3}", layers)) { Mesh = mesh };
            slot.Batch.Mesh = mesh;
            _slots.Add(key, slot);
        }

        slot.UsedFrame = _frameNo;
        return slot;
    }

    private void Upload()
    {
        foreach (var slot in _slots.Values)
        {
            if (slot.Instances.Count == 0)
            {
                if (slot.Batch.Drawn)
                {
                    slot.Batch.Hide();
                }

                slot.UploadedHash = 0;
                continue;
            }

            if (slot.UploadedHash == slot.Hash && slot.UploadedCount == slot.Instances.Count && slot.Batch.Drawn)
            {
                continue;
            }

            slot.Batch.Begin(slot.Instances.Count);
            foreach (var instance in slot.Instances)
            {
                slot.Batch.Add(instance.Transform, instance.Custom, instance.Bounds);
            }

            slot.Batch.End();
            slot.UploadedHash = slot.Hash;
            slot.UploadedCount = slot.Instances.Count;
            UploadsLastFrame++;
            Uploads++;
        }
    }

    /// <summary>The object's light-set row: the cell's lights that reach it, written when they, the object or the render origin changed.</summary>
    private static readonly CellLight[] NoStaticLights = [];

    private int UpdateLights(AvatarState state, in AvatarDraw draw, float height, float radius)
    {
        if (state.LightRow < 0)
        {
            state.LightRow = _lights.Allocate();
            state.LightsValid = false;
            if (state.LightRow < 0)
            {
                return -1;
            }
        }

        var lights = _cellLights.TryGet(draw.CellId);
        var origin = draw.Frame.Origin;
        var moved = (origin - state.LightOrigin).LengthSquared() > 0.0025f;
        var dynamic = _avatars?.DynamicLightFrame;
        var dynamicNow = dynamic is { Count: > 0 };
        // The dynamic lights (a bolt in flight, the viewer light) move every frame, so a row that holds one is rewritten every frame (the write skips an unchanged row); a row of static lights only is rewritten when they or the object changed.
        if (state.LightsValid && !moved && !dynamicNow && !state.LightDynamic && state.LightEpoch == _originEpoch && state.LightVersion == _cellLights.Version && state.LightCell == draw.CellId)
        {
            return state.LightRow;
        }

        if (lights is null && !dynamicNow)
        {
            return state.LightRow; // the worker is still reading the cell; the object shows its ambient and the row is written when the lights land
        }

        var cellBlock = AcCoordinates.LandblockOrigin(LandblockCoord.FromId(draw.CellId));
        var anchorBlock = AcCoordinates.LandblockOrigin(LandblockCoord.FromId(draw.AnchorCellId));
        var anchorToCell = new AcVector3((float)(anchorBlock.X - cellBlock.X), (float)(anchorBlock.Y - cellBlock.Y), 0f);
        var center = origin + anchorToCell + new AcVector3(0f, 0f, height * 0.5f);
        Span<ObjectLight> set = stackalloc ObjectLight[ObjectLightSets.MaxLights];
        var sphere = Math.Max(height * 0.5f, radius);
        var count = dynamicNow
            ? ObjectLightSets.Select(center, sphere, draw.CellId, lights ?? NoStaticLights, dynamic!, set) // the dynamic lights first (Render::minimize_object_lighting 11.6096 0x0054E090), then the static ones, 8 at most
            : ObjectLightSets.Select(center, sphere, lights!, set);
        for (var i = 0; i < count; i++)
        {
            var world = _world.ToRender(cellBlock + set[i].Position);
            set[i] = set[i] with { Position = new AcVector3(world.X, world.Y, world.Z) };
        }

        _lights.Write(state.LightRow, set, count);
        state.LightsValid = lights is not null; // a row written from the dynamic lights alone is written again when the cell's lights land
        state.LightDynamic = dynamicNow;
        state.LightOrigin = origin;
        state.LightEpoch = _originEpoch;
        state.LightVersion = _cellLights.Version;
        state.LightCell = draw.CellId;
        return state.LightRow;
    }

    private int ReleaseLights(AvatarState state)
    {
        if (state.LightRow >= 0)
        {
            _lights.Release(state.LightRow);
            state.LightRow = -1;
            state.LightsValid = false;
        }

        return -1;
    }

    private AvatarState StateOf(uint guid, int partCount)
    {
        if (!_states.TryGetValue(guid, out var state) || state.Current.Length != partCount)
        {
            if (state is not null)
            {
                ReleaseLights(state);
            }

            state = new AvatarState(partCount);
            _states[guid] = state;
        }

        return state;
    }

    private void Sweep()
    {
        _outdoor.Sweep();
        _indoor.Sweep();
        _stale.Clear();
        foreach (var (guid, state) in _states)
        {
            if (_frameNo - state.SeenFrame > StateIdleFrames)
            {
                _stale.Add(guid);
            }
        }

        foreach (var guid in _stale)
        {
            ReleaseLights(_states[guid]);
            _states.Remove(guid);
        }

        _staleSlots.Clear();
        foreach (var (key, slot) in _slots)
        {
            if (_frameNo - slot.UsedFrame > SlotIdleFrames)
            {
                _staleSlots.Add(key);
            }
        }

        foreach (var key in _staleSlots)
        {
            _slots[key].Batch.Dispose();
            _slots.Remove(key);
        }
    }

    private void HideAll()
    {
        _particles?.HideAll();
        foreach (var slot in _slots.Values)
        {
            slot.Instances.Clear();
            slot.UploadedHash = 0;
            slot.Batch.Hide();
        }

        foreach (var state in _states.Values)
        {
            ReleaseLights(state);
        }
    }

    private void ForgetStates()
    {
        foreach (var state in _states.Values)
        {
            ReleaseLights(state);
        }

        _states.Clear();
        HideAll();
    }

    private readonly record struct SlotKey(ulong MeshId, uint Layers);

    private readonly record struct Instance(Transform3D Transform, Vector4 Custom, Aabb Bounds);

    private sealed class Slot(DynamicObjectBatch batch)
    {
        public DynamicObjectBatch Batch { get; } = batch;

        public ArrayMesh? Mesh { get; init; }

        public List<Instance> Instances { get; } = [];

        public ulong Hash = 14695981039346656037UL;

        public ulong UploadedHash;

        public int UploadedCount;

        public long UsedFrame;

        public void Add(in Transform3D transform, Vector4 custom, Aabb bounds)
        {
            Instances.Add(new Instance(transform, custom, bounds));
            var h = Hash;
            h = Mix(h, transform.Basis.X.X);
            h = Mix(h, transform.Basis.X.Y);
            h = Mix(h, transform.Basis.X.Z);
            h = Mix(h, transform.Basis.Y.X);
            h = Mix(h, transform.Basis.Y.Y);
            h = Mix(h, transform.Basis.Y.Z);
            h = Mix(h, transform.Basis.Z.X);
            h = Mix(h, transform.Basis.Z.Y);
            h = Mix(h, transform.Basis.Z.Z);
            h = Mix(h, transform.Origin.X);
            h = Mix(h, transform.Origin.Y);
            h = Mix(h, transform.Origin.Z);
            h = Mix(h, custom.X);
            h = Mix(h, custom.Y);
            h = Mix(h, custom.W);
            Hash = h;
        }

        private static ulong Mix(ulong hash, float value) => (hash ^ (uint)BitConverter.SingleToInt32Bits(value)) * 1099511628211UL;
    }

    private sealed class AvatarState(int partCount)
    {
        public ArrayMesh?[] Current { get; } = new ArrayMesh?[partCount];

        public ArrayMesh?[] Last { get; } = new ArrayMesh?[partCount];

        public bool Shown;
        public int WaitFrames;
        public long SeenFrame;
        public long DrawnFrame = -1;
        public int LightRow = -1;
        public bool LightsValid;
        public bool LightDynamic;
        public AcVector3 LightOrigin;
        public long LightEpoch;
        public long LightVersion;
        public uint LightCell;
    }
}
