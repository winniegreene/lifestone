namespace Lifestone.Net.Messages;

/// <summary>
/// One of the player's own PrivateUpdate messages (ACE <c>GameMessagePrivateUpdate*</c>): each starts with a u8
/// sequence. <see cref="Kind"/> says which fields are meaningful:
/// <list type="bullet">
/// <item><see cref="PrivateUpdateKind.PropertyInt"/> and <see cref="PrivateUpdateKind.PropertyInt64"/>:
/// <see cref="Key"/> is the property id, <see cref="Value"/> its value;</item>
/// <item><see cref="PrivateUpdateKind.VitalCurrent"/>: <see cref="Key"/> is the vital id, <see cref="Value"/> its
/// current value;</item>
/// <item><see cref="PrivateUpdateKind.Attribute"/>, <see cref="PrivateUpdateKind.Vital"/> and
/// <see cref="PrivateUpdateKind.Skill"/>: the matching description.</item>
/// </list>
/// </summary>
/// <param name="Kind">Which update (its value is the opcode).</param>
/// <param name="Sequence">The u8 update sequence.</param>
/// <param name="Key">The property, vital, attribute or skill id.</param>
/// <param name="Value">The new value for property and vital-current updates.</param>
/// <param name="Attribute">The attribute (Attribute only).</param>
/// <param name="Vital">The vital (Vital only).</param>
/// <param name="Skill">The skill (Skill only).</param>
public readonly record struct PrivateUpdateMsg(
    PrivateUpdateKind Kind,
    byte Sequence,
    uint Key,
    long Value,
    AttributeDesc Attribute,
    VitalDesc Vital,
    SkillDesc Skill);

/// <summary>The PrivateUpdate messages M2 decodes; each value is the message's opcode.</summary>
public enum PrivateUpdateKind : uint
{
    /// <summary>0x02CD PrivateUpdatePropertyInt.</summary>
    PropertyInt = MessageOpcodes.PrivateUpdatePropertyInt,

    /// <summary>0x02CF PrivateUpdatePropertyInt64.</summary>
    PropertyInt64 = MessageOpcodes.PrivateUpdatePropertyInt64,

    /// <summary>0x02DD PrivateUpdateSkill.</summary>
    Skill = MessageOpcodes.PrivateUpdateSkill,

    /// <summary>0x02E3 PrivateUpdateAttribute.</summary>
    Attribute = MessageOpcodes.PrivateUpdateAttribute,

    /// <summary>0x02E7 PrivateUpdateVital.</summary>
    Vital = MessageOpcodes.PrivateUpdateVital,

    /// <summary>0x02E9 PrivateUpdateAttribute2ndLevel: a vital's current value.</summary>
    VitalCurrent = MessageOpcodes.PrivateUpdateAttribute2ndLevel,
}
