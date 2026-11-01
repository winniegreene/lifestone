using System.Diagnostics;
using System.Globalization;
using Godot;
using Lifestone.Client.Avatars;
using Lifestone.Client.Camera;
using Lifestone.Client.Movement;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Streaming;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.World.Interiors;

namespace Lifestone.Game.Player;

/// <summary>
/// The player in the main scene (M2 G5): each frame it keeps the collision around the player attached, steps the player's
/// physics through the movement controller (retail's 30 Hz gate and <c>update_object</c>), advances the avatar set and hands the
/// result to <see cref="PlayerView"/>. It runs before the camera (process priority -20) so the camera of the frame sees this
/// frame's player. It is also the player's input: the actions of retail's movement and camera input maps
/// (<see cref="IInputActionSink"/>) and the world pointer (<see cref="IWorldPointerSink"/>: the mouse look).
/// </summary>
/// <remarks>
/// <para><b>The code that will run online.</b> The player is a <see cref="OfflineWalker"/>: a physics object with the human Setup and
/// the avatar's own part array, stepped by a <see cref="PlayerMovementController"/> that sends its movement messages to a sink that
/// sends nothing (<see cref="DryRunMovementSink"/>, which logs them). Online, the same controller sends them to the session and the weenie comes from the
/// character's qualities; the avatar, the collision streaming, the lighting, the view, the keys and the camera do not change.</para>
/// <para><b>Order of a frame</b> (docs/avatars.md §5.3, docs/physics.md §12): the input clock; the collision streamer's focus and pump; the
/// controller's step (inside it <c>PhysicsObj.UpdateObject</c> runs the target reports, the movement manager, the part array's
/// completion check <c>CPartArray::HandleMovement</c> and the position manager, in retail's order) and its <c>UseTime</c> (the keys held, the
/// position messages); the avatar set's advance, which takes the body's position and pose; the view. Then <see cref="ChaseCamera"/> reads
/// <see cref="DrawPosition"/>.</para>
/// <para><b>Placement.</b> The collision of the spawn landblock builds on the workers, so the player waits (the walker's
/// <see cref="PhysicsObj.PlacementPending"/>, <see cref="Placing"/>) and is placed when the streamer attaches it; it is not drawn
/// meanwhile, instead of floating at its requested height.</para>
/// <para><b>Draw positions</b> (D26) are the 30 Hz physics positions interpolated between steps (<see cref="DrawInterpolation"/>);
/// <c>--motion-stepping=retail</c> (<see cref="RetailStepping"/>) draws the step positions as retail did. The camera follows the same drawn frame.</para>
/// </remarks>
public sealed partial class PlayerController : Node, IInputActionSink, IWorldPointerSink, ICameraInputHost
{
    private readonly GameServices _services;
    private readonly OfflineWalker _walker;
    private readonly AvatarSet _avatars;
    private readonly PlayerAvatar _player;
    private readonly PlayerView _view;
    private readonly CollisionStreamer _streamer;
    private readonly InteriorController? _interiors;
    private readonly PlayerStreamingFocus _focus;
    private readonly PlayerPhysicsEvents _events;
    private readonly MovementBindings _movement;
    private readonly CameraBindings _cameraKeys;
    private CellFrame _spawn;
    private PlayerInput? _input;
    private DrawInterpolation.Offset _drawOffset = DrawInterpolation.Offset.None;
    private long _steps;
    private long _stepsAtWindow;
    private double _windowSeconds;
    private double _stepsPerSecond;
    private bool _placedLogged;
    private bool _pointerLogged;
    private long _frames;
    private long _slowFrames;
    private double _maxFrameMs;
    private double _lastOwnMs;
    private double _maxOwnMs;
    private readonly PipelineCompilationWatch _pipelines = new();
    private readonly GcFrameWatch _gcWatch = new();
    private readonly RenderFrameWatch _renderWatch = new(); // M5 7 D151: the render, physics and process-CPU shares of a slow frame
    private string _notice = string.Empty;
    private bool _loggedNoticePlaceholder;

    /// <summary>The second line of the notice: the line is a placeholder for retail's chat window (the routing is deferred, <c>TODO(movement-notice-chat)</c>).</summary>
    private const string NoticePlaceholderTag = "\n(placeholder: retail prints this in the chat window)";
    private double _noticeUntil;

    /// <summary>A frame longer than this many milliseconds is reported (the M2 frame-time rule: no frame over 50 ms).</summary>
    public const double SlowFrameMilliseconds = 50.0;

    /// <summary>
    /// The first frames belong to the engine's and the other tracks' start-up (the terrain atlas upload, the first attach drain: 100-150 ms
    /// with or without the player), as the fly-path measurement leaves them out; the player's own cost is measured from the first frame
    /// (<c>max_player_ms</c> in the summary).
    /// </summary>
    public const int WarmupFrames = 5;

    /// <summary>How long a notice (autorun on or off, a refused move) stays on screen.</summary>
    public const double NoticeSeconds = 3.0;

    public PlayerController(
        GameServices services,
        OfflineWalker walker,
        AvatarSet avatars,
        PlayerAvatar player,
        PlayerView view,
        CollisionStreamer streamer,
        PlayerStreamingFocus focus,
        PlayerPhysicsEvents events,
        InteriorController? interiors,
        in CellFrame spawn)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(walker);
        ArgumentNullException.ThrowIfNull(avatars);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(streamer);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(events);
        _services = services;
        _walker = walker;
        _avatars = avatars;
        _player = player;
        _view = view;
        _streamer = streamer;
        _focus = focus;
        _events = events;
        _interiors = interiors;
        _spawn = spawn;
        CameraModel = new ChaseCameraModel { RetailStillStep = PlayOptions.Parse(OS.GetCmdlineUserArgs()).RetailCameraStill }; // --retail-camera-still
        _movement = new MovementBindings(walker.Controller, Clock, () => walker.Controller.AutoRun);
        _cameraKeys = new CameraBindings(CameraModel, Clock, this);
        walker.Controller.Notice += OnNotice;
        _movement.HumanKey += key => HumanKey?.Invoke(key);
        Name = "PlayerController";
        ProcessPriority = -20;
    }

    /// <summary>The controller of the scene's player, or null (PerfOverlay's rows).</summary>
    public static PlayerController? Current { get; private set; }

    /// <summary>The player's physics, walking and animation.</summary>
    public OfflineWalker Walker => _walker;

    /// <summary>Retail's command interpreter for the player: the keys' intents, the jump bar, the position messages.</summary>
    public PlayerMovementController Controller => _walker.Controller;

    /// <summary>The avatar set holding the player.</summary>
    public AvatarSet Avatars => _avatars;

    /// <summary>The player's avatar.</summary>
    public PlayerAvatar Avatar => _player;

    /// <summary>The player's view.</summary>
    public PlayerView View => _view;

    /// <summary>The collision streamer.</summary>
    public CollisionStreamer Streamer => _streamer;

    /// <summary>The camera's model: the chase camera's offsets and keys (retail's <c>CameraSet</c>).</summary>
    public ChaseCameraModel CameraModel { get; }

    /// <summary>The player's camera (the chase camera or first person), or null until it is installed (the walk checks read its pose).</summary>
    public ChaseCamera? Camera { get; set; }

    /// <summary>The player's input, or null until it is attached (diagnostics and the retail UI hand-over).</summary>
    public PlayerInput? Input => _input;

    /// <summary>True when the player is drawn at the physics step positions, as retail did (<c>--motion-stepping=retail</c>).</summary>
    public bool RetailStepping { get; set; }

    /// <summary>
    /// Whether the keys may reach the player now: false while a panel's text field has the keyboard, the fly-path runner drives the camera or portal
    /// space is busy (the same flag the free camera obeys). Null: always.
    /// </summary>
    public Func<bool>? InputAllowed { get; set; }

    /// <summary>The on-screen line for the notices of the movement keys (autorun on or off, a refused move), or null.</summary>
    public Label? NoticeLabel { get; set; }

    /// <summary>True until the player's landblock collision is attached and the walker has been placed.</summary>
    public bool Placing => _walker.Physics.PlacementPending;

    /// <summary>The player's position: where the walker is, or where it will be placed while it waits.</summary>
    public CellFrame Position => _walker.Physics.Cell is null ? _spawn : _walker.Position;

    /// <summary>
    /// The player's frame as it is drawn this frame (D26: between the last two physics steps, or the last step's with
    /// <see cref="RetailStepping"/>). The camera's pivot.
    /// </summary>
    public CellFrame DrawPosition { get; private set; }

    /// <summary>The move from the physics step's body frame to the interpolated one the player is drawn at (D26); the server-objects layer applies it to the player's held items (M3 4.1 fix A, R6).</summary>
    public DrawInterpolation.Offset DrawOffset => _drawOffset;

    /// <summary>The streaming focus: the player's position with the dungeon rule (<see cref="PlayerStreamingFocus"/>).</summary>
    public StreamingFocus StreamingFocus => _focus.Of(Position);

    /// <summary>Physics steps run since the player was made.</summary>
    public long Steps => _steps;

    /// <summary>The longest this node's own frame work took, in milliseconds (the walk checks report it).</summary>
    public double MaxOwnMilliseconds => _maxOwnMs;

    /// <summary>The input clock, in seconds: Godot's monotonic time (the clock the physics gate and the jump bar run on).</summary>
    public static double Clock() => Time.GetTicksUsec() / 1_000_000.0;

    /// <summary>Gives the controller the input stack that carries the keys to it (its own router, or the retail UI's).</summary>
    public void AttachInput(PlayerInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _input = input;
    }

    /// <inheritdoc/>
    public override void _EnterTree() => Current = this;

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }

        _input?.Dispose();
        _streamer.Dispose();
        Log.Info(
            "play",
            "player frame times",
            ("frames", _frames),
            ("max_ms", _maxFrameMs),
            ("max_player_ms", _maxOwnMs),
            ("slow_frames", _slowFrames),
            ("slow_over_ms", SlowFrameMilliseconds),
            ("physics_steps", _steps),
            ("move_to_state", _walker.Sink is DryRunMovementSink dry ? dry.MoveToStateCount : -1),
            ("autonomous_position", _walker.Sink is DryRunMovementSink dry2 ? dry2.AutonomousPositionCount : -1));
    }

    /// <summary>
    /// A server object put into the world on the placed player (the lifestone's create after a recall's teleport, a creature created on the player's spot) puts the player out of it
    /// by retail's sliding placement, the headless body's call (<see cref="OfflineWalker.ReplaceAfterObjectsEntered"/>; M5 item 5 F1, <c>TODO(m5-create-overlap)</c>): the walker's
    /// transition would hold a body that is already inside an object (measured, 0.00 m).
    /// </summary>
    private void ReplaceAfterObjectsEntered()
    {
        var moved = _walker.ReplaceAfterObjectsEntered((_avatars.ServerObjects as Lifestone.Client.Physics.ServerObjectPhysicsHost)?.Stats);
        if (moved > 0.01f)
        {
            Log.Info(
                "play",
                "a server object appeared on the player: placed out of it (TODO(m5-create-overlap): retail creates the objects before it places the player)",
                ("moved_m", moved.ToString("F2", CultureInfo.InvariantCulture)),
                ("cell", $"0x{_walker.Position.CellId:X8}"),
                ("at", _walker.Position.Origin));
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        var started = Stopwatch.GetTimestamp();
        var now = Clock();
        WatchFrame(delta);
        if (_input is not null)
        {
            _input.Enabled = InputAllowed?.Invoke() ?? true;
            _input.Tick(now);
        }

        _streamer.SetFocus(Position.CellId);
        _interiors?.SetPlayerCell(Lifestone.Client.World.AcCoordinates.IsEnvCell(Position.CellId) ? Position.CellId : 0); // the layer meshes the player's cell when it needs more than the viewer's
        _streamer.Pump(_services.MainThread.Scheduler);
        ReplaceAfterObjectsEntered();
        if (_walker.Controller.Tick(now))
        {
            _steps++;
        }

        // Deliberately unbudgeted (wave 3, dod-6-7-8): this is the frame's only advance of the shared avatar set, and an object the budget skipped would step its
        // whole gap (up to 2 s) next turn, a visible jump; stepping a few hundred avatars costs well under the 50 ms frame rule (the census prints the step time).
        _avatars.Advance(now, new AvatarViewer(Position), long.MaxValue);
        if (Placing)
        {
            DrawPosition = _spawn;
            _drawOffset = DrawInterpolation.Offset.None;
            _view.HideAll();
        }
        else
        {
            if (!_placedLogged && _walker.Physics.Cell is not null)
            {
                _placedLogged = true;
                Log.Info("play", "the player is in the world", ("cell", $"0x{_walker.Position.CellId:X8}"), ("at", _walker.Position.Origin), ("on_ground", _walker.OnGround));
            }

            var controller = _walker.Controller;
            DrawPosition = DrawInterpolation.Interpolate(controller.PreviousPosition, controller.Position, controller.PreviousStepTime, controller.LastStepTime, now, RetailStepping);
            _drawOffset = DrawInterpolation.Offset.Between(controller.Position, DrawPosition);
            _view.Update(_avatars.Frame, _player, _walker.Physics.Height, _walker.Physics.Radius, _drawOffset);
        }

        ShowNotice(now);
        _windowSeconds += delta;
        if (_windowSeconds >= 1.0)
        {
            _stepsPerSecond = (_steps - _stepsAtWindow) / _windowSeconds;
            _stepsAtWindow = _steps;
            _windowSeconds = 0;
        }

        _lastOwnMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _maxOwnMs = Math.Max(_maxOwnMs, _lastOwnMs);
    }

    /// <summary>
    /// A person pressed a movement key (<see cref="OnInputAction"/> delivered a movement-map press; <see cref="MovementBindings.HumanKey"/>): the human's
    /// key wins over automation (R6). The play host forwards it to <c>MoveIntents.ReportHumanKey</c>, which the runner's Interrupted state listens to.
    /// Camera keys and releases do not raise it.
    /// </summary>
    public event Action<string>? HumanKey;

    /// <summary>
    /// M5 7 D145: the movement keys' lock while the autopilot runs (<c>MoveIntents.KeyLock</c>; the play host sets it when it binds the session's movement, null when it unbinds): a locked movement-map action reaches nothing.
    /// </summary>
    public Lifestone.Client.Session.Contracts.HumanKeyLock? KeyLock
    {
        get => _movement.Lock;
        set => _movement.Lock = value;
    }

    /// <summary>
    /// An action of retail's movement map (4) or camera maps (5, and 6 while the alternate camera mode is held) fired
    /// (<c>IInputActionCallback::OnAction</c>): movement actions go to the command interpreter's intents (<see cref="MovementBindings"/>), camera
    /// actions to the camera (<see cref="CameraBindings"/>).
    /// </summary>
    public bool OnInputAction(uint inputMapId, uint actionId, RetailActivation activation) =>
        inputMapId == UiInputRouter.MovementMap
            ? _movement.OnInputAction(inputMapId, actionId, activation)
            : _cameraKeys.OnInputAction(inputMapId, actionId, activation);

    /// <summary>
    /// What a click on the world picks and selects (M2 G5 W7, docs/avatars.md §18); set by play mode, null in the explore scene (a click then does nothing,
    /// logged once).
    /// </summary>
    public WorldPicking? Picking { get; set; }

    /// <summary>The selected object (shared with the shell); set with <see cref="Picking"/>.</summary>
    public ObjectSelection? Selection { get; set; }

    /// <summary>The session's Action API, which a double click Uses an object through (<see cref="IActionApi.Use"/>); set with <see cref="Picking"/>.</summary>
    public IActionApi? Actions { get; set; }

    /// <summary>The retail UI's target mode (M5 7 S6): while one is on, a left press on the world runs it on the object under the pointer instead of selecting (<c>UIElement_SmartBoxWrapper::MouseDown</c> 11.6096 0x004E6390); null without the UI.</summary>
    public Lifestone.Client.RetailUi.Core.UiTargetMode? TargetMode { get; set; }

    /// <summary>The last Use a double click made (the checks wait for the server to answer it), or null.</summary>
    public ActionHandle? LastUse { get; private set; }

    /// <summary>
    /// A mouse button over the world, in logical UI pixels. A left press selects the object under the pointer, or clears the selection over empty space
    /// (the nearest drawn object whose bounds the pick ray passes through, not the player: <see cref="RayPicking"/>); retail's double click Uses it
    /// (<c>Actions.Use</c>, the way the headless Academy exit does). The other buttons and the releases are the mouse look's or nobody's.
    /// </summary>
    public void OnWorldPointer(int x, int y, UiMouseButton button, bool down, bool doubleClick)
    {
        if (!down || button != UiMouseButton.Left)
        {
            return;
        }

        if (Picking is not { } picking || Selection is not { } selection)
        {
            if (!_pointerLogged)
            {
                _pointerLogged = true;
                Log.Info("play", "a click on the world selects nothing: this scene has no picking (play mode sets it up)", ("button", button));
            }

            return;
        }

        var picked = picking.TryPick(x, y, out var guid) ? guid : 0u;
        if (TargetMode is { Active: true } target)
        {
            target.Execute(picked); // HandleTargetedUseLeftClick (0x004E6010): the mode runs on the object (nothing over empty space) and the selection stays; the press ends the mode
            return;
        }

        selection.Select(picked);
        if (doubleClick && picked != 0 && Actions is { } actions)
        {
            Log.Info("play", "double click: use", ("guid", $"0x{picked:X8}"));
            LastUse = actions.Use(picked, ActionSource.Ui);
        }
    }

    /// <inheritdoc/>
    public void OnMouseLook(float dx, float dy) => CameraModel.MouseLook(dx, dy, Clock());

    /// <summary>
    /// A text field took or gave up the keyboard (retail's text mode): taking it drops the movement keys, as <c>CommandInterpreter::LoseKeyboardFocus</c>
    /// (11.4186 0x006B31F0, 11.6096 0x006B4130) does.
    /// </summary>
    public bool TextEntryActive
    {
        set
        {
            if (value)
            {
                _walker.Controller.LoseKeyboardFocus();
            }
        }
    }

    /// <summary>
    /// The window gained or lost the focus (<see cref="UiInputRouter.ClientActiveChanged"/>): losing it drops the keys and stops the player
    /// (<c>LoseKeyboardFocus</c>), as retail's input manager does.
    /// </summary>
    public void OnClientActive(bool active)
    {
        if (!active)
        {
            _walker.Controller.LoseKeyboardFocus();
        }
    }

    /// <summary>
    /// Places the player at <paramref name="frame"/> as a server teleport does (<c>SmartBox::TeleportPlayer</c>): the camera places itself again there.
    /// <paramref name="keepConstraint"/> is true online: the server keeps echoing the accepted positions, which keeps retail's constraint (<c>ConstrainTo</c>)
    /// near the player; offline nothing echoes, so the constraint is taken off (below).
    /// </summary>
    public void Teleport(in CellFrame frame, bool keepConstraint = false)
    {
        _spawn = frame;
        _streamer.SetFocus(frame.CellId);
        _walker.Controller.Apply(new ObjectMoved(_walker.Physics.Id, frame, PositionVerdict.SelfTeleport, true, true, null, null), Clock());

        // A teleport arrival constrains the player to the position (retail: the server echoes every accepted position and the constraint follows the
        // player, SmartBox::HandleReceivedPosition). Offline nothing echoes, so the constraint would slow the player after 10 m (5 m indoors) and stop it
        // at 50 m (20 m indoors) from the spot it was teleported to: there is no server to keep the player near, so none.
        if (!keepConstraint)
        {
            _walker.Physics.UnConstrain();
        }

        CameraModel.Snap();
        Log.Info("play", "the player is teleported", ("cell", $"0x{frame.CellId:X8}"), ("at", frame.Origin));
    }

    /// <inheritdoc/>
    void ICameraInputHost.SetMouseLook(bool on) => _input?.SetMouseLook(on);

    /// <inheritdoc/>
    void ICameraInputHost.SetAlternateKeys(bool on) => _input?.SetAlternateKeys(on);

    /// <summary>Counts the frame and reports one over <see cref="SlowFrameMilliseconds"/> (the whole frame, not only the player's share).</summary>
    private void WatchFrame(double delta)
    {
        var ms = delta * 1000.0;
        _frames++;
        var compiled = _pipelines.Step();
        var gc = _gcWatch.Step();
        if (_frames == 1)
        {
            _renderWatch.Measure(GetViewport());
        }

        _renderWatch.Step();
        if (_frames > WarmupFrames)
        {
            _maxFrameMs = Math.Max(_maxFrameMs, ms);
        }

        if (ms > SlowFrameMilliseconds && _frames > WarmupFrames)
        {
            _slowFrames++;
            // process_ms is what Godot spent in every node's _Process in the previous frame; render_setup_cpu_ms the render server's CPU set-up; the rest of the frame is the draw and the wait.
            // pipelines_* are the pipeline compilations since the previous frame's process (a first-use shader compile: foreground ones stall the frame).
            Log.Warn(
                "play",
                "slow frame",
                [
                    ("ms", ms),
                    ("frame", _frames),
                    ("placing", Placing),
                    ("player_ms_in_previous_frame", _lastOwnMs),
                    ("process_ms", Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0),
                    ("render_setup_cpu_ms", RenderingServer.GetFrameSetupTimeCpu()),
                    ("gc_gen0_collections", GC.CollectionCount(0)),
                    .. gc.Fields(), // M4 P4 g3fix: the collector's pause and collections in this frame's interval, so the line names a GC pause as the cause (TODO(m4-throwaway-teleport-frame))
                    .. gc.LastCollectionFields(), // M4 P5b: and how big that collection was
                    .. compiled.Fields(),
                    .. _renderWatch.Fields(), // M5 7 D151: a frame with little process time, no GC and no pipeline: was it drawing, or waiting (process_cpu_ms small)?
                ]);
        }
    }

    /// <summary>The floating origin moved: draw the character again from its exact position.</summary>
    public void SyncToOrigin()
    {
        if (!Placing)
        {
            _view.Redraw(_avatars.Frame);
        }
    }

    private void OnNotice(MovementNotice notice)
    {
        if (!_loggedNoticePlaceholder)
        {
            _loggedNoticePlaceholder = true;
            Log.Warn("movement", "TODO(movement-notice-chat): movement notices (AutoRun ON/OFF, jump and load refusals) are a labelled placeholder HUD line; retail prints them in the chat window and the routing is deferred");
        }

        var text = notice.Kind switch
        {
            MovementNoticeKind.AutoRunOn => "AutoRun ON",
            MovementNoticeKind.AutoRunOff => "AutoRun OFF",
            _ => notice.Text ?? notice.Kind.ToString(),
        };
        _notice = text + NoticePlaceholderTag;
        _noticeUntil = Clock() + NoticeSeconds;
        Log.Info("movement", "notice", ("text", text), ("kind", notice.Kind), ("error", notice.Error));
    }

    private void ShowNotice(double now)
    {
        if (NoticeLabel is not { } label)
        {
            return;
        }

        var visible = now < _noticeUntil;
        if (label.Visible != visible)
        {
            label.Visible = visible;
        }

        if (visible && label.Text != _notice)
        {
            label.Text = _notice;
        }
    }

    /// <summary>The player and physics rows of the performance overlay.</summary>
    public string Describe()
    {
        var ci = CultureInfo.InvariantCulture;
        var position = _walker.Position;
        var forward = _walker.Motion.Interpreted.ForwardCommand;
        var state = forward == MotionCommands.Ready ? "Ready" : $"0x{forward:X8}";
        var sb = new System.Text.StringBuilder(256);
        sb.Append(string.Create(
            ci,
            $"player: {(Placing ? "waiting for collision" : $"cell 0x{position.CellId:X8}")}   at ({position.Origin.X:0.0}, {position.Origin.Y:0.0}, {position.Origin.Z:0.0})   " +
            $"ground {(_walker.OnGround ? "yes" : "no")}   motion {state}   hooks {_events.HooksForwarded}"));
        sb.Append((char)10);
        sb.Append(string.Create(
            ci,
            $"physics: {_stepsPerSecond:0} steps/s   collision {_streamer.RequestedCount} landblocks wanted, {_streamer.FailureCount} failed   " +
            $"draw {_view.DrawnParts} parts, {_view.MeshCount} meshes, {(_view.InCell ? "cell" : "outdoor")} lighting, " +
            $"{(_view.LightRow >= 0 ? $"light row {_view.LightRow} with {_view.LightCount} lights" : "no light set")}"));
        return sb.ToString();
    }

    /// <summary>
    /// The physics overlay's text (<c>--physics-overlay</c>): the player's cell, velocity and contact, its keys and the movement messages the dry-run
    /// sink would have sent.
    /// </summary>
    public string DescribePhysics()
    {
        var ci = CultureInfo.InvariantCulture;
        var physics = _walker.Physics;
        var controller = _walker.Controller;
        var position = _walker.Position;
        var velocity = physics.Velocity;
        var plane = physics.ContactPlane;
        var sb = new System.Text.StringBuilder(512);
        sb.Append(string.Create(
            ci,
            $"physics: cell 0x{position.CellId:X8}  pos ({position.Origin.X:0.00}, {position.Origin.Y:0.00}, {position.Origin.Z:0.00})  heading {position.HeadingDegrees:0.0}  speed {velocity.Length():0.00} m/s"));
        sb.Append((char)10);
        sb.Append(string.Create(
            ci,
            $"  contact {(physics.HasContact ? "yes" : "no")}  walkable {(physics.OnWalkable ? "yes" : "no")}  plane normal ({plane.Normal.X:0.00}, {plane.Normal.Y:0.00}, {plane.Normal.Z:0.00})  state {physics.TransientState}"));
        sb.Append((char)10);
        sb.Append(string.Create(
            ci,
            $"  keys: run {(controller.HoldRun ? "held" : "-")}  autorun {(controller.AutoRun ? "on" : "off")}  server control {(controller.ControlledByServer ? "yes" : "no")}  jump bar {(controller.Jump.Building ? "charging" : controller.Jump.Pending ? "pending" : "-")}"));
        sb.Append((char)10);
        if (_walker.Sink is DryRunMovementSink dry)
        {
            sb.Append(string.Create(
                ci,
                $"  would send: {dry.MoveToStateCount} MoveToState, {dry.AutonomousPositionCount} AutonomousPosition, {dry.JumpCount} Jump   last: {(dry.Last.Length == 0 ? "-" : dry.Last)}"));
            sb.Append((char)10);
        }

        sb.Append(string.Create(
            ci,
            $"  input: {(_input is null ? "none" : $"{_input.Counts.Events} events, {_input.Counts.Taken} taken")}   steps {_steps}   draw {(RetailStepping ? "stepping (retail)" : "interpolated (D26)")}   max player frame {_maxOwnMs:0.00} ms"));
        return sb.ToString();
    }
}
