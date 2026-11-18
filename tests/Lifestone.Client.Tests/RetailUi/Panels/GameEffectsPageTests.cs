using System.Collections.Immutable;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Indicators;
using Lifestone.Client.RetailUi.Options;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The effects pages 4 and 5, the vitae page 15, the effects, vitae and burden indicators and character information's refresh (<see cref="GameEffectsPage"/>, <see cref="GameVitaePage"/>,
/// <see cref="GameIndicators"/>; M3 2.2 a3) over the real layout and scripted registries: the beneficial and harmful split, retail's name order, the surpassed entries left out, a registry of 24 buffs of about an
/// hour (BSD Mage's), the countdown rewritten only when a whole second changes and not while hidden, the vitae text and its "no vitae" state, the indicators lit from the registry, and their clicks.
/// </summary>
public sealed class GameEffectsPageTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint AdditiveAttribute = 0x2000000 | EnchantmentRules.Additive | 0x1000 | EnchantmentRules.AttributeKind;
    private const uint BeneficialPage = 4, HarmfulPage = 5, VitaePage = 15, CharacterInfoPage = 3;

    private static readonly InboundContext Context = new(0, MessageQueue.SmartBox, 0);
    private static readonly GameEventHeader Header = new(1, 1, 0x02C2);

    private SpellTableData Table => _table ??= SpellTableData.Load(fixture.Dats);

    private SpellTableData? _table;

    /// <summary>Spells of one kind, one per spell category and per name, in id order, with ids a registry may hold.</summary>
    private List<SpellInfo> Spells(bool beneficial, int count) =>
        [.. Table.Spells.Values
            .Where(s => s.Id < EffectRules.FirstNonSpellId && !string.IsNullOrEmpty(s.Name) && ((s.Flags & SpellFlags.Beneficial) != 0) == beneficial)
            .GroupBy(static s => s.Category).Select(static g => g.OrderBy(static s => s.Id).First())
            .GroupBy(static s => s.Name).Select(static g => g.OrderBy(static s => s.Id).First())
            .OrderBy(static s => s.Id).Take(count)];

    private static EnchantmentDesc Ench(SpellInfo spell, double duration, ushort layer = 1, uint power = 100, double start = 0) =>
        new((ushort)spell.Id, layer, (ushort)spell.Category, 0, power, start, duration, 0x50000001, 0f, 0f, 0.0, AdditiveAttribute, 1, 1f, 0, EnchantmentBucket.Additive);

    private static EnchantmentDesc Vitae(float value) =>
        new(666, 1, 666, 0, 0, 0.0, -1.0, 0x50000001, 0f, 0f, 0.0, EnchantmentRules.Multiplicative, 0, value, 0, EnchantmentBucket.Vitae);

    private PlayerDescriptionMsg Description(IEnumerable<EnchantmentDesc>? enchantments = null, int burden = 0, int level = 50, int pool = 0, uint options = StandInPlayerStats.AceDefaultOptions1)
    {
        var list = (enchantments ?? []).ToImmutableArray();
        var mask = 0u;
        foreach (var e in list)
        {
            mask |= (uint)e.Bucket;
        }

        return new PlayerDescriptionMsg
        {
            Options1 = options,
            Options2 = PlayerOptionTable.DefaultWord2,
            IntProperties = ImmutableDictionary<uint, int>.Empty.Add(24, 5).Add(PlayerQualities.LevelProperty, level).Add(5, burden).Add(EffectRules.VitaeCpPoolProperty, pool),
            Int64Properties = ImmutableDictionary<uint, long>.Empty.Add(2, 1000).Add(1, 5_000_000),
            Attributes =
            [
                new AttributeDesc(1, 0, 90, 0), new AttributeDesc(2, 0, 80, 0), new AttributeDesc(3, 0, 80, 0),
                new AttributeDesc(4, 0, 90, 0), new AttributeDesc(5, 0, 70, 0), new AttributeDesc(6, 0, 100, 0),
            ],
            Vitals = [new VitalDesc(1, 5, 0, 0, 52), new VitalDesc(3, 0, 0, 0, 120), new VitalDesc(5, 0, 0, 0, 75)],
            Enchantments = list,
            EnchantmentMask = mask,
        };
    }

    private sealed record Setup(LoginRig Rig, GamePlayScreen Screen, IInboundMessageHandler Handler) : IDisposable
    {
        public GamePanelHost Host => Screen.PanelHost!;

        public GameEffectsPage Beneficial => Screen.PanelControllers!.ControllersOf(BeneficialPage).OfType<GameEffectsPage>().Single();

        public GameEffectsPage Harmful => Screen.PanelControllers!.ControllersOf(HarmfulPage).OfType<GameEffectsPage>().Single();

        public GameVitaePage VitaePage => Screen.PanelControllers!.ControllersOf(15).OfType<GameVitaePage>().Single();

        public GameIndicators Indicators => Screen.IndicatorsUi!;

        public void Dispose() => Rig.Dispose();
    }

    private Setup Enter(PlayerDescriptionMsg? description = null, bool spells = true, UiRetailGlitches glitches = UiRetailGlitches.None)
    {
        IInboundMessageHandler? handler = null;
        var rig = new LoginRig(fixture, h =>
        {
            handler = h;
            StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true, glitches: glitches);
        if (spells)
        {
            rig.Session.UseSpellsFrom(fixture.Dats);
            Assert.True(rig.Session.SpellTableLoaded.Wait(TimeSpan.FromSeconds(60)), "the SpellTable did not load");
        }

        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        var screen = Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        if (description is not null)
        {
            handler!.OnPlayerDescription(Context, new GameEventHeader(1, 1, 0x0013), description);
            rig.Frame();
        }

        return new Setup(rig, screen, handler!);
    }

    private static void Open(Setup s, uint panel)
    {
        Assert.True(s.Host.Toggle(panel));
        s.Rig.Frame();
        Assert.Equal(panel, s.Host.CurrentPanel);
    }

    private static void Close(Setup s, uint panel)
    {
        Assert.True(s.Host.Toggle(panel));
        s.Rig.Frame();
        Assert.NotEqual(panel, s.Host.CurrentPanel);
    }

    private static void Add(Setup s, EnchantmentDesc enchantment) => s.Handler.OnMagicUpdateEnchantment(Context, Header, new MagicUpdateEnchantmentEvent(enchantment));

    // ------------------------------------------------------------------ the registry's split, order and times

    [RequiresLocalDatsFact]
    public void The_pages_are_built_in_staged_units_and_list_nothing_until_they_show()
    {
        var good = Spells(true, 3);
        using var s = Enter(Description(good.Select(g => Ench(g, 600.5))));
        Assert.Equal(BeneficialPage, s.Beneficial.PanelId);
        Assert.Equal(HarmfulPage, s.Harmful.PanelId);
        Assert.True(s.Beneficial.IsBeneficialPage);
        Assert.False(s.Harmful.IsBeneficialPage);
        Assert.Equal(0, s.Beneficial.Rebuilds);
        Assert.Equal(0, s.Beneficial.LineCount);
        Assert.Equal(PanelPageKind.Built, s.Host.KindOf(BeneficialPage));
        Assert.Equal(PanelPageKind.Built, s.Host.KindOf(HarmfulPage));
        Assert.Equal(PanelPageKind.Built, s.Host.KindOf(VitaePage));
        Open(s, BeneficialPage);
        Assert.Equal(1, s.Beneficial.Rebuilds);
        Assert.Equal(3, s.Beneficial.LineCount);
        Assert.Equal(0, s.Harmful.Rebuilds); // the other page did not show
    }

    [RequiresLocalDatsFact]
    public void Twenty_four_buffs_of_about_an_hour_list_in_name_order_with_icons_names_and_times()
    {
        var spells = Spells(true, 24);
        Assert.Equal(24, spells.Count);
        var entries = spells.Select((spell, i) => Ench(spell, 3600.75 - (i * 61))).ToList();
        using var s = Enter(Description(entries.AsEnumerable().Reverse())); // the registry order is not the name order
        Open(s, BeneficialPage);
        var page = s.Beneficial;
        Assert.Equal(24, page.LineCount);
        var expected = spells.Select((spell, i) => (spell, remaining: (long)(3600.75 - (i * 61) - (3 * LoginRig.Step)))).OrderBy(static t => t.spell.Name, StringComparer.Ordinal).ToList();
        var lines = page.Lines.ToList();
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].spell.Id, lines[i].SpellId);
            Assert.Equal(expected[i].spell.Name, lines[i].Name);
            Assert.Equal(EffectRules.FormatDuration(expected[i].remaining), lines[i].Time);
            var item = Assert.IsAssignableFrom<UiElement>(page.ItemOf(expected[i].spell.Id));
            var icon = item.GetChildRecursive(GameEffectsPage.RowIconId)!;
            Assert.Equal(Lifestone.Client.RetailUi.Rendering.UiImageLookup.Ready, s.Rig.Manager.SpellIcons.TryGetSpell(UiSpellIcon.Of(expected[i].spell), out var picture));
            Assert.Same(picture, icon.GeneratedImage); // EffectInfoRegion: the composed picture (GetSpellIcon, M5 D147), not the raw icon
            Assert.Equal(Lifestone.Client.RetailUi.Rendering.UiBlitMode.ThreeChannelAlpha, icon.Image.Mode);
            Assert.Equal(expected[i].spell.Name, Assert.IsAssignableFrom<Lifestone.Client.RetailUi.Text.UiTextElement>(item.GetChildRecursive(GameEffectsPage.RowNameId)).Text);
        }

        output.WriteLine(string.Join(" | ", lines.Take(4).Select(static l => $"{l.Name} {l.Time}")));
        Assert.Equal("1:00:00", EffectRules.FormatDuration(3600));
        Assert.Equal("58:59", EffectRules.FormatDuration(3539));
        Assert.Equal("0:09", EffectRules.FormatDuration(9));
    }

    [RequiresLocalDatsFact]
    public void The_registry_splits_into_the_beneficial_and_the_harmful_page()
    {
        var good = Spells(true, 5);
        var bad = Spells(false, 4);
        Assert.Equal(4, bad.Count);
        using var s = Enter(Description(good.Concat(bad).Select(spell => Ench(spell, 900.5))));
        Open(s, BeneficialPage);
        Assert.Equal(good.Select(static g => g.Id).Order(), s.Beneficial.Lines.Select(static l => l.SpellId).Order());
        Close(s, BeneficialPage);
        Open(s, HarmfulPage);
        Assert.Equal(bad.Select(static g => g.Id).Order(), s.Harmful.Lines.Select(static l => l.SpellId).Order());
        Assert.Equal(4, s.Harmful.LineCount);
    }

    [RequiresLocalDatsFact]
    public void An_entry_a_stronger_one_surpasses_is_not_listed_and_the_layers_of_one_spell_count_once()
    {
        var good = Spells(true, 3);
        var rival = good[0];
        // layers 1 to 3 of one spell (power rising), and a second spell of the first one's category that is stronger than all, and a third that is weaker than the rest
        var layers = new[] { Ench(rival, 600.5, 1, 50), Ench(rival, 600.5, 2, 70), Ench(rival, 600.5, 3, 60) };
        using var s = Enter(Description([.. layers, Ench(good[1], 600.5), Ench(good[2], 600.5)]));
        Open(s, BeneficialPage);
        Assert.Equal(3, s.Beneficial.LineCount); // one line for the spell whatever its layers, not five
        Assert.Equal(good.Select(static g => g.Id).Order(), s.Beneficial.Lines.Select(static l => l.SpellId).Order());
        var q = s.Rig.Session.State.Self.Qualities;
        var inEffect = new List<ActiveEnchantment>();
        Assert.Equal(3, EffectRules.InEffect(q, inEffect));
        Assert.Equal((ushort)2, inEffect.Single(static e => e.Enchantment.PowerLevel == 70).Layer); // the strongest layer is the one in effect
    }

    [RequiresLocalDatsFact]
    public void A_spell_category_duel_keeps_the_higher_power_and_at_equal_power_the_later_start()
    {
        var good = Spells(true, 2);
        // two spells of ONE category: the higher power wins, whichever came first
        var a = Ench(good[0], 600.5, 1, 80) with { SpellCategory = 4000 };
        var b = Ench(good[1], 600.5, 1, 50) with { SpellCategory = 4000 };
        using var s = Enter(Description([b, a]));
        Open(s, BeneficialPage);
        Assert.Equal([good[0].Id], s.Beneficial.Lines.Select(static l => l.SpellId));
        // equal power: the one that started later holds
        var c = Ench(good[1], 600.5, 1, 80, start: -30) with { SpellCategory = 4000 };
        Add(s, c);
        s.Rig.Frame();
        Assert.Equal([good[0].Id], s.Beneficial.Lines.Select(static l => l.SpellId));
    }

    [RequiresLocalDatsFact]
    public void A_permanent_or_overdue_entry_shows_no_time_and_the_option_hides_every_time()
    {
        var good = Spells(true, 3);
        using var s = Enter(Description([Ench(good[0], -1.0), Ench(good[1], 0.25), Ench(good[2], 600.5)]));
        Open(s, BeneficialPage);
        s.Rig.Run(1.0);
        var times = s.Beneficial.Lines.ToDictionary(static l => l.SpellId, static l => l.Time);
        Assert.Equal(string.Empty, times[good[0].Id]); // permanent
        Assert.Equal(string.Empty, times[good[1].Id]); // past its end
        Assert.NotEqual(string.Empty, times[good[2].Id]);
        Close(s, BeneficialPage);

        using var off = Enter(Description([Ench(good[2], 600.5)], options: StandInPlayerStats.AceDefaultOptions1 & ~EffectRules.SpellDurationOption));
        Open(off, BeneficialPage);
        Assert.Equal(string.Empty, Assert.Single(off.Beneficial.Lines).Time); // PlayerModule::SpellDuration is off
    }

    // ------------------------------------------------------------------ the countdown and the redraw

    [RequiresLocalDatsFact]
    public void The_countdown_is_rewritten_only_when_a_whole_second_changes_and_not_while_hidden()
    {
        var good = Spells(true, 1);
        using var s = Enter(Description([Ench(good[0], 600.5)]));
        Open(s, BeneficialPage);
        var page = s.Beneficial;
        var first = Assert.Single(page.Lines).Time;
        var writes = page.TimeWrites;
        var rebuilds = page.Rebuilds;
        s.Rig.Run(1.0); // sixty frames cross one second boundary
        Assert.Equal(writes + 1, page.TimeWrites);
        var second = Assert.Single(page.Lines).Time;
        Assert.NotEqual(first, second);
        writes = page.TimeWrites;
        s.Rig.Run(0.25); // fifteen frames cross at most one
        Assert.InRange(page.TimeWrites - writes, 0, 1);
        Assert.Equal(rebuilds, page.Rebuilds); // the list itself was not rebuilt

        Close(s, BeneficialPage);
        writes = page.TimeWrites;
        s.Rig.Run(5.0);
        Assert.Equal(writes, page.TimeWrites); // nothing runs while the page is hidden
        Assert.Equal(rebuilds, page.Rebuilds);
        Open(s, BeneficialPage);
        s.Rig.Frame();
        Assert.Equal(1, page.TimeWrites - writes); // shown again: the line is current in one write
        var now = s.Rig.Session.Clock.Now;
        Assert.True(EffectRules.TryRemainingWholeSeconds(s.Rig.Session.State.Self.Qualities.ActiveEnchantments[0], now, out var remaining));
        Assert.Equal(EffectRules.FormatDuration(remaining), Assert.Single(page.Lines).Time);
    }

    [RequiresLocalDatsFact]
    public void A_change_of_the_registry_marks_a_hidden_page_and_it_is_redrawn_once_when_shown()
    {
        var good = Spells(true, 3);
        using var s = Enter(Description([Ench(good[0], 600.5)]));
        Open(s, BeneficialPage);
        Assert.Equal(1, s.Beneficial.LineCount);
        Close(s, BeneficialPage);
        var rebuilds = s.Beneficial.Rebuilds;
        Add(s, Ench(good[1], 700.5));
        Add(s, Ench(good[2], 800.5));
        s.Rig.Run(0.5);
        Assert.Equal(rebuilds, s.Beneficial.Rebuilds); // hidden: nothing is drawn
        Open(s, BeneficialPage);
        Assert.Equal(rebuilds + 1, s.Beneficial.Rebuilds); // one redraw for both changes
        Assert.Equal(3, s.Beneficial.LineCount);

        // a shown page follows the registry: an entry ending leaves the list
        s.Handler.OnMagicRemoveEnchantment(Context, new GameEventHeader(1, 1, 0x02C3), new MagicRemoveEnchantmentEvent((ushort)good[1].Id, 1));
        s.Rig.Frame();
        s.Rig.Frame();
        Assert.Equal(rebuilds + 2, s.Beneficial.Rebuilds);
        Assert.Equal(2, s.Beneficial.LineCount);
        Assert.DoesNotContain(s.Beneficial.Lines, l => l.SpellId == good[1].Id);
    }

    [RequiresLocalDatsFact]
    public void A_click_selects_a_line_and_the_info_box_names_the_spell_the_same_line_clears_it()
    {
        var good = Spells(true, 3);
        using var s = Enter(Description(good.Select(g => Ench(g, 600.5))));
        Open(s, BeneficialPage);
        var page = s.Beneficial;
        output.WriteLine("info on open: [" + page.InfoText + "]");
        Assert.NotEqual(string.Empty, page.InfoText); // "select a spell"
        var info0 = page.InfoText;
        var list = Assert.IsType<UiListBox>(page.List);
        var item = Assert.IsAssignableFrom<UiElement>(page.ItemOf(good[1].Id));
        list.SetSelectedItem(item, broadcast: true);
        s.Rig.Frame();
        Assert.Equal(good[1].Id, page.SelectedSpell);
        Assert.StartsWith(good[1].Name, page.InfoText, StringComparison.Ordinal);
        Assert.Contains(good[1].Description, page.InfoText, StringComparison.Ordinal);
        Assert.Equal(UiStateIds.Highlight, item.State);
        Assert.Equal(UiStateIds.Normal, Assert.IsAssignableFrom<UiElement>(page.ItemOf(good[0].Id)).State);
        list.SetSelectedItem(item, broadcast: true); // the same line again
        s.Rig.Frame();
        Assert.Equal(0u, page.SelectedSpell);
        Assert.Equal(info0, page.InfoText);
    }

    [RequiresLocalDatsFact]
    public void The_harmful_page_with_only_beneficial_effects_says_it_has_no_spells_and_an_empty_registry_leaves_the_box_as_retail_does()
    {
        var good = Spells(true, 2);
        using var s = Enter(Description(good.Select(g => Ench(g, 600.5))));
        Open(s, HarmfulPage);
        Assert.Equal(0, s.Harmful.LineCount);
        output.WriteLine("harmful info: [" + s.Harmful.InfoText + "]");
        Assert.NotEqual(string.Empty, s.Harmful.InfoText); // something is in effect, nothing of this kind: the "no spells" text
        Close(s, HarmfulPage);

        using var empty = Enter(Description());
        Open(empty, BeneficialPage);
        Assert.Equal(0, empty.Beneficial.LineCount);
        Assert.Equal(string.Empty, empty.Beneficial.InfoText); // RebuildList returns before UpdateSelection when nothing is in effect (a retail quirk; the fix leaves it blank too, see When_the_last_effect_ends)
    }

    [RequiresLocalDatsFact]
    public void When_the_last_effect_ends_the_selection_and_the_info_box_clear_unless_retails_stale_info_is_restored()
    {
        // Review 2.2-R7 (decision D32): gmEffectsUI::RebuildList (11.6096 0x004B8F70) returns before UpdateSelection when nothing is in effect, so retail keeps the ended spell's text; Lifestone clears it.
        var good = Spells(true, 1);
        foreach (var retail in new[] { false, true })
        {
            using var s = Enter(Description([Ench(good[0], 600.5)]), glitches: retail ? UiRetailGlitches.EffectsStaleInfo : UiRetailGlitches.None);
            Open(s, BeneficialPage);
            var page = s.Beneficial;
            var list = Assert.IsType<UiListBox>(page.List);
            list.SetSelectedItem(Assert.IsAssignableFrom<UiElement>(page.ItemOf(good[0].Id)), broadcast: true);
            s.Rig.Frame();
            Assert.Equal(good[0].Id, page.SelectedSpell);
            var text = page.InfoText;
            Assert.StartsWith(good[0].Name, text, StringComparison.Ordinal);

            s.Handler.OnMagicRemoveEnchantment(Context, new GameEventHeader(1, 1, 0x02C3), new MagicRemoveEnchantmentEvent((ushort)good[0].Id, 1));
            s.Rig.Frame();
            s.Rig.Frame();
            Assert.Equal(0, page.LineCount);
            if (retail)
            {
                Assert.Equal(good[0].Id, page.SelectedSpell); // retail: the ended spell stays selected and described
                Assert.Equal(text, page.InfoText);
            }
            else
            {
                Assert.Equal(0u, page.SelectedSpell);
                Assert.Equal(string.Empty, page.InfoText);
            }
        }
    }

    [Fact]
    public void The_stale_info_flag_is_part_of_the_retail_ui_glitches_and_has_its_own_option()
    {
        var own = Lifestone.Client.RetailUi.RetailUiOptions.Parse(["--retail-effects-stale-info"]);
        Assert.Equal(UiRetailGlitches.EffectsStaleInfo, own.Glitches);
        Assert.Empty(own.Errors);
        Assert.True((Lifestone.Client.RetailUi.RetailUiOptions.Parse(["--retail-ui-glitches"]).Glitches & UiRetailGlitches.EffectsStaleInfo) != 0);
        Assert.Equal(UiRetailGlitches.None, Lifestone.Client.RetailUi.RetailUiOptions.Parse([]).Glitches);
        Assert.Contains(Lifestone.Client.Options.OptionCatalog.RetailUi().Options, static o => o.Name == "--retail-effects-stale-info");
    }

    [RequiresLocalDatsFact]
    public void A_relog_leaves_no_controller_subscribed_disposes_the_figure_and_a_worse_vitae_touches_only_the_new_host()
    {
        // Review 2.2-R2: the session and its context outlive a log off, so every controller of the ended screen must unsubscribe, and the paperdoll's figure must leave the viewport.
        const uint player = 0x50000001;
        IInboundMessageHandler? handler = null;
        var views = new RecordingViewHost();
        using var rig = new LoginRig(fixture, h =>
        {
            handler = h;
            StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true, view3D: true, viewHost: views);
        rig.Run(2.5);
        var baseline = rig.Session.Bus.HandlerCount; // character management is up: the screens that are always there
        Assert.Empty(views.AttachedFigures);

        GamePlayScreen EnterGame()
        {
            rig.Flow.QueueUIMode(UiModes.GamePlay);
            rig.Frame();
            rig.Frame();
            return Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        }

        var first = EnterGame();
        handler!.OnPlayerCreate(Context, new PlayerCreateMsg(player));
        handler.OnCreateObject(Context, ReducerHarness.Create(player, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD), state: PhysicsState.Hidden | PhysicsState.Gravity, name: "Tester"), isUpdate: false);
        handler.OnPlayerDescription(Context, new GameEventHeader(1, 1, 0x0013), Description());
        rig.Frame();
        Assert.True(first.PanelHost!.Toggle(7)); // the inventory page with the paperdoll: its figure is served when the page first shows
        for (var i = 0; i < 14; i++)
        {
            rig.Frame();
        }

        Assert.Single(views.AttachedFigures);
        Assert.Equal(0, views.Detached);
        Assert.True(rig.Session.Bus.HandlerCount > baseline + 10, "the game screen's controllers subscribe to the session");

        // Log off with the page open.
        var oldHost = first.PanelHost;
        var oldHostEvents = 0;
        oldHost.PageVisibilityChanged += (_, _) => oldHostEvents++;
        rig.Flow.QueueUIMode(UiModes.CharacterManagement);
        for (var i = 0; i < 4; i++)
        {
            rig.Frame();
        }

        Assert.Equal(baseline, rig.Session.Bus.HandlerCount);
        Assert.Equal(1, views.Detached); // the figure's attachment was disposed
        oldHostEvents = 0;

        // Enter again: a new screen, a new set of controllers, one more figure.
        var second = EnterGame();
        Assert.NotSame(first, second);
        Assert.True(second.PanelHost!.Toggle(7));
        for (var i = 0; i < 14; i++)
        {
            rig.Frame();
        }

        Assert.Equal(2, views.AttachedFigures.Count);
        Assert.Equal(1, views.Detached);

        // A worse vitae while the vitae page is hidden opens the new host's page, and the old host hears nothing.
        Assert.True(second.PanelHost.Toggle(7)); // close the inventory page: the vitae page is hidden
        rig.Frame();
        handler.OnMagicUpdateEnchantment(Context, Header, new MagicUpdateEnchantmentEvent(Vitae(0.95f)));
        rig.Frame();
        rig.Frame();
        Assert.Equal(VitaePage, second.PanelHost.CurrentPanel);
        Assert.Equal(0, oldHostEvents);
    }

    [RequiresLocalDatsFact]
    public void A_spell_the_table_does_not_hold_yet_is_not_guessed_and_is_listed_once_the_table_answers()
    {
        var good = Spells(true, 2);
        using var s = Enter(Description(good.Select(g => Ench(g, 600.5))), spells: false);
        Assert.Null(s.Rig.Session.Spell(good[0].Id));
        Open(s, BeneficialPage);
        Assert.Equal(0, s.Beneficial.LineCount);
        Assert.Single(s.Rig.Log.Lines, static l => l.Contains("TODO(m3-effects-spell-table)", StringComparison.Ordinal) && l.Contains("gmEffectsUI", StringComparison.Ordinal));
        s.Rig.Run(0.5);
        Assert.Equal(1, s.Beneficial.Rebuilds); // it waits; it does not rebuild every frame
        s.Rig.Session.UseSpellsFrom(fixture.Dats);
        Assert.True(s.Rig.Session.SpellTableLoaded.Wait(TimeSpan.FromSeconds(60)));
        s.Rig.Run(0.5);
        Assert.Equal(2, s.Beneficial.LineCount);
        Assert.Equal(2, s.Beneficial.Rebuilds);
    }

    [RequiresLocalDatsFact]
    [Trait("Tier", "Timing")] // m4-timing-tier-candidates: an 8 ms wall-clock bound and a zero-byte allocation count
    public void A_controller_is_built_in_under_8_ms_and_an_idle_step_allocates_nothing()
    {
        var good = Spells(true, 24);
        using var s = Enter(Description(good.Select((g, i) => Ench(g, 3600.75 - (i * 61)))));
        var panels = s.Screen.PanelControllers!;
        var times = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using var built = new GameEffectsPage(panels.Context, panels.Host, BeneficialPage);
            using var vitae = new GameVitaePage(panels.Context, panels.Host);
            watch.Stop();
            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        var median = times.Order().ElementAt(2);
        output.WriteLine($"construction (page + vitae page) median {median:0.000} ms of [{string.Join(", ", times.Select(static t => t.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)))}]");
        Assert.True(median < 8.0, $"the controllers' construction took {median} ms");

        Open(s, BeneficialPage);
        Assert.Equal(24, s.Beneficial.LineCount);
        IGamePanel page = s.Beneficial;
        IGamePanel vitaePage = s.VitaePage;
        page.Step(1.0);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
        {
            page.Step(1.0 + (i * 0.001)); // the session clock does not move, so no line's whole seconds change
            vitaePage.Step(1.0);
        }

        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // ------------------------------------------------------------------ the vitae page

    [RequiresLocalDatsFact]
    public void The_vitae_page_says_there_is_no_vitae_for_a_character_without_a_penalty()
    {
        using var s = Enter(Description());
        Assert.Equal(string.Empty, s.VitaePage.Report); // not drawn while hidden
        Open(s, VitaePage);
        output.WriteLine("no vitae: [" + s.VitaePage.Report + "]");
        Assert.NotEqual(string.Empty, s.VitaePage.Report);
        Assert.Equal(1, s.VitaePage.Updates);
        Assert.Equal(1f, s.VitaePage.Vitae);
    }

    [RequiresLocalDatsFact]
    public void The_vitae_page_shows_the_penalty_percentage_and_the_experience_still_owed()
    {
        using var s = Enter(Description([Vitae(0.95f)], level: 50, pool: 10_000));
        Open(s, VitaePage);
        var text = s.VitaePage.Report;
        output.WriteLine("vitae: [" + string.Join("<LF>", text.Split((char)10)) + "]");
        Assert.Contains("5", text, StringComparison.Ordinal);
        Assert.Contains("24,212", text, StringComparison.Ordinal); // (50^2.5 * 2.5 + 20) * 0.95^5 = 34,212 less the pool of 10,000
        Assert.Equal(5, EffectRules.VitaePercentage(0.95f));
        Assert.Equal(34212, EffectRules.VitaeCpPoolThreshold(0.95f, 50));
        Assert.Equal(24212, EffectRules.VitaeExperienceOwed(s.Rig.Session.State.Self.Qualities, 0.95f));
    }

    [RequiresLocalDatsFact]
    public void A_vitae_page_built_after_the_description_starts_from_the_vitae_the_qualities_hold()
    {
        // The live shape: the shell is wired after the login description, so no vitae event reaches a page built later (2.2 fix B's play-check found it saying "no vitae").
        using var s = Enter(Description([Vitae(0.95f)], level: 50, pool: 10_000));
        using var late = new GameVitaePage(s.Screen.PanelControllers!.Context, s.Screen.PanelHost!);
        Assert.Equal(0.95f, late.Vitae);
    }

    [RequiresLocalDatsFact]
    public void A_changed_pool_redraws_the_shown_page_and_a_worse_penalty_opens_a_hidden_one()
    {
        using var s = Enter(Description([Vitae(0.95f)], level: 50, pool: 10_000));
        Open(s, VitaePage);
        var updates = s.VitaePage.Updates;
        Close(s, VitaePage);
        Add(s, Vitae(0.90f)); // another death: the penalty is worse (the multiplier fell), the page is hidden, so it comes up
        s.Rig.Frame();
        s.Rig.Frame();
        Assert.Equal(VitaePage, s.Host.CurrentPanel);
        Assert.Equal(0.90f, s.VitaePage.Vitae);
        Assert.True(s.VitaePage.Updates > updates);
        Assert.Contains("10", s.VitaePage.Report, StringComparison.Ordinal);
        updates = s.VitaePage.Updates;
        Add(s, Vitae(0.95f)); // milder: a shown page just updates
        s.Rig.Frame();
        s.Rig.Frame();
        Assert.Equal(VitaePage, s.Host.CurrentPanel);
        Assert.Equal(updates + 1, s.VitaePage.Updates);
        Close(s, VitaePage);
        Add(s, Vitae(1.0f)); // cleared while hidden: not worse, so no page
        s.Rig.Run(0.2);
        Assert.NotEqual(VitaePage, s.Host.CurrentPanel);
    }

    // ------------------------------------------------------------------ the indicators

    [RequiresLocalDatsFact]
    public void The_effects_indicators_light_from_the_registry_and_a_click_opens_their_page()
    {
        var good = Spells(true, 2);
        var bad = Spells(false, 1);
        using var s = Enter(Description());
        var ind = s.Indicators;
        var beneficial = Assert.IsType<UiStateIndicator>(ind.Beneficial);
        var harmful = Assert.IsType<UiStateIndicator>(ind.Harmful);
        Assert.Equal(UiStateIds.Ghosted, beneficial.State);
        Assert.Equal(UiStateIds.Ghosted, harmful.State);
        Add(s, Ench(good[0], 600.5));
        s.Rig.Frame();
        Assert.Equal(UiStateIds.Normal, beneficial.State); // lit
        Assert.Equal(UiStateIds.Ghosted, harmful.State);
        Add(s, Ench(bad[0], 600.5));
        s.Rig.Frame();
        Assert.Equal(UiStateIds.Normal, harmful.State);

        Assert.True(s.Rig.Click(beneficial));
        s.Rig.Frame();
        Assert.Equal(BeneficialPage, s.Host.CurrentPanel);
        Assert.Equal(1, s.Beneficial.LineCount);
        Close(s, BeneficialPage);
        Assert.True(s.Rig.Click(harmful));
        s.Rig.Frame();
        Assert.Equal(HarmfulPage, s.Host.CurrentPanel);
        Close(s, HarmfulPage);

        s.Handler.OnMagicRemoveEnchantment(Context, new GameEventHeader(1, 1, 0x02C3), new MagicRemoveEnchantmentEvent((ushort)good[0].Id, 1));
        s.Rig.Frame();
        Assert.Equal(UiStateIds.Ghosted, beneficial.State); // the last beneficial effect ended
        Assert.Equal(UiStateIds.Normal, harmful.State);
        s.Rig.Click(beneficial);
        s.Rig.Frame();
        Assert.NotEqual(BeneficialPage, s.Host.CurrentPanel); // a ghosted indicator takes no click
    }

    [RequiresLocalDatsFact]
    public void The_effects_indicator_lights_once_the_spell_table_has_loaded()
    {
        var good = Spells(true, 1);
        using var s = Enter(Description([Ench(good[0], 600.5)]), spells: false);
        Assert.Equal(UiStateIds.Ghosted, s.Indicators.Beneficial!.State); // the table cannot say whether the spell is beneficial: not a guess
        Assert.Contains(s.Rig.Log.Lines, static l => l.Contains("TODO(m3-effects-spell-table)", StringComparison.Ordinal) && l.Contains("EffectsIndicator", StringComparison.Ordinal));
        s.Rig.Session.UseSpellsFrom(fixture.Dats);
        Assert.True(s.Rig.Session.SpellTableLoaded.Wait(TimeSpan.FromSeconds(60)));
        s.Rig.Run(0.2);
        Assert.Equal(UiStateIds.Normal, s.Indicators.Beneficial.State);
    }

    [RequiresLocalDatsFact]
    public void The_vitae_indicator_is_lit_by_a_penalty_and_opens_the_vitae_page()
    {
        using var s = Enter(Description());
        var vitae = Assert.IsType<UiStateIndicator>(s.Indicators.Vitae);
        Assert.Equal(UiStateIds.Ghosted, vitae.State);
        Add(s, Vitae(0.95f));
        s.Rig.Run(0.1);
        Assert.Equal(UiStateIds.Normal, vitae.State);
        Close(s, VitaePage); // the worse penalty opened the page; close it, then click the indicator
        Assert.True(s.Rig.Click(vitae));
        s.Rig.Frame();
        Assert.Equal(VitaePage, s.Host.CurrentPanel);
        Close(s, VitaePage);
        s.Handler.OnMagicRemoveEnchantment(Context, new GameEventHeader(1, 1, 0x02C3), new MagicRemoveEnchantmentEvent(666, 1));
        s.Rig.Frame();
        Assert.Equal(UiStateIds.Ghosted, vitae.State);
    }

    [RequiresLocalDatsFact]
    public void The_burden_indicator_follows_the_load_and_opens_character_information()
    {
        // Strength 90 carries 150 * 90 = 13,500: under it the indicator shows state 14, from it state 15, from twice it state 16
        using var s = Enter(Description(burden: 9_000));
        var burden = Assert.IsType<UiStateIndicator>(s.Indicators.Burden);
        Assert.Equal(GameIndicators.BurdenLightState, burden.State);
        s.Handler.OnPlayerDescription(Context, new GameEventHeader(1, 1, 0x0013), Description(burden: 13_500));
        s.Rig.Frame();
        Assert.Equal(GameIndicators.BurdenHeavyState, burden.State);
        s.Handler.OnPlayerDescription(Context, new GameEventHeader(1, 1, 0x0013), Description(burden: 27_000));
        s.Rig.Frame();
        Assert.Equal(GameIndicators.BurdenOverState, burden.State);
        s.Handler.OnPlayerDescription(Context, new GameEventHeader(1, 1, 0x0013), Description(burden: 100));
        s.Rig.Frame();
        Assert.Equal(GameIndicators.BurdenLightState, burden.State);

        Assert.True(s.Rig.Click(burden));
        s.Rig.Frame();
        Assert.Equal(CharacterInfoPage, s.Host.CurrentPanel);
    }

    [RequiresLocalDatsFact]
    public void Strength_enchantments_raise_the_capacity_the_load_is_measured_against()
    {
        // 13,500 is a full load on Strength 90; a Strength buff of +90 doubles the capacity, so the same burden is half a load
        var strength = new EnchantmentDesc(7, 1, 7, 0, 100, 0.0, 600.5, 0x50000001, 0f, 0f, 0.0, AdditiveAttribute, 1, 90f, 0, EnchantmentBucket.Additive);
        using var s = Enter(Description([strength], burden: 13_500));
        Assert.Equal(GameIndicators.BurdenLightState, s.Indicators.Burden!.State);
        Assert.Equal(0.5f, GameCharacterInfo.LoadOf(s.Rig.Session.State.Self.Qualities, out var capacity, out var burden));
        Assert.Equal((27_000, 13_500), (capacity, burden));
    }

    // ------------------------------------------------------------------ character information

    [RequiresLocalDatsFact]
    public void Character_information_is_rebuilt_on_a_qualities_change_while_shown_and_not_while_hidden()
    {
        using var s = Enter(Description(burden: 100));
        var info = Assert.IsType<GameCharacterInfo>(s.Screen.CharacterInfo);
        Open(s, CharacterInfoPage);
        info.Open();
        s.Rig.Frame();
        var updates = info.Updates;
        Assert.True(updates > 0);
        s.Handler.OnPlayerDescription(Context, new GameEventHeader(1, 1, 0x0013), Description(burden: 20_000)); // 20,000 over 13,500: burdened
        s.Rig.Frame();
        Assert.True(info.Updates > updates);
        Assert.NotEqual(string.Empty, info.Report);
        output.WriteLine("character info tail: [" + info.Report[^Math.Min(info.Report.Length, 160)..] + "]");
        Close(s, CharacterInfoPage);
        updates = info.Updates;
        s.Handler.OnPlayerDescription(Context, new GameEventHeader(1, 1, 0x0013), Description(burden: 100));
        s.Rig.Frame();
        Assert.Equal(updates, info.Updates); // hidden: no rebuild
    }
}
