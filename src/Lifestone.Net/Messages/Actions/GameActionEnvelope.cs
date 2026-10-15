using Lifestone.Net.Packets;

namespace Lifestone.Net.Messages;

/// <summary>
/// The F7B1 GameAction envelope, client to server (docs/protocol.md §3): u32 action sequence, u32 action type, then the
/// action's payload. Retail packs it with <c>OrderHdr::Pack</c> (11.4186 0x006B30D0, 11.6096 0x006B4010), stamping the
/// sequence from <c>Proto_UI::GetNextUICounter</c> (11.4186 0x005467E0, 11.6096 0x005473A0), which counts up from 1 and
/// is handed back by <c>Proto_UI::UICounterFailedSend</c> when a send fails. The session owns that counter; ACE does not
/// check it ("TODO: verify sequence", <c>GameActionPacket.HandleGameAction</c>).
/// </summary>
internal static class GameActionEnvelope
{
    /// <summary>The opcode and the two header words.</summary>
    public const int HeaderSize = 3 * sizeof(uint);

    /// <summary>The first sequence the session stamps.</summary>
    public const uint FirstSequence = 1;

    /// <summary>Writes the opcode, <paramref name="sequence"/> and <paramref name="actionType"/>; the payload follows.</summary>
    public static void WriteHeader(PacketWriter writer, uint sequence, uint actionType)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteUInt32(MessageOpcodes.GameAction);
        writer.WriteUInt32(sequence);
        writer.WriteUInt32(actionType);
    }
}
