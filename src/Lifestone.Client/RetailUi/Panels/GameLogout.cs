using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

// 11.4186 addresses. Cited at 11.6096 in the comments below because this file was re-expressed against the 11.6096 symbol-table pin; the same
// functions in 11.4186 (the decompile symbols) are at: gmGamePlayUI::HandleKeyPress 0x004E9DF0, ::MakeLogoutConfirmationDialog 0x004EB6A0,
// ::RecvNotice_EndCharacterSession 0x004EBEA0, ::UseTime 0x004EA3A0, ::RecvNotice_CloseDialog 0x004EA4F0; CPlayerSystem::LogOffCharacter 0x00563520.
/// <summary>
/// Logging out through the shell (docs/retail-ui.md section 21.8). The options panel's "Exit to Character Selection" button (0x10000203) sends <c>CM_UI::SendNotice_EndCharacterSession(1)</c>;
/// <c>gmGamePlayUI::RecvNotice_EndCharacterSession</c> (11.6096 0x004ECB30) answers with <c>MakeLogoutConfirmationDialog</c> (0x004EC330) and the string <c>ID_Client_EndCharacterSessionConfirm</c>
/// (table enum 0x10000001): "This will exit your character from the game world. Are you sure?". The dialog is a type 1 confirmation (property 0x8E = 1) in the immediate queue (property 0xC3 = 1) with the
/// text in property 0xC5; it refuses to open a second one while one is open. Yes is <c>RecvNotice_CloseDialog</c> (0x004EB180) setting "confirmed", and the next <c>gmGamePlayUI::UseTime</c> (0x004EB030) ends
/// the session (<c>CPlayerSystem::LogOffCharacter</c>, 0x005642C0). Here Yes calls <c>Actions.LogOff</c>; No does nothing. Retail's own log-off key (Shift+Escape, <c>EndSessionKey</c>) skips the dialog
/// (<c>gmGamePlayUI::HandleKeyPress</c>, 0x004EAA80, sets the request, the confirmation and the end together). The in-the-air refusal ("Cannot log off while in mid-air.") belongs to the end step and is
/// NOT built: Yes logs off whatever the body is doing (<c>TODO(g6-logoff-midair)</c>, logged once with the first Yes).
/// </summary>
public sealed class GameLogout
{
    /// <summary>The string table enum (0x10000001) of <c>ID_Client_EndCharacterSessionConfirm</c>.</summary>
    public const uint StringTableEnum = 0x10000001;

    private readonly UiLoginContext _context;
    private uint _dialog;

    /// <summary>The logout path of <paramref name="context"/>'s session.</summary>
    public GameLogout(UiLoginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>True while the confirmation is open (<c>m_logoutContext</c> is not 0).</summary>
    public bool IsOpen => _dialog != 0;

    /// <summary>How many log offs Yes asked for.</summary>
    public int Confirmed { get; private set; }

    /// <summary>The handle of the last log off Yes asked for, or null.</summary>
    public ActionHandle? Last { get; private set; }

    /// <summary>How many times Yes asked to leave the game (<see cref="RequestExitGame"/>).</summary>
    public int ExitConfirmed { get; private set; }

    /// <summary><c>RecvNotice_EndCharacterSession</c>: opens the confirmation. False when one is open already.</summary>
    public bool RequestEndCharacterSession() => Ask(exitGame: false);

    /// <summary>
    /// The options panel's "Exit Game": the same confirmation, and Yes queues the epilogue (<c>UiModes.Epilogue</c>), which ends the live session (Quit)
    /// and closes the application, where "Exit to Character Selection" logs off. Retail's own confirmation for this button is not read
    /// (<c>TODO(g6-exit-game-confirm)</c>, logged once): the text is the log-off question's, as the plan (dod-3-4-7) asks. False when a confirmation is open already.
    /// </summary>
    public bool RequestExitGame()
    {
        _context.Manager.LogOnce(
            "logout:exit-game-confirm",
            LogSeverity.Warning,
            "TODO(g6-exit-game-confirm): Exit Game asks the log-off confirmation (ID_Client_EndCharacterSessionConfirm) and then queues the epilogue; retail's own handler for the button (gmGameplayOptionsUI::ListenToElementMessage, 0x0049E390) is not read for its confirmation");
        return Ask(exitGame: true);
    }

    private bool Ask(bool exitGame)
    {
        var log = _context.Manager.Services.Log;
        var strings = _context.Manager.Services.Strings;
        if (_dialog != 0)
        {
            return false;
        }

        if (strings is null || !strings.TryReference(StringTableEnum, "ID_Client_EndCharacterSessionConfirm", out var text))
        {
            log.Error("retailui", "gmGamePlayUI::MakeLogoutConfirmationDialog: ID_Client_EndCharacterSessionConfirm is not in the string tables; the log off is not offered (TODO(g6-logout-string))");
            return false;
        }

        var data = UiDialogData.Of(UiDialogType.Confirmation, text)
            .WithQueue(DialogFactory.ImmediateQueue) // property 0xC3 = 1 (the immediate queue)
            .WithFlag(UiPropertyIds.DialogModal, true); // the base UI's gameplay-modal state
        _dialog = _context.Manager.Dialogs.MakeCallbackDialog(data, answer =>
        {
            _dialog = 0;
            if (answer.ConfirmationResponse == true && exitGame)
            {
                ExitConfirmed++;
                log.Info("retailui", "the exit-game confirmation was answered Yes: the epilogue closes the application");
                _context.Flow.QueueUIMode(UiModes.Epilogue);
            }
            else if (answer.ConfirmationResponse == true)
            {
                Confirmed++;
                _context.Manager.LogOnce(
                    "logoff:midair",
                    LogSeverity.Warning,
                    "TODO(g6-logoff-midair): retail refuses to log off in the air (\"Cannot log off while in mid-air.\"); Lifestone logs off whatever the body is doing, as the UI cannot read the airborne state yet");
                Last = _context.Session.Actions.LogOff(ActionSource.Client);
                log.Info("retailui", "the logout confirmation was answered Yes: logging off");
            }
        });
        return _dialog != 0;
    }
}
