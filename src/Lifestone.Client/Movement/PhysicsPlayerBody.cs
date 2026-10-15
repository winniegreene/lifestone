using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement;

/// <summary>
/// The player's <see cref="PhysicsObj"/> as the avatars track reads a player body (m2-plan §4.9): its position, contact
/// and the motion interpreter it hosts (<see cref="IPlayerBody"/>; the player avatar, AV10, draws the player from it), the
/// server's movements for the player (<see cref="IPlayerMovementBody"/>: an emote, or the MoveTo after an out-of-range
/// Use, m2-plan DoD 9), and the player as other objects target it (<see cref="ITargetable"/>: a creature chasing the
/// player gets its reports from the physics object's update).
/// </summary>
/// <remarks>
/// <para><b>Use.</b> The host makes the physics object with the player's guid, gives this body to the avatar set
/// (<c>AvatarSet.PlayerBody</c>) and the avatar set to the physics object (<see cref="PhysicsObj.TargetWorld"/>), so that
/// target ids on both sides name the same objects.</para>
/// <para>Every member reads or calls the physics object; the body keeps only the state it unpacks server movements into.
/// Main thread (or the headless tick) only.</para>
/// </remarks>
public sealed class PhysicsPlayerBody : IPlayerMovementBody, ITargetable
{
    private readonly InterpretedMotionState _scratch = new();

    /// <summary>A body over <paramref name="physics"/>, which must host a motion interpreter.</summary>
    /// <exception cref="ArgumentException"><paramref name="physics"/> has no motion table.</exception>
    public PhysicsPlayerBody(PhysicsObj physics)
    {
        ArgumentNullException.ThrowIfNull(physics);
        Physics = physics;
        Motion = physics.Motion ?? throw new ArgumentException("the player's physics object has no motion table", nameof(physics));
        Movement = physics.Movement!; // made with the interpreter (MakeMovementManager)
    }

    /// <summary>The player's physics object.</summary>
    public PhysicsObj Physics { get; }

    /// <summary>
    /// The command interpreter the server's MoveTo and TurnTo take control from: <see cref="ApplyServerMovement"/> calls its
    /// <see cref="PlayerMovementController.LoseControlToServer"/> first, as <c>ACSmartBox::DispatchSmartBoxEvent</c> (11.4186 0x005595D0,
    /// 11.6096 0x0055A210) does for a server movement of the player (it calls retail <c>CommandInterpreter::LoseControlToServer</c>,
    /// 11.4186 0x006B3170, 11.6096 0x006B40B0). Null for a body nobody controls by keys.
    /// </summary>
    public PlayerMovementController? ServerControl { get; set; }

    /// <inheritdoc/>
    public CellFrame Position => Physics.Position;

    /// <inheritdoc/>
    public bool HasContact => Physics.HasContact;

    /// <inheritdoc/>
    public bool OnWalkable => Physics.OnWalkable;

    /// <inheritdoc/>
    public MotionInterpreter Motion { get; }

    /// <inheritdoc/>
    public MovementManager Movement { get; }

    /// <inheritdoc/>
    public uint Id => Physics.Id;

    /// <inheritdoc/>
    /// <remarks>The physics object's measured velocity (<see cref="PhysicsObj.CachedVelocity"/>, retail <c>get_velocity</c>).</remarks>
    public Vector3 Velocity => Physics.CachedVelocity;

    /// <inheritdoc/>
    public float Radius => Physics.Radius;

    /// <inheritdoc/>
    public float Height => Physics.Height;

    /// <inheritdoc/>
    public uint TopLevelId => Physics.Id;

    /// <inheritdoc/>
    public double Time => Physics.World.CurrentTime;

    /// <inheritdoc/>
    public TargetManager Targets => Physics.Targets;

    /// <inheritdoc/>
    public void HandleUpdateTarget(in TargetInfo info) => Physics.HandleUpdateTarget(info);

    /// <inheritdoc/>
    /// <remarks>
    /// Retail <c>CPhysics::SetObjectMovement</c> (11.4186 0x00509690, 11.6096 0x0050A160) after the stamps: the autonomous
    /// bit becomes the last move's (<see cref="PhysicsObj.LastMoveWasAutonomous"/>), then
    /// <see cref="PhysicsObj.UnpackMovement"/>. A MoveTo or TurnTo then runs in the physics object's update.
    /// </remarks>
    public MovementUnpackResult ApplyServerMovement(in WireMovement movement, ReadOnlySpan<WireMotionItem> actions, bool autonomous, out WireMotionFailure failure)
    {
        if ((int)movement.Type is >= (int)WireMovementType.MoveToObject and <= (int)WireMovementType.TurnToHeading)
        {
            ServerControl?.LoseControlToServer(); // autorun off, a pending jump dropped: the server walks or turns the player now
        }

        Physics.LastMoveWasAutonomous = autonomous;
        return Physics.UnpackMovement(movement, actions, _scratch, out failure);
    }
}
