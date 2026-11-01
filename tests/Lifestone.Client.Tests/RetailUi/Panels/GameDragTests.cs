using System.Collections.Immutable;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Toolbar;
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

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The minimal drag (M4 2.2 s3, D4; <see cref="UiDrag"/>) over the real gameplay layout: a press on an item cell arms it, the pointer moving a few pixels starts it, the item is carried to a pack, a side pack, the container window, a paperdoll slot or the
/// world view and moves through the Action API only (<see cref="ActionSource.Ui"/>); anything else cancels and sends nothing. Pins OpenAC #206 (the pressed item is the dragged one), #146 (the hover highlight), #221 (a pack move moves that pack) and #219
/// (the toolbar holds no item to leave). Every test drives the real input router with mouse events, as the Godot feed does.
/// </summary>
public sealed class GameDragTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint InventoryButton = 0x100001B1;
    private const uint Player = 0x50000001;
    private const uint Sword = 0x80000101;
    private const uint Pyreal = 0x80000102;
    private const uint Wand = 0x80000103;
    private const uint Pack = 0x80000110;
    private const uint Gem = 0x80000111;
    private const uint Corpse = 0x80000201;
    private const uint Bowl = 0x80000301;
    private const uint Smock = 0x80000302;
    private const uint MeleeWeapon = 0x00100000;
    private const uint WeaponSlot = 0x100001DF;
    private const uint ShortcutBarId = 0x1000001B;

    private sealed class Game(LoginRig rig, GamePlayScreen screen, IInboundMessageHandler handler) : IDisposable
    {
        private uint _event;

        public LoginRig Rig => rig;

        public GamePlayScreen Screen => screen;

        public InboundContext Context => new(0, MessageQueue.SmartBox, 0);

        public IInboundMessageHandler Handler => handler;

        public GameInventoryPage Page => Assert.Single(screen.PanelControllers!.ControllersOf(7).OfType<GameInventoryPage>());

        public GamePaperDoll Doll => Assert.Single(screen.PanelControllers!.ControllersOf(7).OfType<GamePaperDoll>());

        public GameContainerPanel Window => Assert.IsType<GameContainerPanel>(screen.Root!.GetChildRecursive(GameContainerPanel.WindowId));

        public UiInputRouter Input => rig.Manager.Input;

        public UiDrag Drag => rig.Manager.Input.Drag;

        public ActionApi Actions => Assert.IsType<ActionApi>(rig.Session.Actions);

        public IGameSessionView Session => rig.Session;

        public void Create(CreateObjectMsg message) => handler.OnCreateObject(Context, message, isUpdate: false);

        public void Put(uint item, uint container, uint placement) =>
            handler.OnInventoryPutObjInContainer(Context, new GameEventHeader(Player, ++_event, 0x0022), new InventoryPutObjInContainerEvent(item, container, placement, 0));

        public void View(uint container, params (uint Guid, uint Type)[] entries) =>
            handler.OnViewContents(Context, new GameEventHeader(Player, ++_event, 0x0196), new ViewContentsEvent(container, [.. entries.Select(static e => new ViewContentsEntry(e.Guid, e.Type))]));

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

        /// <summary>The next handle id: a probe, so the actions sent between two probes are the difference less one.</summary>
        public long Mark() => Actions.NextId();

        public long SentSince(long mark) => Actions.NextId() - mark - 1;

        public static (int X, int Y) Center(UiElement element)
        {
            var box = element.ScreenBox;
            return ((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
        }

        public void MoveTo((int X, int Y) point) => Input.MouseMove(point.X, point.Y);

        public void MoveTo(UiElement element) => MoveTo(Center(element));

        public void Down() => Input.MouseButton(UiMouseButton.Left, down: true);

        public void Up() => Input.MouseButton(UiMouseButton.Left, down: false);

        /// <summary>Presses on <paramref name="source"/>, walks the pointer to <paramref name="target"/> in steps and releases there.</summary>
        public void DragTo(UiElement source, (int X, int Y) target)
        {
            var from = Center(source);
            MoveTo(from);
            Down();
            MoveTo((from.X + 3, from.Y)); // under the threshold
            MoveTo((from.X + 10, from.Y + 2));
            MoveTo(target);
            Up();
            Frames(1);
        }

        public void DragTo(UiElement source, UiElement target) => DragTo(source, Center(target));

        public void Click(UiElement element)
        {
            MoveTo(element);
            Down();
            Up();
        }

        public void Dispose() => rig.Dispose();
    }

    private static CreateObjectMsg Item(uint guid, string name, uint icon, uint itemType, uint? container = Player, ushort? stack = null, uint? valid = null)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
        return basis with { Weenie = basis.Weenie with { IconId = icon, ItemType = itemType, ContainerId = container, StackSize = stack, ValidLocations = valid } };
    }

    /// <summary>The player with a pack of a sword, a stack of 250 pyreals, a wand and a side pack holding a gem; a corpse on the ground holding a bowl and a smock; the inventory page open.</summary>
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
        g.Create(ReducerHarness.Create(Player, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD), state: PhysicsState.Hidden | PhysicsState.Gravity, name: "Tester"));
        g.Create(Item(Sword, "Sword", 0x060011D1, MeleeWeapon, valid: MeleeWeapon));
        g.Create(Item(Pyreal, "Pyreal", 0x060011D3, 0x40, stack: 250));
        g.Create(Item(Wand, "Wand", 0x060011CF, 0x01000000, valid: 0x01000000));
        g.Create(Item(Pack, "Pack", 0x060011D2, 0x200));
        g.Create(Item(Gem, "Gem", 0x060011D0, 0x8, container: Pack));
        g.Create(Item(Corpse, "Corpse of Tusker", 0x060011D2, 0x200, container: null));
        handler.OnPlayerDescription(
            g.Context,
            new GameEventHeader(Player, 1, 0x0013),
            new PlayerDescriptionMsg
            {
                IntProperties = ImmutableDictionary<uint, int>.Empty.Add(PlayerInventory.EncumbranceProperty, 720),
                Attributes = [new AttributeDesc(1, 0, 100, 0)],
                Inventory = [new ContentDesc(Sword, 0), new ContentDesc(Pyreal, 0), new ContentDesc(Pack, 1), new ContentDesc(Wand, 0)],
            });
        g.Put(Gem, Pack, 0);
        g.View(Corpse, (Bowl, 0), (Smock, 0));
        g.Create(Item(Bowl, "Bowl", 0x060011D0, 0, container: Corpse));
        g.Create(Item(Smock, "Smock", 0x060011D1, 0x2, container: Corpse));
        g.Frames(2);
        g.Open();
        g.Frames(4);
        return g;
    }

    private static UiItem Cell(UiItemList list, uint item) => list.FindItem(item) ?? throw new InvalidOperationException($"no cell shows 0x{item:X8}");

    // ------------------------------------------------------------------ the state machine

    [RequiresLocalDatsFact]
    public void The_drag_icon_is_the_pressed_cells_composed_icon_with_its_underlay_overlay_type_and_effects()
    {
        // M4 4.1 v4 (m4-drag-icon): the carried icon is a cell of the source list's template shown with the pressed cell's own fields, so it is composed by the same path (IconData::RenderIcons 0x0058DFB0) as the pack's.
        using var g = Enter();
        var sword = Cell(g.Page.PackList, Sword);
        Assert.True(sword.TryGetIcon(out var pressed));
        var from = Game.Center(sword);
        g.MoveTo(from);
        g.Down();
        g.MoveTo((from.X + 40, from.Y + 30));
        g.Frames(3);
        var icon = Assert.IsType<UiItem>(g.Drag.Icon);
        Assert.True(icon.TryGetIcon(out var carried));
        Assert.Equal(pressed.IconId, carried.IconId);
        Assert.Equal(pressed.UnderlayId, carried.UnderlayId);
        Assert.Equal(pressed.OverlayId, carried.OverlayId);
        Assert.Equal(pressed.ItemType, carried.ItemType);
        Assert.Equal(pressed.UiEffects, carried.UiEffects);
        Assert.False(icon.IconPending);
        Assert.NotNull(icon.ComposedIcon);
        Assert.NotNull(icon.IconElement!.GeneratedImage);
        Assert.True(g.Input.KeyDown(DikScanCodes.Escape));
        g.Up();
    }

    [RequiresLocalDatsFact]
    public void A_press_that_moves_a_few_pixels_carries_the_pressed_item_with_its_icon_at_the_pointer_and_escape_cancels_and_sends_nothing()
    {
        using var g = Enter();
        var sword = Cell(g.Page.PackList, Sword);
        var mark = g.Mark();
        var from = Game.Center(sword);
        g.MoveTo(from);
        g.Down();
        Assert.Equal(UiDragPhase.Pressed, g.Drag.Phase);
        Assert.Equal(Sword, g.Drag.ItemId);
        g.MoveTo((from.X + UiDrag.StartDistancePixels - 1, from.Y));
        Assert.Equal(UiDragPhase.Pressed, g.Drag.Phase); // under the threshold it is still a click
        Assert.Null(g.Input.DragElement);

        g.MoveTo((from.X + 40, from.Y + 30));
        g.Frames(2);
        Assert.Equal(UiDragPhase.Dragging, g.Drag.Phase);
        Assert.Same(sword, g.Input.DragElement); // UiField's rollover reads it
        var icon = Assert.IsType<UiItem>(g.Drag.Icon);
        Assert.True(icon.Visible);
        Assert.Equal(Sword, icon.ItemId); // the carried item's own icon
        var box = icon.ScreenBox;
        Assert.True(box.X0 <= from.X + 40 && box.X1 >= from.X + 40 && box.Y0 <= from.Y + 30 && box.Y1 >= from.Y + 30, "the icon is under the pointer");
        Assert.NotSame(icon, g.Rig.Manager.HitTest(from.X + 40, from.Y + 30)); // the icon lets the pointer through

        Assert.True(g.Input.KeyDown(DikScanCodes.Escape));
        Assert.Equal(UiDragPhase.Idle, g.Drag.Phase);
        Assert.Null(g.Input.DragElement);
        Assert.False(icon.Visible);
        Assert.Equal(UiDragEnd.Cancelled, g.Drag.LastEnd);
        g.Up(); // the release after the cancel is nothing
        g.Frames(2);
        Assert.Equal(0, g.SentSince(mark));
        Assert.Equal(0, g.Drag.Dropped);
    }

    [RequiresLocalDatsFact]
    public void A_press_released_before_the_distance_is_a_click_that_selects_and_starts_no_drag_and_the_source_cell_takes_the_next_press()
    {
        using var g = Enter();
        var sword = Cell(g.Page.PackList, Sword);
        var mark = g.Mark();
        g.MoveTo(sword);
        g.Down();
        g.MoveTo((Game.Center(sword).X + 1, Game.Center(sword).Y));
        g.Up();
        Assert.Equal(Sword, g.Page.PackList.SelectedItemId);
        Assert.Equal(0, g.Drag.Started);
        Assert.Equal(UiDragPhase.Idle, g.Drag.Phase);

        // A drag that ends on its own cell leaves the cell able to take a click again (its press is forgotten).
        var from = Game.Center(sword);
        g.Down();
        g.MoveTo((from.X + 20, from.Y));
        g.MoveTo(from);
        g.Up();
        Assert.Equal(UiDragEnd.Cancelled, g.Drag.LastEnd); // released on the source cell
        g.Click(sword);
        Assert.Equal(Sword, g.Page.PackList.SelectedItemId);
        Assert.Equal(0, g.SentSince(mark));
    }

    [RequiresLocalDatsFact]
    public void A_release_over_a_non_target_cancels_and_sends_nothing()
    {
        using var g = Enter();
        var mark = g.Mark();
        var button = g.Screen.Root!.GetChildRecursive(InventoryButton)!; // a toolbar button: UI that takes no item
        Assert.True(button.Visible);
        g.DragTo(Cell(g.Page.PackList, Sword), button);
        Assert.Equal(UiDragEnd.Cancelled, g.Drag.LastEnd);
        Assert.Equal(0, g.SentSince(mark));
        Assert.Equal(UiDragPhase.Idle, g.Drag.Phase);
        Assert.True(g.Drag.Cancelled + g.Drag.Refused + g.Drag.Dropped >= 1);
    }

    [RequiresLocalDatsFact]
    public void Carrying_an_item_over_the_targets_allocates_nothing_per_pointer_move()
    {
        using var g = Enter();
        var from = Game.Center(Cell(g.Page.PackList, Sword));
        var stops = new[]
        {
            Game.Center(g.Page.SidePackList.CellAt(0)!),
            Game.Center(g.Page.TopContainerList.CellAt(0)!),
            Game.Center(Cell(g.Page.PackList, Wand)),
            Game.Center(g.Doll.SlotList(WeaponSlot)!.CellAt(0)!),
            Game.Center(g.Window.ContainerCell!.CellAt(0)!),
            (X: 120, Y: 120), // the world view
        };
        g.MoveTo(from);
        g.Down();
        g.MoveTo((from.X + 12, from.Y));
        Assert.Equal(UiDragPhase.Dragging, g.Drag.Phase);
        g.Frames(2);
        for (var warm = 0; warm < 3; warm++)
        {
            foreach (var stop in stops)
            {
                g.MoveTo(stop); // warm: JIT and first-use caches
            }
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var round = 0; round < 40; round++)
        {
            foreach (var stop in stops)
            {
                g.MoveTo(stop);
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        g.Input.KeyDown(DikScanCodes.Escape);
        g.Up();
        Assert.True(allocated <= 4096, $"{allocated} bytes allocated by {40 * stops.Length} pointer moves while carrying an item");
    }

    [RequiresLocalDatsFact]
    public void A_release_outside_the_window_cancels_instead_of_dropping_on_the_world_view_and_a_lost_focus_cancels_too()
    {
        using var g = Enter();
        var mark = g.Mark();
        var from = Game.Center(Cell(g.Page.PackList, Sword));
        g.MoveTo(from);
        g.Down();
        g.MoveTo((from.X + 12, from.Y));
        Assert.Equal(UiDragPhase.Dragging, g.Drag.Phase);
        g.Input.MouseLeave();
        g.Up();
        Assert.Equal(0, g.SentSince(mark));
        Assert.Equal((UiDragPhase.Idle, UiDragEnd.Cancelled), (g.Drag.Phase, g.Drag.LastEnd));

        var mark2 = g.Mark(); // each SentSince call takes an id itself: a second count needs a second mark
        g.MoveTo(from);
        g.Down();
        g.MoveTo((from.X + 12, from.Y));
        Assert.Equal(UiDragPhase.Dragging, g.Drag.Phase);
        g.Input.SetClientActive(false); // the window lost the focus
        Assert.Equal(UiDragPhase.Idle, g.Drag.Phase);
        g.Input.SetClientActive(true);
        g.Up();
        Assert.Equal(0, g.SentSince(mark2));
        Assert.False(g.Drag.Icon!.Visible);
    }

    // ------------------------------------------------------------------ #206

    [RequiresLocalDatsFact]
    public void OpenAC_206_a_quick_press_and_drag_carries_the_item_that_was_pressed_even_when_the_first_move_event_is_already_over_another_cell()
    {
        using var g = Enter();
        var sword = Cell(g.Page.PackList, Sword);
        var side = g.Page.SidePackList.CellAt(0)!;
        Assert.Equal(Pack, side.ItemId);
        var mark = g.Mark();
        g.MoveTo(sword);
        g.Down();
        g.MoveTo(Game.Center(side)); // the first move event after the press lands on the side pack: no step over the sword's own cell
        Assert.Equal(Sword, g.Drag.ItemId);
        g.Up();
        g.Frames(1);
        Assert.Equal(1, g.SentSince(mark));
        var move = Assert.IsType<ActionHandle>(g.Page.SidePackDrop.LastAction);
        Assert.Equal((ActionKind.MoveToPack, ActionSource.Ui), (move.Kind, move.Source));
        Assert.Equal($"MoveToPack item=0x{Sword:X8} container=0x{Pack:X8} placement=0", move.Summary, ignoreCase: true);
    }

    [RequiresLocalDatsFact]
    public void OpenAC_206_the_item_is_fixed_at_mouse_down_even_when_the_pressed_cell_shows_another_item_by_the_time_the_drag_begins()
    {
        using var g = Enter();
        var sword = Cell(g.Page.PackList, Sword);
        var side = g.Page.SidePackList.CellAt(0)!;
        var mark = g.Mark();
        var from = Game.Center(sword);
        g.MoveTo(from);
        g.Down();
        Assert.True(g.Session.State.Objects.TryGet(Wand, out var wand));
        sword.ShowItem(Wand, GameInventoryPage.IconOf(wand)); // a redraw puts another item in the pressed cell
        g.MoveTo((from.X + 12, from.Y));
        g.MoveTo(Game.Center(side));
        g.Up();
        g.Frames(1);
        Assert.Equal(1, g.SentSince(mark));
        Assert.Contains($"item=0x{Sword:X8}", g.Page.SidePackDrop.LastAction!.Summary, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ #146

    [RequiresLocalDatsFact]
    public void OpenAC_146_a_side_pack_cell_under_a_dragged_item_shows_the_accept_state_and_loses_it_when_the_pointer_leaves_or_the_drag_ends()
    {
        using var g = Enter();
        var side = g.Page.SidePackList.CellAt(0)!;
        var sword = Cell(g.Page.PackList, Sword);
        var from = Game.Center(sword);
        var iconState = side.IconElement!.State;
        g.MoveTo(from);
        g.Down();
        g.MoveTo((from.X + 12, from.Y));
        Assert.Equal(UiDragRollover.None, side.DragRollover);
        g.MoveTo(side);
        Assert.Same(side, g.Drag.HoverCell);
        Assert.Equal(UiDragRollover.Accept, side.DragRollover);
        Assert.NotNull(side.DragAcceptFrame); // the cell template's drag-accept frame (0x1000045A) is the art for the accept
        Assert.True(side.DragAcceptFrame.Visible);
        Assert.False(side.Desc.States.ContainsKey(UiStateIds.DragRolloverAccept)); // the template has no rollover states of its own: the frame is the whole answer

        g.MoveTo((from.X, from.Y + 5)); // back over the pack list
        Assert.Equal(UiDragRollover.None, side.DragRollover);
        Assert.False(side.DragAcceptFrame!.Visible);
        g.MoveTo(side);
        Assert.Equal(UiDragRollover.Accept, side.DragRollover);
        g.Input.KeyDown(DikScanCodes.Escape); // the drag ends: the highlight goes with it
        Assert.Equal(UiDragRollover.None, side.DragRollover);
        Assert.Null(g.Drag.HoverCell);
        Assert.Equal(iconState, side.IconElement!.State); // the cell keeps its picture's state
    }

    [RequiresLocalDatsFact]
    public void OpenAC_146_the_players_own_cell_and_the_container_windows_own_cell_show_the_answer_too_and_a_pack_over_itself_shows_the_reject()
    {
        using var g = Enter();
        g.Frames(2);
        var own = g.Page.TopContainerList.CellAt(0)!;
        var sword = Cell(g.Page.PackList, Sword);
        var from = Game.Center(sword);
        g.MoveTo(from);
        g.Down();
        g.MoveTo((from.X + 12, from.Y));
        g.MoveTo(own);
        Assert.Same(own, g.Drag.HoverCell);
        Assert.Equal(UiDragRollover.Accept, own.DragRollover); // a pack cell takes the item
        g.MoveTo((from.X, from.Y + 5));
        Assert.Equal(UiDragRollover.None, own.DragRollover);

        var window = g.Window;
        Assert.True(window.Visible);
        var windowCell = window.ContainerCell!.CellAt(0)!;
        Assert.Equal(Corpse, windowCell.ItemId);
        g.MoveTo(windowCell);
        Assert.Same(windowCell, g.Drag.HoverCell);
        Assert.Equal(UiDragRollover.Accept, windowCell.DragRollover); // a put into the open container
        g.MoveTo((from.X, from.Y + 5));
        Assert.Equal(UiDragRollover.None, windowCell.DragRollover);
        g.Up();

        // The side pack over its own cell: the source cell shows nothing (the release cancels); over the main pack's cell it is a move of that pack.
        var side = g.Page.SidePackList.CellAt(0)!;
        var sideCenter = Game.Center(side);
        g.MoveTo(sideCenter);
        g.Down();
        g.MoveTo((sideCenter.X + 12, sideCenter.Y));
        g.MoveTo(sideCenter);
        Assert.Equal(UiDragRollover.None, side.DragRollover);
        g.Up();
    }

    [RequiresLocalDatsFact]
    public void OpenAC_146_a_paperdoll_slot_shows_accept_for_an_item_it_holds_and_reject_for_one_it_does_not()
    {
        using var g = Enter();
        var slot = g.Doll.SlotList(WeaponSlot)!;
        var slotCell = slot.CellAt(0)!;
        var sword = Cell(g.Page.PackList, Sword);
        var pyreal = Cell(g.Page.PackList, Pyreal);
        var from = Game.Center(sword);
        g.MoveTo(from);
        g.Down();
        g.MoveTo((from.X + 12, from.Y));
        g.MoveTo(slotCell);
        Assert.Same(slotCell, g.Drag.HoverCell);
        Assert.Equal(UiDragRollover.Accept, slotCell.DragRollover);
        Assert.True(slotCell.DragAcceptFrame!.Visible); // an empty slot shows the frame too
        g.Input.KeyDown(DikScanCodes.Escape);
        g.Up();

        var from2 = Game.Center(pyreal);
        g.MoveTo(from2);
        g.Down();
        g.MoveTo((from2.X + 12, from2.Y));
        g.MoveTo(slotCell);
        Assert.Equal(UiDragRollover.Reject, slotCell.DragRollover); // a stack of pyreals cannot be wielded
        Assert.False(slotCell.DragAcceptFrame!.Visible);
        g.Up();
        g.Frames(1);
        Assert.Equal(UiDragEnd.Refused, g.Drag.LastEnd);
    }

    // ------------------------------------------------------------------ the drops

    [RequiresLocalDatsFact]
    public void A_held_item_dropped_on_a_side_pack_moves_into_that_pack_and_a_pack_list_item_on_the_players_cell_goes_to_the_main_pack()
    {
        using var g = Enter();
        var mark = g.Mark();
        g.DragTo(Cell(g.Page.PackList, Wand), g.Page.SidePackList.CellAt(0)!);
        Assert.Equal(1, g.SentSince(mark));
        Assert.Equal($"MoveToPack item=0x{Wand:X8} container=0x{Pack:X8} placement=0", g.Page.SidePackDrop.LastAction!.Summary, ignoreCase: true);

        // The side pack's contents are listed (a click on its cell): its gem dragged onto the player's own cell goes to the main pack.
        g.Page.ShowContents(Pack);
        g.Frames(2);
        var mark2 = g.Mark();
        g.DragTo(Cell(g.Page.PackList, Gem), g.Page.TopContainerList.CellAt(0)!);
        Assert.Equal(1, g.SentSince(mark2));
        var move = Assert.IsType<ActionHandle>(g.Page.OwnerDrop.LastAction);
        Assert.Equal((ActionKind.MoveToPack, ActionSource.Ui), (move.Kind, move.Source));
        Assert.Equal($"MoveToPack item=0x{Gem:X8} container=0x{Player:X8} placement=0", move.Summary, ignoreCase: true);
    }

    [RequiresLocalDatsFact]
    public void An_item_dragged_out_of_the_container_window_is_picked_up_into_the_pack_or_a_side_pack_and_one_already_in_the_window_is_refused()
    {
        using var g = Enter();
        var window = g.Window;
        Assert.Equal([Bowl, Smock], window.ShownEntries);
        var mark = g.Mark();
        g.DragTo(Cell(window.ItemRow!, Bowl), g.Page.SidePackList.CellAt(0)!);
        Assert.Equal(1, g.SentSince(mark));
        var pick = Assert.IsType<ActionHandle>(g.Page.SidePackDrop.LastAction);
        Assert.Equal((ActionKind.PickUp, ActionSource.Ui), (pick.Kind, pick.Source));
        Assert.Equal($"PickUp item=0x{Bowl:X8} container=0x{Pack:X8} placement=0", pick.Summary, ignoreCase: true);

        var mark2 = g.Mark();
        var packCell = Cell(g.Page.PackList, Sword);
        g.DragTo(Cell(window.ItemRow!, Smock), packCell);
        Assert.Equal(1, g.SentSince(mark2));
        Assert.Contains($"PickUp item=0x{Smock:X8} container=0x{Player:X8}", g.Page.PackDrop.LastAction!.Summary, StringComparison.OrdinalIgnoreCase);

        // Dropped back on the window it came from: nothing is sent.
        var mark3 = g.Mark();
        g.DragTo(Cell(window.ItemRow!, Bowl), window.ContainerCell!.CellAt(0)!);
        Assert.Equal(0, g.SentSince(mark3));
        Assert.Equal(UiDragEnd.Refused, g.Drag.LastEnd);
    }

    [RequiresLocalDatsFact]
    public void A_held_item_dropped_on_the_container_window_goes_into_the_open_container()
    {
        using var g = Enter();
        var window = g.Window;
        var mark = g.Mark();
        g.DragTo(Cell(g.Page.PackList, Sword), window.ContainerCell!.CellAt(0)!);
        Assert.Equal(1, g.SentSince(mark));
        var move = Assert.IsType<ActionHandle>(window.ContainerCellDrop!.LastAction);
        Assert.Equal((ActionKind.MoveToPack, ActionSource.Ui), (move.Kind, move.Source));
        Assert.Equal($"MoveToPack item=0x{Sword:X8} container=0x{Corpse:X8} placement=0", move.Summary, ignoreCase: true);

        var mark2 = g.Mark();
        g.DragTo(Cell(g.Page.PackList, Wand), Cell(window.ItemRow!, Bowl));
        Assert.Equal(1, g.SentSince(mark2));
        Assert.Equal($"MoveToPack item=0x{Wand:X8} container=0x{Corpse:X8} placement=0", window.ItemRowDrop!.LastAction!.Summary, ignoreCase: true);
    }

    [RequiresLocalDatsFact]
    public void An_item_dropped_on_the_world_view_is_dropped_to_the_ground_if_held_and_an_item_on_the_corpse_is_refused()
    {
        using var g = Enter();
        var world = (X: 120, Y: 120);
        Assert.True(g.Screen.PanelControllers!.WorldDrop is not null);
        var mark = g.Mark();
        g.DragTo(Cell(g.Page.PackList, Sword), world);
        Assert.Equal(1, g.SentSince(mark));
        var drop = Assert.IsType<ActionHandle>(g.Screen.PanelControllers.WorldDrop!.LastAction);
        Assert.Equal((ActionKind.Drop, ActionSource.Ui), (drop.Kind, drop.Source));
        Assert.Equal($"Drop item=0x{Sword:X8}", drop.Summary, ignoreCase: true);

        var mark2 = g.Mark();
        g.DragTo(Cell(g.Window.ItemRow!, Bowl), world);
        Assert.Equal(0, g.SentSince(mark2)); // the bowl is not carried: it is already in the world
        Assert.Equal(UiDragEnd.Refused, g.Drag.LastEnd);
    }

    [RequiresLocalDatsFact]
    public void A_pack_dropped_on_itself_or_on_its_own_contents_is_refused_locally_with_a_log_line()
    {
        using var g = Enter();
        var side = g.Page.SidePackList.CellAt(0)!;
        g.Page.ShowContents(Pack); // the pack list now shows the side pack's contents
        g.Frames(2);
        Assert.Equal(Pack, g.Page.OpenContainer);
        var mark = g.Mark();
        var before = g.Rig.Log.Entries.Count;
        g.DragTo(side, Cell(g.Page.PackList, Gem)); // on the pack's own contents
        Assert.Equal(0, g.SentSince(mark));
        Assert.Equal(UiDragEnd.Refused, g.Drag.LastEnd);
        Assert.Contains(g.Rig.Log.Entries.Skip(before), static e => e.Message.Contains("cannot be put in itself", StringComparison.Ordinal));

        // The verdicts, directly: a pack in itself, a pack in a container that lies inside it, an ordinary move.
        Assert.Equal(DropVerdict.Self, GameDrops.CheckMove(g.Session, Pack, Pack));
        Assert.Equal(DropVerdict.OwnContents, GameDrops.CheckMove(g.Session, Pack, Gem));
        Assert.Equal(DropVerdict.Ok, GameDrops.CheckMove(g.Session, Sword, Pack));
        Assert.Equal(DropVerdict.NoTarget, GameDrops.CheckMove(g.Session, Sword, 0));
    }

    [RequiresLocalDatsFact]
    public void M4_2_2_fix_R4_a_release_over_an_empty_part_of_a_panel_cancels_and_sends_nothing_while_one_over_the_world_still_drops()
    {
        using var g = Enter();
        var mark = g.Mark();
        var pack = g.Page.PackList.ScreenBox;
        var panel = g.Screen.Panel!.ScreenBox;
        var empty = (X: pack.X0 + 3, Y: pack.Y1 + 4); // inside the panel host's window, below the pack list: no element of the page is mouse-visible there
        Assert.True(empty.X >= panel.X0 && empty.X <= panel.X1 && empty.Y >= panel.Y0 && empty.Y <= panel.Y1, "the point lies inside the panel's window");
        Assert.True(g.Screen.SmartBoxViewport!.Equals(g.Rig.Manager.HitTest(empty.X, empty.Y)), "the hit test falls through the page's fields to the full-screen 3D viewport (the world's registered target)");

        g.DragTo(Cell(g.Page.PackList, Sword), empty);
        Assert.Equal(UiDragEnd.Cancelled, g.Drag.LastEnd);
        Assert.Equal(0, g.SentSince(mark));
        Assert.Equal(0, g.Screen.PanelControllers!.WorldDrop!.Drops);
        Assert.Equal(UiDragPhase.Idle, g.Drag.Phase);

        // The world itself (the viewport under no window) still takes the drop.
        var world = (X: 300, Y: 250);
        Assert.True(g.Screen.SmartBoxViewport.Equals(g.Rig.Manager.HitTest(world.X, world.Y)));
        var mark2 = g.Mark();
        g.DragTo(Cell(g.Page.PackList, Sword), world);
        Assert.Equal(1, g.SentSince(mark2));
        Assert.Equal(UiDragEnd.Dropped, g.Drag.LastEnd);
        Assert.Equal(1, g.Screen.PanelControllers.WorldDrop.Drops);
        Assert.Equal($"Drop item=0x{Sword:X8}", g.Screen.PanelControllers.WorldDrop.LastAction!.Summary, ignoreCase: true);

        // Nor does the toolbar's or the container window's own empty area: a window's box is not the world.
        var mark3 = g.Mark();
        var windowBox = g.Window.ScreenBox;
        g.DragTo(Cell(g.Page.PackList, Wand), (windowBox.X0 + 2, windowBox.Y1 - 2));
        Assert.Equal(0, g.SentSince(mark3));
        Assert.NotEqual(UiDragEnd.Dropped, g.Drag.LastEnd);
        Assert.Equal(1, g.Screen.PanelControllers.WorldDrop.Drops);
    }

    [RequiresLocalDatsFact]
    public void M4_2_2_fix_R8_carrying_a_corpse_entry_over_the_container_window_and_the_panels_allocates_nothing_per_pointer_move()
    {
        using var g = Enter();
        var window = g.Window;
        var pack = g.Page.PackList.ScreenBox;
        var from = Game.Center(Cell(window.ItemRow!, Bowl)); // an item that is not held: CheckMove walks the viewed container's entries on every move over a target naming it
        var stops = new[]
        {
            Game.Center(window.ContainerCell!.CellAt(0)!), // the container window's own cell: names the viewed container, the entry loop runs and finds the item
            Game.Center(Cell(window.ItemRow!, Smock)),
            Game.Center(g.Page.SidePackList.CellAt(0)!),
            Game.Center(g.Page.TopContainerList.CellAt(0)!),
            Game.Center(g.Doll.SlotList(WeaponSlot)!.CellAt(0)!),
            (X: pack.X0 + 3, Y: pack.Y1 + 4), // an empty part of the page: the world's predicate runs
            (X: 300, Y: 250), // the world
        };
        g.MoveTo(from);
        g.Down();
        g.MoveTo((from.X + 12, from.Y));
        Assert.Equal(UiDragPhase.Dragging, g.Drag.Phase);
        Assert.Equal(DropVerdict.AlreadyThere, GameDrops.CheckMove(g.Session, Bowl, Corpse)); // the branch under test is the one taken
        g.Frames(2);
        for (var warm = 0; warm < 3; warm++)
        {
            foreach (var stop in stops)
            {
                g.MoveTo(stop);
            }
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var round = 0; round < 40; round++)
        {
            foreach (var stop in stops)
            {
                g.MoveTo(stop);
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        g.Input.KeyDown(DikScanCodes.Escape);
        g.Up();
        Assert.True(allocated <= 4096, $"{allocated} bytes allocated by {40 * stops.Length} pointer moves while carrying a corpse entry");
    }

    [RequiresLocalDatsFact]
    public void M4_2_2_fix_R7_the_placement_guess_and_the_wield_from_a_container_refusal_name_their_TODOs_once_each()
    {
        using var g = Enter();
        var before = g.Rig.Log.Entries.Count;
        var slotCell = g.Doll.SlotList(WeaponSlot)!.CellAt(0)!;
        g.DragTo(Cell(g.Window.ItemRow!, Bowl), slotCell); // an item in the open container onto a paperdoll slot: refused, not carried
        g.DragTo(Cell(g.Window.ItemRow!, Smock), slotCell); // again: the TODO is not repeated
        Assert.Equal(UiDragEnd.Refused, g.Drag.LastEnd);
        g.DragTo(Cell(g.Page.PackList, Wand), g.Page.SidePackList.CellAt(0)!); // a drop that names no slot: placement 0 is a guess
        g.DragTo(Cell(g.Page.PackList, Sword), g.Page.SidePackList.CellAt(0)!);

        var lines = g.Rig.Log.Entries.Skip(before).Select(static e => e.Message).ToList();
        Assert.Single(lines, static l => l.Contains("TODO(m4-drag-wield-from-container)", StringComparison.Ordinal));
        Assert.Single(lines, static l => l.Contains("TODO(m4-drag-placement)", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ #221

    [RequiresLocalDatsFact]
    public void OpenAC_221_dragging_a_side_pack_to_the_main_pack_sends_one_move_for_that_pack_and_none_for_any_other()
    {
        using var g = Enter();
        var mark = g.Mark();

        // The press selects the side pack, which opens it in the pack list (as a click does); the main pack is the player's own cell.
        g.DragTo(g.Page.SidePackList.CellAt(0)!, g.Page.TopContainerList.CellAt(0)!);
        Assert.Equal(1, g.SentSince(mark));
        var move = Assert.IsType<ActionHandle>(g.Page.OwnerDrop.LastAction);
        Assert.Equal(ActionKind.MoveToPack, move.Kind);
        Assert.Equal($"MoveToPack item=0x{Pack:X8} container=0x{Player:X8} placement=0", move.Summary, ignoreCase: true);
        Assert.DoesNotContain($"0x{Sword:X8}", move.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"0x{Gem:X8}", move.Summary, StringComparison.OrdinalIgnoreCase); // the pack's own contents do not move on their own
    }

    [RequiresLocalDatsFact]
    public void OpenAC_221_the_main_packs_own_cell_is_not_draggable()
    {
        using var g = Enter();
        var own = g.Page.TopContainerList.CellAt(0)!;
        Assert.False(g.Page.TopContainerList.DragSource);
        var mark = g.Mark();
        var from = Game.Center(own);
        g.MoveTo(from);
        g.Down();
        Assert.Equal(UiDragPhase.Idle, g.Drag.Phase); // nothing armed
        g.MoveTo((from.X + 30, from.Y));
        g.MoveTo(Game.Center(g.Page.SidePackList.CellAt(0)!));
        g.Up();
        Assert.Equal(0, g.Drag.Started);
        Assert.Equal(0, g.SentSince(mark));
    }

    // ------------------------------------------------------------------ the hotbar (M4 3.2 h2) and #219

    private static GameHotbar Hotbar(Game g) => Assert.IsType<GameHotbar>(g.Screen.ToolbarUi!.Hotbar);

    /// <summary>Puts an item on the model's bar as the tracker does when it sends (the offline rig refuses the sends, so a test applies the result itself).</summary>
    private static void ApplyAdd(Game g, int slot, uint item) => g.Actions.Reducer.ApplyShortcutAdded(slot, item);

    [RequiresLocalDatsFact]
    public void A_pack_item_dragged_onto_a_hotbar_slot_adds_it_with_AddShortcut_and_one_that_is_not_carried_is_refused()
    {
        using var g = Enter();
        var bar = Hotbar(g);
        var target = bar.CellOf(4)!;
        g.DragTo(Cell(g.Page.PackList, Sword), Game.Center(target));
        Assert.Equal(UiDragEnd.Dropped, g.Drag.LastEnd);
        Assert.Equal(1, bar.Sends);
        Assert.Equal($"AddShortcut slot=4 item=0x{Sword:X8}", bar.LastAction!.Summary, ignoreCase: true);
        Assert.Equal(ActionSource.Ui, bar.LastAction.Source);

        // An item that is not carried (the corpse's bowl) is refused by the slot: no send (retail picks it up first: TODO(m4-shortcut-pickup)).
        var sends = bar.Sends;
        g.DragTo(Cell(g.Window.ItemRow!, Bowl), Game.Center(bar.CellOf(5)!));
        Assert.Equal(sends, bar.Sends);
        Assert.Equal(UiDragEnd.Refused, g.Drag.LastEnd);

        // The second item onto the occupied slot (the model has the sword on slot 4): AddShortcut for slot 4 again; h1's rule puts the occupant to the right.
        ApplyAdd(g, 4, Sword);
        g.Frames(2);
        Assert.Equal(Sword, bar.CellOf(4)!.ItemId);
        g.DragTo(Cell(g.Page.PackList, Wand), Game.Center(bar.CellOf(4)!));
        Assert.Equal($"AddShortcut slot=4 item=0x{Wand:X8}", bar.LastAction!.Summary, ignoreCase: true);
    }

    [RequiresLocalDatsFact]
    public void A_hotbar_slot_dragged_onto_another_slot_moves_it_and_onto_an_occupied_slot_swaps_in_retails_order()
    {
        using var g = Enter();
        var bar = Hotbar(g);
        ApplyAdd(g, 2, Sword);
        ApplyAdd(g, 6, Wand);
        g.Frames(2);
        Assert.Equal(Sword, bar.CellOf(2)!.ItemId);
        Assert.Equal(Wand, bar.CellOf(6)!.ItemId);

        g.DragTo(bar.CellOf(2)!, Game.Center(bar.CellOf(4)!)); // a move onto an empty slot: one AddShortcut (h1 sends the RemoveShortcut for the old slot)
        Assert.Equal(UiDragEnd.Dropped, g.Drag.LastEnd);
        Assert.Equal(1, bar.Sends);
        Assert.Equal($"AddShortcut slot=4 item=0x{Sword:X8}", bar.LastAction!.Summary, ignoreCase: true);

        var before = bar.Sends;
        g.DragTo(bar.CellOf(2)!, Game.Center(bar.CellOf(6)!)); // the offline rig refused the send, so the model still has the sword on slot 2: onto the wand's slot
        Assert.Equal(before + 4, bar.Sends);
        var swap = bar.RecentSends.TakeLast(4).Select(static h => h.Summary.ToLowerInvariant()).ToArray();
        Assert.Equal(
            ["removeshortcut slot=2", "removeshortcut slot=6", $"addshortcut slot=6 item=0x{Sword:X8}".ToLowerInvariant(), $"addshortcut slot=2 item=0x{Wand:X8}".ToLowerInvariant()],
            swap); // R(from), R(slot), A(slot, item), A(from, occupant)

        var again = bar.Sends;
        g.DragTo(bar.CellOf(2)!, Game.Center(bar.CellOf(2)!)); // released on its own cell: nothing
        Assert.Equal(again, bar.Sends);
        Assert.Equal(UiDragEnd.Cancelled, g.Drag.LastEnd);
    }

    [RequiresLocalDatsFact]
    public void A_hotbar_slot_dragged_off_the_bar_is_removed_whatever_is_under_it_and_never_moves_or_drops_the_item()
    {
        using var g = Enter();
        var bar = Hotbar(g);
        var offBar = new (string Where, (int X, int Y) Point)[]
        {
            ("the world view", (120, 120)),
            ("the pack list", Game.Center(Cell(g.Page.PackList, Pyreal))),
            ("a side pack", Game.Center(g.Page.SidePackList.CellAt(0)!)),
            ("a toolbar button", Game.Center(g.Screen.Root!.GetChildRecursive(InventoryButton)!)),
        };
        var mark = g.Mark();
        for (var i = 0; i < offBar.Length; i++)
        {
            ApplyAdd(g, 1, Sword); // the offline rig refuses the remove, so the slot is filled again for the next drag
            g.Frames(2);
            var sendsNow = bar.Sends;
            g.DragTo(bar.CellOf(1)!, offBar[i].Point);
            Assert.Equal(sendsNow + 1, bar.Sends);
            Assert.Equal("RemoveShortcut slot=1", bar.LastAction!.Summary, ignoreCase: true);
            Assert.Equal(UiDragEnd.Dropped, g.Drag.LastEnd);
        }

        // Nothing else was sent: no Drop on the world, no move of the item.
        Assert.Equal(0, g.Screen.PanelControllers!.WorldDrop!.Drops);
        Assert.Equal(0, g.Page.PackDrop.Drops);
        Assert.Equal(0, g.Page.SidePackDrop.Drops);
        Assert.Equal(offBar.Length, g.SentSince(mark)); // the four removes, no other action
    }

    [RequiresLocalDatsFact]
    public void A_hotbar_drag_cancelled_by_escape_keeps_the_shortcut_and_an_empty_slot_is_no_drag_source()
    {
        using var g = Enter();
        var bar = Hotbar(g);
        ApplyAdd(g, 1, Sword);
        g.Frames(2);
        var from = Game.Center(bar.CellOf(1)!);
        g.MoveTo(from);
        g.Down();
        g.MoveTo((from.X + 12, from.Y - 3));
        Assert.Equal(UiDragPhase.Dragging, g.Drag.Phase);
        Assert.True(g.Input.KeyDown(DikScanCodes.Escape));
        g.Up();
        Assert.Equal(0, bar.Sends);
        Assert.Equal(Sword, bar.CellOf(1)!.ItemId);

        var empty = Game.Center(bar.CellOf(7)!); // an empty slot: nothing to carry
        g.MoveTo(empty);
        g.Down();
        g.MoveTo((empty.X + 12, empty.Y - 3));
        Assert.Equal(UiDragPhase.Idle, g.Drag.Phase);
        g.Up();
        Assert.Equal(0, bar.Sends);
    }

    [RequiresLocalDatsFact]
    public void OpenAC_219_an_item_that_leaves_the_inventory_leaves_the_hotbar_and_the_slots_are_drag_sources_and_targets()
    {
        using var g = Enter();
        var bar = Hotbar(g);
        var owned = new HashSet<UiElement>();
        foreach (var controller in g.Screen.PanelControllers!.ControllersOf(7))
        {
            if (controller is GameInventoryPage page)
            {
                owned.Add(page.PackList);
                owned.Add(page.SidePackList);
                owned.Add(page.TopContainerList);
            }
        }

        var lists = new List<UiItemList>();
        Collect(g.Screen.Root!, lists);
        var outside = lists.Where(l => !owned.Contains(l) && !InsidePanels(l)).ToList();
        Assert.NotEmpty(lists);
        var shortcuts = outside.Where(static l => l.Parent?.ElementId == ShortcutBarId).ToList();
        Assert.Equal(18, shortcuts.Count); // the toolbar's 18 shortcut slots: now a drag source and a drop target each (M4 3.2 h2; M3 had them inert)
        foreach (var list in shortcuts)
        {
            Assert.True(list.DragSource, $"list 0x{list.ElementId:X8} is a drag source");
            Assert.True(g.Drag.IsTarget(list), $"list 0x{list.ElementId:X8} takes drops");
        }

        // M5 6 s1: the spellbook's list and the spell bars' lists carry spells (no item), so every item target refuses them; the bars also take spell drops (SpellBarEditTests).
        var spellLists = outside.Where(static l => l.ElementId is GameSpellbookPage.ListId or GameSpellcastingPanel.ListId).ToList();
        Assert.Contains(spellLists, static l => l.ElementId == GameSpellbookPage.ListId);
        foreach (var list in spellLists)
        {
            Assert.True(list.DragSource, $"spell list 0x{list.ElementId:X8} is a drag source");
            Assert.Equal(list.ElementId == GameSpellcastingPanel.ListId, g.Drag.IsTarget(list));
            Assert.Equal(0, ItemCells(list));
        }

        foreach (var list in outside.Except(shortcuts).Except(spellLists))
        {
            Assert.False(list.DragSource, $"list 0x{list.ElementId:X8} is a drag source");
            Assert.False(g.Drag.IsTarget(list), $"list 0x{list.ElementId:X8} takes drops");
            Assert.Equal(0, ItemCells(list));
        }

        // An item on the bar that leaves the inventory (a drop to the ground) leaves its slot: h1's model drops the row, the panel shows an empty cell.
        ApplyAdd(g, 2, Sword);
        ApplyAdd(g, 3, Wand);
        g.Frames(2);
        Assert.Equal(Sword, bar.CellOf(2)!.ItemId);
        g.Handler.OnInventoryPutObjectIn3D(g.Context, new GameEventHeader(Player, 90, 0x019A), new InventoryPutObjectIn3DEvent(Sword));
        g.Frames(3);
        Assert.True(bar.CellOf(2)!.IsEmpty);
        Assert.Equal(Wand, bar.CellOf(3)!.ItemId); // the other row stays
        foreach (var list in outside.Except(shortcuts))
        {
            Assert.Null(list.FindItem(Sword));
        }

        Assert.All(shortcuts, static l => Assert.Null(l.FindItem(Sword)));
    }

    private static bool InsidePanels(UiElement element)
    {
        for (var e = element; e is not null; e = e.Parent)
        {
            if (e is GameContainerPanel or GameVendorPanel || e.ElementId is 0x100001CD or 0x10000015 or 0x10000061 or 0x100006B5)
            {
                return true; // the container window, the vendor window (M4 3.2 s4b: its shop list is a drag source and a sell/buy target), the paperdoll, the spellcasting window (spell shortcuts) and its floaty
            }
        }

        return false;
    }

    private static int ItemCells(UiItemList list)
    {
        var n = 0;
        for (var i = 0; i < list.CellCount; i++)
        {
            if (list.CellAt(i) is { IsEmpty: false, ItemId: not 0 })
            {
                n++;
            }
        }

        return n;
    }

    private static void Collect(UiElement root, List<UiItemList> into)
    {
        if (root is UiItemList list)
        {
            into.Add(list);
        }

        foreach (var child in root.Children)
        {
            Collect(child, into);
        }
    }
}
