using System.Diagnostics;
using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Scenery;
using Lifestone.Net.Types;

namespace Lifestone.Loopback.Tests.Net;

/// <summary>
/// When a walking client sends AutonomousPosition (m2-plan N12; docs/protocol.md section 5.4). Retail's rule, re-expressed from
/// <c>CommandInterpreter::ShouldSendPositionEvent</c> (11.6096 0x006B5520) and <c>SendPositionEvent</c> (0x006B56B0): the position goes out
/// at once when the cell differs from the last position sent, or in the same cell when the contact plane (here: the terrain triangle) differs;
/// otherwise when strictly more than 1.0 s has passed since the last message of either kind (MoveToState or AutonomousPosition) and the frame
/// differs. Retail sends only with Contact and OnWalkable, which a walker on the terrain always has.
/// </summary>
/// <remarks>
/// A stress cadence replaces the 1.0 s rule by a shorter fixed interval (the lossy soak sends every 200 ms, like the virtual soak, so a two minute run
/// puts several hundred datagrams on the lossy link); the cell and triangle rules stay.
/// </remarks>
internal sealed class PositionCadence
{
    /// <summary>Retail's interval: strictly more than this many seconds after the last message.</summary>
    public const double RetailIntervalSeconds = 1.0;

    private readonly double? _stressInterval;
    private bool _haveSent;
    private uint _cell;
    private int _polygon;
    private AcPosition _position;
    private double _lastMessage;

    /// <summary>The retail cadence (<paramref name="stressIntervalSeconds"/> null) or a fixed shorter interval.</summary>
    public PositionCadence(double? stressIntervalSeconds = null)
    {
        if (stressIntervalSeconds is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stressIntervalSeconds));
        }

        _stressInterval = stressIntervalSeconds;
    }

    /// <summary>True when the walker at <paramref name="position"/> (on terrain triangle <paramref name="polygon"/>) should send an AutonomousPosition at <paramref name="now"/> seconds.</summary>
    public bool ShouldSend(double now, in AcPosition position, int polygon)
    {
        if (!_haveSent || position.CellId != _cell || polygon != _polygon)
        {
            return true;
        }

        return _stressInterval is { } interval
            ? now - _lastMessage >= interval
            : now - _lastMessage > RetailIntervalSeconds && position != _position;
    }

    /// <summary>Records a message of either kind (a MoveToState restarts the timer too).</summary>
    public void Sent(double now, in AcPosition position, int polygon)
    {
        _haveSent = true;
        _cell = position.CellId;
        _polygon = polygon;
        _position = position;
        _lastMessage = now;
    }
}

/// <summary>
/// A test-only kinematic walker over one landblock's terrain (<see cref="TerrainSampler"/>, the retail height rule): it moves along a closed route at a
/// fixed ground speed, stands on the terrain's exact height, faces its direction of travel and reports the outdoor cell it is in. It collides with nothing
/// (ACE forces the requested position without checking collisions, docs/protocol.md section 6) and is not product code: the client's own mover is the
/// physics track's <c>PlayerMovementController</c> (m2-plan section 5).
/// </summary>
internal sealed class KinematicWalker
{
    private readonly TerrainSampler _terrain;
    private readonly uint _landblockId;
    private readonly Vector2[] _route;
    private double _x;
    private double _y;
    private int _target;

    /// <summary>A walker starting at <paramref name="start"/> (landblock-local metres) and heading round <paramref name="route"/> again and again.</summary>
    public KinematicWalker(TerrainSampler terrain, LandblockCoord block, Vector2 start, IReadOnlyList<Vector2> route, double metresPerSecond = MotionInterpreter.WalkAnimSpeed)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(route);
        if (route.Count == 0 || route.Any(p => !Inside(p)) || !Inside(start))
        {
            throw new ArgumentException("the start and every route point must lie inside the landblock [0, 192)");
        }

        if (metresPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(metresPerSecond));
        }

        _terrain = terrain;
        _landblockId = (uint)((block.X << 24) | (block.Y << 16));
        _route = [.. route];
        _x = start.X;
        _y = start.Y;
        Speed = metresPerSecond;
    }

    /// <summary>The ground speed in metres a second.</summary>
    public double Speed { get; }

    /// <summary>The metres walked so far.</summary>
    public double Metres { get; private set; }

    /// <summary>The outdoor cell the walker is in (<c>0xAAAA0001</c> to <c>0xAAAA0040</c>: cell x * 8 + cell y + 1).</summary>
    public uint CellId
    {
        get
        {
            var (cx, cy) = (TerrainSampler.CellIndexOf((float)_x), TerrainSampler.CellIndexOf((float)_y));
            return _landblockId | (uint)((cx * TerrainConstants.CellsPerSide) + cy + 1);
        }
    }

    /// <summary>The terrain triangle under the walker (retail's contact plane), 0 to 127.</summary>
    public int Polygon => _terrain.FindTerrainPolygon((float)_x, (float)_y);

    /// <summary>The walker's frame: its cell, the terrain height at (x, y) as z, and the rotation about up that faces its direction of travel (AC faces local +Y at the identity).</summary>
    public AcPosition Position
    {
        get
        {
            var (dx, dy) = Direction();
            var half = Math.Atan2(-dx, dy) / 2;
            var origin = new Vector3((float)_x, (float)_y, _terrain.GetHeight((float)_x, (float)_y));
            return new AcPosition(CellId, origin, new Quaternion(0, 0, (float)Math.Sin(half), (float)Math.Cos(half)));
        }
    }

    /// <summary>Walks for <paramref name="seconds"/>, turning at each route point (a longer step passes several).</summary>
    public void Advance(double seconds)
    {
        var distance = Speed * seconds;
        while (distance > 1e-9)
        {
            var goal = _route[_target];
            var (dx, dy) = (goal.X - _x, goal.Y - _y);
            var remaining = Math.Sqrt((dx * dx) + (dy * dy));
            if (remaining <= distance)
            {
                (_x, _y) = (goal.X, goal.Y);
                Metres += remaining;
                distance -= remaining;
                _target = (_target + 1) % _route.Length;
                continue;
            }

            _x += dx / remaining * distance;
            _y += dy / remaining * distance;
            Metres += distance;
            distance = 0;
        }
    }

    private static bool Inside(Vector2 p) => p.X is >= 0 and < TerrainConstants.LandblockSize && p.Y is >= 0 and < TerrainConstants.LandblockSize;

    private (double X, double Y) Direction()
    {
        for (var i = 0; i < _route.Length; i++)
        {
            var goal = _route[(_target + i) % _route.Length];
            var (dx, dy) = (goal.X - _x, goal.Y - _y);
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length > 1e-9)
            {
                return (dx / length, dy / length);
            }
        }

        return (0, 1);
    }
}

/// <summary>
/// Drives a <see cref="KinematicWalker"/> through a live session on the game thread: a MoveToState (WalkForward) when it starts, an AutonomousPosition
/// whenever <see cref="PositionCadence"/> says so, a MoveToState (Ready) when it stops, and every inbound message pumped in between. With a walk
/// and a stand span it alternates (the soak's motion changes). Every position it sends is kept for the tests to compare with what ACE reports.
/// </summary>
internal sealed class WalkDriver
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(15);

    private readonly LiveNetClient _client;
    private readonly KinematicWalker _walker;
    private readonly PositionCadence _cadence;
    private readonly TimeSpan? _walkSpan;
    private readonly TimeSpan _standSpan;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _moving;
    private double _nextToggle;

    /// <summary>A driver over <paramref name="client"/> (in the world, unhidden). <paramref name="walkSpan"/> null walks without stopping.</summary>
    public WalkDriver(LiveNetClient client, KinematicWalker walker, PositionCadence cadence, TimeSpan? walkSpan = null, TimeSpan standSpan = default)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _walker = walker ?? throw new ArgumentNullException(nameof(walker));
        _cadence = cadence ?? throw new ArgumentNullException(nameof(cadence));
        _walkSpan = walkSpan;
        _standSpan = standSpan;
    }

    /// <summary>The AutonomousPosition messages sent.</summary>
    public int AutonomousPositions { get; private set; }

    /// <summary>The MoveToState messages sent.</summary>
    public int MoveToStates { get; private set; }

    /// <summary>The position of the last message sent, or null before the first.</summary>
    public AcPosition? LastSent { get; private set; }

    /// <summary>Every position sent, with the driver's clock in seconds, oldest first.</summary>
    public List<(double At, AcPosition Position)> Sent { get; } = [];

    /// <summary>True between <see cref="Run"/>'s first walking message and <see cref="Stop"/>.</summary>
    public bool Moving => _moving;

    /// <summary>The walker.</summary>
    public KinematicWalker Walker => _walker;

    /// <summary>Walks (and stands, if the driver alternates) for <paramref name="duration"/>, pumping the session; stops early when the session leaves the world.</summary>
    public void Run(TimeSpan duration)
    {
        var start = _clock.Elapsed.TotalSeconds;
        var end = start + duration.TotalSeconds;
        var last = start;
        if (!_moving)
        {
            SetMoving(true, start);
        }

        while (_client.Session.Phase == Lifestone.Net.Session.SessionPhase.InWorld)
        {
            _client.Session.Pump(TimeSpan.FromMilliseconds(8));
            var now = _clock.Elapsed.TotalSeconds;
            if (now >= end)
            {
                break;
            }

            if (_moving)
            {
                _walker.Advance(Math.Min(now - last, 0.25));
            }

            last = now;
            if (_walkSpan is not null && now >= _nextToggle)
            {
                SetMoving(!_moving, now);
            }
            else if (_moving && _cadence.ShouldSend(now, _walker.Position, _walker.Polygon))
            {
                var position = _walker.Position;
                _client.Session.SendAutonomousPosition(position, _client.Sink.Sequences, contact: true);
                AutonomousPositions++;
                Record(now, position);
            }

            _client.Session.WaitForInboundAsync(Tick, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>Stops walking: a MoveToState (Ready) at the walker's position, which is what retail sends when the key goes up.</summary>
    public void Stop()
    {
        if (_moving)
        {
            SetMoving(false, _clock.Elapsed.TotalSeconds);
        }
    }

    private void SetMoving(bool moving, double now)
    {
        _moving = moving;
        var state = moving
            ? RawMotionStateWire.Default with { ForwardCommand = MotionCommands.WalkForward, ForwardHoldKey = 1 }
            : RawMotionStateWire.Default;
        var position = _walker.Position;
        _client.Session.SendMoveToState(state, position, _client.Sink.Sequences, contact: true, standingLongJump: false);
        MoveToStates++;
        Record(now, position);
        _nextToggle = now + (_walkSpan is { } walk ? (moving ? walk.TotalSeconds : _standSpan.TotalSeconds) : double.MaxValue);
    }

    private void Record(double now, AcPosition position)
    {
        _cadence.Sent(now, position, _walker.Polygon);
        LastSent = position;
        Sent.Add((now, position));
    }
}
