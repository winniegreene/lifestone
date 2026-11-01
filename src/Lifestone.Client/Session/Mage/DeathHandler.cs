using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
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
public sealed class DeathHandler : IDisposable
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

    private readonly HuntingGround _ground;
    private readonly DeathTally _tally;
    private readonly IJourneyTravel _travel;
    private readonly FightLegFactory _factory;
    private readonly IFightWalk? _walk;
    private readonly GearKit _kit;
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
    private ActionHandle? _pending;
    private ActionHandle? _healCast;
    private CastAttempt? _healAttempt;
    private bool _healOpen;
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
    private uint _hostile;

    /// <summary>M5 7 F19 (D92): the trip fights (a creature near the path or the corpse fought before the trip went on), report-only.</summary>
    public int TripFights { get; private set; }

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
    }

    /// <summary>The step the handler is in: <c>idle</c>, <c>respawn</c>, <c>rearm</c> (only when a caster is to be wielded or bought), <c>rebuff</c>, <c>heal</c>, <c>route</c>, <c>recall</c>, <c>runner</c>, <c>corpse-walk</c>, <c>trip-heal</c>, <c>corpse-seek</c>, <c>corpse-open</c>, <c>corpse-loot</c>, <c>corpse-close</c>, <c>wear</c>, <c>relines</c>, <c>fight</c>, <c>second-death</c>, <c>done</c>.</summary>
    public string CurrentStep { get; private set; } = "idle";

    /// <summary>Why the handler failed, or null.</summary>
    public string? Failure { get; private set; }

    /// <summary><c>deaths=</c>: the deaths the handler handled (the first and every restart).</summary>
    public int Deaths { get; private set; }

    /// <summary><c>corpse_recovered=</c>: <c>yes</c>, <c>partial</c>, <c>no</c>, <c>rotted</c>, <c>not-found</c>, or <c>n/a</c> before the trip.</summary>
    public string Corpse { get; private set; } = DeathLegVerdict.NotApplicable;

    /// <summary><c>rearm=</c> (M5 3 F5, D53; report-only): <see cref="DeathRearm.None"/> (a caster was wielded), <see cref="DeathRearm.Pack"/>, <see cref="DeathRearm.Bought"/>, <see cref="DeathRearm.Failed"/>, or <c>n/a</c> before the respawn's end.</summary>
    public string Rearm { get; private set; } = DeathLegVerdict.NotApplicable;

    /// <summary><c>route=</c>: <c>recall</c>, <c>runner</c> or <c>n/a</c> (no trip planned yet).</summary>
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

    /// <summary>The verdict fields, additive at the end of the death's line (report-only): <c>deaths= corpse_recovered= route= items_back= vitae_grants= rebuffed= rebuffed_pretrip= relines= rearm=</c> (the handler's <c>deaths=</c> and <c>rebuffed=</c> are the only ones on the death line: the respawn measures are <c>respawn_deaths=</c> and <c>stage_rebuffed=</c>, 3-R2).</summary>
    public string Fields => string.Create(CultureInfo.InvariantCulture, $"deaths={Deaths} corpse_recovered={Corpse} route={Route} items_back={ItemsBack} vitae_grants={(VitaeGrants < 0 ? DeathLegVerdict.NotApplicable : VitaeGrants.ToString(CultureInfo.InvariantCulture))} rebuffed={Rebuffed} rebuffed_pretrip={RebuffedPretrip} relines={Relines} rearm={Rearm}");

    /// <summary>The fields before any handler ran: every one <c>n/a</c> (a constant 0 for <c>vitae_grants=</c> would pass the judge on a run that never counted, 3-R1).</summary>
    public const string UnmeasuredFields = "deaths=n/a corpse_recovered=n/a route=n/a items_back=n/a vitae_grants=n/a rebuffed=n/a rebuffed_pretrip=n/a relines=n/a rearm=n/a";

    /// <summary>Starts the handler after a death.</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The session clock.</param>
    /// <param name="facts">The death's corpse.</param>
    /// <param name="boundLifestone">The lifestone the respawn is classified against, or null (any lifestone object).</param>
    /// <param name="respawned">True when the caller already waited for the arrival and the vitals (the death loop's <c>respawn-vitals</c>): the respawn is classified at once.</param>
    public void Begin(IGameSessionView session, double now, in DeathFacts facts, CellFrame? boundLifestone = null, bool respawned = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        (_facts, _bound, _lastNow) = (facts, boundLifestone, now);
        Deaths = 1;
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
            "rebuff" => BuffStep(session, now, relines: false),
            "heal" => HealStep(session, now),
            "topup" => TopUpStep(session, now),
            "trip-fight" => TripFightStep(session, now),
            "route" => RouteStep(session, now),
            "recall" => RecallStep(session, now),
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
        _looter = null;
        _restorer = null;
        (Rearm, _shop) = (DeathLegVerdict.NotApplicable, null);
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
            return Enter("rebuff", now);
        }

        Enter("rearm", now);
        var spare = GearInventory.PackCaster(session.State, _scratch);
        if (spare != 0)
        {
            _log.Info(LogCategory, $"handler: rearm: no caster is wielded (the wand went to the corpse: ACE drops the wielded wand among the 6 to 8 most valuable unbonded items of a level-127 character, Player_Death.cs 488-505, 650-672 @95ef426d) and the pack holds one, 0x{spare:X8}: wield it in the held slot; no walk, no buy");
            return WieldRearm(session, now, spare, DeathRearm.Pack, session.State.Objects.TryGet(spare, out var item) ? item.Name : "caster");
        }

        var wands = _ground.Supplies.Kit.Where(k => k.Slot == KitSlot.Wielded).ToList();
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
        _pending = session.Actions.Wield(guid, EquipMask.Held, ActionSource.Automation);
        _stepStart = now;
        return ScenarioStatus.Running;
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
                    _log.Info(LogCategory, $"handler: rearm={Rearm}: the {_rearmLine} is wielded; the rebuff follows");
                    return Enter("rebuff", now);
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

    private static bool IsBuffing(string stage) => stage is "start" or "gear" or "buffs" or "buffs-mode" or "buffs-cycle";

    /// <summary>The rebuff (what the mage still holds) and, after the items came back, the item lines: the composed inner fight's buff stage alone, dropped at its own stage <c>place</c>.</summary>
    private ScenarioStatus BuffStep(IGameSessionView session, double now, bool relines)
    {
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

                return relines ? BeginFight(now) : Enter("heal", now);
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
            return BeginFight(now);
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

    private static readonly uint[] HealLevels = [DeathScenario.HealSelfVii];

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

        if (casts >= DeathScenario.PreTripMaxHeals || !_spells.TryGetValue(DeathScenario.HealSelfVii, out var heal))
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
        _healAttempt.Start(new CastRequest { Levels = HealLevels, Rules = CastRules.Once, Purpose = "handler " + what, Source = ActionSource.Automation }, now);
        return HealOutcome.Running;
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

        var portal = _ground.Portal;
        var tied = _options.Facts?.PortalTieOf(_ground.Key) ?? PortalTie.Unknown; // the tie of THIS ground (M5 3 p1, 2-R12)
        string why;
        if (portal is null)
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

        if (Route == DeathRoute.Runner)
        {
            return Enter("runner", now);
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

    private ScenarioStatus RecallStep(IGameSessionView session, double now)
    {
        var recall = _recall!;
        switch (recall.Step(now))
        {
            case RecallStatus.Done:
                if (_options.Facts is { } done)
                {
                    done.SetPortalTie(_ground.Key, PortalTie.Tied);
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

                    Route = DeathRoute.Runner;
                    _log.Warn(LogCategory, "handler: the recall was refused untied (WeenieError 0x04A3 YouMustLinkToPortalToRecall): route=runner");
                    return Enter("runner", now);
                }

                return Fail("recall: " + recall.Failure + (recall.Detail is null ? string.Empty : ": " + recall.Detail));
            default:
                return ScenarioStatus.Running;
        }
    }

    // ------------------------------------------------------------------------------------------------ the corpse

    private ScenarioStatus BeginCorpseLeg(IGameSessionView session, double now)
    {
        _groundAt = now; // the arrival in the ground: --death-recover-abort-seconds counts from here
        if (!_options.RecoverCorpse)
        {
            return NoCorpse(now, DeathLegVerdict.NotApplicable, "skipped", "--death-recover-corpse is off: the corpse is not gone back for");
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

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"walking: purpose=corpse (the runner over {_ground.Name}'s room graph to the corpse 0x{_facts.CorpseGuid:X8} in cell 0x{at.CellId:X8}, {age:0} s old; ACE's rot floor {DeathScenario.PlayerCorpseMinRotSeconds:0} s)"));
        _leg = _travel.GoTo(session, new TravelRequest(DeathScenario.CorpseDestination(at), TravelPurpose.Corpse, _facts.CorpseGuid, DeathScenario.CorpseTravelRangeMetres), now);
        return Enter("corpse-walk", now);
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
        if (TripOver(now) is { } over)
        {
            return AbortTrip(session, now, over.Reason, over.Why);
        }

        if (HostileNearPath(session, out var hostile, out var reach))
        {
            return BeginTripFight(session, now, hostile, reach);
        }

        var health = Share(session, 1);
        return !double.IsNaN(health) && health < _options.AbortHealthPercent ? BeginTripHeal(now, health) : null;
    }

    private (string Reason, string Why)? TripOver(double now) =>
        !double.IsNaN(_groundAt) && now - _groundAt > _options.AbortSeconds
            ? (string.Create(CultureInfo.InvariantCulture, $"trip-over-{_options.AbortSeconds:0.##}s"), string.Create(CultureInfo.InvariantCulture, $"the trip took over {_options.AbortSeconds:0.##} s from the arrival in {_ground.Name} (--death-recover-abort-seconds)"))
            : null;

    private string HealthAbort => string.Create(CultureInfo.InvariantCulture, $"health-under-{_options.AbortHealthPercent:0.##}");

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
        if (TripOver(now) is { } over)
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
    /// </summary>
    private bool HostileNearPath(IGameSessionView session, out uint hostile, out double reach)
    {
        (hostile, reach) = (0, double.MaxValue);
        if (_options.TripAggroMetres <= 0 || session.State.Self.Position is not { } at)
        {
            return false;
        }

        var self = at.ToGlobal();
        GlobalPosition? corpse = _facts.CorpsePosition is { } c ? c.ToGlobal() : null;
        var wcids = _ground.TargetWcids();
        foreach (var o in session.State.Objects.All)
        {
            if (o.Position is not { } p || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || !Listed(wcids, o.Weenie.WeenieClassId))
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
            var near = HostileNearPath(session, out var hostile, out _);
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
            _log.Info(LogCategory, "handler: trip-fight: a kill; the path is looked at again");
            return ScenarioStatus.Running;
        }

        if (!HostileNearPath(session, out _, out _))
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

        public bool BeginLegTo(IGameSessionView session, double now, in CellFrame goal, out string description) => inner.BeginLegTo(session, now, in goal, out description); // M5 7 P7b (D139 j)

        public void Dispose() => inner.Abandon();
    }

    private ScenarioStatus EndTripFight(IGameSessionView session, double now)
    {
        _tripFighting = false;
        var held = now - _tripFightStart;
        CurrentStep = _resumeStep;
        _stepStart = _resumeStart + held;
        if (!double.IsNaN(_groundAt))
        {
            _groundAt += held; // the fight's time does not count against --death-recover-abort-seconds: the trip's time is its walking and its looting
        }

        _leg?.Resume(now);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"handler: trip-fight: no creature within {_options.TripAggroMetres:0.#} m of the path after {held:0.0} s; the trip goes on in {CurrentStep} (health {Share(session, 1):0}%)"));
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
        return status == TravelStatus.Failed
            ? NoCorpse(now, DeathCorpse.No, "travel-failed", "the corpse leg failed: " + failure)
            : Enter("corpse-seek", now);
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
        _looter = new CorpseLooter(_log, LogCategory, _facts.CorpseGuid, new LayoutTakeAll(new PackRules(_ground), SaleMemory.Shared.KeptAside.Contains, PackMemory.Shared.Rotation.Contains)); // M5 7 D121: each item back into the pack of its job
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
        Corpse = looter.Looted <= 0 ? DeathCorpse.No
            : looter.Looted >= CorpseEntries && looter is { Refused: 0, TimedOut: 0, Skipped: 0 } ? DeathCorpse.Yes
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
                _pending = session.Actions.Unwield(other.Guid, ActionSource.Automation);
                _stepStart = now;
                return ScenarioStatus.Running;
            }

            _log.Info(LogCategory, (item.Kind == GearKind.Wand ? "handler: wield " : "handler: wear the ") + item.Name + " again from the pack");
            _pending = session.Actions.Wield(carried, (EquipMask)WieldSlotChoice.Choose(inventory, (uint)item.ValidLocations), ActionSource.Automation);
            _stepStart = now;
            return ScenarioStatus.Running;
        }

        // M5 7 S2d (closes TODO(m5-self-equip-death-rewear)): the take-back put every piece into a pack and the loop above wore the kit's lines only; the worn set of before the death (the shirt, the breeches, the cantrip pieces),
        // the clothing whose coverage is free and the best caster held go back on now.
        _restorer ??= new WornRestorer(_ground, session.State.Self.Qualities.Level, _spells, _log, LogCategory, "the death's take-back");
        if (_restorer.Step(session))
        {
            return ScenarioStatus.Running;
        }

        WornRestored = _restorer.Restored;
        int worn = 0, wanted = 0;
        var wand = true;
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

    private ScenarioStatus BeginFight(double now)
    {
        if (!_options.Fight)
        {
            _log.Info(LogCategory, "handler: --death-fight is off: the handler ends in the ground");
            return Done(now);
        }

        if (_walk is null)
        {
            _log.Warn(LogCategory, "handler: no walk for the composed fight in this host: the handler ends in the ground without fighting (TODO(m4-death-walkless-wait))");
            return Done(now);
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

    /// <summary>No caster could be wielded or bought: the handler failed by name.</summary>
    public const string Failed = "failed";
}

/// <summary>The handler's <c>route=</c> values (D51: a plan value).</summary>
public static class DeathRoute
{
    /// <summary>Primary Portal Recall: the mage holds the ground's tie.</summary>
    public const string Recall = "recall";

    /// <summary>The runner from the lifestone to the ground's portal and through it.</summary>
    public const string Runner = "runner";
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
