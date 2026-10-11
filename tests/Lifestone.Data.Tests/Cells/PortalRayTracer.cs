using System.Numerics;
using Lifestone.Data.World.Cells;

namespace Lifestone.Data.Tests.Cells;

/// <summary>
/// Completeness oracle for <see cref="PortalVisibility.FromInside"/> that shares none of its clipping or cone code.
/// Rays from the eye travel through a cell until they hit one of its drawing polygons. A portal polygon passes the ray
/// on to the neighbour, and any other polygon stops it. Every cell a ray enters must be in the walk's result.
/// </summary>
/// <remarks>
/// <para>The ray applies the same retail rules as the walk, so a ray reaches only what retail could draw:</para>
/// <list type="bullet">
/// <item>It stays inside the viewer's PVS (<c>add_views</c>).</item>
/// <item>It passes a portal only when the eye is on the cell's interior side of the portal plane (<c>PView::InitCell</c>).</item>
/// <item>For a portal without ExactMatch, the hit point must also lie in the polygon of the neighbour's portal
/// <c>other_portal_id</c> (<c>PView::OtherPortalClip</c>).</item>
/// </list>
/// <para>Rules that make the oracle weaker, never stricter:</para>
/// <list type="bullet">
/// <item>A ray passes a portal (and the neighbour's polygon) only at least <see cref="PortalInset"/> inside it, so a
/// ray grazing an edge, where the visible part of the portal has no area, proves nothing. Walls block with a
/// 10 micrometre margin outside.</item>
/// <item>A wall coplanar with a portal at the same hit distance wins.</item>
/// <item>Several portals of a cell can name the same polygon (a big floor or ceiling opening shared by several
/// neighbours); the ray takes the first of them that it can pass.</item>
/// </list>
/// </remarks>
internal sealed class PortalRayTracer(LandblockCellSet cells)
{
    /// <summary>How far inside a portal polygon (metres) a ray must hit it to pass.</summary>
    public const float PortalInset = 1e-4f;

    private const float StepEpsilon = 1e-4f;
    private const float TieEpsilon = 1e-5f;
    private readonly Dictionary<uint, CellPolygons> _polygons = new();

    /// <summary>The cells the rays enter, and whether a ray leaves through an outside portal.</summary>
    public (HashSet<uint> Reached, bool Outside) Trace(EnvCellData viewer, Vector3 eye, IEnumerable<Vector3> directions)
    {
        var pvs = PortalVisibility.PotentiallyVisibleSet(viewer).ToHashSet();
        var reached = new HashSet<uint>();
        var outside = false;
        var path = new List<RayHop>();
        foreach (var direction in directions)
        {
            path.Clear();
            Follow(viewer, pvs, eye, direction, path);
            foreach (var hop in path)
            {
                if (hop.To == CellIds.Outside)
                {
                    outside = true;
                }
                else
                {
                    reached.Add(hop.To);
                }
            }
        }

        return (reached, outside);
    }

    /// <summary>The portals one ray passes, in order (the last hop enters <see cref="CellIds.Outside"/> when it leaves).</summary>
    public IReadOnlyList<RayHop> Path(EnvCellData viewer, Vector3 eye, Vector3 direction)
    {
        var path = new List<RayHop>();
        Follow(viewer, PortalVisibility.PotentiallyVisibleSet(viewer).ToHashSet(), eye, direction, path);
        return path;
    }

    private void Follow(EnvCellData viewer, HashSet<uint> pvs, Vector3 eye, Vector3 direction, List<RayHop> path)
    {
        var current = viewer;
        var entryPolygon = -1;
        var tMin = 0f;
        for (var step = 0; step < 256; step++)
        {
            RayPolygon? best = null;
            var bestT = float.MaxValue;
            foreach (var polygon in Polygons(current).All)
            {
                if (polygon.Id == entryPolygon)
                {
                    continue; // the ray just came through this plane
                }

                var denominator = Vector3.Dot(polygon.Plane.Normal, direction);
                if (MathF.Abs(denominator) < 1e-6f)
                {
                    continue;
                }

                var t = -polygon.Plane.Distance(eye) / denominator;
                if (t <= tMin + StepEpsilon || t > bestT + TieEpsilon || !polygon.Contains(eye + (direction * t)))
                {
                    continue;
                }

                if (best is null || t < bestT - TieEpsilon)
                {
                    best = polygon;
                    bestT = t;
                }
                else if (best.Portals.Length > 0 && polygon.Portals.Length == 0)
                {
                    // Within the tie distance a wall wins over a portal.
                    best = polygon;
                    bestT = MathF.Min(t, bestT);
                }
            }

            var hit = eye + (direction * bestT);
            if (best is null || best.Portals.Length == 0 || !best.ContainsInside(hit, PortalInset))
            {
                return; // a wall, nothing (a gap in the room's polygons), or a portal's very edge
            }

            var passed = false;
            foreach (var index in best.Portals)
            {
                var portal = current.Portals[index];
                var interior = portal.PortalSide ? PlaneSide.Negative : PlaneSide.Positive;
                if (best.Plane.SideOf(eye) != interior)
                {
                    continue; // retail never looks through this portal from where the eye is
                }

                if (portal.LeadsOutside)
                {
                    path.Add(new RayHop(current.Id, index, CellIds.Outside, bestT));
                    return;
                }

                var next = cells.GetCell(portal.OtherCellId);
                if (next is null || !pvs.Contains(next.Id))
                {
                    continue;
                }

                if (!portal.ExactMatch && portal.OtherPortalIndex >= 0 && portal.OtherPortalIndex < next.Portals.Count
                    && Polygons(next).ById.TryGetValue(next.Portals[portal.OtherPortalIndex].PolygonId, out var back)
                    && !back.ContainsInside(hit, PortalInset))
                {
                    continue;
                }

                path.Add(new RayHop(current.Id, index, next.Id, bestT));
                entryPolygon = portal.OtherPortalIndex >= 0 && portal.OtherPortalIndex < next.Portals.Count
                    ? next.Portals[portal.OtherPortalIndex].PolygonId
                    : -1;
                current = next;
                tMin = bestT;
                passed = true;
                break;
            }

            if (!passed)
            {
                return;
            }
        }
    }

    /// <summary>Directions spread evenly over the whole sphere (Fibonacci lattice, rotated by <paramref name="rng"/>).</summary>
    public static IEnumerable<Vector3> Sphere(int count, Random rng)
    {
        var offset = (float)rng.NextDouble();
        var golden = MathF.PI * (3f - MathF.Sqrt(5f));
        var spin = (float)(rng.NextDouble() * MathF.Tau);
        for (var i = 0; i < count; i++)
        {
            var z = 1f - (2f * (i + offset) / count);
            var r = MathF.Sqrt(MathF.Max(0f, 1f - (z * z)));
            var phi = spin + (golden * i);
            yield return new Vector3(r * MathF.Cos(phi), r * MathF.Sin(phi), z);
        }
    }

    /// <summary>Directions inside a camera frustum, spread over its image plane.</summary>
    public static IEnumerable<Vector3> InFrustum(CameraFrustum frustum, int count, Random rng)
    {
        var extent = MathF.Tan(frustum.HalfAngle) * 0.999f;
        for (var i = 0; i < count; i++)
        {
            var u = ((float)rng.NextDouble() * 2f) - 1f;
            var v = ((float)rng.NextDouble() * 2f) - 1f;
            yield return Vector3.Normalize(frustum.Forward + (frustum.Side * (u * extent)) + (frustum.Up * (v * extent)));
        }
    }

    private CellPolygons Polygons(EnvCellData cell)
    {
        if (_polygons.TryGetValue(cell.Id, out var cached))
        {
            return cached;
        }

        var structure = cells.GetStructure(cell);
        var portalsByPolygon = new Dictionary<ushort, List<int>>();
        for (var i = 0; i < cell.Portals.Count; i++)
        {
            if (!portalsByPolygon.TryGetValue(cell.Portals[i].PolygonId, out var list))
            {
                portalsByPolygon.Add(cell.Portals[i].PolygonId, list = new List<int>());
            }

            list.Add(i);
        }

        var all = new List<RayPolygon>();
        foreach (var polygon in structure.Polygons)
        {
            var vertices = polygon.VertexIndices.Select(i => cell.Frame.LocalToParent(structure.Positions[i])).ToArray();
            if (!CellPlane.TryFromPolygon(vertices, out var plane))
            {
                continue;
            }

            var portals = portalsByPolygon.TryGetValue(polygon.Id, out var indices) ? indices.ToArray() : [];
            all.Add(new RayPolygon(polygon.Id, vertices, plane, portals));
        }

        cached = new CellPolygons(all.ToArray(), all.ToDictionary(p => (ushort)p.Id));
        _polygons.Add(cell.Id, cached);
        return cached;
    }

    private sealed record CellPolygons(RayPolygon[] All, Dictionary<ushort, RayPolygon> ById);

    /// <summary>A drawing polygon in landblock space and the indices of the cell portals that name it.</summary>
    private sealed record RayPolygon(int Id, Vector3[] Vertices, CellPlane Plane, int[] Portals)
    {
        /// <summary>Point in the (convex, fan-triangulated) polygon, with a 10 micrometre margin.</summary>
        public bool Contains(Vector3 point)
        {
            for (var i = 1; i + 1 < Vertices.Length; i++)
            {
                if (InTriangle(Vertices[0], Vertices[i], Vertices[i + 1], point))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Point (projected onto the polygon's plane) in the polygon and at least <paramref name="inset"/> from
        /// each of its edges.</summary>
        public bool ContainsInside(Vector3 point, float inset)
        {
            point -= Plane.Normal * Plane.Distance(point);
            if (!Contains(point))
            {
                return false;
            }

            for (var i = 0; i < Vertices.Length; i++)
            {
                var a = Vertices[i];
                var ab = Vertices[(i + 1) % Vertices.Length] - a;
                var t = Math.Clamp(Vector3.Dot(point - a, ab) / MathF.Max(ab.LengthSquared(), 1e-12f), 0f, 1f);
                if (Vector3.Distance(point, a + (ab * t)) < inset)
                {
                    return false;
                }
            }

            return true;
        }

        private bool InTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 p)
        {
            const float margin = -1e-5f;
            var n = Plane.Normal;
            return Vector3.Dot(Vector3.Cross(b - a, p - a), n) >= margin * Vector3.Distance(a, b)
                && Vector3.Dot(Vector3.Cross(c - b, p - b), n) >= margin * Vector3.Distance(b, c)
                && Vector3.Dot(Vector3.Cross(a - c, p - c), n) >= margin * Vector3.Distance(c, a);
        }
    }
}

/// <summary>Compares one indoor portal walk with the ray oracle.</summary>
internal static class PortalWalkOracle
{
    /// <summary>Where a test stands in a cell: 1 m above its largest floor, else 0.1 m; null when neither is inside.</summary>
    public static Vector3? EyeIn(LandblockCellSet set, EnvCellData cell)
    {
        var floor = CellDiscovery.FloorPointInCell(set, cell);
        if (floor is null)
        {
            return null;
        }

        foreach (var height in new[] { 1f, 0.1f })
        {
            var eye = floor.Position + new Vector3(0, 0, height);
            if (set.Contains(cell, eye))
            {
                return eye;
            }
        }

        return null;
    }

    /// <summary>Walks from <paramref name="cell"/> and traces rays from the same eye.</summary>
    /// <param name="frustum">Optional camera frustum; rays are then shot inside it only.</param>
    public static OracleResult Check(LandblockCellSet set, PortalRayTracer tracer, EnvCellData cell, Vector3 eye, CameraFrustum? frustum, int rays, Random rng)
    {
        var walk = PortalVisibility.FromInside(set, cell.Id, eye, frustum?.Planes);
        var directions = frustum is null ? PortalRayTracer.Sphere(rays, rng) : PortalRayTracer.InFrustum(frustum, rays, rng);
        var (reached, outside) = tracer.Trace(cell, eye, directions);
        var found = walk.Cells.Select(c => c.Cell.Id).ToHashSet();
        var missed = reached.Where(id => !found.Contains(id)).Order().ToArray();
        return new OracleResult(walk, reached.Count, missed, outside, outside && !walk.OutsideVisible);
    }
}

/// <summary>One oracle comparison.</summary>
/// <param name="Walk">The walk's result.</param>
/// <param name="RayCells">Cells the rays entered.</param>
/// <param name="Missed">Cells a ray entered that the walk did not list.</param>
/// <param name="RaySawOutside">A ray left through an outside portal.</param>
/// <param name="MissedOutside">A ray left through an outside portal but the walk says the outside is not visible.</param>
internal sealed record OracleResult(CellVisibility Walk, int RayCells, uint[] Missed, bool RaySawOutside, bool MissedOutside);

/// <summary>One portal a ray passes: it leaves cell <paramref name="From"/> by portal <paramref name="PortalIndex"/> at
/// distance <paramref name="T"/> and enters <paramref name="To"/>.</summary>
internal readonly record struct RayHop(uint From, int PortalIndex, uint To, float T);

/// <summary>A symmetric camera frustum through the eye (four side planes, positive inside).</summary>
internal sealed record CameraFrustum(Vector3 Eye, Vector3 Forward, Vector3 Side, Vector3 Up, float HalfAngle)
{
    public IReadOnlyList<CellPlane> Planes =>
        new[] { Side, -Side, Up, -Up }
            .Select(axis => Vector3.Normalize((Forward * MathF.Sin(HalfAngle)) - (axis * MathF.Cos(HalfAngle))))
            .Select(n => new CellPlane(n, -Vector3.Dot(n, Eye)))
            .ToArray();

    /// <summary>A random view direction (pitch within about +-37 degrees) with a 0.6 rad half angle.</summary>
    public static CameraFrustum Random(Vector3 eye, Random rng)
    {
        var z = (float)((rng.NextDouble() * 1.2) - 0.6);
        var phi = (float)(rng.NextDouble() * Math.PI * 2);
        var r = MathF.Sqrt(1 - (z * z));
        var forward = new Vector3(r * MathF.Cos(phi), r * MathF.Sin(phi), z);
        var side = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        var up = Vector3.Cross(side, forward);
        return new CameraFrustum(eye, forward, side, up, 0.6f);
    }
}
