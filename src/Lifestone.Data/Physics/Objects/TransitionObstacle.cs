namespace Lifestone.Data.Physics.Objects;

/// <summary>What stopped a transition that <see cref="PhysicsObj.TryTransitionTo"/> ran (Lifestone's diagnostic: retail records the same fields in <c>COLLISIONINFO</c> and discards them).</summary>
public enum TransitionObstacleKind : byte
{
    /// <summary>Nothing recorded: the transition was not stopped, or the collision info holds no cause.</summary>
    None,

    /// <summary>Another physics object stopped it (<see cref="TransitionObstacle.Object"/>; retail <c>last_collided_object</c>).</summary>
    Object,

    /// <summary>The environment did: terrain, a building, a cell's walls or a static scenery object (a collision or sliding normal with no object).</summary>
    Environment,

    /// <summary>Only a refused step-up or edge slide is recorded (no object, no normal): a sill or an edge the object cannot cross.</summary>
    Step,
}

/// <summary>
/// The last obstacle of one transition, read after it ran: an object, the environment with the cell the object is in, or only a
/// refused step or edge. A value (no allocation).
/// </summary>
/// <param name="Kind">What stopped it.</param>
/// <param name="Object">The object that stopped it, for <see cref="TransitionObstacleKind.Object"/>.</param>
/// <param name="CellId">The cell the object stood in after the transition (the environment it met belongs to its neighbourhood).</param>
/// <param name="StepRefused">The transition refused a step-up or an edge slide on the way (also true for a wall: a wall is a step too high).</param>
/// <param name="Normal">The sliding normal, else the collision normal, of the transition (zero when it has neither): a wall's is level, a floor's or a ramp's points up.</param>
public readonly record struct TransitionObstacle(TransitionObstacleKind Kind, PhysicsObj? Object, uint CellId, bool StepRefused, System.Numerics.Vector3 Normal = default);
