// Ported from ACEmulator/ACE@95ef426d Source/ACE.Entity/Enum/WeenieError.cs (AGPL-3.0)
// with changes: only the values the motion code returns, as a uint enum; XML docs describe where retail returns each.
namespace Lifestone.Data.Motion;

/// <summary>
/// Retail <c>WeenieError</c> codes, the subset the motion interpreter and its helpers return. The numbers are the
/// literals of the 11.4186 decompile (<c>CMotionInterp</c>, <c>CPartArray</c>, <c>MotionTableManager</c>); the names are
/// ACE's. <see cref="None"/> is success. The same enum carries the chat-window error messages (game event 0x028A), so a
/// caller can show, for example, <see cref="YouCantJumpFromThisPosition"/> as retail does.
/// </summary>
public enum WeenieError : uint
{
    /// <summary>Success.</summary>
    None = 0x0000,

    /// <summary>The part array has no motion table (<c>CPartArray::DoInterpretedMotion</c> and its siblings).</summary>
    NoAnimationTable = 0x0007,

    /// <summary>The interpreter has no physics object (every <c>CMotionInterp</c> entry point).</summary>
    NoPhysicsObject = 0x0008,

    /// <summary>The object has no motion interpreter (<c>MoveToManager::_DoMotion</c>, <c>_StopMotion</c>).</summary>
    NoMotionInterpreter = 0x000B,

    /// <summary>"You can't jump while in the air": an action without ground contact, or a jump while airborne.</summary>
    YouCantJumpWhileInTheAir = 0x0024,

    /// <summary>A MoveTo was cancelled by a new movement (<c>MoveToManager::PerformMovement</c>, <c>cancel_moveto</c>).</summary>
    ActionCancelled = 0x0036,

    /// <summary>A MoveTo's initialised target stopped reporting Ok (<c>MoveToManager::HandleUpdateTarget</c>).</summary>
    ObjectGone = 0x0037,

    /// <summary>A MoveTo's target never reported Ok (<c>MoveToManager::HandleUpdateTarget</c>).</summary>
    NoObject = 0x0038,

    /// <summary>A teleport cancelled the MoveTo (<c>CPhysicsObj::teleport_hook</c>).</summary>
    ITeleported = 0x003C,

    /// <summary>A MoveTo went farther than its fail distance (<c>MoveToManager::HandleMoveToPosition</c>).</summary>
    YouChargedTooFar = 0x003D,

    /// <summary>Crouch outside the NonCombat stance (<c>CMotionInterp::DoMotion</c>).</summary>
    CantCrouchInCombat = 0x003F,

    /// <summary>Sitting outside the NonCombat stance.</summary>
    CantSitInCombat = 0x0040,

    /// <summary>Sleeping outside the NonCombat stance.</summary>
    CantLieDownInCombat = 0x0041,

    /// <summary>A chat emote (command bit 0x02000000) outside the NonCombat stance.</summary>
    CantChatEmoteInCombat = 0x0042,

    /// <summary>The motion table has no data for the motion (<c>MotionTableManager::PerformMovement</c>).</summary>
    NoMtableData = 0x0043,

    /// <summary>Six actions are already queued (<c>CMotionInterp::DoMotion</c>).</summary>
    TooManyActions = 0x0045,

    /// <summary>A general failure: an unknown movement type, a fully constrained jump, too little stamina to jump.</summary>
    GeneralMovementFailure = 0x0047,

    /// <summary>"You can't jump from this position": the motion state forbids a jump (the jump-gating set).</summary>
    YouCantJumpFromThisPosition = 0x0048,

    /// <summary>The weenie cannot jump with its load (<c>CMotionInterp::charge_jump</c>).</summary>
    CantJumpLoadedDown = 0x0049,
}
