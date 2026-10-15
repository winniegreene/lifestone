namespace Lifestone.Net.Types;

/// <summary>
/// The header of a GameEvent envelope (F7B0, server to client; docs/protocol.md §3): the player's GUID (0 before
/// entering the world), the event sequence (reset to 1 on entering the world, +1 per event) and the event type.
/// </summary>
/// <param name="PlayerGuid">The player the event is for.</param>
/// <param name="Sequence">The event sequence.</param>
/// <param name="EventType">The event type (0x0004 PopupString, 0x0013 PlayerDescription, 0x02BD Tell, ...).</param>
public readonly record struct GameEventHeader(uint PlayerGuid, uint Sequence, uint EventType);
