using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.LifestonePanels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Data.Tests;
using Lifestone.Net.Session;

namespace Lifestone.Client.Tests.RetailUi.LifestonePanelTests;

/// <summary>
/// The tools panel in the in-game shell over the real layouts (docs/retail-ui.md section 22.1): hidden at login, F7 opens and closes it, a search narrows the list, a click on a row teleports, Where am I
/// and Add XP answer in the panel and the chat, and the two refusals are retail message dialogs. The stand-in session that answers the world is a placeholder server.
/// </summary>
public sealed class ToolsPanelTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static readonly StandInVisit Visit = new() { EnterAfter = 0.2, UnhideAfter = 1.0, LogOffDelay = 1.0 };

    private (LoginRig Rig, GamePlayScreen Screen, ToolsPanel Panel) Enter()
    {
        var rig = new LoginRig(
            fixture,
            h =>
            {
                StandInSessions.CharacterSelect(h);
                StandInSessions.PlayerVisits(h, Visit);
                StandInSessions.AnswerWorld(h, Visit, TimeSpan.FromSeconds(0.1));
            },
            startMode: UiModes.GamePlay,
            liveGame: true);
        rig.Run(3.0);
        var screen = Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        return (rig, screen, Assert.IsType<ToolsPanel>(screen.ToolsUi));
    }

    private static void Tap(LoginRig rig, int dik)
    {
        rig.Manager.Input.KeyDown(dik);
        rig.Manager.Input.KeyUp(dik);
        rig.Frame();
    }

    [RequiresLocalDatsFact]
    public void The_panel_is_made_from_tools_json_when_it_is_first_opened_and_sits_over_the_shell()
    {
        var (rig, screen, panel) = Enter();
        using var _ = rig;

        // The shell's own build does not make it (a first build costs 35 ms: the frame that builds the shell is already the heaviest of the login).
        Assert.Equal(SessionPhase.InWorld, rig.Session.State.Phase);
        Assert.Null(panel.Window);
        Assert.False(panel.IsOpen);
        Tap(rig, LifestoneHotkeys.ToolsKey);
        Assert.True(panel.IsOpen);
        Assert.NotNull(panel.Window);
        Assert.Same(screen.Root, panel.Window!.Parent);
        Assert.Equal(ToolsPanel.WindowId, panel.Window.ElementId);
        Tap(rig, LifestoneHotkeys.ToolsKey);
        Assert.False(panel.IsOpen);
        Assert.Equal(ToolsModel.PageSize, panel.Rows.Count);
        Assert.NotNull(panel.SearchField);
        Assert.NotNull(panel.AmountField);
        Assert.DoesNotContain(rig.Log.Entries, e => e.Message.Contains("TODO(g7-tools-layout)", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void F7_toggles_the_panel_even_while_the_search_field_has_the_focus_and_Scroll_Lock_is_the_inspectors()
    {
        var (rig, screen, panel) = Enter();
        using var _ = rig;
        var hotkeys = Assert.IsType<LifestoneHotkeys>(screen.Hotkeys);

        Tap(rig, LifestoneHotkeys.ToolsKey);
        Assert.True(panel.IsOpen);
        Assert.Same(panel.SearchField, rig.Manager.FocusElement);
        Assert.Equal("Holtburg", panel.Rows[0]!.Text.Length > 0 ? "Holtburg" : "(blank)"); // the list is full before anything is typed

        Tap(rig, LifestoneHotkeys.ToolsKey); // the focused search field does not swallow the key
        Assert.False(panel.IsOpen);
        Assert.Equal(2, hotkeys.ToolsPresses);

        // Scroll Lock toggles the automation inspector, which GamePlayScreen wires (lane B).
        var inspector = Assert.IsType<AutomationInspector>(screen.Inspector);
        Tap(rig, LifestoneHotkeys.InspectorKey);
        Assert.Equal(1, hotkeys.InspectorPresses);
        Assert.True(inspector.IsOpen);
        Tap(rig, LifestoneHotkeys.InspectorKey);
        Assert.False(inspector.IsOpen);

        // A host that wires nothing to the key is told so.
        hotkeys.OpenInspector = null;
        Tap(rig, LifestoneHotkeys.InspectorKey);
        Assert.Equal(3, hotkeys.InspectorPresses);
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("TODO(g7-inspector-unwired)", StringComparison.Ordinal));
        var opened = 0;
        hotkeys.OpenInspector = () => opened++;
        Tap(rig, LifestoneHotkeys.InspectorKey);
        Assert.Equal(1, opened);
    }

    [RequiresLocalDatsFact]
    public void Typing_in_the_search_field_narrows_the_list_and_a_click_on_a_row_teleports()
    {
        var (rig, screen, panel) = Enter();
        using var _ = rig;
        Tap(rig, LifestoneHotkeys.ToolsKey);

        foreach (var c in "holtburg")
        {
            rig.Manager.Input.Character(c);
        }

        rig.Frame();
        Assert.Equal("holtburg", panel.SearchField!.Text);
        Assert.Equal("holtburg", panel.Model.Query);
        Assert.Equal("Holtburg", panel.Rows[0]!.Text);
        Assert.True(panel.Rows[0]!.Visible);
        Assert.Equal(panel.Model.Page.Count, panel.Rows.Count(r => r!.Visible));

        var teleportsBefore = rig.Session.State.Self.Stamps.Teleport;
        Assert.True(rig.Click(panel.Rows[0]!));
        rig.Run(1.0);
        var place = Assert.IsAssignableFrom<ToolHandle<PlaceResult>>(panel.Model.LastPlace);
        Assert.Equal(ActionOutcomeKind.Confirmed, place.Outcome!.Value.Kind);
        Assert.Equal(ActionSource.Ui, place.Source);
        Assert.Equal(0xA9B40019u, place.Result!.Destination.CellId);
        Assert.NotEqual(teleportsBefore, rig.Session.State.Self.Stamps.Teleport); // the server's teleport arrived: portal space follows it
        Assert.StartsWith("Teleport started: Holtburg", panel.Model.Status, StringComparison.Ordinal);
        Assert.Contains("Teleport started", Statuses(panel), StringComparison.Ordinal);
        Assert.Contains(Chat(rig), l => l == "Lifestone: teleporting to Holtburg...");
    }

    [RequiresLocalDatsFact]
    public void Where_am_I_and_Add_XP_answer_in_the_panel_and_the_chat()
    {
        var (rig, screen, panel) = Enter();
        using var _ = rig;
        Tap(rig, LifestoneHotkeys.ToolsKey);

        panel.Model.ClientPosition = () => rig.Session.State.Self.Position; // the test double for the physics body (the stand-in server answers with the same position)
        Assert.True(rig.Click(panel.Window!.GetChildRecursive(ToolsPanel.WhereButtonId)!));
        rig.Run(1.0);
        Assert.Equal(ActionOutcomeKind.Confirmed, panel.Model.LastWhere!.Outcome!.Value.Kind);
        Assert.True(panel.Model.LastAgreement is { Agrees: true });
        Assert.StartsWith("server: 0x", panel.Model.WhereText, StringComparison.Ordinal);
        Assert.Equal(panel.Model.WhereText, ((Lifestone.Client.RetailUi.Text.UiTextElement)panel.Window.GetChildRecursive(ToolsPanel.WhereTextId)!).Text);
        var log = screen.Chat!.Main!.LogText;
        Assert.Contains("Lifestone: server 0x", log, StringComparison.Ordinal);
        Assert.Contains("agrees within 0.5 m", log, StringComparison.Ordinal);

        panel.AmountField!.SetText("2500");
        Assert.True(rig.Click(panel.Window.GetChildRecursive(ToolsPanel.AddXpButtonId)!));
        rig.Run(1.0);
        Assert.Equal(2500, panel.Model.LastXp!.Result!.Added);
        Assert.Contains("Added 2,500 experience", Statuses(panel), StringComparison.Ordinal);
        Assert.Contains(Chat(rig), l => l.StartsWith("Lifestone: added 2,500 experience", StringComparison.Ordinal));

        panel.AmountField.SetText("lots");
        rig.Click(panel.Window.GetChildRecursive(ToolsPanel.AddXpButtonId)!);
        rig.Frame();
        Assert.StartsWith("Type an amount", Statuses(panel), StringComparison.Ordinal);

        Assert.True(rig.Click(panel.Window.GetChildRecursive(ToolsPanel.HideButtonId)!));
        Assert.False(panel.IsOpen);
    }

    [RequiresLocalDatsFact]
    public void Up_and_Down_page_through_the_list()
    {
        var (rig, screen, panel) = Enter();
        using var _ = rig;
        Tap(rig, LifestoneHotkeys.ToolsKey);
        var first = panel.Rows[0]!.Text;

        Assert.True(rig.Click(panel.Window!.GetChildRecursive(ToolsPanel.DownId)!));
        Assert.Equal(ToolsModel.PageSize, panel.Model.Top);
        Assert.NotEqual(first, panel.Rows[0]!.Text);
        Assert.True(rig.Click(panel.Window.GetChildRecursive(ToolsPanel.UpId)!));
        Assert.Equal(0, panel.Model.Top);
        Assert.Equal(first, panel.Rows[0]!.Text);
    }

    [RequiresLocalDatsFact]
    public void Without_the_mod_the_panel_shows_the_mod_missing_message_dialog_and_stays_closed()
    {
        var (rig, screen, panel) = Enter();
        using var _ = rig;

        rig.Session.ActionApi.PublishToolsUnavailable(ToolAvailability.ModMissing, "the server answered Unknown command: lifestone");
        Tap(rig, LifestoneHotkeys.ToolsKey);

        Assert.False(panel.IsOpen);
        Assert.Equal(1, panel.Refusals);
        Assert.Equal(ToolsPanel.ModMissingText, panel.LastDialogText);
        Assert.Contains("Lifestone: " + ToolsPanel.ModMissingText, screen.Chat!.Main!.LogText, StringComparison.Ordinal); // a refused F7 is in the chat too
        var dialog = Assert.IsAssignableFrom<UiDialog>(panel.Dialog);
        Assert.Equal(ToolsPanel.ModMissingText, Assert.IsAssignableFrom<Lifestone.Client.RetailUi.Text.UiTextElement>(dialog.GetChildRecursive(UiDialog.TextElement)).Text);
        Assert.Contains("Lifestone server mod", ToolsPanel.ModMissingText, StringComparison.Ordinal);
    }

    [RequiresLocalDatsFact]
    public void A_server_that_is_not_on_loopback_closes_the_panel_with_the_refusal_dialog()
    {
        var (rig, screen, panel) = Enter();
        using var _ = rig;
        Tap(rig, LifestoneHotkeys.ToolsKey);
        Assert.True(panel.IsOpen);

        rig.Session.ActionApi.PublishToolsUnavailable(ToolAvailability.NotLoopback, "the server is not on loopback");
        rig.Frame();

        Assert.False(panel.IsOpen);
        Assert.Equal(ToolsPanel.NotLoopbackText, panel.LastDialogText);
        Assert.Contains("Lifestone: " + ToolsPanel.NotLoopbackText, screen.Chat!.Main!.LogText, StringComparison.Ordinal);
        Assert.NotNull(panel.Dialog);
        Assert.Contains("not on loopback", ToolsPanel.NotLoopbackText, StringComparison.Ordinal);
    }

    /// <summary>A stand-in for the dial: a status a test sets, and a Changed it raises.</summary>
    private sealed class FakeDial : IAutopilotStatusSource
    {
        public AutopilotStatus Status { get; set; }

        public event Action? Changed;

        public void Set(AutopilotMode mode)
        {
            Status = new AutopilotStatus(mode, string.Empty, string.Empty, string.Empty, 0, 0, 0);
            Changed?.Invoke();
        }
    }

    [Theory]
    [InlineData(AutopilotMode.Off, "Autopilot")]
    [InlineData(AutopilotMode.Done, "Autopilot")]
    [InlineData(AutopilotMode.Running, "Stop")]
    [InlineData(AutopilotMode.Stopping, "Stopping")]
    [InlineData(AutopilotMode.Interrupted, "Resume")]
    [InlineData(AutopilotMode.Halted, "Resume")]
    public void M5_7_s1_4_r17_the_autopilot_buttons_text_says_what_a_press_will_do_for_each_mode(AutopilotMode mode, string text)
    {
        Assert.Equal(text, ToolsPanel.AutopilotButtonText(mode));
    }

    [RequiresLocalDatsFact]
    public void M5_7_s1_4_r17_the_autopilot_button_follows_the_dials_mode_from_its_status_only()
    {
        var (rig, _, panel) = Enter();
        using var _ = rig;
        var dial = new FakeDial();
        panel.Autopilot = dial; // set before the window exists: the text is written when it is made
        Assert.Equal("Autopilot", panel.AutopilotText);
        Tap(rig, LifestoneHotkeys.ToolsKey);
        var button = Assert.IsAssignableFrom<UiTextElement>(panel.Window!.GetChildRecursive(ToolsPanel.AutopilotButtonId));
        Assert.Equal("Autopilot", button.Text);

        foreach (var (mode, text) in new[] { (AutopilotMode.Running, "Stop"), (AutopilotMode.Stopping, "Stopping"), (AutopilotMode.Interrupted, "Resume"), (AutopilotMode.Halted, "Resume"), (AutopilotMode.Done, "Autopilot"), (AutopilotMode.Off, "Autopilot") })
        {
            dial.Set(mode);
            Assert.Equal((text, text), (button.Text, panel.AutopilotText));
        }

        panel.Autopilot = null; // a replaced source no longer moves the button
        dial.Set(AutopilotMode.Running);
        Assert.Equal("Autopilot", button.Text);
    }

    [RequiresLocalDatsFact]
    public void M6_6_md2_the_jobs_button_sits_above_the_autopilot_button_and_toggles_the_menu_the_host_wired()
    {
        var (rig, screen, panel) = Enter();
        using var _ = rig;
        Tap(rig, LifestoneHotkeys.ToolsKey);
        var jobs = Assert.IsAssignableFrom<UiTextElement>(panel.Window!.GetChildRecursive(ToolsPanel.JobsButtonId));
        var autopilot = panel.Window.GetChildRecursive(ToolsPanel.AutopilotButtonId)!;
        Assert.Equal("Jobs", jobs.Text);
        Assert.Equal(autopilot.ScreenBox.X0, jobs.ScreenBox.X0); // the same column, above it
        Assert.True(jobs.ScreenBox.Y1 <= autopilot.ScreenBox.Y0);

        var menu = Assert.IsType<AutomationMenu>(screen.Menu);
        panel.ToggleJobs = menu.Toggle; // the play host's wiring (AutopilotHost.WireShell)
        Assert.True(rig.Click(jobs));
        Assert.Equal(1, panel.JobsPresses);
        Assert.True(menu.IsOpen);
        Assert.True(rig.Click(jobs));
        Assert.Equal(2, panel.JobsPresses);
        Assert.False(menu.IsOpen);
        Assert.DoesNotContain(rig.Log.Entries, e => e.Message.Contains("TODO(g7-tools-layout)", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void M6_6_md2_an_unwired_jobs_button_says_so_in_the_status_and_the_chat()
    {
        var (rig, screen, panel) = Enter();
        using var _ = rig;
        Tap(rig, LifestoneHotkeys.ToolsKey);
        panel.PressJobs();
        rig.Frame();
        Assert.Equal(1, panel.JobsPresses);
        Assert.Contains("automation menu is not available", Statuses(panel), StringComparison.Ordinal);
        Assert.Contains("Lifestone: The automation menu is not available in this host.", screen.Chat!.Main!.LogText, StringComparison.Ordinal);
    }

    private static string Statuses(ToolsPanel panel) =>
        ((Lifestone.Client.RetailUi.Text.UiTextElement)panel.Window!.GetChildRecursive(ToolsPanel.StatusId)!).Text;

    private static List<string> Chat(LoginRig rig)
    {
        var lines = new List<string>();
        for (var i = 0; i < rig.Session.State.Chat.Count; i++)
        {
            lines.Add(rig.Session.State.Chat[i].Text);
        }

        return lines;
    }
}
