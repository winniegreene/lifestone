using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Data.Magic;
using Lifestone.Data.World;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Mage;

/// <summary>What a death left behind for the handler: the mage's own corpse (its guid, the cell and position its F745 had, the session clock of that F745), as <see cref="DeathTally"/> remembers it.</summary>
/// <param name="CorpseGuid">The corpse's guid, or 0 when none was remembered.</param>
/// <param name="CorpsePosition">The corpse's cell and position, or null.</param>
/// <param name="DiedAt">The session clock of the corpse's F745, or NaN.</param>
public readonly record struct DeathFacts(uint CorpseGuid, CellFrame? CorpsePosition, double DiedAt)
{
    /// <summary>The facts of the tally's last death.</summary>
    /// <param name="tally">The tally.</param>
    public static DeathFacts From(DeathTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);
        return new DeathFacts(tally.LastCorpseGuid, tally.LastCorpsePosition, tally.LastCorpseAt);
    }
}

/// <summary>
/// The death handler (M5 item 3 d1, D51, D53, DoD 7): what a player does after ANY death, with no mod verb: the respawn at the lifestone (classified as the death loop does), the rearm when the wielded caster went to the corpse (D53, below), the re-buff of what the mage still holds (the composed inner fight's buff stage),
/// the corpse trip by its route (a plan value, logged <c>route=recall|runner</c>: Primary Portal Recall, the ground's <c>portal.recall_spell</c>, where the mage holds the ground's tie, else the runner from the lifestone to the ground's portal, which
/// the travel seam has no leg for yet, <c>TODO(m6-first-tie-walk)</c>), inside the ground the runner over the room graph to the corpse's cell (<c>purpose=corpse</c>, D47), the items back (<see cref="CorpseLooter"/> with
/// <see cref="LootRules.TakeAll"/>, then the wand wielded and the worn items worn again from the pack: nothing is spawned), the item lines re-cast (the buff lines that missed while their item lay on the corpse) and the return to the fight (the composed fight's
/// kills). The vitae is never granted: it clears by earned experience (@95ef426d <c>Player_Xp.cs</c> 134-184 <c>UpdateXpVitae</c>, the pool paid out rank by rank; <c>Player_Death.cs</c> 125-145 <c>InflictVitaePenalty</c>, a zeroed pool at the death), so <c>vitae_grants=</c> is the session's own <c>xp</c> verb count (0 is the rule) and no vitae step is in the order. It knows no den and no town: every fact is the
/// <see cref="HuntingGround"/>'s (D46). Each step is logged once as <c>[death] handler &lt;step&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>The rearm (M5 3 F5, D53; <c>rearm=none|pack|bought|failed</c>, report-only).</b> ACE drops a level-127 character's 6 to 8 most valuable unbonded items at its death (@95ef426d <c>Player_Death.cs</c> 650-672 <c>GetNumItemsDropped</c>: level / 20 plus a roll of 0 to 2, capped at 14 (623); 488-505 <c>CalculateDeathItems</c>: wielded items drop from level 35, bonded items never),
/// the only wielded wand among them; and ACE puts a player back in peace who asks for the magic mode with no caster in the held slot (<c>Player_Combat.cs</c> 872-880, <c>Creature_Equipment.cs</c> 141-144 <c>GetEquippedWand</c>), which Lifestone's <see cref="CombatModeGuard"/> reads as <see cref="CombatModeProblem.MagicWithoutCaster"/>. The rebuff, the heals and the
/// recall are all cast in the magic mode, so between the respawn and the rebuff a mage with no wielded caster is armed again: (a) a caster the pack holds is wielded in the held slot; (b) else the ground's kit wielded line is bought at the ground's vendor that sells it (<see cref="ResupplyOptions.Kit"/>: the resupply's own shop leg, the purse's coin, a note changed when the coin is short, the walk logged
/// <c>purpose=vendor</c>) and wielded; (c) else (no vendor sells it, the buy fails, or the host has no journey context, <c>TODO(m5-death-rearm-standalone)</c>) the handler fails by name. Nothing is granted (D51). The original wand comes back on the corpse and is carried as a spare.</para>
/// <para><b>Named outcomes.</b> A corpse past <see cref="DeathScenario.PlayerCorpseMinRotSeconds"/> is <c>corpse_recovered=rotted</c>, one not in the object table within <see cref="CorpseFindSeconds"/> of the arrival at its cell (or none remembered) is
/// <c>not-found</c>; the fight resumes without it either way. A death at any step (a second death before the first trip, or a death on the trip), by any creature (an avoided one too: <c>m5-avoid-creature-reach</c>; the handler does not ask who killed),
/// restarts the handler for the newer corpse: the older is remembered and named (<see cref="OlderCorpses"/>, not gone back for: <c>TODO(m5-death-older-corpse)</c>).</para>
/// <para><b>The trip's aborts (M5 3 d4, the ones M4's place trip had), each named in <see cref="TripAbort"/> (<c>trip_abort=</c>, report-only) and tested on both dungeon profiles.</b> <c>health-under-&lt;N&gt;</c>: the health share fell under
/// <see cref="DeathOptions.AbortHealthPercent"/> (40) on the way to the corpse, at it or at its open: the trip HOLDS where it stands (the runner paused), heals as the pre-trip heal does up to the larger of that line and
/// <see cref="DeathOptions.RecoverMinHealthPercent"/> and goes on, and turns back (gives the corpse up, keeps what it took, the fight resumes) only when the heals cannot bring the share back, since the corpse carries the wand and the worn gear, nothing may be
/// placed back on foot and a turn at low health among the creatures would fight at that health anyway. <c>trip-over-&lt;N&gt;s</c>: <see cref="DeathOptions.AbortSeconds"/> (90) from the arrival in the ground (a hold counts). <c>open-refused</c>: the open
/// was refused or unanswered; a view is closed fire-and-forget and an open that lands late is closed when it lands. A corpse that is in the object table late, within <see cref="CorpseFindSeconds"/> of the arrival at its cell, is found (the seek polls every frame).</para>
/// <para><b>No place.</b> The handler never sends the mod's <c>place</c>: the recall is cast (<see cref="RecallHome"/>), and the corpse leg goes through the travel seam's <see cref="TravelPurpose.Corpse"/> runner; a seam that is today's place
/// (<see cref="PlaceTravel"/>) has no runner, so the leg is not made (<c>corpse_recovered=no</c>, <c>TODO(m5-death-corpse-runner)</c>) and the fight resumes.</para>
/// </remarks>
public sealed partial class DeathHandler : IDisposable
{
    /// <summary>The log category (the death loop's).</summary>
    public const string LogCategory = "death";

    /// <summary>The wait after the respawn's arrival for ACE's 75% vitals (3 s after the teleport, @95ef426d <c>Player_Death.cs</c> 262-282), as the death loop's.</summary>
    public const double RespawnVitalsSeconds = 5;

    /// <summary>The limit on the respawn's arrival after a death, in seconds.</summary>
    public const double RespawnArrivalSeconds = 60;

    /// <summary>How long the corpse may take to be in the object table after the arrival at its cell.</summary>
    public const double CorpseFindSeconds = DeathScenario.CorpseFindSeconds;

    /// <summary>The limit on the corpse's open, a wield and the close, each, in seconds.</summary>
    public const double ActionSeconds = 15;

    /// <summary>ACE's WeenieError 0x04A3 <c>YouMustLinkToPortalToRecall</c>: the mage holds no portal tie (@95ef426d <c>WorldObject_Magic.cs</c> 1107-1119, the Primary Portal Recall case: <c>LinkedPortalOneDID</c> null; the arrival: the 2 s delay and the teleport, 1166-1175).</summary>
    public const uint UntiedRefusal = 0x04A3;

    /// <summary>
    /// M6 6 C2 (the lead's live round 1: the corpse route's rung change failed once at the Town Network walk, "moved 24.9 m in one step", and the corpse was never gone back for): the retries of a failed route travel (the rung change, the
    /// leg's cut rung change, the corpse walk, the way back to the rung) in one death's handling, each logged <c>handler: route retry &lt;n&gt; of &lt;max&gt; after &lt;failure&gt;</c>, made only while the player is present, alive and not
    /// dead again on the leg (a new death restarts the handler instead); a failure past them is the named stop it was (Lifestone's own number).
    /// </summary>
    public const int RouteRetries = 1;

    /// <summary>
    /// M6 6 FXD1 (6-D3): the slowest pace a route=leg corpse leg is allowed, in metres a second of straight line (Lifestone's own): the wayfinder's assumed run, <see cref="WayfindScenario.AssumedSpeedMetresPerSecond"/> (4.8 m/s, a low
    /// fighter's Quickness 10 and Run 15), over its <see cref="WayfindScenario.RouteFactor"/> (1.5, the route against the straight line), about 3.2 m/s. A leg death's trip starts its clock at the corpse's travel place (the island run's
    /// drop, up to the run's own length from the corpse), so <see cref="DeathOptions.AbortSeconds"/> counts from there plus that line at this pace; no new world fact, only the wayfinder's two numbers.
    /// </summary>
    public const double LegCorpseMinMetresPerSecond = WayfindScenario.AssumedSpeedMetresPerSecond / WayfindScenario.RouteFactor;

    /// <summary>M6 6 C2: the <c>trip_abort=</c> of a leg death whose corpse no travel place holds (<see cref="RungRoute.PlaceHolding"/>; with <c>corpse_recovered=no</c>).</summary>
    public const string CorpseOffRouteAbort = "corpse-off-route";

    /// <summary>The pre-trip heal's limit, in seconds (four casts and the regeneration of <see cref="DeathScenario.PreTripRegenSeconds"/>).</summary>
    public const double HealSeconds = 120;

    /// <summary>
    /// M5 4 G1: the limit on the corpse recall's wait for the mana its cast needs, in seconds (Lifestone's own; the buff cycle's mana wait is 180 s too). The respawned mage's mana is spent by the rebuff (live: 1 of 415), the recall costs its
    /// base mana, and the mana regenerates in seconds to a minute; past the limit the cast goes out and ACE's refusal is the existing failure.
    /// </summary>
    public const double RecallManaSeconds = 180;

    /// <summary>M5 7 F19 (D92): the deaths in one handling (the first and its restarts) at which the run stops by name: a third death before the corpse is recovered.</summary>
    public const int MaxDeathsInCycle = 3;

    /// <summary>M5 7 F19: the failure's name at the third death; the autopilot stops on it (no recovery can cure it).</summary>
    public const string ThirdDeathStop = "third-death";

    /// <summary>M5 7 F19 (D92, rule 4): the top-up's goal before the corpse trip, in percent of each vital's maximum (Lifestone's own: "full" with a little room for ACE's rounding and the regeneration ticks).</summary>
    public const double TopUpPercent = 95;

    /// <summary>M5 7 F19: the top-up's bound, in seconds from its start (Lifestone's own, the recall's old mana bound); past it the trip goes as the mage is, named.</summary>
    public const double TopUpSeconds = 180;

    /// <summary>M5 7 F19: how far ahead of the mage the straight line toward the corpse is taken as the path ahead, in metres (Lifestone's own, three aggro reaches; <c>TODO(f19-trip-path-geometry)</c>: the runner's own route is not read).</summary>
    public const double TripLookAheadMetres = 60;

    /// <summary>M5 7 F19: one trip fight's failure limit, in seconds (the inner fight's <c>Seconds</c>; Lifestone's own).</summary>
    public const double TripFightSeconds = 120;

    /// <summary>M5 7 F19: inner fights in a row that end without the creature dead or gone while it stays near the path, after which the trip is given up by name (Lifestone's own).</summary>
    public const int TripFightMaxFails = 3;

    /// <summary>M5 7 F19: the hold between two trip fights after one ended without a kill (Lifestone's own: the creature comes on in the meantime).</summary>
    public const double TripFightRetrySeconds = 3;

    /// <summary>
    /// M6 7 AG (the gate's autopilot-mage lane, m6-7-g: 40 kills and 650 s of the trip's 725 s in trip fights, 76 s of walking, health never under 77%, cut 9.7 m from the corpse): the seconds of one trip's fights that clear the path (every creature within
    /// the aggro reach of it, M5 D92: the corpse trip never runs past tuskers). Past them the trip fights only what attacks it (M5 D88) and walks on to the corpse, so a ground that fills the path as fast as the mage clears it cannot hold the trip for ever.
    /// Lifestone's own: a third of the death state's 900 s watchdog (<c>AutopilotWatchdogs.BaseSeconds</c>), which leaves room for the 100 s before the trip, the 90 s the trip's own clock allows and the fights back. The fights back have no cap (D88).
    /// </summary>
    public const double TripFightBudgetSeconds = 300;

    /// <summary>M6 7 AG: the seconds after an attack (a hit, an evade or a spell on the mage) during which it counts as attacked once <see cref="TripFightBudgetSeconds"/> is spent (the same quiet as <see cref="RebuffDriver.FightQuietSeconds"/> and the autopilot's <c>HostileWatch</c>).</summary>
    public const double TripAttackQuietSeconds = RebuffDriver.FightQuietSeconds;

    private readonly HuntingGround _ground;
    private readonly DeathTally _tally;
    private readonly IJourneyTravel _travel;
    private readonly FightLegFactory _factory;
    private readonly IFightWalk? _walk;
    private readonly GearKit _kit;

    /// <summary>The worn set of before the death the take-back wears again (the autopilot's snapshot while the mage lived): <see cref="DeathOptions.Worn"/>, else <see cref="WornMemory.Shared"/> (M6 6 C1: set in the constructor).</summary>
    private WornMemory _worn;

    /// <summary>The worn set of before the death (M6 0 F3: the tests' own; M6 6 C1: <see cref="DeathOptions.Worn"/> when the caller gives one, else <see cref="WornMemory.Shared"/>; this init overrides both).</summary>
    public WornMemory WornSet { get => _worn; init => _worn = value ?? throw new ArgumentNullException(nameof(value)); }
    private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
    private readonly BuffOptions _buff;
    private readonly FightOptions _fight;
    private readonly DeathOptions _options;
    private readonly ILogSink _log;
    private readonly double _arrivalSeconds;
    private readonly List<uint> _scratch = [];
    private readonly List<uint> _older = [];
    private readonly HashSet<string> _logged = [];
    private DeathFacts _facts;
    private CellFrame? _bound;
    private IFightLeg? _inner;
    private ITravelLeg? _leg;
    private RecallHome? _recall;
    private CorpseLooter? _looter;
    private WornRestorer? _restorer; // M5 7 S2d: the worn set back on after the take-back
    private WornRestorer? _rewearer; // M6 7 AE: the remembered pieces the pack holds, worn right after the respawn
    private List<uint> _rewearWanted = [];
    private string _afterRewear = "rebuff";
    private Dictionary<uint, uint>? _wornBefore; // M6 0 F3: the worn set as the take-back found it (the restorer snapshots the memory when it ends), the kit-less check's "before"

    private ActionHandle? _pending;
    private ActionHandle? _healCast;
    private CastAttempt? _healAttempt;
    private bool _healOpen;

    /// <summary>M6 6 R1G: true once the heal spell is dropped for the trip (ACE's 0x0400 on a cast, or the pack short of its components before one): never cast again, no retry; the rest and regeneration, and the hold's and the abort's own rules, go on.</summary>
    private bool _healDropped;
    private ActionHandle? _abandonedOpen;
    private int _wearIndex;
    private bool _slotFreed;
    private bool _freeingSlot;
    private bool _arrived;
    private double _arrivedAt = double.NaN;
    private double _stepStart;
    private double _tripStart = double.NaN;
    private double _tripEnd = double.NaN;
    private int _noticeMark;
    private int _deadMark;
    private int _deathsMark;
    private int _teleportMark;
    private bool _rebuffAllUp = true;
    private IReadOnlyList<string> _misses = [];
    private bool _walkPrepared;
    private KeptWalk? _tripWalk;
    private bool _tripWalkMissingLogged;
    private double _groundAt = double.NaN;
    private double _drainUntil = double.NaN;
    private bool _tripHealing;
    private string _resumeStep = "idle";
    private double _resumeStart;
    private double _healStart;
    private int _heals;
    private int _holdCasts;
    private int _tripHeals;
    private double _lastNow;
    private bool _disposed;
    private ModVerbCounter? _verbs;
    private ModVerbCounts _verbMark;
    private ModVerbCounts? _verbEnd;
    private ResupplyRun? _shop;
    private string _rearmHow = DeathRearm.Pack;
    private string _rearmLine = "caster";
    private ManaDance? _dance;
    private int _topupHeals;
    private bool _topupHealOver;
    private bool _healNoWait;
    private bool _tripFighting;
    private double _tripFightStart;
    private double _tripFightRetryAt = double.NaN;
    private int _tripFightFails;
    private double _tripFightSpent; // M6 7 AG: the seconds of this trip's fights that ended (the fight in progress is added by TripFightSpentSeconds)
    private bool _tripBudgetLogged;
    private long _attackMark = -1;
    private double _lastAttackAt = double.NegativeInfinity;
    private bool _respawnRead; // M6 7 AG: the station was entered again after a cut, away from the respawn: nothing is waited for or classified again
    private uint _hostile;
    private DeathDropDecision? _drop; // M6 6 C2: the drop rule's answer, read once a death (before a leg route's trip, or at the corpse leg)
    private bool _corpseRunAsked; // M6 7 AY: the rung's owner was asked for this death
    private bool _coinFetch; // M6 7 AY: the coin rule said fetch
    private LadderCorpseCoin? _stall; // M6 8 I: set when the corpse is worth fetching (fetch:met, a need): the trip's limit is no progress, not time
    private (string Step, CellFrame At, double Since)? _stallMark; // M6 8 I: where the body stood when the stall clock last restarted
    private string? _legPlace; // M6 6 C2: the travel place a leg death's corpse trip stops in
    private int _routeRetries; // M6 6 C2 (live round 1): the route travel's retries this death (at most RouteRetries)
    private int _rungBackRetries; // M6 6 FXD1 (6-D13): the way back to the rung's own retries this death (at most RouteRetries)
    private double _legAllowance; // M6 6 FXD1 (6-D3): the seconds a route=leg trip's clock adds for the corpse leg's straight line (0 on every other route)
    private double _legMetres = double.NaN; // M6 6 FXD1 (6-D3): that straight line, in metres (NaN when not measured)
    private double _rungBackEnded = double.NaN; // M6 6 FXD1: the clock the way back to the rung's leg ended at, while its check after waits for a placed player

    /// <summary>M5 7 F19 (D92): the trip fights (a creature near the path or the corpse fought before the trip went on), report-only.</summary>
    public int TripFights { get; private set; }

    /// <summary>M6 7 AG: the kills of the trip's fights in this handling (report-only; each trip fight is one kill a leg).</summary>
    public int TripFightKills { get; private set; }

    /// <summary>M6 7 AG: true once this trip's fights spent <see cref="TripFightBudgetSeconds"/> with a creature still near the path: from there the trip fights only what attacks it.</summary>
    public bool TripFightBudgetSpent => _tripBudgetLogged;

    /// <summary>M5 7 F19 (D92): the dance's casts and drinks for the handler's mana (the recall, the heals, the top-up), report-only.</summary>
    public string DanceFields => _dance is { } d ? string.Create(CultureInfo.InvariantCulture, $"dance_transfers={d.Transfers} dance_revitalizes={d.Revitalizes} dance_potions={d.Potions}") : "dance_transfers=0 dance_revitalizes=0 dance_potions=0";

    private ManaDance Dance => _dance ??= new ManaDance(_spells, _ground, _fight, _buff, _log, LogCategory);

    /// <summary>The handler.</summary>
    /// <param name="ground">The ground the mage fights in (its portal, its home, its floor).</param>
    /// <param name="tally">The death's tally (the corpse, the deaths, the arrival); started by the caller.</param>
    /// <param name="travel">The travel seam (the corpse leg's runner).</param>
    /// <param name="fightFactory">Makes the composed inner fight (the rebuff, the item lines, the fight).</param>
    /// <param name="walk">The ground's walk for the fight, or null (the fight is not resumed: <c>TODO(m4-death-walkless-wait)</c>).</param>
    /// <param name="kit">The kit: its wand and armour are worn again from the pack.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="buff">The buff cycle's options.</param>
    /// <param name="fight">The fight's options.</param>
    /// <param name="options">The death loop's options (<see cref="DeathOptions.Rebuff"/>, <see cref="DeathOptions.Fight"/>, <see cref="DeathOptions.RecoverCorpse"/>, <see cref="DeathOptions.RecoverMinHealthPercent"/>, <see cref="DeathOptions.Facts"/>).</param>
    /// <param name="log">Where the handler logs.</param>
    /// <param name="arrivalSeconds">The inner scenarios' arrival limit.</param>
    public DeathHandler(HuntingGround ground, DeathTally tally, IJourneyTravel travel, FightLegFactory fightFactory, IFightWalk? walk, GearKit kit, IReadOnlyDictionary<uint, SpellInfo> spells, BuffOptions buff, FightOptions fight, DeathOptions options, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds)
    {
        (_ground, _tally, _travel, _factory) = (ground ?? throw new ArgumentNullException(nameof(ground)), tally ?? throw new ArgumentNullException(nameof(tally)), travel ?? throw new ArgumentNullException(nameof(travel)), fightFactory ?? throw new ArgumentNullException(nameof(fightFactory)));
        (_walk, _kit, _spells, _buff, _fight, _options, _log, _arrivalSeconds) = (walk, kit ?? throw new ArgumentNullException(nameof(kit)), spells ?? throw new ArgumentNullException(nameof(spells)), buff ?? throw new ArgumentNullException(nameof(buff)), fight ?? throw new ArgumentNullException(nameof(fight)), options ?? throw new ArgumentNullException(nameof(options)), log ?? throw new ArgumentNullException(nameof(log)), arrivalSeconds);
        _worn = options.Worn ?? WornMemory.Shared; // M6 6 C1: the caller's memory (the contract); null is today's process-wide one
    }

    /// <summary>The step the handler is in: <c>idle</c>, <c>respawn</c>, <c>rearm</c> (only when a caster is to be wielded or bought), <c>rebuff</c>, <c>heal</c>, <c>route</c>, <c>recall</c>, <c>runner</c>, <c>corpse-walk</c>, <c>trip-heal</c>, <c>corpse-seek</c>, <c>corpse-open</c>, <c>corpse-loot</c>, <c>corpse-close</c>, <c>wear</c>, <c>relines</c>, <c>fight</c>, <c>second-death</c>, <c>done</c>.</summary>
    public string CurrentStep { get; private set; } = "idle";

    /// <summary>Why the handler failed, or null.</summary>
    public string? Failure { get; private set; }

    /// <summary><c>deaths=</c>: the deaths the handler handled (the first and every restart).</summary>
    public int Deaths { get; private set; }

    /// <summary><c>corpse_recovered=</c>: <c>yes</c>, <c>partial</c>, <c>no</c>, <c>rotted</c>, <c>not-found</c>, <c>none-needed</c> (M6 6 C1: nothing but pyreals left the inventory, <see cref="DeathDropRule"/>; no walk), or <c>n/a</c> before the trip.</summary>
    public string Corpse { get; private set; } = DeathLegVerdict.NotApplicable;

    /// <summary><c>rearm=</c> (M5 3 F5, D53; report-only): <see cref="DeathRearm.None"/> (a caster was wielded), <see cref="DeathRearm.Pack"/>, <see cref="DeathRearm.Bought"/>, <see cref="DeathRearm.Failed"/>, or <c>n/a</c> before the respawn's end.</summary>
    public string Rearm { get; private set; } = DeathLegVerdict.NotApplicable;

    /// <summary><c>route=</c>: <c>recall</c>, <c>runner</c>, <c>walk</c>, <c>rung-change</c> (M6 6 C1: also after a 0x04A3 refusal when the travel rule, asked again, routes the ground), <c>leg</c> (M6 6 C2: the corpse lies on a leg, off the ground)
    /// or <c>n/a</c> (no trip planned yet).</summary>
    public string Route { get; private set; } = DeathLegVerdict.NotApplicable;

    /// <summary>Why the trip stopped short (<c>trip_abort=</c>), or <see cref="DeathLegVerdict.TripAbortNone"/>.</summary>
    public string TripAbort { get; private set; } = DeathLegVerdict.TripAbortNone;

    /// <summary><c>items_back=</c>: the items taken back from the corpse.</summary>
    public int ItemsBack => _looter?.Looted ?? 0;

    /// <summary>The entries the opened corpse listed (0 before an open).</summary>
    public int CorpseEntries { get; private set; }

    /// <summary>The kit items wielded or worn again from the pack.</summary>
    public int Rewielded { get; private set; }

    /// <summary>True when every wand and armour line of the kit is equipped (vacuously true for a kit with none): <c>gear_restored=</c>.</summary>
    public bool GearBack { get; private set; }

    /// <summary>The kit's wand and armour as the read model shows them after the items step: <c>gear=wielded|missing worn=&lt;n&gt;</c>.</summary>
    public string WornFields { get; private set; } = "gear=n/a worn=0";

    /// <summary>M5 7 S2d: the pieces the worn-set restore put back on after the take-back.</summary>
    public int WornRestored { get; private set; }

    /// <summary>
    /// <c>rebuffed=</c> (D56, closes the code half of <c>m5-death-rebuffed-item-lines</c>): <see cref="DeathLegVerdict.RebuffedYes"/> when every buff line is up once the item lines are re-cast (an item line's piece lies on the corpse until the items come back, so the rebuff
    /// before the trip cannot hold it), <see cref="DeathLegVerdict.RebuffedNo"/> or <see cref="DeathLegVerdict.RebuffedSkipped"/>. The value before the trip is <see cref="RebuffedPretrip"/>.
    /// </summary>
    public string Rebuffed { get; private set; } = DeathLegVerdict.RebuffedSkipped;

    /// <summary><c>rebuffed_pretrip=</c> (D56, report-only): the rebuff's own verdict before the corpse trip, the value <c>rebuffed=</c> had before the item lines were re-cast.</summary>
    public string RebuffedPretrip { get; private set; } = DeathLegVerdict.RebuffedSkipped;

    /// <summary>
    /// <c>rewear=</c> (M6 7 AE, report-only, D27): the remembered worn pieces the pack held at the respawn and the step wore again before the rebuff, <c>&lt;worn&gt;of&lt;held&gt;</c>; <c>none</c> when the pack held none (a real death: the dead mage's pieces
    /// stay worn or lie on the corpse, so the step is a no-op); <c>n/a</c> before the step.
    /// </summary>
    public string Rewear { get; private set; } = DeathLegVerdict.NotApplicable;

    /// <summary><c>relines=</c>: the item lines re-cast after the items came back.</summary>
    public int Relines { get; private set; }

    /// <summary>The lines the re-buff missed (the item lines whose item lay on the corpse among them), as the buff cycle names them.</summary>
    public IReadOnlyList<string> Misses => _misses;

    /// <summary>The composed fight's kills.</summary>
    public int Kills { get; private set; }

    /// <summary>
    /// <c>vitae_grants=</c> (3-R1, D51: the vitae clears by earned experience only): the <c>xp</c> verbs the session sent from the death's start (<see cref="Begin"/>) to the handler's end (done or failed), read from the session's
    /// <see cref="PlayerTools.Verbs"/> counter, never a constant; 0 is what the judge wants, and a later <c>xp</c> send on the death path shows here. -1 before the handler began (printed <c>n/a</c>).
    /// </summary>
    public int VitaeGrants => _verbs is null ? -1 : (_verbEnd ?? _verbs.Counts).Since(_verbMark).Xp;

    /// <summary>The pre-trip heal's casts.</summary>
    public int Heals => _heals;

    /// <summary>The casts of the holds on the trip (the health line, <see cref="DeathOptions.AbortHealthPercent"/>): the mage stood where it was and healed.</summary>
    public int TripHeals => _tripHeals;

    /// <summary>The older corpses a later death left behind (remembered, named, not gone back for).</summary>
    public IReadOnlyList<uint> OlderCorpses => _older;

    /// <summary>The corpse the trip goes for.</summary>
    public DeathFacts Facts => _facts;

    /// <summary>The inner scenario running now (the rebuff, the item lines or the fight), or null.</summary>
    public IFightLeg? Inner => _inner;

    /// <summary>The seconds the trip took from the route's start (to now while it runs), or null before it.</summary>
    public double? TripSeconds => double.IsNaN(_tripStart) ? null : (double.IsNaN(_tripEnd) ? _lastNow : _tripEnd) - _tripStart;

    /// <summary>A hold before the fight starts (the death loop's mana wait): true while the fight must wait.</summary>
    public Func<IGameSessionView, bool>? HoldBeforeFight { get; set; }

    /// <summary>
    /// M6 item 14: asked each frame before the rebuff begins; true holds the rebuff (the host's own step is running: the stop at the buff bot, ten metres from the lifestone she respawned at). The host answers false
    /// for good once its step is over or not due.
    /// </summary>
    public Func<IGameSessionView, double, bool>? BeforeRebuff { get; set; }

    /// <summary>The verdict fields, additive at the end of the death's line (report-only): <c>deaths= corpse_recovered= route= items_back= vitae_grants= rebuffed= rebuffed_pretrip= relines= rearm=</c> (the handler's <c>deaths=</c> and <c>rebuffed=</c> are the only ones on the death line: the respawn measures are <c>respawn_deaths=</c> and <c>stage_rebuffed=</c>, 3-R2).</summary>
    public string Fields => string.Create(CultureInfo.InvariantCulture, $"deaths={Deaths} corpse_recovered={Corpse} route={Route} items_back={ItemsBack} vitae_grants={(VitaeGrants < 0 ? DeathLegVerdict.NotApplicable : VitaeGrants.ToString(CultureInfo.InvariantCulture))} rebuffed={Rebuffed} rebuffed_pretrip={RebuffedPretrip} relines={Relines} rearm={Rearm} rewear={Rewear} replace={Replace}");

    /// <summary>The fields before any handler ran: every one <c>n/a</c> (a constant 0 for <c>vitae_grants=</c> would pass the judge on a run that never counted, 3-R1).</summary>
    public const string UnmeasuredFields = "deaths=n/a corpse_recovered=n/a route=n/a items_back=n/a vitae_grants=n/a rebuffed=n/a rebuffed_pretrip=n/a relines=n/a rearm=n/a rewear=n/a replace=n/a";

    /// <summary>Starts the handler after a death.</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The session clock.</param>
    /// <param name="facts">The death's corpse.</param>
    /// <param name="boundLifestone">The lifestone the respawn is classified against, or null (any lifestone object).</param>
    /// <param name="respawned">True when the caller already waited for the arrival and the vitals (the death loop's <c>respawn-vitals</c>): the respawn is classified at once.</param>
    /// <param name="respawnRead">M6 7 AG: true when the station was entered again after a cut with the body away from the respawn: the respawn was read at its first entry and the object table no longer holds its lifestone, so it is not classified again.</param>
    public void Begin(IGameSessionView session, double now, in DeathFacts facts, CellFrame? boundLifestone = null, bool respawned = false, bool respawnRead = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        (_facts, _bound, _lastNow, _respawnRead) = (facts, boundLifestone, now, respawnRead);
        Deaths = respawnRead ? Math.Max(1, _tally.Deaths) : 1; // M6 7 AG: a cut's second entry keeps the handling's deaths (the server's count since the station began): one death stays one, and a third still stops the run
        _verbs = session.Tools.Verbs;
        (_verbMark, _verbEnd) = (_verbs.Counts, null); // vitae_grants= (3-R1): the xp verbs sent from here to the handler's end
        Mark();
        _arrived = respawned;
        _arrivedAt = respawned ? now - RespawnVitalsSeconds : double.NaN;
        _log.Info(LogCategory, $"handler: the order (D51, D53): respawn, rearm, rebuff, the corpse trip, the items back, the item lines, the fight; the vitae is never granted, it clears by earned experience (Player_Xp.cs 134-184, Player_Death.cs 125-145 @95ef426d): vitae_grants is the session's xp-verb count over the handler; ground {_ground.Name}");
        Enter("respawn", now);
    }

    /// <summary>One frame of the handler.</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The session clock.</param>
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        _lastNow = now;
        PollAbandonedOpen(session);
        NoteAttacks(now);
        if (Failure is not null)
        {
            return ScenarioStatus.Failed;
        }

        if (CurrentStep == "done")
        {
            return ScenarioStatus.Succeeded;
        }

        if (CurrentStep != "second-death" && CurrentStep != "idle" && NewDeath())
        {
            return Restart(now);
        }

        return CurrentStep switch
        {
            "respawn" => RespawnStep(session, now),
            "rearm" => RearmStep(session, now),
            "rewear" => RewearStep(session, now),
            "replace" => ReplaceStep(session, now),
            "rebuff" => BuffStep(session, now, relines: false),
            "heal" => HealStep(session, now),
            "topup" => TopUpStep(session, now),
            "trip-fight" => TripFightStep(session, now),
            "route" => RouteStep(session, now),
            "recall" => RecallStep(session, now),
            "rung-change" => RungChangeStep(session, now),
            "leg" => LegStep(session, now),
            "rung-back" => RungBackStep(session, now),
            "runner" => Fail(string.Create(CultureInfo.InvariantCulture, $"handler: route=runner: the travel seam has no runner from the lifestone to {_ground.Name}'s portal{(_ground.Portal is { } p ? $" (its front point in cell 0x{p.FrontCell:X8}) and its Use" : " (the ground has no portal section)")}: TODO(m6-first-tie-walk)")),
            "trip-heal" => TripHealStep(session, now),
            "corpse-walk" => CorpseWalkStep(session, now),
            "corpse-seek" => CorpseSeekStep(session, now),
            "corpse-open" => CorpseOpenStep(session, now),
            "corpse-loot" => CorpseLootStep(session, now),
            "corpse-close" => CorpseCloseStep(session, now),
            "wear" => WearStep(session, now),
            "relines" => BuffStep(session, now, relines: true),
            "fight" => FightStep(session, now),
            "second-death" => SecondDeathStep(session, now),
            _ => Fail($"TODO(state): unknown handler step '{CurrentStep}'"),
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DropInFlight("disposed");
        EndThreatShadow(); // M6 16 E2b: the handling's line of its shadow's asks, report-only
        _legWatch?.Dispose(); // M6 14 run past: the corpse leg's hearing
        _legWatch = null;
    }

    // ------------------------------------------------------------------------------------------------ deaths

    private void Mark() => (_noticeMark, _deadMark, _deathsMark, _teleportMark) = (_tally.VictimNotices, _tally.DeadMotions, _tally.Deaths, _tally.DeathTeleports);

    private bool NewDeath() => _tally.VictimNotices > _noticeMark || _tally.DeadMotions > _deadMark || _tally.Deaths > _deathsMark;

    private ScenarioStatus Restart(double now)
    {
        var during = CurrentStep;
        DropInFlight("died");
        if (_facts.CorpseGuid != 0 && Corpse is not ("yes" or "partial"))
        {
            _older.Add(_facts.CorpseGuid);
        }

        Deaths++;
        if (Deaths >= MaxDeathsInCycle)
        {
            _log.Error(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: a death during {during} is death {Deaths} of this handling (the corpse 0x{_facts.CorpseGuid:X8} not yet recovered): the run stops by name, {ThirdDeathStep(during)} (M5 7 F19, D92: a third death in one cycle)"));
            return Fail(ThirdDeathStep(during));
        }

        _log.Warn(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"handler: a death during {during} (death {Deaths}; the handler does not ask which creature killed, an avoided one too: m5-avoid-creature-reach); the older corpse 0x{_facts.CorpseGuid:X8} is remembered and not gone back for (TODO(m5-death-older-corpse)); the handler restarts for the newer corpse"));
        (_noticeMark, _deadMark, _deathsMark) = (_tally.VictimNotices, _tally.DeadMotions, _tally.Deaths);
        (Corpse, Route, TripAbort, _tripStart, _tripEnd, _arrived, _arrivedAt, _wearIndex, CorpseEntries) = (DeathLegVerdict.NotApplicable, DeathLegVerdict.NotApplicable, DeathLegVerdict.TripAbortNone, double.NaN, double.NaN, false, double.NaN, 0, 0);
        (_groundAt, _drainUntil, _tripHealing, _tripHeals, _holdCasts) = (double.NaN, double.NaN, false, 0, 0);
        (_tripFighting, _tripFightFails, _tripFightRetryAt, _hostile, _topupHeals, _topupHealOver) = (false, 0, double.NaN, 0, 0, false);
        (_tripFightSpent, _tripBudgetLogged, _respawnRead) = (0, false, false); // M6 7 AG: a new death's trip has its own budget, and its respawn is read as any
        _looter = null;
        _restorer = null;
        (_rewearer, Rewear, _afterRewear) = (null, DeathLegVerdict.NotApplicable, "rebuff");
        ResetReplace();
        _wornBefore = null;
        (Rearm, _shop) = (DeathLegVerdict.NotApplicable, null);
        (_drop, _legPlace, _routeRetries, _rungBackRetries, _legAllowance, _legMetres, _rungBackEnded) = (null, null, 0, 0, 0, double.NaN, double.NaN);
        (_corpseRunAsked, _coinFetch, _stall, _stallMark) = (false, false, null, null);
        _logged.Clear(); // each step is logged once per death
        Enter("second-death", now);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus SecondDeathStep(IGameSessionView session, double now)
    {
        if (_tally.DeathTeleports > _teleportMark && session.State.Self.Travel == SelfTravel.Present)
        {
            var older = _facts.CorpseGuid;
            _facts = DeathFacts.From(_tally);
            Mark(); // M5 7 F19: the dead motion, the death line and the teleport of THIS death come in over the respawn's seconds; marking them all at the arrival keeps a late one from reading as a new death (the rehearsal's "death 5")
            (_arrived, _arrivedAt) = (true, now);
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: the newer corpse is 0x{_facts.CorpseGuid:X8}{(_facts.CorpseGuid == older ? " (no newer corpse was seen: the same one)" : string.Empty)}; older corpses remembered: {string.Join(", ", _older.Select(g => "0x" + g.ToString("X8", CultureInfo.InvariantCulture)))}"));
            Enter("respawn", now);
            return ScenarioStatus.Running;
        }

        return now - _stepStart > RespawnArrivalSeconds ? Fail(string.Create(CultureInfo.InvariantCulture, $"respawn: respawn=none: no arrival within {RespawnArrivalSeconds:0} s of the death during the handler")) : ScenarioStatus.Running;
    }

    private void DropInFlight(string why)
    {
        _inner?.Dispose();
        _inner = null;
        _leg?.Dispose();
        _leg = null;
        _recall?.Dispose();
        _recall = null;
        _looter?.Abandon(why);
        _shop = null;
        _pending = null;
        (_healCast, _tripHealing) = (null, false);
        if (_healOpen)
        {
            _healAttempt?.Stop(why, _lastNow);
            _healOpen = false;
        }

        _walk?.Abandon();
        _dance?.Stop(why, _lastNow);
        _tripFighting = false;
    }

    // ------------------------------------------------------------------------------------------------ the respawn and the rebuff

    private ScenarioStatus RespawnStep(IGameSessionView session, double now)
    {
        if (!_arrived)
        {
            if (_tally.DeathTeleports > _teleportMark && session.State.Self.Travel == SelfTravel.Present)
            {
                (_arrived, _arrivedAt, _teleportMark) = (true, now, _tally.DeathTeleports);
            }
            else
            {
                return now - _stepStart > RespawnArrivalSeconds ? Fail(string.Create(CultureInfo.InvariantCulture, $"respawn: respawn=none: no arrival within {RespawnArrivalSeconds:0} s")) : ScenarioStatus.Running;
            }
        }

        if (now < _arrivedAt + RespawnVitalsSeconds)
        {
            return ScenarioStatus.Running;
        }

        if (_respawnRead)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: respawn=read at the station's first entry (arrival cell 0x{_tally.ArrivalCell:X8}): entered again after a cut, it is not run again; the corpse 0x{_facts.CorpseGuid:X8}{(_facts.CorpsePosition is { } lies ? $" in cell 0x{lies.CellId:X8}" : " (no position remembered)")}"));
            return BeginRearm(session, now);
        }

        var respawn = DeathLegVerdict.ClassifyRespawn(_tally, session.State.Objects, _bound);
        if (respawn != DeathRespawn.Lifestone)
        {
            return Fail(string.Create(CultureInfo.InvariantCulture, $"respawn: respawn={respawn.ToString().ToLowerInvariant()}: the player arrived in cell 0x{_tally.ArrivalCell:X8}, not at a lifestone"));
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: respawn=lifestone (cell 0x{_tally.ArrivalCell:X8}); the corpse 0x{_facts.CorpseGuid:X8}{(_facts.CorpsePosition is { } at ? $" in cell 0x{at.CellId:X8}" : " (no position remembered)")}"));
        return BeginRearm(session, now);
    }

    // ------------------------------------------------------------------------------------------------ the rearm (M5 3 F5, D53)

    /// <summary>The kit trip's limit, in seconds (the walk to the vendor, his open and the buy).</summary>
    public const double ShopSeconds = 180;

    /// <summary>
    /// The end of the respawn: when a caster is wielded, <c>rearm=none</c> and the rebuff follows at once (no <c>rearm</c> step); else (a) the pack's caster is wielded, or (b) the kit wand is bought (the <c>rearm</c> step), or (c) the handler fails.
    /// </summary>
    private ScenarioStatus BeginRearm(IGameSessionView session, double now)
    {
        var wielded = CombatModeGuard.Read(session.State);
        if (wielded.Caster is not null || wielded.Unknown != 0)
        {
            Rearm = DeathRearm.None;
            _log.Info(LogCategory, wielded.Caster is { } held ? $"handler: rearm=none: the {held.Name} is still wielded" : $"handler: rearm=none: a wielded weapon-slot item 0x{wielded.Unknown:X8} has no object yet, so its kind is unknown; no rearm is decided (the combat-mode guard names it if it matters)");
            return EnterRewear(session, now, "rebuff");
        }

        Enter("rearm", now);
        var spare = GearInventory.PackCaster(session.State, _scratch);
        if (spare != 0)
        {
            _log.Info(LogCategory, $"handler: rearm: no caster is wielded (the wand went to the corpse: ACE drops the wielded wand among the 6 to 8 most valuable unbonded items of a level-127 character, Player_Death.cs 488-505, 650-672 @95ef426d) and the pack holds one, 0x{spare:X8}: wield it in the held slot; no walk, no buy");
            return WieldRearm(session, now, spare, DeathRearm.Pack, session.State.Objects.TryGet(spare, out var item) ? item.Name : "caster");
        }

        var wands = _ground.Supplies.Kit.Where(k => k.Slot == KitSlot.Wielded).ToList();
        if (wands.Count == 0 && _kit.Items.Count == 0)
        {
            // M6 0 F3 (closes TODO(m6-death-outdoor-kit)): a character whose profile has no kit has no wand to buy and none to wield from the pack: the wand it wielded (if any) lies on the corpse. No walk, no buy, no cast without a caster:
            // the corpse trip goes first (the lifestone to the corpse on foot) and the wear step wields the best caster held again; the rebuff follows the items back (the item lines).
            Rearm = DeathRearm.Corpse;
            Rebuffed = RebuffedPretrip = DeathLegVerdict.RebuffedSkipped;
            _log.Info(LogCategory, "handler: rearm=corpse: no caster is wielded or held and the profile has no kit to buy one from: the corpse trip goes first, the wear step wields the best caster again, and the rebuff follows (nothing is granted, D51)");
            return EnterRewear(session, now, "route");
        }

        if (wands.Count == 0)
        {
            return RearmFailed($"no caster is wielded or held, and the ground {_ground.Name}'s kit has no wielded line to buy (its profile's supplies.kit): nothing is granted (D51)");
        }

        var unsold = new List<KitLine>();
        ResupplyPlanner.KitBuyList(_ground, wands, unsold);
        if (unsold.Count > 0)
        {
            return RearmFailed(string.Create(CultureInfo.InvariantCulture, $"no caster is wielded or held, and no vendor of {_ground.Home.Town} sells the kit line {string.Join(", ", unsold.Select(k => $"{k.Name} (weenie {k.Wcid})"))} (the profile's vendors list none): nothing is granted (D51)"));
        }

        if (_options.Journey is not { } context)
        {
            return RearmFailed("no caster is wielded or held, and this host has no journey context to shop with, so the kit wand cannot be bought at the vendor: TODO(m5-death-rearm-standalone)");
        }

        if (!string.Equals(context.Dungeon.Key, _ground.Key, StringComparison.Ordinal))
        {
            return RearmFailed($"the journey's dungeon '{context.Dungeon.Key}' is not the handler's ground '{_ground.Key}': the kit trip would shop for the wrong ground");
        }

        _rearmLine = wands[0].Name;
        _shop = new ResupplyRun(context, new ResupplyOptions(Kit: wands));
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: rearm: no caster is wielded or held (the wand went to the corpse, Player_Death.cs 488-505, 650-672 @95ef426d; the magic mode needs one, Player_Combat.cs 872-880 @95ef426d): buy the ground's kit wand {string.Join(", ", wands.Select(k => $"{k.Name} (weenie {k.Wcid})"))} at {string.Join(", ", wands.Select(k => _ground.VendorSelling(k.Wcid)!.Name))} through the resupply (purpose=vendor; the purse's coin, a note changed when the coin is short; nothing is granted, D51); the original comes back on the corpse and is carried as a spare"));
        return ScenarioStatus.Running;
    }

    private ScenarioStatus WieldRearm(IGameSessionView session, double now, uint guid, string how, string name)
    {
        (_rearmHow, _rearmLine) = (how, name);
        var wield = new Quartermaster.WieldResult();
        _pending = Quartermaster.Wardrobe.Send(session, new Quartermaster.WieldRequest(guid) { Name = name, Location = (uint)EquipMask.Held }, wield); // M6 16 E5e1: the one wield routine's send (its check before, the slot, the wield); the check after is this step's own
        _stepStart = now;
        return _pending is null ? RearmFailed("the wield of the " + name + " was not sent: " + wield.Why) : ScenarioStatus.Running;
    }

    private ScenarioStatus RearmStep(IGameSessionView session, double now)
    {
        if (_shop is { } shop)
        {
            var status = shop.Step(session, now);
            if (status == ScenarioStatus.Running)
            {
                return now - _stepStart > ShopSeconds ? RearmFailed(string.Create(CultureInfo.InvariantCulture, $"the kit trip was not over within {ShopSeconds:0} s (stage {shop.Stage})")) : ScenarioStatus.Running;
            }

            _shop = null;
            _log.Info(LogCategory, $"handler: rearm: the kit trip ended {status}: {shop.VerdictFields()}");
            if (status != ScenarioStatus.Succeeded || !shop.Ok)
            {
                return RearmFailed("the kit trip failed: " + (shop.Failure ?? (shop.Problems.Count == 0 ? status.ToString() : shop.Problems[0])));
            }

            if (shop.Bought < 1)
            {
                return RearmFailed(string.Create(CultureInfo.InvariantCulture, $"the {_rearmLine} was not bought (bought=0 vendors={shop.Vendors} not_found={shop.NotFound} short={shop.Short} refusals={(shop.Refusals.Count == 0 ? "none" : string.Join('+', shop.Refusals))})"));
            }

            var bought = GearInventory.PackCaster(session.State, _scratch);
            if (bought == 0)
            {
                return RearmFailed($"the {_rearmLine} was bought but the pack holds no caster after it (bought={shop.Bought})");
            }

            _log.Info(LogCategory, $"handler: rearm: bought the {_rearmLine} (0x{bought:X8}) for {shop.Spent} pyreals; wield it in the held slot");
            return WieldRearm(session, now, bought, DeathRearm.Bought, session.State.Objects.TryGet(bought, out var item) ? item.Name : _rearmLine);
        }

        if (_pending is { } wield)
        {
            if (wield.Outcome is { } done)
            {
                if (done.Kind != ActionOutcomeKind.Confirmed)
                {
                    return RearmFailed($"the wield of the {_rearmLine} ended {done.Kind}{(done.Code == 0 ? string.Empty : $" (code 0x{done.Code:X})")}");
                }

                if (CombatModeGuard.Read(session.State).Caster is not null)
                {
                    _pending = null;
                    Rearm = _rearmHow;
                    _log.Info(LogCategory, $"handler: rearm={Rearm}: the {_rearmLine} is wielded; the worn pieces of the pack go on, then the rebuff follows");
                    return EnterRewear(session, now, "rebuff");
                }
            }

            return now - _stepStart > ActionSeconds ? RearmFailed($"the {_rearmLine} was not wielded within {ActionSeconds:0} s (the held slot shows no caster)") : ScenarioStatus.Running;
        }

        return RearmFailed("TODO(state): the rearm step has nothing running");
    }

    private ScenarioStatus RearmFailed(string why)
    {
        Rearm = DeathRearm.Failed;
        return Fail("rearm: rearm=failed: " + why);
    }

    /// <summary>The seconds the rewear may take (a piece a second is plenty; Lifestone's own).</summary>
    public const double RewearSeconds = 60;

    /// <summary>
    /// M6 7 AE: between the rearm and the rebuff (the item spells need a worn piece to land on): the remembered worn pieces the pack holds and the body does not wear are put on, each named; check before (the state of every remembered piece), act
    /// (the restorer's memory phase only), check after. A real death leaves nothing of the set in the pack, so it is a logged no-op (<c>rewear=none</c>). What the corpse holds is the take-back's.
    /// </summary>
    private ScenarioStatus EnterRewear(IGameSessionView session, double now, string next)
    {
        _afterRewear = next;
        _rewearWanted = [];
        foreach (var (guid, location) in _worn.Set)
        {
            var place = session.State.Self.Inventory.Find(guid);
            var name = session.State.Objects.TryGet(guid, out var o) ? o.Name : "?";
            var where = !place.Held ? "not held (on the corpse or gone: the take-back's)" : place.Kind == InventoryPlaceKind.Equipped ? "worn" : "in the pack";
            _log.Info(LogCategory, $"rewear: before: '{name}' 0x{guid:X8} (location 0x{location:X8}): {where}");
            if (place.Held && place.Kind != InventoryPlaceKind.Equipped)
            {
                _rewearWanted.Add(guid);
            }
        }

        if (_rewearWanted.Count == 0)
        {
            Rewear = "none";
            _log.Info(LogCategory, $"rewear=none: no remembered worn piece lies in the pack ({_worn.Set.Count} remembered); nothing to put on before the {next}");
            return EnterReplace(session, now, next);
        }

        _log.Info(LogCategory, $"rewear: {_rewearWanted.Count} remembered piece(s) lie in the pack: they go on before the {next} so the item spells find a worn piece");
        _rewearer = new WornRestorer(_ground, session.State.Self.Qualities.Level, _spells, _log, LogCategory, "the death's respawn", _worn, memoryOnly: true);
        return Enter("rewear", now);
    }

    private ScenarioStatus RewearStep(IGameSessionView session, double now)
    {
        var restorer = _rewearer;
        if (restorer is not null && restorer.Step(session) && now - _stepStart <= RewearSeconds)
        {
            return ScenarioStatus.Running;
        }

        var timedOut = restorer is not null && !restorer.IsDone;
        _rewearer = null;
        var worn = 0;
        foreach (var guid in _rewearWanted)
        {
            var on = session.State.Self.Inventory.TryGetEquipped(guid, out _);
            worn += on ? 1 : 0;
            var name = session.State.Objects.TryGet(guid, out var o) ? o.Name : "?";
            var line = $"rewear: after: '{name}' 0x{guid:X8}: {(on ? "worn" : "not worn")}";
            if (on)
            {
                _log.Info(LogCategory, line);
            }
            else
            {
                _log.Warn(LogCategory, line + " (report-only; the take-back tries again)");
            }
        }

        Rewear = string.Create(CultureInfo.InvariantCulture, $"{worn}of{_rewearWanted.Count}");
        _log.Info(LogCategory, $"rewear={Rewear}{(timedOut ? $" (the step ran past {RewearSeconds:0} s and was left; report-only)" : string.Empty)}; the replace step follows, then the {_afterRewear}");
        return EnterReplace(session, now, _afterRewear);
    }

    private static bool IsBuffing(string stage) => stage is "start" or "gear" or "buffs" or "buffs-mode" or "buffs-cycle";

    /// <summary>The rebuff (what the mage still holds) and, after the items came back, the item lines: the composed inner fight's buff stage alone, dropped at its own stage <c>place</c>.</summary>
    private ScenarioStatus BuffStep(IGameSessionView session, double now, bool relines)
    {
        if (_inner is null && !relines && _options.Rebuff && BeforeRebuff?.Invoke(session, now) == true)
        {
            return ScenarioStatus.Running;
        }

        if (_inner is null)
        {
            if (!_options.Rebuff || (relines && _rebuffAllUp && Rewielded == 0))
            {
                if (!relines)
                {
                    Rebuffed = RebuffedPretrip = DeathLegVerdict.RebuffedSkipped;
                    _log.Info(LogCategory, "handler: --death-rebuff is off: no rebuff; rebuffed=skipped");
                }
                else
                {
                    _log.Info(LogCategory, "handler: no item line to re-cast (the rebuff had every line up and nothing came back to be worn); relines=0");
                }

                return relines ? BeginFight(session, now) : Enter("heal", now);
            }

            _inner = _factory(_kit, _spells, _buff, _fight, null, _log, _arrivalSeconds, false, FightStages.Buffs); // no walk: the inner fight disposes the walk it is given, and the ground's walk is the fight's
        }

        var inner = _inner;
        var status = inner.Step(session, now);
        if (status == ScenarioStatus.Running && IsBuffing(inner.Stage))
        {
            return ScenarioStatus.Running;
        }

        var up = status != ScenarioStatus.Failed && inner.BuffsAllUp;
        var note = inner.BuffsNote;
        var misses = inner.BuffMisses;
        var failure = inner.Failure;
        inner.Dispose();
        _inner = null;
        if (relines)
        {
            Relines = Casts(note);
            Rebuffed = up ? DeathLegVerdict.RebuffedYes : DeathLegVerdict.RebuffedNo; // D56: every line up once the item lines are re-cast
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: the item lines re-cast after the items came back: relines={Relines} (buffs={note}; the rebuff had missed: {(_misses.Count == 0 ? "none named" : string.Join(", ", _misses))}){(up ? string.Empty : "; a line is still down (report-only)")}"));
            return BeginFight(session, now);
        }

        (_rebuffAllUp, _misses) = (up, misses);
        Rebuffed = RebuffedPretrip = up ? DeathLegVerdict.RebuffedYes : DeathLegVerdict.RebuffedNo;
        if (status == ScenarioStatus.Failed)
        {
            _log.Warn(LogCategory, "rebuff: the buff stage failed: " + failure + "; rebuffed=no (report-only: the trip still goes)");
        }
        else if (up)
        {
            _log.Info(LogCategory, $"rebuff: done (buffs={note}, every profile line up); rebuffed=yes");
        }
        else
        {
            _log.Warn(LogCategory, $"rebuff: the buff stage ended with a profile line down (buffs={note}; missed: {(misses.Count == 0 ? "none named" : string.Join(", ", misses))}); rebuffed=no (report-only: an item line whose item lies on the corpse is re-cast after the items come back)");
        }

        return Enter("heal", now);
    }

    private static int Casts(string note) =>
        note.StartsWith("topped-up:", StringComparison.Ordinal) && int.TryParse(note.AsSpan("topped-up:".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var casts) ? casts : 0;

    // ------------------------------------------------------------------------------------------------ the pre-trip heal (m4-death-trip-heal-resume)

    /// <summary>
    /// The pre-trip heal: when the health share is under <see cref="DeathOptions.RecoverMinHealthPercent"/>, Heal Self VII up to <see cref="DeathScenario.PreTripMaxHeals"/> casts; a cast waits while the mana is under the spell's base mana and goes
    /// out when the mana regenerates (closes <c>m4-death-trip-heal-resume</c>: a heal skipped for mana resumes), bounded by <see cref="HealSeconds"/>. A mage still under the minimum goes on (the trip is the way to the fight).
    /// </summary>
    private ScenarioStatus HealStep(IGameSessionView session, double now)
    {
        if (!_options.RecoverCorpse)
        {
            return Enter("route", now);
        }

        switch (Heal(session, now, _options.RecoverMinHealthPercent, ref _heals, "heal"))
        {
            case HealOutcome.Healthy:
                return Enter("topup", now);
            case HealOutcome.GaveUp:
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: the health share is {Share(session, 1):0}% after {Heals} heal(s), under --death-recover-min-health {_options.RecoverMinHealthPercent:0.##}%: the trip goes on (the way to the fight; TODO(m4-death-trip-health))"));
                return Enter("topup", now);
            default:
                return ScenarioStatus.Running;
        }
    }

    /// <summary>
    /// M6 6 FXD1 (6-D1): the heal's ends that never drop it for the trip: the server ran it (<see cref="CastEnd.Landed"/>, <see cref="CastEnd.Unproven"/>) or the end is luck or time that passes (<see cref="CastEnd.Fizzled"/>,
    /// <see cref="CastEnd.Busy"/>, <see cref="CastEnd.ManaShort"/>: the next cast may land). Every other end (a component, an unknown spell's <see cref="CastEnd.ServerError"/>, <see cref="CastEnd.NoEffect"/>, <see cref="CastEnd.Refused"/>, a
    /// timeout, ...) is permanent for this trip: the heal is dropped once, named with the end, and never cast again (live round 1's refusal loop).
    /// </summary>
    internal static bool HealEndKeeps(CastEnd end) => end is CastEnd.Landed or CastEnd.Unproven or CastEnd.Fizzled or CastEnd.Busy or CastEnd.ManaShort;

    private enum HealOutcome
    {
        Running,
        Healthy,
        GaveUp,
    }

    /// <summary>
    /// One frame of a heal toward <paramref name="goal"/> percent (the pre-trip heal's and the trip hold's): Heal Self VII up to <see cref="DeathScenario.PreTripMaxHeals"/> casts of this heal (<paramref name="casts"/>), a cast waiting for the mana,
    /// then the regeneration of <see cref="DeathScenario.PreTripRegenSeconds"/>; given up after that or <see cref="HealSeconds"/> from the step's start. The cast goes through item 1's seam (<see cref="CastAttempt"/>, <see cref="CastRules.Once"/>, automation; 3-R5) and only a cast the server ran (<see cref="CastEnd.Landed"/> or <see cref="CastEnd.Unproven"/>) counts toward <paramref name="casts"/>; <c>_healCast</c> holds the one mode change (the corpse's open keeps <c>_pending</c>).
    /// </summary>
    private HealOutcome Heal(IGameSessionView session, double now, double goal, ref int casts, string what)
    {
        if (_healCast is { } cast)
        {
            if (cast.Outcome is not { } done)
            {
                return HealOutcome.Running;
            }

            _healCast = null;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: {what} mode change: {done.Kind}{(done.Code == 0 ? string.Empty : $" (code 0x{done.Code:X})")}"));
        }

        if (_healOpen)
        {
            // the cast goes through item 1's seam (3-R5, CastAttempt with CastRules.Once, ActionSource.Automation): a fizzle, a busy answer past the rules' waits or a mana refusal is a named end and is not one of the heals
            var end = _healAttempt!.Step(now);
            if (end == CastEnd.Running)
            {
                return HealOutcome.Running;
            }

            _healOpen = false;
            var landed = end is CastEnd.Landed or CastEnd.Unproven; // UseDone 0: the server ran it (Heal Self has no enchantment to confirm)
            if (landed)
            {
                casts++;
            }

            var report = _healAttempt.Report;
            if (!HealEndKeeps(end))
            {
                // M6 6 FXD1 (6-D1): every end but Landed, Unproven, Fizzled, Busy and ManaShort is permanent for the trip (0x0400, an unknown spell's 0x03FC, no effect, refused, a timeout): dropped once, named, never recast
                DropHeal(what, end == CastEnd.NoComponents ? "no components (ACE 0x0400)" : string.Create(CultureInfo.InvariantCulture, $"the cast ended {end.Name()}{(report.Code == 0 ? string.Empty : $" (code 0x{report.Code:X})")}, a permanent end for this trip"));
            }

            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: {what} cast ended {end.Name()}{(landed ? $" (heal {casts} of {DeathScenario.PreTripMaxHeals})" : " (not counted as a heal)")} fizzles={report.Fizzles} busy={report.Busy}; health {Share(session, 1):0}%"));
        }

        var health = Share(session, 1);
        if (double.IsNaN(health) || health >= goal)
        {
            return HealOutcome.Healthy;
        }

        if (now - _stepStart > HealSeconds || casts >= DeathScenario.PreTripMaxHeals && now - _stepStart > DeathScenario.PreTripRegenSeconds)
        {
            return HealOutcome.GaveUp;
        }

        if (casts >= DeathScenario.PreTripMaxHeals || _healDropped || HealLevel(session, what) is not { } heal)
        {
            if (_healNoWait)
            {
                return HealOutcome.GaveUp; // the top-up waits for no regeneration (D92)
            }

            return HealOutcome.Running; // regeneration only
        }

        if (session.State.Self.Qualities.CombatMode != CombatModes.Magic)
        {
            if (_logged.Add(what + "-mode"))
            {
                _healCast = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
            }

            return HealOutcome.Running;
        }

        if (Dance.Busy)
        {
            Dance.Step(session, now, heal.BaseMana, 0, what); // a transfer in flight ends before the heal is sent
            return HealOutcome.Running;
        }

        if (!session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _) || mana < heal.BaseMana)
        {
            if (Dance.Step(session, now, heal.BaseMana, 0, what) is not (DanceStep.Wait or DanceStep.Done))
            {
                return HealOutcome.Running; // M5 7 F19 (D92): never wait for mana: the dance brings it
            }

            if (_healNoWait)
            {
                return HealOutcome.GaveUp; // the top-up never waits for the mana a heal needs (D92): it goes on as the mage is
            }

            if (_logged.Add(what + "-mana"))
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: {what} skipped for mana ({mana} under {heal.BaseMana}): the last resort, no transfer, no Revitalize Self and no mana potion can be cast (D92); it resumes when the mana regenerates (m4-death-trip-heal-resume)"));
            }

            return HealOutcome.Running;
        }

        _logged.Remove(what + "-mana");
        _healAttempt ??= new CastAttempt(session.Actions, session.State, _log, _spells);
        _healOpen = true;
        _healAttempt.Start(new CastRequest { Levels = [heal.Id], Rules = CastRules.Once, Purpose = "handler " + what, Source = ActionSource.Automation }, now);
        return HealOutcome.Running;
    }

    /// <summary>
    /// M6 6 FXD1 (6-D1, the check before the cast): the Heal Self level the handler casts, or null when the heal is dropped for the trip (logged once by <see cref="DropHeal"/>). A read spellbook gives the highest level of the keeper's
    /// Heal Self line (<see cref="VitalsMeans.HealSelf"/>, I to VII, the ids the vitals keeper's heal reads) that the book knows, the SpellTable holds and the pack can pay for (M6 6 R1G: the foci formula,
    /// <see cref="LevelFloor.HeldForAccount"/>, the same read the bolt pick uses; not read: not judged, the refusal drops it); a book that knows none, or none the pack pays for, drops the heal, named. An unread book (the server's
    /// list not in yet) is not judged, logged once: the den mage's Heal Self VII as before, and a permanent refusal drops it after one cast (<see cref="HealEndKeeps"/>). The climber is never granted a spell (D10, D34): below Heal
    /// Self VII its book holds a lower level, and the level picked is the one cast.
    /// </summary>
    private SpellInfo? HealLevel(IGameSessionView session, string what)
    {
        var book = session.State.Self.Spellbook;
        if (!book.IsKnown)
        {
            if (_logged.Add("heal-book-unread"))
            {
                _log.Info(LogCategory, $"handler: {what}: the spellbook is not read yet: the heal's level is not judged (Heal Self VII as the den mage's; a permanent refusal drops it after one cast)");
            }

            return _spells.TryGetValue(DeathScenario.HealSelfVii, out var vii) ? vii : null; // not in the SpellTable: regeneration only, as before
        }

        var held = LevelFloor.HeldForAccount(session.State, DatReaderWriter.Enums.MagicSchool.LifeMagic, null);
        var ids = VitalsMeans.HealSelf;
        string? shortOf = null;
        for (var i = ids.Count - 1; i >= 0; i--)
        {
            if (!book.Knows(ids[i]) || !_spells.TryGetValue(ids[i], out var info))
            {
                continue;
            }

            if (held is not null && LevelFloor.ShortOf(held, info, out var need))
            {
                shortOf ??= info.Name + " needs " + need;
                continue;
            }

            if (_logged.Add("heal-level"))
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: {what}: the heal's level is {info.Name} ({info.Id}), the highest Heal Self level the spellbook ({book.Count} spells) knows{(held is null ? string.Empty : " and the pack pays for")}"));
            }

            return info;
        }

        DropHeal(what, shortOf is null
            ? string.Create(CultureInfo.InvariantCulture, $"not in the spellbook: the read spellbook ({book.Count} spells) knows no Heal Self level the SpellTable holds")
            : "the pack is short: " + shortOf);
        return null;
    }

    /// <summary>M6 6 R1G, FXD1: drops the heal spell for the trip, logged once with why; the next heal source is the regeneration (and the hold's, the abort's own rules), never a retry.</summary>
    private void DropHeal(string what, string why)
    {
        if (_healDropped)
        {
            return;
        }

        _healDropped = true;
        _log.Warn(LogCategory, $"handler: {what}: Heal Self dropped for the trip: {why}; the trip goes on at the health it has (regeneration, the hold and the abort rules)");
    }

    private static double Share(IGameSessionView session, uint maxId) =>
        session.State.Self.Qualities.TryGetVitalLevels(maxId, out var current, out var max) && max > 0 ? current * 100.0 / max : double.NaN;

    // ------------------------------------------------------------------------------------------------ the route

    private ScenarioStatus RouteStep(IGameSessionView session, double now)
    {
        if (double.IsNaN(_tripStart))
        {
            _tripStart = now; // the first look; the recall's mana wait below keeps this step and does not restart the trip's clock
        }

        if (CorpseRunShut(session, now) is { } shut)
        {
            return shut; // M6 7 AY: a shut gate means no fetch, before any way is taken
        }

        var portal = _ground.Portal;
        var tied = _options.Facts?.TieOf(_ground) ?? PortalTie.Unknown; // the tie of THIS ground (M5 3 p1, 2-R12); M6 4 F1 (4-R2): untied when the one slot holds another
        string why;
        if (portal is null && WayHomeRule.HomeOnFoot(_ground))
        {
            // M6 0 F3: an outdoor ground hunted from its home on foot (the climber's rung): the lifestone is in the town beside it and the corpse lies in the ground's cells, so the way is the corpse leg's own walk (an object goal through the travel seam);
            // there is no portal to recall to and no portal to walk to.
            (Route, why) = (DeathRoute.Walk, "the ground is outdoors with no portal and is hunted from " + _ground.Home.Town + " on foot: the corpse walk goes from the lifestone");
        }
        else if (LegDeath(session, now) is { } leg)
        {
            return leg;
        }
        else if (_travel is JourneyTravel && session.State.Self.Position is { } here && _ground.InLandblocks(here.CellId))
        {
            // M6 7 U (r1f-climb, TODO(m6-first-tie-walk) with a route in the travel file): a station entered again after a cut starts here with the body already on the ground, whatever the tally's respawn says. The travel rule then answers
            // "route here, the ground's own station", not the rung change, so the portal section's arm below would name a stop for a ground whose route exists. C1's re-decide reads a 0x04A3 refusal and does not run here. The way is done: the corpse leg.
            (Route, why) = (DeathRoute.Here, string.Create(CultureInfo.InvariantCulture, $"the body already stands in {_ground.Name} (cell 0x{here.CellId:X8}): the station was entered again and there is no way left to travel"));
        }
        else if (_travel is JourneyTravel journey && journey.Decide(session, _ground, RungDirection.Out) is { Way: Autopilot.TravelWay.RungChange } routed)
        {
            // M6 4 F1 (4-R1): the travel rule the autopilot's Travel state uses (AutopilotTravelRule.Out), decided before the act: a routed ground (untieable, or its tie not known tied) is reached by the rung change, never a recall tried.
            (Route, why) = (DeathRoute.RungChange, $"the travel rule says the rung change (route {routed.Route ?? "n/a"}): {routed.Reason}");
        }
        else if (portal is null)
        {
            (Route, why) = (DeathRoute.Runner, "the ground has no portal section");
        }
        else if (tied == PortalTie.Untied)
        {
            (Route, why) = (DeathRoute.Runner, "the journey's tie fact says the portal is not tied");
        }
        else
        {
            (Route, why) = (DeathRoute.Recall, tied == PortalTie.Tied ? "the journey's tie fact says the portal is tied" : "the tie is not known: the recall's answer decides (0x04A3 switches to the runner)");
        }

        if (_logged.Add("route-plan"))
        {
            _log.Info(LogCategory, $"handler: the corpse trip's route=recall|runner plan: route={Route} ({why}); {_ground.Name}{(portal is { } p ? $", portal 0x{p.Guid:X8}, recall spell {p.RecallSpell}" : string.Empty)}");
        }

        if (Route is DeathRoute.Walk or DeathRoute.Here)
        {
            return BeginCorpseLeg(session, now);
        }

        if (NothingOwedHere(session))
        {
            return BeginCorpseLeg(session, now); // M6 7 U: corpse_recovered=none-needed with no trip: the corpse leg's own arm ends it
        }

        if (Route == DeathRoute.Runner)
        {
            return Enter("runner", now);
        }

        if (Route == DeathRoute.RungChange)
        {
            // M6 4 F1 (4-R1): the corpse trip's way to the ground by the travel seam's Ground purpose, whose leg is the rung change (JourneyTravel asks the same rule); check after: present in the ground before the corpse leg.
            _log.Info(LogCategory, $"walking: purpose=ground (the corpse trip: the rung change to {_ground.Name}, then the corpse leg)");
            _leg = _travel.GoTo(session, new TravelRequest(_ground.ArrivalDestination(), TravelPurpose.Ground), now);
            return Enter("rung-change", now);
        }

        if (WaitsForRecallMana(session, now, portal!.RecallSpell))
        {
            return ScenarioStatus.Running;
        }

        _recall = new RecallHome(session, null, _log, _ground.ArrivalLandblock(), portal!.RecallSpell);
        if (!_recall.Begin(now))
        {
            return Fail("recall: the recall did not begin: " + (_recall.Failure ?? "already running"));
        }

        return Enter("recall", now);
    }

    /// <summary>
    /// M6 7 U (D67, r1f-climb: two rung changes of about 220 s, 611 and 725 m, to learn <c>none-needed</c> on arrival): the drop rule is read here, before any trip. With nothing owed and no fight of the handler's own (<see cref="DeathOptions.Fight"/>
    /// off: the autopilot's known state owns the next move) no trip is made: the handler ends where the body stands and the known state takes the way out by its own rule, so the walk is the climb going on and never the cost of the answer. With the
    /// handler's own fight the way to the ground is the fight's, and it goes as before. <c>route=</c> stays the plan.
    /// </summary>
    private bool NothingOwedHere(IGameSessionView session)
    {
        if (_options.Fight || !_options.RecoverCorpse || !NoneOwed(session))
        {
            return false;
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: nothing is owed (the drop rule, read before any trip, D67) and this station has no fight of its own: no trip from cell 0x{session.State.Self.Position?.CellId ?? 0:X8}; route={Route} stays a plan and the caller's known state takes the way out"));
        return true;
    }

    /// <summary>
    /// M5 4 G1: true while the corpse recall waits for the mana its cast needs (the live gate: the rebuff spent the respawned mage's mana to 1 of 415 and the recall was refused 0x0401, failing the station). The wait is bounded by
    /// <see cref="RecallManaSeconds"/> from the step's start; a cost the maximum mana cannot pay is named and not waited for. The mana regenerates by itself (mana has no stance factor), so nothing is cast meanwhile.
    /// </summary>
    private bool WaitsForRecallMana(IGameSessionView session, double now, uint recallSpell)
    {
        if (!_spells.TryGetValue(recallSpell, out var info) || !session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var max))
        {
            return false; // the spell or the vitals are unknown: the cast's own answer decides, as before
        }

        var cost = GearCasts.ManaCeiling(info, session.State.Self.Inventory.Equipped.Count);
        if (mana >= cost)
        {
            if (_logged.Remove("recall-mana"))
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: the recall's mana is here after {now - _stepStart:0.#} s: {mana} of {max}, {cost} needed for {info.Name}"));
            }

            return false;
        }

        if (cost > max)
        {
            if (_logged.Add("recall-mana-max"))
            {
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: {info.Name} costs {cost} mana and the maximum is {max}: no wait can pay for it; the cast goes out and ACE's answer decides"));
            }

            return false;
        }

        if (Dance.Step(session, now, cost, 0, "the recall") is not (DanceStep.Wait or DanceStep.Done))
        {
            return true; // M5 7 F19 (D92): never wait for mana: the dance brings the recall's mana
        }

        if (now - _stepStart > RecallManaSeconds)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: the recall's mana did not come in {RecallManaSeconds:0.#} s ({mana} of {max}, {cost} needed for {info.Name}): the cast goes out and ACE's answer decides"));
            _logged.Remove("recall-mana");
            return false;
        }

        if (_logged.Add("recall-mana"))
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: the recall waits for mana ({mana} of {max}, {cost} needed for {info.Name}; the last resort, D92: no transfer, no Revitalize Self and no mana potion can be cast): it goes when the mana regenerates, at most {RecallManaSeconds:0.#} s (the rebuff spends the respawned mage's mana)"));
        }

        return true;
    }

    /// <summary>M6 4 F1 (4-R1): the corpse trip's rung change to the ground (the travel seam's Ground leg): arrived and present in the ground's landblocks, the corpse leg; else the handler fails by the leg's name.</summary>
    private ScenarioStatus RungChangeStep(IGameSessionView session, double now)
    {
        var leg = _leg!;
        var status = leg.Step(session, now);
        if (status == TravelStatus.Running)
        {
            return ScenarioStatus.Running;
        }

        var failure = leg.Failure;
        leg.Dispose();
        _leg = null;
        if (status == TravelStatus.Failed)
        {
            return RetryRoute(session, now, "route=rung-change: " + (failure ?? "travel:rung-change:failed"), "route");
        }

        if (session.State.Self.Position is not { } at || !_ground.InLandblocks(at.CellId))
        {
            return RetryRoute(session, now, $"route=rung-change: the rung change ended but the player is not in {_ground.Name}'s landblocks (cell 0x{session.State.Self.Position?.CellId ?? 0:X8})", "route");
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: the rung change brought the mage into {_ground.Name} (cell 0x{at.CellId:X8}); the corpse leg"));
        return BeginCorpseLeg(session, now);
    }

    private ScenarioStatus RecallStep(IGameSessionView session, double now)
    {
        var recall = _recall!;
        switch (recall.Step(now))
        {
            case RecallStatus.Done:
                if (_options.Facts is { } done)
                {
                    done.SetPrimaryPortalTie(_ground.Key); // M6 4 F1 (4-R2): the recall landed in the ground, so the one slot holds it
                    done.Recalls++;
                }

                recall.Dispose();
                _recall = null;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: recalled into {_ground.Name} (cell 0x{session.State.Self.Position?.CellId ?? 0:X8}); {recall.Fields}"));
                return BeginCorpseLeg(session, now);
            case RecallStatus.Failed:
                var refusal = recall.Refusal;
                recall.Dispose();
                _recall = null;
                if (refusal == UntiedRefusal)
                {
                    if (_options.Facts is { } untied)
                    {
                        untied.SetPortalTie(_ground.Key, PortalTie.Untied);
                    }

                    // M6 6 C1 (map-death section 2, gap a): the refusal taught the tie (Untied), so the travel rule is asked again, as the travel seam does (AutopilotTravelRule.Out: "ACE's 0x04A3 turns the travel to the rung change"):
                    // a ground the rule now routes is reached by the rung change with no failure; only a ground with no such way stops at the runner's named TODO.
                    if (_travel is JourneyTravel journey && journey.Decide(session, _ground, RungDirection.Out) is { Way: Autopilot.TravelWay.RungChange } routed)
                    {
                        Route = DeathRoute.RungChange;
                        _log.Warn(LogCategory, $"handler: the recall was refused untied (WeenieError 0x04A3 YouMustLinkToPortalToRecall): the tie is now known untied and the travel rule, asked again, says the rung change (route {routed.Route ?? "n/a"}): {routed.Reason}; route=rung-change");
                        _log.Info(LogCategory, $"walking: purpose=ground (the corpse trip: the rung change to {_ground.Name} after the untied recall, then the corpse leg)");
                        _leg = _travel.GoTo(session, new TravelRequest(_ground.ArrivalDestination(), TravelPurpose.Ground), now);
                        return Enter("rung-change", now);
                    }

                    Route = DeathRoute.Runner;
                    _log.Warn(LogCategory, $"handler: the recall was refused untied (WeenieError 0x04A3 YouMustLinkToPortalToRecall): route=runner ({(_travel is JourneyTravel ? "the travel rule, asked again with the tie untied, gives no rung change" : "this host's travel seam has no travel rule to ask again")})");
                    return Enter("runner", now);
                }

                return Fail("recall: " + recall.Failure + (recall.Detail is null ? string.Empty : ": " + recall.Detail));
            default:
                return ScenarioStatus.Running;
        }
    }

    // ------------------------------------------------------------------------------------------------ M6 6 C2: a corpse lost on a leg

    /// <summary>
    /// M6 6 C2 (map-death section 2, the leg death): the corpse's landblock is not the ground's (and the ground is not hunted from home on foot: that is <c>route=walk</c>), so the death was on a leg (the island run, a mainland walk). The drop rule
    /// is read first: nothing to recover is no trip to the corpse (the route to the ground is decided as for any death, <c>corpse_recovered=none-needed</c>). Else <c>route=leg</c>: the travel place that holds the corpse
    /// (<see cref="RungRoute.PlaceHolding"/>), the rung change out cut there (<see cref="JourneyTravel.ToPlace"/>), then the corpse leg on foot (<see cref="TravelPurpose.Corpse"/>); after the items, the wear and the item lines, the normal rung
    /// change back to the ground and the fight. No place holds it: <c>corpse_recovered=no trip_abort=corpse-off-route</c>, named with the landblock. Null when the death is not a leg death, nothing is to recover, or this host's travel has no
    /// travel file (today's route then).
    /// </summary>
    private ScenarioStatus? LegDeath(IGameSessionView session, double now)
    {
        if (_facts.CorpsePosition is not { } corpse || _ground.InLandblocks(corpse.CellId))
        {
            return null;
        }

        var landblock = RungRoute.Hex(corpse.CellId >> 16);
        if (_travel is not JourneyTravel { Context: { } context } journey)
        {
            if (_logged.Add("leg-no-context"))
            {
                _log.Warn(LogCategory, $"handler: the corpse 0x{_facts.CorpseGuid:X8} lies in landblock {landblock}, off {_ground.Name}'s landblocks (a death on a leg), but this host's travel seam has no travel file to plan a way to it: today's route is decided (route=leg needs the journey's travel)");
            }

            return null;
        }

        if (NoneOwed(session))
        {
            if (_logged.Add("leg-none-needed"))
            {
                _log.Info(LogCategory, $"handler: the corpse 0x{_facts.CorpseGuid:X8} lies in landblock {landblock}, off {_ground.Name}'s landblocks (a death on a leg), and nothing is to recover: no trip to it; the route to the ground is decided as for any death");
            }

            return null;
        }

        Route = DeathRoute.Leg;
        var level = session.State.Self.Qualities.Level;
        var held = RungRoute.PlaceHolding(context.Routes, _ground, level, corpse.CellId, corpse.Frame.Origin);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: the corpse trip's route=recall|runner plan: route={DeathRoute.Leg} (the corpse 0x{_facts.CorpseGuid:X8} lies in cell 0x{corpse.CellId:X8}, off {_ground.Name}'s landblocks: a death on a leg; level {level}); place={held.Key ?? "none"}: {held.Why}"));
        if (held.Key is not { } place)
        {
            return NoCorpse(now, DeathCorpse.No, CorpseOffRouteAbort, $"the corpse 0x{_facts.CorpseGuid:X8} lies in landblock {landblock} (cell 0x{corpse.CellId:X8}), which no travel place holds and no on-foot leg of {_ground.Key}'s route reaches: trip_abort={CorpseOffRouteAbort} ({held.Why})");
        }

        _legPlace = place;
        _log.Info(LogCategory, $"walking: purpose=ground (the corpse trip of a death on a leg: the rung change out to {_ground.Name} cut at {place}, stop_at={place}, then the corpse leg on foot)");
        _leg = journey.ToPlace(session, _ground, place, now);
        return Enter("leg", now);
    }

    /// <summary>M6 6 C2: the rung change cut at the corpse's place: arrived (the station checked the place), the corpse leg; else the handler fails by the leg's name.</summary>
    private ScenarioStatus LegStep(IGameSessionView session, double now)
    {
        var leg = _leg!;
        var status = leg.Step(session, now);
        if (status == TravelStatus.Running)
        {
            return ScenarioStatus.Running;
        }

        var failure = leg.Failure;
        leg.Dispose();
        _leg = null;
        if (status == TravelStatus.Failed)
        {
            return RetryRoute(session, now, "route=leg: " + (failure ?? "travel:rung-change:failed"), "route");
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: the rung change brought the mage to {_legPlace} (cell 0x{session.State.Self.Position?.CellId ?? 0:X8}), the place of the corpse 0x{_facts.CorpseGuid:X8} in cell 0x{_facts.CorpsePosition?.CellId ?? 0:X8}; the corpse leg on foot"));
        return BeginCorpseLeg(session, now);
    }

    /// <summary>
    /// M6 6 C2: after a leg death's corpse (taken back, or given up by name), the way back to the rung: check before (already in the ground: the fight at once), the normal rung change out (the travel seam's Ground leg, as
    /// <c>route=rung-change</c>), check after (present in the ground's landblocks), then the fight; a failure is the handler's by the leg's name. M6 6 FXD1: any route whose fight would begin outside the ground comes here (6-D6), and this way
    /// has its own retry (6-D13).
    /// </summary>
    private ScenarioStatus RungBackStep(IGameSessionView session, double now)
    {
        if (_leg is null && double.IsNaN(_rungBackEnded))
        {
            if (session.State.Self.Position is { } here && _ground.InLandblocks(here.CellId))
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: back to the rung: the mage already stands in {_ground.Name} (cell 0x{here.CellId:X8}); the fight"));
                return Enter("fight", now);
            }

            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"walking: purpose=ground (back to the rung before the fight (route={Route}): the rung change out to {_ground.Name} from cell 0x{session.State.Self.Position?.CellId ?? 0:X8}, then the fight)"));
            _leg = _travel.GoTo(session, new TravelRequest(_ground.ArrivalDestination(), TravelPurpose.Ground), now);
            return ScenarioStatus.Running;
        }

        if (_leg is not null)
        {
            var status = _leg.Step(session, now);
            if (status == TravelStatus.Running)
            {
                return ScenarioStatus.Running;
            }

            var failure = _leg.Failure;
            _leg.Dispose();
            _leg = null;
            if (status == TravelStatus.Failed)
            {
                return RetryRoute(session, now, $"route={Route}: back to the rung: " + (failure ?? "travel:rung-change:failed"), "rung-back");
            }

            _rungBackEnded = now;
        }

        // M6 6 FXD1: the check after reads a placed player: a rung change that ends while the last teleport is still in flight (portal space) is waited for, at most the handler's arrival limit, logged once; then judged
        if (session.State.Self.Travel != SelfTravel.Present && now - _rungBackEnded < _arrivalSeconds)
        {
            if (_logged.Add("rung-back-arrival"))
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: back to the rung: the rung change ended with the player in travel {session.State.Self.Travel}: the check after waits for the arrival (at most {_arrivalSeconds:0} s)"));
            }

            return ScenarioStatus.Running;
        }

        _rungBackEnded = double.NaN;
        _logged.Remove("rung-back-arrival");
        if (session.State.Self.Position is not { } at || !_ground.InLandblocks(at.CellId))
        {
            return RetryRoute(session, now, $"route={Route}: back to the rung: the rung change ended but the player is not in {_ground.Name}'s landblocks (cell 0x{session.State.Self.Position?.CellId ?? 0:X8})", "rung-back");
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: back to the rung: the rung change brought the mage into {_ground.Name} (cell 0x{at.CellId:X8}); the fight"));
        return Enter("fight", now);
    }

    // ------------------------------------------------------------------------------------------------ the corpse

    private ScenarioStatus BeginCorpseLeg(IGameSessionView session, double now)
    {
        _groundAt = now; // the arrival in the ground: --death-recover-abort-seconds counts from here
        LegAllowance(session);
        if (!_options.RecoverCorpse)
        {
            return NoCorpse(now, DeathLegVerdict.NotApplicable, "skipped", "--death-recover-corpse is off: the corpse is not gone back for");
        }

        // M6 6 C1 (map-death section 2, gap c): is there anything to recover? Checked before any corpse walk, on one line with its inputs; an unread diff walks as before and says why (M6 6 C2: read once a death, a leg route reads it first).
        if (NoneOwed(session))
        {
            (Corpse, TripAbort, _tripEnd) = (DeathCorpse.NoneNeeded, DeathLegVerdict.TripAbortNoneNeeded, now);
            _log.Info(LogCategory, $"handler: corpse_recovered={DeathCorpse.NoneNeeded} trip_abort={DeathLegVerdict.TripAbortNoneNeeded}: nothing but pyreals left the inventory at the death, so the corpse 0x{_facts.CorpseGuid:X8} is not walked to (route={Route} planned, not walked); the fight resumes");
            return Enter("wear", now);
        }

        if (_facts.CorpseGuid == 0 || _facts.CorpsePosition is not { } at)
        {
            return NoCorpse(now, DeathCorpse.NotFound, "corpse-not-found", "no corpse of the mage was remembered (the tally saw none between the death and the arrival)");
        }

        var age = now - _facts.DiedAt;
        if (double.IsNaN(age) || age >= DeathScenario.PlayerCorpseMinRotSeconds)
        {
            return NoCorpse(now, DeathCorpse.Rotted, "rotted", string.Create(CultureInfo.InvariantCulture, $"the corpse 0x{_facts.CorpseGuid:X8} is {age:0} s old, at or past ACE's rot floor of {DeathScenario.PlayerCorpseMinRotSeconds:0} s (@95ef426d Corpse.cs 103-118, TimeToRot = max(3600, level * 300) at 110)"));
        }

        if (_travel is PlaceTravel)
        {
            return NoCorpse(now, DeathCorpse.No, "no-runner", $"this host's travel seam is today's place and the handler never places: the corpse 0x{_facts.CorpseGuid:X8} in cell 0x{at.CellId:X8} is not walked to (TODO(m5-death-corpse-runner))");
        }

        return StartCorpseWalk(at, age, session, now);
    }

    private ScenarioStatus StartCorpseWalk(CellFrame at, double age, IGameSessionView session, double now)
    {
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"walking: purpose=corpse (the runner over {_ground.Name}'s room graph to the corpse 0x{_facts.CorpseGuid:X8} in cell 0x{at.CellId:X8}, {age:0} s old; ACE's rot floor {DeathScenario.PlayerCorpseMinRotSeconds:0} s)"));
        _leg = _travel.GoTo(session, new TravelRequest(DeathScenario.CorpseDestination(at), TravelPurpose.Corpse, _facts.CorpseGuid, DeathScenario.CorpseTravelRangeMetres), now);
        return Enter("corpse-walk", now);
    }

    /// <summary>
    /// M6 6 C2 (live round 1): one named recovery for a failed route travel: when a retry is left (<see cref="RouteRetries"/>) and <see cref="CanRetryRoute"/> holds, the route is decided again from where the body stands and started once more
    /// (<paramref name="step"/>: <c>route</c> re-decides the way to the ground or the corpse's place, <c>rung-back</c> the way back to the rung); else the named stop it was.
    /// </summary>
    private ScenarioStatus RetryRoute(IGameSessionView session, double now, string failure, string step)
    {
        if (!CanRetryRoute(session, failure, step == "rung-back"))
        {
            return Fail(failure);
        }

        _logged.Remove("route-plan"); // the decision is logged again
        _logged.Remove("step:" + step);
        return Enter(step, now);
    }

    /// <summary>
    /// M6 6 C2: true (and counted, logged <c>handler: route retry n of max after &lt;failure&gt;</c>) when a route retry is left, the player is present and alive, and the failure is not a death on the leg; else false, logged why. M6 6 FXD1
    /// (6-D13): the way back to the rung (<paramref name="rungBack"/>) counts its own <see cref="RouteRetries"/>, so a retry spent on the way out never fails a station whose items are already back; its lines say <c>(the way back to the rung)</c>.
    /// </summary>
    private bool CanRetryRoute(IGameSessionView session, string failure, bool rungBack = false)
    {
        var self = session.State.Self;
        var alive = self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _) && health > 0;
        var died = failure.Contains("died:", StringComparison.Ordinal) || failure.Contains("dead:", StringComparison.Ordinal) || NewDeath();
        var spent = rungBack ? _rungBackRetries : _routeRetries;
        var whose = rungBack ? " (the way back to the rung's own)" : string.Empty;
        var why = spent >= RouteRetries ? string.Create(CultureInfo.InvariantCulture, $"the {RouteRetries} retry{whose} is spent")
            : died ? "the failure is a death on the leg (the handler restarts for it)"
            : self.Travel != SelfTravel.Present ? $"the player is not present (travel {self.Travel})"
            : !alive ? "the player's health is 0 or unknown"
            : null;
        if (why is not null)
        {
            _log.Warn(LogCategory, $"handler: no route retry ({why}) after {failure}");
            return false;
        }

        spent = rungBack ? ++_rungBackRetries : ++_routeRetries;
        _leg?.Dispose();
        _leg = null;
        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: route retry {spent} of {RouteRetries}{whose} after {failure}; the route is decided again from cell 0x{self.Position?.CellId ?? 0:X8}"));
        return true;
    }

    /// <summary>M6 7 AY: nothing is owed and no coin is fetched: the drop rule's none-needed, unless the coin rule said fetch.</summary>
    private bool NoneOwed(IGameSessionView session) => DropCheck(session).NoneNeeded && !_coinFetch;

    /// <summary>
    /// M6 7 AY (the owner's decision of 2026-11-06; closes <c>m6-corpse-run-gate</c>), read once a death, before any way is taken: the ladder's character asks <see cref="RungOwner.CorpseRun"/> on the buffs that stand after the rebuff. A corpse that owes an item and lies behind a shut gate is not fetched
    /// (<c>corpse_recovered=no trip_abort=gate-shut</c>, an answer: the climb goes on from where she stands); a corpse that owes nothing is fetched only on <see cref="CorpseCoinRule"/>'s yes. Both logged by name, met or not. Null: the trip goes on as before.
    /// </summary>
    private ScenarioStatus? CorpseRunShut(IGameSessionView session, double now)
    {
        if (_corpseRunAsked || !_options.RecoverCorpse || _facts.CorpsePosition is not { } at)
        {
            return null;
        }

        _corpseRunAsked = true;
        var state = session.State;
        var ladder = _options.CorpseLadder ?? (CharacterPolicy.For(state.Self.Name).WarLadder ? LadderLoader.WarMage : null);
        if (ladder is null || !state.Self.Qualities.TryGetInt(PlayerQualities.LevelProperty, out var level))
        {
            return null;
        }

        var buffs = _options.CorpseBuffsForTest ?? LiveBuffs(session, now);
        var ground = RungOwner.GroundOf(ladder, at.CellId) ?? (_ground.InLandblocks(at.CellId) ? _ground : null);
        var gate = RungOwner.CorpseRun(ladder, new RungAsk(level, state.Self.Position?.CellId, state.Self.Qualities, AutopilotScenario.GateKnows(session), buffs) { Trial = _options.Journey?.Facts.Trial }, ground?.Key); // M6 item 14: a corpse on the trial's ground is the trial's gate's
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: corpse run: asked the rung's owner: level={level} corpse_cell=0x{at.CellId:X8} ground={ground?.Key ?? "none"} rung={gate.Rung ?? "none"} -> {(gate.Open ? "open" : "shut")} ({gate.Reason})"));
        if (DropCheck(session).NoneNeeded)
        {
            var ask = new CorpseCoinAsk(ladder.CorpseCoin, CorpseCoinRule.CoinLost(ReadDrops(session).LostLine), at.CellId, ground, gate);
            var answer = CorpseCoinRule.Decide(in ask);
            _coinFetch = answer.Fetch;
            _log.Info(LogCategory, "handler: " + CorpseCoinRule.Line(in ask, answer));
            if (answer.Fetch && CorpseApproachShut(session, at.CellId, ground, level, now) is { } shutForCoin)
            {
                _coinFetch = false;
                return shutForCoin;
            }

            LogLimit(ladder.CorpseCoin, answer.Fetch);
            return null;
        }

        // M6 7 BB: an item is owed; the walk is for what she needs, and only when the corpse's cell is safe (the coin gate's data) and the gate is open. M6 7 finish: the coin counts, when the server leaves it on the corpse
        var owed = ReadDrops(session);
        var lost = owed.LostLine;
        var trip = new CorpseTripAsk(ladder.CorpseCoin, owed.WornMissing, owed.CasterKept, lost is not null && lost.Contains("Healing Kit", StringComparison.OrdinalIgnoreCase), false, null, !owed.LinesRead, at.CellId, ground, gate, CorpseCoinRule.CoinLost(lost));
        var tripAnswer = CorpseCoinRule.DecideTrip(in trip);
        _log.Info(LogCategory, "handler: " + CorpseCoinRule.TripLine(in trip, tripAnswer));
        if ((tripAnswer.Fetch || tripAnswer.Reason == "cell-unsafe") && CorpseApproachShut(session, at.CellId, ground, level, now) is { } shutForItems)
        {
            return shutForItems; // M6 14 retreat attackers (rule 7): a corpse she needs, in an approach she cannot survive, is left by that name, the rot's time with it
        }

        if (tripAnswer.Fetch)
        {
            LogLimit(ladder.CorpseCoin, true);
            return null;
        }

        if (tripAnswer.Reason is "nothing-needed" or "cell-unsafe")
        {
            _log.Warn(LogCategory, "handler: TODO(corpse-item-value): the drop line names items but not their Value, and no quest item is known (TODO(corpse-quest-item)); only worn gear, the caster, a kit and an unread diff count as needed");
            return NoCorpse(now, DeathCorpse.No, tripAnswer.Reason == "cell-unsafe" ? DeathLegVerdict.TripAbortCellUnsafe : DeathLegVerdict.TripAbortNoneNeeded, tripAnswer.Reason == "cell-unsafe"
                ? $"the corpse holds something she needs but lies on cell 0x{at.CellId:X8}, which the ground's corpse_fetch data does not call safe: the handler leaves the corpse and goes on"
                : "the corpse holds nothing she needs (no worn gear, caster or kit; no item value is read): the handler leaves the corpse and goes on");
        }

        return NoCorpse(now, DeathCorpse.No, DeathLegVerdict.TripAbortGateShut, $"the corpse lies on {ground?.Key}, behind a shut gate ({gate.Reason}): a shut gate means no fetch");
    }

    /// <summary>
    /// M6 14 retreat attackers (rule 7; live 2026-11-14: her corpse lay on the Holding's approach, on no rung's ground, so no gate guarded the walk back): a corpse the trip would fetch is first judged by the approach it lies in (an approach
    /// whose landblocks hold its cell) or the approach of the ground it lies in, on her state after the rebuff (<see cref="ApproachCheck.Live"/>). One that she cannot survive is not walked to (<see cref="CorpseApproachRule"/>):
    /// <c>corpse_recovered=no trip_abort=approach</c>, the rot's time named. Null: the trip goes on.
    /// </summary>
    private ScenarioStatus? CorpseApproachShut(IGameSessionView session, uint cell, HuntingGround? ground, int level, double now)
    {
        var place = ground?.Approach is not null ? ground : ApproachCheck.ApproachOf(GroundCatalog.All, cell);
        // M6 14 approach cost (rule 3): the corpse trip reads the way out's verdict: the approach check, the crossing's cost on her state after the rebuff, and a crossing given up this run
        var facts = _options.Facts ?? _options.Journey?.Facts;
        var costWhy = string.Empty;
        _approachLine.Times = CreatureAttackTimes.For(_options.Journey?.Dats); // M6 14 attack timing: the camps' hits at their own timing from the dats
        var verdict = place is null ? null : ApproachCrossing.Judge(session.State, place, _approachLine, now, _options.CorpseLadder ?? LadderLoader.WarMage, _spells, _options.Journey?.Routes ?? TravelRoutes.Default, facts?.ApproachAbandonedOf(place.Key), out costWhy, CreaturePaces.For(_options.Journey?.Dats)); // M6 14 run past (D137): the corpse trip reads the crossing priced as a run past
        if (verdict is { Cost: null, Abandoned: null } && costWhy.Length > 0)
        {
            _log.Info(LogCategory, $"handler: corpse approach: the crossing's cost is not judged: {costWhy}");
        }

        var answer = CorpseApproachRule.Decide(verdict, level, now - _facts.DiedAt);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: corpse approach: corpse_cell=0x{cell:X8} approach={place?.Key ?? "none"} -> {(answer.Walk ? "walk" : "no walk")} ({answer.Reason}): {answer.Why}"));
        return answer.Walk ? null : NoCorpse(now, DeathCorpse.No, DeathLegVerdict.TripAbortApproach, answer.Why);
    }

    private readonly LiveFightLine _approachLine = new(); // M6 14 retreat attackers: her armour and resistance for the corpse trip's approach check

    /// <summary>
    /// M6 8 I (round 1, 19:10 UTC, level 11: 44,664 pyreals on a corpse, fetch:met, given up at the 90 s limit of a walk whose fights had spent 320 s). A corpse worth fetching on a safe, gate-open ground gets the ladder's no-progress limit
    /// (<see cref="LadderCorpseCoin.TripStallSeconds"/> without <see cref="LadderCorpseCoin.TripStallMetres"/> of movement; a fight and a heal hold restart it) in place of <c>--death-recover-abort-seconds</c>, which keeps its meaning for every other trip. One line, with the numbers.
    /// </summary>
    private void LogLimit(LadderCorpseCoin rule, bool fetch)
    {
        _stall = fetch && rule.TripStallSeconds > 0 ? rule : null;
        _stallMark = null;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: trip limit: fetch={(fetch ? "yes" : "no")} -> {(_stall is null ? $"time: {_options.AbortSeconds:0.##} s from the arrival (--death-recover-abort-seconds)" : $"no progress: {rule.TripStallSeconds:0.##} s without {rule.TripStallMetres:0.##} m of movement (the fights and the heal holds do not count); --death-recover-abort-seconds {_options.AbortSeconds:0.##} s does not apply")}"));
    }

    private RungBuffRead LiveBuffs(IGameSessionView session, double now)
    {
        var cycle = new BuffCycle(session, _spells, _buff, _log, ArmourBuffLedger.For(session.State));
        return new RungBuffRead(name => cycle.ActiveLevel(name, now), cycle.ReachLevel);
    }

    /// <summary>M6 6 C1, C2: the drop rule's answer for this death, read and logged once (<c>handler: corpse check: ...</c>).</summary>
    private DeathDropDecision DropCheck(IGameSessionView session)
    {
        if (_drop is { } known)
        {
            return known;
        }

        var drops = ReadDrops(session);
        var decision = DeathDropRule.Decide(in drops);
        _drop = decision;
        _log.Info(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"handler: corpse check: level={drops.Level?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} retained_line={(drops.RetainedLine ? "yes" : "no")} lost_line={(drops.LostLine is { } lost ? "'" + lost + "'" : drops.LinesRead ? "none" : "unread")} worn_before={drops.WornBefore} worn_missing={drops.WornMissing} caster_kept={(drops.CasterKept ? "yes" : "no")} rearm={Rearm} -> {(decision.NoneNeeded ? "none-needed" : "walk")}: {decision.Why}"));
        return decision;
    }

    /// <summary>
    /// M6 6 C1: how long before the corpse's own create ACE's drop line may land and still be this death's, in seconds (Lifestone's own: ACE sends the line and the corpse in the same tick, @95ef426d <c>Creature_Death.cs</c>:540-580,
    /// but in two queues, the player's own and the landblock's, so either may come first; two seconds covers that order and no earlier death, which is at least a respawn's length before).
    /// </summary>
    public const double DropLineSlackSeconds = 2;

    /// <summary>M6 6 C1: what the handler can read at the respawn about what left the inventory (see <see cref="DeathDropRule"/>).</summary>
    private DeathDropInput ReadDrops(IGameSessionView session)
    {
        var state = session.State;
        int? level = state.Self.Qualities.Ints.TryGetValue(PlayerQualities.LevelProperty, out var l) ? l : null;
        var (retained, lost, linesRead) = (false, (string?)null, !double.IsNaN(_facts.DiedAt));
        if (linesRead)
        {
            var since = _facts.DiedAt - DropLineSlackSeconds;
            var chat = state.Chat;
            for (var i = 0; i < chat.Count; i++)
            {
                var line = chat[i];
                if (line.Time < since || line.Kind != ChatKind.System)
                {
                    continue;
                }

                if (string.Equals(line.Text, DeathDropRule.RetainedLine, StringComparison.Ordinal))
                {
                    retained = true;
                }
                else if (line.Text.StartsWith(DeathDropRule.LostPrefix, StringComparison.Ordinal))
                {
                    lost = line.Text;
                }
            }
        }

        var (before, missing) = (0, 0);
        foreach (var guid in _worn.Set.Keys)
        {
            before++;
            if (!state.Self.Inventory.Contains(guid))
            {
                missing++;
            }
        }

        return new DeathDropInput(level, retained, lost, linesRead, before, missing, Rearm == DeathRearm.None);
    }

    private ScenarioStatus NoCorpse(double now, string outcome, string abort, string why)
    {
        (Corpse, TripAbort, _tripEnd) = (outcome, abort, now);
        _log.Warn(LogCategory, $"handler: corpse_recovered={outcome}: {why}; the fight resumes without it");
        return Enter("wear", now);
    }

    // ------------------------------------------------------------------------------------------------ the trip's aborts (M5 3 d4)

    /// <summary>The trip's limits, checked each frame of the walk, the seek, the open and the loot: null when the trip goes on; the time limit aborts at once, the health line holds and heals.</summary>
    private ScenarioStatus? TripGuard(IGameSessionView session, double now)
    {
        if (TripOver(session, now) is { } over)
        {
            return AbortTrip(session, now, over.Reason, over.Why);
        }

        if (TripHostile(session, now, out var hostile, out var reach))
        {
            return BeginTripFight(session, now, hostile, reach);
        }

        var health = Share(session, 1);
        return !double.IsNaN(health) && health < _options.AbortHealthPercent ? BeginTripHeal(now, health) : null;
    }

    /// <summary>
    /// M6 8 I: the trip's limit. A corpse worth fetching (<see cref="_stall"/>): no progress, <c>trip-stalled-&lt;N&gt;s</c>: the body has not moved the ladder's metres since the clock last restarted, which a new step, a fight and a heal hold also do (so a busy trip is not cut and a stuck one is).
    /// Any other trip: <see cref="DeathOptions.AbortSeconds"/> from the arrival, as before.
    /// </summary>
    private (string Reason, string Why)? TripOver(IGameSessionView session, double now)
    {
        if (_stall is not { } rule || double.IsNaN(_groundAt))
        {
            return TripOverByTime(now);
        }

        var at = session.State.Self.Position;
        if (_stallMark is not { } mark || mark.Step != CurrentStep || (at is { } p && p.ToGlobal().DistanceTo(mark.At.ToGlobal()) >= rule.TripStallMetres))
        {
            if (at is { } here)
            {
                _stallMark = (CurrentStep, here, now);
            }

            return null;
        }

        var still = now - mark.Since;
        return still > rule.TripStallSeconds
            ? (string.Create(CultureInfo.InvariantCulture, $"trip-stalled-{rule.TripStallSeconds:0.##}s"), string.Create(CultureInfo.InvariantCulture, $"the body moved under {rule.TripStallMetres:0.##} m in {still:0.#} s in {CurrentStep} (trip_stall_seconds {rule.TripStallSeconds:0.##}, trip_stall_metres {rule.TripStallMetres:0.##}): no progress; the fights and heal holds are not counted"))
            : null;
    }

    private (string Reason, string Why)? TripOverByTime(double now) =>
        !double.IsNaN(_groundAt) && now - _groundAt > _options.AbortSeconds + _legAllowance
            ? (string.Create(CultureInfo.InvariantCulture, $"trip-over-{_options.AbortSeconds:0.##}s"), Route == DeathRoute.Leg && _legPlace is { } place
                ? string.Create(CultureInfo.InvariantCulture, $"the trip took over {_options.AbortSeconds + _legAllowance:0.#} s from the arrival in {place}, the corpse's travel place (--death-recover-abort-seconds {_options.AbortSeconds:0.##} s plus {_legAllowance:0.#} s for the corpse leg's {_legMetres:0} m of straight line at {LegCorpseMinMetresPerSecond:0.0} m/s)")
                : string.Create(CultureInfo.InvariantCulture, $"the trip took over {_options.AbortSeconds:0.##} s from the arrival in {_ground.Name} (--death-recover-abort-seconds)"))
            : null;

    /// <summary>
    /// M6 6 FXD1 (6-D3, the review's live scenario: the Burrow's island run, the corpse up to 609 m from the island's drop, about 85 s of walk before any detour): on <c>route=leg</c> the trip's clock starts at the corpse's travel place, not
    /// in the ground, so the limit adds the corpse leg's straight-line metres from where the mage stands at its start over <see cref="LegCorpseMinMetresPerSecond"/>; logged once with its inputs. Every other route adds nothing (the
    /// corpse lies in the ground the clock starts in). An unmeasurable line (no position, or no corpse position) adds nothing, said so.
    /// </summary>
    private void LegAllowance(IGameSessionView session)
    {
        (_legAllowance, _legMetres) = (0, double.NaN);
        if (Route != DeathRoute.Leg)
        {
            return;
        }

        if (session.State.Self.Position is not { } here || _facts.CorpsePosition is not { } corpse)
        {
            _log.Warn(LogCategory, $"handler: route=leg: the corpse leg's straight line cannot be measured (the mage's position {(session.State.Self.Position is null ? "unknown" : "known")}, the corpse's {(_facts.CorpsePosition is null ? "unknown" : "known")}): the trip's clock is --death-recover-abort-seconds alone, from the arrival in {_legPlace}");
            return;
        }

        var metres = here.ToGlobal().DistanceTo(corpse.ToGlobal());
        if (double.IsNaN(metres) || double.IsInfinity(metres))
        {
            _log.Warn(LogCategory, $"handler: route=leg: the corpse leg's straight line is not a number: the trip's clock is --death-recover-abort-seconds alone, from the arrival in {_legPlace}");
            return;
        }

        (_legMetres, _legAllowance) = (metres, metres / LegCorpseMinMetresPerSecond);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: route=leg: the trip's clock starts in {_legPlace} (cell 0x{here.CellId:X8}), {metres:0} m of straight line from the corpse in cell 0x{corpse.CellId:X8}: the limit is --death-recover-abort-seconds {_options.AbortSeconds:0.##} s plus {_legAllowance:0.#} s ({metres:0} m at {LegCorpseMinMetresPerSecond:0.0} m/s, the wayfinder's assumed run over its route factor) = {_options.AbortSeconds + _legAllowance:0.#} s"));
    }

    private string HealthAbort => string.Create(CultureInfo.InvariantCulture, $"health-under-{_options.AbortHealthPercent:0.##}");

    /// <summary>M6 8 K: the survival layer held the trip for <paramref name="seconds"/>: its step clock shifts by the hold and the no-progress clock restarts, as for the trip's own heal hold.</summary>
    /// <param name="seconds">The seconds held.</param>
    public void NoteHeld(double seconds)
    {
        _stepStart += seconds;
        _stallMark = null;
    }

    /// <summary>The health fell under the line: hold where the mage stands (the runner paused), then heal; the step it was in is resumed with its clock shifted by the hold.</summary>
    private ScenarioStatus BeginTripHeal(double now, double health)
    {
        _tripHealing = true;
        (_resumeStep, _resumeStart, _healStart, _holdCasts) = (CurrentStep, _stepStart, now, 0);
        _leg?.Pause();
        _logged.Remove("trip heal-mode");
        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: the health share fell to {health:0}% during {CurrentStep}, under --death-recover-abort-health {_options.AbortHealthPercent:0.##}%: the trip holds here and heals (the corpse carries the wand and the worn gear, nothing is placed back on foot); it turns back only if the heals cannot bring the share to {Math.Max(_options.RecoverMinHealthPercent, _options.AbortHealthPercent):0.##}%"));
        return Enter("trip-heal", now);
    }

    private ScenarioStatus TripHealStep(IGameSessionView session, double now)
    {
        if (TripOver(session, now) is { } over)
        {
            return AbortTrip(session, now, over.Reason, over.Why);
        }

        var before = _holdCasts;
        var goal = Math.Max(_options.RecoverMinHealthPercent, _options.AbortHealthPercent);
        var outcome = Heal(session, now, goal, ref _holdCasts, "trip heal");
        _tripHeals += _holdCasts - before;
        switch (outcome)
        {
            case HealOutcome.Healthy:
                _tripHealing = false;
                _stallMark = null; // M6 8 I: a hold restarts the no-progress clock
                CurrentStep = _resumeStep;
                _stepStart = _resumeStart + (now - _healStart);
                _leg?.Resume(now);
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: the hold is over after {now - _healStart:0.0} s ({_holdCasts} heal(s)); health {Share(session, 1):0}%: the trip goes on in {CurrentStep}"));
                return ScenarioStatus.Running;
            case HealOutcome.GaveUp:
                return AbortTrip(session, now, HealthAbort, string.Create(CultureInfo.InvariantCulture, $"the health share is {Share(session, 1):0}% after {_holdCasts} heal(s) and the regeneration, under --death-recover-abort-health {_options.AbortHealthPercent:0.##}%"));
            default:
                return ScenarioStatus.Running;
        }
    }

    /// <summary>Gives the corpse up by name: the runner stops, a view is closed fire-and-forget, an open still in flight is closed when it lands, what the looter took is kept (its pick-up in flight is drained); the fight resumes.</summary>
    private ScenarioStatus AbortTrip(IGameSessionView session, double now, string reason, string why)
    {
        var during = _tripHealing || _tripFighting ? _resumeStep : CurrentStep;
        (_tripHealing, _healCast) = (false, null);
        if (_tripFighting)
        {
            _inner?.Dispose();
            _inner = null;
            _tripFighting = false;
        }

        if (_healOpen)
        {
            _healAttempt?.Stop("the hold is over", now);
            _healOpen = false;
        }

        _leg?.Dispose();
        _leg = null;
        if (during == "corpse-open" && _pending is not null)
        {
            _abandonedOpen = _pending;
        }

        _log.Warn(LogCategory, $"handler: trip aborted ({reason}) during {during}: {why}; what was taken is kept and the fight resumes without the rest");
        if (during == "corpse-loot" && _looter is { } looter)
        {
            TripAbort = reason;
            looter.Abandon("abandoned");
            _drainUntil = now + CorpseLooter.DrainSeconds;
            CloseViewFireAndForget(session);
            _pending = null;
            return Enter("corpse-close", now);
        }

        _pending = null;
        CloseViewFireAndForget(session);
        return NoCorpse(now, DeathCorpse.No, reason, why);
    }

    private static void CloseViewFireAndForget(IGameSessionView session)
    {
        var viewed = session.State.Containers.ViewedGuid;
        if (viewed != 0)
        {
            _ = session.Actions.CloseContainer(viewed, ActionSource.Automation);
        }
    }

    private void PollAbandonedOpen(IGameSessionView session)
    {
        if (_abandonedOpen is not { Outcome: { } done })
        {
            return;
        }

        _abandonedOpen = null;
        if (done.Kind == ActionOutcomeKind.Confirmed)
        {
            _log.Info(LogCategory, $"handler: the abandoned open of 0x{_facts.CorpseGuid:X8} landed Confirmed; closing it");
            CloseViewFireAndForget(session);
        }
    }

    // ------------------------------------------------------------------------------------------------ M5 7 F19 (D92): the top-up, the trip fight and the third death

    private static string ThirdDeathStep(string during) => ThirdDeathStop + ": a third death in one handling, during " + during;

    /// <summary>
    /// M5 7 F19 (D92, rule 4): before the trip the mage tops up: Heal Self to <see cref="TopUpPercent"/> of the health (the handler's heal, its mana danced for), the dance to that share of the mana, Revitalize Self to that share of the
    /// stamina; bounded by <see cref="TopUpSeconds"/>. Nothing is waited for: a vital the dance cannot raise (no transfer, no Revitalize Self, no potion) is named and the trip goes as the mage is.
    /// </summary>
    private ScenarioStatus TopUpStep(IGameSessionView session, double now)
    {
        if (!_options.RecoverCorpse)
        {
            return Enter("route", now);
        }

        if (now - _stepStart > TopUpSeconds && !Dance.Busy && !_healOpen && _healCast is null)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: top-up: not full after {TopUpSeconds:0} s (health {Share(session, 1):0}%, mana {Share(session, MageTemplate.MaxMana):0}%, stamina {Share(session, MageTemplate.MaxStamina):0}%); the trip goes as the mage is ({DanceFields})"));
            return Enter("route", now);
        }

        if (!_topupHealOver && !Dance.Busy)
        {
            _healNoWait = true;
            var healed = Heal(session, now, TopUpPercent, ref _topupHeals, "top-up heal");
            _healNoWait = false;
            switch (healed)
            {
                case HealOutcome.Running:
                    return ScenarioStatus.Running;
                case HealOutcome.GaveUp:
                    _topupHealOver = true;
                    _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: top-up: the health is {Share(session, 1):0}% after {_topupHeals} heal(s); the mana and the stamina are topped up and the trip goes"));
                    break;
            }
        }

        var q = session.State.Self.Qualities;
        if (!q.TryGetVitalLevels(MageTemplate.MaxMana, out _, out var manaMax) || !q.TryGetVitalLevels(MageTemplate.MaxStamina, out _, out var staminaMax))
        {
            return Enter("route", now);
        }

        var step = Dance.Step(session, now, (uint)Math.Ceiling(manaMax * TopUpPercent / 100), (uint)Math.Ceiling(staminaMax * TopUpPercent / 100), "the top-up before the corpse trip");
        switch (step)
        {
            case DanceStep.Done:
                if (!_topupHealOver && Share(session, 1) < TopUpPercent)
                {
                    return ScenarioStatus.Running; // the dance's mana pays the next heal
                }

                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: top-up: done in {now - _stepStart:0.#} s: health {Share(session, 1):0}%, mana {Share(session, MageTemplate.MaxMana):0}%, stamina {Share(session, MageTemplate.MaxStamina):0}% ({_topupHeals} heal(s), {DanceFields})"));
                return Enter("route", now);
            case DanceStep.Wait:
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: top-up: the dance cannot go on (no transfer, no Revitalize Self and no mana potion can be cast; D92's last resort is not to wait here): health {Share(session, 1):0}%, mana {Share(session, MageTemplate.MaxMana):0}%, stamina {Share(session, MageTemplate.MaxStamina):0}%; the trip goes"));
                return Enter("route", now);
            default:
                return ScenarioStatus.Running;
        }
    }

    /// <summary>
    /// M5 7 F19 (D92, rule 4): true when a living target creature of the ground (the profile's targets: any ground, D46) stands within <see cref="DeathOptions.TripAggroMetres"/> of the path ahead: the straight line from the mage toward
    /// the corpse, <see cref="TripLookAheadMetres"/> long (at the corpse, within the reach of the mage). The nearest such creature is <paramref name="hostile"/>.
    /// M6 16 E2b (the one world picture): each creature is measured where the session's <see cref="IGameSessionView.Live"/> says it stands (the body the client draws when it draws one and the body can be trusted, else the
    /// server's last report), as the outer loop's watch and the corpse leg's read (<see cref="LegRunPast"/>) take it; a session that draws no bodies answers from the report, as before.
    /// </summary>
    private bool HostileNearPathOwn(IGameSessionView session, out uint hostile, out double reach)
    {
        (hostile, reach) = (0, double.MaxValue);
        if (_options.TripAggroMetres <= 0 || session.State.Self.Position is not { } at)
        {
            return false;
        }

        var self = at.ToGlobal();
        GlobalPosition? corpse = _facts.CorpsePosition is { } c ? c.ToGlobal() : null;
        var wcids = FightTargets.Wcids(_ground); // M6 F2: an outdoor ground's targets are its creatures in the band (a5's FightTargets), not the profile's empty target list
        var live = session.Live;
        foreach (var o in session.State.Objects.All)
        {
            if (o.Position is null || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || !Listed(wcids, o.Weenie.WeenieClassId) || live.At(o) is not { } p)
            {
                continue;
            }

            var d = DeathTripPath.DistanceToPathAhead(self, corpse, p.ToGlobal(), TripLookAheadMetres);
            if (d <= _options.TripAggroMetres && d < reach)
            {
                (hostile, reach) = (o.Guid, d);
            }
        }

        return hostile != 0;
    }

    private static bool Listed(IReadOnlyList<uint> wcids, uint wcid)
    {
        for (var i = 0; i < wcids.Count; i++)
        {
            if (wcids[i] == wcid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// M6 7 AG: what the trip stops for: within <see cref="TripFightBudgetSeconds"/> of its fights, any creature near the path (M5 D92); past them, only when something attacked the mage within <see cref="TripAttackQuietSeconds"/> (M5 D88): the fight back, and then the walk goes on.
    /// A creature that stands near the path and has not attacked no longer holds the trip.
    /// </summary>
    private bool TripHostile(IGameSessionView session, double now, out uint hostile, out double reach)
    {
        if (Route == DeathRoute.Leg)
        {
            return LegRunPast(session, out hostile, out reach); // M6 14 run past (D137): a corpse on a travel leg is fetched by the leg's rule
        }

        if (!HostileNearPath(session, out hostile, out reach))
        {
            return false;
        }

        var spent = TripFightSpentSeconds(now);
        if (spent < TripFightBudgetSeconds)
        {
            return true;
        }

        if (!_tripBudgetLogged)
        {
            _tripBudgetLogged = true;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: trip-fight: the trip's fights spent {spent:0.0} s of their {TripFightBudgetSeconds:0} s ({TripFightKills} kills, {TripFights} fights) and a creature is still near the path: from here the trip fights only what attacks it (M5 D88) and walks on to the corpse"));
        }

        if (now - _lastAttackAt < TripAttackQuietSeconds)
        {
            return true;
        }

        (hostile, reach) = (0, double.MaxValue);
        return false;
    }

    private double TripFightSpentSeconds(double now) => _tripFightSpent + (_tripFighting ? now - _tripFightStart : 0);

    private AttackWatch? _legWatch;
    private readonly RunPastReader _legRunPast = new();
    private readonly List<PresentAttacker> _legPresent = [];

    /// <summary>
    /// M6 14 run past (D137; the owner's amendment of M5 D88 on travel legs): the corpse leg of a death on a travel leg (<c>route=leg</c>: the corpse lies on an approach, off the ground) runs past as the leg's walks do
    /// (<see cref="RunPast"/>, <see cref="RunPastReader"/>): a creature near the path that has not attacked holds nothing (no D92 clearing on a travel leg); an attacker holds the trip for its fight only when the rule must fight it
    /// (it keeps pace and keeps hitting; a ranged one whose hits would bring her to the trip's health line before she is out of its range; at the corpse, any still on her). Only the creatures the trip's fight can take are judged (the
    /// ground's targets, <see cref="HostileNearPath"/>'s). The nearest such is <paramref name="hostile"/>; each verdict is logged once a change.
    /// </summary>
    private bool LegRunPast(IGameSessionView session, out uint hostile, out double reach)
    {
        (hostile, reach) = (0, double.MaxValue);
        if (session.State.Self.Position is not { } at)
        {
            return false;
        }

        if (_legWatch is null)
        {
            _legWatch = new AttackWatch(session, line => _log.Info(LogCategory, "handler: corpse leg: " + line));
            _legWatch.Start();
        }

        LegPresent(session); // M6 16 Q2: the threat board answers the corpse leg's own question (DeathHandler.ThreatShadow.cs)
        if (_legPresent.Count == 0)
        {
            return false;
        }

        var q = session.State.Self.Qualities;
        var speed = CreaturePace.Her(q, out var speedWhy);
        var g = at.ToGlobal();
        var her = new System.Numerics.Vector2((float)g.X, (float)g.Y);
        System.Numerics.Vector2? goal = _facts.CorpsePosition is { } corpse ? new System.Numerics.Vector2((float)corpse.ToGlobal().X, (float)corpse.ToGlobal().Y) : null;
        var above = q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var max) ? health - (_options.AbortHealthPercent / 100.0 * max) : 0;
        var side = new RunPastHer(speed ?? 0, speed is null ? speedWhy + ": every creature keeps pace" : speedWhy, false, CurrentStep != "corpse-walk", above, string.Create(CultureInfo.InvariantCulture, $"the trip's health line {_options.AbortHealthPercent:0}%"));
        var paces = CreaturePaces.For(_options.Journey?.Dats);
        var wcids = FightTargets.Wcids(_ground);
        foreach (var p in _legPresent)
        {
            if (!Listed(wcids, p.Wcid))
            {
                continue; // the trip's fight takes the ground's targets only; another is left to the leg's safety (the trip's health line)
            }

            var a = _legRunPast.Read(session, _legWatch, p, her, goal, side.Speed, paces, CreatureAttackTimes.For(_options.Journey?.Dats));
            var v = RunPast.Judge(in side, in a);
            if (_legRunPast.Note(p.Guid, in v) is { } line)
            {
                _log.Info(LogCategory, "handler: corpse leg: " + line);
            }

            if (v.Call == RunPastCall.Fight && a.Metres < reach)
            {
                (hostile, reach) = (p.Guid, a.Metres);
            }
        }

        return hostile != 0;
    }

    /// <summary>M6 7 AG: each frame, whether something attacked the mage: the tally's hits, evades and spells on the mage moved (0x01B2, 0x01B4, the Magic lines; <see cref="FightTally"/>).</summary>
    private void NoteAttacks(double now)
    {
        var fight = _tally.Fight;
        var attacks = fight.HitsTaken + (long)fight.Evades + fight.SpellDamageTaken + fight.DrainTaken;
        if (_attackMark >= 0 && attacks != _attackMark)
        {
            _lastAttackAt = now;
        }

        _attackMark = attacks;
    }

    /// <summary>A creature is near the path or the corpse: the trip holds (the runner paused) and fights it first; the step it was in resumes when none is near, with the trip's clocks shifted by the fight.</summary>
    private ScenarioStatus BeginTripFight(IGameSessionView session, double now, uint hostile, double reach)
    {
        _tripFighting = true;
        (_resumeStep, _resumeStart, _tripFightStart, _hostile, _tripFightFails, _tripFightRetryAt) = (CurrentStep, _stepStart, now, hostile, 0, double.NaN);
        _leg?.Pause();
        var name = session.State.Objects.TryGet(hostile, out var o) ? o.Name ?? "creature" : "creature";
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: trip-fight: {name} 0x{hostile:X8} is {reach:0.#} m from the path ahead during {CurrentStep} (aggro reach {_options.TripAggroMetres:0.#} m, --death-trip-aggro-m): the trip holds and fights it first (the den fight's own casting, picker and emergency rule), then goes on"));
        Enter("trip-fight", now);
        return TripFightStep(session, now);
    }

    /// <summary>
    /// The trip fight: the composed fight (the same <see cref="FightLegFactory"/> as the handler's own fight, no walk, one kill a leg, <see cref="TripFightSeconds"/>) runs while a creature is near the path; when none is, the trip resumes.
    /// A leg that ends without a kill while the creature stays near holds <see cref="TripFightRetrySeconds"/> and fights again; <see cref="TripFightMaxFails"/> such legs give the trip up by name.
    /// </summary>
    private ScenarioStatus TripFightStep(IGameSessionView session, double now)
    {
        if (_inner is null)
        {
            var near = TripHostile(session, now, out var hostile, out _);
            if (!near)
            {
                return EndTripFight(session, now);
            }

            if (!double.IsNaN(_tripFightRetryAt) && now < _tripFightRetryAt)
            {
                return ScenarioStatus.Running;
            }

            _hostile = hostile;
            TripFights++;
            _inner = _factory(_kit, _spells, _buff, _fight with { Kills = 1, Minutes = 0, Seconds = TripFightSeconds, Loot = false }, TripFightWalk(session, now), _log, _arrivalSeconds, false, FightStages.Mode);
        }

        var status = _inner.Step(session, now);
        if (status == ScenarioStatus.Running)
        {
            return ScenarioStatus.Running;
        }

        var failure = _inner.Failure;
        _inner.Dispose();
        _inner = null;
        if (status == ScenarioStatus.Succeeded)
        {
            _tripFightFails = 0;
            _tripFightRetryAt = double.NaN;
            TripFightKills++;
            _log.Info(LogCategory, "handler: trip-fight: a kill; the path is looked at again");
            return ScenarioStatus.Running;
        }

        if (!TripHostile(session, now, out _, out _))
        {
            return EndTripFight(session, now);
        }

        if (++_tripFightFails >= TripFightMaxFails)
        {
            return AbortTrip(session, now, "hostile-unbeaten", string.Create(CultureInfo.InvariantCulture, $"{TripFightMaxFails} trip fights in a row ended without the creature 0x{_hostile:X8} dead while it stays near the path (last: {failure})"));
        }

        _tripFightRetryAt = now + TripFightRetrySeconds;
        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: trip-fight: the fight ended without a kill ({failure}); a creature is still near the path: the trip holds {TripFightRetrySeconds:0} s and fights again ({_tripFightFails} of {TripFightMaxFails})"));
        return ScenarioStatus.Running;
    }

    /// <summary>
    /// M5 7 F23 (item 4; closes <c>TODO(m3-fight-no-walker)</c> for the corpse trip): the trip fight's walker is the handler's own ground walk (the den fight's runner over the room graph, as the den-fight station's), prepared once, so a bolt
    /// that hits a wall walks to a line as in the fight. The inner fight disposes the walk it is given, so it gets a <see cref="KeptWalk"/>: its dispose only drops the leg, and the handler's own fight keeps the walk. A host with no walk
    /// keeps today's walkless trip fight, named once (<c>TODO(m4-death-walkless-wait)</c>).
    /// </summary>
    private IFightWalk? TripFightWalk(IGameSessionView session, double now)
    {
        if (_walk is null)
        {
            if (!_tripWalkMissingLogged)
            {
                _tripWalkMissingLogged = true;
                _log.Warn(LogCategory, "handler: trip-fight: this host gives the handler no walk: a bolt that hits a wall ends the trip fight by name (TODO(m4-death-walkless-wait))");
            }

            return null;
        }

        if (!_walkPrepared)
        {
            _walkPrepared = true;
            _walk.Prepare(session, now); // the mage stands in the ground on its corpse trip: no place stage prepares it
        }

        if (_walk.Failure is { } failure)
        {
            _log.Warn(LogCategory, "handler: trip-fight: the ground walk failed (" + failure + "): the trip fight has no walker this leg");
            return null;
        }

        if (_tripWalk is null)
        {
            _tripWalk = new KeptWalk(_walk);
            _log.Info(LogCategory, "handler: trip-fight: the trip fight walks with the ground's walk (a bolt that hits a wall walks to a line, M5 7 F23)");
        }

        return _tripWalk;
    }

    /// <summary>M5 7 F23: a walk lent to an inner fight: everything goes to the handler's walk except the dispose, which only drops the leg (the handler's own fight uses the walk after the trip).</summary>
    private sealed class KeptWalk(IFightWalk inner) : IFightWalk
    {
        public string? Failure => inner.Failure;

        public IGroundTour? Tour => inner.Tour;

        public int? LegsRecovered => inner.LegsRecovered;

        public void Prepare(IGameSessionView session, double now) => inner.Prepare(session, now);

        public bool BeginLeg(IGameSessionView session, double now, out string description) => inner.BeginLeg(session, now, out description);

        public FightWalkStatus Tick(IGameSessionView session, double now) => inner.Tick(session, now);

        public void Abandon() => inner.Abandon();

        public bool HasClearLine(in CellFrame from, in CellFrame to) => inner.HasClearLine(in from, in to);

        public int LegStuckRungs => inner.LegStuckRungs; // M5 7 P7b (D139 g)

        public uint LegGoalCell => inner.LegGoalCell;

        public bool JumpInFlight => inner.JumpInFlight; // M6 14 airborne

        public bool BeginLegTo(IGameSessionView session, double now, in CellFrame goal, out string description) => inner.BeginLegTo(session, now, in goal, out description); // M5 7 P7b (D139 j)

        // M6 6 C1 (map-death section 6): the corpse trip's fight keeps the ground walk's whole seam, not only BeginLegTo: the approach trace, the defence spots and the avoided spot are the inner walk's (a default here would read "not traced" and "no spots").
        public bool AvoidSpot(in CellFrame at, double until, out string description) => inner.AvoidSpot(in at, until, out description);

        public LineTrace CellsOnLine(in CellFrame from, in CellFrame to, List<uint> cells) => inner.CellsOnLine(in from, in to, cells);

        public IReadOnlyList<CellFrame> FloorSpots => inner.FloorSpots;

        public void Dispose() => inner.Abandon();
    }

    private ScenarioStatus EndTripFight(IGameSessionView session, double now)
    {
        _tripFighting = false;
        var held = now - _tripFightStart;
        _tripFightSpent += held;
        _stallMark = null; // M6 8 I: a fight restarts the no-progress clock
        CurrentStep = _resumeStep;
        _stepStart = _resumeStart + held;
        if (!double.IsNaN(_groundAt))
        {
            _groundAt += held; // the fight's time does not count against --death-recover-abort-seconds: the trip's time is its walking and its looting
        }

        _leg?.Resume(now);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: trip-fight: {(_tripBudgetLogged ? $"no attacker for {TripAttackQuietSeconds:0} s" : $"no creature within {_options.TripAggroMetres:0.#} m of the path")} after {held:0.0} s; the trip goes on in {CurrentStep} (health {Share(session, 1):0}%, trip_fight_s={_tripFightSpent:0.0} of {TripFightBudgetSeconds:0}, kills={TripFightKills})"));
        return ScenarioStatus.Running;
    }

    // ------------------------------------------------------------------------------------------------ the corpse steps

    private ScenarioStatus CorpseWalkStep(IGameSessionView session, double now)
    {
        if (TripGuard(session, now) is { } guarded)
        {
            return guarded;
        }

        var leg = _leg!;
        var status = leg.Step(session, now);
        if (status == TravelStatus.Running)
        {
            return ScenarioStatus.Running;
        }

        var failure = leg.Failure;
        leg.Dispose();
        _leg = null;
        if (status != TravelStatus.Failed)
        {
            return Enter("corpse-seek", now);
        }

        // M6 6 C2: the corpse walk is the route's last leg: one retry from where the body stands, else the named outcome it was
        return CanRetryRoute(session, "the corpse leg failed: " + failure) && _facts.CorpsePosition is { } at
            ? StartCorpseWalk(at, now - _facts.DiedAt, session, now)
            : NoCorpse(now, DeathCorpse.No, "travel-failed", "the corpse leg failed: " + failure);
    }

    private ScenarioStatus CorpseSeekStep(IGameSessionView session, double now)
    {
        if (TripGuard(session, now) is { } guarded)
        {
            return guarded;
        }

        if (session.State.Objects.TryGet(_facts.CorpseGuid, out _))
        {
            var waited = now - _stepStart;
            if (waited > 0.5)
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: the corpse 0x{_facts.CorpseGuid:X8} appeared {waited:0.0} s after the arrival at its cell, within its {CorpseFindSeconds:0} s window: found, not given up"));
            }

            _pending = session.Actions.OpenContainer(_facts.CorpseGuid, ActionSource.Automation);
            return Enter("corpse-open", now);
        }

        return now - _stepStart > CorpseFindSeconds
            ? NoCorpse(now, DeathCorpse.NotFound, "corpse-not-found", string.Create(CultureInfo.InvariantCulture, $"the corpse 0x{_facts.CorpseGuid:X8} was not in the object table within {CorpseFindSeconds:0} s of the arrival at its cell"))
            : ScenarioStatus.Running;
    }

    private ScenarioStatus CorpseOpenStep(IGameSessionView session, double now)
    {
        if (TripGuard(session, now) is { } guarded)
        {
            return guarded;
        }

        if (_pending is not { Outcome: { } opened })
        {
            if (now - _stepStart <= ActionSeconds)
            {
                return ScenarioStatus.Running;
            }

            _abandonedOpen = _pending; // an open that lands late is closed when it lands (PollAbandonedOpen)
            _pending = null;
            return NoCorpse(now, DeathCorpse.No, "open-refused", string.Create(CultureInfo.InvariantCulture, $"the open was not answered within {ActionSeconds:0} s"));
        }

        _pending = null;
        if (opened.Kind != ActionOutcomeKind.Confirmed || session.State.Containers.Viewed is not { } view || view.Guid != _facts.CorpseGuid)
        {
            CloseViewFireAndForget(session);
            return NoCorpse(now, DeathCorpse.No, "open-refused", "the open of 0x" + _facts.CorpseGuid.ToString("X8", CultureInfo.InvariantCulture) + " was not confirmed with a view (" + opened.Kind + ")");
        }

        CorpseEntries = view.Entries.Count;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: opened the corpse 0x{_facts.CorpseGuid:X8}: {CorpseEntries} item(s); taking everything (take-all)"));
        _looter = new CorpseLooter(_log, LogCategory, _facts.CorpseGuid, MakeTakeAll(session)); // M5 7 D121: each item back into the pack of its job
        return Enter("corpse-loot", now);
    }

    private ScenarioStatus CorpseLootStep(IGameSessionView session, double now)
    {
        if (TripGuard(session, now) is { } guarded)
        {
            return guarded;
        }

        if (_looter!.Step(session, now) == LootStatus.Running)
        {
            return ScenarioStatus.Running;
        }

        _pending = session.Actions.CloseContainer(_facts.CorpseGuid, ActionSource.Automation);
        return Enter("corpse-close", now);
    }

    private ScenarioStatus CorpseCloseStep(IGameSessionView session, double now)
    {
        if (_looter is { IsDraining: true } draining && now < _drainUntil)
        {
            draining.Step(session, now); // an abort left a pick-up in flight: settle it (its outcome is counted)
            return ScenarioStatus.Running;
        }

        if (_pending is not { Outcome: { } } && now - _stepStart <= ActionSeconds)
        {
            return ScenarioStatus.Running;
        }

        _pending = null;
        var looter = _looter!;
        foreach (var guid in looter.LeftGuids)
        {
            _leftOnCorpse.Add(guid); // M6 7 AH: left on purpose, not lost: the wear step's check after does not count them
        }

        var accounted = looter.Looted + looter.Left; // M6 7 AH: an entry left because the replacement fills its slot is accounted for
        Corpse = accounted <= 0 ? DeathCorpse.No
            : accounted >= CorpseEntries && looter is { Refused: 0, TimedOut: 0, Skipped: 0 } ? DeathCorpse.Yes
            : DeathCorpse.Partial;
        _tripEnd = now;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: corpse_recovered={Corpse} items_back={ItemsBack} of {CorpseEntries} ({looter.Summary}){(TripAbort == DeathLegVerdict.TripAbortNone ? string.Empty : "; trip_abort=" + TripAbort)}"));
        return Enter("wear", now);
    }

    // ------------------------------------------------------------------------------------------------ the items back

    /// <summary>The kit's wand, then its armour in kit order: a line with no equipped object of its weenie class and one carried in the pack is wielded or worn (GameAction 0x001A, as the gear scenario's wield); nothing is spawned.</summary>
    private ScenarioStatus WearStep(IGameSessionView session, double now)
    {
        if (_pending is { } wield)
        {
            if (wield.Outcome is not { } done)
            {
                return now - _stepStart > ActionSeconds ? WearNext(now, "the wield was not answered") : ScenarioStatus.Running;
            }

            _pending = null;
            if (_freeingSlot)
            {
                _freeingSlot = false;
                if (done.Kind != ActionOutcomeKind.Confirmed)
                {
                    _log.Warn(LogCategory, "handler: the caster in the wand's slot did not go to the pack: " + done.Kind);
                }
            }
            else if (done.Kind == ActionOutcomeKind.Confirmed)
            {
                Rewielded++;
            }
            else
            {
                _log.Warn(LogCategory, "handler: a wield was not confirmed: " + done.Kind);
            }
        }

        var inventory = session.State.Self.Inventory;
        var lines = _kit.Items;
        while (_wearIndex < lines.Count)
        {
            var item = lines[_wearIndex++];
            if (item.Kind is not (GearKind.Wand or GearKind.Armour))
            {
                continue;
            }

            if (Find(session, item, out var carried))
            {
                continue; // equipped already (the death did not take it)
            }

            if (carried == 0)
            {
                _log.Warn(LogCategory, $"handler: the {item.Name} (weenie {item.Wcid.ToString(CultureInfo.InvariantCulture)}) is neither equipped nor in the pack: lost with the corpse, nothing spawns it (D51)");
                continue;
            }

            if (item.Kind == GearKind.Wand && !_slotFreed && CombatModeGuard.Read(session.State).Caster is { } other && other.Guid != carried)
            {
                // M5 3 gate round 3: the rearm wielded another caster (the new character's Training Wand) and ACE refuses a wield into the taken held slot; that caster goes to the pack first
                // (Event_PutItemInContainer through IActionApi.Unwield, as the gear scenario's blocking piece), once, and the loop comes back to this line.
                _log.Info(LogCategory, $"handler: the {other.Name} 0x{other.Guid:X8} holds the wand's slot: it goes to the pack first, then the {item.Name}");
                _wearIndex--;
                _slotFreed = true;
                _freeingSlot = true;
                _pending = Quartermaster.Wardrobe.SendTakeOff(session, new Quartermaster.WieldBlocker(other.Guid, other.Name)); // M6 16 E5e1: the wear service's take-off
                _stepStart = now;
                return ScenarioStatus.Running;
            }

            _log.Info(LogCategory, (item.Kind == GearKind.Wand ? "handler: wield " : "handler: wear the ") + item.Name + " again from the pack");
            _pending = Quartermaster.Wardrobe.Send(session, new Quartermaster.WieldRequest(carried) { Name = item.Name, Valid = (uint)item.ValidLocations }, new Quartermaster.WieldResult()); // M6 16 E5e1: the one wield routine's send (the piece was read held and not worn just above; the slot by its one rule); the check after is this step's own
            _stepStart = now;
            return ScenarioStatus.Running;
        }

        // M5 7 S2d (closes TODO(m5-self-equip-death-rewear)): the take-back put every piece into a pack and the loop above wore the kit's lines only; the worn set of before the death (the shirt, the breeches, the cantrip pieces),
        // the clothing whose coverage is free and the best caster held go back on now.
        _wornBefore ??= new Dictionary<uint, uint>(_worn.Set);
        _restorer ??= new WornRestorer(_ground, session.State.Self.Qualities.Level, _spells, _log, LogCategory, "the death's take-back", _worn);
        if (_restorer.Step(session))
        {
            return ScenarioStatus.Running;
        }

        WornRestored = _restorer.Restored;
        int worn = 0, wanted = 0;
        var wand = true;
        if (lines.Count == 0)
        {
            // M6 0 F3: no kit plan to count against: the check after is the state against what the character wore before the death (the memory the restorer wore it back from) and a caster wielded again.
            wand = CombatModeGuard.Read(session.State).Caster is not null;
            foreach (var guid in _wornBefore!.Keys)
            {
                if (_leftOnCorpse.Contains(guid))
                {
                    _log.Info(LogCategory, $"handler: the piece 0x{guid:X8} worn before the death was left on the corpse (replaced at the vendor): not counted");
                    continue;
                }

                wanted++;
                if (session.State.Self.Inventory.TryGetEquipped(guid, out _))
                {
                    worn++;
                }
                else
                {
                    _log.Warn(LogCategory, $"handler: the piece 0x{guid:X8} worn before the death is not worn again (lost with the corpse, or refused)");
                }
            }
        }

        foreach (var item in lines)
        {
            if (item.Kind is not (GearKind.Wand or GearKind.Armour))
            {
                continue;
            }

            var on = Find(session, item, out _);
            if (item.Kind == GearKind.Wand)
            {
                wand &= on;
            }
            else
            {
                wanted++;
                worn += on ? 1 : 0;
            }
        }

        GearBack = wand && worn == wanted;
        WornFields = string.Create(CultureInfo.InvariantCulture, $"gear={(wand ? "wielded" : "missing")} worn={worn}");
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: worn_restored={WornRestored} (the worn set, the free-coverage clothing, the cantrip pieces and the best caster held, M5 7 S2d)"));
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: the items back: items_back={ItemsBack}, {Rewielded} wielded or worn again from the pack; {WornFields}"));
        return Enter("relines", now);
    }

    private ScenarioStatus WearNext(double now, string why)
    {
        _pending = null;
        _freeingSlot = false;
        _log.Warn(LogCategory, "handler: " + why);
        _stepStart = now;
        return ScenarioStatus.Running;
    }

    /// <summary>True when an object of the item's weenie class is equipped within its locations; else <paramref name="carried"/> is one in the pack (or 0).</summary>
    private bool Find(IGameSessionView session, GearItem item, out uint carried)
    {
        carried = 0;
        var inventory = session.State.Self.Inventory;
        GearInventory.Collect(session.State, _scratch);
        foreach (var guid in _scratch)
        {
            if (!session.State.Objects.TryGet(guid, out var o) || o.Weenie.WeenieClassId != item.Wcid)
            {
                continue;
            }

            if (inventory.TryGetEquipped(guid, out var e) && e.Location != 0 && (e.Location & ~(uint)item.ValidLocations) == 0)
            {
                return true;
            }

            if (carried == 0 && inventory.Find(guid).Kind != InventoryPlaceKind.Equipped)
            {
                carried = guid;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------------------------------------ the fight

    private ScenarioStatus BeginFight(IGameSessionView session, double now)
    {
        if (!_options.Fight)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: --death-fight is off: the handler ends where the body stands (cell 0x{session.State.Self.Position?.CellId ?? 0:X8}, {(session.State.Self.Position is { } end && _ground.InLandblocks(end.CellId) ? "on the ground" : "off the ground")})"));
            return Done(now);
        }

        if (_walk is null)
        {
            _log.Warn(LogCategory, "handler: no walk for the composed fight in this host: the handler ends in the ground without fighting (TODO(m4-death-walkless-wait))");
            return Done(now);
        }

        // M6 6 FXD1 (6-D6, generalising C2's leg arm): check before the fight: the fight's walk is prepared on the assumption that the mage stands in the ground, so a mage outside its landblocks (a leg death's corpse place; route=walk
        // with nothing to recover, still at the Holtburg lifestone) goes back to the rung first by the normal rung change, which checks before and after. The rung change is the journey's travel (JourneyTravel's Ground leg, the
        // only Ground leg the handler has ever asked for: the route's rung change and the leg death both need it); another host's seam may answer a Ground leg with the mod's place, which the handler never sends, so there the
        // fight begins where the mage stands, as before, said loudly (TODO(m6-death-fight-outside-ground)).
        if (Route == DeathRoute.Leg || session.State.Self.Position is not { } here || !_ground.InLandblocks(here.CellId))
        {
            var cell = session.State.Self.Position?.CellId ?? 0;
            if (Route == DeathRoute.Leg || _travel is JourneyTravel)
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: before the fight: the mage stands in cell 0x{cell:X8}, outside {_ground.Name}'s landblocks (route={Route}): back to the rung first"));
                return Enter("rung-back", now);
            }

            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: before the fight: the mage stands in cell 0x{cell:X8}, outside {_ground.Name}'s landblocks (route={Route}), and this host's travel seam has no rung change (the handler never places): the fight begins here, as before (TODO(m6-death-fight-outside-ground))"));
        }

        return Enter("fight", now);
    }

    private ScenarioStatus FightStep(IGameSessionView session, double now)
    {
        if (_inner is null)
        {
            if (HoldBeforeFight?.Invoke(session) == true)
            {
                return ScenarioStatus.Running;
            }

            if (!_walkPrepared)
            {
                _walkPrepared = true;
                _walk!.Prepare(session, now); // the mage stands in the ground: the inner fight has no place stage that would prepare it
                if (_walk.Failure is { } walkFailure)
                {
                    return Fail("fight: " + walkFailure);
                }
            }

            _inner = _factory(_kit, _spells, _buff, _fight, _walk, _log, _arrivalSeconds, false, FightStages.Mode);
        }

        var inner = _inner;
        var status = inner.Step(session, now);
        Kills = inner.Kills;
        switch (status)
        {
            case ScenarioStatus.Failed:
                return Fail("fight: " + inner.Failure);
            case ScenarioStatus.Succeeded:
                inner.Dispose();
                _inner = null;
                _log.Info(LogCategory, $"fight: done; den_kills={Kills}");
                return Done(now);
            default:
                return ScenarioStatus.Running;
        }
    }

    private ScenarioStatus Done(double now)
    {
        _verbEnd ??= _verbs?.Counts;
        _log.Info(LogCategory, "handler: " + Fields + " trip_abort=" + TripAbort + " trip_heals=" + TripHeals.ToString(CultureInfo.InvariantCulture));
        Enter("done", now);
        return ScenarioStatus.Succeeded;
    }

    // ------------------------------------------------------------------------------------------------ helpers

    private ScenarioStatus Enter(string step, double now)
    {
        CurrentStep = step;
        _stepStart = now;
        if (_logged.Add("step:" + step))
        {
            _log.Info(LogCategory, "handler " + step);
        }

        return ScenarioStatus.Running;
    }

    private ScenarioStatus Fail(string why)
    {
        Failure = why;
        _verbEnd ??= _verbs?.Counts;
        if (Corpse == DeathLegVerdict.NotApplicable && !double.IsNaN(_tripStart))
        {
            Corpse = DeathCorpse.No;
        }

        DropInFlight("failed");
        _log.Error(LogCategory, "handler: " + why);
        return ScenarioStatus.Failed;
    }
}

/// <summary>The handler's <c>corpse_recovered=</c> values.</summary>
public static class DeathCorpse
{
    /// <summary>Every entry of the opened corpse came back.</summary>
    public const string Yes = "yes";

    /// <summary>Some came back.</summary>
    public const string Partial = "partial";

    /// <summary>A trip was made (or could not be) and nothing came back.</summary>
    public const string No = "no";

    /// <summary>The corpse was past ACE's rot floor.</summary>
    public const string Rotted = "rotted";

    /// <summary>The corpse was not in the object table at its cell, or none was remembered.</summary>
    public const string NotFound = "not-found";

    /// <summary>M6 6 C1: nothing but pyreals left the inventory at the death (<see cref="DeathDropRule"/>): no trip is made and the fight resumes; the judges read it as recovered.</summary>
    public const string NoneNeeded = "none-needed";
}

/// <summary>
/// M6 7 U (r1f-climb): whether a death station that did not end ok still answered for the corpse, so the autopilot neither runs it again nor stops the night. <c>none-needed</c> is an answer however the station ended (nothing is owed). A verdict
/// that fails after the handler finished (<c>why=verdict</c>: the handler's order ran to its end) keeps the handler's named end: a recovered corpse, a rotted one, one not found, or <c>corpse_recovered=no</c> with a named <c>trip_abort=</c>. A plain
/// <c>corpse_recovered=no</c> from a failed handler (no named abort) is no answer: the trip is still owed.
/// </summary>
public static class DeathAnswer
{
    /// <summary>The failure text of a station that ran to its end and whose verdict failed (the autopilot's <c>why</c>).</summary>
    public const string VerdictOnly = "verdict";

    /// <summary>The answer, <c>corpse_recovered=&lt;c&gt; trip_abort=&lt;a&gt;</c>, or null when the corpse is still owed.</summary>
    /// <param name="why">The station's failure (<see cref="VerdictOnly"/>, a handler's failure, or null for a cut).</param>
    /// <param name="corpse">The station's <c>corpse_recovered=</c>, or null.</param>
    /// <param name="abort">The station's <c>trip_abort=</c>, or null.</param>
    public static string? Of(string? why, string? corpse, string? abort)
    {
        var line = $"corpse_recovered={corpse ?? DeathLegVerdict.NotApplicable} trip_abort={abort ?? DeathLegVerdict.NotApplicable}";
        if (corpse == DeathCorpse.NoneNeeded)
        {
            return line;
        }

        if (why != VerdictOnly)
        {
            return null;
        }

        var named = abort is not null and not DeathLegVerdict.TripAbortNone and not DeathLegVerdict.NotApplicable;
        return corpse is DeathCorpse.Yes or DeathCorpse.Partial or DeathCorpse.Rotted or DeathCorpse.NotFound || named ? line : null;
    }
}

/// <summary>The handler's <c>rearm=</c> values (M5 3 F5, D53).</summary>
public static class DeathRearm
{
    /// <summary>A caster was wielded after the respawn: no rearm.</summary>
    public const string None = "none";

    /// <summary>A caster held in the pack was wielded.</summary>
    public const string Pack = "pack";

    /// <summary>The ground's kit wand was bought at its vendor and wielded.</summary>
    public const string Bought = "bought";

    /// <summary>M6 0 F3: no caster is wielded or held and the profile has no kit: the corpse trip goes first and the wear step wields the best caster again.</summary>
    public const string Corpse = "corpse";

    /// <summary>No caster could be wielded or bought: the handler failed by name.</summary>
    public const string Failed = "failed";
}

/// <summary>The handler's <c>route=</c> values (D51: a plan value).</summary>
public static class DeathRoute
{
    /// <summary>Primary Portal Recall: the mage holds the ground's tie.</summary>
    public const string Recall = "recall";

    /// <summary>M6 0 F3: no portal at all: an outdoor ground hunted from home on foot; the corpse walk starts at the lifestone.</summary>
    public const string Walk = "walk";

    /// <summary>The runner from the lifestone to the ground's portal and through it.</summary>
    public const string Runner = "runner";

    /// <summary>M6 4 F1 (4-R1): the rung change (<see cref="RungChangeStation"/>) through the travel seam's Ground purpose, where the travel rule says so (a routed ground: untieable, or its tie not known tied).</summary>
    public const string RungChange = "rung-change";

    /// <summary>M6 7 U: the body already stands in the ground (a station entered again after a cut): no travel, the corpse leg from where it stands.</summary>
    public const string Here = "here";

    /// <summary>M6 6 C2: the corpse lies on a leg, off the ground (the island run, a mainland walk): the rung change out cut at the travel place that holds it, the corpse leg on foot, then the rung change back to the ground.</summary>
    public const string Leg = "leg";
}

/// <summary>M5 7 F19 (D92): the corpse trip's path ahead, a straight stand-in for the runner's route (<c>TODO(f19-trip-path-geometry)</c>). Engine-free.</summary>
public static class DeathTripPath
{
    /// <summary>
    /// The distance from <paramref name="creature"/> to the path ahead: the segment from <paramref name="self"/> toward <paramref name="corpse"/>, cut to <paramref name="lookAhead"/> metres (the mage's own position when the corpse is
    /// unknown or reached).
    /// </summary>
    /// <param name="self">The mage.</param>
    /// <param name="corpse">The corpse, or null.</param>
    /// <param name="creature">The creature.</param>
    /// <param name="lookAhead">How far ahead the path is taken, in metres.</param>
    public static double DistanceToPathAhead(GlobalPosition self, GlobalPosition? corpse, GlobalPosition creature, double lookAhead)
    {
        if (corpse is not { } c)
        {
            return creature.DistanceTo(self);
        }

        var (dx, dy, dz) = (c.X - self.X, c.Y - self.Y, c.Z - self.Z);
        var length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (length < 1e-6)
        {
            return creature.DistanceTo(self);
        }

        var along = (((creature.X - self.X) * dx) + ((creature.Y - self.Y) * dy) + ((creature.Z - self.Z) * dz)) / length;
        var t = Math.Clamp(along, 0, Math.Min(length, lookAhead)) / length;
        return creature.DistanceTo(new GlobalPosition(self.X + (dx * t), self.Y + (dy * t), self.Z + (dz * t)));
    }
}
