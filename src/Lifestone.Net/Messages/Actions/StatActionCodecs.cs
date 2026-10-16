using Lifestone.Net.Packets;

namespace Lifestone.Net.Messages;

/// <summary>
/// The game actions that spend experience or skill credits (M3 1.1): each is F7B1 {sequence, type} then two 32-bit
/// fields, little-endian, nothing else. Lifestone's own, from the retail senders and ACE's handlers; ACE answers with the
/// private updates 0x02E3, 0x02E7 and 0x02DD (docs/milestones/m3-lights-and-sound.md, 5.3).
/// <list type="bullet">
/// <item>0x0045 RaiseAttribute {u32 attribute, u32 xp}: retail <c>CM_Train::Event_TrainAttribute</c> (11.4186 0x006A7F30,
/// 11.6096 0x006A8E90); ACE <c>GameActionRaiseAttribute</c> reads <c>ReadUInt32</c> (a <c>PropertyAttribute</c>) then <c>ReadUInt32</c> (xpSpent).</item>
/// <item>0x0044 RaiseVital {u32 vital, u32 xp}: retail <c>CM_Train::Event_TrainAttribute2nd</c> (11.4186 0x006A8040,
/// 11.6096 0x006A8FA0); ACE <c>GameActionRaiseVital</c> reads a <c>PropertyAttribute2nd</c> (the vital's maximum id: 1 health,
/// 3 stamina, 5 mana) then a <c>ReadUInt32</c> xpSpent.</item>
/// <item>0x0046 RaiseSkill {u32 skill, u32 xp}: retail <c>CM_Train::Event_TrainSkill</c> (11.4186 0x006A8150, 11.6096 0x006A90B0);
/// ACE <c>GameActionRaiseSkill</c> reads a <c>Skill</c> then a <c>ReadUInt32</c> xpSpent.</item>
/// <item>0x0047 TrainSkill {u32 skill, i32 credits}: retail <c>CM_Train::Event_TrainSkillAdvancementClass</c> (11.4186 0x006A8260,
/// 11.6096 0x006A91C0); ACE <c>GameActionTrainSkill</c> reads a <c>Skill</c> then a <c>ReadInt32</c> creditsSpent: the one signed field.</item>
/// </list>
/// The XP amount is unsigned 32-bit on the wire (retail's prototypes and ACE agree), so the XP to level 126 (4,286,609,098,
/// above <see cref="int.MaxValue"/>) fits in one raise; a signed write would not.
/// </summary>
internal static class StatActionCodecs
{
    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0045, <paramref name="attribute"/>, <paramref name="xp"/>}.</summary>
    public static void WriteRaiseAttribute(PacketWriter writer, uint sequence, uint attribute, uint xp)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.RaiseAttribute);
        writer.WriteUInt32(attribute);
        writer.WriteUInt32(xp);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0044, <paramref name="vital"/>, <paramref name="xp"/>}.</summary>
    public static void WriteRaiseVital(PacketWriter writer, uint sequence, uint vital, uint xp)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.RaiseVital);
        writer.WriteUInt32(vital);
        writer.WriteUInt32(xp);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0046, <paramref name="skill"/>, <paramref name="xp"/>}.</summary>
    public static void WriteRaiseSkill(PacketWriter writer, uint sequence, uint skill, uint xp)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.RaiseSkill);
        writer.WriteUInt32(skill);
        writer.WriteUInt32(xp);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0047, <paramref name="skill"/>, <paramref name="credits"/> as i32}.</summary>
    public static void WriteTrainSkill(PacketWriter writer, uint sequence, uint skill, int credits)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.TrainSkill);
        writer.WriteUInt32(skill);
        writer.WriteInt32(credits);
    }
}
