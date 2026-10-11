using System.Globalization;

namespace Lifestone.Client.Cinematic;

/// <summary>
/// A double-precision 3-vector for camera paths: AC axes (+X east, +Y north, +Z up), metres. Double so that offsets of a
/// few kilometres and their arc lengths keep sub-millimetre precision (paths are evaluated thousands of times per shot).
/// </summary>
public readonly record struct Vec3d(double X, double Y, double Z)
{
    public static Vec3d Zero => default;

    public static Vec3d operator +(Vec3d a, Vec3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vec3d operator -(Vec3d a, Vec3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vec3d operator *(Vec3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    public static Vec3d operator *(double s, Vec3d a) => new(a.X * s, a.Y * s, a.Z * s);

    public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

    public double HorizontalLength => Math.Sqrt((X * X) + (Y * Y));

    public static double Distance(Vec3d a, Vec3d b) => (a - b).Length;

    public static Vec3d Lerp(Vec3d a, Vec3d b, double t) => a + ((b - a) * t);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X:0.###}, {Y:0.###}, {Z:0.###})");
}
