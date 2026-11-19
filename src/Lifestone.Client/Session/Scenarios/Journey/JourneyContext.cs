using System.Globalization;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Travel;
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
    private readonly HashSet<string> _questsDone = new(StringComparer.Ordinal);

    /// <summary>The quests (by <c>Quest.Key</c>) a station saw done this journey (M6 0 F4): the Academy's stamps are erased by its exit, so the level up job's quest rung reads this fact, and asks the server's <c>quest has</c> for the quests that keep a stamp.</summary>
    public IReadOnlyCollection<string> QuestsDone => _questsDone;

    /// <summary>True when a station saw the quest of <paramref name="key"/> done this journey.</summary>
    /// <param name="key">The quest's key.</param>
    public bool QuestDone(string key) => _questsDone.Contains(key);

    /// <summary>Records that the quest of <paramref name="key"/> is done for this character.</summary>
    /// <param name="key">The quest's key.</param>
    public void MarkQuestDone(string key) => _questsDone.Add(key);

    /// <summary>
    /// M6 14 (the Cave's run of 2026-11-12: one stop at the Dryreach healer failed at 09:15 UTC, her walk stuck where the portal set her down, and "not tried again" left her without a tincture for two and a half hours, a death
    /// among them): the stops that may fail for one ware in a run before it is given up (Lifestone's own: a failed stop costs about 90 s, and the trips out are about an hour apart).
    /// </summary>
    public const int TownStockTries = 3;

    private readonly Dictionary<uint, (string Why, int Stop, int Stops)> _townStockFailed = [];
    private readonly HashSet<(uint Wcid, string Token)> _townStockSaid = [];
    private int _townStockStop;

    /// <summary>M6 14: a stop for the town stock begins: the failures recorded until the next one are one failed stop for each ware they name.</summary>
    public void TownStockStopBegins() => _townStockStop++;

    /// <summary>M6 14 T: why the stop for the town stock ware <paramref name="wcid"/> first failed this run, or null (kept for the run, not in the checkpoint).</summary>
    /// <param name="wcid">The ware's weenie class id.</param>
    public string? TownStockFailure(uint wcid) => _townStockFailed.TryGetValue(wcid, out var f) ? f.Why : null;

    /// <summary>M6 14: why the stop for <paramref name="wcid"/> is given up for this run (<see cref="TownStockTries"/> stops failed for it; the first failure's name), or null while it is still tried on the next way out.</summary>
    /// <param name="wcid">The ware's weenie class id.</param>
    public string? TownStockGivenUp(uint wcid) =>
        _townStockFailed.TryGetValue(wcid, out var f) && f.Stops >= TownStockTries ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{f.Why}; {f.Stops} stops failed, the bound {TownStockTries}") : null;

    /// <summary>M6 14 T: remembers that the stop for <paramref name="wcid"/> failed, by name (the first failure stands; one stop counts once, whatever failed in it).</summary>
    /// <param name="wcid">The ware's weenie class id.</param>
    /// <param name="why">The failure's name and words.</param>
    public void TownStockFailed(uint wcid, string why)
    {
        ArgumentNullException.ThrowIfNull(why);
        if (!_townStockFailed.TryGetValue(wcid, out var f))
        {
            _townStockFailed[wcid] = (why, _townStockStop, 1);
        }
        else if (f.Stop != _townStockStop)
        {
            _townStockFailed[wcid] = (f.Why, _townStockStop, f.Stops + 1);
        }
    }

    /// <summary>M6 14 T: true the first time this run that <paramref name="token"/> is said of the ware <paramref name="wcid"/> (a line held back for the purse is said once, not on every way out).</summary>
    /// <param name="wcid">The ware's weenie class id.</param>
    /// <param name="token">What is said.</param>
    public bool TownStockSayOnce(uint wcid, string token) => _townStockSaid.Add((wcid, token));

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

    /// <summary>
    /// M6 item 4 t2, F1 (4-R2): records that the primary tie holds ground <paramref name="ground"/>'s portal: a Primary Portal Tie ACE confirmed ("You have successfully linked with the portal."), or a Primary Portal Recall that landed in the
    /// ground's arrival. The primary tie is one slot (ACE keeps one <c>LinkedPortalOneDID</c>, @95ef426d WorldObject_Magic.cs:1105-1115, and its recall teleports there without 0x04A3), so every other ground with a tie record is known
    /// <see cref="PortalTie.Untied"/>: each ground this journey knew a tie of, and each catalog ground with a tie record (<paramref name="tieRecords"/>, by default <see cref="GroundCatalog.All"/>'s grounds with a <see cref="HuntingGround.Portal"/>);
    /// a recall to one of them would land at the slot's ground, so the travel rule plans the route (whose tie leg moves the slot) instead.
    /// </summary>
    /// <param name="ground">The ground's key.</param>
    /// <param name="tieRecords">The keys of the grounds with a tie record, or null for the catalog's.</param>
    public void SetPrimaryPortalTie(string ground, IEnumerable<string>? tieRecords = null)
    {
        ArgumentNullException.ThrowIfNull(ground);
        var others = _portalTies.Keys.Concat(tieRecords ?? GroundCatalog.All.Where(g => g.Portal is not null).Select(g => g.Key)).Where(k => !string.Equals(k, ground, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var other in others)
        {
            _portalTies[other] = PortalTie.Untied;
        }

        _portalTies[ground] = PortalTie.Tied;
        PrimaryPortalSlot = ground;
    }

    /// <summary>M6 4 F1 (4-R2): the ground whose portal the primary tie is known to hold (<see cref="SetPrimaryPortalTie"/>), or null when the slot is not known; the tie memory stores this one slot.</summary>
    public string? PrimaryPortalSlot { get; private set; }

    private readonly Dictionary<string, HashSet<string>> _failedRoutes = new(StringComparer.Ordinal);

    /// <summary>M6 7 M: the keys of a ground's routes whose rung change failed on a hop this run (the next rung change plans them last, <see cref="Travel.TravelRoutes.FirstAdmitted"/>'s <c>avoid</c>); empty when none.</summary>
    /// <param name="ground">The ground's key.</param>
    public IReadOnlyCollection<string> FailedRoutesOf(string ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        return _failedRoutes.TryGetValue(ground, out var set) ? set : [];
    }

    /// <summary>M6 7 M: remembers that <paramref name="route"/> of <paramref name="ground"/> failed on a hop (kept for the run, not in the checkpoint).</summary>
    /// <param name="ground">The ground's key.</param>
    /// <param name="route">The route's key.</param>
    public void RouteFailed(string ground, string route)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(route);
        if (!_failedRoutes.TryGetValue(ground, out var set))
        {
            _failedRoutes[ground] = set = new HashSet<string>(StringComparer.Ordinal);
        }

        set.Add(route);
    }

    /// <summary>
    /// M6 4 F1 (4-R2): what the journey knows of <paramref name="ground"/>'s portal tie: its own fact (<see cref="PortalTieOf"/>) when known; else, when the slot (<see cref="PrimaryPortalSlot"/>) holds another ground and this ground has
    /// a tie record (<see cref="HuntingGround.Portal"/>) or a tieable entry, <see cref="PortalTie.Untied"/> (the one slot holds the other); else <see cref="PortalTie.Unknown"/>.
    /// </summary>
    /// <param name="ground">The ground.</param>
    public PortalTie TieOf(HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        var own = PortalTieOf(ground.Key);
        if (own != PortalTie.Unknown)
        {
            return own;
        }

        return PrimaryPortalSlot is { } slot && !string.Equals(slot, ground.Key, StringComparison.Ordinal) && (ground.Portal is not null || ground.Entry is { Tieable: true }) ? PortalTie.Untied : PortalTie.Unknown;
    }

    /// <summary>
    /// M6 4 F1 (4-R2): the ground among <paramref name="grounds"/> (other than <paramref name="target"/>) whose arrival landblock holds <paramref name="cell"/>: a Primary Portal Recall meant for <paramref name="target"/> that lands there found the
    /// one slot holding that ground (<c>recall:portal:slot-holds:&lt;ground&gt;</c>); null when none does.
    /// </summary>
    /// <param name="cell">Where the recall landed.</param>
    /// <param name="target">The ground the recall was meant for.</param>
    /// <param name="grounds">The known grounds (the catalog, or a test's own).</param>
    public static HuntingGround? SlotHolderAt(uint cell, HuntingGround target, IEnumerable<HuntingGround> grounds)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(grounds);
        return grounds.FirstOrDefault(g => !string.Equals(g.Key, target.Key, StringComparison.Ordinal) && g.ArrivalLandblock() == cell >> 16);
    }

    /// <summary>The failure's name of a Primary Portal Recall that landed in <paramref name="holder"/>'s arrival (4-R2).</summary>
    /// <param name="holder">The ground the slot holds.</param>
    public static string SlotHoldsFailure(HuntingGround holder) => "recall:portal:slot-holds:" + (holder ?? throw new ArgumentNullException(nameof(holder))).Key;

    /// <summary>The ground keys whose tie is known (tied or untied), with the tie: the verdict lines and the tests read it.</summary>
    public IReadOnlyDictionary<string, PortalTie> PortalTies => _portalTies;

    /// <summary>
    /// M6 7 AV (live, 2026-11-06: Lifestone Recall refused 0x0400, YouDontHaveAllTheComponents, three times, and the run stopped 180 m from town): true once ACE refused a Lifestone Recall for a missing component. For the rest of the run the recall counts as not available
    /// (<c>recall-unavailable:components</c>): the way home is planned without it (portals or foot), and the trip that follows buys the components. Never set by another refusal.
    /// </summary>
    public bool LifestoneRecallUnavailable { get; set; }

    /// <summary>
    /// M6, the recall's components (<see cref="RecallReadiness"/>): the login name the server hashes for the account's spell formula, set once by the autopilot at its start (the name its buff options carry, M6 1 w), so every way-home
    /// decision of the run can judge Lifestone Recall's components on the account's full formula when the pack holds no Item Enchantment focus. Null: unknown (a plain journey, a host that gave none): a school without its focus is not
    /// judged, and the cast's own refusal sets <see cref="LifestoneRecallUnavailable"/>. Hashed, never logged.
    /// </summary>
    public string? FormulaAccount { get; set; }

    /// <summary>True once Lifestone Tie on Holtburg's lifestone succeeded or a Lifestone Recall arrived; false after a 0x049E; null when unknown.</summary>
    public bool? LifestoneTied { get; set; }

    /// <summary>
    /// M5 4 a3 (<c>m5-autopilot-death-unscripted</c>): the death tally the autopilot has kept running since its start, offered to the death station when a death the autopilot did not script has been seen. The death station takes it (and sets this back to null),
    /// enters at the respawn instead of at the lie-down, and the handler reads the corpse's facts from it. Null for every other death.
    /// </summary>
    public DeathTally? ObservedDeath { get; set; }

    /// <summary>True once the character bound (used) Holtburg's lifestone this journey (its respawn point, ACE's Sanctuary).</summary>
    public bool Bound { get; set; }

    /// <summary>
    /// M6 6 FXD1 (6-D5): the cell and point of the lifestone the bind station used, recorded at its Use when ACE said the spirit was attuned (ACE sets Sanctuary there, @95ef426d <c>Lifestone.cs</c>:44-76; the death sends the character to
    /// it, <c>Player_Death.cs</c>:250); null until a bind in this journey. The death station classifies the respawn against it. A Lifestone Tie never sets it: the tie changes Lifestone Recall's target, not where death sends one
    /// (<c>WorldObject_Magic.cs</c>:966-975).
    /// </summary>
    public Lifestone.Data.World.CellFrame? BoundAt { get; set; }

    /// <summary>
    /// M6 6 F2: the run's worn memory (the autopilot owns one a run), handed to the death station as <see cref="DeathOptions.Worn"/>. Null: the death handler's own default.
    /// </summary>
    public Mage.WornMemory? Worn { get; set; }

    /// <summary>
    /// M6 8 E (<c>m6-minutes-stop-safe-point</c>): true once the run's minutes stop rule is due; the autopilot sets it (its own clock), a station reads it at a safe point inside its errand (the logistics trip, at home before the way back). Null: no autopilot, never due.
    /// The minutes rule is offered, and since M6 item 14 a pending stop request too (a pending stop starts nothing new, so no way back is begun under one; the autopilot logs which of the two answered): what the level rule does is not changed.
    /// </summary>
    public Func<bool>? MinutesStopDue { get; set; }

    /// <summary>
    /// M6 audit (finding 9, the owner's decision of 2026-11-19): at the logistics trip's end at home, the reason the buff pass must run here before the way back (more lines to cast than the ladder's
    /// <c>pass_home_over_lines</c>), else null; the autopilot sets it for the level up job. Live 2026-11-19, runs 2 to 4 at level 9: the trip learned 43 to 45 spells, walked 456 to 461 m back to the Redoubt, read
    /// "5 line(s) to cast ... home first" there and recalled home to cast them (3 m 38 s to 4 m 28 s and the first walk lost, every run).
    /// </summary>
    public Func<IGameSessionView, string?>? PassAtHomeDue { get; set; }

    /// <summary>
    /// M6 item 14 (D125): true while the run's stop at the buff bot applies (the autopilot's option is on, the job is the level up job; <c>AutopilotScenario.BuffBot.cs</c>): the logistics trip then stops at the bot at home,
    /// after the errand and before the way back, when a line of its profile is due. Null (a plain journey): no stop.
    /// </summary>
    public Func<bool>? BuffBotOn { get; set; }

    /// <summary>
    /// M6 14 (<c>m6-caster-spend</c>): the casters the climber holds short of a wield requirement, scored as if wielded, and her measured rate (<see cref="Mage.CasterSpendFacts"/>): the trip's experience spend buys the points of the best one worth it
    /// first, as the level-up spend does (<see cref="Mage.XpSpendRule"/>'s <c>casterFacts</c>). The autopilot's level up job sets it; null (a plain journey): no caster is read.
    /// </summary>
    public Func<Mage.CasterSpendFacts?>? CasterSpend { get; set; }

    /// <summary>
    /// M6 14 (<c>m6-lore-spend</c>): the pieces the climber holds whose spells her Arcane Lore alone keeps off, with the wear pick's verdict at their ItemDifficulty (<see cref="Mage.LoreSpendFacts"/>): the trip's experience spend raises the lore toward the
    /// lowest one that pays, as the level-up spend does. The autopilot's level up job sets it; null (a plain journey): no lore is read.
    /// </summary>
    public Func<Mage.LoreSpendFacts?>? LoreSpend { get; set; }

    /// <summary>
    /// M6 14 (<c>m6-lore-spend</c>): the resupply's last wear pick, with the terms of her at that visit (<c>ResupplyRun.WithWearTerms</c>): what the Arcane Lore rule judges her held pieces with between visits (the level-up spend in the field).
    /// Null until a visit's gear phase has run.
    /// </summary>
    public Mage.SelfEquipJudge? WearJudge { get; set; }

    /// <summary>
    /// M6 14 own lore: what the fight's loot step calls when a find or a worn piece waits on her Arcane Lore (<c>FightScenario.LoreWanted</c>): the autopilot's level up job starts its experience spend then, whose Arcane Lore rule raises
    /// the lore toward it. Null (a plain journey): nothing is asked.
    /// </summary>
    public Action<string>? LoreWanted { get; set; }

    /// <summary>
    /// M6 item 14 (the owner, 2026-11-12: she says <c>all</c>): the profile the run asks the buff bot for, which every stop at the bot tells by its key when its spec names no word (<see cref="BuffBotStation"/>) and every read of
    /// the bot's lines reads (the trip's stop, the death's, the Pathwarden's). The autopilot and the headless journey set it from the option (<c>--autopilot-buff-bot-word</c>); a plain journey has the default,
    /// <see cref="Lifestone.ServerTools.BuffBotWire.Default"/>.
    /// </summary>
    public Lifestone.ServerTools.BuffBotProfile BuffBotProfile { get; set; } = Lifestone.ServerTools.BuffBotWire.Default;

    /// <summary>
    /// M6 item 14 (the den run, the owner, 2026-11-12): while set, the road is run only: no walk of a travel leg stops for an attacker, none fights back and none retreats (<c>TravelLeg.AttackerStopsWalk</c>), the autopilot's survival
    /// layer and vitals guard hold nothing (<c>AutopilotScenario.SurvivalFrame</c>), and the one thing done for her health is a drink on the move. The value is the reason in words, for the lines. The den-run station sets it for its road
    /// and clears it at the den's door and at its end; nothing else sets it.
    /// </summary>
    public string? RunOnly { get; set; }

    /// <summary>
    /// M6 item 14 (the den run): the travel lines an order adds to the learn plan, read first (<c>ScrivenerLearnPlan.LinesFor</c>): Primary Portal Tie and Primary Portal Recall while the den run is ordered, else none.
    /// </summary>
    public IReadOnlyList<string> LearnTravelLines { get; set; } = [];

    /// <summary>
    /// M6 14 (the owner, 2026-11-16: she buys every spell, eventually when affordable): true while the run's trips learn every spell she can cast beyond the spell plan (<see cref="Mage.EverySpellPlan"/>). The autopilot sets it from
    /// <c>--autopilot-every-spell</c> for the level up job; a plain journey keeps it off.
    /// </summary>
    public bool EverySpell { get; set; }

    /// <summary>M6 14 (the every-spell rule): true while the run's villa track runs, so the rule's reserve holds the villa's price (<see cref="Mage.EverySpellPlan.ReserveOf"/>). The autopilot sets it.</summary>
    public bool EverySpellVilla { get; set; }

    /// <summary>
    /// M6 item 14 (the den run, the owner's order of 2026-11-13: sell first and turn the coin into notes before the run): true while an order wants the trip to leave no coin above nothing. The trip's purse then buys notes down to a
    /// floor of 0 (a death takes half the coin and no trade note: ACE Player_Death.cs:484-502 @95ef426d, the notes are Bonded), and makes no town trip for scrolls (a scroll bought now changes a note back into loose coin).
    /// </summary>
    public bool CoinToNotes { get; set; }

    /// <summary>
    /// M6 item 14 (the den run's rehearsal, after the first run was called off on 2026-11-13 with nothing of it ever tried before): true while the order is a rehearsal (<c>--autopilot-den-run-rehearse</c>). The den-run station then
    /// runs its road only as far as <c>DenRunStation.RehearseBeforeEdge</c>'s portal, ends its run-only walk <c>DenRunStation.RehearseShortMetres</c> short of it, recalls home and stops <c>den-run:rehearsed</c> with what it measured.
    /// </summary>
    public bool DenRunRehearse { get; set; }

    /// <summary>
    /// M6 16 Q8 (the one fight-back, part 2): true while the quest steps' kill and their walks' defence run their own old loop (<c>--autopilot-quest-own-kill-loop on</c>), the fallback kept for the first live gate of the skirmish on the
    /// errands; false (the default): the casting and the staying alive are the skirmish's (<c>QuestStepLeg.Skirmish.cs</c>). The autopilot sets it.
    /// </summary>
    public bool QuestOwnKillLoop { get; set; }

    /// <summary>
    /// M6 item 14 (the trial switch): the run's trial of a later rung's ground (<see cref="Autopilot.LadderTrial"/>), or null. The death handler's corpse run reads it (<see cref="Autopilot.RungOwner.CorpseRun"/>): a corpse on the
    /// trial's ground, where she was let in, is walked back to when the trial's gate reads open after the rebuff. The autopilot sets it; nothing else does.
    /// </summary>
    public Autopilot.LadderTrial? Trial { get; set; }

    /// <summary>
    /// M6 sprint (the owner's decision of 2026-11-18: the vitae burnt first, the corpse after; <see cref="Autopilot.VitaeBurn"/>): asked once by the death handler before the corpse trip's route, after the re-arm and the re-buff: the reason the corpse is owed instead of
    /// fetched now (the vitae is burnt on the rung below first), or null to fetch it now. The autopilot of the ladder sets it; nothing else does.
    /// </summary>
    public Func<IGameSessionView, Mage.DeathFacts, double, string?>? DeferCorpse { get; set; }

    /// <summary>M6 sprint: her level at the death of an owed corpse taken back after a vitae burn (ACE's rot time is the level at the death's), or null: the handler reads her level now.</summary>
    public int? CorpseRotLevel { get; set; }

    /// <summary>
    /// M6 14 retreat attackers (rule 4; live 2026-11-14 10:59:16 UTC: the run leg's retreat found the mana at 49, under Lifestone Recall's 50, after the vitals guard's heals spent it down in the fight-back): the mana a travel leg's
    /// fight-back holds back for its way out (Lifestone Recall's cost plus one heal's, <see cref="Vitals.ManaWayOut"/>'s line), or null when no fight-back is under way. The leg sets it at a fight-back's start and clears it at its end;
    /// the autopilot's vitals guard reads it so that no heal it casts during the fight-back takes the mana under the recall's cost (<see cref="Vitals.ManaWayOut.Spend"/>, the heal's arm).
    /// </summary>
    public Vitals.WayOutLine? FightBackReserve { get; set; }

    /// <summary>
    /// M6 14 walk drains (the owner, 2026-11-15: the target's health and kind are known data): the travel leg's fight-back target with its health as data (<see cref="Vitals.KnownTargetHealth"/>), or null when no fight-back is under way or its
    /// facts are not held. The leg sets it at each target and clears it at the fight-back's end; the autopilot's vitals guard offers Drain Health Other on it.
    /// </summary>
    public Vitals.KnownTargetHealth? FightBackTarget { get; set; }

    private readonly Dictionary<string, string> _approachAbandoned = new(StringComparer.Ordinal);

    /// <summary>
    /// M6 14 approach cost (rule 2; the owner, 2026-11-14: she should recall and go back to the previous ground): why a rung change's run leg gave the crossing to a ground up this run (<see cref="Vitals.ApproachCost.Measured"/>), or null.
    /// The autopilot's way out reads it (<c>approach-abandoned:&lt;rung&gt;</c>), so the same crossing is never tried again in the same run; the trial and the corpse trip read the same verdict.
    /// </summary>
    /// <param name="groundKey">The ground's key.</param>
    public string? ApproachAbandonedOf(string groundKey) => _approachAbandoned.TryGetValue(groundKey, out var why) ? why : null;

    /// <summary>M6 14 approach cost (rule 2): the run leg gives the crossing to <paramref name="groundKey"/> up for the rest of the run, with <paramref name="why"/>; the first reason stands.</summary>
    /// <param name="groundKey">The ground's key.</param>
    /// <param name="why">The measured numbers in words.</param>
    public void AbandonApproach(string groundKey, string why) => _approachAbandoned.TryAdd(groundKey, why);
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

    /// <summary>The travel seam the stations' scenarios use at their choke points. M6 4 F1 (4-R1): a <see cref="JourneyTravel"/> not yet attached to a context is attached to this one (<see cref="JourneyTravel.Context"/>), so its Ground and Home
    /// purposes can run the rung change by the travel rule.</summary>
    public required IJourneyTravel Travel
    {
        get => _travel;
        init
        {
            _travel = value;
            if (value is JourneyTravel { Context: null } journey)
            {
                journey.Context = this;
            }
        }
    }

    private readonly IJourneyTravel _travel = null!;

    /// <summary>M6 4 F1 (4-R3): the grounds the travel reads to find the ground whose landblocks hold the body (<see cref="RungRoute.BodyGroundOf"/>); the catalog's by default, a test's own otherwise.</summary>
    public IReadOnlyList<HuntingGround> Grounds { get; init; } = GroundCatalog.All;

    /// <summary>
    /// M6 4 F1 (4-R1): this context with <paramref name="ground"/> as its <see cref="Dungeon"/>, every other member the same (the same facts, travel and routes): the rung change out to a ground the journey's travel enters that is not the
    /// journey's own.
    /// </summary>
    /// <param name="ground">The ground.</param>
    public JourneyContext WithDungeon(HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        return ReferenceEquals(ground, Dungeon) ? this : new JourneyContext
        {
            Character = Character,
            Account = Account,
            Log = Log,
            Travel = Travel,
            Spells = Spells,
            Keeper = Keeper,
            Dats = Dats,
            AttackTimes = AttackTimes,
            Runners = Runners,
            Observer = Observer,
            Facts = Facts,
            Dungeon = ground,
            Routes = Routes,
            WindowFrames = WindowFrames,
            Grounds = Grounds,
            Quests = Quests,
            QuestStore = QuestStore,
            _questNote = _questNote,
        };
    }

    /// <summary>The spell table (<c>SpellTableData.Spells</c>).</summary>
    public required IReadOnlyDictionary<uint, SpellInfo> Spells { get; init; }

    /// <summary>The vitals keeper, or null when the journey runs none.</summary>
    public VitalsKeeper? Keeper { get; init; }

    /// <summary>The dats (the hunt's follower and the walker need them), or null in a test without dats.</summary>
    public DatSet? Dats { get; init; }

    /// <summary>M6 14 run past (D137): the creatures' paces the travel legs read, or null: the dats' (<see cref="Vitals.CreaturePaces.For"/>, none without dats). A test gives its own.</summary>
    public Vitals.CreaturePaces? Paces { get; init; }

    /// <summary>M6 14 attack timing: the creatures' attack timings the travel legs read, or null: the dats' (<see cref="Vitals.CreatureAttackTimes.For"/>, the named fallback without dats). A test gives its own.</summary>
    public Vitals.CreatureAttackTimes? AttackTimes { get; init; }

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

    /// <summary>The travel routes (M6 item 4 D50, <c>Session/Travel/travel-routes.json</c>): the portal edges, the grounds' routes and the ways home the rung-change station composes (t2); a test may pass its own.</summary>
    public TravelRoutes Routes { get; init; } = TravelRoutes.Default;

    /// <summary>M6 5 Q2: the character's quest timers (<see cref="QuestTimers"/>), loaded once from <see cref="QuestStore"/> by <see cref="JourneyScenario.NewContext"/>; null when the host keeps none (then <see cref="CompleteQuest"/> keeps them in memory).</summary>
    public QuestTimers? Quests { get; init; }

    /// <summary>M6 5 Q2: the store behind <see cref="Quests"/> (<c>quests/&lt;checkpoint file name&gt;</c>, <see cref="QuestTimers.FileName"/>), or null when the host has none.</summary>
    public IAutopilotCheckpointStore? QuestStore { get; init; }

    private QuestNote _questNote = new();

    /// <summary>The timers a station reads and records into: <see cref="Quests"/>, else this run's in-memory ones (the TODO is logged at the first completion).</summary>
    public QuestTimers QuestTimersInUse => Quests ?? (_questNote.Memory ??= new QuestTimers(Character));

    /// <summary>
    /// M6 5 Q2 (closes <c>m6-pathwarden-timers-save</c>): records a quest's completion: <see cref="QuestTimers.Complete"/>, then <see cref="QuestTimers.Save"/> through <see cref="QuestStore"/>, then logs <see cref="QuestTimers.Line"/> (the check after: the line reads the recorded entry).
    /// With no store it logs <c>TODO(m6-quest-timers-no-store)</c> once per run and keeps the timers in memory. A write that fails is logged by name and thrown (the station stops; the entry stays in memory).
    /// </summary>
    /// <param name="quest">The quest.</param>
    /// <param name="at">When the completion was seen (UTC).</param>
    /// <param name="how">How it was learned (<see cref="QuestTimers.HowReply"/>, <see cref="QuestTimers.HowItem"/> or <see cref="QuestTimers.HowServerStamp"/>).</param>
    public QuestTimerEntry CompleteQuest(Quest quest, DateTimeOffset at, string how)
    {
        ArgumentNullException.ThrowIfNull(quest);
        var timers = QuestTimersInUse;
        var entry = timers.Complete(quest, at, how);
        if (QuestStore is { } store)
        {
            try
            {
                timers.Save(store);
            }
            catch (IOException e)
            {
                Log.Warn("quests", $"TODO(m6-quest-timers-save-failed): quest {quest.Key} is complete but {store.Describe} could not be written ({e.Message}); the entry stays in memory");
                throw;
            }
        }
        else if (!_questNote.NoStoreLogged)
        {
            _questNote.NoStoreLogged = true;
            Log.Warn("quests", "TODO(m6-quest-timers-no-store): this host keeps no quest timers file; completions are kept in memory for this run only");
        }

        Log.Info("quests", $"{timers.Line(quest, at)} (completions {entry.Completions}; saved={(QuestStore is null ? "no" : "yes")})");
        return entry;
    }

    /// <summary>
    /// Binds the quest timers to the character in the world (<see cref="QuestTimers.BindTo"/>), once the run knows her guid: the check before any quest rule
    /// reads them. The file is named for the character's name, which a character deleted and created again keeps, so another guid's records are another
    /// character's: they are dropped, each named in the log, and the file is saved without them. Records under no guid (a file written before the guid was
    /// kept) are kept and named <c>TODO(m6-quest-timers-unowned)</c>. Calling it again for the same guid does nothing.
    /// </summary>
    /// <param name="characterGuid">The guid of the character in the world (0: unknown, nothing is done).</param>
    public void BindQuestTimers(uint characterGuid)
    {
        var timers = QuestTimersInUse;
        var was = timers.CharacterGuid;
        var bind = timers.BindTo(characterGuid);
        if (timers.CharacterGuid == was)
        {
            return;
        }

        if (bind is not null)
        {
            var records = string.Join(", ", bind.Entries.Select(e => string.Create(CultureInfo.InvariantCulture, $"{e.Key} (last {e.LastCompleted:yyyy-MM-dd'T'HH:mm:ss'Z'}, {e.How})")));
            Log.Warn("quests", bind.Dropped
                ? string.Create(CultureInfo.InvariantCulture, $"quest timers: check before: the file's records are another character's (guid 0x{bind.FileGuid:X8} in the file, 0x{characterGuid:X8} now: {Character} re-created under the same name); {bind.Entries.Count} record(s) dropped, so each quest reads as never done: {records}")
                : string.Create(CultureInfo.InvariantCulture, $"TODO(m6-quest-timers-unowned): the quest timers file names no character guid (written before the guid was kept); its {bind.Entries.Count} record(s) are kept as {Character}'s (0x{characterGuid:X8}): {records}; if she was re-created since, move the file aside"));
        }

        if (QuestStore is { } store)
        {
            try
            {
                timers.Save(store);
            }
            catch (IOException e)
            {
                Log.Warn("quests", $"TODO(m6-quest-timers-save-failed): the quest timers are bound to this character in memory but {store.Describe} could not be written ({e.Message})");
            }
        }

        Log.Info("quests", string.Create(CultureInfo.InvariantCulture, $"quest timers: check after: bound to {Character} 0x{timers.CharacterGuid:X8}, {timers.Entries.Count} record(s), saved={(QuestStore is null ? "no" : "yes")}"));
    }

    /// <summary>What the contexts of one run share: the in-memory timers and the one-time no-store note.</summary>
    private sealed class QuestNote
    {
        public QuestTimers? Memory { get; set; }

        public bool NoStoreLogged { get; set; }
    }

    /// <summary>The window frame times of the running station (M4 4.2 J1: the Godot host's <see cref="JourneyWindowSamplers"/>), or null when the host has no frame clock (headless: the station lines print <c>n/a</c>).</summary>
    public IJourneyWindowFrames? WindowFrames { get; init; }
}
