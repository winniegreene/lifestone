using System.Globalization;
using System.Text;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>
/// The pages of character creation, in the order <c>gmCharGenMainUI::ECGProgress</c> numbers them (1 to 6). Retail names them
/// <c>ECG_HERTAGE</c> (sic), <c>ECG_PROFESSION</c>, <c>ECG_SKILLS</c>, <c>ECG_APPEARANCE</c>, <c>ECG_TOWN</c> and <c>ECG_SUMMARY</c>.
/// </summary>
public enum CharGenPageId
{
    /// <summary>1: the heritage page (<c>gmCGHeritagePage</c>).</summary>
    Heritage = 1,

    /// <summary>2: the profession and attributes page (<c>gmCGProfessionPage</c>).</summary>
    Profession = 2,

    /// <summary>3: the skills page (<c>gmCGSkillsPage</c>).</summary>
    Skills = 3,

    /// <summary>4: the appearance page (<c>gmCGAppearancePage</c>).</summary>
    Appearance = 4,

    /// <summary>5: the starting-town page (<c>gmCGTownPage</c>).</summary>
    Town = 5,

    /// <summary>6: the summary page (<c>gmCGSummaryPage</c>), the last: it has Finish instead of Next.</summary>
    Summary = 6,
}

/// <summary>A heritage's button on the heritage page and what the page shows for it.</summary>
/// <param name="Heritage">The heritage group (1 Aluvian ... 13 acid Olthoi).</param>
/// <param name="ButtonId">The heritage button's element id.</param>
/// <param name="BackgroundState">The state of the page's picture field (0x100003BE) that shows this heritage.</param>
/// <param name="DescriptionToken">The pregame string of the heritage's description.</param>
/// <param name="BonusSkillsToken">The pregame string of the skills the heritage trains.</param>
public sealed record CharGenHeritageEntry(uint Heritage, uint ButtonId, uint BackgroundState, string DescriptionToken, string BonusSkillsToken);

/// <summary>A profession (starting template) button on the profession page and what the page shows for it.</summary>
/// <param name="Template">The template index (0 custom, 1 bow hunter, 2 swashbuckler, 3 life caster, 4 war mage, 5 wayfarer, 6 soldier).</param>
/// <param name="ButtonId">The template button's element id.</param>
/// <param name="BackgroundState">The state of the page's picture field (0x100003D8) that shows this profession.</param>
/// <param name="DescriptionToken">The pregame string of the profession's description.</param>
public sealed record CharGenTemplateEntry(int Template, uint ButtonId, uint BackgroundState, string DescriptionToken);

/// <summary>One start town of the town page (<c>gmCGTownPage::ETown</c>: 1 Holtburg, 2 Shoushi, 3 Yaraq, 4 Sanamar).</summary>
/// <param name="Town">Retail's town number; the start area is one less.</param>
/// <param name="StartArea">The CharGen start area it picks (<c>CharGenState::SetStartArea</c>): its index in the CharGen data's start areas.</param>
/// <param name="ButtonId">The town's button on the page.</param>
/// <param name="PageState">The state the page takes (0x10000034 to 0x10000037), which shows the town's name and map.</param>
/// <param name="TextToken">The pregame string of the town's description.</param>
public sealed record CharGenTownEntry(int Town, int StartArea, uint ButtonId, uint PageState, string TextToken);

/// <summary>What a skill row of the skills page shows and allows (<c>gmCGSkillsPage::SetSkillText</c>).</summary>
/// <param name="UpCost">The up arrow's cost text: empty when the step costs 999 or more (the skill cannot be bought).</param>
/// <param name="DownCost">The down arrow's cost text (what the step back refunds).</param>
/// <param name="UpEnabled">Whether the up arrow is usable (the credits left pay for the step).</param>
/// <param name="DownEnabled">Whether the down arrow is usable (the skill may go back a step).</param>
public readonly record struct CharGenSkillRow(string UpCost, string DownCost, bool UpEnabled, bool DownEnabled);

/// <summary>
/// The rules of character creation's main screen and its first three pages, apart from any element: the page order (with the Olthoi skipping
/// the pages they do not have), the tables that tie the heritage and profession buttons to the state and its texts, the attribute readouts,
/// the typed-number rules of the attribute fields, and the skills page's rows and formula text. Re-expressed from
/// <c>gmCharGenMainUI::SetProgressState</c> (11.4186 0x004E7A10, 11.6096 0x004E86A0), <c>gmCGHeritagePage::Update</c> (0x00483210, 0x004835D0),
/// <c>gmCGProfessionPage::UpdateProfession</c> (0x004821B0, 0x00482570), <c>UpdateAttributeValues</c> (0x00482450, 0x00482810),
/// <c>gmCGSkillsPage::SetSkillText</c> (0x00480600, 0x004809C0) and <c>MakeSkillFormula</c> (0x00480E10, 0x004811D0).
/// </summary>
public static class CharGenRules
{
    /// <summary>The state of a heritage or profession button that is not chosen (UISTATE 0x10000016).</summary>
    public const uint UnselectedState = 0x10000016;

    /// <summary>The state of the button that is chosen (UISTATE 0x10000017).</summary>
    public const uint SelectedState = 0x10000017;

    /// <summary>The state of an attribute's slider and lock button while the attribute is free (UISTATE 0x10000018).</summary>
    public const uint UnlockedState = 0x10000018;

    /// <summary>The state of an attribute's slider and lock button while the attribute is locked (UISTATE 0x10000019).</summary>
    public const uint LockedState = 0x10000019;

    /// <summary>The state of a skill's arrow that cannot be used (UISTATE 0x1000001A).</summary>
    public const uint ArrowDisabledState = 0x1000001A;

    /// <summary>The state of a skill's arrow that can be used (UISTATE 0x1000001B).</summary>
    public const uint ArrowEnabledState = 0x1000001B;

    /// <summary>The step cost from which a skill cannot be bought (retail prints no cost for 999 or more).</summary>
    public const int UnbuyableCost = 999;

    /// <summary>The heritage buttons (<c>gmCGHeritagePage::InitializePage</c> finds them, <c>Update</c> shows them), in heritage order.</summary>
    public static IReadOnlyList<CharGenHeritageEntry> Heritages { get; } =
    [
        new(1, 0x100003BF, 0x10000021, "ID_CharGen_AluvianText", "ID_CharGen_AluvianText_BonusSkills_Trained"),
        new(2, 0x100003C1, 0x10000022, "ID_CharGen_GaruText", "ID_CharGen_GaruText_BonusSkills_Trained"),
        new(3, 0x100003C2, 0x10000023, "ID_CharGen_ShoText", "ID_CharGen_ShoText_BonusSkills_Trained"),
        new(4, 0x100003C3, 0x10000024, "ID_CharGen_ViaText", "ID_CharGen_ViaText_BonusSkills_Trained"),
        new(5, 0x10000590, 0x10000058, "ID_CharGen_ShadText", "ID_CharGen_ShadText_BonusSkills_Trained"),
        new(6, 0x100005A9, 0x1000005A, "ID_CharGen_GearText", "ID_CharGen_GearText_BonusSkills_Trained"),
        new(7, 0x100005E8, 0x1000005F, "ID_CharGen_AunTText", "ID_CharGen_AunTText_BonusSkills_Trained"),
        new(8, 0x100005F1, 0x10000060, "ID_CharGen_LugText", "ID_CharGen_LugText_BonusSkills_Trained"),
        new(9, 0x100005C4, 0x1000005C, "ID_CharGen_EmpText", "ID_CharGen_EmpText_BonusSkills_Trained"),

        // Retail shows the Shadowbound texts for the Penumbraen too (Update's case for heritage 10 jumps to the Shadowbound strings).
        new(10, 0x10000591, 0x10000059, "ID_CharGen_ShadText", "ID_CharGen_ShadText_BonusSkills_Trained"),
        new(11, 0x100005BF, 0x1000005B, "ID_CharGen_UndText", "ID_CharGen_UndText_BonusSkills_Trained"),
        new(12, 0x100005C7, 0x1000005D, "ID_CharGen_OlthoiText", "ID_CharGen_OlthoiText_BonusSkills_Trained"),
        new(13, 0x100005C8, 0x1000005E, "ID_CharGen_OlthoiAcidText", "ID_CharGen_OlthoiAcidText_BonusSkills_Trained"),
    ];

    /// <summary>The profession buttons (<c>gmCGProfessionPage::UpdateProfession</c> shows them, <c>ListenToElementMessage</c> reads them), in template order.</summary>
    public static IReadOnlyList<CharGenTemplateEntry> Templates { get; } =
    [
        new(0, 0x100003D9, 0x1000002B, "ID_CharGen_CustomText"),
        new(1, 0x100003DA, 0x1000002C, "ID_CharGen_BowText"),
        new(2, 0x100003DF, 0x10000031, "ID_CharGen_SwashText"),
        new(3, 0x100003DB, 0x1000002D, "ID_CharGen_LifeText"),
        new(4, 0x100003DC, 0x1000002E, "ID_CharGen_WarText"),
        new(5, 0x100003DD, 0x1000002F, "ID_CharGen_WayText"),
        new(6, 0x100003DE, 0x10000030, "ID_CharGen_SoldierText"),
    ];

    /// <summary>The heritage button <paramref name="buttonId"/> belongs to, or null.</summary>
    public static CharGenHeritageEntry? HeritageOfButton(uint buttonId) => Heritages.FirstOrDefault(h => h.ButtonId == buttonId);

    /// <summary>The heritage's entry, or null for a heritage outside 1-13.</summary>
    public static CharGenHeritageEntry? HeritageEntry(uint heritage) => Heritages.FirstOrDefault(h => h.Heritage == heritage);

    /// <summary>The template's entry, or null for a template outside 0-6 (retail's <c>UpdateProfession</c> has no case for it).</summary>
    public static CharGenTemplateEntry? TemplateEntry(int template) => Templates.FirstOrDefault(t => t.Template == template);

    /// <summary>The template button <paramref name="buttonId"/> belongs to, or null.</summary>
    public static CharGenTemplateEntry? TemplateOfButton(uint buttonId) => Templates.FirstOrDefault(t => t.ButtonId == buttonId);

    /// <summary>
    /// The four start towns (<c>gmCGTownPage::SetTown</c>, 11.4186 0x0047C360, 11.6096 0x0047C710, and <c>ListenToElementMessage</c>, 0x0047C480,
    /// 0x0047C830): the start area is the town number less one (Holtburg 0, Shoushi 1, Yaraq 2, Sanamar 3, which is how the CharGen
    /// data lists them). The page states of Sanamar (0x10000035) and Shoushi (0x10000037) are retail's constants; Holtburg's and Yaraq's are read from
    /// the decompile as the two that remain.
    /// </summary>
    public static IReadOnlyList<CharGenTownEntry> Towns { get; } =
    [
        new(1, 0, 0x1000040D, 0x10000034, "ID_CharGen_HoltText"),
        new(2, 1, 0x1000040F, 0x10000037, "ID_CharGen_ShoushiText"),
        new(3, 2, 0x1000040E, 0x10000036, "ID_CharGen_YaraqText"),
        new(4, 3, 0x1000040B, 0x10000035, "ID_CharGen_SanamarText"),
    ];

    /// <summary>
    /// The camera of the whole-figure views (<c>gmCGSummaryPage::InitializePage</c> and <c>Update</c>, 11.4186 0x0047BBF0 and 0x0047BAA0, 11.6096 0x0047BFB0 and
    /// 0x0047BE60; the appearance page's <c>ZoomOut</c>, 11.4186 0x0047D050, 11.6096 0x0047D400): a point in front of the
    /// character, in AC space, looking along +Y: (0, -2.5, 0.95); the Olthoi's (0, -3.8, 1.15) and the acid Olthoi's (0, -5.7, 1.65), which are bigger.
    /// </summary>
    public static System.Numerics.Vector3 BodyCamera(uint heritage) => heritage switch
    {
        CharGenCatalog.OlthoiHeritage => new System.Numerics.Vector3(0f, -3.8f, 1.15f),
        CharGenCatalog.OlthoiAcidHeritage => new System.Numerics.Vector3(0f, -5.7f, 1.65f),
        _ => new System.Numerics.Vector3(0f, -2.5f, 0.95f),
    };

    /// <summary>
    /// The camera of the appearance page's face view (<c>gmCGAppearancePage::ZoomIn</c>, 11.4186 0x0047CF00, 11.6096 0x0047D2B0, and <c>InitializePage</c>): close to the head, (0, -0.55, 1.65);
    /// the Tumerok's (0, -0.85, 1.65), the Olthoi's (0, -1.85, 1.85) and the acid Olthoi's (0, -3.05, 2.75).
    /// </summary>
    public static System.Numerics.Vector3 FaceCamera(uint heritage) => heritage switch
    {
        CharGenCatalog.OlthoiHeritage => new System.Numerics.Vector3(0f, -1.85f, 1.85f),
        CharGenCatalog.OlthoiAcidHeritage => new System.Numerics.Vector3(0f, -3.05f, 2.75f),
        7 => new System.Numerics.Vector3(0f, -0.85f, 1.65f),
        _ => new System.Numerics.Vector3(0f, -0.55f, 1.65f),
    };

    /// <summary>The start town whose button is <paramref name="buttonId"/>, or null.</summary>
    public static CharGenTownEntry? TownOfButton(uint buttonId) => Towns.FirstOrDefault(t => t.ButtonId == buttonId);

    /// <summary>The start town of CharGen start area <paramref name="startArea"/> (<c>gmCGTownPage::Update</c> adds one to it), or null for an area the page has no town for.</summary>
    public static CharGenTownEntry? TownOfStartArea(int startArea) => Towns.FirstOrDefault(t => t.StartArea == startArea);

    /// <summary>
    /// The pregame string of the sample names the summary page lists for a character (<c>gmCGSummaryPage::SetHowToText</c>): by heritage and gender, the
    /// Shadowbound and Penumbraen sharing one list, the two Olthoi another; null for a heritage the page has none for.
    /// </summary>
    public static string? NamesToken(uint heritage, uint gender)
    {
        var female = gender == 2;
        var stem = heritage switch
        {
            1 => "Alu",
            2 => "Gharu",
            3 => "Sho",
            4 => "Via",
            5 or 10 => "Shad",
            6 => "Gear",
            7 => "AunT",
            8 => "Lug",
            9 => "Emp",
            11 => "Und",
            12 or 13 => "Olthoi",
            _ => null,
        };
        return stem is null ? null : $"ID_CharGen_{stem}{(female ? "Female" : "Male")}Names";
    }

    // ---------------------------------------------------------------- Page order

    /// <summary>True for the Olthoi heritages (12 and 13), which have no profession, skills or town page.</summary>
    public static bool IsOlthoi(uint heritage) => heritage is CharGenCatalog.OlthoiHeritage or CharGenCatalog.OlthoiAcidHeritage;

    /// <summary>
    /// The page <c>SetProgressState</c> ends up showing when <paramref name="requested"/> is asked for while <paramref name="previous"/> shows. For the
    /// Olthoi the profession, skills and town pages do not exist: going forward they land on the next page that does (profession and skills on
    /// appearance, town on summary), going back on the one before (profession and skills on heritage, town on appearance).
    /// </summary>
    public static CharGenPageId Resolve(CharGenPageId requested, CharGenPageId previous, uint heritage)
    {
        if (!IsOlthoi(heritage))
        {
            return requested;
        }

        if (requested < previous)
        {
            return requested switch
            {
                CharGenPageId.Profession or CharGenPageId.Skills => CharGenPageId.Heritage,
                CharGenPageId.Town => CharGenPageId.Appearance,
                _ => requested,
            };
        }

        return requested switch
        {
            CharGenPageId.Profession or CharGenPageId.Skills => CharGenPageId.Appearance,
            CharGenPageId.Town => CharGenPageId.Summary,
            _ => requested,
        };
    }

    /// <summary>The page element id of <paramref name="page"/> (0x100003D1 to 0x100003D6).</summary>
    public static uint PageElementId(CharGenPageId page) => 0x100003D0u + (uint)page;

    /// <summary>The tab (progress bar button) of <paramref name="page"/> (0x100003EF to 0x100003F4).</summary>
    public static uint TabElementId(CharGenPageId page) => 0x100003EEu + (uint)page;

    /// <summary>The master page's state that goes with <paramref name="page"/> (0x10000025 to 0x1000002A).</summary>
    public static uint MasterState(CharGenPageId page) => 0x10000024u + (uint)page;

    // ---------------------------------------------------------------- Profession page

    /// <summary>Hit points as the profession page shows them: endurance halved and rounded half up (<c>UpdateAttributeValues</c>).</summary>
    public static int HealthOf(int endurance) => (int)((endurance * 0.5) + 0.5);

    /// <summary>
    /// What an attribute field's text means when the field loses the focus (<c>gmCGProfessionPage::ListenToElementMessage</c>, element 0x100002EF): the
    /// leading digits as a number (0 when there are none), 100 for more than 100 and 10 for less than 10.
    /// </summary>
    public static int AttributeFromTypedText(string text)
    {
        var value = ParseLeadingNumber(text);
        return value > CharGenState.AttributeMax ? CharGenState.AttributeMax : Math.Max((int)value, CharGenState.AttributeMin);
    }

    /// <summary>
    /// Where the slider goes while an attribute's text is being typed (the same handler, messages 0x12 and 0x44): the number as a fraction of 100,
    /// kept between 0.1 and 1.
    /// </summary>
    public static float SliderGoalFromTypedText(string text) => (float)Math.Clamp(ParseLeadingNumber(text) * 0.0099999998, 0.1, 1.0);

    /// <summary>What a slider's position (the message's value, thousandths) means as an attribute: a tenth of it, at least 10 (<c>ListenToElementMessage</c> case 0x100002EE).</summary>
    public static int AttributeFromSlider(uint thousandths) => Math.Max((int)(thousandths * 0.1), CharGenState.AttributeMin);

    private static double ParseLeadingNumber(string text)
    {
        var end = 0;
        while (end < text.Length && text[end] is >= '0' and <= '9')
        {
            end++;
        }

        return end == 0 ? 0 : double.Parse(text.AsSpan(0, end), NumberStyles.None, CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------- Skills page

    /// <summary>
    /// A skill row's costs and arrows for a skill of class <paramref name="level"/> (<c>SetSkillText</c>): <paramref name="trainCost"/> and
    /// <paramref name="specCost"/> are the page's own figures (the SkillTable's, with a free skill's costs shifted, see <see cref="SkillCosts"/>), and
    /// <paramref name="credits"/> the skill credits left. Null for a class the page has no rows for.
    /// </summary>
    public static CharGenSkillRow? SkillRow(SkillAdvancementClass level, int trainCost, int specCost, bool canUntrain, bool canUnspecialize, int credits)
    {
        string Cost(int cost) => cost < UnbuyableCost ? cost.ToString(CultureInfo.InvariantCulture) : string.Empty;
        return level switch
        {
            SkillAdvancementClass.Untrained => new CharGenSkillRow(Cost(trainCost), "0", credits >= trainCost, false),
            SkillAdvancementClass.Trained => new CharGenSkillRow(Cost(specCost - trainCost), trainCost.ToString(CultureInfo.InvariantCulture), credits >= specCost - trainCost, canUntrain),
            SkillAdvancementClass.Specialized => new CharGenSkillRow("0", (specCost - trainCost).ToString(CultureInfo.InvariantCulture), false, canUnspecialize),
            _ => null,
        };
    }

    /// <summary>
    /// The costs <c>DoSkillRecords</c> gives a skill's record: the SkillTable's trained and specialised costs, then the heritage's say-so. A skill the
    /// heritage trains for nothing (a normal cost of 0) costs 0 to train, its specialised cost drops by the trained one, and it cannot be untrained;
    /// one the heritage specialises for nothing (a specialised cost of 0) cannot be specialised. A cost of 0 in the table also forbids the step.
    /// </summary>
    public static (int TrainCost, int SpecCost, bool CanUntrain, bool CanUnspecialize) SkillCosts(SkillTableEntry skill, CharGenSkill? heritageSkill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        var train = skill.TrainedCost;
        var spec = skill.SpecializedCost;
        var canUntrain = train != 0;
        var canUnspecialize = spec != 0;
        if (heritageSkill is { } own)
        {
            if (own.NormalCost == 0)
            {
                spec -= train;
                train = 0;
                canUntrain = false;
            }

            if (own.PrimaryCost == 0)
            {
                spec = 0;
                canUnspecialize = false;
            }
        }

        return (train, spec, canUntrain, canUnspecialize);
    }

    /// <summary>The skills page's list group a skill of class <paramref name="level"/> belongs to (<c>UpdateSkillEntry</c>): its minimum level separates the untrained skills that cannot be used from those that can.</summary>
    public static CharGenSkillGroup GroupOf(SkillAdvancementClass level, uint minimumLevel) => level switch
    {
        SkillAdvancementClass.Specialized => CharGenSkillGroup.Specialized,
        SkillAdvancementClass.Trained => CharGenSkillGroup.Trained,
        _ => minimumLevel > 1 ? CharGenSkillGroup.UnusableUntrained : CharGenSkillGroup.UsableUntrained,
    };

    /// <summary>
    /// A skill's formula as the skills page words it (<c>MakeSkillFormula</c>): <c>Formula : (Coordination + (2 x Focus)) / 3 +5</c>. An attribute is
    /// named when its multiplier is 1 or more and its id is not 0; a multiplier above 1 is written <c>(2 x Name)</c>; a divisor other than 1 and an
    /// added bonus other than 0 follow. The text is retail's literal ("Formula : ", " + ", " / ", " +").
    /// </summary>
    public static string FormulaText(SkillTableFormula formula)
    {
        var text = new StringBuilder("Formula : ");
        var first = formula.Attribute1Multiplier >= 1 && formula.Attribute1 != 0;
        var second = formula.Attribute2Multiplier >= 1 && formula.Attribute2 != 0;
        if (first)
        {
            if (second)
            {
                text.Append('(');
            }

            text.Append(Term(formula.Attribute1Multiplier, formula.Attribute1));
            if (second)
            {
                text.Append(" + ");
            }
        }

        if (second)
        {
            text.Append(Term(formula.Attribute2Multiplier, formula.Attribute2));
        }

        if (first && second)
        {
            text.Append(')');
        }

        if (formula.Divisor != 1)
        {
            text.Append(CultureInfo.InvariantCulture, $" / {unchecked((uint)formula.Divisor)}");
        }

        if (formula.AdditiveBonus != 0)
        {
            text.Append(CultureInfo.InvariantCulture, $" +{unchecked((uint)formula.AdditiveBonus)}");
        }

        return text.ToString();

        static string Term(int multiplier, uint attribute) =>
            multiplier <= 1 ? CharGenState.GetAttributeName(attribute) : string.Create(CultureInfo.InvariantCulture, $"({multiplier} x {CharGenState.GetAttributeName(attribute)})");
    }

    /// <summary>The line the skills page adds to a skill's description for its class (<c>ShowSkillsText</c>), or the empty string for an untrained skill.</summary>
    public static string BonusLine(SkillAdvancementClass level) => level switch
    {
        SkillAdvancementClass.Trained => "Training Bonus  +5\n",
        SkillAdvancementClass.Specialized => "Specialization Bonus  +10\n",
        _ => string.Empty,
    };
}

/// <summary>The four groups of the skills page's list, in the order they are listed (<c>gmCGSkillsPage::DoSkillRecords</c>).</summary>
public enum CharGenSkillGroup
{
    /// <summary>Specialised skills.</summary>
    Specialized = 0,

    /// <summary>Trained skills.</summary>
    Trained = 1,

    /// <summary>Untrained skills that can still be used.</summary>
    UsableUntrained = 2,

    /// <summary>Untrained skills that cannot be used until trained.</summary>
    UnusableUntrained = 3,
}
