namespace Lifestone.Net.Messages;

/// <summary>
/// The kind of a confirmation dialog, as ACE names it (<c>ACE.Entity/Enum/ConfirmationType.cs</c> @95ef426d, lines 3-13). The wire
/// carries it as a u32 (<see cref="ConfirmationRequestEvent.Type"/>); a value this enum lacks is kept raw, never refused.
/// </summary>
public enum ConfirmationType : uint
{
    /// <summary>0x00 Undefined.</summary>
    Undefined = 0x00,

    /// <summary>0x01 SwearAllegiance.</summary>
    SwearAllegiance = 0x01,

    /// <summary>0x02 AlterSkill.</summary>
    AlterSkill = 0x02,

    /// <summary>0x03 AlterAttribute.</summary>
    AlterAttribute = 0x03,

    /// <summary>0x04 Fellowship.</summary>
    Fellowship = 0x04,

    /// <summary>0x05 CraftInteraction.</summary>
    CraftInteraction = 0x05,

    /// <summary>0x06 Augmentation.</summary>
    Augmentation = 0x06,

    /// <summary>0x07 Yes_No (ACE's name; the generic yes or no).</summary>
    YesNo = 0x07,
}

/// <summary>
/// GameEvent 0x0274 CharacterConfirmationRequest (M5 6 w1): the server asks the player a yes-or-no question and holds the answer
/// until a ConfirmationResponse (GA 0x0275) or a timeout. ACE writes <c>(uint)confirmationType</c>, <c>context</c>, then
/// <c>WriteString16L(text)</c> (<c>GameEventConfirmationRequest.cs:10-12</c>, @95ef426d). Retail's handler is
/// <c>CM_Character::DispatchUI_ConfirmationRequest</c> (11.6096 0x006A3BB0).
/// </summary>
/// <param name="Type">The raw u32 of ACE's <see cref="ConfirmationType"/>.</param>
/// <param name="Context">The server's context word, handed back unchanged in the response.</param>
/// <param name="Text">The question, as ACE sends it (retail composes the dialog from it).</param>
public readonly record struct ConfirmationRequestEvent(uint Type, uint Context, string Text)
{
    /// <summary>The type as ACE's enum; a value the enum does not name is cast as is.</summary>
    public ConfirmationType Kind => (ConfirmationType)Type;
}

/// <summary>
/// GameEvent 0x0276 CharacterConfirmationDone (M5 6 w1): the server closes the question it asked (answered elsewhere, timed out or
/// withdrawn). ACE writes <c>(uint)confirmationType</c> then <c>contextId</c> (<c>GameEventConfirmationDone.cs:10-11</c>, @95ef426d).
/// Retail's handler is <c>CM_Character::DispatchUI_ConfirmationDone</c> (11.6096 0x006A1C40).
/// </summary>
/// <param name="Type">The raw u32 of ACE's <see cref="ConfirmationType"/>.</param>
/// <param name="Context">The context word of the request it closes.</param>
public readonly record struct ConfirmationDoneEvent(uint Type, uint Context)
{
    /// <summary>The type as ACE's enum; a value the enum does not name is cast as is.</summary>
    public ConfirmationType Kind => (ConfirmationType)Type;
}
