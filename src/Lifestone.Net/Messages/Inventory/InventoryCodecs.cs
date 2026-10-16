using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// The inventory, wield and public-quality messages (M3 1.2 s0), laid out as ACE writes them (<c>ACE.Server/Network/
/// GameMessages/Messages/</c> and <c>GameEvent/Events/</c>). Every parser takes the whole message, opcode first (for a
/// game event, the whole F7B0 message), and accepts it only when every byte was read. The ids are u32 GUIDs
/// (<c>WriteGuid</c>); a GameMessage's u8 sequence comes first and nothing is aligned.
/// </summary>
/// <remarks>
/// <para>The layouts, and where retail handles each (11.6096, with 11.4186 where the symbol table has it):</para>
/// <list type="bullet">
/// <item>GM 0x0024 InventoryRemoveObject: u32 GUID (<c>GameMessageInventoryRemoveObject</c>; <c>Player_Magic.cs:1221</c>,
/// <c>Player_Inventory.cs</c>). TODO(retail-handler-0024): the dispatcher is not in the symbol table (nearest by name:
/// <c>ACCWeenieObject::RemoveContent</c> 0x0058DBA0, 11.4186 0x0058CD70, unverified).</item>
/// <item>GM 0x0197 SetStackSize: u8 sequence, u32 GUID, u32 stack size, u32 value (<c>GameMessageSetStackSize</c>; retail
/// <c>ACCWeenieObject::ServerSaysSetStackSize</c> 0x0058DA70, 11.4186 0x0058CC40).</item>
/// <item>GM 0x02CE PublicUpdatePropertyInt: u8 sequence, u32 GUID, u32 property, i32 value
/// (<c>GameMessagePublicUpdatePropertyInt</c>; <c>CM_Qualities::DispatchUI_UpdateInt</c> 0x006B0000, 11.4186 0x006AF130;
/// <c>ClientObjMaintSystem::Handle_Qualities__UpdateInt</c> 0x005599A0).</item>
/// <item>GM 0x02DA PublicUpdateInstanceId: u8 sequence, u32 GUID, u32 property, u32 value
/// (<c>GameMessagePublicUpdateInstanceID</c>; <c>CM_Qualities::DispatchUI_UpdateInstanceID</c> 0x006AFFB0, 11.4186
/// 0x006AF0E0; <c>ClientObjMaintSystem::Handle_Qualities__UpdateInstanceID</c> 0x00559A80).</item>
/// <item>GE 0x0022 InventoryPutObjInContainer: u32 item, u32 container, u32 placement, u32 container type
/// (<c>GameEventItemServerSaysContainId</c>; <c>ACCWeenieObject::ServerSaysContainID</c> 0x0058CC70, 11.4186
/// 0x0058BE40).</item>
/// <item>GE 0x0023 WieldObject: u32 item, u32 equip mask (<c>GameEventWieldItem</c>). TODO(retail-handler-0023): not in the
/// symbol table (nearest by name: <c>ACCWeenieObject::SetPlayerWieldLocation</c> 0x0058E6F0, 11.4186 0x0058D8C0, unverified).</item>
/// <item>GE 0x019A InventoryPutObjectIn3D: u32 item (<c>GameEventItemServerSaysMoveItem</c>;
/// <c>CPlayerSystem::RecvNotice_ServerSaysMoveItem</c> 0x00564000, 11.4186 0x00563260, which receives the notice
/// <c>ECM_Item::SendNotice_ServerSaysMoveItem</c> 0x006938A0).</item>
/// <item>GE 0x00A0 InventoryServerSaysFailed: u32 item, u32 WeenieError (<c>GameEventInventoryServerSaveFailed</c>;
/// <c>CPlayerSystem::RecvNotice_ServerSaysAttemptFailed</c> 0x0055F1B0, 11.4186 0x0055E490).</item>
/// </list>
/// </remarks>
internal static class InventoryCodecs
{
    /// <summary>GM 0x0024 InventoryRemoveObject.</summary>
    public static bool TryParseRemoveObject(ReadOnlySpan<byte> message, out InventoryRemoveObjectMsg result)
    {
        result = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.InventoryRemoveObject) || !reader.TryReadU32(out var guid) || !reader.IsAtEnd)
        {
            return false;
        }

        result = new InventoryRemoveObjectMsg(guid);
        return true;
    }

    /// <summary>GM 0x0197 SetStackSize.</summary>
    public static bool TryParseSetStackSize(ReadOnlySpan<byte> message, out SetStackSizeMsg result)
    {
        result = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.SetStackSize)
            || !reader.TryReadU8(out var sequence)
            || !reader.TryReadU32(out var guid)
            || !reader.TryReadU32(out var stackSize)
            || !reader.TryReadU32(out var value)
            || !reader.IsAtEnd)
        {
            return false;
        }

        result = new SetStackSizeMsg(sequence, guid, stackSize, value);
        return true;
    }

    /// <summary>GM 0x02CE PublicUpdatePropertyInt.</summary>
    public static bool TryParsePublicUpdatePropertyInt(ReadOnlySpan<byte> message, out PublicUpdatePropertyIntMsg result)
    {
        result = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.PublicUpdatePropertyInt)
            || !reader.TryReadU8(out var sequence)
            || !reader.TryReadU32(out var guid)
            || !reader.TryReadU32(out var property)
            || !reader.TryReadI32(out var value)
            || !reader.IsAtEnd)
        {
            return false;
        }

        result = new PublicUpdatePropertyIntMsg(sequence, guid, property, value);
        return true;
    }

    /// <summary>GM 0x02DA PublicUpdateInstanceId.</summary>
    public static bool TryParsePublicUpdateInstanceId(ReadOnlySpan<byte> message, out PublicUpdateInstanceIdMsg result)
    {
        result = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.PublicUpdateInstanceId)
            || !reader.TryReadU8(out var sequence)
            || !reader.TryReadU32(out var guid)
            || !reader.TryReadU32(out var property)
            || !reader.TryReadU32(out var value)
            || !reader.IsAtEnd)
        {
            return false;
        }

        result = new PublicUpdateInstanceIdMsg(sequence, guid, property, value);
        return true;
    }

    /// <summary>GE 0x0022 InventoryPutObjInContainer.</summary>
    public static bool TryParsePutObjInContainer(ReadOnlySpan<byte> message, out InventoryPutObjInContainerEvent result)
    {
        result = default;
        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        if (!reader.TryReadU32(out var item) || !reader.TryReadU32(out var container) || !reader.TryReadU32(out var placement)
            || !reader.TryReadU32(out var containerType) || !reader.IsAtEnd)
        {
            return false;
        }

        result = new InventoryPutObjInContainerEvent(item, container, placement, containerType);
        return true;
    }

    /// <summary>GE 0x0023 WieldObject.</summary>
    public static bool TryParseWieldObject(ReadOnlySpan<byte> message, out WieldObjectEvent result)
    {
        result = default;
        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        if (!reader.TryReadU32(out var item) || !reader.TryReadU32(out var location) || !reader.IsAtEnd)
        {
            return false;
        }

        result = new WieldObjectEvent(item, location);
        return true;
    }

    /// <summary>GE 0x019A InventoryPutObjectIn3D.</summary>
    public static bool TryParsePutObjectIn3D(ReadOnlySpan<byte> message, out InventoryPutObjectIn3DEvent result)
    {
        result = default;
        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        if (!reader.TryReadU32(out var item) || !reader.IsAtEnd)
        {
            return false;
        }

        result = new InventoryPutObjectIn3DEvent(item);
        return true;
    }

    /// <summary>GE 0x00A0 InventoryServerSaysFailed.</summary>
    public static bool TryParseServerSaysFailed(ReadOnlySpan<byte> message, out InventoryServerSaysFailedEvent result)
    {
        result = default;
        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        if (!reader.TryReadU32(out var item) || !reader.TryReadU32(out var error) || !reader.IsAtEnd)
        {
            return false;
        }

        result = new InventoryServerSaysFailedEvent(item, error);
        return true;
    }
}
