using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement;

/// <summary>
/// The player's movement: retail's command interpreter (<c>CommandInterpreter</c> with <c>ACCmdInterp</c>'s overrides,
/// 11.4186 PORTAL/cmdinterp/CmdInterp.c and AC/accmdinterp/ACCmdInterp.c), the jump bar (<c>ClientCombatSystem</c>,
/// <see cref="JumpCharge"/>) and the player branch of <c>SmartBox::HandleReceivedPosition</c>
/// (<see cref="ServerPositionPolicy"/>), re-expressed function by function (names and 11.4186 / 11.6096 addresses
/// cited, nothing copied). It turns the keymap's intents into motion on the player's <see cref="PhysicsObj"/>, steps it,
/// and sends MoveToState, AutonomousPosition and Jump through an <see cref="IMovementSink"/> on retail's cadence.
/// </summary>
/// <remarks>
/// <para><b>Each frame</b> the host calls <see cref="Tick"/> (<see cref="StepPhysics"/>, which is
/// <c>CPhysics::UseTime</c>'s 30 Hz gate and <c>CPhysicsObj::update_object</c> for the player, then <see cref="UseTime"/>,
/// <c>CommandInterpreter::UseTime</c>). Retail dispatches the frame's network messages between the two
/// (<c>SmartBox::UseTime</c>); a host that wants that order calls them separately and gives the session's player
/// positions to <see cref="Apply"/> in between. The controller calls <see cref="PhysicsWorld"/>'s <c>UseTime</c>, so the
/// host must not.</para>
/// <para><b>The cadence</b> (decision table row 7, docs/physics.md §12):</para>
/// <list type="bullet">
/// <item>MoveToState after every movement change (<c>CommandInterpreter::SendMovementEvent</c>, 11.4186 0x006B4680,
/// 11.6096 0x006B55C0), with the contact flag Contact and OnWalkable and the standing long jump; it restarts the
/// position timer but not the last position sent.</item>
/// <item>AutonomousPosition (<c>CommandInterpreter::ShouldSendPositionEvent</c>, 0x006B45E0 / 0x006B5520, and
/// <c>SendPositionEvent</c>, 0x006B4770 / 0x006B56B0): once more than 1.0 s has passed since the last send, if the cell or
/// the frame changed; before that, at once if the cell changed or, in the same cell, the contact plane did. It is only
/// sent while the player stands on walkable ground, so nothing goes out in the air and the position goes out on
/// landing.</item>
/// <item>Jump when the jump key is released (<c>ClientCombatSystem::DoJump</c>).</item>
/// </list>
/// <para><b>Control.</b> Retail starts with the server in control (<c>controlled_by_server</c>); the first movement key
/// takes control back (<c>TakeControlFromServer</c>), and the host gives it to the server with
/// <see cref="LoseControlToServer"/> when the server moves the player (a non-autonomous UpdateMotion for the player,
/// <c>ACSmartBox::DispatchSmartBoxEvent</c>).</para>
/// <para><b>Not built (named, documented):</b> autonomy levels other than 2 (<c>MovePlayer_NonAutonomous</c> sends
/// DoMovement/StopMovement events; retail's default level is 2 and ACE never changes it); mouse movement and mouse-look
/// turning (<c>HandleMouseMovementCommand</c>, the <c>m_UseMouseTurning</c> branch of <c>MovePlayer</c>); housing
/// barriers (physics lets the player into a restricted cell, and the controller logs the first such entry as a
/// <c>TODO(physics)</c> warning).</para>
/// <para>Main thread (or the headless tick) only. <see cref="Tick"/> allocates nothing once warm.</para>
/// </remarks>
public sealed partial class PlayerMovementController : IPlayerMovementIntents, IRunByDefaultSource
{
    /// <summary>Retail <c>autonomy_level</c>: 2, the <c>CommandInterpreter</c> constructor's value; the only level M2 uses.</summary>
    public const uint AutonomyLevel = 2;

    private readonly PhysicsObj _player;
    private readonly MotionInterpreter _motion;
    private readonly IMovementSink _sink;
    private readonly ILogSink _log;
    private readonly double _timeBetweenPositionEvents;
    private double _now;
    private double _lastSentPositionTime;
    private CellFrame _lastSentPosition = new(0, AcFrame.Identity);
    private Plane _lastSentContactPlane;
    private CellFrame _previousTickPosition;
    private bool _hasPreviousTick;
    private bool _loggedUnenforcedRestriction;
    private bool _loggedLanding;
    private bool _loggedThinDoorway;
    private bool _wasOnGround;
    private bool _hasBeenOnGround;

    /// <summary>
    /// Retail <c>CommandInterpreter::CommandInterpreter</c> (11.4186 0x006B3E40, 11.6096 0x006B4D80) and
    /// <c>SetSmartBox</c>: a controller for <paramref name="player"/>, which must be its world's player and host a motion
    /// interpreter. The server is in control until the first movement key; the position timer starts at the world's
    /// current time; the last position sent is nowhere, so the first chance sends one.
    /// </summary>
    /// <param name="player">The player's physics object (<see cref="PhysicsWorld.Player"/> of its world).</param>
    /// <param name="sink">Where the movement messages go (the session's <see cref="IActionApi.Movement"/>, a
    /// <c>MovementAudit</c> around it, or <see cref="NullMovementSink"/> offline).</param>
    /// <param name="options">The player's movement options.</param>
    /// <param name="log">Where the controller reports what it cannot do (category "movement"); null for none.</param>
    /// <exception cref="ArgumentException"><paramref name="player"/> is not its world's player or has no motion table.</exception>
    public PlayerMovementController(PhysicsObj player, IMovementSink sink, MovementOptions options, ILogSink? log = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(options);
        if (!player.IsThePlayer)
        {
            throw new ArgumentException("the controller moves its world's player (PhysicsWorld.SetPlayer)", nameof(player));
        }

        _player = player;
        _motion = player.Motion ?? throw new ArgumentException("the player's physics object has no motion table", nameof(player));
        _sink = sink;
        _log = log ?? NullLogSink.Instance;
        Options = options;
        _timeBetweenPositionEvents = player.World.Compat.PositionEventInterval;
        _now = player.World.CurrentTime;
        _lastSentPositionTime = _now;
        PreviousPosition = player.Position;
    }

    /// <summary>The player's physics object.</summary>
    public PhysicsObj Player => _player;

    /// <summary>The player's motion interpreter.</summary>
    public MotionInterpreter Motion => _motion;

    /// <summary>The options the controller runs with.</summary>
    public MovementOptions Options { get; }

    /// <inheritdoc/>
    public bool RunByDefault => Options.RunByDefault;

    /// <summary>The jump bar (for the UI's power bar: <see cref="JumpCharge.GetJumpPowerLevel"/>).</summary>
    public JumpCharge Jump { get; } = new();

    /// <summary>What was sent and received.</summary>
    public MovementStats Stats { get; } = new();

    /// <summary>Retail <c>controlled_by_server</c>: true until the first movement key, and after <see cref="LoseControlToServer"/>.</summary>
    public bool ControlledByServer { get; private set; } = true;

    /// <summary>
    /// Retail <c>enabled</c>: the interpreter takes keys and sends positions (<c>CommandInterpreter::IsActive</c>, 11.4186
    /// 0x006B3CE0, 11.6096 0x006B4C20). <see cref="Disable"/> and <see cref="Enable"/> change it.
    /// </summary>
    public bool Enabled { get; private set; } = true;

    /// <summary>Retail <c>auto_run</c>.</summary>
    public bool AutoRun { get; private set; }

    /// <summary>Retail <c>hold_run</c>: the run key is down.</summary>
    public bool HoldRun { get; private set; }

    /// <summary>Retail <c>hold_sidestep</c>: the sidestep modifier is down.</summary>
    public bool HoldSidestep { get; private set; }

    /// <summary>
    /// Raised for each message retail's movement code puts in the chat window (autorun on and off, refused motions and
    /// jumps). Handlers run synchronously on the controller's thread.
    /// </summary>
    public event Action<MovementNotice>? Notice;

    /// <inheritdoc/>
    public CellFrame Position => _player.Position;

    /// <inheritdoc/>
    public bool OnGround => _player.HasContact && _player.OnWalkable;

    /// <inheritdoc/>
    /// <remarks>Placed in a cell (not waiting on a placement) and not <see cref="OnGround"/>; an unplaced body is nowhere, not in the air.</remarks>
    public bool InTheAir => _player.Cell is not null && !_player.PlacementPending && !OnGround;

    /// <summary>
    /// What the movement stack believes right now, in one line: the held keys, the interpreter's forward command and speed, the player's velocity and transient
    /// state. A walker that stalls logs it (the walk check's failure, <c>RoutePlay</c>'s stuck warning) so a stall can be told from a wall.
    /// </summary>
    public string DescribeMotion()
    {
        var raw = _motion.Raw;
        var interpreted = _motion.Interpreted;
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"enabled={Enabled} server={ControlledByServer} hold_run={HoldRun} keys(substates={_substates.Count} turns={_turns.Count}) raw(fwd=0x{raw.ForwardCommand:X8} hold={raw.CurrentHoldKey}) interpreted(fwd=0x{interpreted.ForwardCommand:X8} speed={interpreted.ForwardSpeed:F2}) pending={_motion.MotionsPending} moving_to={_player.IsMovingTo} velocity={_player.Velocity} cached={_player.CachedVelocity} transient={_player.TransientState} active={_player.IsActive} in_cell={_player.Cell is not null} placement_pending={_player.PlacementPending} now={_now:F3} last_step={LastStepTime:F3}");
    }

    /// <inheritdoc/>
    public string? MotionText => DescribeMotion();

    /// <inheritdoc/>
    public bool? ServerMoving => ControlledByServer && IsMovingTo;

    /// <summary>The controller's clock (seconds): the latest of the times it was given and the world's time.</summary>
    public double Now => _now;

    /// <summary>
    /// The player's position before the last physics step that ran (for drawing it between 30 Hz steps, owner decision
    /// D26: the renderer interpolates from here to <see cref="Position"/> over <see cref="PreviousStepTime"/> to
    /// <see cref="LastStepTime"/>). Equal to <see cref="Position"/> before the first step.
    /// </summary>
    public CellFrame PreviousPosition { get; private set; }

    /// <summary>When the step before the last one ran (the world's time, seconds); NaN before two steps.</summary>
    public double PreviousStepTime { get; private set; } = double.NaN;

    /// <summary>When the last physics step ran (the world's time, seconds); NaN before the first.</summary>
    public double LastStepTime { get; private set; } = double.NaN;

    /// <summary>
    /// One frame at <paramref name="now"/> (the host's monotonic seconds): <see cref="StepPhysics"/>, then
    /// <see cref="UseTime"/>.
    /// </summary>
    /// <returns>True when physics ran this frame.</returns>
    public bool Tick(double now)
    {
        var ran = StepPhysics(now);
        UseTime();
        return ran;
    }

    /// <summary>
    /// Retail <c>CPhysics::UseTime</c> for the player (11.4186 0x00509950, 11.6096 0x0050A420): when the 30 Hz gate lets
    /// physics run, <c>CPhysicsObj::update_object</c> steps the player.
    /// </summary>
    /// <returns>True when physics ran.</returns>
    public bool StepPhysics(double now)
    {
        Advance(now);
        if (!_player.World.UseTime(now))
        {
            return false;
        }

        PreviousPosition = _player.Position;
        var cellBefore = _player.Cell;
        PreviousStepTime = LastStepTime;
        LastStepTime = _player.World.CurrentTime;
        _player.UpdateObject();
        ReportLanding();
        ReportThinDoorway(cellBefore, PreviousPosition);
        return true;
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::UseTime</c> (11.4186 0x006B3BF0, 11.6096 0x006B4B30): an AutonomousPosition when
    /// <c>ShouldSendPositionEvent</c> says so, then, while the server controls the player, its motions are done and a
    /// movement key (or autorun) is still held, control comes back (<c>TakeControlFromServer</c>).
    /// </summary>
    public void UseTime()
    {
        if (ShouldSendPositionEvent())
        {
            SendPositionEvent();
        }

        WatchServerMoveTo();
        if (Enabled && ControlledByServer && !_motion.MotionsPending && !IsMovingTo
            && (_substates.HasHead || _turns.HasHead || _sidesteps.HasHead || AutoRun))
        {
            TakeControlFromServer();
        }

        ReportUnenforcedRestriction();
        Measure();
    }

    /// <summary>
    /// The longest a server MoveTo or TurnTo of the player is held, in seconds: ACE's <c>defaultMoveToTimeout</c> (15 s, <c>Player_Move.cs:16</c> @95ef426d). ACE's walk-to-use
    /// chain (<c>CreateMoveToChain</c>, <c>Player_Move.cs:37-97</c>) gives up after it (<c>MoveToChain</c>, <c>Player_Move.cs:119-124</c>: the chain stops and the Use's callback runs
    /// unsuccessful, so the client gets a UseDone with no error) and sends the client nothing that ends its MoveTo. Retail's <c>MoveToManager</c> never ends a MoveTo for want of
    /// progress either: <c>HandleMoveToPosition</c> (11.4186 0x00529D80, 11.6096 0x0052A990) and <c>HandleTurnToHeading</c> (0x0052A0C0 / 0x0052ACD0) only count
    /// <c>fail_progress_count</c>, which nothing reads, and <c>UseTime</c> does nothing out of contact or while a target has not reported. Retail's player ends such a walk with a key;
    /// a body nobody is at the keys of would hold it for ever (M5 item 5 F6: the Brawler's walk to Renald, round 2). <c>TODO(m5-moveto-stuck)</c>: the rule is ACE's timeout,
    /// not retail's; retail's server-side end of a player's MoveTo is unread.
    /// </summary>
    public const double ServerMoveToLimitSeconds = 15.0;

    /// <summary>
    /// M6 sprint (run 5, cache/m6/sprint/run5/step3-levelup.log 14:51:06-14:51:21 UTC: a Use of a corpse 23.1 m away let ACE walk her straight at a wall; she made 8.99 m and then
    /// pressed into the wall until the 15 s rule, and every sprint run held 1 or 2 such walks, 15 s each): a server MoveTo (to an object or a position, once initialized and in contact) that
    /// has not closed its distance by <see cref="ServerMoveToProgressMetres"/> for this long is cancelled as a key would end it. Lifestone's own, not retail (retail's <c>MoveToManager</c>
    /// only counts <c>fail_progress_count</c>): longer than a half turn in place before the walk starts, so a MoveTo that turns first is never cut.
    /// </summary>
    public const double ServerMoveToNoProgressSeconds = 3.0;

    /// <summary>The distance a server MoveTo must close to count as progress (<see cref="ServerMoveToNoProgressSeconds"/>; Lifestone's own).</summary>
    public const float ServerMoveToProgressMetres = 0.5f;

    private double _serverMoveToSince = double.NaN;
    private float _serverMoveToBest = float.NaN;
    private double _serverMoveToProgressAt = double.NaN;
    private int _serverMoveToCancels = -1;
    private CellFrame _serverMoveToFrom;

    /// <summary>The server MoveTos the stuck rule ended (<see cref="ServerMoveToLimitSeconds"/>, <c>TODO(m5-moveto-stuck)</c>).</summary>
    public int ServerMoveToStuckEnds { get; private set; }

    /// <summary>
    /// How the last server MoveTo or TurnTo of the player ended, for the runner's line: <c>arrived</c>, <c>cancelled (0xNNNN Name)</c> (retail's own cancels: a new movement,
    /// the target gone or silent, charged too far), <c>not started</c> (never under way: an unknown target does nothing), or <c>stuck</c> (the <see cref="ServerMoveToLimitSeconds"/> rule); null while one is under way or before the first.
    /// </summary>
    public string? LastServerMoveToEnd { get; private set; }

    /// <summary>A server MoveTo or TurnTo arrives (<see cref="LoseControlToServer"/>, before it is unpacked): the watch starts again.</summary>
    private void BeginServerMoveToWatch()
    {
        _serverMoveToSince = _now;
        _serverMoveToCancels = -1; // noted on the first step it is under way: the unpack cancels the MoveTo it replaces (0x36) after this call
        _serverMoveToFrom = _player.Position;
        _serverMoveToBest = float.NaN;
        _serverMoveToProgressAt = _now;
        LastServerMoveToEnd = null;
    }

    /// <summary>
    /// Each step while a server MoveTo is watched: its end is named (<see cref="LastServerMoveToEnd"/>), and one held <see cref="ServerMoveToLimitSeconds"/> is cancelled as retail's
    /// <c>CPhysicsObj::cancel_moveto</c> (11.4186 0x005101F0, 11.6096 0x00510CC0) cancels, with its error 0x36 ActionCancelled, and logged with what held it (<c>TODO(m5-moveto-stuck)</c>).
    /// Control stays with the server until a key is held, as after any MoveTo.
    /// </summary>
    private void WatchServerMoveTo()
    {
        if (double.IsNaN(_serverMoveToSince))
        {
            return;
        }

        var moveTo = _player.Movement?.MoveTo;
        if (!_player.IsMovingTo)
        {
            LastServerMoveToEnd = _serverMoveToCancels < 0
                ? "not started" // never under way: an unknown target does nothing (MovementManager.MoveToObject), or it ended inside its unpack
                : moveTo is not null && moveTo.Cancels != _serverMoveToCancels
                    ? string.Create(CultureInfo.InvariantCulture, $"cancelled (0x{(uint)moveTo.LastCancelReason:X4} {moveTo.LastCancelReason})")
                    : "arrived";
            _serverMoveToSince = double.NaN;
            return;
        }

        if (_serverMoveToCancels < 0)
        {
            _serverMoveToCancels = moveTo?.Cancels ?? 0;
        }

        var held = _now - _serverMoveToSince;
        var noProgress = NoServerMoveToProgress(moveTo);
        if (held < ServerMoveToLimitSeconds && !noProgress)
        {
            return;
        }

        var moved = _serverMoveToFrom.CellId == 0 ? float.NaN : PositionFrames.Distance(_serverMoveToFrom, _player.Position);
        _log.Warn(
            "movement",
            noProgress
                ? string.Create(CultureInfo.InvariantCulture, $"the server's MoveTo closed less than {ServerMoveToProgressMetres:0.0} m on its target in {ServerMoveToNoProgressSeconds:0.0} s (a wall or a body in the way): cancelled as a key would end it (0x36 ActionCancelled), and a MoveToState sent so ACE's walk-to-use chain stops too (m6-moveto-no-progress)")
                : "TODO(m5-moveto-stuck): the server's MoveTo held the player past ACE's MoveTo timeout without ending (retail's MoveToManager counts no-progress checks and never ends on them); cancelled as a key would end it (0x36 ActionCancelled)",
            new LogField("held_s", held.ToString("F1", CultureInfo.InvariantCulture)),
            new LogField("moved_m", moved.ToString("F2", CultureInfo.InvariantCulture)),
            new LogField("type", moveTo?.MovementType.ToString() ?? "none"),
            new LogField("target", moveTo is null ? "none" : $"0x{moveTo.TopLevelObjectId:X8}"),
            new LogField("initialized", moveTo?.Initialized.ToString() ?? "none"),
            new LogField("step", moveTo is { PendingActions.Count: > 0 } ? moveTo.PendingActions[0].Type.ToString() : "none"),
            new LogField("command", $"0x{moveTo?.CurrentCommand ?? 0:X8}"),
            new LogField("fail_progress", (moveTo?.FailProgressCount ?? 0).ToString(CultureInfo.InvariantCulture)),
            new LogField("distance_m", moveTo is null ? "none" : moveTo.GetCurrentDistance().ToString("F2", CultureInfo.InvariantCulture)),
            new LogField("wanted_m", moveTo is null ? "none" : moveTo.MovementParams.DistanceToObject.ToString("F2", CultureInfo.InvariantCulture)),
            new LogField("contact", _player.HasContact.ToString()),
            new LogField("motions_pending", _motion.MotionsPending.ToString()),
            new LogField("obstacle", _player.LastTransitionObstacle.Kind.ToString()),
            new LogField("at", $"0x{_player.Position.CellId:X8} {_player.Position.Frame.Origin}"));
        ServerMoveToStuckEnds++;
        _player.CancelMoveTo();
        SendMovementEvent(); // M6 sprint: as a key press would: ACE's GameActionMoveToState stops the walk-to-use chain (StopExistingMoveToChains), so the Use ends now, not at its own timeout
        LastServerMoveToEnd = "stuck";
        _serverMoveToSince = double.NaN;
    }

    /// <summary>
    /// M6 sprint: true when the watched server MoveTo walks (to an object or a position, initialized, the body in contact) and has not closed its distance by <see cref="ServerMoveToProgressMetres"/>
    /// for <see cref="ServerMoveToNoProgressSeconds"/>. A turn, a MoveTo not yet initialized (its target has not reported) or a body in the air is not judged, and restarts the clock.
    /// </summary>
    private bool NoServerMoveToProgress(MoveToManager? moveTo)
    {
        if (moveTo is not { Initialized: true } || moveTo.MovementType is not (WireMovementType.MoveToObject or WireMovementType.MoveToPosition) || !_player.HasContact)
        {
            _serverMoveToProgressAt = _now;
            return false;
        }

        var distance = moveTo.GetCurrentDistance();
        if (!float.IsFinite(distance))
        {
            _serverMoveToProgressAt = _now;
            return false;
        }

        if (float.IsNaN(_serverMoveToBest) || distance <= _serverMoveToBest - ServerMoveToProgressMetres)
        {
            _serverMoveToBest = distance;
            _serverMoveToProgressAt = _now;
            return false;
        }

        return _now - _serverMoveToProgressAt >= ServerMoveToNoProgressSeconds;
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::ShouldSendPositionEvent</c> (11.4186 0x006B45E0, 11.6096 0x006B5520), at autonomy
    /// level 2 with the interpreter active: after the interval (1.0 s, compared strictly), when the cell or the frame
    /// (<see cref="RetailFrames.FrameIsEqual"/>) differs from the last position sent; before it, when the cell differs or,
    /// in the same cell, the contact plane does (<see cref="RetailFrames.PlaneEquals"/>).
    /// </summary>
    public bool ShouldSendPositionEvent()
    {
        if (!IsActive)
        {
            return false;
        }

        var position = _player.Position;
        if (_timeBetweenPositionEvents + _lastSentPositionTime < _now)
        {
            return _lastSentPosition.CellId != position.CellId || !RetailFrames.FrameIsEqual(_lastSentPosition.Frame, position.Frame);
        }

        return _lastSentPosition.CellId != position.CellId || !RetailFrames.PlaneEquals(_lastSentContactPlane, _player.ContactPlane);
    }

    /// <summary>Retail <c>CommandInterpreter::IsActive</c> (11.4186 0x006B3CE0, 11.6096 0x006B4C20): enabled, with a player.</summary>
    private bool IsActive => Enabled;

    /// <summary>
    /// Retail <c>CPhysicsObj::IsMovingTo</c> (11.4186 0x0050EB10, 11.6096 0x0050F5E0): the server's MoveTo or TurnTo for the
    /// player (an out-of-range Use's approach) is under way; control does not come back while it is.
    /// </summary>
    public bool IsMovingTo => _player.IsMovingTo;

    /// <summary>
    /// Retail <c>CommandInterpreter::SendPositionEvent</c> (11.4186 0x006B4770, 11.6096 0x006B56B0): only while the player
    /// has Contact and OnWalkable and a valid position (<see cref="RetailFrames.PositionIsValid"/>), an AutonomousPosition
    /// with the contact flag set; the time, the position and the contact plane are remembered.
    /// </summary>
    private void SendPositionEvent()
    {
        if (!(_player.HasContact && _player.OnWalkable))
        {
            return;
        }

        if (_sink is IMovementSinkGate { IsOpen: false })
        {
            return; // dropped, not sent: the last position sent stays, so the first frame after the gate opens sends one
        }

        var position = _player.Position;
        if (!RetailFrames.PositionIsValid(position))
        {
            return;
        }

        _sink.AutonomousPosition(position, contact: true);
        _lastSentPositionTime = _now;
        _lastSentPosition = position;
        _lastSentContactPlane = _player.ContactPlane;
        Stats.AutonomousPositionSent++;
        Stats.LastAutonomousPositionTime = _now;
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::SendMovementEvent</c> (11.4186 0x006B4680, 11.6096 0x006B55C0): a MoveToState with
    /// the raw motion state, the position, the contact flag (Contact and OnWalkable) and the standing long jump; the
    /// position timer restarts (the last position sent does not change).
    /// </summary>
    private void SendMovementEvent()
    {
        _sink.MoveToState(_motion.Raw, _player.Position, _player.HasContact && _player.OnWalkable, _motion.StandingLongJump);
        _lastSentPositionTime = _now;
        Stats.MoveToStateSent++;
        Stats.LastMoveToStateTime = _now;
    }

    /// <summary>Moves the controller's clock forward (retail <c>Timer::cur_time</c> is one clock for the whole frame).</summary>
    private void Advance(double now)
    {
        if (now > _now)
        {
            _now = now;
        }

        if (_player.World.CurrentTime > _now)
        {
            _now = _player.World.CurrentTime;
        }
    }

    /// <summary>
    /// The loud half of the physics TODO for housing barriers: the first time the world has let a player into a cell a
    /// barrier guards without retail's check (<see cref="PhysicsWorld.UnenforcedRestrictionEntries"/>), a warning names
    /// the cell. Later entries are only counted on the world.
    /// </summary>
    private void ReportUnenforcedRestriction()
    {
        var world = _player.World;
        if (_loggedUnenforcedRestriction || world.UnenforcedRestrictionEntries == 0)
        {
            return;
        }

        _loggedUnenforcedRestriction = true;
        _log.Warn(
            "movement",
            "TODO(physics): housing barrier not enforced; the player entered a restricted cell retail would refuse until the barrier's house object is loaded",
            new LogField("cell", $"0x{world.LastUnenforcedRestrictionCell:X8}"));
    }

    /// <summary>
    /// The loud half of <c>TODO(physics-landing-velocity)</c> (docs/physics.md section 15): the first time the player lands
    /// from the air (walkable ground again after having left it), a warning says the velocity handling of retail's landing
    /// (<c>CPhysicsObj::HitGround</c>) is not checked against retail. Once per controller.
    /// </summary>
    private void ReportLanding()
    {
        var onGround = OnGround;
        if (onGround && _hasBeenOnGround && !_wasOnGround && !_loggedLanding)
        {
            _loggedLanding = true;
            _log.Warn(
                "movement",
                "TODO(physics-landing-velocity): the player landed; the velocity it carries after a landing (a jump that lands while the route turns showed up to 12 m/s) is not checked against retail CPhysicsObj::HitGround",
                new LogField("cell", $"0x{_player.Position.CellId:X8}"));
        }

        _hasBeenOnGround |= onGround;
        _wasOnGround = onGround;
    }

    /// <summary>
    /// The loud half of <c>TODO(physics-thin-doorway)</c> (docs/physics.md section 15): the first time one physics step
    /// takes the player between the landscape and a room that is not a doorway (no portal onto the landscape), or between
    /// two rooms with no portal between them, the cell that holds the sphere after the step skipped the thin doorway cell.
    /// Whether retail's own player skips it at a run is not measured. Steps longer than 3 m (teleports) are not examined.
    /// </summary>
    private void ReportThinDoorway(PhysCell? before, CellFrame positionBefore)
    {
        var after = _player.Cell;
        if (_loggedThinDoorway || before is null || after is null || before.Id == after.Id)
        {
            return;
        }

        var now = _player.Position;
        if ((positionBefore.CellId >> 16) != (now.CellId >> 16)
            || Vector3.Distance(positionBefore.Frame.Origin, now.Frame.Origin) > 3f)
        {
            return;
        }

        var skipped = (before, after) switch
        {
            (PhysEnvCell from, PhysEnvCell to) => !from.Portals.Any(portal => portal.OtherCellId == to.Id),
            (PhysEnvCell room, _) => !room.Portals.Any(portal => portal.LeadsOutside),
            (_, PhysEnvCell room) => !room.Portals.Any(portal => portal.LeadsOutside),
            _ => false,
        };
        if (!skipped)
        {
            return;
        }

        _loggedThinDoorway = true;
        _log.Warn(
            "movement",
            "TODO(physics-thin-doorway): one physics step took the player across a doorway without the doorway cell holding it; whether retail's own player skips the thin cell at a run is not measured",
            new LogField("from", $"0x{before.Id:X8}"),
            new LogField("to", $"0x{after.Id:X8}"));
    }

    /// <summary>The cadence and constraint measurements of <see cref="Stats"/>.</summary>
    private void Measure()
    {
        var position = _player.Position;
        var moved = _hasPreviousTick
            && (position.CellId != _previousTickPosition.CellId || !RetailFrames.FrameIsEqual(position.Frame, _previousTickPosition.Frame));
        _previousTickPosition = position;
        _hasPreviousTick = true;
        if (moved && OnGround)
        {
            var gap = _now - _lastSentPositionTime;
            if (gap > Stats.MaxGroundMovingGapSeconds)
            {
                Stats.MaxGroundMovingGapSeconds = gap;
            }
        }

        if (_player.HasContact && _player.Positions?.Constraint is { IsConstrained: true } constraint)
        {
            var factor = ServerPositionPolicy.ConstraintFactor(constraint.Offset, constraint.StartDistance, constraint.MaxDistance);
            if (factor < Stats.MinConstraintFactor)
            {
                Stats.MinConstraintFactor = factor;
            }
        }
    }

    private void Raise(MovementNoticeKind kind, WeenieError error = WeenieError.None, string? text = null) =>
        Notice?.Invoke(new MovementNotice(kind, error, text));
}
