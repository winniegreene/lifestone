namespace Lifestone.Net.Messages;

/// <summary>
/// 0x01E0 EmoteText and 0x01E2 SoulEmote (ACE <c>GameMessageEmoteText</c>, <c>GameMessageSoulEmote</c>): u32 sender
/// GUID, String16L sender name, String16L text.
/// </summary>
/// <param name="SenderGuid">The emoting object.</param>
/// <param name="SenderName">Its name.</param>
/// <param name="Text">The emote text.</param>
/// <param name="IsSoulEmote">True for 0x01E2 SoulEmote.</param>
public readonly record struct EmoteMsg(uint SenderGuid, string SenderName, string Text, bool IsSoulEmote);
