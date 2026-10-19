using System.Diagnostics;
using System.Globalization;
using System.Text;
using Godot;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Interiors;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.Player;

namespace Lifestone.Game.World.Interiors;

/// <summary>
/// Automated measurement (<c>--interior-tour=DIR</c>): teleports to every curated interior (<see cref="InteriorLocations"/>)
/// in turn, waits until streaming is idle, every expected cell is attached and the main-thread queue is empty, saves a
/// screenshot to <c>DIR/interiors-tour-KEY.png</c>, then measures
/// frame times, draw calls and the portal walk while the camera stands still and while it turns a full circle. Logs one
/// line per location and quits (docs/interiors.md §7).
/// </summary>
public partial class InteriorTourRunner : Node
{
    private const int SettleFrames = 30;

    /// <summary>Also settle this long, so the HUDs (refreshed every 0.1 s and 0.25 s) show the finished state in the screenshot.</summary>
    private const double SettleSeconds = 0.6;
    private const int StillFrames = 240;
    private const int TurnFrames = 240;
    private const double IdleTimeoutSeconds = 120.0;

    private readonly List<string> _report = new();
    private InteriorController _controller = null!;
    private FlyCamera _camera = null!;
    private WorldRoot _world = null!;
    private MainThreadQueue _mainThread = null!;
    private string _directory = ".";
    private int _index = -1;
    private Phase _phase = Phase.Next;
    private int _frames;
    private double _waited;
    private long _lastTimestamp;
    private FrameTimeRecorder _frameTimes = new();
    private double _drawCallsSum;
    private double _primitivesSum;
    private long _maxDrawCalls;
    private int _maxCells;
    private double _startHeading;
    private readonly DynamicLightList _lights = new() { ViewerLightEnabled = true };
    private readonly DynamicLightFrame _lightFrame = new();
    private readonly PipelineCompilationWatch _pipelines = new();
    private ulong _foregroundPipelines;

    private enum Phase
    {
        Next,
        WaitIdle,
        Settle,
        Still,
        Turn,
    }

    public void Initialize(InteriorController controller, FlyCamera camera, WorldRoot world, MainThreadQueue mainThread, string directory)
    {
        _controller = controller;
        _camera = camera;
        _world = world;
        _mainThread = mainThread;
        _directory = directory;
        _camera.InputEnabled = false;
        _lights.ViewerLightEnabled = LightOptions.Parse(OS.GetCmdlineUserArgs()).ViewerLight; // --viewer-light=on|off: the glow as play shows it, so the tour measures what play draws
        _controller.DynamicLights.Source = _lightFrame;
        ProcessPriority = -10; // before the camera, so a turn applies this frame
    }

    public override void _Process(double delta)
    {
        var now = Stopwatch.GetTimestamp();
        var frameMs = _lastTimestamp == 0 ? 0.0 : (now - _lastTimestamp) * 1000.0 / Stopwatch.Frequency;
        _lastTimestamp = now;
        var all = InteriorLocations.All;
        FillViewerLight();
        _pipelines.Step();
        if (_pipelines.Last.Foreground > 0)
        {
            _foregroundPipelines += _pipelines.Last.Foreground;
            Log.Info("interiors", "tour pipeline compiled in a measured phase", ("location", all[Math.Max(0, _index)].Key), ("phase", _phase), ("frame_ms", frameMs), ("pipelines_surface", _pipelines.Last.Surface), ("pipelines_mesh", _pipelines.Last.Mesh), ("pipelines_draw", _pipelines.Last.Draw), ("lights", _lightFrame.Count));
        }

        switch (_phase)
        {
            case Phase.Next:
                if (++_index >= all.Count)
                {
                    Finish();
                    return;
                }

                _controller.PlaceCamera(all[_index].Location, teleport: true);
                _startHeading = _camera.Heading;
                _waited = 0;
                _phase = Phase.WaitIdle;
                StartLoadMeasure();
                break;
            case Phase.WaitIdle:
                _waited += delta;
                SampleLoad(frameMs);

                // Complete content: the landblocks are attached, every cell they mesh is built, nothing is queued.
                var complete = _world.Streamer.IsIdle && _controller.PendingCells == 0 && _mainThread.Pending == 0;
                if ((complete && _waited > 0.2) || _waited > IdleTimeoutSeconds)
                {
                    if (!complete)
                    {
                        Log.Warn("interiors", "tour: content not complete after the timeout", ("pending_cells", _controller.PendingCells), ("queue", _mainThread.Pending));
                    }

                    RecordLoad(all[_index].Key);
                    _frames = 0;
                    _waited = 0;
                    _phase = Phase.Settle;
                }

                break;
            case Phase.Settle:
                _waited += delta;
                if (++_frames >= SettleFrames && _waited >= SettleSeconds)
                {
                    var key = all[_index].Key;
                    SaveScreenshot(System.IO.Path.Combine(_directory, $"interiors-tour-{key}.png"));
                    StartMeasure(Phase.Still);
                }

                break;
            case Phase.Still:
                Sample(frameMs);
                if (++_frames >= StillFrames)
                {
                    Record("still");
                    StartMeasure(Phase.Turn);
                }

                break;
            case Phase.Turn:
                Sample(frameMs);
                _camera.Heading = _startHeading + (360.0 * _frames / TurnFrames);
                if (++_frames >= TurnFrames)
                {
                    Record("turn");
                    _camera.Heading = _startHeading;
                    _phase = Phase.Next;
                }

                break;
        }
    }

    // --- wave-3 performance pass: the main-thread cost of each load (teleport to complete content) ---
    private TimeSpan _loadPause;
    private double _loadMaxFrameMs;
    private double _loadMaxDrainMs;
    private double _loadMaxDrainWorkMs;
    private double _loadGcPauseMs;
    private int _loadFrames;

    private void StartLoadMeasure()
    {
        _loadPause = GC.GetTotalPauseDuration();
        _loadMaxFrameMs = _loadMaxDrainMs = _loadMaxDrainWorkMs = _loadGcPauseMs = 0;
        _loadFrames = 0;
        _mainThread.Scheduler.ResetPeaks();
    }

    /// <summary>One frame of a load: its drain, and the drain without the GC pause that fell in the frame.</summary>
    private void SampleLoad(double frameMs)
    {
        var pause = GC.GetTotalPauseDuration();
        var pauseMs = (pause - _loadPause).TotalMilliseconds;
        _loadPause = pause;
        var drain = GameServices.Instance.LastDrainMs;
        _loadFrames++;
        _loadGcPauseMs += pauseMs;
        _loadMaxFrameMs = Math.Max(_loadMaxFrameMs, frameMs);
        _loadMaxDrainMs = Math.Max(_loadMaxDrainMs, drain);
        _loadMaxDrainWorkMs = Math.Max(_loadMaxDrainWorkMs, drain - Math.Min(drain, pauseMs));
    }

    private void RecordLoad(string key) =>
        Log.Info(
            "interiors",
            "tour load",
            ("location", key),
            ("seconds", _waited),
            ("frames", _loadFrames),
            ("max_frame_ms", _loadMaxFrameMs),
            ("max_drain_ms", _loadMaxDrainMs),
            ("max_drain_work_ms", _loadMaxDrainWorkMs),
            ("slowest_item_ms", _mainThread.Scheduler.MaxItemMilliseconds),
            ("slowest_item_work_ms", _mainThread.Scheduler.MaxItemWorkMilliseconds), // without a GC pause inside it
            ("gc_pause_ms", _loadGcPauseMs));

    private void StartMeasure(Phase phase)
    {
        _phase = phase;
        _frames = 0;
        _frameTimes = new FrameTimeRecorder();
        _controller.ResetViewPeak();
        _drawCallsSum = 0;
        _primitivesSum = 0;
        _maxDrawCalls = 0;
        _maxCells = 0;
    }

    private void Sample(double frameMs)
    {
        if (_frames > 0)
        {
            _frameTimes.Add(frameMs);
        }

        var drawCalls = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        _drawCallsSum += drawCalls;
        _maxDrawCalls = Math.Max(_maxDrawCalls, drawCalls);
        _primitivesSum += RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
        _maxCells = Math.Max(_maxCells, _controller.View.VisibleCells.Count);
    }

    private void Record(string what)
    {
        var location = InteriorLocations.All[_index];
        var n = Math.Max(1, _frames);
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{location.Key} {what}: avg {_frameTimes.Average:0.00} ms ({1000.0 / Math.Max(0.001, _frameTimes.Average):0} fps) p99 {_frameTimes.Percentile(99):0.00} max {_frameTimes.Max:0.00} ms; " +
            $"draw calls avg {_drawCallsSum / n:0} max {_maxDrawCalls}; primitives avg {_primitivesSum / n:0}; cells drawn max {_maxCells} " +
            $"of {_controller.AttachedCellNodes} meshes ({_controller.AttachedCells} of {_controller.ExpectedCells} meshed, {_controller.LoadedCells} as data, slots peak {_controller.SlotsPeak}); view max {_controller.MaxViewMilliseconds:0.00} ms; own meshes {_controller.OwnMeshBytes / (1024.0 * 1024.0):0.0} MB; " +
            $"video memory {Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024.0 * 1024.0):0} MB");
        _report.Add(line);
        Log.Info("interiors", "tour", ("result", line));
    }

    /// <summary>The viewer light as play makes it (<c>AvatarSet.Advance</c>): the list's update at the camera's cell and position, then the frame the table reads.</summary>
    private void FillViewerLight()
    {
        var eye = _camera.WorldPosition;
        var cell = _controller.Tracker.CellId;
        var origin = Lifestone.Client.World.AcCoordinates.LandblockOrigin(Lifestone.Data.Terrain.LandblockCoord.FromId(cell));
        var local = new System.Numerics.Vector3((float)(eye.X - origin.X), (float)(eye.Y - origin.Y), (float)eye.Z);
        _lights.Update(new CellFrame(cell, new AcFrame(local, System.Numerics.Quaternion.Identity)), null);
        _lightFrame.Fill(_lights);
    }

    /// <summary>The shared screenshot rule (<see cref="Diagnostics.ScreenshotWriter"/>: repository-relative, never inside src/ or tests/).</summary>
    private void SaveScreenshot(string path) => Diagnostics.ScreenshotWriter.Save(GetViewport(), path, "interiors");

    private void Finish()
    {
        var sb = new StringBuilder();
        foreach (var line in _report)
        {
            sb.Append(line).Append('\n');
        }

        Log.Info("interiors", "tour done", ("locations", InteriorLocations.All.Count), ("foreground_pipelines_in_measured_phases", _foregroundPipelines), ("dyn_light_cells_peak", _controller.DynLightCellsPeak));
        Log.FlushFile();
        GetTree().Quit(0);
    }
}
