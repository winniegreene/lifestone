using System.Diagnostics.CodeAnalysis;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// A character to create, as the CharGen screens' state produces it (retail UI's <c>CharGenState.GetResult</c>) and
/// <see cref="IActionApi.CreateCharacter"/> takes it. The session turns it into the F656 CharacterCreate request (net's
/// <see cref="CharacterCreateRequest"/>), adding the account string the server sent with the character list, the slot,
/// and the privilege words, which are always 0.
/// </summary>
/// <remarks>
/// Equality compares <see cref="SkillAdvancementClasses"/> by reference, as for any record; compare intents field by
/// field when that matters.
/// </remarks>
/// <param name="Heritage">The heritage group id (CharGen's heritage list).</param>
/// <param name="Gender">The gender (CharGen's gender list for the heritage).</param>
/// <param name="Appearance">The appearance choices and shades.</param>
/// <param name="TemplateOption">The profession template index.</param>
/// <param name="Strength">Strength.</param>
/// <param name="Endurance">Endurance.</param>
/// <param name="Coordination">Coordination.</param>
/// <param name="Quickness">Quickness.</param>
/// <param name="Focus">Focus.</param>
/// <param name="Self">Self.</param>
/// <param name="ClassId">The class id.</param>
/// <param name="SkillAdvancementClasses">
/// Exactly <see cref="RequiredSkillCount"/> SkillAdvancementClass values, indexed by skill id (the end-of-retail skill
/// table): ACE ends the session for any other count.
/// </param>
/// <param name="Name">The character's name: printable ASCII only (only ASCII round-trips through ACE).</param>
/// <param name="StartArea">The starting town (0 = Holtburg's, which places a new character in the Training Academy).</param>
public sealed record CharacterCreateIntent(
    uint Heritage,
    uint Gender,
    CharacterAppearance Appearance,
    int TemplateOption,
    uint Strength,
    uint Endurance,
    uint Coordination,
    uint Quickness,
    uint Focus,
    uint Self,
    uint ClassId,
    IReadOnlyList<uint> SkillAdvancementClasses,
    string Name,
    uint StartArea)
{
    /// <summary>The number of skill advancement classes a create must carry (55; net's <see cref="CharacterCreateRequest.RequiredSkillCount"/>).</summary>
    public const int RequiredSkillCount = CharacterCreateRequest.RequiredSkillCount;

    /// <summary>
    /// Checks what the session checks before sending: exactly <see cref="RequiredSkillCount"/> skills and a non-empty
    /// printable-ASCII name. The CharGen screens check their own credits and rules first.
    /// </summary>
    /// <param name="problem">Why the intent cannot be sent, or null.</param>
    /// <returns>True when it can be sent.</returns>
    public bool TryValidate([NotNullWhen(false)] out string? problem)
    {
        var count = SkillAdvancementClasses?.Count ?? 0;
        if (count != RequiredSkillCount)
        {
            problem = $"a character needs exactly {RequiredSkillCount} skill advancement classes, not {count}";
            return false;
        }

        if (string.IsNullOrEmpty(Name))
        {
            problem = "a character needs a name";
            return false;
        }

        foreach (var c in Name)
        {
            if (c is < ' ' or > '~')
            {
                problem = "a character name must be printable ASCII (only ASCII round-trips through ACE)";
                return false;
            }
        }

        problem = null;
        return true;
    }

    /// <summary>
    /// The F656 request for this intent in character slot <paramref name="slot"/>. The intent must validate
    /// (<see cref="TryValidate"/>); the request validates the same rules again.
    /// </summary>
    /// <param name="slot">The character slot the request names.</param>
    /// <returns>The request.</returns>
    /// <exception cref="ArgumentException">The intent does not validate.</exception>
    internal CharacterCreateRequest ToRequest(uint slot)
    {
        if (!TryValidate(out var problem))
        {
            throw new ArgumentException(problem, nameof(CharacterCreateIntent));
        }

        var request = new CharacterCreateRequest(
            Heritage,
            Gender,
            Appearance.ToWire(),
            TemplateOption,
            Strength,
            Endurance,
            Coordination,
            Quickness,
            Focus,
            Self,
            slot,
            ClassId,
            [.. SkillAdvancementClasses],
            Name,
            StartArea);
        request.Validate();
        return request;
    }
}

/// <summary>
/// A character's appearance choices, as CharGen's appearance page sets them: 14 choices (strip, colour and style
/// indices) and 6 shades. The order is the wire order of F656 CharacterCreate.
/// </summary>
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
public readonly record struct CharacterAppearance(
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
    double FootwearShade)
{
    /// <summary>The same values as net's wire record.</summary>
    internal CharacterCreateAppearance ToWire() => new(
        Eyes,
        Nose,
        Mouth,
        HairColor,
        EyeColor,
        HairStyle,
        HeadgearStyle,
        HeadgearColor,
        ShirtStyle,
        ShirtColor,
        TrousersStyle,
        TrousersColor,
        FootwearStyle,
        FootwearColor,
        SkinShade,
        HairShade,
        HeadgearShade,
        ShirtShade,
        TrousersShade,
        FootwearShade);
}
