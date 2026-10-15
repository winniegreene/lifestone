using System.Diagnostics;
using System.Globalization;
using Godot;
using Lifestone.Client.Navigation;
using Lifestone.Client.PortalSpace;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.Render;
using Lifestone.Game.UI;
using Lifestone.Game.World;

namespace Lifestone.Game.PortalSpace;

/// <summary>
/// <c>--portal-space-frames[=DIR]</c>: plays one scripted portal-space sequence, Holtburg to Arwic through the teleport panel's
/// own hook (<see cref="OfflinePortalTransition"/>), at a fixed step of 1/60 s and a fixed seed, saves four review frames of the
/// window and quits (docs/portal-space.md §11.7). The sequence is the same frame for frame on every machine: only the world's
/// loading takes real time, and state 3 waits for it.
/// </summary>
/// <remarks>
/// <para><b>The four frames</b> (<c>01-mid-expand</c>, <c>02-in-tunnel</c>, <c>03-mid-collapse</c>, <c>04-mid-arrival</c>) are taken
/// at a state and an elapsed time, never at a wall-clock time: the tunnel half way through its first expansion (state 2, 0.5 s),
/// the tunnel holding (state 4, 1.0 s), half way through the collapse that leaves it (state 5, 0.5 s) and half way through the
/// world's expansion on arrival (state 6, 0.5 s). Each is the window's last drawn frame, read at the start of the next frame,
/// so it shows exactly the frame the presenter applied. Files are written under <c>DIR</c> (default
/// <see cref="PortalSpaceOptions.DefaultFramesDirectory"/>, gitignored).</para>
/// <para><b>Verdict</b> (frozen, read by <c>tools\smoke.ps1</c>): <c>portal space frames done captured=4 failures=0 max_frame_ms=&lt;x&gt; slow_frames=&lt;n&gt;</c>;
/// the exit code is 0 only when four frames were taken and nothing failed. A failure is: a frame missed or blank, the sequence not
/// ending in time, the world or the lens not restored, a LoginComplete sent offline, the transition switched off, or a frame over
/// <see cref="SlowFrameMilliseconds"/> ms (m2-log-in DoD 15: no frame over 50 ms caused by the tunnel).</para>
/// <para><b>Frame times.</b> A frame is the time between two consecutive post-draw signals of the engine (the time between two presented
/// frames, whatever ran in it), <i>less the time this runner spent capturing</i> (the GPU readback and the PNG encode are the runner's own
/// cost, not the tunnel's). Each is split at that frame's pre-draw signal into <c>process_ms</c> (the frame's start, the scene tree's
/// signals, input and every node's <c>_Process</c>, the presenter's included) and <c>draw_ms</c> (the renderer, with the present).
/// Three frames are logged on their own lines: the <b>start frame</b> (the frame of the teleport hook's call: the presenter's first
/// step, the world hidden, the tunnel's first draw), the <b>jump frame</b> (the next one, in which
/// the hook moves the camera and the old world is torn down, already hidden: a bare jump costs about 40 ms on its own, which is
/// why the hook does not do it in the start frame, docs/portal-space.md §11.7) and the <b>first arrival frame</b> (the first frame of
/// state 6: the world drawn again through the zoom's frustum). Every other frame over the limit is a WARNING line with its state.
/// <c>max_frame_ms</c> is the longest of them all; a <c>frame times</c> line gives the mean and the longest per state.</para>
/// <para>Silent unless <c>--volume</c> is given (D8). The debug and tool overlays (the position HUD, the F3 overlay, the panels) are
/// hidden for the capture; the placeholder status line is part of the picture.</para>
/// </remarks>
public partial class PortalSpaceFramesRunner : Node
{
    /// <summary>Process priority: before the presenter (15), so the hook this runner calls is stepped by the presenter in the same frame.</summary>
    public const int Priority = 10;

    /// <summary>The fixed step of the sequence's clock, in seconds.</summary>
    public const double FixedStep = 1.0 / 60.0;

    /// <summary>The limit for a frame the tunnel may cost (m2-log-in DoD 15).</summary>
    public const double SlowFrameMilliseconds = 50.0;

    private const int SettleFrames = 30;
    private const double SettleTimeoutSeconds = 60.0;
    private const double RunTimeoutSeconds = 120.0;
    private const int BlankGridX = 64;
    private const int BlankGridY = 36;

    private static readonly (string File, string Name, TeleportAnimState State, double Elapsed)[] Shots =
    [
        ("01-mid-expand.png", "mid-expand", TeleportAnimState.Tunnel, 0.5),
        ("02-in-tunnel.png", "in the tunnel", TeleportAnimState.Holding, 1.0),
        ("03-mid-collapse.png", "mid-collapse", TeleportAnimState.LeavingTunnel, 0.5),
        ("04-mid-arrival.png", "mid-arrival", TeleportAnimState.Arriving, 0.5),
    ];

    private CaptureReadiness _readiness = null!;
    private Navigator _navigator = null!;
    private ITeleporter _teleporter = null!;
    private PortalSpacePresenter _presenter = null!;
    private WorldVisibility _visibility = null!;
    private string _directory = string.Empty;
    private TeleportDestination? _destination;
    private Phase _phase = Phase.Settle;
    private int _settled;
    private int _next;
    private int _captured;
    private int _failures;
    private double _phaseSeconds;
    private double _maxFrameMs;
    private int _slowFrames;
    private long _preDrawTicks;
    private long _postDrawTicks;
    private double _captureMs;
    private int _framesMeasured;
    private readonly Dictionary<TeleportAnimState, (int Count, double Sum, double Max)> _perState = [];
    private TeleportAnimState _lastMeasuredState = TeleportAnimState.Off;
    private bool _started;
    private bool _sawActive;
    private int _idleFrames;
    private bool _done;

    private enum Phase
    {
        Settle,
        Run,
        Drain,
    }

    /// <summary>Builds the runner.</summary>
    /// <param name="services">The shared services.</param>
    /// <param name="world">The streamed world (readiness).</param>
    /// <param name="navigator">Moves the camera to a teleport destination (the panel's own).</param>
    /// <param name="teleporter">The panel's hook, which wraps the jump in portal space.</param>
    /// <param name="presenter">The presenter, on a fixed clock.</param>
    /// <param name="directory">Where the frames go (absolute, or relative to the repository root).</param>
    public static PortalSpaceFramesRunner Create(GameServices services, WorldRoot world, Navigator navigator, ITeleporter teleporter, PortalSpacePresenter presenter, string directory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(teleporter);
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var runner = new PortalSpaceFramesRunner
        {
            Name = "PortalSpaceFramesRunner",
            ProcessPriority = Priority,
            _readiness = new CaptureReadiness(services, world),
            _navigator = navigator,
            _teleporter = teleporter,
            _presenter = presenter,
            _visibility = services.WorldVisibility,
            _directory = directory,
            _destination = navigator.Catalog.Find("Arwic"),
        };
        if (runner._destination is null)
        {
            runner.Fail("the teleport catalogue has no destination 'Arwic'");
        }

        return runner;
    }

    public override void _EnterTree()
    {
        RenderingServer.FramePreDraw += OnPreDraw;
        RenderingServer.FramePostDraw += OnPostDraw;
    }

    public override void _ExitTree()
    {
        RenderingServer.FramePreDraw -= OnPreDraw;
        RenderingServer.FramePostDraw -= OnPostDraw;
    }

    private void OnPreDraw() => _preDrawTicks = Stopwatch.GetTimestamp();

    /// <summary>
    /// The frame that has just been drawn ends here: the interval since the previous frame's post-draw is the time between two
    /// presented frames, whatever ran in it (the jump, which the transition runs at the start of its frame, included).
    /// </summary>
    private void OnPostDraw()
    {
        var post = Stopwatch.GetTimestamp();
        if (!_done && _phase != Phase.Settle && _postDrawTicks != 0 && _preDrawTicks >= _postDrawTicks)
        {
            Measure(_postDrawTicks, _preDrawTicks, post);
        }

        _postDrawTicks = post;
        _captureMs = 0.0;
    }

    public override void _Process(double delta)
    {
        if (_done)
        {
            return;
        }

        _phaseSeconds += delta;

        switch (_phase)
        {
            case Phase.Settle:
                Settle();
                break;
            case Phase.Run:
                Run();
                break;
            case Phase.Drain:
                Drain();
                break;
        }
    }

    /// <summary>
    /// Judges the frame just drawn: from the previous frame's post-draw (<paramref name="previousPost"/>) to this one's
    /// (<paramref name="post"/>), less this runner's own capture, split at this frame's pre-draw (<paramref name="pre"/>) into the
    /// process time (the frame's start, the scene tree's signals, input and every node's <c>_Process</c>) and the draw time. The frame
    /// state is the one the presenter applied in this frame.
    /// </summary>
    private void Measure(long previousPost, long pre, long post)
    {
        static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

        var frameMs = Ms(previousPost, post) - _captureMs;
        var processMs = Ms(previousPost, pre) - _captureMs;
        var drawMs = Ms(pre, post);
        var frame = _presenter.LastFrame;
        _maxFrameMs = Math.Max(_maxFrameMs, frameMs);

        var firstArrival = frame.State == TeleportAnimState.Arriving && _lastMeasuredState != TeleportAnimState.Arriving;
        var what = _framesMeasured switch
        {
            0 => "start frame",
            1 => "jump frame",
            _ => firstArrival ? "first arrival frame" : "frame",
        };
        _framesMeasured++;
        _lastMeasuredState = frame.State;
        var (count, sum, max) = _perState.GetValueOrDefault(frame.State);
        _perState[frame.State] = (count + 1, sum + frameMs, Math.Max(max, frameMs));
        if (what != "frame")
        {
            Log.Info(
                "portalframes",
                what,
                ("ms", frameMs.ToString("0.0", CultureInfo.InvariantCulture)),
                ("process_ms", processMs.ToString("0.0", CultureInfo.InvariantCulture)),
                ("draw_ms", drawMs.ToString("0.0", CultureInfo.InvariantCulture)),
                ("state", frame.State),
                ("tunnel", frame.TunnelVisible),
                ("world", frame.WorldVisible));
        }

        if (frameMs > SlowFrameMilliseconds)
        {
            _slowFrames++;
            Log.Warn(
                "portalframes",
                "slow frame",
                ("what", what),
                ("ms", frameMs.ToString("0.0", CultureInfo.InvariantCulture)),
                ("process_ms", processMs.ToString("0.0", CultureInfo.InvariantCulture)),
                ("draw_ms", drawMs.ToString("0.0", CultureInfo.InvariantCulture)),
                ("state", frame.State),
                ("elapsed_s", frame.StateElapsed.ToString("0.000", CultureInfo.InvariantCulture)),
                ("limit_ms", SlowFrameMilliseconds));
        }
    }

    private void Settle()
    {
        _settled = _readiness.IsComplete ? _settled + 1 : 0;
        if (_settled < SettleFrames)
        {
            if (_phaseSeconds > SettleTimeoutSeconds)
            {
                Fail($"the starting place was not complete after {SettleTimeoutSeconds:0} s: {_readiness.Describe()}");
                Finish();
            }

            return;
        }

        if (!_teleporter.Enabled)
        {
            Fail("the portal transition is switched off (--portal-transition=off): there is no sequence to capture");
            Finish();
            return;
        }

        if (_destination is null)
        {
            Finish();
            return;
        }

        Log.Info("portalframes", "starting place complete; jumping through the teleport hook", ("to", _destination.Name), ("step_s", FixedStep), ("directory", _directory));
        HideOverlays();
        _phase = Phase.Run;
        _phaseSeconds = 0.0;
        _started = _teleporter.Teleport(() => _navigator.Teleport(_destination));
        if (!_started)
        {
            Fail("the teleport hook did not start portal space");
            Finish();
        }
    }

    /// <summary>
    /// Hides the debug and tool overlays (the position HUD, the F3 overlay, the panels), so the review frames show the tunnel, the
    /// world and the placeholder status line only. The tunnel's own layer and the status line are left alone.
    /// </summary>
    private void HideOverlays()
    {
        var hidden = 0;
        foreach (var layer in GetTree().Root.FindChildren("*", nameof(CanvasLayer), recursive: true, owned: false).OfType<CanvasLayer>())
        {
            if (layer is not (PortalTunnelView or PortalNoticeLine) && layer.Visible)
            {
                layer.Visible = false;
                hidden++;
            }
        }

        Log.Info("portalframes", "overlays hidden for the capture", ("layers", hidden));
    }

    private void Run()
    {
        if (_phaseSeconds > RunTimeoutSeconds)
        {
            Fail($"the sequence did not finish in {RunTimeoutSeconds:0} s (state {_presenter.LastFrame.State}, {_captured} frame(s) taken)");
            Finish();
            return;
        }

        // The presenter stepped in the last frame (priority 15), and the window's last drawn frame is that frame's picture.
        if (_next >= Shots.Length)
        {
            _phase = Phase.Drain;
            return;
        }

        var frame = _presenter.LastFrame;
        if (frame.State != TeleportAnimState.Off)
        {
            _sawActive = true;
        }
        else if (_sawActive || ++_idleFrames > 5)
        {
            Fail(_sawActive ? $"the sequence ended with {_captured} frame(s) taken, before the others were due" : "portal space did not start after the jump");
            Finish();
            return;
        }

        while (_next < Shots.Length)
        {
            var (_, name, state, elapsed) = Shots[_next];
            if (frame.State == state && frame.StateElapsed >= elapsed)
            {
                Capture(_next, frame);
                _next++;
                return;
            }

            if (frame.State > state && state != TeleportAnimState.Off)
            {
                Fail($"the '{name}' frame was missed: the sequence reached state {frame.State} without {state} at {elapsed:0.0} s");
                _next++;
                continue;
            }

            break;
        }
    }

    private void Drain()
    {
        // Let the sequence run out (the arrival's LoginComplete is suppressed offline), then judge the end state.
        if (_presenter.LastFrame.State != TeleportAnimState.Off)
        {
            if (_phaseSeconds > RunTimeoutSeconds)
            {
                Fail($"the sequence did not end in {RunTimeoutSeconds:0} s (state {_presenter.LastFrame.State})");
                Finish();
            }

            return;
        }

        var director = _presenter.Director;
        if (director.LoginCompletesSent != 0 || director.LoginCompletesSuppressed != 1)
        {
            Fail($"offline arrival: LoginComplete sent={director.LoginCompletesSent} suppressed={director.LoginCompletesSuppressed}, expected 0 and 1");
        }

        if (!_visibility.Visible || _presenter.Lens.Overridden)
        {
            Fail($"the world was not given back: visible={_visibility.Visible} lens_overridden={_presenter.Lens.Overridden}");
        }

        Finish();
    }

    private void Capture(int index, in PortalFrame frame)
    {
        var (file, name, _, _) = Shots[index];
        var captureStart = Stopwatch.GetTimestamp();
        try
        {
            CaptureCore(file, name, frame);
        }
        finally
        {
            // The runner's own cost (readback and PNG encode): taken off this frame's time, so it is not the tunnel's.
            _captureMs += (Stopwatch.GetTimestamp() - captureStart) * 1000.0 / Stopwatch.Frequency;
        }
    }

    private void CaptureCore(string file, string name, in PortalFrame frame)
    {
        var path = System.IO.Path.Combine(_directory, file);
        if (ScreenshotWriter.Resolve(path, "portalframes") is not { } full)
        {
            Fail($"the output path for '{name}' was refused");
            return;
        }

        try
        {
            using var image = GetViewport().GetTexture().GetImage();
            if (image is null || image.IsEmpty())
            {
                Fail($"the '{name}' frame: the window image is empty");
                return;
            }

            var lit = ContentSamples(image);
            if (lit < 12)
            {
                Fail($"the '{name}' frame is blank ({lit} of {BlankGridX * BlankGridY} samples differ from the background)");
            }

            if (System.IO.Path.GetDirectoryName(full) is { Length: > 0 } directory)
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            var error = image.SavePng(full);
            if (error != Error.Ok)
            {
                Fail($"the '{name}' frame could not be saved: {error}");
                return;
            }

            _captured++;
            Log.Info(
                "portalframes",
                "frame captured",
                ("name", name),
                ("file", full),
                ("size", $"{image.GetWidth()}x{image.GetHeight()}"),
                ("state", frame.State),
                ("elapsed_s", frame.StateElapsed.ToString("0.000", CultureInfo.InvariantCulture)),
                ("focal", frame.Lens.Focal.ToString("0.0000", CultureInfo.InvariantCulture)),
                ("roll_deg", frame.RollDegrees.ToString("0.0", CultureInfo.InvariantCulture)),
                ("tunnel", frame.TunnelVisible),
                ("world", frame.WorldVisible),
                ("content_samples", lit));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Fail($"the '{name}' frame could not be written: {ex.Message}");
        }
    }

    /// <summary>
    /// How many samples of a coarse grid over the picture's upper four fifths (above the status line) differ visibly from the
    /// picture's most common corner colour: a blank frame has almost none.
    /// </summary>
    private static int ContentSamples(Image image)
    {
        var width = image.GetWidth();
        var height = (int)(image.GetHeight() * 0.8);
        var background = image.GetPixel(0, 0);
        var count = 0;
        for (var gy = 0; gy < BlankGridY; gy++)
        {
            for (var gx = 0; gx < BlankGridX; gx++)
            {
                var pixel = image.GetPixel(Math.Min(width - 1, gx * width / BlankGridX), Math.Min(Math.Max(height - 1, 0), gy * height / BlankGridY));
                if (Math.Abs(pixel.R - background.R) + Math.Abs(pixel.G - background.G) + Math.Abs(pixel.B - background.B) > 0.06f)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private void Fail(string problem)
    {
        _failures++;
        Log.Error("portalframes", "portal-space frames: " + problem);
    }

    private void Finish()
    {
        if (_done)
        {
            return;
        }

        _done = true;
        foreach (var (state, (count, sum, max)) in _perState.OrderBy(pair => pair.Key))
        {
            Log.Info(
                "portalframes",
                "frame times",
                ("state", state),
                ("frames", count),
                ("mean_ms", (sum / count).ToString("0.0", CultureInfo.InvariantCulture)),
                ("max_ms", max.ToString("0.0", CultureInfo.InvariantCulture)));
        }

        if (_slowFrames > 0)
        {
            Fail($"{_slowFrames} frame(s) took more than {SlowFrameMilliseconds:0} ms (the longest {_maxFrameMs:0.0} ms); see the slow frame lines above");
        }

        var ok = _failures == 0 && _captured == Shots.Length && _started;
        Log.Info(
            "portalframes",
            "portal space frames done",
            ("captured", _captured),
            ("failures", _failures),
            ("max_frame_ms", _maxFrameMs.ToString("0.0", CultureInfo.InvariantCulture)),
            ("slow_frames", _slowFrames),
            ("frames", _presenter.Frames),
            ("simulated_s", _presenter.Now.ToString("0.00", CultureInfo.InvariantCulture)));
        Log.FlushToGodot();
        GetTree().Quit(ok ? 0 : 1);
    }
}
