using System.Numerics;

namespace Lifestone.Data.Models;

/// <summary>A sphere in a model's local AC frame (Z up, metres). Retail <c>CSphere</c>.</summary>
public readonly record struct ModelSphere(Vector3 Center, float Radius)
{
    public static readonly ModelSphere Zero = new(Vector3.Zero, 0f);
}

/// <summary>
/// A vertical cylinder-sphere (retail <c>CCylSphere</c>: low point, radius, height) in a Setup's local frame.
/// </summary>
public readonly record struct ModelCylSphere(Vector3 LowPoint, float Radius, float Height);

/// <summary>
/// A rigid frame: translation plus rotation, as stored in the dats (retail <c>Frame</c>/<c>AFrame</c>).
/// </summary>
/// <remarks>
/// On disk a frame is <c>Vector3 origin</c> followed by a quaternion stored <b>W, X, Y, Z</b>
/// (ACE <c>Source/ACE.DatLoader/Entity/Frame.cs</c>; DatReaderWriter <c>DatBinReader.ReadQuaternion</c> reads w, x, y, z
/// and builds <c>new Quaternion(x, y, z, w)</c>). <see cref="Orientation"/> is therefore a normal
/// <see cref="System.Numerics.Quaternion"/> and rotates AC-space vectors with <see cref="Vector3.Transform(Vector3, Quaternion)"/>.
/// </remarks>
public readonly record struct PartFrame(Vector3 Origin, Quaternion Orientation)
{
    public static readonly PartFrame Identity = new(Vector3.Zero, Quaternion.Identity);

    /// <summary>Transforms a point from this frame's local space into its parent space.</summary>
    public Vector3 TransformPoint(Vector3 local) => Origin + Vector3.Transform(local, Orientation);
}

/// <summary>
/// Where one part of an object is drawn, relative to the <b>object's</b> frame (not to a parent part), plus the
/// scale applied to that part's mesh vertices in part-local space.
/// </summary>
/// <remarks>
/// Retail composition (<c>CPartArray::UpdateParts</c> → <c>Frame::combine</c>, <c>Render::SetObjectScale</c>):
/// <c>world = objectFrame ∘ T(Origin) ∘ R(Orientation) ∘ S(Scale)</c>, where the object frame is the placed
/// object's position and heading. <see cref="ToMatrix"/> builds the <c>S·R·T</c> part of that in System.Numerics'
/// row-vector convention, so <c>Vector3.Transform(v, pose.ToMatrix() * objectMatrix)</c> gives the world point.
/// </remarks>
public readonly record struct PartPose(Vector3 Origin, Quaternion Orientation, Vector3 Scale)
{
    /// <summary>Part-local vertex → object space: scale, then rotate, then translate.</summary>
    public Matrix4x4 ToMatrix() =>
        Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Orientation) * Matrix4x4.CreateTranslation(Origin);

    /// <summary>Transforms a part-local mesh vertex into object space.</summary>
    public Vector3 TransformPoint(Vector3 local) => Origin + Vector3.Transform(local * Scale, Orientation);
}
