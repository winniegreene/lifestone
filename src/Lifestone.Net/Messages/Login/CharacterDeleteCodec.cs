// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/CharacterDelete.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Messages; internal;
// renamed CharacterDeleteCodec; BuildRequestBody writes into a PacketWriter (Write) and takes the slot as retail's int;
// the account goes through the ASCII guard; IsAcknowledgement keeps its exact-length check.

using Lifestone.Net.Packets;

namespace Lifestone.Net.Messages;

/// <summary>
/// F655 CharacterDelete: the client sends {String16L account, i32 list index}, as retail's
/// <c>Proto_UI::SendDeleteCharacter</c> (11.4186 0x00546B30, 11.6096 0x005476F0) does; the server acknowledges with the
/// opcode alone, then sends a fresh F658 in which the character is greyed out (ACE <c>CharacterHandler.CharacterDelete</c>).
/// </summary>
internal static class CharacterDeleteCodec
{
    /// <summary>Writes F655 for the character at <paramref name="listIndex"/> of the latest CharacterList.</summary>
    /// <exception cref="ArgumentException">The account is empty or not ASCII.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="listIndex"/> is negative.</exception>
    public static void Write(PacketWriter writer, ReadOnlySpan<char> account, int listIndex)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentOutOfRangeException.ThrowIfNegative(listIndex);
        if (account.IsEmpty)
        {
            throw new ArgumentException("CharacterDelete needs the account string of the character list", nameof(account));
        }

        writer.WriteUInt32(MessageOpcodes.CharacterDelete);
        writer.WriteAsciiString16L(account, nameof(account));
        writer.WriteInt32(listIndex);
    }

    /// <summary>True when <paramref name="message"/> is exactly the server's F655 acknowledgement (the opcode alone).</summary>
    public static bool IsAcknowledgement(ReadOnlySpan<byte> message) => MessageCodec.IsOpcodeOnly(message, MessageOpcodes.CharacterDelete);
}
