// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/GameActionLoginComplete.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Messages; internal;
// renamed LoginCompleteCodec; Build writes into a PacketWriter (Write) through GameActionEnvelope, with the session's
// sequence where OpenAC wrote 0.

using Lifestone.Net.Packets;

namespace Lifestone.Net.Messages;

/// <summary>
/// GameAction 0x00A1 LoginComplete (docs/protocol.md §4.3 row 16): the envelope alone, 12 bytes. The client sends it
/// when it leaves portal space, after entering the world and after every teleport; until then ACE treats the player as
/// teleporting and ignores its positions. Retail sends it from <c>CM_Character::Event_LoginCompleteNotification</c>
/// (11.4186 0x006A1480, 11.6096 0x006A22A0).
/// </summary>
internal static class LoginCompleteCodec
{
    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x00A1}.</summary>
    public static void Write(PacketWriter writer, uint sequence) =>
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.LoginComplete);
}
