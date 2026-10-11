using System.Globalization;
using System.Numerics;
using Lifestone.Client.Effects;
using Lifestone.Data.Models;
using Lifestone.Data.Tests;
using Lifestone.Data.World.Scenery;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Effects;

/// <summary>
/// The spell viewer's stage in Holtburg must be clear of the town: the caster, the drudge and the straight line the
/// bolts fly along keep <see cref="StageSite.MinimumClearance"/> metres of horizontal clearance from every drawn building
/// and static below <see cref="StageSite.ObstacleHeight"/> above the ground (review finding: the first placement put the
/// drudge inside the timbered house's south wall and the bolts flew along it).
/// </summary>
public sealed class StageSiteTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [Fact]
    public void Segment_and_polygon_distances()
    {
        Vector2[] square = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
        Assert.Equal(0f, StaticClearance.SegmentPolygonDistance(new Vector2(1, 1), new Vector2(1, 1), square)); // point inside
        Assert.Equal(0f, StaticClearance.SegmentPolygonDistance(new Vector2(-1, 1), new Vector2(3, 1), square)); // crossing
        Assert.Equal(1f, StaticClearance.SegmentPolygonDistance(new Vector2(-1, 3), new Vector2(3, 3), square), 5);
        Assert.Equal(MathF.Sqrt(2f), StaticClearance.SegmentPolygonDistance(new Vector2(3, 3), new Vector2(3, 3), square), 5);

        // An edge-on (vertical wall) triangle projects to a segment: the distance is to that segment.
        Vector2[] wall = [new(0, 5), new(4, 5), new(4, 5)];
        Assert.Equal(0f, StaticClearance.SegmentPolygonDistance(new Vector2(2, 5), new Vector2(2, 5), wall));
        Assert.Equal(1f, StaticClearance.SegmentPolygonDistance(new Vector2(-3, 4), new Vector2(7, 4), wall), 5);
        Assert.Equal(0f, StaticClearance.SegmentSegmentDistance(new Vector2(0, 0), new Vector2(2, 2), new Vector2(0, 2), new Vector2(2, 0)));
    }

    [RequiresDatsFact]
    public void The_stage_is_clear_of_Holtburgs_buildings_and_statics()
    {
        var dats = fixture.Dats;
        var meshes = new GfxObjMeshCache(dats);
        var clearance = StaticClearance.Load(dats, StageSite.Landblock, meshes);
        var terrain = dats.GetLandblockTerrain(StageSite.Landblock) ?? throw new InvalidOperationException("Holtburg has no terrain");
        var ground = new TerrainSampler(terrain, dats.TerrainRegion.LandHeightTable);
        Assert.NotEmpty(clearance.Triangles);

        var caster = StageSite.CasterStand;
        var target = StageSite.TargetStand;
        Assert.Equal(StageSite.Distance, Vector2.Distance(caster, target), 3);

        // The ground band along the cast line.
        var heights = Enumerable.Range(0, 25).Select(i => ground.GetHeight(Vector2.Lerp(caster, target, i / 24f).X, Vector2.Lerp(caster, target, i / 24f).Y)).ToArray();
        var zMin = heights.Min() - 0.5f;
        var zMax = heights.Max() + StageSite.ObstacleHeight;

        var checks = new (string Name, Vector2 A, Vector2 B)[]
        {
            ("caster", caster, caster),
            ("target", target, target),
            ("cast line", caster, target),
        };
        foreach (var (name, a, b) in checks)
        {
            var nearest = clearance.Nearest(a, b, zMin, zMax);
            output.WriteLine(Describe(name, nearest));
            Assert.True(nearest.Distance >= StageSite.MinimumClearance, Describe(name, nearest));
        }

        // Evidence for the review finding: the first placement (y = 25) ran along the timbered house's south wall.
        var old = clearance.Nearest(new Vector2(93, 25), new Vector2(105, 25), zMin, zMax);
        output.WriteLine(Describe("first placement's cast line (y = 25)", old));
        Assert.True(old.Distance < 0.01f);
        Assert.Equal(0x01000C17u, old.Triangle!.Value.ModelId);

        // Procedural scenery is not drawn by the viewer yet (objects track), but must not stand on the stage either: none
        // within the clearance plus a generous crown radius (3 m) of the cast line (none within 15 m in the EoR dats).
        var scenery = SceneryGenerator.FromDats(dats).Generate(StageSite.Landblock);
        foreach (var p in scenery.Placements)
        {
            var d = StaticClearance.PointSegmentDistance(new Vector2(p.Origin.X, p.Origin.Y), caster, target);
            Assert.True(d >= StageSite.MinimumClearance + 3f, string.Create(CultureInfo.InvariantCulture, $"scenery 0x{p.ObjectId:X8} at ({p.Origin.X:0.0}, {p.Origin.Y:0.0}) is {d:0.0} m from the cast line"));
        }
    }

    private static string Describe(string name, ClearanceResult r) =>
        r.Triangle is { } t
            ? string.Create(CultureInfo.InvariantCulture,
                $"{name}: {r.Distance:0.00} m from model 0x{t.ModelId:X8} (part 0x{t.GfxObjId:X8}) at ({t.ModelOrigin.X:0.0}, {t.ModelOrigin.Y:0.0}), triangle z {MathF.Min(t.A.Z, MathF.Min(t.B.Z, t.C.Z)):0.0}–{MathF.Max(t.A.Z, MathF.Max(t.B.Z, t.C.Z)):0.0}")
            : $"{name}: nothing in the height band";
}
