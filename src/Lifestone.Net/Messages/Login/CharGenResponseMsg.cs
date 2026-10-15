namespace Lifestone.Net.Messages;

/// <summary>
/// F643 CharacterCreateResponse (docs/protocol.md §5.2), also the answer to CharacterRestore (F7D9): a result, and on
/// success the character's GUID and name. The server does not resend the character list after a create.
/// </summary>
/// <param name="Result">The result code.</param>
/// <param name="Guid">The character's GUID (only when <paramref name="Result"/> is <see cref="CharGenResult.Ok"/>).</param>
/// <param name="Name">The character's name (only on success).</param>
/// <param name="Trailing">The trailing u32 after the name on success: the identity's seconds greyed out (retail
/// <c>CharacterIdentity</c>; ACE writes 0).</param>
public readonly record struct CharGenResponseMsg(CharGenResult Result, uint Guid, string? Name, uint Trailing);

/// <summary>The result codes of <see cref="CharGenResponseMsg"/> (docs/protocol.md §5.2).</summary>
public enum CharGenResult : uint
{
    /// <summary>0: not a code ACE sends.</summary>
    Undefined = 0,

    /// <summary>1: created (or restored).</summary>
    Ok = 1,

    /// <summary>2: pending.</summary>
    Pending = 2,

    /// <summary>3: the name is taken.</summary>
    NameInUse = 3,

    /// <summary>4: the name is banned (taboo table or creature names).</summary>
    NameBanned = 4,

    /// <summary>5: the request was corrupt.</summary>
    Corrupt = 5,

    /// <summary>6: the database is down.</summary>
    DatabaseDown = 6,

    /// <summary>7: admin privilege denied.</summary>
    AdminPrivilegeDenied = 7,
}
