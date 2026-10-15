// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/ChatRequests.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: BuildTalk and BuildTell only, as an internal
// static ChatActionCodecs that writes into a PacketWriter through GameActionEnvelope with the session's action sequence;
// Tell's message and target go through the ASCII guard (ACE reads a client string by UTF-8 characters, so anything else
// shifts the field after it), Talk's text, the last field, stays CP1252; both are checked before anything is written.

using Lifestone.Net.Packets;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// The chat game actions: 0x0015 Talk {String16L text} (local speech; ACE runs a text starting with '@' as an admin
/// command) and 0x005D Tell {String16L message, String16L target name}, message first. Retail sends them from
/// <c>CM_Communication::Event_Talk</c> (11.4186 0x006A45C0, 11.6096 0x006A53E0) and
/// <c>CM_Communication::Event_TalkDirectByName</c> (11.4186 0x006A4780, 11.6096 0x006A55A0); ACE reads them in
/// <c>GameActionTalk</c> and <c>GameActionTell</c> (message, then target).
/// </summary>
internal static class ChatActionCodecs
{
    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0015, text}.</summary>
    /// <exception cref="ArgumentException">A character has no CP1252 byte, or the text is too long; nothing is
    /// written.</exception>
    public static void WriteTalk(PacketWriter writer, uint sequence, ReadOnlySpan<char> text)
    {
        _ = PacketWriter.String16LSize(text, "Talk text");
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.Talk);
        writer.WriteString16L(text, "Talk text");
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x005D, message, target}.</summary>
    /// <exception cref="ArgumentException">The target is empty, the message or the target is not ASCII, or either is
    /// too long; nothing is written.</exception>
    public static void WriteTell(PacketWriter writer, uint sequence, ReadOnlySpan<char> target, ReadOnlySpan<char> text)
    {
        if (target.IsEmpty)
        {
            throw new ArgumentException("a Tell needs a target name.", nameof(target));
        }

        AsciiGuard.Require(text, "Tell text");
        AsciiGuard.Require(target, "Tell target");
        _ = PacketWriter.String16LSize(text, "Tell text");
        _ = PacketWriter.String16LSize(target, "Tell target");
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.Tell);
        writer.WriteAsciiString16L(text, "Tell text");
        writer.WriteAsciiString16L(target, "Tell target");
    }
}
