using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Screens;

/// <summary>
/// Character creation's main screen and its heritage, profession and skills pages (m2-plan G3 U5, first stage) over the user's dats with the retail
/// fonts and strings: the elements each page uses, the page order and what ghosts, hides or asks, the heritage buttons, the attribute credits through
/// the profession page's controller, and the skill credits through the skills page's. Retail never ghosts Back or Next, so the gates tested here are
/// the ones retail has: the Olthoi's missing pages, the Throne of Destiny, the credits, the locks and the skills that may not go back.
/// </summary>
public sealed class CharGenScreenTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private UiElementManager Manager(RecordingLogSink log) => new(
        new UiServices { Descs = UiDescSource.From(new UiLayouts(fixture.Dats)), Log = log },
        UiScalePolicy.Choose(800, 600));

    private LoginRig Rig(bool throneOfDestiny = true, int seed = 7, UiRetailGlitches glitches = UiRetailGlitches.None) =>
        new(fixture, startMode: UiModes.CharGen, throneOfDestiny: throneOfDestiny, seed: seed, glitches: glitches);

    private static CharGenMainScreen Open(LoginRig rig)
    {
        rig.Run(0.3);
        return Assert.IsType<CharGenMainScreen>(rig.Flow.CurrentScreen);
    }

    private string Pregame(string token) => new UiStrings(new UiStringTables(fixture.Dats)).Render(new UiStringRef(StringHash.Tokenize(token), GmUiFlow.PregameTableId)).Text;

    private static void ClickAndRun(LoginRig rig, UiElement element)
    {
        Assert.True(rig.Click(element), $"the click on element 0x{element.ElementId:X8} reached nothing");
        rig.Run(0.1);
    }

    /// <summary>Clicks a skill row's arrow with the mouse, the row first scrolled into the list's view.</summary>
    private static void ClickArrow(LoginRig rig, CharGenSkillsPage page, uint skill, bool up)
    {
        var index = page.List!.Items.ToList().IndexOf(page.RowOf(skill)!);
        page.List.ScrollToShow(index);
        rig.Run(0.05);
        ClickAndRun(rig, (up ? page.UpArrowOf(skill) : page.DownArrowOf(skill))!);
    }

    private static UiElement Child(CharGenMainScreen screen, uint id) => Assert.IsAssignableFrom<UiElement>(screen.Root!.GetChildRecursive(id));

    // ---------------------------------------------------------------- The layout and the strings

    public static TheoryData<string, uint, Type> Elements()
    {
        var data = new TheoryData<string, uint, Type>();
        void Add(string what, Type type, params uint[] ids)
        {
            foreach (var id in ids)
            {
                data.Add(what, id, type);
            }
        }

        Add("tabs", typeof(UiButton), Enum.GetValues<CharGenPageId>().Select(CharGenRules.TabElementId).ToArray());
        Add("navigation", typeof(UiButton), CharGenMainScreen.LeftButtonId, CharGenMainScreen.RightButtonId, CharGenMainScreen.FinishButtonId, CharGenMainScreen.HelpButtonId, CharGenMainScreen.ExitButtonId, CharGenMainScreen.RandomButtonId);
        Add("master", typeof(UiElement), CharGenMainScreen.ProgressBarId, CharGenMainScreen.MainMenuId, CharGenMainScreen.MasterPageId);
        Add("page roots", typeof(UiElement), Enum.GetValues<CharGenPageId>().Select(CharGenRules.PageElementId).ToArray());
        Add("heritage buttons", typeof(UiButton), CharGenRules.Heritages.Select(h => h.ButtonId).ToArray());
        Add("heritage", typeof(UiTextElement), CharGenHeritagePage.TextId);
        Add("heritage", typeof(UiElement), CharGenHeritagePage.BackgroundId);
        Add("profession buttons", typeof(UiButton), CharGenRules.Templates.Select(t => t.ButtonId).ToArray());
        Add("profession", typeof(UiTextElement), CharGenProfessionPage.DescriptionId);
        Add("profession", typeof(UiElement), CharGenProfessionPage.BackgroundId);
        Add("profession", typeof(UiButton), CharGenProfessionPage.CreditsFieldId, CharGenProfessionPage.HealthFieldId, CharGenProfessionPage.StaminaFieldId, CharGenProfessionPage.ManaFieldId);
        Add("profession rows", typeof(UiElement), CharGenProfessionPage.RowFields.Select(r => r.FieldId).ToArray());
        Add("skills", typeof(UiListBox), CharGenSkillsPage.ListId);
        Add("skills", typeof(UiTextElement), CharGenSkillsPage.CreditsId, CharGenSkillsPage.InfoTitleId, CharGenSkillsPage.InfoTextId);
        return data;
    }

    [RequiresLocalDatsTheory]
    [MemberData(nameof(Elements))]
    public void Every_element_character_creation_uses_exists_in_its_layout_with_the_class_it_expects(string what, uint id, Type type)
    {
        var log = new RecordingLogSink(LogSeverity.Error);
        var manager = Manager(log);
        manager.ForceDisplayResolution(true);

        var root = manager.CreateRootElement(UiLayoutEnums.CharGenRoot.LayoutEnum, UiLayoutEnums.CharGenRoot.ElementId);

        Assert.NotNull(root);
        var found = root.GetChildRecursive(id);
        Assert.True(found is not null, $"{what}: element 0x{id:X8} is missing from the layout");
        Assert.IsAssignableFrom(type, found);
        Assert.Empty(log.Lines);
    }

    [RequiresLocalDatsFact]
    public void Each_attribute_row_and_skill_row_holds_the_children_the_pages_find_in_them()
    {
        var manager = Manager(new RecordingLogSink(LogSeverity.Error));
        manager.ForceDisplayResolution(true);
        var root = manager.CreateRootElement(UiLayoutEnums.CharGenRoot.LayoutEnum, UiLayoutEnums.CharGenRoot.ElementId)!;

        foreach (var (_, fieldId) in CharGenProfessionPage.RowFields)
        {
            var field = root.GetChildRecursive(fieldId)!;
            Assert.IsAssignableFrom<UiButton>(field.GetChildRecursive(CharGenProfessionPage.LockId));
            Assert.IsAssignableFrom<UiTextElement>(field.GetChildRecursive(CharGenProfessionPage.NameId));
            Assert.IsAssignableFrom<UiScrollbar>(field.GetChildRecursive(CharGenProfessionPage.SliderId));
            Assert.IsAssignableFrom<UiTextElement>(field.GetChildRecursive(CharGenProfessionPage.NumberId));
            Assert.Contains(CharGenRules.LockedState, field.GetChildRecursive(CharGenProfessionPage.LockId)!.Desc.States.Keys);
        }

        foreach (var id in new[] { CharGenProfessionPage.CreditsFieldId, CharGenProfessionPage.HealthFieldId, CharGenProfessionPage.StaminaFieldId, CharGenProfessionPage.ManaFieldId })
        {
            Assert.IsAssignableFrom<UiTextElement>(root.GetChildRecursive(id)!.GetChildRecursive(id == CharGenProfessionPage.CreditsFieldId ? CharGenProfessionPage.CreditsTextId : CharGenProfessionPage.ValueTextId));
        }

        // Every heritage and profession button has the two states the pages set, and the skills list has a header and a skill row template.
        Assert.All(CharGenRules.Heritages, h => Assert.Contains(CharGenRules.SelectedState, root.GetChildRecursive(h.ButtonId)!.Desc.States.Keys));
        Assert.All(CharGenRules.Heritages, h => Assert.Contains(h.BackgroundState, root.GetChildRecursive(CharGenHeritagePage.BackgroundId)!.Desc.States.Keys));
        Assert.All(CharGenRules.Templates, t => Assert.Contains(t.BackgroundState, root.GetChildRecursive(CharGenProfessionPage.BackgroundId)!.Desc.States.Keys));
        var list = Assert.IsType<UiListBox>(root.GetChildRecursive(CharGenSkillsPage.ListId));
        var header = Assert.IsAssignableFrom<UiElement>(list.AddItemFromTemplateList(0));
        Assert.IsAssignableFrom<UiTextElement>(header.GetChildRecursive(CharGenSkillsPage.HeaderTextId));
        var row = Assert.IsAssignableFrom<UiElement>(list.AddItemFromTemplateList(1));
        foreach (var id in new[] { CharGenSkillsPage.RowNameId, CharGenSkillsPage.RowLevelId, CharGenSkillsPage.RowUpCostId, CharGenSkillsPage.RowDownCostId })
        {
            Assert.IsAssignableFrom<UiTextElement>(row.GetChildRecursive(id));
        }

        Assert.IsAssignableFrom<UiButton>(row.GetChildRecursive(CharGenSkillsPage.UpButtonId));
        Assert.IsAssignableFrom<UiButton>(row.GetChildRecursive(CharGenSkillsPage.DownButtonId));
        Assert.Contains(CharGenRules.ArrowEnabledState, row.GetChildRecursive(CharGenSkillsPage.UpButtonId)!.Desc.States.Keys);
        Assert.Contains(CharGenRules.ArrowDisabledState, row.GetChildRecursive(CharGenSkillsPage.DownButtonId)!.Desc.States.Keys);
    }

    [RequiresLocalDatsFact]
    public void Every_string_the_pages_and_dialogs_ask_for_is_in_the_pregame_table()
    {
        var strings = new UiStrings(new UiStringTables(fixture.Dats));
        var tokens = new List<string>
        {
            "ID_CharGen_ExitWarning",
            "ID_CharGen_RandomizeWarning",
            "ID_CharGen_ToDRequiredWarning",
            "ID_CharGen_CreditWarning",
            "ID_CharGen_NoNameWarning",
            "ID_CharGen_Heritage_StartingSkills_Header",
            "ID_CharGen_Heritage_StartingSkills",
            "ID_CharGen_Heritage_BonusSkills_Trained_Header",
            "ID_CharGen_Specialized",
            "ID_CharGen_Specialized_Tooltip",
            "ID_CharGen_Trained",
            "ID_CharGen_Trained_Tooltip",
            "ID_CharGen_UseableUntrained",
            "ID_CharGen_UseableUntrained_Tooltip",
            "ID_CharGen_UnuseableUntrained",
            "ID_CharGen_UnuseableUntrained_Tooltip",
        };
        tokens.AddRange(CharGenRules.Heritages.SelectMany(h => new[] { h.DescriptionToken, h.BonusSkillsToken }));
        tokens.AddRange(CharGenRules.Templates.Select(t => t.DescriptionToken));

        foreach (var token in tokens.Distinct())
        {
            var result = strings.Render(new UiStringRef(StringHash.Tokenize(token), GmUiFlow.PregameTableId));
            Assert.True(result.Status is UiStringStatus.Ok or UiStringStatus.Unsupported, $"{token} is not in the pregame table ({result.Status})");
            Assert.False(string.IsNullOrWhiteSpace(result.Text), $"{token} is empty");
        }

        Assert.StartsWith("Warning!", Pregame("ID_CharGen_ExitWarning"), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- Reaching it, and leaving

    [RequiresLocalDatsFact]
    public void Character_management_Create_opens_character_creation_on_the_heritage_page_and_Back_then_Yes_returns()
    {
        using var rig = new LoginRig(fixture);
        var management = rig.ToCharacterManagement();

        ClickAndRun(rig, management.CreateButton!);
        rig.Run(0.2);

        var screen = Assert.IsType<CharGenMainScreen>(rig.Flow.CurrentScreen);
        Assert.Equal(UiModes.CharGen, rig.Flow.CurrentMode);
        Assert.Equal(CharGenPageId.Heritage, screen.Progress);

        ClickAndRun(rig, screen.LeftButton!);
        Assert.NotEqual(0u, screen.ExitDialog);
        Assert.StartsWith("Warning!", rig.DialogText(screen.ExitDialog), StringComparison.Ordinal);
        Assert.IsType<UiConfirmationDialog>(rig.Dialog(screen.ExitDialog));
        var first = screen.ExitDialog;
        ClickAndRun(rig, screen.LeftButton!); // a second ask while one shows opens no second dialog
        Assert.Equal(first, screen.ExitDialog);

        var no = Assert.IsAssignableFrom<UiElement>(rig.Dialog(screen.ExitDialog)!.GetChildRecursive(UiConfirmationDialog.NoButton));
        ClickAndRun(rig, no);
        Assert.Equal(0u, screen.ExitDialog);
        Assert.Equal(UiModes.CharGen, rig.Flow.CurrentMode);

        ClickAndRun(rig, Child(screen, CharGenMainScreen.ExitButtonId)); // the Exit button asks the same
        var yes = Assert.IsAssignableFrom<UiElement>(rig.Dialog(screen.ExitDialog)!.GetChildRecursive(UiConfirmationDialog.YesButton));
        ClickAndRun(rig, yes);
        rig.Run(0.2);

        Assert.Equal(UiModes.CharacterManagement, rig.Flow.CurrentMode);
        Assert.IsType<CharacterManagementScreen>(rig.Flow.CurrentScreen);
    }

    [RequiresLocalDatsFact]
    public void The_escape_key_action_asks_to_leave_like_Exit()
    {
        using var rig = Rig();
        var screen = Open(rig);

        rig.Manager.BroadcastGlobalMessage(UiGlobalMessageIds.ActionStart, (int)CharGenMainScreen.ExitAction);
        rig.Frame();

        Assert.NotEqual(0u, screen.ExitDialog);
        rig.Manager.BroadcastGlobalMessage(UiGlobalMessageIds.ActionStart, 5); // another action is not ours
        rig.Manager.BroadcastGlobalMessage(UiGlobalMessageIds.ActionEnd, (int)CharGenMainScreen.ExitAction);
        Assert.Single(rig.Manager.Dialogs.OpenDialogs);
    }

    [RequiresLocalDatsFact]
    public void Help_says_it_is_not_in_M2_and_logs_a_todo()
    {
        using var rig = Rig();
        var screen = Open(rig);

        ClickAndRun(rig, Child(screen, CharGenMainScreen.HelpButtonId));

        var dialog = Assert.Single(rig.Manager.Dialogs.OpenDialogs);
        Assert.StartsWith("Not in Lifestone M2: Help", Assert.IsAssignableFrom<UiTextElement>(dialog.GetChildRecursive(UiDialog.TextElement)).Text, StringComparison.Ordinal);
        Assert.True(rig.Log.AnyLineContains("TODO(retailui-chargen-help)"));
    }

    // ---------------------------------------------------------------- Navigation

    private static void AssertOnly(CharGenMainScreen screen, CharGenPageId shown)
    {
        Assert.Equal(shown, screen.Progress);
        foreach (var page in Enum.GetValues<CharGenPageId>())
        {
            Assert.Equal(page == shown, screen.Page(page)!.Root.Visible);
            Assert.Equal(page == shown ? UiStateIds.Highlight : UiStateIds.Normal, screen.TabOf(page)!.State);
        }

        Assert.Equal(shown != CharGenPageId.Summary, screen.RightButton!.Visible);
        Assert.Equal(shown == CharGenPageId.Summary, screen.FinishButton!.Visible);
    }

    [RequiresLocalDatsFact]
    public void Next_and_Back_walk_the_six_pages_and_never_ghost()
    {
        using var rig = Rig();
        var screen = Open(rig);
        AssertOnly(screen, CharGenPageId.Heritage);

        foreach (var page in new[] { CharGenPageId.Profession, CharGenPageId.Skills, CharGenPageId.Appearance, CharGenPageId.Town, CharGenPageId.Summary })
        {
            Assert.NotEqual(UiStateIds.Ghosted, screen.RightButton!.State);
            Assert.NotEqual(UiStateIds.Ghosted, screen.LeftButton!.State);
            ClickAndRun(rig, screen.RightButton);
            AssertOnly(screen, page);
        }

        // Next is gone on the summary page; a message for it is ignored.
        rig.Manager.BroadcastElementMessage(screen.RightButton!, UiMessageIds.ButtonClick, 7, 0);
        rig.Run(0.1);
        AssertOnly(screen, CharGenPageId.Summary);

        foreach (var page in new[] { CharGenPageId.Town, CharGenPageId.Appearance, CharGenPageId.Skills, CharGenPageId.Profession, CharGenPageId.Heritage })
        {
            ClickAndRun(rig, screen.LeftButton!);
            AssertOnly(screen, page);
        }

        Assert.Equal(0u, screen.ExitDialog); // Back on a later page never asked to leave
    }

    [RequiresLocalDatsFact]
    public void A_tab_opens_its_page_directly_and_the_master_page_takes_the_pages_state()
    {
        using var rig = Rig();
        var screen = Open(rig);

        foreach (var page in new[] { CharGenPageId.Summary, CharGenPageId.Skills, CharGenPageId.Appearance, CharGenPageId.Profession, CharGenPageId.Town, CharGenPageId.Heritage })
        {
            ClickAndRun(rig, screen.TabOf(page)!);
            AssertOnly(screen, page);
            Assert.Contains(CharGenRules.MasterState(page), screen.Page(page)!.Root.Desc.States.Keys);
        }
    }

    [RequiresLocalDatsTheory]
    [InlineData(0x100005C7u, 12u)]
    [InlineData(0x100005C8u, 13u)]
    public void The_olthoi_have_no_profession_skills_or_town_page(uint button, uint heritage)
    {
        using var rig = Rig();
        var screen = Open(rig);

        ClickAndRun(rig, Child(screen, button));

        Assert.Equal(heritage, screen.State.Heritage);
        Assert.All(new[] { CharGenPageId.Profession, CharGenPageId.Skills, CharGenPageId.Town }, p => Assert.False(screen.TabOf(p)!.Visible));
        Assert.All(new[] { CharGenPageId.Heritage, CharGenPageId.Appearance, CharGenPageId.Summary }, p => Assert.True(screen.TabOf(p)!.Visible));

        ClickAndRun(rig, screen.RightButton!);
        AssertOnly(screen, CharGenPageId.Appearance);
        ClickAndRun(rig, screen.RightButton!);
        AssertOnly(screen, CharGenPageId.Summary);
        ClickAndRun(rig, screen.LeftButton!);
        AssertOnly(screen, CharGenPageId.Appearance);
        ClickAndRun(rig, screen.LeftButton!);
        AssertOnly(screen, CharGenPageId.Heritage);
        Assert.False(screen.TabOf(CharGenPageId.Skills)!.Visible); // still hidden on the way back

        ClickAndRun(rig, Child(screen, 0x100003BF)); // Aluvian: the tabs return
        Assert.All(new[] { CharGenPageId.Profession, CharGenPageId.Skills, CharGenPageId.Town }, p => Assert.True(screen.TabOf(p)!.Visible));
    }

    [RequiresLocalDatsTheory]
    [InlineData(UiRetailGlitches.None, true)]
    [InlineData(UiRetailGlitches.CharGenHeritageTabs, false)]
    public void Choosing_the_Lugians_after_the_olthoi_shows_the_tabs_unless_retails_glitch_is_restored(UiRetailGlitches glitches, bool tabsShown)
    {
        using var rig = Rig(glitches: glitches);
        var screen = Open(rig);
        ClickAndRun(rig, Child(screen, 0x100005C7)); // Olthoi
        Assert.False(screen.TabOf(CharGenPageId.Skills)!.Visible);

        ClickAndRun(rig, Child(screen, 0x100005F1)); // Lugian

        Assert.Equal(8u, screen.State.Heritage);
        Assert.Equal(tabsShown, screen.TabOf(CharGenPageId.Skills)!.Visible);
        ClickAndRun(rig, screen.RightButton!); // the next page always shows them again, and Lugians have their pages
        AssertOnly(screen, CharGenPageId.Profession);
        Assert.True(screen.TabOf(CharGenPageId.Skills)!.Visible);
    }

    // ---------------------------------------------------------------- Heritage page

    [RequiresLocalDatsFact]
    public void Each_heritage_button_chooses_its_heritage_and_shows_it()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = screen.Heritage!;
        var strings = new UiStrings(new UiStringTables(fixture.Dats));

        foreach (var entry in CharGenRules.Heritages)
        {
            ClickAndRun(rig, Child(screen, entry.ButtonId));

            Assert.Equal(entry.Heritage, screen.State.Heritage);
            foreach (var other in CharGenRules.Heritages)
            {
                Assert.Equal(other.Heritage == entry.Heritage ? CharGenRules.SelectedState : CharGenRules.UnselectedState, page.ButtonState(other.Heritage));
            }

            Assert.Equal(entry.BackgroundState, page.BackgroundState);
            var description = strings.Render(new UiStringRef(StringHash.Tokenize(entry.DescriptionToken), GmUiFlow.PregameTableId)).Text;
            var bonus = strings.Render(new UiStringRef(StringHash.Tokenize(entry.BonusSkillsToken), GmUiFlow.PregameTableId)).Text;
            var starting = strings.Render(new UiStringRef(StringHash.Tokenize("ID_CharGen_Heritage_StartingSkills"), GmUiFlow.PregameTableId)).Text;
            Assert.StartsWith(Pregame("ID_CharGen_Heritage_StartingSkills_Header"), page.Text, StringComparison.Ordinal);
            Assert.Contains(starting, page.Text, StringComparison.Ordinal);
            Assert.Contains(bonus, page.Text, StringComparison.Ordinal);
            Assert.EndsWith(description, page.Text, StringComparison.Ordinal);
            Assert.True(page.Text.IndexOf(bonus, StringComparison.Ordinal) < page.Text.IndexOf(description, StringComparison.Ordinal), "the bonus skills come before the description");
        }
    }

    [RequiresLocalDatsFact]
    public void A_heritage_click_takes_the_new_heritages_credits_and_template_through_the_state()
    {
        using var rig = Rig();
        var screen = Open(rig);

        ClickAndRun(rig, Child(screen, 0x100005C7)); // Olthoi: 60 attribute credits and 68 skill credits
        Assert.Equal((60, 68), (screen.State.TotalAttributeCredits, screen.State.TotalSkillCredits));
        ClickAndRun(rig, Child(screen, 0x100003BF)); // Aluvian
        Assert.Equal((330, 52), (screen.State.TotalAttributeCredits, screen.State.TotalSkillCredits));
    }

    [RequiresLocalDatsFact]
    public void Viamontian_without_the_Throne_of_Destiny_says_so_and_changes_nothing()
    {
        using var rig = Rig(throneOfDestiny: false);
        var screen = Open(rig);
        ClickAndRun(rig, Child(screen, 0x100003BF)); // Aluvian
        var before = screen.State.Heritage;

        ClickAndRun(rig, Child(screen, 0x100003C3)); // Viamontian

        Assert.Equal(before, screen.State.Heritage);
        Assert.NotEqual(0u, screen.ThroneDialog);
        Assert.IsType<UiMessageDialog>(rig.Dialog(screen.ThroneDialog));
        Assert.Equal(Pregame("ID_CharGen_ToDRequiredWarning"), rig.DialogText(screen.ThroneDialog));
        Assert.Equal(CharGenRules.SelectedState, screen.Heritage!.ButtonState(before));
        Assert.Equal(CharGenRules.UnselectedState, screen.Heritage.ButtonState(CharGenMainScreen.ViamontianHeritage));
        var first = screen.ThroneDialog;
        ClickAndRun(rig, Child(screen, 0x100003C3)); // a second click while the message shows opens no second one
        Assert.Equal(first, screen.ThroneDialog);
    }

    [RequiresLocalDatsFact]
    public void Viamontian_with_the_Throne_of_Destiny_is_chosen()
    {
        using var rig = Rig();
        var screen = Open(rig);

        ClickAndRun(rig, Child(screen, 0x100003C3));

        Assert.Equal(CharGenMainScreen.ViamontianHeritage, screen.State.Heritage);
        Assert.Equal(0u, screen.ThroneDialog);
    }

    // ---------------------------------------------------------------- Profession page

    private static CharGenProfessionPage ToProfession(LoginRig rig, CharGenMainScreen screen, uint heritageButton = 0x100003BF)
    {
        ClickAndRun(rig, Child(screen, heritageButton));
        ClickAndRun(rig, screen.TabOf(CharGenPageId.Profession)!);
        return screen.Profession!;
    }

    private static void AssertAttributesShown(CharGenMainScreen screen)
    {
        var state = screen.State;
        var page = screen.Profession!;
        for (uint attribute = 1; attribute <= 6; attribute++)
        {
            Assert.Equal(state.GetAttribute(attribute).ToString(System.Globalization.CultureInfo.InvariantCulture), page.NumberText(attribute));
            Assert.Equal(state.GetAttribute(attribute) * 0.01f, page.SliderOf(attribute)!.Position, 3);
        }

        Assert.Equal(state.RemainingAttributeCredits.ToString(System.Globalization.CultureInfo.InvariantCulture), page.CreditsText);
        Assert.Equal(CharGenRules.HealthOf(state.Endurance).ToString(System.Globalization.CultureInfo.InvariantCulture), page.HealthText);
        Assert.Equal(state.Endurance.ToString(System.Globalization.CultureInfo.InvariantCulture), page.StaminaText);
        Assert.Equal(state.Self.ToString(System.Globalization.CultureInfo.InvariantCulture), page.ManaText);
        var sum = Enumerable.Range(1, 6).Sum(a => state.GetAttribute((uint)a));
        Assert.Equal(state.TotalAttributeCredits - sum, state.RemainingAttributeCredits);
        Assert.True(state.RemainingAttributeCredits >= 0);
    }

    [RequiresLocalDatsFact]
    public void Each_profession_button_applies_its_template_and_shows_it()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToProfession(rig, screen);
        var templates = screen.Catalog.Data.GetHeritage(screen.State.Heritage).Templates;

        foreach (var entry in CharGenRules.Templates)
        {
            ClickAndRun(rig, Child(screen, entry.ButtonId));

            Assert.Equal(entry.Template, screen.State.Template);
            foreach (var other in CharGenRules.Templates)
            {
                Assert.Equal(other.Template == entry.Template ? CharGenRules.SelectedState : CharGenRules.UnselectedState, page.TemplateButtonState(other.Template));
            }

            Assert.Equal(entry.BackgroundState, page.BackgroundState);
            Assert.Equal(Pregame(entry.DescriptionToken), page.DescriptionText);
            if (entry.Template != 0)
            {
                var template = templates[entry.Template];
                Assert.Equal(
                    [template.Strength, template.Endurance, template.Quickness, template.Coordination, template.Focus, template.Self],
                    [screen.State.Strength, screen.State.Endurance, screen.State.Quickness, screen.State.Coordination, screen.State.Focus, screen.State.Self]);
            }

            AssertAttributesShown(screen);
        }
    }

    [RequiresLocalDatsFact]
    public void A_raise_is_cut_to_what_the_credits_allow_and_the_other_attributes_balance()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToProfession(rig, screen);
        ClickAndRun(rig, Child(screen, CharGenRules.TemplateEntry(0)!.ButtonId));
        var state = screen.State;

        // Raising Strength to 100 with the credits for less: the page cuts the request to current + allowed, so the state never overspends.
        for (uint attribute = 1; attribute <= 6; attribute++)
        {
            var allowed = state.GetAbsRemainingCredits(attribute);
            var current = state.GetAttribute(attribute);
            page.SetAttribValue(attribute, 100);

            Assert.Equal(Math.Min(100, current + allowed), state.GetAttribute(attribute));
            AssertAttributesShown(screen);
            Assert.All(Enumerable.Range(1, 6), a => Assert.InRange(state.GetAttribute((uint)a), 10, 100));
        }

        // Lowering one is allowed all the way to 10, and the credits it frees show.
        var before = state.RemainingAttributeCredits;
        page.SetAttribValue(1, 10);
        Assert.Equal(10, state.Strength);
        Assert.True(state.RemainingAttributeCredits >= before);
        AssertAttributesShown(screen);
    }

    [RequiresLocalDatsFact]
    public void A_slider_moved_by_the_player_sets_the_attribute_a_tenth_of_its_thousandths()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToProfession(rig, screen);
        ClickAndRun(rig, Child(screen, CharGenRules.TemplateEntry(0)!.ButtonId));
        var state = screen.State;
        page.SetAttribValue(1, 10);
        page.SetAttribValue(2, 10);
        var allowed = state.GetAbsRemainingCredits(3);
        var current = state.Quickness;

        page.SliderOf(3)!.SetScrollbarPosition(0.425f);

        Assert.Equal(42 <= current ? 42 : Math.Min(42, current + allowed), state.Quickness); // asked 42 (425 thousandths), cut to what the credits allow
        AssertAttributesShown(screen);

        page.SliderOf(4)!.SetScrollbarPosition(0f); // the slider's low end is the attribute's floor
        Assert.Equal(10, state.Coordination);
        AssertAttributesShown(screen);
    }

    [RequiresLocalDatsFact]
    public void A_typed_number_is_digits_only_moves_the_slider_while_typed_and_sets_the_attribute_when_the_field_loses_the_focus()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToProfession(rig, screen);
        ClickAndRun(rig, Child(screen, CharGenRules.TemplateEntry(0)!.ButtonId));
        var state = screen.State;
        page.SetAttribValue(5, 10);
        page.SetAttribValue(6, 10);
        var field = page.NumberFieldOf(5)!;

        Assert.False(field.Filter!('a'));
        Assert.False(field.Filter('-'));
        Assert.True(field.Filter('7'));

        rig.Manager.SetFocusElement(field);
        Assert.Equal(field.Text, field.SelectedText); // gaining the focus selects the whole number
        field.SetText(string.Empty);
        field.CharacterHandler('4');
        field.CharacterHandler('0');
        Assert.Equal("40", field.Text);

        // The slider's goal follows the typing (retail sets property 0x85); whether the slider glides there depends on the layout's smooth-movement
        // duration, which this slider lacks, so it catches up when the field loses the focus (UiScrollbar.StartAnimation, as the engine port reads it).
        Assert.True(page.SliderOf(5)!.InqProperty(UiPropertyIds.ScrollbarGoalPosition, out var goal));
        Assert.Equal(0.4f, Assert.IsType<UiFloatProperty>(goal).Value, 3);
        var allowed = state.GetAbsRemainingCredits(5);
        var current = state.Focus;
        rig.Manager.SetFocusElement(null);
        rig.Run(0.1);

        Assert.Equal(40 <= current ? 40 : Math.Min(40, current + allowed), state.Focus);
        AssertAttributesShown(screen);

        // Too little means 10, too much 100 (then cut by the credits).
        rig.Manager.SetFocusElement(field);
        field.SetText("3");
        rig.Manager.SetFocusElement(null);
        Assert.Equal(10, state.Focus);
        AssertAttributesShown(screen);
    }

    [RequiresLocalDatsFact]
    public void A_locked_attribute_keeps_its_value_through_balancing_and_a_profession_click_clears_the_locks()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToProfession(rig, screen);
        ClickAndRun(rig, Child(screen, CharGenRules.TemplateEntry(0)!.ButtonId));
        var state = screen.State;
        var endurance = state.Endurance;

        ClickAndRun(rig, page.LockOf(2)!);

        Assert.True(page.IsLocked(2));
        Assert.True(state.IsAttributeLocked(2));
        Assert.Equal(CharGenRules.LockedState, page.LockOf(2)!.State);
        Assert.Equal(CharGenRules.LockedState, page.SliderOf(2)!.State);
        Assert.Equal(CharGenRules.UnlockedState, page.LockOf(1)!.State);

        page.SetAttribValue(1, 100);
        page.SetAttribValue(3, 100);
        Assert.Equal(endurance, state.Endurance);
        AssertAttributesShown(screen);

        ClickAndRun(rig, Child(screen, CharGenRules.TemplateEntry(1)!.ButtonId)); // a profession clears every lock
        Assert.False(page.IsLocked(2));
        Assert.False(state.IsAttributeLocked(2));
        Assert.Equal(CharGenRules.UnlockedState, page.LockOf(2)!.State);
        Assert.Equal(CharGenRules.UnlockedState, page.SliderOf(2)!.State);

        ClickAndRun(rig, page.LockOf(4)!);
        ClickAndRun(rig, page.LockOf(4)!); // a second click unlocks
        Assert.False(page.IsLocked(4));
        Assert.False(state.IsAttributeLocked(4));
    }

    [RequiresLocalDatsFact]
    public void The_profession_page_names_its_attributes()
    {
        using var rig = Rig();
        var screen = Open(rig);
        ToProfession(rig, screen);
        var page = screen.Profession!;

        foreach (var (attribute, fieldId) in CharGenProfessionPage.RowFields)
        {
            var name = Assert.IsAssignableFrom<UiTextElement>(Child(screen, fieldId).GetChildRecursive(CharGenProfessionPage.NameId));
            Assert.Equal(CharGenState.GetAttributeName(attribute), name.Text);
        }

        Assert.NotNull(page);
    }

    // ---------------------------------------------------------------- Skills page

    /// <summary>The credits and rows agree with the state; the list is in groups, each by name; the arrows say what the state would do.</summary>
    private void AssertSkillsConsistent(CharGenMainScreen screen)
    {
        var state = screen.State;
        var page = screen.Skills!;
        Assert.True(state.RemainingSkillCredits >= 0, $"skill credits {state.RemainingSkillCredits}");
        Assert.Equal(state.RemainingSkillCredits.ToString(System.Globalization.CultureInfo.InvariantCulture), page.CreditsText);
        var incremental = state.RemainingSkillCredits;
        state.UpdateRemainingSkillCredits();
        Assert.Equal(incremental, state.RemainingSkillCredits); // what was charged step by step is what the classes cost

        Assert.Equal(state.SkillsByName.Count, page.SkillCount);
        var previousGroup = -1;
        string? previousName = null;
        foreach (var id in page.ListedSkills)
        {
            var entry = state.SkillsByName.Single(s => s.Id == id);
            var level = state.GetSkillLevel(id);
            var group = page.GroupOfSkill(id)!.Value;
            Assert.Equal(CharGenRules.GroupOf(level, entry.MinLevel), group);
            Assert.True((int)group >= previousGroup, $"{entry.Name} is out of its group's place in the list");
            if ((int)group != previousGroup)
            {
                previousName = null;
            }

            Assert.True(previousName is null || string.CompareOrdinal(previousName, entry.Name) < 0, $"{previousName} and {entry.Name} are not in name order");
            (previousGroup, previousName) = ((int)group, entry.Name);

            // What the arrows say, from the state's own prices.
            var view = page.RowView(id)!.Value;
            var stepUp = level switch
            {
                SkillAdvancementClass.Untrained => state.TrainedCost(id),
                SkillAdvancementClass.Trained => state.SpecializedCost(id) - state.TrainedCost(id),
                _ => int.MaxValue,
            };
            var canStepUp = stepUp <= state.RemainingSkillCredits;
            Assert.Equal(canStepUp ? CharGenRules.ArrowEnabledState : CharGenRules.ArrowDisabledState, view.UpState);
            if (canStepUp)
            {
                Assert.Equal(stepUp.ToString(System.Globalization.CultureInfo.InvariantCulture), view.UpCost);
            }

            var canStepDown = level switch
            {
                SkillAdvancementClass.Trained => state.TrainedCost(id) != 0,
                SkillAdvancementClass.Specialized => state.SpecializedCost(id) != 0,
                _ => false,
            };
            Assert.Equal(canStepDown ? CharGenRules.ArrowEnabledState : CharGenRules.ArrowDisabledState, view.DownState);
            Assert.Equal(state.GetSkillScore(id).ToString(System.Globalization.CultureInfo.InvariantCulture), view.Level);
        }
    }

    /// <summary>Takes every skill the page lets go back down to its floor: what is left trained is what the heritage trains for nothing.</summary>
    private static void Floor(CharGenSkillsPage page, CharGenState state)
    {
        foreach (var id in page.ListedSkills.ToArray())
        {
            while (state.GetSkillLevel(id) > SkillAdvancementClass.Untrained && page.DownArrowOf(id)!.State == CharGenRules.ArrowEnabledState)
            {
                page.DecreaseSkillLevel(page.RowOf(id)!);
            }
        }
    }

    private static CharGenSkillsPage ToSkills(LoginRig rig, CharGenMainScreen screen, uint heritageButton)
    {
        ClickAndRun(rig, Child(screen, heritageButton));
        ClickAndRun(rig, screen.TabOf(CharGenPageId.Skills)!);
        return screen.Skills!;
    }

    [RequiresLocalDatsFact]
    public void The_skills_page_lists_every_skill_in_four_groups_by_name_with_the_heritages_free_skills_trained()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToSkills(rig, screen, 0x100003BF);
        AssertSkillsConsistent(screen);
        Floor(page, screen.State);

        Assert.Equal(38, page.SkillCount);
        Assert.Equal(38, page.ListedSkills.Count());
        Assert.Equal(42, page.List!.Items.Count); // four headers
        AssertSkillsConsistent(screen);

        // Headers, in retail's order and words.
        var headers = page.List.Items.Where(i => i.InqProperty(CharGenSkillsPage.SkillIdProperty, out _) is false).ToArray();
        Assert.Equal(4, headers.Length);
        Assert.Equal(
            [Pregame("ID_CharGen_Specialized"), Pregame("ID_CharGen_Trained"), Pregame("ID_CharGen_UseableUntrained"), Pregame("ID_CharGen_UnuseableUntrained")],
            headers.Select(h => Assert.IsAssignableFrom<UiTextElement>(h.GetChildRecursive(CharGenSkillsPage.HeaderTextId)).Text));
        Assert.Equal(
            [Pregame("ID_CharGen_Specialized_Tooltip"), Pregame("ID_CharGen_Trained_Tooltip"), Pregame("ID_CharGen_UseableUntrained_Tooltip"), Pregame("ID_CharGen_UnuseableUntrained_Tooltip")],
            headers.Select(h => Assert.IsAssignableFrom<UiTextElement>(h.GetChildRecursive(CharGenSkillsPage.HeaderTextId)).TooltipText is { } t ? new UiStrings(new UiStringTables(fixture.Dats)).Render(t).Text : string.Empty));

        // Arcane Lore is trained for free by every heritage and cannot be untrained; its step up costs the heritage's 2.
        Assert.Equal(CharGenSkillGroup.Trained, page.GroupOfSkill(14));
        var arcane = page.RowView(14)!.Value;
        Assert.Equal("2", arcane.UpCost);
        Assert.Equal("0", arcane.DownCost);
        Assert.Equal(CharGenRules.ArrowDisabledState, arcane.DownState);
    }

    [RequiresLocalDatsFact]
    public void An_up_arrow_trains_then_specialises_and_the_credits_and_the_row_follow()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToSkills(rig, screen, 0x100003BF);
        var state = screen.State;
        Floor(page, state);

        AssertSkillsConsistent(screen);
        var credits = state.RemainingSkillCredits;
        var skill = state.SkillsByName.First(s => s.Name == "Healing"); // untrained 6 to train, 10 in all to specialise
        Assert.Equal(SkillAdvancementClass.Untrained, state.GetSkillLevel(skill.Id));
        Assert.Equal(CharGenSkillGroup.UnusableUntrained, page.GroupOfSkill(skill.Id)); // Healing needs training to be used

        ClickArrow(rig, page, skill.Id, up: true);

        Assert.Equal(SkillAdvancementClass.Trained, state.GetSkillLevel(skill.Id));
        Assert.Equal(credits - skill.TrainedCost, state.RemainingSkillCredits);
        Assert.Equal(CharGenSkillGroup.Trained, page.GroupOfSkill(skill.Id));
        AssertSkillsConsistent(screen);

        page.IncreaseSkillLevel(page.RowOf(skill.Id)!);

        Assert.Equal(SkillAdvancementClass.Specialized, state.GetSkillLevel(skill.Id));
        Assert.Equal(credits - skill.SpecializedCost, state.RemainingSkillCredits);
        Assert.Equal(CharGenSkillGroup.Specialized, page.GroupOfSkill(skill.Id));
        AssertSkillsConsistent(screen);

        ClickArrow(rig, page, skill.Id, up: false);
        ClickArrow(rig, page, skill.Id, up: false);
        Assert.Equal(SkillAdvancementClass.Untrained, state.GetSkillLevel(skill.Id));
        Assert.Equal(credits, state.RemainingSkillCredits);
        AssertSkillsConsistent(screen);
    }

    [RequiresLocalDatsFact]
    public void An_arrow_the_credits_cannot_pay_is_ghosted_does_nothing_and_a_step_that_exactly_fits_is_taken()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToSkills(rig, screen, 0x100003BF);
        var state = screen.State;

        // Spend everything: train skills one by one until nothing more fits.
        foreach (var id in page.ListedSkills.ToArray())
        {
            page.IncreaseSkillLevel(page.RowOf(id)!);
        }

        foreach (var id in page.ListedSkills.ToArray())
        {
            page.IncreaseSkillLevel(page.RowOf(id)!);
        }

        AssertSkillsConsistent(screen);
        var snapshot = state.SkillsByName.Select(s => state.GetSkillLevel(s.Id)).ToArray();
        var credits = state.RemainingSkillCredits;
        var untrained = state.SkillsByName.First(s => state.GetSkillLevel(s.Id) == SkillAdvancementClass.Untrained && s.TrainedCost > credits);
        Assert.Equal(CharGenRules.ArrowDisabledState, page.UpArrowOf(untrained.Id)!.State);

        rig.Click(page.UpArrowOf(untrained.Id)!); // a ghosted arrow takes no click
        rig.Run(0.1);
        page.IncreaseSkillLevel(page.RowOf(untrained.Id)!); // and the page itself refuses what the credits cannot pay

        Assert.Equal(snapshot, state.SkillsByName.Select(s => state.GetSkillLevel(s.Id)));
        Assert.Equal(credits, state.RemainingSkillCredits);
        AssertSkillsConsistent(screen);
    }

    [RequiresLocalDatsTheory]
    [InlineData(0x100003BFu)]
    [InlineData(0x100003C1u)]
    [InlineData(0x100003C2u)]
    [InlineData(0x100003C3u)]
    [InlineData(0x10000590u)]
    [InlineData(0x100005A9u)]
    [InlineData(0x100005E8u)]
    [InlineData(0x100005F1u)]
    [InlineData(0x100005C4u)]
    [InlineData(0x10000591u)]
    [InlineData(0x100005BFu)]
    public void Any_run_of_arrow_clicks_keeps_the_credits_the_rows_and_the_list_consistent_for_every_heritage_with_a_skills_page(uint heritageButton)
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToSkills(rig, screen, heritageButton);
        var random = new Random((int)heritageButton);
        var ids = page.ListedSkills.ToArray();
        AssertSkillsConsistent(screen);

        for (var step = 0; step < 160; step++)
        {
            var id = ids[random.Next(ids.Length)];
            var row = page.RowOf(id)!;
            if (random.Next(2) == 0)
            {
                page.IncreaseSkillLevel(row);
            }
            else
            {
                page.DecreaseSkillLevel(row);
            }

            AssertSkillsConsistent(screen);
        }

        // The finished character is one retail would send.
        screen.State.SetName("Skill Tester");
        Assert.True(CharGenVerifier.Verify(screen.State.GetCharGenResult(), screen.Catalog, out var problem), problem);
    }

    [RequiresLocalDatsFact]
    public void The_skill_box_describes_the_selected_skill_its_bonus_and_its_formula()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToSkills(rig, screen, 0x100003BF);
        var state = screen.State;
        Floor(page, state);
        var arcane = state.SkillsByName.Single(s => s.Id == 14); // trained for free

        page.List!.SetSelectedItem(page.RowOf(14), broadcast: true);

        Assert.Equal($"{arcane.Name} ({state.GetSkillScore(14)})\n", page.InfoTitle);
        Assert.StartsWith(arcane.Description, page.InfoBody, StringComparison.Ordinal);
        Assert.Contains("Training Bonus  +5\n", page.InfoBody, StringComparison.Ordinal);
        Assert.EndsWith(CharGenRules.FormulaText(arcane.Formula) + "\n", page.InfoBody, StringComparison.Ordinal);
        Assert.StartsWith("Formula : ", CharGenRules.FormulaText(arcane.Formula), StringComparison.Ordinal);

        var untrained = state.SkillsByName.First(s => state.GetSkillLevel(s.Id) == SkillAdvancementClass.Untrained);
        page.List.SetSelectedItem(page.RowOf(untrained.Id), broadcast: true);
        Assert.DoesNotContain("Bonus", page.InfoBody, StringComparison.Ordinal);
        Assert.Equal($"{untrained.Name} ({state.GetSkillScore(untrained.Id)})\n", page.InfoTitle);

        page.List.SetSelectedItem(page.List.Items[0], broadcast: true); // a group header is not a skill: the box empties
        Assert.Equal(string.Empty, page.InfoTitle);
        Assert.Equal(string.Empty, page.InfoBody);
    }

    [RequiresLocalDatsFact]
    public void An_arrow_click_selects_its_row_and_shows_the_new_level_in_the_box()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToSkills(rig, screen, 0x100003BF);
        var state = screen.State;
        Floor(page, state);
        var skill = state.SkillsByName.First(s => s.Name == "Lockpick");
        page.IncreaseSkillLevel(page.RowOf(skill.Id)!); // trained: its down arrow is live

        ClickArrow(rig, page, skill.Id, up: false);

        Assert.Same(page.RowOf(skill.Id), page.List!.SelectedItem);
        Assert.Equal($"{skill.Name} ({state.GetSkillScore(skill.Id)})\n", page.InfoTitle);
    }

    // ---------------------------------------------------------------- Random, and what is not built

    [RequiresLocalDatsFact]
    public void Random_draws_the_current_page_again_and_on_the_summary_page_asks_first()
    {
        using var rig = Rig(seed: 3);
        var screen = Open(rig);
        var state = screen.State;

        var seen = new HashSet<uint>();
        for (var i = 0; i < 12; i++)
        {
            ClickAndRun(rig, Child(screen, CharGenMainScreen.RandomButtonId));
            seen.Add(state.Heritage);
            Assert.InRange(state.Heritage, 1u, 4u); // the random heritage is one of the four that Throne of Destiny allows
            Assert.Equal(CharGenRules.SelectedState, screen.Heritage!.ButtonState(state.Heritage));
        }

        Assert.True(seen.Count > 1, "twelve random heritages were all the same");

        ClickAndRun(rig, screen.TabOf(CharGenPageId.Profession)!);
        var templates = new HashSet<int>();
        for (var i = 0; i < 12; i++)
        {
            ClickAndRun(rig, Child(screen, CharGenMainScreen.RandomButtonId));
            templates.Add(state.Template);
            AssertAttributesShown(screen);
        }

        Assert.True(templates.Count > 1);

        ClickAndRun(rig, screen.TabOf(CharGenPageId.Skills)!);
        for (var i = 0; i < 8; i++)
        {
            ClickAndRun(rig, Child(screen, CharGenMainScreen.RandomButtonId));
            AssertSkillsConsistent(screen);
        }

        ClickAndRun(rig, screen.TabOf(CharGenPageId.Town)!);
        ClickAndRun(rig, Child(screen, CharGenMainScreen.RandomButtonId));
        Assert.InRange(state.StartArea, 0, 3);

        ClickAndRun(rig, screen.TabOf(CharGenPageId.Summary)!);
        var before = (state.Heritage, state.Strength, state.HairStyle, state.StartArea, state.Gender);
        ClickAndRun(rig, Child(screen, CharGenMainScreen.RandomButtonId));
        Assert.NotEqual(0u, screen.RandomizeDialog);
        Assert.Equal(before, (state.Heritage, state.Strength, state.HairStyle, state.StartArea, state.Gender)); // nothing changed yet
        var no = Assert.IsAssignableFrom<UiElement>(rig.Dialog(screen.RandomizeDialog)!.GetChildRecursive(UiConfirmationDialog.NoButton));
        ClickAndRun(rig, no);
        Assert.Equal(before, (state.Heritage, state.Strength, state.HairStyle, state.StartArea, state.Gender));

        var changed = false;
        for (var i = 0; i < 6 && !changed; i++)
        {
            ClickAndRun(rig, Child(screen, CharGenMainScreen.RandomButtonId));
            var yes = Assert.IsAssignableFrom<UiElement>(rig.Dialog(screen.RandomizeDialog)!.GetChildRecursive(UiConfirmationDialog.YesButton));
            ClickAndRun(rig, yes);
            changed = before != (state.Heritage, state.Strength, state.HairStyle, state.StartArea, state.Gender);
        }

        Assert.True(changed, "the whole character never changed after Yes");
        Assert.Equal(0u, screen.RandomizeDialog);
    }

    [RequiresLocalDatsFact]
    public void Without_the_3D_view_data_the_appearance_and_summary_pages_say_so_on_screen_and_log_a_todo_and_the_others_do_not()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var notes = new List<string?>();
        screen.PlaceholderNoteChanged += () => notes.Add(screen.PlaceholderNote);

        Assert.True(LoginScreens.IsStandIn(screen, UiModes.CharGen) is false);
        foreach (var page in new[] { CharGenPageId.Profession, CharGenPageId.Skills, CharGenPageId.Town })
        {
            ClickAndRun(rig, screen.TabOf(page)!);
            Assert.Null(screen.PlaceholderNote);
            Assert.Null(screen.Page(page)!.PlaceholderText);
        }

        foreach (var page in new[] { CharGenPageId.Appearance, CharGenPageId.Summary })
        {
            ClickAndRun(rig, screen.TabOf(page)!);
            Assert.StartsWith("placeholder:", screen.PlaceholderNote, StringComparison.Ordinal);
            Assert.Contains("3D character", screen.PlaceholderNote!, StringComparison.Ordinal);
        }

        Assert.Equal(5, notes.Count);
        Assert.Equal(3, notes.Count(n => n is null));
        Assert.True(rig.Log.AnyLineContains("TODO(retailui-chargen-view)"));
    }

    [RequiresLocalDatsFact]
    public void Without_CharGen_data_the_mode_shows_the_stand_in_and_says_so()
    {
        using var rig = new LoginRig(fixture, startMode: UiModes.CharGen, charGen: false);
        rig.Run(0.3);

        Assert.NotNull(rig.Flow.CurrentScreen);
        Assert.IsNotType<CharGenMainScreen>(rig.Flow.CurrentScreen);
        Assert.True(LoginScreens.IsStandIn(rig.Flow.CurrentScreen, UiModes.CharGen));
        Assert.True(rig.Log.AnyLineContains("shows a stand-in"));
    }

    [RequiresLocalDatsFact]
    public void The_screen_makes_the_same_random_character_for_the_same_seed_and_a_different_one_for_another()
    {
        (uint Heritage, int Strength) Make(int seed)
        {
            using var rig = Rig(seed: seed);
            var screen = Open(rig);
            return (screen.State.Heritage, screen.State.Strength);
        }

        Assert.Equal(Make(5), Make(5));
        Assert.True(Enumerable.Range(1, 8).Select(Make).Distinct().Count() > 1, "eight seeds made the same character");
    }

    [RequiresLocalDatsFact]
    public void Opening_the_screen_logs_nothing_worse_than_the_known_todos()
    {
        using var rig = Rig();
        var screen = Open(rig);
        foreach (var page in Enum.GetValues<CharGenPageId>())
        {
            ClickAndRun(rig, screen.TabOf(page)!);
        }

        var problems = rig.Log.Entries
            .Where(e => e.Severity >= LogSeverity.Error)
            .Select(e => e.Line)
            .ToArray();
        Assert.Empty(problems);
    }
}
