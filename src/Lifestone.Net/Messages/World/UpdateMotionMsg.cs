using Lifestone.Net.Types;

namespace Lifestone.Net.Messages;

/// <summary>
/// F74C UpdateMotion (docs/protocol.md §5.3): u32 GUID, u16 instance sequence, then the MovementData with its header
/// (u16 movement sequence, u16 server-control sequence, u8 is-autonomous, align) and its body.
/// </summary>
/// <param name="Guid">The object.</param>
/// <param name="InstanceSeq">The instance sequence.</param>
/// <param name="MovementSeq">The movement sequence.</param>
/// <param name="ServerControlSeq">The server-control sequence.</param>
/// <param name="IsAutonomous">True when the motion echoes the client's own input.</param>
/// <param name="Motion">The movement body (command low words raw).</param>
public sealed record UpdateMotionMsg(
    uint Guid,
    ushort InstanceSeq,
    ushort MovementSeq,
    ushort ServerControlSeq,
    bool IsAutonomous,
    ServerMotionState Motion);
