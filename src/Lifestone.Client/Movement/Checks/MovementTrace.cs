using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Checks;

/// <summary>Which movement message a <see cref="SentMessage"/> is.</summary>
public enum SentKind
{
    /// <summary>GameAction 0xF61C MoveToState (<c>CommandInterpreter::SendMovementEvent</c>).</summary>
    MoveToState,

    /// <summary>GameAction 0xF753 AutonomousPosition (<c>CommandInterpreter::SendPositionEvent</c>).</summary>
    AutonomousPosition,

    /// <summary>GameAction 0xF61B Jump (<c>ClientCombatSystem::DoJump</c>).</summary>
    Jump,
}

/// <summary>One movement message the controller sent, with the state it was sent in.</summary>
/// <param name="Kind">Which message.</param>
/// <param name="Time">The controller's clock when it was sent (seconds).</param>
/// <param name="Position">The position it carried.</param>
/// <param name="OnGround">Whether the player stood on walkable ground when it was sent (retail's Contact and OnWalkable).</param>
/// <param name="ContactPlane">The player's contact plane when it was sent (retail compares it with the last one sent).</param>
/// <param name="ForwardCommand">A MoveToState's forward command (raw state; 0 for the other messages).</param>
/// <param name="HoldKey">A MoveToState's current hold key (raw state: <see cref="HoldKey.Run"/> when the player is running); <see cref="HoldKey.Invalid"/> for the other messages.</param>
/// <param name="Velocity">A Jump's velocity (retail's local frame: x right, y forward, z up); zero for the other messages.</param>
public readonly record struct SentMessage(SentKind Kind, double Time, CellFrame Position, bool OnGround, Plane ContactPlane, uint ForwardCommand = 0, HoldKey HoldKey = HoldKey.Invalid, Vector3 Velocity = default);

/// <summary>
/// Records every movement message the <see cref="PlayerMovementController"/> sends, with the controller's clock and ground state at the time
/// (the walk checks' witness: <c>--offline-player-checks</c> and the tests). It is an <see cref="IMovementSink"/> that sends nothing: the explore
/// mode's dry-run sink passes each message to it (<see cref="DryRunMovementSink.Tap"/>), or a headless host gives it to the controller directly.
/// </summary>
/// <remarks>Main thread only. <see cref="Attach"/> the controller once it exists (the sink is needed to make it).</remarks>
public sealed class MovementTrace : IMovementSink
{
    private readonly List<SentMessage> _messages = [];
    private PlayerMovementController? _controller;

    /// <summary>Every message so far, in order.</summary>
    public IReadOnlyList<SentMessage> Messages => _messages;

    /// <summary>Gives the trace the controller whose clock and ground state stamp the messages.</summary>
    public void Attach(PlayerMovementController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
    }

    /// <summary>Messages of <paramref name="kind"/> from message number <paramref name="from"/> on.</summary>
    public int Count(SentKind kind, int from = 0)
    {
        var count = 0;
        for (var i = Math.Max(0, from); i < _messages.Count; i++)
        {
            if (_messages[i].Kind == kind)
            {
                count++;
            }
        }

        return count;
    }

    /// <inheritdoc/>
    public void MoveToState(RawMotionState raw, in CellFrame position, bool contact, bool standingLongJump) =>
        Add(SentKind.MoveToState, position, forwardCommand: raw.ForwardCommand, holdKey: raw.CurrentHoldKey);

    /// <inheritdoc/>
    public void AutonomousPosition(in CellFrame position, bool contact) => Add(SentKind.AutonomousPosition, position);

    /// <inheritdoc/>
    public void Jump(float extent, Vector3 velocity, in CellFrame position) => Add(SentKind.Jump, position, velocity: velocity);

    private void Add(SentKind kind, in CellFrame position, uint forwardCommand = 0, HoldKey holdKey = HoldKey.Invalid, Vector3 velocity = default)
    {
        var controller = _controller ?? throw new InvalidOperationException("the trace has no controller (call Attach)");
        _messages.Add(new SentMessage(kind, controller.Now, position, controller.OnGround, controller.Player.ContactPlane, forwardCommand, holdKey, velocity));
    }
}
