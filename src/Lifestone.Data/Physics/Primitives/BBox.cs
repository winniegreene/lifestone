// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Collision/BBox.cs (AGPL-3.0)
// with changes: a struct; retail's corner order and starting corner in LocalToLocal, LocalToGlobal and ConvertToGlobal
// (ACE starts Max at the transformed maximum corner, so a rotated box can miss a corner, and ConvertToGlobal transforms
// only two corners); GetCenter is the centre (ACE returns the half extent); no corner list is allocated.

using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Data.Physics.Primitives;

/// <summary>An axis-aligned box (retail <c>BBox</c>: <c>m_vMin</c>, <c>m_vMax</c>).</summary>
public struct BBox : IEquatable<BBox>
{
    /// <summary>The minimum corner.</summary>
    public Vector3 Min;

    /// <summary>The maximum corner.</summary>
    public Vector3 Max;

    /// <summary>A box with the given corners.</summary>
    public BBox(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>
    /// Retail <c>BBox::InitForAdjustment</c> (11.4186 0x005B1DF0, 11.6096 0x005B2EA0): an empty box, minimum at
    /// <see cref="float.MaxValue"/> and maximum at <see cref="float.MinValue"/>, ready for <see cref="AdjustBBox"/>.
    /// </summary>
    public static BBox ForAdjustment => new(new Vector3(float.MaxValue), new Vector3(float.MinValue));

    /// <summary>
    /// Retail <c>BBox::AdjustBBox</c> (11.4186 0x00534150, 11.6096 0x00534E90; not in the per-module decompile, so ACE's
    /// component-wise reading): grows the box to contain <paramref name="point"/>.
    /// </summary>
    public void AdjustBBox(Vector3 point)
    {
        if (point.X < Min.X)
        {
            Min.X = point.X;
        }

        if (point.Y < Min.Y)
        {
            Min.Y = point.Y;
        }

        if (point.Z < Min.Z)
        {
            Min.Z = point.Z;
        }

        if (point.X > Max.X)
        {
            Max.X = point.X;
        }

        if (point.Y > Max.Y)
        {
            Max.Y = point.Y;
        }

        if (point.Z > Max.Z)
        {
            Max.Z = point.Z;
        }
    }

    /// <summary>
    /// Retail <c>BBox::BuildBoundingBox</c> (11.4186 0x005B1D70, 11.6096 0x005B2E20): grows <paramref name="into"/> to
    /// contain this box.
    /// </summary>
    public readonly void BuildBoundingBox(ref BBox into)
    {
        if (Min.X < into.Min.X)
        {
            into.Min.X = Min.X;
        }

        if (Min.Y < into.Min.Y)
        {
            into.Min.Y = Min.Y;
        }

        if (Min.Z < into.Min.Z)
        {
            into.Min.Z = Min.Z;
        }

        if (Max.X > into.Max.X)
        {
            into.Max.X = Max.X;
        }

        if (Max.Y > into.Max.Y)
        {
            into.Max.Y = Max.Y;
        }

        if (Max.Z > into.Max.Z)
        {
            into.Max.Z = Max.Z;
        }
    }

    /// <summary>Retail <c>BBox::GetCenter</c> (11.4186 0x005B2790, 11.6096 0x005B3840): <c>(max + min) * 0.5</c>.</summary>
    public readonly Vector3 GetCenter() =>
        new((Max.X + Min.X) * 0.5f, (Max.Y + Min.Y) * 0.5f, (Max.Z + Min.Z) * 0.5f);

    /// <summary>
    /// Retail <c>BBox::LocalToLocal</c> (11.4186 0x005B1E60, 11.6096 0x005B2F10): the box around
    /// <paramref name="fromBox"/>'s eight corners, each taken from <paramref name="fromPos"/>'s frame into
    /// <paramref name="toPos"/>'s (<see cref="PhysicsPositions.LocalToLocal"/>).
    /// </summary>
    public static BBox LocalToLocal(in BBox fromBox, in CellFrame fromPos, in CellFrame toPos)
    {
        var first = toPos.LocalToLocal(fromPos, fromBox.Min);
        var box = new BBox(first, first);
        var min = fromBox.Min;
        var max = fromBox.Max;
        box.AdjustBBox(toPos.LocalToLocal(fromPos, new Vector3(max.X, max.Y, max.Z)));
        box.AdjustBBox(toPos.LocalToLocal(fromPos, new Vector3(max.X, max.Y, min.Z)));
        box.AdjustBBox(toPos.LocalToLocal(fromPos, new Vector3(max.X, min.Y, max.Z)));
        box.AdjustBBox(toPos.LocalToLocal(fromPos, new Vector3(min.X, max.Y, max.Z)));
        box.AdjustBBox(toPos.LocalToLocal(fromPos, new Vector3(min.X, max.Y, min.Z)));
        box.AdjustBBox(toPos.LocalToLocal(fromPos, new Vector3(max.X, min.Y, min.Z)));
        box.AdjustBBox(toPos.LocalToLocal(fromPos, new Vector3(min.X, min.Y, max.Z)));
        return box;
    }

    /// <summary>
    /// Retail <c>BBox::LocalToGlobal</c> (11.4186 0x005B2120, 11.6096 0x005B31D0): as <see cref="LocalToLocal"/>, with
    /// the corners carried into <paramref name="toPos"/>'s landblock space (<see cref="PhysicsPositions.LocalToGlobal"/>).
    /// </summary>
    public static BBox LocalToGlobal(in BBox fromBox, in CellFrame fromPos, in CellFrame toPos)
    {
        var first = toPos.LocalToGlobal(fromPos, fromBox.Min);
        var box = new BBox(first, first);
        var min = fromBox.Min;
        var max = fromBox.Max;
        box.AdjustBBox(toPos.LocalToGlobal(fromPos, new Vector3(max.X, max.Y, max.Z)));
        box.AdjustBBox(toPos.LocalToGlobal(fromPos, new Vector3(max.X, max.Y, min.Z)));
        box.AdjustBBox(toPos.LocalToGlobal(fromPos, new Vector3(max.X, min.Y, max.Z)));
        box.AdjustBBox(toPos.LocalToGlobal(fromPos, new Vector3(min.X, max.Y, max.Z)));
        box.AdjustBBox(toPos.LocalToGlobal(fromPos, new Vector3(min.X, max.Y, min.Z)));
        box.AdjustBBox(toPos.LocalToGlobal(fromPos, new Vector3(max.X, min.Y, min.Z)));
        box.AdjustBBox(toPos.LocalToGlobal(fromPos, new Vector3(min.X, min.Y, max.Z)));
        return box;
    }

    /// <summary>
    /// Retail <c>BBox::ConvertToGlobal</c> (11.4186 0x005B23E0, 11.6096 0x005B3490): the box around this box's corners
    /// moved by <paramref name="origin"/>'s frame. Retail transforms seven corners (the minimum corner and six more) and
    /// never the maximum corner (max.x, max.y, max.z); Lifestone does the same, so a rotated box can come out smaller
    /// than its true bound exactly where retail's did.
    /// </summary>
    public readonly BBox ConvertToGlobal(in AcFrame origin)
    {
        var first = origin.LocalToGlobal(Min);
        var box = new BBox(first, first);
        box.AdjustBBox(origin.LocalToGlobal(new Vector3(Max.X, Max.Y, Min.Z)));
        box.AdjustBBox(origin.LocalToGlobal(new Vector3(Max.X, Min.Y, Max.Z)));
        box.AdjustBBox(origin.LocalToGlobal(new Vector3(Min.X, Max.Y, Max.Z)));
        box.AdjustBBox(origin.LocalToGlobal(new Vector3(Min.X, Max.Y, Min.Z)));
        box.AdjustBBox(origin.LocalToGlobal(new Vector3(Max.X, Min.Y, Min.Z)));
        box.AdjustBBox(origin.LocalToGlobal(new Vector3(Min.X, Min.Y, Max.Z)));
        return box;
    }

    /// <inheritdoc/>
    public readonly bool Equals(BBox other) => Min == other.Min && Max == other.Max;

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj) => obj is BBox other && Equals(other);

    /// <inheritdoc/>
    public override readonly int GetHashCode() => HashCode.Combine(Min, Max);

    /// <inheritdoc/>
    public override readonly string ToString() => $"BBox {Min} .. {Max}";
}
