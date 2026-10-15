namespace Lifestone.Net.Messages;

/// <summary>
/// F751 PlayerTeleport (docs/protocol.md §5.3): the player is entering portal space; u16 teleport sequence, align.
/// Accepted when not older (m2-plan §1.6 item 12).
/// </summary>
/// <param name="TeleportSeq">The teleport sequence.</param>
public readonly record struct PlayerTeleportMsg(ushort TeleportSeq);
