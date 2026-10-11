using System.Diagnostics;
using System.Globalization;
using Godot;
using Lifestone.Client.Cinematic;
using Lifestone.Client.Navigation;
using Lifestone.Client.World;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.Player;
using Lifestone.Game.UI;
using Lifestone.Game.World;
using Lifestone.Game.World.Sky;

namespace Lifestone.Game.Cinematic;

/// <summary>
/// <c>--cinematic=FILE</c> in the main scene: films the world shots of a shot list with a deterministic, frame-stepped
/// camera (docs/hero-video.md). Per shot: set the Dereth clock and day group, jump there (indoor starts through the
/// interiors, as the teleport panel), wait under a black cover until the place is complete (<see cref="CaptureReadiness"/>:
/// streaming, objects, the interiors' cells, the main-thread queue) and the sky is built, run the pre-roll (living statics
/// get going), then record: every frame the camera takes the path's pose for that frame's time and the clock its time.
/// With <c>--cinematic-capture</c> exactly those frames are written (<see cref="FrameRecorder"/>); under Movie Maker the log
/// line "shot recorded" gives each shot's frame numbers, so loading frames never reach the film either way.
/// </summary>
/// <remarks>
/// <para>Only stable main-scene APIs are used: <see cref="FlyCamera"/> (placement, field of view), <see cref="WorldRoot"/>
/// through <see cref="Navigator"/> (jumps), <see cref="InteriorLocations"/> and teleport names through
/// <see cref="GoldenViewpoints.TryResolveTarget"/>, <see cref="SkyController"/> (clock, day group, refresh), and
/// <see cref="NavigationUi.SetHudVisible"/>.</para>
/// <para><b>Light and fog</b> are refreshed every recorded frame (retail refreshes them every 15 s and 0.8 s of client
/// time), so a time-lapse dims smoothly instead of in steps. A clock speed of exactly 1 would make the sky follow the
/// real clock (retail's live mode, <c>DerethTimeController.IsLive</c>), which a recording slower than real time must
/// not; such speeds run at 1 + 1e-9 instead.</para>
/// <para><b>Outdoors</b> the eye is kept <see cref="Shot.Clearance"/> above the exact ground; a path that comes closer is
/// logged when the shot starts (and clamped while it plays), so a key can be fixed rather than the camera cutting through
/// a hill.</para>
/// <para><b>Stills</b> (<c>--cinematic-stills</c>): the same set-up, then the shot is played frame by frame at the list's
/// rate (so streaming and the interiors' cell tracker follow the motion as in the film) with the clock set exactly for each
/// frame, and a PNG is saved at each fraction of <c>--cinematic-stills-at</c>, for framing without recording a film.</para>
/// </remarks>
public partial class CinematicDirector : Node
{
    private const string Category = "cinematic";
    private const double ReadyTimeoutSeconds = 150.0;

    private readonly Stopwatch _wait = new();
    private readonly Stopwatch _recording = new();
    private GameServices _services = null!;
    private FlyCamera _camera = null!;
    private SkyController _sky = null!;
    private Navigator _navigator = null!;
    private CaptureReadiness _readiness = null!;
    private ShotList _list = null!;
    private IReadOnlyList<Shot> _shots = [];
    private CinematicOptions _options = new();
    private CanvasLayer _cover = null!;
    private Phase _phase = Phase.Idle;
    private int _index = -1;
    private Shot? _shot;
    private CameraPath? _path;
    private bool _outdoor;
    private double _startGameTime;
    private double _speed;
    private long _frame = -1;
    private long _firstFrame;
    private int _waitedFrames;
    private int _prerollFrames;
    private int _recorded;
    private int _frameCount;
    private int _fps;
    private double _previewTime;
    private string? _pendingStill;
    private int _failures;
    private bool _fixed;
    private FrameRecorder? _recorder;

    private enum Phase
    {
        Idle,
        Waiting,
        Recording,
        Done,
    }

    /// <summary>
    /// Main thread, from <c>Main._Ready</c> after the sky track: hides every HUD, applies the capture settings and jumps
    /// to the first shot before the world streams anything around the start location.
    /// </summary>
    public void Initialize(GameServices services, WorldRoot world, FlyCamera camera, SkyController sky, ShotList list, IReadOnlyList<Shot> shots, CinematicOptions options)
    {
        _services = services;
        _camera = camera;
        _sky = sky;
        _list = list;
        _shots = shots;
        _options = options;
        _fixed = CinematicView.FixedStep || options.CaptureDirectory is not null;
        _navigator = Navigator.Create(GetParent(), services, world, camera);
        _readiness = new CaptureReadiness(services, world);

        // Before the camera (0), the interiors (5), the world's rebase and streaming (10) and the sky (20).
        ProcessPriority = -10;
        _camera.InputEnabled = false;
        CinematicView.ApplyCaptureSettings(GetViewport(), list, Category);
        NavigationUi.SetHudVisible(this, false);
        _cover = CinematicView.AddCover(this);
        Log.Info(Category, options.StillsDirectory is null ? "filming" : "stills", ("shots", string.Join(",", shots.Select(s => s.Id))),
            ("fixed_step", _fixed), ("capture", options.CaptureDirectory), ("movie_maker", CinematicView.Recording), ("dir", options.StillsDirectory));
        NextShot();
    }

    public override void _Process(double delta)
    {
        _frame++;
        switch (_phase)
        {
            case Phase.Waiting:
                Wait(delta);
                break;
            case Phase.Recording:
                Record(delta);
                break;
        }
    }

    private void NextShot()
    {
        _cover.Visible = true;
        _index++;
        if (_index >= _shots.Count)
        {
            Finish();
            return;
        }

        var shot = _shot = _shots[_index];
        if (!GoldenViewpoints.TryResolveTarget(shot.Start!, _navigator.Catalog, out var location, out var error))
        {
            Log.Error(Category, "shot start not found", ("id", shot.Id), ("start", shot.Start), ("error", error));
            _failures++;
            NextShot();
            return;
        }

        var indoor = GoldenViewpoints.IsIndoor(location);
        _outdoor = !indoor;
        var anchor = indoor ? location.Global : CameraPlacement.Spot(location, _navigator.GroundAt);
        try
        {
            _path = ShotPaths.Build(shot, new Vec3d(anchor.X, anchor.Y, anchor.Z), Ground);
        }
        catch (ArgumentException ex)
        {
            Log.Error(Category, "bad camera keys", ("id", shot.Id), ("error", ex.Message));
            _failures++;
            NextShot();
            return;
        }

        if (_outdoor)
        {
            var clearance = ShotPaths.CheckClearance(_path, Ground, shot.Clearance);
            if (!clearance.Clear)
            {
                Log.Warn(Category, "the path comes closer to the ground than the shot's clearance; the eye is lifted there", ("id", shot.Id),
                    ("min_clearance_m", clearance.MinClearance), ("at_s", clearance.AtTime), ("samples_below", clearance.SamplesBelow), ("required_m", shot.Clearance));
            }
        }

        // The clock: the shot's day group (forced, so a time-lapse past midnight keeps the weather), its start time on the
        // first day from 10 P.Y. whose retail day group that is (as the golden viewpoints), standing still until recording.
        var dayFraction = shot.DayFraction ?? _services.Options.TimeOfDay;
        _sky.SetDayGroup(shot.DayGroup);
        _startGameTime = GoldenViewpoints.GameTimeFor(new GoldenTime(dayFraction, shot.DayGroup), _services.Sky);
        _speed = shot.EffectiveTimeSpeed(_services.Sky.Calendar.DayLength);
        _sky.Time.SetSpeed(0.0);
        _sky.SetGameTime(_startGameTime);
        _sky.Director.RefreshNow();

        var first = _path.Sample(0);
        if (indoor)
        {
            // As the teleport panel: in the cell from the first frame (a dungeon streams indoor at once), then the first pose.
            _navigator.PlaceIndoors(location with { HeadingDegrees = first.Yaw }, shot.Id);
        }
        else
        {
            _navigator.Place(new CameraPose(Eye(first), first.Yaw, first.Pitch), shot.Id);
        }

        Pose(first);
        _wait.Restart();
        _waitedFrames = 0;
        _prerollFrames = 0;
        _phase = Phase.Waiting;
        Log.Info(Category, "shot", ("id", shot.Id), ("start", location.Name), ("indoor", indoor), ("anchor", anchor), ("seconds", shot.Duration),
            ("path_m", _path.Length), ("timing", _path.Timing), ("ease", shot.Ease), ("day_fraction", dayFraction), ("day_group", shot.DayGroup),
            ("time_speed", _speed), ("keys", shot.Keys.Count));
    }

    private void Wait(double delta)
    {
        var shot = _shot!;
        _waitedFrames++;
        Pose(_path!.Sample(0));
        var ready = _readiness.IsComplete && _sky.IsReady;
        if (!ready && _wait.Elapsed.TotalSeconds < ReadyTimeoutSeconds)
        {
            return;
        }

        if (!ready && _prerollFrames == 0)
        {
            Log.Warn(Category, "the place or the sky is not complete; filming anyway", ("id", shot.Id), ("seconds", _wait.Elapsed.TotalSeconds),
                ("state", _readiness.Describe()), ("sky_ready", _sky.IsReady));
        }

        // Pre-roll: the place is complete; living statics (chimney smoke, torches, birds) run for a while before the film starts.
        var prerollFrames = (int)Math.Round(shot.Preroll / Math.Max(1e-6, StepOf(delta)));
        if (_prerollFrames++ < prerollFrames)
        {
            return;
        }

        // Record from this frame on: it is drawn with the first pose and the clock at the shot's start. Stills play the
        // shot the same way, frame by frame at the list's rate, and save the frames at their moments.
        _fps = _fixed && _options.StillsDirectory is null ? (int)Math.Round(1.0 / Math.Max(1e-6, delta)) : _list.Fps;
        if (_fixed && Math.Abs((delta * _fps) - 1.0) > 1e-3)
        {
            Log.Error(Category, "frames are not stepped at a fixed rate; pass the engine's --fixed-fps", ("delta", delta));
            _failures++;
        }
        else if (_fixed && _fps != _list.Fps)
        {
            Log.Warn(Category, "the engine's fixed frame rate is not the shot list's; filming at the engine's", ("engine_fps", _fps), ("list_fps", _list.Fps));
        }

        if (_options.CaptureDirectory is { } capture && _options.StillsDirectory is null)
        {
            _recorder = FrameRecorder.Start(GetViewport(), capture, shot.Id, _fps, Category);
            if (_recorder is null)
            {
                _failures++;
            }
        }

        _frameCount = shot.FrameCount(_fps);
        _recorded = 0;
        _previewTime = 0;
        _firstFrame = _frame;
        _cover.Visible = false;
        _phase = Phase.Recording;
        _recording.Restart();
        _pendingStill = null;
        if (_options.StillsDirectory is null)
        {
            _sky.SetGameTime(_startGameTime - (_speed * StepOf(delta))); // the sky advances by one step after this node
            _sky.Time.SetSpeed(NotLive(_speed));
        }
        Log.Info(Category, "recording", ("id", shot.Id), ("first_frame", _firstFrame), ("frames", _frameCount), ("fps", _fps),
            ("engine_process_frames", Engine.GetProcessFrames()), ("engine_frames_drawn", Engine.GetFramesDrawn()), ("waited_s", _wait.Elapsed.TotalSeconds), ("waited_frames", _waitedFrames));
        Record(delta);
    }

    private void Record(double delta)
    {
        var shot = _shot!;
        if (_pendingStill is { } still)
        {
            // The viewport holds the frame drawn after the previous update: the still's pose.
            _pendingStill = null;
            if (!ScreenshotWriter.Save(GetViewport(), still, Category, ("id", shot.Id), ("camera", _camera.WorldPosition), ("heading", _camera.Heading), ("pitch", _camera.Pitch)))
            {
                _failures++;
            }
        }

        if (_recorded >= _frameCount)
        {
            if (_recorder is { } recorder)
            {
                if (!recorder.Finish() || recorder.Frames != _frameCount)
                {
                    Log.Error(Category, "the shot's frames were not all written", ("id", shot.Id), ("written", recorder.Frames), ("frames", _frameCount));
                    _failures++;
                }

                recorder.Dispose();
                _recorder = null;
            }

            CinematicView.LogRecorded(Category, shot, _firstFrame, _frameCount, _fps, _wait.Elapsed.TotalSeconds - _recording.Elapsed.TotalSeconds, _waitedFrames, _recording.Elapsed.TotalSeconds);
            NextShot();
            return;
        }

        var stills = _options.StillsDirectory is not null;
        var t = _fixed || stills ? _recorded / (double)_fps : _previewTime;
        Pose(_path!.Sample(Math.Min(t, shot.Duration)));
        _recorder?.CaptureThisFrame();
        if (stills)
        {
            // Stills: the clock exactly at this moment (whatever the real frame time), and a PNG at each still's frame.
            _sky.SetGameTime(_startGameTime + (_speed * t));
            foreach (var fraction in _options.StillsAt)
            {
                if ((int)Math.Round(fraction * (_frameCount - 1)) == _recorded)
                {
                    _pendingStill = System.IO.Path.Combine(_options.StillsDirectory!, string.Create(CultureInfo.InvariantCulture, $"{shot.Id}-{t:00.00}.png"));
                }
            }
        }

        _sky.Director.RefreshNow(); // smooth light and fog every frame (class remarks)
        _recorded++;
        _previewTime += delta;
        if (!_fixed && !stills && _previewTime >= shot.Duration)
        {
            _frameCount = _recorded; // a preview ends on time, whatever its frame rate
        }
    }

    public override void _ExitTree() => _recorder?.Dispose();

    private void Finish()
    {
        if (_phase == Phase.Done)
        {
            return;
        }

        _phase = Phase.Done;
        _cover.Visible = true;
        Log.Info(Category, "done", ("shots", _shots.Count), ("failures", _failures), ("frame", _frame));
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    /// <summary>Puts the camera at a sample: outdoors never closer to the ground than the shot's clearance.</summary>
    private void Pose(CameraSample sample)
    {
        _camera.PlaceAt(Eye(sample), sample.Yaw, sample.Pitch);
        _camera.Camera.Fov = (float)sample.Fov;
    }

    private GlobalPosition Eye(CameraSample sample)
    {
        var eye = new GlobalPosition(sample.Position.X, sample.Position.Y, sample.Position.Z);
        if (_outdoor && _shot is { } shot && _navigator.GroundAt(eye) is { } ground && eye.Z < ground + shot.Clearance)
        {
            eye = eye with { Z = ground + shot.Clearance };
        }

        return eye;
    }

    private double? Ground(double x, double y) => _navigator.GroundAt(new GlobalPosition(x, y, 0));

    private double StepOf(double delta) => _fixed ? delta : 1.0 / _list.Fps;

    /// <summary>A clock speed that is not retail's live mode (class remarks).</summary>
    private static double NotLive(double speed) => speed == 1.0 ? 1.0 + 1e-9 : speed;
}
