using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Casting;
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
/// <param name="NextCastFloor">
/// The next cast's try floor (M5 1 K5, D15: <see cref="ManaOrder.FloorOf"/>), the partner of <paramref name="NextCastMana"/>: with the tries on (<c>--cast-mana-tries</c> above 0) the mana need is met at the floor, because the
/// station's own attempt tries the real spell from there; 0 (the default) or the tries off: the ceiling, as before. Placed last so every positional caller keeps its meaning.
/// </param>
public readonly record struct VitalsNeeds(int MinHealthPercent = 0, int MinStaminaPercent = 0, int MinManaPercent = 0, uint NextCastMana = 0, bool NoManaRest = false, uint NextCastFloor = 0);

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
/// <para><b>The mana order</b> (M5 1 K5, D15 and D27, <see cref="ManaOrder"/>): with <c>--cast-mana-tries</c> above 0 the keeper's own casts are sent from their try floor with the tries, a need is met at the station's
/// <see cref="VitalsNeeds.NextCastFloor"/>, and under it the mage dances (Stamina to Mana Self pressed until it lands), drinks a Mana Potion only when the stamina is too low for a transfer and otherwise rests in peace
/// for stamina; the fighters keep D40's order with the transfer now a dance at their stamina gate. Every press of a dance counts as one action against <see cref="MaxActionsPerPass"/> (a dance, and the fizzle and Mana Conversion tries of any other cast, are cut to the actions
/// left), and the rests keep their cap. <c>--cast-mana-tries 0</c> is the order before K5, exactly.</para>
/// <para><b>The verdict</b> (<see cref="VerdictFields"/>, report-only in its first gate): <c>vitals_min=h/s/m heals= kits= potions= rests= rest_s= transfers=</c>, and, once the keeper cast, the cast policy's
/// <c>mana_tries= mana_refusals= mana_landed= busy= resists= proved=</c>, and where a dance ran <c>mana_presses= dances= dances_landed=</c>.</para>
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

    /// <summary>The <see cref="Why"/> of a pass the kill switch stopped (<c>Blocked why=kill-switch</c>, M5 D13).</summary>
    public const string KillSwitchWhy = "kill-switch";

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

    /// <summary>
    /// The retry rules of the keeper's casts (M5 item 1 lane K4, D13): <see cref="CastRules.Utility(int)"/> of <see cref="MaxFizzlesInRow"/> casts, the same level, the mana read again before each retry. The streak of fizzles
    /// across looks stays the keeper's own (<c>_fizzleRow</c>), handed to each attempt as <see cref="CastRequest.FizzlesSpent"/>, so a line still goes aside on its third fizzle in a row, whichever attempts they fell in.
    /// </summary>
    private static readonly CastRules FizzleRules = CastRules.Utility(MaxFizzlesInRow);

    /// <summary>The Stamina to Mana Self rows of <see cref="VitalsMeans.StaminaToManaSelf"/>, looked up once (<see cref="BuffStatMods.TransferOf"/> allocates).</summary>
    private static readonly TransferRow?[] DanceRows = [.. VitalsMeans.StaminaToManaSelf.Select(BuffStatMods.TransferOf)];

    private readonly CastRules _triesRules;
    private readonly CastTally _tally = new();
    private readonly DanceLevel[] _danceLevels = new DanceLevel[7];
    private readonly SpellInfo?[] _danceInfos = new SpellInfo?[7];
    private readonly HashSet<uint> _floorNoted = [];
    private readonly bool[] _triesSpent = new bool[4];
    private CastRules? _danceRules;
    private CastRules? _cappedRules;
    private CastRules? _cappedBase;
    private CastChatProof? _danceProof;
    private bool _castIsDance;
    private uint _castFloor;

    private readonly IGameSessionView _session;
    private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
    private readonly VitalsOptions _options;
    private readonly ILogSink _log;
    private readonly CasterSwap? _swap;
    private readonly List<uint> _scratch = [];
    private readonly int[] _fizzleRow = new int[4];
    private readonly bool[] _lineAside = new bool[4];
    private readonly uint[] _castIds = new uint[1];
    private CastAttempt? _cast;

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
    private int _castRow;
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
        _triesRules = options.Mana.On ? FizzleRules.WithManaTries(options.Mana.Tries, options.Mana.TrySpacingSeconds) : FizzleRules; // made once: the forms build a name string
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

    /// <summary>The mana order's options the keeper runs with (<c>--cast-mana-*</c>); the stations that hand it a mana need read them to work out the same try floor (M5 1 fix B, G2).</summary>
    public ManaOrderOptions ManaOptions => _options.Mana;

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

    /// <summary>Stamina-to-mana dances begun (M5 D27).</summary>
    public int Dances { get; private set; }

    /// <summary>Dances that landed.</summary>
    public int DancesLanded { get; private set; }

    /// <summary>The presses of every dance (each one action against <see cref="MaxActionsPerPass"/>).</summary>
    public int DancePresses { get; private set; }

    /// <summary>The cast policy's counts over every cast of the keeper (<see cref="CastTally"/>).</summary>
    public CastTally Tally => _tally;

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
        Array.Clear(_triesSpent);
        var r = _options.Recovery;
        var below = Math.Max(r.BelowPercent, Math.Clamp(needs.MinHealthPercent, 0, 100));
        _recovery = r with { BelowPercent = below, UntilPercent = Math.Max(r.UntilPercent, below) };
        var vitae = VitaePercent;
        _log.Info(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"vitals: needs health {below}% (until {_recovery.UntilPercent}%), stamina {Math.Max(_options.StaminaBelowPercent, needs.MinStaminaPercent)}%, mana {needs.MinManaPercent}% and {needs.NextCastMana} for the next cast{(_options.Mana.On && needs.NextCastFloor > 0 ? $" (its try floor {needs.NextCastFloor})" : string.Empty)}{(needs.NoManaRest ? " (no mana rest)" : string.Empty)}; now {Shares()}{(vitae > 0 ? $"; vitae {vitae}% (reported, not acted on)" : string.Empty)}"));
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

        if (_session.Actions.KillSwitch)
        {
            // M5 D13: the keeper's actions are automation, so the kill switch reaches the whole pass. The action in flight finished above; nothing more is sent (a swap half in flight is not stepped either).
            return State is VitalsKeeperState.Working or VitalsKeeperState.Resting ? Block(KillSwitchWhy, "the kill switch is on: the keeper sends nothing more") : State;
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
        if (_options.Mana.On)
        {
            // M5 1 K5: the mana order (D15, D27); --cast-mana-tries 0 keeps the order below, exactly as before.
            var floorNeed = FloorNeed(need, mMax);
            return m < floorNeed ? ManaOrderStep(now, m, mMax, s, sMax, need, floorNeed, staminaUntil) : Finish(now);
        }

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

    /// <summary>The station's mana need with the tries on: its <see cref="VitalsNeeds.NextCastFloor"/> (at most the ceiling) when it gave one, else the ceiling; never under its <see cref="VitalsNeeds.MinManaPercent"/>.</summary>
    private uint FloorNeed(uint ceilingNeed, uint mMax)
    {
        if (!_options.Mana.On || Needs.NextCastFloor == 0 || Needs.NextCastMana == 0)
        {
            return ceilingNeed;
        }

        return Math.Max(Math.Min(Needs.NextCastFloor, Needs.NextCastMana), (uint)Math.Ceiling(Math.Clamp(Needs.MinManaPercent, 0, 100) * (double)mMax / 100));
    }

    /// <summary>
    /// The mana branch through <see cref="ManaOrder"/> (M5 1 K5): the mage dances first, drinks only when the stamina is too low for a transfer, else rests in peace for stamina; the fighters drink, then dance at their
    /// stamina gate within <see cref="FighterTransferCap"/>, then rest. A wait is a rest for mana, or <c>Blocked why=mana:</c> where the station allows no rest.
    /// </summary>
    private VitalsKeeperState ManaOrderStep(double now, uint m, uint mMax, uint s, uint sMax, uint need, uint floorNeed, int staminaUntil)
    {
        if (need > mMax)
        {
            return Block(string.Create(CultureInfo.InvariantCulture, $"mana-max:{need}/{mMax}"), $"the next cast needs {need} mana and the maximum is {mMax}");
        }

        var q = _session.State.Self.Qualities;
        ManaOrder.TryManaConversion(q, out var mc);
        var count = FillDanceLevels(q);
        var potions = !_manaPotionAside && VitalsMeans.FindManaPotions(_session.State, _scratch, out _, out var held) ? held : 0;
        var fighter = _options.FighterOrder;
        var input = new ManaOrderInput
        {
            Mana = m,
            ManaMax = mMax,
            Stamina = s,
            StaminaMax = sMax,
            ManaConversion = mc,
            Need = new ManaNeed(need, floorNeed),
            ManaPotions = potions,
            Profile = fighter ? ManaProfile.Fighter : ManaProfile.Mage,
            FighterGatePercent = staminaUntil,
            FighterTransfersLeft = FighterTransferCap - _passTransfers,
        };
        var decision = ManaOrder.Decide(input, _danceLevels.AsSpan(0, count), _options.Mana);
        if (fighter && potions == 0 && !_fighterOrderNoted)
        {
            _fighterOrderNoted = true;
            var staminaPct = 100.0 * s / sMax;
            var allowed = staminaPct >= staminaUntil && _passTransfers < FighterTransferCap;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitals: fighter mana order: no Mana Potion to drink ({(_manaPotionAside ? "set aside" : "none held")}); Stamina to Mana Self (the dance) only at stamina {staminaUntil}% or more and at most {FighterTransferCap} casts a pass (now stamina {staminaPct:0}%, {_passTransfers} cast(s)): {(allowed ? "allowed" : "not allowed")}; else a rest in peace, about {(floorNeed - m) * ManaRegenSecondsPerPoint:0} s for {floorNeed - m} mana (1 mana a {ManaRegenSecondsPerPoint:0} s tick, no stance factor for mana)"));
        }

        var at = string.Create(CultureInfo.InvariantCulture, $"mana {m} of {mMax}, {floorNeed} needed{(floorNeed < need ? $" (the try floor under {need})" : string.Empty)}");
        switch (decision.Step)
        {
            case ManaStep.Ready:
            case ManaStep.TryReal:
                return Finish(now);
            case ManaStep.Potion:
                if (VitalPotionStep(true, m, at) is { } drink)
                {
                    return drink;
                }

                break;
            case ManaStep.Dance:
                if (CanCast(VitalsLine.StaminaToManaSelf, m, out _, forDance: true) && _danceInfos[decision.DanceIndex] is { } info)
                {
                    if (fighter)
                    {
                        _passTransfers++;
                    }

                    return Cast(VitalsLine.StaminaToManaSelf, info, at + "; " + decision.Reason, dance: true);
                }

                break;
            case ManaStep.RestStamina:
                if (!Needs.NoManaRest)
                {
                    return RestFor(Rest.Stamina, now, string.Create(CultureInfo.InvariantCulture, $"{at}: {decision.Reason}, to stamina {decision.StaminaTarget} (now {s})"));
                }

                break; // a station that allows no mana rest allows no rest for the transfer's stamina either: Blocked below
        }

        if (Needs.NoManaRest)
        {
            return Block(string.Create(CultureInfo.InvariantCulture, $"mana:{m}/{floorNeed}"), "the next cast needs more mana, the mana order has nothing to press or drink (" + decision.Reason + ") and the station allows no rest");
        }

        return RestFor(Rest.Mana, now, at);
    }

    /// <summary>
    /// M5 4 G1: what the pack holds when the level floor applies to a keeper line (Heal Self, Revitalize Self, Stamina to Mana Self: the pack holds the Life Magic focus, so ACE casts on the foci formula: <see cref="LevelFloor"/>), else null.
    /// A level the pack cannot pay for is named once, as the buff cycle names its own (the live dance fell to level V at mana 1, whose Gold Scarab no stock line carries, and ACE refused it 0x0400; a Heal Self short of mana falls the same way).
    /// </summary>
    private IReadOnlyDictionary<uint, int>? FloorFor(VitalsLine line)
    {
        var held = LevelFloor.HeldFor(_session.State, DatReaderWriter.Enums.MagicSchool.LifeMagic);
        if (held is null)
        {
            return null;
        }

        var ids = VitalsMeans.IdsOf(line);
        for (var i = 0; i < ids.Count; i++)
        {
            if (_spells.TryGetValue(ids[i], out var info) && LevelFloor.ShortOf(held, info, out var need) && _floorNoted.Add(info.Id))
            {
                _log.Warn(LogCategory, "vitals: " + info.Name + " needs " + need + "; skipped (the level floor)");
            }
        }

        return held;
    }

    /// <summary>The Stamina to Mana Self levels the book holds at the cast chance, lowest first, into <c>_danceLevels</c>; their count.</summary>
    private int FillDanceLevels(PlayerQualities q)
    {
        var book = _session.State.Self.Spellbook;
        var ids = VitalsMeans.StaminaToManaSelf;
        var skillRead = false;
        var skill = 0u;
        var count = 0;
        var held = FloorFor(VitalsLine.StaminaToManaSelf);
        for (var i = 0; i < ids.Count; i++)
        {
            if (!book.Knows(ids[i]) || !_spells.TryGetValue(ids[i], out var info) || DanceRows[i] is not { } row)
            {
                continue;
            }

            if (held is not null && LevelFloor.ShortOf(held, info, out _))
            {
                continue; // M5 4 G1: the level floor: a level whose foci-formula scarab the pack lacks is not pressed (named once by FloorFor); the keeper rests for the mana instead
            }

            if (!skillRead)
            {
                if (!GearCasts.TryCurrentSkill(q, GearCasts.SkillOf(info.School), out skill, out _))
                {
                    return 0;
                }

                skillRead = true;
            }

            if (GearCasts.Chance(skill, info.Power) < _options.CastChance)
            {
                continue;
            }

            _danceInfos[count] = info;
            _danceLevels[count++] = new DanceLevel(info.Id, GearCasts.ManaCeiling(info, 0), info.Power, row);
        }

        return count;
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
        if (!_options.InFightPotion || targetInReach || _pending != Pending.None || _session.Actions.KillSwitch)
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
        _handle = _session.Actions.Drink(potion, ActionSource.Automation);
        _pending = Pending.InFightPotion;
        return true;
    }

    /// <summary>The verdict fields (report-only in their first gate): <c>vitals_min=h/s/m heals= kits= potions= rests= rest_s= transfers=</c>, and <c>cast=needs-swap</c> when a fighter holds its caster in the pack and the keeper cast nothing for want of the swap (M4 P5a C1b).</summary>
    public string VerdictFields() => string.Create(
        CultureInfo.InvariantCulture,
        $"vitals_min={Pct(MinHealthPercent)}/{Pct(MinStaminaPercent)}/{Pct(MinManaPercent)} heals={Heals} kits={Kits} potions={Potions} rests={Rests} rest_s={RestSeconds:0} transfers={Transfers} mana_potions={ManaPotions} stamina_potions={StaminaPotions}{(_tally.Attempts > 0 ? " " + _tally.Fields : string.Empty)}{(Dances > 0 ? $" mana_presses={DancePresses} dances={Dances} dances_landed={DancesLanded}" : string.Empty)}{(ReferenceEquals(_castOff, NeedsSwap) ? " cast=needs-swap" : string.Empty)}{(Swaps > 0 ? $" swaps={Swaps}" : string.Empty)}");

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
                return Send(Pending.Potion, _session.Actions.Drink(potionGuid, ActionSource.Automation));
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
        return Send(Pending.Kit, _session.Actions.UseWithTarget(kit, _session.State.Self.Guid, ActionSource.Automation));
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

    private bool CanCast(VitalsLine line, uint mana, out SpellInfo? info, bool forDance = false)
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

        info = VitalsMeans.Pick(state.Self.Qualities, state.Self.Spellbook, _spells, line, _options.CastChance, out _, FloorFor(line));
        if (info is null)
        {
            return false;
        }

        // M5 1 K5 (D15): with the tries on, a line is cast from its try floor (a 0x0401 inside the tries is expected); once its tries are spent it waits for the ceiling until the next station's needs. The dance presses at
        // any mana (the order chose a level a press can land at).
        var ceiling = GearCasts.ManaCeiling(info, state.Self.Inventory.Equipped.Count);
        _castFloor = 0;
        if (!forDance && mana < ceiling)
        {
            if (!_options.Mana.On || _triesSpent[(int)line])
            {
                return false;
            }

            ManaOrder.TryManaConversion(state.Self.Qualities, out var mc);
            var floor = ManaOrder.FloorOf(ceiling, info.Power, ceiling, mc, _options.Mana, (info.Flags & SpellFlags.IgnoresManaConversion) != 0);
            if (mana < floor)
            {
                return false;
            }

            _castFloor = floor;
        }

        if (state.Self.Qualities.CombatMode != CombatModes.Magic && CombatModeGuard.Check(CombatMode.Magic, state) is { } refusal)
        {
            if (CasterOnlyInPack(state))
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

    /// <summary>
    /// True when a caster (an item whose ItemType has the Caster bit, <see cref="GearKit.ItemTypeCaster"/>: wand, staff or orb) is held in a pack and none is equipped (M5 item 3 w1): the keeper's "caster in the pack" read for the swap. It is the item's
    /// type, never one weenie: the mage's wand is the kit's wielded vendor line (the ground's <c>supplies.kit</c>: the Wand 2472), no longer the retired loot wand (The Healer's Heart 8552), and the fighters' caster is found the same way.
    /// </summary>
    private bool CasterOnlyInPack(GameState state)
    {
        GearInventory.Collect(state, _scratch);
        var inventory = state.Self.Inventory;
        var inPack = false;
        for (var i = 0; i < _scratch.Count; i++)
        {
            if (!state.Objects.TryGet(_scratch[i], out var item) || (item.ItemType & GearKit.ItemTypeCaster) == 0)
            {
                continue;
            }

            if (inventory.Find(_scratch[i]).Kind == InventoryPlaceKind.Equipped)
            {
                return false;
            }

            inPack = true;
        }

        return inPack;
    }

    private VitalsKeeperState Cast(VitalsLine line, SpellInfo info, string why, bool dance = false)
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
            return Send(Pending.Mode, _session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation));
        }

        _castLine = line;
        _castName = info.Name;
        _castIsDance = dance;
        CastRules rules;
        if (dance)
        {
            // Each press is one action against the pass's cap, so the dance is cut to the actions left (Send counts the first press, SettleCast the rest).
            var presses = Math.Max(1, Math.Min(_options.Mana.DancePresses, MaxActionsPerPass - _actions));
            if (_danceRules is null || _danceRules.MaxTries != presses)
            {
                _danceRules = CastRules.ManaDance(presses, _options.Mana.TrySpacingSeconds);
            }

            rules = _danceRules;
            _castFloor = 0;
            Dances++;
        }
        else
        {
            // M5 3 m4 (m5-keeper-tries-cap, review 1-R8): the fizzle tries and the Mana Conversion tries are casts too, so the attempt is cut to the actions the pass has left (Send counts the first, SettleCast the rest).
            var baseRules = _castFloor > 0 ? _triesRules : FizzleRules;
            var left = Math.Max(1, MaxActionsPerPass - _actions);
            if (_cappedRules is null || !ReferenceEquals(_cappedBase, baseRules) || _cappedRules.MaxTries != left)
            {
                _cappedBase = baseRules;
                _cappedRules = baseRules with { MaxTries = left };
            }

            rules = _cappedRules;
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitals: {(dance ? "dance" : "cast")} {info.Name} ({info.Id}) at {why}{(dance || _castFloor > 0 ? $" ({rules.Name}{(_castFloor > 0 ? $", from its try floor {_castFloor}" : string.Empty)})" : string.Empty)}"));
        _castIds[0] = info.Id;
        _castRow = dance ? 0 : _fizzleRow[(int)line];
        _cast ??= new CastAttempt(_session.Actions, _session.State, _log, _spells); // made on the first cast: a host's view may not hold its Action API until then
        _cast.Start(
            new CastRequest
            {
                Levels = _castIds,
                Rules = rules,
                Purpose = "vitals " + info.Name,
                FizzlesSpent = _castRow,
                WornItems = _session.State.Self.Inventory.Equipped.Count,
                ManaTryFloor = _castFloor,
                Proof = dance ? _danceProof ??= new CastChatProof(_session.State, _spells) : null, // M5 1 fix B (G5): a landed press is proved by ACE's transfer line; the other casts ask no proof
            },
            _now);
        return Send(Pending.Cast, null);
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
            return Send(Pending.Mode, _session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Automation));
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

    private VitalsKeeperState Send(Pending kind, ActionHandle? handle)
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
        if (_pending == Pending.Cast)
        {
            // The keeper's casts go through the cast policy (M5 D13): the attempt carries the fizzle retries and answers once.
            if (_cast!.Step(now) == CastEnd.Running)
            {
                return false;
            }

            _pending = Pending.None;
            SettleCast(_cast.Report);
            return true;
        }

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
        return Send(mana ? Pending.ManaPotion : Pending.StaminaPotion, _session.Actions.Drink(guid, ActionSource.Automation));
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

    private void SettleCast(in CastReport report)
    {
        var line = (int)_castLine;
        var row = _castRow + report.Fizzles;
        Fizzles += report.Fizzles;
        _actions += Math.Max(0, report.Tries - 1); // the retries are casts too: the pass's cap counts them as the looks did
        _tally.Add(report);
        if (_castIsDance)
        {
            SettleDance(report);
            return;
        }

        if (report.End == CastEnd.ManaShort && report.ManaRefusals > 0 && _options.Mana.On)
        {
            // D15: a 0x0401 inside the tries is expected, never a failure; once they are spent the line waits for its ceiling until the next station's needs (the order's step 3).
            _fizzleRow[line] = row;
            _triesSpent[line] = true;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitals: {_castName}: {report.ManaRefusals} Mana Conversion tries refused (0x0401, nothing charged); {VitalsMeans.NameOf(_castLine)} now waits for its ceiling"));
            return;
        }

        if (report.End.Succeeded())
        {
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

            _log.Info(LogCategory, report.Fizzles == 0 ? $"vitals: {_castName} landed" : $"vitals: {_castName} landed after {report.Fizzles} fizzle(s)");
            return;
        }

        _fizzleRow[line] = row;
        if (report.End == CastEnd.Fizzled || (report.End == CastEnd.ManaShort && report.ManaRefusals == 0 && report.Fizzles > 0))
        {
            // Three fizzles in a row, whichever attempts they fell in, set the line aside; the mana read under the retry's ceiling (nothing sent) is a fizzle the next look decides on.
            if (row >= MaxFizzlesInRow)
            {
                _lineAside[line] = true;
                _log.Warn(LogCategory, $"vitals: {_castName} fizzled {row} times in a row; {VitalsMeans.NameOf(_castLine)} is set aside until the next station's needs");
            }
            else
            {
                _log.Info(LogCategory, $"vitals: {_castName} fizzled ({row} in a row); the next look decides again");
            }

            return;
        }

        _lineAside[line] = true;
        var last = _cast!.LastOutcome;
        var what = last is { } outcome ? $"{outcome.Kind} 0x{outcome.Code:X4}: {outcome.Detail}" : $"{report.End.Name()}: {report.Why}";
        _log.Warn(LogCategory, $"vitals: {_castName} was not carried out ({what}); {VitalsMeans.NameOf(_castLine)} is set aside until the next station's needs");
    }

    /// <summary>The end of a dance (D27): every press counted; a landing is a transfer; presses spent with none landed leave the next look to decide (the actions cap bounds it); any other end sets the line aside.</summary>
    private void SettleDance(in CastReport report)
    {
        DancePresses += report.Tries;
        if (report.End.Succeeded())
        {
            DancesLanded++;
            Transfers++;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitals: the dance landed {_castName} on press {report.Tries} ({report.ManaRefusals} refused, {report.Fizzles} fizzled)"));
            return;
        }

        _lineAside[(int)_castLine] = true;
        if (report.End is CastEnd.ManaShort or CastEnd.TimeCap)
        {
            // Every press refused or fizzled: the server's costs are not the model's. Named; the line is set aside until the next station's needs (a rest for mana instead).
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitals: the dance of {_castName} did not land in {report.Tries} presses ({report.ManaRefusals} refused, {report.Fizzles} fizzled); {VitalsMeans.NameOf(_castLine)} is set aside until the next station's needs"));
            return;
        }

        var what = _cast!.LastOutcome is { } outcome ? $"{outcome.Kind} 0x{outcome.Code:X4}: {outcome.Detail}" : $"{report.End.Name()}: {report.Why}";
        _log.Warn(LogCategory, $"vitals: the dance of {_castName} was not carried out ({what}); {VitalsMeans.NameOf(_castLine)} is set aside until the next station's needs");
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
                return Send(Pending.Mode, _session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Automation));
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
        if (PostureDown && _pending == Pending.None && !_session.Actions.KillSwitch)
        {
            _log.Info(LogCategory, "vitals: standing up (the stop key) as the keeper stops");
            _pending = Pending.Stand;
            _handle = _session.Actions.Move.Command(MotionCommands.Ready, true, ActionSource.Automation);
        }

        if (_swappedIn && _swap is not null && _swapPhase == SwapPhase.None && !_session.Actions.KillSwitch)
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
        var need = FloorNeed(Math.Max(Needs.NextCastMana, (uint)Math.Ceiling(Math.Clamp(Needs.MinManaPercent, 0, 100) * (double)mMax / 100)), mMax);
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
