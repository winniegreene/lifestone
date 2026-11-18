using Lifestone.Data.World;

namespace Lifestone.Data.Physics.World;

/// <summary>What <see cref="PhysicsWorld.SweepBolt"/> found.</summary>
public enum BoltLine
{
    /// <summary>The path reached its end.</summary>
    Clear,

    /// <summary>The world stopped the path short of its end.</summary>
    Blocked,

    /// <summary>No answer: the start or the end lies in a landblock that is not attached.</summary>
    NotAttached,
}

/// <summary>A bolt's sweep: the answer and where it ended.</summary>
/// <param name="Line">The answer.</param>
/// <param name="Reached">Where the sphere ended (the start when nothing was swept).</param>
public readonly record struct BoltSweepResult(BoltLine Line, CellFrame Reached);
