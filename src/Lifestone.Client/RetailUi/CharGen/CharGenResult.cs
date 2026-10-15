using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.CharacterCreation;

/// <summary>A skill's advancement class (retail's <c>SKILL_ADVANCEMENT_CLASS</c>).</summary>
public enum SkillAdvancementClass : uint
{
    /// <summary>0: the character does not have the skill (a skill id the SkillTable does not hold).</summary>
    Inactive = 0,

    /// <summary>1: usable untrained (if the skill allows it).</summary>
    Untrained = 1,

    /// <summary>2: trained.</summary>
    Trained = 2,

    /// <summary>3: specialised.</summary>
    Specialized = 3,
}

/// <summary>
/// The character CharGen built (retail's <c>ACCharGenResult</c>, filled by <c>CharGenState::GetCharGenResult</c>), in
/// the field order <c>ACCharGenResult::CG_Pack</c> (11.4186 0x005C7200, 11.6096 0x005C81E0) writes. Strips, colours and
/// styles are indices into the gender's CharGen lists, except the clothing colours, which are palette templates; a
/// headgear style of -1 is no headgear; shades are 0-1; -1 is retail's "not chosen" everywhere.
/// </summary>
public sealed record CharGenResult(
    uint Heritage,
    uint Gender,
    int EyesStrip,
    int NoseStrip,
    int MouthStrip,
    int HairColor,
    int EyeColor,
    int HairStyle,
    int HeadgearStyle,
    uint HeadgearColor,
    int ShirtStyle,
    uint ShirtColor,
    int TrousersStyle,
    uint TrousersColor,
    int FootwearStyle,
    uint FootwearColor,
    double SkinShade,
    double HairShade,
    double HeadgearShade,
    double ShirtShade,
    double TrousersShade,
    double FootwearShade,
    int TemplateNum,
    int Strength,
    int Endurance,
    int Coordination,
    int Quickness,
    int Focus,
    int Self,
    int Slot,
    uint ClassId,
    IReadOnlyList<SkillAdvancementClass> SkillAdvancementClasses,
    string Name,
    int StartArea)
{
    /// <summary>
    /// The session's create intent (state's <see cref="CharacterCreateIntent"/>): the same fields, signed values carried
    /// as their 32-bit patterns (a headgear style of -1 becomes 0xFFFFFFFF, as <c>CG_Pack</c> writes it). The slot is the
    /// session's to choose.
    /// </summary>
    public CharacterCreateIntent ToIntent() => new(
        Heritage,
        Gender,
        new CharacterAppearance(
            unchecked((uint)EyesStrip),
            unchecked((uint)NoseStrip),
            unchecked((uint)MouthStrip),
            unchecked((uint)HairColor),
            unchecked((uint)EyeColor),
            unchecked((uint)HairStyle),
            unchecked((uint)HeadgearStyle),
            HeadgearColor,
            unchecked((uint)ShirtStyle),
            ShirtColor,
            unchecked((uint)TrousersStyle),
            TrousersColor,
            unchecked((uint)FootwearStyle),
            FootwearColor,
            SkinShade,
            HairShade,
            HeadgearShade,
            ShirtShade,
            TrousersShade,
            FootwearShade),
        TemplateNum,
        unchecked((uint)Strength),
        unchecked((uint)Endurance),
        unchecked((uint)Coordination),
        unchecked((uint)Quickness),
        unchecked((uint)Focus),
        unchecked((uint)Self),
        ClassId,
        [.. SkillAdvancementClasses.Select(s => (uint)s)],
        Name,
        unchecked((uint)StartArea));
}
