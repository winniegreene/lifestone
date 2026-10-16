using System.Numerics;
using Lifestone.Client.PortalSpace;
using Lifestone.Data.Animation;
using Lifestone.Data.Appearance;
using Lifestone.Data.Effects;
using Lifestone.Data.Models;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.World;

namespace Lifestone.Client.Avatars;

/// <summary>Retail <c>PhysicsState</c> bits the avatars read (values as the net track's <c>PhysicsState</c>).</summary>
public static class AvatarStateBits
{
    /// <summary>Never moves; never becomes active.</summary>
    public const uint Static = 0x1;

    /// <summary>Reports collisions (cleared while hidden).</summary>
    public const uint ReportCollisions = 0x8;

    /// <summary>Ignores collisions (set while hidden).</summary>
    public const uint IgnoreCollisions = 0x10;

    /// <summary>Not drawn (<c>set_nodraw</c>).</summary>
    public const uint NoDraw = 0x20;

    /// <summary>Turns to face the direction it moves.</summary>
    public const uint AlignPath = 0x100;

    /// <summary>Falls under gravity.</summary>
    public const uint Gravity = 0x400;

    /// <summary>Its Setup's lights are on.</summary>
    public const uint LightingOn = 0x800;

    /// <summary>Hidden: not drawn, animation and physics frozen (<c>set_hidden</c>).</summary>
    public const uint Hidden = HiddenTransition.Hidden;

    /// <summary>Frozen: never updated.</summary>
    public const uint Frozen = 0x1000000;
}

/// <summary>What an avatar body needs from its set for one update: where the player is and the ground probe.</summary>
/// <param name="PlayerPosition">The player's position (retail <c>CPhysicsObj::player_object</c>), or null with no player.</param>
/// <param name="GroundProbe">The ground clamp for grounded objects (owner decision D24), or null for none.</param>
public readonly record struct AvatarStep(CellFrame? PlayerPosition, IGroundProbe? GroundProbe);

/// <summary>
/// One server object as retail's <c>CPhysicsObj</c> holds it, without collision: its part array (appearance and
/// animation), effects, motion interpreter with MoveTo (<see cref="MovementManager"/>), position, interpolation,
/// constraint and sticky managers (<see cref="PositionManager"/>), targets (<see cref="TargetManager"/>), children and
/// parent. Re-expressed from the 11.4186 decompile (PORTAL/cphysobj/CPhysicsObj.c, PORTAL/smartbox/SmartBox.c; names
/// and addresses cited, nothing copied); addresses are 11.4186 / 11.6096.
/// </summary>
/// <remarks>
/// <para><b>Stepping</b> (<c>update_object</c>, 0x00515D10 / 0x00516810): an object with a parent, outside a cell or
/// Frozen is inactive and not stepped; one more than 96 m from the player is inactive but still runs its particles and
/// scripts (<c>UpdateObjectInternal</c> runs those outside its active block); otherwise <see cref="ObjectClock"/> slices
/// the frame into quanta of at most 0.2 s and each quantum is <c>UpdateObjectInternal</c> (0x005156B0 / 0x005161B0):
/// <list type="number">
/// <item><c>UpdatePositionInternal</c> (0x00512C30 / 0x00513730): unless Hidden, the part array's animation step, whose
/// root motion is scaled by the object's scale on walkable ground and zeroed elsewhere; then
/// <see cref="PositionManager.AdjustOffset"/> (always, even Hidden); the offset is combined onto the position; unless
/// Hidden, <c>UpdatePhysicsInternal</c> (0x00510700 / 0x005111D0): velocity capped at 50 m/s, under 0.25 m/s zeroed,
/// acceleration, angular velocity; the animation hooks run.</item>
/// <item>An object whose Setup has no spheres (<c>CPartArray::GetNumSphere</c> 0) keeps its origin: only the new
/// rotation is taken, so it never moves by root motion, interpolation or velocity; a server position reaches it only
/// through a placement or the position manager's blip once its interpolation has failed. Any other object takes the new
/// frame without a collision transition (owner decision D24, a documented deviation): AlignPath turns it along its
/// motion, outdoors the cell follows the origin (<see cref="LandDefs.AdjustToOutside"/>), indoors it is kept, and it
/// becomes the server's cell once the object reaches the server's last position; with a ground probe, a grounded
/// object's height is clamped to the ground under it.</item>
/// <item>Retail's order: the target manager's reports (<c>HandleTargetting</c>), the MoveTo manager
/// (<c>MovementManager::UseTime</c>), the part array's completion check (<c>CPartArray::HandleMovement</c>,
/// <see cref="MotionPlayer.HandleMovement"/>, which completes the queued motions without animations), and the position
/// manager's blips (<c>PositionManager::UseTime</c>).</item>
/// <item>Particles and scripts (with the hooks, <see cref="EffectObject.Update"/>), active or not.</item>
/// </list></para>
/// <para><b>Contact</b> comes from the last UpdatePosition's Grounded flag (protocol.md §5.3): no transition runs for a
/// server object, so contact and walkable ground are both that flag. Gravity is not integrated (D24).</para>
/// <para><b>Not modelled</b>: <c>calc_friction</c>; the collision transition and its cell changes (TODO(physics): full
/// transitions for server objects are deferred, D24); <c>CPhysicsObj::UpdateObjectInternal</c>'s heading rule for an
/// unnamed state bit (ACE reads it as Sledding, which M2 never sees); the rest deactivation in its sphereless branch of
/// an object without a movement manager standing on walkable ground (docs/avatars.md §7).</para>
/// <para>Not thread-safe; the avatar set's frame loop owns it. An update that queues no motion allocates nothing once
/// warm (the allocation tests pin a steadily walking crowd at 0 bytes); one that queues a motion (a MoveTo's steering)
/// allocates the motion table's pending entry (<see cref="MotionPlayer.MotionsQueued"/>).</para>
/// </remarks>
public sealed class AvatarBody : IMotionInterpHost, IMoveToHost
{
    /// <summary>Retail's activity range: an object farther than this from the player is not updated.</summary>
    public const float ActiveDistance = 96f;

    /// <summary>A velocity under this many metres per second is zeroed (0.25).</summary>
    public const float MinVelocity = 0.25f;

    /// <summary>A velocity is capped at this many metres per second.</summary>
    public const float MaxVelocity = 50f;

    private const double Epsilon = 0.00019999999;

    private readonly AvatarSources _sources;
    private readonly ITargetWorld _world;
    private ObjectClock _clock;
    private readonly List<AvatarBody> _children = [];
    private readonly InterpretedMotionState _scratch = new();
    private readonly RemoteWeenie _weenie;
    private AcFrame[] _partFrames;
    private IReadOnlyList<AcFrame>? _lastPose;
    private AcFrame _lastFrame;
    private bool _poseDirty = true;
    private CellFrame? _serverPosition;

    internal AvatarBody(uint guid, ModelAssembly setup, PartArrayAppearance appearance, AvatarSources sources, ITargetWorld world, IEffectRandom random, bool isCreature, double now)
    {
        Guid = guid;
        Setup = setup;
        Appearance = appearance;
        _sources = sources;
        _world = world;
        _weenie = new RemoteWeenie(isCreature);
        _clock = new ObjectClock(now);
        Time = now;
        Active = true; // a dynamic object (makeObject(…, 1)) before its state is described; Describe settles it
        Position = new CellFrame(0, AcFrame.Identity);
        _partFrames = new AcFrame[setup.Parts.Count];
        Positions = new PositionManager();
        Targets = new TargetManager(this, world);
        Effects = new EffectObject(sources.Effects, random, setup.DefaultScriptTableId);
        var defaults = new DefaultAnimationPlayer(sources.Animations, setup.DefaultAnimationId);
        Driver = defaults;
        defaults.Sequence.SetPlacementFrame(sources.Placement(setup, PlacementIds.Resting), PlacementIds.Resting);
        PoseParts();
        Effects.SetPlacement(now, AnchorFrame, _partFrames);
    }

    /// <summary>The object id.</summary>
    public uint Guid { get; }

    /// <summary>The object id (<see cref="ITargetable"/>).</summary>
    public uint Id => Guid;

    /// <summary>The Setup.</summary>
    public ModelAssembly Setup { get; }

    /// <summary>The part array's appearance (ObjDesc changes).</summary>
    public PartArrayAppearance Appearance { get; }

    /// <summary>
    /// What animates the parts: a <see cref="MotionPlayer"/> with a motion table, else a
    /// <see cref="DefaultAnimationPlayer"/> on the Setup's default animation (an empty sequence shows the placement).
    /// </summary>
    public IPoseDriver Driver { get; private set; }

    /// <summary>The part array's sequence.</summary>
    public AnimSequence Sequence => Driver switch
    {
        MotionPlayer m => m.Sequence,
        DefaultAnimationPlayer d => d.Sequence,
        _ => throw new InvalidOperationException("unknown pose driver"),
    };

    /// <summary>The motion-table player, or null without a motion table.</summary>
    public MotionPlayer? Parts => Driver as MotionPlayer;

    /// <summary>The movement manager (interpreter and MoveTo), or null without a motion table.</summary>
    public MovementManager? Movement { get; private set; }

    /// <summary>The motion interpreter, or null (<see cref="IMoveToHost.Motion"/>).</summary>
    public MotionInterpreter? Motion => Movement?.Motion;

    /// <summary>The position managers.</summary>
    public PositionManager Positions { get; }

    /// <summary>The target manager.</summary>
    public TargetManager Targets { get; }

    /// <summary>The effects: scripts, particles, hooks.</summary>
    public EffectObject Effects { get; }

    /// <summary>
    /// The object's sound table (STable, 0x20xxxxxx), or 0: the CreateObject's, else the Setup's default (retail <c>InitDefaults</c> then <c>set_description</c>). The sound type of a server sound
    /// (F750) is looked up in it (<see cref="QueueServerSound"/>).
    /// </summary>
    public uint SoundTableId { get; private set; }

    private readonly List<EffectEvent> _serverSounds = [];

    /// <summary>Server sounds (F750) waiting for the set's next delivery.</summary>
    internal int PendingServerSounds => _serverSounds.Count;

    /// <summary>
    /// F750 Sound on this object (<c>SmartBox::HandleSoundEvent</c>, 11.6096 0x00452000, then <c>CPhysicsObj::play_sound</c>): queues a sound-table event of <paramref name="soundType"/> at
    /// <paramref name="volume"/>, which the set hands to its effect sink with the object's other hook events (<see cref="ServerSoundTableHook"/>). One small allocation per message, none per frame.
    /// </summary>
    internal void QueueServerSound(uint soundType, float volume) =>
        _serverSounds.Add(new EffectEvent(Effects.CurrentTime, new ServerSoundTableHook(SoundTableId, soundType, volume)));

    /// <summary>Appends the queued server sounds to <paramref name="into"/>, oldest first, and clears them.</summary>
    internal int DrainServerSounds(List<EffectEvent> into)
    {
        var count = _serverSounds.Count;
        into.AddRange(_serverSounds);
        _serverSounds.Clear();
        return count;
    }

    /// <summary>Forgets the queued server sounds (no sink to hear them); returns how many.</summary>
    internal int DiscardServerSounds()
    {
        var count = _serverSounds.Count;
        _serverSounds.Clear();
        return count;
    }

    /// <summary>The objects this one holds.</summary>
    public IReadOnlyList<AvatarBody> Children => _children;

    /// <summary>The object holding this one, or null.</summary>
    public AvatarBody? Parent { get; private set; }

    /// <summary>The holding location this object is held at (its parent's <c>HoldingLocations</c> key).</summary>
    public int ParentLocation { get; private set; }

    /// <summary>The parent part it follows (the object frame when beyond the parent's parts).</summary>
    public int ParentPartIndex { get; private set; }

    /// <summary>Its frame relative to that part.</summary>
    public AcFrame HoldingFrame { get; private set; } = AcFrame.Identity;

    /// <summary>The position (cell 0 while not in the world).</summary>
    public CellFrame Position { get; private set; }

    /// <summary>In a cell: in the world, or held by an object that is.</summary>
    public bool InCell => Parent is { } parent ? parent.InCell : Position.CellId != 0;

    /// <summary>In the world on its own (a cell, no parent).</summary>
    public bool InWorld => Parent is null && Position.CellId != 0;

    /// <summary>The physics state bits.</summary>
    public uint State { get; private set; }

    /// <summary>True while Hidden.</summary>
    public bool IsHidden => (State & AvatarStateBits.Hidden) != 0;

    /// <summary>Drawn: in a cell, neither NoDraw nor Hidden.</summary>
    public bool IsDrawn => InCell && (State & (AvatarStateBits.NoDraw | AvatarStateBits.Hidden)) == 0;

    /// <summary>Retail <c>transient_state</c> Contact (here the Grounded flag).</summary>
    public bool HasContact { get; private set; }

    /// <summary>Retail <c>transient_state</c> OnWalkable (here the Grounded flag).</summary>
    public bool OnWalkable { get; private set; }

    /// <summary>Retail <c>transient_state</c> Active (0x80): updated this frame.</summary>
    public bool Active { get; private set; }

    /// <summary>Retail <c>player_distance</c>: the distance from the player at the last update, or 0 without a player.</summary>
    public float PlayerDistance { get; private set; }

    /// <summary>Retail <c>m_velocityVector</c>: the physics velocity, world axes.</summary>
    public Vector3 VelocityVector { get; private set; }

    /// <summary>Retail <c>cached_velocity</c>: the velocity measured over the last update (zero for an object without spheres).</summary>
    public Vector3 Velocity { get; private set; }

    /// <summary>Retail <c>m_accelerationVector</c>.</summary>
    public Vector3 Acceleration { get; set; }

    /// <summary>Retail <c>m_omegaVector</c>: the angular velocity, radians per second.</summary>
    public Vector3 Omega { get; set; }

    /// <summary>Retail <c>m_scale</c>.</summary>
    public float Scale { get; private set; } = 1f;

    /// <summary>Retail <c>last_move_was_autonomous</c>.</summary>
    public bool MovementIsAutonomous { get; set; }

    /// <summary>The frame clock at the last <see cref="Advance"/> (<see cref="ITargetable.Time"/>).</summary>
    public double Time { get; private set; }

    /// <summary>
    /// The landblock the drawn frames are expressed in: a cell of the landblock the object was last placed in. It moves
    /// only on a placement (a teleport, a blip, entering the world) or when the object is more than two landblocks away,
    /// so the object's frames and its particles stay continuous across landblock seams.
    /// </summary>
    public uint AnchorCellId { get; private set; }

    /// <summary>The object frame in <see cref="AnchorCellId"/>'s landblock space.</summary>
    public AcFrame AnchorFrame => ToAnchor(Position);

    /// <summary>The frames of the parts, in <see cref="AnchorCellId"/>'s landblock space.</summary>
    public ReadOnlySpan<AcFrame> PartFrames => _partFrames;

    /// <summary>Changes whenever the drawn keyframe or the object frame changes, so a renderer rewrites transforms only then.</summary>
    public int PoseVersion { get; private set; }

    /// <summary>The number of quanta stepped (active ones).</summary>
    public long Steps { get; private set; }

    /// <summary>The cylinder radius (<c>CPartArray::GetRadius</c>: the Setup's radius × scale).</summary>
    public float Radius => Setup.Radius * Scale;

    /// <summary>The cylinder height (<c>CPartArray::GetHeight</c>: the Setup's height × scale).</summary>
    public float Height => Setup.Height * Scale;

    /// <summary>The parent's id when held, else its own.</summary>
    public uint TopLevelId => Parent?.Guid ?? Guid;

    /// <summary>The compass heading (<c>get_heading</c>).</summary>
    public float Heading => PositionFrames.Heading(Position.Orientation);

    /// <summary>Interpolating to a server position.</summary>
    public bool IsInterpolating => Positions.IsInterpolating;

    /// <summary>Retail <c>state</c> Gravity.</summary>
    public bool HasGravity => (State & AvatarStateBits.Gravity) != 0;

    /// <summary>Fully constrained (jumps refused).</summary>
    public bool IsFullyConstrained => Positions.IsFullyConstrained;

    /// <summary>The orientation.</summary>
    public Quaternion Orientation => Position.Orientation;

    /// <summary>The weenie: a server object's, which knows whether it is a creature and nothing else.</summary>
    public IMotionWeenie? Weenie => _weenie;

    /// <summary>The objects ids resolve to.</summary>
    public ITargetWorld World => _world;

    Vector3 IMotionInterpHost.Velocity => VelocityVector;

    // ----- creation (makeObject, set_description) -----

    /// <summary>
    /// Retail <c>CPhysicsObj::InitDefaults</c> (0x005139D0 / 0x005144D0) for the Setup's default script, and
    /// <c>set_description</c> (0x00514F40 / 0x00515A40) for the rest: the motion table (see
    /// <see cref="SetMotionTable"/>), the physics script table, the movement (or the placement), the state, scale,
    /// translucency, velocity, angular velocity and default script.
    /// </summary>
    /// <remarks>
    /// <para><b>Active from creation.</b> <c>ACCObjectMaint::CreateObject</c> (0x00558870 / 0x005594B0) makes every
    /// server object dynamic (<c>CPhysicsObj::makeObject</c> with <c>bDynamic</c> 1, so <c>InitObjectBegin</c> clears
    /// Static), and <c>set_description</c> sets the described state last. So the active bit that
    /// <c>MakeMovementManager</c> (0x00510270 / 0x00510D40) and <c>unpack_movement</c> (0x00512040 / 0x00512B10) set
    /// "unless Static" is set for any object with a motion table (the Setup's, through <c>InitDefaults</c> →
    /// <c>SetMotionTableID</c>, or the description's) or a movement, Static or not. Any other object is made active by
    /// <c>enter_world</c> → <c>prepare_to_enter_world</c> (0x00511FA0 / 0x00512A70) only when not Static. A Static object
    /// that is active stays so until it is once more than 96 m from the player (<c>set_active(1)</c> refuses a Static
    /// object but does not clear the bit); one that is not is never stepped, its particles and scripts aside.</para>
    /// <para>For the same reason <c>InitDefaults</c>' static-animation registration (the 0x40000 and 0x80000 bits and
    /// <c>CPhysics::AddStaticAnimatingObject</c>, which <c>animate_static_object</c>, 0x00513DF0 / 0x005148F0, steps) never
    /// sees the Static bit for a server object: its default animation plays only while it is active.</para>
    /// </remarks>
    /// <param name="create">The description.</param>
    /// <param name="now">The frame clock.</param>
    /// <param name="withInterpreter">
    /// False for the player's own avatar: its motion interpreter is the player body's (physics hosts it on
    /// <see cref="Parts"/>), so none is made here and the movement is left to the body.
    /// </param>
    /// <param name="problems">Receives what failed.</param>
    internal void Describe(AvatarCreate create, double now, bool withInterpreter, ICollection<string> problems)
    {
        if (Setup.DefaultScriptId != 0)
        {
            Effects.PlayScriptId(Setup.DefaultScriptId);
        }

        SetMotionTable(create.MotionTableId, withInterpreter, problems);
        Effects.PhysicsScriptTableId = create.PhysicsScriptTableId;
        SoundTableId = create.SoundTableId != 0 ? create.SoundTableId : Setup.DefaultSoundTableId;
        if (create.Movement is { } movement)
        {
            MovementIsAutonomous = create.MovementAutonomous;
            if (Movement is not null)
            {
                Span<WireMotionItem> actions = stackalloc WireMotionItem[Math.Min(create.MovementActions.Count, 64)];
                for (var i = 0; i < actions.Length; i++)
                {
                    actions[i] = create.MovementActions[i];
                }

                var result = UnpackMovement(movement, actions, out var failure);
                if (result != MovementUnpackResult.Applied)
                {
                    problems.Add($"movement: {result}{(result == MovementUnpackResult.UnknownCommand ? " (" + failure.Message + ")" : "")}");
                }
            }
        }
        else
        {
            SetPlacementFrame(create.Placement);
        }

        SetCreationState(create.PhysicsState);
        Active = Driver is MotionPlayer || create.Movement is not null || (State & AvatarStateBits.Static) == 0;
        Scale = create.Scale;
        Effects.Visual.TranslucencyOriginal = create.Translucency;
        SetVelocity(create.Velocity);
        Omega = create.Omega;
        Acceleration = create.Acceleration;
        Effects.DefaultScript = create.DefaultScript;
        Effects.DefaultScriptIntensity = create.DefaultScriptIntensity;
        Time = now;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::SetMotionTableID</c> (0x00512780 / 0x00513280) after <c>InitDefaults</c> set the Setup's
    /// default table: the description's table, with a movement manager in its default state; none when it is 0 (and
    /// when the Setup's table was set, its default state keeps playing without a manager, as retail's sequence keeps the
    /// animations); the Setup's default animation otherwise. A table that does not load is reported and left out.
    /// </summary>
    private void SetMotionTable(uint tableId, bool withInterpreter, ICollection<string> problems)
    {
        var defaultTable = Setup.DefaultMotionTableId;
        var id = tableId != 0 ? tableId : defaultTable;
        if (id == 0)
        {
            return;
        }

        var table = _sources.GetMotionTable(id);
        if (table is null)
        {
            problems.Add($"motion table 0x{id:X8} does not load; the object keeps its {(Setup.DefaultAnimationId != 0 ? "default animation" : "placement")}");
            return;
        }

        var player = new MotionPlayer(table, _sources.Animations);
        var placement = Driver is DefaultAnimationPlayer d ? d.Sequence.PlacementFrame : null;
        player.Sequence.SetPlacementFrame(placement, PlacementIds.Resting);
        Driver = player;
        _poseDirty = true;
        if (tableId == 0 || !withInterpreter)
        {
            // tableId 0: retail destroyed the manager; the sequence keeps cycling the default state with no interpreter.
            return;
        }

        var motion = new MotionInterpreter(player, this);
        Movement = new MovementManager(motion, this);
        motion.EnterDefaultState();
        _poseDirty = true;
    }

    // ----- server messages -----

    /// <summary>
    /// Retail <c>CPhysics::SetObjectMovement</c> (0x00509690 / 0x0050A160) after the stamps: the autonomous bit becomes
    /// <see cref="MovementIsAutonomous"/> and the movement is unpacked (<see cref="MovementManager.UnpackMovement"/>). An
    /// object without a motion table ignores it, as retail's movement manager without an interpreter does.
    /// </summary>
    public MovementUnpackResult ApplyMovement(in WireMovement movement, ReadOnlySpan<WireMotionItem> actions, bool autonomous, out WireMotionFailure failure)
    {
        MovementIsAutonomous = autonomous;
        return UnpackMovement(movement, actions, out failure);
    }

    private MovementUnpackResult UnpackMovement(in WireMovement movement, ReadOnlySpan<WireMotionItem> actions, out WireMotionFailure failure)
    {
        failure = default;
        if (Movement is not { } manager)
        {
            return MovementUnpackResult.UnknownType;
        }

        var result = manager.UnpackMovement(movement, actions, _scratch, out failure);
        _poseDirty = true;
        return result;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::MoveOrTeleport</c> (0x00516330 / 0x00516E30) for a newer server position of a server
    /// object, after <c>SmartBox::HandleReceivedPosition</c> (0x00453FD0 / 0x00454070) unset its parent and set the
    /// placement when it has no animations:
    /// <list type="bullet">
    /// <item>a newer teleport sequence, or no cell: the object is placed there (<c>SetPosition</c>, with the teleport hook
    /// that cancels its MoveTo, stick, interpolation and constraint);</item>
    /// <item>no contact: ignored;</item>
    /// <item>96 m or more from the player: interpolation stops and the object is placed there (<c>SetPositionSimple</c>);</item>
    /// <item>otherwise it interpolates there, keeping its heading while a MoveTo is under way.</item>
    /// </list>
    /// Then, when the position was taken, the object is constrained to its own position with the retail distances.
    /// </summary>
    /// <returns>True when the position was taken (retail's return value).</returns>
    public bool MoveOrTeleport(in CellFrame position, bool teleportAdvanced, bool hasContact, uint? placement)
    {
        if (Parent is { } parent)
        {
            parent.RemoveChild(this);
        }

        if (!Sequence.HasAnims && placement is { } p)
        {
            SetPlacementFrame(p);
        }

        SetContact(hasContact);
        var target = position.Normalized();
        if (teleportAdvanced || Position.CellId == 0)
        {
            TeleportHook();
            Place(target);
        }
        else if (!hasContact)
        {
            return false;
        }
        else if (PlayerDistance >= ActiveDistance)
        {
            Positions.StopInterpolating();
            Place(target);
        }
        else
        {
            var result = Positions.InterpolateTo(target, Movement?.IsMovingTo ?? false, Context());
            if (result.SetHeading is { } heading)
            {
                SetHeading(heading);
            }
        }

        _serverPosition = target;
        var cell = Position.CellId;
        Positions.ConstrainTo(Position, PositionManager.GetStartConstraintDistance(cell), PositionManager.GetMaxConstraintDistance(cell), Context());
        return true;
    }

    /// <summary>
    /// Places the object (retail <c>SetPosition</c> / <c>SetPositionSimple</c> without a transition): the position is
    /// taken as given, the parts are posed and the children follow.
    /// </summary>
    public void Place(in CellFrame position)
    {
        Position = position;
        AnchorCellId = position.CellId;
        _poseDirty = true;
        PoseParts();
        UpdateChildren();
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::teleport_hook</c> (0x00514ED0 / 0x005159D0): cancels a MoveTo (0x3C), ends a stick, stops
    /// interpolating and unconstrains.
    /// </summary>
    public void TeleportHook()
    {
        Movement?.CancelMoveTo(WeenieError.ITeleported);
        Positions.UnStick();
        Positions.StopInterpolating();
        Positions.UnConstrain();
    }

    /// <summary>
    /// The Grounded flag of a position: contact and walkable ground (<see cref="SetOnWalkable"/> on a change). Landing
    /// also stops the physics velocity, standing in for the contact plane and friction that retail's transition applies
    /// (no transition runs for a server object, D24).
    /// </summary>
    public void SetContact(bool hasContact)
    {
        var landed = hasContact && !HasContact;
        HasContact = hasContact;
        SetOnWalkable(hasContact);
        if (landed)
        {
            VelocityVector = Vector3.Zero;
        }
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::set_state</c> (0x00514DD0 / 0x005158D0): acts only on changed bits: LightingOn turns the
    /// Setup's lights on or off (the renderer reads <see cref="LightsOn"/>), NoDraw <c>set_nodraw</c>, Hidden
    /// <c>set_hidden</c> (0x00514C60 / 0x00515760) through the portal track's <see cref="HiddenTransition"/>: the Hidden
    /// or UnHide script, the collision bits, the children's NoDraw, and the link animations dropped.
    /// </summary>
    /// <returns>The hidden change, so the set can report the player's.</returns>
    public HiddenChange SetState(uint newState) => ApplyState(State, newState);

    /// <summary>
    /// <c>set_description</c>'s <c>set_state</c> for a new object (11.4186 0x00514F40, 11.6096 0x00515A40): the change is read against the state
    /// <c>CPhysicsObj::CPhysicsObj</c> gave the object (<see cref="HiddenTransition.ConstructedState"/>, Hidden clear; 11.4186 0x005123F0,
    /// 11.6096 0x00512EF0), so an object created Hidden plays its Hidden script (<see cref="HiddenTransition.ClassifyCreation"/>'s rule).
    /// </summary>
    private HiddenChange SetCreationState(uint newState) => ApplyState(HiddenTransition.ConstructedState, newState);

    private HiddenChange ApplyState(uint old, uint newState)
    {
        State = newState;
        var change = HiddenChange.None;
        if (((old ^ newState) & AvatarStateBits.Hidden) != 0)
        {
            change = HiddenTransition.Classify(old, newState);
            State = HiddenTransition.ApplyToObjectState(State, change);
            if (change == HiddenChange.Unhide)
            {
                _unhideFadesQueued = Effects.TranslucencyFadesQueued;
                _unhideAt = Effects.CurrentTime;
                _unhideHoldPending = true;
            }

            HiddenTransition.Play(Effects, change);
            var hidden = change == HiddenChange.Hide;
            for (var i = 0; i < _children.Count; i++)
            {
                _children[i].SetNoDrawBit(hidden);
            }

            Parts?.RemoveLinkAnimations();
        }

        _poseDirty = true;
        return change;
    }

    private bool _unhideHoldPending;
    private long _unhideFadesQueued;
    private double _unhideAt;

    /// <summary>
    /// The glitch fix for retail's one opaque frame after an unhide (docs/avatars.md section 17; <c>--retail-unhide-flash</c> restores the frame): true for the first frame
    /// drawn after an unhide in which the object's translucency must be drawn as 1 (invisible) rather than whatever an older fade hook left in it. Retail's UnHide script
    /// queues its fade (a Transparent hook from 1 to 0) in the update that plays it, and the hook first applies in the next, so the first frame shows the value left behind:
    /// 1 from the Hidden script's own hook normally, but 0 when an older fade ran on unseen while the object was hidden (a re-hide mid-fade, an unhide after that fade
    /// ended). The hold is for that one frame, and only when the unhide queued a fade (or the body has not been updated since the unhide, so it is not known yet); an
    /// unhide that plays no fade is drawn as it is. Consumed by the call that reports it.
    /// </summary>
    internal bool ConsumeUnhideHold()
    {
        if (!_unhideHoldPending)
        {
            return false;
        }

        var updated = Effects.CurrentTime > _unhideAt;
        var queued = Effects.TranslucencyFadesQueued != _unhideFadesQueued;
        if (updated)
        {
            _unhideHoldPending = false; // the frame is decided: held if the fade was queued in the update, else shown as it is
        }

        return queued || !updated;
    }

    /// <summary>True while the Setup's lights are on (LightingOn; retail <c>set_lights</c>).</summary>
    public bool LightsOn => (State & AvatarStateBits.LightingOn) != 0;

    /// <summary>
    /// Retail <c>SmartBox::DoVectorUpdate</c> (0x004521C0 / 0x00452200) after the stamp: the velocity and angular
    /// velocity.
    /// </summary>
    public void SetVectors(Vector3 velocity, Vector3 omega)
    {
        SetVelocity(velocity);
        Omega = omega;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::DoObjDescChangesFromDefault</c> (0x0050F9B0 / 0x00510480), which an ObjDescEvent applies
    /// through <c>ACCObjectMaint::SetVisualDesc</c>: the part array's changes, then the translucency is applied again.
    /// </summary>
    public AppearanceResult SetVisualDesc(ObjDesc desc)
    {
        var result = Appearance.DoObjDescChangesFromDefault(desc);
        _poseDirty = true;
        return result;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::SetPlacementFrame</c> (0x00511380 / 0x00511E50): the Setup's placement (else Default) as
    /// the sequence's placement frame, shown while no animation plays.
    /// </summary>
    public void SetPlacementFrame(uint placementId)
    {
        Sequence.SetPlacementFrame(_sources.Placement(Setup, placementId), placementId);
        _poseDirty = true;
        PoseParts();
    }

    // ----- parenting (set_parent, unset_parent, UpdateChild) -----

    /// <summary>
    /// Retail <c>CPhysicsObj::set_parent(obj, where)</c> (0x00515A90 / 0x00516590) with <c>add_child</c> (0x0050F870 /
    /// 0x00510340): the parent's Setup must hold the location (<c>CSetup::GetHoldingLocation</c>); then the child leaves
    /// any old parent and the world, follows the location's part and frame, and is NoDraw while the parent is Hidden.
    /// </summary>
    /// <returns>False (nothing changes) when the parent is this object or lacks the location.</returns>
    /// <param name="parent">The holder.</param>
    /// <param name="location">The holding location.</param>
    /// <param name="placement">The child's placement (<c>SetPlacementFrame</c> after <c>set_parent</c>, as
    /// <c>SmartBox::DoParentEvent</c> does), or null to keep it (<c>CObjectMaint::SetChildren</c>).</param>
    public bool SetParent(AvatarBody parent, int location, uint? placement)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (ReferenceEquals(parent, this) || !parent.Setup.HoldingLocations.TryGetValue(location, out var hold))
        {
            return false;
        }

        Parent?.RemoveChild(this);
        LeaveWorld();
        Parent = parent;
        ParentLocation = location;
        ParentPartIndex = hold.PartIndex;
        HoldingFrame = new AcFrame(hold.Frame.Origin, hold.Frame.Orientation);
        parent._children.Add(this);
        if (parent.IsHidden)
        {
            SetNoDrawBit(true);
        }

        if (placement is { } p)
        {
            SetPlacementFrame(p);
        }

        parent.UpdateChild(this);
        return true;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::unset_parent</c> (0x00513470 / 0x00513F70) from the parent's side: the child leaves the
    /// parent's list, loses the NoDraw a Hidden parent gave it, and its clock restarts.
    /// </summary>
    internal void RemoveChild(AvatarBody child)
    {
        if (!_children.Remove(child))
        {
            return;
        }

        if (IsHidden)
        {
            child.SetNoDrawBit(false);
        }

        child.Parent = null;
        child._clock = new ObjectClock(Time);
    }

    /// <summary>
    /// Retail <c>SmartBox::DoPickupEvent</c> (0x00452240 / 0x00452280) after the stamp: unparented and out of the world.
    /// </summary>
    public void PickUp()
    {
        Parent?.RemoveChild(this);
        LeaveWorld();
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::leave_world</c> (0x005155A0 / 0x005160A0): inactive, out of its cell, no contact (losing
    /// walkable ground leaves the ground), and its voyeurs told it left.
    /// </summary>
    public void LeaveWorld()
    {
        Targets.NotifyVoyeurOfEvent(TargetStatus.ExitWorld);
        Active = false;
        Position = Position with { CellId = 0 };
        HasContact = false;
        SetOnWalkable(false);
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::exit_world</c> (0x00514E60 / 0x00515960), before a delete: the part array's and the
    /// interpreter's exit, the stick ended, the target cleared and the voyeurs told.
    /// </summary>
    public void ExitWorld()
    {
        Parts?.RemoveLinkAnimations();
        Movement?.HandleExitWorld();
        Positions.UnStick();
        Targets.ClearTarget();
        Targets.NotifyVoyeurOfEvent(TargetStatus.ExitWorld);
        foreach (var child in _children.ToArray())
        {
            RemoveChild(child);
        }

        Parent?.RemoveChild(this);
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::UpdateChild</c> (0x00512D50 / 0x00513850): the child's frame is its parent part's frame
    /// (the object frame for a part index beyond the parts) combined with its holding frame; its particles and scripts
    /// run.
    /// </summary>
    private void UpdateChild(AvatarBody child)
    {
        var anchor = child.ParentPartIndex >= 0 && child.ParentPartIndex < _partFrames.Length
            ? _partFrames[child.ParentPartIndex]
            : AnchorFrame;
        var frame = AcFrame.Combine(anchor, child.HoldingFrame);
        child.AnchorCellId = AnchorCellId;
        child.Position = new CellFrame(AnchorCellId, frame);
        child.Time = Time;
        child.PoseParts();
        child.Effects.Update(Time, child.AnchorFrame, child._partFrames);
        child.UpdateChildren();
    }

    /// <summary>Retail <c>UpdateChildrenInternal</c> (0x00513DA0 / 0x005148A0).</summary>
    private void UpdateChildren()
    {
        for (var i = 0; i < _children.Count; i++)
        {
            UpdateChild(_children[i]);
        }
    }

    private void SetNoDrawBit(bool noDraw)
    {
        State = noDraw ? State | AvatarStateBits.NoDraw : State & ~AvatarStateBits.NoDraw;
    }

    // ----- stepping (update_object) -----

    /// <summary>
    /// Retail <c>CPhysicsObj::update_object</c> (0x00515D10 / 0x00516810) up to <paramref name="now"/> (the frame clock,
    /// seconds); see the class remarks.
    /// </summary>
    public void Advance(double now, in AvatarStep step)
    {
        Time = now;
        if (Parent is not null || Position.CellId == 0 || (State & AvatarStateBits.Frozen) != 0)
        {
            Active = false;
            return;
        }

        if (step.PlayerPosition is { } player)
        {
            PlayerDistance = PositionFrames.Distance(player, Position);
            if (PlayerDistance > ActiveDistance)
            {
                Active = false;
            }
            else
            {
                SetActive(now);
            }
        }
        else
        {
            PlayerDistance = 0f;
            SetActive(now);
        }

        foreach (var (quantum, time) in _clock.Advance(now))
        {
            UpdateObjectInternal(quantum, time, step);
        }

        PoseParts();
    }

    /// <summary>
    /// Retail <c>set_active(1)</c> (0x0050FC40 / 0x00510710): a Static object never becomes active (it keeps the bit it
    /// was created with until it goes out of range, and still runs its particles and scripts); becoming active restarts
    /// the clock at <paramref name="now"/>, so the time spent inactive is not stepped.
    /// </summary>
    private void SetActive(double now)
    {
        if ((State & AvatarStateBits.Static) != 0)
        {
            return;
        }

        if (!Active)
        {
            _clock = new ObjectClock(now);
        }

        Active = true;
    }

    private void UpdateObjectInternal(double quantum, double time, in AvatarStep step)
    {
        if (Active)
        {
            if (Position.CellId == 0)
            {
                return;
            }

            Steps++;
            var newFrame = UpdatePositionInternal(quantum);
            var old = Position;
            var newOrigin = newFrame.Origin;
            var probe = step.GroundProbe;
            if (Setup.Spheres.Count == 0)
            {
                // CPartArray::GetNumSphere (0x00518060; TODO(avatars): 11.6096) is 0: retail puts the old origin back into
                // the new frame, so only the rotation changes. Root motion, interpolation and velocity never move the
                // object (its interpolation fails its progress checks and the position manager blips it), and AlignPath
                // does not turn it. No transition runs, so the ground probe that stands in for one (D24) does not either.
                // The branch's rest rule (no movement manager and on walkable ground: the active bit is cleared) is not
                // modelled (avatars.md §7).
                newFrame = newFrame with { Origin = old.Origin };
                Velocity = Vector3.Zero;
                probe = null;
            }
            else if (newOrigin == old.Origin)
            {
                Velocity = Vector3.Zero;
            }
            else
            {
                if ((State & AvatarStateBits.AlignPath) != 0)
                {
                    newFrame = EffectActor.SetVectorHeading(newFrame, newOrigin - old.Origin);
                }

                // Retail's transition runs here (TODO(physics), D24): the frame is taken as it is.
                Velocity = (newOrigin - old.Origin) / (float)quantum;
            }

            SetFrame(newFrame, probe);
            Targets.HandleTargetting(time);
            Movement?.UseTime();
            Parts?.HandleMovement();
            if (Positions.UseTime(Context()) is { } blip)
            {
                Place(blip.Position);
                Positions.Interpolation.CompleteBlip(placed: true);
            }
        }

        Effects.Update(time, AnchorFrame, _partFrames);
    }

    /// <summary>Retail <c>UpdatePositionInternal</c> (0x00512C30 / 0x00513730); see the class remarks.</summary>
    private AcFrame UpdatePositionInternal(double quantum)
    {
        var offset = AcFrame.Identity;
        if (!IsHidden)
        {
            var update = Driver.Update(quantum);
            offset = update.RootMotion;
            offset = offset with { Origin = offset.Origin * (OnWalkable ? Scale : 0f) };
            var hooks = update.Hooks;
            for (var i = 0; i < hooks.Count; i++)
            {
                Effects.QueueAnimHook(hooks[i]);
            }
        }

        Positions.AdjustOffset(ref offset, quantum, Context());
        var frame = AcFrame.Combine(Position.Frame, offset);
        if (!IsHidden)
        {
            frame = UpdatePhysicsInternal((float)quantum, frame);
        }

        return frame;
    }

    /// <summary>Retail <c>UpdatePhysicsInternal</c> (0x00510700 / 0x005111D0), without friction.</summary>
    private AcFrame UpdatePhysicsInternal(float quantum, AcFrame frame)
    {
        var v = VelocityVector;
        var speed2 = v.LengthSquared();
        if (speed2 > 0f)
        {
            if (speed2 > MaxVelocity * MaxVelocity)
            {
                v = Vector3.Normalize(v) * MaxVelocity;
                speed2 = MaxVelocity * MaxVelocity;
            }

            if (speed2 - (MinVelocity * MinVelocity) < Epsilon)
            {
                v = Vector3.Zero;
            }

            frame = frame with { Origin = frame.Origin + (v * quantum) + (Acceleration * 0.5f * quantum * quantum) };
        }

        VelocityVector = v + (Acceleration * quantum);
        return frame.GRotate(Omega * quantum);
    }

    /// <summary>
    /// The new frame without a transition: the cell follows the origin outdoors, becomes the server's when the object is
    /// at the server's position, and the ground probe clamps a grounded object.
    /// </summary>
    private void SetFrame(AcFrame frame, IGroundProbe? probe)
    {
        var cell = Position.CellId;
        var origin = frame.Origin;
        if (LandDefs.IsOutdoorCell(cell))
        {
            var adjusted = cell;
            var local = origin;
            if (LandDefs.AdjustToOutside(ref adjusted, ref local))
            {
                cell = adjusted;
                origin = local;
            }
        }

        var position = new CellFrame(cell, frame with { Origin = origin });
        if (_serverPosition is { } server && server.CellId != cell && PositionFrames.Distance(position, server) < InterpolationManager.DesiredDistance)
        {
            position = new CellFrame(server.CellId, position.Frame with { Origin = position.Origin + LandDefs.GetBlockOffset(server.CellId, cell) });
        }

        if (probe is not null && HasContact && !IsHidden && probe.TryGroundZ(position, GroundRadius, out var z, out _))
        {
            position = position with { Frame = position.Frame with { Origin = position.Origin with { Z = z } } };
        }

        Position = position;
        if (AnchorCellId == 0 || IsFar(AnchorCellId, position.CellId))
        {
            AnchorCellId = position.CellId;
        }

        // Retail set_frame poses the parts (CPartArray::SetFrame → UpdateParts) before the children follow them.
        PoseParts();
        UpdateChildren();
    }

    /// <summary>More than two landblocks apart on either axis.</summary>
    private static bool IsFar(uint a, uint b) =>
        Math.Abs((int)(a >> 24) - (int)(b >> 24)) > 2 || Math.Abs((int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF)) > 2;

    /// <summary><paramref name="position"/>'s frame in the anchor landblock's space.</summary>
    private AcFrame ToAnchor(in CellFrame position) =>
        AnchorCellId == 0 || position.CellId == 0
            ? position.Frame
            : position.Frame with { Origin = position.Origin + LandDefs.GetBlockOffset(AnchorCellId, position.CellId) };

    /// <summary>The radius of the lowest sphere, scaled, for the ground probe.</summary>
    private float GroundRadius => Setup.Spheres.Count > 0 ? Setup.Spheres[0].Radius * Scale : 0f;

    /// <summary>The position managers' view of this object at this moment.</summary>
    private PositionContext Context() =>
        PositionContext.For(Position, HasContact, isThePlayer: false, Motion, Positions.GetStickyObjectId());

    /// <summary>
    /// Poses the parts (retail <c>CPartArray::UpdateParts</c> through <see cref="Data.Animation.PartPose"/>) when the drawn keyframe or
    /// the object frame changed, and bumps <see cref="PoseVersion"/>.
    /// </summary>
    internal void PoseParts()
    {
        var pose = Driver.Pose;
        var frame = AnchorFrame;
        if (!_poseDirty && ReferenceEquals(pose, _lastPose) && frame == _lastFrame)
        {
            return;
        }

        _poseDirty = false;
        _lastPose = pose;
        _lastFrame = frame;
        PoseVersion++;
        if (pose is null)
        {
            Array.Fill(_partFrames, frame);
            return;
        }

        Data.Animation.PartPose.Update(frame, pose, new Vector3(Scale), _partFrames);
        for (var i = pose.Count; i < _partFrames.Length; i++)
        {
            _partFrames[i] = frame;
        }
    }

    // ----- the player's avatar, moved by the player body -----

    /// <summary>
    /// The player's avatar draws the pose of the player body's part array (the physics track steps it; it is
    /// <see cref="Parts"/> when the body was built on it).
    /// </summary>
    internal void UseExternalDriver(IPoseDriver driver)
    {
        Driver = driver;
        _poseDirty = true;
    }

    /// <summary>
    /// One frame of the player's avatar while a player body moves it: the body's position (the measured velocity from
    /// the last one), the parts posed, the voyeurs served, the effects and the children updated. The animation and the
    /// physics are the body's.
    /// </summary>
    internal void Follow(double now, in CellFrame position, bool hasContact)
    {
        var elapsed = now - Time;
        var old = Position;
        Time = now;
        Velocity = old.CellId != 0 && elapsed > Epsilon ? PositionFrames.Offset(old, position) / (float)elapsed : Vector3.Zero;
        Position = position;
        if (AnchorCellId == 0 || IsFar(AnchorCellId, position.CellId))
        {
            AnchorCellId = position.CellId;
        }

        HasContact = hasContact;
        OnWalkable = hasContact;
        Active = position.CellId != 0;
        PoseParts();
        Targets.HandleTargetting(now);
        Effects.Update(now, AnchorFrame, _partFrames);
        UpdateChildren();
    }

    // ----- host interfaces -----

    /// <summary>Retail <c>set_velocity</c> (0x005113F0 / 0x00511EC0): capped at 50 m/s.</summary>
    private void SetVelocity(Vector3 velocity)
    {
        if (velocity.LengthSquared() > MaxVelocity * MaxVelocity)
        {
            velocity = Vector3.Normalize(velocity) * MaxVelocity;
        }

        VelocityVector = velocity;
    }

    /// <inheritdoc/>
    public void SetLocalVelocity(Vector3 localVelocity) => SetVelocity(Vector3.Transform(localVelocity, Position.Orientation));

    /// <summary>
    /// Retail <c>CPhysicsObj::set_on_walkable</c> (0x00511310 / 0x00511DE0): on a change, the movement manager hits or
    /// leaves the ground.
    /// </summary>
    public void SetOnWalkable(bool onWalkable)
    {
        var was = OnWalkable;
        OnWalkable = onWalkable;
        if (was && !onWalkable)
        {
            Movement?.LeaveGround();
        }
        else if (!was && onWalkable)
        {
            Movement?.HitGround();
        }
    }

    /// <inheritdoc/>
    public void CancelMoveTo() => Movement?.CancelMoveTo(WeenieError.ActionCancelled);

    /// <inheritdoc/>
    public void UnstickFromObject() => Positions.UnStick();

    /// <inheritdoc/>
    public void SetHeading(float degrees)
    {
        Position = Position with { Frame = Position.Frame with { Orientation = PositionFrames.WithHeading(Position.Orientation, degrees) } };
        _poseDirty = true;
    }

    /// <inheritdoc/>
    public void StopCompletely() => Motion?.PerformMovement(MotionMovementType.StopCompletely, 0, new MovementParameters());

    /// <inheritdoc/>
    public void StickTo(uint objectId, float radius, float height) => Positions.StickTo(objectId, radius, height, this);

    /// <summary>
    /// Retail <c>CPhysicsObj::HandleUpdateTarget</c> (0x00512BC0 / 0x005136C0): a context-0 report goes to the MoveTo
    /// and sticky managers.
    /// </summary>
    public void HandleUpdateTarget(in TargetInfo info)
    {
        if (info.ContextId != 0)
        {
            return;
        }

        Movement?.HandleUpdateTarget(info);
        Positions.HandleUpdateTarget(info);
    }

    /// <summary>A server object's weenie: a creature or not; it cannot tell run rates or jumps.</summary>
    private sealed class RemoteWeenie(bool isCreature) : IMotionWeenie
    {
        public bool IsCreature { get; } = isCreature;

        public bool IsThePlayer => false;

        public bool TryGetRunRate(out float rate)
        {
            rate = 0f;
            return false;
        }

        public bool TryGetJumpVelocity(float extent, out float velocityZ)
        {
            velocityZ = 0f;
            return false;
        }

        public bool CanJump(float extent) => true;

        public bool TryGetJumpStaminaCost(float extent, out int staminaCost)
        {
            staminaCost = 0;
            return false;
        }
    }
}
