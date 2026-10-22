// Ported from eriknihlen/OpenAC@f22b048 src/AcDream.Core.Net/Messages/InventoryActions.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: only BuildNoLongerViewingContents, BuildDropItem,
// BuildStackableSplitToContainer, BuildStackableSplitTo3D, BuildStackableSplitToWield and BuildGiveObjectRequest; namespace
// Lifestone.Net.Messages; internal; the Build* methods returning a fresh byte[] become Write* methods over PacketWriter and
// the GameAction envelope the session's other sends use (GameActionEnvelope.WriteHeader, which owns the F7B1 opcode, the
// sequence and the action type); the local opcode constants move to GameActionTypes; the placement and the amounts are int
// (ACE reads Int32; OpenAC takes uint) and the equip location is a uint EquipMask; XML docs with the retail and ACE cites added.

using Lifestone.Net.Packets;

namespace Lifestone.Net.Messages;

/// <summary>
/// The item-move game actions (M4 2.1 s0), each F7B1 {sequence, type} then 32-bit little-endian fields. ACE's readers
/// (<c>ACE.Server/Network/GameAction/Actions/</c>), field by field:
/// <list type="bullet">
/// <item>0x0195 NoLongerViewingContents {u32 container}: <c>GameActionNoLongerViewingContents</c> reads <c>ReadUInt32</c>.</item>
/// <item>0x001B DropItem {u32 item}: <c>GameActionDropItem</c> reads <c>ReadUInt32</c>.</item>
/// <item>0x0055 StackableSplitToContainer {u32 stack, u32 container, i32 placement, i32 amount}: <c>GameActionStackableSplitToContainer</c>
/// reads <c>ReadUInt32</c>, <c>ReadUInt32</c>, <c>ReadInt32</c>, <c>ReadInt32</c>.</item>
/// <item>0x0056 StackableSplitTo3D {u32 stack, i32 amount}: <c>GameActionStackableSplitTo3D</c> reads <c>ReadUInt32</c>, <c>ReadInt32</c>.</item>
/// <item>0x019B StackableSplitToWield {u32 stack, u32 EquipMask, i32 amount}: <c>GameActionStackableSplitToWield</c> reads
/// <c>ReadUInt32</c>, <c>(EquipMask)ReadInt32</c>, <c>ReadInt32</c>.</item>
/// <item>0x00CD GiveObjectRequest {u32 target, u32 item, i32 amount}: <c>GameActionGiveObjectRequest</c> reads <c>ReadUInt32</c>,
/// <c>ReadUInt32</c>, <c>ReadInt32</c>.</item>
/// </list>
/// </summary>
/// <remarks>
/// The readers check nothing; the amount is checked in <c>Player.HandleActionStackableSplitToContainer</c>, <c>...SplitTo3D</c>,
/// <c>...SplitToWield</c> and <c>HandleActionGiveObjectRequest</c> (Player_Inventory.cs), which refuse an amount of 0 or less
/// (the split ones log it; the 3D split and the give also send a transient string). The codec sends the value as given.
/// The retail senders are <c>TODO(m4-item-move-retail-cites)</c>: none of the six has a named 11.6096 address yet.
/// </remarks>
internal static class ItemMoveActionCodecs
{
    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0195, <paramref name="container"/>}.</summary>
    public static void WriteNoLongerViewingContents(PacketWriter writer, uint sequence, uint container)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.NoLongerViewingContents);
        writer.WriteUInt32(container);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x001B, <paramref name="item"/>}.</summary>
    public static void WriteDropItem(PacketWriter writer, uint sequence, uint item)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.DropItem);
        writer.WriteUInt32(item);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0055, stack, container, placement, amount}.</summary>
    public static void WriteSplitToContainer(PacketWriter writer, uint sequence, uint stack, uint container, int placement, int amount)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.StackableSplitToContainer);
        writer.WriteUInt32(stack);
        writer.WriteUInt32(container);
        writer.WriteInt32(placement);
        writer.WriteInt32(amount);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0056, stack, amount}.</summary>
    public static void WriteSplitTo3D(PacketWriter writer, uint sequence, uint stack, int amount)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.StackableSplitTo3D);
        writer.WriteUInt32(stack);
        writer.WriteInt32(amount);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x019B, stack, equipMask, amount}.</summary>
    public static void WriteSplitToWield(PacketWriter writer, uint sequence, uint stack, uint equipMask, int amount)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.StackableSplitToWield);
        writer.WriteUInt32(stack);
        writer.WriteUInt32(equipMask);
        writer.WriteInt32(amount);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x00CD, target, item, amount}.</summary>
    public static void WriteGiveObject(PacketWriter writer, uint sequence, uint target, uint item, int amount)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.GiveObjectRequest);
        writer.WriteUInt32(target);
        writer.WriteUInt32(item);
        writer.WriteInt32(amount);
    }
}
