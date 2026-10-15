using System.Diagnostics;
using System.Numerics;
using Vector3 = System.Numerics.Vector3;
using Godot;
using Lifestone.Client.Avatars;
using Lifestone.Client.Camera;
using Lifestone.Client.Movement;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;
using Lifestone.Game.Core;
using Lifestone.Game.World.Interiors;

namespace Lifestone.Game.Player;

/// <summary>
/// The player's camera (M2 G5, process priority -5: after the player controller has stepped and posed the character, before the free camera and the
/// interiors read the camera): retail's chase camera (<see cref="ChaseCameraModel"/>) or, from the player's head, first person. It places the
/// <see cref="FlyCamera"/> node, which stops obeying M1's keys (<see cref="FlyCamera.Driven"/>), and tells the interiors which cell the camera is in.
/// </summary>
/// <remarks>
/// <para><b>The sweep.</b> The camera never goes through walls or floors: each frame the viewer is moved from the pivot (the player's head) to where
/// the smoothed camera wants to be by <see cref="PhysicsWorld.SweepViewer"/>, <c>SmartBox::update_viewer</c>'s transition with a viewer sphere of
/// <see cref="ViewerRadius"/> (retail's 0.3 m); the cell the sweep ends in is the viewer's cell for the interiors
/// (<see cref="InteriorController.SetViewer"/>). With no collision attached around the player the camera is not swept.</para>
/// <para><b>The pose</b> follows the player as drawn (<c>PlayerController.DrawPosition</c>, interpolated between physics steps, D26), so the character
/// does not shake against the view. While the player waits for its landblock the camera stays where it was.</para>
/// <para><b>First person</b> hides the body (<see cref="FirstPersonView"/>); the first-person turn (the mouse look and the camera keys turn the player,
/// not the camera) is carried out with the turn commands. TODO(g5-first-person-turn): retail sets the heading in 8-degree steps for the keys; Lifestone
/// holds the turn command at its first speed while the requests last.</para>
/// </remarks>
public sealed partial class ChaseCamera : Node
{
    /// <summary>The viewer sphere's radius of the camera sweep (retail's <c>SmartBox::update_viewer</c> uses 0.3 m).</summary>
    public const float ViewerRadius = 0.3f;

    /// <summary>Frames without a first-person turn request before the held turn command is released.</summary>
    public const int SteerGraceFrames = 2;

    private readonly FlyCamera _camera;
    private readonly PlayerController _player;
    private readonly InteriorController? _interiors;
    private readonly PhysicsWorld _world;
    private readonly FirstPersonView _firstPerson;
    private readonly ViewerSweep _sweep;
    private uint _steerCommand;
    private bool _steering;
    private bool _wheelLogged;
    private int _steerIdleFrames;
    private long _blockedFrames;
    private long _frames;
    private double _lastMs;
    private double _maxMs;

    public ChaseCamera(FlyCamera camera, PlayerController player, ChaseCameraModel model, PhysicsWorld world, InteriorController? interiors)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(world);
        _camera = camera;
        _player = player;
        Model = model;
        _world = world;
        _interiors = interiors;
        _firstPerson = new FirstPersonView(player.Avatar);
        _sweep = Sweep;
        Name = "ChaseCamera";
        ProcessPriority = -5;
        model.SteerRequested += OnSteer;
        model.Unbuilt += message => Log.Warn("camera", message);
    }

    /// <summary>The camera model (retail's <c>CameraSet</c> and <c>CameraManager</c>).</summary>
    public ChaseCameraModel Model { get; }

    /// <summary>The last frame's pose.</summary>
    public CameraPose Pose { get; private set; }

    /// <summary>True when the last frame's sweep stopped the camera short.</summary>
    public bool Blocked => Pose.Blocked;

    /// <summary>The camera's cell after the sweep.</summary>
    public uint ViewerCell => Pose.Viewer.CellId;

    /// <summary>Frames the sweep stopped the camera.</summary>
    public long BlockedFrames => _blockedFrames;

    /// <summary>The slowest frame of this node (the sweep included) in milliseconds.</summary>
    public double MaxMilliseconds => _maxMs;

    /// <inheritdoc/>
    public override void _EnterTree() => _camera.Driven = true;

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        _camera.Driven = false;
        Log.Info("camera", "chase camera frames", ("frames", _frames), ("blocked", _blockedFrames), ("max_ms", _maxMs));
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        var started = Stopwatch.GetTimestamp();
        var now = Time.GetTicksUsec() / 1_000_000.0;
        if (_player.Placing)
        {
            // The collision around the player is still building (at the start, or after a teleport): the camera stands behind the place it will be, unswept.
            Place(Model.Update(now, _player.Position, sweep: null));
            return;
        }

        Place(Model.Update(now, _player.DrawPosition, _sweep));
        _lastMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _maxMs = Math.Max(_maxMs, _lastMs);
    }

    /// <summary>
    /// The mouse wheel zooms when nothing else took it (the retail UI's controls and the panels see it first). TODO(g5-wheel-zoom): retail's route from the
    /// wheel to <c>CameraSet::Closer</c> and <c>Farther</c> is not read (the camera map binds no wheel); a notch zooms by 10 percent.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel || !(_player.InputAllowed?.Invoke() ?? true))
        {
            return;
        }

        if (!_wheelLogged)
        {
            _wheelLogged = true;
            Log.Warn("camera", "TODO(g5-wheel-zoom): retail's route from the mouse wheel to the camera zoom is not read; a notch zooms by 10 percent");
        }

        var now = PlayerController.Clock();
        var notches = Math.Max(1, (int)Math.Round(wheel.Factor));
        for (var i = 0; i < notches; i++)
        {
            if (wheel.ButtonIndex == MouseButton.WheelUp)
            {
                Model.Closer(now, keepGoing: false, modOverride: 2f); // 1 - 0.2 / 2
            }
            else
            {
                Model.Farther(now, keepGoing: false, modOverride: 0.5f); // 1 + 0.2 * 0.5
            }
        }

        GetViewport().SetInputAsHandled();
    }

    /// <summary>The camera must be placed again without smoothing (the player was teleported).</summary>
    public void Snap() => Model.Snap();

    /// <summary>The camera's rows of the performance overlay.</summary>
    public string Describe()
    {
        var offset = Model.ViewerOffset;
        var pose = Pose;
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"camera: {(Model.InHead ? "first person" : "chase")}   offset ({offset.X:0.00}, {offset.Y:0.00}, {offset.Z:0.00})   {(pose.Blocked ? "BLOCKED by collision" : "clear")}   cell 0x{pose.Viewer.CellId:X8}   sweeps stopped {_blockedFrames}   {_lastMs:0.00} ms (max {_maxMs:0.00})");
    }

    private void Place(in CameraPose pose)
    {
        Pose = pose;
        _frames++;
        if (pose.Blocked)
        {
            _blockedFrames++;
        }

        var viewer = pose.Viewer;
        var eye = viewer.ToGlobal();
        var forward = Vector3.Transform(Vector3.UnitY, viewer.Orientation);
        _camera.WorldPosition = eye;
        _camera.Heading = AcCoordinates.HeadingOf(forward.X, forward.Y);
        _camera.Pitch = Math.Clamp(Math.Asin(Math.Clamp(forward.Z, -1f, 1f)) * (180.0 / Math.PI), -89.0, 89.0);
        _camera.SyncToOrigin();
        _interiors?.SetViewer(viewer.CellId, eye);
        _firstPerson.Apply(pose);

        // The first-person turn holds a turn command while the camera keeps asking for it, and lets go when it does not.
        if (_steering && !Model.SteeringRequested)
        {
            // The mouse reports on some frames only; a short gap does not let the turn go (each start and stop is a MoveToState).
            if (++_steerIdleFrames > SteerGraceFrames)
            {
                _player.Controller.HandleCommand(_steerCommand, false);
                _steering = false;
            }
        }
        else
        {
            _steerIdleFrames = 0;
        }

        Model.EndFrame();
    }

    private void OnSteer(bool clockwise, float speed)
    {
        var command = clockwise ? MotionCommands.TurnRight : MotionCommands.TurnLeft;
        if (_steering && _steerCommand == command)
        {
            return;
        }

        if (_steering)
        {
            _player.Controller.HandleCommand(_steerCommand, false);
        }

        _steering = true;
        _steerCommand = command;
        _player.Controller.HandleCommand(command, true, speed);
    }

    private ViewerSweepResult Sweep(in CellFrame from, in CellFrame to)
    {
        if (_world.GetCell(from.CellId) is null)
        {
            return new ViewerSweepResult(to, Blocked: false); // no collision is attached here: the camera is not swept
        }

        return _world.SweepViewer(from, to, ViewerRadius);
    }
}
