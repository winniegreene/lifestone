using System.Diagnostics;
using Godot;
using Lifestone.Client.Cinematic;
using Lifestone.Game.Core;
using Lifestone.Game.UI;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.Cinematic;

/// <summary>
/// <c>--cinematic=FILE</c> in the spell viewer: films the spell shots of a shot list on the viewer's Holtburg stage
/// (docs/hero-video.md). The spell viewer's own deterministic stepping does the work (its stage advances by exactly the
/// step this director gives, like the showcase's 1/30 s, here the film's 1/60 s); this adds the shot's keyed orbit
/// camera (<see cref="OrbitTrack"/>: tight framing on the hands, the projectile or the drudge), the cast at a set moment,
/// a black cover over the loading frames, and the "shot recorded" log line tools\hero-video.ps1 trims by.
/// </summary>
/// <remarks>
/// <para>Called from <c>SpellViewer._Process</c> once per frame before the stage advances (the same hook the showcase
/// director uses). Every HUD layer is hidden; the spell words over the caster's head are hidden unless the shot asks for
/// them. The time of day is the viewer's start-up one (<c>--time-of-day</c>, which tools\hero-video.ps1 passes from the
/// shot's <c>time</c>).</para>
/// </remarks>
public sealed class SpellCinematic
{
    private const string Category = "cinematic";

    private readonly SpellViewer.SpellViewer _viewer;
    private readonly ShotList _list;
    private readonly IReadOnlyList<Shot> _shots;
    private readonly CanvasLayer _cover;
    private readonly Stopwatch _wait = new();
    private readonly Stopwatch _recordWatch = new();
    private int _index = -1;
    private Shot? _shot;
    private OrbitTrack? _track;
    private long _frame = -1;
    private long _firstFrame;
    private int _waited;
    private int _preroll;
    private int _recorded;
    private int _frameCount;
    private bool _recording;
    private bool _cast;
    private bool _impactLogged;
    private Vec3d? _lastProjectile;
    private readonly string? _capture;
    private FrameRecorder? _recorder;

    private SpellCinematic(SpellViewer.SpellViewer viewer, ShotList list, IReadOnlyList<Shot> shots, bool missing, string? capture)
    {
        _viewer = viewer;
        _list = list;
        _shots = shots;
        _capture = capture;
        ExitCode = missing ? 1 : 0;
        CinematicView.ApplyCaptureSettings(viewer.GetViewport(), list, Category);
        NavigationUi.SetHudVisible(viewer, false);
        _cover = CinematicView.AddCover(viewer);
        viewer.Camera.InputEnabled = false;
        viewer.Camera.ShiftRight = 0f;
        Log.Info(Category, "filming spells", ("shots", string.Join(",", shots.Select(s => s.Id))), ("fixed_step", CinematicView.FixedStep || capture is not null), ("capture", capture));
        NextShot();
    }

    /// <summary>Seconds the stage advances per frame while filming (the film's frame time).</summary>
    public double StepSeconds => 1.0 / _list.Fps;

    public bool Finished { get; private set; }

    public int ExitCode { get; private set; }

    /// <summary>
    /// The director for <c>--cinematic</c>, or null when the run does not ask for one. A shot list that does not load,
    /// or one without spell shots to film, gives a director that finishes at once with exit code 1.
    /// </summary>
    public static SpellCinematic? Create(SpellViewer.SpellViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        var options = CinematicOptions.Parse(OS.GetCmdlineUserArgs());
        foreach (var error in options.Errors)
        {
            Log.Error(Category, "bad argument", ("problem", error));
        }

        if (!options.Requested)
        {
            return null;
        }

        if (options.StillsDirectory is not null)
        {
            Log.Warn(Category, "--cinematic-stills is for world shots; the spell viewer films its shots (stills: the viewer's own --shots)");
        }

        var list = CinematicView.Load(options.ShotListPath!, Category) ?? new ShotList();
        var shots = CinematicView.Select(list, ShotScene.Spell, options.ShotIds, Category, out var missing);
        if (shots.Count == 0)
        {
            Log.Error(Category, "no spell shots to film", ("ids", string.Join(",", options.ShotIds)));
            missing = true;
        }

        return new SpellCinematic(viewer, list, shots, missing || options.Errors.Count > 0, options.CaptureDirectory);
    }

    /// <summary>One frame, before the stage advances by <paramref name="step"/> (<see cref="StepSeconds"/>).</summary>
    public void Process(double step)
    {
        _frame++;
        if (Finished || _shot is null)
        {
            return;
        }

        var shot = _shot;
        if (!_recording)
        {
            _waited++;
            Apply(_track!.Step(0, step, Focus));
            if (!_viewer.StaticsReady || !_viewer.ParticlesReady || _frame < 3)
            {
                return;
            }

            // A negative castAt casts during the pre-roll, so the film can open part-way through a long wind-up.
            var prerollFrames = (int)Math.Round(Math.Max(shot.Preroll, -shot.CastAt + 0.1) / step);
            if (shot.CastAt < 0 && !_cast && _preroll == prerollFrames - (int)Math.Round(-shot.CastAt / step))
            {
                CastNow(shot, shot.CastAt);
            }

            if (_preroll++ < prerollFrames)
            {
                return;
            }

            _recording = true;
            _recorded = 0;
            _frameCount = shot.FrameCount(_list.Fps);
            _firstFrame = _frame;
            _track.Reset();
            _cover.Visible = false;
            _recordWatch.Restart();
            if (_capture is not null)
            {
                // The stage steps by exactly 1/fps; the living statics and the sky step by the engine's delta, which
                // must be the same fixed step (the engine's --fixed-fps).
                var delta = _viewer.GetProcessDeltaTime();
                if (Math.Abs((delta * _list.Fps) - 1.0) > 1e-3)
                {
                    Log.Error(Category, "frames are not stepped at the film's rate; pass the engine's --fixed-fps", ("delta", delta), ("fps", _list.Fps));
                    ExitCode = 1;
                }

                _recorder = FrameRecorder.Start(_viewer.GetViewport(), _capture, shot.Id, _list.Fps, Category);
                if (_recorder is null)
                {
                    ExitCode = 1;
                }
            }

            Log.Info(Category, "recording", ("id", shot.Id), ("first_frame", _firstFrame), ("frames", _frameCount), ("fps", _list.Fps),
                ("engine_process_frames", Engine.GetProcessFrames()), ("waited_s", _wait.Elapsed.TotalSeconds), ("waited_frames", _waited));
        }

        if (_recorded >= _frameCount)
        {
            if (_recorder is { } recorder)
            {
                if (!recorder.Finish() || recorder.Frames != _frameCount)
                {
                    Log.Error(Category, "the shot's frames were not all written", ("id", shot.Id), ("written", recorder.Frames), ("frames", _frameCount));
                    ExitCode = 1;
                }

                recorder.Dispose();
                _recorder = null;
            }

            CinematicView.LogRecorded(Category, shot, _firstFrame, _frameCount, _list.Fps, _wait.Elapsed.TotalSeconds - _recordWatch.Elapsed.TotalSeconds, _waited, _recordWatch.Elapsed.TotalSeconds);
            NextShot();
            return;
        }

        var t = _recorded * step;
        if (!_cast && t >= shot.CastAt - 1e-9)
        {
            CastNow(shot, t);
        }

        if (_cast && !_impactLogged && _viewer.Stage.Simulation?.Projectiles.Select(p => p.ImpactTime).FirstOrDefault(i => i is not null) is { } impact)
        {
            _impactLogged = true;
            Log.Info(Category, "first impact", ("id", shot.Id), ("shot_t", shot.CastAt + (impact - _viewer.Stage.CastTime)));
        }

        Apply(_track!.Step(t, step, Focus));
        _recorder?.CaptureThisFrame();
        _recorded++;
    }

    /// <summary>Casts the shot's spell and logs its timeline in shot time (for keying the camera, docs/hero-video.md).</summary>
    private void CastNow(Shot shot, double shotTime)
    {
        _cast = true;
        _impactLogged = false;
        _lastProjectile = null;
        _viewer.Cast(shot.Spell!.Value, shot.Fizzle);
        if (_viewer.Stage.Simulation is { } sim)
        {
            var effect = sim.CastStartTime - _viewer.Stage.CastTime + sim.Visual.Plan.EffectTime;
            Log.Info(Category, "cast", ("id", shot.Id), ("spell", sim.Visual.Spell.Name), ("shot_t", shotTime), ("effect_shot_t", shotTime + effect),
                ("projectile", sim.Visual.Projectile?.Kind));
        }
    }

    private void NextShot()
    {
        _cover.Visible = true;
        _recording = false;
        _cast = false;
        _index++;
        if (_index >= _shots.Count)
        {
            Finished = true;
            if (_viewer.Exceptions > 0)
            {
                ExitCode = 1;
            }

            Log.Info(Category, "done", ("shots", _shots.Count), ("exceptions", _viewer.Exceptions), ("frame", _frame));
            return;
        }

        var shot = _shot = _shots[_index];
        _track = ShotPaths.BuildOrbit(shot);
        _waited = 0;
        _preroll = 0;
        _wait.Restart();
        if (_viewer.GetNodeOrNull<Label3D>("Words") is { } words)
        {
            words.Layers = shot.Words ? 1u : 0u; // the viewer's chant label; layer 0 = drawn by no camera
        }

        if (shot.DayFraction is { } fraction && Math.Abs(fraction - GameServices.Instance.Options.TimeOfDay) > 1e-4)
        {
            Log.Warn(Category, "the spell viewer's time of day comes from --time-of-day at start-up; the shot's time is not applied",
                ("id", shot.Id), ("shot_time", fraction), ("time_of_day", GameServices.Instance.Options.TimeOfDay));
        }

        Log.Info(Category, "shot", ("id", shot.Id), ("spell", shot.Spell), ("fizzle", shot.Fizzle), ("cast_at", shot.CastAt), ("seconds", shot.Duration), ("keys", shot.Keys.Count));
    }

    private void Apply(OrbitSample sample)
    {
        _viewer.SetCamera((float)sample.Yaw, (float)sample.Pitch, (float)sample.Distance, new AcVector3((float)sample.Focus.X, (float)sample.Focus.Y, (float)sample.Focus.Z));
        _viewer.Camera.Fov = (float)sample.Fov;
    }

    /// <summary>The stage points an orbit key can name (stage space: landblock 0xA9B4's local AC frame).</summary>
    private Vec3d Focus(string name)
    {
        var stage = _viewer.Stage;
        var caster = stage.CasterFrame.Origin;
        var target = stage.TargetFrame.Origin;
        var east = AcVector3.Normalize(target - caster);
        var hands = caster + (east * 0.55f) + new AcVector3(0, 0, 1.15f);
        switch (name)
        {
            case "caster":
                return V(caster + new AcVector3(0, 0, 1.1f));
            case "target":
                return V(target + new AcVector3(0, 0, 0.9f));
            case "hands":
                return V(hands);
            case "projectile":
                if (stage.Simulation is { Projectiles.Count: > 0 } sim)
                {
                    _lastProjectile = V(sim.Projectiles[0].Actor.Frame.Origin);
                }

                return _lastProjectile ?? V(hands);
            default:
                return V(_viewer.StageFocus);
        }
    }

    private static Vec3d V(AcVector3 v) => new(v.X, v.Y, v.Z);
}
