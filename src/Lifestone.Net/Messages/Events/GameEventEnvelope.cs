// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/GameEventEnvelope.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Messages; internal static
// class instead of a record; TryParse/TryParseBorrowed (nullable record over ReadOnlyMemory) became TryReadHeader over a
// span into the N0 record GameEventHeader, with the payload offset; the event type stays a raw u32 (OpenAC's
// GameEventType enum is not taken; GameEventTypes holds the ones M2 decodes).

using System.Buffers.Binary;
using Lifestone.Net.Types;

namespace Lifestone.Net.Messages;

/// <summary>
/// The F7B0 GameEvent envelope, server to client (docs/protocol.md §3; ACE <c>GameEventMessage</c>): u32 player GUID
/// (0 before entering the world), u32 event sequence (reset to 1 on entering the world, +1 per event), u32 event type,
/// then the event's payload.
/// </summary>
internal static class GameEventEnvelope
{
    /// <summary>The opcode and the three header words.</summary>
    public const int HeaderSize = 4 * sizeof(uint);

    /// <summary>
    /// Reads the header of <paramref name="message"/> (the whole F7B0 message, opcode first). The payload is
    /// <c>message[HeaderSize..]</c>, possibly empty. False when the message is shorter than the header or not F7B0.
    /// </summary>
    public static bool TryReadHeader(ReadOnlySpan<byte> message, out GameEventHeader header)
    {
        if (message.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(message) != MessageOpcodes.GameEvent)
        {
            header = default;
            return false;
        }

        header = new GameEventHeader(
            BinaryPrimitives.ReadUInt32LittleEndian(message[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(message[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(message[12..]));
        return true;
    }
}
