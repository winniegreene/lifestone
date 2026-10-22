using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>Why an item may not go into the vendor's sell list (<see cref="GameVendorPanel.SellVerdictFor"/>); the codes 1 to 4 are <c>VendorProfile::InqAcceptability</c>'s (11.6096 0x005D2A90).</summary>
public enum VendorSellVerdict
{
    /// <summary>The item may go into the sell list.</summary>
    Ok = 0,

    /// <summary>Acceptability 1: the item's type is not in the vendor's mask (or its bitfield bit 24 is set): "That item cannot be sold here".</summary>
    WrongType = 1,

    /// <summary>Acceptability 2: the item has no value: "That item has no value and cannot be sold".</summary>
    NoValue = 2,

    /// <summary>Acceptability 3: the value of one unit is below the vendor's minimum: "That item is too cheap to sell here".</summary>
    TooCheap = 3,

    /// <summary>Acceptability 4: the value of one unit is above the vendor's maximum (a promissory note is exempt): "That item is too valuable to sell here".</summary>
    TooValuable = 4,

    /// <summary>The vendor does not deal in magical items and the item is one: "You cannot sell that here" (retail's default text; <c>TODO(m4-vendor-magic-rule)</c>: retail's own test has no address read).</summary>
    Magical = 5,

    /// <summary>The player does not carry the item: "You can only sell items you are carrying" (<c>VendorSellUI::DragItemAcceptable</c> 11.6096 0x004C2CB0).</summary>
    NotCarried = 6,

    /// <summary>The item is in the sell list already (a Lifestone literal: retail lists it again).</summary>
    AlreadyListed = 7,

    /// <summary>No vendor is open.</summary>
    NoVendor = 8,

    /// <summary>The vendor trades in an alternate currency (<c>TODO(m4-vendor-alt-currency)</c>).</summary>
    AlternateCurrency = 9,
}

/// <summary>One line of the buy list (the cart): a row of the vendor's stock and how many of it.</summary>
/// <param name="Guid">The stock row's guid (<see cref="VendorStockEntry.Guid"/>).</param>
/// <param name="Amount">How many are bought.</param>
public readonly record struct VendorCartLine(uint Guid, uint Amount);

/// <summary>
/// The vendor window (M4 3.2 s4b): <c>gmVendorUI</c> (class 0x10000017, window 0x10000062 of the gameplay layout, panel id 0x17) in the environment floaty <c>gmFloatyEnvPanelUI</c>, over the vendor model
/// (<see cref="IGameSessionView.Vendor"/>, M4 3.2 s1) and the Action API (<see cref="IActionApi.Buy"/>, <see cref="IActionApi.Sell"/>; the panel never sends on the wire). Re-expressed from the layout and from the 11.6096 functions of
/// <c>gmVendorUI</c> (0x004C0E90 to 0x004C62F0): <c>PostInit</c> 0x004C1590, <c>OpenVendor</c> 0x004C5790, <c>RecvNotice_CloseVendor</c> 0x004C0F40, <c>OnObjectRangeExit</c> 0x004C0F70, <c>HandleButtonClicks</c> 0x004C5CC0,
/// <c>HandleMousePresses</c> 0x004C4CC0, <c>BuySingleItem</c> 0x004C3410, <c>SellSingleItem</c> 0x004C3730, <c>UpdateDragOver</c> 0x004C1030, <c>VendorSellUI::DragItemAcceptable</c> 0x004C2CB0, <c>AcceptDragObject</c> 0x004C5AF0.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout.</b> Three tabs (the tab panel 0x100000B8): the vendor's items (page 0x100000BC: list 0x100000BD, the selected item's name 0x100000C0 and cost 0x100000C1, "buy" 0x100000C2 and "add to the buy list" 0x100000C3), the buy
/// list (page 0x100000C4: list 0x100000C5, "Buying N items worth Xp" 0x100000C7, "You have Xp" 0x100000C8, buy this 0x100000C9, buy all 0x100000CA, remove 0x100000CB, clear 0x100000CC) and the sell list (page 0x100000CD: list 0x100000CE,
/// "Selling N items worth Xp" 0x100000D0, "You have Xp" 0x100000D1, sell this 0x100000D2, sell all 0x100000D3, remove 0x100000D4, clear 0x100000D5), the close button 0x100000D6. Retail's window shows no vendor name; Lifestone adds it as
/// a title text on the right of the tab bar (<c>TODO(m4-vendor-title)</c>). The cells show icons only, as retail: the selected item's name and cost are in the text lines (the cost is <c>PriceToBuy</c>, the prediction the Action API checks the
/// trade against). A shop row of unlimited stock (amount -1, the 0062's 0xFFFFFF) shows no count, as retail's cell does for a stack of one (<c>TODO(m4-vendor-unlimited-mark)</c>: retail's unlimited mark is not read).
/// </para>
/// <para>
/// <b>Follows the vendor model.</b> It opens on <see cref="VendorOpened"/> (the layout shows the window by default, so it hides itself until then, and the floaty that holds it follows), redraws on <see cref="VendorStockChanged"/>
/// and <see cref="CoinValueChanged"/>, and closes on <see cref="VendorClosed"/> (the model closes the vendor when the player leaves its range: <c>OnObjectRangeExit</c>) without sending anything. The close button hides the window (retail
/// asks first when a list is not empty: "You have not completed all transactions. Are you sure you want to leave this vendor?"); a later 0062 shows it again.
/// </para>
/// <para>
/// <b>Buy.</b> A double click on a shop cell buys one at once (<c>HandleMousePresses</c>); "buy" does the same for the selected cell; "add" (or a drag onto the window) puts one into the buy list, again for one more (the quantity is
/// the number of adds: <c>TODO(m4-vendor-quantity-overlay)</c>, retail's slider <c>UpdateQuantityOverlay</c> 0x004C18B0 is not built); "buy all" sends the whole list. The total is the sum of <see cref="VendorStock.PriceToBuy(VendorStockEntry, uint)"/>.
/// A buy that costs more than the pyreals carried is refused before anything is sent with retail's text "You don't have enough money". The list is cleared when the trade is <see cref="ActionOutcomeKind.Confirmed"/>, and kept with the
/// refusal's text in the tab's first line when it is not. <b>Sell.</b> Items dragged from the packs onto the window go into the sell list (the window opens the sell tab while an acceptable item is over it: <c>UpdateDragOver</c>), refused
/// with retail's texts (<see cref="VendorSellVerdict"/>); the payout is <see cref="VendorStock.PayoutFor(uint, uint)"/> and "sell all" sends <see cref="IActionApi.Sell"/>; the items in the list carry the sell mark
/// (<see cref="UiItem.SetSellState"/>).
/// </para>
/// <para>
/// <b>Not built (named, each answers where it can).</b> The type filter menu: the menu opens the not-built dialog (<c>TODO(m4-vendor-type-filter)</c>, <c>AddTypeFilter</c> 0x004C1240); an alternate-currency vendor: its texts say so and
/// a trade is refused (<c>TODO(m4-vendor-alt-currency)</c>); splitting a stack before selling it (<c>TODO(m4-vendor-split-stack)</c>); the pack-space test of a buy (<c>TODO(m4-vendor-pack-space)</c>: the server's refusal shows).
/// </para>
/// </remarks>
public sealed class GameVendorPanel : UiElement
{
    /// <summary>The retail class id (<c>gmVendorUI</c>; <c>gmVendorUI::Register</c> 11.6096 0x004C3BF0).</summary>
    public const uint RetailClassId = 0x10000017;

    /// <summary>The window's element id in the gameplay layout (inside <see cref="UiFloatyEnvPanel"/>).</summary>
    public const uint WindowId = 0x10000062;

    /// <summary>The close button.</summary>
    public const uint CloseButtonId = 0x100000D6;

    /// <summary>The tab panel.</summary>
    public const uint TabPanelId = 0x100000B8;

    /// <summary>The tab of the vendor's items.</summary>
    public const uint ItemsTabId = 0x100000B9;

    /// <summary>The tab of the buy list.</summary>
    public const uint BuyTabId = 0x100000BA;

    /// <summary>The tab of the sell list (<c>gmVendorUI::UpdateDragOver</c> opens it).</summary>
    public const uint SellTabId = 0x100000BB;

    /// <summary>The page of the vendor's items.</summary>
    public const uint ItemsPageId = 0x100000BC;

    /// <summary>The page of the buy list.</summary>
    public const uint BuyPageId = 0x100000C4;

    /// <summary>The page of the sell list.</summary>
    public const uint SellPageId = 0x100000CD;

    /// <summary>The vendor's items.</summary>
    public const uint ShopListId = 0x100000BD;

    /// <summary>The buy list.</summary>
    public const uint BuyListId = 0x100000C5;

    /// <summary>The sell list.</summary>
    public const uint SellListId = 0x100000CE;

    /// <summary>The type filter menu of the items page.</summary>
    public const uint TypeMenuId = 0x100000BF;

    /// <summary>The selected item's name (items page).</summary>
    public const uint ItemNameTextId = 0x100000C0;

    /// <summary>The selected item's cost (items page).</summary>
    public const uint ItemCostTextId = 0x100000C1;

    /// <summary>"buy" (items page).</summary>
    public const uint BuyNowButtonId = 0x100000C2;

    /// <summary>"add to the buy list" (items page).</summary>
    public const uint AddToBuyButtonId = 0x100000C3;

    /// <summary>"Buying N items worth Xp" (buy page).</summary>
    public const uint BuyTextId = 0x100000C7;

    /// <summary>"You have Xp" (buy page).</summary>
    public const uint BuyPurseTextId = 0x100000C8;

    /// <summary>Buy the selected line (buy page).</summary>
    public const uint BuyLineButtonId = 0x100000C9;

    /// <summary>Buy the whole list (buy page).</summary>
    public const uint BuyAllButtonId = 0x100000CA;

    /// <summary>Remove one of the selected line (buy page).</summary>
    public const uint RemoveBuyButtonId = 0x100000CB;

    /// <summary>Clear the buy list.</summary>
    public const uint ClearBuyButtonId = 0x100000CC;

    /// <summary>"Selling N items worth Xp" (sell page).</summary>
    public const uint SellTextId = 0x100000D0;

    /// <summary>"You have Xp" (sell page).</summary>
    public const uint SellPurseTextId = 0x100000D1;

    /// <summary>Sell the selected item (sell page).</summary>
    public const uint SellLineButtonId = 0x100000D2;

    /// <summary>Sell the whole list (sell page).</summary>
    public const uint SellAllButtonId = 0x100000D3;

    /// <summary>Remove the selected item from the sell list.</summary>
    public const uint RemoveSellButtonId = 0x100000D4;

    /// <summary>Clear the sell list.</summary>
    public const uint ClearSellButtonId = 0x100000D5;

    /// <summary>The layout the vendor name's title text is made from (the window's own layout; its item-name text 0x100000C0 is the model).</summary>
    public const uint TitleLayoutId = 0x21000012;

    /// <summary>The most cells a redraw makes (a shop of fifty rows finishes over the next frames).</summary>
    public const int CellsPerRedraw = 24;

    /// <summary>The most of one item a buy list holds (a Lifestone bound: <c>TODO(m4-vendor-quantity-overlay)</c>).</summary>
    public const uint MaxCartAmount = 999;

    /// <summary>The frames between two sweeps that re-mark the sell list's items in the packs (the packs redraw their cells on their own).</summary>
    public const int MarkSweepFrames = 15;

    /// <summary>Retail's text when the pyreals do not cover a buy (<c>BuySingleItem</c>, <c>HandleButtonClicks</c>).</summary>
    public const string NotEnoughMoneyText = "You don't have enough money";

    /// <summary>Retail's text when a list is not empty and the close button is pressed (<c>HandleButtonClicks</c>, the callback dialog).</summary>
    public const string LeaveText = "You have not completed all transactions. Are you sure you want to leave this vendor?";

    /// <summary>What the type filter menu answers.</summary>
    public const string TypeFilterNotBuiltText = "The vendor's type filter is not built in M4";

    private readonly List<VendorCartLine> _cart = new(8);
    private readonly List<uint> _sells = new(8);
    private readonly List<(uint Guid, UiItemIcon Icon)> _rows = new(64);
    private UiLoginContext? _context;
    private UiPanel? _tabs;
    private UiItemList? _shop;
    private UiItemList? _buyList;
    private UiItemList? _sellList;
    private UiMenu? _menu;
    private UiTextElement? _title;
    private UiTextElement? _itemName;
    private UiTextElement? _itemCost;
    private UiTextElement? _buyText;
    private UiTextElement? _buyPurse;
    private UiTextElement? _sellText;
    private UiTextElement? _sellPurse;
    private IDisposable[] _subscriptions = [];
    private uint _shownVendor;
    private bool _more;
    private bool _frameRegistered;
    private bool _dialogOpen;
    private uint _lastTab;
    private int _framesSinceSweep;
    private string _status = string.Empty;
    private uint _statusTab;
    private VendorTradeHandle? _pending;
    private Action? _onConfirmed;
    private bool _typeFilterLogged;
    private bool _quantityLogged;
    private bool _unlimitedLogged;
    private string _titleText = string.Empty;
    private string _itemNameText = string.Empty;
    private string _itemCostText = string.Empty;
    private string _buyTextShown = string.Empty;
    private string _buyPurseShown = string.Empty;
    private string _sellTextShown = string.Empty;
    private string _sellPurseShown = string.Empty;

    /// <summary>The window of <paramref name="init"/>.</summary>
    public GameVendorPanel(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>The vendor's name (the title the window shows), or empty before it drew.</summary>
    public string Title => _titleText;

    /// <summary>The vendor the window shows now (0 while it is hidden).</summary>
    public uint ShownVendor => Visible ? _shownVendor : 0;

    /// <summary>The list of the vendor's items (a test reads its cells).</summary>
    public UiItemList? ShopRow => _shop;

    /// <summary>The buy list's cells.</summary>
    public UiItemList? BuyRow => _buyList;

    /// <summary>The sell list's cells.</summary>
    public UiItemList? SellRow => _sellList;

    /// <summary>The type filter menu.</summary>
    public UiMenu? TypeMenu => _menu;

    /// <summary>The buy list, in the order the lines were added.</summary>
    public IReadOnlyList<VendorCartLine> Cart => _cart;

    /// <summary>The items of the sell list, in the order they were added.</summary>
    public IReadOnlyList<uint> SellItems => _sells;

    /// <summary>The open tab (<see cref="ItemsTabId"/>, <see cref="BuyTabId"/> or <see cref="SellTabId"/>), or 0.</summary>
    public uint OpenTabId => _tabs?.OpenTabId ?? 0;

    /// <summary>The selected item's name line of the items page.</summary>
    public string ItemNameText => _itemNameText;

    /// <summary>The selected item's cost line of the items page (or the refusal text while one shows).</summary>
    public string ItemCostText => _itemCostText;

    /// <summary>The buy page's first line, "Buying N items worth Xp" (or the refusal text while one shows).</summary>
    public string BuyText => _buyTextShown;

    /// <summary>The buy page's second line, "You have Xp".</summary>
    public string BuyPurseText => _buyPurseShown;

    /// <summary>The sell page's first line, "Selling N items worth Xp" (or the refusal text while one shows).</summary>
    public string SellText => _sellTextShown;

    /// <summary>The sell page's second line, "You have Xp".</summary>
    public string SellPurseText => _sellPurseShown;

    /// <summary>The refusal or notice the first line of the tab it belongs to shows now, or empty.</summary>
    public string Status => _status;

    /// <summary>The last buy the window sent, or null.</summary>
    public VendorTradeHandle? LastBuy { get; private set; }

    /// <summary>The last sale the window sent, or null.</summary>
    public VendorTradeHandle? LastSell { get; private set; }

    /// <summary>How many buys the window sent.</summary>
    public int Buys { get; private set; }

    /// <summary>How many sales the window sent.</summary>
    public int Sells { get; private set; }

    /// <summary>The sum of the buy list's lines at the predicted prices (<see cref="VendorStock.PriceToBuy(VendorStockEntry, uint)"/>).</summary>
    public long CartTotal
    {
        get
        {
            var stock = _context?.Session.State.Vendor;
            long total = 0;
            if (stock is null)
            {
                return 0;
            }

            foreach (var line in _cart)
            {
                total += stock.Find(line.Guid) is { } entry ? stock.PriceToBuy(entry, line.Amount) : 0;
            }

            return total;
        }
    }

    /// <summary>What the sell list pays: the sum of <see cref="VendorStock.PayoutFor(WorldObject)"/> over its items.</summary>
    public long SellTotal
    {
        get
        {
            var context = _context;
            var stock = context?.Session.State.Vendor;
            long total = 0;
            if (stock is null)
            {
                return 0;
            }

            foreach (var guid in _sells)
            {
                total += context!.Session.State.Objects.TryGet(guid, out var item) ? stock.PayoutFor(item) : 0;
            }

            return total;
        }
    }

    /// <summary>True while the window is drawn for the stock the model holds and no redraw has cells left to make.</summary>
    public bool Drawn => Visible && !_more && _context?.Session.State.Vendor is { } stock && stock.Vendor == _shownVendor;

    /// <summary>The window and its lists, texts and buttons are found by id; the layout shows the window by default, so it hides itself (the floaty that holds it follows). Subscribes to nothing until <see cref="Attach"/>.</summary>
    protected internal override void PostInit()
    {
        base.PostInit();
        _tabs = GetChildRecursive(TabPanelId) as UiPanel;
        _shop = GetChildRecursive(ShopListId) as UiItemList;
        _buyList = GetChildRecursive(BuyListId) as UiItemList;
        _sellList = GetChildRecursive(SellListId) as UiItemList;
        _menu = GetChildRecursive(TypeMenuId) as UiMenu;
        _itemName = GetChildRecursive(ItemNameTextId) as UiTextElement;
        _itemCost = GetChildRecursive(ItemCostTextId) as UiTextElement;
        _buyText = GetChildRecursive(BuyTextId) as UiTextElement;
        _buyPurse = GetChildRecursive(BuyPurseTextId) as UiTextElement;
        _sellText = GetChildRecursive(SellTextId) as UiTextElement;
        _sellPurse = GetChildRecursive(SellPurseTextId) as UiTextElement;
        SetVisible(false);
        (Parent as UiFloatyEnvPanel)?.UpdateVisibility();
        if (_shop is not null)
        {
            _shop.ItemSelected += OnShopSelected;
            _shop.ItemActivated += OnShopActivated;
            _shop.DragSource = true; // a shop cell is dragged onto the window to put it in the buy list
        }

        if (_buyList is not null)
        {
            _buyList.ItemSelected += OnLineSelected;
            _buyList.ItemActivated += OnBuyLineActivated;
        }

        if (_sellList is not null)
        {
            _sellList.ItemSelected += OnLineSelected;
            _sellList.ItemActivated += OnSellLineActivated;
        }
    }

    /// <summary>Hands the window the session: it follows the vendor events, makes its title and opens at once when the session holds an open vendor now.</summary>
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
            events.Subscribe<VendorOpened>(OnOpened),
            events.Subscribe<VendorStockChanged>(OnStockChanged),
            events.Subscribe<VendorClosed>(OnClosed),
            events.Subscribe<CoinValueChanged>(OnCoinsChanged),
        ];
        if (_shop is null || _buyList is null || _sellList is null || _tabs is null || GetChildRecursive(CloseButtonId) is not UiButton)
        {
            context.Manager.Services.Log.Error(UiElementManager.Category, "gmVendorUI: the window lacks its tab panel, one of its three lists or its close button; the vendor window cannot trade (TODO(m4-vendor-window-missing))");
        }

        MakeTitle(context);
        if (_menu is not null && _menu.ItemCount == 0)
        {
            _menu.InsertTextItem(UiStringRef.FromLiteral("All"), 0); // the type filter: "All" only (VendorItemsUI::OpenVendor adds a row per type the shop holds: not built)
        }

        _menu?.RegisterForElementMessages(AsListener);
        var drag = context.Manager.Input.Drag;
        drag.RegisterTarget(this, new GameVendorDrop(this));
        context.Manager.Services.Log.Warn("retailui", "gmVendorUI: TODO(m4-vendor-title) retail's window names no vendor, the title is Lifestone's; TODO(m4-vendor-type-filter) the menu holds 'All' and opens the not-built dialog; TODO(m4-vendor-alt-currency) TODO(m4-vendor-quantity-overlay) TODO(m4-vendor-split-stack) TODO(m4-vendor-pack-space) TODO(m4-vendor-unlimited-mark) TODO(m4-vendor-magic-rule)");
        if (context.Session.State.Vendor is not null)
        {
            Open();
        }
    }

    /// <summary>Shows the window for the open vendor, on its items tab, and draws it (what <see cref="VendorOpened"/> does; <c>gmVendorUI::OpenVendor</c> 0x004C5790).</summary>
    public void Open()
    {
        if (_context is null || _context.Session.State.Vendor is not { } stock)
        {
            return;
        }

        if (_shownVendor != stock.Vendor)
        {
            ResetLists(); // another vendor: the lists of the last one go
        }

        _shownVendor = stock.Vendor;
        SetVisible(true);
        (Parent as UiFloatyEnvPanel)?.UpdateVisibility();
        _tabs?.OpenTab(ItemsTabId);
        _lastTab = ItemsTabId;
        RegisterFrames();
        Redraw();
        if (_shop is not null && _shop.SelectedItemId == 0 && stock.Entries.Length > 0)
        {
            _shop.SelectItem(stock.Entries[0].Guid); // UpdateItemsList(.., selectFirst): the first row, once its cell exists
            RedrawTexts();
        }
    }

    /// <summary>Hides the window and lets go of the vendor and the lists (what <see cref="VendorClosed"/> does, and the close button); nothing is sent.</summary>
    public void Close()
    {
        ResetLists();
        _shownVendor = 0;
        _more = false;
        _pending = null;
        _onConfirmed = null;
        SetVisible(false);
        (Parent as UiFloatyEnvPanel)?.UpdateVisibility();
        UnregisterFrames();
    }

    /// <summary>The close button: closes at once, or asks first when a list is not empty (<c>HandleButtonClicks</c> 0x004C5CC0, case 0x100000D6). True when it closed.</summary>
    public bool RequestClose()
    {
        if (_cart.Count == 0 && _sells.Count == 0)
        {
            CloseByPlayer();
            return true;
        }

        if (_context is null || _dialogOpen)
        {
            return false;
        }

        var data = UiDialogData.Of(UiDialogType.Confirmation, UiStringRef.FromLiteral(LeaveText))
            .WithQueue(DialogFactory.ImmediateQueue)
            .WithFlag(UiPropertyIds.DialogModal, true);
        _dialogOpen = true;
        _context.Manager.Dialogs.MakeCallbackDialog(data, answer =>
        {
            _dialogOpen = false;
            if (answer.ConfirmationResponse == true)
            {
                CloseByPlayer();
            }
        });
        return false;
    }

    // The player's own close (the button, or its confirmation): the model's open vendor closes with the window, so both agree (3.2-R23); the model's VendorClosed reaches OnClosed, which closes the window too.
    private void CloseByPlayer()
    {
        _context?.Session.CloseVendorWindow();
        Close();
    }

    /// <summary>Opens a tab by its id (<see cref="ItemsTabId"/>, <see cref="BuyTabId"/>, <see cref="SellTabId"/>); <c>gmVendorUI::OpenTab</c> 0x004C1010.</summary>
    public bool OpenTab(uint tabId)
    {
        var opened = _tabs?.OpenTab(tabId) == true;
        if (opened)
        {
            AfterTabChange();
        }

        return opened;
    }

    /// <summary>
    /// The prediction of a sale: why the item may not go into the sell list, or <see cref="VendorSellVerdict.Ok"/>. The player must carry it, the vendor's mask must take its type and the value of one unit must lie within the vendor's
    /// bounds (<c>VendorProfile::InqAcceptability</c> 11.6096 0x005D2A90, <c>VendorSellUI::DragItemAcceptable</c>).
    /// </summary>
    public VendorSellVerdict SellVerdictFor(uint itemGuid)
    {
        if (_context is null || _context.Session.State.Vendor is not { } stock)
        {
            return VendorSellVerdict.NoVendor;
        }

        if (stock.AlternateCurrencyWcid != 0)
        {
            return VendorSellVerdict.AlternateCurrency;
        }

        var state = _context.Session.State;
        if (!state.Objects.TryGet(itemGuid, out var item) || !state.Self.Inventory.Find(itemGuid).Held)
        {
            return VendorSellVerdict.NotCarried;
        }

        if (_sells.Contains(itemGuid))
        {
            return VendorSellVerdict.AlreadyListed;
        }

        if (!stock.BuysType(item.ItemType))
        {
            return VendorSellVerdict.WrongType;
        }

        var unit = item.StackValue() / item.StackSizeOr1();
        if (unit == 0)
        {
            return VendorSellVerdict.NoValue;
        }

        if (stock.MaxValue != uint.MaxValue && unit > stock.MaxValue && (item.ItemType & VendorStock.PromissoryNoteType) == 0)
        {
            return VendorSellVerdict.TooValuable;
        }

        if (stock.MinValue != uint.MaxValue && unit < stock.MinValue)
        {
            return VendorSellVerdict.TooCheap;
        }

        if (!stock.DealMagicalItems && item.IsMagical())
        {
            return VendorSellVerdict.Magical;
        }

        return VendorSellVerdict.Ok;
    }

    /// <summary>Retail's text for a refusal (<c>DragItemAcceptable</c>); empty for <see cref="VendorSellVerdict.Ok"/>.</summary>
    public static string SellRefusalText(VendorSellVerdict verdict) => verdict switch
    {
        VendorSellVerdict.Ok => string.Empty,
        VendorSellVerdict.WrongType => "That item cannot be sold here",
        VendorSellVerdict.NoValue => "That item has no value and cannot be sold",
        VendorSellVerdict.TooCheap => "That item is too cheap to sell here",
        VendorSellVerdict.TooValuable => "That item is too valuable to sell here",
        VendorSellVerdict.NotCarried => "You can only sell items you are carrying",
        VendorSellVerdict.AlreadyListed => "That item is already in the sell list",
        VendorSellVerdict.NoVendor => "No vendor is open",
        VendorSellVerdict.AlternateCurrency => AlternateCurrencyText,
        _ => "You cannot sell that here",
    };

    /// <summary>What the window says of an alternate-currency vendor (<c>TODO(m4-vendor-alt-currency)</c>).</summary>
    public const string AlternateCurrencyText = "This vendor trades in another currency, which is not built in M4";

    /// <summary>Puts one more of the stock row <paramref name="guid"/> into the buy list (the "add" button, a drag onto the window). False, with the reason in <see cref="Status"/>, when the row is not for sale (out of stock, not in the stock).</summary>
    public bool AddToCart(uint guid)
    {
        if (_context?.Session.State.Vendor is not { } stock)
        {
            return false;
        }

        if (stock.AlternateCurrencyWcid != 0)
        {
            SetStatus(AlternateCurrencyText, BuyTabId);
            return false;
        }

        if (stock.Find(guid) is not { } entry)
        {
            return false;
        }

        var have = 0u;
        var at = -1;
        for (var i = 0; i < _cart.Count; i++)
        {
            if (_cart[i].Guid == guid)
            {
                have = _cart[i].Amount;
                at = i;
            }
        }

        var amount = have + 1;
        if (!entry.IsBuyable(amount) || amount > MaxCartAmount)
        {
            SetStatus(entry.Name + " is out of stock", BuyTabId); // a Lifestone literal: retail's text for it was not read (TODO(m4-vendor-out-of-stock-text))
            _context.Manager.LogOnce("vendor:oos", LogSeverity.Warning, "TODO(m4-vendor-out-of-stock-text): the text for a row the vendor cannot supply (a listed stack of 0, or more than it holds) is a Lifestone literal");
            return false;
        }

        if (at >= 0)
        {
            _cart[at] = new VendorCartLine(guid, amount);
        }
        else
        {
            _cart.Add(new VendorCartLine(guid, amount));
        }

        if (entry.MaxStackSize > 1)
        {
            LogQuantityOnce();
        }

        ClearStatus();
        RedrawBuy();
        return true;
    }

    /// <summary>Takes one of the line <paramref name="guid"/> out of the buy list (the remove button); the line goes at zero.</summary>
    public void RemoveFromCart(uint guid)
    {
        for (var i = 0; i < _cart.Count; i++)
        {
            if (_cart[i].Guid != guid)
            {
                continue;
            }

            if (_cart[i].Amount > 1)
            {
                _cart[i] = new VendorCartLine(guid, _cart[i].Amount - 1);
            }
            else
            {
                _cart.RemoveAt(i);
            }

            break;
        }

        ClearStatus();
        RedrawBuy();
    }

    /// <summary>Empties the buy list (the clear button).</summary>
    public void ClearCart()
    {
        _cart.Clear();
        ClearStatus();
        RedrawBuy();
    }

    /// <summary>Buys one of the stock row <paramref name="guid"/> at once (a double click on its cell, the "buy" button; <c>BuySingleItem</c> 0x004C3410): <see cref="IActionApi.Buy"/>, or the refusal text.</summary>
    public VendorTradeHandle? BuyNow(uint guid)
    {
        if (_context?.Session.State.Vendor is not { } stock || stock.Find(guid) is not { } entry)
        {
            return null;
        }

        if (!CheckCanTrade(stock, ItemsTabId) || !entry.IsBuyable(1))
        {
            if (!entry.IsBuyable(1))
            {
                SetStatus(entry.Name + " is out of stock", ItemsTabId);
            }

            return null;
        }

        var price = stock.PriceToBuy(entry, 1);
        if (price > Coins())
        {
            SetStatus(NotEnoughMoneyText, ItemsTabId);
            return null;
        }

        ClearStatus();
        return SendBuy(stock, [(guid, 1u)], null);
    }

    /// <summary>Buys one of each unit of the buy list's line <paramref name="guid"/> (the "buy" button of the buy page); the line goes when the trade is confirmed.</summary>
    public VendorTradeHandle? BuyLine(uint guid)
    {
        if (_context?.Session.State.Vendor is not { } stock || !CheckCanTrade(stock, BuyTabId))
        {
            return null;
        }

        foreach (var line in _cart)
        {
            if (line.Guid != guid)
            {
                continue;
            }

            var price = stock.Find(guid) is { } entry ? stock.PriceToBuy(entry, line.Amount) : 0;
            if (price > Coins())
            {
                SetStatus(NotEnoughMoneyText, BuyTabId);
                return null;
            }

            ClearStatus();
            var sent = line;
            return SendBuy(stock, [(sent.Guid, sent.Amount)], () =>
            {
                for (var i = 0; i < _cart.Count; i++)
                {
                    if (_cart[i].Guid == sent.Guid)
                    {
                        _cart.RemoveAt(i);
                        break;
                    }
                }

                RedrawBuy();
            });
        }

        return null;
    }

    /// <summary>Buys the whole buy list (the "buy all" button; <c>HandleButtonClicks</c> case 0x100000CA): one <see cref="IActionApi.Buy"/>; the list is cleared when it is confirmed and kept, with the refusal's text, when it is not.</summary>
    public VendorTradeHandle? BuyCart()
    {
        if (_context?.Session.State.Vendor is not { } stock || _cart.Count == 0 || !CheckCanTrade(stock, BuyTabId))
        {
            return null;
        }

        if (CartTotal > Coins())
        {
            SetStatus(NotEnoughMoneyText, BuyTabId);
            return null;
        }

        ClearStatus();
        var items = new (uint item, uint amount)[_cart.Count];
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = (_cart[i].Guid, _cart[i].Amount);
        }

        return SendBuy(stock, items, () =>
        {
            _cart.Clear();
            RedrawBuy();
        });
    }

    /// <summary>Puts the carried item <paramref name="guid"/> into the sell list (a drag from a pack; <c>VendorSellUI::AcceptDragObject</c> 0x004C5AF0 then <c>AddItemToSell</c>): the sell tab opens and the item carries the sell mark. The refusal's text is in <see cref="Status"/>.</summary>
    public VendorSellVerdict AddToSell(uint guid)
    {
        var verdict = SellVerdictFor(guid);
        if (verdict != VendorSellVerdict.Ok)
        {
            SetStatus(SellRefusalText(verdict), SellTabId);
            return verdict;
        }

        _sells.Add(guid);
        ClearStatus();
        OpenTab(SellTabId);
        RedrawSell();
        SyncMarks(); // after the list's cell shows the item: the cells of the packs and of the list all carry the mark
        _sellList?.SelectItem(guid);
        return verdict;
    }

    /// <summary>Takes the item out of the sell list (the remove button, a double click on its cell).</summary>
    public void RemoveFromSell(uint guid)
    {
        if (_sells.Remove(guid))
        {
            SyncMarks();
        }

        ClearStatus();
        RedrawSell();
    }

    /// <summary>Empties the sell list (the clear button).</summary>
    public void ClearSell()
    {
        _sells.Clear();
        SyncMarks();
        ClearStatus();
        RedrawSell();
    }

    /// <summary>Sells the one item <paramref name="guid"/> of the sell list (the "sell" button of the sell page); it goes from the list when the trade is confirmed.</summary>
    public VendorTradeHandle? SellLine(uint guid)
    {
        if (_context?.Session.State.Vendor is not { } stock || !_sells.Contains(guid) || !CheckCanTrade(stock, SellTabId))
        {
            return null;
        }

        ClearStatus();
        return SendSell(stock, [guid], () => RemoveSold([guid]));
    }

    // A confirmed sale removes from the sell list the items that left the pack; one the server skipped (its 02EB "... is unsellable.": a sale is Confirmed all the same) stays, named in the line (3.2-R21).
    private void RemoveSold(IReadOnlyList<uint> sent)
    {
        var inventory = _context?.Session.State.Self.Inventory;
        List<string>? kept = null;
        foreach (var guid in sent)
        {
            if (inventory is not null && inventory.Contains(guid))
            {
                (kept ??= []).Add(_context!.Session.State.Objects.TryGet(guid, out var obj) ? obj.Name : "0x" + guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
                continue;
            }

            RemoveFromSell(guid);
        }

        if (kept is not null)
        {
            SetStatus("The vendor did not take: " + string.Join(", ", kept), SellTabId);
        }
    }

    /// <summary>Sells the whole sell list (the "sell all" button): one <see cref="IActionApi.Sell"/>; the items that left the pack go from the list when it is confirmed (a skipped one stays, named), the whole list is kept, with the refusal's text, when it is not.</summary>
    public VendorTradeHandle? SellAll()
    {
        if (_context?.Session.State.Vendor is not { } stock || _sells.Count == 0 || !CheckCanTrade(stock, SellTabId))
        {
            return null;
        }

        ClearStatus();
        var sent = _sells.ToArray();
        return SendSell(stock, sent, () => RemoveSold(sent));
    }

    /// <summary>The stack that would be bought for <paramref name="guid"/> in the buy list (0 when it is not in it).</summary>
    public uint CartAmount(uint guid)
    {
        foreach (var line in _cart)
        {
            if (line.Guid == guid)
            {
                return line.Amount;
            }
        }

        return 0;
    }

    private bool CheckCanTrade(VendorStock stock, uint tab)
    {
        if (stock.AlternateCurrencyWcid != 0)
        {
            SetStatus(AlternateCurrencyText, tab);
            return false;
        }

        if (_pending is { IsCompleted: false })
        {
            SetStatus("A transaction is already in progress", tab);
            return false;
        }

        return true;
    }

    private VendorTradeHandle SendBuy(VendorStock stock, IReadOnlyList<(uint item, uint amount)> items, Action? onConfirmed)
    {
        Buys++;
        _pending = LastBuy = _context!.Session.Actions.Buy(stock.Vendor, items, ActionSource.Ui);
        _onConfirmed = onConfirmed;
        _context.Manager.Services.Log.Info("retailui", $"vendor window: buy sent for {items.Count} line(s): {LastBuy.Summary}");
        return LastBuy;
    }

    private VendorTradeHandle SendSell(VendorStock stock, IReadOnlyList<uint> items, Action? onConfirmed)
    {
        Sells++;
        _pending = LastSell = _context!.Session.Actions.Sell(stock.Vendor, items, ActionSource.Ui);
        _onConfirmed = onConfirmed;
        _context.Manager.Services.Log.Info("retailui", $"vendor window: sell sent for {items.Count} item(s): {LastSell.Summary}");
        return LastSell;
    }

    private int Coins() => _context?.Session.State.Self.Qualities.CoinValue ?? 0;

    /// <summary>A seam for the tests: the offline rig of the panel tests has no stand-in world, so the Action API refuses a trade at once; a test swaps the handle the window waits on for one it completed itself (a confirmed trade), before the next frame reads it.</summary>
    internal void ReplacePendingTrade(VendorTradeHandle handle) => _pending = handle;

    /// <summary>The trade the window sent has an outcome: a confirmed one clears what it carried, a refused one leaves the lists and shows its text.</summary>
    private void FinishPending()
    {
        var handle = _pending;
        if (handle is not { IsCompleted: true })
        {
            return;
        }

        _pending = null;
        var done = _onConfirmed;
        _onConfirmed = null;
        var tab = handle.Kind == ActionKind.Sell ? SellTabId : OpenTabId == BuyTabId ? BuyTabId : ItemsTabId;
        if (handle.Outcome is { Kind: ActionOutcomeKind.Confirmed })
        {
            ClearStatus();
            done?.Invoke(); // may set a status of its own (an item the server skipped)
            RedrawTexts();
            return;
        }

        var detail = handle.Outcome?.Detail;
        SetStatus(string.IsNullOrWhiteSpace(detail) ? "The vendor refused: " + handle.Outcome?.Kind : detail, tab);
    }

    private void SetStatus(string text, uint tab)
    {
        _status = text;
        _statusTab = tab;
        _context?.Manager.Services.Log.Info("retailui", "vendor window: " + text);
        RedrawTexts();
    }

    private void ClearStatus()
    {
        if (_status.Length == 0)
        {
            return;
        }

        _status = string.Empty;
        _statusTab = 0;
    }

    private void LogQuantityOnce()
    {
        if (_quantityLogged || _context is null)
        {
            return;
        }

        _quantityLogged = true;
        _context.Manager.Services.Log.Warn("retailui", "placeholder: the vendor window adds a stackable item to the buy list one at a time; retail's quantity slider (VendorItemsUI::UpdateQuantityOverlay 11.6096 0x004C18B0) is not built (TODO(m4-vendor-quantity-overlay))");
    }

    /// <summary>Redraws all three pages from the model.</summary>
    public void Redraw()
    {
        if (_context is null || !Visible)
        {
            return;
        }

        _more = false;
        CullLists();
        var made = 0;
        FillShop(ref made);
        FillBuy(ref made);
        FillSell(ref made);
        PadVisibleLists();
        RedrawTexts();
        SetTitle(NameOf(_shownVendor));
        if (_more)
        {
            RegisterFrames();
        }
    }

    private void RedrawBuy()
    {
        var made = 0;
        FillBuy(ref made);
        _buyList?.UpdateEmptySlots();
        RedrawTexts();
    }

    private void RedrawSell()
    {
        var made = 0;
        FillSell(ref made);
        _sellList?.UpdateEmptySlots();
        RedrawTexts();
    }

    /// <summary>Drops what the model no longer backs: buy-list lines whose row left the stock or can no longer be bought, sell-list items that left the packs.</summary>
    private void CullLists()
    {
        if (_context?.Session.State.Vendor is not { } stock)
        {
            return;
        }

        for (var i = _cart.Count - 1; i >= 0; i--)
        {
            if (stock.Find(_cart[i].Guid) is not { } entry || !entry.IsBuyable(_cart[i].Amount))
            {
                _cart.RemoveAt(i);
            }
        }

        var changed = false;
        var state = _context.Session.State;
        for (var i = _sells.Count - 1; i >= 0; i--)
        {
            if (!state.Objects.TryGet(_sells[i], out _) || !state.Self.Inventory.Find(_sells[i]).Held)
            {
                _sells.RemoveAt(i);
                changed = true;
            }
        }

        if (changed)
        {
            SyncMarks();
        }
    }

    private void FillShop(ref int made)
    {
        if (_shop is null || _context?.Session.State.Vendor is not { } stock)
        {
            return;
        }

        _rows.Clear();
        foreach (var entry in stock.Entries)
        {
            _rows.Add((entry.Guid, IconOf(entry, entry.IsUnlimited ? 1 : (int)entry.StackSize)));
            if (entry.IsUnlimited)
            {
                LogUnlimitedOnce();
            }
        }

        FillList(_shop, ref made);
    }

    private void FillBuy(ref int made)
    {
        if (_buyList is null || _context?.Session.State.Vendor is not { } stock)
        {
            return;
        }

        _rows.Clear();
        foreach (var line in _cart)
        {
            if (stock.Find(line.Guid) is { } entry)
            {
                _rows.Add((line.Guid, IconOf(entry, (int)line.Amount)));
            }
        }

        FillList(_buyList, ref made);
    }

    private void FillSell(ref int made)
    {
        if (_sellList is null || _context is null)
        {
            return;
        }

        _rows.Clear();
        var objects = _context.Session.State.Objects;
        foreach (var guid in _sells)
        {
            if (objects.TryGet(guid, out var item))
            {
                _rows.Add((guid, GameInventoryPage.IconOf(item)));
            }
        }

        FillList(_sellList, ref made);
    }

    /// <summary>Makes <paramref name="list"/> show <see cref="_rows"/> in order, changing only what differs. At most <see cref="CellsPerRedraw"/> new cells are made in all; the rest follow on the next frames. The cells past the last row are emptied (they are the list's empty slot boxes).</summary>
    private void FillList(UiItemList list, ref int made)
    {
        var shown = 0;
        for (; shown < _rows.Count; shown++)
        {
            if (shown >= list.CellCount)
            {
                if (made >= CellsPerRedraw)
                {
                    _more = true;
                    break;
                }

                if (!list.EnsureCells(shown + 1))
                {
                    break; // no cell template: the list logs it once
                }

                made++;
            }

            if (list.CellAt(shown) is not { } cell)
            {
                break;
            }

            var (guid, icon) = _rows[shown];
            cell.ShowItem(guid, icon);
        }

        for (var i = list.CellCount - 1; i >= shown; i--)
        {
            if (list.CellAt(i) is { IsEmpty: false } leftover)
            {
                leftover.Clear();
            }
        }

        list.UpdateEmptySlots();
        list.SelectItem(list.SelectedItemId); // the selection goes with a row that left
    }

    private void PadVisibleLists()
    {
        _shop?.UpdateEmptySlots();
        _buyList?.UpdateEmptySlots();
        _sellList?.UpdateEmptySlots();
    }

    private void AfterTabChange()
    {
        _lastTab = OpenTabId;
        PadVisibleLists();
        RedrawTexts();
    }

    private static UiItemIcon IconOf(VendorStockEntry entry, int stack)
    {
        var f = entry.ItemFields();
        return new UiItemIcon(f.IconId, f.UnderlayId, f.OverlayId, entry.ItemType, f.UiEffects, stack, entry.Name, f.PluralName);
    }

    private void LogUnlimitedOnce()
    {
        if (_unlimitedLogged || _context is null)
        {
            return;
        }

        _unlimitedLogged = true;
        _context.Manager.Services.Log.Warn("retailui", "placeholder: a shop row of unlimited stock (amount -1) shows no count, as a single item does; retail's mark for it was not read (TODO(m4-vendor-unlimited-mark))");
    }

    private string NameOf(uint vendor) => _context is not null && _context.Session.State.Objects.TryGet(vendor, out var v) && v.Name.Length > 0 ? v.Name : "Vendor";

    private static string Coins(long n) => n.ToString("N0", CultureInfo.InvariantCulture) + "p";

    private static string Plural(long n) => n == 1 ? "item" : "items";

    /// <summary>The texts of the three pages (<c>UpdateItemsUI</c> 0x004C44D0, <c>VendorBuyUI::UpdateTransactionValue</c> 0x004C3D40 and <c>UpdateTotalValue</c> 0x004C3FC0, the sell page's two): the selected item's name and cost, the buy and sell totals and the pyreals carried.</summary>
    private void RedrawTexts()
    {
        if (_context?.Session.State.Vendor is not { } stock)
        {
            return;
        }

        var coins = Coins();
        var alternate = stock.AlternateCurrencyWcid != 0;
        var name = string.Empty;
        var cost = string.Empty;
        if (_shop is { SelectedItemId: not 0 } && stock.Find(_shop.SelectedItemId) is { } entry)
        {
            name = entry.Name;
            cost = alternate ? AlternateCurrencyText : $"costs {Coins(stock.PriceToBuy(entry, 1))} (you have {Coins(coins)})";
        }

        SetText(_itemName, ref _itemNameText, name);
        SetText(_itemCost, ref _itemCostText, _statusTab == ItemsTabId && _status.Length > 0 ? _status : cost);
        long units = 0;
        foreach (var line in _cart)
        {
            units += line.Amount;
        }

        var buying = alternate ? AlternateCurrencyText : $"Buying {units.ToString(CultureInfo.InvariantCulture)} {Plural(units)} worth {Coins(CartTotal)}";
        SetText(_buyText, ref _buyTextShown, _statusTab == BuyTabId && _status.Length > 0 ? _status : buying);
        SetText(_buyPurse, ref _buyPurseShown, $"You have {Coins(coins)}");
        var selling = alternate ? AlternateCurrencyText : $"Selling {_sells.Count.ToString(CultureInfo.InvariantCulture)} {Plural(_sells.Count)} worth {Coins(SellTotal)}";
        SetText(_sellText, ref _sellTextShown, _statusTab == SellTabId && _status.Length > 0 ? _status : selling);
        SetText(_sellPurse, ref _sellPurseShown, $"You have {Coins(coins)}");
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

    private void SetTitle(string text)
    {
        if (text != _titleText)
        {
            _titleText = text;
            _title?.SetText(text);
        }
    }

    /// <summary>Sets the sell mark of every cell in the UI whose item is in the sell list, and clears it from the others (retail's <c>SetSellState</c> re-draws every cell of the object).</summary>
    public void SyncMarks()
    {
        var root = this as UiElement;
        while (root.Parent is { } up)
        {
            root = up;
        }

        MarkTree(root);
        _framesSinceSweep = 0;
    }

    private void MarkTree(UiElement element)
    {
        if (element is UiItem cell)
        {
            cell.SetSellState(cell.ItemId != 0 && _sells.Contains(cell.ItemId));
            return;
        }

        var children = element.Children;
        for (var i = 0; i < children.Count; i++)
        {
            MarkTree(children[i]);
        }
    }

    private void ResetLists()
    {
        _cart.Clear();
        var hadSells = _sells.Count > 0;
        _sells.Clear();
        _status = string.Empty;
        _statusTab = 0;
        _shop?.FlushItems();
        _buyList?.FlushItems();
        _sellList?.FlushItems();
        if (hadSells)
        {
            SyncMarks();
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

    /// <summary>Makes the vendor's name text from the layout's item-name text (the same font and colour), on the right of the tab bar.</summary>
    private void MakeTitle(UiLoginContext context)
    {
        if (_title is not null)
        {
            return;
        }

        var manager = context.Manager;
        if (!manager.Services.Descs.TryGetLayout(TitleLayoutId, out var layout) || layout.AllElements().FirstOrDefault(static e => e.ElementId == ItemNameTextId) is not { } desc)
        {
            manager.Services.Log.Error(UiElementManager.Category, $"gmVendorUI: layout 0x{TitleLayoutId:X8} or its text 0x{ItemNameTextId:X8} is missing; the window has no vendor name (TODO(m4-vendor-title))");
            return;
        }

        _title = manager.CreateChildElement(this, layout, desc) as UiTextElement;
        if (_title is null)
        {
            manager.Services.Log.Error(UiElementManager.Category, "gmVendorUI: the title text could not be made; the window has no vendor name (TODO(m4-vendor-title))");
            return;
        }

        _title.MoveTo(290, 3);
        _title.ResizeTo(270, 15);
        if (_titleText.Length > 0)
        {
            _title.SetText(_titleText);
        }
    }

    /// <summary>The frame: a redraw that left cells goes on, a finished trade is read, a page that was just shown is padded, and the sell marks are swept now and then.</summary>
    protected internal override void ListenToGlobalMessage(uint messageId, int data)
    {
        base.ListenToGlobalMessage(messageId, data);
        if (messageId != UiGlobalMessageIds.Frame || !Visible)
        {
            return;
        }

        if (_more)
        {
            Redraw();
        }

        FinishPending();
        if (_tabs is not null && _tabs.OpenTabId != _lastTab)
        {
            AfterTabChange();
        }

        if (_sells.Count > 0 && ++_framesSinceSweep >= MarkSweepFrames)
        {
            CullLists();
            SyncMarks();
        }
    }

    /// <summary>The buttons, and the type filter menu: its opening answers with the not-built dialog.</summary>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.MenuOpened && _menu is not null && ReferenceEquals(message.Element, _menu))
        {
            _menu.Close();
            ShowTypeFilterNotBuilt();
            return UiListenResult.Handled;
        }

        if (message.MessageId == UiMessageIds.ButtonClick)
        {
            switch (message.ElementId)
            {
                case CloseButtonId:
                    RequestClose();
                    return UiListenResult.Handled;
                case BuyNowButtonId:
                    if (_shop is { SelectedItemId: not 0 })
                    {
                        BuyNow(_shop.SelectedItemId);
                    }

                    return UiListenResult.Handled;
                case AddToBuyButtonId:
                    if (_shop is { SelectedItemId: not 0 })
                    {
                        AddToCart(_shop.SelectedItemId);
                    }

                    return UiListenResult.Handled;
                case BuyLineButtonId:
                    if (_buyList is { SelectedItemId: not 0 })
                    {
                        BuyLine(_buyList.SelectedItemId);
                    }

                    return UiListenResult.Handled;
                case BuyAllButtonId:
                    BuyCart();
                    return UiListenResult.Handled;
                case RemoveBuyButtonId:
                    if (_buyList is { SelectedItemId: not 0 })
                    {
                        RemoveFromCart(_buyList.SelectedItemId);
                    }

                    return UiListenResult.Handled;
                case ClearBuyButtonId:
                    ClearCart();
                    return UiListenResult.Handled;
                case SellLineButtonId:
                    if (_sellList is { SelectedItemId: not 0 })
                    {
                        SellLine(_sellList.SelectedItemId);
                    }

                    return UiListenResult.Handled;
                case SellAllButtonId:
                    SellAll();
                    return UiListenResult.Handled;
                case RemoveSellButtonId:
                    if (_sellList is { SelectedItemId: not 0 })
                    {
                        RemoveFromSell(_sellList.SelectedItemId);
                    }

                    return UiListenResult.Handled;
                case ClearSellButtonId:
                    ClearSell();
                    return UiListenResult.Handled;
            }
        }

        if (message.MessageId == UiMessageIds.VisibilityChanged && message.Element.Parent is { } parent && ReferenceEquals(parent, _tabs) && Visible)
        {
            // a page of the tab panel was shown: its list pads its empty slots now that it is visible
            PadVisibleLists();
            RedrawTexts();
        }

        return base.ListenToElementMessage(message);
    }

    private void ShowTypeFilterNotBuilt()
    {
        if (_context is null)
        {
            return;
        }

        if (!_typeFilterLogged)
        {
            _typeFilterLogged = true;
            _context.Manager.Services.Log.Warn("retailui", $"placeholder: '{TypeFilterNotBuiltText}' (VendorItemsUI::AddTypeFilter 11.6096 0x004C1240; TODO(m4-vendor-type-filter)); a retail message dialog says so");
        }

        var data = UiDialogData.Of(UiDialogType.Message, UiStringRef.FromLiteral(TypeFilterNotBuiltText)).WithFlag(UiPropertyIds.DialogModal, true);
        _context.Manager.Dialogs.MakeDialog(data);
    }

    private void OnShopSelected(UiItemList list, UiItem cell)
    {
        ClearStatus();
        RedrawTexts();
    }

    private void OnLineSelected(UiItemList list, UiItem cell) => RedrawTexts();

    /// <summary>A double click on a shop cell buys one at once (<c>HandleMousePresses</c> 0x004C4CC0).</summary>
    private void OnShopActivated(UiItemList list, UiItem cell)
    {
        if (cell.ItemId != 0)
        {
            BuyNow(cell.ItemId);
        }
    }

    /// <summary>A double click on a buy-list line takes it out (retail says "Removing &lt;name&gt; from shopping list" in the chat; <c>TODO(m4-vendor-chat-lines)</c>).</summary>
    private void OnBuyLineActivated(UiItemList list, UiItem cell)
    {
        if (cell.ItemId != 0)
        {
            RemoveFromCart(cell.ItemId);
        }
    }

    private void OnSellLineActivated(UiItemList list, UiItem cell)
    {
        if (cell.ItemId != 0)
        {
            RemoveFromSell(cell.ItemId);
        }
    }

    private void OnOpened(in VendorOpened opened) => Open();

    private void OnStockChanged(in VendorStockChanged changed)
    {
        if (_context?.Session.State.Vendor is null)
        {
            return;
        }

        if (Visible && _shownVendor == changed.Vendor)
        {
            Redraw();
        }
        else
        {
            Open(); // a fresh 0062 for a window the close button hid: the player used the vendor again
        }
    }

    private void OnClosed(in VendorClosed closed)
    {
        if (_shownVendor == closed.Vendor || (_context?.Session.State.Vendor is null && Visible))
        {
            Close(); // RecvNotice_CloseVendor / OnObjectRangeExit: the player left the vendor's range (or the server closed it)
        }
    }

    private void OnCoinsChanged(in CoinValueChanged changed)
    {
        if (Visible)
        {
            RedrawTexts();
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

    /// <summary>
    /// The window as a drop target (<c>HandleDropRelease</c> 0x004C5680, <c>UpdateDragOver</c> 0x004C1030): an item dragged from a pack goes into the sell list when it is acceptable, and the window shows the sell tab while one is over it; a shop
    /// cell dragged onto the window goes into the buy list. A refused release leaves retail's text in the window.
    /// </summary>
    private sealed class GameVendorDrop(GameVendorPanel panel) : IUiDropTarget
    {
        /// <inheritdoc/>
        public bool HighlightsCell => false;

        /// <inheritdoc/>
        public bool Accepts(in UiDragPayload drag, UiItem? cell)
        {
            if (ReferenceEquals(drag.SourceList, panel._shop))
            {
                return panel._context?.Session.State.Vendor?.Find(drag.ItemId) is { } entry && entry.IsBuyable(1);
            }

            var ok = panel.SellVerdictFor(drag.ItemId) == VendorSellVerdict.Ok;
            if (ok && panel.OpenTabId != SellTabId)
            {
                panel.OpenTab(SellTabId); // UpdateDragOver: an acceptable item over the window opens the sell tab
            }

            return ok;
        }

        /// <inheritdoc/>
        public string? Refusal(in UiDragPayload drag, UiItem? cell)
        {
            if (ReferenceEquals(drag.SourceList, panel._shop))
            {
                return "that row cannot be bought";
            }

            var text = SellRefusalText(panel.SellVerdictFor(drag.ItemId));
            panel.SetStatus(text, SellTabId);
            return text;
        }

        /// <inheritdoc/>
        public void Drop(in UiDragPayload drag, UiItem? cell)
        {
            if (ReferenceEquals(drag.SourceList, panel._shop))
            {
                if (panel.AddToCart(drag.ItemId))
                {
                    panel.OpenTab(BuyTabId);
                }

                return;
            }

            panel.AddToSell(drag.ItemId);
        }
    }
}
