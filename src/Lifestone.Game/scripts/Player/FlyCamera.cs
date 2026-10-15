using Godot;
using Lifestone.Client.Navigation;
using Lifestone.Client.Streaming;
using Lifestone.Client.World;
using Lifestone.Game.Core;
using Lifestone.Game.World;

namespace Lifestone.Game.Player;

/// <summary>
/// The M1 camera: free flight and a terrain-following walk mode. Its true position is a double-precision
/// <see cref="GlobalPosition"/> (<see cref="WorldPosition"/>); the node's render position is recomputed from it and the
/// world's floating origin every frame, so it never drifts. It is the world's streaming focus.
/// </summary>
/// <remarks>
/// <para><b>Controls.</b> WASD move, Q/E down/up (fly), Shift fast (x5), Ctrl slow (x0.2), mouse wheel changes the base
/// speed (x1.25 per notch), right mouse button held = mouse look, Tab toggles mouse capture, Esc releases it, F toggles
/// walk mode.</para>
/// <para><b>Walk mode</b> keeps the eye <see cref="EyeHeight"/> above the exact retail ground height
/// (<see cref="GroundHeights"/>, i.e. <c>TerrainSampler</c>) and moves horizontally at <see cref="WalkSpeed"/>. No
/// collision yet (M1). Retail's first-person camera sits 0.18 m in front of the player's head pivot
/// (<c>CameraSet::InHead</c> compares the camera offset with (0, 0.18, 0), acclient.c:148094); the head's height comes
/// from the player's Setup and animation, which M1 does not load, so the eye height is a 1.7 m placeholder.
/// TODO(M2): derive it from the human Setup's head part.</para>
/// <para><b>Fly mode outdoors</b> keeps the eye at least <see cref="FlyClearance"/> above the exact ground
/// (<see cref="FlyGroundClearance"/>, with the interiors' cell tracker as <see cref="ViewerCell"/>): terrain is drawn
/// back-face culled, as retail draws it, so a camera under the ground would see the sky through it. Retail has no free
/// camera; its player and camera never go below the terrain (physics), so this is the M1 stand-in for that, not retail
/// behaviour. Steps that stay in EnvCells are never clamped; after leaving a dungeon cell (not seen_outside) or a cellar
/// (a cell under the ground) through a wall or a floor the eye is not pushed up out of the ground, so cells below the
/// ground stay reachable; from a building interior it stops on the floor; an explicit placement under the ground
/// outdoors is lifted on the next step.</para>
/// </remarks>
public partial class FlyCamera : Node3D, IStreamingFocusSource
{
    /// <summary>Placeholder eye height above the ground in walk mode (see remarks).</summary>
    public const float EyeHeight = 1.7f;

    /// <summary>
    /// Walk speed: ACE <c>MotionInterp.RunAnimSpeed</c> = 4.0 m/s, the run animation's speed at run rate 1 (ACE
    /// v1.78.4816 Source/ACE.Server/Physics/Animation/MotionInterp.cs). A real character runs faster (run rate from the
    /// Run skill); Shift multiplies it as in fly mode.
    /// </summary>
    public const float WalkSpeed = 4.0f;

    /// <summary>Fly mode outdoors: the eye's minimum height above the ground (m; see the remarks).</summary>
    public const float FlyClearance = (float)FlyGroundClearance.Clearance;

    private const double MouseSensitivity = 0.15; // degrees per pixel
    private const double MaxPitch = 89.0;
    private const double MinSpeed = 1.0;
    private const double MaxSpeed = 5000.0;

    private readonly FlyGroundClearance _clearance = new();
    private WorldRoot? _world;
    private GroundHeights? _ground;
    private bool _captureToggled;
    private bool _looking;

    /// <summary>The camera itself (a child, identity transform).</summary>
    public Camera3D Camera { get; private set; } = null!;

    /// <summary>The true position (global metres, AC axes).</summary>
    public GlobalPosition WorldPosition { get; set; }

    /// <summary>Compass heading in degrees (0 = north, 90 = east).</summary>
    public double Heading { get; set; }

    /// <summary>Pitch in degrees (positive looks up).</summary>
    public double Pitch { get; set; }

    /// <summary>Fly speed in m/s before modifiers.</summary>
    public double BaseSpeed { get; set; } = 40.0;

    public bool WalkMode { get; set; }

    private bool _inputEnabled = true;

    /// <summary>
    /// False while something else (the fly-path runner, a text field that has focus) drives the camera or holds its input. Reads
    /// the effective state: also false while <see cref="InputSuspended"/> or <see cref="InputHeldByRetailUi"/>. Writes set the owner's
    /// own flag, which a suspension or the retail UI's hold does not disturb.
    /// </summary>
    public bool InputEnabled
    {
        get => _inputEnabled && !InputSuspended && !InputHeldByRetailUi;
        set => _inputEnabled = value;
    }

    /// <summary>
    /// True while a retail UI text field has the keyboard (<c>RetailUiLayer.KeyboardCapturedChanged</c>): the retail UI's own hold, apart
    /// from <see cref="InputEnabled"/>'s writers (the teleport panel's typing flag, the tools' runners) and <see cref="InputSuspended"/>,
    /// so releasing it never releases another owner's hold.
    /// </summary>
    public bool InputHeldByRetailUi { get; set; }

    /// <summary>
    /// True while portal space is busy (retail's <c>teleportInProgress</c>): the camera ignores the keys and the mouse, so the hidden
    /// camera cannot fly and move the readiness destination under the wait. Independent of <see cref="InputEnabled"/>'s writers (the
    /// teleport panel's typing flag), so neither releases the other's hold.
    /// </summary>
    public bool InputSuspended { get; set; }

    /// <summary>
    /// True while the chase camera (<c>ChaseCamera</c>, M2 G5) drives this camera: the keys, the mouse and the walk-mode clamp of M1's free flight do
    /// nothing, and the camera's pose is whatever the driver sets. <see cref="InputEnabled"/> is left alone, so the panels' typing flag and the other
    /// tools' keys (T, F3, F7, F9) work as in M1.
    /// </summary>
    public bool Driven { get; set; }

    /// <summary>
    /// Play mode (<c>--play</c>, M2 G6; owner decision D41): M1's free-camera keys do nothing, as before there is a player to drive the camera. Tab (mouse
    /// capture), Escape and F (walk mode) are dropped with the flight keys and the mouse look; the tool keys that belong to other nodes (F1, F3) stay.
    /// </summary>
    public bool Locked { get; set; }

    /// <summary>Speed actually used in the last frame (m/s).</summary>
    public double CurrentSpeed { get; private set; }

    public StreamingFocus StreamingFocus => new(WorldPosition, StreamingFocusKind.Outdoor);

    /// <summary>The viewer's cell from the interiors' tracker; null without interiors (always outdoors).</summary>
    public IViewerCell? ViewerCell { get; set; }

    /// <summary>True while the viewer is in an EnvCell (a building interior or a dungeon).</summary>
    public bool IsIndoors => ViewerCell is { } cell && AcCoordinates.IsEnvCell(cell.CellId);

    /// <summary>Connects the camera to the world (call before the first frame).</summary>
    public void Initialize(WorldRoot world, GroundHeights? ground)
    {
        _world = world;
        _ground = ground;
        world.FocusSource = this;
        SyncToOrigin();
    }

    /// <summary>Places the camera (e.g. at a start location) facing <paramref name="heading"/>.</summary>
    public void PlaceAt(GlobalPosition position, double heading, double pitch)
    {
        if (position != WorldPosition)
        {
            _clearance.Placed(); // somewhere new: not "around the cells" any more (an outdoor spot under the ground is lifted)
        }

        WorldPosition = position;
        Heading = Wrap360(heading);
        Pitch = Math.Clamp(pitch, -MaxPitch, MaxPitch);
        SyncToOrigin();
    }

    public void SyncToOrigin()
    {
        if (_world is null)
        {
            return;
        }

        Position = _world.ToRender(WorldPosition);
        Quaternion = AcToGodot.CameraRotation(Heading, Pitch);
    }

    public override void _Ready()
    {
        ProcessPriority = 0;
        Camera = GetNodeOrNull<Camera3D>("Camera") ?? new Camera3D { Name = "Camera" };
        if (Camera.GetParent() is null)
        {
            AddChild(Camera);
        }

        Camera.Fov = (float)(GameServices.Instance.Options.Fov ?? GameOptions.DefaultFov); // --fov (default 60)
        Camera.Near = 0.1f;
        Camera.Far = 6000f;
        Camera.Current = true;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Driven || Locked)
        {
            return;
        }

        if (!InputEnabled)
        {
            if (_looking && @event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false })
            {
                _looking = false; // a mouse-look that was held when input stopped must not outlast the button
                UpdateCapture();
            }

            return;
        }

        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } rmb:
                _looking = rmb.Pressed;
                UpdateCapture();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                BaseSpeed = Math.Min(MaxSpeed, BaseSpeed * 1.25);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                BaseSpeed = Math.Max(MinSpeed, BaseSpeed / 1.25);
                break;
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                Heading = Wrap360(Heading + (motion.Relative.X * MouseSensitivity));
                Pitch = Math.Clamp(Pitch - (motion.Relative.Y * MouseSensitivity), -MaxPitch, MaxPitch);
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                switch (key.PhysicalKeycode)
                {
                    case Key.Tab:
                        _captureToggled = !_captureToggled;
                        UpdateCapture();
                        break;
                    case Key.Escape:
                        _captureToggled = false;
                        _looking = false;
                        UpdateCapture();
                        break;
                    case Key.F:
                        WalkMode = !WalkMode;
                        Log.Info("camera", WalkMode ? "walk mode" : "fly mode");
                        break;
                }

                break;
        }
    }

    public override void _Process(double delta)
    {
        if (InputEnabled && !Driven && !Locked)
        {
            Move(delta);
        }

        SyncToOrigin();
    }

    private void Move(double delta)
    {
        var forwardInput = Axis(Key.W, Key.S);
        var rightInput = Axis(Key.D, Key.A);
        var upInput = WalkMode ? 0.0 : Axis(Key.E, Key.Q);
        var multiplier = Input.IsPhysicalKeyPressed(Key.Shift) ? 5.0 : Input.IsPhysicalKeyPressed(Key.Ctrl) ? 0.2 : 1.0;
        var speed = (WalkMode ? WalkSpeed : BaseSpeed) * multiplier;
        CurrentSpeed = 0.0;

        var h = Heading * Math.PI / 180.0;
        var p = WalkMode ? 0.0 : Pitch * Math.PI / 180.0;
        var forward = (X: Math.Sin(h) * Math.Cos(p), Y: Math.Cos(h) * Math.Cos(p), Z: Math.Sin(p));
        var right = (X: Math.Cos(h), Y: -Math.Sin(h));
        var dx = (forward.X * forwardInput) + (right.X * rightInput);
        var dy = (forward.Y * forwardInput) + (right.Y * rightInput);
        var dz = (forward.Z * forwardInput) + upInput;
        var length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        var position = WorldPosition;
        if (length > 1e-9)
        {
            var step = speed * delta / length;
            position = new GlobalPosition(position.X + (dx * step), position.Y + (dy * step), position.Z + (dz * step));
            CurrentSpeed = speed;
        }

        // Stay on the map (landblocks 0x00-0xFE).
        const double edge = AcCoordinates.LandblocksPerSide * AcCoordinates.LandblockLength;
        position = position with { X = Math.Clamp(position.X, 0.0, edge - 0.001), Y = Math.Clamp(position.Y, 0.0, edge - 0.001) };
        if (WalkMode && _ground is not null && _ground.TryGetHeight(position, out var ground))
        {
            position = position with { Z = ground + EyeHeight };
        }
        else if (!WalkMode && _ground is not null)
        {
            // Outdoors the free camera stays above the terrain; cells below the ground stay reachable (class remarks).
            double? groundFrom = _ground.TryGetHeight(WorldPosition, out var g0) ? g0 : null;
            double? groundTo = _ground.TryGetHeight(position, out var g1) ? g1 : null;
            position = position with { Z = _clearance.Apply(WorldPosition, position, groundFrom, groundTo, ViewerCell) };
        }

        WorldPosition = position;
    }

    private void UpdateCapture() =>
        Input.MouseMode = _looking || _captureToggled ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;

    private static double Axis(Key positive, Key negative) =>
        (Input.IsPhysicalKeyPressed(positive) ? 1.0 : 0.0) - (Input.IsPhysicalKeyPressed(negative) ? 1.0 : 0.0);

    private static double Wrap360(double degrees)
    {
        degrees %= 360.0;

        // "+ 0.0" turns -0 (a heading of -2 atan2(0, 1)) into 0, so readouts never show "-0".
        return degrees < 0 ? degrees + 360.0 : degrees + 0.0;
    }
}
