using System.Globalization;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Scheduling;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.World;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Game.Audio;
using Lifestone.Game.Core;
using Lifestone.Game.Player;

namespace Lifestone.Game.Sessions;

/// <summary>
/// The Godot host's runner for a journey (M4 P5b J5, J5b; docs/session.md section 40): the player's own body is the walker. A leg is the shared
/// <see cref="JourneyRunLeg"/> with the shared <see cref="JourneyLegPlanner"/> (the headless host's goal, path source and exit route, confirm to an object's range and
/// named failures), over a <see cref="NavRunner"/> of <see cref="ControllerNavBody"/> of the play's <c>PlayerMovementController</c>, pressing keys through the session's
/// <c>IActionApi.Move</c>, confirming its routes on the play's own collision world with the <see cref="RouteConfirmer"/> sliced to <see cref="ConfirmBudgetMilliseconds"/> a frame.
/// The nav tiles are built on the game's workers as the player's own <see cref="CollisionStreamer"/> attaches landblocks (a <see cref="HeadlessStreamer"/> in its external-collision
/// form, made at the first leg), and the runner's route feeds that streamer's corridor. Every leg waits before it plans until the player is present, its body placed where the server
/// has it and the focus round it settled (<see cref="HeadlessStreamer.IsFocusSettled"/>, the headless body's rule, J5b), bounded by <see cref="SettleSeconds"/>: the failure is
/// <c>travel:run:not-settled:&lt;cell&gt;</c>, never a plan over missing tiles. A person's movement key ends a leg <c>travel:run:interrupted</c> (R6: the player's keys win); a teleport
/// under a leg ends it <c>travel:run:moved</c> and the journey drops it. The den fight's and the death's walks are <see cref="GameFightWalk"/> over the same player (the M3 fight leg's),
/// placed by the play wherever the server has the player, so a recall into the den before the station needs no teleport during it (J5b).
/// </summary>
public sealed class GodotJourneyRunnerHost : IJourneyRunnerHost, IJourneyWalks, IJourneyServerSounds, IDisposable
{
    /// <summary>The milliseconds a frame may spend confirming a route (RouteConfirmer's sliced confirm, the Godot figure of P5a S5).</summary>
    public const double ConfirmBudgetMilliseconds = 2.0;

    /// <summary>The seconds between the runner's route being fed to the collision streamer's corridor.</summary>
    public const double CorridorSeconds = 0.5;

    /// <summary>How long a leg waits for the focus round the player to settle before it fails <c>travel:run:not-settled</c> (the headless body's wait, <see cref="JourneySettleGate"/>; Lifestone's own).</summary>
    public const double SettleSeconds = JourneySettleGate.Seconds;

    private const string LogCategory = HeadlessJourneyRunnerHost.LogCategory;
    private const int AdoptRadius = 2;

    private readonly PlayMode _mode;
    private readonly WorkerPool _workers;
    private readonly Task<RoomGraph>? _den;
    private readonly HuntingGround _ground;
    private readonly ILogSink _log;
    private readonly Func<uint, float?> _setupRadius;
    private readonly Func<uint, float?> _setupExtent;
    private readonly JourneyLegPlanner _planner;
    private readonly LegBody _body;
    private readonly JourneyGroundTours _tours;
    private NavMap? _map;
    private HeadlessStreamer? _streamer;
    private RouteConfirmer? _confirmer;
    private IDisposable? _moves;
    private int _teleports;
    private int _legs;

    /// <summary>A host over the play's player.</summary>
    /// <param name="mode">Play mode (its rig holds the player's controller, collision world and streamer).</param>
    /// <param name="services">The game services (the worker pool).</param>
    /// <param name="dats">The dats.</param>
    /// <param name="den">The fight ground's room graph being built on a worker, or null when the journey has no dungeon station.</param>
    /// <param name="ground">The fight ground (<c>--fight-ground</c>) the worker's graph is of.</param>
    /// <param name="log">The log.</param>
    public GodotJourneyRunnerHost(PlayMode mode, GameServices services, DatSet dats, Task<RoomGraph>? den, HuntingGround ground, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(log);
        (_mode, _workers, _den, _ground, _log) = (mode, services.Workers, den, ground, log);
        _setupRadius = HostileCreatureFeed.SetupRadii(dats); // each creature's own radius for the runner's creature layer (nav-creature-radius-host)
        _setupExtent = DoorCrossings.SetupExtents(dats); // M5 5 d2 (closes TODO(door-extent-host)): built once per host, every leg's door feed and the fight walk read the doors' own sizes
        _planner = new JourneyLegPlanner(dats, services.CellStructures, g => g.Key == _ground.Key ? _den?.Result ?? throw new InvalidOperationException("the journey built no room graph for its ground (TODO(m4-journey-den-graph): the plan has no dungeon station)") : null, ground: _ground); // M5 3 p1: the journey's own ground (2-R12)
        _body = new LegBody(this);
        _tours = new JourneyGroundTours(log, LogCategory);
    }

    /// <summary>The nav tile feeder's figures for the journey's end line, or empty before the first leg.</summary>
    public string StreamerStats => _streamer?.StatsLine() ?? string.Empty;

    /// <summary>The last runner made for a leg (null before the first): the run clip's watch reads it (<see cref="ShowpieceEvents.Run"/>).</summary>
    public NavRunner? CurrentRunner { get; private set; }

    /// <inheritdoc />
    public ITravelLeg Run(IGameSessionView session, TravelRequest request, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        if (_mode.Rig is not { } rig)
        {
            return new Refused("travel:run:no-player");
        }

        if (rig.Controller.Walker.Physics.Setup is null)
        {
            return new Refused("travel:run:no-body-setup");
        }

        _moves ??= session.Events.Subscribe<ObjectMoved>((in ObjectMoved e) =>
        {
            if (e.IsSelf && e.Verdict == PositionVerdict.SelfTeleport)
            {
                _teleports++;
            }
        });
        var name = string.Create(CultureInfo.InvariantCulture, $"{request.Purpose.ToString().ToLowerInvariant()}-{++_legs}");
        _log.Info(LogCategory, $"journey runner: {request.Purpose} run to '{request.Destination.Name}' asked (leg {name}; it waits for the focus round the player to settle, then plans)");
        return new JourneyRunLeg(_planner, _body, request, name, session.Clock.Now, _log);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The M3 Godot fight leg's walk over the player (<see cref="GameFightWalk"/>), on the ground's graph built on the loading worker; null (the station's own fallback) when the plan has no dungeon station or the fight's ground is not the one the worker built.
    /// M5 7 P7v: every walk takes the ground's one tour for the journey (<see cref="JourneyGroundTours"/>), so the death station's wait and the corpse trip's fight know the den fight's kills and their due times.
    /// </remarks>
    public IFightWalk? FightWalk(JourneyContext context, HuntingGround ground) =>
        _den is { IsCompletedSuccessfully: true } den && ground.Key == _ground.Key ? new GameFightWalk(new RoutePlayHost(() => _mode.Rig?.Controller.Walker, () => _mode.MovementGate?.Held ?? 0), den.Result, ground, _log, _setupRadius, _tours.For(ground, () => GroundTour.For(ground, den.Result, _log)), _setupExtent) : null;

    /// <inheritdoc />
    /// <remarks>The play's hook-sound router, counted as the retired login-buff leg did (<c>AudioService.ReadSources</c>'s server count); null when the router does not count sounded voices.</remarks>
    public int? ServerSounds => AudioService.ReadSources(_mode.HookSounds, null, null).Server;

    /// <inheritdoc />
    /// <remarks>None: the hunt station reads the player from the session in the game, as before J5b. The recall-home station's view of the body is <see cref="BodyPosition"/>.</remarks>
    public IHuntFollow? HuntFollow(JourneyContext context) => null;

    /// <inheritdoc />
    /// <remarks>The player's walker (the rig's controller), once placed: the hunt leg's view of it (a placement pending, or no cell yet, is not a position). M5 item 5 c1, closes <c>TODO(m5-godot-body-position)</c>.</remarks>
    public CellFrame? BodyPosition(JourneyContext context) =>
        _mode.Rig?.Controller.Walker is { } walker && !walker.Physics.PlacementPending && walker.Physics.Cell is not null ? walker.Position : null;

    /// <inheritdoc />
    /// <remarks>The play's avatar set's physics cost and jump counts (<c>PlayMode.Avatars</c>, read only); null before the set exists.</remarks>
    public AvatarPhysicsCost? PhysicsCost => _mode.Avatars?.Stats.PhysicsCost;

    /// <inheritdoc />
    public void Dispose()
    {
        _moves?.Dispose();
        _moves = null;
        _streamer?.Dispose();
        _streamer = null;
    }

    private HeadlessStreamer EnsureStreamer(PlayerRig rig)
    {
        if (_streamer is null)
        {
            _map = new NavMap();
            _streamer = new HeadlessStreamer(rig.Physics, rig.Controller.Streamer, _workers, _map, _log);
        }

        return _streamer;
    }

    /// <summary>The player as the shared leg's body: present, placed where the server has it and the focus round it settled before a leg plans.</summary>
    private sealed class LegBody(GodotJourneyRunnerHost host) : IJourneyLegBody
    {
        public string LogCategory => GodotJourneyRunnerHost.LogCategory;

        public double WaitSeconds => SettleSeconds;

        public double CorridorSeconds => GodotJourneyRunnerHost.CorridorSeconds;

        public Func<uint, float?>? SetupExtent => host._setupExtent;

        public NavMap? Map => host._map;

        public CellFrame Position => Walker!.Position;

        public bool Placed => Walker is { } w && !w.Physics.PlacementPending && w.Physics.Cell is not null;

        public int Moves => host._teleports;

        private OfflineWalker? Walker => host._mode.Rig?.Controller.Walker;

        public void Frame(IGameSessionView session, double now) => host._streamer?.PumpTiles();

        public string? WaitReason(IGameSessionView session)
        {
            if (host._mode.Rig is not { } rig)
            {
                return "no-player: play mode has no player";
            }

            return JourneySettleGate.Reason(session, Placed ? rig.Controller.Walker.Position : null, cell =>
            {
                var streamer = host.EnsureStreamer(rig);
                streamer.AdoptAttached(LandblockCoord.FromId(cell), AdoptRadius); // the landblocks the player's streamer attached before the feeder saw them
                streamer.PumpTiles();
                return streamer.IsFocusSettled(cell);
            });
        }

        public string WaitFailure(IGameSessionView session, string reason) =>
            JourneySettleGate.Failure(Walker is { } w && Placed ? w.Position.CellId : session.State.Self.Position?.CellId ?? 0, reason);

        public NavRunner NewRunner(IGameSessionView session, INavPathSource source, string name)
        {
            var rig = host._mode.Rig ?? throw new InvalidOperationException("play mode has no player");
            host._confirmer ??= new RouteConfirmer(rig.Physics, rig.Controller.Walker.Physics.Setup!, host._log);
            return host.CurrentRunner = new NavRunner(
                session.Actions.Move,
                new ControllerNavBody(rig.Controller.Controller),
                source,
                host._confirmer,
                null,
                new NavRunnerOptions { Name = name, ConfirmBudgetMilliseconds = ConfirmBudgetMilliseconds },
                host._log,
                new HostileCreatureFeed(session.State.Objects, session.State.Self.Guid, host._log, host._setupRadius)); // the run's creature layer (nav-creature-avoid), each creature's own radius
        }

        public void FollowRoute(NavRunner runner)
        {
            if (host._streamer is { } streamer && Walker is { } w)
            {
                streamer.FollowRoute(runner, w.Position);
            }
        }
    }

    private sealed class Refused(string failure) : ITravelLeg
    {
        public TravelMeans Means => TravelMeans.Run;

        public string? Failure => failure;

        public string Fields => string.Empty;

        public TravelStatus Step(IGameSessionView session, double now) => TravelStatus.Failed;

        public void Pause()
        {
        }

        public void Resume(double now)
        {
        }

        public void Dispose()
        {
        }
    }
}
