using Godot;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Game.Core;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// The capture's live proof frames (M5 item 7, D138, with <c>--examine-capture-creatures</c>): once each per run, an 800x600 frame of the examination window as it shows a creature's full page from a successful roll
/// (<c>examine-capture-creature.png</c>), a failed roll (<c>examine-capture-failed-roll.png</c>) and an item whose inspection holds an inscription, gear ratings or a set line (<c>examine-capture-item-extras.png</c>), as
/// <see cref="GameExaminePanel.CaptureKind"/> tells them; one INFO line per frame with the page's lines (<see cref="GameExaminePanel.CaptureText"/>). <see cref="LogNotSeen"/> names, at the end, a kind no answer showed.
/// </summary>
/// <remarks>
/// <para><b>Gated as the other review frames</b> (<see cref="GamePaperDoll.WorldReviewWait"/>: placed, no tunnel, materialised, the shell painted, the spew box empty), then <see cref="SettleFrames"/> frames with the same object shown;
/// the HUD is hidden two frames before the read-back. A change of the window during the settle starts over.</para>
/// <para><b>Off the fight's path.</b> The creature frames only watch what the capture (<see cref="ExamineCreatureCapture"/>) opened. While no item frame is saved and nothing waits for a frame, the first magic weapon, armour piece,
/// jewel or caster of an open container window not yet tried is inspected through the list's right-click path (<see cref="Lifestone.Client.RetailUi.Widgets.UiItemList.Examine"/>), one a time, so a looted item with a set, ratings or an
/// inscription can turn up: a read and one Identify, nothing held. The read-back stalls one frame.</para>
/// </remarks>
public sealed class ExamineCaptureFrames
{
    /// <summary>Frames the window must show the same object, answered, before its frame is taken.</summary>
    public const int SettleFrames = 6;

    private const string LogCategory = "examine";

    private readonly string? _directory;
    private readonly Action _hideHud;
    private readonly Action _restoreHud;
    private readonly HashSet<string> _saved = [];
    private readonly HashSet<uint> _tried = [];
    private string? _kind;
    private uint _guid;
    private int _frames;
    private bool _hudHidden;
    private uint _askedItem;

    /// <summary>Creates the frame saver over <paramref name="directory"/> (refused folders are logged once and save nothing).</summary>
    public ExamineCaptureFrames(string directory, Action hideHud, Action restoreHud)
    {
        _hideHud = hideHud;
        _restoreHud = restoreHud;
        _directory = ScreenshotWriter.Resolve(directory, LogCategory);
        if (_directory is not null)
        {
            Log.Info(LogCategory, "examine capture frames on: a creature's full page, a failed roll and an item with an inscription, ratings or a set are each saved once", ("dir", _directory));
        }
    }

    /// <summary>True once the frame of <paramref name="kind"/> (<see cref="GameExaminePanel.CaptureKinds"/>) was saved.</summary>
    public bool Saved(string kind) => _saved.Contains(kind);

    /// <summary>True until all three frames are saved (or the folder was refused).</summary>
    public bool Active => _directory is not null && _saved.Count < GameExaminePanel.CaptureKinds.Count;

    /// <summary>Once a frame, with the gameplay shell and the world's state (<see cref="GamePaperDoll.WorldReviewWait"/>).</summary>
    public void Step(Viewport viewport, GamePlayScreen? shell, bool playerPlaced, bool tunnelVisible, bool playerMaterialised)
    {
        if (!Active || shell?.PanelHost?.Examine is not { } examine)
        {
            return;
        }

        var world = GamePaperDoll.WorldReviewWait(shell, playerPlaced, tunnelVisible, playerMaterialised);
        var kind = world is null ? examine.CaptureKind() : null;
        if (kind is null || _saved.Contains(kind))
        {
            Reset();
            if (world is null)
            {
                AskCorpseItem(shell, playerPlaced, tunnelVisible, playerMaterialised, examine);
            }

            return;
        }

        if (kind != _kind || examine.ShownGuid != _guid)
        {
            Reset();
            _kind = kind;
            _guid = examine.ShownGuid;
            _frames = SettleFrames;
            return;
        }

        if (_frames == 2 && !_hudHidden)
        {
            _hideHud();
            _hudHidden = true;
        }

        if (--_frames > 0)
        {
            return;
        }

        Save(viewport, kind, examine);
        _saved.Add(kind);
        Reset();
    }

    /// <summary>At the end of the run: one line for each kind no answer showed ("not seen").</summary>
    public void LogNotSeen()
    {
        if (_directory is null)
        {
            return;
        }

        foreach (var kind in GameExaminePanel.CaptureKinds)
        {
            if (!_saved.Contains(kind))
            {
                Log.Info(LogCategory, "examine capture frame not seen", ("kind", kind));
            }
        }
    }

    private void AskCorpseItem(GamePlayScreen shell, bool playerPlaced, bool tunnelVisible, bool playerMaterialised, GameExaminePanel examine)
    {
        if (_saved.Contains(GameExaminePanel.CaptureItemExtras) || (examine.IsOpen && examine.Pending) || (examine.IsOpen && examine.Kind == ExamineKind.Creature && examine.CaptureKind() is { } waiting && !_saved.Contains(waiting)))
        {
            return; // a creature page still waits for its own frame, or an answer is on its way
        }

        if (_askedItem != 0 && examine.ShownGuid == _askedItem && examine.Pending)
        {
            return;
        }

        if (GameContainerPanel.ExamineReviewFrameWait(shell, playerPlaced, tunnelVisible, playerMaterialised, _tried, out var window, out var cell) is not null || window?.ItemRow is not { } row || cell is null)
        {
            return;
        }

        _tried.Add(cell.ItemId);
        _askedItem = cell.ItemId;
        row.Examine(cell);
        Log.Info(LogCategory, "examine capture: inspecting a magic item in the container window for the item frame", ("item", $"0x{cell.ItemId:X8}"), ("container", window.Title));
    }

    private void Reset()
    {
        _kind = null;
        _guid = 0;
        _frames = 0;
        if (_hudHidden)
        {
            _hudHidden = false;
            _restoreHud();
        }
    }

    private void Save(Viewport viewport, string kind, GameExaminePanel examine)
    {
        var name = "examine-capture-" + kind + ".png";
        var path = System.IO.Path.Combine(_directory!, name);
        var text = examine.CaptureText();
        var guid = examine.ShownGuid;
        var size = viewport.GetVisibleRect().Size;
        var readbackMs = ScreenshotWriter.SaveInBackgroundQuietly(viewport, path, LogCategory, ok =>
        {
            if (ok)
            {
                Log.Info(LogCategory, "examine capture frame saved", ("kind", kind), ("guid", $"0x{guid:X8}"), ("size", $"{size.X}x{size.Y}"), ("path", name), ("page", text));
            }
        });
        if (readbackMs < 0)
        {
            Log.Warn(LogCategory, "examine capture frame skipped: the window could not be read back", ("kind", kind));
        }
    }
}
