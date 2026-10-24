using System.Numerics;
using Lifestone.Client.Effects;
using Lifestone.Client.Play;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Camera;

/// <summary>Where a frame's camera is, as <see cref="ChaseCameraModel.Update"/> finds it.</summary>
/// <param name="Viewer">The viewer after the camera sweep: the position the world is drawn from, and the cell it is in.</param>
/// <param name="Desired">Where the smoothed camera wanted to be before the sweep.</param>
/// <param name="Pivot">The point the camera orbits: the player's position plus the pivot offset (<see cref="ChaseCameraModel.DefaultPivotZ"/>).</param>
/// <param name="PlayerTranslucency">How translucent retail draws the player this frame (0 solid, 1 not drawn): 1 in first person, a fade when the camera is closer than <see cref="ChaseCameraModel.FadeStartMetres"/>.</param>
/// <param name="InHead">The camera is in the player's head (first person).</param>
/// <param name="Blocked">The sweep stopped the camera short of where it wanted to be.</param>
public readonly record struct CameraPose(CellFrame Viewer, CellFrame Desired, CellFrame Pivot, float PlayerTranslucency, bool InHead, bool Blocked);

/// <summary>The camera sweep (<c>PhysicsWorld.SweepViewer</c>): from the pivot to where the camera wants to be, with the viewer's radius.</summary>
public delegate ViewerSweepResult ViewerSweep(in CellFrame from, in CellFrame to);

/// <summary>Which way the camera looks (<c>CameraTarget</c>, 11.4186 CameraManager.c).</summary>
[Flags]
public enum CameraTargets
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Along the camera's direction in the pivot object's frame (<c>LOOK_IN_DIRECTION</c>).</summary>
    LookInDirection = 1,

    /// <summary>At a target object (<c>LOOK_AT_OBJECT</c>); not built.</summary>
    LookAtObject = 2,

    /// <summary>At the pivot (<c>LOOK_AT_PIVOT</c>).</summary>
    LookAtPivot = 4,

    /// <summary>Along the slope of the ground (<c>ALIGN_WITH_PLANE</c>); not built, see <see cref="ChaseCameraModel"/>.</summary>
    AlignWithPlane = 0x10,
}

/// <summary>
/// The chase camera of retail (M2 G5): a viewer offset from a pivot on the player, orbited, zoomed and raised by the camera keys and the mouse,
/// and smoothed toward where it wants to be. It re-expresses <c>CameraSet</c> and the viewer part of <c>CameraManager</c>
/// (11.4186 PORTAL/smartbox/CameraSet.c and CameraManager.c; nothing is copied) with no engine and no clock of its own: the host gives it the time.
/// </summary>
/// <remarks>
/// <para><b>Retail functions</b> (11.4186 / 11.6096): <c>SetDefaultOffsets</c> 0x00458F80 / 0x004590A0, <c>Closer</c> 0x004586D0 / 0x004587F0,
/// <c>Farther</c> 0x00458890 / 0x004589B0, <c>Rotate</c> 0x00458310 / 0x00458430, <c>Raise</c> 0x00457980 / 0x00457AA0, <c>Lower</c> 0x00457CF0 / 0x00457E10,
/// <c>MouseLookHandler</c> 0x00458D80 / 0x00458EA0, <c>SetInHead</c> 0x00458CE0 / 0x00458E00, <c>UpdateCamera</c> 0x00458AE0 / 0x00458C00 and
/// <c>CameraManager::UpdateCamera</c> 0x00456660 / 0x00456780.</para>
/// <para><b>The geometry.</b> The pivot is the player's frame applied to the pivot offset (0, 0, <see cref="DefaultPivotZ"/>); the viewer offset is
/// in a frame at the pivot facing the camera's direction (the player's heading, or the pitched direction of the first-person view); the camera
/// looks along that direction, or at the pivot once it is raised, lowered or orbited far enough (<c>SetTargetForOffset</c>). The default offset is
/// (0, -2.5, 0.75) behind and above the pivot; first person is (0, 0.18, 0), 18 cm in front of the pivot. Each frame the position moves a fraction
/// of the way to where it wants to be (<see cref="Stiffness"/> * dt * 10), the orientation likewise, and a first-person camera follows the
/// position at once.</para>
/// <para><b>Constants</b> are the literals of the functions, and three read from the exe (11.6096): <c>CAMERA_DEFAULT_PIVOT_Z</c> 1.5 at 0x0079CC2C,
/// the factors -2.5 and 0.75 at 0x0079CC24 and 0x0079CC28, and the rotation step <c>angle</c> (8 degrees: the static initialiser at 0x006EBAC0 sets it to
/// pi / (180 / 8)); <c>CAMERA_MOUSELOOK_LIMIT</c> is 0.8 at 0x0079CC08. The camera adjustment speed (41) and the stiffness (0.35) are the
/// <c>CameraManager</c> constructor's.</para>
/// <para><b>Not built</b> (each is reported once through <see cref="Unbuilt"/>): the overhead view (<c>LookDown</c>, key 0x3B), the map view
/// (<c>SetMapMode</c>, 0x3C), tracking a target, the camera's alignment to the slope of the ground (<c>ALIGN_WITH_PLANE</c> in
/// <c>CameraManager::UpdateCamera</c>: the camera looks level along the player's heading), and the offset debug movements. The preferences of the
/// mouse look (sensitivity, smoothing, inversion, mouse turning) are Lifestone's placeholders (<see cref="MouseLookSensitivity"/>), reported once at the first mouse
/// look (<c>TODO(g5-mouselook-prefs)</c>), as is the first-person turn with the turn commands at the first turn request (<c>TODO(g5-first-person-turn)</c>).</para>
/// <para>Main thread only; <see cref="Update"/> allocates nothing.</para>
/// </remarks>
public sealed class ChaseCameraModel
{
    /// <summary><c>CAMERA_DEFAULT_PIVOT_Z</c> (11.6096 0x0079CC2C): the pivot is 1.5 m above the player's origin.</summary>
    public const float DefaultPivotZ = 1.5f;

    /// <summary><c>SetDefaultOffsets</c>: the viewer offset is (0, -2.5 * scale, 0.75 * scale); -2.5 is at 11.6096 0x0079CC24.</summary>
    public const float DefaultOffsetYFactor = -2.5f;

    /// <summary>0.75, at 11.6096 0x0079CC28.</summary>
    public const float DefaultOffsetZFactor = 0.75f;

    /// <summary>First person: the viewer offset is (0, 0.18, 0) (<c>CameraSet::InHead</c> compares it exactly).</summary>
    public const float InHeadY = 0.18f;

    /// <summary>The <c>CameraManager</c> constructor's <c>m_rCameraAdjustmentSpeed</c> (bits 0x42240000).</summary>
    public const float AdjustmentSpeed = 41f;

    /// <summary>The constructor's translational and rotational stiffness (bits 0x3EB33333).</summary>
    public const float Stiffness = 0.35f;

    /// <summary>The rotation step <c>angle</c>: 8 degrees in radians (set by the initialiser at 11.6096 0x006EBAC0).</summary>
    public const float AngleRadians = MathF.PI / 22.5f;

    /// <summary><c>CAMERA_MOUSELOOK_LIMIT</c> (11.6096 0x0079CC08): the first-person view's pitch component stays within +-0.8.</summary>
    public const float MouseLookLimit = 0.8f;

    /// <summary>The player starts to fade when the viewer is closer than this to the pivot (<c>UpdateCamera</c>).</summary>
    public const float FadeStartMetres = 0.45f;

    /// <summary>The player is not drawn at all when the viewer is this close.</summary>
    public const float FadeEndMetres = 0.2f;

    private const float MinStepSeconds = 0.0002f;
    private const float ZoomRate = 0.2f;
    private const float MinOffsetLength = 0.5f;
    private const float MaxFartherAxis = 10f;
    private const float MaxFartherZ = 450f;
    private const float LowestStiffnessZ = -1.8f;
    private const float FirstPersonPitchStep = 0.2f;
    private const float PitchHorizontalLimit = 1.2f;
    private const int MouseLookDeadEvents = 5;
    private const float MouseLookScale = 0.06666667f;
    private const float MouseTurnSpeedCap = 1.5f;
    private const float MouseTurnDeadZone = 0.02f;
    private const double MouseFilterSeconds = 0.25;

    private static readonly Vector3 OutOfHeadOffset = new(0f, -1.2f, 0.5f);

    private double _lastRotate;
    private double _lastRaiseOrLower;
    private double _lastZoom;
    private double _lastUpdate;
    private double _lastFilter;
    private float _lastDx;
    private float _lastDy;
    private bool _rotLeft;
    private bool _rotRight;
    private bool _raise;
    private bool _lower;
    private bool _closer;
    private bool _farther;
    private int _lookHorizontalRun;
    private int _lookVerticalRun;
    private CellFrame _viewer;
    private bool _hasViewer;
    private bool _steering;
    private bool _reportedLookDown;
    private bool _reportedMapMode;
    private bool _reportedFirstPersonTurn;
    private bool _reportedMouseLookPrefs;
    private bool _reportedSlope;

    /// <summary>A camera at the default offsets, as <c>CameraSet</c>'s constructor leaves it.</summary>
    public ChaseCameraModel()
    {
        SetDefaultOffsets();
    }

    /// <summary>The viewer offset, in the pivot frame (<c>CameraManager::viewer_offset</c>).</summary>
    public Vector3 ViewerOffset { get; private set; }

    /// <summary>The pivot offset, in the player's frame (<c>CameraManager::pivot_offset</c>).</summary>
    public Vector3 PivotOffset { get; private set; } = new(0f, 0f, DefaultPivotZ);

    /// <summary>The camera's direction in the player's frame (<c>CameraManager::direction</c>): (0, 1, 0), or pitched in first person.</summary>
    public Vector3 Direction { get; private set; } = Vector3.UnitY;

    /// <summary>Where the camera looks (<c>CameraManager::target_status</c>).</summary>
    public CameraTargets Targets { get; private set; } = CameraTargets.LookInDirection;

    /// <summary>The zoom scale (<c>CameraManager::scale</c>, 1).</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>The stiffness of the position (1 follows at once).</summary>
    public float TranslationalStiffness { get; private set; } = Stiffness;

    /// <summary>The stiffness of the orientation.</summary>
    public float RotationalStiffness { get; private set; } = Stiffness;

    /// <summary>The stiffness the camera returns to after a zoom or a rotation (<c>CameraSet::current_stiffness</c>).</summary>
    public float CurrentStiffness { get; private set; } = Stiffness;

    /// <summary>True in first person: the viewer offset is exactly (0, 0.18, 0) (<c>CameraSet::InHead</c>).</summary>
    public bool InHead => ViewerOffset.X == 0f && ViewerOffset.Y == InHeadY && ViewerOffset.Z == 0f;

    /// <summary>True while the mouse look is on (<c>mouselook_active</c>).</summary>
    public bool MouseLookActive { get; private set; }

    /// <summary>
    /// <c>CInputManager::m_UseMouseTurning</c>: the mouse look turns the player (through the turn commands) instead of orbiting the camera. Retail's
    /// default and preference are not read: Lifestone orbits out of first person and steers in it.
    /// </summary>
    public bool MouseTurning { get; set; }

    /// <summary>
    /// <c>m_MouseLookSensitivity</c>. TODO(g5-mouselook-prefs): retail's default is not read; 1.
    /// </summary>
    public float MouseLookSensitivity { get; set; } = 1f;

    /// <summary><c>m_MouseLookSmoothingAmount</c>, 0 to 1 (0: none). TODO(g5-mouselook-prefs): retail's default is not read.</summary>
    public float MouseLookSmoothing { get; set; }

    /// <summary><c>m_InvertMouseLookYAxis</c>. TODO(g5-mouselook-prefs): retail's default is not read.</summary>
    public bool InvertMouseLookY { get; set; }

    /// <summary>True for <c>--retail-camera-still</c>: retail's fixed stall step per frame (CameraManager::UpdateCamera, 11.6096 0x00456780), whose dead zone grows with the frame rate. Default false: the step is scaled by the frame time.</summary>
    public bool RetailStillStep { get; set; }

    /// <summary>The seconds a first step takes before there is a frame time (<c>1 / SceneTool::m_FramesPerSecond</c>).</summary>
    public double FirstStepSeconds { get; set; } = 1.0 / 60.0;

    /// <summary>The last viewer <see cref="Update"/> found, or default before the first.</summary>
    public CellFrame Viewer => _viewer;

    /// <summary>True when keys hold a camera movement (rotate, zoom, raise or lower).</summary>
    public bool KeysHeld => _rotLeft || _rotRight || _raise || _lower || _closer || _farther;

    /// <summary>
    /// The first-person view asks the player to turn: <c>Rotate</c> with the camera in the head turns the player, not the camera. The arguments are
    /// retail's <c>clockwise_dir</c> (true: TurnRight) and the speed. The host holds the turn command at that speed while the requests keep coming each
    /// frame, and stops it when they do not (<see cref="SteeringRequested"/> is false after <see cref="EndFrame"/>).
    /// </summary>
    public event Action<bool, float>? SteerRequested;

    /// <summary>A named, logged TODO: something the camera does not do yet (told once).</summary>
    public event Action<string>? Unbuilt;

    /// <summary>True when a steer request came since <see cref="EndFrame"/>.</summary>
    public bool SteeringRequested => _steering;

    /// <summary>Marks the end of a frame for the host's steer hold: clears <see cref="SteeringRequested"/>.</summary>
    public void EndFrame() => _steering = false;

    /// <summary>The viewer will be placed again by the next <see cref="Update"/> without smoothing (a teleport, a new placement).</summary>
    public void Snap() => _hasViewer = false;

    /// <summary>
    /// <c>CameraSet::SetDefaultOffsets</c> with the camera moved: the pivot on the player, the direction forward, the stiffness back to the current
    /// one, the viewer offset (0, -2.5, 0.75) times <see cref="Scale"/>.
    /// </summary>
    public void SetDefaultOffsets()
    {
        PivotOffset = new Vector3(0f, 0f, DefaultPivotZ);
        Direction = Vector3.UnitY;
        TranslationalStiffness = CurrentStiffness;
        RotationalStiffness = CurrentStiffness;
        ViewerOffset = VideoOffset ?? new Vector3(0f, DefaultOffsetYFactor * Scale, DefaultOffsetZFactor * Scale);
        SetTargetForOffset(ViewerOffset);
    }

    /// <summary>
    /// The default offsets with the viewer turned about the pivot by <paramref name="degrees"/> (the review hunt's side view, M4 1.3-L1: the target stood behind the player's body in line with the camera).
    /// 0 is <see cref="SetDefaultOffsets"/>.
    /// </summary>
    /// <param name="degrees">The turn, in degrees; the sign picks the side.</param>
    /// <param name="pivotZ">The pivot's height above the player's origin (default <see cref="DefaultPivotZ"/>): the look capture lowers it to the chest so the hands are in frame (a capture aid; the next <see cref="SetDefaultOffsets"/> puts retail's back).</param>
    public void SetHeadingOffset(float degrees, float pivotZ = DefaultPivotZ)
    {
        SetDefaultOffsets();
        PivotOffset = new Vector3(0f, 0f, pivotZ);
        if (degrees == 0f)
        {
            return;
        }

        var a = degrees * (MathF.PI / 180f);
        var s = MathF.Sin(a);
        var c = MathF.Cos(a);
        var o = ViewerOffset;
        ViewerOffset = new Vector3((o.X * c) + (s * o.Y), (c * o.Y) - (s * o.X), o.Z);
        SetTargetForOffset(ViewerOffset);
    }

    /// <summary>
    /// <c>--video-camera=distance,height</c> (video capture only, off by default): the viewer offset <see cref="SetDefaultOffsets"/> uses instead of retail's (0, -2.5, 0.75),
    /// so the player and a target in front of him are both in frame. Null: retail's. Not a retail behaviour (TODO(video-camera): a recording aid, nothing in the game changes without the option).
    /// </summary>
    public Vector3? VideoOffset { get; private set; }

    /// <summary>Sets <see cref="VideoOffset"/> to (0, -<paramref name="distance"/>, <paramref name="height"/>) and puts the camera on it.</summary>
    public void SetVideoOffset(float distance, float height)
    {
        VideoOffset = new Vector3(0f, -distance, height);
        SetDefaultOffsets();
    }

    /// <summary><c>CameraSet::SetInHead</c>: the viewer 18 cm in front of the pivot, following the player at once.</summary>
    public void SetInHead()
    {
        TranslationalStiffness = 1f;
        SetTargetDirection(Vector3.UnitY);
        ViewerOffset = new Vector3(0f, InHeadY, 0f);
        SetTargetForOffset(ViewerOffset);
    }

    /// <summary>The overhead view key (<c>ToggleLookDown</c>, 0x3B): not built.</summary>
    public void ToggleLookDown()
    {
        if (!_reportedLookDown)
        {
            _reportedLookDown = true;
            Unbuilt?.Invoke("TODO(g5-camera-overhead): the overhead view (CameraSet::LookDown, 11.4186 0x00457FF0, TODO(camera): 11.6096) is not built; the key does nothing");
        }
    }

    /// <summary>The map view key (<c>ToggleMapMode</c>, 0x3C): not built.</summary>
    public void ToggleMapMode()
    {
        if (!_reportedMapMode)
        {
            _reportedMapMode = true;
            Unbuilt?.Invoke("TODO(g5-camera-map-view): the map view (CameraSet::SetMapMode, 11.4186 0x00458200, TODO(camera): 11.6096) is not built; the key does nothing");
        }
    }

    /// <summary><c>CameraSet::ToggleMouseLook</c>: the mouse look on or off.</summary>
    public void ToggleMouseLook(bool on)
    {
        if (MouseLookActive == on)
        {
            return;
        }

        MouseLookActive = on;
        if (on)
        {
            _lookHorizontalRun = 0;
            _lookVerticalRun = 0;
        }
    }

    /// <summary>Starts or stops the zoom in (<c>CameraManager::OnAction</c> 0x33: <c>Closer(1, 1.0)</c> / <c>StopCloser</c>).</summary>
    public void HoldCloser(bool start, double now)
    {
        if (start)
        {
            Closer(now, true, 1f);
        }
        else
        {
            _closer = false;
            _lastZoom = 0;
        }
    }

    /// <summary>Starts or stops the zoom out (0x34).</summary>
    public void HoldFarther(bool start, double now)
    {
        if (start)
        {
            Farther(now, true, 1f);
        }
        else
        {
            _farther = false;
            _lastZoom = 0;
        }
    }

    /// <summary>Starts or stops the orbit to the left (0x35, <paramref name="left"/>) or the right (0x36).</summary>
    public void HoldRotate(bool left, bool start, double now)
    {
        if (start)
        {
            Rotate(left, now, true, 1f, true);
        }
        else
        {
            _rotLeft = false;
            _rotRight = false;
            _lastRotate = 0;
        }
    }

    /// <summary>Starts or stops raising the camera (0x37).</summary>
    public void HoldRaise(bool start, double now)
    {
        if (start)
        {
            Raise(now, true, 1f);
        }
        else
        {
            _raise = false;
            _lastRaiseOrLower = 0;
        }
    }

    /// <summary>Starts or stops lowering the camera (0x38).</summary>
    public void HoldLower(bool start, double now)
    {
        if (start)
        {
            Lower(now, true, 1f);
        }
        else
        {
            _lower = false;
            _lastRaiseOrLower = 0;
        }
    }

    /// <summary>
    /// <c>CameraSet::Closer</c>: the offset shrinks by <c>1 - 0.2 * 41 * dt</c> (or by <c>1 - 0.2 / <paramref name="modOverride"/></c> when an
    /// override other than 1 is given), and does not go inside 0.5 m of the pivot. Nothing happens in first person.
    /// </summary>
    public void Closer(double now, bool keepGoing, float modOverride)
    {
        if (modOverride == 0f)
        {
            return;
        }

        _closer = keepGoing;
        _farther = false;
        if (InHead)
        {
            return;
        }

        if (_lastZoom == 0.0)
        {
            _lastZoom = now - FirstStepSeconds;
        }

        var dt = now - _lastZoom;
        if (dt < MinStepSeconds)
        {
            return;
        }

        var mod = (float)(dt * AdjustmentSpeed);
        if (modOverride != 1f)
        {
            mod = 1f / modOverride;
        }

        var next = ViewerOffset * (1f - (mod * ZoomRate));
        _lastZoom = now;
        if (next.Length() >= MinOffsetLength)
        {
            SetStiffnessForZ(next.Z);
            ViewerOffset = next;
            SetTargetForOffset(next);
            if (!keepGoing)
            {
                _closer = false;
                _lastZoom = 0;
            }
        }
    }

    /// <summary>
    /// <c>CameraSet::Farther</c>: the offset grows by <c>1 + 0.2 * 41 * dt</c> up to 10 m; from first person the camera leaves the head for (0, -1.2, 0.5).
    /// </summary>
    public void Farther(double now, bool keepGoing, float modOverride)
    {
        _farther = keepGoing;
        _closer = false;
        if (_lastZoom == 0.0)
        {
            _lastZoom = now - FirstStepSeconds;
        }

        var dt = now - _lastZoom;
        if (dt < MinStepSeconds)
        {
            return;
        }

        Vector3 next;
        if (InHead)
        {
            _lastZoom = now;
            PivotOffset = new Vector3(0f, 0f, DefaultPivotZ);
            TranslationalStiffness = CurrentStiffness;
            RotationalStiffness = CurrentStiffness;
            next = OutOfHeadOffset;
        }
        else
        {
            var mod = (float)(dt * AdjustmentSpeed);
            if (modOverride != 1f)
            {
                mod = modOverride;
            }

            next = ViewerOffset * ((mod * ZoomRate) + 1f);
            _lastZoom = now;
            if (!(MathF.Abs(next.X) <= MaxFartherAxis && MathF.Abs(next.Y) <= MaxFartherAxis && next.Z <= MaxFartherZ))
            {
                return;
            }

            SetStiffnessForZ(next.Z);
        }

        ViewerOffset = next;
        SetTargetForOffset(next);
        if (!keepGoing)
        {
            _farther = false;
            _lastZoom = 0;
        }
    }

    /// <summary>
    /// <c>CameraSet::Rotate</c>: orbits the camera about the pivot by <c>41 * dt * 8 degrees</c> (<paramref name="modOverride"/> replaces
    /// <c>41 * dt</c> unless it is 1), to the left when <paramref name="left"/> (the offset turns clockwise seen from above). In first person it turns
    /// the player instead (<see cref="SteerRequested"/>).
    /// </summary>
    /// <param name="left">Retail's <c>clockwise_dir</c>: the "Rotate Camera Left" key.</param>
    /// <param name="now">The host's clock.</param>
    /// <param name="keepGoing">The key is held: <see cref="Update"/> repeats it.</param>
    /// <param name="modOverride">The size of the step, 1 for the held key's (time-based) step.</param>
    /// <param name="cameraKeys">The call comes from the camera keys, not the mouse look (mouse turning applies only to the mouse).</param>
    public void Rotate(bool left, double now, bool keepGoing, float modOverride, bool cameraKeys)
    {
        if (_lastRotate == 0.0)
        {
            _lastRotate = now - FirstStepSeconds;
        }

        var dt = now - _lastRotate;
        if (dt < MinStepSeconds)
        {
            return;
        }

        var offset = ViewerOffset;
        var mod = (float)(dt * AdjustmentSpeed);
        if (modOverride != 1f)
        {
            mod = modOverride;
        }

        if (!InHead && !(MouseTurning && !cameraKeys))
        {
            var a = mod * AngleRadians;
            var s = MathF.Sin(a);
            var c = MathF.Cos(a);
            float x;
            float y;
            if (left)
            {
                _rotLeft = keepGoing;
                _rotRight = false;
                x = (offset.X * c) + (s * offset.Y);
                y = (c * offset.Y) - (s * offset.X);
            }
            else
            {
                _rotRight = keepGoing;
                _rotLeft = false;
                x = (offset.X * c) - (s * offset.Y);
                y = (c * offset.Y) + (s * offset.X);
            }

            _lastRotate = now;
            SetTargetDirection(Vector3.UnitY);
            ViewerOffset = new Vector3(x, y, offset.Z);
            SetTargetForOffset(ViewerOffset);
        }
        else
        {
            // Rotate turns the player here. With the keys retail sets the heading 8 degrees on each frame, through a command-interpreter slot that is
            // not identified (11.4186 CameraSet.c Rotate); with the mouse turning it sends the turn commands at 2 * mod up to 1.5. Lifestone turns with
            // the turn commands for both: TODO(g5-first-person-turn).
            if (!cameraKeys && MathF.Abs(mod) < MouseTurnDeadZone)
            {
                return;
            }

            if (left)
            {
                _rotLeft = keepGoing;
                _rotRight = false;
            }
            else
            {
                _rotRight = keepGoing;
                _rotLeft = false;
            }

            _lastRotate = now;
            _steering = true;
            if (!_reportedFirstPersonTurn)
            {
                _reportedFirstPersonTurn = true;
                Unbuilt?.Invoke("TODO(g5-first-person-turn): in first person retail sets the heading in 8-degree steps for the keys (a command-interpreter slot that is not identified, CameraSet::Rotate 11.4186); Lifestone turns the player with the turn commands instead");
            }

            SteerRequested?.Invoke(left, cameraKeys ? 1f : MathF.Min(2f * mod, MouseTurnSpeedCap));
        }

        if (!keepGoing)
        {
            _rotLeft = false;
            _rotRight = false;
            _lastRotate = 0;
        }
    }

    /// <summary>
    /// <c>CameraSet::Raise</c>: the camera moves up the sphere about the pivot by <c>41 * dt * 8 degrees</c> of pitch, and stays on its side of the
    /// pivot. In first person the view's pitch component falls by 0.2 (within +-0.8), as the decompile reads.
    /// </summary>
    public void Raise(double now, bool keepGoing, float modOverride) => Pitch(now, keepGoing, modOverride, raise: true);

    /// <summary>
    /// <c>CameraSet::Lower</c>: the camera moves down the sphere by the same step, but never below the pivot's height within 1.2 m of it. In first
    /// person the pitch component rises by 0.2.
    /// </summary>
    public void Lower(double now, bool keepGoing, float modOverride) => Pitch(now, keepGoing, modOverride, raise: false);

    /// <summary>
    /// <c>CameraSet::MouseLookHandler</c> with <c>FilterMouseInput</c>: the mouse moved by (<paramref name="dx"/>, <paramref name="dy"/>) in mouse-look
    /// mode. The movement is smoothed, scaled by the sensitivity / 15, and a run of more than five events in one axis acts: the horizontal axis rotates,
    /// the vertical raises or lowers.
    /// </summary>
    public void MouseLook(float dx, float dy, double now)
    {
        if (!_reportedMouseLookPrefs)
        {
            _reportedMouseLookPrefs = true;
            Unbuilt?.Invoke("TODO(g5-mouselook-prefs): retail's mouse-look preferences (m_MouseLookSensitivity, m_MouseLookSmoothingAmount, m_InvertMouseLookYAxis, m_UseMouseTurning) are not read; Lifestone uses sensitivity 1, no smoothing, no inversion, and orbits the camera out of first person");
        }

        float averageX = dx;
        float averageY = dy;
        if (now - _lastFilter <= MouseFilterSeconds)
        {
            averageX = (_lastDx + dx) * 0.5f;
            averageY = (_lastDy + dy) * 0.5f;
        }

        var keep = 1f - MouseLookSmoothing;
        var horizontal = ((dx * keep) + (averageX * MouseLookSmoothing)) * MouseLookSensitivity * MouseLookScale;
        var vertical = ((dy * keep) + (averageY * MouseLookSmoothing)) * MouseLookSensitivity * MouseLookScale;
        _lastDx = dx * keep + (averageX * MouseLookSmoothing);
        _lastDy = dy * keep + (averageY * MouseLookSmoothing);
        _lastFilter = now;
        if (InvertMouseLookY)
        {
            horizontal = -horizontal;
            vertical = -vertical;
        }

        if (horizontal == 0f)
        {
            _lookHorizontalRun = 0;
        }
        else if (++_lookHorizontalRun > MouseLookDeadEvents)
        {
            // Mouse right orbits the camera to the right out of the head; in the head it turns the player right.
            Rotate(horizontal > 0f ? InHead : !InHead, now, keepGoing: false, MathF.Abs(horizontal), cameraKeys: false);
        }

        if (vertical == 0f)
        {
            _lookVerticalRun = 0;
        }
        else if (++_lookVerticalRun > MouseLookDeadEvents)
        {
            if (vertical < 0f)
            {
                Raise(now, false, MathF.Abs(vertical));
            }
            else
            {
                Lower(now, false, vertical);
            }
        }
    }

    /// <summary>
    /// <c>CameraSet::UpdateCamera</c> and <c>CameraManager::UpdateCamera</c> for one frame: the held keys act, the player fades when the viewer is
    /// close, and the viewer moves toward where it wants to be, then through <paramref name="sweep"/>.
    /// </summary>
    /// <param name="now">The host's clock in seconds.</param>
    /// <param name="player">The player's frame as drawn (<see cref="DrawInterpolation"/>).</param>
    /// <param name="sweep">The camera sweep through collision, or null for none.</param>
    public CameraPose Update(double now, in CellFrame player, ViewerSweep? sweep)
    {
        if (_rotLeft)
        {
            Rotate(true, now, true, 1f, true);
        }
        else if (_rotRight)
        {
            Rotate(false, now, true, 1f, true);
        }

        if (_closer)
        {
            Closer(now, true, 1f);
        }
        else if (_farther)
        {
            Farther(now, true, 1f);
        }

        if (_raise)
        {
            Raise(now, true, 1f);
        }
        else if (_lower)
        {
            Lower(now, true, 1f);
        }

        var cell = player.CellId;
        var orientation = player.Orientation;
        var pivot = player.Origin + Vector3.Transform(PivotOffset, orientation);
        var pivotFrame = new CellFrame(cell, new AcFrame(pivot, orientation));
        var previous = _hasViewer ? DrawInterpolation.InSpaceOf(_viewer, cell) : default;

        // CameraSet::UpdateCamera: how translucent the player is drawn (1 in the head; fading out as the viewer comes within 0.45 m of the pivot).
        var translucency = 0f;
        if (InHead)
        {
            translucency = 1f;
        }
        else if (_hasViewer)
        {
            var distance = Vector3.Distance(pivot, previous.Origin);
            if (distance < FadeStartMetres)
            {
                translucency = Math.Clamp(1f - ((distance - FadeEndMetres) / (FadeStartMetres - FadeEndMetres)), 0f, 1f);
            }
        }

        // CameraManager::UpdateCamera: where the camera wants to be.
        var direction = Vector3.Transform(Direction, orientation);
        if ((Targets & CameraTargets.AlignWithPlane) != 0 && !_reportedSlope)
        {
            _reportedSlope = true;
            Unbuilt?.Invoke("TODO(g5-camera-slope): the camera's alignment to the slope of the ground (CameraManager::UpdateCamera ALIGN_WITH_PLANE, 11.4186 0x00456660, 11.6096 0x00456780) is not built; the camera looks level along the player's heading");
        }

        var lookDirection = (Targets & CameraTargets.LookInDirection) != 0 ? direction : Vector3.Zero;
        var pivotDirection = IsSmall(lookDirection) ? direction : Vector3.Normalize(lookDirection);
        var pivotRotation = DrawFrameRules.SetVectorHeading(Quaternion.Identity, pivotDirection);
        var wantedOrigin = pivot + Vector3.Transform(ViewerOffset, pivotRotation);
        if ((Targets & CameraTargets.LookAtPivot) != 0)
        {
            var toPivot = pivot - wantedOrigin;
            lookDirection = IsSmall(toPivot) ? Vector3.Zero : Vector3.Normalize(toPivot);
        }

        if (IsSmall(lookDirection))
        {
            lookDirection = direction;
        }

        var wantedRotation = DrawFrameRules.SetVectorHeading(Quaternion.Identity, lookDirection);

        // The smoothing: a fraction of the way each frame, all of it for a stiffness of 1 (and on the first frame).
        var dt = now - _lastUpdate;
        _lastUpdate = now;
        var fractionOrigin = TranslationalStiffness > 1f - MinStepSeconds ? 1f : MathF.Min(1f, (float)(TranslationalStiffness * dt * 10.0));
        var fractionRotation = RotationalStiffness > 1f - MinStepSeconds ? 1f : MathF.Min(1f, (float)(RotationalStiffness * dt * 10.0));
        CellFrame desired;
        if (!_hasViewer)
        {
            desired = new CellFrame(cell, new AcFrame(wantedOrigin, wantedRotation));
        }
        else
        {
            var origin = Vector3.Lerp(previous.Origin, wantedOrigin, fractionOrigin);
            var rotation = Quaternion.Normalize(Quaternion.Slerp(previous.Orientation, wantedRotation, fractionRotation));
            var still = fractionOrigin < 1f && fractionRotation < 1f
                && Vector3.Distance(origin, previous.Origin) < StillStepMetres(dt, RetailStillStep)
                && MathF.Abs(Quaternion.Dot(rotation, previous.Orientation)) > 1f - 1e-7f;
            desired = still ? previous : new CellFrame(cell, new AcFrame(origin, rotation));
        }

        var blocked = false;
        var viewer = desired;
        if (sweep is not null)
        {
            var result = sweep(pivotFrame, desired);
            viewer = result.Position with { Frame = result.Position.Frame with { Orientation = desired.Orientation } };
            blocked = result.Blocked;
        }

        _viewer = viewer;
        _hasViewer = true;
        return new CameraPose(viewer, desired, pivotFrame, translucency, InHead, blocked);
    }

    /// <summary>
    /// The smallest step (m) the smoothing still takes. Retail keeps the previous viewer when the interpolated position is under a fixed 2 x 0.0002 m from it (CameraManager::UpdateCamera, 11.4186 0x00456660, 11.6096 0x00456780),
    /// so retail's dead zone around the wanted point grows with the frame rate: 0.0004 / (stiffness * 10 * dt), about 7 mm at 60 Hz but 20 mm at 240 Hz. The default is a glitch fix: the step is scaled by the frame time against 60 Hz
    /// (a longer frame keeps the 60 Hz threshold, so at 60 Hz or slower it equals retail), which keeps the dead zone the same on every display (M4 1.3 s2, the shell-frames fade check's 0.30 m stop).
    /// <c>--retail-camera-still</c> (<paramref name="retail"/>) restores retail's fixed step.
    /// </summary>
    internal static float StillStepMetres(double dt, bool retail = false) => retail ? 2f * MinStepSeconds : 2f * MinStepSeconds * (float)Math.Min(1.0, dt * 60.0);

    private void Pitch(double now, bool keepGoing, float modOverride, bool raise)
    {
        if (raise)
        {
            _raise = keepGoing;
            _lower = false;
        }
        else
        {
            _lower = keepGoing;
            _raise = false;
        }

        if (_lastRaiseOrLower == 0.0)
        {
            _lastRaiseOrLower = now - FirstStepSeconds;
        }

        if (now - _lastRaiseOrLower < MinStepSeconds)
        {
            return;
        }

        var offset = ViewerOffset;
        if (InHead)
        {
            var z = Math.Clamp(Direction.Z + (raise ? -FirstPersonPitchStep : FirstPersonPitchStep), -MouseLookLimit, MouseLookLimit);
            SetTargetDirection(new Vector3(Direction.X, Direction.Y, z));
            EndPitch(keepGoing, raise);
            return;
        }

        var mod = (float)(AdjustmentSpeed * (now - _lastRaiseOrLower));
        if (modOverride != 1f)
        {
            mod = modOverride;
        }

        if (MouseTurning)
        {
            mod *= 0.25f;
        }

        _lastRaiseOrLower = now;
        var radius = offset.Length();
        var theta = HeadingOf(offset) * (MathF.PI / 180f);
        var pitch = (PitchOf(offset) * (MathF.PI / 180f)) + (raise ? AngleRadians * mod : -(AngleRadians * mod));
        var cosPitch = MathF.Cos(pitch);
        var next = new Vector3(MathF.Sin(theta) * radius * cosPitch, MathF.Cos(theta) * radius * cosPitch, MathF.Sin(pitch) * radius);
        if (raise)
        {
            // The camera stays on its side of the pivot: neither horizontal component changes sign.
            if (offset.Y < 0f ? next.Y >= 0f : offset.Y > 0f && next.Y <= 0f)
            {
                return;
            }

            if (offset.X < 0f ? next.X >= 0f : offset.X > 0f && next.X <= 0f)
            {
                return;
            }

            if (next.Z > LowestStiffnessZ)
            {
                SetCameraStiffness(CurrentStiffness);
            }
        }
        else
        {
            if (!(PitchHorizontalLimit * PitchHorizontalLimit <= (next.X * next.X) + (next.Y * next.Y) || next.Z >= 0f))
            {
                return;
            }

            if (next.Z <= LowestStiffnessZ)
            {
                SetCameraStiffness(1f);
            }
        }

        SetTargetDirection(Vector3.UnitY);
        ViewerOffset = next;
        SetTargetForOffset(next);
        EndPitch(keepGoing, raise);
    }

    private void EndPitch(bool keepGoing, bool raise)
    {
        if (keepGoing)
        {
            return;
        }

        if (raise)
        {
            _raise = false;
        }
        else
        {
            _lower = false;
        }

        _lastRaiseOrLower = 0;
    }

    /// <summary><c>CameraSet::SetTargetForOffset</c>: where the camera looks for a viewer offset.</summary>
    private void SetTargetForOffset(Vector3 offset)
    {
        if (offset.X == 0f && offset.Y == InHeadY && offset.Z == 0f)
        {
            Targets = CameraTargets.AlignWithPlane | CameraTargets.LookInDirection;
            return;
        }

        if (offset.X > 1f || offset.X < -1f || offset.Y > 0f)
        {
            Targets = CameraTargets.LookAtPivot;
            return;
        }

        Targets = CameraTargets.AlignWithPlane;
        if (offset.Y <= -0.5f && offset.Z >= -0.5f)
        {
            Targets |= CameraTargets.LookInDirection;
        }

        if (offset.Z > 0.75f || offset.Z < -0.5f)
        {
            Targets |= CameraTargets.LookAtPivot;
        }
    }

    /// <summary><c>CameraManager::SetTargetDirection</c>: the direction, normalised; unchanged when it is too short.</summary>
    private void SetTargetDirection(Vector3 direction)
    {
        if (!IsSmall(direction))
        {
            Direction = Vector3.Normalize(direction);
        }
    }

    private void SetStiffnessForZ(float z) => SetCameraStiffness(z <= LowestStiffnessZ ? 1f : CurrentStiffness);

    private void SetCameraStiffness(float stiffness)
    {
        TranslationalStiffness = stiffness;
        RotationalStiffness = stiffness;
    }

    private static bool IsSmall(Vector3 v) => v.LengthSquared() < AcFrame.Epsilon * AcFrame.Epsilon;

    /// <summary><c>Vector3::get_heading</c>: the compass heading of the horizontal part, 0 when it is too short.</summary>
    private static float HeadingOf(Vector3 v)
    {
        var horizontal = new Vector3(v.X, v.Y, 0f);
        if (IsSmall(horizontal))
        {
            return 0f;
        }

        var degrees = 450.0 - (Math.Atan2(horizontal.Y, horizontal.X) * (180.0 / Math.PI));
        return (float)(degrees % 360.0);
    }

    /// <summary><c>Vector3::get_pitch</c>: <c>asin</c> of the unit vector's Z in degrees, 0 when the vector is too short.</summary>
    private static float PitchOf(Vector3 v) =>
        IsSmall(v) ? 0f : (float)(Math.Asin(Math.Clamp(v.Z / v.Length(), -1f, 1f)) * (180.0 / Math.PI));
}
