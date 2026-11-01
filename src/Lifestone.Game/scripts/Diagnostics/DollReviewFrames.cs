using Godot;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Game.Core;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// The paper doll's live review frames (M5 item 7, D129, <c>--doll-review-frames=DIR</c>): the first time the inventory page's paper doll shows in a run, a frame
/// of the window is saved with the doll on its figure view (<c>doll-figure.png</c>), the "Slots" checkbox is ticked through the doll's own handler
/// (<see cref="GamePaperDoll.ClickSlotsCheckbox"/>) and a second frame is saved with each armour piece in its slot (<c>doll-slots.png</c>); the checkbox is then
/// put back as it was; the developer HUD is hidden from shortly before the first frame until after the second (as the panel shots do). Once a run. Plain object, not a node: play mode calls <see cref="Step"/> once a frame. The read-back is on the frame that takes it, the PNG
/// is written on a worker (<see cref="ScreenshotWriter.SaveInBackgroundQuietly"/>). One INFO line per frame.
/// </summary>
public sealed class DollReviewFrames
{
    private const string LogCategory = "doll";

    /// <summary>Frames the gate must stay clear before the first frame is taken (the arrival settles, the page and its figure draw).</summary>
    public const int ReadySettleFrames = 60;

    // Frames the page draws after the view is set to the figure, before the frame (the figure dressed; PlayChecks.PanelB2 waits as long).
    private const int FigureSettleFrames = 30;
    private const int ViewSettleFrames = 4;

    private readonly string? _directory;
    private readonly Action _hideHud;
    private readonly Action _restoreHud;
    private bool _hudHidden;
    private Phase _phase;
    private int _ready;
    private int _wait;
    private bool _slotsBefore;

    /// <summary>Creates the frame saver; with no folder, or a refused one (logged once), it does nothing. <paramref name="hideHud"/> and <paramref name="restoreHud"/> hide and bring back the developer HUD.</summary>
    public DollReviewFrames(string? directory, Action hideHud, Action restoreHud)
    {
        _hideHud = hideHud;
        _restoreHud = restoreHud;
        if (directory is null)
        {
            return;
        }

        _directory = ScreenshotWriter.Resolve(directory, LogCategory);
        if (_directory is not null)
        {
            Log.Info(LogCategory, "doll review frames on: the first showing of the paper doll saves its figure and slots views", ("dir", _directory));
        }
    }

    private enum Phase
    {
        WaitForDoll,
        Figure,
        Slots,
        Done,
    }

    /// <summary>True until both frames are taken (or the folder was refused).</summary>
    public bool Active => _directory is not null && _phase != Phase.Done;

    /// <summary>
    /// Once a frame, with the gameplay shell (null outside the world), whether the player is placed in the world and whether the portal tunnel shows. Nothing is
    /// taken until <see cref="GamePaperDoll.ReviewFrameWait"/> is clear and has stayed clear for <see cref="ReadySettleFrames"/> frames; if it stops being clear before
    /// both frames are taken, the run waits again from the start (a showing in portal space waits for the arrival, a closed page for the next showing).
    /// </summary>
    public void Step(Viewport viewport, GamePlayScreen? shell, bool playerPlaced, bool tunnelVisible, bool playerMaterialised)
    {
        if (!Active)
        {
            return;
        }

        var wait = GamePaperDoll.ReviewFrameWait(shell, playerPlaced, tunnelVisible, playerMaterialised);
        if (wait is not null)
        {
            if (_phase != Phase.WaitForDoll || _ready > 0)
            {
                Log.Info(LogCategory, "doll review frames wait", ("for", wait), ("phase", _phase.ToString()));
                if (_phase != Phase.WaitForDoll && shell?.PanelControllers?.ControllersOf(GamePaperDoll.Panel).OfType<GamePaperDoll>().FirstOrDefault() is { } open && open.SlotsViewShown != _slotsBefore)
                {
                    open.ClickSlotsCheckbox(); // the checkbox as the player left it
                }

                _phase = Phase.WaitForDoll;
                _ready = 0;
                ShowHud();
            }

            return;
        }

        var doll = shell!.PanelControllers!.ControllersOf(GamePaperDoll.Panel).OfType<GamePaperDoll>().First();
        switch (_phase)
        {
            case Phase.WaitForDoll:
                if (++_ready < ReadySettleFrames)
                {
                    return; // the page drawn, the figure dressed and the world settled
                }

                _ready = 0;
                _slotsBefore = doll.SlotsViewShown;
                if (_slotsBefore)
                {
                    doll.ClickSlotsCheckbox(); // the figure view first
                }

                _phase = Phase.Figure;
                _wait = FigureSettleFrames;
                return;
            case Phase.Figure:
                if (_wait == 2 && !_hudHidden)
                {
                    _hideHud();
                    _hudHidden = true; // two frames before the read-back, so the frame is drawn without it
                }

                if (--_wait > 0)
                {
                    return;
                }

                Save(viewport, "doll-figure.png", "figure", doll);
                doll.ClickSlotsCheckbox();
                _phase = Phase.Slots;
                _wait = ViewSettleFrames;
                return;
            case Phase.Slots:
                if (--_wait > 0)
                {
                    return;
                }

                Save(viewport, "doll-slots.png", "slots", doll);
                if (doll.SlotsViewShown != _slotsBefore)
                {
                    doll.ClickSlotsCheckbox(); // the checkbox as the player left it
                }

                _phase = Phase.Done;
                ShowHud();
                return;
        }
    }

    private void ShowHud()
    {
        if (_hudHidden)
        {
            _hudHidden = false;
            _restoreHud();
        }
    }

    private void Save(Viewport viewport, string name, string view, GamePaperDoll doll)
    {
        var path = System.IO.Path.Combine(_directory!, name);
        var size = viewport.GetVisibleRect().Size;
        var shownView = doll.SlotsViewShown ? "slots" : "figure";
        var readbackMs = ScreenshotWriter.SaveInBackgroundQuietly(viewport, path, LogCategory, ok =>
        {
            if (ok)
            {
                Log.Info(LogCategory, "doll review frame saved", ("view", view), ("shown_view", shownView), ("figure_served", doll.FigureServed), ("size", $"{size.X}x{size.Y}"), ("path", name));
            }
        });
        if (readbackMs < 0)
        {
            Log.Warn(LogCategory, "doll review frame skipped: the window could not be read back", ("view", view));
        }
    }
}
