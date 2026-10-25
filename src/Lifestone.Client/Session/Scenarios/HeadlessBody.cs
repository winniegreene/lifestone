using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Navigation;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Dat;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Scenery;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The client's own body in a headless run (M4 P5b J4, factored out of <see cref="WayfindScenario"/>, P5a S6): an <see cref="OfflineWalker"/> over the
/// <see cref="HeadlessStreamer"/>'s collision and tiles, its controller bound to <see cref="IActionApi.Move"/> (<see cref="MoveIntents.Bind"/>: the runner's
/// keys, a lie-down and the keeper's rest posture reach it), the player's own server positions applied to it, and the audit of what it sends. Both
/// <see cref="WayfindScenario"/> and the journey's headless runner host (<c>Journey.HeadlessJourneyRunnerHost</c>) drive one.
/// </summary>
/// <param name="dats">The dats.</param>
/// <param name="log">The log.</param>
/// <param name="category">The log category and the audit's name.</param>
/// <param name="sinkOf">The movement sink for a session (tests), or null for the presence-gated sink over the session's movement actions.</param>
public sealed class HeadlessBody(DatSet dats, ILogSink log, string category, Func<IGameSessionView, IMovementSink>? sinkOf = null) : IDisposable
{
    private readonly DatSet _dats = dats ?? throw new ArgumentNullException(nameof(dats));
    private readonly ILogSink _log = log ?? throw new ArgumentNullException(nameof(log));
    private readonly List<ObjectMoved> _moves = [];
    private readonly List<ObjectMotionChanged> _serverMotions = [];
    private WireMotionItem[] _actions = new WireMotionItem[16];
    private IDisposable? _movesSubscription;
    private IDisposable? _motionSubscription;
    private RouteConfirmer? _confirmer;
    private HostileCreatureFeed? _creatures;
    private bool _focusSet;
    private bool _disposed;

    /// <summary>The collision world, once created.</summary>
    public PhysicsWorld? World { get; private set; }

    /// <summary>The wayfinder's tiles, once created.</summary>
    public NavMap? Map { get; private set; }

    /// <summary>The streamer of collision and tiles round the body and along the route, once created.</summary>
    public HeadlessStreamer? Streamer { get; private set; }

    /// <summary>The body, once created.</summary>
    public OfflineWalker? Walker { get; private set; }

    /// <summary>The audit of what the body sends, once created.</summary>
    public MovementAudit? Audit { get; private set; }

    /// <summary>The player's keys the body's controller is bound to, once created.</summary>
    public MoveIntents? Move { get; private set; }

    /// <summary>The player's weenie (its run rate), once created.</summary>
    public QualitiesWeenie? Weenie { get; private set; }

    /// <summary>
    /// True: the placed body follows the server's MoveTo and TurnTo of the player (ACE's walk-to-use of a corpse, a chest or a vendor, and its attack approach, MoveToObject F74C with no F748
    /// for the player), as the hunt's own body does (<see cref="HuntFollow"/>, M4 1.2 fix3-R1) and as retail's client physics does (<c>CPhysics::SetObjectMovement</c>, 11.6096 0x0050A160).
    /// The journey's headless host sets it (M4 P5b fix FA, P5b-R1); <see cref="WayfindScenario"/> leaves it off. Set before <see cref="Create"/>.
    /// </summary>
    public bool FollowServerMoves { get; init; }

    /// <summary>The server's MoveTos and TurnTos given to the placed body (with <see cref="FollowServerMoves"/>).</summary>
    public int ServerMovesFollowed { get; private set; }

    /// <summary>True once <see cref="Create"/> ran.</summary>
    public bool Created => Walker is not null;

    /// <summary>True while the body stands where the server put the player.</summary>
    public bool Placed { get; private set; }

    /// <summary>The self teleports seen since <see cref="Create"/>.</summary>
    public int Teleports { get; private set; }

    /// <summary>The largest managed heap seen at a corridor update.</summary>
    public long HeapPeak { get; private set; }

    /// <summary>The route's sight and placement checks over the body's world (made at first use).</summary>
    public RouteConfirmer Confirmer => _confirmer ??= new RouteConfirmer(World ?? throw new InvalidOperationException("the headless body is not created"), Walker!.Physics.Setup!, _log);

    /// <summary>Builds the world, the streamer, the audit and the body, binds the body's controller to the player's keys and listens for the player's own moves; the player's qualities must have arrived.</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The host's time.</param>
    public void Create(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Created)
        {
            return;
        }

        var self = session.State.Self;
        Weenie = QualitiesWeenie.FromSkillTable(self.Qualities, SkillTableData.Load(_dats), _log);
        var geometry = new PhysicsGeometryLibrary(_dats);
        World = new PhysicsWorld();
        Map = new NavMap();
        Streamer = new HeadlessStreamer(_dats, geometry, World, Map, _log);
        var sink = sinkOf?.Invoke(session) ?? new PresenceGatedMovementSink(session.Actions.Movement, () => session.State.Self.Travel == SelfTravel.Present);
        Audit = new MovementAudit(sink, session.Events, () => session.Clock.Now, category);
        World.SetTime(now);
        Walker = OfflineWalker.Create(_dats, World, _log, Weenie, geometry, id: self.Guid, sink: Audit);
        Move = session.Actions.Move; // the human's keys (R2): the runner presses them through IActionApi.Move
        _creatures = new HostileCreatureFeed(session.State.Objects, self.Guid, _log); // the runs' creature layer (nav-creature-avoid)
        Move.Bind(Walker.Controller, Walker.Controller.Options.RunByDefault);
        _movesSubscription = session.Events.Subscribe<ObjectMoved>((in ObjectMoved e) =>
        {
            if (e.IsSelf)
            {
                _moves.Add(e);
                if (e.Verdict == PositionVerdict.SelfTeleport)
                {
                    Teleports++;
                }
            }
        });
        if (FollowServerMoves)
        {
            _motionSubscription = session.Events.Subscribe<ObjectMotionChanged>((in ObjectMotionChanged e) =>
            {
                if (e.IsSelf && !e.Autonomous)
                {
                    _serverMotions.Add(e);
                }
            });
        }
    }

    /// <summary>One frame: the streamer's pump, the player's server moves applied to a placed body, the body's tick and the streamer's focus on it.</summary>
    /// <param name="now">The host's time.</param>
    public void Frame(double now)
    {
        if (!Created || _disposed)
        {
            return;
        }

        Streamer!.Pump();
        DrainMoves(now);
        ApplyServerMoves();
        if (Placed)
        {
            Walker!.Controller.Tick(now);
            _focusSet = true;
            Streamer.SetFocus(Walker.Position.CellId);
        }
    }

    /// <summary>
    /// True once the collision and the tiles the streamer's focus on <paramref name="at"/> needs are built (<see cref="HeadlessStreamer.IsFocusSettled"/>: the
    /// landblock and the eight round it outdoors, a dungeon's own landblock alone); focuses the streamer there first.
    /// </summary>
    /// <param name="at">Where the server has the player.</param>
    public bool Settled(in CellFrame at)
    {
        var streamer = Streamer ?? throw new InvalidOperationException("the headless body is not created");
        if (!_focusSet || streamer.Collision.FocusCellId != at.CellId)
        {
            _focusSet = true;
            streamer.SetFocus(at.CellId);
        }

        return streamer.IsFocusSettled(at.CellId);
    }

    /// <summary>True when the collision of <paramref name="at"/>'s landblock is attached.</summary>
    /// <param name="at">A position.</param>
    public bool IsAttached(in CellFrame at) => World?.IsAttached(LandblockCoord.FromId(at.CellId)) == true;

    /// <summary>Puts the body where the server has the player; false when the physics finds no valid position there.</summary>
    /// <param name="at">The server's position.</param>
    /// <param name="clock">The session clock.</param>
    public bool PlaceAt(in CellFrame at, double clock)
    {
        World!.SetTime(clock);
        if (!Walker!.Place(at))
        {
            return false;
        }

        Placed = true;
        _moves.Clear();
        return true;
    }

    /// <summary>Marks the body as no longer where the server has the player (a teleport, a recall or a place moved the player): the next placement re-places it.</summary>
    public void Unplace() => Placed = false;

    /// <summary>A runner over the body through the player's keys, avoiding the hostile creatures the session sees (nav-creature-avoid).</summary>
    /// <param name="source">The path source.</param>
    /// <param name="name">The runner's name.</param>
    /// <param name="confirmBudgetMilliseconds">The confirmation budget per step.</param>
    public NavRunner NewRunner(INavPathSource source, string name, double confirmBudgetMilliseconds) =>
        new(Move!, new ControllerNavBody(Walker!.Controller), source, Confirmer, Audit, new NavRunnerOptions { Name = name, ConfirmBudgetMilliseconds = confirmBudgetMilliseconds }, _log, _creatures);

    /// <summary>Feeds the streamer the landblocks ahead on the runner's route; true when the corridor changed.</summary>
    /// <param name="runner">The runner.</param>
    public bool FollowRoute(NavRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        var changed = Streamer!.FollowRoute(runner, Walker!.Position);
        if (changed)
        {
            _log.Debug(category, "the corridor changed", new LogField("ahead", string.Join(' ', Streamer.Collision.Corridor.Select(c => c.ToString()))), new LogField("streamer", Streamer.StatsLine()));
        }

        HeapPeak = Math.Max(HeapPeak, GC.GetTotalMemory(false));
        return changed;
    }

    /// <summary>The terrain's height at landblock-local (x, y) of <paramref name="cellId"/>'s landblock (0 when the dats have no terrain there).</summary>
    /// <param name="dats">The dats.</param>
    /// <param name="cellId">A cell of the landblock.</param>
    /// <param name="x">Local x.</param>
    /// <param name="y">Local y.</param>
    public static float TerrainZ(DatSet dats, uint cellId, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(dats);
        return dats.GetLandblockTerrain(LandblockCoord.FromId(cellId)) is { } terrain ? new TerrainSampler(terrain, dats.TerrainRegion.LandHeightTable).GetHeight(x, y) : 0f;
    }

    /// <summary>Releases the keys' binding, the subscription, the audit and the streamer.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _confirmer?.Dispose();
        if (Move is not null && Walker is not null)
        {
            Move.Unbind(Walker.Controller);
        }

        _movesSubscription?.Dispose();
        _motionSubscription?.Dispose();
        Audit?.Dispose();
        Streamer?.Dispose();
    }

    /// <summary>The server's MoveTos and TurnTos since the last frame, given to the placed body (dropped while it is not placed: the server's position places it).</summary>
    private void ApplyServerMoves()
    {
        var count = _serverMotions.Count;
        if (count == 0)
        {
            return;
        }

        var types = 0;
        foreach (var e in _serverMotions)
        {
            types += (byte)e.Motion.MovementType is >= 6 and <= 9 ? 1 : 0;
        }

        HuntFollow.ApplyServerMoveTos(_serverMotions, Placed ? Walker : null, ref _actions, _log, category, "the server moves the player: the journey's body follows");
        ServerMovesFollowed += Placed ? types : 0;
    }

    private void DrainMoves(double now)
    {
        if (_moves.Count == 0)
        {
            return;
        }

        if (!Placed)
        {
            _moves.Clear(); // the body is placed from the server's position once the collision is there
            return;
        }

        var batch = _moves.ToArray();
        _moves.Clear();
        foreach (var move in batch)
        {
            if (!World!.IsAttached(LandblockCoord.FromId(move.Position.CellId)))
            {
                _log.Debug(category, "a position of the player outside the attached collision is not applied", new LogField("cell", $"0x{move.Position.CellId:X8}"));
                continue;
            }

            Walker!.Controller.Apply(move, now);
        }
    }
}
