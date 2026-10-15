using System.Collections.Immutable;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Motion;
using Lifestone.Data.World;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Actions;

/// <summary>
/// The session's movement sink (<see cref="IActionApi.Movement"/>; step ST11): physics' movement controller writes
/// MoveToState, AutonomousPosition and Jump here on retail's cadence, and the channel stamps each with the player's
/// current sequences (<see cref="SelfState.Sequences"/>: instance, server control, teleport and force position) and hands
/// it to the network session at once, as retail's <c>CM_Movement::Event_MoveToState</c>, <c>Event_AutonomousPosition</c>
/// and <c>Event_Jump</c> pack them (11.4186 0x006AFC00, 0x006AF790, 0x006AFA70; 11.6096 0x006B0AD0, 0x006B0660,
/// 0x006B0940). No handle, no queue, no allocation per send (a MoveToState with queued actions copies them once).
/// </summary>
/// <remarks>
/// <para><b>Accounting, never a silent drop.</b> A send while the player is not <see cref="SelfTravel.Present"/> still
/// goes out (ACE ignores positions until LoginComplete has been handled, protocol.md §4), and is counted in
/// <see cref="SentWhileNotPresent"/> and logged as a WARNING once per arrival. With no network session to send to (an
/// offline session, or outside the world) nothing is sent; that is counted in <see cref="Unsent"/> and logged as a
/// WARNING once per arrival too. Physics' <c>MovementAudit</c> decorates this sink for the DoD 8 counts.</para>
/// <para>Game thread only.</para>
/// </remarks>
internal sealed class MovementChannel : IMovementSink
{
    private readonly GameState _state;
    private readonly Func<IAcSession?>? _session;
    private readonly ILogSink _log;
    private readonly GameThreadBinding? _thread;
    private bool _warnedNotPresent;
    private bool _warnedUnsent;

    /// <summary>A channel writing to the network session <paramref name="session"/> returns.</summary>
    /// <param name="state">The session's state (the player's sequences, phase and travel).</param>
    /// <param name="session">The current network session, or null for an offline session (nothing can be sent).</param>
    /// <param name="log">Where the warnings go.</param>
    /// <param name="thread">The session's game-thread binding, or null (tests).</param>
    internal MovementChannel(GameState state, Func<IAcSession?>? session, ILogSink log, GameThreadBinding? thread)
    {
        _state = state;
        _session = session;
        _log = log;
        _thread = thread;
    }

    /// <summary>MoveToState messages handed to the network session.</summary>
    internal long MoveToStateSent { get; private set; }

    /// <summary>AutonomousPosition messages handed to the network session.</summary>
    internal long AutonomousPositionSent { get; private set; }

    /// <summary>Jump messages handed to the network session.</summary>
    internal long JumpSent { get; private set; }

    /// <summary>Messages sent while the player was not <see cref="SelfTravel.Present"/> (ACE ignores their positions).</summary>
    internal long SentWhileNotPresent { get; private set; }

    /// <summary>Messages not sent: no network session, or not in the world.</summary>
    internal long Unsent { get; private set; }

    /// <inheritdoc/>
    public void MoveToState(RawMotionState raw, in CellFrame position, bool contact, bool standingLongJump)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (Begin("MoveToState") is not { } session)
        {
            return;
        }

        try
        {
            session.SendMoveToState(ToWire(raw), ToWire(in position), _state.Self.Sequences, contact, standingLongJump);
            MoveToStateSent++;
        }
        catch (InvalidOperationException e)
        {
            Refused("MoveToState", e);
        }
    }

    /// <inheritdoc/>
    public void AutonomousPosition(in CellFrame position, bool contact)
    {
        if (Begin("AutonomousPosition") is not { } session)
        {
            return;
        }

        try
        {
            session.SendAutonomousPosition(ToWire(in position), _state.Self.Sequences, contact);
            AutonomousPositionSent++;
        }
        catch (InvalidOperationException e)
        {
            Refused("AutonomousPosition", e);
        }
    }

    /// <inheritdoc/>
    public void Jump(float extent, Vector3 velocity, in CellFrame position)
    {
        if (Begin("Jump") is not { } session)
        {
            return;
        }

        try
        {
            session.SendJump(extent, velocity, ToWire(in position), _state.Self.Sequences);
            JumpSent++;
        }
        catch (InvalidOperationException e)
        {
            Refused("Jump", e);
        }
    }

    /// <summary>The player's travel state changed: an arrival completed, so the once-per-arrival warnings may repeat.</summary>
    internal void OnTravel(in SelfTravelChanged e)
    {
        if (e.Travel == SelfTravel.Present)
        {
            _warnedNotPresent = false;
            _warnedUnsent = false;
        }
    }

    /// <summary>Forgets the warnings given (a reconnect).</summary>
    internal void Reset()
    {
        _warnedNotPresent = false;
        _warnedUnsent = false;
    }

    /// <summary>The retail wire form of <paramref name="raw"/> (full 32-bit commands; hold keys as their values).</summary>
    internal static RawMotionStateWire ToWire(RawMotionState raw)
    {
        var actions = ImmutableArray<RawMotionAction>.Empty;
        if (raw.Actions.Count > 0)
        {
            var builder = ImmutableArray.CreateBuilder<RawMotionAction>(raw.Actions.Count);
            foreach (var action in raw.Actions)
            {
                builder.Add(new RawMotionAction(action.Action, action.Stamp, action.Autonomous, action.Speed));
            }

            actions = builder.MoveToImmutable();
        }

        return new RawMotionStateWire(
            (uint)raw.CurrentHoldKey,
            raw.CurrentStyle,
            raw.ForwardCommand,
            (uint)raw.ForwardHoldKey,
            raw.ForwardSpeed,
            raw.SideStepCommand,
            (uint)raw.SideStepHoldKey,
            raw.SideStepSpeed,
            raw.TurnCommand,
            (uint)raw.TurnHoldKey,
            raw.TurnSpeed,
            actions);
    }

    /// <summary>The wire form of a position (the rotation as given).</summary>
    internal static AcPosition ToWire(in CellFrame position) => new(position.CellId, position.Frame.Origin, position.Frame.Orientation);

    /// <summary>The network session to send to, or null after counting and (once) logging why there is none.</summary>
    private IAcSession? Begin(string what)
    {
        _thread?.Check("IMovementSink." + what);
        var session = _session?.Invoke();
        if (session is null || _state.Phase != SessionPhase.InWorld)
        {
            Unsent++;
            if (!_warnedUnsent)
            {
                _warnedUnsent = true;
                var why = _session is null
                    ? "offline session (no server)"
                    : session is null ? "there is no network session" : "not in the world (phase " + _state.Phase + ")";
                _log.Warn(SessionLog.Category, "movement messages are not sent: " + why + "; first: " + what);
            }

            return null;
        }

        if (_state.Self.Travel != SelfTravel.Present)
        {
            SentWhileNotPresent++;
            if (!_warnedNotPresent)
            {
                _warnedNotPresent = true;
                _log.Warn(
                    SessionLog.Category,
                    "a movement message was sent while the player is not present (ACE ignores positions until it has handled LoginComplete); first: " + what,
                    new LogField("travel", _state.Self.Travel));
            }
        }

        return session;
    }

    private void Refused(string what, InvalidOperationException e)
    {
        Unsent++;
        if (!_warnedUnsent)
        {
            _warnedUnsent = true;
            _log.Warn(SessionLog.Category, "movement messages are not sent: the network session refused " + what + ": " + e.Message);
        }
    }
}
