using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Play;

// 11.4186 addresses. Cited at 11.6096 in the comments below because this file was re-expressed against the 11.6096 symbol-table pin; the same
// functions in 11.4186 (the decompile symbols) are at: gmGamePlayUI::HandleKeyPress 0x004E9DF0, ::MakeLogoutConfirmationDialog 0x004EB6A0.
/// <summary>
/// Retail's log-off key (m2-plan G6 W11): <c>gmDefaultMap</c> (KEYMAP <c>gmDefault</c>, MasterInputMap 0x14000000) binds the action
/// "End Character Session" (action 0x10000026 of input map 0x10000009, the map that holds Escape's action 0x27) to Shift+Escape. In the world the action calls
/// <see cref="IActionApi.LogOff"/> and nothing else: the Action API prints "Logging off...", sends F653 and publishes <c>LogOffRequested</c>, and the portal
/// space bridge plays retail's log-off transition. Out of the world it does nothing.
/// </summary>
/// <remarks>
/// <para><b>Retail-exact, no confirmation</b> (closes <c>TODO(g6-logoff-confirm)</c>): <c>gmGamePlayUI::HandleKeyPress</c> (11.6096 0x004EAA80) sets the request, the confirmation and the end together for this action, so the key
/// logs off at once; the confirmation (<c>ID_Client_EndCharacterSessionConfirm</c>, <c>MakeLogoutConfirmationDialog</c> 0x004EC330) belongs to the notice path, which the options panel's "Exit to Character Selection" button
/// takes (<c>GameLogout</c>, docs/retail-ui.md section 21.8). Retail's end step refuses in the air ("Cannot log off while in mid-air."); that refusal is NOT built (<c>TODO(g6-logoff-midair)</c>, logged once with the first log off): the
/// key and the panel's Yes log off whatever the body is doing, because the UI does not read the player body's airborne state yet. Closing the window still quits.</para>
/// <para>Register it on the retail UI's router: <c>router.RegisterInputMap(EndSessionKey.InputMap, key, priority)</c>. Game thread only.</para>
/// </remarks>
public sealed class EndSessionKey : IInputActionSink
{
    /// <summary>The input map of the gameplay actions (<c>gmDefaultMap</c>'s map that holds Escape, action 0x27).</summary>
    public const uint InputMap = 0x10000009;

    /// <summary>"End Character Session", bound to Shift+Escape in <c>gmDefaultMap</c>.</summary>
    public const uint Action = 0x10000026;

    private readonly IGameSessionView _session;
    private readonly ILogSink _log;
    private bool _loggedMidair;

    /// <summary>The key's handler for <paramref name="session"/>.</summary>
    public EndSessionKey(IGameSessionView session, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(log);
        _session = session;
        _log = log;
    }

    /// <summary>Log offs the key has asked for.</summary>
    public int Requests { get; private set; }

    /// <summary>The handle of the last log off the key asked for, or null.</summary>
    public ActionHandle? Last { get; private set; }

    /// <inheritdoc/>
    public bool OnInputAction(uint inputMapId, uint actionId, RetailActivation activation)
    {
        if (inputMapId != InputMap || actionId != Action)
        {
            return false;
        }

        if (_session.State.Phase != Lifestone.Net.Session.SessionPhase.InWorld)
        {
            return false; // no character in the world (the login screens): the key is not ours
        }

        if (!activation.Start)
        {
            return true; // the key's release
        }

        Requests++;
        if (!_loggedMidair)
        {
            _loggedMidair = true;
            _log.Warn("play", "TODO(g6-logoff-midair): retail refuses to log off in the air (\"Cannot log off while in mid-air.\"); Lifestone logs off whatever the body is doing, as the UI cannot read the airborne state yet");
        }

        Last = _session.Actions.LogOff(ActionSource.Client);
        _log.Info("play", "the log-off key (Shift+Escape, End Character Session) logs off without asking, as gmGamePlayUI::HandleKeyPress (0x004EAA80) does");
        return true;
    }
}
