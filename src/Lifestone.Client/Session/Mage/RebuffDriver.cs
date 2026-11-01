using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Mage;

/// <summary>Where a <see cref="RebuffDriver"/> is.</summary>
public enum RebuffDriverState
{
    /// <summary>Not enabled (<c>--buff-auto off</c>, or <see cref="RebuffDriver.Enabled"/> cleared): the driver does nothing.</summary>
    Off,

    /// <summary>Enabled and no line is due (or none was due at the last check).</summary>
    Idle,

    /// <summary>A line is due and a guard holds the pass back; <see cref="RebuffDriver.WaitingFor"/> names the guard.</summary>
    Waiting,

    /// <summary>A pass is under way (the combat mode change, the cycle's lines, or the change back).</summary>
    Running,

    /// <summary>The last pass failed or cast nothing; no pass starts until <see cref="RebuffDriver.NextCheckAt"/> (<see cref="RebuffDriver.BackoffSeconds"/> after it ended).</summary>
    BackingOff,
}

/// <summary>
/// The re-buff driver (M4 4.1 lane a; closes <c>TODO(m3-buff-rebuff-driver)</c>, M3 review 2.1-R10): the caller of <see cref="BuffCycle.RebuffDue"/> and <see cref="BuffCycle.StartRebuff"/> that the auto-buff goal needs.
/// While <see cref="Enabled"/> it asks, every <see cref="BuffOptions.AutoCheckSeconds"/> of the session clock, which lines have less than <see cref="BuffOptions.RebuffMarginSeconds"/> left; when a line is due and no
/// guard holds it back it starts one <b>unforced</b> re-buff pass (<c>StartRebuff(now, forced: false)</c>: the forced pass is the check's, never the driver's) and ticks the cycle to the pass's end, the way <c>BuffScenario</c> does.
/// </summary>
/// <remarks>
/// <para><b>The host</b> calls <see cref="Tick"/> with the session clock's time, once per frame or per scenario step. Between checks a tick is a comparison and allocates nothing; a check lists the due lines
/// (one list, once per <see cref="BuffOptions.AutoCheckSeconds"/>); a waiting driver reads its guards every tick (they allocate nothing), so a pass starts the moment the last guard clears. The driver owns the combat
/// mode of its own pass: the cycle needs the magic mode (it fails loudly otherwise), so the driver enters it (an awaited <see cref="IActionApi.SetCombatMode"/>, which <see cref="CombatModeGuard"/> refuses without a caster) and
/// puts the player back in peace afterwards when it was the driver that changed it.</para>
/// <para><b>The guards</b>, each a named waiting reason (<see cref="WaitingFor"/>), logged once per change: <c>not in the world yet</c> (the session is not in the world, or the player is still arriving or materialising),
/// <c>in portal space</c> (a teleport is announced or arrived), <c>dead</c> (the current health is 0), <c>another action holds a lane</c> (the Action API has an action queued or in flight), <c>the buff cycle is running another pass</c> (a host steps the same cycle), and <c>in a fight</c> (combat mode
/// melee or missile, or a defender notice, GameEvent 0x01B2, within <see cref="FightQuietSeconds"/> of the session clock). A pass already started is not interrupted by a guard (the cycle cannot be aborted): the guards decide
/// the start only. A guard that holds for <see cref="GuardWarnSeconds"/> while a line is due logs a warning naming the line and the time left (and again every <see cref="GuardWarnSeconds"/> while it lasts): a re-buff is
/// never skipped silently.</para>
/// <para><b>A failed pass backs off</b> <see cref="BackoffSeconds"/> and logs its reason (<see cref="LastFailure"/>): a cycle that ended <see cref="BuffStatus.Failed"/>, a refused combat mode, a pass that missed lines
/// (<see cref="BuffResult.Misses"/>) or one that cast nothing although a line was due. Without the back-off a pass that cannot succeed would start again at every check and spend the mage's mana for nothing.</para>
/// <para><b>The seed first</b> (M4 4.1 fix, review R2). While the cycle's armour ledger is unseeded (<see cref="BuffCycle.SeedPending"/>: a new login, nothing appraised yet) an armour line has no entry and would count due, so the driver counts
/// nothing and enters no combat mode until the seed has run: it appraises the worn pieces and restores the store's saved seconds (<see cref="BuffCycle.StartSeed"/>, <see cref="BuffCycle.StepSeed"/>; the same guards apply, the pass counter does not move).
/// A pass that then casts nothing because nothing was due is Idle, not a failure.</para>
/// <para><b>Lanes.</b> <c>TODO(m4-buff-driver-lane-holds)</c>: the lane guard reads the Action API's queued and in-flight counts (<see cref="ActionApi.QueuedCount"/>, <see cref="ActionApi.InFlightCount"/>), which miss a lane that is only held (a cast
/// not yet ended by its UseDone, a heal's Ready hold); the pass's own first action then waits in its lane's queue, which is safe, and a view whose <see cref="IActionApi"/> is not the real one reports no lane busy.</para>
/// </remarks>
public sealed class RebuffDriver : IDisposable
{
    /// <summary>The seconds a failed pass keeps the driver from starting another.</summary>
    public const double BackoffSeconds = 60;

    /// <summary>The seconds a guard may hold while a line is due before the driver warns (and the interval of the warnings after it).</summary>
    public const double GuardWarnSeconds = 120;

    /// <summary>The seconds after a defender notice during which the player counts as in a fight.</summary>
    public const double FightQuietSeconds = 10;

    /// <summary>The waiting reason before the world is entered.</summary>
    public const string GuardNotInWorld = "not in the world yet";

    /// <summary>The waiting reason during a teleport.</summary>
    public const string GuardPortalSpace = "in portal space";

    /// <summary>The waiting reason while the player is dead.</summary>
    public const string GuardDead = "dead";

    /// <summary>The waiting reason while another action holds a lane.</summary>
    public const string GuardLane = "another action holds a lane";

    /// <summary>The waiting reason while a pass the driver did not start (the buff scenario's, a host's) is running on the same cycle.</summary>
    public const string GuardCycle = "the buff cycle is running another pass";

    /// <summary>The waiting reason in a fight.</summary>
    public const string GuardFight = "in a fight";

    private const uint HealthMaxId = 1;

    private enum Phase
    {
        None,
        Seed,
        ModeIn,
        SwapIn,
        Cycle,
        ModeOut,
        SwapOut,
    }

    private readonly BuffCycle _cycle;
    private readonly IActionApi _actions;
    private readonly IGameSessionView _session;
    private readonly BuffOptions _options;
    private readonly ILogSink _log;
    private readonly IDisposable? _damageSubscription;
    private readonly CasterSwap? _swap;

    private Phase _phase;
    private ActionHandle? _pending;
    private bool _changedMode;
    private bool _swapped;
    private bool _swapRan;
    private bool _resultFresh;
    private bool _enabled;
    private double _lastDamageAt = double.NegativeInfinity;
    private double _waitingSince;
    private double _lastWarnAt = double.NegativeInfinity;
    private string? _loggedGuard;
    private string? _endReason;

    /// <summary>The driver of <paramref name="cycle"/> over a session.</summary>
    /// <param name="cycle">The buff cycle it drives (no other host steps it while a pass of the driver runs).</param>
    /// <param name="actions">The Action API the combat mode change goes through (the cycle has its own).</param>
    /// <param name="session">The session view: the read model for the guards and the bus for the damage notices.</param>
    /// <param name="options">The buff options: the margin, <see cref="BuffOptions.Auto"/> (the initial <see cref="Enabled"/>) and <see cref="BuffOptions.AutoCheckSeconds"/>.</param>
    /// <param name="log">Where the driver logs (<c>[buff] ...</c>).</param>
    /// <param name="swap">The fighter's caster swap (M4 P5a C1b), or null for the mage, who wields its wand: with one, a pass that finds no caster in the hand wields it first (the weapon and its ammunition to the pack) and puts the weapon back after the pass, instead of the plain combat-mode change.</param>
    public RebuffDriver(BuffCycle cycle, IActionApi actions, IGameSessionView session, BuffOptions options, ILogSink log, CasterSwap? swap = null)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        _cycle = cycle;
        _actions = actions;
        _session = session;
        _options = options;
        _log = log;
        _swap = swap;
        _enabled = options.Auto;
        State = _enabled ? RebuffDriverState.Idle : RebuffDriverState.Off;
        _damageSubscription = session.Events.Subscribe<CombatEvent>(OnCombat);
        _log.Info(BuffCycle.LogCategory, string.Create(CultureInfo.InvariantCulture, $"re-buff driver: {(_enabled ? "on" : "off")}, check every {options.AutoCheckSeconds:0.#} s, margin {options.RebuffMarginSeconds:0.#} s{(options.DriverUnattended ? " (the autopilot's own: the fight guard does not hold it, M5 7 F23)" : string.Empty)}"));
    }

    /// <summary>The fighter's caster swap this driver runs around a pass (null for the mage).</summary>
    public CasterSwap? Swap => _swap;

    /// <summary>The buff profile the driver's cycle casts (M4 P5a: the mage's <see cref="ArenaProfile.Buffs"/>, or a fighter's <c>FighterProfile</c>).</summary>
    public IBuffProfile Profile => _cycle.Profile;

    /// <summary>True while the driver starts passes (<c>--buff-auto on</c>). Clearing it lets a pass already under way finish and starts no other.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            _log.Info(BuffCycle.LogCategory, "re-buff driver: " + (value ? "on" : "off"));
            if (_phase == Phase.None)
            {
                ResetWaiting();
                State = value ? RebuffDriverState.Idle : RebuffDriverState.Off;
            }
        }
    }

    /// <summary>Where the driver is.</summary>
    public RebuffDriverState State { get; private set; }

    /// <summary>The guard that holds a due line back while <see cref="State"/> is <see cref="RebuffDriverState.Waiting"/> (a <c>Guard...</c> text), else null.</summary>
    public string? WaitingFor { get; private set; }

    /// <summary>The passes the driver started (a pass counts when its first action is sent, finished or not).</summary>
    public int Passes { get; private set; }

    /// <summary>What the last pass the driver ran left (also after a failure: the part done), or null before the first.</summary>
    public BuffResult? LastResult { get; private set; }

    /// <summary>Why the last pass failed (and the driver backed off), or null when the last pass succeeded or none ran.</summary>
    public string? LastFailure { get; private set; }

    /// <summary>The session time of the next check (and, while <see cref="RebuffDriverState.BackingOff"/>, the end of the back-off).</summary>
    public double NextCheckAt { get; private set; }

    /// <summary>Advances the driver: the next check when it is due, the guards while a line waits, the pass while one runs. Cheap between checks and allocation-free.</summary>
    /// <param name="now">The session clock's time.</param>
    public void Tick(double now)
    {
        if (_phase != Phase.None)
        {
            Advance(now);
            return;
        }

        if (!_enabled)
        {
            State = RebuffDriverState.Off;
            return;
        }

        if (State == RebuffDriverState.BackingOff)
        {
            if (now < NextCheckAt)
            {
                return;
            }

            _log.Info(BuffCycle.LogCategory, "re-buff driver: the back-off is over");
            State = RebuffDriverState.Idle;
        }

        if (State == RebuffDriverState.Off)
        {
            State = RebuffDriverState.Idle;
        }

        if (now >= NextCheckAt)
        {
            Check(now);
            return;
        }

        if (State == RebuffDriverState.Waiting)
        {
            // Between checks a waiting driver watches only its guards; when they clear it checks at once (a line may have stopped being due meanwhile), so the pass starts the moment they do.
            var guard = Guard(now);
            if (guard is null)
            {
                Check(now);
            }
            else
            {
                Hold(guard, now);
            }
        }
    }

    /// <summary>Unsubscribes from the bus.</summary>
    public void Dispose() => _damageSubscription?.Dispose();

    private void Check(double now)
    {
        NextCheckAt = now + _options.AutoCheckSeconds;
        if (_cycle.SeedPending)
        {
            // Review R2: before the login's appraisal seed and the store's restore have run, an armour line has no entry and would count due. Nothing is counted (and no combat mode is entered) until the ledger is seeded.
            var seedGuard = Guard(now);
            if (seedGuard is null)
            {
                BeginSeed(now);
                return;
            }

            if (State != RebuffDriverState.Waiting)
            {
                State = RebuffDriverState.Waiting;
                _waitingSince = now;
                _lastWarnAt = double.NegativeInfinity;
            }

            Hold(seedGuard, now, "(the armour memory is not seeded yet)");
            return;
        }

        var due = _cycle.RebuffDue(now, _options.RebuffMarginSeconds);
        if (due.Count == 0)
        {
            if (State == RebuffDriverState.Waiting)
            {
                _log.Info(BuffCycle.LogCategory, "re-buff driver: no line is due any more; the wait ends");
            }

            ResetWaiting();
            State = RebuffDriverState.Idle;
            return;
        }

        var guard = Guard(now);
        if (guard is null)
        {
            Begin(now);
            return;
        }

        if (State != RebuffDriverState.Waiting)
        {
            State = RebuffDriverState.Waiting;
            _waitingSince = now;
            _lastWarnAt = double.NegativeInfinity;
        }

        Hold(guard, now, due[0]);
    }

    /// <summary>Records the guard (logged once per change) and warns when it has held for <see cref="GuardWarnSeconds"/>.</summary>
    private void Hold(string guard, double now, string? dueLine = null)
    {
        State = RebuffDriverState.Waiting;
        if (!string.Equals(guard, _loggedGuard, StringComparison.Ordinal))
        {
            _loggedGuard = guard;
            WaitingFor = guard;
            _log.Info(BuffCycle.LogCategory, "re-buff driver: a line is due and the pass waits: " + guard);
        }

        if (now - _waitingSince >= GuardWarnSeconds && now - _lastWarnAt >= GuardWarnSeconds)
        {
            _lastWarnAt = now;
            var line = dueLine ?? (_cycle.SeedPending ? "(the armour memory is not seeded yet)" : _cycle.RebuffDue(now, _options.RebuffMarginSeconds) is { Count: > 0 } due ? due[0] : "(none)");
            var left = _cycle.MinRemainingSeconds(now);
            _log.Warn(
                BuffCycle.LogCategory,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"re-buff driver: {line} is due and has waited {now - _waitingSince:0} s for the guard '{guard}' (the least time left on an active line: {(double.IsNaN(left) ? "no line is active" : left.ToString("0", CultureInfo.InvariantCulture) + " s")}); nothing is skipped, the pass starts when the guard clears"));
        }
    }

    private void ResetWaiting()
    {
        WaitingFor = null;
        _loggedGuard = null;
        _lastWarnAt = double.NegativeInfinity;
    }

    // ------------------------------------------------------------------------------------------------ the guards

    /// <summary>The first guard that holds, or null when the pass may start. Reads only; allocates nothing.</summary>
    private string? Guard(double now)
    {
        var state = _session.State;
        if (state.Phase != SessionPhase.InWorld)
        {
            return GuardNotInWorld;
        }

        switch (state.Self.Travel)
        {
            case SelfTravel.NotInWorld or SelfTravel.Arriving or SelfTravel.Materialising:
                return GuardNotInWorld;
            case SelfTravel.TeleportAnnounced or SelfTravel.TeleportArrived:
                return GuardPortalSpace;
        }

        var qualities = state.Self.Qualities;
        if (qualities.IsKnown && qualities.TryGetVital(HealthMaxId, out var health) && health.Current == 0)
        {
            return GuardDead;
        }

        if (_cycle.Status == BuffStatus.Running)
        {
            return GuardCycle;
        }

        if (_actions is ActionApi api && (api.QueuedCount > 0 || api.InFlightCount > 0))
        {
            return GuardLane;
        }

        var mode = qualities.CombatMode;
        if (!_options.DriverUnattended && (mode is CombatModes.Melee or CombatModes.Missile || now - _lastDamageAt < FightQuietSeconds)) // M5 7 F23: the autopilot's own station is never held by the fight guard
        {
            return GuardFight;
        }

        return null;
    }

    private void OnCombat(in CombatEvent e)
    {
        if (e.Kind == CombatEventKind.DefenseHit)
        {
            _lastDamageAt = _session.Clock.Now;
        }
    }

    // ------------------------------------------------------------------------------------------------ a pass

    /// <summary>Seeds the armour ledger: the worn pieces appraised and the saved seconds restored, no mode change, no cast, no pass counted (review R2).</summary>
    private void BeginSeed(double now)
    {
        ResetWaiting();
        if (!_cycle.StartSeed(now))
        {
            State = RebuffDriverState.Idle;
            return;
        }

        _log.Info(BuffCycle.LogCategory, "re-buff driver: the armour memory is seeded first (appraisal and restore); no line is counted due and no combat mode is entered until it is");
        State = RebuffDriverState.Running;
        _phase = Phase.Seed;
    }

    private void Begin(double now)
    {
        ResetWaiting();
        State = RebuffDriverState.Running;
        Passes++;
        _changedMode = false;
        _swapped = false;
        _swapRan = false;
        _resultFresh = false;
        _endReason = null;
        var mode = _session.State.Self.Qualities.CombatMode;
        if (mode == CombatModes.Magic && (_swap is null || _swap.CasterWielded))
        {
            StartCycle(now);
            return;
        }

        if (_swap is not null)
        {
            // M4 P5a C1b: a fighter has no caster in the hand, and ACE (Player_Combat.cs:872-880 at ACE 95ef426d: the magic mode without a wielded caster is reverted to peace) and the combat-mode guard (D23) refuse the magic mode without one: the swap puts the weapon aside, wields the caster and enters the mode.
            _log.Info(BuffCycle.LogCategory, "re-buff driver: a pass starts; the caster swap first (the weapon to the pack, the caster wielded, the magic combat mode)");
            _swapped = true;
            _swapRan = true;
            _swap.Begin(SwapDirection.In, now);
            _phase = Phase.SwapIn;
            return;
        }

        _log.Info(BuffCycle.LogCategory, "re-buff driver: a pass starts; the magic combat mode first");
        _changedMode = true;
        _pending = _actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
        _phase = Phase.ModeIn;
    }

    private void StartCycle(double now)
    {
        if (!_cycle.StartRebuff(now, forced: false))
        {
            End(now, "the cycle did not start: a pass is already running");
            return;
        }

        _phase = Phase.Cycle;
    }

    private void Advance(double now)
    {
        switch (_phase)
        {
            case Phase.Seed:
                if (_cycle.StepSeed() != BuffStatus.Running)
                {
                    _phase = Phase.None;
                    State = RebuffDriverState.Idle;
                    _log.Info(BuffCycle.LogCategory, "re-buff driver: the armour memory is seeded; the lines are counted now");
                    if (_enabled)
                    {
                        Check(now);
                    }
                    else
                    {
                        State = RebuffDriverState.Off;
                    }
                }

                break;
            case Phase.ModeIn:
                if (_pending is { Outcome: { } entered })
                {
                    _pending = null;
                    if (!entered.Succeeded)
                    {
                        _changedMode = false;
                        End(now, "the magic combat mode was not entered: " + Describe(entered));
                        return;
                    }

                    StartCycle(now);
                }

                break;
            case Phase.SwapIn:
                switch (_swap!.Step(now))
                {
                    case SwapStatus.Done:
                        StartCycle(now);
                        break;
                    case SwapStatus.Failed:
                        _swapped = false; // the swap restored the weapon and peace itself
                        End(now, _swap.Failure ?? "swap:unknown");
                        break;
                }

                break;
            case Phase.SwapOut:
                switch (_swap!.Step(now))
                {
                    case SwapStatus.Done:
                        End(now, _endReason);
                        break;
                    case SwapStatus.Failed:
                        _log.Warn(BuffCycle.LogCategory, "re-buff driver: the caster swap back failed (" + _swap.Failure + "): " + _swap.Detail);
                        End(now, _endReason ?? _swap.Failure);
                        break;
                }

                break;
            case Phase.Cycle:
                switch (_cycle.Step(now))
                {
                    case BuffStatus.Failed:
                        LastResult = _cycle.Result;
                        _resultFresh = true;
                        EndAfterMode(now, "the pass failed: " + (_cycle.Result?.Failure ?? "no reason given"));
                        break;
                    case BuffStatus.Done:
                        LastResult = _cycle.Result;
                        _resultFresh = true;
                        EndAfterMode(now, Shortfall(_cycle.Result, now));
                        break;
                }

                break;
            case Phase.ModeOut:
                if (_pending is { Outcome: { } left })
                {
                    _pending = null;
                    if (!left.Succeeded)
                    {
                        _log.Warn(BuffCycle.LogCategory, "re-buff driver: the player was left in the magic combat mode: " + Describe(left)); // report-only, as the scenario's
                    }

                    End(now, _endReason);
                }

                break;
        }
    }

    /// <summary>The reason a finished pass counts as failed, or null when it did what a pass is for.</summary>
    private string? Shortfall(BuffResult? result, double now)
    {
        if (result is null)
        {
            return "the pass ended with no result";
        }

        if (result.Misses.Count > 0)
        {
            return "the pass missed " + result.Misses.Count.ToString(CultureInfo.InvariantCulture) + " line(s): " + string.Join(';', result.Misses);
        }

        if (result.Cast == 0 && _cycle.RebuffDue(now, _options.RebuffMarginSeconds).Count > 0)
        {
            return "the pass cast nothing although a line was due";
        }

        if (result.Cast == 0)
        {
            // Review R2: nothing was due once the pass looked (the seed or a restore removed the due lines): a pass that casts nothing for that reason is not a failure.
            _log.Info(BuffCycle.LogCategory, "re-buff driver: the pass found no line due and cast nothing; that is not a failure");
        }

        return null;
    }

    /// <summary>The pass ended: puts the player back in peace first when the driver changed the mode, then <see cref="End"/>.</summary>
    private void EndAfterMode(double now, string? reason)
    {
        _endReason = reason;
        if (_swapped)
        {
            _swap!.Begin(SwapDirection.Out, now);
            _phase = Phase.SwapOut;
            return;
        }

        if (_changedMode)
        {
            _pending = _actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Automation);
            _phase = Phase.ModeOut;
            return;
        }

        End(now, reason);
    }

    private void End(double now, string? reason)
    {
        _phase = Phase.None;
        _pending = null;
        _changedMode = false;
        if (_swapRan && _resultFresh && LastResult is { } passed)
        {
            LastResult = passed with { SwapFields = _swap!.Fields, Fields = passed.Fields + " " + _swap.Fields }; // the pass's result gains the swap's fields once the weapon is back
        }

        _swapped = false;
        _swapRan = false;
        _resultFresh = false;

        _endReason = null;
        if (reason is not null)
        {
            LastFailure = reason;
            State = RebuffDriverState.BackingOff;
            NextCheckAt = now + BackoffSeconds;
            _log.Warn(BuffCycle.LogCategory, string.Create(CultureInfo.InvariantCulture, $"re-buff driver: pass {Passes} did not succeed: {reason}; backing off {BackoffSeconds:0} s"));
        }
        else
        {
            LastFailure = null;
            State = RebuffDriverState.Idle;
            NextCheckAt = now + _options.AutoCheckSeconds;
            _log.Info(BuffCycle.LogCategory, "re-buff driver: pass " + Passes.ToString(CultureInfo.InvariantCulture) + " done: " + LastResult?.Fields);
        }

        if (!_enabled)
        {
            State = RebuffDriverState.Off;
        }
    }

    private static string Describe(in ActionOutcome outcome) =>
        outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail);
}
