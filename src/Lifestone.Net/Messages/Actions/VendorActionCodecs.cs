// Ported from eriknihlen/OpenAC@f22b048 src/AcDream.Core.Net/Messages/VendorRequests.cs and AppraiseRequest.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: only BuildBuy, BuildSell and Build; namespace
// Lifestone.Net.Messages; internal; the methods returning a fresh byte[] become WriteBuy, WriteSell and WriteIdentify over
// PacketWriter and the GameAction envelope the session's other sends use (GameActionEnvelope.WriteHeader, which owns the F7B1
// opcode, the sequence and the action type); the local opcode constants move to GameActionTypes (0x005F, 0x0060, 0x00C8);
// BuildBuy's trailing u32 alternate-currency id is dropped (ACE's reader stops after the last item: the line that read it is
// commented out in GameActionBuyItems.cs:29-32); the items are (item, amount) pairs in that order and the amount is written
// as ACE reads it, an Int32 (OpenAC's tuple is (amount, item)); an amount above Int32.MaxValue or a null list is refused
// before anything is written; XML docs with the ACE and retail cites added.

using Lifestone.Net.Packets;

namespace Lifestone.Net.Messages;

/// <summary>
/// The vendor and appraisal game actions (M4 3.2 s0), each F7B1 {sequence, type} then 32-bit little-endian fields. ACE's readers
/// (<c>ACE.Server/Network/GameAction/Actions/</c>, ACE 95ef426d), field by field:
/// <list type="bullet">
/// <item>0x005F BuyItems {u32 vendor, u32 count, count x {i32 amount, u32 item}}: <c>GameActionBuyItems.cs:12-34</c> reads
/// <c>ReadUInt32</c>, <c>ReadUInt32</c>, then per item <c>ReadInt32</c> (the amount) and <c>ReadUInt32</c> (the guid); nothing follows the
/// last item (the alternate-currency read at 29-32 is commented out).</item>
/// <item>0x0060 SellItems {u32 vendor, u32 count, count x {i32 amount, u32 item}}: <c>GameActionSellItems.cs:12-32</c>, the same
/// layout (the same commented-out trailer at 64-67).</item>
/// <item>0x00C8 IdentifyObject {u32 guid}: <c>GameActionIdentifyObject.cs:9-11</c> reads <c>ReadUInt32</c>.</item>
/// </list>
/// Retail's senders are <c>CM_Vendor::Event_Buy</c> (11.4186 0x006AA0F0, 11.6096 0x006AB050), <c>CM_Vendor::Event_Sell</c> (11.4186
/// 0x006AA000, 11.6096 0x006AAF60) and <c>CM_Item::Event_Appraise</c> (11.4186 0x006A8540, 11.6096 0x006A94A0); their bodies were
/// not read, so whether retail itself ends a buy with an alternate-currency word is unknown (<c>TODO(m4-buy-trailer)</c>; ACE
/// ignores any bytes after the last item, so the layout here is what ACE takes).
/// </summary>
internal static class VendorActionCodecs
{
    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x005F, vendor, count, count x {amount, item}}.</summary>
    public static void WriteBuy(PacketWriter writer, uint sequence, uint vendor, IReadOnlyList<(uint Item, uint Amount)> items)
    {
        WriteTrade(writer, sequence, GameActionTypes.BuyItems, vendor, items);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0060, vendor, count, count x {amount, item}}.</summary>
    public static void WriteSell(PacketWriter writer, uint sequence, uint vendor, IReadOnlyList<(uint Item, uint Amount)> items)
    {
        WriteTrade(writer, sequence, GameActionTypes.SellItems, vendor, items);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x00C8, guid}. A guid of 0 clears the appraisal target and gets no reply (<c>Player.cs:256-263</c>).</summary>
    public static void WriteIdentify(PacketWriter writer, uint sequence, uint guid)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.IdentifyObject);
        writer.WriteUInt32(guid);
    }

    /// <summary>Refuses a null list and an amount above Int32.MaxValue (ACE reads an Int32, so it would arrive negative); the session calls it before it takes a sequence.</summary>
    public static void ValidateTrade(IReadOnlyList<(uint Item, uint Amount)> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Amount > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(items), items[i].Amount, "ACE reads the amount as an Int32; an amount above Int32.MaxValue would arrive negative");
            }
        }
    }

    private static void WriteTrade(PacketWriter writer, uint sequence, uint actionType, uint vendor, IReadOnlyList<(uint Item, uint Amount)> items)
    {
        ValidateTrade(items);
        GameActionEnvelope.WriteHeader(writer, sequence, actionType);
        writer.WriteUInt32(vendor);
        writer.WriteUInt32((uint)items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            writer.WriteInt32((int)items[i].Amount);
            writer.WriteUInt32(items[i].Item);
        }
    }
}
