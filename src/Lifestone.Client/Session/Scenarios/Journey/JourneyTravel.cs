using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>The mod's place verb as the journey issues it (default <see cref="JourneyTravel.ToolPlace"/>: <c>Tools.Place</c> from the host); tests script it.</summary>
/// <param name="session">The session.</param>
/// <param name="destination">Where to.</param>
/// <returns>The place's handle (Confirmed when the teleport started).</returns>
public delegate ActionHandle JourneyPlace(IGameSessionView session, TeleportDestination destination);

/// <summary>
/// The journeys' places (M4 P5b 3.4): Holtburg (the lifestone's landblock 0xA9B4), the den's arrival (landblock 0x0105), the belt camp (cell 0xA5B00032)
/// and the forest (cell 0x9FB50039), the ground cells the W0 runs used; and whether the player stands at one.
/// </summary>
public static class JourneyGrounds
{
    /// <summary>Holtburg's landblock (the lifestone 0xA9B40019).</summary>
    public const uint HoltburgLandblock = RecallHome.HoltburgLandblock;

    /// <summary>The den's landblock (the den portal's destination).</summary>
    public const uint DenLandblock = RecallHome.DenLandblock;

    /// <summary>Holtburg's town position (ACViewer's Locations.txt, <see cref="WorldLocations.Holtburg"/>): the pre-journey recovery's place.</summary>
    public static TeleportDestination Holtburg { get; } = new("Holtburg", [], WorldLocations.Holtburg, "journey: Holtburg (WorldLocations.Holtburg)");

    /// <summary>The den's arrival (<see cref="BlackSpawnDen.Arrival"/>, cell 0x01050375): the den's budgeted place when no portal is tied.</summary>
    public static TeleportDestination Den { get; } = new(
        "Black Spawn Den",
        [],
        new WorldLocation("Black Spawn Den", new CellPosition(BlackSpawnDen.Arrival.CellId, BlackSpawnDen.Arrival.Frame.Origin), true, WorldLocations.HeadingFromQuaternion(BlackSpawnDen.Arrival.Frame.Orientation.W, BlackSpawnDen.Arrival.Frame.Orientation.Z)),
        "journey: the den portal's destination (BlackSpawnDen.Arrival)");

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
    public static TeleportDestination? For(StationWhere where, Func<uint, float, float, float?>? height)
    {
        switch (where)
        {
            case StationWhere.Holtburg:
                return Holtburg;
            case StationWhere.Den:
                return Den;
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

    /// <summary>True when <paramref name="position"/> is at <paramref name="where"/>: Holtburg's or the den's landblock, a landblock of the belt's or the forest's hunt cells; always for Here.</summary>
    /// <param name="where">Where.</param>
    /// <param name="position">The player's position, or null (unknown: false, except for Here).</param>
    public static bool At(StationWhere where, CellFrame? position)
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
            StationWhere.Den => landblock == DenLandblock,
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
/// landblock for the den); to the den by Primary Portal Recall (48) when <see cref="JourneyFacts.PortalTied"/> is true, else the budgeted place; to the belt or the
/// forest the budgeted place.</item>
/// <item><b>Home</b>: none in Holtburg's landblock, else Lifestone Recall (1635) through <see cref="RecallHome.ForCharacter"/> (the fighters over their caster swap);
/// counted in <see cref="JourneyFacts.Recalls"/>; <see cref="JourneyFacts.LifestoneTied"/> follows the outcome.</item>
/// <item><b>Seek, Cell, Vendor, Corpse, Spot, Bind</b>: on foot through the host's runner, the request passed unchanged (an object goal, <see cref="TravelRequest.ObjectGuid"/> not 0, is
/// built by the host from the object, its range and its sight; the destination is then a hint); with no runner the leg fails <c>travel:run:no-runner</c>.</item>
/// <item>For any purpose, an object goal without sight is none when the player already stands within its range of the object's current position.</item>
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
    private Leg? _current;

    /// <summary>The journey's travel for <paramref name="character"/>.</summary>
    /// <param name="character">The character (picks the recall: <see cref="RecallHome.ForCharacter"/>).</param>
    /// <param name="facts">The journey's facts (the budget, the counters, the ties); the same object as the context's.</param>
    /// <param name="log">Where the travel logs.</param>
    /// <param name="runners">The host's runner, or null (every run purpose then fails <c>travel:run:no-runner</c>).</param>
    /// <param name="place">The place verb, or null for <see cref="ToolPlace"/>.</param>
    public JourneyTravel(string character, JourneyFacts facts, ILogSink log, IJourneyRunnerHost? runners = null, JourneyPlace? place = null)
    {
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _facts = facts ?? throw new ArgumentNullException(nameof(facts));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _runners = runners;
        _place = place ?? ToolPlace;
    }

    /// <inheritdoc />
    public bool Busy => _current is { Done: false };

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
        var landblock = destination.Location.Position.CellId >> 16;
        if (request.ObjectGuid != 0 && WithinObjectRange(session, request))
        {
            return None(request);
        }

        switch (request.Purpose)
        {
            case TravelPurpose.Home:
                if (InLandblock(session, JourneyGrounds.HoltburgLandblock))
                {
                    return None(request);
                }

                _log.Info(LogCategory, $"journey travel: home by Lifestone Recall ({destination.Name})");
                return new RecallLeg(this, RecallHome.ForCharacter(session, _character, _log), home: true);

            case TravelPurpose.Ground:
                if (request.ObjectGuid == 0 && AlreadyThere(session, destination))
                {
                    return None(request);
                }

                if (landblock == JourneyGrounds.DenLandblock && _facts.PortalTied == true)
                {
                    _log.Info(LogCategory, $"journey travel: to {destination.Name} by Primary Portal Recall (the den portal is tied)");
                    return new RecallLeg(this, RecallHome.ForCharacter(session, _character, _log, JourneyGrounds.DenLandblock, RecallHome.PrimaryPortalRecallSpell), home: false);
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

    private static bool AlreadyThere(IGameSessionView session, TeleportDestination destination)
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

        if (to.CellId >> 16 == JourneyGrounds.DenLandblock)
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

    private sealed class RecallLeg(JourneyTravel owner, RecallHome? recall, bool home) : Leg
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
                    else
                    {
                        owner._facts.PortalTied = true;
                    }

                    return End(TravelStatus.Arrived, null);
                case RecallStatus.Failed:
                    if (recall.Refusal == 0x049E)
                    {
                        owner._facts.LifestoneTied = false;
                    }
                    else if (recall.Refusal == 0x04A3)
                    {
                        owner._facts.PortalTied = false;
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

    private sealed class RunLeg(JourneyTravel owner, ITravelLeg inner) : Leg
    {
        public override TravelMeans Means => TravelMeans.Run;

        public override string Fields => inner.Fields;

        public override bool ArrivedWithoutSight => inner.ArrivedWithoutSight;

        public override TravelStatus Step(IGameSessionView session, double now)
        {
            var status = inner.Step(session, now);
            if (status != TravelStatus.Running && !Done)
            {
                Done = true;
                Failure = inner.Failure ?? (status == TravelStatus.Failed ? "travel:run:failed" : null);
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
