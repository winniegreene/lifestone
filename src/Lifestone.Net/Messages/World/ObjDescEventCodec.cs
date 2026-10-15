// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/ObjDescEvent.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: an internal static ObjDescEventCodec over N2's
// WireReader that returns bool instead of a nullable record and never catches; the ModelData is ModelDataCodec's (which
// refuses a marker other than 0x11); the result is the N0 record ObjDescEventMsg.

using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// F625 ObjDescEvent (docs/protocol.md §5.3; ACE <c>WorldObject_Networking.SerializeUpdateModelData</c>): u32 GUID,
/// ModelData, u16 instance sequence, u16 visual-description sequence. Retail reads it in
/// <c>CM_Physics::DispatchSB_ObjDescEvent</c> (11.4186 0x006ACA20, 11.6096 0x006AD980).
/// </summary>
internal static class ObjDescEventCodec
{
    /// <summary>Decodes <paramref name="message"/>; false when it is not a whole, well-formed F625.</summary>
    public static bool TryParse(ReadOnlySpan<byte> message, out ObjDescEventMsg objDesc)
    {
        objDesc = null!;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.ObjDescEvent)
            || !reader.TryReadU32(out var guid)
            || !ModelDataCodec.TryRead(ref reader, out var model)
            || !reader.TryReadU16(out var instance)
            || !reader.TryReadU16(out var sequence)
            || !reader.IsAtEnd)
        {
            return false;
        }

        objDesc = new ObjDescEventMsg(guid, model, instance, sequence);
        return true;
    }
}
