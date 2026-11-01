using System.Globalization;
using System.Numerics;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// M5 7 P7i: costly floor patches of a <see cref="RoomGraph"/> (spots where walks stuck, and a ground's seeded avoid regions, D144): a step of a plan that passes through a patch costs the patch's metres more, so the graph prefers a
/// way round it while one exists and still plans through it when none does (a patch is never a wall: a mage standing in one must be able to walk out). A learned patch (a disc) lasts until its own time; a seeded one (a box) for good;
/// <see cref="Expire"/> drops the old ones. The runner's stuck ladder keeps its recovery moves out of every patch (<see cref="Contains"/>). Landblock-local points, as the graph's. Not retail: Lifestone's own.
/// </summary>
public sealed class RoomPatches
{
    /// <summary>What a plan's step through a learned patch costs beyond its length, in metres. Lifestone's own: more than a detour round a den room or two.</summary>
    public const float CostMetres = 40f;

    /// <summary>What a plan's step through a seeded avoid region costs beyond its length, in metres (D144: a no-go in all but name; a way through stays when no other exists). Lifestone's own.</summary>
    public const float SeededCostMetres = 400f;

    /// <summary>A patch counts for steps within this height of it (a storey above or below passes free).</summary>
    public const float StoreyMetres = 3f;

    private const float SampleMetres = 0.5f;

    private readonly List<Patch> _patches = [];

    /// <summary>The patches in force.</summary>
    public int Count => _patches.Count;

    /// <summary>Adds a disc of <paramref name="radius"/> metres round <paramref name="centre"/> (landblock-local) until <paramref name="until"/> (the caller's clock), costing <see cref="CostMetres"/>.</summary>
    public void Add(Vector3 centre, float radius, double until) => _patches.Add(new Patch(centre, radius, Vector3.Zero, Vector3.Zero, until, CostMetres, null));

    /// <summary>Adds a seeded box from <paramref name="min"/> to <paramref name="max"/> (landblock-local; the height its storey) for good, costing <see cref="SeededCostMetres"/>, named <paramref name="name"/>.</summary>
    /// <returns>False when a box of that name is already seeded (a graph shared by several walks is seeded once).</returns>
    public bool AddBox(Vector3 min, Vector3 max, string name)
    {
        if (_patches.Exists(p => p.Name == name))
        {
            return false;
        }

        _patches.Add(new Patch(Vector3.Zero, 0f, Vector3.Min(min, max), Vector3.Max(min, max), double.PositiveInfinity, SeededCostMetres, name));
        return true;
    }

    /// <summary>Drops the patches whose time is over at <paramref name="now"/>; the number dropped.</summary>
    public int Expire(double now) => _patches.RemoveAll(p => p.Until <= now);

    /// <summary>The patches as <c>[x y] r=.. until=..</c> or <c>name [x0..x1 y0..y1]</c>, for the log.</summary>
    public override string ToString() => string.Join("; ", _patches.Select(p => p.ToString()));

    /// <summary>The name of the patch holding <paramref name="point"/> (landblock-local), or null when none does.</summary>
    public string? Contains(Vector3 point)
    {
        foreach (var p in _patches)
        {
            if (p.Holds(point))
            {
                return p.ToString();
            }
        }

        return null;
    }

    /// <summary>The extra cost of the step from <paramref name="a"/> to <paramref name="b"/>: each patch's metres when the step passes through it.</summary>
    public float Cost(Vector3 a, Vector3 b)
    {
        var cost = 0f;
        foreach (var p in _patches)
        {
            if (p.Meets(a, b))
            {
                cost += p.Cost;
            }
        }

        return cost;
    }

    private readonly record struct Patch(Vector3 Centre, float Radius, Vector3 Min, Vector3 Max, double Until, float Cost, string? Name)
    {
        private bool IsBox => Name is not null;

        public bool Holds(Vector3 point) => IsBox
            ? point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y && point.Z >= Min.Z - StoreyMetres && point.Z <= Max.Z + StoreyMetres
            : MathF.Abs(point.Z - Centre.Z) <= StoreyMetres && Vector2.Distance(new Vector2(point.X, point.Y), new Vector2(Centre.X, Centre.Y)) <= Radius;

        public bool Meets(Vector3 a, Vector3 b)
        {
            if (!IsBox)
            {
                return (MathF.Abs(a.Z - Centre.Z) <= StoreyMetres || MathF.Abs(b.Z - Centre.Z) <= StoreyMetres)
                    && SegmentDistance(new Vector2(Centre.X, Centre.Y), new Vector2(a.X, a.Y), new Vector2(b.X, b.Y)) <= Radius;
            }

            // A box: the step sampled every half metre (a den step is a few metres; allocates nothing).
            var steps = Math.Max(1, (int)MathF.Ceiling(Vector3.Distance(a, b) / SampleMetres));
            for (var i = 0; i <= steps; i++)
            {
                if (Holds(Vector3.Lerp(a, b, (float)i / steps)))
                {
                    return true;
                }
            }

            return false;
        }

        public override string ToString() => IsBox
            ? string.Create(CultureInfo.InvariantCulture, $"{Name} [{Min.X:0.#}..{Max.X:0.#} {Min.Y:0.#}..{Max.Y:0.#}]")
            : string.Create(CultureInfo.InvariantCulture, $"[{Centre.X:0.#} {Centre.Y:0.#}] r={Radius:0.#} until={Until:0}");
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared();
        var t = len2 <= 0f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + (ab * t));
    }
}
