using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// The host's piece of the travel seam (M4 P5b 3.4): a run on foot with the wayfinder's runner through the player's keys.
/// The headless host runs the offline walker over the corridor streamer (as <c>WayfindScenario</c> does); the Godot host
/// runs the player's own body. The one piece the two hosts do not share, as <c>IFightWalk</c> is today.
/// </summary>
public interface IJourneyRunnerHost
{
    /// <summary>Starts a run to the request's point or object goal; the leg's <see cref="ITravelLeg.Means"/> is <see cref="TravelMeans.Run"/>.</summary>
    ITravelLeg Run(IGameSessionView session, TravelRequest request, double now);
}

/// <summary>What the Godot host hooks for frames (M4 P5b 3.4, 3.6); every member is called on the game thread.</summary>
public interface IJourneyObserver
{
    /// <summary>A station is about to start (after its travel and vitals passes).</summary>
    void StationStarting(string station, double now);

    /// <summary>A station ended (its verdict line was written).</summary>
    void StationEnded(string station, bool ok, double now);

    /// <summary>A named event inside a station worth a frame (a showpiece: <c>frost-arrow</c>, <c>ring-of-fire</c>, <c>portal-recall</c>).</summary>
    void StationEvent(string station, string name, double now);
}

/// <summary>An observer that ignores everything (the headless host).</summary>
public sealed class NoJourneyObserver : IJourneyObserver
{
    /// <summary>The one instance.</summary>
    public static NoJourneyObserver Instance { get; } = new();

    /// <inheritdoc />
    public void StationStarting(string station, double now)
    {
    }

    /// <inheritdoc />
    public void StationEnded(string station, bool ok, double now)
    {
    }

    /// <inheritdoc />
    public void StationEvent(string station, string name, double now)
    {
    }
}

/// <summary>A ground's portal tie as a journey knows it (M5 3 p1): <see cref="Unknown"/> (the default), <see cref="Tied"/> or <see cref="Untied"/>.</summary>
public enum PortalTie
{
    /// <summary>Not known: a recall to the ground is tried, and ACE's answer decides.</summary>
    Unknown = 0,

    /// <summary>The character is tied to the ground's portal (a Primary Portal Tie succeeded, or a Primary Portal Recall to it arrived).</summary>
    Tied = 1,

    /// <summary>ACE said the character is not tied (WeenieError 0x04A3).</summary>
    Untied = 2,
}

/// <summary>
/// What a journey has learned about its character that the server does not send back (M4 P5b W0: the portal tie and the
/// lifestone tie are not in the player description; ACE's <c>GameEventPlayerDescription</c> sends neither), and its
/// counters. Mutable; the journey and its stations share one.
/// </summary>
public sealed class JourneyFacts
{
    private readonly Dictionary<string, PortalTie> _portalTies = new(StringComparer.Ordinal);

    /// <summary>The mod teleports (<c>place</c>) made this journey (the verdict's <c>teleports=</c>), the pre-journey recovery excluded.</summary>
    public int Teleports { get; set; }

    /// <summary>The most mod teleports this journey may make (the plan's budget); one more is refused loudly.</summary>
    public int TeleportBudget { get; set; }

    /// <summary>The recalls cast to completion (Lifestone Recall, Primary Portal Recall; the verdict's <c>recalls=</c>).</summary>
    public int Recalls { get; set; }

    /// <summary>The items the scripted stations granted and saw land (<see cref="ScriptLeg.Granted"/>, summed); the done line's <c>granted=</c> is <see cref="Verbs"/>' instead, every grant the session sent.</summary>
    public int Granted { get; set; }

    /// <summary>The mod-verb counts at the end of the build-verify station (M5 item 3 c1): the build's verbs are build time (spec 5.6); the journey sets it (<see cref="PlayerTools.Verbs"/>).</summary>
    public ModVerbCounts VerbMark { get; set; }

    /// <summary>The mod verbs the session sent since <see cref="VerbMark"/>, set when the journey ends: <c>granted=</c> is <see cref="ModVerbCounts.Granted"/> and <c>places=</c> is <see cref="ModVerbCounts.Place"/> (a recovery place is apart).</summary>
    public ModVerbCounts Verbs { get; set; }

    /// <summary>
    /// What the journey knows of the portal tie of ground <paramref name="ground"/> (<see cref="HuntingGround.Key"/>; M5 3 p1, closes 2-R12 <c>journey-portal-per-ground</c>): <see cref="PortalTie.Tied"/> once its Primary Portal Tie succeeded or a Primary Portal Recall to it arrived,
    /// <see cref="PortalTie.Untied"/> after ACE's 0x04A3 (YouMustLinkToPortalToRecall), else <see cref="PortalTie.Unknown"/>. A tie to ground A says nothing of ground B. A fresh character after the build is tied (D50), but no journey can read the link from the player description,
    /// so unknown is the usual start and the recall is tried.
    /// </summary>
    /// <param name="ground">The ground's key.</param>
    public PortalTie PortalTieOf(string ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        return _portalTies.GetValueOrDefault(ground);
    }

    /// <summary>Sets what the journey knows of ground <paramref name="ground"/>'s portal tie (<see cref="PortalTie.Unknown"/> forgets it).</summary>
    /// <param name="ground">The ground's key.</param>
    /// <param name="tie">The tie.</param>
    public void SetPortalTie(string ground, PortalTie tie)
    {
        ArgumentNullException.ThrowIfNull(ground);
        if (tie == PortalTie.Unknown)
        {
            _portalTies.Remove(ground);
            return;
        }

        _portalTies[ground] = tie;
    }

    /// <summary>The ground keys whose tie is known (tied or untied), with the tie: the verdict lines and the tests read it.</summary>
    public IReadOnlyDictionary<string, PortalTie> PortalTies => _portalTies;

    /// <summary>True once Lifestone Tie on Holtburg's lifestone succeeded or a Lifestone Recall arrived; false after a 0x049E; null when unknown.</summary>
    public bool? LifestoneTied { get; set; }

    /// <summary>
    /// M5 4 a3 (<c>m5-autopilot-death-unscripted</c>): the death tally the autopilot has kept running since its start, offered to the death station when a death the autopilot did not script has been seen. The death station takes it (and sets this back to null),
    /// enters at the respawn instead of at the lie-down, and the handler reads the corpse's facts from it. Null for every other death.
    /// </summary>
    public DeathTally? ObservedDeath { get; set; }

    /// <summary>True once the character bound (used) Holtburg's lifestone this journey (its respawn point, ACE's Sanctuary).</summary>
    public bool Bound { get; set; }
}

/// <summary>
/// What every station of a journey shares (M4 P5b 3.4): the character, the log, the travel seam, the vitals keeper, the
/// spell table, the dats, the host's runner and observer, and the facts. Built by the journey; stations read it.
/// Lanes may add members (init-only, with a safe default); none is renamed or removed.
/// </summary>
public sealed class JourneyContext
{
    /// <summary>The character's name (<c>BSD Mage</c>, <c>Holtburg Brawler</c>, <c>Holtburg Archer</c>).</summary>
    public required string Character { get; init; }

    /// <summary>The test account's key (<c>test</c>, <c>test-melee</c>, <c>test-archer</c>).</summary>
    public required string Account { get; init; }

    /// <summary>The log every station writes to.</summary>
    public required ILogSink Log { get; init; }

    /// <summary>The travel seam the stations' scenarios use at their choke points.</summary>
    public required IJourneyTravel Travel { get; init; }

    /// <summary>The spell table (<c>SpellTableData.Spells</c>).</summary>
    public required IReadOnlyDictionary<uint, SpellInfo> Spells { get; init; }

    /// <summary>The vitals keeper, or null when the journey runs none.</summary>
    public VitalsKeeper? Keeper { get; init; }

    /// <summary>The dats (the hunt's follower and the walker need them), or null in a test without dats.</summary>
    public DatSet? Dats { get; init; }

    /// <summary>The host's runner, or null when the host cannot run (then every travel is a place within the budget, or refused).</summary>
    public IJourneyRunnerHost? Runners { get; init; }

    /// <summary>The host's observer (frames in Godot; none headless).</summary>
    public IJourneyObserver Observer { get; init; } = NoJourneyObserver.Instance;

    /// <summary>The facts and counters the stations share.</summary>
    public JourneyFacts Facts { get; init; } = new();

    /// <summary>
    /// The dungeon ground the journey's den stations run in (<c>--fight-ground</c>, the host passes <see cref="FightOptions.GroundProfile"/>; the default is the option's default ground): <see cref="StationWhere.Den"/> is this ground, its arrival is the budgeted place,
    /// its landblocks are where the player must stand, and the den-travel station reads its portal (M5 item 2 k2, D46: no den fact lives in the journey's code).
    /// </summary>
    public HuntingGround Dungeon { get; init; } = GroundCatalog.Get(FightOptions.DefaultGround);

    /// <summary>The window frame times of the running station (M4 4.2 J1: the Godot host's <see cref="JourneyWindowSamplers"/>), or null when the host has no frame clock (headless: the station lines print <c>n/a</c>).</summary>
    public IJourneyWindowFrames? WindowFrames { get; init; }
}
