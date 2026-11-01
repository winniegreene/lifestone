using Godot;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Game.Core;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// The examination's live review frames (M5 item 7, S6, D136, <c>--examine-review-frames=DIR</c>): the first time a container window (the autopilot's corpse) shows a magic weapon, armour piece, jewel or caster, the item is
/// examined from the window's item list through the right click's own path (<see cref="UiItemList.Examine"/>), and once the server's answer has filled the examination window with the spells, the activation line, the mana and the
/// workmanship (<see cref="GameExaminePanel.MissingReviewLines"/>), one frame of the whole screen (<c>examine-corpse.png</c>: the corpse window with the item selected and its examination open) and one of the examination window
/// alone (<c>examine-window.png</c>) are saved on the same frame; the examination window is then closed. An answer that lacks a line, or an item that leaves the window before its frame, is passed over and the next one is
/// tried. Once a run. Plain object, not a node: play mode calls <see cref="Step"/> once a frame. One INFO line per frame and per item tried.
/// </summary>
/// <remarks>
/// <para><b>The loot step is never held.</b> The capture only reads the windows and sends one Identify (ACE answers in a few milliseconds); the autopilot's pick-ups go on. From the item's first frame in the drawn window to the
/// saved frames is about <see cref="StableFrames"/> + the answer + <see cref="SettleFrames"/> frames (a fifth of a second at 60 fps), well inside the 1.35 s a pick-up takes to move the item out of the corpse; at a low frame rate
/// the item may leave first, and the next one is tried.</para>
/// </remarks>
public sealed class ExamineReviewFrames
{
    /// <summary>Frames the candidate must stay the same in the drawn window before it is examined (the window laid out).</summary>
    public const int StableFrames = 3;

    /// <summary>Frames drawn after the answer filled the window before the frames are taken (the text laid out); the HUD is hidden two frames before.</summary>
    public const int SettleFrames = 4;

    /// <summary>Frames the answer may take before the item is passed over (about 4 s at 60 fps).</summary>
    public const int AnswerCapFrames = 240;

    private const string LogCategory = "examine";

    private readonly string? _directory;
    private readonly Action _hideHud;
    private readonly Action _restoreHud;
    private readonly HashSet<uint> _tried = [];
    private bool _hudHidden;
    private Phase _phase;
    private uint _candidate;
    private int _stable;
    private uint _guid;
    private int _frames;

    /// <summary>Creates the frame saver; with no folder, or a refused one (logged once), it does nothing. <paramref name="hideHud"/> and <paramref name="restoreHud"/> hide and bring back the developer HUD.</summary>
    public ExamineReviewFrames(string? directory, Action hideHud, Action restoreHud)
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
            Log.Info(LogCategory, "examine review frames on: the first magic item a container window shows is examined from the list and its frames saved", ("dir", _directory));
        }
    }

    private enum Phase
    {
        WaitForItem,
        WaitForAnswer,
        Settle,
        Done,
    }

    /// <summary>True until both frames are taken (or the folder was refused).</summary>
    public bool Active => _directory is not null && _phase != Phase.Done;

    /// <summary>
    /// Once a frame, with the gameplay shell (null outside the world), the UI's logical size (the crop maps its pixels to the viewport's), whether the player is placed, whether the portal tunnel shows and whether the player has
    /// materialised (<see cref="GameContainerPanel.ExamineReviewFrameWait"/>).
    /// </summary>
    public void Step(Viewport viewport, GamePlayScreen? shell, Vector2I uiSize, bool playerPlaced, bool tunnelVisible, bool playerMaterialised)
    {
        if (!Active)
        {
            return;
        }

        var wait = GameContainerPanel.ExamineReviewFrameWait(shell, playerPlaced, tunnelVisible, playerMaterialised, _tried, out var window, out var cell);
        if (_phase == Phase.WaitForItem)
        {
            if (wait is not null || window is null || cell is null)
            {
                _candidate = 0;
                _stable = 0;
                return;
            }

            if (cell.ItemId != _candidate)
            {
                _candidate = cell.ItemId;
                _stable = 0;
            }

            if (++_stable < StableFrames)
            {
                return;
            }

            _guid = cell.ItemId;
            _frames = 0;
            Log.Info(LogCategory, "examine review frames: examining from the container window's list", ("item", $"0x{_guid:X8}"), ("container", window.Title));
            if (!window.ItemRow!.Examine(cell))
            {
                PassOver(shell, "the list did not examine it");
                return;
            }

            _phase = Phase.WaitForAnswer;
            return;
        }

        // The item must still be in the window (the autopilot may have taken it) and its examination still the one shown.
        var examine = shell?.PanelHost?.Examine;
        var stillListed = shell?.Root?.GetChildRecursive(GameContainerPanel.WindowId) is GameContainerPanel shown && shown.IsShown && shown.ItemRow?.FindItem(_guid) is { IsShown: true };
        var world = GamePaperDoll.WorldReviewWait(shell, playerPlaced, tunnelVisible, playerMaterialised);
        if (world is not null || !stillListed || examine is null || examine.ShownGuid != _guid || !examine.IsOpen)
        {
            PassOver(shell, world ?? (!stillListed ? "the item left the container window" : "the examination window shows something else"));
            return;
        }

        switch (_phase)
        {
            case Phase.WaitForAnswer:
                if (examine.Pending)
                {
                    if (++_frames > AnswerCapFrames)
                    {
                        PassOver(shell, "no answer in time");
                    }

                    return;
                }

                if (GameExaminePanel.MissingReviewLines(examine.Lines) is { } missing)
                {
                    PassOver(shell, "the answer lacks " + missing);
                    return;
                }

                _phase = Phase.Settle;
                _frames = SettleFrames;
                return;
            case Phase.Settle:
                if (_frames == 2 && !_hudHidden)
                {
                    _hideHud();
                    _hudHidden = true; // two frames before the read-back, so the frame is drawn without it
                }

                if (--_frames > 0)
                {
                    return;
                }

                var title = (shell!.Root!.GetChildRecursive(GameContainerPanel.WindowId) as GameContainerPanel)?.Title ?? string.Empty;
                Save(viewport, "examine-corpse.png", "corpse", examine, title, null);
                Save(viewport, "examine-window.png", "window", examine, title, CropOf(examine.Window, viewport, uiSize));
                examine.Close();
                _phase = Phase.Done;
                ShowHud();
                return;
        }
    }

    private void PassOver(GamePlayScreen? shell, string why)
    {
        Log.Info(LogCategory, "examine review frames: item passed over, the next magic item is tried", ("item", $"0x{_guid:X8}"), ("why", why));
        _tried.Add(_guid);
        if (shell?.PanelHost?.Examine is { } examine && examine.ShownGuid == _guid)
        {
            examine.Close(); // the window this capture opened
        }

        _phase = Phase.WaitForItem;
        _candidate = 0;
        _stable = 0;
        ShowHud();
    }

    private void ShowHud()
    {
        if (_hudHidden)
        {
            _hudHidden = false;
            _restoreHud();
        }
    }

    internal static Rect2I? CropOf(Lifestone.Client.RetailUi.Core.UiElement window, Viewport viewport, Vector2I uiSize)
    {
        if (uiSize.X <= 0 || uiSize.Y <= 0)
        {
            return null;
        }

        var size = viewport.GetVisibleRect().Size;
        var sx = size.X / uiSize.X;
        var sy = size.Y / uiSize.Y;
        var box = window.ScreenBox;
        var x0 = (int)Mathf.Floor(box.X0 * sx);
        var y0 = (int)Mathf.Floor(box.Y0 * sy);
        return new Rect2I(x0, y0, (int)Mathf.Ceil(box.X1 * sx) - x0, (int)Mathf.Ceil(box.Y1 * sy) - y0);
    }

    private void Save(Viewport viewport, string name, string view, GameExaminePanel examine, string container, Rect2I? crop)
    {
        var path = System.IO.Path.Combine(_directory!, name);
        var size = crop is { } c ? $"{c.Size.X}x{c.Size.Y}" : $"{viewport.GetVisibleRect().Size.X}x{viewport.GetVisibleRect().Size.Y}";
        var lines = examine.Lines;
        var spells = lines.FirstOrDefault(static l => l.StartsWith("Spells: ", StringComparison.Ordinal)) ?? "none";
        var activation = lines.FirstOrDefault(static l => l.StartsWith("Activation requires ", StringComparison.Ordinal)) ?? "none";
        var mana = lines.FirstOrDefault(static l => l.StartsWith("Mana: ", StringComparison.Ordinal)) ?? "none";
        var workmanship = lines.FirstOrDefault(static l => l.StartsWith("Workmanship: ", StringComparison.Ordinal)) ?? "none";
        var title = examine.Title;
        var guid = _guid;
        var readbackMs = ScreenshotWriter.SaveInBackgroundQuietly(
            viewport,
            path,
            LogCategory,
            ok =>
            {
                if (ok)
                {
                    Log.Info(LogCategory, "examine review frame saved", ("view", view), ("item", $"0x{guid:X8}"), ("name", title), ("container", container), ("spells", spells), ("activation", activation), ("mana", mana), ("workmanship", workmanship), ("size", size), ("path", name));
                }
            },
            crop);
        if (readbackMs < 0)
        {
            Log.Warn(LogCategory, "examine review frame skipped: the window could not be read back", ("view", view));
        }
    }
}
