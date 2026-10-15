using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>
/// Character management (retail's <c>gmCharacterManagementUI</c>): layout 0x10000005 (<c>charactermanagement</c>), root 0x1000039A. The
/// account's characters in a list (0x1000039D, rows from template 0x100003A5), the world's name (0x1000039B) and the buttons Enter
/// (0x100003A2), Delete (0x1000039F), Restore (0x1000039E, in Delete's place for a character pending deletion), Create (0x100003A0),
/// Credits (0x100003A3) and Exit (0x100003A4). Retail-exact: there is no 3D view of the selected character (owner decision D35).
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from <c>gmCharacterManagementUI</c> (11.4186 / 11.6096): the constructor (0x004EC8F0 / predicted 0x004ED580,
/// TODO(retailui-data)), <c>UpdateWorldName</c> (0x004EC120 / 0x004ECDB0), <c>SelectCharacter</c> (0x004EC160 / 0x004ECDF0),
/// <c>ResetPreviouslySelectedCharacterSlot</c> (0x004EBFF0 / 0x004ECC80), <c>UpdateButtons</c> (0x004EC240 / 0x004ECED0),
/// <c>RebuildCharacterList</c> (0x004EC3A0 / 0x004ED030), <c>Update</c> (0x004ECAB0 / 0x004ED740), the dialogs
/// <c>MakeErrorMessageDialog</c> (0x004ECB10 / 0x004ED7A0), <c>MakeDeleteCharacterConfirmationDialog</c> (0x004ECCA0 / 0x004ED930),
/// <c>MakePleaseWaitDialog</c> (0x004ECED0 / 0x004EDB60), <c>MakeEnteringWorldDialog</c> (0x004ED090 / 0x004EDD20) and
/// <c>MakeConfirmExitDialog</c> (0x004ED250 / 0x004EDEE0), <c>OnAction</c> (0x004ED410 / 0x004EE0A0), <c>EnterGame</c> (0x004ED440 /
/// 0x004EE0D0), <c>CloseDeleteCharacterDialog</c> (0x004ED4A0 / 0x004EE130), <c>ListenToElementMessage</c> (0x004ED5A0 / 0x004EE230),
/// <c>RecvNotice_CloseDialog</c> (0x004ED760 / 0x004EE3F0), <c>RecvNotice_CharGenVerificationResponse</c> (0x004EBF60 / 0x004ECBF0) and
/// <c>RecvNotice_BeginEnterWorld</c> (0x004E79F0 / 0x004E8680).
/// </para>
/// <para>
/// <b>The list</b> is rebuilt whenever the session's character list changes (<see cref="CharacterListChanged"/>; retail calls
/// <c>Update</c> when its persistent data changes): a row per character with the character's id as its instance id (property
/// 0x10000009), its name as text (red, colour 0 of its colour list, when pending deletion, with no countdown, D52), a height of
/// <see cref="CharacterManagementRules.RowHeight"/> and the wish for double clicks; then sorted by name with the pending ones last
/// (<see cref="CharacterManagementRules.DisplayOrder"/>). The selection is kept by character, else the first character in server order
/// that is not pending deletion. <b>Delete</b> names the character by its index in the server's list (the Action API finds it), and
/// <b>Restore</b> by its id.
/// </para>
/// <para>
/// <b>Dialogs</b> (from <see cref="DialogFactory"/>): Delete asks the player to type the confirmation word (case-insensitive, from the
/// strings) before anything is sent, then waits ("Please Wait") until the new list arrives; Restore waits the same way; Enter shows
/// "Entering World" until the flow moves on; Exit and Escape (input map 9, action 39) ask to confirm; an error from the flow
/// (<see cref="DisplayError"/>) or a refused restore is a message dialog. One error dialog and one of each other kind at a time, as
/// retail's contexts do.
/// </para>
/// <para>
/// <b>Differences, all loud.</b> Credits opens a "Not in Lifestone M2" dialog (D38). Entering the world with the offline stand-in session,
/// which has no server, says so in a message dialog (Lifestone's own literal); an action that fails for any reason other than a server
/// error does too. TODO(retailui-runtime): the Delete button's state argument is lost in the 11.4186 decompile of <c>UpdateButtons</c>;
/// it is taken as Normal (the button is always usable), to confirm in the 11.6096 bytes. Retail also records the selected slot in
/// <c>CharGenState</c>; the session's own selection (<see cref="IActionApi.SelectCharacter"/>) replaces it, since Lifestone's character
/// creation chooses the next free slot itself.
/// </para>
/// </remarks>
public sealed class CharacterManagementScreen : UiLoginScreen, IInputActionSink
{
    /// <summary>The world's name text.</summary>
    public const uint WorldNameId = 0x1000039B;

    /// <summary>The character list.</summary>
    public const uint ListId = 0x1000039D;

    /// <summary>Restore (shares Delete's place).</summary>
    public const uint RestoreButtonId = 0x1000039E;

    /// <summary>Delete.</summary>
    public const uint DeleteButtonId = 0x1000039F;

    /// <summary>Create.</summary>
    public const uint CreateButtonId = 0x100003A0;

    /// <summary>Enter.</summary>
    public const uint EnterButtonId = 0x100003A2;

    /// <summary>Credits.</summary>
    public const uint CreditsButtonId = 0x100003A3;

    /// <summary>Exit.</summary>
    public const uint ExitButtonId = 0x100003A4;

    /// <summary>The list's row template (a button): a double click on a row enters the world.</summary>
    public const uint RowTemplateId = 0x100003A5;

    /// <summary>The property that holds a row's character id (retail's InstanceID property 0x10000009).</summary>
    public const uint InstanceIdProperty = 0x10000009;

    /// <summary>The input map of the screen's keys (<c>gmCharacterManagementUI</c> registers map 9 at priority 3000).</summary>
    public const uint InputMap = 9;

    /// <summary>The priority of <see cref="InputMap"/>.</summary>
    public const int InputMapPriority = 3000;

    /// <summary>The action of map 9 that asks to leave (<c>OnAction</c> tests action 39, the Escape key).</summary>
    public const uint LeaveAction = 39;

    /// <summary>
    /// The id of the variable in the delete confirmation's text that takes the character's name (<c>ID_Player_1</c>; the only variable of
    /// string <c>ID_CharacterManagement_DeleteCharacterConfirmation</c>, pinned by a dat test).
    /// </summary>
    public const uint DeleteNameVariable = 0x05506DA2;

    /// <summary>The colour of a character pending deletion (retail sets font colour 0 of the row to red, 1.0 0 0 1.0).</summary>
    public const uint PendingDeletionColor = 0xFFFF0000;

    private readonly UiListBox? _list;
    private readonly UiButton? _enter;
    private readonly UiButton? _delete;
    private readonly UiButton? _restore;
    private readonly UiButton? _create;
    private readonly List<IDisposable> _subscriptions = [];
    private uint _selectedGuid;
    private int _selectedItemIndex = -1;
    private uint _errorContext;
    private uint _confirmExitContext;
    private uint _deleteContext;
    private uint _pleaseWaitContext;
    private uint _enteringWorldContext;
    private bool _dirty;

    /// <summary>Character management over <paramref name="context"/>: the buttons settled, the list built when the characters are in.</summary>
    public CharacterManagementScreen(UiLoginContext context)
        : base(context, UiLayoutEnums.CharacterManagementRoot, "gmCharacterManagementUI")
    {
        _list = Find<UiListBox>(ListId);
        _create = Find<UiButton>(CreateButtonId);
        _enter = Find<UiButton>(EnterButtonId);
        _delete = Find<UiButton>(DeleteButtonId);
        _restore = Find<UiButton>(RestoreButtonId);
        Find<UiButton>(CreditsButtonId);
        Find<UiButton>(ExitButtonId);
        var events = Session.Events;
        _subscriptions.Add(events.Subscribe<CharacterListChanged>(OnListChanged));
        _subscriptions.Add(events.Subscribe<CharacterCreateResult>(OnCreateResult));
        _subscriptions.Add(events.Subscribe<PlayerCreated>(OnPlayerCreated));
        UpdateWorldName();
        _selectedGuid = Session.State.Characters.SelectedGuid;
        if (ListReceived)
        {
            RebuildCharacterList();
        }
        else
        {
            UpdateButtons();
        }

        Manager.Input.RegisterInputMap(InputMap, this, InputMapPriority);
    }

    /// <summary>The character list box, or null when the layout lacks it.</summary>
    public UiListBox? List => _list;

    /// <summary>The character selected (<c>m_iidSelectedCharacter</c>), or 0.</summary>
    public uint SelectedGuid => _selectedGuid;

    /// <summary>The Enter button.</summary>
    public UiButton? EnterButton => _enter;

    /// <summary>The Delete button.</summary>
    public UiButton? DeleteButton => _delete;

    /// <summary>The Restore button.</summary>
    public UiButton? RestoreButton => _restore;

    /// <summary>The Create button.</summary>
    public UiButton? CreateButton => _create;

    /// <summary>The context of the open error dialog, or 0.</summary>
    public uint ErrorDialog => _errorContext;

    /// <summary>The context of the open leave-confirmation dialog, or 0.</summary>
    public uint ConfirmExitDialog => _confirmExitContext;

    /// <summary>The context of the open delete-confirmation dialog, or 0.</summary>
    public uint DeleteDialog => _deleteContext;

    /// <summary>The context of the open "Please Wait" dialog, or 0.</summary>
    public uint PleaseWaitDialog => _pleaseWaitContext;

    /// <summary>The context of the open "Entering World" dialog, or 0.</summary>
    public uint EnteringWorldDialog => _enteringWorldContext;

    /// <summary>True once the session has delivered its character list (retail's persistent data flag).</summary>
    private bool ListReceived => Session.State.Characters.Version > 0;

    // ---------------------------------------------------------------- Frame and events

    /// <summary>
    /// <c>gmCharacterManagementUI::Update</c>, run when the character list changed: the world's name and the list are built again and the
    /// "Please Wait" dialog closes.
    /// </summary>
    public override void Update()
    {
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        if (ListReceived)
        {
            UpdateWorldName();
            RebuildCharacterList();
            CloseDialog(ref _pleaseWaitContext);
        }
    }

    private void OnListChanged(in CharacterListChanged e)
    {
        // The delete acknowledgement changes nothing in the list yet: the F658 that follows it does.
        if (e.Reason != CharacterListReason.DeleteAcknowledged)
        {
            _dirty = true;
        }
    }

    private void OnCreateResult(in CharacterCreateResult e)
    {
        if (e.Succeeded)
        {
            return;
        }

        // RecvNotice_CharGenVerificationResponse: the wait dialog closes whatever the result, and three results are said.
        CloseDialog(ref _pleaseWaitContext);
        if (CharacterManagementRules.VerificationToken(e.ResultCode) is { } token)
        {
            MakeErrorMessageDialog(Pregame(token));
        }
    }

    private void OnPlayerCreated(in PlayerCreated e) => Context.Flow.QueueUIModeAfterAFrame(UiModes.GamePlay); // RecvNotice_BeginEnterWorld; one frame later than retail, so the shell's construction is not in the arrival's frame (UiFlow.QueueUIModeAfterAFrame)

    /// <inheritdoc/>
    public override void DisplayError(in UiStringRef text) => MakeErrorMessageDialog(text);

    // ---------------------------------------------------------------- The list

    private void UpdateWorldName()
    {
        if (Root?.GetChildRecursive(WorldNameId) is UiTextElement world)
        {
            world.SetText(Session.State.WorldName ?? string.Empty);
        }
    }

    private void RebuildCharacterList()
    {
        if (_list is null)
        {
            return;
        }

        var previous = _selectedGuid;
        ResetPreviouslySelectedCharacterSlot();
        _list.Flush();
        var characters = Session.State.Characters;
        var entries = characters.Entries;
        var rowHeight = CharacterManagementRules.RowHeight(_list.Box.Height, entries.Count, characters.SlotCount);
        UiElement? previousEntry = null;
        var rows = new List<UiElement>(entries.Count);
        foreach (var slot in entries)
        {
            var row = _list.AddItemFromTemplateList(0);
            if (row is not UiTextElement text)
            {
                Manager.LogOnce("charman:row", LogSeverity.Error, $"gmCharacterManagementUI: the list has no usable row template (element 0x{RowTemplateId:X8}); the characters are not listed");
                break;
            }

            row.ResizeTo(row.Box.Width, rowHeight);
            Manager.LogOnce("charman:row-virtual", LogSeverity.Warning, "TODO(retailui-runtime): gmCharacterManagementUI::RebuildCharacterList makes an extra virtual call on each row; Lifestone takes it to be SetMouseVisible(true), not confirmed from the 11.6096 bytes (placeholder)");
            row.SetMouseVisible(true);
            row.SetWantsDoubleClicks(true);
            row.SetProperty(new UiInstanceIdProperty(InstanceIdProperty, slot.Guid));
            text.SetText(slot.Name);
            if (slot.PendingDeletion)
            {
                row.SetProperty(new UiArrayProperty(UiPropertyIds.TextFontColors, [new UiColorProperty(UiPropertyIds.TextFontColor, PendingDeletionColor)]));
            }

            if (slot.Guid == previous)
            {
                previousEntry = row;
            }

            rows.Add(row);
        }

        // The default is chosen in server order, then the rows are sorted: by name, the characters pending deletion last.
        var defaultIndex = CharacterManagementRules.DefaultSelection(entries);
        var defaultEntry = defaultIndex >= 0 && defaultIndex < rows.Count ? rows[defaultIndex] : null;
        if (rows.Count == entries.Count)
        {
            var order = CharacterManagementRules.DisplayOrder(entries);
            for (var target = 0; target < order.Count; target++)
            {
                var row = rows[order[target]];
                var at = RowIndex(row);
                if (at != target)
                {
                    _list.RemoveItem(at);
                    _list.InsertItem(row, target);
                }
            }
        }

        _list.SetSelectedItem(previousEntry ?? defaultEntry, broadcast: true);
        UpdateButtons();
    }

    private int RowIndex(UiElement row)
    {
        for (var i = 0; i < _list!.Items.Count; i++)
        {
            if (ReferenceEquals(_list.Items[i], row))
            {
                return i;
            }
        }

        return -1;
    }

    private void ResetPreviouslySelectedCharacterSlot()
    {
        if (_list?.GetItem(_selectedItemIndex) is { } item && InstanceIdOf(item) == _selectedGuid)
        {
            item.SetProperty(new UiBoolProperty(UiPropertyIds.ButtonHighlighted, false));
        }

        SelectInSession(0);
        _selectedGuid = 0;
        _selectedItemIndex = -1;
    }

    private void SelectCharacter(uint guid)
    {
        if (_selectedGuid == guid || _list is null)
        {
            return;
        }

        ResetPreviouslySelectedCharacterSlot();
        if (guid == 0)
        {
            return;
        }

        for (var i = 0; i < _list.Items.Count; i++)
        {
            if (InstanceIdOf(_list.Items[i]) == guid)
            {
                _selectedGuid = guid;
                SelectInSession(guid);
                _selectedItemIndex = i;
                return;
            }
        }
    }

    /// <summary>The session's own selection (retail's persistent data), kept equal to the screen's: a local action, no message.</summary>
    private void SelectInSession(uint guid)
    {
        if (Session.State.Characters.SelectedGuid != guid)
        {
            Session.Actions.SelectCharacter(guid, ActionSource.Ui);
        }
    }

    private static uint InstanceIdOf(UiElement item) =>
        item.InqProperty(InstanceIdProperty, out var property) && property is UiInstanceIdProperty id ? id.Value : 0u;

    private bool IsPendingDeletion(uint guid)
    {
        var characters = Session.State.Characters;
        var index = guid == 0 ? -1 : characters.IndexOf(guid);
        return index >= 0 && characters.Entries[index].PendingDeletion;
    }

    private void UpdateButtons()
    {
        var characters = Session.State.Characters;
        var selectedIndex = _list?.SelectedIndex ?? -1;
        var states = CharacterManagementRules.Buttons(selectedIndex, IsPendingDeletion(_selectedGuid), characters.Entries.Count, characters.SlotCount);
        _enter?.SetState(states.Enter);
        Manager.LogOnce("charman:delete-state", LogSeverity.Warning, "TODO(retailui-runtime): gmCharacterManagementUI::UpdateButtons passes an argument for the Delete button's state that is lost in the 11.4186 decompile; Lifestone takes it as Normal (the button is always usable), to confirm in the 11.6096 bytes (placeholder)");
        _delete?.SetState(UiStateIds.Normal);
        _delete?.SetVisible(states.DeleteVisible);
        _restore?.SetVisible(states.RestoreVisible);
        _create?.SetState(states.Create);
    }

    // ---------------------------------------------------------------- Messages

    /// <inheritdoc/>
    protected override UiListenResult OnElementMessage(in UiElementMessage message)
    {
        switch (message.ElementId)
        {
            case ListId when message.MessageId == UiMessageIds.ListBoxSelection:
                ResetPreviouslySelectedCharacterSlot();
                uint guid = 0;
                if (message.Item is { } item)
                {
                    item.SetProperty(new UiBoolProperty(UiPropertyIds.ButtonHighlighted, true));
                    guid = InstanceIdOf(item);
                }

                SelectCharacter(guid);
                UpdateButtons();
                break;
            case EnterButtonId when message.MessageId == UiMessageIds.ButtonClick:
                EnterGame();
                break;
            case RowTemplateId when message.MessageId == UiMessageIds.DoubleClick:
                EnterGame();
                break;
            case DeleteButtonId when message.MessageId == UiMessageIds.ButtonClick && _list?.SelectedItem is not null:
                MakeDeleteCharacterConfirmationDialog();
                break;
            case RestoreButtonId when message.MessageId == UiMessageIds.ButtonClick && _list?.SelectedItem is { } selected:
                var restore = InstanceIdOf(selected);
                MakePleaseWaitDialog();
                Track(Session.Actions.RestoreCharacter(restore, ActionSource.Ui), "restore");
                break;
            case CreditsButtonId when message.MessageId == UiMessageIds.ButtonClick:
                // Retail queues the credits mode; M2 answers with a "not in M2" dialog (D38).
                NotInM2.Show(Manager.Dialogs, Log, "Credits");
                break;
            case ExitButtonId when message.MessageId == UiMessageIds.ButtonClick:
                MakeConfirmExitDialog();
                break;
            case CreateButtonId when message.MessageId == UiMessageIds.ButtonClick:
                Context.Flow.QueueUIMode(UiModes.CharGen);
                break;
        }

        return UiListenResult.Default;
    }

    /// <summary><c>gmCharacterManagementUI::OnAction</c> (input map 9): the leave action asks to confirm leaving.</summary>
    public bool OnInputAction(uint inputMapId, uint actionId, RetailActivation activation)
    {
        if (actionId != LeaveAction)
        {
            return false;
        }

        if (activation.Start)
        {
            MakeConfirmExitDialog();
        }

        return true;
    }

    // ---------------------------------------------------------------- Actions

    /// <summary><c>gmCharacterManagementUI::EnterGame</c>: a selected character that is not pending deletion gets the "Entering World" dialog and the Enter action.</summary>
    private void EnterGame()
    {
        if (_selectedGuid == 0 || IsPendingDeletion(_selectedGuid))
        {
            return;
        }

        MakeEnteringWorldDialog();
        Track(Session.Actions.EnterWorld(_selectedGuid, ActionSource.Ui), "entering the world");
    }

    /// <summary>
    /// Follows an action to its end. A server error the flow routes (a character error changes the mode) or a dialog says (a refused restore) is theirs,
    /// so anything else that failed closes the wait dialogs and says why, in Lifestone's own words.
    /// </summary>
    private void Track(ActionHandle handle, string what)
    {
        if (handle.IsCompleted)
        {
            OnActionDone(handle, what);
        }
        else
        {
            handle.Completed += h => OnActionDone(h, what);
        }
    }

    private void OnActionDone(ActionHandle handle, string what)
    {
        if (IsDisposed || handle.Outcome is not { } outcome || outcome.Succeeded)
        {
            return;
        }

        CloseDialog(ref _pleaseWaitContext);
        CloseDialog(ref _enteringWorldContext);
        if (outcome.Kind == ActionOutcomeKind.ServerError && (handle.Kind == ActionKind.RestoreCharacter || GmUiFlow.Routes.ContainsKey(outcome.Code)))
        {
            // A refused restore (F643) was said by the verification handler; a character error (F659) the flow routes was shown by the
            // screen it queued. Any other server error is not retail's to say, so it is said below.
            return;
        }

        var offline = Session.State.IsOffline && outcome.Kind == ActionOutcomeKind.Refused;
        var text = offline
            ? $"{NotInM2.Prefix}{what} without a server (this is the offline stand-in session)"
            : $"Lifestone: {what} did not go through ({outcome.Kind}{(string.IsNullOrEmpty(outcome.Detail) ? string.Empty : ": " + outcome.Detail)})";
        Log.Warn(UiElementManager.Category, $"placeholder: {text}");
        MakeErrorMessageDialog(UiStringRef.FromPlainText(text));
    }

    // ---------------------------------------------------------------- Dialogs

    private bool MakeErrorMessageDialog(in UiStringRef error)
    {
        if (_errorContext != 0)
        {
            return false;
        }

        var data = UiDialogData.Of(UiDialogType.Message, error).WithFlag(UiPropertyIds.DialogModal, true);
        _errorContext = Manager.Dialogs.MakeCallbackDialog(data, _ => _errorContext = 0);
        return true;
    }

    private bool MakeDeleteCharacterConfirmationDialog()
    {
        if (_deleteContext != 0)
        {
            return false;
        }

        var characters = Session.State.Characters;
        var index = _selectedGuid == 0 ? -1 : characters.IndexOf(_selectedGuid);
        var name = index >= 0 ? characters.Entries[index].Name : string.Empty;
        var data = UiDialogData.Of(UiDialogType.ConfirmationTextInput, Pregame("ID_CharacterManagement_DeleteCharacterConfirmation"))
            .WithFlag(UiPropertyIds.DialogModal, true)
            .WithTextVariable(DeleteNameVariable, name);
        _deleteContext = Manager.Dialogs.MakeCallbackDialog(data, CloseDeleteCharacterDialog);
        return true;
    }

    private void CloseDeleteCharacterDialog(UiDialogData data)
    {
        _deleteContext = 0;
        if (IsDisposed || Manager.Services.Strings is not { } strings)
        {
            return;
        }

        var reference = Pregame("ID_CharacterManagement_DeleteCharacterResponse");
        if (!strings.TryGetString(reference, out var expected) || !CharacterManagementRules.IsDeleteConfirmed(data.TextResponse, expected))
        {
            return;
        }

        // The character to delete is the one selected now (retail reads the persistent selection, not the one the dialog opened with).
        var guid = _selectedGuid;
        MakePleaseWaitDialog();
        Track(Session.Actions.DeleteCharacter(guid, ActionSource.Ui), "deleting the character");
    }

    private bool MakePleaseWaitDialog()
    {
        if (_pleaseWaitContext != 0)
        {
            return false;
        }

        var data = UiDialogData.Of(UiDialogType.Wait, Pregame("ID_CharacterManagement_PleaseWait")).WithFlag(UiPropertyIds.DialogModal, true);
        _pleaseWaitContext = Manager.Dialogs.MakeCallbackDialog(data, _ => _pleaseWaitContext = 0);
        return true;
    }

    private bool MakeEnteringWorldDialog()
    {
        if (_enteringWorldContext != 0)
        {
            return false;
        }

        var data = UiDialogData.Of(UiDialogType.Wait, Pregame("ID_Character_EnteringWorld")).WithFlag(UiPropertyIds.DialogModal, true);
        _enteringWorldContext = Manager.Dialogs.MakeCallbackDialog(data, _ => _enteringWorldContext = 0);
        return true;
    }

    private bool MakeConfirmExitDialog()
    {
        if (_confirmExitContext != 0)
        {
            return false;
        }

        var data = UiDialogData.Of(UiDialogType.Confirmation, Pregame("ID_CharacterManagement_ConfirmExit")).WithFlag(UiPropertyIds.DialogModal, true);
        _confirmExitContext = Manager.Dialogs.MakeCallbackDialog(data, answer =>
        {
            _confirmExitContext = 0;
            if (!IsDisposed && answer.ConfirmationResponse == true)
            {
                Context.Flow.QueueUIMode(UiModes.Epilogue);
            }
        });
        return true;
    }

    private void CloseDialog(ref uint context)
    {
        var id = context;
        context = 0;
        Manager.Dialogs.CloseDialog(id);
    }

    /// <inheritdoc/>
    protected override void OnDisposing()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        Manager.Input.UnregisterInputMap(InputMap, this);
        CloseDialog(ref _errorContext);
        CloseDialog(ref _confirmExitContext);
        CloseDialog(ref _deleteContext);
        CloseDialog(ref _pleaseWaitContext);
        CloseDialog(ref _enteringWorldContext);
    }
}
