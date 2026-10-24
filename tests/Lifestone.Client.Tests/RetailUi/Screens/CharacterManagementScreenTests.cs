using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.RetailUi.Screens;

/// <summary>
/// Character management (m2-plan G2 U4) over the user's dats with the retail fonts and strings and the offline stand-in session with its
/// responder: the list and its order, the selection and the buttons it ghosts, the typed delete confirmation, restore, enter, leave,
/// create and credits, and the errors the flow and the server send. Retail-exact: no 3D view.
/// </summary>
public sealed class CharacterManagementScreenTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static readonly StandInCharacter[] Roster =
    [
        new("Zed Wanderer"),
        new("Amy Archer"),
        new("Mid Mage", PendingDeletion: true),
        new("Bo Brawler"),
    ];

    private LoginRig Rig(params StandInCharacter[] characters) =>
        new(fixture, h =>
        {
            StandInSessions.CharacterSelect(h, characters.Length == 0 ? Roster : characters);
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        });

    private static string[] Names(CharacterManagementScreen screen) => [.. screen.List!.Items.Select(i => Assert.IsAssignableFrom<UiTextElement>(i).Text)];

    private static UiElement RowOf(CharacterManagementScreen screen, string name) => screen.List!.Items.Single(i => ((UiTextElement)i).Text == name);

    private static uint GuidOf(UiElement row) => row.InqProperty(CharacterManagementScreen.InstanceIdProperty, out var p) ? ((UiInstanceIdProperty)p).Value : 0;

    private static IEnumerable<ActionLogEntry> Logged(LoginRig rig) => Enumerable.Range(0, rig.Session.ActionLog.Count).Select(i => rig.Session.ActionLog[i]);

    private InboundContext Context(LoginRig rig) => new(0, MessageQueue.Login, rig.Time.GetTimestamp());

    // ---------------------------------------------------------------- Pure rules

    [Theory]
    [InlineData(-1, false, 3, 11u, UiStateIds.Ghosted, UiStateIds.Normal, true, false)]
    [InlineData(0, false, 3, 11u, UiStateIds.Normal, UiStateIds.Normal, true, false)]
    [InlineData(2, true, 3, 11u, UiStateIds.Ghosted, UiStateIds.Normal, false, true)]
    [InlineData(0, false, 11, 11u, UiStateIds.Normal, UiStateIds.Ghosted, true, false)]
    [InlineData(-1, true, 1, 11u, UiStateIds.Ghosted, UiStateIds.Normal, true, false)] // pending with no row selected: Delete shows (retail's index test)
    [InlineData(0, false, 0, 0u, UiStateIds.Normal, UiStateIds.Ghosted, true, false)] // an unknown slot count ghosts Create
    public void The_ghosting_matrix_follows_UpdateButtons(int index, bool pending, int count, uint slots, uint enter, uint create, bool deleteVisible, bool restoreVisible)
    {
        var states = CharacterManagementRules.Buttons(index, pending, count, slots);

        Assert.Equal(new CharacterButtonStates(enter, create, deleteVisible, restoreVisible), states);
    }

    [Fact]
    public void The_list_sorts_by_name_with_the_characters_pending_deletion_last()
    {
        CharacterSlot[] entries = [new(1, "Zed", 0), new(2, "amy", 0), new(3, "Amy", 0), new(4, "Bo", 7), new(5, "Al", 1)];

        var names = CharacterManagementRules.DisplayOrder(entries).Select(i => entries[i].Name).ToArray();

        // Ordinal: "Amy" < "Zed" < "amy" for the ready ones; then the pending ones by name.
        Assert.Equal(["Amy", "Zed", "amy", "Al", "Bo"], names);
    }

    [Fact]
    public void The_default_selection_is_the_first_ready_character_in_server_order_else_the_first()
    {
        Assert.Equal(1, CharacterManagementRules.DefaultSelection([new(1, "a", 1), new(2, "b", 0), new(3, "c", 0)]));
        Assert.Equal(0, CharacterManagementRules.DefaultSelection([new(1, "a", 1), new(2, "b", 1)]));
        Assert.Equal(-1, CharacterManagementRules.DefaultSelection([]));
    }

    [Theory]
    [InlineData(320, 11, 11u, 16)]
    [InlineData(320, 20, 11u, 16)]
    [InlineData(320, 40, 11u, 8)]
    [InlineData(320, 0, 0u, 16)]
    public void A_row_is_a_slots_share_of_the_height_but_at_most_a_twentieth(int height, int count, uint slots, int expected) =>
        Assert.Equal(expected, CharacterManagementRules.RowHeight(height, count, slots));

    [Theory]
    [InlineData("DELETE", true)]
    [InlineData("delete", true)]
    [InlineData("delet", false)]
    [InlineData("DELETE ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_typed_confirmation_is_compared_ignoring_case(string? typed, bool confirmed) =>
        Assert.Equal(confirmed, CharacterManagementRules.IsDeleteConfirmed(typed, "DELETE"));

    [Theory]
    [InlineData(1u, null)]
    [InlineData(2u, null)]
    [InlineData(3u, "ID_CharacterManagement_CG_VERIFICATION_RESPONSE_NAME_IN_USE")]
    [InlineData(4u, null)]
    [InlineData(5u, "ID_CharacterManagement_CG_VERIFICATION_RESPONSE_CORRUPT")]
    [InlineData(6u, "ID_CharacterManagement_CG_VERIFICATION_RESPONSE_DATABASE_DOWN")]
    [InlineData(7u, null)]
    public void Only_three_refusals_have_a_message(uint code, string? token) => Assert.Equal(token, CharacterManagementRules.VerificationToken(code));

    // ---------------------------------------------------------------- The list

    [RequiresLocalDatsFact]
    public void The_list_shows_the_characters_by_name_with_the_pending_one_last_and_in_red()
    {
        using var rig = Rig();
        var screen = rig.ToCharacterManagement();

        Assert.Equal(["Amy Archer", "Bo Brawler", "Zed Wanderer", "Mid Mage"], Names(screen));
        var pending = Assert.IsAssignableFrom<UiTextElement>(RowOf(screen, "Mid Mage"));
        Assert.Equal(new UiRgbaColor(1f, 0f, 0f, 1f), pending.FontColor);
        Assert.NotEqual(pending.FontColor, ((UiTextElement)RowOf(screen, "Amy Archer")).FontColor);
        Assert.Equal(rig.Characters.Entries.Single(e => e.Name == "Mid Mage").Guid, GuidOf(pending));
        Assert.Equal("Lifestone Stand-in", Assert.IsAssignableFrom<UiTextElement>(rig.Element<UiElement>(CharacterManagementScreen.WorldNameId)).Text);
        Assert.All(screen.List!.Items, r => Assert.True(r.WantsDoubleClicks));
        Assert.All(screen.List.Items, r => Assert.Equal(16, r.Box.Height));
    }

    [RequiresLocalDatsFact]
    public void The_first_ready_character_in_server_order_is_selected_with_the_buttons_it_calls_for()
    {
        using var rig = Rig();
        var screen = rig.ToCharacterManagement();

        // Server order is Zed, Amy, Mid (pending), Bo: Zed is the first ready one, though Amy sorts first.
        Assert.Equal(rig.Characters.Entries[0].Guid, screen.SelectedGuid);
        Assert.Equal(rig.Characters.Entries[0].Guid, rig.Characters.SelectedGuid);
        Assert.Equal("Zed Wanderer", ((UiTextElement)screen.List!.SelectedItem!).Text);
        Assert.Equal(UiStateIds.Normal, screen.EnterButton!.State);
        Assert.True(screen.DeleteButton!.Visible);
        Assert.False(screen.RestoreButton!.Visible);
        Assert.Equal(UiStateIds.Normal, screen.CreateButton!.State);
    }

    [RequiresLocalDatsFact]
    public void Selecting_a_character_pending_deletion_ghosts_Enter_and_swaps_Delete_for_Restore()
    {
        using var rig = Rig();
        var screen = rig.ToCharacterManagement();

        Assert.True(rig.Click(RowOf(screen, "Mid Mage")));
        rig.Frame();

        Assert.Equal("Mid Mage", ((UiTextElement)screen.List!.SelectedItem!).Text);
        Assert.Equal(UiStateIds.Ghosted, screen.EnterButton!.State);
        Assert.False(screen.DeleteButton!.Visible);
        Assert.True(screen.RestoreButton!.Visible);
        Assert.Equal(rig.Characters.Entries[2].Guid, rig.Characters.SelectedGuid);

        Assert.True(rig.Click(RowOf(screen, "Bo Brawler")));
        rig.Frame();

        Assert.Equal(UiStateIds.Normal, screen.EnterButton.State);
        Assert.True(screen.DeleteButton.Visible);
        Assert.False(screen.RestoreButton.Visible);
        Assert.Equal(rig.Characters.Entries[3].Guid, rig.Characters.SelectedGuid);
    }

    [RequiresLocalDatsFact]
    public void Create_is_ghosted_when_every_slot_is_taken()
    {
        using var rig = Rig([.. Enumerable.Range(0, 11).Select(i => new StandInCharacter($"Char {i:00}"))]);
        var screen = rig.ToCharacterManagement();

        Assert.Equal(UiStateIds.Ghosted, screen.CreateButton!.State);
        Assert.Equal(11, screen.List!.Items.Count);
        Assert.Single(screen.List.Items.Select(i => i.Box.Height).Distinct());
    }

    [RequiresLocalDatsFact]
    public void An_empty_account_lists_nothing_and_ghosts_Enter()
    {
        using var rig = new LoginRig(fixture, h => StandInSessions.CharacterSelect(h, new StandInCharacter("Only")));
        var screen = rig.ToCharacterManagement();
        Assert.Single(screen.List!.Items);

        // The only character is deleted behind the screen's back: the list is empty and nothing is selected.
        rig.Session.Reducer.OnCharacterList(Context(rig), new CharacterListMsg(0, [], 0, 11, "stand-in", 0, 1));
        rig.Frame();

        Assert.Empty(screen.List.Items);
        Assert.Equal(0u, screen.SelectedGuid);
        Assert.Equal(UiStateIds.Ghosted, screen.EnterButton!.State);
    }

    // ---------------------------------------------------------------- Delete and restore

    [RequiresLocalDatsFact]
    public void Delete_asks_for_the_typed_word_then_waits_for_the_new_list()
    {
        using var rig = Rig();
        var screen = rig.ToCharacterManagement();
        Assert.True(rig.Click(RowOf(screen, "Bo Brawler")));
        rig.Frame();

        Assert.True(rig.Click(screen.DeleteButton!));
        rig.Frame();

        Assert.NotEqual(0u, screen.DeleteDialog);
        var dialog = Assert.IsType<UiConfirmationTextInputDialog>(rig.Dialog(screen.DeleteDialog));
        Assert.Contains("Bo Brawler will be deleted", rig.DialogText(screen.DeleteDialog), StringComparison.Ordinal);
        Assert.Contains("type 'DELETE'", rig.DialogText(screen.DeleteDialog), StringComparison.Ordinal);

        // The wrong word deletes nothing.
        Assert.IsAssignableFrom<UiTextElement>(dialog.GetChildRecursive(UiConfirmationTextInputDialog.Field)).SetText("remove");
        Assert.True(rig.Click(Assert.IsAssignableFrom<UiElement>(dialog.GetChildRecursive(UiConfirmationTextInputDialog.DoneButton))));
        rig.Run(1.0);
        Assert.Equal(0u, screen.DeleteDialog);
        Assert.Equal(0u, screen.PleaseWaitDialog);
        Assert.False(rig.Characters.Entries[3].PendingDeletion);

        // The right word, in any case, asks the server and waits.
        Assert.True(rig.Click(screen.DeleteButton!));
        rig.Frame();
        dialog = Assert.IsType<UiConfirmationTextInputDialog>(rig.Dialog(screen.DeleteDialog));
        Assert.IsAssignableFrom<UiTextElement>(dialog.GetChildRecursive(UiConfirmationTextInputDialog.Field)).SetText("delete");
        Assert.True(rig.Click(Assert.IsAssignableFrom<UiElement>(dialog.GetChildRecursive(UiConfirmationTextInputDialog.DoneButton))));
        rig.Frame();

        Assert.NotEqual(0u, screen.PleaseWaitDialog);
        Assert.IsType<UiWaitDialog>(rig.Dialog(screen.PleaseWaitDialog));
        Assert.Equal("Please Wait", rig.DialogText(screen.PleaseWaitDialog));
        rig.Run(0.4);
        Assert.NotEqual(0u, screen.PleaseWaitDialog);

        rig.Run(0.5);
        Assert.Equal(0u, screen.PleaseWaitDialog);
        Assert.True(rig.Characters.Entries[3].PendingDeletion);
        Assert.Equal(["Amy Archer", "Zed Wanderer", "Bo Brawler", "Mid Mage"], Names(screen));
        Assert.Equal("Bo Brawler", ((UiTextElement)screen.List!.SelectedItem!).Text);
        Assert.True(screen.RestoreButton!.Visible);
        Assert.False(screen.DeleteButton!.Visible);
        Assert.Equal(UiStateIds.Ghosted, screen.EnterButton!.State);
    }

    [RequiresLocalDatsFact]
    public void Cancelling_the_delete_dialog_deletes_nothing()
    {
        using var rig = Rig();
        var screen = rig.ToCharacterManagement();

        Assert.True(rig.Click(screen.DeleteButton!));
        rig.Frame();
        var dialog = Assert.IsType<UiConfirmationTextInputDialog>(rig.Dialog(screen.DeleteDialog));
        Assert.IsAssignableFrom<UiTextElement>(dialog.GetChildRecursive(UiConfirmationTextInputDialog.Field)).SetText("DELETE");
        Assert.True(rig.Click(Assert.IsAssignableFrom<UiElement>(dialog.GetChildRecursive(UiConfirmationTextInputDialog.CancelButton))));
        rig.Run(1.0);

        Assert.Equal(0u, screen.DeleteDialog);
        Assert.Equal(0u, screen.PleaseWaitDialog);
        Assert.All(rig.Characters.Entries.Where(e => e.Name != "Mid Mage"), e => Assert.False(e.PendingDeletion));
        Assert.DoesNotContain(Logged(rig), a => a.Kind == ActionKind.DeleteCharacter);
    }

    [RequiresLocalDatsFact]
    public void Delete_does_nothing_when_no_row_is_selected()
    {
        using var rig = Rig();
        var screen = rig.ToCharacterManagement();
        screen.List!.SetSelectedItem(null, broadcast: true);
        rig.Frame();

        rig.Manager.BroadcastElementMessage(new UiElementMessage(screen.DeleteButton!, UiMessageIds.ButtonClick, 7, 0));
        rig.Frame();

        Assert.Equal(0u, screen.DeleteDialog);
        Assert.Equal(UiStateIds.Ghosted, screen.EnterButton!.State);
    }

    [RequiresLocalDatsFact]
    public void Restore_waits_and_the_character_is_ready_again()
    {
        using var rig = Rig();
        var screen = rig.ToCharacterManagement();
        Assert.True(rig.Click(RowOf(screen, "Mid Mage")));
        rig.Frame();

        Assert.True(rig.Click(screen.RestoreButton!));
        rig.Frame();
        Assert.NotEqual(0u, screen.PleaseWaitDialog);
        Assert.True(rig.Characters.Entries[2].PendingDeletion);

        rig.Run(0.8);

        Assert.Equal(0u, screen.PleaseWaitDialog);
        Assert.False(rig.Characters.Entries[2].PendingDeletion);
        Assert.Equal(["Amy Archer", "Bo Brawler", "Mid Mage", "Zed Wanderer"], Names(screen));
        Assert.Equal("Mid Mage", ((UiTextElement)screen.List!.SelectedItem!).Text);
        Assert.Equal(UiStateIds.Normal, screen.EnterButton!.State);
        Assert.True(screen.DeleteButton!.Visible);
        Assert.False(screen.RestoreButton!.Visible);
    }

    [RequiresLocalDatsTheory]
    [InlineData(CharGenResult.NameInUse, "Sorry, another character already exists by that name. Your character can not be restored.")]
    [InlineData(CharGenResult.Corrupt, "Sorry, that character can not be restored.")]
    [InlineData(CharGenResult.DatabaseDown, "Sorry, that character can not be restored. Please try again later.")]
    public void A_refused_restore_closes_the_wait_and_says_why(CharGenResult result, string text)
    {
        using var rig = Rig();
        var screen = rig.ToCharacterManagement();
        Assert.True(rig.Click(RowOf(screen, "Mid Mage")));
        rig.Frame();
        rig.Click(screen.RestoreButton!);
        rig.Frame();
        Assert.NotEqual(0u, screen.PleaseWaitDialog);

        rig.Session.Reducer.OnCharGenResponse(Context(rig), new CharGenResponseMsg(result, 0, null, 0));
        rig.Frame();

        Assert.Equal(0u, screen.PleaseWaitDialog);
        Assert.NotEqual(0u, screen.ErrorDialog);
        Assert.Equal(text, rig.DialogText(screen.ErrorDialog));
        Assert.True(rig.Characters.Entries[2].PendingDeletion);
    }

    [RequiresLocalDatsFact]
    public void A_refused_restore_without_a_message_only_closes_the_wait()
    {
        using var rig = Rig();
        var screen = rig.ToCharacterManagement();
        rig.Click(RowOf(screen, "Mid Mage"));
        rig.Frame();
        rig.Click(screen.RestoreButton!);
        rig.Frame();

        rig.Session.Reducer.OnCharGenResponse(Context(rig), new CharGenResponseMsg(CharGenResult.NameBanned, 0, null, 0));
        rig.Frame();

        Assert.Equal(0u, screen.PleaseWaitDialog);
        Assert.Equal(0u, screen.ErrorDialog);
    }

    // ---------------------------------------------------------------- Enter, leave, create, credits

    [RequiresLocalDatsFact]
    public void Enter_shows_the_entering_dialog_and_the_offline_stand_in_says_it_has_no_server()
    {
        using var rig = Rig();
        var screen = rig.ToCharacterManagement();

        Assert.True(rig.Click(screen.EnterButton!));
        rig.Frame();

        // The stand-in refuses at once: the "Entering World" dialog closes and a message says why, in Lifestone's words.
        Assert.Equal(0u, screen.EnteringWorldDialog);
        Assert.NotEqual(0u, screen.ErrorDialog);
        Assert.StartsWith("Not in Lifestone M2: entering the world without a server", rig.DialogText(screen.ErrorDialog), StringComparison.Ordinal);
        Assert.Contains(Logged(rig), a => a.Kind == ActionKind.EnterWorld && a.Source == ActionSource.Ui);
        Assert.Contains(rig.Log.Lines, l => l.Contains("placeholder: Not in Lifestone M2: entering the world", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void Enter_does_nothing_for_a_character_pending_deletion_or_with_none_selected()
    {
        using var rig = Rig();
        var screen = rig.ToCharacterManagement();
        rig.Click(RowOf(screen, "Mid Mage"));
        rig.Frame();

        rig.Manager.BroadcastElementMessage(new UiElementMessage(screen.EnterButton!, UiMessageIds.ButtonClick, 7, 0));
        rig.Manager.BroadcastElementMessage(new UiElementMessage(RowOf(screen, "Mid Mage"), UiMessageIds.DoubleClick, 10, 0));
        rig.Frame();

        Assert.Equal(0u, screen.EnteringWorldDialog);
        Assert.Equal(0u, screen.ErrorDialog);
        Assert.DoesNotContain(Logged(rig), a => a.Kind == ActionKind.EnterWorld);
    }

    [RequiresLocalDatsFact]
    public void A_double_click_on_a_row_enters_that_character()
    {
        using var rig = Rig();
        var screen = rig.ToCharacterManagement();
        Assert.True(rig.Click(RowOf(screen, "Bo Brawler")));
        rig.Frame();

        rig.Manager.BroadcastElementMessage(new UiElementMessage(RowOf(screen, "Bo Brawler"), UiMessageIds.DoubleClick, 10, 0));
        rig.Frame();

        var entry = Assert.Single(Logged(rig), a => a.Kind == ActionKind.EnterWorld);
        Assert.Contains(rig.Characters.Entries[3].Guid.ToString("X8"), entry.Summary, StringComparison.Ordinal);
    }

    [RequiresLocalDatsFact]
    public void Entering_the_world_starts_the_game_screen_when_the_player_is_created()
    {
        using var rig = Rig();
        rig.ToCharacterManagement();

        rig.Session.Reducer.OnPlayerCreate(Context(rig), new PlayerCreateMsg(rig.Characters.Entries[0].Guid));
        rig.Frame();
        rig.Frame();

        Assert.Equal(UiModes.GamePlay, rig.Flow.CurrentMode);
    }

    [RequiresLocalDatsFact]
    public void Exit_asks_to_leave_and_yes_ends_the_application_through_the_epilogue()
    {
        using var rig = Rig();
        var screen = rig.ToCharacterManagement();

        Assert.True(rig.Click(rig.Element<UiButton>(CharacterManagementScreen.ExitButtonId)));
        rig.Frame();
        Assert.NotEqual(0u, screen.ConfirmExitDialog);
        var dialog = Assert.IsType<UiConfirmationDialog>(rig.Dialog(screen.ConfirmExitDialog));
        Assert.StartsWith("Are you sure you want to leave?", rig.DialogText(screen.ConfirmExitDialog), StringComparison.Ordinal);

        // No leaves the screen as it is.
        Assert.True(rig.Click(Assert.IsAssignableFrom<UiElement>(dialog.GetChildRecursive(UiConfirmationDialog.NoButton))));
        rig.Run(0.2);
        Assert.Equal(0u, screen.ConfirmExitDialog);
        Assert.Equal(UiModes.CharacterManagement, rig.Flow.CurrentMode);
        Assert.Equal(0, rig.Host.Exits);

        rig.Click(rig.Element<UiButton>(CharacterManagementScreen.ExitButtonId));
        rig.Frame();
        dialog = Assert.IsType<UiConfirmationDialog>(rig.Dialog(screen.ConfirmExitDialog));
        Assert.True(rig.Click(Assert.IsAssignableFrom<UiElement>(dialog.GetChildRecursive(UiConfirmationDialog.YesButton))));
        rig.Run(0.2);

        Assert.Equal(UiModes.Epilogue, rig.Flow.CurrentMode);
        Assert.Equal(1, rig.Host.Exits);
    }

    [RequiresLocalDatsFact]
    public void The_escape_key_asks_to_leave_once()
    {
        using var rig = Rig();
        var screen = rig.ToCharacterManagement();

        var taken = rig.Manager.Input.KeyDown(DikScanCodes.Escape);
        rig.Manager.Input.KeyUp(DikScanCodes.Escape);
        rig.Frame();

        Assert.True(taken);
        var first = screen.ConfirmExitDialog;
        Assert.NotEqual(0u, first);
        Assert.IsType<UiConfirmationDialog>(rig.Dialog(first));

        // A second dialog cannot open on top: the context is kept, as retail's.
        Assert.True(screen.OnInputAction(CharacterManagementScreen.InputMap, CharacterManagementScreen.LeaveAction, new RetailActivation(true, 1f, default, 0, 0)));
        Assert.Equal(first, screen.ConfirmExitDialog);
        Assert.Single(rig.Manager.Dialogs.OpenDialogs);
    }

    [RequiresLocalDatsFact]
    public void Create_starts_character_creation_and_credits_say_they_are_not_in_M2()
    {
        using var rig = Rig();
        var screen = rig.ToCharacterManagement();

        Assert.True(rig.Click(rig.Element<UiButton>(CharacterManagementScreen.CreditsButtonId)));
        rig.Frame();
        var dialog = Assert.Single(rig.Manager.Dialogs.OpenDialogs);
        Assert.StartsWith("Not in Lifestone M2: Credits", Assert.IsAssignableFrom<UiTextElement>(dialog.GetChildRecursive(UiDialog.TextElement)).Text, StringComparison.Ordinal);
        dialog.CancelDialog();
        rig.Frame();

        Assert.True(rig.Click(screen.CreateButton!));
        rig.Frame();
        rig.Frame();

        Assert.Equal(UiModes.CharGen, rig.Flow.CurrentMode);
    }

    // ---------------------------------------------------------------- Errors

    [RequiresLocalDatsTheory]
    [InlineData(1u, "ID_CHAR_ERROR_LOGON")]
    public void A_character_error_that_ends_the_session_shows_the_disconnected_screen_with_its_reason(uint code, string token)
    {
        using var rig = Rig();
        rig.ToCharacterManagement();

        rig.Session.Reducer.OnCharacterError(Context(rig), new CharacterErrorMsg(code));
        Assert.True(rig.RunUntilMode(UiModes.Disconnected));
        rig.Frame();

        var expected = ResolveText(rig, token);
        Assert.False(string.IsNullOrWhiteSpace(expected));
        Assert.Equal(expected, rig.Screen<DisconnectedScreen>().ReasonText);
    }

    [RequiresLocalDatsTheory]
    [InlineData(5u, "ID_CHAR_ERROR_LOGOFF")]
    public void A_character_error_the_player_can_retry_returns_to_character_management_with_a_message(uint code, string token)
    {
        using var rig = Rig();
        var first = rig.ToCharacterManagement();
        first.List!.SetSelectedItem(RowOf(first, "Bo Brawler"), broadcast: true);
        rig.Frame();

        rig.Session.Reducer.OnCharacterError(Context(rig), new CharacterErrorMsg(code));
        rig.Frame();
        rig.Frame();

        var screen = Assert.IsType<CharacterManagementScreen>(rig.Flow.CurrentScreen);
        Assert.NotSame(first, screen);
        Assert.NotEqual(0u, screen.ErrorDialog);
        Assert.Equal(ResolveText(rig, token), rig.DialogText(screen.ErrorDialog));
        Assert.Equal(rig.Characters.Entries[3].Guid, screen.SelectedGuid); // the selection survives the screen
        Assert.Equal(2, rig.Manager.Roots.Count(r => r.IsShown)); // the screen and its message dialog
    }

    [RequiresLocalDatsFact]
    public void An_old_character_error_returns_to_character_management_though_retail_has_no_text_for_it()
    {
        using var rig = Rig();
        var first = rig.ToCharacterManagement();

        rig.Session.Reducer.OnCharacterError(Context(rig), new CharacterErrorMsg(17));
        rig.Frame();
        rig.Frame();

        var screen = Assert.IsType<CharacterManagementScreen>(rig.Flow.CurrentScreen);
        Assert.NotSame(first, screen);
        Assert.NotEqual(0u, screen.ErrorDialog); // code 17 has no string in the retail table: the dialog shows the string source's error text, loudly
    }

    [RequiresLocalDatsFact]
    public void A_server_error_retail_does_not_route_closes_the_wait_and_is_said_in_lifestones_words()
    {
        using var rig = Rig();
        var screen = rig.ToCharacterManagement();
        rig.Click(RowOf(screen, "Bo Brawler"));
        rig.Frame();
        rig.Click(screen.DeleteButton!);
        rig.Frame();
        var dialog = Assert.IsType<UiConfirmationTextInputDialog>(rig.Dialog(screen.DeleteDialog));
        Assert.IsAssignableFrom<UiTextElement>(dialog.GetChildRecursive(UiConfirmationTextInputDialog.Field)).SetText("DELETE");
        rig.Click(Assert.IsAssignableFrom<UiElement>(dialog.GetChildRecursive(UiConfirmationTextInputDialog.DoneButton)));
        rig.Run(0.2);
        Assert.NotEqual(0u, screen.PleaseWaitDialog);

        rig.Session.Reducer.OnCharacterError(Context(rig), new CharacterErrorMsg(2));
        rig.Run(0.2);

        Assert.Same(screen, rig.Flow.CurrentScreen);
        Assert.Equal(0u, screen.PleaseWaitDialog);
        Assert.NotEqual(0u, screen.ErrorDialog);
        Assert.StartsWith("Lifestone: deleting the character did not go through (ServerError", rig.DialogText(screen.ErrorDialog), StringComparison.Ordinal);
    }

    [RequiresLocalDatsFact]
    public void A_second_error_while_one_shows_is_dropped_as_in_retail()
    {
        using var rig = Rig();
        var screen = rig.ToCharacterManagement();
        screen.DisplayError(new UiStringRef(StringHash.Tokenize("ID_CHAR_ERROR_DELETE"), GmUiFlow.PregameTableId));
        var open = screen.ErrorDialog;

        screen.DisplayError(new UiStringRef(StringHash.Tokenize("ID_CHAR_ERROR_LOGOFF"), GmUiFlow.PregameTableId));

        Assert.NotEqual(0u, open);
        Assert.Equal(open, screen.ErrorDialog);
        Assert.Single(rig.Manager.Dialogs.OpenDialogs);
    }

    [RequiresLocalDatsFact]
    public void Leaving_the_screen_closes_its_dialogs_and_removes_its_root()
    {
        using var rig = Rig();
        var screen = rig.ToCharacterManagement();
        rig.Click(rig.Element<UiButton>(CharacterManagementScreen.ExitButtonId));
        rig.Frame();
        screen.DisplayError(new UiStringRef(StringHash.Tokenize("ID_CHAR_ERROR_DELETE"), GmUiFlow.PregameTableId));
        Assert.Single(rig.Manager.Dialogs.OpenDialogs); // the second waits behind the first, as dialogs of one queue do
        Assert.NotEqual(0u, screen.ConfirmExitDialog);
        Assert.NotEqual(0u, screen.ErrorDialog);

        rig.Flow.QueueUIMode(UiModes.Disconnected);
        rig.Frame();
        rig.Frame();

        Assert.Empty(rig.Manager.Dialogs.OpenDialogs);
        Assert.Single(rig.Manager.Roots);
        Assert.IsType<DisconnectedScreen>(rig.Flow.CurrentScreen);
    }

    private static string ResolveText(LoginRig rig, string token)
    {
        var reference = new UiStringRef(StringHash.Tokenize(token), GmUiFlow.PregameTableId);
        Assert.True(rig.Manager.Services.Strings!.TryGetString(reference, out var text), $"{token} is not in the pregame strings");
        return text;
    }
}
