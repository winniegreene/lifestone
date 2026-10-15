namespace Lifestone.Net.Messages;

/// <summary>
/// 0x02BB HearSpeech and 0x02BC HearRangedSpeech (ACE <c>GameMessageHearSpeech</c>, <c>GameMessageHearRangedSpeech</c>):
/// someone nearby said something. The ranged form adds an f32 range before the chat type.
/// </summary>
/// <param name="Text">What was said.</param>
/// <param name="SenderName">The speaker's name.</param>
/// <param name="SenderGuid">The speaker's GUID.</param>
/// <param name="ChatType">The chat message type.</param>
/// <param name="Range">The range (HearRangedSpeech only), else null.</param>
public readonly record struct SpeechMsg(string Text, string SenderName, uint SenderGuid, uint ChatType, float? Range);
