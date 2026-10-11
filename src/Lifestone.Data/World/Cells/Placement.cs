using System.Numerics;

namespace Lifestone.Data.World.Cells;

/// <summary>
/// An AC <c>Frame</c>: origin plus orientation, in AC coordinates (right-handed, Z up, metres). Frames of statics,
/// buildings and EnvCells are landblock-local: (0, 0, 0) is the landblock's south-west corner at height 0.
/// </summary>
/// <remarks>
/// <para>
/// The dat stores the quaternion as <c>w, x, y, z</c> (retail <c>Frame::UnPack</c>; DatReaderWriter
/// <c>DatBinReader.ReadQuaternion</c> reorders it into <see cref="Quaternion"/>(x, y, z, w)).
/// </para>
/// <para>
/// A local point maps to the parent frame as <c>origin + rotate(orientation, p)</c> (retail
/// <c>Frame::localtoglobal</c>). Verified on the user's dats: all 1,706,795 reciprocal ExactMatch cell-portal pairs
/// coincide to within 3.5 mm in landblock space with this convention, while the inverse rotation fails on every pair
/// whose two cells are not 0 or 180 degrees apart (see the cells tests).
/// </para>
/// </remarks>
public readonly record struct Placement(Vector3 Origin, Quaternion Orientation)
{
    public static readonly Placement Identity = new(Vector3.Zero, Quaternion.Identity);

    /// <summary>Maps a point from this frame's local space into the parent (landblock) space.</summary>
    public Vector3 LocalToParent(Vector3 local) => Vector3.Transform(local, Orientation) + Origin;

    /// <summary>Rotates a direction (normal) from local into parent space.</summary>
    public Vector3 RotateToParent(Vector3 localDirection) => Vector3.Transform(localDirection, Orientation);

    /// <summary>Maps a point from parent (landblock) space into this frame's local space.</summary>
    public Vector3 ParentToLocal(Vector3 parent) => Vector3.Transform(parent - Origin, Quaternion.Conjugate(Orientation));

    /// <summary>
    /// Local-to-parent transform for row vectors (System.Numerics convention: <c>Vector3.Transform(p, M)</c>).
    /// </summary>
    public Matrix4x4 ToMatrix() => Matrix4x4.CreateFromQuaternion(Orientation) * Matrix4x4.CreateTranslation(Origin);

    internal static Placement FromDat(DatReaderWriter.Types.Frame frame) => new(frame.Origin, frame.Orientation);
}
