using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Screens;

/// <summary>
/// The login screens' ties to the user's dats (m2-plan G2 U4): every element a screen uses exists in its layout with the class the screen
/// casts it to, the states the data check sets exist, and every string the screens ask for is in the pregame table.
/// </summary>
public sealed class LoginScreenDataTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private UiElementManager Manager(RecordingLogSink log) => new(
        new UiServices { Descs = UiDescSource.From(new UiLayouts(fixture.Dats)), Log = log },
        UiScalePolicy.Choose(1920, 1080));

    [RequiresLocalDatsTheory]
    [MemberData(nameof(Elements))]
    public void Every_element_a_screen_uses_exists_in_its_layout_with_the_class_the_screen_expects(string screen, uint layoutEnum, uint rootId, uint elementId, Type type)
    {
        var log = new RecordingLogSink(LogSeverity.Error);
        var manager = Manager(log);
        manager.ForceDisplayResolution(true);

        var root = manager.CreateRootElement(layoutEnum, rootId);

        Assert.NotNull(root);
        var found = root.GetChildRecursive(elementId);
        Assert.True(found is not null, $"{screen}: element 0x{elementId:X8} is missing from layout 0x{layoutEnum:X8}");
        Assert.IsAssignableFrom(type, found);
        Assert.False(found.IsStandIn, $"{screen}: element 0x{elementId:X8} is a stand-in class");
        Assert.Empty(log.Lines);
    }

    public static TheoryData<string, uint, uint, uint, Type> Elements()
    {
        var data = new TheoryData<string, uint, uint, uint, Type>();
        void Add(string screen, UiRootRef root, Type type, params uint[] ids)
        {
            foreach (var id in ids)
            {
                data.Add(screen, root.LayoutEnum, root.ElementId, id, type);
            }
        }

        Add("data check", UiLayoutEnums.DataPatchRoot, typeof(UiButton), DataPatchScreen.QuitButtonId);
        Add("data check", UiLayoutEnums.DataPatchRoot, typeof(UiMeter), DataPatchScreen.ConnectMeterId, DataPatchScreen.PatchMeterId);
        Add("data check", UiLayoutEnums.DataPatchRoot, typeof(UiTextElement), DataPatchScreen.ConnectTextId, DataPatchScreen.PatchTextId);
        Add("disconnected", UiLayoutEnums.DisconnectedRoot, typeof(UiTextElement), DisconnectedScreen.ErrorTextId);
        Add("disconnected", UiLayoutEnums.DisconnectedRoot, typeof(UiButton), DisconnectedScreen.ButtonId);
        Add("character management", UiLayoutEnums.CharacterManagementRoot, typeof(UiListBox), CharacterManagementScreen.ListId);
        Add("character management", UiLayoutEnums.CharacterManagementRoot, typeof(UiTextElement), CharacterManagementScreen.WorldNameId);
        Add(
            "character management",
            UiLayoutEnums.CharacterManagementRoot,
            typeof(UiButton),
            CharacterManagementScreen.RestoreButtonId,
            CharacterManagementScreen.DeleteButtonId,
            CharacterManagementScreen.CreateButtonId,
            CharacterManagementScreen.EnterButtonId,
            CharacterManagementScreen.CreditsButtonId,
            CharacterManagementScreen.ExitButtonId);
        return data;
    }

    [RequiresLocalDatsFact]
    public void The_connect_label_has_the_states_the_data_check_sets()
    {
        var manager = Manager(new RecordingLogSink(LogSeverity.Error));
        manager.ForceDisplayResolution(true);
        var root = manager.CreateRootElement(UiLayoutEnums.DataPatchRoot.LayoutEnum, UiLayoutEnums.DataPatchRoot.ElementId)!;

        var label = root.GetChildRecursive(DataPatchScreen.ConnectTextId)!;

        Assert.Contains(UiStateIds.Normal, label.Desc.States.Keys);
        Assert.Contains(DataPatchScreen.ConnectingState, label.Desc.States.Keys);
        Assert.Contains(DataPatchScreen.ConnectedState, label.Desc.States.Keys);
    }

    [RequiresLocalDatsFact]
    public void The_list_has_the_row_template_with_the_buttons_double_click_and_highlight_states()
    {
        var manager = Manager(new RecordingLogSink(LogSeverity.Error));
        manager.ForceDisplayResolution(true);
        var root = manager.CreateRootElement(UiLayoutEnums.CharacterManagementRoot.LayoutEnum, UiLayoutEnums.CharacterManagementRoot.ElementId)!;
        var list = Assert.IsType<UiListBox>(root.GetChildRecursive(CharacterManagementScreen.ListId));

        var row = list.AddItemFromTemplateList(0)!;

        Assert.IsAssignableFrom<UiTextElement>(row);
        Assert.IsAssignableFrom<UiButton>(row);
        Assert.Equal(CharacterManagementScreen.RowTemplateId, row.ElementId);
        Assert.Contains(UiStateIds.Highlight, row.Desc.States.Keys);
        Assert.Null(list.AddItemFromTemplateList(1));
    }

    [RequiresLocalDatsFact]
    public void Every_string_the_screens_ask_for_is_in_the_pregame_table_and_says_what_retail_says()
    {
        var strings = new UiStrings(new UiStringTables(fixture.Dats));
        (string Token, string StartsWith)[] tokens =
        [
            ("ID_DataPatch_Interrogation", "Looking for data"),
            ("ID_DataPatch_Waiting", "Too many people patching"),
            ("ID_DataPatch_Patching", "Patching data"),
            ("ID_DataPatch_PatchProgress", string.Empty),
            ("ID_DataPatch_PatchingDone", "Patching Done"),
            ("ID_CharacterManagement_DeleteCharacterConfirmation", "WARNING!"),
            ("ID_CharacterManagement_DeleteCharacterResponse", "DELETE"),
            ("ID_CharacterManagement_PleaseWait", "Please Wait"),
            ("ID_Character_EnteringWorld", "Entering World"),
            ("ID_CharacterManagement_ConfirmExit", "Are you sure you want to leave?"),
            ("ID_CharacterManagement_CG_VERIFICATION_RESPONSE_NAME_IN_USE", "Sorry"),
            ("ID_CharacterManagement_CG_VERIFICATION_RESPONSE_CORRUPT", "Sorry"),
            ("ID_CharacterManagement_CG_VERIFICATION_RESPONSE_DATABASE_DOWN", "Sorry"),
            (GmUiFlow.ConnectionLostToken, "The connection to the server has been lost"),
        ];

        foreach (var (token, startsWith) in tokens)
        {
            var variables = new UiStringVariables().SetText("total", "7").SetText("percent", "50").SetText(CharacterManagementScreen.DeleteNameVariable, "Someone");
            var result = strings.Render(new UiStringRef(StringHash.Tokenize(token), GmUiFlow.PregameTableId), variables);
            Assert.True(result.Status is UiStringStatus.Ok or UiStringStatus.Unsupported, $"{token} is not in the pregame table ({result.Status})");
            Assert.StartsWith(startsWith, result.Text, StringComparison.Ordinal);
        }
    }

    [RequiresLocalDatsFact]
    public void Every_character_error_route_has_its_string_but_the_old_character_one_which_retail_lacks()
    {
        var strings = new UiStrings(new UiStringTables(fixture.Dats));

        foreach (var route in GmUiFlow.Routes.Values)
        {
            var result = strings.Render(new UiStringRef(StringHash.Tokenize(route.Token), GmUiFlow.PregameTableId));
            var missing = route.Token == "ID_CHAR_ERROR_ENTER_GAME_OLD_CHARACTER";
            Assert.Equal(!missing, result.Status is UiStringStatus.Ok or UiStringStatus.Unsupported);
        }
    }

    [RequiresLocalDatsFact]
    public void The_delete_confirmation_names_the_character_through_the_variable_the_screen_gives()
    {
        var tables = new UiStringTables(fixture.Dats);
        var table = tables.GetTable(GmUiFlow.PregameTableId);

        Assert.True(table.TryGetEntry(StringHash.Tokenize("ID_CharacterManagement_DeleteCharacterConfirmation"), out var entry));
        Assert.Equal([CharacterManagementScreen.DeleteNameVariable], entry.Variables);
        Assert.True(table.TryGetEntry(StringHash.Tokenize("ID_DataPatch_PatchProgress"), out var progress));
        Assert.Equal([StringHash.Tokenize("percent"), StringHash.Tokenize("total")], progress.Variables);
        Assert.True(table.TryGetEntry(StringHash.Tokenize("ID_DataPatch_Waiting"), out var waiting));
        Assert.Equal([StringHash.Tokenize("total")], waiting.Variables);
    }

    [RequiresLocalDatsFact]
    public void Every_mode_the_flow_registers_gets_a_screen_a_stand_in_or_a_logged_todo()
    {
        var log = new RecordingLogSink();
        var manager = Manager(log);
        var host = new FakeLoginHost();
        using var session = Lifestone.Client.Session.GameSession.Offline(h => Lifestone.Client.Session.StandIn.StandInSessions.CharacterSelect(h), new Lifestone.Tests.Shared.ManualTimeProvider(), log);
        using var screens = new RetailUiScreenHost(manager, session, host, new UiCursors(fixture.Dats).Default);
        var context = screens.Context;

        foreach (var mode in UiModes.RetailOrder)
        {
            using var screen = LoginScreens.Create(context, mode);
            var expectsScreen = mode is not UiModes.Credits;
            Assert.True(expectsScreen == (screen is not null), $"{UiModes.NameOf(mode)}: screen {(screen is null ? "missing" : "made")}");
            Assert.Equal(LoginScreens.IsStandIn(screen, mode), mode is UiModes.CharGen); // this host gives no CharGen data, so character creation is the stand-in; the game mode is the in-game shell (GamePlayScreen)
        }
    }
}
