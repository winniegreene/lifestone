using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Tests.RetailUi.Screens;

/// <summary>
/// The rules of character creation's main screen and its first three pages with no element in the way (m2-plan G3 U5): the page order and the
/// Olthoi skipping, the button tables, the attribute readouts and typed numbers, and the skills page's rows, costs, groups and formula text.
/// </summary>
public sealed class CharGenRulesTests
{
    private const CharGenPageId H = CharGenPageId.Heritage;
    private const CharGenPageId P = CharGenPageId.Profession;
    private const CharGenPageId S = CharGenPageId.Skills;
    private const CharGenPageId A = CharGenPageId.Appearance;
    private const CharGenPageId T = CharGenPageId.Town;
    private const CharGenPageId M = CharGenPageId.Summary;

    [Theory]
    [InlineData(1u, P, H)]
    [InlineData(6u, T, A)]
    [InlineData(11u, M, T)]
    public void Every_page_other_than_the_olthoi_lands_where_it_is_asked_to(uint heritage, CharGenPageId requested, CharGenPageId previous) =>
        Assert.Equal(requested, CharGenRules.Resolve(requested, previous, heritage));

    [Theory]
    [InlineData(P, H, A)] // forward: the profession and skills pages do not exist, appearance does
    [InlineData(S, H, A)]
    [InlineData(T, A, M)] // forward: no town page, the summary does
    [InlineData(A, H, A)]
    [InlineData(M, A, M)]
    [InlineData(P, S, H)] // back: the profession and skills requests land on heritage
    [InlineData(S, A, H)]
    [InlineData(T, M, A)] // back from the summary asks for town: lands on appearance
    [InlineData(A, T, A)]
    [InlineData(H, A, H)]
    public void The_olthoi_skip_the_profession_skills_and_town_pages_in_both_directions(CharGenPageId requested, CharGenPageId previous, CharGenPageId expected)
    {
        Assert.Equal(expected, CharGenRules.Resolve(requested, previous, CharGenCatalog.OlthoiHeritage));
        Assert.Equal(expected, CharGenRules.Resolve(requested, previous, CharGenCatalog.OlthoiAcidHeritage));
    }

    [Fact]
    public void The_page_tab_and_master_state_ids_are_retails()
    {
        Assert.Equal([0x100003D1u, 0x100003D2, 0x100003D3, 0x100003D4, 0x100003D5, 0x100003D6], Enum.GetValues<CharGenPageId>().Select(CharGenRules.PageElementId));
        Assert.Equal([0x100003EFu, 0x100003F0, 0x100003F1, 0x100003F2, 0x100003F3, 0x100003F4], Enum.GetValues<CharGenPageId>().Select(CharGenRules.TabElementId));
        Assert.Equal([0x10000025u, 0x10000026, 0x10000027, 0x10000028, 0x10000029, 0x1000002A], Enum.GetValues<CharGenPageId>().Select(CharGenRules.MasterState));
    }

    [Fact]
    public void The_heritage_table_has_thirteen_distinct_buttons_pictures_and_tokens_in_heritage_order()
    {
        var table = CharGenRules.Heritages;

        Assert.Equal(Enumerable.Range(1, 13).Select(i => (uint)i), table.Select(h => h.Heritage));
        Assert.Equal(13, table.Select(h => h.ButtonId).Distinct().Count());
        Assert.Equal(13, table.Select(h => h.BackgroundState).Distinct().Count());
        Assert.All(table, h => Assert.Same(h, CharGenRules.HeritageOfButton(h.ButtonId)));
        Assert.Equal(0x100005F1u, CharGenRules.HeritageEntry(8)!.ButtonId); // Lugian
        Assert.Equal(0x100003C3u, CharGenRules.HeritageEntry(CharGenMainScreen.ViamontianHeritage)!.ButtonId);
        Assert.Null(CharGenRules.HeritageEntry(0));
        Assert.Null(CharGenRules.HeritageEntry(14));

        // The Penumbraen use the Shadowbound strings, as retail's Update does.
        Assert.Equal(table[4].DescriptionToken, table[9].DescriptionToken);
        Assert.Equal(table[4].BonusSkillsToken, table[9].BonusSkillsToken);
    }

    [Fact]
    public void The_profession_table_has_seven_templates_whose_buttons_are_not_in_template_order()
    {
        var table = CharGenRules.Templates;

        Assert.Equal(Enumerable.Range(0, 7), table.Select(t => t.Template));
        Assert.Equal(7, table.Select(t => t.ButtonId).Distinct().Count());
        Assert.Equal(7, table.Select(t => t.BackgroundState).Distinct().Count());
        Assert.Equal(2, CharGenRules.TemplateOfButton(0x100003DF)!.Template); // swashbuckler: the last button, the third template
        Assert.Equal(0x100003DBu, CharGenRules.TemplateEntry(3)!.ButtonId); // life caster
        Assert.Null(CharGenRules.TemplateEntry(-1));
        Assert.Null(CharGenRules.TemplateEntry(7));
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(11, 6)]
    [InlineData(99, 50)]
    [InlineData(100, 50)]
    [InlineData(55, 28)]
    public void Health_is_endurance_halved_and_rounded_half_up(int endurance, int health) => Assert.Equal(health, CharGenRules.HealthOf(endurance));

    [Theory]
    [InlineData("", 10)]
    [InlineData("5", 10)]
    [InlineData("10", 10)]
    [InlineData("55", 55)]
    [InlineData("100", 100)]
    [InlineData("101", 100)]
    [InlineData("99999999999999999999", 100)]
    [InlineData("12abc", 12)]
    [InlineData("abc", 10)]
    public void A_typed_attribute_means_its_leading_digits_between_10_and_100(string text, int expected) => Assert.Equal(expected, CharGenRules.AttributeFromTypedText(text));

    [Theory]
    [InlineData("", 0.1f)]
    [InlineData("5", 0.1f)]
    [InlineData("50", 0.5f)]
    [InlineData("100", 1f)]
    [InlineData("250", 1f)]
    public void The_slider_follows_a_typed_number_between_a_tenth_and_one(string text, float expected) => Assert.Equal(expected, CharGenRules.SliderGoalFromTypedText(text), 4);

    [Theory]
    [InlineData(0u, 10)]
    [InlineData(99u, 10)]
    [InlineData(100u, 10)]
    [InlineData(551u, 55)]
    [InlineData(1000u, 100)]
    public void A_slider_position_in_thousandths_is_a_tenth_of_it_at_least_10(uint thousandths, int expected) => Assert.Equal(expected, CharGenRules.AttributeFromSlider(thousandths));

    [Theory]
    [InlineData(SkillAdvancementClass.Untrained, 6, 12, true, true, 10, "6", "0", true, false)]
    [InlineData(SkillAdvancementClass.Untrained, 6, 12, true, true, 5, "6", "0", false, false)]
    [InlineData(SkillAdvancementClass.Untrained, 6, 12, true, true, 6, "6", "0", true, false)] // exactly enough
    [InlineData(SkillAdvancementClass.Trained, 6, 12, true, true, 6, "6", "6", true, true)]
    [InlineData(SkillAdvancementClass.Trained, 6, 12, true, true, 5, "6", "6", false, true)]
    [InlineData(SkillAdvancementClass.Trained, 0, 4, false, true, 9, "4", "0", true, false)] // a free skill cannot be untrained
    [InlineData(SkillAdvancementClass.Specialized, 6, 12, true, true, 0, "0", "6", false, true)]
    [InlineData(SkillAdvancementClass.Specialized, 6, 12, true, false, 9, "0", "6", false, false)] // may not be specialised, so may not be unspecialised
    [InlineData(SkillAdvancementClass.Trained, 0, 999, false, true, 20, "", "0", false, false)] // a step of 999 or more shows no cost and cannot be bought
    [InlineData(SkillAdvancementClass.Untrained, 999, 999, true, true, 20, "", "0", false, false)]
    public void A_skill_row_shows_the_step_costs_and_enables_the_arrows_the_credits_allow(
        SkillAdvancementClass level, int train, int spec, bool canUntrain, bool canUnspecialize, int credits, string up, string down, bool upEnabled, bool downEnabled)
    {
        var row = CharGenRules.SkillRow(level, train, spec, canUntrain, canUnspecialize, credits);

        Assert.Equal(new CharGenSkillRow(up, down, upEnabled, downEnabled), row);
    }

    [Fact]
    public void A_skill_in_no_class_has_no_row() => Assert.Null(CharGenRules.SkillRow(SkillAdvancementClass.Inactive, 1, 2, true, true, 5));

    [Fact]
    public void A_heritage_that_trains_a_skill_for_nothing_shifts_its_costs_and_forbids_untraining_it()
    {
        var skill = Entry(trained: 4, specialized: 10);

        Assert.Equal((4, 10, true, true), CharGenRules.SkillCosts(skill, null));
        Assert.Equal((0, 6, false, true), CharGenRules.SkillCosts(skill, new CharGenSkill(skill.Id, 0, 2)));
        Assert.Equal((0, 0, false, false), CharGenRules.SkillCosts(skill, new CharGenSkill(skill.Id, 0, 0)));
        Assert.Equal((4, 0, true, false), CharGenRules.SkillCosts(skill, new CharGenSkill(skill.Id, 3, 0)));
        Assert.Equal((4, 10, true, true), CharGenRules.SkillCosts(skill, new CharGenSkill(skill.Id, 3, 8))); // a nonzero heritage cost changes nothing on the page
        Assert.Equal((0, 5, false, true), CharGenRules.SkillCosts(Entry(trained: 0, specialized: 5), null));
        Assert.Equal((4, 0, true, false), CharGenRules.SkillCosts(Entry(trained: 4, specialized: 0), null));
    }

    [Theory]
    [InlineData(SkillAdvancementClass.Specialized, 1u, CharGenSkillGroup.Specialized)]
    [InlineData(SkillAdvancementClass.Specialized, 2u, CharGenSkillGroup.Specialized)]
    [InlineData(SkillAdvancementClass.Trained, 2u, CharGenSkillGroup.Trained)]
    [InlineData(SkillAdvancementClass.Untrained, 1u, CharGenSkillGroup.UsableUntrained)]
    [InlineData(SkillAdvancementClass.Untrained, 2u, CharGenSkillGroup.UnusableUntrained)]
    public void A_skill_belongs_to_the_group_of_its_class_and_minimum_level(SkillAdvancementClass level, uint minimum, CharGenSkillGroup expected) =>
        Assert.Equal(expected, CharGenRules.GroupOf(level, minimum));

    [Theory]
    [InlineData(0, 1, 0, 1, 1, 0, "Formula : Strength")]
    [InlineData(0, 1, 1, 2, 4, 3, "Formula : (Coordination + Quickness) / 2")]
    [InlineData(0, 1, 2, 3, 6, 5, "Formula : (Self + (2 x Focus)) / 3")]
    [InlineData(5, 3, 0, 4, 2, 0, "Formula : (3 x Endurance) / 4 +5")]
    [InlineData(0, 0, 1, 1, 0, 6, "Formula : Self")] // the second attribute alone
    [InlineData(0, 0, 0, 1, 0, 0, "Formula : ")] // no attribute at all
    [InlineData(0, 2, 0, 0, 1, 0, "Formula : (2 x Strength) / 0")] // a divisor of 0 is printed, as retail prints it
    public void A_skill_formula_is_worded_as_retail_words_it(int bonus, int x, int y, int divisor, uint attribute1, uint attribute2, string expected) =>
        Assert.Equal(expected, CharGenRules.FormulaText(new SkillTableFormula(bonus, x, y, divisor, attribute1, attribute2)));

    [Fact]
    public void The_bonus_line_names_the_class_bonus()
    {
        Assert.Equal("Training Bonus  +5\n", CharGenRules.BonusLine(SkillAdvancementClass.Trained));
        Assert.Equal("Specialization Bonus  +10\n", CharGenRules.BonusLine(SkillAdvancementClass.Specialized));
        Assert.Equal(string.Empty, CharGenRules.BonusLine(SkillAdvancementClass.Untrained));
    }

    [Fact]
    public void The_attribute_names_are_retails()
    {
        Assert.Equal(["Strength", "Endurance", "Quickness", "Coordination", "Focus", "Self"], Enumerable.Range(1, 6).Select(i => CharGenState.GetAttributeName((uint)i)));
        Assert.Equal(string.Empty, CharGenState.GetAttributeName(0));
        Assert.Equal(string.Empty, CharGenState.GetAttributeName(7));
    }

    private static SkillTableEntry Entry(int trained, int specialized) =>
        new(1, "Test", "A test skill.", 0, trained, specialized, 1, true, 1, new SkillTableFormula(0, 1, 1, 2, 1, 2), 0, 0, 0);
}
