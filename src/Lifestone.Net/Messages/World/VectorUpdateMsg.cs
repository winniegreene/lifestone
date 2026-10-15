using System.Numerics;

namespace Lifestone.Net.Messages;

/// <summary>F74E VectorUpdate (docs/protocol.md §5.3).</summary>
/// <param name="Guid">The object.</param>
/// <param name="Velocity">Its velocity.</param>
/// <param name="Omega">Its angular velocity.</param>
/// <param name="InstanceSeq">The instance sequence.</param>
/// <param name="VectorSeq">The vector sequence.</param>
public readonly record struct VectorUpdateMsg(uint Guid, Vector3 Velocity, Vector3 Omega, ushort InstanceSeq, ushort VectorSeq);
