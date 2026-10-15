using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;

namespace Lifestone.Data.Physics.Objects;

/// <summary>
/// A moving physics object (retail <c>CPhysicsObj</c>, 11.4186 PORTAL/cphysobj/CPhysicsObj.c, re-expressed function by
/// function with the 11.4186 and 11.6096 addresses cited on each member; nothing copied): its position and cell, state,
/// velocity and contact, the 30 Hz stepping of <c>update_object</c>, placement (<c>SetPosition</c>), the collision
/// transitions and their bookkeeping (<c>SetPositionInternal</c>, <c>handle_all_collisions</c>), the shadows it casts
/// into cells so that other objects collide with it, and the host of the avatars track's motion: its movement manager
/// (<see cref="MovementManager"/>, with the <see cref="MotionInterpreter"/>, retail <c>CMotionInterp</c>, and the MoveTo
/// manager), its position managers (interpolation, sticky, constraint) and its target manager.
/// </summary>
/// <remarks>
/// <para><b>Use.</b> Make one in a <see cref="PhysicsWorld"/> from its Setup's <see cref="PhysicsSetup"/> (with a
/// <see cref="MotionPlayer"/> when it animates), put it into the world with <see cref="EnterWorld"/> or
/// <see cref="SetPosition"/>, and step it with <see cref="UpdateObject"/> whenever <see cref="PhysicsWorld.UseTime"/>
/// lets physics run. The client's player is the world's <see cref="PhysicsWorld.Player"/>.</para>
/// <para><b>Server objects</b> are not stepped (owner decision D24: interpolation and a ground probe instead of
/// transitions); a host that wants the player to collide with one (D25) keeps a PhysicsObj for it in the world and moves
/// it with <see cref="ForceIntoCell"/> and <see cref="RecalcCrossCells"/>.</para>
/// <para><b>Targets and MoveTos.</b> The object is an <see cref="ITargetable"/> and an <see cref="IMoveToHost"/>: a server
/// MoveTo, TurnTo or sticky follow for it runs in its own update (<see cref="UnpackMovement"/>), and the objects that
/// target it (a creature chasing the player) get its reports from that update too. Target ids are looked up in
/// <see cref="TargetWorld"/> (the session's objects; the host sets it), else among this world's objects.</para>
/// <para><b>Not here</b> (the game's or the renderer's business): parenting, lights, translucency, particles, scripts,
/// sounds, detection and attacks. Animation hooks other than animation-done go to
/// <see cref="IPhysicsEvents.OnAnimationHooks"/>.</para>
/// <para>Main thread (or the headless tick) only; nothing allocates per step once warm, except for an object whose parts
/// have physics BSPs, whose posed parts are rebuilt when it moves.</para>
/// </remarks>
public sealed partial class PhysicsObj : IMotionInterpHost, IMoveToHost
{
    /// <summary>Retail <c>dummy_sphere</c>: the sphere a transition of an object without spheres uses.</summary>
    private static readonly PhysSphere[] DummySpheres =
        [new PhysSphere(new Vector3(0f, 0f, PhysicsGlobals.DummySphereRadius), PhysicsGlobals.DummySphereRadius)];

    private PhysPart[] _parts = [];
    private PhysCell?[] _shadowCells = new PhysCell?[4];
    private uint[] _shadowIds = new uint[4];
    private int _shadowCount;

    /// <summary>
    /// Retail <c>CPhysicsObj::makeObject</c> for a dynamic object (11.4186 0x00513970, 11.6096 0x00514470: the
    /// <c>CPhysicsObj</c> constructor, 0x005123F0 / 0x00512EF0, then <c>InitObjectBegin</c>, the part array and
    /// <c>CacheHasPhysicsBSP</c>), with a motion table as <c>InitDefaults</c> gives one (<c>SetMotionTableID</c>, which
    /// makes the movement manager and enters its default state).
    /// </summary>
    /// <param name="world">The world the object lives in.</param>
    /// <param name="setup">Its Setup's physics, or null for an object without parts (a transition then uses the dummy sphere).</param>
    /// <param name="id">Its object id (the server's guid), or 0 for an object only the client knows.</param>
    /// <param name="weenie">Its weenie, or null.</param>
    /// <param name="motionTable">Its motion table's player, or null for an object that does not animate.</param>
    /// <param name="scale">Its scale (retail <c>m_scale</c>).</param>
    public PhysicsObj(PhysicsWorld world, PhysicsSetup? setup, uint id = 0, IPhysicsWeenie? weenie = null, MotionPlayer? motionTable = null, float scale = 1f)
    {
        ArgumentNullException.ThrowIfNull(world);
        World = world;
        Setup = setup;
        Id = id;
        Weenie = weenie;
        Scale = scale;
        State = PhysicsState.DefaultForNewObject;
        UpdateTime = world.CurrentTime;
        if (setup is not null && setup.HasPhysicsBsp)
        {
            State |= PhysicsState.HasPhysicsBsp;
        }

        world.Register(this);
        if (motionTable is not null)
        {
            MotionPlayer = motionTable;
            MakeMovementManager(initMotion: true);
        }
    }

    /// <summary>The world the object lives in.</summary>
    public PhysicsWorld World { get; }

    /// <summary>The object id (retail <c>id</c>), or 0.</summary>
    public uint Id { get; }

    /// <summary>The Setup's physics, or null for an object without parts.</summary>
    public PhysicsSetup? Setup { get; }

    /// <summary>The object's weenie (retail <c>weenie_obj</c>), or null.</summary>
    public IPhysicsWeenie? Weenie { get; private set; }

    /// <summary>Where collisions and animation hooks are reported (in place of retail's weenie calls), or null.</summary>
    public IPhysicsEvents? Events { get; set; }

    /// <summary>The object's motion table player (retail <c>CPartArray</c>'s motion table and sequence), or null.</summary>
    public MotionPlayer? MotionPlayer { get; }

    /// <summary>The motion interpreter (retail <c>movement_manager-&gt;motion_interpreter</c>), or null without a motion table.</summary>
    public MotionInterpreter? Motion { get; private set; }

    /// <summary>
    /// The movement manager (retail <c>movement_manager</c>): <see cref="Motion"/> and the MoveTo manager it makes for the
    /// first server MoveTo or TurnTo; null when <see cref="Motion"/> is.
    /// </summary>
    public MovementManager? Movement { get; private set; }

    /// <summary>The position manager (retail <c>position_manager</c>), or null until the object first needs one.</summary>
    public PositionManager? Positions { get; private set; }

    /// <summary>The object's position (retail <c>m_position</c>).</summary>
    public CellFrame Position { get; private set; } = new(0, AcFrame.Identity);

    /// <summary>The cell the object is in (retail <c>cell</c>), or null outside the world.</summary>
    public PhysCell? Cell { get; private set; }

    /// <summary>The object's state bits (retail <c>state</c>).</summary>
    public PhysicsState State { get; private set; }

    /// <summary>The object's transient state (retail <c>transient_state</c>).</summary>
    public TransientState TransientState { get; private set; }

    /// <summary>The object's scale (retail <c>m_scale</c>).</summary>
    public float Scale { get; }

    /// <summary>The physics velocity in world axes (retail <c>m_velocityVector</c>): gravity, jumps and bounces; walking is root motion.</summary>
    public Vector3 Velocity { get; private set; }

    /// <summary>The acceleration (retail <c>m_accelerationVector</c>): gravity off walkable ground.</summary>
    public Vector3 Acceleration { get; private set; }

    /// <summary>The angular velocity in world axes (retail <c>m_omegaVector</c>).</summary>
    public Vector3 Omega { get; private set; }

    /// <summary>
    /// The velocity the last step actually moved the object at (retail <c>cached_velocity</c>, which
    /// <c>CPhysicsObj::get_velocity</c> answers): the transition's displacement over the quantum.
    /// </summary>
    public Vector3 CachedVelocity { get; private set; }

    /// <summary>The plane the object rests on (retail <c>contact_plane</c>), in its cell's landblock space; valid with <see cref="HasContact"/>.</summary>
    public Plane ContactPlane { get; private set; }

    /// <summary>The cell <see cref="ContactPlane"/> belongs to (retail <c>contact_plane_cell_id</c>).</summary>
    public uint ContactPlaneCellId { get; private set; }

    /// <summary>The horizontal normal the object slides along (retail <c>sliding_normal</c>); valid with <see cref="TransientState.Sliding"/>.</summary>
    public Vector3 SlidingNormal { get; private set; }

    /// <summary>Retail <c>friction</c> (<see cref="PhysicsGlobals.DefaultFriction"/>).</summary>
    public float Friction { get; set; } = PhysicsGlobals.DefaultFriction;

    /// <summary>Retail <c>elasticity</c> (<see cref="PhysicsGlobals.DefaultElasticity"/>).</summary>
    public float Elasticity { get; set; } = PhysicsGlobals.DefaultElasticity;

    /// <summary>When the object was last stepped to, in <see cref="PhysicsWorld.CurrentTime"/> seconds (retail <c>update_time</c>).</summary>
    public double UpdateTime { get; private set; }

    /// <summary>The offset from the player to the object at its last update (retail <c>player_vector</c>).</summary>
    public Vector3 PlayerVector { get; private set; } = Vector3.UnitZ;

    /// <summary>The distance from the player at its last update (retail <c>player_distance</c>; float.MaxValue until known).</summary>
    public float PlayerDistance { get; private set; } = float.MaxValue;

    /// <summary>Retail <c>jumped_this_frame</c>: the velocity was set during this frame's update.</summary>
    public bool JumpedThisFrame { get; private set; }

    /// <summary>Retail <c>last_move_was_autonomous</c>: the last movement was the client's own (<see cref="DoMotion"/>).</summary>
    public bool LastMoveWasAutonomous { get; set; }

    /// <summary>Retail <c>colliding_with_environment</c>: an environment collision has been reported and has not ended.</summary>
    public bool CollidingWithEnvironment { get; private set; }

    /// <summary>
    /// True while the object waits for its landblock (retail's lost cell: a placement or a step into a cell whose
    /// landblock is not attached stores the position, and <see cref="PhysicsWorld.Attach"/> places it again).
    /// </summary>
    public bool PlacementPending { get; private set; }

    /// <summary>Retail transient Contact.</summary>
    public bool HasContact => (TransientState & TransientState.Contact) != 0;

    /// <summary>Retail transient OnWalkable.</summary>
    public bool OnWalkable => (TransientState & TransientState.OnWalkable) != 0;

    /// <summary>Retail transient Active: <see cref="UpdateObject"/> moves the object.</summary>
    public bool IsActive => (TransientState & TransientState.Active) != 0;

    /// <summary>The object is the world's player (retail <c>this == CPhysicsObj::player_object</c>).</summary>
    public bool IsThePlayer => ReferenceEquals(World.Player, this);

    /// <summary>The cells the object's shadows are in (retail <c>shadow_objects</c>), in the order they were found.</summary>
    public int ShadowCellCount => _shadowCount;

    /// <summary>The object's posed parts with physics BSPs, for an object whose state has HasPhysicsBsp.</summary>
    internal ReadOnlySpan<PhysPart> Parts => _parts;

    /// <inheritdoc/>
    public override string ToString() => $"PhysicsObj 0x{Id:X8} at {Position}";

    /// <summary>The id of the cell of the <paramref name="index"/>th shadow.</summary>
    public uint ShadowCellIdAt(int index) => _shadowIds[index];

    /// <summary>
    /// Sets the weenie (retail <c>CPhysicsObj::set_weenie_obj_ptr</c>, 11.4186 0x0050F4D0, 11.6096 0x0050FFA0, and
    /// <c>MovementManager::SetWeenieObject</c>, which re-applies the current movement).
    /// </summary>
    public void SetWeenie(IPhysicsWeenie? weenie)
    {
        Weenie = weenie;
        Motion?.OnWeenieChanged();
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::set_state</c> (11.4186 0x00514DD0, 11.6096 0x005158D0): the new state; a change of Hidden
    /// hides or shows the object (<see cref="SetHidden"/>). LightingOn and NoDraw are the renderer's (their part-array
    /// side, lights and the no-draw flag, is not physics).
    /// </summary>
    public void SetState(PhysicsState newState)
    {
        var changed = newState ^ State;
        State = newState;
        if ((changed & PhysicsState.Hidden) != 0)
        {
            SetHidden((State & PhysicsState.Hidden) != 0);
        }
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::set_hidden</c> (11.4186 0x00514C60, 11.6096 0x00515760): a hidden object neither animates nor
    /// moves (<c>UpdatePositionInternal</c>) and is ignored by collisions; hiding ends its collisions and stops reporting,
    /// showing it again reports the collisions it is still in. Either way the motion table drops its link animations
    /// (<c>CPartArray::HandleEnterWorld</c>). The hide/unhide scripts and the children are not physics.
    /// </summary>
    public void SetHidden(bool hidden)
    {
        if (hidden)
        {
            State |= PhysicsState.Hidden;
            if ((State & PhysicsState.ReportCollisions) != 0)
            {
                ReportCollisionEnd(forceEnd: true);
                State &= ~PhysicsState.ReportCollisions;
            }

            State |= PhysicsState.IgnoreCollisions;
            MotionPlayer?.RemoveLinkAnimations();
        }
        else
        {
            State &= ~(PhysicsState.Hidden | PhysicsState.IgnoreCollisions);
            if ((State & PhysicsState.ReportCollisions) == 0)
            {
                State |= PhysicsState.ReportCollisions;
                ReportCollisionStart();
            }

            MotionPlayer?.RemoveLinkAnimations();
        }
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::set_ethereal</c> (11.4186 0x00511C40, 11.6096 0x00512710). Becoming ethereal always works.
    /// Becoming solid fails while something overlaps the object (<c>ethereal_check_for_collisions</c>): it then stays
    /// ethereal and tries again on each update (CheckEthereal).
    /// </summary>
    /// <returns>True when the object now has the requested state.</returns>
    public bool SetEthereal(bool ethereal)
    {
        if (ethereal)
        {
            State |= PhysicsState.Ethereal;
            TransientState &= ~TransientState.CheckEthereal;
            return true;
        }

        State &= ~PhysicsState.Ethereal;
        if (Cell is null || !EtherealCheckForCollisions())
        {
            TransientState &= ~TransientState.CheckEthereal;
            return true;
        }

        State |= PhysicsState.Ethereal;
        TransientState |= TransientState.CheckEthereal;
        return false;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::set_velocity</c> (11.4186 0x005113F0, 11.6096 0x00511EC0): a changed velocity is capped at
    /// <see cref="PhysicsGlobals.MaxVelocity"/> and marks <see cref="JumpedThisFrame"/>; a dynamic object becomes active.
    /// </summary>
    /// <remarks>Retail's inequality (<c>Vector3::operator!=</c>, 11.4186 0x0045F9F0, 11.6096 0x0045FAD0) is a component more than the epsilon
    /// apart, so a change within the epsilon is ignored. The decompile keeps only the flags of the cap's comparison;
    /// ACE's reading (the squared length above the squared cap) is the one consistent with
    /// <c>UpdatePhysicsInternal</c>'s explicit cap.</remarks>
    public void SetVelocity(Vector3 velocity)
    {
        if (!RetailEqual(velocity, Velocity))
        {
            Velocity = velocity;
            if (velocity.LengthSquared() > PhysicsGlobals.MaxVelocitySquared)
            {
                Velocity = Vector3.Normalize(velocity) * PhysicsGlobals.MaxVelocity;
            }

            JumpedThisFrame = true;
        }

        if ((State & PhysicsState.Static) == 0)
        {
            SetActiveFlag();
        }
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::set_local_velocity</c> (11.4186 0x005114D0, 11.6096 0x00511FA0): the velocity from the
    /// object's local axes.
    /// </summary>
    public void SetLocalVelocity(Vector3 localVelocity) => SetVelocity(Position.Frame.LocalToGlobalVec(localVelocity));

    /// <summary>
    /// Retail <c>CPhysicsObj::get_local_physics_velocity</c> (11.4186 0x00512140, 11.6096 0x00512C10): the physics velocity
    /// in the object's local axes.
    /// </summary>
    public Vector3 GetLocalPhysicsVelocity() => Position.Frame.GlobalToLocalVec(Velocity);

    /// <summary>Retail <c>CPhysicsObj::set_omega</c> (11.4186 0x0050F6D0, 11.6096 0x005101A0).</summary>
    public void SetOmega(Vector3 omega) => Omega = omega;

    /// <summary>
    /// Retail <c>CPhysicsObj::set_heading</c> (11.4186 0x00514160, 11.6096 0x00514C60): turns the object to
    /// <paramref name="degrees"/> (0 north, clockwise) about the vertical.
    /// </summary>
    public void SetHeading(float degrees) => SetFrame(RetailHeadings.SetHeading(Position.Frame, degrees));

    /// <summary>
    /// Retail <c>CPhysicsObj::set_active</c> (11.4186 0x0050FC40, 11.6096 0x00510710): a static object cannot be active;
    /// an object that becomes active starts its clock now. False for a static object asked to be active.
    /// </summary>
    public bool SetActive(bool active)
    {
        if (!active)
        {
            TransientState &= ~TransientState.Active;
            return true;
        }

        if ((State & PhysicsState.Static) != 0)
        {
            return false;
        }

        SetActiveFlag();
        return true;
    }

    /// <summary>The active half of <see cref="SetActive"/>, which several retail functions repeat inline.</summary>
    private void SetActiveFlag()
    {
        if ((TransientState & TransientState.Active) == 0)
        {
            UpdateTime = World.CurrentTime;
        }

        TransientState |= TransientState.Active;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::set_frame</c> (11.4186 0x00514090, 11.6096 0x00514B90): the frame, and the posed parts with
    /// it. (Retail first repairs a frame whose rotation alone is invalid; Lifestone's frames come from transitions and the
    /// server, normalised.)
    /// </summary>
    private void SetFrame(in AcFrame frame)
    {
        Position = new CellFrame(Position.CellId, frame);
        UpdateParts();
    }

    /// <summary>
    /// Retail <c>CPartArray::SetFrame</c> → <c>UpdateParts</c> for the collision parts: each part with a physics BSP posed
    /// at the object's frame, its rest frame scaled by the object's scale.
    /// </summary>
    private void UpdateParts()
    {
        if ((State & PhysicsState.HasPhysicsBsp) == 0 || Setup is null)
        {
            return;
        }

        var setupParts = Setup.PartSpan;
        if (_parts.Length != setupParts.Length)
        {
            _parts = new PhysPart[setupParts.Length];
        }

        for (var i = 0; i < setupParts.Length; i++)
        {
            var part = setupParts[i];
            var frame = AcFrame.Combine(Position.Frame, new AcFrame(part.RestFrame.Origin * Scale, part.RestFrame.Orientation));
            _parts[i] = new PhysPart(new CellFrame(Position.CellId, frame), part.Shape, part.DefaultScale * Scale, part.Always2D);
        }
    }

    /// <summary>The spheres a transition of this object uses (retail <c>CPartArray::GetSphere</c>, or <c>dummy_sphere</c>).</summary>
    private ReadOnlySpan<PhysSphere> TransitionSpheres(out float scale)
    {
        if (Setup is not null && Setup.Spheres.Count > 0)
        {
            scale = Scale;
            return Setup.SphereSpan;
        }

        scale = 1f;
        return DummySpheres;
    }

    /// <summary>Retail <c>CPhysicsObj::GetStepUpHeight</c> (11.4186 0x0050EA00, 11.6096 0x0050F4D0): the Setup's, scaled; 0 without parts.</summary>
    public float GetStepUpHeight() => Setup is null ? 0f : Setup.StepUpHeight * Scale;

    /// <summary>Retail <c>CPhysicsObj::GetStepDownHeight</c> (11.4186 0x0050EA20, 11.6096 0x0050F4F0): the Setup's, scaled; 0 without parts.</summary>
    public float GetStepDownHeight() => Setup is null ? 0f : Setup.StepDownHeight * Scale;

    /// <summary>
    /// Retail <c>CPhysicsObj::get_object_info</c> (11.4186 0x00511CC0, 11.6096 0x00512790) and <c>OBJECTINFO::init</c>
    /// (0x0050CF30, 0x0050DA00): the transition's flags (EdgeSlide; unless an admin move, the carried contact plane while
    /// the velocity does not leave it (<c>check_contact</c>) and the sliding normal; FreeRotate for a Setup that allows a
    /// free heading; PathClipped for a missile; the weenie's Impenetrable, Player, PK and PK-lite), then the object, its
    /// scale and step heights.
    /// </summary>
    private void InitObject(Transition transition, bool admin)
    {
        var flags = ObjectInfoState.None;
        if ((State & PhysicsState.EdgeSlide) != 0)
        {
            flags |= ObjectInfoState.EdgeSlide;
        }

        if (!admin)
        {
            if (HasContact)
            {
                var isWater = (TransientState & TransientState.WaterContact) != 0;
                if (CheckContact(true))
                {
                    transition.InitContactPlane(ContactPlaneCellId, ContactPlane, isWater);
                    flags |= ObjectInfoState.Contact;
                    if (OnWalkable)
                    {
                        flags |= ObjectInfoState.OnWalkable;
                    }
                }
                else
                {
                    transition.InitLastKnownContactPlane(ContactPlaneCellId, ContactPlane, isWater);
                }
            }

            if ((TransientState & TransientState.Sliding) != 0)
            {
                transition.InitSlidingNormal(SlidingNormal);
            }
        }

        if (Setup is not null && Setup.AllowsFreeHeading)
        {
            flags |= ObjectInfoState.FreeRotate;
        }

        if ((State & PhysicsState.Missile) != 0)
        {
            flags |= ObjectInfoState.PathClipped;
        }

        InitObjectInfo(transition, flags);
    }

    /// <summary>
    /// Retail <c>CTransition::init_object</c> → <c>OBJECTINFO::init</c> (11.4186 0x0050CF30, 11.6096 0x0050DA00): the
    /// object, the flags with its weenie's (slot 10 Impenetrable, 4 Player, 8 PK, 9 PK-lite), its scale and step heights.
    /// </summary>
    private void InitObjectInfo(Transition transition, ObjectInfoState flags)
    {
        if (Weenie is { } weenie)
        {
            if (weenie.IsImpenetrable)
            {
                flags |= ObjectInfoState.IsImpenetrable;
            }

            if (weenie.IsPlayer)
            {
                flags |= ObjectInfoState.IsPlayer;
            }

            if (weenie.IsPK)
            {
                flags |= ObjectInfoState.IsPK;
            }

            if (weenie.IsPKLite)
            {
                flags |= ObjectInfoState.IsPKLite;
            }
        }

        transition.InitObject(this, flags, State, Scale, GetStepUpHeight(), GetStepDownHeight());
        var spheres = TransitionSpheres(out var scale);
        transition.InitSphere(spheres, scale);
    }

    /// <summary>
    /// Retail <c>AC1Legacy::Vector3::operator==</c> (11.4186 0x00509F10, 11.6096 0x0050A9E0) and the negation of
    /// <c>Vector3::operator!=</c>: every component within the epsilon.
    /// </summary>
    internal static bool RetailEqual(Vector3 a, Vector3 b) =>
        Math.Abs((double)a.X - b.X) <= PhysicsGlobals.Epsilon
        && Math.Abs((double)a.Y - b.Y) <= PhysicsGlobals.Epsilon
        && Math.Abs((double)a.Z - b.Z) <= PhysicsGlobals.Epsilon;

    /// <summary>Retail <c>AC1Legacy::Vector3::is_zero</c> (11.4186 0x0050E7B0, 11.6096 0x0050F280): every component below the epsilon.</summary>
    internal static bool RetailIsZero(Vector3 v) =>
        Math.Abs(v.X) < PhysicsGlobals.Epsilon && Math.Abs(v.Y) < PhysicsGlobals.Epsilon && Math.Abs(v.Z) < PhysicsGlobals.Epsilon;

    /// <summary>
    /// Retail <c>CPhysicsObj::check_contact</c> (11.4186 0x0050F5B0, 11.6096 0x00510080): the carried contact holds unless
    /// the object is moving away from its contact plane faster than the epsilon.
    /// </summary>
    private bool CheckContact(bool contact) =>
        (!HasContact || Vector3.Dot(Velocity, ContactPlane.Normal) <= PhysicsGlobals.Epsilon) && contact;
}
