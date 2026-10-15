namespace Lifestone.Net.Messages;

/// <summary>F746 PlayerCreate: the GUID of the player's own object, sent on entering the world.</summary>
/// <param name="Guid">The player's GUID.</param>
public readonly record struct PlayerCreateMsg(uint Guid);
