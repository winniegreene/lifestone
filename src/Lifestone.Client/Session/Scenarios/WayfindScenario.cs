using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Navigation;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Tools;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Data;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The wayfinder's live tool (P5a S6, <c>--scenario=wayfind --to=&lt;cell&gt;[@x,y,z] [--from=...] [--wayfind-back]</c>): one login as the fighter of the account, a known start (one server-mod
/// <c>place</c> to <c>--from</c> when the player is more than <see cref="FromToleranceMetres"/> from it, else none), the client's own body made from the dats with the collision and the
/// navigation tiles streamed by a <see cref="HeadlessStreamer"/> (two workers, the attaches on a frame budget), a planned run through the corridor with <see cref="NavRunner"/> pressing the
/// human's keys through <see cref="IActionApi.Move"/>, then with <c>--wayfind-back</c> the run back to the start. Each leg prints one line and the run a final one (see <see cref="Lines"/>).
/// </summary>
/// <remarks>
/// <para><b>The run is outdoors.</b> An indoor end is refused before anything moves (TODO(wayfind-indoor): the journeys of P5b walk the room graphs).</para>
/// <para><b>The corridor.</b> Every half second the scenario names the next <see cref="HeadlessStreamer.AheadLandblocks"/> landblocks of the route beyond the focus's 3x3 (the runner's path, then the straight line from
/// the path's end to the goal while the path only reaches the horizon of the attached tiles) to <see cref="CollisionStreamer.SetCorridor"/>, keeping <see cref="HeadlessStreamer.KeepBehind"/> dropped ones; the
/// runner replans itself when tiles attach near the end of a horizon route. Not retail: the wayfinder is Lifestone's own, and so are these constants.</para>
/// <para><b>Verdicts, report-only in their first gate.</b> A leg is ok when the runner arrived, the server sent no correction during it and the server's where-am-I agrees with the client's body at its
/// end (within <see cref="MovementAuditSummary.WhereToleranceMetres"/>).</para>
/// </remarks>
public sealed class WayfindScenario : IScenario, IDisposable
{
    /// <summary>The scenario's token (<c>--scenario=wayfind</c>).</summary>
    public const string ScenarioName = "wayfind";

    /// <summary>A player within this distance of <c>--from</c> is not placed there (the plan: "more than 5 m").</summary>
    public const double FromToleranceMetres = 5;

    /// <summary>How often the corridor is recomputed, in seconds of the session clock.</summary>
    public const double CorridorSeconds = 0.5;

    /// <summary>The run speed the default time limit assumes (the fighters run at 4.8 m/s with Quickness 10 and Run 15).</summary>
    public const double AssumedSpeedMetresPerSecond = 4.8;

    /// <summary>How much longer than the straight line the default limit allows the route to be.</summary>
    public const double RouteFactor = 1.5;

    /// <summary>The margin the default limit adds to each leg, in seconds.</summary>
    public const double LegMarginSeconds = 300;

    /// <summary>The margin the default limit adds for the login and the log off, in seconds.</summary>
    public const double LoginMarginSeconds = 180;

    /// <summary>The longest the collision and tiles round the start may take to settle, in seconds.</summary>
    public const double SettleSeconds = 120;

    /// <summary>The longest the placement teleport may take, in seconds.</summary>
    public const double TeleportSeconds = 90;

    /// <summary>
    /// The time the body must have been at rest (no movement command held, the position unchanged) before the end of a leg asks the server where it is, in
    /// seconds: at 9.6 m/s the server still had the body 0.6 m away when the query was sent the instant the run stopped (P5a live round 1). Not retail:
    /// Lifestone's own.
    /// </summary>
    public const double RestSeconds = 1.0;

    /// <summary>The longest the end of a leg waits for the body to rest, in seconds; past it the where-am-I is asked anyway and the line says so. Not retail: Lifestone's own.</summary>
    public const double RestMaxSeconds = 10.0;

    /// <summary>The runner's confirmation budget per step, in milliseconds (the Godot host's figure), so a long confirmation never stalls the net pump.</summary>
    public const double ConfirmBudgetMilliseconds = 2;

    private readonly LoginScenario _login;
    private readonly Play _play;

    /// <summary>The scenario.</summary>
    /// <param name="dats">The opened dats.</param>
    /// <param name="log">Where it logs.</param>
    /// <param name="options">The run (<see cref="WayfindOptions.To"/> is required).</param>
    /// <param name="character">The character to enter (the account's fighter template), or null.</param>
    /// <param name="arrivalSeconds">The limit on the login's arrival.</param>
    /// <param name="sinkOf">Where the body sends its positions: null for the session's movement channel behind the presence gate; a test gives its own sink.</param>
    public WayfindScenario(DatSet dats, ILogSink log, WayfindOptions options, string? character = null, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, Func<IGameSessionView, IMovementSink>? sinkOf = null)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(options);
        if (options.To is null)
        {
            throw new ArgumentException("--to is required by --scenario=wayfind", nameof(options));
        }

        _play = new Play(dats, log, options, sinkOf);
        _login = new LoginScenario(character, null, null, arrivalSeconds, _play);
    }

    /// <summary>The login's part of the scenario.</summary>
    public LoginScenario Login => _login;

    /// <inheritdoc/>
    public string Name => ScenarioName;

    /// <inheritdoc/>
    public string Stage => _login.Stage == "playing" ? "playing:" + _play.Stage : _login.Stage;

    /// <inheritdoc/>
    public string? Failure => _login.Failure;

    /// <inheritdoc/>
    public double? WhereMaxMetres
    {
        get
        {
            var (a, b) = (_login.WhereMaxMetres, _play.WhereMaxMetres);
            return a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
        }
    }

    /// <inheritdoc/>
    public long Unmatched => _play.Unmatched;

    /// <summary>The leg lines so far (<c>wayfind leg=out ok=... landblocks_peak=...</c>), in order.</summary>
    public IReadOnlyList<string> Lines => _play.Lines;

    /// <summary>The final line for a run that ended <paramref name="ok"/>: <c>wayfind done ok=... legs=...</c>.</summary>
    /// <param name="ok">Whether the run succeeded (the host's result).</param>
    public string DoneLine(bool ok) => string.Create(CultureInfo.InvariantCulture, $"wayfind done ok={(ok ? "true" : "false")} legs={_play.Lines.Count}");

    /// <summary>The streamer's line: the peak landblocks and tiles, the memory, and the longest stalls (report-only), or null when no streamer was made.</summary>
    public string? StreamerLine => _play.StreamerLine;

    /// <summary>The streamer (collision and tiles), or null before the player's body is made.</summary>
    public HeadlessStreamer? Streamer => _play.Streamer;

    /// <inheritdoc/>
    public ScenarioStatus Step(IGameSessionView session, double now) => _login.Step(session, now);

    /// <inheritdoc/>
    public void Dispose() => _play.Dispose();

    /// <summary>
    /// The default time limit of the run, in seconds: the login margin plus, for each leg, the straight line from <c>--from</c> (the Holtburg lifestone when none is given)
    /// to <c>--to</c> at <see cref="AssumedSpeedMetresPerSecond"/> times <see cref="RouteFactor"/>, plus <see cref="LegMarginSeconds"/>.
    /// </summary>
    /// <param name="options">The run.</param>
    public static double QuitAfterSeconds(WayfindOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var to = options.To is { } t ? GlobalOf(t) : Vector2.Zero;
        var from = options.From is { } f ? GlobalOf(f) : GlobalOf(new WayfindPlace(AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg").CellId, null));
        var legs = options.Back ? 2 : 1;
        return LoginMarginSeconds + (legs * ((Vector2.Distance(from, to) * RouteFactor / AssumedSpeedMetresPerSecond) + LegMarginSeconds));
    }

    private static Vector2 GlobalOf(WayfindPlace place) => PointOf(place, 0f).Global;

    /// <summary>The lattice point of a place: its landblock-local position, or an outdoor cell's centre, at height <paramref name="z"/> when the place has none.</summary>
    private static NavPoint PointOf(WayfindPlace place, float z)
    {
        if (place.Position is { } p)
        {
            return new NavPoint(place.CellId & 0xFFFF0000u, p);
        }

        var low = (int)(place.CellId & 0xFFFFu) - 1;
        return new NavPoint(place.CellId & 0xFFFF0000u, new Vector3(((low / 8) * 24f) + 12f, ((low % 8) * 24f) + 12f, z));
    }

    /// <summary>The in-world part of the scenario (the login scenario's <c>play</c>).</summary>
    private sealed class Play : IScenario, IDisposable
    {
        private const string LogCategory = "wayfind";

        private readonly DatSet _dats;
        private readonly ILogSink _log;
        private readonly WayfindOptions _options;
        private readonly Func<IGameSessionView, IMovementSink>? _sinkOf;
        private readonly HeadlessBody _body;
        private readonly List<string> _lines = [];
        private NavRunner? _runner;
        private INavPathSource? _source;
        private ActionHandle? _pending;
        private ToolHandle<WhereResult>? _where;
        private CellFrame _whereClient;
        private CellFrame _fromFrame;
        private CellFrame _start;
        private NavGoal? _goal;
        private string _leg = "out";
        private bool _placed;
        private bool _disposed;
        private int _teleportMark;
        private int _legTeleports;
        private int _legCorrectionsAtStart;
        private int _legPeak;
        private double _stageStart;
        private double _nextCorridor;
        private double _nextWhere;
        private double _lastStepMs = double.NaN;
        private double _stepGapMax;
        private string? _legFailure;
        private double? _whereStartMetres;
        private double? _whereEndMetres;
        private double _restSince = double.NaN;
        private CellFrame _restAt;
        private double _restSeconds;
        private double? _lastEndMetres;

        public Play(DatSet dats, ILogSink log, WayfindOptions options, Func<IGameSessionView, IMovementSink>? sinkOf)
        {
            (_dats, _log, _options, _sinkOf) = (dats, log, options, sinkOf);
            _body = new HeadlessBody(dats, log, LogCategory, sinkOf);
        }

        public string Name => ScenarioName;

        public string Stage { get; private set; } = "start";

        public string? Failure { get; private set; }

        public double? WhereMaxMetres => _body.Audit?.Summary.WhereMaxMetres;

        public long Unmatched => 0;

        public IReadOnlyList<string> Lines => _lines;

        public HeadlessStreamer? Streamer => _body.Streamer;

        public string? StreamerLine => _body.Streamer is { } s
            ? string.Create(CultureInfo.InvariantCulture, $"wayfind streamer landblocks_peak={s.PeakAttached} tiles_peak={s.PeakTiles} heap_peak_mb={_body.HeapPeak / 1048576.0:F0} pump_max_ms={s.PumpMaxMilliseconds:F1} attach_max_ms={s.AttachMaxMilliseconds:F1} tile_build_max_ms={s.TileBuildMaxMilliseconds:F1} tile_attach_max_ms={s.TileAttachMaxMilliseconds:F1} attaches={s.Attaches} releases={s.Releases} tile_builds={s.TileBuilds} tile_rebuilds={s.TileRebuilds} tile_failures={s.TileFailures} step_gap_max_ms={_stepGapMax:F1}")
            : null;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (Failure is not null)
            {
                return ScenarioStatus.Failed;
            }

            if (Stage == "done")
            {
                return ScenarioStatus.Succeeded;
            }

            try
            {
                var status = Run(session, now);
                if (status != ScenarioStatus.Running)
                {
                    Dispose();
                }

                return status;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _runner?.Dispose();
            _body.Dispose();
        }

        private ScenarioStatus Run(IGameSessionView session, double now)
        {
            var ms = session.Clock.Now * 1000.0; // the session clock (real time live): the gap between two steps is what the net pump waited
            if (!double.IsNaN(_lastStepMs))
            {
                _stepGapMax = Math.Max(_stepGapMax, ms - _lastStepMs);
            }

            _lastStepMs = ms;
            if (_body.Walker is null)
            {
                return Begin(session, now);
            }

            _body.Frame(now);
            if (_placed)
            {
                _legPeak = Math.Max(_legPeak, _body.World!.AttachedCount);
            }

            switch (Stage)
            {
                case "placing":
                    return Placing(session);
                case "settling":
                    return Settling(session);
                case "resting":
                    return Resting(session);
                case "where-start":
                case "where-end":
                    return Where(session);
                case "running":
                    return Running(session, now);
                default:
                    return Fail($"TODO(state): unknown wayfind stage '{Stage}'");
            }
        }

        private ScenarioStatus Begin(IGameSessionView session, double now)
        {
            var self = session.State.Self;
            if (!self.Qualities.IsKnown)
            {
                return Fail("begin: the player's qualities have not arrived (no PlayerDescription), so its run skill is unknown");
            }

            var to = _options.To!.Value;
            if (!LandDefs.IsOutdoorCell(to.CellId) || (_options.From is { } f && !LandDefs.IsOutdoorCell(f.CellId)))
            {
                return Fail("begin: TODO(wayfind-indoor): the run is outdoors; an indoor end is a journey (P5b)");
            }

            if (self.Position is not { } at)
            {
                return Fail("begin: the client has no position for the player");
            }

            if (_options.From is null && !LandDefs.IsOutdoorCell(at.CellId))
            {
                return Fail("begin: the player is indoors and no --from was given; the run starts outdoors");
            }

            _body.Create(session, now);
            var weenie = _body.Weenie!;
            _log.Info(
                LogCategory,
                "the wayfind scenario begins",
                new LogField("to", to.ToString()),
                new LogField("from", _options.From?.ToString() ?? "where the login left the player"),
                new LogField("back", _options.Back),
                new LogField("run_rate", weenie.TryGetRunRate(out var rate) ? rate : null));

            if (_options.From is { } from)
            {
                _fromFrame = FrameOf(from);
                if (!SameLandblock(at, _fromFrame) || at.Distance(_fromFrame) > FromToleranceMetres)
                {
                    var name = "wayfind start";
                    var destination = new TeleportDestination(name, [], new WorldLocation(name, new CellPosition(_fromFrame.CellId, _fromFrame.Origin), HasHeight: true, HeadingDegrees: 0), "scenario");
                    _teleportMark = _body.Teleports;
                    _pending = session.Tools.Place(destination, ActionSource.Host);
                    return Go("placing", session);
                }
            }

            return Go("settling", session);
        }

        private ScenarioStatus Placing(IGameSessionView session)
        {
            if (_pending is { Outcome: { } placed })
            {
                _pending = null;
                if (placed.Kind != ActionOutcomeKind.Confirmed)
                {
                    return Fail("placing: sending the player to --from: " + placed.Kind + (placed.Detail is null ? string.Empty : ": " + placed.Detail));
                }
            }

            if (_pending is null && _body.Teleports > _teleportMark && session.State.Self.Travel == SelfTravel.Present && session.State.Self.Position is { } at && SameLandblock(at, _fromFrame))
            {
                return Go("settling", session);
            }

            return session.Clock.Now - _stageStart > TeleportSeconds
                ? Fail(string.Create(CultureInfo.InvariantCulture, $"placing: the player was not present at --from within {TeleportSeconds:0.#} s (teleports {_body.Teleports - _teleportMark}, travel {session.State.Self.Travel})"))
                : ScenarioStatus.Running;
        }

        /// <summary>Waits for the collision and the tiles round the server's position, then places the client's body there.</summary>
        private ScenarioStatus Settling(IGameSessionView session)
        {
            if (session.State.Self.Position is not { } at || session.State.Self.Travel != SelfTravel.Present)
            {
                return session.Clock.Now - _stageStart > SettleSeconds ? Fail("settling: the player is not present") : ScenarioStatus.Running;
            }

            if (!_body.Settled(at))
            {
                return session.Clock.Now - _stageStart > SettleSeconds
                    ? Fail(string.Create(CultureInfo.InvariantCulture, $"settling: the collision and tiles round the start did not settle within {SettleSeconds:0.#} s ({_body.Streamer!.StatsLine()})"))
                    : ScenarioStatus.Running;
            }

            if (!_body.World!.IsAttached(LandblockCoord.FromId(at.CellId)))
            {
                return Fail("settling: the collision of the start's landblock failed to build (see the log)");
            }

            if (!_body.PlaceAt(at, session.Clock.Now))
            {
                return Fail("settling: no valid position for the client's body where the server put the player");
            }

            _placed = true;
            _start = _body.Walker!.Position;
            _log.Info(LogCategory, "the client's body is placed", new LogField("cell", $"0x{_start.CellId:X8}"), new LogField("streamer", _body.Streamer!.StatsLine()));
            _whereClient = _body.Walker.Position;
            _where = session.Tools.WhereAmI(ActionSource.Host);
            _pending = _where;
            return Go("where-start", session);
        }

        private ScenarioStatus Where(IGameSessionView session)
        {
            if (_pending is null)
            {
                if (session.Clock.Now < _nextWhere)
                {
                    return ScenarioStatus.Running;
                }

                _whereClient = _body.Walker!.Position;
                _where = session.Tools.WhereAmI(ActionSource.Host);
                _pending = _where;
                return ScenarioStatus.Running;
            }

            if (_pending.Outcome is not { } outcome)
            {
                return session.Clock.Now - _stageStart > TeleportSeconds ? Fail(Stage + ": no answer from the server about where the player is") : ScenarioStatus.Running;
            }

            _pending = null;
            if (outcome.Kind != ActionOutcomeKind.Confirmed || _where?.Result is not { } server)
            {
                return Fail(Stage + ": asking the server where the player is: " + outcome.Kind);
            }

            if (server.Teleporting)
            {
                _nextWhere = session.Clock.Now + 0.25;
                return ScenarioStatus.Running;
            }

            var metres = _body.Audit!.RecordWhere(_whereClient, server.Position, server.ForcePositionSequence);
            _log.Info(LogCategory, "the server's view of the player", new LogField("stage", Stage), new LogField("metres", metres), new LogField("rest_s", _restSeconds));
            if (Stage == "where-start")
            {
                _whereStartMetres = metres;
            }
            else
            {
                _whereEndMetres = metres;
            }

            if (metres > MovementAuditSummary.WhereToleranceMetres)
            {
                // Report-only in its first gate (the start and the end alike): the line carries where_start_m / where_end_m, no check sets ok=false.
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{Stage}: the server has the player {metres:0.00} m from the client's body (report-only) ({PositionAgreement.Describe(server.Position, _whereClient)})"));
            }

            return Stage == "where-start" ? StartLeg(session, "out") : EndLeg(session);
        }

        private ScenarioStatus StartLeg(IGameSessionView session, string leg)
        {
            _leg = leg;
            _legFailure = null;
            _whereEndMetres = null;
            if (leg == "back")
            {
                _whereStartMetres = _lastEndMetres; // the back leg starts where the out leg's end check found the body
            }

            _legTeleports = leg == "out" ? _body.Teleports - _teleportMark : 0;
            _legCorrectionsAtStart = _body.Audit!.Summary.Corrections;
            _legPeak = _body.World!.AttachedCount;
            var target = leg == "out" ? NavGoal.ToPoint(PointOf(_options.To!.Value, GroundOf(_options.To.Value))) : NavGoal.ToPoint(NavMap.FromCellFrame(_start));
            _goal = target;
            _source = new OutdoorPathSource(new PathPlanner(_body.Map!));
            _runner?.Dispose();
            _runner = _body.NewRunner(_source, "wayfind-" + leg, ConfirmBudgetMilliseconds);
            _runner.Start(target, session.Clock.Now);
            _nextCorridor = 0;
            _log.Info(LogCategory, "the leg begins", new LogField("leg", leg), new LogField("goal", target.Point.ToString()));
            return Go("running", session);
        }

        private float GroundOf(WayfindPlace place)
        {
            var local = PointOf(place, 0f).Local;
            return HeadlessBody.TerrainZ(_dats, place.CellId, local.X, local.Y);
        }

        private CellFrame FrameOf(WayfindPlace place)
        {
            var local = PointOf(place, 0f).Local;
            if (place.Position is null)
            {
                local = local with { Z = GroundOf(place) + 0.5f };
            }

            return new CellFrame(place.CellId, new AcFrame(local, Quaternion.Identity));
        }

        private ScenarioStatus Running(IGameSessionView session, double now)
        {
            var runner = _runner!;
            if (session.Clock.Now >= _nextCorridor)
            {
                _nextCorridor = session.Clock.Now + CorridorSeconds;
                UpdateCorridor();
            }

            var state = runner.Step(now);
            if (!runner.IsDone)
            {
                return ScenarioStatus.Running;
            }

            if (state != NavRunState.Arrived)
            {
                _legFailure = $"the run ended {state}: {runner.FailureReason ?? "no reason given"}";
            }

            // The server's view at the end of the leg, once the body has been at rest for RestSeconds (the runner stopped its keys; the body still glides on).
            _restSince = double.NaN;
            _restSeconds = 0;
            return Go("resting", session);
        }

        private ScenarioStatus Resting(IGameSessionView session)
        {
            var now = session.Clock.Now;
            var at = _body.Walker!.Position;
            var still = _body.Walker.Controller.Motion.IsStandingStill() && Vector3.DistanceSquared(at.Origin, _restAt.Origin) < 1e-6f && at.CellId == _restAt.CellId;
            _restAt = at;
            if (!still)
            {
                _restSince = double.NaN;
            }
            else if (double.IsNaN(_restSince))
            {
                _restSince = now;
            }

            var rested = !double.IsNaN(_restSince) && now - _restSince >= RestSeconds;
            var timedOut = now - _stageStart >= RestMaxSeconds;
            if (!rested && !timedOut)
            {
                return ScenarioStatus.Running;
            }

            _restSeconds = now - _stageStart;
            _log.Info(LogCategory, rested ? "the body is at rest" : "the body did not come to rest in time; asking anyway", new LogField("settle_s", _restSeconds));
            _whereClient = _body.Walker.Position;
            _where = session.Tools.WhereAmI(ActionSource.Host);
            _pending = _where;
            return Go("where-end", session);
        }

        private void UpdateCorridor()
        {
            _body.FollowRoute(_runner!);
        }

        private ScenarioStatus EndLeg(IGameSessionView session)
        {
            var runner = _runner!;
            runner.Dispose();
            var summary = _body.Audit!.Summary;
            var corrections = summary.Corrections - _legCorrectionsAtStart;
            var arrived = runner.State == NavRunState.Arrived;
            if (corrections > 0 && _legFailure is null)
            {
                _legFailure = $"the server corrected the player {corrections} time(s) during the leg";
            }

            var ok = arrived && _legFailure is null;
            _lastEndMetres = _whereEndMetres;
            string Metres(double? m) => m is { } v ? v.ToString("0.00", CultureInfo.InvariantCulture) : "none";
            var legWhereMax = _whereStartMetres is { } ws && _whereEndMetres is { } we ? Math.Max(ws, we) : _whereStartMetres ?? _whereEndMetres;
            var seconds = runner.RunSeconds;
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"wayfind leg={_leg} ok={(ok ? "true" : "false")} route_m={runner.RouteMetres:0.0} run_s={seconds:0.0} speed_mps={(seconds > 0 ? runner.RouteMetres / seconds : 0):0.00} plans={runner.Plans} replans={runner.Replans} rungs={runner.Rungs} blocked={(runner.State == NavRunState.Blocked ? 1 : 0)} stuck={runner.Stuck} corrections={corrections} where_start_m={Metres(_whereStartMetres)} where_end_m={Metres(_whereEndMetres)} where_max_m={Metres(legWhereMax)} settle_s={_restSeconds:0.0} teleports={_legTeleports} landblocks_peak={_legPeak} turns={runner.Turns} key_changes_per_min={runner.KeyChangesPerMinute:0}");
            _lines.Add(line);
            _log.Info(LogCategory, line + (_legFailure is null ? string.Empty : " -- " + _legFailure));
            _log.Info(LogCategory, "streamer: " + _body.Streamer!.StatsLine());
            if (!ok)
            {
                return Fail($"the {_leg} leg did not succeed: {_legFailure}");
            }

            if (_leg == "out" && _options.Back)
            {
                return StartLeg(session, "back");
            }

            Stage = "done";
            return ScenarioStatus.Succeeded;
        }

        private static bool SameLandblock(CellFrame a, CellFrame b) => (a.CellId >> 16) == (b.CellId >> 16);

        private ScenarioStatus Go(string stage, IGameSessionView session)
        {
            Stage = stage;
            _stageStart = session.Clock.Now;
            return ScenarioStatus.Running;
        }

        private ScenarioStatus Fail(string why)
        {
            _runner?.Dispose(); // no key stays held
            Failure = why;
            _log.Error(LogCategory, why);
            return ScenarioStatus.Failed;
        }
    }
}
