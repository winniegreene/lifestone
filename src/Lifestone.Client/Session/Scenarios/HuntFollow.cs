using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The client's own body in the hunt (M4 1.2 fix3-R1): follows the server's view of the player and its MoveTos, and reports the positions it walks. Game thread only.
/// </summary>
public interface IHuntFollow : IDisposable
{
    /// <summary>The reason the follower cannot work, or null.</summary>
    string? Failure { get; }

    /// <summary>Where the client's own body stands, or null while it is not placed.</summary>
    CellFrame? Position { get; }

    /// <summary>Makes the body once the player's qualities are known (a call before that, or after, does nothing).</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The frame time.</param>
    void Prepare(IGameSessionView session, double now);

    /// <summary>One frame: applies the server's positions and MoveTos of the player and ticks the body, which sends its positions.</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The frame time.</param>
    void Tick(IGameSessionView session, double now);
}

/// <summary>
/// <see cref="IHuntFollow"/> for the headless host: an <see cref="OfflineWalker"/> (the one <see cref="RoutePlay"/> and <see cref="DenWalk"/> make) in a physics world that gets the collision of each landblock
/// the server puts the player in and the eight around it, built from the dats on first need. It is placed by the server's teleports and given the player's positions and the server's MoveTo and TurnTo as
/// <see cref="DenWalk"/> gives them, so ACE's approach to a target (its MoveToObject F74C, with no F748 of its own for the player) moves the client's own position and the client's autonomous positions tell ACE where
/// it is. Retail does this in the client's physics: <c>CPhysics::SetObjectMovement</c> (11.4186 0x00509690, 11.6096 0x0050A160) runs the server's MoveTo in the physics object's update (see
/// <see cref="PhysicsPlayerBody.ApplyServerMovement"/>); the cadence of the position reports is the controller's. The game does not use this class: its player controller already owns the walker
/// (<see cref="RoutePlayHost"/>).
/// </summary>
public sealed class HuntFollow : IHuntFollow
{
    private const string LogCategory = "hunt";

    private readonly DatSet _dats;
    private readonly ILogSink _log;
    private readonly Func<IGameSessionView, IMovementSink>? _sinkOf;
    private readonly PhysicsGeometryLibrary _geometry;
    private readonly LandblockCollisionBuilder _builder;
    private readonly PhysicsWorld _world = new();
    private readonly List<ObjectMoved> _moves = [];
    private readonly List<ObjectMotionChanged> _serverMotions = [];
    private WireMotionItem[] _actions = new WireMotionItem[16];
    private IDisposable? _movesSubscription;
    private IDisposable? _motionSubscription;
    private OfflineWalker? _walker;
    private SessionTargetWorld? _targets;
    private bool _placed;
    private bool _disposed;

    /// <summary>A follower.</summary>
    /// <param name="dats">The dats.</param>
    /// <param name="log">Where it logs.</param>
    /// <param name="sinkOf">Where the body sends its positions: null for the session's movement channel behind the presence gate; a test gives its own sink (the offline session sends no movement).</param>
    public HuntFollow(DatSet dats, ILogSink log, Func<IGameSessionView, IMovementSink>? sinkOf = null)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(log);
        (_dats, _log, _sinkOf) = (dats, log, sinkOf);
        _geometry = new PhysicsGeometryLibrary(dats);
        _builder = new LandblockCollisionBuilder(dats, _geometry);
    }

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <inheritdoc/>
    public CellFrame? Position => _walker is not null && _placed ? _walker.Position : null;

    /// <summary>The walker, or null before <see cref="Prepare"/> made it.</summary>
    public OfflineWalker? Walker => _walker;

    /// <inheritdoc/>
    public void Prepare(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        var self = session.State.Self;
        if (_walker is not null || _disposed || !self.Qualities.IsKnown)
        {
            return;
        }

        var weenie = QualitiesWeenie.FromSkillTable(self.Qualities, SkillTableData.Load(_dats), _log);
        var sink = _sinkOf?.Invoke(session) ?? new PresenceGatedMovementSink(session.Actions.Movement, () => session.State.Self.Travel == SelfTravel.Present);
        _world.SetTime(now);
        _walker = OfflineWalker.Create(_dats, _world, _log, weenie, _geometry, id: self.Guid, sink: sink);
        _targets = new SessionTargetWorld(session.State, _dats, _geometry, () => _world.CurrentTime, _walker.Physics);
        _walker.Physics.TargetWorld = _targets;
        _movesSubscription = session.Events.Subscribe<ObjectMoved>((in ObjectMoved e) =>
        {
            if (e.IsSelf)
            {
                _moves.Add(e);
            }
        });
        _motionSubscription = session.Events.Subscribe<ObjectMotionChanged>((in ObjectMotionChanged e) =>
        {
            if (e.IsSelf && !e.Autonomous)
            {
                _serverMotions.Add(e);
            }
        });
        _log.Info(LogCategory, "the hunt's own body is made; it is placed by the server's teleport and follows the server's MoveTos", new LogField("run_rate", weenie.TryGetRunRate(out var rate) ? rate : null));
    }

    /// <inheritdoc/>
    public void Tick(IGameSessionView session, double now)
    {
        if (_walker is null || _disposed)
        {
            return;
        }

        DrainMoves(now);
        ApplyServerMotions();
        if (_placed)
        {
            _walker.Controller.Tick(now);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _movesSubscription?.Dispose();
        _motionSubscription?.Dispose();
    }

    /// <summary>Attaches the collision of the landblock of <paramref name="cellId"/> and the eight around it that the world does not hold yet; false when the landblock itself fails to build.</summary>
    private bool EnsureAttached(uint cellId)
    {
        var centre = LandblockCoord.FromId(cellId);
        if (_world.IsAttached(centre))
        {
            return true;
        }

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (centre.Offset(dx, dy) is { } coord && !_world.IsAttached(coord))
                {
                    try
                    {
                        _world.Attach(_builder.Build(coord));
                    }
                    catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException)
                    {
                        _log.Error(LogCategory, "TODO(m4-hunt-follow-collision): the collision of a landblock around the player did not build; the hunt's own body cannot follow there", new LogField("landblock", coord.ToString()), new LogField("why", e.Message));
                        Failure ??= "the collision of landblock " + coord + " did not build: " + e.Message;
                        if (coord == centre)
                        {
                            return false;
                        }
                    }
                }
            }
        }

        return _world.IsAttached(centre);
    }

    private void DrainMoves(double now)
    {
        if (_moves.Count == 0)
        {
            return;
        }

        // The controller's own report can come back as the server's echo while it applies a move: the batch is taken first.
        var batch = _moves.ToArray();
        _moves.Clear();
        foreach (var move in batch)
        {
            if (!EnsureAttached(move.Position.CellId))
            {
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
                    _log.Error(LogCategory, "no valid position for the hunt's own body where the server put the player", new LogField("cell", $"0x{move.Position.CellId:X8}"));
                    continue;
                }

                _placed = true;
            }

            _walker!.Controller.Apply(move, now);
        }

    }

    /// <summary>Gives the body the server's MoveTo and TurnTo of the player (<c>DispatchSmartBoxEvent</c> makes the controller lose control to the server first, 11.4186 0x005595D0, 11.6096 0x0055A210); the rest of the server's motion is left alone.</summary>
    private void ApplyServerMotions()
    {
        if (_serverMotions.Count == 0)
        {
            return;
        }

        foreach (var e in _serverMotions)
        {
            var type = (byte)e.Motion.MovementType;
            if (type is < 6 or > 9 || !_placed || _walker!.Body is not IPlayerMovementBody body)
            {
                continue;
            }

            var count = AvatarWire.CopyActions(e.Motion, _actions);
            if (count > _actions.Length)
            {
                _actions = new WireMotionItem[count];
                AvatarWire.CopyActions(e.Motion, _actions);
            }

            var result = body.ApplyServerMovement(AvatarWire.ToMovement(e.Motion), _actions.AsSpan(0, count), e.Autonomous, out var failure);
            _log.Info(
                LogCategory,
                "the server moves the player: the hunt's own body follows",
                new LogField("type", type),
                new LogField("target", e.Motion.MoveTo?.TargetGuid is { } target ? $"0x{target:X8}" : "none"),
                new LogField("result", result.ToString()),
                new LogField("moving_to", _walker.Physics.IsMovingTo),
                new LogField("failure", result == MovementUnpackResult.Applied ? "none" : failure.Message));
        }

        _serverMotions.Clear();
    }
}
