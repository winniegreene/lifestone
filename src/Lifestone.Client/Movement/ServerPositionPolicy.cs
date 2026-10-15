using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Motion;

namespace Lifestone.Client.Movement;

/// <summary>What <see cref="PlayerMovementController.Apply"/> did with a position the server sent.</summary>
public enum ServerPositionOutcome
{
    /// <summary>Not the player's (a <see cref="PositionVerdict.Remote"/> verdict): the avatars track moves other objects.</summary>
    Ignored,

    /// <summary>
    /// The server echoed an accepted move (<see cref="PositionVerdict.SelfEcho"/>): the player was constrained to it and
    /// keeps moving on its own (<c>CPhysicsObj::ConstrainTo</c>).
    /// </summary>
    Constrained,

    /// <summary>
    /// The server corrected the player (<see cref="PositionVerdict.ForceBlip"/>): it was put there, keeping its heading,
    /// and its position sent back at once (<c>SmartBox::BlipPlayer</c>).
    /// </summary>
    Blipped,

    /// <summary>
    /// The player arrived from a teleport (<see cref="PositionVerdict.SelfTeleport"/>): it was placed there (or waits in
    /// the lost cell for the landblock's collision, <see cref="Data.Physics.Objects.PhysicsObj.PlacementPending"/>),
    /// constrained to it and stopped (<c>SmartBox::TeleportPlayer</c>).
    /// </summary>
    Teleported,
}

/// <summary>
/// The rules by which the player follows the positions the server sends it: retail <c>SmartBox::HandleReceivedPosition</c>
/// (11.4186 0x00453FD0, 11.6096 0x00454070) for the player's own object, after the session's reducer has judged the
/// update's sequences (<see cref="PositionVerdict"/>). <see cref="PlayerMovementController.Apply"/> carries them out.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>ForceBlip</b> (a newer force-position sequence, the teleport sequence not older): the position takes the
/// player's current heading (<c>CPhysicsObj::get_heading</c>, <c>Frame::set_heading</c>), <c>SmartBox::BlipPlayer</c>
/// (11.4186 0x00453940, 11.6096 0x004539E0) puts the player there with a sliding <c>SetPositionSimple</c>, and the
/// command interpreter sends an AutonomousPosition at once (<c>CommandInterpreter::SendPositionEvent</c>).</item>
/// <item><b>SelfTeleport</b> (a newer position with a newer teleport sequence): <c>SmartBox::TeleportPlayer</c> (11.4186
/// 0x00453910, 11.6096 0x004539B0) places the player with a sliding <c>SetPositionSimple</c>, then
/// <c>SmartBox::PlayerPositionUpdated</c> (0x00453870 / 0x00453910) runs <c>CPhysicsObj::teleport_hook</c> and tells the
/// command interpreter (<c>CommandInterpreter::PlayerTeleported</c>, 0x006B32B0 / 0x006B41F0: autorun off and a
/// MoveToState); then the player is constrained to the position and its velocity zeroed.</item>
/// <item><b>SelfEcho</b> (a newer position, the same teleport sequence): the player is constrained to the position
/// (<c>CPhysicsObj::ConstrainTo</c>, 11.4186 0x00510520, 11.6096 0x00510FF0) with <see cref="StartConstraintDistance"/>
/// and <see cref="MaxConstraintDistance"/> of the cell it is in; it would also interpolate towards it only at an
/// autonomy level other than 2 (<c>CommandInterpreter::UsePositionFromServer</c>, 0x006B3B40 / 0x006B4A80), which M2
/// never uses.</item>
/// </list>
/// <para><b>Inferred.</b> The command interpreter's teleport notice is a virtual call (vtable slot 29) the decompile does
/// not name; it is read as <c>CommandInterpreter::PlayerTeleported</c>, the only entry point of that name and purpose
/// (docs/physics.md §12).</para>
/// <para><b>D23.</b> Retail constrains on every echo, and so does Lifestone; the slow-down this causes a fast runner is
/// measured (<see cref="MovementStats.MinConstraintFactor"/>, docs/physics.md §12).</para>
/// </remarks>
public static class ServerPositionPolicy
{
    /// <summary>
    /// Retail <c>CPhysicsObj::GetStartConstraintDistance</c> (11.4186 0x0050EBC0, 11.6096 0x0050F690): the constraint
    /// starts slowing the player after 10 m outdoors, 5 m in an EnvCell.
    /// </summary>
    public static float StartConstraintDistance(uint cellId) => PositionManager.GetStartConstraintDistance(cellId);

    /// <summary>
    /// Retail <c>CPhysicsObj::GetMaxConstraintDistance</c> (11.4186 0x0050EC10, 11.6096 0x0050F6E0): the constraint stops
    /// the player at 50 m outdoors, 20 m in an EnvCell.
    /// </summary>
    public static float MaxConstraintDistance(uint cellId) => PositionManager.GetMaxConstraintDistance(cellId);

    /// <summary>
    /// The factor retail <c>ConstraintManager::adjust_offset</c> (11.4186 0x00556180, 11.6096 0x00556DA0) scales a step by
    /// after <paramref name="accumulated"/> metres: 1 up to <paramref name="start"/>, then
    /// <c>(max - accumulated) / (max - start)</c>, and 0 from <paramref name="max"/> on.
    /// </summary>
    public static float ConstraintFactor(float accumulated, float start, float max) =>
        accumulated < max ? accumulated > start ? (max - accumulated) / (max - start) : 1f : 0f;
}
