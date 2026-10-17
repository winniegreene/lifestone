using Godot;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session;


namespace Lifestone.Game.Sessions;

/// <summary>
/// <c>--play-panel-shots=DIR</c> (M3 2.2 s0, <see cref="PanelFramesPlan"/>): in a plain <c>--play</c> run, once the player is placed and the arrival has settled, each registered panel page is opened in turn
/// through its toolbar action, saved as <c>DIR/&lt;panelId&gt;-&lt;name&gt;.png</c> with the developer HUD hidden (as <c>--play-screenshot</c> does), and closed; then the run quits like <c>--play-screenshot</c>
/// (0, or 1 when a frame could not be saved). With no registered page it logs <see cref="PanelFramesPlan.NoPanelText"/> and quits. With <c>--play-checks</c> (the stand-in) it only saves and the checks go on.
/// </summary>
public sealed partial class PlayMode
{
    /// <summary>Frames a page gets to draw after it opens, before its frame is taken.</summary>
    private const int PanelShotSettleFrames = 40; // 8 was too few for the spellbook's first open on BSD Mage (247 spells filled 8 a frame): its shot showed the page before (M3 2.2 live gate)

    private enum PanelShotPhase
    {
        Open,
        Settle,
        Capture,
    }

    private double _panelShotWait;
    private int _panelShotWindowIndex;
    private int _panelShotRestoreMode;
    private int _panelShotRestoreTab; // the spellcasting window's open tab before its frame (M3 3.2 fix B)
    private PanelShotPhase _panelShotWindowPhase;
    private bool _panelShotsDone;
    private bool _panelShotTabOpen;
    private IReadOnlyList<PanelFrame>? _panelShotPages;
    private int _panelShotIndex;
    private int _panelShotFrames;
    private int _panelShotCaptured;
    private int _panelShotFailures;
    private PanelShotPhase _panelShotPhase;

    private void StepPanelShots(double delta, string directory)
    {
        if (!IsPlayerPresent() || _rig is not { } rig || rig.Controller.Placing || rig.Controller.Position.CellId == 0)
        {
            _panelShotWait = 0;
            return;
        }

        _panelShotWait += delta;
        if (_panelShotWait < _services.Options.Play.ScreenshotAfterSeconds
            || _ui?.Screens?.Flow.CurrentScreen is not GamePlayScreen { IsBuilt: true, PanelHost: { } host, PanelControllers: { } panels } shell)
        {
            return;
        }

        _panelShotPages ??= PanelFramesPlan.FramesFor(panels.RegisteredPages);
        if (_panelShotPages.Count == 0)
        {
            Log.Info(LogCategory, PanelFramesPlan.NoPanelText);
            FinishPanelShots(0);
            return;
        }

        if (_panelShotIndex >= _panelShotPages.Count)
        {
            if (StepWindowShots(shell, directory))
            {
                return; // the combat window's and the power bar's frames (M3 3.2 pa) follow the pages'
            }

            Log.Info(LogCategory, PanelFramesPlan.Verdict(_panelShotCaptured, _panelShotFailures));
            FinishPanelShots(_panelShotFailures == 0 ? 0 : 1);
            return;
        }

        var frame = _panelShotPages[_panelShotIndex];
        var page = frame.Page;
        switch (_panelShotPhase)
        {
            case PanelShotPhase.Open:
                if (host.CurrentPanel != page.PanelId)
                {
                    host.Toggle(page.PanelId); // what the page's toolbar button does
                }

                _panelShotTabOpen = host.CurrentPanel == page.PanelId && PanelFramesPlan.OpenTab(frame, host.ElementFor(page.PanelId)); // a click on the tab button

                _panelShotFrames = 0;
                _panelShotPhase = PanelShotPhase.Settle;
                break;
            case PanelShotPhase.Settle:
                if (++_panelShotFrames >= PanelShotSettleFrames)
                {
                    HideDeveloperHud();
                    _playShotHiddenFrames = 0;
                    _panelShotFrames = 0;
                    _panelShotPhase = PanelShotPhase.Capture;
                }

                break;
            default:
                if (++_panelShotFrames < PlayShotHiddenFrames)
                {
                    break;
                }

                var shown = _panelShotTabOpen && host.CurrentPanel == page.PanelId && host.ElementFor(page.PanelId)?.Visible == true && PanelFramesPlan.OpenTab(frame, host.ElementFor(page.PanelId));
                var path = System.IO.Path.Combine(directory, PanelFramesPlan.FileName(frame));
                var saved = shown && ScreenshotWriter.Save(GetViewport(), path, LogCategory, ("frame", "play-panel-shots"), ("panel", page.PanelId), ("developer_hud", "hidden"));
                RestoreDeveloperHud();
                if (saved)
                {
                    _panelShotCaptured++;
                    Log.Info(LogCategory, "panel shot saved", ("panel", page.PanelId), ("name", page.Name), ("tab", frame.TabName), ("path", path));
                }
                else
                {
                    _panelShotFailures++;
                    Log.Error(LogCategory, "panel shot failed", ("panel", page.PanelId), ("name", page.Name), ("tab", frame.TabName), ("shown", shown));
                }

                if (host.CurrentPanel == page.PanelId)
                {
                    host.Toggle(page.PanelId); // closes the page
                }

                _panelShotIndex++;
                _panelShotPhase = PanelShotPhase.Open;
                break;
        }
    }

    private void FinishPanelShots(int exitCode)
    {
        _panelShotsDone = true;
        if (_services.Options.Checks != GameCheckSuite.Play)
        {
            GetTree().Quit(exitCode); // as --play-screenshot: with --play-checks it only saves and the checks go on
        }
    }

    // M3 3.2 pa and pb: after the pages, the floaty windows that are not host pages (PanelFramesPlan.AllWindowFrames: c1-combat, c1-powerbar, c2-spellcasting). Each is shown by the UI's own mode notice (never a session action: a live run's character
    // keeps its mode), the power bar begun by hand at a placeholder level (nothing in M3 charges a bar), the shot taken, the notice put back. True while frames remain.
    private bool StepWindowShots(GamePlayScreen shell, string directory)
    {
        var frames = PanelFramesPlan.AllWindowFrames; // pa's two, then pb's c2-spellcasting
        if (_panelShotWindowIndex >= frames.Count)
        {
            return false;
        }

        var frame = frames[_panelShotWindowIndex];
        var combat = shell.Root?.GetChildRecursive(GameCombatPanel.WindowId) as GameCombatPanel;
        var bar = shell.Root?.GetChildRecursive(GamePowerBar.WindowId) as GamePowerBar;
        var spells = shell.Root?.GetChildRecursive(GameSpellcastingPanel.WindowId) as GameSpellcastingPanel;
        if (combat is null || bar is null || spells is null)
        {
            Log.Error(LogCategory, "window shot failed: the shell has no combat window, power bar or spellcasting window", ("window", frame.Name));
            _panelShotFailures++;
            _panelShotWindowIndex++;
            return true;
        }

        switch (_panelShotWindowPhase)
        {
            case PanelShotPhase.Open:
                _panelShotRestoreMode = combat.Mode;
                combat.SetCombatMode(frame.CombatMode);
                spells.SetCombatMode(frame.CombatMode); // M3 3.2 pb: the spellcasting window shows in Magic mode only (the UI's notice, never a session action)
                if (frame.WindowId == GameSpellcastingPanel.WindowId)
                {
                    // M3 3.2 fix B (3.2-R7): the first tab whose bar holds spells (live, BSD Mage's sit on bars 1 and 2), opened as a tab click; put back after the shot.
                    _panelShotRestoreTab = spells.OpenTab;
                    var choice = PanelFramesPlan.OpenSpellcastingFrameTab(spells);
                    Log.Info(LogCategory, "spellcasting shot tab", ("window", frame.Name), ("tab", choice.Tab), ("holds_spells", choice.HoldsSpells), ("opened", choice.Opened));
                }

                if (frame.DrivePowerBar)
                {
                    bar.Begin(PowerBarMode.Advanced, frame.CombatMode);
                    bar.SetLevel(PowerBarMode.Advanced, WindowFrame.PowerBarLevel);
                }

                _panelShotFrames = 0;
                _panelShotWindowPhase = PanelShotPhase.Settle;
                break;
            case PanelShotPhase.Settle:
                if (++_panelShotFrames >= PanelShotSettleFrames)
                {
                    HideDeveloperHud();
                    _playShotHiddenFrames = 0;
                    _panelShotFrames = 0;
                    _panelShotWindowPhase = PanelShotPhase.Capture;
                }

                break;
            default:
                if (++_panelShotFrames < PlayShotHiddenFrames)
                {
                    break;
                }

                var shown = shell.Root?.GetChildRecursive(frame.WindowId)?.IsShown == true;
                var path = System.IO.Path.Combine(directory, PanelFramesPlan.FileName(frame));
                var saved = shown && ScreenshotWriter.Save(GetViewport(), path, LogCategory, ("frame", "play-panel-shots"), ("window", frame.Name), ("developer_hud", "hidden"));
                RestoreDeveloperHud();
                if (saved)
                {
                    _panelShotCaptured++;
                    Log.Info(LogCategory, "panel shot saved", ("window", frame.Name), ("path", path));
                }
                else
                {
                    _panelShotFailures++;
                    Log.Error(LogCategory, "panel shot failed", ("window", frame.Name), ("shown", shown));
                }

                if (frame.DrivePowerBar)
                {
                    bar.Finish(PowerBarMode.Advanced);
                }

                if (frame.WindowId == GameSpellcastingPanel.WindowId)
                {
                    spells.OpenTabByClick(_panelShotRestoreTab);
                }

                combat.SetCombatMode(_panelShotRestoreMode);
                spells.SetCombatMode(_panelShotRestoreMode);
                _panelShotWindowIndex++;
                _panelShotWindowPhase = PanelShotPhase.Open;
                break;
        }

        return true;
    }
}
