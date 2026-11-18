using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Wayfinding;
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
/// <see cref="IHuntFollow"/> for the headless host: an <see cref="OfflineWalker"/> (the one <see cref="RoutePlay"/> and <see cref="GroundWalk"/> make) in a physics world that gets the collision of each landblock
/// the server puts the player in and the eight around it, built from the dats on first need. It is placed by the server's teleports and given the player's positions and the server's MoveTo and TurnTo as
/// <see cref="GroundWalk"/> gives them, so ACE's approach to a target (its MoveToObject F74C, with no F748 of its own for the player) moves the client's own position and the client's autonomous positions tell ACE where
/// it is. Retail does this in the client's physics: <c>CPhysics::SetObjectMovement</c> (11.4186 0x00509690, 11.6096 0x0050A160) runs the server's MoveTo in the physics object's update (see
/// <see cref="PhysicsPlayerBody.ApplyServerMovement"/>); the cadence of the position reports is the controller's. The game does not use this class: its player controller already owns the walker
/// (<see cref="RoutePlayHost"/>).
/// </summary>
public sealed class HuntFollow : IHuntFollow
{
    private const string LogCategory = "hunt";

    /// <summary>
    /// How long a move into a landblock whose collision is not attached yet is held back (the streamer builds it on its workers while the net pump keeps
    /// running), in seconds; past it the follower builds it at once (<c>HeadlessStreamer.EnsureNow</c>) and logs <c>TODO(hunt-crossing-stall)</c> with the stall.
    /// Not retail: Lifestone's own.
    /// </summary>
    public const double CrossingWaitSeconds = 3.0;

    private readonly DatSet _dats;
    private readonly ILogSink _log;
    private readonly Func<IGameSessionView, IMovementSink>? _sinkOf;
    private readonly PhysicsGeometryLibrary _geometry;
    private readonly HeadlessStreamer _streamer;
    private readonly HashSet<LandblockCoord> _failedBlocks = [];
    private readonly PhysicsWorld _world = new();
    private LandblockCoord? _focusBlock;
    private double _crossingSince = double.NaN;
    private readonly List<ObjectMoved> _moves = [];
    private readonly List<ObjectMotionChanged> _serverMotions = [];
    private WireMotionItem[] _actions = new WireMotionItem[16];
    private IDisposable? _movesSubscription;
    private IDisposable? _motionSubscription;
    private OfflineWalker? _walker;
    private MoveIntents? _move;
    private MoveLease? _lease;
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
        _streamer = new HeadlessStreamer(dats, _geometry, _world, null, log);
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
        _move = session.Actions.Move; // scripted movement goes through IActionApi.Move (the human's keys, R2)
        _lease = _move.BindOver(_walker.Controller, _walker.Controller.Options.RunByDefault); // M6 14 motes: the play host's controller is bound again at the end
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

        _streamer.Pump(); // P5a-R8: the landblock crossing builds on the workers while the net pump runs; only the start waits for them
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
        if (_move is not null && _walker is not null)
        {
            _move.Release(_lease!); // M6 14 motes
        }

        _movesSubscription?.Dispose();
        _motionSubscription?.Dispose();
        _streamer.Dispose();
    }

    /// <summary>
    /// Makes sure the collision of the landblock of <paramref name="cellId"/> is attached, through the headless streamer (P5a S6: the collision streamer over
    /// two workers; the player's landblock is the focus, so the ones left behind are released). The first landblock (the placement) is built at once; a
    /// later crossing is not waited for (P5a-R8: the net pump keeps running): <see cref="EnsureResult.Pending"/> holds the move back until the workers have
    /// it, for at most <see cref="CrossingWaitSeconds"/>. <see cref="EnsureResult.Failed"/> when the landblock itself fails to build.
    /// </summary>
    private EnsureResult EnsureAttached(uint cellId, double now)
    {
        var centre = LandblockCoord.FromId(cellId);
        if (_focusBlock == centre && _world.IsAttached(centre))
        {
            return EnsureResult.Ready;
        }

        if (!_placed || _focusBlock is null)
        {
            _focusBlock = centre;
            _streamer.EnsureNow(cellId);
        }
        else
        {
            _streamer.SetFocus(cellId);
            _streamer.Pump();
            if (!_world.IsAttached(centre) && !_streamer.IsSettled(cellId))
            {
                if (double.IsNaN(_crossingSince))
                {
                    _crossingSince = now;
                }

                if (now - _crossingSince < CrossingWaitSeconds)
                {
                    return EnsureResult.Pending;
                }

                var waited = now - _crossingSince;
                _streamer.EnsureNow(cellId);
                _log.Warn(
                    LogCategory,
                    "TODO(hunt-crossing-stall): the landblock crossing did not attach on the workers in time; built at once, the net pump stalled",
                    new LogField("landblock", centre.ToString()),
                    new LogField("waited_s", waited),
                    new LogField("streamer", _streamer.StatsLine())); // (no wall clock in Session sources: the pump and build maxima are the stall's measure)
            }

            _focusBlock = centre;
        }

        _crossingSince = double.NaN;
        if (!_world.IsAttached(centre))
        {
            if (_failedBlocks.Add(centre))
            {
                _log.Error(LogCategory, "TODO(m4-hunt-follow-collision): the collision of the player's landblock did not build; the hunt's own body cannot follow there", new LogField("landblock", centre.ToString()));
            }

            Failure ??= "the collision of landblock " + centre + " did not build";
            return EnsureResult.Failed;
        }

        return EnsureResult.Ready;
    }

    private enum EnsureResult
    {
        Ready,
        Pending,
        Failed,
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
        for (var i = 0; i < batch.Length; i++)
        {
            var move = batch[i];
            var ensured = EnsureAttached(move.Position.CellId, now);
            if (ensured == EnsureResult.Pending)
            {
                _moves.AddRange(batch.AsSpan(i)); // held back, in order, until the landblock is attached
                break;
            }

            if (ensured == EnsureResult.Failed)
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
    private void ApplyServerMotions() => ApplyServerMoveTos(_serverMotions, _placed ? _walker : null, ref _actions, _log, LogCategory, "the server moves the player: the hunt's own body follows");

    /// <summary>
    /// Gives a placed body the server's MoveTo and TurnTo of the player (movement types 6-9: <c>DispatchSmartBoxEvent</c> makes the controller lose control to the server first, 11.4186 0x005595D0,
    /// 11.6096 0x0055A210, and <c>CPhysics::SetObjectMovement</c> runs it in the physics object's update) and clears <paramref name="motions"/>; the rest of the server's motion is left alone, and nothing is applied
    /// while <paramref name="walker"/> is null (not placed). Shared by <see cref="HuntFollow"/> and <see cref="HeadlessBody"/> (M4 P5b fix FA, P5b-R1: the journey's headless body follows ACE's walk-to-use).
    /// </summary>
    /// <param name="motions">The server's (not autonomous) motions of the player since the last call.</param>
    /// <param name="walker">The placed body, or null.</param>
    /// <param name="actions">A scratch buffer for the motion's actions (grown when needed).</param>
    /// <param name="log">The log.</param>
    /// <param name="category">The log category.</param>
    /// <param name="message">The line logged for each MoveTo or TurnTo applied.</param>
    internal static void ApplyServerMoveTos(List<ObjectMotionChanged> motions, OfflineWalker? walker, ref WireMotionItem[] actions, ILogSink log, string category, string message)
    {
        if (motions.Count == 0)
        {
            return;
        }

        foreach (var e in motions)
        {
            var type = (byte)e.Motion.MovementType;
            if (type is < 6 or > 9 || walker?.Body is not IPlayerMovementBody body)
            {
                continue;
            }

            var count = AvatarWire.CopyActions(e.Motion, actions);
            if (count > actions.Length)
            {
                actions = new WireMotionItem[count];
                AvatarWire.CopyActions(e.Motion, actions);
            }

            var result = body.ApplyServerMovement(AvatarWire.ToMovement(e.Motion), actions.AsSpan(0, count), e.Autonomous, out var failure);
            log.Info(
                category,
                message,
                new LogField("type", type),
                new LogField("target", e.Motion.MoveTo?.TargetGuid is { } target ? $"0x{target:X8}" : "none"),
                new LogField("result", result.ToString()),
                new LogField("moving_to", walker.Physics.IsMovingTo),
                new LogField("failure", result == MovementUnpackResult.Applied ? "none" : failure.Message));
        }

        motions.Clear();
    }
}
