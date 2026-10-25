using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// What a journey host offers the embedded scenarios besides the runner (M4 P5b J4): the den fight's walk between the floor's spots and the hunt's
/// view of the client's own body, both over the host's one body, so no station binds a second controller to the player's keys.
/// </summary>
public interface IJourneyWalks
{
    /// <summary>The den fight's walk over the host's body (the floor's spots by the room graph), or null when the host has none.</summary>
    /// <param name="context">The journey's context.</param>
    IFightWalk? DenWalk(JourneyContext context);

    /// <summary>The hunt's view of the client's own body, or null when the host has none.</summary>
    /// <param name="context">The journey's context.</param>
    IHuntFollow? HuntFollow(JourneyContext context);
}

/// <summary>
/// The headless host's piece of the travel seam (M4 P5b 3.4, J4): the client's body (<see cref="HeadlessBody"/>, factored out of
/// <see cref="WayfindScenario"/>) over the headless streamer's collision and tiles, its controller bound to <see cref="IActionApi.Move"/>, and a
/// <see cref="NavRunner"/> per leg: <see cref="OutdoorPathSource"/> outdoors, <see cref="IndoorOutdoorPathSource"/> when either end is indoors
/// (Renald's front room 0xA9B4013F, Thelnoth's cellar 0xA9B40111, the chests), and <see cref="RoomPathSource"/> over the den's floor
/// (landblock 0x0105, lean <see cref="DenRooms.MaxLean"/>) for the fight's spots and the corpse trip. A recall or a place moves the player: the next
/// <see cref="Tick"/> re-places the body where the server put it, once the collision round it is built. The leg and its planning are shared with the Godot
/// host since M4 P5b J5b (<see cref="JourneyRunLeg"/>, <see cref="JourneyLegPlanner"/>); this host gives them its own body.
/// </summary>
public sealed class HeadlessJourneyRunnerHost : IJourneyRunnerHost, IJourneyWalks, IDisposable
{
    /// <summary>The log category.</summary>
    public const string LogCategory = "journey-runner";

    /// <summary>The route confirmation's budget per step (the wayfind scenario's).</summary>
    public const double ConfirmBudgetMilliseconds = WayfindScenario.ConfirmBudgetMilliseconds;

    /// <summary>How often the corridor ahead is fed to the streamer.</summary>
    public const double CorridorSeconds = WayfindScenario.CorridorSeconds;

    /// <summary>How long a leg waits for the body to be placed (the collision round the player built) before it fails.</summary>
    public const double PlaceWaitSeconds = WayfindScenario.SettleSeconds;

    /// <summary>The server has the player this far (or more) from the body without a teleport: the body is re-placed (a run trails the server by a few metres at most).</summary>
    public const float MovedMetres = 25f;

    /// <summary>Renald's front room (the shop's goal, P5a S5: the room behind his counter is not walkable, TODO(room-steps)).</summary>
    public const uint RenaldFrontRoomCell = 0xA9B4013F;

    /// <summary>Thelnoth's cellar.</summary>
    public const uint ThelnothCellarCell = 0xA9B40111;

    private readonly DatSet _dats;
    private readonly ILogSink _log;
    private readonly HeadlessBody _body;
    private readonly JourneyLegPlanner _planner;
    private readonly LegBody _legBody;
    private double _lastTick = double.NaN;
    private int _teleportsSeen;
    private bool _placeWarned;
    private bool _following;
    private bool _disposed;

    /// <summary>A host over the dats.</summary>
    /// <param name="dats">The dats.</param>
    /// <param name="log">The log.</param>
    /// <param name="sinkOf">The movement sink for a session (tests), or null for the presence-gated sink.</param>
    public HeadlessJourneyRunnerHost(DatSet dats, ILogSink log, Func<IGameSessionView, IMovementSink>? sinkOf = null)
    {
        _dats = dats ?? throw new ArgumentNullException(nameof(dats));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _body = new HeadlessBody(dats, log, LogCategory, sinkOf) { FollowServerMoves = true }; // P5b-R1: ACE's walk-to-use and attack approach move the body, as HuntFollow did
        _planner = new JourneyLegPlanner(dats);
        _legBody = new LegBody(this);
    }

    /// <summary>The leg planner (shared with the Godot host, M4 P5b J5b).</summary>
    public JourneyLegPlanner Planner => _planner;

    /// <summary>The client's body.</summary>
    public HeadlessBody Body => _body;

    /// <summary>The times the body was placed where the server had the player (the first placement and each one after a recall or a place).</summary>
    public int Placements { get; private set; }

    /// <summary>The legs run.</summary>
    public int Legs { get; private set; }

    /// <summary>Why the body is not placed now, as a named reason (<c>not-present</c>, <c>collision-not-settled:&lt;cell&gt;</c>, <c>no-valid-position:&lt;cell&gt;</c>), or null once placed.</summary>
    public string? PlaceBlocker { get; private set; } = "not-built: the player is not in the world with its qualities yet";

    /// <summary>
    /// One frame (idempotent for the same <paramref name="now"/>): builds the body once the player is in the world with its qualities, pumps the streamer,
    /// applies the player's moves, ticks the controller (so a lie-down or a rest posture sent through the keys reaches it), and re-places the body after a
    /// teleport. The host calls it every frame before the journey steps (the frame order: controller, runner, session).
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The host's time.</param>
    public void Tick(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_disposed || now.Equals(_lastTick))
        {
            return;
        }

        _lastTick = now;
        var self = session.State.Self;
        if (!_body.Created)
        {
            if (session.State.Phase != SessionPhase.InWorld || self.Guid == 0 || !self.Qualities.IsKnown || self.Position is null)
            {
                return;
            }

            _body.Create(session, now);
            _teleportsSeen = _body.Teleports;
            _log.Info(LogCategory, "the journey's body is built (the player's keys are bound to it)");
        }

        _body.Frame(now);
        if (_body.Placed)
        {
            if (_body.Teleports == _teleportsSeen && (self.Position is not { } server || !JourneyLegPlanner.Moved(server, _body.Walker!.Position, MovedMetres)))
            {
                return;
            }

            _teleportsSeen = _body.Teleports;
            _body.Unplace();
            _following = true;
            _log.Info(LogCategory, "the player was moved by the server (a recall, a place or a portal): the body is placed again where the server has it");
        }

        if (self.Travel != SelfTravel.Present || self.Position is not { } at)
        {
            PlaceBlocker = "not-present: the player is not present in the world (portal space or a login)";
            return;
        }

        if (!_body.Settled(at))
        {
            PlaceBlocker = $"collision-not-settled:0x{at.CellId:X8}: the collision and the tiles round the player's cell are not built yet";
            return;
        }

        if (!_body.IsAttached(at) || !_body.PlaceAt(at, session.Clock.Now))
        {
            PlaceBlocker = $"no-valid-position:0x{at.CellId:X8}: the physics finds no valid position for the body where the server has the player";
            if (!_placeWarned)
            {
                _placeWarned = true;
                _log.Warn(LogCategory, $"no valid position for the body where the server has the player (cell 0x{at.CellId:X8}); retried every frame");
            }

            return;
        }

        _placeWarned = false;
        PlaceBlocker = null;
        _teleportsSeen = _body.Teleports;
        Placements++;
        var cell = new LogField("cell", $"0x{at.CellId:X8}");
        if (_following)
        {
            _following = false;
            _log.Info(LogCategory, $"the body follows the player to 0x{at.CellId:X8}", cell, new LogField("placements", Placements), new LogField("streamer", _body.Streamer!.StatsLine()));
            return;
        }

        _log.Info(LogCategory, "the body is placed", cell, new LogField("placements", Placements), new LogField("streamer", _body.Streamer!.StatsLine()));
    }

    /// <inheritdoc />
    public ITravelLeg Run(IGameSessionView session, TravelRequest request, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        Legs++;
        return new JourneyRunLeg(_planner, _legBody, request, request.Purpose.ToString().ToLowerInvariant() + "-" + Legs.ToString(CultureInfo.InvariantCulture), session.Clock.Now, _log);
    }

    /// <inheritdoc />
    public IFightWalk DenWalk(JourneyContext context) => new DenFightWalk(this);

    /// <inheritdoc />
    public IHuntFollow HuntFollow(JourneyContext context) => new BodyFollow(this);

    /// <summary>The path source for a leg from <paramref name="fromCell"/> to <paramref name="toCell"/>: the den's room graph, the indoor-outdoor source when either end is indoors, else the outdoor planner.</summary>
    /// <param name="fromCell">The body's cell.</param>
    /// <param name="toCell">The goal's cell.</param>
    public INavPathSource SourceFor(uint fromCell, uint toCell) => _planner.SourceFor(fromCell, toCell, _body.Map);

    /// <summary>The den's floor graph (landblock 0x0105, lean <see cref="DenRooms.MaxLean"/>), loaded once.</summary>
    public RoomGraph DenGraph() => _planner.DenGraph();

    /// <summary>Releases the body (the keys' binding, the streamer).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _body.Dispose();
    }

    private void Frame(IGameSessionView session, double now) => Tick(session, now);

    /// <summary>The headless body as the shared leg's body (M4 P5b J5b): the leg waits for the body to be placed, as before the shared planner.</summary>
    private sealed class LegBody(HeadlessJourneyRunnerHost host) : IJourneyLegBody
    {
        public string LogCategory => HeadlessJourneyRunnerHost.LogCategory;

        public double WaitSeconds => PlaceWaitSeconds;

        public double CorridorSeconds => HeadlessJourneyRunnerHost.CorridorSeconds;

        public NavMap? Map => host._body.Map;

        public CellFrame Position => host._body.Walker!.Position;

        public bool Placed => host._body.Placed;

        public int Moves => host._body.Teleports;

        public void Frame(IGameSessionView session, double now) => host.Frame(session, now);

        public string? WaitReason(IGameSessionView session) => host._body.Placed ? null : host.PlaceBlocker ?? "no reason recorded";

        public string WaitFailure(IGameSessionView session, string reason) =>
            string.Create(CultureInfo.InvariantCulture, $"travel:run:not-placed: the body was not placed within {PlaceWaitSeconds:0} s ({reason})");

        public NavRunner NewRunner(IGameSessionView session, INavPathSource source, string name) => host._body.NewRunner(source, name, ConfirmBudgetMilliseconds);

        public void FollowRoute(NavRunner runner) => host._body.FollowRoute(runner);
    }

    /// <summary>The den fight's walk: each leg to the next spawn cell of <see cref="Scenarios.DenWalk.Tour"/> that is not the player's, over the host's runner.</summary>
    private sealed class DenFightWalk(HeadlessJourneyRunnerHost host) : IFightWalk
    {
        private ITravelLeg? _leg;
        private int _next;

        public string? Failure { get; private set; }

        public void Prepare(IGameSessionView session, double now) => host.Frame(session, now);

        public bool BeginLeg(IGameSessionView session, double now, out string description)
        {
            description = string.Empty;
            host.Frame(session, now);
            if (!host._body.Placed)
            {
                Failure = $"the journey's body is not placed where the server has the player ({host.PlaceBlocker ?? "no reason recorded"})";
                return false;
            }

            var from = host._body.Walker!.Position;
            var graph = host.DenGraph();
            for (var tries = 0; tries < Scenarios.DenWalk.Tour.Count; tries++)
            {
                var cellId = (JourneyGrounds.DenLandblock << 16) | Scenarios.DenWalk.Tour[_next++ % Scenarios.DenWalk.Tour.Count];
                if (cellId == from.CellId || graph.RoomFrame(cellId) is not { } room)
                {
                    continue;
                }

                _leg?.Dispose();
                var destination = new Navigation.TeleportDestination("den spot", [], new World.WorldLocation("den spot", new World.CellPosition(cellId, room.Origin), HasHeight: true, HeadingDegrees: 0), "journey: the den fight's spot");
                _leg = host.Run(session, new TravelRequest(destination, TravelPurpose.Spot), now);
                description = $"leg to 0x{cellId:X8} on foot (the room graph)";
                return true;
            }

            Failure = $"no spot of the den's floor besides the player's cell 0x{from.CellId:X8}";
            return false;
        }

        public FightWalkStatus Tick(IGameSessionView session, double now)
        {
            host.Frame(session, now);
            if (_leg is null)
            {
                return FightWalkStatus.Idle;
            }

            switch (_leg.Step(session, now))
            {
                case TravelStatus.Arrived:
                    _leg.Dispose();
                    _leg = null;
                    return FightWalkStatus.Arrived;
                case TravelStatus.Failed:
                    Failure = _leg.Failure;
                    _leg.Dispose();
                    _leg = null;
                    return FightWalkStatus.Failed;
                default:
                    return FightWalkStatus.Walking;
            }
        }

        public void Abandon()
        {
            _leg?.Dispose();
            _leg = null;
        }

        public bool HasClearLine(in CellFrame from, in CellFrame to) => !host._body.Placed || !host._body.Confirmer.WallBetween(from, to);

        public void Dispose() => Abandon();
    }

    /// <summary>The hunt's view of the host's body (no second walker; disposing it leaves the body to the host); the body follows the server's MoveTos and TurnTos (<see cref="HeadlessBody.FollowServerMoves"/>, P5b-R1), as <see cref="Scenarios.HuntFollow"/> does.</summary>
    private sealed class BodyFollow(HeadlessJourneyRunnerHost host) : IHuntFollow
    {
        public string? Failure => null;

        public CellFrame? Position => host._body.Placed ? host._body.Walker!.Position : null;

        public void Prepare(IGameSessionView session, double now) => host.Frame(session, now);

        public void Tick(IGameSessionView session, double now) => host.Frame(session, now);

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// <c>--scenario=journey</c> in the headless host (M4 P5b J4): at its first step it builds the journey's keeper over the session
/// (<see cref="JourneyStations.CreateKeeper"/>), the context with the headless runner host (<see cref="JourneyScenario.NewContext"/>), the station
/// catalog (<see cref="JourneyStations.Create(JourneyPlan, IReadOnlyList{string}, DatSet?, string?)"/>) and the <see cref="JourneyScenario"/>; then
/// every step ticks the runner host before the journey (the frame order: controller, runner, session).
/// </summary>
/// <param name="plan">The plan.</param>
/// <param name="arguments">The host's command line (the scenario options).</param>
/// <param name="dats">The dats.</param>
/// <param name="spells">The spell table.</param>
/// <param name="log">The log.</param>
/// <param name="kitAccount">The account's login name the gear kit is built for (hashed by the kit, never stored or printed), or null.</param>
/// <param name="host">The runner host (the scenario disposes it).</param>
public sealed class HeadlessJourneyScenario(JourneyPlan plan, IReadOnlyList<string> arguments, DatSet dats, IReadOnlyDictionary<uint, SpellInfo> spells, ILogSink log, string? kitAccount, HeadlessJourneyRunnerHost host) : IScenario, IDisposable
{
    private JourneyScenario? _journey;

    /// <summary>The plan.</summary>
    public JourneyPlan Plan { get; } = plan ?? throw new ArgumentNullException(nameof(plan));

    /// <summary>The character the journey enters (the account rule reads it).</summary>
    public string Character => Plan.Character;

    /// <summary>The runner host.</summary>
    public HeadlessJourneyRunnerHost Host { get; } = host ?? throw new ArgumentNullException(nameof(host));

    /// <summary>The journey, once the first step built it.</summary>
    public JourneyScenario? Journey => _journey;

    /// <summary>The journey's lines (each station's, then the done line).</summary>
    public IReadOnlyList<string> Lines => _journey?.Lines ?? [];

    /// <inheritdoc />
    public string Name => JourneyScenario.ScenarioName;

    /// <inheritdoc />
    public string Stage => _journey?.Stage ?? "start";

    /// <inheritdoc />
    public string? Failure => _journey?.Failure;

    /// <inheritdoc />
    public double? WhereMaxMetres => _journey?.WhereMaxMetres;

    /// <inheritdoc />
    public long Unmatched => _journey?.Unmatched ?? 0;

    /// <inheritdoc />
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_journey is null)
        {
            var keeper = JourneyStations.CreateKeeper(Plan, session, spells, arguments, log);
            var context = JourneyScenario.NewContext(Plan, log, spells, keeper, dats, Host);
            _journey = new JourneyScenario(Plan, JourneyStations.Create(Plan, arguments, dats, kitAccount), context);
        }

        Host.Tick(session, now);
        return _journey.Step(session, now);
    }

    /// <summary>Releases the journey and the runner host.</summary>
    public void Dispose()
    {
        _journey?.Dispose();
        Host.Dispose();
    }
}
