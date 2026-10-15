using Lifestone.Net.Packets;

namespace Lifestone.Net.Messages;

/// <summary>
/// The game actions that name one object: 0x0036 Use {u32 GUID} (<c>CM_Inventory::Event_UseEvent</c>, 11.4186
/// 0x006AC3B0, 11.6096 0x006AD310; ACE answers with GameEvent 0x01C7 UseDone, after moving the player into range with a
/// MoveToObject when it is too far) and 0x01BF QueryHealth {u32 GUID} (<c>CM_Combat::Event_QueryHealth</c>, 11.4186
/// 0x006A9B40, 11.6096 0x006AAAA0; ACE answers with GameEvent 0x01C0 UpdateHealth). Lifestone's own, from the retail
/// senders and ACE's <c>GameActionUseItem</c> and <c>GameActionQueryHealth</c>.
/// </summary>
internal static class ObjectActionCodecs
{
    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0036, <paramref name="guid"/>}.</summary>
    public static void WriteUse(PacketWriter writer, uint sequence, uint guid)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.Use);
        writer.WriteUInt32(guid);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x01BF, <paramref name="guid"/>}.</summary>
    public static void WriteQueryHealth(PacketWriter writer, uint sequence, uint guid)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.QueryHealth);
        writer.WriteUInt32(guid);
    }
}
