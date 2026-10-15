namespace Lifestone.Net.Messages;

/// <summary>F7E1 ServerName (docs/protocol.md §5.2): the population and the world's name.</summary>
/// <param name="Online">Players online.</param>
/// <param name="Max">The maximum (-1 when unlimited).</param>
/// <param name="WorldName">The world's name.</param>
public readonly record struct ServerNameMsg(int Online, int Max, string WorldName);
