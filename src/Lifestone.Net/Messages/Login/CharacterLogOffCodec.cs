// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/CharacterLogOff.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Messages; internal;
// renamed CharacterLogOffCodec; BuildRequestBody writes into a PacketWriter (Write); IsConfirmation is IsServerLogOff.

using Lifestone.Net.Packets;

namespace Lifestone.Net.Messages;

/// <summary>
/// F653 CharacterLogOff, both ways (docs/protocol.md §4.3 row 18): the client sends {u32 character GUID}, as retail's
/// <c>Proto_UI::LogOffCharacter</c> (11.4186 0x00546A20, 11.6096 0x005475E0) does (ACE reads nothing after the opcode);
/// the server answers, about 6 s later, with the opcode alone, then F658 and F7E1.
/// </summary>
internal static class CharacterLogOffCodec
{
    /// <summary>Writes the client's F653 for the character <paramref name="guid"/>.</summary>
    public static void Write(PacketWriter writer, uint guid)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteUInt32(MessageOpcodes.CharacterLogOff);
        writer.WriteUInt32(guid);
    }

    /// <summary>True when <paramref name="message"/> is exactly the server's F653 (the opcode alone).</summary>
    public static bool IsServerLogOff(ReadOnlySpan<byte> message) => MessageCodec.IsOpcodeOnly(message, MessageOpcodes.CharacterLogOff);
}
