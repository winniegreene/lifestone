using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.World;
using Lifestone.Data.Motion;
using Lifestone.Data.World;
using Lifestone.Data.World.Data;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M4's hunt loop in its first form (<c>--scenario=hunt</c>, spec 5.10, plan 4.2 s4a): "Holtburg Hunter" binds at Holtburg's lifestone, goes to the Low B belt (<see cref="HuntGround.LowB"/>), fights the
/// nearest creature of the belt's names until <see cref="HuntOptions.Kills"/> kills or <see cref="HuntOptions.Seconds"/>, and comes back to Holtburg before it logs off. It acts only through
/// <see cref="IActionApi"/> and <see cref="PlayerTools"/>.
/// </summary>
/// <remarks>
/// <para><b>Stages</b> (<see cref="Stage"/>): <c>login choosing entering arriving name</c> (the character is entered by name and never another: a pre-entered session is checked, <c>fighter=wrong</c> fails
/// the run before anything is sent; with the player already in the world, as the gear scenario does, the login is skipped), <c>bind-*</c> (<c>place poi</c> Holtburg when the player is not in its landblock,
/// a 3 s settle, the nearest lifestone object, <c>Use</c>; once per run, M3 D34), <c>health heal-use heal-wait</c> (M4 3.1 s3, the driver of <see cref="RecoveryRules"/>: before the first fight and between kills, under <c>--heal-below</c> (75%) of the maximum
/// health a Handy Healing Kit by <c>UseWithTarget</c> while its estimated chance holds, else a Potion of Healing by <c>Drink</c>, else up to 300 s of regeneration, then a loud failure; never during a fight, where the retreat at 30% stays the rule, <c>TODO(m4-hunt-infight-heal)</c>;
/// <c>--heal off</c> runs none), <c>swap-*</c> (missile only, capture.md C2's order: NonCombat, the Mace to the pack, the Shortbow
/// wielded, the Arrows wielded), <c>acquire query mode fight-* kill-wait</c>, then the wind-down <c>wind-*</c> (cancel a running sequence, NonCombat, restore the Mace, <c>place poi</c> Holtburg, log off,
/// quit). A failure after the character is verified goes through the same wind-down, so the run still returns; only a run that cannot return reports <c>returned=no</c>.</para>
/// <para><b>The target.</b> <c>WorldObjectTable.NearestCreature</c> within 60 m (35 m under the bow) of the creatures whose weenie class id is in the ground's table
/// (<see cref="HuntGround.LowBTable"/> for the belt, <see cref="HuntGround.LowNorthForestTable"/> for the forest: <c>--hunt-ground</c>, M4 3.1 s0) <b>and</b> whose level, read from that table (the level is not on the
/// wire: <c>TODO(m4-creature-level)</c>), is in the ground's band (the belt's 8 to 20: the level-30 Tumerok Warrior is listed and filtered; the forest's 30 to 40). A creature that is not in the table is never a target. None in range: the next dry cell (at most <see cref="MaxCells"/> cells in all), then the run fails. A refused Attack (the 02EB "You cannot
/// attack", ACE's 0x04EB, or any other non-Confirmed outcome) excludes that target and picks the next; nothing is sent twice for the same refusal.</para>
/// <para><b>The attack.</b> One <c>Attack</c> (or <c>MissileAttack</c>) per sequence: ACE walks the player in and repeats the swing or the shot by itself (capture.md C1, C3). The next is sent only when the
/// sequence's Done (<see cref="AttackSequence"/>) is in and the target lives; a kill waits for the Done or <see cref="KillDoneSeconds"/> first (the Done trails the corpse by about a second, and an Attack
/// sent in the meantime is queued and lost). A health fall under 30% mid-fight cancels the attack, goes to NonCombat and returns; the run fails (<c>returned=yes</c>).</para>
/// <para><b>The verdict</b> (<see cref="VerdictFields"/>, logged as <c>[hunt] verdict ...</c>): <c>fighter=ok|wrong kills swings shots hits misses damage_dealt damage_taken hits_taken evades xp_gained
/// returned=yes|no</c>. All are report-only in item 1.2 (the plan's section 5 item 3). <see cref="DetailFields"/> adds the run's own facts.</para>
/// </remarks>
public sealed partial class HuntScenario : IScenario
{
    /// <summary>The scenario's token (<c>--scenario=hunt</c>).</summary>
    public const string ScenarioName = "hunt";

    /// <summary>The log category of the scenario's lines (<c>[hunt] ...</c>).</summary>
    public const string LogCategory = HuntGround.LogCategory;

    /// <summary>The longest regeneration wait of one recovery, in seconds (<see cref="RecoveryRules.RegenSeconds"/>: 1.2's wait; live round 2: regeneration alone reached 77% in 300 s).</summary>
    public const double RegenSeconds = RecoveryRules.RegenSeconds;

    /// <summary>The longest a heal's handle is waited for, in seconds (the Heal lane's own timeout is 20 s from the send).</summary>
    public const double HealHandleSeconds = 30;

    /// <summary>The most heals one recovery sends before it fails loudly (a safety net against a server that heals nothing, never a rule: the kit has 20 uses and the potions 5).</summary>
    public const int MaxHealsPerRecovery = 40;

    /// <summary>The wait before a heal that ACE refused as busy (WeenieError 0x001D: the Heal lane holds its own chain, but a pick-up or a close of the corpse just before is another lane's) is sent again, in seconds.</summary>
    public const double HealBusyRetrySeconds = 1;

    /// <summary>The most busy refusals one recovery answers with a wait; the next counts as a refusal like any other.</summary>
    public const int MaxHealBusyRetries = 10;

    /// <summary>How often the Healing skill's value is read again, in seconds (the read may allocate while enchantments are on).</summary>
    public const double SkillRefreshSeconds = 5;

    /// <summary>The health fraction under which a fight is abandoned.</summary>
    public const float RetreatShare = 0.3f;

    /// <summary>The distance within which a listed melee target is looked for, in metres.</summary>
    public const float MeleeRange = 60f;

    /// <summary>The distance within which a missile target is looked for, in metres.</summary>
    public const float MissileRange = 35f;

    /// <summary>The most cells the hunt stands in before it fails for want of a listed creature in the band.</summary>
    public const int MaxCells = 4;

    /// <summary>The settle after an arrival, in seconds on the session clock.</summary>
    public const double SettleSeconds = 3;

    /// <summary>
    /// The most times one target's melee approach is retried after ACE cancels it with <c>YouChargedTooFar</c> (0x003D): the approach is cancelled once the player is more than 15 m (<c>FailDistance</c>,
    /// <c>W/Player_Move.cs</c> 167 and 241; <c>MoveToManager.cs</c> 489) from where it began, so a target farther than that takes several sends, each from where the last one stopped. Each retry must have brought the
    /// player at least <see cref="ChargeGainMetres"/> closer, or the target is excluded (a walk that does not close in).
    /// </summary>
    public const int MaxChargeRetries = 6;

    /// <summary>The least a retried approach must close the distance to the target, in metres.</summary>
    public const float ChargeGainMetres = 3f;

    /// <summary>The longest wait after a kill for the sequence's Done, in seconds.</summary>
    public const double KillDoneSeconds = 3;

    /// <summary>
    /// The longest after the kill notice (the 01AD) the kill's corpse is waited for, in seconds (capture C2: the corpse's F745 came 0.54 s after the 01AD, then the target's F747). A corpse that has not appeared
    /// is logged and counted (<see cref="CorpsesMissed"/>), never a failure.
    /// </summary>
    public const double CorpseAppearSeconds = 5;

    /// <summary>How near the target's last position the kill's corpse must be, in metres (a corpse is created where the creature fell).</summary>
    public const float CorpseRangeMetres = 6f;

    /// <summary>The longest a precondition the read model has not caught up with is waited for, in seconds.</summary>
    public const double WaitSeconds = 10;

    /// <summary>The longest a teleport may take, in seconds.</summary>
    public const double PlaceSeconds = 30;

    /// <summary>The longest the lifestone's Use may take, in seconds.</summary>
    public const double UseSeconds = 60;

    /// <summary>How long a confirmed attack may go without the sequence beginning, in seconds.</summary>
    public const double SequenceBeginSeconds = 15;

    /// <summary>The power of a melee blow and the accuracy of a shot (the bar, 0 to 1): the value the capture proved.</summary>
    public const float AttackBar = 0.5f;

    private readonly HuntOptions _options;
    private readonly ILogSink _log;
    private readonly Func<HuntCell, float?> _groundHeight;
    private readonly HuntGroundRow _ground;
    private readonly IReadOnlyList<HuntCell> _cells;
    private readonly TeleportDestination _holtburg;
    private readonly uint _holtburgLandblock;
    private readonly HuntTally _tally = new();
    private readonly List<uint> _excluded = [];
    private readonly List<uint> _scratch = [];
    private readonly HashSet<string> _sent = [];
    private readonly ReasonTimer _wait = new(WaitSeconds);
    private readonly List<IDisposable> _subscriptions = [];
    private ActionHandle? _pending;
    private ActionHandle? _abandonedOpen;
    private InFlight? _step;
    private bool _attached;
    private bool _acting;
    private bool _winding;
    private string _fighter = "ok";
    private double _clock;
    private double _arrivalStart = double.NaN;
    private double _stageStart;
    private double _settleUntil;
    private double _huntStart = double.NaN;
    private double _sentAt;
    private int _teleports;
    private int _teleportMark;
    private string _placeNext = string.Empty;
    private TeleportDestination? _placeDestination;
    private bool _placeHome;
    private double _placeStart;
    private bool _leftHome;
    private bool _returned;
    private bool _bound;
    private bool _swapped;
    private uint _arrowStack;
    private uint _maceAside;
    private readonly HealTally _healTally = new();
    private HealHandle? _heal;
    private string? _afterHeal;
    private bool _recoveryActive;
    private double _waitStart = double.NaN;
    private int _healSends;
    private int _lostRolls;
    private uint _lostRollsAt;
    private int _kitRefusals;
    private double _busyUntil;
    private int _busyRetries;
    private int _potionRefusals;
    private bool _kitOff;
    private bool _potionOff;
    private double _skillAt = double.NaN;
    private int _healing = RecoveryRules.FallbackHealingSkill;
    private bool _specialised;
    private bool _healingRead;
    private bool _skillWarned;
    private double _healthBefore = double.NaN;
    private double _healthLast = double.NaN;
    private double _activeSince = double.NaN;
    private int _stalls;
    private bool _awaitStart;
    private bool _chargedTooFar;
    private uint? _noStartCode;
    private int _chargeRetries;
    private int _chargeRetriesTotal;
    private double _distAtSend;
    private int _cellIndex = -1;
    private int _cellsTried;
    private uint _target;
    private string _targetName = string.Empty;
    private int _killsAtTarget;
    private int _seqNumber;
    private int _empty;
    private bool _damageWaitLogged;
    private int _attacks;
    private int _refused;
    private uint _lifestone;
    private readonly bool _endSession;
    private readonly IHuntFollow? _follow;
    private bool _followWarned;
    private CorpsePhase _corpse = CorpsePhase.None;
    private uint _corpseGuid;
    private CellFrame? _killPosition;
    private readonly List<uint> _corpsesBefore = [];
    private int _corpsesOpened;
    private int _corpseItems;
    private int _corpsesMissed;
    private int _corpsesRefused;
    private CorpseLooter? _looter;
    private CorpseLooter? _drainLooter;
    private int _drainFolded;
    private readonly LootRules _lootRules;
    private bool _mergeGapLogged;

    private enum CorpsePhase
    {
        None,
        Seek,
        Opening,
        Opened,

        /// <summary>M4 2.2 s0: the contents are listed and the <see cref="CorpseLooter"/> is taking them, one pick-up at a time; the close waits for it.</summary>
        Looting,
        Closing,
        Finished,
    }

    /// <summary>The hunt.</summary>
    /// <param name="options">The kills, the time and the weapon.</param>
    /// <param name="log">Where the scenario logs.</param>
    /// <param name="groundHeight">The terrain height at a cell's centre (<see cref="HuntGround.TerrainHeight"/> over the dats), or null when unknown: the hunt will not go to a cell it has no height for.</param>
    /// <param name="ground">The ground to hunt (default <see cref="HuntGround.LowB"/>; <see cref="HuntOptions.GroundRow"/> is the <c>--hunt-ground</c> choice); a row that is not <see cref="HuntGroundRow.InUse"/> is refused.</param>
    /// <param name="arrivalSeconds">The limit on the arrival after Enter.</param>
    /// <param name="follow">The client's own body, headless (<see cref="HuntFollow"/>): it follows the server's MoveTo of the approach and reports its positions, as retail's client physics does; null in the game (the player controller's walker already does it) and in hosts without physics, where the distance is the session's.</param>
    /// <param name="endSession">False for a host that logs off itself (a leg of the login check, the offline stand-in, which refuses the log off and the quit): the run ends at the return.</param>
    public HuntScenario(HuntOptions options, ILogSink log, Func<HuntCell, float?> groundHeight, HuntGroundRow? ground = null, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, bool endSession = true, IHuntFollow? follow = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(groundHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arrivalSeconds);
        _ground = ground ?? HuntGround.LowB;
        if (!_ground.InUse)
        {
            throw new ArgumentException($"TODO(m4-hunt-ground-unused): the ground '{_ground.Name}' is not in use (HuntGroundRow.InUse is false)", nameof(ground));
        }

        _options = options;
        _lootRules = options.Loot.Rules();
        _follow = follow;
        _endSession = endSession;
        _log = log;
        _groundHeight = groundHeight;
        ArrivalSeconds = arrivalSeconds;
        _cells = [.. _ground.UsableCells];
        _holtburg = TeleportCatalog.FromAce(AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg"));
        _holtburgLandblock = _holtburg.Location.Position.CellId >> 16;
    }

    /// <inheritdoc/>
    public string Name => ScenarioName;

    /// <inheritdoc/>
    public string Stage { get; private set; } = "start";

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <summary>The limit on the arrival, in seconds.</summary>
    public double ArrivalSeconds { get; }

    /// <summary>The character entered (its guid), or 0.</summary>
    public uint EnteredGuid { get; private set; }

    /// <inheritdoc/>
    public double? WhereMaxMetres => null;

    /// <inheritdoc/>
    /// <remarks>Always 0: the scenario sends no movement (ACE walks the player in to its target).</remarks>
    public long Unmatched => 0;

    /// <summary>The fighter check's result for the verdict line: <c>ok</c> or <c>wrong</c> (the entered character is not the template's).</summary>
    public string Fighter => _fighter;

    /// <summary>The creature the hunt is attacking now (its object id), or 0 between targets (read by the hunt leg's review frames).</summary>
    public uint Target => _target;

    /// <summary>True while the kill's corpse is open and its close not yet sent, the loot included (read by the hunt leg's <c>corpse</c> review frame and its loot frame window).</summary>
    public bool CorpseOpen => _corpse is CorpsePhase.Opened or CorpsePhase.Looting;

    /// <summary>
    /// True from a corpse's open being sent to its close being settled (or the sequence being abandoned, or the open refused): the window <see cref="LootFrameSampler"/> measures (<c>loot_frame_max_ms</c>, M4 2.2 s0).
    /// </summary>
    public bool CorpseWindow => _corpse is CorpsePhase.Opening or CorpsePhase.Opened or CorpsePhase.Looting or CorpsePhase.Closing;

    /// <summary>True while the loot loop is taking items from the corpse (M4 2.2 s0).</summary>
    public bool Looting => _corpse == CorpsePhase.Looting;

    /// <summary>The items taken from the kills' corpses (<c>looted</c>; the PickUps Confirmed).</summary>
    public int Looted => _tally.Looted;

    /// <summary>The corpse entries the loot rule left (<c>loot_skipped</c>).</summary>
    public int LootSkipped => _tally.LootSkipped;

    /// <summary>The kill's corpse (its object id), or 0 before one was found.</summary>
    public uint CorpseGuid => _corpseGuid;

    /// <summary>The name of <see cref="Target"/> (empty when none).</summary>
    public string TargetName => _targetName;

    /// <summary>The run's counters.</summary>
    public HuntTally Tally => _tally;

    /// <summary>True when the run bound at the lifestone (the Use was Confirmed).</summary>
    public bool Bound => _bound;

    /// <summary>The Potions of Healing drunk (confirmed; <see cref="HealTally.Potions"/>).</summary>
    public int PotionsUsed => _healTally.Potions;

    /// <summary>The heal counters (M4 3.1 s3): the kit's uses, the rolls won and lost, the potions, the points and the wasted drinks.</summary>
    public HealTally HealTally => _healTally;

    /// <summary>The recoveries that raised the health (<c>heals</c> on the leg line): kit uses whose roll won plus potions that restored something.</summary>
    public int Heals => _healTally.Heals + _healTally.Potions - _healTally.Wasted;

    /// <summary>The health share (0 to 1) the hunt first read from the session's vitals (<c>health_start_pct</c> on the detail line), or null before it read one: the verdict's L2 judge reads it.</summary>
    public double? HealthStartShare => double.IsNaN(_healthBefore) ? null : _healthBefore;

    /// <summary>The kit uses confirmed, won or lost (<c>kit_uses</c> on the leg line).</summary>
    public int KitUses => _healTally.KitUses;

    /// <summary>The cells the hunt stood in.</summary>
    public int CellsVisited => _cellsTried;

    /// <summary>The sequences that never ended and were cancelled (<see cref="FightSend"/>'s bounded wait).</summary>
    public int Stalls => _stalls;

    /// <summary>The melee approaches retried after <c>YouChargedTooFar</c> (the walk to a far target, in 15 m legs).</summary>
    public int ChargeRetries => _chargeRetriesTotal;

    /// <summary>The Attack or MissileAttack sends.</summary>
    public int Attacks => _attacks;

    /// <summary>The kills' corpses whose open was Confirmed (<c>corpses_opened</c>; M4 2.1 s4a).</summary>
    public int CorpsesOpened => _corpsesOpened;

    /// <summary>The entries the opened corpses showed, summed (<c>corpse_items</c>).</summary>
    public int CorpseItems => _corpseItems;

    /// <summary>The kills whose corpse did not appear within <see cref="CorpseAppearSeconds"/> (logged, never a failure).</summary>
    public int CorpsesMissed => _corpsesMissed;

    /// <summary>The corpse opens the server refused or that timed out (logged, never a failure).</summary>
    public int CorpsesRefused => _corpsesRefused;

    /// <summary>True when the player ended in Holtburg's landblock (what <c>returned=</c> says).</summary>
    public bool Returned => _returned;

    /// <summary>True when the run ended in the missile weapon swap's restored state (the Mace wielded, no bow and no arrows wielded); false before the restore.</summary>
    public bool Restored { get; private set; }

    /// <summary>The verdict fields (no character name, no account).</summary>
    public string VerdictFields() =>
        string.Create(CultureInfo.InvariantCulture, $"fighter={_fighter} {_tally.Fields()} returned={(_returned ? "yes" : "no")}");

    /// <summary>The run's own facts, beside the verdict: the weapon, the bind, the health (report-only start and end), the cells and the sends.</summary>
    public string DetailFields() =>
        string.Create(CultureInfo.InvariantCulture, $"weapon={_options.Weapon.ToString().ToLowerInvariant()} bound={(_bound ? "yes" : "no")} potions={_healTally.Potions} heal_failed_rolls={_healTally.FailedRolls} heal_points={_healTally.Points} heal_wasted={_healTally.Wasted} cells={_cellsTried} attacks={_attacks} refused={_refused} restored={(Restored ? "yes" : "no")} stalls={_stalls} charge_retries={_chargeRetriesTotal} corpses_missed={_corpsesMissed} corpses_refused={_corpsesRefused} looted={_tally.Looted} loot_skipped={_tally.LootSkipped} health_start_pct={Pct(_healthBefore)} health_end_pct={Pct(_healthLast)} {VendorFields()}");

    private static string Pct(double share) => double.IsNaN(share) ? "n/a" : (share * 100).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>True when <paramref name="name"/> is the fighter template's character.</summary>
    public static bool NameCheck(string? name) => FighterBuildScenario.NameCheck(name);

    /// <inheritdoc/>
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        _clock = session.Clock.Now;
        if (HealthShare(session) is { } healthNow)
        {
            _healthLast = healthNow; // report-only: the last health read (health_end_pct)
        }

        if (_follow is { } follow)
        {
            follow.Prepare(session, now);
            follow.Tick(session, now);
        }

        PollAbandonedOpen(session);
        StepLootDrain(session);
        StepAppraise(session); // M4 3.2 s4a: --appraise-loot (HuntScenario.Vendor.cs)
        if (!_attached)
        {
            _attached = true;
            _tally.Start(session);
            _subscriptions.Add(session.Events.Subscribe<ObjectMoved>(OnMoved));
            _subscriptions.Add(session.Events.Subscribe<AttackChanged>(OnAttackChanged));
            _subscriptions.Add(session.Events.Subscribe<ChatReceived>(OnChat));
        }

        switch (Stage)
        {
            case "done":
                return Failure is null ? ScenarioStatus.Succeeded : ScenarioStatus.Failed;
            case "failed":
                return ScenarioStatus.Failed;
            case "start":
                if (session.State.Phase == SessionPhase.InWorld && session.State.Self.Guid != 0)
                {
                    // Already in the world (a host that logged in itself, or the offline stand-in): nothing to log in.
                    EnteredGuid = session.State.Self.Guid;
                    _arrivalStart = _clock;
                    return Enter("arriving");
                }

                _pending = session.Actions.Login(ActionSource.Host);
                return Enter("login");
            case "login":
                if (Settled(out var login))
                {
                    return login.Kind == ActionOutcomeKind.Confirmed ? Enter("choosing") : Fail(session, "login", login);
                }

                break;
            case "choosing":
                return Choose(session);
            case "entering":
                if (Settled(out var entered))
                {
                    if (entered.Kind != ActionOutcomeKind.Confirmed)
                    {
                        return Fail(session, "entering the world", entered);
                    }

                    _arrivalStart = _clock;
                    return Enter("arriving");
                }

                break;
            case "arriving":
                if (session.State.Self.Travel == SelfTravel.Present)
                {
                    return Enter("name");
                }

                if (_clock - _arrivalStart > ArrivalSeconds)
                {
                    return Fail(session, string.Create(CultureInfo.InvariantCulture, $"arriving: the player was not unhidden within {ArrivalSeconds:0.#} s (travel {session.State.Self.Travel})"));
                }

                break;
            case "name":
                return NameStep(session);
            case "bind":
                return BindStart(session);
            case "bind-settle":
                return BindSettle(session);
            case "bind-use":
                return BindUse(session);
            case "health":
                return HealthStep(session);
            case "heal-use":
                return HealUse(session);
            case "heal-wait":
                return HealthStep(session); // the same look every tick: nothing helps now, regeneration is waited for
            case "swap":
                return SwapStep(session);
            case "ground":
                return GroundStart(session);
            case "placing":
                return Placing(session);
            case "acquire-settle":
                return _clock >= _settleUntil ? Enter("acquire") : ScenarioStatus.Running;
            case "acquire":
                return Acquire(session);
            case "query":
                return Query(session);
            case "mode":
                return Mode(session);
            case "mode-wait":
                return ModeWait(session);
            case "fight-send":
                return FightSend(session);
            case "fight-sent":
                return FightSent(session);
            case "fight-run":
                return FightRun(session);
            case "kill-wait":
                return KillWait(session);
            case "wind-cancel":
                return WindCancel(session);
            case "wind-peace":
                return WindPeace(session);
            case "wind-restore":
                return WindRestore(session);
            case "wind-home":
                return WindHome(session);
            case "logging-off":
                if (Settled(out var loggedOff))
                {
                    if (loggedOff.Kind != ActionOutcomeKind.Confirmed)
                    {
                        Problem(session, "logging off: " + Describe(loggedOff));
                    }

                    _pending = session.Actions.Quit(ActionSource.Host);
                    return Enter("quitting");
                }

                break;
            case "quitting":
                if (Settled(out var quit))
                {
                    if (quit.Kind != ActionOutcomeKind.Sent)
                    {
                        Problem(session, "quitting: " + Describe(quit));
                    }

                    return Done();
                }

                break;
            default:
                if (Stage.StartsWith("vendor-", StringComparison.Ordinal))
                {
                    return VendorStep(session); // M4 3.2 s4a: the sale and the buy at the loop's end (HuntScenario.Vendor.cs); a failure there never fails the hunt
                }

                return Fail(session, $"TODO(state): unknown hunt stage '{Stage}'");
        }

        return ScenarioStatus.Running;
    }

    // ------------------------------------------------------------------------------------------------ the character

    private ScenarioStatus Choose(IGameSessionView session)
    {
        foreach (var c in session.State.Characters.Entries)
        {
            if (NameCheck(c.Name))
            {
                if (c.PendingDeletion)
                {
                    return Fail(session, "choosing: the fighter is pending deletion (restore it first)");
                }

                EnteredGuid = c.Guid;
                _log.Info(LogCategory, "entering the existing character " + FighterTemplate.CharacterName);
                _pending = session.Actions.EnterWorld(c.Guid, ActionSource.Host);
                return Enter("entering");
            }
        }

        return Fail(session, "choosing: " + FighterTemplate.CharacterName + " is not in the character list (run --scenario=fighter-build first; the hunt creates nobody)");
    }

    private ScenarioStatus NameStep(IGameSessionView session)
    {
        var name = session.State.Self.Name;
        if (name is null)
        {
            if (_wait.Expired(_clock, "the entered character's name is not known"))
            {
                return Fail(session, "name: the entered character's name was not known within " + WaitSeconds.ToString("0", CultureInfo.InvariantCulture) + " s");
            }

            return ScenarioStatus.Running;
        }

        if (!NameCheck(name))
        {
            _fighter = "wrong";
            return Fail(session, "the entered character is not " + FighterTemplate.CharacterName + ": nothing is sent for it");
        }

        _fighter = "ok";
        _acting = true;
        _wait.Reset();
        _log.Info(LogCategory, $"the hunt is {_options}; ground {_ground.Name}: {_cells.Count} usable cell(s) of {_ground.Cells.Count}");
        _log.Info(LogCategory, $"recovery: {_options.Recovery}; checked before the first fight and between kills, never during a fight: the retreat at {RetreatShare * 100:0}% health ends a fight (TODO(m4-hunt-infight-heal))");
        if (_cells.Count == 0)
        {
            return Fail(session, "the ground has no usable cell (TODO(m4-belt-cell-terrain): every cell reads water or steep)");
        }

        return Enter("bind");
    }

    // ------------------------------------------------------------------------------------------------ the bind (M3 D34)

    private ScenarioStatus BindStart(IGameSessionView session)
    {
        if (InHoltburg(session))
        {
            _settleUntil = _clock;
            return Enter("bind-settle");
        }

        BeginPlace(session, _holtburg, "bind-settle", home: false);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus BindSettle(IGameSessionView session)
    {
        if (_clock < _settleUntil)
        {
            return ScenarioStatus.Running;
        }

        if (session.State.Self.Position is not { } here)
        {
            return Fail(session, "bind: the player has no position");
        }

        if (!session.State.Objects.NearestWithFlags(WorldObjectTable.LifestoneFlag, in here, DeathScenario.LifestoneSearchMetres, out var guid))
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"bind: no lifestone (an object with the BF_LIFESTONE description flag) within {DeathScenario.LifestoneSearchMetres:0} m of the player in cell 0x{here.CellId:X8}: the run goes on unbound (TODO(m4-hunt-bind))"));
            return Enter("health");
        }

        _lifestone = guid;
        _log.Info(LogCategory, $"bind: using the lifestone 0x{guid:X8}");
        _pending = session.Actions.Use(guid, ActionSource.Host);
        return Enter("bind-use");
    }

    private ScenarioStatus BindUse(IGameSessionView session)
    {
        if (Settled(out var used))
        {
            _bound = used.Kind == ActionOutcomeKind.Confirmed;
            if (_bound)
            {
                _log.Info(LogCategory, $"bound at the lifestone 0x{_lifestone:X8}");
            }
            else
            {
                _log.Warn(LogCategory, "bind: the Use was not confirmed (" + Describe(used) + "); the run goes on unbound (TODO(m4-hunt-bind))");
            }

            return Enter("health");
        }

        if (_clock - _stageStart > UseSeconds)
        {
            _log.Warn(LogCategory, "bind: the lifestone's Use did not finish within " + UseSeconds.ToString("0", CultureInfo.InvariantCulture) + " s; the run goes on unbound (TODO(m4-hunt-bind))");
            _pending = null;
            return Enter("health");
        }

        return ScenarioStatus.Running;
    }

    // ------------------------------------------------------------------------------------------------ health: the recovery rule's driver (M4 3.1 s3; closes TODO(m4-hunt-recovery))

    /// <summary>The health as a fraction of the maximum, or null when the vitals are not known.</summary>
    private static float? HealthShare(IGameSessionView session)
    {
        var q = session.State.Self.Qualities;
        if (!q.IsKnown || !q.TryGetVitalLevels(MageTemplate.MaxHealth, out var current, out var max) || max == 0)
        {
            return null;
        }

        return (float)current / max;
    }

    private static bool HealthNumbers(IGameSessionView session, out uint current, out uint max)
    {
        var q = session.State.Self.Qualities;
        current = 0;
        max = 0;
        return q.IsKnown && q.TryGetVitalLevels(MageTemplate.MaxHealth, out current, out max) && max != 0;
    }

    private static string PctOf(uint current, uint max) => max == 0 ? "unknown" : ((double)current * 100 / max).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// The health stage, the driver of <see cref="RecoveryRules"/> (M4 3.1 s3): run before the first fight (from the bind) and between kills (<see cref="BetweenKills"/>), never during a fight (the retreat at
    /// <see cref="RetreatShare"/> stays the in-fight rule, <c>TODO(m4-hunt-infight-heal)</c>). Each look asks the rule (<see cref="RecoveryRules.Decide(in RecoveryInput, RecoveryOptions, out double)"/>): the kit by
    /// <c>UseWithTarget</c> on the player, a potion by <c>Drink</c>, a wait for regeneration (the stage <c>heal-wait</c>, this same look every tick), a loud failure, or nothing (the recovery is over). The Heal lane holds the
    /// busy chain: the next heal is sent as soon as the last one's outcome is in, and the lane releases it after the UseDone and the Ready.
    /// </summary>
    private ScenarioStatus HealthStep(IGameSessionView session)
    {
        if (!HealthNumbers(session, out var current, out var max))
        {
            if (_wait.Expired(_clock, "the vitals are not known"))
            {
                _log.Warn(LogCategory, "health: the vitals were not known within " + WaitSeconds.ToString("0", CultureInfo.InvariantCulture) + " s; the run goes on with the health unknown and no recovery");
                return AfterRecovery(session);
            }

            return ScenarioStatus.Running;
        }

        _wait.Reset();
        if (_clock < _busyUntil)
        {
            return ScenarioStatus.Running; // a heal ACE refused as busy waits its second (HealUse); the health read is the same
        }

        if (double.IsNaN(_healthBefore))
        {
            _healthBefore = (float)current / max;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: {PctOf(current, max)} of the maximum ({current} of {max})"));
        }

        if (_afterHeal is not null && !double.IsNaN(_huntStart) && _clock - _huntStart > _options.Seconds)
        {
            return Fail(session, string.Create(CultureInfo.InvariantCulture, $"time: {_options.Seconds:0.#} s of hunting passed with {_tally.Kills} of {_options.Kills} kill(s), during a recovery at {PctOf(current, max)} health"));
        }

        if (_lostRolls > 0 && current != _lostRollsAt)
        {
            _lostRolls = 0; // a lost roll changes nothing, so the chance stands until the health moves (regeneration, a potion): then the kit is weighed again
        }

        var kit = FindKit(session, out var kitGuid, out var kitUses) && !_kitOff;
        var potion = FindPotions(session, out var potionGuid, out var potions) && !_potionOff;
        RefreshHealingSkill(session);
        var recovery = _options.Recovery;
        var waited = double.IsNaN(_waitStart) ? 0 : _clock - _waitStart;
        var input = new RecoveryInput(current, max, _recoveryActive, kit ? kitUses : 0, potion ? potions : 0, _lostRolls, waited, _healing, _specialised, InCombatMode(session));
        var decision = RecoveryRules.Decide(in input, recovery, out var chance);
        var missing = max - current;
        switch (decision)
        {
            case RecoveryDecision.None:
                if (_recoveryActive || !double.IsNaN(_waitStart))
                {
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: {PctOf(current, max)} holds (heal-below {recovery.BelowPercent}%, heal-until {recovery.UntilPercent}%): the recovery is over after {_healSends} heal(s) and {waited:0} s of regeneration; kit uses {_healTally.KitUses}, potions {_healTally.Potions}"));
                }

                _recoveryActive = false;
                _waitStart = double.NaN;
                _healSends = 0;
                return AfterRecovery(session);
            case RecoveryDecision.Kit:
            case RecoveryDecision.KitLastResort:
                _recoveryActive = true;
                _waitStart = double.NaN;
                if (TooManyHeals(session, current, max, out var tooMany))
                {
                    return tooMany;
                }

                var wanted = decision == RecoveryDecision.KitLastResort
                    ? string.Create(CultureInfo.InvariantCulture, $"under the {recovery.Chance:0.##} wanted, but no potion can be drunk and the kit is the last resort before the regeneration wait (at least {recovery.FloorChance:0.##} needed, --heal-floor-chance)")
                    : string.Create(CultureInfo.InvariantCulture, $"at least {recovery.Chance:0.##} wanted");
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"heal: kit 0x{kitGuid:X8} ({kitUses} use(s) left) at {PctOf(current, max)} ({missing} missing), estimated chance {chance:0.00} ({wanted}), healing {_healing}{(_healingRead ? string.Empty : " (the fallback)")}, {(input.CombatMode ? "combat mode" : "peace")}"));
                _heal = session.Actions.UseWithTarget(kitGuid, session.State.Self.Guid, ActionSource.Host);
                return Enter("heal-use");
            case RecoveryDecision.Potion:
                _recoveryActive = true;
                _waitStart = double.NaN;
                if (TooManyHeals(session, current, max, out var tooManyPotions))
                {
                    return tooManyPotions;
                }

                var why = _kitOff ? "the kit is refused or Healing is untrained" : kitUses <= 0 || !kit ? "no kit with uses is held" : _lostRolls >= RecoveryRules.MaxLostRolls ? $"{_lostRolls} rolls lost in a row" : string.Create(CultureInfo.InvariantCulture, $"the kit's chance {chance:0.00} is under {recovery.Chance:0.##}");
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"heal: potion 0x{potionGuid:X8} ({potions} held) at {PctOf(current, max)} ({missing} missing): {why}"));
                _heal = session.Actions.Drink(potionGuid, ActionSource.Host);
                return Enter("heal-use");
            case RecoveryDecision.Wait:
                _recoveryActive = true;
                if (double.IsNaN(_waitStart))
                {
                    _waitStart = _clock;
                    _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"heal: {PctOf(current, max)} is under heal-below {recovery.BelowPercent}% and nothing helps now (kit uses {(kit ? kitUses : 0)}, estimated chance {(double.IsNaN(chance) ? "n/a" : chance.ToString("0.00", CultureInfo.InvariantCulture))}, potions {(potion ? potions : 0)}; the kit's last-resort floor is {recovery.FloorChance:0.##}): regenerating for up to {RecoveryRules.RegenSeconds:0} s"));
                    return Enter("heal-wait");
                }

                return ScenarioStatus.Running;
            default:
                return Fail(session, string.Create(CultureInfo.InvariantCulture, $"recovery: {PctOf(current, max)} of the maximum health and nothing can heal (kit uses {(kit ? kitUses : 0)}, potions {(potion ? potions : 0)}, {_healSends} heal(s) sent): {(waited >= RecoveryRules.RegenSeconds ? $"still under heal-below {recovery.BelowPercent}% after {RecoveryRules.RegenSeconds:0} s of regeneration" : $"under the retreat share {RetreatShare * 100:0}%, so no regeneration wait")}; no fight is started or continued, the run returns (m4-hunt-recovery: the rule's loud failure)"));
        }
    }

    /// <summary>A safety net, never a rule: more than <see cref="MaxHealsPerRecovery"/> heals sent in one recovery (a stand-in or a server that heals nothing) fails the run loudly instead of looping.</summary>
    private bool TooManyHeals(IGameSessionView session, uint current, uint max, out ScenarioStatus status)
    {
        status = ScenarioStatus.Running;
        if (_healSends < MaxHealsPerRecovery)
        {
            _healSends++;
            return false;
        }

        status = Fail(session, $"recovery: {_healSends} heals were sent in one recovery and the health is still {PctOf(current, max)} (a heal that does nothing); giving up");
        return true;
    }

    private ScenarioStatus HealUse(IGameSessionView session)
    {
        if (_heal is not { Outcome: { } outcome } done)
        {
            if (_clock - _stageStart > HealHandleSeconds)
            {
                _log.Warn(LogCategory, "heal: the use had no outcome within " + HealHandleSeconds.ToString("0", CultureInfo.InvariantCulture) + " s; counted as refused");
                NoteRefusal(_heal?.Kind == ActionKind.UseWithTarget, 0);
                _heal = null;
                return Enter("health");
            }

            return ScenarioStatus.Running;
        }

        _heal = null;
        var kit = done.Kind == ActionKind.UseWithTarget;
        _healTally.Record(done);
        if (outcome.Kind != ActionOutcomeKind.Confirmed)
        {
            _log.Warn(LogCategory, $"heal: the {(kit ? "kit's use" : "potion's drink")} was not confirmed ({Describe(outcome)})");
            if (outcome.Code == BusyCode && _busyRetries < MaxHealBusyRetries)
            {
                // ACE refuses a heal while the player is busy (0x001D): after a pick-up or the corpse's close just before, or the last heal's Ready. Not a refusal of the kit or the potion: tried again after a second, logged.
                _busyRetries++;
                _busyUntil = _clock + HealBusyRetrySeconds;
                _log.Warn(LogCategory, $"heal: the server was busy (0x001D); trying again in {HealBusyRetrySeconds:0} s (busy retry {_busyRetries} of {MaxHealBusyRetries})");
                return Enter("health");
            }

            NoteRefusal(kit, outcome.Code);
            return Enter("health");
        }

        _busyRetries = 0;
        if (kit)
        {
            _kitRefusals = 0;
        }
        else
        {
            _potionRefusals = 0;
        }

        switch (done.Result)
        {
            case { Kind: HealResultKind.FailedRoll } failed:
                _lostRolls++;
                HealthNumbers(session, out var now, out _);
                _lostRollsAt = now;
                _log.Info(LogCategory, $"heal: the kit's roll was lost (a use spent; {(failed.Left >= 0 ? failed.Left.ToString(CultureInfo.InvariantCulture) : "?")} left; {_lostRolls} lost in a row)");
                break;
            case { } result:
                _lostRolls = 0;
                _log.Info(LogCategory, $"heal: {(kit ? "the kit" : "the potion")} {(result.Kind == HealResultKind.Wasted ? "was wasted (0 points)" : "restored " + result.Amount.ToString(CultureInfo.InvariantCulture) + " " + result.Vital)}{(result.Critical ? " (a critical)" : string.Empty)}{(result.Left >= 0 ? $" ({result.Left} left)" : string.Empty)}");
                break;
            default:
                _log.Warn(LogCategory, $"heal: the {(kit ? "kit's use" : "potion's drink")} was confirmed but its line was not read (counted as a use, no amount)");
                break;
        }

        return Enter("health");
    }

    /// <summary>ACE's <c>WeenieError.YoureTooBusy</c> (0x001D), a heal's UseDone while the player is still busy.</summary>
    private const uint BusyCode = 0x001D;

    /// <summary>A heal that was not confirmed: Healing untrained (0x04FC) sets the kit aside at once, any other refusal after two in a row (the rule then weighs the potions, or waits).</summary>
    private void NoteRefusal(bool kit, uint code)
    {
        if (kit)
        {
            _kitRefusals++;
            if (code == 0x04FC || _kitRefusals >= 2)
            {
                _kitOff = true;
                _log.Warn(LogCategory, $"heal: the kit is set aside for the run ({(code == 0x04FC ? "Healing is untrained, ACE's 0x04FC" : _kitRefusals + " refusals in a row")})");
            }
        }
        else
        {
            _potionRefusals++;
            if (_potionRefusals >= 2)
            {
                _potionOff = true;
                _log.Warn(LogCategory, $"heal: the potions are set aside for the run ({_potionRefusals} refusals in a row)");
            }
        }
    }

    /// <summary>The first held Handy Healing Kit with uses left (its Structure, the 02CE the reducer mirrors; unknown counts as one use).</summary>
    private bool FindKit(IGameSessionView session, out uint guid, out int uses)
    {
        var state = session.State;
        GearInventory.Collect(state, _scratch);
        for (var i = 0; i < _scratch.Count; i++)
        {
            if (state.Objects.TryGet(_scratch[i], out var o) && o.Weenie.WeenieClassId == FighterKit.HealingKitWcid && (o.Weenie.Structure ?? 1) > 0)
            {
                guid = _scratch[i];
                uses = o.Weenie.Structure ?? 1;
                return true;
            }
        }

        guid = 0;
        uses = 0;
        return false;
    }

    /// <summary>The first held Potion of Healing stack and how many potions are held in all (ACE never merges stacks at pick-up, so a looted one is its own).</summary>
    private bool FindPotions(IGameSessionView session, out uint guid, out int count)
    {
        var state = session.State;
        GearInventory.Collect(state, _scratch);
        guid = 0;
        count = 0;
        for (var i = 0; i < _scratch.Count; i++)
        {
            if (state.Objects.TryGet(_scratch[i], out var o) && o.Weenie.WeenieClassId == FighterKit.PotionWcid)
            {
                if (guid == 0)
                {
                    guid = _scratch[i];
                }

                count += o.Weenie.StackSize ?? 1;
            }
        }

        return count > 0;
    }

    private static bool InCombatMode(IGameSessionView session) => session.State.Self.Qualities.CombatMode is not (0 or (int)CombatMode.NonCombat);

    /// <summary>
    /// Reads the Healing skill every <see cref="SkillRefreshSeconds"/> (the read may allocate with enchantments on): an untrained skill sets the kit aside (ACE 0x04FC), an unreadable one falls back to the capture's number loudly, once
    /// (<c>TODO(m4-heal-skill-read)</c>).
    /// </summary>
    private void RefreshHealingSkill(IGameSessionView session)
    {
        if (!double.IsNaN(_skillAt) && _clock - _skillAt < SkillRefreshSeconds)
        {
            return;
        }

        _skillAt = _clock;
        switch (RecoveryRules.ReadHealingSkill(session.State.Self.Qualities, out var value, out var specialised))
        {
            case HealingSkillState.Read:
                _healing = value;
                _specialised = specialised;
                _healingRead = true;
                break;
            case HealingSkillState.Untrained:
                if (!_kitOff)
                {
                    _kitOff = true;
                    _log.Warn(LogCategory, "heal: Healing is below trained, so a kit use would end UseDone 0x04FC (capture C4): the kit is not used, potions only");
                }

                break;
            default:
                _healing = RecoveryRules.FallbackHealingSkill;
                _specialised = false;
                _healingRead = false;
                if (!_skillWarned)
                {
                    _skillWarned = true;
                    _log.Warn(LogCategory, $"heal: the Healing skill ({RecoveryRules.HealingSkillId}) cannot be read from the read model (TODO(m4-heal-skill-read)); the chance is estimated with {RecoveryRules.FallbackHealingSkill}, the capture's value (effective 264 with the Handy Healing Kit)");
                }

                break;
        }
    }

    /// <summary>Starts a recovery between two kills when the health is under <c>--heal-below</c> (the caller has just entered <c>acquire</c>): the health stage then returns to <c>acquire</c>.</summary>
    private ScenarioStatus BetweenKills(IGameSessionView session)
    {
        if (_options.Recovery.Enabled && HealthShare(session) is { } share && share < _options.Recovery.Below)
        {
            _afterHeal = "acquire";
            _recoveryActive = false;
            _waitStart = double.NaN;
            _healSends = 0;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: {share * 100:0}% after kill {_tally.Kills} is under heal-below {_options.Recovery.BelowPercent}%: recovering before the next fight"));
            return Enter("health");
        }

        return ScenarioStatus.Running;
    }

    private ScenarioStatus AfterRecovery(IGameSessionView session)
    {
        if (_afterHeal is { } next)
        {
            _afterHeal = null;
            _log.Info(LogCategory, "health: " + (HealthShare(session) is { } share ? (share * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "unknown") + $", kit uses {_healTally.KitUses}, potions {_healTally.Potions}; back to the hunt");
            return Enter(next);
        }

        return NextAfterHealth(session);
    }

    private ScenarioStatus NextAfterHealth(IGameSessionView session)
    {
        _log.Info(LogCategory, "health: " + (HealthShare(session) is { } share ? (share * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "unknown") + $", kit uses {_healTally.KitUses}, potions {_healTally.Potions}");
        return Enter(_options.Weapon == HuntWeapon.Missile ? "swap" : "ground");
    }

    // ------------------------------------------------------------------------------------------------ the weapon swap (capture.md C2)

    private ScenarioStatus SwapStep(IGameSessionView session)
    {
        if (StepWait(session, out var waiting))
        {
            return waiting;
        }

        var mode = session.State.Self.Qualities.CombatMode;
        if (mode is not (0 or (int)CombatMode.NonCombat))
        {
            return Send(session, "peace for the weapon swap", session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host), "swap", fatal: true);
        }

        // M4 3.1 fix2: the lookup lets an equipped instance win over a pack copy of its class (the loot adds duplicates; the gate's first hunter test waited for ever on a second Shortbow in the pack).
        // The label names the guid, so the same step on another instance is a new step and the same step on the same instance is never sent twice.
        var state = session.State;
        if (FighterItems.Find(state, _scratch, FighterKit.MaceWcid, out var mace, out var maceWielded) && maceWielded)
        {
            _swapped = true;
            _maceAside = mace;
            return Send(session, $"the Mace to the pack (ACE refuses the bow while it is wielded) 0x{mace:X8}", session.Actions.MoveToPack(mace, source: ActionSource.Host), "swap", fatal: true);
        }

        if (!FighterItems.Find(state, _scratch, FighterKit.ShortbowWcid, out var bow, out var bowWielded))
        {
            return Fail(session, "swap: the Shortbow is not in the pack (run --scenario=fighter-build first)");
        }

        if (!bowWielded)
        {
            _swapped = true;
            return Send(session, $"wield the Shortbow 0x{bow:X8}", session.Actions.Wield(bow, EquipMask.MissileWeapon, ActionSource.Host), "swap", fatal: true);
        }

        // Live round 3: the fighter-build top-up had added arrows as a second stack, and the wield of the first one found was never seen in the read model. Every Arrow stack is merged into the largest, which is
        // then wielded by its guid and checked by that guid (ACE: Player_Inventory.cs HandleActionStackableMerge; the merge is the tracker's, waited for by the step).
        var stacks = ArrowStacks(session);
        if (stacks.Count == 0)
        {
            return Fail(session, "swap: the Arrows are not in the pack (run --scenario=fighter-build first)");
        }

        var largest = stacks[0];
        if (stacks.Count > 1)
        {
            var from = stacks[1];
            var room = largest.Max is { } max ? (long)max - largest.Size : long.MaxValue;
            var amount = (int)Math.Min(from.Size, Math.Min(room, int.MaxValue));
            if (amount <= 0)
            {
                _log.Warn(LogCategory, $"swap: the largest Arrow stack 0x{largest.Guid:X8} holds {largest.Size} (its maximum); {stacks.Count - 1} other stack(s) stay in the pack unmerged");
            }
            else
            {
                foreach (var stack in stacks)
                {
                    if (stack.Equipped)
                    {
                        return Send(session, "an Arrow stack wielded before the merge goes to the pack", session.Actions.MoveToPack(stack.Guid, source: ActionSource.Host), "swap", fatal: true);
                    }
                }

                return Send(session, $"merge the Arrows 0x{from.Guid:X8} ({from.Size}) into the largest stack 0x{largest.Guid:X8} ({largest.Size}) of {stacks.Count}", session.Actions.MergeStack(from.Guid, largest.Guid, amount, ActionSource.Host), "swap", fatal: true);
            }
        }

        _arrowStack = largest.Guid;
        if (!largest.Equipped)
        {
            return Send(session, $"wield the Arrows 0x{largest.Guid:X8} ({largest.Size})", session.Actions.Wield(largest.Guid, EquipMask.MissileAmmo, ActionSource.Host), "swap", fatal: true);
        }

        _log.Info(LogCategory, "swap: the Shortbow and the Arrows are wielded");
        return Enter("ground");
    }

    private ScenarioStatus WindRestore(IGameSessionView session)
    {
        if (StepWait(session, out var waiting))
        {
            return waiting;
        }

        // The template's state (the Mace wielded; no launcher and no ammunition wielded) whatever the run did: the END STATE is read, not _swapped (1.2's fix), and a half-done swap is repaired the same way: a launcher
        // that is still wielded is moved to the pack by ITS guid from the equipped list, and waited for, before the Mace is wielded (ACE refuses a melee weapon while a launcher is wielded; live round 4 saw that
        // refusal, WeenieError 0, after the lookup had missed the wielded Shortbow). The ammunition goes first, the swap's stack when it is wielded (M4 3.1 fix2; every step is logged).
        var state = session.State;
        var ammunition = FighterItems.WieldedAmmunition(state, _arrowStack);
        if (ammunition != 0)
        {
            return Send(session, $"the Arrows 0x{ammunition:X8} to the pack", session.Actions.MoveToPack(ammunition, source: ActionSource.Host), "wind-restore", fatal: false);
        }

        if (FighterItems.WieldedLauncher(state, _scratch) is { } launcher)
        {
            var name = launcher.Weenie.WeenieClassId == FighterKit.ShortbowWcid ? "the Shortbow" : $"the launcher '{launcher.Name}'";
            return Send(session, $"{name} to the pack 0x{launcher.Guid:X8}", session.Actions.MoveToPack(launcher.Guid, source: ActionSource.Host), "wind-restore", fatal: false);
        }

        if (!FighterItems.Find(state, _scratch, FighterKit.MaceWcid, out var mace, out var maceWielded, _maceAside))
        {
            if (_swapped)
            {
                Problem(session, "restore: the Mace is not held");
            }
            else
            {
                _log.Warn(LogCategory, "restore: the Mace is not held (the template's state is not reached; the run never swapped)");
            }

            return Enter("wind-home");
        }

        if (!maceWielded)
        {
            return Send(session, $"wield the Mace again 0x{mace:X8}", session.Actions.Wield(mace, EquipMask.MeleeWeapon, ActionSource.Host), "wind-restore", fatal: false);
        }

        Restored = true;
        _log.Info(LogCategory, "restore: the Mace is wielded, no launcher and no ammunition are wielded");
        return Enter("wind-home");
    }

    /// <summary>One Arrow stack the player holds.</summary>
    private readonly record struct ArrowStack(uint Guid, uint Size, uint? Max, bool Equipped);

    /// <summary>The Arrow stacks held (pack or wielded), the largest first (ties by guid).</summary>
    private List<ArrowStack> ArrowStacks(IGameSessionView session)
    {
        var state = session.State;
        GearInventory.Collect(state, _scratch);
        var stacks = new List<ArrowStack>();
        foreach (var guid in _scratch)
        {
            if (state.Objects.TryGet(guid, out var o) && o.Weenie.WeenieClassId == FighterKit.ArrowWcid)
            {
                var size = state.Self.Inventory.TryGetStack(guid, out var known, out _) ? known : o.Weenie.StackSize ?? 1u;
                stacks.Add(new ArrowStack(guid, size, o.Weenie.MaxStackSize, state.Self.Inventory.Find(guid).Kind == InventoryPlaceKind.Equipped));
            }
        }

        stacks.Sort((a, b) => a.Size != b.Size ? b.Size.CompareTo(a.Size) : a.Guid.CompareTo(b.Guid));
        return stacks;
    }

    /// <summary>
    /// Waits for an in-flight step's outcome and, once it is Confirmed, for the read model to show it (the caller re-reads the state): returns true while the caller must wait or the step has just ended
    /// the stage. A step that was not Confirmed ends the run (fatal) or is logged and the restore goes on.
    /// </summary>
    private bool StepWait(IGameSessionView session, out ScenarioStatus status)
    {
        status = ScenarioStatus.Running;
        if (_step is not { } step)
        {
            return false;
        }

        if (step.Handle.Outcome is not { } outcome)
        {
            return true;
        }

        _step = null;
        if (outcome.Kind != ActionOutcomeKind.Confirmed)
        {
            if (step.Fatal)
            {
                status = Fail(session, step.Label, outcome);
                return true;
            }

            Problem(session, step.Label + ": " + Describe(outcome));
            status = Enter("wind-home");
            return true;
        }

        _log.Info(LogCategory, step.Label + ": confirmed");
        _wait.Reset();
        _settleUntil = _clock + 1; // the inventory model follows the confirmation by a tick or two
        return false;
    }

    private ScenarioStatus Send(IGameSessionView session, string label, ActionHandle handle, string stage, bool fatal)
    {
        if (!_sent.Add(label))
        {
            // The same step again: the model did not show the last one yet; wait, never send twice.
            return Waiting(session, "a step the server confirmed is not in the read model yet: " + label, fatal);
        }

        _wait.Reset();
        _log.Info(LogCategory, label);
        _step = new InFlight(label, handle, fatal);
        Stage = stage;
        return ScenarioStatus.Running;
    }

    private ScenarioStatus Waiting(IGameSessionView session, string reason, bool fatal)
    {
        if (!_wait.Expired(_clock, reason))
        {
            return ScenarioStatus.Running;
        }

        if (fatal)
        {
            return Fail(session, "still waiting after " + WaitSeconds.ToString("0", CultureInfo.InvariantCulture) + " s: " + reason);
        }

        Problem(session, "still waiting after " + WaitSeconds.ToString("0", CultureInfo.InvariantCulture) + " s: " + reason);
        return Enter("wind-home");
    }

    // ------------------------------------------------------------------------------------------------ the grounds

    private ScenarioStatus GroundStart(IGameSessionView session)
    {
        _huntStart = _clock;
        _cellIndex = -1;
        return NextCell(session) ?? Fail(session, "ground: no usable cell has a terrain height");
    }

    /// <summary>Sends the player to the next usable cell that has a height; null when none is left.</summary>
    private ScenarioStatus? NextCell(IGameSessionView session)
    {
        while (++_cellIndex < _cells.Count)
        {
            var cell = _cells[_cellIndex];
            if (_groundHeight(cell) is not { } height)
            {
                _log.Warn(LogCategory, $"ground: cell {cell} has no terrain height; skipped");
                continue;
            }

            var point = HuntGround.PlacePoint(cell, height);
            var name = "hunt cell 0x" + cell.CellId.ToString("X8", CultureInfo.InvariantCulture);
            var destination = new TeleportDestination(name, [], new WorldLocation(name, new CellPosition(cell.CellId, point), HasHeight: true, HeadingDegrees: 0), "hunt ground");
            _cellsTried++;
            _leftHome = true;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ground: cell {cell}, place loc at ({point.X:0.#}, {point.Y:0.#}, {point.Z:0.#}) (cell {_cellsTried} of at most {MaxCells})"));
            BeginPlace(session, destination, "acquire-settle", home: false);
            return ScenarioStatus.Running;
        }

        return null;
    }

    private ScenarioStatus Acquire(IGameSessionView session)
    {
        if (TimeOrHealthEnds(session) is { } ended)
        {
            return ended;
        }

        var missile = _options.Weapon == HuntWeapon.Missile;
        var listed = missile ? MissileRange : MeleeRange;
        if (Pick(session, listed, out var guid))
        {
            return Chosen(session, guid);
        }

        if (_cellsTried < MaxCells && _cellIndex + 1 < _cells.Count)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"acquire: no creature of the table in the band {_ground.MinLevel}-{_ground.MaxLevel} within {listed:0} m of the player; the next cell"));
            return NextCell(session) ?? Fail(session, "acquire: no further cell has a terrain height");
        }

        return Fail(session, string.Create(CultureInfo.InvariantCulture, $"acquire: no creature of the table in the band {_ground.MinLevel}-{_ground.MaxLevel} within {listed:0} m of the player after {_cellsTried} cell(s) ({_ground.Table.Count} creatures are listed; an unlisted creature is never a target)"));
    }

    /// <summary>The nearest creature of the ground's table in its level band: the level is the table's by weenie class id; a refused target is excluded apart from the level.</summary>
    private bool Pick(IGameSessionView session, float range, out uint guid)
    {
        guid = 0;
        var objects = session.State.Objects;
        int? LevelOf(uint candidate) => _excluded.Contains(candidate) ? null : objects.TryGet(candidate, out var o) ? _ground.LevelOf(o.Weenie.WeenieClassId) : null;
        return session.State.Self.Position is { } here && objects.NearestCreature(in here, range, 0, null, _ground.MinLevel, _ground.MaxLevel, LevelOf, out guid);
    }

    private ScenarioStatus Chosen(IGameSessionView session, uint guid)
    {
        _target = guid;
        _chargeRetries = 0;
        var found = session.State.Objects.TryGet(guid, out var o);
        _targetName = found ? o!.Name : string.Empty;
        _log.Info(LogCategory, $"acquire: target '{_targetName}' wcid {(found ? o!.Weenie.WeenieClassId : 0)} level {(found ? _ground.LevelOf(o!.Weenie.WeenieClassId) ?? 0 : 0)} 0x{guid:X8}");

        _empty = 0;
        _pending = session.Actions.QueryHealth(guid, ActionSource.Host);
        return Enter("query");
    }

    private ScenarioStatus Query(IGameSessionView session)
    {
        if (!Settled(out var queried) && _clock - _stageStart < WaitSeconds)
        {
            return ScenarioStatus.Running;
        }

        _pending = null;
        var known = session.State.Objects.HealthOf(_target, out var fraction);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"query: the target's health {(known ? (fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "was not reported")}"));
        return Enter("mode");
    }

    private ScenarioStatus Mode(IGameSessionView session)
    {
        var wanted = _options.Weapon == HuntWeapon.Missile ? CombatMode.Missile : CombatMode.Melee;
        if (session.State.Self.Qualities.CombatMode == (int)wanted)
        {
            return Enter("fight-send");
        }

        _log.Info(LogCategory, "mode: " + wanted);
        _pending = session.Actions.SetCombatMode(wanted, ActionSource.Host);
        return Enter("mode-wait");
    }

    private ScenarioStatus ModeWait(IGameSessionView session)
    {
        if (!Settled(out var changed))
        {
            return _clock - _stageStart > WaitSeconds ? Fail(session, "mode: the combat mode was not confirmed within " + WaitSeconds.ToString("0", CultureInfo.InvariantCulture) + " s") : ScenarioStatus.Running;
        }

        return changed.Kind == ActionOutcomeKind.Confirmed ? Enter("fight-send") : Fail(session, "setting the combat mode", changed);
    }

    // ------------------------------------------------------------------------------------------------ the fight

    private ScenarioStatus? TimeOrHealthEnds(IGameSessionView session)
    {
        if (HealthShare(session) is { } share && share < RetreatShare)
        {
            return Fail(session, string.Create(CultureInfo.InvariantCulture, $"retreat: the health fell to {share * 100:0}% (under {RetreatShare * 100:0}%) with {_tally.Kills} kill(s); cancelling the attack and returning"));
        }

        if (!double.IsNaN(_huntStart) && _clock - _huntStart > _options.Seconds)
        {
            return Fail(session, string.Create(CultureInfo.InvariantCulture, $"time: {_options.Seconds:0.#} s of hunting passed with {_tally.Kills} of {_options.Kills} kill(s)"));
        }

        return null;
    }

    private bool TargetAlive(IGameSessionView session)
    {
        var objects = session.State.Objects;
        if (!objects.TryGet(_target, out var o) || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord })
        {
            return false;
        }

        return true;
    }

    private ScenarioStatus FightSend(IGameSessionView session)
    {
        if (TimeOrHealthEnds(session) is { } ended)
        {
            return ended;
        }

        if (KillsDone())
        {
            return Succeed(session);
        }

        if (!TargetAlive(session))
        {
            _log.Info(LogCategory, $"fight: the target 0x{_target:X8} is dead or gone; the next");
            return Exclude(session);
        }

        if (session.Attack.Active)
        {
            // A sequence still runs: an Attack now would be queued and lost when its Done lands. The wait is bounded (review 1.2-R4): a lost final 01A7, or a sequence left Active, would stall the hunt silently.
            if (double.IsNaN(_activeSince))
            {
                _activeSince = _clock;
            }

            if (_clock - _activeSince < KillDoneSeconds + WaitSeconds)
            {
                return ScenarioStatus.Running;
            }

            _stalls++;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"fight: the attack sequence {session.Attack.Number} was still Active {_clock - _activeSince:0} s after it should have ended (the final 01A7 never came); sending CancelAttack and excluding 0x{_target:X8}"));
            _activeSince = double.NaN;
            _ = session.Actions.CancelAttack(ActionSource.Host); // fire and go on: the outcome is not waited for
            return Exclude(session);
        }

        _activeSince = double.NaN;

        _killsAtTarget = _tally.Kills;
        _seqNumber = session.Attack.Number;
        _awaitStart = true;
        _chargedTooFar = false;
        _noStartCode = null;
        _distAtSend = Distance(session, _target);
        _sentAt = _clock;
        _attacks++;
        _pending = _options.Weapon == HuntWeapon.Missile
            ? session.Actions.MissileAttack(_target, AttackHeight.Medium, AttackBar, ActionSource.Host)
            : session.Actions.Attack(_target, AttackHeight.Medium, AttackBar, ActionSource.Host);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"fight: {(_options.Weapon == HuntWeapon.Missile ? "MissileAttack" : "Attack")} {_attacks} at '{_targetName}' 0x{_target:X8}; the client is {_distAtSend:0.#} m from it ({(_follow is null ? "session position" : "own body")})"));
        return Enter("fight-sent");
    }

    private ScenarioStatus FightSent(IGameSessionView session)
    {
        if (!Settled(out var sent))
        {
            return ScenarioStatus.Running;
        }

        if (sent.Kind == ActionOutcomeKind.Confirmed)
        {
            return Enter("fight-run");
        }

        if (_chargedTooFar)
        {
            return EndedWithoutStart(session, 0x0036); // the approach was cancelled before the tracker saw it start: the same retry
        }

        _refused++;
        _log.Warn(LogCategory, $"fight: the attack on '{_targetName}' 0x{_target:X8} was refused ({Describe(sent)}); excluding it and picking the next");
        return Exclude(session);
    }

    private ScenarioStatus FightRun(IGameSessionView session)
    {
        if (TimeOrHealthEnds(session) is { } ended)
        {
            return ended;
        }

        if (_tally.Kills > _killsAtTarget || !TargetAlive(session))
        {
            _log.Info(LogCategory, _tally.Kills > _killsAtTarget ? $"fight: the kill notice came ({_tally.Kills} of {_options.Kills}); waiting for the sequence's Done" : $"fight: the target 0x{_target:X8} is dead or gone; waiting for the sequence's Done");
            return EnterKillWait(session);
        }

        var attack = session.Attack;
        if (attack.Number > _seqNumber)
        {
            _awaitStart = false; // a swing or a launch began the sequence
        }

        if (_awaitStart && _noStartCode is { } endedWith)
        {
            return EndedWithoutStart(session, endedWith);
        }

        if (attack.Number > _seqNumber && attack.State == AttackSequenceState.Done)
        {
            _empty = attack.Swings == 0 && attack.Shots == 0 ? _empty + 1 : 0;
            if (_empty >= 2)
            {
                _log.Warn(LogCategory, $"fight: two sequences on 0x{_target:X8} ended with no swing and no shot (WeenieError 0x{attack.LastWeenieError:X}); excluding it");
                return Exclude(session);
            }

            _log.Info(LogCategory, $"fight: the sequence ended (swings {attack.Swings}, shots {attack.Shots}, WeenieError 0x{attack.LastWeenieError:X}) and the target lives; the next Attack");
            return Enter("fight-send");
        }

        if (attack.Number == _seqNumber && _clock - _sentAt > SequenceBeginSeconds)
        {
            _refused++;
            _log.Warn(LogCategory, $"fight: the attack on 0x{_target:X8} was confirmed but no sequence began within {SequenceBeginSeconds:0} s; excluding it");
            return Exclude(session);
        }

        return ScenarioStatus.Running;
    }

    /// <summary>
    /// Whether the hunt has its kills: the count reached and at least one damage line. ACE writes no damage line for a lethal blow
    /// (<c>W/Player_Combat.cs</c>: 01AD, never 01B1), so a first swing that kills leaves <c>damage_dealt</c> 0 and the judge
    /// (<c>kills&gt;=1 damage_dealt&gt;0</c>) would fail on a good kill: the hunt goes on, within its seconds limit, until a damage line, as
    /// the M3 fight loop does (<see cref="FightScenario"/>, 3.1-R1). Found at item 1.2's live smoke (a one-shot kill, <c>damage_dealt=0</c>).
    /// </summary>
    private bool KillsDone()
    {
        if (_tally.Kills < _options.Kills)
        {
            return false;
        }

        if (_tally.DamageDealt > 0)
        {
            return true;
        }

        if (!_damageWaitLogged)
        {
            _damageWaitLogged = true;
            _log.Info(LogCategory, $"kills={_tally.Kills} of {_options.Kills} reached but damage_dealt=0 (ACE writes no damage line for the killing blow); hunting on until a damage line, within the seconds limit");
        }

        return false;
    }

    private ScenarioStatus EnterKillWait(IGameSessionView session)
    {
        _corpse = CorpsePhase.None;
        _corpseGuid = 0;
        if (_options.OpenCorpse && _tally.Kills > _killsAtTarget)
        {
            // M4 2.1 s4a: the kill's corpse is opened at once (capture C2: ACE accepts the open in the combat stance), listed, looted by the rule (M4 2.2 s0: --loot, CorpseLooter) and closed once the loot is done and the attack's last 01A7 is in.
            _corpse = CorpsePhase.Seek;
            _corpsesBefore.Clear();
            foreach (var o in session.State.Objects.All)
            {
                if ((o.DescriptionFlags & WorldObjectTable.CorpseFlag) != 0)
                {
                    _corpsesBefore.Add(o.Guid);
                }
            }

            RememberKillPosition(session);
        }

        return Enter("kill-wait");
    }

    private void RememberKillPosition(IGameSessionView session)
    {
        if (session.State.Objects.TryGet(_target, out var t) && t.Position is { } there)
        {
            _killPosition = there;
        }
    }

    private bool FindCorpse(IGameSessionView session, out uint guid)
    {
        guid = 0;
        if (_killPosition is not { } centre)
        {
            return false;
        }

        var wanted = "Corpse of " + _targetName;
        var origin = centre.ToGlobal();
        var best = (double)CorpseRangeMetres;
        foreach (var o in session.State.Objects.All)
        {
            if (o.Position is not { } p || !string.Equals(o.Name, wanted, StringComparison.Ordinal) || _corpsesBefore.Contains(o.Guid))
            {
                continue;
            }

            var d = p.ToGlobal().DistanceTo(origin);
            if (d <= best)
            {
                best = d;
                guid = o.Guid;
            }
        }

        return guid != 0;
    }

    /// <summary>True when the retreat rule (the health share under <see cref="RetreatShare"/>) or the hunt's seconds say the hunt must not wait for the corpse sequence.</summary>
    private bool GuardFires(IGameSessionView session) =>
        (HealthShare(session) is { } share && share < RetreatShare) || (!double.IsNaN(_huntStart) && _clock - _huntStart > _options.Seconds);

    /// <summary>Abandons the corpse sequence (an open view is closed fire-and-forget, a loop in flight is abandoned and its pick-up kept to drain, an open in flight is remembered), as the retreat rule and the hunt's seconds do.</summary>
    private void AbandonCorpse(IGameSessionView session)
    {
        _log.Warn(LogCategory, "corpse: the retreat rule or the hunt's time came; the corpse sequence is abandoned");
        if (_corpse is CorpsePhase.Opened or CorpsePhase.Looting)
        {
            _ = session.Actions.CloseContainer(_corpseGuid, ActionSource.Host);
        }

        if (_looter is { } running)
        {
            running.Abandon("abandoned"); // M4 2.2 s0: nothing more is sent; M4 2.2 fix R3: a pick-up in flight is kept and counted when it settles (StepLootDrain)
            FoldLoot(running);
            _looter = null;
            if (running.IsDraining)
            {
                if (_drainLooter is { } earlier)
                {
                    FinishDrain(earlier);
                }

                _drainLooter = running;
                _drainFolded = running.Looted;
            }
        }

        if (_corpse == CorpsePhase.Opening)
        {
            _abandonedOpen = _pending; // 2.1-R10: the open may still land Confirmed; PollAbandonedOpen closes the view then
        }

        _pending = null;
        _corpse = CorpsePhase.Finished;
    }

    /// <summary>
    /// M4 2.2 fix2 R15: called by the game's leg while it holds the scenario for a review frame (the hunt's <c>corpse</c> frame holds it for up to <c>HuntUiShotPlan.CorpseHoldSeconds</c>). The scenario is not stepped then, so the
    /// retreat rule and the hunt's seconds would go unchecked; this runs only those two guards and, when one fires with a corpse sequence under way, abandons the sequence exactly as <see cref="StepCorpse"/> does. Nothing else of the
    /// hunt is stepped and nothing is sent but the close of an open view.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <returns>True when the sequence was abandoned (the caller releases its hold: the window is closing); false when none is under way or no guard fired.</returns>
    public bool GuardWhileHeld(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_corpse is CorpsePhase.None or CorpsePhase.Finished)
        {
            return false;
        }

        _clock = session.Clock.Now;
        if (!GuardFires(session))
        {
            return false;
        }

        AbandonCorpse(session);
        return true;
    }

    /// <summary>Advances the corpse sequence one step; true while it still holds the kill wait (an open or a close in flight, or the corpse still to come).</summary>
    private bool StepCorpse(IGameSessionView session, bool attackOver)
    {
        if (_corpse is CorpsePhase.None or CorpsePhase.Finished)
        {
            return false;
        }

        // The retreat rule and the hunt's seconds are never delayed by the sequence: either one abandons it (an open view is closed fire-and-forget).
        if (GuardFires(session))
        {
            AbandonCorpse(session);
            return false;
        }

        switch (_corpse)
        {
            case CorpsePhase.Seek:
                RememberKillPosition(session);
                if (FindCorpse(session, out var corpse))
                {
                    _corpseGuid = corpse;
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"corpse: 0x{corpse:X8} appeared {_clock - _stageStart:0.00} s after the kill; opening it"));
                    _pending = session.Actions.OpenContainer(corpse, ActionSource.Host);
                    _corpse = CorpsePhase.Opening;
                    return true;
                }

                if (_clock - _stageStart > CorpseAppearSeconds)
                {
                    _corpsesMissed++;
                    _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"corpse: no 'Corpse of {_targetName}' within {CorpseRangeMetres:0} m of the target's last position appeared within {CorpseAppearSeconds:0} s of the kill (counted, not a failure)"));
                    _corpse = CorpsePhase.Finished;
                    return false;
                }

                return true;
            case CorpsePhase.Opening:
                if (!Settled(out var opened))
                {
                    return true;
                }

                if (opened.Kind != ActionOutcomeKind.Confirmed)
                {
                    _corpsesRefused++;
                    _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"corpse: the open of 0x{_corpseGuid:X8} was not confirmed ({Describe(opened)}); counted, not a failure"));
                    _corpse = CorpsePhase.Finished;
                    return false;
                }

                _corpsesOpened++;
                ListCorpse(session);
                _corpse = CorpsePhase.Opened;
                if (_options.Loot.Enabled)
                {
                    if (session.State.Containers.Viewed is { } viewed && viewed.Guid == _corpseGuid)
                    {
                        // M4 2.2 s0: the loot runs between the listing and the close, one pick-up at a time; the lane holds the busy chain (capture: about 2.8 s per item from a corpse).
                        _looter = new CorpseLooter(_log, LogCategory, _corpseGuid, _lootRules);
                        _log.Info(LogCategory, "corpse: looting " + _corpseGuid.ToString("X8", CultureInfo.InvariantCulture) + " with the rule: " + _lootRules.Describe() + (_lootRules.MaskNote.Length == 0 ? string.Empty : "; " + _lootRules.MaskNote));
                        _corpse = CorpsePhase.Looting;
                        LogLootMaskSource(session);
                    }
                    else
                    {
                        // M4 2.2 fix R9: --loot is on but the open view is not the kill's corpse (another container, or none): no loop starts, the sequence closes the corpse with nothing taken. Said, not silent.
                        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"corpse: --loot is on but the viewed container is {(session.State.Containers.Viewed is { } other ? "0x" + other.Guid.ToString("X8", CultureInfo.InvariantCulture) : "none")}, not the corpse 0x{_corpseGuid:X8}; the loot did not start"));
                    }
                }

                return true;
            case CorpsePhase.Opened:
            case CorpsePhase.Looting:
                if (_looter is { } looter)
                {
                    if (looter.Step(session, _clock) == LootStatus.Running)
                    {
                        return true;
                    }

                    FoldLoot(looter);
                    _looter = null;
                    _corpse = CorpsePhase.Opened;
                }

                if (!attackOver)
                {
                    return true;
                }

                _pending = session.Actions.CloseContainer(_corpseGuid, ActionSource.Host);
                _corpse = CorpsePhase.Closing;
                return true;
            default: // Closing
                if (!Settled(out var closed))
                {
                    return true;
                }

                _log.Info(LogCategory, closed.Kind == ActionOutcomeKind.Confirmed ? "corpse: closed" : "corpse: the close was not confirmed (" + Describe(closed) + "); not a failure");
                _corpse = CorpsePhase.Finished;
                return false;
        }
    }

    /// <summary>
    /// Adds a finished (or abandoned) loot loop's counts to the tally and, once per run, names the stacking gap: a looted stackable beside a held stack of its class stays a separate stack
    /// (<c>TODO(m4-loot-merge-stacks)</c>; ACE never merges at pick-up and the hunt does not call <c>MergeStack</c> in 2.2).
    /// </summary>
    private void FoldLoot(CorpseLooter looter)
    {
        _tally.AddLoot(looter.Looted, looter.Skipped);
        if (looter.StacksBesideHeld > 0 && !_mergeGapLogged)
        {
            _mergeGapLogged = true;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"TODO(m4-loot-merge-stacks): {looter.StacksBesideHeld} looted stack(s) (weenie class {looter.FirstStackBesideHeldWcid}) have a held stack of their class; they stay separate stacks, the hunt does not call MergeStack in 2.2"));
        }
    }

    /// <summary>
    /// M4 2.2 fix R3: steps an abandoned loop that still has a pick-up in flight (it sends nothing new) and adds the outcome to the tally when it settles, so <c>looted=</c> does not read one low after a retreat. The pick-up's own
    /// 25 s timeout ends it at the latest.
    /// </summary>
    private void StepLootDrain(IGameSessionView session)
    {
        if (_drainLooter is not { } drain)
        {
            return;
        }

        _ = drain.Step(session, _clock);
        if (!drain.IsDraining)
        {
            FinishDrain(drain);
        }
    }

    private void FinishDrain(CorpseLooter drain)
    {
        var late = drain.Looted - _drainFolded;
        _tally.AddLoot(late, 0);
        _drainLooter = null;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the pick-up in flight at the abandon is counted: looted={drain.Looted} ({late} after the abandon; refused={drain.Refused} timed_out={drain.TimedOut})"));
    }

    /// <summary>An open abandoned in flight (the retreat rule or the hunt's time came): when it lands Confirmed, the view is closed fire-and-forget as the Opened branch does (2.1-R10), so ACE does not keep it open to the logout.</summary>
    private void PollAbandonedOpen(IGameSessionView session)
    {
        if (_abandonedOpen is not { Outcome: { } done })
        {
            return;
        }

        _abandonedOpen = null;
        if (done.Kind == ActionOutcomeKind.Confirmed)
        {
            _log.Info(LogCategory, $"corpse: the abandoned open of 0x{_corpseGuid:X8} landed Confirmed; closing it");
            _ = session.Actions.CloseContainer(_corpseGuid, ActionSource.Host);
        }
    }

    private void ListCorpse(IGameSessionView session)
    {
        var view = session.State.Containers.Viewed;
        if (view is null || view.Guid != _corpseGuid)
        {
            _log.Warn(LogCategory, "corpse: the open was confirmed but the session shows no view of it; no contents listed");
            return;
        }

        _corpseItems += view.Entries.Count;
        var list = new System.Text.StringBuilder();
        foreach (var entry in view.Entries)
        {
            list.Append(list.Length == 0 ? string.Empty : ", ");
            if (session.State.Objects.TryGet(entry.Guid, out var item))
            {
                list.Append(CultureInfo.InvariantCulture, $"'{item.Name}' wcid {item.Weenie.WeenieClassId}");
            }
            else
            {
                list.Append(CultureInfo.InvariantCulture, $"0x{entry.Guid:X8} (not yet resolved)");
            }
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"corpse: 0x{_corpseGuid:X8} holds {view.Entries.Count} item(s): {(list.Length == 0 ? "none" : list.ToString())}"));
    }

    private ScenarioStatus KillWait(IGameSessionView session)
    {
        var attack = session.Attack;
        var done = (attack.Number > _seqNumber && attack.State == AttackSequenceState.Done) || !attack.Active;
        var attackOver = done || _clock - _stageStart >= KillDoneSeconds;
        if (StepCorpse(session, attackOver) || !attackOver)
        {
            return ScenarioStatus.Running;
        }

        _log.Info(LogCategory, done ? "fight: the sequence is over" : $"fight: no Done within {KillDoneSeconds:0} s of the kill; going on");
        if (KillsDone())
        {
            return Succeed(session);
        }

        _ = Exclude(session);
        return BetweenKills(session); // M4 3.1 s3: the recovery rule runs here, after the corpse sequence and the attack's Done and before the next acquire (never during a fight)
    }

    private ScenarioStatus Exclude(IGameSessionView session)
    {
        if (_target != 0 && !_excluded.Contains(_target))
        {
            _excluded.Add(_target);
        }

        _target = 0;
        return Enter("acquire");
    }

    // ------------------------------------------------------------------------------------------------ the wind-down

    private ScenarioStatus Succeed(IGameSessionView session)
    {
        _log.Info(LogCategory, $"hunt done: {_tally.Kills} kill(s); winding down");
        return Wind();
    }

    private ScenarioStatus Wind()
    {
        _winding = true;
        _step = null;
        _pending = null;
        return Enter("wind-cancel");
    }

    private ScenarioStatus WindCancel(IGameSessionView session)
    {
        if (_pending is null)
        {
            if (!session.Attack.Active)
            {
                return Enter("wind-peace");
            }

            _log.Info(LogCategory, "wind-down: CancelAttack");
            _pending = session.Actions.CancelAttack(ActionSource.Host);
            return ScenarioStatus.Running;
        }

        if (Settled(out var cancelled))
        {
            if (cancelled.Kind is not (ActionOutcomeKind.Confirmed or ActionOutcomeKind.Sent))
            {
                _log.Warn(LogCategory, "wind-down: the cancel was not confirmed (" + Describe(cancelled) + ")");
            }

            return Enter("wind-peace");
        }

        if (_clock - _stageStart > WaitSeconds)
        {
            _pending = null;
            _log.Warn(LogCategory, "wind-down: the cancel did not settle within " + WaitSeconds.ToString("0", CultureInfo.InvariantCulture) + " s");
            return Enter("wind-peace");
        }

        return ScenarioStatus.Running;
    }

    private ScenarioStatus WindPeace(IGameSessionView session)
    {
        if (_pending is null)
        {
            var mode = session.State.Self.Qualities.CombatMode;
            if (mode is 0 or (int)CombatMode.NonCombat)
            {
                return Enter("wind-restore");
            }

            _log.Info(LogCategory, "wind-down: NonCombat");
            _pending = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
            return ScenarioStatus.Running;
        }

        if (Settled(out var peace))
        {
            if (peace.Kind != ActionOutcomeKind.Confirmed)
            {
                Problem(session, "wind-down: the non-combat mode: " + Describe(peace));
            }

            return Enter("wind-restore");
        }

        if (_clock - _stageStart > WaitSeconds)
        {
            _pending = null;
            Problem(session, "wind-down: the non-combat mode was not confirmed within " + WaitSeconds.ToString("0", CultureInfo.InvariantCulture) + " s");
            return Enter("wind-restore");
        }

        return ScenarioStatus.Running;
    }

    private ScenarioStatus WindHome(IGameSessionView session)
    {
        if (_step is not null)
        {
            _step = null;
        }

        if (!_leftHome && InHoltburg(session))
        {
            return Home(session);
        }

        BeginPlace(session, _holtburg, "wind-home-done", home: true);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus Home(IGameSessionView session)
    {
        _returned = InHoltburg(session);
        _log.Info(LogCategory, "wind-down: " + (_returned ? "back in Holtburg" : "NOT back in Holtburg (returned=no)"));
        if (VendorTripBegin(session) is { } trip)
        {
            return trip; // M4 3.2 s4a: the sale at Renald's and the buy at Thelnoth's, then the way home again (HuntScenario.Vendor.cs)
        }

        if (!_endSession)
        {
            return Done();
        }

        _pending = session.Actions.LogOff(ActionSource.Host);
        return Enter("logging-off");
    }

    // ------------------------------------------------------------------------------------------------ teleports

    private void BeginPlace(IGameSessionView session, TeleportDestination destination, string next, bool home)
    {
        _placeDestination = destination;
        _placeNext = next;
        _placeHome = home;
        _teleportMark = _teleports;
        _placeStart = _clock;
        _pending = session.Tools.Place(destination, ActionSource.Host);
        Enter("placing");
    }

    private ScenarioStatus Placing(IGameSessionView session)
    {
        if (_pending is { Outcome: { } placed })
        {
            _pending = null;
            if (placed.Kind != ActionOutcomeKind.Confirmed)
            {
                return PlaceFailed(session, "sending the player to " + _placeDestination!.Name + ": " + Describe(placed));
            }
        }

        var landblock = _placeDestination!.Location.Position.CellId >> 16;
        if (_pending is null && _teleports > _teleportMark && session.State.Self.Travel == SelfTravel.Present && session.State.Self.Position is { } at && (at.CellId >> 16) == landblock)
        {
            _settleUntil = _clock + SettleSeconds;
            if (_placeHome)
            {
                return Home(session);
            }

            return Enter(_placeNext);
        }

        return _clock - _placeStart > PlaceSeconds
            ? PlaceFailed(session, string.Create(CultureInfo.InvariantCulture, $"the player was not present at {_placeDestination.Name} within {PlaceSeconds:0.#} s (teleports {_teleports - _teleportMark}, travel {session.State.Self.Travel})"))
            : ScenarioStatus.Running;
    }

    private ScenarioStatus PlaceFailed(IGameSessionView session, string why)
    {
        if (_vendorPlace)
        {
            _vendorPlace = false;
            return VendorAbort(session, "placing: " + why); // M4 3.2 s4a: a failed sale never fails the hunt
        }

        if (_placeHome && _vendorTripBegun)
        {
            _log.Warn(LogCategory, "returning from the shops: " + why + " (not a failure: the hunt was back in Holtburg before the shops)");
            return Home(session);
        }

        if (_placeHome)
        {
            Problem(session, "returning: " + why);
            return Home(session);
        }

        return Fail(session, "placing: " + why);
    }

    /// <summary>
    /// Every 01A7 is published as an <see cref="AttackChanged"/>: a sequence that ended with a code before any swing began (the number is still the send's) is an approach that ACE cancelled; the hunt reacts at once
    /// instead of waiting <see cref="SequenceBeginSeconds"/> (the live melee round 2: 028A 0x003D then 01A7 0x0036, 3 s after the send).
    /// </summary>
    private void OnAttackChanged(in AttackChanged e)
    {
        if (_awaitStart && e.Number == _seqNumber && e.State == AttackSequenceState.Done && e.LastWeenieError != 0)
        {
            _noStartCode = e.LastWeenieError;
        }
    }

    private void OnChat(in ChatReceived e)
    {
        if (_awaitStart && e.Line.Kind == ChatKind.WeenieError && e.Line.ChatType == (uint)WeenieError.YouChargedTooFar)
        {
            _chargedTooFar = true; // the 028A that precedes the 01A7
        }
    }

    private static string ErrorName(uint code) => Enum.IsDefined(typeof(WeenieError), code) ? $"{(WeenieError)code} (0x{code:X4})" : $"0x{code:X4}";

    /// <summary>
    /// How far the client's own body is from the target, in metres: the follower's position when there is one (ACE's approach moves that body, and the session's own position does not move
    /// from the client's side: live round 3), else the session's player position.
    /// </summary>
    private double Distance(IGameSessionView session, uint target)
    {
        if (!session.State.Objects.TryGet(target, out var o) || o.Position is not { } there)
        {
            return double.NaN;
        }

        if (_follow?.Position is { } own)
        {
            return own.ToGlobal().DistanceTo(there.ToGlobal());
        }

        if (_follow is not null && !_followWarned)
        {
            _followWarned = true;
            _log.Warn(LogCategory, "TODO(m4-hunt-follow-unplaced): the hunt's own body is not placed yet; the distance is the session's position");
        }

        return session.State.Self.Position is { } here ? here.ToGlobal().DistanceTo(there.ToGlobal()) : double.NaN;
    }

    /// <summary>The attack ended with a code before it began: ACE's approach cancelled for <c>YouChargedTooFar</c> is retried from where the player now stands (the walk in 15 m legs); anything else excludes the target.</summary>
    private ScenarioStatus EndedWithoutStart(IGameSessionView session, uint code)
    {
        _awaitStart = false;
        _noStartCode = null;
        var distance = Distance(session, _target);
        if (_chargedTooFar)
        {
            if (_chargeRetries < MaxChargeRetries && distance <= _distAtSend - ChargeGainMetres)
            {
                _chargeRetries++;
                _chargeRetriesTotal++;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"fight: the approach to '{_targetName}' 0x{_target:X8} was cancelled with {ErrorName((uint)WeenieError.YouChargedTooFar)} (15 m from where it began), AttackDone {ErrorName(code)}; {distance:0.#} m left (was {_distAtSend:0.#} m): retry {_chargeRetries} of {MaxChargeRetries} from here"));
                return Enter("fight-send");
            }

            _refused++;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"fight: the approach to '{_targetName}' 0x{_target:X8} was cancelled with {ErrorName((uint)WeenieError.YouChargedTooFar)} and did not close in ({distance:0.#} m left, was {_distAtSend:0.#} m, {_chargeRetries} retries); excluding it"));
            return Exclude(session);
        }

        _refused++;
        _log.Warn(LogCategory, $"fight: the attack on '{_targetName}' 0x{_target:X8} ended with AttackDone {ErrorName(code)} before any swing; excluding it");
        return Exclude(session);
    }

    private void OnMoved(in ObjectMoved e)
    {
        if (e.IsSelf && e.Verdict == PositionVerdict.SelfTeleport)
        {
            _teleports++;
        }
    }

    private bool InHoltburg(IGameSessionView session) =>
        session.State.Self.Position is { } at && (at.CellId >> 16) == _holtburgLandblock;

    // ------------------------------------------------------------------------------------------------ plumbing

    private bool Settled(out ActionOutcome outcome)
    {
        if (_pending is { Outcome: { } done })
        {
            _pending = null;
            outcome = done;
            return true;
        }

        outcome = default;
        return false;
    }

    private ScenarioStatus Enter(string stage)
    {
        Stage = stage;
        _stageStart = _clock;
        return ScenarioStatus.Running;
    }

    private ScenarioStatus Done()
    {
        Stage = "done";
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        FinishAppraise();
        _log.Info(LogCategory, "verdict " + VerdictFields());
        _log.Info(LogCategory, "detail " + DetailFields());
        _tally.Dispose();
        _follow?.Dispose();
        return Failure is null ? ScenarioStatus.Succeeded : ScenarioStatus.Failed;
    }

    private static string Describe(in ActionOutcome outcome) =>
        outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail);

    /// <summary>Notes a problem during the wind-down: the run is marked failed, and goes on.</summary>
    private void Problem(IGameSessionView session, string why)
    {
        Failure ??= why;
        _log.Error(LogCategory, why);
    }

    private ScenarioStatus Fail(IGameSessionView session, string step, in ActionOutcome outcome) => Fail(session, step + ": " + Describe(outcome));

    private ScenarioStatus Fail(IGameSessionView session, string why)
    {
        Failure ??= why;
        _log.Error(LogCategory, why);
        if (_winding)
        {
            return ScenarioStatus.Running;
        }

        if (!_acting)
        {
            // Not in the world as the fighter (login, choosing, a wrong character): nothing was sent for the character, so there is nothing to wind down.
            _returned = InHoltburg(session);
            Stage = "failed";
            _log.Info(LogCategory, "verdict " + VerdictFields());
            _tally.Dispose();
            _follow?.Dispose();
            return ScenarioStatus.Failed;
        }

        return Wind();
    }

    private sealed record InFlight(string Label, ActionHandle Handle, bool Fatal);
}
