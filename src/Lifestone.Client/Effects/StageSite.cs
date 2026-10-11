using System.Numerics;
using Lifestone.Client.Objects;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Effects;

/// <summary>
/// Where the spell viewer stands its caster and target: Holtburg (landblock 0xA9B4), in that landblock's local AC frame
/// (metres from its south-west corner, Z up). Engine-agnostic so the placement can be checked against the town's
/// buildings without Godot (<see cref="StaticClearance"/>, <c>StageSiteTests</c>).
/// </summary>
/// <remarks>
/// The caster stands on the open green in the south of the town and faces east; the drudge stands
/// <see cref="Distance"/> metres further east. The line between them runs 4 m south of the timbered house
/// (building 0x01000C17 at (107.5, 36), whose south wall lies on y = 25) and 3.8 m north of the lamp post
/// (Setup 0x020005D8 at (106, 17.2)); <c>StageSiteTests</c> pins at least <see cref="MinimumClearance"/> metres of
/// horizontal clearance from every drawn building and static below head height.
/// </remarks>
public static class StageSite
{
    /// <summary>Landblock the stage stands in.</summary>
    public static readonly LandblockCoord Landblock = LandblockCoord.Holtburg;

    /// <summary>Caster position (landblock-local x, y).</summary>
    public static readonly Vector2 CasterStand = new(93f, 21f);

    /// <summary>Compass heading the caster faces (90 = east); the target faces the opposite way.</summary>
    public const float Heading = 90f;

    /// <summary>Caster–target distance (the brief asks for 10–15 m; ACE projectile speeds make bolts take ~0.7 s).</summary>
    public const float Distance = 12f;

    /// <summary>Horizontal clearance the actors and the cast line must keep from buildings and statics.</summary>
    public const float MinimumClearance = 2f;

    /// <summary>Height above the ground below which geometry counts as in the way (roof eaves above it do not).</summary>
    public const float ObstacleHeight = 3.5f;

    /// <summary>Unit compass direction of <see cref="Heading"/> (x east, y north).</summary>
    public static Vector2 Direction
    {
        get
        {
            var radians = Heading * MathF.PI / 180f;
            return new Vector2(MathF.Sin(radians), MathF.Cos(radians));
        }
    }

    /// <summary>Target position (landblock-local x, y).</summary>
    public static Vector2 TargetStand => CasterStand + (Direction * Distance);
}

/// <summary>One drawn triangle of a building or static object, in the centre landblock's local AC frame.</summary>
/// <param name="ModelId">The LandBlockInfo object or building model (GfxObj or Setup).</param>
/// <param name="GfxObjId">The part's GfxObj.</param>
/// <param name="ModelOrigin">Where the model is placed (centre-landblock local).</param>
/// <param name="A">First vertex.</param>
/// <param name="B">Second vertex.</param>
/// <param name="C">Third vertex.</param>
public readonly record struct ObstacleTriangle(uint ModelId, uint GfxObjId, Vector3 ModelOrigin, Vector3 A, Vector3 B, Vector3 C);

/// <summary>The nearest obstacle found by <see cref="StaticClearance.Nearest"/>.</summary>
/// <param name="Distance">Horizontal distance (metres); 0 when the probe touches or crosses the obstacle.</param>
/// <param name="Triangle">The nearest triangle, or null when nothing lies in the height band.</param>
public readonly record struct ClearanceResult(float Distance, ObstacleTriangle? Triangle);

/// <summary>
/// The drawn geometry of the LandBlockInfo statics and buildings around a landblock, created and posed by the shared
/// object path the world and the viewer draw them with (<see cref="LandblockObjectSet"/>, <see cref="ObjectPlacement"/>:
/// Setup rest pose under the static's frame; level-0 meshes; building sub-meshes retail skips are left out,
/// <see cref="SurfaceDrawRules"/>), for horizontal clearance queries in a height band.
/// </summary>
public sealed class StaticClearance
{
    private readonly List<ObstacleTriangle> _triangles;

    private StaticClearance(List<ObstacleTriangle> triangles) => _triangles = triangles;

    /// <summary>Every drawn triangle.</summary>
    public IReadOnlyList<ObstacleTriangle> Triangles => _triangles;

    /// <summary>
    /// Loads the statics and buildings of the <paramref name="radius"/>-ring of landblocks around
    /// <paramref name="center"/>, in <paramref name="center"/>'s local frame.
    /// </summary>
    public static StaticClearance Load(DatSet dats, LandblockCoord center, GfxObjMeshCache meshes, int radius = 1)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(meshes);
        var library = new ObjectModelLibrary(dats, meshes);
        var triangles = new List<ObstacleTriangle>();
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (center.Offset(dx, dy) is not { } coord || LandblockStatics.Read(dats, coord) is not { } statics)
                {
                    continue;
                }

                // The objects the world (and the viewer) creates and poses, through the shared object path
                // (LandblockObjectSet → ObjectPlacement): their level-0 meshes under their draw rules.
                var shift = new Vector3(dx * TerrainConstants.LandblockSize, dy * TerrainConstants.LandblockSize, 0f);
                var set = LandblockObjectSet.Build(coord, statics, null, library, WorldObjectKinds.Statics | WorldObjectKinds.Buildings);
                AddObjects(dats, meshes, set, shift, triangles);
            }
        }

        return new StaticClearance(triangles);
    }

    /// <summary>
    /// The nearest obstacle to the horizontal segment <paramref name="a"/>–<paramref name="b"/> (a point when they are
    /// equal), counting only the parts of triangles between heights <paramref name="zMin"/> and <paramref name="zMax"/>.
    /// </summary>
    public ClearanceResult Nearest(Vector2 a, Vector2 b, float zMin, float zMax)
    {
        var best = new ClearanceResult(float.PositiveInfinity, null);
        Span<Vector3> clipped = stackalloc Vector3[8];
        Span<Vector3> scratch = stackalloc Vector3[8];
        Span<Vector2> polygon = stackalloc Vector2[8];
        foreach (var t in _triangles)
        {
            var count = ClipToBand(t, zMin, zMax, clipped, scratch);
            if (count == 0)
            {
                continue;
            }

            for (var i = 0; i < count; i++)
            {
                polygon[i] = new Vector2(clipped[i].X, clipped[i].Y);
            }

            var d = SegmentPolygonDistance(a, b, polygon[..count]);
            if (d < best.Distance)
            {
                best = new ClearanceResult(d, t);
            }
        }

        return best;
    }

    /// <summary>Horizontal distance between a segment and a convex polygon (0 when they overlap).</summary>
    public static float SegmentPolygonDistance(Vector2 a, Vector2 b, ReadOnlySpan<Vector2> polygon)
    {
        if (polygon.Length == 0)
        {
            return float.PositiveInfinity;
        }

        if (polygon.Length == 1)
        {
            return PointSegmentDistance(polygon[0], a, b);
        }

        if (polygon.Length >= 3 && (InsideConvex(a, polygon) || InsideConvex(b, polygon)))
        {
            return 0f;
        }

        var best = float.PositiveInfinity;
        for (var i = 0; i < polygon.Length; i++)
        {
            best = MathF.Min(best, SegmentSegmentDistance(a, b, polygon[i], polygon[(i + 1) % polygon.Length]));
        }

        return best;
    }

    /// <summary>Distance from <paramref name="p"/> to the segment <paramref name="a"/>–<paramref name="b"/>.</summary>
    public static float PointSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        var t = lengthSquared <= 0f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(p, a + (ab * t));
    }

    /// <summary>Distance between two segments (0 when they cross).</summary>
    public static float SegmentSegmentDistance(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        if (Cross(b - a, c - a) * Cross(b - a, d - a) < 0f && Cross(d - c, a - c) * Cross(d - c, b - c) < 0f)
        {
            return 0f;
        }

        return MathF.Min(
            MathF.Min(PointSegmentDistance(a, c, d), PointSegmentDistance(b, c, d)),
            MathF.Min(PointSegmentDistance(c, a, b), PointSegmentDistance(d, a, b)));
    }

    private static float Cross(Vector2 u, Vector2 v) => (u.X * v.Y) - (u.Y * v.X);

    private static bool InsideConvex(Vector2 p, ReadOnlySpan<Vector2> polygon)
    {
        var sign = 0f;
        for (var i = 0; i < polygon.Length; i++)
        {
            var c = Cross(polygon[(i + 1) % polygon.Length] - polygon[i], p - polygon[i]);
            if (MathF.Abs(c) < 1e-6f)
            {
                continue;
            }

            if (sign == 0f)
            {
                sign = MathF.Sign(c);
            }
            else if (MathF.Sign(c) != sign)
            {
                return false;
            }
        }

        // A degenerate (edge-on) polygon has no inside; its edges are measured by the caller.
        return sign != 0f;
    }

    /// <summary>Sutherland–Hodgman clip of a triangle to zMin ≤ z ≤ zMax; returns the vertex count (0 when outside).</summary>
    private static int ClipToBand(in ObstacleTriangle t, float zMin, float zMax, Span<Vector3> output, Span<Vector3> scratch)
    {
        scratch[0] = t.A;
        scratch[1] = t.B;
        scratch[2] = t.C;
        var count = ClipPlane(scratch, 3, output, zMin, keepAbove: true);
        if (count == 0)
        {
            return 0;
        }

        output[..count].CopyTo(scratch);
        return ClipPlane(scratch, count, output, zMax, keepAbove: false);
    }

    private static int ClipPlane(ReadOnlySpan<Vector3> input, int count, Span<Vector3> output, float z, bool keepAbove)
    {
        var n = 0;
        for (var i = 0; i < count; i++)
        {
            var p = input[i];
            var q = input[(i + 1) % count];
            var pIn = keepAbove ? p.Z >= z : p.Z <= z;
            var qIn = keepAbove ? q.Z >= z : q.Z <= z;
            if (pIn)
            {
                output[n++] = p;
            }

            if (pIn != qIn)
            {
                var s = (z - p.Z) / (q.Z - p.Z);
                output[n++] = p + ((q - p) * s);
            }
        }

        return n;
    }

    private static void AddObjects(DatSet dats, GfxObjMeshCache meshes, LandblockObjectSet set, Vector3 shift, List<ObstacleTriangle> triangles)
    {
        foreach (var obj in set.Objects)
        {
            var context = obj.Kind == WorldObjectKind.Building ? MeshDrawContext.Building : MeshDrawContext.Object;
            for (var p = obj.FirstPart; p < obj.FirstPart + obj.PartCount; p++)
            {
                var part = set.Parts[p];
                var gfxObjId = part.Chain.NearestGfxObjId;
                if (meshes.Get(gfxObjId) is not { } gfxObj)
                {
                    continue;
                }

                var drawn = SurfaceDrawRules.DrawnSubMeshes(dats, gfxObj.Mesh, context);
                for (var s = 0; s < gfxObj.Mesh.SubMeshes.Count; s++)
                {
                    if (!drawn[s])
                    {
                        continue;
                    }

                    var sub = gfxObj.Mesh.SubMeshes[s];
                    for (var k = 0; k + 2 < sub.Indices.Length; k += 3)
                    {
                        triangles.Add(new ObstacleTriangle(
                            obj.ModelId,
                            gfxObjId,
                            obj.Origin + shift,
                            Place(sub.Positions[sub.Indices[k]]),
                            Place(sub.Positions[sub.Indices[k + 1]]),
                            Place(sub.Positions[sub.Indices[k + 2]])));
                    }
                }

                // The part frame of the shared posing rule, gfxobj_scale applied first.
                Vector3 Place(Vector3 v) => part.Origin + Vector3.Transform(v * part.Scale, part.Rotation) + shift;
            }
        }
    }
}
