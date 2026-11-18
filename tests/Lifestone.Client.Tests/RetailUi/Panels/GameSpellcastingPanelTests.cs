using System.Collections.Immutable;
using DatReaderWriter.Enums;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The spellcasting window (<c>gmSpellcastingUI</c>, M3 3.2 pb, docs/milestones/m3-plan.md section 4.8) over the real gameplay layout and a scripted spellbook: it shows in Magic mode only, lists each tab's bar in slot order
/// with the SpellTable's icons and names, switches tabs as a click does, selects on a click and casts on a double click or the cast button through the Action API (a self spell at the player, a targeted spell at the selected
/// object, retail's chat line with none), rebuilds only when the bar or the tab changes, waits for the SpellTable, and draws nothing while it is hidden. Everything is scripted on the offline stand-in: no server is contacted
/// (the offline session refuses the actions it is asked for; the tests read what was asked).
/// </summary>
public sealed class GameSpellcastingPanelTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint Player = 0x50000001;
    private const uint Monster = 0x80000042;
    private static readonly InboundContext Context = new(0, MessageQueue.SmartBox, 0);

    private SpellTableData Table => _table ??= SpellTableData.Load(fixture.Dats);

    private SpellTableData? _table;

    private SpellInfo Named(string name) => Table.Spells.Values.First(s => s.Name == name);

    private sealed class Game(LoginRig rig, GamePlayScreen screen, IInboundMessageHandler handler) : IDisposable
    {
        private byte _sequence;
        private uint _event = 1;

        public LoginRig Rig => rig;

        public GamePlayScreen Screen => screen;

        public IInboundMessageHandler Handler => handler;

        public GameSpellcastingPanel Cast => Assert.IsType<GameSpellcastingPanel>(screen.Root!.GetChildRecursive(GameSpellcastingPanel.WindowId));

        public UiFloatyCombatPanel Floaty => Assert.IsType<UiFloatyCombatPanel>(Cast.Parent);

        public List<string> Chat { get; } = [];

        public uint Selected { get; set; }

        // ACE's answer to a mode change (0x02CD, property 40): the offline session refuses the request itself, so the test plays the server.
        public void ChangeMode(CombatMode mode)
        {
            Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(Context, AceWireWriters.PrivateUpdatePropertyInt(++_sequence, CombatModes.Property, (int)mode), handler).Outcome);
            rig.Frame();
        }

        public void Describe(IEnumerable<uint> known, ImmutableArray<ImmutableArray<uint>> bars)
        {
            var book = ImmutableDictionary.CreateBuilder<uint, float>();
            foreach (var id in known)
            {
                book[id] = 2f;
            }

            handler.OnPlayerDescription(Context, new GameEventHeader(Player, ++_event, 0x0013), new PlayerDescriptionMsg { Spells = book.ToImmutable(), SpellBars = bars });
            rig.Frame();
        }

        public void Learn(uint spell)
        {
            handler.OnMagicUpdateSpell(Context, new GameEventHeader(Player, ++_event, 0x02C1), new MagicUpdateSpellEvent((ushort)spell, 1));
            rig.Frame();
        }

        public UiItem Cell(int tab, int index) => Assert.IsType<UiItem>(Cast.ListOf(tab)!.CellAt(index));

        public void Click(UiElement element)
        {
            Assert.True(rig.Click(element));
            rig.Frame();
        }

        public void DoubleClick(UiElement element)
        {
            var box = element.ScreenBox;
            rig.Manager.Input.MouseMove((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
            rig.Manager.Input.MouseButton(UiMouseButton.Left, down: true, doubleClick: true);
            rig.Manager.Input.MouseButton(UiMouseButton.Left, down: false);
            rig.Frame();
        }

        public void Dispose() => rig.Dispose();
    }

    private Game Enter(bool tables = true)
    {
        IInboundMessageHandler? handler = null;
        var rig = new LoginRig(fixture, h =>
        {
            handler = h;
            StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true);
        if (tables)
        {
            rig.Session.UseSpellsFrom(fixture.Dats);
            rig.Session.SpellTableLoaded.Wait(); // decoded on a worker
        }

        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        var screen = Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        handler!.OnPlayerCreate(Context, new PlayerCreateMsg(Player));
        handler.OnCreateObject(Context, ReducerHarness.Create(Player, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD), state: PhysicsState.Hidden | PhysicsState.Gravity, name: "Tester"), isUpdate: false);
        handler.OnPlayerDescription(Context, new GameEventHeader(Player, 1, 0x0013), new PlayerDescriptionMsg());
        rig.Frame();
        var game = new Game(rig, screen, handler);
        game.Cast.ChatNotice = game.Chat.Add;
        game.Cast.SelectedProvider = () => game.Selected;
        return game;
    }

    // The window draws when it, or anything inside it, is shown (the painter draws an element that is shown, its own flag and every ancestor's).
    private static bool Drawn(UiElement window) => window.IsShown || window.Children.Any(static c => c.IsShown);

    private static ImmutableArray<ImmutableArray<uint>> Bars(params uint[][] bars) => [.. bars.Select(static b => ImmutableArray.Create(b))];

    [RequiresLocalDatsFact]
    public void The_window_shows_in_magic_mode_only_and_the_floaty_and_the_drawing_follow()
    {
        using var g = Enter();
        var window = g.Cast;
        Assert.False(window.Visible); // the layout shows it; the controller hides it outside magic mode
        Assert.False(g.Floaty.Visible);
        Assert.False(Drawn(window));

        g.ChangeMode(CombatMode.Magic);
        Assert.True(window.Visible);
        Assert.True(g.Floaty.Visible);
        Assert.True(Drawn(window));
        Assert.Equal(CombatModes.Magic, window.Mode);

        foreach (var other in new[] { CombatMode.Melee, CombatMode.Missile, CombatMode.NonCombat })
        {
            g.ChangeMode(CombatMode.Magic);
            Assert.True(window.Visible);
            g.ChangeMode(other);
            Assert.False(window.Visible);
            Assert.Equal(other is CombatMode.Melee or CombatMode.Missile, g.Floaty.Visible); // the combat window's, not the spellcasting window's
            Assert.False(Drawn(window), $"a hidden window draws nothing (mode {other})");
        }
    }

    [RequiresLocalDatsFact]
    public void The_bars_list_each_tabs_spells_in_slot_order_with_the_SpellTables_icons_and_names_and_the_tabs_switch_on_a_click()
    {
        using var g = Enter();
        var bolt1 = Named("Flame Bolt I");
        var bolt2 = Named("Flame Bolt II");
        var strength = Named("Strength Self I");
        var frost = Named("Frost Bolt I");
        g.Describe([bolt1.Id, bolt2.Id, strength.Id, frost.Id], Bars([strength.Id, bolt2.Id, bolt1.Id], [frost.Id], [], [], [], [], [], []));
        g.ChangeMode(CombatMode.Magic);
        var window = g.Cast;

        Assert.Equal(0, window.OpenTab);
        Assert.Equal<uint>([strength.Id, bolt2.Id, bolt1.Id], window.ShownSpells(0)); // slot order, not the book's
        for (var i = 0; i < 3; i++)
        {
            var cell = g.Cell(0, i);
            var info = Table.Spells[cell.SpellId];
            Assert.Equal(Lifestone.Client.RetailUi.Rendering.UiImageLookup.Ready, g.Rig.Manager.SpellIcons.TryGetSpell(UiSpellIcon.Of(info), out var picture));
            Assert.Same(picture, cell.ComposedIcon); // the SpellTable's icon composed as retail's GetSpellIcon (M5 D147)
            Assert.Same(picture, cell.IconElement!.GeneratedImage);
            Assert.Equal(Lifestone.Client.RetailUi.Rendering.UiBlitMode.ThreeChannelAlpha, cell.IconElement.Image.Mode);
            Assert.Equal(info.Name + GameSpellcastingPanel.CastHint, cell.TooltipText?.Literal); // the name, then the hint
        }

        Assert.True(window.ListOf(0)!.IsShown);
        Assert.Empty(window.ShownSpells(1)); // tab 1 is not built until it opens

        g.Click(window.TabElement(1)!); // a click on the second tab
        Assert.Equal(1, window.OpenTab);
        Assert.Equal<uint>([frost.Id], window.ShownSpells(1));
        Assert.True(window.ListOf(1)!.IsShown);
        Assert.False(window.ListOf(0)!.IsShown);

        g.Click(window.TabElement(2)!);
        Assert.Equal(2, window.OpenTab);
        Assert.Empty(window.ShownSpells(2));
        g.Click(window.TabElement(0)!);
        Assert.Equal(0, window.OpenTab);
        Assert.Equal<uint>([strength.Id, bolt2.Id, bolt1.Id], window.ShownSpells(0));
    }

    [RequiresLocalDatsFact]
    public void A_favourite_the_character_does_not_know_is_left_out_and_a_bar_change_rewrites_the_list_live()
    {
        using var g = Enter();
        var bolt1 = Named("Flame Bolt I");
        var bolt2 = Named("Flame Bolt II");
        var strength = Named("Strength Self I");
        g.Describe([bolt1.Id, strength.Id], Bars([bolt1.Id, bolt2.Id, strength.Id])); // bolt2 is not known
        g.ChangeMode(CombatMode.Magic);
        Assert.Equal<uint>([bolt1.Id, strength.Id], g.Cast.ShownSpells(0));

        g.Learn(bolt2.Id); // now it is known: the favourite shows
        Assert.Equal<uint>([bolt1.Id, bolt2.Id, strength.Id], g.Cast.ShownSpells(0));

        g.Describe([bolt1.Id, bolt2.Id, strength.Id], Bars([strength.Id])); // the bar shrank
        Assert.Equal<uint>([strength.Id], g.Cast.ShownSpells(0));
        Assert.Equal(1, g.Cast.ListOf(0)!.OccupiedCount); // the cells after the favourite are empty slot boxes (M4 3.2 lane e)
    }

    [RequiresLocalDatsFact]
    public void M4_3_2_e_each_bar_draws_its_empty_slot_boxes_after_its_favourites_from_the_first_cell_and_an_empty_bar_draws_only_boxes()
    {
        // The cause (M4 3.2 lane e): the bars' item list 0x100000B6 is a list that grows (property 0x10000015 of -1, one row: 0x5F of -1, width 439); retail pads it with empty slot cells (UIElement_ItemList::UpdateEmptySlots, 11.6096
        // 0x004E4390) and Lifestone made cells only for the favourites and deleted the rest, so an empty bar drew nothing. A favourite still fills the first cell (ItemList_AddSpellShortcut 0x004E4D30).
        using var g = Enter();
        var bolt1 = Named("Flame Bolt I");
        var strength = Named("Strength Self I");
        g.Describe([bolt1.Id, strength.Id], Bars([bolt1.Id, strength.Id]));
        g.ChangeMode(CombatMode.Magic);

        var list = g.Cast.ListOf(0)!;
        var width = list.Box.Width / UiItemList.DefaultCellWidth;
        Assert.True(width >= 10, $"the bar is {list.Box.Width} wide");
        Assert.Equal(width, list.CellCount); // a drawn cell for every whole cell the bar's width holds
        Assert.Equal(2, list.OccupiedCount);
        Assert.Equal(width - 2, list.EmptyCount);
        Assert.Equal(bolt1.Id, g.Cell(0, 0).SpellId); // the favourites fill from the first cell
        Assert.Equal(strength.Id, g.Cell(0, 1).SpellId);
        for (var i = 2; i < width; i++)
        {
            var cell = g.Cell(0, i);
            Assert.True(cell.IsEmpty);
            Assert.Equal(UiItem.EmptyState, cell.IconElement!.State); // the empty-slot art: the icon child is in the empty state
            Assert.NotEmpty(cell.IconElement.CurrentStateDesc!.Media); // and that state has an image
            Assert.True(cell.IsShown);
        }

        // An empty bar: every cell a box.
        var empty = g.Cast.FirstTabWithoutSpells();
        Assert.True(empty > 0);
        Assert.True(g.Cast.OpenTabByClick(empty));
        g.Rig.Frame();
        var emptyList = g.Cast.ListOf(empty)!;
        Assert.Equal(width, emptyList.CellCount);
        Assert.Equal(0, emptyList.OccupiedCount);
        Assert.All(emptyList.Items.Cast<UiItem>(), static c => Assert.Equal(UiItem.EmptyState, c.IconElement!.State));

        // A bar that shrank empties its surplus cells: the boxes stay, the favourite goes.
        Assert.True(g.Cast.OpenTabByClick(0));
        g.Rig.Frame();
        g.Describe([bolt1.Id, strength.Id], Bars([strength.Id]));
        Assert.Equal(width, list.CellCount);
        Assert.Equal(1, list.OccupiedCount);
        Assert.Equal(strength.Id, g.Cell(0, 0).SpellId);
        Assert.True(g.Cell(0, 1).IsEmpty);
    }

    [RequiresLocalDatsFact]
    public void A_known_favourite_the_loaded_table_lacks_is_left_out_and_named_once_and_the_tab_still_builds()
    {
        // M3 3.2 review finding 9: readiness is the table's own, not the bar's first known favourite's. The stand-in's probe book knows 60001 on purpose (StandInPlayerStats.ProbeUnknownSpell); no retail SpellTable holds it.
        using var g = Enter();
        var bolt1 = Named("Flame Bolt I");
        var strength = Named("Strength Self I");
        const uint unknown = StandInPlayerStats.ProbeUnknownSpell;
        Assert.False(Table.Spells.ContainsKey(unknown));
        g.Describe([unknown, bolt1.Id, strength.Id], Bars([unknown, bolt1.Id, strength.Id])); // the unknown spell is the bar's first known favourite
        g.ChangeMode(CombatMode.Magic);

        Assert.False(g.Cast.AwaitingSpellTable); // the table is loaded: nothing to wait for
        Assert.Equal(1, g.Cast.Builds);
        Assert.Equal<uint>([bolt1.Id, strength.Id], g.Cast.ShownSpells(0)); // the known spells, in slot order; the unknown one left out
        Assert.Equal(1, g.Rig.Log.Lines.Count(l => l.Contains("TODO(m3-spellcast-unknown-spell)", StringComparison.Ordinal) && l.Contains($"favourite {unknown} ", StringComparison.Ordinal)));

        g.Describe([unknown, bolt1.Id, strength.Id], Bars([unknown, strength.Id])); // the bar changed: the tab is built again, the spell is not named a second time
        Assert.Equal(2, g.Cast.Builds);
        Assert.Equal<uint>([strength.Id], g.Cast.ShownSpells(0));
        Assert.Equal(1, g.Rig.Log.Lines.Count(l => l.Contains("TODO(m3-spellcast-unknown-spell)", StringComparison.Ordinal)));
    }

    [RequiresLocalDatsFact]
    public void The_frame_opens_the_first_tab_whose_bar_holds_spells_by_a_tab_click_and_the_earlier_tab_comes_back()
    {
        // M3 3.2 fix B (3.2-R7): live, BSD Mage's spells sit on bars 1 and 2, so retail's opening tab (the first) is an empty list in the shot.
        using var g = Enter();
        var bolt = Named("Flame Bolt I");
        var strength = Named("Strength Self I");
        g.Describe([bolt.Id, strength.Id], Bars([], [bolt.Id], [strength.Id]));
        g.ChangeMode(CombatMode.Magic);
        Assert.Equal(0, g.Cast.OpenTab); // retail's own opening tab stays for play
        Assert.Empty(g.Cast.ShownSpells(0));
        Assert.Equal(1, g.Cast.FirstTabWithSpells());

        var choice = PanelFramesPlan.OpenSpellcastingFrameTab(g.Cast);
        g.Rig.Frame();
        Assert.Equal(new SpellcastingTabChoice(1, true, true), choice);
        Assert.Equal(1, g.Cast.OpenTab);
        Assert.Equal<uint>([bolt.Id], g.Cast.ShownSpells(1)); // the tab click built the tab

        Assert.True(g.Cast.OpenTabByClick(0)); // the earlier tab comes back
        Assert.Equal(0, g.Cast.OpenTab);
    }

    [RequiresLocalDatsFact]
    public void A_bar_that_changed_while_another_tab_was_open_shows_its_new_spells_when_its_tab_is_opened()
    {
        // 3.2-R6: a favourite added to bar 3 (index 2) while bar 1 is shown; opening tab 3 must show it, not the bar as it was built, nor an empty box.
        using var g = Enter();
        var bolt = Named("Flame Bolt I");
        var strength = Named("Strength Self I");
        g.Describe([bolt.Id, strength.Id], Bars([bolt.Id], [], [strength.Id]));
        g.ChangeMode(CombatMode.Magic);
        Assert.True(g.Cast.OpenTabByClick(2)); // built once with the strength spell
        g.Rig.Frame();
        Assert.Equal<uint>([strength.Id], g.Cast.ShownSpells(2));
        Assert.True(g.Cast.OpenTabByClick(0));
        g.Rig.Frame();

        g.Describe([bolt.Id, strength.Id], Bars([bolt.Id], [], [strength.Id, bolt.Id])); // bar 3 changes while tab 1 is shown (a description also sets the combat mode: the window is hidden until the mode is magic again)
        g.ChangeMode(CombatMode.Magic);
        Assert.Equal(0, g.Cast.OpenTab);
        Assert.Equal<uint>([strength.Id], g.Cast.ShownSpells(2)); // not the open tab: not rebuilt yet

        var buildsBefore = g.Cast.Builds;
        Assert.True(g.Cast.OpenTabByClick(2));
        g.Rig.Frame();
        Assert.True(g.Cast.Builds > buildsBefore); // the tab click rebuilt the tab whose bar changed
        Assert.Equal<uint>([strength.Id, bolt.Id], g.Cast.ShownSpells(2));
        Assert.Equal(strength.Id, g.Cell(2, 0).SpellId);
        Assert.Equal(bolt.Id, g.Cell(2, 1).SpellId);
    }

    [RequiresLocalDatsFact]
    public void The_frame_keeps_the_open_tab_when_no_bar_holds_spells()
    {
        using var g = Enter();
        g.ChangeMode(CombatMode.Magic);
        Assert.Equal(-1, g.Cast.FirstTabWithSpells());
        Assert.Equal(new SpellcastingTabChoice(0, false, true), PanelFramesPlan.OpenSpellcastingFrameTab(g.Cast));
        Assert.Equal(0, g.Cast.OpenTab);
    }

    [RequiresLocalDatsFact]
    public void A_shown_window_builds_only_when_its_bar_or_its_tab_changes_and_a_hidden_one_builds_nothing()
    {
        using var g = Enter();
        var bolt1 = Named("Flame Bolt I");
        var strength = Named("Strength Self I");
        var frost = Named("Frost Bolt I");
        var other = Named("Flame Bolt II");
        g.Describe([bolt1.Id, strength.Id, frost.Id], Bars([bolt1.Id, strength.Id], [frost.Id]));
        Assert.Equal(0, g.Cast.Builds); // hidden: the description built nothing

        g.ChangeMode(CombatMode.Magic);
        Assert.Equal(1, g.Cast.Builds); // the shown tab, once
        g.Learn(other.Id); // a learned spell that is on no bar
        Assert.Equal(1, g.Cast.Builds);
        g.Click(g.Cast.TabElement(1)!);
        Assert.Equal(2, g.Cast.Builds); // the tab that opened
        g.Click(g.Cast.TabElement(0)!);
        Assert.Equal(2, g.Cast.Builds); // tab 0 is still as it was built
        g.Describe([bolt1.Id, strength.Id, frost.Id, other.Id], Bars([bolt1.Id, strength.Id, other.Id], [frost.Id]));
        Assert.Equal(3, g.Cast.Builds); // the open tab's bar changed

        g.ChangeMode(CombatMode.NonCombat);
        g.Describe([bolt1.Id, strength.Id, frost.Id, other.Id], Bars([strength.Id], [frost.Id]));
        Assert.Equal(3, g.Cast.Builds); // hidden: nothing built
        g.ChangeMode(CombatMode.Magic);
        Assert.Equal(4, g.Cast.Builds); // it shows again with the new bar
        Assert.Equal<uint>([strength.Id], g.Cast.ShownSpells(0));
    }

    [RequiresLocalDatsFact]
    public void A_click_selects_a_spell_names_it_and_a_double_click_casts_a_self_spell_at_the_player_through_the_Action_API()
    {
        using var g = Enter();
        var strength = Named("Strength Self I");
        Assert.True((strength.Flags & SpellFlags.SelfTargeted) != 0);
        var bolt1 = Named("Flame Bolt I");
        g.Describe([strength.Id, bolt1.Id], Bars([strength.Id, bolt1.Id]));
        g.ChangeMode(CombatMode.Magic);
        var window = g.Cast;
        Assert.Equal(0u, window.SelectedSpell);
        Assert.Equal(UiStateIds.Ghosted, window.CastButton!.State); // nothing to cast yet

        g.Click(g.Cell(0, 0));
        Assert.Equal(strength.Id, window.SelectedSpell);
        Assert.True(g.Cell(0, 0).IsSelected);
        Assert.False(g.Cell(0, 1).IsSelected);
        Assert.Equal(strength.Name, window.NameText!.Text);
        Assert.NotEqual(UiStateIds.Ghosted, window.CastButton.State);
        Assert.Equal(0, window.Casts); // a click selects; it does not cast

        g.DoubleClick(g.Cell(0, 0));
        Assert.Equal(1, window.Casts);
        var cast = Assert.IsType<ActionHandle>(window.LastCast);
        Assert.Equal(ActionKind.CastSpell, cast.Kind);
        Assert.Equal(ActionSource.Ui, cast.Source);
        Assert.Contains($"spell={strength.Id}", cast.Summary, StringComparison.Ordinal);
        Assert.Contains($"target={ActionText.Hex(Player)}", cast.Summary, StringComparison.Ordinal); // at the player
        Assert.Empty(g.Chat);
    }

    [RequiresLocalDatsFact]
    public void A_targeted_spell_casts_at_the_selected_object_and_with_none_the_chat_says_so_and_nothing_is_sent()
    {
        using var g = Enter();
        var bolt1 = Named("Flame Bolt I");
        Assert.True((bolt1.Flags & SpellFlags.SelfTargeted) == 0);
        g.Describe([bolt1.Id], Bars([bolt1.Id]));
        g.ChangeMode(CombatMode.Magic);
        var window = g.Cast;

        g.DoubleClick(g.Cell(0, 0)); // no object selected
        Assert.Equal(0, window.Casts);
        Assert.Null(window.LastCast);
        Assert.Equal([GameSpellcastingPanel.NoTargetText], g.Chat);

        g.Selected = Monster;
        g.DoubleClick(g.Cell(0, 0));
        Assert.Equal(1, window.Casts);
        var cast = Assert.IsType<ActionHandle>(window.LastCast);
        Assert.Equal(ActionKind.CastSpell, cast.Kind);
        Assert.Equal(ActionSource.Ui, cast.Source);
        Assert.Contains($"spell={bolt1.Id}", cast.Summary, StringComparison.Ordinal);
        Assert.Contains($"target={ActionText.Hex(Monster)}", cast.Summary, StringComparison.Ordinal);
        Assert.Single(g.Chat); // no second line
    }

    [RequiresLocalDatsFact]
    public void The_cast_button_casts_the_selected_spell_and_with_none_selected_says_so_and_each_tab_keeps_its_own_selection()
    {
        using var g = Enter();
        var strength = Named("Strength Self I");
        var frost = Named("Frost Bolt I");
        g.Describe([strength.Id, frost.Id], Bars([strength.Id], [frost.Id]));
        g.ChangeMode(CombatMode.Magic);
        var window = g.Cast;

        window.CastButton!.BroadcastElementMessage(UiMessageIds.ButtonClick); // the button is ghosted with nothing selected: its click goes no further
        Assert.Equal(0, window.Casts);
        Assert.Empty(g.Chat);
        window.CastSelected(); // gmSpellcastingUI::Cast itself (a hotkey reaches it): none selected
        Assert.Equal(0, window.Casts);
        Assert.Equal([GameSpellcastingPanel.NoSpellText], g.Chat);

        g.Click(g.Cell(0, 0));
        window.CastButton.BroadcastElementMessage(UiMessageIds.ButtonClick);
        Assert.Equal(1, window.Casts);
        Assert.Contains($"spell={strength.Id}", window.LastCast!.Summary, StringComparison.Ordinal);

        g.Click(window.TabElement(1)!);
        Assert.Equal(0u, window.SelectedSpell); // tab 1 has its own selection
        Assert.Equal(UiStateIds.Ghosted, window.CastButton.State);
        g.Click(g.Cell(1, 0));
        Assert.Equal(frost.Id, window.SelectedSpell);
        g.Click(window.TabElement(0)!);
        Assert.Equal(strength.Id, window.SelectedSpell);
        Assert.True(g.Cell(0, 0).IsSelected);
    }

    [RequiresLocalDatsFact]
    public void M6_15_an_automation_cast_shows_as_a_persons_cast_shows_its_tab_is_the_open_tab_and_the_spell_is_the_selected_one()
    {
        using var g = Enter();
        var strength = Named("Strength Self I");
        var bolt1 = Named("Flame Bolt I");
        var frost = Named("Frost Bolt I");
        g.Describe([strength.Id, bolt1.Id, frost.Id], Bars([strength.Id], [bolt1.Id, frost.Id]));
        g.ChangeMode(CombatMode.Magic);
        var window = g.Cast;
        Assert.Equal(0, window.OpenTab);
        Assert.Equal(0u, window.SelectedSpell);

        // The Action API's notice of an automation cast from tab II, position 2 (PlayerRulesGate.CastSent): no click, no cast of the window's own.
        g.Rig.Session.ActionApi.PublishSpellBarCast(frost.Id, 1, 1);
        g.Rig.Frame();
        Assert.Equal(1, window.OpenTab); // the spell's tab is the shown tab
        Assert.True(window.ListOf(1)!.IsShown);
        Assert.Equal(frost.Id, window.SelectedSpell); // and the spell is the selected one
        Assert.True(g.Cell(1, 1).IsSelected);
        Assert.False(g.Cell(1, 0).IsSelected);
        Assert.Equal(frost.Name, window.NameText!.Text); // the name text and the cast button name it, as after a person's click
        Assert.NotEqual(UiStateIds.Ghosted, window.CastButton!.State);
        Assert.Equal(0, window.Casts); // the window cast nothing: the automation's cast is already on its way
        Assert.Equal(1, window.BarCastsShown);

        // The next cast is from tab I: the window follows, and tab II keeps its own selection for when it opens again.
        g.Rig.Session.ActionApi.PublishSpellBarCast(strength.Id, 0, 0);
        g.Rig.Frame();
        Assert.Equal(0, window.OpenTab);
        Assert.Equal(strength.Id, window.SelectedSpell);
        Assert.True(g.Cell(0, 0).IsSelected);
        g.Click(window.TabElement(1)!);
        Assert.Equal(frost.Id, window.SelectedSpell);

        // A hidden window (the mode is not Magic) opens the tab and keeps the selection for when it shows.
        g.ChangeMode(CombatMode.NonCombat);
        Assert.False(window.Visible);
        g.Rig.Session.ActionApi.PublishSpellBarCast(bolt1.Id, 1, 0);
        g.Rig.Frame();
        g.ChangeMode(CombatMode.Magic);
        Assert.Equal(1, window.OpenTab);
        Assert.Equal(bolt1.Id, window.SelectedSpell);
        Assert.True(g.Cell(1, 0).IsSelected);

        // A spell the tab does not show selects nothing (a cast named for a bar that no longer holds it).
        Assert.False(window.ShowCastFromBar(0, frost.Id));
        Assert.Equal(0u, window.SelectedSpell);
    }

    [RequiresLocalDatsFact]
    public void The_list_waits_for_the_SpellTable_and_fills_when_it_arrives()
    {
        using var g = Enter(tables: false);
        var bolt1 = Named("Flame Bolt I"); // the test reads its own copy of the table; the session has none
        g.Describe([bolt1.Id], Bars([bolt1.Id]));
        g.ChangeMode(CombatMode.Magic);
        Assert.True(g.Cast.AwaitingSpellTable);
        Assert.Empty(g.Cast.ShownSpells(0)); // never a guess

        g.Rig.Session.UseSpellsFrom(fixture.Dats);
        g.Rig.Session.SpellTableLoaded.Wait();
        for (var i = 0; i < 4 && g.Cast.AwaitingSpellTable; i++)
        {
            g.Rig.Frame();
        }

        Assert.False(g.Cast.AwaitingSpellTable);
        Assert.Equal<uint>([bolt1.Id], g.Cast.ShownSpells(0));
    }

    [RequiresLocalDatsFact]
    public void A_log_off_leaves_no_handler_on_the_session()
    {
        IInboundMessageHandler? handler = null;
        using var rig = new LoginRig(fixture, h =>
        {
            handler = h;
            StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true);
        rig.Run(2.5);
        var baseline = rig.Session.Bus.HandlerCount;
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        Assert.NotNull(handler);
        Assert.True(rig.Session.Bus.HandlerCount >= baseline + 3, "the window follows the mode, the description and the book");
        rig.Flow.QueueUIMode(UiModes.CharacterManagement);
        for (var i = 0; i < 4; i++)
        {
            rig.Frame();
        }

        Assert.Equal(baseline, rig.Session.Bus.HandlerCount);
    }

    [Fact]
    public void A_session_says_when_its_SpellTable_is_there_whatever_id_is_asked()
    {
        // The readiness the window reads (M3 3.2 review finding 9): false without a table, true once the Action API answers from one, and an id that table lacks is null while the table stays ready.
        using var session = GameSession.Offline(static h => StandInSessions.CharacterSelect(h), new ManualTimeProvider(), new RecordingLogSink());
        IGameSessionView view = session;
        Assert.False(view.SpellTableReady);
        Assert.Null(view.Spell(27));

        var bolt = WorldActionRig.Spell(27, SpellType.Projectile) with { Name = "Flame Bolt I" };
        session.ActionApi.SpellLookup = id => id == bolt.Id ? bolt : null; // a table given another way than the dats (the stand-in tests' seam)
        Assert.True(view.SpellTableReady);
        Assert.Same(bolt, view.Spell(27));
        Assert.Null(view.Spell(StandInPlayerStats.ProbeUnknownSpell));
        Assert.True(view.SpellTableReady);
    }
}
