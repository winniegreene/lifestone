using System.Numerics;
using Lifestone.Net.Types;

namespace Lifestone.Net.Messages;

/// <summary>
/// F748 UpdatePosition, field for field (docs/protocol.md §5.3; ACE <c>PositionPack</c>). Quaternion components whose
/// "absent" bit is set are 0 in <see cref="AcPosition.Rotation"/>, as on the wire; nothing is normalised. For the
/// player's own object the reducer applies it only when the teleport or force-position sequence advanced.
/// </summary>
/// <param name="Guid">The object.</param>
/// <param name="Flags">The raw position flags.</param>
/// <param name="Position">The cell, origin and raw rotation.</param>
/// <param name="Velocity">The velocity (flag 0x1).</param>
/// <param name="Placement">The placement (flag 0x2).</param>
/// <param name="InstanceSeq">The instance sequence.</param>
/// <param name="PositionSeq">The position sequence.</param>
/// <param name="TeleportSeq">The teleport sequence.</param>
/// <param name="ForcePositionSeq">The force-position sequence.</param>
public readonly record struct UpdatePositionMsg(
    uint Guid,
    PositionFlags Flags,
    AcPosition Position,
    Vector3? Velocity,
    uint? Placement,
    ushort InstanceSeq,
    ushort PositionSeq,
    ushort TeleportSeq,
    ushort ForcePositionSeq)
{
    /// <summary>True when the object is on the ground (flag 0x4).</summary>
    public bool Grounded => (Flags & PositionFlags.Grounded) != 0;
}

/// <summary>The UpdatePosition flags (docs/protocol.md §5.3).</summary>
[Flags]
public enum PositionFlags : uint
{
    /// <summary>No flags: every quaternion component present, no velocity or placement, airborne.</summary>
    None = 0,

    /// <summary>3 × f32 velocity follows the rotation.</summary>
    HasVelocity = 0x1,

    /// <summary>u32 placement follows.</summary>
    HasPlacement = 0x2,

    /// <summary>The object is on the ground.</summary>
    Grounded = 0x4,

    /// <summary>W is absent (0).</summary>
    NoW = 0x8,

    /// <summary>X is absent (0).</summary>
    NoX = 0x10,

    /// <summary>Y is absent (0).</summary>
    NoY = 0x20,

    /// <summary>Z is absent (0).</summary>
    NoZ = 0x40,
}
