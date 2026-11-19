using System.Globalization;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
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
    /// <summary>The fight's walk over the host's body (the ground's tour by its room graph), or null when the host has none.</summary>
    /// <param name="context">The journey's context.</param>
    /// <param name="ground">The ground the fight runs in.</param>
    IFightWalk? FightWalk(JourneyContext context, HuntingGround ground);

    /// <summary>The hunt's view of the client's own body, or null when the host has none.</summary>
    /// <param name="context">The journey's context.</param>
    IHuntFollow? HuntFollow(JourneyContext context);

    /// <summary>
    /// M6 14 body (one world picture): true when the host's body is the player the screen draws (the game). Under such a host a fight never walks through the offline <see cref="GroundWalk"/>, whose own walker would move the server player
    /// apart from the drawn body (<see cref="FightWalkChoice"/>). False (the default): the headless body, which nothing draws.
    /// </summary>
    bool DrawsBody => false;

    /// <summary>
    /// Where the host's own body stands, or null when the host cannot say or the body is not placed (M5 item 5 c1, closes <c>TODO(m5-godot-body-position)</c>): the recall-home station's step-off measures this body against the lifestone's collision.
    /// The default reads <see cref="HuntFollow"/> (the headless host's view); the Godot host answers from the player's walker without giving the hunt a follower (its hunt reads the player from the session).
    /// </summary>
    /// <param name="context">The journey's context.</param>
    CellFrame? BodyPosition(JourneyContext context)
    {
        using var follow = HuntFollow(context);
        return follow?.Position;
    }

    /// <summary>The avatars' physics cost and jump counts now (<see cref="AvatarStats.PhysicsCost"/>), read-only, for the den-fight station's report-only fields; null when the host has no avatars (the headless host): the fields print <c>n/a</c>.</summary>
    AvatarPhysicsCost? PhysicsCost => null;
}

/// <summary>
/// The headless host's piece of the travel seam (M4 P5b 3.4, J4): the client's body (<see cref="HeadlessBody"/>, factored out of
/// <see cref="WayfindScenario"/>) over the headless streamer's collision and tiles, its controller bound to <see cref="IActionApi.Move"/>, and a
/// <see cref="NavRunner"/> per leg: <see cref="OutdoorPathSource"/> outdoors, <see cref="IndoorOutdoorPathSource"/> when either end is indoors
/// (Renald's front room 0xA9B4013F, Thelnoth's cellar 0xA9B40111, the chests), and <see cref="RoomPathSource"/> over the den's floor
/// (the ground's walkable cells at its profile's doorway lean, <see cref="GroundRooms"/>) for the fight's spots and the corpse trip. A recall or a place moves the player: the next
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
    private readonly Func<uint, float?> _setupExtent;
    private Func<uint, float?>? _setupRadius; // the NPCs' Setup radii (HostileCreatureFeed.SetupRadii), made at the first runner
    private readonly ILogSink _log;
    private readonly HeadlessBody _body;
    private readonly JourneyLegPlanner _planner;
    private readonly LegBody _legBody;
    private readonly JourneyGroundTours _tours;
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
        _setupExtent = DoorCrossings.SetupExtents(dats); // M5 5 d2 (closes TODO(door-extent-host)): built once per host, every leg's door feed reads the doors' own sizes
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _body = new HeadlessBody(dats, log, LogCategory, sinkOf) { FollowServerMoves = true }; // P5b-R1: ACE's walk-to-use and attack approach move the body, as HuntFollow did
        _planner = new JourneyLegPlanner(dats);
        _legBody = new LegBody(this);
        _tours = new JourneyGroundTours(log, LogCategory);
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

    /// <summary>The body a leg on foot runs over (what <see cref="Run"/> hands every <see cref="JourneyRunLeg"/>). For tests.</summary>
    internal IJourneyLegBody JourneyLegBody => _legBody;

    /// <inheritdoc />
    public ITravelLeg Run(IGameSessionView session, TravelRequest request, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        Legs++;
        if (session.Actions.Shots is null)
        {
            session.Actions.UseShots(new Casting.PhysicsShotCheck(() => _body.Placed ? _body.Walker?.World : null, _dats)); // M6 8 L: the headless body's physics world answers the shot check, as the player's does in the game
        }

        return new JourneyRunLeg(_planner, _legBody, request, request.Purpose.ToString().ToLowerInvariant() + "-" + Legs.ToString(CultureInfo.InvariantCulture), session.Clock.Now, _log);
    }

    /// <inheritdoc />
    /// <remarks>M5 7 P7v: every walk on a ground takes the ground's one tour for the journey (<see cref="JourneyGroundTours"/>), so the death station's wait and the corpse trip's fight know the den fight's kills and their due times.</remarks>
    public IFightWalk FightWalk(JourneyContext context, HuntingGround ground) => new GroundFightWalk(this, ground, _tours.For(ground, () => GroundTour.For(ground, GroundGraph(ground), _log)));

    /// <summary>The fight's walk over a tour given (a test's: the real one is <see cref="GroundTour.For"/>), the same walk as <see cref="FightWalk(JourneyContext, HuntingGround)"/>.</summary>
    /// <param name="context">The journey context (not read).</param>
    /// <param name="ground">The ground.</param>
    /// <param name="tour">Where each leg's next waypoint, and its purpose, come from.</param>
    public IFightWalk FightWalk(JourneyContext context, HuntingGround ground, IGroundTour tour) => new GroundFightWalk(this, ground, tour);

    /// <inheritdoc />
    public IHuntFollow HuntFollow(JourneyContext context) => new BodyFollow(this);

    /// <summary>The path source for a leg from <paramref name="fromCell"/> to <paramref name="toCell"/>: the den's room graph, the indoor-outdoor source when either end is indoors, else the outdoor planner.</summary>
    /// <param name="fromCell">The body's cell.</param>
    /// <param name="toCell">The goal's cell.</param>
    public INavPathSource SourceFor(uint fromCell, uint toCell) => _planner.SourceFor(fromCell, toCell, _body.Map);

    /// <summary>The ground's room graph (its walkable cells at its doorway lean), loaded once per ground.</summary>
    /// <param name="ground">The ground.</param>
    public RoomGraph GroundGraph(HuntingGround ground) => _planner.GroundGraph(ground);

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

        public Func<uint, float?>? SetupExtent => host._setupExtent;

        public NavMap? Map => host._body.Map;

        public CellFrame Position => host._body.Walker!.Position;

        public bool Placed => host._body.Placed;

        public int Moves => host._body.Teleports;

        public void Frame(IGameSessionView session, double now) => host.Frame(session, now);

        public string? WaitReason(IGameSessionView session) => host._body.Placed ? null : host.PlaceBlocker ?? "no reason recorded";

        public string WaitFailure(IGameSessionView session, string reason) =>
            string.Create(CultureInfo.InvariantCulture, $"travel:run:not-placed: the body was not placed within {PlaceWaitSeconds:0} s ({reason})");

        public NavRunner NewRunner(IGameSessionView session, INavPathSource source, string name)
        {
            var runner = host._body.NewRunner(source, name, ConfirmBudgetMilliseconds);
            runner.BodyFeed = new NpcBodyFeed(session.State.Objects, session.State.Self.Guid, host._log, host._setupRadius ??= HostileCreatureFeed.SetupRadii(host._dats)); // the NPCs' bodies on the plan (M6 4 W2, m6-npc-in-door-lane)
            return runner;
        }

        public void FollowRoute(NavRunner runner) => host._body.FollowRoute(runner);
    }

    /// <summary>
    /// The fight's walk: each leg to the next waypoint of the ground's <see cref="IGroundTour"/> (not the player's cell), over the host's runner (<see cref="NavRunner"/> over the ground's room graph with the creature layer). A failed leg (the runner's failure, or
    /// over the profile's <see cref="WalkModel.LegSeconds"/>) is abandoned and the walk goes on to the next waypoint, three in a row ending it (<c>m5-fight-walk-recover</c>, <see cref="LegRecovery"/>). Entering a no-go cell of the ground stops it, named.
    /// </summary>
    private sealed class GroundFightWalk(HeadlessJourneyRunnerHost host, HuntingGround ground, IGroundTour tour) : IFightWalk
    {
        private readonly LegRecovery _recovery = new(host._log);
        private readonly LegPacing _pacing = new(); // F6: the hold and the walking seconds, the rules RunnerLegs follows (F5)

        /// <inheritdoc/>
        int? IFightWalk.LegsRecovered => _recovery.Recovered;

        private ITravelLeg? _leg;
        private uint _goal;
        private int _legs;
        private double _legStart;
        private bool _spot; // M6 6 B1: the leg under way is BeginLegTo's (a spot of the fight's own), not the tour's: its end is Arrived either way and the tour hears nothing of it
        private IReadOnlyList<CellFrame>? _floorSpots;

        public string? Failure { get; private set; }

        public IGroundTour Tour => tour;

        /// <summary>M6 6 B1 (<c>m5-loot-walk-back-walk</c>): a leg over the room graph to <paramref name="goal"/> (the host's runner, the same seam as the tour's legs); a leg that fails ends Arrived, logged (IFightWalk.BeginLegTo).</summary>
        public bool BeginLegTo(IGameSessionView session, double now, in CellFrame goal, out string description)
        {
            host.Frame(session, now);
            if (!host._body.Placed || host._body.Walker is not { } walker)
            {
                description = $"the journey's body is not placed where the server has the player ({host.PlaceBlocker ?? "no reason recorded"})";
                return false;
            }

            var from = walker.Position;
            if (host.GroundGraph(ground).Plan(from.CellId, from.Origin, goal.CellId, goal.Origin) is null)
            {
                description = $"no walk of the room graph joins the player's cell 0x{from.CellId:X8} and the spot's cell 0x{goal.CellId:X8}";
                return false;
            }

            _pacing.Stop();
            _leg?.Dispose();
            var destination = new Navigation.TeleportDestination("ground spot", [], new World.WorldLocation("ground spot", new World.CellPosition(goal.CellId, goal.Origin), HasHeight: true, HeadingDegrees: 0), "journey: the fight's own spot");
            _leg = host.Run(session, new TravelRequest(destination, TravelPurpose.Spot), now);
            (_goal, _spot) = (goal.CellId, true);
            _legs++;
            _pacing.BeginWalk(now);
            _legStart = session.Clock.Now;
            description = $"leg {_legs} from 0x{from.CellId:X8} to the spot in 0x{goal.CellId:X8} on foot (the room graph)";
            return true;
        }

        /// <summary>M5 7 P7s (<c>m5-loot-approach-trace</c>): the cells of a straight line on the host's walker's physics world (<see cref="GameFightWalk.TraceCells"/>); not traced before the body is made.</summary>
        public LineTrace CellsOnLine(in CellFrame from, in CellFrame to, List<uint> cells)
        {
            if (host._body.Walker is not { } walker)
            {
                ArgumentNullException.ThrowIfNull(cells);
                cells.Clear();
                return LineTrace.NotTraced;
            }

            return GameFightWalk.TraceCells(walker.World, in from, in to, cells);
        }

        /// <summary>M5 7 P7s (<c>m5-defend-step-spots</c>): the middle of every room of the host's room graph for the ground (<see cref="GameFightWalk.RoomSpots"/>), built once.</summary>
        public IReadOnlyList<CellFrame> FloorSpots => _floorSpots ??= GameFightWalk.RoomSpots(host.GroundGraph(ground));

        public void Prepare(IGameSessionView session, double now) => host.Frame(session, now);

        /// <summary>M6 14 airborne and pit-hold: the leg's runner has a jump committed (<see cref="NavRunner.JumpCommitted"/>: the run for the brink with the jump key down, or the flight).</summary>
        public bool JumpInFlight => (_leg as JourneyRunLeg)?.Runner?.JumpCommitted == true;

        public bool BeginLeg(IGameSessionView session, double now, out string description)
        {
            description = string.Empty;
            host.Frame(session, now);
            _spot = false;
            if (!host._body.Placed)
            {
                Failure = $"the journey's body is not placed where the server has the player ({host.PlaceBlocker ?? "no reason recorded"})";
                return false;
            }

            return Begin(session, host._body.Walker!.Position.CellId, now, out description);
        }

        public FightWalkStatus Tick(IGameSessionView session, double now)
        {
            host.Frame(session, now);
            if (_pacing.Holding)
            {
                // F6 (D47): the tour named no purpose to walk: no leg; the walk "arrives" after RunnerLegs.HoldRecheckSeconds and the fight looks for a target and asks the tour again.
                if (!_pacing.HoldOver(session.Clock.Now))
                {
                    return FightWalkStatus.Walking;
                }

                _pacing.Stop();
                return FightWalkStatus.Arrived;
            }

            if (_leg is null)
            {
                return FightWalkStatus.Idle;
            }

            if (host._body.Walker is { } walker && ground.IsNoGo(walker.Position.CellId))
            {
                Failure = GroundWalk.NoGoReason(ground, walker.Position.CellId);
                Abandon();
                return FightWalkStatus.Failed;
            }

            _pacing.Walked((_leg as JourneyRunLeg)?.Runner?.State == NavRunState.Running, now); // F6: the runner's walking seconds, not the time from the offer to the arrival
            switch (_leg.Step(session, now))
            {
                case TravelStatus.Arrived:
                    _leg.Dispose();
                    _leg = null;
                    _recovery.Arrived();
                    if (!_spot)
                    {
                        tour.LegWalked(_goal, session.Clock.Now, _pacing.WalkSeconds);
                    }

                    return FightWalkStatus.Arrived;
                case TravelStatus.Failed:
                    return Recover(session, now, _leg.Failure ?? "the leg failed without saying why");
                default:
                    return session.Clock.Now - _legStart > ground.Walk.LegSeconds && !JumpInFlight // M6 14 airborne: the leg's clock waits for the jump's landing check
                        ? Recover(session, now, string.Create(CultureInfo.InvariantCulture, $"the leg did not finish within {ground.Walk.LegSeconds:0.#} s"))
                        : FightWalkStatus.Walking;
            }
        }

        public void Abandon()
        {
            _leg?.Dispose();
            _leg = null;
            _pacing.Stop();
        }

        /// <summary>Plans and starts the leg to the tour's next waypoint that has a room frame, from the cell <paramref name="fromCell"/>.</summary>
        private bool Begin(IGameSessionView session, uint fromCell, double now, out string description)
        {
            description = string.Empty;
            var graph = host.GroundGraph(ground);
            for (var tries = 0; tries < tour.Waypoints; tries++)
            {
                if (tour.Next(fromCell, session.Clock.Now) is not { } cellId)
                {
                    break;
                }

                if (tour.Purpose == TourPurpose.Hold)
                {
                    // F6 (D47): no purpose to walk: no leg, the player stands (RunnerLegs.Begin's rule, F5).
                    _leg?.Dispose();
                    _leg = null;
                    _goal = cellId;
                    description = _pacing.BeginHold(tour, fromCell, session.Clock.Now);
                    return true;
                }

                if (graph.RoomFrame(cellId) is not { } room)
                {
                    continue;
                }

                _leg?.Dispose();
                var destination = new Navigation.TeleportDestination("ground spot", [], new World.WorldLocation("ground spot", new World.CellPosition(cellId, room.Origin), HasHeight: true, HeadingDegrees: 0), "journey: the ground fight's spot");
                _leg = host.Run(session, new TravelRequest(destination, TravelPurpose.Spot), now);
                _goal = cellId;
                _legs++;
                _pacing.BeginWalk(now);
                _legStart = session.Clock.Now;
                description = $"leg to 0x{cellId:X8} on foot (the room graph)";
                return true;
            }

            Failure = $"no spot of {ground.Name}'s tour besides the player's cell 0x{fromCell:X8}";
            return false;
        }

        /// <summary>The leg failed: it is dropped and the walk goes on to the next waypoint from where the player stands, unless three legs in a row failed.</summary>
        private FightWalkStatus Recover(IGameSessionView session, double now, string reason)
        {
            _leg?.Dispose();
            _leg = null;
            if (_spot)
            {
                // BeginLegTo's contract: a failed spot leg ends Arrived, logged; the fight reads where the player stands and the tour is not told.
                _spot = false;
                _pacing.Stop();
                host._log.Warn(LogCategory, $"the leg to the spot in 0x{_goal:X8} failed ({reason}); ended Arrived without recovery onto the tour");
                return FightWalkStatus.Arrived;
            }

            tour.LegFailed(_goal, session.Clock.Now);
            if (_recovery.Failed(_legs, _goal, reason) is { } ended)
            {
                Failure = ended;
                return FightWalkStatus.Failed;
            }

            var at = host._body.Walker?.Position.CellId ?? 0;
            if (!Begin(session, at, now, out var next))
            {
                Failure = $"leg {_legs} failed ({reason}) and no leg to the next waypoint could be planned: " + Failure;
                return FightWalkStatus.Failed;
            }

            host._log.Info(LogCategory, "recovered: " + next);
            return FightWalkStatus.Walking;
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
            JourneyScenario.BindPitLog(log); // M6 4 R2: TODO(m6-abode-pits) reaches the run's log file
            var keeper = JourneyStations.CreateKeeper(Plan, session, spells, arguments, log, kitAccount); // M6 1 w: the keeper's floors read the login name
            var context = JourneyScenario.NewContext(Plan, log, spells, keeper, dats, Host, dungeon: JourneyLimits.FightOf(arguments).GroundProfile); // --fight-ground: a bad key is the station factory's error and keeps the default
            JourneyBotFacts.Apply(context.Facts, Plan, arguments); // M6 item 14: the Pathwarden station's stop at the buff bot before the chest reads the same option and word as the autopilot (the game window's BuildParts sets them the same way)
            _journey = new JourneyScenario(Plan, JourneyStations.Create(Plan, arguments, dats, kitAccount), context);
        }

        Host.Tick(session, now);
        return _journey.Step(session, now);
    }

    /// <summary>
    /// M6 item 14: whether the stop at the buff bot applies to a headless journey (<see cref="JourneyFacts.BuffBotOn"/>). Only a plan with the Pathwarden station reads it (the climbers' first journey, in Holtburg, the bot's town): the
    /// answer is the autopilot's own option (<c>--autopilot-buff-bot</c>, on by default), so the journey that opens the chest and the level up job that follows it never disagree. Any other plan: null, as before (no station of theirs
    /// stops at the bot in a headless journey).
    /// </summary>
    /// <param name="plan">The journey's plan.</param>
    /// <param name="arguments">The host's command line.</param>
    public static Func<bool>? BotStopOf(JourneyPlan plan, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(arguments);
        if (!plan.Stations.Any(s => s.Kind == StationKinds.Pathwarden))
        {
            return null;
        }

        var on = Autopilot.AutopilotOptions.FromArguments(Lifestone.Client.Options.CommandLineArgument.ParseAll(arguments)).BuffBot;
        return () => on;
    }

    /// <summary>
    /// M6 item 14: the profile a headless journey asks the buff bot for (<see cref="JourneyFacts.BuffBotProfile"/>): the autopilot's own option (<c>--autopilot-buff-bot-word</c>, <c>all</c> by default), so the journey that opens the
    /// chest and the level up job that follows it tell the bot the same word. A word the bot does not know is the option's own error (the host reports it); here it keeps the default.
    /// </summary>
    /// <param name="arguments">The host's command line.</param>
    public static Lifestone.ServerTools.BuffBotProfile BotProfileOf(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return Autopilot.AutopilotOptions.FromArguments(Lifestone.Client.Options.CommandLineArgument.ParseAll(arguments)).BuffBotProfile;
    }

    /// <summary>Releases the journey and the runner host.</summary>
    public void Dispose()
    {
        _journey?.Dispose();
        Host.Dispose();
    }
}
