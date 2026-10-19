using System.Collections.Immutable;
using System.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The item widgets and the inventory page (M3 2.2 slice b1): <see cref="UiItemList"/> and <see cref="UiItem"/> over the real layout (cells made when shown, filled in order, selection, a double click), an item's icon layers
/// and stack count, and the page <see cref="GameInventoryPage"/> over a scripted inventory (the main pack in order, a side pack and its contents, the burden, live on item events, a double click that wields).
/// </summary>
public sealed class GameInventoryPageTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint InventoryButton = 0x100001B1;
    private const uint Player = 0x50000001;
    private const uint Sword = 0x80000101;
    private const uint Pyreal = 0x80000102;
    private const uint Wand = 0x80000103;
    private const uint Pack = 0x80000110;
    private const uint Gem = 0x80000111;
    private const uint Dagger = 0x80000104;
    private const uint MeleeWeapon = 0x00100000;

    private sealed class Game(LoginRig rig, GamePlayScreen screen, IInboundMessageHandler handler) : IDisposable
    {
        private uint _event;

        public LoginRig Rig => rig;

        public GamePlayScreen Screen => screen;

        public IInboundMessageHandler Handler => handler;

        public GameInventoryPage Page => Assert.Single(screen.PanelControllers!.ControllersOf(7).OfType<GameInventoryPage>());

        public GamePanelHost Host => screen.PanelHost!;

        public PlayerInventory Inventory => rig.Session.State.Self.Inventory;

        public InboundContext Context => new(0, MessageQueue.SmartBox, 0);

        public UiButton Button => Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(InventoryButton));

        public void Create(CreateObjectMsg message) => handler.OnCreateObject(Context, message, isUpdate: false);

        public void Put(uint item, uint container, uint placement) =>
            handler.OnInventoryPutObjInContainer(Context, new GameEventHeader(Player, ++_event, 0x0022), new InventoryPutObjInContainerEvent(item, container, placement, 0));

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

    private static CreateObjectMsg Item(uint guid, string name, uint icon, uint itemType, uint? container = Player, ushort? stack = null, uint? valid = null, uint? effects = null, uint? overlay = null, uint? underlay = null, string? plural = null)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
        return basis with
        {
            Weenie = basis.Weenie with
            {
                IconId = icon,
                ItemType = itemType,
                ContainerId = container,
                StackSize = stack,
                ValidLocations = valid,
                UiEffects = effects,
                IconOverlayId = overlay,
                IconUnderlayId = underlay,
                PluralName = plural,
            },
        };
    }

    private Game Enter(bool populate)
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
        var game = new Game(rig, screen, handler!);
        if (populate)
        {
            Populate(game);
        }

        return game;
    }

    /// <summary>The player, a pack of a sword, a stack of 250 pyreals (magical) and a wand, a side pack holding a gem, a strength of 100 and a burden of 720.</summary>
    private static void Populate(Game g)
    {
        g.Handler.OnPlayerCreate(g.Context, new PlayerCreateMsg(Player));
        g.Create(ReducerHarness.Create(Player, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD), state: PhysicsState.Hidden | PhysicsState.Gravity, name: "Tester"));
        g.Create(Item(Sword, "Sword", 0x060011D1, MeleeWeapon, valid: MeleeWeapon));
        g.Create(Item(Pyreal, "Pyreal", 0x060011D3, 0x40, stack: 250, effects: 1, plural: "Pyreals"));
        g.Create(Item(Wand, "Wand", 0x060011CF, 0x01000000, valid: 0x01000000));
        g.Create(Item(Pack, "Pack", 0x060011D2, 0x200, underlay: 0x060011D4));
        g.Create(Item(Gem, "Gem", 0x060011D0, 0x8, container: Pack));
        g.Handler.OnPlayerDescription(
            g.Context,
            new GameEventHeader(Player, 1, 0x0013),
            new PlayerDescriptionMsg
            {
                IntProperties = ImmutableDictionary<uint, int>.Empty.Add(PlayerInventory.EncumbranceProperty, 720),
                Attributes = [new AttributeDesc(1, 0, 100, 0)],
                Inventory = [new ContentDesc(Sword, 0), new ContentDesc(Pyreal, 0), new ContentDesc(Pack, 1), new ContentDesc(Wand, 0)],
            });
        g.Put(Gem, Pack, 0);
        g.Frames(2);
    }

    // ------------------------------------------------------------------ the item list

    [RequiresLocalDatsFact]
    public void The_item_list_makes_its_cells_when_shown_the_first_ones_at_once_and_fills_the_first_empty_cell_in_order()
    {
        using var g = Enter(populate: false);
        var page = g.Page;
        Assert.Equal(0, page.PackList.CellCount); // nothing is made while the page is hidden
        Assert.Equal(102, page.PackList.FixedSlots);
        Assert.Equal(7, page.SidePackList.FixedSlots);
        Assert.True(page.PackList.IsContainerList is false && page.SidePackList.IsContainerList);

        Assert.True(g.Rig.Click(g.Button));
        Assert.InRange(page.PackList.CellCount, UiItemList.InitialCells, 102); // the first at once
        Assert.Equal(1, page.TopContainerList.CellCount);
        g.Frames(14);
        Assert.Equal(102, page.PackList.CellCount); // the rest over frames
        Assert.Equal(7, page.SidePackList.CellCount);
        Assert.All(page.PackList.Items, c => Assert.True(Assert.IsType<UiItem>(c).IsEmpty));

        var list = page.PackList;
        var icon = new UiItemIcon(0x060011D1, 0, 0, MeleeWeapon, 0);
        Assert.Same(list.CellAt(0), list.AddItem(11, icon));
        Assert.Same(list.CellAt(1), list.AddItem(12, icon));
        Assert.Same(list.CellAt(1), list.FindItem(12));
        Assert.Equal(2, list.OccupiedCount);
        list.CellAt(0)!.Clear();
        Assert.Same(list.CellAt(0), list.AddItem(13, icon)); // the hole is the first empty cell again after a flush
        list.FlushItems();
        Assert.Equal(0, list.OccupiedCount);
        Assert.Equal(102, list.CellCount); // the cells stay
        for (uint i = 0; i < 102; i++)
        {
            Assert.NotNull(list.AddItem(100 + i, icon));
        }

        Assert.Null(list.AddItem(999, icon)); // a fixed list that is full
    }

    [RequiresLocalDatsFact]
    public void A_click_selects_a_cell_and_a_double_click_tells_the_owner_and_an_empty_cell_does_nothing()
    {
        using var g = Enter(populate: true);
        g.Open();
        var list = g.Page.PackList;
        var selected = new List<uint>();
        var activated = new List<uint>();
        list.ItemSelected += (_, c) => selected.Add(c.ItemId);
        list.ItemActivated += (_, c) => activated.Add(c.ItemId);
        var first = list.CellAt(0)!;
        var second = list.CellAt(1)!;
        var empty = list.CellAt(10)!;
        Assert.Equal([Sword, Pyreal, Wand], [.. list.Items.Cast<UiItem>().Where(static c => !c.IsEmpty).Select(static c => c.ItemId)]);

        Assert.True(g.Rig.Click(first));
        Assert.Equal([Sword], selected);
        Assert.Equal(Sword, list.SelectedItemId);
        Assert.True(first.IsSelected && first.SelectedFrameShown);
        Assert.True(g.Rig.Click(second));
        Assert.Equal(Pyreal, list.SelectedItemId);
        Assert.False(first.IsSelected);
        Assert.True(second.SelectedFrameShown && !first.SelectedFrameShown);

        g.Rig.Manager.Input.MouseMove(first.ScreenBox.X0 + 5, first.ScreenBox.Y0 + 5);
        g.Rig.Manager.Input.MouseButton(UiMouseButton.Left, down: true, doubleClick: true);
        g.Rig.Manager.Input.MouseButton(UiMouseButton.Left, down: false);
        Assert.Equal([Sword], activated);

        selected.Clear();
        g.Rig.Click(empty);
        Assert.Empty(selected);
        Assert.Equal(Sword, list.SelectedItemId); // the double click selected the sword; the empty cell changed nothing
    }

    // ------------------------------------------------------------------ the item cell

    [RequiresLocalDatsFact]
    public void An_item_cell_composes_the_background_the_underlay_and_the_icon_with_the_overlay_as_retail_does()
    {
        using var g = Enter(populate: false);
        g.Rig.Click(g.Button);
        g.Frames(2);
        var images = g.Rig.Manager.Services.Images!;
        UiImage Image(uint did)
        {
            Assert.Equal(UiImageLookup.Ready, images.TryGetImage(did, out var image));
            return image!;
        }

        const uint iconDid = 0x060011D1;
        const uint underlayDid = 0x060011D2;
        const uint overlayDid = 0x060011D3;
        var itemType = 0x8u; // Misc-like: the background of the lowest set bit plus one (entry 4)
        Assert.True(g.Rig.Manager.Services.EnumMaps!.TryResolve(new EnumGroup(0x10000004), 4, out var backgroundDid));
        var cell = g.Page.PackList.CellAt(0)!;

        // Icon alone: the background copied, the icon over it with three-channel alpha.
        cell.ShowItem(1, new UiItemIcon(iconDid, 0, 0, itemType, 0));
        var expected = new UiSurface(32, 32);
        var front = new UiSurface(32, 32);
        UiBlitter.Blit(Image(iconDid), 0, 0, front, 0, 0, 32, 32, UiBlitMode.SrcCopy, 1f);
        UiBlitter.Blit(Image(backgroundDid), 0, 0, expected, 0, 0, 32, 32, UiBlitMode.SrcCopy, 1f);
        UiBlitter.Blit(new UiImage(1, 32, 32, UiPixelFormat.Argb, front.Pixels), 0, 0, expected, 0, 0, 32, 32, UiBlitMode.ThreeChannelAlpha, 1f);
        Assert.Equal(expected.Pixels, cell.ComposedIcon!.Pixels);
        Assert.Equal((32, 32), (cell.ComposedIcon.Width, cell.ComposedIcon.Height));

        // With an underlay and an overlay: the overlay blends over the icon with four-channel alpha; the underlay goes between the background and the icon.
        cell.ShowItem(1, new UiItemIcon(iconDid, underlayDid, overlayDid, itemType, 0));
        front = new UiSurface(32, 32);
        UiBlitter.Blit(Image(iconDid), 0, 0, front, 0, 0, 32, 32, UiBlitMode.SrcCopy, 1f);
        UiBlitter.Blit(Image(overlayDid), 0, 0, front, 0, 0, 32, 32, UiBlitMode.FourChannelAlpha, 1f);
        expected = new UiSurface(32, 32);
        UiBlitter.Blit(Image(backgroundDid), 0, 0, expected, 0, 0, 32, 32, UiBlitMode.SrcCopy, 1f);
        UiBlitter.Blit(Image(underlayDid), 0, 0, expected, 0, 0, 32, 32, UiBlitMode.ThreeChannelAlpha, 1f);
        UiBlitter.Blit(new UiImage(1, 32, 32, UiPixelFormat.Argb, front.Pixels), 0, 0, expected, 0, 0, 32, 32, UiBlitMode.ThreeChannelAlpha, 1f);
        Assert.Equal(expected.Pixels, cell.ComposedIcon!.Pixels);

        // The same fields again compose nothing; a stack count and a tooltip do not recompose either.
        var compositions = cell.Compositions;
        cell.ShowItem(1, new UiItemIcon(iconDid, underlayDid, overlayDid, itemType, 0, StackSize: 5, Name: "Thing"));
        Assert.Equal(compositions, cell.Compositions);
        cell.Clear();
        Assert.True(cell.IsEmpty);
        Assert.Null(cell.ComposedIcon);
    }

    [Fact]
    public void The_glow_replaces_the_pure_white_pixels_with_the_effect_texture_and_the_enum_entries_follow_the_lowest_set_bit()
    {
        var surface = new UiSurface(2, 1);
        surface.Pixels[0] = surface.Pixels[1] = surface.Pixels[2] = surface.Pixels[3] = 255; // opaque white
        surface.Pixels[4] = 255;
        surface.Pixels[5] = 255;
        surface.Pixels[6] = 254; // nearly white: kept
        surface.Pixels[7] = 255;
        var texture = new UiImage(1, 2, 1, UiPixelFormat.Argb, [10, 20, 30, 40, 50, 60, 70, 80]);
        UiItemIcons.ReplaceWhite(surface, texture);
        Assert.Equal(new byte[] { 10, 20, 30, 40, 255, 255, 254, 255 }, surface.Pixels);

        Assert.Equal(33, UiItemIcons.EntryFor(0));
        Assert.Equal(1, UiItemIcons.EntryFor(1)); // Magical
        Assert.Equal(8, UiItemIcons.EntryFor(0x80));
        Assert.Equal(7, UiItemIcons.EntryFor(0x40));
        Assert.Equal(21, UiItemIcons.EntryFor(0x00100000));
    }

    [RequiresLocalDatsFact]
    public void A_stack_shows_its_count_and_a_single_item_none_and_the_selected_ghosted_and_open_frames_follow_their_setters()
    {
        using var g = Enter(populate: false);
        g.Rig.Click(g.Button);
        g.Frames(2);
        var cell = g.Page.PackList.CellAt(0)!;
        cell.ShowItem(1, new UiItemIcon(0x060011D1, 0, 0, 0x40, 1, StackSize: 250, Name: "Pyreal", PluralName: "Pyreals"));
        Assert.Equal(250, cell.Quantity);
        Assert.True(cell.QuantityText!.Visible);
        Assert.Equal("250", cell.QuantityText.Text);
        Assert.True(cell.QuantityText.IsShown, "the count is not hidden by an ancestor (the template nests it in the selected frame)");
        Assert.False(cell.SelectedFrameShown);
        Assert.Equal("250 Pyreals", cell.TooltipText?.Literal);
        Assert.True(cell.IsMouseVisible);

        cell.ShowItem(1, new UiItemIcon(0x060011D1, 0, 0, 0x40, 1, StackSize: 1, Name: "Pyreal", PluralName: "Pyreals"));
        Assert.Equal(-1, cell.Quantity);
        Assert.False(cell.QuantityText.Visible);
        Assert.Equal("Pyreal", cell.TooltipText?.Literal);

        Assert.False(cell.CapacityBar!.Visible || cell.StructureBar!.Visible); // an ordinary item shows neither bar

        // A container shows how full it is (hidden while empty); an object with a structure shows what is left (hidden while full).
        cell.ShowItem(2, new UiItemIcon(0x060011D1, 0, 0, 0x200, 0, ItemsCapacity: 10, Contained: 5, Structure: 40, MaxStructure: 100));
        Assert.True(cell.CapacityBar.Visible && cell.StructureBar.Visible);
        Assert.Equal(0.5f, cell.CapacityBar.Position, 0.0001f);
        Assert.Equal(0.4f, cell.StructureBar.Position, 0.0001f);
        cell.ShowItem(2, new UiItemIcon(0x060011D1, 0, 0, 0x200, 0, ItemsCapacity: 10, Contained: 0, Structure: 100, MaxStructure: 100));
        Assert.False(cell.CapacityBar.Visible || cell.StructureBar.Visible);
        cell.ShowItem(1, new UiItemIcon(0x060011D1, 0, 0, 0x40, 1, StackSize: 1, Name: "Pyreal", PluralName: "Pyreals"));

        cell.SetSelected(true);
        Assert.True(cell.SelectedFrameShown);
        cell.SetSelectable(false);
        cell.SetSelected(false);
        cell.SetSelected(true);
        Assert.False(cell.IsSelected); // a cell that is not selectable ignores a select
        cell.SetGhosted(true);
        Assert.True(cell.GhostedFrame!.Visible);
        var side = g.Page.SidePackList.CellAt(0)!; // the side-pack template (0x1000033F) has the open-container frame
        side.ShowItem(2, new UiItemIcon(0x060011D1, 0, 0, 0x200, 0));
        side.SetOpenContainer(true);
        Assert.True(side.OpenContainerShown);
        side.Clear();
        Assert.False(side.OpenContainerShown);
        cell.Clear();
        Assert.False(cell.GhostedFrame.Visible || cell.QuantityText.Visible);
        Assert.False(cell.IsMouseVisible);
    }

    [RequiresLocalDatsFact]
    public void A_spell_cell_shows_the_icon_the_owner_names_with_three_channel_alpha_and_the_name()
    {
        using var g = Enter(populate: false);
        g.Rig.Click(g.Button);
        g.Frames(2);
        var cell = g.Page.PackList.CellAt(0)!;
        cell.ShowSpell(1234, 0x060011D1, "Strength Self");
        Assert.Equal((0u, 1234u), (cell.ItemId, cell.SpellId));
        Assert.False(cell.IsEmpty);
        Assert.Null(cell.ComposedIcon);
        Assert.Equal(UiBlitMode.ThreeChannelAlpha, cell.IconElement!.Image.Mode);
        Assert.Equal(0x060011D1u, cell.IconElement.Image.Did);
        cell.Clear();
        Assert.True(cell.IsEmpty);
    }

    // ------------------------------------------------------------------ the page

    [RequiresLocalDatsFact]
    public void The_page_shows_the_main_pack_in_order_with_stack_counts_the_side_pack_the_player_the_title_and_the_burden()
    {
        using var g = Enter(populate: true);
        var page = g.Page;
        Assert.Equal(0, page.Refreshes); // nothing is drawn while the page is hidden
        g.Open();
        Assert.Equal(1, page.Refreshes);

        var pack = page.PackList;
        Assert.Equal([Sword, Pyreal, Wand], g.Inventory.Pack.Select(static e => e.Guid).ToArray());
        Assert.Equal([Sword, Pyreal, Wand], [.. pack.Items.Cast<UiItem>().Where(static c => !c.IsEmpty).Select(static c => c.ItemId)]);
        Assert.Equal(Sword, pack.CellAt(0)!.ItemId);
        Assert.Equal(250, pack.CellAt(1)!.Quantity);
        Assert.Equal("250", pack.CellAt(1)!.QuantityText!.Text);
        Assert.NotNull(pack.CellAt(0)!.ComposedIcon);
        Assert.Equal(Pack, page.SidePackList.CellAt(0)!.ItemId);
        Assert.Equal(1, page.SidePackList.OccupiedCount);
        Assert.Equal(Player, page.TopContainerList.CellAt(0)!.ItemId);
        Assert.Equal("Inventory of Tester", page.Title);
        Assert.Equal("Contents of Backpack", page.ContentsTitle);
        Assert.Equal(Player, page.OpenContainer);

        // 720 against a capacity of 150 x 100 = 15,000: a load of 0.048, a meter of a third of it, 4 per cent.
        Assert.Equal("4%", page.BurdenTextNow);
        Assert.Equal(0.016f, page.BurdenLevel, 0.0005f);

        // The close button of the layout closes the page.
        var close = Assert.IsAssignableFrom<UiButton>(g.Host.ElementFor(7)!.GetChildRecursive(0x100001D2));
        Assert.True(g.Rig.Click(close));
        g.Frames(2);
        Assert.False(g.Host.IsOpen);
    }

    [RequiresLocalDatsFact]
    public void A_click_on_a_side_pack_shows_its_contents_and_a_click_on_the_player_shows_the_main_pack_again()
    {
        using var g = Enter(populate: true);
        g.Open();
        var page = g.Page;
        Assert.True(g.Rig.Click(page.SidePackList.CellAt(0)!));
        g.Frames(2);
        Assert.Equal(Pack, page.OpenContainer);
        Assert.Equal("Contents of Pack", page.ContentsTitle);
        Assert.Equal([Gem], [.. page.PackList.Items.Cast<UiItem>().Where(static c => !c.IsEmpty).Select(static c => c.ItemId)]);
        Assert.Equal(Pack, page.SidePackList.OpenItemId);
        Assert.True(page.SidePackList.CellAt(0)!.OpenContainerShown);

        Assert.True(g.Rig.Click(page.TopContainerList.CellAt(0)!));
        g.Frames(2);
        Assert.Equal(Player, page.OpenContainer);
        Assert.Equal("Contents of Backpack", page.ContentsTitle);
        Assert.Equal(3, page.PackList.OccupiedCount);
        Assert.False(page.SidePackList.CellAt(0)!.OpenContainerShown);
    }

    [RequiresLocalDatsFact]
    public void A_vital_or_option_change_does_not_redraw_the_page_and_a_burden_change_does()
    {
        // M3 2.2 fix B (2.2-R11): the page is marked dirty only for what it shows (the description, property 5, Strength, the registry), as GameIndicators filters its quality changes.
        using var g = Enter(populate: true);
        g.Open();
        var page = g.Page;
        g.Frames(3);
        var drawn = page.Refreshes;
        for (uint tick = 0; tick < 5; tick++)
        {
            g.Handler.OnPrivateUpdate(g.Context, new PrivateUpdateMsg(PrivateUpdateKind.VitalCurrent, 2, 2, 40 + tick, default, default, default)); // a regeneration tick
            g.Frames(2);
        }

        g.Handler.OnPrivateUpdate(g.Context, new PrivateUpdateMsg(PrivateUpdateKind.PropertyInt, 3, 25, 9, default, default, default)); // the level: not shown here
        g.Frames(2);
        Assert.Equal(drawn, page.Refreshes); // nothing the page shows changed: not one redraw

        g.Handler.OnPrivateUpdate(g.Context, new PrivateUpdateMsg(PrivateUpdateKind.PropertyInt, 4, PlayerInventory.EncumbranceProperty, 905, default, default, default)); // the burden: the meter moves
        g.Frames(2);
        Assert.Equal(drawn + 1, page.Refreshes);
    }

    [RequiresLocalDatsFact]
    public void The_page_redraws_once_on_an_inventory_change_while_shown_and_once_when_it_next_shows_if_it_changed_hidden()
    {
        using var g = Enter(populate: true);
        g.Open();
        var page = g.Page;
        var drawn = page.Refreshes;
        g.Frames(5);
        Assert.Equal(drawn, page.Refreshes); // idle: nothing redrawn

        g.Create(Item(Dagger, "Dagger", 0x060011CF, MeleeWeapon, valid: MeleeWeapon));
        g.Put(Dagger, Player, 3);
        g.Frames(3);
        Assert.Equal(drawn + 1, page.Refreshes); // the change drew once, in step with the frame
        Assert.Equal(4, page.PackList.OccupiedCount);
        Assert.Equal(Dagger, g.Inventory.Pack[^1].Guid);

        // Hidden: events only mark it dirty; the next show draws once.
        Assert.True(g.Rig.Click(g.Button));
        g.Frames(2);
        Assert.False(g.Host.IsOpen);
        var hidden = page.Refreshes;
        g.Put(Gem, Player, 4);
        g.Frames(4);
        Assert.Equal(hidden, page.Refreshes);
        g.Open();
        Assert.Equal(hidden + 1, page.Refreshes);
        Assert.Equal(5, page.PackList.OccupiedCount);
    }

    [RequiresLocalDatsFact]
    public void A_double_click_on_a_wearable_pack_item_wields_it_through_the_action_api_and_an_item_with_no_valid_locations_does_nothing()
    {
        using var g = Enter(populate: true);
        g.Open();
        var session = g.Rig.Session;
        var before = session.ActionLog.Total;
        var sword = g.Page.PackList.CellAt(0)!;
        DoubleClick(g, sword);
        g.Frames(2);

        // The wield was asked for by the person (ActionSource.Ui) at the sword's valid locations; the log has it once it completes, whatever the outcome.
        var wielded = Enumerable.Range(0, session.ActionLog.Count).Select(i => session.ActionLog[i]).Where(static e => e.Kind == ActionKind.Wield).ToArray();
        Assert.Single(wielded);
        Assert.Equal(ActionSource.Ui, wielded[0].Source);
        Assert.Contains($"item=0x{Sword:X8} location=0x{MeleeWeapon:X8}", wielded[0].Summary, StringComparison.OrdinalIgnoreCase);

        DoubleClick(g, g.Page.PackList.CellAt(1)!); // the pyreals: no valid locations
        g.Frames(2);
        Assert.Equal(before + 1, session.ActionLog.Total);
    }

    [Fact]
    public void The_held_slot_rule_refuses_a_non_caster_outside_peace_and_nothing_else()
    {
        // CPlayerSystem::AutoWieldIsLegal (0x0055FAD0): Held in the valid locations, not peace, not a caster (ItemType 0x8000).
        Assert.True(GameInventoryPage.HoldRefused(CombatModes.Melee, 0x01000000, 0x10));
        Assert.True(GameInventoryPage.HoldRefused(CombatModes.Magic, 0x01000000 | 0x100000, 0));
        Assert.False(GameInventoryPage.HoldRefused(CombatModes.Melee, 0x01000000, 0x8000)); // a caster
        Assert.False(GameInventoryPage.HoldRefused(CombatModes.NonCombat, 0x01000000, 0x10)); // peace
        Assert.False(GameInventoryPage.HoldRefused(0, 0x01000000, 0x10)); // no mode said yet is peace
        Assert.False(GameInventoryPage.HoldRefused(CombatModes.Melee, 0x100000, 0x10)); // a melee weapon is not held
    }

    [RequiresLocalDatsFact]
    public void A_double_click_wielding_a_held_non_caster_in_combat_says_retails_line_and_sends_nothing_and_in_peace_wields()
    {
        using var g = Enter(populate: true);
        g.Open();
        var lines = new List<string>();
        g.Page.HoldNotice = lines.Add;
        var session = g.Rig.Session;
        var wield = () => Enumerable.Range(0, session.ActionLog.Count).Select(i => session.ActionLog[i]).Count(static e => e.Kind == ActionKind.Wield);

        // ACE's answer to a mode change (0x02CD, property 40): the stand-in's own session would refuse the request, so the test plays the server.
        Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(g.Context, AceWireWriters.PrivateUpdatePropertyInt(1, CombatModes.Property, CombatModes.Melee), g.Handler).Outcome);
        g.Frames(2);
        DoubleClick(g, g.Page.PackList.CellAt(2)!); // the wand: valid location Held, ItemType 0x01000000 (not a caster)
        g.Frames(2);
        Assert.Equal(["Cannot hold Wand while in combat"], lines);
        Assert.Equal(0, wield());

        Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(g.Context, AceWireWriters.PrivateUpdatePropertyInt(2, CombatModes.Property, CombatModes.NonCombat), g.Handler).Outcome);
        g.Frames(2);
        DoubleClick(g, g.Page.PackList.CellAt(2)!);
        g.Frames(2);
        Assert.Single(lines);
        Assert.Equal(1, wield());
    }

    private static void DoubleClick(Game g, UiItem cell)
    {
        var box = cell.ScreenBox;
        g.Rig.Manager.Input.MouseMove((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
        g.Rig.Manager.Input.MouseButton(UiMouseButton.Left, down: true, doubleClick: true);
        g.Rig.Manager.Input.MouseButton(UiMouseButton.Left, down: false);
    }

    [RequiresLocalDatsFact]
    public void The_controllers_construction_is_one_small_unit_under_eight_milliseconds()
    {
        using var g = Enter(populate: false);
        var panels = g.Screen.PanelControllers!;
        GameInventoryPage.Create(panels.Context, panels.Host)!.Dispose(); // warm
        var watch = Stopwatch.StartNew();
        var page = GameInventoryPage.Create(panels.Context, panels.Host);
        watch.Stop();
        Assert.NotNull(page);
        page.Dispose();
        Assert.True(watch.Elapsed.TotalMilliseconds < 8.0, $"construction took {watch.Elapsed.TotalMilliseconds:0.00} ms");
    }
}
