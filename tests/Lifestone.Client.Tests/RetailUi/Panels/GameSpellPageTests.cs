using System.Collections.Immutable;
using System.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The spell page (M3 2.2 slice b3): the spellbook tab <see cref="GameSpellbookPage"/> (retail's filter rule and sort over a scripted spellbook, a 247-spell book filled over frames, the filter buttons, a list that waits
/// for the SpellTable, delete answering NotInM3) and the components tab <see cref="GameComponentsPage"/> (the pack's components by category with summed counts and the desired amounts, live on inventory events, the
/// buy-rate box answering NotInM3), and the toolbar's spells button opening the page on the spellbook tab. Everything is scripted on the offline stand-in: no server is contacted.
/// </summary>
public sealed class GameSpellPageTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint SpellsButton = 0x10000198;
    private const uint Player = 0x50000001;
    private const uint ComponentsTab = 0x100002AA;
    private const uint WarButton = 0x1000029B;
    private const uint CreatureButton = 0x10000298;
    private const uint Level1Button = 0x1000029C;
    private const uint ComponentItemType = 0x1000;

    private SpellTableData Table => _table ??= SpellTableData.Load(fixture.Dats);

    private SpellTableData? _table;

    private sealed class Game(LoginRig rig, GamePlayScreen screen, IInboundMessageHandler handler) : IDisposable
    {
        private uint _event = 1;

        public LoginRig Rig => rig;

        public GamePlayScreen Screen => screen;

        public IInboundMessageHandler Handler => handler;

        public GamePanelHost Host => screen.PanelHost!;

        public GameSpellbookPage Book => Assert.IsType<GameSpellbookPage>(Assert.Single(screen.PanelControllers!.ControllersOf(13).OfType<GameSpellbookPage>()));

        public GameComponentsPage Components => Assert.IsType<GameComponentsPage>(Assert.Single(screen.PanelControllers!.ControllersOf(13).OfType<GameComponentsPage>()));

        public UiButton Button => Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(SpellsButton));

        public InboundContext Context => new(0, MessageQueue.SmartBox, 0);

        public void Create(CreateObjectMsg message) => handler.OnCreateObject(Context, message, isUpdate: false);

        public void Put(uint item, uint container, uint placement) =>
            handler.OnInventoryPutObjInContainer(Context, new GameEventHeader(Player, ++_event, 0x0022), new InventoryPutObjInContainerEvent(item, container, placement, 0));

        public void Learn(uint spell) => handler.OnMagicUpdateSpell(Context, new GameEventHeader(Player, ++_event, 0x02C1), new MagicUpdateSpellEvent((ushort)spell, 1));

        public void Frames(int n)
        {
            for (var i = 0; i < n; i++)
            {
                rig.Frame();
            }
        }

        public void Open()
        {
            Assert.True(rig.Click(Button));
            Frames(2);
            Assert.Equal(13u, Host.CurrentPanel);
        }

        public void Close()
        {
            Assert.True(rig.Click(Button));
            Frames(2);
        }

        public UiPanel Panel => Assert.IsType<UiPanel>(Host.ElementFor(13));

        public void Dispose() => rig.Dispose();
    }

    private Game Enter(bool tables = true, bool describe = true, IEnumerable<uint>? spells = null, uint filters = 0x3FFF, ImmutableArray<DesiredComponentDesc> desired = default, IEnumerable<uint>? packed = null, Func<IUiImageSource, IUiImageSource>? wrapImages = null)
    {
        IInboundMessageHandler? handler = null;
        var rig = new LoginRig(fixture, h =>
        {
            handler = h;
            StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true, wrapImages: wrapImages);
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
        var game = new Game(rig, screen, handler!);
        if (describe)
        {
            Describe(game, spells ?? [], filters, desired, packed ?? []);
        }

        return game;
    }

    private static void Describe(Game g, IEnumerable<uint> spells, uint filters, ImmutableArray<DesiredComponentDesc> desired, IEnumerable<uint> packed)
    {
        g.Handler.OnPlayerCreate(g.Context, new PlayerCreateMsg(Player));
        g.Create(ReducerHarness.Create(Player, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD), state: PhysicsState.Hidden | PhysicsState.Gravity, name: "Tester"));
        var book = ImmutableDictionary.CreateBuilder<uint, float>();
        foreach (var id in spells)
        {
            book[id] = 2f;
        }

        var contents = packed.Select(static guid => new ContentDesc(guid, 0)).ToImmutableArray();
        g.Handler.OnPlayerDescription(
            g.Context,
            new GameEventHeader(Player, 1, 0x0013),
            new PlayerDescriptionMsg
            {
                Spells = book.ToImmutable(),
                SpellbookFilters = filters,
                DesiredComponents = desired.IsDefault ? [] : desired,
                Inventory = contents,
            });
        g.Frames(2);
    }

    private static CreateObjectMsg Component(uint guid, uint wcid, string name, int stack, uint icon = 0x060011D3)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
        return basis with
        {
            Weenie = basis.Weenie with { WeenieClassId = wcid, IconId = icon, ItemType = ComponentItemType, ContainerId = Player, StackSize = (ushort)stack },
        };
    }

    private static uint WcidOf(uint componentId)
    {
        Assert.True(ComponentWeenies.TryGetByComponent(componentId, out var c));
        return c.Wcid;
    }

    /// <summary>The spells of the table a filter word of 0x3FFF shows, ordered by id: a book of them is never cut by the level rule.</summary>
    private List<SpellInfo> Listable() => [.. Table.Spells.Values.Where(static s => !GameSpellbookPage.IsFilteredOut(0x3FFF, s)).OrderBy(static s => s.Id)];

    private static List<uint> ExpectedOrder(IEnumerable<SpellInfo> spells, uint filters) =>
        [.. spells.Where(s => !GameSpellbookPage.IsFilteredOut(filters, s)).OrderBy(static s => s.DisplayOrder).ThenBy(static s => s.Id).Select(static s => s.Id)];

    // ------------------------------------------------------------------ the filter rule (no dats)

    [Theory]
    [InlineData(0x3FFFu, 4u, 1, false)] // the default word shows everything: creature level 1
    [InlineData(0x3FFFu, 5u, 8, false)] // void level 8
    [InlineData(0x3FFFu, 3u, 9, false)] // item level 9 (bit 0x1000)
    [InlineData(0x3FFEu, 4u, 3, true)] // creature bit cleared
    [InlineData(0x3FF7u, 1u, 3, true)] // war bit cleared
    [InlineData(0x3FFBu, 2u, 3, true)] // life bit cleared
    [InlineData(0x3FFDu, 3u, 3, true)] // item bit cleared
    [InlineData(0x1FFFu, 5u, 3, true)] // void bit cleared
    [InlineData(0x3FEFu, 1u, 1, true)] // level 1 bit cleared
    [InlineData(0x3F7Fu, 1u, 4, true)] // level 4 bit (0x80) cleared
    [InlineData(0x37FFu, 1u, 8, true)] // level 8 bit (0x800) cleared
    [InlineData(0x2FFFu, 1u, 9, true)] // level 9 bit (0x1000) cleared
    [InlineData(0x3FFFu, 1u, 0, true)] // no level by the rough heuristic: filtered out
    [InlineData(0x3FFFu, 1u, 10, true)] // a level past 9: filtered out
    [InlineData(0x3FFFu, 0u, 3, true)] // no school: filtered out
    [InlineData(0xFFFF3FFFu, 1u, 3, false)] // only the low 16 bits count
    public void IsFilteredOut_follows_retails_rule(uint filters, uint school, int level, bool expected) =>
        Assert.Equal(expected, GameSpellbookPage.IsFilteredOut(filters, school, level));

    [Theory]
    [InlineData(1u, 0u)] // the first scarab
    [InlineData(6u, 0u)]
    [InlineData(7u, 1u)] // the first herb
    [InlineData(24u, 1u)]
    [InlineData(25u, 2u)] // the first powder
    [InlineData(37u, 3u)] // the first potion
    [InlineData(49u, 4u)] // the first talisman
    [InlineData(63u, 5u)] // the first taper
    [InlineData(74u, 5u)]
    [InlineData(188u, 5u)] // the Prismatic Taper
    [InlineData(190u, 4u)] // the Banyan Talisman
    [InlineData(300u, 6u)] // not in the map: a pea
    public void A_component_id_has_its_retail_category(uint componentId, uint category) => Assert.Equal((int)category, SpellPageData.CategoryOfComponent(componentId));

    [RequiresLocalDatsFact]
    public void The_category_ranges_match_the_SpellComponentTable_types_of_every_pinned_component()
    {
        var table = SpellComponentTableData.Load(fixture.Dats);
        var byType = new SortedDictionary<string, SortedSet<int>>();
        foreach (var weenie in ComponentWeenies.All)
        {
            var info = table.Components[weenie.ComponentId];
            var key = $"{info.Type}";
            if (!byType.TryGetValue(key, out var set))
            {
                byType[key] = set = [];
            }

            set.Add(SpellPageData.CategoryOfComponent(weenie.ComponentId));
            output.WriteLine($"{weenie.ComponentId,4} {weenie.Name,-24} type={info.Type,-10} dat-category={info.Category} ours={SpellPageData.CategoryOfComponent(weenie.ComponentId)}");
        }

        // Each dat type maps to exactly one of our categories (a type is never split across two), the one retail lists it under (scarabs, herbs, gems = powders, alchemical = potions, talismans, tapers).
        var expected = new Dictionary<string, int> { ["Scarab"] = 0, ["Herb"] = 1, ["Powder"] = 2, ["Potion"] = 3, ["Talisman"] = 4, ["Taper"] = 5 };
        foreach (var (type, categories) in byType)
        {
            Assert.True(categories.Count == 1, $"type {type} is split across categories {string.Join(",", categories)}");
            Assert.True(expected.TryGetValue(type, out var category) && categories.Single() == category, $"type {type} lands in category {categories.Single()}");
        }
    }

    // ------------------------------------------------------------------ the page opens on the spellbook tab

    [RequiresLocalDatsFact]
    public void The_spells_button_opens_the_spell_page_on_the_spellbook_tab_and_the_tab_switch_shows_the_components()
    {
        using var g = Enter();
        Assert.Equal(PanelPageKind.Built, g.Host.KindOf(13));
        Assert.Equal(2, g.Screen.PanelControllers!.ControllersOf(13).Count);
        Assert.Empty(g.Book.ShownSpells); // nothing before the page opens
        g.Open();
        var page = g.Host.ElementFor(13)!;
        Assert.True(page.GetChildRecursive(GameSpellbookPage.TabId)!.Visible); // retail's default tab
        Assert.False(page.GetChildRecursive(GameComponentsPage.TabId)!.Visible);
        Assert.True(g.Panel.OpenTab(ComponentsTab));
        g.Frames(2);
        Assert.False(page.GetChildRecursive(GameSpellbookPage.TabId)!.Visible);
        Assert.True(page.GetChildRecursive(GameComponentsPage.TabId)!.Visible);
        g.Close();
        Assert.False(g.Host.IsOpen);
    }

    // ------------------------------------------------------------------ the spellbook

    [RequiresLocalDatsFact]
    public void The_list_shows_the_known_spells_in_display_order_with_the_spells_icons_and_names()
    {
        var listable = Listable();
        var chosen = listable.Where(static (s, i) => i % 7 == 0).Take(40).ToList();
        using var g = Enter(spells: chosen.Select(static s => s.Id).OrderByDescending(static id => id));
        g.Open();
        g.Frames(12);
        var book = g.Book;
        Assert.False(book.IsFilling);
        var expected = ExpectedOrder(chosen, 0x3FFF);
        Assert.Equal(expected, [.. book.ShownSpells]);
        Assert.Equal(chosen.Count, book.List.CellCount); // no empty cells beyond the spells
        var first = Assert.IsType<UiItem>(book.List.CellAt(0));
        var info = Table.Spells[expected[0]];
        Assert.Equal(expected[0], first.SpellId);
        Assert.False(first.IsEmpty);
        Assert.Equal(UiImageLookup.Ready, g.Rig.Manager.SpellIcons.TryGetSpell(UiSpellIcon.Of(info), out var picture));
        Assert.Same(picture, first.ComposedIcon); // the composed picture (GetSpellIcon, M5 D147), drawn with three-channel alpha
        Assert.Same(picture, first.IconElement!.GeneratedImage);
        Assert.Equal(UiBlitMode.ThreeChannelAlpha, first.IconElement.Image.Mode);
        Assert.Equal(info.Name, g.Rig.Session.Spell(expected[0])!.Name);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], book.List.CellAt(i)!.SpellId);
        }
    }

    [RequiresLocalDatsFact]
    public void The_filter_buttons_show_the_books_word_and_a_click_hides_and_shows_a_school_without_a_server()
    {
        var listable = Listable();
        var chosen = listable.GroupBy(static s => s.School).SelectMany(static g => g.Take(12)).ToList();
        Assert.Contains(chosen, static s => (uint)s.School == 1);
        Assert.Contains(chosen, static s => (uint)s.School == 4);
        using var g = Enter(spells: chosen.Select(static s => s.Id), filters: 0x3FF7); // war off
        g.Open();
        g.Frames(10);
        var book = g.Book;
        Assert.Equal(0x3FF7u, book.Filters);
        Assert.Equal(UiStateIds.Normal, book.ButtonFor(0x8)!.State); // ResetFilterButtons: bit clear is state 1
        Assert.Equal(UiStateIds.Highlight, book.ButtonFor(0x1)!.State); // bit set is state 6
        Assert.Equal(UiStateIds.Highlight, book.ButtonFor(0x2000)!.State);
        Assert.Equal(UiStateIds.Highlight, book.ButtonFor(0x800)!.State);
        Assert.Equal(ExpectedOrder(chosen, 0x3FF7), [.. book.ShownSpells]);
        Assert.DoesNotContain(book.ShownSpells, id => (uint)Table.Spells[id].School == 1);

        // Clicking the war button sets its bit again: the war spells are back, in sorted place, and nothing was sent.
        var plans = book.Plans;
        Assert.True(g.Rig.Click(Assert.IsAssignableFrom<UiButton>(g.Host.ElementFor(13)!.GetChildRecursive(WarButton))));
        g.Frames(10);
        Assert.Equal(0x3FFFu, book.Filters);
        Assert.True(book.Plans > plans);
        Assert.Equal(ExpectedOrder(chosen, 0x3FFF), [.. book.ShownSpells]);

        // Clicking creature off removes its spells and trims the surplus cells.
        Assert.True(g.Rig.Click(Assert.IsAssignableFrom<UiButton>(g.Host.ElementFor(13)!.GetChildRecursive(CreatureButton))));
        g.Frames(10);
        Assert.Equal(0x3FFEu, book.Filters);
        var expected = ExpectedOrder(chosen, 0x3FFE);
        Assert.Equal(expected, [.. book.ShownSpells]);
        Assert.Equal(expected.Count, book.List.CellCount);
        Assert.DoesNotContain(book.ShownSpells, id => (uint)Table.Spells[id].School == 4);
        Assert.Equal(PanelPageKind.Built, g.Host.KindOf(13));
    }

    [RequiresLocalDatsFact]
    public void A_level_filter_hides_a_level_and_the_book_a_new_word_replaces_the_session_local_one()
    {
        var listable = Listable();
        var chosen = listable.Take(120).ToList();
        using var g = Enter(spells: chosen.Select(static s => s.Id));
        g.Open();
        g.Frames(20);
        var book = g.Book;
        var level1 = chosen.Where(static s => SpellFormulaRules.LevelByRoughHeuristic(s.Formula) == 1).Select(static s => s.Id).ToList();
        Assert.NotEmpty(level1);
        Assert.True(g.Rig.Click(Assert.IsAssignableFrom<UiButton>(g.Host.ElementFor(13)!.GetChildRecursive(Level1Button))));
        g.Frames(20);
        Assert.Equal(0x3FEFu, book.Filters);
        Assert.Empty(book.ShownSpells.Intersect(level1));
        Assert.Equal(ExpectedOrder(chosen, 0x3FEF), [.. book.ShownSpells]);
    }

    /// <summary>M3 2.2 fix B (2.2-R9): a bound on the bytes one fill unit allocates (eight cells' worth of cells, texts and icons; about 100 KB measured, 2.5 times that allowed); measured with <see cref="GC.GetAllocatedBytesForCurrentThread"/>, which a parallel run does not disturb.</summary>
    private const long FillUnitAllocationBound = 250_000;

    [RequiresLocalDatsFact]
    public void A_book_of_247_spells_is_written_over_frames_in_bounded_units_of_work()
    {
        // M3 2.2 fix B (2.2-R9): the work is judged, not the clock. Every unit writes at most SpellsPerFrame spells and creates at most the cells those need; the fill takes ceil(247 / SpellsPerFrame) units give or take one; a unit
        // allocates under a byte bound. The times are printed, report-only (a collector pause under a parallel run made them flaky).
        var listable = Listable();
        Assert.True(listable.Count >= 247, $"the table lists {listable.Count} spells the default filter shows");
        var chosen = listable.Take(247).ToList();
        using var g = Enter(spells: chosen.Select(static s => s.Id));
        var book = g.Book;
        Assert.Equal(0, book.List.CellCount); // the controller's construction made nothing

        // Open it and look at the first frame: some of the spells, not all (that frame is the first unit).
        Assert.True(g.Rig.Click(g.Button));
        g.Rig.Frame();
        var early = book.ShownSpells.Count();
        Assert.InRange(early, 1, GameSpellbookPage.SpellsPerFrame);
        Assert.True(book.IsFilling);
        var units = 1;
        var occupied = book.List.OccupiedCount;
        var cells = book.List.CellCount;
        Assert.True(occupied <= GameSpellbookPage.SpellsPerFrame && cells <= occupied + GameSpellbookPage.SpellsPerFrame, $"first unit: {occupied} written, {cells} cells");

        // Three whole shell frames, then each unit by itself: the page's fill step, called directly so only it is measured.
        var worst = 0.0;
        var worstFrame = 0.0;
        var worstBytes = 0L;
        var times = new List<double>();
        while (book.IsFilling && units < 200)
        {
            var wholeFrame = units < 4;
            var bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            if (wholeFrame)
            {
                g.Rig.Frame();
            }
            else
            {
                book.FillSome();
            }

            var ms = sw.Elapsed.TotalMilliseconds;
            var bytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
            units++;
            if (wholeFrame)
            {
                worstFrame = Math.Max(worstFrame, ms);
            }
            else
            {
                worst = Math.Max(worst, ms);
                worstBytes = Math.Max(worstBytes, bytes);
                times.Add(ms);
                Assert.True(bytes < FillUnitAllocationBound, $"unit {units} allocated {bytes} bytes (bound {FillUnitAllocationBound})");
            }

            var written = book.List.OccupiedCount - occupied;
            var created = book.List.CellCount - cells;
            Assert.InRange(written, 0, GameSpellbookPage.SpellsPerFrame); // at most SpellsPerFrame spells a unit
            Assert.InRange(created, 0, Math.Max(written, GameSpellbookPage.SpellsPerFrame)); // and no more cells than it needs: the cells it made hold what it wrote
            occupied = book.List.OccupiedCount;
            cells = book.List.CellCount;
        }

        times.Sort();
        output.WriteLine($"247 spells: first frame {early}, then {units} units in all (expected {(247 + GameSpellbookPage.SpellsPerFrame - 1) / GameSpellbookPage.SpellsPerFrame} give or take one), the most written in one unit {book.MostWrittenInAFrame}; report-only: the longest fill unit {worst:0.00} ms, median {(times.Count > 0 ? times[times.Count / 2] : 0):0.00} ms, the longest whole shell frame while filling {worstFrame:0.00} ms, the most one unit allocated {worstBytes} bytes");
        Assert.False(book.IsFilling);
        Assert.InRange(units, ((247 + GameSpellbookPage.SpellsPerFrame - 1) / GameSpellbookPage.SpellsPerFrame) - 1, ((247 + GameSpellbookPage.SpellsPerFrame - 1) / GameSpellbookPage.SpellsPerFrame) + 1);
        Assert.True(book.MostWrittenInAFrame <= GameSpellbookPage.SpellsPerFrame);
        Assert.Equal(ExpectedOrder(chosen, 0x3FFF), [.. book.ShownSpells]);
        Assert.Equal(247, book.List.OccupiedCount);
        Assert.Equal(247, book.List.CellCount); // no cell beyond the 247 the book needs
    }

    /// <summary>The game's image source decodes on a worker: every DID in <see cref="Held"/> answers pending until it is released.</summary>
    private sealed class HoldingImages(IUiImageSource inner) : IUiImageSource
    {
        public HashSet<uint> Held { get; } = [];

        public UiImageLookup TryGetImage(uint did, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out UiImage? image)
        {
            if (Held.Contains(did))
            {
                image = null;
                return UiImageLookup.Pending;
            }

            return inner.TryGetImage(did, out image);
        }
    }

    [RequiresLocalDatsFact]
    public void The_first_frame_after_open_paints_the_page_and_no_cell_is_written_ahead_of_its_icon()
    {
        // m3-spellbook-first-paint: the compositor withholds a surface while an image in its dirty area is pending, so a cell written before its icon decoded left the previous page on screen.
        // The fill now writes a cell only when its icon is ready: with every icon still decoding, the page is open and its frame is built (the list is there, shown, empty) and no cell holds an undecoded icon.
        var chosen = Listable().Take(60).ToList();
        HoldingImages? images = null;
        using var g = Enter(spells: chosen.Select(static s => s.Id), wrapImages: inner => images = new HoldingImages(inner));
        Assert.NotNull(images);
        foreach (var spell in chosen)
        {
            images.Held.Add(spell.IconId);
        }

        Assert.True(g.Rig.Click(g.Button));
        g.Rig.Frame();
        Assert.Equal(13u, g.Host.CurrentPanel);
        Assert.True(g.Panel.Visible); // the page area shows the spellbook's frame from the first frame
        Assert.True(g.Book.List.Visible);
        Assert.True(g.Book.IsFilling);
        g.Frames(5);
        Assert.Equal(0, g.Book.List.OccupiedCount); // nothing is written while its icon is pending
        Assert.Equal(0, g.Book.List.CellCount);

        // The icons decode a few at a time: the fill follows them, never ahead.
        var released = 0;
        while (released < chosen.Count)
        {
            for (var i = 0; i < 5 && released < chosen.Count; i++)
            {
                images.Held.Remove(chosen[released++].IconId);
            }

            g.Frames(1);
            foreach (var shown in g.Book.ShownSpells)
            {
                var icon = chosen.First(c => c.Id == shown).IconId;
                Assert.DoesNotContain(icon, images.Held);
            }
        }

        g.Frames(20);
        Assert.False(g.Book.IsFilling);
        Assert.Equal(ExpectedOrder(chosen, 0x3FFF), [.. g.Book.ShownSpells]);
    }

    [RequiresLocalDatsFact]
    public void The_list_waits_for_the_SpellTable_and_fills_when_it_arrives_without_a_guess()
    {
        var chosen = Listable().Take(30).ToList();
        using var g = Enter(tables: false, spells: chosen.Select(static s => s.Id));
        g.Open();
        g.Frames(6);
        var book = g.Book;
        Assert.True(book.AwaitingSpellTable);
        Assert.Empty(book.ShownSpells); // the table has not loaded: no cell, no placeholder name
        Assert.Equal(0, book.List.OccupiedCount);

        g.Rig.Session.UseSpellsFrom(fixture.Dats);
        g.Rig.Session.SpellTableLoaded.Wait();
        g.Frames(10);
        Assert.False(book.AwaitingSpellTable);
        Assert.Equal(ExpectedOrder(chosen, 0x3FFF), [.. book.ShownSpells]);
    }

    [RequiresLocalDatsFact]
    public void A_learned_spell_joins_in_place_selected_and_a_click_selects_a_cell_and_delete_answers_NotInM3()
    {
        var chosen = Listable().Take(30).ToList();
        var known = chosen.Take(29).ToList();
        using var g = Enter(spells: known.Select(static s => s.Id));
        g.Open();
        g.Frames(10);
        var book = g.Book;
        Assert.Equal(29, book.List.OccupiedCount);

        var learned = chosen[29];
        g.Learn(learned.Id);
        g.Frames(10);
        Assert.Equal(ExpectedOrder(chosen, 0x3FFF), [.. book.ShownSpells]);
        Assert.Equal(learned.Id, book.SelectedSpell); // RecvNotice_SpellAdded selects it
        var cell = book.List.Items.OfType<UiItem>().Single(c => c.SpellId == learned.Id);
        Assert.True(cell.IsSelected);

        // A click on another cell moves the selection (SetSelected).
        var at = book.List.Items.ToList().IndexOf(cell); // the list scrolled to the new spell: its neighbour is in view
        var other = Assert.IsType<UiItem>(book.List.CellAt(at + 1) ?? book.List.CellAt(at - 1));
        Assert.True(g.Rig.Click(other));
        g.Frames(2);
        Assert.Equal(other.SpellId, book.SelectedSpell);
        Assert.True(other.IsSelected);
        Assert.False(cell.IsSelected);

        // Delete spell is not in M3.
        var delete = Assert.IsAssignableFrom<UiButton>(g.Host.ElementFor(13)!.GetChildRecursive(GameSpellbookPage.DeleteButtonId));
        Assert.True(g.Rig.Click(delete));
        g.Frames(2);
        var dialog = Assert.Single(g.Rig.Manager.Dialogs.OpenDialogs);
        Assert.Equal(NotInM3.Prefix + "Delete spell", Assert.IsType<UiTextElement>(dialog.GetChildRecursive(UiDialog.TextElement)).Text);
    }

    [RequiresLocalDatsFact]
    public void Nothing_is_listed_before_the_description_and_a_closed_page_does_nothing()
    {
        using var g = Enter(describe: false);
        g.Open();
        g.Frames(4);
        Assert.Empty(g.Book.ShownSpells);
        Assert.False(g.Book.AwaitingSpellTable);
        g.Close();
        var plans = g.Book.Plans;
        g.Frames(10);
        Assert.Equal(plans, g.Book.Plans);
    }

    // ------------------------------------------------------------------ the components

    [RequiresLocalDatsFact]
    public void The_components_tab_lists_the_packs_components_by_category_with_summed_counts_and_the_desired_amounts()
    {
        var scarab = WcidOf(1);
        var herb = WcidOf(7);
        var taper = WcidOf(63);
        uint[] guids = [0x80000201, 0x80000202, 0x80000203, 0x80000204];
        using var g = Enter(desired: [new DesiredComponentDesc(scarab, 50), new DesiredComponentDesc(taper, 200)], packed: guids);
        g.Create(Component(guids[0], scarab, "Lead Scarab", 30));
        g.Create(Component(guids[1], scarab, "Lead Scarab", 12));
        g.Create(Component(guids[2], herb, "Hyssop", 5));
        g.Create(Component(guids[3], taper, "Red Taper", 100));
        g.Frames(2);
        g.Open();
        Assert.True(g.Panel.OpenTab(ComponentsTab));
        g.Frames(14);
        var page = g.Components;
        Assert.False(page.IsBuilding);
        var rows = page.Rows.ToList();
        Assert.Equal(
            [(true, 0, 0u), (false, 0, scarab), (true, 1, 0u), (false, 1, herb), (true, 5, 0u), (false, 5, taper)],
            rows.Select(static r => (r.IsTitle, r.Category, r.Wcid)).ToList());
        Assert.All(rows.Where(static r => r.IsTitle), r => Assert.False(string.IsNullOrEmpty(r.Name)));
        output.WriteLine("titles: " + string.Join(" | ", rows.Where(static r => r.IsTitle).Select(static r => r.Name)));
        var scarabRow = rows.Single(r => r.Wcid == scarab);
        Assert.Equal(("Lead Scarab", "42", "50"), (scarabRow.Name, scarabRow.Count, scarabRow.BuyRate)); // 30 + 12 in one row
        var herbRow = rows.Single(r => r.Wcid == herb);
        Assert.Equal(("Hyssop", "5", "0"), (herbRow.Name, herbRow.Count, herbRow.BuyRate));
        var taperRow = rows.Single(r => r.Wcid == taper);
        Assert.Equal(("Red Taper", "100", "200"), (taperRow.Name, taperRow.Count, taperRow.BuyRate));
    }

    [RequiresLocalDatsFact]
    public void A_row_draws_the_SpellComponentTables_icon_through_the_session_and_logs_no_TODO()
    {
        // M4 4.1 fix (review R9): GameSession implements ISpellComponentIcons (the table is loaded where the stat tables are), so gmSpellComponentUI::MakeIcon (0x0048A250) draws the table's icon and not the pack object's.
        var herb = WcidOf(7);
        using var g = Enter(packed: [0x80000221, 0x80000222]);
        g.Create(Component(0x80000221, herb, "Hyssop", 5, icon: 0x060011D7));
        g.Create(Component(0x80000222, WcidOf(8), "Mandrake", 3, icon: 0x060011D8));
        g.Frames(2);
        g.Open();
        Assert.True(g.Panel.OpenTab(ComponentsTab));
        g.Frames(14);
        var first = g.Components.Rows.First(static r => !r.IsTitle);
        Lifestone.Client.Session.Contracts.SpellPageData.CategoryOf(first.Wcid, out var componentId, out var known);
        Assert.True(known);
        var expected = Lifestone.Data.Magic.SpellComponentTableData.Load(fixture.Dats).Components[componentId].IconId;
        Assert.NotEqual(0u, expected);
        Assert.NotEqual(0x060011D7u, expected); // the table's icon, not the pack object's
        var icon = g.Screen.Root!.GetChildRecursive(GameComponentsPage.RowIconId);
        Assert.NotNull(icon);
        Assert.Equal(UiImageLookup.Ready, g.Rig.Manager.SpellIcons.TryGetComponent(expected, out var picture));
        Assert.Same(picture, icon!.GeneratedImage); // MakeIcon over CompositeSpellComponentIcon (0x005684C0): the table's icon with its white made black (M5 D147)
        Assert.Equal(UiBlitMode.ThreeChannelAlpha, icon.Image.Mode);
        Assert.DoesNotContain(Enumerable.Range(0, 1024), p => picture!.Pixels[p * 4] == 255 && picture.Pixels[(p * 4) + 1] == 255 && picture.Pixels[(p * 4) + 2] == 255 && picture.Pixels[(p * 4) + 3] == 255);
        Assert.DoesNotContain(g.Rig.Log.Entries, static e => e.Message.Contains("TODO(m3-component-icon)", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void The_components_follow_the_pack_live_a_count_rewrites_its_text_and_a_new_class_builds_the_rows_again()
    {
        var herb = WcidOf(7);
        var powder = WcidOf(25);
        using var g = Enter(packed: [0x80000211]);
        g.Create(Component(0x80000211, herb, "Hyssop", 5));
        g.Frames(2);
        g.Open();
        Assert.True(g.Panel.OpenTab(ComponentsTab));
        g.Frames(10);
        var page = g.Components;
        Assert.Equal([(true, 1, 0u), (false, 1, herb)], page.Rows.Select(static r => (r.IsTitle, r.Category, r.Wcid)).ToList());
        var rebuilds = page.Rebuilds;
        var redraws = page.Redraws;

        // Another stack of the same herb: the count changes, the rows do not.
        g.Create(Component(0x80000212, herb, "Hyssop", 4));
        g.Put(0x80000212, Player, 1);
        g.Frames(4);
        Assert.True(page.Redraws > redraws);
        Assert.Equal(rebuilds, page.Rebuilds);
        Assert.Equal("9", page.Rows.Single(static r => !r.IsTitle).Count);

        // A powder: a new category and a new row.
        g.Create(Component(0x80000213, powder, "Powdered Agate", 7));
        g.Put(0x80000213, Player, 2);
        g.Frames(8);
        Assert.Equal(rebuilds + 1, page.Rebuilds);
        Assert.Equal([(true, 1, 0u), (false, 1, herb), (true, 2, 0u), (false, 2, powder)], page.Rows.Select(static r => (r.IsTitle, r.Category, r.Wcid)).ToList());
        Assert.Equal("7", page.Rows.Single(r => r.Wcid == powder).Count);

        // A closed page redraws nothing.
        g.Close();
        var closed = page.Redraws;
        g.Create(Component(0x80000214, herb, "Hyssop", 1));
        g.Put(0x80000214, Player, 3);
        g.Frames(6);
        Assert.Equal(closed, page.Redraws);
        Assert.Equal("9", page.Rows.Single(r => r.Wcid == herb).Count); // still the old count: drawn when it shows again
    }

    [RequiresLocalDatsFact]
    public void Typing_a_buy_rate_answers_NotInM3_and_puts_the_amount_back()
    {
        var scarab = WcidOf(1);
        using var g = Enter(desired: [new DesiredComponentDesc(scarab, 50)], packed: [0x80000221]);
        g.Create(Component(0x80000221, scarab, "Lead Scarab", 3));
        g.Frames(2);
        g.Open();
        Assert.True(g.Panel.OpenTab(ComponentsTab));
        g.Frames(10);
        var page = g.Components;
        var row = Assert.IsAssignableFrom<UiElement>(page.RowOf(scarab));
        var box = Assert.IsType<UiTextElement>(row.GetChildRecursive(GameComponentsPage.RowBuyRateId));
        Assert.Equal("50", box.Text);
        box.SetText("75");
        page.BuyRateEdited(scarab, "75");
        g.Frames(2);
        Assert.Equal("50", box.Text);
        var dialog = Assert.Single(g.Rig.Manager.Dialogs.OpenDialogs);
        Assert.Equal(NotInM3.Prefix + "Component buy rate", Assert.IsType<UiTextElement>(dialog.GetChildRecursive(UiDialog.TextElement)).Text);
    }

    [RequiresLocalDatsFact]
    public void A_buy_rate_outside_0_to_5000_shows_as_0_and_an_empty_pack_lists_nothing()
    {
        var scarab = WcidOf(1);
        using var g = Enter(desired: [new DesiredComponentDesc(scarab, 9000)], packed: [0x80000231]);
        g.Create(Component(0x80000231, scarab, "Lead Scarab", 3));
        g.Frames(2);
        g.Open();
        Assert.True(g.Panel.OpenTab(ComponentsTab));
        g.Frames(10);
        Assert.Equal("0", g.Components.Rows.Single(static r => !r.IsTitle).BuyRate);

        using var empty = Enter();
        empty.Open();
        Assert.True(empty.Panel.OpenTab(ComponentsTab));
        empty.Frames(6);
        Assert.Empty(empty.Components.Rows);
    }

    [RequiresLocalDatsFact]
    public void The_controllers_are_built_in_staged_units_under_8_ms()
    {
        var sw = Stopwatch.StartNew();
        using var g = Enter(describe: false);
        output.WriteLine($"shell with both controllers entered in {sw.ElapsedMilliseconds} ms");
        Assert.Equal(PanelPageKind.Built, g.Host.KindOf(13));

        // A controller's construction alone: finds its elements, subscribes, builds no cell and no row.
        var context = g.Screen.PanelControllers!.Context;
        var samples = new double[8];
        for (var i = 0; i < samples.Length; i++)
        {
            var one = Stopwatch.StartNew();
            using var book = GameSpellbookPage.Create(context, g.Host);
            using var components = GameComponentsPage.Create(context, g.Host);
            samples[i] = one.Elapsed.TotalMilliseconds;
            Assert.NotNull(book);
            Assert.NotNull(components);
        }

        output.WriteLine($"both controllers constructed: {string.Join(", ", samples.Select(static s => s.ToString("0.00")))} ms");
        Assert.True(samples.Skip(1).Max() < 8.0);
    }
}
