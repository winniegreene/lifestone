using Lifestone.Net.Packets;

namespace Lifestone.Net.Messages;

/// <summary>
/// The game actions that move an item between the pack and the body (M3 1.2 s4a): each is F7B1 {sequence, type} then 32-bit
/// little-endian fields, nothing else. Lifestone's own, from the retail senders and ACE's handlers (ACE v1.78.4816,
/// <c>Source/ACE.Server/Network/GameAction/Actions/</c>).
/// <list type="bullet">
/// <item>0x001A GetAndWieldItem {u32 item, u32 equip mask}: retail <c>CM_Inventory::Event_GetAndWieldItem</c> (11.4186 0x006AB9F0,
/// 11.6096 0x006AC950), prototype (item, location); ACE <c>GameActionGetAndWieldItem</c> reads <c>ReadUInt32</c> then
/// <c>(EquipMask)ReadInt32</c>: the same four bytes.</item>
/// <item>0x0019 PutItemInContainer {u32 item, u32 container, u32 placement}: retail <c>CM_Inventory::Event_PutItemInContainer</c>
/// (11.4186 0x006ABD20, 11.6096 0x006ACC80), prototype (item, container, location); ACE <c>GameActionPutItemInContainer</c> reads
/// <c>ReadUInt32</c>, <c>ReadUInt32</c>, then <c>ReadInt32</c> (the placement: the slot in the pack, 0 first): the same four bytes.</item>
/// <item>0x0054 StackableMerge {u32 merge from, u32 merge to, i32 amount}: retail <c>CM_Inventory::Event_StackableMerge</c>
/// (11.4186 0x006ABE70, 11.6096 0x006ACDD0); ACE <c>GameActionStackableMerge</c> reads <c>ReadUInt32</c>, <c>ReadUInt32</c>, then
/// <c>ReadInt32</c> (the amount, which ACE refuses at 0 or less): the same four bytes.</item>
/// </list>
/// </summary>
internal static class InventoryActionCodecs
{
    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x001A, <paramref name="item"/>, <paramref name="equipMask"/>}.</summary>
    public static void WriteGetAndWieldItem(PacketWriter writer, uint sequence, uint item, uint equipMask)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.GetAndWieldItem);
        writer.WriteUInt32(item);
        writer.WriteUInt32(equipMask);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0019, <paramref name="item"/>, <paramref name="container"/>, <paramref name="placement"/>}.</summary>
    public static void WritePutItemInContainer(PacketWriter writer, uint sequence, uint item, uint container, uint placement)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.PutItemInContainer);
        writer.WriteUInt32(item);
        writer.WriteUInt32(container);
        writer.WriteUInt32(placement);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0054, <paramref name="mergeFrom"/>, <paramref name="mergeTo"/>, <paramref name="amount"/>}.</summary>
    public static void WriteStackableMerge(PacketWriter writer, uint sequence, uint mergeFrom, uint mergeTo, int amount)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.StackableMerge);
        writer.WriteUInt32(mergeFrom);
        writer.WriteUInt32(mergeTo);
        writer.WriteUInt32(unchecked((uint)amount));
    }
}
