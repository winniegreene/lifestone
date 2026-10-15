using System.Buffers.Binary;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// What the message codecs (N3, N7, N10) share. Every parser follows the same rules: it takes the whole message, opcode
/// first; it never throws on input; and it accepts a message only when every byte was read, so a layout Lifestone has
/// wrong shows up as a malformed message instead of a silently misread one (m2-plan §3.3 N3).
/// </summary>
internal static class MessageCodec
{
    /// <summary>The size of the opcode that starts every message.</summary>
    public const int OpcodeSize = sizeof(uint);

    /// <summary>Reads the opcode of <paramref name="message"/>; false when it is shorter than one.</summary>
    public static bool TryReadOpcode(ReadOnlySpan<byte> message, out uint opcode)
    {
        if (message.Length < OpcodeSize)
        {
            opcode = 0;
            return false;
        }

        opcode = BinaryPrimitives.ReadUInt32LittleEndian(message);
        return true;
    }

    /// <summary>Starts <paramref name="reader"/> on a message: true when its opcode is <paramref name="opcode"/>.</summary>
    public static bool TryStart(ref WireReader reader, uint opcode) => reader.TryReadU32(out var read) && read == opcode;

    /// <summary>
    /// True when <paramref name="message"/> is exactly <paramref name="opcode"/> and nothing else: the signal messages
    /// (F7DF ServerReady, F7EA DDD_EndDDD, the F655 delete acknowledgement, the server's F653 CharacterLogOff).
    /// </summary>
    public static bool IsOpcodeOnly(ReadOnlySpan<byte> message, uint opcode) =>
        message.Length == OpcodeSize && BinaryPrimitives.ReadUInt32LittleEndian(message) == opcode;
}
