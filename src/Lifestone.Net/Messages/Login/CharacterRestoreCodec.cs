// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/CharacterRestore.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Messages; internal;
// renamed CharacterRestoreCodec; BuildRequestBody writes into a PacketWriter (Write) and, after the GUID, adds the two
// strings of retail's only F7D9 sender (Proto_UI::SendAdminRestoreCharacter); the answer's parser is not taken (it is
// CharGenResponseCodec, the same F643).

using Lifestone.Net.Packets;

namespace Lifestone.Net.Messages;

/// <summary>
/// F7D9 CharacterRestore: {u32 GUID, String16L character name, String16L account}, answered by F643
/// (<see cref="CharGenResponseCodec"/>), whose Ok result carries the restored identity.
/// </summary>
/// <remarks>
/// This is the layout of retail's only F7D9 sender, <c>Proto_UI::SendAdminRestoreCharacter</c> (11.4186 0x00546CF0,
/// 11.6096 0x005478B0). ACE treats F7D9 as a player's restore during character select and reads the GUID alone
/// (<c>CharacterHandler.CharacterRestore</c>), so the two strings are carried for fidelity and are not ASCII-guarded:
/// nothing ACE reads follows them. OpenAC sends the GUID alone, which ACE also accepts.
/// </remarks>
internal static class CharacterRestoreCodec
{
    /// <summary>Writes F7D9 for the character <paramref name="guid"/> named <paramref name="name"/> on <paramref name="account"/>.</summary>
    /// <exception cref="ArgumentException">A string has a character Windows-1252 cannot represent.</exception>
    public static void Write(PacketWriter writer, uint guid, ReadOnlySpan<char> name, ReadOnlySpan<char> account)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteUInt32(MessageOpcodes.CharacterRestore);
        writer.WriteUInt32(guid);
        writer.WriteString16L(name, nameof(name));
        writer.WriteString16L(account, nameof(account));
    }
}
