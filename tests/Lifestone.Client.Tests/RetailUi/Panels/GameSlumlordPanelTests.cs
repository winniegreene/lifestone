using System.Collections.Immutable;
using Lifestone.Client.RetailUi;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Housing;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Tests;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The house guardian's window (M6 villa; <c>gmSlumlordUI</c> 0x10000013, window 0x10000060 in <c>gmFloatyEnvPanelUI</c>) over the real gameplay layout, the reducer and the guardian window's model: a HouseProfile (GameEvent
/// 0x021D) handed to the session's inbound handler opens it on the pane its ownership asks for, its left text lists the price lines with what is paid, placed and short, a pack item dropped on it is placed in the model's pane, its
/// Buy button is the model's <see cref="GuardianWindowModel.PressBuy"/>, a refreshed profile redraws it and the model's close hides it. With <c>LIFESTONE_UI_FRAMES</c> set, two review frames are written (<see cref="UiReviewFrames"/>).
/// </summary>
public sealed class GameSlumlordPanelTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Player = 0x50000001;
    private const uint Cell = 0x860201AD;
    private const uint Guardian = 0x7A9B5001;
    private const uint Coins1 = 0x80000201;
    private const uint Coins2 = 0x80000202;
    private const uint Coins3 = 0x80000203;
    private const uint Coins4 = 0x80000204;
    private const uint Writ = 0x80000205;
    private const uint Kit = 0x80000206;
    private const uint WritWcid = 11710;
    private const uint InventoryButton = 0x100001B1;
    private const uint EnvPanelId = 0x100005FD;

    private sealed class Game(LoginRig rig, GamePlayScreen screen, IInboundMessageHandler handler) : IDisposable
    {
        private uint _event = 700;

        public LoginRig Rig => rig;

        public InboundContext Context => new(0, MessageQueue.SmartBox, 0);

        public GameSlumlordPanel Window => Assert.IsType<GameSlumlordPanel>(screen.Root!.GetChildRecursive(GameSlumlordPanel.WindowId));

        public UiElement Env => screen.Root!.GetChildRecursive(EnvPanelId)!;

        public GuardianWindowModel Model => rig.Session.GuardianWindow;

        public UiDrag Drag => rig.Manager.Input.Drag;

        public GameInventoryPage Page => Assert.Single(screen.PanelControllers!.ControllersOf(7).OfType<GameInventoryPage>());

        public void Profile(HouseProfileMsg profile) => handler.OnHouseProfile(Context, new GameEventHeader(Player, ++_event, GameEventTypes.HouseProfile), profile);

        public void Frames(int n)
        {
            for (var i = 0; i < n; i++)
            {
                rig.Frame();
            }
        }

        public void OpenInventory()
        {
            Assert.True(rig.Click(Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(InventoryButton))));
            Frames(14);
            Assert.Equal(7u, screen.PanelHost!.CurrentPanel);
        }

        public void WalkTo(float x, ushort sequence) => handler.OnUpdatePosition(Context, ReducerHarness.Move(Player, 1, sequence, 1, 1, Cell, x));

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

        public void Dispose() => rig.Dispose();

        private static (int X, int Y) Center(UiElement element)
        {
            var box = element.ScreenBox;
            return ((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
        }
    }

    private static HousePayment Pyreals(int required, int paid = 0) => new(required, paid, HousePayment.PyrealWcid, "Pyreal", "Pyreals");

    private static HousePayment Writs(int required, int paid = 0) => new(required, paid, WritWcid, "Writ of Refuge", "Writs of Refuge");

    /// <summary>An unowned apartment: 100,000 pyreals and one Writ of Refuge to buy. The rent line is a test value (only the purchase pane is judged).</summary>
    private static HouseProfileMsg UnownedApartment() =>
        new(Guardian, 0x1234, 0, HouseProfileMsg.ActiveFlag, -1, -1, -1, -1, false, (uint)HouseType.Apartment, string.Empty, [Pyreals(100_000), Writs(1)], [Pyreals(25_000)]);

    /// <summary>
    /// A villa the player owns, its rent half paid: the villa's terms of cache/m6/villa/villa-facts.md (2,000,000 pyreals, 5 writs and a Crude Lockpick to buy, all paid once owned; 100,000 pyreals and 2 writs a period), with 50,000 pyreals
    /// and one writ in the guardian.
    /// </summary>
    private static HouseProfileMsg OwnedVilla() =>
        new(
            Guardian,
            0x0567,
            Player,
            HouseProfileMsg.ActiveFlag,
            -1,
            -1,
            -1,
            -1,
            false,
            (uint)HouseType.Villa,
            "Tester",
            [Pyreals(2_000_000, 2_000_000), Writs(5, 5), new HousePayment(1, 1, 511, "Crude Lockpick", "Crude Lockpicks")],
            [Pyreals(100_000, 50_000), Writs(2, 1)]);

    private static CreateObjectMsg Item(uint guid, uint wcid, string name, uint icon, uint itemType, uint value, ushort? stack = null)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
        return basis with { Weenie = basis.Weenie with { WeenieClassId = wcid, IconId = icon, ItemType = itemType, ContainerId = Player, Value = value, StackSize = stack } };
    }

    /// <summary>The player with four stacks of 25,000 pyreals, a Writ of Refuge and a Handy Healing Kit in the main pack, and the guardian beside them; no profile yet.</summary>
    private Game Enter()
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
            Item(Coins1, HousePayment.PyrealWcid, "Pyreal", 0x0600229F, 0x40, 25_000, stack: 25_000), // the Pyreal's icon (ACE's weenie 273)
            Item(Coins2, HousePayment.PyrealWcid, "Pyreal", 0x0600229F, 0x40, 25_000, stack: 25_000),
            Item(Coins3, HousePayment.PyrealWcid, "Pyreal", 0x0600229F, 0x40, 25_000, stack: 25_000),
            Item(Coins4, HousePayment.PyrealWcid, "Pyreal", 0x0600229F, 0x40, 25_000, stack: 25_000),
            Item(Writ, WritWcid, "Writ of Refuge", 0x060022DD, 0x80, 100, stack: 1), // the writ's icon, item type and value (ACE's weenie 11710)
            Item(Kit, 628, "Handy Healing Kit", 0x060011D0, 0x80, 10),
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
                IntProperties = ImmutableDictionary<uint, int>.Empty.Add(PlayerInventory.EncumbranceProperty, 100).Add(PlayerQualities.CoinValueProperty, 100_000),
                Attributes = [new AttributeDesc(1, 0, 100, 0)],
                Inventory = [.. pack.Select(static o => new ContentDesc(o.Guid, 0))],
            });
        var guardian = ReducerHarness.Create(Guardian, ReducerHarness.Stamps(), ReducerHarness.Position(Cell, 11, 20), name: "Covenant Crystal");
        handler.OnCreateObject(g.Context, guardian with { Weenie = guardian.Weenie with { Header = WeenieHeaderFlags.UseRadius, UseRadius = 3f } }, isUpdate: false);
        g.Frames(2);
        return g;
    }

    private static uint[] Shown(UiItemList list) => [.. Enumerable.Range(0, list.CellCount).Select(list.CellAt).Where(static c => c is { IsEmpty: false }).Select(static c => c!.ItemId)];

    private void SaveFrame(Game g, string name)
    {
        if (UiReviewFrames.Save(g.Rig, fixture, name) is { } frame)
        {
            output.WriteLine($"wrote {frame}");
        }
    }

    [RequiresLocalDatsFact]
    public void The_window_is_hidden_until_the_profile_and_an_unowned_apartment_opens_on_the_purchase_pane_with_its_price_lines()
    {
        using var g = Enter();
        var window = g.Window;
        Assert.False(window.Visible);
        Assert.False(g.Env.Visible);

        g.Profile(UnownedApartment());
        g.Frames(3);
        Assert.True(window.Visible);
        Assert.True(g.Env.Visible);
        Assert.Equal(Guardian, window.ShownGuardian);
        Assert.Equal(GameSlumlordPanel.BuyTabId, window.OpenTabId);
        Assert.Equal(GuardianWindowPane.Buy, window.OpenPane);
        Assert.True(window.GetChildRecursive(GameSlumlordPanel.BuyPageId)!.Visible);
        Assert.False(window.GetChildRecursive(GameSlumlordPanel.RentPageId)!.Visible);
        Assert.Equal("Apartment, for sale  " + GameSlumlordPanel.PlaceholderTag, window.Title);
        Assert.Equal("The purchase price:\n100,000 Pyreals: 100,000 short\n1 Writ of Refuge: 1 short", window.BuyTermsText);
        Assert.Equal(GameSlumlordPanel.DragHint, window.BuyStatusText);
        Assert.False(g.Button(GameSlumlordPanel.BuyButtonId).Ghosted);
        Assert.True(g.Button(GameSlumlordPanel.PayButtonId).Ghosted); // no maintenance on a dwelling nobody owns
        Assert.Empty(Shown(window.BuyRow!));

        // The tabs and buttons are the dat's: "Buy" and "Maintenance" (table 0x23000001).
        Assert.Equal("Buy", Assert.IsAssignableFrom<UiTextElement>(window.GetChildRecursive(GameSlumlordPanel.BuyTabId)).Text);
        Assert.Equal("Maintenance", Assert.IsAssignableFrom<UiTextElement>(window.GetChildRecursive(GameSlumlordPanel.RentTabId)).Text);
        Assert.Equal("Buy", g.Button(GameSlumlordPanel.BuyButtonId).Text);
        Assert.Equal("Maintenance", g.Button(GameSlumlordPanel.PayButtonId).Text);

        // The other unbuilt windows of the floaty stay hidden (salvage, secure trade).
        foreach (var other in new[] { 0x1000005Eu, 0x1000005Fu })
        {
            Assert.False(g.Env.GetChildRecursive(other)!.Visible);
        }

        // Placed through the model, the lines and the list follow.
        Assert.True(g.Model.Place(GuardianPane.Buy, Coins1));
        Assert.True(g.Model.Place(GuardianPane.Buy, Coins2));
        Assert.True(g.Model.Place(GuardianPane.Buy, Writ));
        window.Redraw();
        Assert.Equal("The purchase price:\n100,000 Pyreals: 50,000 placed, 50,000 short\n1 Writ of Refuge: 1 placed", window.BuyTermsText);
        Assert.Equal([Coins1, Coins2, Writ], Shown(window.BuyRow!));
        SaveFrame(g, "villa-guardian-apartment-unowned");
    }

    [RequiresLocalDatsFact]
    public void An_owned_villa_with_its_rent_half_paid_opens_on_the_maintenance_pane_with_what_is_paid_and_short()
    {
        using var g = Enter();
        var window = g.Window;
        g.Profile(OwnedVilla());
        g.Frames(3);
        Assert.True(window.Visible);
        Assert.Equal(GameSlumlordPanel.RentTabId, window.OpenTabId);
        Assert.Equal(GuardianWindowPane.Rent, window.OpenPane);
        Assert.True(window.GetChildRecursive(GameSlumlordPanel.RentPageId)!.Visible);
        Assert.False(window.GetChildRecursive(GameSlumlordPanel.BuyPageId)!.Visible);
        Assert.Equal("Villa, owned by Tester  " + GameSlumlordPanel.PlaceholderTag, window.Title);
        Assert.Equal("The maintenance this period:\n100,000 Pyreals: 50,000 paid, 50,000 short\n2 Writs of Refuge: 1 paid, 1 short", window.RentTermsText);
        Assert.Equal("The purchase price (paid):\n2,000,000 Pyreals: paid\n5 Writs of Refuge: paid\n1 Crude Lockpick: paid", window.BuyTermsText);
        Assert.True(g.Button(GameSlumlordPanel.BuyButtonId).Ghosted); // owned: nothing to buy
        Assert.False(g.Button(GameSlumlordPanel.PayButtonId).Ghosted);
        Assert.Equal(GameSlumlordPanel.DragHint, window.RentStatusText);

        // Two stacks and the writ placed: the pane covers the rest of the period.
        Assert.True(g.Model.Place(GuardianPane.Rent, Coins1));
        Assert.True(g.Model.Place(GuardianPane.Rent, Coins2));
        Assert.True(g.Model.Place(GuardianPane.Rent, Writ));
        window.Redraw();
        Assert.Equal("The maintenance this period:\n100,000 Pyreals: 50,000 paid, 50,000 placed\n2 Writs of Refuge: 1 paid, 1 placed", window.RentTermsText);
        Assert.Equal(GameSlumlordPanel.ReadyText, window.RentStatusText);
        Assert.Equal([Coins1, Coins2, Writ], Shown(window.RentRow!));
        SaveFrame(g, "villa-guardian-villa-rent-half-paid");

        // A double click on a placed cell takes it back out.
        Assert.True(window.RemoveItem(Writ));
        Assert.Equal([Coins1, Coins2], Shown(window.RentRow!));
        Assert.Equal([Coins1, Coins2], g.Model.Placed(GuardianPane.Rent).ToArray());
    }

    [RequiresLocalDatsFact]
    public void A_pack_item_dropped_on_the_window_is_placed_in_the_open_pane_and_one_the_guardian_does_not_ask_for_shows_the_models_error_line()
    {
        using var g = Enter();
        var window = g.Window;
        g.Profile(UnownedApartment());
        g.Frames(3);
        g.OpenInventory();
        g.Frames(4);
        var pack = g.Page.PackList;
        window.Parent!.MoveTo(95, 20); // the environment floaty (0,400, centred) overlaps the inventory page's lower rows by design (docs/retail-ui.md 23.11); the test moves it up so the press lands on the pack cell
        g.Frames(2);

        g.DragTo(pack.FindItem(Coins3)!, window.GetChildRecursive(GameSlumlordPanel.BuyListId)!);
        Assert.True(new[] { Coins3 }.SequenceEqual(g.Model.Placed(GuardianPane.Buy)), $"drag: dropped={g.Drag.Dropped} refused={g.Drag.Refused} cancelled={g.Drag.Cancelled} window={window.ScreenBox}");
        Assert.Equal([Coins3], Shown(window.BuyRow!));
        Assert.Contains("25,000 placed", window.BuyTermsText, StringComparison.Ordinal);

        g.DragTo(pack.FindItem(Kit)!, window.GetChildRecursive(GameSlumlordPanel.BuyTermsTextId)!); // the kit pays no line: the model refuses it
        Assert.Equal([Coins3], g.Model.Placed(GuardianPane.Buy).ToArray());
        Assert.Equal("The guardian does not ask for that.", g.Model.ErrorLine);
        Assert.Equal("The guardian does not ask for that.", window.BuyStatusText);
        SaveFrame(g, "villa-guardian-refused-drop");
    }

    [RequiresLocalDatsFact]
    public void The_buy_button_is_the_models_press_buy_refused_with_its_error_line_until_the_pane_covers_the_price_then_sent_from_the_ui()
    {
        using var g = Enter();
        var window = g.Window;
        g.Profile(UnownedApartment());
        g.Frames(3);

        // Not covered: the model refuses and nothing is sent.
        var total = g.Rig.Session.ActionLog.Total;
        Assert.True(g.Rig.Click(g.Button(GameSlumlordPanel.BuyButtonId)));
        g.Frames(1);
        Assert.Equal(1, window.Presses);
        Assert.Null(window.LastPress);
        Assert.Equal("The purchase pane does not cover the price.", window.BuyStatusText);
        Assert.Equal(total, g.Rig.Session.ActionLog.Total);
        SaveFrame(g, "villa-guardian-buy-refused");

        // Covered: the press goes out as the model's BuyHouse, from the UI, with the placed stacks.
        foreach (var item in new[] { Coins1, Coins2, Coins3, Coins4, Writ })
        {
            Assert.True(window.PlaceItem(item));
        }

        Assert.Equal(GameSlumlordPanel.ReadyText, window.BuyStatusText);
        Assert.True(g.Rig.Click(g.Button(GameSlumlordPanel.BuyButtonId)));
        g.Frames(1);
        var buy = Assert.IsAssignableFrom<ActionHandle>(window.LastPress);
        Assert.Equal((ActionKind.BuyHouse, ActionSource.Ui), (buy.Kind, buy.Source));
        Assert.Contains($"guardian=0x{Guardian:X8}", buy.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"0x{Writ:X8}", buy.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.True(g.Rig.Session.ActionLog.Total > total);

        // The rig has no server: the Action API refuses at once, and the window shows the refusal.
        g.Frames(2);
        Assert.Equal(ActionOutcomeKind.Refused, buy.Outcome?.Kind);
        Assert.Equal(ActionApi.OfflineRefusal, window.BuyStatusText);
    }

    [RequiresLocalDatsFact]
    public void A_refreshed_profile_redraws_the_window_and_switches_to_maintenance_once_owned_and_the_models_close_hides_it()
    {
        using var g = Enter();
        var window = g.Window;
        g.Profile(UnownedApartment());
        g.Frames(3);
        Assert.Equal(GameSlumlordPanel.BuyTabId, window.OpenTabId);

        // The purchase went through: ACE's refreshed profile names the owner.
        g.Profile(UnownedApartment() with { OwnerId = Player, OwnerName = "Tester", Buy = [Pyreals(100_000, 100_000), Writs(1, 1)] });
        g.Frames(2);
        Assert.True(window.Visible);
        Assert.Equal(GameSlumlordPanel.RentTabId, window.OpenTabId);
        Assert.Equal("Apartment, owned by Tester  " + GameSlumlordPanel.PlaceholderTag, window.Title);
        Assert.True(g.Button(GameSlumlordPanel.BuyButtonId).Ghosted);

        // The close button hides the window only; the player using the guardian again (a new profile) shows it.
        Assert.True(g.Rig.Click(g.Button(GameSlumlordPanel.CloseButtonId)));
        g.Frames(2);
        Assert.False(window.Visible);
        Assert.False(g.Env.Visible);
        g.Profile(OwnedVilla() with { OwnerName = "Tester" });
        g.Frames(2);
        Assert.True(window.Visible);

        // Out of the guardian's range (UseRadius 3 m and the model's margin): the model closes it, and the window and the floaty go.
        g.WalkTo(30, 9);
        g.Frames(3);
        Assert.Null(g.Rig.Session.State.Guardian);
        Assert.False(window.Visible);
        Assert.False(g.Env.Visible);
    }

    [Fact]
    public void The_terms_text_reads_the_profile_alone_without_a_model_and_names_the_level_asked_for()
    {
        var state = new GameState { Guardian = new HouseGuardian(UnownedApartment() with { MinLevel = 20 }, 1, 0) };
        var view = new GuardianWindowView(state, null); // a view without a model: the profile alone, nothing placed
        Assert.False(view.HasModel);
        Assert.Equal("Level 20 or higher may buy.\nThe purchase price:\n100,000 Pyreals: 100,000 short\n1 Writ of Refuge: 1 short", GameSlumlordPanel.TermsText(view, GuardianWindowPane.Buy));
        Assert.Equal("The maintenance once bought:\n25,000 Pyreals: 25,000 short", GameSlumlordPanel.TermsText(view, GuardianWindowPane.Rent));
        Assert.Equal("Apartment, for sale", GameSlumlordPanel.TitleOf(view));
        Assert.False(view.Place(GuardianWindowPane.Buy, Coins1));
        Assert.Null(view.PressBuy(ActionSource.Ui));
        state.Guardian = new HouseGuardian(OwnedVilla() with { MaintenanceFree = true }, 2, 0);
        Assert.StartsWith("No maintenance is due this period.", GameSlumlordPanel.TermsText(view, GuardianWindowPane.Rent), StringComparison.Ordinal);
        state.Guardian = new HouseGuardian(OwnedVilla() with { Type = 9 }, 3, 0);
        Assert.Equal("Dwelling, owned by Tester", GameSlumlordPanel.TitleOf(view));
    }
}
