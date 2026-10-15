using Lifestone.Data.World;

namespace Lifestone.Data.Physics.World;

/// <summary>
/// Where the chase camera's viewer sphere ends after <see cref="PhysicsWorld.SweepViewer"/>: the transition retail runs
/// for the camera (<c>CTransition</c> with the <c>IsViewer</c> object flag), so walls and floors stop the camera and its
/// cell is known for the interiors.
/// </summary>
/// <param name="Position">The viewer's cell and frame at the end of the sweep (the target when nothing was in the way).</param>
/// <param name="Blocked">True when collision stopped the sweep short of the target.</param>
public readonly record struct ViewerSweepResult(CellFrame Position, bool Blocked);
