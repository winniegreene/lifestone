// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/CharacterEnterWorld.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Messages; internal;
// renamed CharacterEnterWorldCodec; the two builders write into a PacketWriter instead of returning arrays; the account
// goes through the ASCII guard; the server's F7DF ServerReady signal is recognised here too.

using Lifestone.Net.Packets;

namespace Lifestone.Net.Messages;

/// <summary>
/// Entering the world (docs/protocol.md §4.3): C→S F7C8 CharacterEnterWorldRequest (the opcode alone), S→C F7DF
/// ServerReady (the opcode alone), then C→S F657 CharacterEnterWorld {u32 GUID, String16L account}.
/// </summary>
/// <remarks>
/// Retail sends them from <c>Proto_UI::SendEnterWorldRequest</c> (11.4186 0x00546A00, 11.6096 0x005475C0) and
/// <c>Proto_UI::SendEnterWorld</c> (11.4186 0x00546BC0, 11.6096 0x00547780), and takes F7DF in
/// <c>CM_Character::DispatchUI_EnterGame_ServerReady</c> (11.4186 0x006A0E50, 11.6096 0x006A1C70). ACE refuses an F657
/// whose account string differs from the session's with F659 0x0F (<c>CharacterHandler.CharacterEnterWorld</c>).
/// </remarks>
internal static class CharacterEnterWorldCodec
{
    /// <summary>Writes F7C8 CharacterEnterWorldRequest.</summary>
    public static void WriteRequest(PacketWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteUInt32(MessageOpcodes.CharacterEnterWorldRequest);
    }

    /// <summary>Writes F657 CharacterEnterWorld for <paramref name="guid"/> with the character list's account string.</summary>
    /// <exception cref="ArgumentException">The account is empty or not ASCII.</exception>
    public static void Write(PacketWriter writer, uint guid, ReadOnlySpan<char> account)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (account.IsEmpty)
        {
            throw new ArgumentException("CharacterEnterWorld needs the account string of the character list", nameof(account));
        }

        writer.WriteUInt32(MessageOpcodes.CharacterEnterWorld);
        writer.WriteUInt32(guid);
        writer.WriteAsciiString16L(account, nameof(account));
    }

    /// <summary>True when <paramref name="message"/> is exactly F7DF CharacterEnterWorldServerReady.</summary>
    public static bool IsServerReady(ReadOnlySpan<byte> message) =>
        MessageCodec.IsOpcodeOnly(message, MessageOpcodes.CharacterEnterWorldServerReady);
}
