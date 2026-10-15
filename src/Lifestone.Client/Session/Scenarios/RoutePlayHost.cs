using System.Numerics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The game's player as a movement scenario's walker (G6 W12; <c>--login-check=academy-exit|holtburg-laps|all</c>). Headless, a
/// <see cref="RoutePlay"/> makes its own <see cref="OfflineWalker"/> in its own physics world; in the game the walker belongs to the
/// player controller (it ticks, streams collision, is placed by every teleport and follows the server's moves and MoveTos), so the scenario
/// only drives its <see cref="PlayerMovementController"/> and audits what it sends. The host (play mode) gives the player's rig
/// <see cref="Sink"/> as its movement sink and <see cref="Inner"/> as the real one behind it (the presence gate over the session's
/// movement channel); a scenario puts its <see cref="MovementAudit"/> in between with <see cref="Current"/> for the length of its walk and
/// holds the movement back with <see cref="Hold"/> while the character is being sent home.
/// </summary>
public sealed class RoutePlayHost
{
    private readonly Func<OfflineWalker?> _walker;
    private readonly Func<long> _held;

    /// <summary>A host.</summary>
    /// <param name="walker">The player's walker now, or null while there is no player.</param>
    /// <param name="held">How many movement messages the gate has held back so far (cumulative).</param>
    public RoutePlayHost(Func<OfflineWalker?> walker, Func<long> held)
    {
        ArgumentNullException.ThrowIfNull(walker);
        ArgumentNullException.ThrowIfNull(held);
        (_walker, _held) = (walker, held);
        Sink = new Forwarder(this);
    }

    /// <summary>The sink the player's controller sends to: the scenario's audit while one is current, else <see cref="Inner"/>.</summary>
    public IMovementSink Sink { get; }

    /// <summary>The real sink behind the audit (the presence gate), set by the host when it builds the player; null before.</summary>
    public IMovementSink? Inner { get; set; }

    /// <summary>The scenario's audit (the walk in progress), or null.</summary>
    public IMovementSink? Current { get; set; }

    /// <summary>True while the scenario wants no movement sent (the character is being sent home); read by the host's gate.</summary>
    public Func<bool>? Hold { get; set; }

    /// <summary>The player's walker, or null while there is none.</summary>
    public OfflineWalker? Walker => _walker();

    /// <summary>How many messages the gate has held back so far.</summary>
    public long Held => _held();

    /// <summary>True when the scenario is holding the movement back.</summary>
    public bool Holding => Hold?.Invoke() ?? false;

    private sealed class Forwarder(RoutePlayHost host) : IMovementSink
    {
        private IMovementSink Target => host.Current ?? host.Inner ?? throw new InvalidOperationException("the movement host has no sink behind it (TODO(g6-logincheck-sink): the host sets Inner when it builds the player)");

        public void MoveToState(RawMotionState raw, in CellFrame position, bool contact, bool standingLongJump) => Target.MoveToState(raw, position, contact, standingLongJump);

        public void AutonomousPosition(in CellFrame position, bool contact) => Target.AutonomousPosition(position, contact);

        public void Jump(float extent, Vector3 velocity, in CellFrame position) => Target.Jump(extent, velocity, position);
    }
}
