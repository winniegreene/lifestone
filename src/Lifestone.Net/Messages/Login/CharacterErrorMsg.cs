namespace Lifestone.Net.Messages;

/// <summary>
/// F659 CharacterError (docs/protocol.md §5.2): one u32 code, at any time during login or character select. The code is
/// kept raw (<see cref="Code"/>); <see cref="Known"/> names it when it is one of <see cref="CharacterErrorCode"/>.
/// </summary>
/// <param name="Code">The raw error code.</param>
public readonly record struct CharacterErrorMsg(uint Code)
{
    /// <summary>The code as a <see cref="CharacterErrorCode"/>, or null when it is not a known one.</summary>
    public CharacterErrorCode? Known => Enum.IsDefined((CharacterErrorCode)Code) ? (CharacterErrorCode)Code : null;
}
