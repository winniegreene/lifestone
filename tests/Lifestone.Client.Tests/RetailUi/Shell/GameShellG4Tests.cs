using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Indicators;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Toolbar;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Shell;

/// <summary>
/// The toolbar, the panel host and the indicators of the in-game shell (docs/retail-ui.md section 21.5): every gameplay button with no controller answers with the NotInM3 message that names its panel (the combat buttons change the combat mode since M3 3.2: GameCombatPanelTests), the host is hidden at
/// login (<c>gmPanelUI::PostInit</c>), g6's two pages call their hook, and the link indicator follows the time since the last server packet.
/// </summary>
public sealed class GameShellG4Tests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static GamePlayScreen EnterGame(LoginRig rig)
    {
        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        return Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
    }

    private static UiDialog OnlyDialog(LoginRig rig) => Assert.Single(rig.Manager.Dialogs.OpenDialogs);

    private static string TextOf(UiDialog dialog) => Assert.IsType<UiTextElement>(dialog.GetChildRecursive(UiDialog.TextElement)).Text;

    public static TheoryData<uint, string> GameplayPanelButtons => new()
    {
        { 0x10000197, "Fellowship, allegiance and friends" },
        { 0x10000198, "Spellbook and components" },
        { 0x10000199, "Skills and attributes" },
        { 0x1000055A, "Contracts, journal and quests" },
        { 0x1000019A, "Map" },
        { 0x100001B1, "Inventory, backpack and paperdoll" },
    };

    public static TheoryData<uint, string> ActionButtons => new()
    {
        { 0x100001A5, "Examination" },
    };

    [RequiresLocalDatsFact]
    public void The_panel_host_is_hidden_at_login_with_every_page_hidden()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        var host = Assert.IsType<GamePanelHost>(screen.PanelHost);
        Assert.False(host.IsOpen);
        Assert.False(screen.Panel!.Visible);
        Assert.Equal(0u, host.CurrentPanel);
        Assert.Equal(16, GamePanelHost.Pages.Count);
        foreach (var page in GamePanelHost.Pages)
        {
            Assert.False(Assert.IsAssignableFrom<UiElement>(host.ElementFor(page.PanelId)).Visible, page.Name);
        }

        Assert.Empty(rig.Manager.Dialogs.OpenDialogs);
        Assert.DoesNotContain("panels", screen.Placeholders);
        Assert.DoesNotContain("toolbar", screen.Placeholders);
        Assert.DoesNotContain("indicators", screen.Placeholders);
    }

    [RequiresLocalDatsTheory]
    [MemberData(nameof(GameplayPanelButtons))]
    public void A_panel_button_with_no_controller_opens_the_NotInM3_message_naming_its_panel(uint buttonId, string panel)
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        var button = Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(buttonId));
        Assert.True(rig.Click(button));
        rig.Frame();
        var page = GamePanelHost.Pages.First(p => p.Name == panel);
        if (screen.PanelControllers!.RegisteredPages.Contains(page.PanelId))
        {
            // M3 2.2: a slice built this page, so its button opens it (the page's own tests judge the content) instead of the message.
            Assert.Equal(page.PanelId, screen.PanelHost!.CurrentPanel);
            Assert.Empty(rig.Manager.Dialogs.OpenDialogs);
            return;
        }

        Assert.Equal(NotInM3.Prefix + panel, TextOf(OnlyDialog(rig)));
        Assert.False(screen.PanelHost!.IsOpen); // the page never shows: the message says it is not built
        Assert.Contains(rig.Log.Lines, l => l.Contains("placeholder", StringComparison.Ordinal) && l.Contains(panel, StringComparison.Ordinal));
    }

    [RequiresLocalDatsTheory]
    [MemberData(nameof(ActionButtons))]
    public void The_examine_button_opens_the_NotInM3_message(uint buttonId, string feature)
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        var button = Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(buttonId));
        if (!button.Visible || button.State == UiStateIds.Ghosted)
        {
            // The layout shows one combat button for the current mode and ghosts Use until something is selected: press the message path directly.
            button.BroadcastElementMessage(UiMessageIds.ButtonClick);
        }
        else
        {
            Assert.True(rig.Click(button));
        }

        rig.Frame();
        Assert.Equal(NotInM3.Prefix + feature, TextOf(OnlyDialog(rig)));
    }

    /// <summary>
    /// The retail panel keys, fed through <see cref="UiInputRouter"/> with the real key map (dod-3-4-8): for each gameplay page whose toggle action has a plain
    /// keyboard binding, pressing the key opens the same "Not in Lifestone M3" message that names the panel as the toolbar button does, and closing it leaves the shell as it was.
    /// </summary>
    [RequiresLocalDatsFact]
    public void Every_panel_key_of_the_real_key_map_opens_the_NotInM3_message_naming_its_panel()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        var bindings = rig.Manager.Input.Bindings!;
        // The play host registers the gameplay map (0x10000009, the map that holds the panel keys) with the log-off key, which passes every other action on; the manager then
        // answers a panel action by its visibility-toggle registration (the host window).
        var endKey = new Lifestone.Client.Play.EndSessionKey(rig.Session, rig.Log);
        Assert.True(rig.Manager.Input.RegisterInputMap(Lifestone.Client.Play.EndSessionKey.InputMap, endKey, 0));
        var maps = rig.Manager.Input.InputMaps.Select(m => m.InputMapId).ToHashSet(); // a binding counts when its map has a registrant
        var tried = new List<string>();
        foreach (var page in GamePanelHost.Pages.Where(p => screen.PanelHost!.KindOf(p.PanelId) == PanelPageKind.NotInM3))
        {
            var action = Assert.IsAssignableFrom<UiElement>(screen.PanelHost!.ElementFor(page.PanelId)).VisibilityToggleAction;
            var key = bindings.Bindings.Where(b => b.ActionId == action && maps.Contains(b.InputMapId) && b.Control.DeviceIndex == RetailKeyBindings.KeyboardDevice && b.MetaMode == 0).Select(b => (int)(b.Control.Key >> 16)).FirstOrDefault();
            if (key == 0)
            {
                continue; // no plain key (a button-only page, or a modifier chord)
            }

            Assert.True(rig.Manager.Input.KeyDown(key), $"{page.Name}: key 0x{key:X2} is not taken");
            rig.Manager.Input.KeyUp(key);
            rig.Frame();
            Assert.Equal(NotInM3.Prefix + page.Name, TextOf(OnlyDialog(rig)));
            Assert.False(screen.PanelHost!.IsOpen);
            Assert.True(rig.Click(Assert.IsAssignableFrom<UiElement>(OnlyDialog(rig).GetChildRecursive(UiMessageDialog.OkButton))));
            rig.Run(0.3);
            Assert.Empty(rig.Manager.Dialogs.OpenDialogs);
            tried.Add($"{page.Name} (key 0x{key:X2})");
        }

        Assert.True(tried.Count >= 1, "too few gameplay pages left to the message have a plain key in the real key map (the map has one): " + string.Join(", ", tried));
    }

    [RequiresLocalDatsFact]
    public void The_Use_button_uses_the_selection_through_the_action_layer_and_does_nothing_without_one()
    {
        using var rig = new LoginRig(fixture, liveGame: true, window: 1920, windowHeight: 1080);
        var screen = EnterGame(rig);
        var button = Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(0x1000019D));
        var toolbar = Assert.IsType<GameToolbar>(screen.ToolbarUi);
        var before = rig.Session.ActionLog.Total;

        button.BroadcastElementMessage(UiMessageIds.ButtonClick);
        rig.Frame();
        Assert.Empty(rig.Manager.Dialogs.OpenDialogs); // no NotInM3 message any more
        Assert.Equal(before, rig.Session.ActionLog.Total); // nothing selected: nothing happens
        Assert.Equal(1, toolbar.UseClicks);

        screen.SelectionUi!.SelectedProvider = () => 0x80000001;
        rig.Frame();
        button.BroadcastElementMessage(UiMessageIds.ButtonClick);
        rig.Frame();
        Assert.Empty(rig.Manager.Dialogs.OpenDialogs);
        Assert.Equal(2, toolbar.UseClicks);
        Assert.True(rig.Session.ActionLog.Total > before); // the Use went through the action layer (the offline session refuses it, which the log records)
    }

    [RequiresLocalDatsFact]
    public void The_message_closes_with_OK_and_the_same_button_opens_it_again()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        var button = Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(0x1000019A)); // the map: still a page left to the message
        Assert.True(rig.Click(button));
        rig.Frame();
        var dialog = OnlyDialog(rig);
        Assert.True(rig.Click(Assert.IsAssignableFrom<UiElement>(dialog.GetChildRecursive(UiMessageDialog.OkButton))));
        rig.Frame();
        Assert.Empty(rig.Manager.Dialogs.OpenDialogs);
        Assert.True(rig.Click(button));
        rig.Frame();
        Assert.Contains("Map", TextOf(OnlyDialog(rig)), StringComparison.Ordinal);
    }

    [RequiresLocalDatsFact]
    public void A_press_after_the_mouse_rested_on_a_tabbed_page_button_opens_one_message_and_ends()
    {
        // Regression: the spellbook page is a tabbed panel that shows itself again when hidden; a page hidden at once looped without end when the mouse had hovered first.
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        var button = Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(0x10000198));
        var box = button.ScreenBox;
        rig.Manager.Input.MouseMove((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
        rig.Frame();
        rig.Manager.Input.MouseButton(UiMouseButton.Left, down: true);
        rig.Manager.Input.MouseButton(UiMouseButton.Left, down: false);
        rig.Frame();
        if (screen.PanelControllers!.RegisteredPages.Contains(13u))
        {
            // b3 built the spell page: the press opens it once and it stays (no message, no loop).
            Assert.Empty(rig.Manager.Dialogs.OpenDialogs);
            Assert.True(screen.PanelHost!.ElementFor(13)!.Visible);
            Assert.Equal(13u, screen.PanelHost.CurrentPanel);
            return;
        }

        Assert.Equal(NotInM3.Prefix + "Spellbook and components", TextOf(OnlyDialog(rig)));
        Assert.False(screen.PanelHost!.ElementFor(13)!.Visible);
    }

    [RequiresLocalDatsFact]
    public void The_options_button_calls_its_hook_and_a_handler_with_content_shows_the_page_in_the_host()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        var host = screen.PanelHost!;
        var options = Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(0x1000019B));

        // g6 registers the options panel as the hook's handler: the button opens the page and the same button closes it (GameShellG6Tests holds the rest).
        Assert.True(rig.Click(options));
        rig.Frame();
        Assert.Empty(rig.Manager.Dialogs.OpenDialogs);
        Assert.True(host.IsOpen);
        Assert.Equal(10u, host.CurrentPanel);
        Assert.True(rig.Click(options));
        rig.Frame();
        Assert.False(host.IsOpen);

        // A handler registered in its place is called the same way: the host opens on its page, the same button closes it.
        var calls = 0;
        host.SetPageHandler(10, () =>
        {
            calls++;
            host.SetPanelVisibility(10, true);
        });
        Assert.True(rig.Click(options));
        rig.Frame();
        Assert.Equal(1, calls);
        Assert.True(host.IsOpen);
        Assert.Equal(10u, host.CurrentPanel);
        Assert.True(host.ElementFor(10)!.Visible);
        Assert.True(rig.Click(options));
        rig.Frame();
        Assert.False(host.IsOpen);
        Assert.Equal(0u, host.CurrentPanel);
        Assert.Equal(1, calls);
    }

    [RequiresLocalDatsFact]
    public void A_page_that_overlays_another_brings_it_back_when_it_closes()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        var host = screen.PanelHost!;
        host.SetPanelVisibility(10, true); // options
        host.SetPanelVisibility(3, true); // character information, which overlays
        Assert.Equal(3u, host.CurrentPanel);
        Assert.False(host.ElementFor(10)!.Visible);
        host.SetPanelVisibility(3, false);
        Assert.Equal(10u, host.CurrentPanel);
        Assert.True(host.IsOpen);
        host.SetPanelVisibility(10, false);
        Assert.False(host.IsOpen);
        Assert.Equal(0u, host.CurrentPanel);
    }

    [RequiresLocalDatsFact]
    public void Every_panel_button_names_a_page_of_the_host_and_every_page_has_its_input_action()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        foreach (var (buttonId, panelId) in GameToolbar.PanelButtons)
        {
            var button = Assert.IsAssignableFrom<UiElement>(screen.Root!.GetChildRecursive(buttonId));
            Assert.True(button.TryGetEnumProperty(0x10000029, out var id), $"{buttonId:X8}");
            Assert.Equal(panelId, id);
            Assert.True(button.TryGetEnumProperty(UiPropertyIds.ButtonClickAction, out var action));
            Assert.Equal(action, screen.PanelHost!.ElementFor(panelId)!.VisibilityToggleAction);
        }

        foreach (var page in GamePanelHost.Pages)
        {
            var element = Assert.IsAssignableFrom<UiElement>(screen.PanelHost!.ElementFor(page.PanelId));
            Assert.Equal(page.ElementId, element.ElementId);
            Assert.True(element.TryGetEnumProperty(0x10000029, out var id));
            Assert.Equal(page.PanelId, id);
            Assert.NotEqual(0u, element.VisibilityToggleAction);
        }
    }

    [RequiresLocalDatsFact]
    public void The_link_indicator_is_the_link_status_class_and_starts_good()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        var link = Assert.IsType<UiLinkStatusIndicator>(screen.IndicatorsUi!.Link);
        Assert.Equal(GameIndicators.LinkIndicatorId, link.ElementId);
        Assert.Equal(LinkStatusRules.Good, link.LinkState);
        Assert.Equal(17u, link.State);
        rig.Frame();
        Assert.Equal(17u, link.State); // the offline stand-in has no link to measure: connected and just heard
    }

    [RequiresLocalDatsFact]
    public void The_link_indicator_shows_the_state_for_0_6_25_and_45_seconds_since_the_last_packet()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        var link = screen.IndicatorsUi!.Link!;
        var seconds = 0d;
        var connected = true;
        link.Source = () => new LinkReading(seconds, connected);

        void ReadAfter(double s, int expectedLinkState, uint expectedElementState)
        {
            seconds = s;
            rig.Run(4.2); // the indicator reads the link when more than 4 seconds passed
            Assert.Equal(expectedLinkState, link.LinkState);
            if (expectedLinkState != LinkStatusRules.Failing)
            {
                Assert.Equal(expectedElementState, link.State);
            }
        }

        ReadAfter(0, LinkStatusRules.Good, 17);
        ReadAfter(6, LinkStatusRules.Slow, 18);
        ReadAfter(25, LinkStatusRules.Failing, 19);
        ReadAfter(45, LinkStatusRules.Bad, 20);
        connected = false;
        ReadAfter(0, LinkStatusRules.Bad, 20);
        connected = true;
        ReadAfter(1, LinkStatusRules.Good, 17);
    }

    [RequiresLocalDatsFact]
    public void The_failing_link_flashes_between_states_18_and_19_every_three_quarters_of_a_second()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        var link = screen.IndicatorsUi!.Link!;
        link.Source = () => new LinkReading(30, true);
        rig.Run(4.2);
        Assert.Equal(LinkStatusRules.Failing, link.LinkState);
        var seen = new HashSet<uint>();
        for (var i = 0; i < 180; i++)
        {
            rig.Frame();
            seen.Add(link.State);
        }

        Assert.Contains(18u, seen);
        Assert.Contains(19u, seen);
        Assert.DoesNotContain(17u, seen);
        Assert.DoesNotContain(20u, seen);
    }

    [Theory]
    [InlineData(0, true, 1)]
    [InlineData(5, true, 1)]
    [InlineData(5.01, true, 2)]
    [InlineData(20, true, 2)]
    [InlineData(20.01, true, 3)]
    [InlineData(40, true, 3)]
    [InlineData(40.01, true, 4)]
    [InlineData(0, false, 4)]
    [InlineData(100, false, 4)]
    public void The_link_state_rules_are_retails_thresholds(double seconds, bool connected, int expected)
    {
        Assert.Equal(expected, LinkStatusRules.LinkStateFor(new LinkReading(seconds, connected)));
        Assert.Equal((uint)(16 + expected), LinkStatusRules.StateFor(expected));
    }
}
