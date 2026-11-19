using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.World;
using Lifestone.Data.World;
using AutopilotTravelRule = Lifestone.Client.Session.Autopilot.AutopilotTravelRule;
using TravelDecision = Lifestone.Client.Session.Autopilot.TravelDecision;
using TravelWay = Lifestone.Client.Session.Autopilot.TravelWay;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>The mod's place verb as the journey issues it (default <see cref="JourneyTravel.ToolPlace"/>: <c>Tools.Place</c> from the host); tests script it.</summary>
/// <param name="session">The session.</param>
/// <param name="destination">Where to.</param>
/// <returns>The place's handle (Confirmed when the teleport started).</returns>
public delegate ActionHandle JourneyPlace(IGameSessionView session, TeleportDestination destination);

/// <summary>
/// The journeys' places (M4 P5b 3.4): Holtburg (the lifestone's landblock 0xA9B4), the dungeon's arrival (the ground's profile), the belt camp (cell 0xA5B00032)
/// and the forest (cell 0x9FB50039), the ground cells the W0 runs used; and whether the player stands at one.
/// </summary>
public static class JourneyGrounds
{
    /// <summary>Holtburg's landblock (the lifestone 0xA9B40019).</summary>
    public const uint HoltburgLandblock = RecallHome.HoltburgLandblock;

    /// <summary>Holtburg's town position (ACViewer's Locations.txt, <see cref="WorldLocations.Holtburg"/>): the pre-journey recovery's place.</summary>
    public static TeleportDestination Holtburg { get; } = new("Holtburg", [], WorldLocations.Holtburg, "journey: Holtburg (WorldLocations.Holtburg)");

    /// <summary>The dungeon ground (any dungeon profile of <see cref="GroundCatalog"/>) whose landblocks hold <paramref name="cell"/>, or null. Allocates nothing.</summary>
    /// <param name="cell">The full cell id.</param>
    public static HuntingGround? DungeonOf(uint cell)
    {
        var all = GroundCatalog.All;
        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].Kind == GroundKind.Dungeon && all[i].InLandblocks(cell))
            {
                return all[i];
            }
        }

        return null;
    }

    /// <summary>
    /// The dungeon ground whose landblocks hold <paramref name="cell"/>, the journey's own ground first (M5 3 p1, closes the first half of 2-R12 <c>journey-portal-per-ground</c>): <paramref name="journeyGround"/> when it is a dungeon and holds the cell, else (a journey
    /// built without a ground, or a cell in another ground's landblocks) the catalog's search of <see cref="DungeonOf(uint)"/>. Allocates nothing.
    /// </summary>
    /// <param name="cell">The full cell id.</param>
    /// <param name="journeyGround">The journey's dungeon ground (<see cref="JourneyContext.Dungeon"/>), or null.</param>
    public static HuntingGround? DungeonOf(uint cell, HuntingGround? journeyGround) =>
        journeyGround is { Kind: GroundKind.Dungeon } own && own.InLandblocks(cell) ? own : DungeonOf(cell);

    /// <summary>The belt camp's cell (the Low B row's first, 0xA5B00032: W0's wayfind target).</summary>
    public static HuntCell BeltCell => HuntGround.LowB.Cells[0];

    /// <summary>The forest's cell (the Low North Forest Mix row's first, 0x9FB50039).</summary>
    public static HuntCell ForestCell => HuntGround.LowNorthForestMix.Cells[0];

    /// <summary>
    /// The place for <paramref name="where"/>: Holtburg, the den, or the ground cell's place point (its centre, half a metre over the terrain, as the hunt places);
    /// null for <see cref="StationWhere.Here"/> or a ground cell whose terrain height <paramref name="height"/> cannot give (no dats).
    /// </summary>
    /// <param name="where">Where.</param>
    /// <param name="height">The terrain height at (cell, x, y) in landblock-local metres (<c>HuntGround.SeekHeight</c> over the dats), or null.</param>
    /// <param name="dungeon">The journey's dungeon ground (<see cref="JourneyContext.Dungeon"/>, <c>--fight-ground</c>): <see cref="StationWhere.Den"/> is its arrival (the dungeon's budgeted place when no portal is tied). Required for the den, never read for the other places.</param>
    public static TeleportDestination? For(StationWhere where, Func<uint, float, float, float?>? height, HuntingGround? dungeon = null)
    {
        switch (where)
        {
            case StationWhere.Holtburg:
                return Holtburg;
            case StationWhere.Den:
                return DungeonOrThrow(dungeon).ArrivalDestination();
            case StationWhere.Belt:
            case StationWhere.Forest:
                var cell = where == StationWhere.Belt ? BeltCell : ForestCell;
                if (height?.Invoke(cell.CellId, cell.X, cell.Y) is not { } h)
                {
                    return null;
                }

                var name = (where == StationWhere.Belt ? "the belt camp 0x" : "the forest 0x") + cell.CellId.ToString("X8", CultureInfo.InvariantCulture);
                return new TeleportDestination(name, [], new WorldLocation(name, new CellPosition(cell.CellId, HuntGround.PlacePoint(cell, h)), true, 0), "journey ground (HuntGround)");
            default:
                return null;
        }
    }

    /// <summary>True when <paramref name="position"/> is inside a building or dungeon: an EnvCell, whose cell index (the low word of the cell id) is at least 0x100.</summary>
    /// <param name="position">The player's position, or null (unknown: false).</param>
    public static bool Indoors(CellFrame? position) => position is { } at && (at.CellId & 0xFFFF) >= 0x100;

    private static HuntingGround DungeonOrThrow(HuntingGround? dungeon) =>
        dungeon ?? throw new ArgumentNullException(nameof(dungeon), "the den is the journey's dungeon ground (JourneyContext.Dungeon, --fight-ground): a caller that names StationWhere.Den passes it (TODO(journey-ground-option) closed by M5 item 2 k2)");

    /// <summary>True when <paramref name="position"/> is at <paramref name="where"/>: Holtburg's landblock, a landblock of the journey's dungeon ground (<paramref name="dungeon"/>), a landblock of the belt's or the forest's hunt cells; always for Here.</summary>
    /// <param name="where">Where.</param>
    /// <param name="position">The player's position, or null (unknown: false, except for Here).</param>
    /// <param name="dungeon">The journey's dungeon ground (<see cref="JourneyContext.Dungeon"/>): the den is this ground's landblocks. Required for the den, never read for the other places.</param>
    public static bool At(StationWhere where, CellFrame? position, HuntingGround? dungeon = null)
    {
        if (where == StationWhere.Here)
        {
            return true;
        }

        if (position is not { } at)
        {
            return false;
        }

        var landblock = at.CellId >> 16;
        return where switch
        {
            StationWhere.Holtburg => landblock == HoltburgLandblock,
            StationWhere.Den => DungeonOrThrow(dungeon).InLandblocks(at.CellId),
            StationWhere.Belt => HuntGround.LowB.Cells.Any(c => c.CellId >> 16 == landblock),
            StationWhere.Forest => HuntGround.LowNorthForestMix.Cells.Any(c => c.CellId >> 16 == landblock),
            _ => false,
        };
    }
}

/// <summary>
/// The journey's travel seam (M4 P5b 3.4, lanes.md contract 1). The means follow the purpose:
/// <list type="bullet">
/// <item><b>Ground</b>: none when the player already stands within the destination's tolerance (its landblock and within <see cref="ToleranceMetres"/>; its
/// landblock for a dungeon ground); to a dungeon ground (the journey's own, <see cref="JourneyContext.Dungeon"/>) by the ground's Primary Portal Recall (48) unless <see cref="JourneyFacts.PortalTieOf"/> says untied: tied is a recall, unknown is a recall TRIED (a fresh
/// character after the build is tied, but no journey can read the link; ACE's 0x04A3 answer sets untied), untied fails <c>travel:ground:untied</c> (named <c>TODO(m6-first-tie-walk)</c>) and so does the way to the ground's portal front: the journey makes no place to a
/// dungeon or its portal (D50: the build ties, the owner's cheat layer is build time only); to the belt or the forest the budgeted place.</item>
/// <item><b>Home</b>: none in the destination's landblock (the journey's home town), else Lifestone Recall (1635) through <see cref="RecallHome.ForCharacter"/> (the fighters over their caster swap);
/// counted in <see cref="JourneyFacts.Recalls"/>; <see cref="JourneyFacts.LifestoneTied"/> follows the outcome.</item>
/// <item><b>Seek, Cell, Vendor, Corpse, Spot, Bind</b>: on foot through the host's runner, the request passed unchanged (an object goal, <see cref="TravelRequest.ObjectGuid"/> not 0, is
/// built by the host from the object, its range and its sight; the destination is then a hint); with no runner the leg fails <c>travel:run:no-runner</c>.</item>
/// <item>For any purpose, an object goal without sight is none when the player already stands within its range of the object's current position.</item>
/// <item>M6 4 F1 (4-R1, closes the journey half of <c>m6-first-tie-walk</c>): with a context (<see cref="Context"/>), a Ground travel into a dungeon ground and a Home travel ask the autopilot's own travel rule first
/// (<see cref="AutopilotTravelRule.Out"/>, <see cref="AutopilotTravelRule.Home"/>: one rule for both sides, decided before the act, never chosen by a failure); where it says <c>rung-change</c> the leg is <see cref="RungChangeStation"/>
/// (out to the ground, or home by portals), its verdict line logged and its <c>fail=</c> the leg's failure (<c>travel:rung-change:&lt;fail&gt;</c>). A Primary Portal Recall that lands in another known ground's arrival fails
/// <c>travel:recall:portal:slot-holds:&lt;ground&gt;</c> and records that the one slot holds that ground (4-R2).</item>
/// </list>
/// A place is counted in <see cref="JourneyFacts.Teleports"/> when it is issued; one past <see cref="JourneyFacts.TeleportBudget"/> is refused loudly: the leg fails
/// <c>travel:place:over-budget</c> and a WARN line (never an ERROR: the refusal is the rule working) names the request. The constants are Lifestone's own.
/// </summary>
public sealed partial class JourneyTravel : IJourneyTravel
{
    /// <summary>The log category of the travel's lines.</summary>
    public const string LogCategory = "journey";

    /// <summary>A ground destination counts as reached within this distance in its landblock (Lifestone's own).</summary>
    public const double ToleranceMetres = 5.0;

    private readonly string _character;
    private readonly JourneyFacts _facts;
    private readonly ILogSink _log;
    private readonly IJourneyRunnerHost? _runners;
    private readonly JourneyPlace _place;
    private readonly HuntingGround? _dungeon;
    private Leg? _current;
    private Leg? _rung;
    private Leg? _heldWalk; // M6 8 K: the walk the survival layer paused

    /// <summary>The journey's travel for <paramref name="character"/>.</summary>
    /// <param name="character">The character (picks the recall: <see cref="RecallHome.ForCharacter"/>).</param>
    /// <param name="facts">The journey's facts (the budget, the counters, the ties); the same object as the context's.</param>
    /// <param name="log">Where the travel logs.</param>
    /// <param name="runners">The host's runner, or null (every run purpose then fails <c>travel:run:no-runner</c>).</param>
    /// <param name="place">The place verb, or null for <see cref="ToolPlace"/>.</param>
    /// <param name="dungeon">The journey's dungeon ground (<see cref="JourneyContext.Dungeon"/>): the ground a Ground travel into its landblocks is a recall to, and whose tie is read; null finds the ground by the cell in the catalog (<see cref="JourneyGrounds.DungeonOf(uint)"/>).</param>
    public JourneyTravel(string character, JourneyFacts facts, ILogSink log, IJourneyRunnerHost? runners = null, JourneyPlace? place = null, HuntingGround? dungeon = null)
    {
        _dungeon = dungeon;
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _facts = facts ?? throw new ArgumentNullException(nameof(facts));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _runners = runners;
        _place = place ?? ToolPlace;
    }

    /// <summary>
    /// M6 7 K (round 1c: <c>travel:recall:no-caster</c> on a Throwaway with Lifestone Recall in its book): the journey's reading of who casts a recall. <see cref="RecallHome.ForCharacter"/> goes by the name (the Brawler, the Archer, BSD Mage, Winnie), so a
    /// character it does not name was no caster whatever its book held; the book decides now: a character no fighter template names, whose read spellbook holds <paramref name="spellId"/>, wields its own caster (no swap), as the mage does. The Hunter
    /// (a template with no magic) and a book without the spell stay null, the named <c>travel:recall:no-caster</c>. The climber was never in this gap: its name is on the list.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="character">The character's name.</param>
    /// <param name="log">The log.</param>
    /// <param name="homeLandblock">The lifestone's landblock (or the tied portal destination's).</param>
    /// <param name="spellId">The recall spell.</param>
    public static RecallHome? RecallFor(IGameSessionView session, string? character, ILogSink log, uint homeLandblock = RecallHome.HoltburgLandblock, uint spellId = RecallHome.LifestoneRecallSpell)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (RecallHome.ForCharacter(session, character, log, homeLandblock, spellId) is { } named)
        {
            return named;
        }

        if (FighterTemplate.ForName(character) is not null || session.State.Self.Spellbook is not { IsKnown: true } book || !book.Knows(spellId))
        {
            return null;
        }

        log.Info(LogCategory, $"journey travel: '{character}' is no named journey caster, and its spellbook holds {RecallHome.SpellName(spellId)}: it casts the recall with the caster it wields");
        return new RecallHome(session, null, log, homeLandblock, spellId);
    }

    /// <summary>
    /// M6 4 F1 (4-R1): the journey's context, set when the context is built over this travel (<see cref="JourneyContext.Travel"/>), else null: the rung change needs it (the routes, the grounds, the station's legs), and without it the
    /// Ground and Home purposes keep the recalls.
    /// </summary>
    public JourneyContext? Context { get; internal set; }

    /// <inheritdoc />
    public bool Busy => _current is { Done: false } || _rung is { Done: false };

    /// <inheritdoc />
    public void HoldWalk()
    {
        if (_current is { Done: false } walk)
        {
            walk.Pause();
            _heldWalk = walk;
        }
    }

    /// <inheritdoc />
    public void ReleaseWalk(double now)
    {
        if (_heldWalk is { Done: false } walk)
        {
            walk.Resume(now);
        }

        _heldWalk = null;
    }

    /// <summary>The places issued (counted in <see cref="JourneyFacts.Teleports"/> too).</summary>
    public int Places { get; private set; }

    /// <summary>The places refused over the budget.</summary>
    public int Refused { get; private set; }

    /// <summary>The runs that ended (arrived or failed).</summary>
    public int Runs { get; private set; }

    /// <summary>The metres of the runs' routes (their legs' <c>route_m=</c>), summed: the done line's <c>route_m=</c>.</summary>
    public double RouteMetres { get; private set; }

    /// <summary>The seconds the runs took (their legs' <c>run_s=</c>), summed: the done line's <c>run_s=</c>.</summary>
    public double RunSeconds { get; private set; }

    /// <summary>The ladder's rungs the runs climbed (their legs' <c>rungs=</c>), summed: the done line's <c>rungs=</c> (report-only; M4 P5b fix FA, P5b-R6).</summary>
    public int Rungs { get; private set; }

    /// <summary>The runs that ended Blocked (their legs' <c>blocked=</c>), summed: the done line's <c>blocked=</c> (report-only).</summary>
    public int Blocked { get; private set; }

    /// <summary>The runs that ended at their range without the clear line they needed (<see cref="ITravelLeg.ArrivedWithoutSight"/>): the done line's <c>sightless=</c> (report-only, P5b-R6).</summary>
    public int Sightless { get; private set; }

    /// <summary>The default place verb: <c>Tools.Place</c> from the host.</summary>
    /// <param name="session">The session.</param>
    /// <param name="destination">Where to.</param>
    public static ActionHandle ToolPlace(IGameSessionView session, TeleportDestination destination)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.Tools.Place(destination, ActionSource.Host);
    }

    /// <inheritdoc />
    public ITravelLeg GoTo(IGameSessionView session, TravelRequest request, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        _current = Start(session, request, now);
        return _current;
    }

    private Leg Start(IGameSessionView session, TravelRequest request, double now)
    {
        var destination = request.Destination;
        if (request.ObjectGuid != 0 && WithinObjectRange(session, request))
        {
            return None(request);
        }

        switch (request.Purpose)
        {
            case TravelPurpose.Home:
                var homeLandblock = destination.Location.Position.CellId >> 16; // the journey's home town: the destination's, never a constant (M5 3 p1, 2-R10)
                if (InLandblock(session, homeLandblock))
                {
                    return None(request);
                }

                var wayHome = _dungeon is null ? null : WayHomeRule.Choose(_dungeon, session.State.Self.Spellbook);
                var near = session.State.Self.Position is { } stands && _dungeon is not null && WayHomeRule.StandsNear(_dungeon, stands.CellId);
                if (wayHome is { Kind: WayHomeKind.Walk } && near)
                {
                    return WalkHome(session, request, _dungeon!, wayHome, now);
                }

                if (HomeByPortals(session, request, homeLandblock) is { } byPortals)
                {
                    return byPortals; // M6 4 F1 (4-R1): the book lacks Lifestone Recall (D43), or (M6 sprint) she cannot cast it, and a way home by portals is known
                }

                if (wayHome is { Kind: WayHomeKind.Walk })
                {
                    // M6 sprint: not near home and no way by portals: a walk across the world is no way home; named, never planned
                    _log.Warn(LogCategory, $"journey travel: REFUSED the walk home to {destination.Name}: she stands at {Where(session)}, not in {_dungeon!.Home.Town}'s landblock or {_dungeon.Key}'s, and no way home by portals is known: travel:home:not-near");
                    return new FixedLeg(TravelMeans.None, "travel:home:not-near"); // a refusal: nothing moves
                }

                if (wayHome is { Kind: WayHomeKind.Recall } && RecallReadiness.Of(session.State, _facts, null) is { Castable: false } cannot) // the book, the run's refusal and the tie (the components are the travel rule's, read with the spell table before this leg)
                {
                    // M6 sprint: the recall is the way left, and it cannot be cast: named before the cast, not by the server's refusal
                    _log.Warn(LogCategory, $"journey travel: REFUSED Lifestone Recall home: {cannot.WhyNot}, and no way home by portals is known from {Where(session)}: travel:recall:not-castable");
                    return new FixedLeg(TravelMeans.Recall, "travel:recall:not-castable");
                }

                if (wayHome is { Kind: not WayHomeKind.Recall })
                {
                    // No way but a recall that the book lacks: the recall is still tried so the failure is the recall's own by name (travel:recall:spell), and the rule's reason is logged beside it.
                    _log.Warn(LogCategory, $"journey travel: no way home but a recall that the spellbook lacks: {wayHome.Why}");
                }

                _log.Info(LogCategory, $"journey travel: home by Lifestone Recall ({destination.Name})");
                return new RecallLeg(this, RecallFor(session, _character, _log, homeLandblock), home: true, ground: null);

            case TravelPurpose.Ground:
                if (request.ObjectGuid == 0 && AlreadyThere(session, destination))
                {
                    return None(request);
                }

                if (GroundOf(destination.Location.Position.CellId) is { } dungeon)
                {
                    if (Decide(session, dungeon, RungDirection.Out) is { Way: TravelWay.RungChange } routed)
                    {
                        return RungChange(session, request, dungeon, RungDirection.Out, routed); // M6 4 F1 (4-R1): a routed ground (untieable, or its tie not known tied): the rung change, never a recall tried
                    }

                    return ToDungeon(session, request, dungeon);
                }

                if (PortalFrontOf(destination) is { } front)
                {
                    return Untied(request, front, "the way to its portal's front");
                }

                if (CharacterPolicy.For(_character).CheatFree)
                {
                    return ClimberGround(session, request, now);
                }

                if (_facts.TeleportBudget == 0 && _dungeon is { } own && WayHomeRule.HomeOnFoot(own) && _runners is not null)
                {
                    return ClimberGround(session, request, now); // M6 7 H: a journey with no place in its budget (a cheat-free stand-in for the climber, LowRungDeathLiveTests) reaches an outdoor ground on foot as the climber does, not by a refused place
                }

                return Place(session, request);

            default:
                if (_runners is null)
                {
                    _log.Warn(LogCategory, $"journey travel: no runner for the {request.Purpose} travel to '{destination.Name}' (the host gave none): travel:run:no-runner");
                    return new FixedLeg(TravelMeans.Run, "travel:run:no-runner");
                }

                return new RunLeg(this, _runners.Run(session, request, now));
        }
    }

    /// <summary>
    /// The way home on foot (M6 item 0 F2, closes <c>m6-trip-way-home</c>): a character whose read spellbook lacks Lifestone Recall, on an outdoor ground hunted from home on foot (<see cref="WayHomeRule"/>), walks to the home's lifestone point
    /// through the host's runner (<see cref="TravelPurpose.Bind"/>, a point goal), as the final recall's walk does. Check, act, check (M5 D141): the purpose and where the player stands are logged first; none when the player already stands within
    /// <see cref="ToleranceMetres"/> of the point; the leg ends Arrived only when the player then stands in the point's landblock within the tolerance, else it fails <c>travel:home:arrival-off-point</c>.
    /// </summary>
    private Leg WalkHome(IGameSessionView session, TravelRequest request, HuntingGround ground, WayHomeChoice way, double now)
    {
        var to = new CellPosition(ground.Home.LifestoneCell, ground.Home.LifestonePoint);
        var point = new TeleportDestination("home-lifestone-point", [], new WorldLocation("home-lifestone-point", to, HasHeight: true, HeadingDegrees: 0), "journey: home on foot (WayHomeRule)");
        var from = session.State.Self.Position;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"journey travel: home on foot: check before: purpose: the way home ({request.Purpose}) to {ground.Home.Town}'s lifestone point as a Bind point goal ({way.Why}); the player stands at {(from is { } f ? $"cell 0x{f.CellId:X8} ({f.Frame.Origin.X:0.#}, {f.Frame.Origin.Y:0.#})" : "an unknown place")}, the point is cell 0x{to.CellId:X8} ({to.Local.X:0.#}, {to.Local.Y:0.#})"));
        if (AlreadyThere(session, point))
        {
            return None(new TravelRequest(point, TravelPurpose.Bind));
        }

        if (_runners is null)
        {
            _log.Warn(LogCategory, "journey travel: no runner for the walk home (the host gave none): travel:run:no-runner");
            return new FixedLeg(TravelMeans.Run, "travel:run:no-runner");
        }

        var walk = new TravelRequest(point, TravelPurpose.Bind);
        return new RunLeg(this, _runners.Run(session, walk, now), s => ArrivedAt(s, point)
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"travel:home:arrival-off-point: the walk home ended at {(s.State.Self.Position is { } q ? $"cell 0x{q.CellId:X8} ({q.Frame.Origin.X:0.#}, {q.Frame.Origin.Y:0.#})" : "an unknown place")}, not within {ToleranceMetres:0.#} m of ({to.Local.X:0.#}, {to.Local.Y:0.#}) in cell 0x{to.CellId:X8}"));
    }

    /// <summary>
    /// A Ground travel the climber cannot make by a recall or the tie (M6 0 F3, item 3: the climber is cheat-free, so <c>place=0</c>): to an outdoor ground hunted from home on foot (<see cref="WayHomeRule"/>) it is a walk, a point goal through the host's runner
    /// (<see cref="TravelPurpose.Cell"/>), check, act, check (the leg ends Arrived only when the player stands in the point's landblock within <see cref="ToleranceMetres"/>, else <c>travel:ground:arrival-off-point</c>); anywhere else it is a loud stop by name,
    /// <c>travel:ground:climber-no-place</c>, never a place (<c>TODO(m6-first-tie-walk)</c>).
    /// </summary>
    private Leg ClimberGround(IGameSessionView session, TravelRequest request, double now)
    {
        var destination = request.Destination;
        var to = destination.Location.Position;
        var from = session.State.Self.Position;
        if (_dungeon is null || !WayHomeRule.HomeOnFoot(_dungeon) || _runners is null)
        {
            const string failure = "travel:ground:climber-no-place";
            _log.Warn(LogCategory, $"journey travel: REFUSED the {request.Purpose} travel to '{destination.Name}': the climber is never placed (place=0), and {(_dungeon is null ? "the journey has no ground" : _runners is null ? "the host gave no runner" : $"{_dungeon.Key} is not an outdoor ground hunted from {_dungeon.Home.Town} on foot")}: {failure} (TODO(m6-first-tie-walk))");
            return new FixedLeg(TravelMeans.None, failure);
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"journey travel: ground on foot: check before: purpose: {request.Purpose} to '{destination.Name}' (no place for the climber); the player stands at {(from is { } f ? $"cell 0x{f.CellId:X8} ({f.Frame.Origin.X:0.#}, {f.Frame.Origin.Y:0.#})" : "an unknown place")}, the point is cell 0x{to.CellId:X8} ({to.Local.X:0.#}, {to.Local.Y:0.#})"));
        var walk = new TravelRequest(destination, TravelPurpose.Cell);
        return new RunLeg(this, _runners.Run(session, walk, now), s => ArrivedAt(s, destination)
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"travel:ground:arrival-off-point: the walk ended at {(s.State.Self.Position is { } q ? $"cell 0x{q.CellId:X8} ({q.Frame.Origin.X:0.#}, {q.Frame.Origin.Y:0.#})" : "an unknown place")}, not within {ToleranceMetres:0.#} m of ({to.Local.X:0.#}, {to.Local.Y:0.#}) in cell 0x{to.CellId:X8}"));
    }

    private static bool ArrivedAt(IGameSessionView session, TeleportDestination destination)
    {
        var to = destination.Location.Position;
        return session.State.Self.Position is { } at && at.CellId >> 16 == to.CellId >> 16
            && Vector2.Distance(new Vector2(at.Frame.Origin.X, at.Frame.Origin.Y), new Vector2(to.Local.X, to.Local.Y)) <= ToleranceMetres;
    }

    /// <summary>The ground a Ground travel to <paramref name="cell"/> enters: the journey's own, else the catalog's (<see cref="JourneyGrounds.DungeonOf(uint, HuntingGround?)"/>).</summary>
    private HuntingGround? GroundOf(uint cell) => JourneyGrounds.DungeonOf(cell, _dungeon);

    /// <summary>The dungeon ground whose portal's front cell is the destination's cell (the untied mage's way to its portal), or null.</summary>
    private HuntingGround? PortalFrontOf(TeleportDestination destination)
    {
        var cell = destination.Location.Position.CellId;
        if (_dungeon is { Kind: GroundKind.Dungeon, Portal: { } own })
        {
            return own.FrontCell == cell ? _dungeon : null;
        }

        var all = GroundCatalog.All;
        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].Kind == GroundKind.Dungeon && all[i].Portal is { } p && p.FrontCell == cell)
            {
                return all[i];
            }
        }

        return null;
    }

    /// <summary>
    /// M6 4 F1 (4-R1): the travel rule's decision for <paramref name="ground"/> in <paramref name="direction"/> (<see cref="AutopilotTravelRule"/>, the autopilot's Travel state's own), logged on one line; null without a context or a ground
    /// (the recalls as before).
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="ground">The ground.</param>
    /// <param name="direction">Out to the ground, or home.</param>
    public TravelDecision? Decide(IGameSessionView session, HuntingGround? ground, RungDirection direction)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Context is not { } context || ground is null)
        {
            return null;
        }

        var state = session.State;
        var input = AutopilotTravelRule.Input(state, ground, _facts, context.Routes, direction, context.Grounds, context.Spells);
        var bookRead = AutopilotTravelRule.BookRead(state.Self.Spellbook);
        var decided = direction == RungDirection.Home ? AutopilotTravelRule.Home(input, bookRead, context.Routes) : AutopilotTravelRule.Out(input, bookRead, context.Routes);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"journey travel: the travel rule: direction={(direction == RungDirection.Home ? "home" : "out")} way={decided.WayName} ground={ground.Key} place={input.Place} where={input.Where.ToString().ToLowerInvariant()}{(input.BodyGround is { } body ? " body_ground=" + body.Key : string.Empty)} level={input.Level} tie={input.Tie.ToString().ToLowerInvariant()} lifestone_recall={(input.KnowsLifestoneRecall ? "yes" : "no")} route={decided.Route ?? "n/a"}; why: {decided.Reason}"));
        return decided;
    }

    /// <summary>
    /// M6 4 F1 (4-R1): the way home by portals when the travel rule says so (<see cref="AutopilotTravelRule.Home"/>: a read book without Lifestone Recall and a way home known), and the home is the travel file's start (where that way ends); else null
    /// (the recall).
    /// </summary>
    private Leg? HomeByPortals(IGameSessionView session, TravelRequest request, uint homeLandblock)
    {
        if (Context is not { } context || context.Routes.PlaceOf(RungRoute.Hex(homeLandblock))?.Key != context.Routes.StartPlace)
        {
            return null;
        }

        var ground = _dungeon ?? context.Dungeon;
        return Decide(session, ground, RungDirection.Home) is { Way: TravelWay.RungChange } decided ? RungChange(session, request, ground, RungDirection.Home, decided) : null;
    }

    /// <summary>The rung change as the leg (M6 4 F1, 4-R1): <see cref="RungChangeStation"/> in <paramref name="direction"/> over the context, its Dungeon <paramref name="ground"/>.</summary>
    private Leg RungChange(IGameSessionView session, TravelRequest request, HuntingGround ground, RungDirection direction, TravelDecision decided)
    {
        var context = Context!.WithDungeon(ground);
        _log.Info(LogCategory, $"journey travel: {request.Purpose} to '{request.Destination.Name}' by the rung change direction={(direction == RungDirection.Home ? "home" : "out")} (route {decided.Route ?? "n/a"}): check before: the player stands at {Where(session)}");
        _rung = new RungChangeLeg(this, new RungChangeStation(direction).Start(context), direction == RungDirection.Home ? "home" : "out:" + ground.Key); // the station's own walks go through this travel too (GoTo), so Busy reads both
        return _rung;
    }

    /// <summary>
    /// M6 6 C2: the corpse trip's way to the travel place that holds a corpse lost on a leg (the island, Holtburg, the hub): the rung change out to <paramref name="ground"/> cut at <paramref name="placeKey"/>
    /// (<see cref="RungChangeStation"/> with <c>stop_at=</c>, <see cref="RungRouteInput.StopAtPlace"/>), its verdict logged and its <c>fail=</c> the leg's failure (<c>travel:rung-change:&lt;fail&gt;</c>); the station checks after that the
    /// player stands in the place. With no context (no travel file) the leg fails <c>travel:place:no-context</c>, named.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="ground">The rung's ground (whose route the leg was on).</param>
    /// <param name="placeKey">The travel place's key.</param>
    /// <param name="now">The session clock.</param>
    public ITravelLeg ToPlace(IGameSessionView session, HuntingGround ground, string placeKey, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentException.ThrowIfNullOrEmpty(placeKey);
        if (Context is not { } context)
        {
            const string failure = "travel:place:no-context";
            _log.Warn(LogCategory, $"journey travel: to the corpse's place {placeKey}: this travel has no journey context (no travel file to plan by): {failure}");
            _current = new FixedLeg(TravelMeans.None, failure);
            return _current;
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"journey travel: to the corpse's place {placeKey}: the rung change out to {ground.Key} cut at {placeKey} (stop_at={placeKey}) at {now:0.#} s; check before: the player stands at {Where(session)}"));
        _rung = new RungChangeLeg(this, new RungChangeStation(RungDirection.Out, stopAt: placeKey).Start(context.WithDungeon(ground)), "out:" + ground.Key + " stop_at=" + placeKey);
        _current = _rung;
        return _rung;
    }

    private static string Where(IGameSessionView session) =>
        session.State.Self.Position is { } at ? string.Create(CultureInfo.InvariantCulture, $"cell 0x{at.CellId:X8} ({at.Frame.Origin.X:0.#}, {at.Frame.Origin.Y:0.#})") : "an unknown place";

    private Leg ToDungeon(IGameSessionView session, TravelRequest request, HuntingGround dungeon)
    {
        var tie = _facts.TieOf(dungeon);
        if (tie == PortalTie.Untied)
        {
            return Untied(request, dungeon, "the way to its arrival");
        }

        _log.Info(
            LogCategory,
            tie == PortalTie.Tied
                ? $"journey travel: to {request.Destination.Name} by Primary Portal Recall (the {dungeon.Key} portal is tied)"
                : $"journey travel: to {request.Destination.Name} by Primary Portal Recall TRIED (the {dungeon.Key} tie is not known: a build ties it, but no journey can read the link from the player description; ACE's 0x04A3 answer sets untied)");
        return new RecallLeg(this, RecallFor(session, _character, _log, dungeon.ArrivalLandblock(), dungeon.Portal?.RecallSpell ?? RecallHome.PrimaryPortalRecallSpell), home: false, ground: dungeon.Key, target: dungeon);
    }

    /// <summary>The refusal of a way a character not tied to the ground's portal has none for: no place (D50), the first tie's walk is M6's.</summary>
    private Leg Untied(TravelRequest request, HuntingGround dungeon, string what)
    {
        _log.Warn(
            LogCategory,
            $"journey travel: REFUSED the {request.Purpose} travel to '{request.Destination.Name}' ({what}): the {dungeon.Key} portal is not tied, and a mage with no tie reaches a portal by the first tie's walk, not a place (TODO(m6-first-tie-walk)): {UntiedFailure}");
        return new FixedLeg(TravelMeans.None, UntiedFailure);
    }

    private Leg None(TravelRequest request)
    {
        _log.Info(LogCategory, $"journey travel: already at '{request.Destination.Name}' ({request.Purpose}): nothing to do");
        return new FixedLeg(TravelMeans.None, null);
    }

    private Leg Place(IGameSessionView session, TravelRequest request)
    {
        var name = request.Destination.Name;
        if (_facts.Teleports >= _facts.TeleportBudget)
        {
            Refused++;
            _log.Warn(
                LogCategory,
                string.Create(CultureInfo.InvariantCulture, $"journey travel: REFUSED the place to '{name}' ({request.Purpose}): the budget of {_facts.TeleportBudget} teleport(s) is spent ({_facts.Teleports} made): travel:place:over-budget"));
            return new FixedLeg(TravelMeans.Place, "travel:place:over-budget");
        }

        _facts.Teleports++;
        Places++;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"journey travel: place to '{name}' ({request.Purpose}), teleport {_facts.Teleports} of {_facts.TeleportBudget}"));
        return new PlaceLeg(_place(session, request.Destination), name);
    }

    /// <summary>The failure of a Ground travel that needs a tie the character does not have.</summary>
    public const string UntiedFailure = "travel:ground:untied";

    private static bool InLandblock(IGameSessionView session, uint landblock) =>
        session.State.Self.Position is { } at && at.CellId >> 16 == landblock;

    /// <summary>
    /// An object goal (lane J2a's note: the destination is then only a hint; the host's runner builds the goal from the whole request) is reached when
    /// the player stands within its range of the object's current position in the same landblock. A goal that needs sight is never decided here (the
    /// runner reads the line of sight), nor an object with no position.
    /// </summary>
    private static bool WithinObjectRange(IGameSessionView session, TravelRequest request)
    {
        if (request.NeedSight || request.Range <= 0 || session.State.Self.Position is not { } at
            || !session.State.Objects.TryGet(request.ObjectGuid, out var o) || o.Position is not { } it || at.CellId >> 16 != it.CellId >> 16)
        {
            return false;
        }

        return Vector3.Distance(at.Frame.Origin, it.Frame.Origin) <= request.Range;
    }

    private bool AlreadyThere(IGameSessionView session, TeleportDestination destination)
    {
        if (session.State.Self.Position is not { } at)
        {
            return false;
        }

        var to = destination.Location.Position;
        if (at.CellId >> 16 != to.CellId >> 16)
        {
            return false;
        }

        if (GroundOf(to.CellId) is not null)
        {
            return true;
        }

        var o = at.Frame.Origin;
        return Vector2.Distance(new Vector2(o.X, o.Y), new Vector2(to.Local.X, to.Local.Y)) <= ToleranceMetres;
    }

    private void NoteRun(string fields, bool sightless)
    {
        Runs++;
        RouteMetres += Number(RouteField().Match(fields));
        RunSeconds += Number(RunField().Match(fields));
        Rungs += (int)Number(RungsField().Match(fields));
        Blocked += (int)Number(BlockedField().Match(fields));
        Sightless += sightless ? 1 : 0;
    }

    private static double Number(Match m) =>
        m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    [GeneratedRegex(@"(?:^|\s)route_m=([0-9.]+)")]
    private static partial Regex RouteField();

    [GeneratedRegex(@"(?:^|\s)run_s=([0-9.]+)")]
    private static partial Regex RunField();

    [GeneratedRegex(@"(?:^|\s)rungs=([0-9]+)")]
    private static partial Regex RungsField();

    [GeneratedRegex(@"(?:^|\s)blocked=([0-9]+)")]
    private static partial Regex BlockedField();

    private abstract class Leg : ITravelLeg
    {
        public bool Done { get; protected set; }

        public abstract TravelMeans Means { get; }

        public string? Failure { get; protected set; }

        public virtual string Fields => string.Empty;

        public virtual bool ArrivedWithoutSight => false;

        public abstract TravelStatus Step(IGameSessionView session, double now);

        public virtual void Pause()
        {
        }

        public virtual void Resume(double now)
        {
        }

        public virtual void Dispose() => Done = true;
    }

    /// <summary>A leg that ended when it began: none (arrived) or a refusal (failed).</summary>
    private sealed class FixedLeg : Leg
    {
        private readonly TravelMeans _means;

        public FixedLeg(TravelMeans means, string? failure)
        {
            _means = means;
            Failure = failure;
            Done = true;
        }

        public override TravelMeans Means => _means;

        public override TravelStatus Step(IGameSessionView session, double now) => Failure is null ? TravelStatus.Arrived : TravelStatus.Failed;
    }

    private sealed class PlaceLeg(ActionHandle handle, string name) : Leg
    {
        public override TravelMeans Means => TravelMeans.Place;

        public override TravelStatus Step(IGameSessionView session, double now)
        {
            if (Failure is not null)
            {
                return TravelStatus.Failed;
            }

            if (handle.Outcome is not { } outcome)
            {
                return TravelStatus.Running;
            }

            Done = true;
            if (outcome.Kind == ActionOutcomeKind.Confirmed)
            {
                return TravelStatus.Arrived;
            }

            Failure = $"travel:place:{name}: {outcome.Kind}" + (outcome.Code == 0 ? string.Empty : $" (code 0x{outcome.Code:X})") + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail);
            return TravelStatus.Failed;
        }
    }

    private sealed class RecallLeg(JourneyTravel owner, RecallHome? recall, bool home, string? ground, HuntingGround? target = null) : Leg
    {
        private bool _begun;
        private TravelStatus _end = TravelStatus.Running;

        public override TravelMeans Means => TravelMeans.Recall;

        public override string Fields => recall?.Fields ?? string.Empty;

        public override TravelStatus Step(IGameSessionView session, double now)
        {
            if (_end != TravelStatus.Running)
            {
                return _end;
            }

            if (recall is null)
            {
                owner._log.Warn(LogCategory, $"journey travel: '{owner._character}' has no recall (not a journey caster): travel:recall:no-caster");
                return End(TravelStatus.Failed, "travel:recall:no-caster");
            }

            if (!_begun)
            {
                _begun = true;
                recall.Begin(now);
            }

            switch (recall.Step(now))
            {
                case RecallStatus.Done:
                    owner._facts.Recalls++;
                    if (home)
                    {
                        owner._facts.LifestoneTied = true;
                    }
                    else if (ground is not null)
                    {
                        owner._facts.SetPrimaryPortalTie(ground); // M6 4 F1 (4-R2): the recall landed in the ground's arrival, so the one slot holds it
                    }

                    return End(TravelStatus.Arrived, null);
                case RecallStatus.Failed:
                    if (recall.Refusal == 0x049E)
                    {
                        owner._facts.LifestoneTied = false;
                    }
                    else if (recall.Refusal == 0x04A3 && ground is not null)
                    {
                        owner._facts.SetPortalTie(ground, PortalTie.Untied);
                        owner._log.Warn(LogCategory, $"journey travel: the recall to {ground} was refused untied (WeenieError 0x04A3 YouMustLinkToPortalToRecall): the tie is untied; the next travel there is the travel rule's (the rung change for a routed ground, else the first tie's walk: TODO(m6-first-tie-walk))");
                    }
                    else if (target is not null && session.State.Self.Position is { } landed && JourneyFacts.SlotHolderAt(landed.CellId, target, owner.Context?.Grounds ?? GroundCatalog.All) is { } holder)
                    {
                        // M6 4 F1 (4-R2): no 0x04A3: ACE teleported to the portal the one slot holds, another known ground's arrival.
                        owner._facts.SetPrimaryPortalTie(holder.Key);
                        var slot = JourneyFacts.SlotHoldsFailure(holder);
                        owner._log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"journey travel: the recall to {target.Key} landed in cell 0x{landed.CellId:X8}, {holder.Key}'s arrival: the primary tie's one slot holds {holder.Key}, so {target.Key} is untied: {slot}"));
                        return End(TravelStatus.Failed, "travel:" + slot);
                    }

                    if (home && recall.SpellId == RecallHome.LifestoneRecallSpell && recall.RefusedForComponents)
                    {
                        // M6, the recall's components (live 2026-11-18 20:29:54 UTC: this leg's cast was refused 0x0400 and the fact stayed unset until the recall-home station was refused too, 50 s later): the seam names it as that
                        // station does (M6 7 AV), so the caller's recovery and every later way-home decision of the run leave the recall out.
                        owner._facts.LifestoneRecallUnavailable = true;
                        owner._log.Info(LogCategory, "journey travel: recall-unavailable:components: ACE refused Lifestone Recall for a missing component (0x0400): the recall counts as not available for the rest of this run");
                    }

                    var step = recall.Failure is { } f && f.StartsWith("recall:", StringComparison.Ordinal) ? f["recall:".Length..] : recall.Failure ?? "unknown";
                    return End(TravelStatus.Failed, "travel:recall:" + step + (recall.Detail is null ? string.Empty : ": " + recall.Detail));
                default:
                    return TravelStatus.Running;
            }
        }

        public override void Dispose()
        {
            Done = true;
            recall?.Dispose();
        }

        private TravelStatus End(TravelStatus status, string? failure)
        {
            _end = status;
            Failure = failure;
            Done = true;
            return status;
        }
    }

    /// <summary>M6 4 F1 (4-R1): the rung change run as a travel leg: Arrived when the station ends Succeeded and ok, else Failed <c>travel:rung-change:&lt;fail&gt;</c> (the verdict's <c>fail=</c>); the verdict is logged on one line.</summary>
    private sealed class RungChangeLeg(JourneyTravel owner, IStationRun run, string what) : Leg
    {
        private TravelStatus _end = TravelStatus.Running;

        public override TravelMeans Means => TravelMeans.Run;

        public override string Fields => run.VerdictFields();

        public override TravelStatus Step(IGameSessionView session, double now)
        {
            if (_end != TravelStatus.Running)
            {
                return _end;
            }

            var status = run.Step(session, now);
            if (status == ScenarioStatus.Running)
            {
                return TravelStatus.Running;
            }

            Done = true;
            var fields = run.VerdictFields();
            owner._log.Info(LogCategory, $"journey travel: the rung change ({what}) ended {status}: {fields}");
            if (status == ScenarioStatus.Succeeded && run.Ok)
            {
                _end = TravelStatus.Arrived;
                return _end;
            }

            var fail = fields.Split(' ').FirstOrDefault(f => f.StartsWith("fail=", StringComparison.Ordinal))?["fail=".Length..] is { Length: > 0 } f && f != "none" ? f : JourneyVerdict.Token(run.Failure ?? status.ToString());
            Failure = "travel:rung-change:" + fail + (run.Failure is null ? string.Empty : ": " + run.Failure);
            _end = TravelStatus.Failed;
            return _end;
        }
    }

    private sealed class RunLeg(JourneyTravel owner, ITravelLeg inner, Func<IGameSessionView, string?>? arrival = null) : Leg
    {
        public override TravelMeans Means => TravelMeans.Run;

        public override string Fields => inner.Fields;

        public override bool ArrivedWithoutSight => inner.ArrivedWithoutSight;

        public override TravelStatus Step(IGameSessionView session, double now)
        {
            var status = inner.Step(session, now);
            if (status == TravelStatus.Arrived && Failure is not null)
            {
                return TravelStatus.Failed; // the arrival check after the walk failed (a later step reads the same answer)
            }

            if (status != TravelStatus.Running && !Done)
            {
                Done = true;
                Failure = inner.Failure ?? (status == TravelStatus.Failed ? "travel:run:failed" : null);
                if (status == TravelStatus.Arrived && arrival?.Invoke(session) is { } off)
                {
                    Failure = off;
                    status = TravelStatus.Failed;
                    owner._log.Warn(LogCategory, "journey travel: " + off);
                }

                owner.NoteRun(inner.Fields, status == TravelStatus.Failed && inner.ArrivedWithoutSight);
            }

            return status;
        }

        public override void Pause() => inner.Pause();

        public override void Resume(double now) => inner.Resume(now);

        public override void Dispose()
        {
            Done = true;
            inner.Dispose();
        }
    }
}
