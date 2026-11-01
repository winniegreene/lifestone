using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.LifestonePanels;

/// <summary>
/// The automation inspector (G7 lane B; m2-plan U7, ST9, DoD 13): a floating window over the shell that shows the Action API's audit log. It lists the
/// <see cref="ActionLog"/>'s rows, newest first (each: when it completed, who asked, what was asked, how it ended), the counters (actions by outcome, refusals,
/// the actions the rate limit made wait) and the two safety switches, the kill switch and dry run, <b>read-only</b>: M2 shows them, and changing them is
/// automation's, later.
/// </summary>
/// <remarks>
/// <para><b>Layout.</b> <c>inspector.json</c> (layout 0x7F000002, format <c>lifestone-layout/1</c>): retail's dialog frame (0x21000042/0x100002AF), text (0x21000040/0x100002CA),
/// button (0x21000040/0x100002C8) and scrollbar (0x2100003E/0x10000367) around a list box (class 5) with the properties of the menu popup's list (0x21000043/0x100004C0), with Lifestone's own text as literals. The panel is a
/// placeholder in look only (it borrows retail's frame and list; retail has no such window) and is labelled so in its title.</para>
/// <para><b>Change-driven.</b> The log raises <see cref="ActionLog.Changed"/> for each action that completes and for each change of the kill switch or dry run. The
/// handler only sets a flag; the window rebuilds on the next frame, once however many actions completed, and only while it is open. A new action adds one row at the top
/// (and drops the oldest past the ring's 1,024), and an idle inspector does nothing and allocates nothing: the frame hook is registered only while the window is open and returns at once when
/// nothing changed.</para>
/// <para><b>Rows are built over frames.</b> A full ring is 1,024 text elements, about 40 ms to make at once (measured in <c>AutomationInspectorTests</c>), more than DoD 15 allows a frame. A
/// (re)build makes the newest <see cref="FirstBatch"/> rows, and each following frame adds <see cref="FrameBatch"/> older ones to the bottom until the list holds the ring
/// (<see cref="IsBackfilling"/>); the list scrolls as it fills.</para>
/// <para>The window is made on the first <see cref="Open"/>. When the layout is missing (a host without the dats' layouts, which registers none of the embedded ones) the inspector logs a loud
/// <c>TODO(g7-inspector-layout)</c> and stays closed. It never talks to the session except to read: <see cref="IGameSessionView.ActionLog"/> and the two flags of
/// <see cref="IActionApi"/>.</para>
/// </remarks>
public sealed class AutomationInspector : IUiElementListener, IUiGlobalListener, IDisposable
{
    /// <summary>The layout's DID (<c>inspector.json</c>).</summary>
    public const uint LayoutId = 0x7F000002;

    /// <summary>The window.</summary>
    public const uint WindowId = 0x7F000200;

    /// <summary>The counters text.</summary>
    public const uint CountersId = 0x7F000204;

    /// <summary>The kill switch and dry run line.</summary>
    public const uint StateId = 0x7F000205;

    /// <summary>The autopilot's three lines (M5 4 h2): its state, its last decision and its next trigger.</summary>
    public const uint AutopilotId = 0x7F000209;

    /// <summary>The longest decision or trigger the autopilot's lines show before they are cut with an ellipsis (the line is 528 px wide).</summary>
    public const int AutopilotLineLimit = 84;

    /// <summary>The list of rows.</summary>
    public const uint ListId = 0x7F000206;

    /// <summary>The list's scrollbar (retail's template 0x2100003E/0x10000367).</summary>
    public const uint ScrollbarId = 0x7F000208;

    /// <summary>The hide button.</summary>
    public const uint CloseId = 0x7F000207;

    /// <summary>The row template (a top-level element of the layout, made detached for each row).</summary>
    public const uint RowTemplateId = 0x7F000210;

    /// <summary>The newest rows a (re)build makes at once (about two pages of the list), so the frame that opens the inspector with a full ring of 1,024 does not build them all.</summary>
    public const int FirstBatch = 32;

    /// <summary>The older rows each following frame adds until the list holds the whole ring.</summary>
    public const int FrameBatch = 64;

    private static readonly string[] OutcomeNames = BuildOutcomeNames();

    private readonly UiElementManager _manager;
    private readonly IGameSessionView _session;
    private readonly ActionLog _log;
    private UiElement? _window;
    private UiListBox? _list;
    private UiTextElement? _counters;
    private UiTextElement? _state;
    private UiTextElement? _autopilotLines;
    private IAutopilotStatusSource? _autopilot;
    private UiElement? _closeButton;
    private bool _dirty;
    private bool _backlog;
    private bool _frameRegistered;
    private bool _disposed;
    private long _shownTotal = -1;
    private string _countersText = string.Empty;
    private string _stateText = string.Empty;
    private string _autopilotText = string.Empty;

    /// <summary>An inspector over <paramref name="session"/>'s action log; nothing is made until <see cref="Open"/>.</summary>
    public AutomationInspector(UiElementManager manager, IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(session);
        _manager = manager;
        _session = session;
        _log = session.ActionLog;
        _log.Changed += OnLogChanged;
    }

    /// <summary>True while the window shows.</summary>
    public bool IsOpen => _window is { IsDeleted: false, Visible: true };

    /// <summary>The window, or null before the first <see cref="Open"/> (or when the layout is missing).</summary>
    public UiElement? Window => _window;

    /// <summary>The rows now listed, newest first (0 before the first refresh).</summary>
    public int RowCount => _list?.Items.Count ?? 0;

    /// <summary>The counters text now shown.</summary>
    public string CountersText => _countersText;

    /// <summary>The kill switch and dry run line now shown.</summary>
    public string StateText => _stateText;

    /// <summary>The autopilot lines now shown (state, last decision, next trigger).</summary>
    public string AutopilotText => _autopilotText;

    /// <summary>
    /// The autopilot whose state the inspector shows (M5 4 h2): the play host sets the autopilot host logic (Session/Autopilot). Its <see cref="IAutopilotStatusSource.Changed"/> makes the window refresh on the next frame, like a
    /// new action does. Null (the default) shows that no autopilot is wired.
    /// </summary>
    public IAutopilotStatusSource? Autopilot
    {
        get => _autopilot;
        set
        {
            if (ReferenceEquals(_autopilot, value))
            {
                return;
            }

            if (_autopilot is not null)
            {
                _autopilot.Changed -= OnLogChanged;
            }

            _autopilot = value;
            if (value is not null && !_disposed)
            {
                value.Changed += OnLogChanged;
            }

            _dirty = true;
        }
    }

    /// <summary>How many times the window brought itself up to date (the first build and each change after).</summary>
    public int Refreshes { get; private set; }

    /// <summary>How many times every row was rebuilt (the first build, and a change the incremental path could not follow).</summary>
    public int Rebuilds { get; private set; }

    /// <summary>True while older rows of the ring are still to be added to the list (a few frames after a full ring is opened).</summary>
    public bool IsBackfilling => _backlog;

    /// <summary>True when the log changed since the window was last brought up to date (it is, while closed and the log moved).</summary>
    public bool IsStale => _dirty;

    /// <summary>The text of row <paramref name="index"/> (0 the newest), or null.</summary>
    public string? RowText(int index) => _list?.GetItem(index) is UiTextElement row ? row.Text : null;

    /// <summary>Shows the window (making it on the first call) and brings it up to date. Scroll Lock calls <see cref="Toggle"/>, which calls this.</summary>
    public void Open()
    {
        if (_disposed)
        {
            return;
        }

        if (_window is null && !Build())
        {
            return;
        }

        _window!.SetVisible(true);
        if (!_frameRegistered)
        {
            _frameRegistered = true;
            _manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        }

        Refresh();
    }

    /// <summary>Hides the window. The log is not watched for the window while it is hidden; <see cref="Open"/> catches up.</summary>
    public void Close()
    {
        if (_window is { IsDeleted: false })
        {
            _window.SetVisible(false);
        }

        if (_frameRegistered)
        {
            _frameRegistered = false;
            _manager.UnregisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        }
    }

    /// <summary>Opens the window when it is closed and closes it when it is open.</summary>
    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    /// <summary>
    /// Brings the window up to date with the log: the counters, the safety switches and the rows. Does nothing when the log has not changed since. The frame hook calls
    /// it; tests and <see cref="Open"/> call it directly.
    /// </summary>
    public void Refresh()
    {
        if (_window is null || _list is null)
        {
            return;
        }

        if (!_dirty && _shownTotal >= 0)
        {
            Backfill();
            return;
        }

        _dirty = false;
        Refreshes++;
        SetIfChanged(_counters, ref _countersText, FormatCounters(_log));
        SetIfChanged(_state, ref _stateText, FormatState(_session.Actions.KillSwitch, _session.Actions.DryRun));
        SetIfChanged(_autopilotLines, ref _autopilotText, FormatAutopilot(_autopilot?.Status));
        UpdateRows();
    }

    /// <inheritdoc/>
    public void ListenToGlobalMessage(uint messageId, int data)
    {
        if (messageId == UiGlobalMessageIds.Frame && (_dirty || _backlog))
        {
            Refresh();
        }
    }

    /// <inheritdoc/>
    public UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.ButtonClick && ReferenceEquals(message.Element, _closeButton))
        {
            Close();
            return UiListenResult.Handled;
        }

        return UiListenResult.Default;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _log.Changed -= OnLogChanged;
        if (_autopilot is not null)
        {
            _autopilot.Changed -= OnLogChanged;
        }

        if (_frameRegistered)
        {
            _frameRegistered = false;
            _manager.UnregisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        }

        if (_window is { IsDeleted: false })
        {
            _manager.DeleteElement(_window);
        }

        _window = null;
        _list = null;
    }

    /// <summary>One row: <c>12.30s Ui Use 0x80000A03 -&gt; Confirmed (35 ms)</c>; a refusal or error adds its detail, and an action the rate limit held back says so.</summary>
    public static string FormatRow(in ActionLogEntry entry)
    {
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{entry.CompletedAt:0.00}s {entry.Source} {entry.Summary} -> {OutcomeNames[(int)entry.Outcome.Kind]}");
        if (entry.Outcome.Detail is { Length: > 0 } detail)
        {
            text += ": " + detail;
        }
        else
        {
            text += string.Create(CultureInfo.InvariantCulture, $" ({entry.Milliseconds:0} ms)");
        }

        return entry.RateLimited ? text + " [waited for the rate limit]" : text;
    }

    /// <summary>The counters, four lines: the log's totals, the outcomes, the refusals and errors, and the cancelled, not-implemented and rate-limited counts. Lifetime counts: the ring's drops do not reduce them.</summary>
    public static string FormatCounters(ActionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Logged {log.Total:N0} ({log.Count:N0} kept, the ring holds {ActionLog.Capacity:N0})\n" +
            $"Confirmed {log.CountOf(ActionOutcomeKind.Confirmed):N0} | Local {log.CountOf(ActionOutcomeKind.Local):N0} | Sent {log.CountOf(ActionOutcomeKind.Sent):N0} | Dry run {log.CountOf(ActionOutcomeKind.DryRun):N0}\n" +
            $"Refusals {log.CountOf(ActionOutcomeKind.Refused):N0} | Server error {log.CountOf(ActionOutcomeKind.ServerError):N0} | Timed out {log.CountOf(ActionOutcomeKind.TimedOut):N0}\n" +
            $"Cancelled {log.CountOf(ActionOutcomeKind.Cancelled):N0} | Not implemented {log.CountOf(ActionOutcomeKind.NotImplemented):N0} | Rate-limited {log.RateLimited:N0}");
    }

    /// <summary>The safety line (read-only).</summary>
    public static string FormatState(bool killSwitch, bool dryRun) =>
        $"Kill switch: {(killSwitch ? "ON" : "off")} | Dry run: {(dryRun ? "ON" : "off")} | read-only here";

    /// <summary>
    /// The autopilot's three lines (M5 4 h2, DoD 9): <c>Autopilot: running | fight:den-fight | cycle 2 | interrupted 1, resumed 1</c>, <c>Last decision: ...</c> and <c>Next trigger: ...</c> (for example
    /// <c>trip at 200 tapers: 412 held</c> or <c>stop at 30 min: 12 min left</c>). A null status is a host that wired no autopilot.
    /// </summary>
    public static string FormatAutopilot(AutopilotStatus? status)
    {
        if (status is not { } s)
        {
            return "Autopilot: not available in this session\nLast decision: -\nNext trigger: -";
        }

        var mode = s.Mode switch
        {
            AutopilotMode.Running => "running",
            AutopilotMode.Stopping => "stopping",
            AutopilotMode.Interrupted => "stopped (the controls are yours; /auto resumes)",
            AutopilotMode.Halted => "halted by the kill switch",
            AutopilotMode.Done => "done",
            _ => "off (type /auto to start it)",
        };
        var head = "Autopilot: " + mode;
        if (s.State.Length > 0)
        {
            head += string.Create(CultureInfo.InvariantCulture, $" | {s.State} | cycle {s.Cycle}");
        }

        if (s.Interrupted > 0 || s.Resumed > 0)
        {
            head += string.Create(CultureInfo.InvariantCulture, $" | interrupted {s.Interrupted}, resumed {s.Resumed}");
        }

        return Clip(head) + "\nLast decision: " + Clip(s.LastDecision.Length > 0 ? s.LastDecision : "-") + "\nNext trigger: " + Clip(s.NextTrigger.Length > 0 ? s.NextTrigger : "-");
    }

    private static string Clip(string text) => text.Length <= AutopilotLineLimit ? text : text[..(AutopilotLineLimit - 3)] + "...";

    private static string[] BuildOutcomeNames()
    {
        var names = new string[Enum.GetValues<ActionOutcomeKind>().Length];
        foreach (var kind in Enum.GetValues<ActionOutcomeKind>())
        {
            names[(int)kind] = kind switch
            {
                ActionOutcomeKind.ServerError => "Server error",
                ActionOutcomeKind.TimedOut => "Timed out",
                ActionOutcomeKind.NotImplemented => "Not implemented",
                ActionOutcomeKind.DryRun => "Dry run",
                _ => kind.ToString(),
            };
        }

        return names;
    }

    private void OnLogChanged() => _dirty = true;

    private static void SetIfChanged(UiTextElement? element, ref string current, string text)
    {
        if (string.Equals(current, text, StringComparison.Ordinal))
        {
            return;
        }

        current = text;
        element?.SetText(text);
    }

    private bool Build()
    {
        if (!_manager.Services.Descs.TryGetLayout(LayoutId, out _))
        {
            _manager.LogOnce("inspector:layout", LogSeverity.Error, $"TODO(g7-inspector-layout): the Lifestone layout 0x{LayoutId:X8} (inspector.json) is not registered with the layouts; the automation inspector stays closed");
            return false;
        }

        _window = _manager.CreateRootElementByDataId(LayoutId, WindowId);
        if (_window is null)
        {
            _manager.LogOnce("inspector:window", LogSeverity.Error, $"TODO(g7-inspector-layout): the inspector's window 0x{WindowId:X8} did not build from layout 0x{LayoutId:X8}; it stays closed");
            return false;
        }

        _list = _window.GetChildRecursive(ListId) as UiListBox;
        _counters = _window.GetChildRecursive(CountersId) as UiTextElement;
        _state = _window.GetChildRecursive(StateId) as UiTextElement;
        _autopilotLines = _window.GetChildRecursive(AutopilotId) as UiTextElement;
        _closeButton = _window.GetChildRecursive(CloseId);
        if (_list is null || _counters is null || _state is null || _autopilotLines is null || _closeButton is null)
        {
            _manager.LogOnce("inspector:parts", LogSeverity.Error, $"TODO(g7-inspector-layout): the inspector's list, counters, state line, autopilot lines or button is missing from layout 0x{LayoutId:X8}; the window may show part of its content");
        }

        _window.RegisterForElementMessages(this);
        _dirty = true;
        return _list is not null;
    }

    private void UpdateRows()
    {
        var list = _list!;
        var count = _log.Count;
        var total = _log.Total;
        var added = total - _shownTotal;
        if (_shownTotal < 0 || added < 0 || added > count || added > FirstBatch || list.Items.Count > Math.Min(_shownTotal, ActionLog.Capacity))
        {
            Rebuild(list);
        }
        else
        {
            // The new entries are the newest `added` of the ring, oldest first; each goes on top, so the newest ends on top.
            for (var i = 0; i < added; i++)
            {
                var row = MakeRow(_log[count - (int)added + i]);
                if (row is not null)
                {
                    list.InsertItem(row, 0);
                }
            }

            while (list.Items.Count > count)
            {
                list.DeleteItem(list.Items.Count - 1);
            }
        }

        _shownTotal = total;
        _backlog = list.Items.Count < count;
    }

    /// <summary>Adds the next <see cref="FrameBatch"/> older rows of the ring to the bottom of the list. Row k of the list (0 the top) is ring entry Count - 1 - k, however many rows are built.</summary>
    private void Backfill()
    {
        if (!_backlog || _list is not { } list)
        {
            return;
        }

        var count = _log.Count;
        var built = list.Items.Count;
        for (var k = built; k < Math.Min(count, built + FrameBatch); k++)
        {
            if (MakeRow(_log[count - 1 - k]) is { } row)
            {
                list.AddItem(row);
            }
        }

        _backlog = list.Items.Count < count && list.Items.Count > built;
    }

    private void Rebuild(UiListBox list)
    {
        Rebuilds++;
        list.Flush();
        for (var i = _log.Count - 1; i >= Math.Max(0, _log.Count - FirstBatch); i--)
        {
            if (MakeRow(_log[i]) is { } row)
            {
                list.AddItem(row);
            }
        }
    }

    private UiElement? MakeRow(in ActionLogEntry entry)
    {
        if (_manager.CreateDetachedElement(LayoutId, RowTemplateId) is not UiTextElement row)
        {
            _manager.LogOnce("inspector:row", LogSeverity.Error, $"TODO(g7-inspector-layout): the row template 0x{RowTemplateId:X8} of layout 0x{LayoutId:X8} is not a text element; the inspector lists no rows");
            return null;
        }

        row.SetText(FormatRow(entry));
        row.SetMouseVisible(false);
        if (_list is { } list)
        {
            row.ResizeTo(Math.Max(1, list.Box.Width), row.Box.Height);
        }

        return row;
    }
}
