using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.PortalSpace;

/// <summary>How the tunnel ends when the player logs off (m2-plan.md D28).</summary>
public enum LogOffTunnelPolicy
{
    /// <summary>
    /// Retail: the SmartBox flag is off during a logoff, so the machine Ends in state 3 and runs to the exit; retail
    /// survives only because the character list tears the UI down during state 4 (<c>--retail-logoff-tunnel</c>).
    /// </summary>
    Retail,

    /// <summary>The documented fix and default: stay in state 3 until <see cref="PortalSpaceMachine.Reset"/> (the character list arrives).</summary>
    HoldUntilCharacterList,
}

/// <summary>
/// The portal-space state machine (m2-log-in.md §4.5, §4.6, §4.8): one <see cref="Update"/> per frame, in retail order,
/// gives the <see cref="PortalFrame"/> to present.
/// </summary>
/// <remarks>
/// <para>Re-expresses, by function name (step PS7), <c>gmSmartBoxUI::UseTime</c> (11.4186 0x004D6E30, 11.6096
/// 0x004D7A90), <c>BeginTeleportAnimation</c> (11.4186 0x004D6300, 11.6096 0x004D6F60), <c>EndTeleportAnimation</c>
/// (11.4186 0x004D65A0, 11.6096 0x004D7200), <c>CPlayerSystem::SetTeleportInProgress</c> (11.4186 0x0055D580, 11.6096
/// 0x0055E2A0) and the timing of <c>CPlayerSystem::RequestLogOff</c> (11.4186 0x00562DD0, 11.6096 0x00563B70).</para>
/// <para><b>One frame</b> (<see cref="Update"/>), in retail's order:</para>
/// <list type="number">
/// <item><b>A logoff that is due</b> (<see cref="LogOff"/>): Begin(1), the player busy, the logoff started.</item>
/// <item><b>The SmartBox flag</b> (<see cref="ITeleportProgress.InProgress"/>) against the player's busy flag
/// (<see cref="PlayerTeleportInProgress"/>): when the SmartBox flag turned on, the player becomes busy and Begin(2) runs;
/// when it is off while the player is still busy, End runs, but only in state 3. A teleport that completes during state 2
/// therefore ends on the frame after state 3 begins, and the whole sequence still plays.</item>
/// <item><b>States 1 and 6</b> enable the selection indicator. <b>States 2 to 5</b> show the tunnel on their first frame
/// if it is hidden (the spin restarts from frame 1, the camera is placed, the world is hidden, the selection indicator is
/// disabled), then roll the camera: each segment eases (<see cref="AnimLevel"/>) from where the last one ended to a target
/// drawn from [0°, 360°] over a duration drawn from [0.6, 1.8] s (duration first; <c>RandDouble</c>), linearly and without
/// wrapping, and each new segment re-posts the portal notice.</item>
/// <item><b>The zoom</b> (<see cref="PortalZoom"/>): states 1 and 5 collapse, states 2 and 6 expand; both set the focal
/// override.</item>
/// <item><b>The transitions</b>, in this order: 4 → 5 (<see cref="ExitWindow.HoldEnds"/> on the tunnel's frame), 1 → 2,
/// 2 → 3, 5 → 6 (the override keeps the collapsed focal; the tunnel hides and stops, the world shows, the exit whoosh plays)
/// and 6 → 0 (LoginComplete, the player no longer busy, the override cleared, the "teleport occurred" flag consumed). The
/// 1-second states end when at least 1.0 s has passed.</item>
/// <item><b>In state 0</b>, a teleport that completed without the animation (<see cref="ITeleportProgress.ConsumeTeleportOccurred"/>)
/// sends LoginComplete at once.</item>
/// </list>
/// <para><b>Begin(state)</b> remembers the game's focal distance (<see cref="GameFocal"/>) only when the machine was off,
/// resets the current focal to it, zeroes the roll's segment (its start time, duration, start and end angles, not the
/// current angle, so the first tunnel frame starts a segment from 0°), enters the state with a new stamp and plays the
/// enter whoosh. <b>End</b> (only when not off) enters state 4 with a new stamp and clears the focal override.</para>
/// <para><b>The tunnel.</b> The machine restarts and stops <see cref="Tunnel"/> itself when it shows and hides it (retail
/// does both inside <c>UseTime</c>), and reads its <see cref="IPortalTunnel.CurrentFrame"/> for the exit window. It never
/// advances it: the director does, after <see cref="Update"/>, while the tunnel shows (retail steps creature-mode objects
/// in <c>CreatureMode::Render</c>, after <c>UseTime</c>), so the exit window sees the frame of the tunnel's last step,
/// taken on an earlier drawn frame (retail's <c>CPhysicsObj::update_position</c> steps at most once per 1/30 s).</para>
/// <para><b>Lifestone's logoff fix</b> (D28): with <see cref="LogOffTunnelPolicy.HoldUntilCharacterList"/> the machine
/// does not End a logoff's tunnel; it stays in state 3 until <see cref="Reset"/>. <see cref="LogOffTunnelPolicy.Retail"/>
/// restores retail, which Ends in state 3 and runs on to state 0, sending LoginComplete, unless the character list
/// resets the machine first.</para>
/// <para><b>LoginComplete</b> is cued only at the end of state 6 (once per Begin) and in state 0 when a completed update
/// is consumed (once per completed update), so it goes out at most once per teleport and never before state 6
/// ends.</para>
/// <para><b>Time</b> is the host's monotonic real seconds, advanced once per frame (never the Dereth clock). Allocation-free
/// per frame and deterministic for a given clock, random source, tunnel and progress. Frame thread only.</para>
/// </remarks>
public sealed class PortalSpaceMachine
{
    /// <summary>The length of the zoom states 1, 2, 5 and 6 (retail's 1.0: the divisor of t and the end test).</summary>
    public const double TransitionSeconds = 1.0;

    /// <summary>The shortest roll segment, in seconds (<c>RandDouble(0.6, 1.8)</c>).</summary>
    public const double RollMinSeconds = 0.6;

    /// <summary>The longest roll segment, in seconds.</summary>
    public const double RollMaxSeconds = 1.8;

    /// <summary>The largest roll target, in degrees (<c>RandDouble(0.0, 360.0)</c>).</summary>
    public const double RollMaxDegrees = 360.0;

    /// <summary>The portal notice each roll segment posts (retail's literal).</summary>
    public const string PortalNotice = "In Portal Space - Please Wait...";

    /// <summary>The text type of <see cref="PortalNotice"/> (<c>ECM_UI::SendNotice_DisplayStringInfo(0x1A, …)</c>).</summary>
    public const uint PortalNoticeTextType = 0x1A;

    private TeleportAnimState _state;
    private double _stateStart;
    private float _gameFocal;
    private float _currentFocal;
    private double _rollStartTime;
    private double _rollDuration;
    private double _rollStartAngle;
    private double _rollEndAngle;
    private double _rollAngle;
    private bool _tunnelShown;
    private bool _worldHidden;
    private bool _selectionIndicator = true;
    private bool _overrideOn;
    private float _overrideFocal;
    private bool _busy;
    private bool _logOffStarted;
    private PortalCues _cues;

    /// <summary>Creates the machine in <see cref="TeleportAnimState.Off"/>.</summary>
    /// <param name="tunnel">The tunnel object (its current frame decides the exit window).</param>
    /// <param name="random">The roll's random source (retail <c>RandDouble</c>).</param>
    /// <param name="gameFocal">
    /// The game camera's focal distance f_game = 1 / tan(vertical FOV / 2) (<see cref="PortalZoom.FocalFromVerticalFov"/>),
    /// read when a sequence begins from <see cref="TeleportAnimState.Off"/>.
    /// </param>
    /// <param name="logOffPolicy">How a logoff's tunnel ends (default: hold until the character list).</param>
    public PortalSpaceMachine(
        IPortalTunnel tunnel,
        IPortalRandom random,
        Func<float> gameFocal,
        LogOffTunnelPolicy logOffPolicy = LogOffTunnelPolicy.HoldUntilCharacterList)
    {
        Tunnel = tunnel ?? throw new ArgumentNullException(nameof(tunnel));
        Random = random ?? throw new ArgumentNullException(nameof(random));
        GameFocal = gameFocal ?? throw new ArgumentNullException(nameof(gameFocal));
        LogOffPolicy = logOffPolicy;
    }

    /// <summary>The tunnel object.</summary>
    public IPortalTunnel Tunnel { get; }

    /// <summary>The roll's random source.</summary>
    public IPortalRandom Random { get; }

    /// <summary>Reads the game camera's focal distance.</summary>
    public Func<float> GameFocal { get; }

    /// <summary>How a logoff's tunnel ends.</summary>
    public LogOffTunnelPolicy LogOffPolicy { get; }

    /// <summary>Receives the machine's warnings (the ignored player-killer logoff delay). The director sets it to its own log.</summary>
    public ILogSink Log { get; set; } = NullLogSink.Instance;

    /// <summary>The logoff timing (<see cref="RequestLogOff"/> schedules it; <see cref="Update"/> begins it).</summary>
    public LogOffSchedule LogOff { get; } = new();

    /// <summary>The current state.</summary>
    public TeleportAnimState State => _state;

    /// <summary>The player is busy teleporting (retail <c>CPlayerSystem::teleportInProgress</c>).</summary>
    public bool PlayerTeleportInProgress => _busy;

    /// <summary>A logoff's tunnel has begun and the machine has not been reset since.</summary>
    public bool LogOffStarted => _logOffStarted;

    /// <summary>When the current state began (retail <c>teleportTransitionStartTime</c>).</summary>
    public double StateStartTime => _stateStart;

    /// <summary>The game's focal distance captured by the last Begin from <see cref="TeleportAnimState.Off"/> (retail <c>gameVDist</c>).</summary>
    public float CapturedGameFocal => _gameFocal;

    /// <summary>The zoom's current focal distance (retail <c>teleportCurVDist</c>).</summary>
    public float CurrentFocal => _currentFocal;

    /// <summary>The tunnel viewport is shown.</summary>
    public bool TunnelVisible => _tunnelShown;

    /// <summary>The world is drawn (<c>SmartBox::hidden</c> is clear).</summary>
    public bool WorldVisible => !_worldHidden;

    /// <summary>The current roll segment's start time, duration, start angle and end angle (retail <c>teleportRotation*</c>).</summary>
    public (double StartTime, double Duration, double StartAngle, double EndAngle) RollSegment =>
        (_rollStartTime, _rollDuration, _rollStartAngle, _rollEndAngle);

    /// <summary>
    /// The player asked to log off at <paramref name="now"/> (the session layer has already sent CharacterLogOff and
    /// printed "Logging off..."): the world collapses 3 s later (<c>CPlayerSystem::RequestLogOff</c>).
    /// </summary>
    /// <param name="now">The request time, in the machine's clock.</param>
    /// <param name="playerKillerDelay">
    /// Retail's extra 20 s when a check on the player's weenie holds (probably player-killer status). Unverified, so off
    /// by default (D33): a true value is logged as a TODO and not applied.
    /// </param>
    public void RequestLogOff(double now, bool playerKillerDelay)
    {
        LogOff.Request(now, playerKillerDelay);
        if (playerKillerDelay)
        {
            Log.Warn(
                "portal",
                "TODO(portal): retail adds 20 s to a logoff when a check on the player's weenie holds (CPlayerSystem::RequestLogOff, 11.4186 0x00562DD0, 11.6096 0x00563B70); the check is unverified, so the delay is not applied (D33)",
                new LogField("due", LogOff.DueTime));
        }
    }

    /// <summary>Runs one frame of <c>gmSmartBoxUI::UseTime</c> at <paramref name="now"/> and returns what to present.</summary>
    public PortalFrame Update(double now, ITeleportProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _cues = PortalCues.None;

        // The logoff trigger (CPlayerSystem::logOffRequested and logOffRequestTime < now).
        if (LogOff.IsDue(now))
        {
            Begin(TeleportAnimState.WorldFadeOut, now);
            SetBusy(true);
            LogOff.Start();
            _logOffStarted = true;
        }

        // SmartBox::teleport_in_progress against CPlayerSystem::teleportInProgress.
        var inProgress = progress.InProgress;
        if (inProgress != _busy)
        {
            if (inProgress)
            {
                SetBusy(true);
                Begin(TeleportAnimState.Tunnel, now);
            }
            else if (_state == TeleportAnimState.InTunnel && !HoldsLogOff)
            {
                End(now);
            }
        }

        if (_state != TeleportAnimState.Off)
        {
            if (_state is TeleportAnimState.WorldFadeOut or TeleportAnimState.Arriving)
            {
                SetSelectionIndicator(true);
            }

            if (_state is >= TeleportAnimState.Tunnel and <= TeleportAnimState.LeavingTunnel)
            {
                if (!_tunnelShown)
                {
                    ShowTunnel();
                }

                Roll(now);
            }

            Zoom(now);
            Advance(now, progress);
        }
        else if (progress.ConsumeTeleportOccurred())
        {
            _cues |= PortalCues.SendLoginComplete;
        }

        return Frame(now);
    }

    /// <summary>
    /// Back to <see cref="TeleportAnimState.Off"/> with nothing pending (the UI mode changed, e.g. the character list
    /// arrived): the tunnel hidden and stopped, the world shown, the override cleared, the selection indicator enabled,
    /// the player no longer busy, any logoff request forgotten and the roll at 0. No cue announces it: a presenter follows
    /// the next frame's steady fields (<see cref="PortalFrame.TunnelVisible"/>, <see cref="PortalFrame.WorldVisible"/>,
    /// <see cref="PortalFrame.Busy"/>, <see cref="PortalFrame.Lens"/>), as it does when it starts mid-sequence.
    /// </summary>
    public void Reset()
    {
        if (_tunnelShown)
        {
            Tunnel.Stop();
        }

        _state = TeleportAnimState.Off;
        _stateStart = 0.0;
        _currentFocal = _gameFocal;
        _rollStartTime = 0.0;
        _rollDuration = 0.0;
        _rollStartAngle = 0.0;
        _rollEndAngle = 0.0;
        _rollAngle = 0.0;
        _tunnelShown = false;
        _worldHidden = false;
        _selectionIndicator = true;
        _overrideOn = false;
        _overrideFocal = 0f;
        _busy = false;
        _logOffStarted = false;
        LogOff.Clear();
    }

    /// <summary>
    /// True while the Hold policy (D28) keeps a logoff's tunnel up in state 3, waiting for the character list
    /// (<see cref="Reset"/>). <see cref="PortalSpaceDirector"/> warns every 10 s while this holds (D30's rule, wave 3).
    /// </summary>
    public bool IsHoldingLogOff => HoldsLogOff && _state == TeleportAnimState.InTunnel;

    /// <summary>The Hold policy keeps a logoff's tunnel in state 3.</summary>
    private bool HoldsLogOff => _logOffStarted && LogOffPolicy == LogOffTunnelPolicy.HoldUntilCharacterList;

    /// <summary><c>gmSmartBoxUI::BeginTeleportAnimation</c>.</summary>
    private void Begin(TeleportAnimState state, double now)
    {
        if (_state == TeleportAnimState.Off)
        {
            _gameFocal = GameFocal();
        }

        _currentFocal = _gameFocal;
        _rollStartTime = 0.0;
        _rollDuration = 0.0;
        _rollStartAngle = 0.0;
        _rollEndAngle = 0.0;
        _state = state;
        _stateStart = now;
        _cues |= PortalCues.PlayEnterPortal;
    }

    /// <summary><c>gmSmartBoxUI::EndTeleportAnimation</c>.</summary>
    private void End(double now)
    {
        if (_state == TeleportAnimState.Off)
        {
            return;
        }

        _state = TeleportAnimState.Holding;
        _stateStart = now;
        _overrideOn = false;
        _overrideFocal = 0f;
    }

    /// <summary><c>CPlayerSystem::SetTeleportInProgress</c>: the busy count changes only when the flag does.</summary>
    private void SetBusy(bool busy)
    {
        if (_busy == busy)
        {
            return;
        }

        _busy = busy;
        _cues |= busy ? PortalCues.BusyOn : PortalCues.BusyOff;
    }

    /// <summary><c>VividTargetIndicator::SetEnabled</c>, cued only when it changes.</summary>
    private void SetSelectionIndicator(bool enabled)
    {
        if (_selectionIndicator == enabled)
        {
            return;
        }

        _selectionIndicator = enabled;
        _cues |= enabled ? PortalCues.SelectionIndicatorOn : PortalCues.SelectionIndicatorOff;
    }

    /// <summary>The first frame of a tunnel state while the tunnel is hidden.</summary>
    private void ShowTunnel()
    {
        SetSelectionIndicator(false);
        Tunnel.Restart();
        _tunnelShown = true;
        _cues |= PortalCues.RestartTunnelSpin | PortalCues.PlaceTunnelCamera | PortalCues.ShowTunnel;
        if (!_worldHidden)
        {
            _worldHidden = true;
            _cues |= PortalCues.HideWorld;
        }
    }

    /// <summary>The roll of states 2 to 5.</summary>
    private void Roll(double now)
    {
        if (_rollDuration + _rollStartTime > now)
        {
            var level = AnimLevel.Level((float)((now - _rollStartTime) / _rollDuration));
            _rollAngle = ((_rollEndAngle - _rollStartAngle) * (level * AnimLevel.Unit)) + _rollStartAngle;
            return;
        }

        _rollAngle = _rollEndAngle;
        _rollStartTime = now;
        _rollDuration = Random.RandDouble(RollMinSeconds, RollMaxSeconds);
        _rollStartAngle = _rollAngle;
        _rollEndAngle = Random.RandDouble(0.0, RollMaxDegrees);
        _cues |= PortalCues.PostPortalNotice;
    }

    /// <summary>The zoom of states 1, 2, 5 and 6.</summary>
    private void Zoom(double now)
    {
        var t = (now - _stateStart) / TransitionSeconds;
        switch (_state)
        {
            case TeleportAnimState.WorldFadeOut or TeleportAnimState.LeavingTunnel:
                SetOverride(_currentFocal = PortalZoom.Collapse(_gameFocal, t));
                break;
            case TeleportAnimState.Tunnel or TeleportAnimState.Arriving:
                SetOverride(_currentFocal = PortalZoom.Expand(_gameFocal, t));
                break;
        }
    }

    /// <summary>The transitions, in retail's order.</summary>
    private void Advance(double now, ITeleportProgress progress)
    {
        // Retail reads the tunnel's frame only once the 2 s hold is over.
        if (_state == TeleportAnimState.Holding
            && now - _stateStart >= ExitWindow.MinHoldSeconds
            && ExitWindow.HoldEnds(now - _stateStart, Tunnel.CurrentFrame))
        {
            Enter(TeleportAnimState.LeavingTunnel, now);
        }

        if (_state == TeleportAnimState.WorldFadeOut && now - _stateStart >= TransitionSeconds)
        {
            Enter(TeleportAnimState.Tunnel, now);
        }

        if (_state == TeleportAnimState.Tunnel && now - _stateStart >= TransitionSeconds)
        {
            Enter(TeleportAnimState.InTunnel, now);
        }

        if (_state == TeleportAnimState.LeavingTunnel && now - _stateStart >= TransitionSeconds)
        {
            SetOverride(_currentFocal);
            _tunnelShown = false;
            _worldHidden = false;
            Tunnel.Stop();
            _cues |= PortalCues.HideTunnel | PortalCues.ShowWorld | PortalCues.StopTunnelSpin | PortalCues.PlayExitPortal;
            Enter(TeleportAnimState.Arriving, now);
        }

        if (_state == TeleportAnimState.Arriving && now - _stateStart >= TransitionSeconds)
        {
            _stateStart = now - TransitionSeconds;
            _cues |= PortalCues.SendLoginComplete;
            SetBusy(false);
            _state = TeleportAnimState.Off;
            _overrideOn = false;
            _overrideFocal = 0f;
            _logOffStarted = false;
            progress.ConsumeTeleportOccurred();
        }
    }

    private void Enter(TeleportAnimState state, double now)
    {
        _state = state;
        _stateStart = now;
    }

    /// <summary><c>SmartBox::SetOverrideFovDistance(1, f)</c>.</summary>
    private void SetOverride(float focal)
    {
        _overrideOn = true;
        _overrideFocal = focal;
    }

    private PortalFrame Frame(double now) => new(
        _state,
        _state == TeleportAnimState.Off ? 0.0 : now - _stateStart,
        _tunnelShown,
        !_worldHidden,
        _overrideOn ? new PortalLens(_overrideFocal, Overridden: true) : new PortalLens(GameFocal(), Overridden: false),
        _rollAngle,
        _cues,
        _busy);
}
