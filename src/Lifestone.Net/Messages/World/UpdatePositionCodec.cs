// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/UpdatePosition.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: an internal static UpdatePositionCodec over N2's
// WireReader that returns bool instead of a nullable record and never catches; the message is accepted only when every
// byte was read; the result is the N0 struct UpdatePositionMsg (raw flags kept, rotation as sent, absent components 0),
// decoded without allocating.

using System.Numerics;
using Lifestone.Net.Types;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// F748 UpdatePosition (docs/protocol.md §5.3; ACE <c>GameMessageUpdatePosition</c>, <c>PositionPack</c>): u32 GUID, u32
/// flags, u32 cell, 3 × f32 origin, each quaternion component W, X, Y, Z unless its "absent" bit is set (absent ones are
/// 0), the velocity (flag 0x1), the placement (flag 0x2), then the instance, position, teleport and force-position
/// sequences; no Align. Retail reads it in <c>SmartBox::UnpackPositionEvent</c> (11.4186 0x004542C0, 11.6096 0x00454360)
/// through <c>PositionPack::UnPack</c> (11.4186 0x00516740, 11.6096 0x00517240), called from
/// <c>ACSmartBox::DispatchSmartBoxEvent</c> (11.4186 0x005595D0, 11.6096 0x0055A210).
/// </summary>
/// <remarks>The hot path of the world: it decodes into a struct and allocates nothing (m2-plan §3.3 N7,
/// AllocationTests).</remarks>
internal static class UpdatePositionCodec
{
    /// <summary>Decodes <paramref name="message"/>; false when it is not a whole, well-formed F748.</summary>
    public static bool TryParse(ReadOnlySpan<byte> message, out UpdatePositionMsg update)
    {
        update = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.UpdatePosition)
            || !reader.TryReadU32(out var guid)
            || !reader.TryReadU32(out var rawFlags)
            || !PositionCodec.TryReadOrigin(ref reader, out var cell, out var origin))
        {
            return false;
        }

        var flags = (PositionFlags)rawFlags;
        if (!TryReadComponent(ref reader, flags, PositionFlags.NoW, out var w)
            || !TryReadComponent(ref reader, flags, PositionFlags.NoX, out var x)
            || !TryReadComponent(ref reader, flags, PositionFlags.NoY, out var y)
            || !TryReadComponent(ref reader, flags, PositionFlags.NoZ, out var z))
        {
            return false;
        }

        Vector3? velocity = null;
        if ((flags & PositionFlags.HasVelocity) != 0)
        {
            if (!PositionCodec.TryReadVector3(ref reader, out var read))
            {
                return false;
            }

            velocity = read;
        }

        uint? placement = null;
        if ((flags & PositionFlags.HasPlacement) != 0)
        {
            if (!reader.TryReadU32(out var read))
            {
                return false;
            }

            placement = read;
        }

        if (!reader.TryReadU16(out var instance)
            || !reader.TryReadU16(out var position)
            || !reader.TryReadU16(out var teleport)
            || !reader.TryReadU16(out var forcePosition)
            || !reader.IsAtEnd)
        {
            return false;
        }

        update = new UpdatePositionMsg(
            guid, flags, new AcPosition(cell, origin, new Quaternion(x, y, z, w)), velocity, placement, instance, position, teleport, forcePosition);
        return true;
    }

    /// <summary>A quaternion component: 0 when its "absent" bit is set, else an f32.</summary>
    private static bool TryReadComponent(ref WireReader reader, PositionFlags flags, PositionFlags absent, out float value)
    {
        if ((flags & absent) != 0)
        {
            value = 0f;
            return true;
        }

        return reader.TryReadF32(out value);
    }
}
