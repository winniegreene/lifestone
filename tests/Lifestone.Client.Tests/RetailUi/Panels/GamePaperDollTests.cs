using System.Diagnostics;
using Lifestone.Client.Avatars;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Tests;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The paperdoll (M3 2.2 slice b2): <see cref="GamePaperDoll"/> over the real layout and a scripted inventory. The slot table is <c>gmPaperDollUI::GetLocationInfoFromElementID</c>'s; BSD Mage's wand and five leather
/// pieces land in the slots retail puts them in (a coat in four); empty slots keep the layout's art; the page follows the inventory, and only while shown; a double click on a worn item takes it off through the Action API.
/// </summary>
public sealed class GamePaperDollTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint InventoryButton = 0x100001B1;
    private const uint Player = 0x50000001;
    private const uint Wand = 0x80000201;
    private const uint Cap = 0x80000202;
    private const uint Coat = 0x80000203;
    private const uint Leggings = 0x80000204;
    private const uint Gauntlets = 0x80000205;
    private const uint Boots = 0x80000206;
    private const uint Held = 0x01000000;
    private const uint HeadWear = 0x1;
    private const uint CoatLocations = 0x200 | 0x400 | 0x800 | 0x1000; // chest, abdomen, upper arm, lower arm armour
    private const uint LegLocations = 0x2000 | 0x4000; // upper and lower leg armour
    private const uint HandWear = 0x20;
    private const uint FootWear = 0x100;

    private sealed class Game(LoginRig rig, GamePlayScreen screen, IInboundMessageHandler handler, RecordingViewHost views) : IDisposable
    {
        private uint _event;
        private ushort _descStamp = 2;

        public RecordingViewHost Views => views;

        /// <summary>The server's ObjDescEvent for the player: a part of the body changed (<paramref name="gfxObj"/> for part <paramref name="part"/>).</summary>
        public void ChangePlayerDesc(byte part, uint gfxObj) =>
            handler.OnObjDescEvent(Context, new ObjDescEventMsg(Player, new ModelData(null, [], [], [new Lifestone.Net.Types.AnimPartChange(part, gfxObj)]), 1, ++_descStamp));

        public LoginRig Rig => rig;

        public GamePlayScreen Screen => screen;

        public IInboundMessageHandler Handler => handler;

        public GamePaperDoll Doll => Assert.Single(screen.PanelControllers!.ControllersOf(7).OfType<GamePaperDoll>());

        public GamePanelHost Host => screen.PanelHost!;

        public PlayerInventory Inventory => rig.Session.State.Self.Inventory;

        public InboundContext Context => new(0, MessageQueue.SmartBox, 0);

        public UiButton Button => Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(InventoryButton));

        public void Create(CreateObjectMsg message) => handler.OnCreateObject(Context, message, isUpdate: false);

        public void Wield(uint item, uint location) =>
            handler.OnWieldObject(Context, new GameEventHeader(Player, ++_event, 0x0023), new WieldObjectEvent(item, location));

        public void Unwield(uint item) =>
            handler.OnInventoryPutObjInContainer(Context, new GameEventHeader(Player, ++_event, 0x0022), new InventoryPutObjInContainerEvent(item, Player, 0, 0));

        public void Open()
        {
            Assert.True(rig.Click(Button));
            Frames(14);
            Assert.Equal(7u, Host.CurrentPanel);
        }

        public void Frames(int n)
        {
            for (var i = 0; i < n; i++)
            {
                rig.Frame();
            }
        }

        public void Dispose() => rig.Dispose();
    }

    private static CreateObjectMsg Item(uint guid, string name, uint icon, uint itemType, uint valid)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
        return basis with { Weenie = basis.Weenie with { IconId = icon, ItemType = itemType, ContainerId = Player, ValidLocations = valid } };
    }

    private Game Enter(bool equip, bool figure = false)
    {
        IInboundMessageHandler? handler = null;
        var views = new RecordingViewHost();
        var rig = new LoginRig(fixture, h =>
        {
            handler = h;
            StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true, view3D: figure, viewHost: figure ? views : null);
        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        var screen = Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        var game = new Game(rig, screen, handler!, views);
        game.Handler.OnPlayerCreate(game.Context, new PlayerCreateMsg(Player));
        game.Create(ReducerHarness.Create(Player, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD), state: PhysicsState.Hidden | PhysicsState.Gravity, name: "Tester"));
        game.Create(Item(Wand, "Wand", 0x060011CF, 0x01000000, Held));
        game.Create(Item(Cap, "Leather Cap", 0x060011D1, 0x2, HeadWear));
        game.Create(Item(Coat, "Leather Coat", 0x060011D2, 0x2, CoatLocations));
        game.Create(Item(Leggings, "Leather Leggings", 0x060011D3, 0x2, LegLocations));
        game.Create(Item(Gauntlets, "Leather Gauntlets", 0x060011D4, 0x2, HandWear));
        game.Create(Item(Boots, "Leather Boots", 0x060011D0, 0x2, FootWear));
        if (equip)
        {
            Dress(game);
        }

        game.Frames(2);
        return game;
    }

    /// <summary>BSD Mage's kit: the wand held and the five leather pieces worn where they cover.</summary>
    private static void Dress(Game g)
    {
        g.Wield(Wand, Held);
        g.Wield(Cap, HeadWear);
        g.Wield(Coat, CoatLocations);
        g.Wield(Leggings, LegLocations);
        g.Wield(Gauntlets, HandWear);
        g.Wield(Boots, FootWear);
    }

    private static void DoubleClick(Game g, UiItem cell)
    {
        var box = cell.ScreenBox;
        g.Rig.Manager.Input.MouseMove((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
        g.Rig.Manager.Input.MouseButton(UiMouseButton.Left, down: true, doubleClick: true);
        g.Rig.Manager.Input.MouseButton(UiMouseButton.Left, down: false);
    }

    // ------------------------------------------------------------------ the slot table

    [Fact]
    public void The_slot_table_is_gmPaperDollUIs_element_to_location_table()
    {
        Assert.Equal(24, GamePaperDoll.SlotTable.Count);
        Assert.Equal(24, GamePaperDoll.SlotTable.Select(static s => s.ElementId).Distinct().Count());
        Assert.Equal(9, GamePaperDoll.SlotTable.Count(static s => s.Armour));

        void Expect(uint element, uint mask, int side)
        {
            Assert.True(GamePaperDoll.GetLocationInfoFromElementId(element, out var location, out var s));
            Assert.Equal((mask, side), (location, s));
        }

        Expect(0x100001DA, 0x8000, 0); // neck
        Expect(0x100001DB, 0x10000, 1); // left wrist
        Expect(0x100001DC, 0x40000, 1); // left ring
        Expect(0x100001DD, 0x20000, 2); // right wrist
        Expect(0x100001DE, 0x80000, 2); // right ring
        Expect(0x100001DF, 0x3500000, 0); // the weapon slot takes melee, missile, held and two-handed
        Expect(0x100001E0, 0x800000, 0); // ammunition
        Expect(0x100001E1, 0x200000, 0); // shield
        Expect(0x100001E2, 0x2, 0); // shirt
        Expect(0x100001E3, 0x40, 0); // pants
        Expect(0x1000058E, 0x4000000, 0); // trinket
        Expect(0x100005E9, 0x8000000, 0); // cloak
        Expect(0x10000595, 0x10000000, 0);
        Expect(0x10000596, 0x20000000, 0);
        Expect(0x10000597, 0x40000000, 0);
        Expect(0x100005AB, 0x1, 0); // head
        Expect(0x100005AC, 0x200, 0); // chest armour
        Expect(0x100005AD, 0x400, 0);
        Expect(0x100005AE, 0x800, 0);
        Expect(0x100005AF, 0x1000, 0);
        Expect(0x100005B0, 0x20, 0); // hand
        Expect(0x100005B1, 0x2000, 0);
        Expect(0x100005B2, 0x4000, 0);
        Expect(0x100005B3, 0x100, 0); // foot
        Assert.False(GamePaperDoll.GetLocationInfoFromElementId(0x100001D5, out var none, out var noSide));
        Assert.Equal((0u, 0), (none, noSide));
    }

    // ------------------------------------------------------------------ the page

    [RequiresLocalDatsFact]
    public void Every_slot_of_the_table_is_a_one_cell_item_list_of_the_layout_and_empty_slots_keep_the_layouts_art()
    {
        using var g = Enter(equip: false);
        var doll = g.Doll;
        g.Open();
        foreach (var slot in GamePaperDoll.SlotTable)
        {
            var list = doll.SlotList(slot.ElementId);
            Assert.NotNull(list);
            Assert.Equal(1, list.FixedSlots);
            Assert.Equal(1, list.CellCount);
            var cell = list.CellAt(0)!;
            Assert.True(cell.IsEmpty, slot.Name);
            Assert.Null(cell.ComposedIcon); // nothing is composed over the layout's own empty-slot art
            Assert.False(cell.IconPending);
            Assert.Equal(0u, doll.ItemIn(slot.ElementId));
        }
    }

    [RequiresLocalDatsFact]
    public void The_wand_and_the_five_leather_pieces_are_in_the_slots_retail_puts_them_in_and_the_rest_stay_empty()
    {
        using var g = Enter(equip: true);
        var doll = g.Doll;
        Assert.Equal(6, g.Inventory.Equipped.Count);
        g.Open();

        var expected = new Dictionary<uint, uint>
        {
            [0x100001DF] = Wand, // the weapon slot, for a held item
            [0x100005AB] = Cap, // head
            [0x100005AC] = Coat, // the coat covers chest, abdomen, upper arm and lower arm armour
            [0x100005AD] = Coat,
            [0x100005AE] = Coat,
            [0x100005AF] = Coat,
            [0x100005B1] = Leggings, // upper and lower leg
            [0x100005B2] = Leggings,
            [0x100005B0] = Gauntlets, // hand
            [0x100005B3] = Boots, // foot
        };
        foreach (var slot in GamePaperDoll.SlotTable)
        {
            Assert.Equal(expected.GetValueOrDefault(slot.ElementId), doll.ItemIn(slot.ElementId));
            var cell = doll.SlotList(slot.ElementId)!.CellAt(0)!;
            Assert.Equal(expected.ContainsKey(slot.ElementId), !cell.IsEmpty);
        }

        // The cells are the item cells of b1: the picture is composed from the object's icon.
        var wand = doll.SlotList(0x100001DF)!.CellAt(0)!;
        g.Frames(4);
        Assert.False(wand.IconPending);
        Assert.NotNull(wand.ComposedIcon);
    }

    [RequiresLocalDatsFact]
    public void The_paperdoll_follows_the_inventory_redrawing_once_when_shown_and_nothing_while_hidden()
    {
        using var g = Enter(equip: false);
        var doll = g.Doll;
        g.Open();
        Assert.Equal(0u, doll.ItemIn(0x100005AB));
        var drawn = doll.Refreshes;

        g.Wield(Cap, HeadWear);
        g.Frames(2);
        Assert.Equal(Cap, doll.ItemIn(0x100005AB));
        Assert.Equal(drawn + 1, doll.Refreshes); // one redraw for the change
        g.Frames(5);
        Assert.Equal(drawn + 1, doll.Refreshes); // idle frames draw nothing

        g.Unwield(Cap);
        g.Frames(2);
        Assert.Equal(0u, doll.ItemIn(0x100005AB)); // the slot is empty again
        Assert.True(doll.SlotList(0x100005AB)!.CellAt(0)!.IsEmpty);

        // Closed: a change draws nothing until the page shows again.
        Assert.True(g.Rig.Click(g.Button));
        g.Frames(4);
        Assert.False(g.Host.IsOpen);
        var hidden = doll.Refreshes;
        g.Wield(Boots, FootWear);
        g.Frames(6);
        Assert.Equal(hidden, doll.Refreshes);
        g.Open();
        Assert.Equal(hidden + 1, doll.Refreshes);
        Assert.Equal(Boots, doll.ItemIn(0x100005B3));
    }

    [RequiresLocalDatsFact]
    public void A_double_click_on_a_worn_item_asks_the_action_api_to_take_it_off_and_an_empty_slot_does_nothing()
    {
        using var g = Enter(equip: true);
        g.Open();
        var session = g.Rig.Session;
        var before = session.ActionLog.Total;
        var weapon = g.Doll.SlotList(0x100001DF)!.CellAt(0)!;
        var shield = g.Doll.SlotList(0x100001E1)!.CellAt(0)!;
        Assert.Equal(Wand, weapon.ItemId);

        DoubleClick(g, shield); // empty
        g.Frames(2);
        Assert.Equal(before, session.ActionLog.Total);

        DoubleClick(g, weapon);
        g.Frames(2);
        var moves = Enumerable.Range(0, session.ActionLog.Count).Select(i => session.ActionLog[i]).Where(static e => e.Kind == ActionKind.MoveToPack).ToArray();
        Assert.Single(moves);
        Assert.Equal(ActionSource.Ui, moves[0].Source);
        Assert.Contains($"item=0x{Wand:X8} container=0x00000000", moves[0].Summary, StringComparison.OrdinalIgnoreCase);

        // The server's answer (GameEvent 0x0022) puts the wand in the pack: the slot empties.
        g.Unwield(Wand);
        g.Frames(2);
        Assert.Equal(0u, g.Doll.ItemIn(0x100001DF));
        Assert.Contains(g.Inventory.Pack, e => e.Guid == Wand);
    }

    [RequiresLocalDatsFact]
    public void The_slots_checkbox_swaps_the_armour_slots_and_the_figure_and_a_host_without_a_figure_view_opens_the_page_on_the_slots()
    {
        using var g = Enter(equip: true);
        var doll = g.Doll;
        g.Open();
        Assert.True(GamePaperDoll.FigureBuilt);
        Assert.False(doll.FigureServed); // the rig has no view data or view host: the slots view, loud (TODO(m3-paperdoll-figure))
        Assert.True(doll.SlotsViewShown);
        Assert.True(doll.Checkbox!.Highlighted);
        Assert.True(doll.SlotList(0x100005AB)!.Visible);
        Assert.False(doll.Viewport!.Visible);
        Assert.False(doll.ClickMask!.Visible);
        Assert.True(doll.SlotList(0x100001DF)!.Visible); // the weapon slot is always there

        Assert.True(g.Rig.Click(doll.Checkbox));
        g.Frames(2);
        Assert.False(doll.SlotsViewShown);
        Assert.False(doll.Checkbox.Highlighted);
        Assert.False(doll.SlotList(0x100005AB)!.Visible); // PostInit's hidden armour slots
        Assert.True(doll.Viewport.Visible);
        Assert.True(doll.ClickMask.Visible);
        Assert.Equal(Cap, doll.ItemIn(0x100005AB)); // hidden, but they hold the worn items

        Assert.True(g.Rig.Click(doll.Checkbox));
        g.Frames(2);
        Assert.True(doll.SlotsViewShown);
        Assert.True(doll.SlotList(0x100005AB)!.Visible);
    }

    [RequiresLocalDatsFact]
    public void The_controllers_construction_is_one_small_unit_under_eight_milliseconds()
    {
        using var g = Enter(equip: false);
        var panels = g.Screen.PanelControllers!;
        GamePaperDoll.Create(panels.Context, panels.Host)!.Dispose(); // warm
        var watch = Stopwatch.StartNew();
        var doll = GamePaperDoll.Create(panels.Context, panels.Host);
        watch.Stop();
        Assert.NotNull(doll);
        doll.Dispose();
        Assert.True(watch.Elapsed.TotalMilliseconds < 8.0, $"construction took {watch.Elapsed.TotalMilliseconds:0.00} ms");
    }

    // ------------------------------------------------------------------ the figure (b4)

    [Fact]
    public void The_figure_camera_is_UpdateForRaces_per_heritage_and_PostInits_for_the_others()
    {
        Assert.Equal(new System.Numerics.Vector3(0.12f, -2.4f, 0.88f), PaperDollFigure.DefaultCamera); // PostInit 0x004A56D0
        Assert.Equal(PaperDollFigure.DefaultCamera, PaperDollFigure.CameraFor(1)); // Aluvian (BSD Mage): UpdateForRace has no case
        foreach (var heritage in new[] { 2, 3, 4, 5, 10, 11, 0 })
        {
            Assert.Equal(PaperDollFigure.DefaultCamera, PaperDollFigure.CameraFor(heritage));
        }

        Assert.Equal(new System.Numerics.Vector3(0.12f, -3.0f, 0.88f), PaperDollFigure.CameraFor(6));
        Assert.Equal(new System.Numerics.Vector3(0.12f, -3.0f, 0.88f), PaperDollFigure.CameraFor(7));
        Assert.Equal(new System.Numerics.Vector3(0.12f, -3.4f, 1.0f), PaperDollFigure.CameraFor(8));
        foreach (var heritage in new[] { 9, 12, 13 })
        {
            Assert.Equal(new System.Numerics.Vector3(0.12f, -3.4f, 0.88f), PaperDollFigure.CameraFor(heritage));
        }

        Assert.Equal(191.3679f, PaperDollFigure.FigureHeading);
    }

    [RequiresLocalDatsFact]
    public void The_page_opens_on_the_figure_with_the_nine_armour_slots_hidden_and_the_figure_is_the_players_body_at_retails_heading_and_camera()
    {
        using var g = Enter(equip: true, figure: true);
        var doll = g.Doll;
        Assert.Empty(g.Views.AttachedFigures); // nothing is made before the page shows
        g.Open();

        Assert.True(doll.FigureServed);
        var (viewport, served) = Assert.Single(g.Views.AttachedFigures);
        Assert.Equal(GamePaperDoll.ViewportId, viewport);
        var figure = Assert.IsType<PaperDollFigure>(served);
        Assert.Same(doll.Figure, figure);

        // The view: retail's PostInit state (the nine armour slots hidden, the viewport and the click mask shown, the checkbox unchecked).
        Assert.False(doll.SlotsViewShown);
        Assert.True(doll.Viewport!.Visible);
        Assert.True(doll.ClickMask!.Visible);
        Assert.False(doll.Checkbox!.Highlighted);
        Assert.False(doll.SlotList(0x100005AB)!.Visible);
        Assert.True(doll.SlotList(0x100001DF)!.Visible); // the weapon slot is always there
        Assert.Equal(Cap, doll.ItemIn(0x100005AB)); // hidden, but they hold the worn items

        // The figure: the player's Setup, heading 191.3679, the idle pose, the camera (no heritage in the stand-in's qualities: PostInit's).
        var preview = Assert.IsType<CreaturePreview>(figure.Preview);
        Assert.Equal(0x02000001u, preview.Setup.Id);
        Assert.Equal(191.3679f, preview.Heading);
        Assert.Equal(PaperDollFigure.DefaultCamera, figure.CameraPosition);
        Assert.Equal(System.Numerics.Vector3.Zero, figure.CameraDirection);
        Assert.Equal(0u, figure.BackdropSetupId);
        Assert.True(preview.Draws.Length > 0);
        Assert.Contains(preview.Draws.ToArray(), static d => d.GfxObjId != 0);
        Assert.Equal(1, figure.Builds);

        // The checkbox swaps the views both ways, as before.
        Assert.True(g.Rig.Click(doll.Checkbox));
        g.Frames(2);
        Assert.True(doll.SlotsViewShown);
        Assert.False(doll.Viewport.Visible);
        Assert.True(doll.SlotList(0x100005AB)!.Visible);
        Assert.True(g.Rig.Click(doll.Checkbox));
        g.Frames(2);
        Assert.False(doll.SlotsViewShown);
        Assert.True(doll.Viewport.Visible);
        Assert.Equal(1, figure.Builds); // toggling the views does not rebuild
    }

    [RequiresLocalDatsFact]
    public void The_figure_is_rebuilt_when_the_players_appearance_changes_and_not_on_an_unrelated_event_nor_while_the_page_is_hidden()
    {
        using var g = Enter(equip: false, figure: true);
        g.Open();
        var figure = g.Doll.Figure!;
        var preview = figure.Preview!;
        Assert.Equal(1, figure.Builds);
        var draws = preview.Draws.ToArray();
        var version = figure.Version;

        // Idle frames and events that are not about the player's appearance: nothing is rebuilt.
        g.Frames(6);
        g.Create(Item(0x80000299, "Pebble", 0x060011D0, 0x80, 0)); // a new item in the pack
        g.Frames(2);
        Assert.Equal(1, figure.Builds);
        Assert.Equal(version, figure.Version);

        // The server's ObjDescEvent for the player: part 1 takes the gfx object of another part; the figure is dressed again, on the same preview.
        var other = draws.First(d => d.PartIndex != 1 && d.GfxObjId != 0 && d.GfxObjId != draws[1].GfxObjId).GfxObjId;
        g.ChangePlayerDesc(1, other);
        g.Frames(2);
        Assert.Equal(2, figure.Builds);
        Assert.Same(preview, figure.Preview);
        Assert.Equal(other, figure.Preview!.Draws[1].GfxObjId);
        Assert.NotEqual(version, figure.Version);
        Assert.Single(g.Views.AttachedFigures); // the same figure; not attached again

        // Putting a piece on re-dresses it too (the server follows with the ObjDescEvent; the same appearance draws as itself).
        g.Wield(Cap, HeadWear);
        g.Frames(2);
        Assert.Equal(3, figure.Builds);
        g.Frames(6);
        Assert.Equal(3, figure.Builds);

        // Hidden: a change rebuilds nothing until the page shows again.
        Assert.True(g.Rig.Click(g.Button));
        g.Frames(4);
        Assert.False(g.Host.IsOpen);
        g.ChangePlayerDesc(1, draws[1].GfxObjId);
        g.Frames(6);
        Assert.Equal(3, figure.Builds);
        g.Open();
        Assert.Equal(4, figure.Builds);
        Assert.Equal(draws[1].GfxObjId, figure.Preview!.Draws[1].GfxObjId);
    }
}
