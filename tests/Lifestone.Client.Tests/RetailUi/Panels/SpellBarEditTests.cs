using System.Collections.Immutable;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Toolbar;
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
/// The spellcasting window made editable and keyed (M5 6 s1) over the real gameplay layout, the real key map and a scripted spellbook: a spell dragged from the spellbook onto a bar's cell is added at that cell
/// (<c>gmSpellcastingUI::HandleDropRelease</c>), one dragged along its bar is removed and added at the new place, one dragged off its bar is removed, a drop ACE would ignore is refused before anything is sent; every action is
/// <see cref="ActionSource.Ui"/>. In Magic mode the number keys cast the open tab's nth spell as a double click does and the navigation keys change the tab and the selection as clicks do; outside Magic mode the spell keys do nothing.
/// Offline: the stand-in session; the tests read what the Action API was asked (<see cref="IGameSessionView.ActionLog"/>) and play the server's descriptions.
/// </summary>
public sealed class SpellBarEditTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint Player = 0x50000001;
    private const uint Monster = 0x80000042;
    private const uint SpellsButton = 0x10000198;
    private static readonly InboundContext Context = new(0, MessageQueue.SmartBox, 0);

    private SpellTableData? _table;

    private SpellTableData Table => _table ??= SpellTableData.Load(fixture.Dats);

    private SpellInfo Named(string name) => Table.Spells.Values.First(s => s.Name == name);

    [RequiresLocalDatsFact]
    public void A_spell_dragged_from_the_spellbook_onto_a_bar_cell_is_added_at_that_cell_and_stays_across_the_next_description()
    {
        using var g = Enter();
        var (strength, bolt, frost) = (Named("Strength Self I").Id, Named("Flame Bolt I").Id, Named("Frost Bolt I").Id);
        g.Describe([strength, bolt, frost], Bars([strength, bolt]));
        g.ChangeMode(CombatMode.Magic);
        g.OpenSpellbook();
        var source = Assert.IsType<UiItem>(Enumerable.Range(0, g.Book.List.CellCount).Select(i => g.Book.List.CellAt(i)).First(c => c?.SpellId == frost));
        var mark = g.Log.Count;

        g.DragTo(source, g.Cell(0, 1)); // onto Flame Bolt I, the bar's second cell
        Assert.Equal(UiDragEnd.Dropped, g.Drag.LastEnd);
        var add = Assert.Single(g.FavouritesSince(mark));
        Assert.Equal((ActionKind.AddSpellFavorite, ActionSource.Ui), (add.Kind, add.Source));
        Assert.Equal($"AddSpellFavorite spell={frost} index=1 bar=0", add.Summary);
        Assert.Equal(add.Summary, Assert.Single(g.Cast.BarEdits).Summary);

        g.Describe([strength, bolt, frost], Bars([strength, frost, bolt])); // ACE's next description carries the bar it stored
        Assert.Equal([strength, frost, bolt], g.Cast.ShownSpells(0));
    }

    [RequiresLocalDatsFact]
    public void A_spell_dragged_along_its_bar_is_removed_and_added_at_the_new_place_and_one_dragged_off_the_bar_is_removed()
    {
        using var g = Enter();
        var (strength, bolt, frost) = (Named("Strength Self I").Id, Named("Flame Bolt I").Id, Named("Frost Bolt I").Id);
        g.Describe([strength, bolt, frost], Bars([strength, bolt, frost]));
        g.ChangeMode(CombatMode.Magic);
        var mark = g.Log.Count;

        g.DragTo(g.Cell(0, 0), g.Cell(0, 2)); // Strength before Frost Bolt: AddFavorite moves the place one left, the old one being before it
        var move = g.FavouritesSince(mark);
        Assert.Equal([$"RemoveSpellFavorite spell={strength} bar=0", $"AddSpellFavorite spell={strength} index=1 bar=0"], move.Select(e => e.Summary));
        Assert.All(move, e => Assert.Equal(ActionSource.Ui, e.Source));

        g.Describe([strength, bolt, frost], Bars([bolt, strength, frost]));
        mark = g.Log.Count;
        g.DragTo(g.Cell(0, 2), g.Cast.TabElement(1)!); // onto tab II: off bar 0, onto the end of bar 1
        Assert.Equal([$"RemoveSpellFavorite spell={frost} bar=0", $"AddSpellFavorite spell={frost} index=0 bar=1"], g.FavouritesSince(mark).Select(e => e.Summary));

        g.Describe([strength, bolt, frost], Bars([bolt, strength], [frost]));
        mark = g.Log.Count;
        var box = g.Cast.ScreenBox;
        g.DragTo(g.Cell(0, 0), (box.X1 + 120, Math.Max(10, box.Y0 - 120))); // off the window, onto the world view
        var remove = Assert.Single(g.FavouritesSince(mark));
        Assert.Equal(($"RemoveSpellFavorite spell={bolt} bar=0", ActionSource.Ui), (remove.Summary, remove.Source));
    }

    [RequiresLocalDatsFact]
    public void A_drop_ACE_would_ignore_is_refused_before_anything_is_sent_and_names_its_TODO()
    {
        using var g = Enter();
        var (strength, bolt) = (Named("Strength Self I").Id, Named("Flame Bolt I").Id);
        g.Describe([strength, bolt], Bars([strength, bolt]));
        g.ChangeMode(CombatMode.Magic);
        var mark = g.Log.Count;
        var from = Center(g.Cell(0, 1));
        g.MoveTo(from);
        g.Down();
        g.MoveTo((from.X + 10, from.Y + 2)); // the drag of Flame Bolt I begins
        Assert.Equal(UiDragPhase.Dragging, g.Drag.Phase);
        g.Describe([strength], Bars([strength, bolt])); // the server says the character no longer knows it
        g.MoveTo(Center(g.Cast.TabElement(2)!));
        g.Up();
        g.Rig.Frame();

        Assert.Equal(UiDragEnd.Refused, g.Drag.LastEnd);
        Assert.Empty(g.FavouritesSince(mark));
        Assert.Contains(g.Rig.Log.Lines, l => l.Contains("TODO(m5-spellbar-refusal-line)", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void A_dragged_spell_carries_its_composed_picture_under_the_pointer()
    {
        // M5 D147: UIElement_ItemList::PrepareDragIcon (11.4186 0x004E2A50) draws the spell's GetSpellIcon picture (the power tile, the icon, the outline and the badge), not the raw SpellTable icon.
        using var g = Enter();
        var (strength, bolt) = (Named("Strength Self I").Id, Named("Flame Bolt I").Id);
        g.Describe([strength, bolt], Bars([strength, bolt]));
        g.ChangeMode(CombatMode.Magic);
        g.Rig.Run(1.0);
        var from = Center(g.Cell(0, 0));
        g.MoveTo(from);
        g.Down();
        g.MoveTo((from.X + 10, from.Y + 2));
        g.Rig.Frame();
        Assert.Equal(UiDragPhase.Dragging, g.Drag.Phase);
        var icon = Assert.IsType<UiItem>(g.Drag.Icon);
        Assert.Equal(strength, icon.SpellId);
        Assert.Equal(Lifestone.Client.RetailUi.Rendering.UiImageLookup.Ready, g.Rig.Manager.SpellIcons.TryGetSpell(UiSpellIcon.Of(Named("Strength Self I")), out var picture));
        Assert.Same(picture, icon.ComposedIcon);
        Assert.Same(picture, icon.IconElement!.GeneratedImage);
        Assert.True(g.Rig.Manager.Input.KeyDown(DikScanCodes.Escape));
        g.Up();
    }

    [RequiresLocalDatsFact]
    public void In_magic_mode_key_3_casts_the_third_spell_of_the_shown_tab_at_the_selection_as_a_double_click_does()
    {
        using var g = Enter();
        var (strength, bolt, frost) = (Named("Strength Self I").Id, Named("Flame Bolt I").Id, Named("Frost Bolt I").Id);
        g.Describe([strength, bolt, frost], Bars([strength], [strength, bolt, frost]));
        g.ChangeMode(CombatMode.Magic);
        Assert.True(g.Cast.OpenTabByClick(1));
        g.Rig.Frame();
        g.Selected = Monster;

        Tap(g, DikScanCodes.D1 + 2);
        Assert.Equal(frost, g.Cast.SelectedSpell);
        Assert.Equal(1, g.Cast.Casts);
        var cast = Assert.IsType<ActionHandle>(g.Cast.LastCast);
        Assert.Equal((ActionKind.CastSpell, ActionSource.Ui), (cast.Kind, cast.Source));
        Assert.Contains($"spell={frost}", cast.Summary, StringComparison.Ordinal);
        Assert.Contains($"target={ActionText.Hex(Monster)}", cast.Summary, StringComparison.Ordinal);
        Assert.Equal(SpellBarKeys.FirstQuickslotAction + 2, g.Cast.Keys!.LastAction);

        Tap(g, DikScanCodes.D1 + 5); // an empty slot: nothing
        Assert.Equal(1, g.Cast.Casts);
    }

    [RequiresLocalDatsFact]
    public void The_navigation_keys_change_the_spell_and_the_tab_as_clicks_do_and_End_casts_the_selected_spell()
    {
        using var g = Enter();
        var (strength, bolt, frost) = (Named("Strength Self I").Id, Named("Flame Bolt I").Id, Named("Frost Bolt I").Id);
        g.Describe([strength, bolt, frost], Bars([strength, bolt, frost], [bolt]));
        g.ChangeMode(CombatMode.Magic);
        var w = g.Cast;

        Tap(g, DikScanCodes.Next); // none selected: the first
        Assert.Equal(strength, w.SelectedSpell);
        Assert.True(g.Cell(0, 0).IsSelected);
        Tap(g, DikScanCodes.Next);
        Assert.Equal(bolt, w.SelectedSpell);
        Tap(g, DikScanCodes.Delete);
        Assert.Equal(strength, w.SelectedSpell);
        Tap(g, DikScanCodes.Delete); // before the first: the last
        Assert.Equal(frost, w.SelectedSpell);
        Tap(g, DikScanCodes.Next); // after the last: the first
        Assert.Equal(strength, w.SelectedSpell);
        CtrlTap(g, DikScanCodes.Next);
        Assert.Equal(frost, w.SelectedSpell);
        CtrlTap(g, DikScanCodes.Delete);
        Assert.Equal(strength, w.SelectedSpell);

        Tap(g, DikScanCodes.End); // the cast button's rule: a self spell at the player
        Assert.Equal(1, w.Casts);
        Assert.Contains($"spell={strength}", w.LastCast!.Summary, StringComparison.Ordinal);
        Assert.Equal(ActionSource.Ui, w.LastCast.Source);

        Tap(g, DikScanCodes.Prior);
        Assert.Equal(1, w.OpenTab);
        Tap(g, DikScanCodes.Insert);
        Assert.Equal(0, w.OpenTab);
        Tap(g, DikScanCodes.Insert); // before the first: the last
        Assert.Equal(GameSpellcastingPanel.TabCount - 1, w.OpenTab);
        Tap(g, DikScanCodes.Prior); // after the last: the first
        Assert.Equal(0, w.OpenTab);
        CtrlTap(g, DikScanCodes.Prior);
        Assert.Equal(GameSpellcastingPanel.TabCount - 1, w.OpenTab);
        CtrlTap(g, DikScanCodes.Insert);
        Assert.Equal(0, w.OpenTab);
        Assert.Equal(strength, w.SelectedSpell); // each tab keeps its own selection
    }

    [RequiresLocalDatsFact]
    public void The_spell_keys_are_registered_in_magic_mode_only_and_outside_it_key_1_stays_the_shortcut_bars()
    {
        using var g = Enter();
        var strength = Named("Strength Self I").Id;
        g.Describe([strength], Bars([strength]));
        var keys = Assert.IsType<SpellBarKeys>(g.Cast.Keys);
        Assert.False(keys.Registered);
        Tap(g, DikScanCodes.D1);
        Tap(g, DikScanCodes.End);
        Assert.Equal(0, g.Cast.Casts);
        Assert.Equal(0, keys.Presses);

        g.ChangeMode(CombatMode.Magic);
        Assert.True(keys.Registered);
        var maps = g.Rig.Manager.Input.InputMaps.Select(m => m.InputMapId).ToList();
        Assert.True(maps.IndexOf(SpellBarKeys.InputMap) < maps.IndexOf(GameHotbar.InputMap), "the magic map registers after the shortcut bar's at the same priority and is searched first");
        Tap(g, DikScanCodes.D1);
        Assert.Equal(1, g.Cast.Casts);

        g.ChangeMode(CombatMode.NonCombat);
        Assert.False(keys.Registered);
        Assert.DoesNotContain(g.Rig.Manager.Input.InputMaps, m => m.InputMapId == SpellBarKeys.InputMap);
        Tap(g, DikScanCodes.D1);
        Assert.Equal(1, g.Cast.Casts); // no spell key outside magic mode
    }

    private static ImmutableArray<ImmutableArray<uint>> Bars(params uint[][] bars) => [.. bars.Select(static b => ImmutableArray.Create(b))];

    private static (int X, int Y) Center(UiElement element)
    {
        var box = element.ScreenBox;
        return ((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
    }

    private static void Tap(Game g, int key)
    {
        g.Rig.Manager.Input.KeyDown(key);
        g.Rig.Manager.Input.KeyUp(key);
        g.Rig.Frame();
    }

    private static void CtrlTap(Game g, int key)
    {
        g.Rig.Manager.Input.KeyDown(DikScanCodes.LControl);
        Tap(g, key);
        g.Rig.Manager.Input.KeyUp(DikScanCodes.LControl);
    }

    private sealed class Game(LoginRig rig, GamePlayScreen screen, IInboundMessageHandler handler) : IDisposable
    {
        private byte _sequence;
        private uint _event = 1;
        private CombatMode? _mode;

        public LoginRig Rig => rig;

        public GameSpellcastingPanel Cast => Assert.IsType<GameSpellcastingPanel>(screen.Root!.GetChildRecursive(GameSpellcastingPanel.WindowId));

        public GameSpellbookPage Book => Assert.Single(screen.PanelControllers!.ControllersOf(13).OfType<GameSpellbookPage>());

        public UiDrag Drag => rig.Manager.Input.Drag;

        public ActionLog Log => rig.Session.ActionLog;

        public uint Selected { get; set; }

        public List<ActionLogEntry> FavouritesSince(int mark)
        {
            var list = new List<ActionLogEntry>();
            for (var i = mark; i < Log.Count; i++)
            {
                if (Log[i].Kind is ActionKind.AddSpellFavorite or ActionKind.RemoveSpellFavorite)
                {
                    list.Add(Log[i]);
                }
            }

            return list;
        }

        public void ChangeMode(CombatMode mode)
        {
            _mode = mode;
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

            handler.OnPlayerDescription(Context, new GameEventHeader(Player, ++_event, 0x0013), new PlayerDescriptionMsg { Spells = book.ToImmutable(), SpellBars = bars, SpellbookFilters = 0x3FFF });
            rig.Frame();
            if (_mode is { } mode)
            {
                ChangeMode(mode); // this description carries no mode, so the window would hide: the server's mode again
            }
        }

        public void OpenSpellbook()
        {
            Assert.True(rig.Click(Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(SpellsButton))));
            for (var i = 0; i < 12; i++)
            {
                rig.Frame();
            }

            Assert.Equal(13u, screen.PanelHost!.CurrentPanel);
        }

        public UiItem Cell(int tab, int index) => Assert.IsType<UiItem>(Cast.ListOf(tab)!.CellAt(index));

        public void MoveTo((int X, int Y) point) => rig.Manager.Input.MouseMove(point.X, point.Y);

        public void Down() => rig.Manager.Input.MouseButton(UiMouseButton.Left, down: true);

        public void Up() => rig.Manager.Input.MouseButton(UiMouseButton.Left, down: false);

        public void DragTo(UiElement source, UiElement target) => DragTo(source, Center(target));

        public void DragTo(UiElement source, (int X, int Y) target)
        {
            rig.Run(1.0); // apart from the last gesture: no double click
            var from = Center(source);
            MoveTo(from);
            Down();
            MoveTo((from.X + 3, from.Y));
            MoveTo((from.X + 10, from.Y + 2));
            MoveTo(target);
            Up();
            rig.Frame();
        }

        public void Dispose() => rig.Dispose();
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
        rig.Session.UseSpellsFrom(fixture.Dats);
        rig.Session.SpellTableLoaded.Wait(); // decoded on a worker
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
        game.Cast.SelectedProvider = () => game.Selected;
        return game;
    }
}
