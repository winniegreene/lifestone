// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Ray.cs (AGPL-3.0)
// with changes: a struct; FromOffset follows retail Ray::InitFromOffset (ACE compares the offset with the start point
// instead of with zero, and leaves the start point and direction unset for a short offset).

using System.Numerics;

namespace Lifestone.Data.Physics.Primitives;

/// <summary>A ray: a start point, a unit direction and a length (retail <c>Ray</c>: <c>pt</c>, <c>dir</c>, <c>length</c>).</summary>
/// <param name="Point">The start point.</param>
/// <param name="Direction">The unit direction.</param>
/// <param name="Length">The length along <paramref name="Direction"/>.</param>
public readonly record struct Ray(Vector3 Point, Vector3 Direction, float Length)
{
    /// <summary>
    /// Retail <c>Ray::InitFromOffset</c> (11.4186 0x0050D330, 11.6096 0x0050DE00): a ray from <paramref name="start"/>
    /// along <paramref name="offset"/>. When every component of the offset is within <see cref="PhysicsGlobals.Epsilon"/>
    /// of zero, the ray has length 0 and points along +Y, as retail sets it.
    /// </summary>
    public static Ray FromOffset(Vector3 start, Vector3 offset)
    {
        if (MathF.Abs(offset.X) > PhysicsGlobals.Epsilon
            || MathF.Abs(offset.Y) > PhysicsGlobals.Epsilon
            || MathF.Abs(offset.Z) > PhysicsGlobals.Epsilon)
        {
            var length = (float)Math.Sqrt((offset.Z * offset.Z) + (offset.Y * offset.Y) + (offset.X * offset.X));
            var inverse = 1.0 / length;
            return new Ray(start, new Vector3((float)(inverse * offset.X), (float)(inverse * offset.Y), (float)(inverse * offset.Z)), length);
        }

        return new Ray(start, Vector3.UnitY, 0f);
    }
}
