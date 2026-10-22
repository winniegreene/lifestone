using Lifestone.Net.Packets;

namespace Lifestone.Net.Messages;

/// <summary>
/// The game actions that name one object, or an item and its target: 0x0036 Use {u32 GUID} (<c>CM_Inventory::Event_UseEvent</c>, 11.4186
/// 0x006AC3B0, 11.6096 0x006AD310; ACE answers with GameEvent 0x01C7 UseDone, after moving the player into range with a
/// MoveToObject when it is too far), 0x0035 UseWithTarget {u32 item, u32 target} (<c>CM_Inventory::Event_UseWithTargetEvent</c>, 11.4186
/// 0x006AC480, 11.6096 0x006AD3E0; the healing kit's and the lockpick's use; M4 3.1 s0) and 0x01BF QueryHealth {u32 GUID}
/// (<c>CM_Combat::Event_QueryHealth</c>, 11.4186 0x006A9B40, 11.6096 0x006AAAA0; ACE answers with GameEvent 0x01C0 UpdateHealth).
/// Lifestone's own, from the retail senders and ACE's <c>GameActionUseItem</c>, <c>GameActionUseWithTarget</c> and
/// <c>GameActionQueryHealth</c>.
/// </summary>
internal static class ObjectActionCodecs
{
    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0036, <paramref name="guid"/>}.</summary>
    public static void WriteUse(PacketWriter writer, uint sequence, uint guid)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.Use);
        writer.WriteUInt32(guid);
    }

    /// <summary>
    /// Writes F7B1 {<paramref name="sequence"/>, 0x0035, <paramref name="item"/>, <paramref name="target"/>}: the item used comes first and the target second,
    /// the order ACE's <c>GameActionUseWithTarget</c> reads them (<c>sourceObjectGuid</c>, then <c>targetObjectGuid</c>).
    /// </summary>
    public static void WriteUseWithTarget(PacketWriter writer, uint sequence, uint item, uint target)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.UseWithTarget);
        writer.WriteUInt32(item);
        writer.WriteUInt32(target);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x01BF, <paramref name="guid"/>}.</summary>
    public static void WriteQueryHealth(PacketWriter writer, uint sequence, uint guid)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.QueryHealth);
        writer.WriteUInt32(guid);
    }
}
