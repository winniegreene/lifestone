using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The examine window's ratings and set block (M5 7 polish, D137): the lines <c>ItemExamineUI::SetAppraiseInfo</c> (11.6096 0x004B7ED0) writes between the tinkering and workmanship (<c>Appraisal_ShowTinkeringInfo</c> 0x004B1510) and the weapon's
/// and armour's data (<c>Appraisal_ShowWeaponAndArmorData</c> 0x004B1780): the set (<see cref="ExamineSetLines.SetLine"/>, <c>Appraisal_ShowSet</c> 0x004AEC00), the gear ratings and vitality (<see cref="RatingLines"/>,
/// <c>Appraisal_ShowRatings</c> 0x004AF400), then one blank line when either showed anything. Pure: the appraisal in, retail's lines out.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the server sends.</b> ACE writes every PropertyInt marked <c>[AssessmentProperty]</c> into an item's appraisal (<c>ACE.Server/Network/Structure/AppraiseInfo.cs:73, 350-352</c>); the gear ratings 370 to 379, 383, 384, 388 and 389
/// carry the mark (<c>ACE.Entity/Enum/Properties/PropertyInt.cs:583-625</c>), as does the set, 265 (<c>PropertyInt.cs:384-385</c>). A creature's own ratings (307 to 315, 381, 382, 386, 387, written by <c>AppraiseInfo.AddRatings</c>,
/// <c>AppraiseInfo.cs:577, 594-658</c>) are the creature window's, not these: the item window never reads them.
/// </para>
/// <para>
/// <b>Retail's order and words</b>, read from the 11.6096 executable: <c>Appraisal_ShowRatings</c> asks for fourteen ints (0x004AF416 to 0x004AF532) and appends, for each one above 0, in this order and joined by ", ", "Dam %d" (370), "Dam Resist
/// %d" (371), "Crit %d" (372), "Crit Dam %d" (374), "Crit Resist %d" (373), "Crit Dam Resist %d" (375), "Overpower% %d" (388), "Overpower Reduction% %d" (389), "PK Dam %d" (383), "PK Dam Resist %d" (384), "Heal Boost %d" (376), "Nether
/// Resist %d" (377), "Life Resist %d" (378) (the literals at 0x007B11A0 down to 0x007B10DC; each is tested against its own stack slot before its <c>sprintf</c>); with any, one line "Ratings: " + the parts + "."; then "This item adds %d
/// Vitality." (0x007B10B4) for a GearMaxHealth (379) above 0; then, when it wrote either, a blank line (<c>AddItemInfo</c> 0x004AE020 of the empty literal 0x007938AF) and it answers true. <c>SetAppraiseInfo</c> adds one more blank
/// line when the set or the ratings answered true (0x004B7FC9 to 0x004B7FF2). Every line is added single-spaced (<c>AddItemInfo</c>'s last argument 1), so each is one row; the blanks are rows retail itself writes.
/// </para>
/// <para>
/// <b>Wiring.</b> In <see cref="GameExaminePanel.ItemLines"/> the block replaces the placeholder that follows <c>TinkeringLines</c> (the "Ratings and set" line and its <c>TODO(m4-examine-ratings)</c>): <c>lines.AddRange(ExamineRatingLines.SetAndRatings(a));</c>.
/// The placeholder's test for 307 to 315 reads the creature's ratings, which retail's item window does not.
/// </para>
/// </remarks>
public static class ExamineRatingLines
{
    /// <summary>PropertyInt 370 (ACE <c>GearDamage</c>).</summary>
    public const uint GearDamage = 370;

    /// <summary>PropertyInt 371 (ACE <c>GearDamageResist</c>).</summary>
    public const uint GearDamageResist = 371;

    /// <summary>PropertyInt 372 (ACE <c>GearCrit</c>).</summary>
    public const uint GearCrit = 372;

    /// <summary>PropertyInt 373 (ACE <c>GearCritResist</c>).</summary>
    public const uint GearCritResist = 373;

    /// <summary>PropertyInt 374 (ACE <c>GearCritDamage</c>).</summary>
    public const uint GearCritDamage = 374;

    /// <summary>PropertyInt 375 (ACE <c>GearCritDamageResist</c>).</summary>
    public const uint GearCritDamageResist = 375;

    /// <summary>PropertyInt 376 (ACE <c>GearHealingBoost</c>).</summary>
    public const uint GearHealingBoost = 376;

    /// <summary>PropertyInt 377 (ACE <c>GearNetherResist</c>).</summary>
    public const uint GearNetherResist = 377;

    /// <summary>PropertyInt 378 (ACE <c>GearLifeResist</c>).</summary>
    public const uint GearLifeResist = 378;

    /// <summary>PropertyInt 379 (ACE <c>GearMaxHealth</c>): the vitality the item adds.</summary>
    public const uint GearMaxHealth = 379;

    /// <summary>PropertyInt 383 (ACE <c>GearPKDamageRating</c>).</summary>
    public const uint GearPKDamage = 383;

    /// <summary>PropertyInt 384 (ACE <c>GearPKDamageResistRating</c>).</summary>
    public const uint GearPKDamageResist = 384;

    /// <summary>PropertyInt 388 (ACE <c>GearOverpower</c>).</summary>
    public const uint GearOverpower = 388;

    /// <summary>PropertyInt 389 (ACE <c>GearOverpowerResist</c>).</summary>
    public const uint GearOverpowerResist = 389;

    /// <summary>The ratings <c>Appraisal_ShowRatings</c> (11.6096 0x004AF400) prints, in its order, each with its format's words before the number.</summary>
    public static IReadOnlyList<(uint Id, string Words)> RatingOrder { get; } =
    [
        (GearDamage, "Dam"), (GearDamageResist, "Dam Resist"), (GearCrit, "Crit"), (GearCritDamage, "Crit Dam"), (GearCritResist, "Crit Resist"), (GearCritDamageResist, "Crit Dam Resist"),
        (GearOverpower, "Overpower%"), (GearOverpowerResist, "Overpower Reduction%"), (GearPKDamage, "PK Dam"), (GearPKDamageResist, "PK Dam Resist"), (GearHealingBoost, "Heal Boost"), (GearNetherResist, "Nether Resist"),
        (GearLifeResist, "Life Resist"),
    ];

    /// <summary>
    /// The whole block <c>ItemExamineUI::SetAppraiseInfo</c> (11.6096 0x004B7ED0) writes after the tinkering lines: the set line, the ratings lines (with their blank) and, when either showed, one more blank line. Empty for an item with
    /// neither, which is most items.
    /// </summary>
    /// <param name="a">The appraisal.</param>
    /// <param name="setExtra">
    /// Lines to put right after the set line, or null for retail. Retail shows no more than "Set: name"; <see cref="ExamineSetLines.WornLines"/> gives the pieces worn and the set's spells, a Lifestone addition the caller shows only behind its
    /// own option (the fidelity rule: retail by default).
    /// </param>
    public static IReadOnlyList<string> SetAndRatings(Appraisal a, IReadOnlyList<string>? setExtra = null)
    {
        ArgumentNullException.ThrowIfNull(a);
        var lines = new List<string>(4);
        var set = ExamineSetLines.SetLine(a);
        if (set is not null)
        {
            lines.Add(set);
            if (setExtra is not null)
            {
                lines.AddRange(setExtra);
            }
        }

        var ratings = RatingLines(a);
        lines.AddRange(ratings);
        if (set is not null || ratings.Count > 0)
        {
            lines.Add(string.Empty); // SetAppraiseInfo's own blank line after the set and ratings
        }

        return lines;
    }

    /// <summary>
    /// <c>ItemExamineUI::Appraisal_ShowRatings</c> (11.6096 0x004AF400): "Ratings: Dam 3, Crit 2." for the gear ratings above 0 in <see cref="RatingOrder"/>, then "This item adds N Vitality." for a GearMaxHealth above 0, then a blank line
    /// when it wrote either; empty when the item has neither.
    /// </summary>
    /// <param name="a">The appraisal.</param>
    public static IReadOnlyList<string> RatingLines(Appraisal a)
    {
        ArgumentNullException.ThrowIfNull(a);
        var lines = new List<string>(3);
        if (RatingsLine(a) is { } ratings)
        {
            lines.Add(ratings);
        }

        if (a.Int(GearMaxHealth) is { } vitality and > 0)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"This item adds {vitality} Vitality."));
        }

        if (lines.Count > 0)
        {
            lines.Add(string.Empty); // Appraisal_ShowRatings' blank line
        }

        return lines;
    }

    /// <summary>The "Ratings: ..." line alone, or null when no gear rating is above 0.</summary>
    /// <param name="a">The appraisal.</param>
    public static string? RatingsLine(Appraisal a)
    {
        ArgumentNullException.ThrowIfNull(a);
        List<string>? parts = null;
        foreach (var (id, words) in RatingOrder)
        {
            if (a.Int(id) is { } value and > 0)
            {
                (parts ??= []).Add(string.Create(CultureInfo.InvariantCulture, $"{words} {value}"));
            }
        }

        return parts is null ? null : "Ratings: " + string.Join(", ", parts) + ".";
    }
}
