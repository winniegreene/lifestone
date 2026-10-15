// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/ServerName.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Messages; internal;
// renamed ServerNameCodec; Parse (which threw) became TryParse over WireReader, which never throws and requires every
// byte to be read; the result is the N0 record ServerNameMsg.

using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// F7E1 ServerName (docs/protocol.md §5.2): i32 players online, i32 maximum (ACE writes its session limit), String16L
/// world name. Retail reads it in <c>CM_Login::DispatchUI_WorldInfo</c> (11.4186 0x006AD860, 11.6096 0x006AE7C0) for
/// <c>ClientUISystem::Handle_Login__WorldInfo</c> (11.4186 0x005641A0, 11.6096 0x00564F40).
/// </summary>
internal static class ServerNameCodec
{
    /// <summary>Decodes <paramref name="message"/>; false when it is not a whole, well-formed F7E1.</summary>
    public static bool TryParse(ReadOnlySpan<byte> message, out ServerNameMsg serverName)
    {
        serverName = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.ServerName)
            || !reader.TryReadI32(out var online)
            || !reader.TryReadI32(out var max)
            || !reader.TryReadString16L(out var worldName)
            || !reader.IsAtEnd)
        {
            return false;
        }

        serverName = new ServerNameMsg(online, max, worldName);
        return true;
    }
}
