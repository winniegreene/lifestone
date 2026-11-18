using System.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
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
/// The container window (M4 2.2 s2; <c>gmExternalContainerUI</c> 0x1000000D, window 0x1000005D in <c>gmFloatyEnvPanelUI</c>) over the real gameplay layout and the container model, fed in the order ACE sends it (cache/m4/2-1/capture.md C2: the
/// 0196, then the items' F745s): it opens on <see cref="ContainerOpened"/>, lists the resolved entries in the view's order, redraws on a change, closes on the server's 0052 without sending anything, and acts only through the Action API
/// (the close button closes, a double click picks up).
/// </summary>
public sealed class GameContainerPanelTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint Player = 0x50000001;
    private const uint Corpse = 0x80000201;
    private const uint Chest = 0x80000202;
    private const uint Bowl = 0x80000301;
    private const uint Smock = 0x80000302;
    private const uint Gem = 0x80000303;
    private const uint Mace = 0x80000304;
    private const uint Sack = 0x80000305;
    private const uint Satchel = 0x80000306;
    private const uint Focus = 0x80000307;
    private const uint EnvPanelId = 0x100005FD;
    private const uint VendorWindowId = 0x10000062;

    private sealed class Game(LoginRig rig, GamePlayScreen screen, IInboundMessageHandler handler) : IDisposable
    {
        private uint _event;

        public LoginRig Rig => rig;

        public InboundContext Context => new(0, MessageQueue.SmartBox, 0);

        public GameContainerPanel Window => Assert.IsType<GameContainerPanel>(screen.Root!.GetChildRecursive(GameContainerPanel.WindowId));

        public UiElement Env => screen.Root!.GetChildRecursive(EnvPanelId)!;

        public UiFloatyCombatPanel CombatFloaty => Assert.IsType<UiFloatyCombatPanel>(screen.Root!.GetChildRecursive(GameCombatPanel.WindowId)!.Parent);

        public UiElement CombatWindow => screen.Root!.GetChildRecursive(GameCombatPanel.WindowId)!;

        private byte _modeSequence;

        // ACE's answer to a mode change (0x02CD, property 40): an offline session refuses the request itself, so the test plays the server (as GameCombatPanelTests does).
        public void ChangeMode(CombatMode mode)
        {
            Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(Context, AceWireWriters.PrivateUpdatePropertyInt(++_modeSequence, CombatModes.Property, (int)mode), handler).Outcome);
            rig.Frame();
        }

        public GamePanels Panels => screen.PanelControllers!;

        public UiButton CloseButton => Assert.IsAssignableFrom<UiButton>(Window.GetChildRecursive(GameContainerPanel.CloseButtonId));

        public ActionLog Log => rig.Session.ActionLog;

        public void Create(CreateObjectMsg message) => handler.OnCreateObject(Context, message, isUpdate: false);

        public void View(uint container, params (uint Guid, uint Type)[] entries) =>
            handler.OnViewContents(Context, new GameEventHeader(Player, ++_event, 0x0196), new ViewContentsEvent(container, [.. entries.Select(static e => new ViewContentsEntry(e.Guid, e.Type))]));

        public void CloseContainer(uint container) =>
            handler.OnCloseGroundContainer(Context, new GameEventHeader(Player, ++_event, 0x0052), new CloseGroundContainerEvent(container));

        public void Put(uint item, uint container, uint placement) =>
            handler.OnInventoryPutObjInContainer(Context, new GameEventHeader(Player, ++_event, 0x0022), new InventoryPutObjInContainerEvent(item, container, placement, 0));

        public void Frames(int n)
        {
            for (var i = 0; i < n; i++)
            {
                rig.Frame();
            }
        }

        public void Dispose() => rig.Dispose();
    }

    private static CreateObjectMsg Thing(uint guid, string name, uint itemType = 0, uint? container = null, uint icon = 0x060011D0)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
        return basis with { Weenie = basis.Weenie with { IconId = icon, ItemType = itemType, ContainerId = container } };
    }

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
        var game = new Game(rig, screen, handler!);

        // The player with a pack of its own (the stand-in's inventory is replaced), and a corpse and a chest on the ground.
        handler!.OnPlayerCreate(game.Context, new PlayerCreateMsg(Player));
        game.Create(ReducerHarness.Create(Player, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD), state: PhysicsState.Hidden | PhysicsState.Gravity, name: "Tester"));
        handler.OnPlayerDescription(game.Context, new GameEventHeader(Player, 1, 0x0013), new PlayerDescriptionMsg { Attributes = [new AttributeDesc(1, 0, 100, 0)], Inventory = [] });
        game.Create(Thing(Corpse, "Corpse of Tusker", itemType: 0x200));
        game.Create(Thing(Chest, "Chest", itemType: 0x200));
        game.Frames(2);
        return game;
    }

    private static uint[] Shown(GameContainerPanel window) => [.. window.ShownEntries];

    [RequiresLocalDatsFact]
    public void The_window_and_the_environment_floaty_are_hidden_until_a_container_opens_and_the_other_windows_of_the_floaty_stay_hidden()
    {
        using var g = Enter();
        var window = g.Window;
        Assert.False(window.Visible);
        Assert.False(g.Env.Visible);
        Assert.IsType<UiFloatyEnvPanel>(g.Env);
        Assert.False(g.Env.GetChildRecursive(VendorWindowId)!.Visible); // the vendor's window is not built (3.2): it is not drawn when the floaty shows
        Assert.Equal(0u, window.ShownContainer);

        g.View(Corpse, (Bowl, 0));
        g.Frames(2);
        Assert.True(window.Visible);
        Assert.True(g.Env.Visible);
        Assert.False(g.Env.GetChildRecursive(VendorWindowId)!.Visible);
        Assert.Equal(Corpse, window.ShownContainer);
    }

    [RequiresLocalDatsFact]
    public void M4_2_2_fix2_R12_opening_the_container_window_hides_the_combat_floaty_in_melee_and_closing_it_brings_it_back_and_in_peace_it_stays_hidden()
    {
        // The environment floaty and the combat floaty share the place 0,400: one shows at a time (retail's host keeps a stack of remembered panels; TODO(m4-floaty-stack-retail)).
        using var g = Enter();
        g.ChangeMode(CombatMode.Melee);
        Assert.True(g.CombatWindow.Visible);
        Assert.True(g.CombatFloaty.Visible);
        Assert.True(g.CombatWindow.IsShown);
        Assert.False(g.Env.Visible);

        g.View(Corpse, (Bowl, 0));
        g.Create(Thing(Bowl, "Bowl", container: Corpse));
        g.Frames(2);
        Assert.True(g.Window.IsShown); // the container window shows
        Assert.True(g.Env.Visible);
        Assert.False(g.CombatFloaty.Visible, "the combat floaty is hidden while the environment floaty shows");
        Assert.False(g.CombatWindow.IsShown, "so the combat window does not show through the container window");
        Assert.True(g.CombatWindow.Visible, "the window itself still wants to show: the combat mode has not changed");
        Assert.Equal(1, g.Rig.Log.Lines.Count(l => l.Contains("TODO(m4-floaty-stack-retail)", StringComparison.Ordinal)));

        g.CloseContainer(Corpse);
        g.Frames(2);
        Assert.False(g.Env.Visible);
        Assert.True(g.CombatFloaty.Visible, "the combat floaty shows again in Melee");
        Assert.True(g.CombatWindow.IsShown);

        // A second open and close: shown again, hidden again, and the TODO is not logged twice.
        g.View(Corpse, (Bowl, 0));
        g.Frames(2);
        Assert.False(g.CombatFloaty.Visible);
        g.CloseContainer(Corpse);
        g.Frames(2);
        Assert.True(g.CombatFloaty.Visible);
        Assert.Equal(1, g.Rig.Log.Lines.Count(l => l.Contains("TODO(m4-floaty-stack-retail)", StringComparison.Ordinal)));

        // In peace the combat window never wants to show, so closing the container does not bring the floaty back.
        g.ChangeMode(CombatMode.NonCombat);
        Assert.False(g.CombatFloaty.Visible);
        g.View(Corpse, (Bowl, 0));
        g.Frames(2);
        Assert.True(g.Env.Visible);
        Assert.False(g.CombatFloaty.Visible);
        g.CloseContainer(Corpse);
        g.Frames(2);
        Assert.False(g.Env.Visible);
        Assert.False(g.CombatFloaty.Visible, "peace: nothing wants the combat floaty");

        // A mode change while the container window shows is held back too, and the floaty follows the mode when the window closes.
        g.View(Corpse, (Bowl, 0));
        g.Frames(2);
        g.ChangeMode(CombatMode.Melee);
        Assert.True(g.CombatWindow.Visible);
        Assert.False(g.CombatFloaty.Visible, "the container window still shows");
        g.CloseContainer(Corpse);
        g.Frames(2);
        Assert.True(g.CombatFloaty.Visible);
    }

    [RequiresLocalDatsFact]
    [Trait("Tier", "Timing")] // m4-timing-tier-candidates: the container panel's zero-byte allocation row
    public void M4_2_2_fix2_R14_DrawState_allocates_nothing_over_a_drawn_window()
    {
        using var g = Enter();
        var window = g.Window;
        g.View(Corpse, (Bowl, 0), (Smock, 0), (Gem, 0));
        g.Create(Thing(Smock, "Smock", container: Corpse));
        g.Create(Thing(Bowl, "Bowl", container: Corpse));
        g.Create(Thing(Gem, "Gem", itemType: 0x8, container: Corpse));
        g.Frames(12);
        Assert.Equal(ContainerWindowDraw.Drawn, window.DrawState);

        for (var warm = 0; warm < 8; warm++)
        {
            _ = window.DrawState;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var last = ContainerWindowDraw.Hidden;
        for (var i = 0; i < 1000; i++)
        {
            last = window.DrawState;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(ContainerWindowDraw.Drawn, last);
        Assert.True(allocated <= 0, $"{allocated} bytes allocated by 1000 DrawState reads over a window of 3 entries (the foreach over IReadOnlyList boxed its enumerator)");
    }

    [RequiresLocalDatsFact]
    public void It_opens_on_ContainerOpened_titles_the_container_and_lists_each_entry_as_its_object_lands_in_the_views_order()
    {
        using var g = Enter();
        var window = g.Window;
        g.View(Corpse, (Bowl, 0), (Smock, 0), (Gem, 0));
        g.Frames(2);

        Assert.Equal("Contents of Corpse of Tusker", window.Title);
        Assert.Equal(Corpse, window.ContainerCell!.CellAt(0)!.ItemId); // the container's own cell
        Assert.Empty(Shown(window)); // the 0196 came first: no entry has an object yet, and nothing is guessed

        g.Create(Thing(Smock, "Smock", container: Corpse));
        g.Frames(1);
        Assert.Equal([Smock], Shown(window)); // an entry whose F745 has not landed is left out
        g.Create(Thing(Bowl, "Bowl", container: Corpse));
        g.Create(Thing(Gem, "Gem", itemType: 0x8, container: Corpse));
        g.Frames(2);
        Assert.Equal([Bowl, Smock, Gem], Shown(window)); // the server's order, not the arrival order
        Assert.True(g.Rig.Session.State.Containers.Viewed!.IsResolved);
        Assert.Equal(3, window.ItemRow!.OccupiedCount);
        Assert.All(window.ItemRow.Items.Cast<UiItem>().Take(3), static c => Assert.False(c.IsEmpty)); // the entries come first
    }

    [RequiresLocalDatsFact]
    public void M4_3_2_e_the_item_row_draws_empty_slot_boxes_after_its_entries_as_far_as_its_width_holds_and_an_empty_container_draws_only_boxes()
    {
        // The cause (M4 3.2 lane e): the item row 0x1000006A is a list that grows (property 0x10000015 of -1, one row: 0x5F of -1); retail pads it with empty slot cells (UIElement_ItemList::UpdateEmptySlots, 11.6096 0x004E4390)
        // and Lifestone made a cell per entry and deleted the rest ("a list that grows keeps no empty cell"), so a corpse drew a plain area under its items.
        using var g = Enter();
        var window = g.Window;
        g.View(Corpse, (Bowl, 0), (Smock, 0), (Gem, 0));
        g.Create(Thing(Bowl, "Bowl", container: Corpse));
        g.Create(Thing(Smock, "Smock", container: Corpse));
        g.Create(Thing(Gem, "Gem", itemType: 0x8, container: Corpse));
        g.Frames(4);

        var row = window.ItemRow!;
        var whole = row.Box.Width / UiItemList.DefaultCellWidth;
        Assert.True(whole >= 10, $"the row is {row.Box.Width} wide");
        Assert.Equal(whole, row.CellCount); // every whole cell the row's width holds is drawn
        Assert.Equal(3, row.OccupiedCount);
        Assert.Equal(whole - 3, row.EmptyCount);
        Assert.Equal([Bowl, Smock, Gem], Shown(window)); // the entries fill from the first cell
        for (var i = 3; i < whole; i++)
        {
            var cell = Assert.IsType<UiItem>(row.CellAt(i));
            Assert.True(cell.IsEmpty);
            Assert.Equal(UiItem.EmptyState, cell.IconElement!.State); // the empty-slot art: the icon child is in the empty state
            Assert.NotEmpty(cell.IconElement.CurrentStateDesc!.Media); // and that state has an image
            Assert.True(cell.IsShown);
        }

        Assert.Equal(ContainerWindowDraw.Drawn, window.DrawState); // the boxes are not entries

        // An empty chest: every cell a box.
        g.View(Chest);
        g.Frames(4);
        Assert.Equal(whole, row.CellCount);
        Assert.Equal(0, row.OccupiedCount);
        Assert.Equal(ContainerWindowDraw.Drawn, window.DrawState);
    }

    [RequiresLocalDatsFact]
    public void M4_3_2_e_a_row_with_more_entries_than_its_width_holds_gets_only_the_one_trailing_slot_and_scrolls()
    {
        using var g = Enter();
        var window = g.Window;
        var entries = Enumerable.Range(0, 30).Select(static i => (Guid: 0x80000500u + (uint)i, Type: 0u)).ToArray();
        g.View(Corpse, entries);
        foreach (var (guid, _) in entries)
        {
            g.Create(Thing(guid, "Item " + guid, container: Corpse));
        }

        g.Frames(6);
        Assert.Equal(30, window.ItemRow!.OccupiedCount);
        Assert.Equal(31, window.ItemRow.CellCount); // no box past the width: the row scrolls; only the one empty slot after the last item that property 0x10000017 asks for
        Assert.Equal(1, window.ItemRow.EmptyCount);
    }

    // ------------------------------------------------------------------ containers inside the container (M4 3.1 p, m4-container-window-subcontainers)

    [RequiresLocalDatsFact]
    public void M4_3_1_p_a_container_inside_the_container_is_listed_in_its_own_row_and_not_in_the_item_row_and_the_row_shows_only_while_there_is_one()
    {
        // A hand-made view, in the order ACE sends it: the 0196 first (the Sack named a container by its ContainerType 1; the Satchel typed 0 but an ItemType Container object; a Focus typed 2 stays with the items), then the F745s.
        using var g = Enter();
        var window = g.Window;
        var sackField = window.GetChildRecursive(GameContainerPanel.SubContainerFieldId)!;
        g.View(Corpse, (Bowl, 0), (Sack, 1), (Satchel, 0), (Focus, 2), (Smock, 0));
        g.Frames(2);
        Assert.False(window.SubContainerRowShown); // no entry has an object: nothing is guessed, and the row stays hidden
        Assert.False(sackField.Visible);

        g.Create(Thing(Bowl, "Bowl", container: Corpse));
        g.Create(Thing(Sack, "Sack", itemType: 0x200, container: Corpse));
        g.Create(Thing(Satchel, "Satchel", itemType: 0x200, container: Corpse));
        g.Create(Thing(Focus, "Focus", itemType: 0x8, container: Corpse));
        g.Create(Thing(Smock, "Smock", container: Corpse));
        g.Frames(12);

        Assert.True(window.SubContainerRowShown);
        Assert.True(sackField.Visible);
        Assert.True(window.SubContainerRow!.Visible);
        Assert.Equal([Sack, Satchel], [.. window.ShownSubContainers]); // the view's order
        Assert.Equal([Bowl, Focus, Smock], Shown(window)); // the item row keeps the items and the focus, and none of the containers
        Assert.Equal(ContainerWindowDraw.Drawn, window.DrawState);
        Assert.False(window.GetChildRecursive(GameContainerPanel.TitleElementId)!.Visible); // the title sits where the row is and gives way (TODO(m4-container-window-subcontainers-title))
        Assert.Equal("Contents of Corpse of Tusker", window.Title);
        Assert.All(window.SubContainerRow.Items.Cast<UiItem>(), static c => Assert.False(c.IsEmpty));

        // Taken out of the container (a 0022 into the player): the row follows, and with the last one gone it hides and the title returns.
        g.Put(Sack, Player, 0);
        g.Frames(2);
        Assert.Equal([Satchel], [.. window.ShownSubContainers]);
        Assert.Equal(ContainerWindowDraw.Drawn, window.DrawState);
        g.Put(Satchel, Player, 1);
        g.Frames(2);
        Assert.False(window.SubContainerRowShown);
        Assert.False(sackField.Visible);
        Assert.True(window.GetChildRecursive(GameContainerPanel.TitleElementId)!.Visible);
        Assert.Equal([Bowl, Focus, Smock], Shown(window));
        Assert.Equal(ContainerWindowDraw.Drawn, window.DrawState);

        // The window closing puts the row away too.
        g.CloseContainer(Corpse);
        g.Frames(2);
        Assert.False(window.SubContainerRowShown);
    }

    [RequiresLocalDatsFact]
    public void M4_3_1_p_the_sub_container_rule_reads_the_0196s_type_or_the_objects_item_type_and_a_focus_is_an_item()
    {
        using var g = Enter();
        g.Create(Thing(Sack, "Sack", itemType: 0x200, container: Corpse));
        g.Create(Thing(Gem, "Gem", itemType: 0x8, container: Corpse));
        var objects = g.Rig.Session.State.Objects;
        Assert.True(objects.TryGet(Sack, out var sack));
        Assert.True(objects.TryGet(Gem, out var gem));
        Assert.True(GameContainerPanel.IsSubContainer(new ContainerViewEntry(Sack, 1, true), sack!)); // ContainerType 1 and the Container item type
        Assert.True(GameContainerPanel.IsSubContainer(new ContainerViewEntry(Sack, 0, true), sack!)); // the object's ItemType alone
        Assert.True(GameContainerPanel.IsSubContainer(new ContainerViewEntry(Gem, 1, true), gem!)); // the 0196's type alone
        Assert.False(GameContainerPanel.IsSubContainer(new ContainerViewEntry(Gem, 0, true), gem!));
        Assert.False(GameContainerPanel.IsSubContainer(new ContainerViewEntry(Gem, 2, true), gem!)); // a focus (ContainerType 2) is listed with the items
        Assert.Throws<ArgumentNullException>(() => GameContainerPanel.IsSubContainer(new ContainerViewEntry(Gem, 0, true), null!));
    }

    [RequiresLocalDatsFact]
    public void M4_3_1_p_a_click_on_a_sub_container_cell_opens_it_through_the_action_api_once_and_the_servers_0196_replaces_the_viewed_container()
    {
        using var g = Enter();
        var window = g.Window;
        g.View(Corpse, (Sack, 1), (Bowl, 0));
        g.Create(Thing(Sack, "Sack", itemType: 0x200, container: Corpse));
        g.Create(Thing(Bowl, "Bowl", container: Corpse));
        g.Frames(12);
        var cell = window.SubContainerRow!.CellAt(0)!;
        Assert.Equal(Sack, cell.ItemId);

        Assert.True(g.Rig.Click(cell));
        g.Frames(2);
        var open = Assert.IsType<ActionHandle>(window.LastSubContainerOpen);
        Assert.Equal((ActionKind.OpenContainer, ActionSource.Ui), (open.Kind, open.Source));
        Assert.Contains($"0x{Sack:X8}", open.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, window.SubContainerOpens);
        Assert.Equal(0, window.PickUps); // a click opens: it does not take the pack
        // The offline session answers the open at once (it has no pack inside a corpse to open), so the in-flight guard of a second click is not reached here; ACE answers over seconds.

        // The server's 0196 for the pack replaces the viewed container: the window redraws for it.
        g.View(Sack, (Gem, 0));
        g.Frames(2);
        Assert.Equal(Sack, window.ShownContainer);
        Assert.Equal("Contents of Sack", window.Title);
        Assert.False(window.SubContainerRowShown); // the Sack holds no container
    }

    [RequiresLocalDatsFact]
    [Trait("Tier", "Timing")] // m4-timing-tier-candidates: the container panel's zero-byte allocation row
    public void M4_3_1_p_DrawState_allocates_nothing_over_a_window_with_a_sub_container_row()
    {
        using var g = Enter();
        var window = g.Window;
        g.View(Corpse, (Bowl, 0), (Sack, 1));
        g.Create(Thing(Bowl, "Bowl", container: Corpse));
        g.Create(Thing(Sack, "Sack", itemType: 0x200, container: Corpse));
        g.Frames(12);
        Assert.Equal(ContainerWindowDraw.Drawn, window.DrawState);
        Assert.True(window.SubContainerRowShown);

        for (var warm = 0; warm < 8; warm++)
        {
            _ = window.DrawState;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var last = ContainerWindowDraw.Hidden;
        for (var i = 0; i < 1000; i++)
        {
            last = window.DrawState;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(ContainerWindowDraw.Drawn, last);
        Assert.True(allocated <= 0, $"{allocated} bytes allocated by 1000 DrawState reads over a window with a sub-container row");
    }

    [RequiresLocalDatsFact]
    public void DrawState_says_what_the_window_waits_for_and_is_Drawn_only_with_every_entry_listed_and_its_icon_drawn()
    {
        // M4 2.2 s4: the hunt's corpse frame and the panel shots wait for Drawn before they save; the states are the first reason that holds.
        using var g = Enter();
        var window = g.Window;
        Assert.Equal(ContainerWindowDraw.Hidden, window.DrawState);

        g.View(Corpse, (Bowl, 0), (Smock, 0));
        g.Frames(2);
        Assert.Equal(ContainerWindowDraw.Unresolved, window.DrawState); // the 0196 came first: neither entry has an object

        g.Create(Thing(Smock, "Smock", container: Corpse));
        g.Frames(2);
        Assert.Equal(ContainerWindowDraw.Unresolved, window.DrawState); // one entry is still missing, so the contents are not all there

        g.Create(Thing(Bowl, "Bowl", container: Corpse));
        g.Frames(12);
        Assert.Equal([Bowl, Smock], Shown(window));
        Assert.Equal(ContainerWindowDraw.Drawn, window.DrawState);
        Assert.All(window.ItemRow!.Items.Cast<UiItem>(), static c => Assert.False(c.IconPending));

        g.Put(Bowl, Player, 0); // an item picked out: the model changed, and the window follows on its own redraw
        g.Frames(2);
        Assert.Equal([Smock], Shown(window));
        Assert.Equal(ContainerWindowDraw.Drawn, window.DrawState);

        g.CloseContainer(Corpse);
        g.Frames(2);
        Assert.Equal(ContainerWindowDraw.Hidden, window.DrawState);
    }

    [RequiresLocalDatsFact]
    public void The_examine_review_frames_wait_out_portal_space_and_a_closed_or_undrawn_window_and_take_the_first_magic_wearable_from_the_list_S6()
    {
        // M5 7 S6 (D136, --examine-review-frames): the world's wait (the doll's, D129) first, then the container window shown and drawn, then the first entry that glows magical and is a weapon, armour, jewel or caster
        // (a scroll glows but has no activation, mana or workmanship). The frames examine it through the list's right-click path.
        using var g = Enter();
        var screen = Assert.IsType<GamePlayScreen>(g.Rig.Flow.CurrentScreen);
        var tried = new HashSet<uint>();
        const uint Wand = 0x80000308;
        const uint Scroll = 0x80000309;

        Assert.Equal("the player to be placed in the world", GameContainerPanel.ExamineReviewFrameWait(screen, playerPlaced: false, tunnelVisible: false, playerMaterialised: true, tried, out _, out _));
        Assert.Equal("the portal tunnel to end", GameContainerPanel.ExamineReviewFrameWait(screen, playerPlaced: true, tunnelVisible: true, playerMaterialised: true, tried, out _, out _));
        Assert.StartsWith("the player to materialise", GameContainerPanel.ExamineReviewFrameWait(screen, playerPlaced: true, tunnelVisible: false, playerMaterialised: false, tried, out _, out _), StringComparison.Ordinal);
        Assert.Equal("the shell to be built and painted", GameContainerPanel.ExamineReviewFrameWait(screen, true, false, true, tried, out _, out _));
        screen.MarkPainted(3, 50.0, timedOut: false); // the host's paint (the offline rig has none)
        Assert.Equal("a container window to show", GameContainerPanel.ExamineReviewFrameWait(screen, true, false, true, tried, out _, out _));

        g.View(Corpse, (Scroll, 0), (Smock, 0), (Wand, 0));
        g.Create(Thing(Smock, "Smock", itemType: 0x4, container: Corpse));
        g.Frames(2);
        Assert.Equal("the container window to be drawn", GameContainerPanel.ExamineReviewFrameWait(screen, true, false, true, tried, out _, out _)); // two entries have no object yet

        var scroll = Thing(Scroll, "Scroll of Frost Bolt", itemType: 0x2000, container: Corpse);
        g.Create(scroll with { Weenie = scroll.Weenie with { UiEffects = 1 } });
        var wand = Thing(Wand, "Ivory Wand", itemType: 0x8000, container: Corpse);
        g.Create(wand with { Weenie = wand.Weenie with { UiEffects = 1 } });
        g.Frames(12);
        Assert.Null(GameContainerPanel.ExamineReviewFrameWait(screen, true, false, true, tried, out var window, out var cell));
        Assert.Same(g.Window, window);
        Assert.Equal(Wand, cell!.ItemId); // the scroll glows too, but is no wearable or caster

        Assert.True(window!.ItemRow!.Examine(cell)); // the right click's own path
        var examine = Assert.IsType<GameExaminePanel>(screen.PanelHost!.Examine);
        Assert.True(examine.IsOpen);
        Assert.Equal(Wand, examine.ShownGuid);
        Assert.Equal("Ivory Wand", examine.Title);
        Assert.Equal(Wand, window.ItemRow.SelectedItemId);
        examine.Close();

        tried.Add(Wand); // an item passed over is not taken again
        Assert.StartsWith("a magic weapon", GameContainerPanel.ExamineReviewFrameWait(screen, true, false, true, tried, out _, out _), StringComparison.Ordinal);

        g.CloseContainer(Corpse);
        g.Frames(2);
        Assert.Equal("a container window to show", GameContainerPanel.ExamineReviewFrameWait(screen, true, false, true, new HashSet<uint>(), out _, out _));
    }

    [Fact]
    public void The_examine_review_frame_needs_the_spells_the_activation_line_the_mana_and_the_workmanship_on_screen_S6()
    {
        string[] full = ["Value: 4,200", "Workmanship: Fine (6)", "Activation requires Arcane Lore: 120", "Spells: Mana Conversion Mastery Other IV, Frost Bolt III", "Spellcraft: 160.", "Mana: 600 / 600."];
        Assert.Null(GameExaminePanel.MissingReviewLines(full));
        Assert.Equal("activation,mana", GameExaminePanel.MissingReviewLines(["Workmanship: Fine (6)", "Spells: Frost Bolt III"]));
        Assert.Equal("spells,activation,mana,workmanship", GameExaminePanel.MissingReviewLines(["Spells: unknown."])); // a failed roll names no spells
    }

    [RequiresLocalDatsFact]
    public void DrawState_reads_a_window_that_has_not_redrawn_yet_as_EntriesDiffer()
    {
        using var g = Enter();
        var window = g.Window;
        g.View(Corpse, (Bowl, 0));
        g.Create(Thing(Bowl, "Bowl", container: Corpse));
        g.Frames(12);
        Assert.Equal(ContainerWindowDraw.Drawn, window.DrawState);

        // The item row's cell is cleared behind the model's back (what a redraw that has not run looks like): the cells are no longer the view's resolved entries.
        window.ItemRow!.CellAt(0)!.Clear();
        Assert.Equal(ContainerWindowDraw.EntriesDiffer, window.DrawState);
        window.Redraw();
        g.Frames(12);
        Assert.Equal(ContainerWindowDraw.Drawn, window.DrawState);
    }

    [RequiresLocalDatsFact]
    public void An_item_picked_out_and_a_second_open_redraw_the_window()
    {
        using var g = Enter();
        var window = g.Window;
        g.View(Corpse, (Bowl, 0), (Smock, 0), (Gem, 0));
        g.Create(Thing(Bowl, "Bowl", container: Corpse));
        g.Create(Thing(Smock, "Smock", container: Corpse));
        g.Create(Thing(Gem, "Gem", itemType: 0x8, container: Corpse));
        g.Frames(2);
        Assert.Equal([Bowl, Smock, Gem], Shown(window));
        var redraws = window.Redraws;

        // The Bowl goes into the player's pack (the 0022 the server sends for a pick-up): the corpse's entry goes, the others shift.
        g.Put(Bowl, Player, 0);
        g.Frames(2);
        Assert.True(window.Redraws > redraws);
        Assert.Equal([Smock, Gem], Shown(window));
        Assert.Equal(2, window.ItemRow!.OccupiedCount); // the cell past the last entry is emptied: it is one of the row's slot boxes now
        Assert.True(window.ItemRow.CellAt(2)!.IsEmpty);

        // A second open replaces the first (ACE closes the earlier container first): the window follows with the chest's name and entries.
        g.View(Chest, (Mace, 0));
        g.Frames(2);
        Assert.True(window.Visible);
        Assert.Equal(Chest, window.ShownContainer);
        Assert.Equal("Contents of Chest", window.Title);
        Assert.Empty(Shown(window));
        g.Create(Thing(Mace, "Mace", container: Chest));
        g.Frames(2);
        Assert.Equal([Mace], Shown(window));
    }

    [RequiresLocalDatsFact]
    public void The_servers_close_hides_the_window_and_the_floaty_and_sends_nothing()
    {
        using var g = Enter();
        var window = g.Window;
        g.View(Corpse, (Bowl, 0));
        g.Create(Thing(Bowl, "Bowl", container: Corpse));
        g.Frames(2);
        Assert.True(g.Env.Visible);
        var total = g.Log.Total;

        g.CloseContainer(Corpse); // GE 0x0052
        g.Frames(2);
        Assert.False(window.Visible);
        Assert.False(g.Env.Visible);
        Assert.Equal(0u, window.ShownContainer);
        Assert.Equal(0u, g.Rig.Session.State.Containers.ViewedGuid);
        Assert.Null(window.LastClose); // closing on the server's word is not a request
        Assert.Equal(0, window.PickUps);
        Assert.Equal(total, g.Log.Total);
    }

    [RequiresLocalDatsFact]
    public void The_close_button_asks_the_server_to_close_through_the_action_api_and_the_window_closes_when_the_server_does()
    {
        using var g = Enter();
        var window = g.Window;
        g.View(Corpse, (Bowl, 0));
        g.Create(Thing(Bowl, "Bowl", container: Corpse));
        g.Frames(2);

        Assert.True(g.Rig.Click(g.CloseButton));
        g.Frames(2);
        var close = Assert.IsType<ActionHandle>(window.LastClose);
        Assert.Equal((ActionKind.CloseContainer, ActionSource.Ui), (close.Kind, close.Source));
        Assert.Contains($"container=0x{Corpse:X8}", close.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.True(window.Visible); // the window closes on the 0052, not on the click

        g.CloseContainer(Corpse);
        g.Frames(2);
        Assert.False(window.Visible);
        Assert.False(g.Env.Visible);
    }

    [RequiresLocalDatsFact]
    public void A_double_click_on_an_entry_asks_to_pick_it_up_into_the_players_pack_through_the_action_api()
    {
        using var g = Enter();
        var window = g.Window;
        g.View(Corpse, (Bowl, 0), (Smock, 0));
        g.Create(Thing(Bowl, "Bowl", container: Corpse));
        g.Create(Thing(Smock, "Smock", container: Corpse));
        g.Frames(2);
        Assert.Equal(0, window.PickUps);

        var cell = window.ItemRow!.CellAt(1)!;
        Assert.Equal(Smock, cell.ItemId);
        var box = cell.ScreenBox;
        g.Rig.Manager.Input.MouseMove((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
        g.Rig.Manager.Input.MouseButton(UiMouseButton.Left, down: true, doubleClick: true);
        g.Rig.Manager.Input.MouseButton(UiMouseButton.Left, down: false);
        g.Frames(2);

        Assert.Equal(1, window.PickUps);
        var pickUp = Assert.IsType<ActionHandle>(window.LastPickUp);
        Assert.Equal((ActionKind.PickUp, ActionSource.Ui), (pickUp.Kind, pickUp.Source));
        Assert.Equal($"PickUp item=0x{Smock:X8} container=0x{Player:X8} placement=0", pickUp.Summary, ignoreCase: true);
        Assert.Equal(Smock, window.ItemRow.SelectedItemId); // the first click of the double click selected it
    }

    [RequiresLocalDatsFact]
    public void A_redraw_makes_a_bounded_number_of_cells_and_finishes_over_the_next_frames()
    {
        using var g = Enter();
        var window = g.Window;
        var entries = Enumerable.Range(0, 60).Select(static i => (Guid: 0x80000400u + (uint)i, Type: 0u)).ToArray();
        g.View(Corpse, entries);
        foreach (var (guid, _) in entries)
        {
            g.Create(Thing(guid, "Item " + guid, container: Corpse));
        }

        g.Frames(2);
        Assert.Equal(60, Shown(window).Length);

        // Hidden, the window is not redrawn; shown again, the first redraw is bounded and the frames finish it.
        window.Close();
        Assert.False(window.Visible);
        window.ItemRow!.FlushItems();
        for (var i = window.ItemRow.CellCount - 1; i >= 0; i--)
        {
            window.ItemRow.DeleteItem(i);
        }

        window.Open();
        Assert.True(window.Visible);
        Assert.InRange(window.ItemRow.CellCount, 1, GameContainerPanel.CellsPerRedraw + 1); // the cells made, and the one trailing empty slot (property 0x10000017)
        g.Frames(6);
        Assert.Equal(60, Shown(window).Length);
        Assert.Equal(entries.Select(static e => e.Guid), Shown(window)); // the order is the view's
    }

    [RequiresLocalDatsFact]
    [Trait("Tier", "Timing")] // m4-timing-tier-candidates: an 8 ms wall-clock bound
    public void Its_attach_is_one_small_unit_under_eight_milliseconds_and_it_registers_no_page_handler()
    {
        using var g = Enter();
        var panels = g.Panels;
        var window = g.Window;
        window.Attach(panels.Context); // warm
        var watch = Stopwatch.StartNew();
        window.Attach(panels.Context);
        watch.Stop();
        Assert.True(watch.Elapsed.TotalMilliseconds < 8.0, $"attach took {watch.Elapsed.TotalMilliseconds:0.00} ms");
        Assert.DoesNotContain(panels.RegisteredPages, static p => p is 0x12u); // a window that is not a host page has no panel id to register under
        Assert.Equal(2, panels.ControllersOf(7).Count); // page 7 keeps its two controllers (the inventory page and the paperdoll): the window adds none
        Assert.Single(window.Children, static c => c.GetType() == typeof(UiTextElement)); // the title is made once (a second Attach makes none)
    }
}
