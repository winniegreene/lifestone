namespace Lifestone.Net.Messages;

/// <summary>F74A PickupEvent (docs/protocol.md §5.3): an object leaves the world into someone's inventory.</summary>
/// <param name="Guid">The object.</param>
/// <param name="InstanceSeq">The instance sequence.</param>
/// <param name="PositionSeq">The position sequence.</param>
public readonly record struct PickupEventMsg(uint Guid, ushort InstanceSeq, ushort PositionSeq);
