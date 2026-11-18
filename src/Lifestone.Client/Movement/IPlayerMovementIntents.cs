using Lifestone.Data.World;

namespace Lifestone.Client.Movement;

/// <summary>
/// What the player asks of their character, as retail's command interpreter receives it from the keymap: movement
/// commands pressed and released, the run and sidestep modifiers, autorun and the jump bar. The physics track's
/// <c>PlayerMovementController</c> implements it; it turns intents into motion (the avatars track's motion interpreter),
/// steps the player through collision and sends MoveToState, AutonomousPosition and Jump on retail's cadence.
/// </summary>
/// <remarks>
/// <para><b>Callers:</b> the state track's <c>MoveIntents</c> bridge (Action API), the wave-2 keyboard input, and the
/// physics track's <c>RouteDriver</c>. Main thread (or the headless tick) only.</para>
/// <para>Retail sources (re-expressed in step PH7, cited by name; nothing copied): <c>CommandInterpreter</c> and
/// <c>ACCmdInterp</c> (11.4186 PORTAL/cmdinterp/CmdInterp.c, AC/accmdinterp/ACCmdInterp.c) for the command lists, hold
/// keys and autorun (<c>ACCmdInterp::OnAction</c>, 11.4186 0x0058B370, 11.6096 0x0058C1A0, for the run key); <c>ClientCombatSystem::CommenceJump</c> (11.4186 0x0056AF90, 11.6096 0x0056BCD0) and
/// <c>ClientCombatSystem::DoJump</c> (11.4186 0x0056B110, 11.6096 0x0056BE50) for the jump bar.</para>
/// </remarks>
public interface IPlayerMovementIntents
{
    /// <summary>
    /// A movement command was pressed (<paramref name="start"/> true) or released (false): a retail motion command such as
    /// WalkForward 0x45000005, WalkBackwards 0x45000006, TurnRight 0x6500000D, TurnLeft 0x6500000E, SideStepRight
    /// 0x6500000F or SideStepLeft 0x65000010, as the keymap delivers it.
    /// </summary>
    /// <param name="command">The retail motion command.</param>
    /// <param name="start">True on press, false on release.</param>
    /// <param name="speed">The speed factor the keymap attaches (1 for keys; analog input may pass less).</param>
    void HandleCommand(uint command, bool start, float speed = 1f);

    /// <summary>
    /// The run modifier (retail <c>HoldKey.Run</c>): while on, walking forward becomes running (walking, with the
    /// run-by-default option). Retail's run key (<c>ACCmdInterp::OnAction</c>, input action 0x32): it changes only the
    /// motion, sends no MoveToState and leaves control with whoever has it.
    /// </summary>
    void SetHoldRun(bool on);

    /// <summary>
    /// The sidestep modifier (retail <c>HoldKey</c> HoldSidestep 0x85000002): while on, the turn commands sidestep instead.
    /// </summary>
    /// <remarks>
    /// Retail's keymap never sends it: <c>ACCmdInterp::OnAction</c> (11.4186 0x0058B370, 11.6096 0x0058C1A0) has no input
    /// action for it, and its sidestep keys are their own actions (0x2C SideStepRight, 0x2D SideStepLeft, through
    /// <see cref="HandleCommand"/>). The modifier is <c>CommandInterpreter::HandleKeyboardCommand</c>'s HoldSidestep
    /// branch, kept for callers that send the command (docs/physics.md §12).
    /// </remarks>
    void SetHoldSidestep(bool on);

    /// <summary>Toggles autorun (retail's autorun key).</summary>
    void ToggleAutoRun();

    /// <summary>The jump key went down: the jump bar starts charging at <paramref name="now"/> (seconds).</summary>
    void CommenceJump(double now);

    /// <summary>The jump key came up: jump with the extent the bar reached at <paramref name="now"/> (seconds).</summary>
    void DoJump(double now);

    /// <summary>The player's cell and frame after the last physics step.</summary>
    CellFrame Position { get; }

    /// <summary>True while the player stands on walkable ground (retail transient state Contact and OnWalkable).</summary>
    bool OnGround { get; }

    /// <summary>
    /// M6 14 airborne: true while the body is in the world and off walkable ground (a jump, a fall): ACE's <c>Player.IsJumping</c> (<c>Player.cs</c>:49-62 @95ef426d: no OnWalkable), for which it refuses a cast, a melee or missile
    /// attack, a heal and a drink with 0x04EB. A body that is not placed in a cell yet is nowhere, not in the air. The default is <c>!</c><see cref="OnGround"/> (a body with no placement of its own to tell).
    /// </summary>
    bool InTheAir => !OnGround;

    /// <summary>
    /// M5 7 P7p (D141): the server MoveTos the controller's stuck rule ended (<c>PlayerMovementController.ServerMoveToStuckEnds</c>, <c>TODO(m5-moveto-stuck)</c>), so a caller can read before and after an
    /// action whether ACE's walk-to-use ended stuck in between. 0 for a body with no such rule.
    /// </summary>
    int ServerMoveToStuckEnds => 0;

    /// <summary>
    /// M5 7 D153 (D141, check, act, check): whether <paramref name="command"/> (a turn, sidestep or locomotion command) is still held: false after something let it go that
    /// the caller did not (a lost window focus or a text field taking the keyboard drops every held key, <c>CommandInterpreter::LoseKeyboardFocus</c>), so a driver can press
    /// it again. Null when this body cannot tell.
    /// </summary>
    bool? IsHeld(uint command) => null;

    /// <summary>M5 7 P7p: how the last server MoveTo ended (<c>PlayerMovementController.LastServerMoveToEnd</c>: <c>arrived</c>, <c>cancelled (...)</c>, <c>not started</c>, <c>stuck</c>), or null.</summary>
    string? LastServerMoveToEnd => null;

    /// <summary>
    /// M6 14 (check, act, check, D141): whether the server's MoveTo or TurnTo of the player is under way now (retail <c>CPhysicsObj::IsMovingTo</c>, 11.6096 0x0050F5E0, while the server has control): an out-of-range Use's walk,
    /// which a movement key ends. A caller reads it before and after the key. Null when this body cannot tell.
    /// </summary>
    bool? ServerMoving => null;

    /// <summary>M6 14: what the movement stack believes the player is doing, in one line for a log (<c>PlayerMovementController.DescribeMotion</c>), or null when this body does not say.</summary>
    string? MotionText => null;
}
