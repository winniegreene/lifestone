using System.Globalization;
using Godot;
using Lifestone.Client.Interiors;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.World;
using Vector3 = System.Numerics.Vector3;

namespace Lifestone.Game.Player;

/// <summary>
/// <c>--offline-player-frames[=DIR]</c>: walks the offline player through four review frames and saves them to <c>DIR</c> (default
/// <see cref="Lifestone.Client.Avatars.AvatarsOptions.DefaultFramesDirectory"/>, gitignored): the chase camera behind the running character in Holtburg,
/// first person, inside a building, and the character mid-jump. Each frame is taken only when the state it is named for has been checked in the
/// frame that was drawn (running at speed with the body visible; the camera in the head with the body hidden; the player and the camera both in a room;
/// off the ground and rising), and its caption on screen says what was checked. A frame whose state does not hold is not saved and counts as a failure.
/// </summary>
/// <remarks>
/// <para><b>The character is the stand-in</b> (a placeholder look, labelled on screen); the world is the retail dats, so the images are kept out of the
/// repository (<c>docs/screenshots/</c> is gitignored).</para>
/// <para><b>Capture.</b> The viewport's image is the last frame drawn, so a frame is requested in one frame (the state is checked and the caption set, both
/// drawn) and read at the start of the next. The frame after a capture is not counted in the frame times (the readback and the PNG encode are this runner's
/// own cost).</para>
/// <para><b>Verdict</b> (read by <c>tools\smoke.ps1</c>): <c>player frames done captured=4 failures=0 max_frame_ms=&lt;x&gt;</c>. A failure is a state that
/// did not hold, a frame that was blank or not saved, a wait that timed out, or a frame over 50 ms; the exit code is 0 only when four frames were taken and
/// nothing failed.</para>
/// </remarks>
public sealed partial class PlayerFramesRunner : Node
{
    /// <summary>Process priority: after the player (-20) and its camera (-5), before the free camera and the draw.</summary>
    public const int Priority = -3;

    /// <summary>How long the character must have held RunForward before the running frame is taken (a stride or two, not the first step of a run).</summary>
    private const double RunHeldSeconds = 1.0;

    /// <summary>
    /// The speed (metres a second, over the last half second) the running frame needs: above a walk (3.12 m/s, plus the tick-sized jitter of a half-second
    /// measurement) and well below the stand-in's run (about 7.7).
    /// </summary>
    private const double RunningMetresPerSecond = 4.0;

    private const int SettleFrames = 30;
    private const double WaitTimeoutSeconds = 30.0;
    private const double SlowFrameMilliseconds = 50.0;
    private const int BlankGridX = 64;
    private const int BlankGridY = 36;
    private const int Shots = 4;

    /// <summary>The caption's bottom edge above the window's bottom: over the interiors' two status lines (the bottom-left, 64 px high), which stay visible in the capture.</summary>
    private const float CaptionBottom = -72;

    private readonly GameServices _services;
    private readonly PlayerController _player;
    private readonly CaptureReadiness _readiness;
    private readonly string _directory;
    private readonly Label _caption;
    private readonly IEnumerator<bool> _script;
    private readonly Queue<(double Time, CellFrame Position)> _trail = new();
    private Request? _pending;
    private int _captured;
    private int _failures;
    private int _frame;
    private double _maxFrameMs;
    private bool _skipNextFrame;
    private bool _measuring;
    private double _now;
    private bool _done;

    private sealed record Request(int Index, string File, string Name, string Caption);

    private PlayerFramesRunner(GameServices services, WorldRoot world, PlayerController player, string directory)
    {
        _services = services;
        _player = player;
        _readiness = new CaptureReadiness(services, world);
        _directory = directory;
        _script = Script().GetEnumerator();
        Name = "PlayerFramesRunner";
        ProcessPriority = Priority;
        var layer = new CanvasLayer { Name = "PlayerFramesCaption", Layer = 6 };
        _caption = new Label { Name = "Caption", MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
        _caption.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _caption.OffsetLeft = 24;
        _caption.OffsetRight = -24;
        _caption.AutowrapMode = TextServer.AutowrapMode.Word; // a long caption wraps (upward) instead of running off both sides of the window
        _caption.GrowVertical = Control.GrowDirection.Begin;
        _caption.OffsetBottom = CaptionBottom;
        _caption.AddThemeColorOverride("font_color", new Color(0.8f, 1f, 0.85f));
        _caption.AddThemeColorOverride("font_outline_color", Colors.Black);
        _caption.AddThemeConstantOverride("outline_size", 4);
        _caption.AddThemeFontSizeOverride("font_size", 15);
        _caption.Visible = false;
        layer.AddChild(_caption);
        AddChild(layer);
    }

    /// <summary>Installs the runner over the offline player; the frames go to <paramref name="directory"/> (absolute, or relative to the repository root).</summary>
    public static PlayerFramesRunner Install(Node main, GameServices services, WorldRoot world, PlayerController player, string directory)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var runner = new PlayerFramesRunner(services, world, player, directory);
        main.AddChild(runner);
        Log.Info("playerframes", "offline-player frames: four review frames, each taken when the state it is named for has been checked", ("directory", directory));
        return runner;
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_done)
        {
            return;
        }

        _now = _player.Controller.Now;
        Trail();
        _frame++;
        if (_pending is { } request)
        {
            _pending = null;
            Capture(request);
            _caption.Visible = false;
            _skipNextFrame = true;
        }
        else if (_measuring && !_skipNextFrame)
        {
            _maxFrameMs = Math.Max(_maxFrameMs, delta * 1000.0);
            if (delta * 1000.0 > SlowFrameMilliseconds)
            {
                _failures++;
                Log.Error("playerframes", "a frame took too long", ("ms", delta * 1000.0), ("limit_ms", SlowFrameMilliseconds), ("frame", _frame));
            }
        }
        else
        {
            _skipNextFrame = false;
        }

        if (!_script.MoveNext())
        {
            Finish();
        }
    }

    /// <summary>The whole run, one yield per frame.</summary>
    private IEnumerable<bool> Script()
    {
        var timeout = 0.0;

        // The world around the player is complete and the player is placed.
        var settled = 0;
        while (settled < SettleFrames)
        {
            settled = WorldReady() ? settled + 1 : 0;
            if ((timeout += GetProcessDeltaTime()) > 90.0)
            {
                Fail("the world around the player was not complete in 90 s: " + _readiness.Describe());
                yield break;
            }

            yield return true;
        }

        HideOverlays();
        var intents = _player.Controller;
        var home = _player.Position;
        _measuring = true; // the frames of the world's start-up are not the player's; every frame from here on is counted

        // 1. The chase camera behind the character running round Holtburg's ring (the clear route of the walk checks, the lifestone in the middle).
        var ring = RouteLibrary.HoltburgLaps(home, _player.Walker.World, _player.Controller.Player.Setup!, laps: 1);
        _player.Teleport(ring.Start!.Value);
        var driver = new RouteDriver(intents, ring, intents.Options.RunByDefault);
        timeout = 0;
        var model = _player.CameraModel;
        var pose = _player.Camera!.Pose;
        var running = false;
        var runningSince = double.NaN;
        while (!running)
        {
            driver.Tick(_now);
            pose = _player.Camera!.Pose;
            runningSince = _player.Walker.Motion.Interpreted.ForwardCommand == MotionCommands.RunForward ? (double.IsNaN(runningSince) ? _now : runningSince) : double.NaN;
            running = !double.IsNaN(runningSince) && _now - runningSince >= RunHeldSeconds && RunSpeed() >= RunningMetresPerSecond && _player.Walker.OnGround && !_player.Position.IsEnvCell && !model.InHead && pose.PlayerTranslucency <= 0f && !_player.Placing;
            if (running || Timed(ref timeout, "the character did not reach a run in the open"))
            {
                break;
            }

            yield return true;
        }

        if (running)
        {
            Ask(1, "01-chase-holtburg.png", "chase camera, running", string.Create(CultureInfo.InvariantCulture, $"frame 1 of {Shots}: chase camera behind the character, running (RunForward held {_now - runningSince:F1} s) round Holtburg's lifestone, {RunSpeed():F1} m/s over the last half second (stand-in character, placeholder look)"));
        }
        else
        {
            Fail($"frame 1 (chase camera, running): not running (RunForward for {RunHeldSeconds:F0} s at {RunningMetresPerSecond:F0} m/s or more) in the open with the chase camera and the body drawn (speed {RunSpeed():F1} m/s, forward command 0x{_player.Walker.Motion.Interpreted.ForwardCommand:X8}, on the ground {_player.Walker.OnGround}, cell 0x{_player.Position.CellId:X8})");
        }

        yield return true;

        // 2. First person at the door of the shop: the camera in the head, the body not drawn.
        _player.Teleport(Frame(new Vector3(91f, 36f, 0f), 90.0));
        driver.Tick(_now); // the route driver sees the jump of the teleport and lets go of every key
        model.SetInHead();
        timeout = 0;
        settled = 0;
        while (settled < SettleFrames)
        {
            settled = WorldReady() && _player.Camera!.Pose.InHead ? settled + 1 : 0;
            if (Timed(ref timeout, "first person: the world was not ready"))
            {
                break;
            }

            yield return true;
        }

        pose = _player.Camera!.Pose;
        if (model.InHead && pose.InHead && pose.PlayerTranslucency >= 1f && !_player.Position.IsEnvCell)
        {
            Ask(2, "02-first-person.png", "first person", $"frame 2 of {Shots}: first person, the camera in the character's head and the body not drawn, outside the Holtburg shop (placeholder character)");
        }
        else
        {
            Fail("frame 2 (first person): the camera is not in the head with the body hidden");
        }

        yield return true;
        model.SetDefaultOffsets();

        // 3. Inside a building: the player and the camera both in a room of the Holtburg shop.
        _player.Teleport(OfflinePlayerSetup.SpawnOf(_services, InteriorLocations.HoltburgShop.Location));
        timeout = 0;
        settled = 0;
        var seconds = 0.0;
        while (settled < SettleFrames || seconds < CaptureReadiness.SettleSeconds)
        {
            var ready = WorldReady() && _player.Camera!.Pose.Viewer.IsEnvCell;
            settled = ready ? settled + 1 : 0;
            seconds = ready ? seconds + GetProcessDeltaTime() : 0;
            if (Timed(ref timeout, "inside: the world around the shop was not ready"))
            {
                break;
            }

            yield return true;
        }

        pose = _player.Camera!.Pose;
        if (_player.Position.IsEnvCell && pose.Viewer.IsEnvCell && !model.InHead)
        {
            Ask(3, "03-inside-building.png", "inside a building", string.Create(CultureInfo.InvariantCulture, $"frame 3 of {Shots}: inside the Holtburg shop, the player in cell 0x{_player.Position.CellId:X8} and the chase camera in cell 0x{pose.Viewer.CellId:X8} (placeholder character)"));
        }
        else
        {
            Fail($"frame 3 (inside a building): the player is in cell 0x{_player.Position.CellId:X8} and the camera in cell 0x{pose.Viewer.CellId:X8}");
        }

        yield return true;

        // 4. Mid-jump on the plaza: off the ground and well above it.
        _player.Teleport(home);
        timeout = 0;
        settled = 0;
        while (settled < SettleFrames)
        {
            settled = WorldReady() && !_player.Position.IsEnvCell ? settled + 1 : 0;
            if (Timed(ref timeout, "jump: the world was not ready"))
            {
                break;
            }

            yield return true;
        }

        var ground = _player.Position.Origin.Z;
        intents.CommenceJump(_now);
        var charged = _now;
        while (_now - charged < 0.4)
        {
            yield return true;
        }

        intents.DoJump(_now);
        timeout = 0;
        var asked = false;
        while (true)
        {
            var rise = _player.Position.Origin.Z - ground;
            if (!_player.Walker.OnGround && rise >= 0.4)
            {
                Ask(4, "04-mid-jump.png", "mid-jump", string.Create(CultureInfo.InvariantCulture, $"frame 4 of {Shots}: mid-jump, {rise:F2} m above the ground and rising or falling (placeholder character)"));
                asked = true;
                break;
            }

            if (Timed(ref timeout, "the jump did not rise 0.4 m"))
            {
                break;
            }

            yield return true;
        }

        if (!asked)
        {
            Fail("frame 4 (mid-jump): the jump never rose 0.4 m");
        }

        yield return true;
    }

    /// <summary>Hides the position HUD and the F3 overlay (the main scene's <c>Hud</c> layer) for the capture; the player's keys, the stand-in label and the caption stay.</summary>
    private void HideOverlays()
    {
        if (GetParent()?.GetNodeOrNull<CanvasLayer>("Hud") is { Visible: true } hud)
        {
            hud.Visible = false;
            Log.Info("playerframes", "the position HUD and the performance overlay are hidden for the capture");
        }
    }

    private bool WorldReady() => !_player.Placing && _readiness.IsComplete;

    /// <summary>How fast the player has moved over the last half second (metres a second): the distance between where it was and where it is.</summary>
    private double RunSpeed()
    {
        if (_trail.Count < 2)
        {
            return 0.0;
        }

        var (time, position) = _trail.Peek();
        var seconds = _now - time;
        return seconds < 0.2 ? 0.0 : _player.Position.GetOffset(position).Length() / seconds;
    }

    /// <summary>Keeps the player's positions of the last half second (the speed is measured from the oldest).</summary>
    private void Trail()
    {
        _trail.Enqueue((_now, _player.Position));
        while (_trail.Count > 2 && _now - _trail.Peek().Time > 0.5)
        {
            _trail.Dequeue();
        }
    }

    /// <summary>Adds the frame's time to <paramref name="timeout"/>; true (and a failure) when the wait is over the limit.</summary>
    private bool Timed(ref double timeout, string what)
    {
        timeout += GetProcessDeltaTime();
        if (timeout <= WaitTimeoutSeconds)
        {
            return false;
        }

        Fail($"{what} (waited {WaitTimeoutSeconds:F0} s)");
        return true;
    }

    /// <summary>The outdoor place at landblock-local <paramref name="landblockLocal"/> (x and y) in Holtburg, on the terrain, facing <paramref name="heading"/> degrees.</summary>
    private CellFrame Frame(Vector3 landblockLocal, double heading)
    {
        var place = BuildingRoutes.OutdoorPoint(_player.Walker.World, LandblockCoord.Holtburg, landblockLocal);
        return new CellFrame(place.CellId, new AcFrame(place.Origin, PositionFrames.WithHeading(System.Numerics.Quaternion.Identity, (float)heading)));
    }

    private void Ask(int index, string file, string name, string caption)
    {
        _caption.Text = caption;
        _caption.Visible = true;
        _pending = new Request(index, file, name, caption);
    }

    private void Fail(string problem)
    {
        _failures++;
        Log.Error("playerframes", "player frames: " + problem);
    }

    private void Capture(Request request)
    {
        var path = System.IO.Path.Combine(_directory, request.File);
        try
        {
            using var image = GetViewport().GetTexture().GetImage();
            if (image is null || image.IsEmpty())
            {
                Fail($"the '{request.Name}' frame is empty");
                return;
            }

            var lit = LitSamples(image);
            if (lit < BlankGridX * BlankGridY / 20)
            {
                Fail($"the '{request.Name}' frame is blank ({lit} of {BlankGridX * BlankGridY} samples differ from the corner)");
                return;
            }

            if (!ScreenshotWriter.Save(GetViewport(), path, "playerframes", ("frame", request.Name), ("caption", request.Caption), ("cell", $"0x{_player.Position.CellId:X8}")))
            {
                Fail($"the '{request.Name}' frame was not saved");
                return;
            }

            _captured++;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException)
        {
            Fail($"the '{request.Name}' frame failed: {ex.Message}");
        }
    }

    /// <summary>How many of a coarse grid of samples differ from the top-left pixel (a blank frame has none).</summary>
    private static int LitSamples(Image image)
    {
        var width = image.GetWidth();
        var height = image.GetHeight();
        var corner = image.GetPixel(0, 0);
        var lit = 0;
        for (var gy = 0; gy < BlankGridY; gy++)
        {
            for (var gx = 0; gx < BlankGridX; gx++)
            {
                var pixel = image.GetPixel(Math.Min(width - 1, gx * width / BlankGridX), Math.Min(height - 1, gy * height / BlankGridY));
                if (!pixel.IsEqualApprox(corner))
                {
                    lit++;
                }
            }
        }

        return lit;
    }

    private void Finish()
    {
        _done = true;
        var ok = _captured == Shots && _failures == 0;
        Log.Info(
            "playerframes",
            string.Create(CultureInfo.InvariantCulture, $"player frames done captured={_captured} failures={_failures} max_frame_ms={_maxFrameMs:F1}"),
            ("directory", _directory));
        Log.FlushToGodot();
        GetTree().Quit(ok ? 0 : 1);
    }
}
