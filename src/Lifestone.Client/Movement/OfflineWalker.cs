using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Physics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement;

/// <summary>
/// The explore-mode player without a server (m2-plan PH6, owner decision on M1's CellWalker placeholder): a human
/// (Setup 0x02000001, motion table 0x09000001) as a <see cref="PhysicsObj"/> in a <see cref="PhysicsWorld"/>, walking,
/// running, turning, jumping and falling with retail's motion interpreter and collision. Its weenie is
/// <see cref="OfflinePlayerWeenie"/>, whose Run and Jump skills are placeholders (logged when the walker is made).
/// </summary>
/// <remarks>
/// <para><b>Use.</b> <see cref="Create"/> it, attach collision around it (the host's <see cref="CollisionStreamer"/>, or
/// <see cref="PhysicsWorld.Attach"/>), <see cref="Place"/> it, then each frame either give the keymap's intents to its
/// <see cref="Controller"/> (retail's command interpreter and jump bar, PH7) and tick that, or give it motion commands
/// directly (<see cref="DoMotion"/>, <see cref="StopMotion"/>, <see cref="SetHoldRun"/>, <see cref="Jump"/>) and
/// <see cref="Tick"/> it. Tick one or the other each frame, not both: each runs the world's 30 Hz gate.</para>
/// <para>The client controls this player entirely, so its last move is always autonomous: the motion interpreter
/// applies its raw (input) state.</para>
/// <para>Main thread (or the headless tick) only.</para>
/// </remarks>
public sealed class OfflineWalker
{
    /// <summary>The human Setup (m2-plan PH6).</summary>
    public const uint HumanSetupId = 0x02000001;

    /// <summary>The human motion table.</summary>
    public const uint HumanMotionTableId = 0x09000001;

    private const string LogCategory = "movement";

    private long _enteredSeen;

    private OfflineWalker(PhysicsWorld world, PhysicsObj physics, IPhysicsWeenie weenie, IMovementSink sink, MovementOptions options, ILogSink log)
    {
        World = world;
        Physics = physics;
        Weenie = weenie;
        Body = new PhysicsPlayerBody(physics);
        Motion = Body.Motion;
        Sink = sink;
        Controller = new PlayerMovementController(physics, Sink, options, log);
        ((PhysicsPlayerBody)Body).ServerControl = Controller;
    }

    /// <summary>The world the walker moves in.</summary>
    public PhysicsWorld World { get; }

    /// <summary>The walker's physics object (the world's <see cref="PhysicsWorld.Player"/>).</summary>
    public PhysicsObj Physics { get; }

    /// <summary>Its motion interpreter.</summary>
    public MotionInterpreter Motion { get; }

    /// <summary>Its weenie: <see cref="OfflinePlayerWeenie"/> (placeholder skills) or, online, the session's <c>QualitiesWeenie</c>.</summary>
    public IPhysicsWeenie Weenie { get; }

    /// <summary>The walker as the avatars track draws a player body.</summary>
    public IPlayerBody Body { get; }

    /// <summary>
    /// The keymap's controller in front of the walker (retail's command interpreter and jump bar): the explore mode's
    /// <see cref="IPlayerMovementIntents"/>. Its movement messages go to <see cref="Sink"/>, which sends nothing.
    /// </summary>
    public PlayerMovementController Controller { get; }

    /// <summary>Where <see cref="Controller"/>'s movement messages go: by default nowhere (a <see cref="NullMovementSink"/> counts them).</summary>
    public IMovementSink Sink { get; }

    /// <summary>The walker's cell and frame after the last physics step.</summary>
    public CellFrame Position => Physics.Position;

    /// <summary>True while the walker stands on walkable ground (retail transient Contact and OnWalkable).</summary>
    public bool OnGround => Physics.HasContact && Physics.OnWalkable;

    /// <summary>
    /// Makes the walker in <paramref name="world"/> and makes it the world's player. The human Setup's collision comes from
    /// <paramref name="geometry"/> (or a new library), its animations from <paramref name="animations"/> (or a new
    /// source). A host that draws the walker with the avatars track gives the player avatar's part array as
    /// <paramref name="parts"/> (<c>PlayerAvatar.Parts</c>, made from the same motion table) and the player's guid as
    /// <paramref name="id"/>: the physics object then hosts its interpreter on the part array the avatar draws, as the avatars
    /// track's binding asks (docs/avatars.md §5.3), and is the object other objects' target ids name. The player's
    /// <paramref name="weenie"/> is the session's <c>QualitiesWeenie</c> online; null gives explore mode's
    /// <see cref="OfflinePlayerWeenie"/> with its placeholder skills (a WARNING says so).
    /// </summary>
    /// <exception cref="InvalidDataException">The dats have no human Setup or motion table.</exception>
    public static OfflineWalker Create(
        DatSet dats,
        PhysicsWorld world,
        ILogSink log,
        IPhysicsWeenie? weenie = null,
        PhysicsGeometryLibrary? geometry = null,
        DatAnimationSource? animations = null,
        MotionPlayer? parts = null,
        uint id = 0,
        IMovementSink? sink = null,
        MovementOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(log);
        var setup = PhysicsSetup.Load(dats, HumanSetupId, geometry)
            ?? throw new InvalidDataException($"The human Setup 0x{HumanSetupId:X8} is missing or does not load.");
        if (parts is null)
        {
            animations ??= new DatAnimationSource(dats);
            var table = animations.GetMotionTable(HumanMotionTableId)
                ?? throw new InvalidDataException($"The human motion table 0x{HumanMotionTableId:X8} is missing.");
            parts = new MotionPlayer(table, animations);
        }

        if (weenie is null)
        {
            var placeholder = new OfflinePlayerWeenie();
            weenie = placeholder;
            log.Warn(
                LogCategory,
                "this player has no character data in the walker (explore mode; online the session's QualitiesWeenie is given): its Run and Jump skills are placeholders",
                new LogField("run", placeholder.RunSkill),
                new LogField("jump", placeholder.JumpSkill));
        }

        var physics = new PhysicsObj(world, setup, id, weenie, parts)
        {
            LastMoveWasAutonomous = true,
        };
        world.SetPlayer(physics);
        return new OfflineWalker(world, physics, weenie, sink ?? new NullMovementSink(), options ?? MovementOptions.Default, log);
    }

    /// <summary>
    /// A walker over a physics object already made (tests without the dats: a synthetic human Setup and motion table, made the
    /// world's player by the caller). Internal: the hosts use <see cref="Create"/>.
    /// </summary>
    internal static OfflineWalker FromPhysics(PhysicsWorld world, PhysicsObj physics, IPhysicsWeenie weenie, ILogSink log, IMovementSink? sink = null, MovementOptions? options = null) =>
        new(world, physics, weenie, sink ?? new NullMovementSink(), options ?? MovementOptions.Default, log);

    /// <summary>
    /// Puts the walker at <paramref name="at"/> (retail <c>CPhysicsObj::enter_world</c>: a sliding placement that sets it
    /// down within its step-down height). When <paramref name="at"/>'s landblock is not attached yet, the walker waits
    /// (<see cref="PhysicsObj.PlacementPending"/>) and is placed when it is.
    /// </summary>
    /// <returns>False when no valid position was found there.</returns>
    public bool Place(in CellFrame at) => Physics.EnterWorld(at, slide: true);

    /// <summary>
    /// The placements of <see cref="ReplaceAfterObjectCreate"/> that found no valid position (M5 item 5 F5), so the walker stayed where it was: retail's
    /// <c>CTransition::find_placement_position</c> (11.6096 0x0050CC40) refuses a spot it cannot validate before any sliding search, as inside a building's hull
    /// in an outdoor cell; without this count such a refusal read as "clear".
    /// </summary>
    public int ReplacementsRefused { get; private set; }

    /// <summary>
    /// The hosts' per-frame check (M5 item 5 F1; the headless body and the game's player controller both call it): when the server-object host put an object into the
    /// world since the last call (<see cref="ServerObjectPhysicsStats.EnteredWorld"/>: a create, a recreate, a return, a forced placement; the host's own placement does
    /// not meet the player, <see cref="PhysicsObj.EnterWorldPastPlayer"/>), a walker placed in a cell is put out of whatever now overlaps it
    /// (<see cref="ReplaceAfterObjectCreate"/>). A walker not yet placed (waiting for its landblock) is placed with the objects there, so the count is only taken.
    /// Allocates nothing.
    /// </summary>
    /// <param name="objects">The host's counters, or null when no host keeps server objects.</param>
    /// <returns>The metres the walker moved (0 when nothing entered, when it is not placed, or when it was clear).</returns>
    public float ReplaceAfterObjectsEntered(ServerObjectPhysicsStats? objects)
    {
        var entered = objects?.EnteredWorld ?? 0;
        if (entered == _enteredSeen)
        {
            return 0f;
        }

        _enteredSeen = entered;
        if (Physics.Cell is null || Physics.PlacementPending)
        {
            return 0f;
        }

        return ReplaceAfterObjectCreate();
    }

    /// <summary>
    /// Puts a grounded walker out of any server object that appeared on it (M5 item 5 l1 and F1, <c>TODO(m5-create-overlap)</c>): the same sliding placement at its own position
    /// (<c>CPhysicsObj::SetPosition</c> with Placement and Slide, the one <c>CPhysicsObj::enter_world</c>, 11.6096 0x00516E10, and so <see cref="Place"/>, runs). Retail's player is placed with
    /// the objects of its cell already created, so its placement slides it out of them; this session can create an object after the teleport (a lifestone after a recall, a creature
    /// coming into view on the player's spot; the transition then finds the walker already in the object and holds it: measured, 0.00 m in four headings), so the body is placed
    /// again when a collision object appears. Nothing moves when nothing overlaps it; the walker's position is the body's only, the server's stays where the server put the player.
    /// </summary>
    /// <returns>The metres the walker moved (0 when it did not, when it is in the air, or when no valid position was found: then counted in
    /// <see cref="ReplacementsRefused"/>).</returns>
    public float ReplaceAfterObjectCreate()
    {
        if (!OnGround)
        {
            return 0f;
        }

        var before = Physics.Position;
        if (Physics.SetPosition(before, SetPositionFlags.Placement | SetPositionFlags.Slide) != SetPositionError.OK)
        {
            ReplacementsRefused++;
            return 0f;
        }

        return Vector3.Distance(Physics.Position.Frame.Origin, before.Frame.Origin);
    }

    /// <summary>
    /// One frame at <paramref name="now"/> (the host's monotonic seconds): when retail's 30 Hz gate lets physics run,
    /// the walker is stepped (<see cref="PhysicsObj.UpdateObject"/>).
    /// </summary>
    /// <returns>True when physics ran this frame.</returns>
    public bool Tick(double now)
    {
        if (!World.UseTime(now))
        {
            return false;
        }

        Physics.UpdateObject();
        return true;
    }

    /// <summary>
    /// Starts a motion command (retail <c>CPhysicsObj::DoMotion</c> with default parameters at <paramref name="speed"/>):
    /// WalkForward, WalkBackwards, TurnRight, TurnLeft, SideStepRight, SideStepLeft, Ready, an emote or an action.
    /// </summary>
    public WeenieError DoMotion(uint motion, float speed = 1f) =>
        Physics.DoMotion(motion, new MovementParameters() with { Speed = speed });

    /// <summary>Stops a motion command (retail <c>CPhysicsObj::StopMotion</c> with default parameters).</summary>
    public WeenieError StopMotion(uint motion) => Physics.StopMotion(motion, new MovementParameters());

    /// <summary>Holds or releases the run key (retail <c>CMotionInterp::set_hold_run</c>): held, walking forward runs.</summary>
    public void SetHoldRun(bool run) => Motion.SetHoldRun(run, cancelMoveTo: true);

    /// <summary>
    /// Jumps with the jump bar at <paramref name="extent"/> in [0, 1] (<see cref="PhysicsObj.Jump"/>): the jump speed comes
    /// from the weenie's placeholder Jump skill.
    /// </summary>
    public WeenieError Jump(float extent) => Physics.Jump(extent, out _);
}
