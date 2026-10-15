using Lifestone.Data.Motion;

namespace Lifestone.Client.Movement;

/// <summary>What a <see cref="MovementNotice"/> reports.</summary>
public enum MovementNoticeKind
{
    /// <summary>
    /// Autorun was switched on: retail <c>CommandInterpreter::SetAutoRun</c> (11.4186 0x006B4850, 11.6096 0x006B5790)
    /// shows "AutoRun ON" in the chat window (notice 0x1A).
    /// </summary>
    AutoRunOn,

    /// <summary>Autorun was switched off: "AutoRun OFF".</summary>
    AutoRunOff,

    /// <summary>
    /// A motion the player asked for was refused (<c>CommandInterpreter::MovePlayer</c>, 11.4186 0x006B3F40, 11.6096
    /// 0x006B4E80, shows a message for <see cref="WeenieError.CantCrouchInCombat"/>, <see cref="WeenieError.CantSitInCombat"/>,
    /// <see cref="WeenieError.CantLieDownInCombat"/>, <see cref="WeenieError.CantChatEmoteInCombat"/>, 0x3E (too tired) and
    /// 0x44 (an emote from this position)); <see cref="MovementNotice.Error"/> says which.
    /// </summary>
    MotionRefused,

    /// <summary>
    /// The jump key was refused (<c>ClientCombatSystem::CommenceJump</c>, 11.4186 0x0056AF90, 11.6096 0x0056BCD0, or
    /// <c>ClientCombatSystem::DoJump</c>, 11.4186 0x0056B110, 11.6096 0x0056BE50): <see cref="WeenieError.YouCantJumpFromThisPosition"/>,
    /// <see cref="WeenieError.CantJumpLoadedDown"/> or <see cref="WeenieError.YouCantJumpWhileInTheAir"/>.
    /// </summary>
    JumpRefused,
}

/// <summary>
/// A message retail's movement code puts in the chat window (<c>ClientSystem::AddTextToScroll</c> or
/// <c>ECM_UI::SendNotice_DisplayStringInfo</c> with chat type 0x1A). The controller only reports it; the UI shows it.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="Error">The retail error number for <see cref="MovementNoticeKind.MotionRefused"/> and
/// <see cref="MovementNoticeKind.JumpRefused"/>; <see cref="WeenieError.None"/> otherwise.</param>
/// <param name="Text">
/// The text retail shows when it is a literal in the code ("AutoRun ON", "AutoRun OFF"); null for the refusals, whose
/// texts are retail string-table entries (<c>cant_jump_in_air</c> and the others) that the UI looks up by
/// <paramref name="Error"/>.
/// </param>
public readonly record struct MovementNotice(MovementNoticeKind Kind, WeenieError Error, string? Text)
{
    /// <summary>The chat type retail shows these messages with (0x1A).</summary>
    public const uint ChatType = 0x1A;
}
