using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>Which creature window retail fills (<c>gmExaminationUI::SetAppraiseInfo</c>, 11.6096 0x004ADE60): a monster or an NPC, or a player.</summary>
public enum ExamineCreatureKind
{
    /// <summary><c>CreatureExamineUI</c>: a creature with a creature profile and neither PropertyString 5 (Template) nor PropertyInt 261 (CharacterTitleId).</summary>
    Creature,

    /// <summary><c>CharExamineUI</c>: a creature profile and PropertyString 5 or PropertyInt 261, which a player's appraisal carries.</summary>
    Character,
}

/// <summary>
/// The font colour index a value is drawn in (<c>UIElement_Text::SetTextWithFont</c>'s last argument; the text element's colour array gives the colour). The numbers are retail's (<c>AttributeInfoRegion::Update</c>, 11.6096
/// 0x004F29F0, and <c>Attribute2ndInfoRegion::Update</c>, 0x004F2AE0, for an appraisal).
/// </summary>
public static class ExamineColor
{
    /// <summary>A plain value: not enchanted, or a row with no colour.</summary>
    public const int Normal = 0;

    /// <summary>The value is raised by an enchantment (the profile's highlight bit is set and its colour bit is set); in the society row, the viewer's own society.</summary>
    public const int Raised = 1;

    /// <summary>The value is lowered by an enchantment (the highlight bit set, the colour bit clear); in the society row, a society the viewer's is at odds with.</summary>
    public const int Lowered = 2;

    /// <summary>The roll failed (<see cref="Appraisal.Success"/> false): every attribute and vital value is drawn in this colour.</summary>
    public const int Unassessed = 3;
}

/// <summary>One row of a list: a label, a value and the colour index both are drawn in.</summary>
/// <param name="Label">The label text (element 0x1000012A of the list's row template).</param>
/// <param name="Value">The value text (element 0x1000012B of the row template).</param>
/// <param name="Color">The font colour index (<see cref="ExamineColor"/>).</param>
public readonly record struct ExamineRow(string Label, string Value, int Color)
{
    /// <summary>Retail's spacer row: an empty label and an empty value (the empty string at 11.6096 0x00795320).</summary>
    public static ExamineRow Blank => new(string.Empty, string.Empty, ExamineColor.Normal);

    /// <summary>True for a spacer row.</summary>
    public bool IsBlank => Label.Length == 0 && Value.Length == 0;
}

/// <summary>
/// The dat tables the creature window's names come from (<see cref="ExamineNameTables"/> reads the real ones): retail's enum-to-name maps (<c>EnumMapper::GetString</c>, 11.6096 0x0041B060) and its string tables
/// (<c>StringInfo::GetString</c>, 11.6096 0x0042E980).
/// </summary>
public interface IExamineNames
{
    /// <summary>The name <paramref name="value"/> has in the enum map <paramref name="mapId"/> as the table spells it, or null when absent.</summary>
    string? EnumName(uint mapId, int value);

    /// <summary>The text of the string <paramref name="token"/> (hashed with <c>compute_str_hash</c>) in the table the STRINGTABLE enum <paramref name="tableEnum"/> names, or null when the table is not there.</summary>
    string? StringText(uint tableEnum, string token);
}

/// <summary>What the creature window needs besides the appraisal.</summary>
/// <param name="ObjectName">The object's name (<c>ACCWeenieObject::GetObjectName</c>, NAME_APPROPRIATE): the character's full name is built on it.</param>
/// <param name="DescriptionFlags">The object's description bits from its CreateObject (<c>WorldObject.DescriptionFlags</c>, retail's <c>PublicWeenieDesc::_bitfield</c>): the player-killer standing is read from them.</param>
/// <param name="ViewerFactionBits">The viewer's own PropertyInt 281 (Faction1Bits) from its player description, 0 when it has none (retail's default: <see cref="ExamineCreatureLines.ViewerFactionBitsOf"/>).</param>
/// <param name="Names">The dat tables, or null when the panel has none wired (every name then is a loud placeholder).</param>
public sealed record ExamineCreatureContext(string ObjectName, uint DescriptionFlags, int ViewerFactionBits, IExamineNames? Names);

/// <summary>
/// The creature window's content (<c>BasicCreatureExamineUI</c>, base field 0x10000140, and its two sub-windows). A null text means retail leaves that element as it is; the panel clears it on open (a fresh window: retail would show the
/// previous object's text there, a glitch not kept).
/// </summary>
/// <param name="Kind">Which sub-window: the creature panel 0x1000014D or the character panel 0x1000014F.</param>
/// <param name="Level">The level value text 0x1000014C (<c>BasicCreatureExamineUI::SetLevelValueText</c>, 11.6096 0x004B4510): the level, or "???" for none or 0.</param>
/// <param name="Stats">The attribute list 0x10000149: nine rows in retail's order (Strength, Endurance, Coordination, Quickness, Focus, Self, Health, Stamina, Mana), each with its colour.</param>
/// <param name="Info">The extra-info list 0x10000335 (<c>BasicCreatureExamineUI::AddLineToMiscInfo</c>, 11.6096 0x004ABF30), flushed and refilled in order; <see cref="ExamineRow.Blank"/> rows are retail's spacers.</param>
/// <param name="CreatureTypeName">Creature panel: text 0x1000014E, the creature type's name, or null (retail leaves it when PropertyInt 2 is absent or not in the table).</param>
/// <param name="FullName">Character panel: the window title 0x1000012D, the allegiance title and the name (<c>AllegianceData::GetFullName</c>, 11.6096 0x005B7A40); null for a creature (the title is the panel's usual name).</param>
/// <param name="HeritageText">Character panel: text 0x10000150, the gender and heritage (<c>AppraisalSystem::InqGenderHeritageDisplay</c>, 11.6096 0x005B6B90).</param>
/// <param name="ProfessionText">Character panel: text 0x10000151, the character title (PropertyInt 261 through the title table, else PropertyString 5), or null (retail leaves it).</param>
/// <param name="PlayerKillerText">Character panel: text 0x10000152, the player-killer standing.</param>
/// <param name="AllegianceNameText">Text 0x1000053A: the allegiance name for a character with an allegiance rank and PropertyString 47, else empty (retail clears it for a creature and for a character).</param>
public sealed record ExamineCreaturePage(
    ExamineCreatureKind Kind,
    string Level,
    IReadOnlyList<ExamineRow> Stats,
    IReadOnlyList<ExamineRow> Info,
    string? CreatureTypeName,
    string? FullName,
    string? HeritageText,
    string? ProfessionText,
    string? PlayerKillerText,
    string AllegianceNameText);

/// <summary>
/// The creature window's lines (M5 7, D137; closes <c>TODO(m4-examine-creature-lines)</c> when wired): a pure producer from an appraisal to retail's level, attribute rows, information rows and sub-window texts. Re-expressed from the 11.6096
/// client (the 11.4186 decompile's names, byte-matched to 11.6096 addresses; the 11.6096 literals and the property ids each function reads were read from the 11.6096 executable): <c>gmExaminationUI::SetAppraiseInfo</c> 0x004ADE60 picks
/// the window, <c>BasicCreatureExamineUI::SetAppraiseInfo</c> 0x004B4610 writes the level and updates the nine rows of the attribute list (built by the constructor 0x004AD0B0 in the order Strength, Endurance, Coordination,
/// Quickness, Focus, Self, then Health with a percentage, Stamina and Mana), <c>CreatureExamineUI::SetAppraiseInfo</c> 0x004B4690 the creature type and the rating rows, and <c>CharExamineUI::SetAppraiseInfo</c> (0x004B4EF0, the
/// function after it, unnamed in the symbol map; 11.4186 0x004B45F0) the character's texts and rows.
/// </summary>
/// <remarks>
/// <para>
/// <b>What ACE sends for a creature</b> (vendor-eval/ACE/Source/ACE.Server): <c>Player.Examine</c> (<c>WorldObjects/Player.cs:302-330</c>) rolls Assess Creature (Assess Person for a player) against the target's Deception, and a repeat
/// within 5 s of a failed roll answers failed without a roll (<c>Player.cs:289</c>). <c>AppraiseInfo.BuildCreature</c> (<c>Network/Structure/AppraiseInfo.cs:566-592</c>) always builds the creature profile; it is flagged unless
/// the NPC looks like an object (<c>AppraiseInfo.cs:723-724</c>), when retail's item window shows it. The profile (<c>Network/Structure/CreatureProfile.cs:47-74</c>) always holds the health and its maximum; only a success adds the
/// six attributes, the stamina and mana and their maxima (flag 0x8) and the enchantment masks (flag 0x1, <c>Network/Enum/AttributeMask.cs:8-19</c>: bit 0x1 Strength to 0x20 Self, 0x40 Health, 0x80 Stamina, 0x100 Mana; the colour
/// bit set is a raise). The ratings (PropertyInt 307 to 387) are added only on a success (<c>AppraiseInfo.cs:594-597</c>), the nine armour levels only on a success and only for a player or an unattackable creature
/// (<c>AppraiseInfo.cs:574-575</c>; <c>ArmorLevel.cs:62-65</c> adds 9999 when every layer is unenchantable). The property tables come in full either way.
/// </para>
/// <para>
/// <b>A failed roll</b> has no text of its own in retail (the 11.6096 executable has no failed-appraisal string): every attribute and vital value is drawn in colour 3 (<see cref="ExamineColor.Unassessed"/>), the attributes and the
/// stamina and mana read "???", the health reads its percentage alone ("55 %", <c>MulDiv(100, current, maximum)</c>), the level still shows, and the rating and armour rows are absent because ACE does not send them.
/// </para>
/// <para>
/// <b>The literals</b> are the 11.6096 executable's: the labels "Dmg/CritDmg" (0x007B2078), "Overpower %" (0x007B2048), "PK Dmg/Res" (0x007B2030), "DoT/Life:" (0x007B201C), the society, allegiance, armour and character labels
/// (0x007B20A0 to 0x007B23A8), the vital formats "%d/%d (%d %%)" (0x007C4F88), "%d/%d" (0x007C4F44) and "%d %%" (0x007C4F7C). The rating formats are "%Rating: %d/%d" (0x007B2090) and "%Resist: %d/%d" (0x007B2068): "%R" is not a
/// conversion, and the C runtime's non-validating formatter writes the 'R' as text, so retail reads "Rating: 10/5"; that reading is this producer's.
/// </para>
/// </remarks>
public static class ExamineCreatureLines
{
    /// <summary>PropertyInt 2 CreatureType.</summary>
    public const uint CreatureTypeId = 2;

    /// <summary>PropertyString 5 Template (a player's).</summary>
    public const uint TemplateId = 5;

    /// <summary>PropertyString 10 Fellowship.</summary>
    public const uint FellowshipNameId = 10;

    /// <summary>PropertyString 21 MonarchsTitle (ACE writes the monarch's title and name).</summary>
    public const uint MonarchId = 21;

    /// <summary>PropertyString 35 PatronsTitle.</summary>
    public const uint PatronId = 35;

    /// <summary>PropertyString 43 DateOfBirth.</summary>
    public const uint DateOfBirthId = 43;

    /// <summary>PropertyString 47 AllegianceName.</summary>
    public const uint AllegianceNameId = 47;

    /// <summary>PropertyInt 30 AllegianceRank.</summary>
    public const uint AllegianceRankId = 30;

    /// <summary>PropertyInt 35 AllegianceFollowers (a monarch's).</summary>
    public const uint AllegianceFollowersId = 35;

    /// <summary>PropertyInt 43 NumDeaths.</summary>
    public const uint DeathsId = 43;

    /// <summary>PropertyInt 113 Gender.</summary>
    public const uint GenderId = 113;

    /// <summary>PropertyInt 125 Age (seconds played).</summary>
    public const uint AgeId = 125;

    /// <summary>PropertyInt 181 ChessRank.</summary>
    public const uint ChessRankId = 181;

    /// <summary>PropertyInt 188 HeritageGroup.</summary>
    public const uint HeritageId = 188;

    /// <summary>PropertyInt 192 FakeFishingSkill.</summary>
    public const uint FishingSkillId = 192;

    /// <summary>PropertyInt 261 CharacterTitleId.</summary>
    public const uint CharacterTitleId = 261;

    /// <summary>PropertyInt 262 NumCharacterTitles.</summary>
    public const uint TitlesEarnedId = 262;

    /// <summary>PropertyInt 281 Faction1Bits (the society: 1 Celestial Hand, 2 Eldrytch Web, 4 Radiant Blood).</summary>
    public const uint FactionBitsId = 281;

    /// <summary>PropertyInt 390 Enlightenment.</summary>
    public const uint EnlightenmentId = 390;

    /// <summary>The armour level at or above which a body part's armour is unenchantable: retail prints "*" and the level less this (ACE <c>ArmorLevel.cs:62-65</c>).</summary>
    public const int UnenchantableOffset = 9999;

    // The ratings (ACE PropertyInt): 307 DamageRating, 308 DamageResistRating, 313 CritRating, 314 CritDamageRating, 315 CritResistRating, 316 CritDamageResistRating, 350 DotResistRating, 351 LifeResistRating,
    // 381 PKDamageRating, 382 PKDamageResistRating, 386 Overpower, 387 OverpowerResist. Retail also reads 323 HealingBoostRating and shows nothing for it.
    private const uint DamageRating = 307, DamageResistRating = 308, CritRating = 313, CritDamageRating = 314, CritResistRating = 315, CritDamageResistRating = 316;
    private const uint DotResistRating = 350, LifeResistRating = 351, PkDamageRating = 381, PkDamageResistRating = 382, Overpower = 386, OverpowerResist = 387;

    // The attribute list's six attribute rows in the constructor's order (11.6096 0x004AD0B0): retail's attribute id and the enchantment mask bit (ACE AttributeMask).
    private static readonly (string Name, int Id, uint Bit)[] Attributes =
    [
        ("Strength", 1, 0x1), ("Endurance", 2, 0x2), ("Coordination", 4, 0x8), ("Quickness", 3, 0x4), ("Focus", 5, 0x10), ("Self", 6, 0x20),
    ];

    /// <summary>
    /// Which window retail shows an appraisal in (<c>gmExaminationUI::SetAppraiseInfo</c>, 11.6096 0x004ADE60): null for no creature profile (the item window), <see cref="ExamineCreatureKind.Character"/> when PropertyString 5 or
    /// PropertyInt 261 is present, else <see cref="ExamineCreatureKind.Creature"/>.
    /// </summary>
    public static ExamineCreatureKind? KindOf(Appraisal appraisal)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        if (appraisal.Creature is null)
        {
            return null;
        }

        return appraisal.String(TemplateId) is not null || appraisal.Int(CharacterTitleId) is not null ? ExamineCreatureKind.Character : ExamineCreatureKind.Creature;
    }

    /// <summary>The creature window's content for <paramref name="appraisal"/>, or null when it has no creature profile (retail's item window shows it). Each name the context cannot give is logged as a named TODO.</summary>
    public static ExamineCreaturePage? Build(Appraisal appraisal, ExamineCreatureContext context, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(log);
        if (KindOf(appraisal) is not { } kind)
        {
            return null;
        }

        var level = LevelText(appraisal);
        var stats = StatRows(appraisal);
        if (kind == ExamineCreatureKind.Creature)
        {
            // CreatureExamineUI::SetAppraiseInfo (0x004B4690): the type's name, the list flushed, the note 0x1000053A cleared, the ratings, then the shared part.
            return new ExamineCreaturePage(kind, level, stats, RatingRows(appraisal, character: false), CreatureTypeName(appraisal, context.Names, log), null, null, null, null, string.Empty);
        }

        var info = new List<ExamineRow>();
        if (SocietyRow(appraisal, context.ViewerFactionBits) is { } society)
        {
            info.Add(society);
        }

        var rank = appraisal.Int(AllegianceRankId) ?? 0;
        var allegianceName = string.Empty;
        if (rank >= 1)
        {
            allegianceName = appraisal.String(AllegianceNameId) ?? string.Empty;
            info.AddRange(AllegianceRows(appraisal));
        }

        info.AddRange(ArmorRows(appraisal));
        info.AddRange(RatingRows(appraisal, character: true));
        info.AddRange(CharacterRows(appraisal));
        info.Add(new ExamineRow("* = Unenchantable", string.Empty, ExamineColor.Normal));
        return new ExamineCreaturePage(
            kind,
            level,
            stats,
            info,
            null,
            FullName(appraisal, context, rank),
            HeritageText(appraisal, context.Names, log),
            ProfessionText(appraisal, context.Names, log),
            PlayerKillerText(context.DescriptionFlags),
            allegianceName);
    }

    /// <summary><c>BasicCreatureExamineUI::SetLevelValueText</c> (11.6096 0x004B4510) with PropertyInt 25 (0 when absent): "???" for 0 or less, else the number.</summary>
    public static string LevelText(Appraisal appraisal)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        return appraisal.Level is { } level and > 0 ? level.ToString(CultureInfo.InvariantCulture) : "???";
    }

    /// <summary>
    /// The nine rows of the attribute list (<c>AttributeInfoRegion::Update</c>, 11.6096 0x004F29F0, and <c>Attribute2ndInfoRegion::Update</c>, 0x004F2AE0, for an appraisal). An attribute reads its value, or "???" when it is 0 or
    /// absent. A vital reads "current/maximum" (Health adds " (percent %)") on a success; on a failure Health reads "percent %" and Stamina and Mana "???"; a vital whose current or maximum is 0 or absent reads "???". The colour is
    /// <see cref="ExamineColor.Unassessed"/> for a failed roll, else <see cref="ExamineColor.Raised"/> or <see cref="ExamineColor.Lowered"/> when the profile's highlight bit (the maximum's, for a vital) is set, else normal.
    /// </summary>
    public static IReadOnlyList<ExamineRow> StatRows(Appraisal appraisal)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        var rows = new List<ExamineRow>(9);
        foreach (var (name, id, bit) in Attributes)
        {
            var text = appraisal.CreatureAttribute(id) is { } n and not 0 ? n.ToString(CultureInfo.InvariantCulture) : "???";
            rows.Add(new ExamineRow(name, text, StatColor(appraisal, bit)));
        }

        rows.Add(VitalRow(appraisal, "Health", appraisal.CreatureHealth, appraisal.CreatureHealthMax, showPercent: true, 0x40));
        rows.Add(VitalRow(appraisal, "Stamina", appraisal.CreatureStamina, appraisal.CreatureStaminaMax, showPercent: false, 0x80));
        rows.Add(VitalRow(appraisal, "Mana", appraisal.CreatureMana, appraisal.CreatureManaMax, showPercent: false, 0x100));
        return rows;
    }

    /// <summary>
    /// The rating rows, the same in both windows but in two orders: the creature's (11.6096 0x004B4690) damage rating, damage resistance, overpower, PK damage, DoT and life; the character's (0x004B4EF0) damage rating, damage
    /// resistance, PK damage, overpower, DoT and life. Each row appears when one of its ratings is above 0; a spacer row goes before the first and after the last.
    /// </summary>
    public static IReadOnlyList<ExamineRow> RatingRows(Appraisal appraisal, bool character)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        int R(uint id) => appraisal.Int(id) ?? 0;
        var damage = R(DamageRating) > 0 || R(CritRating) > 0 || R(CritDamageRating) > 0
            ? new ExamineRow("Dmg/CritDmg", Pair("Rating: ", R(DamageRating), R(CritDamageRating)), ExamineColor.Normal) : (ExamineRow?)null;
        var resist = R(DamageResistRating) > 0 || R(CritResistRating) > 0 || R(CritDamageResistRating) > 0
            ? new ExamineRow("Dmg/CritDmg", Pair("Resist: ", R(DamageResistRating), R(CritDamageResistRating)), ExamineColor.Normal) : (ExamineRow?)null;
        var overpower = R(Overpower) > 0 || R(OverpowerResist) > 0
            ? new ExamineRow("Overpower %", string.Create(CultureInfo.InvariantCulture, $"+{R(Overpower)}/-{R(OverpowerResist)}"), ExamineColor.Normal) : (ExamineRow?)null;
        var pk = R(PkDamageRating) > 0 || R(PkDamageResistRating) > 0
            ? new ExamineRow("PK Dmg/Res", Pair("Rating: ", R(PkDamageRating), R(PkDamageResistRating)), ExamineColor.Normal) : (ExamineRow?)null;
        var dot = R(DotResistRating) > 0 || R(LifeResistRating) > 0
            ? new ExamineRow("DoT/Life:", Pair("Resist: ", R(DotResistRating), R(LifeResistRating)), ExamineColor.Normal) : (ExamineRow?)null;
        ExamineRow?[] order = character ? [damage, resist, pk, overpower, dot] : [damage, resist, overpower, pk, dot];
        var rows = new List<ExamineRow>(7);
        foreach (var row in order)
        {
            if (row is { } r)
            {
                if (rows.Count == 0)
                {
                    rows.Add(ExamineRow.Blank);
                }

                rows.Add(r);
            }
        }

        if (rows.Count > 0)
        {
            rows.Add(ExamineRow.Blank);
        }

        return rows;
    }

    /// <summary>
    /// The armour rows of a character (11.6096 0x004B4EF0): when any of the nine levels is above 0, a spacer and three rows, "Head/Chest/Groin", "Bicep/Wrist/Hand" and "Thigh/Shin/Foot", each "AL: a/b/c"; a level of 9999 or more is
    /// unenchantable and reads "*" and the level less 9999. None without the armour levels (a failed roll, a creature).
    /// </summary>
    public static IReadOnlyList<ExamineRow> ArmorRows(Appraisal appraisal)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        if (appraisal.ArmorLevelValues() is not { } levels)
        {
            return [];
        }

        var v = Array.ConvertAll(levels, static x => unchecked((int)x));
        if (!v.Any(static x => x > 0))
        {
            return [];
        }

        static string One(int x) => x < UnenchantableOffset ? x.ToString(CultureInfo.InvariantCulture) : "*" + (x - UnenchantableOffset).ToString(CultureInfo.InvariantCulture);
        string Three(int a, int b, int c) => $"AL: {One(v[a])}/{One(v[b])}/{One(v[c])}";
        return
        [
            ExamineRow.Blank,
            new ExamineRow("Head/Chest/Groin", Three(0, 1, 2), ExamineColor.Normal),
            new ExamineRow("Bicep/Wrist/Hand", Three(3, 4, 5), ExamineColor.Normal),
            new ExamineRow("Thigh/Shin/Foot", Three(6, 7, 8), ExamineColor.Normal),
        ];
    }

    /// <summary>
    /// The society row of a character (PropertyInt 281): "Society:" and the society ("Celestial Hand", "Eldrytch Web" or "Radiant Blood", by the lowest set bit; "???" for none) with its rank's word from PropertyInt 287, 288 or 289
    /// (" ~ Initiate" 1 to 100, " ~ Adept" 101 to 300, " ~ Knight" 301 to 600, " ~ Lord" 601 to 1000, " ~ Master" 1001 to 1500, none otherwise), coloured by the viewer's society: <see cref="ExamineColor.Raised"/> for the same,
    /// <see cref="ExamineColor.Lowered"/> for another. Null without PropertyInt 281.
    /// </summary>
    public static ExamineRow? SocietyRow(Appraisal appraisal, int viewerFactionBits)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        if (appraisal.Int(FactionBitsId) is not { } bits)
        {
            return null;
        }

        var (name, rankId, same, rival) = (bits & 1) != 0 ? ("Celestial Hand", 287u, 1, 6)
            : (bits & 2) != 0 ? ("Eldrytch Web", 288u, 2, 5)
            : (bits & 4) != 0 ? ("Radiant Blood", 289u, 4, 3)
            : ("???", 0u, 0, 0);
        var rank = rankId == 0 ? 0 : appraisal.Int(rankId) ?? 0;
        var word = rank switch
        {
            >= 1 and <= 100 => " ~ Initiate",
            >= 101 and <= 300 => " ~ Adept",
            >= 301 and <= 600 => " ~ Knight",
            >= 601 and <= 1000 => " ~ Lord",
            >= 1001 and <= 1500 => " ~ Master",
            _ => string.Empty,
        };
        var color = (viewerFactionBits & same) != 0 ? ExamineColor.Raised : (viewerFactionBits & rival) != 0 ? ExamineColor.Lowered : ExamineColor.Normal;
        return new ExamineRow("Society:", name + word, color);
    }

    /// <summary>
    /// The allegiance rows of a character with an allegiance rank of 1 or more: a monarch (no PropertyString 21) shows "Alleg. Monarch:" and "N Follower(s)" from PropertyInt 35; else the monarch (PropertyString 21) and the patron
    /// (PropertyString 35) as "Monarch:" and "Patron:", one "Monarch/Patron:" row when they are the same, or "Monarch:" alone without a patron.
    /// </summary>
    public static IReadOnlyList<ExamineRow> AllegianceRows(Appraisal appraisal)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        if (appraisal.String(MonarchId) is not { } monarch)
        {
            var followers = appraisal.Int(AllegianceFollowersId) is { } f and >= 0 ? f : 0;
            var text = followers == 1 ? "1 Follower" : followers.ToString(CultureInfo.InvariantCulture) + " Followers";
            return [new ExamineRow("Alleg. Monarch:", text, ExamineColor.Normal)];
        }

        if (appraisal.String(PatronId) is not { } patron)
        {
            return [new ExamineRow("Monarch:", monarch, ExamineColor.Normal)];
        }

        return string.Equals(monarch, patron, StringComparison.Ordinal)
            ? [new ExamineRow("Monarch/Patron:", monarch, ExamineColor.Normal)]
            : [new ExamineRow("Monarch:", monarch, ExamineColor.Normal), new ExamineRow("Patron:", patron, ExamineColor.Normal)];
    }

    /// <summary>
    /// A character's remaining rows, each when its property is present: "Fellowship:" (PropertyString 10), "Arrived in Dereth:" (43), "Time in Dereth:" (PropertyInt 125, <see cref="DeltaTimeText"/>), "Chess Rank:" (181),
    /// "Fishing Skill:" (192), "Deaths:" (43; "Has never died" for 0 or less), "Titles Earned:" (262) and "Enlightenment:" (390).
    /// </summary>
    public static IReadOnlyList<ExamineRow> CharacterRows(Appraisal appraisal)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        var rows = new List<ExamineRow>(8);
        void Text(string label, string? value)
        {
            if (value is not null)
            {
                rows.Add(new ExamineRow(label, value, ExamineColor.Normal));
            }
        }

        static string? N(int? v) => v?.ToString(CultureInfo.InvariantCulture);
        Text("Fellowship:", appraisal.String(FellowshipNameId));
        Text("Arrived in Dereth:", appraisal.String(DateOfBirthId));
        Text("Time in Dereth:", appraisal.Int(AgeId) is { } age ? DeltaTimeText(age) : null);
        Text("Chess Rank:", N(appraisal.Int(ChessRankId)));
        Text("Fishing Skill:", N(appraisal.Int(FishingSkillId)));
        Text("Deaths:", appraisal.Int(DeathsId) is { } deaths ? deaths <= 0 ? "Has never died" : N(deaths) : null);
        Text("Titles Earned:", N(appraisal.Int(TitlesEarnedId)));
        Text("Enlightenment:", N(appraisal.Int(EnlightenmentId)));
        return rows;
    }

    /// <summary>
    /// <c>ClientUISystem::DeltaTimeToString</c> (11.6096 0x00566BB0): months of 30 days, days, hours and minutes when not 0, then the seconds always, as "1mo 2d 3h 4m 5s".
    /// </summary>
    public static string DeltaTimeText(int seconds)
    {
        var t = unchecked((uint)seconds);
        var months = t / 2_592_000;
        t %= 2_592_000;
        var days = t / 86_400;
        t %= 86_400;
        var hours = t / 3_600;
        t %= 3_600;
        var minutes = t / 60;
        var parts = new List<string>(5);
        foreach (var (count, unit) in (ReadOnlySpan<(uint, string)>)[(months, "mo"), (days, "d"), (hours, "h"), (minutes, "m")])
        {
            if (count != 0)
            {
                parts.Add(count.ToString(CultureInfo.InvariantCulture) + unit);
            }
        }

        parts.Add((t % 60).ToString(CultureInfo.InvariantCulture) + "s");
        return string.Join(' ', parts);
    }

    /// <summary>The description bit of a player killer (<c>ACCWeenieObject::IsPK</c>, 11.6096 0x0058D6E0: bit 5; ACE <c>ObjectDescriptionFlag.PlayerKiller</c> 0x20).</summary>
    public const uint PlayerKillerFlag = 0x20;

    /// <summary>The description bit of a player killer lite (<c>ACCWeenieObject::IsPKLite</c>, 11.6096 0x0058D6D0: bit 25; ACE <c>ObjectDescriptionFlag.PkLiteStatus</c> 0x02000000).</summary>
    public const uint PlayerKillerLiteFlag = 0x02000000;

    /// <summary>
    /// The player-killer text (11.6096 0x004B4EF0) from the object's description bits, which ACE sets for the CreateObject (<c>WorldObjects/WorldObject_Networking.cs:865, 876</c>), not in the appraisal: "Player Killer"
    /// when IsPK, else "Player Killer Lite" when IsPKLite, else "Non-Player Killer".
    /// </summary>
    public static string PlayerKillerText(uint descriptionFlags) =>
        (descriptionFlags & PlayerKillerFlag) != 0 ? "Player Killer" : (descriptionFlags & PlayerKillerLiteFlag) != 0 ? "Player Killer Lite" : "Non-Player Killer";

    /// <summary>The viewer's society bits as retail reads them for the society row (<c>CBaseQualities::InqInt(281)</c> on its own player description): the value, or 0 without one.</summary>
    public static int ViewerFactionBitsOf(PlayerQualities? viewer) => viewer is not null && viewer.TryGetInt(FactionBitsId, out var bits) ? bits : 0;

    private static ExamineRow VitalRow(Appraisal appraisal, string name, uint? current, uint? maximum, bool showPercent, uint bit)
    {
        string text;
        if (current is not { } cur || cur == 0 || maximum is not { } max || max == 0)
        {
            text = "???";
        }
        else
        {
            var percent = MulDivPercent(cur, max);
            text = appraisal.Success
                ? showPercent ? string.Create(CultureInfo.InvariantCulture, $"{cur}/{max} ({percent} %)") : string.Create(CultureInfo.InvariantCulture, $"{cur}/{max}")
                : showPercent ? string.Create(CultureInfo.InvariantCulture, $"{percent} %") : "???";
        }

        return new ExamineRow(name, text, StatColor(appraisal, bit));
    }

    /// <summary>Windows' <c>MulDiv(100, current, maximum)</c>: the percentage rounded to the nearest, a half away from 0.</summary>
    private static long MulDivPercent(uint current, uint maximum) => ((200L * current) + maximum) / (2L * maximum);

    /// <summary><c>CreatureAppraisalProfile::InqAttributeEnchantmentMod</c> (11.6096 0x005B6F70) and <c>InqAttribute2ndEnchantmentMod</c> (0x005B7060): the highlight word's bit, then the colour word's bit (set is raised).</summary>
    private static int StatColor(Appraisal appraisal, uint bit)
    {
        if (!appraisal.Success)
        {
            return ExamineColor.Unassessed;
        }

        if (appraisal.CreatureEnchantmentMasks() is not { } masks || (masks.Highlights & bit) == 0)
        {
            return ExamineColor.Normal;
        }

        return (masks.Colors & bit) != 0 ? ExamineColor.Raised : ExamineColor.Lowered;
    }

    private static string Pair(string word, int a, int b) => string.Create(CultureInfo.InvariantCulture, $"{word}{a}/{b}");

    /// <summary>The creature panel's type text (11.6096 0x004B4690): PropertyInt 2's display name, or null when it is absent, 0 or not in the table (retail leaves the text).</summary>
    private static string? CreatureTypeName(Appraisal appraisal, IExamineNames? names, ILogSink log)
    {
        if (appraisal.Int(CreatureTypeId) is not { } type || type == 0)
        {
            return null;
        }

        return names is null ? NoTables("Creature type", log) : CreatureDisplayName(names, type);
    }

    /// <summary><c>AppraisalSystem::InqCreatureDisplayName</c> (11.6096 0x005B6A90): the EnumMapper 0x10000005 name with each '_' made a space.</summary>
    private static string? CreatureDisplayName(IExamineNames names, int type) => names.EnumName(0x10000005, type)?.Replace('_', ' ');

    /// <summary>
    /// <c>AppraisalSystem::InqHeritageGroupDisplayName</c> (11.6096 0x005B57C0): the client's own spellings for heritages 2 "Gharu'ndim", 5 "Umbraen" and 13 "Olthoi" (literals 0x007E785C, 0x007E7854, 0x007E784C), else the EnumMapper
    /// 0x10000002 name.
    /// </summary>
    private static string? HeritageDisplayName(IExamineNames names, int heritage) => heritage switch
    {
        2 => "Gharu'ndim",
        5 => "Umbraen",
        13 => "Olthoi",
        _ => names.EnumName(0x10000002, heritage),
    };

    /// <summary><c>AppraisalSystem::InqGenderHeritageDisplay</c> (11.6096 0x005B6B90): the gender's name, a space, and the heritage's name (or, without a heritage, the creature type's).</summary>
    private static string HeritageText(Appraisal appraisal, IExamineNames? names, ILogSink log)
    {
        var gender = appraisal.Int(GenderId) ?? 0;
        var heritage = appraisal.Int(HeritageId) ?? 0;
        var creature = heritage == 0 ? appraisal.Int(CreatureTypeId) ?? 0 : 0;
        if (gender == 0 && heritage == 0 && creature == 0)
        {
            return string.Empty;
        }

        if (names is null)
        {
            return NoTables("Gender and heritage", log);
        }

        // A name the table lacks is left out, as retail's string simply is not appended.
        var parts = new List<string>(2);
        if (gender != 0 && names.EnumName(0x10000001, gender) is { } g)
        {
            parts.Add(g);
        }

        if ((heritage != 0 ? HeritageDisplayName(names, heritage) : creature != 0 ? CreatureDisplayName(names, creature) : null) is { } second)
        {
            parts.Add(second);
        }

        return string.Join(' ', parts);
    }

    /// <summary>The STRINGTABLE enum of the character titles' strings (11.6096 0x005C7EB0 passes 0x10000007 to <c>StringInfo::SetStringIDandTableEnum</c>).</summary>
    public const uint CharacterTitleStringTable = 0x10000007;

    /// <summary>
    /// The character title (11.6096 0x004B4EF0): <c>CharacterTitleTable::GetCharacterTitleFromID</c> (0x005C7EB0) turns PropertyInt 261 into its token through the EnumMapper 0x10000006 ("ID_CharacterTitle_...") and renders that
    /// token from the string table 0x10000007; without an id, or an id the map lacks, PropertyString 5; null when neither (retail leaves the text).
    /// </summary>
    private static string? ProfessionText(Appraisal appraisal, IExamineNames? names, ILogSink log)
    {
        if (appraisal.Int(CharacterTitleId) is { } id and not 0)
        {
            if (names is null)
            {
                return NoTables("Character title", log);
            }

            if (names.EnumName(0x10000006, id) is { } token)
            {
                return names.StringText(CharacterTitleStringTable, token)
                    ?? Placeholder("Character title", "m5-examine-title-strings", $"title {id} ({token}): the string table 0x{CharacterTitleStringTable:X8} is not in the language dat", log);
            }
        }

        return appraisal.String(TemplateId);
    }

    /// <summary><c>AllegianceData::GetFullName</c> (11.6096 0x005B7A40): the allegiance title (<see cref="AllegianceTitle"/>), a space and the name; the name alone without a title.</summary>
    private static string FullName(Appraisal appraisal, ExamineCreatureContext context, int rank) =>
        AllegianceTitle(rank, appraisal.Int(HeritageId) ?? 0, appraisal.Int(GenderId) ?? 0) is { } title ? title + " " + context.ObjectName : context.ObjectName;

    // AllegianceSystem's title tables (11.6096 0x005B8D00 to 0x005B9E00, one per heritage and gender; ranks 1 to 10). The client's literals.
    private static readonly string[] AluvianMale = ["Yeoman", "Baronet", "Baron", "Reeve", "Thane", "Ealdor", "Duke", "Aetheling", "King", "High King"];
    private static readonly string[] AluvianFemale = ["Yeoman", "Baronet", "Baroness", "Reeve", "Thane", "Ealdor", "Duchess", "Aetheling", "Queen", "High Queen"];
    private static readonly string[] GharundimMale = ["Sayyid", "Shayk", "Maulan", "Mu'allim", "Naquib", "Qadi", "Mushir", "Amir", "Malik", "Sultan"];
    private static readonly string[] GharundimFemale = ["Sayyida", "Shayka", "Maulana", "Mu'allima", "Naquiba", "Qadiya", "Mushira", "Amira", "Malika", "Sultana"];
    private static readonly string[] ShoMale = ["Jinin", "Jo-chueh", "Nan-chueh", "Shi-chueh", "Ta-chueh", "Kun-chueh", "Kou", "Taikou", "Ou", "Koutei"];
    private static readonly string[] ShoFemale = ["Jinin", "Jo-chueh", "Nan-chueh", "Shi-chueh", "Ta-chueh", "Kun-chueh", "Kou", "Taikou", "Jo-ou", "Koutei"];
    private static readonly string[] ViamontianMale = ["Squire", "Banner", "Baron", "Viscount", "Count", "Marquis", "Duke", "Grand Duke", "King", "High King"];
    private static readonly string[] ViamontianFemale = ["Dame", "Banner", "Baroness", "Viscountess", "Countess", "Marquise", "Duchess", "Grand Duchess", "Queen", "High Queen"];
    private static readonly string[] ShadowboundMale = ["Tenebrous", "Shade", "Squire", "Knight", "Void Knight", "Void Lord", "Duke", "Archduke", "Highborn", "King"];
    private static readonly string[] ShadowboundFemale = ["Tenebrous", "Shade", "Squire", "Knight", "Void Knight", "Void Lady", "Duchess", "Archduchess", "Highborn", "Queen"];
    private static readonly string[] Gearknight = ["Tribunus", "Praefectus", "Optio", "Centurion", "Principes", "Legatus", "Consul", "Dux", "Secondus", "Primus"];
    private static readonly string[] Tumerok = ["Xutua", "Tuona", "Ona", "Nuona", "Turea", "Rea", "Nurea", "Kauh", "Sutah", "Tah"];
    private static readonly string[] Lugian = ["Laigus", "Raigus", "Amploth", "Arintoth", "Obeloth", "Lithos", "Kantos", "Gigas", "Extas", "Tiatus"];
    private static readonly string[] EmpyreanMale = ["Ensign", "Corporal", "Lieutenant", "Commander", "Captain", "Commodore", "Admiral", "Warlord", "Ipharsin", "Aulin"];
    private static readonly string[] EmpyreanFemale = ["Ensign", "Corporal", "Lieutenant", "Commander", "Captain", "Commodore", "Admiral", "Warlord", "Ipharsia", "Aulia"];
    private static readonly string[] UndeadMale = ["Neophyte", "Acolyte", "Adept", "Esquire", "Squire", "Knight", "Count", "Viscount", "Highness", "Annointed"];
    private static readonly string[] UndeadFemale = ["Neophyte", "Acolyte", "Adept", "Esquire", "Squire", "Knight", "Countess", "Viscountess", "Highness", "Annointed"];

    /// <summary>
    /// <c>AllegianceSystem::GetTitle</c> (11.6096 0x005B9F10): by gender (1 male, 2 female) and heritage (1 Aluvian, 2 Gharu'ndim, 3 Sho, 4 Viamontian, 5 and 10 Shadowbound/Umbraen, 6 Gearknight, 7 Tumerok, 8 Lugian, 9 Empyrean,
    /// 11 Undead; Gearknight, Tumerok and Lugian share one table for both genders) the title of rank 1 to 10; null for another rank, heritage or gender.
    /// </summary>
    public static string? AllegianceTitle(int rank, int heritage, int gender)
    {
        if (rank is < 1 or > 10 || gender is not (1 or 2))
        {
            return null;
        }

        var female = gender == 2;
        var table = heritage switch
        {
            1 => female ? AluvianFemale : AluvianMale,
            2 => female ? GharundimFemale : GharundimMale,
            3 => female ? ShoFemale : ShoMale,
            4 => female ? ViamontianFemale : ViamontianMale,
            5 or 10 => female ? ShadowboundFemale : ShadowboundMale,
            6 => Gearknight,
            7 => Tumerok,
            8 => Lugian,
            9 => female ? EmpyreanFemale : EmpyreanMale,
            11 => female ? UndeadFemale : UndeadMale,
            _ => null,
        };
        return table?[rank - 1];
    }

    private static string NoTables(string feature, ILogSink log) =>
        Placeholder(feature, "m5-examine-name-tables", "the creature window was given no dat name tables (ExamineCreatureContext.Names); wire ExamineNameTables", log);

    private static string Placeholder(string feature, string todo, string text, ILogSink log)
    {
        Todo(log, todo, text);
        return feature + ": not in Lifestone M5";
    }

    private static void Todo(ILogSink log, string name, string text) => log.Warn("retailui", $"placeholder: TODO({name}): {text}");
}
