using System.Diagnostics;
using System.Globalization;
using Godot;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Options;
using Lifestone.Game.Core;
using Lifestone.Game.Player;
using Lifestone.Game.UI;
using Lifestone.Game.World;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// <c>--teleport-tour[=LAPS]</c> (with <c>--teleport-stops=a,b,...</c>): jumps through teleport-panel destinations the
/// way the panel does (<see cref="Navigator.Teleport(TeleportDestination)"/>), and measures each jump: the synchronous
/// call, the frame the jump happens in (start of that frame to the start of the next, so it includes the end-of-frame
/// node frees), the slowest frame in the second after it, and the time until the new place is complete
/// (<see cref="CaptureReadiness"/>). Logs one line per jump and a summary, then quits (0 = every stop found and loaded).
/// </summary>
/// <remarks>
/// Before each jump the runner waits until the current place is complete and 30 more frames have rendered, so a jump
/// always starts from a fully streamed world (the worst case: everything is dropped). The first lap visits places the
/// first time (cold caches); later laps measure warm caches.
/// </remarks>
public partial class TeleportTourRunner : Node
{
    /// <summary>Default stops: towns spread over the map plus indoor places (named in both the old and new catalogues).</summary>
    public static readonly IReadOnlyList<string> DefaultStops =
        ["Holtburg", "Arwic", "Yaraq", "Town Network", "Shoushi", "Underground City", "Cragstone", "Marketplace", "Glenden Wood", "Rithwic"];

    private const int SettleFrames = 30;
    private const double AfterJumpSeconds = 1.0;
    private const double LoadTimeoutSeconds = 30.0;

    private readonly FrameTimeRecorder _callMs = new();
    private readonly FrameTimeRecorder _jumpFrameMs = new();
    private readonly FrameTimeRecorder _afterMaxMs = new();
    private readonly FrameTimeRecorder _afterMaxWithoutGcMs = new();
    private readonly FrameTimeRecorder _loadSeconds = new();
    private Navigator _navigator = null!;
    private CaptureReadiness _readiness = null!;
    private IReadOnlyList<TeleportDestination> _stops = [];
    private int _laps = 1;
    private int _index = -1;
    private int _failures;
    private Phase _phase = Phase.Settle;
    private int _settled;
    private long _frameStart;
    private long _jumpFrameStart;
    private double _phaseSeconds;
    private double _callThisJump;
    private double _jumpFrameThisJump;
    private double _afterMaxThisJump;
    private double _afterMaxGcThisJump;
    private TimeSpan _gcAtFrameStart;
    private double _processThisJump;
    private double? _completeSeconds;
    private FrameTail _tail = null!;
    private bool _done;

    private enum Phase
    {
        Settle,
        JumpFrame,
        After,
        Loading,
    }

    /// <summary>
    /// Reads <c>--teleport-tour[=LAPS]</c> and <c>--teleport-stops</c> (registered in Lifestone.Client's
    /// <c>OptionCatalog</c>); null when the tour was not asked for. A value that does not parse is added to
    /// <paramref name="errors"/> and the option keeps its default.
    /// </summary>
    public static (int Laps, IReadOnlyList<string> Stops)? Requested(IEnumerable<string> args, List<string> errors)
    {
        int? laps = null;
        IReadOnlyList<string> stops = DefaultStops;
        foreach (var argument in CommandLineArgument.ParseAll(args))
        {
            switch (argument.Name)
            {
                case "--teleport-tour":
                    laps = argument.Value.Length == 0 ? 1 : Math.Clamp(OptionValues.Int(argument, errors, 1), 1, 100);
                    break;
                case "--teleport-stops":
                    stops = argument.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
            }
        }

        return laps is { } l ? (l, stops) : null;
    }

    public void Initialize(GameServices services, WorldRoot world, FlyCamera camera, Navigator navigator, int laps, IReadOnlyList<string> stops)
    {
        _navigator = navigator;
        _readiness = new CaptureReadiness(services, world);
        _laps = laps;
        var found = new List<TeleportDestination>();
        foreach (var stop in stops)
        {
            if (navigator.Catalog.Find(stop) is { } destination)
            {
                found.Add(destination);
            }
            else
            {
                Log.Error("tour", "teleport stop not in the catalogue", ("stop", stop));
                _failures++;
            }
        }

        _stops = found;
        camera.InputEnabled = false;

        // First of every node's _Process (GameServices drains at -1000): the frame-start timestamps bracket whole frames.
        // A second node runs last, so a jump frame splits into its process phase and the rest (end-of-frame frees, render).
        ProcessPriority = -2000;
        _tail = new FrameTail { Name = "TeleportTourTail", ProcessPriority = int.MaxValue };
        AddChild(_tail);
        Log.Info("tour", "teleport tour", ("stops", _stops.Count), ("laps", _laps), ("names", string.Join(", ", _stops.Select(s => s.Name))));
    }

    public override void _Process(double delta)
    {
        if (_done)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var frameMs = _frameStart == 0 ? 0.0 : (now - _frameStart) * 1000.0 / Stopwatch.Frequency;
        _frameStart = now;

        // Garbage-collection pause inside the frame that just ended (any thread's collection suspends this one).
        var gcNow = GC.GetTotalPauseDuration();
        var frameGcMs = (gcNow - _gcAtFrameStart).TotalMilliseconds;
        _gcAtFrameStart = gcNow;
        _phaseSeconds += delta;
        switch (_phase)
        {
            case Phase.Settle:
                _settled = _readiness.IsComplete ? _settled + 1 : 0;
                if (_settled >= SettleFrames || _phaseSeconds > LoadTimeoutSeconds)
                {
                    Jump();
                }

                break;
            case Phase.JumpFrame:
                // This frame began right after the jump frame ended: the jump frame's whole wall time.
                _jumpFrameThisJump = (now - _jumpFrameStart) * 1000.0 / Stopwatch.Frequency;
                _processThisJump = (_tail.LastTimestamp - _jumpFrameStart) * 1000.0 / Stopwatch.Frequency;
                _afterMaxThisJump = 0;
                _afterMaxGcThisJump = 0;
                _completeSeconds = null;
                _phase = Phase.After;
                break;
            case Phase.After:
            case Phase.Loading:
                if (_phase == Phase.After && frameMs > _afterMaxThisJump)
                {
                    _afterMaxThisJump = frameMs;
                    _afterMaxGcThisJump = frameGcMs;
                }

                var sinceJump = (now - _jumpFrameStart) / (double)Stopwatch.Frequency;
                if (_completeSeconds is null && _readiness.IsComplete)
                {
                    _completeSeconds = sinceJump;
                }

                if (sinceJump >= AfterJumpSeconds)
                {
                    _phase = Phase.Loading;
                }

                if (_phase == Phase.Loading && (_completeSeconds is not null || sinceJump > LoadTimeoutSeconds))
                {
                    Record(_completeSeconds is not null, _completeSeconds ?? sinceJump);
                    _phase = Phase.Settle;
                    _settled = 0;
                    _phaseSeconds = 0;
                }

                break;
        }
    }

    private void Jump()
    {
        _index++;
        if (_index >= _stops.Count * _laps)
        {
            Finish();
            return;
        }

        var destination = _stops[_index % _stops.Count];
        var start = Stopwatch.GetTimestamp();
        _navigator.Teleport(destination);
        _callThisJump = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _jumpFrameStart = _frameStart;
        _phase = Phase.JumpFrame;
        _phaseSeconds = 0;
    }

    private void Record(bool loaded, double seconds)
    {
        var destination = _stops[_index % _stops.Count];
        if (!loaded)
        {
            _failures++;
            Log.Warn("tour", "place not complete after the timeout", ("to", destination.Name), ("state", _readiness.Describe()));
        }

        _callMs.Add(_callThisJump);
        _jumpFrameMs.Add(_jumpFrameThisJump);
        _afterMaxMs.Add(_afterMaxThisJump);
        _afterMaxWithoutGcMs.Add(_afterMaxThisJump - _afterMaxGcThisJump);
        _loadSeconds.Add(seconds);
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"lap {(_index / _stops.Count) + 1} {destination.Name}: call {_callThisJump:0.0} ms, jump frame {_jumpFrameThisJump:0.0} ms (process {_processThisJump:0.0} ms, then frees and render {_jumpFrameThisJump - _processThisJump:0.0} ms), slowest frame in the next second {_afterMaxThisJump:0.0} ms (GC pause in it {_afterMaxGcThisJump:0.0} ms), complete after {seconds:0.00} s{(loaded ? string.Empty : " (TIMEOUT)")}");
        Log.Info("tour", "jump", ("result", line));
    }

    private void Finish()
    {
        _done = true;
        Log.Info(
            "tour",
            "teleport tour summary",
            ("jumps", _jumpFrameMs.Count),
            ("call_ms_median", Round(_callMs.Percentile(50))),
            ("call_ms_max", Round(_callMs.Max)),
            ("jump_frame_ms_median", Round(_jumpFrameMs.Percentile(50))),
            ("jump_frame_ms_max", Round(_jumpFrameMs.Max)),
            ("next_second_max_ms_median", Round(_afterMaxMs.Percentile(50))),
            ("next_second_max_ms_max", Round(_afterMaxMs.Max)),
            ("next_second_max_without_its_gc_ms_median", Round(_afterMaxWithoutGcMs.Percentile(50))),
            ("next_second_max_without_its_gc_ms_max", Round(_afterMaxWithoutGcMs.Max)),
            ("complete_s_median", Round(_loadSeconds.Percentile(50))),
            ("complete_s_max", Round(_loadSeconds.Max)),
            ("failures", _failures));
        Log.FlushFile();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static double Round(double value) => Math.Round(value, 2);

    /// <summary>Timestamps the end of every frame's process phase (it runs after every other node's _Process).</summary>
    private sealed partial class FrameTail : Node
    {
        public long LastTimestamp { get; private set; }

        public override void _Process(double delta) => LastTimestamp = Stopwatch.GetTimestamp();
    }
}
