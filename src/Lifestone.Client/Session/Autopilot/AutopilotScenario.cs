using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
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
/// <param name="TripMeanSeconds">M6 6 ME (<c>m6-trip-mean-resume</c>): the trip's mean length so far (<see cref="AutopilotScenario.TripMeanSeconds"/>), NaN while no trip has been folded (the seed length then stands); a hand-back and a resume keep it.</param>
/// <param name="TripMeanCount">M6 6 ME: the trips folded into <paramref name="TripMeanSeconds"/>.</param>
/// <param name="TaskDone">M6 6 ME (MB's open point): the run-time task's work was done (<see cref="AutopilotScenario.TaskDone"/>) and only its recall home and stop were left; a hand-back during that recall goes straight to the stop, never a second trip.</param>
/// <param name="TripCutWork">M6 6 FXM2 (6-M7): the cut trip had sold or bought something before the cut (<c>_tripCutWork</c>); the rebuilt autopilot's by-counters trip, when it is skipped, still counts as the trip that was made (as inside one scenario).</param>
/// <param name="TaskName">M6 6 FXM2 (6-M2): the name of the task <paramref name="TaskDone"/> and <paramref name="PendingTrip"/> belong to (<see cref="AutopilotTask.Name"/>); a hand-back that changes the task carries neither of them into the new one.</param>
/// <param name="Seat">What a re-seat carries that a hand-back does not; null for every other start (<see cref="AutopilotHostLogic.AskSeat"/>).</param>
public readonly record struct AutopilotSeed(int Cycle, int Cycles, double SecondsUsed = 0, DeathTally? PendingDeath = null, string? PendingTrip = null, double TripMeanSeconds = double.NaN, int TripMeanCount = 0, bool TaskDone = false, bool TripCutWork = false, string? TaskName = null, AutopilotSeatCarry? Seat = null);

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
        ["kills", "xp", "deaths", "trips", "sold", "bought", "spent_xp", "mana_wait_max_s", "fizzles", "mana_tries", "mana_landed", "storeys_visited", "basement_entries", "granted", "places", "interrupted", "resumed", "reconnects", "retreats", "rest_s"];

    /// <summary>The kills (the fights' <c>kills=</c>).</summary>
    public int Kills { get; set; }

    /// <summary>M6 6 F2: the retreats taken (the fight's, the upkeep heal's and the guard's; <c>retreats=</c>).</summary>
    public int Retreats { get; set; }

    /// <summary>The experience the fights earned (<c>xp_gained=</c>).</summary>
    public long Xp { get; set; }

    /// <summary>The deaths.</summary>
    public int Deaths { get; set; }

    /// <summary>M6 7 U: the corpse trips given up after the death station failed its recoveries (<c>corpse_recovered=no trip_abort=corpse_lost</c>, a true failure counted, the <c>[ladder]</c> line's <c>corpses_lost=</c>; not on the cycle line).</summary>
    public int CorpsesLost { get; set; }

    /// <summary>The logistics trips made.</summary>
    public int Trips { get; set; }

    /// <summary>The items sold on the trips.</summary>
    public int Sold { get; set; }

    /// <summary>The items bought on the trips.</summary>
    public int Bought { get; set; }

    /// <summary>The experience spent on the trips.</summary>
    public long SpentXp { get; set; }

    /// <summary>M6 1 d: the learn scrolls the trips bought (the logistics line's <c>scrolls_bought=</c>; the <c>[ladder]</c> line's, not the cycle line's).</summary>
    public int ScrollsBought { get; set; }

    /// <summary>M6 1 d: the spells the trips learned (the logistics line's <c>learned=</c>; the <c>[ladder]</c> line's, not the cycle line's).</summary>
    public int Learned { get; set; }

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

    /// <summary>M6 7 AB: the seconds the fights rested before a pull (the fight's <c>rest_s=</c>; report-only, D27: the optimizer's share of time at rest).</summary>
    public double RestSeconds { get; set; }

    /// <summary>Adds <paramref name="other"/> (the maxima as maxima).</summary>
    /// <param name="other">A cycle's tally.</param>
    public void Add(AutopilotTally other)
    {
        ArgumentNullException.ThrowIfNull(other);
        (Kills, Xp, Deaths, Trips, Sold, Bought, SpentXp) = (Kills + other.Kills, Xp + other.Xp, Deaths + other.Deaths, Trips + other.Trips, Sold + other.Sold, Bought + other.Bought, SpentXp + other.SpentXp);
        (ScrollsBought, Learned) = (ScrollsBought + other.ScrollsBought, Learned + other.Learned); // M6 1 d
        (ManaWaitMaxSeconds, Fizzles, ManaTries, ManaLanded, StoreysVisited, BasementEntries) = (Math.Max(ManaWaitMaxSeconds, other.ManaWaitMaxSeconds), Fizzles + other.Fizzles, ManaTries + other.ManaTries, ManaLanded + other.ManaLanded, Math.Max(StoreysVisited, other.StoreysVisited), BasementEntries + other.BasementEntries);
        (Granted, Places, Interrupted, Resumed, Reconnects, Seconds) = (Granted + other.Granted, Places + other.Places, Interrupted + other.Interrupted, Resumed + other.Resumed, Reconnects + other.Reconnects, Seconds + other.Seconds);
        Retreats += other.Retreats; // M6 6 F2
        RestSeconds += other.RestSeconds; // M6 7 AB
        CorpsesLost += other.CorpsesLost; // M6 7 U
    }

    /// <summary>The fields, <see cref="FieldNames"/> in order, space-separated.</summary>
    public string Fields() => string.Create(
        CultureInfo.InvariantCulture,
        $"kills={Kills} xp={Xp} deaths={Deaths} trips={Trips} sold={Sold} bought={Bought} spent_xp={SpentXp} mana_wait_max_s={ManaWaitMaxSeconds:0.#} fizzles={Fizzles} mana_tries={ManaTries} mana_landed={ManaLanded} storeys_visited={StoreysVisited} basement_entries={BasementEntries} granted={Granted} places={Places} interrupted={Interrupted} resumed={Resumed} reconnects={Reconnects} retreats={Retreats} rest_s={RestSeconds:0}");
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

    /// <summary>D126 (a), re-measured by M6 item 1 g from the long run (<c>cache/m6/longrun-measures/SUMMARY.md</c>, "m5-leave-refresh-bound: measured"): the seed of the trip's expected length in seconds, from the fight's end to the next fight's start: the sell trips took 77 to 130 s, median 83 s (n=7; soak round 12's were 97, 121 and 119 s).</summary>
    public const double TripSeedSeconds = 85;

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
    private static readonly IReadOnlyDictionary<string, string> UnattendedField = new Dictionary<string, string>(StringComparer.Ordinal) { [JourneyStations.UnattendedArgument] = "yes", [JourneyStations.FieldArgument] = "yes" }; // M5 7 F23: the driver's fight guard never holds the autopilot's own buff station; M6 14 (m6-banes): the upkeep's pass runs in the ground, so the six other banes are left to the pass at home
    private static readonly IReadOnlyDictionary<string, string> StartPass = new Dictionary<string, string>(StringComparer.Ordinal) { [JourneyStations.UnattendedArgument] = "yes", [JourneyStations.StartPassArgument] = "yes" }; // D126 (a): the start's pass recasts every line due within one buff cycle

    private readonly AutopilotOptions _options;
    private readonly IStationFactory _factory;
    private readonly JourneyContext _context;

    private readonly ILogSink _log;
    private readonly HuntingGround _ground;
    private LogisticsCounters _counters; // M6 16 E5d: the session's one set (Quartermaster.TripDesk), taken at the first step (BindTrips); until then what the job states

    /// <summary>M5 7 P7s (D148): the run's logistics counters (they know the floor-only carried lines); the host's next-trigger line reads them.</summary>
    public LogisticsCounters Counters => _counters;

    /// <summary>The ground this scenario was built on (the steer desk's reports and live picture read it).</summary>
    public HuntingGround Ground => _ground;

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
    private bool _deathAnsweredCut; // M6 7 U: a death station was cut after the handler's answer: the recovery goes to the known state, not home
    private BuffCycle? _buffCycle;
    private int? _passBook; // M6 7 AO: the spellbook's size when the last whole pass ended; null: none yet in this run (LearnedPassRule)
    private BuffOptions? _buffOptions;
    private double _lastUpkeep = double.NegativeInfinity;
    private double _tripMean = double.NaN;
    private int _tripMeanCount;
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
    private bool _buyTripTried; // M6 4 t4b: the buy-trip was queued once this run; a line still under its stop after it stops by name (no second try)
    private bool _buyTripActive; // the Trip state is running the buy-trip station, not the logistics station
    private string _buyTripStop = string.Empty; // the audit's stop name, kept for the failure
    private string _buyTripWhy = "not asked";
    private readonly TripLoopGuard _tripLoop = new(); // M5 7 G1 (12a)
    private TripStint _stint = new(); // M6 14 T: the minimum fight stint

    /// <summary>M6 14 T: the minimum fight stint in seconds (<see cref="TripStint"/>; 0 is off): for this long after the run began or a trip ended, the fight's counters call no trip but a stop line's. Set before the run starts.</summary>
    public double MinStintSeconds
    {
        get => _stint.Seconds;
        set => _stint = new TripStint(value, _stint.SaleSeconds);
    }

    /// <summary>M6 safe load: the sale's floor in seconds (<see cref="TripStint.SaleSeconds"/>, the owner's 40 minutes by default; never under the stint): a trip to sell waits this long after the run began or a trip ended. Set before the run starts.</summary>
    public double MinSaleSeconds
    {
        get => _stint.SaleSeconds;
        set => _stint = new TripStint(_stint.Seconds, value);
    }

    /// <summary>M6 14 T: the trips the minimum stint held back, one a trigger a stint.</summary>
    public int StintHeld => _stint.Held;
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
    private string? _lastFailure; // M6 7 I1: the signature of the last station failure (stop, cell, failure text); a passed station clears it
    private int _failureRepeats;
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
    private bool _finalRungTried;
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
    private GlobalPosition? _travelMark; // M6 4 t3b: where the rung change's progress was last seen
    private uint _travelMarkLandblock;

    /// <summary>M6 4 t3b: the metres the body moves in a rung change that count as the Travel state's progress (Lifestone's own: about three seconds of running).</summary>
    public const double TravelProgressMetres = 10;
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
    private bool _reconnectLeftWorld; // M6 14 body: the session has been out of the world since this reconnect began (a later in-world as her is a re-login that happened)
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

        /// <summary>M6 8 N (D120): the survival layer's last resort, <c>/lifestone</c>, runs (AutopilotScenario.LastResort.cs); nothing else moves her.</summary>
        LastResort,
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
    /// <param name="handOver">M6 item 14: what the run that just ended in this session proved (the move-on's restart; <see cref="AutopilotHandOver"/>), or null for a cold start. Each part is checked against the world before it is used.</param>
    public AutopilotScenario(AutopilotOptions options, IStationFactory factory, JourneyContext context, IAutopilotCheckpointStore? checkpoints = null, IAutopilotSession? host = null, AutopilotSeed? seed = null, AutopilotHandOver? handOver = null)
    {
        _handOverGiven = handOver;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _fightLine.Times = Vitals.CreatureAttackTimes.For(context.Dats); // M6 14 attack timing: the lines read each creature's own attack timing from the dats
        if (options.Errors.Count > 0)
        {
            throw new ArgumentException("autopilot: the --autopilot-* options do not parse: " + string.Join("; ", options.Errors), nameof(options));
        }

        _log = context.Log;
        _ground = context.Dungeon;
        _manaWatch = new WornManaWatch(_log, LogCategory);
        var formulaAccount = JourneyStations.IsHostFactory(factory) ? JourneyStations.AccountNameFor(factory, context.Log) : context.Account; // M6 1 w, 1-R9: the name the server hashes (the host's login name; null, with a warning, when the host gave none: the foci rule), not the account key h1 passed; a scripted factory (a test's) keeps the context's
        context.Facts.FormulaAccount = formulaAccount; // M6, the recall's components: every way-home decision of the run judges Lifestone Recall's components on this account's formula (RecallReadiness)
        _options = _options with { Buff = (_options.Buff ?? new BuffOptions()) with { AccountName = formulaAccount, ComponentNeeds = ComponentNeeds.Shared } }; // M6 7 AT: the passes name a short component to the counters; M6 1 h1 (TODO(m6-account-formula-callers)): the account's name reaches the autopilot's buff cycle and vitals guard through the buff options
        _floorOnly = ComponentFloor.FloorOnly(_ground, context.Spells, formulaAccount, school => !CharacterPolicy.For(context.Character).StarterFoci || school is DatReaderWriter.Enums.MagicSchool.WarMagic or DatReaderWriter.Enums.MagicSchool.LifeMagic); // M6 1 h1: the climber holds the starter foci of War and Life only (StartupAudit.AssumedFoci); any other character holds the foci its kit lists. M5 7 P7s (D148): a carried line only the war options need drops them at its floor, not the run
        _counters = Quartermaster.TripDesk.Stated(_ground, _floorOnly.Keys); // M6 16 E5d: the job's statement; the session's trip desk adopts it at the first step (AutopilotScenario.Trips.cs)
        _checkpoints = checkpoints;
        _host = host;
        _watchdogs = new AutopilotWatchdogs(options.WatchdogScale, options.WatchdogRecoveries, AutopilotWatchdogs.FightBaseSeconds(_ground), options.Job == AutopilotJob.LevelUp ? AutopilotWatchdogs.ClimberPassSeconds(LadderLoader.WarMage.Vitals) : 0);
        _context.Facts.MinutesStopDue = TripSafePointStop; // M6 8 E: the trip's safe point reads the same rule as StopDue; M6 item 14: and a pending stop request, so no way back is begun under one
        _context.Facts.PassAtHomeDue = options.Job == AutopilotJob.LevelUp ? PassAtHomeDue : null; // M6 audit (finding 9): a trip that leaves a big pass due casts it at home, never in the ground
        _context.Facts.BuffBotOn = BuffBotApplies; // M6 item 14: the trip's stop at the buff bot reads the same rule as the Home arrow's
        _context.Facts.CasterSpend = options.Job == AutopilotJob.LevelUp ? LadderCasterFacts : null; // M6 14 (m6-caster-spend): the trip's spend reads the held casters as the level-up spend does
        _context.Facts.LoreSpend = options.Job == AutopilotJob.LevelUp ? LadderLoreFacts : null; // M6 14 (m6-lore-spend): and the pieces that wait on her Arcane Lore
        _context.Facts.LoreWanted = options.Job == AutopilotJob.LevelUp ? LadderLoreWanted : null; // M6 14 own lore: a find or worn piece waiting on her lore starts the spend at once
        _context.Facts.BuffBotProfile = options.BuffBotProfile; // M6 item 14: and the same profile (--autopilot-buff-bot-word, 'all' by default): every stop of the run tells its key and is judged by its lines
        _context.Facts.Worn = _worn; // M6 6 F2: the run's own worn memory, the death station's DeathOptions.Worn (never WornMemory.Shared)
        _context.Facts.Trial = options.Job == AutopilotJob.LevelUp ? options.Trial : null; // M6 item 14: the corpse run of a death on the trial's ground reads it
        _context.Facts.DeferCorpse = options.Job == AutopilotJob.LevelUp ? DeferCorpseRule : null; // M6 sprint (the owner's decision of 2026-11-18): the vitae burnt first, the corpse after (AutopilotScenario.VitaeBurn.cs)
        _context.Facts.EverySpell = options.EverySpell && options.Job == AutopilotJob.LevelUp; // M6 14 (the owner, 2026-11-16): the climb's trips learn every spell she can cast
        _context.Facts.EverySpellVilla = options.Villa && options.Job == AutopilotJob.LevelUp && options.Task.Kind == AutopilotTaskKind.Job; // VillaOn's rule: the reserve holds the villa's price while its track runs
        _context.Facts.QuestOwnKillLoop = options.QuestOwnKillLoop; // M6 16 Q8: the quest steps' old kill loop, the skirmish's fallback for its first live gate
        _context.Facts.ApproachReportOnly = options.RungHoldsReportOnly; // M6 holding at 60: the crossing's measure reports, never gives up
        _context.Facts.Race = GuardRace; // M6 the dance before running on (docs/session.md section 123): a travel leg's dance gate reads the vitals guard's own race
        if (options.Task.Kind == AutopilotTaskKind.DenRun)
        {
            _context.Facts.LearnTravelLines = ScrivenerLearnPlan.DenRunLines; // M6 item 14: the den run's two scrolls, first in the trip's learn plan
            _counters.OrderLines = ScrivenerLearnPlan.DenRunLines;
            _context.Facts.CoinToNotes = true; // the owner's order of 2026-11-13: the den run's trip turns the coin into notes (a death takes half the coin, no note)
            _context.Facts.DenRunRehearse = options.DenRunRehearse; // the rehearsal: the road cut short of the Desert March Portal
        }

        if (seed is { } given)
        {
            _orphanAfter = "hand-back"; // M6 6 FXM2 (6-M15): a rebuilt scenario's orphan probe says what armed it
            ApplySeed(given);
        }
    }

    /// <summary>Continues the cycle number and the cycles count from <paramref name="seed"/> (never backwards: the larger count stands).</summary>
    private void ApplySeed(AutopilotSeed seed)
    {
        _cycle = Math.Max(_cycle, Math.Max(1, seed.Cycle));
        _cycles = Math.Max(_cycles, Math.Max(0, seed.Cycles));
        if (seed.Seat is { } seat)
        {
            ApplySeat(seat); // a re-seat, not a hand-back: the run's guards go on (AutopilotScenario.Steer.cs)
        }

        if (double.IsFinite(seed.SecondsUsed))
        {
            _priorSeconds = Math.Max(_priorSeconds, Math.Max(0, seed.SecondsUsed)); // the budget carries over a hand-back (4-R5)
        }

        if (double.IsFinite(seed.TripMeanSeconds) && seed.TripMeanSeconds > 0 && seed.TripMeanCount > _tripMeanCount)
        {
            _tripMean = seed.TripMeanSeconds; // M6 6 ME (m6-trip-mean-resume): a hand-back or a resume keeps what the trips taught
            _tripMeanCount = seed.TripMeanCount;
        }

        // M6 6 FXM2 (6-M2): the task's "done" and its cut trip belong to the task that set them: a hand-back that changed the task (a quest chosen after a finished errand) starts the new one clean. A seed with no name (a checkpoint's,
        // an older caller's) keeps the earlier behaviour.
        var sameTask = seed.TaskName is null || string.Equals(seed.TaskName, _options.Task.Name, StringComparison.Ordinal);
        if (!sameTask)
        {
            _log.Info(LogCategory, $"autopilot task: {_options.Task.Name}: the hand-back changed the task from {seed.TaskName}; its done mark{(seed.PendingTrip is null ? string.Empty : " and its cut trip")} is not carried");
        }

        if (seed.TaskDone && sameTask && _options.Task.Kind != AutopilotTaskKind.Job)
        {
            _taskDone = true; // the task's work was done before the hand-back: its known-state arrow goes straight to the recall home and the task-done stop
            _taskBegun = true;
            _log.Info(LogCategory, $"autopilot task: {_options.Task.Name}: the work was done before the hand-back; only the recall home and the stop are left");
        }

        if (seed.PendingDeath is { } death)
        {
            _deathTally = death;
            _deathPending = true;
        }

        if (seed.PendingTrip is { } trip && (sameTask || _options.Task.Kind != AutopilotTaskKind.Quest))
        {
            _tripPending = true;
            _tripForced = trip == JourneyPlans.TripForced;
            _tripCutWork = seed.TripCutWork; // M6 6 FXM2 (6-M7): a hand-back keeps that the cut trip had done work
        }
    }

    /// <summary>
    /// The buff station's spec: the journey's buff with its Holtburg need overridden by Here (the mage buffs where it stands: at home, or in the ground after a cold start), its mana need kept. D126 (a): a start's (or restart's) pass
    /// recasts every line due within one buff cycle (<see cref="JourneyStations.StartPassArgument"/>).
    /// </summary>
    public static StationSpec BuffSpec { get; } = new("buff", StationKinds.Buff, new StationNeeds(MinManaPercent: JourneyStations.MageBuffManaPercent), 200, StartPass);

    /// <summary>M6 7 AT: <see cref="BuffSpec"/> for a pass in the ground: its wait's cut for an attackable creature within the aggro reach (<see cref="JourneyStations.ShootFirstArgument"/>), so the fight shoots it first and she rests after.</summary>
    /// <param name="start">True: a start's pass, which recasts every line due within one buff cycle. False (M6 7 lead, 2026-11-06, live: every learned scroll recast all 18 lines, 7 minutes): the rebuff margin, so a line with time left stays.</param>
    private StationSpec GroundBuffSpec(bool start = true)
    {
        var arguments = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [JourneyStations.UnattendedArgument] = "yes",
            [JourneyStations.ShootFirstArgument] = _options.AggroReachMetres.ToString("0.###", CultureInfo.InvariantCulture),
            [JourneyStations.FieldArgument] = "yes", // M6 14 (m6-banes): a pass in the ground casts Impenetrability, Bludgeon Bane and the banes of the damage its creatures deal; the others are the pass at home's
        };
        if (start)
        {
            arguments[JourneyStations.StartPassArgument] = "yes";
        }

        return new("buff", StationKinds.Buff, new StationNeeds(MinManaPercent: JourneyStations.MageBuffManaPercent), 200, arguments);
    }

    /// <summary>D126 (a): the upkeep's buff station (a buff margin outside a refreshing fight): <see cref="BuffSpec"/> with the re-buff margin, not the start pass's.</summary>
    public static StationSpec UpkeepSpec { get; } = new("buff", StationKinds.Buff, new StationNeeds(MinManaPercent: JourneyStations.MageBuffManaPercent), 200, UnattendedField);

    /// <summary>The den travel's spec (Primary Portal Recall to the ground's tie, or the walk to its portal).</summary>
    public static StationSpec DenTravelSpec { get; } = new("den-travel", StationKinds.DenTravel, null, 60);

    /// <summary>The recall home's spec (from elsewhere, once).</summary>
    public static StationSpec RecallHomeSpec { get; } = new("recall-home", StationKinds.RecallHome, null, 30);

    /// <summary>M6 4 t3b: the rung change out (<see cref="RungChangeStation"/>, direction=out) in place of <see cref="DenTravelSpec"/> where <see cref="AutopilotTravelRule.Out"/> says so (a routed ground, an untieable or untied entry).</summary>
    public static StationSpec RungOutSpec { get; } = new(RungChangeStation.StationName, StationKinds.RungChange, null, 600, new Dictionary<string, string>(StringComparer.Ordinal) { [RungChangeStation.DirectionArgument] = "out" });

    /// <summary>M6 4 t3b: the way home by portals (<see cref="RungChangeStation"/>, direction=home) in place of <see cref="RecallHomeSpec"/> where <see cref="AutopilotTravelRule.Home"/> says so (no Lifestone Recall in the book, D43, and a way home).</summary>
    public static StationSpec RungHomeSpec { get; } = new(RungChangeStation.StationName + "-home", StationKinds.RungChange, null, 600, new Dictionary<string, string>(StringComparer.Ordinal) { [RungChangeStation.DirectionArgument] = "home" });

    /// <summary>M6 4 t3b: the stop when the rung change out ended well but the check after it does not find the player present in the ground's landblocks.</summary>
    public const string RungNotInGroundStop = "failed:travel:rung-change:not-in-ground";

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

    /// <summary>M6 4 t4b: the buy-trip station's spec (a carried line under its stop line; the Trip state runs it, <c>_buyTripActive</c>).</summary>
    public static StationSpec BuyTripSpec { get; } = new("buy-trip", StationKinds.BuyTrip, null, 900);

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

    /// <summary>M6 1 w: the account name the buff cycle, the vitals guard and the floor-only lines read (the host's login name, else the context's account); hashed by <see cref="AccountFormula"/>, never printed. For the tests.</summary>
    internal string? FormulaAccountName => _options.Buff?.AccountName;

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

    /// <summary>The stop's name once stopped (<c>minutes</c>, <c>level</c>, <c>request:&lt;why&gt;</c>, <c>logistics-stop:&lt;line&gt;</c>, <c>failed:&lt;state&gt;:&lt;why&gt;</c>, <see cref="UnscriptedDeathStop"/>, <see cref="MinutesBudgetStop"/> for a resumed checkpoint whose minutes are already used), else null.</summary>
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
    public AutopilotSeed Seed => new(_cycle, _cycles, SecondsUsed, _deathPending ? _deathTally : null, _tripPending && !_buyTripActive && !QuestHoldsNoTrip ? (_tripForced && !_tripCutWork ? JourneyPlans.TripForced : JourneyStations.TripByCounters) : null, _tripMean, _tripMeanCount, _taskDone, _tripPending && _tripCutWork, _options.Task.Name);

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
        BindVitae(session); // M6 6 F2: the run's vitae ledger (the cycle and done lines' vitae= vitae_steps=)
        BindWardrobe(session); // M6 16 E5e1: the level up job states its gear keeps' cast chance, once a session (AutopilotScenario.Wardrobe.cs)
        BindTrips(session); // M6 16 E5d: the session's one set of logistics counters, once a session (AutopilotScenario.Trips.cs)
        _clock = session.Clock.Now;
        _hostiles ??= new HostileWatch(session, _ground, _options.AggroReachMetres); // M5 7 F23
        ShadowHostiles(); // M6 16 E2a: the threat board in shadow beside the watch (report-only, AutopilotScenario.ThreatShadow.cs)
        _guard ??= new AutopilotVitalsGuard(_context.Spells, _ground, _options, (_options.Buff ?? new BuffOptions()).CastChance, _hostiles, _log) { Times = Vitals.CreatureAttackTimes.For(_context.Dats), Reserve = () => _state is AutopilotState.Travel or AutopilotState.Death ? _context.Facts.FightBackReserve : null, WalkTarget = () => _state is AutopilotState.Travel or AutopilotState.Death ? _context.Facts.FightBackTarget : null, LeaveRoad = RaceRoad }; // M6 14 retreat attackers: a leg's fight-back keeps the recall's mana (the travel legs run in these two states only); M6 14 the losing race: the leave a lost race is handed to
        _guard.Sample(session, _clock); // M6 14 the losing race: her health's trend and her damage a second are read every frame, whatever the state
        if (_end != ScenarioStatus.Running)
        {
            return _end;
        }

        if (_abandoned)
        {
            return ScenarioStatus.Running; // abandoned by its host: nothing runs; the host drops it
        }

        OrderWatch(); // the command registry: a taken order's check after (AutopilotScenario.Orders.cs)

        NoteStance(session);
        if (_options.Job == AutopilotJob.LevelUp)
        {
            SampleWalk(session); // M6 0 a4: the walking clock, cancelled legs included (m6-walk-cancel-seconds)
        }

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
            _worn.Snapshot(session.State); // M5 7 S2d (M6 6 F2: the run's own memory): the worn set while alive (the death's take-back and the audit wear it again)
        }

        BindWatch(); // M6 item 14: a respawn is judged against the proven lifestone; the tie follows what the server said
        _manaWatch.Step(session, _clock); // M5 7 S2b: each worn item's mana re-read every few minutes (ACE pushes none as it burns)
        AnswerUnasked(session); // M6 5 Q5: the server's unasked Yes/No (m5-6-headless-confirm-reply)
        LadderSpendTick(session); // M6 7 R: a mid-run spend's raises, in any state
        KillTaskWatch(session); // M6 14 guard task: the server's lines of the kill tasks (the counter, the giver's talk), in any state
        TrophyPickupWatch(session); // M6 audit: a tusk's first moment in the pack starts its quest's timer (AutopilotScenario.TrophyPickups.cs)
        VillaWatch(session); // M6 villa: the session's one HouseQuery while the status is unread, and the run's money at its start
        BuffBotCoverSync(session); // M6 item 14 S3: the plans read what the bot covers
        if (BodyApartStep(session) is { } apart)
        {
            return apart; // M6 7 AJ: the body and the server parted ways: the one re-login or the stop (AutopilotScenario.BodyApart.cs)
        }

        if (!_deathSeen && _phase is RunPhase.Known or RunPhase.Vitals or RunPhase.Peace or RunPhase.Running && _watchdogs.Over(_clock) is { } held)
        {
            return WatchdogTrip(held);
        }

        if (_checkpointReady && _clock - _lastWrite >= CheckpointRefreshSeconds)
        {
            WriteCheckpoint(_lastDecision, _lastDecisionAt, quiet: true);
        }

        if (_phase is RunPhase.Vitals or RunPhase.Peace or RunPhase.Running && PendingStopCut(session) is { } cut)
        {
            return cut; // M6 item 14: a pending stop request cuts a station at home at its safe point; away from home it ends at its own end
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
            RunPhase.LastResort => LastResortStep(session),
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
        _vitae.Dispose(); // M6 6 F2
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
        _context.BindQuestTimers(_characterGuid); // before any quest rule reads them: a character re-created under the same name does not inherit the old one's records
        LoadCheckpoint();
        if (_minutesRefusal is { } refusal)
        {
            return Stop(MinutesBudgetStop, ok: false, refusal); // M6 item 14: nothing is left of the minutes: a named failure at once, never a start and an ok stop one second later
        }

        BeginRestartReads(); // M6 item 14: the hand-over and the bind's proof, before the audit reads them
        _diedSubscription = session.Events.Subscribe<PlayerDied>(OnDied);
        _chatSubscription = session.Events.Subscribe<ChatReceived>(OnChat); // M5 7 S2b
        if (_deathTally is null)
        {
            _deathTally = new DeathTally();
            _deathTally.Start(session); // running from the start: a death the autopilot did not script is handled with the corpse's facts it saw
        } // else a seeded tally of a cut death (Seed): its counters are kept; the death station starts it

        if (!_deathPending && LadderClimb)
        {
            VitaeBurnTake("the run's start"); // M6 sprint: an owed corpse on this run's ground, its burn over: the known state enters the death station for it first
        }
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
        _deathAnsweredCut = false; // M6 7 U: the known state is where a cut after the death's answer was going
        _retreating = false; // M6 6 F2: the known state decides afresh
        _taskFightBack = false; // M6 6 FXM1 (6-M11): a task's fight-back that ended by a retreat, a death, a trip or a station failure is over too
        _urgentRecall = false;
        _lastResortKey = null; // M6 8 N: the known state is where a failed errand's recovery ends; the last resort is not armed past it
        _lastResortForLeave = false;
        KeeperForget(); // M6 7 BD: the known state decides afresh
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

        if (GuardRetreat(session) is { } retreat)
        {
            return retreat; // M6 6 F2 (f23-guard-nothing-paid): nothing payable under the line on the ground: the retreat
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

        if (OrphanProbe(session) is { } orphan)
        {
            return orphan; // M6 6 MH: after each login and each reconnect, one `where` before anything is classified; an orphaned login re-logs in once (AutopilotScenario.Connection.cs)
        }

        if (_audit is { } audit && AuditStep(session, audit) is { } decided)
        {
            return decided; // M5 7: the start-up audit's fix (the corpse, the bind, the trip), its stop, or its wait for the pack to read
        }

        if (_deathPending)
        {
            return ResumeDeath(); // a death station that was cut: the corpse is still to be recovered, wherever the mage stands (4-R2)
        }

        if (OrderAtKnownState(session) is { } ordered)
        {
            return ordered; // the command registry: an order taken at the known state's safe point (AutopilotScenario.Orders.cs)
        }

        if (SeatAtKnownState(session))
        {
            return ScenarioStatus.Running; // the dial re-seats straight after this step (AutopilotScenario.Steer.cs)
        }

        if (_options.Task.Kind != AutopilotTaskKind.Job && TaskArrow(session) is { } task)
        {
            return task; // M6 6 MB: a one-shot task inside the install job (a quest from home, an errand) decides here (AutopilotScenario.Jobs.cs); null: the place's own arrow (a death; away from home and the ground the recall home first, FXM1 6-M5)
        }

        if (_options.Job == AutopilotJob.LevelUp && _options.Task.Kind == AutopilotTaskKind.Job && _ladderCycle != _cycle && !StopRequested && LadderCycleStart(session) is { } ladderStop)
        {
            return ladderStop; // M6 0 a4: the rung is re-read at each cycle's start; a quest, pending or refused rung, the move-on and the top stop here by name
        }

        var place = Classify(session.State, _ground);
        _log.Info(LogCategory, $"autopilot known-state place={place.ToString().ToLowerInvariant()} ({Cell(session)})");
        var finished = _finished; // M5 7 P7n (D145): the audit that finished this frame decides the ground's arrow and gets its chat summary
        _finished = null;
        switch (place)
        {
            case AutopilotPlace.Home:
                _recalledHome = false;
                if (RebindOwed() is { } rebind)
                {
                    return rebind; // M6 item 14: a respawn proved the bind wrong: bound again before anything else
                }

                if (_tripPending)
                {
                    Announce(finished, "the cut trip runs again from its start");
                    return ResumeTrip();
                }

                if (BuffBotDue(session) is { } botWhy)
                {
                    Announce(finished, $"the buff bot first, then her own pass and the way to {_ground.Name}");
                    return BeginRun(AutopilotState.Buff, BuffBotSpec, botWhy); // M6 item 14: the stop at the buff bot (AutopilotScenario.BuffBot.cs)
                }

                if (HandOverPassStands(session) is { } stands)
                {
                    Announce(finished, $"the buffs of the run before still stand, so straight to {_ground.Name}");
                    return BeginTravelOut(stands); // M6 item 14: the ended run's pass stands: no second pass seconds after it
                }

                Announce(finished, $"buffing, then the way to {_ground.Name}");
                return BeginRun(AutopilotState.Buff, BuffSpec, "home");
            case AutopilotPlace.Ground:
                _recalledHome = false;
                if (finished?.Attacker is { } attacker)
                {
                    Announce(finished, "fighting back from where it stands");
                    return BeginRun(AutopilotState.Fight, FightSpec, $"audit: a hostile is on it ({attacker}): the fight fights back first (D88)");
                }

                if (_tripPending)
                {
                    Announce(finished, "the cut trip runs again from its start");
                    return ResumeTrip();
                }

                _ = HandOverPassStands(session); // M6 item 14: a handed-over pass that stands is this run's pass too (the rule below reads the book it stood on)
                if (_rung is not null && (_passHomeWalked ? "the pass she walked home for" : LearnedPassRule.Why(_passBook, self.Spellbook.Count, CasterAuraMemory.Shared.Owed)) is { } passWhy)
                {
                    // M6 7 AO: on the outskirts the trip ends in the ground, so "home" never comes; the whole pass (its bootstrap first) runs here, before the fight, by name
                    _log.Info(LogCategory, $"ladder: pass: check before: {passWhy}: the whole pass before the fight");
                    if (PassHomeFirst(session, passWhy) is { } home)
                    {
                        return home; // M6 7 AW: a full pass with a creature near her walks home first
                    }

                    Announce(finished, "the whole buff pass, then the fight");
                    return BeginRun(AutopilotState.Buff, GroundBuffSpec(start: LearnedPassRule.IsStartPass(passWhy)), passWhy); // M6 14 (m6-caster): a caster swap's pass casts the due lines, as a changed book's
                }

                if (finished is { Buffs: "buff" } || self.Qualities.Enchantments.IsDefaultOrEmpty)
                {
                    if (PassHomeFirst(session, "the audit's lines") is { } home)
                    {
                        return home; // M6 7 AW
                    }

                    Announce(finished, "buffing, then back to the fight from where it stands");
                    return BeginRun(AutopilotState.Buff, GroundBuffSpec(), finished is { BuffsDue: > 0 } due ? string.Create(CultureInfo.InvariantCulture, $"audit: {due.BuffsDue} buff line(s) due in the ground") : "in the ground with no enchantment up");
                }

                if (finished is { HealFirst: true })
                {
                    Announce(finished, "healing, then back to the fight from where it stands");
                    _upkeepDetail = "the audit found the health under the floor";
                    return BeginHeal("audit: the health is under the floor");
                }

                if (StartQuestNow("in the ground at the run's start") is { } startQuest)
                {
                    return startQuest; // M6 14 the start quest: a run that starts on the ground takes the errand before its first fight (its go walks home first)
                }

                Announce(finished, "back to the fight from where it stands");
                return BeginRun(AutopilotState.Fight, FightSpec, "in the ground");
            case AutopilotPlace.Dead:
                return UnscriptedDeath();
            default:
                if (_recalledHome)
                {
                    return Stop("failed:known-state:elsewhere", ok: false, "the recall home did not bring the mage home or to the ground");
                }

                _recalledHome = true;
                return QuestErrand(QuestThen.Home, forced: false, "elsewhere") ?? BeginRun(AutopilotState.Travel, HomeSpec(), "elsewhere"); // M6 4 t3b: below Lifestone Recall, home by portals from a routed place; M6 5 Q5 (D58): a held due trophy is turned in first
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

        // M6 8 (the long round): the travel file's home place counts too, as the rung change's own rule does (AutopilotTravelRule.Input): the Redoubt's Surface portal lands in 0xAAB1, a landblock of that place, and a route that ended "home" there was stopped as lost.
        var routes = TravelRoutes.Default;
        return at.CellId >> 16 == ground.Home.LifestoneCell >> 16 || JourneyGrounds.At(StationWhere.Holtburg, at) || routes.PlaceOf(RungRoute.Hex(at.CellId >> 16))?.Key == routes.StartPlace ? AutopilotPlace.Home : AutopilotPlace.Elsewhere;
    }

    // ------------------------------------------------------------------------------------------------ the station runs

    private ScenarioStatus BeginRun(AutopilotState state, StationSpec spec, string why, bool resume = false)
    {
        if (ReferenceEquals(spec, RungHomeSpec) && _session is { } walker && WalkOutIsDeadly(walker, out var deadly) && LastResortNoWayHome(walker, spec.Name, deadly) is { } instead)
        {
            return instead; // M6 8: no way home works before the walk begins: the last resort, not the walk
        }

        KeeperForget(); // M6 7 BD: a run that begins owes nothing to a fight-back before it
        if (state == AutopilotState.Death)
        {
            _trips?.Void(); // M6 16 E5d: a trip called before she died is not the trip made after: what it read (the pack, the purse) is the corpse's now
        }

        (_urgentRecall, _urgentAsked) = (_urgentAsked, false);
        if (state == AutopilotState.Fight)
        {
            _fightPassSeen = 0; // M5 7 F29: a new fight counts its line passes from 0
            if (ReferenceEquals(spec, FightSpec) && InGround())
            {
                spec = FightResumeSpec; // M5 7 F30: on the ground already, the fight starts from where the mage stands
            }

            spec = LadderFightSpec(spec); // M6 0 a5: the level up job's bolt cap (JourneyPlans.MaxLevelArgument)
            _fightBack = FightBackNow(why);
            if (_fightBack)
            {
                spec = spec with { NeedsOverride = new StationNeeds(StationWhere.Den) }; // M6 7 BB: no health, mana or buff need before the fight: its own emergency handles them
            }
        }
        else
        {
            _fightBack = false;
        }

        if (StopDue() is { } stop && (!resume || PendingStopHoldsResume(stop, state)))
        {
            // M6 item 14: under a pending stop request a cut trip or a resumed state is not begun again either (only a corpse owed and the way home before the stop are)
            return Stop(stop, ok: true, resume ? $"a stop request is pending: the {spec.Name} station is not begun again" : $"a stop rule is due before the {spec.Name} station");
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
                _tripForced = spec.Arguments.TryGetValue(JourneyPlans.TripArgument, out var trip) && trip is JourneyPlans.TripForced or JourneyPlans.TripErrandRoom; // M6 14 sell first: the errand's sale is forced too
                _tripCutWork = false;
            }
        }

        _retreatHeal = false; // M6 6 F2: a station after the retreat's pass at home ends it
        _retreatForMana = false; // M6 7 BE: a stop that ended some other way (a death) was not the way out's
        if (state != AutopilotState.Travel)
        {
            _retreating = false; // M6 6 F2: only the way home carries the retreat
        }

        SetState(state, why);
        _phaseStart = _clock;
        return StartPhases(skipKeeper: (state == AutopilotState.Death && _unscripted) || (_retreating && state == AutopilotState.Travel) || _fightBack || _urgentRecall); // M6 6 F2: the retreat goes home before any keeper's pass; M6 7 BB: so does a fight with a hostile on her; M6 7 BD: and the urgent recall
    }

    /// <summary>The fight starts at once, no keeper's pass or health, mana or buff need before it, when a hostile is attacking her or in reach in the ground (D88; the fight's own emergency drain, dance and heal run under fire). Named in the log.</summary>
    private bool _fightBack;

    private bool FightBackNow(string why)
    {
        if (_hostiles is not { } watch || !InGround() || !watch.InReach(_clock, out var hostile))
        {
            return false;
        }

        _log.Info(LogCategory, $"autopilot fight-back: a hostile is on her or in reach ({hostile}): the fight starts at once with no keeper's vitals pass and no health, mana or buff need first; its own emergency handles them (M6 7 BB; entry: {why})");
        return true;
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
        if (_healOnly && !_retreatHeal && _state == AutopilotState.Upkeep && RetreatDue(session, "upkeep heal"))
        {
            NoteWhy(RetreatWhy);
            return TakeRetreat(_retreatWhy); // M6 6 F2: the keeper's pass in place cannot outheal what comes in: home
        }

        if (KeeperStands(session, now) is { } stands)
        {
            return stands; // M6 7 BD: the one rule: a hostile on her: the keeper stops, she fights back, the state resumes
        }

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
        if (KeeperStands(session, session.Clock.Now) is { } stands)
        {
            return stands; // M6 7 BD: the one rule, in the last of the keeper's phases
        }

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
            if (_healOnly && !_deathSeen && HealRetreat(session) is { } retreat)
            {
                return retreat; // M6 6 F2: the keeper's pass in place ended under the retreat line: the rule decides (home), else the heal's own failure below
            }

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
            return DenRunDied() ?? UnscriptedDeath(); // a death in any state: the station in progress is ended here (the real fight ends itself on it too); M6 item 14: a death on the den run ends the run, with no death station
        }

        if (ReferenceEquals(_spec, DenRunSpec))
        {
            DenRunFrame(session); // M6 item 14: where she last stood alive, and the Travel state's progress
        }

        if (_state != AutopilotState.Fight && _questTripActive)
        {
            WatchErrandFight(session); // M6 14 errand lapse: the quest station's own fight keeps her lines as every fight does, and her own lines cover the bot's
            if (OrderAtErrandLull(session) is { } cut)
            {
                return cut; // the command registry: a trip order cuts the errand in its fight's lull; the errand runs again from what she holds (AutopilotScenario.Orders.cs)
            }
        }

        if (_state == AutopilotState.Fight)
        {
            WatchFight(session);
            LadderMidRunSpend(session); // M6 7 R: the spend soon after a level-up, the fight running
        }
        else if (SubFightKeepingHerBars() is { } kept)
        {
            if (SubFightWatch(session, kept) is { } left)
            {
                return left; // M6 16 E3.1: a quest's sub-fight keeps her bars as a ground fight does (no heal pick of the guard, no wait of the layer); what the outer loop asks of it instead is AutopilotScenario.SubFightWatch.cs's
            }
        }
        else if (SurvivalFrame(session, now) is { } survival)
        {
            return survival; // M6 8 K: the survival layer: a wait: the station is not stepped this frame and resumes where it stopped; a leave: it has ended by name and she goes home
        }
        else if (StationWaitStands(session) is { } stands)
        {
            return stands; // M6 7 BD: the one rule, in a station's wait: it ends, she fights back, it runs again
        }

        if ((_state == AutopilotState.Travel && IsRung(_spec)) || _state == AutopilotState.Trip || _state == AutopilotState.Death)
        {
            // M6 4 t3b: a rung change that moves is working. M6 4 F1 (4-R7): so is a trip (logistics, buy-trip): its ways home and back are rung changes since F1 (the island's runs: 609 m, 1,506 m) and the buy-trip's hops and walks
            // are minutes, while its station's own limit (3 x 600 + 300 = 2,100 s, 3 x 900 + 300 = 3,000 s) is past the Trip state's 900 s bound; a trip that stands still still trips it.
            // M6 7 AG (gate m6-7-g: cut at 900 s of the state, 9.7 m from the corpse, the trip working): so is a corpse trip: the handler's fights and walks each have their own limits, so the bound is for a body that stands still, not for their sum.
            NoteTravelProgress(session);
            NoteStationWorking(); // M6 14 entry recharge: so is a recharge that fills the bars where she stands
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
        if (ok)
        {
            (_lastFailure, _failureRepeats) = (null, 0); // M6 7 I1: a station passed: the next failure is a first
        }

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
        OrderStationEnded(ok, why); // the command registry: a buff order's check after (AutopilotScenario.Orders.cs)
        if (ReferenceEquals(_spec, DenRunSpec))
        {
            return DenRunEnded(ok, why, fields); // M6 item 14: the den run's station: the stop by name, a death included (never the death station)
        }

        switch (_state)
        {
            case AutopilotState.Buff:
                if (_deathSeen)
                {
                    return UnscriptedDeath();
                }

                if (ReferenceEquals(_spec, BuffBotSpec))
                {
                    return BuffBotEnded(ok, why, fields); // M6 item 14: her own pass follows the stop at the bot, landed or failed
                }

                if (!ok)
                {
                    return StationFailed("failed:buff:" + JourneyVerdict.Token(why ?? "failed"), "the buff station failed", why);
                }

                if (GateRecastEnded())
                {
                    return EnterKnown("the gate recast's pass ended: the cycle's check reads the gates again (check after; M6 14 gate lapse)");
                }

                _botFailedThisVisit = false; // her own pass ended: the next time home asks the bot again
                _passEndedAt = _clock; // M6 item 14: the hand-over's pass (the next run in this session reads how long ago it ended)
                _passBookAny = _session?.State.Self.Spellbook.Count;
                CasterAuraMemory.Shared.Clear(); // M6 14 (m6-caster): a pass ended: a swap's owed aura lines stood on it

                if (_rung is not null && _session is { } passed)
                {
                    _passBook = passed.State.Self.Spellbook.Count; // M6 7 AO: the book this pass stood on
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: pass: check after: a whole pass ended on a book of {_passBook} spells"));
                }

                var walkedHome = _passHomeWalked;
                _passHomeWalked = false;
                if (!walkedHome && InGround() && _session is { } cut && PassHomeFirst(cut, "after the pass") is { } rest)
                {
                    return rest; // M6 7 AW: a pass cut by a creature near her with many lines left ends at home
                }

                if (DenRunOrdered && _denTripDone && !_denPassDone)
                {
                    return DenRunAfterPass(); // M6 item 14: the den run's pass ends in the known state, which checks and begins the run
                }

                return InGround() ? StartQuestNow("buffed in the ground at the run's start") ?? BeginRun(AutopilotState.Fight, FightSpec, "buffed in the ground") : BeginTravelOut("buffed"); // M6 14 the start quest before the first fight // M6 4 t3b: den-travel or the rung change (AutopilotTravelRule.Out)

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

                if (!ok && ReferenceEquals(_spec, RecallHomeSpec) && _context.Facts.LifestoneRecallUnavailable && ReferenceEquals(HomeSpec(), RungHomeSpec))
                {
                    // M6 7 AV (live, 2026-11-06): a recall refused for components neither repeats nor stops the run: the recall is not available this run (recall-unavailable:components) and the way home is the route by portals or foot (the rule is read again, without it)
                    _log.Warn(LogCategory, $"autopilot: recall-unavailable:components: Lifestone Recall was refused for a missing component; the way home is taken without it (the next trip buys the components): {why}");
                    return BeginRun(AutopilotState.Travel, RungHomeSpec, "recall-unavailable:components: the way home by portals or on foot");
                }

                if (!ok && ReferenceEquals(_spec, DenTravelSpec) && PortalUntied(why))
                {
                    // M6 4 t3b: the tie is now known untied: the rule is read again; a route of the travel file reaches the ground by the rung change, else the named gap (TODO(m5-portal-retie-route), narrowed)
                    var again = DecideOut();
                    return again.Way == TravelWay.RungChange
                        ? BeginRun(AutopilotState.Travel, RungOutSpec, $"den-travel was refused untied: the rung change's route {again.Route}")
                        : PortalGap($"the den travel's Primary Portal Recall was refused untied: {why}");
                }

                if (!ok && LastResortOnTravelFailed(why) is { } lastResort)
                {
                    return lastResort; // M6 8 N (D120): a failed ladder errand's way home failed: /lifestone, once
                }

                if (IsRung(_spec) && RungEnded(ok, why, fields) is { } rung)
                {
                    return rung; // M6 4 t3b: the station's own named failure, or the check after it
                }

                if (ok && ReferenceEquals(_spec, RecallHomeSpec))
                {
                    _bindHow ??= "a Lifestone Recall that landed this session";
                }
                else if (ok && ReferenceEquals(_spec, BindSpec))
                {
                    _bindHow = "the bind station this session";
                    NoteBindStation(fields); // M6 item 14: the proof is kept (the checkpoint, the hand-over), so no later start walks to the lifestone again
                }
                else if (ok && ReferenceEquals(_spec, DenTravelSpec))
                {
                    _portalProven = true;
                }

                if (!ok)
                {
                    NoteManaRecallFailed(why); // M6 7 BE
                    return StationFailed($"failed:travel:{_spec!.Name}:{JourneyVerdict.Token(why ?? "failed")}", "the travel station failed", why);
                }

                if (ReferenceEquals(_spec, BindSpec))
                {
                    return EnterKnown("bound at the home lifestone");
                }

                if (_retreating && (ReferenceEquals(_spec, RecallHomeSpec) || ReferenceEquals(_spec, RungHomeSpec)))
                {
                    return RetreatHome(ReferenceEquals(_spec, RecallHomeSpec) ? "recalled" : "by the rung change"); // M6 6 F2: the keeper's pass at home, then the known state
                }

                return ReferenceEquals(_spec, RecallHomeSpec) ? EnterKnown("recalled home") : ReferenceEquals(_spec, RungHomeSpec) ? EnterKnown("home by the rung change") : BeginRun(AutopilotState.Fight, FightSpec, "in the ground");

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
    /// recovery brings the kit back), the place, the caster in the hands (M6 6 MF: the bind's tie is a cast), the bind, the portal tie, the kit, then the buffs.
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
            Notice(audit, "its corpse is not recovered yet: the death path first (the corpse, then the fight)");
            return ResumeDeath();
        }

        // M5 7 P7c (D139 row h): a wait resumes at its own step; the checks before it ran once this pass and are not run (nor logged) again each frame
        var resume = audit.WaitingOn;
        if (resume is null && AuditChecks(session, audit) is { } decided)
        {
            return decided.Status;
        }

        // M6 6 MF (m6-climber-start-weapon): a caster in the hands before the bind and the portal tie. The bind station's Lifestone Tie is a cast, and item 2's live window had it refused once with a weapon wielded (the NoCasterHeld
        // recovery then wielded the Training Wand); now the caster check acts first, checked after, and the bind casts with a caster held. Its wait resumes here; the bind and the tie run once the caster is in.
        if (resume is null or StartupAudit.WaitCaster)
        {
            if (!audit.CasterDone && CasterStep(session, audit) is { } caster)
            {
                return caster;
            }

            if (TieChecks(audit) is { } tied)
            {
                return tied.Status;
            }
        }

        return AuditTail(session, audit, resume);
    }

    /// <summary>The audit's checks from the top (M5 7): the corpses in view, alive, the place, the hostiles. A decision ends the frame (its status, null for the known state's own arrows); null goes on to the caster, then the bind and the tie.</summary>
    private AuditDecision? AuditChecks(IGameSessionView session, StartupAudit audit)
    {
        var state = session.State;
        audit.Evaluations++;
        audit.BeginPass(); // M5 7 P7n: the per-pass steps (the windows, the kit, the caster, the stance) run again on a pass from the top

        if (!audit.CorpsesLooked)
        {
            audit.CorpsesLooked = true;
            var seen = CorpsesInView(state);
            _log.Info(LogCategory, StartupAudit.Line("corpse", seen.Count == 0 ? "no corpse of mine is in the death checkpoint or in view" : $"no death in the checkpoint; {seen.Count} corpse(s) of mine in view ({string.Join(',', seen)}): report-only by rule: the audit recovers only the corpse its death checkpoint names; a corpse of mine in view with no death in the checkpoint is an earlier death's, recovered or emptied, and looks the same (the den keeps them), so walking to it would be a guess (m5-audit-corpse-in-view, closed as designed)", "none"));
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
            var fix = place == AutopilotPlace.Dead ? "the death path" : _recalledHome ? "none: the recall home already ran (the known state stops by name)" : "recall home first (the vitals pass before it dances for the mana when it is short): no route walks back from an unknown place, so it never guesses one";
            _log.Info(LogCategory, StartupAudit.Line("place", $"{audit.Place} ({Cell(session)})", fix));
            if (!audit.PlaceNoticed)
            {
                audit.PlaceNoticed = true;
                Notice(audit, place == AutopilotPlace.Dead
                    ? "it is dead: the death path first (the respawn, then its corpse)"
                    : $"it is neither in {_ground.Home.Town} nor in {_ground.Name} ({Cell(session)}): no route from here, so it recalls home, then takes the way back");
                audit.Note(place == AutopilotPlace.Dead ? "it was dead: the death path ran" : "it was away from home and the den: recalled home");
            }

            return new AuditDecision(null); // the known state's own arrows; after the recall home the audit runs again from the top
        }

        _log.Info(LogCategory, StartupAudit.Line("place", $"{audit.Place} ({Cell(session)}{ArrivalDistance(state)})", place == AutopilotPlace.Ground ? "none: the fight resumes from where it stands and names each walk it makes (D47: a target seen, a spawn due, else it holds)" : "none: the buff, then the den travel"));

        // 1b (P7n, D145) hostiles in reach, and whether something is attacking it: then the slow fixes wait and the fight fights back first (D88: always fight back an attacker)
        if (_hostiles is { } watch && watch.InReach(_clock, out var hostile))
        {
            audit.Hostiles = _clock - watch.LastHitAt < HostileWatch.AttackQuietSeconds ? "attacked" : "in-reach";
            audit.Attacker = place == AutopilotPlace.Ground ? hostile : null;
            _log.Info(LogCategory, StartupAudit.Line("hostiles", $"{audit.Hostiles}: {hostile}", place == AutopilotPlace.Ground ? "self-defence first (D88): the fight fights back before any slow fix (the trip, the pack sort, the worn set, the buff pass wait)" : "none: not in the ground"));
            if (audit.Attacker is not null)
            {
                audit.Note("a hostile on it: fighting back first");
            }
        }
        else
        {
            audit.Hostiles = "clear";
            audit.Attacker = null;
            _log.Info(LogCategory, StartupAudit.Line("hostiles", string.Create(CultureInfo.InvariantCulture, $"clear: no attack within {HostileWatch.AttackQuietSeconds:0} s, no target creature within {_hostiles?.ReachMetres ?? 0:0.#} m"), "none"));
        }

        return null;
    }

    /// <summary>The audit's bind and portal tie (M5 7), after the caster check (M6 6 MF, <c>m6-climber-start-weapon</c>): a decision ends the frame; null goes on to the kit. The place check before them left home or the ground.</summary>
    private AuditDecision? TieChecks(StartupAudit audit)
    {
        var place = audit.Place == "home" ? AutopilotPlace.Home : AutopilotPlace.Ground;

        // 2 the lifestone bind
        var facts = _context.Facts;
        var kept = BindProofNow(); // M6 item 14: the proof a start carried (the checkpoint's by the guid, or the hand-over's), checked against this ground's home lifestone before the bind is skipped
        if (kept.Proven && _bindProofFrom != ThisRun)
        {
            audit.Bind = audit.BindTried ? "bound-now" : "proven";
            _log.Info(LogCategory, StartupAudit.Line("bind", $"proven by the proof carried from {_bindProofFrom}: {kept.Text}", "none: no walk to the lifestone (a respawn away from it, another character or a bind elsewhere unproves it)"));
        }
        else if (_bindHow is not null || (facts.LifestoneTied == true && !_tieFromProof))
        {
            audit.Bind = audit.BindTried ? "bound-now" : "proven";
            _log.Info(LogCategory, StartupAudit.Line("bind", "proven by " + (_bindHow ?? "a recall that landed or a Lifestone Tie this session (the journey's facts)"), "none"));
        }
        else if (place == AutopilotPlace.Home && !audit.BindTried)
        {
            audit.BindTried = true;
            _boundOnce = true; // the F14 bind on a 0x049E refusal does not bind a second time after this one
            var finding = (facts.LifestoneTied == false
                ? $"disproven: a Lifestone Recall was refused with {NoLifestoneRefusal}"
                : "not proven this session (ACE sends no lifestone-bound quality: its player description carries only the LastOutsideDeath position)")
                + $"; no kept proof stands ({kept.Reason}): {kept.Text}";
            _log.Info(LogCategory, StartupAudit.Line("bind", finding, $"bind at the {_ground.Home.Town} lifestone (Use, then Lifestone Tie: harmless when already bound)"));
            Notice(audit, $"its lifestone bind is not proven this session: it binds at the {_ground.Home.Town} lifestone first");
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
            // M6 4 t3b: the same rule as the Travel state (AutopilotTravelRule.Out): a route of the travel file reaches the untied ground by the rung change; only a ground no route reaches stops by the gap
            var untied = $"ACE refused a Primary Portal Recall untied ({StartupAudit.NoPortalTieRefusal}) this session";
            if (DecideOut(quiet: true) is { Way: TravelWay.RungChange } routed)
            {
                audit.PortalTie = "route";
                _log.Info(LogCategory, StartupAudit.Line("portal_tie", untied, $"none: the rung-change station takes the climber ({routed.Reason}); it ties a tieable entry on the way"));
            }
            else
            {
                return new AuditDecision(PortalGap(untied));
            }
        }
        else
        {
            audit.PortalTie = "pending";
            _log.Info(LogCategory, StartupAudit.Line("portal_tie", "not proven this session (ACE sends no portal-tie property: LinkedPortalOne is not SendOnLogin)", $"none: the travel proves it ({DecideOut(quiet: true).Reason}); a {StartupAudit.NoPortalTieRefusal} refusal turns the travel to the rung change's route, or stops by the named gap TODO(m5-portal-retie-route) where no route reaches the portal"));
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
            if (_tripPending && !_questTripActive) // M6 6 FXM2 (6-M13): the quest task's own trip (BeginRun Trip for the station) is no cut logistics trip
            {
                audit.Kit = "trip";
                audit.KitDone = true;
                _log.Info(LogCategory, StartupAudit.Line("kit", "a trip that was cut is still to be made", audit.Attacker is null
                    ? "the trip again from its start: the logistics station reads its counters again (a trip cut after its sales buys only what is still short) and plans its walk from where it stands; a half-made walk or vendor visit is not resumable"
                    : "deferred: a hostile is on it: the fight fights back first (D88), the cut trip stays due for the next known state"));
                audit.Note(audit.Attacker is null ? "the cut trip runs again from its start" : "the cut trip waits until the fight is clear");
            }
            else
            {
                var kit = StartupAudit.ReadKit(state, _ground, TripReader, _options.LootOn, saleMade: audit.TripTried); // 2-R12: before the audit's trip a purse-short floor waits for the trip's sale
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

                    // M6 6 MF (m5-audit-kit-unread): the re-ask, once: one more read window, logged once (StartupAudit.WaitKitReask names why no request to the server can make it send the lists again)
                    if (_clock - audit.StartedAt < 2 * StartupAudit.KitReadSeconds)
                    {
                        if (audit.BeginWait(StartupAudit.WaitKitReask, _clock))
                        {
                            _log.Info(LogCategory, StartupAudit.WaitLine(StartupAudit.WaitKitReask, string.Create(CultureInfo.InvariantCulture, $"the pack did not read within {StartupAudit.KitReadSeconds:0} s ({kit.Verdict.Reason}): re-asked once: ACE sends the pack's lists and the held items' descriptions only at login and a Use of an owned pack opens nothing, so the re-ask is one more read of the client's model, at most {StartupAudit.KitReadSeconds:0} s more")));
                        }

                        return ScenarioStatus.Running;
                    }

                    audit.EndWait();
                    audit.Kit = "unread";
                    audit.Note("the pack did not read (re-asked once)");
                    _log.Warn(LogCategory, StartupAudit.Line("kit", string.Create(CultureInfo.InvariantCulture, $"the pack did not read within {StartupAudit.KitReadSeconds:0} s nor in the re-ask's {StartupAudit.KitReadSeconds:0} s more ({kit.Verdict.Reason}): named unread (m5-audit-kit-unread)"), "none: the fight's counters judge the stock once it reads"));
                }
                else
                {
                    if (audit.WaitingOn == StartupAudit.WaitKitReask)
                    {
                        _log.Info(LogCategory, StartupAudit.Line("kit", string.Create(CultureInfo.InvariantCulture, $"the pack read in the re-ask's window ({_clock - audit.WaitStartedAt:0.#} s into it)"), "none: the check runs on it"));
                    }

                    audit.EndWait();
                    NoteFloors(); // M5 7 P7s (D148): a carried line already at its floor is said once, and the run goes on
                    var missing = KitMissingText(kit);
                    if (kit.CarriedShort.Count > 0)
                    {
                        var carriedStop = "logistics-stop:" + JourneyVerdict.Token(kit.Verdict.Trigger);
                        if (BuyTripCallFor(state, audit, carriedStop, kit.CarriedShort) is { } buyTrip)
                        {
                            audit.Kit = "buy-trip"; // KitDone stays false: the audit reads the pack again when the station ends (check after)
                            Notice(audit, $"a carried line is under its stop line ({string.Join(',', kit.CarriedShort)}): the buy-trip station goes to its seller");
                            audit.Note("made the buy-trip to the seller of a carried line");
                            return buyTrip;
                        }

                        audit.Kit = "gap";
                        _log.Error(LogCategory, StartupAudit.Line("kit", $"carried lines under their stop line: {string.Join(',', kit.CarriedShort)} (no {_ground.Home.Town} vendor sells them; the buy-trip station was not queued: {_buyTripWhy}){missing}", "stop: the run cannot fight past the counters' stop line"));
                        return AuditStop(carriedStop, $"a carried line is under its stop line ({string.Join(',', kit.CarriedShort)}) and no vendor of {_ground.Home.Town} sells it; the buy-trip: {_buyTripWhy}");
                    }

                    if (kit.FociMissing.Count > 0)
                    {
                        // M6 item 0 F2 (belt's finding): the casts assume the starter foci (the foci formula); without one ACE casts the account's full formula under stock lines planned for the foci. A stop by name, never a silent guess.
                        audit.Kit = "gap";
                        _log.Error(LogCategory, StartupAudit.Line("kit", $"the foci the casts assume are not in the pack: {string.Join(',', kit.FociMissing)} (ACE casts that school on the account's full formula without them; TODO(m6-account-formula-plan)){missing}", "stop: m6-foci-missing"));
                        return AuditStop("m6-foci-missing", $"the pack lacks the foci the casts assume ({string.Join(',', kit.FociMissing)}): the formula under the stock lines would change (TODO(m6-account-formula-plan))");
                    }

                    if (kit.FloorNote is { } floorNote)
                    {
                        _log.Warn(LogCategory, StartupAudit.Line("floor", floorNote, "none"));
                    }

                    if (kit.TripDue && !audit.TripTried && audit.Attacker is null)
                    {
                        audit.TripTried = true;
                        audit.Kit = "trip";
                        var stockDue = kit.Short.Count + kit.Low.Count > 0 || kit.Verdict.IsTrip;
                        var what = kit.Short.Count + kit.Low.Count > 0 ? string.Join(',', kit.Short.Concat(kit.Low)) : kit.Verdict.Reason;
                        if (kit.FloorShort is { } floorShort)
                        {
                            // M6 2 e: the trip buys the floor only on a loot trip (ResupplyOptions Gear = --fight-loot, slice b): with loot off the trip buys the stock and the floor stays short, said after it (below), never silent.
                            // Only the floor is short: the counters would skip the trip (no-trip-due), so it is forced.
                            _log.Info(LogCategory, StartupAudit.Line("floor", floorShort, "a trip to " + _ground.Home.Town + " first (the floor is bought on a loot trip: --fight-loot=on; with loot off the trip leaves it short and the audit says so)"));
                            what = stockDue ? what + "; the gear floor is short" : "the gear floor is short (" + floorShort + ")";
                        }

                        _log.Info(LogCategory, StartupAudit.Line("kit", $"short: {what}{missing}", $"a trip to {_ground.Home.Town} first ({(kit.FloorShort is not null && !stockDue ? "forced: only the gear floor is short, the counters would skip it" : "by its counters")})"));
                        HandOverKitLine($"stale: {what}: the trip");
                        Notice(audit, $"a supply trip is due ({what}): a trip to {_ground.Home.Town} first");
                        audit.Note($"made a supply trip to {_ground.Home.Town}");
                        return BeginRun(AutopilotState.Trip, TripSpec(forced: kit.FloorShort is not null && !stockDue), "audit: supplies short (" + what + ")");
                    }

                    if (kit.TripDue && audit.Attacker is not null)
                    {
                        audit.Kit = "trip-deferred";
                        _log.Info(LogCategory, StartupAudit.Line("kit", $"a trip is due: {string.Join(',', kit.Short.Concat(kit.Low))} {kit.Verdict.Reason}{missing}", "deferred: a hostile is on it: the fight fights back first (D88) and its counters call the trip"));
                        audit.Note("a supply trip waits until the fight is clear");
                    }
                    else if (kit.TripDue)
                    {
                        audit.Kit = "gap";
                        // M6 6 MF (m5-audit-restock, closed as designed): the audit makes one trip; what is still short after it is handed to the fight's counters by design (their trip line calls the next trip, their stop line stops the run by name)
                        _log.Warn(LogCategory, StartupAudit.Line("kit", $"still short after the audit's trip: {string.Join(',', kit.Short.Concat(kit.Low))} {kit.Verdict.Reason}{(kit.FloorShort is null ? string.Empty : "; " + kit.FloorShort + " (the floor is bought only on a loot trip: --fight-loot=on; TODO(m6-floor-needs-loot) when loot is off)")}{missing}", "none: the designed hand-off to the counters: the audit makes one trip, and the fight's counters judge what is still short (their trip line calls the next trip, their stop line stops the run by name)"));
                    }
                    else
                    {
                        audit.Kit = kit.Missing.Count == 0 ? "ok" : "gap";
                        _log.Info(LogCategory, StartupAudit.Line("kit", $"every stock line at or over its stop and trip lines{(kit.Missing.Count == 0 ? "; the kit and the foci are held" : missing)}", "none"));
                        HandOverKitLine("every stock line still stands at or over its trip line: no trip");
                    }
                }

                audit.KitDone = true;
            }
        }

        // 5b the packs (M5 7 D121): what the audit finds misplaced goes to the pack of its job, logged, once an audit (P7n: deferred while a hostile is on it)
        if (audit.Packs == "pending" && audit.Kit != "unread")
        {
            if (audit.Attacker is not null)
            {
                audit.Packs = "deferred";
                _log.Info(LogCategory, StartupAudit.Line("packs", "not sorted: a hostile is on it", "deferred: the fight fights back first (D88); the next audit or trip sorts them"));
            }
            else
            {
                _packSorter ??= new PackSorter(new PackRules(_ground), g => CantripRoom.IsGreatPiece(session.State, g, _context.Spells), PackMemory.Shared.Rotation.Contains, _log, LogCategory, "the start-up audit"); // the review pack: great-cantrip pieces only (the owner, 2026-11-16)
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

        // 5c the worn set (M5 7 S2d): the remembered pieces, the clothing whose coverage is free, the cantrip wearables and the best caster held, once an audit (P7n: deferred while a hostile is on it)
        if (audit.WornRestored < 0 && audit.Kit != "unread")
        {
            if (audit.Attacker is not null)
            {
                audit.WornRestored = 0;
                _log.Info(LogCategory, StartupAudit.Line("worn", string.Create(CultureInfo.InvariantCulture, $"{state.Self.Inventory.Equipped.Count} piece(s) worn now; not restored: a hostile is on it"), "deferred: the fight fights back first (D88); the caster check before the bind wielded a caster"));
            }
            else
            {
                _wornRestorer ??= new WornRestorer(_ground, state.Self.Qualities.Level, _context.Spells, _log, LogCategory, "the start-up audit", _worn); // M6 6 F2: the run's own worn memory
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

        // 5d (M6 6 MF, 2-R12) the gear floor's no-caster stop, judged after the kit step and its trip (the caster held was checked before the bind: m6-climber-start-weapon)
        if (!audit.FloorDone && FloorStep(state, audit) is { } floor)
        {
            return floor;
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
        _lastKit = audit.Kit; // M6 item 14: the hand-over's kit finding
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
    /// The ground's portal is not tied and no route of the travel file reaches it (M5 7 audit; narrowed by M6 4 t3b): a routed ground goes by the rung change (<see cref="AutopilotTravelRule.Out"/>), so this stop is left only for a ground
    /// whose route has no portal and no run (the den: its portal is in landblock 0x1B90, Holtburg's 0xA9B4, and its route <c>recall-or-tie</c> is the recall alone), where the fix would be a mod place the rules forbid after the build.
    /// The run stops by name.
    /// </summary>
    private ScenarioStatus PortalGap(string finding)
    {
        if (_audit is { } audit)
        {
            audit.PortalTie = "gap";
        }

        var portal = _ground.Portal is { } p ? string.Create(CultureInfo.InvariantCulture, $"portal 0x{p.Guid:X8} in cell 0x{p.Cell:X8}, tie spell {p.TieSpell}") : "no portal record";
        _log.Error(LogCategory, StartupAudit.Line("portal_tie", $"{finding} ({portal})", $"gap: TODO(m5-portal-retie-route): no route of {TravelRoutes.FileName} walks or travels from {_ground.Home.Town} to the portal's front (the rung change has no portal and no run for {_ground.Key}); the run stops"));
        return AuditStop(StartupAudit.PortalGapStop, $"the {_ground.Key} portal is not tied and no route reaches it without a place (TODO(m5-portal-retie-route))");
    }

    // ------------------------------------------------------------------------------------------------ M6 4 t3b: the rung change in the Travel state

    /// <summary>The way out by <see cref="AutopilotTravelRule.Out"/> from where the body stands now, logged on one line unless <paramref name="quiet"/>.</summary>
    private TravelDecision DecideOut(bool quiet = false)
    {
        if (_session is not { } session)
        {
            return new TravelDecision(TravelWay.DenTravel, "no session yet: den-travel");
        }

        var state = session.State;
        var input = AutopilotTravelRule.Input(state, _ground, _context.Facts, _context.Routes, RungDirection.Out, _context.Grounds, _context.Spells);
        var decided = AutopilotTravelRule.Out(input, AutopilotTravelRule.BookRead(state.Self.Spellbook), _context.Routes);
        if (!quiet)
        {
            _log.Info(LogCategory, TravelLine("out", input, decided));
        }

        return decided;
    }

    /// <summary>The Travel state out of town: <c>den-travel</c>, the rung change, or the named gap (<see cref="AutopilotTravelRule.Out"/>).</summary>
    private ScenarioStatus BeginTravelOut(string why)
    {
        if (StartQuestNow($"{why}: before she leaves home") is { } startQuest)
        {
            return startQuest; // M6 14 the start quest: its errand from home at the run's start; the ladder after it (AutopilotScenario.StartQuest.cs)
        }

        if (KillTaskErrand(QuestThen.Home, $"{why}: before she leaves home") is { } errand)
        {
            return errand; // M6 14 guard task: a kill task's talk (the arm, or the turn-in) is made from home before the way out; after it the rung is read again
        }

        if (LeavingHeld(why) is { } held)
        {
            return held; // M6 7 AW: the gate of the run's ground is read before she leaves for it
        }

        var decided = DecideOut();
        return decided.Way switch
        {
            TravelWay.RungChange => BeginRun(AutopilotState.Travel, RungOutSpec, $"{why}: the rung change's route {decided.Route}"),
            TravelWay.Gap => PortalGap(decided.Reason),
            _ => BeginRun(AutopilotState.Travel, DenTravelSpec, why),
        };
    }

    /// <summary>The way home's spec (<see cref="AutopilotTravelRule.Home"/>): <see cref="RecallHomeSpec"/>, or <see cref="RungHomeSpec"/> for a book without Lifestone Recall where a way home by portals exists; one line says which.</summary>
    private StationSpec HomeSpec() => HomeDecision() is { Way: TravelWay.RungChange } ? RungHomeSpec : RecallHomeSpec;

    /// <summary>The rule's decision for the way home from where the body stands (logged, one line), or null with no session.</summary>
    private TravelDecision? HomeDecision()
    {
        if (_session is not { } session)
        {
            return null;
        }

        var state = session.State;
        var input = AutopilotTravelRule.Input(state, _ground, _context.Facts, _context.Routes, RungDirection.Home, _context.Grounds, _context.Spells);
        var decided = AutopilotTravelRule.Home(input, AutopilotTravelRule.BookRead(state.Self.Spellbook), _context.Routes);
        _log.Info(LogCategory, TravelLine("home", input, decided));
        return decided;
    }

    private static string TravelLine(string direction, RungRouteInput input, TravelDecision decided) =>
        string.Create(CultureInfo.InvariantCulture, $"autopilot travel: direction={direction} way={decided.WayName} ground={input.Ground.Key} place={input.Place} where={input.Where.ToString().ToLowerInvariant()} level={input.Level} tie={input.Tie.ToString().ToLowerInvariant()} lifestone_recall={(input.KnowsLifestoneRecall ? "yes" : "no")} portal_recall={(input.KnowsPortalRecall ? "yes" : "no")} route={decided.Route ?? "n/a"}; why: {decided.Reason}");

    private static bool IsRung(StationSpec? spec) => ReferenceEquals(spec, RungOutSpec) || ReferenceEquals(spec, RungHomeSpec);

    /// <summary>The verdict's <c>fail=</c> names a stop no retry changes: a gate refusal, a named <c>TODO(...)</c> of the plan, or the station's own precondition (<c>rung-change:...</c>).</summary>
    private static bool PlanStop(string fail) =>
        fail.StartsWith("gate:", StringComparison.Ordinal) || fail.StartsWith("TODO(", StringComparison.Ordinal) || fail.StartsWith("rung-change:", StringComparison.Ordinal);

    /// <summary>
    /// The rung change ended (M6 4 t3b): a failure stops by the station's own name <c>failed:travel:&lt;spec&gt;:&lt;fail&gt;</c> (the verdict's <c>fail=</c>) — at once for a plan's named stop (<see cref="PlanStop"/>), else through the station
    /// failure's recovery; out and ended well, the check after it (present in the ground's landblocks) before the fight starts, else <see cref="RungNotInGroundStop"/>. Null: go on as a travel that ended well.
    /// </summary>
    private ScenarioStatus? RungEnded(bool ok, string? why, string fields)
    {
        var name = _spec!.Name;
        if (!ok)
        {
            var fail = Token(fields, "fail") is { Length: > 0 } f && f != "none" ? f : JourneyVerdict.Token(why ?? "failed");
            var stop = $"failed:travel:{name}:{JourneyVerdict.Token(fail)}";
            _log.Error(LogCategory, $"autopilot travel: the {name} station failed by name (fail={fail}): {why ?? "no failure text"}");
            return PlanStop(fail) ? Stop(stop, ok: false, $"the {name} station stopped by its plan's named stop {fail}") : StationFailed(stop, $"the {name} station failed ({fail})", why);
        }

        if (!ReferenceEquals(_spec, RungOutSpec))
        {
            return null;
        }

        var self = _session!.State.Self;
        if (self.Travel != SelfTravel.Present || !InGround())
        {
            return Stop(RungNotInGroundStop, ok: false, $"the rung change ended well but the player is not present in {_ground.Key}'s landblocks ({Cell(_session)}, travel {self.Travel})");
        }

        _log.Info(LogCategory, $"autopilot travel: check after the rung change: present in {_ground.Key} ({Cell(_session)}); the fight starts");
        return null;
    }

    /// <summary>
    /// M6 14 entry recharge: a station that works where she stands (<see cref="IStationWorking"/>: the rung change's recharge at an entry and its top-up before a hostile drop, which may fill for minutes past the Travel state's 150 s bound):
    /// a new sign of its work is the state's progress, as a moving body is; a recharge that stops filling still trips the watchdog.
    /// </summary>
    private void NoteStationWorking()
    {
        if (((_run as IStationInner)?.Scenario as IStationWorking ?? _run as IStationWorking)?.WorkedAt is { } at && at > _stationWorkedSeen)
        {
            _stationWorkedSeen = at;
            _watchdogs.Progress(_clock);
        }
    }

    private double _stationWorkedSeen = double.NegativeInfinity;

    /// <summary>
    /// M6 4 t3b: a rung change walks for minutes (the island's runs: 609 m to the Burrow, 1,506 m to the Holding), past the Travel state's 150 s bound: the body moving <see cref="TravelProgressMetres"/> or more, or changing landblock (a
    /// portal), is the state's progress, so only a stalled rung change trips the watchdog.
    /// </summary>
    private void NoteTravelProgress(IGameSessionView session)
    {
        var self = session.State.Self;
        if (self.Travel != SelfTravel.Present || self.Position is not { } at)
        {
            return;
        }

        var here = at.ToGlobal();
        if (_travelMark is not { } mark || at.CellId >> 16 != _travelMarkLandblock || here.DistanceTo(mark) >= TravelProgressMetres)
        {
            _travelMark = here;
            _travelMarkLandblock = at.CellId >> 16;
            _watchdogs.Progress(_clock);
        }
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

    private FightScenario? LootFight() => (_run as IStationInner)?.Scenario as FightScenario;

    private List<KillCorpse>? _resumeCorpses; // M6 6 F3: the checkpoint's kill/corpse pairs, handed to the first fight that runs
    private List<KillCorpse> _lastCorpses = []; // M6 6 F3: the running fight's pairs at the last write (cleared when its fight ends)

    /// <summary>M6 6 F3: the pairs for the checkpoint: the fight's now while one runs, else none (a fight that ended looted or left what it had).</summary>
    private List<CheckpointCorpse> CorpsesForCheckpoint()
    {
        if (_state == AutopilotState.Fight && LootFight() is { } fight)
        {
            _lastCorpses = [.. fight.KillCorpses];
        }
        else if (_state != AutopilotState.Fight)
        {
            _lastCorpses = [];
        }

        // a restart that comes before the first fight keeps the file's pairs until the resumed fight takes them
        return [.. (_resumeCorpses ?? _lastCorpses).Select(c => new CheckpointCorpse { Guid = c.Guid, Name = c.Name })];
    }

    /// <summary>M6 6 F3 (<c>m6-resume-loot-queue</c>): the resumed run's saved corpses are queued once in the first fight that runs (the fight checks each: in view, the recorded name, not the player's, within the radius; a stale one is dropped by name).</summary>
    private void SeedResumedLoot(IGameSessionView session)
    {
        if (_resumeCorpses is not { } pairs || LootFight() is not { } fight)
        {
            return;
        }

        _resumeCorpses = null;
        var queued = fight.SeedLoot(session, pairs);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot resume: {queued} of the checkpoint's {pairs.Count} kill corpse(s) queued for the loot step ({string.Join(", ", pairs.Select(c => $"'{c.Name}' 0x{c.Guid:X8}"))})"));
    }

    private IStoppableFight? StoppableFight() => (_run as IStationInner)?.Scenario as IStoppableFight ?? _run as IStoppableFight;

    /// <summary>The fight's triggers, in order: a stop rule or request, the scripted death, the forced trip, the counters' stop or trip. Slice a3 adds the upkeep's (a buff line under its margin, the health floor) here.</summary>
    private void WatchFight(IGameSessionView session)
    {
        SeedResumedLoot(session);
        if (StoppableFight() is not { } fight)
        {
            if (!_noFightLogged && (_run as IStationInner)?.Scenario is not null)
            {
                _noFightLogged = true;
                _log.Error(LogCategory, "autopilot fight: the den-fight station's scenario takes no external stop (IStoppableFight): only its own end ends it (by design: only FightScenario implements IStoppableFight, and this guard serves a test station that does not)");
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

        TrialFightFrame(fight, kills); // M6 item 14: the trial's kills and its minutes of fight time
        if (_keeperBack is { } standing && fight.LootHeld is null)
        {
            // M6 14 recall unblock (m6-loot-hold; live 2026-11-14 17:27:42 UTC: the recall's recovery fought back on the travel state): a fight that stands in for a travel or recovery state opens no corpse; loot waits for the next ground visit
            fight.HoldLoot($"the fight stands in for the {StateName(standing.State)} state (a keeper's fight-back, M6 7 BD)");
        }

        if (fight.StopReason is not null)
        {
            return;
        }

        if (_keeperBack is null)
        {
            HourLoadFrame(session); // M6 14 hour load: the cycle's load begins at its first fight frame on the ground; the comfort rule is read on its cadence
        }

        FightStopReason? reason = null;
        if (StopDue() is { } stop && !StopWaitsForLull(stop))
        {
            _stopAsked ??= stop;
            reason = FightStopReason.StopRule;
        }
        else if (_options.TestDeath && !_testDeathDone && kills >= TestDeathAfterKills)
        {
            _testDeathDone = true;
            reason = FightStopReason.TestDeath;
        }
        else if (_options.TripAfterKills > 0 && !_forcedTripDone && _keeperBack is null && _tally.Kills + kills >= _options.TripAfterKills && !TripWaitsForLull("the forced trip"))
        {
            _forcedTripDone = true;
            reason = FightStopReason.ForcedTrip;
        }
        else if (RetreatDue(session, "fight"))
        {
            // M6 6 F2: under the ground's line and losing (the incoming beats the payable heal, nothing payable, or the emergency set aside), held RetreatHoldSeconds.
            // M6 8 (the sixth death): a retreat whose recall is not paid for waits while the fight drinks for it (FightScenario.DrinkForRecall); the way home's need would fail and she would stand.
            if (!(WayOutOn() && fight is FightScenario drinks && drinks.DrinkForRecall(session)))
            {
                reason = RetreatRule.StopReason;
            }
        }
        else if (ManaRetreatDue(session, fight))
        {
            reason = RetreatRule.StopReason; // M6 7 BE: attackers on her and the mana at the way out's line: she leaves while the recall can be paid
        }
        else if (TaskFightBackOver())
        {
            reason = TaskFightBackDone; // M6 6 F2: the task's fight-back first (D88) heard no attack for HostileWatch.AttackQuietSeconds: the task begins
        }
        else if (KeeperFightOver())
        {
            reason = KeeperFightDone; // M6 7 BD: no hostile in reach: the state the fight stood in for resumes
        }
        else if (_keeperBack is not null)
        {
            KeeperFightBuffs(session); // M6 14 gate lapse: the fight-back's fight keeps the buff lines as every fight does; it is never stopped for them
            return; // M6 7 BD: a keeper's fight-back reads no other trigger: the state it stands in for comes next
        }
        else if (LadderTrialOver(session))
        {
            reason = TrialOver; // M6 item 14: the trial's minutes are fought, in a lull: the cycle ends at once and its check takes the ladder's own rung
        }
        else if (LadderKillTaskDone(session))
        {
            reason = KillTaskDone; // M6 14 guard task: the server's counter read complete on the task's ground, in a lull: the cycle ends and the turn-in starts from here
        }
        else if (VitaeBurnOver(session))
        {
            reason = VitaeBurnt; // M6 sprint: the vitae is gone (or the owed corpse's rot is near), in a lull: the cycle ends at once and its check takes her rung again
        }
        else if (LadderMoveOnReached(session))
        {
            reason = MoveOnReached; // M6 7 R: the rung's move-on level, in a lull: the cycle ends at once, no trip needed
        }
        else if (LadderPassOwed(session))
        {
            reason = PassOwed; // M6 7 lead: an audit's fight skipped the run's first pass: it runs now, in a lull
        }
        else if (LadderCapRose())
        {
            reason = BoltCapRose; // M6 7 R: the raised War Magic lifted the rung's bolt cap and no hostile is in reach: the fight takes it where it stands
        }
        else if (BuffBotTripWanted(session) && !_tripLoop.Holding(_tally.Kills + kills) && BuffBotTripGoes(session))
        {
            BuffBotTripAsked(); // M6 item 14: the bot's lines are about to lapse in the field: home, the bot, and back
            _tripCadence = true; // forced: the station's own counters know nothing of the bot
            reason = FightStopReason.LogisticsTrip;
            CallTrip(session, "buff-bot", "the buff bot's lines are about to lapse in the field"); // M6 16 E5d: the verdict the trip carries
        }
        else if (_clock - _lastCounters >= CounterCheckSeconds)
        {
            _lastCounters = _clock;
            var verdict = _counters.Check(session.State, BuffCycles);
            if (SaleFloorHolds(verdict))
            {
                verdict = _counters.Check(session.State, BuffCycles, skipSale: true); // M6 safe load: the other reasons are not held to the sale's floor
            }

            if (verdict.Kind != LogisticsKind.Unread)
            {
                NoteFloors(); // M5 7 P7s (D148)
                NoteHeldBack();
                NoteWaits(session); // M6 14 T: a scroll or a sale that waits for coin is named once
            }

            if (verdict.IsStop && _rung is not null && !verdict.Carried && _counters.FirstCarriedShort() < 0 && !_tripLoop.Holding(_tally.Kills + kills))
            {
                // M6 7 lead (the rehearsal's stop at level 7, 2 Iron Scarabs): a line a home vendor sells under its stop line is what a trip restocks, as the station has read it since M5 7's audit; the run stops only when the trip cannot help.
                _log.Info(LogCategory, $"autopilot fight: the counters say stop, and the line is sold at home: a trip restocks it: {verdict.Reason}");
                if (TripUrgent(session, verdict.Reason))
                {
                    _tripCadence = false;
                    reason = FightStopReason.LogisticsTrip;
                    CallTrip(session, verdict); // M6 16 E5d: the verdict the trip carries
                }
            }
            else if (verdict.IsStop)
            {
                _countersStop = JourneyVerdict.Token(verdict.Trigger);
                _log.Warn(LogCategory, $"autopilot fight: the counters say stop: {verdict.Reason}");
                reason = FightStopReason.LogisticsStop;
            }
            else if (verdict.IsTrip && !_tripLoop.Holding(_tally.Kills + kills) && !StintHolds(verdict) && !BotHourHolds(session, verdict) && !TripWaitsForLull(verdict.Reason))
            {
                _log.Info(LogCategory, $"autopilot fight: the counters say a trip is due: {verdict.Reason}");
                _tripCadence = verdict.Cause == LogisticsCause.Cadence;
                reason = FightStopReason.LogisticsTrip;
                CallTrip(session, verdict); // M6 16 E5d: the verdict the trip carries
            }

            if (reason is null && !verdict.IsStop && !verdict.IsTrip && HourLoadTripDue(session, kills))
            {
                _tripCadence = true; // M6 14 hour load: the hour's trip, after every other trigger (they win); forced: the station's own counters know nothing of the hour
                reason = FightStopReason.LogisticsTrip;
            }
        }

        if (reason is null && LapsedLineTripDue(fight, kills))
        {
            _tripCadence = true; // forced: the station's own counters know nothing of a line down in the field
            reason = FightStopReason.LogisticsTrip;
            CallTrip(session, "lapsed-line", "a buff line ran out in the fight and is still down"); // M6 16 E5d: the verdict the trip carries
        }

        if (reason is null && _clock - _lastUpkeep >= UpkeepCheckSeconds)
        {
            _lastUpkeep = _clock;
            reason = UpkeepDue(session);
        }

        if (reason is null && OrderAtLull(fight) is { } ordered)
        {
            reason = ordered; // the command registry: an order taken in the lull, the last arm before the seat's (AutopilotScenario.Orders.cs)
        }

        if (reason is { } asked)
        {
            if ((asked == FightStopReason.LogisticsTrip || asked == FightStopReason.ForcedTrip || (asked == OrderStop && _order is { Kind: ScenarioOrderKind.Trip })) && !_tripUrgent && !_botTrip && fight is IBuffRefreshFight leaving) // the bot's trip recasts nothing of her own first: the bot renews it
            {
                // D126 (a) (soak round 12, 10:27 UTC: 15 lines came due inside the trip and lapsed in it): the lines due within the trip's expected length are recast before the fight ends for it
                _buffOptions ??= _options.Buff ?? new BuffOptions();
                var horizon = TripMeanSeconds + _buffOptions.LookaheadMarginSeconds;
                leaving.RequestLeaveRefresh(horizon, _options.AggroReachMetres, string.Create(CultureInfo.InvariantCulture, $"{asked.Name}: the trip's mean {TripMeanSeconds:0} s + margin {_buffOptions.LookaheadMarginSeconds:0} s"));
            }

            NoteWhy(_retreatForMana && asked == RetreatRule.StopReason ? RetreatManaWhy : asked.Name);
            _fightStop = asked;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot fight: stop asked ({asked.Name}) at kills={kills} (cycle {_cycle}: {_tally.Kills + kills} kills)"));
            fight.RequestStop(asked);
        }
        else
        {
            _trips?.Void(); // M6 16 E5d: the fight is on and asks nothing: a trip called before and never made is no longer due
            SeatAtLull(fight); // the last arm: nothing else asked to stop the fight (AutopilotScenario.Steer.cs)
        }
    }

    /// <summary>
    /// M6 14 T: true while the minimum fight stint holds the counters' trip (<see cref="TripStint"/>): no stop line is under, a visit has just bought what the purse pays (or the run has just begun, after its audit), and the trip is asked
    /// again when the stint is over. Named once a stint for each trigger, with the seconds left.
    /// </summary>
    /// <summary>
    /// M6 safe load (the owner, 2026-11-20: she never goes back to town to sell before 40 minutes have passed; other reasons are not held to it): true when <paramref name="verdict"/> is a trip to sell
    /// (<see cref="LogisticsVerdict.IsSale"/>) and the sale's floor still holds it (<see cref="TripStint.HoldsSale"/>) past the minimum stint, which holds every trip alike. Named once a stint for each trigger, with the seconds left; the
    /// caller asks the counters again without the sale's triggers, so a trigger after them (the foci, a scroll to learn) still calls its trip.
    /// </summary>
    private bool SaleFloorHolds(LogisticsVerdict verdict)
    {
        if (!verdict.IsSale || _stint.Holds(_clock) || !_stint.HoldsSale(_clock))
        {
            return false;
        }

        if (_stint.SayOnce("sale-floor:" + verdict.Trigger))
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot fight: trip held back by the sale's floor: {verdict.Reason} (trigger={verdict.Trigger}): {_stint.SaleLeft(_clock):0} s of the {_stint.SaleSeconds:0} s since {(_stint.AfterTrip ? "the last trip ended" : "the run began")} are left; a trip to sell waits that long, the loot swaps for better finds meanwhile (m6-sale-floor)"));
        }

        return true;
    }

    private bool StintHolds(LogisticsVerdict verdict)
    {
        if (!_stint.Holds(_clock))
        {
            return false;
        }

        if (_stint.SayOnce(verdict.Trigger))
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot fight: trip held back by the minimum stint: {verdict.Reason} (trigger={verdict.Trigger}): {_stint.Left(_clock):0} s of the {_stint.Seconds:0} s since {(_stint.AfterTrip ? "the last trip ended" : "the run began")} are left; no stop line is under, so the fight goes on and the trip is asked then (M6 14 T)"));
        }

        return true;
    }

    private int _learnWaitSaid;
    private bool _saleWaitSaid;

    /// <summary>
    /// M6 14 T (no silent choices): a want that calls no trip is named once: the scrolls the plan wants while the purse does not pay them (<see cref="LogisticsCounters.LearnWaitNote"/>, again when the scrolls waiting change), and a sale
    /// whose payout would not pay what is wanted (<see cref="LogisticsCounters.SaleWaitNote"/>, once each time it begins to wait).
    /// </summary>
    private void NoteWaits(IGameSessionView session)
    {
        var learn = _counters.LearnWaitKey;
        if (learn != _learnWaitSaid)
        {
            _learnWaitSaid = learn;
            if (_counters.LearnWaitNote(session.State) is { } note)
            {
                _log.Info(LogCategory, "autopilot fight: " + note + " (M6 14 T)");
            }
        }

        if (_counters.SaleWaits != _saleWaitSaid)
        {
            _saleWaitSaid = _counters.SaleWaits;
            if (_counters.SaleWaitNote(session.State) is { } note)
            {
                _log.Info(LogCategory, "autopilot fight: " + note + " (M6 14 T)");
            }
        }
    }

    private ulong _heldBackAnnounced; // M6 7 V: the stock lines whose trip is held back for the purse that were last logged

    /// <summary>
    /// M6 7 V (the r1h loop: level 10, 14 coin, a Mana Potion trip called at once, again and again): a stock line under its trip line whose wares the purse cannot pay is no trip (<see cref="LogisticsCounters.IsHeldBack"/>); each line is logged once when it
    /// is held back and once when the coin can pay it again, and the fight goes on.
    /// </summary>
    private void NoteHeldBack()
    {
        var mask = _counters.HeldBackMask;
        if (mask == _heldBackAnnounced)
        {
            return;
        }

        for (var i = 0; i < _counters.LineCount && i < 64; i++)
        {
            var bit = 1UL << i;
            if ((mask & bit) == (_heldBackAnnounced & bit))
            {
                continue;
            }

            var line = _counters.LineAt(i);
            _log.Info(LogCategory, (mask & bit) != 0
                ? string.Create(CultureInfo.InvariantCulture, $"autopilot fight: trip held back: {line.Name} {_counters.HeldOf(i)} held, under its trip line {line.TripUnder}, but the purse cannot pay the wares (the vendor's price x {LogisticsCounters.ClearOf(line) - _counters.HeldOf(i)}, to lift it clear of the line at {LogisticsCounters.ClearOf(line)}, inside one trip's share: a purse of {_counters.HeldBackCoin(i)}); no trip until the coin can, the fight goes on (M6 7 V, M6 14 T)")
                : string.Create(CultureInfo.InvariantCulture, $"autopilot fight: trip no longer held back for {line.Name} ({_counters.HeldOf(i)} held): the coin pays the wares, or the stock rose (M6 7 V)"));
        }

        _heldBackAnnounced = mask;
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

    /// <summary>
    /// M6 14 gate lapse (live, 2026-11-13 23:37:49 to 23:44:09 UTC: a keeper's fight-back of 379 s in the Tusker Barracks; Bludgeon Bane VI ran out on her armour at 23:39:35 and nothing recast it, because a fight-back reads no
    /// trigger but its own end and so never asked its fight to keep the lines): every <see cref="UpkeepCheckSeconds"/> the fight-back's fight is asked to keep the buff lines when one is under the re-buff margin, as the upkeep asks every
    /// other fight (F23): it recasts a lapsed line or one under the hard floor between its casts, the next due one between kills, and looks ahead at a lull. Never a stop: a fight-back ends only when no hostile is in reach.
    /// </summary>
    private void KeeperFightBuffs(IGameSessionView session)
    {
        if (_clock - _lastUpkeep < UpkeepCheckSeconds || StoppableFight() is not IBuffRefreshFight refresh || refresh.BuffRefreshAsked || refresh.BuffsKeptAhead)
        {
            return;
        }

        _lastUpkeep = _clock;
        _buffOptions ??= _options.Buff ?? new BuffOptions();
        BuffReads(session, inFight: true);
        if (_buffCycle.SeedPending)
        {
            return;
        }

        var due = _buffCycle.RebuffDue(_clock, _buffOptions.RebuffMarginSeconds);
        if (due.Count == 0)
        {
            return;
        }

        var detail = string.Create(CultureInfo.InvariantCulture, $"{string.Join(';', due)} under the {_buffOptions.RebuffMarginSeconds:0.#} s margin");
        _log.Info(LogCategory, $"autopilot fight: a buff line is due in the keeper's fight-back: {detail}: the fight keeps the lines itself as every fight does (a lapsed line or one under the hard floor between its casts, the next due one between kills, F23 F24); the fight-back is not stopped (M6 14 gate lapse)");
        refresh.RequestBuffRefresh(_buffOptions.RebuffMarginSeconds, _options.AggroReachMetres, detail);
    }

    /// <summary>The upkeep's two triggers, read every <see cref="UpkeepCheckSeconds"/>: the health under <see cref="HealthFloorPercent"/> (first: a mage near death is healed before it is buffed), a buff line of the profile under its margin (the lines the rebuff driver would call due).</summary>
    private bool _floorHeldLogged; // the health floor held back for a hostile in reach was logged for this episode

    private FightStopReason? UpkeepDue(IGameSessionView session)
    {
        var healthFloor = HealthFloorNow(session); // M5 7 F16 (D91): the ground's profile may say the fight never retreats for the health (0); M6 14 holding safety: the derived line where the place gives the facts
        if (healthFloor > 0 && session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var max) && max > 0 && health * 100.0 / max < healthFloor)
        {
            if (WayOutOn() && _hostiles is { } onHer && onHer.InReach(_clock, out var hostile))
            {
                // M6 8 (the climb's fourth death, 2026-11-11 19:38 UTC): the fight ended for the health floor with a tusker on her; the keeper could pay no heal and rested her on her feet in the peace mode, the retreat rule held
                // for a heal the guard called payable, the fight-back stands down under the retreat line in the heal in place, and she died in nineteen seconds of 1 to 4 a hit. With a hostile in reach the fight is not ended for
                // the floor: its own emergency heals, its potions pay, and the retreat rule (the recall) is the way out.
                if (!_floorHeldLogged)
                {
                    _floorHeldLogged = true;
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot fight: health {health} of {max} is under the {healthFloor:0}% floor, and a hostile is in reach ({hostile}): the fight is not ended for the keeper's heal in place; its emergency and the retreat rule decide (m6-floor-under-attack)"));
                }

                return null;
            }

            _floorHeldLogged = false;
            _upkeepDetail = string.Create(CultureInfo.InvariantCulture, $"health {health} of {max} is under the {healthFloor:0}% floor");
            _log.Info(LogCategory, $"autopilot fight: {_upkeepDetail}");
            return HealthFloor;
        }

        _buffOptions ??= _options.Buff ?? new BuffOptions();
        BuffReads(session, inFight: true); // M6 14 (m6-banes): the fight's upkeep reads the field's lines
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
        if (_retreatHeal && !_deathSeen)
        {
            _retreatHeal = false;
            return EnterKnown("after the retreat: the keeper restored the health at home"); // M6 6 F2: the known state goes back out
        }

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
            return QuestErrand(QuestThen.Trip, forced: true, "the forced trip") ?? BeginRun(AutopilotState.Trip, TripSpec(forced: true), string.Create(CultureInfo.InvariantCulture, $"the forced trip after {_options.TripAfterKills} kills (D52)"));
        }

        if (reason == FightStopReason.LogisticsTrip)
        {
            if (_tripUrgent)
            {
                _botTrip = false; // the bot's urgent leaving is the urgent recall's: the trip it owes runs from home, forced, and stops at the bot there
                return BeginUrgentRecall(); // M6 7 BD: a stop line with a hostile on her: Lifestone Recall, by name, then the trip from home
            }

            var botTrip = _botTrip;
            var hourTrip = _hourTrip;
            (_botTrip, _hourTrip) = (false, false);
            return QuestErrand(QuestThen.Trip, _tripCadence, botTrip ? "the buff bot's trip" : hourTrip ? "the hour's trip" : "the counters' trip")
                ?? BeginRun(AutopilotState.Trip, TripSpec(forced: _tripCadence), botTrip ? "the buff bot's trip: its lines are about to lapse (m6-buffbot-trip)" : hourTrip ? "the hour's trip: the cycle's minutes are fought on a comfortable ground (m6-hour-load)" : _tripCadence ? "the sell trip on the buff-cycle schedule (F29, D96)" : "the counters' trip");
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

        if (reason == RetreatRule.StopReason)
        {
            return _retreatForMana ? TakeManaRetreat(_session!) : TakeRetreat(_retreatWhy); // M6 6 F2: the way home, the keeper at home, then out again
        }

        if (reason == KeeperFightDone)
        {
            return KeeperResume(); // M6 7 BD: the state the fight stood in for runs again
        }

        if (reason == TaskFightBackDone)
        {
            _taskFightBack = false;
            return EnterKnown("the task's fight-back is over (D88): the task begins");
        }

        if (reason == VitaeBurnt)
        {
            EndCycle("vitae"); // M6 sprint: the cycle's check releases the hold and takes her rung again (rung-moved:<rung>); the run there fetches the corpse
            return EnterKnown("the vitae burn is over (M6 sprint: the vitae burnt first, the corpse after)");
        }

        if (reason == MoveOnReached)
        {
            EndCycle("move-on"); // M6 7 R: the cycle's check spends, then decides: rung-moved:<rung>, or the rung changes on the same ground
            return EnterKnown("the rung's move-on level is reached (M6 7 R)");
        }

        if (reason == TrialOver)
        {
            EndCycle("trial"); // M6 item 14: the cycle's check takes the ladder's own rung (rung-moved:<rung>), a normal rung change
            return EnterKnown("the trial's minutes are fought (M6 item 14, --autopilot-trial-minutes)");
        }

        if (reason == KillTaskDone)
        {
            EndCycle("kill-task"); // M6 14 guard task: the task's hunt is over; the turn-in, then the cycle's check takes her own rung (rung-moved:<rung>)
            return KillTaskErrand(QuestThen.Home, "the kill task's counter reads complete on its ground") ?? EnterKnown("the kill task's counter reads complete (its turn-in waits for the next way home)");
        }

        if (reason == OrderStop)
        {
            return OrderAfterFight(); // the command registry: the order taken in the lull acts (AutopilotScenario.Orders.cs)
        }

        if (reason == PassOwed)
        {
            return EnterKnown("a whole pass is owed (M6 7 lead)");
        }

        if (reason == BoltCapRose)
        {
            return BackToTheFight("the bolt cap rose on the raised War Magic (M6 7 R)");
        }

        // M6 6 F2 (closes m5-autopilot-reason): every reason the autopilot asks has its arm above; a reason it never asks (a fight that stopped itself by a name of its own, e.g. until-met) stops the run by name, loudly
        _log.Error(LogCategory, $"autopilot fight: stop reason {reason.Name} has no arm (the autopilot asks only stop-rule, test-death, forced-trip, logistics-trip, logistics-stop, buff-margin, health-floor, retreat, task-fight-back, keeper-fight-back, bolt-cap-rose, move-on-reached, trial-over and kill-task-done): the run stops");
        return Stop("failed:fight:unrouted-reason:" + JourneyVerdict.Token(reason.Name), ok: false, $"the fight ended for the reason {reason.Name}, which the autopilot never asks");
    }

    private void AddFight(string fields)
    {
        var t = _tally;
        t.Kills += Int(fields, "kills");
        t.Xp += Long(fields, "xp_gained");
        t.ManaWaitMaxSeconds = Math.Max(t.ManaWaitMaxSeconds, Double(fields, "mana_wait_max_s"));
        t.RestSeconds += Double(fields, "rest_s"); // M6 7 AB
        t.Fizzles += Int(fields, "fizzles");
        t.ManaTries += Int(fields, "mana_tries");
        t.ManaLanded += Int(fields, "mana_landed");
        t.StoreysVisited = Math.Max(t.StoreysVisited, Int(fields, "storeys_visited"));
        t.BasementEntries += Int(fields, "basement_entries");
        _hitMaxEnded = Math.Max(_hitMaxEnded, Long(fields, "hit_max")); // M5 7 F16
        _targetDiedEnded += Long(fields, "target_died_mid_cast"); // M5 7 F28
        NoteLadderCasts(fields); // M6 0 a5: the fight's casts= for the [ladder] line's casts a kill
    }

    // ------------------------------------------------------------------------------------------------ the trip and the death

    /// <summary>D126 (a): the trip's expected length in seconds (an exponentially weighted mean of the trips made, seeded by <see cref="TripSeedSeconds"/>): the lines due within it plus the lookahead margin are recast before a trip.</summary>
    public double TripMeanSeconds => double.IsNaN(_tripMean) ? TripSeedSeconds : _tripMean;

    /// <summary>
    /// D126 (a): a trip that ended joins the trip's mean length (from its station's start to its end). M6 6 ME (<c>m6-trip-mean-empty</c>): only a trip that sold or bought something joins it: a trip that made no sale and no purchase
    /// (the loop guard's idle trip, a trip skipped by its counters) is not a trip's length and would pull the mean down.
    /// </summary>
    /// <param name="sold">The items the trip sold.</param>
    /// <param name="bought">The items the trip bought.</param>
    private void NoteTripLength(int sold, int bought)
    {
        if (double.IsNaN(_tripStartedAt))
        {
            return;
        }

        var length = Math.Max(0, _clock - _tripStartedAt);
        _tripStartedAt = double.NaN;
        if (sold + bought <= 0)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot trip: it took {length:0} s and sold and bought nothing: it does not join the trip's mean ({TripMeanSeconds:0} s stays, D126)"));
            return;
        }

        _tripMeanCount++;
        _tripMean = (TripMeanSeconds * (1 - TripMeanWeight)) + (length * TripMeanWeight);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot trip: it took {length:0} s; the trip's mean is now {_tripMean:0} s (the buff lines due within it are recast before the next, D126)"));
    }

    private ScenarioStatus TripEnded(bool ok, string? why, string fields)
    {
        if (_buyTripActive)
        {
            return BuyTripEnded(ok, why, fields);
        }

        if (_villaActive)
        {
            return VillaStepEnded(ok, why, fields); // M6 villa: a villa step of the town trip's boundary
        }

        if (_questTripActive)
        {
            return QuestEnded(ok, why, fields); // M6 5 Q5 (D58): the trophy errand
        }

        var cutWork = _tripCutWork;
        if (Token(fields, "skipped") is { } skipped && !cutWork)
        {
            _tripPending = false;
            _errandAfterTrip = false; // no sale was made: the next way home asks the errand again
            ErrandRoomDropped($"made no visit (skipped={skipped})");
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
        NoteTripLength(Int(fields, "sold"), Int(fields, "bought"));
        _tally.Trips++;
        _tripEndedAtKills = _total.Kills + _tally.Kills; // M6 14 sell first (the run's kills: the cycle's tally starts afresh at the trip's end): an errand asked before the next kill reads its room as the check after the sale
        TripsMade++; // the command registry: a trip order's check after
        OrderTripEnded(fields);
        _stint.TripEnded(_clock); // M6 14 T: the minimum stint counts from here
        QuestTripReset(); // M6 5 Q5: the next way home may call the trophy errand again
        _counters.MarkTrip(BuffCycles); // M5 7 F29: the schedule counts from here
        _tally.Sold += Int(fields, "sold");
        _tally.Bought += Int(fields, "bought");
        _tally.ScrollsBought += Int(fields, "scrolls_bought"); // M6 1 d: the [ladder] line's
        _tally.Learned += Int(fields, "learned");
        if (_tripLoop.TripEnded(Int(fields, "sold"), Int(fields, "bought"), _tally.Kills))
        {
            _log.Warn(LogCategory, $"autopilot trip-loop: {TripLoopGuard.IdleLimit} trips in a row sold and bought nothing: trips are held back for {TripLoopGuard.HoldKills} kills and the run goes on fighting (M5 7 G1, edge audit 12a)");
        }

        _tally.SpentXp += Long(fields, "spent_xp");
        if (_deathSeen)
        {
            ErrandRoomDropped("ended in a death");
            return UnscriptedDeath();
        }

        EndCycle("trip");
        _recalledHome = false; // M6 7 T: a finished leg is a new leg of the cycle: the known state takes the way home, not the stop by name
        if (!ok)
        {
            _errandAfterTrip = false;
            ErrandRoomDropped("failed");
            return Stop("failed:trip:" + JourneyVerdict.Token(why ?? "failed"), ok: false, "the logistics station failed");
        }

        return ErrandAfterRoom() ?? ErrandAfterTrip() ?? VillaErrand("after the trip", boundary: true) ?? EnterKnown("after the trip"); // M6 8: a ladder errand that waited for the sale runs now, lighter; M6 villa: then the villa schedule's step of this town trip
    }

    /// <summary>
    /// M5 7 (TODO(m5-trip-failed-continue)): the trip failed after its one retry. A stock truly under its stop line (or a pack that cannot be read) stops the run by name, as before; with every line at or over its stop line the trip is given up for this
    /// cycle: a named gap line with the reason, the counters kept, the next trip held back by <see cref="TripLoopGuard.Hold"/> (<see cref="TripLoopGuard.HoldKills"/> kills, more than a buff cycle), and the mage goes back to the fight through the known state.
    /// </summary>
    private ScenarioStatus TripGivenUp(string? why)
    {
        TripsGivenUp++; // the command registry: a trip order's check after
        var token = JourneyVerdict.Token(why ?? "failed");
        var kit = _session is { } session ? StartupAudit.ReadKit(session.State, _ground, TripReader, _options.LootOn) : null;
        if (kit is null || kit.Unread || kit.Short.Count > 0 || kit.CarriedShort.Count > 0 || kit.Verdict.IsStop)
        {
            var named = kit is null || kit.Unread ? "the pack could not be read" : kit.Short.Count + kit.CarriedShort.Count > 0 ? "under their stop line: " + string.Join(',', kit.Short.Concat(kit.CarriedShort)) : kit.Verdict.Reason;
            return Stop("failed:trip:" + token, ok: false, $"the logistics station failed twice and the stock cannot go on ({named})");
        }

        _log.Warn(LogCategory, $"autopilot trip: the trip is given up for this cycle after its retry failed ({why ?? "failed"}); every stock line is at or over its stop line, so the run goes back to the fight; the next trip waits {TripLoopGuard.HoldKills} kills (TODO(m5-trip-failed-continue))");
        _tripPending = false;
        _tripCutWork = false;
        _tripFailedOnce = false;
        _errandAfterTrip = false; // no sale was made: the next way home asks the errand again
        ErrandRoomDropped("was given up");
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

        freeSlots = Quartermaster.PackView.MainPackFree(session.State);
        burdenPercent = load.Burden * 100.0 / load.Capacity;
        return true;
    }

    private ScenarioStatus DeathEnded(bool ok, string? why, string fields)
    {
        if (!ok && !Unrecoverable(why))
        {
            if (DeathAnswer.Of(why, Token(fields, "corpse_recovered"), Token(fields, "trip_abort")) is not { } answer)
            {
                return StationFailed("failed:death:" + JourneyVerdict.Token(why ?? "failed"), "the death station failed", why); // the death stays pending: the corpse is still to be recovered (4-R2, 4-R6)
            }

            // M6 7 U (r1f-climb): the handler answered for the corpse, so a second corpse trip owes nothing: the station is not entered again and the climb goes on from where the body stands.
            _log.Warn(LogCategory, $"autopilot death: the station ended ok=no ({why ?? "failed"}) and the handler answered {answer}: nothing is owed, no second trip; the climb goes on");
            ok = true;
        }

        return DeathCounted(Int(fields, "deaths"), ok, why);
    }

    /// <summary>The death is over (the station's end, or <see cref="CorpseLost"/>): counted, the cycle ended, then the known state, or the named stop of a fact a recall cannot change.</summary>
    private ScenarioStatus DeathCounted(int deaths, bool ok, string? why)
    {
        if (_vitaeFetch)
        {
            _vitaeFetch = false; // M6 sprint: an owed corpse's fetch: its death was counted when the corpse was deferred; a newer death on the trip is the station's own count
            _tally.Deaths += Math.Max(0, deaths);
        }
        else
        {
            _tally.Deaths += deaths > 0 ? deaths : 1;
            TrialDeath(deaths > 0 ? deaths : 1); // M6 item 14: a death during the trial ends it; the cycle's check after the recovery takes the ladder's own rung
        }

        _context.Facts.CorpseRotLevel = null;
        VitaeBurnAfterDeath(); // M6 sprint: a deferred corpse's tally goes to the burn; the run keeps a new one
        PackHoldAfterDeath(); // M6 7 AY
        _deathSeen = false;
        _unscripted = false;
        _deathPending = false;
        QuestTripReset(); // M6 5 Q5: the next way home may call the trophy errand again
        EndCycle("death");
        _recalledHome = false; // M6 7 T: a finished leg is a new leg of the cycle: the known state takes the way home, not the stop by name
        return ok ? EnterKnown("after the death") : Stop("failed:death:" + JourneyVerdict.Token(why ?? "failed"), ok: false, "the death station failed");
    }

    /// <summary>
    /// A death the autopilot did not script (seen by its PlayerDied subscription in any state, or a known state with no health) is handled by the death station entered after the respawn (<c>m5-autopilot-death-unscripted</c>): the autopilot's own
    /// death tally has been running since its start, so the corpse is known; it is offered to the station (<see cref="JourneyFacts.ObservedDeath"/>), which takes it and begins at the respawn instead of at the lie-down. The order after it is the handler's
    /// (D51, D53: the rearm, the rebuff, the corpse trip, the items back, the item lines), then the known state, which sends the mage back to the fight. A second death during the handler is the handler's own case (it restarts for the newer corpse).
    /// </summary>
    private ScenarioStatus UnscriptedDeath()
    {
        if (_questTripActive && _questLadderErrand)
        {
            LadderErrandDied(_questKey); // M6 8 F: a death in a ladder errand goes on to the death path; the errand is not walked again this run
        }

        VillaStepLeft("a death took it"); // M6 villa

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
        if (_villaActive)
        {
            return VillaStepCut(); // M6 villa: a cut villa step is not run again at this boundary
        }

        if (_questTripActive)
        {
            return QuestCut(); // M6 5 Q5: a cut errand is not run again this trip
        }

        if (_buyTripActive)
        {
            _log.Warn(LogCategory, "autopilot trip: the buy-trip was cut: the buy-trip station runs again from its start (it counts the stack again)");
            return BeginRun(AutopilotState.Trip, BuyTripSpec, "the buy-trip again after it was cut", resume: true);
        }

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
        LadderSpendCut(); // M6 7 R: a drop, a death or an abandon under a level-up spend: the rest is owed to the next cycle's start
        if (clearFinal && _finalStop is not null)
        {
            // a drop, a death or an abandon during the recall before the stop: the recall is given up; a stop rule still due asks for it again when the mage is back (M5 7 F18)
            _log.Warn(LogCategory, "autopilot: the recall home before the stop was cut; it is asked for again if the stop is still due");
            _finalStop = null;
            _finalAttempts = 0;
            _finalRetryFrom = double.NaN;
            _finalRungTried = false;
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
                else if (_fightStop == OrderStop && _order is { State: ScenarioOrderState.Taken, Spec: null } order)
                {
                    order.State = ScenarioOrderState.Waiting; // asked, not acted on: the next safe point takes it again
                }

                break;
            case AutopilotState.Trip:
                if (_run is { } trip)
                {
                    FoldTrip(SafeFields(trip));
                }

                break;
            case AutopilotState.Death:
                NoteDeathCut(_run is { } death ? SafeFields(death) : string.Empty);
                break;
            case AutopilotState.Buff when _order is { State: ScenarioOrderState.Taken } order && ReferenceEquals(order.Spec, _spec):
                (order.State, order.Spec) = (ScenarioOrderState.Waiting, null); // the order's pass was cut: the next safe point takes it again
                break;
        }

        DisposeRun();
        _pending = null;
        _fightStop = null;
        _healOnly = false;
        _unscripted = false;
        _context.Facts.ObservedDeath = null;
    }

    /// <summary>
    /// M6 4 t4b (D44, closes <c>m6-buy-trip-autopilot</c>): a carried line under its stop line. When the station's rule says the trip is due and the seller's town is reachable (<see cref="AutopilotBuyTripRule"/>), the Trip state runs
    /// <see cref="BuyTripStation"/> at the trip point, once a run; null (the audit's named stop stands, <c>_buyTripWhy</c> says why) when it is not due, not reachable, a hostile is on it, or it was tried already.
    /// </summary>
    private ScenarioStatus? BuyTripCallFor(GameState state, StartupAudit audit, string stop, IReadOnlyList<string> lines)
    {
        if (_buyTripTried)
        {
            _buyTripWhy = "the buy-trip ran once this run and the line is still under its stop line (check after)";
            return null;
        }

        if (audit.Attacker is not null)
        {
            _buyTripWhy = "a hostile is on it";
            return null;
        }

        var plan = BuyTripRule.PlanLevel(state.Self.Qualities, LadderLoader.WarMage, out var planWhy);
        var decision = BuyTripRule.Decide(_ground, TripReader, plan, planWhy, ScrivenerCatalog.Default, GroundCatalog.All); // M6 16 E5d: the counts the audit's kit read just counted (the reading twin's)
        var call = AutopilotBuyTripRule.Call(decision, _context.Facts, state.Self.Position is { } at ? (ushort)(at.CellId >> 16) : null, ScrivenerCatalog.Default, _context.Routes, AutopilotBuyTripRule.TownInput(state, _ground, _context.Routes, _context.Facts, _context.Spells), LeverDoors.Default); // M6 4 F1 (4-R4): the station's own choice and inputs
        _buyTripWhy = call.Reason;
        _log.Info(LogCategory, StartupAudit.Line("buy_trip", $"{string.Join(',', lines)}: {(call.Queue ? "queued" : "not queued")} via={call.Via}: {call.Reason}; {decision.Kind} {decision.Token}: held {decision.Held}, want {decision.Want}", call.Queue ? "the buy-trip station at the trip point; the pack is read again after it" : "the audit's named stop"));
        if (!call.Queue)
        {
            return null;
        }

        _buyTripTried = true;
        _buyTripActive = true;
        _buyTripStop = stop;
        return BeginRun(AutopilotState.Trip, BuyTripSpec, $"audit: {decision.Line!.Name} under its stop line ({decision.Held} held, want {decision.Want}): the buy-trip via {call.Via}");
    }

    /// <summary>The buy-trip station's end (D44): well, the known state re-audits and the audit reads the stack again; failed, the audit's stop by name.</summary>
    private ScenarioStatus BuyTripEnded(bool ok, string? why, string fields)
    {
        _buyTripActive = false;
        _tripPending = false;
        _log.Info(LogCategory, $"autopilot buy-trip: ended ok={(ok ? "yes" : "no")} bought={Int(fields, "bought")}{(why is null ? string.Empty : " why=" + why)}");
        _tally.Bought += Int(fields, "bought");
        if (_deathSeen)
        {
            return UnscriptedDeath();
        }

        _recalledHome = false; // M6 7 T: as after the trip
        return ok
            ? EnterKnown("after the buy-trip")
            : Stop(_buyTripStop, ok: false, $"the buy-trip failed ({why ?? "failed"}): the carried line stays under its stop line");
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

    private void NoteDeathCut(string fields)
    {
        if (!_unscripted && !SawDeath(_deathTally))
        {
            _deathPending = false;
            _testDeathDone = false; // the scripted death had not happened: it is scripted again at the next fight with a kill
            _log.Info(LogCategory, "autopilot death: the death station was cut before the mage died: the scripted death is made again");
            return;
        }

        if (DeathAnswer.Of(null, Token(fields, "corpse_recovered"), Token(fields, "trip_abort")) is { } answer)
        {
            // M6 7 U: a cut after the handler's answer owes no corpse: the death counts here and the recovery goes to the known state, which resumes the climb from where the body stands.
            var deaths = Int(fields, "deaths");
            _tally.Deaths += deaths > 0 ? deaths : 1;
            (_deathPending, _deathSeen, _deathAnsweredCut) = (false, false, true);
            _log.Warn(LogCategory, $"autopilot death: the death station was cut after the handler answered {answer}: nothing is owed, it is not entered again; the climb goes on");
            return;
        }

        _deathPending = true;
        _log.Warn(LogCategory, "autopilot death: the death station was cut: the death's tally is kept and the station is entered again from the known state");
    }

    /// <summary>The <c>trip_abort=</c> of a corpse trip given up after the death station failed its recoveries (M6 7 U; with <c>corpse_recovered=no</c>).</summary>
    public const string CorpseLostAbort = "corpse_lost";

    /// <summary>True for the stop of a death station that failed its recoveries while the death is still pending: never a stop of the run while the only loss is a corpse trip (M6 7 U); the third death (<see cref="Unrecoverable"/>) stays the owner's stop (M5 D92).</summary>
    private bool OnlyACorpseIsLost(string stop, string why) => _finalStop is null && _deathPending && DeathStationStop(stop) && !Unrecoverable(stop) && !Unrecoverable(why);

    private static bool DeathStationStop(string stop) => stop.StartsWith("failed:death:", StringComparison.Ordinal) || stop == "watchdog:" + StateName(AutopilotState.Death);

    /// <summary>
    /// M6 7 U (r1f-climb: the night ended on <c>failed:death:handler:...</c>): the death station used its recoveries (the same failure three times, or the cycle's budget), so the corpse trip is given up by name, <c>corpse_recovered=no
    /// trip_abort=corpse_lost</c>, and counted (<c>corpses_lost=</c> on the <c>[ladder]</c> line): the character has respawned, the corpse holds only what the drop rule says, and the known state decides from where the body stands. A true failure stays
    /// loud; it just does not end the night.
    /// </summary>
    private ScenarioStatus CorpseLost(string stop, string why)
    {
        DisposeRun();
        (_fix, _retry, _lastFailure, _failureRepeats, _fightStop, _pending) = (null, null, null, 0, null, null);
        _unscripted = false;
        _context.Facts.ObservedDeath = null;
        _tally.CorpsesLost++;
        _log.Error(LogCategory, $"autopilot death: corpse_recovered=no trip_abort={CorpseLostAbort}: the corpse trip is given up after {stop}: {why}; the death is counted and the climb goes on (corpses_lost={_tally.CorpsesLost} this cycle)");
        return DeathCounted(1, ok: true, why: null);
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
        if (RepeatedFailure(stop, label, detail, raw) is { } repeated)
        {
            return repeated;
        }

        if (!_watchdogs.TryRecover())
        {
            return Stop(stop, ok: false, $"{detail}; the {_watchdogs.MaxRecoveries} recoveries of cycle {_cycle} are used");
        }

        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot recovery after {label}: {detail} (cycle {_cycle}, recovery {_watchdogs.Recoveries} of {_watchdogs.MaxRecoveries})"));
        return RecoverTo($"recovery after {label}");
    }

    /// <summary>
    /// M6 7 I1: the same station failing from the same cell with the same failure again must not spend a recovery at once on the identical plan (r1-climb: both recoveries of cycle 2 went in 0.1 s and the run stopped). The first
    /// failure goes to the usual recovery (null). The second is settled (<see cref="RecoveryTable.SettleSeconds"/>), then one named act changes the state (a step back), then the last retry, which is not charged to the cycle's budget
    /// (it is bounded by the signature: <see cref="RecoveryTable.RepeatStopCount"/>). The third stops the run by name. One log line per decision.
    /// </summary>
    private ScenarioStatus? RepeatedFailure(string stop, string label, string detail, string? raw)
    {
        var cell = _session is { } s ? Cell(s) : "no session";
        var signature = RecoveryTable.Signature(stop, cell, raw ?? detail);
        var count = string.Equals(signature, _lastFailure, StringComparison.Ordinal) ? _failureRepeats + 1 : 1;
        (_lastFailure, _failureRepeats) = (signature, count);
        if (count == 1)
        {
            return null;
        }

        if (count >= RecoveryTable.RepeatStopCount)
        {
            _log.Error(LogCategory, $"autopilot recovery: repeat={count} the same failure from the same station and cell ({label}, {cell}) after the settle and the step: the run stops safely");
            return Stop(stop, ok: false, $"{detail}; the same failure {count} times from {cell} (after a settle and a step back)");
        }

        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot recovery: repeat={count} fix=settle-step after {label} from {cell}: the same station failed with the same failure again; settle {RecoveryTable.SettleSeconds:0} s, step back {RecoveryTable.StepBackSeconds:0.0#} s, then the last retry (cycle {_cycle}, recoveries used {_watchdogs.Recoveries} of {_watchdogs.MaxRecoveries}, this retry is not charged)"));
        _retry = new TypedRetry(ActionReason.None, RecoveryFix.SettleStep, label, stop, detail);
        _fix = new RecoveryFixRun(RecoveryFix.SettleStep, _context.Keeper);
        _phase = RunPhase.Fixing;
        _phaseStart = _clock;
        return ScenarioStatus.Running;
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
        if (ManaDanceGivesWay(session) is { } answered)
        {
            return answered;
        }

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
        if (_deathPending || _deathAnsweredCut)
        {
            return EnterKnown(why);
        }

        var way = HomeDecision();
        if (way is { Way: TravelWay.AtHome })
        {
            // M6 7 BC: check before (the rule reads where she stands: at home), no act (no recall from home), the known state classifies after
            _log.Info(LogCategory, $"autopilot recovery: at-home: {way.Reason}; no recall home is run ({why})");
            return EnterKnown(why);
        }

        _recalledHome = true; // the recall home is this recovery's: the known state that follows stops by name if it finds the mage elsewhere again
        return BeginRun(AutopilotState.Travel, way is { Way: TravelWay.RungChange } ? RungHomeSpec : RecallHomeSpec, why); // M6 4 t3b: below Lifestone Recall, home by portals from a routed place
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
        _orphanWhere = null; // M6 6 FXM2 (6-M6): the old session's `where` answers for nobody
        _apartWhere = null; // M6 7 AJ: the same
        _bodyApart.Reset();
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
        _reconnectLeftWorld = true; // the drop itself is the session leaving the world
        _reconnectAt = _clock + Wait(1);
        _phase = RunPhase.Reconnecting;
        Stage = "reconnecting";
        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"reconnect: the session left the world (phase {phase.ToString().ToLowerInvariant()}) in the {StateName(_state)} state, cycle {_cycle}; try 1 of {_options.ReconnectWaits.Count} in {Wait(1):0.#} s"));
        return ScenarioStatus.Running;
    }

    private double Wait(int attempt) => _options.ReconnectWaits[Math.Min(attempt, _options.ReconnectWaits.Count) - 1];

    private ScenarioStatus ReconnectStep(IGameSessionView session)
    {
        if (session.State.Phase != SessionPhase.InWorld)
        {
            _reconnectLeftWorld = true;
        }

        if (_orphanStage == OrphanStage.LoggingOff)
        {
            return OrphanReloginStep(session); // M6 6 MH: an orphaned login's log-off before its wait (AutopilotScenario.Connection.cs)
        }

        if (!_reconnectLoggedIn && InWorldAgain(session))
        {
            // M6 14 body (live ladder49 01:59:12-02:03:31 UTC): the check before a try reads the session's phase. Back in the world as her (another owner's entry, or a try whose login was miscounted) is the re-login done: the run resumes
            // now, its known state's checks first (a death seen, the guard's heal under the line, the retreat), instead of waiting out a try's wait standing still under attack and then sending an EnterWorld the session refuses.
            return Reconnected(session, "the session was in the world again as the run's character while the reconnect waited (an entry the try did not send itself)");
        }

        if (_orphanStage != OrphanStage.None)
        {
            if (session.State.Phase == SessionPhase.InWorld && session.State.Self.Guid != 0)
            {
                GuardHolds(session); // M6 14 body: a log-off that did not take her out leaves her in the world for the wait: never standing still under attack, the guard heals under its line
            }

            return OrphanReloginStep(session); // M6 6 MH: an orphaned login's wait before its re-login (AutopilotScenario.Connection.cs)
        }

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

                return Reconnected(session, _host.LoginNote);
            case ScenarioStatus.Failed:
                return TryFailed(_host.LoginFailure ?? "the login failed");
            default:
                return _clock - _reconnectLoginStart > ReconnectLoginSeconds ? TryFailed($"the login took over {ReconnectLoginSeconds:0} s") : ScenarioStatus.Running;
        }
    }

    /// <summary>M6 14 body: the session is in the world as the run's own character again since the reconnect took it out (the check before and after a try).</summary>
    private bool InWorldAgain(IGameSessionView session) =>
        _reconnectLeftWorld && session.State.Phase == SessionPhase.InWorld && session.State.Self.Guid != 0 && (_characterGuid == 0 || session.State.Self.Guid == _characterGuid);

    /// <summary>The reconnect's success (its login's, or the world found entered by the check before a try): counted, the watchdogs paused, the orphan probe and the audit armed, then the known state.</summary>
    private ScenarioStatus Reconnected(IGameSessionView session, string? note)
    {
        var tries = _options.ReconnectWaits.Count;
        _tally.Reconnects++;
        _reconnectLoggedIn = false;
        _orphanStage = OrphanStage.None;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"reconnect: try {_reconnectTry} of {tries} ok: back in the world (player 0x{session.State.Self.Guid:X8}); reconnects={_total.Reconnects + _tally.Reconnects} this run, {_tally.Reconnects} this cycle; the state is classified from the world again, its checks first (a death seen, the guard's heal){(note is null ? string.Empty : "; " + note)}"));
        _watchdogs.Pause(_clock);
        ArmOrphanProbe(); // M6 6 MH: the known state asks the server once where the player is (AutopilotScenario.Connection.cs)
        BeginAudit("reconnect");
        return EnterKnown($"reconnected after {_reconnectTry} {(_reconnectTry == 1 ? "try" : "tries")} (dropped in the {StateName(_reconnectFrom)} state)");
    }

    private ScenarioStatus TryFailed(string why)
    {
        var tries = _options.ReconnectWaits.Count;
        if (_session is { } session && InWorldAgain(session))
        {
            // M6 14 body: the check after the try reads the phase too: a try that "failed" with the player in the world as her (live: "entering the world: Refused: not at the character list (phase InWorld)") is the re-login done.
            return Reconnected(session, $"the try's login said \"{why}\", but the session is in the world as the run's character: the re-login succeeded");
        }

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
        RecallTies(); // M6 4 t3b (m6-tie-memory): before the load, which may set a finished or another run's file aside: the tie is the character's
        RecallPackHold(); // M6 7 AY: so is the big pack rule's state
        ReadCheckpointBind(); // M6 item 14: and the lifestone bind's proof
        var checkpoint = AutopilotCheckpoint.Load(_checkpoints, _context.Character, out var note, out var warn, _characterGuid, _options.RunTag);
        if (checkpoint is not null && _options.TestDeath)
        {
            SetAsideForTheScriptedRun(checkpoint);
            return;
        }

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

        _resumeCorpses = checkpoint.Corpses is { Count: > 0 } saved ? [.. saved.Select(c => new KillCorpse(c.Guid, c.Name))] : null; // M6 6 F3 (m6-resume-loot-queue)
        _worn.Seed(checkpoint.Worn); // M5 7 S2d (M6 6 F2: the run's own memory): the worn set of the run being resumed
        _total.Add(checkpoint.Totals.ToTally());
        _tally.Add(checkpoint.CycleTally.ToTally());
        _ladderTrainedRun.AddRange(checkpoint.Trained ?? []); // M6 1 fix (1-R11): the skills the run trained so far (the [ladder] line's trained=)
        ApplySeed(new AutopilotSeed(checkpoint.Cycle, checkpoint.Cycles, TripMeanSeconds: checkpoint.TripMeanSeconds ?? double.NaN, TripMeanCount: checkpoint.TripMeanCount));
        _priorSeconds = Math.Max(_priorSeconds, checkpoint.MinutesUsed * 60); // the seed's seconds (a hand-back) and the file's: the larger stands
        NoteMinutesBudget(checkpoint); // M6 item 14: a budget already used fails the start by name (Begin)
        if (checkpoint.MinutesTarget != _options.Minutes || checkpoint.LevelTarget != _options.Level)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"checkpoint: the stop rules differ from the file's (minutes {checkpoint.MinutesTarget:0.#} -> {_options.Minutes:0.#}, level {checkpoint.LevelTarget} -> {_options.Level}): this run's rules hold, the minutes used carry over"));
        }
    }

    /// <summary>
    /// M6 7 D (<c>m6-gate-autopilot-cycles</c>): <c>--autopilot-test-death</c> scripts one whole run from its start (the death, the forced trip, the hand-off inside the minutes), so an earlier run's unfinished file is not resumed under it: the gate's
    /// rerun without a re-create (<c>-Steps</c>, M6 4 round 4) took over round 3's failed file with 17.4 of its 30 minutes used, the death took 9.8 more and the minutes ran out before the forced trip: <c>cycles=1</c>. The file is moved aside, named, and
    /// the run starts fresh; the tie memory was read before this. A plain run still resumes a failed or killed run. A hand-back never gets here (<see cref="ColdReadOnceStore"/> reads empty once).
    /// </summary>
    private void SetAsideForTheScriptedRun(AutopilotCheckpoint checkpoint)
    {
        string where;
        try
        {
            where = "moved to " + _checkpoints!.SetAside();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            where = "it could not be moved aside (the run's first write replaces it): " + e.Message;
        }

        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"checkpoint: set aside: a scripted run (--autopilot-test-death) starts whole, and this file is an earlier run's (cycle {checkpoint.Cycle}, {checkpoint.Cycles} ended, {checkpoint.MinutesUsed:0.#} minutes used, it was {checkpoint.State}{(checkpoint.Stop is null ? string.Empty : ", stop=" + checkpoint.Stop)}); {where}"));
    }

    /// <summary>
    /// M6 4 t3b (<c>m6-tie-memory</c>): the grounds the checkpoint remembers tied (<see cref="AutopilotCheckpoint.ReadTieMemory"/>) are put in the journey's facts at the start, unless this session already knows that ground's tie; one line
    /// says what was carried. The next trip to such a ground recalls (<see cref="AutopilotTravelRule.Out"/>: known tied) instead of walking the route.
    /// </summary>
    private void RecallTies()
    {
        var tied = AutopilotCheckpoint.ReadTieMemory(_checkpoints!, _context.Character, _characterGuid, out var note);
        var carried = new List<string>();
        if (tied.Count == 1 && _context.Facts.PrimaryPortalSlot is null && _context.Facts.PortalTieOf(tied[0]) == PortalTie.Unknown)
        {
            _context.Facts.SetPrimaryPortalTie(tied[0]); // M6 4 F1 (4-R2): the one slot; every other ground with a tie record is known untied
            carried.Add(tied[0]);
        }
        else if (tied.Count > 1)
        {
            note += $"; the primary tie is one slot and the file names {tied.Count}: none is carried (an older file; the next tie or recall sets the slot)";
        }

        if (note is not null)
        {
            _log.Info(LogCategory, $"checkpoint: {note}; carried into the journey's facts: {(carried.Count == 0 ? "none (this session knows them already)" : string.Join(",", carried))}");
        }
    }

    /// <summary>M6 4 F1 (4-R2): the tie memory's one slot: the ground the primary tie is known to hold, else the one ground known tied (a recall a station saw land), else none (two known tied: the slot is not known, so none is written).</summary>
    private IEnumerable<string> TieSlot()
    {
        if (_context.Facts.PrimaryPortalSlot is { } slot)
        {
            return [slot];
        }

        var tied = _context.Facts.PortalTies.Where(t => t.Value == PortalTie.Tied).Select(t => t.Key).ToArray();
        return tied.Length == 1 ? tied : [];
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
            PackHold = _packHold == PackHold.None ? null : _packHold, // M6 7 AY
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
            Worn = _worn.Entries(), // M5 7 S2d (M6 6 F2: the run's own memory): the worn set, for a restart's audit
            Trained = [.. _ladderTrainedRun], // M6 1 fix (1-R11)
            TripMeanSeconds = double.IsNaN(_tripMean) ? null : _tripMean, // M6 6 ME (m6-trip-mean-resume)
            TripMeanCount = _tripMeanCount,
            Corpses = CorpsesForCheckpoint(),
            PortalTied = [.. TieSlot()], // M6 4 t3b (m6-tie-memory); F1 (4-R2): the one slot
            Bind = CheckpointBind.From(_bindProof), // M6 item 14: the bind's proof, the character's (read back by the guid at each start)
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
        stop is SessionClosedStop or NoReconnectStop or ReconnectFailedStop or UnscriptedDeathStop or NoBindStop or MinutesBudgetStop or "failed:known-state:not-in-world" or "failed:known-state:not-present" or "failed:known-state:elsewhere"
        || stop.StartsWith("failed:travel:recall-home", StringComparison.Ordinal) || stop.StartsWith("failed:travel:" + RungHomeSpec.Name, StringComparison.Ordinal) || LadderStop(stop) || stop.StartsWith(LastResortRule.StopPrefix, StringComparison.Ordinal); // M6 4 t3b (M6 8 N: the last resort's own stop never asks for the recall again): the way home by portals is a recall home

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
        _finalRungTried = false;
        _log.Info(LogCategory, $"autopilot: recalling home before the stop (stop={final.Name}; the mage stands {(place == AutopilotPlace.Ground ? "on the ground" : "elsewhere")}): a character logged off away from home stays in the world and can be killed");
        return (final.Ok ? QuestErrand(QuestThen.Final, forced: false, "the way home before the stop") : null) ?? FinalAttempt("first"); // M6 5 Q5 (D58): a held due trophy is turned in on the way home
    }

    private ScenarioStatus FinalAttempt(string why)
    {
        _finalAttempts++;
        FoldInFlightForStop();
        DisposeRun();
        return BeginRun(AutopilotState.Travel, HomeSpec(), $"the recall home before the stop (try {_finalAttempts}{(_finalAttempts <= FinalRecallAttempts ? $" of {FinalRecallAttempts}" : string.Empty)}, {why})", resume: true);
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

        if (!_finalRungTried && FinalRungTry(why) is { } rung)
        {
            return rung; // M6 6 ME (m5-autopilot-stop-recall): the one last try, the way home by portals
        }

        _log.Error(LogCategory, $"autopilot: the recall home before the stop failed twice ({why}); the stop goes on with the mage away from home");
        return FinishFinal(arrived: false, why);
    }

    /// <summary>
    /// M6 6 ME (<c>m5-autopilot-stop-recall</c>): after the two recalls and the retries failed, one last try by <see cref="RungHomeSpec"/> (item 4's way home by portals, without Lifestone Recall) when the travel file has a way home from where the
    /// mage stands (<see cref="RungRoute.Decide"/>: legs and no named stop); null when it has none (the stop then goes on, named). Checked before the act (the way exists), checked after (the settle step reads the place; a failure comes back here and finishes).
    /// </summary>
    private ScenarioStatus? FinalRungTry(string why)
    {
        if (_session is not { } session || session.State.Phase != SessionPhase.InWorld)
        {
            return null;
        }

        var input = AutopilotTravelRule.Input(session.State, _ground, _context.Facts, _context.Routes, RungDirection.Home, _context.Grounds, _context.Spells);
        var plan = RungRoute.Decide(input with { KnowsLifestoneRecall = false, Direction = RungDirection.Home }, _context.Routes);
        if (plan.Stop is not null || plan.Legs.Count == 0)
        {
            _log.Info(LogCategory, $"autopilot: no way home by portals from {input.Place} either ({plan.Stop ?? plan.Why}): the stop goes on, named");
            return null;
        }

        _finalRungTried = true;
        DisposeRun();
        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot: the recall home before the stop failed twice ({why}); one last try by the way home by portals (route {plan.RouteKey}, {plan.Hops} portals, from {input.Place})"));
        return BeginRun(AutopilotState.Travel, RungHomeSpec, "the recall home before the stop (the last try, by portals)", resume: true);
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
        return Stop(final.Name, final.Ok, final.Why + (arrived ? string.Empty : "; the recall home before the stop failed twice and the way home by portals was not open or failed, the mage is not home"));
    }

    // ------------------------------------------------------------------------------------------------ the stop rules, the cycles and the lines

    /// <summary>
    /// M6 8 E (<c>m6-minutes-stop-safe-point</c>): the minutes rule alone, read at every safe point: before each station (<see cref="BeginRun"/>), each frame of a fight (<see cref="WatchFight"/>), a heal's start, and inside a logistics trip once its errand is done
    /// at home, before the way back (<c>JourneyFacts.MinutesStopDue</c>). It waits for what must not be cut: a walk or portal hop in flight ends at its own end, a corpse owed is recovered by the death station first, a fight ends at the next frame (it is the fight that stops).
    /// </summary>
    private bool MinutesDue() => _options.Minutes > 0 && SecondsUsed >= _options.Minutes * 60;

    /// <summary>The stop rule due now, or null: a request, the minutes (the session clock since the start), the level (the state's PropertyInt 25).</summary>
    private string? StopDue()
    {
        if (_stopAsked is { } asked)
        {
            return asked;
        }

        if (MinutesDue())
        {
            return "minutes";
        }

        if (_options.Level > 0 && !DenRunOrdered && _session is { } s && s.State.Self.Qualities.TryGetInt(PlayerQualities.LevelProperty, out var level) && level >= _options.Level)
        {
            return "level"; // M6 item 14: the den run is ordered at the level it is for (40), so the level rule is not read while the order stands: the order ends by its own stop (the minutes rule and a stop request still end it)
        }

        return null;
    }

    /// <summary>
    /// M6 2 e: the casters the character holds (wielded, main pack, side packs: <see cref="GearFloor.CastersHeld"/>) for the cycle and stop lines, a state read and not a summed tally (report-only in its first gate, D27). <c>?</c> while no session
    /// is bound or the inventory is not read yet: a count of 0 there would be a guess.
    /// </summary>
    private string CastersField() => _session is { } s && s.State.Self.Inventory.IsKnown ? GearFloor.CastersHeld(s.State).ToString(CultureInfo.InvariantCulture) : "?";

    private void EndCycle(string how)
    {
        CloseTally();
        var hourLoad = HourLoadEndCycle(); // M6 14 hour load: the cycle's mark, then its load ends
        Emit(string.Create(CultureInfo.InvariantCulture, $"autopilot cycle n={_cycle} state={how} why={WhyField()} {_tally.Fields()} casters={CastersField()} s={_tally.Seconds:0} {_vitae.Fields} hour_load={hourLoad} villa={VillaMarker()} every_spell={EverySpellMarker()} {ThreatShadowFields()}")); // M6 6 F2: the vitae fields, then M6 14's hour_load=, M6 villa's villa=, M6 14's every_spell= and M6 16 E2a's threat_shadow_agree= threat_shadow_differ=, close the line
        EmitLadder("cycle", _tally);
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
        if (!ok && OnlyACorpseIsLost(stop, why))
        {
            return CorpseLost(stop, why);
        }

        if (!ok && _deathAnsweredCut && DeathStationStop(stop))
        {
            return EnterKnown($"the death station was cut after its answer ({stop}): nothing is owed"); // M6 7 U: the cut's recovery budget is spent, but no corpse is owed: the known state, not a stop
        }

        stop = TaskStopName(stop); // M6 6 MB: the errand task's trip failure stops by the task's name (failed:errand:<why>)
        if (ok && _finalStop is null && _stopAsked is { } pending && IsRequest(pending) && stop.StartsWith("rung-moved:", StringComparison.Ordinal))
        {
            // M6 item 14: a move-on's stop would have the dial start the next rung's run: under a pending stop request nothing new starts, so the run stops by the request (and goes home first if she is not there)
            _log.Info(LogCategory, $"{PendingStopLinePrefix} ({pending}): the move-on ({stop}) is not taken: the run stops by the request, and the next start reads the rung again");
            (stop, why) = (pending, $"a stop request was pending at the move-on ({stop}): {why}");
        }

        if (_finalStop is not null)
        {
            return FinalAttemptFailed($"{stop}: {why}"); // the recall's own station could not be made or began wrongly: a try spent, never a second recall
        }

        if (!ok && LastResortInsteadOfStop(stop, why) is { } lastResort)
        {
            return lastResort; // the owner's widening of D120: no way home worked, so /lifestone, and the climb goes on
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
        Emit(string.Create(CultureInfo.InvariantCulture, $"autopilot cycle n={_cycle} state=stopped why={WhyField()} {_tally.Fields()} casters={CastersField()} s={_tally.Seconds:0} {_vitae.Fields} hour_load={HourLoadEndCycle()} villa={VillaMarker()} every_spell={EverySpellMarker()} {ThreatShadowFields()}"));
        EmitLadder("cycle", _tally);
        _total.Add(_tally);
        _tally = new AutopilotTally();
        var seconds = double.IsNaN(_startClock) ? 0 : _clock - _startClock;
        Emit(string.Create(CultureInfo.InvariantCulture, $"autopilot done ok={(ok ? "yes" : "no")} cycles={_cycles} stop={stop} {_total.Fields()} s={seconds:0} {_vitae.Fields}"));
        EmitLadder("done", _total);
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
        else if (_deathSeen)
        {
            // M6 14 body (live ladder49 02:02:28 UTC): a death seen (the server's death notice) that no death station took up, here during the reconnect's waits, is still a death: the stop's cycle, done and ladder lines count it.
            _tally.Deaths++;
            _log.Warn(LogCategory, $"autopilot stop: a death was seen in the {StateName(_state)} state{(_phase == RunPhase.Reconnecting ? " while reconnecting" : string.Empty)} and no death station took it up (\"{_deathText ?? "no text"}\"); the death is counted, its corpse was not recovered");
            _deathSeen = false;
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
