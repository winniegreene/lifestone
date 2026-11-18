using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Data.Animation;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The walk of the death scenario's bind (M3 3.2 lane da): a short straight leg over the landscape from where the player stands to a point beside an object (the lifestone), so that the player is within the object's
/// use radius when ACE checks it. ACE moves a player who is out of range itself (<c>Player_Use.cs</c> <c>CreateMoveToChain</c>) only while the client follows the MoveToObject it sends; a client that does not follow gets no
/// UseDone and no UseMessage (the first live run: 5.4 m away, nothing came in 15 s). <see cref="DeathScenario"/> asks for this walk before the Use. Game thread only.
/// </summary>
public interface IApproachWalk : IDisposable
{
    /// <summary>The reason the walk failed, or null.</summary>
    string? Failure { get; }

    /// <summary>Makes the body and starts following the server's positions of the player; call before the teleport that puts the player where the leg starts, once (a second call does nothing).</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The frame time.</param>
    void Prepare(IGameSessionView session, double now);

    /// <summary>Starts the leg from where the player stands toward <paramref name="target"/>, stopping when it is within <paramref name="stopWithinMetres"/> (centre to centre, horizontally).</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The frame time.</param>
    /// <param name="target">The object's position.</param>
    /// <param name="stopWithinMetres">The stop radius.</param>
    /// <param name="description">What the leg is, for the log.</param>
    /// <returns>False with <see cref="Failure"/> set when no leg can start. A walker the teleport's position event did not place is placed from the session's own player position first.</returns>
    bool Begin(IGameSessionView session, double now, in CellFrame target, float stopWithinMetres, out string description);

    /// <summary>One frame: applies the server's positions of the player, ticks the body and the leg. Call every frame while the leg is walked.</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The frame time.</param>
    FightWalkStatus Tick(IGameSessionView session, double now);

    /// <summary>Drops the leg and releases every key.</summary>
    void Abandon();
}

/// <summary>
/// <see cref="IApproachWalk"/> for the headless host: an <see cref="OfflineWalker"/> in the collision of the ground's home landblock and the eight around it (M5 3 d1, 2-R10: <see cref="HuntingGround.Home"/>, the lifestone's cell and the point beside it;
/// Holtburg for the den), placed by the server's teleport, with the
/// <see cref="RouteDriver"/> of a one-goal route. The game's equivalent (its own player as the walker, as <c>GameFightWalk</c> is for the den) is dd's; it implements the same interface.
/// </summary>
public sealed class HoltburgApproach : IApproachWalk
{
    private const string LogCategory = "death";

    private readonly DatSet _dats;
    private readonly PhysicsGeometryLibrary _geometry;
    private readonly PhysicsWorld _world;
    private readonly ILogSink _log;
    private readonly List<ObjectMoved> _moves = [];
    private IDisposable? _movesSubscription;
    private OfflineWalker? _walker;
    private MoveIntents? _move;
    private MoveLease? _lease;
    private PresenceGatedMovementSink? _gate;
    private RouteDriver? _driver;
    private bool _placed;
    private bool _disposed;

    private readonly GroundHome _home;

    private HoltburgApproach(DatSet dats, PhysicsGeometryLibrary geometry, PhysicsWorld world, ILogSink log, GroundHome home)
    {
        (_dats, _geometry, _world, _log, _home) = (dats, geometry, world, log, home);
    }

    /// <summary>The home the walk is built around (the ground's: its town, its lifestone's cell, the point beside it).</summary>
    public GroundHome Home => _home;

    /// <summary>The point beside the home lifestone (<see cref="GroundHome.LifestonePoint"/> in <see cref="GroundHome.LifestoneCell"/>): the walk's goal when the lifestone object is not in the table.</summary>
    public CellFrame HomePoint => new(_home.LifestoneCell, new AcFrame(_home.LifestonePoint, System.Numerics.Quaternion.Identity));

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <summary>The walker, or null before <see cref="Prepare"/>.</summary>
    public OfflineWalker? Walker => _walker;

    /// <summary>Builds the collision of the default ground's home landblock and its neighbours from the dats (a few seconds): <see cref="Create(DatSet, ILogSink, HuntingGround)"/> with the fight's default ground.</summary>
    /// <param name="dats">The dats.</param>
    /// <param name="log">Where the walk logs.</param>
    public static HoltburgApproach Create(DatSet dats, ILogSink log) => Create(dats, log, new FightOptions().GroundProfile);

    /// <summary>Builds the collision of the ground's home landblock (<see cref="HuntingGround.Home"/>, 2-R10) and its neighbours from the dats (a few seconds).</summary>
    /// <param name="dats">The dats.</param>
    /// <param name="log">Where the walk logs.</param>
    /// <param name="ground">The ground whose home lifestone the bind walks to.</param>
    public static HoltburgApproach Create(DatSet dats, ILogSink log, HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(ground);
        var geometry = new PhysicsGeometryLibrary(dats);
        return new HoltburgApproach(dats, geometry, HoltburgLapsScenario.AttachAround(dats, geometry, LandblockCoord.FromId(ground.Home.LifestoneCell)), log, ground.Home);
    }

    /// <inheritdoc/>
    public void Prepare(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_walker is not null)
        {
            return;
        }

        var self = session.State.Self;
        if (!self.Qualities.IsKnown)
        {
            Failure = "preparing the approach walk: the player's qualities have not arrived (no PlayerDescription), so its run and jump skills are unknown";
            return;
        }

        var weenie = QualitiesWeenie.FromSkillTable(self.Qualities, SkillTableData.Load(_dats), _log);
        _gate = new PresenceGatedMovementSink(session.Actions.Movement, () => session.State.Self.Travel == SelfTravel.Present);
        _world.SetTime(now);
        _walker = OfflineWalker.Create(_dats, _world, _log, weenie, _geometry, id: self.Guid, sink: _gate);
        _move = session.Actions.Move; // scripted movement goes through IActionApi.Move (the human's keys, R2)
        _lease = _move.BindOver(_walker.Controller, _walker.Controller.Options.RunByDefault); // M6 14 motes: the play host's controller is bound again at the end
        _movesSubscription = session.Events.Subscribe<ObjectMoved>((in ObjectMoved e) =>
        {
            if (e.IsSelf)
            {
                _moves.Add(e);
            }
        });
        _log.Info(LogCategory, "the approach walker is made; it is placed by the server's teleport");
    }

    /// <inheritdoc/>
    public bool Begin(IGameSessionView session, double now, in CellFrame target, float stopWithinMetres, out string description)
    {
        description = string.Empty;
        if (_walker is null)
        {
            Failure = "the approach walker was not prepared (Prepare was not called before the teleport)";
            return false;
        }

        DrainMoves(now);
        if (!_placed)
        {
            // The teleport's position did not place it (no Tick saw it, or it landed before Prepare): place it from the session's own settled position.
            if (session.State.Self.Position is not { } here || !_world.IsAttached(LandblockCoord.FromId(here.CellId)))
            {
                Failure = $"the approach walker is not placed and the player's position is not in {_home.Town}'s landblock (the ground's home, cell 0x{_home.LifestoneCell:X8})";
                return false;
            }

            if (!_walker.Place(here))
            {
                Failure = $"the approach walker is not placed and has no valid position at the player's cell 0x{here.CellId:X8}";
                return false;
            }

            _placed = true;
            _log.Info(LogCategory, "the approach walker was placed from the session's own player position (the teleport's position event had not placed it)", new LogField("cell", $"0x{here.CellId:X8}"));
        }

        var from = _walker.Position;
        var route = new ScriptedRoute("lifestone-approach", [new RouteGoal(target, stopWithinMetres, Stop: true, DwellSeconds: 0)]);
        _driver?.Abandon();
        _driver = new RouteDriver(_walker.Controller, route, _move!);
        _driver.KeysPressedAgain += line => _log.Warn(LogCategory, line); // M5 7 D153 (D141): a held key let go by something else was pressed again
        description = $"from cell 0x{from.CellId:X8} toward cell 0x{target.CellId:X8}, stopping within {stopWithinMetres:0.0##} m";
        return true;
    }

    /// <inheritdoc/>
    public FightWalkStatus Tick(IGameSessionView session, double now)
    {
        if (_walker is null)
        {
            return FightWalkStatus.Idle;
        }

        DrainMoves(now);
        if (_placed)
        {
            _walker.Controller.Tick(now);
        }

        if (_driver is not { } driver)
        {
            return FightWalkStatus.Idle;
        }

        switch (driver.Tick(now))
        {
            case RouteStatus.Done:
                _driver = null;
                return FightWalkStatus.Arrived;
            case RouteStatus.Stuck:
            case RouteStatus.Failed:
                Failure = driver.FailureReason ?? "the approach leg failed without saying why";
                driver.Abandon();
                _driver = null;
                return FightWalkStatus.Failed;
        }

        return FightWalkStatus.Walking;
    }

    /// <inheritdoc/>
    public void Abandon() => _driver?.Abandon();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _driver?.Abandon();
        if (_move is not null && _walker is not null)
        {
            _move.Release(_lease!); // M6 14 motes
        }

        _movesSubscription?.Dispose();
    }

    private void DrainMoves(double now)
    {
        if (_moves.Count == 0)
        {
            return;
        }

        foreach (var move in _moves)
        {
            if (!_world.IsAttached(LandblockCoord.FromId(move.Position.CellId)))
            {
                continue; // an arrival outside the home's landblocks (the login's, a teleport to the ground)
            }

            if (!_placed)
            {
                if (move.Verdict != PositionVerdict.SelfTeleport)
                {
                    continue;
                }

                if (!_walker!.Place(move.Position))
                {
                    _log.Error(LogCategory, "no valid position for the approach walker where the server put the player", new LogField("cell", $"0x{move.Position.CellId:X8}"));
                    continue;
                }

                _placed = true;
            }

            _walker!.Controller.Apply(move, now);
        }

        _moves.Clear();
    }
}
