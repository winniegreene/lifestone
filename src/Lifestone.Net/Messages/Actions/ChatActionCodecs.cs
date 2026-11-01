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

    /// <summary>
    /// Writes F7B1 {<paramref name="sequence"/>, 0x01DF, text}: the emote text is one String16L, read by ACE's <c>GameActionEmote</c>
    /// (<c>ReadString16L</c>, <c>GameActionEmote.cs:10</c>, @95ef426d); retail's sender is <c>CM_Communication::Event_Emote</c> (11.6096
    /// 0x006A4F40). Like Talk it is the last field, so it stays CP1252.
    /// </summary>
    /// <exception cref="ArgumentException">A character has no CP1252 byte, or the text is too long; nothing is written.</exception>
    public static void WriteEmote(PacketWriter writer, uint sequence, ReadOnlySpan<char> text)
    {
        _ = PacketWriter.String16LSize(text, "Emote text");
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.Emote);
        writer.WriteString16L(text, "Emote text");
    }

    /// <summary>
    /// Writes F7B1 {<paramref name="sequence"/>, 0x00BF, item, inscription} (M5 7 D137): retail <c>CM_Writing::Event_SetInscription</c> (11.6096 0x006AA810); ACE's <c>GameActionSetInscription</c> reads the item's guid, then
    /// <c>ReadString16L</c> (@95ef426d). The inscription is the last field, so it stays CP1252 as Talk's text; an empty one clears the inscription.
    /// </summary>
    /// <exception cref="ArgumentException">A character has no CP1252 byte, or the text is too long; nothing is written.</exception>
    public static void WriteSetInscription(PacketWriter writer, uint sequence, uint item, ReadOnlySpan<char> inscription)
    {
        _ = PacketWriter.String16LSize(inscription, "Inscription");
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.SetInscription);
        writer.WriteUInt32(item);
        writer.WriteString16L(inscription, "Inscription");
    }

    /// <summary>
    /// Writes F7B1 {<paramref name="sequence"/>, 0x000F, afk}: ACE reads <c>Convert.ToBoolean(ReadUInt32())</c>
    /// (<c>GameActionSetAfkMode.cs:10</c>, @95ef426d); retail's sender is <c>CM_Communication::Event_SetAFKMode</c> (11.6096 0x006A4D00).
    /// </summary>
    public static void WriteSetAfkMode(PacketWriter writer, uint sequence, bool afk)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.SetAfkMode);
        writer.WriteUInt32(afk ? 1u : 0u);
    }

    /// <summary>
    /// Writes F7B1 {<paramref name="sequence"/>, 0x0010, message}: one String16L, read by ACE's <c>GameActionSetAFKMessage</c>
    /// (<c>GameActionSetAfkMessage.cs:10</c>, @95ef426d); retail's sender is <c>CM_Communication::Event_SetAFKMessage</c> (11.6096
    /// 0x006A5260). The last field, so CP1252.
    /// </summary>
    /// <exception cref="ArgumentException">A character has no CP1252 byte, or the text is too long; nothing is written.</exception>
    public static void WriteSetAfkMessage(PacketWriter writer, uint sequence, ReadOnlySpan<char> message)
    {
        _ = PacketWriter.String16LSize(message, "AFK message");
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.SetAfkMessage);
        writer.WriteString16L(message, "AFK message");
    }
}
