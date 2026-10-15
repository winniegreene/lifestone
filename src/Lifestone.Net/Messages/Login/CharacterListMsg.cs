using System.Collections.Immutable;

namespace Lifestone.Net.Messages;

/// <summary>
/// F658 CharacterList, field for field (docs/protocol.md §5.2; retail <c>CharacterSet::UnPack</c>, 11.4186 0x004FE340,
/// 11.6096 0x004FEDC0): the account's characters, the slot count and the account string the server knows the session
/// by. Raw values are passed through, never interpreted.
/// </summary>
/// <param name="Leading">The leading u32 (retail <c>status_</c>; ACE writes 0).</param>
/// <param name="Characters">The characters, in the server's order (the order DeleteCharacter's list index refers to).</param>
/// <param name="DeletedCount">The u32 count of the second (deleted) list; always the length of
/// <see cref="DeletedCharacters"/> when the codec decoded the message. ACE always writes 0.</param>
/// <param name="SlotCount">How many characters the account may have (ACE: <c>max_chars_per_account</c>, 11).</param>
/// <param name="Account">
/// The account string to echo back verbatim in CharacterCreate, CharacterDelete and CharacterEnterWorld: ACE ignores
/// (or refuses) a request whose account string differs (docs/protocol.md §4.2).
/// </param>
/// <param name="UseTurbineChat">The raw use-TurbineChat word (1 = TurbineChat channels on).</param>
/// <param name="HasThroneOfDestiny">The raw expansion word (ACE writes 1).</param>
public sealed record CharacterListMsg(
    uint Leading,
    ImmutableArray<CharacterListEntry> Characters,
    uint DeletedCount,
    uint SlotCount,
    string Account,
    uint UseTurbineChat,
    uint HasThroneOfDestiny)
{
    /// <summary>
    /// The second list (retail <c>delSet_</c>), whose entries have the same layout as <see cref="Characters"/>
    /// (<c>CharacterIdentity::UnPack</c>, 11.4186 0x004FE880, 11.6096 0x004FF300). ACE never fills it; empty unless a
    /// server sends one.
    /// </summary>
    public ImmutableArray<CharacterListEntry> DeletedCharacters { get; init; } = [];
}

/// <summary>One character of a <see cref="CharacterListMsg"/> (retail <c>CharacterIdentity</c>).</summary>
/// <param name="Guid">The character's GUID.</param>
/// <param name="Name">The name as sent (a leading '+' marks an admin character).</param>
/// <param name="DeleteTimeout">
/// The raw seconds-greyed-out word (retail <c>secondsGreyedOut_</c>; 0 = active). Passed raw (m2-plan §1.6 item 24): ACE
/// writes <c>Math.Max(1, now − DeleteTime)</c> while a deletion is pending, so it reads 1 there, not a countdown.
/// </param>
public readonly record struct CharacterListEntry(uint Guid, string Name, uint DeleteTimeout);
