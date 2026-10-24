using System.Numerics;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Scenery;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Scenery;

/// <summary>
/// <see cref="SceneryGenerator"/> against the independent ACE port (<see cref="AceSceneryOracle"/>, run with retail's 2^-32
/// scale; ACE's own constant is shown separately to change no decision). Every difference
/// must be one of the documented ACE deviations (docs/third-party/scenery.md):
/// <list type="bullet">
/// <item>ACE's float <c>point_in_poly2D</c> picks the other triangle for points micrometres off a cell diagonal;</item>
/// <item>ACE's float <c>make_plane</c>/<c>set_height</c> move z by under half a millimetre;</item>
/// <item>ACE's <c>AdjustToOutside</c> snaps a coordinate within 0.2 mm of the block's west/south edge to 0 in the
/// placed position itself;</item>
/// <item>ACE's <c>set_heading</c> tilts about the wrong axis (Y instead of X, and adds M13) for the 8 ObjectDescs whose
/// BaseLoc orientation is tilted.</item>
/// </list>
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class SceneryOracleTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>Largest z difference accepted from ACE's float plane math (measured maximum 0.43 mm).</summary>
    private const float PositionTolerance = 5e-4f;

    /// <summary>Two float ulps of relative scale difference (measured maximum 1.19e-7).</summary>
    private const double ScaleTolerance = 2.5e-7;

    /// <summary>Quaternion component tolerance for untilted objects.</summary>
    private const float OrientationTolerance = 1e-5f;

    private sealed record Summary(
        int Landblocks,
        int Candidates,
        int AfterRoad,
        int Placed,
        int[] StageTotals,
        List<string> StageDifferences,
        int PositionDifferences,
        float MaxPositionDifference,
        int TiltedOrientationDifferences,
        int ScaleDifferences,
        int SnappedToZeroByAce);

    [RequiresDatsFact]
    public void Holtburg_region_matches_ACE_and_the_research_counts()
    {
        var summary = Compare(Region(0xA0, 0xB2, 0xAA, 0xBD));
        output.WriteLine(Describe(summary));

        Assert.Equal(380, summary.Landblocks);
        Assert.Empty(summary.StageDifferences);
        Assert.Equal(0, summary.SnappedToZeroByAce);
        Assert.Equal(0, summary.ScaleDifferences);
        Assert.True(summary.MaxPositionDifference <= PositionTolerance);

        // docs/research/foundation-openac.md: 49,157 candidates pass the road and block-edge checks on these 380
        // landblocks; 42,032 of them pass the slope test with the retail split (buildings ignored).
        Assert.Equal(49157, summary.AfterRoad);
        Assert.Equal(42032, SlopePassIgnoringBuildings(Region(0xA0, 0xB2, 0xAA, 0xBD)));

        Assert.Equal(193, summary.StageTotals[(int)SceneryStage.Building]);
        Assert.Equal(7114, summary.StageTotals[(int)SceneryStage.Slope]);
        Assert.Equal(39, summary.StageTotals[(int)SceneryStage.NotWithinBlock]);
        Assert.Equal(0, summary.StageTotals[(int)SceneryStage.ObjectUnavailable]);
        Assert.Equal(41811, summary.Placed);
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_landblock_matches_ACE_apart_from_its_documented_deviations()
    {
        var summary = Compare(Region(0x00, 0xFE, 0x00, 0xFE));
        output.WriteLine(Describe(summary));

        Assert.Equal(65025, summary.Landblocks);
        Assert.Equal(5912342, summary.Candidates);
        Assert.Equal(3786622, summary.Placed);
        Assert.Equal(0, summary.ScaleDifferences);
        Assert.True(summary.MaxPositionDifference <= PositionTolerance);
        Assert.InRange(summary.SnappedToZeroByAce, 0, 10);

        // 25,722 of the 25,727 placements of the 8 tilted ObjectDescs (scenes 0x1200024A, 29D, 2A3, 2B2, 2B3) differ
        // from ACE's set_heading by more than the tolerance; no other orientation does (asserted per placement in
        // Compare). SceneryTiltedOrientationTests checks all of them against the retail set_heading invariants.
        Assert.Equal(25722, summary.TiltedOrientationDifferences);

        // The only decisions that differ: two points 3.8e-6 off a cell diagonal (see TerrainSamplerTests). Exact
        // arithmetic (retail x87) puts them in the steeper triangle, so retail and Lifestone reject them on slope.
        Assert.Equal(
            ["0x7A8B v(5,0) #4 0x02001064: Slope vs ACE Placed", "0x7A8C v(6,0) #4 0x02001064: Slope vs ACE Placed"],
            summary.StageDifferences);
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void ACE_constant_and_retail_two_to_the_minus_32_never_change_a_decision()
    {
        // Same oracle, two scales: ACE's double 2.3283064e-10 and retail's float 2^-32.
        var ace = new AceSceneryOracle(fixture.Dats, useRetailUnitScale: false);
        var retail = new AceSceneryOracle(fixture.Dats, useRetailUnitScale: true);
        var compared = 0;
        var all = Region(0x00, 0xFE, 0x00, 0xFE).ToArray();
        ParallelSweep.For(all.Length, i =>
        {
            var coord = all[i];
            var a = ace.GetLandScenes(coord);
            var r = retail.GetLandScenes(coord);
            Assert.Equal(a.Select(c => (c.VertexX, c.VertexY, c.ObjectIndex, c.Stage)), r.Select(c => (c.VertexX, c.VertexY, c.ObjectIndex, c.Stage)));
            Interlocked.Add(ref compared, a.Count);
        });

        Assert.Equal(5912342, compared);
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

    private static string Describe(Summary s) =>
        $"{s.Landblocks} landblocks, {s.Candidates} candidates, {s.AfterRoad} after road, {s.Placed} placed; stage totals " +
        string.Join(", ", Enum.GetValues<SceneryStage>().Select(st => $"{st} {s.StageTotals[(int)st]}")) +
        $"; stage differences {s.StageDifferences.Count} [{string.Join("; ", s.StageDifferences)}]; positions > 0.1 mm: " +
        $"{s.PositionDifferences} (max {s.MaxPositionDifference * 1000:F3} mm); tilted orientation differences {s.TiltedOrientationDifferences}; " +
        $"scale differences {s.ScaleDifferences}; edge coordinates ACE snapped to 0 {s.SnappedToZeroByAce}";

    private int SlopePassIgnoringBuildings(IEnumerable<LandblockCoord> coords)
    {
        var generator = SceneryGenerator.FromDats(fixture.Dats);
        var passed = 0;
        foreach (var coord in coords)
        {
            var landblock = generator.Data.GetLandblock(coord)!;
            var sampler = new TerrainSampler(landblock.Terrain, generator.Data.Region.LandHeightTable);
            var result = generator.Generate(landblock, includeCandidates: true);
            foreach (var c in result.Candidates!)
            {
                if (c.Stage is SceneryStage.Building or SceneryStage.Slope or SceneryStage.ObjectUnavailable or SceneryStage.NotWithinBlock or SceneryStage.Placed)
                {
                    var obj = generator.Data.GetScene(c.SceneId)!.Objects[c.ObjectIndex];
                    passed += SceneryGenerator.CheckSlope(obj, sampler.GetNormal(c.Position.X, c.Position.Y).Z) ? 1 : 0;
                }
            }
        }

        return passed;
    }

    /// <summary>
    /// C2 (q5): one landblock per work item, in parallel; the generator and the oracle are thread-safe (concurrent
    /// caches, dat reads under DatSet's lock), and the per-landblock summaries merge in coordinate order so the stage
    /// differences list is the sequential run's.
    /// </summary>
    private Summary Compare(IEnumerable<LandblockCoord> coordinates)
    {
        var coords = coordinates.ToArray();
        var generator = SceneryGenerator.FromDats(fixture.Dats);
        // ACE with its 2.3283064e-10 replaced by retail's 2^-32, so displaced x/y are bit-identical; the separate
        // test below shows the constant alone never changes a decision.
        var oracle = new AceSceneryOracle(fixture.Dats, useRetailUnitScale: true);
        var parts = new Summary[coords.Length];
        ParallelSweep.For(coords.Length, i => parts[i] = CompareOne(generator, oracle, coords[i]));

        var totals = new int[Enum.GetValues<SceneryStage>().Length];
        var stageDifferences = new List<string>();
        int landblocks = 0, candidates = 0, afterRoad = 0, placed = 0, positionDifferences = 0, tilted = 0, scaleDifferences = 0, snapped = 0;
        var maxPosition = 0f;
        foreach (var part in parts)
        {
            landblocks += part.Landblocks;
            candidates += part.Candidates;
            afterRoad += part.AfterRoad;
            placed += part.Placed;
            for (var s = 0; s < totals.Length; s++)
            {
                totals[s] += part.StageTotals[s];
            }

            stageDifferences.AddRange(part.StageDifferences);
            positionDifferences += part.PositionDifferences;
            maxPosition = Math.Max(maxPosition, part.MaxPositionDifference);
            tilted += part.TiltedOrientationDifferences;
            scaleDifferences += part.ScaleDifferences;
            snapped += part.SnappedToZeroByAce;
        }

        return new Summary(landblocks, candidates, afterRoad, placed, totals, stageDifferences, positionDifferences, maxPosition, tilted, scaleDifferences, snapped);
    }

    private static Summary CompareOne(SceneryGenerator generator, AceSceneryOracle oracle, LandblockCoord coord)
    {
        var totals = new int[Enum.GetValues<SceneryStage>().Length];
        var stageDifferences = new List<string>();
        int landblocks = 0, candidates = 0, afterRoad = 0, placed = 0, positionDifferences = 0, tilted = 0, scaleDifferences = 0, snapped = 0;
        var maxPosition = 0f;

        {
            landblocks++;
            var result = generator.Generate(coord, includeCandidates: true);
            foreach (var stage in Enum.GetValues<SceneryStage>())
            {
                totals[(int)stage] += result.Funnel[stage];
            }

            afterRoad += result.Funnel.AfterRoad;
            var mine = result.Candidates!.Where(c => c.Stage is not (SceneryStage.FrequencyRejected or SceneryStage.Weenie)).ToList();
            var theirs = oracle.GetLandScenes(coord);
            candidates += mine.Count;

            // Same candidates (frequency, weenie, scene pick) in the same order.
            Assert.Equal(theirs.Select(c => (c.VertexX, c.VertexY, c.SceneId, c.ObjectIndex, c.ObjectId)), mine.Select(c => (c.VertexX, c.VertexY, c.SceneId, c.ObjectIndex, c.ObjectId)));

            var placements = result.Placements.GetEnumerator();
            for (var i = 0; i < mine.Count; i++)
            {
                var m = mine[i];
                var a = theirs[i];
                if (m.Stage == SceneryStage.Placed)
                {
                    Assert.True(placements.MoveNext());
                }

                if (m.Stage != a.Stage)
                {
                    stageDifferences.Add($"{coord} v({m.VertexX},{m.VertexY}) #{m.ObjectIndex} 0x{m.ObjectId:X8}: {m.Stage} vs ACE {a.Stage}");
                    continue;
                }

                if (m.Stage != SceneryStage.Placed)
                {
                    continue;
                }

                placed++;
                var p = placements.Current;
                var d = Math.Abs(p.Origin.Z - a.Origin.Z);
                maxPosition = Math.Max(maxPosition, d);
                positionDifferences += d > 1e-4f ? 1 : 0;
                // x/y are bit-identical, except that ACE's AdjustToOutside writes its |v| < 0.0002 -> 0 snap back into
                // the placed position; retail snaps only a copy (Position::get_outside_cell_id) and keeps obj_vector.
                if (p.Origin.X != a.Origin.X || p.Origin.Y != a.Origin.Y)
                {
                    Assert.True(SnappedByAce(p.Origin.X, a.Origin.X) && SnappedByAce(p.Origin.Y, a.Origin.Y), $"{coord} {p}: x/y differ from ACE {a.Origin}");
                    snapped++;
                }

                var obj = generator.Data.GetScene(p.SceneId)!.Objects[p.ObjectIndex];
                var isTilted = obj.BaseOrientation.X != 0f || obj.BaseOrientation.Y != 0f;
                var q = a.Orientation;
                if (Quaternion.Dot(p.Orientation, q) < 0)
                {
                    q = -q;
                }

                var orientationDiffers = (Quaternion.Normalize(p.Orientation) - Quaternion.Normalize(q)).Length() > OrientationTolerance;
                if (isTilted)
                {
                    tilted += orientationDiffers ? 1 : 0;
                }
                else
                {
                    Assert.False(orientationDiffers, $"{coord} {p}: orientation differs from ACE {a.Orientation}");
                }

                scaleDifferences += Math.Abs(p.Scale - a.Scale) / p.Scale > ScaleTolerance ? 1 : 0;
            }

            Assert.False(placements.MoveNext());
        }

        return new Summary(landblocks, candidates, afterRoad, placed, totals, stageDifferences, positionDifferences, maxPosition, tilted, scaleDifferences, snapped);

        static bool SnappedByAce(float mine, float ace) => mine == ace || (ace == 0f && Math.Abs(mine) < 0.0002f);
    }
}
