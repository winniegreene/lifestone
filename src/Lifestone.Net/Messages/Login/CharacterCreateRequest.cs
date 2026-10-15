using System.Collections.Immutable;

namespace Lifestone.Net.Messages;

/// <summary>
/// What <see cref="Session.IAcSession.CreateCharacter"/> sends as F656 CharacterCreate (docs/protocol.md §5.2). The
/// session adds the account string from the latest CharacterList and the constant fields; the is-admin and is-sentinel
/// words are always written as 0, since Lifestone never asks for privileges (owner decision, m2 §7).
/// </summary>
/// <param name="Heritage">The heritage group.</param>
/// <param name="Gender">The gender.</param>
/// <param name="Appearance">The 14 appearance choices and 6 shades.</param>
/// <param name="TemplateOption">The template (profession) index, i32.</param>
/// <param name="Strength">Strength.</param>
/// <param name="Endurance">Endurance.</param>
/// <param name="Coordination">Coordination.</param>
/// <param name="Quickness">Quickness.</param>
/// <param name="Focus">Focus.</param>
/// <param name="Self">Self.</param>
/// <param name="Slot">The character slot.</param>
/// <param name="ClassId">The class id.</param>
/// <param name="SkillAdvancementClasses">
/// Exactly <see cref="RequiredSkillCount"/> SkillAdvancementClass words: ACE terminates the session for any other
/// count ("client is not the correct version", PlayerFactory.cs L166).
/// </param>
/// <param name="Name">The character's name: ASCII only (only ASCII round-trips through ACE).</param>
/// <param name="StartArea">The starter area (0 = Holtburg's, which places a new character in the Training Academy).</param>
public readonly record struct CharacterCreateRequest(
    uint Heritage,
    uint Gender,
    CharacterCreateAppearance Appearance,
    int TemplateOption,
    uint Strength,
    uint Endurance,
    uint Coordination,
    uint Quickness,
    uint Focus,
    uint Self,
    uint Slot,
    uint ClassId,
    ImmutableArray<uint> SkillAdvancementClasses,
    string Name,
    uint StartArea)
{
    /// <summary>The number of skills a CharacterCreate must carry (end-of-retail skill table).</summary>
    public const int RequiredSkillCount = 55;

    /// <summary>
    /// Throws <see cref="ArgumentException"/> unless the request has exactly <see cref="RequiredSkillCount"/> skills and
    /// a non-empty name of printable ASCII characters. The session calls it before sending.
    /// </summary>
    public void Validate()
    {
        if (SkillAdvancementClasses.IsDefault || SkillAdvancementClasses.Length != RequiredSkillCount)
        {
            var count = SkillAdvancementClasses.IsDefault ? 0 : SkillAdvancementClasses.Length;
            throw new ArgumentException(
                $"a CharacterCreate needs exactly {RequiredSkillCount} skill advancement classes, not {count}", nameof(SkillAdvancementClasses));
        }

        if (string.IsNullOrEmpty(Name))
        {
            throw new ArgumentException("a CharacterCreate needs a name", nameof(Name));
        }

        foreach (var c in Name)
        {
            if (c is < ' ' or > '~')
            {
                throw new ArgumentException("a character name must be printable ASCII (only ASCII round-trips through ACE)", nameof(Name));
            }
        }
    }
}

/// <summary>The appearance words of a <see cref="CharacterCreateRequest"/>, in wire order: 14 × u32, then 6 × f64 shades.</summary>
/// <param name="Eyes">Eyes strip.</param>
/// <param name="Nose">Nose strip.</param>
/// <param name="Mouth">Mouth strip.</param>
/// <param name="HairColor">Hair colour.</param>
/// <param name="EyeColor">Eye colour.</param>
/// <param name="HairStyle">Hair style.</param>
/// <param name="HeadgearStyle">Headgear style.</param>
/// <param name="HeadgearColor">Headgear colour.</param>
/// <param name="ShirtStyle">Shirt style.</param>
/// <param name="ShirtColor">Shirt colour.</param>
/// <param name="TrousersStyle">Trousers style.</param>
/// <param name="TrousersColor">Trousers colour.</param>
/// <param name="FootwearStyle">Footwear style.</param>
/// <param name="FootwearColor">Footwear colour.</param>
/// <param name="SkinShade">Skin shade.</param>
/// <param name="HairShade">Hair shade.</param>
/// <param name="HeadgearShade">Headgear shade.</param>
/// <param name="ShirtShade">Shirt shade.</param>
/// <param name="TrousersShade">Trousers shade.</param>
/// <param name="FootwearShade">Footwear shade.</param>
public readonly record struct CharacterCreateAppearance(
    uint Eyes,
    uint Nose,
    uint Mouth,
    uint HairColor,
    uint EyeColor,
    uint HairStyle,
    uint HeadgearStyle,
    uint HeadgearColor,
    uint ShirtStyle,
    uint ShirtColor,
    uint TrousersStyle,
    uint TrousersColor,
    uint FootwearStyle,
    uint FootwearColor,
    double SkinShade,
    double HairShade,
    double HeadgearShade,
    double ShirtShade,
    double TrousersShade,
    double FootwearShade);
