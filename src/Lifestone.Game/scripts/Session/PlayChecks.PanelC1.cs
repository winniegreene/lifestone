using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
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
/// hidden throughout (nothing in M3 begins it: the attack is <c>NotInM3</c>). With <c>--play-frames</c> the two review frames <c>c1-combat</c> and <c>c1-powerbar</c> are saved under <c>panels/</c>. Ends in peace.
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

        if (_framesDirectory is { } directory)
        {
            var captured = await SaveWindowFramesAsync(shell, combat, bar, directory);
            Log.Info(LogCategory, $"window frames done captured={captured}/{PanelFramesPlan.WindowFrames.Count}");
        }
    }

    // Saves the combat window's and the power bar's review frames (PanelFramesPlan.WindowFrames) under <frames dir>/panels/: the window's mode notice, the shot, the mode put back. The power bar is begun by hand at
    // WindowFrame.PowerBarLevel, a placeholder: nothing in M3 charges a bar.
    private async Task<int> SaveWindowFramesAsync(GamePlayScreen shell, GameCombatPanel combat, GamePowerBar bar, string directory)
    {
        var folder = PanelFramesPlan.FolderFor(ShellFramesPlan.FolderFor(directory, _framesSize.X, _framesSize.Y));
        var captured = 0;
        var restore = combat.Mode;
        foreach (var frame in PanelFramesPlan.WindowFrames)
        {
            combat.SetCombatMode(frame.CombatMode);
            if (frame.DrivePowerBar)
            {
                bar.Begin(PowerBarMode.Advanced, frame.CombatMode);
                bar.SetLevel(PowerBarMode.Advanced, WindowFrame.PowerBarLevel);
            }

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

            if (frame.DrivePowerBar)
            {
                bar.Finish(PowerBarMode.Advanced);
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
