using System.Numerics;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Scenery;
using Lifestone.Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 AM, the real case of gate A's stretch 3 on the retail terrain: 11 bolts of 20 stopped short outdoors in landblock 0xAAB3 and its neighbours, each at a target 50 to 70 m away. The first (13:37:49 UTC) left (3.8, 171.4) at bolt
/// height 101.99 with velocity (9.58, -11.29, 2.39) m/s, so the Mite Snippet stood 68.8 m along it, and the bolt stopped 2.95 s (44 m) out. The line test the outdoor walk now holds reads the ground's own triangles between the two.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class OutdoorLineDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void The_live_case_is_blocked_on_the_real_terrain_where_the_line_grazes_the_hill()
    {
        var dats = fixture.Dats;
        var samplers = new Dictionary<uint, TerrainSampler>();
        float? Terrain(uint cell, float x, float y)
        {
            if (!samplers.TryGetValue(cell >> 16, out var sampler))
            {
                var t = dats.GetLandblockTerrain(LandblockCoord.FromId(cell));
                if (t is null)
                {
                    return null;
                }

                samplers[cell >> 16] = sampler = new TerrainSampler(t, dats.TerrainRegion.LandHeightTable);
            }

            return x is < 0 or >= 192 || y is < 0 or >= 192 ? null : sampler.GetHeight(x, y);
        }

        var from = new CellFrame(0xAAB30008, new AcFrame(new Vector3(3.8f, 171.4f, 100.8f), Quaternion.Identity));
        var to = new CellFrame(0xAAB30000 | 11, new AcFrame(new Vector3(47.7f, 119.6f, 111.7f), Quaternion.Identity));
        var ground = new[] { 0f, 10f, 20f, 30f, 40f, 44f, 50f, 60f, 66f }.Select(m => (m, z: Terrain(0xAAB30001, 3.8f + (9.58f / 15f * m), 171.4f - (11.29f / 15f * m)))).ToArray();
        output.WriteLine("metres along the bolt: terrain height | bolt height (feet + 1.2, rising 2.39 per 15 m flown)");
        foreach (var (m, z) in ground)
        {
            output.WriteLine($"{m,5:0}: {z:0.0} | {101.99f + (2.385f / 15f * m):0.0}");
        }

        var clear = OutdoorFightWalk.ClearOverTerrain(Terrain, in from, in to);
        output.WriteLine($"ClearOverTerrain: {clear}");
        Assert.NotNull(ground[0].z);
        Assert.False(clear, "the line passes 0.4 m over the hill at 30 m: closer than the bolt's sphere and the sampling");
        Assert.True(OutdoorFightWalk.ClearOverTerrain(Terrain, in from, in from)); // the same spot is never a wall
    }
}
