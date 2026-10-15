// Partly ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Extensions/Vector3Extensions.cs (Dot2D,
// LengthSquared2D, IsValid) and Source/ACE.Server/Physics/Common/Vector.cs (Vec.NormalizeCheckSmall, Vec.IsZero)
// (AGPL-3.0) with changes: normalize_check_small is the scenery track's x87 re-expression (RetailFrame); the 2D length
// helpers return float, as retail's float locals hold them; heading and angle helpers are left out until a step uses them.

using System.Numerics;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Data.Physics.Primitives;

/// <summary>Small vector helpers the physics code shares (retail <c>AC1Legacy::Vector3</c> members and inlined idioms).</summary>
public static class VectorMath
{
    /// <summary>The horizontal dot product <c>a.x * b.x + a.y * b.y</c>.</summary>
    public static float Dot2D(this Vector3 a, Vector3 b) => (a.X * b.X) + (a.Y * b.Y);

    /// <summary>The squared horizontal length <c>x² + y²</c>.</summary>
    public static float LengthSquared2D(this Vector3 v) => (v.X * v.X) + (v.Y * v.Y);

    /// <summary>
    /// Retail <c>AC1Legacy::Vector3::normalize_check_small</c>: true (and <paramref name="v"/> unchanged) when its length
    /// is below <see cref="PhysicsGlobals.Epsilon"/>; otherwise scales it to unit length and returns false. The x87 order
    /// is <see cref="RetailFrame.NormalizeCheckSmall"/>'s, which the scenery track checked against the machine code.
    /// </summary>
    public static bool NormalizeCheckSmall(ref Vector3 v) => RetailFrame.NormalizeCheckSmall(ref v);

    /// <summary>True when every component is within <see cref="PhysicsGlobals.Epsilon"/> of zero (ACE <c>Vec.IsZero</c>).</summary>
    public static bool IsZero(Vector3 v) =>
        MathF.Abs(v.X) < PhysicsGlobals.Epsilon && MathF.Abs(v.Y) < PhysicsGlobals.Epsilon && MathF.Abs(v.Z) < PhysicsGlobals.Epsilon;

    /// <summary>True when no component is NaN (ACE <c>Vector3Extensions.IsValid</c>).</summary>
    public static bool IsValid(this Vector3 v) => !float.IsNaN(v.X) && !float.IsNaN(v.Y) && !float.IsNaN(v.Z);
}
