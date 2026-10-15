using System.Numerics;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// Where the player's movement controller sends its movement messages (<see cref="IActionApi.Movement"/>). Physics'
/// <c>PlayerMovementController</c> decides when to send, on retail's cadence (<c>CommandInterpreter::SendMovementEvent</c>,
/// <c>ShouldSendPositionEvent</c> and <c>SendPositionEvent</c>; 11.4186 0x006B4680, 0x006B45E0, 0x006B4770; 11.6096
/// 0x006B55C0, 0x006B5520, 0x006B56B0); the sink only stamps and sends. Physics' <c>MovementAudit</c> decorates it.
/// </summary>
/// <remarks>
/// <para>Each send is stamped with the player's own instance, server-control, teleport and force-position sequences
/// (<see cref="SelfState.Sequences"/>; net's <c>ObjSeqs</c> names the retail packs that echo them), and goes to the
/// network session at once: no handle, no queue, no allocation per send.</para>
/// <para>A send while the player is not <see cref="SelfTravel.Present"/> still goes out (ACE ignores positions during a
/// teleport), and is counted and logged as a WARNING once per arrival. With no network session to send to (an offline
/// session, or outside the world) nothing is sent, and that is counted and logged as a WARNING once per arrival too:
/// never a silent drop. Dry run does not apply here: the controller is the same one the keyboard drives, and automation
/// reaches it through <see cref="MoveIntents"/>.</para>
/// <para>Game thread only. Arguments are copied before the call returns, so the caller may reuse them.</para>
/// </remarks>
public interface IMovementSink
{
    /// <summary>Sends GameAction 0xF61C MoveToState: the raw motion state, the position, the contact flag and the standing long jump flag.</summary>
    /// <param name="raw">The player's raw motion state (copied; full 32-bit motion commands).</param>
    /// <param name="position">The player's position.</param>
    /// <param name="contact">True when the player is on the ground (retail's contact flag).</param>
    /// <param name="standingLongJump">True for a standing long jump.</param>
    void MoveToState(RawMotionState raw, in CellFrame position, bool contact, bool standingLongJump);

    /// <summary>Sends GameAction 0xF753 AutonomousPosition.</summary>
    /// <param name="position">The player's position.</param>
    /// <param name="contact">True when the player is on the ground.</param>
    void AutonomousPosition(in CellFrame position, bool contact);

    /// <summary>Sends GameAction 0xF61B Jump (retail's 56-byte JumpPack).</summary>
    /// <param name="extent">The jump's charge, 0 to 1.</param>
    /// <param name="velocity">The jump's launch velocity, AC axes.</param>
    /// <param name="position">The player's position.</param>
    void Jump(float extent, Vector3 velocity, in CellFrame position);
}
