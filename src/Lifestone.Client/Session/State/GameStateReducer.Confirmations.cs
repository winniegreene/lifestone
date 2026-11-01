using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Recording;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.State;

/// <summary>
/// The server's yes-or-no questions (M5 6 w2; DoD 11): <see cref="GameState.PendingConfirmation"/> from GameEvent 0x0274 CharacterConfirmationRequest, closed by
/// GameEvent 0x0276 CharacterConfirmationDone, by the player's answer (GameAction 0x0275, sent by <see cref="IActionApi.RespondToConfirmation"/>) and by the world exit.
/// </summary>
/// <remarks>
/// <para>Retail (11.6096): <c>CM_Character::DispatchUI_ConfirmationRequest</c> 0x006A3BB0 and <c>CM_Character::DispatchUI_ConfirmationDone</c> 0x006A1C40 hand the two
/// events to the UI (<c>CM_UI::Event_ConfirmationRequest</c>, address unknown, opens the dialog); the answer is <c>CM_Character::Event_ConfirmationResponse</c> 0x006A2030.</para>
/// <para>ACE's rule (@95ef426d <c>WorldObjects/Managers/ConfirmationManager.cs</c>): the open questions are a dictionary keyed by the type (:16), so at most one of
/// each type is open; a second request of an open type is refused before it is sent (:36-48); each times out after 30 s (:20, :39-42), and only then does ACE send
/// 0x0276, and only when the context still matches (:57-61); an answer removes it without a 0x0276 (:89-135). For the types 2, 3, 5, 6 and 7 the timeout's 0x0276 does not remove it (:57-84: it triggers a response from the client), so the read model keeps the closed question in <see cref="GameState.ClosedAwaitingReply"/> until the answer is sent (6-R1). The read model keeps the same shape: one per type,
/// a 0x0276 closes the one whose type and context match, the answer closes it locally at once. ACE notes that retail's fellowship dialog does not close on
/// 0x0276 (:98-99); the read model closes it anyway (a question the server has withdrawn cannot be answered usefully: ACE then says the offer expired), and the
/// dialog (slice u1) follows the read model.</para>
/// </remarks>
internal sealed partial class GameStateReducer
{
    /// <summary>GameEvent 0x0274 CharacterConfirmationRequest: the question opens (a second of an open type replaces it, logged).</summary>
    public void OnConfirmationRequest(in InboundContext context, in GameEventHeader header, ConfirmationRequestEvent message)
    {
        var list = _state.MutableConfirmations;
        var at = IndexOfConfirmation(message.Type);
        PendingConfirmation? replaced = null;
        if (at >= 0)
        {
            replaced = list[at];
            list.RemoveAt(at);
            _log.Warn(
                SessionLog.Category,
                "a second confirmation request of an open type replaced the first (ACE refuses that before sending it: ConfirmationManager.cs:36-48)",
                new LogField("type", message.Type),
                new LogField("old_context", replaced.Context),
                new LogField("context", message.Context));
        }

        var pending = new PendingConfirmation(message.Type, message.Context, message.Text ?? string.Empty, _clock.Now);
        list.Add(pending);
        Changed();
        _log.Info(SessionLog.Category, "the server asked a confirmation", new LogField("type", message.Kind), new LogField("context", message.Context));
        if (replaced is not null)
        {
            Publish(new ConfirmationClosed(replaced.Type, replaced.Context, ConfirmationCloseReason.Replaced, null));
        }

        Publish(new ConfirmationRequested(pending.Type, pending.Context, pending.Text));
    }

    /// <summary>GameEvent 0x0276 CharacterConfirmationDone: the open question of that type and context closes; one that is not open (answered already, or another context) is ignored.</summary>
    public void OnConfirmationDone(in InboundContext context, in GameEventHeader header, in ConfirmationDoneEvent message)
    {
        var at = IndexOfConfirmation(message.Type);
        if (at < 0 || _state.MutableConfirmations[at].Context != message.Context)
        {
            _log.Info(SessionLog.Category, "a confirmation done for no open question: ignored", new LogField("type", message.Type), new LogField("context", message.Context));
            return;
        }

        if (GameState.ServerKeepsSlotAfterDone(message.Type))
        {
            // ACE keeps this type's slot until the client's 0x0275 (6-R1): the reply is still due, so the tracker accepts it after the close.
            var held = _state.MutableClosedAwaitingReply;
            var doneType = message.Type;
            held.RemoveAll(h => h.Type == doneType);
            held.Add((message.Type, message.Context));
        }

        CloseConfirmation(at, ConfirmationCloseReason.ServerDone, null);
    }

    /// <summary>The player's answer (<see cref="SessionNotices.ConfirmationAnswered"/>: value the type, text "context:1" or "context:0"): the question closes when the answer is sent.</summary>
    private void ApplyConfirmationAnswered(in RecordingNotice notice)
    {
        var text = notice.Text ?? string.Empty;
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon != text.Length - 2 || text[^1] is not ('0' or '1')
            || !uint.TryParse(text.AsSpan(0, colon), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var contextWord))
        {
            throw new InvalidDataException($"local notice {SessionNotices.ConfirmationAnswered} (confirmation answered) has a bad payload: '{text}' (want 'context:1' or 'context:0')");
        }

        var answeredType = notice.Value;
        _state.MutableClosedAwaitingReply.RemoveAll(h => h.Type == answeredType && h.Context == contextWord);
        var at = IndexOfConfirmation(notice.Value);
        if (at < 0 || _state.MutableConfirmations[at].Context != contextWord)
        {
            // An answer to a question that is not open (it timed out, or the caller answered a context of its own): sent, nothing to close.
            return;
        }

        CloseConfirmation(at, ConfirmationCloseReason.Answered, text[^1] == '1');
    }

    /// <summary>The world exit closes every open question (called from <c>LeaveWorld</c>).</summary>
    private void CloseConfirmationsOnWorldExit()
    {
        _state.MutableClosedAwaitingReply.Clear();
        var list = _state.MutableConfirmations;
        if (list.Count == 0)
        {
            return;
        }

        var closed = list.ToArray();
        list.Clear();
        Changed();
        foreach (var c in closed)
        {
            Publish(new ConfirmationClosed(c.Type, c.Context, ConfirmationCloseReason.WorldExit, null));
        }
    }

    private void CloseConfirmation(int at, ConfirmationCloseReason reason, bool? accepted)
    {
        var list = _state.MutableConfirmations;
        var closed = list[at];
        list.RemoveAt(at);
        Changed();
        Publish(new ConfirmationClosed(closed.Type, closed.Context, reason, accepted));
    }

    private int IndexOfConfirmation(uint type)
    {
        var list = _state.MutableConfirmations;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Type == type)
            {
                return i;
            }
        }

        return -1;
    }
}
