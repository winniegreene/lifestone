using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Animation;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Vitals;

/// <summary>Where the keeper stands (<see cref="VitalsKeeper.State"/>).</summary>
public enum VitalsKeeperState
{
    /// <summary>No needs were asked for yet (<see cref="VitalsKeeper.NeedsFor"/>).</summary>
    Idle,

    /// <summary>A need is not met and the keeper is acting on it (an action in flight, or the next one about to be sent).</summary>
    Working,

    /// <summary>A need is not met and the keeper rests in peace for regeneration (bounded by <see cref="VitalsOptions.RestCapSeconds"/>).</summary>
    Resting,

    /// <summary>Every need is met: the next station may start.</summary>
    Ready,

    /// <summary>A need cannot be met: <see cref="VitalsKeeper.Why"/> names it (<c>rest-cap:&lt;vital&gt;</c>, <c>mana-max:&lt;n&gt;/&lt;max&gt;</c>, <c>mana:&lt;n&gt;/&lt;need&gt;</c>, <c>vitals-unknown</c>, <c>actions-cap</c>, <c>busy</c>, <c>mode:&lt;detail&gt;</c>).</summary>
    Blocked,
}

/// <summary>
/// What the next station needs before it starts (<see cref="VitalsKeeper.NeedsFor"/>). The keeper's own rules always apply on top: health from the recovery rule's thresholds, stamina from
/// <see cref="VitalsOptions.StaminaBelowPercent"/>; a field here can only raise them.
/// </summary>
/// <param name="MinHealthPercent">The least health share, in percent, the station starts at (raises <c>--heal-below</c>; the restore goes on to the larger of it and <c>--heal-until</c>).</param>
/// <param name="MinStaminaPercent">The least stamina share (raises <c>--vitals-stamina-below</c>).</param>
/// <param name="MinManaPercent">The least mana share.</param>
/// <param name="NextCastMana">The mana the station's next cast needs (its <see cref="GearCasts.ManaCeiling"/>); 0 when it casts nothing.</param>
/// <param name="NoManaRest">True when the station's place forbids waiting for mana (a mana shortfall the transfer cannot cover then ends <c>Blocked why=mana:...</c>).</param>
public readonly record struct VitalsNeeds(int MinHealthPercent = 0, int MinStaminaPercent = 0, int MinManaPercent = 0, uint NextCastMana = 0, bool NoManaRest = false);

/// <summary>
/// The vitals keeper (M4 P5a lane C2; the design's section 3.3): one policy for the three characters, stepped like <see cref="RebuffDriver.Tick"/> (no blocking waits), acting only through <see cref="IActionApi"/> and
/// reading only the session's server-confirmed vitals, spellbook, pack and skills (automation-vision R2 and R3). P5b's journeys call <see cref="NeedsFor"/> between stations and tick <see cref="Step"/> until
/// <see cref="VitalsKeeperState.Ready"/> or <see cref="VitalsKeeperState.Blocked"/>; the hunt asks <see cref="MayAttack"/> and <see cref="TryInFightPotion"/>.
/// </summary>
/// <remarks>
/// <para><b>Health</b> (first): the recovery rule decides (<see cref="RecoveryRules.Decide(in RecoveryInput, RecoveryOptions, out double)"/>, the hunt's own rule, called, not copied; its thresholds raised to the station's
/// <see cref="VitalsNeeds.MinHealthPercent"/>). The means in the order they are cheapest: the kit when the rule says <see cref="RecoveryDecision.Kit"/> (Healing trained, chance at least <c>--heal-chance</c>); else
/// Heal Self (the highest known level at <c>--vitals-cast-chance</c>, the mana for its cost in hand: mana comes back, a potion does not); else what the rule said: the potion, the kit as the last resort, or a rest
/// (the rule's <see cref="RecoveryDecision.Wait"/> and <see cref="RecoveryDecision.Fail"/> are both a rest here: the keeper runs between fights, where regeneration is safe). As the rule's tail says, a recovery with no kit or potion
/// that helps ends at the start trigger: a rest regenerates to <c>--heal-below</c>, and only Heal Self goes on to <c>--heal-until</c>.</para>
/// <para><b>Stamina</b>: under <c>--vitals-stamina-below</c> (30%) Revitalize Self where known and castable, else a rest, each on to <c>--vitals-stamina-until</c> (80%). <see cref="MayAttack"/> refuses an attack under
/// <c>--vitals-attack-stamina</c> (15%).</para>
/// <para><b>Mana</b>: when the station's next cast needs more than the mana left, Stamina to Mana Self where known, castable and giving something; else a rest where the station allows it, else
/// <c>Blocked why=mana:&lt;mana&gt;/&lt;need&gt;</c>.</para>
/// <para><b>Vitae</b> is reported (<see cref="VitaePercent"/>, logged at <see cref="NeedsFor"/>), never acted on.</para>
/// <para><b>The rest</b> is in peace and still (ACE <c>Creature_Vitals.cs</c> 210-236 at 95ef426d: combat mode or running forward halves health and stamina regeneration; sitting 2.5, lying down 3.0; mana has no stance
/// factor). The keeper enters peace (<see cref="IActionApi.SetCombatMode"/>) and takes the posture of <c>--vitals-rest-posture</c> through the human's keys (<see cref="MoveIntents.Command"/>: the emote key pressed
/// and released, MotionCommand Sitting 0x41000013 or Sleeping 0x41000014, which <c>PlayerMovementController.HandleCommand</c> sends as a MoveToState; ACE stores it as the forward command regeneration reads,
/// <c>Player_Networking.cs</c> 321-369). It stands up (the stop key: <see cref="MotionCommands.Ready"/>) before any other action. Every rest is capped (<c>--vitals-rest-cap-s</c>); over the cap the keeper ends
/// <c>Blocked why=rest-cap:&lt;vital&gt;</c>, never a silent retry.</para>
/// <para><b>Casts</b> need the magic combat mode: the keeper enters it when <see cref="CombatModeGuard"/> allows it with what the player wields, and returns to peace before <see cref="VitalsKeeperState.Ready"/>. A
/// refused mode sets the spells aside for the keeper's life, logged once (a fighter with a sword and no caster: the potion and the kit remain). A level that fizzles <see cref="MaxFizzlesInRow"/> times in a row is set
/// aside until the next <see cref="NeedsFor"/>.</para>
/// <para><b>The verdict</b> (<see cref="VerdictFields"/>, report-only in its first gate): <c>vitals_min=h/s/m heals= kits= potions= rests= rest_s= transfers=</c>.</para>
/// </remarks>
public sealed class VitalsKeeper
{
    /// <summary>The log category.</summary>
    public const string LogCategory = "vitals";

    /// <summary>A safety net, never a rule: the actions one pass may send before it ends <c>Blocked why=actions-cap</c> (a server that heals nothing loops no further).</summary>
    public const int MaxActionsPerPass = 40;

    /// <summary>
    /// <see cref="CastOff"/> of a fighter that holds its caster in the pack (M4 P5a C1b): the magic mode needs it wielded and the hand free, which is <see cref="CasterSwap"/>'s work around a whole pass, so the keeper casts nothing itself and says so
    /// (<c>cast=needs-swap</c>); kits, potions and rest remain. M4 P5b J3a closed <c>TODO(m4-keeper-swap)</c>: a keeper built with a <see cref="CasterSwap"/> runs its casts inside it (In, the casts, Out) and never reports this; the value stays for a keeper built without one.
    /// </summary>
    public const string NeedsSwap = "needs-swap";

    /// <summary>Fizzles in a row of one line after which it is set aside until the next <see cref="VitalsKeeper.NeedsFor"/>.</summary>
    public const int MaxFizzlesInRow = 3;

    /// <summary>Busy refusals (0x001D) of a heal in a row the keeper waits out (<see cref="BusyRetrySeconds"/> each, logged) before it ends <c>Blocked why=busy</c>.</summary>
    public const int MaxBusyRetries = 3;

    /// <summary>The wait after a busy refusal (the hunt's heal rule).</summary>
    public const double BusyRetrySeconds = 1;

    /// <summary>How long the keeper waits for the vitals to be known before it ends <c>Blocked why=vitals-unknown</c>.</summary>
    public const double VitalsUnknownSeconds = 10;

    /// <summary>How often the Healing skill is read again (the read may allocate with enchantments on).</summary>
    public const double SkillRefreshSeconds = 30;

    /// <summary>
    /// The most Stamina to Mana Self casts a pass sends in the fighters' order (<see cref="VitalsOptions.FighterOrder"/>, M4 P5a C7), and only at a high stamina with no Mana Potion left. Not retail: Lifestone's own bound; the keeper names it when it stops (<c>fighter mana order</c>).
    /// </summary>
    public const int FighterTransferCap = 2;

    /// <summary>How long the keeper waits for the vital update after a confirmed Mana or Stamina Potion before it sets potions aside and names it (ACE sends the vital's update 0.74 s into the 1.48 s use, so a wait of this length is a lost message).</summary>
    public const double PotionVitalWaitSeconds = 3;

    /// <summary>Seconds a point of mana takes to regenerate at a fighter's rate (about 1 mana a 5 s tick; the stance factors do not apply to mana, ACE <c>Creature_Vitals.cs</c> <c>GetStanceMod</c> at 95ef426d): the rest's estimate in the log, never a rule.</summary>
    public const double ManaRegenSecondsPerPoint = 5;

    /// <summary>MotionCommand Sitting (client <c>MotionCommandTable</c>, the emote key 0x10000096 in <c>MovementBindings.EmoteCommandOf</c>; ACE <c>MotionCommand.cs</c> 25-27).</summary>
    public const uint SitCommand = 0x41000013;

    /// <summary>MotionCommand Sleeping, the lie-down emote (the key 0x10000097; ACE <c>MotionCommand.cs</c> 25-27).</summary>
    public const uint LieCommand = 0x41000014;

    private readonly IGameSessionView _session;
    private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
    private readonly VitalsOptions _options;
    private readonly ILogSink _log;
    private readonly CasterSwap? _swap;
    private readonly List<uint> _scratch = [];
    private readonly int[] _fizzleRow = new int[4];
    private readonly bool[] _lineAside = new bool[4];

    private RecoveryOptions _recovery;
    private Pending _pending;
    private ActionHandle? _handle;
    private ActionHandle? _posturePress;
    private VitalsLine _castLine;
    private string _castName = string.Empty;
    private int _actions;
    private bool _healActive;
    private bool _staminaActive;
    private bool _enteredMagic;
    private bool _postureFailed;
    private string? _castOff;
    private string? _kitOff;
    private bool _kitAside;
    private bool _potionAside;
    private int _lostRolls;
    private uint _lostRollsAt;
    private int _busyRetries;
    private double _busyUntil = double.NegativeInfinity;
    private double _unknownSince = double.NaN;
    private double _skillAt = double.NaN;
    private int _healing = RecoveryRules.FallbackHealingSkill;
    private bool _specialised;
    private bool _skillWarned;
    private bool _noPotionWarned;
    private bool _manaPotionAside;
    private bool _staminaPotionAside;
    private bool _manaPotionNoted;
    private bool _staminaPotionNoted;
    private bool _fighterOrderNoted;
    private int _passTransfers;
    private uint _vitalBefore;
    private VitalWait _vitalWait;
    private double _vitalWaitUntil;
    private Rest _rest;
    private double _restStart;
    private double _now;
    private SwapPhase _swapPhase;
    private bool _swappedIn;

    /// <summary>A keeper over the session.</summary>
    /// <param name="session">The session view: the read model and the Action API.</param>
    /// <param name="spells">The SpellTable's spells (<c>SpellTableData.Spells</c>): the utility lines' power, school and cost.</param>
    /// <param name="options">The keeper's options.</param>
    /// <param name="log">The log.</param>
    /// <param name="swap">The fighter's caster swap (<see cref="CasterSwap.ForCharacter"/>), or null: with it the keeper's casts run inside one swap (In, the casts, Out) instead of <c>cast=needs-swap</c> (M4 P5b J3a, closes <c>TODO(m4-keeper-swap)</c>).</param>
    public VitalsKeeper(IGameSessionView session, IReadOnlyDictionary<uint, SpellInfo> spells, VitalsOptions options, ILogSink log, CasterSwap? swap = null)
    {
        _swap = swap;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _spells = spells ?? throw new ArgumentNullException(nameof(spells));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _recovery = options.Recovery;
    }

    private enum SwapPhase
    {
        None,
        In,
        Out,
    }

    private enum Pending
    {
        None,
        Kit,
        Potion,
        InFightPotion,
        ManaPotion,
        StaminaPotion,
        Cast,
        Mode,
        Posture,
        Stand,
    }

    private enum VitalWait
    {
        None,
        Mana,
        Stamina,
    }

    private enum Rest
    {
        None,
        Health,
        Stamina,
        Mana,
    }

    /// <summary>Where the keeper stands.</summary>
    public VitalsKeeperState State { get; private set; }

    /// <summary>True when every need of the last <see cref="NeedsFor"/> is met.</summary>
    public bool Ready => State == VitalsKeeperState.Ready;

    /// <summary>Why the keeper is <see cref="VitalsKeeperState.Blocked"/> (<c>rest-cap:health</c> and the like), or null.</summary>
    public string? Why { get; private set; }

    /// <summary>The needs of the last <see cref="NeedsFor"/>.</summary>
    public VitalsNeeds Needs { get; private set; }

    /// <summary>True while one of the keeper's actions is in flight.</summary>
    public bool Busy => _pending != Pending.None || _swapPhase != SwapPhase.None;

    /// <summary>Caster swaps the keeper began (In halves; M4 P5b J3a).</summary>
    public int Swaps { get; private set; }

    /// <summary>The reason the last swap half failed (<c>swap:&lt;step&gt;</c>), or null.</summary>
    public string? SwapFailure { get; private set; }

    /// <summary>True while the keeper holds the player sitting or lying down.</summary>
    public bool PostureDown { get; private set; }

    /// <summary>Heals that landed: kits (a won roll or a lost one: a use spent), potions and Heal Self casts.</summary>
    public int Heals => Kits + Potions + HealSelfCasts;

    /// <summary>Kit uses confirmed.</summary>
    public int Kits { get; private set; }

    /// <summary>Potions drunk (the in-fight ones included).</summary>
    public int Potions { get; private set; }

    /// <summary>The in-fight potions drunk.</summary>
    public int InFightPotions { get; private set; }

    /// <summary>Heal Self casts that landed.</summary>
    public int HealSelfCasts { get; private set; }

    /// <summary>Revitalize Self casts that landed.</summary>
    public int Revitalizes { get; private set; }

    /// <summary>Stamina to Mana Self casts that landed.</summary>
    public int Transfers { get; private set; }

    /// <summary>Mana Potions drunk and confirmed (M4 P5a C7).</summary>
    public int ManaPotions { get; private set; }

    /// <summary>Stamina Potions drunk and confirmed (M4 P5a C7).</summary>
    public int StaminaPotions { get; private set; }

    /// <summary>Fizzles of the keeper's casts.</summary>
    public int Fizzles { get; private set; }

    /// <summary>Rests begun.</summary>
    public int Rests { get; private set; }

    /// <summary>Seconds rested, over every rest that ended.</summary>
    public double RestSeconds { get; private set; }

    /// <summary>The least health share seen, in percent (NaN until the vitals are known).</summary>
    public double MinHealthPercent { get; private set; } = double.NaN;

    /// <summary>The least stamina share seen, in percent.</summary>
    public double MinStaminaPercent { get; private set; } = double.NaN;

    /// <summary>The least mana share seen, in percent.</summary>
    public double MinManaPercent { get; private set; } = double.NaN;

    /// <summary>The vitae penalty in percent as the vitae panel shows it (0 with none): reported, never acted on.</summary>
    public long VitaePercent => _session.State.Self.Qualities.IsKnown ? EffectRules.VitaePercentage(EffectRules.VitaeValue(_session.State.Self.Qualities)) : 0;

    /// <summary>Why the keeper casts nothing (the magic mode refused with what the player wields), or null.</summary>
    public string? CastOff => _castOff;

    /// <summary>
    /// Asks for the next station's needs: the pass's counters of actions, fizzles and set-aside means start again (the counts and minima of <see cref="VerdictFields"/> do not). Returns true when every need is met now
    /// (the state <see cref="VitalsKeeperState.Ready"/>); else the state is <see cref="VitalsKeeperState.Working"/> and the host ticks <see cref="Step"/>.
    /// </summary>
    /// <param name="needs">The station's needs.</param>
    public bool NeedsFor(VitalsNeeds needs)
    {
        Needs = needs;
        Why = null;
        _actions = 0;
        _healActive = false;
        _staminaActive = false;
        _kitAside = false;
        _potionAside = false;
        _manaPotionAside = false;
        _staminaPotionAside = false;
        _manaPotionNoted = false;
        _staminaPotionNoted = false;
        _fighterOrderNoted = false;
        _passTransfers = 0;
        _vitalWait = VitalWait.None;
        _busyRetries = 0;
        _unknownSince = double.NaN;
        _skillAt = double.NaN;
        Array.Clear(_fizzleRow);
        Array.Clear(_lineAside);
        var r = _options.Recovery;
        var below = Math.Max(r.BelowPercent, Math.Clamp(needs.MinHealthPercent, 0, 100));
        _recovery = r with { BelowPercent = below, UntilPercent = Math.Max(r.UntilPercent, below) };
        var vitae = VitaePercent;
        _log.Info(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"vitals: needs health {below}% (until {_recovery.UntilPercent}%), stamina {Math.Max(_options.StaminaBelowPercent, needs.MinStaminaPercent)}%, mana {needs.MinManaPercent}% and {needs.NextCastMana} for the next cast{(needs.NoManaRest ? " (no mana rest)" : string.Empty)}; now {Shares()}{(vitae > 0 ? $"; vitae {vitae}% (reported, not acted on)" : string.Empty)}"));
        State = VitalsKeeperState.Working;
        if (_pending == Pending.None && !PostureDown && !_enteredMagic && !_swappedIn && _swapPhase == SwapPhase.None && Met())
        {
            State = VitalsKeeperState.Ready;
            return true;
        }

        return false;
    }

    /// <summary>
    /// One step at session time <paramref name="now"/>: settles the action in flight (in any state: an in-fight potion too), then, while <see cref="VitalsKeeperState.Working"/> or
    /// <see cref="VitalsKeeperState.Resting"/>, decides and sends at most one action. Never blocks.
    /// </summary>
    /// <param name="now">The session clock's time.</param>
    /// <returns>The state after the step.</returns>
    public VitalsKeeperState Step(double now)
    {
        _now = now;
        if (_pending != Pending.None && !Settle(now))
        {
            return State;
        }

        if (_swapPhase != SwapPhase.None)
        {
            return StepSwap(now);
        }

        if (State is not (VitalsKeeperState.Working or VitalsKeeperState.Resting) || now < _busyUntil)
        {
            return State;
        }

        var q = _session.State.Self.Qualities;
        if (!q.IsKnown || !q.TryGetVitalLevels(MageTemplate.MaxHealth, out var h, out var hMax) || !q.TryGetVitalLevels(MageTemplate.MaxStamina, out var s, out var sMax)
            || !q.TryGetVitalLevels(MageTemplate.MaxMana, out var m, out var mMax) || hMax == 0 || sMax == 0)
        {
            if (double.IsNaN(_unknownSince))
            {
                _unknownSince = now;
            }

            return now - _unknownSince > VitalsUnknownSeconds ? Block("vitals-unknown", "the vitals were not known within " + VitalsUnknownSeconds.ToString("0", CultureInfo.InvariantCulture) + " s") : State;
        }

        _unknownSince = double.NaN;
        Note(h, hMax, s, sMax, m, mMax);
        if (_actions >= MaxActionsPerPass)
        {
            return Block("actions-cap", $"{_actions} actions were sent in one pass and a need is still not met ({Shares()})");
        }

        // A Mana or Stamina Potion was confirmed: the next look waits for the vital's update (bounded, named on failure).
        if (_vitalWait != VitalWait.None)
        {
            var isMana = _vitalWait == VitalWait.Mana;
            if ((isMana ? m : s) != _vitalBefore)
            {
                _vitalWait = VitalWait.None;
            }
            else if (now < _vitalWaitUntil)
            {
                return State;
            }
            else
            {
                _vitalWait = VitalWait.None;
                if (isMana)
                {
                    _manaPotionAside = true;
                }
                else
                {
                    _staminaPotionAside = true;
                }

                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitals: the {(isMana ? "mana" : "stamina")} did not change within {PotionVitalWaitSeconds:0} s of a confirmed {(isMana ? "Mana" : "Stamina")} Potion (still {(isMana ? m : s)}); {(isMana ? "Mana" : "Stamina")} Potions are set aside until the next station's needs"));
            }
        }

        // Health first.
        if (HealthStep(now, h, hMax) is { } afterHealth)
        {
            return afterHealth;
        }

        // Stamina, before a run or an attack.
        var staminaPct = 100.0 * s / sMax;
        var staminaStart = Math.Max(_options.StaminaBelowPercent, Needs.MinStaminaPercent);
        var staminaUntil = Math.Max(_options.StaminaUntilPercent, staminaStart);
        if (staminaPct < staminaStart || (_staminaActive && staminaPct < staminaUntil))
        {
            _staminaActive = true;
            if (VitalPotionStep(false, s, string.Create(CultureInfo.InvariantCulture, $"stamina {staminaPct:0}% (under {staminaStart}%, on to {staminaUntil}%)")) is { } staminaPotion)
            {
                return staminaPotion;
            }

            if (CanCast(VitalsLine.RevitalizeSelf, m, out var revitalize))
            {
                return Cast(VitalsLine.RevitalizeSelf, revitalize!, string.Create(CultureInfo.InvariantCulture, $"stamina {staminaPct:0}% (under {staminaStart}%, on to {staminaUntil}%)"));
            }

            return RestFor(Rest.Stamina, now, string.Create(CultureInfo.InvariantCulture, $"stamina {staminaPct:0}% (under {staminaStart}%, on to {staminaUntil}%)"));
        }

        _staminaActive = false;

        // Mana, for the next cast.
        var need = Math.Max(Needs.NextCastMana, (uint)Math.Ceiling(Math.Clamp(Needs.MinManaPercent, 0, 100) * (double)mMax / 100));
        if (m < need)
        {
            if (need > mMax)
            {
                return Block(string.Create(CultureInfo.InvariantCulture, $"mana-max:{need}/{mMax}"), $"the next cast needs {need} mana and the maximum is {mMax}");
            }

            // M4 P5a C7: a Mana Potion first, whatever the character (a mage holds none, so its order below is unchanged).
            if (VitalPotionStep(true, m, string.Create(CultureInfo.InvariantCulture, $"mana {m} of {mMax}, {need} needed")) is { } manaPotion)
            {
                return manaPotion;
            }

            var fighter = _options.FighterOrder;
            var transferAllowed = !fighter || (staminaPct >= staminaUntil && _passTransfers < FighterTransferCap);
            if (fighter && !_fighterOrderNoted)
            {
                _fighterOrderNoted = true;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitals: fighter mana order: no Mana Potion to drink ({(_manaPotionAside ? "set aside" : "none held")}); Stamina to Mana Self only at stamina {staminaUntil}% or more and at most {FighterTransferCap} casts a pass (now stamina {staminaPct:0}%, {_passTransfers} cast(s)): {(transferAllowed ? "allowed" : "not allowed")}; else a rest in peace, about {(need - m) * ManaRegenSecondsPerPoint:0} s for {need - m} mana (1 mana a {ManaRegenSecondsPerPoint:0} s tick, no stance factor for mana)"));
            }

            if (transferAllowed && CanCast(VitalsLine.StaminaToManaSelf, m, out var transfer) && VitalsMeans.TransferGain(transfer!, s, mMax - m) > 0)
            {
                if (fighter)
                {
                    _passTransfers++;
                }

                return Cast(VitalsLine.StaminaToManaSelf, transfer!, string.Create(CultureInfo.InvariantCulture, $"mana {m} of {mMax}, {need} needed"));
            }

            if (Needs.NoManaRest)
            {
                return Block(string.Create(CultureInfo.InvariantCulture, $"mana:{m}/{need}"), "the next cast needs more mana, no transfer can be cast and the station allows no rest");
            }

            return RestFor(Rest.Mana, now, string.Create(CultureInfo.InvariantCulture, $"mana {m} of {mMax}, {need} needed"));
        }

        return Finish(now);
    }

    /// <summary>
    /// The hunt's question before an attack: false under <c>--vitals-attack-stamina</c> (15%) of stamina, or with the vitals unknown, with the reason in <paramref name="why"/>.
    /// </summary>
    public bool MayAttack(out string why)
    {
        var q = _session.State.Self.Qualities;
        if (!q.IsKnown || !q.TryGetVitalLevels(MageTemplate.MaxStamina, out var s, out var sMax) || sMax == 0)
        {
            why = "vitals unknown";
            return false;
        }

        var pct = 100.0 * s / sMax;
        NoteStamina(pct);
        if (pct < _options.AttackStaminaPercent)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"stamina {pct:0}% is under --vitals-attack-stamina {_options.AttackStaminaPercent}%");
            return false;
        }

        why = string.Empty;
        return true;
    }

    /// <summary>
    /// The in-fight potion (closes <c>m4-hunt-infight-heal</c> as the owner's call; off unless <c>--vitals-infight-potion on</c>): drinks a Potion of Healing when the health is under
    /// <c>--vitals-infight-potion-below</c> (40%) and the target is out of reach, nothing of the keeper's is in flight and a potion is held. Returns true when the drink was sent; the host keeps ticking
    /// <see cref="Step"/>, which settles it.
    /// </summary>
    /// <param name="targetInReach">True when the target can hit or be hit now (then a fight goes on: no potion).</param>
    public bool TryInFightPotion(bool targetInReach)
    {
        if (!_options.InFightPotion || targetInReach || _pending != Pending.None)
        {
            return false;
        }

        var q = _session.State.Self.Qualities;
        if (!q.IsKnown || !q.TryGetVitalLevels(MageTemplate.MaxHealth, out var h, out var hMax) || hMax == 0)
        {
            return false;
        }

        var pct = 100.0 * h / hMax;
        NoteHealth(pct);
        if (pct >= _options.InFightPotionBelowPercent)
        {
            return false;
        }

        if (!VitalsMeans.FindPotions(_session.State, _scratch, out var potion, out var count))
        {
            if (!_noPotionWarned)
            {
                _noPotionWarned = true;
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitals: in-fight potion wanted at {pct:0}% health with the target out of reach, but no Potion of Healing is held"));
            }

            return false;
        }

        _noPotionWarned = false;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitals: in-fight potion 0x{potion:X8} ({count} held) at {pct:0}% health (under {_options.InFightPotionBelowPercent}%), the target out of reach"));
        _handle = _session.Actions.Drink(potion, ActionSource.Host);
        _pending = Pending.InFightPotion;
        return true;
    }

    /// <summary>The verdict fields (report-only in their first gate): <c>vitals_min=h/s/m heals= kits= potions= rests= rest_s= transfers=</c>, and <c>cast=needs-swap</c> when a fighter holds its caster in the pack and the keeper cast nothing for want of the swap (M4 P5a C1b).</summary>
    public string VerdictFields() => string.Create(
        CultureInfo.InvariantCulture,
        $"vitals_min={Pct(MinHealthPercent)}/{Pct(MinStaminaPercent)}/{Pct(MinManaPercent)} heals={Heals} kits={Kits} potions={Potions} rests={Rests} rest_s={RestSeconds:0} transfers={Transfers} mana_potions={ManaPotions} stamina_potions={StaminaPotions}{(ReferenceEquals(_castOff, NeedsSwap) ? " cast=needs-swap" : string.Empty)}{(Swaps > 0 ? $" swaps={Swaps}" : string.Empty)}");

    private static string Pct(double value) => double.IsNaN(value) ? "-" : Math.Floor(value).ToString("0", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------------------------------------ health

    private VitalsKeeperState? HealthStep(double now, uint h, uint hMax)
    {
        if (_lostRolls > 0 && h != _lostRollsAt)
        {
            _lostRolls = 0; // a lost roll changes nothing, so the chance stands until the health moves
        }

        RefreshHealingSkill(now);
        var kit = VitalsMeans.FindKit(_session.State, _scratch, out var kitGuid, out var kitUses) && _kitOff is null && !_kitAside;
        var potion = VitalsMeans.FindPotions(_session.State, _scratch, out var potionGuid, out var potions) && !_potionAside;
        var combat = _session.State.Self.Qualities.CombatMode is not (0 or CombatModes.NonCombat);
        var input = new RecoveryInput(h, hMax, _healActive, kit ? kitUses : 0, potion ? potions : 0, _lostRolls, 0, _healing, _specialised, combat);
        var decision = RecoveryRules.Decide(in input, _recovery, out var chance);
        var pct = 100.0 * h / hMax;
        var mana = _session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var m, out _) ? m : 0;
        if (decision == RecoveryDecision.None)
        {
            // The rule's tail: a started recovery with no kit or potion that helps stops at the start trigger; Heal Self goes on to the until share.
            if (_healActive && pct < _recovery.UntilPercent && h < hMax && CanCast(VitalsLine.HealSelf, mana, out var tail))
            {
                return Cast(VitalsLine.HealSelf, tail!, string.Create(CultureInfo.InvariantCulture, $"health {pct:0}% (on to {_recovery.UntilPercent}%)"));
            }

            _healActive = false;
            return null;
        }

        _healActive = true;
        var at = string.Create(CultureInfo.InvariantCulture, $"health {pct:0}% ({hMax - h} missing; below {_recovery.BelowPercent}%, until {_recovery.UntilPercent}%)");
        if (decision == RecoveryDecision.Kit)
        {
            return UseKit(kitGuid, kitUses, chance, at);
        }

        if (CanCast(VitalsLine.HealSelf, mana, out var heal))
        {
            return Cast(VitalsLine.HealSelf, heal!, at);
        }

        switch (decision)
        {
            case RecoveryDecision.Potion:
                if (StandFirst())
                {
                    return State;
                }

                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitals: potion 0x{potionGuid:X8} ({potions} held) at {at}"));
                return Send(Pending.Potion, _session.Actions.Drink(potionGuid, ActionSource.Host));
            case RecoveryDecision.KitLastResort:
                return UseKit(kitGuid, kitUses, chance, at + " (the last resort)");
            default:
                return RestFor(Rest.Health, now, at + (decision == RecoveryDecision.Fail ? " (nothing heals: under the retreat share, the keeper rests out of the fight)" : string.Empty));
        }
    }

    private VitalsKeeperState UseKit(uint kit, int uses, double chance, string at)
    {
        if (StandFirst())
        {
            return State;
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitals: kit 0x{kit:X8} ({uses} use(s) left) at {at}, estimated chance {chance:0.00}, healing {_healing}"));
        return Send(Pending.Kit, _session.Actions.UseWithTarget(kit, _session.State.Self.Guid, ActionSource.Host));
    }

    private void RefreshHealingSkill(double now)
    {
        if (!double.IsNaN(_skillAt) && now - _skillAt < SkillRefreshSeconds)
        {
            return;
        }

        _skillAt = now;
        switch (RecoveryRules.ReadHealingSkill(_session.State.Self.Qualities, out var value, out var specialised))
        {
            case HealingSkillState.Read:
                _healing = value;
                _specialised = specialised;
                break;
            case HealingSkillState.Untrained:
                if (_kitOff is null)
                {
                    _kitOff = "Healing is below trained (a kit use would end UseDone 0x04FC)";
                    _log.Info(LogCategory, "vitals: " + _kitOff + ": no kit; the means are Heal Self, potions and rest");
                }

                break;
            default:
                _healing = RecoveryRules.FallbackHealingSkill;
                _specialised = false;
                if (!_skillWarned)
                {
                    _skillWarned = true;
                    _log.Warn(LogCategory, $"vitals: the Healing skill cannot be read from the read model (TODO(m4-heal-skill-read)); the kit's chance is estimated with {RecoveryRules.FallbackHealingSkill}");
                }

                break;
        }
    }

    // ------------------------------------------------------------------------------------------------ casts

    private bool CanCast(VitalsLine line, uint mana, out SpellInfo? info)
    {
        info = null;
        if (_castOff is not null || _lineAside[(int)line])
        {
            return false;
        }

        var state = _session.State;
        if (!VitalsMeans.KnowsAny(state.Self.Spellbook, line))
        {
            return false;
        }

        info = VitalsMeans.Pick(state.Self.Qualities, state.Self.Spellbook, _spells, line, _options.CastChance, out _);
        if (info is null || mana < GearCasts.ManaCeiling(info, state.Self.Inventory.Equipped.Count))
        {
            return false;
        }

        if (state.Self.Qualities.CombatMode != CombatModes.Magic && CombatModeGuard.Check(CombatMode.Magic, state) is { } refusal)
        {
            if (FighterItems.Find(state, _scratch, GearKit.Wand.Wcid, out _, out var wielded) && !wielded)
            {
                if (_swap is not null)
                {
                    return true; // Cast() runs the swap In first (M4 P5b J3a)
                }

                _castOff = NeedsSwap;
                _log.Warn(LogCategory, "vitals: the player wields a weapon and holds a caster in the pack (" + refusal + "): the keeper casts nothing until a caster swap runs around the cast (cast=needs-swap, TODO(m4-keeper-swap)); kits, potions and rest remain");
                return false;
            }

            _castOff = refusal;
            _log.Warn(LogCategory, "vitals: the magic mode is refused with what the player wields (" + refusal + "): the keeper casts nothing (no Heal Self, Revitalize Self or Stamina to Mana Self); kits, potions and rest remain");
            return false;
        }

        return true;
    }

    private VitalsKeeperState Cast(VitalsLine line, SpellInfo info, string why)
    {
        if (StandFirst())
        {
            return State;
        }

        if (_session.State.Self.Qualities.CombatMode != CombatModes.Magic && _swap is not null && !_swappedIn && CombatModeGuard.Check(CombatMode.Magic, _session.State) is { } needsSwap)
        {
            _log.Info(LogCategory, "vitals: the caster is in the pack (" + needsSwap + "): swap in once for " + info.Name + " and the casts after it");
            _swapPhase = SwapPhase.In;
            Swaps++;
            _swap.Begin(SwapDirection.In, _now);
            return State;
        }

        if (_session.State.Self.Qualities.CombatMode != CombatModes.Magic)
        {
            _enteredMagic = true;
            _log.Info(LogCategory, "vitals: entering the magic mode for " + info.Name);
            return Send(Pending.Mode, _session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host));
        }

        _castLine = line;
        _castName = info.Name;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitals: cast {info.Name} ({info.Id}) at {why}"));
        return Send(Pending.Cast, _session.Actions.Cast(info.Id, ActionSource.Host));
    }

    // ------------------------------------------------------------------------------------------------ rest and posture

    private VitalsKeeperState RestFor(Rest vital, double now, string why)
    {
        if (_rest != vital)
        {
            EndRest(now);
            _rest = vital;
            _restStart = now;
            Rests++;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitals: rest for {NameOf(vital)} at {why}; posture {_options.Posture.ToString().ToLowerInvariant()}, cap {_options.RestCapSeconds:0} s"));
        }

        if (now - _restStart > _options.RestCapSeconds)
        {
            return Block("rest-cap:" + NameOf(vital), string.Create(CultureInfo.InvariantCulture, $"{now - _restStart:0} s of rest for {NameOf(vital)} and still {why}"));
        }

        if (_session.State.Self.Qualities.CombatMode is not (0 or CombatModes.NonCombat))
        {
            if (StandFirst())
            {
                return State;
            }

            _enteredMagic = false;
            _log.Info(LogCategory, "vitals: entering peace to rest (combat mode halves health and stamina regeneration)");
            return Send(Pending.Mode, _session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host));
        }

        if (_options.Posture != RestPosture.Stand && !PostureDown && !_postureFailed)
        {
            var command = _options.Posture == RestPosture.Sit ? SitCommand : LieCommand;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitals: {(_options.Posture == RestPosture.Sit ? "sitting" : "lying")} down (0x{command:X8}, the emote key pressed and released)"));
            _posturePress = _session.Actions.Move.Command(command, true, ActionSource.Automation);
            return Send(Pending.Posture, _session.Actions.Move.Command(command, false, ActionSource.Automation));
        }

        State = VitalsKeeperState.Resting;
        return State;
    }

    private void EndRest(double now)
    {
        if (_rest != Rest.None)
        {
            RestSeconds += Math.Max(0, now - _restStart);
            _rest = Rest.None;
        }
    }

    /// <summary>Stands up (the stop key) when the keeper holds a posture; true when the stand was sent (the caller's action waits for the next step).</summary>
    private bool StandFirst()
    {
        if (!PostureDown)
        {
            return false;
        }

        EndRest(_now);
        _log.Info(LogCategory, "vitals: standing up (the stop key, Ready 0x41000003) before the next action");
        Send(Pending.Stand, _session.Actions.Move.Command(MotionCommands.Ready, true, ActionSource.Automation));
        return true;
    }

    private static string NameOf(Rest vital) => vital switch
    {
        Rest.Health => "health",
        Rest.Stamina => "stamina",
        _ => "mana",
    };

    // ------------------------------------------------------------------------------------------------ settling

    private VitalsKeeperState Send(Pending kind, ActionHandle handle)
    {
        _pending = kind;
        _handle = handle;
        if (kind is not (Pending.Stand or Pending.Posture))
        {
            _actions++;
        }

        if (State == VitalsKeeperState.Resting)
        {
            State = VitalsKeeperState.Working;
        }

        return State;
    }

    /// <summary>Reads the outcome of the action in flight; false while it has none.</summary>
    private bool Settle(double now)
    {
        if (_handle?.Outcome is not { } outcome)
        {
            return false;
        }

        var handle = _handle;
        var kind = _pending;
        _handle = null;
        _pending = Pending.None;
        switch (kind)
        {
            case Pending.Kit:
            case Pending.Potion:
            case Pending.InFightPotion:
                SettleHeal(kind, (HealHandle)handle, outcome, now);
                break;
            case Pending.ManaPotion:
            case Pending.StaminaPotion:
                SettleVitalPotion(kind == Pending.ManaPotion, (HealHandle)handle, outcome, now);
                break;
            case Pending.Cast:
                SettleCast(outcome);
                break;
            case Pending.Mode:
                if (!outcome.Succeeded)
                {
                    _enteredMagic = false;
                    Block("mode:" + outcome.Kind, "the combat mode change was not carried out: " + outcome.Detail);
                }

                break;
            case Pending.Posture:
                var press = _posturePress?.Outcome;
                _posturePress = null;
                if (outcome.Kind == ActionOutcomeKind.Local && press is { Kind: ActionOutcomeKind.Local })
                {
                    PostureDown = true;
                }
                else
                {
                    _postureFailed = true;
                    _log.Warn(LogCategory, $"vitals: the rest posture {_options.Posture.ToString().ToLowerInvariant()} was not taken ({(press ?? outcome).Kind}: {(press ?? outcome).Detail}); resting standing (ACE's stance factor 1.0)");
                }

                break;
            default:
                PostureDown = false;
                if (outcome.Kind != ActionOutcomeKind.Local)
                {
                    _log.Warn(LogCategory, $"vitals: the stand-up key was not taken ({outcome.Kind}: {outcome.Detail})");
                }

                break;
        }

        return true;
    }

    private void SettleHeal(Pending kind, HealHandle handle, ActionOutcome outcome, double now)
    {
        var kit = kind == Pending.Kit;
        if (outcome.Kind == ActionOutcomeKind.Confirmed)
        {
            _busyRetries = 0;
            if (kit)
            {
                Kits++;
                if (handle.Result is { Kind: HealResultKind.FailedRoll } && _session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var h, out _))
                {
                    _lostRolls++;
                    _lostRollsAt = h;
                }
            }
            else
            {
                Potions++;
                if (kind == Pending.InFightPotion)
                {
                    InFightPotions++;
                }
            }

            _log.Info(LogCategory, $"vitals: {(kit ? "kit" : "potion")} confirmed ({outcome.Detail})");
            return;
        }

        if (outcome.Kind == ActionOutcomeKind.ServerError && outcome.Code == HealLaneTracker.TooBusyCode)
        {
            if (++_busyRetries > MaxBusyRetries)
            {
                Block("busy", $"the server was busy (0x001D) {_busyRetries} times in a row");
                return;
            }

            _busyUntil = now + BusyRetrySeconds;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitals: the server was busy (0x001D); the next heal waits {BusyRetrySeconds:0} s (busy {_busyRetries} of {MaxBusyRetries})"));
            return;
        }

        if (kit && outcome.Kind == ActionOutcomeKind.ServerError && outcome.Code == HealLaneTracker.NotTrainedCode)
        {
            _kitOff = "the kit was refused: Healing is not trained (0x04FC)";
            _log.Warn(LogCategory, "vitals: " + _kitOff + "; no kit from now on");
            return;
        }

        if (kit)
        {
            _kitAside = true;
        }
        else
        {
            _potionAside = true;
        }

        _log.Warn(LogCategory, $"vitals: the {(kit ? "kit's use" : "potion")} was not carried out ({outcome.Kind} 0x{outcome.Code:X4}: {outcome.Detail}); set aside until the next station's needs");
    }

    /// <summary>
    /// The Mana or Stamina Potion the keeper drinks before a transfer or a rest (M4 P5a C7), through <see cref="IActionApi.Drink"/> as the Potion of Healing is: the use is confirmed when the stack falls (<c>PotionTracker</c>), and the next look waits for the vital
    /// to change (<see cref="PotionVitalWaitSeconds"/>) before it decides again, so one drink is never counted twice.
    /// </summary>
    private void SettleVitalPotion(bool mana, HealHandle handle, ActionOutcome outcome, double now)
    {
        var name = mana ? "Mana" : "Stamina";
        if (outcome.Kind == ActionOutcomeKind.Confirmed)
        {
            _busyRetries = 0;
            if (mana)
            {
                ManaPotions++;
            }
            else
            {
                StaminaPotions++;
            }

            if (handle.Result is not { Kind: HealResultKind.Wasted })
            {
                _vitalWait = mana ? VitalWait.Mana : VitalWait.Stamina;
                _vitalWaitUntil = now + PotionVitalWaitSeconds;
            }

            _log.Info(LogCategory, $"vitals: {name} Potion confirmed ({outcome.Detail})");
            return;
        }

        if (outcome.Kind == ActionOutcomeKind.ServerError && outcome.Code == HealLaneTracker.TooBusyCode)
        {
            if (++_busyRetries > MaxBusyRetries)
            {
                Block("busy", $"the server was busy (0x001D) {_busyRetries} times in a row");
                return;
            }

            _busyUntil = now + BusyRetrySeconds;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitals: the server was busy (0x001D); the next {name} Potion waits {BusyRetrySeconds:0} s (busy {_busyRetries} of {MaxBusyRetries})"));
            return;
        }

        if (mana)
        {
            _manaPotionAside = true;
        }
        else
        {
            _staminaPotionAside = true;
        }

        _log.Warn(LogCategory, $"vitals: the {name} Potion was not carried out ({outcome.Kind} 0x{outcome.Code:X4}: {outcome.Detail}); {name} Potions are set aside until the next station's needs");
    }

    /// <summary>
    /// Drinks a Mana (<paramref name="mana"/>) or Stamina Potion when one is held and not set aside (M4 P5a C7): the state after the send, or null when there is none to drink (the lack is named once a pass, then the order goes on to the cast or the rest).
    /// </summary>
    private VitalsKeeperState? VitalPotionStep(bool mana, uint current, string at)
    {
        if (mana ? _manaPotionAside : _staminaPotionAside)
        {
            return null;
        }

        var found = mana ? VitalsMeans.FindManaPotions(_session.State, _scratch, out var guid, out var count) : VitalsMeans.FindStaminaPotions(_session.State, _scratch, out guid, out count);
        if (!found)
        {
            if (!(mana ? _manaPotionNoted : _staminaPotionNoted))
            {
                if (mana)
                {
                    _manaPotionNoted = true;
                }
                else
                {
                    _staminaPotionNoted = true;
                }

                _log.Info(LogCategory, $"vitals: no {(mana ? "Mana" : "Stamina")} Potion is held at {at}");
            }

            return null;
        }

        if (StandFirst())
        {
            return State;
        }

        _vitalBefore = current;
        _log.Info(LogCategory, $"vitals: {(mana ? "Mana" : "Stamina")} Potion 0x{guid:X8} ({count} held) at {at}");
        return Send(mana ? Pending.ManaPotion : Pending.StaminaPotion, _session.Actions.Drink(guid, ActionSource.Host));
    }

    /// <summary>Steps the caster swap's half in flight (M4 P5b J3a): In done lets the casts go on; Out done lets <see cref="Finish"/> end the pass; a failed half blocks the keeper with <c>swap:&lt;step&gt;</c> (a failed In already restored the weapon).</summary>
    private VitalsKeeperState StepSwap(double now)
    {
        var half = _swapPhase;
        switch (_swap!.Step(now))
        {
            case SwapStatus.Running:
                return State;
            case SwapStatus.Done:
                _swapPhase = SwapPhase.None;
                _swappedIn = half == SwapPhase.In;
                _log.Info(LogCategory, $"vitals: caster swap {(half == SwapPhase.In ? "in" : "out")} done ({_swap.Fields})");
                return State;
            default:
                _swapPhase = SwapPhase.None;
                _swappedIn = false;
                SwapFailure = _swap.Failure ?? "swap:" + half.ToString().ToLowerInvariant();
                return Block(SwapFailure, $"the caster swap {half.ToString().ToLowerInvariant()} failed: {_swap.Failure} {_swap.Detail}");
        }
    }

    private void SettleCast(ActionOutcome outcome)
    {
        var line = (int)_castLine;
        if (outcome.Kind == ActionOutcomeKind.ServerError && outcome.Code == GearCasts.FizzleCode)
        {
            Fizzles++;
            if (++_fizzleRow[line] >= MaxFizzlesInRow)
            {
                _lineAside[line] = true;
                _log.Warn(LogCategory, $"vitals: {_castName} fizzled {_fizzleRow[line]} times in a row; {VitalsMeans.NameOf(_castLine)} is set aside until the next station's needs");
            }
            else
            {
                _log.Info(LogCategory, $"vitals: {_castName} fizzled ({_fizzleRow[line]} in a row); the next look decides again");
            }

            return;
        }

        if (!outcome.Succeeded)
        {
            _lineAside[line] = true;
            _log.Warn(LogCategory, $"vitals: {_castName} was not carried out ({outcome.Kind} 0x{outcome.Code:X4}: {outcome.Detail}); {VitalsMeans.NameOf(_castLine)} is set aside until the next station's needs");
            return;
        }

        _fizzleRow[line] = 0;
        switch (_castLine)
        {
            case VitalsLine.HealSelf:
                HealSelfCasts++;
                break;
            case VitalsLine.RevitalizeSelf:
                Revitalizes++;
                break;
            default:
                Transfers++;
                break;
        }

        _log.Info(LogCategory, $"vitals: {_castName} landed");
    }

    // ------------------------------------------------------------------------------------------------ the end of a pass

    private VitalsKeeperState Finish(double now)
    {
        EndRest(now);
        if (StandFirst())
        {
            return State;
        }

        if (_swappedIn && _swap is not null)
        {
            _enteredMagic = false;
            _log.Info(LogCategory, "vitals: caster swap out after the keeper's casts (peace, the weapon back in hand)");
            _swapPhase = SwapPhase.Out;
            _swap.Begin(SwapDirection.Out, now);
            return State;
        }

        if (_enteredMagic)
        {
            _enteredMagic = false;
            if (_session.State.Self.Qualities.CombatMode == CombatModes.Magic)
            {
                _log.Info(LogCategory, "vitals: back to peace after the keeper's casts");
                return Send(Pending.Mode, _session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host));
            }
        }

        State = VitalsKeeperState.Ready;
        _log.Info(LogCategory, $"vitals: ready at {Shares()}; {VerdictFields()}");
        return State;
    }

    private VitalsKeeperState Block(string why, string detail)
    {
        EndRest(_now);
        Why = why;
        State = VitalsKeeperState.Blocked;
        _log.Warn(LogCategory, $"vitals: Blocked why={why}: {detail}; {VerdictFields()}");
        if (PostureDown && _pending == Pending.None)
        {
            _log.Info(LogCategory, "vitals: standing up (the stop key) as the keeper stops");
            _pending = Pending.Stand;
            _handle = _session.Actions.Move.Command(MotionCommands.Ready, true, ActionSource.Automation);
        }

        if (_swappedIn && _swap is not null && _swapPhase == SwapPhase.None)
        {
            _log.Info(LogCategory, "vitals: caster swap out as the keeper stops (the weapon back in hand)");
            _swapPhase = SwapPhase.Out;
            _swap.Begin(SwapDirection.Out, _now);
        }

        return State;
    }

    /// <summary>True when every need holds at the current vitals (the start triggers, not the until levels: a station that may start).</summary>
    private bool Met()
    {
        var q = _session.State.Self.Qualities;
        if (!q.IsKnown || !q.TryGetVitalLevels(MageTemplate.MaxHealth, out var h, out var hMax) || !q.TryGetVitalLevels(MageTemplate.MaxStamina, out var s, out var sMax)
            || !q.TryGetVitalLevels(MageTemplate.MaxMana, out var m, out var mMax) || hMax == 0 || sMax == 0)
        {
            return false;
        }

        Note(h, hMax, s, sMax, m, mMax);
        var need = Math.Max(Needs.NextCastMana, (uint)Math.Ceiling(Math.Clamp(Needs.MinManaPercent, 0, 100) * (double)mMax / 100));
        return (!_recovery.Enabled || 100.0 * h / hMax >= _recovery.BelowPercent || h >= hMax)
            && 100.0 * s / sMax >= Math.Max(_options.StaminaBelowPercent, Needs.MinStaminaPercent)
            && m >= need;
    }

    private string Shares()
    {
        var q = _session.State.Self.Qualities;
        string One(uint id) => q.IsKnown && q.TryGetVitalLevels(id, out var c, out var x) && x != 0 ? string.Create(CultureInfo.InvariantCulture, $"{c}/{x}") : "?";
        return "health " + One(MageTemplate.MaxHealth) + ", stamina " + One(MageTemplate.MaxStamina) + ", mana " + One(MageTemplate.MaxMana);
    }

    private void Note(uint h, uint hMax, uint s, uint sMax, uint m, uint mMax)
    {
        NoteHealth(100.0 * h / hMax);
        NoteStamina(100.0 * s / sMax);
        if (mMax != 0)
        {
            var mana = 100.0 * m / mMax;
            MinManaPercent = double.IsNaN(MinManaPercent) ? mana : Math.Min(MinManaPercent, mana);
        }
    }

    private void NoteHealth(double pct) => MinHealthPercent = double.IsNaN(MinHealthPercent) ? pct : Math.Min(MinHealthPercent, pct);

    private void NoteStamina(double pct) => MinStaminaPercent = double.IsNaN(MinStaminaPercent) ? pct : Math.Min(MinStaminaPercent, pct);
}
