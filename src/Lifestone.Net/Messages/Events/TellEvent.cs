namespace Lifestone.Net.Messages;

/// <summary>GameEvent 0x02BD Tell (ACE <c>GameEventTell</c>): a private message to the player.</summary>
/// <param name="Text">The message.</param>
/// <param name="SenderName">The sender's name (ACE appends '&amp;' for an Olthoi).</param>
/// <param name="SenderGuid">The sender's GUID.</param>
/// <param name="TargetGuid">The recipient (the player).</param>
/// <param name="ChatType">The chat message type.</param>
/// <param name="Trailing">The trailing u32 ACE writes (0; seen in retail captures).</param>
public readonly record struct TellEvent(string Text, string SenderName, uint SenderGuid, uint TargetGuid, uint ChatType, uint Trailing);
