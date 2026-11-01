using System.Diagnostics;
using Godot;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session;
using Lifestone.Game.Core;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// The worn items' examination frames (M5 item 7, D142, <c>--examine-worn-frames=DIR</c>): the live proof that the item window draws spell-changed values in the raised and lowered colours. Once the world is settled (the gate of the
/// other review frames, <see cref="GamePaperDoll.WorldReviewWait"/>), the wielded caster and then the worn robe (<see cref="ExamineWornTargets.Pick"/>) are examined through the examination window's own path
/// (<see cref="GameExaminePanel.Open"/>, as the toolbar's Examine); when the answer has filled the window with a line in the raised or lowered colour (<see cref="ExamineWornTargets.ShowsEnchantment"/>), or the tries ran out, a frame of the
/// whole screen and one of the examination window alone are saved on the same frame and the window is closed. A page with no coloured line yet is asked again every <see cref="RetrySeconds"/> (the start's buff pass may still be
/// casting), at most <see cref="MaxTries"/> times an item. Once a run. Plain object, not a node: play mode calls <see cref="Step"/> once a frame. One INFO line a frame and a try.
/// </summary>
/// <remarks>Nothing is held: each try is one Identify (ACE answers in milliseconds) and a window the autopilot never reads.</remarks>
public sealed class ExamineWornFrames
{
    /// <summary>Frames the world must stay settled before the first try (the arrival settles, the shell draws).</summary>
    public const int ReadySettleFrames = 60;

    /// <summary>Frames drawn after the answer filled the window before the frames are taken (the text laid out); the HUD is hidden two frames before.</summary>
    public const int SettleFrames = 4;

    /// <summary>Frames the answer may take before the try is counted as failed (about 4 s at 60 fps).</summary>
    public const int AnswerCapFrames = 240;

    /// <summary>Seconds between the tries of an item whose page shows no coloured line yet.</summary>
    public const double RetrySeconds = 15;

    /// <summary>The tries an item gets; the last one saves its frame whatever the page shows (enchanted=no).</summary>
    public const int MaxTries = 40;

    private const string LogCategory = "examine";

    private static readonly ExamineWornKind[] Kinds = [ExamineWornKind.Caster, ExamineWornKind.Robe];

    private readonly string? _directory;
    private readonly Action _hideHud;
    private readonly Action _restoreHud;
    private bool _hudHidden;
    private Phase _phase;
    private int _kind;
    private int _ready;
    private int _frames;
    private int _tries;
    private uint _guid;
    private string _name = string.Empty;
    private double _nextTryAt;

    /// <summary>Creates the frame saver; with no folder, or a refused one (logged once), it does nothing. <paramref name="hideHud"/> and <paramref name="restoreHud"/> hide and bring back the developer HUD.</summary>
    public ExamineWornFrames(string? directory, Action hideHud, Action restoreHud)
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
            Log.Info(LogCategory, "examine worn frames on: the wielded caster and the worn robe are examined once their pages show a spell-changed value", ("dir", _directory), ("retry_s", RetrySeconds), ("max_tries", MaxTries));
        }
    }

    private enum Phase
    {
        WaitWorld,
        WaitTry,
        WaitAnswer,
        Settle,
        Done,
    }

    /// <summary>True until both items are done (or the folder was refused).</summary>
    public bool Active => _directory is not null && _phase != Phase.Done;

    /// <summary>Once a frame, with the gameplay shell (null outside the world), the session, the UI's logical size (the window crop) and the world's state (<see cref="GamePaperDoll.WorldReviewWait"/>).</summary>
    public void Step(Viewport viewport, GamePlayScreen? shell, GameSession? session, Vector2I uiSize, bool playerPlaced, bool tunnelVisible, bool playerMaterialised)
    {
        if (!Active)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        var world = GamePaperDoll.WorldReviewWait(shell, playerPlaced, tunnelVisible, playerMaterialised);
        var examine = shell?.PanelHost?.Examine;
        if (world is not null || examine is null || session is null)
        {
            if (_phase is Phase.WaitAnswer or Phase.Settle)
            {
                Log.Info(LogCategory, "examine worn frames wait: the world left the review state, the try is asked again once it settles", ("kind", KindName), ("for", world ?? "the examination window"));
                CloseOurs(examine);
                _nextTryAt = now;
                ShowHud();
            }

            _phase = Phase.WaitWorld; // a portal or a login: the world settles again first
            _ready = 0;
            return;
        }

        switch (_phase)
        {
            case Phase.WaitWorld:
                if (++_ready >= ReadySettleFrames)
                {
                    _phase = Phase.WaitTry; // the next try when it is due (the first at once)
                }

                return;
            case Phase.WaitTry:
                if (now < _nextTryAt)
                {
                    return;
                }

                Ask(examine, session, now);
                return;
            case Phase.WaitAnswer:
                if (examine.ShownGuid != _guid || !examine.IsOpen)
                {
                    Retry(now, "the examination window shows something else");
                    return;
                }

                if (examine.Pending)
                {
                    if (++_frames > AnswerCapFrames)
                    {
                        CloseOurs(examine);
                        Retry(now, "no answer in time");
                    }

                    return;
                }

                if (!ExamineWornTargets.ShowsEnchantment(examine.LineColors) && _tries < MaxTries)
                {
                    CloseOurs(examine);
                    Retry(now, "the page shows no spell-changed line yet");
                    return;
                }

                _phase = Phase.Settle;
                _frames = SettleFrames;
                return;
            case Phase.Settle:
                if (examine.ShownGuid != _guid || !examine.IsOpen)
                {
                    Retry(now, "the examination window shows something else");
                    return;
                }

                if (_frames == 2 && !_hudHidden)
                {
                    _hideHud();
                    _hudHidden = true; // two frames before the read-back, so the frame is drawn without it
                }

                if (--_frames > 0)
                {
                    return;
                }

                var file = Kinds[_kind] == ExamineWornKind.Caster ? "examine-worn-caster" : "examine-worn-robe";
                Save(viewport, file + ".png", "screen", examine, null);
                Save(viewport, file + "-window.png", "window", examine, ExamineReviewFrames.CropOf(examine.Window, viewport, uiSize));
                examine.Close();
                ShowHud();
                Next(now);
                return;
        }
    }

    private string KindName => _kind < Kinds.Length ? Kinds[_kind].ToString().ToLowerInvariant() : "none";

    private void Ask(GameExaminePanel examine, GameSession session, double now)
    {
        var target = ExamineWornTargets.Pick(session.State.Objects.All, session.State.Self.Guid, Kinds[_kind]);
        if (target is null)
        {
            Log.Warn(LogCategory, "examine worn frames: the player wears no such item, its frame is skipped", ("kind", KindName));
            Next(now);
            return;
        }

        _guid = target.Guid;
        _name = target.Name;
        _tries++;
        _frames = 0;
        Log.Info(LogCategory, "examine worn frames: examining", ("kind", KindName), ("item", $"0x{_guid:X8}"), ("name", _name), ("try", _tries), ("max_tries", MaxTries));
        if (!examine.Open(_guid))
        {
            Retry(now, "the examination window did not open");
            return;
        }

        _phase = Phase.WaitAnswer;
    }

    private void Retry(double now, string why)
    {
        Log.Info(LogCategory, "examine worn frames: asked again later", ("kind", KindName), ("item", $"0x{_guid:X8}"), ("try", _tries), ("why", why), ("again_s", RetrySeconds));
        _phase = Phase.WaitTry;
        _nextTryAt = now + RetrySeconds;
        ShowHud();
    }

    private void Next(double now)
    {
        _kind++;
        _tries = 0;
        _guid = 0;
        _phase = _kind < Kinds.Length ? Phase.WaitTry : Phase.Done;
        _nextTryAt = now;
    }

    private void CloseOurs(GameExaminePanel? examine)
    {
        if (examine is not null && _guid != 0 && examine.ShownGuid == _guid)
        {
            examine.Close(); // the window this capture opened
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

    private void Save(Viewport viewport, string name, string view, GameExaminePanel examine, Rect2I? crop)
    {
        var path = System.IO.Path.Combine(_directory!, name);
        var size = crop is { } c ? $"{c.Size.X}x{c.Size.Y}" : $"{viewport.GetVisibleRect().Size.X}x{viewport.GetVisibleRect().Size.Y}";
        var raised = new List<string>();
        var lowered = new List<string>();
        for (var i = 0; i < examine.Lines.Count && i < examine.LineColors.Count; i++)
        {
            if (examine.LineColors[i] == ExamineColor.Raised)
            {
                raised.Add(examine.Lines[i]);
            }
            else if (examine.LineColors[i] == ExamineColor.Lowered)
            {
                lowered.Add(examine.Lines[i]);
            }
        }

        var enchanted = raised.Count + lowered.Count > 0 ? "yes" : "no";
        var kind = KindName;
        var title = examine.Title;
        var guid = _guid;
        var tries = _tries;
        var raisedText = string.Join(" | ", raised);
        var loweredText = string.Join(" | ", lowered);
        var readbackMs = ScreenshotWriter.SaveInBackgroundQuietly(
            viewport,
            path,
            LogCategory,
            ok =>
            {
                if (ok)
                {
                    Log.Info(LogCategory, "examine worn frame saved", ("kind", kind), ("view", view), ("item", $"0x{guid:X8}"), ("name", title), ("enchanted", enchanted), ("tries", tries), ("raised", raisedText), ("lowered", loweredText), ("size", size), ("path", name));
                }
            },
            crop);
        if (readbackMs < 0)
        {
            Log.Warn(LogCategory, "examine worn frame skipped: the window could not be read back", ("kind", kind), ("view", view));
        }
    }
}
