using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Client.Avatars;

/// <summary>
/// The player's body as the avatars read it (m2-plan §4.9): where it is, whether it stands on walkable ground, and the
/// motion interpreter that plays its animations. The physics track's player object implements it (it hosts the
/// interpreter as retail's <c>CPhysicsObj</c> hosts <c>CMotionInterp</c>, through <see cref="IMotionInterpHost"/>);
/// <c>KinematicPlayerBody</c> (AV10) is the test double. The player avatar (AV10) draws the player from it.
/// </summary>
public interface IPlayerBody
{
    /// <summary>The player's position (retail <c>m_position</c>).</summary>
    CellFrame Position { get; }

    /// <summary>Retail <c>transient_state</c> Contact.</summary>
    bool HasContact { get; }

    /// <summary>Retail <c>transient_state</c> OnWalkable.</summary>
    bool OnWalkable { get; }

    /// <summary>The player's motion interpreter.</summary>
    MotionInterpreter Motion { get; }
}

/// <summary>
/// A player body that also takes the server's movements for the player (a non-autonomous UpdateMotion: an emote, an
/// action, or the MoveTo the server sends after an out-of-range Use, m2-plan DoD 9): retail's player
/// <c>CPhysicsObj</c> unpacks them into its own movement manager (<c>CPhysics::SetObjectMovement</c>, 11.4186
/// 0x00509690, 11.6096 0x0050A160). The avatar set hands them to it; without one they are counted and dropped.
/// </summary>
public interface IPlayerMovementBody : IPlayerBody
{
    /// <summary>The player's movement manager (its interpreter is <see cref="IPlayerBody.Motion"/>).</summary>
    MovementManager Movement { get; }

    /// <summary>
    /// Retail <c>CPhysics::SetObjectMovement</c> for the player after the stamps: records the autonomous bit as the
    /// last movement's (<see cref="IMotionInterpHost.MovementIsAutonomous"/>) and unpacks the movement
    /// (<see cref="MovementManager.UnpackMovement"/>).
    /// </summary>
    MovementUnpackResult ApplyServerMovement(in WireMovement movement, ReadOnlySpan<WireMotionItem> actions, bool autonomous, out WireMotionFailure failure);
}
