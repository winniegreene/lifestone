using Godot;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.LifestonePanels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// The play checks of Lifestone's tools panel (M2 G7 lane A; docs/retail-ui.md section 22.1): the panel is opened with F7, searched by typing, a place is picked with a click (its
/// <c>place poi</c> request is answered by the stand-in, a placeholder server, with the teleport and portal space), then Where am I and Add XP are pressed (the stand-in answers those
/// too, with placeholder values), and the panel is closed with F7. Everything goes through the shell's own input router, the panel's buttons and the Action API.
/// </summary>
public sealed partial class PlayChecks
{
    /// <summary>Clicks the middle of <paramref name="element"/> through the shell's input and waits two frames.</summary>
    private static async Task ClickAsync(PlayChecks checks, UiElementManager manager, UiElement element)
    {
        var box = element.ScreenBox;
        var x = (box.X0 + box.X1) / 2;
        var y = (box.Y0 + box.Y1) / 2;
        manager.Input.MouseMove(x, y);
        await checks.Frames(2);
        manager.Input.MouseMove(x, y);
        manager.Input.MouseButton(UiMouseButton.Left, true);
        manager.Input.MouseButton(UiMouseButton.Left, false);
        await checks.Frames(2);
    }

    private static void TapKey(UiElementManager manager, int dik)
    {
        manager.Input.KeyDown(dik);
        manager.Input.KeyUp(dik);
    }

    /// <summary>F7, a typed search, a click on the first row: returns the teleport the panel asked for, or null.</summary>
    private async Task<ToolHandle<PlaceResult>?> ToolsPanelPlaceAsync(RetailUiLayer? ui)
    {
        if (ui?.Manager is not { } manager || ui.Screens?.Flow.CurrentScreen is not GamePlayScreen { ToolsUi: { } panel, Hotkeys: { } hotkeys })
        {
            Check("the-tools-panel-is-in-the-shell-unbuilt-and-its-keys-are-registered", false, "the shell has no tools panel");
            return null;
        }

        Check("the-tools-panel-is-in-the-shell-unbuilt-and-its-keys-are-registered", panel.Window is null && !panel.IsOpen && manager.Input.InputMaps.Any(m => m.InputMapId == LifestoneHotkeys.InputMap), $"window_made_before_the_first_open={panel.Window is not null} open={panel.IsOpen}");

        TapKey(manager, LifestoneHotkeys.ToolsKey);
        await Frames(4);
        Check("f7-opens-the-tools-panel-and-focuses-its-search-field", panel.IsOpen && hotkeys.ToolsPresses == 1 && ReferenceEquals(manager.FocusElement, panel.SearchField), $"open={panel.IsOpen} presses={hotkeys.ToolsPresses} focus={manager.FocusElement?.GetType().Name ?? "none"}");

        // p2: the throwaway leg's driver does the typing and the click (--login-check=throwaway presses the same place the same way).
        var pressPlace = new ThrowawayScreens(ui, ui.Session!).PressPlace("holtburg");
        var pressed = await RunOp(ui.Session!, pressPlace, 15);
        Check(
            "typing-in-the-search-field-lists-holtburg-first",
            pressed && pressPlace.Succeeded && panel.Model.Query == "holtburg" && panel.Rows[0]!.Visible && panel.Rows[0]!.Text == "Holtburg" && pressPlace.Text == "Holtburg" && panel.Model.Results.Count >= 2,
            $"failure={pressPlace.Failure} query='{panel.Model.Query}' first='{panel.Rows[0]!.Text}' results={panel.Model.Results.Count}");
        var place = panel.Model.LastPlace;
        Check("a-click-on-a-place-asks-the-server-to-teleport-through-the-action-api", place is { Source: ActionSource.Ui, Kind: ActionKind.ToolPlace }, $"handle={(place is null ? "none" : place.ToString())}");

        // The panel goes away with F7 (the focused search field does not swallow the key), so the portal tunnel is the only thing over the world.
        TapKey(manager, LifestoneHotkeys.ToolsKey);
        await Frames(2);
        Check("f7-closes-the-tools-panel-again", !panel.IsOpen && hotkeys.ToolsPresses == 2 && !ReferenceEquals(manager.FocusElement, panel.SearchField), $"open={panel.IsOpen} presses={hotkeys.ToolsPresses}");
        return place;
    }

    /// <summary>Where am I and Add XP through the panel, the use trail in the chat and the action log, and the panel's review frame.</summary>
    private async Task ToolsPanelWhereAndXpAsync(IGameSessionView session, RetailUiLayer? ui)
    {
        if (ui?.Manager is not { } manager || ui.Screens?.Flow.CurrentScreen is not GamePlayScreen { ToolsUi: { Window: { } window } panel, Hotkeys: { } hotkeys, Inspector: { } inspector } shell)
        {
            Check("the-tools-panel-answers-where-am-i-and-add-xp", false, "the shell has no tools panel");
            return;
        }

        TapKey(manager, LifestoneHotkeys.ToolsKey);
        await Frames(4);
        Check("f7-opens-the-tools-panel-again", panel.IsOpen, $"open={panel.IsOpen}");

        await ClickAsync(this, manager, window.GetChildRecursive(ToolsPanel.WhereButtonId)!);
        var where = panel.Model.LastWhere;
        var whereDone = where is not null && await WaitFor(() => where.IsCompleted, 10.0);
        await Frames(3);
        var chat = shell.Chat?.Main?.LogText ?? string.Empty;
        Check(
            "where-am-i-compares-the-servers-position-with-the-physics-body-in-the-panel-and-the-chat",
            whereDone && where!.Outcome?.Kind == ActionOutcomeKind.Confirmed && panel.Model.LastAgreement is { Agrees: true, Client: not null } && panel.Model.WhereText.StartsWith("server: 0x", StringComparison.Ordinal)
                && panel.Model.WhereText.Contains("client body", StringComparison.Ordinal) && chat.Contains("agrees within 0.5 m", StringComparison.Ordinal),
            $"outcome={where?.Outcome?.Kind} agreement={panel.Model.LastAgreement?.Agrees} metres={panel.Model.LastAgreement?.Metres:0.00} text='{panel.Model.WhereText.Replace('\n', '|')}' chat_has_agreement={chat.Contains("agrees within 0.5 m", StringComparison.Ordinal)}");

        panel.AmountField!.SetText("2500");
        await ClickAsync(this, manager, window.GetChildRecursive(ToolsPanel.AddXpButtonId)!);
        var xp = panel.Model.LastXp;
        var xpDone = xp is not null && await WaitFor(() => xp.IsCompleted, 10.0);
        await Frames(3);
        chat = shell.Chat?.Main?.LogText ?? string.Empty;
        Check(
            "add-xp-is-confirmed-by-the-server-in-the-chat",
            xpDone && xp!.Outcome?.Kind == ActionOutcomeKind.Confirmed && xp.Result is { Added: 2500 } && chat.Contains("added 2,500 experience", StringComparison.Ordinal) && panel.Model.Status.StartsWith("Added 2,500 experience", StringComparison.Ordinal),
            $"outcome={xp?.Outcome?.Kind} added={xp?.Result?.Added} status='{panel.Model.Status}' chat_has_reply={chat.Contains("added 2,500 experience", StringComparison.Ordinal)}");

        var log = session.ActionLog;
        var uses = Enumerable.Range(0, log.Count).Select(i => log[i]).Where(e => e.Source == ActionSource.Ui && e.Kind is ActionKind.ToolPlace or ActionKind.ToolWhereAmI or ActionKind.ToolAddXp).Select(e => e.Kind).Distinct().ToArray();
        Check("every-use-of-the-panel-is-in-the-action-log-as-a-ui-action", uses.Length == 3, $"kinds=[{string.Join(", ", uses)}]");

        // Scroll Lock toggles lane B's automation inspector (wired in GamePlayScreen): it opens, then Scroll Lock closes it again so the tools frame and the inspector's own checks start clean.
        TapKey(manager, LifestoneHotkeys.InspectorKey);
        await Frames(2);
        Check("scroll-lock-opens-the-inspector", hotkeys.InspectorPresses == 1 && hotkeys.OpenInspector is not null && inspector.IsOpen, $"presses={hotkeys.InspectorPresses} wired={hotkeys.OpenInspector is not null} open={inspector.IsOpen}");
        TapKey(manager, LifestoneHotkeys.InspectorKey);
        await Frames(2);
        Check("scroll-lock-closes-the-inspector-again", hotkeys.InspectorPresses == 2 && !inspector.IsOpen, $"presses={hotkeys.InspectorPresses} open={inspector.IsOpen}");

        await Frames(20);
        SaveReviewFrame("10-shell-tools-panel");
        TapKey(manager, LifestoneHotkeys.ToolsKey);
        await Frames(2);
        Check("f7-closes-the-tools-panel-after-the-uses", !panel.IsOpen && hotkeys.ToolsPresses == 4, $"open={panel.IsOpen} presses={hotkeys.ToolsPresses}");
    }

    /// <summary>
    /// M5 4 h3 (DoD 9), the controls of M5 7 D145: the autopilot's dial, offline. /auto typed into the main chat starts it and locks the movement keys; Move Forward through Godot's input (the person's own path) then does nothing and the chat
    /// says who has the controls; /stop typed into the chat stops him where he stands and unlocks the keys; Num Lock does nothing to the dial (a hint in the chat); the tools panel's Autopilot button hands the controls back and, pressed again
    /// while it runs, is a /stop; the inspector shows its state line; a keypad key means the same with Num Lock on and off. The stations are idle stand-ins (no spell table, no den: the dial's wiring is what is judged; the real stations are
    /// the login check's).
    /// </summary>
    private async Task AutopilotDialAsync(IGameSessionView session, RetailUiLayer? ui)
    {
        if (ui?.Manager is not { } manager || ui.Screens?.Flow.CurrentScreen is not GamePlayScreen { ToolsUi: { Window: { } window } panel, Hotkeys: { } hotkeys, Inspector: { } inspector, Chat: { Main: { } mainChat } chat })
        {
            Check("the-autopilot-dial-is-wired-to-the-chat-the-tools-button-and-the-inspector", false, "the shell has no tools panel or no main chat");
            return;
        }

        // The play host's own node (PlayMode.Install): installed, after the retail UI in the frame, and wiring the chat's controls and the button once the game screen shows.
        await Frames(3);
        var real = _main.GetNodeOrNull<AutopilotHost>("AutopilotHost");
        Check("the-play-host-installs-the-autopilot-dial-and-wires-the-chat-and-the-button", real is { ProcessPriority: 60 } && chat.Autopilot is not null && hotkeys.AutopilotKeyHint is not null && panel.ToggleAutopilot is not null && inspector.Autopilot is not null, $"node={real is not null} chat={chat.Autopilot is not null} num_lock_hint={hotkeys.AutopilotKeyHint is not null} button={panel.ToggleAutopilot is not null} inspector={inspector.Autopilot is not null}");
        if (real is not null)
        {
            _main.RemoveChild(real); // the real dial reads the spell table and the den on its first /auto; this check swaps in a dial over idle stations
            real.Free();
        }

        var log = GodotLogSink.Instance;
        var options = new AutopilotOptions();
        var logic = new AutopilotHostLogic(
            carry =>
            {
                var scenario = new AutopilotScenario(carry.Remaining(options), new IdleStations(), new JourneyContext { Character = "BSD Mage", Account = "test", Log = log, Travel = new PlaceTravel(), Spells = new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>() });
                carry.Apply(scenario);
                return scenario;
            },
            log,
            options);
        var host = AutopilotHost.InstallWith(_main, _host, ui, logic, new AutopilotHostOptions());
        chat.Autopilot = null;
        hotkeys.AutopilotKeyHint = null;
        panel.ToggleAutopilot = null;
        await Frames(3);
        var keys = session.Actions.Move.KeyLock;
        Check("the-autopilot-dial-is-wired-to-the-chat-the-tools-button-and-the-inspector", chat.Autopilot is not null && hotkeys.AutopilotKeyHint is not null && panel.ToggleAutopilot is not null && ReferenceEquals(inspector.Autopilot, logic) && logic.Mode == AutopilotMode.Off && !keys.Locked, $"chat={chat.Autopilot is not null} num_lock_hint={hotkeys.AutopilotKeyHint is not null} button={panel.ToggleAutopilot is not null} mode={logic.Mode} keys_locked={keys.Locked}");

        // Num Lock (the shell router, as F7 is pressed above) no longer starts it: the dial stays off and the chat says what does.
        TapKey(manager, LifestoneHotkeys.AutopilotKey);
        await Frames(4);
        Check("num-lock-does-nothing-to-the-autopilot-and-the-chat-says-what-does", logic.Mode == AutopilotMode.Off && hotkeys.AutopilotPresses == 1 && host.Presses == 0 && mainChat.LogText.Contains(LifestoneHotkeys.AutopilotKeyHintLine, StringComparison.Ordinal), $"mode={logic.Mode} presses={hotkeys.AutopilotPresses} host_presses={host.Presses} hint_in_chat={mainChat.LogText.Contains(LifestoneHotkeys.AutopilotKeyHintLine, StringComparison.Ordinal)}");

        // /auto typed into the main chat (its entry and its Enter: the person's path) starts it from the state it finds and locks the movement keys.
        var talkBefore = chat.TalkSent; // the main chat is shared by the earlier checks: /auto itself must send no talk
        var typed = AutopilotHost.TypeInChat(ui, "/auto");
        var started = await WaitFor(() => logic.Mode == AutopilotMode.Running && logic.Scenario is not null, 5.0);
        await Frames(10);
        Check("typing-auto-starts-the-autopilot-and-locks-the-movement-keys", typed && started && host.Presses == 1 && logic.Mode == AutopilotMode.Running && keys.Locked && logic.StartReason == AutopilotHostLogic.ResumeReason && chat.TalkSent == talkBefore, $"typed={typed} mode={logic.Mode} host_presses={host.Presses} keys_locked={keys.Locked} reason={logic.StartReason} talk_sent={chat.TalkSent - talkBefore} stage={logic.Scenario?.Stage}");

        // The inspector (Scroll Lock) shows the state line.
        TapKey(manager, LifestoneHotkeys.InspectorKey);
        await Frames(4);
        var running = inspector.AutopilotText;
        Check("the-inspector-shows-the-autopilots-state-line", inspector.IsOpen && running.StartsWith("Autopilot: running", StringComparison.Ordinal) && running.Contains("Last decision:", StringComparison.Ordinal), $"text='{running.Replace('\n', '|')}'");
        TapKey(manager, LifestoneHotkeys.InspectorKey);
        await Frames(2);

        // A person's movement key, through Godot's input (Input.ParseInputEvent: the path a real key takes, the retail router and the movement bindings), does nothing while it runs: the lock swallows it and the chat says why.
        var swallowed = keys.Swallowed;
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.W, Keycode = Key.W, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.W, Keycode = Key.W, Pressed = false });
        await Frames(4);
        var notice = mainChat.LogText.Contains(HumanKeyLock.NoticeLine, StringComparison.Ordinal);
        Check("a-movement-key-does-nothing-while-the-autopilot-runs-and-the-chat-says-so", logic.Mode == AutopilotMode.Running && keys.Swallowed == swallowed + 1 && notice && logic.Interrupted == 0, $"mode={logic.Mode} swallowed={keys.Swallowed - swallowed} notice_in_chat={notice} interrupted={logic.Interrupted}");

        // /stop typed into the chat: he stops where he stands, nothing held, the keys unlocked.
        typed = AutopilotHost.TypeInChat(ui, "/stop");
        var stopped = await WaitFor(() => logic.Mode == AutopilotMode.Interrupted, 5.0);
        await Frames(4);
        var stoppedText = AutomationInspector.FormatAutopilot(logic.Status);
        Check("typing-stop-stops-him-where-he-stands-and-unlocks-the-keys", typed && stopped && logic.Interrupted == 1 && !keys.Locked && session.Actions.Move.AutomationHeld == 0 && stoppedText.StartsWith("Autopilot: stopped", StringComparison.Ordinal) && mainChat.LogText.Contains(AutopilotHostLogic.StoppedHereLine, StringComparison.Ordinal), $"typed={typed} mode={logic.Mode} interrupted={logic.Interrupted} keys_locked={keys.Locked} held={session.Actions.Move.AutomationHeld} text='{stoppedText.Split('\n')[0]}'");

        // After /stop the movement key is the person's again: the lock lets it through (a tap: the stand-in server does not follow the body, and the checks after this one compare the two).
        swallowed = keys.Swallowed;
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.W, Keycode = Key.W, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.W, Keycode = Key.W, Pressed = false });
        await Frames(4);
        Check("after-stop-the-movement-key-is-the-persons", keys.Swallowed == swallowed && logic.Mode == AutopilotMode.Interrupted, $"swallowed={keys.Swallowed - swallowed} mode={logic.Mode}");

        // The tools panel's Autopilot button hands the controls back (/auto by the mode).
        TapKey(manager, LifestoneHotkeys.ToolsKey);
        await Frames(4);
        var button = window.GetChildRecursive(ToolsPanel.AutopilotButtonId) as Lifestone.Client.RetailUi.Widgets.UiButton;
        // M5 item 7 s1: the button's text follows the dial; stopped, it says what a press does ("Resume").
        Check("the-tools-panel-has-an-autopilot-button-with-lifestones-own-word", button is { Text: "Resume" }, $"button={(button is null ? "missing" : button.Text)}");
        if (button is not null)
        {
            await ClickAsync(this, manager, button);
        }

        var resumed = await WaitFor(() => logic.Mode == AutopilotMode.Running, 5.0);
        Check("the-panel-button-hands-the-controls-back", resumed && panel.AutopilotPresses == 1 && logic.Resumed == 1 && host.Presses == 2 && keys.Locked, $"mode={logic.Mode} button_presses={panel.AutopilotPresses} resumed={logic.Resumed} host_presses={host.Presses} keys_locked={keys.Locked}");

        // /auto while it runs answers and does nothing else; the button while it runs is a /stop.
        typed = AutopilotHost.TypeInChat(ui, "/auto");
        await Frames(2);
        Check("auto-while-running-answers-and-does-nothing-else", typed && logic.Mode == AutopilotMode.Running && host.Presses == 2 && logic.Resumed == 1, $"mode={logic.Mode} host_presses={host.Presses} resumed={logic.Resumed}");
        if (button is not null)
        {
            await ClickAsync(this, manager, button);
        }

        var buttonStopped = await WaitFor(() => logic.Mode == AutopilotMode.Interrupted, 5.0);
        Check("the-panel-button-while-running-is-a-stop", buttonStopped && logic.Interrupted == 2 && !keys.Locked, $"mode={logic.Mode} interrupted={logic.Interrupted} keys_locked={keys.Locked}");
        TapKey(manager, LifestoneHotkeys.ToolsKey);
        await Frames(2);

        // D59's caveat: the keypad's keys are the same keys with Num Lock on (logical key = the digit) and off (logical key = the arrow or Home/End/...); Lifestone translates the physical code.
        var same = true;
        var detail = new List<string>();
        foreach (var (physical, off) in new[] { (Key.Kp8, Key.Up), (Key.Kp2, Key.Down), (Key.Kp4, Key.Left), (Key.Kp6, Key.Right), (Key.Kp7, Key.Home), (Key.Kp1, Key.End), (Key.Kp9, Key.Pageup), (Key.Kp3, Key.Pagedown), (Key.Kp5, Key.None) })
        {
            var on = ui.Input.Handle(new InputEventKey { PhysicalKeycode = physical, Keycode = physical, Pressed = true });
            ui.Input.Handle(new InputEventKey { PhysicalKeycode = physical, Keycode = physical, Pressed = false });
            var offTaken = ui.Input.Handle(new InputEventKey { PhysicalKeycode = physical, Keycode = off, Pressed = true });
            ui.Input.Handle(new InputEventKey { PhysicalKeycode = physical, Keycode = off, Pressed = false });
            same &= on == offTaken;
            detail.Add($"{physical}:{(on ? "taken" : "free")}/{(offTaken ? "taken" : "free")}");
        }

        Check("a-keypad-key-is-the-same-key-with-num-lock-on-and-off", same, string.Join(' ', detail));

        // Back to the host's own state for the checks after this one (the dial's disposal unlocks the keys, D145).
        host.Logic.Dispose();
        chat.Autopilot = null;
        hotkeys.AutopilotKeyHint = null;
        panel.ToggleAutopilot = null;
        inspector.Autopilot = null;
        _main.RemoveChild(host);
        host.Free();
        Check("the-dial-leaves-the-movement-keys-unlocked", !keys.Locked, $"keys_locked={keys.Locked}");
        await AutomationMenuChecksAsync(session, ui); // M6 6 MD2: the automation menu (the Jobs button, its rows, errand, the inspector's Job line, stop) and its frame 11 (PlayChecks.Menu.cs)
    }

    /// <summary>An idle station for the dial's check: it runs until the check ends.</summary>
    private sealed class IdleStations : IStationFactory
    {
        public IStation Create(StationSpec spec) => new IdleStation(spec.Name);
    }

    private sealed class IdleStation(string name) : IStation
    {
        public string Name => name;

        public StationNeeds Needs => StationNeeds.None;

        public IStationRun Start(JourneyContext context) => new IdleRun();
    }

    private sealed class IdleRun : IStationRun
    {
        public string Name => "idle";

        public string Stage => "idle";

        public string? Failure => null;

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public bool Ok => true;

        public string VerdictFields() => "ok=yes";

        public ScenarioStatus Step(IGameSessionView session, double now) => ScenarioStatus.Running;
    }
}
