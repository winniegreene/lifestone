using System.Numerics;

namespace Lifestone.Client.Effects;

/// <summary>
/// An affine transform as the three rows of a 3×4 matrix <c>[basis | origin]</c> in the engine's axes: row r holds the
/// r-th component of the three basis columns, then of the origin. This is the per-instance layout of GPU instancing
/// buffers (Godot's <c>MultiMesh</c> TRANSFORM_3D format).
/// </summary>
public readonly record struct AffineRows(Vector4 Row0, Vector4 Row1, Vector4 Row2)
{
    public static AffineRows Identity { get; } = new(new Vector4(1, 0, 0, 0), new Vector4(0, 1, 0, 0), new Vector4(0, 0, 1, 0));

    /// <summary>Builds the rows from the basis columns (images of the unit axes) and the origin.</summary>
    public static AffineRows FromColumns(Vector3 x, Vector3 y, Vector3 z, Vector3 origin) => new(
        new Vector4(x.X, y.X, z.X, origin.X),
        new Vector4(x.Y, y.Y, z.Y, origin.Y),
        new Vector4(x.Z, y.Z, z.Z, origin.Z));

    /// <summary>Applies the transform to a point.</summary>
    public Vector3 TransformPoint(Vector3 p) => new(
        (Row0.X * p.X) + (Row0.Y * p.Y) + (Row0.Z * p.Z) + Row0.W,
        (Row1.X * p.X) + (Row1.Y * p.Y) + (Row1.Z * p.Z) + Row1.W,
        (Row2.X * p.X) + (Row2.Y * p.Y) + (Row2.Z * p.Z) + Row2.W);
}

/// <summary>
/// A growable float buffer of instances, each a 3×4 transform (<see cref="AffineRows"/>, 12 floats) followed by 4 custom
/// floats: the layout of a Godot <c>MultiMesh</c> with <c>TRANSFORM_3D</c>, no colours and custom data. The particle
/// renderer writes one per particle and hands the whole array to the engine in one call.
/// </summary>
/// <remarks>
/// Capacity grows to the next power of two (at least <see cref="MinimumCapacity"/>) and never shrinks, so the engine-side
/// instance count changes only a few times per run; the visible count is set separately.
/// </remarks>
public sealed class InstanceBuffer
{
    /// <summary>Floats per instance: 12 transform + 4 custom.</summary>
    public const int Stride = 16;

    /// <summary>Smallest capacity allocated.</summary>
    public const int MinimumCapacity = 16;

    private float[] _data = [];

    /// <summary>Instances the array has room for.</summary>
    public int Capacity { get; private set; }

    /// <summary>Instances written since the last <see cref="Reset"/>.</summary>
    public int Count { get; private set; }

    /// <summary>The raw array (length <see cref="Capacity"/> × <see cref="Stride"/>); entries past <see cref="Count"/> are stale.</summary>
    public float[] Data => _data;

    /// <summary>The capacity <see cref="Reserve"/> would allocate for <paramref name="needed"/> instances.</summary>
    public static int CapacityFor(int needed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(needed);
        var capacity = MinimumCapacity;
        while (capacity < needed)
        {
            capacity = checked(capacity * 2);
        }

        return capacity;
    }

    /// <summary>Starts writing from instance 0 and makes room for <paramref name="needed"/> instances. Returns true when the capacity changed.</summary>
    public bool Reserve(int needed)
    {
        Count = 0;
        if (needed <= Capacity)
        {
            return false;
        }

        Capacity = CapacityFor(needed);
        _data = new float[Capacity * Stride];
        return true;
    }

    /// <summary>Forgets the written instances (keeps the array).</summary>
    public void Reset() => Count = 0;

    /// <summary>Appends one instance.</summary>
    public void Add(in AffineRows transform, Vector4 custom)
    {
        if (Count >= Capacity)
        {
            throw new InvalidOperationException($"Instance buffer is full ({Capacity}); call Reserve first.");
        }

        Write(_data, Count, transform, custom);
        Count++;
    }

    /// <summary>Writes instance <paramref name="index"/> of <paramref name="buffer"/>.</summary>
    public static void Write(Span<float> buffer, int index, in AffineRows transform, Vector4 custom)
    {
        var s = buffer.Slice(index * Stride, Stride);
        s[0] = transform.Row0.X;
        s[1] = transform.Row0.Y;
        s[2] = transform.Row0.Z;
        s[3] = transform.Row0.W;
        s[4] = transform.Row1.X;
        s[5] = transform.Row1.Y;
        s[6] = transform.Row1.Z;
        s[7] = transform.Row1.W;
        s[8] = transform.Row2.X;
        s[9] = transform.Row2.Y;
        s[10] = transform.Row2.Z;
        s[11] = transform.Row2.W;
        s[12] = custom.X;
        s[13] = custom.Y;
        s[14] = custom.Z;
        s[15] = custom.W;
    }

    /// <summary>
    /// The custom vector of a particle instance: x = part material alpha (1 − t), y = 1 when the part material replaces the
    /// surface alpha (<see cref="PartTranslucencyRules.HasPartMaterial"/>), else 0; z = the caller's per-object tag
    /// (<see cref="ParticleDraw.Tag"/>: the world's EnvCell statics carry their cell's draw-state slot, so their particles
    /// get the cell's depth rules; 0 = none). Exact as a float up to 2^24.
    /// </summary>
    public static Vector4 ParticleCustom(float alpha, bool partMaterial, int tag = 0) => new(alpha, partMaterial ? 1f : 0f, tag, 0f);
}
