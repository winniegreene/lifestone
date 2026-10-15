using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Routes;

/// <summary>
/// The DoD 8 acceptance counts of one walk: what was sent, how often ACE corrected the player, and how far ACE's
/// "where am I" was from the client at the stops.
/// </summary>
/// <remarks>Fields may be added (init-only); none is removed or renamed.</remarks>
public sealed record MovementAuditSummary
{
    /// <summary>The largest where-am-I distance, in metres, that still passes (DoD 8: "agrees within 0.5 m").</summary>
    public const double WhereToleranceMetres = 0.5;

    /// <summary>The route's name (<see cref="ScriptedRoute.Name"/>).</summary>
    public required string Route { get; init; }

    /// <summary>Laps walked.</summary>
    public int Laps { get; init; }

    /// <summary>MoveToState messages sent (one per movement change).</summary>
    public int MoveToStateSent { get; init; }

    /// <summary>AutonomousPosition messages sent.</summary>
    public int AutonomousPositionSent { get; init; }

    /// <summary>Jump messages sent.</summary>
    public int JumpSent { get; init; }

    /// <summary>
    /// Server corrections: UpdatePositions for the player whose force-position stamp advanced (the session's
    /// <c>ForceBlip</c> verdict). DoD 8 requires zero.
    /// </summary>
    public int Corrections { get; init; }

    /// <summary>UpdatePositions for the player that echoed an accepted position (the session's <c>SelfEcho</c> verdict).</summary>
    public int SelfEchoes { get; init; }

    /// <summary>Teleport arrivals of the player (the session's <c>SelfTeleport</c> verdict).</summary>
    public int Teleports { get; init; }

    /// <summary>
    /// The longest time, in seconds, from a movement message to the next AutonomousPosition while the player held a
    /// movement (no Jump in between): retail's cadence keeps it at 1.0 s plus a frame on the ground. 0 when no
    /// AutonomousPosition followed a movement.
    /// </summary>
    public double MaxAutonomousPositionIntervalSeconds { get; init; }

    /// <summary>The longest time, in seconds, from an AutonomousPosition to the next echo of the player; null without a clock or an echo.</summary>
    public double? MaxEchoDelaySeconds { get; init; }

    /// <summary>The where-am-I checks made.</summary>
    public int WhereChecks { get; init; }

    /// <summary>The largest where-am-I distance in metres over the stops, or null when no check ran.</summary>
    public double? WhereMaxMetres { get; init; }

    /// <summary>The server's force-position sequence at the first where-am-I check, when the check reported it.</summary>
    public ushort? ForcePositionSequenceFirst { get; init; }

    /// <summary>The server's force-position sequence at the last where-am-I check (equal to the first when nothing corrected the player).</summary>
    public ushort? ForcePositionSequenceLast { get; init; }

    /// <summary>True when there were no corrections and every where-am-I check agreed within <see cref="WhereToleranceMetres"/>; false when no check ran.</summary>
    public bool Passed => Corrections == 0 && WhereMaxMetres is { } where && where <= WhereToleranceMetres
        && ForcePositionSequenceFirst == ForcePositionSequenceLast;
}

/// <summary>
/// Audits a walk for DoD 8: it decorates the session's movement sink to count what the controller sends, follows the
/// session's events for the player's own UpdatePositions (corrections, echoes and teleports), records the where-am-I
/// checks, and prints the movement verdict line (m2-plan §4.14).
/// </summary>
/// <remarks>
/// <para><b>Use.</b> Give the audit, not the session's sink, to the <see cref="PlayerMovementController"/>; it passes every
/// call on to the inner sink unchanged. Call <see cref="CompleteLap"/> as the route's laps end and
/// <see cref="RecordWhere"/> with each where-am-I answer; dispose it to stop following the bus.</para>
/// <para><b>What counts</b> as a correction is the session's <see cref="PositionVerdict.ForceBlip"/> for the player:
/// ACE advances the force-position sequence only when it overrides the client (its z-hack snap, Player_Tick.cs, and the
/// PK-lite reset, Player.cs). The where-am-I distance is the full 3D distance across landblocks
/// (<see cref="PhysicsPositions.Distance"/>).</para>
/// <para><b>Files.</b> <see cref="ToJson"/> gives the summary as JSON; the host writes it (to <c>cache/movement/</c>), since
/// Lifestone.Client does no file I/O.</para>
/// <para>Game thread only, like the bus and the controller.</para>
/// </remarks>
public sealed class MovementAudit : IMovementSink, IMovementSinkGate, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IMovementSink _inner;
    private readonly Func<double>? _clock;
    private readonly IDisposable _subscription;
    private int _moveToState;
    private int _autonomousPosition;
    private int _jump;
    private int _corrections;
    private int _echoes;
    private int _teleports;
    private int _laps;
    private int _whereChecks;
    private double? _whereMax;
    private ushort? _forceFirst;
    private ushort? _forceLast;
    private bool _moving;
    private bool _jumpSincePosition;
    private double _lastSendTime = double.NaN;
    private double _lastPositionTime = double.NaN;
    private double _maxInterval;
    private double? _maxEchoDelay;
    private bool _disposed;

    /// <summary>Open unless the sink it wraps is a closed <see cref="IMovementSinkGate"/> (the controller then sends no position into it).</summary>
    public bool IsOpen => _inner is not IMovementSinkGate gate || gate.IsOpen;

    /// <summary>An audit of <paramref name="inner"/> that follows <paramref name="bus"/> (m2-plan §4.10).</summary>
    /// <param name="inner">The session's movement sink.</param>
    /// <param name="bus">The session's event bus.</param>
    /// <param name="clock">The clock the intervals are measured with (seconds), or null to measure none.</param>
    /// <param name="route">The route's name for the summary line.</param>
    public MovementAudit(IMovementSink inner, IEventBus bus, Func<double>? clock = null, string route = "unnamed")
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        _inner = inner;
        _clock = clock;
        Route = route;
        _subscription = bus.Subscribe<ObjectMoved>(OnObjectMoved);
    }

    /// <summary>The route's name for the summary.</summary>
    public string Route { get; set; }

    /// <summary>The counts so far.</summary>
    public MovementAuditSummary Summary => new()
    {
        Route = Route,
        Laps = _laps,
        MoveToStateSent = _moveToState,
        AutonomousPositionSent = _autonomousPosition,
        JumpSent = _jump,
        Corrections = _corrections,
        SelfEchoes = _echoes,
        Teleports = _teleports,
        MaxAutonomousPositionIntervalSeconds = _maxInterval,
        MaxEchoDelaySeconds = _maxEchoDelay,
        WhereChecks = _whereChecks,
        WhereMaxMetres = _whereMax,
        ForcePositionSequenceFirst = _forceFirst,
        ForcePositionSequenceLast = _forceLast,
    };

    /// <summary>The verdict line for <see cref="Summary"/> (<see cref="FormatSummaryLine"/>).</summary>
    public string SummaryLine() => FormatSummaryLine(Summary);

    /// <summary>The summary as indented JSON, for <c>cache/movement/&lt;run&gt;.json</c>.</summary>
    public string ToJson() => JsonSerializer.Serialize(Summary, JsonOptions);

    /// <summary>
    /// The movement verdict line, a frozen text contract (m2-plan §4.14):
    /// <c>movement route=&lt;r&gt; laps=&lt;n&gt; corrections=&lt;n&gt; where_max_m=&lt;x&gt; result=PASS|FAIL</c>, with
    /// <c>where_max_m</c> in metres to two decimals (invariant culture), or <c>none</c> when no check ran.
    /// </summary>
    public static string FormatSummaryLine(MovementAuditSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var where = summary.WhereMaxMetres is { } metres ? metres.ToString("0.00", CultureInfo.InvariantCulture) : "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"movement route={summary.Route} laps={summary.Laps} corrections={summary.Corrections} where_max_m={where} result={(summary.Passed ? "PASS" : "FAIL")}");
    }

    /// <summary>A lap of the route ended.</summary>
    public void CompleteLap() => _laps++;

    /// <summary>
    /// Records a where-am-I check: the client's position <paramref name="client"/> against the server's
    /// <paramref name="server"/>, and the server's force-position sequence when the answer carried it.
    /// </summary>
    /// <returns>The distance in metres.</returns>
    public double RecordWhere(in CellFrame client, in CellFrame server, ushort? forcePositionSequence = null)
    {
        var distance = (double)client.Distance(server);
        _whereChecks++;
        if (_whereMax is not { } max || distance > max)
        {
            _whereMax = distance;
        }

        if (forcePositionSequence is { } sequence)
        {
            _forceFirst ??= sequence;
            _forceLast = sequence;
        }

        return distance;
    }

    /// <inheritdoc/>
    public void MoveToState(RawMotionState raw, in CellFrame position, bool contact, bool standingLongJump)
    {
        ArgumentNullException.ThrowIfNull(raw);
        _moveToState++;
        _moving = raw.ForwardCommand != MotionCommands.Ready || raw.SideStepCommand != 0 || raw.TurnCommand != 0;
        Sent();
        _inner.MoveToState(raw, position, contact, standingLongJump);
    }

    /// <inheritdoc/>
    public void AutonomousPosition(in CellFrame position, bool contact)
    {
        _autonomousPosition++;
        if (_clock is not null)
        {
            var now = _clock();
            if (_moving && !_jumpSincePosition && !double.IsNaN(_lastSendTime))
            {
                _maxInterval = Math.Max(_maxInterval, now - _lastSendTime);
            }

            _lastPositionTime = now;
        }

        _jumpSincePosition = false;
        Sent();
        _inner.AutonomousPosition(position, contact);
    }

    /// <inheritdoc/>
    public void Jump(float extent, Vector3 velocity, in CellFrame position)
    {
        _jump++;
        _jumpSincePosition = true;
        Sent();
        _inner.Jump(extent, velocity, position);
    }

    /// <summary>Stops following the bus.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _subscription.Dispose();
        }
    }

    private void Sent()
    {
        if (_clock is not null)
        {
            _lastSendTime = _clock();
        }
    }

    private void OnObjectMoved(in ObjectMoved e)
    {
        switch (e.Verdict)
        {
            case PositionVerdict.ForceBlip:
                _corrections++;
                break;
            case PositionVerdict.SelfTeleport:
                _teleports++;
                break;
            case PositionVerdict.SelfEcho:
                _echoes++;
                if (_clock is not null && !double.IsNaN(_lastPositionTime))
                {
                    var delay = _clock() - _lastPositionTime;
                    if (_maxEchoDelay is not { } max || delay > max)
                    {
                        _maxEchoDelay = delay;
                    }
                }

                break;
        }
    }
}
