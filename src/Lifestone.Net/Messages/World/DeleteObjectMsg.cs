namespace Lifestone.Net.Messages;

/// <summary>F747 ObjectDelete (docs/protocol.md §5.3): u32 GUID, u16 instance sequence, align.</summary>
/// <param name="Guid">The object to delete.</param>
/// <param name="InstanceSeq">The instance sequence it applies to.</param>
public readonly record struct DeleteObjectMsg(uint Guid, ushort InstanceSeq);
