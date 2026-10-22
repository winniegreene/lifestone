using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Game.Sessions;

// M4 3.2 s4c: the examine panel's frame of --play-panel-shots (PanelFramesPlan.ExamineKitFrame). A Handy Healing Kit in the pack is examined through the entry the item list's right click uses (GameExaminePanel.Open: it sends
// IActionApi.Identify with the Ui source), the shot is taken once the appraisal has filled the window, and the window is closed again. No kit in the pack: PanelFramesPlan.NoKitText (not a failure). An answer that does not come
// within 15 s or a window that is not shown is a failure. No creature is examined (D23). True while the frame is in work.
public sealed partial class PlayMode
{
    private enum ExamineShotPhase
    {
        Find,
        Waiting,
        Capture,
        Done,
    }

    private ExamineShotPhase _examineShotPhase;
    private uint _examineShotGuid;
    private double _examineShotWait;
    private int _examineShotFrames;
    private int _examineShotFills;

    private bool StepExamineShot(GamePlayScreen shell, string directory, double delta)
    {
        if (_examineShotPhase == ExamineShotPhase.Done)
        {
            return false;
        }

        var session = _host.Session;
        var examine = shell.PanelHost?.Examine;
        if (session is null || examine is null)
        {
            Log.Error(LogCategory, "examine shot failed: the shell has no session or no examine panel", ("session", session is not null), ("panel", examine is not null));
            _panelShotFailures++;
            _examineShotPhase = ExamineShotPhase.Done;
            return false;
        }

        _examineShotWait += delta;
        switch (_examineShotPhase)
        {
            case ExamineShotPhase.Find:
            {
                _examineShotGuid = PanelFramesPlan.KitInPack(session.State);
                if (_examineShotGuid == 0)
                {
                    Log.Info(LogCategory, PanelFramesPlan.NoKitText);
                    _examineShotPhase = ExamineShotPhase.Done;
                    return false;
                }

                _examineShotFills = examine.Fills;
                Log.Info(LogCategory, "examine shot: examining the kit", ("kit", $"0x{_examineShotGuid:X8}"));
                examine.Open(_examineShotGuid);
                _examineShotWait = 0;
                _examineShotPhase = ExamineShotPhase.Waiting;
                return true;
            }

            case ExamineShotPhase.Waiting:
                if (examine.Fills > _examineShotFills && !examine.Pending && examine.IsOpen)
                {
                    HideDeveloperHud();
                    _playShotHiddenFrames = 0;
                    _examineShotFrames = 0;
                    _examineShotPhase = ExamineShotPhase.Capture;
                }
                else if (_examineShotWait > 15.0)
                {
                    Log.Error(LogCategory, "examine shot failed: the appraisal did not fill the window within 15 s", ("kit", $"0x{_examineShotGuid:X8}"), ("identify", examine.LastIdentify?.Outcome?.Kind.ToString() ?? "pending"));
                    _panelShotFailures++;
                    examine.Close();
                    _examineShotPhase = ExamineShotPhase.Done;
                    return false;
                }

                return true;
            default: // Capture
            {
                if (++_examineShotFrames < PlayShotHiddenFrames)
                {
                    return true;
                }

                var path = System.IO.Path.Combine(directory, PanelFramesPlan.ExamineKitFrame + ".png");
                var saved = examine.IsOpen && ScreenshotWriter.Save(GetViewport(), path, LogCategory, ("frame", "play-panel-shots"), ("window", PanelFramesPlan.ExamineKitFrame), ("kit", $"0x{_examineShotGuid:X8}"), ("title", examine.Title), ("lines", examine.Lines.Count), ("developer_hud", "hidden"));
                RestoreDeveloperHud();
                if (saved)
                {
                    _panelShotCaptured++;
                    Log.Info(LogCategory, "panel shot saved", ("window", PanelFramesPlan.ExamineKitFrame), ("title", examine.Title), ("lines", string.Join(" | ", examine.Lines)), ("path", path));
                }
                else
                {
                    _panelShotFailures++;
                    Log.Error(LogCategory, "panel shot failed", ("window", PanelFramesPlan.ExamineKitFrame), ("shown", examine.IsOpen));
                }

                examine.Close();
                _examineShotPhase = ExamineShotPhase.Done;
                return false;
            }
        }
    }
}
