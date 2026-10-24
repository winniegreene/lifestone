using System.Numerics;
using Lifestone.Data.Models;

namespace Lifestone.Data.Tests.Models;

/// <summary>
/// LOD tables and retail <c>GfxObjDegradeInfo::get_degrade</c> selection. Table ids and level GfxObj ids were read
/// independently with the python research reader (session scratch <c>models/dump_degrade.py</c>); switch distances are
/// re-read from the user's dats at test time by the independent <see cref="RawModelReader"/>.
/// </summary>
public sealed class DegradeTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Holtburg_building_chain_and_switch_distances()
    {
        var chain = LodChain.Load(fixture.Dats, 0x01000C1E);
        Assert.True(chain.IsLoadable);
        Assert.NotNull(chain.Table);
        Assert.Equal(0x110004EEu, chain.Table.Id);
        Assert.Equal(new uint[] { 0x01000C1E, 0x01000C26, 0x01000C25, 0 }, chain.GfxObjIds);
        Assert.All(chain.Table.Levels, l => Assert.Equal(DegradeMode.Normal, l.Mode));
        Assert.True(chain.Table.Levels[3].DrawsNothing);
        Assert.Equal(float.MaxValue, chain.Table.Levels[3].MaxDistance);

        var levels = RawDegrade(0x110004EE).Levels;
        Assert.Equal(
            levels.Select(l => (l.Min, l.Ideal, l.Max)),
            chain.Table.Levels.Select(l => (l.MinDistance, l.IdealDistance, l.MaxDistance)));
        Assert.Equal(levels[2].Max, chain.MaxDegradeDistance); // the level before the empty one
        Assert.Equal((0, 0x01000C1Eu, DegradeMode.Normal), chain.Select(0f));

        // Each bias switches level i at its own distance + 50 m (s_rDegradeDistance): the steady-state bias 1 at
        // max_dist, bias 0 (retail startup) at ideal_dist, bias -1 (worst framerate) at min_dist. Past the last real
        // level nothing is drawn.
        var columns = new (float Bias, Func<(uint GfxObj, uint Mode, float Min, float Ideal, float Max), float> Distance)[]
        {
            (1f, l => l.Max), (0f, l => l.Ideal), (-1f, l => l.Min),
        };
        foreach (var (bias, distance) in columns)
        {
            for (var i = 0; i < 3; i++)
            {
                var at = distance(levels[i]) + DegradeSelector.DefaultDegradeDistance;
                Assert.Equal(i, chain.Select(at - 0.1f, bias).Level);
                Assert.Equal(i + 1, chain.Select(at, bias).Level);
            }
        }

        Assert.Equal((3, 0u, DegradeMode.Normal), chain.Select(levels[2].Max + DegradeSelector.DefaultDegradeDistance));

        // A user Degrade Distance of 0 removes the 50 m offset.
        Assert.Equal(1, chain.Select(levels[0].Max, degradeDistance: 0f).Level);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Tree_canopy_chain_has_three_levels_then_nothing()
    {
        var chain = LodChain.Load(fixture.Dats, 0x010037A1);
        Assert.Equal(0x1100001Eu, chain.Table!.Id);
        Assert.Equal(new uint[] { 0x010037A1, 0x010037A9, 0x01003AF0, 0 }, chain.GfxObjIds);
        var lastReal = RawDegrade(0x1100001E).Levels[2].Max;
        Assert.Equal(lastReal, chain.MaxDegradeDistance);
        Assert.Equal(0u, chain.Select(lastReal + DegradeSelector.DefaultDegradeDistance).GfxObjId);
        Assert.Equal(0x01003AF0u, chain.Select(lastReal + DegradeSelector.DefaultDegradeDistance - 1f).GfxObjId);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Anchor_gfxobj_is_hidden_by_its_degrade_table()
    {
        // 0x1100002E: level 0 is the anchor itself with min = ideal = max = 0, level 1 is nothing.
        var chain = LodChain.Load(fixture.Dats, 0x010001EC);
        Assert.True(chain.IsLoadable);
        Assert.Equal(new uint[] { 0x010001EC, 0 }, chain.GfxObjIds);
        Assert.Equal(0f, chain.MaxDegradeDistance);
        foreach (var bias in new[] { -1f, 0f, 1f })
        {
            foreach (var distance in new[] { 0f, 0.001f, 10f, 1000f })
            {
                Assert.Equal(0u, chain.Select(distance, bias).GfxObjId);
            }
        }
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Human_head_draws_a_more_detailed_gfxobj_up_close_and_a_z_billboard_far_away()
    {
        var chain = LodChain.Load(fixture.Dats, 0x0100004E);
        Assert.Equal(0x110006C6u, chain.Table!.Id);
        Assert.Equal(0x01001787u, chain.NearestGfxObjId);
        Assert.Equal(new uint[] { 0x01001787, 0x0100004E, 0x010001A2, 0x010001A0, 0x010001F1, 0 }, chain.GfxObjIds);
        Assert.Equal(DegradeMode.AxisAlignedZ, chain.Table.Levels[4].Mode);
        Assert.False(chain.Table.Always2D);
        var levels = RawDegrade(0x110006C6).Levels;
        Assert.Equal(levels[4].Max, chain.MaxDegradeDistance);
        Assert.Equal((4, 0x010001F1u, DegradeMode.AxisAlignedZ), chain.Select(levels[3].Max + DegradeSelector.DefaultDegradeDistance));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Gfxobj_without_degrade_or_missing_gfxobj()
    {
        var plain = LodChain.Load(fixture.Dats, 0x010008AA);
        Assert.Null(plain.Table);
        Assert.True(plain.IsLoadable);
        Assert.Equal(new uint[] { 0x010008AA }, plain.GfxObjIds);
        Assert.Equal(100f, plain.MaxDegradeDistance);
        Assert.Equal((0, 0x010008AAu, DegradeMode.Normal), plain.Select(5000f));

        Assert.False(LodChain.Load(fixture.Dats, 0x01004E29).IsLoadable);
        Assert.False(LodChain.Load(fixture.Dats, 0).IsLoadable);
    }

    private RawModelReader.RawDegrade RawDegrade(uint id) => RawModelReader.ReadDegrade(RawModelReader.PortalBytes(fixture.Dats, id));

    [Fact]
    public void Selection_follows_retail_get_degrade()
    {
        var table = new DegradeTable(0x11000001,
        [
            new DegradeLevel(0x01000001, DegradeMode.Normal, 10f, 20f, 40f),
            new DegradeLevel(0x01000002, DegradeMode.Billboard, 30f, 60f, 100f),
            new DegradeLevel(0, DegradeMode.Normal, float.MaxValue, float.MaxValue, float.MaxValue),
        ]);

        // d = max(0, |distance| - degradeDistance).
        Assert.Equal(0, DegradeSelector.SelectLevel(table, -15f, bias: 0f, degradeDistance: 0f));
        Assert.Equal(1, DegradeSelector.SelectLevel(table, -45f, bias: 0f, degradeDistance: 0f));
        Assert.Equal(0, DegradeSelector.SelectLevel(table, 69f, bias: 0f, degradeDistance: 50f));

        // bias 0.5: thresholds 30 and 80.
        Assert.Equal(0, DegradeSelector.SelectLevel(table, 29.9f, 0.5f, 0f));
        Assert.Equal(1, DegradeSelector.SelectLevel(table, 30f, 0.5f, 0f));
        Assert.Equal(2, DegradeSelector.SelectLevel(table, 80f, 0.5f, 0f));

        // bias -0.5: thresholds 20 - 5 = 15 and 60 - 15 = 45.
        Assert.Equal(0, DegradeSelector.SelectLevel(table, 14.9f, -0.5f, 0f));
        Assert.Equal(1, DegradeSelector.SelectLevel(table, 15f, -0.5f, 0f));
        Assert.Equal(2, DegradeSelector.SelectLevel(table, 45f, -0.5f, 0f));

        Assert.Equal(40f, new DegradeTable(1, [table.Levels[0], table.Levels[2]]).MaxDegradeDistance);
        Assert.Equal(100f, table.MaxDegradeDistance);
        Assert.False(table.Always2D);
        Assert.True(new DegradeTable(2, [table.Levels[1]]).Always2D);
        Assert.Throws<InvalidDataException>(() => new DegradeTable(3, []));
    }

    [Fact]
    public void Object_and_particle_degrade_distances_follow_SetDegradeLevelInternal()
    {
        Assert.Equal(50f, DegradeSelector.ObjectDegradeDistance(1f));
        Assert.Equal(25f, DegradeSelector.ObjectDegradeDistance(0f));
        Assert.Equal(8f, DegradeSelector.ObjectDegradeDistance(-1f));
        Assert.Equal(25f, DegradeSelector.ParticleDegradeDistance(1f));
        Assert.Equal(16f, DegradeSelector.ParticleDegradeDistance(0f));
        Assert.Equal(8f, DegradeSelector.ParticleDegradeDistance(-1f));
    }

    [Fact]
    public void Part_distance_is_measured_to_the_scaled_sort_center_and_divided_by_scale_z()
    {
        var frame = new PartFrame(new Vector3(10f, 0f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f));
        var (viewerDistance, lodDistance) = DegradeSelector.PartDistances(Vector3.Zero, frame, new Vector3(2f, 2f, 4f), new Vector3(1f, 0f, 0f));
        Assert.Equal(MathF.Sqrt(104f), viewerDistance, 1e-4f);
        Assert.Equal(MathF.Sqrt(104f) / 4f, lodDistance, 1e-4f);
    }
}
