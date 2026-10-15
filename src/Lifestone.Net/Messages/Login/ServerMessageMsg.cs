namespace Lifestone.Net.Messages;

/// <summary>
/// F7E0 ServerMessage (system chat; docs/protocol.md §5.3): a String16L text and an i32 chat type. The server mod's tool
/// replies arrive this way, prefixed <c>#lifestone/1</c>; the state track recognises them.
/// </summary>
/// <param name="Text">The text.</param>
/// <param name="ChatType">The chat message type.</param>
public readonly record struct ServerMessageMsg(string Text, int ChatType);
