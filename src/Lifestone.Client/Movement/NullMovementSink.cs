using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement;

/// <summary>
/// A movement sink with no server behind it, for the explore-mode player (<see cref="OfflineWalker.Controller"/>) and
/// tests: it sends nothing and counts what it was given.
/// </summary>
public sealed class NullMovementSink : IMovementSink
{
    /// <summary>MoveToState calls received.</summary>
    public long MoveToStateCount { get; private set; }

    /// <summary>AutonomousPosition calls received.</summary>
    public long AutonomousPositionCount { get; private set; }

    /// <summary>Jump calls received.</summary>
    public long JumpCount { get; private set; }

    /// <inheritdoc/>
    public void MoveToState(RawMotionState raw, in CellFrame position, bool contact, bool standingLongJump) => MoveToStateCount++;

    /// <inheritdoc/>
    public void AutonomousPosition(in CellFrame position, bool contact) => AutonomousPositionCount++;

    /// <inheritdoc/>
    public void Jump(float extent, Vector3 velocity, in CellFrame position) => JumpCount++;
}
