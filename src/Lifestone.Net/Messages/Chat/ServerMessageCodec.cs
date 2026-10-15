// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/ServerMessage.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Messages; internal;
// renamed ServerMessageCodec; TryParse returns bool over WireReader instead of a nullable record, never catches (it never
// throws), and requires every byte to be read; the chat type is ACE's i32; the result is the N0 record ServerMessageMsg.

using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// F7E0 ServerMessage, system chat (ACE <c>GameMessageSystemChat</c>): String16L text, i32 chat type. Retail shows it
/// through <c>CM_Communication::DispatchUI_TextboxString</c> (11.4186 0x006A4F30, 11.6096 0x006A5E90). The server mod's
/// tool replies arrive this way (prefix <c>#lifestone/1</c>; the state track recognises them).
/// </summary>
internal static class ServerMessageCodec
{
    /// <summary>Decodes <paramref name="message"/>; false when it is not a whole, well-formed F7E0.</summary>
    public static bool TryParse(ReadOnlySpan<byte> message, out ServerMessageMsg serverMessage)
    {
        serverMessage = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.ServerMessage)
            || !reader.TryReadString16L(out var text)
            || !reader.TryReadI32(out var chatType)
            || !reader.IsAtEnd)
        {
            return false;
        }

        serverMessage = new ServerMessageMsg(text, chatType);
        return true;
    }
}
