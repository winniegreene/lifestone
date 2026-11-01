using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Fighter;

/// <summary>Which half of the swap a <see cref="CasterSwap"/> runs.</summary>
public enum SwapDirection
{
    /// <summary>The weapon and its ammunition to the pack, the caster wielded, the magic combat mode entered.</summary>
    In,

    /// <summary>Peace, the caster to the pack, the weapon (and its ammunition) wielded again.</summary>
    Out,
}

/// <summary>Where a <see cref="CasterSwap"/> is.</summary>
public enum SwapStatus
{
    /// <summary>No half has been begun, or the last one ended and nothing runs.</summary>
    Idle,

    /// <summary>A half is under way: step again.</summary>
    Running,

    /// <summary>The half reached its goal (<see cref="CasterSwap.Seconds"/> and <see cref="CasterSwap.Fields"/> are set).</summary>
    Done,

    /// <summary>A step failed (<see cref="CasterSwap.Failure"/> names it, <c>swap:&lt;step&gt;</c>); a failed <see cref="SwapDirection.In"/> has already put the weapon back and the player in peace as far as the server allowed.</summary>
    Failed,
}

/// <summary>
/// The fighters' caster swap (M4 P5a C1b): how a Brawler or an Archer, who wields a weapon and no caster, casts a buff pass or a heal. ACE puts a player in the magic combat mode only with a wielded caster
/// (<c>Player_Combat.cs</c> 872-880 sends the player back to NonCombat without one; <c>Creature_Equipment.cs</c> 141-144 finds it in the Held slot), and a Held item needs the main hand free
/// (<c>CheckWeaponCollision</c>, <c>Player_Inventory.cs:1878-2056</c>, the Held branch 1979-1997, at ACE 95ef426d: a Held item collides with a wielded melee weapon or launcher); the client's <see cref="CombatModeGuard"/> refuses the mode without a caster (D23: a character never
/// enters a combat mode the wielded item forbids; the guard is unchanged). So the fighter's kit keeps a caster in the pack (<see cref="FighterRole.Caster"/>) and this machine moves it into the hand for the cast and back.
/// </summary>
/// <remarks>
/// <para><b>In</b> (<see cref="SwapDirection.In"/>): the wielded ammunition to the pack (<c>unwield-ammo</c>), the wielded launcher or melee weapon to the pack (<c>unwield-weapon</c>), the caster wielded in the Held slot
/// (<c>wield-caster</c>), the magic combat mode (<c>magic-mode</c>). <b>Out</b>: peace (<c>peace</c>), the caster to the pack (<c>unwield-caster</c>), the weapon wielded again (<c>wield-weapon</c>), then its ammunition
/// (<c>wield-ammo</c>). What was wielded is recorded when the In half begins; when nothing was (a half-done earlier swap) the kit's own weapon is wielded instead, so the end state is the template's.</para>
/// <para>Every step is one <see cref="IActionApi"/> action waited for (the wield and unwield trackers confirm on the server's message, GameEvent 0x0023 and the move to the pack; a refusal is a ServerError), then the read model is looked at again to
/// choose the next step: nothing is assumed. A step that confirms while the read model still shows the old state waits <see cref="SettleSeconds"/> and then fails; each half is bounded by <see cref="MaxHalfSeconds"/> and
/// <see cref="MaxSteps"/>. A failure is named <c>swap:&lt;step&gt;</c> and never retried. A failed In half then runs the Out half as a restore (the same steps, logged), so the weapon is back in hand and the player in peace at the end of
/// a refused <c>wield-caster</c>; the status is <see cref="SwapStatus.Failed"/> after it, with the first failure as <see cref="Failure"/>.</para>
/// <para><b>The busy wait</b> (M4 fresh-gate fix, gate round 1, as M4 P5b fix FC-a1 did for <see cref="RecallHome"/>): ACE refuses an item move sent into the player's busy chain (<c>Player.IsBusy</c>), and the fresh Brawler's
/// unwield, sent 30 ms after the vitals keeper's fourth Mana Potion, was refused so. Before each action the swap waits while <see cref="IActionApi.BusyReason"/> names a busy chain (a potion's Ready on the Heal lane, a move's
/// chain), at most <see cref="BusyWaitSeconds"/> (<c>swap:busy-wait</c> past it, nothing sent), then sends once; a refusal after that is still the step's failure, never retried.</para>
/// <para><b>Source</b> (M5 1 fix B, review 1-R7): every send of the swap (the wields, the unwields and the mode changes) is <see cref="ActionSource.Automation"/>, so the kill switch and dry run reach a swap as they reach the casts around it. A switch armed
/// between two steps leaves the swap where it stood (a refused step is a failure, named; the restore it then tries is refused too): the keeper and the recall freeze or skip their restore under the switch (K4), the harm-heal script runs its swap out and ends Failed.</para>
/// <para>The mage wields its wand already and never swaps: <see cref="For"/> gives a swap only to a template whose kit holds a caster in the pack.</para>
/// </remarks>
public sealed class CasterSwap
{
    /// <summary>The log category of the swap's lines.</summary>
    public const string LogCategory = "swap";

    /// <summary>The seconds the read model may lag a confirmed step before the swap fails it.</summary>
    public const double SettleSeconds = 3.0;

    /// <summary>The seconds one half may take (a wield or unwield is a second or two on ACE).</summary>
    public const double MaxHalfSeconds = 60.0;

    /// <summary>The most actions one half may send (a loud bound against a loop that never reaches its goal).</summary>
    public const int MaxSteps = 12;

    /// <summary>The most seconds the swap waits for <see cref="IActionApi.BusyReason"/> to clear before an action (<see cref="RecallHome.BusyWaitSeconds"/>'s bound).</summary>
    public const double BusyWaitSeconds = RecallHome.BusyWaitSeconds;

    private const EquipMask MeleeSlots = EquipMask.MeleeWeapon | EquipMask.TwoHanded;
    private const EquipMask MissileSlots = EquipMask.MissileWeapon;
    private const EquipMask AmmoSlot = EquipMask.MissileAmmo;
    private const EquipMask HeldSlot = EquipMask.Held;

    private readonly IActionApi _actions;
    private readonly GameState _state;
    private readonly FighterKit _kit;
    private readonly ILogSink _log;
    private readonly List<uint> _scratch = [];
    private readonly List<(uint Guid, EquipMask Location)> _recorded = [];
    private readonly List<string> _sent = [];

    private SwapDirection _direction;
    private bool _restoring;
    private ActionHandle? _pending;
    private string _pendingStep = string.Empty;
    private string? _lastKey;
    private double _settleSince;
    private double _halfStart;
    private int _halfSteps;
    private double _seconds;
    private string? _firstFailure;
    private string? _firstDetail;
    private double _busySince = double.NaN;

    /// <summary>A swap over an Action API and a read model for the fighter whose kit is <paramref name="kit"/>.</summary>
    /// <param name="actions">The Action API the wields, unwields and combat-mode changes go through.</param>
    /// <param name="state">The read model.</param>
    /// <param name="kit">The fighter's kit: the caster's class and the weapon the Out half falls back to.</param>
    /// <param name="log">Where the swap logs.</param>
    public CasterSwap(IActionApi actions, GameState state, FighterKit kit, ILogSink log)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _kit = kit ?? throw new ArgumentNullException(nameof(kit));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        if (!kit.HasCaster)
        {
            throw new ArgumentException("the kit holds no caster: the template cannot swap (the Hunter has no magic; the mage wields its wand)", nameof(kit));
        }
    }

    /// <summary>The swap for a fighter template, or null when the template has no caster in its kit (the Hunter).</summary>
    /// <param name="template">The template, or null.</param>
    /// <param name="actions">The Action API.</param>
    /// <param name="state">The read model.</param>
    /// <param name="log">The log.</param>
    public static CasterSwap? For(FighterTemplate? template, IActionApi actions, GameState state, ILogSink log) =>
        template is { Kit.HasCaster: true } ? new CasterSwap(actions, state, template.Kit, log) : null;

    /// <summary>The swap for the character named <paramref name="characterName"/>, or null for the mage (it wields its wand), the Hunter and any other character.</summary>
    /// <param name="characterName">The character's name as the read model holds it, or null.</param>
    /// <param name="actions">The Action API.</param>
    /// <param name="state">The read model.</param>
    /// <param name="log">The log.</param>
    public static CasterSwap? ForCharacter(string? characterName, IActionApi actions, GameState state, ILogSink log) =>
        For(FighterTemplate.ForName(characterName), actions, state, log);

    /// <summary>Where the swap is.</summary>
    public SwapStatus Status { get; private set; }

    /// <summary>The half under way or last run.</summary>
    public SwapDirection Direction => _direction;

    /// <summary>The first failure as <c>swap:&lt;step&gt;</c> (<c>swap:wield-caster</c>), or null. Cleared by the next <see cref="SwapDirection.In"/> half.</summary>
    public string? Failure { get; private set; }

    /// <summary>The sentence behind <see cref="Failure"/> (the outcome's kind and detail), or null.</summary>
    public string? Detail { get; private set; }

    /// <summary>The step names of every action sent since the last In half began, in order (<c>unwield-ammo</c>, <c>unwield-weapon</c>, <c>wield-caster</c>, <c>magic-mode</c>, <c>peace</c>, <c>unwield-caster</c>, <c>wield-weapon</c>, <c>wield-ammo</c>).</summary>
    public IReadOnlyList<string> Steps => _sent;

    /// <summary>True when the In half moved at least one item (a weapon aside or the caster into the hand): <c>swapped=yes</c>. False for a fighter that already held the caster.</summary>
    public bool Swapped { get; private set; }

    /// <summary>The session seconds spent in the halves that ended since the last In half began (<c>swap_s</c>).</summary>
    public double Seconds => _seconds;

    /// <summary>The verdict fields: <c>swapped=yes|no swap_s=&lt;n&gt;</c> (and <c>swap_why=swap:&lt;step&gt;</c> after a failure).</summary>
    public string Fields =>
        string.Create(CultureInfo.InvariantCulture, $"swapped={(Swapped ? "yes" : "no")} swap_s={_seconds:0.0}{(Failure is null ? string.Empty : " swap_why=" + Failure)}");

    /// <summary>True when the caster is wielded now.</summary>
    public bool CasterWielded => FighterItems.Find(_state, _scratch, _kit.CasterWcid, out _, out var equipped) && equipped;

    /// <summary>True when the kit's caster is held (pack or hand): a swap can run.</summary>
    public bool CasterHeld => FighterItems.Find(_state, _scratch, _kit.CasterWcid, out _, out _);

    /// <summary>Begins a half. An In half clears the record of the last swap. Returns false when a half is already running.</summary>
    /// <param name="direction">The half.</param>
    /// <param name="now">The session clock's time.</param>
    public bool Begin(SwapDirection direction, double now)
    {
        if (Status == SwapStatus.Running)
        {
            return false;
        }

        if (direction == SwapDirection.In)
        {
            _sent.Clear();
            _recorded.Clear();
            _seconds = 0;
            Swapped = false;
            Failure = null;
            Detail = null;
            _firstFailure = null;
            _firstDetail = null;
            _restoring = false;
            Record();
        }

        Start(direction, now);
        return true;
    }

    /// <summary>
    /// Advances the half: reads the action in flight, then looks at the read model, sends the next step or ends the half. Call every frame while <see cref="SwapStatus.Running"/>; it allocates nothing while an action is in flight.
    /// </summary>
    /// <param name="now">The session clock's time.</param>
    public SwapStatus Step(double now)
    {
        if (Status != SwapStatus.Running)
        {
            return Status;
        }

        if (_pending is not null)
        {
            if (_pending.Outcome is not { } outcome)
            {
                return CheckClock(now);
            }

            _pending = null;
            if (!outcome.Succeeded)
            {
                return Fail(_pendingStep, outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail), now);
            }

            _settleSince = now;
        }

        if (CheckClock(now) != SwapStatus.Running)
        {
            return Status;
        }

        var next = _direction == SwapDirection.In ? DecideIn(out var action) : DecideOut(out action);
        if (next is null)
        {
            return Complete(now);
        }

        if (action is null)
        {
            // No action behind the decision: the caster is not held, or a recorded weapon is no longer held.
            return Fail(StepOf(next), next.StartsWith("caster-missing", StringComparison.Ordinal)
                ? "the kit's caster (weenie " + _kit.CasterWcid.ToString(CultureInfo.InvariantCulture) + ") is not in the pack or the hand (run --scenario=fighter-build first)"
                : "an item the swap put aside is no longer held", now);
        }

        if (string.Equals(next, _lastKey, StringComparison.Ordinal))
        {
            // The action confirmed, but the read model still shows the old state: give it a moment, then name the step.
            return now - _settleSince > SettleSeconds
                ? Fail(StepOf(next), "the step was confirmed but the read model still shows the old state after " + SettleSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s", now)
                : Status;
        }

        // The busy wait (M4 fresh-gate fix): never send into ACE's busy chain (a potion's Ready, a move's chain); bounded, then one send.
        if (_actions.BusyReason is { } busy)
        {
            if (double.IsNaN(_busySince))
            {
                _busySince = now;
                _log.Info(LogCategory, "swap:busy-wait: the player is busy (" + busy + "); " + StepOf(next) + " waits up to " + BusyWaitSeconds.ToString("0", CultureInfo.InvariantCulture) + " s");
            }

            return now - _busySince > BusyWaitSeconds
                ? Fail("busy-wait", "the player was still busy after " + BusyWaitSeconds.ToString("0", CultureInfo.InvariantCulture) + " s (" + busy + "); " + StepOf(next) + " was not sent", now)
                : Status;
        }

        if (!double.IsNaN(_busySince))
        {
            _log.Info(LogCategory, "swap:busy-wait: the player is free after " + (now - _busySince).ToString("0.00", CultureInfo.InvariantCulture) + " s");
            _busySince = double.NaN;
        }

        if (++_halfSteps > MaxSteps)
        {
            return Fail(StepOf(next), "more than " + MaxSteps.ToString(CultureInfo.InvariantCulture) + " actions in one half", now);
        }

        _lastKey = next;
        _pendingStep = StepOf(next);
        _sent.Add(_pendingStep);
        if (_direction == SwapDirection.In && _pendingStep != "magic-mode")
        {
            Swapped = true;
        }

        _log.Info(LogCategory, "swap " + _direction.ToString().ToLowerInvariant() + ": " + _pendingStep + (_restoring ? " (restoring after " + _firstFailure + ")" : string.Empty));
        _pending = action();
        return Status;
    }

    // ------------------------------------------------------------------------------------------------ the In half

    /// <summary>The key of the next In step (<c>step:guid</c>) with its action, or null when the goal holds (the weapon aside, the caster wielded, the magic mode).</summary>
    private string? DecideIn(out Func<ActionHandle>? action)
    {
        action = null;
        var ammo = FighterItems.WieldedAmmunition(_state);
        if (ammo != 0)
        {
            action = () => _actions.Unwield(ammo, ActionSource.Automation);
            return "unwield-ammo:" + Hex(ammo);
        }

        if (FighterItems.WieldedLauncher(_state, _scratch) is { } launcher)
        {
            action = () => _actions.Unwield(launcher.Guid, ActionSource.Automation);
            return "unwield-weapon:" + Hex(launcher.Guid);
        }

        var inHand = EquippedAt(MeleeSlots | HeldSlot, excludeWcid: _kit.CasterWcid);
        if (inHand != 0)
        {
            action = () => _actions.Unwield(inHand, ActionSource.Automation);
            return "unwield-weapon:" + Hex(inHand);
        }

        if (!FighterItems.Find(_state, _scratch, _kit.CasterWcid, out var caster, out var wielded))
        {
            return "caster-missing";
        }

        if (!wielded)
        {
            action = () => _actions.Wield(caster, EquipMask.Held, ActionSource.Automation);
            return "wield-caster:" + Hex(caster);
        }

        if (_state.Self.Qualities.CombatMode != CombatModes.Magic)
        {
            action = () => _actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
            return "magic-mode:" + Hex(0);
        }

        return null;
    }

    // ------------------------------------------------------------------------------------------------ the Out half

    /// <summary>The key of the next Out step, or null when the goal holds (peace, no caster in the hand, the weapon and its ammunition wielded).</summary>
    private string? DecideOut(out Func<ActionHandle>? action)
    {
        action = null;
        if (_state.Self.Qualities.CombatMode is not (0 or CombatModes.NonCombat))
        {
            action = () => _actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Automation);
            return "peace:" + Hex(0);
        }

        if (FighterItems.Find(_state, _scratch, _kit.CasterWcid, out var caster, out var wielded) && wielded)
        {
            action = () => _actions.Unwield(caster, ActionSource.Automation);
            return "unwield-caster:" + Hex(caster);
        }

        var targets = Targets();
        foreach (var (guid, location) in targets)
        {
            if (_state.Self.Inventory.TryGetEquipped(guid, out var held) && (held.Location & (uint)location) != 0)
            {
                continue;
            }

            var step = (location & AmmoSlot) != 0 ? "wield-ammo" : "wield-weapon";
            if (!_state.Objects.Contains(guid))
            {
                return step + ":gone"; // a recorded item that is no longer held: the step fails by name below
            }

            action = () => _actions.Wield(guid, location, ActionSource.Automation);
            return step + ":" + Hex(guid);
        }

        return null;
    }

    /// <summary>What the Out half wields: the items recorded when the In half began (the weapon, then its ammunition); with none recorded, the kit's own weapon (the Mace, or the launcher and its ammunition) from the pack.</summary>
    private List<(uint Guid, EquipMask Location)> Targets()
    {
        if (_recorded.Count > 0)
        {
            return _recorded;
        }

        var fallback = new List<(uint, EquipMask)>();
        foreach (var line in _kit.Items)
        {
            var location = line.Role switch
            {
                FighterRole.Weapon => EquipMask.MeleeWeapon,
                FighterRole.Bow => EquipMask.MissileWeapon,
                FighterRole.Ammo => EquipMask.MissileAmmo,
                _ => EquipMask.None,
            };
            if (location != EquipMask.None && FighterItems.Find(_state, _scratch, line.Wcid, out var guid, out _))
            {
                fallback.Add((guid, location));
            }
        }

        // The kit's lines in grant order: the Mace alone, or the launcher then its ammunition.
        _recorded.AddRange(fallback);
        return _recorded;
    }

    /// <summary>Notes what is wielded in the weapon slots (not the caster), the launcher or melee weapon first and the ammunition last: the Out half wields them again by guid.</summary>
    private void Record()
    {
        _recorded.Clear();
        var equipped = _state.Self.Inventory.Equipped;
        uint ammo = 0;
        var ammoLocation = EquipMask.None;
        for (var i = 0; i < equipped.Count; i++)
        {
            var entry = equipped[i];
            var location = (EquipMask)entry.Location;
            if (_state.Objects.TryGet(entry.Guid, out var o) && o.Weenie.WeenieClassId == _kit.CasterWcid)
            {
                continue;
            }

            if ((location & AmmoSlot) != 0)
            {
                (ammo, ammoLocation) = (entry.Guid, location & AmmoSlot);
            }
            else if ((location & (MeleeSlots | MissileSlots)) != 0)
            {
                _recorded.Add((entry.Guid, location & (MeleeSlots | MissileSlots)));
            }
        }

        if (ammo != 0)
        {
            _recorded.Add((ammo, ammoLocation));
        }
    }

    // ------------------------------------------------------------------------------------------------ plumbing

    private void Start(SwapDirection direction, double now)
    {
        _direction = direction;
        _pending = null;
        _lastKey = null;
        _halfStart = now;
        _halfSteps = 0;
        _settleSince = now;
        _busySince = double.NaN;
        Status = SwapStatus.Running;
    }

    private SwapStatus CheckClock(double now) =>
        now - _halfStart > MaxHalfSeconds
            ? Fail(string.IsNullOrEmpty(_pendingStep) ? "timeout" : _pendingStep, "the half did not end within " + MaxHalfSeconds.ToString("0", CultureInfo.InvariantCulture) + " s", now)
            : Status;

    private SwapStatus Complete(double now)
    {
        _seconds += Math.Max(0, now - _halfStart);
        _pending = null;
        if (_restoring)
        {
            _restoring = false;
            Failure = _firstFailure;
            Detail = _firstDetail;
            Status = SwapStatus.Failed;
            _log.Warn(LogCategory, $"swap restored after {Failure}: the weapon is wielded again and the player is in peace; {Fields}");
            return Status;
        }

        Status = SwapStatus.Done;
        _log.Info(LogCategory, $"swap {_direction.ToString().ToLowerInvariant()} done: {Fields}");
        return Status;
    }

    private SwapStatus Fail(string step, string detail, double now)
    {
        var failure = "swap:" + step;
        _pending = null;
        _log.Warn(LogCategory, $"FAILED {failure}: {detail}");
        if (_restoring)
        {
            // The restore itself failed: no second restore and no retry; the first failure stays the reason.
            _seconds += Math.Max(0, now - _halfStart);
            _restoring = false;
            Failure = _firstFailure;
            Detail = (_firstDetail ?? string.Empty) + "; the restore then failed at " + failure + ": " + detail;
            Status = SwapStatus.Failed;
            return Status;
        }

        if (_direction == SwapDirection.In)
        {
            // Put the weapon back and the player in peace as far as the server allows (the same steps as the Out half, logged), then report the failure.
            _seconds += Math.Max(0, now - _halfStart);
            (_firstFailure, _firstDetail) = (failure, detail);
            _restoring = true;
            Start(SwapDirection.Out, now);
            return Status;
        }

        _seconds += Math.Max(0, now - _halfStart);
        Failure = failure;
        Detail = detail;
        Status = SwapStatus.Failed;
        return Status;
    }

    /// <summary>The step name of a decision key (<c>wield-caster:0000ABCD</c> is <c>wield-caster</c>).</summary>
    private static string StepOf(string key) => key.Contains(':', StringComparison.Ordinal) ? key[..key.IndexOf(':', StringComparison.Ordinal)] : key;

    private static string Hex(uint guid) => guid.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>The guid of an equipped item at any of <paramref name="slots"/> that is not of weenie class <paramref name="excludeWcid"/>, or 0.</summary>
    private uint EquippedAt(EquipMask slots, uint excludeWcid)
    {
        var equipped = _state.Self.Inventory.Equipped;
        for (var i = 0; i < equipped.Count; i++)
        {
            var entry = equipped[i];
            if ((entry.Location & (uint)slots) == 0)
            {
                continue;
            }

            if (_state.Objects.TryGet(entry.Guid, out var o) && o.Weenie.WeenieClassId == excludeWcid)
            {
                continue;
            }

            return entry.Guid;
        }

        return 0;
    }
}
