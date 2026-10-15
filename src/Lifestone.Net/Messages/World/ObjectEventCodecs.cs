// Partly ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/DeleteObject.cs, SetState.cs,
// VectorUpdate.cs, ParentEvent.cs, PickupEvent.cs and SoundEvent.cs (MIT; Copyright (c) 2026 Erik Nihlén and OpenAC
// contributors) with changes: one internal static class, ObjectEventCodecs, with a TryParse per message over N2's
// WireReader that returns bool instead of a nullable record and never catches; each is accepted only when every byte
// was read (DeleteObject's closing Align included, which OpenAC did not check); ParentEvent's location and placement are
// ACE's i32; the results are the N0 structs. PlayerTeleport (F751) and PlayEffect (F755) are Lifestone's own: OpenAC's
// PlayPhysicsScript decodes F754, not F755.

using Lifestone.Net.Types;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// The small, fixed-layout object messages of docs/protocol.md §5.3: F747 ObjectDelete, F74B SetState, F74E
/// VectorUpdate, F751 PlayerTeleport, F749 ParentEvent, F74A PickupEvent, F750 Sound and F755 PlayEffect. Each decodes
/// into a struct without allocating (m2-plan §3.3 N7).
/// </summary>
/// <remarks>
/// Retail reads them in <c>CM_Physics::DispatchSB_DeleteObject</c> (11.4186 0x006AC6A0, 11.6096 0x006AD600),
/// <c>DispatchSB_SetState</c> (11.4186 0x006ACC80, 11.6096 0x006ADBE0), <c>DispatchSB_VectorUpdate</c> (11.4186
/// 0x006ACD20, 11.6096 0x006ADC80), <c>DispatchSB_PlayerTeleport</c> (11.4186 0x006AC730, 11.6096 0x006AD690),
/// <c>DispatchSB_ParentEvent</c> (11.4186 0x006ACAF0, 11.6096 0x006ADA50), <c>DispatchSB_PickupEvent</c> (11.4186
/// 0x006ACBB0, 11.6096 0x006ADB10), <c>DispatchSB_SoundEvent</c> (11.4186 0x006AC760, 11.6096 0x006AD6C0) and
/// <c>DispatchSB_PlayScriptType</c> (11.4186 0x006AC6E0, 11.6096 0x006AD640; F755 is "play script type": a PlayScript
/// value and an intensity, as ACE's <c>GameMessageScript</c> writes it).
/// </remarks>
internal static class ObjectEventCodecs
{
    /// <summary>F747: u32 GUID, u16 instance sequence, Align.</summary>
    public static bool TryParseDelete(ReadOnlySpan<byte> message, out DeleteObjectMsg delete)
    {
        delete = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.ObjectDelete)
            || !reader.TryReadU32(out var guid)
            || !reader.TryReadU16(out var instance)
            || !reader.TryAlign()
            || !reader.IsAtEnd)
        {
            return false;
        }

        delete = new DeleteObjectMsg(guid, instance);
        return true;
    }

    /// <summary>F74B: u32 GUID, u32 PhysicsState, u16 instance sequence, u16 state sequence.</summary>
    public static bool TryParseSetState(ReadOnlySpan<byte> message, out SetStateMsg setState)
    {
        setState = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.SetState)
            || !reader.TryReadU32(out var guid)
            || !reader.TryReadU32(out var state)
            || !reader.TryReadU16(out var instance)
            || !reader.TryReadU16(out var stateSequence)
            || !reader.IsAtEnd)
        {
            return false;
        }

        setState = new SetStateMsg(guid, (PhysicsState)state, instance, stateSequence);
        return true;
    }

    /// <summary>F74E: u32 GUID, 3 × f32 velocity, 3 × f32 omega, u16 instance sequence, u16 vector sequence.</summary>
    public static bool TryParseVector(ReadOnlySpan<byte> message, out VectorUpdateMsg vector)
    {
        vector = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.VectorUpdate)
            || !reader.TryReadU32(out var guid)
            || !PositionCodec.TryReadVector3(ref reader, out var velocity)
            || !PositionCodec.TryReadVector3(ref reader, out var omega)
            || !reader.TryReadU16(out var instance)
            || !reader.TryReadU16(out var vectorSequence)
            || !reader.IsAtEnd)
        {
            return false;
        }

        vector = new VectorUpdateMsg(guid, velocity, omega, instance, vectorSequence);
        return true;
    }

    /// <summary>F751: u16 teleport sequence, Align.</summary>
    public static bool TryParseTeleport(ReadOnlySpan<byte> message, out PlayerTeleportMsg teleport)
    {
        teleport = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.PlayerTeleport)
            || !reader.TryReadU16(out var sequence)
            || !reader.TryAlign()
            || !reader.IsAtEnd)
        {
            return false;
        }

        teleport = new PlayerTeleportMsg(sequence);
        return true;
    }

    /// <summary>F749: u32 parent, u32 child, i32 parent location, i32 placement, u16 parent instance, u16 child position
    /// sequence.</summary>
    public static bool TryParseParent(ReadOnlySpan<byte> message, out ParentEventMsg parent)
    {
        parent = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.ParentEvent)
            || !reader.TryReadU32(out var parentGuid)
            || !reader.TryReadU32(out var childGuid)
            || !reader.TryReadI32(out var location)
            || !reader.TryReadI32(out var placement)
            || !reader.TryReadU16(out var parentInstance)
            || !reader.TryReadU16(out var childPosition)
            || !reader.IsAtEnd)
        {
            return false;
        }

        parent = new ParentEventMsg(parentGuid, childGuid, location, placement, parentInstance, childPosition);
        return true;
    }

    /// <summary>F74A: u32 GUID, u16 instance sequence, u16 position sequence.</summary>
    public static bool TryParsePickup(ReadOnlySpan<byte> message, out PickupEventMsg pickup)
    {
        pickup = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.PickupEvent)
            || !reader.TryReadU32(out var guid)
            || !reader.TryReadU16(out var instance)
            || !reader.TryReadU16(out var position)
            || !reader.IsAtEnd)
        {
            return false;
        }

        pickup = new PickupEventMsg(guid, instance, position);
        return true;
    }

    /// <summary>F750: u32 GUID, u32 sound, f32 volume.</summary>
    public static bool TryParseSound(ReadOnlySpan<byte> message, out SoundMsg sound)
    {
        sound = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.Sound)
            || !reader.TryReadU32(out var guid)
            || !reader.TryReadU32(out var id)
            || !reader.TryReadF32(out var volume)
            || !reader.IsAtEnd)
        {
            return false;
        }

        sound = new SoundMsg(guid, id, volume);
        return true;
    }

    /// <summary>F755: u32 GUID, u32 PlayScript, f32 speed.</summary>
    public static bool TryParseEffect(ReadOnlySpan<byte> message, out PlayEffectMsg effect)
    {
        effect = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.PlayEffect)
            || !reader.TryReadU32(out var guid)
            || !reader.TryReadU32(out var script)
            || !reader.TryReadF32(out var speed)
            || !reader.IsAtEnd)
        {
            return false;
        }

        effect = new PlayEffectMsg(guid, script, speed);
        return true;
    }
}
