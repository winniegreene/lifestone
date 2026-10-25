using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Scheduling;
using Lifestone.Client.Session.Contracts;
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
    private readonly ILogSink _log;
    private readonly JourneyLegPlanner _planner;
    private readonly LegBody _body;
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
    /// <param name="den">The den's room graph being built on a worker, or null when the journey has no den station.</param>
    /// <param name="log">The log.</param>
    public GodotJourneyRunnerHost(PlayMode mode, GameServices services, DatSet dats, Task<RoomGraph>? den, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(log);
        (_mode, _workers, _den, _log) = (mode, services.Workers, den, log);
        _planner = new JourneyLegPlanner(dats, services.CellStructures, () => _den?.Result ?? throw new InvalidOperationException("the journey built no den room graph (TODO(m4-journey-den-graph): the plan has no den station)"));
        _body = new LegBody(this);
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
    /// <remarks>The M3 Godot fight leg's walk over the player (<see cref="GameFightWalk"/>), on the den's graph built on the loading worker; null (the station's own fallback) when the plan has no den.</remarks>
    public IFightWalk? DenWalk(JourneyContext context) =>
        _den is { IsCompletedSuccessfully: true } den ? new GameFightWalk(new RoutePlayHost(() => _mode.Rig?.Controller.Walker, () => _mode.MovementGate?.Held ?? 0), den.Result, _log) : null;

    /// <inheritdoc />
    /// <remarks>The play's hook-sound router, counted as the retired login-buff leg did (<c>AudioService.ReadSources</c>'s server count); null when the router does not count sounded voices.</remarks>
    public int? ServerSounds => AudioService.ReadSources(_mode.HookSounds, null, null).Server;

    /// <inheritdoc />
    /// <remarks>None: the hunt station reads the player from the session in the game, as before J5b.</remarks>
    public IHuntFollow? HuntFollow(JourneyContext context) => null;

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
                new HostileCreatureFeed(session.State.Objects, session.State.Self.Guid, host._log)); // the run's creature layer (nav-creature-avoid)
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
