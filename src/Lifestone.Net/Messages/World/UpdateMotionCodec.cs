// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/UpdateMotion.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: the header half of TryParse as an internal static
// UpdateMotionCodec over N2's WireReader that returns bool and never catches; the ACDREAM_DUMP_MOTION environment read and
// its console write (UpdateMotion.cs:60-65) are gone; the header's Align is read as Align instead of a fixed 2-byte skip;
// the body is MovementDataCodec's (defect 2 fixed there); the message is accepted only when every byte was read; the
// result is the N0 record UpdateMotionMsg.

using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// F74C UpdateMotion (docs/protocol.md §5.3; ACE <c>GameMessageUpdateMotion</c>, <c>MovementData.Write</c> with its
/// header): u32 GUID, u16 instance sequence, u16 movement sequence, u16 server-control sequence, u8 is-autonomous, Align,
/// then the MovementData body (<see cref="MovementDataCodec"/>). Retail reads the GUID and instance sequence in
/// <c>ACSmartBox::DispatchSmartBoxEvent</c> (11.4186 0x005595D0, 11.6096 0x0055A210), the sequences, the autonomous byte
/// and the Align in the three-argument <c>CPhysics::SetObjectMovement</c> (11.4186 0x00509790, 11.6096 0x0050A260), and
/// the body through <c>CPhysicsObj::unpack_movement</c> (11.4186 0x00512040, 11.6096 0x00512B10).
/// </summary>
internal static class UpdateMotionCodec
{
    /// <summary>Decodes <paramref name="message"/>; false when it is not a whole, well-formed F74C.</summary>
    public static bool TryParse(ReadOnlySpan<byte> message, out UpdateMotionMsg update)
    {
        update = null!;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.UpdateMotion)
            || !reader.TryReadU32(out var guid)
            || !reader.TryReadU16(out var instance)
            || !reader.TryReadU16(out var movement)
            || !reader.TryReadU16(out var serverControl)
            || !reader.TryReadU8(out var autonomous)
            || !reader.TryAlign()
            || !MovementDataCodec.TryRead(ref reader, out var motion)
            || !reader.IsAtEnd)
        {
            return false;
        }

        update = new UpdateMotionMsg(guid, instance, movement, serverControl, autonomous != 0, motion);
        return true;
    }
}
