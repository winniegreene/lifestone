using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using NetCharGenResult = Lifestone.Net.Messages.CharGenResult;

namespace Lifestone.Client.Tests.RetailUi.Screens;

/// <summary>
/// The start town page, the summary page with its name field, and Finish and the create (m2-plan G3 U5, second stage), on the real screens over the user's
/// dats: the towns and their texts, the list the summary builds, what the name field takes, the credit and name warnings, the create's intent, the
/// answers (name reserved, banned, admin denied, database down) and the return to character management with the new character selected.
/// </summary>
public sealed class CharGenSummaryTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private string Pregame(string token) => new UiStrings(new UiStringTables(fixture.Dats)).Render(new UiStringRef(StringHash.Tokenize(token), GmUiFlow.PregameTableId)).Text;

    private LoginRig Rig(bool view3D = false, int seed = 7, UiRetailGlitches glitches = UiRetailGlitches.None, bool throneOfDestiny = true, RecordingViewHost? host = null) =>
        new(fixture, startMode: UiModes.CharGen, seed: seed, glitches: glitches, throneOfDestiny: throneOfDestiny, view3D: view3D, viewHost: host);

    private static CharGenMainScreen Open(LoginRig rig)
    {
        rig.Run(0.3);
        return Assert.IsType<CharGenMainScreen>(rig.Flow.CurrentScreen);
    }

    /// <summary>Character creation as the player reaches it: the data check ends, the list shows, and Create is clicked (the session is then at its character list).</summary>
    private static CharGenMainScreen OpenViaCreate(LoginRig rig)
    {
        var management = rig.ToCharacterManagement();
        ClickAndRun(rig, management.CreateButton!);
        rig.Run(0.5);
        return Assert.IsType<CharGenMainScreen>(rig.Flow.CurrentScreen);
    }

    private static void ClickAndRun(LoginRig rig, UiElement element)
    {
        Assert.True(rig.Click(element), $"the click on element 0x{element.ElementId:X8} reached nothing");
        rig.Run(0.1);
    }

    private static void Go(LoginRig rig, CharGenMainScreen screen, CharGenPageId page) => ClickAndRun(rig, screen.TabOf(page)!);

    /// <summary>Types text into the name field the way the keyboard does (the field must have the keyboard).</summary>
    private static void Type(LoginRig rig, string text)
    {
        foreach (var c in text)
        {
            rig.Manager.Input.Character(c);
        }

        rig.Run(0.05);
    }

    private InboundContext Context(LoginRig rig) => new(0, MessageQueue.Login, rig.Time.GetTimestamp());

    // ---------------------------------------------------------------- The layout and the strings

    [RequiresLocalDatsFact]
    public void Every_element_the_new_pages_use_exists_with_the_class_they_expect()
    {
        using var rig = Rig();
        var screen = Open(rig);
        UiElement Child(uint id) => Assert.IsAssignableFrom<UiElement>(screen.Root!.GetChildRecursive(id));

        foreach (var id in new[] { CharGenAppearancePage.FemaleButtonId, CharGenAppearancePage.MaleButtonId, CharGenAppearancePage.FaceButtonId, CharGenAppearancePage.ClothesButtonId, CharGenAppearancePage.ZoomInId, CharGenAppearancePage.ZoomOutId, CharGenAppearancePage.RotateClockId, CharGenAppearancePage.RotateCounterClockId })
        {
            Assert.IsAssignableFrom<UiButton>(Child(id));
        }

        foreach (var part in Enum.GetValues<CharGenAppearancePart>())
        {
            var spinner = Assert.IsAssignableFrom<UiButton>(screen.Appearance!.Spinner(part));
            Assert.IsAssignableFrom<UiButton>(spinner.GetChildRecursive(CharGenAppearancePage.LeftArrowId));
            Assert.IsAssignableFrom<UiButton>(spinner.GetChildRecursive(CharGenAppearancePage.RightArrowId));
        }

        for (var i = 0; i < CharGenAppearancePage.SpotCount; i++)
        {
            Assert.IsAssignableFrom<UiButton>(Child(CharGenAppearancePage.FirstBulletId + (uint)i));
            Assert.IsAssignableFrom<UiElement>(Child(CharGenAppearancePage.FirstPointerId + (uint)i));
        }

        Assert.IsType<UiScrollbar>(Child(CharGenAppearancePage.ShadeScrollId));
        Assert.IsAssignableFrom<UiElement>(Child(CharGenAppearancePage.GradCircleId));
        Assert.IsType<UiViewport>(Child(CharGenMainScreen.AppearanceViewportId));
        Assert.IsType<UiViewport>(Child(CharGenMainScreen.SummaryViewportId));
        Assert.IsAssignableFrom<UiTextElement>(Child(CharGenTownPage.TextId));
        Assert.All(CharGenRules.Towns, t => Assert.IsAssignableFrom<UiButton>(Child(t.ButtonId)));
        Assert.All(CharGenRules.Towns, t => Assert.Contains(t.PageState, Child(0x100003D5).Desc.States.Keys));
        Assert.IsType<UiListBox>(Child(CharGenSummaryPage.ListId));
        Assert.IsAssignableFrom<UiTextElement>(Child(CharGenSummaryPage.NameId));
        Assert.IsAssignableFrom<UiTextElement>(Child(CharGenSummaryPage.HowToId));
        var list = (UiListBox)Child(CharGenSummaryPage.ListId);
        Assert.IsAssignableFrom<UiTextElement>(list.AddItemFromTemplateList(CharGenSummaryPage.LineTemplate)!.GetChildRecursive(CharGenSummaryPage.LineTextId));
        Assert.IsAssignableFrom<UiTextElement>(list.AddItemFromTemplateList(CharGenSummaryPage.HeaderTemplate)!.GetChildRecursive(CharGenSummaryPage.HeaderTextId));
        var pair = list.AddItemFromTemplateList(CharGenSummaryPage.PairTemplate)!;
        Assert.IsAssignableFrom<UiTextElement>(pair.GetChildRecursive(CharGenSummaryPage.PairNameId));
        Assert.IsAssignableFrom<UiTextElement>(pair.GetChildRecursive(CharGenSummaryPage.PairValueId));
        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("ERROR", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void Every_string_the_new_pages_and_the_create_say_exists_in_the_pregame_table()
    {
        var tokens = new List<string>
        {
            "ID_CharGen_HairStyle", "ID_CharGen_Eyes", "ID_CharGen_Skin",
            "ID_CharGen_GearText_HairButton", "ID_CharGen_GearText_EyesButton", "ID_CharGen_GearText_SkinButton",
            "ID_CharGen_OlthoiText_HairButton", "ID_CharGen_OlthoiText_EyesButton", "ID_CharGen_OlthoiText_SkinButton",
            "ID_CharGen_TownHowTo", "ID_CharGen_NamePrompt", "ID_CharGen_NameTooLong", "ID_CharGen_SummaryHowTo", "ID_CharGen_SummaryHowToEnd",
            "ID_CharGen_CreditWarning", "ID_CharGen_NoNameWarning", "ID_Character_Err_NameReserved", "ID_Character_Err_NameBanned",
            "ID_Character_Err_NameAdminDenied", "ID_Character_Err_NameDBDown",
        };
        tokens.AddRange(CharGenRules.Towns.Select(t => t.TextToken));
        foreach (var heritage in Enumerable.Range(1, 13))
        {
            foreach (var gender in new[] { 1u, 2u })
            {
                tokens.Add(CharGenRules.NamesToken((uint)heritage, gender) ?? throw new InvalidOperationException($"no sample names for heritage {heritage}"));
            }
        }

        foreach (var token in tokens)
        {
            Assert.False(string.IsNullOrWhiteSpace(Pregame(token)), $"the pregame string {token} is missing or empty");
        }
    }

    [Theory]
    [InlineData(1u, 1u, "ID_CharGen_AluMaleNames")]
    [InlineData(1u, 2u, "ID_CharGen_AluFemaleNames")]
    [InlineData(2u, 1u, "ID_CharGen_GharuMaleNames")]
    [InlineData(5u, 2u, "ID_CharGen_ShadFemaleNames")]
    [InlineData(10u, 1u, "ID_CharGen_ShadMaleNames")]
    [InlineData(7u, 1u, "ID_CharGen_AunTMaleNames")]
    [InlineData(12u, 2u, "ID_CharGen_OlthoiFemaleNames")]
    [InlineData(13u, 1u, "ID_CharGen_OlthoiMaleNames")]
    public void The_sample_names_follow_the_heritage_and_gender(uint heritage, uint gender, string token) =>
        Assert.Equal(token, CharGenRules.NamesToken(heritage, gender));

    [Fact]
    public void The_towns_are_retails_four_and_their_start_areas_follow_the_chargen_data()
    {
        Assert.Equal([1, 2, 3, 4], CharGenRules.Towns.Select(t => t.Town));
        Assert.Equal([0, 1, 2, 3], CharGenRules.Towns.Select(t => t.StartArea));
        Assert.Equal(4, CharGenRules.Towns.Select(t => t.ButtonId).Distinct().Count());
        Assert.Equal(4, CharGenRules.Towns.Select(t => t.PageState).Distinct().Count());
        Assert.Equal(0x10000035u, CharGenRules.TownOfStartArea(3)!.PageState); // Sanamar, as ListenToElementMessage sets it
        Assert.Equal(0x10000037u, CharGenRules.TownOfStartArea(1)!.PageState); // Shoushi, as SetTown sets it
        Assert.Null(CharGenRules.TownOfStartArea(4)); // the Olthoi lair has no town
    }

    [RequiresLocalDatsFact]
    public void The_start_areas_of_the_chargen_data_are_the_towns_in_retails_order()
    {
        var data = CharGenCatalog.Load(fixture.Dats).Data;

        Assert.Equal(["Holtburg", "Shoushi", "Yaraq", "Sanamar"], data.StartAreas.Take(4).Select(a => a.Name));
    }

    // ---------------------------------------------------------------- The town page

    [RequiresLocalDatsFact]
    public void The_town_page_shows_the_start_area_of_the_state_and_a_click_picks_a_town()
    {
        using var rig = Rig();
        var screen = Open(rig);
        screen.State.SetStartArea(2);
        Go(rig, screen, CharGenPageId.Town);
        var page = screen.Town!;

        Assert.Equal(UiStateIds.Highlight, page.ButtonState(3));
        Assert.Equal(UiStateIds.Normal, page.ButtonState(1));
        Assert.Equal(CharGenRules.TownOfStartArea(2)!.PageState, page.PageState);
        Assert.StartsWith(Pregame("ID_CharGen_YaraqText"), page.Text, StringComparison.Ordinal);
        Assert.EndsWith(Pregame("ID_CharGen_TownHowTo") + "\n", page.Text, StringComparison.Ordinal);

        foreach (var town in CharGenRules.Towns.Where(t => t.Town != 4))
        {
            ClickAndRun(rig, rig.Element<UiButton>(town.ButtonId));
            Assert.Equal(town.StartArea, screen.State.StartArea);
            Assert.Equal(UiStateIds.Highlight, page.ButtonState(town.Town));
            Assert.Equal(1, CharGenRules.Towns.Count(t => page.ButtonState(t.Town) == UiStateIds.Highlight));
            Assert.Equal(town.PageState, page.PageState);
            Assert.StartsWith(Pregame(town.TextToken), page.Text, StringComparison.Ordinal);
        }

        // The town's name is the page's heading (the page's state shows it): each state's heading is the town it was meant for.
        foreach (var town in CharGenRules.Towns)
        {
            ClickAndRun(rig, rig.Element<UiButton>(town.ButtonId));
            var heading = Assert.IsAssignableFrom<UiTextElement>(rig.Element<UiElement>(0x100003D5).GetChildRecursive(0x10000408));
            Assert.Equal(CharGenCatalog.Load(fixture.Dats).Data.StartAreas[town.StartArea].Name, heading.Text.Trim());
        }
    }

    [RequiresLocalDatsFact]
    public void Sanamar_needs_the_Throne_of_Destiny_and_says_so_and_keeps_the_old_town()
    {
        using var rig = Rig(throneOfDestiny: false);
        var screen = Open(rig);
        screen.State.SetStartArea(0);
        Go(rig, screen, CharGenPageId.Town);

        ClickAndRun(rig, rig.Element<UiButton>(0x1000040B));

        Assert.NotEqual(0u, screen.ThroneDialog);
        Assert.Equal(0, screen.State.StartArea);
        Assert.Equal(UiStateIds.Highlight, screen.Town!.ButtonState(1));
        Assert.Equal(UiStateIds.Normal, screen.Town.ButtonState(4));
    }

    [RequiresLocalDatsFact]
    public void Random_on_the_town_page_picks_one_of_the_towns_the_account_has()
    {
        using var rig = Rig(throneOfDestiny: false);
        var screen = Open(rig);
        Go(rig, screen, CharGenPageId.Town);
        var seen = new HashSet<int>();

        for (var i = 0; i < 40; i++)
        {
            ClickAndRun(rig, rig.Element<UiButton>(CharGenMainScreen.RandomButtonId));
            seen.Add(screen.State.StartArea);
            Assert.InRange(screen.State.StartArea, 0, 2); // three towns without the Throne of Destiny
            Assert.Equal(UiStateIds.Highlight, screen.Town!.ButtonState(screen.State.StartArea + 1));
        }

        Assert.True(seen.Count >= 2);
    }

    // ---------------------------------------------------------------- The summary page

    [RequiresLocalDatsFact]
    public void The_summary_lists_what_the_state_holds_in_retails_order()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var state = screen.State;
        Go(rig, screen, CharGenPageId.Summary);
        var rows = screen.Summary!.Rows;
        var data = CharGenCatalog.Load(fixture.Dats).Data;

        Assert.StartsWith("Profession: ", rows[0], StringComparison.Ordinal);
        Assert.Equal("Profession: " + CharGenSummaryPage.ProfessionName(state.Template, data.GetHeritage(state.Heritage)), rows[0]);
        Assert.Equal("Gender: " + (state.Gender == 1 ? "Male" : "Female"), rows[1]);
        Assert.Equal("Heritage: " + data.GetHeritage(state.Heritage).Name, rows[2]);
        Assert.Equal("Starting Town: " + data.StartAreas[state.StartArea].Name, rows[3]);
        Assert.Equal("Attributes", rows[4]);
        Assert.Equal(
            [
                $"Strength: {state.Strength}", $"Endurance: {state.Endurance}", $"Coordination: {state.Coordination}", $"Quickness: {state.Quickness}", $"Focus: {state.Focus}", $"Self: {state.Self}",
                $"Health: {CharGenRules.HealthOf(state.Endurance)}", $"Stamina: {state.Endurance}", $"Mana: {state.Self}", $"Skill Credits: {state.RemainingSkillCredits}",
            ],
            rows.Skip(5).Take(10));
        var headings = rows.Where(r => r is "Specialized Skills" or "Trained Skills" or "Useable Untrained Skills" or "Unuseable Untrained Skills").ToList();
        Assert.Equal(["Specialized Skills", "Trained Skills", "Useable Untrained Skills", "Unuseable Untrained Skills"], headings);

        // Every skill the state has appears once with its score, under the heading of its class.
        var skills = rows.Skip(15).ToList();
        foreach (var skill in state.SkillsByName)
        {
            var level = state.GetSkillLevel(skill.Id);
            var row = $"{skill.Name}: {state.GetSkillScore(skill.Id)}";
            Assert.Equal(1, skills.Count(r => r == row));
            var heading = level switch
            {
                SkillAdvancementClass.Specialized => "Specialized Skills",
                SkillAdvancementClass.Trained => "Trained Skills",
                _ => skill.MinLevel > 1 ? "Unuseable Untrained Skills" : "Useable Untrained Skills",
            };
            var at = skills.IndexOf(row);
            var from = skills.IndexOf(heading);
            var next = skills.FindIndex(from + 1, r => r.EndsWith("Skills", StringComparison.Ordinal) && !r.Contains(':', StringComparison.Ordinal));
            Assert.True(from < at && (next < 0 || at < next), $"{row} comes under {heading}");
        }
    }

    [RequiresLocalDatsFact]
    public void The_how_to_text_holds_the_sample_names_of_the_heritage_and_gender()
    {
        using var rig = Rig();
        var screen = Open(rig);
        Go(rig, screen, CharGenPageId.Summary);
        var names = Pregame(CharGenRules.NamesToken(screen.State.Heritage, screen.State.Gender)!);

        Assert.Contains(names, screen.Summary!.HowToText, StringComparison.Ordinal);
        Assert.StartsWith(Pregame("ID_CharGen_SummaryHowTo"), screen.Summary.HowToText, StringComparison.Ordinal);
        Assert.EndsWith(Pregame("ID_CharGen_SummaryHowToEnd"), screen.Summary.HowToText, StringComparison.Ordinal);
    }

    [RequiresLocalDatsFact]
    public void Retails_summary_names_only_the_first_four_heritages_and_Lifestone_names_them_all()
    {
        using var fixed_ = Rig();
        var screen = Open(fixed_);
        screen.State.SetHeritageGroup(7);
        Go(fixed_, screen, CharGenPageId.Summary);
        Assert.Equal("Heritage: Tumerok", screen.Summary!.Rows[2]);

        using var retail = Rig(glitches: UiRetailGlitches.CharGenSummaryHeritage);
        var retailScreen = Open(retail);
        retailScreen.State.SetHeritageGroup(7);
        Go(retail, retailScreen, CharGenPageId.Summary);
        Assert.Equal("Heritage: ", retailScreen.Summary!.Rows[2]);
        retailScreen.State.SetHeritageGroup(3);
        retailScreen.Summary.Update();
        Assert.Equal("Heritage: Sho", retailScreen.Summary.Rows[2]);
    }

    [RequiresLocalDatsFact]
    public void The_summary_logs_the_names_it_takes_from_the_data_instead_of_the_exe()
    {
        using var rig = Rig();
        var screen = Open(rig);
        Go(rig, screen, CharGenPageId.Summary);

        Assert.True(rig.Log.AnyLineContains("TODO(retailui-chargen-summary-data)"));
    }

    [RequiresLocalDatsFact]
    public void The_profession_names_of_a_template_agree_across_the_non_Olthoi_heritages_and_the_custom_one_is_retails()
    {
        // The summary takes the profession's name from the heritage's own template; retail has one table for all. Pin that the data's words agree.
        var data = CharGenCatalog.Load(fixture.Dats).Data;
        var reference = data.GetHeritage(1).Templates.Select(t => t.Name).ToArray();
        foreach (var heritage in data.Heritages.Where(h => !CharGenRules.IsOlthoi(h.Id)))
        {
            Assert.Equal(reference, heritage.Templates.Select(t => t.Name));
            Assert.Equal("Custom", CharGenSummaryPage.ProfessionName(0, heritage));
            for (var template = 1; template <= 6; template++)
            {
                Assert.Equal(heritage.Templates[template].Name, CharGenSummaryPage.ProfessionName(template, heritage));
            }

            Assert.Equal(string.Empty, CharGenSummaryPage.ProfessionName(-1, heritage));
            Assert.Equal(string.Empty, CharGenSummaryPage.ProfessionName(7, heritage));
        }
    }

    // ---------------------------------------------------------------- The name field

    [RequiresLocalDatsFact]
    public void The_name_field_has_the_keyboard_and_the_prompt_selected_until_a_name_is_typed()
    {
        using var rig = Rig();
        var screen = Open(rig);
        Go(rig, screen, CharGenPageId.Summary);
        var field = screen.Summary!.NameField!;

        Assert.True(field.HasFocus);
        Assert.Equal(Pregame("ID_CharGen_NamePrompt"), field.Text);
        Assert.Equal(field.Text, field.SelectedText);
        Assert.False(screen.Summary.NameEntered);

        Type(rig, "b");

        Assert.Equal("b", field.Text); // the typed letter replaced the selected prompt
        Assert.True(screen.Summary.NameEntered);
        Assert.Equal("B", screen.State.Name); // the state holds the formatted name
    }

    [RequiresLocalDatsTheory]
    [InlineData("bob")]
    [InlineData("o'brien")]
    [InlineData("mary-jane smith")]
    [InlineData("MACDONALD")]
    [InlineData("McDonald")]
    [InlineData("b0b!_x.y")]
    [InlineData("émile")]
    public void What_is_typed_goes_through_the_name_filter_and_the_state_formats_what_the_field_holds(string typed)
    {
        using var rig = Rig();
        var screen = Open(rig);
        Go(rig, screen, CharGenPageId.Summary);

        Type(rig, typed);

        var kept = new string([.. typed.Where(TextFilters.Name)]);
        Assert.Equal(kept, screen.Summary!.NameField!.Text);
        Assert.Equal(CharGenNames.Format(kept), screen.State.Name);
    }

    [Theory]
    [InlineData('a', true)]
    [InlineData('Z', true)]
    [InlineData('\'', true)]
    [InlineData('-', true)]
    [InlineData(' ', true)]
    [InlineData('1', false)]
    [InlineData('_', false)]
    [InlineData('.', false)]
    [InlineData('é', false)]
    public void The_name_filter_takes_ascii_letters_apostrophes_hyphens_and_spaces(char c, bool taken) => Assert.Equal(taken, TextFilters.Name(c));

    [RequiresLocalDatsFact]
    public void A_name_of_more_than_32_characters_is_taken_back_and_a_message_says_so()
    {
        using var rig = Rig();
        var screen = Open(rig);
        Go(rig, screen, CharGenPageId.Summary);
        var field = screen.Summary!.NameField!;
        Type(rig, "Aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"); // 32
        Assert.Equal(32, field.Text.Length);
        var stored = screen.State.Name;
        Assert.Equal(0u, screen.Summary.NameLimitDialog);

        Type(rig, "a"); // the 33rd

        Assert.NotEqual(0u, screen.Summary.NameLimitDialog);
        Assert.Equal(stored, field.Text);
        Assert.Equal(stored, screen.State.Name);
        Assert.Equal(Pregame("ID_CharGen_NameTooLong"), rig.DialogText(screen.Summary.NameLimitDialog));
    }

    // ---------------------------------------------------------------- Finish and the create

    [RequiresLocalDatsFact]
    public void Finish_with_no_name_typed_says_so_and_sends_nothing()
    {
        using var rig = new LoginRig(fixture);
        var screen = OpenViaCreate(rig);
        Go(rig, screen, CharGenPageId.Summary);

        ClickAndRun(rig, screen.FinishButton!);

        Assert.NotEqual(0u, screen.NoNameDialog);
        Assert.Equal(Pregame("ID_CharGen_NoNameWarning"), rig.DialogText(screen.NoNameDialog));
        Assert.False(screen.IsCreatePending);
        Assert.Equal(0, rig.Characters.Entries.Count(e => e.Name == "Bob"));
    }

    [RequiresLocalDatsFact]
    public void Finish_with_only_a_trimmed_away_name_is_the_same_as_no_name()
    {
        using var rig = new LoginRig(fixture);
        var screen = OpenViaCreate(rig);
        Go(rig, screen, CharGenPageId.Summary);
        Type(rig, " ");

        ClickAndRun(rig, screen.FinishButton!);

        Assert.NotEqual(0u, screen.NoNameDialog);
        Assert.False(screen.IsCreatePending);
    }

    [RequiresLocalDatsFact]
    public void A_name_emptied_from_the_field_is_no_name_and_Finish_says_so()
    {
        using var rig = new LoginRig(fixture);
        var screen = OpenViaCreate(rig);
        Go(rig, screen, CharGenPageId.Summary);
        Type(rig, "Bob");
        Assert.Equal("Bob", screen.State.Name);

        var field = screen.Summary!.NameField!;
        field.DeleteBackward();
        field.DeleteBackward();
        Assert.Equal("B", screen.State.Name);
        field.DeleteBackward();
        Assert.Equal(string.Empty, screen.State.Name);
        ClickAndRun(rig, screen.FinishButton!);

        Assert.NotEqual(0u, screen.NoNameDialog);
        Assert.False(screen.IsCreatePending);
        Assert.DoesNotContain(rig.Characters.Entries, e => e.Name == "Bob");
    }

    [RequiresLocalDatsFact]
    public void A_name_deleted_by_select_all_is_no_name_too()
    {
        using var rig = new LoginRig(fixture);
        var screen = OpenViaCreate(rig);
        Go(rig, screen, CharGenPageId.Summary);
        Type(rig, "Bob");

        screen.Summary!.NameField!.SelectAll();
        screen.Summary.NameField.DeleteSelection();
        ClickAndRun(rig, screen.FinishButton!);

        Assert.Equal(string.Empty, screen.State.Name);
        Assert.NotEqual(0u, screen.NoNameDialog);
        Assert.False(screen.IsCreatePending);
    }

    [RequiresLocalDatsFact]
    public void Retail_keeps_the_last_non_empty_name_and_creates_the_character_under_it()
    {
        using var rig = new LoginRig(fixture, glitches: UiRetailGlitches.CharGenEmptyName);
        var screen = OpenViaCreate(rig);
        Go(rig, screen, CharGenPageId.Summary);
        Type(rig, "Bob");

        screen.Summary!.NameField!.SelectAll();
        screen.Summary.NameField.DeleteSelection();
        ClickAndRun(rig, screen.FinishButton!);

        Assert.Equal("Bob", screen.State.Name);
        Assert.Equal(0u, screen.NoNameDialog);
        Assert.True(screen.IsCreatePending);
    }

    [RequiresLocalDatsFact]
    public void Finish_with_attribute_credits_left_asks_first_and_yes_sends_the_create()
    {
        using var rig = new LoginRig(fixture);
        var screen = OpenViaCreate(rig);
        Go(rig, screen, CharGenPageId.Summary);
        screen.State.SetStrength(screen.State.Strength - 20, false); // credits left over
        Assert.True(screen.State.RemainingAttributeCredits > 0);
        Type(rig, "Credit Test");

        ClickAndRun(rig, screen.FinishButton!);

        Assert.NotEqual(0u, screen.CreditDialog);
        Assert.Equal(Pregame("ID_CharGen_CreditWarning"), rig.DialogText(screen.CreditDialog));
        Assert.False(screen.IsCreatePending);

        var dialog = rig.Dialog(screen.CreditDialog)!;
        ClickAndRun(rig, dialog.GetChildRecursive(UiConfirmationDialog.YesButton)!);

        Assert.Equal(0u, screen.CreditDialog);
        Assert.True(screen.IsCreatePending);
    }

    [RequiresLocalDatsFact]
    public void Finish_with_attribute_credits_left_and_no_keeps_the_page()
    {
        using var rig = new LoginRig(fixture);
        var screen = OpenViaCreate(rig);
        Go(rig, screen, CharGenPageId.Summary);
        screen.State.SetStrength(screen.State.Strength - 20, false);
        Type(rig, "Credit Test");
        ClickAndRun(rig, screen.FinishButton!);

        ClickAndRun(rig, rig.Dialog(screen.CreditDialog)!.GetChildRecursive(UiConfirmationDialog.NoButton)!);

        Assert.Equal(0u, screen.CreditDialog);
        Assert.False(screen.IsCreatePending);
        Assert.Equal(CharGenPageId.Summary, screen.Progress);
    }

    [RequiresLocalDatsFact]
    public void A_successful_create_logs_the_new_character_in_at_once_and_the_offline_stand_in_says_it_has_no_server()
    {
        using var rig = new LoginRig(fixture);
        var screen = OpenViaCreate(rig);
        Go(rig, screen, CharGenPageId.Summary);
        Type(rig, "New Hero");
        var listed = rig.Characters.Entries.Count;

        ClickAndRun(rig, screen.FinishButton!);
        Assert.True(screen.IsCreatePending);
        Assert.True(rig.RunUntilMode(UiModes.CharacterManagement, 5));
        rig.Run(0.3);

        var management = Assert.IsType<CharacterManagementScreen>(rig.Flow.CurrentScreen);
        Assert.Equal(listed + 1, rig.Characters.Entries.Count);
        var created = rig.Characters.Entries.Single(e => e.Name == "New Hero");
        Assert.Equal(created.Guid, rig.Characters.SelectedGuid);
        Assert.Equal(created.Guid, management.SelectedGuid);
        Assert.Equal("New Hero", ((UiTextElement)management.List!.SelectedItem!).Text);

        // CPlayerSystem::LogOnCharacter: the Enter action went out for the new character at once, from the UI, the way the Enter button's does.
        var enter = Assert.Single(Enumerable.Range(0, rig.Session.ActionLog.Count).Select(i => rig.Session.ActionLog[i]), a => a.Kind == ActionKind.EnterWorld);
        Assert.Equal(ActionSource.Ui, enter.Source);
        Assert.Contains(created.Guid.ToString("X8"), enter.Summary, StringComparison.Ordinal);

        // The offline stand-in has no server to enter: character management says so in Lifestone's words (a placeholder), as it does for its Enter button.
        management = Assert.IsType<CharacterManagementScreen>(rig.Flow.CurrentScreen);
        Assert.True(rig.RunUntil(() => management.ErrorDialog != 0, 3));
        Assert.StartsWith("Not in Lifestone M2: entering the world without a server", rig.DialogText(management.ErrorDialog), StringComparison.Ordinal);
        Assert.True(rig.Log.AnyLineContains("placeholder: Not in Lifestone M2: entering the world"));
        Assert.False(rig.Log.AnyLineContains("TODO(retailui-chargen-enter)"));
        Assert.True(rig.Log.AnyLineContains("TODO(retailui-chargen-11-6096)"));
        Assert.True(rig.Log.AnyLineContains("TODO(retailui-chargen-enter-dialog)"));
    }

    [RequiresLocalDatsFact]
    public void A_taken_name_says_so_in_retails_words_keeps_the_screen_and_can_be_changed_and_sent_again()
    {
        using var rig = new LoginRig(fixture);
        var screen = OpenViaCreate(rig);
        Go(rig, screen, CharGenPageId.Summary);
        Type(rig, "Stand-in alpha");

        ClickAndRun(rig, screen.FinishButton!);
        Assert.True(rig.RunUntil(() => screen.ErrorDialog != 0, 3));

        Assert.False(screen.IsCreatePending);
        Assert.Equal(Pregame("ID_Character_Err_NameReserved"), rig.DialogText(screen.ErrorDialog));
        Assert.IsType<CharGenMainScreen>(rig.Flow.CurrentScreen);
        Assert.Equal(CharGenPageId.Summary, screen.Progress);

        ClickAndRun(rig, rig.Dialog(screen.ErrorDialog)!.GetChildRecursive(UiMessageDialog.OkButton)!);
        Assert.Equal(0u, screen.ErrorDialog);

        // The Finish button has the keyboard (it took it with the click, as any focusable button does), and the window is active again behind the closed
        // dialog (retail activates the next window when a dialog goes), so a click on the name field gives it the keyboard back and a new name can be sent.
        Assert.True(screen.Root!.IsActivated);
        ClickAndRun(rig, screen.Summary!.NameField!);
        Assert.True(screen.Summary!.NameField!.HasFocus);
        screen.Summary.NameField.SelectAll();
        Type(rig, "Someone Else");
        ClickAndRun(rig, screen.FinishButton!);
        Assert.True(rig.RunUntilMode(UiModes.CharacterManagement, 5));
        Assert.Contains(rig.Characters.Entries, e => e.Name == "Someone Else");
    }

    [RequiresLocalDatsTheory]
    [InlineData(NetCharGenResult.NameBanned, "ID_Character_Err_NameBanned")]
    [InlineData(NetCharGenResult.AdminPrivilegeDenied, "ID_Character_Err_NameAdminDenied")]
    [InlineData(NetCharGenResult.DatabaseDown, "ID_Character_Err_NameDBDown")]
    [InlineData(NetCharGenResult.Corrupt, "ID_Character_Err_NameDBDown")]
    [InlineData(NetCharGenResult.Pending, "ID_Character_Err_NameDBDown")]
    [InlineData(NetCharGenResult.NameInUse, "ID_Character_Err_NameReserved")]
    public void The_server_s_answers_are_said_as_retail_says_them(NetCharGenResult result, string token)
    {
        using var rig = new LoginRig(fixture);
        var screen = OpenViaCreate(rig);
        Go(rig, screen, CharGenPageId.Summary);
        Type(rig, "answer test");
        ClickAndRun(rig, screen.FinishButton!);
        Assert.True(screen.IsCreatePending);

        rig.Session.Reducer.OnCharGenResponse(Context(rig), new CharGenResponseMsg(result, 0, null, 0));
        rig.Frame();

        Assert.False(screen.IsCreatePending);
        Assert.NotEqual(0u, screen.ErrorDialog);
        Assert.Equal(Pregame(token), rig.DialogText(screen.ErrorDialog));
        Assert.IsType<CharGenMainScreen>(rig.Flow.CurrentScreen);
    }

    [RequiresLocalDatsFact]
    public void Finish_while_a_create_waits_for_its_answer_sends_nothing_more()
    {
        using var rig = new LoginRig(fixture);
        var screen = OpenViaCreate(rig);
        Go(rig, screen, CharGenPageId.Summary);
        Type(rig, "Double Click");
        var listed = rig.Characters.Entries.Count;
        ClickAndRun(rig, screen.FinishButton!);
        Assert.True(screen.IsCreatePending);

        ClickAndRun(rig, screen.FinishButton!);

        Assert.True(screen.IsCreatePending);
        Assert.True(rig.RunUntilMode(UiModes.CharacterManagement, 5));
        rig.Run(1.0);
        Assert.Equal(listed + 1, rig.Characters.Entries.Count);
    }

    [RequiresLocalDatsFact]
    public void Without_a_responder_the_create_is_refused_offline_and_says_so_as_a_placeholder()
    {
        using var rig = new LoginRig(fixture, standIn: h => Lifestone.Client.Session.StandIn.StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2)));
        var screen = OpenViaCreate(rig);
        Go(rig, screen, CharGenPageId.Summary);
        Type(rig, "no server");

        ClickAndRun(rig, screen.FinishButton!);

        Assert.False(screen.IsCreatePending);
        Assert.NotEqual(0u, screen.ErrorDialog);
        Assert.StartsWith(NotInM2.Prefix, rig.DialogText(screen.ErrorDialog), StringComparison.Ordinal);
        Assert.True(rig.Log.AnyLineContains("placeholder: Not in Lifestone M2: creating a character"));
    }

    [RequiresLocalDatsFact]
    public void The_intent_the_create_sends_always_has_exactly_55_skills_and_verifies_for_every_heritage_and_gender()
    {
        var catalog = CharGenCatalog.Load(fixture.Dats);
        foreach (var heritage in Enumerable.Range(1, 13))
        {
            foreach (var gender in new[] { 1u, 2u })
            {
                for (var seed = 1; seed <= 3; seed++)
                {
                    var state = new CharGenState(catalog, new CharGenRandom(seed));
                    state.RandomizeCharacter(true);
                    state.SetHeritageGroup((uint)heritage);
                    state.SetGender(gender);
                    state.SetName("Intent Test");
                    state.FitTemplateToCharacter();

                    var result = state.GetCharGenResult();
                    var intent = result.ToIntent();

                    Assert.True(CharGenVerifier.Verify(result, catalog, out var problem), $"heritage {heritage} gender {gender} seed {seed}: {problem}");
                    Assert.Equal(CharacterCreateIntent.RequiredSkillCount, intent.SkillAdvancementClasses.Count);
                    Assert.Equal(55, intent.SkillAdvancementClasses.Count);
                    Assert.True(intent.TryValidate(out var invalid), invalid);
                    Assert.Equal("Intent Test", intent.Name);
                    Assert.True(state.Template >= 0, "the summary page fits the profession, so a character always has a template of 0 or more");
                }
            }
        }
    }

    [RequiresLocalDatsFact]
    public void A_character_that_does_not_verify_is_not_sent_and_the_screen_says_so()
    {
        using var rig = new LoginRig(fixture, throneOfDestiny: false);
        var screen = OpenViaCreate(rig);
        screen.State.SetHeritageGroup(4); // Viamontian, which this account lacks
        Go(rig, screen, CharGenPageId.Summary);
        Type(rig, "not allowed");

        ClickAndRun(rig, screen.FinishButton!);

        Assert.False(screen.IsCreatePending);
        Assert.NotEqual(0u, screen.ErrorDialog);
        Assert.StartsWith("Lifestone: this character cannot be created", rig.DialogText(screen.ErrorDialog), StringComparison.Ordinal);
        Assert.True(rig.Log.AnyLineContains("does not verify"));
        Assert.DoesNotContain(rig.Characters.Entries, e => e.Name == "Not Allowed");
    }
}
