// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/HearSpeech.cs, EmoteText.cs and SoulEmote.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: one internal static class, ChatCodecs, over N2's
// WireReader that returns bool instead of a nullable record and never catches; a message is accepted only when every
// byte was read; strings are CP1252 through WireReader (no length cap); the results are the N0 structs SpeechMsg (the
// range is null for HearSpeech) and EmoteMsg.

using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// Local chat (docs/protocol.md §5.1; m2-plan §1.6 item 24): 0x02BB HearSpeech {text, sender name, u32 sender, u32 chat
/// type}, 0x02BC HearRangedSpeech (an f32 range before the chat type), and 0x01E0 EmoteText and 0x01E2 SoulEmote {u32
/// sender, sender name, text}, as ACE's <c>GameMessageHearSpeech</c>, <c>GameMessageHearRangedSpeech</c>,
/// <c>GameMessageEmoteText</c> and <c>GameMessageSoulEmote</c> write them.
/// </summary>
/// <remarks>
/// Retail reads them in <c>CM_Communication::DispatchUI_HearSpeech</c> (11.4186 0x006A4DB0, 11.6096 0x006A5D10),
/// <c>DispatchUI_HearRangedSpeech</c> (11.4186 0x006A4BB0, 11.6096 0x006A5B10), <c>DispatchUI_HearEmote</c> (11.4186
/// 0x006A4AC0, 11.6096 0x006A5A20) and <c>DispatchUI_HearSoulEmote</c> (11.4186 0x006A4CC0, 11.6096 0x006A5C20).
/// </remarks>
internal static class ChatCodecs
{
    /// <summary>Decodes 0x02BB HearSpeech or 0x02BC HearRangedSpeech; false when it is neither, or not whole.</summary>
    public static bool TryParseSpeech(ReadOnlySpan<byte> message, out SpeechMsg speech)
    {
        speech = default;
        if (!MessageCodec.TryReadOpcode(message, out var opcode) || opcode is not (MessageOpcodes.HearSpeech or MessageOpcodes.HearRangedSpeech))
        {
            return false;
        }

        var reader = new WireReader(message, MessageCodec.OpcodeSize);
        if (!reader.TryReadString16L(out var text) || !reader.TryReadString16L(out var sender) || !reader.TryReadU32(out var senderGuid))
        {
            return false;
        }

        float? range = null;
        if (opcode == MessageOpcodes.HearRangedSpeech)
        {
            if (!reader.TryReadF32(out var read))
            {
                return false;
            }

            range = read;
        }

        if (!reader.TryReadU32(out var chatType) || !reader.IsAtEnd)
        {
            return false;
        }

        speech = new SpeechMsg(text, sender, senderGuid, chatType, range);
        return true;
    }

    /// <summary>Decodes 0x01E0 EmoteText or 0x01E2 SoulEmote; false when it is neither, or not whole.</summary>
    public static bool TryParseEmote(ReadOnlySpan<byte> message, out EmoteMsg emote)
    {
        emote = default;
        if (!MessageCodec.TryReadOpcode(message, out var opcode) || opcode is not (MessageOpcodes.EmoteText or MessageOpcodes.SoulEmote))
        {
            return false;
        }

        var reader = new WireReader(message, MessageCodec.OpcodeSize);
        if (!reader.TryReadU32(out var senderGuid)
            || !reader.TryReadString16L(out var sender)
            || !reader.TryReadString16L(out var text)
            || !reader.IsAtEnd)
        {
            return false;
        }

        emote = new EmoteMsg(senderGuid, sender, text, opcode == MessageOpcodes.SoulEmote);
        return true;
    }
}
