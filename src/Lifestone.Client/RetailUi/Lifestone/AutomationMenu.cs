using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.LifestonePanels;

/// <summary>
/// The automation menu (M6 6 MC; the lead's L1): a Lifestone floating window that lists what the autopilot can do now and starts or stops it. Its rows are the job menu's
/// (<c>JobMenu</c>, read from the host-wired <see cref="Source"/>): one row per open job with its <c>xp &lt;n&gt;</c> (or <c>xp ?</c>) and then its label, the task the dial runs
/// marked <c>&gt;</c> and <c>| current</c>; the quests left out below them, prefixed <c>(refused)</c> with their reason; and a <c>stop</c> row last. Choosing a row calls the
/// host-wired <see cref="Choose"/> (the stop row <see cref="Stop"/>) and shows the line it returns in the window's status line.
/// </summary>
/// <remarks>
/// <para><b>Layout.</b> <c>menu.json</c> (layout 0x7F000003, format <c>lifestone-layout/1</c>): the inspector's frame, text, button, scrollbar and list templates
/// (<see cref="AutomationInspector"/>), with Lifestone's own text as literals. Retail had no such window: it is a placeholder in look and is labelled so in its title.</para>
/// <para><b>Acts through delegates only.</b> The menu never calls the session or <c>IActionApi</c>: the play host wires <see cref="Source"/>, <see cref="Choose"/> and
/// <see cref="Stop"/> (the tools panel's pattern for its Autopilot button). A row chosen with nothing wired says so in the status line and warns in the log.</para>
/// <para><b>When it rebuilds.</b> On <see cref="Open"/>; on the next frame after the source's <see cref="IAutopilotJobSource.Changed"/>; and, while open, once
/// <see cref="RebuildSeconds"/> passed since the last build, because a quest's timer comes due without any change being raised. Never while closed: <see cref="Open"/>
/// catches up. The frame hook is registered only while the window is open.</para>
/// <para>Not bound to a key (L1): the tools panel's button opens it (the host's, slice MD). The window is made on the first <see cref="Open"/>; when the layout is missing
/// the menu logs a loud <c>TODO(m6-menu-layout)</c> and stays closed.</para>
/// <para><b>Where it hangs (M6 6 FXU, closes <c>m6-menu-row-click</c>).</b> The game screen gives the menu its root (0x10000495) as the parent, as the tools panel's: the window is that root's child, so the mouse's hit test reaches its
/// rows before the shell's world viewport (0x10000436). Made as a root of its own (MC's), it drew on top but the shell's root (z level 1, in front of the menu's 20 among the manager's roots) took every click over it. Its z level
/// is 21 (<c>menu.json</c>): in front of the main chat (600) and the SmartBox (9999), behind the tools panel (20), so the tools panel's Jobs button, which opens and closes it, stays on top where the two overlap (the play host
/// closes the tools panel when the button opens the menu). Without a parent (a host or test with no screen) it is a manager root as before.</para>
/// </remarks>
public sealed class AutomationMenu : IUiElementListener, IUiGlobalListener, IDisposable
{
    /// <summary>The layout's DID (<c>menu.json</c>).</summary>
    public const uint LayoutId = 0x7F000003;

    /// <summary>The window.</summary>
    public const uint WindowId = 0x7F000300;

    /// <summary>The title (it says the window is a placeholder).</summary>
    public const uint TitleId = 0x7F000303;

    /// <summary>The summary line: the level, the open and refused quests, the current task.</summary>
    public const uint SummaryId = 0x7F000304;

    /// <summary>The status line: the line the last choice returned.</summary>
    public const uint StatusId = 0x7F000305;

    /// <summary>The list of rows.</summary>
    public const uint ListId = 0x7F000306;

    /// <summary>The hide button.</summary>
    public const uint CloseId = 0x7F000307;

    /// <summary>The list's scrollbar.</summary>
    public const uint ScrollbarId = 0x7F000308;

    /// <summary>The row template (a top-level element of the layout, made detached for each row).</summary>
    public const uint RowTemplateId = 0x7F000310;

    /// <summary>The open window rebuilds at most this often without a change (a quest's timer comes due on the clock alone). Lifestone's own tuning, not a world fact.</summary>
    public const double RebuildSeconds = 60.0;

    /// <summary>The longest row or line shown before it is cut with an ellipsis (the list is 432 px wide, the lines 448).</summary>
    public const int LineLimit = 68;

    /// <summary>The status line before anything was chosen.</summary>
    public const string ReadyLine = "Choose a row: the autopilot runs it; stop stops it.";

    /// <summary>The status line when a row is chosen and the host wired nothing to act on it.</summary>
    public const string UnwiredLine = "The autopilot is not available in this host.";

    private readonly UiElementManager _manager;
    private readonly UiElement? _parent;
    private readonly List<JobEntry> _rows = [];
    private UiElement? _window;
    private UiListBox? _list;
    private UiTextElement? _summary;
    private UiTextElement? _status;
    private UiElement? _closeButton;
    private IAutopilotJobSource? _source;
    private bool _dirty = true;
    private bool _frameRegistered;
    private bool _disposed;
    private double _builtAt = double.NegativeInfinity;
    private string _summaryText = string.Empty;
    private string _statusText = ReadyLine;

    /// <summary>A menu over <paramref name="manager"/>; nothing is made until <see cref="Open"/>.</summary>
    /// <param name="manager">The UI manager.</param>
    /// <param name="parent">The element the window is a child of (the game screen's root, as the tools panel's); null makes it a manager root.</param>
    public AutomationMenu(UiElementManager manager, UiElement? parent = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
        _parent = parent;
    }

    /// <summary>Where the rows and the current task come from (the play host's); its <see cref="IAutopilotJobSource.Changed"/> rebuilds the open window on the next frame. Null lists nothing.</summary>
    public IAutopilotJobSource? Source
    {
        get => _source;
        set
        {
            if (ReferenceEquals(_source, value))
            {
                return;
            }

            if (_source is not null)
            {
                _source.Changed -= OnChanged;
            }

            _source = value;
            if (value is not null && !_disposed)
            {
                value.Changed += OnChanged;
            }

            _dirty = true;
        }
    }

    /// <summary>Runs a task (a job, quest or errand row): the play host's; returns the line the status shows (the chat's answer to <c>/auto</c>).</summary>
    public Func<AutopilotTask, string>? Choose { get; set; }

    /// <summary>Stops the run (the stop row): the play host's; returns the line the status shows (the chat's answer to <c>/stop</c>).</summary>
    public Func<string>? Stop { get; set; }

    /// <summary>True while the window shows.</summary>
    public bool IsOpen => _window is { IsDeleted: false, Visible: true };

    /// <summary>The window, or null before the first <see cref="Open"/> (or when the layout is missing).</summary>
    public UiElement? Window => _window;

    /// <summary>The rows now listed (0 before the first build).</summary>
    public int RowCount => _list?.Items.Count ?? 0;

    /// <summary>The summary line now shown.</summary>
    public string SummaryText => _summaryText;

    /// <summary>The status line now shown (<see cref="ReadyLine"/>, or the line the last choice returned).</summary>
    public string StatusText => _statusText;

    /// <summary>How many times the rows were rebuilt.</summary>
    public int Rebuilds { get; private set; }

    /// <summary>True when the source changed since the rows were built (it is, while closed and the source moved).</summary>
    public bool IsStale => _dirty;

    /// <summary>The text of row <paramref name="index"/> (0 the top), or null.</summary>
    public string? RowText(int index) => _list?.GetItem(index) is UiTextElement row ? row.Text : null;

    /// <summary>The job entry behind row <paramref name="index"/>, or null.</summary>
    public JobEntry? RowEntry(int index) => index >= 0 && index < _rows.Count ? _rows[index] : null;

    /// <summary>Shows the window (making it on the first call) and rebuilds its rows.</summary>
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

        Rebuild();
    }

    /// <summary>Hides the window; nothing is rebuilt while it is hidden.</summary>
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

    /// <summary>Opens the window when it is closed and closes it when it is open (the tools panel's button calls this).</summary>
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

    /// <summary>Rebuilds the open window when the source changed or <see cref="RebuildSeconds"/> passed since the last build; the frame hook calls it, tests call it directly.</summary>
    public void Refresh()
    {
        if (_list is not null && IsOpen && (_dirty || _manager.Now - _builtAt >= RebuildSeconds))
        {
            Rebuild();
        }
    }

    /// <summary>
    /// Chooses row <paramref name="index"/> (a click on the list does this; checks and tests call it): the stop row calls <see cref="Stop"/>, an open row calls
    /// <see cref="Choose"/> with its task, a refused row calls nothing and says why. Returns the status line it shows.
    /// </summary>
    public string ChooseRow(int index)
    {
        if (RowEntry(index) is not { } entry)
        {
            return SetStatus(string.Create(CultureInfo.InvariantCulture, $"No row {index} in the menu."));
        }

        string line;
        if (!entry.Enabled)
        {
            line = $"{entry.Label} is not open: {entry.Refusal}";
        }
        else if (IsStop(entry))
        {
            line = Stop is { } stop ? stop() : Unwired(entry);
        }
        else
        {
            line = Choose is { } choose ? choose(entry.Task) : Unwired(entry);
        }

        _manager.Services.Log.Info(UiElementManager.Category, string.Create(CultureInfo.InvariantCulture, $"automation menu: row {index} job={(IsStop(entry) ? JobLabels.Stop : entry.Task.Name)} chosen -> {line}"));
        return SetStatus(line);
    }

    /// <inheritdoc/>
    public void ListenToGlobalMessage(uint messageId, int data)
    {
        if (messageId == UiGlobalMessageIds.Frame)
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

        if (_list is { } list && ReferenceEquals(message.Element, list))
        {
            var index = message.MessageId switch
            {
                UiMessageIds.ListBoxSelection => unchecked((int)message.Param1),
                UiMessageIds.ListBoxReselect when message.Item is { } item => IndexOf(list, item),
                _ => -1,
            };
            if (index >= 0)
            {
                ChooseRow(index);
                return UiListenResult.Handled;
            }
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
        if (_source is not null)
        {
            _source.Changed -= OnChanged;
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
        _rows.Clear();
    }

    /// <summary>True for the stop row (it carries <see cref="AutopilotTask.Job"/> and the label <see cref="JobLabels.Stop"/>).</summary>
    public static bool IsStop(JobEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Enabled && entry.Task.Kind == AutopilotTaskKind.Job && entry.Label == JobLabels.Stop;
    }

    /// <summary>The rows in the menu's order: the open jobs as listed (stop left out), then the refused quests, then the stop row.</summary>
    public static IReadOnlyList<JobEntry> RowsOf(JobList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        var rows = new List<JobEntry>(list.Jobs.Count + list.Refused.Count);
        rows.AddRange(list.Jobs.Where(e => e.Enabled && !IsStop(e)));
        rows.AddRange(list.Jobs.Where(e => !e.Enabled));
        rows.AddRange(list.Refused);
        rows.AddRange(list.Jobs.Where(IsStop));
        return rows;
    }

    /// <summary>
    /// One row, its experience first (M6 6 FXU, closes <c>m6-menu-row-xp-clipped</c>: the catalog's quest names are long, and a row cut at <see cref="LineLimit"/> lost the xp it was listed for): <c>xp 12,000 | do a quest: Tusker Tusks</c>;
    /// the current task <c>&gt; xp ? | level up | current</c>; a refused quest <c>(refused) xp 900 | Bone Dust | not-due:...</c>; <c>stop | stop the run where it stands</c>. An open row too long for the line cuts its label, never its xp
    /// or its <c>| current</c>; a refused row is cut at its end (its reason).
    /// </summary>
    public static string FormatRow(JobEntry entry, AutopilotTask current)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var xp = XpText(entry.Experience);
        if (IsStop(entry))
        {
            return Clip(JobLabels.Stop + " | " + entry.Why);
        }

        if (!entry.Enabled)
        {
            return Clip($"(refused) {xp} | {entry.Label} | {entry.Refusal}");
        }

        var (head, tail) = entry.Task == current ? ($"> {xp} | ", " | current") : ($"{xp} | ", string.Empty);
        return head + Clip(entry.Label, LineLimit - head.Length - tail.Length) + tail;
    }

    /// <summary>A row's experience as the menu shows it: <c>xp 12,000</c>, or <c>xp ?</c> when unknown.</summary>
    public static string XpText(long? experience) => "xp " + (experience is { } n ? n.ToString("N0", CultureInfo.InvariantCulture) : "?");

    /// <summary>The summary line: <c>Level 12 | 3 quest(s) open, 5 refused | now: job</c>; without a list, that the host wired no source.</summary>
    public static string FormatSummary(JobList? list, AutopilotTask current)
    {
        if (list is null)
        {
            return "No job source in this host: the menu lists nothing.";
        }

        var open = list.Jobs.Count(e => e.Enabled && e.Task.Kind == AutopilotTaskKind.Quest);
        var refused = list.Refused.Count + list.Jobs.Count(e => !e.Enabled);
        return Clip(string.Create(CultureInfo.InvariantCulture, $"Level {list.Level} | {open} quest(s) open, {refused} refused | now: {current.Name}"));
    }

    private static string Clip(string text, int limit = LineLimit) => text.Length <= limit ? text : limit <= 3 ? text[..Math.Max(0, limit)] : text[..(limit - 3)] + "...";

    private static int IndexOf(UiListBox list, UiElement item)
    {
        for (var i = 0; i < list.Items.Count; i++)
        {
            if (ReferenceEquals(list.Items[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    private void OnChanged() => _dirty = true;

    private string Unwired(JobEntry entry)
    {
        _manager.Services.Log.Warn(UiElementManager.Category, $"the automation menu's row '{entry.Label}' was chosen but nothing is wired to it (the play host wires Choose and Stop when it starts)");
        return UnwiredLine;
    }

    private string SetStatus(string line)
    {
        var text = Clip(line.ReplaceLineEndings(" "));
        if (!string.Equals(text, _statusText, StringComparison.Ordinal))
        {
            _statusText = text;
            _status?.SetText(text);
        }

        return text;
    }

    private bool Build()
    {
        if (!_manager.Services.Descs.TryGetLayout(LayoutId, out _))
        {
            _manager.LogOnce("menu:layout", LogSeverity.Error, $"TODO(m6-menu-layout): the Lifestone layout 0x{LayoutId:X8} (menu.json) is not registered with the layouts; the automation menu stays closed");
            return false;
        }

        _window = _parent is { IsDeleted: false } parent ? _manager.CreateChildElement(parent, LayoutId, WindowId) : _manager.CreateRootElementByDataId(LayoutId, WindowId);
        if (_window is null)
        {
            _manager.LogOnce("menu:window", LogSeverity.Error, $"TODO(m6-menu-layout): the menu's window 0x{WindowId:X8} did not build from layout 0x{LayoutId:X8}; it stays closed");
            return false;
        }

        _list = _window.GetChildRecursive(ListId) as UiListBox;
        _summary = _window.GetChildRecursive(SummaryId) as UiTextElement;
        _status = _window.GetChildRecursive(StatusId) as UiTextElement;
        _closeButton = _window.GetChildRecursive(CloseId);
        if (_list is null || _summary is null || _status is null || _closeButton is null)
        {
            _manager.LogOnce("menu:parts", LogSeverity.Error, $"TODO(m6-menu-layout): the menu's list, summary, status line or button is missing from layout 0x{LayoutId:X8}; the window may show part of its content");
        }

        _status?.SetText(_statusText);
        _window.RegisterForElementMessages(this);
        return _list is not null;
    }

    private void Rebuild()
    {
        if (_list is not { } list)
        {
            return;
        }

        _dirty = false;
        _builtAt = _manager.Now;
        Rebuilds++;
        var jobs = _source?.Jobs;
        var current = _source?.Current ?? AutopilotTask.Job;
        _summaryText = FormatSummary(jobs, current);
        _summary?.SetText(_summaryText);

        list.Flush();
        _rows.Clear();
        if (jobs is null)
        {
            return;
        }

        foreach (var entry in RowsOf(jobs))
        {
            if (_manager.CreateDetachedElement(LayoutId, RowTemplateId) is not UiTextElement row)
            {
                _manager.LogOnce("menu:row", LogSeverity.Error, $"TODO(m6-menu-layout): the row template 0x{RowTemplateId:X8} of layout 0x{LayoutId:X8} is not a text element; the menu lists no rows");
                _rows.Clear();
                list.Flush();
                return;
            }

            row.SetText(FormatRow(entry, current));
            row.SetMouseVisible(false);
            row.ResizeTo(Math.Max(1, list.Box.Width), row.Box.Height);
            list.AddItem(row);
            _rows.Add(entry);
        }
    }
}
