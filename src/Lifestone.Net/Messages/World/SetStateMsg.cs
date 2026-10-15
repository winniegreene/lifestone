using Lifestone.Net.Types;

namespace Lifestone.Net.Messages;

/// <summary>F74B SetState (docs/protocol.md §5.3). LoginComplete makes the server clear Hidden and IgnoreCollisions.</summary>
/// <param name="Guid">The object.</param>
/// <param name="State">The new physics state.</param>
/// <param name="InstanceSeq">The instance sequence.</param>
/// <param name="StateSeq">The state sequence.</param>
public readonly record struct SetStateMsg(uint Guid, PhysicsState State, ushort InstanceSeq, ushort StateSeq);
