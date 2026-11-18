using Lifestone.Net.Packets;

namespace Lifestone.Net.Messages;

/// <summary>
/// The housing game actions (M6 villa track), each F7B1 {sequence, type} then 32-bit little-endian fields, in ACE's reader order
/// (<c>ACE.Server/Network/GameAction/Actions/</c>):
/// <list type="bullet">
/// <item>0x021C BuyHouse {u32 guardian, u32 count, count x u32 item}: <c>GameActionHouseBuyHouse.cs</c> (<c>ReadUInt32</c>, then
/// <c>ReadListUInt32</c>, <c>PackableList.cs:8-18</c>). The items are the stacks the player put into the guardian window's purchase
/// pane; ACE checks them against the price (<c>Player_House.VerifyPurchase</c>) and takes what the price needs from them, smallest
/// stacks first (<c>HousePayment.GetConsumeItems</c>).</item>
/// <item>0x0221 RentHouse {u32 guardian, u32 count, count x u32 item}: <c>GameActionHouseRentHouse.cs</c>, the same layout.</item>
/// <item>0x021E HouseQuery, 0x021F AbandonHouse and 0x0262 TeleToHouse: no payload.</item>
/// <item>0x0258 QueryLord {u32 guardian}: <c>GameActionHouseQueryLord.cs</c> (ACE reads it and does nothing).</item>
/// </list>
/// Retail's senders are <c>CM_House::Event_BuyHouse</c>, <c>Event_RentHouse</c>, <c>Event_QueryHouse</c>, <c>Event_AbandonHouse</c>,
/// <c>Event_QueryLord</c> and <c>Event_TeleToHouse</c>; their 11.6096 addresses are not in the project's symbol notes yet
/// (<c>TODO(villa-retail-house-senders)</c>), so the layouts here are what ACE reads.
/// </summary>
internal static class HouseActionCodecs
{
    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x021C, guardian, count, items}.</summary>
    public static void WriteBuyHouse(PacketWriter writer, uint sequence, uint guardian, IReadOnlyList<uint> items) =>
        WriteGuardianList(writer, sequence, GameActionTypes.BuyHouse, guardian, items);

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0221, guardian, count, items}.</summary>
    public static void WriteRentHouse(PacketWriter writer, uint sequence, uint guardian, IReadOnlyList<uint> items) =>
        WriteGuardianList(writer, sequence, GameActionTypes.RentHouse, guardian, items);

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x021E}.</summary>
    public static void WriteHouseQuery(PacketWriter writer, uint sequence) =>
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.HouseQuery);

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x021F}.</summary>
    public static void WriteAbandonHouse(PacketWriter writer, uint sequence) =>
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.AbandonHouse);

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0262}.</summary>
    public static void WriteTeleToHouse(PacketWriter writer, uint sequence) =>
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.TeleToHouse);

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0258, guardian}.</summary>
    public static void WriteQueryLord(PacketWriter writer, uint sequence, uint guardian)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.QueryLord);
        writer.WriteUInt32(guardian);
    }

    /// <summary>Refuses a null or empty list (ACE would take nothing and answer nothing); the session calls it before it takes a sequence.</summary>
    public static void ValidateItems(IReadOnlyList<uint> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("a house payment names at least one item: ACE takes nothing from an empty list and answers nothing", nameof(items));
        }
    }

    private static void WriteGuardianList(PacketWriter writer, uint sequence, uint type, uint guardian, IReadOnlyList<uint> items)
    {
        ValidateItems(items);
        GameActionEnvelope.WriteHeader(writer, sequence, type);
        writer.WriteUInt32(guardian);
        writer.WriteUInt32((uint)items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            writer.WriteUInt32(items[i]);
        }
    }
}
