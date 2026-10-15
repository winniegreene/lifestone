using System.Numerics;
using Lifestone.Data.World;

namespace Lifestone.Data.Physics;

/// <summary>
/// Finds the walkable ground under an object that the server moves, so a remote object interpolated between two
/// UpdatePositions can be kept on the terrain and on EnvCell floors without running full collision transitions for it
/// (owner decision D24: interpolation plus a ground-probe clamp, a flagged deviation; retail steps every active object
/// through <c>CTransition</c>).
/// </summary>
/// <remarks>
/// <para><b>Provider:</b> the physics track (<see cref="World.PhysicsWorld"/> implements it, step PH8; docs/physics.md §13).
/// <b>Consumers:</b> the avatars track's AvatarBody (AV7) and the wave-2 world objects; the clamp is switched by
/// <c>--remote-ground-clamp</c>.</para>
/// <para>Main thread (or the headless tick) only, like the world it reads. A probe never loads anything: where the
/// landblock's collision is not attached it answers false.</para>
/// </remarks>
public interface IGroundProbe
{
    /// <summary>
    /// The height at which an object of foot-sphere <paramref name="radius"/> (centred that high above the origin)
    /// standing at <paramref name="at"/>'s origin rests on walkable ground (a contact plane whose normal Z is at least
    /// <c>FloorZ</c>, 0.66417414), found by retail's step-down from a little above the origin to a little below it
    /// (<see cref="World.PhysicsWorld.GroundProbeRise"/>, <see cref="World.PhysicsWorld.GroundProbeDrop"/>).
    /// </summary>
    /// <param name="at">The object's cell and frame; only the cell and the origin are read.</param>
    /// <param name="radius">The radius of the object's lowest sphere (its Setup's first sphere, scaled).</param>
    /// <param name="z">The origin Z, in <paramref name="at"/>'s landblock space, at which the sphere touches the ground.</param>
    /// <param name="contact">The walkable plane touched, in <paramref name="at"/>'s landblock space.</param>
    /// <returns>False when the cell's collision is not attached or no walkable ground is found; the outputs are then default.</returns>
    bool TryGroundZ(in CellFrame at, float radius, out float z, out Plane contact);
}
