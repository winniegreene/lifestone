// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Network/Enum/CharacterError.cs (AGPL-3.0)
// with changes: the names and values only, as a uint enum; shorter XML docs.
namespace Lifestone.Net.Messages;

/// <summary>
/// The F659 CharacterError codes (retail <c>ID_CHAR_ERROR_*</c>). The session retries a login on <see cref="Logon"/>,
/// <see cref="EnterGameCharacterInWorld"/> and <see cref="LogonServerFull"/> (Lifestone deviation D18, off with
/// <c>--retail-net</c>; <see cref="Session.LoginRetryPolicy"/>).
/// </summary>
public enum CharacterErrorCode : uint
{
    /// <summary>0x01: the account is already logged on (two sessions at once).</summary>
    Logon = 0x01,

    /// <summary>0x03: the server could not access the account.</summary>
    AccountLogin = 0x03,

    /// <summary>0x04: the server disconnected.</summary>
    ServerCrash1 = 0x04,

    /// <summary>0x05: the server could not log the character off.</summary>
    Logoff = 0x05,

    /// <summary>0x06: the server could not delete the character.</summary>
    Delete = 0x06,

    /// <summary>0x08: the server disconnected.</summary>
    ServerCrash2 = 0x08,

    /// <summary>0x09: the account name is not valid.</summary>
    AccountInvalid = 0x09,

    /// <summary>0x0A: the account does not exist.</summary>
    AccountDoesntExist = 0x0A,

    /// <summary>0x0B: entering the game failed (generic).</summary>
    EnterGameGeneric = 0x0B,

    /// <summary>0x0C: a stress-account character cannot enter.</summary>
    EnterGameStressAccount = 0x0C,

    /// <summary>0x0D: one of the account's characters is still in the world.</summary>
    EnterGameCharacterInWorld = 0x0D,

    /// <summary>0x0E: the player account is missing.</summary>
    EnterGamePlayerAccountMissing = 0x0E,

    /// <summary>0x0F: the account does not own the character (or the account string did not match).</summary>
    EnterGameCharacterNotOwned = 0x0F,

    /// <summary>0x10: a character is in the world (server side).</summary>
    EnterGameCharacterInWorldServer = 0x10,

    /// <summary>0x11: an old character (back to character select).</summary>
    EnterGameOldCharacter = 0x11,

    /// <summary>0x12: the character's data is corrupt.</summary>
    EnterGameCorruptCharacter = 0x12,

    /// <summary>0x13: the starting server is down.</summary>
    EnterGameStartServerDown = 0x13,

    /// <summary>0x14: the character could not be placed in the world.</summary>
    EnterGameCouldntPlaceCharacter = 0x14,

    /// <summary>0x15: the server is full (ACE also sends it while the world is not open yet).</summary>
    LogonServerFull = 0x15,

    /// <summary>0x17: a save of the character is still in progress.</summary>
    EnterGameCharacterLocked = 0x17,

    /// <summary>0x18: the subscription has expired.</summary>
    SubscriptionExpired = 0x18,
}
