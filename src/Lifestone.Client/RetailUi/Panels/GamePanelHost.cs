using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>What a panel page of the host does in M2.</summary>
public enum PanelPageKind
{
    /// <summary>The page's content needs gameplay (m2-log-in section 5.3): opening it shows the retail message dialog that says so (<see cref="NotInM2"/>).</summary>
    NeedsGameplay,

    /// <summary>A page M2 builds later (g6: the options and the read-only character information): opening it calls the handler registered with <see cref="GamePanelHost.SetPageHandler"/>, else logs a named TODO.</summary>
    M2Hook,
}

/// <summary>One page of the panel host: a child of the host window that carries a panel id (property 0x10000029) and an input action that toggles it (property 0x57).</summary>
/// <param name="PanelId">The panel id (the enum of property 0x10000029).</param>
/// <param name="ElementId">The page's element id in <c>classic_floatypanel</c>.</param>
/// <param name="Name">The panel's English name, as the message dialog says it.</param>
/// <param name="Kind">What opening it does in M2.</param>
/// <param name="Todo">The named TODO an unfilled <see cref="PanelPageKind.M2Hook"/> logs.</param>
public sealed record PanelPage(uint PanelId, uint ElementId, string Name, PanelPageKind Kind, string? Todo = null);

/// <summary>
/// The panel host (<c>gmFloatyPanelUI</c>, window 0x100005FF of <c>classic_floatypanel</c>; docs/retail-ui.md section 21.5): the floating window the toolbar's panel buttons open pages in. Its sixteen pages are
/// children of the window's page frame; <c>gmPanelUI::SetupChildren</c> (11.6096 0x004BD670) finds each by id with its panel id and hides it, and <c>PostInit</c> (0x004BDCA0) hides the window itself while no panel is
/// shown. <b>So the host is hidden at login</b> and nothing shows until a toolbar button (or a key) toggles a page.
/// </summary>
/// <remarks>
/// <para><b>How a page opens.</b> A toolbar button fires its input action (property 0x12). In retail the page registered for that action (property 0x57) toggles its own visibility and the host, which hears the page
/// (<c>gmPanelUI::ListenToElementMessage</c>, 11.6096 0x004BD300), tells the shell which panel is shown (<c>RecvNotice_SetPanelVisibility</c>, 0x004BD380). Lifestone registers the host window for the pages' actions
/// instead (a deliberate change: a tabbed page shows itself again whenever it is hidden, so hiding a page that must not open looped without end) and answers the action by the page's <see cref="PanelPageKind"/>: a
/// gameplay page opens the NeedsGameplay message ("Not in Lifestone M2: &lt;panel&gt;"), a g6 page calls its handler. A handler that has content shows its page with <see cref="SetPanelVisibility"/>, which is
/// retail's notice (one page at a time; a page that overlays another remembers it and brings it back when it closes); the same action closes the page shown.</para>
/// </remarks>
public sealed class GamePanelHost : IUiElementListener
{
    /// <summary>The host window.</summary>
    public const uint WindowId = 0x100005FF;

    /// <summary>The pages in the order <c>SetupChildren</c> looks for them (11.6096 0x004BD670), with the names the M2 message dialog uses.</summary>
    public static IReadOnlyList<PanelPage> Pages { get; } =
    [
        new(7, 0x1000018B, "Inventory, backpack and paperdoll", PanelPageKind.NeedsGameplay),
        new(12, 0x1000018F, "Fellowship, allegiance and friends", PanelPageKind.NeedsGameplay),
        new(11, 0x1000018E, "Skills and attributes", PanelPageKind.NeedsGameplay),
        new(25, 0x10000559, "Contracts, journal and quests", PanelPageKind.NeedsGameplay),
        new(16, 0x1000018C, "Map", PanelPageKind.NeedsGameplay),
        new(2, 0x10000182, "Books", PanelPageKind.NeedsGameplay),
        new(10, 0x1000018D, "Options", PanelPageKind.M2Hook, "TODO(g6-options): the options panel is built by g6"),
        new(13, 0x10000190, "Spellbook and components", PanelPageKind.NeedsGameplay),
        new(4, 0x10000184, "Beneficial effects", PanelPageKind.NeedsGameplay),
        new(5, 0x10000185, "Harmful effects", PanelPageKind.NeedsGameplay),
        new(1, 0x10000181, "Reporting abuse", PanelPageKind.NeedsGameplay),
        new(14, 0x10000189, "Customer assistance", PanelPageKind.NeedsGameplay),
        new(3, 0x10000183, "Character information", PanelPageKind.M2Hook, "TODO(g6-character-info): the read-only character information is built by g6"),
        new(15, 0x1000018A, "Vitae", PanelPageKind.NeedsGameplay),
        new(8, 0x10000187, "Link status details", PanelPageKind.NeedsGameplay),
        new(9, 0x10000188, "Mini-games", PanelPageKind.NeedsGameplay),
    ];

    /// <summary>Property 0x10000049: the page overlays the page below it, which comes back when it closes (read by <c>RecvNotice_SetPanelVisibility</c>).</summary>
    private const uint OverlayPropertyId = 0x10000049;

    private readonly UiLoginContext _context;
    private readonly Dictionary<uint, (PanelPage Page, UiElement Element)> _byPanel = [];
    private readonly Dictionary<uint, PanelPage> _byAction = [];
    private readonly Dictionary<uint, Action> _handlers = [];
    private PanelPage? _current;
    private PanelPage? _previous;

    /// <summary>The host of <paramref name="window"/> (null when the layout lacks it): finds the pages and hides them and the window (<c>PostInit</c>).</summary>
    public GamePanelHost(UiLoginContext context, UiElement? window)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        Window = window;
        if (window is null)
        {
            return;
        }

        foreach (var page in Pages)
        {
            if (window.GetChildRecursive(page.ElementId) is { } element)
            {
                _byPanel[page.PanelId] = (page, element);
            }
            else
            {
                context.Manager.Services.Log.Error(UiElementManager.Category, $"gmPanelUI::SetupChildren: the page 0x{page.ElementId:X8} ({page.Name}) is missing from the layout; its button will do nothing (TODO(g4-panel-page))");
            }
        }

        // Retail registers each page for its toggle action (property 0x57) and the page toggles itself. A tabbed page (a UiPanel) shows its tab again whenever it is hidden, so a page Lifestone hides at once
        // would show itself again without end. The host takes the actions instead: it is registered for them in the pages' place, hears the toggle message about itself and answers the page.
        foreach (var (page, element) in _byPanel.Values)
        {
            var action = element.VisibilityToggleAction;
            context.Manager.UnregisterElementForAllInputActions(element);
            if (action != 0)
            {
                _byAction[action] = page;
                context.Manager.RegisterElementForInputAction(window, action);
            }
        }

        foreach (var (_, element) in _byPanel.Values)
        {
            element.SetVisible(false); // SetupChildren hides every page
        }

        window.SetVisible(false); // PostInit: no panel is shown, so the window is hidden
        window.RegisterForElementMessages(this);
    }

    /// <summary>The host window (0x100005FF), or null.</summary>
    public UiElement? Window { get; }

    /// <summary>The panel shown now (its panel id), or 0 when none is.</summary>
    public uint CurrentPanel => _current?.PanelId ?? 0;

    /// <summary>True while the host window shows.</summary>
    public bool IsOpen => Window?.Visible == true;

    /// <summary>The page with panel id <paramref name="panelId"/>, or null.</summary>
    public PanelPage? PageFor(uint panelId) => _byPanel.TryGetValue(panelId, out var entry) ? entry.Page : null;

    /// <summary>The page element for <paramref name="panelId"/>, or null.</summary>
    public UiElement? ElementFor(uint panelId) => _byPanel.TryGetValue(panelId, out var entry) ? entry.Element : null;

    /// <summary>g6 fills this: <paramref name="open"/> runs when the <see cref="PanelPageKind.M2Hook"/> page <paramref name="panelId"/> is asked to open. It shows its page with <see cref="SetPanelVisibility"/>.</summary>
    public void SetPageHandler(uint panelId, Action open)
    {
        ArgumentNullException.ThrowIfNull(open);
        _handlers[panelId] = open;
    }

    /// <summary>
    /// <c>gmPanelUI::RecvNotice_SetPanelVisibility</c> (11.4186 0x004BC6F0, 11.6096 0x004BD380): shows or hides the page of <paramref name="panelId"/>. Showing hides the page shown before (remembering it when the new
    /// page overlays it) and shows the window; hiding the shown page brings back the remembered one or hides the window.
    /// </summary>
    public void SetPanelVisibility(uint panelId, bool visible)
    {
        if (panelId == 0 || Window is null || !_byPanel.TryGetValue(panelId, out var entry))
        {
            return;
        }

        var (page, element) = entry;
        if (visible)
        {
            if (_current != page)
            {
                var old = _current;
                _current = page;
                if (old is not null && _byPanel[old.PanelId].Element.Visible)
                {
                    var overlay = element.TryGetBoolProperty(OverlayPropertyId, out var newOverlay) && newOverlay;
                    var oldOverlay = _byPanel[old.PanelId].Element.TryGetBoolProperty(OverlayPropertyId, out var o) && o;
                    _previous = overlay && !oldOverlay ? old : null;
                    SetPanelVisibility(old.PanelId, false);
                }
            }

            element.SetVisible(true);
            Window.SetVisible(true);
        }
        else if (_current == page)
        {
            element.SetVisible(false);
            if (_previous is { } previous)
            {
                _previous = null;
                SetPanelVisibility(previous.PanelId, true);
            }
            else
            {
                _current = null;
                Window.SetVisible(false);
            }
        }
        else
        {
            element.SetVisible(false);
        }
    }

    /// <summary>
    /// Hears a page's toggle action (<c>gmPanelUI::ListenToElementMessage</c>, 11.6096 0x004BD300, hears the page itself in retail): the page shown closes, any other is answered by its kind (a NeedsGameplay message, or
    /// g6's handler).
    /// </summary>
    public UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId != UiMessageIds.VisibilityToggleAction || !ReferenceEquals(message.Element, Window) || !_byAction.TryGetValue(message.Param1, out var page))
        {
            return UiListenResult.Default;
        }

        if (_current == page && _byPanel[page.PanelId].Element.Visible)
        {
            SetPanelVisibility(page.PanelId, false);
        }
        else
        {
            Open(page);
        }

        return UiListenResult.Handled;
    }

    private void Open(PanelPage page)
    {
        var log = _context.Manager.Services.Log;
        if (page.Kind == PanelPageKind.NeedsGameplay)
        {
            NotInM2.Show(_context.Manager.Dialogs, log, page.Name);
        }
        else if (_handlers.TryGetValue(page.PanelId, out var open))
        {
            open();
        }
        else
        {
            log.Warn(UiElementManager.Category, $"placeholder: the '{page.Name}' panel does nothing yet; {page.Todo}");
        }
    }
}
