using System.Collections.Immutable;
using Lifestone.Client.RetailUi;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Tests;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The vendor window (M4 3.2 s4b; <c>gmVendorUI</c> 0x10000017, window 0x10000062 in <c>gmFloatyEnvPanelUI</c>) over the real gameplay layout and the vendor model, fed in the order ACE sends it (cache/m4/3-2/capture.md C1, C2): it opens on
/// the 0062, lists the shop's rows with their predicted prices and pads the empty cells, buys and sells through the Action API only (<see cref="ActionSource.Ui"/>), keeps the lists on a refusal, clears them on a confirmed trade, refuses an item
/// the vendor's mask does not take with retail's text, and closes when the player leaves the vendor's range.
/// </summary>
public sealed class GameVendorPanelTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint Player = 0x50000001;
    private const uint Cell = 0x860201AD;
    private const uint Thelnoth = 0x7A9B4027;
    private const uint KitRow = 0x8000033F;
    private const uint PotionRow = 0x80000340;
    private const uint ArrowRow = 0x80000341;
    private const uint ZeroRow = 0x80000342;
    private const uint Coins = 0x80000102;
    private const uint Bracers = 0x80000103;
    private const uint Scroll = 0x80000104;
    private const uint Junk = 0x80000105;
    private const uint PackKit = 0x80000106;
    private const uint Gem = 0x80000107;
    private const uint InventoryButton = 0x100001B1;
    private const uint Mask = 0x00044080; // Thelnoth: Misc, Key, PromissoryNote
    private const uint EnvPanelId = 0x100005FD;

    private sealed class Game(LoginRig rig, GamePlayScreen screen, IInboundMessageHandler handler) : IDisposable
    {
        private uint _event = 500;
        private byte _seq;

        public LoginRig Rig => rig;

        public InboundContext Context => new(0, MessageQueue.SmartBox, 0);

        public GameVendorPanel Window => Assert.IsType<GameVendorPanel>(screen.Root!.GetChildRecursive(GameVendorPanel.WindowId));

        public UiElement Env => screen.Root!.GetChildRecursive(EnvPanelId)!;

        public IGameSessionView Session => rig.Session;

        public UiDrag Drag => rig.Manager.Input.Drag;

        public GameInventoryPage Page => Assert.Single(screen.PanelControllers!.ControllersOf(7).OfType<GameInventoryPage>());

        public void Feed(byte[] message) => Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(Context, message, handler).Outcome);

        public void Stock(uint mask = Mask, IReadOnlyList<AceVendorRow>? rows = null, uint min = 0, uint max = 1_000_000, bool magic = true, uint altWcid = 0) =>
            Feed(AceVendorWriters.ApproachVendor(Player, ++_event, Thelnoth, mask, min, max, magic, 0.9f, 1.35f, rows ?? ThelnothRows(), altWcid, altWcid == 0 ? 0u : 1u, altWcid == 0 ? string.Empty : "Trade Notes"));

        public void SetCoins(int value) => Feed(AceWireWriters.PrivateUpdatePropertyInt(++_seq, PlayerQualities.CoinValueProperty, value));

        public void WalkTo(float x, ushort sequence) => handler.OnUpdatePosition(Context, ReducerHarness.Move(Player, 1, sequence, 1, 1, Cell, x));

        public void Frames(int n)
        {
            for (var i = 0; i < n; i++)
            {
                rig.Frame();
            }
        }

        public void Open()
        {
            Assert.True(rig.Click(Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(InventoryButton))));
            Frames(14);
            Assert.Equal(7u, screen.PanelHost!.CurrentPanel);
        }

        public static (int X, int Y) Center(UiElement element)
        {
            var box = element.ScreenBox;
            return ((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
        }

        public void DragTo(UiElement source, UiElement target)
        {
            var input = rig.Manager.Input;
            var from = Center(source);
            input.MouseMove(from.X, from.Y);
            input.MouseButton(UiMouseButton.Left, down: true);
            input.MouseMove(from.X + 3, from.Y);
            input.MouseMove(from.X + 10, from.Y + 2);
            var to = Center(target);
            input.MouseMove(to.X, to.Y);
            input.MouseButton(UiMouseButton.Left, down: false);
            Frames(1);
        }

        public UiButton Button(uint id) => Assert.IsAssignableFrom<UiButton>(Window.GetChildRecursive(id));

        public GameExaminePanel Examine => Assert.IsType<GameExaminePanel>(screen.PanelHost!.Examine);

        public void RightClick(UiElement element)
        {
            var (x, y) = Center(element);
            rig.Manager.Input.MouseMove(x, y);
            rig.Manager.Input.MouseButton(UiMouseButton.Right, down: true);
            rig.Manager.Input.MouseButton(UiMouseButton.Right, down: false);
        }

        public void Dispose() => rig.Dispose();
    }

    private static AceVendorRow[] ThelnothRows() =>
    [
        VendorRig.Row(-1, KitRow, "Handy Healing Kit", 628, 0x80, 10),
        VendorRig.Row(-1, PotionRow, "Potion of Healing", 377, 0x80, 170),
        VendorRig.Row(-1, ArrowRow, "Arrow", 300, 0x100, 5, stackSize: 1, maxStack: 25),
        VendorRig.Row(0, ZeroRow, "Out of Stock", 999, 0x80, 7),
    ];

    private static CreateObjectMsg Item(uint guid, string name, uint itemType, uint value, ushort? stack = null, uint? container = Player, uint? uiEffects = null)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
        return basis with { Weenie = basis.Weenie with { IconId = 0x060011D0, ItemType = itemType, ContainerId = container, Value = value, StackSize = stack, UiEffects = uiEffects } };
    }

    /// <summary>The player (10,000 pyreals; a Handy Healing Kit, Leather Bracers, a scroll worth 1, a worthless trinket) and Thelnoth one metre away with his 0062: the order of the capture (the description, the objects, the vendor, then the open).</summary>
    private Game Enter(bool openVendor = true, int coins = 10_000)
    {
        IInboundMessageHandler? handler = null;
        var rig = new LoginRig(fixture, h =>
        {
            handler = h;
            StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true);
        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        var screen = Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        var g = new Game(rig, screen, handler!);
        handler!.OnPlayerCreate(g.Context, new PlayerCreateMsg(Player));
        handler.OnCreateObject(g.Context, ReducerHarness.Create(Player, ReducerHarness.Stamps(), ReducerHarness.Position(Cell), state: PhysicsState.Hidden | PhysicsState.Gravity, name: "Tester"), isUpdate: false);
        CreateObjectMsg[] pack =
        [
            Item(Coins, "Pyreal", 0x40, (uint)coins, stack: (ushort)Math.Min(coins, 25_000)),
            Item(PackKit, "Handy Healing Kit", 0x80, 10),
            Item(Bracers, "Leather Bracers", 0x2, 2_329),
            Item(Scroll, "Scroll of Leadership Ineptitude", 0x2000, 1),
            Item(Junk, "Worthless Trinket", 0x80, 0),
            Item(Gem, "Faceted Gem", 0x8, 40_000_000),
        ];
        foreach (var o in pack)
        {
            handler.OnCreateObject(g.Context, o, isUpdate: false);
        }

        handler.OnPlayerDescription(
            g.Context,
            new GameEventHeader(Player, 1, 0x0013),
            new PlayerDescriptionMsg
            {
                IntProperties = ImmutableDictionary<uint, int>.Empty.Add(PlayerInventory.EncumbranceProperty, 100).Add(PlayerQualities.CoinValueProperty, coins),
                Attributes = [new AttributeDesc(1, 0, 100, 0)],
                Inventory = [.. pack.Select(static o => new ContentDesc(o.Guid, 0))],
            });
        var vendor = ReducerHarness.Create(Thelnoth, ReducerHarness.Stamps(), ReducerHarness.Position(Cell, 11, 20), name: "Thelnoth Cort the Healer");
        handler.OnCreateObject(g.Context, vendor with { Weenie = vendor.Weenie with { Header = WeenieHeaderFlags.UseRadius, UseRadius = 3f } }, isUpdate: false);
        g.Frames(2);
        if (openVendor)
        {
            g.Stock();
            g.Frames(3);
        }

        return g;
    }

    /// <summary>A trade handle that ended as <paramref name="kind"/> (the offline rig has no stand-in world: the Action API refuses a trade at once, so a test plays ACE's answer by handing the window a handle it completed).</summary>
    private static VendorTradeHandle Ended(ActionKind kind, ActionOutcomeKind outcome, string? detail = null)
    {
        var handle = new VendorTradeHandle(9_000, kind, ActionSource.Ui, kind + " (test)", 0);
        Assert.True(handle.TryComplete(null, new ActionOutcome(outcome, 0, detail), 0, static (_, _) => { }));
        return handle;
    }

    private static uint[] Shown(UiItemList list) => [.. Enumerable.Range(0, list.CellCount).Select(list.CellAt).Where(static c => c is { IsEmpty: false }).Select(static c => c!.ItemId)];

    [RequiresLocalDatsFact]
    public void The_window_and_the_floaty_are_hidden_until_the_0062_and_show_the_vendors_name_and_the_other_windows_of_the_floaty_stay_hidden()
    {
        using var g = Enter(openVendor: false);
        var window = g.Window;
        Assert.False(window.Visible);
        Assert.False(g.Env.Visible);
        Assert.Equal(0u, window.ShownVendor);

        g.Stock();
        g.Frames(3);
        Assert.True(window.Visible);
        Assert.True(g.Env.Visible);
        Assert.Equal(Thelnoth, window.ShownVendor);
        Assert.Equal("Thelnoth Cort the Healer", window.Title);
        Assert.Equal(GameVendorPanel.ItemsTabId, window.OpenTabId); // the window opens on the vendor's items
        foreach (var other in new[] { 0x1000005Eu, 0x1000005Fu, 0x10000060u }) // salvage, secure trade, slumlord: not built (m4-env-panel-windows)
        {
            Assert.False(g.Env.GetChildRecursive(other)!.Visible);
        }

        Assert.True(g.Window.Drawn);
    }

    [RequiresLocalDatsFact]
    public void The_shop_rows_are_listed_in_the_0062s_order_with_the_predicted_price_of_the_selected_one_and_the_empty_cells_are_padded()
    {
        using var g = Enter();
        var window = g.Window;
        var shop = window.ShopRow!;
        Assert.Equal([KitRow, PotionRow, ArrowRow, ZeroRow], Shown(shop));
        Assert.Equal(4, shop.OccupiedCount);
        Assert.True(shop.EmptyCount > 0, "the row's width is padded with empty cells (UpdateEmptySlots)");
        Assert.True(shop.CellCount * 32 >= shop.Box.Width - 31);

        // The first row is selected when the window opens (UpdateItemsList's selectFirst): the name and the cost, from PriceToBuy (ceil(1.35 x 10 - 0.1) = 14).
        Assert.Equal(KitRow, shop.SelectedItemId);
        Assert.Equal("Handy Healing Kit", window.ItemNameText);
        Assert.Equal("costs 14p (you have 10,000p)", window.ItemCostText);
        Assert.Equal(g.Session.State.Vendor!.PriceToBuy(KitRow, 1), 14u);
        Assert.False(shop.CellAt(0)!.QuantityText is { Visible: true }); // unlimited stock shows no count

        g.Rig.Click(shop.CellAt(1)!);
        g.Frames(1);
        Assert.Equal(PotionRow, shop.SelectedItemId);
        Assert.Equal("Potion of Healing", window.ItemNameText);
        Assert.Equal("costs 230p (you have 10,000p)", window.ItemCostText);

        // The other two pages are empty and padded too (the sell list keeps one box after its last cell; its page is not shown yet).
        Assert.Equal("Buying 0 items worth 0p", window.BuyText);
        Assert.Equal("You have 10,000p", window.BuyPurseText);
        Assert.Equal("Selling 0 items worth 0p", window.SellText);
    }

    [RequiresLocalDatsFact]
    public void A_right_click_on_a_shop_row_examines_it_with_the_rows_name_and_no_not_built_dialog_S6()
    {
        // M5 7 S6 (D136): retail's item list examines on a right click in every list, a vendor's too (UIElement_ItemList::ListenToElementMessage 0x004E59E0 has no vendor exception; ACE's
        // HandleActionIdentifyObject finds a row for sale through the last opened vendor). A row is no world object, so the window names it from the vendor's list until the answer.
        using var g = Enter();
        var potion = g.Window.ShopRow!.FindItem(PotionRow)!;
        g.RightClick(potion);
        g.Frames(1);
        var examine = g.Examine;
        Assert.True(examine.IsOpen);
        Assert.Equal(PotionRow, examine.ShownGuid);
        Assert.Equal("Potion of Healing", examine.Title);
        Assert.Equal(PotionRow, g.Window.ShopRow!.SelectedItemId); // the right click selects the row too
        Assert.Equal(ActionKind.Identify, Assert.IsAssignableFrom<IdentifyHandle>(examine.LastIdentify).Kind);
        Assert.Empty(g.Rig.Manager.Dialogs.OpenDialogs); // not the old "The examine panel is built in M4 3.2"
        Assert.DoesNotContain(g.Rig.Log.Lines, static l => l.Contains("TODO(m4-itemlist-examine)", StringComparison.Ordinal));

        g.Feed(AceVendorWriters.IdentifyResponse(Player, 900, PotionRow, new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 170 } }));
        g.Frames(1);
        Assert.False(examine.Pending);
        Assert.Equal("Potion of Healing", examine.Title); // the answer carries no name: the row's stays
        Assert.Contains("Value: 170", examine.Lines);
        examine.Close();
    }

    [RequiresLocalDatsFact]
    public void The_buy_button_sends_one_buy_through_the_action_api_and_a_double_click_does_the_same()
    {
        using var g = Enter();
        var window = g.Window;
        var total = g.Rig.Session.ActionLog.Total;
        Assert.True(g.Rig.Click(g.Button(GameVendorPanel.BuyNowButtonId)));
        g.Frames(1);
        var buy = Assert.IsType<VendorTradeHandle>(window.LastBuy);
        Assert.Equal((ActionKind.Buy, ActionSource.Ui), (buy.Kind, buy.Source));
        Assert.Contains($"0x{Thelnoth:X8}", buy.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("items=1", buy.Summary, StringComparison.Ordinal);
        Assert.Equal(1, window.Buys);
        Assert.True(g.Rig.Session.ActionLog.Total > total);

        // The rig has no stand-in world: the Action API refuses the buy at once (the offline refusal), which the window shows; a double click on the potion's cell is a second buy.
        g.Frames(2);
        Assert.Equal(ActionOutcomeKind.Refused, buy.Outcome?.Kind);
        Assert.Equal(ActionApi.OfflineRefusal, window.ItemCostText);
        var potion = window.ShopRow!.FindItem(PotionRow)!;
        var box = potion.ScreenBox;
        g.Rig.Manager.Input.MouseMove((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
        g.Rig.Manager.Input.MouseButton(UiMouseButton.Left, down: true, doubleClick: true);
        g.Rig.Manager.Input.MouseButton(UiMouseButton.Left, down: false);
        g.Frames(2);
        Assert.Equal(2, window.Buys);
        Assert.NotSame(buy, window.LastBuy);
    }

    [RequiresLocalDatsFact]
    public void The_add_button_fills_the_buy_list_with_quantities_and_the_total_is_the_sum_of_the_predicted_prices()
    {
        using var g = Enter();
        var window = g.Window;
        var shop = window.ShopRow!;
        Assert.True(g.Rig.Click(g.Button(GameVendorPanel.AddToBuyButtonId))); // the kit (selected)
        Assert.True(g.Rig.Click(g.Button(GameVendorPanel.AddToBuyButtonId)));
        g.Rig.Click(shop.CellAt(1)!); // the potion
        Assert.True(g.Rig.Click(g.Button(GameVendorPanel.AddToBuyButtonId)));
        g.Frames(2);
        Assert.Equal([new VendorCartLine(KitRow, 2), new VendorCartLine(PotionRow, 1)], window.Cart.ToArray());
        Assert.Equal(2 * 14 + 230, window.CartTotal);
        Assert.Equal([KitRow, PotionRow], Shown(window.BuyRow!));
        Assert.Equal("Buying 3 items worth 258p", window.BuyText);

        // The out-of-stock row (a listed stack of 0) cannot be added; the window says so and the list is as it was.
        Assert.False(window.AddToCart(ZeroRow));
        Assert.Equal("Out of Stock is out of stock", window.Status);
        Assert.Equal(2, window.Cart.Count);

        // Remove takes one unit at a time; clear takes the rest.
        window.OpenTab(GameVendorPanel.BuyTabId);
        g.Frames(2);
        g.Rig.Click(window.BuyRow!.CellAt(0)!);
        Assert.True(g.Rig.Click(g.Button(GameVendorPanel.RemoveBuyButtonId)));
        Assert.Equal(1u, window.CartAmount(KitRow));
        Assert.True(g.Rig.Click(g.Button(GameVendorPanel.ClearBuyButtonId)));
        Assert.Empty(window.Cart);
        Assert.Equal("Buying 0 items worth 0p", window.BuyText);
    }

    [RequiresLocalDatsFact]
    public void Buy_all_sends_one_buy_and_the_list_is_cleared_when_it_is_confirmed()
    {
        using var g = Enter();
        var window = g.Window;
        window.AddToCart(KitRow);
        window.AddToCart(PotionRow);
        window.OpenTab(GameVendorPanel.BuyTabId);
        g.Frames(2);
        Assert.True(g.Rig.Click(g.Button(GameVendorPanel.BuyAllButtonId)));
        var buy = Assert.IsType<VendorTradeHandle>(window.LastBuy);
        Assert.Equal((ActionKind.Buy, ActionSource.Ui), (buy.Kind, buy.Source));
        Assert.Equal(1, window.Buys);
        Assert.Contains("items=2", buy.Summary, StringComparison.Ordinal);

        // ACE accepts (no 00A0, UseDone 0: capture C1): the window waits on a confirmed handle, and the list goes with it.
        window.ReplacePendingTrade(Ended(ActionKind.Buy, ActionOutcomeKind.Confirmed));
        g.Frames(3);
        Assert.Empty(window.Cart);
        Assert.Empty(Shown(window.BuyRow!));
        Assert.Equal(string.Empty, window.Status);
        Assert.Equal("Buying 0 items worth 0p", window.BuyText);
    }

    [RequiresLocalDatsFact]
    public void A_refused_buy_keeps_the_list_and_shows_the_refusal_in_the_buy_pages_first_line()
    {
        using var g = Enter();
        var window = g.Window;
        window.AddToCart(KitRow);
        window.OpenTab(GameVendorPanel.BuyTabId);
        g.Frames(2);
        Assert.NotNull(window.BuyCart());
        window.ReplacePendingTrade(Ended(ActionKind.Buy, ActionOutcomeKind.ServerError, "The vendor cannot supply that")); // the 00A0 of a refusal (capture C2)
        g.Frames(3);
        Assert.Equal([new VendorCartLine(KitRow, 1)], window.Cart.ToArray()); // kept
        Assert.Equal("The vendor cannot supply that", window.Status);
        Assert.Equal(window.Status, window.BuyText); // the refusal's text replaces "Buying ..." until the list changes
        window.AddToCart(KitRow);
        Assert.StartsWith("Buying 2 items", window.BuyText, StringComparison.Ordinal);
    }

    [RequiresLocalDatsFact]
    public void A_buy_the_pyreals_do_not_cover_is_refused_before_anything_is_sent_with_retails_text()
    {
        using var g = Enter(coins: 5);
        var window = g.Window;
        g.Frames(2);
        Assert.Equal("costs 14p (you have 5p)", window.ItemCostText);
        Assert.Null(window.BuyNow(KitRow));
        Assert.Equal(GameVendorPanel.NotEnoughMoneyText, window.Status);
        Assert.Equal(GameVendorPanel.NotEnoughMoneyText, window.ItemCostText);
        Assert.Equal(0, window.Buys);

        window.AddToCart(KitRow);
        Assert.Null(window.BuyCart());
        Assert.Equal(0, window.Buys);
        Assert.Single(window.Cart); // kept

        g.SetCoins(10_000); // the purse grows: the cost line is the plain one again
        g.Frames(2);
        Assert.Equal("You have 10,000p", window.BuyPurseText);
    }

    [RequiresLocalDatsFact]
    public void The_sell_lists_drop_rule_follows_the_vendors_mask_value_bounds_and_the_pack_and_refuses_with_retails_texts()
    {
        using var g = Enter();
        var window = g.Window;
        Assert.Equal(VendorSellVerdict.Ok, window.SellVerdictFor(PackKit)); // a Misc item the mask takes
        Assert.Equal(VendorSellVerdict.WrongType, window.SellVerdictFor(Bracers)); // armour: not in Thelnoth's mask 0x44080
        Assert.Equal("That item cannot be sold here", GameVendorPanel.SellRefusalText(VendorSellVerdict.WrongType));
        Assert.Equal(VendorSellVerdict.WrongType, window.SellVerdictFor(Scroll)); // 0x2000 is not in the mask either
        Assert.Equal(VendorSellVerdict.NoValue, window.SellVerdictFor(Junk));
        Assert.Equal("That item has no value and cannot be sold", GameVendorPanel.SellRefusalText(VendorSellVerdict.NoValue));
        Assert.Equal(VendorSellVerdict.NotCarried, window.SellVerdictFor(KitRow)); // a shop row is not carried
        Assert.Equal("You can only sell items you are carrying", GameVendorPanel.SellRefusalText(VendorSellVerdict.NotCarried));

        // The bounds: a vendor that pays for 20..1,000 only.
        g.Stock(mask: Mask | 0x2 | 0x8, min: 20, max: 1_000);
        g.Frames(2);
        Assert.Equal(VendorSellVerdict.TooCheap, window.SellVerdictFor(PackKit)); // value 10 < 20
        Assert.Equal("That item is too cheap to sell here", GameVendorPanel.SellRefusalText(VendorSellVerdict.TooCheap));
        Assert.Equal(VendorSellVerdict.TooValuable, window.SellVerdictFor(Bracers)); // 2,329 > 1,000
        Assert.Equal("That item is too valuable to sell here", GameVendorPanel.SellRefusalText(VendorSellVerdict.TooValuable));
        Assert.Equal(VendorSellVerdict.TooValuable, window.SellVerdictFor(Gem));

        // A refused item goes nowhere and the window says why.
        Assert.Equal(VendorSellVerdict.TooValuable, window.AddToSell(Bracers));
        Assert.Empty(window.SellItems);
        Assert.Equal("That item is too valuable to sell here", window.Status);
        Assert.Equal(window.Status, window.SellText);
    }

    [RequiresLocalDatsFact]
    public void An_item_in_the_sell_list_carries_the_sell_mark_the_payout_is_the_predicted_one_and_sell_all_sends_one_sale_that_clears_the_list_when_confirmed()
    {
        using var g = Enter();
        var window = g.Window;
        g.Open(); // the inventory page: the item list's cells are the ones the sell mark goes on
        g.Frames(4);
        var packCell = g.Page.PackList.FindItem(PackKit);
        Assert.NotNull(packCell);

        Assert.Equal(VendorSellVerdict.Ok, window.AddToSell(PackKit));
        g.Frames(2);
        Assert.Equal(GameVendorPanel.SellTabId, window.OpenTabId); // the sell tab opens on a drop (UpdateDragOver)
        Assert.Equal([PackKit], window.SellItems.ToArray());
        Assert.Equal([PackKit], Shown(window.SellRow!));
        Assert.Equal(9, window.SellTotal); // PayoutFor(Value 10) = floor(0.9 x 10 + 0.1)
        Assert.Equal("Selling 1 item worth 9p", window.SellText);
        Assert.Equal(VendorSellVerdict.AlreadyListed, window.AddToSell(PackKit));
        Assert.True(window.SellRow!.CellAt(0)!.HasSellMark, "the item cell's template has the sell mark element 0x10000437");
        Assert.True(window.SellRow.CellAt(0)!.SellMarkShown);
        Assert.True(packCell!.SellMarkShown, "the pack's cell of the item carries the mark too (SetSellState re-draws every cell of the object)");

        var sentBefore = window.Sells;
        Assert.True(g.Rig.Click(g.Button(GameVendorPanel.SellAllButtonId)));
        var sale = Assert.IsType<VendorTradeHandle>(window.LastSell);
        Assert.Equal((ActionKind.Sell, ActionSource.Ui), (sale.Kind, sale.Source));
        Assert.Equal(sentBefore + 1, window.Sells);
        Assert.Contains("items=1", sale.Summary, StringComparison.Ordinal);
        g.Feed(AceM3Writers.InventoryRemoveObject(PackKit)); // the kit left the pack (a sale's 0024/0022): 3.2-R21 removes the items that left
        window.ReplacePendingTrade(Ended(ActionKind.Sell, ActionOutcomeKind.Confirmed));
        g.Frames(3);
        Assert.Empty(window.SellItems);
        Assert.Empty(Shown(window.SellRow));
        Assert.False(packCell.SellMarkShown); // the mark goes with the list
        Assert.Equal("Selling 0 items worth 0p", window.SellText);
    }

    [RequiresLocalDatsFact]
    public void A_confirmed_sale_keeps_an_item_the_server_skipped_in_the_sell_list_and_names_it_3_2_R21()
    {
        using var g = Enter();
        var window = g.Window;
        Assert.Equal(VendorSellVerdict.Ok, window.AddToSell(PackKit));
        g.Frames(2);
        Assert.True(g.Rig.Click(g.Button(GameVendorPanel.SellAllButtonId)));
        window.ReplacePendingTrade(Ended(ActionKind.Sell, ActionOutcomeKind.Confirmed)); // ACE skipped the kit with its 02EB and the sale is Confirmed all the same: the kit never left the pack
        g.Frames(3);

        Assert.Equal([PackKit], window.SellItems.ToArray());
        Assert.Equal("The vendor did not take: Handy Healing Kit", window.Status);
    }

    [RequiresLocalDatsFact]
    public void The_close_button_closes_the_models_open_vendor_with_the_window_3_2_R23()
    {
        using var g = Enter();
        Assert.NotNull(g.Session.Vendor);
        Assert.True(g.Window.RequestClose());
        Assert.False(g.Window.Visible);
        Assert.Null(g.Session.Vendor); // the window and the model agree
    }

    [RequiresLocalDatsFact]
    public void A_refused_sale_keeps_the_list_and_the_marks_and_remove_and_clear_empty_it()
    {
        using var g = Enter();
        var window = g.Window;
        g.Open();
        g.Frames(4);
        Assert.Equal(VendorSellVerdict.Ok, window.AddToSell(PackKit));
        Assert.NotNull(window.SellAll());
        window.ReplacePendingTrade(Ended(ActionKind.Sell, ActionOutcomeKind.ServerError, "The vendor will not buy that"));
        g.Frames(3);
        Assert.Equal([PackKit], window.SellItems.ToArray());
        Assert.Equal("The vendor will not buy that", window.Status);
        Assert.True(g.Page.PackList.FindItem(PackKit)!.SellMarkShown);

        Assert.True(g.Rig.Click(g.Button(GameVendorPanel.ClearSellButtonId)));
        Assert.Empty(window.SellItems);
        Assert.False(g.Page.PackList.FindItem(PackKit)!.SellMarkShown);
        Assert.Equal(VendorSellVerdict.Ok, window.AddToSell(PackKit));
        g.Rig.Click(window.SellRow!.CellAt(0)!);
        Assert.True(g.Rig.Click(g.Button(GameVendorPanel.RemoveSellButtonId)));
        Assert.Empty(window.SellItems);
    }

    [RequiresLocalDatsFact]
    public void A_pack_item_dragged_onto_the_window_goes_into_the_sell_list_and_one_the_mask_refuses_goes_nowhere()
    {
        using var g = Enter();
        var window = g.Window;
        g.Open();
        g.Frames(4);
        var pack = g.Page.PackList;
        Assert.Equal(GameVendorPanel.ItemsTabId, window.OpenTabId);

        window.Parent!.MoveTo(95, 20); // the environment floaty (0,400, centred) overlaps the inventory page's lower rows by design; the test moves it up so the press lands on the pack cell
        g.Frames(2);
        g.DragTo(pack.FindItem(PackKit)!, window.GetChildRecursive(GameVendorPanel.ShopListId)!); // dropped on the shop's list: the window is the target
        Assert.True(new[] { PackKit }.SequenceEqual(window.SellItems), $"drag: dropped={g.Drag.Dropped} refused={g.Drag.Refused} cancelled={g.Drag.Cancelled} end={g.Drag.LastEnd} window={window.ScreenBox} cell={Game.Center(pack.FindItem(PackKit)!)} shop={window.GetChildRecursive(GameVendorPanel.ShopListId)!.ScreenBox} page={g.Page.PackList.ScreenBox}");
        Assert.Equal(GameVendorPanel.SellTabId, window.OpenTabId);
        Assert.Equal(1, g.Drag.Dropped);

        var refused = g.Drag.Refused;
        g.DragTo(pack.FindItem(Bracers)!, window.GetChildRecursive(GameVendorPanel.SellListId)!);
        Assert.Equal([PackKit], window.SellItems.ToArray());
        Assert.Equal(refused + 1, g.Drag.Refused);
        Assert.Equal("That item cannot be sold here", window.Status);
    }

    [RequiresLocalDatsFact]
    public void A_shop_cell_dragged_onto_the_window_goes_into_the_buy_list()
    {
        using var g = Enter();
        var window = g.Window;
        g.DragTo(window.ShopRow!.CellAt(1)!, window.GetChildRecursive(GameVendorPanel.TabPanelId)!);
        Assert.Equal([new VendorCartLine(PotionRow, 1)], window.Cart.ToArray());
        Assert.Equal(GameVendorPanel.BuyTabId, window.OpenTabId);
    }

    [RequiresLocalDatsFact]
    public void A_second_0062_redraws_the_window_and_drops_buy_lines_the_shop_no_longer_holds()
    {
        using var g = Enter();
        var window = g.Window;
        window.AddToCart(KitRow);
        window.AddToCart(PotionRow);
        g.Stock(rows: [VendorRig.Row(-1, PotionRow, "Potion of Healing", 377, 0x80, 170), VendorRig.Row(5, 0x80000350, "Stamina Potion", 378, 0x80, 170)]);
        g.Frames(3);
        Assert.Equal([PotionRow, 0x80000350u], Shown(window.ShopRow!));
        Assert.Equal([new VendorCartLine(PotionRow, 1)], window.Cart.ToArray()); // the kit row left the shop
        Assert.True(window.Visible);
    }

    [RequiresLocalDatsFact]
    public void The_window_closes_when_the_player_leaves_the_vendors_range_and_the_close_button_hides_it_or_asks_first()
    {
        using var g = Enter();
        var window = g.Window;
        Assert.True(g.Env.Visible);

        // The close button with both lists empty hides it at once (nothing is sent: the Action API has no close for a vendor).
        var total = g.Rig.Session.ActionLog.Total;
        Assert.True(g.Rig.Click(g.Button(GameVendorPanel.CloseButtonId)));
        g.Frames(2);
        Assert.False(window.Visible);
        Assert.False(g.Env.Visible);
        Assert.Equal(total, g.Rig.Session.ActionLog.Total);

        // A new 0062 (the player uses him again) shows it; a list that is not empty asks first.
        g.Stock();
        g.Frames(3);
        Assert.True(window.Visible);
        window.AddToCart(KitRow);
        Assert.False(window.RequestClose());
        Assert.True(window.Visible);
        g.Frames(2);
        Assert.True(g.Rig.Manager.Dialogs.OpenDialogs.Any());

        // Walking out of range (UseRadius 3 m plus the model's 1 m): the window closes, the lists go.
        g.WalkTo(30, 9);
        g.Frames(3);
        Assert.Null(g.Session.State.Vendor);
        Assert.False(window.Visible);
        Assert.False(g.Env.Visible);
        Assert.Empty(window.Cart);
    }

    [RequiresLocalDatsFact]
    public void The_type_filter_the_alternate_currency_and_the_quantity_overlay_answer_as_not_built()
    {
        using var g = Enter();
        var window = g.Window;
        var menu = window.TypeMenu!;
        Assert.Equal(1, menu.ItemCount); // "All" only
        menu.Open();
        g.Frames(2);
        Assert.False(menu.IsOpen); // the not-built dialog took it
        Assert.True(g.Rig.Manager.Dialogs.OpenDialogs.Any());
        Assert.Contains(g.Rig.Log.Lines, l => l.Contains("TODO(m4-vendor-type-filter)", StringComparison.Ordinal));

        // A vendor that trades in another currency: its texts say so and a trade is refused, nothing sent.
        g.Stock(altWcid: 2621);
        g.Frames(3);
        Assert.Equal(GameVendorPanel.AlternateCurrencyText, window.ItemCostText);
        Assert.Null(window.BuyNow(KitRow));
        Assert.False(window.AddToCart(KitRow));
        Assert.Equal(VendorSellVerdict.AlternateCurrency, window.AddToSell(PackKit));
        Assert.Equal(0, window.Buys);
        Assert.Contains(g.Rig.Log.Lines, l => l.Contains("TODO(m4-vendor-alt-currency)", StringComparison.Ordinal));

        // A stackable item (the arrow stacks to 25) is added one at a time; retail's slider is named.
        g.Stock();
        g.Frames(2);
        window.AddToCart(ArrowRow);
        Assert.Contains(g.Rig.Log.Lines, l => l.Contains("TODO(m4-vendor-quantity-overlay)", StringComparison.Ordinal));
    }
}
