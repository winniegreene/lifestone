using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Dialogs;

/// <summary>
/// The server's Yes/No question as a dialog (docs/retail-ui.md section 21.8; M5 6 u1). ACE asks with GameEvent 0x0274 (<see cref="ConfirmationRequested"/>: the type word, the context word and the
/// text) and withdraws or times out the question with 0x0276 (<see cref="ConfirmationClosed"/>). Retail's path is <c>CM_Character::DispatchUI_ConfirmationRequest</c> (11.6096 0x006A3BB0) to
/// <c>CM_UI::Event_ConfirmationRequest</c> (address unknown: not in the symbol table), then <c>ClientUISystem::Handle_Character__ConfirmationRequest</c> (0x00564E40), which sends a notice by type to
/// <c>gmGamePlayUI::RecvNotice_AlterSkill_ConfirmationRequest</c> (0x004EC700, type 2), <c>RecvNotice_AlterAttribute_ConfirmationRequest</c> (0x004EC7E0, type 3), <c>RecvNotice_Augmentation_ConfirmationRequest</c>
/// (0x004EC8C0, type 6), <c>RecvNotice_YesNo_ConfirmationRequest</c> (0x004EC9A0, type 7) or <c>RecvNotice_CraftInteraction_ConfirmationRequest</c> (0x004ECA50, type 5); each remembers the type and context and calls
/// <c>MakeGameplayConfirmationDialog</c>: a type 1 confirmation (property 0x8E = 1), modal (0xAC), in the default queue, with the server's text as a literal (0xC5). Types 2, 3, 5 and 6 add " Continue?" to
/// the text, type 7 shows it as it is. <c>RecvNotice_AbortConfirmationRequest</c> (0x004EAA00) closes the dialog when 0x0276 names the remembered type and context. Yes or No sends the answer once through
/// <see cref="IActionApi.RespondToConfirmation"/> (<see cref="ActionSource.Ui"/>); a question the server closed closes its dialog; when it was a done (0x0276) of a type ACE keeps until an answer (2, 3, 5, 6, 7) a No is sent for it (6-R1), and a replaced one or the world exit sends nothing.
/// <para>
/// <b>Two questions.</b> ACE keeps one question per type (<see cref="GameState.PendingConfirmations"/>), so two of different types can be open together. The dialogs queue (the default queue shows one
/// and the next opens when it closes). Retail's <c>MakeGameplayConfirmationDialog</c> instead refuses while one is open (its context field is not 0) after its handler has already overwritten the
/// remembered type and context, so the second question is lost and the first dialog's answer goes to the second question. That is a glitch: the default is the queue, and
/// <see cref="UiRetailGlitches.ConfirmationSingleSlot"/> (<c>--retail-confirm-slot</c>) restores retail's drop (the stale slot is not reproduced; the dropped question times out at the server).
/// </para>
/// <para>
/// <b>Not built.</b> Types 1 (swear allegiance) and 4 (fellowship) go to the allegiance and fellowship windows in retail (<c>CM_Allegiance::SendNotice_SwearAllegianceRequest</c>, <c>CM_Fellowship::
/// SendNotice_FellowshipRequest</c>), which Lifestone has not built: they get the same Yes/No dialog with the server's text (<c>TODO(g6-confirm-social)</c>, logged once). The 30 s countdown the server
/// runs is not shown (retail's dialog has none either).
/// </para>
/// </summary>
public sealed class ServerConfirmation : IDisposable
{
    /// <summary>What retail appends to the server's text for types 2, 3, 5 and 6.</summary>
    public const string ContinueSuffix = " Continue?";

    private readonly UiElementManager _manager;
    private readonly IGameSessionView _session;
    private readonly List<Shown> _shown = [];
    private readonly IDisposable _requested;
    private readonly IDisposable _closed;
    private readonly bool _retailSingleSlot;

    /// <summary>
    /// The dialogs for <paramref name="session"/>'s questions; shows the ones already open. <paramref name="retailSingleSlot"/> overrides the services' <see cref="UiRetailGlitches.ConfirmationSingleSlot"/>
    /// (null reads it).
    /// </summary>
    public ServerConfirmation(UiElementManager manager, IGameSessionView session, bool? retailSingleSlot = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(session);
        _manager = manager;
        _session = session;
        _retailSingleSlot = retailSingleSlot ?? (manager.Services.RetailGlitches & UiRetailGlitches.ConfirmationSingleSlot) != 0;
        _requested = session.Events.Subscribe<ConfirmationRequested>(OnRequested);
        _closed = session.Events.Subscribe<ConfirmationClosed>(OnClosed);
        foreach (var open in session.State.PendingConfirmations.ToList())
        {
            Show(open.Type, open.Context, open.Text);
        }
    }

    /// <summary>How many dialogs are open or waiting for an answer.</summary>
    public int Open => _shown.Count;

    /// <summary>How many answers the player gave (Yes or No sent).</summary>
    public int Answered { get; private set; }

    /// <summary>How many No replies were sent for questions the server closed (6-R1: ACE keeps the slot of types 2, 3, 5, 6 and 7 until an answer).</summary>
    public int ServerClosedReplies { get; private set; }

    /// <summary>The contexts of the questions with a dialog, oldest first.</summary>
    public IReadOnlyList<(uint Type, uint Context)> Questions => [.. _shown.Select(static s => (s.Type, s.Context))];

    /// <summary>The text a question shows (<see cref="ContinueSuffix"/> added for the types that retail adds it to).</summary>
    public static string TextOf(uint type, string text) => type is 2 or 3 or 5 or 6 ? text + ContinueSuffix : text;

    /// <inheritdoc/>
    public void Dispose()
    {
        _requested.Dispose();
        _closed.Dispose();
        foreach (var shown in _shown.ToList())
        {
            shown.Withdrawn = true;
            _manager.Dialogs.CloseDialog(shown.Dialog);
        }

        _shown.Clear();
    }

    private void OnRequested(in ConfirmationRequested request) => Show(request.Type, request.Context, request.Text);

    private void OnClosed(in ConfirmationClosed closed)
    {
        var (type, context, reason) = (closed.Type, closed.Context, closed.Reason);
        if (reason == ConfirmationCloseReason.ServerDone && GameState.ServerKeepsSlotAfterDone(type))
        {
            // 6-R1: ACE's timeout sends the 0x0276 and keeps this type's slot until the client answers (ConfirmationManager.cs:57-84 @95ef426d), so a question closed with no answer would
            // refuse every later craft, gem or Yes/No ("confirmation in progress") until the next login. Retail's abort (gmGamePlayUI::RecvNotice_AbortConfirmationRequest, 0x004EAA00)
            // closes the dialog through DialogFactory::CloseDialog, which runs the dialog's callback with the dialog's own data (the answer property reads "no" unless Yes was pressed): the
            // reply sent is a No. The callback's send is inferred from the code's shape, not observed on the wire (TODO(m5-6-abort-no-unverified): confirm against a retail capture).
            var reply = _session.Actions.RespondToConfirmation(type, context, false, ActionSource.Ui);
            ServerClosedReplies++;
            _manager.Services.Log.Info("retailui", $"the server's question (type {type}, context {context}) closed ({reason}); ACE keeps its slot until an answer, so No is sent (action {reply.Id})");
        }

        var shown = _shown.Find(s => s.Type == type && s.Context == context);
        if (shown is null)
        {
            return; // our own answer closed it (OnAnswer removes the dialog before it sends), or it never had a dialog
        }

        // M6 5 Q5 (m5-6-headless-confirm-reply): an answer that was not the dialog's (the automation's: the autopilot's tick, the quest station's confirm step) closes the question locally; its dialog closes too, with no second answer.
        _manager.Services.Log.Info("retailui", reason == ConfirmationCloseReason.Answered
            ? $"the server's question (type {type}, context {context}) was answered by the automation ({(closed.Accepted == true ? "Yes" : "No")}); its dialog closes"
            : $"the server's question (type {type}, context {context}) closed ({reason}); its dialog closes");
        shown.Withdrawn = true;
        _manager.Dialogs.CloseDialog(shown.Dialog);
    }

    private void Show(uint type, uint context, string text)
    {
        var log = _manager.Services.Log;
        if (_shown.Exists(s => s.Type == type && s.Context == context))
        {
            return;
        }

        if (_retailSingleSlot && _shown.Count > 0)
        {
            log.Warn("retailui", $"gmGamePlayUI::MakeGameplayConfirmationDialog refuses while a confirmation is open (--retail-confirm-slot): the question of type {type} is not shown and times out at the server");
            return;
        }

        if (type is 1 or 4)
        {
            _manager.LogOnce(
                "confirmation:social",
                LogSeverity.Warning,
                "TODO(g6-confirm-social): retail shows allegiance (type 1) and fellowship (type 4) requests in their own windows; Lifestone shows the same Yes/No dialog as the other types");
        }

        var shown = new Shown(type, context);
        var data = UiDialogData.Of(UiDialogType.Confirmation, UiStringRef.FromLiteral(TextOf(type, text)))
            .WithFlag(UiPropertyIds.DialogModal, true); // property 0xAC; the queue stays the default (retail sets only 0x8E, 0xAC and 0xC5)
        _shown.Add(shown);
        shown.Dialog = _manager.Dialogs.MakeCallbackDialog(data, answer => OnAnswer(shown, answer));
    }

    private void OnAnswer(Shown shown, UiDialogData answer)
    {
        _shown.Remove(shown);
        if (shown.Withdrawn)
        {
            return;
        }

        var log = _manager.Services.Log;
        if (answer.ConfirmationResponse is not { } accepted)
        {
            log.Info("retailui", $"the dialog of the server's question (type {shown.Type}, context {shown.Context}) closed with no answer; the server's question stays open until it times out");
            return;
        }

        Answered++;
        var handle = _session.Actions.RespondToConfirmation(shown.Type, shown.Context, accepted, ActionSource.Ui);
        log.Info("retailui", $"the server's question (type {shown.Type}, context {shown.Context}) was answered {(accepted ? "Yes" : "No")} (action {handle.Id})");
    }

    private sealed class Shown(uint type, uint context)
    {
        public uint Type { get; } = type;

        public uint Context { get; } = context;

        public uint Dialog { get; set; }

        public bool Withdrawn { get; set; }
    }
}
