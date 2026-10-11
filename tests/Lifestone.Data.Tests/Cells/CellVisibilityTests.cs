using System.Numerics;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;

namespace Lifestone.Data.Tests.Cells;

public sealed class CellVisibilityTests(CellsFixture fixture) : IClassFixture<CellsFixture>
{
    private const uint InnEntry = 0xA9B40100u;

    [RequiresDatsFact]
    public void Inside_the_Holtburg_inn_the_walk_stays_in_the_PVS_and_sees_outside()
    {
        var set = LandblockCellSet.Load(fixture.Structures, LandblockCoord.Holtburg);
        var cell = set.GetCell(InnEntry)!;
        var eye = EyeInCell(set, cell);

        var result = PortalVisibility.FromInside(set, InnEntry, eye);
        var pvs = PortalVisibility.PotentiallyVisibleSet(cell).ToHashSet();
        Assert.Equal(17, pvs.Count);
        Assert.Equal(InnEntry, result.Cells[0].Cell.Id);
        Assert.All(result.Cells, c => Assert.Contains(c.Cell.Id, pvs));
        Assert.All(result.Cells, c => Assert.NotEmpty(c.Views));
        Assert.True(result.OutsideVisible, "the inn's entry room has three doors to the outside");
        Assert.False(result.Truncated);

        // Every neighbour through a portal the eye looks out of is reached (no frustum: all directions).
        foreach (var portal in cell.Portals.Where(p => !p.LeadsOutside))
        {
            var plane = set.GetStructure(cell).GetPortal(portal.PolygonId)!.Plane.ToParent(cell.Frame);
            var interior = portal.PortalSide ? PlaneSide.Negative : PlaneSide.Positive;
            if (plane.SideOf(eye) == interior)
            {
                Assert.True(result.Contains(portal.OtherCellId), $"neighbour 0x{portal.OtherCellId:X8} through a portal facing the eye");
            }
        }
    }

    [RequiresDatsFact]
    public void A_frustum_looking_at_a_wall_sees_only_the_viewer_cell()
    {
        var set = LandblockCellSet.Load(fixture.Structures, new LandblockCoord(0x86, 0x03));
        var cell = set.GetCell(0x860301ADu)!;
        var eye = new Vector3(12.3199f, -28.482f, 1.6f);
        Assert.True(set.Contains(cell, eye));

        var all = PortalVisibility.FromInside(set, cell.Id, eye);
        Assert.False(all.OutsideVisible, "a dungeon never sees the landscape");
        Assert.True(all.Cells.Count > 1);

        // A tiny frustum pointing straight down at the floor: no portal is in it.
        var down = NarrowFrustum(eye, -Vector3.UnitZ, halfAngle: 0.05f);
        var looking = PortalVisibility.FromInside(set, cell.Id, eye, down);
        Assert.Single(looking.Cells);
        Assert.Equal(cell.Id, looking.Cells[0].Cell.Id);
    }

    [RequiresDatsFact]
    public void Looking_into_the_inn_from_outside_enters_through_a_doorway()
    {
        var set = LandblockCellSet.Load(fixture.Structures, LandblockCoord.Holtburg);
        var inn = LandblockStatics.Read(fixture.Dats, LandblockCoord.Holtburg)!.Buildings[0];
        var geometry = BuildingPortalGeometry.Load(fixture.Dats, inn);
        Assert.Equal(inn.Portals.Count, geometry.Doorways.Count);

        var door = geometry.Doorways[0];
        var centre = door.Vertices.Aggregate(Vector3.Zero, (s, v) => s + v) / door.Vertices.Length;
        var outwards = door.Portal.PortalSide ? -door.Plane.Normal : door.Plane.Normal;
        var eye = centre + (outwards * 3f) + new Vector3(0, 0, 0.5f);
        Assert.True(door.CanLookIn(eye));
        Assert.False(door.CanLookIn(centre - (outwards * 1f)), "from inside the doorway the building portal is not looked through");

        var result = PortalVisibility.FromOutside(set, geometry, eye);
        Assert.True(result.Contains(door.Portal.OtherCellId));
        Assert.False(result.OutsideVisible, "the outdoor view never re-adds the landscape");
        var stab = inn.Portals.SelectMany(p => p.StabList).ToHashSet();
        Assert.All(result.Cells, c => Assert.Contains(c.Cell.Id, stab));

        // Each doorway coincides with its entry cell's outside portal.
        foreach (var doorway in geometry.Doorways)
        {
            var cell = set.GetCell(doorway.Portal.OtherCellId)!;
            var portal = cell.Portals[doorway.Portal.OtherPortalIndex];
            Assert.True(portal.LeadsOutside);
            var cellPoly = set.GetStructure(cell).GetPortal(portal.PolygonId)!.Vertices.Select(cell.Frame.LocalToParent).ToArray();
            Assert.True(CellGeometryMath.SetDistance(cellPoly, doorway.Vertices) < 0.01f);
        }
    }

    /// <summary>
    /// Viewpoints where an earlier walk lost cells: clipping produced near-duplicate vertices, a cone plane built from
    /// that tiny edge cut the next portal away, and every cell behind it was dropped (e.g. 0x5E440318 lost 0x5E4402FB
    /// behind 0x5E440313 portal 1 and 0x5E440314 portal 0). The ray oracle must find nothing the walk misses.
    /// </summary>
    [RequiresDatsTheory]
    [InlineData(0x5E440318u, false)]
    [InlineData(0x800301CCu, false)]
    [InlineData(0x03C4012Bu, false)]
    [InlineData(0x6043033Eu, false)]
    [InlineData(0x01690184u, false)]
    [InlineData(0xF81E0128u, false)]
    [InlineData(0x5052012Bu, true)]
    [InlineData(0x5256012Bu, true)]
    [InlineData(0x5477012Bu, true)]
    public void Walks_from_once_lossy_viewpoints_reach_every_cell_a_ray_reaches(uint viewerId, bool withFrustum)
    {
        var set = LandblockCellSet.Load(fixture.Structures, LandblockCoord.FromId(viewerId));
        var cell = set.GetCell(viewerId)!;
        var eye = PortalWalkOracle.EyeIn(set, cell) ?? throw new InvalidOperationException("no eye point");
        var frustum = withFrustum ? CameraFrustum.Random(eye, new Random((int)viewerId ^ 0x5151)) : null;
        var result = PortalWalkOracle.Check(set, new PortalRayTracer(set), cell, eye, frustum, rays: 20_000, new Random((int)viewerId));
        Assert.True(result.RayCells > 0, "the rays should enter some cell");
        Assert.True(result.Missed.Length == 0, $"walk missed {string.Join(" ", result.Missed.Select(id => $"0x{id:X8}"))}");
        Assert.False(result.MissedOutside);
    }

    [RequiresDatsFact]
    public void Walk_from_0x5E440318_reaches_0x5E4402FB()
    {
        var set = LandblockCellSet.Load(fixture.Structures, LandblockCoord.FromId(0x5E440318u));
        var cell = set.GetCell(0x5E440318u)!;
        var eye = PortalWalkOracle.EyeIn(set, cell)!.Value;
        Assert.True(PortalVisibility.FromInside(set, cell.Id, eye).Contains(0x5E4402FBu));
    }

    [Fact]
    public void Clipping_and_cones_behave()
    {
        var square = new[] { new Vector3(-1, -1, 5), new Vector3(1, -1, 5), new Vector3(1, 1, 5), new Vector3(-1, 1, 5) };
        var cone = ViewVolume.Through(Vector3.Zero, square);
        Assert.Equal(4, cone.Planes.Count);
        Assert.True(cone.Contains(new Vector3(0, 0, 10)));
        Assert.False(cone.Contains(new Vector3(3, 0, 10)));
        Assert.True(cone.IntersectsSphere(new Vector3(3, 0, 10), 2f));

        var bigger = square.Select(v => new Vector3(v.X * 3, v.Y * 3, 10)).ToArray();
        var clipped = ViewVolume.Clip(bigger, cone);
        Assert.Equal(4, clipped.Length);
        Assert.All(clipped, v => Assert.InRange(MathF.Abs(v.X), 0f, 2.0001f));

        var outside = square.Select(v => v + new Vector3(10, 0, 0)).ToArray();
        Assert.Empty(ViewVolume.Clip(outside, cone));
        Assert.Equal(square.Length, ViewVolume.Clip(square, new ViewVolume([])).Length);
    }

    [Fact]
    public void A_view_through_a_portal_keeps_every_ray_through_the_visible_part()
    {
        // Second portals whose corners sit within 10 micrometres of the first view's planes: clipping them makes
        // near-duplicate corners, the case that once produced cone planes of rounding noise.
        var rng = new Random(7);
        var eye = new Vector3(101.234f, -57.678f, 12.9f);
        var checkedRays = 0;
        for (var trial = 0; trial < 3000; trial++)
        {
            var d1 = 1f + (rng.NextSingle() * 5f);
            var a = Quad(eye + new Vector3((rng.NextSingle() - 0.5f) * 2f, d1, (rng.NextSingle() - 0.5f) * 2f), 0.3f + rng.NextSingle(), 0.3f + rng.NextSingle(), rng.Next(2) == 0);
            var viewA = ViewVolume.ThroughPortal(eye, a, new ViewVolume([]));
            Assert.NotNull(viewA);
            Assert.All(viewA.Planes, p => Assert.True(MathF.Abs(p.Distance(eye)) < 1e-4f));

            // The second portal: the first one pushed farther away, shifted sideways by a whole number of corners'
            // worth of edge, and jittered.
            var scale = (d1 + 0.5f + (rng.NextSingle() * 20f)) / d1;
            var shift = rng.Next(3) switch
            {
                0 => Vector3.Zero,
                1 => (a[1] - a[0]) * scale * 0.5f,
                _ => (a[3] - a[0]) * scale * 0.5f,
            };
            var b = a.Select(v => eye + ((v - eye) * scale) + shift + Jitter(rng, 1e-5f)).ToArray();
            var viewB = ViewVolume.ThroughPortal(eye, b, viewA);
            Assert.NotNull(viewB);
            foreach (var plane in viewB.Planes)
            {
                Assert.True(MathF.Abs(plane.Distance(eye)) < 1e-4f, "every cone plane passes through the eye");
            }

            // Rays through points of b well inside viewA stay inside viewB, before and beyond b.
            for (var k = 0; k < 20; k++)
            {
                var u = rng.NextSingle();
                var v = rng.NextSingle();
                var p = b[0] + ((b[1] - b[0]) * u) + ((b[3] - b[0]) * v);
                if (viewA.Planes.Any(plane => plane.Distance(p) < 1e-3f))
                {
                    continue;
                }

                checkedRays++;
                foreach (var t in new[] { 0.25f, 1f, 4f })
                {
                    var q = eye + ((p - eye) * t);
                    Assert.All(viewB.Planes, plane => Assert.True(plane.Distance(q) >= -1e-4f * t, $"trial {trial}: ray point cut by {plane.Distance(q)}"));
                }
            }
        }

        Assert.True(checkedRays > 10_000, $"only {checkedRays} rays checked");
    }

    [Fact]
    public void A_portal_that_only_touches_the_view_is_not_looked_through()
    {
        var eye = Vector3.Zero;
        var a = Quad(new Vector3(0, 5, 0), 1, 1, clockwise: false);
        var viewA = ViewVolume.ThroughPortal(eye, a, new ViewVolume([]))!;

        // Behind a, touching the view's x = +y/5 plane along its left edge only.
        var touching = Quad(new Vector3(3, 10, 0), 1, 1, clockwise: true);
        Assert.Null(ViewVolume.ThroughPortal(eye, touching, viewA));

        // Overlapping it by 1 cm: seen, and bounded by viewA's own plane on that side.
        var overlapping = touching.Select(v => v - new Vector3(0.01f, 0, 0)).ToArray();
        var viewB = ViewVolume.ThroughPortal(eye, overlapping, viewA);
        Assert.NotNull(viewB);
        Assert.Contains(viewB.Planes, p => viewA.Planes.Contains(p));
    }

    private static Vector3[] Quad(Vector3 centre, float halfWidth, float halfHeight, bool clockwise)
    {
        // In the plane y = centre.Y, facing -y (towards an eye in front) unless clockwise.
        var corners = new[]
        {
            centre + new Vector3(-halfWidth, 0, -halfHeight),
            centre + new Vector3(halfWidth, 0, -halfHeight),
            centre + new Vector3(halfWidth, 0, halfHeight),
            centre + new Vector3(-halfWidth, 0, halfHeight),
        };
        return clockwise ? corners.Reverse().ToArray() : corners;
    }

    private static Vector3 Jitter(Random rng, float size) =>
        new Vector3(rng.NextSingle() - 0.5f, rng.NextSingle() - 0.5f, rng.NextSingle() - 0.5f) * (2f * size);

    private static Vector3 EyeInCell(LandblockCellSet set, EnvCellData cell)
    {
        var floor = CellDiscovery.FloorPointInCell(set, cell)!;
        var eye = floor.Position + new Vector3(0, 0, 1.5f);
        Assert.True(set.Contains(cell, eye));
        return eye;
    }

    private static IReadOnlyList<CellPlane> NarrowFrustum(Vector3 eye, Vector3 forward, float halfAngle)
    {
        var side = Vector3.Normalize(Vector3.Cross(forward, MathF.Abs(forward.Z) > 0.9f ? Vector3.UnitX : Vector3.UnitZ));
        var up = Vector3.Cross(side, forward);
        var planes = new List<CellPlane>();
        foreach (var axis in new[] { side, -side, up, -up })
        {
            // Plane through the eye whose normal leans toward the forward direction.
            var normal = Vector3.Normalize((forward * MathF.Sin(halfAngle)) - (axis * MathF.Cos(halfAngle)));
            planes.Add(new CellPlane(normal, -Vector3.Dot(normal, eye)));
        }

        return planes;
    }
}

internal static class CellGeometryMath
{
    /// <summary>Symmetric max nearest-vertex distance between two vertex sets.</summary>
    public static float SetDistance(IReadOnlyList<Vector3> a, IReadOnlyList<Vector3> b) =>
        MathF.Max(a.Max(p => b.Min(q => Vector3.Distance(p, q))), b.Max(p => a.Min(q => Vector3.Distance(p, q))));
}
