using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;
using Lifestone.Data.World;

namespace Lifestone.Client.RetailUi.LifestonePanels;

/// <summary>
/// Lifestone's tools panel (G7 lane A; docs/retail-ui.md section 22.1): a floating window over the in-game shell, built from <c>tools.json</c> (layout 0x7F000010, retail templates by id,
/// Lifestone literal text), with the three tools of the server mod: <b>Places</b> (a search field and a list of <see cref="Lifestone.Client.Navigation.TeleportCatalog"/>'s places: ACE's
/// points of interest and the curated dungeons and interiors; a click teleports, and portal space follows), <b>Where am I</b> (the server's position, compared with the client's) and
/// <b>Add XP</b>. The panel only drives <see cref="ToolsModel"/>, which goes through the Action API (<see cref="PlayerTools"/>, <see cref="ActionSource.Ui"/>), so every use is in the chat and
/// the action log and is subject to the same rules as everything else.
/// </summary>
/// <remarks>
/// <para><b>Refusals.</b> Against a server that is not on loopback the panel does not open: a retail message dialog says why (and each tool is refused by the Action API anyway). When the mod is
/// absent (the server answered "Unknown command: lifestone" to the status probe the session sends on entering the world, or to the panel's own) another message dialog says so. Both texts are
/// Lifestone literals on the retail message dialog.</para>
/// <para>Keys: F7 toggles it (<see cref="LifestoneHotkeys"/>). Game thread only.</para>
/// </remarks>
public sealed class ToolsPanel : IUiElementListener, IDisposable
{
    /// <summary>The layout of <c>tools.json</c>.</summary>
    public const uint LayoutId = 0x7F000010;

    /// <summary>The panel window.</summary>
    public const uint WindowId = 0x7F000100;

    /// <summary>The search field.</summary>
    public const uint SearchId = 0x7F000106;

    /// <summary>The first of the <see cref="ToolsModel.PageSize"/> place rows (0x7F000110-0x7F000117).</summary>
    public const uint FirstRowId = 0x7F000110;

    /// <summary>The Up button (the previous rows).</summary>
    public const uint UpId = 0x7F000118;

    /// <summary>The Down button (the next rows).</summary>
    public const uint DownId = 0x7F000119;

    /// <summary>The "1-8 of 123" text.</summary>
    public const uint CounterId = 0x7F000120;

    /// <summary>The Where am I button.</summary>
    public const uint WhereButtonId = 0x7F000121;

    /// <summary>The Where am I result text.</summary>
    public const uint WhereTextId = 0x7F000122;

    /// <summary>The Add XP amount field.</summary>
    public const uint AmountId = 0x7F000124;

    /// <summary>The Add XP button.</summary>
    public const uint AddXpButtonId = 0x7F000125;

    /// <summary>The status text at the bottom.</summary>
    public const uint StatusId = 0x7F000126;

    /// <summary>The Hide panel button.</summary>
    public const uint HideButtonId = 0x7F000127;

    /// <summary>
    /// The Autopilot button (M5 item 4 h3, DoD 9): <see cref="ToggleAutopilot"/>, since M5 7 D145 the same as typing <c>/stop</c> while the autopilot runs and <c>/auto</c> otherwise (the play host wires it so; Num Lock no longer
    /// is the autopilot's key). The word "Autopilot" is Lifestone's own literal and the button a <b>placeholder</b>: retail never had one.
    /// </summary>
    public const uint AutopilotButtonId = 0x7F000128;

    /// <summary>
    /// M6 6 MD2 (L1): the Jobs button, above the Autopilot button: <see cref="ToggleJobs"/> opens and closes the automation menu (the play host wires it to the game screen's <c>Menu.Toggle</c>). The word "Jobs" is Lifestone's own
    /// literal and the button a <b>placeholder</b>: retail had no menu.
    /// </summary>
    public const uint JobsButtonId = 0x7F000129;

    /// <summary>The message the panel shows against a server that is not on loopback (a Lifestone literal on the retail message dialog).</summary>
    public const string NotLoopbackText = "The Lifestone tools work only against the bundled server on this computer. This server is not on loopback, so the tools are switched off.";

    /// <summary>The message the panel shows when the server has no Lifestone mod.</summary>
    public const string ModMissingText = "The Lifestone server mod is not loaded on this server, so the tools cannot work. Start the bundled server with its own script, which loads the mod.";

    private readonly UiLoginContext _context;
    private readonly UiElement _parent;
    private readonly IDisposable _unavailable;
    private readonly UiButton?[] _rows = new UiButton?[ToolsModel.PageSize];
    private UiTextElement? _search;
    private UiTextElement? _amount;
    private UiTextElement? _counter;
    private UiTextElement? _whereText;
    private UiTextElement? _statusText;
    private UiButton? _up;
    private UiButton? _down;
    private bool _windowBuilt;
    private uint _dialog;
    private bool _focusPending;
    private bool _disposed;
    private readonly Action<string> _chat;

    /// <summary>What the Autopilot button does: the play host wires /stop while the autopilot runs and /auto otherwise (M5 7 D145). Null until it is wired; a press then says so in the status line and the chat.</summary>
    public Action? ToggleAutopilot { get; set; }

    /// <summary>How many times the Autopilot button was pressed.</summary>
    public int AutopilotPresses { get; private set; }

    /// <summary>M6 6 MD2: what the Jobs button does: the play host wires the automation menu's toggle. Null until it is wired; a press then says so in the status line and the chat.</summary>
    public Action? ToggleJobs { get; set; }

    /// <summary>How many times the Jobs button was pressed.</summary>
    public int JobsPresses { get; private set; }

    /// <summary>The button's text when the dial is off or done (Lifestone's own literal; a placeholder button, retail never had one).</summary>
    public const string AutopilotStartText = "Autopilot";

    /// <summary>The button's text while the dial runs: a press asks it to stop.</summary>
    public const string AutopilotStopText = "Stop";

    /// <summary>The button's text while a stop is asked and the loop ends at its next safe point.</summary>
    public const string AutopilotStoppingText = "Stopping";

    /// <summary>The button's text while a person or the kill switch paused the dial: a press hands the controls back.</summary>
    public const string AutopilotResumeText = "Resume";

    /// <summary>
    /// What a press of the Autopilot button will do, as the button's text (M5 item 7 s1, 4-R17): Off and Done "Autopilot" (a press starts), Running "Stop", Stopping "Stopping", Interrupted and Halted "Resume".
    /// Lifestone's own literals on a placeholder button.
    /// </summary>
    /// <param name="mode">The dial's mode.</param>
    public static string AutopilotButtonText(AutopilotMode mode) => mode switch
    {
        AutopilotMode.Running => AutopilotStopText,
        AutopilotMode.Stopping => AutopilotStoppingText,
        AutopilotMode.Interrupted or AutopilotMode.Halted => AutopilotResumeText,
        _ => AutopilotStartText,
    };

    /// <summary>The text the Autopilot button shows now (the dial's mode through <see cref="Autopilot"/>; "Autopilot" with none wired).</summary>
    public string AutopilotText => AutopilotButtonText(_autopilot?.Status.Mode ?? AutopilotMode.Off);

    /// <summary>
    /// The autopilot whose mode the button's text follows: the play host sets the dial (it reads <see cref="IAutopilotStatusSource.Status"/> only, never the host). Its <see cref="IAutopilotStatusSource.Changed"/> rewrites the text.
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
                _autopilot.Changed -= RefreshAutopilotText;
            }

            _autopilot = value;
            if (value is not null && !_disposed)
            {
                value.Changed += RefreshAutopilotText;
            }

            RefreshAutopilotText();
        }
    }

    private IAutopilotStatusSource? _autopilot;
    private UiButton? _autopilotButton;

    /// <summary>The panel over <paramref name="parent"/> (the shell's root). The window is made when the panel is first opened (see <see cref="EnsureWindow"/>).</summary>
    /// <param name="context">The shell's context (the session and the element manager).</param>
    /// <param name="parent">The element the window is a child of.</param>
    /// <param name="chat">Shows a local line in the chat.</param>
    /// <param name="clientPosition">The client's physics body position, for Where am I (null: none yet; a host sets it later with <see cref="ToolsModel.ClientPosition"/>, play mode with the player's body).</param>
    public ToolsPanel(UiLoginContext context, UiElement parent, Action<string> chat, Func<CellFrame?>? clientPosition = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(chat);
        _context = context;
        _parent = parent;
        _chat = chat;
        Model = new ToolsModel(context.Session.Tools, clientPosition, chat, logError: message => context.Manager.Services.Log.Error(UiElementManager.Category, message));
        _unavailable = context.Session.Events.Subscribe<ToolsUnavailable>(OnToolsUnavailable);
        Model.Changed += Refresh;
    }

    /// <summary>
    /// Makes the window from <c>tools.json</c> the first time it is needed (the first F7): the build costs about 35 ms the first time (the fonts and glyphs of the dialog templates, which nothing
    /// else of the shell uses), and the frame that builds the shell is already the heaviest of the login (DoD 15: no frame over 50 ms), so the panel is not made with it. Null, with a named TODO
    /// logged, when the layout is missing or does not resolve.
    /// </summary>
    public UiElement? EnsureWindow()
    {
        if (_windowBuilt)
        {
            return Window;
        }

        _windowBuilt = true;
        Window = _context.Manager.CreateChildElement(_parent, LayoutId, WindowId);
        if (Window is null)
        {
            _context.Manager.Services.Log.Error(UiElementManager.Category, $"the tools panel: layout 0x{LayoutId:X8} element 0x{WindowId:X8} is missing or does not resolve; the tools panel has no window (TODO(g7-tools-layout))");
            return null;
        }

        for (var i = 0; i < _rows.Length; i++)
        {
            _rows[i] = Find<UiButton>(FirstRowId + (uint)i);
        }

        _search = Find<UiTextElement>(SearchId);
        _amount = Find<UiTextElement>(AmountId);
        _counter = Find<UiTextElement>(CounterId);
        _whereText = Find<UiTextElement>(WhereTextId);
        _statusText = Find<UiTextElement>(StatusId);
        _up = Find<UiButton>(UpId);
        _down = Find<UiButton>(DownId);
        _autopilotButton = Find<UiButton>(AutopilotButtonId);
        _ = Find<UiButton>(JobsButtonId); // M6 6 MD2: a layout without the Jobs button says so (TODO(g7-tools-layout)); its click is the element message below
        RefreshAutopilotText();
        Window.SetVisible(false);
        Window.RegisterForElementMessages(this);
        return Window;
    }

    /// <summary>What the panel does (the places, the three tools and their results).</summary>
    public ToolsModel Model { get; }

    /// <summary>The panel window (0x7F000100): null until the panel is first opened (<see cref="EnsureWindow"/>), and when the layout is missing.</summary>
    public UiElement? Window { get; private set; }

    /// <summary>True while the panel shows.</summary>
    public bool IsOpen => Window?.Visible == true;

    /// <summary>The search field (null when the layout lacks it).</summary>
    public UiTextElement? SearchField => _search;

    /// <summary>The Add XP amount field.</summary>
    public UiTextElement? AmountField => _amount;

    /// <summary>The place rows, in order.</summary>
    public IReadOnlyList<UiButton?> Rows => _rows;

    /// <summary>The message dialog the panel opened last and that is still open, or null.</summary>
    public UiDialog? Dialog => _dialog == 0 ? null : _context.Manager.Dialogs.Find(_dialog);

    /// <summary>The last message the panel put in a dialog (for the checks and the tests).</summary>
    public string? LastDialogText { get; private set; }

    /// <summary>How many times the refusal dialogs were shown.</summary>
    public int Refusals { get; private set; }

    /// <summary>Shows the panel and focuses the search field. Against a server that is not on loopback or without the mod it shows the message dialog instead and stays closed (false).</summary>
    public bool Open()
    {
        switch (Model.Gate)
        {
            case ToolsGate.NotLoopback:
                Refuse(NotLoopbackText);
                return false;
            case ToolsGate.ModMissing:
                Refuse(ModMissingText);
                return false;
        }

        if (EnsureWindow() is not { } window)
        {
            return false;
        }

        Model.ProbeIfNeeded();
        window.SetVisible(true);
        Refresh();

        // The search field takes the focus on the next frame, not inside the key press that opened the panel: a text field that gains the focus while a key is being handled makes the router
        // drop the next character (it takes it for that key's own), and F7 produces none, so the first letter typed would be lost (UiInputRouter.SetTextMode).
        _focusPending = true;
        return true;
    }

    /// <summary>Hides the panel.</summary>
    public void Close()
    {
        _focusPending = false;
        if (Window is { Visible: true })
        {
            if (_context.Manager.FocusElement is { } focused && (ReferenceEquals(focused, _search) || ReferenceEquals(focused, _amount)))
            {
                _context.Manager.SetFocusElement(null); // the typing goes back to the game
            }

            Window.SetVisible(false);
        }
    }

    /// <summary>F7: closes the panel when it shows, otherwise opens it (<see cref="Open"/>).</summary>
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

    /// <summary>Per frame: gives a newly opened panel's search field the focus and follows the field's text.</summary>
    public void Update()
    {
        if (_focusPending)
        {
            _focusPending = false;
            if (_search is not null && IsOpen)
            {
                _context.Manager.SetFocusElement(_search);
            }
        }

        if (_search is not null && IsOpen && !_search.TextEquals(Model.Query))
        {
            Model.SetQuery(_search.Text);
        }
    }

    /// <summary>Types <paramref name="text"/> into the search field (the checks and tests; typing does the same through the element).</summary>
    public void Search(string text)
    {
        _search?.SetText(text);
        Model.SetQuery(text);
    }

    /// <summary>Teleports to the row <paramref name="row"/> of the page shown (a click on it); false when the row is empty.</summary>
    public bool PickRow(int row)
    {
        var page = Model.Page;
        if (row < 0 || row >= page.Count)
        {
            return false;
        }

        Model.Teleport(page[row]);
        return true;
    }

    /// <inheritdoc/>
    public UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId != UiMessageIds.ButtonClick)
        {
            return UiListenResult.Default;
        }

        var id = message.Element.ElementId;
        if (id is >= FirstRowId and < FirstRowId + ToolsModel.PageSize)
        {
            PickRow((int)(id - FirstRowId));
            return UiListenResult.Handled;
        }

        switch (id)
        {
            case UpId:
                Model.Scroll(down: false);
                return UiListenResult.Handled;
            case DownId:
                Model.Scroll(down: true);
                return UiListenResult.Handled;
            case WhereButtonId:
                Model.WhereAmI();
                return UiListenResult.Handled;
            case AddXpButtonId:
                Model.AddXp(_amount?.Text);
                return UiListenResult.Handled;
            case AutopilotButtonId:
                PressAutopilot();
                return UiListenResult.Handled;
            case JobsButtonId:
                PressJobs();
                return UiListenResult.Handled;
            case HideButtonId:
                Close();
                return UiListenResult.Handled;
            default:
                return UiListenResult.Default;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_autopilot is not null)
        {
            _autopilot.Changed -= RefreshAutopilotText;
        }

        Model.Detach();
        _unavailable.Dispose();
        Model.Changed -= Refresh;
    }

    private T? Find<T>(uint elementId)
        where T : UiElement
    {
        if (Window?.GetChildRecursive(elementId) is T found)
        {
            return found;
        }

        _context.Manager.LogOnce($"tools:{elementId:X8}", LogSeverity.Error, $"the tools panel: element 0x{elementId:X8} is missing from tools.json or is not a {typeof(T).Name}; that part of the panel does nothing (TODO(g7-tools-layout))");
        return null;
    }

    /// <summary>The Autopilot button's press (the checks and tests press it by this too): the toggle the host wired, or a plain line when none is.</summary>
    public void PressAutopilot()
    {
        AutopilotPresses++;
        if (ToggleAutopilot is { } toggle)
        {
            toggle();
            return;
        }

        const string unwired = "The autopilot is not available in this host.";
        Model.SetStatus(unwired);
        _chat("Lifestone: " + unwired);
        _context.Manager.Services.Log.Warn(UiElementManager.Category, "the Autopilot button was pressed but nothing is wired to it (m5-autopilot-key-unwired (closed): the play host wires ToggleAutopilot when it starts)");
    }

    /// <summary>M6 6 MD2: the Jobs button's press (the checks and tests press it by this too): the menu toggle the host wired, or a plain line when none is.</summary>
    public void PressJobs()
    {
        JobsPresses++;
        if (ToggleJobs is { } toggle)
        {
            toggle();
            return;
        }

        const string unwired = "The automation menu is not available in this host.";
        Model.SetStatus(unwired);
        _chat("Lifestone: " + unwired);
        _context.Manager.Services.Log.Warn(UiElementManager.Category, "the Jobs button was pressed but nothing is wired to it (the play host wires ToggleJobs to the automation menu when it starts)");
    }

    private void RefreshAutopilotText() => _autopilotButton?.SetText(AutopilotText);

    private void Refresh()
    {
        if (Window is null)
        {
            return;
        }

        var page = Model.Page;
        for (var i = 0; i < _rows.Length; i++)
        {
            var row = _rows[i];
            if (row is null)
            {
                continue;
            }

            row.SetText(i < page.Count ? page[i].Label : string.Empty);
            row.SetVisible(i < page.Count);
        }

        _counter?.SetText(Model.Counter);
        _whereText?.SetText(Model.WhereText);
        _statusText?.SetText(Model.Status);
        _up?.SetState(Model.Top > 0 ? UiStateIds.Normal : UiStateIds.Ghosted);
        _down?.SetState(Model.Top + ToolsModel.PageSize < Model.Results.Count ? UiStateIds.Normal : UiStateIds.Ghosted);
    }

    // A refusal learned while the panel shows (a request refused, the mod found missing): the same message dialogs as at the opening.
    private void OnToolsUnavailable(in ToolsUnavailable e)
    {
        if (!IsOpen)
        {
            return;
        }

        switch (e.Availability)
        {
            case ToolAvailability.NotLoopback:
                Refuse(NotLoopbackText);
                Close();
                break;
            case ToolAvailability.ModMissing:
                Refuse(ModMissingText);
                Close();
                break;
        }
    }

    private void Refuse(string text)
    {
        Refusals++;
        LastDialogText = text;
        Model.SetStatus(text);
        _context.Manager.Services.Log.Warn(UiElementManager.Category, "the Lifestone tools are not available: " + text);
        if (_dialog != 0 && _context.Manager.Dialogs.Find(_dialog) is not null)
        {
            return; // one at a time
        }

        _chat("Lifestone: " + text); // the refusal is in the chat too, like every other use of the tools
        var data = UiDialogData.Of(UiDialogType.Message, UiStringRef.FromPlainText(text)).WithFlag(UiPropertyIds.DialogModal, true);
        _dialog = _context.Manager.Dialogs.MakeDialog(data);
    }
}
