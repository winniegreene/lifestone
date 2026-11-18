using System.Globalization;
using System.Text;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The house guardian's window (M6 villa): <c>gmSlumlordUI</c> (class 0x10000013, window 0x10000060 of the gameplay layout, panel id 0x15) in the environment floaty <c>gmFloatyEnvPanelUI</c>, over the open guardian
/// (<see cref="GameState.Guardian"/>, GameEvent 0x021D HouseProfile) and the guardian window's model (<see cref="IGameSessionView.GuardianView"/>, over the session's <c>GuardianWindowModel</c>: the panes, the error line, the Buy and Pay presses, shared with the autopilot). The
/// window only shows the model and forwards to it: it keeps no copy of the panes and never sends on the wire.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout</b> (layout 0x2100000E, 600 by 110; <c>cache/m4/2-2/layout-envpanel.txt</c>). The window is itself a tab panel (its property 0x2E pairs two tabs with two pages, the first open): the tab "Buy" 0x1000008E (string 0x0365C892 of
/// table 0x23000001) with the page 0x10000090, and the tab "Maintenance" 0x1000008F (0x0AAEFAF2) with the page 0x10000097; the tab bar's art 0x1000008D and the close button 0x1000009E. Each page holds a text of 245 by 87 on the left
/// (0x10000091, 0x10000098) and on the right a field (0x10000092, 0x10000099) with a one-line text (0x10000093, 0x1000009A), the page's button ("Buy" 0x10000094, string 0x0FB28D0E; "Maintenance" 0x1000009B, 0x0C9411BE, which is the
/// Pay button) and a one-row item list of 32-pixel cells with its scroll bar (0x10000095 and 0x10000096, 0x1000009C and 0x1000009D).
/// </para>
/// <para>
/// <b>What each part shows (Lifestone's reading).</b> The left text lists the pane's price lines, each with what is paid, what the placed stacks bring and what is short (<see cref="GuardianWindowView.Lines"/>); the item list shows the
/// stacks placed in the pane (<see cref="GuardianWindowView.Placed"/>; an item dragged from the packs onto the window is <see cref="GuardianWindowView.Place"/>, a double click on a placed cell is <see cref="GuardianWindowView.Remove"/>);
/// the one-line text (widened to two lines, <see cref="StatusWidth"/>) is the window's error line (<see cref="GuardianWindowView.ErrorLine"/>), or the outcome of the last press, or a hint. The window adds a title on the tab bar (the dwelling's kind and owner). Retail fills the texts and
/// the list in <c>gmSlumlordUI</c>'s own handlers, whose addresses are not in the project's notes (<c>TODO(villa-retail-slumlord-recv)</c>), so every text here is Lifestone's wording, said on screen as a placeholder in the title
/// (<see cref="PlaceholderTag"/>) and logged once (<c>TODO(villa-retail-slumlord-texts)</c>); whether retail's list shows the placed stacks or the price lines is unread (<c>TODO(villa-retail-slumlord-list)</c>).
/// </para>
/// <para>
/// <b>Which pane shows.</b> The layout keeps both tabs, so both stay: the window opens on the purchase pane for an unowned dwelling (its terms and Buy) and on the maintenance pane for an owned one (its rent and Pay), and switches to
/// maintenance when a refreshed profile shows the purchase went through. A button that cannot act for the profile is ghosted (the layout gives both buttons a ghosted state, 0x0D): Buy for an owned dwelling, Maintenance for an
/// unowned one or one with no maintenance owed (<c>TODO(villa-retail-slumlord-ghost)</c>: retail's rule is unread). Otherwise a press goes to the model, which checks and refuses with its error line.
/// </para>
/// <para>
/// <b>Follows the model.</b> It opens on <see cref="GuardianOpened"/> (the layout shows the window by default, so it hides itself until then, and the floaty follows), redraws on <see cref="GuardianProfileChanged"/> (and shows again if
/// the close button hid it: the player used the guardian again) and every <see cref="RefreshFrames"/> frames while shown (the placed stacks follow the packs), and closes on <see cref="GuardianClosed"/>. The close button closes the open guardian
/// in the model too (<see cref="IGameSessionView.CloseGuardianWindow"/>, as the vendor window's close does), so the window and the model agree.
/// </para>
/// </remarks>
public sealed class GameSlumlordPanel : UiPanel
{
    /// <summary>The retail class id (<c>gmSlumlordUI</c>).</summary>
    public const uint RetailClassId = 0x10000013;

    /// <summary>The window's element id in the gameplay layout (inside <see cref="UiFloatyEnvPanel"/>).</summary>
    public const uint WindowId = 0x10000060;

    /// <summary>The layout the window's parts come from.</summary>
    public const uint LayoutId = 0x2100000E;

    /// <summary>The tab of the purchase pane ("Buy").</summary>
    public const uint BuyTabId = 0x1000008E;

    /// <summary>The tab of the maintenance pane ("Maintenance").</summary>
    public const uint RentTabId = 0x1000008F;

    /// <summary>The purchase page.</summary>
    public const uint BuyPageId = 0x10000090;

    /// <summary>The maintenance page.</summary>
    public const uint RentPageId = 0x10000097;

    /// <summary>The purchase page's left text (the price lines).</summary>
    public const uint BuyTermsTextId = 0x10000091;

    /// <summary>The maintenance page's left text (the rent lines).</summary>
    public const uint RentTermsTextId = 0x10000098;

    /// <summary>The purchase page's one-line text (the error line).</summary>
    public const uint BuyStatusTextId = 0x10000093;

    /// <summary>The maintenance page's one-line text (the error line).</summary>
    public const uint RentStatusTextId = 0x1000009A;

    /// <summary>The Buy button.</summary>
    public const uint BuyButtonId = 0x10000094;

    /// <summary>The Maintenance button (Pay).</summary>
    public const uint PayButtonId = 0x1000009B;

    /// <summary>The purchase pane's item list (the placed stacks).</summary>
    public const uint BuyListId = 0x10000095;

    /// <summary>The maintenance pane's item list (the placed stacks).</summary>
    public const uint RentListId = 0x1000009C;

    /// <summary>The close button.</summary>
    public const uint CloseButtonId = 0x1000009E;

    /// <summary>The one-line texts' width and height in Lifestone (the layout's are 150 by 16): the field's width left of the button, two lines above the list.</summary>
    public const int StatusWidth = 250, StatusHeight = 32;

    /// <summary>The frames between two redraws while the window shows (the placed stacks follow the packs).</summary>
    public const int RefreshFrames = 15;

    /// <summary>What the title says of its own texts (the placeholder rule: retail's texts are unread).</summary>
    public const string PlaceholderTag = "[placeholder texts]";

    /// <summary>The one-line text's hint when no error shows and the pane is not covered.</summary>
    public const string DragHint = "Drag the payment here";

    /// <summary>The one-line text when the pane covers every line.</summary>
    public const string ReadyText = "The payment is placed";

    private readonly List<(uint Guid, UiItemIcon Icon)> _rows = new(8);
    private UiLoginContext? _context;
    private UiTextElement? _title;
    private UiTextElement? _buyTerms;
    private UiTextElement? _rentTerms;
    private UiTextElement? _buyStatus;
    private UiTextElement? _rentStatus;
    private UiButton? _buyButton;
    private UiButton? _payButton;
    private UiItemList? _buyList;
    private UiItemList? _rentList;
    private IDisposable[] _subscriptions = [];
    private uint _shownGuardian;
    private bool _shownOwned;
    private bool _frameRegistered;
    private int _framesSinceRefresh;
    private ActionHandle? _pending;
    private string _outcomeLine = string.Empty;
    private string _titleText = string.Empty;
    private string _buyTermsText = string.Empty;
    private string _rentTermsText = string.Empty;
    private string _buyStatusText = string.Empty;
    private string _rentStatusText = string.Empty;

    /// <summary>The window of <paramref name="init"/>.</summary>
    public GameSlumlordPanel(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>The guardian the window shows now (0 while it is hidden).</summary>
    public uint ShownGuardian => Visible ? _shownGuardian : 0;

    /// <summary>The pane of the open tab.</summary>
    public GuardianWindowPane OpenPane => OpenTabId == RentTabId ? GuardianWindowPane.Rent : GuardianWindowPane.Buy;

    /// <summary>The title on the tab bar.</summary>
    public string Title => _titleText;

    /// <summary>The purchase page's left text as drawn.</summary>
    public string BuyTermsText => _buyTermsText;

    /// <summary>The maintenance page's left text as drawn.</summary>
    public string RentTermsText => _rentTermsText;

    /// <summary>The purchase page's one-line text as drawn.</summary>
    public string BuyStatusText => _buyStatusText;

    /// <summary>The maintenance page's one-line text as drawn.</summary>
    public string RentStatusText => _rentStatusText;

    /// <summary>The purchase pane's list (a test reads its cells).</summary>
    public UiItemList? BuyRow => _buyList;

    /// <summary>The maintenance pane's list.</summary>
    public UiItemList? RentRow => _rentList;

    /// <summary>The last press the window forwarded that the model sent (a BuyHouse or a RentHouse), or null.</summary>
    public ActionHandle? LastPress { get; private set; }

    /// <summary>How many presses the window forwarded to the model (sent or refused).</summary>
    public int Presses { get; private set; }

    private GuardianWindowView? View => _context?.Session.GuardianView;

    /// <summary>The window's parts are found by id; the layout shows the window by default, so it hides itself (the floaty follows). Subscribes to nothing until <see cref="Attach"/>.</summary>
    protected internal override void PostInit()
    {
        base.PostInit();
        _buyTerms = GetChildRecursive(BuyTermsTextId) as UiTextElement;
        _rentTerms = GetChildRecursive(RentTermsTextId) as UiTextElement;
        _buyStatus = GetChildRecursive(BuyStatusTextId) as UiTextElement;
        _rentStatus = GetChildRecursive(RentStatusTextId) as UiTextElement;
        _buyButton = GetChildRecursive(BuyButtonId) as UiButton;
        _payButton = GetChildRecursive(PayButtonId) as UiButton;
        _buyList = GetChildRecursive(BuyListId) as UiItemList;
        _rentList = GetChildRecursive(RentListId) as UiItemList;
        foreach (var status in (ReadOnlySpan<UiTextElement?>)[_buyStatus, _rentStatus])
        {
            // The layout's one line (0,15, 150 by 16) cuts the model's error lines ("The purchase pane does not cover the price."): Lifestone gives it the field's free width left of the button and two lines above the list
            // (TODO(villa-slumlord-status-width): retail's texts, and so whether they fit its line, are unread).
            status?.MoveTo(0, 1);
            status?.ResizeTo(StatusWidth, StatusHeight);
        }

        SetVisible(false);
        (Parent as UiFloatyEnvPanel)?.UpdateVisibility();
        if (_buyList is not null)
        {
            _buyList.ItemActivated += OnPlacedActivated;
        }

        if (_rentList is not null)
        {
            _rentList.ItemActivated += OnPlacedActivated;
        }
    }

    /// <summary>Hands the window the session: it follows the guardian events, makes its title and opens at once when the session holds an open guardian now.</summary>
    public void Attach(UiLoginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        foreach (var earlier in _subscriptions)
        {
            earlier.Dispose();
        }

        var events = context.Session.Events;
        _subscriptions =
        [
            events.Subscribe<GuardianOpened>(OnOpened),
            events.Subscribe<GuardianProfileChanged>(OnProfileChanged),
            events.Subscribe<GuardianClosed>(OnClosed),
        ];
        var log = context.Manager.Services.Log;
        if (_buyTerms is null || _rentTerms is null || _buyStatus is null || _rentStatus is null || _buyButton is null || _payButton is null || _buyList is null || _rentList is null || GetChildRecursive(CloseButtonId) is not UiButton)
        {
            log.Error(UiElementManager.Category, "gmSlumlordUI: the window lacks one of its texts, buttons or lists; the guardian window cannot buy or pay (TODO(villa-slumlord-window-missing))");
        }

        if (!context.Session.GuardianView.HasModel)
        {
            log.Error(UiElementManager.Category, "gmSlumlordUI: the session has no guardian window model; the window shows the terms but cannot place, buy or pay (TODO(villa-slumlord-no-model))");
        }

        MakeTitle(context);
        context.Manager.Input.Drag.RegisterTarget(this, new GameSlumlordDrop(this));
        log.Warn(
            "retailui",
            "gmSlumlordUI: placeholder: TODO(villa-retail-slumlord-recv) retail's handlers of the window are not in the notes; TODO(villa-retail-slumlord-texts) the title, the price lines and the one-line texts are Lifestone's wording (the title says "
            + PlaceholderTag + "); TODO(villa-retail-slumlord-list) the item list shows the placed stacks; TODO(villa-retail-slumlord-ghost) the ghosting of Buy and Maintenance is Lifestone's rule");
        if (context.Session.GuardianView.IsOpen)
        {
            Open();
        }
    }

    /// <summary>Shows the window for the open guardian, on the pane its ownership asks for, and draws it (what <see cref="GuardianOpened"/> does).</summary>
    public void Open()
    {
        if (View is not { IsOpen: true } guardian)
        {
            return;
        }

        if (_shownGuardian != guardian.Guardian)
        {
            _buyList?.FlushItems();
            _rentList?.FlushItems();
            _outcomeLine = string.Empty;
            _pending = null;
        }

        _shownGuardian = guardian.Guardian;
        _shownOwned = guardian.IsOwned;
        SetVisible(true);
        (Parent as UiFloatyEnvPanel)?.UpdateVisibility();
        OpenTab(guardian.IsOwned ? RentTabId : BuyTabId);
        RegisterFrames();
        Redraw();
    }

    /// <summary>Hides the window (what <see cref="GuardianClosed"/> and the close button do); nothing is sent and the model's panes are left as they are (the model empties them for another guardian).</summary>
    public void Close()
    {
        _shownGuardian = 0;
        _pending = null;
        _outcomeLine = string.Empty;
        _buyList?.FlushItems();
        _rentList?.FlushItems();
        SetVisible(false);
        (Parent as UiFloatyEnvPanel)?.UpdateVisibility();
        UnregisterFrames();
    }

    /// <summary>Opens the pane <paramref name="pane"/>'s tab.</summary>
    public bool OpenPaneTab(GuardianWindowPane pane) => OpenTab(pane == GuardianWindowPane.Rent ? RentTabId : BuyTabId);

    /// <summary>Drags <paramref name="item"/> into the open pane (a drop on the window): <see cref="GuardianWindowView.Place"/>; a refusal shows the model's error line.</summary>
    public bool PlaceItem(uint item)
    {
        if (View is not { HasModel: true } view)
        {
            return false;
        }

        var placed = view.Place(OpenPane, item);
        _outcomeLine = string.Empty;
        _context?.Manager.Services.Log.Info("retailui", $"guardian window: place 0x{item:X8} in the {OpenPane} pane: " + (placed ? "placed" : "refused: " + view.ErrorLine));
        Redraw();
        return placed;
    }

    /// <summary>Takes <paramref name="item"/> out of the open pane (a double click on its cell): <see cref="GuardianWindowView.Remove"/>.</summary>
    public bool RemoveItem(uint item)
    {
        var removed = View?.Remove(OpenPane, item) ?? false;
        Redraw();
        return removed;
    }

    /// <summary>The Buy button: <see cref="GuardianWindowView.PressBuy"/> from the UI. Null when the model refused (its error line shows) or the button is ghosted.</summary>
    public ActionHandle? PressBuy() => Press(GuardianWindowPane.Buy);

    /// <summary>The Maintenance button: <see cref="GuardianWindowView.PressPay"/> from the UI. Null when the model refused (its error line shows) or the button is ghosted.</summary>
    public ActionHandle? PressPay() => Press(GuardianWindowPane.Rent);

    private ActionHandle? Press(GuardianWindowPane pane)
    {
        if (View is not { HasModel: true } view || (pane == GuardianWindowPane.Buy ? _buyButton : _payButton) is { Ghosted: true })
        {
            return null;
        }

        Presses++;
        _outcomeLine = string.Empty;
        var handle = pane == GuardianWindowPane.Buy ? view.PressBuy(ActionSource.Ui) : view.PressPay(ActionSource.Ui);
        _context?.Manager.Services.Log.Info("retailui", "guardian window: " + (pane == GuardianWindowPane.Buy ? "Buy" : "Maintenance") + " pressed: " + (handle is null ? "refused: " + view.ErrorLine : "sent: " + handle.Summary));
        if (handle is not null)
        {
            LastPress = handle;
            _pending = handle;
        }

        Redraw();
        return handle;
    }

    /// <summary>Redraws both pages from the model: the title, the price lines, the one-line texts, the placed stacks and the buttons' ghosting.</summary>
    public void Redraw()
    {
        if (View is not { IsOpen: true } view || !Visible)
        {
            return;
        }

        if (view.IsOwned && !_shownOwned)
        {
            OpenTab(RentTabId); // the purchase went through: the maintenance pane is the one that acts now
        }

        _shownOwned = view.IsOwned;
        _framesSinceRefresh = 0;
        SetText(_title, ref _titleText, TitleOf(view) + "  " + PlaceholderTag);
        SetText(_buyTerms, ref _buyTermsText, TermsText(view, GuardianWindowPane.Buy));
        SetText(_rentTerms, ref _rentTermsText, TermsText(view, GuardianWindowPane.Rent));
        SetText(_buyStatus, ref _buyStatusText, StatusText(GuardianWindowPane.Buy, view));
        SetText(_rentStatus, ref _rentStatusText, StatusText(GuardianWindowPane.Rent, view));
        Ghost(_buyButton, view.IsOwned);
        Ghost(_payButton, !view.IsOwned || view.IsRentPaid);
        FillPlaced(_buyList, view, GuardianWindowPane.Buy);
        FillPlaced(_rentList, view, GuardianWindowPane.Rent);
    }

    /// <summary>The title: the dwelling's kind and whether it is for sale or whose it is.</summary>
    public static string TitleOf(GuardianWindowView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.IsOwned ? view.KindName + ", owned by " + (view.OwnerName.Length > 0 ? view.OwnerName : "someone") : view.KindName + ", for sale";
    }

    /// <summary>
    /// The left text of <paramref name="pane"/>: a heading, then one line per price line, "&lt;count&gt; &lt;name&gt;: " and what is paid, what the placed stacks bring and what is short (<see cref="GuardianWindowView.Lines"/>).
    /// The purchase pane of an unowned dwelling starts with the level asked for.
    /// </summary>
    public static string TermsText(GuardianWindowView view, GuardianWindowPane pane)
    {
        ArgumentNullException.ThrowIfNull(view);
        var text = new StringBuilder(160);
        if (pane == GuardianWindowPane.Buy)
        {
            if (!view.IsOwned && view.MinLevel > 0)
            {
                text.Append("Level ").Append(view.MinLevel.ToString(CultureInfo.InvariantCulture)).Append(" or higher may buy.\n");
            }

            text.Append(view.IsOwned ? "The purchase price (paid):" : "The purchase price:");
        }
        else if (view.MaintenanceFree)
        {
            text.Append("No maintenance is due this period.");
        }
        else
        {
            text.Append(!view.IsOwned ? "The maintenance once bought:" : view.IsRentPaid ? "The maintenance this period (paid):" : "The maintenance this period:");
        }

        var lines = view.Lines(pane);
        if (lines.Length == 0)
        {
            text.Append("\nnothing");
        }

        foreach (var line in lines)
        {
            AppendLine(text, line);
        }

        return text.ToString();
    }

    private static void AppendLine(StringBuilder text, in GuardianPriceLine line)
    {
        text.Append('\n').Append(Count(line.Required)).Append(' ').Append(line.NameFor(line.Required)).Append(": ");
        var first = true;
        if (line.Paid > 0)
        {
            text.Append(line.Paid >= line.Required ? "paid" : Count(line.Paid) + " paid");
            first = false;
        }

        if (line.Placed > 0)
        {
            text.Append(first ? string.Empty : ", ").Append(Count(line.Placed)).Append(" placed");
            first = false;
        }

        if (line.Missing > 0)
        {
            text.Append(first ? string.Empty : ", ").Append(Count(line.Missing)).Append(" short");
        }
        else if (first)
        {
            text.Append("paid");
        }
    }

    private static string Count(long n) => n.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The one-line text of <paramref name="pane"/>: the model's error line, else the outcome of the last press, else a hint.</summary>
    private string StatusText(GuardianWindowPane pane, GuardianWindowView view)
    {
        if (view.ErrorLine is { Length: > 0 } error)
        {
            return error;
        }

        if (_outcomeLine.Length > 0)
        {
            return _outcomeLine;
        }

        if (!view.HasModel || !view.IsOpen || (pane == GuardianWindowPane.Buy ? view.IsOwned : !view.IsOwned || view.IsRentPaid))
        {
            return string.Empty;
        }

        return view.Covers(pane) ? ReadyText : DragHint;
    }

    private void FillPlaced(UiItemList? list, GuardianWindowView view, GuardianWindowPane pane)
    {
        if (list is null || _context is null)
        {
            return;
        }

        _rows.Clear();
        var objects = _context.Session.State.Objects;
        foreach (var guid in view.Placed(pane))
        {
            if (objects.TryGet(guid, out var item))
            {
                _rows.Add((guid, GameInventoryPage.IconOf(item)));
            }
        }

        var shown = 0;
        for (; shown < _rows.Count; shown++)
        {
            if ((shown >= list.CellCount && !list.EnsureCells(shown + 1)) || list.CellAt(shown) is not { } cell)
            {
                break;
            }

            cell.ShowItem(_rows[shown].Guid, _rows[shown].Icon);
        }

        for (var i = list.CellCount - 1; i >= shown; i--)
        {
            if (list.CellAt(i) is { IsEmpty: false } leftover)
            {
                leftover.Clear();
            }
        }

        list.UpdateEmptySlots();
    }

    private static void Ghost(UiButton? button, bool ghosted)
    {
        if (button is null || button.Ghosted == ghosted)
        {
            return;
        }

        button.SetState(ghosted ? UiStateIds.Ghosted : UiStateIds.Normal);
    }

    private static void SetText(UiTextElement? element, ref string shown, string text)
    {
        if (shown == text)
        {
            return;
        }

        shown = text;
        element?.SetText(text);
    }

    /// <summary>Makes the title text from the purchase page's one-line text (the same font and colour), on the tab bar right of the two tabs.</summary>
    private void MakeTitle(UiLoginContext context)
    {
        if (_title is not null)
        {
            return;
        }

        var manager = context.Manager;
        if (!manager.Services.Descs.TryGetLayout(LayoutId, out var layout) || layout.AllElements().FirstOrDefault(static e => e.ElementId == BuyStatusTextId) is not { } desc
            || manager.CreateChildElement(this, layout, desc) is not UiTextElement title)
        {
            manager.Services.Log.Error(UiElementManager.Category, $"gmSlumlordUI: the title text could not be made from 0x{BuyStatusTextId:X8} of layout 0x{LayoutId:X8}; the window has no title (TODO(villa-slumlord-title))");
            return;
        }

        _title = title;
        _title.MoveTo(194, 2);
        _title.ResizeTo(376, 16);
        if (_titleText.Length > 0)
        {
            _title.SetText(_titleText);
        }
    }

    private void RegisterFrames()
    {
        if (!_frameRegistered)
        {
            _frameRegistered = true;
            Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        }
    }

    private void UnregisterFrames()
    {
        if (_frameRegistered)
        {
            _frameRegistered = false;
            Manager.UnregisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        }
    }

    /// <summary>The frame: a finished press shows its outcome, and the window redraws now and then (the placed stacks follow the packs).</summary>
    protected internal override void ListenToGlobalMessage(uint messageId, int data)
    {
        base.ListenToGlobalMessage(messageId, data);
        if (messageId != UiGlobalMessageIds.Frame || !Visible)
        {
            return;
        }

        if (_pending is { IsCompleted: true } done)
        {
            _pending = null;
            var detail = done.Outcome?.Detail;
            _outcomeLine = done.Outcome is { Kind: ActionOutcomeKind.Confirmed } ? string.Empty : string.IsNullOrWhiteSpace(detail) ? "The guardian refused: " + done.Outcome?.Kind : detail;
            _context?.Manager.Services.Log.Info("retailui", "guardian window: " + done.Kind + " ended " + done.Outcome?.Kind + (_outcomeLine.Length > 0 ? ": " + _outcomeLine : string.Empty));
            Redraw();
        }
        else if (++_framesSinceRefresh >= RefreshFrames)
        {
            Redraw();
        }
    }

    /// <summary>The buttons; everything else goes to the tab panel (a click on a tab opens its page).</summary>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.ButtonClick)
        {
            switch (message.ElementId)
            {
                case CloseButtonId:
                    _context?.Manager.Services.Log.Info("retailui", "guardian window: closed by the player (the model's guardian closes with it)");
                    _context?.Session.CloseGuardianWindow();
                    Close();
                    return UiListenResult.Handled;
                case BuyButtonId:
                    PressBuy();
                    return UiListenResult.Handled;
                case PayButtonId:
                    PressPay();
                    return UiListenResult.Handled;
            }
        }

        return base.ListenToElementMessage(message);
    }

    private void OnPlacedActivated(UiItemList list, UiItem cell)
    {
        if (cell.ItemId != 0)
        {
            RemoveItem(cell.ItemId);
        }
    }

    private void OnOpened(in GuardianOpened opened) => Open();

    private void OnProfileChanged(in GuardianProfileChanged changed)
    {
        if (Visible && _shownGuardian == changed.Guardian)
        {
            Redraw();
        }
        else
        {
            Open(); // a fresh 0x021D for a window the close button hid: the player used the guardian again
        }
    }

    private void OnClosed(in GuardianClosed closed)
    {
        if (_shownGuardian == closed.Guardian || (View is { IsOpen: false } && Visible))
        {
            Close();
        }
    }

    /// <inheritdoc/>
    protected internal override void OnDeleting()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions = [];
        UnregisterFrames();
        base.OnDeleting();
    }

    /// <summary>The window as a drop target: an item the player holds, dragged from a pack onto the window, is placed in the open pane; the model accepts or refuses it and says why in its error line.</summary>
    private sealed class GameSlumlordDrop(GameSlumlordPanel panel) : IUiDropTarget
    {
        /// <inheritdoc/>
        public bool HighlightsCell => false;

        /// <inheritdoc/>
        public bool Accepts(in UiDragPayload drag, UiItem? cell) =>
            panel.Visible && panel.View is { HasModel: true } && drag.SpellId == 0 && !ReferenceEquals(drag.SourceList, panel._buyList) && !ReferenceEquals(drag.SourceList, panel._rentList)
            && panel._context!.Session.State.Self.Inventory.Contains(drag.ItemId);

        /// <inheritdoc/>
        public string? Refusal(in UiDragPayload drag, UiItem? cell) => panel.View is not { HasModel: true } ? "the guardian window has no model" : "only an item from your packs can be placed";

        /// <inheritdoc/>
        public void Drop(in UiDragPayload drag, UiItem? cell) => panel.PlaceItem(drag.ItemId);
    }
}
