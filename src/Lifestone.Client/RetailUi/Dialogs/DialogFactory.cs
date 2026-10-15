using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Dialogs;

/// <summary>A dialog opened (<c>ECM_UI::SendNotice_OpenDialog</c>, 11.4186 0x00692650, 11.6096 0x006935A0).</summary>
/// <param name="Context">The dialog's context.</param>
public readonly record struct UiDialogOpened(uint Context);

/// <summary>
/// A dialog closed (<c>ECM_UI::SendNotice_CloseDialog</c>, 11.4186 0x00692500, 11.6096 0x00693450): its context and its
/// data with the response.
/// </summary>
/// <param name="Context">The dialog's context.</param>
/// <param name="Data">The dialog's data, the response included (<see cref="UiDialogData.ConfirmationResponse"/> and its kin).</param>
public readonly record struct UiDialogClosed(uint Context, UiDialogData Data);

/// <summary>
/// The dialogs (retail's <c>DialogFactory</c>): makes a dialog from its data as a root of the dialog layout (UILAYOUT 2,
/// 0x2100003C; the element its type names, <see cref="ElementOf"/>), queues dialogs one at a time per queue, and reports
/// opening and closing with the response.
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from <c>DialogFactory::MakeDialog</c> (11.4186 0x00477E90, 11.6096 0x00478290), <c>CreateDialog_</c>
/// (11.4186 0x00477AD0, 11.6096 0x00477ED0), <c>CloseDialog</c> (11.4186 0x00478160, 11.6096 0x00478560),
/// <c>DialogDone</c> (11.4186 0x004773C0, 11.6096 0x004777C0), <c>OpenNextDialog</c> (11.4186 0x00477D30, 11.6096
/// 0x00478130), <c>IsDialogOpen</c> (11.4186 0x00477060, 11.6096 0x00477460), <c>UpdatePendingDialogDisplay_</c> (11.4186
/// 0x00477590, 11.6096 0x00477990), <c>Reset</c> (11.4186 0x00477950, 11.6096 0x00477D50) and
/// <c>MakeCallbackDialogInCurrentUI</c> (11.4186 0x00478430, 11.6096 0x00478830).
/// </para>
/// <para>
/// <b>Queues</b> (property 0xC3, 2 when absent): queue 1 shows each dialog at once; any other queue shows one dialog at a
/// time and keeps the rest waiting in order, except that a topmost dialog (0x8D) displaces the open one, which waits at
/// the front. A dialog opens brought to the front, with <see cref="DialogOpened"/>; closing it (its buttons, or
/// <see cref="CloseDialog"/>) reports <see cref="DialogClosed"/> with the response, deletes it and opens the next of its
/// queue.
/// </para>
/// <para>
/// <b>Glitch fixes</b> (the default; <see cref="RetailQueueing"/>, from <see cref="UiRetailGlitches.DialogQueueing"/>,
/// restores retail): retail drops a dialog a topmost one displaced (when its turn comes nothing opens), and closing a
/// dialog that is still waiting removes it from a copy of the queue, so it opens later anyway. Lifestone reopens the
/// displaced dialog from its data when its turn comes, and removes a closed waiting dialog for good.
/// </para>
/// <para>
/// <b>Re-entry.</b> A close callback may open a dialog: in the queue being closed, it becomes the queue's dialog and the
/// waiting ones keep waiting behind it (retail's <c>CreateDialog_</c> refuses a second dialog for a queue, and its
/// <c>OpenNextDialog</c> drops the entry it popped; Lifestone keeps that entry waiting). <see cref="Reset"/> empties its
/// tables before the callbacks run, so a dialog they open survives it. The UI flow resets the dialogs whenever the mode
/// changes (<see cref="Flow.UiFlow.UseNewMode"/>).
/// </para>
/// <para>
/// <b>A dialog that is not created</b> (an unknown type, a type the layout has no element for, such as text input on the
/// end-of-retail dats, or an element that does not resolve) opens nothing, loudly, and holds no queue: retail's
/// <c>MakeDialog</c>, <c>CreateDialog_</c> and <c>IsDialogOpen</c> test the queue's current dialog pointer, so an entry
/// with no dialog is a free slot. Lifestone drops the entry, so it never shows, waits, closes or reports; a callback given
/// for it never hears. When it was a queue's next dialog, the one waiting behind it opens (retail leaves the queue waiting
/// until another dialog opens and closes there; <see cref="RetailQueueing"/> restores that).
/// </para>
/// </remarks>
public sealed class DialogFactory
{
    /// <summary>The queue that shows dialogs at once.</summary>
    public const uint ImmediateQueue = 1;

    /// <summary>The queue a dialog goes to when its data names none.</summary>
    public const uint DefaultQueue = 2;

    private readonly UiElementManager _manager;
    private readonly List<Entry> _immediate = [];
    private readonly Dictionary<uint, Entry> _current = [];
    private readonly Dictionary<uint, LinkedList<Entry>> _waiting = [];
    private readonly Dictionary<uint, Action<UiDialogData>> _callbacks = [];
    private uint _globalContext;

    internal DialogFactory(UiElementManager manager)
    {
        _manager = manager;
        RetailQueueing = (manager.Services.RetailGlitches & UiRetailGlitches.DialogQueueing) != 0;
    }

    /// <summary>
    /// True to queue as retail did, glitches included (the class remarks): a dialog a topmost one displaced is dropped, a
    /// closed waiting dialog opens later anyway, and the dialogs waiting behind one that is not created keep waiting.
    /// False by default (Lifestone's fix); the host sets it from <see cref="UiServices.RetailGlitches"/>.
    /// TODO(retailui-runtime): in the 11.4186 decompile, <c>CreateDialog_</c> may find the queue's entry still pointing at
    /// the displaced dialog and not open the topmost one at all; this mode opens it (confirm in 11.6096).
    /// </summary>
    public bool RetailQueueing { get; set; }

    /// <summary>A dialog opened.</summary>
    public event Action<UiDialogOpened>? DialogOpened;

    /// <summary>A dialog closed, with its response.</summary>
    public event Action<UiDialogClosed>? DialogClosed;

    /// <summary>
    /// The element of the dialog layout a dialog of <paramref name="type"/> is, as <c>DialogFactory::CreateDialog_</c> maps
    /// them (0 for an unknown type, which makes no dialog).
    /// </summary>
    public static uint ElementOf(UiDialogType type) => type switch
    {
        UiDialogType.Confirmation => 0x15,
        UiDialogType.Wait => 0x31,
        UiDialogType.Message => 0x24,
        UiDialogType.TextInput => 0x28,
        UiDialogType.ConfirmationTextInput => 0x2C,
        UiDialogType.Menu => 0x1B,
        UiDialogType.ConfirmationMenu => 0x1F,
        _ => 0,
    };

    /// <summary>The dialogs showing now, oldest first.</summary>
    public IEnumerable<UiDialog> OpenDialogs =>
        _immediate.Select(e => e.Dialog).Concat(_current.Values.Select(e => e.Dialog)).OfType<UiDialog>();

    /// <summary>
    /// <c>DialogFactory::MakeDialog</c>: makes (or queues) a dialog from <paramref name="data"/> and returns its context
    /// (never 0). When its type makes no dialog, the context is still returned and nothing opens, as retail (an error is
    /// logged), and the queue stays free (the class remarks).
    /// </summary>
    public uint MakeDialog(UiDialogData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var entry = new Entry(new UiDialogData(data), ++_globalContext);
        var topmost = data.TryGet(UiPropertyIds.DialogTopmost, out var t) && t is UiBoolProperty { Value: true };
        var queue = data.TryGet(UiPropertyIds.DialogQueue, out var q) && q is UiEnumProperty queueId ? queueId.Value : DefaultQueue;
        if (queue == ImmediateQueue)
        {
            _immediate.Add(entry);
            if (!CreateDialog(entry))
            {
                _immediate.Remove(entry);
            }

            return entry.Context;
        }

        if (!_waiting.TryGetValue(queue, out var waiting))
        {
            waiting = new LinkedList<Entry>();
            _waiting[queue] = waiting;
        }

        // Retail tests the queue's current dialog pointer (curInfo.pointer), not whether the queue has an entry.
        if (_current.TryGetValue(queue, out var open) && open.Dialog is not null)
        {
            if (topmost)
            {
                // The open dialog steps aside to the front of the queue (with RetailQueueing, to be dropped there).
                _manager.DeleteElement(open.Dialog);
                open.Dialog = null;
                open.Displaced = RetailQueueing;
                waiting.AddFirst(open);
                OpenInQueue(queue, entry);
            }
            else
            {
                waiting.AddLast(entry);
                UpdatePendingDialogDisplay();
            }
        }
        else
        {
            OpenInQueue(queue, entry);
        }

        return entry.Context;
    }

    /// <summary>
    /// <c>DialogFactory::MakeCallbackDialogInCurrentUI</c>: as <see cref="MakeDialog"/>, and <paramref name="callback"/> hears
    /// the data when the dialog closes (before <see cref="DialogClosed"/>). A dialog that is not created never closes, so
    /// its callback is not kept.
    /// </summary>
    public uint MakeCallbackDialog(UiDialogData data, Action<UiDialogData> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var context = MakeDialog(data);
        if (IsKnown(context))
        {
            _callbacks[context] = callback;
        }

        return context;
    }

    /// <summary>
    /// <c>DialogFactory::IsDialogOpen</c>: with queue 0, whether any dialog shows; with queue 1, whether an immediate one
    /// does; otherwise whether the queue's dialog does (its current entry has a dialog, as retail tests its pointer).
    /// </summary>
    public bool IsDialogOpen(uint queue = 0) => queue switch
    {
        0 => AnyImmediateOpen() || AnyQueuedOpen(),
        ImmediateQueue => AnyImmediateOpen(),
        _ => _current.TryGetValue(queue, out var entry) && entry.Dialog is not null,
    };

    /// <summary>
    /// <c>DialogFactory::CloseDialog</c>: closes the dialog <paramref name="context"/> names, whether it shows or waits; the
    /// next of its queue opens. Nothing happens for an unknown context or 0.
    /// </summary>
    public void CloseDialog(uint context)
    {
        if (context == 0)
        {
            return;
        }

        var immediate = _immediate.FindIndex(e => e.Context == context);
        if (immediate >= 0)
        {
            var entry = _immediate[immediate];
            _immediate.RemoveAt(immediate);
            DialogDone(entry);
            return;
        }

        foreach (var (queue, entry) in _current)
        {
            if (entry.Context == context)
            {
                _current.Remove(queue);
                DialogDone(entry);

                // The callback may have opened a dialog in this queue already (a "Deleting..." after a confirmation):
                // it stays current, and the waiting ones keep waiting behind it.
                if (!_current.ContainsKey(queue))
                {
                    OpenNextDialog(queue);
                }

                return;
            }
        }

        foreach (var waiting in _waiting.Values)
        {
            for (var node = waiting.First; node is not null; node = node.Next)
            {
                if (node.Value.Context == context)
                {
                    if (!RetailQueueing)
                    {
                        waiting.Remove(node);
                    }

                    DialogDone(node.Value);
                    UpdatePendingDialogDisplay();
                    return;
                }
            }
        }
    }

    /// <summary>The dialog showing with <paramref name="context"/>, or null.</summary>
    public UiDialog? Find(uint context) => OpenDialogs.FirstOrDefault(d => d.Context == context);

    /// <summary>
    /// <c>DialogFactory::Reset</c>: every dialog, showing or waiting, is done (reported closed and deleted), as when the UI
    /// mode changes. The tables are emptied first, so a dialog a callback opens meanwhile survives the reset, with its
    /// own callback.
    /// </summary>
    public void Reset()
    {
        var entries = new List<Entry>(_immediate);
        entries.AddRange(_current.Values);
        foreach (var waiting in _waiting.Values)
        {
            entries.AddRange(waiting);
        }

        var callbacks = new Dictionary<uint, Action<UiDialogData>>(_callbacks);
        _immediate.Clear();
        _current.Clear();
        _waiting.Clear();
        _callbacks.Clear();
        foreach (var entry in entries)
        {
            DialogDone(entry, callbacks.Remove(entry.Context, out var callback) ? callback : null);
        }
    }

    /// <summary>
    /// The entry becomes <paramref name="queue"/>'s current dialog and is created (<c>CreateDialog_</c>). When it is not
    /// created, it is dropped and holds no queue (the class remarks), and the next waiting dialog opens instead.
    /// </summary>
    private void OpenInQueue(uint queue, Entry entry)
    {
        _current[queue] = entry;
        if (CreateDialog(entry))
        {
            return;
        }

        if (_current.TryGetValue(queue, out var current) && ReferenceEquals(current, entry))
        {
            _current.Remove(queue);
        }

        _callbacks.Remove(entry.Context);
        if (!RetailQueueing)
        {
            OpenNextDialog(queue);
        }
    }

    private bool AnyImmediateOpen()
    {
        foreach (var entry in _immediate)
        {
            if (entry.Dialog is not null)
            {
                return true;
            }
        }

        return false;
    }

    private bool AnyQueuedOpen()
    {
        foreach (var entry in _current.Values)
        {
            if (entry.Dialog is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True while <paramref name="context"/> names a dialog that shows or waits.</summary>
    private bool IsKnown(uint context)
    {
        foreach (var entry in _immediate)
        {
            if (entry.Context == context)
            {
                return true;
            }
        }

        foreach (var entry in _current.Values)
        {
            if (entry.Context == context)
            {
                return true;
            }
        }

        foreach (var waiting in _waiting.Values)
        {
            foreach (var entry in waiting)
            {
                if (entry.Context == context)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// <c>DialogFactory::CreateDialog_</c>: the dialog of the entry's type is created as a root of the dialog layout, gets
    /// its context and data, comes to the front, and opens. False (logged as an error) when no dialog is created.
    /// </summary>
    private bool CreateDialog(Entry entry)
    {
        var type = (UiDialogType)entry.Data.Type;
        var elementId = ElementOf(type);
        if (elementId == 0)
        {
            _manager.Services.Log.Error("retailui", $"dialog type {entry.Data.Type} names no element of the dialog layout; no dialog opens (as retail)", new LogField("context", entry.Context.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return false;
        }

        var root = _manager.CreateRootElement(UiLayoutEnums.Dialog, elementId);
        if (root is not UiDialog dialog)
        {
            if (root is not null)
            {
                _manager.DeleteElement(root);
            }

            _manager.Services.Log.Error("retailui", $"dialog type {type} (element 0x{elementId:X} of UILAYOUT 2) is not created; no dialog opens (as retail)", new LogField("context", entry.Context.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return false;
        }

        entry.Dialog = dialog;
        dialog.SetContext(entry.Context);
        dialog.SetData(entry.Data);
        dialog.Parent?.BringToFront(dialog);
        UpdatePendingDialogDisplay();
        DialogOpened?.Invoke(new UiDialogOpened(entry.Context));
        return true;
    }

    /// <summary>
    /// <c>DialogFactory::DialogDone</c>: the callback hears the data, <see cref="DialogClosed"/> reports it, and the dialog is
    /// deleted (its context cleared).
    /// </summary>
    private void DialogDone(Entry entry) => DialogDone(entry, _callbacks.Remove(entry.Context, out var callback) ? callback : null);

    private void DialogDone(Entry entry, Action<UiDialogData>? callback)
    {
        var data = entry.Dialog?.Data ?? entry.Data;
        callback?.Invoke(data);

        DialogClosed?.Invoke(new UiDialogClosed(entry.Context, data));
        if (entry.Dialog is { } dialog)
        {
            dialog.SetContext(0);

            // Retail's DialogFactory::DialogDone (11.4186 0x004773C0, 11.6096 0x004777C0) takes the dialog out of its parent and calls a virtual of
            // it with 0 before the dialog goes: taken here to be the hide, which deactivates the dialog's window and activates the one that was active
            // before it (UIElement::OnVisibilityChanged: ActivateNext), so a text field behind the dialog has the keyboard again when it closes
            // (TODO(retailui-runtime): confirm that the virtual, slot 26, is the visibility change).
            _manager.LogOnce(
                "dialog:done-slot26",
                LogSeverity.Warning,
                "TODO(retailui-runtime): DialogFactory::DialogDone (11.6096 0x004777C0) calls the dialog's virtual at slot 26 with 0 before deleting it; Lifestone takes it to be the visibility change (hide, activate the window behind) and hides every closing dialog, not yet confirmed from the 11.6096 bytes");
            dialog.SetVisible(false);
            _manager.DeleteElement(dialog);
            entry.Dialog = null;
        }
    }

    /// <summary>
    /// <c>DialogFactory::OpenNextDialog</c>: the first waiting dialog of the queue opens, unless the queue shows one
    /// already; when it is not created, the next one does (<see cref="OpenInQueue"/>). With <see cref="RetailQueueing"/>,
    /// a dialog a topmost one displaced is dropped when its turn comes and nothing opens (retail's popped entry still
    /// points at the deleted dialog, so <c>CreateDialog_</c> makes none).
    /// </summary>
    private void OpenNextDialog(uint queue)
    {
        if (queue == ImmediateQueue || _current.ContainsKey(queue) || !_waiting.TryGetValue(queue, out var waiting) || waiting.First is not { } first)
        {
            return;
        }

        waiting.RemoveFirst();
        if (waiting.Count == 0)
        {
            _waiting.Remove(queue);
        }

        if (first.Value.Displaced)
        {
            UpdatePendingDialogDisplay();
            return;
        }

        OpenInQueue(queue, first.Value);
    }

    /// <summary><c>DialogFactory::UpdatePendingDialogDisplay_</c>: each queue's open dialog shows whether more wait behind it.</summary>
    private void UpdatePendingDialogDisplay()
    {
        foreach (var (queue, entry) in _current)
        {
            var pending = _waiting.TryGetValue(queue, out var waiting) ? waiting.Count : 0;
            entry.Dialog?.UpdatePendingDialogDisplay(pending);
        }
    }

    private sealed class Entry(UiDialogData data, uint context)
    {
        public UiDialogData Data { get; } = data;

        public uint Context { get; } = context;

        public UiDialog? Dialog { get; set; }

        /// <summary>With <see cref="RetailQueueing"/>: a topmost dialog displaced this one, so it is dropped when its turn comes.</summary>
        public bool Displaced { get; set; }
    }
}
