using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>The autopilot's states (M5 item 4 a1, D57; the spec's DoD 8 and section 5.5).</summary>
public enum AutopilotState
{
    /// <summary>The world decides where the mage is (<see cref="AutopilotPlace"/>): at the start and after each return to town.</summary>
    KnownState,

    /// <summary>The journey's buff station.</summary>
    Buff,

    /// <summary>The journey's travel stations: the den travel to the ground, or the recall home from elsewhere.</summary>
    Travel,

    /// <summary>The journey's den-fight station, open-ended: only the autopilot's external stop ends it.</summary>
    Fight,

    /// <summary>Between fight segments (slice a3): the buff station for a buff line under its margin, or the vitals keeper's heal in place for the health under the floor; then the fight again.</summary>
    Upkeep,

    /// <summary>The journey's logistics station: by its counters, or forced (D52).</summary>
    Trip,

    /// <summary>The journey's death station with the journey context (D51, D53).</summary>
    Death,

    /// <summary>Stopped: the done line is written.</summary>
    Stopped,
}

/// <summary>Where the known state found the mage.</summary>
public enum AutopilotPlace
{
    /// <summary>Not classified yet.</summary>
    Unknown,

    /// <summary>The home town: the ground's home (its profile's <c>home</c>) or the journey's Holtburg.</summary>
    Home,

    /// <summary>Inside the ground (the profile's landblocks).</summary>
    Ground,

    /// <summary>Neither.</summary>
    Elsewhere,

    /// <summary>Dead (a death the autopilot saw, or no health).</summary>
    Dead,
}

/// <summary>Where a rebuilt autopilot continues counting (a host that rebuilds the scenario after the person's hand-back, or the checkpoint's resume): the cycle in progress and the cycles ended.</summary>
/// <param name="Cycle">The cycle in progress (from 1).</param>
/// <param name="Cycles">The cycles ended by a return to town.</param>
/// <param name="SecondsUsed">The seconds of the session clock the run has used over all its starts (prior plus elapsed: <see cref="AutopilotScenario.SecondsUsed"/>), so the minutes budget survives a hand-back (M5 4 F1, 4-R5).</param>
/// <param name="PendingDeath">The death tally of a death station that was cut and whose corpse is not recovered (<see cref="AutopilotScenario.Seed"/>); the rebuilt autopilot re-enters the death station with it (4-R2).</param>
/// <param name="PendingTrip">The trip that was cut (<see cref="JourneyPlans.TripForced"/> or <see cref="JourneyStations.TripByCounters"/>), else null; the rebuilt autopilot runs the logistics station again (4-R2).</param>
public readonly record struct AutopilotSeed(int Cycle, int Cycles, double SecondsUsed = 0, DeathTally? PendingDeath = null, string? PendingTrip = null);

/// <summary>One arrow taken: the seam the checkpoint and the watchdogs (slice a2) plug into.</summary>
/// <param name="From">The state left.</param>
/// <param name="To">The state entered.</param>
/// <param name="Why">The arrow's reason, as logged.</param>
/// <param name="Cycle">The cycle's number.</param>
/// <param name="Now">The session clock.</param>
public readonly record struct AutopilotTransition(AutopilotState From, AutopilotState To, string Why, int Cycle, double Now);

/// <summary>The counts of a cycle or of the run (the cycle and done lines' fields, spec section 5.5). A field a later slice fills reads 0 until then, never omitted.</summary>
public sealed class AutopilotTally
{
    /// <summary>The line's field names, in order (between <c>state=</c> or <c>stop=</c> and <c>s=</c>).</summary>
    public static IReadOnlyList<string> FieldNames { get; } =
        ["kills", "xp", "deaths", "trips", "sold", "bought", "spent_xp", "mana_wait_max_s", "fizzles", "mana_tries", "mana_landed", "storeys_visited", "basement_entries", "granted", "places", "interrupted", "resumed", "reconnects"];

    /// <summary>The kills (the fights' <c>kills=</c>).</summary>
    public int Kills { get; set; }

    /// <summary>The experience the fights earned (<c>xp_gained=</c>).</summary>
    public long Xp { get; set; }

    /// <summary>The deaths.</summary>
    public int Deaths { get; set; }

    /// <summary>The logistics trips made.</summary>
    public int Trips { get; set; }

    /// <summary>The items sold on the trips.</summary>
    public int Sold { get; set; }

    /// <summary>The items bought on the trips.</summary>
    public int Bought { get; set; }

    /// <summary>The experience spent on the trips.</summary>
    public long SpentXp { get; set; }

    /// <summary>The longest mana wait of the fights, in seconds.</summary>
    public double ManaWaitMaxSeconds { get; set; }

    /// <summary>The fights' fizzles (the fight line's <c>fizzles=</c>, the cast policy's tally: <c>m5-autopilot-fizzles</c>, closed by slice a3).</summary>
    public int Fizzles { get; set; }

    /// <summary>The fights' mana tries.</summary>
    public int ManaTries { get; set; }

    /// <summary>The fights' landed mana casts.</summary>
    public int ManaLanded { get; set; }

    /// <summary>The most storeys one fight visited.</summary>
    public int StoreysVisited { get; set; }

    /// <summary>The fights' basement entries.</summary>
    public int BasementEntries { get; set; }

    /// <summary>The grants the server mod's verbs made (the session's verb counter over the cycle).</summary>
    public int Granted { get; set; }

    /// <summary>The places the server mod's verbs made.</summary>
    public int Places { get; set; }

    /// <summary>The person's interruptions (slice h2).</summary>
    public int Interrupted { get; set; }

    /// <summary>The hand-backs (slice h2).</summary>
    public int Resumed { get; set; }

    /// <summary>The reconnects (slice a2).</summary>
    public int Reconnects { get; set; }

    /// <summary>The seconds.</summary>
    public double Seconds { get; set; }

    /// <summary>Adds <paramref name="other"/> (the maxima as maxima).</summary>
    /// <param name="other">A cycle's tally.</param>
    public void Add(AutopilotTally other)
    {
        ArgumentNullException.ThrowIfNull(other);
        (Kills, Xp, Deaths, Trips, Sold, Bought, SpentXp) = (Kills + other.Kills, Xp + other.Xp, Deaths + other.Deaths, Trips + other.Trips, Sold + other.Sold, Bought + other.Bought, SpentXp + other.SpentXp);
        (ManaWaitMaxSeconds, Fizzles, ManaTries, ManaLanded, StoreysVisited, BasementEntries) = (Math.Max(ManaWaitMaxSeconds, other.ManaWaitMaxSeconds), Fizzles + other.Fizzles, ManaTries + other.ManaTries, ManaLanded + other.ManaLanded, Math.Max(StoreysVisited, other.StoreysVisited), BasementEntries + other.BasementEntries);
        (Granted, Places, Interrupted, Resumed, Reconnects, Seconds) = (Granted + other.Granted, Places + other.Places, Interrupted + other.Interrupted, Resumed + other.Resumed, Reconnects + other.Reconnects, Seconds + other.Seconds);
    }

    /// <summary>The fields, <see cref="FieldNames"/> in order, space-separated.</summary>
    public string Fields() => string.Create(
        CultureInfo.InvariantCulture,
        $"kills={Kills} xp={Xp} deaths={Deaths} trips={Trips} sold={Sold} bought={Bought} spent_xp={SpentXp} mana_wait_max_s={ManaWaitMaxSeconds:0.#} fizzles={Fizzles} mana_tries={ManaTries} mana_landed={ManaLanded} storeys_visited={StoreysVisited} basement_entries={BasementEntries} granted={Granted} places={Places} interrupted={Interrupted} resumed={Resumed} reconnects={Reconnects}");
}

/// <summary>
/// The autopilot (M5 item 4 a1, D57; the spec's DoD 8): one state machine over the journey's own stations. It makes every run with the journey's <see cref="IStationFactory"/> and one <see cref="JourneyContext"/> (the travel seam, the keeper,
/// the facts, the ground), so the buff, the den travel, the fight, the logistics trip and the death are the stations the gate already proves; the autopilot adds the arrows between them, the fight's external stop
/// (<see cref="IStoppableFight.RequestStop"/>), the stop rules and the lines.
/// <list type="bullet">
/// <item><b>known state</b>: the world decides (<see cref="AutopilotPlace"/>): home: buff; the ground: the fight (the buff first when no enchantment is up); elsewhere: the recall home once, then the known state again; dead: the death.</item>
/// <item><b>buff</b> (the buff station, anywhere: the plan's Holtburg need is overridden by Here), then <b>travel</b> (the den-travel station) unless already in the ground, then <b>fight</b>.</item>
/// <item><b>fight</b>: the den-fight station open-ended (<see cref="JourneyStations.OpenEndedArgument"/>: no kill cap, no fight timer); the autopilot asks it to stop for a stop rule, the scripted death, the forced trip or the counters'
/// trip or stop (<see cref="LogisticsCounters.Check"/>), a buff line under its margin (<see cref="BuffMargin"/>) or the health under the floor (<see cref="HealthFloor"/>), and routes by the reason: a trip, the death, a stop, else the <b>upkeep</b>:
/// the buff station for the margin, the vitals keeper's heal in place for the floor, then the fight again.</item>
/// <item><b>trip</b>: the logistics station by its counters (<see cref="JourneyStations.TripByCounters"/>) or forced (<see cref="JourneyPlans.TripForced"/>, after <see cref="AutopilotOptions.TripAfterKills"/>); its recall back ends in the ground.</item>
/// <item><b>death</b>: the death station with the journey context (<see cref="DeathOptions.Journey"/>, so the rearm can shop: D53) and without the handler's own fight (<see cref="JourneyStations.DeathNoFightArgument"/>), then the known state. A death the
/// autopilot did not script is the same station entered after the respawn: the autopilot keeps a death tally running from its start and offers it (<see cref="JourneyFacts.ObservedDeath"/>), so the corpse is known (<c>m5-autopilot-death-unscripted</c>).</item>
/// </list>
/// A cycle ends at each return to town, by a trip or a death: one <c>autopilot cycle</c> line each; a stop writes the cycle in progress (<c>state=stopped</c>, not counted in <c>cycles=</c>) and one <c>autopilot done</c> line with the totals.
/// The stop rules are checked at every station boundary and inside the fight; a trip or a death in progress finishes first. The autopilot runs in the world and never logs in or off (its hosts do: slice h1). The constants are Lifestone's own.
/// </summary>
public sealed partial class AutopilotScenario : IScenario, IDisposable
{
    /// <summary>The scenario's token.</summary>
    public const string ScenarioName = "autopilot";

    /// <summary>The log category of the autopilot's lines.</summary>
    public const string LogCategory = "autopilot";

    /// <summary>Seconds the known state waits for the player to be present (out of portal space) before it stops by name.</summary>
    public const double PresentSeconds = 60;

    /// <summary>
    /// Lifestone's own bound (M5 7 F18) on the wait at the end of the recall home before a stop: after the recall station reports success the mage must show Present at home within this many seconds, else the attempt counts as failed.
    /// </summary>
    public const double FinalSettleSeconds = 20;

    /// <summary>The recall home before a stop is tried this many times in all (the first and one more; M5 7 F18) before the stop goes on, named.</summary>
    public const int FinalRecallAttempts = 2;

    /// <summary>
    /// M5 7 G2 (edge audit 12g): after the two tries fail with the mage still on the ground the stop does not log off in the den: it waits this many seconds and recalls again (Lifestone's own; a character logged off in the ground stays in the
    /// world and can be killed).
    /// </summary>
    public const double FinalRetrySeconds = 30;

    /// <summary>M5 7 G2: the retries of the final recall go on for this many seconds from the second failure (5 minutes), then the stop goes on named, with the mage not home.</summary>
    public const double FinalRetryLimitSeconds = 300;

    /// <summary>Seconds between two reads of the logistics counters during the fight.</summary>
    public const double CounterCheckSeconds = 1;

    /// <summary>The scripted death comes after this many kills of the first fight that makes them.</summary>
    public const int TestDeathAfterKills = 1;

    /// <summary>Seconds one reconnect try's login may take on the session clock before the try counts as failed (Lifestone's own: the login scenario's arrival limit is 90 s, the enter-world cap 30 s).</summary>
    public const double ReconnectLoginSeconds = 150;

    /// <summary>Seconds between two refreshes of the checkpoint inside a state (Lifestone's own): a kill of the process mid-fight loses at most this much of the stop rule's budget and the fight's kills since.</summary>
    public const double CheckpointRefreshSeconds = 60;

    /// <summary>Seconds between two reads of the buff lines' time left and of the health during the fight (Lifestone's own; the rebuff driver's check is <see cref="BuffOptions.AutoCheckSeconds"/>).</summary>
    public const double UpkeepCheckSeconds = 5;

    /// <summary>D126 (a): the seed of the trip's expected length in seconds, from the fight's end to the next fight's start: soak round 12's trips took 97, 121 and 119 s (08:10, 09:26, 10:27 UTC).</summary>
    public const double TripSeedSeconds = 120;

    /// <summary>D126 (a): the weight of the newest trip in the trip's mean length (as the fight's mean, <see cref="Scenarios.FightScenario.FightMeanWeight"/>).</summary>
    public const double TripMeanWeight = 0.3;

    /// <summary>The health share under which the fight is stopped for the keeper's heal (Lifestone's own: the fight has no heal of its own; the heal goes on to <see cref="JourneyPlans.FightHealthPercent"/>, the share the fight station starts at).</summary>
    public const double HealthFloorPercent = 50;

    /// <summary>M5 7 F15 (7-F2): the highest health share the upkeep stops the fight at while the fight's drain rule is on (Lifestone's own; see <see cref="AutopilotOptions.HealthFloorPercent"/>): under the drain line (50) so D88's drains run.</summary>
    public const double HealthFloorWithDrainPercent = 30;

    /// <summary>M5 7 F23: the upkeep's no-cast watchdog: an upkeep station that makes no cast for this many seconds while a hostile is in reach is ended by name (Lifestone's own; soak round 2 stood 50 s).</summary>
    public const double UpkeepNoCastSeconds = 20;

    /// <summary>M5 7 F23: the name of the upkeep ended by its no-cast watchdog (the cycle line's <c>why=</c>).</summary>
    public const string UpkeepNoCastName = "upkeep-no-cast";

    /// <summary>The stop name when the last reconnect try failed.</summary>
    public const string ReconnectFailedStop = "reconnect-failed";

    /// <summary>The stop name of a session the person ended (a log off or a close: not a drop to recover from).</summary>
    public const string SessionClosedStop = "session-closed";

    /// <summary>The stop name of a dropped session when the host gave the autopilot no <see cref="IAutopilotSession"/>.</summary>
    public const string NoReconnectStop = "failed:session-dropped";

    /// <summary>The stop name of a death the autopilot did not script (see <see cref="AutopilotScenario"/>'s death arrow).</summary>
    public const string UnscriptedDeathStop = "death-unscripted";

    /// <summary>The fight stop reason of a buff line of the character's profile under its margin (<see cref="BuffOptions.RebuffMarginSeconds"/>, read as <see cref="RebuffDriver"/> reads it: <see cref="BuffCycle.RebuffDue"/>).</summary>
    public static FightStopReason BuffMargin { get; } = new("buff-margin");

    /// <summary>The fight stop reason of the health under <see cref="HealthFloorPercent"/>.</summary>
    public static FightStopReason HealthFloor { get; } = new("health-floor");

    private static readonly IReadOnlyDictionary<string, string> OpenEnded = new Dictionary<string, string>(StringComparer.Ordinal) { [JourneyStations.OpenEndedArgument] = "yes", [JourneyStations.UnattendedArgument] = "yes" }; // M5 7 F25: unattended, the fight holds the magic mode at its end
    private static readonly IReadOnlyDictionary<string, string> NoFight = new Dictionary<string, string>(StringComparer.Ordinal) { [JourneyStations.DeathNoFightArgument] = "yes", [JourneyStations.UnattendedArgument] = "yes" }; // M5 7 F25: the corpse trip's fights hold it too
    private static readonly IReadOnlyDictionary<string, string> Unattended = new Dictionary<string, string>(StringComparer.Ordinal) { [JourneyStations.UnattendedArgument] = "yes" }; // M5 7 F23: the driver's fight guard never holds the autopilot's own buff station
    private static readonly IReadOnlyDictionary<string, string> StartPass = new Dictionary<string, string>(StringComparer.Ordinal) { [JourneyStations.UnattendedArgument] = "yes", [JourneyStations.StartPassArgument] = "yes" }; // D126 (a): the start's pass recasts every line due within one buff cycle

    private readonly AutopilotOptions _options;
    private readonly IStationFactory _factory;
    private readonly JourneyContext _context;
    private readonly ILogSink _log;
    private readonly HuntingGround _ground;
    private readonly LogisticsCounters _counters;

    /// <summary>M5 7 P7s (D148): the run's logistics counters (they know the floor-only carried lines); the host's next-trigger line reads them.</summary>
    public LogisticsCounters Counters => _counters;

    private readonly IReadOnlyDictionary<uint, IReadOnlyList<string>> _floorOnly; // M5 7 P7s (D148): the carried lines whose floor drops spells, with the spells
    private ulong _floorAnnounced; // M5 7 P7s (D148): the floor-only lines at their floor that were last said in chat
    private readonly List<string> _lines = [];
    private readonly AutopilotTally _total = new();
    private readonly IAutopilotCheckpointStore? _checkpoints;
    private readonly IAutopilotSession? _host;
    private readonly AutopilotWatchdogs _watchdogs;

    private readonly List<string> _whyNames = [];
    private readonly List<int> _whyCounts = [];
    private DeathTally? _deathTally;
    private BuffCycle? _buffCycle;
    private BuffOptions? _buffOptions;
    private double _lastUpkeep = double.NegativeInfinity;
    private double _tripMean = double.NaN;
    private double _tripStartedAt = double.NaN;
    private HostileWatch? _hostiles;
    private AutopilotVitalsGuard? _guard;
    private bool _guardDownLogged; // M5 7 P7o: the guard's stand-down for the scripted death was logged (once per death station)
    private double _upkeepLastCastAt;
    private long _actionsSeen;
    private bool _upkeepDeferLogged;
    private string? _upkeepDetail;
    private bool _healOnly;
    private bool _unscripted;
    private bool _seedLogged;
    private bool _deathPending;
    private bool _tripPending;
    private readonly TripLoopGuard _tripLoop = new(); // M5 7 G1 (12a)
    private bool _tripForced;
    private bool _tripFailedOnce;
    private bool _tripCutWork;
    private bool _abandoned;
    private uint _characterGuid;

    private AutopilotTally _tally = new();
    private AutopilotState _state = AutopilotState.KnownState;
    private RunPhase _phase = RunPhase.Start;
    private double _clock;
    private double _startClock = double.NaN;
    private double _phaseStart;
    private double _cycleStart;
    private ModVerbCounts _cycleVerbs;
    private IGameSessionView? _session;
    private RecoveryFixRun? _fix; // M5 7 D141: the recovery table's fix in progress
    private TypedRetry? _retry; // M5 7 D141: the typed failure whose one retry is under way (cleared when a station passes, or by the safe stop)
    private IDisposable? _diedSubscription;
    private IDisposable? _chatSubscription; // M5 7 S2b: the worn items' low and out lines for the mana watch
    private readonly WornManaWatch _manaWatch;
    private StationSpec? _spec;
    private IStation? _station;
    private StationNeeds _needs = StationNeeds.None;
    private IStationRun? _run;
    private ActionHandle? _pending;
    private FightStopReason? _fightStop;
    private string? _stopAsked;
    private string? _countersStop;
    private double _lastCounters = double.NegativeInfinity;
    private bool _testDeathDone;
    private bool _forcedTripDone;
    private int _buffStations; // M5 7 F29 (D96): upkeeps (the buff station run between fight segments) that ended well: each is a buff cycle
    private int _fightLinePasses; // the fights' buff line passes that cast, over the run
    private int _fightPassSeen; // the running fight's count already folded in
    private int _fightLineCount = 1; // the lines a full cycle recasts
    private bool _tripCadence; // the trip asked is the schedule's: forced, as the station's own counters do not count the buff cycles
    private bool _recalledHome;
    private FinalStop? _finalStop;
    private int _finalAttempts;
    private double _finalRetryFrom = double.NaN;
    private bool _finalDone;
    private bool _boundOnce;
    private StartupAudit? _audit; // M5 7: the start-up state audit due (at the start and after each reconnect), else null
    private PackSorter? _packSorter; // M5 7 D121: the audit's sort of the packs while it runs
    private WornRestorer? _wornRestorer; // M5 7 S2d: the audit's restore of the worn set while it runs
    private double _wornSnapshotAt = double.NegativeInfinity;

    /// <summary>M5 7 S2d: seconds between two snapshots of the worn set while the mage lives.</summary>
    public const double WornSnapshotSeconds = 5;
    private string? _bindHow; // M5 7 audit: what proved the lifestone bind this session, else null
    private bool _portalProven; // M5 7 audit: a den travel arrived this session
    private bool _deathSeen;
    private string? _deathText;
    private bool _noFightLogged;
    private int _cycle = 1;
    private int _cycles;
    private ScenarioStatus _end = ScenarioStatus.Running;
    private double _priorSeconds;
    private bool _checkpointReady;
    private bool _exitWritten;
    private int _lastKills;
    private string _lastDecision = "start";
    private double _lastDecisionAt;
    private double _lastWrite = double.NegativeInfinity;
    private int _reconnectTry;
    private bool _reconnectLoggedIn;
    private double _reconnectAt;
    private double _reconnectLoginStart;
    private AutopilotState _reconnectFrom;

    private enum RunPhase
    {
        Start,
        Known,
        Vitals,
        Peace,
        Running,
        Done,
        Reconnecting,
        FinalSettle,
        FinalRetryWait,

        /// <summary>M5 7 D141: a fix of the recovery table runs (<see cref="RecoveryFixRun"/>); its retry follows.</summary>
        Fixing,
    }

    /// <summary>M5 7 D141: a typed failure under recovery: its reason and fix, the failure's stop name and sentence.</summary>
    private sealed record TypedRetry(ActionReason Reason, RecoveryFix Fix, string Label, string Stop, string Detail);

    /// <summary>The stop held while the recall home before it runs (M5 7 F18): the stop's name, its ok and its sentence.</summary>
    private sealed record FinalStop(string Name, bool Ok, string Why);

    /// <summary>An autopilot over the journey's stations.</summary>
    /// <param name="options">The stop rules, the forced trip and the scripted death.</param>
    /// <param name="factory">The journey's station factory (<see cref="JourneyStations.Create(JourneyPlan, IReadOnlyList{string}, Lifestone.Data.Dat.DatSet?, string?)"/>; the tests script one).</param>
    /// <param name="context">The journey's one context (its <see cref="JourneyContext.Dungeon"/> is the ground).</param>
    /// <param name="checkpoints">Where the checkpoint is written on every state change and on exit and read at the start (slice a2); null: none is kept.</param>
    /// <param name="host">The host's control of the session for the reconnect (slice a2); null: a dropped session stops the run by name (<see cref="NoReconnectStop"/>).</param>
    /// <param name="seed">Where the cycle number and the cycles count continue from (a scenario rebuilt after the person's hand-back); null: cycle 1, no cycles. The checkpoint's resume goes through the same path (the larger of the two counts stands).</param>
    public AutopilotScenario(AutopilotOptions options, IStationFactory factory, JourneyContext context, IAutopilotCheckpointStore? checkpoints = null, IAutopilotSession? host = null, AutopilotSeed? seed = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        if (options.Errors.Count > 0)
        {
            throw new ArgumentException("autopilot: the --autopilot-* options do not parse: " + string.Join("; ", options.Errors), nameof(options));
        }

        _log = context.Log;
        _ground = context.Dungeon;
        _manaWatch = new WornManaWatch(_log, LogCategory);
        _floorOnly = ComponentFloor.FloorOnly(_ground, context.Spells); // M5 7 P7s (D148): a carried line only the war options need drops them at its floor, not the run
        _counters = new LogisticsCounters(_ground, _floorOnly.Keys);
        _checkpoints = checkpoints;
        _host = host;
        _watchdogs = new AutopilotWatchdogs(options.WatchdogScale, options.WatchdogRecoveries, AutopilotWatchdogs.FightBaseSeconds(_ground));
        if (seed is { } given)
        {
            ApplySeed(given);
        }
    }

    /// <summary>Continues the cycle number and the cycles count from <paramref name="seed"/> (never backwards: the larger count stands).</summary>
    private void ApplySeed(AutopilotSeed seed)
    {
        _cycle = Math.Max(_cycle, Math.Max(1, seed.Cycle));
        _cycles = Math.Max(_cycles, Math.Max(0, seed.Cycles));
        if (double.IsFinite(seed.SecondsUsed))
        {
            _priorSeconds = Math.Max(_priorSeconds, Math.Max(0, seed.SecondsUsed)); // the budget carries over a hand-back (4-R5)
        }

        if (seed.PendingDeath is { } death)
        {
            _deathTally = death;
            _deathPending = true;
        }

        if (seed.PendingTrip is { } trip)
        {
            _tripPending = true;
            _tripForced = trip == JourneyPlans.TripForced;
        }
    }

    /// <summary>
    /// The buff station's spec: the journey's buff with its Holtburg need overridden by Here (the mage buffs where it stands: at home, or in the ground after a cold start), its mana need kept. D126 (a): a start's (or restart's) pass
    /// recasts every line due within one buff cycle (<see cref="JourneyStations.StartPassArgument"/>).
    /// </summary>
    public static StationSpec BuffSpec { get; } = new("buff", StationKinds.Buff, new StationNeeds(MinManaPercent: JourneyStations.MageBuffManaPercent), 200, StartPass);

    /// <summary>D126 (a): the upkeep's buff station (a buff margin outside a refreshing fight): <see cref="BuffSpec"/> with the re-buff margin, not the start pass's.</summary>
    public static StationSpec UpkeepSpec { get; } = new("buff", StationKinds.Buff, new StationNeeds(MinManaPercent: JourneyStations.MageBuffManaPercent), 200, Unattended);

    /// <summary>The den travel's spec (Primary Portal Recall to the ground's tie, or the walk to its portal).</summary>
    public static StationSpec DenTravelSpec { get; } = new("den-travel", StationKinds.DenTravel, null, 60);

    /// <summary>The recall home's spec (from elsewhere, once).</summary>
    public static StationSpec RecallHomeSpec { get; } = new("recall-home", StationKinds.RecallHome, null, 30);

    /// <summary>
    /// The bind (M5 7 F14): the journey's bind station (<see cref="BindStation"/>: on foot to the ground profile's home lifestone, Use it, then Lifestone Tie 2644 at it) run once, from wherever the mage stands, when a recall home is refused with
    /// 0x049E. It needs mana for the tie (the station goes to peace for the Use and to the magic mode for the tie itself: a Peace need would hold the autopilot in its peace phase before the station and bind nothing in the scripted world); it needs no town (the station walks to the profile's home lifestone, a point goal first when the stone is not in view).
    /// </summary>
    public static StationSpec BindSpec { get; } = new("bind", StationKinds.Bind, new StationNeeds(MinManaPercent: 20), 180);

    /// <summary>The refusal ACE sends when Lifestone Recall is cast with no tied lifestone (<c>YouMustLinkToLifestoneToRecall</c>, @95ef426d <c>WeenieError.cs</c> 658); the recall-home station's failure text carries it.</summary>
    public const string NoLifestoneRefusal = "0x049E";

    /// <summary>The stop name of a recall home refused with 0x049E after the autopilot had already bound once (a second refusal).</summary>
    public const string NoBindStop = "failed:travel:no-lifestone-bind";

    /// <summary>The open-ended fight's spec.</summary>
    public static StationSpec FightSpec { get; } = new("den-fight", StationKinds.DenFight, null, 0, OpenEnded);

    private static readonly IReadOnlyDictionary<string, string> OpenEndedResume = new Dictionary<string, string>(StringComparer.Ordinal) { [JourneyStations.OpenEndedArgument] = "yes", [JourneyStations.UnattendedArgument] = "yes", [JourneyStations.ResumeOnGroundArgument] = "yes" };

    /// <summary>M5 7 F30: the open-ended fight's spec when the mage already stands on the ground: it fights from where it stands (no teleport to the arrival, no walk to it); <see cref="FightSpec"/> otherwise.</summary>
    public static StationSpec FightResumeSpec { get; } = new("den-fight", StationKinds.DenFight, null, 0, OpenEndedResume);

    /// <summary>The death's spec: the journey's death station without the handler's own fight.</summary>
    public static StationSpec DeathSpec { get; } = new("death", StationKinds.Death, null, 280, NoFight);

    /// <summary>The death station for a death the autopilot did not script: no place to be in (the mage stands at the lifestone after its respawn), no vitals pass before it; the station enters at the respawn (<see cref="JourneyFacts.ObservedDeath"/>).</summary>
    public static StationSpec UnscriptedDeathSpec { get; } = new("death", StationKinds.Death, StationNeeds.None, 280, NoFight);

    /// <summary>The heal in place of the upkeep (the vitals keeper's pass; no station is made for it).</summary>
    public static StationSpec HealSpec { get; } = new("health-floor", StationKinds.HarmHeal, null, 120);

    /// <summary>The trip's spec.</summary>
    /// <param name="forced">True: forced (D52); false: by the counters.</param>
    public static StationSpec TripSpec(bool forced) => new("logistics", StationKinds.Logistics, null, 600, new Dictionary<string, string>(StringComparer.Ordinal) { [JourneyPlans.TripArgument] = forced ? JourneyPlans.TripForced : JourneyStations.TripByCounters });

    /// <inheritdoc />
    public string Name => ScenarioName;

    /// <inheritdoc />
    public string Stage { get; private set; } = "known-state";

    /// <inheritdoc />
    public string? Failure { get; private set; }

    /// <inheritdoc />
    public double? WhereMaxMetres { get; private set; }

    /// <inheritdoc />
    public long Unmatched { get; private set; }

    /// <summary>The state now.</summary>
    public AutopilotState State => _state;

    /// <summary>The cycle in progress (from 1).</summary>
    public int Cycle => _cycle;

    /// <summary>M5 7 F29 (D96): the buff cycles so far: the upkeeps that ended well plus the fights' line passes in full sets of the profile's lines.</summary>
    public int BuffCycles => _buffStations + (_fightLinePasses / Math.Max(1, _fightLineCount));

    /// <summary>The cycles ended by a return to town (a trip or a death).</summary>
    public int Cycles => _cycles;

    /// <summary>M5 7: the start-up state audits finished (one at the start, one after each reconnect).</summary>
    public int Audits { get; private set; }

    /// <summary>M5 7: the last audit's summary line (<c>autopilot audit: place=... bind=... portal_tie=... kit=... corpse=... buffs=...</c>), or null before one finished.</summary>
    public string? LastAudit { get; private set; }

    /// <summary>The totals of the ended cycles (the done line adds the cycle in progress).</summary>
    public AutopilotTally Totals => _total;

    /// <summary>The cycle in progress.</summary>
    public AutopilotTally CycleTally => _tally;

    /// <summary>The stop's name once stopped (<c>minutes</c>, <c>level</c>, <c>request:&lt;why&gt;</c>, <c>logistics-stop:&lt;line&gt;</c>, <c>failed:&lt;state&gt;:&lt;why&gt;</c>, <see cref="UnscriptedDeathStop"/>), else null.</summary>
    public string? StopName { get; private set; }

    /// <summary>True once stopped by a stop rule or a request (the done line's <c>ok=yes</c>).</summary>
    public bool Ok { get; private set; }

    /// <summary>The station run in progress, or null.</summary>
    public IStationRun? CurrentRun => _run;

    /// <summary>The seconds of the session clock the run has used over all its starts: the seconds resumed (the checkpoint's or the seed's) plus this start's (M5 4 F1, 4-R5). The stop rule's minutes and the checkpoint's <c>minutesUsed</c> read it.</summary>
    public double SecondsUsed => _priorSeconds + (double.IsNaN(_startClock) ? 0 : Math.Max(0, _clock - _startClock));

    /// <summary>
    /// Where a rebuilt autopilot continues (M5 4 F1): the cycle, the cycles ended, <see cref="SecondsUsed"/>, and the death or the trip that was cut and is still to be done (4-R2). A host that abandons this scenario
    /// (<see cref="Abandon"/>) and builds the next one passes this seed; its <see cref="AutopilotSeed.PendingDeath"/> is then the next scenario's to own.
    /// </summary>
    public AutopilotSeed Seed => new(_cycle, _cycles, SecondsUsed, _deathPending ? _deathTally : null, _tripPending ? (_tripForced && !_tripCutWork ? JourneyPlans.TripForced : JourneyStations.TripByCounters) : null);

    /// <summary>The lines written (each cycle line and the done line), bare (a host prefixes them).</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>The arrows' seam (slice a2: the checkpoint written on every state change, the per-state watchdogs): called after each state change.</summary>
    public Action<AutopilotTransition>? StateChanged { get; set; }

    /// <summary>
    /// A stop request (the key, the hosts): the autopilot stops at its next safe point (a fight is asked to stop and ends cleanly; a trip or a death handler in progress finishes first), <c>stop=request:&lt;why&gt;</c>, <c>ok=yes</c>.
    /// The first request is kept.
    /// </summary>
    /// <param name="why">Who or what asked (a token: <c>key</c>, <c>host</c>).</param>
    public void RequestStop(string why)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(why);
        if (_stopAsked is null)
        {
            _stopAsked = "request:" + JourneyVerdict.Token(why);
            _log.Info(LogCategory, $"autopilot stop requested ({_stopAsked}): it stops at its next safe point");
        }
    }

    /// <inheritdoc />
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _clock = session.Clock.Now;
        _hostiles ??= new HostileWatch(session, _ground, _options.AggroReachMetres); // M5 7 F23
        _guard ??= new AutopilotVitalsGuard(_context.Spells, _ground, _options, (_options.Buff ?? new BuffOptions()).CastChance, _hostiles, _log);
        if (_end != ScenarioStatus.Running)
        {
            return _end;
        }

        if (_abandoned)
        {
            return ScenarioStatus.Running; // abandoned by its host: nothing runs; the host drops it
        }

        NoteStance(session);

        if (_phase == RunPhase.Start)
        {
            return Begin(session);
        }

        if (_phase == RunPhase.Reconnecting)
        {
            return ReconnectStep(session);
        }

        if (session.State.Phase != SessionPhase.InWorld)
        {
            return Dropped(session);
        }

        if (!_deathSeen && _clock - _wornSnapshotAt >= WornSnapshotSeconds && session.State.Self.Qualities.TryGetVitalLevels(1, out var health, out _) && health > 0 && _audit is null && _wornRestorer is null)
        {
            _wornSnapshotAt = _clock;
            WornMemory.Shared.Snapshot(session.State); // M5 7 S2d: the worn set while alive (the death's take-back and the audit wear it again)
        }

        _manaWatch.Step(session, _clock); // M5 7 S2b: each worn item's mana re-read every few minutes (ACE pushes none as it burns)

        if (!_deathSeen && _phase is RunPhase.Known or RunPhase.Vitals or RunPhase.Peace or RunPhase.Running && _watchdogs.Over(_clock) is { } held)
        {
            return WatchdogTrip(held);
        }

        if (_checkpointReady && _clock - _lastWrite >= CheckpointRefreshSeconds)
        {
            WriteCheckpoint(_lastDecision, _lastDecisionAt, quiet: true);
        }

        return _phase switch
        {
            RunPhase.Known => KnownState(session),
            RunPhase.Vitals => VitalsStep(session, now),
            RunPhase.Peace => PeaceStep(session),
            RunPhase.Running => RunStep(session, now),
            RunPhase.FinalSettle => FinalSettleStep(session),
            RunPhase.FinalRetryWait => FinalRetryWaitStep(),
            RunPhase.Fixing => FixStep(session),
            _ => _end,
        };
    }

    /// <summary>
    /// The host takes the keys (the person's interrupt, a halt) or drops this scenario for another: the fight's fields so far are folded into the cycle's tally, a death station's or a trip's state is kept (<see cref="Seed"/>: the corpse
    /// is still to be recovered and the trip still to be made; a trip's sold and bought so far count), the run is disposed and the checkpoint is written (M5 4 F1, 4-R9 and 4-R2). No done line: the run is not over. Nothing runs after it.
    /// </summary>
    /// <param name="why">Who or what abandoned it (a token: <c>interrupt</c>, <c>halt</c>).</param>
    public void Abandon(string why)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(why);
        if (_abandoned || _end != ScenarioStatus.Running)
        {
            return;
        }

        _abandoned = true;
        _log.Info(LogCategory, $"autopilot abandoned ({JourneyVerdict.Token(why)}) in the {StateName(_state)} state, cycle {_cycle}: the fight's kills are folded in; a death's or a trip's state is kept");
        FoldInFlight();
        _phase = RunPhase.Done;
        if (_checkpointReady && StopName is null)
        {
            _exitWritten = true;
            WriteCheckpoint("abandoned: " + JourneyVerdict.Token(why), _clock);
        }
    }

    /// <summary>Releases the station run in progress and the death subscription.</summary>
    public void Dispose()
    {
        _hostiles?.Dispose(); // M5 7 F23
        if (_checkpointReady && StopName is null && !_exitWritten)
        {
            _exitWritten = true;
            WriteCheckpoint("exit: the autopilot was disposed", _clock); // on exit, so a restart resumes the totals
        }

        DisposeRun();
        _diedSubscription?.Dispose();
        _diedSubscription = null;
        _chatSubscription?.Dispose();
        _chatSubscription = null;
        _deathTally?.Dispose();
    }

    // ------------------------------------------------------------------------------------------------ the start and the known state

    private ScenarioStatus Begin(IGameSessionView session)
    {
        _startClock = _clock;
        _cycleStart = _clock;
        _cycleVerbs = session.Tools.Verbs.Counts;
        if (session.State.Phase != SessionPhase.InWorld || session.State.Self.Guid == 0)
        {
            return Stop("failed:known-state:not-in-world", ok: false, "the autopilot runs in the world; its host logs in first");
        }

        _characterGuid = session.State.Self.Guid;
        LoadCheckpoint();
        _diedSubscription = session.Events.Subscribe<PlayerDied>(OnDied);
        _chatSubscription = session.Events.Subscribe<ChatReceived>(OnChat); // M5 7 S2b
        if (_deathTally is null)
        {
            _deathTally = new DeathTally();
            _deathTally.Start(session); // running from the start: a death the autopilot did not script is handled with the corpse's facts it saw
        } // else a seeded tally of a cut death (Seed): its counters are kept; the death station starts it
        _log.Info(LogCategory, $"autopilot start: {_context.Character} on {_ground.Key} ({_options} {_options.Resilience}){(_options.HasStopRule ? string.Empty : "; no stop rule: it runs until a stop request")}");
        BeginAudit(StartReason);
        return EnterKnown("start");
    }

    /// <summary>M5 7 S2b: the mana watch reads ACE's "Your X is low on Mana." and "Your X is out of Mana." (Player_Tick.cs:662-699).</summary>
    private void OnChat(in ChatReceived e)
    {
        _manaWatch.OnChat(e.Line.Text, _clock);
        NoteHit(e); // M5 7 P7n (D145 point 6): each landed damage spell with the caster held and the Spirit Drinker aura
    }

    /// <summary>M5 7 S2b: the worn items' mana as the watch last read it.</summary>
    public WornManaWatch ManaWatch => _manaWatch;

    private void OnDied(in PlayerDied e)
    {
        if (_state == AutopilotState.Death)
        {
            return; // the death station's own death (the scripted one, or a second death its handler restarts for)
        }

        _deathSeen = true;
        _deathText = e.Text;
    }

    private ScenarioStatus EnterKnown(string why)
    {
        SetState(AutopilotState.KnownState, why);
        _phase = RunPhase.Known;
        _phaseStart = _clock;
        return ScenarioStatus.Running;
    }

    private ScenarioStatus KnownState(IGameSessionView session)
    {
        if (_deathSeen)
        {
            return UnscriptedDeath();
        }

        if (GuardHolds(session))
        {
            return ScenarioStatus.Running; // M5 7 F23: under the emergency line the guard heals first
        }

        var self = session.State.Self;
        if (self.Travel != SelfTravel.Present)
        {
            if (_audit is { TravelLogged: false } waiting)
            {
                waiting.TravelLogged = true; // M5 7 P7n (D145): in portal space: the audit waits for the arrival, logged once
                waiting.Travel = "portal-space";
                _log.Info(LogCategory, StartupAudit.Line("portal_space", string.Create(CultureInfo.InvariantCulture, $"in portal space (travel {self.Travel})"), string.Create(CultureInfo.InvariantCulture, $"wait for the arrival (at most {PresentSeconds:0} s, else the run stops by name)")));
            }

            return _clock - _phaseStart > PresentSeconds ? Stop("failed:known-state:not-present", ok: false, $"the player was not present within {PresentSeconds:0} s (travel {self.Travel})") : ScenarioStatus.Running;
        }

        if (_audit is { } audit && AuditStep(session, audit) is { } decided)
        {
            return decided; // M5 7: the start-up audit's fix (the corpse, the bind, the trip), its stop, or its wait for the pack to read
        }

        if (_deathPending)
        {
            return ResumeDeath(); // a death station that was cut: the corpse is still to be recovered, wherever the mage stands (4-R2)
        }

        var place = Classify(session.State, _ground);
        _log.Info(LogCategory, $"autopilot known-state place={place.ToString().ToLowerInvariant()} ({Cell(session)})");
        var finished = _finished; // M5 7 P7n (D145): the audit that finished this frame decides the ground's arrow and gets its chat summary
        _finished = null;
        switch (place)
        {
            case AutopilotPlace.Home:
                _recalledHome = false;
                if (_tripPending)
                {
                    Announce(finished, "the cut trip runs again from its start");
                    return ResumeTrip();
                }

                Announce(finished, $"buffing, then the way to {_ground.Name}");
                return BeginRun(AutopilotState.Buff, BuffSpec, "home");
            case AutopilotPlace.Ground:
                _recalledHome = false;
                if (finished?.Attacker is { } attacker)
                {
                    Announce(finished, "fighting back from where he stands");
                    return BeginRun(AutopilotState.Fight, FightSpec, $"audit: a hostile is on him ({attacker}): the fight fights back first (D88)");
                }

                if (_tripPending)
                {
                    Announce(finished, "the cut trip runs again from its start");
                    return ResumeTrip();
                }

                if (finished is { Buffs: "buff" } || self.Qualities.Enchantments.IsDefaultOrEmpty)
                {
                    Announce(finished, "buffing, then back to the fight from where he stands");
                    return BeginRun(AutopilotState.Buff, BuffSpec, finished is { BuffsDue: > 0 } due ? string.Create(CultureInfo.InvariantCulture, $"audit: {due.BuffsDue} buff line(s) due in the ground") : "in the ground with no enchantment up");
                }

                if (finished is { HealFirst: true })
                {
                    Announce(finished, "healing, then back to the fight from where he stands");
                    _upkeepDetail = "the audit found the health under the floor";
                    return BeginHeal("audit: the health is under the floor");
                }

                Announce(finished, "back to the fight from where he stands");
                return BeginRun(AutopilotState.Fight, FightSpec, "in the ground");
            case AutopilotPlace.Dead:
                return UnscriptedDeath();
            default:
                if (_recalledHome)
                {
                    return Stop("failed:known-state:elsewhere", ok: false, "the recall home did not bring the mage home or to the ground");
                }

                _recalledHome = true;
                return BeginRun(AutopilotState.Travel, RecallHomeSpec, "elsewhere");
        }
    }

    /// <summary>Where <paramref name="state"/> puts the player: dead (health known and 0), in the ground (its landblocks), home (the ground's home landblock or the journey's Holtburg), else elsewhere.</summary>
    /// <param name="state">The session's state.</param>
    /// <param name="ground">The ground.</param>
    public static AutopilotPlace Classify(GameState state, HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ground);
        var self = state.Self;
        if (self.Qualities.IsKnown && self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var max) && max > 0 && health == 0)
        {
            return AutopilotPlace.Dead;
        }

        if (self.Position is not { } at)
        {
            return AutopilotPlace.Unknown;
        }

        if (JourneyGrounds.At(StationWhere.Den, at, ground))
        {
            return AutopilotPlace.Ground;
        }

        return at.CellId >> 16 == ground.Home.LifestoneCell >> 16 || JourneyGrounds.At(StationWhere.Holtburg, at) ? AutopilotPlace.Home : AutopilotPlace.Elsewhere;
    }

    // ------------------------------------------------------------------------------------------------ the station runs

    private ScenarioStatus BeginRun(AutopilotState state, StationSpec spec, string why, bool resume = false)
    {
        if (state == AutopilotState.Fight)
        {
            _fightPassSeen = 0; // M5 7 F29: a new fight counts its line passes from 0
            if (ReferenceEquals(spec, FightSpec) && InGround())
            {
                spec = FightResumeSpec; // M5 7 F30: on the ground already, the fight starts from where the mage stands
            }
        }

        if (!resume && StopDue() is { } stop)
        {
            return Stop(stop, ok: true, $"a stop rule is due before the {spec.Name} station");
        }

        _spec = spec;
        _guardDownLogged = false; // M5 7 P7o: each death station logs its stand-down once
        try
        {
            _station = _factory.Create(spec);
        }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return Stop($"failed:{StateName(state)}:factory", ok: false, $"the factory made no {spec.Name} station: {e.Message}");
        }

        _needs = spec.NeedsOverride ?? _station.Needs;
        _healOnly = false;
        if (state != AutopilotState.Death)
        {
            RestartDeathTally(); // the tally counts the deaths since this station began
        }
        else
        {
            _deathPending = true; // until the death station ends (DeathEnded) or the run stops: a cut keeps it (4-R2)
        }

        if (state == AutopilotState.Trip)
        {
            _tripPending = true; // until the logistics station ends (TripEnded): a cut runs it again
            if (!resume)
            {
                _tripStartedAt = _clock; // D126 (a): the trip's length is measured
                _tripForced = spec.Arguments.TryGetValue(JourneyPlans.TripArgument, out var trip) && trip == JourneyPlans.TripForced;
                _tripCutWork = false;
            }
        }

        SetState(state, why);
        _phaseStart = _clock;
        return StartPhases(skipKeeper: state == AutopilotState.Death && _unscripted);
    }

    /// <summary>The vitals pass, then the peace and the preconditions, then the station (or, for the heal in place, the end of the pass).</summary>
    private ScenarioStatus StartPhases(bool skipKeeper)
    {
        if (!skipKeeper && _context.Keeper is { } keeper)
        {
            var n = _needs;
            if (!keeper.NeedsFor(new VitalsNeeds((int)Math.Ceiling(n.MinHealthPercent), (int)Math.Ceiling(n.MinStaminaPercent), (int)Math.Ceiling(n.MinManaPercent))))
            {
                _phase = RunPhase.Vitals;
                return ScenarioStatus.Running;
            }
        }

        _phase = RunPhase.Peace;
        return ScenarioStatus.Running;
    }

    private void RestartDeathTally()
    {
        if (_deathPending)
        {
            return; // the tally holds the corpse of a death still to be handled
        }

        if (_deathTally is { } tally && _session is { } session)
        {
            tally.Start(session);
            tally.Reset();
        }
    }

    private ScenarioStatus VitalsStep(IGameSessionView session, double now)
    {
        var keeper = _context.Keeper!;
        switch (keeper.Step(now))
        {
            case VitalsKeeperState.Ready:
                _phase = RunPhase.Peace;
                _phaseStart = _clock;
                return ScenarioStatus.Running;
            case VitalsKeeperState.Blocked:
                return _deathSeen ? UnscriptedDeath() : StationFailed($"failed:{StateName(_state)}:vitals", $"the keeper is blocked before {_spec!.Name}: {keeper.Why ?? "blocked"}");
            default:
                return _clock - _phaseStart > JourneyScenario.VitalsTimeoutSeconds ? StationFailed($"failed:{StateName(_state)}:vitals", $"the keeper's pass before {_spec!.Name} took over {JourneyScenario.VitalsTimeoutSeconds:0} s") : ScenarioStatus.Running;
        }
    }

    private ScenarioStatus PeaceStep(IGameSessionView session)
    {
        if (_needs.Peace && session.State.Self.Qualities.CombatMode != CombatModes.NonCombat)
        {
            if (_pending is null)
            {
                _pending = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Automation);
                return ScenarioStatus.Running;
            }

            if ((_pending.Outcome is { Succeeded: false }) || _clock - _phaseStart > JourneyScenario.PeaceSeconds)
            {
                _pending = null;
                return StationFailed($"failed:{StateName(_state)}:peace", $"the peace mode did not show before {_spec!.Name}");
            }

            return ScenarioStatus.Running;
        }

        _pending = null;
        if (StationPreconditions.Miss(_needs, session.State, _log, _ground) is { } miss)
        {
            return _deathSeen ? UnscriptedDeath() : StationFailed($"failed:{StateName(_state)}:precondition:{miss}", $"the {_spec!.Name} station's need {miss} does not hold");
        }

        if (_healOnly)
        {
            return HealDone();
        }

        _run = _station!.Start(_context);
        _phase = RunPhase.Running;
        _phaseStart = _clock;
        _upkeepLastCastAt = _clock; // M5 7 F23: the upkeep's no-cast watchdog counts from the station's start
        _actionsSeen = _session?.ActionLog.Total ?? 0;
        _watchdogs.Enter(_state, _clock); // the state's clock runs from the station's start: the keeper's pass and the peace came first, each with its own bound (4-R7)
        _context.Observer.StationStarting(_spec!.Name, _clock);
        _log.Info(LogCategory, $"autopilot {StateName(_state)}: station {_spec.Name} starts");
        return ScenarioStatus.Running;
    }

    private ScenarioStatus RunStep(IGameSessionView session, double now)
    {
        var run = _run!;
        if (_deathSeen && _state != AutopilotState.Death)
        {
            return UnscriptedDeath(); // a death in any state: the station in progress is ended here (the real fight ends itself on it too)
        }

        if (_state == AutopilotState.Fight)
        {
            WatchFight(session);
        }
        else if (GuardHolds(session))
        {
            return ScenarioStatus.Running; // M5 7 F23: the guard holds the state: its station is not stepped, so it starts no cast of its own
        }

        if (_state == AutopilotState.Upkeep && UpkeepStalled(session, out var hostile))
        {
            return UpkeepNoCast(hostile);
        }

        var status = run.Step(session, now);
        if (status == ScenarioStatus.Running)
        {
            return ScenarioStatus.Running;
        }

        WhereMaxMetres = WhereMaxMetres is { } a && run.WhereMaxMetres is { } b ? Math.Max(a, b) : WhereMaxMetres ?? run.WhereMaxMetres;
        Unmatched += run.Unmatched;
        string fields;
        try
        {
            fields = run.VerdictFields();
        }
        catch (InvalidOperationException e)
        {
            fields = "fields_why=" + JourneyVerdict.Token(e.Message);
        }

        var ok = status == ScenarioStatus.Succeeded && run.Ok;
        var why = ok ? null : run.Failure ?? (status == ScenarioStatus.Succeeded ? "verdict" : "failed");
        var name = _spec!.Name;
        if (ok && _retry is { } retried)
        {
            _retry = null; // M5 7 D141: the retry's first station passed: the recovery worked
            _log.Info(LogCategory, $"autopilot recovery: reason={retried.Reason} fix={RecoveryTable.Name(retried.Fix)} retry=ok: the {name} station passed after the fix (the failure was {retried.Label})");
        }
        DisposeRun();
        _phase = RunPhase.Done;
        _context.Observer.StationEnded(name, ok, _clock);
        _log.Info(LogCategory, $"autopilot {StateName(_state)}: station {name} ended {status} ok={(ok ? "yes" : "no")}{(why is null ? string.Empty : " why=" + JourneyVerdict.Token(why))}: {fields}");
        return Ended(status, ok, why, fields);
    }

    private ScenarioStatus Ended(ScenarioStatus status, bool ok, string? why, string fields)
    {
        switch (_state)
        {
            case AutopilotState.Buff:
                if (_deathSeen)
                {
                    return UnscriptedDeath();
                }

                if (!ok)
                {
                    return StationFailed("failed:buff:" + JourneyVerdict.Token(why ?? "failed"), "the buff station failed", why);
                }

                return InGround() ? BeginRun(AutopilotState.Fight, FightSpec, "buffed in the ground") : BeginRun(AutopilotState.Travel, DenTravelSpec, "buffed");

            case AutopilotState.Travel:
                if (_deathSeen)
                {
                    return UnscriptedDeath();
                }

                if (_finalStop is not null)
                {
                    if (!ok && why?.Contains(NoLifestoneRefusal, StringComparison.Ordinal) == true)
                    {
                        // M5 7 F30 (soak round 6: a mage re-created and never bound logged off in the den after two refused recalls): a second try cannot succeed and the stop does not walk out to bind; the mage build binds after a re-create.
                        DisposeRun();
                        _log.Error(LogCategory, $"autopilot: not bound: the build binds after a re-create (the recall home before the stop was refused with {NoLifestoneRefusal}; no walk out to bind): {why}");
                        return FinishFinal(arrived: false, "not bound: the build binds after a re-create");
                    }

                    return ok ? BeginFinalSettle() : FinalAttemptFailed($"the recall home station failed: {why ?? "failed"}");
                }

                if (!ok && ReferenceEquals(_spec, RecallHomeSpec) && why?.Contains(NoLifestoneRefusal, StringComparison.Ordinal) == true)
                {
                    return NoBind(why);
                }

                if (!ok && ReferenceEquals(_spec, DenTravelSpec) && PortalUntied(why))
                {
                    return PortalGap($"the den travel's Primary Portal Recall was refused untied: {why}");
                }

                if (ok && ReferenceEquals(_spec, RecallHomeSpec))
                {
                    _bindHow ??= "a Lifestone Recall that landed this session";
                }
                else if (ok && ReferenceEquals(_spec, BindSpec))
                {
                    _bindHow = "the bind station this session";
                }
                else if (ok && ReferenceEquals(_spec, DenTravelSpec))
                {
                    _portalProven = true;
                }

                if (!ok)
                {
                    return StationFailed($"failed:travel:{_spec!.Name}:{JourneyVerdict.Token(why ?? "failed")}", "the travel station failed", why);
                }

                if (ReferenceEquals(_spec, BindSpec))
                {
                    return EnterKnown("bound at the home lifestone");
                }

                return ReferenceEquals(_spec, RecallHomeSpec) ? EnterKnown("recalled home") : BeginRun(AutopilotState.Fight, FightSpec, "in the ground");

            case AutopilotState.Fight:
                return FightEnded(status, why, fields);

            case AutopilotState.Trip:
                return TripEnded(ok, why, fields);

            case AutopilotState.Death:
                return DeathEnded(ok, why, fields);

            case AutopilotState.Upkeep:
                if (_deathSeen)
                {
                    return UnscriptedDeath();
                }

                if (ok)
                {
                    NoteBuffCycle();
                }

                return ok ? BackToTheFight($"the upkeep ({_upkeepDetail}) is done") : StationFailed("failed:upkeep:" + JourneyVerdict.Token(why ?? "failed"), "the upkeep's buff station failed", why);

            default:
                return Stop($"failed:{StateName(_state)}:unknown", ok: false, $"TODO(state): no arrow from the {StateName(_state)} state");
        }
    }

    /// <summary>
    /// A recall home refused with 0x049E (M5 7 F14): the character is bound to no lifestone. The client cannot read the bind beforehand (ACE's player description carries only the LastOutsideDeath position, and the state keeps none), so the refusal is
    /// the sign. The journey's bind station runs once, from where the mage stands, then the known state classifies again; a second refusal stops by name (<see cref="NoBindStop"/>).
    /// </summary>
    private ScenarioStatus NoBind(string why)
    {
        if (_boundOnce)
        {
            return Stop(NoBindStop, ok: false, $"the recall home was refused with {NoLifestoneRefusal} again after the autopilot had bound at {_ground.Home.Town}: {why}");
        }

        _boundOnce = true;
        _recalledHome = false;
        _log.Info(LogCategory, $"autopilot: no lifestone bind: binding at {_ground.Home.Town} first");
        return BeginRun(AutopilotState.Travel, BindSpec, "no lifestone bind (0x049E)");
    }

    // ------------------------------------------------------------------------------------------------ the start-up state audit (M5 7)

    /// <summary>
    /// Arms the start-up audit (<see cref="StartupAudit"/>): at the start and after each reconnect. The checkpoint's totals are kept (they were loaded, or are this run's); the recoveries' per-run guards are reset: the F14 bind-once and the
    /// recall-home-once (the watchdogs' recovery budget is per cycle and stays).
    /// </summary>
    private void BeginAudit(string why)
    {
        _audit = new StartupAudit(why, _clock);
        _packSorter = null;
        _wornRestorer = null;
        _auditFix = null; // M5 7 P7n: nothing of an earlier audit's fixes is carried
        _auditAction = null;
        _finished = null;
        _boundOnce = false;
        _recalledHome = false;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{StartupAudit.LinePrefix} begins ({why}): the checkpoint's totals are kept (kills={_total.Kills + _tally.Kills} trips={_total.Trips + _tally.Trips} deaths={_total.Deaths + _tally.Deaths} cycles={_cycles}); the per-run guards (the bind once, the recall home once) are reset"));
    }

    /// <summary>
    /// One known-state frame of the audit (M5 7). Returns the station or the stop a check chose, <see cref="ScenarioStatus.Running"/> while the kit check waits for the pack to read, or null to let the known state's own arrows go on (the
    /// audit finished, or the place check defers to them: dead goes to the death, elsewhere to the recall home, and the known state then runs the audit again from the top). The checks run in the order their fixes must: a corpse first (the
    /// recovery brings the kit back), the place, the bind, the portal tie, the kit, then the buffs.
    /// </summary>
    private ScenarioStatus? AuditStep(IGameSessionView session, StartupAudit audit)
    {
        // 0 (M5 7 P7n, D145): the stopped run's actions settle before anything is read, and no movement key it held is still down
        if (!audit.ActionsSettled && SettleActions(session, audit) is { } settling)
        {
            return settling;
        }

        // 5 first: a corpse of mine still standing
        if (_deathPending)
        {
            audit.Corpse = "recover";
            var corpse = _deathTally is { LastCorpseGuid: not 0 } t ? string.Create(CultureInfo.InvariantCulture, $"corpse 0x{t.LastCorpseGuid:X8}") : "its corpse";
            _log.Info(LogCategory, StartupAudit.Line("corpse", $"the death checkpoint holds a death whose corpse is not recovered ({corpse})", "recover: the death station runs first (its own rot handling covers a corpse that rotted)"));
            Notice(audit, "his corpse is not recovered yet: the death path first (the corpse, then the fight)");
            return ResumeDeath();
        }

        // M5 7 P7c (D139 row h): a wait resumes at its own step; the checks before it ran once this pass and are not run (nor logged) again each frame
        var resume = audit.WaitingOn;
        if (resume is null && AuditChecks(session, audit) is { } decided)
        {
            return decided.Status;
        }

        return AuditTail(session, audit, resume);
    }

    /// <summary>The audit's checks from the top (M5 7): the corpses in view, the place, the bind, the portal tie. A decision ends the frame (its status, null for the known state's own arrows); null goes on to the kit.</summary>
    private AuditDecision? AuditChecks(IGameSessionView session, StartupAudit audit)
    {
        var state = session.State;
        audit.Evaluations++;
        audit.BeginPass(); // M5 7 P7n: the per-pass steps (the windows, the kit, the caster, the stance) run again on a pass from the top

        if (!audit.CorpsesLooked)
        {
            audit.CorpsesLooked = true;
            var seen = CorpsesInView(state);
            _log.Info(LogCategory, StartupAudit.Line("corpse", seen.Count == 0 ? "no corpse of mine is in the death checkpoint or in view" : $"no death in the checkpoint; {seen.Count} corpse(s) of mine in view ({string.Join(',', seen)}): report-only, an earlier death's emptied corpse looks the same (the den keeps them; TODO(m5-audit-corpse-in-view))", "none"));
        }

        // 0b (P7n, D145) alive or dead, and out of portal space (the known state waited for the arrival before the audit ran)
        var place = Classify(state, _ground);
        var health = state.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var hp, out var hpMax) && hpMax > 0 ? string.Create(CultureInfo.InvariantCulture, $"health {hp} of {hpMax}") : "no health known yet";
        audit.Alive = place == AutopilotPlace.Dead ? "dead" : hpMax > 0 ? "alive" : "unknown";
        _log.Info(LogCategory, StartupAudit.Line("alive", $"{audit.Alive} ({health})", place == AutopilotPlace.Dead ? "the death path (the death station after the respawn)" : "none"));
        audit.Travel = "present";
        _log.Info(LogCategory, StartupAudit.Line("portal_space", string.Create(CultureInfo.InvariantCulture, $"out of portal space (travel {state.Self.Travel})"), "none"));

        // 1 where am I
        audit.Place = place.ToString().ToLowerInvariant();
        if (place is not (AutopilotPlace.Home or AutopilotPlace.Ground))
        {
            var fix = place == AutopilotPlace.Dead ? "the death path" : _recalledHome ? "none: the recall home already ran (the known state stops by name)" : "recall home first (the vitals pass before it dances for the mana when it is short): no route walks back from an unknown place, so he never guesses one";
            _log.Info(LogCategory, StartupAudit.Line("place", $"{audit.Place} ({Cell(session)})", fix));
            if (!audit.PlaceNoticed)
            {
                audit.PlaceNoticed = true;
                Notice(audit, place == AutopilotPlace.Dead
                    ? "he is dead: the death path first (the respawn, then his corpse)"
                    : $"he is neither in {_ground.Home.Town} nor in {_ground.Name} ({Cell(session)}): no route from here, so he recalls home, then takes the way back");
                audit.Note(place == AutopilotPlace.Dead ? "he was dead: the death path ran" : "he was away from home and the den: recalled home");
            }

            return new AuditDecision(null); // the known state's own arrows; after the recall home the audit runs again from the top
        }

        _log.Info(LogCategory, StartupAudit.Line("place", $"{audit.Place} ({Cell(session)}{ArrivalDistance(state)})", place == AutopilotPlace.Ground ? "none: the fight resumes from where he stands and names each walk it makes (D47: a target seen, a spawn due, else he holds)" : "none: the buff, then the den travel"));

        // 1b (P7n, D145) hostiles in reach, and whether something is attacking him: then the slow fixes wait and the fight fights back first (D88: always fight back an attacker)
        if (_hostiles is { } watch && watch.InReach(_clock, out var hostile))
        {
            audit.Hostiles = _clock - watch.LastAttackAt < HostileWatch.AttackQuietSeconds ? "attacked" : "in-reach";
            audit.Attacker = place == AutopilotPlace.Ground ? hostile : null;
            _log.Info(LogCategory, StartupAudit.Line("hostiles", $"{audit.Hostiles}: {hostile}", place == AutopilotPlace.Ground ? "self-defence first (D88): the fight fights back before any slow fix (the trip, the pack sort, the worn set, the buff pass wait)" : "none: not in the ground"));
            if (audit.Attacker is not null)
            {
                audit.Note("a hostile on him: fighting back first");
            }
        }
        else
        {
            audit.Hostiles = "clear";
            audit.Attacker = null;
            _log.Info(LogCategory, StartupAudit.Line("hostiles", string.Create(CultureInfo.InvariantCulture, $"clear: no attack within {HostileWatch.AttackQuietSeconds:0} s, no target creature within {_hostiles?.ReachMetres ?? 0:0.#} m"), "none"));
        }

        // 2 the lifestone bind
        var facts = _context.Facts;
        if (_bindHow is not null || facts.LifestoneTied == true)
        {
            audit.Bind = audit.BindTried ? "bound-now" : "proven";
            _log.Info(LogCategory, StartupAudit.Line("bind", "proven by " + (_bindHow ?? "a recall that landed or a Lifestone Tie this session (the journey's facts)"), "none"));
        }
        else if (place == AutopilotPlace.Home && !audit.BindTried)
        {
            audit.BindTried = true;
            _boundOnce = true; // the F14 bind on a 0x049E refusal does not bind a second time after this one
            var finding = facts.LifestoneTied == false
                ? $"disproven: a Lifestone Recall was refused with {NoLifestoneRefusal}"
                : "not proven this session (ACE sends no lifestone-bound quality: its player description carries only the LastOutsideDeath position)";
            _log.Info(LogCategory, StartupAudit.Line("bind", finding, $"bind at the {_ground.Home.Town} lifestone (Use, then Lifestone Tie: harmless when already bound)"));
            Notice(audit, $"his lifestone bind is not proven this session: he binds at the {_ground.Home.Town} lifestone first");
            audit.Note($"bound at the {_ground.Home.Town} lifestone");
            return new AuditDecision(BeginRun(AutopilotState.Travel, BindSpec, "audit: the lifestone bind is not proven"));
        }
        else
        {
            audit.Bind = "unknown";
            var why = audit.BindTried ? "the bind station ran and nothing proved the tie" : "not proven this session and the mage stands in the ground";
            _log.Warn(LogCategory, StartupAudit.Line("bind", why, $"none: the first Lifestone Recall that lands proves it; a {NoLifestoneRefusal} refusal binds at {_ground.Home.Town} (F14)"));
        }

        // 3 the portal tie
        var tie = facts.PortalTieOf(_ground.Key);
        if (_ground.Portal is null)
        {
            audit.PortalTie = "none";
            _log.Info(LogCategory, StartupAudit.Line("portal_tie", $"the ground {_ground.Key} has no portal record", "none"));
        }
        else if (_portalProven || tie == PortalTie.Tied)
        {
            audit.PortalTie = "proven";
            _log.Info(LogCategory, StartupAudit.Line("portal_tie", "proven by a den travel that arrived, or a Primary Portal Recall that landed, this session", "none"));
        }
        else if (tie == PortalTie.Untied)
        {
            return new AuditDecision(PortalGap($"ACE refused a Primary Portal Recall untied ({StartupAudit.NoPortalTieRefusal}) this session"));
        }
        else
        {
            audit.PortalTie = "pending";
            _log.Info(LogCategory, StartupAudit.Line("portal_tie", "not proven this session (ACE sends no portal-tie property: LinkedPortalOne is not SendOnLogin)", $"none: the den travel's Primary Portal Recall proves it; a {StartupAudit.NoPortalTieRefusal} refusal stops the run by the named gap TODO(m5-portal-retie-route)"));
        }

        return null;
    }

    /// <summary>
    /// The audit's tail (M5 7; widened by P7n, D145): the windows, the kit, the packs, the worn set, the caster, the stance, then the buffs, the Spirit Drinker aura, the vitals, the load and the loot queue. Each step runs once a pass
    /// (<see cref="StartupAudit.BeginPass"/>, the packs and the worn set once an audit); a step that takes frames is a wait, begun once with one line (<see cref="StartupAudit.WaitLine"/>), and the frames resume at it. With a hostile on
    /// the mage (<see cref="StartupAudit.Attacker"/>) the slow steps (a trip, the pack sort, the worn set, the buff pass) wait: the fight fights back first (D88).
    /// </summary>
    private ScenarioStatus? AuditTail(IGameSessionView session, StartupAudit audit, string? resume)
    {
        _ = resume; // M5 7 P7n: the steps keep their own "ran this pass" flags (StartupAudit.BeginPass), so a wait resumes at its own step
        var state = session.State;

        // 4a (P7n, D145) an open vendor, corpse or chest is closed first, and checked closed after (D141); trade is named
        if (!audit.WindowsDone && WindowsStep(session, audit) is { } windows)
        {
            return windows;
        }

        // 4 the components and the kit
        if (!audit.KitDone)
        {
            if (_tripPending)
            {
                audit.Kit = "trip";
                audit.KitDone = true;
                _log.Info(LogCategory, StartupAudit.Line("kit", "a trip that was cut is still to be made", audit.Attacker is null
                    ? "the trip again from its start: the logistics station reads its counters again (a trip cut after its sales buys only what is still short) and plans its walk from where he stands; a half-made walk or vendor visit is not resumable"
                    : "deferred: a hostile is on him: the fight fights back first (D88), the cut trip stays due for the next known state"));
                audit.Note(audit.Attacker is null ? "the cut trip runs again from its start" : "the cut trip waits until the fight is clear");
            }
            else
            {
                var kit = StartupAudit.ReadKit(state, _ground, _counters);
                if (kit.Unread)
                {
                    if (_clock - audit.StartedAt < StartupAudit.KitReadSeconds)
                    {
                        if (audit.BeginWait(StartupAudit.WaitKit, _clock))
                        {
                            _log.Info(LogCategory, StartupAudit.WaitLine(StartupAudit.WaitKit, string.Create(CultureInfo.InvariantCulture, $"the pack's list or a held item's description has not arrived ({kit.Verdict.Reason}); the check runs once when it is in, at most {StartupAudit.KitReadSeconds:0} s after the audit began")));
                        }

                        return ScenarioStatus.Running; // the pack's list or a held item's description has not arrived yet
                    }

                    audit.EndWait();
                    audit.Kit = "unread";
                    _log.Warn(LogCategory, StartupAudit.Line("kit", string.Create(CultureInfo.InvariantCulture, $"the pack did not read within {StartupAudit.KitReadSeconds:0} s ({kit.Verdict.Reason}): TODO(m5-audit-kit-unread)"), "none: the fight's counters judge the stock once it reads"));
                }
                else
                {
                    audit.EndWait();
                    NoteFloors(); // M5 7 P7s (D148): a carried line already at its floor is said once, and the run goes on
                    var missing = kit.Missing.Count == 0 ? string.Empty : $"; kit_missing={string.Join(',', kit.Missing)} (report-only in its first gate; a trip buys no worn kit yet: TODO(m5-audit-kit-restock))";
                    if (kit.CarriedShort.Count > 0)
                    {
                        audit.Kit = "gap";
                        _log.Error(LogCategory, StartupAudit.Line("kit", $"carried lines under their stop line: {string.Join(',', kit.CarriedShort)} (no {_ground.Home.Town} vendor sells them; TODO(m5-platinum-stock)){missing}", "stop: the run cannot fight past the counters' stop line"));
                        return AuditStop("logistics-stop:" + JourneyVerdict.Token(kit.Verdict.Trigger), $"a carried line is under its stop line ({string.Join(',', kit.CarriedShort)}) and no vendor of {_ground.Home.Town} sells it");
                    }

                    if (kit.TripDue && !audit.TripTried && audit.Attacker is null)
                    {
                        audit.TripTried = true;
                        audit.Kit = "trip";
                        var what = kit.Short.Count + kit.Low.Count > 0 ? string.Join(',', kit.Short.Concat(kit.Low)) : kit.Verdict.Reason;
                        _log.Info(LogCategory, StartupAudit.Line("kit", $"short: {what}{missing}", $"a trip to {_ground.Home.Town} first (by its counters)"));
                        Notice(audit, $"a supply trip is due ({what}): a trip to {_ground.Home.Town} first");
                        audit.Note($"made a supply trip to {_ground.Home.Town}");
                        return BeginRun(AutopilotState.Trip, TripSpec(forced: false), "audit: supplies short (" + what + ")");
                    }

                    if (kit.TripDue && audit.Attacker is not null)
                    {
                        audit.Kit = "trip-deferred";
                        _log.Info(LogCategory, StartupAudit.Line("kit", $"a trip is due: {string.Join(',', kit.Short.Concat(kit.Low))} {kit.Verdict.Reason}{missing}", "deferred: a hostile is on him: the fight fights back first (D88) and its counters call the trip"));
                        audit.Note("a supply trip waits until the fight is clear");
                    }
                    else if (kit.TripDue)
                    {
                        audit.Kit = "gap";
                        _log.Warn(LogCategory, StartupAudit.Line("kit", $"still short after the audit's trip: {string.Join(',', kit.Short.Concat(kit.Low))} {kit.Verdict.Reason}{missing}: TODO(m5-audit-restock)", "none: the run goes on; the fight's counters judge the stock"));
                    }
                    else
                    {
                        audit.Kit = kit.Missing.Count == 0 ? "ok" : "gap";
                        _log.Info(LogCategory, StartupAudit.Line("kit", $"every stock line at or over its stop and trip lines{(kit.Missing.Count == 0 ? "; the kit and the foci are held" : missing)}", "none"));
                    }
                }

                audit.KitDone = true;
            }
        }

        // 5b the packs (M5 7 D121): what the audit finds misplaced goes to the pack of its job, logged, once an audit (P7n: deferred while a hostile is on him)
        if (audit.Packs == "pending" && audit.Kit != "unread")
        {
            if (audit.Attacker is not null)
            {
                audit.Packs = "deferred";
                _log.Info(LogCategory, StartupAudit.Line("packs", "not sorted: a hostile is on him", "deferred: the fight fights back first (D88); the next audit or trip sorts them"));
            }
            else
            {
                _packSorter ??= new PackSorter(new PackRules(_ground), SaleMemory.Shared.KeptAside.Contains, PackMemory.Shared.Rotation.Contains, _log, LogCategory, "the start-up audit");
                if (_packSorter.Step(session))
                {
                    if (audit.BeginWait(StartupAudit.WaitPacks, _clock))
                    {
                        _log.Info(LogCategory, StartupAudit.WaitLine(StartupAudit.WaitPacks, "the pack sort's moves are in flight; the checks above ran once and are not run again while it waits"));
                    }

                    return ScenarioStatus.Running;
                }

                audit.EndWait();
                audit.Packs = _packSorter.Finding();
                _log.Info(LogCategory, StartupAudit.Line("packs", $"{_packSorter.Planned} item(s) out of their pack's job, {_packSorter.Stayed} left for a full pack", $"moved {_packSorter.Moved}, refused {_packSorter.Refused}"));
                if (_packSorter.Moved > 0)
                {
                    audit.Note(string.Create(CultureInfo.InvariantCulture, $"{_packSorter.Moved} item(s) put back in their packs"));
                }

                _packSorter = null;
            }
        }

        // 5c the worn set (M5 7 S2d): the remembered pieces, the clothing whose coverage is free, the cantrip wearables and the best caster held, once an audit (P7n: deferred while a hostile is on him)
        if (audit.WornRestored < 0 && audit.Kit != "unread")
        {
            if (audit.Attacker is not null)
            {
                audit.WornRestored = 0;
                _log.Info(LogCategory, StartupAudit.Line("worn", string.Create(CultureInfo.InvariantCulture, $"{state.Self.Inventory.Equipped.Count} piece(s) worn now; not restored: a hostile is on him"), "deferred: the fight fights back first (D88); the caster check below still wields a caster"));
            }
            else
            {
                _wornRestorer ??= new WornRestorer(_ground, state.Self.Qualities.Level, _context.Spells, _log, LogCategory, "the start-up audit");
                if (_wornRestorer.Step(session))
                {
                    if (audit.BeginWait(StartupAudit.WaitWorn, _clock))
                    {
                        _log.Info(LogCategory, StartupAudit.WaitLine(StartupAudit.WaitWorn, "the worn-set restore's appraisals and wears are in flight; the checks above ran once and are not run again while it waits"));
                    }

                    return ScenarioStatus.Running;
                }

                audit.EndWait();
                audit.WornRestored = _wornRestorer.Restored;
                _log.Info(LogCategory, StartupAudit.Line("worn", string.Create(CultureInfo.InvariantCulture, $"{state.Self.Inventory.Equipped.Count} piece(s) worn now"), string.Create(CultureInfo.InvariantCulture, $"restored {_wornRestorer.Restored}, refused {_wornRestorer.Refused}")));
                if (_wornRestorer.Restored > 0)
                {
                    audit.Note(string.Create(CultureInfo.InvariantCulture, $"{_wornRestorer.Restored} piece(s) put back on"));
                }

                _wornRestorer = null;
            }
        }

        // 5d (P7n, D145) the caster held, and whether it is the gear plan's pick; none held: the recovery table's wield, checked after (D141)
        if (!audit.CasterDone && CasterStep(session, audit) is { } caster)
        {
            return caster;
        }

        // 5e (P7n, D145) the combat stance: on the ground the fight's magic mode, checked after (D141)
        if (!audit.StanceDone && StanceStep(session, audit) is { } stance)
        {
            return stance;
        }

        // 6 the vitae and the buffs (P7n: every line and its time left, and the Spirit Drinker aura)
        BuffsCheck(session, audit);

        // 7 (P7n, D145) the vitals, the load and the loot queue: reads, each with its fix
        VitalsCheck(session, audit);
        LoadCheck(audit);
        LootQueueCheck(state, audit);
        FinishAudit(audit);
        return null;
    }

    /// <summary>A decision of the audit's checks (<see cref="AuditChecks"/>): the frame's status, or null for the known state's own arrows.</summary>
    private readonly record struct AuditDecision(ScenarioStatus? Status);

    private void FinishAudit(StartupAudit audit)
    {
        _audit = null;
        _finished = audit; // M5 7 P7n: the known state's arrow reads it this frame and sends its chat summary
        Audits++;
        LastAudit = audit.Summary();
        _log.Info(LogCategory, LastAudit);
    }

    /// <summary>The audit ends the run by name: its summary is written first.</summary>
    private ScenarioStatus AuditStop(string stop, string why)
    {
        if (_audit is { } audit)
        {
            FinishAudit(audit);
        }

        return Stop(stop, ok: false, why);
    }

    /// <summary>True when a den travel's failure says the ground's portal is not tied (the station sets the journey's fact on ACE's 0x04A3, and its walk to the portal is refused untied).</summary>
    private bool PortalUntied(string? why) =>
        _context.Facts.PortalTieOf(_ground.Key) == PortalTie.Untied
        || why?.Contains(StartupAudit.NoPortalTieRefusal, StringComparison.Ordinal) == true
        || why?.Contains(JourneyTravel.UntiedFailure, StringComparison.Ordinal) == true;

    /// <summary>
    /// The den portal is not tied (M5 7 audit): the fix would be Primary Portal Tie at the den portal, but no route reaches its front from the home town without a mod place (the journey refuses the Ground travel to it untied,
    /// <c>TODO(m6-first-tie-walk)</c>; the den's portal is in landblock 0x1B90, Holtburg's 0xA9B4), and the rules forbid a place after the build. The run stops by name.
    /// </summary>
    private ScenarioStatus PortalGap(string finding)
    {
        if (_audit is { } audit)
        {
            audit.PortalTie = "gap";
        }

        var portal = _ground.Portal is { } p ? string.Create(CultureInfo.InvariantCulture, $"portal 0x{p.Guid:X8} in cell 0x{p.Cell:X8}, tie spell {p.TieSpell}") : "no portal record";
        _log.Error(LogCategory, StartupAudit.Line("portal_tie", $"{finding} ({portal})", $"gap: TODO(m5-portal-retie-route): no route walks or travels from {_ground.Home.Town} to the portal's front without a mod place (the journey refuses it untied, TODO(m6-first-tie-walk)); the run stops"));
        return AuditStop(StartupAudit.PortalGapStop, $"the {_ground.Key} portal is not tied and no route reaches it without a place (TODO(m5-portal-retie-route))");
    }

    /// <summary>The guids of the corpses of the player in the object table (ACE names them "Corpse of &lt;name&gt;", <c>Creature_Death.cs</c> 496), report-only.</summary>
    private static List<string> CorpsesInView(GameState state)
    {
        var found = new List<string>();
        var name = state.Self.Name;
        if (string.IsNullOrEmpty(name))
        {
            return found;
        }

        var wanted = "Corpse of " + name;
        foreach (var o in state.Objects.All)
        {
            if ((o.DescriptionFlags & WorldObjectTable.CorpseFlag) != 0 && string.Equals(o.Name, wanted, StringComparison.Ordinal))
            {
                found.Add(string.Create(CultureInfo.InvariantCulture, $"0x{o.Guid:X8}"));
            }
        }

        return found;
    }

    // ------------------------------------------------------------------------------------------------ the fight

    private IStoppableFight? StoppableFight() => (_run as IStationInner)?.Scenario as IStoppableFight ?? _run as IStoppableFight;

    /// <summary>The fight's triggers, in order: a stop rule or request, the scripted death, the forced trip, the counters' stop or trip. Slice a3 adds the upkeep's (a buff line under its margin, the health floor) here.</summary>
    private void WatchFight(IGameSessionView session)
    {
        if (StoppableFight() is not { } fight)
        {
            if (!_noFightLogged && (_run as IStationInner)?.Scenario is not null)
            {
                _noFightLogged = true;
                _log.Error(LogCategory, "autopilot fight: the den-fight station's scenario takes no external stop (IStoppableFight): only its own end ends it (TODO(m5-autopilot-fight-stop))");
            }

            return;
        }

        if (fight is IBuffRefreshFight passes)
        {
            var seen = passes.BuffLinePasses; // M5 7 F29: fold the running fight's line passes into the run's
            if (seen != _fightPassSeen)
            {
                _fightLinePasses += Math.Max(0, seen - _fightPassSeen);
                _fightPassSeen = seen;
            }

            if (passes.BuffLineCount > 0)
            {
                _fightLineCount = passes.BuffLineCount;
            }
        }

        var kills = fight.Kills;
        if (kills != _lastKills)
        {
            _lastKills = kills;
            _watchdogs.Progress(_clock); // a fight that kills is working: its watchdog counts the time since the last kill
        }

        if (fight.StopReason is not null)
        {
            return;
        }

        FightStopReason? reason = null;
        if (StopDue() is { } stop)
        {
            _stopAsked ??= stop;
            reason = FightStopReason.StopRule;
        }
        else if (_options.TestDeath && !_testDeathDone && kills >= TestDeathAfterKills)
        {
            _testDeathDone = true;
            reason = FightStopReason.TestDeath;
        }
        else if (_options.TripAfterKills > 0 && !_forcedTripDone && _tally.Kills + kills >= _options.TripAfterKills)
        {
            _forcedTripDone = true;
            reason = FightStopReason.ForcedTrip;
        }
        else if (_clock - _lastCounters >= CounterCheckSeconds)
        {
            _lastCounters = _clock;
            var verdict = _counters.Check(session.State, BuffCycles);
            if (verdict.Kind != LogisticsKind.Unread)
            {
                NoteFloors(); // M5 7 P7s (D148)
            }

            if (verdict.IsStop)
            {
                _countersStop = JourneyVerdict.Token(verdict.Trigger);
                _log.Warn(LogCategory, $"autopilot fight: the counters say stop: {verdict.Reason}");
                reason = FightStopReason.LogisticsStop;
            }
            else if (verdict.IsTrip && !_tripLoop.Holding(_tally.Kills + kills))
            {
                _log.Info(LogCategory, $"autopilot fight: the counters say a trip is due: {verdict.Reason}");
                _tripCadence = verdict.Cause == LogisticsCause.Cadence;
                reason = FightStopReason.LogisticsTrip;
            }
        }

        if (reason is null && _clock - _lastUpkeep >= UpkeepCheckSeconds)
        {
            _lastUpkeep = _clock;
            reason = UpkeepDue(session);
        }

        if (reason is { } asked)
        {
            if ((asked == FightStopReason.LogisticsTrip || asked == FightStopReason.ForcedTrip) && fight is IBuffRefreshFight leaving)
            {
                // D126 (a) (soak round 12, 10:27 UTC: 15 lines came due inside the trip and lapsed in it): the lines due within the trip's expected length are recast before the fight ends for it
                _buffOptions ??= _options.Buff ?? new BuffOptions();
                var horizon = TripMeanSeconds + _buffOptions.LookaheadMarginSeconds;
                leaving.RequestLeaveRefresh(horizon, _options.AggroReachMetres, string.Create(CultureInfo.InvariantCulture, $"{asked.Name}: the trip's mean {TripMeanSeconds:0} s + margin {_buffOptions.LookaheadMarginSeconds:0} s"));
            }

            NoteWhy(asked.Name);
            _fightStop = asked;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot fight: stop asked ({asked.Name}) at kills={kills} (cycle {_cycle}: {_tally.Kills + kills} kills)"));
            fight.RequestStop(asked);
        }
    }

    /// <summary>
    /// M5 7 P7s (the owner's cheat-free default, D148; <c>m5-8-unsold-component-floor</c>): after a counted check, each floor-only carried line that reached its floor or rose back over it since the last word is logged and said once in
    /// chat. At the floor the run does not stop: the fight drops the spells that need it (its own counters, <c>FightScenario.WarPick.cs</c>) and fights on with the bolts, what is held stays as the reserve, and the spells come back
    /// when more turn up (loot, a trade). A line any other spell needs, and every line a vendor sells, keeps today's trip and stop.
    /// </summary>
    private void NoteFloors()
    {
        var mask = _counters.FloorMask;
        if (mask == _floorAnnounced)
        {
            return;
        }

        for (var i = 0; i < _counters.LineCount && i < 64; i++)
        {
            var bit = 1UL << i;
            if ((mask & bit) == (_floorAnnounced & bit))
            {
                continue;
            }

            var line = _counters.LineAt(i);
            var held = _counters.HeldOf(i);
            var spells = _floorOnly.TryGetValue(line.Wcid, out var names) && names.Count > 0 ? string.Join(" and ", names) : "the spells that need it";
            if ((mask & bit) != 0)
            {
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"components: {line.Name} (wcid {line.Wcid}) is at its floor: {held} held, under its stop line {line.StopUnder}; carried, no shop on the ground's trips sells it: {spells} is not cast until more turn up, the {held} held stay as the reserve, and the run fights on with the bolts (D148, m5-8-unsold-component-floor)"));
                Notice(string.Create(CultureInfo.InvariantCulture, $"Autopilot: {line.Name} is down to {held} and no shop sells it, so {spells} is put away; fighting on with bolts."));
            }
            else
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"components: {line.Name} (wcid {line.Wcid}) is back over its floor: {held} held, its stop line {line.StopUnder}; {spells} is cast again (D148)"));
                Notice(string.Create(CultureInfo.InvariantCulture, $"Autopilot: {line.Name} is back to {held}, so {spells} is cast again."));
            }
        }

        _floorAnnounced = mask;
    }

    /// <summary>The upkeep's two triggers, read every <see cref="UpkeepCheckSeconds"/>: the health under <see cref="HealthFloorPercent"/> (first: a mage near death is healed before it is buffed), a buff line of the profile under its margin (the lines the rebuff driver would call due).</summary>
    private FightStopReason? UpkeepDue(IGameSessionView session)
    {
        var healthFloor = HealthFloorFor(_ground, _options); // M5 7 F16 (D91): the ground's profile may say the fight never retreats for the health (0)
        if (healthFloor > 0 && session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var max) && max > 0 && health * 100.0 / max < healthFloor)
        {
            _upkeepDetail = string.Create(CultureInfo.InvariantCulture, $"health {health} of {max} is under the {healthFloor:0}% floor");
            _log.Info(LogCategory, $"autopilot fight: {_upkeepDetail}");
            return HealthFloor;
        }

        _buffOptions ??= _options.Buff ?? new BuffOptions();
        _buffCycle ??= new BuffCycle(session, _context.Spells, _buffOptions, _log, ArmourBuffLedger.For(session.State));
        if (_buffCycle.SeedPending)
        {
            if (!_seedLogged)
            {
                _seedLogged = true;
                _log.Info(LogCategory, "autopilot fight: the armour memory is not seeded yet: no buff line is counted due until it is (as the rebuff driver waits)");
            }

            return null;
        }

        var due = _buffCycle.RebuffDue(_clock, _buffOptions.RebuffMarginSeconds);
        if (due.Count == 0)
        {
            return null;
        }

        var detail = string.Create(CultureInfo.InvariantCulture, $"{string.Join(';', due)} under the {_buffOptions.RebuffMarginSeconds:0.#} s margin");
        if (StoppableFight() is IBuffRefreshFight refresh)
        {
            // M5 7 F23: the fight refreshes the lines itself, between kills, when no hostile is in reach; it is never stopped for a buff margin (any ground: only when the buff is cast changes, never what)
            if (!refresh.BuffRefreshAsked && !refresh.BuffsKeptAhead) // M5 7 F25: once armed, the fight keeps the lines itself (the lookahead at each lull): asked once
            {
                _log.Info(LogCategory, $"autopilot fight: a buff line is due: {detail}: the fight refreshes it between kills with no hostile in reach (F23); the fight is not stopped");
                refresh.RequestBuffRefresh(_buffOptions.RebuffMarginSeconds, _options.AggroReachMetres, detail);
            }

            return null;
        }

        if (_hostiles is { } watch && watch.InReach(_clock, out var what))
        {
            // M5 7 F23: never stop the fight for a buff margin while a hostile is in reach or attacking: the upkeep waits until it is clear (a line that runs out is the lesser harm)
            if (!_upkeepDeferLogged)
            {
                _upkeepDeferLogged = true;
                _log.Info(LogCategory, $"autopilot fight: a buff line is due: {detail}; the upkeep is deferred while {what} (F23: the fight is never stopped with a hostile in reach)");
            }

            return null;
        }

        _upkeepDeferLogged = false;
        _upkeepDetail = detail;
        _log.Info(LogCategory, $"autopilot fight: a buff line is due: {_upkeepDetail}");
        return BuffMargin;
    }

    /// <summary>
    /// M5 7 F23: one frame of the vitals guard in a state outside the fight (the mage in the world and alive). True while it holds the state. M5 7 P7o (the item-end gate's scripted death that never came): the guard stands down
    /// during a SCRIPTED death until the death is seen (<see cref="GuardStandsDown"/>): its heal's magic mode cancelled the D37 lie-down, ACE's non-combat defence factor stopped and the mage evaded 691 of 718 swings in 600 s.
    /// </summary>
    private bool GuardHolds(IGameSessionView session)
    {
        if (_deathSeen || _guard is not { } guard)
        {
            return false;
        }

        if (GuardStandsDown(_state, _unscripted, SawDeath(_deathTally)))
        {
            if (!_guardDownLogged)
            {
                _guardDownLogged = true;
                _log.Info(LogCategory, $"{GuardStandsDownLine}: the scripted death (--autopilot-test-death, D37) waits lying down in non-combat; no heal and no magic mode until the death is seen (the respawn, the corpse trip and the handler's fight are guarded again)");
            }

            return false;
        }

        if (_guardDownLogged && _state == AutopilotState.Death)
        {
            _guardDownLogged = false;
            _log.Info(LogCategory, "guard: death: the scripted death is seen: the guard is back for the respawn, the corpse trip and the handler's fight");
        }

        return guard.Step(session, _clock, StateName(_state));
    }

    /// <summary>The line the guard logs once when it stands down for a scripted death (M5 7 P7o): the lead greps it in the gate.</summary>
    public const string GuardStandsDownLine = "guard: death: stands down for the scripted death";

    /// <summary>
    /// M5 7 P7o: true while the vitals guard stands down: the death station of a SCRIPTED death (<c>--autopilot-test-death</c>, D37: undressed, lying down in non-combat among the tuskers) before the death is seen. An unscripted death's
    /// station, and the scripted one once the death is seen (the respawn, the corpse trip, the handler's fight), stay guarded.
    /// </summary>
    /// <param name="state">The autopilot's state.</param>
    /// <param name="unscripted">True for the station of a death the autopilot did not script (or a resumed one).</param>
    /// <param name="deathSeen">True once the autopilot's own death tally saw the death (a victim notice, a death count, the Dead motion or the corpse).</param>
    internal static bool GuardStandsDown(AutopilotState state, bool unscripted, bool deathSeen) =>
        state == AutopilotState.Death && !unscripted && !deathSeen;

    /// <summary>
    /// M5 7 F23: the upkeep's no-cast watchdog. Every completed cast in the Action API's log (the station's or the guard's) restarts its clock; an upkeep station with no cast for <see cref="UpkeepNoCastSeconds"/> while a hostile is in
    /// reach is stalled. Allocates only when it fires.
    /// </summary>
    private bool UpkeepStalled(IGameSessionView session, out string hostile)
    {
        hostile = string.Empty;
        var log = session.ActionLog;
        var total = log.Total;
        if (total != _actionsSeen)
        {
            var fresh = (int)Math.Min(total - _actionsSeen, log.Count);
            for (var i = log.Count - fresh; i < log.Count; i++)
            {
                if (log[i].Kind == ActionKind.CastSpell)
                {
                    _upkeepLastCastAt = _clock;
                }
            }

            _actionsSeen = total;
        }

        return _healOnly is false && _clock - _upkeepLastCastAt >= UpkeepNoCastSeconds && _hostiles is { } watch && watch.InReach(_clock, out hostile);
    }

    /// <summary>M5 7 F23: the upkeep station is ended by name (<see cref="UpkeepNoCastName"/>) and the fight takes the hostile.</summary>
    private ScenarioStatus UpkeepNoCast(string hostile)
    {
        var name = _spec!.Name;
        DisposeRun();
        _phase = RunPhase.Done;
        _context.Observer.StationEnded(name, false, _clock);
        NoteWhy(UpkeepNoCastName);
        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot upkeep: watchdog: the {name} station made no cast for {UpkeepNoCastSeconds:0} s while {hostile}: it ends ({UpkeepNoCastName}) and the fight takes the hostile"));
        return BackToTheFight($"the upkeep ended by its no-cast watchdog ({UpkeepNoCastName})");
    }

    /// <summary>Counts a trigger for the cycle line's <c>why=</c>.</summary>
    private void NoteWhy(string name)
    {
        var at = _whyNames.IndexOf(name);
        if (at < 0)
        {
            _whyNames.Add(name);
            _whyCounts.Add(1);
        }
        else
        {
            _whyCounts[at]++;
        }
    }

    /// <summary>The cycle's triggers as the line writes them: <c>name:count</c> joined by commas, or <c>none</c>.</summary>
    private string WhyField()
    {
        if (_whyNames.Count == 0)
        {
            return "none";
        }

        var parts = new string[_whyNames.Count];
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = string.Create(CultureInfo.InvariantCulture, $"{_whyNames[i]}:{_whyCounts[i]}");
        }

        return string.Join(',', parts);
    }

    private ScenarioStatus BackToTheFight(string why) =>
        BeginRun(AutopilotState.Fight, FightSpec, why);

    /// <summary>The heal in place: the vitals keeper's pass to the share the fight starts at, in peace, then the fight again. A keeper that cannot restore the health stops the run by name (<c>failed:upkeep:vitals</c>).</summary>
    private ScenarioStatus BeginHeal(string why)
    {
        if (StopDue() is { } stop)
        {
            return Stop(stop, ok: true, "a stop rule is due before the heal");
        }

        _spec = HealSpec;
        _station = null;
        _healOnly = true;
        _needs = new StationNeeds(StationWhere.Here, MinHealthPercent: JourneyPlans.FightHealthPercent); // no Peace need: the keeper itself returns to peace before it is Ready, and the fight enters its mode again
        RestartDeathTally();
        SetState(AutopilotState.Upkeep, why);
        _phaseStart = _clock;
        return StartPhases(skipKeeper: false);
    }

    private ScenarioStatus HealDone()
    {
        _healOnly = false;
        _phase = RunPhase.Done;
        _log.Info(LogCategory, $"autopilot upkeep: the keeper restored the health ({_upkeepDetail})");
        return _deathSeen ? UnscriptedDeath() : BackToTheFight("the health is restored");
    }

    private ScenarioStatus FightEnded(ScenarioStatus status, string? why, string fields)
    {
        AddFight(fields);
        var stop = _fightStop;
        _fightStop = null;
        if (_deathSeen)
        {
            return UnscriptedDeath();
        }

        if (status != ScenarioStatus.Succeeded)
        {
            return StationFailed("failed:fight:" + JourneyVerdict.Token(why ?? "failed"), "the fight failed", why);
        }

        if (stop is not { } reason)
        {
            return Stop("failed:fight:ended-unasked", ok: false, "the open-ended fight ended without a stop being asked");
        }

        if (reason == FightStopReason.StopRule)
        {
            return Stop(_stopAsked ?? StopDue() ?? "stop-rule", ok: true, "the fight stopped for the stop rule");
        }

        if (reason == FightStopReason.TestDeath)
        {
            return BeginRun(AutopilotState.Death, DeathSpec, "the scripted death (--autopilot-test-death, M4 D37)");
        }

        if (reason == FightStopReason.ForcedTrip)
        {
            return BeginRun(AutopilotState.Trip, TripSpec(forced: true), string.Create(CultureInfo.InvariantCulture, $"the forced trip after {_options.TripAfterKills} kills (D52)"));
        }

        if (reason == FightStopReason.LogisticsTrip)
        {
            return BeginRun(AutopilotState.Trip, TripSpec(forced: _tripCadence), _tripCadence ? "the sell trip on the buff-cycle schedule (F29, D96)" : "the counters' trip");
        }

        if (reason == FightStopReason.LogisticsStop)
        {
            return Stop("logistics-stop:" + (_countersStop ?? "?"), ok: false, "the logistics counters say stop");
        }

        if (reason == BuffMargin)
        {
            return BeginRun(AutopilotState.Upkeep, UpkeepSpec, $"buff-margin: {_upkeepDetail}");
        }

        if (reason == HealthFloor)
        {
            return BeginHeal($"health-floor: {_upkeepDetail}");
        }

        _upkeepDetail = reason.Name;
        _log.Warn(LogCategory, $"autopilot upkeep why={reason.Name}: TODO(m5-autopilot-reason): no upkeep is defined for this fight stop reason; back to the fight");
        return BackToTheFight("after an unknown fight stop reason " + reason.Name);
    }

    private void AddFight(string fields)
    {
        var t = _tally;
        t.Kills += Int(fields, "kills");
        t.Xp += Long(fields, "xp_gained");
        t.ManaWaitMaxSeconds = Math.Max(t.ManaWaitMaxSeconds, Double(fields, "mana_wait_max_s"));
        t.Fizzles += Int(fields, "fizzles");
        t.ManaTries += Int(fields, "mana_tries");
        t.ManaLanded += Int(fields, "mana_landed");
        t.StoreysVisited = Math.Max(t.StoreysVisited, Int(fields, "storeys_visited"));
        t.BasementEntries += Int(fields, "basement_entries");
        _hitMaxEnded = Math.Max(_hitMaxEnded, Long(fields, "hit_max")); // M5 7 F16
        _targetDiedEnded += Long(fields, "target_died_mid_cast"); // M5 7 F28
    }

    // ------------------------------------------------------------------------------------------------ the trip and the death

    /// <summary>D126 (a): the trip's expected length in seconds (an exponentially weighted mean of the trips made, seeded by <see cref="TripSeedSeconds"/>): the lines due within it plus the lookahead margin are recast before a trip.</summary>
    public double TripMeanSeconds => double.IsNaN(_tripMean) ? TripSeedSeconds : _tripMean;

    /// <summary>D126 (a): a trip that ended joins the trip's mean length (from its station's start to its end).</summary>
    private void NoteTripLength()
    {
        if (double.IsNaN(_tripStartedAt))
        {
            return;
        }

        var length = Math.Max(0, _clock - _tripStartedAt);
        _tripStartedAt = double.NaN;
        _tripMean = (TripMeanSeconds * (1 - TripMeanWeight)) + (length * TripMeanWeight);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot trip: it took {length:0} s; the trip's mean is now {_tripMean:0} s (the buff lines due within it are recast before the next, D126)"));
    }

    private ScenarioStatus TripEnded(bool ok, string? why, string fields)
    {
        var cutWork = _tripCutWork;
        if (Token(fields, "skipped") is { } skipped && !cutWork)
        {
            _tripPending = false;
            _log.Info(LogCategory, $"autopilot trip: no trip was made (skipped={skipped}): back to the fight");
            return _deathSeen ? UnscriptedDeath() : InGround() ? BeginRun(AutopilotState.Fight, FightSpec, "no trip was due") : EnterKnown("no trip was due");
        }

        if (!ok && !Unrecoverable(why))
        {
            FoldTrip(fields); // the failed trip's sold and bought count; the trip is made again, by its counters (4-R2, 4-R6)
            if (_tripFailedOnce || _watchdogs.Recoveries >= _watchdogs.MaxRecoveries)
            {
                return TripGivenUp(why);
            }

            _tripFailedOnce = true; // the one retry (the recovery's "the trip again after it was cut"); a second failure gives the trip up
            return StationFailed("failed:trip:" + JourneyVerdict.Token(why ?? "failed"), "the logistics station failed", why);
        }

        if (cutWork && Token(fields, "skipped") is not null)
        {
            _log.Info(LogCategory, "autopilot trip: the trip that was cut had made its sales and purchases already; the counters need no more: it counts as the trip");
        }

        _tripPending = false;
        _tripCutWork = false;
        _tripFailedOnce = false;
        NoteTripLength();
        _tally.Trips++;
        _counters.MarkTrip(BuffCycles); // M5 7 F29: the schedule counts from here
        _tally.Sold += Int(fields, "sold");
        _tally.Bought += Int(fields, "bought");
        if (_tripLoop.TripEnded(Int(fields, "sold"), Int(fields, "bought"), _tally.Kills))
        {
            _log.Warn(LogCategory, $"autopilot trip-loop: {TripLoopGuard.IdleLimit} trips in a row sold and bought nothing: trips are held back for {TripLoopGuard.HoldKills} kills and the run goes on fighting (M5 7 G1, edge audit 12a)");
        }

        _tally.SpentXp += Long(fields, "spent_xp");
        if (_deathSeen)
        {
            return UnscriptedDeath();
        }

        EndCycle("trip");
        return ok ? EnterKnown("after the trip") : Stop("failed:trip:" + JourneyVerdict.Token(why ?? "failed"), ok: false, "the logistics station failed");
    }

    /// <summary>
    /// M5 7 (TODO(m5-trip-failed-continue)): the trip failed after its one retry. A stock truly under its stop line (or a pack that cannot be read) stops the run by name, as before; with every line at or over its stop line the trip is given up for this
    /// cycle: a named gap line with the reason, the counters kept, the next trip held back by <see cref="TripLoopGuard.Hold"/> (<see cref="TripLoopGuard.HoldKills"/> kills, more than a buff cycle), and the mage goes back to the fight through the known state.
    /// </summary>
    private ScenarioStatus TripGivenUp(string? why)
    {
        var token = JourneyVerdict.Token(why ?? "failed");
        var kit = _session is { } session ? StartupAudit.ReadKit(session.State, _ground, _counters) : null;
        if (kit is null || kit.Unread || kit.Short.Count > 0 || kit.CarriedShort.Count > 0 || kit.Verdict.IsStop)
        {
            var named = kit is null || kit.Unread ? "the pack could not be read" : kit.Short.Count + kit.CarriedShort.Count > 0 ? "under their stop line: " + string.Join(',', kit.Short.Concat(kit.CarriedShort)) : kit.Verdict.Reason;
            return Stop("failed:trip:" + token, ok: false, $"the logistics station failed twice and the stock cannot go on ({named})");
        }

        _log.Warn(LogCategory, $"autopilot trip: the trip is given up for this cycle after its retry failed ({why ?? "failed"}); every stock line is at or over its stop line, so the run goes back to the fight; the next trip waits {TripLoopGuard.HoldKills} kills (TODO(m5-trip-failed-continue))");
        _tripPending = false;
        _tripCutWork = false;
        _tripFailedOnce = false;
        _tripLoop.Hold(_tally.Kills);
        return _deathSeen ? UnscriptedDeath() : EnterKnown("the trip was given up");
    }

    /// <summary>M5 7 F29 (D96): an upkeep (a buff line refreshed by the buff station) ended well: one buff cycle. The home buff before the fight (login, after a trip or a death) is the start of a cycle, not one.</summary>
    private void NoteBuffCycle() => _buffStations++;

    /// <summary>
    /// M5 7 F29 (D96, report-only; the soak line's <c>free_slots=</c> and <c>burden_pct=</c>): the main pack's free slots and the burden as a share of the capacity, or false before the load is known.
    /// </summary>
    /// <param name="freeSlots">The main pack's free slots.</param>
    /// <param name="burdenPercent">The burden in percent of the capacity.</param>
    public bool TryLoadShares(out int freeSlots, out double burdenPercent)
    {
        (freeSlots, burdenPercent) = (0, 0);
        if (_session is not { } session || !session.State.Self.Inventory.IsKnown)
        {
            return false;
        }

        var load = LootContext.Of(session.State);
        if (load.Capacity <= 0)
        {
            return false;
        }

        freeSlots = MageLootRules.FreeSlots(session.State);
        burdenPercent = load.Burden * 100.0 / load.Capacity;
        return true;
    }

    private ScenarioStatus DeathEnded(bool ok, string? why, string fields)
    {
        if (!ok && !Unrecoverable(why))
        {
            return StationFailed("failed:death:" + JourneyVerdict.Token(why ?? "failed"), "the death station failed", why); // the death stays pending: the corpse is still to be recovered (4-R2, 4-R6)
        }

        var deaths = Int(fields, "deaths");
        _tally.Deaths += deaths > 0 ? deaths : 1;
        _deathSeen = false;
        _unscripted = false;
        _deathPending = false;
        EndCycle("death");
        return ok ? EnterKnown("after the death") : Stop("failed:death:" + JourneyVerdict.Token(why ?? "failed"), ok: false, "the death station failed");
    }

    /// <summary>
    /// A death the autopilot did not script (seen by its PlayerDied subscription in any state, or a known state with no health) is handled by the death station entered after the respawn (<c>m5-autopilot-death-unscripted</c>): the autopilot's own
    /// death tally has been running since its start, so the corpse is known; it is offered to the station (<see cref="JourneyFacts.ObservedDeath"/>), which takes it and begins at the respawn instead of at the lie-down. The order after it is the handler's
    /// (D51, D53: the rearm, the rebuff, the corpse trip, the items back, the item lines), then the known state, which sends the mage back to the fight. A second death during the handler is the handler's own case (it restarts for the newer corpse).
    /// </summary>
    private ScenarioStatus UnscriptedDeath()
    {
        FoldInFlight(); // the kills made before the death count, and a trip cut by it counts its sold and bought and is made again after
        _deathSeen = false;
        _unscripted = true;
        NoteWhy(UnscriptedDeathStop);
        _context.Facts.ObservedDeath = _deathTally;
        _log.Warn(LogCategory, $"autopilot death: a death the autopilot did not script ({_deathText ?? "no health"}): the death station is entered after the respawn");
        return BeginRun(AutopilotState.Death, UnscriptedDeathSpec, "a death the autopilot did not script" + (_deathText is null ? string.Empty : $" ({_deathText})"));
    }

    /// <summary>
    /// The death station again after it was cut by a drop, a watchdog trip, a failure or an abandon (4-R2): the death's tally is kept, offered through <see cref="JourneyFacts.ObservedDeath"/> and the station enters
    /// after the respawn, from wherever the mage stands, until the corpse is recovered or named lost (rotted, not found) and the death is counted.
    /// </summary>
    private ScenarioStatus ResumeDeath()
    {
        _unscripted = true;
        _context.Facts.ObservedDeath = _deathTally;
        _log.Warn(LogCategory, "autopilot death: the death station was cut before the corpse was recovered: it is entered again with the death's tally (the corpse is recovered or named lost, and the death counted, before anything else)");
        return BeginRun(AutopilotState.Death, UnscriptedDeathSpec, "the death station again: the corpse is not recovered yet", resume: true);
    }

    /// <summary>The logistics station again after it was cut (4-R2): forced again only when the forced trip had made no sale, purchase or spending yet, else by its counters.</summary>
    private ScenarioStatus ResumeTrip()
    {
        var forced = _tripForced && !_tripCutWork;
        _log.Warn(LogCategory, $"autopilot trip: the trip was cut: the logistics station runs again ({(forced ? "forced: the forced trip was not taken yet" : "by its counters")})");
        return BeginRun(AutopilotState.Trip, TripSpec(forced), "the trip again after it was cut", resume: true);
    }

    /// <summary>
    /// What the state in flight keeps when its run is dropped (a reconnect, a watchdog trip, a recovery, an abandon, an unscripted death): a fight's fields count, a trip's sold and bought count and the trip stays to be made, a death station's
    /// tally stays to be handled (a scripted death that had not happened yet is scripted again); the run is disposed; the unscripted flag and the offered tally are cleared (4-R15: the death station sets them again when it is entered).
    /// </summary>
    private void FoldInFlight(bool clearFinal = true)
    {
        if (clearFinal && _finalStop is not null)
        {
            // a drop, a death or an abandon during the recall before the stop: the recall is given up; a stop rule still due asks for it again when the mage is back (M5 7 F18)
            _log.Warn(LogCategory, "autopilot: the recall home before the stop was cut; it is asked for again if the stop is still due");
            _finalStop = null;
            _finalAttempts = 0;
            _finalRetryFrom = double.NaN;
            _finalDone = false;
        }

        switch (_state)
        {
            case AutopilotState.Fight:
                if (_run is { } fight)
                {
                    AddFight(SafeFields(fight));
                }

                if (_fightStop == FightStopReason.TestDeath)
                {
                    _testDeathDone = false; // asked, not acted on: the next fight asks again
                }
                else if (_fightStop == FightStopReason.ForcedTrip)
                {
                    _forcedTripDone = false;
                }

                break;
            case AutopilotState.Trip:
                if (_run is { } trip)
                {
                    FoldTrip(SafeFields(trip));
                }

                break;
            case AutopilotState.Death:
                NoteDeathCut();
                break;
        }

        DisposeRun();
        _pending = null;
        _fightStop = null;
        _healOnly = false;
        _unscripted = false;
        _context.Facts.ObservedDeath = null;
    }

    private void FoldTrip(string fields)
    {
        var sold = Int(fields, "sold");
        var bought = Int(fields, "bought");
        var spentXp = Long(fields, "spent_xp");
        _tally.Sold += sold;
        _tally.Bought += bought;
        _tally.SpentXp += spentXp;
        if (sold + bought + Int(fields, "spent") + spentXp > 0)
        {
            _tripCutWork = true;
        }
    }

    private void NoteDeathCut()
    {
        if (!_unscripted && !SawDeath(_deathTally))
        {
            _deathPending = false;
            _testDeathDone = false; // the scripted death had not happened: it is scripted again at the next fight with a kill
            _log.Info(LogCategory, "autopilot death: the death station was cut before the mage died: the scripted death is made again");
            return;
        }

        _deathPending = true;
        _log.Warn(LogCategory, "autopilot death: the death station was cut: the death's tally is kept and the station is entered again from the known state");
    }

    private static bool SawDeath(DeathTally? tally) =>
        tally is { } t && (t.VictimNotices > 0 || t.Deaths > 0 || t.DeadMotions > 0 || t.LastCorpseGuid != 0);

    /// <summary>The facts a recall cannot change: the logistics counters' stop (a line under its stop line) and the third death. A failure text that merely contains "kit" is none of them (M5 7 G2, edge audit 12h): it goes to the recovery like any station failure. They stop the run by name at once (4-R6).</summary>
    private static bool Unrecoverable(string? why) =>
        why is { } w && (w.Contains("logistics: stop", StringComparison.Ordinal) || w.Contains("logistics:_stop", StringComparison.Ordinal) || w.Contains(DeathHandler.ThirdDeathStop, StringComparison.Ordinal));

    /// <summary>
    /// A station failed (4-R6): the same per-cycle recovery budget as the watchdogs (a recall home, then the known state), logged <c>recovery after &lt;state&gt;:&lt;why&gt;</c>; past the budget, or for a fact a recall cannot change
    /// (<see cref="Unrecoverable"/>), the run stops by <paramref name="stop"/>.
    /// </summary>
    /// <param name="stop">The stop name, <c>failed:&lt;state&gt;:&lt;why&gt;</c>.</param>
    /// <param name="detail">The sentence for the log.</param>
    /// <param name="raw">The station's own failure text, when it gave one.</param>
    private ScenarioStatus StationFailed(string stop, string detail, string? raw = null)
    {
        if (_finalStop is not null)
        {
            FoldInFlight(clearFinal: false);
            return FinalAttemptFailed($"{detail} ({stop})");
        }

        if (Unrecoverable(raw ?? detail))
        {
            return Stop(stop, ok: false, detail);
        }

        var label = stop.StartsWith("failed:", StringComparison.Ordinal) ? stop["failed:".Length..] : stop;
        if (TypedRecovery(stop, label, detail, raw) is { } typed)
        {
            return typed;
        }

        FoldInFlight();
        if (!_watchdogs.TryRecover())
        {
            return Stop(stop, ok: false, $"{detail}; the {_watchdogs.MaxRecoveries} recoveries of cycle {_cycle} are used");
        }

        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot recovery after {label}: {detail} (cycle {_cycle}, recovery {_watchdogs.Recoveries} of {_watchdogs.MaxRecoveries})"));
        return RecoverTo($"recovery after {label}");
    }

    /// <summary>
    /// M5 7 D141 part 4: the recovery table (<see cref="RecoveryTable"/>). The failure's typed reason is read from the action log (<see cref="RecoveryTable.ReasonOf"/>); a reason with a fix of its own is fixed
    /// (<see cref="RunPhase.Fixing"/>) and retried once, inside the cycle's recovery budget; the same reason again after its retry, or a fix that cannot be made, stops the run safely with the reason. Null: the generic recovery
    /// (an untyped failure, or a reason the table leaves to it). One line per decision: <c>autopilot recovery: reason=&lt;reason&gt; fix=&lt;fix&gt; ...</c>.
    /// </summary>
    private ScenarioStatus? TypedRecovery(string stop, string label, string detail, string? raw)
    {
        var reason = RecoveryTable.ReasonOf(_session?.ActionLog, _actionsSeen, raw ?? detail);
        if (_retry is { } previous)
        {
            _retry = null;
            if (reason == previous.Reason)
            {
                _log.Error(LogCategory, $"autopilot recovery: reason={reason} fix={RecoveryTable.Name(previous.Fix)} retry=failed: the same reason again after the fix ({label}): the run stops safely");
                return Stop(stop, ok: false, $"{detail}; the retry after the {RecoveryTable.Name(previous.Fix)} fix failed with the same reason ({reason})");
            }

            _log.Warn(LogCategory, $"autopilot recovery: reason={previous.Reason} fix={RecoveryTable.Name(previous.Fix)} retry=failed with another reason ({(reason == ActionReason.None ? "untyped" : reason.ToString())}): {label}");
        }

        if (reason == ActionReason.None)
        {
            return null;
        }

        var fix = RecoveryTable.FixFor(reason);
        if (fix == RecoveryFix.Generic)
        {
            _log.Info(LogCategory, $"autopilot recovery: reason={reason} fix=generic: the table has no fix of its own for it: the recall home and the known state");
            return null;
        }

        FoldInFlight();
        if (!_watchdogs.TryRecover())
        {
            _log.Error(LogCategory, $"autopilot recovery: reason={reason} fix={RecoveryTable.Name(fix)} not run: the {_watchdogs.MaxRecoveries} recoveries of cycle {_cycle} are used");
            return Stop(stop, ok: false, $"{detail}; the {_watchdogs.MaxRecoveries} recoveries of cycle {_cycle} are used");
        }

        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot recovery: reason={reason} fix={RecoveryTable.Name(fix)} after {label}: {detail} (cycle {_cycle}, recovery {_watchdogs.Recoveries} of {_watchdogs.MaxRecoveries}; one retry after the fix)"));
        _retry = new TypedRetry(reason, fix, label, stop, detail);
        _fix = new RecoveryFixRun(fix, _context.Keeper);
        _phase = RunPhase.Fixing;
        _phaseStart = _clock;
        return ScenarioStatus.Running;
    }

    /// <summary>
    /// M5 7 D141: steps the recovery table's fix. Made: the one retry (the fight again from where the mage stands, so it picks again; any other state by the recall home and the known state, which runs a cut trip or death again).
    /// Not made: the safe stop with the reason.
    /// </summary>
    private ScenarioStatus FixStep(IGameSessionView session)
    {
        var retry = _retry!;
        var fix = RecoveryTable.Name(retry.Fix);
        if (_deathSeen)
        {
            _fix = null;
            _retry = null;
            _log.Warn(LogCategory, $"autopilot recovery: reason={retry.Reason} fix={fix} cut by a death: the death comes first");
            return UnscriptedDeath();
        }

        var status = _fix!.Step(session, _clock);
        if (status == RecoveryFixStatus.Running)
        {
            return ScenarioStatus.Running;
        }

        var why = _fix.Why;
        _fix = null;
        if (status == RecoveryFixStatus.Failed)
        {
            _retry = null;
            _log.Error(LogCategory, $"autopilot recovery: reason={retry.Reason} fix={fix} failed: {why}: the run stops safely ({retry.Label})");
            return Stop(retry.Stop, ok: false, $"{retry.Detail}; the {fix} fix for {retry.Reason} could not be made: {why}");
        }

        var again = _state == AutopilotState.Fight && !_deathPending;
        _log.Info(LogCategory, $"autopilot recovery: reason={retry.Reason} fix={fix} done: {why}; retry: {(again ? "the fight again from where the mage stands" : "the recall home, then the known state")}");
        return again ? BeginRun(AutopilotState.Fight, FightSpec, $"retry after the {fix} fix ({retry.Reason})") : RecoverTo($"retry after the {fix} fix ({retry.Reason})");
    }

    /// <summary>The recovery's way back to a known state: the recall home, then the known state; a death station that was cut goes straight to the known state (a dead mage casts nothing, and its corpse is not at the lifestone).</summary>
    private ScenarioStatus RecoverTo(string why)
    {
        if (_deathPending)
        {
            return EnterKnown(why);
        }

        _recalledHome = true; // the recall home is this recovery's: the known state that follows stops by name if it finds the mage elsewhere again
        return BeginRun(AutopilotState.Travel, RecallHomeSpec, why);
    }

    // ------------------------------------------------------------------------------------------------ the watchdogs

    /// <summary>
    /// A state over its bound (<see cref="AutopilotWatchdogs"/>): logged by name, the run in progress dropped and recovered by the recall home and then the known state, at most <see cref="AutopilotOptions.WatchdogRecoveries"/> times a cycle;
    /// the next trip stops the run (<c>stop=watchdog:&lt;state&gt;</c>, ok=no).
    /// </summary>
    private ScenarioStatus WatchdogTrip(double held)
    {
        var state = _state;
        if (_finalStop is not null)
        {
            _log.Warn(LogCategory, AutopilotWatchdogs.Line(state, held) + " (during the recall home before the stop)");
            FoldInFlight(clearFinal: false);
            return FinalAttemptFailed($"the {StateName(state)} state went over its bound");
        }

        _log.Warn(LogCategory, AutopilotWatchdogs.Line(state, held) + string.Create(CultureInfo.InvariantCulture, $" (bound {_watchdogs.BoundSeconds(state):0} s, cycle {_cycle}, recovery {_watchdogs.Recoveries + 1} of {_watchdogs.MaxRecoveries})"));
        FoldInFlight(); // the kills made before the trip count; a cut trip's sold and bought; a cut death keeps its tally
        if (!_watchdogs.TryRecover())
        {
            return Stop("watchdog:" + StateName(state), ok: false, $"the {StateName(state)} state went over its bound {_watchdogs.MaxRecoveries + 1} times in cycle {_cycle}");
        }

        return RecoverTo($"watchdog recovery after the {StateName(state)} state went over its bound");
    }

    private static string SafeFields(IStationRun run)
    {
        try
        {
            return run.VerdictFields();
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    // ------------------------------------------------------------------------------------------------ the reconnect

    /// <summary>
    /// The session left the world (<c>m5-reconnect-auto</c>): a failure, a boot or the silence watchdog (<see cref="SessionPhase.Failed"/>, or a phase before the world after a reset), seen in any state. A log off or a close is the person's:
    /// the run ends. Otherwise the station in progress is dropped (a fight's kills so far are counted) and the host reconnects it with a bounded retry (<see cref="AutopilotOptions.ReconnectWaits"/>).
    /// </summary>
    private ScenarioStatus Dropped(IGameSessionView session)
    {
        var phase = session.State.Phase;
        FoldInFlight(); // a fight's kills so far count; a cut trip's sold and bought count and it is made again; a cut death keeps its tally (4-R2)
        if (phase is SessionPhase.LoggingOff or SessionPhase.Closing or SessionPhase.Closed)
        {
            return Stop(SessionClosedStop, ok: true, $"the session is {phase}: ended by the person or the host, not a drop to recover from");
        }

        if (_host is null)
        {
            return Stop(NoReconnectStop, ok: false, $"the session left the world (phase {phase}) in the {StateName(_state)} state and the host gave the autopilot no way to reconnect (IAutopilotSession)");
        }

        _reconnectFrom = _state;
        _reconnectTry = 1;
        _reconnectLoggedIn = false;
        _reconnectAt = _clock + Wait(1);
        _phase = RunPhase.Reconnecting;
        Stage = "reconnecting";
        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"reconnect: the session left the world (phase {phase.ToString().ToLowerInvariant()}) in the {StateName(_state)} state, cycle {_cycle}; try 1 of {_options.ReconnectWaits.Count} in {Wait(1):0.#} s"));
        return ScenarioStatus.Running;
    }

    private double Wait(int attempt) => _options.ReconnectWaits[Math.Min(attempt, _options.ReconnectWaits.Count) - 1];

    private ScenarioStatus ReconnectStep(IGameSessionView session)
    {
        var tries = _options.ReconnectWaits.Count;
        if (!_reconnectLoggedIn)
        {
            if (_clock < _reconnectAt)
            {
                return ScenarioStatus.Running;
            }

            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"reconnect: try {_reconnectTry} of {tries} after {Wait(_reconnectTry):0.#} s (session phase {session.State.Phase.ToString().ToLowerInvariant()})"));
            try
            {
                _host!.Reconnect();
            }
            catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException or IOException)
            {
                return TryFailed("Reconnect threw: " + e.Message);
            }

            _reconnectLoggedIn = true;
            _reconnectLoginStart = _clock;
            return ScenarioStatus.Running;
        }

        switch (_host!.StepLogin(session.Clock.Now))
        {
            case ScenarioStatus.Succeeded:
                if (session.State.Phase != SessionPhase.InWorld || session.State.Self.Guid == 0)
                {
                    return TryFailed($"the login ended outside the world (phase {session.State.Phase.ToString().ToLowerInvariant()})");
                }

                _tally.Reconnects++;
                _log.Info(LogCategory, $"reconnect: try {_reconnectTry} of {tries} ok: back in the world; reconnects={_total.Reconnects + _tally.Reconnects} this run, {_tally.Reconnects} this cycle; the state is classified from the world again");
                _watchdogs.Pause(_clock);
                BeginAudit("reconnect");
                return EnterKnown($"reconnected after {_reconnectTry} {(_reconnectTry == 1 ? "try" : "tries")} (dropped in the {StateName(_reconnectFrom)} state)");
            case ScenarioStatus.Failed:
                return TryFailed(_host.LoginFailure ?? "the login failed");
            default:
                return _clock - _reconnectLoginStart > ReconnectLoginSeconds ? TryFailed($"the login took over {ReconnectLoginSeconds:0} s") : ScenarioStatus.Running;
        }
    }

    private ScenarioStatus TryFailed(string why)
    {
        var tries = _options.ReconnectWaits.Count;
        _log.Warn(LogCategory, $"reconnect: try {_reconnectTry} of {tries} failed: {why}");
        if (_reconnectTry >= tries)
        {
            return Stop(ReconnectFailedStop, ok: false, $"all {tries} reconnect tries failed; the last: {why}");
        }

        _reconnectTry++;
        _reconnectLoggedIn = false;
        _reconnectAt = _clock + Wait(_reconnectTry);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"reconnect: try {_reconnectTry} of {tries} in {Wait(_reconnectTry):0.#} s"));
        return ScenarioStatus.Running;
    }

    // ------------------------------------------------------------------------------------------------ the checkpoint

    /// <summary>Reads the checkpoint at the start (the totals, the cycle count and the budget resume; the state is classified from the world, never from the file: D57) and arms the writes.</summary>
    private void LoadCheckpoint()
    {
        if (_checkpoints is null)
        {
            return;
        }

        _checkpointReady = true;
        var checkpoint = AutopilotCheckpoint.Load(_checkpoints, _context.Character, out var note, out var warn, _characterGuid, _options.RunTag);
        if (note is not null)
        {
            if (warn)
            {
                _log.Warn(LogCategory, "checkpoint: " + note);
            }
            else
            {
                _log.Info(LogCategory, "checkpoint: " + note);
            }
        }

        if (checkpoint is null)
        {
            return;
        }

        WornMemory.Shared.Seed(checkpoint.Worn); // M5 7 S2d: the worn set of the run being resumed
        _total.Add(checkpoint.Totals.ToTally());
        _tally.Add(checkpoint.CycleTally.ToTally());
        ApplySeed(new AutopilotSeed(checkpoint.Cycle, checkpoint.Cycles));
        _priorSeconds = Math.Max(_priorSeconds, checkpoint.MinutesUsed * 60); // the seed's seconds (a hand-back) and the file's: the larger stands
        if (checkpoint.MinutesTarget != _options.Minutes || checkpoint.LevelTarget != _options.Level)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"checkpoint: the stop rules differ from the file's (minutes {checkpoint.MinutesTarget:0.#} -> {_options.Minutes:0.#}, level {checkpoint.LevelTarget} -> {_options.Level}): this run's rules hold, the minutes used carry over"));
        }
    }

    private void WriteCheckpoint(string why, double at, bool quiet = false)
    {
        if (_checkpoints is null)
        {
            return;
        }

        _lastDecision = why;
        _lastDecisionAt = at;
        _lastWrite = _clock;
        var progress = new AutopilotTally();
        progress.Add(_tally);
        if (_state == AutopilotState.Fight && StopName is null && StoppableFight() is { } fight)
        {
            progress.Kills += fight.Kills; // the fight in progress: its kills so far (its other fields are counted when it ends)
        }

        var inCycle = CheckpointTally.From(progress);
        var checkpoint = new AutopilotCheckpoint
        {
            Character = _context.Character,
            CharacterGuid = _characterGuid,
            RunTag = _options.RunTag,
            Ground = _ground.Key,
            State = StateName(_state),
            Cycle = _cycle,
            Cycles = _cycles,
            Totals = CheckpointTally.From(_total),
            CycleTally = inCycle,
            MinutesUsed = SecondsUsed / 60,
            MinutesTarget = _options.Minutes,
            LevelTarget = _options.Level,
            LastDecision = why,
            LastDecisionAt = at,
            Stop = StopName,
            Completed = StopName is not null && Ok,
            Worn = WornMemory.Shared.Entries(), // M5 7 S2d: the worn set, for a restart's audit
        };
        try
        {
            _checkpoints.Write(checkpoint.ToJson());
            if (!quiet)
            {
                _log.Info(LogCategory, $"checkpoint: written ({checkpoint.State}, cycle {checkpoint.Cycle}, {_checkpoints.Describe})");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Warn(LogCategory, $"checkpoint: write failed ({_checkpoints.Describe}): {e.Message}; the next state change tries again");
        }
    }

    // ------------------------------------------------------------------------------------------------ the recall home before a stop (M5 7 F18)

    /// <summary>Stops that never recall: the session is gone or not the autopilot's (a drop, a close, the host's login), a death the autopilot did not script, and the recall home's own failures (they would only ask for the recall again).</summary>
    private static bool NoRecallStop(string stop) =>
        stop is SessionClosedStop or NoReconnectStop or ReconnectFailedStop or UnscriptedDeathStop or NoBindStop or "failed:known-state:not-in-world" or "failed:known-state:not-present" or "failed:known-state:elsewhere"
        || stop.StartsWith("failed:travel:recall-home", StringComparison.Ordinal);

    /// <summary>
    /// Where the mage stands when the run is about to stop, if a recall home should come first (on the ground or elsewhere, in the world, alive, the stop one that leaves the session usable), else null. The interrupt never gets here
    /// (<see cref="Abandon"/>: the person has the keys).
    /// </summary>
    private AutopilotPlace? RecallBeforeStop(string stop)
    {
        if (NoRecallStop(stop) || _session is not { } session || _phase == RunPhase.Reconnecting || _deathSeen || _deathPending || _abandoned)
        {
            return null;
        }

        if (session.State.Phase != SessionPhase.InWorld || session.State.Self.Travel != SelfTravel.Present)
        {
            return null;
        }

        var place = Classify(session.State, _ground);
        return place is AutopilotPlace.Ground or AutopilotPlace.Elsewhere ? place : null;
    }

    private ScenarioStatus BeginFinalRecall(FinalStop final, AutopilotPlace place)
    {
        _finalStop = final;
        _finalAttempts = 0;
        _finalRetryFrom = double.NaN;
        _log.Info(LogCategory, $"autopilot: recalling home before the stop (stop={final.Name}; the mage stands {(place == AutopilotPlace.Ground ? "on the ground" : "elsewhere")}): a character logged off away from home stays in the world and can be killed");
        return FinalAttempt("first");
    }

    private ScenarioStatus FinalAttempt(string why)
    {
        _finalAttempts++;
        FoldInFlightForStop();
        DisposeRun();
        return BeginRun(AutopilotState.Travel, RecallHomeSpec, $"the recall home before the stop (try {_finalAttempts}{(_finalAttempts <= FinalRecallAttempts ? $" of {FinalRecallAttempts}" : string.Empty)}, {why})", resume: true);
    }

    private ScenarioStatus BeginFinalSettle()
    {
        _phase = RunPhase.FinalSettle;
        _phaseStart = _clock;
        return ScenarioStatus.Running;
    }

    private ScenarioStatus FinalSettleStep(IGameSessionView session)
    {
        if (_deathSeen)
        {
            return UnscriptedDeath();
        }

        var self = session.State.Self;
        if (self.Travel == SelfTravel.Present && Classify(session.State, _ground) == AutopilotPlace.Home)
        {
            return FinishFinal(arrived: true, "home");
        }

        return _clock - _phaseStart > FinalSettleSeconds
            ? FinalAttemptFailed(string.Create(CultureInfo.InvariantCulture, $"the mage was not home {FinalSettleSeconds:0} s after the recall (travel {self.Travel}, place {Classify(session.State, _ground).ToString().ToLowerInvariant()})"))
            : ScenarioStatus.Running;
    }

    private ScenarioStatus FinalAttemptFailed(string why)
    {
        DisposeRun();
        if (_finalAttempts < FinalRecallAttempts)
        {
            _log.Warn(LogCategory, $"autopilot: the recall home before the stop failed ({why}); it is tried once more");
            return FinalAttempt("again");
        }

        // M5 7 G2 (edge audit 12g): never log off in the ground. While the mage stands there, wait and recall again every FinalRetrySeconds for FinalRetryLimitSeconds from the second failure; past that the stop goes on, named.
        if (double.IsNaN(_finalRetryFrom))
        {
            _finalRetryFrom = _clock;
        }

        if (_session is { } s && s.State.Phase == SessionPhase.InWorld && Classify(s.State, _ground) == AutopilotPlace.Ground && _clock - _finalRetryFrom + FinalRetrySeconds <= FinalRetryLimitSeconds)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot: the recall home before the stop failed twice ({why}); the mage is on the ground, so it does not log off here: the recall is tried again in {FinalRetrySeconds:0} s (retries end {FinalRetryLimitSeconds - (_clock - _finalRetryFrom):0} s from now)"));
            _phase = RunPhase.FinalRetryWait;
            _phaseStart = _clock;
            return ScenarioStatus.Running;
        }

        _log.Error(LogCategory, $"autopilot: the recall home before the stop failed twice ({why}); the stop goes on with the mage away from home");
        return FinishFinal(arrived: false, why);
    }

    private ScenarioStatus FinalRetryWaitStep()
    {
        if (_deathSeen)
        {
            return UnscriptedDeath();
        }

        return _clock - _phaseStart >= FinalRetrySeconds ? FinalAttempt("retry") : ScenarioStatus.Running;
    }

    private ScenarioStatus FinishFinal(bool arrived, string detail)
    {
        var final = _finalStop!;
        _finalStop = null;
        _finalDone = true;
        _log.Info(LogCategory, arrived ? $"autopilot: the mage is {detail}; the stop goes on" : $"autopilot: stopping without the recall home ({detail})");
        return Stop(final.Name, final.Ok, final.Why + (arrived ? string.Empty : "; TODO(m5-autopilot-stop-recall): the recall home before the stop failed twice, the mage is not home"));
    }

    // ------------------------------------------------------------------------------------------------ the stop rules, the cycles and the lines

    /// <summary>The stop rule due now, or null: a request, the minutes (the session clock since the start), the level (the state's PropertyInt 25).</summary>
    private string? StopDue()
    {
        if (_stopAsked is { } asked)
        {
            return asked;
        }

        if (_options.Minutes > 0 && SecondsUsed >= _options.Minutes * 60)
        {
            return "minutes";
        }

        if (_options.Level > 0 && _session is { } s && s.State.Self.Qualities.TryGetInt(PlayerQualities.LevelProperty, out var level) && level >= _options.Level)
        {
            return "level";
        }

        return null;
    }

    private void EndCycle(string how)
    {
        CloseTally();
        Emit(string.Create(CultureInfo.InvariantCulture, $"autopilot cycle n={_cycle} state={how} why={WhyField()} {_tally.Fields()} s={_tally.Seconds:0}"));
        _total.Add(_tally);
        _whyNames.Clear();
        _whyCounts.Clear();
        _cycles++;
        _cycle++;
        _watchdogs.NewCycle();
        _tally = new AutopilotTally();
        _cycleStart = _clock;
        _cycleVerbs = _session?.Tools.Verbs.Counts ?? _cycleVerbs;
    }

    private void CloseTally()
    {
        if (_session is { } s)
        {
            var delta = s.Tools.Verbs.Counts.Since(_cycleVerbs);
            _tally.Granted += delta.Granted;
            _tally.Places += delta.Place;
        }

        _tally.Seconds = _clock - _cycleStart;
    }

    private ScenarioStatus Stop(string stop, bool ok, string why)
    {
        if (_finalStop is not null)
        {
            return FinalAttemptFailed($"{stop}: {why}"); // the recall's own station could not be made or began wrongly: a try spent, never a second recall
        }

        if (!_finalDone && RecallBeforeStop(stop) is { } place)
        {
            return BeginFinalRecall(new FinalStop(stop, ok, why), place);
        }

        FoldInFlightForStop();
        DisposeRun();
        PeaceAtStop();
        _phase = RunPhase.Done;
        StopName = stop;
        Ok = ok;
        if (!ok)
        {
            Failure = $"{stop}: {why}";
        }

        if (ok)
        {
            _log.Info(LogCategory, $"autopilot stop={stop}: {why}");
        }
        else
        {
            _log.Error(LogCategory, $"autopilot stop={stop}: {why}");
        }
        CloseTally();
        Emit(string.Create(CultureInfo.InvariantCulture, $"autopilot cycle n={_cycle} state=stopped why={WhyField()} {_tally.Fields()} s={_tally.Seconds:0}"));
        _total.Add(_tally);
        _tally = new AutopilotTally();
        var seconds = double.IsNaN(_startClock) ? 0 : _clock - _startClock;
        Emit(string.Create(CultureInfo.InvariantCulture, $"autopilot done ok={(ok ? "yes" : "no")} cycles={_cycles} stop={stop} {_total.Fields()} s={seconds:0}"));
        SetState(AutopilotState.Stopped, stop);
        Dispose();
        _end = ok ? ScenarioStatus.Succeeded : ScenarioStatus.Failed;
        NoticeStop(stop, why); // M5 7 P7n (D145): the stop says why in the chat window, and the host unlocks the keys on Stopped
        Stopped?.Invoke(stop);
        return _end;
    }

    /// <summary>M5 7 F25 (D93): the stop is one of the moments the stance leaves the magic mode: the mage in the world, alive and in a combat mode is put back in peace (sent; the stop does not wait for it).</summary>
    private void PeaceAtStop()
    {
        if (_session is not { } session || session.State.Phase != SessionPhase.InWorld || _deathSeen || session.State.Self.Qualities.CombatMode is 0 or CombatModes.NonCombat)
        {
            return;
        }

        _log.Info(LogCategory, "autopilot stop: the stance returns to peace (F25: the stations held the magic mode until the stop)");
        session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Automation);
    }

    /// <summary>A stop with a death still to be handled counts that death (its corpse was not recovered): the done line reads what happened.</summary>
    private void FoldInFlightForStop()
    {
        if (_deathPending && SawDeath(_deathTally))
        {
            _tally.Deaths++;
            _log.Warn(LogCategory, "autopilot stop: the death station had not ended; the death is counted, its corpse was not recovered");
        }

        _deathPending = false;
        _tripPending = false;
    }

    private void SetState(AutopilotState state, string why)
    {
        var from = _state;
        _state = state;
        Stage = StateName(state) + (_spec is { } spec && state is not AutopilotState.KnownState and not AutopilotState.Stopped and not AutopilotState.Upkeep ? ":" + spec.Name : string.Empty);
        _log.Info(LogCategory, $"autopilot state {StateName(from)} -> {StateName(state)} (cycle {_cycle}): {why}");
        if (state is AutopilotState.KnownState or AutopilotState.Stopped)
        {
            _watchdogs.Enter(state, _clock);
        }
        else
        {
            _watchdogs.Hold(state); // a station's state counts from the station's start, after the keeper's pass and the peace (PeaceStep; 4-R7)
        }

        _lastKills = 0;
        if (_checkpointReady)
        {
            WriteCheckpoint(why, _clock);
        }

        StateChanged?.Invoke(new AutopilotTransition(from, state, why, _cycle, _clock));
    }

    private void Emit(string line)
    {
        _lines.Add(line);
        _log.Info(LogCategory, line);
    }

    private bool InGround() => _session is { } s && JourneyGrounds.At(StationWhere.Den, s.State.Self.Position, _ground);

    private void DisposeRun()
    {
        (_run as IDisposable)?.Dispose();
        _run = null;
    }

    /// <summary>The state's token (<c>known-state</c>, <c>buff</c>, ...).</summary>
    /// <param name="state">The state.</param>
    public static string StateName(AutopilotState state) => state == AutopilotState.KnownState ? "known-state" : state.ToString().ToLowerInvariant();

    private static string Cell(IGameSessionView session) =>
        session.State.Self.Position is { } at ? "cell 0x" + at.CellId.ToString("X8", CultureInfo.InvariantCulture) : "no position";

    private static string? Token(string fields, string name)
    {
        foreach (var part in fields.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length > name.Length && part[name.Length] == '=' && part.StartsWith(name, StringComparison.Ordinal))
            {
                return part[(name.Length + 1)..];
            }
        }

        return null;
    }

    /// <summary>
    /// M5 7 F16 (D91): the health share under which the upkeep stops the fight for the health on <paramref name="ground"/>: the profile's <c>fight.retreat_health_percent</c> when it has one (0 = never, the den), else the options' derived line.
    /// The other upkeep reasons (the buff margin, the logistics counters) do not read it.
    /// </summary>
    /// <param name="ground">The hunting ground.</param>
    /// <param name="options">The autopilot's options.</param>
    public static double HealthFloorFor(HuntingGround ground, AutopilotOptions options)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(options);
        return ground.FightRetreatHealthPercent is { } percent ? percent : options.HealthFloorPercent;
    }

    private long _hitMaxEnded;
    private long _targetDiedEnded;
    private int _stanceSeen;

    /// <summary>
    /// M5 7 F25 (D93): the combat-mode changes seen while the autopilot runs (the soak line's <c>stance_changes=</c>, report-only; soak round 3 had 75 in 71 minutes). The stations hold the magic mode between each other; a change is
    /// expected only for a vendor (a station with a Peace need), the stop, a log off or a relogin.
    /// </summary>
    public int StanceChanges { get; private set; }

    /// <summary>Counts a change of the mage's combat mode since the last step (in the world only; an unknown mode, 0, is not a change). Reads one integer, allocates nothing.</summary>
    private void NoteStance(IGameSessionView session)
    {
        if (session.State.Phase != SessionPhase.InWorld)
        {
            return;
        }

        var mode = session.State.Self.Qualities.CombatMode;
        if (mode == 0)
        {
            return;
        }

        if (_stanceSeen != 0 && mode != _stanceSeen)
        {
            StanceChanges++;
        }

        _stanceSeen = mode;
    }

    /// <summary>The biggest single hit the mage took over the run so far (the ended fights and the one in progress; the soak line's <c>hit_max=</c>).</summary>
    public long HitMax => Math.Max(_hitMaxEnded, StoppableFight()?.HitMax ?? 0);

    /// <summary>The casts whose target died or left during the wind-up over the run so far (the ended fights and the one in progress; the soak line's <c>target_died_mid_cast=</c>, M5 7 F28, report-only).</summary>
    public long TargetDiedMidCast => _targetDiedEnded + (StoppableFight()?.TargetDiedMidCast ?? 0);

    /// <summary>The mage's health, stamina and mana as shares of their maxima, in percent, at the last step (the soak line's <c>hp_pct= sp_pct= mp_pct=</c>); false before the session or the vitals are known. Reads three integers, allocates nothing.</summary>
    /// <param name="health">The health share.</param>
    /// <param name="stamina">The stamina share.</param>
    /// <param name="mana">The mana share.</param>
    public bool TryVitalShares(out double health, out double stamina, out double mana)
    {
        (health, stamina, mana) = (0, 0, 0);
        if (_session is not { } session)
        {
            return false;
        }

        var q = session.State.Self.Qualities;
        if (!q.TryGetVitalLevels(MageTemplate.MaxHealth, out var h, out var hMax) || !q.TryGetVitalLevels(MageTemplate.MaxStamina, out var s, out var sMax) || !q.TryGetVitalLevels(MageTemplate.MaxMana, out var m, out var mMax) || hMax == 0 || sMax == 0 || mMax == 0)
        {
            return false;
        }

        (health, stamina, mana) = (h * 100.0 / hMax, s * 100.0 / sMax, m * 100.0 / mMax);
        return true;
    }

    private static int Int(string fields, string name) => int.TryParse(Token(fields, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static long Long(string fields, string name) => long.TryParse(Token(fields, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double Double(string fields, string name) => double.TryParse(Token(fields, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
