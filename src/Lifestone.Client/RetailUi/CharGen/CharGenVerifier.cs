using System.Diagnostics.CodeAnalysis;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.RetailUi.CharacterCreation;

/// <summary>
/// Retail's check of a finished character: <c>ACCharGenResult::VerifyCharacterGenerationResult</c> (11.4186 0x005C79F0
/// and 0x005C80E0, 11.6096 0x005C89D0 and 0x005C90C0), re-expressed by function name. The main CharGen page runs it
/// before it sends the character; the server checks again.
/// </summary>
/// <remarks>
/// In retail's order, a result passes when:
/// <list type="number">
///   <item>its heritage exists (the Viamontians, 4, need Throne of Destiny) and its gender is 1 or 2;</item>
///   <item>every strip, colour and style index is inside its list (headgear may be -1, none), and every clothing colour
///   is a palette template of its gear's ClothingTable (<c>ClothingTable::GetCloPaletteTemplate</c>);</item>
///   <item>every shade is 0-1;</item>
///   <item>every attribute is 10-100 and together they spend at most the heritage's attribute credits;</item>
///   <item>exactly the SkillTable's skills (38) have an advancement class, each 1-3, and the trained and specialised
///   costs spend at most the heritage's skill credits;</item>
///   <item>the name is 1-32 characters and formatting leaves it unchanged (<see cref="CharGenNames.Format"/>);</item>
///   <item>the template is -1 or one of the heritage's, the start area exists, and Sanamar needs Throne of Destiny.</item>
/// </list>
/// Retail's last check concerns administrator and envoy characters, which Lifestone never creates
/// (<see cref="CharGenResult"/> has no such flags). Every end-of-retail account has Throne of Destiny, so
/// <c>hasThroneOfDestiny</c> defaults to true.
/// </remarks>
public static class CharGenVerifier
{
    /// <summary>The start area that needs Throne of Destiny.</summary>
    public const string ThroneOfDestinyStartArea = "Sanamar";

    /// <summary>The heritage that needs Throne of Destiny (Viamontian).</summary>
    public const uint ThroneOfDestinyHeritage = 4;

    /// <summary>
    /// <c>VerifyCharacterGenerationResult</c>: true when <paramref name="result"/> passes; otherwise
    /// <paramref name="problem"/> names the first check it failed.
    /// </summary>
    public static bool Verify(CharGenResult result, CharGenCatalog catalog, [NotNullWhen(false)] out string? problem, bool hasThroneOfDestiny = true)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(catalog);
        problem = Check(result, catalog, hasThroneOfDestiny);
        return problem is null;
    }

    private static string? Check(CharGenResult r, CharGenCatalog catalog, bool hasThroneOfDestiny)
    {
        var data = catalog.Data;
        if (r.Heritage == 0 || r.Heritage > data.Heritages.Count || !data.TryGetHeritage(r.Heritage, out var hg))
        {
            return $"heritage {r.Heritage} does not exist";
        }

        if (r.Heritage == ThroneOfDestinyHeritage && !hasThroneOfDestiny)
        {
            return "the Viamontian heritage needs Throne of Destiny";
        }

        if (r.Gender is not (1 or 2) || !hg.TryGetGender(r.Gender, out var sx))
        {
            return $"gender {r.Gender} is not 1 or 2";
        }

        if (Outside(r.EyesStrip, sx.EyeStrips.Count) is { } eyes)
        {
            return "eye strip " + eyes;
        }

        if (Outside(r.NoseStrip, sx.NoseStrips.Count) is { } nose)
        {
            return "nose strip " + nose;
        }

        if (Outside(r.MouthStrip, sx.MouthStrips.Count) is { } mouth)
        {
            return "mouth strip " + mouth;
        }

        if (Outside(r.HairColor, sx.HairColors.Count) is { } hairColor)
        {
            return "hair colour " + hairColor;
        }

        if (Outside(r.EyeColor, sx.EyeColors.Count) is { } eyeColor)
        {
            return "eye colour " + eyeColor;
        }

        if (Outside(r.HairStyle, sx.HairStyles.Count) is { } hairStyle)
        {
            return "hair style " + hairStyle;
        }

        if (r.HeadgearStyle >= sx.Headgears.Count || r.HeadgearStyle < -1)
        {
            return $"headgear style {r.HeadgearStyle} is outside -1..{sx.Headgears.Count - 1}";
        }

        if (r.HeadgearStyle > -1 && !data.HasPaletteTemplate(sx.Headgears[r.HeadgearStyle].ClothingTableId, r.HeadgearColor))
        {
            return $"headgear colour 0x{r.HeadgearColor:X} is no palette template of its ClothingTable";
        }

        if (Garment("shirt", r.ShirtStyle, r.ShirtColor, sx.Shirts, data) is { } shirt)
        {
            return shirt;
        }

        if (Garment("trousers", r.TrousersStyle, r.TrousersColor, sx.Pants, data) is { } trousers)
        {
            return trousers;
        }

        if (Garment("footwear", r.FootwearStyle, r.FootwearColor, sx.Footwear, data) is { } footwear)
        {
            return footwear;
        }

        ReadOnlySpan<double> shades = [r.SkinShade, r.HairShade, r.HeadgearShade, r.ShirtShade, r.TrousersShade, r.FootwearShade];
        foreach (var shade in shades)
        {
            if (shade is > 1.0 or < 0.0)
            {
                return $"a shade ({shade}) is outside 0-1";
            }
        }

        ReadOnlySpan<int> attributes = [r.Strength, r.Endurance, r.Coordination, r.Quickness, r.Focus, r.Self];
        var sum = 0;
        foreach (var attribute in attributes)
        {
            if (attribute is < CharGenState.AttributeMin or > CharGenState.AttributeMax)
            {
                return $"an attribute ({attribute}) is outside {CharGenState.AttributeMin}-{CharGenState.AttributeMax}";
            }

            sum += attribute;
        }

        if (sum > hg.AttributeCredits)
        {
            return $"the attributes spend {sum} of {hg.AttributeCredits} credits";
        }

        var classes = 0;
        foreach (var sac in r.SkillAdvancementClasses)
        {
            if (sac == SkillAdvancementClass.Inactive)
            {
                continue;
            }

            if (sac > SkillAdvancementClass.Specialized)
            {
                return $"advancement class {(uint)sac} is not 1-3";
            }

            classes++;
        }

        if (classes != catalog.Skills.Skills.Count)
        {
            return $"{classes} skills have an advancement class; the SkillTable holds {catalog.Skills.Skills.Count}";
        }

        var spent = 0;
        for (uint skill = 0; skill < r.SkillAdvancementClasses.Count; skill++)
        {
            spent += r.SkillAdvancementClasses[(int)skill] switch
            {
                SkillAdvancementClass.Trained => data.GetSkillTrainedCost(skill, r.Heritage, catalog.Skills),
                SkillAdvancementClass.Specialized => data.GetSkillSpecializedCost(skill, r.Heritage, catalog.Skills),
                _ => 0,
            };
        }

        if (spent > hg.SkillCredits)
        {
            return $"the skills spend {spent} of {hg.SkillCredits} credits";
        }

        if (r.Name.Length is 0 or > CharGenNames.MaxLength || !string.Equals(CharGenNames.Format(r.Name), r.Name, StringComparison.Ordinal))
        {
            return "the name is empty, longer than 32 characters, or not as formatting leaves it";
        }

        if (r.TemplateNum >= hg.Templates.Count || r.TemplateNum < -1)
        {
            return $"template {r.TemplateNum} is not one of the heritage's";
        }

        if ((uint)r.StartArea >= (uint)data.StartAreas.Count)
        {
            return $"start area {r.StartArea} does not exist";
        }

        if (data.StartAreas[r.StartArea].Name == ThroneOfDestinyStartArea && !hasThroneOfDestiny)
        {
            return "Sanamar needs Throne of Destiny";
        }

        return null;
    }

    private static string? Outside(int index, int count) => index < count && index >= 0 ? null : $"{index} is outside 0..{count - 1}";

    private static string? Garment(string kind, int style, uint color, IReadOnlyList<CharGenGear> gear, CharGenData data)
    {
        if (style >= gear.Count || style < 0)
        {
            return $"{kind} style {style} is outside 0..{gear.Count - 1}";
        }

        return data.HasPaletteTemplate(gear[style].ClothingTableId, color) ? null : $"{kind} colour 0x{color:X} is no palette template of its ClothingTable";
    }
}
