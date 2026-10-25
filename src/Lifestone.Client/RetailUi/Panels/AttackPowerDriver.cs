using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>Where the power bar's notices go (retail's <c>CM_Character::SendNotice_BeginPowerbar</c>, <c>SendNotice_SetPowerbarLevel</c> 11.6096 0x006A36D0 and <c>SendNotice_FinishPowerbar</c>): the combat window's meter and the power bar window.</summary>
public interface IPowerBarNotices
{
    /// <summary>A bar of <paramref name="mode"/> begins (<c>gmPowerbarUI::RecvNotice_BeginPowerbar</c>).</summary>
    void Begin(PowerBarMode mode);

    /// <summary>The bar of <paramref name="mode"/> shows <paramref name="level"/> (0..1).</summary>
    void SetLevel(PowerBarMode mode, float level);

    /// <summary>The bar of <paramref name="mode"/> ends.</summary>
    void Finish(PowerBarMode mode);
}

/// <summary>
/// The attack side of <c>ClientCombatSystem</c> that drives the power bar and sends the combat window's attack (M4 1.3 s1; docs/retail-ui.md section 23.8). Engine-free: the combat window gives it the notices, the clock, the selection and
/// the slider; the Action API gets the attack.
/// </summary>
/// <remarks>
/// <para><b>The retail flow, re-expressed</b> (11.6096 addresses; the 11.4186 decompile was read, nothing is copied). A mouse down on a height button is <c>SetRequestedAttackHeight</c> (0x0056D640) and so
/// <c>StartAttackRequest</c> (0x0056CD90): with no attackable selection the chat line "You must select a valid combat target before attacking" and nothing else; otherwise the request is in progress, the power asked for is 1.0
/// and <c>AttemptStartBuildingAttack</c> (0x0056C850) begins a bar: power bar mode 1 (the combat window's own meter 0x10000050) for the basic combat UI, mode 2 (the bottom window 0x10000044) under the AdvancedCombatUI option, the
/// level 0 and the build clock started. <c>UseTime</c> (0x0056CF40) raises the level every frame: <c>GetPowerBarLevel</c> (0x0056BB20) is the seconds since the build began, clamped to 0..1 (1.0 s for a full bar; retail
/// divides by 0.8 while the player's stance is DualWieldCombat: <c>JumpCharge.GetPowerBarLevel</c> already holds that divisor; this driver does not read the stance, <c>TODO(m4-build-rate-dualwield)</c>). The click is <c>EndAttackRequest</c> (0x0056CE30): the request ends; the power is the slider's value or the bar's level,
/// whichever is greater (the advanced UI: the bar's level); with a reply still pending the attack waits for it. Otherwise it is sent at once when the bar already is at the slider (or the advanced UI is on, or a repeat is running),
/// and else the bar goes on charging and <c>UseTime</c> sends the attack the frame the level reaches the power (<c>ExecuteAttack</c> 0x0056C8C0: <c>CM_Combat::Event_TargetedMeleeAttack</c> or
/// <c>Event_TargetedMissileAttack</c> with the power, by the combat mode; the attack waits for the server's answer).</para>
/// <para><b>What the server does with it</b> (the live order, item 1.2): one send gives a whole auto-repeated run. <c>HandleCommenceAttackEvent</c> (0x0056BA60, 0x01B8 and the swing) shows the bar at the requested power and ends the
/// build; <c>HandleAttackDoneEvent</c> (0x0056D250, 0x01A7): {0} (a swing over) keeps a repeating run's bar and starts an automatic build from 0 up to the requested power (it stops there); any other code (0x0036) ends the run, hides the
/// bar (<c>HidePowerBar</c> 0x0056B5F0: mode 1 sets the meter to 0, mode 2 finishes the window) and forgets the mode. A click made while a reply is pending is kept and made when the 0x01A7 arrives. Retail then also sends a
/// CancelAttack on a non-zero code while its repeat flag is up (<c>AbortAutomaticAttack</c> 0x0056BBD0); Lifestone does not (<c>TODO(m4-attack-done-cancel)</c>: the server has ended the run). Retail sends a second attack at the slider's power
/// when the power held was greater (<c>EndAttackRequest</c>); Lifestone sends the first only (<c>TODO(m4-attack-second-send)</c>).</para>
/// <para><b>Other sources.</b> Retail has no sender but its own window; Lifestone's hunt loop and tools send through the Action API as Host or Automation. <see cref="AttackRequested"/> begins the bar for them as
/// <c>HandleCommenceAttackEvent</c> does for a reply: the mode by the option, an automatic build from 0 up to the power sent, and a run that repeats until a 0x01A7 with a code. The AutoRepeatAttack option box is ghosted
/// (<c>TODO(m3-combat-options)</c>), so a run is taken as repeating: ACE repeats for this character whatever the option says.</para>
/// <para>Not read: <c>PlayerInReadyPosition</c> (not mapped to 11.6096; the build never waits for the player's motions, <c>TODO(m4-ready-position)</c>) and <c>ObjectIsAttackable</c> (0x0056B340; <c>TODO(m3-attackable-rule)</c>: any selection counts).</para>
/// </remarks>
public sealed class AttackPowerDriver : IDisposable
{
    /// <summary>The seconds a full bar takes (<c>GetPowerBarLevel</c>).</summary>
    public const double BuildSeconds = 1.0;

    /// <summary>Retail's line for an attack with nothing selected (<c>StartAttackRequest</c>, <c>ExecuteAttack</c>).</summary>
    public const string NoTargetText = "You must select a valid combat target before attacking";

    private readonly IGameSessionView _session;
    private readonly IPowerBarNotices _notices;
    private readonly Func<double> _clock;
    private readonly Func<uint> _selected;
    private readonly Func<float> _uiPower;
    private readonly Func<bool> _advanced;
    private readonly Action<string>? _say;
    private readonly Action<bool>? _tickWanted;
    private readonly Action<string>? _todo;
    private readonly IDisposable _requested;
    private readonly IDisposable _changed;
    private double _start;
    private bool _building;
    private bool _automatic;
    private bool _requestInProgress;
    private bool _pending;
    private bool _repeating;

    // True for a run this driver began (ActionSource.Ui); the slider re-send of HandleAttackDoneEvent (0x0056D250) belongs to the combat system's own run only (1.3-R1).
    private bool _ownRun;
    private bool _queued;
    private bool _sending;
    private float _queuedPower;
    private AttackHeight _queuedHeight;
    private ActionHandle? _handle;

    /// <summary>A driver over <paramref name="session"/>. <paramref name="clock"/> is the UI manager's time in seconds; <paramref name="selected"/> the selected object (0: none); <paramref name="uiPower"/> the slider (0..1).</summary>
    public AttackPowerDriver(IGameSessionView session, IPowerBarNotices notices, Func<double> clock, Func<uint> selected, Func<float> uiPower, Func<bool> advanced, Action<string>? say, Action<bool>? tickWanted, Action<string>? todo = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _notices = notices ?? throw new ArgumentNullException(nameof(notices));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _selected = selected ?? throw new ArgumentNullException(nameof(selected));
        _uiPower = uiPower ?? throw new ArgumentNullException(nameof(uiPower));
        _advanced = advanced ?? throw new ArgumentNullException(nameof(advanced));
        _say = say;
        _tickWanted = tickWanted;
        _todo = todo;
        _requested = session.Events.Subscribe<AttackRequested>(OnAttackRequested);
        _changed = session.Events.Subscribe<AttackChanged>(OnAttackChanged);
    }

    /// <summary>The bar in use (<c>powerBarMode</c>), <see cref="PowerBarMode.Undefined"/> when none.</summary>
    public PowerBarMode Mode { get; private set; }

    /// <summary>The level last shown (<c>latestPowerBarLevel</c>).</summary>
    public float LatestLevel { get; private set; }

    /// <summary>The attack height asked for (<c>requestedAttackHeight</c>; medium at first).</summary>
    public AttackHeight RequestedHeight { get; private set; } = AttackHeight.Medium;

    /// <summary>The power the attack will be (or was) sent with (<c>requestedAttackPower</c>).</summary>
    public float RequestedPower { get; private set; }

    /// <summary>True while a press is held (<c>attackRequestInProgress</c>).</summary>
    public bool RequestInProgress => _requestInProgress;

    /// <summary>True while the bar charges (<c>buildInProgress</c>); the clock then needs <see cref="Tick"/> every frame.</summary>
    public bool Building => _building;

    /// <summary>True from the send to the 0x01A7 that answers it (<c>attackServerResponsePending</c>).</summary>
    public bool ResponsePending => _pending;

    /// <summary>How many attacks this driver sent itself (the window's clicks).</summary>
    public int Sends { get; private set; }

    /// <summary>The level the bar would show now (<c>GetPowerBarLevel</c>): seconds since the build began, 0..1; 0 when no build runs.</summary>
    public float Level()
    {
        if (!_building)
        {
            return 0f;
        }

        return (float)Math.Clamp((_clock() - _start) / BuildSeconds, 0.0, 1.0);
    }

    /// <summary><c>SetRequestedAttackHeight</c> (0x0056D640), a mouse down on a height button: remembers the height and starts the request when it changed or none is in progress.</summary>
    public void PressHeight(AttackHeight height)
    {
        var changed = RequestedHeight != height;
        RequestedHeight = height;
        if (changed || !_requestInProgress)
        {
            StartAttackRequest();
        }
    }

    /// <summary>
    /// <c>EndAttackRequest(height, USE_POWER_BAR_LEVEL)</c> (0x0056CE30), the click on a height button. A click with no press before it (an automated click through the element message) starts the request first, as the press would
    /// have; retail does nothing then (<c>TODO(m4-click-without-press)</c>).
    /// </summary>
    public void ReleaseHeight(AttackHeight height)
    {
        if (!_requestInProgress)
        {
            _todo?.Invoke("TODO(m4-click-without-press): a click on an attack button came with no mouse down before it; the request starts now, as the press would have (retail's EndAttackRequest does nothing without one)");
            RequestedHeight = height;
            StartAttackRequest();
            if (!_requestInProgress)
            {
                return; // nothing selected: the line was said
            }
        }

        RequestedHeight = height;
        EndAttackRequest(-1f);
    }

    /// <summary>One frame of <c>UseTime</c> (0x0056CF40): the level rises; a build that reached its power sends the attack or stops.</summary>
    public void Tick()
    {
        if (_building && Mode is PowerBarMode.Combat or PowerBarMode.Advanced)
        {
            var level = Level();
            if (_requestInProgress || _advanced() || level < RequestedPower)
            {
                SetLevel(level);
            }
            else
            {
                SetLevel(Math.Min(RequestedPower, level));
                if (_automatic)
                {
                    _building = false;
                }
                else
                {
                    ExecuteAttack(RequestedHeight);
                }
            }
        }

        if (_requestInProgress && !_building && !_pending)
        {
            AttemptStartBuildingAttack();
        }

        if (_handle is { IsCompleted: true, Outcome: { } outcome } && outcome.Kind is ActionOutcomeKind.Refused or ActionOutcomeKind.NotImplemented or ActionOutcomeKind.Cancelled or ActionOutcomeKind.DryRun or ActionOutcomeKind.TimedOut)
        {
            // The send never reached the wire (or was never answered): no 0x01A7 will end this, so the bar ends here.
            _handle = null;
            _pending = false;
            _repeating = false;
            HidePowerBar();
        }

        _tickWanted?.Invoke(_building || _handle is not null);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _requested.Dispose();
        _changed.Dispose();
    }

    private void StartAttackRequest()
    {
        if (!_advanced() && _selected() == 0)
        {
            _say?.Invoke(NoTargetText);
            return;
        }

        _requestInProgress = true;
        RequestedPower = 1f;
        _automatic = false;
        AttemptStartBuildingAttack();
    }

    private void AttemptStartBuildingAttack()
    {
        if (_building)
        {
            return;
        }

        Mode = _advanced() ? PowerBarMode.Advanced : PowerBarMode.Combat;
        _notices.Begin(Mode);
        _building = true;
        _start = _clock();
        SetLevel(0f);
        _tickWanted?.Invoke(true);
    }

    private void EndAttackRequest(float power)
    {
        if (!_requestInProgress)
        {
            return;
        }

        _requestInProgress = false;
        var level = Level();
        var slider = _uiPower();
        if (power < 0f)
        {
            RequestedPower = _advanced() ? level : slider >= level ? slider : level;
        }
        else
        {
            RequestedPower = power;
        }

        if (_pending)
        {
            _queued = true;
            _queuedPower = RequestedPower;
            _queuedHeight = RequestedHeight;
        }
        else if (_advanced() || slider <= level || _repeating)
        {
            ExecuteAttack(RequestedHeight);
            if (!_advanced() && RequestedPower > slider)
            {
                _todo?.Invoke("TODO(m4-attack-second-send): retail sends a second attack at the slider's power after one made with more; Lifestone sends the first only");
            }
        }
    }

    private void ExecuteAttack(AttackHeight height)
    {
        _building = false;
        var target = _selected();
        var mode = _session.State.Self.Qualities.CombatMode;
        ActionHandle? handle = null;
        _sending = true;
        try
        {
            if (target != 0 && mode == CombatModes.Melee)
            {
                handle = _session.Actions.Attack(target, height, RequestedPower, ActionSource.Ui);
            }
            else if (target != 0 && mode == CombatModes.Missile)
            {
                handle = _session.Actions.MissileAttack(target, height, RequestedPower, ActionSource.Ui);
            }
        }
        finally
        {
            _sending = false;
        }

        if (target == 0)
        {
            _say?.Invoke(NoTargetText);
        }

        if (handle is not null)
        {
            Sends++;
            _handle = handle;
            _repeating = true;
            _ownRun = true;
            _pending = true;
        }

        if (!_pending)
        {
            HidePowerBar();
        }

        _tickWanted?.Invoke(_building || _handle is not null);
    }

    // A send from any other source (the hunt loop, a tool): the bar shows the attack as HandleCommenceAttackEvent does for a reply, charging up to the power sent.
    private void OnAttackRequested(in AttackRequested e)
    {
        if (e.Source == ActionSource.Ui && (_handle is not null || _sending))
        {
            return; // this driver's own send: the bar is begun already
        }

        if (_pending && Mode != PowerBarMode.Undefined)
        {
            return; // a run is already shown (the new power is queued at the server)
        }

        RequestedPower = e.Power;
        _pending = true;
        _repeating = true;
        _ownRun = false; // a Host or Automation run: the bar is shown and driven, never re-sent at the slider
        _requestInProgress = false;
        Mode = _advanced() ? PowerBarMode.Advanced : PowerBarMode.Combat;
        _notices.Begin(Mode);
        StartPowerBarBuild(automatic: true);
    }

    private void OnAttackChanged(in AttackChanged e)
    {
        switch (e.State)
        {
            case AttackSequenceState.Started:
                HandleCommenceAttack();
                break;
            case AttackSequenceState.SwingOver:
                HandleAttackDone(0);
                break;
            case AttackSequenceState.Done:
                HandleAttackDone(e.LastWeenieError);
                break;
        }
    }

    // HandleCommenceAttackEvent (0x0056BA60): the swing (or 0x01B8) starts; with no press held the bar shows the requested power and the build ends.
    private void HandleCommenceAttack()
    {
        _pending = true;
        if (_requestInProgress)
        {
            return;
        }

        if (Mode == PowerBarMode.Undefined)
        {
            Mode = _advanced() ? PowerBarMode.Advanced : PowerBarMode.Combat;
            _notices.Begin(Mode);
        }

        if (RequestedPower <= 0f)
        {
            RequestedPower = _uiPower();
        }

        SetLevel(RequestedPower);
        _building = false;
        _tickWanted?.Invoke(_handle is not null);
    }

    // HandleAttackDoneEvent (0x0056D250).
    private void HandleAttackDone(uint code)
    {
        _pending = false;
        _handle = null;
        if (code != 0 && _repeating)
        {
            _repeating = false; // AbortAutomaticAttack, without the CancelAttack (TODO(m4-attack-done-cancel))
        }

        if (!_requestInProgress && !_advanced() && _repeating && _ownRun && Math.Abs(RequestedPower - _uiPower()) > 0.01f)
        {
            RequestedPower = _uiPower();
            ExecuteAttack(RequestedHeight);
        }

        if (!_repeating)
        {
            _building = false;
            HidePowerBar();
        }
        else if (!_requestInProgress)
        {
            StartPowerBarBuild(automatic: true);
        }

        if (_requestInProgress && !_building && !_pending)
        {
            AttemptStartBuildingAttack();
        }

        if (_queued)
        {
            _queued = false;
            var power = _queuedPower;
            RequestedHeight = _queuedHeight;
            StartAttackRequest();
            if (_requestInProgress)
            {
                _requestInProgress = false;
                RequestedPower = power;
                ExecuteAttack(RequestedHeight);
            }
        }

        _tickWanted?.Invoke(_building || _handle is not null);
    }

    // StartPowerBarBuild (0x0056BAF0).
    private void StartPowerBarBuild(bool automatic)
    {
        if (Mode == PowerBarMode.Undefined)
        {
            return;
        }

        _building = true;
        _automatic = automatic;
        _start = _clock();
        SetLevel(0f);
        _tickWanted?.Invoke(true);
    }

    // HidePowerBar (0x0056B5F0): mode 1 sets the combat meter to 0, any other finishes its bar; the mode is forgotten.
    private void HidePowerBar()
    {
        _building = false;
        if (Mode == PowerBarMode.Combat)
        {
            _notices.SetLevel(PowerBarMode.Combat, 0f);
        }
        else if (Mode != PowerBarMode.Undefined)
        {
            _notices.Finish(Mode);
        }

        Mode = PowerBarMode.Undefined;
        LatestLevel = 0f;
    }

    private void SetLevel(float level)
    {
        _notices.SetLevel(Mode, level);
        LatestLevel = level;
    }
}
