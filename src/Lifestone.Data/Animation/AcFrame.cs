using System.Numerics;

namespace Lifestone.Data.Animation;

/// <summary>
/// A rigid frame in AC coordinates (right-handed, Z up): an origin plus a unit orientation quaternion.
/// The operations re-express retail <c>Frame</c> (acclient 11.4186, PORTAL/math): <c>Frame::combine</c>,
/// <c>Frame::subtract1</c>, <c>Frame::rotate</c>, <c>Frame::grotate</c>, <c>Frame::localtoglobalvec</c>.
/// Quaternion products are Hamilton products (<c>q_out = q1 ⊗ q2</c>), see docs/research/rendering.md §1.
/// </summary>
/// <remarks>
/// Retail keeps frames in single precision and renormalises every new orientation in <c>Frame::set_rotate</c>; so do we.
/// </remarks>
public readonly record struct AcFrame(Vector3 Origin, Quaternion Orientation)
{
    /// <summary>Retail <c>F_EPSILON</c> (0.0002), used by <c>Frame::grotate</c> and the animation code.</summary>
    public const float Epsilon = 0.00019999999f;

    public static AcFrame Identity { get; } = new(Vector3.Zero, Quaternion.Identity);

    /// <summary>Rotates a local vector into the parent space (retail <c>Frame::localtoglobalvec</c>).</summary>
    public Vector3 LocalToGlobalVec(Vector3 v) => Vector3.Transform(v, Orientation);

    /// <summary>Transforms a local point into the parent space (retail <c>Frame::localtoglobal</c>).</summary>
    public Vector3 LocalToGlobal(Vector3 p) => Origin + LocalToGlobalVec(p);

    /// <summary>Rotates a parent-space vector into local space (retail <c>Frame::globaltolocalvec</c>).</summary>
    public Vector3 GlobalToLocalVec(Vector3 v) => Vector3.Transform(v, Quaternion.Conjugate(Orientation));

    /// <summary>
    /// Retail <c>Frame::combine(f1, f2)</c>: <c>origin = f1.origin + R(f1.q)·f2.origin</c>, <c>q = f1.q ⊗ f2.q</c>.
    /// </summary>
    public static AcFrame Combine(in AcFrame f1, in AcFrame f2) =>
        new(f1.LocalToGlobal(f2.Origin), SetRotate(Hamilton(f1.Orientation, f2.Orientation), f1.Orientation));

    /// <summary>
    /// Retail <c>Frame::combine(f1, f2, scale)</c> (CPartArray::UpdateParts): like <see cref="Combine(in AcFrame, in AcFrame)"/>
    /// but the child origin is scaled component-wise first.
    /// </summary>
    public static AcFrame Combine(in AcFrame f1, in AcFrame f2, Vector3 scale) =>
        new(f1.LocalToGlobal(f2.Origin * scale), SetRotate(Hamilton(f1.Orientation, f2.Orientation), f1.Orientation));

    /// <summary>
    /// Retail <c>Frame::subtract1(f1, f2)</c>, the inverse of <see cref="Combine(in AcFrame, in AcFrame)"/>:
    /// <c>q = f1.q ⊗ conj(f2.q)</c>, then <c>origin = f1.origin − R(q)·f2.origin</c>.
    /// </summary>
    public static AcFrame Subtract1(in AcFrame f1, in AcFrame f2)
    {
        var q = SetRotate(Hamilton(f1.Orientation, Quaternion.Conjugate(f2.Orientation)), f1.Orientation);
        return new AcFrame(f1.Origin - Vector3.Transform(f2.Origin, q), q);
    }

    /// <summary>
    /// Retail <c>Frame::grotate(w)</c>: rotates the frame by the parent-space rotation vector <paramref name="w"/>
    /// (axis = w/|w|, angle = |w| radians), i.e. <c>q' = q_w ⊗ q</c>. Ignored when <c>|w|² &lt; ε²</c>.
    /// </summary>
    public AcFrame GRotate(Vector3 w)
    {
        var lengthSquared = w.LengthSquared();
        if (lengthSquared < Epsilon * Epsilon)
        {
            return this;
        }

        var theta = MathF.Sqrt(lengthSquared);
        var half = theta * 0.5f;
        var s = MathF.Sin(half) / theta;
        var qw = new Quaternion(w.X * s, w.Y * s, w.Z * s, MathF.Cos(half));
        return this with { Orientation = SetRotate(Hamilton(qw, Orientation), Orientation) };
    }

    /// <summary>Retail <c>Frame::rotate(w)</c>: <paramref name="w"/> is in local space; it is rotated to the parent and applied with <see cref="GRotate"/>.</summary>
    public AcFrame Rotate(Vector3 w) => GRotate(LocalToGlobalVec(w));

    /// <summary>Hamilton product <c>a ⊗ b</c> written out as in retail <c>Frame::combine</c>.</summary>
    public static Quaternion Hamilton(Quaternion a, Quaternion b) => new(
        a.X * b.W + a.W * b.X + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Z * b.X + a.Y * b.W,
        a.X * b.Y + a.Z * b.W - a.Y * b.X + a.W * b.Z,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    /// <summary>
    /// Retail <c>Frame::set_rotate</c>: normalises the new quaternion; if the result is not finite the previous
    /// orientation is kept (retail <c>Frame::IsValid</c> check).
    /// </summary>
    public static Quaternion SetRotate(Quaternion q, Quaternion previous)
    {
        var length = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
        if (!(length > 0f) || !float.IsFinite(length))
        {
            return previous;
        }

        var inv = 1f / length;
        var n = new Quaternion(q.X * inv, q.Y * inv, q.Z * inv, q.W * inv);
        return float.IsFinite(n.X) && float.IsFinite(n.Y) && float.IsFinite(n.Z) && float.IsFinite(n.W) ? n : previous;
    }
}
