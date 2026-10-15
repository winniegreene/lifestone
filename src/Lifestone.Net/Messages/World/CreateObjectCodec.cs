// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/CreateObject.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: TryParse and TryParseUpdate as one internal
// static CreateObjectCodec over N2's WireReader that returns bool instead of a nullable record and never catches; the
// three blocks are read by ModelDataCodec, PhysicsDescCodec and WeenieDescCodec (each with its own changes); the message
// is accepted only when every byte was read; the result is the N0 record CreateObjectMsg instead of the flat Parsed
// record.

using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// F745 ObjectCreate and F7DB UpdateObject, the same body (docs/protocol.md §5.3; ACE
/// <c>WorldObject_Networking.SerializeCreateObject</c>): u32 GUID, ModelData, PhysicsDesc, WeenieDesc. Retail dispatches
/// them with <c>CM_Physics::DispatchSB_CreateObject</c> (11.4186 0x006AC8D0, 11.6096 0x006AD830) and
/// <c>CM_Physics::DispatchSB_UpdateObject</c> (11.4186 0x006AC7B0, 11.6096 0x006AD710); what the reducer does with them
/// (<c>SmartBox::HandleCreateObject</c>, 11.4186 0x00454C80, 11.6096 0x00454D20; <c>SmartBox::HandleUpdateObject</c>,
/// 11.4186 0x00455580, 11.6096 0x00455620) is the state track's.
/// </summary>
internal static class CreateObjectCodec
{
    /// <summary>
    /// Decodes <paramref name="message"/>, which must start with <paramref name="opcode"/>
    /// (<see cref="MessageOpcodes.ObjectCreate"/> or <see cref="MessageOpcodes.UpdateObject"/>); false when it is not a
    /// whole, well-formed message.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> message, uint opcode, out CreateObjectMsg create)
    {
        create = null!;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, opcode)
            || !reader.TryReadU32(out var guid)
            || !ModelDataCodec.TryRead(ref reader, out var model)
            || !PhysicsDescCodec.TryRead(message, ref reader, out var physics)
            || !WeenieDescCodec.TryRead(ref reader, out var weenie)
            || !reader.IsAtEnd)
        {
            return false;
        }

        create = new CreateObjectMsg(guid, model, physics, weenie);
        return true;
    }
}
