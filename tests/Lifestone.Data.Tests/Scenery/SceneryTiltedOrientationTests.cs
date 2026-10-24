using System.Numerics;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Scenery;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Scenery;

/// <summary>
/// Placements of the ObjectDescs whose BaseLoc orientation is tilted (X or Y component non-zero). This is the one path
/// where Lifestone deliberately departs from ACE (ACE's <c>set_heading</c> tilts about the wrong axis, see
/// docs/third-party/scenery.md), so the ACE oracle cannot check it. Instead every such placement is checked against the
/// invariants of retail <c>Frame::set_heading</c>, with expected values computed here from the dats, independently of
/// <see cref="RetailFrame"/> and <see cref="SceneryRandom"/>:
/// <list type="bullet">
/// <item>set_heading normalises (sin d, cos d, m5), m5 being the z of the base orientation's forward (+Y) axis, so the
/// placed forward axis has z = m5 / sqrt(1 + m5^2);</item>
/// <item>the forward axis has compass heading d: the rotation noise times MaxRotation (ObjectDesc::GetObjFrame) or,
/// for Align, the heading of the downhill terrain normal (ObjectDesc::ObjAlign);</item>
/// <item>it pitches about local X only (Euler order 0, no roll), so local +X stays horizontal;</item>
/// <item>without Align or MaxRotation the stored BaseLoc quaternion is used unchanged.</item>
/// </list>
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class SceneryTiltedOrientationTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>Retail's double pi / 180.</summary>
    private const double DegToRad = Math.PI / 180.0;

    private sealed record Counts(int Placements, int Rotated, int Aligned, int Unchanged)
    {
        public int Tilted => Rotated + Aligned + Unchanged;
    }

    [RequiresDatsFact]
    public void Set_heading_keeps_the_pitch_of_a_tilted_BaseLoc_read_from_the_dats()
    {
        // Scene 0x1200024A, ObjectDesc 0 has a BaseLoc tilted by about a degree.
        Assert.True(fixture.Dats.TryGetPortal<Scene>(0x1200024Au, out var scene));
        var tilted = scene.Objects[0].BaseLoc.Orientation;
        Assert.True(tilted.X != 0f || tilted.Y != 0f);

        var frame = RetailFrame.FromStored(Vector3.Zero, tilted);
        frame.SetHeading(30f);

        var forward = Vector3.Transform(Vector3.UnitY, frame.Orientation);
        Assert.Equal(PitchedForwardZ(tilted), forward.Z, 6);
        Assert.Equal(30.0, CompassHeading(forward), 4);                                  // heading replaced
        Assert.Equal(0f, Vector3.Transform(Vector3.UnitX, frame.Orientation).Z, 6);     // no roll: X stays horizontal
    }

    [RequiresDatsFact]
    public void Tilted_placements_in_a_sample_region_follow_retail_set_heading()
    {
        var counts = Check(Region(0x0D, 0x11, 0xB5, 0xC0));
        output.WriteLine(counts.ToString());
        Assert.Equal(7058, counts.Placements);
        Assert.Equal(354, counts.Tilted);

        // At end of retail every tilted ObjectDesc has MaxRotation > 0 and no Align.
        Assert.Equal(counts.Tilted, counts.Rotated);
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_tilted_placement_follows_retail_set_heading()
    {
        var counts = Check(Region(0x00, 0xFE, 0x00, 0xFE));
        output.WriteLine(counts.ToString());
        Assert.Equal(3786622, counts.Placements);
        Assert.Equal(25727, counts.Tilted);
        Assert.Equal(counts.Tilted, counts.Rotated);
    }

    private static IEnumerable<LandblockCoord> Region(int x0, int x1, int y0, int y1)
    {
        for (var x = x0; x <= x1; x++)
        {
            for (var y = y0; y <= y1; y++)
            {
                yield return new LandblockCoord((byte)x, (byte)y);
            }
        }
    }

    /// <summary>Retail <c>Frame::cache</c> m[5] (z of the forward axis) of the stored quaternion, then set_heading's pitch.</summary>
    private static double PitchedForwardZ(Quaternion q)
    {
        var m5 = (float)((2.0 * q.Y * q.Z) + (2.0 * q.W * q.X));
        return m5 / Math.Sqrt(1.0 + ((double)m5 * m5));
    }

    /// <summary>Compass heading in degrees (0 = +Y, clockwise) of the horizontal part of <paramref name="v"/>.</summary>
    private static double CompassHeading(Vector3 v)
    {
        var degrees = Math.Atan2(v.X, v.Y) / DegToRad;
        return degrees < 0 ? degrees + 360.0 : degrees;
    }

    /// <summary>Signed difference of two headings, wrapped to (-180, 180].</summary>
    private static double HeadingDifference(double a, double b)
    {
        var d = (a - b) % 360.0;
        return d > 180.0 ? d - 360.0 : d <= -180.0 ? d + 360.0 : d;
    }

    /// <summary>
    /// The heading <c>ObjectDesc::GetObjFrame</c> passes to set_heading: the per-object hash with salt 63127, scaled by
    /// 2^-32 and MaxRotation, stored as float.
    /// </summary>
    private static float RandomHeading(uint gx, uint gy, uint k, float maxRotation)
    {
        unchecked
        {
            var hash = (1813693831u * gy) - ((k + 63127u) * ((1360117743u * gy * gx) + 1888038839u)) - (1109124029u * gx);
            return (float)(hash / 4294967296.0 * maxRotation);
        }
    }

    private Counts Check(IEnumerable<LandblockCoord> coords)
    {
        var dats = fixture.Dats;
        var generator = SceneryGenerator.FromDats(dats);
        var scenes = new Dictionary<uint, Scene>();
        int placements = 0, rotated = 0, aligned = 0, unchanged = 0;

        foreach (var coord in coords)
        {
            var result = generator.Generate(coord);
            TerrainSampler? sampler = null;
            foreach (var p in result.Placements)
            {
                placements++;
                if (!scenes.TryGetValue(p.SceneId, out var scene))
                {
                    Assert.True(dats.TryGetPortal(p.SceneId, out scene));
                    scenes[p.SceneId] = scene;
                }

                var obj = scene.Objects[p.ObjectIndex];
                var baseOrientation = obj.BaseLoc.Orientation;
                if (baseOrientation.X == 0f && baseOrientation.Y == 0f)
                {
                    continue;
                }

                var where = $"{coord} v({p.VertexX},{p.VertexY}) scene 0x{p.SceneId:X8} #{p.ObjectIndex}";
                double expectedHeading;
                if (obj.Align != 0)
                {
                    // ObjAlign: face down the slope, i.e. the heading of the negated terrain normal (0 when it is level).
                    sampler ??= new TerrainSampler(dats.GetLandblockTerrain(coord)!, dats.TerrainRegion.LandHeightTable);
                    var normal = sampler.GetNormal(p.Origin.X, p.Origin.Y);
                    var level = Math.Sqrt(((double)normal.X * normal.X) + ((double)normal.Y * normal.Y)) < 0.0002;
                    expectedHeading = level ? 0.0 : CompassHeading(-normal);
                    aligned++;
                }
                else if (obj.MaxRotation > 0f)
                {
                    var gx = (uint)(coord.GlobalCellX + p.VertexX);
                    var gy = (uint)(coord.GlobalCellY + p.VertexY);
                    expectedHeading = RandomHeading(gx, gy, (uint)p.ObjectIndex, obj.MaxRotation);
                    rotated++;
                }
                else
                {
                    Assert.Equal(baseOrientation, p.Orientation);
                    unchanged++;
                    continue;
                }

                var orientation = Quaternion.Normalize(p.Orientation);
                var forward = Vector3.Transform(Vector3.UnitY, orientation);
                Assert.True(Math.Abs(forward.Z - PitchedForwardZ(baseOrientation)) < 2e-6, $"{where}: forward z {forward.Z} vs {PitchedForwardZ(baseOrientation)}");
                Assert.True(Math.Abs(HeadingDifference(CompassHeading(forward), expectedHeading)) < 2e-4, $"{where}: heading {CompassHeading(forward)} vs {expectedHeading}");
                Assert.True(Math.Abs(Vector3.Transform(Vector3.UnitX, orientation).Z) < 2e-6, $"{where}: local +X is not horizontal");
            }
        }

        return new Counts(placements, rotated, aligned, unchanged);
    }
}
