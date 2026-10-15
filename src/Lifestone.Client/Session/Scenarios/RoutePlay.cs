using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Navigation;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Tools;
using Lifestone.Client.World;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The in-world part of the movement scenarios (step ST16; DoD 8): the player, already in the world, is sent to the
/// route's start with the server mod's <c>place loc</c>, walks the route the way a person at the keyboard would, and the
/// walk is audited against what ACE makes of it. <see cref="LoginScenario"/> runs it (its <c>play</c> stage) between the
/// arrival and the logoff; <see cref="HoltburgLapsScenario"/> is the composition.
/// </summary>
/// <remarks>
/// <para><b>Stages</b> (<see cref="Stage"/>):</para>
/// <list type="number">
/// <item><c>placing</c>: the walker is made on the first step: an <see cref="OfflineWalker"/> in the physics world the
/// scenario was given, with the session's <see cref="QualitiesWeenie"/> (the character's own run and jump skills, read from
/// <c>Self.Qualities</c> and the dats' SkillTable), whose movement messages go through a <see cref="MovementAudit"/> and a
/// <see cref="PresenceGatedMovementSink"/> (nothing is sent while the server has the player hidden or teleporting) to the
/// session's movement channel. <see cref="PlayerTools.Place"/> sends the player to <see cref="ScriptedRoute.Start"/>.</item>
/// <item><c>teleporting</c>: portal space plays the teleport and sends LoginComplete (the headless host's director and bridge,
/// as on screen); the player's own position updates are applied to the walker (<see cref="PlayerMovementController.Apply"/>:
/// the teleport places it, an echo constrains it, a correction blips it); the stage ends when the teleport arrived at the
/// start and the player is <see cref="SelfTravel.Present"/>, or fails after <see cref="TeleportSeconds"/>.</item>
/// <item><c>settling</c>: <see cref="PlayerTools.WhereAmI"/> until the server no longer has the player teleporting (ACE
/// ignores a position while it does), recorded as the first where-am-I check.</item>
/// <item><c>walking</c>: each frame the controller ticks and then the <see cref="RouteDriver"/>. At each stop the audit's lap
/// is completed, and once per stop, 1.1 s into the 1.5 s dwell (the 1.0 s cadence has sent the rest position), where-am-I is
/// asked and the server's answer recorded against the client's position at the time of the ask
/// (<see cref="MovementAudit.RecordWhere"/>). A route that is <see cref="RouteStatus.Stuck"/> or
/// <see cref="RouteStatus.Failed"/>, or takes longer than <see cref="WalkSeconds"/>, fails the scenario.</item>
/// <item><c>verdict</c>: the movement summary line is logged; <see cref="MovementAuditSummary.Passed"/> (no corrections, every
/// where-am-I within 0.5 m, the force-position sequence unchanged) and at least one check per lap are required.</item>
/// <item><c>returning</c> (unless <c>returnHome</c> is off): the character is sent back with <c>place loc</c> to where the
/// scenario found it (the position the login arrived at), movement stops being sent, and once it is present again the
/// server must agree it is there within <see cref="HomeToleranceMetres"/> (<c>restoring</c>). The characters of the
/// loopback tests and the game's login check must be left at their starts.</item>
/// </list>
/// <para>The scenario acts through <see cref="IGameSessionView"/> only. It does not disconnect: the caller logs off. Game
/// thread only.</para>
/// </remarks>
public sealed partial class RoutePlay : IScenario, IDisposable
{
    /// <summary>The limit on the teleport to the route's start, in seconds on the session clock (portal space takes 5 s or more).</summary>
    public const double TeleportSeconds = 90;

    /// <summary>The limit on the server ending the player's teleporting, in seconds.</summary>
    public const double SettleSeconds = 30;

    /// <summary>The limit on the walk, in seconds on the session clock (three Holtburg laps take about 25 s on the ground).</summary>
    public const double WalkSeconds = 300;

    /// <summary>How far into a stop's dwell where-am-I is asked (the cadence has sent the rest position by then).</summary>
    public const double WhereDelaySeconds = 1.1;

    /// <summary>How near the route's start the teleport must arrive, in metres (the server places the player on the ground there).</summary>
    public const float StartToleranceMetres = 2f;

    /// <summary>How near the character's home the server must say it is after the return, in metres.</summary>
    public const float HomeToleranceMetres = 1f;

    private const string LogCategory = "scenario";

    private readonly DatSet _dats;
    private readonly PhysicsWorld _world;
    private readonly PhysicsGeometryLibrary _geometry;
    private readonly ILogSink _log;
    private readonly bool _returnHome;
    private readonly PortalExit? _exit;
    private readonly RoutePlayHost? _host;
    private readonly ExitQuestMode _exitQuest;
    private IGameSessionView? _session;
    private long _heldBase;
    private double? _beginWaitStart;
    private ScriptedRoute _route;
    private readonly List<ObjectMoved> _moves = [];
    private CellFrame? _home;
    private int _selfTeleports;
    private int _homeMark;
    private bool _returning;
    private IDisposable? _movesSubscription;
    private OfflineWalker? _walker;
    private MovementAudit? _audit;
    private PresenceGatedMovementSink? _gate;
    private RouteDriver? _driver;
    private ActionHandle? _pending;
    private ToolHandle<WhereResult>? _where;
    private CellFrame _whereClient;
    private bool _placed;
    private bool _arrivedAtStart;
    private bool _wasDwelling;
    private bool _whereAsked;
    private double _stageStart;
    private double _dwellStart;
    private double _nextWhere;
    private bool _disposed;

    /// <summary>A play of <paramref name="route"/> in <paramref name="world"/> (the collision of its landblocks, attached).</summary>
    /// <param name="dats">The dats (the human Setup and motion table, the SkillTable).</param>
    /// <param name="world">The physics world with the route's landblocks attached; the scenario's walker joins it.</param>
    /// <param name="geometry">The collision shapes the world was built with.</param>
    /// <param name="route">The route; its <see cref="ScriptedRoute.Start"/> is required.</param>
    /// <param name="log">Where the scenario logs.</param>
    /// <param name="returnHome">True to send the character back to where it was found when the walk passed.</param>
    /// <param name="exit">A portal to leave through when the walk ends (the Academy exit, <see cref="PortalExit"/>), or null.
    /// With one the route is planned once the player stands at its start and the portal is known, so
    /// <paramref name="route"/> only needs a name and a start.</param>
    /// <param name="host">The game's player as the walker (<see cref="RoutePlayHost"/>), or null to make the scenario's own walker in <paramref name="world"/>.
    /// With a host the walker, its ticks, its placement by the teleports and the server's moves and MoveTos belong to the game; the scenario drives the
    /// walker's controller and audits what it sends, and <paramref name="world"/> and <paramref name="geometry"/> only served to plan the route.</param>
    /// <param name="exitQuest">What to do about the quest the exit portal needs (<see cref="ExitQuestMode"/>): <see cref="ExitQuestMode.Stamp"/> (the default) holds
    /// it for the Use and expects the teleport; <see cref="ExitQuestMode.Erase"/> does not hold it and takes ACE's refusal (<see cref="QuestRefusalWeenieError"/>
    /// after the use) as the outcome, once the server's moves to the portal were followed and the server and the walker agree where that is
    /// (<see cref="ExitRefusedByQuest"/>). Either way the registry is asked after the way home and the quest brought back to what the scenario found
    /// (<see cref="QuestVerdict"/>); the portal's emote set, which the taken exit runs, also moves the sanctuary and erases nine other quests, and that stays: graduated is the test characters' baseline, by the owner's decision (docs/session.md section 25).</param>
    /// <param name="jumps">True to add the jump leg after the laps (<see cref="JumpLeg"/>: a standing and a running jump, then where-am-I); ignored with an <paramref name="exit"/>.</param>
    /// <exception cref="ArgumentException">The route has no start.</exception>
    public RoutePlay(DatSet dats, PhysicsWorld world, PhysicsGeometryLibrary geometry, ScriptedRoute route, ILogSink log, bool returnHome = true, PortalExit? exit = null, RoutePlayHost? host = null, ExitQuestMode exitQuest = ExitQuestMode.Stamp, bool jumps = false)
    {
        _jumps = jumps;
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(log);
        if (route.Start is null)
        {
            throw new ArgumentException("the route has no start to send the player to", nameof(route));
        }

        (_dats, _world, _geometry, _route, _log, _returnHome, _exit, _host, _exitQuest) = (dats, world, geometry, route, log, returnHome, exit, host, exitQuest);
    }

    /// <inheritdoc/>
    public string Name => _route.Name;

    /// <inheritdoc/>
    public string Stage { get; private set; } = "start";

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <inheritdoc/>
    public double? WhereMaxMetres => _audit?.Summary.WhereMaxMetres;

    /// <inheritdoc/>
    /// <remarks>
    /// The position-carrying messages (MoveToState, AutonomousPosition, Jump) the session sent to the server (those the
    /// presence gate did not hold back) that the server never echoed as an UpdatePosition of the player
    /// (<see cref="MovementAuditSummary.SelfEchoes"/>), never below 0; 0 before the walk. TODO(st16-unmatched): ACE's echo
    /// rule for every message is not pinned (it echoed each one in the b1 walk); the number is reported, not asserted.
    /// </remarks>
    public long Unmatched
    {
        get
        {
            if (_audit?.Summary is not { } s)
            {
                return 0;
            }

            return Math.Max(0, (long)s.MoveToStateSent + s.AutonomousPositionSent + s.JumpSent - HeldNow - s.SelfEchoes);
        }
    }

    /// <summary>The audit's counts so far, or null before the walker exists.</summary>
    public MovementAuditSummary? Audit => _audit?.Summary;

    /// <summary>The movement verdict line (<see cref="MovementAudit.FormatSummaryLine"/>) once the walk ended, else null.</summary>
    public string? SummaryLine { get; private set; }

    /// <summary>
    /// The audit's summary as JSON (<see cref="MovementAudit.ToJson"/>) once the walk ended, else null: the host writes it to <c>cache/movement/&lt;run&gt;.json</c> (this layer does no file I/O;
    /// the login check does, <c>LoginCheckRunner</c>).
    /// </summary>
    public string? SummaryJson { get; private set; }

    /// <summary>The run rate the character's qualities gave the walker when it was made (<see cref="QualitiesWeenie"/>), or null before.</summary>
    public float? RunRate { get; private set; }

    /// <summary>The walker, or null before the first step.</summary>
    public OfflineWalker? Walker => _walker;

    private long HeldNow => _host is { } host ? host.Held - _heldBase : _gate?.Held ?? 0;

    /// <inheritdoc/>
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

    /// <summary>Stops following the session.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        PutTheQuestBackAfterFailure(); // a play ended from outside (a timeout, a quit) after it changed the registry: say so, and send the cleanup if it is safe
        _driver?.Abandon(); // however the play ends, no key stays held: hosted, the walker is the game's real player controller
        if (_host is { } host && ReferenceEquals(host.Current, _audit))
        {
            host.Current = null;
            host.Hold = null;
        }

        _movesSubscription?.Dispose();
        _motionSubscription?.Dispose();
        _teleportSubscription?.Dispose();
        _audit?.Dispose();
    }

    private ScenarioStatus Run(IGameSessionView session, double now)
    {
        _session = session;
        if (_walker is null)
        {
            return Begin(session, now);
        }

        DrainMoves(now);
        ApplyServerMotions();
        if (_placed && _host is null)
        {
            _walker.Controller.Tick(now); // with a host the player controller ticks its own walker
        }

        switch (Stage)
        {
            case "placing":
                if (_pending is { Outcome: { } placed })
                {
                    _pending = null;
                    return placed.Kind == ActionOutcomeKind.Confirmed ? Go("teleporting", session) : Fail("placing the player at the route's start", placed);
                }

                break;

            case "teleporting":
                if (_arrivedAtStart && session.State.Self.Travel == SelfTravel.Present)
                {
                    _pending = null;
                    _nextWhere = 0;
                    return Go("settling", session);
                }

                if (session.Clock.Now - _stageStart > TeleportSeconds)
                {
                    return Fail(string.Create(
                        CultureInfo.InvariantCulture,
                        $"teleporting: the player did not arrive at the route's start and become present within {TeleportSeconds:0.#} s (arrived {_arrivedAtStart}, travel {session.State.Self.Travel})"));
                }

                break;

            case "settling":
                return Settle(session, now);

            case "walking":
                return Walk(session, now);

            case "jumping":
                return Jumping(session, now);

            case "jump-where":
                return JumpWhere(session);

            case "quest-check":
                return QuestCheck(session);

            case "quest-change":
                return QuestApply(session);

            case "using":
                return UsePortal(session);

            case "exiting":
                return Exiting(session);

            case "exit-where":
                return ExitWhere(session);

            case "refused-where":
                return RefusedWhere(session);

            case "verdict":
                return Verdict(session);

            case "returning":
                if (_pending is { Outcome: { } homePlaced })
                {
                    _pending = null;
                    return homePlaced.Kind == ActionOutcomeKind.Confirmed ? Go("returning-teleport", session) : Fail("returning the character to where it was found", homePlaced);
                }

                break;

            case "returning-teleport":
                if (_selfTeleports > _homeMark && session.State.Self.Travel == SelfTravel.Present)
                {
                    _pending = null;
                    _nextWhere = 0;
                    return Go("restoring", session);
                }

                return Within(session, TeleportSeconds, "returning: the character did not arrive home and become present");

            case "restoring":
                return Restore(session);

            case "quest-restore":
                return QuestRestore(session);

            default:
                return Fail($"TODO(state): unknown route-play stage '{Stage}'");
        }

        return ScenarioStatus.Running;
    }

    /// <summary>Makes the walker, the audit and the driver, and sends the player to the route's start.</summary>
    private ScenarioStatus Begin(IGameSessionView session, double now)
    {
        var self = session.State.Self;
        if (!self.Qualities.IsKnown)
        {
            return Fail("placing: the player's qualities have not arrived (no PlayerDescription), so its run and jump skills are unknown");
        }

        // The Academy exit sends the character home to the Academy start (where a new character begins), not to where the login
        // found it: an earlier run that failed at the portal left it elsewhere in the dungeon.
        _home = _exit is null ? self.Position : _exit.Home ?? _route.Start;
        IPhysicsWeenie weenie;
        if (_host is { } host)
        {
            // The game's player: its walker is made with the player, placed by the teleports and ticked by the player controller.
            if (host.Walker is not { } own || host.Inner is not { } behind)
            {
                _beginWaitStart ??= session.Clock.Now;
                return session.Clock.Now - _beginWaitStart > SettleSeconds ? Fail("placing: the game has no player body to drive") : ScenarioStatus.Running;
            }

            weenie = own.Weenie;
            _walker = own;
            _placed = true;
            _heldBase = host.Held;
            var hosted = new MovementAudit(behind, session.Events, () => session.Clock.Now, _route.Name);
            _audit = hosted;
            host.Current = hosted;
            host.Hold = () => _returning || _movementHeld;
        }
        else
        {
            weenie = QualitiesWeenie.FromSkillTable(self.Qualities, SkillTableData.Load(_dats), _log);
            var gate = _gate = new PresenceGatedMovementSink(session.Actions.Movement, () => !_returning && !_movementHeld && session.State.Self.Travel == SelfTravel.Present);
            _audit = new MovementAudit(gate, session.Events, () => session.Clock.Now, _route.Name);
            _world.SetTime(now);
            _walker = OfflineWalker.Create(_dats, _world, _log, weenie, _geometry, id: self.Guid, sink: _audit);
        }

        var audit = _audit!;
        if (_exit is null)
        {
            _driver = new RouteDriver(_walker.Controller, _route);
            _driver.Stopped += _ => audit.CompleteLap();
        }

        _movesSubscription = session.Events.Subscribe<ObjectMoved>((in ObjectMoved e) =>
        {
            if (e.IsSelf)
            {
                _moves.Add(e);
                if (e.Verdict == PositionVerdict.SelfTeleport)
                {
                    _selfTeleports++;
                    NoteSelfTeleport(e);
                }
            }
        });
        BeginServerMovement(session);
        RunRate = weenie is QualitiesWeenie qualities && qualities.TryGetRunRate(out var runRate) ? runRate : null;
        _log.Info(
            LogCategory,
            _host is null ? "the walker is made; sending the player to the route's start" : "the game's player is the walker; sending the player to the route's start",
            new LogField("route", _route.Name),
            new LogField("run_rate", RunRate),
            new LogField("run_skill", (weenie as QualitiesWeenie)?.RunSkill),
            new LogField("run_skill_init", self.Qualities.TryGetSkill(QualitiesWeenie.RunSkillId, out var runSkill) ? runSkill.Init : null),
            new LogField("run_skill_ranks", runSkill.Ranks),
            new LogField("run_skill_class", runSkill.AdvancementClass),
            new LogField("jump_skill", (weenie as QualitiesWeenie)?.JumpSkill),
            new LogField("load", (weenie as QualitiesWeenie)?.Load));

        var start = _route.Start!.Value;
        var destination = new TeleportDestination(
            _route.Name + " start",
            [],
            new WorldLocation(_route.Name + " start", new CellPosition(start.CellId, start.Frame.Origin), HasHeight: true, HeadingDegrees: RetailFrames.GetHeading(start.Frame)),
            "scenario");
        var handle = session.Tools.Place(destination, ActionSource.Host);
        _pending = handle;
        return Go("placing", session);
    }

    /// <summary>Hands the player's position updates to the walker, in order, as retail's <c>SmartBox::HandleReceivedPosition</c> would.</summary>
    private void DrainMoves(double now)
    {
        if (_moves.Count == 0)
        {
            return;
        }

        var start = _route.Start!.Value;
        foreach (var move in _moves)
        {
            if (_host is null)
            {
                var coord = LandblockCoord.FromId(move.Position.CellId);
                if (!_world.IsAttached(coord))
                {
                    // The login's own arrival at the Academy: the scenario's world holds only the route's landblocks.
                    _log.Debug(LogCategory, "a position of the player outside the scenario's physics world is not applied", new LogField("cell", $"0x{move.Position.CellId:X8}"));
                    continue;
                }

                if (!_placed)
                {
                    if (move.Verdict != PositionVerdict.SelfTeleport)
                    {
                        continue;
                    }

                    if (!_walker!.Place(move.Position))
                    {
                        _log.Error(LogCategory, "no valid position for the walker where the server put the player", new LogField("cell", $"0x{move.Position.CellId:X8}"));
                        continue;
                    }

                    _placed = true;
                }

                _walker!.Controller.Apply(move, now); // with a host, play mode applies the player's positions and teleports
            }

            if (move.Verdict == PositionVerdict.SelfTeleport && move.Position.Distance(start) <= StartToleranceMetres)
            {
                _arrivedAtStart = true;
            }
        }

        _moves.Clear();
    }

    private ScenarioStatus Settle(IGameSessionView session, double now)
    {
        if (_pending is null)
        {
            if (session.Clock.Now < _nextWhere)
            {
                return Within(session, SettleSeconds, "settling: the server still had the player teleporting");
            }

            if (_host is not null && (_walker!.Physics.PlacementPending || _walker.Physics.Cell is null))
            {
                return Within(session, SettleSeconds, "settling: the game's player body was not placed where the server put it");
            }

            _whereClient = _walker!.Position;
            _where = session.Tools.WhereAmI(ActionSource.Host);
            _pending = _where;
            return ScenarioStatus.Running;
        }

        if (_pending.Outcome is not { } outcome)
        {
            return Within(session, SettleSeconds, "settling: no answer from the server about where the player is");
        }

        _pending = null;
        if (outcome.Kind != ActionOutcomeKind.Confirmed || _where?.Result is not { } server)
        {
            return Fail("settling: asking the server where the player is", outcome);
        }

        if (server.Teleporting)
        {
            _nextWhere = session.Clock.Now + 0.25;
            return Within(session, SettleSeconds, "settling: the server still had the player teleporting");
        }

        var metres = _audit!.RecordWhere(_whereClient, server.Position, server.ForcePositionSequence);
        _log.Info(
            LogCategory,
            "the server's view of the player at the route's start",
            new LogField("metres", metres),
            new LogField("force_position_sequence", server.ForcePositionSequence));
        _wasDwelling = false;
        if (_exit is not null && PlanExitRoute(session) is { } planFailure)
        {
            return planFailure;
        }

        return Go("walking", session);
    }

    private ScenarioStatus Walk(IGameSessionView session, double now)
    {
        if (_exit is not null && _portal is null && _pending is null && WatchForPortal(session) is { } replanFailure)
        {
            return replanFailure;
        }

        if (_exit is not null)
        {
            _visited.Add(_walker!.Position.CellId);
        }

        var driver = _driver!;
        var status = driver.Tick(now);
        if (status == RouteStatus.Stuck || status == RouteStatus.Failed)
        {
            LogStuckWalker(status);
            return Fail(driver.FailureReason ?? "the route failed without saying why");
        }

        // Settle the outstanding where-am-I check.
        if (_pending is { Outcome: { } outcome })
        {
            _pending = null;
            if (outcome.Kind != ActionOutcomeKind.Confirmed || _where?.Result is not { } server)
            {
                return Fail("walking: asking the server where the player is", outcome);
            }

            var metres = _audit!.RecordWhere(_whereClient, server.Position, server.ForcePositionSequence);
            _log.Info(
                LogCategory,
                "where-am-I at a stop",
                new LogField("lap", driver.Lap),
                new LogField("metres", metres),
                new LogField("force_position_sequence", server.ForcePositionSequence));
        }

        if (driver.Dwelling && !_wasDwelling)
        {
            _dwellStart = now;
            _whereAsked = false;
        }

        _wasDwelling = driver.Dwelling;
        if (driver.Dwelling && !_whereAsked && _pending is null && now - _dwellStart >= WhereDelaySeconds)
        {
            _whereAsked = true;
            _whereClient = _walker!.Position;
            _where = session.Tools.WhereAmI(ActionSource.Host);
            _pending = _where;
        }

        if (status == RouteStatus.Done)
        {
            if (_pending is not null)
            {
                return ScenarioStatus.Running;
            }

            if (_exit is not null && _portal is null)
            {
                return NextSearchLeg();
            }

            return Go(_exit is not null ? "quest-check" : _jumps && _jump is null ? "jumping" : "verdict", session);
        }

        if (session.Clock.Now - _stageStart > WalkSeconds)
        {
            return Fail(string.Create(
                CultureInfo.InvariantCulture,
                $"walking: the route '{_route.Name}' did not finish within {WalkSeconds:0.#} s (lap {driver.Lap}, goal {driver.GoalIndex})"));
        }

        return ScenarioStatus.Running;
    }

    /// <summary>
    /// A walk that stalls names what the walker's world looked like at that moment: which landblocks of the 3 x 3 around the player's have collision attached, whether the
    /// player has ground contact, and its motion state (<c>motion=</c>: <c>active</c>, <c>in_cell</c>, <c>server</c>, the held keys and the interpreted forward command).
    /// <c>TODO(holtburg-ring-stall)</c> (docs/session.md section 27): the walker now and then stops with its keys held. It was seen in the game's Holtburg ring and the Academy wing
    /// earlier, and, with no server at all, once in 11 runs of the offline <c>holtburg-lap</c> check (at ring point 1); the earlier rate in the game did not recur in the runs
    /// that followed. It is not ACE, the quest or the exit, and the suspect is a frame-timing family (the cellar check's cell-tracking failure may be the same). The next stall's
    /// <c>motion=</c> field tells a parked physics object (<c>active</c> or <c>in_cell</c> false), a lost motion (keys held, interpreted forward command 0) and a wall apart.
    /// </summary>
    private void LogStuckWalker(RouteStatus status)
    {
        var at = _walker!.Position;
        var coord = LandblockCoord.FromId(at.CellId);
        var attached = new List<string>();
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (coord.Offset(dx, dy) is { } c)
                {
                    attached.Add($"{c}{(_walker.World.IsAttached(c) ? "+" : "-")}");
                }
            }
        }

        var near = _session is null
            ? []
            : _session.State.Objects.All
                .Where(o => !o.IsSelf && o.Position is { } p && PositionAgreement.DistanceMetres(at, p) <= 8.0)
                .Select(o => (Object: o, Metres: PositionAgreement.DistanceMetres(at, o.Position!.Value)))
                .OrderBy(t => t.Metres)
                .Take(8)
                .Select(t => string.Create(CultureInfo.InvariantCulture, $"{t.Object.Name}#{t.Object.Guid:X8}(wcid {t.Object.Weenie.WeenieClassId}, {t.Metres:0.0} m)"))
                .ToList();
        _log.Warn(
            LogCategory,
            "TODO(holtburg-ring-stall): the walker stalled; the collision around it, its contact and the server objects within 8 m are listed",
            new LogField("status", status.ToString()),
            new LogField("cell", $"0x{at.CellId:X8}"),
            new LogField("at", at.Frame.Origin),
            new LogField("landblocks_attached", string.Join(' ', attached)),
            new LogField("objects_near", near.Count == 0 ? "none" : string.Join("; ", near)),
            new LogField("has_contact", _walker.Physics.HasContact),
            new LogField("on_walkable", _walker.Physics.OnWalkable),
            new LogField("moving_to", _walker.Physics.IsMovingTo),
            new LogField("server_controlled", _walker.Controller.ControlledByServer),
            new LogField("hosted", _host is not null),
            new LogField("motion", _walker.Controller.DescribeMotion()));
    }

    private ScenarioStatus Verdict(IGameSessionView session)
    {
        var audit = _audit!;
        var summary = audit.Summary;
        SummaryLine = audit.SummaryLine();
        SummaryJson = audit.ToJson();
        _log.Info(LogCategory, SummaryLine);
        if (!summary.Passed)
        {
            return Fail(string.Create(
                CultureInfo.InvariantCulture,
                $"the movement audit failed: {SummaryLine} (selfEchoes {summary.SelfEchoes}, force-position sequence {summary.ForcePositionSequenceFirst} to {summary.ForcePositionSequenceLast})"));
        }

        if (summary.WhereChecks < _route.Laps)
        {
            return Fail(string.Create(
                CultureInfo.InvariantCulture,
                $"the movement audit made {summary.WhereChecks} where-am-I checks for {_route.Laps} laps: {SummaryLine}"));
        }

        if (!_returnHome || _home is not { } home)
        {
            if (_returnHome)
            {
                _log.Warn(LogCategory, "the character is not sent home: the login arrival gave no position to return to");
            }

            return Done(session);
        }

        _returning = true;
        _homeMark = _selfTeleports;
        var destination = new TeleportDestination(
            "home",
            [],
            new WorldLocation("home", new CellPosition(home.CellId, home.Frame.Origin), HasHeight: true, HeadingDegrees: RetailFrames.GetHeading(home.Frame)),
            "scenario");
        _pending = session.Tools.Place(destination, ActionSource.Host);
        return Go("returning", session);
    }

    /// <summary>After the return: the server must have the character where it was found.</summary>
    private ScenarioStatus Restore(IGameSessionView session)
    {
        if (_pending is null)
        {
            if (session.Clock.Now < _nextWhere)
            {
                return Within(session, SettleSeconds, "restoring: the server still had the character teleporting");
            }

            _where = session.Tools.WhereAmI(ActionSource.Host);
            _pending = _where;
            return ScenarioStatus.Running;
        }

        if (_pending.Outcome is not { } outcome)
        {
            return Within(session, SettleSeconds, "restoring: no answer from the server about where the character is");
        }

        _pending = null;
        if (outcome.Kind != ActionOutcomeKind.Confirmed || _where?.Result is not { } server)
        {
            return Fail("restoring: asking the server where the character is", outcome);
        }

        if (server.Teleporting)
        {
            _nextWhere = session.Clock.Now + 0.25;
            return Within(session, SettleSeconds, "restoring: the server still had the character teleporting");
        }

        var metres = PositionAgreement.DistanceMetres(server.Position, _home!.Value);
        _log.Info(LogCategory, "the character is home again", new LogField("metres", metres), new LogField("cell", $"0x{server.Position.CellId:X8}"));
        if (metres > HomeToleranceMetres)
        {
            return Fail(string.Create(
                CultureInfo.InvariantCulture,
                $"restoring: the server has the character {metres:0.00} m from where it was found ({PositionAgreement.Describe(server.Position, _home.Value)}); it must be put back by hand"));
        }

        return Done(session);
    }

    /// <summary>The play's work is over: puts the quest registry back first when an exit scenario changed it, else it has succeeded.</summary>
    private ScenarioStatus Done(IGameSessionView session)
    {
        if (QuestRestoreNeeded)
        {
            return Go("quest-restore", session);
        }

        Stage = "done";
        return ScenarioStatus.Succeeded;
    }

    private ScenarioStatus Within(IGameSessionView session, double seconds, string why) =>
        session.Clock.Now - _stageStart > seconds
            ? Fail(string.Create(CultureInfo.InvariantCulture, $"{why} after {seconds:0.#} s"))
            : ScenarioStatus.Running;

    private ScenarioStatus Go(string stage, IGameSessionView session)
    {
        Stage = stage;
        _stageStart = session.Clock.Now;
        return ScenarioStatus.Running;
    }

    private ScenarioStatus Fail(string step, in ActionOutcome outcome) =>
        Fail(step + ": " + outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) +
             (outcome.Detail is null ? string.Empty : ": " + outcome.Detail));

    private ScenarioStatus Fail(string why)
    {
        _driver?.Abandon(); // a failed play leaves no key held: the runner may not dispose the play before the recovery (hosted, the keys are the game's own)
        PutTheQuestBackAfterFailure();
        Failure = why;
        return ScenarioStatus.Failed;
    }
}
