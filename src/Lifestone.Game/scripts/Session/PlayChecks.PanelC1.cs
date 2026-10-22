using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Toolbar;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;

namespace Lifestone.Game.Sessions;

/// <summary>
/// Slice pa (the combat panel and the power bar), docs/milestones/m3-plan.md section 4.8 row "pa". The windows are floaty windows of the gameplay layout, not host pages: they are reached through the toolbar's
/// combat buttons and the Action API on the stand-in. Melee mode (the toolbar's button for peace, <c>ToggleCombatMode</c>) shows the combat window in its melee state with the floaty that holds it and the toolbar's melee button;
/// magic mode (an action: the button picks the wielded weapon's mode) hides the combat window and shows the magic button; the button for the mode in use returns to peace and hides everything. The power bar is judged
/// hidden throughout (it is the advanced UI's bar; the basic UI charges the combat window's meter). A fourth row (M4 1.3 s1) clicks the Medium button on the stand-in's selected tusker: one attack is sent from the UI, the meter charges, and it is empty and the mode forgotten after the 0x01A7 that ends the run. With <c>--play-frames</c> the review frames <c>c1-combat</c> and <c>c1-powerbar</c> (taken during that attack) are saved under <c>panels/</c>. Ends in peace.
/// </summary>
public sealed partial class PlayChecks
{
    private async partial Task PanelC1ChecksAsync(GamePlayScreen shell)
    {
        const string meleeName = "the-combat-button-puts-the-player-in-melee-mode-and-the-combat-window-shows-with-the-melee-button";
        const string magicName = "magic-mode-hides-the-combat-window-and-shows-the-magic-button";
        const string peaceName = "the-mode-button-returns-to-peace-and-hides-the-windows";
        var session = _host.Session;
        var root = shell.Root;
        var combat = root?.GetChildRecursive(GameCombatPanel.WindowId) as GameCombatPanel;
        var bar = root?.GetChildRecursive(GamePowerBar.WindowId) as GamePowerBar;
        var toolbar = shell.ToolbarUi;
        if (session is null || root is null || combat is null || bar is null || toolbar is null)
        {
            foreach (var name in new[] { meleeName, magicName, peaceName })
            {
                Check(name, false, $"session={session is not null} combat_window={combat is not null} power_bar={bar is not null} toolbar={toolbar is not null}");
            }

            return;
        }

        var floaty = combat.Parent as UiFloatyCombatPanel;
        var cast = root.GetChildRecursive(0x10000061); // the spellcasting window (pb's) shares the floaty
        bool FloatyFollows() => floaty is not null && floaty.Visible == (combat.Visible || cast?.Visible == true);
        var q = session.State.Self.Qualities;
        if (q.CombatMode is not (0 or CombatModes.NonCombat))
        {
            var reset = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
            await WaitFor(() => reset.IsCompleted, 10.0);
        }

        // The spellcasting window (pb's) is a plain container until its controller lands and the layout shows it: hide it as pb's controller will outside magic mode, so the floaty follows the combat window alone here.
        cast?.SetVisible(false);
        await Frames(2);

        var expected = CombatModeRules.DefaultMode(session.State, out var blockedBy);
        var wantMode = (int)expected;
        var press = root.GetChildRecursive(GameToolbar.CombatButtonIds[0]);
        press?.BroadcastElementMessage(Lifestone.Client.RetailUi.Core.UiMessageIds.ButtonClick);
        var changed = await WaitFor(() => q.CombatMode == wantMode, 10.0);
        await Frames(3);
        var meleeButtonOnly = OnlyCombatButton(root, GameToolbar.CombatButtonIds[1]);
        Check(
            meleeName,
            changed && blockedBy is null && expected == CombatMode.Melee && combat.Visible && combat.State == GameCombatPanel.MeleeState && floaty?.Visible == true && FloatyFollows() && meleeButtonOnly && !bar.Visible && toolbar.CombatPresses == 1,
            $"mode={q.CombatMode} want={wantMode} combat_visible={combat.Visible} combat_state=0x{combat.State:X8} floaty_visible={floaty?.Visible} melee_button_only={meleeButtonOnly} power_bar_visible={bar.Visible} presses={toolbar.CombatPresses}");

        // M4 3.1 g: the magic mode needs a caster in hand (the combat-mode guard): from peace, the wand is wielded first and put away after the peace row.
        var toPeace = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
        await WaitFor(() => toPeace.IsCompleted, 10.0);
        var caster = await HoldCasterAsync(session);
        var magic = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
        var inMagic = await WaitFor(() => magic.IsCompleted && q.CombatMode == CombatModes.Magic, 10.0);
        await Frames(3);
        var magicButtonOnly = OnlyCombatButton(root, GameToolbar.CombatButtonIds[3]);
        Check(
            magicName,
            inMagic && !combat.Visible && !combat.IsShown && FloatyFollows() && magicButtonOnly && !bar.Visible,
            $"mode={q.CombatMode} combat_visible={combat.Visible} combat_shown={combat.IsShown} floaty_visible={floaty?.Visible} magic_button_only={magicButtonOnly} power_bar_visible={bar.Visible}");

        // The button visible for the mode in use toggles back to peace.
        root.GetChildRecursive(GameToolbar.CombatButtonIds[3])?.BroadcastElementMessage(Lifestone.Client.RetailUi.Core.UiMessageIds.ButtonClick);
        var peace = await WaitFor(() => q.CombatMode == CombatModes.NonCombat, 10.0);
        await Frames(3);
        var peaceButtonOnly = OnlyCombatButton(root, GameToolbar.CombatButtonIds[0]);
        Check(
            peaceName,
            peace && !combat.Visible && FloatyFollows() && peaceButtonOnly && !bar.Visible && toolbar.CombatPresses == 2,
            $"mode={q.CombatMode} combat_visible={combat.Visible} floaty_visible={floaty?.Visible} peace_button_only={peaceButtonOnly} power_bar_visible={bar.Visible} presses={toolbar.CombatPresses}");

        await StowAsync(session, caster); // the attack row after this presses the melee button: with a caster in hand the toolbar would pick magic
        await PanelC1AttackRowAsync(shell, combat, bar);

        if (_framesDirectory is { } directory)
        {
            var captured = await SaveWindowFramesAsync(shell, combat, directory);
            Log.Info(LogCategory, $"window frames done captured={captured}/{PanelFramesPlan.WindowFrames.Count}");
        }
    }

    private bool _c1AttackFrameCaptured;

    // M4 1.3 s1: the combat window's Medium button on the stand-in's selected tusker, through the UI's own element messages (a mouse down and the click). Judged: the mode is melee; ONE attack was sent, from the Ui, for the
    // tusker, at the Medium height and the slider's power; the meter charged above 0 before the send and showed a level during the run; after the 0x01A7 that ends the run the meter is 0, the bar's mode forgotten and the
    // power bar window hidden. With --play-frames the c1-powerbar frame is taken while the power charges. Ends in peace with nothing selected.
    private async Task PanelC1AttackRowAsync(GamePlayScreen shell, GameCombatPanel combat, GamePowerBar bar)
    {
        const string name = "the-combat-windows-medium-click-on-the-selected-tusker-sends-one-attack-and-the-meter-charges-and-empties-after-the-done";
        var session = _host.Session;
        if (session is null || _host.StandIn is not { } server || shell.Root is not { } root || combat.GetChildRecursive(GameCombatPanel.MeterId) is not UiMeter meter
            || combat.GetChildRecursive(GameCombatPanel.SliderId) is not UiScrollbar slider || combat.HeightButton(2) is not { } medium || combat.Attacks is not { } attacks)
        {
            Check(name, false, $"session={session is not null} stand_in={_host.StandIn is not null} meter_slider_button_or_driver_missing");
            return;
        }

        var q = session.State.Self.Qualities;
        server.PlayerHitRate = 1.0;
        root.GetChildRecursive(GameToolbar.CombatButtonIds[0])?.BroadcastElementMessage(UiMessageIds.ButtonClick); // the button picks melee (fists)
        var inMelee = await WaitFor(() => q.CombatMode == CombatModes.Melee, 10.0);
        PanelFramesPlan.PlaceAttackFrameTusker(session, server);
        var present = await WaitFor(() => session.State.Objects.Contains(PanelFramesPlan.AttackFrameTusker), 10.0);
        _mode.Selection.Select(PanelFramesPlan.AttackFrameTusker);
        var selected = await WaitFor(() => shell.SelectionUi?.Selected == PanelFramesPlan.AttackFrameTusker, 5.0);
        var oldPower = combat.RequestedPower;
        slider.SetScrollbarPosition(PanelFramesPlan.AttackFramePower);
        var requests = new List<AttackRequested>();
        using var subscription = session.Events.Subscribe<AttackRequested>((in AttackRequested e) => requests.Add(e));
        medium.BroadcastElementMessage(UiMessageIds.MouseDown); // SetRequestedAttackHeight: the request starts, the bar begins
        medium.BroadcastElementMessage(UiMessageIds.ButtonClick); // EndAttackRequest: the attack goes when the charge reaches the slider
        var chargedBeforeSend = await WaitFor(() => attacks.Building && meter.Position >= PanelFramesPlan.AttackFrameLevel && requests.Count == 0, 10.0);
        var midLevel = meter.Position;
        _c1AttackFrameCaptured = false;
        if (chargedBeforeSend && _framesDirectory is { } directory)
        {
            await Frames(1);
            var folder = PanelFramesPlan.FolderFor(ShellFramesPlan.FolderFor(directory, _framesSize.X, _framesSize.Y));
            var frame = PanelFramesPlan.WindowFrames[1];
            if (combat.IsShown && SaveCheckedFrame($"{folder}/{PanelFramesPlan.FileName(frame)}", frame.Name))
            {
                _c1AttackFrameCaptured = true;
            }
            else
            {
                FrameFailed($"{frame.Name}: the combat window did not show for the attack frame (shown={combat.IsShown})");
            }
        }

        var maxRun = 0f;
        var sent = await WaitFor(() => requests.Count >= 1, 10.0);
        var ended = await WaitFor(() =>
        {
            maxRun = Math.Max(maxRun, meter.Position);
            return sent && !session.Attack.Active && session.Attack.State == AttackSequenceState.Done;
        }, AttackSequenceSeconds);
        if (!ended)
        {
            await WaitFor(() => session.Actions.CancelAttack(ActionSource.Host).IsCompleted, 10.0);
            await WaitFor(() => !session.Attack.Active, 10.0);
        }

        await Frames(3);
        var first = requests.Count > 0 ? requests[0] : default;
        var emptied = meter.Position == 0f && attacks.Mode == PowerBarMode.Undefined && !attacks.Building && !bar.Visible;
        slider.SetScrollbarPosition(oldPower);
        _mode.Selection.Clear();
        root.GetChildRecursive(GameToolbar.CombatButtonIds[1])?.BroadcastElementMessage(UiMessageIds.ButtonClick); // the melee button toggles back to peace
        var peace = await WaitFor(() => q.CombatMode == CombatModes.NonCombat, 10.0);
        Check(
            name,
            inMelee && present && selected && chargedBeforeSend && sent && requests.Count == 1 && first.Source == ActionSource.Ui && first.Target == PanelFramesPlan.AttackFrameTusker && first.Height == AttackHeight.Medium && !first.Missile
                && Math.Abs(first.Power - PanelFramesPlan.AttackFramePower) < 0.001f && maxRun > 0f && ended && emptied && attacks.Sends == 1 && peace,
            $"melee={inMelee} present={present} selected={selected} charged_before_send={chargedBeforeSend} mid_level={midLevel:0.00} sends={requests.Count} source={first.Source} height={first.Height} power={first.Power:0.00} run_meter_max={maxRun:0.00} sequence_ended={ended} emptied={emptied} (meter={meter.Position:0.00} mode={attacks.Mode} bar_visible={bar.Visible}) peace={peace}");
    }

    // Saves the combat window's review frames (PanelFramesPlan.WindowFrames) under <frames dir>/panels/: the window's mode notice, the shot, the mode put back. c1-powerbar is taken by the attack row above, during a real attack;
    // this counts it. Nothing is begun by hand.
    private async Task<int> SaveWindowFramesAsync(GamePlayScreen shell, GameCombatPanel combat, string directory)
    {
        var folder = PanelFramesPlan.FolderFor(ShellFramesPlan.FolderFor(directory, _framesSize.X, _framesSize.Y));
        var captured = 0;
        var restore = combat.Mode;
        foreach (var frame in PanelFramesPlan.WindowFrames)
        {
            if (frame.ByAttack)
            {
                captured += _c1AttackFrameCaptured ? 1 : 0; // taken during the attack (a miss was reported by the row)
                continue;
            }

            combat.SetCombatMode(frame.CombatMode);
            await Frames(8);
            var window = shell.Root?.GetChildRecursive(frame.WindowId);
            if (window?.IsShown == true && SaveCheckedFrame($"{folder}/{PanelFramesPlan.FileName(frame)}", frame.Name))
            {
                captured++;
            }
            else
            {
                FrameFailed($"{frame.Name}: the window did not show (shown={window?.IsShown})");
            }

            combat.SetCombatMode(restore);
            await Frames(2);
        }

        return captured;
    }

    private static bool OnlyCombatButton(Lifestone.Client.RetailUi.Core.UiElement root, uint shown)
    {
        foreach (var id in GameToolbar.CombatButtonIds)
        {
            if (root.GetChildRecursive(id) is not { } button || button.Visible != (id == shown))
            {
                return false;
            }
        }

        return true;
    }
}
