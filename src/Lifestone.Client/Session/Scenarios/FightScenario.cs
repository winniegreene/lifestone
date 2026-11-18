using System.Collections.Immutable;
using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Tools;
using Lifestone.Client.World;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Data.World;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>Which stages of the fight scenario run before the fight itself (the stages that need the server's tools or the character's own kit can be left to the caller).</summary>
[Flags]
public enum FightStages
{
    /// <summary>Only the fight: the player is in the world, in the den, in the magic combat mode (the tests', and a host that has done the rest).</summary>
    None = 0,

    /// <summary>The gear scenario (login, the wand and the armour).</summary>
    Gear = 1,

    /// <summary>The buff top-up (the buff cycle when a profile line is missing or has under <see cref="FightScenario.TopUpSeconds"/> left).</summary>
    Buffs = 2,

    /// <summary>The teleport to the ground's arrival (<see cref="HuntingGround.Arrival"/>) and the settle.</summary>
    Place = 4,

    /// <summary>The change to the magic combat mode.</summary>
    Mode = 8,

    /// <summary>Everything (the headless host's).</summary>
    All = Gear | Buffs | Place | Mode,
}

/// <summary>M6 6 F3 (<c>m6-resume-loot-queue</c>): a kill's corpse, by guid and the creature's name (the corpse is "Corpse of &lt;name&gt;").</summary>
/// <param name="Guid">The corpse's guid.</param>
/// <param name="Name">The creature's name.</param>
public readonly record struct KillCorpse(uint Guid, string Name);

/// <summary>
/// M3 3.1's fight scenario (<c>--scenario=fight</c>, plan section 4.7 lane fa; BSD Mage and nobody else): the character has the gear on, is topped up with the profile's buffs, is sent to the ground's arrival (the Black Spawn Den's by default)
/// (<see cref="FightOptions.Ground"/>), enters the magic combat mode and casts Flame Bolt at the nearest tusker on the arrival's floor until <see cref="FightOptions.Kills"/> kills (the server's 0x01AD notices, counted by
/// <see cref="FightTally"/>) or <see cref="FightOptions.Seconds"/> pass, walking along the floor when nothing is in range or the casts miss, and returns to non-combat.
/// </summary>
/// <remarks>
/// <para><b>Stages</b> (<see cref="Stage"/>, each logged once on entry as <c>[fight] stage &lt;name&gt;</c>): <c>start</c>, <c>gear</c> (login, entering and the gear are <see cref="GearScenario"/>'s), <c>buffs</c> (the top-up, or the
/// logged skip), <c>place</c> and <c>teleporting</c> and <c>settle</c> (<c>PlayerTools.Place</c> to the den's Places row through <see cref="TeleportCatalog"/>; settled as <see cref="RoutePlay"/> does: the player is present, then
/// <see cref="SettleSeconds"/>), <c>mode-magic</c>, <c>fight</c>, <c>mode-peace</c>, <c>leave-den</c> (the way out of the den before the end, <see cref="GroundExit"/>; not in the arena-here run, where the host owns where the player
/// stands), <c>logging-off</c>, <c>quitting</c>, <c>done</c>. With <c>endSession: false</c> (the game's leg, the tests) it ends at <c>done</c> in the world.</para>
/// <para><b>The buff top-up</b> runs <see cref="BuffCycle"/> itself (the scenario's engine) with a re-buff margin of <see cref="TopUpSeconds"/>, not <see cref="BuffScenario"/>: that would run the gear a second time and cast
/// its forced re-buff pass for nothing. It is skipped, and logged, when every one of the profile's 24 lines has at least <see cref="TopUpSeconds"/> left.</para>
/// <para><b>The fight</b> (D10, D12, D21): the Flame Bolt level is the highest of levels I-VII the spellbook holds whose ACE cast chance from the buffed War Magic skill (<see cref="GearCasts.TryCurrentSkill"/>,
/// <see cref="GearCasts.Chance"/>, the buff cycle's code) reaches <see cref="BuffOptions.CastChance"/>; a fizzle drops one level once. The target is
/// <c>WorldObjectTable.NearestCreature</c> over the ground's target creatures (<see cref="GroundRules.TargetWcids"/>) on its walkable cells (<see cref="HuntingGround.IsWalkable"/>) within the cast's range: ACE's
/// <c>min(BaseRangeConstant + (Init + Ranks) * BaseRangeMod, MaxRadarRange_Outdoors)</c> (<c>Player_Magic.cs</c> 495-505, which reads the skill's start level plus its ranks, not the buffed value; beyond it ACE answers UseDone
/// <c>MissileOutOfRange</c> 0x0550). Each cast is a miss unless it is Confirmed (the projectile exists) <i>and</i> a damage line or the kill follows within the hit wait (<see cref="HitWaitFor(double, float)"/>: the projectile's
/// flight to the target at its CreateObject velocity plus <see cref="HitWaitMarginSeconds"/>, at most <see cref="HitWaitMaxSeconds"/>; a resist line ends it at once, and the emergency reads every frame of it, M6 7 AA); <see cref="MissesBeforeWalking"/> misses in
/// a row, an out-of-range answer, or no target in range send the player along the floor to the next waypoint (<see cref="IFightWalk"/>). ACE turns the caster to a targeted spell's target itself (<c>Player_Magic.cs</c> about 827,
/// <c>CreateTurnToChain2</c>), so the scenario never turns the player to cast.</para>
/// <para><b>The mana</b> (M5 1 F1; D15, D27; <see cref="ManaOrder"/>, the mage's profile): the bolt is sent from its try floor (<see cref="ManaOrder.FloorOf"/>) with the Mana Conversion tries (<see cref="CastRules.WithManaTries"/>; a
/// 0x0401 inside them is the mana's, not a miss); under the floor, or once the tries are spent (the ceiling then), the mana phase follows <see cref="ManaOrder.Decide"/>: the dance (<see cref="CastRules.ManaDance"/>, the
/// level <see cref="ManaOrder.DanceLevelOf"/> chose, every press counted), a Mana Potion only when the stamina is too low for a transfer (the vital's update waited for, as the vitals keeper does), a rest in peace for stamina
/// (non-combat until the decision's stamina target or <see cref="RestCapSeconds"/>; a health fall ends it early, named; then the magic mode again and the dance), or the wait for regeneration. Before a bolt, when
/// <see cref="ManaOrder.PreemptTransfer"/> holds, the dance comes first ("transfer before stamina reaches its maximum", D27). <c>--cast-mana-tries 0</c> is today's ceiling wait exactly (the bolt waits for its
/// ceiling). A mana wait runs from the first bolt not sent for mana to the next bolt sent (<c>mana_wait_s=</c>, <c>mana_wait_max_s=</c>).</para>
/// <para><b>The level</b> (D10, D26): the highest level the book holds at the cast chance, capped by <see cref="FightOptions.MaxLevel"/>; chosen again (and logged once per change) when the enchantments, the
/// character's level, the War Magic skill's ranks or Focus or Self change (an allocation-free check at each pick: the registry's array identity and the numbers).</para>
/// <para><b>The limits</b>: <see cref="FightOptions.Kills"/> (0: none), <see cref="FightOptions.Minutes"/> (a CLEAN end with the kills made) and <see cref="FightOptions.FailSeconds"/> (a failure).</para>
/// <para><b>Failure</b> is loud: in the world the run leaves the den first (<see cref="GroundExit"/>, whatever <c>endSession</c> is: the game's leg logs off as soon as the scenario returns Failed, and a mage logged off in the den meets
/// the tuskers at its next login), then logs off (<c>endSession: true</c>, as the buff scenario) or ends Failed where it stands. The causes: the player's death, the seconds limit (with the counts), a walk that cannot be planned or
/// fails, an unreadable skill or spellbook.</para>
/// <para>Per-step code allocates nothing but log lines on events: the cell filter is one cached delegate, the death and projectile handlers are subscribed once.</para>
/// </remarks>
public sealed partial class FightScenario : IScenario, IDisposable, IStoppableFight
{
    /// <summary>The scenario's token (<c>--scenario=fight</c>).</summary>
    public const string ScenarioName = "fight";

    /// <summary>The log category of the scenario's lines.</summary>
    public const string LogCategory = "fight";

    /// <summary>A profile line with less than this many seconds left is topped up before the fight (the plan's ten minutes).</summary>
    public const double TopUpSeconds = 600;

    /// <summary>How long the player is present in the den before the fight starts, in seconds (RoutePlay's settle).</summary>
    public const double SettleSeconds = 3;

    /// <summary>The limit on the teleport, in seconds (portal space takes 5 s or more).</summary>
    public const double TeleportSeconds = RoutePlay.TeleportSeconds;

    /// <summary>
    /// The hit wait when the object table gives no flight time (no projectile or no position in it). Until M6 7 AA it was the floor of every wait: a target beside the player then sat 3 s after a bolt of a tenth of a second, the fight blind (r3c: a resisted bolt at 2.6 m while two tuskers took 22 health to 5).
    /// </summary>
    public const double HitWaitSeconds = 3.0;

    /// <summary>
    /// Added to the projectile's flight time in the hit wait (<see cref="HitWaitFor(double, float)"/>). M6 7 AA, measured: over the climb's logs and four den soak rounds, 1,123 confirmed bolts with a real speed, the damage, resist or kill line came at most 0.07 s after distance over speed (the median 0.06 s before it: the tusker runs at the bolt). So the wait protects two things and this margin keeps both: a line still on its way is no miss (0.1 s over the worst measured), and a bolt
    /// stopped at the end of its flight still gets its <see cref="WallGraceSeconds"/> before the wall verdict (the wait must outlast the grace).
    /// </summary>
    public const double HitWaitMarginSeconds = WallGraceSeconds + 0.1;

    /// <summary>
    /// The cap of the hit wait. A Flame Bolt flies at the weenie's MaximumVelocity (<c>WorldObject_Magic.cs</c> 1898-1920), about 15 m/s live (session.md section 31), so the farthest target (<see cref="MaxRange"/>) is hit after
    /// 5 s; twice that bounds a velocity the table read low, so one bad read cannot hold the loop for the run (a wait past the cap would be judged at the cap and logged as such).
    /// </summary>
    public const double HitWaitMaxSeconds = 10.0;

    /// <summary>A projectile velocity under this, in m/s, is not a speed to divide by (a CreateObject with a zero or placeholder velocity): the hit wait is then <see cref="HitWaitMaxSeconds"/>, and the log says so.</summary>
    public const float MinProjectileSpeed = 1f;

    /// <summary>The seconds after a target's death (its Dead motion or its deletion) the kill notice may take before the target is left.</summary>
    public const double KillNoticeSeconds = 2.0;

    /// <summary>The most nearest-tusker candidates the clear-line rule tries before it walks on.</summary>
    public const int MaxLinePicks = 6;

    /// <summary>M6 7 Y: the seconds between two logged <c>blocked:</c> lines for one creature (Lifestone's own; the picks are still counted).</summary>
    public const double BlockedLogSeconds = 5;

    /// <summary>M6 7 Y: the seconds between two looks for a target during a tour's leg (Lifestone's own: a few sweeps a second, about a metre of a low mage's run).</summary>
    public const double WalkSightSeconds = 0.25;

    private const int BlockedLogCap = 256;
    private readonly Dictionary<uint, (double At, int HeldBack)> _blockedLogged = [];
    private bool _walkSight;
    private bool _pickLeaving;
    private double _walkSightAt;

    /// <summary>The seconds a bolt may sit stopped before the damage line must have come (ACE sends the Explode and the zero velocity before the damage line), else the stop is a wall hit.</summary>
    public const double WallGraceSeconds = 0.6;

    /// <summary>
    /// How far short of the target's current position the bolt's stop must be, in metres, to count as a wall hit (4.2 fix A, R2): the target's radius (<c>TODO(fight-target-radius)</c>: the scenario has no Setup of the target, a tusker's is
    /// about 1 m) plus a margin for the cast's confirmation latency (about 0.15 s at 20 m/s). A stop nearer the target than this is a resisted spell or an evade, whatever the target did while the bolt flew.
    /// </summary>
    public const double WallStopShortMetres = 4.0;

    /// <summary>The misses in a row that send the player to the next waypoint.</summary>
    public const int MissesBeforeWalking = 3;

    /// <summary>ACE's <c>Player.MaxRadarRange_Outdoors</c> (<c>Player.cs</c> 74), the cap of a spell's range.</summary>
    public const float MaxRange = 75f;

    /// <summary>The WeenieError ACE sends when the target is beyond the spell's range (<c>MissileOutOfRange</c>, 0x0550).</summary>
    public const uint MissileOutOfRange = 0x0550;

    /// <summary>The code of a fizzle (GameEvent 0x028A 0x0402).</summary>
    public const uint FizzleCode = 0x0402;

    /// <summary>The Flame Bolt levels in play (I-VII; the profile's eighth spell is Incantation, D10).</summary>
    public const int Levels = 7;

    /// <summary>
    /// The most seconds a rest in peace for stamina lasts (M5 1 F1, <see cref="ManaStep.RestStamina"/>). Lifestone's own bound: item 0 measured 1.30 stamina a second at rest, so the mage's 275 come back from 0 in about 210 s;
    /// past the cap the rest ends, named, and the order decides again.
    /// </summary>
    public const double RestCapSeconds = 300;

    /// <summary>
    /// The seconds the dance is set aside after one that spent every press without landing (M5 1 F1; the server's costs are not the model's, named). Lifestone's own: long enough for the order's potion, rest or wait to
    /// run instead, short enough that the next need dances again.
    /// </summary>
    public const double DanceAsideSeconds = 60;

    /// <summary>The presses of a dance with no Mana Conversion (M6 7 AD; Lifestone's own): one, and one more after a fizzle. A press is refused only for a mana short of the base, so ACE's roll has nothing to give a third.</summary>
    public const int PressesUntrained = 2;

    /// <summary>
    /// The seconds the rest in peace for stamina is set aside after a health fall ended one (M5 1 fix A, 1-R3): something is hitting the mage, and a rest in peace beside it would end on the next blow, flip the combat mode
    /// each time and never bolt. Lifestone's own: long enough for the mage to bolt or the blows to stop, short enough to rest again when nothing hits. The order waits in the magic mode instead (a bolt or a dance as the mana allows).
    /// </summary>
    public const double RestAsideSeconds = 30;

    private static readonly string[] Numerals = ["I", "II", "III", "IV", "V", "VI", "VII"];

    /// <summary>The Stamina to Mana Self rows (<see cref="VitalsMeans.StaminaToManaSelf"/>), looked up once (<see cref="BuffStatMods.TransferOf"/> allocates).</summary>
    private static readonly TransferRow?[] DanceRows = [.. VitalsMeans.StaminaToManaSelf.Select(BuffStatMods.TransferOf)];

    private enum Phase
    {
        Choose,
        Mana,
        Cast,
        Hit,
        DeadWait,
        Walk,
        Drain, // M5 7 F7 (D88): a Drain Health Other or Heal Self cast of the drain rule in flight (FightScenario.Drain.cs)
        Opener, // M5 7 F8 (D89): the opener's cast in flight (FightScenario.Opener.cs)
        Refresh, // M5 7 F23: the buff refresh's pass between kills (FightScenario.Refresh.cs)
        PullRest, // M6 7 AB: the rest before the next pull (FightScenario.PullRest.cs)
        Combine, // M6 14 combine: the no-roll combines of the until's chain at a safe moment (FightScenario.Combine.cs)
    }

    /// <summary>The mana phase's own step (the new order; <c>--cast-mana-tries 0</c> keeps the ceiling wait).</summary>
    private enum ManaPhase
    {
        Decide,
        Dancing,
        Drinking,
        DrinkBusy, // M6 8: the server was too busy for the drink (0x1D): a short wait, then the drink again
        PotionVital,
        ToRest,
        Resting,
        ToMagic,
        Waiting,
        Revitalizing, // M5 7 F5 (D88): a Revitalize Self cast in flight, the owner's refill of the stamina
        StaminaDrain, // M5 7 F10: a Drain Stamina Other cast in flight, the stamina floor's other refill (the owner's addition to D88)
    }

    private readonly GearScenario? _gear;
    private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
    private readonly BuffOptions _buffOptions;
    private readonly FightOptions _options;
    private readonly IFightWalk? _walk;
    private readonly bool _endSession;
    private readonly FightStages _stages;
    private readonly ILogSink _log;
    private readonly FightTally _tally = new();

    /// <summary>M6 1 F2: the session the scroll keep hook reads (the book and the qualities at each loot decision); set at each step, null before the first.</summary>
    private IGameSessionView? _hookSession;

    /// <summary>M6 1 F2: <see cref="ScrollKeepRule.Hook"/> over the session of the step; keeps nothing before the first step. M6 5 q4 (D57): composed with the fight's wants (<see cref="WantHook"/>).</summary>
    private Func<WorldObject, string?> KeepScrollHook(string groundKey, IReadOnlyDictionary<uint, SpellInfo> spells)
    {
        var hook = ScrollKeepRule.Hook(LadderLoader.WarMage, groundKey, spells, () => _hookSession!.State.Self.Spellbook, () => _hookSession!.State.Self.Qualities, botCovers: () => BuffBotCover.For(_hookSession!.State).Covering); // M6 item 14 S3: no scroll of a line the buff bot casts is kept
        return WantHook(_options.Want, _wantRefused, item => _hookSession is null ? null : hook(item));
    }

    /// <summary>
    /// M6 5 q4 (D57): the wanted wcids whose pick-up the server refused: the plan kept a quest-want item of the wcid, it is not held now and the chat lines since the pick-ups began carry the server's refusal (<see cref="QuestSolvedTooText"/>).
    /// A want that arrived (held) is never named; no refusal line names none.
    /// </summary>
    /// <param name="said">The chat texts since the pick-ups began.</param>
    /// <param name="plan">The corpse's plan.</param>
    /// <param name="held">True when the guid is in the pack now.</param>
    internal static IReadOnlyList<uint> RefusedWants(IReadOnlyList<string?> said, MageLootPlan plan, Func<uint, bool> held)
    {
        if (!said.Any(t => t?.StartsWith(QuestSolvedTooText, StringComparison.Ordinal) == true))
        {
            return [];
        }

        return [.. plan.Kept.Select(g => plan.Items.First(i => i.Guid == g)).Where(i => MageLootRules.IsQuestWant(i) && i.Wcid != 0 && !held(i.Guid)).Select(i => i.Wcid).Distinct()];
    }

    /// <summary>The wcids whose pick-up the server refused in this fight (the quest's 20 h gate): no longer wanted, so the next corpse's plan leaves them (M6 5 q4).</summary>
    private readonly HashSet<uint> _wantRefused = [];

    /// <summary>M6 5 q4 (D57): the chat total at the fight's start (the <see cref="FightOptions.Until"/> chat form reads the lines since).</summary>
    private long _untilMark;

    /// <summary>M6 5 q4: true once the <see cref="FightOptions.Until"/> ended the fight.</summary>
    private bool _untilMet;

    /// <summary>M6 5 F1 (review 5-R4): the server's refusal line when the item the fight's <see cref="FightOptions.Until"/> waits for was refused at the pick-up (it can never be met), or null.</summary>
    private string? _untilRefused;

    /// <summary>True once the malformed-argument stop was raised (<see cref="FightOptions.SpecError"/>).</summary>
    private bool _specErrorRaised;

    /// <summary>
    /// M6 5 q4 (D57): the loot's keep hook over the wanted wcids: an item of one that was not refused is kept aside with the verdict <c>quest: want &lt;wcid&gt;</c> (<see cref="MageLootRules.QuestWantPrefix"/>), under every value floor; any other item goes to
    /// <paramref name="next"/> (the scroll rule).
    /// </summary>
    /// <param name="want">The wanted wcids.</param>
    /// <param name="refused">The wcids the server refused in this fight.</param>
    /// <param name="next">The hook for every other item.</param>
    internal static Func<WorldObject, string?> WantHook(IReadOnlyList<uint> want, IReadOnlySet<uint> refused, Func<WorldObject, string?> next) =>
        want.Count == 0 ? next : item => want.Contains(item.Weenie.WeenieClassId) && !refused.Contains(item.Weenie.WeenieClassId)
            ? MageLootRules.QuestWantPrefix + item.Weenie.WeenieClassId.ToString(CultureInfo.InvariantCulture)
            : next(item);

    /// <summary>
    /// The start of the server's refusal of a pick-up inside a quest's timer: "You have solved this quest too recently!" (or "... too many times!" at the quest's max solves); ACE sends it as a system chat line with the error
    /// (<c>QuestManager.HandleSolveError</c> @95ef426d Managers/QuestManager.cs:558-578, called from Player_Inventory.cs:1257-1259).
    /// </summary>
    internal const string QuestSolvedTooText = "You have solved this quest too ";
    private readonly Func<uint, bool>? _cellFilter;
    private readonly Func<uint, bool>? _huntFilter;
    private readonly Func<uint, bool> _nextStoreyFilter;
    private readonly Func<uint, bool> _otherStoreyFilter;
    private int _pickNextStorey = -1;
    private int _pickHereStorey = -1;
    private readonly List<uint> _killed = [];
    private readonly uint[] _levelIds = new uint[Levels];
    private readonly HuntingGround _ground;
    private bool _wantsChecked;
    private readonly KillFirstPick _killFirstPick; // M6 3 a2: the pick takes a kill-first creature of the ground first
    private readonly GearKit _kit;
    private readonly IReadOnlyList<uint> _targetWcids;
    private readonly TeleportDestination _destination;
    private BuffCycle? _cycle;
    private ActionHandle? _pending;
    private ActionHandle? _health;
    private readonly SeamLeg _seam;
    private readonly LootStep? _loot; // M5 item 3 m2 (D38): null with --fight-loot off, so nothing of the loot step exists
    private string? _seenName; // the target's name and position as last seen (the loot step's, kept only with looting on)
    private CellFrame? _seenAt;
    private IDisposable? _diedSubscription;
    private IDisposable? _createdSubscription;
    private IDisposable? _movedSubscription;
    private readonly GroundExit _exit;
    private bool _failLeave;
    private bool _failLogOff;
    private bool _subscribed;
    private bool _disposed;
    private string _gearFields = "gear=n/a worn=0";
    private string _buffsNote = "n/a";
    private Phase _phase;
    private int _level = -1;
    private bool _fizzleDropped;
    private CastAttempt? _cast;
    private Spellbook? _usableBook;
    private readonly Func<uint, bool> _usable;
    private int _fizzlesSpent;
    private int _castNoted;
    private float _range;
    private uint _initPlusRanks;
    private uint _manaNeeded;
    private bool _manaLogged;
    private uint _target;
    private bool _damageWaitLogged;
    private int _missesInARow;
    private uint _boltGuid;
    private double _boltSpeed = double.NaN;
    private CellFrame _boltOrigin;
    private int _resistMark;
    private double _boltStopAt = double.NaN;
    private readonly List<uint> _pickExcluded = [];
    private int _walks;
    private bool _holdingLogged;
    private readonly bool[] _storeysSeen; // M5 item 2 g6, D44: the storeys the body stood in during the fight
    private int _storeysVisited;
    private int _bodyStorey = -1;
    private int _basementEntries;
    private bool _inNoGo;
    private int _planCapLogged = -1;
    private int _fedKills;
    private uint _killPendingGuid; // M5 7 F15 (7-F1): the target a name-matching kill notice named, until the object table says dead or gone
    private double _killPendingAt = -1;
    private uint _fedTarget;
    private uint _seenTarget; // g6: the target's wcid and cell as last seen (ACE deletes the body with the kill notice)
    private uint _seenWcid;
    private uint _seenCell;
    private int _selfTeleports;
    private int _teleportMark;
    private int _damageMark;
    private int _killMark;
    private int _targetKillMark;
    private int _projectiles;
    private double _stageStart;
    private double _fightStart;
    private double _awaitSince;
    private double _hitWait = HitWaitSeconds;
    private double _settleUntil = double.NaN;
    private double _deadSince;
    private bool _died;
    private FightStopReason? _stopRequest;
    private string? _lootHold; // M6 14 recall unblock (m6-loot-hold): why no corpse is opened any more, or null
    private string? _deathText;
    private double _fightEnd = double.NaN;

    // M5 1 F1: the mana order, the level's re-choice, the waits and the rates.
    private readonly CastRules _boltRules;
    private readonly CastRules? _danceRules;
    private readonly CastRules? _pressRules; // M6 7 AD: the dance of a caster with no Mana Conversion: a fizzle pressed once more, no more
    private readonly CastTally _castTally = new();
    private readonly DanceLevel[] _danceLevels = new DanceLevel[7];
    private readonly int[] _danceIndex = new int[7];
    private readonly List<uint> _scratch = [];
    private CastAttempt? _manaCast;
    private CastChatProof? _danceProof;
    private readonly FightRefill _refill; // M5 7 F5 (D88): the stamina refill (Revitalize Self, else a Stamina Potion) and the counts of stamina_min= and revitalizes=
    private bool _drinkStamina; // M5 7 F5: the potion in hand is a Stamina Potion (the refill's), not a Mana Potion
    private bool _staminaPotionsAside;
    private ActionHandle? _manaPending;
    private int _manaDrinkBusy; // M6 8: the busy refusals of the drink in hand
    private double _manaDrinkAt;
    private ManaPhase _manaPhase;
    private ManaDecision _decision;
    private int _danceCount;
    private uint _manaConversion;
    private uint _manaFloor;
    private uint _manaReserve; // M5 7 F5, D88: the Stamina to Mana press floor plus the Revitalize Self press floor, added to the bolt's try floor (ManaOrder.ManaFloorOf)
    private uint _staminaFloor; // D88: the least stamina at which a landed Stamina to Mana pays itself and the Revitalize after it (ManaOrder.StaminaFloorOf)
    private bool _triesSpent;
    private bool _preempting;
    private bool _potionsAside;
    private double _danceAsideUntil = double.NegativeInfinity;
    private double _restAsideUntil = double.NegativeInfinity;
    private bool _restAsideOpen;
    private bool _restAsideLogged;
    private double _phaseSince;
    private uint _seenMana;
    private uint _seenStamina;
    private uint _restHealth;
    private int _dances;
    private int _dancesLanded;
    private int _manaPresses;
    private int _potionsDrunk;
    private int _rests;
    private double _manaWaitSince = double.NaN;
    private double _manaWaitTotal;
    private double _manaWaitMax;
    private double _idleSince = double.NaN;
    private double _idleTotal;
    private bool _signatureSet;
    private ImmutableArray<ActiveEnchantment> _sigEnchantments;
    private int _sigLevel;
    private uint _sigWarRanks;
    private uint _sigFocus;
    private uint _sigSelf;

    /// <summary>The fight scenario.</summary>
    /// <param name="spells">The SpellTable's spells (<c>SpellTableData.Spells</c>): the kit's components, the buff casts and the Flame Bolt levels' ranges and costs.</param>
    /// <param name="account">The account name the kit is built for (hashed by the kit, never stored, logged or printed).</param>
    /// <param name="buff">The buff cycle's options (the top-up's cast chance and the rest).</param>
    /// <param name="fight">The fight's options.</param>
    /// <param name="walk">The walk along the floor (<see cref="GroundWalk"/>), or null: a leg then fails the fight loudly.</param>
    /// <param name="log">Where the scenario logs.</param>
    /// <param name="arrivalSeconds">The limit on the arrival.</param>
    /// <param name="endSession">True: log off and quit when done. False: end at <c>done</c> in the world.</param>
    /// <param name="stages">The stages to run before the fight (<see cref="FightStages.All"/>).</param>
    public FightScenario(IReadOnlyDictionary<uint, SpellInfo> spells, string account, BuffOptions buff, FightOptions fight, IFightWalk? walk, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, bool endSession = true, FightStages stages = FightStages.All)
        : this(GearKit.BuildFor(spells ?? throw new ArgumentNullException(nameof(spells)), account, fight?.GroundProfile), spells, buff is null ? buff! : buff with { AccountName = account }, fight!, walk, log, arrivalSeconds, endSession, stages) // M5 3 m1: the fight's ground's kit (D46); M6 0 F3: the bare kit for an outdoor ground with none
    {
    }

    /// <summary>The fight scenario over a kit already built (the tests').</summary>
    /// <param name="kit">The kit.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="buff">The buff cycle's options.</param>
    /// <param name="fight">The fight's options.</param>
    /// <param name="walk">The walk along the floor, or null.</param>
    /// <param name="log">Where the scenario logs.</param>
    /// <param name="arrivalSeconds">The limit on the arrival.</param>
    /// <param name="endSession">True: log off and quit when done. False: end at <c>done</c> in the world.</param>
    /// <param name="stages">The stages to run before the fight.</param>
    public FightScenario(GearKit kit, IReadOnlyDictionary<uint, SpellInfo> spells, BuffOptions buff, FightOptions fight, IFightWalk? walk, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, bool endSession = true, FightStages stages = FightStages.All)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(buff);
        ArgumentNullException.ThrowIfNull(fight);
        ArgumentNullException.ThrowIfNull(log);
        // The arena here (the offline stand-in's tusker): the player is where it is, in the mode the check chose, so only the mode stage may run, and the floor rule is off (fd).
        stages = fight.ArenaHere ? stages & FightStages.Mode : stages;
        (_spells, _options, _log, _walk, _endSession, _stages) = (spells, fight, log, walk, endSession, stages);
        _kit = kit;
        _ground = fight.GroundProfile; // an unknown key was reported by FightOptions.FromArguments; one set by hand fails here, naming the known keys
        _storeysSeen = new bool[_ground.Storeys.Count];
        _killFirstPick = new KillFirstPick(_ground);
        if (walk?.Tour is GroundTour tour)
        {
            tour.StoreysMin = fight.StoreysMin; // --fight-storeys-min (g6)
            tour.BodyStoreysVisited = 0; // M5 7 P7v: a journey's tour is the ground's one (JourneyGroundTours), kept from an earlier fight; this fight's body has stood on no storey yet
        }
        _targets = FightTargets.Of(_ground); // M6 0 a5: a dungeon's profile targets as before; an outdoor ground's outdoor.creatures within its band
        _targetWcids = _targets.Select(t => t.Wcid).ToArray();
        _destination = _ground.ArrivalDestination(); // M5 2 g2b: the profile's arrival, not the Places row
        _cellFilter = fight.ArenaHere ? null : FightTargets.CellFilter(_ground); // a dungeon's walkable cells are the storeys' without the no-go cells (a creature in a no-go cell is never a target); M6 0 a5: an outdoor ground's, its landblocks' outdoor cells
        InitFloors(buff.AccountName); // M5 7 P7s (D148): the carried lines whose floor drops the war options that need them
        _huntFilter = _cellFilter is null ? null : c => _cellFilter(c) && !_keptOut.Contains(c); // M6 7 AR: the pick's cells; the emergency and the fight back keep _cellFilter (FightScenario.Hunt.cs)
        _nextStoreyFilter = c => _ground.IsWalkable(c) && !_keptOut.Contains(c) && _ground.StoreyOf(c) == _pickNextStorey; // 2-R2: the pick while the tour wants to leave a storey; cached, a lambda per pick allocates
        _otherStoreyFilter = c => _ground.IsWalkable(c) && !_keptOut.Contains(c) && _ground.StoreyOf(c) is var storey && storey >= 0 && storey != _pickHereStorey;
        _usable = id => (_usableBook is null || _usableBook.Knows(id)) && _spells.ContainsKey(id) && !_droppedSpells.Contains(id); // a level down may use the levels the spellbook holds and the SpellTable has (the old drop's rule); cached: a lambda per request allocates
        _buffOptions = buff with { RebuffMarginSeconds = TopUpSeconds, FieldDamageTypes = buff.FieldDamageTypes ?? ArmourBanes.FieldMask(_ground) }; // M6 14 (m6-banes): a fight is the field: its top-up and its refresh want one of the six other banes only where the ground's targets deal its damage type
        _gear = (stages & FightStages.Gear) != 0 ? new GearScenario(kit, log, arrivalSeconds, endSession: false, cast: false, spells) { Account = buff.AccountName } : null; // M6 1 w: the first cast's floor on the account's formula
        var travel = fight.Travel ?? new PlaceTravel(); // M4 P5b: one seam per scenario, shared by the place and the den exit
        _seam = new SeamLeg(travel);
        // M6 1 F2 (e's wiring, 1-R2): a scroll of the plan's next level, or any VII, is kept aside (ScrollKeepRule); the loot step's keep-aside record is the sale's skip (SaleMemory.Shared.KeptAside).
        _loot = fight.Loot ? new LootStep(log, new MageLootRules(fight.EffectiveLootThresholds with { KeepScroll = KeepScrollHook(_ground.Key, spells) }, spells), this) : null;
        if (_loot is not null && fight.EffectiveLootThresholds.KeepAside is { } keepAside)
        {
            foreach (var missing in keepAside.NamesMissingFrom(spells))
            {
                log.Warn(LogCategory, $"loot: keep-aside spell '{missing}' is not in the SpellTable: the profile's name is wrong or the table lacks it; nothing is kept aside for it (TODO(m5-keep-aside-spell-name))");
            }
        }

        _exit = new GroundExit(_ground, log, LogCategory, travel);
        // M5 1 F1: made once (the forms build a name string). --cast-mana-tries 0 keeps K3's BoltD14 exactly.
        _boltRules = fight.Mana.On ? CastRules.BoltD14.WithManaTries(fight.Mana.Tries, fight.Mana.TrySpacingSeconds) : CastRules.BoltD14;
        _danceRules = fight.Mana.On ? CastRules.ManaDance(fight.Mana.DancePresses, fight.Mana.TrySpacingSeconds) : null;
        _pressRules = fight.Mana.On ? CastRules.ManaDance(Math.Min(fight.Mana.DancePresses, PressesUntrained), fight.Mana.TrySpacingSeconds) : null;
        _drain = new FightDrain(spells, log, fight.DrainUnderPercent, fight.DrainUntilPercent);
        _tally.HitNotice = NoteHit; // M5 7 F16: each hit's amount in the log
        _tally.KillNotice = text =>
        {
            NoteKillCasts(text); // M6 0 a5: each kill's line carries the casts since the kill before
            NoteKillClaim(text); // M5 7 P7b (D139 i): every kill of the mage's is a claim on a corpse (FightScenario.LootWalks.cs)
        };
        _emergency = new FightEmergency(fight.EmergencyFloorPercent, fight.EmergencySafePercent, fight.EmergencyMarginPercent); // M5 7 F16 (D91)
        _refill = new FightRefill(spells, log, fight.Mana.On ? fight.StaminaFullPercent : 0, buff.AccountName); // --cast-mana-tries 0 is today's ceiling order exactly: no refill
        var line = ArenaProfile.LineNamed(ArenaProfile.WarLineName) ?? throw new InvalidOperationException("the profile has no " + ArenaProfile.WarLineName + " line");
        for (var i = 0; i < Levels; i++)
        {
            _levelIds[i] = line.Spells[i].Id;
        }
    }

    /// <inheritdoc/>
    public string Name => ScenarioName;

    /// <inheritdoc/>
    public string Stage { get; private set; } = "start";

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <inheritdoc/>
    public double? WhereMaxMetres => null;

    /// <inheritdoc/>
    /// <remarks>Always 0: the scenario's own walker sends movement, but its audit is <c>TODO(m3-fight-walk-audit)</c>.</remarks>
    public long Unmatched => 0;

    /// <summary>The fight's counters (casts, misses, damage dealt and taken, kills, experience).</summary>
    public FightTally Tally => _tally;

    /// <inheritdoc />
    int IStoppableFight.Kills => _tally.Kills;

    /// <inheritdoc />
    long IStoppableFight.HitMax => _tally.HitMax;

    /// <inheritdoc />
    long IStoppableFight.TargetDiedMidCast => _tally.TargetDiedMidCast;

    /// <inheritdoc />
    public FightStopReason? StopReason => _stopRequest;

    /// <summary>
    /// M5 4 a1 (D57): asks the fight to end cleanly at its next safe point, the clean <c>--fight-minutes</c> stop's (the fight phase's end checks, after the kill notices reached the tour and with no loot step in flight): the casts and the dance in
    /// flight are stopped and counted, the walk abandoned, the peace mode asked for, and the fight succeeds with the kills it made, its line naming the reason (<c>stop=</c>). Asked before the fight phase, it ends the fight at that phase's first
    /// frame. The first reason is kept; a later one is logged and ignored.
    /// </summary>
    /// <param name="reason">Why (an open set: <see cref="FightStopReason"/>).</param>
    public void RequestStop(FightStopReason reason)
    {
        if (string.IsNullOrWhiteSpace(reason.Name))
        {
            throw new ArgumentException("a stop reason needs a name", nameof(reason));
        }

        if (_stopRequest is { } kept)
        {
            _log.Info(LogCategory, $"the fight was asked to stop again ({reason.Name}); the first reason stands ({kept.Name})");
            return;
        }

        _stopRequest = reason;
        _log.Info(LogCategory, $"the fight is asked to stop ({reason.Name}): it ends at its next safe point");
        if (LeavesTheGround(reason))
        {
            HoldLoot($"the fight is asked to stop ({reason.Name}): she leaves the ground by a trip, a retreat or the stop's recall");
        }
    }

    /// <summary>
    /// M6 14 recall unblock (m6-loot-hold): true for a stop after which she leaves the ground (a trip, a retreat, the stop's recall home): under it no corpse is opened, so the way out never waits behind a corpse's Use. The other stops
    /// (the quest's end, a trial's or a fight-back's end, a scripted death) keep the clean stop's rule: the loot in hand finishes first.
    /// </summary>
    internal static bool LeavesTheGround(FightStopReason reason) =>
        reason == FightStopReason.LogisticsTrip || reason == FightStopReason.ForcedTrip || reason == FightStopReason.LogisticsStop || reason == FightStopReason.StopRule || reason == RetreatRule.StopReason;

    /// <inheritdoc />
    public string? LootHeld => _lootHold;

    /// <inheritdoc />
    public void HoldLoot(string why)
    {
        if (_loot is null || _lootHold is not null)
        {
            return;
        }

        _lootHold = why;
        _log.Info(LogCategory, $"loot: held: {why}: no corpse is opened from now on; a loot in hand stands down (its walk cut, its open view closed) and the corpses wait for the next ground visit (m6-loot-hold)");
    }

    /// <summary>The Flame Bolt level in use as a numeral (<c>I</c> to <c>VII</c>), or <c>n/a</c> before the fight chose one.</summary>
    public string Level => _level < 0 ? "n/a" : Numerals[_level];

    /// <summary>The Flame Bolt spell id in use, or 0.</summary>
    public uint SpellId => _level < 0 ? 0 : _levelIds[_level];

    /// <summary>The cast range in metres (<see cref="CastRange"/>), 0 before the fight chose a level.</summary>
    public float RangeMetres => _range;

    /// <summary>The guid of the creature the loop is aimed at (0 when it has none now: before the fight, between targets, while it walks).</summary>
    public uint Target => _target;

    /// <summary>True from the fight's stage to its end: the loop is casting, waiting for a hit or walking.</summary>
    public bool Fighting => Stage == "fight";

    /// <summary>The Missile objects the server created during the fight (the war casts' projectiles).</summary>
    public int Projectiles => _projectiles;

    /// <summary>The legs the loop walked.</summary>
    public int Walks => _walks;

    /// <summary>The seconds the fight took (to now while it runs), or NaN before it began.</summary>
    public double FightSeconds => double.IsNaN(_fightStart) || _fightStart == 0 ? double.NaN : (double.IsNaN(_fightEnd) ? _lastClock : _fightEnd) - _fightStart;

    private double _lastClock;

    /// <summary>The cast policy's counts over the fight's bolts and dances (<see cref="CastTally"/>; report-only).</summary>
    public CastTally Casts => _castTally;

    /// <summary>The dances sent (M5 1 F1, D27), the pre-emptive ones included.</summary>
    public int Dances => _dances;

    /// <summary>The dances that landed.</summary>
    public int DancesLanded => _dancesLanded;

    /// <summary>The dances' presses in all (every press counted).</summary>
    public int ManaPresses => _manaPresses;

    /// <summary>The Mana Potions the mana order drank.</summary>
    public int PotionsDrunk => _potionsDrunk;

    /// <summary>The Revitalize Self casts of the stamina refill that landed (M5 7 F5, D88; <c>revitalizes=</c>).</summary>
    public int Revitalizes => _refill.Revitalizes;

    /// <summary>The Stamina Potions the refill drank (no Revitalize Self level was payable).</summary>
    public int StaminaPotionsDrunk => _refill.PotionsDrunk;

    /// <summary>The lowest stamina the fight saw (<c>stamina_min=</c>), or null before it saw any.</summary>
    public uint? StaminaMin => _refill.StaminaMin;

    /// <summary>The rests in peace for stamina the mana order took.</summary>
    public int Rests => _rests;

    /// <summary>The guids the loop holds as killed (never picked again until their object leaves the table: <c>m5-fight-killed-growth</c>).</summary>
    public int KilledHeld => _killed.Count;

    /// <summary>
    /// The mana waits' seconds in all (<c>mana_wait_s=</c>). A wait is the whole pause for mana: it opens at the bolt's first refused try (0x0401) or at the first bolt not sent for mana, whichever comes first, and closes at the next
    /// bolt that lands or is sent with the mana for it (at or over its ceiling). The refused tries, the dance, the potion, the rest in peace for stamina and the plain wait are inside it; the pre-emptive transfer (D27) is not a wait.
    /// One still open counts to now. (M5 1 fix A, 1-R9; docs/session.md section 45.)
    /// </summary>
    public double ManaWaitSeconds => _manaWaitTotal + OpenSeconds(_manaWaitSince);

    /// <summary>The longest mana wait, in seconds (<c>mana_wait_max_s=</c>; what a wait is: <see cref="ManaWaitSeconds"/>; one still open counts to now).</summary>
    public double ManaWaitMaxSeconds => Math.Max(_manaWaitMax, OpenSeconds(_manaWaitSince));

    /// <summary>The seconds with no target to pick: the walks after "no creature of the ground is in range" or "no clear line", to the next target picked.</summary>
    public double IdleSeconds => _idleTotal + OpenSeconds(_idleSince);

    /// <summary>The kills over the whole fight time, per hour (NaN before the fight).</summary>
    public double KillsPerHour => PerHour(_tally.Kills, FightSeconds);

    /// <summary>The kills over the fight time less <see cref="IdleSeconds"/>, per hour: the rate while targets stand (NaN before the fight).</summary>
    public double KillsPerHourBurst => PerHour(_tally.Kills, FightSeconds - IdleSeconds);

    private double EndClock => double.IsNaN(_fightEnd) ? _lastClock : _fightEnd;

    private double OpenSeconds(double since) => double.IsNaN(since) ? 0 : Math.Max(0, EndClock - since);

    private static double PerHour(int kills, double seconds) => double.IsNaN(seconds) || seconds <= 0 ? double.NaN : kills * 3600.0 / seconds;

    /// <summary>
    /// The fight's mana and rate fields (M5 1 F1, report-only; m5-plan.md §5 item 3): <c>level= mana_wait_max_s= mana_wait_s= kills_per_hour= kills_per_hour_burst=</c>, the <see cref="CastTally"/> fields, then
    /// <c>revitalizes= stamina_min=</c> (M5 7 F5, D88: the Revitalize Self casts that landed and the lowest stamina seen), <c>mana_presses= dances= dances_landed=</c>. At the end of <see cref="VerdictFields"/>, the <c>bsd-fight done</c> line and the journey's den-fight station.
    /// </summary>
    /// <param name="withLevel">False: without the leading <c>level=</c> (<see cref="VerdictFields"/>, which prints it earlier).</param>
    public string ManaFields(bool withLevel = true) => string.Create(
        CultureInfo.InvariantCulture,
        $"{(withLevel ? "level=" + Level + " " : string.Empty)}mana_wait_max_s={ManaWaitMaxSeconds:0.#} mana_wait_s={ManaWaitSeconds:0.#} kills_per_hour={Rate(KillsPerHour)} kills_per_hour_burst={Rate(KillsPerHourBurst)} {_castTally.Fields} {_refill.Fields} {_drain.Fields} {ManaLoopFields()} mana_presses={_manaPresses} dances={_dances} dances_landed={_dancesLanded}");

    private static string Rate(double v) => double.IsNaN(v) ? "n/a" : v.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>The inner gear scenario (null when <see cref="FightStages.Gear"/> is off).</summary>
    public GearScenario? Gear => _gear;

    /// <summary>True (a shared session, M4 P4): a player already in the world must be BSD Mage, else the run fails at once and sends nothing (<see cref="GearScenario.RequireCharacter"/>; no effect without the gear stage).</summary>
    public bool RequireCharacter
    {
        get => _gear?.RequireCharacter ?? false;
        init
        {
            if (_gear is not null)
            {
                _gear.RequireCharacter = value;
            }
        }
    }

    /// <summary>The cycle of the buff top-up (null until the top-up needed one).</summary>
    public BuffCycle? Cycle => _cycle;

    /// <summary>
    /// The hit wait of a confirmed cast: the projectile's flight time, <paramref name="distanceMetres"/> over <paramref name="speedMetresPerSecond"/>, plus <see cref="HitWaitMarginSeconds"/>, at most
    /// <see cref="HitWaitMaxSeconds"/> (M6 7 AA: no floor); a speed under <see cref="MinProjectileSpeed"/> gives the cap (M3 3.2 review, package 2 finding 6: the fixed 3 s was shorter than a bolt's flight past about 45 m, so a hit at range was a
    /// miss credited to the next cast).
    /// </summary>
    /// <param name="distanceMetres">The distance from the projectile's origin to the target, in metres.</param>
    /// <param name="speedMetresPerSecond">The projectile's speed from its CreateObject velocity, in m/s.</param>
    public static double HitWaitFor(double distanceMetres, float speedMetresPerSecond) =>
        speedMetresPerSecond < MinProjectileSpeed || double.IsNaN(distanceMetres) ? HitWaitMaxSeconds : Math.Min((distanceMetres / speedMetresPerSecond) + HitWaitMarginSeconds, HitWaitMaxSeconds);

    /// <summary>ACE's cast range for <paramref name="spell"/> at the skill's start level plus ranks <paramref name="initPlusRanks"/> (see the class remarks).</summary>
    /// <param name="spell">The spell.</param>
    /// <param name="initPlusRanks">The skill's start level plus its ranks.</param>
    public static float CastRange(SpellInfo spell, uint initPlusRanks)
    {
        ArgumentNullException.ThrowIfNull(spell);
        return Math.Min(spell.BaseRangeConstant + (initPlusRanks * spell.BaseRangeMod), MaxRange);
    }

    /// <summary>
    /// D10's level: the highest of the first <see cref="Levels"/> spells of the war line that <paramref name="book"/> holds and whose ACE cast chance from the current War Magic skill (the buffed value) is at least
    /// <paramref name="castChance"/>; the buff cycle's chance code (<see cref="GearCasts.Chance"/>).
    /// </summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="book">The player's spellbook.</param>
    /// <param name="castChance">The threshold (<see cref="BuffOptions.CastChance"/>).</param>
    /// <param name="skill">The War Magic skill the choice was made from.</param>
    /// <param name="why">Why there is none.</param>
    /// <param name="maxLevel">The highest level allowed (<see cref="FightOptions.MaxLevel"/>, 1 to 7; M5 1 F1, D26).</param>
    /// <returns>The level's index (0 for I), or -1.</returns>
    public static int ChooseLevel(PlayerQualities q, Spellbook book, double castChance, out uint skill, out string why, int maxLevel = Levels)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(book);
        skill = 0;
        if (!GearCasts.TryCurrentSkill(q, MageTemplate.WarMagic, out skill, out why))
        {
            return -1;
        }

        return ChooseLevel(skill, book, castChance, out why, maxLevel);
    }

    /// <summary>D10's level from a skill already known (the arena-here run's <see cref="FightOptions.ArenaSkill"/>); see the other overload.</summary>
    /// <param name="skill">The War Magic skill.</param>
    /// <param name="book">The player's spellbook, or null for none: the arena-here run (the offline stand-in does not send the spell-learn update, so its book never holds the levels the tool taught: <c>TODO(m3-standin-spellbook-updates)</c>) knows them all.</param>
    /// <param name="castChance">The threshold.</param>
    /// <param name="why">Why there is none.</param>
    /// <param name="maxLevel">The highest level allowed (1 to 7).</param>
    /// <returns>The level's index (0 for I), or -1.</returns>
    public static int ChooseLevel(uint skill, Spellbook? book, double castChance, out string why, int maxLevel = Levels)
    {
        if (ArenaProfile.LineNamed(ArenaProfile.WarLineName) is not { } line)
        {
            why = "the profile has no war line";
            return -1;
        }

        if (book is { IsKnown: false })
        {
            why = "the spellbook has not arrived (no PlayerDescription)";
            return -1;
        }

        for (var i = Math.Min(Math.Clamp(maxLevel, 1, Levels), line.Spells.Count) - 1; i >= 0; i--)
        {
            if ((book is null || book.Knows(line.Spells[i].Id)) && GearCasts.Chance(skill, line.Spells[i].Power) >= castChance)
            {
                why = string.Empty;
                return i;
            }
        }

        why = string.Create(CultureInfo.InvariantCulture, $"no known {ArenaProfile.WarLineName} level{(maxLevel < Levels ? $" up to {Numerals[Math.Clamp(maxLevel, 1, Levels) - 1]}" : string.Empty)} reaches a chance of {castChance} at War Magic skill {skill}");
        return -1;
    }

    /// <summary>The verdict fields (see the headless line): the gear and buff stage notes, then the counters, then (M5 1 F1) <see cref="ManaFields"/> without its <c>level=</c> (the line carries it before <c>range_m=</c>, where a test pins it: one key, once). No account or character name is in it.</summary>
    public string VerdictFields()
    {
        var seconds = FightSeconds;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Token(_gearFields, "gear")} {Token(_gearFields, "worn")} buffs={_buffsNote} kills={_tally.Kills} damage_dealt={_tally.DamageDealt} spells_cast={_tally.Casts} misses={_tally.Misses} blocked_picks={_tally.BlockedPicks} wall_hits={_tally.WallHits} {Shots.Fields} xp_gained={_tally.XpGained} damage_taken={_tally.DamageTaken} hits_taken={_tally.HitsTaken} evades={_tally.Evades} level={Level} range_m={_range:0.#} projectiles={_projectiles} walks={_walks} fight_s={(double.IsNaN(seconds) ? "n/a" : seconds.ToString("0", CultureInfo.InvariantCulture))} target_kills={_tally.TargetKills} defended={_defended} fizzles={_castTally.Fizzles} hit_max={_tally.HitMax} target_died_mid_cast={_tally.TargetDiedMidCast} doomed_skips={DoomedSkips} {OpenerFields()} {CastWalkFields()} {PickField()} {PullRestFields()} {ManaFields(withLevel: false)} {TourFields()} {LootFields()}{LivePositionFields()}{LifeThroughFields()}{(BuffsExpired > 0 ? " buffs_expired=" + BuffsExpired.ToString(CultureInfo.InvariantCulture) : string.Empty)}{(_stopRequest is { } stop ? " stop=" + stop.Name : string.Empty)}{(_options.Until is null ? string.Empty : _untilMet ? " until=met" : _untilRefused is not null ? " until=refused" : " until=not-met")}");
    }

    /// <summary>F3: <c>--fight-storeys-min</c> asks for 2 or more storeys, so the fight leg's review frames (first, bolt, hit, kill) wait for their trigger on a storey other than the arrival's (<see cref="FightShotPlan.WaitsForOtherStorey"/>).</summary>
    public bool ReviewFramesWaitForOtherStorey => _options.StoreysMin >= 2 && !_options.ArenaHere && _walk?.Tour is GroundTour { ArrivalStorey: >= 0 };

    /// <summary>F3: the body stands on a storey of the ground other than the arrival's (false while either is unknown: no tour, an arrival on no storey, a body not yet seen on one). Allocates nothing.</summary>
    public bool OffArrivalStorey => _bodyStorey >= 0 && _walk?.Tour is GroundTour { ArrivalStorey: >= 0 } tour && _bodyStorey != tour.ArrivalStorey;

    /// <summary>The distinct storeys of the ground (<see cref="HuntingGround.Storeys"/>, D44) the body stood in during the fight (<c>storeys_visited=</c>).</summary>
    public int StoreysVisited => _storeysVisited;

    /// <summary>The entries into a no-go cell of the ground during the fight (<c>basement_entries=</c>, D44): the walk stops on one, so 0 unless that rule breaks.</summary>
    public int BasementEntries => _basementEntries;

    /// <summary>
    /// The tour's fields (M5 item 2 g6, D44, report-only): <c>storeys_visited= basement_entries= returns= slots_standing= legs_recovered= kills_unpaired= slots_expired=</c>; <c>returns=</c> are the walk's tour's timed returns
    /// (from another storey only), <c>slots_standing=</c> its schedule's hunted slots standing at the end (<see cref="GroundTour"/>); 2-R4: <c>legs_recovered=</c> the failed legs the walk abandoned and went on from
    /// (<c>m5-fight-walk-recover</c>), <c>kills_unpaired=</c> the target kills the schedule paired to no slot and <c>slots_expired=</c> the dead slots it took as standing with no create seen
    /// (<c>TODO(ground-respawn-unseen)</c>), so a recovery or a schedule that does not work shows on the line. F5 (D47, report-only) adds <c>walks_by_purpose=seen:n,presumed:n,due:n,return:n holds= hold_s= slots_seen_empty= walk_loops=</c>
    /// (<see cref="GroundTour.PurposeFields"/>). <c>n/a</c> when the walk has no such tour or count (the arena run, a scripted walk, the headless journey host's walk).
    /// After <see cref="ManaFields"/> on the verdict line, the <c>bsd-fight done</c> line and the journey's den-fight station, where a test pins their place.
    /// </summary>
    public string TourFields()
    {
        var tour = _walk?.Tour as GroundTour;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"storeys_visited={_storeysVisited} basement_entries={_basementEntries} returns={(tour is null ? "n/a" : tour.Returns.ToString(CultureInfo.InvariantCulture))} slots_standing={(tour is null ? "n/a" : tour.Schedule.StandingHunted.ToString(CultureInfo.InvariantCulture))} legs_recovered={(_walk?.LegsRecovered is { } recovered ? recovered.ToString(CultureInfo.InvariantCulture) : "n/a")} kills_unpaired={(tour is null ? "n/a" : tour.Schedule.Unpaired.ToString(CultureInfo.InvariantCulture))} slots_expired={(tour is null ? "n/a" : tour.Schedule.Expired.ToString(CultureInfo.InvariantCulture))} {(tour is null ? NoPurposeFields : tour.PurposeFields(_view?.Clock.Now ?? 0))}");
    }

    /// <summary>F5's fields for a walk with no <see cref="GroundTour"/> (the arena run, a scripted walk).</summary>
    private const string NoPurposeFields = "walks_by_purpose=n/a holds=n/a hold_s=n/a slots_seen_empty=n/a walk_loops=n/a";

    /// <summary>
    /// D41's level cap (M5 item 2 g6, <c>m5-level80-vi</c>): the ground's spell plan's highest level for the character's level (<see cref="HuntingGround.PlanFor"/>; the den: Flame Bolt VI below 100, VII from 100), and
    /// <paramref name="maxLevel"/> (<c>--fight-max-level</c>) lower still. A plan with no row for the level, or a row of another spell family than the bolt line's, leaves <paramref name="maxLevel"/> alone, named in
    /// <paramref name="note"/>.
    /// </summary>
    /// <param name="ground">The ground.</param>
    /// <param name="characterLevel">The character's level.</param>
    /// <param name="maxLevel">The option's cap (1 to 7).</param>
    /// <param name="note">What capped it, for the log.</param>
    /// <returns>The highest level the fight may choose, 1 to 7.</returns>
    public static int PlanCap(HuntingGround ground, int characterLevel, int maxLevel, out string note)
    {
        ArgumentNullException.ThrowIfNull(ground);
        var top = Numerals[Math.Clamp(maxLevel, 1, Levels) - 1];
        if (ground.PlanFor(characterLevel) is not { } row)
        {
            note = $"the ground's plan has no row for level {characterLevel}{(ground.SpellPlan.Count == 0 ? string.Empty : " (TODO(ground-plan-gap))")}; levels up to {top}";
            return maxLevel;
        }

        if (BoltElement.ElementOf(row.Family) is null)
        {
            note = $"the ground's plan names {row.Family} for level {characterLevel}, no bolt line (TODO(ground-plan-family)); levels up to {top}";
            return maxLevel; // M6 14 the element line: every element's bolt line has the same levels, so a plan that names any of them caps the bolt in hand
        }

        var cap = Math.Clamp(Math.Min(row.SpellLevel, maxLevel), 1, Levels);
        note = row.SpellLevel <= maxLevel
            ? $"the ground's plan caps {row.Family} at {Numerals[cap - 1]} for level {characterLevel}"
            : $"--fight-max-level caps {row.Family} at {Numerals[cap - 1]} (the ground's plan allows {Numerals[Math.Clamp(row.SpellLevel, 1, Levels) - 1]} for level {characterLevel})";
        return cap;
    }

    /// <summary>
    /// M6 14 (the owner, 2026-11-13: the climber casts the highest bolt her live skill lands at the plan chance): the cap the fight casts by under <paramref name="options"/>. With <see cref="FightOptions.LiveCap"/> (the level
    /// up job's ladder fight, whose <see cref="FightOptions.MaxLevel"/> is the ladder's cap on her current, buffed War Magic) that cap governs: the ground's plan row, the projection on the projected base skill with no buff, does
    /// not lower it and is named in <paramref name="note"/>. Without it (BSD Mage's den job, a command line's <c>--fight-max-level</c>, every fight without the ladder's cap) <see cref="PlanCap(HuntingGround, int, int, out string)"/>
    /// exactly as before. The fight, its opener and the mana dance read this one cap; a level whose components the pack does not hold is still skipped by the fight's own check.
    /// </summary>
    /// <param name="ground">The ground.</param>
    /// <param name="characterLevel">The character's level.</param>
    /// <param name="options">The fight's options (<see cref="FightOptions.MaxLevel"/>, <see cref="FightOptions.LiveCap"/>).</param>
    /// <param name="note">What capped it, for the log.</param>
    /// <returns>The highest level the fight may choose, 1 to 7.</returns>
    public static int PlanCap(HuntingGround ground, int characterLevel, FightOptions options, out string note)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.LiveCap)
        {
            return PlanCap(ground, characterLevel, options.MaxLevel, out note);
        }

        var cap = Math.Clamp(options.MaxLevel, 1, Levels);
        var row = ground.PlanFor(characterLevel);
        var projection = row is null
            ? $"the ground's plan has no row for level {characterLevel}"
            : $"the ground's plan row, the projection on the base skill, names {row.Family} {Numerals[Math.Clamp(row.SpellLevel, 1, Levels) - 1]} for level {characterLevel}{(row.SpellLevel < cap ? " and does not lower it" : string.Empty)}";
        note = $"the ladder's live cap governs: {ArenaProfile.WarLineName} up to {Numerals[cap - 1]} (the highest level at the plan chance on the current War Magic); {projection}";
        return cap;
    }

    /// <summary>The cap for the player's level now (<see cref="PlanCap(HuntingGround, int, FightOptions, out string)"/>), logged when it changes.</summary>
    private int LevelCapFor(PlayerQualities q)
    {
        var cap = PlanCap(_ground, q.Level, _options, out var note);
        if (cap != _planCapLogged)
        {
            _planCapLogged = cap;
            _log.Info(LogCategory, note);
        }

        return cap;
    }

    /// <summary>D44: the storey and the no-go entry of the cell the body stands in, every fight frame.</summary>
    private void NoteStorey(uint cell)
    {
        if (cell == 0)
        {
            return;
        }

        var storey = _ground.StoreyOf(cell);
        if (storey >= 0)
        {
            _bodyStorey = storey;
        }

        if (storey >= 0 && !_storeysSeen[storey])
        {
            _storeysSeen[storey] = true;
            _storeysVisited++;
            if (_walk?.Tour is GroundTour tour)
            {
                tour.BodyStoreysVisited = _storeysVisited; // M5 7 P7o: the tour leaves for --fight-storeys-min until the body has stood on that many storeys
            }
        }

        var noGo = _ground.IsNoGo(cell);
        if (noGo && !_inNoGo)
        {
            _basementEntries++;
            _log.Warn(LogCategory, $"the body entered the no-go cell 0x{cell:X8} of {_ground.Name} (basement entry {_basementEntries})");
        }

        _inNoGo = noGo;
    }

    /// <inheritdoc/>
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        _hookSession = session;
        _lastClock = session.Clock.Now;
        PackPeakSample(session, _lastClock); // M6 7 finish: report-only
        LivePositionWatch(session, _lastClock); // M6 14 (m6-live-position): report-only
        ThreatShadowStep(session, _lastClock); // M6 16 E2b: the self-defence list beside the threat board, report-only
        IdleWatch(session, _lastClock); // M6 8: report-only
        if (Failure is null && !_specErrorRaised && _options.SpecError is { } badSpec)
        {
            _specErrorRaised = true;
            return Fail(session, "fight: " + badSpec); // M6 5 q4: a malformed station argument is a loud stop at the station's start, never a guess
        }

        if (Failure is null && !_wantsChecked)
        {
            _wantsChecked = true;
            var state = session.State;
            var wants = GroundWants.Check(_ground, state.Self.Qualities.Level, state.Self.Spellbook.Knows, wcid => HardestOn(state, wcid)); // M6 14 the element line: a target no line she casts can hurt is the stop
            foreach (var line in wants.Lines)
            {
                _log.Warn(LogCategory, line);
            }

            if (wants.Failure is { } refused)
            {
                return Fail(session, $"{NoCastableFailure}: {refused}"); // M6 8 B1, M6 14: a target every line she casts does nothing to is a loud stop, never a silent bolt
            }
        }

        if (Failure is not null)
        {
            if (_failLeave)
            {
                return FailLeaveStep(session);
            }

            if (_failLogOff && _pending is { Outcome: { } left })
            {
                _pending = null;
                _failLogOff = false;
                _log.Info(LogCategory, "the failed run logged off: " + left.Kind + (left.Detail is null ? string.Empty : ": " + left.Detail));
            }

            if (_failLogOff)
            {
                return ScenarioStatus.Running;
            }

            Dispose();
            return ScenarioStatus.Failed;
        }

        if (Stage == "done")
        {
            return ScenarioStatus.Succeeded;
        }

        var actions = session.Actions;
        var clock = session.Clock.Now;
        switch (Stage)
        {
            case "start":
                Enter(_gear is not null ? "gear" : "buffs", clock);
                break;

            case "gear":
                switch (_gear!.Step(session, now))
                {
                    case ScenarioStatus.Failed:
                        return Fail(session, "gear: " + _gear.Failure);
                    case ScenarioStatus.Succeeded:
                        _gearFields = _gear.VerdictFields();
                        _log.Info(LogCategory, "gear done (" + _gearFields + ")");
                        Enter("buffs", clock);
                        break;
                }

                break;

            case "buffs":
                return BuffsCheck(session, clock);

            case "buffs-mode":
                if (_pending is { Outcome: { } magicForBuffs })
                {
                    _pending = null;
                    if (!magicForBuffs.Succeeded)
                    {
                        return Fail(session, "buffs: the magic combat mode was not entered: " + Describe(magicForBuffs));
                    }

                    if (!_cycle!.Start(clock))
                    {
                        return Fail(session, "buffs: the cycle did not start: " + (_cycle.Result?.Failure ?? "a pass is already running"));
                    }

                    Enter("buffs-cycle", clock);
                }

                break;

            case "buffs-cycle":
                switch (_cycle!.Step(clock))
                {
                    case BuffStatus.Failed:
                        return Fail(session, "buffs: " + _cycle.Result?.Failure);
                    case BuffStatus.Done:
                        var result = _cycle.Result;
                        _buffsNote = string.Create(CultureInfo.InvariantCulture, $"topped-up:{result?.Cast ?? 0}");
                        _log.Info(LogCategory, $"the buff top-up is done ({result?.Cast ?? 0} casts, {result?.Active ?? 0} of {result?.Wanted ?? 0} lines active)");
                        Enter("place", clock);
                        break;
                }

                break;

            case "place":
                return PlaceStep(session, clock);

            case "teleporting":
                return TeleportingStep(session, clock);

            case "settle":
                return SettleStep(session, clock);

            case "mode-magic":
                return ModeStep(session, clock);

            case "fight":
                return FightStep(session, now, clock);

            case "mode-peace":
                if (_pending is { Outcome: { } peace })
                {
                    _pending = null;
                    if (!peace.Succeeded)
                    {
                        _log.Warn(LogCategory, "the player was left in the magic combat mode: " + Describe(peace)); // report-only, as the buff scenario's
                    }

                    return Conclude(session);
                }

                break;

            case "leave-den":
                return LeaveDenStep(session, clock);

            case "logging-off":
                if (_pending is { Outcome: { } loggedOff })
                {
                    _pending = null;
                    if (loggedOff.Kind != ActionOutcomeKind.Confirmed)
                    {
                        return Fail(session, "logging off: " + Describe(loggedOff));
                    }

                    _pending = actions.Quit(ActionSource.Host);
                    Enter("quitting", clock);
                }

                break;

            case "quitting":
                if (_pending is { Outcome: { } quit })
                {
                    _pending = null;
                    if (quit.Kind != ActionOutcomeKind.Sent)
                    {
                        return Fail(session, "quitting: " + Describe(quit));
                    }

                    LogVerdict(session);
                    Stage = "done";
                    Dispose();
                    return ScenarioStatus.Succeeded;
                }

                break;

            default:
                return Fail(session, $"TODO(state): unknown fight stage '{Stage}'");
        }

        return ScenarioStatus.Running;
    }

    /// <summary>Stops following the session (the tally, the handlers, the walk).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tally.Dispose();
        _diedSubscription?.Dispose();
        _createdSubscription?.Dispose();
        _movedSubscription?.Dispose();
        DisposeDefence(); // M5 7 F4
        EndThreatShadow(); // M6 16 E2b: the fight's line of its shadow's asks, report-only
        _volleyCasts?.CloseAll(); // M5 7 F24
        _ringCasts?.CloseAll(); // M5 7 F22: the ring casts still counting their hits are closed and logged
        DisposeHeals(); // M5 7 F19
        _walk?.Dispose();
        _loot?.Dispose();
    }

    private ScenarioStatus BuffsCheck(IGameSessionView session, double clock)
    {
        if ((_stages & FightStages.Buffs) == 0)
        {
            Enter("place", clock);
            return ScenarioStatus.Running;
        }

        _cycle ??= new BuffCycle(session, _spells, _buffOptions, _log, ArmourBuffLedger.For(session.State), profile: _options.BuffProfile); // the session's armour memory (M4 4.1 lane c): a top-up after a login seeds it by appraisal
        var due = _cycle.RebuffDue(clock, TopUpSeconds);
        if (due.Count == 0)
        {
            _buffsNote = "skipped";
            _log.Info(LogCategory, $"the buff top-up is skipped: all {_cycle.Wanted} profile lines have at least {TopUpSeconds:0} s left");
            Enter("place", clock);
            return ScenarioStatus.Running;
        }

        _log.Info(LogCategory, $"the buff top-up is needed: {due.Count} of {_cycle.Wanted} lines are missing or have under {TopUpSeconds:0} s left ({string.Join(';', due)})");
        _pending = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
        Enter("buffs-mode", clock);
        return ScenarioStatus.Running;
    }

    /// <summary>
    /// M5 7 F30: a stop asked before the first pick (the autopilot's counters ask for a trip at the fight's start) has its safe point now: the fight ends at once with no walk, no teleport and no cast. Null when no stop is asked.
    /// </summary>
    private ScenarioStatus? StopBeforeTheFirstPick(IGameSessionView session, double clock)
    {
        if (_stopRequest is not { } asked)
        {
            return null;
        }

        if (LeaveRefreshAsked)
        {
            _log.Info(LogCategory, "buff refresh: leave: not run, the fight stops before its first pick (F30); the lines are the next station's (D126)");
            _leaveHorizon = double.NaN;
        }

        return EndFight(session, clock, $"the fight was asked to stop before its first pick ({asked.Name}): no walk, no cast");
    }

    private ScenarioStatus PlaceStep(IGameSessionView session, double clock)
    {
        if (StopBeforeTheFirstPick(session, clock) is { } stopped)
        {
            return stopped;
        }

        if (_options.ResumeOnGround)
        {
            Subscribe(session);
            _walk?.Prepare(session, clock);
            if (_walk?.Failure is { } resumeFailure)
            {
                return Fail(session, "place: " + resumeFailure);
            }

            _log.Info(LogCategory, "fight: resumed on the ground: no walk to the arrival");
            Enter("mode-magic", clock);
            return ScenarioStatus.Running;
        }

        if ((_stages & FightStages.Place) == 0)
        {
            Enter("mode-magic", clock);
            return ScenarioStatus.Running;
        }

        Subscribe(session);
        _walk?.Prepare(session, clock);
        if (_walk?.Failure is { } walkFailure)
        {
            return Fail(session, "place: " + walkFailure);
        }

        _teleportMark = _selfTeleports;
        _seam.Start(session, new TravelRequest(_destination, _options.PlacePurpose), clock); // M4 P5b: the travel seam (PlaceTravel by default: today's Tools.Place)
        Enter("teleporting", clock);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus TeleportingStep(IGameSessionView session, double clock)
    {
        var travel = _seam.Step(session, clock);
        if (travel == TravelStatus.Failed)
        {
            return Fail(session, "place: sending the player to the den: " + _seam.Failure);
        }

        _walk?.Tick(session, clock);
        if (travel == TravelStatus.Arrived && (_seam.Authoritative || (_selfTeleports > _teleportMark && session.State.Self.Travel == SelfTravel.Present)))
        {
            // The arrival contract (IJourneyTravel): a place's arrival is today's check (the teleport seen, present); a run's, a recall's or a None's is authoritative.
            _settleUntil = clock + SettleSeconds;
            Enter("settle", clock);
            return ScenarioStatus.Running;
        }

        return _seam.Means == TravelMeans.Place && clock - _stageStart > TeleportSeconds
            ? Fail(session, string.Create(CultureInfo.InvariantCulture, $"place: the player did not arrive in the den and become present within {TeleportSeconds:0.#} s (teleports {_selfTeleports - _teleportMark}, travel {session.State.Self.Travel})"))
            : ScenarioStatus.Running;
    }

    private ScenarioStatus SettleStep(IGameSessionView session, double clock)
    {
        if (_walk?.Tick(session, clock) == FightWalkStatus.Failed)
        {
            return Fail(session, "settle: the walk failed: " + _walk.Failure);
        }

        if (clock >= _settleUntil)
        {
            var cell = session.State.Self.Position?.CellId ?? 0;
            if (!_ground.IsWalkable(cell))
            {
                // D12: the loop never fights from a cell off the ground's walkable cells; a settle that ends elsewhere is a loud failure, not a note (3.1-R4).
                return Fail(session, $"settle: the player settled in cell 0x{cell:X8}, which is not a walkable cell of {_ground.Name} (floor cell no); the fight would start off the floor");
            }

            _log.Info(LogCategory, $"settled in cell 0x{cell:X8}; floor cell yes");
            Enter("mode-magic", clock);
        }

        return ScenarioStatus.Running;
    }

    private ScenarioStatus ModeStep(IGameSessionView session, double clock)
    {
        Subscribe(session);
        if (_pending is null)
        {
            if (StopBeforeTheFirstPick(session, clock) is { } stopped)
            {
                return stopped;
            }

            if ((_stages & FightStages.Mode) == 0 || session.State.Self.Qualities.CombatMode == CombatModes.Magic)
            {
                _log.Info(LogCategory, "the player is in the magic combat mode");
                return BeginFight(session, clock);
            }

            _pending = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
            return ScenarioStatus.Running;
        }

        _walk?.Tick(session, clock);
        if (_pending.Outcome is { } mode)
        {
            _pending = null;
            if (!mode.Succeeded)
            {
                return Fail(session, "mode-magic: the magic combat mode was not entered: " + Describe(mode));
            }

            return BeginFight(session, clock);
        }

        return ScenarioStatus.Running;
    }

    private ScenarioStatus BeginFight(IGameSessionView session, double clock)
    {
        var self = session.State.Self;
        uint skill;
        string why;
        if (_options.SpellId != 0)
        {
            // M4 P5b J7: a named spell instead of the Flame Bolt line (the hero plan's Cassius' Ring of Fire): slot 0 holds it, and a fizzle has no lower level to drop to.
            _levelIds[0] = _options.SpellId;
            if (_options.ArenaHere)
            {
                skill = _options.ArenaSkill;
                why = string.Empty;
                _level = 0;
            }
            else if (!GearCasts.TryCurrentSkill(self.Qualities, MageTemplate.WarMagic, out skill, out why))
            {
                _level = -1;
            }
            else if (self.Spellbook is { IsKnown: false } || !self.Spellbook.Knows(_options.SpellId))
            {
                _level = -1;
                why = $"the spellbook does not hold spell {_options.SpellId} (the build learns it)";
            }
            else if (!_spells.TryGetValue(_options.SpellId, out var named) || GearCasts.Chance(skill, named.Power) < _buffOptions.CastChance)
            {
                _level = -1;
                why = $"spell {_options.SpellId} is not in the SpellTable or does not reach a cast chance of {_buffOptions.CastChance} at War Magic skill {skill}";
            }
            else
            {
                _level = 0;
            }
        }
        else if (_options.ArenaHere)
        {
            skill = _options.ArenaSkill;
            _level = ChooseLevel(skill, null, _buffOptions.CastChance, out why, _options.MaxLevel); // the arena-here run does not read the spellbook
        }
        else
        {
            ReadShortStock(session); // M6 14 cap: a short carried line leaves out the war spells that burn it, before the line and the level are chosen
            StartElement(session); // M6 14 the element line: the ground's planned line when the book can cast it, else the Flame Bolt; each target then takes its hardest line (FightScenario.Element.cs)
            ElementsUsed.For(session.State).Note(_boltElement); // M6 14: the line in hand at the start (the caster keep's)
            _level = ChooseOnLine(self.Qualities, self.Spellbook, out skill, out why, LevelCapFor(self.Qualities)); // D41: the ground's plan, then --fight-max-level
        }

        if (_level < 0 && _stockCapNotes.Count > 0)
        {
            return Fail(session, $"{NoCastableFailure}: {StockCapEndWords()}; no level of the {_boltLineName} is left that the pack pays for over the lines ({why})"); // M6 14 cap: the named end when nothing can be paid
        }

        if (_level < 0)
        {
            return Fail(session, "fight: no " + (_options.SpellId != 0 ? "spell" : _boltLineName + " level") + " to cast: " + why);
        }

        if (!TakeCastableAtStart(session, out var castableWhy, out var lack))
        {
            return EndNoCastable(session, castableWhy, lack); // M6 7 K: the pack pays for no level of the bolt
        }

        if (!_spells.TryGetValue(_levelIds[_level], out var info))
        {
            return Fail(session, $"fight: the SpellTable has no spell {_levelIds[_level]}");
        }

        LogStockCap(); // M6 14 cap

        uint initPlusRanks;
        if (_options.ArenaHere)
        {
            initPlusRanks = _options.ArenaSkill; // the stand-in's range is worked out from its fixed skill alone (StandInWorld.Combat.cs)
        }
        else if (self.Qualities.TryGetSkill(MageTemplate.WarMagic, out var war))
        {
            initPlusRanks = war.Init + war.Ranks;
        }
        else
        {
            return Fail(session, "fight: the read model has no War Magic skill");
        }

        _initPlusRanks = initPlusRanks;
        _range = CastRange(info, initPlusRanks);
        _manaNeeded = GearCasts.ManaCeiling(info, 0);
        ReadMana(self.Qualities, _options.ArenaHere ? null : self.Spellbook);
        ReadDrain(session);
        NoteSignature(self.Qualities);
        if (CarriedStockStop(session) is { } stop)
        {
            return Fail(session, "fight: " + stop);
        }

        _tally.Start(session);
        _fightStart = clock;
        _untilMark = session.State.Chat.Total; // M6 5 q4: the until's chat form reads the lines since here
        _target = 0;
        _missesInARow = 0;
        _phase = Phase.Choose;
        _log.Info(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"the fight begins: {info.Name} ({info.Id}) at chance {GearCasts.Chance(skill, info.Power):0.0000} from War Magic {skill}, range {_range:0.#} m (init+ranks {initPlusRanks}, base {info.BaseRangeConstant} + {info.BaseRangeMod} per point), mana up to {_manaNeeded}{(_options.Mana.On ? $" (try floor {_manaFloor} at Mana Conversion {_manaConversion}; the mana order, {_danceCount} Stamina to Mana Self level(s) to dance)" : " (--cast-mana-tries 0: the ceiling wait)")}; {(_options.UnlimitedKillCount ? "no kill limit" : _options.Kills.ToString(CultureInfo.InvariantCulture) + " kill(s)")}{(_options.Minutes > 0 ? $", a clean end at {_options.Minutes:0.#} min" : string.Empty)} within {_options.FailSeconds:0.#} s{(_options.MaxLevel < Levels ? $"; levels up to {Numerals[_options.MaxLevel - 1]}" : string.Empty)}"));
        ApplyHuntKeepOut(session); // M6 7 AR: before the seed, so the slots kept out are not counted standing
        _walk?.Tour?.Seed(session.State.Objects, clock); // F4: the creatures already standing in their slots' cells were created before the fight (OnCreated hears only the fight's own), so their kills would pair by the death cell alone
        Enter("fight", clock);
        return ScenarioStatus.Running;
    }

    /// <summary>
    /// M5 3 m1 (D8, D48): the counts read at the fight's start, before the first cast. Every CARRIED stock line of the ground's supplies (no home vendor sells it: <see cref="HuntingGround.IsCarried"/>) that the kit holds and whose count in the pack is under its
    /// <c>stop_under</c> stops the fight, named (the carried-stock stop, <c>TODO(m6-buy-trip-autopilot)</c> since M6 1 d (D44: the buy-trip station makes the restock, no plan runs it yet; <c>GearKit</c>'s stop text opens with the same tag): the Platinum Scarab stack is 2, under its stop line 3; the vendors of ... sell none (D8); <c>m5-mage-foci</c> when a school's focus is not held, the foci formula's precondition);
    /// a sold line under its line is only logged (the counters are m4's). A kit without the line (a test's hand-made one) is not checked; the stand-in arena has no stock.
    /// </summary>
    /// <returns>The stop in words, or null when every carried line holds.</returns>
    private string? CarriedStockStop(IGameSessionView session)
    {
        if (_options.ArenaHere || _kit.ItemOf(GearKit.MageFociWcids[0]) is null)
        {
            return null;
        }

        var inventory = session.State.Self.Inventory;
        if (!inventory.IsKnown)
        {
            return "m5-mage-foci: the inventory is not known, so the foci and the stock cannot be counted before the first cast";
        }

        var held = GearInventory.CountAll(session.State, out var unsettled);
        if (unsettled > 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"m5-mage-foci: {unsettled} held items have no world object yet, so the stock cannot be counted before the first cast");
        }

        return GearKit.FightStartStop(_ground, _kit, held, text => _log.Info(LogCategory, text), _floorOnlyLines, _cappedLines); // M6 14 motes: a floor-only line (the ring's Diamond Scarab) leaves its options out, it stops nothing (D148)
    }

    private ScenarioStatus FightStep(IGameSessionView session, double now, double clock)
    {
        if (_died)
        {
            return Fail(session, "fight: the player died (" + _deathText + ")");
        }

        if (session.Actions.KillSwitch)
        {
            // M5 1 fix A, 1-R15 (D13): the kill switch is on: the fight fails at once, named as the keeper names it (Blocked why=kill-switch), and sends nothing more as Automation (a Drink or a mode change would be cancelled anyway,
            // and the fight would otherwise wait on in the mana phase until its next bolt).
            return Fail(session, "fight: Blocked why=" + VitalsKeeper.KillSwitchWhy + ": the kill switch is on, the fight sends nothing more");
        }

        var walkStatus = _walk?.Tick(session, now) ?? FightWalkStatus.Idle;
        if (!_options.ArenaHere)
        {
            NoteStorey(session.State.Self.Position?.CellId ?? 0);
            NoteCrossingProgress(session.State.Self.Position?.CellId ?? 0, clock); // M6 7 D (m6-gate-den-storey-race)
            if (session.State.Self.Position is { } seenFrom)
            {
                _walk?.Tour?.Look(seenFrom.CellId, session.State.Objects, clock); // F5 (D47): the tour learns from what the player sees (seen-empty slots, the targets the table holds)
            }
        }

        if (_target != 0 && session.State.Objects.TryGet(_target, out var seen) && StandsAt(session, seen) is { } seenAt)
        {
            (_seenTarget, _seenWcid, _seenCell) = (_target, seen.Weenie.WeenieClassId, seenAt.CellId);
            if (_loot is not null)
            {
                (_seenName, _seenAt) = (seen.Name, seenAt);
            }
        }

        if (_tally.TargetKills > _fedKills)
        {
            // M5 7 F15 (7-F1): a kill notice that names the target's name is the target's own only when the target is dead or gone; a ring or a stray bolt that kills a neighbour of the same name writes the same text. The notice is
            // held as a pending kill and settled below (and again each frame) by the object table, never by the name.
            _fedKills = _tally.TargetKills;
            SettlePendingKill(session, clock);
            _killPendingGuid = _target;
            _killPendingAt = clock;
            _killPendingName = _target != 0 && session.State.Objects.TryGet(_target, out var pendingBody) ? pendingBody.Name : _seenName; // M5 7 P7b (D139 i): the splash pairing leaves this name's notices to the target path while it waits
        }

        SettlePendingKill(session, clock);
        if (_claims.Count > 0)
        {
            PairSplashKills(session, clock); // M5 7 P7b (D139 i): a kill notice that was not the target's queues the corpse it names
        }

        if (walkStatus == FightWalkStatus.Failed)
        {
            // Any phase: the walker left the floor, or a leg failed (a leg's end is only read in the Walk phase, but the floor check runs every frame).
            return Fail(session, "fight: the walk failed: " + _walk!.Failure);
        }

        if (AirborneHolds(session, clock))
        {
            return ScenarioStatus.Running; // M6 14 airborne: the walk's jump is in flight: every rule below waits for its landing check (FightScenario.Airborne.cs)
        }

        if (_lootHold is { } held && _loot is { Active: true })
        {
            _loot.Abandon(session, "under the loot's hold (" + held + ")"); // M6 14 recall unblock (m6-loot-hold): no corpse is opened, and the stop below does not wait for one
        }

        if (_stopRequest is { } stopAsked && !LootBusy && !SplashPending && _killPendingAt < 0 && _phase != Phase.Refresh && !LeaveRefreshHolds(session, clock)) // D126 (a): a trip's stop waits for the lines due within its length // M5 7 F23: a buff refresh pass cannot be cut; the stop waits for its end // M5 7 F15 (7-F1): a kill notice the table has not yet confirmed (at most KillNoticeSeconds) is settled first, so the tour hears the last kill
        {
            // M5 4 a1 (D57): the external stop, at the clean minutes stop's point: the kill notices have reached the tour above, and no loot step is in flight.
            return EndFight(session, clock, $"the fight was asked to stop ({stopAsked.Name})");
        }

        if (_options.Until is { } until && !LootBusy && !SplashPending && _killPendingAt < 0 && _phase != Phase.Refresh && UntilHolds(session, until) is { } untilWhat)
        {
            // M6 5 q4 (D57): the quest's own end, at the same safe point as the external stop: no loot in flight, the kill notices settled.
            _untilMet = true;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"fight: until met: {untilWhat} after kills={_tally.Kills} s={clock - _fightStart:0}"));
            return EndFight(session, clock, "until met: " + untilWhat);
        }

        if (_options.Until is { Chat: null } itemUntil && _untilRefused is { } refusal && !LootBusy && !SplashPending && _killPendingAt < 0 && _phase != Phase.Refresh)
        {
            // M6 5 F1 (review 5-R4): the server refused the only item that would end the fight: it can never be met, so the fight ends by name (the quest station stops hunt-until:refused:<wcid>); no hunt for hours.
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"fight: until: refused: {itemUntil.ItemWcid} ({refusal}); the until can not be met, the fight ends after kills={_tally.Kills} s={clock - _fightStart:0}"));
            return EndFight(session, clock, string.Create(CultureInfo.InvariantCulture, $"until: refused: {itemUntil.ItemWcid} ({refusal})"));
        }

        if (_options.Minutes > 0 && clock - _fightStart >= _options.Minutes * 60)
        {
            // M5 1 F1: the clean duration stop (--fight-minutes): the fight succeeds with the kills it made; --fight-seconds stays the failure limit.
            return EndFight(session, clock, string.Create(CultureInfo.InvariantCulture, $"the fight's {_options.Minutes:0.#} minutes are up"));
        }

        if (!_options.UnlimitedKillCount && _tally.Kills >= _options.Kills && !LootBusy && !SplashPending && _killPendingAt < 0 && !StoreysHoldTheWin(session, clock)) // M5 7 P7o (D44 c2b): not won while the tour still reaches the storeys it is short of
        {
            // The judge wants damage_dealt > 0 and ACE writes no damage line for the killing blow: a first hit that kills leaves it 0, so the fight goes on (bounded by the seconds limit) until a damage line (3.1-R1).
            if (_tally.DamageDealt > 0)
            {
                return EndFight(session, clock, "the fight is won");
            }

            if (!_damageWaitLogged)
            {
                _damageWaitLogged = true;
                _log.Info(LogCategory, $"kills={_tally.Kills} of {_options.Kills} reached but damage_dealt=0 (ACE writes no damage line for the killing blow); fighting on until a damage line, within the seconds limit");
            }
        }

        if (clock - _fightStart > _options.FailSeconds && !CrossingOutlivesTheBudget(session, clock)) // M6 7 D (m6-gate-den-storey-race): an owed crossing that still progresses is not cut by the clock
        {
            return Fail(session, string.Create(CultureInfo.InvariantCulture, $"fight: {_options.FailSeconds:0.#} s passed with kills={_tally.Kills} of {_options.Kills} and damage_dealt={_tally.DamageDealt} (casts={_tally.Casts} misses={_tally.Misses} walks={_walks} phase={_phase}){_crossingEndNote}"));
        }

        if (_health is { Outcome: not null })
        {
            LogHealth(session);
        }

        if (session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out var staminaHeld, out _))
        {
            _refill.Note(staminaHeld); // M5 7 F5: stamina_min=
        }

        if (session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var healthHeld, out _))
        {
            _drain.Note(healthHeld); // M5 7 F7: health_min=
        }

        var walkCut = _loot is not null && _loot.WalkCutStep(session, clock); // M6 14 (m6-loot-walk-cut): the after-check of a corpse's walk ended with a key, read in every phase
        CastWaitWatch(session, clock); // M6 14 (m6-cast-wait): report-only
        ExpiryWatch(clock); // M6 14 upkeep mana: a buff line's lapse is named in every phase, not only at a decision
        switch (_phase)
        {
            case Phase.Choose:
                _loot?.Sweep(session, clock); // M5 7 loot retry: a put-off corpse whose window passed, or that is gone, is given up
                if (walkCut)
                {
                    return ScenarioStatus.Running; // M6 14: the Use whose walk was just ended is not answered yet (at most LootWalkCutSeconds): no cast is asked behind it
                }

                if (_lootHold is null && _loot is { Active: true } && _loot.Step(session, clock))
                {
                    _lootStepAt = clock; // M6 14 combine2: the loot step's last frame of work (its last pick-up's animation runs on past it on the server)
                    return ScenarioStatus.Running; // M5 3 m2: the kill's corpse first (the walk, the open, the rule's pick-ups, the close), then the next target
                }

                if (BeginCombines(session, clock) is { } combining)
                {
                    return combining; // M6 14 combine: between kills, safe: the until's pairs are combined first
                }

                return Choose(session, clock);

            case Phase.Combine:
                return CombineStep(session, clock);

            case Phase.Mana:
                if (_options.Mana.On)
                {
                    return ManaOrderStep(session, clock);
                }

                // --cast-mana-tries 0: today's ceiling wait, exactly.
                if (ManaShort(session))
                {
                    return ScenarioStatus.Running;
                }

                _phase = Phase.Choose;
                return ScenarioStatus.Running;

            case Phase.Cast:
                return CastAnswer(session, clock);

            case Phase.Drain:
                return DrainStep(session, clock);

            case Phase.Opener:
                return OpenerAnswer(session, clock);

            case Phase.Refresh:
                return RefreshPassStep(session, clock);

            case Phase.PullRest:
                return PullRestRun(session, clock);

            case Phase.Hit:
                return HitWait(session, clock);

            case Phase.DeadWait:
                if (_tally.Kills > _killMark || clock - _deadSince > KillNoticeSeconds)
                {
                    if (_tally.Kills > _killMark)
                    {
                        NoteKill(session, clock);
                    }
                    else
                    {
                        _log.Warn(LogCategory, $"the target 0x{_target:X8} died but no kill notice came within {KillNoticeSeconds:0.#} s; it is not counted");
                    }

                    _phase = Phase.Choose;
                }

                return ScenarioStatus.Running;

            case Phase.Walk:
                if (EmergencyDropsWalk(session, clock) || DefenceDropsWalk(session, clock) || RefreshDropsWalk(session, clock) || SightDropsWalk(session, clock))
                {
                    return ScenarioStatus.Running; // M5 7 F4: an attacker is in range with a clear line now; D126 (a): a line under the hard floor is recast before the walk goes on; M6 7 Y: a target came into range and line
                }

                NoteBreadcrumb(session); // M5 7 P7i: the way in, for a walk out of a stuck spot
                if (WalkStuckGivesUp(session, clock) || EscapeStep(session, walkStatus) || WalkBackStep(session, clock, walkStatus))
                {
                    // M5 7 P7b (D139 g): a stuck walk is given up and the fight picks again; (D139 j): a walk back ends when its corpse is sent again or its leg ends; P7i: a stuck spot is walked out of, or the recall is asked
                    return _spotRecall is { } recall ? FailStuckSpot(session, recall) : ScenarioStatus.Running;
                }

                switch (walkStatus)
                {
                    case FightWalkStatus.Arrived:
                        if (!_holdingLogged)
                        {
                            _log.Info(LogCategory, "the leg is done"); // F5: a hold's recheck is no leg
                        }

                        SpotLegArrived(session); // M5 7 P7i: an arrival away from a stuck spot starts its count again

                        _missesInARow = 0;
                        _phase = Phase.Choose;
                        break;
                    case FightWalkStatus.Idle:
                        return Fail(session, "fight: the walk stopped without arriving");
                }

                return ScenarioStatus.Running;

            default:
                return Fail(session, $"TODO(state): unknown fight phase {_phase}");
        }
    }

    /// <summary>
    /// Picks the nearest live tusker on the floor in range (<see cref="WorldObjectTable"/>'s nearest-creature query) and casts at it. Retail picks its attack target in <c>ClientCombatSystem::GetAttackTarget</c>
    /// (11.6096 0x0056B630, 11.4186 0x0056A8F0); the loop has no selection, so it stands in for it, and the server stays the judge.
    /// </summary>
    private ScenarioStatus Choose(IGameSessionView session, double clock)
    {
        var self = session.State.Self;
        _walkBackCorpse = 0; // M5 7 P7b (D139 j): a walk back that something else ended (an attacker, a heal) leaves its corpse queued as lost from view
        if (self.Position is not { } here)
        {
            return ScenarioStatus.Running;
        }

        if (!_options.ArenaHere && _options.SpellId == 0 && SignatureChanged(self.Qualities))
        {
            Rechoose(self);
        }

        PruneKilled(session.State.Objects);
        PruneDefence(session.State.Objects);
        PruneStaminaYield(session.State.Objects);
        PruneLiveHealth(session.State.Objects);
        Picker.Rates.NoteIncoming(clock, _tally.DamageTaken); // M5 7 F16: the incoming rate is read every decision, the emergency line is worked out from it
        _finishGuid = 0;
        _walkSight = false;
        if (ReserveStep(session, clock, recallOnly: true))
        {
            return ScenarioStatus.Running; // M6 8: the retreat waits for the recall's mana: the potion comes before the heal
        }

        if (EmergencyStep(session, clock, in here))
        {
            NoteRefreshBlocked("the emergency heal came first"); // M5 7 F24
            return ScenarioStatus.Running; // M5 7 F16 (D91): the emergency drain outranks every other move: Drain Health Other I at the fullest tusker in reach
        }

        if (_finishGuid != 0)
        {
            NoteRefreshBlocked("the emergency's finishing bolt came first");
            _defendingGuid = _finishGuid;
            return BeginTarget(session, clock, in here, _finishGuid, DefenceMove.Cast, leaving: false); // M6 7 Y: the bolt that kills the only attacker beats the emergency's heal (FightScenario.Emergency.cs)
        }

        if (ReserveStep(session, clock, recallOnly: false))
        {
            return ScenarioStatus.Running; // M6 8: with potions in use the mana is kept at the line and one bolt over it, before any bolt
        }

        if (RefreshStep(session, clock))
        {
            return ScenarioStatus.Running; // M5 7 F23: the buff refresh between kills, when no hostile is in reach
        }

        var guid = 0u;
        var found = false;
        // M5 7 F4 (self-defence, FightScenario.SelfDefence.cs): a creature that attacked the player is the target before the profile's, whatever the profile says of it; none that can be cast at from here walks (the line rule) or, in a no-go cell, holds.
        var defence = DefenceStep(session, clock, in here, announce: true, out var defender);
        if (defence == DefenceMove.Hold)
        {
            if (HealDecide(session, clock))
            {
                NoteDrainStart(session, clock);
            }

            return ScenarioStatus.Running; // M5 7 F15 (7-F5): the hold heals first when the health is under the drain line (Heal Self needs no target); DefendHoldSeconds bounds the hold
        }

        if (defence == DefenceMove.Step)
        {
            if (HealDecide(session, clock))
            {
                NoteDrainStart(session, clock);
                return ScenarioStatus.Running; // Heal Self before the step
            }

            return StartDefenceStep(session, clock, defender); // M5 7 P7s (D148, D88): a step to a spot with a line, or out of its reach (FightScenario.SelfDefence.cs)
        }

        if (defence == DefenceMove.Walk)
        {
            if (HealDecide(session, clock))
            {
                NoteDrainStart(session, clock);
                return ScenarioStatus.Running; // M5 7 F15 (7-F5): Heal Self before the walk
            }

            return StartDefenceWalk(session, clock, defender); // M6 6 B2 (m5-defend-walk-to): a leg toward it, to a spot with range and a line; else the tour's next leg, named (FightScenario.SelfDefence.cs)
        }

        if (defence == DefenceMove.Life)
        {
            if (HealDecide(session, clock))
            {
                NoteDrainStart(session, clock);
                return ScenarioStatus.Running; // Heal Self first while the health is under the drain line, as before the walk it stands in for
            }

            return LifeThroughBegin(session, clock, in here); // M6 14 (m6-life-through): Drain Health Other or Harm Other at the attacker no bolt reaches, in place of the walk (FightScenario.LifeThrough.cs)
        }

        _defendingGuid = defence == DefenceMove.Cast ? defender : 0;
        if (defence == DefenceMove.Cast)
        {
            (guid, found) = (defender, true);
        }

        _pickExcluded.Clear();
        _pickExcluded.AddRange(_killed);
        ExcludeWallBlocked(session, clock); // M6 7 AM: a target the bolt could not reach three times waits out its block
        if (found && Shots.IsDropped(guid, clock) && LifeThroughPlans(session, clock, in here, guid))
        {
            // M6 14 (m6-life-through): an attacker the shot rule dropped (its bolts came to nothing) is fought with a life spell while the drop stands, before the close-attacker rule takes the drop back for more bolts.
            if (HealDecide(session, clock))
            {
                NoteDrainStart(session, clock);
                return ScenarioStatus.Running;
            }

            return LifeThroughBegin(session, clock, in here);
        }

        if (found && ShotDropHolds(session, guid, clock))
        {
            (guid, found, _defendingGuid) = (0, false, 0); // M6 8 L: an attacker the shot rule dropped is not shot at again until its time is up, or until it stands close
        }

        if (ExcludeDoomed(session, clock) && found && _pickExcluded.Contains(guid))
        {
            (guid, found, _defendingGuid) = (0, false, 0); // M5 7 D124 (S1b): an attacker the projectiles in flight will kill is left to them (FightScenario.InFlight.cs)
        }

        if (found && defence == DefenceMove.Cast && guid != _target && KeepsCurrent(session, clock, in here, guid))
        {
            // M6 14 tactics (live ladder56 08:33:02.349 UTC): the attacker is weighed against the target she is on, by each one's seconds to kill and damage a second on her; the one that costs her less health first is
            // finished first (FightScenario.TargetSwitch.cs), so a hurt wisp is not left to bolt her while a fresh one is taken
            (guid, _defendingGuid) = (_target, IsAttacker(_target) ? _target : 0);
        }

        var killGrace = KillInGrace();
        if (killGrace)
        {
            _pickExcluded.Add(_killPendingGuid); // M5 7 F15 (7-F1): the notice may be this creature's own death, not yet in the table
        }

        if (!found && !killGrace && _doomedThisPick == 0 && PullRestBegins(session, clock, in here))
        {
            return ScenarioStatus.Running; // M6 7 AB: no attacker, and a vital under the ground's floor: the rest comes before the pick and before any walk (FightScenario.PullRest.cs)
        }

        if (!found && PullBarHolds(session, in here))
        {
            return ScenarioStatus.Running; // M6 7 AN (the owner): under the ground's fullness bar no pick and no walk, whatever kept the rest from beginning this frame
        }

        // 2-R2: while the tour wants the player off this storey (--fight-storeys-min: one kill made here, fewer storeys visited than the minimum, a next storey with a slot standing), the pick takes a target off it, the tour's next
        // storey first, and none in range means a walk; else the pick is the nearest target on any walkable cell.
        var leaving = !_options.ArenaHere && _walk?.Tour is GroundTour tour && tour.WantsToLeave(here.CellId, clock, out _pickNextStorey);
        _pickLeaving = leaving;
        _pickHereStorey = _ground.StoreyOf(here.CellId);
        for (var tier = 0; tier < (leaving ? 2 : 1) && !found; tier++)
        {
            var filter = leaving ? (tier == 0 ? _nextStoreyFilter : _otherStoreyFilter) : _huntFilter;
            for (var tries = 0; tries < MaxLinePicks; tries++)
            {
                found = _killFirstPick.Nearest(session.State.Objects, PickWcids(session, in here), in here, _range, filter, _pickExcluded, out guid, session.Live); // M6 0 a5: the character's band on an outdoor ground; M6 3 a2: a kill-first creature in reach first
                if (!found || _options.IgnoreLine || LineIsClear(session, in here, guid))
                {
                    break;
                }

                // TODO(m3-fight-los-approximation): the line is a sphere sweep through the collision, not the bolt's own transition. The pick is the loop's judgement: retail checks no line (CastSpell 0x00568DE0).
                NoteBlocked(guid, tries + 1, clock);
                _pickExcluded.Add(guid);
                found = false;
            }
        }

        if (!found)
        {
            if (HealDecide(session, clock))
            {
                NoteDrainStart(session, clock);
                return ScenarioStatus.Running; // M5 7 F7 (D88): health low and no target in reach: Heal Self
            }

            if (killGrace)
            {
                return ScenarioStatus.Running; // M5 7 F15 (7-F1): the only target in reach may be the one a kill notice just named; wait for the table, no walk
            }

            if (_doomedThisPick > 0)
            {
                return ScenarioStatus.Running; // M5 7 D124 (S1b): the targets in reach are about to die to projectiles in flight: hold until they land, no walk
            }

            if (_lootHold is null && _loot is not null && _loot.TryRetry(session, clock))
            {
                return ScenarioStatus.Running; // M5 7 loot retry: between pulls (no target in reach), the oldest put-off corpse that nothing is in the way of is looted now
            }

            if (_lootHold is null && StartWalkBack(session, clock))
            {
                return ScenarioStatus.Running; // M5 7 P7b (D139 j): a put-off corpse lost from view is walked back to before the tour walks on
            }

            if (PullStep(session, clock, in here, defence) is { } pulled)
            {
                return pulled; // M6 7 AX: the wanted trophy's pull, before the tour walks on (FightScenario.Pull.cs)
            }

            return StartWalk(session, clock, _pickExcluded.Count > _killed.Count ? NoClearLine : leaving ? TourLeaves : NoTargetInRange);
        }

        return BeginTarget(session, clock, in here, guid, defence, leaving);
    }

    /// <summary>A target is chosen (the pick's, self-defence's or the emergency's finishing bolt): it is selected, the mana order and the picker decide, and the bolt or the war option starts.</summary>
    private ScenarioStatus BeginTarget(IGameSessionView session, double clock, in CellFrame here, uint guid, DefenceMove defence, bool leaving)
    {
        if (!double.IsNaN(_idleSince))
        {
            _idleTotal += clock - _idleSince; // a target to pick again: the time with none ends
            _idleSince = double.NaN;
        }

        if (guid != _target)
        {
            var previous = _target;
            _target = guid;
            _tally.SetTarget(session.State.Objects.TryGet(guid, out var chosen) ? chosen.Name : null);
            var reason = defence == DefenceMove.Cast ? "self-defence: it attacked the player" : leaving ? "the tour leaves this storey: the next storey's nearest" : previous == 0 ? "the first pick: the nearest profile target in range with a clear line" : TargetGone(session.State.Objects, previous) || _killed.Contains(previous) ? "the last target is dead or gone: the nearest profile target in range with a clear line" : "the last target is out of the pick (out of range, no clear line, or not the nearest): the nearest profile target in range with a clear line";
            _log.Info(LogCategory, $"target 0x{guid:X8} ({chosen?.Name ?? "unknown"}), was 0x{previous:X8}: {reason}");
            SelectTarget(session, guid, clock); // M5 7 F20: selected, so ACE pushes its health on every change
            ElementFor(session, guid); // M6 14 the element line: the bolt line that hits this creature hardest from her book (FightScenario.Element.cs)
        }

        if (WayOutHoldsBolt(session, clock, in here, guid))
        {
            return ScenarioStatus.Running; // M6 7 BE: the mana is the way out's: a press in the bolt's place, or the autopilot leaves (FightScenario.WayOut.cs)
        }

        if (_finishGuid != 0)
        {
            // M6 7 Y: the finishing bolt's mana was read before it was chosen (FinishStep); no refill comes before it
        }
        else if (!_options.Mana.On)
        {
            if (ManaShort(session))
            {
                BeginManaWait(clock);
                _phase = Phase.Mana;
                return ScenarioStatus.Running;
            }
        }
        else if (BoltNeedsMana(session, clock))
        {
            return ScenarioStatus.Running;
        }

        if (PickerDecide(session, clock, in here, guid))
        {
            return ScenarioStatus.Running; // M5 7 F10 (D90): the one picker: D88's drain-health floor (F7), then the least all-in seconds of the bolt, F9's war options, the drain as an attack and the debuffs (F8's opener) (FightScenario.Picker.cs)
        }

        // M5 1 fix A, 1-R9: the mana wait closes at a bolt sent with the mana for it (at or over its ceiling: no 0x0401 is possible); a bolt sent from the try floor is a mana try that may be refused, so the wait stays open
        // until a try is accepted (CastAnswer), and a refused try opens it if it was not open.
        if (!session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var held, out _) || held >= _manaNeeded)
        {
            EndManaWait(clock);
        }

        if (!_warActive && _level >= 0 && ShotRefused(session, clock, guid, _levelIds[_level]))
        {
            return ScenarioStatus.Running; // M6 8 L: the shot is checked before it is paid for (FightScenario.ShotCheck.cs)
        }

        _triesSpent = false;

        _damageMark = _tally.DamageLinesDealt;
        _killMark = _tally.Kills;
        _targetKillMark = _tally.TargetKills;
        _resistMark = _tally.Resists;
        // M5 1 K3: the bolt goes through the cast policy as Automation (D13). BoltD14 is the owner's fizzle rule (D14): the same level twice more, then one level down, once per scenario (_fizzleDropped, kept across attempts), then a miss; the mana is checked again before each retry.
        _cast ??= new CastAttempt(session.Actions, session.State, _log, _spells);
        _usableBook = _options.ArenaHere ? null : session.State.Self.Spellbook;
        _phase = Phase.Cast;
        _castNoted = 0;
        var (castLevels, castIndex) = WarCastFor(); // M5 7 F9 (D89) through F10's picker: the war option it chose, else the bolt in hand
        _cast.Start(
            new CastRequest { Levels = castLevels, Index = castIndex, Target = _target, Rules = _boltRules, Purpose = "fight bolt", TargetGone = _targetGoneFn ??= TargetDeadOrGone, Usable = _usable, FizzlesSpent = _fizzlesSpent, LevelsDownSpent = _fizzleDropped ? 1 : 0, ManaTryFloor = _options.Mana.On ? _manaFloor : 0 },
            clock);
        NoteCasts();
        NoteManaLoopBolt(); // M6 8 H: the bolts between the mana casts
        return ScenarioStatus.Running;
    }

    /// <summary>
    /// One pick the line rule skipped: counted every time (<c>blocked_picks</c>), logged at most once per creature every <see cref="BlockedLogSeconds"/> with the count held back. r2b's hold on a projectile in flight wrote the same six lines every frame
    /// (78 lines in 0.2 s).
    /// </summary>
    private void NoteBlocked(uint guid, int pick, double clock)
    {
        _tally.NoteBlockedPick();
        var heldBack = 0;
        if (_blockedLogged.TryGetValue(guid, out var last))
        {
            if (clock - last.At < BlockedLogSeconds)
            {
                _blockedLogged[guid] = (last.At, last.HeldBack + 1);
                return;
            }

            heldBack = last.HeldBack;
        }

        if (_blockedLogged.Count >= BlockedLogCap)
        {
            _blockedLogged.Clear(); // the hours-long fight does not grow it; a creature forgotten is logged once more
        }

        _blockedLogged[guid] = (clock, 0);
        _log.Info(LogCategory, $"blocked: no clear line to 0x{guid:X8} (pick {pick}{(heldBack > 0 ? $"; {heldBack} more not logged" : string.Empty)})");
    }

    /// <summary>
    /// M6 7 Y (r2b's arrival): the tour's leg is dropped the moment the pick would find a target from where the mage stands. A leg begins because no target had a line; the walk read only attackers, so the mage ran the whole leg up to a tusker that
    /// may have come into line and range on the way, and the first thing to end the leg was its first hit (at 3.5 m, the leg 13.4 s old). The same pick as <c>Choose</c>: the band, the filter, the range and the line. Read every
    /// <see cref="WalkSightSeconds"/>, on a leg of the tour only (<c>_walkSight</c>): never a loot walk, a walk back, a leg that leaves the storey or a hold.
    /// </summary>
    private bool SightDropsWalk(IGameSessionView session, double clock)
    {
        if (!_walkSight || clock < _walkSightAt || session.State.Self.Position is not { } here)
        {
            return false;
        }

        _walkSightAt = clock + WalkSightSeconds;
        _pickExcluded.Clear();
        _pickExcluded.AddRange(_killed);
        ExcludeWallBlocked(session, clock); // M6 7 AM
        ExcludeDoomed(session, clock);
        for (var tries = 0; tries < MaxLinePicks; tries++)
        {
            if (!_killFirstPick.Nearest(session.State.Objects, PickWcids(session, in here), in here, _range, _huntFilter, _pickExcluded, out var guid, session.Live))
            {
                return false;
            }

            if (_options.IgnoreLine || LineIsClear(session, in here, guid))
            {
                _walk?.Abandon();
                _log.Info(LogCategory, $"sight: the walk is dropped; 0x{guid:X8} is in range with a clear line");
                _phase = Phase.Choose;
                return true;
            }

            _pickExcluded.Add(guid);
        }

        return false;
    }

    /// <summary>True when the walker finds the line from the player to creature <paramref name="guid"/> clear (true when there is no walker or a position is unknown).</summary>
    private bool LineIsClear(IGameSessionView session, in CellFrame here, uint guid) =>
        _walk is null || !session.State.Objects.TryGet(guid, out var o) || StandsAt(session, o) is not { } at || _walk.HasClearLine(in here, in at);

    /// <summary>M5 7 F27: true when the walker finds the line from <paramref name="here"/> to <paramref name="at"/> clear (the fight's shot line test; true when there is no walker or <c>--fight-ignore-line</c> is on).</summary>
    private bool LineIsClearTo(in CellFrame here, in CellFrame at) => _options.IgnoreLine || _walk is null || _walk.HasClearLine(in here, in at);

    /// <summary>
    /// M5 7 F27: the nearest living target creature of the ground (not the kill's own, not a killed one) within <paramref name="reachMetres"/> of <paramref name="corpse"/>, or 0 (<paramref name="distance"/> is its distance).
    /// </summary>
    private uint HostileBeside(IGameSessionView session, GlobalPosition corpse, double reachMetres, uint except, out double distance)
    {
        (var best, distance) = (0u, double.MaxValue);
        if (reachMetres <= 0)
        {
            return 0;
        }

        foreach (var o in session.State.Objects.All)
        {
            if (o.Guid == except || StandsAt(session, o) is not { } p || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || _killed.Contains(o.Guid) || !IsTargetWcid(o.Weenie.WeenieClassId))
            {
                continue;
            }

            var d = p.ToGlobal().DistanceTo(corpse);
            if (d <= reachMetres && d < distance)
            {
                (best, distance) = (o.Guid, d);
            }
        }

        return best;
    }

    /// <summary>M6 7 AN: a creature of this ground: a target of the list, or on an outdoor ground any creature its generators spawn, in band or not (the attacker out of band is drained and watched for as well).</summary>
    private bool IsFightCreature(uint wcid)
    {
        if (IsTargetWcid(wcid))
        {
            return true;
        }

        if (_ground.Outdoor is { } outdoor)
        {
            for (var i = 0; i < outdoor.Creatures.Count; i++)
            {
                if (outdoor.Creatures[i].Wcid == wcid)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsTargetWcid(uint wcid)
    {
        for (var i = 0; i < _targetWcids.Count; i++)
        {
            if (_targetWcids[i] == wcid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True (and logs once) while the read model's mana is under what the cast can cost.</summary>
    private bool ManaShort(IGameSessionView session)
    {
        if (!session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _) || mana >= _manaNeeded)
        {
            _manaLogged = false;
            return false;
        }

        if (!_manaLogged)
        {
            _manaLogged = true;
            _log.Info(LogCategory, $"waiting for mana: the cast needs up to {_manaNeeded}, the player has {mana}");
        }

        return true;
    }

    private const string NoTargetInRange = "no creature of the ground is in range on the floor"; // M6 14 motes: the ground's creatures, not only the den's tuskers
    private const string NoClearLine = "no creature of the ground has a clear line";
    private const string SelfDefenceNoLine = "self-defence: no attacker is in range with a clear line";
    private const string TourLeaves = "the tour leaves this storey (--fight-storeys-min) and no creature of the ground off it is in range";

    /// <summary>
    /// The mana order before a bolt (M5 1 F1): the pre-emptive transfer when <see cref="ManaOrder.PreemptTransfer"/> holds (D27; M6 7 AD: with no Mana Conversion, <see cref="KeepOnePress"/> in its place), else true (the mana phase entered) while the mana is under the try floor, or under the ceiling
    /// once the tries are spent. Allocation-free.
    /// </summary>
    private bool BoltNeedsMana(IGameSessionView session, double clock)
    {
        var q = session.State.Self.Qualities;
        if (!q.TryGetVitalLevels(MageTemplate.MaxMana, out var m, out var mMax) || !q.TryGetVitalLevels(MageTemplate.MaxStamina, out var s, out var sMax))
        {
            return false; // vitals unknown: the bolt is sent and the server judges, as the ceiling wait did
        }

        var drainFirst = DrainFirst(session, clock); // M5 7 F15 (7-F3): under the drain line the drain comes before the stamina refill and the early transfer
        _policyDecided = false; // M6 14 (m6-vitals-policy): set by the policy's step below when it decided
        if (!drainFirst && ManaLoopStep(session, clock, m, mMax, s, sMax))
        {
            return true; // M6 7 AN: the fight's mana loop (FightScenario.ManaLoop.cs)
        }

        if (!drainFirst && !_policyDecided && _danceCount > 0 && clock >= _danceAsideUntil && _manaConversion == 0 && KeepOnePress(session, clock, m, mMax, s, sMax))
        {
            return true; // M6 7 AD: with no Mana Conversion a press costs what a bolt costs: the press before the bolt that would leave none
        }

        if (!drainFirst && !_policyDecided && !_manaLoopHoldsPress && _danceCount > 0 && clock >= _danceAsideUntil && _manaConversion > 0 && ManaOrder.PreemptTransfer(s, sMax, m, mMax)) // M6 7 AD: D27's constants are level 80's (100 missing against a landing of 200): with no Mana Conversion the press is KeepOnePress's or the rest's; M6 14: not over the climber's loop holding a press that would overflow, nor behind the policy's decision (m6-vitals-policy)
        {
            var index = CharacterPolicy.For(session.State.Self.Name).ManaBySum // the owner's ruling, 2026-11-08: pick by the sum
                ? ManaOrder.PressLevelOf(_danceLevels.AsSpan(0, _danceCount), s, m, _manaConversion, _options.Mana.TryChance, out _)
                : ManaOrder.DanceLevelOf(_danceLevels.AsSpan(0, _danceCount), m, _manaConversion, _options.Mana.TryChance);
            if (index >= 0)
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: the pre-emptive transfer (D27): stamina {s} of {sMax} (at least {ManaOrder.PreemptStaminaShare:0.##} of it) and {mMax - m} mana missing (at least {ManaOrder.PreemptMinMissingMana}): the dance before the bolt"));
                _preempting = true;
                _phase = Phase.Mana;
                StartDance(session, index, m, mMax, s, sMax, clock);
                return true;
            }
        }

        var boltMana = _triesSpent ? _manaNeeded : _manaFloor;
        if (m >= boltMana)
        {
            // M5 7 F5 (D88): between kills, the stamina under the share and the mana paying a Revitalize Self with the bolt still in hand: the stamina first (the order decides the level; no mana wait: the bolt is not short of mana).
            if (!drainFirst && _refill.PercentAt(clock) > 0 && ManaOrder.StaminaUnder(s, sMax, _refill.Percent) && ManaOrder.RevitalizeLevelOf(_refill.Levels, m, _wayOut is { } wayOut ? wayOut.Line + _manaNeeded : boltMana == 0 ? _manaNeeded : boltMana) >= 0) // the reserve is the order's target (ManaOrder.Decide), so the hook and the decision agree; M6 7 BE: and it keeps the way out's line
            {
                _manaPhase = ManaPhase.Decide;
                _phase = Phase.Mana;
                return true;
            }

            return false;
        }

        BeginManaWait(clock);
        _manaPhase = ManaPhase.Decide;
        _phase = Phase.Mana;
        return true;
    }

    /// <summary>
    /// M6 7 AD: the press before the bolt (<see cref="ManaOrder.KeepPress"/>): the bolt can be paid, the press after it could not, and the press nets at least half a bolt. Named on the log, and spent like the pre-emptive transfer: back to the pick after it. True when the press was sent.
    /// </summary>
    private bool KeepOnePress(IGameSessionView session, double clock, uint mana, uint manaMax, uint stamina, uint staminaMax)
    {
        var boltMana = _triesSpent ? _manaNeeded : _manaFloor;
        if (mana < boltMana)
        {
            return false; // the order's: ManaOrder.Decide presses when it can
        }

        var levels = _danceLevels.AsSpan(0, _danceCount);
        var index = ManaOrder.BestNetLevel(levels, stamina, mana, _manaConversion, _options.Mana.TryChance, out var net);
        if (index < 0 || !ManaOrder.KeepPress(mana, boltMana, in levels[index], net))
        {
            return false;
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: a press before the bolt: mana {mana} of {manaMax} pays the bolt's {boltMana} but not a press after it ({levels[index].BaseMana}); stamina {stamina} of {staminaMax} nets {net:0.#}"));
        _preempting = true;
        _phase = Phase.Mana;
        StartDance(session, index, mana, manaMax, stamina, staminaMax, clock);
        return true;
    }

    /// <summary>
    /// A mana wait begins, unless one is open: at the first bolt not sent for mana, or at the first try of a bolt refused for mana (0x0401), whichever comes first. The pre-emptive transfer (D27, <see cref="BoltNeedsMana"/>)
    /// opens none: it spends stamina before the mana is short, and the bolt waits for nothing (M5 1 fix A, 1-R9).
    /// </summary>
    private void BeginManaWait(double clock)
    {
        if (double.IsNaN(_manaWaitSince))
        {
            _manaWaitSince = clock;
        }
    }

    /// <summary>The bolt lands (or is sent with the mana for it): the open mana wait ends and counts.</summary>
    private void EndManaWait(double clock)
    {
        if (double.IsNaN(_manaWaitSince))
        {
            return;
        }

        var wait = clock - _manaWaitSince;
        _manaWaitTotal += wait;
        _manaWaitMax = Math.Max(_manaWaitMax, wait);
        _manaWaitSince = double.NaN;
        _manaLogged = false;
    }

    /// <summary>The mana phase under the order (M5 1 F1): one step of <see cref="ManaOrder.Decide"/>'s choice. Allocation-free while it waits or rests.</summary>
    private ScenarioStatus ManaOrderStep(IGameSessionView session, double clock)
    {
        var q = session.State.Self.Qualities;
        switch (_manaPhase)
        {
            case ManaPhase.Decide:
                if (EmergencyWanted(session, clock, out _, out _) && EmergencyLevelIndex() >= 0)
                {
                    _phase = Phase.Choose; // M5 7 F16 (D91): the emergency drain outranks the stamina order; no cast is in flight at a decision
                    return ScenarioStatus.Running;
                }

                return ManaDecide(session, clock);

            case ManaPhase.Dancing:
                return DanceStep(session, clock);

            case ManaPhase.Revitalizing:
                return RevitalizeStep(session, clock);

            case ManaPhase.StaminaDrain:
                return StaminaDrainStep(session, clock);

            case ManaPhase.Drinking:
                if (_manaPending is not { Outcome: { } drunk })
                {
                    return ScenarioStatus.Running;
                }

                _manaPending = null;
                if (!drunk.Succeeded && drunk.Code == PullDrinkBusyCode && _manaDrinkBusy < PullDrinkBusyTries)
                {
                    // M6 8: as the drink before a pull (live 2026-11-11 10:05 UTC): ACE answers a use sent while the last motion still runs with YoureTooBusy. Busy is a wait, not a failure of the potions.
                    _manaDrinkBusy++;
                    _manaDrinkAt = clock;
                    _manaPhase = ManaPhase.DrinkBusy;
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: the server was too busy for the {(_drinkStamina ? "Stamina" : "Mana")} Potion (try {_manaDrinkBusy} of {PullDrinkBusyTries}); again in {PullDrinkBusySeconds:0.#} s"));
                    return ScenarioStatus.Running;
                }

                _manaDrinkBusy = 0;
                if (!drunk.Succeeded)
                {
                    if (_drinkStamina)
                    {
                        _staminaPotionsAside = true;
                    }
                    else
                    {
                        _potionsAside = true;
                    }

                    _log.Warn(LogCategory, $"mana: the {(_drinkStamina ? "Stamina" : "Mana")} Potion was not drunk (" + Describe(drunk) + $"); {(_drinkStamina ? "Stamina" : "Mana")} Potions are set aside for the fight");
                    _manaPhase = ManaPhase.Decide;
                    return ScenarioStatus.Running;
                }

                if (_drinkStamina)
                {
                    _refill.PotionsDrunk++;
                }
                else
                {
                    _potionsDrunk++;
                }

                _phaseSince = clock;
                _manaPhase = ManaPhase.PotionVital;
                return ScenarioStatus.Running;

            case ManaPhase.DrinkBusy:
                if (clock - _manaDrinkAt < PullDrinkBusySeconds)
                {
                    return ScenarioStatus.Running;
                }

                uint again = 0;
                if (_drinkStamina ? VitalsMeans.FindStaminaPotions(session.State, _scratch, out again, out _) : VitalsMeans.FindManaPotions(session.State, _scratch, out again, out _))
                {
                    _manaPending = session.Actions.Drink(again, ActionSource.Automation);
                    _manaPhase = ManaPhase.Drinking;
                    return ScenarioStatus.Running;
                }

                _manaPhase = ManaPhase.Decide; // none is held any more: the order decides again
                return ScenarioStatus.Running;

            case ManaPhase.PotionVital:
                // As the vitals keeper: ACE sends the vital's update 0.74 s into the 1.48 s use; no change within the wait is a lost message, named, and the potions are set aside.
                q.TryGetVitalLevels(_drinkStamina ? MageTemplate.MaxStamina : MageTemplate.MaxMana, out var potionVital, out _);
                var vitalName = _drinkStamina ? "stamina" : "mana";
                if (potionVital != (_drinkStamina ? _seenStamina : _seenMana))
                {
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: the {(_drinkStamina ? "Stamina" : "Mana")} Potion brought the {vitalName} from {(_drinkStamina ? _seenStamina : _seenMana)} to {potionVital}"));
                    _manaPhase = ManaPhase.Decide;
                }
                else if (clock - _phaseSince > VitalsKeeper.PotionVitalWaitSeconds)
                {
                    if (_drinkStamina)
                    {
                        _staminaPotionsAside = true;
                    }
                    else
                    {
                        _potionsAside = true;
                    }

                    _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: the {vitalName} did not change within {VitalsKeeper.PotionVitalWaitSeconds:0} s of a confirmed {(_drinkStamina ? "Stamina" : "Mana")} Potion (still {potionVital}); {(_drinkStamina ? "Stamina" : "Mana")} Potions are set aside for the fight"));
                    _manaPhase = ManaPhase.Decide;
                }

                return ScenarioStatus.Running;

            case ManaPhase.ToRest:
                if (_manaPending is not { Outcome: { } peace })
                {
                    return ScenarioStatus.Running;
                }

                _manaPending = null;
                if (!peace.Succeeded)
                {
                    _log.Warn(LogCategory, "mana: the non-combat mode for the rest was not entered (" + Describe(peace) + "); the order waits in the magic mode instead");
                    _manaPhase = ManaPhase.Waiting;
                    return ScenarioStatus.Running;
                }

                _phaseSince = clock;
                _manaPhase = ManaPhase.Resting;
                return ScenarioStatus.Running;

            case ManaPhase.Resting:
                return RestStep(session, clock);

            case ManaPhase.ToMagic:
                if (_manaPending is null)
                {
                    if (q.CombatMode == CombatModes.Magic)
                    {
                        _manaPhase = ManaPhase.Decide;
                        return ScenarioStatus.Running;
                    }

                    _manaPending = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
                    return ScenarioStatus.Running;
                }

                if (_manaPending.Outcome is not { } magic)
                {
                    return ScenarioStatus.Running;
                }

                _manaPending = null;
                if (!magic.Succeeded)
                {
                    return Fail(session, "fight: the magic combat mode was not entered after the rest for stamina: " + Describe(magic));
                }

                _manaPhase = ManaPhase.Decide;
                return ScenarioStatus.Running;

            default:
                // The wait for regeneration: back to the pick at the decision's target; the order decides again when a vital moves.
                if (!q.TryGetVitalLevels(MageTemplate.MaxMana, out var m, out _) || !q.TryGetVitalLevels(MageTemplate.MaxStamina, out var s, out _))
                {
                    return ScenarioStatus.Running;
                }

                if (m >= _decision.Target)
                {
                    _manaPhase = ManaPhase.Decide;
                    _phase = Phase.Choose;
                }
                else if (m != _seenMana || s != _seenStamina)
                {
                    _manaPhase = ManaPhase.Decide;
                }
                else if (_restAsideOpen && clock >= _restAsideUntil)
                {
                    _restAsideOpen = false;
                    _manaPhase = ManaPhase.Decide; // the aside is over: the order may rest again
                }

                return ScenarioStatus.Running;
        }
    }

    /// <summary>The order's decision (<see cref="ManaOrder.Decide"/>, the mage's profile) and its first action.</summary>
    private ScenarioStatus ManaDecide(IGameSessionView session, double clock)
    {
        var q = session.State.Self.Qualities;
        if (!q.TryGetVitalLevels(MageTemplate.MaxMana, out var m, out var mMax) || !q.TryGetVitalLevels(MageTemplate.MaxStamina, out var s, out var sMax))
        {
            _phase = Phase.Choose;
            return ScenarioStatus.Running;
        }

        uint potion = 0;
        var potions = 0;
        if (!_potionsAside && VitalsMeans.FindManaPotions(session.State, _scratch, out potion, out var held))
        {
            potions = held;
        }

        var refillPercent = DrainFirst(session, clock) ? 0 : _refill.PercentAt(clock); // M5 7 F15 (7-F3): no refill branch while the drain rule is on and a target is in reach
        uint staminaPotion = 0;
        var staminaPotions = 0;
        if (refillPercent > 0 && !_staminaPotionsAside && VitalsMeans.FindStaminaPotions(session.State, _scratch, out staminaPotion, out var staminaHeld))
        {
            staminaPotions = staminaHeld;
        }

        var input = new ManaOrderInput
        {
            Mana = m,
            ManaMax = mMax,
            Stamina = s,
            StaminaMax = sMax,
            ManaConversion = _manaConversion,
            Need = new ManaNeed(_manaNeeded, _manaFloor),
            TriesSpent = _triesSpent,
            ManaPotions = potions,
            Profile = ManaProfile.Mage,
            StaminaFloor = _staminaFloor,
            StaminaFullPercent = refillPercent,
            StaminaPotions = staminaPotions,
            BestNetPress = _manaConversion == 0 || CharacterPolicy.For(session.State.Self.Name).ManaBySum, // the owner's ruling, 2026-11-08: pick by the sum: the ladder's character at any Mana Conversion
        };
        var decision = ManaOrder.Decide(in input, _danceLevels.AsSpan(0, clock < _danceAsideUntil ? 0 : _danceCount), _options.Mana, _refill.Levels);
        _decision = decision;
        (_seenMana, _seenStamina) = (m, s);
        switch (decision.Step)
        {
            case ManaStep.Ready:
            case ManaStep.TryReal:
            case ManaStep.CeilingOrder:
                _refill.Reset(); // the refill's run of casts ends with the order handing back to the bolt
                _phase = Phase.Choose;
                return ScenarioStatus.Running;

            case ManaStep.Dance:
                _preempting = false;
                StartDance(session, decision.DanceIndex, m, mMax, s, sMax, clock);
                return ScenarioStatus.Running;

            case ManaStep.Revitalize:
                if (TryStaminaDrain(session, clock, decision.DanceIndex, s, sMax))
                {
                    return ScenarioStatus.Running; // M5 7 F10: a target in reach and the drain refills faster than Revitalize Self
                }

                StartRevitalize(session, decision.DanceIndex, m, mMax, s, sMax, clock);
                return ScenarioStatus.Running;

            case ManaStep.StaminaPotion:
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: Stamina Potion 0x{staminaPotion:X8} ({staminaPotions} held), stamina {s} of {sMax}, mana {m} of {mMax} ({decision.Reason})"));
                _drinkStamina = true;
                _manaPending = session.Actions.Drink(staminaPotion, ActionSource.Automation);
                _manaPhase = ManaPhase.Drinking;
                return ScenarioStatus.Running;

            case ManaStep.Potion:
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: Mana Potion 0x{potion:X8} ({potions} held), mana {m} of {mMax}, stamina {s} of {sMax}, {decision.Target} needed ({decision.Reason})"));
                _drinkStamina = false;
                _manaPending = session.Actions.Drink(potion, ActionSource.Automation);
                _manaPhase = ManaPhase.Drinking;
                return ScenarioStatus.Running;

            case ManaStep.RestStamina when clock < _restAsideUntil:
                // M5 1 fix A, 1-R3: a health fall ended the last rest a moment ago; wait in the magic mode (a bolt or a dance as the mana allows) instead of flipping modes beside whatever is hitting.
                if (!_restAsideLogged)
                {
                    _restAsideLogged = true;
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: the rest for stamina is set aside for {_restAsideUntil - clock:0.#} more s (a health fall ended the last one: something is hitting); waiting in the magic mode, stamina {s} of {sMax}, mana {m} of {mMax}, {decision.Target} needed"));
                }

                _manaPhase = ManaPhase.Waiting;
                return ScenarioStatus.Running;

            case ManaStep.RestStamina:
                _rests++;
                _restHealth = q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _) ? health : 0;
                _phaseSince = clock;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: a rest in peace for stamina: stamina {s} of {sMax} to {decision.StaminaTarget} (at most {RestCapSeconds:0} s), mana {m} of {mMax}, {decision.Target} needed ({decision.Reason}); NonCombat now (ACE halves stamina regeneration in a combat mode, Creature_Vitals.cs GetStanceMod)"));
                if (q.CombatMode == CombatModes.NonCombat)
                {
                    _manaPhase = ManaPhase.Resting;
                }
                else
                {
                    _manaPending = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Automation);
                    _manaPhase = ManaPhase.ToRest;
                }

                return ScenarioStatus.Running;

            default:
                if (!_manaLogged)
                {
                    _manaLogged = true;
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"waiting for mana: the bolt needs {decision.Target} (up to {_manaNeeded}), the player has {m}; {decision.Reason}"));
                }

                _manaPhase = ManaPhase.Waiting;
                return ScenarioStatus.Running;
        }
    }

    /// <summary>Sends the dance (D27): the level <paramref name="index"/> of the dance levels pressed until it lands, at most <see cref="ManaOrderOptions.DancePresses"/> presses, every press counted.</summary>
    private void StartDance(IGameSessionView session, int index, uint mana, uint manaMax, uint stamina, uint staminaMax, double clock)
    {
        var level = _danceLevels[index];
        _manaCast ??= new CastAttempt(session.Actions, session.State, _log, _spells);
        _dances++;
        _manaPhase = ManaPhase.Dancing;
        NoteDanceStart(mana, clock); // M5 7 F10: the dance's measured seconds a mana point
        _log.Info(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"mana: the dance: {(_spells.TryGetValue(level.SpellId, out var info) ? info.Name : "spell")} ({level.SpellId}) pressed until it lands ({(_manaConversion == 0 ? _pressRules : _danceRules)!.Name}), mana {mana} of {manaMax}, stamina {stamina} of {staminaMax}, net {ManaOrder.NetYield(in level, stamina, _manaConversion, mana):0.#}{(_preempting ? " (pre-emptive)" : string.Empty)}"));
        // M5 1 fix A: the press carries the chat proof, so a landed Stamina to Mana Self ends Landed (proved by ACE's own line, "You cast <spell> on yourself and lose N points of stamina and also gain M points of mana", WorldObject_Magic.cs 835
        // @95ef426d, chat type Magic) and counts in mana_landed= and proved= as well as dances_landed=. The pre-emptive transfer starts here too.
        _danceProof ??= new CastChatProof(session.State, _spells);
        _manaCast.Start(new CastRequest { Levels = VitalsMeans.StaminaToManaSelf, Index = _danceIndex[index], Rules = (_manaConversion == 0 ? _pressRules : _danceRules)!, Purpose = "fight mana dance", Proof = _danceProof }, clock); // M6 7 AD: with no Mana Conversion every press costs the base, and one refused says the mana is short: no 40 presses
    }

    /// <summary>
    /// Sends the refill (M5 7 F5, D88): Revitalize Self at the level <paramref name="index"/> of the refill's levels, the stamina under <c>--fight-stamina-full</c>. One line each:
    /// <c>mana: revitalize: &lt;spell&gt; at stamina x of y, mana m of n</c>.
    /// </summary>
    private void StartRevitalize(IGameSessionView session, int index, uint mana, uint manaMax, uint stamina, uint staminaMax, double clock)
    {
        var level = _refill.Levels[index];
        _manaPhase = ManaPhase.Revitalizing;
        NoteRefill(clock, ended: false);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: revitalize: {(_spells.TryGetValue(level.SpellId, out var info) ? info.Name : "spell")} at stamina {stamina} of {staminaMax}, mana {mana} of {manaMax}"));
        _refill.Start(session, index, clock);
    }

    /// <summary>The refill's cast ends: counted in the line's tally, the scarabs it used read again, and the order decides again (more Revitalize Self until the stamina is back at the share, or the bolt).</summary>
    private ScenarioStatus RevitalizeStep(IGameSessionView session, double clock)
    {
        var cast = _refill.Cast!;
        var end = cast.Step(clock);
        if (end == CastEnd.Running)
        {
            return ScenarioStatus.Running;
        }

        var report = cast.Report;
        _castTally.Add(in report);
        if (end is CastEnd.Cancelled or CastEnd.DryRun)
        {
            return Fail(session, "fight: the Revitalize Self cast was not carried out: " + (cast.LastOutcome is { } stopped ? Describe(stopped) : end + ": " + report.Why));
        }

        var q = session.State.Self.Qualities;
        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax);
        _refill.Ended(end, stamina, staminaMax, report.Why ?? string.Empty, clock);
        NoteRefill(clock, ended: true);
        ReadRefill(session);
        _manaPhase = ManaPhase.Decide;
        return ScenarioStatus.Running;
    }

    /// <summary>The dance's end: the presses counted, a landing counted; a dance that spent every press is named and the dance set aside <see cref="DanceAsideSeconds"/>.</summary>
    private ScenarioStatus DanceStep(IGameSessionView session, double clock)
    {
        var end = _manaCast!.Step(clock);
        if (end == CastEnd.Running)
        {
            return ScenarioStatus.Running;
        }

        var report = _manaCast.Report;
        _castTally.Add(in report);
        _manaPresses += report.Tries;
        if (end is CastEnd.Cancelled or CastEnd.DryRun)
        {
            return Fail(session, "fight: the dance was not carried out: " + (_manaCast.LastOutcome is { } stopped ? Describe(stopped) : end + ": " + report.Why));
        }

        session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _);
        if (end.Succeeded())
        {
            _dancesLanded++;
            NoteDanceLanded(mana, clock);
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: the dance landed on press {report.Tries} ({report.ManaRefusals} refused, {report.Fizzles} fizzled); mana {mana} now"));
        }
        else if (end == CastEnd.InTheAir)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: the dance was held in the air ({report.Why}); not set aside, the order decides again (M6 14 airborne)"));
        }
        else if (!_preempting && _staminaFloor > 0 && report.ManaRefusals + report.Fizzles > 0 && session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out var staminaLeft, out _) && staminaLeft >= _staminaFloor)
        {
            // M5 7 F5 (D88): uptime is the goal, so a dance whose presses were all refused or fizzled is pressed again at once while the stamina is there to transfer (no cap that gives up early; a refused press charges nothing).
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: the dance did not land in {report.Tries} presses ({report.ManaRefusals} refused, {report.Fizzles} fizzled; {end.Name()}: {report.Why}); the stamina {staminaLeft} is over its floor {_staminaFloor}, so the presses go on"));
        }
        else
        {
            _danceAsideUntil = clock + DanceAsideSeconds;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: the dance did not land in {report.Tries} presses ({report.ManaRefusals} refused, {report.Fizzles} fizzled; {end.Name()}: {report.Why}); the dance is set aside {DanceAsideSeconds:0} s (the server's costs are not the model's): the order's potion, rest or wait instead"));
        }

        NoteManaLoopAfter(session, "press", end.Succeeded()); // M6 8 H: the check after the act: what it really moved
        ReadRefill(session); // M5 7 F5: the scarabs the press used are gone from the pack: the payable levels read again
        _manaPhase = ManaPhase.Decide;
        if (_preempting)
        {
            _preempting = false;
            _phase = Phase.Choose;
        }

        return ScenarioStatus.Running;
    }

    /// <summary>The rest in peace for stamina: to the decision's stamina target or <see cref="RestCapSeconds"/>; a health fall ends it early, named; then the magic mode again.</summary>
    private ScenarioStatus RestStep(IGameSessionView session, double clock)
    {
        var q = session.State.Self.Qualities;
        if (q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _) && health < _restHealth)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: the rest for stamina ends early after {clock - _phaseSince:0.#} s: the health fell from {_restHealth} to {health}; the magic mode again, and no rest for {RestAsideSeconds:0} s (the order waits in the magic mode, casting what the mana allows)"));
            _restAsideUntil = clock + RestAsideSeconds;
            _restAsideOpen = true;
            _restAsideLogged = false;
            _manaPhase = ManaPhase.ToMagic;
            return ScenarioStatus.Running;
        }

        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var s, out var sMax);
        if (s >= _decision.StaminaTarget)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: rested {clock - _phaseSince:0.#} s to stamina {s} of {sMax}; the magic mode again, then the dance"));
            _manaPhase = ManaPhase.ToMagic;
        }
        else if (clock - _phaseSince >= RestCapSeconds)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: the rest for stamina reached its cap of {RestCapSeconds:0} s at stamina {s} of {sMax} (target {_decision.StaminaTarget}); the magic mode again and the order decides again"));
            _manaPhase = ManaPhase.ToMagic;
        }

        return ScenarioStatus.Running;
    }

    /// <summary>The Mana Conversion, the dance levels and the bolt's try floor, read again (at the fight's start and when the level's signature changes: it reads enchantments, so it may allocate).</summary>
    private void ReadMana(PlayerQualities q, Spellbook? book)
    {
        if (!_options.Mana.On)
        {
            _manaFloor = _manaNeeded;
            _danceCount = 0;
            return;
        }

        ManaOrder.TryManaConversion(q, out _manaConversion);
        var held = _options.ArenaHere || _view is null ? null : LevelFloor.HeldForAccount(_view.State, DatReaderWriter.Enums.MagicSchool.LifeMagic, _buffOptions.AccountName); // M6 1 h1: the account's formula for a school without its focus; M5 7 F5 (D88): the highest level the pack's scarabs allow, for the transfer and the refill
        _danceCount = _options.ArenaHere ? 0 : FillDanceLevels(q, book, held, LevelCapFor(q)); // the arena-here run's stand-in teaches no transfer line
        if (!_options.ArenaHere)
        {
            _refill.Fill(q, book, held, _buffOptions.CastChance, LevelCapFor(q)); // M5 7 F15 (7-F6): the dance's self spells respect the plan cap like the attacks (D26)
        }

        ComputeFloors(q);
        _manaFloor = _level >= 0 && _spells.TryGetValue(_levelIds[_level], out var info) ? FloorFor(info) : _manaNeeded;
    }

    /// <summary>
    /// M5 7 F5, the owner's D88: the mana floor leaves at least one Stamina to Mana cast and one Revitalize Self affordable (<see cref="ManaOrder.ManaFloorOf"/> of the highest levels the pack pays for, added to the bolt's own try
    /// floor), and the stamina floor is the least stamina at which a landing gives that mana back (<see cref="ManaOrder.StaminaFloorOf"/>). Zero with the refill off or no level of either line. Named in the log when it changes.
    /// </summary>
    private void ComputeFloors(PlayerQualities q)
    {
        var (reserve, stamina) = (0u, 0u);
        if (_refill.Percent > 0 && _danceCount > 0 && _refill.Count > 0 && q.TryGetVitalLevels(MageTemplate.MaxStamina, out _, out var staminaMax))
        {
            var transfer = _danceLevels[_danceCount - 1];
            var revitalize = _refill.Levels[_refill.Count - 1];
            reserve = ManaOrder.ManaFloorOf(transfer.BaseMana, transfer.Power, revitalize.BaseMana, revitalize.Power, _manaConversion, _options.Mana.TryChance);
            stamina = ManaOrder.StaminaFloorOf(transfer.Row, staminaMax, transfer.BaseMana, revitalize.BaseMana);
            if ((reserve, stamina) != (_manaReserve, _staminaFloor))
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: D88 floors: {reserve} mana kept for one Stamina to Mana press ({transfer.BaseMana} base) and one Revitalize Self press ({revitalize.BaseMana} base) at Mana Conversion {_manaConversion}, on top of the bolt's try floor; stamina floor {stamina} of {staminaMax} (a landing gives at least {transfer.BaseMana + revitalize.BaseMana})"));
            }
        }

        (_manaReserve, _staminaFloor) = (reserve, stamina);
    }

    /// <summary>The payable dance and refill levels read again after a cast that used scarabs (allocates: it reads the pack).</summary>
    private void ReadRefill(IGameSessionView session)
    {
        if (!_options.Mana.On || _options.ArenaHere)
        {
            return;
        }

        var self = session.State.Self;
        var held = LevelFloor.HeldForAccount(session.State, DatReaderWriter.Enums.MagicSchool.LifeMagic, _buffOptions.AccountName); // M6 1 h1
        var cap = LevelCapFor(self.Qualities);
        _danceCount = FillDanceLevels(self.Qualities, self.Spellbook, held, cap);
        _refill.Fill(self.Qualities, self.Spellbook, held, _buffOptions.CastChance, cap);
        ComputeFloors(self.Qualities);
        if (_level >= 0 && _spells.TryGetValue(_levelIds[_level], out var bolt))
        {
            _manaFloor = FloorFor(bolt);
        }
    }

    /// <summary>The try floor of a bolt level (<see cref="ManaOrder.FloorOf"/>; the ceiling with <c>--cast-mana-tries 0</c>).</summary>
    private uint FloorFor(SpellInfo info)
    {
        var ceiling = GearCasts.ManaCeiling(info, 0);
        return _options.Mana.On ? Math.Min(ceiling, ManaOrder.FloorOf(ceiling, info.Power, ceiling, _manaConversion, _options.Mana, (info.Flags & SpellFlags.IgnoresManaConversion) != 0) + _manaReserve) : ceiling; // D88: the reserve for one transfer press and one Revitalize press on top
    }

    /// <summary>The Stamina to Mana Self levels the book holds at the cast chance under the plan cap <paramref name="maxLevel"/>, lowest first, into <c>_danceLevels</c> (their index in <see cref="VitalsMeans.StaminaToManaSelf"/> in <c>_danceIndex</c>); their count.</summary>
    private int FillDanceLevels(PlayerQualities q, Spellbook? book, IReadOnlyDictionary<uint, int>? held, int maxLevel)
    {
        var ids = VitalsMeans.StaminaToManaSelf;
        var skillRead = false;
        var skill = 0u;
        var count = 0;
        for (var i = 0; i < ids.Count && i < maxLevel && count < _danceLevels.Length; i++) // M5 7 F15 (7-F6): level i + 1 is offered only under the plan cap (D26: VI at level 80, whatever the pack's scarabs would pay)
        {
            if ((book is not null && !book.Knows(ids[i])) || !_spells.TryGetValue(ids[i], out var info) || DanceRows[i] is not { } row)
            {
                continue;
            }

            if (!skillRead)
            {
                if (!GearCasts.TryCurrentSkill(q, GearCasts.SkillOf(info.School), out skill, out _))
                {
                    return 0;
                }

                skillRead = true;
            }

            if (GearCasts.Chance(skill, info.Power) < _buffOptions.CastChance || !_refill.Payable(held, info, VitalsMeans.NameOf(VitalsLine.StaminaToManaSelf), i))
            {
                continue;
            }

            _danceIndex[count] = i;
            _danceLevels[count++] = new DanceLevel(info.Id, GearCasts.ManaCeiling(info, 0), info.Power, row);
        }

        return count;
    }

    /// <summary>True when the enchantments (the registry's array, replaced on every change), the character's level, War Magic's ranks or Focus or Self changed since the level was chosen. Allocation-free.</summary>
    private bool SignatureChanged(PlayerQualities q)
    {
        if (!_signatureSet)
        {
            return false;
        }

        q.TryGetSkill(MageTemplate.WarMagic, out var war);
        q.TryGetAttribute(MageTemplate.Focus, out var focus);
        q.TryGetAttribute(MageTemplate.Self, out var self);
        return q.ActiveEnchantments != _sigEnchantments || q.Level != _sigLevel || war.Ranks != _sigWarRanks || focus.Ranks != _sigFocus || self.Ranks != _sigSelf;
    }

    private void NoteSignature(PlayerQualities q)
    {
        q.TryGetSkill(MageTemplate.WarMagic, out var war);
        q.TryGetAttribute(MageTemplate.Focus, out var focus);
        q.TryGetAttribute(MageTemplate.Self, out var self);
        (_sigEnchantments, _sigLevel, _sigWarRanks, _sigFocus, _sigSelf) = (q.ActiveEnchantments, q.Level, war.Ranks, focus.Ranks, self.Ranks);
        _signatureSet = true;
    }

    /// <summary>The level chosen again (D10's rule, capped, M5 1 F1) after the buffed War Magic or the character's level changed; logged once per change. The mana's reads follow.</summary>
    private void Rechoose(SelfState self)
    {
        var q = self.Qualities;
        NoteSignature(q);
        var level = ChooseOnLine(q, self.Spellbook, out var skill, out var why, LevelCapFor(q)); // M6 14 the element line: on the line in hand
        level = _view is { } view ? CastableRechosen(view.State, level) : level; // M6 7 K: never back up to a level the pack cannot pay for or a refusal dropped
        if (level < 0)
        {
            _log.Warn(LogCategory, $"the level could not be chosen again ({why}); {Level} stays");
        }
        else if (level != _level && _spells.TryGetValue(_levelIds[level], out var info))
        {
            var from = Level;
            if (q.TryGetSkill(MageTemplate.WarMagic, out var war))
            {
                _initPlusRanks = war.Init + war.Ranks;
            }

            _level = level;
            _range = CastRange(info, _initPlusRanks);
            _manaNeeded = GearCasts.ManaCeiling(info, 0);
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"the level is chosen again: {from} to {Level}, {info.Name} ({info.Id}) at chance {GearCasts.Chance(skill, info.Power):0.0000} from War Magic {skill} (the enchantments, the level or the skill changed); range {_range:0.#} m, mana up to {_manaNeeded}"));
        }

        ReadMana(q, self.Spellbook);
        if (_view is not null)
        {
            ReadDrain(_view);
        }
    }

    /// <summary>The killed guids whose object has left the table leave the list (<c>m5-fight-killed-growth</c>; item 0's client-side cull removes a dead creature's object, and a re-created guid is alive again). No allocation.</summary>
    private void PruneKilled(WorldObjectTable objects)
    {
        for (var i = _killed.Count - 1; i >= 0; i--)
        {
            if (!objects.TryGet(_killed[i], out _))
            {
                _killed.RemoveAt(i);
            }
        }
    }

    private ScenarioStatus CastAnswer(IGameSessionView session, double clock)
    {
        var end = _cast!.Step(clock);
        NoteCasts();
        if (_cast.Report.ManaRefusals > 0)
        {
            BeginManaWait(clock); // M5 1 fix A, 1-R9: the pause for mana opens at the bolt's first refused try
        }

        if (end == CastEnd.Running)
        {
            return ScenarioStatus.Running;
        }

        var report = _cast.Report;
        var last = _cast.LastOutcome;
        if (end is CastEnd.Cancelled or CastEnd.DryRun)
        {
            // M5 1 K3 (D13): the kill switch or a dry run reaches the bolt now that it is an automation cast. A loop that went on missing would walk and cast again at once, so the fight stops, named.
            return Fail(session, "fight: the cast was not carried out: " + (last is { } stopped ? Describe(stopped) : end + ": " + report.Why));
        }

        _castTally.Add(in report);
        var retryManaShort = end == CastEnd.ManaShort && report.Code == CastAttempt.FizzleCode; // the retry's level needs more mana than is held: nothing was sent
        // M5 1 F1: under the mana order a 0x0401 end is the mana's (the tries were spent, or the mana is under the floor), not a miss: the mana phase, funding the ceiling now (the design's section 3.4 step 3).
        var refusedForMana = _options.Mana.On && end == CastEnd.ManaShort && report.Code == CastAttempt.ManaCode;
        NoteFizzleMisses(report.Fizzles);
        var level = retryManaShort ? report.NextLevelIndex : report.LevelIndex;
        if (level != _level && !_warActive) // M5 7 F9: a pick is no level of the bolt line
        {
            DropTo(level);
        }

        var confirmedOutcome = last.GetValueOrDefault();
        var confirmed = end == CastEnd.Landed && last is { Kind: ActionOutcomeKind.Confirmed };
        if (confirmed)
        {
            NoteInFlight(session, clock, report.SpellId, _target, clock - report.Seconds); // M5 7 D124 (S1b): its projectiles count against the next picks until they land
        }

        if (_ringOpen)
        {
            _ringOpen = false;
            _ringCasts?.Ended(confirmed, clock); // M5 7 F22: the ring has left the hand; its hits are counted until the next ring leaves
        }

        if (_volleyOpen)
        {
            _volleyOpen = false;
            _volleyCasts?.Ended(confirmed, clock); // M5 7 F24
        }

        var fizzleEnd = end == CastEnd.Fizzled || retryManaShort;
        if (fizzleEnd && !_fizzleDropped)
        {
            _fizzleDropped = true; // a fizzle spends the once-per-scenario drop even when there is no lower level to drop to
            if (report.LevelsDownSpent == 0)
            {
                _log.Warn(LogCategory, "a fizzle on the lowest known level; there is no level to drop to");
            }
        }

        _fizzlesSpent = retryManaShort ? report.FizzleRetriesSpent : 0;
        if (!refusedForMana && !retryManaShort)
        {
            EndManaWait(clock); // a try was accepted (it landed, fizzled or ended otherwise): the pause for mana, if any, is over
        }

        if (refusedForMana)
        {
            _triesSpent = report.ManaRefusals > 0;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"the bolt's Mana Conversion tries: {report.ManaRefusals} refused (0x0401, nothing charged) in {report.Tries} tries; the mana order funds {(_triesSpent ? "the ceiling " + _manaNeeded : "the floor " + _manaFloor)} next"));
            BeginManaWait(clock);
            _manaPhase = ManaPhase.Decide;
            _phase = Phase.Mana;
            return ScenarioStatus.Running;
        }

        if (end == CastEnd.InTheAir)
        {
            return AirEnded("the bolt", in report); // M6 14 airborne: the moment's, not the spell's: no miss, no refusal counted toward M6 7 K's drop, no walk
        }

        if (end == CastEnd.TargetDied)
        {
            // M5 7 F28: the kill landed during the wind-up and the release found no target (0x042C). Not a miss and no walk: the dead guid is kept out of the pick and the next target is chosen at once.
            var gone = _target;
            _tally.NoteTargetDiedMidCast();
            if (gone != 0 && !_killed.Contains(gone) && session.State.Objects.TryGet(gone, out _))
            {
                _killed.Add(gone);
            }

            _log.Info(LogCategory, $"the target 0x{gone:X8} died during the wind-up ({report.Why}); not a miss, no walk: the next target is chosen at once (target_died_mid_cast {_tally.TargetDiedMidCast})");
            _phase = Phase.Choose;
            return ScenarioStatus.Running;
        }

        if (confirmed)
        {
            // The projectile exists. A hit shows as a damage line, a kill as the 0x01AD notice, within the hit wait: the bolt's flight to the target at its CreateObject velocity plus a margin (the marks were taken when the
            // cast was sent: a close target can be hit in the frame the outcome arrives).
            _awaitSince = clock;
            _hitWait = HitWaitFor(session, in confirmedOutcome);
            _boltResistMark = _boltResists; // M6 7 AA: taken here, not at the send: a resist of an earlier bolt during this one's wind-up says nothing of this one
            _finishHoldNoted = false;
            _refusalRun = default;
            _phase = Phase.Hit;
            return ScenarioStatus.Running;
        }

        if (end == CastEnd.NoComponents)
        {
            return ComponentRefusal(session, in report); // M6 7 K: dropped for the fight, the next castable, or the fight's named end; no miss, no walk
        }

        if (!fizzleEnd)
        {
            _tally.NoteMiss();
            _missesInARow++;
            _log.Info(LogCategory, $"miss: the cast was not confirmed: {(last is { } outcome ? Describe(outcome) : end + ": " + report.Why)} (misses in a row {_missesInARow})");
        }

        if (end == CastEnd.ServerError && RefusedForGood(in report))
        {
            // M6 7 K: a permanent refusal never repeats every frame: the third in a row of the same code on the same spell drops it for the fight
            DropWarSpell(report.SpellId, string.Create(CultureInfo.InvariantCulture, $"ACE refused it {RefusalRepeats} times in a row (0x{report.Code:X4}: {report.Why})"), component: false);
            if (!GoOnAfterDrop(session, out var failed))
            {
                return failed;
            }
        }

        if (last is { Kind: ActionOutcomeKind.ServerError, Code: MissileOutOfRange })
        {
            return StartWalk(session, clock, "the server answered MissileOutOfRange (0x0550)");
        }

        if (last is { Kind: ActionOutcomeKind.ServerError, Reason: ActionReason.NoEffect } && _tally.DamageLinesDealt == _damageMark && WastedDrops(session, clock, "the cast ended with no bolt: " + Describe(last.Value)))
        {
            return ScenarioStatus.Running; // M6 8 L: a target the server will not let a bolt reach (below a ledge: no wall hit is ever seen) is dropped at the shared count
        }

        if (_missesInARow >= MissesBeforeWalking)
        {
            return StartWalk(session, clock, $"{_missesInARow} misses in a row");
        }

        if (retryManaShort)
        {
            BeginManaWait(clock);
            _manaPhase = ManaPhase.Decide;
        }

        _phase = retryManaShort ? Phase.Mana : Phase.Choose; // the retry's mana: the mana phase (the order, or the ceiling wait with --cast-mana-tries 0)
        return ScenarioStatus.Running;
    }

    /// <summary>
    /// Counts the casts that went through (<c>spells_cast=</c>, M5 1 fix A, 1-R10): the tries the attempt sent, less those the server refused for mana (0x0401: nothing was cast, nothing charged; they stay in
    /// <c>mana_refusals=</c>) and less the one still in flight, which is counted when its answer comes.
    /// </summary>
    private void NoteCasts()
    {
        var report = _cast!.Report;
        var inFlight = _cast.IsRunning && _cast.Handle is { Outcome: null } ? 1 : 0;
        var tries = report.Tries - report.ManaRefusals - inFlight;
        while (_castNoted < tries)
        {
            _castNoted++;
            _tally.NoteCast();
        }
    }

    /// <summary>A fizzled try is a miss, as it was when the bolt was sent a try at a time.</summary>
    private void NoteFizzleMisses(int fizzles)
    {
        for (var i = 0; i < fizzles; i++)
        {
            _tally.NoteMiss();
            _missesInARow++;
            _log.Info(LogCategory, $"miss: the cast was not confirmed: ServerError (code 0x{FizzleCode:X}) (misses in a row {_missesInARow})");
        }
    }

    private ScenarioStatus HitWait(IGameSessionView session, double clock)
    {
        _finishInFlight = false;
        if (_tally.Kills > _killMark)
        {
            _wallHitsOn.Remove(_target); // M6 7 AM
            Shots.NoteDamage(_target); // M6 8 L
            _log.Info(LogCategory, $"kill {_tally.Kills}: {_tally.LastKillText}");
            NoteKill(session, clock);
            _missesInARow = 0;
            _phase = Phase.Choose;
            return ScenarioStatus.Running;
        }

        if (_tally.DamageLinesDealt > _damageMark)
        {
            _wallHitsOn.Remove(_target); // M6 7 AM: a bolt that reached it ends the count of wall hits on it
            Shots.NoteDamage(_target); // M6 8 L: a target that takes damage is kept
            _missesInARow = 0;
            _health = session.Actions.QueryHealth(_target, ActionSource.Host);
            _phase = Phase.Choose;
            return ScenarioStatus.Running;
        }

        if (TargetGone(session.State.Objects, _target))
        {
            _deadSince = clock;
            _log.Info(LogCategory, $"the target 0x{_target:X8} is dead or gone; waiting for the kill notice");
            _phase = Phase.DeadWait;
            return ScenarioStatus.Running;
        }

        if (_boltResists > _boltResistMark)
        {
            return ResistEndsWait(session, clock); // M6 7 AA: the bolt's answer is in: nothing more can come of it
        }

        if (EmergencyEndsWait(session, clock))
        {
            _phase = Phase.Choose; // M6 7 AA: the hit wait never blinds the fight: the emergency acts now, the bolt stays in the in-flight ledger and no miss is read
            return ScenarioStatus.Running;
        }

        if (WallHit(session, clock))
        {
            _tally.NoteMiss();
            _tally.NoteWallHit();
            _missesInARow++;
            _log.Info(LogCategory, $"wall hit: the bolt stopped {clock - _awaitSince:0.0#} s after the cast, short of the target, with no damage line");
            Shots.NoteDisagreement(_target, NameOfTarget(session, _target), "the bolt hit the world short of it"); // M6 8 L
            if (WallStay(session, clock))
            {
                return ScenarioStatus.Running; // M6 7 AM: outdoors the fight stays on the target: a step to a better line, or the target blocked for a time (FightScenario.WallStay.cs); each try is from a new spot, so its three stand
            }

            if (WastedDrops(session, clock, "the bolt hit the world short of it"))
            {
                return ScenarioStatus.Running; // M6 8 L: in a dungeon too a target the bolts cannot reach is dropped at the shared count
            }

            return StartWalk(session, clock, "a bolt hit a wall");
        }

        if (clock - _awaitSince > _hitWait)
        {
            _tally.NoteMiss();
            _missesInARow++;
            _log.Info(LogCategory, $"miss: the projectile was confirmed but no damage line or kill came within {_hitWait:0.##} s (misses in a row {_missesInARow})");
            if (WastedDrops(session, clock, string.Create(CultureInfo.InvariantCulture, $"the bolt flew and no damage landed within {_hitWait:0.##} s")))
            {
                return ScenarioStatus.Running; // M6 8 L
            }

            if (_missesInARow >= MissesBeforeWalking)
            {
                return StartWalk(session, clock, $"{_missesInARow} misses in a row");
            }

            _phase = Phase.Choose;
        }

        return ScenarioStatus.Running;
    }

    /// <summary>
    /// True when the confirmed bolt has stopped (its velocity went to zero: ACE's <c>ProjectileImpact</c> sends the Explode, then the zero velocity, then the damage) for <see cref="WallGraceSeconds"/> with no damage line,
    /// and its stop point (the bolt's origin plus its speed times the seconds it flew) is more than <see cref="WallStopShortMetres"/> short of the target's position now: a bolt that dies short of its target hit the world. A stop
    /// at the target is a resist or an evade, and so is any stop with an "X resists your spell" line since the cast (4.2 R2: a tusker that ran at the mage met the bolt early and resisted it).
    /// </summary>
    private bool WallHit(IGameSessionView session, double clock)
    {
        if (_options.IgnoreLine || _boltGuid == 0 || double.IsNaN(_boltSpeed) || !session.State.Objects.TryGet(_boltGuid, out var bolt) || bolt.Velocity.Length() >= MinProjectileSpeed)
        {
            return false;
        }

        if (double.IsNaN(_boltStopAt))
        {
            _boltStopAt = clock;
        }

        if (clock - _boltStopAt < WallGraceSeconds || _tally.Resists > _resistMark || !session.State.Objects.TryGet(_target, out var victim) || StandsAt(session, victim) is not { } at)
        {
            return false;
        }

        var toTarget = PositionAgreement.DistanceMetres(in _boltOrigin, in at);
        return !double.IsNaN(toTarget) && StoppedShortOfTarget(toTarget, _boltSpeed, _boltStopAt - _awaitSince);
    }

    /// <summary>The bolt's stop judged on its own, for the tests: see <see cref="WallHit"/>.</summary>
    internal static bool StoppedShortOfTarget(double distanceToTarget, double speed, double flightSeconds) => distanceToTarget - speed * flightSeconds > WallStopShortMetres;

    /// <summary>
    /// The hit wait of the cast just confirmed, once per cast: the projectile the tracker took (its guid is the outcome's detail, <see cref="CastTracker.TryReadProjectile"/>), its velocity and position from the object table
    /// and the target's position give <see cref="HitWaitFor(double, float)"/>; the floor applies, logged, when the projectile or a position is not in the table. Allocates the one log line.
    /// </summary>
    private double HitWaitFor(IGameSessionView session, in ActionOutcome outcome)
    {
        var objects = session.State.Objects;
        _boltGuid = 0;
        _boltSpeed = double.NaN;
        _boltStopAt = double.NaN;
        if (!CastTracker.TryReadProjectile(in outcome, out var guid) || !objects.TryGet(guid, out var bolt))
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"hit wait: the confirmed cast names no projectile in the object table ({outcome.Detail}); the floor of {HitWaitSeconds:0.#} s applies"));
            return HitWaitSeconds;
        }

        var from = bolt.Position ?? session.State.Self.Position;
        if (from is not { } origin || !objects.TryGet(_target, out var victim) || StandsAt(session, victim) is not { } at)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"hit wait: the projectile 0x{guid:X8} or the target 0x{_target:X8} has no position in the object table; the floor of {HitWaitSeconds:0.#} s applies"));
            return HitWaitSeconds;
        }

        var speed = bolt.Velocity.Length();
        var distance = PositionAgreement.DistanceMetres(in origin, in at);
        var wait = HitWaitFor(distance, speed);
        if (speed >= MinProjectileSpeed && !double.IsNaN(distance))
        {
            (_boltGuid, _boltSpeed, _boltOrigin) = (guid, speed, origin);
        }

        _log.Info(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"cast confirmed: projectile 0x{guid:X8} at {speed:0.0} m/s, the target 0x{_target:X8} {distance:0.0} m from it: hit wait {wait:0.##} s (flight plus {HitWaitMarginSeconds:0.#} s, at most {HitWaitMaxSeconds:0.#}{(speed < MinProjectileSpeed ? "; the velocity is not a speed to divide by, so the cap" : string.Empty)})"));
        return wait;
    }

    /// <summary>
    /// A kill notice arrived for the cast in flight. When its text names the target, the notice is held as pending and settled by the object table (<see cref="SettlePendingKill"/>): ACE sends the 0x01AD before the Dead motion
    /// and keeps the body for its death animation, so the read model may still show a live-looking creature for a frame or two (3.1-R2). A notice that does not name the target (3.1-R7) still counts toward the total kills, is
    /// logged, and leaves the target pickable.
    /// </summary>
    private void NoteKill(IGameSessionView session, double clock)
    {
        if (_tally.TargetKills > _targetKillMark)
        {
            SettlePendingKill(session, clock);
            return;
        }

        _log.Info(LogCategory, $"the kill notice does not name the target 0x{_target:X8}; it counts in kills but not in target_kills");
    }

    /// <summary>
    /// M5 7 F15 (7-F1): a pending kill notice (one that named the target's name) becomes the target's kill only when that creature is dead or gone: health 0, the Dead motion, or out of the object table. Then it is never
    /// picked again, and the tour, the defence count and the loot take the kill. A notice that the object table does not confirm within <see cref="KillNoticeSeconds"/> was a neighbour's of the same name (a ring or a stray
    /// bolt): it is logged once and the living target stays in the fight.
    /// </summary>
    private void SettlePendingKill(IGameSessionView session, double clock)
    {
        var guid = _killPendingGuid;
        if (_killPendingAt < 0)
        {
            return;
        }

        if (guid == 0 || TargetGone(session.State.Objects, guid))
        {
            _killPendingAt = -1;
            _killPendingGuid = 0;
            if (guid != 0 && !_killed.Contains(guid) && session.State.Objects.TryGet(guid, out _))
            {
                _killed.Add(guid); // a body still in the table (dead motion, deletion to come) is kept out of the pick; one already gone cannot be picked
            }

            NoteDefenceKill(guid); // M5 7 F4: defended=
            FeedKill(guid); // g6: the tour's schedule takes the kill at once, before the fight's own end checks
            if (_loot is not null && NoteLootKill(session, guid) is { } lootName)
            {
                ConsumeClaim(lootName); // M5 7 P7b (D139 i): the target's own notice is spent; the others pair with their corpses
            }

            return;
        }

        if (clock - _killPendingAt > KillNoticeSeconds)
        {
            _log.Info(LogCategory, $"the kill notice named 0x{guid:X8}'s name but that creature lives (a neighbour of the same name died); it stays the target{(_loot is not null ? "; the neighbour's corpse is queued by the splash pairing (D139 i)" : string.Empty)}");
            _killPendingAt = -1;
            _killPendingGuid = 0;
        }
    }

    /// <summary>True while a kill notice for <see cref="_killPendingGuid"/> awaits the table (at most <see cref="KillNoticeSeconds"/>): the creature is not picked, so a real kill whose Dead motion and deletion are still coming (3.1-R2) is not bolted again.</summary>
    private bool KillInGrace() => _killPendingAt >= 0;

    /// <summary>g6: the tour's schedule takes the kill: the target's wcid and the cell it died in (the read model's last position of it).</summary>
    private void FeedKill(uint guid)
    {
        guid = guid == 0 ? _seenTarget : guid; // the target was let go (a walk began) before the notice came
        if (_walk?.Tour is not { } tour || _view is null || guid == 0 || guid == _fedTarget)
        {
            return;
        }

        _fedTarget = guid;

        if (_view.State.Objects.TryGet(guid, out var o) && StandsAt(_view, o) is { } at)
        {
            tour.Killed(o.Weenie.WeenieClassId, at.CellId, _view.Clock.Now, guid);
        }
        else if (guid == _seenTarget)
        {
            tour.Killed(_seenWcid, _seenCell, _view.Clock.Now, guid); // the body is already gone: where it was last seen
        }
        else
        {
            _log.Warn(LogCategory, $"the kill of 0x{guid:X8} reaches the tour without its wcid and cell: the target was never seen with a position");
        }
    }

    private ScenarioStatus StartWalk(IGameSessionView session, double clock, string why)
    {
        PullWalkAsked(session, why); // M6 7 AX: FightScenario.Pull.cs
        if (_walk is null)
        {
            return Fail(session, "fight: the loop must walk (" + why + ") but has no walker; TODO(m3-fight-no-walker)");
        }

        if (!_walk.BeginLeg(session, clock, out var leg))
        {
            return Fail(session, "fight: no walk could be planned (" + why + "): " + _walk.Failure);
        }

        // F5 (D47): the tour names the leg's purpose (seen, presumed, due, return), or holds (no leg: the walk waits a moment and the fight looks again); a hold is not a walk, and its line is the tour's, once.
        var purpose = _walk.Tour?.Purpose ?? TourPurpose.None;
        var holding = purpose == TourPurpose.Hold;
        if (!holding)
        {
            _walks++;
        }

        NoteLeg(purpose); // M5 7 P7b (D139 g): what a stuck give-up names and rests
        NoteLegFrom(session); // M5 7 P7i: where the leg began (the stuck spot's count)
        _missesInARow = 0;
        _target = 0;
        if (double.IsNaN(_idleSince) && why is NoTargetInRange or NoClearLine or TourLeaves)
        {
            _idleSince = clock; // no target to pick: the burst rate leaves this time out
        }

        if (!holding || !_holdingLogged)
        {
            _log.Info(LogCategory, purpose == TourPurpose.None ? $"walking: {why}; {leg}" : $"{(holding ? "holding" : "walking")}: purpose={purpose.ToString().ToLowerInvariant()} ({_walk.Tour!.PurposeNote}); {why}; {leg}");
        }

        _holdingLogged = holding;
        _walkSight = !holding && !_pickLeaving && why is NoClearLine or NoTargetInRange; // M6 7 Y: read by SightDropsWalk
        _walkSightAt = clock + WalkSightSeconds;
        _phase = Phase.Walk;
        return ScenarioStatus.Running;
    }

    /// <summary>
    /// A fizzle drops one level once (D10, as the buff cycle's retry): the cast policy (<see cref="CastRules.Bolt"/>) chose the next lower level the spellbook holds (the request's <see cref="CastRequest.Usable"/>; the arena-here run has no book and
    /// knows them all) and the SpellTable has, and the scenario takes it as its level, with the range (<see cref="CastRange"/>) and the mana ceiling recomputed for it (M3 3.2 review, package 2 finding 16: the drop read the SpellTable alone
    /// and kept the higher level's range).
    /// </summary>
    private void DropTo(int i)
    {
        _fizzleDropped = true;
        if (!_spells.TryGetValue(_levelIds[i], out var info))
        {
            return;
        }

        var from = _level;
        _level = i;
        _range = CastRange(info, _initPlusRanks);
        _manaNeeded = GearCasts.ManaCeiling(info, 0);
        _manaFloor = FloorFor(info);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"a fizzle: one level down, to {info.Name} ({info.Id}){(from - i > 1 ? ", the next lower level the spellbook knows" : string.Empty)}; range {_range:0.#} m, mana up to {_manaNeeded}"));
    }

    private void LogHealth(IGameSessionView session)
    {
        _health = null;
        var objects = session.State.Objects;
        _log.Info(LogCategory, objects.HealthOf(_target, out var fraction) ? string.Create(CultureInfo.InvariantCulture, $"target 0x{_target:X8} health {fraction:0.00}") : $"target 0x{_target:X8} health unknown");
    }

    private Func<uint, bool>? _targetGoneFn;

    /// <summary>M5 7 F28: the fight's own test for <see cref="CastRequest.TargetGone"/>: the guid is dead or gone from the world, or already a kill (the health at 0, the death motion, the removal, or the kill list).</summary>
    private bool TargetDeadOrGone(uint guid) => _view is { } view && (TargetGone(view.State.Objects, guid) || _killed.Contains(guid));

    private static bool TargetGone(WorldObjectTable objects, uint guid) =>
        !objects.TryGet(guid, out var o) || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord };

    /// <summary>M6 5 q4 (D57): what the fight's <see cref="FightOptions.Until"/> reads as met now (<c>item 7 x1 held 1</c>, <c>chat "..."</c>), or null while it is not.</summary>
    private string? UntilHolds(IGameSessionView session, FightUntil until)
    {
        if (until.Chat is { } chat)
        {
            // M6 5 F1 (review 5-R14): system chat only (the server's kill counter and quest lines), never a player's or an NPC's words.
            return QuestReads.Since(session.State.Chat, _untilMark).Any(l => l.Kind == ChatKind.System && l.Text.Contains(chat, StringComparison.Ordinal)) ? $"chat \"{chat}\"" : null;
        }

        var held = UntilUnits(session.State, until); // M6 14 combine: along the chain, in the item's equivalents
        return held >= until.Count ? string.Create(CultureInfo.InvariantCulture, $"item {until.ItemWcid} held {held} of {until.Count}{(until.Chain.Count > 1 ? " (its equivalents along the no-roll combine chain)" : string.Empty)}") : null;
    }

    private readonly List<uint> _untilScratch = [];

    private ScenarioStatus EndFight(IGameSessionView session, double clock, string why)
    {
        _fightEnd = clock;
        if (session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var manaAtEnd, out var manaMaxAtEnd))
        {
            (_manaEnd, _manaEndMax) = (manaAtEnd, manaMaxAtEnd); // M6 8 H: mana_end= mana_end_pct=, the bar the owner reads
        }

        PullRestAbandon(session, clock, why); // M6 7 AB: a rest the stop cut stands her up and keeps its seconds in the verdict
        if (_manaCast is { IsRunning: true })
        {
            // M5 1 fix A, 1-R11: a dance in progress at the stop keeps its presses in the line's counts.
            _manaCast.Stop("the fight ended", clock);
            var danceReport = _manaCast.Report;
            _castTally.Add(in danceReport);
            _manaPresses += danceReport.Tries;
        }

        if (_refill.Cast is { IsRunning: true } refillCast)
        {
            refillCast.Stop("the fight ended", clock); // M5 7 F5: a Revitalize Self in flight at the stop keeps its tries in the line's counts
            var refillReport = refillCast.Report;
            _castTally.Add(in refillReport);
        }

        if (_drain.Cast is { IsRunning: true } drainCast)
        {
            drainCast.Stop("the fight ended", clock); // M5 7 F7: a drain or heal in flight at the stop keeps its tries in the line's counts
            var drainReport = drainCast.Report;
            _castTally.Add(in drainReport);
        }

        LifeThroughAtStop(); // M6 14: a life cast whose kill ended the fight is counted as landed (FightScenario.LifeThrough.cs)

        if (_openerCast is { IsRunning: true } openerCast)
        {
            openerCast.Stop("the fight ended", clock); // M5 7 F15 (7-F6): an opener pending at the stop keeps its tries in the line's counts and is not left in flight into the next scenario
            var openerReport = openerCast.Report;
            _castTally.Add(in openerReport);
            _openersSent += Math.Max(0, openerReport.Tries - openerReport.ManaRefusals);
        }

        if (_staminaDrainCast is { IsRunning: true } staminaDrainCast)
        {
            staminaDrainCast.Stop("the fight ended", clock); // M5 7 F15 (7-F6): likewise a Drain Stamina Other pending at the stop
            var staminaDrainReport = staminaDrainCast.Report;
            _castTally.Add(in staminaDrainReport);
        }

        if (_cast is { IsRunning: true })
        {
            _cast.Stop("the fight ended", clock);
            NoteCasts();
            var boltReport = _cast.Report;
            _castTally.Add(in boltReport);
        }

        _loot?.Abandon(session);
        _log.Info(LogCategory, $"{why}: {VerdictFields()}");
        _walk?.Abandon();
        if (_options.HoldStance && !_endSession)
        {
            // M5 7 F25 (D93): the autopilot's own fight; its next station keeps the magic mode, so no peace between the two
            _log.Info(LogCategory, "the stance holds: the magic mode stays up for the autopilot's next station (F25)");
            return Conclude(session);
        }

        _pending = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Automation);
        Enter("mode-peace", clock);
        return ScenarioStatus.Running;
    }

    private void Subscribe(IGameSessionView session)
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        _diedSubscription = session.Events.Subscribe<PlayerDied>(OnDied);
        _createdSubscription = session.Events.Subscribe<ObjectCreated>(OnCreated);
        _movedSubscription = session.Events.Subscribe<ObjectMoved>(OnMoved);
        SubscribeDefence(session); // M5 7 F4: self-defence hears the attacks on the player
        SubscribeHeals(session); // M5 7 F19 (D92): the measured heals hear ACE's chat lines
        _view = session;
    }

    private IGameSessionView? _view;

    private void OnDied(in PlayerDied e)
    {
        _died = true;
        _deathText = e.Text;
    }

    private void OnMoved(in ObjectMoved e)
    {
        if (e.IsSelf && e.Verdict == PositionVerdict.SelfTeleport)
        {
            _selfTeleports++;
        }
    }

    private void OnCreated(in ObjectCreated e)
    {
        if (e.IsSelf || Stage != "fight" || _view is null || !_view.State.Objects.TryGet(e.Guid, out var o))
        {
            return;
        }

        NoteFreshCorpse(o, _view.Clock.Now); // M5 7 P7b (D139 i)
        if ((o.State & PhysicsState.Missile) == 0)
        {
            if (_walk?.Tour is { } tour && o.Position is { } at && _ground.InLandblocks(at.CellId))
            {
                tour.Created(o.Weenie.WeenieClassId, at.CellId, _view.Clock.Now, e.Guid); // g6: a slot's creature seen again stands; 2-R1: a create in a slot's cell is that slot's creature
            }

            return;
        }

        _projectiles++;
        _log.Info(
            LogCategory,
            "projectile created",
            new LogField("guid", $"0x{e.Guid:X8}"),
            new LogField("setup", $"0x{o.Physics.SetupId ?? 0:X8}"),
            new LogField("cell", o.Position is { } p ? $"0x{p.CellId:X8}" : "none"),
            new LogField("at", o.Position?.Frame.Origin),
            new LogField("velocity", o.Velocity));
    }

    /// <summary>After <c>mode-peace</c>: out of the den (<see cref="GroundExit"/>, best effort; not in the arena-here run, where the host put the player and keeps it there), then the end.</summary>
    private ScenarioStatus Conclude(IGameSessionView session)
    {
        var clock = session.Clock.Now;
        Subscribe(session); // the leave reads the player's own teleport positions
        if (!_options.ArenaHere && _options.LeaveAtEnd && _exit.Begin(session, clock, _selfTeleports))
        {
            Enter("leave-den", clock);
            return ScenarioStatus.Running;
        }

        return Finish(session, clock);
    }

    /// <summary>The end: <c>done</c> in the world when the host owns the session, else the log off and the quit.</summary>
    private ScenarioStatus Finish(IGameSessionView session, double clock)
    {
        if (!_endSession)
        {
            LogVerdict(session);
            Stage = "done";
            Dispose();
            return ScenarioStatus.Succeeded;
        }

        _pending = session.Actions.LogOff(ActionSource.Host);
        Enter("logging-off", clock);
        return ScenarioStatus.Running;
    }

    /// <summary>The conclusion's leave: waits for it, then the end.</summary>
    private ScenarioStatus LeaveDenStep(IGameSessionView session, double clock) =>
        _exit.Tick(session, clock, _selfTeleports) ? Finish(session, clock) : ScenarioStatus.Running;

    /// <summary>The failed run's leave, whatever <c>endSession</c> is: waits for it, then the log off (<c>endSession: true</c>) or the end as Failed (the host logs off).</summary>
    private ScenarioStatus FailLeaveStep(IGameSessionView session)
    {
        if (!_exit.Tick(session, session.Clock.Now, _selfTeleports))
        {
            return ScenarioStatus.Running;
        }

        _failLeave = false;
        return FailInWorld(session);
    }

    /// <summary>The failed run's end in the world, out of the den: the log off when the scenario owns the session (as the buff scenario's failure, so the next login is not refused as CharacterInWorld), else Failed at once (the game's leg logs off itself).</summary>
    private ScenarioStatus FailInWorld(IGameSessionView session)
    {
        if (!_endSession)
        {
            Dispose();
            return ScenarioStatus.Failed;
        }

        _log.Info(LogCategory, "logging off before the run ends as failed");
        _pending = session.Actions.LogOff(ActionSource.Host);
        _failLogOff = true;
        return ScenarioStatus.Running;
    }

    /// <summary>
    /// M6 14 motes (live 2026-11-17 00:07:58: the mote hunt's fight ended on its first frame and the run's log held no line saying why: a failure's own line is an error, which the live capture writes to another stream, out of order): a failed
    /// fight also names its why at Info in the fight's stream, <c>fight end: failed: &lt;why&gt;</c>, just before its verdict. A clean end (<see cref="EndFight"/>) already opens its Info line with its why. The verdict's fields are not
    /// touched: their readers pin its tail and its order (the loot fields, the tour fields, the station's until).
    /// </summary>
    private void NoteFailedEnd(string why) => _log.Info(LogCategory, "fight end: failed: " + why);

    private ScenarioStatus Fail(IGameSessionView session, string why)
    {
        Failure = why;
        NoteFailedEnd(why);
        _fightEnd = double.IsNaN(_fightEnd) ? session.Clock.Now : _fightEnd;
        PullRestAbandon(session, session.Clock.Now, why); // M6 7 AB
        _walk?.Abandon();
        _seam.Abandon(); // a travel leg still running releases its keys
        _loot?.Abandon(session);
        _log.Error(LogCategory, why);
        LogVerdict(session);
        if (session.State.Phase == SessionPhase.InWorld && Stage is not ("logging-off" or "quitting"))
        {
            // Out of the den first, whatever endSession is (best effort, GroundExit; not in the arena-here run): a mage left in the den by this run, logged off by it or by the game's leg as soon as this returns Failed, meets the
            // tuskers at its next login. The run stays Running until the leave is over, then logs off (endSession: true) or ends Failed where it stands.
            Subscribe(session);
            if (!_options.ArenaHere && _options.LeaveOnFailure && _exit.Begin(session, session.Clock.Now, _selfTeleports))
            {
                _failLeave = true;
                return ScenarioStatus.Running;
            }

            return FailInWorld(session);
        }

        Dispose();
        return ScenarioStatus.Failed;
    }

    private void Enter(string stage, double clock)
    {
        Stage = stage;
        _stageStart = clock;
        _log.Info(LogCategory, "stage " + stage);
    }

    private static string Describe(in ActionOutcome outcome) =>
        outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail);

    /// <summary>The <c>name=value</c> token of a verdict line (the gear scenario's), or <c>name=?</c>.</summary>
    private static string Token(string fields, string name)
    {
        var key = name + "=";
        var at = fields.StartsWith(key, StringComparison.Ordinal) ? 0 : fields.IndexOf(" " + key, StringComparison.Ordinal);
        if (at < 0)
        {
            return key + "?";
        }

        if (at > 0)
        {
            at++;
        }

        var end = fields.IndexOf(' ', at);
        return end < 0 ? fields[at..] : fields[at..end];
    }

    // ------------------------------------------------------------------------------------------------ the loot step (M5 item 3 m2, D38)

    /// <summary>
    /// <c>--fight-loot</c>'s verdict fields (M5 item 3 m2, report-only): <c>loot=on|off looted=&lt;corpses&gt; kept=&lt;items&gt; loot_value=&lt;sum of the kept Values&gt;</c>. <c>looted</c> counts the corpses the step opened and judged,
    /// <c>kept</c> the items it picked up (the server's 0022) and <c>loot_value</c> their Values as the rule read them. With looting off: <c>loot=off looted=0 kept=0 loot_value=0</c>. The last fields of <see cref="VerdictFields"/>.
    /// M5 7 F27 (report-only, looting on only): <c>loot_opened</c> the corpses opened, <c>loot_skipped_unreached</c> those left beyond the radius, out of sight or not reached by ACE's walk, <c>loot_skipped_hostile</c> those left with a creature beside them
    /// and <c>loot_walk_s</c> the seconds spent waiting on ACE's walk to a corpse.
    /// M5 7 F28 (D95, report-only): <c>loot_plan_kept</c> and <c>loot_plan_left</c> the items the rule's plans kept and left over all the corpses it planned (pyreals count as kept; the log has a line per item) and <c>loot_plan_value</c> the Value of the items kept.
    /// M5 7 loot retry (report-only, the last fields): <c>corpses_opened</c> the corpses opened, <c>loot_left_aggro</c> the times a corpse was put off by the aggro rule (an attacker, or a living target creature within <c>--fight-loot-aggro-m</c>; one corpse can count more than once;
    /// <c>loot_skipped_hostile</c> counts each corpse once) and <c>loot_left_decayed</c> the corpses given up after their retry window (<see cref="FightOptions.LootRetryWindowSeconds"/>) or gone from the world.
    /// M5 7 D123 (report-only, after <c>stack_merges</c>): <c>loot_busy_waits</c> the opens answered "You're too busy!" (0x001D) and sent again, <c>loot_left_busy</c> the corpses left with every open answered busy.
    /// </summary>
    public string LootFields() =>
        _loot is null
            ? "loot=off looted=0 kept=0 loot_value=0"
            : string.Create(CultureInfo.InvariantCulture, $"loot=on looted={_loot.Looted} kept={_loot.Kept} loot_value={_loot.Value} loot_opened={_loot.Opened} loot_skipped_unreached={_loot.SkippedUnreached} loot_skipped_hostile={_loot.SkippedHostile} loot_walk_s={_loot.WalkedSeconds:0.#} loot_plan_kept={_loot.PlanKept} loot_plan_left={_loot.PlanLeft} loot_plan_value={_loot.PlanValue} corpses_opened={_loot.Opened} loot_left_aggro={_loot.AggroLeft} loot_left_decayed={_loot.DecayedLeft} worth_a_look={_loot.WorthALook} stack_merges={_loot.StackMerges} loot_busy_waits={_loot.BusyWaits} loot_left_busy={_loot.BusyLeft}{(_loot.GreatTaken + _loot.GreatLeft + _loot.RoomDrops > 0 ? $" great_cantrips_taken={_loot.GreatTaken} great_cantrips_left={_loot.GreatLeft} loot_room_drops={_loot.RoomDrops} loot_room_dropped_value={_loot.RoomDroppedValue}" : string.Empty)}{(_loot.SwapsMade + _loot.SwapsShort > 0 ? $" loot_swaps={_loot.SwapsMade} loot_swaps_short={_loot.SwapsShort}" : string.Empty)}");

    /// <summary>
    /// The guids the loot step picked up and saw held (M5 item 3 c2a, closes <c>TODO(m5-resupply-kept-list)</c>): the list the logistics station hands the resupply to sell. Read-only, in the order the items were kept; empty with looting off.
    /// </summary>
    public IReadOnlyList<uint> LootKeptGuids => _loot?.KeptGuids ?? [];

    /// <summary>The corpses the loot step left without looting (report-only; the log names each): one that did not appear, a walk or an open that failed, one out of reach with no walk on foot, the player's own, one that took too long, one given up to decay and (at the fight's end) one still put off. 0 with looting off.</summary>
    public int LootCorpsesLeft => _loot?.Left ?? 0;

    /// <summary>M5 7 D123: the opens of one corpse sent again after "You're too busy!" before the corpse is left (<c>loot_left_busy</c>).</summary>
    public const int LootBusyRetries = LootStep.BusyRetries;

    /// <summary>True while the loot step has a kill to loot or a corpse in hand (always false with looting off).</summary>
    public bool LootBusy => _loot is { Active: true };

    /// <summary>M6 6 F3 (<c>m6-resume-loot-queue</c>): the kills of this fight whose corpse is found and not yet looted (a read-only export: guid and the corpse's name), for the autopilot's checkpoint. Empty with looting off.</summary>
    public IReadOnlyList<KillCorpse> KillCorpses => _loot?.Pending() ?? [];

    /// <summary>
    /// M6 6 F3 (<c>m6-resume-loot-queue</c>): queues the corpses a stopped run's last kills left (<paramref name="corpses"/>, the checkpoint's) for this fight's loot step. Check before: the object is in view, is a corpse of the recorded name, is not
    /// the player's own, is not held or handled already and lies within the loot radius; any other is dropped, named. Returns the corpses queued.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="corpses">The recorded kill/corpse pairs.</param>
    public int SeedLoot(IGameSessionView session, IReadOnlyList<KillCorpse> corpses)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(corpses);
        var queued = 0;
        foreach (var pair in corpses)
        {
            string? drop = null;
            if (_loot is null)
            {
                drop = "looting is off";
            }
            else if (!session.State.Objects.TryGet(pair.Guid, out var o) || (o.DescriptionFlags & WorldObjectTable.CorpseFlag) == 0 || o.Position is not { } at)
            {
                drop = "not in view";
            }
            else if (!string.Equals(o.Name, CorpsePrefix + pair.Name, StringComparison.Ordinal))
            {
                drop = $"it is '{o.Name}' now, not the corpse of '{pair.Name}'";
            }
            else if (MageLootRules.IsOwnCorpse(o.Name, session.State.Self.Name))
            {
                drop = "the player's own corpse";
            }
            else if (_loot.Holds(pair.Guid))
            {
                drop = "already queued or looted";
            }
            else if (session.State.Self.Position is not { } own || at.ToGlobal().DistanceTo(own.ToGlobal()) is var d && d > LootRadius)
            {
                drop = "beyond the loot radius";
            }
            else
            {
                _loot.NoteKill(pair.Name, at, 0, session.Clock.Now, pair.Guid);
                queued++;
                continue;
            }

            _log.Warn(LogCategory, $"loot: the resumed run's corpse 0x{pair.Guid:X8} of '{pair.Name}' is dropped ({drop})");
        }

        return queued;
    }

    /// <summary>The kill notice named the target: the loot step is told which creature died and where it lay (its last known name and position), so it can look for the corpse. Returns the name queued, or null when none was.</summary>
    private string? NoteLootKill(IGameSessionView session, uint target)
    {
        var guid = target == 0 ? _seenTarget : target;
        string? name = null;
        CellFrame? at = null;
        var wcid = 0u;
        if (guid != 0 && session.State.Objects.TryGet(guid, out var body))
        {
            (name, at, wcid) = (body.Name, StandsAt(session, body), body.Weenie.WeenieClassId);
        }

        if (guid != 0 && guid == _seenTarget)
        {
            name ??= _seenName;
            at ??= _seenAt;
            wcid = wcid != 0 ? wcid : _seenWcid;
        }

        if (name is null || at is not { } where)
        {
            _log.Warn(LogCategory, $"loot: the kill of 0x{guid:X8} has no known name and position; its corpse is not looked for here (its kill notice stays for the splash pairing, D139 i)");
            return null;
        }

        _loot!.NoteKill(name, where, guid, session.Clock.Now, wcid: wcid);
        return name;
    }

    /// <summary>
    /// The loot step's machine (M5 item 3 m2; <c>--fight-loot on</c>, D38): after a kill it finds the kill's corpse (<c>Corpse of &lt;name&gt;</c> within <see cref="CorpseRangeMetres"/> of where the creature lay), opens it (M5 7 F27, closes
    /// <c>m5-loot-no-walk</c>: a corpse beyond reach (<see cref="HuntScenario.CorpseReachMetres"/>) is taken only within <c>--fight-loot-radius-m</c>, with a clear line of sight and no living target creature within <c>--fight-loot-aggro-m</c> of it; the Use is sent as it is and
    /// ACE's own MoveTo walks the mage to it, Player_Use.cs:184-206 and Player_Move.cs:37-96 @95ef426d, the magic stance kept), reads the items (the description's Value, else an appraisal), plans
    /// with <see cref="MageLootRules"/>, takes the plan's items with the shared <see cref="CorpseLooter"/> and closes it. A corpse that does not appear, a walk or an open that fails and a full pack are named, counted and the fight goes on.
    /// One corpse at a time, in the order of the kills. Not created with looting off: nothing of it runs.
    /// M5 7 loot retry: a corpse is put off only when a creature is attacking the mage now (the self-defence list) or a living target creature stands within <c>--fight-loot-aggro-m</c> (6 m) of it; a corpse put off stays queued, is retried when the fight has no
    /// target in reach (<see cref="TryRetry"/>) and the rule passes, in the order of the kills, until <see cref="FightOptions.LootRetryWindowSeconds"/> after the kill, then is given up (counted as left to decay). A creature that attacks while the walk or the open is
    /// in hand closes the corpse (when open) and puts it back in the queue: the fight goes first.
    /// </summary>
    private sealed partial class LootStep(ILogSink log, MageLootRules rules, FightScenario owner) : IDisposable
    {
        /// <summary>The seconds the corpse may take to appear after the step began looking (ACE shows it after the death animation; the hunt's value).</summary>
        public const double CorpseAppearSeconds = HuntScenario.CorpseAppearSeconds;

        /// <summary>How near the kill's last position the corpse must be, in metres (the hunt's value).</summary>
        public const float CorpseRangeMetres = HuntScenario.CorpseRangeMetres;

        /// <summary>The seconds the items of an opened corpse may take to arrive (the shared looter's cap).</summary>
        public const double ResolveSeconds = CorpseLooter.UnresolvedCapSeconds;

        /// <summary>The seconds the appraisals of items with no Value in their description may take; an unanswered one leaves the item unknown (<c>TODO(m5-loot-value-unknown)</c>).</summary>
        public const double AppraiseSeconds = 5;

        /// <summary>The seconds an open or a close may take before it is given up (the Action API's own timeouts are longer; the step must not hold the fight).</summary>
        public const double ActionSeconds = 10;

        /// <summary>The most seconds one corpse may hold the fight, from the search to the close: the walk, the open, the reading, the pick-ups (about 2.8 s each) and the close. Lifestone's own.</summary>
        public const double CorpseSeconds = 120;

        /// <summary>
        /// M5 7 D123: the wait after "You're too busy!" (0x001D) before the open is sent again (the cast's busy wait, <see cref="CastRules.DefaultBusyWaitSeconds"/>). ACE refuses a Use of a landblock object while the player
        /// <c>IsBusy</c> (Player_Use.cs:199-203 @95ef426d); a pick-up keeps it set until its return animation ends (Player_Inventory.cs:759-787), after the item is already in the pack, and a cast until its UseDone
        /// (Player_Magic.cs:935-985). Round 12 asked the next corpse 30-80 ms after closing the last one, inside that animation.
        /// </summary>
        public const double BusyWaitSeconds = CastRules.DefaultBusyWaitSeconds;

        /// <summary>M5 7 D123: the opens sent again after busy refusals of one corpse before it is left (named and counted <c>loot_left_busy</c>): about five seconds of waiting, past any pick-up's or cast's end. Lifestone's own.</summary>
        public const int BusyRetries = 5;

        private enum Stage
        {
            Idle,
            Seek,
            Opening,
            BusyWait,
            Resolve,
            Appraise,
            Looting,
            Wearing,
            Closing,

            /// <summary>M5 7 P7s (D148): the fight's own walk toward a corpse whose straight approach crosses a no-go cell (<see cref="OwnWalk"/>).</summary>
            OwnWalk,

            /// <summary>M6 6 B2 (<c>m5-loot-walk-for-los</c>): the fight's own walk to a floor spot with a clear line to a corpse that has none from where the mage stands (<see cref="LosWalk"/>).</summary>
            LosWalk,

            /// <summary>M6 14 keep every great cantrip (the owner, 2026-11-15): the sell loot the plan drops to make room for a great-cantrip piece, dropped and checked before the take (<see cref="MakingRoom"/>).</summary>
            MakingRoom,

            /// <summary>M6 14 want loot: the fight's own walk to the corpse of a creature whose create list can hold a wanted item, beyond the loot radius or with no line (<see cref="WantWalk"/>).</summary>
            WantWalk,
        }

        /// <summary>A kill whose corpse the step looks for: the creature's name and last position, the session clock the kill notice came, the corpse once found and whether it was ever put off.</summary>
        private sealed class LootKill(string name, CellFrame at, uint guid, double killedAt, uint wcid = 0)
        {
            public string Name { get; } = name;

            /// <summary>M6 14 want loot: the creature's weenie class id when the kill knew it (0: a splash kill or a resumed corpse, matched by name).</summary>
            public uint Wcid { get; } = wcid;

            public CellFrame At { get; } = at;

            public uint Guid { get; } = guid;

            public double KilledAt { get; } = killedAt;

            /// <summary>The corpse's guid once the step has found it (0 before).</summary>
            public uint Corpse { get; set; }

            /// <summary>True once the corpse was counted in <c>loot_skipped_hostile</c> (that field counts each corpse once).</summary>
            public bool Counted { get; set; }

            /// <summary>M5 7 P7b (D139 j): where the corpse lay when last seen (null before it was found).</summary>
            public CellFrame? CorpseAt { get; set; }

            /// <summary>M5 7 P7b (D139 j): true while the corpse is out of the object table before it can rot (lost from view); <see cref="CorpseAt"/> is where it lay.</summary>
            public bool OutOfView { get; set; }

            /// <summary>M5 7 P7b (D139 j): the session clock the corpse was missed from the table.</summary>
            public double LostAt { get; set; } = double.NaN;

            /// <summary>M5 7 P7b (D139 j): the session clock a walk back arrived at <see cref="CorpseAt"/> (NaN before).</summary>
            public double SpotReachedAt { get; set; } = double.NaN;

            /// <summary>M5 7 P7b (D139 j): true once the aggro rule's hold of the walk back was logged (once per loss).</summary>
            public bool WalkBackHeldLogged { get; set; }
        }

        private readonly Queue<LootKill> _kills = new();
        private readonly List<LootKill> _retry = []; // M5 7 loot retry: the corpses put off, in the order of the kills
        private readonly HashSet<uint> _handled = [];
        private readonly List<IdentifyHandle> _identifies = [];
        private Stage _stage;
        private LootKill _kill = null!;
        private uint _corpse;
        private string _corpseName = string.Empty;
        private double _stageStart;
        private double _corpseStart;
        private ActionHandle? _pending;
        private CorpseLooter? _looter;
        private MageLootPlan? _plan;
        private PackRules? _packRules; // M5 7 D121: the ground's pack rules, made at the first plan
        private readonly Queue<uint> _wear = new(); // M5 7 D125: the corpse's finds worth a look, judged for the self-equip before the close
        private bool _unknownNamed;
        private bool _aceWalk; // M5 7 F27: the open in hand is out of reach, so ACE's MoveTo walks the mage to the corpse first
        private readonly List<uint> _approach = []; // M5 7 P7s (D148): the cells of ACE's straight approach to the corpse in hand
        private bool _ownWalked; // M5 7 P7s (D148): the fight's own walk went toward the corpse in hand once (it is not walked toward twice)
        private bool _ownWalkStarted;
        private CellFrame _ownWalkSpot;
        private bool _traceTodoLogged;
        private int _busyRefusals; // M5 7 D123: the corpse in hand's 0x001D answers so far
        private bool _losWalked; // M6 6 B2 (m5-loot-walk-for-los): the walk for a line went once for the corpse in hand
        private bool _losWalkStarted;
        private CellFrame _losWalkSpot;
        private string _losWalkNote = string.Empty;
        private bool _seekYieldLogged; // M6 6 B2 (m5-defend-loot-first): the wait for a corpse not yet there gave the frame to the fight (logged once a kill)
        private uint _closeSent; // M6 6 B2 (m5-defend-loot-first): the corpse whose close the aggro rule sent, until the view confirms it (check, act, check)
        private double _closeSentAt;
        private bool _wearWaitLogged; // M6 6 FXD3 (6-D9): the wait for an in-flight wield is logged once
        private uint _wearingGuid; // the find the wield in flight was sent for

        /// <summary>True while a kill waits for its corpse or a corpse is in hand.</summary>
        public bool Active => _stage != Stage.Idle || _kills.Count > 0;

        /// <summary>The corpses opened and judged.</summary>
        public int Looted { get; private set; }

        /// <summary>The items the plans' pick-ups confirmed.</summary>
        public int Kept { get; private set; }

        /// <summary>The sum of the confirmed items' Values as the rule read them.</summary>
        public long Value { get; private set; }

        /// <summary>The guids of the items the plans' pick-ups confirmed held, in the order kept (the resupply's sale list).</summary>
        public List<uint> KeptGuids { get; } = [];

        /// <summary>M5 7 (report-only): the merges of a looted stack into a held stack of its class that the server confirmed (the shared looter's, ACE never merges at pick-up).</summary>
        public int StackMerges { get; private set; }

        /// <summary>The corpses left without being looted: not found, refused, unreachable, over the time, the player's own.</summary>
        public int Left { get; private set; }

        /// <summary>M5 7 F27 (report-only): the corpses the server opened (confirmed with the view in place).</summary>
        public int Opened { get; private set; }

        /// <summary>M5 7 F27 (report-only): the corpses left beyond the radius, out of sight, or not reached by ACE's walk (the move-to timed out, was busy or stuck).</summary>
        public int SkippedUnreached { get; private set; }

        /// <summary>M5 7 F27 (report-only): the corpses put off because a creature attacked the mage or a living target creature stood within the aggro reach of them (each corpse once; <see cref="AggroLeft"/> counts the times).</summary>
        public int SkippedHostile { get; private set; }

        /// <summary>M5 7 loot retry (report-only): the times a corpse was put off by the aggro rule (one corpse can count more than once).</summary>
        public int AggroLeft { get; private set; }

        /// <summary>M5 7 loot retry (report-only): the corpses given up after their retry window or gone from the world.</summary>
        public int DecayedLeft { get; private set; }

        /// <summary>M5 7 D123 (report-only): the opens answered "You're too busy!" (0x001D) and sent again after <see cref="BusyWaitSeconds"/>.</summary>
        public int BusyWaits { get; private set; }

        /// <summary>M5 7 D123 (report-only): the corpses left because every open of them was answered busy (<see cref="BusyRetries"/> waits spent).</summary>
        public int BusyLeft { get; private set; }

        /// <summary>The items the rule's plans kept over all the corpses planned (M5 7 F28; pyreals count).</summary>
        public int PlanKept { get; private set; }

        /// <summary>The items the rule's plans left, whatever the reason (M5 7 F28).</summary>
        public int PlanLeft { get; private set; }

        /// <summary>The Value of the items the rule's plans kept (M5 7 F28).</summary>
        public long PlanValue { get; private set; }

        /// <summary>M5 7 F30 (the owner): the wands and cantrip pieces the keep-aside rule took, worth a look (M6 14: each once, when held after the pick-up).</summary>
        public int WorthALook { get; private set; }

        private readonly HashSet<uint> _looked = [];

        /// <summary>M5 7 P7s (D148, report-only): the corpses skipped with no Use because they lie in a no-go cell or ACE's straight approach to them crosses one (also counted in <see cref="SkippedUnreached"/>).</summary>
        public int NoGoSkipped { get; private set; }

        /// <summary>M5 7 F27 (report-only): the session-clock seconds spent waiting on ACE's walk to a corpse (from the Use sent to the open, or to giving the corpse up).</summary>
        public double WalkedSeconds { get; private set; }

        /// <summary>M6 6 F3 (<c>m6-resume-loot-queue</c>): the kills whose corpse the step has found and has not handled: the one in hand, the put-off ones and the queued ones that hold a corpse, in the order of the kills.</summary>
        public List<KillCorpse> Pending()
        {
            var list = new List<KillCorpse>();
            void Add(LootKill k)
            {
                if (k.Corpse != 0 && !_handled.Contains(k.Corpse) && !list.Exists(c => c.Guid == k.Corpse))
                {
                    list.Add(new KillCorpse(k.Corpse, k.Name));
                }
            }

            if (_stage != Stage.Idle)
            {
                Add(_kill);
            }

            _retry.ForEach(Add);
            foreach (var k in _kills)
            {
                Add(k);
            }

            return list;
        }

        /// <summary>M6 6 F3: true when the corpse is already spoken for or handled (the seed leaves it).</summary>
        public bool Holds(uint guid) => _handled.Contains(guid) || _corpse == guid && _stage != Stage.Idle || Bound(guid, null);

        /// <summary>A kill is told: its creature's name and where it lay; M5 7 P7b (D139 i): with <paramref name="corpse"/> set (a splash kill paired with its corpse), that corpse and no other is the kill's.</summary>
        public void NoteKill(string name, CellFrame at, uint guid, double clock, uint corpse = 0, uint wcid = 0)
        {
            _kills.Enqueue(new LootKill(name, at, guid, clock, wcid) { Corpse = corpse, CorpseAt = corpse != 0 ? at : null });
            log.Info(LogCategory, corpse != 0 ? $"loot: the splash kill of '{name}' (its corpse 0x{corpse:X8}) waits for the loot step ({_kills.Count} waiting)" : $"loot: the kill of '{name}' 0x{guid:X8} waits for the loot step ({_kills.Count} waiting)");
        }

        /// <summary>M5 7 P7b (D139 i): a splash kill notice met no corpse: named (<paramref name="line"/>) and counted as left.</summary>
        public void NoteNoCorpse(string line)
        {
            Left++;
            log.Warn(LogCategory, line);
        }

        /// <summary>
        /// M5 7 P7b (D139 i): true when <paramref name="corpse"/> is spoken for: handled, the corpse in hand, the corpse a queued or put-off kill holds, or the one an unbound queued kill of its name would find (the target's corpse, not yet
        /// sought). The splash pairing never takes such a corpse.
        /// </summary>
        public bool Reserved(IGameSessionView session, WorldObject corpse)
        {
            var guid = corpse.Guid;
            if (_handled.Contains(guid) || (_corpse != 0 && _corpse == guid) || Bound(guid, null))
            {
                return true;
            }

            if (_stage == Stage.Seek && _kill.Corpse == 0 && Nearest(session, _kill) == guid)
            {
                return true;
            }

            foreach (var k in _kills)
            {
                if (k.Corpse == 0 && Nearest(session, k) == guid)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>One frame. True: the step used the frame (the fight casts nothing now); false: nothing to do, the fight goes on.</summary>
        public bool Step(IGameSessionView session, double clock)
        {
            if (_stage == Stage.Idle)
            {
                if (_kills.Count == 0)
                {
                    return false;
                }

                _kill = _kills.Dequeue();
                _stage = Stage.Seek;
                _stageStart = _corpseStart = clock;
                _corpse = 0;
                _busyRefusals = 0;
                _ownWalked = false;
                _losWalked = false;
                _wantLegs = 0; // M6 14 want loot
                _seekYieldLogged = false;
                _roomSpent = 0; // M6 14 keep every great cantrip: the drops' seconds are the corpse's own extra
            }
            else if (clock - _corpseStart > CorpseSeconds + RoomSeconds(clock))
            {
                Give(session, string.Create(CultureInfo.InvariantCulture, $"the corpse 0x{_corpse:X8} took over {CorpseSeconds:0} s"));
                return true;
            }

            // M6 6 B2 (m5-defend-loot-first, D88): a hit while the corpse is sought, looted or its finds judged is answered too, not only while it is reached or read (the corpse put off, its open view closed first).
            if (_stage is Stage.Seek or Stage.Opening or Stage.BusyWait or Stage.Resolve or Stage.Appraise or Stage.MakingRoom or Stage.Looting or Stage.Wearing && owner._options.LootAggroMetres > 0 && owner.AttackerNow(session, clock) is { } attacker
                && AttackerFirst(session, clock, attacker) is { } answered)
            {
                return answered;
            }

            switch (_stage)
            {
                case Stage.Seek:
                    return Seek(session, clock);
                case Stage.Opening:
                    return Opening(session, clock);
                case Stage.BusyWait:
                    return BusyWait(session, clock);
                case Stage.Resolve:
                    return Resolve(session, clock);
                case Stage.Appraise:
                    return Appraise(session, clock);
                case Stage.MakingRoom:
                    return MakingRoom(session, clock);
                case Stage.Looting:
                    return Looting(session, clock);
                case Stage.Wearing:
                    return Wearing(session, clock);
                case Stage.OwnWalk:
                    return OwnWalk(session, clock);
                case Stage.LosWalk:
                    return LosWalk(session, clock);
                case Stage.WantWalk:
                    return WantWalk(session, clock);
                default:
                    return Closing(clock);
            }
        }

        /// <summary>
        /// The fight ends or fails, or its loot is held (<see cref="HoldLoot"/>, <paramref name="when"/> names it): a walk stops, a loop in hand is abandoned and an open corpse is closed (fire and forget). M6 14 recall unblock: the open
        /// view of a put-off corpse (a cut walk that had arrived: "its view stays for the retry") is closed too, so no corpse's open holds the Use lane the way out needs; the kills not yet looked at are left with the put-off ones.
        /// </summary>
        public void Abandon(IGameSessionView session, string when = "at the fight's end")
        {
            if (_stage != Stage.Idle)
            {
                log.Info(LogCategory, $"loot: abandoned {when} (stage {_stage.ToString().ToLowerInvariant()}, corpse 0x{_corpse:X8})");
                CutWalk(session, session.Clock.Now, "the fight ends", wait: false); // M6 14: what comes after the fight (a recall, a heal) does not wait behind the corpse's walk
                Finish(session, true);
            }
            else if (session.State.Containers.ViewedGuid is var open and not 0 && (open == _corpse || _retry.Exists(k => k.Corpse == open)))
            {
                _ = session.Actions.CloseContainer(open, ActionSource.Automation);
                log.Info(LogCategory, $"loot: the open view of the put-off corpse 0x{open:X8} is closed {when} (the close is sent; the way out's own check reads the view again, m6-loot-hold)");
            }

            if (_kills.Count > 0)
            {
                log.Info(LogCategory, $"loot: {_kills.Count} kill(s) not yet looked at are left {when}");
                _kills.Clear();
            }

            if (_retry.Count > 0)
            {
                Left += _retry.Count;
                log.Info(LogCategory, $"loot: {_retry.Count} corpse(s) still put off {when} are left ({string.Join(", ", _retry.ConvertAll(k => "0x" + k.Corpse.ToString("X8", CultureInfo.InvariantCulture) + (k.OutOfView ? " (lost from view)" : string.Empty)))})");
                _retry.Clear();
            }
        }

        /// <summary>
        /// M5 7 loot retry: once a frame in the Choose phase. A corpse put off whose retry window (<see cref="FightOptions.LootRetryWindowSeconds"/> from the kill) has passed, or that has left the object table (ACE deleted it: it rotted, or something emptied it),
        /// is given up: one log line, counted as left (<c>loot_left_decayed</c>).
        /// </summary>
        /// <remarks>
        /// M5 7 P7b (D139 j): a put-off corpse that leaves the table before its window was lost from view (it cannot have rotted yet): it stays queued with the spot it lay at (<see cref="LoseFromView"/>), is retried as soon as the
        /// server sends it again, and is given up after a walk back reached its spot and <see cref="LootWalkBackSendSeconds"/> passed with no sign of it. A put-off corpse still in the table has its spot kept current.
        /// </remarks>
        public void Sweep(IGameSessionView session, double clock)
        {
            CloseChecked(session, clock);
            for (var i = _retry.Count - 1; i >= 0; i--)
            {
                var k = _retry[i];
                if (clock - k.KilledAt >= FightOptions.LootRetryWindowSeconds)
                {
                    _retry.RemoveAt(i);
                    Decay(k, clock, string.Create(CultureInfo.InvariantCulture, $"its retry window of {FightOptions.LootRetryWindowSeconds:0} s from the kill has passed{(k.OutOfView ? $" (lost from view {clock - k.LostAt:0} s ago)" : string.Empty)}"));
                }
                else if (k.Corpse != 0 && session.State.Objects.TryGet(k.Corpse, out var seen))
                {
                    k.CorpseAt = seen.Position ?? k.CorpseAt;
                    if (k.OutOfView)
                    {
                        k.OutOfView = false;
                        k.SpotReachedAt = double.NaN;
                        var away = session.State.Self.Position is { } own && k.CorpseAt is { } lies ? own.ToGlobal().DistanceTo(lies.ToGlobal()) : double.NaN;
                        log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the corpse 0x{k.Corpse:X8} of '{k.Name}' lost from view {clock - k.LostAt:0} s ago is sent again ({away:0.#} m away); it is retried as any put-off corpse"));
                    }
                }
                else if (k.Corpse != 0 && k.OutOfView)
                {
                    if (!double.IsNaN(k.SpotReachedAt) && clock - k.SpotReachedAt >= LootWalkBackSendSeconds)
                    {
                        _retry.RemoveAt(i);
                        Decay(k, clock, string.Create(CultureInfo.InvariantCulture, $"the walk back reached where it lay and the server did not send it again within {LootWalkBackSendSeconds:0} s (it rotted early or was emptied)"));
                    }
                }
                else if (k.Corpse != 0 && !LoseFromView(session, k, clock))
                {
                    _retry.RemoveAt(i);
                    Decay(k, clock, "it is gone from the world");
                }
            }
        }

        /// <summary>
        /// M5 7 P7b (D139 j): kill <paramref name="k"/>'s corpse is not in the object table. Within its window and with a known spot it was lost from view, not rotted (a monster corpse rots 300 s after the kill, WorldObject_Decay.cs:18;
        /// the client culls an object 25 s out of view and ACE sends no delete, Physics/Common/ObjectMaint.cs:21,41 @95ef426d): it is kept put off (queued in the order of the kills), named once, and true. Else false (the caller gives it up).
        /// </summary>
        private bool LoseFromView(IGameSessionView session, LootKill k, double clock)
        {
            if (k.CorpseAt is not { } lies || clock - k.KilledAt >= FightOptions.LootRetryWindowSeconds)
            {
                return false;
            }

            if (!k.OutOfView)
            {
                k.OutOfView = true;
                k.LostAt = clock;
                k.SpotReachedAt = double.NaN;
                k.WalkBackHeldLogged = false;
                var away = session.State.Self.Position is { } own ? own.ToGlobal().DistanceTo(lies.ToGlobal()) : double.NaN;
                log.Warn(
                    LogCategory,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"loot: the corpse 0x{k.Corpse:X8} of '{k.Name}' (killed {clock - k.KilledAt:0} s ago) left the object table before it could rot: lost from view (the client's cull of an object 25 s out of view; ACE sends no delete, Physics/Common/ObjectMaint.cs:21,41 @95ef426d); remembered at 0x{lies.CellId:X8} [{lies.Origin.X:0.#} {lies.Origin.Y:0.#} {lies.Origin.Z:0.#}], {away:0.#} m from the mage; kept queued: the fight walks back to it when it has no target and nothing is in the way, until {FightOptions.LootRetryWindowSeconds:0} s after the kill (D139 j)"));
            }

            if (!_retry.Contains(k))
            {
                var at = _retry.FindIndex(r => r.KilledAt > k.KilledAt);
                _retry.Insert(at < 0 ? _retry.Count : at, k);
            }

            return true;
        }

        /// <summary>
        /// M5 7 P7b (D139 j): the fight has no target in reach and nothing could be retried: the oldest corpse lost from view, not yet walked back to, whose spot the aggro rule passes, is offered for a walk back. One farther than
        /// <see cref="LootWalkBackMaxMetres"/> is given up, named. A spot the aggro rule holds is logged once and skipped. True with the corpse, its spot and the leg's note.
        /// </summary>
        public bool TryWalkBack(IGameSessionView session, double clock, out uint corpse, out CellFrame spot, out string note)
        {
            (corpse, spot, note) = (0, default, string.Empty);
            if (_stage != Stage.Idle || _kills.Count > 0 || _retry.Count == 0 || session.State.Self.Position is not { } own)
            {
                return false;
            }

            for (var i = 0; i < _retry.Count; i++)
            {
                var k = _retry[i];
                if (!k.OutOfView || !double.IsNaN(k.SpotReachedAt) || k.CorpseAt is not { } lies)
                {
                    continue;
                }

                if (!owner._options.ArenaHere && owner._ground.IsNoGo(lies.CellId))
                {
                    _retry.RemoveAt(i--);
                    NoGoSkipped++;
                    Decay(k, clock, $"lost from view, and where it lay is the no-go cell 0x{lies.CellId:X8}: no walk back goes there, and no Use would be sent from beside it (D148, D141)");
                    continue;
                }

                var away = own.ToGlobal().DistanceTo(lies.ToGlobal());
                var wanted = WantedBy(k);
                if (away > LootWalkBackMaxMetres && (wanted is null || !owner._ground.InLandblocks(lies.CellId)))
                {
                    _retry.RemoveAt(i--);
                    Decay(k, clock, wanted is null
                        ? string.Create(CultureInfo.InvariantCulture, $"lost from view {clock - k.LostAt:0} s ago, and where it lay is {away:0.#} m off, beyond the walk back's {LootWalkBackMaxMetres:0} m (FightScenario.LootWalkBackMaxMetres)")
                        : string.Create(CultureInfo.InvariantCulture, $"lost from view {clock - k.LostAt:0} s ago, {away:0.#} m off, and where it lay (0x{lies.CellId:X8}) is off the ground {owner._ground.Name}: {WantWhy(wanted)}, but no walk back leaves the ground"));
                    continue;
                }

                if (Blocked(session, clock, lies.ToGlobal(), k.Guid, out var why))
                {
                    if (!k.WalkBackHeldLogged)
                    {
                        k.WalkBackHeldLogged = true;
                        log.Info(LogCategory, $"loot: the walk back to the corpse 0x{k.Corpse:X8} of '{k.Name}' waits: {why}");
                    }

                    continue;
                }

                (corpse, spot) = (k.Corpse, lies);
                note = string.Create(CultureInfo.InvariantCulture, $"the corpse 0x{k.Corpse:X8} of '{k.Name}', killed {clock - k.KilledAt:0} s ago, lost from view {clock - k.LostAt:0} s ago at 0x{lies.CellId:X8} [{lies.Origin.X:0.#} {lies.Origin.Y:0.#} {lies.Origin.Z:0.#}], {away:0.#} m off{(wanted is null ? string.Empty : "; " + WantWhy(wanted) + ", walked back whatever the distance on the ground")}");
                return true;
            }

            return false;
        }

        /// <summary>M5 7 P7b (D139 j): the walk back to <paramref name="corpse"/> ended <paramref name="away"/> m from its spot: near enough, the server is given <see cref="LootWalkBackSendSeconds"/> to send it again (<see cref="Sweep"/>); else it is given up, named.</summary>
        public void WalkBackEnded(uint corpse, double clock, double away)
        {
            var i = _retry.FindIndex(k => k.Corpse == corpse);
            if (i < 0)
            {
                return;
            }

            var k = _retry[i];
            if (away <= LootWalkBackArriveMetres)
            {
                k.SpotReachedAt = clock;
                log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the walk back reached where the corpse 0x{corpse:X8} of '{k.Name}' lay ({away:0.#} m from it); the server has {LootWalkBackSendSeconds:0} s to send it again"));
                return;
            }

            _retry.RemoveAt(i);
            Decay(k, clock, string.Create(CultureInfo.InvariantCulture, $"the walk back ended {away:0.#} m from where it lay, beyond {LootWalkBackArriveMetres:0} m (its leg failed or was cut; the leg's line says why)"));
        }

        /// <summary>M5 7 P7b (D139 j, g): the walk back to <paramref name="corpse"/> is given up for <paramref name="why"/>: the corpse is left to decay, named.</summary>
        public void WalkBackGivenUp(uint corpse, double clock, string why)
        {
            var i = _retry.FindIndex(k => k.Corpse == corpse);
            if (i < 0)
            {
                return;
            }

            var k = _retry[i];
            _retry.RemoveAt(i);
            Decay(k, clock, why);
        }

        /// <summary>
        /// M5 7 loot retry: the fight has no target in reach (called from its pick). The oldest put-off corpse that the aggro rule no longer stands in the way of is looted now. True when one was started.
        /// </summary>
        public bool TryRetry(IGameSessionView session, double clock)
        {
            if (_stage != Stage.Idle || _kills.Count > 0 || _retry.Count == 0 || session.State.Self.Position is not { } own)
            {
                return false;
            }

            foreach (var k in _retry)
            {
                if (!session.State.Objects.TryGet(k.Corpse, out var corpse) || corpse.Position is not { } lies || Blocked(session, clock, lies.ToGlobal(), k.Guid, out _)
                    || WalkHeld(session, clock, in own, own.ToGlobal().DistanceTo(lies.ToGlobal()), k.Guid, out _)) // M6 14 (m6-loot-walk-waits): the same check the retry's own look would make
                {
                    continue;
                }

                _retry.Remove(k);
                _kill = k;
                _stage = Stage.Seek;
                _stageStart = _corpseStart = clock;
                _corpse = 0;
                _busyRefusals = 0;
                _ownWalked = false;
                _losWalked = false;
                _wantLegs = 0; // M6 14 want loot
                _seekYieldLogged = false;
                log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the corpse 0x{k.Corpse:X8} of '{k.Name}' put off {clock - k.KilledAt:0} s after its kill is retried: no target in reach and nothing in the way ({_retry.Count} more put off)"));
                return Seek(session, clock);
            }

            return false;
        }

        /// <summary>The aggro rule: true (and why) when a creature is attacking the mage now or a living target creature stands within <c>--fight-loot-aggro-m</c> of <paramref name="corpse"/>. 0 m turns the whole rule off.</summary>
        private bool Blocked(IGameSessionView session, double clock, GlobalPosition corpse, uint except, out string why)
        {
            why = string.Empty;
            var reach = owner._options.LootAggroMetres;
            if (reach <= 0)
            {
                return false;
            }

            if (owner.AttackerNow(session, clock) is { } attacker)
            {
                why = string.Create(CultureInfo.InvariantCulture, $"{attacker.Name} 0x{attacker.Guid:X8} attacked the mage {clock - attacker.LastAt:0.#} s ago");
                return true;
            }

            var hostile = owner.HostileBeside(session, corpse, reach, except, out var at);
            if (hostile == 0)
            {
                return false;
            }

            var name = session.State.Objects.TryGet(hostile, out var h) ? h.Name ?? "creature" : "creature";
            why = string.Create(CultureInfo.InvariantCulture, $"{name} 0x{hostile:X8} stands {at:0.#} m from the corpse (aggro reach {reach:0.#} m, --fight-loot-aggro-m)");
            return true;
        }

        /// <summary>A kill's corpse is given up to decay: counted as left and as decayed, one log line.</summary>
        private void Decay(LootKill k, double clock, string why)
        {
            Left++;
            DecayedLeft++;
            LootTally.AddLeftDecayed();
            log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the corpse 0x{k.Corpse:X8} of '{k.Name}' (killed {clock - k.KilledAt:0} s ago) is given up, left to decay: {why}"));
        }

        /// <summary>
        /// M5 7 loot retry: the corpse in hand is put off by the aggro rule (<paramref name="why"/>): counted, put back in the queue in the order of the kills (not marked handled), the walk or the read abandoned, an open view closed fire and forget.
        /// A corpse already past its window is given up instead.
        /// </summary>
        private void Requeue(IGameSessionView session, double clock, string why)
        {
            CutWalk(session, clock, "the corpse is put off", wait: true); // M6 14 (m6-loot-walk-cut): ACE's walk to it ends now, so the fight's cast does not wait behind the Use
            if (_stage == Stage.Opening && _aceWalk)
            {
                WalkedSeconds += Math.Max(0, clock - _stageStart);
            }

            if (_stage == Stage.Looting && _looter is { } partial && _plan is not null)
            {
                // M6 6 B2 (m5-defend-loot-first): the pick-ups already confirmed are kept and counted now (the resupply's sale list); the retry plans what the corpse still holds.
                var (kept, value) = TallyKept(session, partial);
                Kept += kept;
                Value += value;
                log.Info(LogCategory, $"loot: the corpse 0x{_corpse:X8} was being looted: kept {kept} of {_plan.Kept.Count} planned so far (value {value}); the retry plans what it still holds");
            }

            StackMerges += _looter?.StackMerges ?? 0;
            _looter?.Abandon("fight-moved-on");
            _looter = null;
            _identifies.Clear();
            CloseFirst(session, clock);
            _pending = null;
            _stage = Stage.Idle;
            _corpse = 0;
            _plan = null;
            AggroLeft++;
            LootTally.AddLeftAggro();
            if (!_kill.Counted)
            {
                _kill.Counted = true;
                SkippedHostile++;
            }

            if (clock - _kill.KilledAt >= FightOptions.LootRetryWindowSeconds)
            {
                Decay(_kill, clock, $"put off ({why}) and its retry window has passed");
                return;
            }

            var at = _retry.FindIndex(k => k.KilledAt > _kill.KilledAt);
            _retry.Insert(at < 0 ? _retry.Count : at, _kill);
            log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the corpse 0x{_kill.Corpse:X8} of '{_kill.Name}' is put off: {why}; it stays queued ({_retry.Count} put off), retried when the fight has no target and nothing is in the way, until {FightOptions.LootRetryWindowSeconds:0} s after the kill; the fight goes first"));
        }

        /// <summary>
        /// M6 6 B2 (<c>m5-defend-loot-first</c>, D88): a creature attacked the mage while the corpse in hand was sought, reached, read, looted or its finds judged: the fight answers first. Sought with no corpse yet, the frame goes to
        /// the fight (false) while the corpse is still waited for (the miss after <see cref="CorpseAppearSeconds"/> is the seek's own, null); once it is there it is bound and put off. Looted, with only the finds' judging left, it is
        /// closed and done (nothing is left in it to retry), the finds not yet judged staying in the pack. Else it is put off (<see cref="Requeue"/>). True or false: the frame is answered; null: the stage goes on.
        /// </summary>
        private bool? AttackerFirst(IGameSessionView session, double clock, Attacker attacker)
        {
            var who = string.Create(CultureInfo.InvariantCulture, $"{attacker.Name} 0x{attacker.Guid:X8} attacked the mage {clock - attacker.LastAt:0.#} s ago");
            switch (_stage)
            {
                case Stage.Seek when _kill.Corpse == 0:
                    if (!Find(session, out var found))
                    {
                        if (clock - _stageStart > CorpseAppearSeconds)
                        {
                            return null; // the seek names the miss
                        }

                        if (!_seekYieldLogged)
                        {
                            _seekYieldLogged = true;
                            log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the corpse of '{_kill.Name}' has not appeared yet and {who}: the fight answers it first; the corpse is still waited for (up to {CorpseAppearSeconds:0} s)"));
                        }

                        return false;
                    }

                    _corpse = _kill.Corpse = found.Guid;
                    _kill.CorpseAt = found.Position ?? _kill.CorpseAt;
                    break;
                case Stage.Wearing when _swap is { } swap && clock - swap.Start <= SwapSeconds:
                    // M6 14 capacity: a replace in hand finishes before the fight goes first (a piece taken off is never left off): its own steps run on, up to SwapSeconds
                    if (!_wearWaitLogged)
                    {
                        _wearWaitLogged = true;
                        log.Info(LogCategory, $"loot: {who} while a {swap.Name} of '{swap.Choice.Name}' 0x{swap.Choice.Guid:X8} was in hand: it finishes first (up to {SwapSeconds:0} s), then the fight goes first");
                    }

                    return null;
                case Stage.Wearing:
                    // M6 6 FXD3 (6-D9; check, act, check, M5 D141): a wield already sent is read before the stage ends (up to ActionSeconds), so the fight's magic-mode ask never races a wield still in flight, and what was worn is logged.
                    if (_pending is { Outcome: null } && clock - _stageStart <= ActionSeconds)
                    {
                        if (!_wearWaitLogged)
                        {
                            _wearWaitLogged = true;
                            log.Info(LogCategory, $"loot: {who} while a wield (0x{_wearingGuid:X8}) was in flight: its outcome is read first (up to {ActionSeconds:0} s), then the fight goes first");
                        }

                        return true;
                    }

                    _wearWaitLogged = false;
                    SwapCut(session, who); // M6 16 E5e4: a swap still in hand past its seconds: what it has off is named, loudly
                    if (_pending is not null)
                    {
                        log.Info(LogCategory, $"loot: gear: the wield ended {(_pending.Outcome is { } ended ? Describe(ended) : "unanswered")} (read before the fight went first)");
                        if (_pending.Outcome is { Kind: ActionOutcomeKind.Confirmed })
                        {
                            PackTally.AddEquip();
                        }
                    }

                    log.Warn(LogCategory, $"loot: {who} while the finds of the corpse 0x{_corpse:X8} were judged for the self-equip: the corpse is looted and done; {_wear.Count} find(s) not yet judged stay in the pack; the fight goes first");
                    _wear.Clear();
                    CloseFirst(session, clock);
                    _pending = null;
                    Done();
                    return true;
                case Stage.MakingRoom when RoomAttacked(session, clock, who):
                    return true; // M6 14 keep every great cantrip: a drop in flight is read to its end first
            }

            var what = _stage switch
            {
                Stage.Seek => "sought",
                Stage.MakingRoom => "readied (room made for a great cantrip)",
                Stage.Looting => "looted",
                _ => "reached or read",
            };
            Requeue(session, clock, $"{who} while the corpse was being {what}{(WantedBy(_kill) is { } want ? "; " + WantWhy(want) + ", so it is walked to once nothing is in the way" : string.Empty)}"); // M6 14 want loot: the put-off names the want
            return true;
        }

        /// <summary>
        /// M6 6 B2 (<c>m5-defend-loot-first</c>; check, act, check, M5 D141): the corpse in hand's view is closed before the fight goes first. Check: the view shows it open; act: the close is sent (fire and forget, the fight does not
        /// wait); check: <see cref="CloseChecked"/> reads the view on the next frames. Nothing is sent when no view of it is open.
        /// </summary>
        private void CloseFirst(IGameSessionView session, double clock)
        {
            if (_corpse == 0 || session.State.Containers.Viewed is not { } view || view.Guid != _corpse)
            {
                return;
            }

            _ = session.Actions.CloseContainer(_corpse, ActionSource.Automation);
            (_closeSent, _closeSentAt) = (_corpse, clock);
            log.Info(LogCategory, $"loot: the open corpse 0x{_corpse:X8} is closed first (its view was open; the close is sent, the view is read again)");
        }

        /// <summary>M6 6 B2: the after-check of <see cref="CloseFirst"/>: the view no longer shows the corpse (confirmed, logged), or after <see cref="ActionSeconds"/> it still does (named; nothing is sent again: the next open replaces the view).</summary>
        private void CloseChecked(IGameSessionView session, double clock)
        {
            if (_closeSent == 0)
            {
                return;
            }

            if (session.State.Containers.Viewed is not { } view || view.Guid != _closeSent)
            {
                log.Info(LogCategory, $"loot: the corpse 0x{_closeSent:X8} is closed (the view confirms it)");
                _closeSent = 0;
            }
            else if (clock - _closeSentAt > ActionSeconds)
            {
                log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the corpse 0x{_closeSent:X8} is still open {ActionSeconds:0} s after its close was sent; not sent again (the next open of a corpse replaces the view)"));
                _closeSent = 0;
            }
        }

        /// <summary>The plan's keeps the looter's pick-ups made held (or merged wholly into a held stack), added to <see cref="KeptGuids"/>: the count and their Value.</summary>
        private (int Kept, long Value) TallyKept(IGameSessionView session, CorpseLooter looter)
        {
            var kept = 0;
            long value = 0;
            foreach (var guid in _plan!.Kept)
            {
                if (session.State.Self.Inventory.Find(guid).Held)
                {
                    kept++;
                    value += _plan.ValueOf(guid);
                    KeptGuids.Add(guid);
                }
                else if (looter.WasMergedAway(guid))
                {
                    kept++; // picked up, then wholly merged into a held stack: the guid is gone, the items are held
                    value += _plan.ValueOf(guid);
                }
            }

            return (kept, value);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
        }

        private bool Seek(IGameSessionView session, double clock)
        {
            if (!Find(session, out var corpse))
            {
                if (_kill.Corpse != 0)
                {
                    if (LoseFromView(session, _kill, clock))
                    {
                        _stage = Stage.Idle; // M5 7 P7b (D139 j): kept queued with its spot; not handled, so it can be found again when the server sends it
                        _corpse = 0;
                        return true;
                    }

                    Decay(_kill, clock, "it is gone from the world");
                    Finish(session, false);
                    return true;
                }

                if (clock - _stageStart > CorpseAppearSeconds)
                {
                    Left++;
                    log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: no '{MageLootRules.CorpseNameOf(_kill.Name)}' within {CorpseRangeMetres:0} m of where '{_kill.Name}' 0x{_kill.Guid:X8} lay appeared within {CorpseAppearSeconds:0} s; counted, the fight goes on"));
                    Finish(session, false);
                }

                return true;
            }

            _corpse = _kill.Corpse = corpse.Guid;
            _kill.CorpseAt = corpse.Position ?? _kill.CorpseAt;
            _corpseName = corpse.Name;
            if (MageLootRules.IsOwnCorpse(corpse.Name, session.State.Self.Name))
            {
                Left++;
                log.Warn(LogCategory, $"loot: '{corpse.Name}' 0x{corpse.Guid:X8} is the player's own corpse; it is the death handler's, not looted here");
                Finish(session, false);
                return true;
            }

            if (session.State.Self.Position is not { } own || corpse.Position is not { } lies)
            {
                Left++;
                log.Warn(LogCategory, $"loot: the corpse 0x{corpse.Guid:X8} or the player has no position; the corpse is left");
                Finish(session, false);
                return true;
            }

            var away = own.ToGlobal().DistanceTo(lies.ToGlobal());
            var options = owner._options;
            if (WantedBy(_kill) is { } want && away > HuntScenario.CorpseReachMetres && (away > owner.LootRadius || !owner.LineIsClearTo(in own, in lies)))
            {
                return WantWalkFrom(session, clock, corpse.Guid, in own, in lies, away, want); // M6 14 want loot: a carrier's corpse is walked to whatever its distance on the ground
            }

            if (away > owner.LootRadius)
            {
                SkippedUnreached++;
                LootTally.AddLeftUnreached();
                Left++;
                log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the corpse 0x{corpse.Guid:X8} of '{_kill.Name}' is {away:0.#} m away, beyond the loot radius ({owner.LootRadius:0.#} m, --fight-loot-radius-m); left, not walked to"));
                Finish(session, false);
                return true;
            }

            if (Blocked(session, clock, lies.ToGlobal(), _kill.Guid, out var blockedBy))
            {
                Requeue(session, clock, blockedBy);
                return true;
            }

            if (WalkHeld(session, clock, in own, away, _kill.Guid, out var heldBy))
            {
                WalkWaits++; // M6 14 (m6-loot-walk-waits): no walk to a corpse begins with a hostile in reach of her; the corpse waits like any put-off one
                Requeue(session, clock, heldBy);
                return true;
            }

            var los = owner.LineIsClearTo(in own, in lies);
            var losText = los ? "los=yes" : "los=no";
            if (away <= HuntScenario.CorpseReachMetres)
            {
                log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the corpse 0x{corpse.Guid:X8} of '{_kill.Name}' is {away:0.#} m away, within reach ({HuntScenario.CorpseReachMetres:0.#} m): no walk; opening it ({losText})"));
                Open(session, false);
                return true;
            }

            if (!los)
            {
                // M6 6 B2 (m5-loot-walk-for-los): before the corpse is left for its line, the fight's own walk goes once to a floor spot within the loot radius that has one (the step's sweep, FightScenario.SelfDefence.cs).
                var noWalk = "the walk for a line went, and it still has none";
                if (!_losWalked)
                {
                    _losWalked = true;
                    if (owner.LootLosSpot(in own, in lies, out var spot, out var spotNote))
                    {
                        (_losWalkStarted, _losWalkSpot, _losWalkNote) = (false, spot, spotNote);
                        _stage = Stage.LosWalk;
                        _stageStart = clock;
                        log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the corpse 0x{corpse.Guid:X8} of '{_kill.Name}' is {away:0.#} m away with no clear line of sight to it ({losText}): the fight's own walk goes first (once) to {spotNote}; then it is looked at again (m5-loot-walk-for-los)"));
                        return true;
                    }

                    noWalk = "no walk for a line: " + spotNote;
                }

                SkippedUnreached++;
                LootTally.AddLeftUnreached();
                Left++;
                log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the corpse 0x{corpse.Guid:X8} of '{_kill.Name}' is {away:0.#} m away with no clear line of sight to it ({losText}); {noWalk}; left, not walked to by ACE (its straight-line walk could run the mage into a wall)"));
                Finish(session, false);
                return true;
            }

            if (NoGoApproach(session, in own, in lies, away))
            {
                return true; // M5 7 P7s (D148, D141): skipped with no Use, or the fight's own walk goes round first
            }

            log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the corpse 0x{corpse.Guid:X8} of '{_kill.Name}' is {away:0.#} m away, beyond reach ({HuntScenario.CorpseReachMetres:0.#} m) ({losText}): the Use goes and ACE's own MoveTo walks the mage there (Player_Use.cs:184-206, Player_Move.cs:37-96 @95ef426d), up to {options.LootWalkSeconds:0} s"));
            Open(session, true);
            return true;
        }

        /// <summary>
        /// M5 7 P7s (D148, D141; <c>m5-8-loot-no-go-corpse</c>): checked before a Use of a corpse beyond reach, because that Use lets ACE's own MoveTo walk the mage straight to it (<c>Player_Use.cs:184-206</c>,
        /// <c>Player_Move.cs:37-96</c> @95ef426d) and the fight's no-go map cannot stop the server's walk: both no-go entries of the long run of 2026-10-30 (19:43 and 22:43 UTC, the den's 0x01050244) were that walk to a Virindi
        /// Servant's corpse. A corpse in a no-go cell is skipped, named once, with no Use. Else ACE's straight approach is traced (<see cref="IFightWalk.CellsOnLine"/>): crossing a no-go cell, or not traceable to its end, the fight's
        /// own walk (planned over the walkable cells only) goes toward the corpse once and the corpse is looked at again from there; still crossing, it is skipped with no Use. True when the step used the frame (no Use from here).
        /// </summary>
        private bool NoGoApproach(IGameSessionView session, in CellFrame own, in CellFrame lies, double away)
        {
            var ground = owner._ground;
            if (owner._options.ArenaHere)
            {
                return false;
            }

            if (ground.IsNoGo(lies.CellId))
            {
                SkipNoGo(session, string.Create(CultureInfo.InvariantCulture, $"it lies in the no-go cell 0x{lies.CellId:X8} of {ground.Name}, {away:0.#} m away"));
                return true;
            }

            if (owner._walk is not { } walk)
            {
                return false;
            }

            var trace = walk.CellsOnLine(in own, in lies, _approach);
            if (trace == LineTrace.NotTraced)
            {
                if (!_traceTodoLogged)
                {
                    _traceTodoLogged = true;
                    log.Warn(LogCategory, "loot: TODO(m5-loot-approach-trace): this fight's walk cannot trace ACE's straight approach to a corpse; only the corpse's own cell is checked against the no-go cells");
                }

                return false;
            }

            uint crossed = 0;
            foreach (var cell in _approach)
            {
                if (ground.IsNoGo(cell))
                {
                    crossed = cell;
                    break;
                }
            }

            if (crossed == 0 && trace == LineTrace.Traced)
            {
                log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: ACE's straight approach to the corpse 0x{_corpse:X8} of '{_kill.Name}' crosses no no-go cell ({_approach.Count} cell(s), 0x{_approach[0]:X8} to 0x{_approach[^1]:X8}); the Use may walk"));
                return false;
            }

            var why = crossed != 0
                ? $"ACE's straight approach from 0x{own.CellId:X8} crosses the no-go cell 0x{crossed:X8} of {ground.Name}"
                : $"ACE's straight approach from 0x{own.CellId:X8} could not be traced past 0x{(_approach.Count > 0 ? _approach[^1] : own.CellId):X8} (the sweep was stopped), so it may cross a no-go cell";
            if (!_ownWalked)
            {
                _ownWalked = true;
                _ownWalkStarted = false;
                _ownWalkSpot = lies;
                _stage = Stage.OwnWalk;
                _stageStart = session.Clock.Now;
                log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the corpse 0x{_corpse:X8} of '{_kill.Name}' is {away:0.#} m away and {why}: no Use from here; the fight's own walk, planned over the walkable cells only, goes toward it first (once)"));
                return true;
            }

            SkipNoGo(session, why + ", still, after the fight's own walk toward it");
            return true;
        }

        /// <summary>M5 7 P7s (D148): the corpse in hand is skipped for the no-go map: named once, counted (<see cref="NoGoSkipped"/>, and as unreached), no Use sent; the fight goes on.</summary>
        private void SkipNoGo(IGameSessionView session, string why)
        {
            NoGoSkipped++;
            SkippedUnreached++;
            LootTally.AddLeftUnreached();
            Left++;
            log.Warn(LogCategory, $"loot: the corpse 0x{_corpse:X8} of '{_kill.Name}' is skipped, no Use: {why}; ACE's own MoveTo cannot be kept off a no-go cell (D141, m5-8-loot-no-go-corpse); left, the fight goes on");
            Finish(session, false);
        }

        /// <summary>
        /// M5 7 P7s (D148): the fight's own walk toward the corpse in hand. The first frame begins the leg (the fight's Walk phase then runs it); the next frame the step is given (the leg arrived, was given up, or was dropped for an
        /// attacker or a heal) looks at the corpse again from where the mage stands: within reach it is opened with no walk, a clear approach lets ACE walk the rest, one still crossing a no-go cell is skipped.
        /// </summary>
        private bool OwnWalk(IGameSessionView session, double clock)
        {
            if (!_ownWalkStarted)
            {
                _ownWalkStarted = true;
                var note = string.Create(CultureInfo.InvariantCulture, $"the corpse 0x{_corpse:X8} of '{_kill.Name}' at 0x{_ownWalkSpot.CellId:X8} [{_ownWalkSpot.Origin.X:0.#} {_ownWalkSpot.Origin.Y:0.#} {_ownWalkSpot.Origin.Z:0.#}]");
                if (!owner.BeginLootWalk(session, clock, _corpse, in _ownWalkSpot, note, OwnWalkWhy, out var why))
                {
                    SkipNoGo(session, "ACE's straight approach may cross a no-go cell and no walk of the fight's own could be planned to it: " + why);
                }

                return true;
            }

            log.Info(LogCategory, $"loot: the fight's own walk toward the corpse 0x{_corpse:X8} of '{_kill.Name}' ended; it is looked at again from where the mage stands");
            _stage = Stage.Seek;
            _stageStart = clock;
            return Seek(session, clock);
        }

        private const string OwnWalkWhy = "ACE's straight approach would cross a no-go cell, so the fight's own walk goes round over the walkable cells (D141, m5-8-loot-no-go-corpse)";

        private const string LosWalkWhy = "the corpse has no clear line of sight from where the mage stood, so the fight's own walk goes to a spot with one (m5-loot-walk-for-los)";

        /// <summary>
        /// M6 6 B2 (<c>m5-loot-walk-for-los</c>): the fight's own walk to the spot with a line (<see cref="FightScenario.LootLosSpot"/>), once a corpse, as <see cref="OwnWalk"/>: the first frame begins the leg (the Walk phase runs it;
        /// an attacker or a heal may drop it); the next frame the step is given looks at the corpse again from where the mage stands. A leg that cannot be planned leaves the corpse, named.
        /// </summary>
        private bool LosWalk(IGameSessionView session, double clock)
        {
            if (!_losWalkStarted)
            {
                _losWalkStarted = true;
                if (!owner.BeginLootWalk(session, clock, _corpse, in _losWalkSpot, $"the corpse 0x{_corpse:X8} of '{_kill.Name}': {_losWalkNote}", LosWalkWhy, out var why))
                {
                    SkippedUnreached++;
                    LootTally.AddLeftUnreached();
                    Left++;
                    log.Warn(LogCategory, $"loot: the corpse 0x{_corpse:X8} of '{_kill.Name}' has no clear line of sight and the walk for a line could not be planned ({why}); left, not walked to");
                    Finish(session, false);
                }

                return true;
            }

            log.Info(LogCategory, $"loot: the walk for a line to the corpse 0x{_corpse:X8} of '{_kill.Name}' ended; it is looked at again from where the mage stands");
            _stage = Stage.Seek;
            _stageStart = clock;
            return Seek(session, clock);
        }

        private void Open(IGameSessionView session, bool aceWalk)
        {
            if (_closeSent == _corpse)
            {
                _closeSent = 0; // opened again before the close's check: the new open owns the view
            }

            _aceWalk = aceWalk;
            _pending = session.Actions.OpenContainer(_corpse, ActionSource.Automation);
            _stage = Stage.Opening;
            _stageStart = session.Clock.Now;
        }

        /// <summary>M5 7 F27: a corpse ACE's walk did not bring the mage to: named once (<c>loot: ... not reached: why</c>), counted, the fight resumes; an open view (the walk may have ended at the corpse) is closed.</summary>
        private void NotReached(IGameSessionView session, double clock, string why)
        {
            WalkedSeconds += Math.Max(0, clock - _stageStart);
            SkippedUnreached++;
            LootTally.AddLeftUnreached();
            Left++;
            log.Warn(LogCategory, $"loot: '{_corpseName}' 0x{_corpse:X8} not reached: {why}; the corpse is left, the fight goes on");
            Finish(session, true);
        }

        private bool Opening(IGameSessionView session, double clock)
        {
            if (_pending is not { Outcome: { } opened })
            {
                var limit = _aceWalk ? owner._options.LootWalkSeconds : ActionSeconds;
                if (clock - _stageStart > limit)
                {
                    if (_aceWalk)
                    {
                        NotReached(session, clock, string.Create(CultureInfo.InvariantCulture, $"ACE's move-to brought no open within {limit:0} s"));
                    }
                    else
                    {
                        Give(session, $"the open of 0x{_corpse:X8} got no answer within {ActionSeconds:0} s");
                    }
                }

                return true;
            }

            _pending = null;
            if (_aceWalk)
            {
                WalkedSeconds += Math.Max(0, clock - _stageStart);
            }

            if (opened.Kind == ActionOutcomeKind.ServerError && opened.Code == WeenieErrorText.YoureTooBusy)
            {
                Busy(session, clock, opened);
                return true;
            }

            if (opened.Kind != ActionOutcomeKind.Confirmed)
            {
                if (_aceWalk)
                {
                    SkippedUnreached++;
                    LootTally.AddLeftUnreached();
                    Left++;
                    log.Warn(LogCategory, $"loot: '{_corpseName}' 0x{_corpse:X8} not reached: the open was not confirmed ({Describe(opened)}); the corpse is left, the fight goes on");
                    Finish(session, false);
                    return true;
                }

                Left++;
                log.Warn(LogCategory, $"loot: the open of 0x{_corpse:X8} was not confirmed ({Describe(opened)}); counted, the fight goes on");
                Finish(session, false);
                return true;
            }

            if (session.State.Containers.Viewed is not { } view || view.Guid != _corpse)
            {
                Left++;
                log.Warn(LogCategory, $"loot: the open of 0x{_corpse:X8} was confirmed but the viewed container is {(session.State.Containers.Viewed is { } other ? "0x" + other.Guid.ToString("X8", CultureInfo.InvariantCulture) : "none")}; the corpse is left");
                Finish(session, false);
                return true;
            }

            Opened++;
            LootTally.AddOpened();
            log.Info(LogCategory, _busyRefusals > 0
                ? string.Create(CultureInfo.InvariantCulture, $"loot: 0x{_corpse:X8} holds {view.Entries.Count} item(s) (opened after {_busyRefusals} busy refusal(s))")
                : $"loot: 0x{_corpse:X8} holds {view.Entries.Count} item(s)");
            _stage = Stage.Resolve;
            _stageStart = clock;
            return true;
        }

        /// <summary>
        /// M5 7 D123: the open was answered "You're too busy!" (0x001D, a UseDone with no ViewContents): ACE's player was still busy with the last pick-up or a cast (Player_Use.cs:199-203 @95ef426d), and nothing walked. The corpse is
        /// kept in hand and opened again after <see cref="BusyWaitSeconds"/>, up to <see cref="BusyRetries"/> times; every refusal and every new open is logged. Past that it is left for a named reason (<c>loot_left_busy</c>). The fight
        /// waits meanwhile (the step holds the frame): it never walks off from a corpse it can open.
        /// </summary>
        private void Busy(IGameSessionView session, double clock, in ActionOutcome refused)
        {
            _busyRefusals++;
            if (_busyRefusals > BusyRetries)
            {
                BusyLeft++;
                LootTally.AddLeftBusy();
                Left++;
                log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: '{_corpseName}' 0x{_corpse:X8} left: busy (0x001D \"You're too busy!\") {_busyRefusals} times in a row, {BusyRetries} new opens {BusyWaitSeconds:0.#} s apart spent ({Describe(refused)}); counted loot_left_busy, the fight goes on"));
                Finish(session, false);
                return;
            }

            BusyWaits++;
            LootTally.AddBusyWait();
            _stage = Stage.BusyWait;
            _stageStart = clock;
            log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the open of '{_corpseName}' 0x{_corpse:X8} was answered busy (0x001D \"You're too busy!\": the last pick-up or cast is still finishing, Player_Use.cs:199-203 @95ef426d); refusal {_busyRefusals}, opened again in {BusyWaitSeconds:0.#} s (at most {BusyRetries} new opens); the fight waits, no walk"));
        }

        /// <summary>M5 7 D123: the wait after a busy refusal. A corpse gone from the world meanwhile (rotted, or emptied) is given up as decayed; else the open is sent again once <see cref="BusyWaitSeconds"/> passed.</summary>
        private bool BusyWait(IGameSessionView session, double clock)
        {
            if (!session.State.Objects.TryGet(_corpse, out _))
            {
                Decay(_kill, clock, "it is gone from the world (while the open waited out a busy refusal)");
                Finish(session, false);
                return true;
            }

            if (clock - _stageStart < BusyWaitSeconds)
            {
                return true;
            }

            log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the open of '{_corpseName}' 0x{_corpse:X8} is sent again after a busy refusal (new open {_busyRefusals} of at most {BusyRetries}{(_aceWalk ? ", ACE's MoveTo walks the mage there" : string.Empty)})"));
            Open(session, _aceWalk);
            return true;
        }

        private bool Resolve(IGameSessionView session, double clock)
        {
            if (session.State.Containers.Viewed is not { } view || view.Guid != _corpse)
            {
                Left++;
                log.Warn(LogCategory, $"loot: the view of 0x{_corpse:X8} went away before it was read; the corpse is left");
                Finish(session, false);
                return true;
            }

            if (!view.IsResolved && clock - _stageStart <= ResolveSeconds)
            {
                return true;
            }

            // The items whose description sent no Value are appraised (session.md section 38) before the rule reads them; money is never asked.
            _identifies.Clear();
            foreach (var entry in view.Entries)
            {
                if (entry.Resolved && session.State.Objects.TryGet(entry.Guid, out var item) && (item.ItemType & SellRules.ItemTypeMoney) == 0
                    && (item.Weenie.Value is null || (rules.WantsKeepAsideAppraisal(item.ItemType) && !session.State.Appraisals.TryGet(entry.Guid, out _))))
                {
                    log.Info(LogCategory, item.Weenie.Value is null ? $"loot: '{item.Name}' 0x{entry.Guid:X8} has no Value in its description; appraising it" : $"loot: '{item.Name}' 0x{entry.Guid:X8} is a caster or a wearable; appraising it for the keep-aside rule (M5 7 F30)");
                    _identifies.Add(session.Actions.Identify(entry.Guid, ActionSource.Automation));
                }
            }

            _stage = Stage.Appraise;
            _stageStart = clock;
            return Appraise(session, clock);
        }

        private bool Appraise(IGameSessionView session, double clock)
        {
            if (clock - _stageStart <= AppraiseSeconds && _identifies.Exists(h => h.Outcome is null))
            {
                return true;
            }

            _identifies.Clear();
            var state = session.State;
            if (state.Containers.Viewed is not { } view || view.Guid != _corpse)
            {
                Left++;
                log.Warn(LogCategory, $"loot: the view of 0x{_corpse:X8} went away before it was planned; the corpse is left");
                Finish(session, false);
                return true;
            }

            // M5 7 D149 (the owner): a heavy find worth a look is judged by the self-equip against what the mage wears now, on the appraisal just read; it is kept only as a wearable upgrade.
            var gearJudge = GearJudge(session);
            var worn = SelfEquipJudge.Worn(state);
            var skills = new QualitiesSkills(state.Self.Qualities);
            var items = new List<MageLootItem>(view.Entries.Count);
            var cantrips = CantripJudge.Of(state); // M6 14 useful cantrips (the owner, 2026-11-16): a great cantrip keeps its piece only when it is useful to her, her skills read live
            foreach (var entry in view.Entries)
            {
                if (entry.Resolved && state.Objects.TryGet(entry.Guid, out var item))
                {
                    var appraised = state.Appraisals.TryGet(entry.Guid, out var appraisal) ? appraisal : null;
                    items.Add(rules.ItemFor(item, appraised, state.Self.Inventory.Find(entry.Guid).Held, c => gearJudge.Judge(c, worn, skills), cantrips));
                    if (KeepAsideRule.NeedsAppraisal(item.ItemType) && cantrips.GreatOf(appraised, rules.SpellName) is { Count: > 0 } verdicts && CantripVerdictLog.Once(entry.Guid, item.Name, verdicts, "the corpse") is { } line)
                    {
                        log.Write(CantripVerdictLog.HasUnread(verdicts) ? LogSeverity.Warning : LogSeverity.Info, LogCategory, line, []);
                    }
                }
            }

            var context = LootContext.Of(state);
            _packRules ??= new PackRules(owner._ground);
            var layout = PackLayout.Of(state, _packRules, g => CantripRoom.IsGreatPiece(state, g, owner._spells), PackMemory.Shared.Rotation.Contains); // M5 7 D121: each kept item to the pack of its job; the review pack holds the great-cantrip pieces only (the owner, 2026-11-16)
            layout.Publish();
            var sellLoot = RoomCandidates(state, items); // M6 14 keep every great cantrip and the hour load's swap: the sell loot she may drop to make room, read for a corpse with a find that may need it
            var baseLine = rules.Thresholds.LoadLinePercent;
            var cycle = CycleLoad.For(state);
            var loadLine = cycle.LineFor(baseLine, owner._ground.Jumps.Count > 0, session.Clock.Now); // M6 14 hour load: the cycle's rising line on a comfortable ground, else the base line
            _plan = rules.Plan(_corpseName, state.Self.Name, items, context.Burden, context.Capacity, Quartermaster.PackView.MainPackFree(state), layout, _packRules, ItemManaKeeper.StonesWanted(state), sellLoot, loadLine); // M5 7 S2b: the kit's stones are kept under the floors
            foreach (var line in _plan.DecisionLines())
            {
                log.Info(LogCategory, line);
            }

            foreach (var line in _plan.WorthALookLines())
            {
                log.Info(LogCategory, line);
            }

            foreach (var guid in _plan.KeptAsideGuids)
            {
                SaleMemory.Shared.KeptAside.Add(guid); // M5 7 F30: the sale never offers it (M6 14: the scrolls kept and the quests' wants too; the soak line counts the rule's finds alone, once held: the done stage)
            }
            PlanKept += _plan.KeptCount;
            PlanLeft += _plan.LeftCount;
            LootTally.Add(_plan.KeptCount, _plan.LeftCount); // M5 7 F29 (D96): the soak line's loot_kept= and loot_left=
            PackRoomMemory.For(state).NoteLoot(_plan); // M6 14 pack room (m6-pack-room): the items left over the load line call the full-pack trip; with the kept burden they are the loot's room at the next visit
            PlanValue += _plan.KeptValue;
            log.Info(LogCategory, $"loot: plan for 0x{_corpse:X8} ({rules.Thresholds}; burden {context.Burden} of {context.Capacity}, {_plan.FreeSlots} free slot(s); {layout.SlotsLine()}{(cycle.Begun ? "; " + cycle.LineWhy(baseLine, loadLine, session.Clock.Now) : string.Empty)}): {_plan.Summary()}{_plan.RouteSummary()}");
            LogRoom(state, sellLoot);
            if (_plan.Count(MageLootVerdict.ValueUnknown) > 0 && !_unknownNamed)
            {
                _unknownNamed = true;
                log.Warn(LogCategory, $"{MageLootRules.ValueUnknownTodo}: {_plan.Count(MageLootVerdict.ValueUnknown)} item(s) of the corpse have no Value in their description or in an appraisal; the rule counts them as worth nothing");
            }

            if (_plan.Kept.Count == 0)
            {
                Looted++;
                Close(session);
                return true;
            }

            return _plan.Drops.Count > 0 ? BeginRoom(session, clock) : StartLooting(session); // M6 14 keep every great cantrip: the room is made before the take, on the same visit
        }

        /// <summary>M6 5 q4: the chat total when the pick-ups began (the refusal's line is read since).</summary>
        private long _lootChatMark;

        /// <summary>
        /// M6 5 q4 (D57): a wanted item the plan took that is not held after the loop was refused by the server when its chat says so ("You have solved this quest too recently!", the 20 h pick-up gate of the tusk's own Quest string):
        /// logged by wcid with the server's text, and the wcid is no longer wanted in this fight (<see cref="WantHook"/>), so no later corpse is tried for it and there is no loop. Check, act, check: the pick-up was the act, the held state and the chat the check.
        /// </summary>
        private void NoteWantRefusals(IGameSessionView session)
        {
            if (owner._options.Want.Count == 0 || _plan is null)
            {
                return;
            }

            var said = QuestReads.Since(session.State.Chat, _lootChatMark).Select(l => l.Text).ToList();
            foreach (var wcid in RefusedWants(said, _plan, guid => session.State.Self.Inventory.Find(guid).Held))
            {
                if (owner._wantRefused.Add(wcid))
                {
                    var line = said.First(t => t?.StartsWith(QuestSolvedTooText, StringComparison.Ordinal) == true)!;
                    log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: want {wcid} refused by the server: {line}; the item is left and not tried again in this fight"));
                    if (owner._options.Until is { Chat: null } until && until.ItemWcid == wcid)
                    {
                        owner._untilRefused = line; // M6 5 F1 (review 5-R4): the until's only item is refused: the fight ends at its safe point
                    }
                }
            }
        }

        private bool Looting(IGameSessionView session, double clock)
        {
            if (_looter!.Step(session, clock) == LootStatus.Running)
            {
                return true;
            }

            var looter = _looter;
            _looter = null;
            NoteWantRefusals(session);
            var (kept, value) = TallyKept(session, looter);
            StackMerges += looter.StackMerges;
            CheckGreatTaken(session, looter); // M6 14 keep every great cantrip: each piece looked for in her pack

            Looted++;
            Kept += kept;
            Value += value;
            var named = looter.StoppedBy is "pack-full" or "burden" ? $"; the loop stopped on {looter.StoppedBy}, the rest of the corpse is left" : string.Empty;
            log.Info(LogCategory, $"loot: corpse 0x{_corpse:X8} done: kept {kept} of {_plan!.Kept.Count} planned (value {value}); {looter.Summary}{named}");
            owner.NoteUntilCount(session, _kill.Name, _corpse, WantedBy(_kill)); // M6 14 want loot: hunt-until: N of 16
            _wear.Clear();
            foreach (var guid in _plan.WorthALookGuids)
            {
                if (session.State.Self.Inventory.Find(guid).Held && !_plan.Items.Any(i => i.Guid == guid && MageLootRules.IsQuestWant(i))) // M6 5 q4: a quest item is no gear
                {
                    _wear.Enqueue(guid); // M5 7 D125: a find worth a look is judged for the self-equip before the corpse closes
                    if (_looked.Add(guid))
                    {
                        WorthALook++; // M6 14 (m6-lore-cantrips, the count kept honest): a wand or cantrip piece the rule matched, counted once and only when it is in her packs (not a planned pick-up the loop did not make, not a scroll, not a quest item)
                    }

                    KeepAsideTally.Add(guid); // the soak line's, once over the process
                }
            }

            _stage = Stage.Wearing;
            _stageStart = clock;
            return Wearing(session, clock);
        }

        /// <summary>
        /// M5 7 D125: the loot step's self-equip: each find worth a look that this corpse gave is judged (<see cref="SelfEquipJudge"/>); a wearable with a useful cantrip and a free slot is worn now (a jewellery or armour wield leaves the
        /// magic stance alone); a better caster waits for the home visit's gear phase, where the mage stands in peace (unwielding the held caster drops the magic mode: Player_Combat.cs 872-880 @95ef426d); anything else stays in the review pack.
        /// </summary>
        private bool Wearing(IGameSessionView session, double clock)
        {
            if (_swap is not null)
            {
                if (SwapStep(session, clock))
                {
                    return true;
                }

                _stageStart = clock;
            }

            if (_pending is not null)
            {
                if (_pending.Outcome is null && clock - _stageStart <= ActionSeconds)
                {
                    return true;
                }

                log.Info(LogCategory, $"loot: gear: the wield ended {(_pending.Outcome is { } o ? Describe(o) : "unanswered")}");
                if (_pending.Outcome is { Kind: ActionOutcomeKind.Confirmed })
                {
                    PackTally.AddEquip();
                }

                _pending = null;
            }

            while (_wear.Count > 0)
            {
                var guid = _wear.Dequeue();
                if (!session.State.Objects.TryGet(guid, out var item) || !session.State.Self.Inventory.Find(guid).Held || session.State.Self.Inventory.Find(guid).Kind == InventoryPlaceKind.Equipped)
                {
                    continue;
                }

                var worn = SelfEquipJudge.Worn(session.State);
                var choice = LootJudge(session).Judge(SelfEquipJudge.CandidateOf(session.State, item), worn, new QualitiesSkills(session.State.Self.Qualities)); // M6 14 capacity: the gear phase's wear pick on her live load, when one is held
                if (choice.Kind == SelfEquipKind.None)
                {
                    continue;
                }

                log.Info(LogCategory, "loot: " + choice + (choice.Kind == SelfEquipKind.Swap ? " (the swap waits for the home visit, in peace)" : string.Empty));
                if (choice.Kind == SelfEquipKind.Wear)
                {
                    _pending = Quartermaster.Wardrobe.Send(session, new Quartermaster.WieldRequest(guid) { Name = item.Name, Location = choice.Location }, new Quartermaster.WieldResult()); // M6 16 E5e1: the one wield routine's send (the find was read held and not worn just above); the check after is this step's own
                    _wearingGuid = guid;
                    _stageStart = clock;
                    return true;
                }

                if (choice.Kind == SelfEquipKind.Replace)
                {
                    // M6 14 capacity (live ladder58 12:23:21: a decided replace was only logged): carried out now, between kills with no attacker on her, check, act, check; else it waits for the next visit's gear phase, named
                    var displaced = (choice.Displaced.Count > 0 ? choice.Displaced : [choice.Replaces]).Select(g => worn.FirstOrDefault(w => w.Guid == g)).ToList();
                    if (displaced.Any(d => d is null))
                    {
                        log.Info(LogCategory, $"loot: gear: replace with '{choice.Name}' 0x{choice.Guid:X8} waits for the next visit: a piece it displaces is not worn now");
                        continue;
                    }

                    if (BeginSwap(session, clock, choice, displaced!, relight: false, out var waits))
                    {
                        _stageStart = clock;
                        return SwapStep(session, clock) || Wearing(session, clock);
                    }

                    log.Info(LogCategory, $"loot: gear: replace with '{choice.Name}' 0x{choice.Guid:X8} waits for the next visit's gear phase: {waits}");
                    continue;
                }

                if (choice is { Kind: SelfEquipKind.Review, LoreNeed: { } need })
                {
                    owner.LoreWanted?.Invoke(string.Create(CultureInfo.InvariantCulture, $"the find '{choice.Name}' 0x{choice.Guid:X8} waits on Arcane Lore {need}")); // M6 14 own lore: the spend raises it now, not at the next level-up
                }
            }

            if (PutBackLeftOff(session, clock)) // M6 16 E5e4: a piece an earlier swap left off goes back on before anything else comes off
            {
                _stageStart = clock;
                return SwapStep(session, clock) || Wearing(session, clock);
            }

            // M6 14 own lore: a worn piece whose spells are off and whose lore she now meets is re-wielded once (ACE reads the lore only at the wield)
            if (RelightCandidate(session, SelfEquipJudge.Worn(session.State)) is { } dark)
            {
                var relight = new SelfEquipChoice(SelfEquipKind.Replace, dark.Guid, dark.Name, dark.Location, dark.Guid, "its spells are off and her Arcane Lore now meets its ItemDifficulty: off and on again lights them");
                log.Info(LogCategory, $"loot: gear: re-wield '{dark.Name}' 0x{dark.Guid:X8}: {relight.Why}");
                if (BeginSwap(session, clock, relight, [dark], relight: true, out var waits))
                {
                    _stageStart = clock;
                    return SwapStep(session, clock) || Wearing(session, clock);
                }

                log.Info(LogCategory, $"loot: gear: re-wield '{dark.Name}' 0x{dark.Guid:X8} waits: {waits}");
            }

            Close(session);
            return true;
        }

        /// <summary>M5 7 D125, D149: the self-equip judge of the loot step: the keep-aside block's cantrips and the war element of the plan at the mage's level.</summary>
        private SelfEquipJudge GearJudge(IGameSessionView session) =>
            Quartermaster.Wardrobe.For(session.State).JudgeWithTerms(owner._ground, owner._spells, out _); // M6 16 E5e2: the session's one wear judge, the trip's; Q10c: with the wear pick's terms of her now, as the trip's pick reads them

        private void Close(IGameSessionView session)
        {
            _pending = session.Actions.CloseContainer(_corpse, ActionSource.Automation);
            _stage = Stage.Closing;
            _stageStart = session.Clock.Now;
        }

        private bool Closing(double clock)
        {
            if (_pending is { Outcome: { } closed })
            {
                log.Info(LogCategory, closed.Kind == ActionOutcomeKind.Confirmed ? $"loot: closed 0x{_corpse:X8}" : $"loot: the close of 0x{_corpse:X8} was not confirmed ({Describe(closed)}); not a failure");
            }
            else if (_pending is not null && clock - _stageStart <= ActionSeconds)
            {
                return true;
            }

            _pending = null;
            Done();
            return false;
        }

        /// <summary>Gives the corpse up where it stands: named and counted as left, a walk stops, a loop in hand is abandoned, an open view is closed fire and forget.</summary>
        private void Give(IGameSessionView session, string why)
        {
            Left++;
            log.Warn(LogCategory, $"loot: {why}; the corpse is left, the fight goes on");
            Finish(session, true);
        }

        private void Finish(IGameSessionView session, bool close)
        {
            CutWalk(session, session.Clock.Now, "the corpse is left", wait: true); // M6 14: a corpse left while ACE still walks her to it (the step's own time limits)
            StackMerges += _looter?.StackMerges ?? 0;
            _looter?.Abandon("fight-moved-on");
            _looter = null;
            _identifies.Clear();
            if (close && _corpse != 0 && session.State.Containers.Viewed is { } view && view.Guid == _corpse)
            {
                _ = session.Actions.CloseContainer(_corpse, ActionSource.Automation);
            }

            _pending = null;
            Done();
        }

        private void Done()
        {
            if (_corpse != 0)
            {
                _handled.Add(_corpse);
            }

            _stage = Stage.Idle;
            _corpse = 0;
            _plan = null;
        }

        /// <summary>
        /// The kill in hand's corpse: the one it holds (found before, or paired as a splash kill: that guid and no other), else the nearest <c>Corpse of &lt;name&gt;</c> within <see cref="CorpseRangeMetres"/> of where the creature lay
        /// that is not handled and that no other kill holds. M5 7 P7b (D139 j): round 13's 21 corpses "gone from the world" at 6-17 s were taken by a later kill whose own corpse had not appeared yet (the nearest by name was the
        /// earlier kill's, put off); the put-off kill then found its corpse handled.
        /// </summary>
        private bool Find(IGameSessionView session, out WorldObject corpse)
        {
            corpse = null!;
            if (_kill.Corpse != 0)
            {
                if (session.State.Objects.TryGet(_kill.Corpse, out var held) && held.Position is not null && !_handled.Contains(held.Guid))
                {
                    corpse = held;
                }

                return corpse is not null;
            }

            var guid = Nearest(session, _kill);
            if (guid != 0 && session.State.Objects.TryGet(guid, out var near))
            {
                corpse = near;
            }

            return corpse is not null;
        }

        /// <summary>The nearest corpse named for <paramref name="k"/>'s creature within <see cref="CorpseRangeMetres"/> of where it lay, not handled and held by no other kill; 0 for none.</summary>
        private uint Nearest(IGameSessionView session, LootKill k)
        {
            var wanted = MageLootRules.CorpseNameOf(k.Name);
            var origin = k.At.ToGlobal();
            var best = (double)CorpseRangeMetres;
            var found = 0u;
            foreach (var o in session.State.Objects.All)
            {
                if ((o.DescriptionFlags & WorldObjectTable.CorpseFlag) == 0 || o.Position is not { } p || !string.Equals(o.Name, wanted, StringComparison.Ordinal) || _handled.Contains(o.Guid) || Bound(o.Guid, k))
                {
                    continue;
                }

                var d = p.ToGlobal().DistanceTo(origin);
                if (d <= best)
                {
                    best = d;
                    found = o.Guid;
                }
            }

            return found;
        }

        /// <summary>True when a kill other than <paramref name="except"/> holds corpse <paramref name="guid"/>: the kill in hand, a queued one or a put-off one.</summary>
        private bool Bound(uint guid, LootKill? except)
        {
            if (_stage != Stage.Idle && _kill != except && _kill.Corpse == guid)
            {
                return true;
            }

            foreach (var k in _retry)
            {
                if (k != except && k.Corpse == guid)
                {
                    return true;
                }
            }

            foreach (var k in _kills)
            {
                if (k != except && k.Corpse == guid)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
