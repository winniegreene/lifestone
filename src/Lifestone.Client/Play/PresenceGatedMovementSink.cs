using System.Numerics;
using Lifestone.Client.Movement;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Client.Play;

/// <summary>
/// The movement sink play mode gives the player's body: it forwards to the session's channel only while the server has the player present (its Hidden
/// state cleared after LoginComplete), and holds, counts and drops everything the body sends before that. ACE ignores positions until it has handled
/// LoginComplete, and retail's player is frozen while hidden, so a body that has been placed on the ground inside portal space must not talk to the server
/// yet (found by the first live login: the channel's "sent while the player is not present" warning). Dropped messages lose nothing: the body's next
/// cadence message carries the position after the unhide: the sink is an <see cref="IMovementSinkGate"/>, so the controller sends no position
/// into it while it is closed and does not count one as sent (the first frame after the unhide sends it).
/// </summary>
/// <param name="inner">The session's movement channel.</param>
/// <param name="isPresent">True while the player is present (<see cref="SelfTravel.Present"/>); read on the game thread for each message.</param>
public sealed class PresenceGatedMovementSink(IMovementSink inner, Func<bool> isPresent) : IMovementSink, IMovementSinkGate
{
    private readonly IMovementSink _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly Func<bool> _isPresent = isPresent ?? throw new ArgumentNullException(nameof(isPresent));

    /// <summary>The channel the messages go to once the player is present.</summary>
    public IMovementSink Inner => _inner;

    /// <inheritdoc/>
    public bool IsOpen => _isPresent();

    /// <summary>How many messages were held back because the player was not present.</summary>
    public long Held { get; private set; }

    /// <inheritdoc/>
    public void MoveToState(RawMotionState raw, in CellFrame position, bool contact, bool standingLongJump)
    {
        if (_isPresent())
        {
            _inner.MoveToState(raw, in position, contact, standingLongJump);
        }
        else
        {
            Held++;
        }
    }

    /// <inheritdoc/>
    public void AutonomousPosition(in CellFrame position, bool contact)
    {
        if (_isPresent())
        {
            _inner.AutonomousPosition(in position, contact);
        }
        else
        {
            Held++;
        }
    }

    /// <inheritdoc/>
    public void Jump(float extent, Vector3 velocity, in CellFrame position)
    {
        if (_isPresent())
        {
            _inner.Jump(extent, velocity, in position);
        }
        else
        {
            Held++;
        }
    }
}
