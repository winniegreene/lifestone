using System.Collections.Immutable;
using System.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The attributes tab of the skill page (M3 2.2 a1, <see cref="GameAttributesTab"/>; docs/retail-ui.md section 23): the page opens on the toolbar's skills button with the attributes tab, the nine rows
/// show the stats with the enchantment registry applied (buffed above the base, in the buffed colour), selecting a row swaps the footer, the raise buttons send the right experience through the Action API
/// and ghost when the experience falls short, and a change of the qualities redraws the tab once.
/// </summary>
public sealed class GameAttributesTabTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint SkillsButton = 0x10000199;

    // A spell category and the type of an additive attribute enchantment (EnchantmentRulesTests' AdditiveAttribute).
    private const uint AdditiveAttribute = 0x2000000 | EnchantmentRules.Additive | 0x1000 | EnchantmentRules.AttributeKind;

    private static EnchantmentDesc SelfBuff(float value) =>
        new(1, 1, 7, 0, 300, 0.0, 3600.0, 0x50000001, 0f, 0f, 0.0, AdditiveAttribute, PlayerQualities.SelfAttributeId, value, 0, EnchantmentBucket.Additive);

    private XpTableData Table => XpTableData.Load(fixture.Dats);

    // BSD Mage's finals (docs/session.md section 29): Strength 141, Endurance 140, Quickness 140, Coordination 229, Focus and Self 267; Health 205, Stamina 275, Mana 426.
    private PlayerDescriptionMsg Description(long available = 1_000_000, bool buffed = true, uint strengthRanks = 131, uint manaCurrent = 300, int level = 126, long total = 4_286_609_098)
    {
        var table = Table;
        return new PlayerDescriptionMsg
        {
            Options1 = Lifestone.Client.Session.StandIn.StandInPlayerStats.AceDefaultOptions1,
            Options2 = Lifestone.Client.RetailUi.Options.PlayerOptionTable.DefaultWord2,
            IntProperties = ImmutableDictionary<uint, int>.Empty.Add(PlayerQualities.LevelProperty, level).Add(PlayerQualities.AvailableSkillCreditsProperty, 7),
            Int64Properties = ImmutableDictionary<uint, long>.Empty.Add(PlayerQualities.TotalExperienceProperty, total).Add(PlayerQualities.AvailableExperienceProperty, available),
            Attributes =
            [
                new AttributeDesc(1, strengthRanks, 10, table.AttributeXpList[(int)strengthRanks]), new AttributeDesc(2, 130, 10, table.AttributeXpList[130]),
                new AttributeDesc(3, 130, 10, table.AttributeXpList[130]), new AttributeDesc(4, 129, 100, table.AttributeXpList[129]),
                new AttributeDesc(5, 167, 100, table.AttributeXpList[167]), new AttributeDesc(6, 167, 100, table.AttributeXpList[167]),
            ],
            Vitals =
            [
                new VitalDesc(1, 135, 0, table.VitalXpList[135], 205), new VitalDesc(3, 135, 0, table.VitalXpList[135], 275), new VitalDesc(5, 159, 0, table.VitalXpList[159], manaCurrent),
            ],
            Enchantments = buffed ? [SelfBuff(40f)] : [],
            EnchantmentMask = buffed ? 2u : 0u,
        };
    }

    private (LoginRig Rig, GamePlayScreen Screen, GameAttributesTab Tab, IInboundMessageHandler Handler) Enter(PlayerDescriptionMsg? description = null)
    {
        IInboundMessageHandler? handler = null;
        var rig = new LoginRig(fixture, h =>
        {
            handler = h;
            Lifestone.Client.Session.StandIn.StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            Lifestone.Client.Session.StandIn.StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true);
        rig.Session.UseStatTablesFrom(fixture.Dats);
        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        var screen = Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        handler!.OnPlayerDescription(new InboundContext(0, MessageQueue.SmartBox, 0), new GameEventHeader(1, 1, 0x0013), description ?? Description());
        rig.Frame();
        var tab = Assert.Single(screen.PanelControllers!.ControllersOf(11).OfType<GameAttributesTab>());
        return (rig, screen, tab, handler);
    }

    private static void Describe(IInboundMessageHandler handler, PlayerDescriptionMsg description) =>
        handler.OnPlayerDescription(new InboundContext(0, MessageQueue.SmartBox, 0), new GameEventHeader(1, 1, 0x0013), description);

    private static void OpenPage(LoginRig rig, GamePlayScreen screen)
    {
        var button = Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(SkillsButton));
        Assert.True(rig.Click(button));
        rig.Frame();
    }

    private static IEnumerable<ActionLogEntry> Entries(LoginRig rig) => Enumerable.Range(0, rig.Session.ActionLog.Count).Select(i => rig.Session.ActionLog[i]);

    private static string N(long value) => value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    private static string[] Rows(GameAttributesTab tab) => [.. tab.StatLines.Select(static l => $"{l.Label} {l.ValueText}")];

    // ------------------------------------------------------------------ the page

    [RequiresLocalDatsFact]
    public void The_toolbars_skills_button_opens_the_page_on_the_attributes_tab()
    {
        var (rig, screen, tab, _) = Enter();
        using var _ = rig;
        var host = screen.PanelHost!;
        Assert.Equal(PanelPageKind.Built, host.KindOf(11));
        Assert.False(host.IsOpen);
        OpenPage(rig, screen);

        Assert.Empty(rig.Manager.Dialogs.OpenDialogs); // a page, not the NotInM3 message
        Assert.True(host.IsOpen);
        Assert.Equal(11u, host.CurrentPanel);
        var panel = Assert.IsType<UiPanel>(host.ElementFor(11));
        Assert.Equal(GameAttributesTab.TabId, panel.OpenTabId);
        Assert.Equal(GameAttributesTab.PageId, panel.OpenPageId);
        Assert.True(tab.IsTabShown);
        Assert.False(panel.GetChildRecursive(0x1000022C)!.Visible); // the skills page waits for a2's tab
        Assert.False(panel.GetChildRecursive(GameAttributesTab.TitlesPageId)!.Visible);
        Assert.Equal(UiStateIds.Open, panel.GetChildRecursive(GameAttributesTab.TabId)!.State);
        Assert.True(tab.Redraws >= 1);

        Assert.True(rig.Click(Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(SkillsButton)))); // the same button closes it
        rig.Frame();
        Assert.False(host.IsOpen);
    }

    [RequiresLocalDatsFact]
    public void The_character_titles_tab_shows_its_art_and_logs_its_TODO_once()
    {
        var (rig, screen, tab, _) = Enter();
        using var _ = rig;
        OpenPage(rig, screen);
        var panel = Assert.IsType<UiPanel>(screen.PanelHost!.ElementFor(11));
        Assert.DoesNotContain(rig.Log.Lines, static l => l.Contains("TODO(m3-character-titles)", StringComparison.Ordinal));
        Assert.True(panel.OpenTab(GameAttributesTab.TitlesTabId));
        rig.Frame();
        rig.Frame();
        Assert.True(panel.GetChildRecursive(GameAttributesTab.TitlesPageId)!.Visible);
        Assert.False(tab.IsTabShown);
        Assert.Single(rig.Log.Lines, static l => l.Contains("TODO(m3-character-titles)", StringComparison.Ordinal));
        Assert.True(panel.OpenTab(GameAttributesTab.TabId)); // back to the attributes
        rig.Frame();
        Assert.True(tab.IsTabShown);
        Assert.Single(rig.Log.Lines, static l => l.Contains("TODO(m3-character-titles)", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void The_controller_is_built_in_one_staged_unit_well_under_the_budget()
    {
        var (rig, screen, tab, _) = Enter();
        using var _ = rig;
        tab.Dispose();
        var panels = screen.PanelControllers!;
        Assert.NotNull(GameAttributesTab.Create(panels)); // warm
        var clock = Stopwatch.StartNew();
        var second = GameAttributesTab.Create(panels);
        clock.Stop();
        Assert.NotNull(second);
        Assert.True(clock.Elapsed.TotalMilliseconds < 8, $"building the controller took {clock.Elapsed.TotalMilliseconds:F2} ms");
    }

    // ------------------------------------------------------------------ the values

    [RequiresLocalDatsFact]
    public void The_nine_rows_show_the_stats_with_the_buffed_values_above_the_base()
    {
        var (rig, screen, tab, _) = Enter();
        using var _ = rig;
        OpenPage(rig, screen);

        // Retail's order (gmAttributeUI::PostInit): Strength, Endurance, Coordination, Quickness, Focus, Self; then the vitals. A Self buff of +40 lifts Self 267 to 307 and Max Mana 426 to 466.
        Assert.Equal(
            [
                "Strength 141", "Endurance 140", "Coordination 229", "Quickness 140", "Focus 267", "Self 307",
                "Health 205/205", "Stamina 275/275", "Mana 300/466",
            ],
            Rows(tab));
        Assert.All(tab.StatLines.Take(4), static l => Assert.Equal(0, l.ValueColor));
        Assert.Equal(0, tab.LineOf(false, 5)!.ValueColor); // Focus is not buffed
        Assert.Equal(1, tab.LineOf(false, 6)!.ValueColor); // Self is raised
        Assert.Equal(0, tab.LineOf(true, 1)!.ValueColor);
        Assert.Equal(1, tab.LineOf(true, 5)!.ValueColor); // Max Mana is raised
        Assert.All(tab.StatLines, static l => Assert.NotNull(l.Row));
        Assert.Equal(9, tab.List!.Items.Count);

        // The base: the same description without the buff shows the unbuffed numbers (Max Mana 426).
        Assert.True(screen.PanelControllers is not null);
        Assert.Equal("Strength", tab.StatLines[0].Label);
    }

    [RequiresLocalDatsFact]
    public void Without_a_buff_every_value_is_the_base_in_the_plain_colour()
    {
        var (rig, screen, tab, _) = Enter(Description(buffed: false));
        using var _ = rig;
        OpenPage(rig, screen);
        Assert.Equal("Self 267", Rows(tab)[5]);
        Assert.Equal("Mana 300/426", Rows(tab)[8]);
        Assert.All(tab.StatLines, static l => Assert.Equal(0, l.ValueColor));
    }

    [RequiresLocalDatsFact]
    public void The_header_shows_the_level_the_total_experience_and_the_experience_to_the_next_level()
    {
        var table = Table;
        var total = (long)table.XpForLevel(100) + 123_456;
        var (rig, screen, tab, _) = Enter(Description(level: 100, total: total));
        using var _ = rig;
        OpenPage(rig, screen);
        Assert.Equal("100", tab.LevelText);
        Assert.Contains(N(total), tab.TotalExperienceText, StringComparison.Ordinal);
        var toNext = (long)table.XpForLevel(101) - total;
        Assert.Contains(N(toNext), tab.ExperienceToLevelText, StringComparison.Ordinal);
        var span = (double)(table.XpForLevel(101) - table.XpForLevel(100));
        Assert.Equal(123_456 / span, tab.ExperienceMeterPosition, 3);
        Assert.DoesNotContain("string table error", tab.TotalExperienceText + tab.ExperienceToLevelText, StringComparison.Ordinal);
    }

    [RequiresLocalDatsFact]
    public void With_nothing_selected_the_footer_shows_the_skill_credits_and_the_unassigned_experience()
    {
        var (rig, screen, tab, _) = Enter(Description(available: 5_000_000));
        using var _ = rig;
        OpenPage(rig, screen);
        Assert.Equal(-1, tab.SelectedIndex);
        Assert.Equal("Select an Attribute to Improve", tab.FooterTitle);
        Assert.Equal("Skill Credits Available: 7", tab.FooterLineOne);
        Assert.Equal("Unassigned Experience: 5,000,000", tab.FooterLineTwo);
        Assert.True(tab.List!.Parent!.GetChildRecursive(GameAttributesTab.DefaultFooterId)!.IsShown);
        Assert.False(tab.List.Parent.GetChildRecursive(GameAttributesTab.SelectionFooterId)!.IsShown);
    }

    // ------------------------------------------------------------------ selecting and raising

    [RequiresLocalDatsFact]
    public void Selecting_a_row_shows_its_name_value_and_difference_and_the_cost_of_the_next_rank()
    {
        var table = Table;
        var (rig, screen, tab, _) = Enter(Description(available: uint.MaxValue));
        using var _ = rig;
        OpenPage(rig, screen);

        // A click on the Self row selects it (gmAttributeUI::SetSelection); the same row again clears the selection.
        rig.Frame();
        Assert.True(rig.Click(tab.StatLines[5].Row!));
        rig.Frame();
        Assert.Equal(5, tab.SelectedIndex);
        Assert.Equal("Self: 307 (+40)", tab.FooterTitle); // Self 100 + 167 ranks = 267 raised by the buff
        var attribute = (uint)(table.AttributeXpList[168] - table.AttributeXpList[167]);
        Assert.Equal("Experience To Raise: " + N(attribute), tab.FooterLineOne);
        Assert.Equal("Unassigned Experience: 4,294,967,295", tab.FooterLineTwo);
        Assert.Equal(UiStateIds.Highlight, tab.StatLines[5].Row!.State);
        Assert.Equal(UiStateIds.Normal, tab.StatLines[0].Row!.State);
        Assert.True(tab.List!.Parent!.GetChildRecursive(GameAttributesTab.SelectionFooterId)!.IsShown);
        Assert.False(tab.List.Parent.GetChildRecursive(GameAttributesTab.DefaultFooterId)!.IsShown);
        Assert.Equal(UiStateIds.Normal, tab.RaiseButton!.State);
        Assert.Equal(UiStateIds.Normal, tab.Raise10Button!.State);

        // A vital: Max Mana 466 over a base of 426, the difference in the buff colour. The ninth row is below the list's 160 pixels until it scrolls.
        tab.List.ScrollToShow(8);
        rig.Frame();
        Assert.True(rig.Click(tab.StatLines[8].Row!));
        rig.Frame();
        Assert.Equal(8, tab.SelectedIndex);
        Assert.Equal("Mana: 300/466 (+40)", tab.FooterTitle);
        Assert.Equal("Experience To Raise: " + N(table.VitalXpList[160] - table.VitalXpList[159]), tab.FooterLineOne);

        Assert.True(rig.Click(tab.StatLines[8].Row!));
        rig.Frame();
        Assert.Equal(-1, tab.SelectedIndex);
        Assert.Equal("Select an Attribute to Improve", tab.FooterTitle);
    }

    [RequiresLocalDatsFact]
    public void A_raise_click_sends_the_action_with_the_cost_of_the_next_rank_and_the_ui_source()
    {
        var table = Table;
        var (rig, screen, tab, _) = Enter(Description(available: uint.MaxValue));
        using var _ = rig;
        OpenPage(rig, screen);
        var before = rig.Session.ActionLog.Total;

        tab.Select(0); // Strength: 131 ranks
        rig.Frame();
        var cost = table.AttributeXpList[132] - table.AttributeXpList[131];
        Assert.Equal((ulong)cost, tab.CostToRaise(tab.StatLines[0], ten: false));
        Assert.True(rig.Click(tab.RaiseButton!));
        Assert.Equal(UiStateIds.Ghosted, tab.RaiseButton!.State); // waits for the stat's update
        Assert.Equal(UiStateIds.Ghosted, tab.Raise10Button!.State);
        rig.Run(0.5);

        var entry = Assert.Single(Entries(rig), e => e.Kind == ActionKind.RaiseAttribute);
        Assert.Equal(ActionSource.Ui, entry.Source);
        Assert.Contains($"attribute=1 xp={cost}", entry.Summary, StringComparison.Ordinal);
        Assert.True(rig.Session.ActionLog.Total > before);

        // The vital's button: Max Stamina, ten ranks.
        tab.Select(7);
        rig.Frame();
        var ten = table.VitalXpList[145] - table.VitalXpList[135];
        Assert.Equal((ulong)ten, tab.CostToRaise(tab.StatLines[7], ten: true));
        Assert.True(rig.Click(tab.Raise10Button!));
        rig.Run(0.5);
        var vital = Assert.Single(Entries(rig), e => e.Kind == ActionKind.RaiseVital);
        Assert.Equal(ActionSource.Ui, vital.Source);
        Assert.Contains($"vital=3 xp={ten}", vital.Summary, StringComparison.Ordinal);
    }

    /// <summary>The Action API with a raise that stays in flight: every call goes to the real one, except <c>RaiseAttribute</c> and <c>RaiseVital</c>, which answer a handle that completes only when the test says.</summary>
    private class PendingRaises : System.Reflection.DispatchProxy
    {
        public IActionApi? Real { get; set; }

        public List<ActionHandle> Raises { get; } = [];

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name is nameof(IActionApi.RaiseAttribute) or nameof(IActionApi.RaiseVital))
            {
                var kind = targetMethod.Name == nameof(IActionApi.RaiseAttribute) ? ActionKind.RaiseAttribute : ActionKind.RaiseVital;
                var handle = new ActionHandle(1000 + Raises.Count, kind, ActionSource.Ui, targetMethod.Name, 0);
                Raises.Add(handle);
                return handle;
            }

            return targetMethod.Invoke(Real, args);
        }
    }

    /// <summary>The session as the rig has it, with <paramref name="actions"/> in place of its Action API.</summary>
    private sealed class ViewWith(GameSession inner, IActionApi actions) : IGameSessionView
    {
        public GameState State => inner.State;

        public IEventBus Events => inner.Events;

        public IActionApi Actions => actions;

        public PlayerTools Tools => inner.Tools;

        public SessionClock Clock => inner.Clock;

        public ActionLog ActionLog => inner.ActionLog;

        public LinkStats Link => inner.Link;

        public Lifestone.Data.Magic.SpellInfo? Spell(uint id) => inner.Spell(id);

        public XpTableData? XpTable => inner.XpTable;

        public Lifestone.Data.CharacterCreation.SkillTableData? SkillTable => inner.SkillTable;
    }

    [RequiresLocalDatsFact]
    public void A_raise_in_flight_keeps_both_buttons_ghosted_through_a_vital_tick_and_an_experience_update()
    {
        // Review 2.2-R1: m_bAwaitingRaise clears when the raise's own answer arrives, not on any qualities change (ACE's AvailableExperience update comes before the stat's own).
        var (rig, screen, _, handler) = Enter(Description(available: uint.MaxValue));
        using var _ = rig;
        var actions = System.Reflection.DispatchProxy.Create<IActionApi, PendingRaises>();
        var pending = (PendingRaises)(object)actions;
        pending.Real = rig.Session.Actions;
        var panels = screen.PanelControllers!;
        using var second = new GamePanels(panels.Context with { Session = new ViewWith(rig.Session, actions) }, screen.PanelHost!, screen);
        using var tab = GameAttributesTab.Create(second)!; // a second controller over the same page, its raises held in flight
        tab.Refresh();
        tab.Select(0); // Strength
        Assert.Equal(UiStateIds.Normal, tab.RaiseButton!.State);
        Assert.Equal(UiStateIds.Normal, tab.Raise10Button!.State);

        Assert.True(tab.Raise(ten: false));
        var raise = Assert.Single(pending.Raises);
        Assert.False(raise.IsCompleted);
        Assert.Equal(UiStateIds.Ghosted, tab.RaiseButton.State);

        // A vital's regeneration tick, then the experience update ACE sends before the stat's own: neither frees the buttons.
        handler.OnPrivateUpdate(new InboundContext(0, MessageQueue.SmartBox, 0), new PrivateUpdateMsg(PrivateUpdateKind.VitalCurrent, 100, 6, 111, default, default, default));
        rig.Frame();
        tab.Refresh();
        Assert.Equal(UiStateIds.Ghosted, tab.RaiseButton.State);
        Assert.Equal(UiStateIds.Ghosted, tab.Raise10Button.State);
        Assert.False(tab.Raise(ten: false));

        handler.OnPrivateUpdate(new InboundContext(0, MessageQueue.SmartBox, 0), new PrivateUpdateMsg(PrivateUpdateKind.PropertyInt64, 101, PlayerQualities.AvailableExperienceProperty, uint.MaxValue - 5, default, default, default));
        rig.Frame();
        tab.Refresh();
        Assert.Equal(UiStateIds.Ghosted, tab.RaiseButton.State);
        Assert.Equal(UiStateIds.Ghosted, tab.Raise10Button.State);
        Assert.False(tab.Raise(ten: false));
        Assert.False(tab.Raise(ten: true));
        Assert.Single(pending.Raises); // no second raise was sent with the stale cost

        // The raise's answer arrives: the buttons come back and a raise sends again.
        Assert.True(raise.TryComplete(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "ranks"), 1.0, static (_, _) => { }));
        tab.Refresh();
        Assert.Equal(UiStateIds.Normal, tab.RaiseButton.State);
        Assert.Equal(UiStateIds.Normal, tab.Raise10Button.State);
        Assert.True(tab.Raise(ten: false));
        Assert.Equal(2, pending.Raises.Count);
    }

    [RequiresLocalDatsFact]
    public void The_raise_buttons_are_ghosted_when_the_experience_is_short_or_the_rank_is_capped()
    {
        var table = Table;
        var cost = table.AttributeXpList[132] - table.AttributeXpList[131];
        var (rig, screen, tab, handler) = Enter(Description(available: cost - 1));
        using var _ = rig;
        OpenPage(rig, screen);
        tab.Select(0);
        Assert.Equal(UiStateIds.Ghosted, tab.RaiseButton!.State); // one short of the next rank
        Assert.Equal(UiStateIds.Ghosted, tab.Raise10Button!.State);
        var before = rig.Session.ActionLog.Total;
        Assert.False(tab.Raise(ten: false));
        rig.Run(0.5);
        Assert.Equal(before, rig.Session.ActionLog.Total);

        // Exactly the cost: the one-rank button wakes, the ten-rank button stays ghosted.
        Describe(handler, Description(available: cost));
        rig.Frame();
        rig.Frame();
        Assert.Equal(UiStateIds.Normal, tab.RaiseButton!.State);
        Assert.Equal(UiStateIds.Ghosted, tab.Raise10Button!.State);

        // A capped attribute (190 ranks) costs nothing to raise: the footer says infinity and both buttons are ghosted.
        Describe(handler, Description(available: uint.MaxValue, strengthRanks: 190));
        rig.Frame();
        rig.Frame();
        Assert.Equal(0ul, tab.CostToRaise(tab.StatLines[0], ten: false));
        Assert.Equal("Experience To Raise: Infinity!", tab.FooterLineOne);
        Assert.Equal(UiStateIds.Ghosted, tab.RaiseButton!.State);
        Assert.Equal(UiStateIds.Ghosted, tab.Raise10Button!.State);
    }

    // ------------------------------------------------------------------ live

    [RequiresLocalDatsFact]
    public void A_change_marks_the_tab_dirty_and_it_redraws_once_while_shown_and_never_while_hidden()
    {
        var (rig, screen, tab, handler) = Enter();
        using var _ = rig;
        OpenPage(rig, screen);
        var redraws = tab.Redraws;
        rig.Frame();
        rig.Frame();
        Assert.Equal(redraws, tab.Redraws); // nothing changed: no rebuild

        Describe(handler, Description(buffed: false, manaCurrent: 111));
        Assert.Equal(redraws, tab.Redraws); // the change only marks the tab dirty
        rig.Frame();
        Assert.Equal(redraws + 1, tab.Redraws); // the redraw happens once, in the shell's frame
        rig.Frame();
        Assert.Equal(redraws + 1, tab.Redraws);
        Assert.Equal("Mana 111/426", Rows(tab)[8]);
        Assert.Equal("Self 267", Rows(tab)[5]);

        // Hidden: the change is noted but nothing is built until the page shows.
        OpenPage(rig, screen);
        Assert.False(screen.PanelHost!.IsOpen);
        var hidden = tab.Redraws;
        Describe(handler, Description(buffed: true, manaCurrent: 222));
        rig.Frame();
        rig.Frame();
        Assert.Equal(hidden, tab.Redraws);
        OpenPage(rig, screen);
        Assert.True(tab.Redraws > hidden);
        Assert.Equal("Mana 222/466", Rows(tab)[8]);
    }
}
